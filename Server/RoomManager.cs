/*
[ 코드 설명 ]

RoomManager

1. 방과 플레이어 소속을 단일 잠금으로 관리한다.
2. 방 생성, 입장, 퇴장과 멤버 조회 명령을 처리한다.
3. 같은 방의 수신자 스냅샷에 메시지를 전달한다.
4. 연결 종료 시 소속과 빈 방을 동기적으로 정리한다.
5. GAME| 명령은 같은 잠금 안에서 RoomManager.Game.cs(C 게임 로직)로 분배한다.
*/

using System.Globalization;

public sealed partial class RoomManager
{
    private readonly GameServer server;
    private readonly TimeProvider time;
    private readonly object stateLock = new();
    // 아래 자료구조는 반드시 같은 잠금 안에서 함께 변경한다.
    private readonly Dictionary<int, HashSet<int>> rooms = new();
    private readonly Dictionary<int, int> memberships = new();
    private int nextRoomId;
    public const int MaxMembers = 4;

    // 잠금 안에서 결정한 송신 내용이다. 실제 전송은 잠금 밖에서 순서대로 한다.
    private readonly record struct Outgoing(string Message, int[] Recipients);

    // 시간은 공격 쿨타임과 이동 속도 검사에 쓰며, 검사에서는 수동 시계를 넣을 수 있다.
    public RoomManager(GameServer server, TimeProvider? time = null)
    {
        this.server = server;
        this.time = time ?? TimeProvider.System;
    }

    public async Task HandleMessageAsync(int clientId, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<Outgoing> outgoing;
        lock (stateLock)
        {
            // 전송은 이 잠금 밖에서 한다. 본문은 분할하지 않아 한글과 |를 보존한다.
            outgoing = ProcessLocked(clientId, message);
        }
        // 한 명령이 만든 메시지(예: KILL 다음 END)는 만든 순서대로 보낸다.
        foreach (Outgoing item in outgoing)
            await server.SendToClientsAsync(item.Recipients, item.Message, cancellationToken);
    }

    private static List<Outgoing> Single(int clientId, string message) => [new(message, [clientId])];

    private List<Outgoing> ProcessLocked(int clientId, string message)
    {
        List<Outgoing> Reply(string value) => Single(clientId, value);
        List<Outgoing> Error(string code) => Reply($"SERVER|ROOM|ERROR|{code}");
        if (message == "GAME" || message.StartsWith("GAME|", StringComparison.Ordinal))
            return ProcessGameLocked(clientId, message);
        if (message != "ROOM" && !message.StartsWith("ROOM|", StringComparison.Ordinal))
            return Reply($"SERVER|RECEIVED|{message}");

        bool joined = memberships.TryGetValue(clientId, out int roomId);
        if (message == "ROOM|CREATE")
        {
            if (joined) return Error("ALREADY_IN_ROOM");
            // 오버플로 시 ID를 재사용하지 않고 실제 프로그램 오류로 보고한다.
            int created = checked(++nextRoomId);
            rooms.Add(created, new HashSet<int> { clientId });
            memberships.Add(clientId, created);
            return Reply($"SERVER|ROOM|CREATED|{created}");
        }
        if (message.StartsWith("ROOM|JOIN|", StringComparison.Ordinal))
        {
            if (!int.TryParse(message.AsSpan(10), NumberStyles.None, CultureInfo.InvariantCulture, out int target) || target <= 0)
                return Error("INVALID_ROOM_ID");
            if (joined) return Error("ALREADY_IN_ROOM");
            if (!rooms.TryGetValue(target, out HashSet<int>? members)) return Error("ROOM_NOT_FOUND");
            // 진행 중인 게임에는 중간 참가할 수 없다.
            if (games.ContainsKey(target)) return Error("GAME_IN_PROGRESS");
            // 정원 확인과 추가는 원자적으로 실행한다.
            if (members.Count >= MaxMembers) return Error("ROOM_FULL");
            members.Add(clientId);
            memberships.Add(clientId, target);
            return Reply($"SERVER|ROOM|JOINED|{target}");
        }
        if (message == "ROOM|LEAVE")
        {
            if (!joined) return Error("NOT_IN_ROOM");
            List<Outgoing> result = Reply($"SERVER|ROOM|LEFT|{roomId}");
            RemoveLocked(clientId, result); // 게임 중이면 남은 사람에게 OUT/END가 뒤에 붙는다.
            return result;
        }
        if (message == "ROOM|MEMBERS")
        {
            if (!joined) return Error("NOT_IN_ROOM");
            return Reply($"SERVER|ROOM|MEMBERS|{roomId}|{string.Join(",", rooms[roomId].Order())}");
        }
        if (message.StartsWith("ROOM|SAY|", StringComparison.Ordinal))
        {
            string body = message[9..];
            if (string.IsNullOrWhiteSpace(body)) return Error("EMPTY_MESSAGE");
            if (!joined) return Error("NOT_IN_ROOM");
            // 송신자는 본문이 아닌 서버가 전달한 clientId로 결정한다.
            return [new($"SERVER|ROOM|MESSAGE|{roomId}|{clientId}|{body}", rooms[roomId].ToArray())];
        }
        return Error("INVALID_COMMAND");
    }

    public void HandleClientDisconnected(int clientId)
    {
        var notices = new List<Outgoing>();
        lock (stateLock)
        {
            RemoveLocked(clientId, notices); // 퇴장 직후의 종료와 중복 종료도 안전하다.
        }
        // 게임 중 종료이면 남은 플레이어에게 OUT/END를 알린다. 이벤트는 동기이므로 송신은 추적 작업으로 넘긴다.
        if (notices.Count > 0) TrackNotices(notices);
    }

    private void RemoveLocked(int clientId, List<Outgoing> outgoing)
    {
        if (!memberships.Remove(clientId, out int roomId)) return;
        HashSet<int> members = rooms[roomId];
        members.Remove(clientId);
        LeaveGameLocked(roomId, clientId, members, outgoing);
        if (members.Count == 0)
        {
            rooms.Remove(roomId);
            games.Remove(roomId);
        }
    }
}
