using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer.EditorTools
{
    public static class HUDBuilder
    {
        private const string Dir = "Assets/UI/HUD";
        private static readonly Color Accent = new Color(1f, 0.62f, 0.25f);
        private static readonly Color Panel = new Color(0.08f, 0.06f, 0.05f, 0.6f);
        private const float SkillSize = 130f;

        [MenuItem("Tools/HUD 만들기")]
        private static void Build()
        {
            Canvas canvas = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<Canvas>()
                : Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("HUD 만들기", "씬에 Canvas가 없습니다.", "확인");
                return;
            }

            Sprite circle = SaveCircleSprite("Circle", 0f);
            Sprite ring = SaveCircleSprite("Ring", 0.86f);

            // 기존 HUD가 있으면 교체 (Ctrl+Z로 되돌릴 수 있음)
            Transform old = canvas.transform.Find("HUD");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var hud = new GameObject("HUD", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(hud, "HUD 만들기");
            var hudRt = (RectTransform)hud.transform;
            hudRt.SetParent(canvas.transform, false);
            Stretch(hudRt);
            var view = hud.AddComponent<UIView>();

            // ── 생존자 패널 (오른쪽 위) ──
            Image panel = NewImage("PlayersPanel", hudRt, circle, Panel);
            panel.type = Image.Type.Sliced;
            RectTransform prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-30f, -30f);
            prt.sizeDelta = new Vector2(210f, 64f);

            Image dot = NewImage("Dot", prt, circle, Accent);
            dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            dot.rectTransform.anchoredPosition = new Vector2(30f, 0f);
            dot.rectTransform.sizeDelta = new Vector2(14f, 14f);

            TMP_Text remaining = NewText("Remaining", prt, 34);
            Stretch(remaining.rectTransform);
            remaining.rectTransform.offsetMin = new Vector2(40f, 0f);
            remaining.rectTransform.offsetMax = new Vector2(-12f, 0f);
            remaining.text = "<size=60%><color=#FFFFFFAA>생존</color></size>  <b>3</b><size=70%><color=#FFFFFFAA> / 3</color></size>";

            // ── 사망 알림 (생존 패널 아래) ──
            var feedGo = new GameObject("KillFeed", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var feedRt = (RectTransform)feedGo.transform;
            feedRt.SetParent(hudRt, false);
            feedRt.anchorMin = feedRt.anchorMax = feedRt.pivot = new Vector2(1f, 1f);
            feedRt.anchoredPosition = new Vector2(-30f, -110f);
            feedRt.sizeDelta = new Vector2(400f, 220f);
            var layout = feedGo.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperRight;
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            TMP_Text template = NewText("FeedTemplate", feedRt, 26);
            template.alignment = TextAlignmentOptions.Right;
            template.rectTransform.sizeDelta = new Vector2(400f, 36f);
            template.text = "<color=#FF6B6B>AI</color> 사망";
            template.gameObject.SetActive(false);   // 꺼진 오브젝트는 레이아웃에서 제외됨

            // ── 스킬(공격) 버튼 (오른쪽 아래) ──
            var skillGo = new GameObject("SkillButton", typeof(RectTransform));
            var skill = (RectTransform)skillGo.transform;
            skill.SetParent(hudRt, false);
            skill.anchorMin = skill.anchorMax = new Vector2(1f, 0f);
            skill.anchoredPosition = new Vector2(-70f - SkillSize / 2f, 70f + SkillSize / 2f);
            skill.sizeDelta = Vector2.one * SkillSize;

            Image glow = Layer("Glow", skill, circle, new Color(Accent.r, Accent.g, Accent.b, 0.25f), 1.35f);
            Layer("Background", skill, circle, Panel, 1f);
            Layer("RingBack", skill, ring, new Color(1f, 1f, 1f, 0.15f), 1f);
            Image ringFill = Layer("RingFill", skill, ring, Accent, 1f);
            SetRadial(ringFill, 1f);
            Image cdFill = Layer("CooldownFill", skill, circle, new Color(0f, 0f, 0f, 0.55f), 0.86f);
            SetRadial(cdFill, 0f);

            TMP_Text center = NewText("Center", skill, 30);
            Stretch(center.rectTransform);
            center.text = "공격";

            Image badge = NewImage("KeyBadge", skill, circle, Accent);
            badge.type = Image.Type.Sliced;
            badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            badge.rectTransform.anchoredPosition = new Vector2(0f, 2f);
            badge.rectTransform.sizeDelta = new Vector2(64f, 26f);
            TMP_Text hint = NewText("Hint", badge.rectTransform, 16);
            Stretch(hint.rectTransform);
            hint.text = "<b>LMB</b>";
            hint.color = new Color(0.15f, 0.08f, 0.03f);
            hint.outlineWidth = 0f;

            // ── UIView 참조 자동 연결 ──
            var so = new SerializedObject(view);
            so.FindProperty("skillRoot").objectReferenceValue = skill;
            so.FindProperty("glow").objectReferenceValue = glow;
            so.FindProperty("ringFill").objectReferenceValue = ringFill;
            so.FindProperty("cooldownFill").objectReferenceValue = cdFill;
            so.FindProperty("centerText").objectReferenceValue = center;
            so.FindProperty("remainingText").objectReferenceValue = remaining;
            so.FindProperty("killFeedRoot").objectReferenceValue = feedRt;
            so.FindProperty("feedTemplate").objectReferenceValue = template;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 씬의 UIPresent가 있으면 새 UIView로 연결
            var presenter = Object.FindFirstObjectByType<UIPresent>();
            if (presenter != null)
            {
                var pso = new SerializedObject(presenter);
                pso.FindProperty("view").objectReferenceValue = view;
                pso.ApplyModifiedProperties();
            }

            Selection.activeGameObject = hud;
            Debug.Log("[HUD] 생성 완료. UIView의 Font에 한글 폰트를 넣으세요.");
        }

        // ───────── 도우미 ─────────

        private static Image Layer(string n, Transform parent, Sprite s, Color c, float scale)
        {
            Image img = NewImage(n, parent, s, c);
            img.rectTransform.sizeDelta = Vector2.one * SkillSize * scale;
            return img;
        }

        private static void SetRadial(Image img, float amount)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = true;
            img.fillAmount = amount;
        }

        private static Image NewImage(string n, Transform parent, Sprite s, Color c)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.sprite = s; img.color = c; img.raycastTarget = false;
            return img;
        }

        private static TMP_Text NewText(string n, Transform parent, float size)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            t.richText = true;
            t.enableWordWrapping = false;
            t.outlineWidth = 0.15f;
            t.outlineColor = new Color32(0, 0, 0, 160);
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // 원/링 PNG를 Assets/UI/HUD에 저장하고 9-slice 스프라이트로 설정
        private static Sprite SaveCircleSprite(string name, float inner)
        {
            string path = $"{Dir}/{name}.png";
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Dir);
                const int size = 256;
                float r = size / 2f;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                        float a = Mathf.Clamp01((1f - d) * r);
                        if (inner > 0f) a *= Mathf.Clamp01((d - inner) * r);
                        px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                tex.SetPixels32(px);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.spriteBorder = new Vector4(127, 127, 127, 127);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
