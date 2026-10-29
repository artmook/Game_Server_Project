/*
[ 코드 설명 ]

RoomManager

1. 방과 플레이어 소속을 단일 잠금으로 관리한다.
2. 방 생성, 입장, 퇴장과 멤버 조회 명령을 처리한다.
3. 같은 방의 수신자 스냅샷에 메시지를 전달한다.
4. 연결 종료 시 소속과 빈 방을 동기적으로 정리한다.
*/

using System.Globalization;

public sealed class RoomManager
{
    private readonly GameServer server;
    private readonly object stateLock = new();
    // 두 자료구조는 반드시 같은 잠금 안에서 함께 변경한다.
    private readonly Dictionary<int, HashSet<int>> rooms = new();
    private readonly Dictionary<int, int> memberships = new();
    private int nextRoomId;
    public const int MaxMembers = 4;

    public RoomManager(GameServer server) => this.server = server;

    public async Task HandleMessageAsync(int clientId, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string response;
        int[]? recipients = null;
        lock (stateLock)
        {
            // 전송은 이 잠금 밖에서 한다. 본문은 분할하지 않아 한글과 |를 보존한다.
            (response, recipients) = ProcessLocked(clientId, message);
        }
        if (recipients is null)
            await server.SendToClientAsync(clientId, response, cancellationToken);
        else
            await server.SendToClientsAsync(recipients, response, cancellationToken);
    }

    private (string Response, int[]? Recipients) ProcessLocked(int clientId, string message)
    {
        (string, int[]?) Reply(string value) => (value, null);
        (string, int[]?) Error(string code) => Reply($"SERVER|ROOM|ERROR|{code}");
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
            // 정원 확인과 추가는 원자적으로 실행한다.
            if (members.Count >= MaxMembers) return Error("ROOM_FULL");
            members.Add(clientId);
            memberships.Add(clientId, target);
            return Reply($"SERVER|ROOM|JOINED|{target}");
        }
        if (message == "ROOM|LEAVE")
        {
            if (!joined) return Error("NOT_IN_ROOM");
            RemoveLocked(clientId);
            return Reply($"SERVER|ROOM|LEFT|{roomId}");
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
            return ($"SERVER|ROOM|MESSAGE|{roomId}|{clientId}|{body}", rooms[roomId].ToArray());
        }
        return Error("INVALID_COMMAND");
    }

    public void HandleClientDisconnected(int clientId)
    {
        lock (stateLock)
        {
            RemoveLocked(clientId); // 퇴장 직후의 종료와 중복 종료도 안전하다.
        }
    }

    private void RemoveLocked(int clientId)
    {
        if (!memberships.Remove(clientId, out int roomId)) return;
        HashSet<int> members = rooms[roomId];
        members.Remove(clientId);
        if (members.Count == 0) rooms.Remove(roomId);
    }
}
