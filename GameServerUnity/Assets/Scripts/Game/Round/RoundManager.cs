/*
[ 코드 설명 ]

RoundManager (라운드 시간 · 승패 판정)

1. 시작 연출의 "Start!" 순간(MissionIntro.GameplayStarted)부터 제한 시간(기본 4분)을 잰다.
   화면 위쪽 가운데 타이머에 남은 시간을 보여 준다.
2. 승패
   - 술래 승리: 도망자가 모두 잡혔을 때 (마지막 도망자가 쓰러지고 잠깐 뒤 결과 화면)
   - 도망자 승리: 시간이 끝났을 때 도망자가 한 명이라도 살아 있으면
3. 결과 화면은 술래·도망자 모두에게 뜬다. 이긴 쪽 플레이어 이름이 나오고(탈락한 도망자는 회색 줄),
   결과가 뜨면 조작을 멈추고 마우스를 풀어 "나가기" 버튼을 누를 수 있게 한다.
4. 나가기 버튼: 로비 씬이 아직 없어서 지금은 로그만 남긴다. On Exit에 나중에 씬 이동을 연결하면 된다.
5. 클라이언트 판정이다. 서버 코드는 고치지 않았고, 서버가 승패를 보내 주게 되면 ShowResult만 불러 주면 된다.
   술래 정보는 RoundRoles.Tagger를 쓴다(내가 도망자면 지금은 누가 술래인지 모른다 → 다른 플레이어 전원을 도망자로 센다).
6. 시작 연출을 다시 틀면(MissionIntro.IntroStarted) 타이머와 결과 화면이 처음 상태로 돌아간다.
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
        [SerializeField] private SurvivorCounter survivors;   // 플레이어 이름용(없어도 된다)
        [Tooltip("내 캐릭터. 비워 두면 PlayerInputSource가 붙은 캐릭터를 찾는다.")]
        [SerializeField] private Character localPlayer;

        [Header("규칙")]
        [Tooltip("라운드 제한 시간(초). 240 = 4분")]
        [SerializeField, Min(1f)] private float roundSeconds = 240f;
        [Tooltip("마지막 도망자가 쓰러진 뒤 결과 화면이 뜨기까지(초)")]
        [SerializeField, Min(0f)] private float endDelay = 1.5f;
        [SerializeField] private string playerTag = "Player";

        [Header("나가기 버튼")]
        [Tooltip("로비 씬이 생기면 여기에 씬 이동을 연결한다.")]
        public UnityEvent onExit = new UnityEvent();

        private readonly List<(Character character, string name)> runners = new();
        private bool running;
        private bool ended;
        private float remaining;
        private Coroutine pendingEnd;

        public float Remaining => remaining;
        public bool IsRunning => running;
        public float RoundSeconds => roundSeconds;

        private void Awake()
        {
            if (view == null) view = GetComponent<RoundView>();
            if (intro == null) intro = FindFirstObjectByType<MissionIntro>();
            if (survivors == null) survivors = FindFirstObjectByType<SurvivorCounter>();
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
            }
            CharacterHealth.AnyDied += HandleAnyDied;
        }

        private void OnDisable()
        {
            if (intro != null)
            {
                intro.IntroStarted -= ResetRound;
                intro.GameplayStarted -= BeginRound;
            }
            CharacterHealth.AnyDied -= HandleAnyDied;
        }

        private void Update()
        {
            if (!running) return;
            remaining -= Time.deltaTime;
            if (view != null) view.SetTime(remaining);
            if (remaining <= 0f)
            {
                remaining = 0f;
                running = false;
                if (CountAliveRunners() > 0) EndRound(false, "제한 시간 동안 살아남았습니다!");
                else EndRound(true, "모든 도망자를 잡았습니다!");
            }
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
                view.ResetTimer(roundSeconds);
                view.HideResult();
            }
        }

        private void BeginRound()
        {
            ResetRound();
            CollectRunners();
            running = true;
        }

        // 시작 순간의 도망자 명단(Player 태그 + 술래가 아님). 죽어서 사라져도 결과 화면에 이름이 남는다.
        private void CollectRunners()
        {
            runners.Clear();
            foreach (Character c in FindObjectsByType<Character>(FindObjectsSortMode.None))
            {
                if (!c.CompareTag(playerTag) || RoundRoles.IsTagger(c)) continue;
                runners.Add((c, NameOf(c)));
            }
        }

        private void HandleAnyDied(CharacterHealth victim, HitInfo hit)
        {
            if (!running || ended || !victim.CompareTag(playerTag)) return;
            if (runners.Count == 0 || CountAliveRunners() > 0) return;

            // 마지막 도망자가 쓰러졌다 → 타이머를 멈추고 잠깐 뒤 결과
            running = false;
            pendingEnd = StartCoroutine(EndAfter(endDelay));
        }

        private IEnumerator EndAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            pendingEnd = null;
            EndRound(true, "모든 도망자를 잡았습니다!");
        }

        private int CountAliveRunners()
        {
            int alive = 0;
            foreach (var (c, _) in runners) if (c != null && c.IsAlive) alive++;
            return alive;
        }

        private void EndRound(bool taggerWon, string reason)
        {
            if (ended) return;
            ended = true;
            running = false;
            GameFlow.ControlsLocked = true;   // 결과가 뜨면 모두 멈춘다
            GameFlow.RoundOver = true;        // 마우스를 풀어 나가기 버튼을 누를 수 있게
            CursorController.Unlock();
            if (view != null) view.ShowResult(taggerWon, reason, taggerWon ? TaggerNames() : RunnerNames());
        }

        // 서버가 승패를 알려 주게 되면 이걸 부르면 된다.
        public void ShowResult(bool taggerWon, string reason = null)
        {
            if (pendingEnd != null) StopCoroutine(pendingEnd);
            pendingEnd = null;
            ended = false;
            EndRound(taggerWon, reason ?? (taggerWon ? "모든 도망자를 잡았습니다!" : "제한 시간 동안 살아남았습니다!"));
        }

        // 테스트용: 남은 시간을 바꾼다.
        public void SetRemaining(float seconds)
        {
            if (!running) return;
            remaining = Mathf.Max(0.01f, seconds);
        }

        private void Exit()
        {
            Debug.Log("나가기: 로비 씬이 아직 없습니다. RoundManager의 On Exit에 씬 이동을 연결하세요.");
            onExit?.Invoke();
        }

        // ───────────── 이름 ─────────────

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
