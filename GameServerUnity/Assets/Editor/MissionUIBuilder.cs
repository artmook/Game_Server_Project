using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer.EditorTools
{
    // Tools > 미션 UI 만들기: Canvas 아래에 미션 룰렛(3줄)·왼쪽 위 미션 표시(3줄)·술래 안내·카운트다운 UI를 만들고 참조를 연결한다.
    // 만든 UI는 편집 화면에서도 미리보기로 보인다(게임을 실행하면 MissionIntro가 처음에 숨긴다).
    // 다시 실행하면 기존 MissionIntro를 지우고 새로 만든다(Ctrl+Z로 되돌릴 수 있음).
    public static class MissionUIBuilder
    {
        private static readonly Color Accent = new Color(1f, 0.62f, 0.25f);
        private static readonly Color Panel = new Color(0.08f, 0.06f, 0.05f, 0.85f);
        private static readonly string[] Preview = { "점프 5번 하기", "10초 동안 달리기", "5초 동안 가만히 있기", "20초 동안 걷기만 하기", "점프 10번 하기" };
        private static readonly string[] PreviewProgress = { "0/5", "0/10초", "0/5초" };

        // 룰렛
        private const int ReelCount = 3;
        private const int SlotsPerReel = 5;
        private const float SlotHeight = 56f;
        private const float ReelWidth = 560f;
        private const float ReelHeight = 84f;
        private const float ReelGap = 10f;

        // 왼쪽 위 미션 표시
        private const float LabelWidth = 320f;
        private const float RowHeight = 28f;

        [MenuItem("Tools/미션 UI 만들기")]
        private static void Build()
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("미션 UI 만들기", "씬에 Canvas가 없습니다.", "확인");
                return;
            }

            Sprite circle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/HUD/Circle.png");
            Sprite squareRing = SaveSquareRingSprite();   // 테두리만 있는 네모 (룰렛 강조 테두리용)
            TMP_FontAsset font = FindKoreanFont(canvas);

            Transform old = canvas.transform.Find("MissionIntro");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject("MissionIntro", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "미션 UI 만들기");
            var rootRt = (RectTransform)root.transform;
            rootRt.SetParent(canvas.transform, false);
            Stretch(rootRt);
            rootRt.SetAsLastSibling();   // HUD 위에 그린다.
            var view = root.AddComponent<MissionIntroView>();
            var intro = root.AddComponent<MissionIntro>();

            // ───────── 룰렛 (화면 중앙, 3줄) ─────────
            float reelsHeight = ReelCount * ReelHeight + (ReelCount - 1) * ReelGap;
            var rouletteGo = new GameObject("Roulette", typeof(RectTransform), typeof(CanvasGroup));
            var roulette = (RectTransform)rouletteGo.transform;
            roulette.SetParent(rootRt, false);
            roulette.sizeDelta = new Vector2(ReelWidth + 60f, reelsHeight + 84f);
            var rouletteGroup = rouletteGo.GetComponent<CanvasGroup>();
            rouletteGroup.blocksRaycasts = false;
            rouletteGroup.interactable = false;

            Image bg = NewImage("Background", roulette, circle, Panel);
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 4f;
            Stretch(bg.rectTransform);


            TMP_Text title = NewText("Title", roulette, 24, font);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -30f);
            title.rectTransform.sizeDelta = new Vector2(ReelWidth, 36f);
            title.text = "<b>MISSION</b>";
            title.color = Accent;
            title.characterSpacing = 12f;

            var reelRefs = new (RectTransform viewport, TMP_Text[] slots)[ReelCount];
            float firstY = -22f + (ReelCount - 1) * (ReelHeight + ReelGap) * 0.5f;
            for (int r = 0; r < ReelCount; r++)
            {
                float y = firstY - r * (ReelHeight + ReelGap);

                Image rowBg = NewImage($"ReelBackground{r}", roulette, circle, new Color(1f, 1f, 1f, 0.06f));
                rowBg.type = Image.Type.Sliced;
                rowBg.pixelsPerUnitMultiplier = 6f;
                rowBg.rectTransform.anchoredPosition = new Vector2(0f, y);
                rowBg.rectTransform.sizeDelta = new Vector2(ReelWidth, ReelHeight);

                // 마스크 영역: 가운데 칸 하나와 위아래 칸 일부만 보인다. 가장자리는 흐리게 잘린다.
                var viewportGo = new GameObject($"Reel{r}", typeof(RectTransform), typeof(RectMask2D));
                var viewport = (RectTransform)viewportGo.transform;
                viewport.SetParent(roulette, false);
                viewport.anchoredPosition = new Vector2(0f, y);
                viewport.sizeDelta = new Vector2(ReelWidth, ReelHeight);
                viewportGo.GetComponent<RectMask2D>().softness = new Vector2Int(0, 22);

                Image lineTop = NewImage("LineTop", viewport, null, new Color(Accent.r, Accent.g, Accent.b, 0.6f));
                lineTop.rectTransform.sizeDelta = new Vector2(ReelWidth - 60f, 2f);
                lineTop.rectTransform.anchoredPosition = new Vector2(0f, SlotHeight * 0.5f);
                Image lineBottom = NewImage("LineBottom", viewport, null, new Color(Accent.r, Accent.g, Accent.b, 0.6f));
                lineBottom.rectTransform.sizeDelta = new Vector2(ReelWidth - 60f, 2f);
                lineBottom.rectTransform.anchoredPosition = new Vector2(0f, -SlotHeight * 0.5f);

                var slots = new TMP_Text[SlotsPerReel];
                for (int s = 0; s < SlotsPerReel; s++)
                {
                    TMP_Text slot = NewText($"Slot{s}", viewport, 32, font);
                    slot.rectTransform.sizeDelta = new Vector2(ReelWidth - 20f, SlotHeight);
                    slot.fontStyle = FontStyles.Bold;
                    // 미리보기: 가운데 칸(s = 2)이 멈춘 모습. 위아래 칸은 흐리게.
                    int offset = SlotsPerReel / 2 - s;
                    slot.rectTransform.anchoredPosition = new Vector2(0f, offset * SlotHeight);
                    slot.text = Preview[(r + s) % Preview.Length];
                    slot.color = offset == 0 ? Accent : new Color(1f, 1f, 1f, 0f);   // 위아래 칸은 숨긴다(잘린 글자 방지)
                    slots[s] = slot;
                }
                reelRefs[r] = (viewport, slots);
            }

            // ───────── 미션 표시 (왼쪽 위, 3줄) ─────────
            float labelHeight = 46f + ReelCount * RowHeight + 8f;
            var labelGo = new GameObject("MissionLabel", typeof(RectTransform), typeof(CanvasGroup));
            var label = (RectTransform)labelGo.transform;
            label.SetParent(rootRt, false);
            label.anchorMin = label.anchorMax = label.pivot = new Vector2(0f, 1f);
            label.anchoredPosition = new Vector2(24f, -24f);
            label.sizeDelta = new Vector2(LabelWidth, labelHeight);
            var labelGroup = labelGo.GetComponent<CanvasGroup>();
            labelGroup.blocksRaycasts = false;
            labelGroup.interactable = false;

            Image labelBg = NewImage("Background", label, circle, new Color(Panel.r, Panel.g, Panel.b, 0.6f));
            labelBg.type = Image.Type.Sliced;
            labelBg.pixelsPerUnitMultiplier = 5f;
            Stretch(labelBg.rectTransform);

            Image bar = NewImage("AccentBar", label, null, Accent);
            bar.rectTransform.anchorMin = new Vector2(0f, 0f);
            bar.rectTransform.anchorMax = new Vector2(0f, 1f);
            bar.rectTransform.pivot = new Vector2(0f, 0.5f);
            bar.rectTransform.anchoredPosition = new Vector2(12f, 0f);
            bar.rectTransform.sizeDelta = new Vector2(4f, -22f);

            TMP_Text labelTitle = NewText("Title", label, 16, font);
            labelTitle.rectTransform.anchorMin = labelTitle.rectTransform.anchorMax = labelTitle.rectTransform.pivot = new Vector2(0f, 1f);
            labelTitle.rectTransform.anchoredPosition = new Vector2(28f, -12f);
            labelTitle.rectTransform.sizeDelta = new Vector2(LabelWidth - 40f, 22f);
            labelTitle.alignment = TextAlignmentOptions.MidlineLeft;
            labelTitle.characterSpacing = 8f;
            labelTitle.text = "<b>MISSION</b>";
            labelTitle.color = Accent;

            var rowRefs = new (RectTransform root, Image bullet, TMP_Text text)[ReelCount];
            for (int i = 0; i < ReelCount; i++)
            {
                var rowGo = new GameObject($"Row{i}", typeof(RectTransform));
                var row = (RectTransform)rowGo.transform;
                row.SetParent(label, false);
                row.anchorMin = row.anchorMax = new Vector2(0f, 1f);
                row.pivot = new Vector2(0f, 0.5f);   // 튈 때 왼쪽을 기준으로 커진다.
                row.anchoredPosition = new Vector2(28f, -46f - i * RowHeight);
                row.sizeDelta = new Vector2(LabelWidth - 40f, RowHeight);

                Image bullet = NewImage("Bullet", row, null, Accent);
                bullet.rectTransform.anchorMin = bullet.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                bullet.rectTransform.anchoredPosition = new Vector2(4f, 0f);
                bullet.rectTransform.sizeDelta = new Vector2(7f, 7f);

                TMP_Text text = NewText("Text", row, 19, font);
                Stretch(text.rectTransform);
                text.rectTransform.offsetMin = new Vector2(16f, 0f);
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.text = $"{Preview[i]}  <size=80%><color=#FF9E40>{PreviewProgress[i]}</color></size>";

                rowRefs[i] = (row, bullet, text);
            }

            // ───────── 술래 안내 (화면 위쪽) ─────────
            var noticeGo = new GameObject("TaggerNotice", typeof(RectTransform), typeof(CanvasGroup));
            var notice = (RectTransform)noticeGo.transform;
            notice.SetParent(rootRt, false);
            notice.anchorMin = notice.anchorMax = notice.pivot = new Vector2(0.5f, 1f);
            notice.anchoredPosition = new Vector2(0f, -150f);
            notice.sizeDelta = new Vector2(600f, 118f);
            var noticeGroup = noticeGo.GetComponent<CanvasGroup>();
            noticeGroup.blocksRaycasts = false;
            noticeGroup.interactable = false;

            Image noticeBg = NewImage("Background", notice, circle, Panel);
            noticeBg.type = Image.Type.Sliced;
            noticeBg.pixelsPerUnitMultiplier = 4f;
            Stretch(noticeBg.rectTransform);


            TMP_Text noticeText = NewText("Text", notice, 34, font);
            Stretch(noticeText.rectTransform);
            noticeText.rectTransform.offsetMin = new Vector2(20f, 10f);
            noticeText.rectTransform.offsetMax = new Vector2(-20f, -10f);
            noticeText.lineSpacing = -6f;
            noticeText.text = MissionIntroView.TaggerNoticeText(ColorUtility.ToHtmlStringRGB(Accent), 3);

            // ───────── 카운트다운 (화면 중앙) ─────────
            TMP_Text countdown = NewText("Countdown", rootRt, 150, font);
            countdown.rectTransform.sizeDelta = new Vector2(800f, 240f);
            countdown.fontStyle = FontStyles.Bold;
            countdown.outlineWidth = 0.2f;   // 게임 화면 위에 바로 떠서 테두리가 있어야 읽힌다.
            countdown.outlineColor = new Color32(0, 0, 0, 160);
            countdown.text = "5";

            // ───────── 참조 연결 ─────────
            var so = new SerializedObject(view);
            so.FindProperty("rouletteGroup").objectReferenceValue = rouletteGroup;
            so.FindProperty("rouletteFrame").objectReferenceValue = roulette;
            so.FindProperty("slotHeight").floatValue = SlotHeight;

            SerializedProperty reelsProp = so.FindProperty("reels");
            reelsProp.arraySize = ReelCount;
            for (int r = 0; r < ReelCount; r++)
            {
                SerializedProperty e = reelsProp.GetArrayElementAtIndex(r);
                e.FindPropertyRelative("viewport").objectReferenceValue = reelRefs[r].viewport;
                SerializedProperty slotsProp = e.FindPropertyRelative("slots");
                slotsProp.arraySize = SlotsPerReel;
                for (int s = 0; s < SlotsPerReel; s++)
                    slotsProp.GetArrayElementAtIndex(s).objectReferenceValue = reelRefs[r].slots[s];
            }

            so.FindProperty("missionLabelGroup").objectReferenceValue = labelGroup;
            so.FindProperty("missionLabelTitle").objectReferenceValue = labelTitle;
            so.FindProperty("missionLabelBar").objectReferenceValue = bar;
            SerializedProperty rowsProp = so.FindProperty("missionRows");
            rowsProp.arraySize = ReelCount;
            for (int i = 0; i < ReelCount; i++)
            {
                SerializedProperty e = rowsProp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("root").objectReferenceValue = rowRefs[i].root;
                e.FindPropertyRelative("bullet").objectReferenceValue = rowRefs[i].bullet;
                e.FindPropertyRelative("text").objectReferenceValue = rowRefs[i].text;
            }
            so.FindProperty("taggerNoticeGroup").objectReferenceValue = noticeGroup;
            so.FindProperty("taggerNoticeText").objectReferenceValue = noticeText;
            so.FindProperty("countdownText").objectReferenceValue = countdown;
            so.ApplyModifiedPropertiesWithoutUndo();
            RouletteSlotLayout.Arrange(view);   // 슬롯머신처럼 가로 3칸으로 배치

            var iso = new SerializedObject(intro);
            iso.FindProperty("view").objectReferenceValue = view;
            iso.FindProperty("gameManager").objectReferenceValue = Object.FindFirstObjectByType<GameManager>();
            iso.FindProperty("network").objectReferenceValue = Object.FindFirstObjectByType<NetworkGameManager>();
            var input = Object.FindFirstObjectByType<PlayerInputSource>();
            if (input != null) iso.FindProperty("localPlayer").objectReferenceValue = input.GetComponent<Character>();
            iso.ApplyModifiedPropertiesWithoutUndo();

            // 편집 화면에서는 새로 만든 글자의 모양이 바로 그려지지 않을 수 있어, 한 프레임 뒤 다시 그린다.
            EditorApplication.delayCall += () =>
            {
                if (root == null) return;
                // 오브젝트를 한 번 껐다 켜야 글자가 확실히 다시 그려진다.
                root.SetActive(false);
                root.SetActive(true);
                foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    t.ForceMeshUpdate(true, true);
                    EditorUtility.SetDirty(t);
                }
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            };

            Selection.activeGameObject = root;
            Debug.Log(font != null
                ? $"[미션 UI] 생성 완료 (폰트: {font.name}). 편집 화면에서 미리보기로 보이고, 실행하면 필요할 때만 나옵니다."
                : "[미션 UI] 생성 완료. 한글 폰트를 찾지 못했습니다. 텍스트에 한글 TMP 폰트를 넣으세요.");
        }

        // Tools > 미션 UI 미리보기 정리: 다시 만들지 않고(직접 고친 내용 유지)
        // 1) 룰렛 위아래 칸 글자를 숨기고 2) 미션 UI·처치 알림 글자의 테두리를 없앤다(글자 뒤 회색 사각형 방지).
        [MenuItem("Tools/미션 UI 미리보기 정리")]
        private static void HideReelNeighbors()
        {
            int outlines = RemoveTextOutlines();
            int changed = 0;
            foreach (MissionIntroView view in Object.FindObjectsByType<MissionIntroView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                SerializedProperty reels = new SerializedObject(view).FindProperty("reels");
                for (int r = 0; r < reels.arraySize; r++)
                {
                    SerializedProperty slots = reels.GetArrayElementAtIndex(r).FindPropertyRelative("slots");
                    int center = slots.arraySize / 2;
                    for (int s = 0; s < slots.arraySize; s++)
                    {
                        if (s == center) continue;
                        if (!(slots.GetArrayElementAtIndex(s).objectReferenceValue is TMP_Text t)) continue;
                        Undo.RecordObject(t, "미션 UI 미리보기 정리");
                        Color c = t.color;
                        c.a = 0f;
                        t.color = c;
                        EditorUtility.SetDirty(t);
                        changed++;
                    }
                }
            }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Debug.Log($"[미션 UI] 룰렛 위아래 칸 {changed}개를 숨기고, 글자 {outlines}개의 테두리를 없앴습니다.");
        }

        // 미션 UI(카운트다운 제외)와 처치 알림 글자의 테두리를 없애고 폰트 기본 머티리얼로 되돌린다.
        private static int RemoveTextOutlines()
        {
            var targets = new System.Collections.Generic.List<TMP_Text>();
            foreach (MissionIntroView view in Object.FindObjectsByType<MissionIntroView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                targets.AddRange(view.GetComponentsInChildren<TMP_Text>(true));
            foreach (UIView hud in Object.FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var feedRoot = new SerializedObject(hud).FindProperty("killFeedRoot").objectReferenceValue as RectTransform;
                if (feedRoot != null) targets.AddRange(feedRoot.GetComponentsInChildren<TMP_Text>(true));
            }

            int count = 0;
            foreach (TMP_Text t in targets)
            {
                if (t == null || t.font == null || t.name == "Countdown") continue;
                // TMP의 테두리 두께는 머티리얼에 저장된다. 테두리가 들어간 개별 머티리얼 대신 폰트 기본 머티리얼(테두리 없음)을 쓴다.
                Undo.RecordObject(t, "미션 UI 미리보기 정리");
                t.fontSharedMaterial = t.font.material;
                EditorUtility.SetDirty(t);
                t.ForceMeshUpdate(true, true);
                count++;
            }
            return count;
        }

        // HUD의 UIView 폰트 → Canvas 안 다른 텍스트 폰트 순서로 한글 폰트를 찾는다.
        private static TMP_FontAsset FindKoreanFont(Canvas canvas)
        {
            var uiView = Object.FindFirstObjectByType<UIView>();
            if (uiView != null)
            {
                var font = new SerializedObject(uiView).FindProperty("font").objectReferenceValue as TMP_FontAsset;
                if (font != null) return font;
            }
            foreach (TMP_Text t in canvas.GetComponentsInChildren<TMP_Text>(true))
                if (t.font != null && t.font.HasCharacter('공')) return t.font;
            return null;
        }

        // 테두리만 있는 네모 PNG를 Assets/UI/HUD/SquareRing.png로 저장하고 9-slice 스프라이트로 설정한다.
        // (원형 Ring.png는 공격 버튼이 쓰므로 그대로 둔다.)
        private static Sprite SaveSquareRingSprite()
        {
            const string dir = "Assets/UI/HUD";
            const string path = dir + "/SquareRing.png";
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(dir);
                const int size = 64;
                const int thickness = 6;   // 테두리 두께(픽셀)
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                        px[y * size + x] = new Color32(255, 255, 255, (byte)(edge < thickness ? 255 : 0));
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
                importer.filterMode = FilterMode.Point;   // 테두리가 흐려지지 않게
                importer.spriteBorder = new Vector4(thickness + 2, thickness + 2, thickness + 2, thickness + 2);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Image NewImage(string n, Transform parent, Sprite s, Color c)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.sprite = s; img.color = c; img.raycastTarget = false;
            return img;
        }

        private static TMP_Text NewText(string n, Transform parent, float size, TMP_FontAsset font)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            // 테두리(Outline)는 넣지 않는다. 폰트 아틀라스 여유 공간보다 두꺼우면 글자 뒤에 회색 사각형이 생긴다.
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
