/*
[ 코드 설명 ]

NetworkTests 방 검사

1. 기존 A 검사 종료 후 별도 서버에 RoomManager를 연결한다.
2. 실제 TCP로 정원, 소속, 메시지 격리와 동시 변경을 검사한다.
3. 실제 TestClient 여러 개로 방 명령과 EXIT 및 강제 종료를 검사한다.
4. 소속자가 있는 서버 종료와 빈 방 삭제를 확인한다.
*/

using System.Diagnostics;
using System.Text;
using System.Net.Sockets;

internal static partial class NetworkTests
{
    private static async Task RunRoomTestsAsync(Action<string> pass)
    {
        using var stopping = new CancellationTokenSource();
        var server = new GameServer();
        var rooms = new RoomManager(server);
        server.MessageHandler = rooms.HandleMessageAsync;
        server.ClientDisconnected += rooms.HandleClientDisconnected;
        Task running = server.StartAsync(7777, stopping.Token);
        var peers = new List<Peer>();
        async Task<string> Ask(Peer peer, string command)
        {
            await peer.Connection.SendAsync(command);
            return await peer.ReadAsync() ?? throw new InvalidOperationException("방 응답 없이 종료");
        }
        async Task Expect(Peer peer, string command, string expected) =>
            Check(await Ask(peer, command) == expected, $"방 명령 실패: {command} / 기대: {expected}");
        async Task<int> Create(Peer peer)
        {
            string response = await Ask(peer, "ROOM|CREATE");
            Check(response.StartsWith("SERVER|ROOM|CREATED|"), $"방 생성 실패: {response}");
            return int.Parse(response.Split('|')[3]);
        }
        string Members(int room, params Peer[] values) =>
            $"SERVER|ROOM|MEMBERS|{room}|{string.Join(",", values.Select(p => p.Id).Order())}";
        try
        {
            if (running.IsCompleted) await running;
            for (int i = 0; i < 6; i++) peers.Add(await Peer.ConnectAsync());
            Peer a = peers[0], b = peers[1], c = peers[2], d = peers[3], e = peers[4], f = peers[5];
            int room = await Create(a);
            await Expect(a, "ROOM|MEMBERS", Members(room, a));
            foreach (Peer peer in new[] { b, c, d })
                await Expect(peer, $"ROOM|JOIN|{room}", $"SERVER|ROOM|JOINED|{room}");
            await Expect(a, "ROOM|MEMBERS", Members(room, a, b, c, d));
            await Expect(e, $"ROOM|JOIN|{room}", "SERVER|ROOM|ERROR|ROOM_FULL");
            pass("방 생성 자동 입장 / 4명 멤버 목록 / 5번째 입장 거부");

            await Expect(d, "ROOM|LEAVE", $"SERVER|ROOM|LEFT|{room}");
            string[] race = await Task.WhenAll(new[] { d, e }.Select(peer => Ask(peer, $"ROOM|JOIN|{room}")));
            Check(race.Count(s => s == $"SERVER|ROOM|JOINED|{room}") == 1 &&
                  race.Count(s => s == "SERVER|ROOM|ERROR|ROOM_FULL") == 1, "마지막 자리 동시 입장 실패");
            Peer winner = race[0].Contains("JOINED") ? d : e;
            Peer outsider = winner == d ? e : d;
            await Expect(a, "ROOM|MEMBERS", Members(room, a, b, c, winner));
            pass("마지막 한 자리 동시 입장 / 정원과 소속 일치");

            int otherRoom = await Create(f);
            string body = "한글🙂|999|송신자 위조 불가";
            await a.Connection.SendAsync($"ROOM|SAY|{body}");
            foreach (Peer peer in new[] { a, b, c, winner })
                await peer.ExpectAsync($"SERVER|ROOM|MESSAGE|{room}|{a.Id}|{body}");
            // HELLO가 바로 다음 프레임이면 다른 방과 미입장자에게 방 메시지가 새지 않았다.
            await Expect(f, "HELLO", "SERVER|RECEIVED|HELLO");
            await Expect(outsider, "ROOM|SAY|거부", "SERVER|ROOM|ERROR|NOT_IN_ROOM");
            await Expect(outsider, "HELLO", "SERVER|RECEIVED|HELLO");
            string maxBody = new('x', 4087); // ROOM|SAY| 포함 정확히 4096바이트이다.
            await a.Connection.SendAsync($"ROOM|SAY|{maxBody}");
            foreach (Peer peer in new[] { a, b, c, winner })
                await peer.ExpectAsync($"SERVER|ROOM|MESSAGE|{room}|{a.Id}|{maxBody}");
            pass("두 방 및 미입장자 메시지 격리 / 서버 송신자 ID / 한글·파이프·4096바이트 입력");

            foreach (string command in new[] { "ROOM", "ROOM|UNKNOWN", "ROOM|MEMBERS|extra", "ROOM|CREATE|extra", "ROOM|SAY" })
                await Expect(a, command, "SERVER|ROOM|ERROR|INVALID_COMMAND");
            foreach (string id in new[] { "", "abc", "0", "-1", "2147483648", "1|2", " 1", "+1" })
                await Expect(outsider, $"ROOM|JOIN|{id}", "SERVER|ROOM|ERROR|INVALID_ROOM_ID");
            await Expect(outsider, "ROOM|JOIN|2147483647", "SERVER|ROOM|ERROR|ROOM_NOT_FOUND");
            await Expect(a, "ROOM|CREATE", "SERVER|ROOM|ERROR|ALREADY_IN_ROOM");
            await Expect(a, $"ROOM|JOIN|{room}", "SERVER|ROOM|ERROR|ALREADY_IN_ROOM");
            await Expect(a, $"ROOM|JOIN|{otherRoom}", "SERVER|ROOM|ERROR|ALREADY_IN_ROOM");
            foreach (string text in new[] { "", "   " })
                await Expect(a, $"ROOM|SAY|{text}", "SERVER|ROOM|ERROR|EMPTY_MESSAGE");
            await Expect(outsider, "ROOM|LEAVE", "SERVER|ROOM|ERROR|NOT_IN_ROOM");
            await Expect(outsider, "ROOM|MEMBERS", "SERVER|ROOM|ERROR|NOT_IN_ROOM");
            await Expect(a, "HELLO", "SERVER|RECEIVED|HELLO");
            await Expect(a, "일반|문자열", "SERVER|RECEIVED|일반|문자열");
            pass("잘못된 ROOM 명령·ID·중복 소속·빈 본문 오류 / 연결·HELLO·일반 응답 유지");

            await Expect(winner, "ROOM|LEAVE", $"SERVER|ROOM|LEFT|{room}");
            await Expect(winner, "ROOM|MEMBERS", "SERVER|ROOM|ERROR|NOT_IN_ROOM");
            await Expect(outsider, $"ROOM|JOIN|{room}", $"SERVER|ROOM|JOINED|{room}");
            await Expect(outsider, "ROOM|LEAVE", $"SERVER|ROOM|LEFT|{room}");
            b.Socket.LingerState = new LingerOption(true, 0);
            b.Dispose();
            c.Dispose();
            await UntilAsync(() => !server.ClientIds.Contains(b.Id) && !server.ClientIds.Contains(c.Id));
            // 서버 목록 제거와 콜백 사이에도 경합할 수 있어 응답을 반복 조회한다.
            string remaining = "";
            for (int i = 0; i < 100; i++)
            {
                remaining = await Ask(a, "ROOM|MEMBERS");
                if (remaining == Members(room, a)) break;
                await Task.Delay(10);
            }
            Check(remaining == Members(room, a), "종료된 멤버 잔류");
            await Expect(a, "ROOM|LEAVE", $"SERVER|ROOM|LEFT|{room}");
            await Expect(winner, $"ROOM|JOIN|{room}", "SERVER|ROOM|ERROR|ROOM_NOT_FOUND");
            int oldId = b.Id;
            Peer reconnected = await Peer.ConnectAsync(); peers.Add(reconnected);
            Check(reconnected.Id > oldId, "재접속 ID 재사용");
            await Expect(reconnected, "ROOM|MEMBERS", "SERVER|ROOM|ERROR|NOT_IN_ROOM");
            int deleted = await Create(reconnected);
            reconnected.Dispose();
            await UntilAsync(() => !server.ClientIds.Contains(reconnected.Id));
            string deletedReply = "";
            for (int i = 0; i < 100; i++)
            {
                deletedReply = await Ask(winner, $"ROOM|JOIN|{deleted}");
                if (deletedReply == "SERVER|ROOM|ERROR|ROOM_NOT_FOUND") break;
                await Expect(winner, "ROOM|LEAVE", $"SERVER|ROOM|LEFT|{deleted}");
                await Task.Delay(10);
            }
            Check(deletedReply == "SERVER|ROOM|ERROR|ROOM_NOT_FOUND", "마지막 종료 후 빈 방 잔류");
            pass("퇴장·빈자리 재입장 / FIN·RST 멤버 제거 / 마지막 퇴장·종료의 방 삭제 / 재접속 미소속");

            int stressRoom = await Create(a);
            Peer worker3 = await Peer.ConnectAsync(); peers.Add(worker3);
            Peer[] workers = [winner, outsider, worker3];
            // 세 작업이 같은 방에서 입장·송신·퇴장을 반복하며 생성자는 계속 수신한다.
            Task read = Task.Run(async () =>
            {
                var expected = workers.SelectMany(p => Enumerable.Range(0, 20)
                    .Select(i => $"SERVER|ROOM|MESSAGE|{stressRoom}|{p.Id}|{p.Id}:{i}")).ToHashSet();
                for (int i = 0; i < 60; i++) Check(expected.Remove(await a.ReadAsync() ?? ""), "동시 방 메시지 손상/중복");
                Check(expected.Count == 0, "동시 방 메시지 누락");
            });
            await Task.WhenAll(workers.Select(async peer =>
            {
                for (int i = 0; i < 20; i++)
                {
                    await peer.Connection.SendAsync($"ROOM|JOIN|{stressRoom}");
                    string joined;
                    do
                    {
                        joined = await peer.ReadAsync() ?? "";
                    } while (joined.StartsWith($"SERVER|ROOM|MESSAGE|{stressRoom}|"));
                    Check(joined == $"SERVER|ROOM|JOINED|{stressRoom}", "동시 재입장 실패");
                    await peer.Connection.SendAsync($"ROOM|SAY|{peer.Id}:{i}");
                    await peer.Connection.SendAsync("ROOM|LEAVE");
                    bool own = false;
                    while (true)
                    {
                        string value = await peer.ReadAsync() ?? "";
                        if (value == $"SERVER|ROOM|LEFT|{stressRoom}") break;
                        Check(value.StartsWith($"SERVER|ROOM|MESSAGE|{stressRoom}|"), "동시 방 처리 응답 오류");
                        if (value == $"SERVER|ROOM|MESSAGE|{stressRoom}|{peer.Id}|{peer.Id}:{i}") own = true;
                    }
                    Check(own, "자기 방 메시지 누락");
                    // 퇴장 전 스냅샷 송신이 늦게 도착할 수 있다. HELLO까지 정상 프레임을 배출한다.
                    await peer.Connection.SendAsync("HELLO");
                    while (await peer.ReadAsync() is { } tail && tail != "SERVER|RECEIVED|HELLO")
                        Check(tail.StartsWith($"SERVER|ROOM|MESSAGE|{stressRoom}|"), "늦은 방 메시지 손상");
                }
            }).Append(read));
            foreach (Peer worker in workers)
            {
                await worker.Connection.SendAsync("HELLO");
                while (await worker.ReadAsync() is { } value && value != "SERVER|RECEIVED|HELLO")
                    Check(value.StartsWith($"SERVER|ROOM|MESSAGE|{stressRoom}|"), "잔여 방 프레임 손상");
            }
            await Expect(a, "ROOM|MEMBERS", Members(stressRoom, a));
            pass("3개 클라이언트 동시 입장·방송·퇴장 60회 / 수신 스냅샷·최종 소속 일관성");

            await TestRoomConsoleProcessesAsync(server);
            pass("실제 TestClient 3개: 생성·입장·입력 대기 수신 / EXIT·강제 종료 후 멤버·빈 방 정리");
            stopping.Cancel();
            await running.WaitAsync(TestTimeout);
            Check(server.ClientIds.Length == 0, "방 소속자 서버 종료 후 접속 잔류");
            foreach (Peer peer in peers.Where(p => p != b && p != c && p != reconnected)) await peer.ExpectClosedAsync();
            // 같은 서버와 관리자를 재시작해 종료 콜백이 모든 방을 지웠는지 TCP로 확인한다.
            using var restarting = new CancellationTokenSource();
            Task restarted = server.StartAsync(7777, restarting.Token);
            try
            {
                using Peer probe = await Peer.ConnectAsync();
                await Expect(probe, $"ROOM|JOIN|{stressRoom}", "SERVER|ROOM|ERROR|ROOM_NOT_FOUND");
                await Expect(probe, $"ROOM|JOIN|{otherRoom}", "SERVER|ROOM|ERROR|ROOM_NOT_FOUND");
            }
            finally { restarting.Cancel(); await restarted.WaitAsync(TestTimeout); }
            pass("방 소속자 서버 취소 종료 / 재시작 후 기존 방 삭제 확인");
        }
        finally
        {
            stopping.Cancel();
            foreach (Peer peer in peers) peer.Dispose();
            await running.WaitAsync(TestTimeout);
        }
    }

