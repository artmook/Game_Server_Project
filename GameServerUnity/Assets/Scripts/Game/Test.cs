/*
[ 코드 설명 ]

Test (에디터 전용, 서버 없이 클라이언트 기능 시험)

켜고 끄기: 인스펙터에서 Test 컴포넌트 이름 옆 체크박스로 한다.
- 플레이 전에 꺼 두면 테스트 없이 실행된다.
- 플레이 중에 끄면 바로 원래대로 돌아가고(마우스 잠금 규칙, 원격 플레이어 즉사 규칙), 다시 켜면 다시 적용된다.

켜져 있을 때
1. 서버 없이 혼자 플레이한다. 원격 플레이어(ServerAuthoritative)를 때리면 바로 죽는다.
   (도망자는 애초에 공격 피해가 없으므로, 술래로 시험할 때만 해당된다.)
2. 처음 켜질 때 GameManager로 게임을 직접 시작한다(Start Game On Play).
   → 미션 3개 룰렛 · 왼쪽 위 미션 표시 · 5,4,3,2,1 Start! · 미션 진행/클리어를 그대로 볼 수 있다.
3. 단축키: F1 시작 연출 다시 보기 / F2 술래·도망자 바꿔서 다시 보기
           F3 가장 가까운 AI를 내가 처치(술래일 때만, 처치 알림 확인용) / ESC 마우스 풀기

빌드에서는 아무것도 하지 않는다.
*/

using System;
using System.Collections.Generic;
using UnityEngine;
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

        private MissionIntro missionIntro;
        private GameManager gameManager;
        private bool started;
        private bool applied;
        private bool startedGame;
        private Func<bool> previousCanLock;
        private readonly List<(CharacterCombat combat, Action<IDamageable> handler)> hitHandlers = new();

        private void Start()
        {
            if (!Application.isEditor) { enabled = false; return; }   // 빌드에서는 항상 끈다.
            missionIntro = FindFirstObjectByType<MissionIntro>();
            gameManager = FindFirstObjectByType<GameManager>();
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
            if (applied && CursorController.CanLock != null && !CursorController.CanLock())
                CursorController.CanLock = () => true;
        }

        private void Apply()
        {
            if (applied || !Application.isEditor) return;
            applied = true;
            previousCanLock = CursorController.CanLock;
            CursorController.CanLock = () => true;
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
            Debug.Log("Test: 켜짐 (F1 연출 다시 보기, F2 술래·도망자 전환)");
        }

        private void Revert()
        {
            if (!applied) return;
            applied = false;
            foreach (var (combat, handler) in hitHandlers)
                if (combat != null) combat.TargetHit -= handler;
            hitHandlers.Clear();
            CursorController.CanLock = previousCanLock;
            Debug.Log("Test: 꺼짐");
        }

        private void Replay() =>
            missionIntro.Play(playAsTagger, fixedMissions != null && fixedMissions.Length > 0 ? fixedMissions : null);

        private void OnGUI()
        {
            if (!applied || !showHelp) return;
            const float w = 320f, h = 96f;
            GUILayout.BeginArea(new Rect(10f, Screen.height - h - 10f, w, h), GUI.skin.box);
            GUILayout.Label($"[테스트] 지금 역할: {(playAsTagger ? "술래" : "도망자")}");
            GUILayout.Label("F1 연출 다시 보기   F2 술래·도망자 전환   F3 AI 처치");
            GUILayout.Label("끄기: 인스펙터에서 Test 체크 해제");
            GUILayout.EndArea();
        }
    }
}
