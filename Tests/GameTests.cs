/*
[ 코드 설명 ]

NetworkTests 게임 검사 (C)

1. 방 검사 종료 후 별도 서버에 수동 시계를 넣은 RoomManager를 연결한다.
2. 실제 TCP로 게임 시작 조건, 이동·행동 중계와 값 검사를 확인한다.
3. 공격 쿨타임, 대상·거리 판정, 처치와 승패 통지를 확인한다.
4. 게임 중 퇴장·FIN·RST 종료의 탈락 처리와 승패 재판정을 확인한다.
*/

using System.Net.Sockets;

internal static partial class NetworkTests
{
    // 쿨타임 검사를 실제 2초 대기 없이 하기 위한 수동 시계이다.
    private sealed class ManualTime : TimeProvider
    {
        private long ticks = TimeSpan.TicksPerDay;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public void Advance(TimeSpan value) => Interlocked.Add(ref ticks, value.Ticks);
    }

    private static async Task RunGameTestsAsync(Action<string> pass)
    {
        using var stopping = new CancellationTokenSource();
        var clock = new ManualTime();
        var server = new GameServer();
        var rooms = new RoomManager(server, clock);
        server.MessageHandler = rooms.HandleMessageAsync;
        server.ClientDisconnected += rooms.HandleClientDisconnected;
        Task running = server.StartAsync(7777, stopping.Token);
        var peers = new List<Peer>();
        async Task Expect(Peer peer, string command, string expected)
        {
            await peer.Connection.SendAsync(command);
            await peer.ExpectAsync(expected);
        }
        // 다음 응답이 HELLO이면 그 사이에 추가 프레임이 오지 않은 것이다.
        async Task Quiet(params Peer[] values)
        {
            foreach (Peer peer in values) await Expect(peer, "HELLO", "SERVER|RECEIVED|HELLO");
        }
        async Task All(string expected, params Peer[] values)
        {
            foreach (Peer peer in values) await peer.ExpectAsync(expected);
        }
        try
        {
            if (running.IsCompleted) await running;
            for (int i = 0; i < 5; i++) peers.Add(await Peer.ConnectAsync());
            Peer a = peers[0], b = peers[1], c = peers[2], d = peers[3], outsider = peers[4];

            foreach (string command in new[] { "GAME", "GAME|", "GAME|UNKNOWN", "GAME|START|1", "GAME|MOVE|1|2",
                                               "GAME|ACTION", "GAME|ATTACK|1|2", "GAME|JUMP|1", "GAME|DIE|1" })
                await Expect(a, command, "SERVER|GAME|ERROR|INVALID_COMMAND");
            await Expect(outsider, "GAME|START", "SERVER|GAME|ERROR|NOT_IN_ROOM");
            await Expect(a, "ROOM|CREATE", "SERVER|ROOM|CREATED|1");
            await Expect(a, "GAME|START", "SERVER|GAME|ERROR|NOT_ENOUGH_PLAYERS");
            await Expect(a, "GAME|MOVE|0|0|0|0|0", "SERVER|GAME|ERROR|NOT_STARTED");
            await Expect(a, "GAME|ATTACK", "SERVER|GAME|ERROR|NOT_STARTED");
            await Expect(a, "일반|문자열", "SERVER|RECEIVED|일반|문자열");
            pass("게임 명령 형식·미소속·인원 부족·시작 전 오류 / 일반 응답 유지");

            await Expect(b, "ROOM|JOIN|1", "SERVER|ROOM|JOINED|1");
            await Expect(c, "ROOM|JOIN|1", "SERVER|ROOM|JOINED|1");
            await a.Connection.SendAsync("GAME|START");
            string started = await a.ReadAsync() ?? "";
            await All(started, b, c);
            string[] fields = started.Split('|');
            Check(fields.Length == 7 && started.StartsWith("SERVER|GAME|STARTED|1|"), $"시작 통지 형식 오류: {started}");
            int game1 = int.Parse(fields[4]);
            Check(int.Parse(fields[5]) > 0, "AI 시드 오류");
            string[] spawns = fields[6].Split(',');
            Check(spawns.Select(s => int.Parse(s.Split(':')[0])).SequenceEqual(new[] { a.Id, b.Id, c.Id }.Order()),
                  "시작 플레이어 목록 오류");
            Check(spawns.Select(s => int.Parse(s.Split(':')[1])).Order().SequenceEqual(new[] { 0, 1, 2 }),
                  "스폰 번호 중복/누락");
            await Expect(b, "GAME|START", "SERVER|GAME|ERROR|ALREADY_STARTED");
            await Expect(d, "ROOM|JOIN|1", "SERVER|ROOM|ERROR|GAME_IN_PROGRESS");
            pass("방 3명 게임 시작 / 시드·스폰 번호 통지 / 중복 시작·게임 중 입장 거부");

            await a.Connection.SendAsync("GAME|MOVE|1|0|2|450|5");
            await All($"SERVER|GAME|MOVE|{a.Id}|1|0|2|90|5", b, c);
            await b.Connection.SendAsync("GAME|MOVE|3.5|0.25|2|-90|0");
            await All($"SERVER|GAME|MOVE|{b.Id}|3.5|0.25|2|270|0", a, c);
            await c.Connection.SendAsync("GAME|MOVE|50|0|50|0|0");
            await All($"SERVER|GAME|MOVE|{c.Id}|50|0|50|0|0", a, b);
            foreach (string bad in new[] { "abc|0|0|0|0", "NaN|0|0|0|0", "1e400|0|0|0|0", "0|0|0|0|-1",
                                           "0|0|0|0|21", "20000|0|0|0|0", " 1|0|0|0|0", "1,5|0|0|0|0" })
                await Expect(a, $"GAME|MOVE|{bad}", "SERVER|GAME|ERROR|INVALID_VALUE");
            await Expect(a, "GAME|MOVE|30|0|2|0|5", "SERVER|GAME|ERROR|TOO_FAST");
            clock.Advance(TimeSpan.FromSeconds(1));
            await a.Connection.SendAsync("GAME|MOVE|10|0|2|0|5"); // 1초에 9m는 허용 범위(8m + 1.5m) 안이다.
            await All($"SERVER|GAME|MOVE|{a.Id}|10|0|2|0|5", b, c);
            clock.Advance(TimeSpan.FromSeconds(1));
            await a.Connection.SendAsync("GAME|MOVE|2|0|2|0|5");
            await All($"SERVER|GAME|MOVE|{a.Id}|2|0|2|0|5", b, c);
            await a.Connection.SendAsync("GAME|ACTION|2");
            await All($"SERVER|GAME|ACTION|{a.Id}|2", b, c);
            await a.Connection.SendAsync("GAME|JUMP");
            await All($"SERVER|GAME|JUMP|{a.Id}", b, c);
            foreach (string bad in new[] { "0", "5", "x", "+1" })
                await Expect(a, $"GAME|ACTION|{bad}", "SERVER|GAME|ERROR|INVALID_VALUE");
            await Quiet(a, b, c);
            pass("이동·행동·점프를 송신자 외 같은 방에 중계 / 회전 정규화 / 잘못된 값·순간이동 거부");

            await a.Connection.SendAsync($"GAME|ATTACK|{c.Id}");
            await All($"SERVER|GAME|ATTACK|{a.Id}", b, c);
            await a.ExpectAsync("SERVER|GAME|ERROR|OUT_OF_RANGE");
            await Expect(a, $"GAME|ATTACK|{b.Id}", "SERVER|GAME|ERROR|COOLDOWN");
            clock.Advance(TimeSpan.FromSeconds(1.7));
            await Expect(a, $"GAME|ATTACK|{b.Id}", "SERVER|GAME|ERROR|COOLDOWN");
            clock.Advance(TimeSpan.FromSeconds(0.2)); // 1.9초: 지연 여유 0.2초 안이라 허용한다.
            await a.Connection.SendAsync($"GAME|ATTACK|{a.Id}");
            await All($"SERVER|GAME|ATTACK|{a.Id}", b, c);
            await a.ExpectAsync("SERVER|GAME|ERROR|INVALID_TARGET");
            clock.Advance(TimeSpan.FromSeconds(2));
            await a.Connection.SendAsync("GAME|ATTACK|999999");
            await All($"SERVER|GAME|ATTACK|{a.Id}", b, c);
            await a.ExpectAsync("SERVER|GAME|ERROR|INVALID_TARGET");
            clock.Advance(TimeSpan.FromSeconds(2));
            await a.Connection.SendAsync($"GAME|ATTACK|{b.Id}");
            await All($"SERVER|GAME|ATTACK|{a.Id}", b, c);
            await All($"SERVER|GAME|KILL|{game1}|{a.Id}|{b.Id}|2", a, b, c);
            foreach (string command in new[] { "GAME|MOVE|3|0|2|0|0", "GAME|ATTACK", "GAME|JUMP", "GAME|DIE" })
                await Expect(b, command, "SERVER|GAME|ERROR|DEAD");
            clock.Advance(TimeSpan.FromSeconds(2));
            await c.Connection.SendAsync($"GAME|ATTACK|{b.Id}"); // 이미 죽은 대상
            await All($"SERVER|GAME|ATTACK|{c.Id}", a, b);
            await c.ExpectAsync("SERVER|GAME|ERROR|INVALID_TARGET");
            await a.Connection.SendAsync("GAME|ATTACK"); // 빗나감·AI 공격은 동작만 중계한다.
            await All($"SERVER|GAME|ATTACK|{a.Id}", b, c);
            await Quiet(a, b, c);
            pass("공격 쿨타임(지연 여유 포함) / 거리·자기·없는·죽은 대상 거부 / 처치 통지 / 사망자 명령 거부");

            await c.Connection.SendAsync("GAME|DIE");
            await All($"SERVER|GAME|KILL|{game1}|0|{c.Id}|1", a, b, c);
            await All($"SERVER|GAME|END|{game1}|{a.Id}", a, b, c);
            await Expect(a, "GAME|MOVE|0|0|0|0|0", "SERVER|GAME|ERROR|NOT_STARTED");
            await Expect(d, "ROOM|JOIN|1", "SERVER|ROOM|JOINED|1");
            await Quiet(a, b, c, d);
            pass("AI 피격 사망(DIE) / 마지막 생존자 승리 END / 종료 후 대기 상태·재입장");

            await d.Connection.SendAsync("GAME|START");
            string restarted = await d.ReadAsync() ?? "";
            await All(restarted, a, b, c);
            int game2 = int.Parse(restarted.Split('|')[4]);
            Check(game2 > game1 && restarted.Split('|')[6].Split(',').Length == 4, $"두 번째 게임 시작 오류: {restarted}");
            d.Socket.LingerState = new LingerOption(true, 0); // RST 종료
            d.Dispose();
            await All($"SERVER|GAME|OUT|{game2}|{d.Id}|3", a, b, c);
            await Expect(c, "ROOM|LEAVE", "SERVER|ROOM|LEFT|1");
            await All($"SERVER|GAME|OUT|{game2}|{c.Id}|2", a, b);
            b.Dispose(); // FIN 종료
            await a.ExpectAsync($"SERVER|GAME|OUT|{game2}|{b.Id}|1");
            await a.ExpectAsync($"SERVER|GAME|END|{game2}|{a.Id}");
            await rooms.WaitForNoticesAsync().WaitAsync(TestTimeout);
            await Expect(a, "ROOM|MEMBERS", $"SERVER|ROOM|MEMBERS|1|{a.Id}");
            await Expect(a, "GAME|START", "SERVER|GAME|ERROR|NOT_ENOUGH_PLAYERS");
            await Quiet(a, c);
            pass("게임 중 RST·LEAVE·FIN 종료 탈락 통지 / 남은 1명 승리 / 방 대기 상태 복귀");

            await Expect(c, "ROOM|JOIN|1", "SERVER|ROOM|JOINED|1");
            await a.Connection.SendAsync("GAME|START");
            string third = await a.ReadAsync() ?? "";
            await All(third, c);
            int game3 = int.Parse(third.Split('|')[4]);
            stopping.Cancel();
            await running.WaitAsync(TestTimeout);
            await rooms.WaitForNoticesAsync().WaitAsync(TestTimeout);
            Check(server.ClientIds.Length == 0, "게임 중 서버 종료 후 접속 잔류");
            using var restarting = new CancellationTokenSource();
            Task again = server.StartAsync(7777, restarting.Token);
            try
            {
                using Peer probe = await Peer.ConnectAsync();
                await Expect(probe, "ROOM|JOIN|1", "SERVER|ROOM|ERROR|ROOM_NOT_FOUND");
            }
            finally { restarting.Cancel(); await again.WaitAsync(TestTimeout); }
            Check(game3 > game2, "게임 번호 재사용");
            pass("게임 중 서버 취소 종료 / 탈락 통지 작업 정리 / 재시작 후 방·게임 삭제");
        }
        finally
        {
            stopping.Cancel();
            foreach (Peer peer in peers) peer.Dispose();
            await running.WaitAsync(TestTimeout);
        }
    }
}
