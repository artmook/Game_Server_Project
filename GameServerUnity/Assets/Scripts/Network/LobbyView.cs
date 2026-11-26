/*
[ 코드 설명 ]

LobbyView

1. 게임 시작 전 대기실 화면. NetworkGameManager 상태에 맞춰 세 화면 중 하나를 보여 준다.
   - 접속 화면: 서버 접속 상태와 "다시 접속" 버튼
   - 메뉴 화면: 닉네임 입력, "방 만들기", 초대 코드 입력 후 "참가"
   - 방 화면: 초대 코드(= 서버 방 번호)와 복사 버튼, 참가 인원·닉네임 목록(방장 표시),
     "게임 시작"(방장만, 2명 이상), "나가기"
2. 게임 중에는 대기실을 숨기고, 게임이 끝나면 결과 화면(GameResultView)에서 확인을 누른 뒤 방 화면으로 돌아온다.
3. 게임이 시작되면 위쪽에 역할 안내(내 역할, 술래·도망자 닉네임)를 잠깐 보여 준다.
4. 서버는 다른 사람의 입장·퇴장을 알려 주지 않으므로, 방 화면에서 멤버 목록을 주기적으로 다시 요청한다.
5. UI는 Tools > 로비 UI 만들기(LobbyUIBuilder)로 만들고 참조를 연결한다.
*/

