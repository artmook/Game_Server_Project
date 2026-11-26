/*
[ 코드 설명 ]

RoomManager.Game (C 게임 로직)

1. 방 단위로 게임 시작, 이동·행동 중계, 공격 판정, 승패를 처리한다.
2. 모든 상태 변경은 RoomManager의 같은 잠금 안에서 하고, 송신은 잠금 밖에서 한다.
3. 공격은 서버가 쿨타임, 생존 여부, 마지막 위치 기준 거리로 판정한다.
4. 게임 중 퇴장·접속 종료는 탈락으로 처리하고 승패를 다시 판정한다.
5. 술래잡기 규칙: 게임 시작 때 서버가 술래 1명을 뽑아 STARTED에 넣는다. 나머지는 도망자이다.
   - 술래의 공격만 도망자를 잡는다. 도망자의 대상 공격은 NOT_TAGGER로 거부한다.
   - 도망자는 미션을 모두 끝내면 GAME|MISSION을 보낸다. 서버가 완료 상태를 관리한다.
   - 제한시간 5분은 서버 시계로 잰다. RunClockAsync가 남은 초가 바뀔 때마다 TIME을 보낸다.
     잡아도 시간은 늘지 않는다.
   - 승패: 살아 있는 도망자가 없으면 TAGGER, 살아 있는 도망자가 모두 미션을 끝내면 RUNNER,
     시간이 다 되면 TAGGER, 술래가 나가면 RUNNER 승리이다.
6. AI 동기화(호스트 권한): 게임에 남은 가장 작은 ID가 AI 호스트이다(STARTED 끝, 바뀌면 AIHOST로 알림).
   호스트만 GAME|AI로 AI 위치 묶음을 보낼 수 있고 서버는 나머지에게 중계한다.
   술래가 AI를 때리면 GAME|AISTUN|번호를 보내고, 서버가 다른 사람에게 알려 모든 화면에서 기절시킨다.

AI는 각 클라이언트가 시드로 같은 위치에 배치한 뒤 자기 화면에서만 움직인다.
서버는 AI 위치를 모르므로 플레이어끼리의 공격만 판정한다.
*/

using System.Globalization;

public sealed partial class RoomManager
{
    public const int MinGamePlayers = 2;
    public const int ActionSlotCount = 4;            // Unity CharacterAction.SlotCount
    public static readonly TimeSpan AttackCooldown = TimeSpan.FromSeconds(2); // Unity CharacterCombat.cooldown
    // 같은 2초 간격으로 보낸 공격도 네트워크 지연 차이로 조금 빨리 도착할 수 있다.
    public static readonly TimeSpan AttackCooldownTolerance = TimeSpan.FromMilliseconds(200);
    public const double AttackRange = 3.0;           // Unity CharacterCombat.attackRange
    // 위치는 주기적으로만 보고되고, Unity는 몸 중심이 아닌 맞은 표면까지 거리를 잰다.
    public const double AttackRangeTolerance = 1.5;
    public const double MaxPlanarSpeed = 8.0;        // 달리기 5m/s + 밀림 여유
    public const double MoveDistanceTolerance = 1.5;
    public const double MaxCoordinate = 10000.0;
    public const double MaxReportedSpeed = 20.0;
    public static readonly TimeSpan GameTimeLimit = TimeSpan.FromMinutes(5); // 잡아도 늘지 않는다.
    public static readonly TimeSpan ClockInterval = TimeSpan.FromMilliseconds(200);
    public const int MaxAiIndex = 999;
    public const int MaxAiPayload = 4000;            // 입력 4096바이트 안 (명령 접두사 포함)
    public const string RunnerTeam = "RUNNER";
    public const string TaggerTeam = "TAGGER";

    // 방 ID → 진행 중인 게임. 게임이 끝나면 제거되어 방은 대기 상태로 돌아간다.
    private readonly Dictionary<int, RoomGame> games = new();
    private int nextGameNumber;

    private readonly object noticeLock = new();
    private readonly HashSet<Task> pendingNotices = new();

