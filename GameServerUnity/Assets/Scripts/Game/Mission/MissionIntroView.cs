/*
[ 코드 설명 ]

MissionIntroView

1. UI 오브젝트는 Tools > 미션 UI 만들기로 미리 만들어 둔다. 편집 화면에서도 미리보기로 보이고,
   게임을 실행하면 처음에 숨겼다가 필요할 때만 켠다. 이 스크립트는 움직임만 맡는다.
2. 룰렛: 미션 3줄이 동시에 위에서 아래로 훅 내려가며 돌다가, 위 줄부터 하나씩 멈춘다.
   각 줄은 목표 칸을 살짝 지나쳤다가 되돌아와 "걸리는" 느낌으로 멈추고, 멈출 때 글자가 톡 튄다.
3. 칸 오브젝트는 몇 개만 두고 위치와 글자를 계속 바꿔 재활용한다.
4. 왼쪽 위 미션 표시: 뽑힌 미션 3개를 세로로 보여 주고 진행도(3/5)를 붙인다.
   한 개를 클리어하면 그 줄의 주황색이 초록색으로 바뀌고 그 줄이 "또잉" 튄다.
   3개를 모두 클리어하면 제목이 MISSION CLEAR(초록)로 바뀌고 표시 전체가 튄다.
5. 카운트다운: 숫자가 크게 튀어나왔다가 줄어들며 사라진다. 마지막에 Start!
6. 술래 안내: 도망자들이 룰렛을 보는 동안, 술래 화면 위쪽에 "도망자들이 미션을 뽑고 있어요..." 안내를 띄운다.
   점 3개가 차례로 깜빡이고 살짝 숨 쉬듯 커졌다 작아진다. 룰렛과 같은 시간 동안 보이고 카운트다운 전에 사라진다.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer
{
    public class MissionIntroView : MonoBehaviour
    {
        [Serializable]
        public class Reel
        {
            public RectTransform viewport;
            public TMP_Text[] slots;      // 재활용하는 미션 칸
        }

        [Serializable]
        public class MissionRow
        {
            public RectTransform root;
            public Image bullet;          // 줄 앞 작은 네모 (주황 → 초록)
            public TMP_Text text;
        }

        [Header("스타일")]
        [SerializeField] private Color accent = new Color(1f, 0.62f, 0.25f);
        [SerializeField] private Color clearColor = new Color(0.36f, 0.86f, 0.42f);

        [Header("룰렛 (화면 중앙)")]
        [SerializeField] private CanvasGroup rouletteGroup;
        [SerializeField] private RectTransform rouletteFrame;
        [SerializeField] private Reel[] reels;
        [SerializeField] private Image highlight;            // 다 멈췄을 때 반짝이는 테두리
        [SerializeField] private float slotHeight = 64f;
        [SerializeField] private int spinLoops = 4;          // 목표에 닿기 전 미션 목록을 몇 바퀴 도는지
        [SerializeField] private float reelStagger = 0.55f;  // 줄마다 멈추는 시간 차이

        [Header("미션 표시 (왼쪽 위)")]
        [SerializeField] private CanvasGroup missionLabelGroup;
        [SerializeField] private TMP_Text missionLabelTitle;
        [SerializeField] private Image missionLabelBar;      // 왼쪽 세로 막대
        [SerializeField] private MissionRow[] missionRows;

        [Header("술래 안내 (화면 위쪽)")]
        [SerializeField] private CanvasGroup taggerNoticeGroup;
        [SerializeField] private TMP_Text taggerNoticeText;

        [Header("카운트다운 (화면 중앙)")]
        [SerializeField] private TMP_Text countdownText;

        private const float Overshoot = 0.35f;   // 목표 칸을 지나치는 양(칸 단위)
        private const float SettleShare = 0.18f; // 회전 시간 중 되돌아와 멈추는 구간 비율
        private const float FadeTime = 0.35f;
        private const float NeighborFade = 2.2f;  // 가운데에서 멀어질수록 사라지는 속도 (0.45칸 벗어나면 0)

        private readonly System.Random visualRandom = new System.Random();
        private readonly Dictionary<RectTransform, Coroutine> bounces = new Dictionary<RectTransform, Coroutine>();
        private Vector2 labelHome;
        private bool initialized;

        public int MaxMissions => Mathf.Min(reels?.Length ?? 0, missionRows?.Length ?? 0);

        // 술래가 기다릴 시간. PlayRoulette 전체 길이와 같아야 카운트다운이 동시에 시작된다.
        public static float RouletteLength(float spinTime, float holdTime) => spinTime + holdTime + FadeTime;

        private void Awake() => Init();

        // MissionIntro.Awake가 이 Awake보다 먼저 HideAll을 부를 수 있으므로, 쓰기 전에 한 번 준비한다.
        private void Init()
        {
            if (initialized) return;
            initialized = true;
            if (missionLabelGroup) labelHome = ((RectTransform)missionLabelGroup.transform).anchoredPosition;
        }

        public void HideAll()
        {
            Init();
            bounces.Clear();
            SetAlpha(rouletteGroup, 0f);
            SetAlpha(missionLabelGroup, 0f);
            SetAlpha(taggerNoticeGroup, 0f);
            if (missionLabelGroup)
            {
                var rt = (RectTransform)missionLabelGroup.transform;
                rt.anchoredPosition = labelHome;
                rt.localScale = Vector3.one;
            }
            if (missionRows != null)
                foreach (MissionRow row in missionRows)
                    if (row.root) row.root.localScale = Vector3.one;
            if (countdownText) countdownText.gameObject.SetActive(false);
            if (highlight) SetImageAlpha(highlight, 0f);
        }

        // ───────────── 룰렛 ─────────────

        public IEnumerator PlayRoulette(string[] items, int[] targets, float spinTime, float holdTime)
        {
            int n = items.Length;
            int count = Mathf.Min(targets.Length, reels.Length);
            var start = new int[count];
            var end = new int[count];
            var stopAt = new float[count];
            var settled = new bool[count];
            var lastTick = new int[count];

            for (int r = 0; r < reels.Length; r++)
                if (reels[r].viewport) reels[r].viewport.gameObject.SetActive(r < count);

            for (int r = 0; r < count; r++)
            {
                start[r] = visualRandom.Next(n);
                end[r] = start[r] + (spinLoops + r) * n + Mod(targets[r] - start[r], n);
                stopAt[r] = Mathf.Max(0.6f, spinTime - (count - 1 - r) * reelStagger);   // 위 줄부터 멈춘다.
                lastTick[r] = int.MinValue;
            }

            SetAlpha(rouletteGroup, 1f);
            if (highlight) SetImageAlpha(highlight, 0f);
            float punch = 1f;

            for (float t = 0f; t < spinTime; t += Time.unscaledDeltaTime)
            {
                for (int r = 0; r < count; r++)
                {
                    if (settled[r]) continue;
                    if (t >= stopAt[r])
                    {
                        settled[r] = true;
                        LayoutReel(reels[r], items, end[r], true);
                        TMP_Text center = CenterSlot(reels[r], end[r]);
                        if (center) Bounce(center.rectTransform, 0.18f, 0.35f);
                        continue;
                    }

                    float p = ReelPosition(start[r], end[r], t, stopAt[r]);
                    int tick = Mathf.RoundToInt(p);
                    if (tick != lastTick[r]) { lastTick[r] = tick; punch = 0f; }
                    LayoutReel(reels[r], items, p, false);
                }

                // 칸이 바뀔 때마다 틀이 톡톡 튄다(느려질수록 뜸해진다).
                punch = Mathf.Min(1f, punch + Time.unscaledDeltaTime * 14f);
                if (rouletteFrame) rouletteFrame.localScale = Vector3.one * (1f + Mathf.Sin(punch * Mathf.PI) * 0.02f);
                yield return null;
            }

            for (int r = 0; r < count; r++)
                if (!settled[r]) LayoutReel(reels[r], items, end[r], true);
            if (rouletteFrame) rouletteFrame.localScale = Vector3.one;

            for (float t = 0f; t < holdTime; t += Time.unscaledDeltaTime)
            {
                if (highlight) SetImageAlpha(highlight, 0.55f + Mathf.Sin(t * 14f) * 0.45f * (1f - t / holdTime));
                yield return null;
            }

            for (float t = 0f; t < FadeTime; t += Time.unscaledDeltaTime)
            {
                SetAlpha(rouletteGroup, 1f - t / FadeTime);
                yield return null;
            }
            SetAlpha(rouletteGroup, 0f);
        }

        // 빠르게 돌다가 느려지고, 목표를 살짝 지나쳤다가 되돌아온다.
        private static float ReelPosition(int start, int end, float t, float duration)
        {
            float spinPart = duration * (1f - SettleShare);
            float settlePart = duration - spinPart;
            return t < spinPart
                ? Mathf.LerpUnclamped(start, end + Overshoot, EaseOutQuart(t / spinPart))
                : Mathf.LerpUnclamped(end + Overshoot, end, EaseOutBack((t - spinPart) / settlePart));
        }

        // p: 가운데에 오는 미션 번호(실수). 정수가 아니면 두 칸 사이를 지나는 중이다.
        private void LayoutReel(Reel reel, string[] items, float p, bool settled)
        {
            int n = items.Length;
            int first = Mathf.FloorToInt(p) - reel.slots.Length / 2;
            int centerIndex = Mathf.RoundToInt(p);

            for (int s = 0; s < reel.slots.Length; s++)
            {
                TMP_Text slot = reel.slots[s];
                int j = first + s;
                float offset = j - p;           // 0 = 가운데, 양수 = 위. p가 커지면 칸들이 아래로 내려간다.
                float dist = Mathf.Abs(offset);

                slot.text = items[Mod(j, n)];
                slot.rectTransform.anchoredPosition = new Vector2(0f, offset * slotHeight);
                if (!settled || j != centerIndex)
                    slot.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.8f, Mathf.Clamp01(dist));
                slot.color = settled && j == centerIndex ? accent : Color.white;
                // 위아래 칸은 반 칸만 벗어나도 완전히 투명하게 한다.
                // (줄 상자가 한 칸보다 커서, 흐리게라도 보이면 상자 경계에 반쯤 잘린 글자가 깨져 보인다.)
                slot.alpha = Mathf.Clamp01(1f - dist * NeighborFade);
            }
        }

        private static TMP_Text CenterSlot(Reel reel, int centerIndex)
        {
            int s = reel.slots.Length / 2;   // LayoutReel에서 정수 위치일 때 가운데 칸 번호
            return s < reel.slots.Length ? reel.slots[s] : null;
        }

        // ───────────── 왼쪽 위 미션 표시 ─────────────

        // 미션 목록으로 표시를 채우고 서서히 보여 준다. 남는 줄은 숨긴다.
        public void ShowMissions(string[] missions, string[] progress)
        {
            Init();
            if (!missionLabelGroup) return;
            SetTitle(false);
            for (int i = 0; i < missionRows.Length; i++)
            {
                bool used = i < missions.Length;
                if (missionRows[i].root) missionRows[i].root.gameObject.SetActive(used);
                if (used) SetMissionRow(i, missions[i], progress?[i], false);
            }
            StartCoroutine(FadeIn(missionLabelGroup));
        }

        // progress: "3/5" 같은 진행도. cleared면 그 줄을 초록색으로.
        public void SetMissionRow(int index, string mission, string progress, bool cleared)
        {
            if (missionRows == null || index < 0 || index >= missionRows.Length) return;
            MissionRow row = missionRows[index];
            Color c = cleared ? clearColor : accent;
            if (row.bullet) row.bullet.color = c;
            if (!row.text) return;
            string tail = string.IsNullOrEmpty(progress)
                ? ""
                : $"  <size=80%><color=#{ColorUtility.ToHtmlStringRGB(c)}>{progress}</color></size>";
            row.text.text = cleared ? $"<color=#{ColorUtility.ToHtmlStringRGB(clearColor)}>{mission}</color>{tail}" : mission + tail;
        }

        // 한 줄 클리어 순간 "또잉"
        public void PlayRowBounce(int index)
        {
            if (missionRows == null || index < 0 || index >= missionRows.Length || !missionRows[index].root) return;
            Bounce(missionRows[index].root, 0.22f, 0.7f);
        }

        // 전부 클리어: 제목·막대를 초록으로 바꾸고 표시 전체를 튕긴다.
        public void ShowAllCleared()
        {
            SetTitle(true);
            if (missionLabelGroup) Bounce((RectTransform)missionLabelGroup.transform, 0.2f, 0.75f, labelHome);
        }

        private void SetTitle(bool cleared)
        {
            Color c = cleared ? clearColor : accent;
            if (missionLabelBar) missionLabelBar.color = c;
            if (missionLabelTitle)
            {
                missionLabelTitle.text = cleared ? "<b>MISSION CLEAR</b>" : "<b>MISSION</b>";
                missionLabelTitle.color = c;
            }
        }

        private IEnumerator FadeIn(CanvasGroup group)
        {
            RectTransform rt = (RectTransform)group.transform;
            for (float t = 0f; t < FadeTime; t += Time.unscaledDeltaTime)
            {
                float k = EaseOutQuart(t / FadeTime);
                SetAlpha(group, k);
                rt.anchoredPosition = labelHome + new Vector2(-40f * (1f - k), 0f);   // 왼쪽에서 살짝 밀려 들어온다.
                yield return null;
            }
            SetAlpha(group, 1f);
            rt.anchoredPosition = labelHome;
        }

        // ───────────── 튀는 애니메이션 ─────────────

        // 커졌다가 살짝 작아졌다가 제자리로. home을 주면 기준점(왼쪽 위)과 상관없이 가운데를 중심으로 커진다.
        private void Bounce(RectTransform rt, float amount, float duration, Vector2? home = null)
        {
            if (bounces.TryGetValue(rt, out Coroutine running) && running != null) StopCoroutine(running);
            bounces[rt] = StartCoroutine(BounceRoutine(rt, amount, duration, home));
        }

        private IEnumerator BounceRoutine(RectTransform rt, float amount, float duration, Vector2? home)
        {
            Vector2 size = rt.rect.size;
            Vector2 pivotShift = new Vector2(0.5f - rt.pivot.x, 0.5f - rt.pivot.y);
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float s = 1f + amount * Mathf.Exp(-5.5f * t) * Mathf.Sin(t * 24f);
                rt.localScale = Vector3.one * s;
                if (home.HasValue)
                    rt.anchoredPosition = home.Value - Vector2.Scale(pivotShift, size) * (s - 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
            if (home.HasValue) rt.anchoredPosition = home.Value;
            bounces.Remove(rt);
        }

        // ───────────── 술래 안내 ─────────────

        // duration 동안 술래에게 "미션을 뽑는 중" 안내를 보여 준다. 끝나기 직전 서서히 사라진다.
        public IEnumerator PlayTaggerNotice(float duration)
        {
            if (!taggerNoticeGroup || !taggerNoticeText)
            {
                yield return new WaitForSecondsRealtime(duration);
                yield break;
            }

            RectTransform rt = (RectTransform)taggerNoticeGroup.transform;
            string accentHex = ColorUtility.ToHtmlStringRGB(accent);
            int lastDots = -1;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float fadeIn = Mathf.Clamp01(t / FadeTime);
                float fadeOut = Mathf.Clamp01((duration - t) / FadeTime);
                SetAlpha(taggerNoticeGroup, Mathf.Min(fadeIn, fadeOut));
                rt.localScale = Vector3.one * (1f + Mathf.Sin(t * 3f) * 0.015f);   // 숨 쉬듯 살짝

                int dots = (int)(t * 3f) % 4;   // 0~3개가 차례로 켜진다. 자리는 그대로라 글자가 흔들리지 않는다.
                if (dots != lastDots)
                {
                    lastDots = dots;
                    taggerNoticeText.text = TaggerNoticeText(accentHex, dots);
                }
                yield return null;
            }
            SetAlpha(taggerNoticeGroup, 0f);
            rt.localScale = Vector3.one;
        }

        public static string TaggerNoticeText(string accentHex, int dots)
        {
            string d = "";
            for (int i = 0; i < 3; i++) d += i < dots ? "." : "<alpha=#00>.<alpha=#FF>";
            return $"<size=60%><color=#{accentHex}><b>당신은 술래입니다</b></color></size>\n도망자들이 미션을 뽑고 있어요{d}";
        }

        // ───────────── 카운트다운 ─────────────

        public IEnumerator PlayCountdown(int from, Action onStart)
        {
            if (!countdownText) { onStart?.Invoke(); yield break; }
            countdownText.gameObject.SetActive(true);
            for (int i = from; i >= 1; i--)
                yield return Pop(i.ToString(), Color.white, 1f);

            onStart?.Invoke();
            yield return Pop("Start!", accent, 1f);
            countdownText.gameObject.SetActive(false);
        }

        private IEnumerator Pop(string text, Color color, float duration)
        {
            countdownText.text = text;
            countdownText.color = color;
            RectTransform rt = countdownText.rectTransform;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = t / duration;
                rt.localScale = Vector3.one * Mathf.LerpUnclamped(1.8f, 1f, EaseOutBack(Mathf.Clamp01(k / 0.3f)));
                countdownText.alpha = k < 0.1f ? k / 0.1f : k < 0.75f ? 1f : (1f - k) / 0.25f;
                yield return null;
            }
            countdownText.alpha = 0f;
        }

        // ───────────── 도우미 ─────────────

        private static int Mod(int a, int n) => ((a % n) + n) % n;
        private static float EaseOutQuart(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 4f);

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = Mathf.Clamp01(t) - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        private static void SetAlpha(CanvasGroup g, float a)
        {
            if (!g) return;
            g.alpha = a;
            g.gameObject.SetActive(a > 0f);
        }

        private static void SetImageAlpha(Image img, float a)
        {
            Color c = img.color;
            c.a = a;
            img.color = c;
        }
    }
}