using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer
{
    public class LobbyView : MonoBehaviour
    {
        private const int MinPlayers = 2;   // 서버 GAME|START 조건
        private const int MaxMembers = 4;   // 서버 RoomManager.MaxMembers
        private const string NicknameKey = "nickname";
        private const string AccentHex = "#FF9E40";
        private const string TaggerHex = "#FF5A4E";

        [SerializeField] private NetworkGameManager network;
        [SerializeField] private CanvasGroup group;          // 어두운 배경 + 패널 (게임 중 숨김)
        [SerializeField] private float membersPollInterval = 1f;

        [Header("접속 화면")]
        [SerializeField] private GameObject connectPanel;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button reconnectButton;

        [Header("메뉴 화면")]
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private Button createButton;
        [SerializeField] private TMP_InputField codeInput;
        [SerializeField] private Button joinButton;

        [Header("방 화면")]
        [SerializeField] private GameObject roomPanel;
        [SerializeField] private TMP_Text codeText;
        [SerializeField] private Button copyButton;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private TMP_Text membersText;
        [SerializeField] private Button startButton;
        [SerializeField] private TMP_Text startHint;
        [SerializeField] private Button leaveButton;

        [Header("공통")]
        [SerializeField] private TMP_Text errorText;

        [Header("역할 안내")]
        [SerializeField] private CanvasGroup roleGroup;
        [SerializeField] private TMP_Text roleTitle;
        [SerializeField] private TMP_Text roleDetail;
        [SerializeField] private float roleShowTime = 6f;

        private float pollTimer;
        private bool wasVisible;
        private Coroutine roleRoutine;

        private void Awake()
        {
            if (network == null) network = FindFirstObjectByType<NetworkGameManager>();
            nicknameInput.characterLimit = NetworkGameManager.MaxNicknameLength;
            nicknameInput.text = PlayerPrefs.GetString(NicknameKey, "");
            reconnectButton.onClick.AddListener(() => { ClearError(); network.Connect(); });
            createButton.onClick.AddListener(() => { if (ApplyNickname()) network.CreateRoom(); });
            joinButton.onClick.AddListener(Join);
            codeInput.onSubmit.AddListener(_ => Join());
            copyButton.onClick.AddListener(() => GUIUtility.systemCopyBuffer = network.RoomId.ToString());
            startButton.onClick.AddListener(() => { ClearError(); network.RequestStartGame(); });
            leaveButton.onClick.AddListener(() => { ClearError(); network.LeaveRoom(); });
            ClearError();
            roleGroup.alpha = 0f;
        }

        private void OnEnable()
        {
            network.RequestFailed += HandleError;
            network.GameStarted += HandleGameStarted;
            network.GameEnded += HandleGameEnded;
        }

        private void OnDisable()
        {
            network.RequestFailed -= HandleError;
            network.GameStarted -= HandleGameStarted;
            network.GameEnded -= HandleGameEnded;
        }

        private void Update()
        {
            // 게임 결과 화면을 보는 동안에는 숨기고, 결과 확인 버튼을 누르면 방 화면으로 돌아온다.
            bool visible = !network.InGame && !network.ResultPending;
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = group.interactable = visible;
            if (visible && !wasVisible) pollTimer = 0f;   // 게임이 끝나 돌아오면 바로 목록을 다시 받는다.
            wasVisible = visible;
            if (!visible) return;

            bool connected = network.IsConnected;
            bool inRoom = connected && network.RoomId != 0;
            connectPanel.SetActive(!connected);
            menuPanel.SetActive(connected && !inRoom);
            roomPanel.SetActive(inRoom);

            // 끊김 사유는 OS 예외 문구(널 문자 포함)라 화면에는 쉬운 문장만 보인다. 원문은 [NET] 로그에 남는다.
            if (!connected)
                statusText.text = network.Status.StartsWith("연결 끊김")
                    ? "서버와 연결이 끊겼어요\n<size=75%>서버가 켜져 있는지 확인한 뒤 다시 접속해 주세요</size>"
                    : network.Status;
            if (!inRoom) return;

            pollTimer -= Time.deltaTime;
            if (pollTimer <= 0f)
            {
                pollTimer = membersPollInterval;
                network.RequestMembers();
            }
            RefreshRoom();
        }

        private void RefreshRoom()
        {
            codeText.text = network.RoomId.ToString();
            var members = network.Members;
            countText.text = $"참가 인원 <color={AccentHex}>{members.Count}</color> / {MaxMembers}명";

            var sb = new StringBuilder();
            foreach (int id in members)
            {
                if (sb.Length > 0) sb.Append('\n');
                string name = Escape(network.GetNickname(id));
                string tags = (id == network.HostId ? " <size=80%><color=#FFD35A>[방장]</color></size>" : "") +
                              (id == network.MyId ? " <size=80%>(나)</size>" : "");
                sb.Append(id == network.MyId ? $"<color={AccentHex}>{name}</color>{tags}" : name + tags);
            }
            membersText.text = sb.ToString();

            bool enough = members.Count >= MinPlayers;
            bool host = network.IsHost;
            startButton.gameObject.SetActive(host);
            startButton.interactable = host && enough;
            startHint.text = !enough ? $"{MinPlayers}명 이상 모이면 시작할 수 있어요"
                           : host ? "모두 모였으면 게임을 시작하세요"
                           : $"방장({Escape(network.GetNickname(network.HostId))})이 시작하기를 기다리는 중...";
        }

        private bool ApplyNickname()
        {
            ClearError();
            string name = NetworkGameManager.SanitizeNickname(nicknameInput.text);
            if (name.Length == 0)
            {
                ShowError("닉네임을 입력해 주세요");
                return false;
            }
            nicknameInput.text = name;
            network.Nickname = name;
            PlayerPrefs.SetString(NicknameKey, name);
            return true;
        }

        private void Join()
        {
            if (!ApplyNickname()) return;
            if (int.TryParse(codeInput.text.Trim(), out int roomId) && roomId > 0) network.JoinRoom(roomId);
            else ShowError("초대 코드를 숫자로 입력해 주세요");
        }

        // ───────────── 역할 안내 ─────────────

        private void HandleGameStarted(int gameId)
        {
            ClearError();
            int tagger = network.TaggerId;
            if (tagger == 0) return;

            bool meTagger = tagger == network.MyId;
            roleTitle.text = meTagger
                ? $"당신은 <color={TaggerHex}>술래</color>입니다!"
                : $"당신은 <color={AccentHex}>도망자</color>입니다!";

            var runners = new StringBuilder();
            foreach (int id in network.GamePlayers)
            {
                if (id == tagger) continue;
                if (runners.Length > 0) runners.Append(", ");
                runners.Append(Escape(network.GetNickname(id)));
            }
            roleDetail.text = $"술래  <color={TaggerHex}>{Escape(network.GetNickname(tagger))}</color>\n" +
                              $"도망자  <color={AccentHex}>{runners}</color>";

            if (roleRoutine != null) StopCoroutine(roleRoutine);
            roleRoutine = StartCoroutine(ShowRole());
        }

        private void HandleGameEnded(int winner, bool iWon)
        {
            if (roleRoutine != null) StopCoroutine(roleRoutine);
            roleRoutine = null;
            roleGroup.alpha = 0f;
        }

        private IEnumerator ShowRole()
        {
            const float fade = 0.35f;
            for (float t = 0f; t < fade; t += Time.unscaledDeltaTime) { roleGroup.alpha = t / fade; yield return null; }
            roleGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(roleShowTime);
            for (float t = 0f; t < fade; t += Time.unscaledDeltaTime) { roleGroup.alpha = 1f - t / fade; yield return null; }
            roleGroup.alpha = 0f;
            roleRoutine = null;
        }

        // ───────────── 오류 ─────────────

        private void HandleError(string code) => ShowError(code switch
        {
            "ROOM_NOT_FOUND" => "없거나 만료된 초대 코드예요",
            "ROOM_FULL" => $"방이 가득 찼어요 (최대 {MaxMembers}명)",
            "GAME_IN_PROGRESS" => "이미 게임이 진행 중인 방이에요",
            "INVALID_ROOM_ID" => "초대 코드 형식이 올바르지 않아요",
            "ALREADY_IN_ROOM" => "이미 방에 들어가 있어요",
            "NOT_IN_ROOM" => "방에서 나간 상태예요",
            "NOT_ENOUGH_PLAYERS" => $"{MinPlayers}명 이상이어야 시작할 수 있어요",
            "ALREADY_STARTED" => "이미 게임이 시작됐어요",
            "EMPTY_MESSAGE" => "닉네임을 입력해 주세요",
            _ => $"요청 실패 ({code})",
        });

        private void ShowError(string message) => errorText.text = message;
        private void ClearError() => errorText.text = "";

        // 닉네임은 이미 <, >를 빼지만, 화면에 넣을 때 한 번 더 막는다.
        private static string Escape(string s) => "<noparse>" + s + "</noparse>";
    }
}
