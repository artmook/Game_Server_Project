using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer.EditorTools
{
    // Tools > UI 디자인 적용 (Neumorphic)
    // Basic 폴더의 Neumorphic UI - Basic 에셋으로 게임 안 UI 전체를 밝은 회색 뉴모픽 스타일로 바꾼다.
    //  1) 그림 가져오기 설정: Sprite / 밉맵 끔 / 압축 없음 / 9-슬라이스 테두리(패널·버튼)
    //  2) Canvas Scaler: 1920x1080 기준으로 화면 크기에 맞춰 늘고 줄어든다(Scale With Screen Size, Match 0.5)
    //  3) HUD · 미션 연출 · 술래 가리개 · 타이머 · 결과 화면의 배경/글자 색을 바꾼다.
    //     위치·크기는 건드리지 않는다(직접 옮겨 둔 배치 유지). 원래 배경은 지우지 않고 Image만 꺼 둔다.
    //     새 배경은 각 UI 아래 "Neu"라는 자식으로 붙는다. 다시 실행해도 같은 결과(겹쳐 쌓이지 않음).
    // 에셋 원본의 버튼 그림에는 영어 글자(Exit 등)가 박혀 있어, 글자만 지운 빈 버튼을 Assets/UI/Neumorphic에 두고 쓴다.
    public static class NeumorphicUIApplier
    {
        // ── 팔레트 (에셋 배경색 #E0E5EC 기준) ──
        public static readonly Color Base = new Color32(224, 229, 236, 255);
        public static readonly Color TextMain = new Color32(69, 82, 106, 255);     // 본문 글자
        public static readonly Color TextSub = new Color32(135, 147, 168, 255);    // 보조 글자
        public static readonly Color Accent = new Color32(242, 140, 56, 255);      // 주황 (강조)
        public static readonly Color Green = new Color32(46, 173, 91, 255);        // 미션 클리어 / 도망자 승리
        public static readonly Color Red = new Color32(225, 45, 72, 255);          // 에셋 PlayButton_red 색 / 경고
        public static readonly Color Track = new Color32(200, 208, 220, 255);      // 링 바탕

        private const string BasicDir = "Assets/Basic/Textures";
        private const string NeuDir = "Assets/UI/Neumorphic";

        // 그림별 모양 정보(픽셀): 몸통 모서리 반지름 R, 몸통 바깥 그림자·빛 번짐 폭 G, 9-슬라이스 테두리(왼,아래,오른,위)
        private struct Shape
        {
            public Sprite sprite; public float R; public float G;
            public Shape(Sprite s, float r, float g) { sprite = s; R = r; G = g; }
        }

        private static Shape panelH, panelV, pill, pillPressed;
        private static Sprite circle;

        [MenuItem("Tools/UI 디자인 적용 (Neumorphic)")]
        private static void Apply()
        {
            if (!LoadSprites()) return;
            int scalers = SetupCanvasScalers();

            Undo.SetCurrentGroupName("UI 디자인 적용 (Neumorphic)");
            int group = Undo.GetCurrentGroup();
            var touched = new List<GameObject>();

            StyleHud(touched);
            StyleMission(touched);
            StyleRound(touched);

            Undo.CollapseUndoOperations(group);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Refresh(touched);
            Debug.Log($"[UI 디자인] Neumorphic 스타일 적용 완료. Canvas Scaler {scalers}개를 1920x1080 기준으로 맞췄습니다. Ctrl+Z로 한 번에 되돌릴 수 있습니다.");
        }

        // ───────────── 1. 그림 가져오기 설정 ─────────────

        private static bool LoadSprites()
        {
            // 에셋 원본
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { BasicDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                Vector4 border = file == "panel_horizontal" ? new Vector4(150, 140, 150, 140)
                               : file == "panel_vertical" ? new Vector4(150, 150, 150, 150)
                               : Vector4.zero;
                ConfigureSprite(path, border);
            }
            // 글자를 지운 빈 버튼
            ConfigureSprite($"{NeuDir}/NeuPill.png", new Vector4(140, 145, 140, 145));
            ConfigureSprite($"{NeuDir}/NeuPillPressed.png", new Vector4(140, 138, 140, 138));
            ConfigureSprite($"{NeuDir}/NeuCircle.png", Vector4.zero);

            panelH = new Shape(Load($"{BasicDir}/panel_horizontal.png"), 63f, 76f);
            panelV = new Shape(Load($"{BasicDir}/panel_vertical.png"), 63f, 76f);
            pill = new Shape(Load($"{NeuDir}/NeuPill.png"), 77f, 66f);
            pillPressed = new Shape(Load($"{NeuDir}/NeuPillPressed.png"), 68f, 66f);
            circle = Load($"{NeuDir}/NeuCircle.png");

            if (panelH.sprite == null || pill.sprite == null || circle == null)
            {
                EditorUtility.DisplayDialog("UI 디자인 적용",
                    "Assets/Basic/Textures 또는 Assets/UI/Neumorphic 그림을 찾지 못했습니다.", "확인");
                return false;
            }
            return true;
        }

        private static void ConfigureSprite(string path, Vector4 border)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;               // UI는 밉맵이 필요 없다(흐려짐 방지)
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;               // 원본 크기 그대로 (가장 큰 그림 1819px)
            importer.textureCompression = TextureImporterCompression.Uncompressed;   // 부드러운 그림자 줄무늬 방지
            importer.spriteBorder = border;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;   // 9-슬라이스용
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static Sprite Load(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        // ───────────── 2. Canvas Scaler ─────────────

        private static int SetupCanvasScalers()
        {
            int n = 0;
            foreach (CanvasScaler scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Canvas canvas = scaler.GetComponent<Canvas>();
                if (canvas != null && canvas.renderMode == RenderMode.WorldSpace) continue;
                Undo.RecordObject(scaler, "Canvas Scaler");
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);   // 지금 배치를 만든 Full HD 기준
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;                          // 가로·세로 비율이 달라도 고르게
                scaler.referencePixelsPerUnit = 100f;
                if (canvas != null)
                {
                    Undo.RecordObject(canvas, "Canvas");
                    canvas.pixelPerfect = false;   // 크기가 바뀌는 UI 애니메이션이 떨리지 않게
                }
                n++;
            }
            return n;
        }

        // ───────────── 3. HUD ─────────────

        private static void StyleHud(List<GameObject> touched)
        {
            foreach (UIView view in Object.FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(view);
                SetColor(so, "accent", Accent);
                SetColor(so, "subTextColor", TextSub);
                SetColor(so, "ringChargingColor", Color.Lerp(Accent, Base, 0.45f));
                SetColor(so, "penaltyColor", Red);
                SetColor(so, "feedAIColor", Red);
                SetColor(so, "feedBackground", Color.white);
                so.FindProperty("feedBackgroundSprite").objectReferenceValue = pill.sprite;
                // 처치 알림 상자 높이 약 40 → 알약 모양 그림 배율
                float feedM = PillScale(40f);
                so.FindProperty("feedSpriteScale").floatValue = feedM;
                so.FindProperty("feedGlowOutset").floatValue = pill.G / feedM;
                so.FindProperty("feedPadding").vector2Value = new Vector2(22f, 6f);
                so.ApplyModifiedProperties();

                var skill = so.FindProperty("skillRoot").objectReferenceValue as RectTransform;
                if (skill == null) continue;   // 아래는 공격 버튼이 연결된 진짜 HUD만
                Transform hud = skill.parent;
                touched.Add(hud.gameObject);

                // 생존자 수
                Transform players = hud.Find("PlayersPanel");
                if (players != null)
                {
                    HideImage(players);
                    AddPill(players, Color.white);
                    SetText(players.Find("Remaining"), TextMain);
                    SetImageColor(players.Find("Dot"), Accent);
                }

                // 공격 버튼: 둥근 뉴모픽 버튼 + 링
                Transform bg = skill.Find("Background");
                if (bg != null && bg.TryGetComponent(out Image bgImg))
                {
                    Undo.RecordObject(bgImg, "UI 디자인");
                    Undo.RecordObject(bg, "UI 디자인");
                    var rt = (RectTransform)bg;
                    float body = Mathf.Min(rt.sizeDelta.x, rt.sizeDelta.y);
                    if (bgImg.sprite != circle) rt.sizeDelta = Vector2.one * body * 289f / 150f;   // 몸통(150px)이 원래 크기가 되게
                    bgImg.sprite = circle;
                    bgImg.type = Image.Type.Simple;
                    bgImg.preserveAspect = true;
                    bgImg.color = Color.white;
                }
                SetImageColor(skill.Find("RingBack"), Track);
                SetImageColor(skill.Find("RingFill"), Accent);
                SetImageColor(skill.Find("CooldownFill"), new Color(0.45f, 0.5f, 0.6f, 0.28f));
                SetText(skill.Find("Center"), TextMain);
                SetImageColor(skill.Find("KeyBadge"), Accent);
                SetText(skill.Find("KeyBadge/Hint"), Color.white);

                // 공격 패널티 알림
                Transform penalty = skill.Find("PenaltyNotice");
                if (penalty != null)
                {
                    HideImage(penalty.Find("Background"));
                    HideImage(penalty.Find("Frame"));
                    AddPanel(penalty, panelH, 16f, Color.white);
                    SetText(penalty.Find("Text"), TextMain);
                    var pt = penalty.Find("Text")?.GetComponent<TMP_Text>();
                    if (pt != null) { Undo.RecordObject(pt, "UI 디자인"); pt.text = UIView.PenaltyText(2f, Red); }
                }

                // 처치 알림: 글자 템플릿 색 + 미리보기 다시 만들기(새 배경)
                var feedTemplate = so.FindProperty("feedTemplate").objectReferenceValue as TMP_Text;
                if (feedTemplate != null) SetText(feedTemplate.transform, TextMain);
                var oldPreview = so.FindProperty("feedItemTemplate").objectReferenceValue as RectTransform;
                if (oldPreview != null) Undo.DestroyObjectImmediate(oldPreview.gameObject);
                RectTransform preview = view.CreateKillFeedPreview();
                if (preview != null)
                {
                    Undo.RegisterCreatedObjectUndo(preview.gameObject, "UI 디자인");
                    so.Update();
                    so.FindProperty("feedItemTemplate").objectReferenceValue = preview;
                    so.ApplyModifiedProperties();
                }

                // 남은 시간 타이머
                Transform timer = hud.Find("RoundTimer");
                if (timer != null)
                {
                    HideImage(timer.Find("Background"));
                    HideImage(timer.Find("Frame"));
                    AddPanel(timer, panelH, 22f, Color.white);
                    SetText(timer.Find("Label"), TextSub);
                    SetText(timer.Find("Time"), TextMain);
                }
            }
        }

        // ───────────── 4. 미션 연출 · 술래 가리개 ─────────────

        private static void StyleMission(List<GameObject> touched)
        {
            var view = Object.FindFirstObjectByType<MissionIntroView>(FindObjectsInactive.Include);
            if (view == null) return;
            touched.Add(view.gameObject);
            var so = new SerializedObject(view);
            SetColor(so, "accent", Accent);
            SetColor(so, "clearColor", Green);
            SetColor(so, "slotColor", TextMain);
            so.ApplyModifiedProperties();

            // 룰렛
            var roulette = (so.FindProperty("rouletteGroup").objectReferenceValue as CanvasGroup)?.transform;
            if (roulette != null)
            {
                HideImage(roulette.Find("Background"));
                AddPanel(roulette, panelH, 30f, Color.white);
                SetText(roulette.Find("Title"), TextSub);
                SetImageColor(roulette.Find("Highlight"), new Color(Accent.r, Accent.g, Accent.b, 0.9f));
                for (int i = 0; i < 8; i++)
                {
                    Transform reelBg = roulette.Find($"ReelBackground{i}");
                    if (reelBg == null) continue;
                    HideImage(reelBg);
                    AddPressedPill(reelBg, Color.white);   // 안으로 파인 칸
                    Transform reel = roulette.Find($"Reel{i}");
                    if (reel != null)
                        foreach (TMP_Text t in reel.GetComponentsInChildren<TMP_Text>(true)) SetText(t.transform, TextMain);
                }
            }

            // 왼쪽 위 미션 표시
            var label = (so.FindProperty("missionLabelGroup").objectReferenceValue as CanvasGroup)?.transform;
            if (label != null)
            {
                HideImage(label.Find("Background"));
                AddPanel(label, panelH, 22f, Color.white);
                SetImageColor(label.Find("AccentBar"), Accent);
                SetText(label.Find("Title"), Accent);
                var rows = so.FindProperty("missionRows");
                for (int i = 0; i < rows.arraySize; i++)
                {
                    var row = rows.GetArrayElementAtIndex(i);
                    var text = row.FindPropertyRelative("text").objectReferenceValue as TMP_Text;
                    var bullet = row.FindPropertyRelative("bullet").objectReferenceValue as Image;
                    if (text != null) SetText(text.transform, TextMain);
                    if (bullet != null) SetImageColor(bullet.transform, Accent);
                }
            }

            // 술래 가리개 + 안내
            var cover = so.FindProperty("taggerCover").objectReferenceValue as CanvasGroup;
            if (cover != null) SetImageColor(cover.transform, Base);   // 에셋 배경색으로 화면을 덮는다
            var notice = (so.FindProperty("taggerNoticeGroup").objectReferenceValue as CanvasGroup)?.transform;
            if (notice != null)
            {
                HideImage(notice.Find("Background"));
                HideImage(notice.Find("Frame"));
                AddPanel(notice, panelH, 30f, Color.white);
                SetText(notice.Find("Text"), TextMain);
            }
        }

        // ───────────── 5. 결과 화면 ─────────────

        private static void StyleRound(List<GameObject> touched)
        {
            var view = Object.FindFirstObjectByType<RoundView>(FindObjectsInactive.Include);
            if (view == null) return;
            touched.Add(view.gameObject);
            var so = new SerializedObject(view);
            SetColor(so, "timerColor", TextMain);
            SetColor(so, "warningColor", Red);
            SetColor(so, "taggerColor", Red);
            SetColor(so, "runnerColor", Green);
            so.ApplyModifiedProperties();

            var result = (so.FindProperty("resultGroup").objectReferenceValue as CanvasGroup)?.transform;
            if (result == null) return;
            SetImageColor(result.Find("Dim"), new Color(0.16f, 0.19f, 0.25f, 0.55f));

            var panel = so.FindProperty("resultPanel").objectReferenceValue as RectTransform;
            if (panel != null)
            {
                HideImage(panel.Find("Background"));
                HideImage(panel.Find("Frame"));
                AddPanel(panel, panelV, 44f, Color.white);
                SetText(panel.Find("Title"), Green);        // 미리보기용. 실행 중에는 승패에 따라 RoundView가 칠한다.
                SetText(panel.Find("NamesLabel"), Green);
                SetText(panel.Find("Reason"), TextSub);
                SetImageColor(panel.Find("Divider"), new Color(Track.r, Track.g, Track.b, 0.8f));
                SetText(panel.Find("Names"), TextMain);

                // 나가기 버튼: 볼록한 알약 → 누르면 오목한 알약(에셋의 pressed 그림)
                var button = (so.FindProperty("exitButton").objectReferenceValue as Button);
                if (button != null)
                {
                    HideImage(button.transform);
                    Image face = AddPill(button.transform, Color.white);
                    face.raycastTarget = true;   // 버튼을 누르는 영역
                    Undo.RecordObject(button, "UI 디자인");
                    button.targetGraphic = face;
                    button.transition = Selectable.Transition.SpriteSwap;
                    button.spriteState = new SpriteState { pressedSprite = pillPressed.sprite, selectedSprite = null, highlightedSprite = null };
                    SetText(button.transform.Find("Text"), TextMain);
                }
            }
        }

        // ───────────── 도우미 ─────────────

        // 패널 그림을 target 뒤에 깐다. radius = 화면에서의 모서리 반지름.
        private static Image AddPanel(Transform target, Shape shape, float radius, Color tint)
        {
            float m = shape.R / Mathf.Max(1f, radius);
            return AddNeu(target, shape.sprite, m, shape.G / m, tint);
        }

        // 알약 모양(양 끝이 반원). 높이에 맞춰 배율을 정한다.
        private static Image AddPill(Transform target, Color tint)
        {
            float h = ((RectTransform)target).rect.height;
            float m = PillScale(h);
            return AddNeu(target, pill.sprite, m, pill.G / m, tint);
        }

        private static Image AddPressedPill(Transform target, Color tint)
        {
            float h = ((RectTransform)target).rect.height;
            float m = (279f - 2f * pillPressed.G) / Mathf.Max(1f, h);
            return AddNeu(target, pillPressed.sprite, m, pillPressed.G / m, tint);
        }

        // 알약 그림(높이 292px, 위아래 그림자 66px씩)의 몸통이 높이 h가 되는 배율
        private static float PillScale(float h) => (292f - 2f * pill.G) / Mathf.Max(1f, h);

        private static Image AddNeu(Transform target, Sprite sprite, float multiplier, float outset, Color tint)
        {
            Transform old = target.Find("Neu");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var go = new GameObject("Neu", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "UI 디자인");
            var rt = (RectTransform)go.transform;
            rt.SetParent(target, false);
            rt.SetAsFirstSibling();   // 글자·아이콘 뒤에
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = -Vector2.one * outset;   // 그림자·빛 번짐은 상자 바깥으로
            rt.offsetMax = Vector2.one * outset;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = multiplier;
            img.color = tint;
            img.raycastTarget = false;
            return img;
        }

        // 원래 배경은 지우지 않고 Image만 끈다(되돌리기 쉽게).
        private static void HideImage(Transform t)
        {
            if (t == null || !t.TryGetComponent(out Image img)) return;
            Undo.RecordObject(img, "UI 디자인");
            img.enabled = false;
        }

        private static void SetImageColor(Transform t, Color c)
        {
            if (t == null || !t.TryGetComponent(out Image img)) return;
            Undo.RecordObject(img, "UI 디자인");
            img.color = c;
        }

        private static void SetText(Transform t, Color c)
        {
            if (t == null || !t.TryGetComponent(out TMP_Text text)) return;
            Undo.RecordObject(text, "UI 디자인");
            text.color = c;
            if (text.font != null) text.fontSharedMaterial = text.font.material;   // 테두리 없는 기본 재질
        }

        private static void SetColor(SerializedObject so, string name, Color c)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.colorValue = c;
        }

        // 편집 화면에서 새 글자·그림이 바로 그려지지 않을 수 있어 한 프레임 뒤 껐다 켜서 다시 그린다.
        private static void Refresh(List<GameObject> roots)
        {
            EditorApplication.delayCall += () =>
            {
                foreach (GameObject go in roots)
                {
                    if (go == null) continue;
                    foreach (TMP_Text t in go.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate(true, true);
                    if (!go.activeSelf) continue;
                    go.SetActive(false);
                    go.SetActive(true);
                }
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            };
        }
    }
}
