/*
[ 코드 설명 ]

GameServer

1. TCP Socket 서버를 생성하고 여러 클라이언트의 접속을 처리한다.
2. 클라이언트별 ID와 연결 목록을 관리한다.
3. 개인 전송, 지정한 여러 명에게 전송, 테스트용 전체 전송을 제공한다.
4. 통신 오류와 접속 종료를 처리하고 서버 종료 시 작업을 정리한다.
*/

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GameServerProject.Networking;

public class GameServer
{
    // 여러 수신 작업이 동시에 목록을 사용하므로 동시 접근을 지원하는 자료구조를 쓴다.
    private readonly ConcurrentDictionary<int, TcpConnection> clients = new();
    // 재접속에도 이전 ID를 재사용하지 않고, 같은 인스턴스의 중복 실행을 막는다.
    private int nextClientId;
    private int running;
    public const int MaxClients = 100;

    // B 담당자는 현재 ID 목록과 전송 API를 사용해 방별 수신자를 선택할 수 있다.
    public int[] ClientIds => clients.Keys.ToArray();

    // HELLO와 BROADCAST 이외의 명령 처리 지점이다. 방 기능 자체는 구현하지 않는다.
    public Func<int, string, CancellationToken, Task>? MessageHandler { get; set; }
    public event Action<int>? ClientDisconnected;

    public async Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref running, 1) != 0)
        {
            throw new InvalidOperationException("이미 실행 중인 서버입니다.");
        }

        // 접속 수락 오류가 나도 서버 내부에서 기존 연결 작업을 취소할 수 있게 한다.
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var clientTasks = new List<Task>();

        try
        {
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, port));
            serverSocket.Listen(100); // 접속 대기 큐 크기이며, 접속자 제한과는 다르다.
            Console.WriteLine($"[START] TCP 서버 0.0.0.0:{port} / 종료: Ctrl+C");

            while (!stopping.IsCancellationRequested)
            {
                // 접속 수락과 클라이언트별 수신을 분리하여 기존 통신이 새 접속을 막지 않는다.
                Socket socket = await serverSocket.AcceptAsync(stopping.Token);
                clientTasks.RemoveAll(task => task.IsCompleted);
                // 접속 안내를 아직 전송 중인 작업도 제한에 포함한다.
                if (clientTasks.Count >= MaxClients)
                {
                    Console.WriteLine($"[REJECT] 최대 접속자 {MaxClients}명 초과");
                    socket.Dispose();
                    continue;
                }

                int clientId = Interlocked.Increment(ref nextClientId);
                var connection = new TcpConnection(socket);
                // 비동기 소켓은 대기 중 스레드를 점유하지 않으므로 Task.Run이 필요하지 않다.
                // 완료된 작업을 제거하여 장시간 재접속해도 작업 목록이 계속 커지지 않게 한다.
                clientTasks.Add(HandleClientAsync(clientId, connection, stopping.Token));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Ctrl+C 등으로 접속 대기가 취소된 것은 정상적인 서버 종료이다.
        }
        finally
        {
            stopping.Cancel();
            serverSocket.Dispose();
            foreach (TcpConnection connection in clients.Values)
            {
                connection.Dispose();
            }

            // 수신 작업의 finally와 B 담당자의 접속 종료 처리까지 끝난 뒤 종료한다.
            await Task.WhenAll(clientTasks);
            Volatile.Write(ref running, 0);
            Console.WriteLine("[STOP] 모든 클라이언트 연결 정리 완료");
        }
    }

    // 대상이 사라졌거나 통신에 실패하면 false를 반환하고 해당 연결만 닫는다.
    // 잘못된 메시지 등 호출자 코드의 오류는 숨기지 않고 예외로 전달한다.
    public async Task<bool> SendToClientAsync(
        int clientId, string message, CancellationToken cancellationToken = default)
    {
        if (!clients.TryGetValue(clientId, out TcpConnection? connection))
        {
            return false;
        }

        try
        {
            await connection.SendAsync(message, cancellationToken);
            return true;
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException ||
                                 e is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"[SEND ERROR] Client {clientId} : {e.Message}");
            connection.Dispose();
            return false;
        }
    }

    // 방 기능에서는 수신자 ID 목록만 넘기면 된다. 같은 ID는 한 번만 전송한다.
    // 병렬 송신으로 느린 대상이 다른 대상의 전송 시작을 막지 않게 한다.
    public Task SendToClientsAsync(
        IEnumerable<int> clientIds, string message, CancellationToken cancellationToken = default)
    {
        int[] recipients = clientIds.Distinct().ToArray();
        return Task.WhenAll(recipients.Select(id => SendToClientAsync(id, message, cancellationToken)));
    }

    private async Task HandleClientAsync(int clientId, TcpConnection connection, CancellationToken cancellationToken)
    {
        // 수신 데이터가 이미 도착해 비동기 호출이 즉시 끝나더라도 이 작업을 먼저 분리한다.
        // 접속 수락 루프는 특정 클라이언트의 메시지 처리에 붙잡히지 않고 계속 진행한다.
        await Task.Yield();
        try
        {
            await connection.SendAsync($"SERVER|CONNECTED|{clientId}", cancellationToken);
            // 안내를 먼저 보낸 뒤 공개해야 접속 직후 방송이 ID 안내보다 앞서 도착하지 않는다.
            if (!clients.TryAdd(clientId, connection))
            {
                throw new InvalidOperationException("클라이언트 ID가 중복되었습니다.");
            }
            Console.WriteLine($"[CONNECT] Client {clientId}");
            while (!cancellationToken.IsCancellationRequested)
            {
                string? message = await connection.ReceiveAsync(cancellationToken);
                if (message is null) // 상대가 정상적으로 송신을 종료한 경우이다.
                {
                    break;
                }
                if (Encoding.UTF8.GetByteCount(message) > TcpConnection.MaxClientMessageBytes)
                {
                    throw new InvalidDataException("클라이언트 메시지 본문은 최대 4096바이트입니다.");
                }

                Console.WriteLine($"[RECV] Client {clientId} : {message}");
                if (message.StartsWith("BROADCAST|", StringComparison.Ordinal))
                {
                    string body = message["BROADCAST|".Length..];
                    await SendToClientsAsync(ClientIds, $"SERVER|BROADCAST|{clientId}|{body}", cancellationToken);
                }
                else if (message == "HELLO" || MessageHandler is null)
                {
                    // 기존 HELLO 응답과 일반 문자열 확인 응답을 유지한다.
                    await connection.SendAsync($"SERVER|RECEIVED|{message}", cancellationToken);
                }
                else
                {
                    await MessageHandler(clientId, message, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 서버 종료 요청으로 진행 중인 송수신을 중단했다.
        }
        catch (Exception e)
        {
            // 클라이언트 작업의 최상위 경계이다. 확장 코드의 예외도 반드시 기록하고
            // 해당 클라이언트만 종료하여 다른 접속과 접속 수락 작업을 보호한다.
            Console.WriteLine($"[CLIENT ERROR] Client {clientId} : {e.GetType().Name} : {e.Message}");
        }
        finally
        {
            clients.TryRemove(clientId, out _);
            connection.Dispose();
            Console.WriteLine($"[DISCONNECT] Client {clientId}");
            try
            {
                ClientDisconnected?.Invoke(clientId);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[DISCONNECT HANDLER ERROR] Client {clientId} : {e}");
            }
        }
    }
}
