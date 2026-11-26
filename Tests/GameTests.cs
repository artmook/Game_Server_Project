/*
[ 코드 설명 ]

NetworkTests 게임 검사 (C)

1. 방 검사 종료 후 별도 서버에 수동 시계를 넣은 RoomManager를 연결한다.
2. 실제 TCP로 게임 시작 조건, 이동·행동 중계와 값 검사를 확인한다.
3. 서버 술래 지정(STARTED의 술래 ID·제한시간), 술래만 잡기, 공격 쿨타임, 대상·거리 판정을 확인한다.
4. 미션 완료 통지와 RUNNER 승리, 모두 잡힘 TAGGER 승리, 시간 초과 TAGGER 승리, 술래 이탈 RUNNER 승리를 확인한다.
5. 게임 중 퇴장·FIN·RST 종료의 탈락 처리와 승패 재판정을 확인한다.
6. 시계는 백그라운드로 돌리지 않고 수동 시계를 움직인 뒤 TickClockAsync를 직접 호출한다.
*/

using System.Net.Sockets;

internal static partial class NetworkTests
{
    // 쿨타임·제한시간 검사를 실제 대기 없이 하기 위한 수동 시계이다.
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
        // 게임을 시작하고 STARTED를 모두 받은 뒤 (게임번호, 술래, 도망자 ID 오름차순)을 돌려준다.
        async Task<(int Game, Peer Tagger, Peer[] Runners, string Started)> Start(Peer starter, params Peer[] players)
        {
            await starter.Connection.SendAsync("GAME|START");
            string line = await starter.ReadAsync() ?? "";
            await All(line, players.Where(p => p != starter).ToArray());
            string[] f = line.Split('|');
            Check(f.Length == 10 && f[0] == "SERVER" && f[1] == "GAME" && f[2] == "STARTED", $"시작 통지 형식 오류: {line}");
            Check(f[8] == "300", $"제한시간 300초 아님: {line}");
            Check(int.Parse(f[9]) == players.Min(p => p.Id), $"AI 호스트가 가장 작은 ID가 아님: {line}");
            int taggerId = int.Parse(f[7]);
            Peer[] matches = players.Where(p => p.Id == taggerId).ToArray();
            Check(matches.Length == 1, $"술래 ID가 참가자가 아님: {line}");
            return (int.Parse(f[4]), matches[0], players.Where(p => p.Id != taggerId).OrderBy(p => p.Id).ToArray(), line);
        }
        try
        {
            if (running.IsCompleted) await running;
            for (int i = 0; i < 5; i++) peers.Add(await Peer.ConnectAsync());
            Peer a = peers[0], b = peers[1], c = peers[2], d = peers[3], outsider = peers[4];

            foreach (string command in new[] { "GAME", "GAME|", "GAME|UNKNOWN", "GAME|START|1", "GAME|MOVE|1|2",
                                               "GAME|ACTION", "GAME|ATTACK|1|2", "GAME|JUMP|1", "GAME|DIE|1",
                                               "GAME|MISSION|1" })
                await Expect(a, command, "SERVER|GAME|ERROR|INVALID_COMMAND");
            await Expect(outsider, "GAME|START", "SERVER|GAME|ERROR|NOT_IN_ROOM");
            await Expect(a, "ROOM|CREATE", "SERVER|ROOM|CREATED|1");
            await Expect(a, "GAME|START", "SERVER|GAME|ERROR|NOT_ENOUGH_PLAYERS");
            await Expect(a, "GAME|MOVE|0|0|0|0|0", "SERVER|GAME|ERROR|NOT_STARTED");
            await Expect(a, "GAME|ATTACK", "SERVER|GAME|ERROR|NOT_STARTED");
            await Expect(a, "GAME|MISSION", "SERVER|GAME|ERROR|NOT_STARTED");
            await Expect(a, "일반|문자열", "SERVER|RECEIVED|일반|문자열");
            pass("게임 명령 형식·미소속·인원 부족·시작 전 오류 / 일반 응답 유지");

            await Expect(b, "ROOM|JOIN|1", "SERVER|ROOM|JOINED|1");
            await Expect(c, "ROOM|JOIN|1", "SERVER|ROOM|JOINED|1");
            var (game1, tagger, runners, started) = await Start(a, a, b, c);
            string[] fields = started.Split('|');
            Check(started.StartsWith("SERVER|GAME|STARTED|1|"), $"시작 통지 방 번호 오류: {started}");
            Check(int.Parse(fields[5]) > 0, "AI 시드 오류");
            string[] spawns = fields[6].Split(',');
            Check(spawns.Select(s => int.Parse(s.Split(':')[0])).SequenceEqual(new[] { a.Id, b.Id, c.Id }.Order()),
                  "시작 플레이어 목록 오류");
            Check(spawns.Select(s => int.Parse(s.Split(':')[1])).Order().SequenceEqual(new[] { 0, 1, 2 }),
                  "스폰 번호 중복/누락");
            await Expect(b, "GAME|START", "SERVER|GAME|ERROR|ALREADY_STARTED");
            await Expect(d, "ROOM|JOIN|1", "SERVER|ROOM|ERROR|GAME_IN_PROGRESS");
            pass("방 3명 게임 시작 / 시드·스폰 번호·서버 술래 ID·제한시간 300초 통지 / 중복 시작·게임 중 입장 거부");

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

            // AI 동기화: a가 가장 작은 ID라 AI 호스트이다.
            const string aiPayload = "0:1.5:0:2:90:0:0,1:-3.25:0.1:4:270:1.2:1";
            await a.Connection.SendAsync($"GAME|AI|{aiPayload}");
            await All($"SERVER|GAME|AI|{game1}|{aiPayload}", b, c);
            await Expect(b, "GAME|AI|0:0:0:0:0:0:0", "SERVER|GAME|ERROR|NOT_AI_HOST");
            foreach (string bad in new[] { "abc", "0:1e5:0:0:0:0:0", "0;1" })
                await Expect(a, $"GAME|AI|{bad}", "SERVER|GAME|ERROR|INVALID_VALUE");
            await Expect(a, "GAME|AI|0:1|2", "SERVER|GAME|ERROR|INVALID_COMMAND");
            Peer[] notTagger = new[] { a, b, c }.Where(p => p != tagger).ToArray();
            await tagger.Connection.SendAsync("GAME|AISTUN|3");
            await All($"SERVER|GAME|AISTUN|{game1}|3", notTagger);
            await Expect(notTagger[0], "GAME|AISTUN|3", "SERVER|GAME|ERROR|NOT_TAGGER");
            foreach (string bad in new[] { "x", "-1", "1000" })
                await Expect(tagger, $"GAME|AISTUN|{bad}", "SERVER|GAME|ERROR|INVALID_VALUE");
            await Quiet(a, b, c);
            pass("AI 호스트(가장 작은 ID)만 AI 위치 중계 / 형식 검사 / 술래만 AI 기절 알림");

            // 술래 근처에 첫 도망자, 멀리 두 번째 도망자를 둔다. (10초면 81.5m까지 이동 가능)
            Peer r1 = runners[0], r2 = runners[1];
            clock.Advance(TimeSpan.FromSeconds(10));
            await tagger.Connection.SendAsync("GAME|MOVE|0|0|0|0|0");
            await All($"SERVER|GAME|MOVE|{tagger.Id}|0|0|0|0|0", r1, r2);
            await r1.Connection.SendAsync("GAME|MOVE|1|0|0|0|0");
            await All($"SERVER|GAME|MOVE|{r1.Id}|1|0|0|0|0", tagger, r2);
            await r2.Connection.SendAsync("GAME|MOVE|40|0|40|0|0");
            await All($"SERVER|GAME|MOVE|{r2.Id}|40|0|40|0|0", tagger, r1);

            await r1.Connection.SendAsync($"GAME|ATTACK|{r2.Id}"); // 도망자는 잡을 수 없다(동작만 중계).
            await All($"SERVER|GAME|ATTACK|{r1.Id}", tagger, r2);
            await r1.ExpectAsync("SERVER|GAME|ERROR|NOT_TAGGER");
            await tagger.Connection.SendAsync($"GAME|ATTACK|{r2.Id}");
            await All($"SERVER|GAME|ATTACK|{tagger.Id}", r1, r2);
            await tagger.ExpectAsync("SERVER|GAME|ERROR|OUT_OF_RANGE");
            await Expect(tagger, $"GAME|ATTACK|{r1.Id}", "SERVER|GAME|ERROR|COOLDOWN");
            clock.Advance(TimeSpan.FromSeconds(1.7));
            await Expect(tagger, $"GAME|ATTACK|{r1.Id}", "SERVER|GAME|ERROR|COOLDOWN");
            clock.Advance(TimeSpan.FromSeconds(0.2)); // 1.9초: 지연 여유 0.2초 안이라 허용한다.
            await tagger.Connection.SendAsync($"GAME|ATTACK|{tagger.Id}");
            await All($"SERVER|GAME|ATTACK|{tagger.Id}", r1, r2);
            await tagger.ExpectAsync("SERVER|GAME|ERROR|INVALID_TARGET");
            clock.Advance(TimeSpan.FromSeconds(2));
            await tagger.Connection.SendAsync("GAME|ATTACK|999999");
            await All($"SERVER|GAME|ATTACK|{tagger.Id}", r1, r2);
            await tagger.ExpectAsync("SERVER|GAME|ERROR|INVALID_TARGET");
            await Expect(r2, "GAME|DIE", "SERVER|GAME|ERROR|NOT_ALLOWED");
            await Expect(tagger, "GAME|MISSION", "SERVER|GAME|ERROR|NOT_RUNNER");
            await Quiet(a, b, c);
            pass("도망자 대상 공격 거부(NOT_TAGGER) / 술래 쿨타임·거리·자기·없는 대상 거부 / AI 사망(DIE)·술래 미션 거부");

            clock.Advance(TimeSpan.FromSeconds(2));
            await tagger.Connection.SendAsync($"GAME|ATTACK|{r1.Id}");
            await All($"SERVER|GAME|ATTACK|{tagger.Id}", r1, r2);
            await All($"SERVER|GAME|KILL|{game1}|{tagger.Id}|{r1.Id}|2", a, b, c); // 미완료 도망자가 남아 계속 진행
            foreach (string command in new[] { "GAME|MOVE|3|0|2|0|0", "GAME|ATTACK", "GAME|JUMP", "GAME|DIE", "GAME|MISSION" })
                await Expect(r1, command, "SERVER|GAME|ERROR|DEAD");
            // 잡힌 플레이어도 AI 호스트이면 AI 위치를 계속 보낸다.
            if (r1 == a)
            {
                await a.Connection.SendAsync("GAME|AI|0:0:0:0:0:0:0");
                await All($"SERVER|GAME|AI|{game1}|0:0:0:0:0:0:0", b, c);
            }
            else await Expect(r1, "GAME|AI|0:0:0:0:0:0:0", "SERVER|GAME|ERROR|NOT_AI_HOST");
            clock.Advance(TimeSpan.FromSeconds(2));
            await tagger.Connection.SendAsync($"GAME|ATTACK|{r1.Id}"); // 이미 잡힌 대상
            await All($"SERVER|GAME|ATTACK|{tagger.Id}", r1, r2);
            await tagger.ExpectAsync("SERVER|GAME|ERROR|INVALID_TARGET");
            clock.Advance(TimeSpan.FromSeconds(2));
            await tagger.Connection.SendAsync("GAME|ATTACK"); // 빗나감·AI 공격은 동작만 중계한다.
            await All($"SERVER|GAME|ATTACK|{tagger.Id}", r1, r2);
            await Quiet(a, b, c);
            pass("술래가 도망자 잡기 KILL 통지 / 잡힌 도망자 명령 거부 / 시간 보상 없음");

            await r2.Connection.SendAsync("GAME|MISSION");
            await All($"SERVER|GAME|MISSION|{game1}|{r2.Id}|1|1", a, b, c);
            await All($"SERVER|GAME|END|{game1}|0|RUNNER|MISSION_CLEAR", a, b, c);
            await Expect(a, "GAME|MOVE|0|0|0|0|0", "SERVER|GAME|ERROR|NOT_STARTED");
            await rooms.TickClockAsync(); // 끝난 게임에는 TIME을 보내지 않는다.
            await Expect(d, "ROOM|JOIN|1", "SERVER|ROOM|JOINED|1");
            await Quiet(a, b, c, d);
            pass("생존 도망자 전원 미션 완료 → RUNNER 승리 END / 종료 후 대기 상태·재입장");

            var (game2, tagger2, runners2, _) = await Start(d, a, b, c, d);
            Check(game2 > game1, "게임 번호 증가 오류");
            Peer[] everyone2 = [a, b, c, d];
            clock.Advance(TimeSpan.FromSeconds(0.5));
            await rooms.TickClockAsync(); // 299.5초 → 표시 300초 그대로라 보내지 않는다.
            await Quiet(everyone2);
            clock.Advance(TimeSpan.FromSeconds(1));
            await rooms.TickClockAsync();
            await All($"SERVER|GAME|TIME|{game2}|299", everyone2);
            await rooms.TickClockAsync(); // 같은 초는 다시 보내지 않는다.
            await Quiet(everyone2);

            Peer q0 = runners2[0], q1 = runners2[1], q2 = runners2[2];
            await q0.Connection.SendAsync("GAME|MISSION");
            await All($"SERVER|GAME|MISSION|{game2}|{q0.Id}|1|3", everyone2);
            await Expect(q0, "GAME|MISSION", "SERVER|GAME|ERROR|ALREADY_DONE");
            q0.Socket.LingerState = new LingerOption(true, 0); // RST 종료
            q0.Dispose();
            int aiHost2 = a.Id; // 가장 작은 ID
            await All($"SERVER|GAME|OUT|{game2}|{q0.Id}|3", tagger2, q1, q2);
            if (q0.Id == aiHost2)
            {
                aiHost2 = new[] { tagger2, q1, q2 }.Min(p => p.Id);
                await All($"SERVER|GAME|AIHOST|{game2}|{aiHost2}", tagger2, q1, q2);
            }
            await Expect(q1, "ROOM|LEAVE", "SERVER|ROOM|LEFT|1");
            await All($"SERVER|GAME|OUT|{game2}|{q1.Id}|2", tagger2, q2);
            if (q1.Id == aiHost2)
            {
                aiHost2 = Math.Min(tagger2.Id, q2.Id);
                await All($"SERVER|GAME|AIHOST|{game2}|{aiHost2}", tagger2, q2);
            }
            Peer host2 = tagger2.Id == aiHost2 ? tagger2 : q2;
            Peer other2 = host2 == tagger2 ? q2 : tagger2;
            await host2.Connection.SendAsync("GAME|AI|5:1:0:1:0:0:0");
            await other2.ExpectAsync($"SERVER|GAME|AI|{game2}|5:1:0:1:0:0:0");
            await rooms.WaitForNoticesAsync().WaitAsync(TestTimeout);
            clock.Advance(TimeSpan.FromSeconds(300));
            await rooms.TickClockAsync();
            await All($"SERVER|GAME|TIME|{game2}|0", tagger2, q2);
            await All($"SERVER|GAME|END|{game2}|{tagger2.Id}|TAGGER|TIME_OVER", tagger2, q2);
            await Expect(tagger2, "ROOM|MEMBERS", $"SERVER|ROOM|MEMBERS|1|{string.Join(",", new[] { tagger2.Id, q2.Id }.Order())}");
            await Quiet(tagger2, q1, q2);
            pass("남은 시간 TIME 통지(초가 바뀔 때만) / 미션 완료 통지·중복 거부 / RST·LEAVE 탈락 / 시간 초과 TAGGER 승리");

            await Expect(q1, "ROOM|JOIN|1", "SERVER|ROOM|JOINED|1");
            var (game3, tagger3, runners3, _) = await Start(q1, tagger2, q1, q2);
            tagger3.Dispose(); // 술래 FIN 종료
            await All($"SERVER|GAME|OUT|{game3}|{tagger3.Id}|2", runners3);
            await All($"SERVER|GAME|END|{game3}|0|RUNNER|TAGGER_LEFT", runners3);
            await rooms.WaitForNoticesAsync().WaitAsync(TestTimeout);
            await Quiet(runners3);
            pass("게임 중 술래 FIN 종료 → OUT 통지 / RUNNER 승리");

            var (game4, tagger4, runners4, _) = await Start(runners3[0], runners3);
            Peer last = runners4[0];
            await tagger4.Connection.SendAsync("GAME|MOVE|0|0|0|0|0");
            await last.ExpectAsync($"SERVER|GAME|MOVE|{tagger4.Id}|0|0|0|0|0");
            await last.Connection.SendAsync("GAME|MOVE|1|0|0|0|0");
            await tagger4.ExpectAsync($"SERVER|GAME|MOVE|{last.Id}|1|0|0|0|0");
            await tagger4.Connection.SendAsync($"GAME|ATTACK|{last.Id}");
            await last.ExpectAsync($"SERVER|GAME|ATTACK|{tagger4.Id}");
            await All($"SERVER|GAME|KILL|{game4}|{tagger4.Id}|{last.Id}|1", tagger4, last);
            await All($"SERVER|GAME|END|{game4}|{tagger4.Id}|TAGGER|ALL_CAUGHT", tagger4, last);
            await Quiet(tagger4, last);
            pass("모든 도망자 잡힘 → TAGGER 승리 END / 방 대기 상태 복귀");

            var (game5, _, _, _) = await Start(tagger4, tagger4, last);
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
            Check(game5 > game4 && game4 > game3 && game3 > game2, "게임 번호 재사용");
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
