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

        [Header("처치 알림")]
        [SerializeField] private Color feedBackground = new Color(0f, 0f, 0f, 0.55f);   // 반투명 검정 배경
        [SerializeField] private Sprite feedBackgroundSprite;                            // 비우면 HUD의 Circle(둥근 모서리)
        [SerializeField] private Vector2 feedPadding = new Vector2(16f, 6f);
        [Tooltip("편집 화면 미리보기 겸 복제 원본. Tools > 처치 알림 미리보기 만들기로 만든다. 실행하면 숨긴다.")]
        [SerializeField] private RectTransform feedItemTemplate;

        private bool isReady = true;
        private float punchTime = 1f;
        private int lastTenths = -1;

        private readonly List<(CanvasGroup item, float time)> feed = new();
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

        // Show/hide the attack (skill) button. MissionIntro hides it for runners (non-taggers).
        public void SetSkillVisible(bool visible)
        {
            if (skillRoot) skillRoot.gameObject.SetActive(visible);
        }

        public void SetRemainingPlayers(int remaining, int max)
        {
            if (!remainingText) return;
            remainingText.text =
                $"<size=60%><color=#FFFFFFAA>생존</color></size>  <b>{remaining}</b><size=70%><color=#FFFFFFAA> / {max}</color></size>";
        }

        // 편집 화면에서 보이던 미리보기(복제 원본)를 실행할 때 숨긴다.
        private void Awake()
        {
            if (feedItemTemplate) feedItemTemplate.gameObject.SetActive(false);
        }

        // 술래(내 캐릭터)가 처치했을 때: "AI를 처리했습니다." 한 줄을 반투명 검정 배경과 함께 띄운다.
        public void ShowKill(string targetName)
        {
            if (!killFeedRoot) return;

            RectTransform box;
            if (feedItemTemplate)
            {
                box = Instantiate(feedItemTemplate, killFeedRoot);
                box.name = "KillFeedItem";
                box.gameObject.SetActive(true);
            }
            else
            {
                box = CreateFeedItem();
                if (!box) return;
            }

            SetFeedText(box, KillText(targetName));
            box.SetAsFirstSibling();   // 최신이 위로
            if (!box.TryGetComponent(out CanvasGroup group)) group = box.gameObject.AddComponent<CanvasGroup>();
            feed.Add((group, Time.unscaledTime));

            if (feed.Count > FeedMax) { Destroy(feed[0].item.gameObject); feed.RemoveAt(0); }
        }

        // 편집 화면용: KillFeed 아래에 미리보기(복제 원본)를 만들어 연결한다. (에디터 메뉴가 부른다)
        public RectTransform CreateKillFeedPreview()
        {
            RectTransform box = CreateFeedItem();
            if (!box) return null;
            box.name = "KillFeedItemTemplate";
            SetFeedText(box, KillText("AI"));
            feedItemTemplate = box;
            return box;
        }

        private string KillText(string targetName)
        {
            string color = ColorUtility.ToHtmlStringRGB(targetName == "AI" ? new Color(1f, 0.42f, 0.42f) : accent);
            return $"<color=#{color}>{targetName}</color>{ObjectParticle(targetName)} 처리했습니다.";
        }

        // 배경 상자(반투명 검정, 둥근 모서리) + 글자. 글자 스타일은 feedTemplate을 따른다.
        private RectTransform CreateFeedItem()
        {
            if (!feedTemplate || !killFeedRoot) return null;
            var go = new GameObject("KillFeedItem", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            var box = (RectTransform)go.transform;
            box.SetParent(killFeedRoot, false);
            var bg = go.GetComponent<Image>();
            bg.sprite = feedBackgroundSprite != null ? feedBackgroundSprite : glow != null ? glow.sprite : null;
            bg.type = bg.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            bg.pixelsPerUnitMultiplier = 8f;
            bg.color = feedBackground;
            bg.raycastTarget = false;

            TMP_Text t = Instantiate(feedTemplate, box);
            t.name = "Text";
            t.gameObject.SetActive(true);
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.fontSharedMaterial = t.font.material;   // 테두리 없는 기본 머티리얼 (배경이 있어 테두리 불필요, 회색 사각형 방지)
            return box;
        }

        // 글자를 넣고, 글자 길이에 맞춰 상자 크기를 정한다.
        private void SetFeedText(RectTransform box, string text)
        {
            TMP_Text t = box.GetComponentInChildren<TMP_Text>(true);
            if (!t) return;
            t.text = text;
            Vector2 size = t.GetPreferredValues(text);
            size.y = Mathf.Min(size.y, t.fontSize * 1.4f);
            var trt = t.rectTransform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = size;
            box.sizeDelta = size + feedPadding * 2f;
        }

        // 받침이 있으면 "을", 없으면 "를". 한글이 아니면 끝 글자를 읽는 소리로 판단한다(AI → 를, 2 → 를, 3 → 을).
        private static string ObjectParticle(string word)
        {
            if (string.IsNullOrEmpty(word)) return "을(를)";
            char c = word[word.Length - 1];
            if (c >= 0xAC00 && c <= 0xD7A3) return (c - 0xAC00) % 28 != 0 ? "을" : "를";
            if (char.IsDigit(c)) return "013678".IndexOf(c) >= 0 ? "을" : "를";   // 영·일·삼·육·칠·팔
            if (char.IsLetter(c)) return "LMNRlmnr".IndexOf(c) >= 0 ? "을" : "를";  // 엘·엠·엔·알
            return "을(를)";
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
                if (age >= FeedLife || feed[i].item == null)
                {
                    if (feed[i].item != null) Destroy(feed[i].item.gameObject);
                    feed.RemoveAt(i);
                    continue;
                }
                // 처음 0.15초 동안 서서히 나타나고, 마지막 0.5초 동안 사라진다(배경도 같이).
                feed[i].item.alpha = Mathf.Min(Mathf.Clamp01(age / 0.15f), Mathf.Clamp01((FeedLife - age) / 0.5f));
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
