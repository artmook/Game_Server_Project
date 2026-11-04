/*
[ 코드 설명 ]

NetworkGameManager (초안)

1. 서버에 접속해 방 생성·입장·퇴장과 게임 시작을 요청한다.
2. 서버 메시지를 메인 스레드에서 처리해 원격 플레이어를 만들고 움직인다.
3. 내 이동(초당 10회), 행동, 점프, 공격을 서버 규격으로 보낸다.
4. STARTED의 게임 번호를 기억하고, 다른 번호의 KILL/OUT/END는 버린다.
5. 내 화면의 AI에게 맞아 죽으면 GAME|DIE로 서버에 알린다.

플레이어 생사는 서버가 판정한다. 원격 플레이어는 로컬 피격으로 죽지 않고 서버 KILL로만 죽는다.
AI는 시드로 같은 위치에 배치하지만 이후 움직임은 PC마다 다르다.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace GameServer
{
    public class NetworkGameManager : MonoBehaviour
    {
        [Header("Server")]
        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private int port = 7777;
        [SerializeField] private bool connectOnStart = true;

        [Header("References")]
        [SerializeField] private GameManager gameManager;
        [SerializeField] private Character localPlayer;
        [Tooltip("원격 플레이어 외형. AICharacter 프리팹을 그대로 넣으면 AI 조종 부품을 빼고 사용한다.")]
        [SerializeField] private Character remotePlayerPrefab;
        [SerializeField] private Transform remoteRoot;

        [Header("Sync")]
        [SerializeField] private float moveSendInterval = 0.1f;
        [SerializeField] private bool showDebugLobby = true;

        private sealed class RemotePlayer
        {
            public Character Character;
            public CharacterHealth Health;
            public RemoteCommandSource Source;
        }

        private readonly Dictionary<int, RemotePlayer> remotes = new Dictionary<int, RemotePlayer>();
        private readonly Dictionary<CharacterHealth, int> remoteIdsByHealth = new Dictionary<CharacterHealth, int>();
        private readonly List<string> logLines = new List<string>();

        private ServerConnection connection;
        private CharacterHealth localHealth;
        private CharacterMove localMotor;
        private CharacterCombat localCombat;
        private CharacterAction localActions;

        private float moveTimer;
        private bool attackPending;
        private int attackTarget;
        private bool suppressDieReport;
        private string joinInput = "1";
        private static readonly Rect LobbyRect = new Rect(10, 10, 320, 420);

        public string Status { get; private set; } = "미접속";
        public int MyId { get; private set; }
        public int RoomId { get; private set; }
        public int CurrentGameId { get; private set; }   // 0이면 게임 중이 아니다.
        public int LastGameId { get; private set; }      // 지금까지 받은 가장 큰 게임 번호
        public bool InGame => CurrentGameId != 0;
        public bool IsConnected => connection != null && !connection.IsClosed && MyId != 0;

        public event Action<int> GameStarted;        // 게임 번호
        public event Action<int, bool> GameEnded;    // 승자 ID(없으면 0), 내가 이겼는지
        public event Action<int, int, int> PlayerKilled; // 공격자(0 = AI), 대상, 남은 수

        // ───────────── 생명주기 ─────────────

        private void Awake()
        {
            localHealth = localPlayer.GetComponent<CharacterHealth>();
            localMotor = localPlayer.GetComponent<CharacterMove>();
            localCombat = localPlayer.GetComponent<CharacterCombat>();
            localActions = localPlayer.GetComponent<CharacterAction>();
        }

        private void OnEnable()
        {
            localHealth.Died += HandleLocalDied;
            localCombat.AttackPerformed += HandleLocalAttack;
            localCombat.TargetHit += HandleLocalTargetHit;
            localActions.ActionStarted += HandleLocalAction;
            localMotor.Jumped += HandleLocalJump;
        }

        private void OnDisable()
        {
            localHealth.Died -= HandleLocalDied;
            localCombat.AttackPerformed -= HandleLocalAttack;
            localCombat.TargetHit -= HandleLocalTargetHit;
            localActions.ActionStarted -= HandleLocalAction;
            localMotor.Jumped -= HandleLocalJump;
        }

        private void Start()
        {
            // 게임 중에만 마우스를 잠근다. 로비 UI 위를 클릭할 때는 잠그지 않는다.
            CursorController.CanLock = () => InGame && !IsPointerOverLobby();
            if (connectOnStart) Connect();
        }

        private bool IsPointerOverLobby()
        {
            if (!showDebugLobby || Mouse.current == null) return false;
            Vector2 p = Mouse.current.position.ReadValue();
            return LobbyRect.Contains(new Vector2(p.x, Screen.height - p.y)); // GUI 좌표는 위가 0이다.
        }

        private void Update()
        {
            if (connection != null)
            {
                while (connection != null && connection.TryDequeue(out ConnectionEvent e))
                    HandleConnectionEvent(e);
            }

            if (InGame && localHealth.IsAlive)
            {
                moveTimer -= Time.deltaTime;
                if (moveTimer <= 0f)
                {
                    moveTimer = moveSendInterval;
                    SendMove();
                }
            }
        }

        // 공격은 Update에서 AttackPerformed → TargetHit 순서로 끝나므로 프레임 끝에 한 번 보낸다.
        private void LateUpdate()
        {
            if (!attackPending) return;
            attackPending = false;
            if (!InGame || !localHealth.IsAlive) return;
            Send(attackTarget > 0 ? $"GAME|ATTACK|{attackTarget}" : "GAME|ATTACK");
        }

        private void OnDestroy()
        {
            CursorController.CanLock = null;
            connection?.Dispose();
        }
        private void OnApplicationQuit() => connection?.Dispose();

        // ───────────── 요청 API (UI 버튼에서 호출) ─────────────

        public void Connect()
        {
            connection?.Dispose();
            MyId = 0;
            RoomId = 0;
            Status = $"{host}:{port} 접속 중...";
            connection = new ServerConnection();
            connection.Connect(host, port);
        }

        public void CreateRoom() => Send("ROOM|CREATE");
        public void JoinRoom(int roomId) => Send($"ROOM|JOIN|{roomId}");
        public void LeaveRoom() => Send("ROOM|LEAVE");
        public void RequestMembers() => Send("ROOM|MEMBERS");
        public void RequestStartGame() => Send("GAME|START");

        public bool Send(string message)
        {
            if (connection == null || connection.IsClosed) return false;
            try
            {
                return connection.Send(message);
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"[NET] 보낼 수 없는 메시지: {e.Message}");
                return false;
            }
        }

        // ───────────── 서버 메시지 처리 ─────────────

        private void HandleConnectionEvent(ConnectionEvent e)
        {
            switch (e.Type)
            {
                case ConnectionEventType.Connected:
                    Status = "접속됨, ID 대기 중";
                    break;
                case ConnectionEventType.Message:
                    try
                    {
                        HandleMessage(e.Text);
                    }
                    catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is IndexOutOfRangeException)
                    {
                        Debug.LogWarning($"[NET] 해석할 수 없는 서버 메시지: {e.Text} ({ex.Message})");
                    }
                    break;
                case ConnectionEventType.Disconnected:
                    AddLog($"연결 끊김: {e.Text}");
                    if (InGame) CleanupGame();
                    MyId = 0;
                    RoomId = 0;
                    Status = $"연결 끊김 ({e.Text})";
                    break;
            }
        }

        private void HandleMessage(string message)
        {
            string[] p = message.Split('|');
            if (p.Length < 2 || p[0] != "SERVER")
            {
                AddLog(message);
                return;
            }
            switch (p[1])
            {
                case "CONNECTED":
                    MyId = ParseInt(p[2]);
                    Status = $"접속됨 (내 ID {MyId})";
                    break;
                case "ROOM":
                    HandleRoom(p, message);
                    break;
                case "GAME":
                    HandleGame(p, message);
                    break;
                default:
                    AddLog(message); // RECEIVED, BROADCAST 등
                    break;
            }
        }

        private void HandleRoom(string[] p, string message)
        {
            switch (p[2])
            {
                case "CREATED":
                case "JOINED":
                    RoomId = ParseInt(p[3]);
                    AddLog($"방 {RoomId} 입장");
                    break;
                case "LEFT":
                    RoomId = 0;
                    if (InGame) CleanupGame(); // 게임 중 퇴장은 서버가 OUT으로 처리한다.
                    AddLog("방 퇴장");
                    break;
                default:
                    AddLog(message); // MEMBERS, MESSAGE, ERROR
                    break;
            }
        }

        private void HandleGame(string[] p, string message)
        {
            switch (p[2])
            {
                case "STARTED": // SERVER|GAME|STARTED|방|게임번호|시드|ID:스폰,...
                {
                    int gameId = ParseInt(p[4]);
                    if (gameId <= LastGameId) return; // 이미 지나간 게임
                    LastGameId = gameId;
                    BeginGame(gameId, ParseInt(p[5]), p[6]);
                    break;
                }
                case "MOVE": // SERVER|GAME|MOVE|ID|x|y|z|회전|속도
                    if (InGame && remotes.TryGetValue(ParseInt(p[3]), out RemotePlayer mover))
                        mover.Source.SetState(new Vector3(ParseFloat(p[4]), ParseFloat(p[5]), ParseFloat(p[6])),
                                              ParseFloat(p[7]), ParseFloat(p[8]));
                    break;
                case "ACTION":
                    if (InGame && remotes.TryGetValue(ParseInt(p[3]), out RemotePlayer actor))
                        actor.Source.QueueAction(ParseInt(p[4]));
                    break;
                case "JUMP":
                    if (InGame && remotes.TryGetValue(ParseInt(p[3]), out RemotePlayer jumper))
                        jumper.Source.QueueJump();
                    break;
                case "ATTACK":
                    if (InGame && remotes.TryGetValue(ParseInt(p[3]), out RemotePlayer attacker))
                        attacker.Source.QueueAttack();
                    break;
                case "KILL": // SERVER|GAME|KILL|게임번호|공격자|대상|남은 수
                    if (ParseInt(p[3]) != CurrentGameId) return; // 이전 게임 메시지
                    ApplyKill(ParseInt(p[4]), ParseInt(p[5]), ParseInt(p[6]));
                    break;
                case "OUT": // SERVER|GAME|OUT|게임번호|ID|남은 수
                    if (ParseInt(p[3]) != CurrentGameId) return;
                    ApplyOut(ParseInt(p[4]));
                    break;
                case "END": // SERVER|GAME|END|게임번호|승자
                    if (ParseInt(p[3]) != CurrentGameId) return;
                    int winner = ParseInt(p[4]);
                    AddLog(winner == 0 ? "게임 종료: 승자 없음" : winner == MyId ? "게임 종료: 승리!" : $"게임 종료: {winner} 승리");
                    CleanupGame();
                    GameEnded?.Invoke(winner, winner != 0 && winner == MyId);
                    break;
                case "ERROR":
                    AddLog(message); // COOLDOWN, OUT_OF_RANGE 등
                    break;
                default:
                    AddLog(message);
                    break;
            }
        }

        // ───────────── 게임 진행 ─────────────

        private void BeginGame(int gameId, int seed, string spawnList)
        {
            if (InGame) CleanupGame(); // 이전 게임의 END보다 새 STARTED가 먼저 온 경우
            CurrentGameId = gameId;

            // 모든 클라이언트가 같은 시드로 AI와 플레이어 시작 위치를 만든다.
            gameManager.StartNetworkGame(seed);

            var entries = new List<(int Id, int Spawn)>();
            foreach (string item in spawnList.Split(','))
            {
                string[] pair = item.Split(':');
                entries.Add((ParseInt(pair[0]), ParseInt(pair[1])));
            }

            // 내 캐릭터를 먼저 되살린 뒤 원격 플레이어를 등록해야 남은 인원 표시가 맞는다.
            foreach (var (id, spawn) in entries)
            {
                if (id != MyId) continue;
                gameManager.TryGetPlayerSpawn(spawn, out Vector3 position, out Quaternion rotation);
                localPlayer.Spawn(position, rotation);
            }
            foreach (var (id, spawn) in entries)
            {
                if (id == MyId) continue;
                gameManager.TryGetPlayerSpawn(spawn, out Vector3 position, out Quaternion rotation);
                CreateRemote(id, position, rotation);
            }

            moveTimer = 0f; // 시작 위치를 바로 보고해야 서버가 공격 거리를 판정할 수 있다.
            attackPending = false;
            AddLog($"게임 {gameId} 시작 ({entries.Count}명)");
            GameStarted?.Invoke(gameId);
        }

        private void CreateRemote(int id, Vector3 position, Quaternion rotation)
        {
            // 비활성 부모 아래에서 만들면 Awake 전에 AI 조종 부품을 바꿔 끼울 수 있다.
            var holder = new GameObject("RemoteSpawnHolder");
            holder.SetActive(false);
            Character character = Instantiate(remotePlayerPrefab, holder.transform);
            NavAIInputSource ai = character.GetComponent<NavAIInputSource>();
            if (ai != null) DestroyImmediate(ai);
            NavMeshAgent agent = character.GetComponent<NavMeshAgent>();
            if (agent != null) DestroyImmediate(agent);
            RemoteCommandSource source = character.GetComponent<RemoteCommandSource>();
            if (source == null) source = character.gameObject.AddComponent<RemoteCommandSource>();
            CharacterHealth health = character.GetComponent<CharacterHealth>();
            health.ServerAuthoritative = true; // 내 화면의 피격으로는 죽지 않는다.
            character.gameObject.tag = "Player";
            character.name = $"Remote Player {id}";
            character.transform.SetParent(remoteRoot, false); // 여기서 활성화되어 Awake가 실행된다.
            Destroy(holder);

            character.Spawn(position, rotation);
            source.PlayerId = id;
            remotes[id] = new RemotePlayer { Character = character, Health = health, Source = source };
            remoteIdsByHealth[health] = id;
            gameManager.Register_Player(character);
        }

        private void ApplyKill(int attacker, int target, int remaining)
        {
            GameObject attackerObject = attacker == MyId ? localPlayer.gameObject
                : remotes.TryGetValue(attacker, out RemotePlayer a) ? a.Character.gameObject : null;
            if (target == MyId)
            {
                // 서버가 이미 판정한 죽음이므로 DIE를 다시 보내지 않는다.
                suppressDieReport = true;
                try { localHealth.ApplyServerDeath(attackerObject, playEffect: true); }
                finally { suppressDieReport = false; }
            }
            else if (remotes.TryGetValue(target, out RemotePlayer victim))
            {
                victim.Health.ApplyServerDeath(attackerObject, playEffect: attacker == MyId);
            }
            AddLog(attacker == 0 ? $"{target} 탈락 (AI)" : $"{attacker} → {target} 처치 (남은 {remaining}명)");
            PlayerKilled?.Invoke(attacker, target, remaining);
        }

        private void ApplyOut(int id)
        {
            if (remotes.TryGetValue(id, out RemotePlayer player))
                player.Health.ApplyServerDeath(null, playEffect: false);
            AddLog($"{id} 이탈");
        }

        private void CleanupGame()
        {
            foreach (RemotePlayer remote in remotes.Values)
            {
                gameManager.Unregister_Player(remote.Character);
                Destroy(remote.Character.gameObject);
            }
            remotes.Clear();
            remoteIdsByHealth.Clear();
            gameManager.End_Game();
            CurrentGameId = 0;
            attackPending = false;
            // 대기실에서 다시 움직일 수 있게 내 캐릭터를 되살린다.
            if (!localHealth.IsAlive)
                localPlayer.Spawn(localPlayer.transform.position, localPlayer.transform.rotation);
        }

        // ───────────── 내 캐릭터 → 서버 ─────────────

        private void SendMove()
        {
            Transform t = localPlayer.transform;
            Vector3 p = t.position;
            float speed = Mathf.Clamp(localMotor.PlanarSpeed, 0f, 20f);
            Send($"GAME|MOVE|{F(p.x)}|{F(p.y)}|{F(p.z)}|{F(t.eulerAngles.y)}|{F(speed)}");
        }

        private void HandleLocalAttack()
        {
            if (!InGame) return;
            attackPending = true;
            attackTarget = 0;
        }

        private void HandleLocalTargetHit(IDamageable target)
        {
            // 원격 플레이어를 맞혔으면 대상 ID를 붙여 서버 판정을 요청한다. AI는 대상 없이 보낸다.
            if (target is CharacterHealth health && remoteIdsByHealth.TryGetValue(health, out int id))
                attackTarget = id;
        }

        private void HandleLocalAction(int index)
        {
            if (InGame && localHealth.IsAlive) Send($"GAME|ACTION|{index}");
        }

        private void HandleLocalJump()
        {
            if (InGame && localHealth.IsAlive) Send("GAME|JUMP");
        }

        private void HandleLocalDied(HitInfo hit)
        {
            // 내 화면의 AI에게 맞은 경우만 직접 알린다. 서버 KILL로 죽은 경우는 제외한다.
            if (!InGame || suppressDieReport) return;
            Send("GAME|DIE");
        }

        // ───────────── 유틸 ─────────────

        private static int ParseInt(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        private static float ParseFloat(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private void AddLog(string line)
        {
            Debug.Log($"[NET] {line}");
            logLines.Add(line);
            if (logLines.Count > 8) logLines.RemoveAt(0);
        }

        // 테스트용 임시 로비 UI. 게임 중에는 ESC로 마우스를 풀고 누른다.
        private void OnGUI()
        {
            if (!showDebugLobby) return;
            GUILayout.BeginArea(LobbyRect, GUI.skin.box);
            GUILayout.Label(Status);
            GUILayout.Label($"방: {(RoomId == 0 ? "-" : RoomId.ToString())}   게임: {(InGame ? CurrentGameId.ToString() : "-")}");
            if (!IsConnected)
            {
                if (GUILayout.Button("서버 접속")) Connect();
            }
            else if (RoomId == 0)
            {
                if (GUILayout.Button("방 만들기")) CreateRoom();
                GUILayout.BeginHorizontal();
                joinInput = GUILayout.TextField(joinInput, GUILayout.Width(80));
                if (GUILayout.Button("입장") && int.TryParse(joinInput, out int id)) JoinRoom(id);
                GUILayout.EndHorizontal();
            }
            else
            {
                if (!InGame && GUILayout.Button("게임 시작")) RequestStartGame();
                if (GUILayout.Button("멤버 보기")) RequestMembers();
                if (GUILayout.Button("방 나가기")) LeaveRoom();
            }
            foreach (string line in logLines) GUILayout.Label(line);
            GUILayout.EndArea();
        }
    }
}
