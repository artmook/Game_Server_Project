/*
[ 코드 설명 ]

TCP 테스트 클라이언트

1. 127.0.0.1:7777 서버에 접속하고 UTF-8 문자열을 전송한다.
2. 콘솔 입력과 서버 메시지 수신을 독립적으로 진행한다.
3. HELLO 응답과 BROADCAST 메시지를 Unity 없이 확인한다.
4. EXIT, Ctrl+C, 서버 연결 종료 시 소켓과 작업을 정리한다.
*/

using System.Net;
using System.Net.Sockets;
using System.Text;
using GameServerProject.Networking;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
using var stopping = new CancellationTokenSource();
using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

ConsoleCancelEventHandler cancelHandler = (_, e) =>
{
    e.Cancel = true;
    stopping.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    Console.WriteLine("127.0.0.1:7777 서버에 접속 중...");
    using var connecting = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
    connecting.CancelAfter(TimeSpan.FromSeconds(10));
    await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 7777), connecting.Token);
    using var connection = new TcpConnection(socket);
    Console.WriteLine("접속 성공. HELLO / BROADCAST|안녕하세요 / EXIT (입력 본문 최대 4096바이트)");

    Console.WriteLine("방 명령: ROOM|CREATE / ROOM|JOIN|방ID / ROOM|LEAVE / ROOM|MEMBERS / ROOM|SAY|내용");
    Console.WriteLine("게임 명령: GAME|START / GAME|MOVE|x|y|z|회전|속도 / GAME|ACTION|1~4 / GAME|JUMP / GAME|ATTACK[|대상ID] / GAME|DIE");
    Task receiving = ReceiveMessagesAsync(connection, stopping.Token);
    try
    {
        while (!stopping.IsCancellationRequested)
        {
            // Windows 콘솔의 ReadLine은 즉시 취소되지 않을 수 있다. 별도 스레드에서
            // 입력을 기다리고, 서버 연결이 끝나면 입력 중이어도 프로그램을 종료한다.
            Task<string?> input = Task.Run(Console.ReadLine);
            Task completed = await Task.WhenAny(input, receiving).WaitAsync(stopping.Token);
            if (completed == receiving)
            {
                break;
            }

            string? message = await input;
            if (message is null || message.Equals("EXIT", StringComparison.OrdinalIgnoreCase))
            {
                break; // 입력 스트림 끝도 정상 종료로 처리한다.
            }
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }
            if (Encoding.UTF8.GetByteCount(message) > TcpConnection.MaxClientMessageBytes)
            {
                Console.WriteLine("[INPUT ERROR] 입력 본문은 최대 4096바이트입니다.");
                continue;
            }

            await connection.SendAsync(message, stopping.Token);
        }
    }
    finally
    {
        stopping.Cancel();
        connection.Dispose();
        // 수신 작업 예외도 반드시 관찰하여 통신 오류를 놓치지 않는다.
        await receiving;
    }
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested)
{
    // 사용자가 Ctrl+C 또는 EXIT로 종료했다.
}
catch (Exception e)
{
    Console.Error.WriteLine($"[CLIENT ERROR] {e.GetType().Name} : {e.Message}");
    Environment.ExitCode = 1;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
    Console.WriteLine("클라이언트 종료");
}

// 송신 여부와 상관없이 서버가 보내는 브로드캐스트를 계속 받는다.
static async Task ReceiveMessagesAsync(TcpConnection connection, CancellationToken cancellationToken)
{
    try
    {
        while (true)
        {
            string? message = await connection.ReceiveAsync(cancellationToken);
            if (message is null)
            {
                Console.WriteLine("서버가 연결을 종료했습니다.");
                return;
            }
            Console.WriteLine($"Server : {message}");
        }
    }
    catch (Exception e) when (cancellationToken.IsCancellationRequested &&
                             e is OperationCanceledException or ObjectDisposedException or SocketException)
    {
        // 로컬 종료로 수신을 취소하거나 소켓을 닫은 경우에만 정상 정리로 처리한다.
    }
}
