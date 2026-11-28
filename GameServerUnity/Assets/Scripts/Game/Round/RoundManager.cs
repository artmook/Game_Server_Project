/*
[ 코드 설명 ]

RoundManager (라운드 시간 · 승패 결과) — 타이머와 결과 화면은 이것 하나만 쓴다.

1. 네트워크 게임(서버 연결)
   - 남은 시간: 서버 TIME 값을 그대로 보여 준다. 클라이언트는 시간을 세지 않는다. (제한시간은 서버 STARTED가 정한다)
   - 승패: 서버 END(GameResultReceived)만 따른다. 클라이언트가 따로 판정하지 않는다.
   - 결과 화면의 "나가기"는 NetworkGameManager.ConfirmResult()를 불러 결과를 닫고 로비 화면(방 화면)으로 돌아간다.
   - Network 폴더의 GameStatusView(서버 시간·결과를 보여 주던 화면)는 같은 일을 하므로 실행 중에 꺼 둔다.
     (GameStatusView 코드는 고치지 않는다. 화면만 숨기고, 그 역할은 여기서 똑같이 한다)
2. 오프라인 시험(Test, 서버 없음)
   - 시작 연출의 "Start!"부터 roundSeconds(기본 300초 = 5분)를 직접 센다.
   - 승패는 서버 규칙과 같게 판정한다.
     · 도망자 승리: 살아 있는 도망자가 미션을 모두 완료 (오프라인에서는 미션을 하는 사람이 나뿐이라 내가 다 깨면)
     · 술래 승리: 도망자를 모두 잡음 / 제한시간 안에 미션을 완료하지 못함
3. 결과 화면은 술래·도망자 모두에게 뜬다. 이긴 쪽 이름(탈락한 도망자는 회색 줄)과 내 승패를 보여 준다.
   결과가 뜨면 조작을 멈추고 마우스를 풀어 버튼을 누를 수 있게 한다.
4. 시작 연출을 다시 틀면(MissionIntro.IntroStarted) 타이머와 결과 화면이 처음 상태로 돌아간다.
*/

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace GameServer
{
    public class RoundManager : MonoBehaviour
    {
        [SerializeField] private RoundView view;
        [SerializeField] private MissionIntro intro;
        [SerializeField] private SurvivorCounter survivors;   // 오프라인 이름용(없어도 된다)
        [Tooltip("내 캐릭터. 비워 두면 PlayerInputSource가 붙은 캐릭터를 찾는다.")]
        [SerializeField] private Character localPlayer;
        [Tooltip("비워 두면 씬에서 찾는다. 서버 게임 중에는 시간·승패를 여기서 받는다.")]
        [SerializeField] private NetworkGameManager network;

        [Header("규칙 (오프라인 시험용. 서버 게임은 서버 값을 따른다)")]
        [Tooltip("라운드 제한 시간(초). 300 = 5분")]
        [SerializeField, Min(1f)] private float roundSeconds = 300f;
        [Tooltip("마지막 도망자가 쓰러진 뒤 결과 화면이 뜨기까지(초)")]
        [SerializeField, Min(0f)] private float endDelay = 1.5f;
        [SerializeField] private string playerTag = "Player";

        [Header("나가기 버튼")]
        [Tooltip("결과를 닫고 로비 화면으로 돌아간 뒤 불린다. 따로 할 일이 있으면 여기에 연결한다.")]
        public UnityEvent onExit = new UnityEvent();

        // 서버 END 이유 → 화면 문구 (GameStatusView와 같은 문구)
        private static string ReasonText(string reason) => reason switch
        {
            "MISSION_CLEAR" => "살아 있는 도망자가 모두 미션을 완료했습니다",
            "ALL_CAUGHT" => "술래가 도망자를 모두 잡았습니다",
            "TIME_OVER" => "제한시간 안에 미션을 완료하지 못했습니다",
            "TAGGER_LEFT" => "술래가 게임을 떠났습니다",
            _ => "",
        };

        private readonly List<(Character character, string name)> runners = new();   // 오프라인
        private bool running;
        private bool ended;
        private float remaining;
        private Coroutine pendingEnd;

        // 서버 게임 정보(END 때는 NetworkGameManager가 술래 정보를 이미 지우므로 시작할 때 기억해 둔다)
        private int netTagger;
        private readonly List<int> netPlayers = new();
        private readonly HashSet<int> netCaught = new();
        private bool netResultShown;
        private GameStatusView hiddenStatusView;

        private bool Online => network != null && network.InGame;

        public float Remaining => Online && network.RemainingSeconds >= 0 ? network.RemainingSeconds : remaining;
        public bool IsRunning => running;
        public float RoundSeconds => roundSeconds;

        private void Awake()
        {
            if (view == null) view = GetComponent<RoundView>();
            if (intro == null) intro = FindFirstObjectByType<MissionIntro>();
            if (survivors == null) survivors = FindFirstObjectByType<SurvivorCounter>();
            if (network == null) network = FindFirstObjectByType<NetworkGameManager>();
            if (localPlayer == null)
            {
                var input = FindFirstObjectByType<PlayerInputSource>();
                if (input != null) localPlayer = input.GetComponent<Character>();
            }
            remaining = roundSeconds;
            if (view != null)
            {
                view.ResetTimer(roundSeconds);
                if (view.ExitButton != null) view.ExitButton.onClick.AddListener(Exit);
            }
        }

        private void OnEnable()
        {
            if (intro != null)
            {
                intro.IntroStarted += ResetRound;
                intro.GameplayStarted += BeginRound;
                intro.AllCleared += HandleLocalMissionsCleared;
            }
            CharacterHealth.AnyDied += HandleAnyDied;
            if (network != null)
            {
                network.GameStarted += HandleServerStarted;
                network.PlayerKilled += HandleServerKill;
                network.GameResultReceived += HandleServerResult;
            }
        }

        private void OnDisable()
        {
            if (intro != null)
            {
                intro.IntroStarted -= ResetRound;
                intro.GameplayStarted -= BeginRound;
                intro.AllCleared -= HandleLocalMissionsCleared;
            }
            CharacterHealth.AnyDied -= HandleAnyDied;
            if (network != null)
            {
                network.GameStarted -= HandleServerStarted;
                network.PlayerKilled -= HandleServerKill;
                network.GameResultReceived -= HandleServerResult;
            }
        }

        private void Update()
        {
            HideServerStatusView();

            if (network != null)
            {
                // 서버 게임: 서버가 보낸 남은 시간을 그대로 보여 준다.
                if (Online && network.RemainingSeconds >= 0 && view != null) view.SetTime(network.RemainingSeconds);
                // 다른 곳에서 결과를 닫았거나 새 게임이 시작되면 결과 화면도 닫는다.
                if (netResultShown && !network.ResultPending) CloseResult();
                if (Online) return;
            }

            if (!running) return;
            remaining -= Time.deltaTime;
            if (view != null) view.SetTime(remaining);
            if (remaining <= 0f)
            {
                remaining = 0f;
                running = false;
                EndRound(true, ReasonText("TIME_OVER"), null);   // 서버 규칙: 시간이 다 되면 술래 승리
            }
        }

        // GameStatusView는 서버 시간·결과를 따로 보여 주는 화면이다. 같은 내용을 여기서 보여 주므로 숨긴다.
        private void HideServerStatusView()
        {
            if (hiddenStatusView != null) return;
            hiddenStatusView = FindFirstObjectByType<GameStatusView>();
            if (hiddenStatusView != null) hiddenStatusView.gameObject.SetActive(false);
        }

        // ───────────── 흐름 ─────────────

        private void ResetRound()
        {
            if (pendingEnd != null) StopCoroutine(pendingEnd);
            pendingEnd = null;
            running = false;
            ended = false;
            remaining = roundSeconds;
            runners.Clear();
            GameFlow.RoundOver = false;
            if (view != null)
            {
                view.ResetTimer(Online && network.RemainingSeconds >= 0 ? network.RemainingSeconds : roundSeconds);
                view.HideResult();
            }
        }

        private void BeginRound()
        {
            ResetRound();
            if (Online) return;   // 서버 게임은 서버가 시간을 센다.
            CollectRunners();
            running = true;
        }

        private void CollectRunners()
        {
            runners.Clear();
            foreach (Character c in FindObjectsByType<Character>(FindObjectsSortMode.None))
            {
                if (!c.CompareTag(playerTag) || RoundRoles.IsTagger(c)) continue;
                runners.Add((c, NameOf(c)));
            }
        }

        // 오프라인: 마지막 도망자가 쓰러지면 술래 승리
        private void HandleAnyDied(CharacterHealth victim, HitInfo hit)
        {
            if (Online || !running || ended || !victim.CompareTag(playerTag)) return;
            if (runners.Count == 0 || CountAliveRunners() > 0) return;
            running = false;
            pendingEnd = StartCoroutine(EndAfter(endDelay));
        }

        // 오프라인: 미션을 하는 도망자가 나뿐이므로, 내가 다 깨면 도망자 승리
        private void HandleLocalMissionsCleared()
        {
            if (Online || !running || ended || intro == null || intro.IsLocalTagger) return;
            if (localPlayer != null && !localPlayer.IsAlive) return;
            running = false;
            EndRound(false, ReasonText("MISSION_CLEAR"), null);
        }

        private IEnumerator EndAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            pendingEnd = null;
            EndRound(true, ReasonText("ALL_CAUGHT"), null);
        }

        private int CountAliveRunners()
        {
            int alive = 0;
            foreach (var (c, _) in runners) if (c != null && c.IsAlive) alive++;
            return alive;
        }

        private void EndRound(bool taggerWon, string reason, string names)
        {
            if (ended) return;
            ended = true;
            running = false;
            GameFlow.ControlsLocked = true;   // 결과가 뜨면 모두 멈춘다
            GameFlow.RoundOver = true;        // 마우스를 풀어 나가기 버튼을 누를 수 있게
            CursorController.Unlock();
            if (view != null)
                view.ShowResult(taggerWon, reason, names ?? (taggerWon ? TaggerNames() : RunnerNames()));
        }

        // 서버 없이 결과를 보여 줘야 할 때 쓴다.
        public void ShowResult(bool taggerWon, string reason = null)
        {
            if (pendingEnd != null) StopCoroutine(pendingEnd);
            pendingEnd = null;
            ended = false;
            EndRound(taggerWon, reason ?? ReasonText(taggerWon ? "ALL_CAUGHT" : "MISSION_CLEAR"), null);
        }

        // 테스트용: 남은 시간을 바꾼다(오프라인만).
        public void SetRemaining(float seconds)
        {
            if (!running || Online) return;
            remaining = Mathf.Max(0.01f, seconds);
        }

        // 나가기: 술래·도망자 누가 이겼든 결과를 닫고 로비 화면(LobbyView)으로 돌아간다.
        // - 서버 게임: ConfirmResult로 결과 대기를 끝내면 LobbyView가 다시 보이고 방 화면(다음 게임 시작 가능)이 뜬다.
        // - 오프라인 시험: 진행 중이던 시작 연출·미션을 정리하고 결과를 닫는다. 서버에 연결되어 있지 않으므로 로비는 접속 화면이 보인다.
        private void Exit()
        {
            if (network != null && network.ResultPending) network.ConfirmResult();
            if (intro != null) intro.Cancel();   // 미션 표시·역할(공격 버튼 등) 정리
            CloseResult();
            if (view != null) view.ResetTimer(roundSeconds);
            remaining = roundSeconds;
            CursorController.Unlock();           // 로비에서 바로 버튼을 누를 수 있게
            onExit?.Invoke();
        }

        private void CloseResult()
        {
            netResultShown = false;
            ended = false;
            GameFlow.RoundOver = false;
            GameFlow.ControlsLocked = false;
            if (view != null) view.HideResult();
        }

        // ───────────── 서버 게임 ─────────────

        private void HandleServerStarted(int gameId)
        {
            netTagger = network.TaggerId;
            netPlayers.Clear();
            netPlayers.AddRange(network.GamePlayers);
            netCaught.Clear();
            netResultShown = false;
            ResetRound();
        }

        private void HandleServerKill(int attacker, int target, int remainingCount) => netCaught.Add(target);

        private void HandleServerResult(NetworkGameManager.GameResult result)
        {
            bool taggerWon = result.WinnerTeam == NetworkGameManager.TaggerTeam;
            string role = result.LocalWasTagger ? "술래" : "도망자";
            string me = result.LocalWon ? "승리" : "패배";
            string reason = ReasonText(result.Reason);
            reason = reason.Length > 0 ? $"{reason}  ·  {me} (내 역할: {role})" : $"{me} (내 역할: {role})";

            if (pendingEnd != null) StopCoroutine(pendingEnd);
            pendingEnd = null;
            ended = false;
            netResultShown = true;
            EndRound(taggerWon, reason, taggerWon ? NetTaggerName() : NetRunnerNames());
        }

        private string NetTaggerName() => netTagger != 0 ? NetName(netTagger) : "술래";

        private string NetRunnerNames()
        {
            var sb = new StringBuilder();
            foreach (int id in netPlayers)
            {
                if (id == netTagger) continue;
                if (sb.Length > 0) sb.Append('\n');
                string name = NetName(id);
                if (netCaught.Contains(id)) sb.Append("<color=#8A8F99><s>").Append(name).Append("</s> <size=70%>탈락</size></color>");
                else sb.Append(name);
            }
            return sb.Length > 0 ? sb.ToString() : "-";
        }

        private string NetName(int id)
        {
            string name = $"<noparse>{network.GetNickname(id)}</noparse>";
            return id == network.MyId ? $"{name} <size=70%>(나)</size>" : name;
        }

        // ───────────── 오프라인 이름 ─────────────

        private string TaggerNames()
        {
            Character tagger = RoundRoles.Tagger;
            return tagger != null ? NameOf(tagger) : "술래";
        }

        private string RunnerNames()
        {
            var sb = new StringBuilder();
            foreach (var (c, name) in runners)
            {
                if (sb.Length > 0) sb.Append('\n');
                bool alive = c != null && c.IsAlive;
                if (alive) sb.Append(name);
                else sb.Append("<color=#8A8F99><s>").Append(name).Append("</s> <size=70%>탈락</size></color>");
            }
            return sb.Length > 0 ? sb.ToString() : "-";
        }

        private string NameOf(Character c)
        {
            string name = survivors != null ? survivors.GetDisplayName(c) : c.gameObject.name.Replace("(Clone)", "");
            name = name.Replace(" (술래)", "");
            return c == localPlayer ? $"{name} <size=70%>(나)</size>" : name;
        }
    }
}
