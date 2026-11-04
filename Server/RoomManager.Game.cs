/*
[ 코드 설명 ]

RoomManager.Game (C 게임 로직)

1. 방 단위로 게임 시작, 이동·행동 중계, 공격 판정, 승패를 처리한다.
2. 모든 상태 변경은 RoomManager의 같은 잠금 안에서 하고, 송신은 잠금 밖에서 한다.
3. 공격은 서버가 쿨타임, 생존 여부, 마지막 위치 기준 거리로 판정한다.
4. 게임 중 퇴장·접속 종료는 탈락으로 처리하고 승패를 다시 판정한다.

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

    // 방 ID → 진행 중인 게임. 게임이 끝나면 제거되어 방은 대기 상태로 돌아간다.
    private readonly Dictionary<int, RoomGame> games = new();
    private int nextGameNumber;

    private readonly object noticeLock = new();
    private readonly HashSet<Task> pendingNotices = new();

    private sealed class RoomGame(int number)
    {
        public int Number { get; } = number;
        public HashSet<int> Alive { get; } = new();
        public Dictionary<int, PlayerState> Players { get; } = new();
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
            "START" or "JUMP" or "DIE" => args == 0,
            "MOVE" => args == 5,
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
            case "JUMP":
                return [new($"SERVER|GAME|JUMP|{clientId}", others)];
            case "DIE":
            {
                // 자기 화면의 AI에게 맞은 경우이다. 공격자는 0(AI)으로 알린다.
                var result = new List<Outgoing>();
                EliminateLocked(roomId, game, members, clientId, attacker: 0, result);
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

        var game = new RoomGame(checked(++nextGameNumber));
        int[] ids = members.Order().ToArray();
        int[] spawns = Enumerable.Range(0, ids.Length).ToArray();
        Random.Shared.Shuffle(spawns);
        int seed = Random.Shared.Next(1, int.MaxValue);
        foreach (int id in ids)
        {
            game.Alive.Add(id);
            game.Players.Add(id, new PlayerState());
        }
        games.Add(roomId, game);
        string spawnList = string.Join(",", ids.Select((id, i) => $"{id}:{spawns[i]}"));
        return [new($"SERVER|GAME|STARTED|{roomId}|{game.Number}|{seed}|{spawnList}", ids)];
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
        if (!game.Alive.Remove(clientId)) return; // 이미 죽은 사람이 나간 경우 판정은 그대로이다.
        int[] everyone = members.ToArray();
        if (everyone.Length > 0)
            outgoing.Add(new($"SERVER|GAME|OUT|{game.Number}|{clientId}|{game.Alive.Count}", everyone));
        EndIfFinishedLocked(roomId, game, everyone, outgoing);
    }

    private void EndIfFinishedLocked(int roomId, RoomGame game, int[] everyone, List<Outgoing> outgoing)
    {
        if (game.Alive.Count > 1) return;
        int winner = game.Alive.Count == 1 ? game.Alive.First() : 0;
        games.Remove(roomId);
        if (everyone.Length > 0)
            outgoing.Add(new($"SERVER|GAME|END|{game.Number}|{winner}", everyone));
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
