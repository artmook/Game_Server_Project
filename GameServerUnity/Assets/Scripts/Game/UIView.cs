using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer
{
    // UI 오브젝트는 씬에 미리 있다 (Tools > HUD 만들기). 이 스크립트는 값 갱신과 연출만 맡는다.
    public class UIView : MonoBehaviour
    {
        [Header("스타일")]
        [SerializeField] private TMP_FontAsset font;   // 넣으면 하위 텍스트 전체에 즉시 적용
        [SerializeField] private Color accent = new Color(1f, 0.62f, 0.25f);

        [Header("참조 (HUD 만들기가 자동 연결)")]
        [SerializeField] private RectTransform skillRoot;
        [SerializeField] private Image glow;
        [SerializeField] private Image ringFill;
        [SerializeField] private Image cooldownFill;
        [SerializeField] private TMP_Text centerText;
        [SerializeField] private TMP_Text remainingText;
        [SerializeField] private RectTransform killFeedRoot;
        [SerializeField] private TMP_Text feedTemplate;   // 꺼져 있는 템플릿, 복제해서 사용

        private bool isReady = true;
        private float punchTime = 1f;
        private int lastTenths = -1;

        private readonly List<(TMP_Text text, float time)> feed = new();
        private const float FeedLife = 4f;
        private const int FeedMax = 5;

        public void SetCooldown(float remaining, float duration)
        {
            float t = duration > 0f ? Mathf.Clamp01(remaining / duration) : 0f;
            bool ready = remaining <= 0f;

            if (cooldownFill) cooldownFill.fillAmount = t;
            if (ringFill)
            {
                ringFill.fillAmount = 1f - t;
                ringFill.color = ready ? accent : Color.Lerp(accent, Color.white, 0.35f);
            }

            if (centerText)
            {
                if (ready)
                {
                    if (lastTenths != 0) { centerText.text = "공격"; centerText.fontSize = 30; lastTenths = 0; }
                }
                else
                {
                    int tenths = Mathf.CeilToInt(remaining * 10f);
                    if (tenths != lastTenths)
                    {
                        lastTenths = tenths;
                        centerText.text = (tenths / 10f).ToString("0.0");
                        centerText.fontSize = 40;
                    }
                }
            }

            if (ready && !isReady) punchTime = 0f;   // 방금 쿨이 끝남 → 펀치 연출
            isReady = ready;
        }

        public void SetRemainingPlayers(int remaining, int max)
        {
            if (!remainingText) return;
            remainingText.text =
                $"<size=60%><color=#FFFFFFAA>생존</color></size>  <b>{remaining}</b><size=70%><color=#FFFFFFAA> / {max}</color></size>";
        }

        public void ShowDeath(string displayName)
        {
            if (!feedTemplate || !killFeedRoot) return;

            TMP_Text t = Instantiate(feedTemplate, killFeedRoot);
            t.gameObject.SetActive(true);
            t.text = displayName == "AI"
                ? "<color=#FF6B6B>AI</color> 사망"
                : $"<color=#{ColorUtility.ToHtmlStringRGB(accent)}>{displayName}</color> 사망";
            t.transform.SetAsFirstSibling();   // 최신이 위로
            feed.Add((t, Time.unscaledTime));

            if (feed.Count > FeedMax) { Destroy(feed[0].text.gameObject); feed.RemoveAt(0); }
        }

        private void Update()
        {
            if (skillRoot)
            {
                if (punchTime < 1f)
                {
                    punchTime += Time.unscaledDeltaTime * 3f;
                    float s = 1f + Mathf.Sin(Mathf.Clamp01(punchTime) * Mathf.PI) * 0.12f;
                    skillRoot.localScale = Vector3.one * s;
                }
                else skillRoot.localScale = Vector3.one;
            }

            if (glow)
            {
                float a = isReady ? 0.25f + Mathf.Sin(Time.unscaledTime * 3f) * 0.1f : 0f;
                Color g = accent;
                g.a = Mathf.MoveTowards(glow.color.a, a, Time.unscaledDeltaTime * 2f);
                glow.color = g;
            }

            for (int i = feed.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - feed[i].time;
                if (age >= FeedLife) { Destroy(feed[i].text.gameObject); feed.RemoveAt(i); continue; }
                feed[i].text.alpha = Mathf.Clamp01((FeedLife - age) / 0.5f);
            }
        }

        // Inspector에서 폰트를 바꾸면 하위 텍스트 전체에 바로 적용 (씬 창에서 확인 가능)
        private void OnValidate()
        {
            if (font == null) return;
            foreach (TMP_Text t in GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.font == font) continue;
                t.font = font;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(t);
#endif
            }
        }
    }
}
