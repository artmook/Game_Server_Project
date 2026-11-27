/*
[ 코드 설명 ]

RoundView (라운드 화면)

1. 화면 위쪽 가운데 남은 시간 타이머(04:00). 30초 남으면 빨간색, 10초부터는 1초마다 톡 튄다.
2. 라운드 결과 화면: "술래 승리!" / "도망자 승리!" + 이유 + 이긴 플레이어 이름 + 나가기 버튼.
   술래·도망자 모두에게 똑같이 뜬다.
3. 편집 화면에서는 둘 다 보인다(Tools > 라운드 타이머·결과 화면 만들기). 실행하면 결과 화면은 숨겼다가 끝날 때만 나온다.
   결과 화면 미리보기가 거슬리면 하이어라키에서 Result를 꺼 두어도 된다(실행 중에는 알아서 켜진다).
4. 판정은 RoundManager가 하고, 이 스크립트는 보여 주기만 한다.
*/

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer
{
    public class RoundView : MonoBehaviour
    {
        [Header("타이머 (화면 위쪽 가운데)")]
        [SerializeField] private RectTransform timerRoot;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private Color timerColor = Color.white;
        [SerializeField] private Color warningColor = new Color(1f, 0.36f, 0.36f);
        [SerializeField] private float warningSeconds = 30f;
        [SerializeField] private float pulseSeconds = 10f;

        [Header("결과 화면")]
        [SerializeField] private CanvasGroup resultGroup;
        [SerializeField] private RectTransform resultPanel;
        [SerializeField] private Image resultFrame;
        [SerializeField] private TMP_Text resultTitle;
        [SerializeField] private TMP_Text resultReason;
        [SerializeField] private TMP_Text resultNamesLabel;
        [SerializeField] private TMP_Text resultNames;
        [SerializeField] private Button exitButton;
        [SerializeField] private Color taggerColor = new Color(1f, 0.45f, 0.35f);
        [SerializeField] private Color runnerColor = new Color(0.36f, 0.86f, 0.42f);

        private int lastWholeSecond = -1;
        private Coroutine timerPulse;
        private Coroutine resultAnim;

        public Button ExitButton => exitButton;

        private void Awake()
        {
            if (Application.isPlaying) HideResult();   // 편집 화면 미리보기는 실행할 때 숨긴다.
        }

        // ───────────── 타이머 ─────────────

        public static string TimeText(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }

        public void SetTime(float seconds)
        {
            if (!timerText) return;
            timerText.text = TimeText(seconds);
            timerText.color = seconds <= warningSeconds ? warningColor : timerColor;

            int whole = Mathf.CeilToInt(seconds);
            if (whole != lastWholeSecond)
            {
                bool tick = lastWholeSecond >= 0 && whole < lastWholeSecond && whole <= pulseSeconds && whole > 0;
                lastWholeSecond = whole;
                if (tick && isActiveAndEnabled)
                {
                    if (timerPulse != null) StopCoroutine(timerPulse);
                    timerPulse = StartCoroutine(Pulse(timerRoot ? timerRoot : timerText.rectTransform, 1.22f, 0.3f));
                }
            }
        }

        public void ResetTimer(float seconds)
        {
            lastWholeSecond = -1;
            if (timerPulse != null) StopCoroutine(timerPulse);
            timerPulse = null;
            if (timerRoot) timerRoot.localScale = Vector3.one;
            SetTime(seconds);
        }

        // ───────────── 결과 화면 ─────────────

        public static string TitleText(bool taggerWon) => taggerWon ? "술래 승리!" : "도망자 승리!";
        public static string NamesLabelText(bool taggerWon) => taggerWon ? "술래" : "도망자";

        public void ShowResult(bool taggerWon, string reason, string names)
        {
            if (!resultGroup) return;
            Color c = taggerWon ? taggerColor : runnerColor;
            if (resultTitle) { resultTitle.text = TitleText(taggerWon); resultTitle.color = c; }
            if (resultReason) resultReason.text = reason;
            if (resultNamesLabel) { resultNamesLabel.text = NamesLabelText(taggerWon); resultNamesLabel.color = c; }
            if (resultNames) resultNames.text = names;
            if (resultFrame) resultFrame.color = new Color(c.r, c.g, c.b, 0.6f);

            resultGroup.gameObject.SetActive(true);
            resultGroup.blocksRaycasts = true;
            resultGroup.interactable = true;
            if (resultAnim != null) StopCoroutine(resultAnim);
            resultAnim = StartCoroutine(PopIn());
        }

        public void HideResult()
        {
            if (resultAnim != null) StopCoroutine(resultAnim);
            resultAnim = null;
            if (!resultGroup) return;
            resultGroup.alpha = 0f;
            resultGroup.blocksRaycasts = false;
            resultGroup.interactable = false;
            resultGroup.gameObject.SetActive(false);
            if (resultPanel) resultPanel.localScale = Vector3.one;
        }

        private IEnumerator PopIn()
        {
            const float time = 0.4f;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                float k = t / time;
                resultGroup.alpha = Mathf.Clamp01(k * 2.5f);
                if (resultPanel) resultPanel.localScale = Vector3.one * BackOut(k, 0.7f);
                yield return null;
            }
            resultGroup.alpha = 1f;
            if (resultPanel) resultPanel.localScale = Vector3.one;
            resultAnim = null;
        }

        // 0.7배에서 시작해 살짝 넘쳤다가(1.06배) 제자리로
        private static float BackOut(float k, float from)
        {
            const float s = 2.2f;
            float x = k - 1f;
            float e = 1f + (s + 1f) * x * x * x + s * x * x;
            return Mathf.LerpUnclamped(from, 1f, e);
        }

        private static IEnumerator Pulse(RectTransform rt, float peak, float time)
        {
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                float k = t / time;
                rt.localScale = Vector3.one * Mathf.Lerp(peak, 1f, 1f - (1f - k) * (1f - k));
                yield return null;
            }
            rt.localScale = Vector3.one;
        }
    }
}
