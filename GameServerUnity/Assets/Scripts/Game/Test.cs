/*
[ 코드 설명 ]

Test (에디터 전용, 서버 없이 클라이언트 기능 시험)

켜고 끄기: 인스펙터에서 Test 컴포넌트 이름 옆 체크박스로 한다.
- 플레이 중에는 ESC로 마우스를 풀고 체크박스를 누르면 된다(테스트 중엔 Game 화면을 클릭할 때만 마우스가 다시 잠긴다).
- 플레이 전에 꺼 두면 테스트 없이 실행된다.
- 플레이 중에 끄면 바로 원래대로 돌아가고(마우스 잠금 규칙, 원격 플레이어 즉사 규칙), 다시 켜면 다시 적용된다.

켜져 있을 때
1. 서버 없이 혼자 플레이한다. 원격 플레이어(ServerAuthoritative)를 때리면 바로 죽는다.
   (도망자는 애초에 공격 피해가 없으므로, 술래로 시험할 때만 해당된다.)
2. 처음 켜질 때 GameManager로 게임을 직접 시작한다(Start Game On Play).
   → 미션 3개 룰렛 · 왼쪽 위 미션 표시 · 5,4,3,2,1 Start! · 미션 진행/클리어를 그대로 볼 수 있다.
3. 플레이어 수: 나 말고 가짜 플레이어(봇)를 넣어 Test Players명(기본 3명, 나 포함)으로 시험한다.
   봇은 AI처럼 돌아다니고 Player 태그라 생존자 수·처치 알림에 플레이어로 잡힌다.
   내가 도망자면 첫 번째 봇("Player 2 (술래)")이 술래 역할이다. 봇은 공격하지 않는다.
4. 단축키: F1 시작 연출 다시 보기 / F2 술래·도망자 바꿔서 다시 보기 (F1·F2는 봇도 새로 만든다)
           F3 가장 가까운 AI를 내가 처치 / F4 가장 가까운 플레이어 봇을 내가 처치 (둘 다 술래일 때만)
           F5 라운드 남은 시간을 10초로 (도망자 승리 결과 화면 시험)
           술래 승리 결과 화면: 술래로 F4를 봇 수만큼 누르면 된다.
           ESC 마우스 풀기

빌드에서는 아무것도 하지 않는다.
*/

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace GameServer
{
    [DefaultExecutionOrder(1000)]   // NetworkGameManager.Start가 정한 마우스 규칙을 기억했다가 끌 때 돌려준다.
    public class Test : MonoBehaviour
    {
        [Header("시작 연출 시험")]
        [Tooltip("테스트가 처음 켜질 때 서버 없이 게임을 시작한다(AI 배치 + 시작 연출).")]
        [SerializeField] private bool startGameOnPlay = true;
        [Tooltip("켜면 술래로 시험한다(미션 없이 카운트다운만, 공격 피해 있음, 공격 버튼 보임).")]
        [SerializeField] private bool playAsTagger;
        [Tooltip("비워 두면 무작위. 번호를 넣으면 MissionIntro 미션 목록의 그 미션들로 고정 (최대 3개)")]
        [SerializeField] private int[] fixedMissions = Array.Empty<int>();
        [SerializeField] private bool showHelp = true;

        [Header("플레이어 수 (나 포함)")]
        [Tooltip("나 + 봇. 3이면 봇 2명이 같이 들어온다.")]
        [SerializeField, Range(1, 4)] private int testPlayers = 3;
        [Tooltip("봇으로 쓸 캐릭터 프리팹. 비우면 씬의 AI 하나를 복사해서 쓴다.")]
        [SerializeField] private Character botPrefab;

        private readonly List<Character> bots = new();
        private SurvivorCounter survivors;

        private MissionIntro missionIntro;
        private RoundManager round;
        private GameManager gameManager;
        private bool started;
        private bool applied;
        private bool startedGame;
        private Func<bool> previousCanLock;

        // 테스트 중 마우스 잠금 규칙: 에디터에서는 Game 화면 위를 클릭할 때만 잠근다.
        // (항상 잠그면 플레이 중 인스펙터를 클릭해도 그 클릭이 잠금에 먹혀 체크박스 등을 누를 수 없다.)
        private static readonly Func<bool> TestCanLock = () =>
        {
#if UNITY_EDITOR
            var window = UnityEditor.EditorWindow.mouseOverWindow;
            return window != null && window.GetType().Name == "GameView";
#else
            return true;
#endif
        };
        private readonly List<(CharacterCombat combat, Action<IDamageable> handler)> hitHandlers = new();

        private void Start()
        {
            if (!Application.isEditor) { enabled = false; return; }   // 빌드에서는 항상 끈다.
            missionIntro = FindFirstObjectByType<MissionIntro>();
            gameManager = FindFirstObjectByType<GameManager>();
            survivors = FindFirstObjectByType<SurvivorCounter>();
            round = FindFirstObjectByType<RoundManager>();
            if (missionIntro == null)
                Debug.LogWarning("Test: 씬에 MissionIntro가 없습니다. Tools > 미션 UI 만들기를 먼저 실행하세요.");
            started = true;
            Apply();
        }

        // 체크박스를 켜면 OnEnable, 끄면 OnDisable이 불린다.
        // 처음 시작할 때는 다른 매니저들이 준비된 뒤(Start)에 적용한다.
        private void OnEnable()
        {
            if (started) Apply();
        }

        private void OnDisable() => Revert();

        private void Update()
        {
            if (!applied || missionIntro == null) return;

            if (missionIntro.LocalIsTagger != playAsTagger)
                missionIntro.LocalIsTagger = playAsTagger;   // 인스펙터에서 바꾸면 다음 연출부터 적용

            Keyboard k = Keyboard.current;
            if (k == null) return;
            if (k.f1Key.wasPressedThisFrame) Replay();
            if (k.f2Key.wasPressedThisFrame)
            {
                playAsTagger = !playAsTagger;
                missionIntro.LocalIsTagger = playAsTagger;
                Replay();
            }
            if (k.f3Key.wasPressedThisFrame) KillNearestAI();
            if (k.f4Key.wasPressedThisFrame) KillNearestBot();
            if (k.f5Key.wasPressedThisFrame)
            {
                if (round != null && round.IsRunning) round.SetRemaining(10f);
                else Debug.Log("Test: 라운드가 진행 중일 때(Start! 이후) F5를 누르세요.");
            }
        }

        // 조준 없이 처치 알림을 시험한다. 실제 공격과 같이 "내 캐릭터가 공격자"로 처리된다.
        private void KillNearestAI()
        {
            if (!missionIntro.IsLocalTagger) { Debug.Log("Test: 도망자는 처치할 수 없습니다. F2로 술래로 바꾸세요."); return; }
            var me = FindFirstObjectByType<PlayerInputSource>();
            if (me == null) return;

            CharacterHealth nearest = null;
            float best = float.MaxValue;
            foreach (var h in FindObjectsByType<CharacterHealth>(FindObjectsSortMode.None))
            {
                if (!h.IsAlive || h.gameObject == me.gameObject || h.CompareTag("Player")) continue;
                float d = (h.transform.position - me.transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = h; }
            }
            if (nearest != null) nearest.ApplyServerDeath(me.gameObject, true);
        }

        private void LateUpdate()
        {
            if (applied && CursorController.CanLock != TestCanLock)
                CursorController.CanLock = TestCanLock;
        }

        private void Apply()
        {
            if (applied || !Application.isEditor) return;
            applied = true;
            previousCanLock = CursorController.CanLock;
            CursorController.CanLock = TestCanLock;
            CursorController.Lock();

            foreach (var combat in FindObjectsByType<CharacterCombat>(FindObjectsSortMode.None))
            {
                GameObject attacker = combat.gameObject;
                Action<IDamageable> handler = target =>
                {
                    if (target is CharacterHealth h && h.ServerAuthoritative)
                        h.ApplyServerDeath(attacker, true);
                };
                combat.TargetHit += handler;
                hitHandlers.Add((combat, handler));
            }

            if (missionIntro != null) missionIntro.LocalIsTagger = playAsTagger;

            if (startGameOnPlay && !startedGame && gameManager != null && !gameManager.IsPlaying)
            {
                startedGame = true;
                gameManager.StartGame();   // GameStarted → MissionIntro가 시작 연출을 튼다.
                if (fixedMissions != null && fixedMissions.Length > 0 && missionIntro != null) Replay();   // 고정 미션이면 그걸로 다시
            }
            if (gameManager != null && gameManager.IsPlaying) SpawnBots();
            Debug.Log("Test: 켜짐 (F1 연출 다시 보기, F2 술래·도망자 전환)");
        }

        private void Revert()
        {
            if (!applied) return;
            applied = false;
            foreach (var (combat, handler) in hitHandlers)
                if (combat != null) combat.TargetHit -= handler;
            hitHandlers.Clear();
            RemoveBots();
            CursorController.CanLock = previousCanLock;
            Debug.Log("Test: 꺼짐");
        }

        private void Replay()
        {
            SpawnBots();   // 죽은 봇도 다시 살려서 매번 같은 인원으로 시작
            missionIntro.Play(playAsTagger, fixedMissions != null && fixedMissions.Length > 0 ? fixedMissions : null);
            AssignTagger();
        }

        // 내가 도망자면 첫 번째 봇이 술래. (승패 판정이 술래를 알아야 "도망자 전원 탈락"을 셀 수 있다)
        private void AssignTagger()
        {
            if (playAsTagger)
            {
                var me = FindFirstObjectByType<PlayerInputSource>();
                RoundRoles.Tagger = me != null ? me.GetComponent<Character>() : null;
            }
            else RoundRoles.Tagger = bots.Count > 0 ? bots[0] : null;
        }

        // ───────────── 플레이어 봇 ─────────────

        private void SpawnBots()
        {
            RemoveBots();
            int count = Mathf.Max(0, testPlayers - 1);
            if (count == 0) return;

            Character template = botPrefab != null ? botPrefab : FindAITemplate();
            var me = FindFirstObjectByType<PlayerInputSource>();
            if (template == null || me == null)
            {
                Debug.LogWarning("Test: 봇으로 복사할 캐릭터를 찾지 못했습니다. Bot Prefab을 넣어 주세요.");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                // 내 주변에 120도 간격으로 둔다.
                Vector3 pos = me.transform.position + Quaternion.Euler(0f, 120f * (i + 1), 0f) * Vector3.forward * 4f;
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 4f, NavMesh.AllAreas)) pos = hit.position;

                Character bot = Instantiate(template);
                bool isTaggerBot = !playAsTagger && i == 0;
                string displayName = $"Player {i + 2}" + (isTaggerBot ? " (술래)" : "");
                bot.name = displayName;
                bot.gameObject.tag = "Player";   // 생존자 수·처치 알림에서 플레이어로 잡힌다(AI 처치 패널티 없음)
                bot.gameObject.SetActive(true);
                bot.Spawn(pos, Quaternion.Euler(0f, 180f + 120f * (i + 1), 0f));

                // AI 복사본이라 죽으면 AI 풀로 돌아가려 한다(5초 뒤). 그 전에 지운다.
                bot.Died += dead => { if (dead != null) Destroy(dead.gameObject, 4f); };

                if (survivors != null) survivors.Register(bot, displayName);
                if (gameManager != null) gameManager.Register_Player(bot);
                bots.Add(bot);
            }
            AssignTagger();
            Debug.Log($"Test: 플레이어 {count + 1}명으로 시험 (나 + 봇 {count}명, 술래: {(playAsTagger ? "나" : "Player 2")})");
        }

        private void RemoveBots()
        {
            foreach (Character bot in bots)
            {
                if (bot == null) continue;
                if (survivors != null) survivors.Unregister(bot);
                if (gameManager != null) gameManager.Unregister_Player(bot);
                Destroy(bot.gameObject);
            }
            bots.Clear();
        }

        private static Character FindAITemplate()
        {
            foreach (var c in FindObjectsByType<Character>(FindObjectsSortMode.None))
                if (!c.CompareTag("Player") && c.GetComponent<NavAIInputSource>() != null) return c;
            return null;
        }

        // 술래일 때 가장 가까운 살아 있는 플레이어 봇을 내가 처치한 것으로 처리한다.
        private void KillNearestBot()
        {
            if (!missionIntro.IsLocalTagger) { Debug.Log("Test: 도망자는 처치할 수 없습니다. F2로 술래로 바꾸세요."); return; }
            var me = FindFirstObjectByType<PlayerInputSource>();
            if (me == null) return;
            CharacterHealth nearest = null;
            float best = float.MaxValue;
            foreach (Character bot in bots)
            {
                if (bot == null || !bot.IsAlive) continue;
                float d = (bot.transform.position - me.transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = bot.Health; }
            }
            if (nearest != null) nearest.ApplyServerDeath(me.gameObject, true);
        }

        private void OnGUI()
        {
            if (!applied || !showHelp) return;
            const float w = 330f, h = 138f;
            GUILayout.BeginArea(new Rect(10f, Screen.height - h - 10f, w, h), GUI.skin.box);
            GUILayout.Label($"[테스트] 지금 역할: {(playAsTagger ? "술래" : "도망자")}");
            GUILayout.Label("F1 연출 다시 보기   F2 술래·도망자 전환");
            GUILayout.Label("F3 AI 처치   F4 플레이어 봇 처치 (술래일 때)");
            GUILayout.Label("F5 남은 시간 10초로");
            GUILayout.Label("끄기: 인스펙터에서 Test 체크 해제");
            GUILayout.EndArea();
        }
    }
}
