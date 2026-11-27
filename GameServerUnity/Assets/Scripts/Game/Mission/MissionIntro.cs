/*
[ 코드 설명 ]

MissionIntro

1. 게임이 시작되면 시작 연출을 진행한다: 미션 3개 추첨(룰렛) → 왼쪽 위 미션 표시 → 5,4,3,2,1 Start!
2. 술래에게는 미션 룰렛과 미션 표시를 보여주지 않는다. 대신 같은 시간 동안 "도망자들이 미션을 뽑고 있어요"
   안내를 보여 주고, 모든 플레이어의 카운트다운이 동시에 시작된다.
3. 클라이언트 기능만 담는다. 네트워크 코드는 고치지 않고, 이미 있는 공개 이벤트만 구독한다.
   - 미션: 네트워크 게임이면 게임 번호로 뽑는다(모든 클라이언트가 같은 번호를 받으므로 같은 미션).
   - 술래: 지금 서버는 술래 정보를 보내지 않는다. LocalIsTagger 값을 쓰므로,
     나중에 서버가 술래를 알려 주면 게임 시작 전에 이 값만 넣어 주면 된다.
4. 연출 동안 GameFlow.ControlsLocked로 조작을 막고, "Start!"가 뜨는 순간 풀어 게임을 시작한다.
5. 다른 곳에서 직접 연출을 틀고 싶으면 Play(술래 여부, 미션 번호들)를 부른다. (Test가 이걸로 시험한다.)
6. 역할 규칙(도망자 = 술래가 아닌 플레이어):
   - 공격해도 피해가 없다(AI도 다른 플레이어도 죽일 수 없다). CharacterCombat.CanDealDamage를 끈다.
   - 오른쪽 아래 공격 버튼 UI를 숨긴다.
   게임이 끝나면 둘 다 원래대로 돌린다.
   술래: 자기 공격으로 AI를 죽이면 지금 공격 쿨타임이 aiKillPenalty초(기본 2초) 늘어난다. 플레이어를 죽이면 없음.
7. "Start!" 이후 미션마다 MissionTracker로 진행도를 센다. 하나를 채우면 그 줄이 초록색으로 바뀌며 튀고,
   셋 다 채우면 제목이 MISSION CLEAR로 바뀌며 표시 전체가 튄다.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GameServer
{
    public class MissionIntro : MonoBehaviour
    {
        [SerializeField] private MissionIntroView view;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private NetworkGameManager network;   // 없으면 GameManager 시작만 본다.
        [Tooltip("내 캐릭터. 비워 두면 PlayerInputSource가 붙은 캐릭터를 찾는다.")]
        [SerializeField] private Character localPlayer;

        [Header("미션 목록 (모든 클라이언트가 같아야 함)")]
        [SerializeField] private MissionDefinition[] missionList =
        {
            new MissionDefinition("점프 5번 하기", MissionGoal.Jump, 5),
            new MissionDefinition("점프 10번 하기", MissionGoal.Jump, 10),
            new MissionDefinition("10초 동안 달리기", MissionGoal.RunSeconds, 10),
            new MissionDefinition("5초 동안 가만히 있기", MissionGoal.StandStillSeconds, 5),
            new MissionDefinition("20초 동안 걷기만 하기", MissionGoal.WalkOnlySeconds, 20),
        };
        [Tooltip("한 게임에 뽑는 미션 수 (UI 줄 수까지)")]
        [SerializeField, Range(1, 3)] private int missionsPerGame = 3;

        [Header("연출 시간")]
        [SerializeField] private float spinTime = 3.6f;      // 룰렛이 돌기 시작해 마지막 줄이 멈출 때까지
        [SerializeField] private float resultHold = 1.2f;    // 걸린 미션을 보여주는 시간
        [SerializeField] private int countdownFrom = 5;

        [Header("술래")]
        [Tooltip("이번 게임에서 내가 술래인지. 서버가 술래를 알려 주게 되면 게임 시작 전에 이 값을 넣는다.")]
        [SerializeField] private bool localIsTagger;
        [Tooltip("술래가 AI를 죽였을 때 다음 공격까지 늘어나는 쿨타임(초)")]
        [SerializeField, Min(0f)] private float aiKillPenalty = 2f;

        private Coroutine running;
        private readonly List<MissionTracker> trackers = new List<MissionTracker>();
        private MissionDefinition[] current = Array.Empty<MissionDefinition>();
        private CharacterCombat localCombat;
        private UIView[] huds = Array.Empty<UIView>();   // 오른쪽 아래 공격 버튼이 있는 HUD (씬에 여러 개일 수 있어 전부)
        private bool defaultCanDealDamage = true;

        public bool LocalIsTagger { get => localIsTagger; set => localIsTagger = value; }
        public bool IsLocalTagger { get; private set; }       // 지금 진행 중인 게임 기준
        public IReadOnlyList<MissionDefinition> CurrentMissions => current;
        public int MissionCount => missionList.Length;
        public bool IsIntroPlaying => running != null;
        public bool AllMissionsCleared { get; private set; }

        public event Action IntroStarted;                    // 시작 연출이 (다시) 시작되는 순간 → 라운드 초기화
        public event Action GameplayStarted;                 // "Start!" 순간
        public event Action<int> MissionCleared;             // 클리어한 미션 줄 번호(0부터)
        public event Action AllCleared;

        private int Count => Mathf.Clamp(Mathf.Min(missionsPerGame, missionList.Length), 0, view != null ? view.MaxMissions : 0);

        private void Awake()
        {
            if (view == null) view = GetComponent<MissionIntroView>();
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (network == null) network = FindFirstObjectByType<NetworkGameManager>();
            huds = FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (localPlayer == null)
            {
                var input = FindFirstObjectByType<PlayerInputSource>();
                if (input != null) localPlayer = input.GetComponent<Character>();
            }
            if (localPlayer != null && localPlayer.TryGetComponent(out localCombat))
                defaultCanDealDamage = localCombat.CanDealDamage;

            view.HideAll();   // 편집 화면에서는 보이던 미리보기를 실행할 때 숨긴다.
        }

        private void Update()
        {
            foreach (MissionTracker t in trackers) t.Tick(Time.deltaTime);
        }

        private void OnEnable()
        {
            if (gameManager != null) gameManager.GameStarted += HandleLocalGameStarted;
            CharacterHealth.AnyDied += HandleAnyDied;
            if (network != null)
            {
                network.GameStarted += HandleNetworkGameStarted;
                network.GameEnded += HandleNetworkGameEnded;
            }
        }

        private void OnDisable()
        {
            if (gameManager != null) gameManager.GameStarted -= HandleLocalGameStarted;
            CharacterHealth.AnyDied -= HandleAnyDied;
            if (network != null)
            {
                network.GameStarted -= HandleNetworkGameStarted;
                network.GameEnded -= HandleNetworkGameEnded;
            }
            Cancel();
        }

        // GameManager.GameStarted는 네트워크 게임에서도 먼저 불린다. 그때는 네트워크 쪽 이벤트를 기다린다.
        private void HandleLocalGameStarted()
        {
            if (network != null && network.InGame) return;
            Play(localIsTagger);
        }

        // 게임 번호는 모든 클라이언트가 같으므로 미션도 같게 나온다.
        private void HandleNetworkGameStarted(int gameId) =>
            Play(localIsTagger, PickMissions(new System.Random(gameId)));

        private void HandleNetworkGameEnded(int winner, bool iWon) => Cancel();

        // 시작 연출을 처음부터 튼다. missionIndices가 비었거나 틀리면 무작위로 겹치지 않게 고른다.
        public void Play(bool isTagger, int[] missionIndices = null)
        {
            if (Count == 0) { Debug.LogWarning("MissionIntro: 미션 목록이나 미션 UI가 비어 있습니다."); return; }
            int[] picks = IsValid(missionIndices) ? missionIndices : PickMissions(new System.Random());

            Cancel();
            IsLocalTagger = isTagger;
            // 내가 술래면 술래 = 내 캐릭터. 도망자면 내 캐릭터를 술래에서 뺀다(다른 술래 정보는 그대로 둔다).
            if (isTagger) RoundRoles.Tagger = localPlayer;
            else if (RoundRoles.Tagger == localPlayer) RoundRoles.Tagger = null;
            current = new MissionDefinition[picks.Length];
            for (int i = 0; i < picks.Length; i++) current[i] = missionList[picks[i]];
            AllMissionsCleared = false;
            ApplyRole(isTagger);
            running = StartCoroutine(Run(picks));
            IntroStarted?.Invoke();
        }

        private int[] PickMissions(System.Random rng)
        {
            var pool = new List<int>();
            for (int i = 0; i < missionList.Length; i++) pool.Add(i);
            var picks = new int[Count];
            for (int i = 0; i < picks.Length; i++)
            {
                int k = rng.Next(pool.Count);
                picks[i] = pool[k];
                pool.RemoveAt(k);
            }
            return picks;
        }

        private bool IsValid(int[] indices)
        {
            if (indices == null || indices.Length == 0 || indices.Length > Count) return false;
            foreach (int i in indices) if (i < 0 || i >= missionList.Length) return false;
            return true;
        }

        private IEnumerator Run(int[] picks)
        {
            GameFlow.ControlsLocked = true;

            if (IsLocalTagger)
            {
                // 술래는 미션을 볼 수 없다. 룰렛이 도는 시간만큼 "미션을 뽑는 중" 안내를 보여 주고 카운트다운을 맞춘다.
                yield return view.PlayTaggerNotice(MissionIntroView.RouletteLength(spinTime, resultHold));
            }
            else
            {
                yield return view.PlayRoulette(MissionTexts(), picks, spinTime, resultHold);
                var texts = new string[current.Length];
                var progress = new string[current.Length];
                for (int i = 0; i < current.Length; i++)
                {
                    texts[i] = current[i].text;
                    progress[i] = ProgressText(0f, current[i]);
                }
                view.ShowMissions(texts, progress);
            }

            yield return view.PlayCountdown(countdownFrom, () =>
            {
                GameFlow.ControlsLocked = false;
                if (!IsLocalTagger) BeginTracking();
                GameplayStarted?.Invoke();
            });
            running = null;
        }

        // 게임이 끝나거나 새 게임이 시작되면 연출을 멈추고 화면을 비운다.
        public void Cancel()
        {
            if (running != null) StopCoroutine(running);
            running = null;
            GameFlow.ControlsLocked = false;
            StopTracking();
            RestoreRole();
            if (view != null)
            {
                view.StopAllCoroutines();
                view.HideAll();
            }
        }

        // ───────────── 역할 ─────────────

        // 도망자(술래 아님): 공격 피해 없음 + 공격 버튼 숨김
        private void ApplyRole(bool isTagger)
        {
            if (localCombat != null) localCombat.CanDealDamage = isTagger && defaultCanDealDamage;
            foreach (UIView hud in huds) if (hud != null) hud.SetSkillVisible(isTagger);
        }

        // 술래가 자기 공격으로 AI(Player 태그가 아닌 캐릭터)를 죽이면 다음 공격 쿨타임에 패널티를 준다.
        private void HandleAnyDied(CharacterHealth victim, HitInfo hit)
        {
            if (!IsLocalTagger || localCombat == null || localPlayer == null) return;
            if (hit.Attacker != localPlayer.gameObject || victim.CompareTag("Player")) return;
            localCombat.AddCooldownPenalty(aiKillPenalty);
        }

        private void RestoreRole()
        {
            if (localCombat != null) localCombat.CanDealDamage = defaultCanDealDamage;
            foreach (UIView hud in huds) if (hud != null) hud.SetSkillVisible(true);
        }

        // ───────────── 미션 진행 ─────────────

        private void BeginTracking()
        {
            StopTracking();
            for (int i = 0; i < current.Length; i++)
            {
                int index = i;
                var tracker = new MissionTracker();
                tracker.ProgressChanged += (value, goal) =>
                    view.SetMissionRow(index, current[index].text, ProgressText(value, current[index]), false);
                tracker.Cleared += () => HandleMissionCleared(index);
                trackers.Add(tracker);
                tracker.Begin(current[i], localPlayer);
            }
        }

        private void StopTracking()
        {
            foreach (MissionTracker t in trackers) t.Stop();
            trackers.Clear();
        }

        private void HandleMissionCleared(int index)
        {
            MissionDefinition m = current[index];
            view.SetMissionRow(index, m.text, ProgressText(m.amount, m), true);
            view.PlayRowBounce(index);
            MissionCleared?.Invoke(index);

            foreach (MissionTracker t in trackers) if (!t.IsCleared) return;
            AllMissionsCleared = true;
            view.ShowAllCleared();
            AllCleared?.Invoke();
        }

        private static string ProgressText(float value, MissionDefinition m)
        {
            string unit = m.IsTimed ? "초" : "";
            return $"{Mathf.FloorToInt(value)}/{Mathf.RoundToInt(m.amount)}{unit}";
        }

        private string[] MissionTexts()
        {
            var texts = new string[missionList.Length];
            for (int i = 0; i < texts.Length; i++) texts[i] = missionList[i].text;
            return texts;
        }
    }
}
