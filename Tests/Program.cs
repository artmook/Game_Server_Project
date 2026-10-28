/*
[ 코드 설명 ]

NetworkTests

1. 실제 7777 포트에 GameServer를 실행하고 여러 TCP 클라이언트로 검증한다.
2. 분할 수신, 연속 메시지, 한글, 잘못된 입력, 동시 송신과 종료를 검사한다.
3. 실제 TestClient 프로세스 4개를 실행해 터미널 프로그램도 검사한다.
4. 실패 시 예외와 실패 종료 코드를 남기며 사용한 연결과 프로세스를 정리한다.
*/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GameServerProject.Networking;

internal static class NetworkTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(8);

    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        TextWriter report = Console.Out;
        string logPath = Path.Combine(AppContext.BaseDirectory, "network-test-server.log");
        using var log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
        Console.SetOut(TextWriter.Synchronized(log)); // 대량의 서버 로그는 bin 안에 따로 보관한다.
        using var stopping = new CancellationTokenSource();
        var server = new GameServer();
        var disconnected = new ConcurrentDictionary<int, bool>();
        server.ClientDisconnected += id => disconnected.TryAdd(id, true);
        Task serverTask = server.StartAsync(7777, stopping.Token);
        Peer[] peers = [];
        int passed = 0;

        void Pass(string name)
        {
            passed++;
            report.WriteLine($"PASS {passed:00} {name}");
        }

        try
        {
            if (serverTask.IsCompleted)
            {
                await serverTask; // 포트 충돌 등 시작 오류를 실제 원인과 함께 보고한다.
                throw new InvalidOperationException("서버가 접속 수락 전에 종료되었습니다.");
            }

            // 네 연결을 동시에 만들고, 서버가 알려 준 ID로 이후 응답을 검사한다.
            peers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Peer.ConnectAsync()));
            Check(peers.Select(peer => peer.Id).Distinct().Count() == 4, "고유 ID 중복");
            await UntilAsync(() => server.ClientIds.Length == 4);
            Pass("실제 TCP 서버 실행 / 클라이언트 4개 동시 접속 / 고유 ID");

            await Task.WhenAll(peers.Select(async peer =>
            {
                await peer.Connection.SendAsync("HELLO");
                await peer.ExpectAsync("SERVER|RECEIVED|HELLO");
            }));
            Pass("모든 클라이언트의 기존 HELLO 응답");

            await peers[0].Connection.SendAsync("안녕하세요 🙂 한글 테스트");
            await peers[0].ExpectAsync("SERVER|RECEIVED|안녕하세요 🙂 한글 테스트");
            await peers[0].Connection.SendAsync("BROADCAST|전체 안녕하세요");
            await Task.WhenAll(peers.Select(peer => peer.ExpectAsync($"SERVER|BROADCAST|{peers[0].Id}|전체 안녕하세요")));
            Pass("한글/이모지 송수신 / 송신자를 포함한 전체 브로드캐스트");

            await SendRawAsync(peers[0].Socket, Encoding.UTF8.GetBytes("HE"));
            await Task.Delay(40);
            Check(peers[0].Socket.Available == 0, "LF 전의 불완전 메시지에 응답함");
            await SendRawAsync(peers[0].Socket, Encoding.UTF8.GetBytes("LLO\n"));
            await peers[0].ExpectAsync("SERVER|RECEIVED|HELLO");
            foreach (byte value in Encoding.UTF8.GetBytes("한글🙂분할\n"))
            {
                await SendRawAsync(peers[0].Socket, new byte[] { value });
                await Task.Delay(2); // UTF-8 한 문자 내부도 여러 수신에 걸치게 만든다.
            }
            await peers[0].ExpectAsync("SERVER|RECEIVED|한글🙂분할");
            Pass("메시지 분할 / LF 전 응답 없음 / UTF-8 문자 내부 바이트 분할");

            await SendRawAsync(peers[0].Socket, Encoding.UTF8.GetBytes("ONE\nTWO\nTHREE\n"));
            foreach (string value in new[] { "ONE", "TWO", "THREE" })
            {
                await peers[0].ExpectAsync($"SERVER|RECEIVED|{value}");
            }
            Pass("한 번에 연속 전송한 메시지 3개의 경계 및 순서");

            string maxInput = new string('가', 1365) + "a"; // UTF-8로 정확히 4096바이트이다.
            await peers[0].Connection.SendAsync(maxInput);
            await peers[0].ExpectAsync($"SERVER|RECEIVED|{maxInput}");
            string maxFrame = new('x', TcpConnection.MaxMessageBytes);
            Check(await server.SendToClientAsync(peers[0].Id, maxFrame), "8192바이트 송신 실패");
            await peers[0].ExpectAsync(maxFrame);
            Pass("4096바이트 한글 입력 및 8192바이트 서버 메시지 경계값");

            await RejectAsync(server, Encoding.UTF8.GetBytes(new string('x', 4097) + "\n"));
            await RejectAsync(server, Encoding.UTF8.GetBytes(new string('x', 8193)));
            await RejectAsync(server, new byte[] { 0xff, (byte)'\n' });
            await RejectAsync(server, Encoding.UTF8.GetBytes("BAD\r\n"));
            await RejectAsync(server, Encoding.UTF8.GetBytes("INCOMPLETE"), endSending: true);
            await peers[0].Connection.SendAsync("HELLO");
            await peers[0].ExpectAsync("SERVER|RECEIVED|HELLO");
            Pass("본문/프레임 초과, 잘못된 UTF-8, CR, 미완성 종료 차단 / 다른 연결 유지");

            int oldId = peers[3].Id;
            peers[3].Dispose();
            await UntilAsync(() => !server.ClientIds.Contains(oldId) && disconnected.ContainsKey(oldId));
            peers[3] = await Peer.ConnectAsync();
            Check(peers[3].Id > oldId, "재접속 시 ID 재사용");

            oldId = peers[2].Id;
            peers[2].Socket.LingerState = new LingerOption(true, 0); // FIN 대신 RST로 비정상 종료한다.
            peers[2].Socket.Dispose();
            peers[2].Dispose();
            await UntilAsync(() => !server.ClientIds.Contains(oldId) && disconnected.ContainsKey(oldId));
            peers[2] = await Peer.ConnectAsync();
            await peers[1].Connection.SendAsync("HELLO");
            await peers[1].ExpectAsync("SERVER|RECEIVED|HELLO");
            Pass("정상 종료 / TCP RST 종료 / 목록 제거와 종료 통지 / 재접속 / 기존 통신 유지");

            Check(await server.SendToClientAsync(peers[0].Id, "SERVER|PRIVATE|개인"), "개인 송신 실패");
            await peers[0].ExpectAsync("SERVER|PRIVATE|개인");
            await server.SendToClientsAsync(new[] { peers[0].Id, peers[1].Id, peers[0].Id }, "SERVER|GROUP|선택");
            await peers[0].ExpectAsync("SERVER|GROUP|선택");
            await peers[1].ExpectAsync("SERVER|GROUP|선택");
            await Task.Delay(40);
            Check(peers.All(peer => peer.Socket.Available == 0), "중복 또는 잘못된 대상에게 전송");
            Check(!await server.SendToClientAsync(int.MaxValue, "없는 대상"), "없는 ID 전송 성공 처리");
            Pass("개인/선택한 여러 명 전송 API / 중복 ID 제거 / 없는 대상 false");

            server.MessageHandler = async (id, message, token) =>
            {
                if (message == "TEST|THROW")
                {
                    throw new InvalidOperationException("테스트용 확장 처리 오류");
                }
                await server.SendToClientAsync(id, $"SERVER|EXTENSION|{message}", token);
            };
            await peers[0].Connection.SendAsync("TEST|확장");
            await peers[0].ExpectAsync("SERVER|EXTENSION|TEST|확장");
            await peers[0].Connection.SendAsync("HELLO");
            await peers[0].ExpectAsync("SERVER|RECEIVED|HELLO");
            using (Peer broken = await Peer.ConnectAsync())
            {
                await broken.Connection.SendAsync("TEST|THROW");
                await broken.ExpectClosedAsync();
                await UntilAsync(() => !server.ClientIds.Contains(broken.Id));
            }
            server.MessageHandler = null;
            Pass("B 연계 메시지 처리 지점 / HELLO 유지 / 확장 처리 예외 격리");

            await ConcurrentTrafficAsync(peers);
            Pass("4개 동시 송신: 방송 160건 + HELLO 80건 / 수신 720건 무손실·무중복 / 통신 중 추가 접속");

            // 같은 서버 소켓에 여러 송신을 겹쳐 보내 프레임이 섞이지 않는지 검사한다.
            string[] messages = Enumerable.Range(0, 80).Select(index => $"SERVER|PARALLEL|{index}|" + new string('z', 8000)).ToArray();
            Task reading = Task.Run(async () =>
            {
                var received = new HashSet<string>();
                for (int index = 0; index < messages.Length; index++)
                {
                    Check(received.Add(await peers[0].ReadAsync() ?? ""), "동시 송신 중복");
                }
                Check(received.SetEquals(messages), "동시 송신 메시지 경계 손상");
            });
            bool[] sent = await Task.WhenAll(messages.Select(message => server.SendToClientAsync(peers[0].Id, message)));
            Check(sent.All(value => value), "동시 서버 송신 실패");
            await reading;
            Pass("같은 서버 소켓에 8KB급 메시지 80개 동시 송신 / 프레임 혼합 없음");

            string consoleBroadcast = await TestConsoleProcessesAsync(server);
            foreach (Peer peer in peers)
            {
                // 콘솔 클라이언트의 전체 방송은 기존 원시 TCP 클라이언트에게도 도착한다.
                // 다음 검사에 남겨 두지 않고 여기서 정확한 내용까지 확인한다.
                await peer.ExpectAsync(consoleBroadcast);
            }
            Pass("실제 TestClient 4개: HELLO / 입력 대기 중 방송 수신 / 입력 초과 차단 / EXIT / 프로세스 강제 종료");

            using (Peer slow = await Peer.ConnectAsync(receiveBufferSize: 1024))
            {
                // 더 이상 읽지 않는 대상의 송신 버퍼를 채워 실제 전송 제한시간을 검사한다.
                bool[] results = await Task.WhenAll(Enumerable.Range(0, 512)
                    .Select(_ => server.SendToClientAsync(slow.Id, maxFrame))).WaitAsync(TimeSpan.FromSeconds(20));
                Check(results.Any(value => !value), "느린 대상의 송신 타임아웃이 발생하지 않음");
                await UntilAsync(() => !server.ClientIds.Contains(slow.Id));
            }
            await peers[0].Connection.SendAsync("HELLO");
            await peers[0].ExpectAsync("SERVER|RECEIVED|HELLO");
            Pass("읽지 않는 클라이언트의 실제 송신 타임아웃 / 대기 송신 정리 / 다른 연결 유지");

            // 메시지 일부를 받은 채 대기하는 수신도 서버 종료 시 안전하게 취소되어야 한다.
            await SendRawAsync(peers[3].Socket, Encoding.UTF8.GetBytes("종료 전 미완성 메시지"));
            await Task.Delay(20);
            stopping.Cancel();
            await serverTask.WaitAsync(TestTimeout);
            foreach (Peer peer in peers)
            {
                await peer.ExpectClosedAsync();
            }
            Check(server.ClientIds.Length == 0, "서버 종료 후 접속 목록이 남음");
            using (var rebound = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                rebound.Bind(new IPEndPoint(IPAddress.Any, 7777));
            }
            Pass("미완성 수신 중 서버 취소 종료 / 전체 작업·목록 정리 / 포트 재바인딩");

            report.WriteLine($"ALL PASS ({passed}) / 서버 로그: {logPath}");
            return 0;
        }
        catch (Exception e)
        {
            report.WriteLine($"FAIL ({passed}개 통과 후) {e}");
            report.WriteLine($"서버 로그: {logPath}");
            return 1;
        }
        finally
        {
            stopping.Cancel();
            foreach (Peer peer in peers)
            {
                peer.Dispose();
            }
            try
            {
                await serverTask.WaitAsync(TestTimeout);
            }
            catch (Exception e)
            {
                report.WriteLine($"서버 종료 확인: {e.Message}");
            }
            Console.SetOut(report);
        }
    }

    private static async Task ConcurrentTrafficAsync(Peer[] peers)
    {
        const int broadcasts = 40;
        const int hellos = 20;
        string Body(int id, int index) => $"{id}:{index}:동시:" + new string('x', 512);
        var expected = peers.SelectMany(peer => Enumerable.Range(0, broadcasts)
            .Select(index => $"SERVER|BROADCAST|{peer.Id}|{Body(peer.Id, index)}")).ToHashSet();

        Task[] readers = peers.Select(async peer =>
        {
            var remaining = new HashSet<string>(expected);
            int helloCount = 0;
            for (int index = 0; index < expected.Count + hellos; index++)
            {
                string? message = await peer.ReadAsync();
                if (message == "SERVER|RECEIVED|HELLO")
                {
                    helloCount++;
                }
                else
                {
                    Check(message is not null && remaining.Remove(message), "동시 방송의 중복/손상/잘못된 내용");
                }
            }
            Check(remaining.Count == 0 && helloCount == hellos, "동시 송수신 누락");
        }).ToArray();

        Task[] writers = peers.SelectMany(peer => new[]
        {
            Task.Run(async () =>
            {
                for (int index = 0; index < broadcasts; index++)
                    await peer.Connection.SendAsync($"BROADCAST|{Body(peer.Id, index)}");
            }),
            Task.Run(async () =>
            {
                for (int index = 0; index < hellos; index++)
                    await peer.Connection.SendAsync("HELLO");
            })
        }).ToArray();
        Task accepting = Task.Run(async () =>
        {
            // 방송과 새 접속이 경합해도 첫 메시지는 반드시 ID 안내여야 한다.
            for (int index = 0; index < 12; index++)
            {
                using Peer additional = await Peer.ConnectAsync();
            }
        });
        await Task.WhenAll(readers.Concat(writers).Append(accepting)).WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static async Task<string> TestConsoleProcessesAsync(GameServer server)
    {
        // 테스트 실행 위치가 달라도 Debug/Release에 맞는 실제 TestClient를 찾는다.
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        string clientPath = Path.Combine(root, "TestClient", "bin", configuration, "net10.0", "TestClient.dll");
        var processes = new List<Process>();
        var errors = new List<Task<string>>();
        var ids = new List<int>();
        try
        {
            for (int index = 0; index < 4; index++)
            {
                var info = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardInputEncoding = new UTF8Encoding(false),
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                info.ArgumentList.Add(clientPath);
                Process process = Process.Start(info) ?? throw new InvalidOperationException("TestClient 실행 실패");
                processes.Add(process);
                errors.Add(process.StandardError.ReadToEndAsync());
            }
            foreach (Process process in processes)
            {
                string welcome = await ReadUntilAsync(process, "Server : SERVER|CONNECTED|");
                ids.Add(int.Parse(welcome.Split('|')[2]));
                await process.StandardInput.WriteLineAsync("HELLO");
                await process.StandardInput.FlushAsync();
                await ReadUntilAsync(process, "Server : SERVER|RECEIVED|HELLO");
            }
            await processes[0].StandardInput.WriteLineAsync("BROADCAST|실제 콘솔 방송");
            await processes[0].StandardInput.FlushAsync();
            foreach (Process process in processes)
            {
                await ReadUntilAsync(process, $"Server : SERVER|BROADCAST|{ids[0]}|실제 콘솔 방송");
            }

            await processes[0].StandardInput.WriteLineAsync(new string('a', 4097));
            await processes[0].StandardInput.FlushAsync();
            await ReadUntilAsync(processes[0], "[INPUT ERROR]");
            await processes[0].StandardInput.WriteLineAsync("HELLO");
            await processes[0].StandardInput.FlushAsync();
            await ReadUntilAsync(processes[0], "Server : SERVER|RECEIVED|HELLO");

            processes[3].Kill(entireProcessTree: true);
            await processes[3].WaitForExitAsync().WaitAsync(TestTimeout);
            await UntilAsync(() => !server.ClientIds.Contains(ids[3]));
            for (int index = 0; index < 3; index++)
            {
                await processes[index].StandardInput.WriteLineAsync("EXIT");
                await processes[index].StandardInput.FlushAsync();
                await processes[index].WaitForExitAsync().WaitAsync(TestTimeout);
                Check(processes[index].ExitCode == 0, $"EXIT 실패: {await errors[index]}");
                await ReadUntilAsync(processes[index], "클라이언트 종료");
            }
            await UntilAsync(() => ids.All(id => !server.ClientIds.Contains(id)));
            return $"SERVER|BROADCAST|{ids[0]}|실제 콘솔 방송";
        }
        finally
        {
            foreach (Process process in processes)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                process.Dispose();
            }
            await Task.WhenAll(errors);
        }
    }

    private static async Task<string> ReadUntilAsync(Process process, string expected)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.Contains(expected, StringComparison.Ordinal))
                return line;
        }
        throw new InvalidOperationException($"TestClient 출력 없음: {expected}");
    }

    private static async Task RejectAsync(GameServer server, byte[] data, bool endSending = false)
    {
        using Peer peer = await Peer.ConnectAsync();
        try
        {
            await SendRawAsync(peer.Socket, data);
            if (endSending)
                peer.Socket.Shutdown(SocketShutdown.Send);
        }
        catch (SocketException)
        {
            // 초과 바이트를 보내는 도중 서버가 먼저 연결을 닫을 수도 있다.
        }
        await peer.ExpectClosedAsync();
        await UntilAsync(() => !server.ClientIds.Contains(peer.Id));
    }

    private static async Task SendRawAsync(Socket socket, byte[] bytes)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        int sent = 0;
        while (sent < bytes.Length)
        {
            int count = await socket.SendAsync(bytes.AsMemory(sent), SocketFlags.None, timeout.Token);
            Check(count > 0, "테스트 원시 송신 실패");
            sent += count;
        }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    // 테스트용 연결은 원시 바이트 전송과 공통 프로토콜 수신을 함께 제공한다.
    private sealed class Peer : IDisposable
    {
        public Socket Socket { get; }
        public TcpConnection Connection { get; }
        public int Id { get; private set; }

        private Peer(Socket socket)
        {
            Socket = socket;
            Connection = new TcpConnection(socket);
        }

        public static async Task<Peer> ConnectAsync(int? receiveBufferSize = null)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var peer = new Peer(socket);
            try
            {
                if (receiveBufferSize is { } size)
                    socket.ReceiveBufferSize = size;
                using var timeout = new CancellationTokenSource(TestTimeout);
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 7777), timeout.Token);
                string? welcome = await peer.ReadAsync();
                Check(welcome?.StartsWith("SERVER|CONNECTED|", StringComparison.Ordinal) == true, "접속 ID 응답 없음");
                peer.Id = int.Parse(welcome!.Split('|')[2]);
                return peer;
            }
            catch
            {
                peer.Dispose();
                throw;
            }
        }

        public async Task<string?> ReadAsync()
        {
            using var timeout = new CancellationTokenSource(TestTimeout);
            return await Connection.ReceiveAsync(timeout.Token);
        }

        public async Task ExpectAsync(string expected)
        {
            string? actual = await ReadAsync();
            Check(actual == expected, $"응답 불일치 (기대 {expected.Length}글자, 실제 {actual?.Length}글자): {actual?[..Math.Min(actual.Length, 120)]}");
        }

        public async Task ExpectClosedAsync()
        {
            try
            {
                Check(await ReadAsync() is null, "종료되어야 할 연결에 데이터가 도착함");
            }
            catch (SocketException)
            {
                // 정상 EOF와 TCP reset 모두 연결이 종료된 것으로 확인한다.
            }
        }

        public void Dispose() => Connection.Dispose();
    }
}