    private sealed class RoomGame(int number, int taggerId, long startedAt)
    {
        public int Number { get; } = number;
        public int TaggerId { get; } = taggerId;
        public long StartedAt { get; } = startedAt;
        public int LastSentSeconds { get; set; } = (int)GameTimeLimit.TotalSeconds; // STARTED가 처음 값을 알린다.
        public int AiHostId { get; set; }   // AI를 계산해 위치를 보내는 클라이언트 (게임에 남은 가장 작은 ID)
        public HashSet<int> Alive { get; } = new();
        public HashSet<int> MissionDone { get; } = new();   // 미션을 모두 끝낸 도망자
        public Dictionary<int, PlayerState> Players { get; } = new();

        public IEnumerable<int> AliveRunners => Alive.Where(id => id != TaggerId);
    }

    private sealed class PlayerState
    {
        public bool HasPosition;
        public double X, Y, Z;
        public long MovedAt;
        public long? AttackedAt;
    }

    private List<Outgoing> ProcessGameLocked(int clientId, string message)
    {
        List<Outgoing> Error(string code) => Single(clientId, $"SERVER|GAME|ERROR|{code}");
        string[] parts = message.Split('|');
        string command = parts.Length > 1 ? parts[1] : "";
        int args = parts.Length - 2;
        bool validShape = command switch
        {
            "START" or "JUMP" or "DIE" or "MISSION" => args == 0,
            "MOVE" => args == 5,
            "AI" or "AISTUN" => args == 1,
            "ACTION" => args == 1,
            "ATTACK" => args is 0 or 1,
            _ => false,
        };
        if (!validShape) return Error("INVALID_COMMAND");
        if (!memberships.TryGetValue(clientId, out int roomId)) return Error("NOT_IN_ROOM");

        HashSet<int> members = rooms[roomId];
        games.TryGetValue(roomId, out RoomGame? game);
        if (command == "START") return StartGameLocked(clientId, roomId, members, game);
        if (game is null) return Error("NOT_STARTED");
        // AI 위치는 잡힌 플레이어도 호스트라면 계속 보낸다. 그래서 생존 검사보다 먼저 처리한다.
        if (command == "AI") return RelayAiLocked(clientId, parts[2], game, members);
        if (!game.Alive.Contains(clientId)) return Error("DEAD");

        PlayerState self = game.Players[clientId];
        int[] others = members.Where(id => id != clientId).ToArray();
        long now = time.GetTimestamp();

        switch (command)
        {
            case "MOVE":
            {
                double[] values = new double[5];
                for (int i = 0; i < values.Length; i++)
                {
                    if (!TryParseNumber(parts[i + 2], out values[i])) return Error("INVALID_VALUE");
                }
                (double x, double y, double z, double rotation, double speed) =
                    (values[0], values[1], values[2], values[3], values[4]);
                if (Math.Abs(x) > MaxCoordinate || Math.Abs(y) > MaxCoordinate || Math.Abs(z) > MaxCoordinate ||
                    speed < 0 || speed > MaxReportedSpeed)
                    return Error("INVALID_VALUE");
                if (self.HasPosition)
                {
                    // 높이는 낙하가 있어 검사하지 않고, 바닥 평면 이동 거리만 경과 시간과 비교한다.
                    double elapsed = time.GetElapsedTime(self.MovedAt, now).TotalSeconds;
                    double planar = Math.Sqrt((x - self.X) * (x - self.X) + (z - self.Z) * (z - self.Z));
                    if (planar > MaxPlanarSpeed * elapsed + MoveDistanceTolerance) return Error("TOO_FAST");
                }
                self.HasPosition = true;
                (self.X, self.Y, self.Z, self.MovedAt) = (x, y, z, now);
                rotation %= 360.0;
                if (rotation < 0) rotation += 360.0;
                return [new($"SERVER|GAME|MOVE|{clientId}|{Format(x)}|{Format(y)}|{Format(z)}|{Format(rotation)}|{Format(speed)}", others)];
            }
            case "ACTION":
            {
                if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int slot) ||
                    slot < 1 || slot > ActionSlotCount)
                    return Error("INVALID_VALUE");
                return [new($"SERVER|GAME|ACTION|{clientId}|{slot}", others)];
            }
            case "AISTUN":
            {
                // 술래가 자기 화면에서 AI를 때렸다. 다른 화면(특히 AI 호스트)도 그 AI를 기절시킨다.
                if (clientId != game.TaggerId) return Error("NOT_TAGGER");
                if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                    index > MaxAiIndex)
                    return Error("INVALID_VALUE");
                return [new($"SERVER|GAME|AISTUN|{game.Number}|{index}", others)];
            }
            case "JUMP":
                return [new($"SERVER|GAME|JUMP|{clientId}", others)];
            case "DIE":
                // 술래잡기에서는 술래의 공격만 도망자를 잡는다. 자기 화면의 AI 피격 사망은 받지 않는다.
                return Error("NOT_ALLOWED");
            case "MISSION":
            {
                // 도망자가 자기 미션을 모두 끝냈다. 완료 상태는 서버가 갖고 승패도 서버가 판정한다.
                if (clientId == game.TaggerId) return Error("NOT_RUNNER");
                if (!game.MissionDone.Add(clientId)) return Error("ALREADY_DONE");
                int[] everyone = members.ToArray();
                var result = new List<Outgoing> { new(MissionMessage(game, clientId), everyone) };
                EndIfFinishedLocked(roomId, game, everyone, result);
                return result;
            }
            default: // ATTACK
            {
                if (self.AttackedAt is long last &&
                    time.GetElapsedTime(last, now) < AttackCooldown - AttackCooldownTolerance)
                    return Error("COOLDOWN");
                self.AttackedAt = now;
                // 빗나감·AI 공격도 다른 화면에서 공격 동작이 보이도록 중계한다.
                var result = new List<Outgoing> { new($"SERVER|GAME|ATTACK|{clientId}", others) };
                if (args == 0) return result;

                void Reject(string code) => result.Add(new($"SERVER|GAME|ERROR|{code}", [clientId]));
                // 도망자는 다른 도망자를 잡을 수 없다. 공격 동작은 위에서 중계해 AI 흉내는 그대로 보인다.
                if (clientId != game.TaggerId)
                {
                    Reject("NOT_TAGGER");
                    return result;
                }
                if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int target) ||
                    target == clientId || !game.Alive.Contains(target))
                {
                    Reject("INVALID_TARGET");
                    return result;
                }
                PlayerState victim = game.Players[target];
                if (!self.HasPosition || !victim.HasPosition || Distance(self, victim) > AttackRange + AttackRangeTolerance)
                {
                    Reject("OUT_OF_RANGE");
                    return result;
                }
                EliminateLocked(roomId, game, members, target, clientId, result);
                return result;
            }
        }
    }

    private List<Outgoing> StartGameLocked(int clientId, int roomId, HashSet<int> members, RoomGame? current)
    {
        if (current is not null) return Single(clientId, "SERVER|GAME|ERROR|ALREADY_STARTED");
        if (members.Count < MinGamePlayers) return Single(clientId, "SERVER|GAME|ERROR|NOT_ENOUGH_PLAYERS");

        int[] ids = members.Order().ToArray();
        // 술래는 서버가 뽑는다. 모든 클라이언트는 STARTED의 술래 ID를 그대로 쓴다.
        int tagger = ids[Random.Shared.Next(ids.Length)];
        var game = new RoomGame(checked(++nextGameNumber), tagger, time.GetTimestamp());
        int[] spawns = Enumerable.Range(0, ids.Length).ToArray();
        Random.Shared.Shuffle(spawns);
        int seed = Random.Shared.Next(1, int.MaxValue);
        foreach (int id in ids)
        {
            game.Alive.Add(id);
            game.Players.Add(id, new PlayerState());
        }
        game.AiHostId = ids[0];
        games.Add(roomId, game);
        string spawnList = string.Join(",", ids.Select((id, i) => $"{id}:{spawns[i]}"));
        // 기존 필드 뒤에 술래 ID와 제한시간(초)을 붙인다. 앞 필드만 읽는 클라이언트도 그대로 동작한다.
        return [new($"SERVER|GAME|STARTED|{roomId}|{game.Number}|{seed}|{spawnList}|{tagger}|{game.LastSentSeconds}|{game.AiHostId}", ids)];
    }

    // AI 호스트의 위치 묶음을 나머지에게 그대로 전한다. 서버는 AI를 계산하지 않고 송신자와 형식만 검사한다.
    // 묶음: "번호:x:y:z:회전:속도:기절(0/1)"을 쉼표로 이은 것. 숫자·부호·점·콜론·쉼표만 허용한다.
    private List<Outgoing> RelayAiLocked(int clientId, string payload, RoomGame game, HashSet<int> members)
    {
        if (clientId != game.AiHostId) return Single(clientId, "SERVER|GAME|ERROR|NOT_AI_HOST");
        if (payload.Length == 0 || payload.Length > MaxAiPayload ||
            payload.Any(c => !(char.IsAsciiDigit(c) || c is '.' or '-' or ':' or ',')))
            return Single(clientId, "SERVER|GAME|ERROR|INVALID_VALUE");
        int[] others = members.Where(id => id != clientId).ToArray();
        return [new($"SERVER|GAME|AI|{game.Number}|{payload}", others)];
    }

    // 게임에서 AI 호스트가 나가면 남은 가장 작은 ID에게 넘기고 알린다.
    private static void ReassignAiHostLocked(RoomGame game, int[] everyone, List<Outgoing> outgoing)
    {
        if (game.Players.Count == 0) return;
        game.AiHostId = game.Players.Keys.Min();
        if (everyone.Length > 0)
            outgoing.Add(new($"SERVER|GAME|AIHOST|{game.Number}|{game.AiHostId}", everyone));
    }

    // SERVER|GAME|MISSION|{게임번호}|{완료한 ID}|{완료한 생존 도망자 수}|{생존 도망자 수}
    private static string MissionMessage(RoomGame game, int clientId)
    {
        int runners = 0, done = 0;
        foreach (int id in game.AliveRunners)
        {
            runners++;
            if (game.MissionDone.Contains(id)) done++;
        }
        return $"SERVER|GAME|MISSION|{game.Number}|{clientId}|{done}|{runners}";
    }

    private void EliminateLocked(int roomId, RoomGame game, HashSet<int> members, int target, int attacker, List<Outgoing> outgoing)
    {
        if (!game.Alive.Remove(target)) return;
        int[] everyone = members.ToArray();
        outgoing.Add(new($"SERVER|GAME|KILL|{game.Number}|{attacker}|{target}|{game.Alive.Count}", everyone));
        EndIfFinishedLocked(roomId, game, everyone, outgoing);
    }

    // RemoveLocked가 방에서 뺀 뒤 호출한다. members에는 떠난 사람이 없다.
    private void LeaveGameLocked(int roomId, int clientId, HashSet<int> members, List<Outgoing> outgoing)
    {
        if (!games.TryGetValue(roomId, out RoomGame? game)) return;
        game.Players.Remove(clientId);
        int[] everyone = members.ToArray();
        bool hostLeft = game.AiHostId == clientId;
        if (game.Alive.Remove(clientId)) // 이미 죽은 사람이 나간 경우 판정은 그대로이다.
        {
            if (everyone.Length > 0)
                outgoing.Add(new($"SERVER|GAME|OUT|{game.Number}|{clientId}|{game.Alive.Count}", everyone));
            EndIfFinishedLocked(roomId, game, everyone, outgoing);
        }
        // 게임이 계속되는데 AI 호스트가 나갔으면 다음 사람에게 넘긴다.
        if (hostLeft && games.ContainsKey(roomId)) ReassignAiHostLocked(game, everyone, outgoing);
    }

    // 승패 판정. 끝났으면 SERVER|GAME|END|{게임번호}|{승자ID}|{승리 팀}|{이유}를 붙이고 방을 대기 상태로 돌린다.
    // 승자ID는 기존 필드 자리를 유지한다. TAGGER 승리면 술래 ID, RUNNER 승리면 여러 명이라 0이다.
    private void EndIfFinishedLocked(int roomId, RoomGame game, int[] everyone, List<Outgoing> outgoing, bool timeUp = false)
    {
        string? team = null, reason = null;
        if (!game.Alive.Contains(game.TaggerId))
        {
            (team, reason) = (RunnerTeam, "TAGGER_LEFT");
        }
        else
        {
            int runners = 0, done = 0;
            foreach (int id in game.AliveRunners)
            {
                runners++;
                if (game.MissionDone.Contains(id)) done++;
            }
            if (runners == 0) (team, reason) = (TaggerTeam, "ALL_CAUGHT");
            else if (done == runners) (team, reason) = (RunnerTeam, "MISSION_CLEAR");
            else if (timeUp) (team, reason) = (TaggerTeam, "TIME_OVER");
        }
        if (team is null) return;

        int winner = team == TaggerTeam ? game.TaggerId : 0;
        games.Remove(roomId);
        if (everyone.Length > 0)
            outgoing.Add(new($"SERVER|GAME|END|{game.Number}|{winner}|{team}|{reason}", everyone));
    }

    // ───────────── 제한시간 ─────────────

    // 서버 실행 동안 돌리는 게임 시계이다. Program.cs가 서버와 함께 시작하고 종료 때 기다린다.
    public async Task RunClockAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(ClockInterval, time, cancellationToken);
                try
                {
                    await TickClockAsync(cancellationToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Console.WriteLine($"[GAME CLOCK ERROR] {e}"); // 한 번의 실패로 시계를 멈추지 않는다.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    // 진행 중인 모든 게임의 남은 시간을 계산해, 남은 초가 바뀐 게임에 TIME을 보내고 0초면 종료한다.
    // 검사에서는 수동 시계를 움직인 뒤 직접 호출한다.
    public async Task TickClockAsync(CancellationToken cancellationToken = default)
    {
        var outgoing = new List<Outgoing>();
        lock (stateLock)
        {
            long now = time.GetTimestamp();
            foreach ((int roomId, RoomGame game) in games.ToArray())
            {
                TimeSpan left = GameTimeLimit - time.GetElapsedTime(game.StartedAt, now);
                int seconds = left <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(left.TotalSeconds);
                if (seconds == game.LastSentSeconds && seconds > 0) continue;
                int[] everyone = rooms[roomId].ToArray();
                if (seconds != game.LastSentSeconds)
                {
                    game.LastSentSeconds = seconds;
                    outgoing.Add(new($"SERVER|GAME|TIME|{game.Number}|{seconds}", everyone));
                }
                if (seconds == 0) EndIfFinishedLocked(roomId, game, everyone, outgoing, timeUp: true);
            }
        }
        foreach (Outgoing item in outgoing)
            await server.SendToClientsAsync(item.Recipients, item.Message, cancellationToken);
    }

    private static double Distance(PlayerState a, PlayerState b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // Unity 쪽은 float.ToString(CultureInfo.InvariantCulture)로 보내야 한다(소수점은 항상 '.').
    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                        CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    // 접속 종료 이벤트는 동기이므로 OUT/END 송신을 추적 작업으로 실행한다.
    // 예외는 작업 안에서 모두 기록하며, 서버 종료 후 WaitForNoticesAsync로 끝까지 기다린다.
    private void TrackNotices(List<Outgoing> notices)
    {
        lock (noticeLock)
        {
            pendingNotices.RemoveWhere(task => task.IsCompleted);
            pendingNotices.Add(SendNoticesAsync(notices));
        }
    }

    private async Task SendNoticesAsync(List<Outgoing> notices)
    {
        await Task.Yield(); // 종료 콜백(수신 작업의 finally)을 송신 동안 붙잡지 않는다.
        try
        {
            foreach (Outgoing item in notices)
                await server.SendToClientsAsync(item.Recipients, item.Message);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[GAME NOTICE ERROR] {e}");
        }
    }

    public async Task WaitForNoticesAsync()
    {
        while (true)
        {
            Task[] snapshot;
            lock (noticeLock)
            {
                pendingNotices.RemoveWhere(task => task.IsCompleted);
                snapshot = pendingNotices.ToArray();
            }
            if (snapshot.Length == 0) return;
            await Task.WhenAll(snapshot);
        }
    }
}