    private static async Task TestRoomConsoleProcessesAsync(GameServer server)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var processes = new List<Process>();
        var errors = new List<Task<string>>();
        var ids = new List<int>();
        async Task Send(int index, string value)
        {
            await processes[index].StandardInput.WriteLineAsync(value);
            await processes[index].StandardInput.FlushAsync();
        }
        try
        {
            for (int i = 0; i < 3; i++)
            {
                var info = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
                };
                info.ArgumentList.Add(Path.Combine(root, "TestClient", "bin", configuration, "net10.0", "TestClient.dll"));
                Process p = Process.Start(info) ?? throw new InvalidOperationException("콘솔 시작 실패");
                processes.Add(p); errors.Add(p.StandardError.ReadToEndAsync());
                string welcome = await ReadUntilAsync(p, "SERVER|CONNECTED|");
                ids.Add(int.Parse(welcome.Split('|')[2]));
            }
            await Send(0, "ROOM|CREATE");
            int room = int.Parse((await ReadUntilAsync(processes[0], "SERVER|ROOM|CREATED|")).Split('|')[3]);
            foreach (int index in new[] { 1, 2 })
            {
                await Send(index, $"ROOM|JOIN|{room}");
                await ReadUntilAsync(processes[index], $"SERVER|ROOM|JOINED|{room}");
            }
            await Send(0, "ROOM|SAY|콘솔|방 메시지");
            foreach (Process p in processes)
                await ReadUntilAsync(p, $"SERVER|ROOM|MESSAGE|{room}|{ids[0]}|콘솔|방 메시지");
            await Send(1, "EXIT");
            await processes[1].WaitForExitAsync().WaitAsync(TestTimeout);
            Check(processes[1].ExitCode == 0, "방 소속 EXIT 실패");
            processes[2].Kill(entireProcessTree: true);
            await processes[2].WaitForExitAsync().WaitAsync(TestTimeout);
            await UntilAsync(() => !server.ClientIds.Contains(ids[1]) && !server.ClientIds.Contains(ids[2]));
            await Send(0, "ROOM|MEMBERS");
            await ReadUntilAsync(processes[0], $"SERVER|ROOM|MEMBERS|{room}|{ids[0]}");
            await Send(0, "EXIT");
            await processes[0].WaitForExitAsync().WaitAsync(TestTimeout);
            Check(processes[0].ExitCode == 0, "방 생성자 EXIT 실패");
            await UntilAsync(() => !server.ClientIds.Contains(ids[0]));
            using Peer probe = await Peer.ConnectAsync();
            await probe.Connection.SendAsync($"ROOM|JOIN|{room}");
            await probe.ExpectAsync("SERVER|ROOM|ERROR|ROOM_NOT_FOUND");
        }
        finally
        {
            foreach (Process p in processes)
            {
                if (!p.HasExited) { p.Kill(entireProcessTree: true); await p.WaitForExitAsync(); }
                p.Dispose();
            }
            string[] output = await Task.WhenAll(errors);
            Check(output.All(string.IsNullOrEmpty), "콘솔 클라이언트 오류: " + string.Join(" / ", output));
        }
    }
}
