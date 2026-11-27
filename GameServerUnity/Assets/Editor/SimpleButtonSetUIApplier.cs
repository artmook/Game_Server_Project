using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace GameServer.EditorTools
{
    // Tools > UI 디자인 적용 (Simple Button Set)
    // TWD 폴더의 Simple Button Set 02 에셋으로 게임 안 UI 전체를 남색·파란 버튼 스타일로 바꾼다.
    //  1) 그림 가져오기 설정: Sprite / 밉맵 끔 / 압축 없음 / 9-슬라이스 테두리(패널·버튼)
    //  2) Canvas Scaler: 1920x1080 기준으로 화면 크기에 맞춰 늘고 줄어든다(Scale With Screen Size, Match 0.5)
    //  3) 에셋 글꼴 Poppins-Bold로 숫자·영어 글자(타이머, 카운트다운, MISSION)를 쓴다. 한글은 기존 글꼴.
    //  4) HUD · 미션 연출 · 술래 가리개 · 타이머 · 결과 화면의 배경/버튼/글자 색을 바꾼다.
    //     위치·크기는 건드리지 않는다. 원래 배경은 Image만 꺼 두고, 새 배경은 각 UI 아래 "Skin" 자식으로 붙는다.
    //     이전 Neumorphic 디자인("Neu")은 지운다. 다시 실행해도 같은 결과.
    public static class SimpleButtonSetUIApplier
    {
        // ── 팔레트 (에셋 샘플 씬 기준: 반투명 남색 패널 + 하늘색 강조 + 흰 글자) ──
        public static readonly Color Panel = new Color(0.03f, 0.1f, 0.18f, 0.82f);
        public static readonly Color PanelDeep = new Color(0.02f, 0.07f, 0.13f, 0.92f);
        public static readonly Color Inset = new Color(0f, 0f, 0f, 0.35f);          // 샘플 입력칸처럼 어두운 홈
        public static readonly Color Cyan = new Color(0.04f, 0.74f, 1f);            // 샘플 슬라이더 Fill 색
        public static readonly Color Green = new Color(0.3f, 0.85f, 0.4f);
        public static readonly Color Red = new Color(1f, 0.36f, 0.38f);
        public static readonly Color TaggerOrange = new Color(1f, 0.55f, 0.25f);
        public static readonly Color TextMain = Color.white;
        public static readonly Color TextSub = new Color(0.72f, 0.84f, 0.95f);

        private const string Root = "Assets/TWD/FreeButtonSet";
        private const string Tex = Root + "/Textures";
        private const string OutDir = "Assets/UI/SimpleButtonSet";
        private const string FontPath = Root + "/Fonts/Poppins-Bold.ttf";
        private const string FontAssetPath = OutDir + "/Poppins-Bold SDF.asset";

        // 그림 + 그 그림의 모서리 반지름(픽셀)
        private struct Shape
        {
            public Sprite sprite; public float R;
            public Shape(Sprite s, float r) { sprite = s; R = r; }
        }

        private static Shape panel20, panel40, button100, button200;
        private static Sprite buttonHover, buttonDown, buttonInactive, background, userIcon;
        private static TMP_FontAsset poppins;

        [MenuItem("Tools/UI 디자인 적용 (Simple Button Set)")]
        private static void Apply()
        {
            // 게임 씬이 아니면(예: 에셋 샘플 씬) 아무것도 바꾸지 않는다.
            if (Object.FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
            {
                EditorUtility.DisplayDialog("UI 디자인 적용", "지금 열린 씬에 게임 UI(HUD)가 없습니다. Game 씬을 열고 다시 실행하세요.", "확인");
                return;
            }
            if (!LoadSprites()) return;
            poppins = LoadOrCreatePoppins();
            int scalers = SetupCanvasScalers();

            Undo.SetCurrentGroupName("UI 디자인 적용 (Simple Button Set)");
            int group = Undo.GetCurrentGroup();
            var touched = new List<GameObject>();

            try
            {
                StyleHud(touched);
                StyleMission(touched);
                StyleRound(touched);
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Refresh(touched);
            Debug.Log($"[UI 디자인] Simple Button Set 스타일 적용 완료. Canvas Scaler {scalers}개를 1920x1080 기준으로 맞췄습니다. Ctrl+Z로 한 번에 되돌릴 수 있습니다.");
        }

        // ───────────── 1. 그림 · 글꼴 ─────────────

        private static bool LoadSprites()
        {
            var borders = new Dictionary<string, float>
            {
                { "universal_panel_10", 10 }, { "universal_panel_20", 22 }, { "universal_panel_30", 32 }, { "universal_panel_40", 42 },
                { "button_50", 12 }, { "button_100", 22 }, { "button_150", 32 }, { "button_200", 42 },
            };
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Tex }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(path);
                float b = 0f;
                foreach (var kv in borders) if (file == kv.Key || file.StartsWith(kv.Key + "_")) b = kv.Value;
                ConfigureSprite(path, new Vector4(b, b, b, b), file.StartsWith("button_") || file.StartsWith("universal_panel"));
            }
            ConfigureSprite("Assets/TWD/background.png", Vector4.zero, false);

            panel20 = new Shape(Load($"{Tex}/other/universal_panel_20.png"), 20f);
            panel40 = new Shape(Load($"{Tex}/other/universal_panel_40.png"), 40f);
            button100 = new Shape(Load($"{Tex}/buttons/button_100.png"), 20f);
            button200 = new Shape(Load($"{Tex}/buttons/button_200.png"), 40f);
            buttonHover = Load($"{Tex}/buttons/button_100_hover.png");
            buttonDown = Load($"{Tex}/buttons/button_100_down.png");
            buttonInactive = Load($"{Tex}/buttons/button_100_inactive.png");
            background = Load("Assets/TWD/background.png");
            userIcon = Load($"{Tex}/icons/64x64/user.png");

            if (panel20.sprite == null || button100.sprite == null)
            {
                EditorUtility.DisplayDialog("UI 디자인 적용", "Assets/TWD/FreeButtonSet 그림을 찾지 못했습니다.", "확인");
                return false;
            }
            return true;
        }

        private static void ConfigureSprite(string path, Vector4 border, bool fullRect)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;   // 배경(1920x1080)도 원본 크기 그대로
            importer.textureCompression = TextureImporterCompression.Uncompressed;   // 그라데이션 줄무늬 방지
            importer.spriteBorder = border;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (fullRect) settings.spriteMeshType = SpriteMeshType.FullRect;   // 9-슬라이스용
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static Sprite Load(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        // Poppins-Bold로 TMP 글꼴을 만든다(필요한 글자를 그때그때 그리는 Dynamic). 한글은 기존 글꼴로 대신 그린다.
        private static TMP_FontAsset LoadOrCreatePoppins()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null)
            {
                // 한글이 섞여도 네모로 깨지지 않게 기존 한글 글꼴을 대체 글꼴로 넣어 둔다.
                if (existing.fallbackFontAssetTable == null || existing.fallbackFontAssetTable.Count == 0)
                {
                    TMP_FontAsset ko = FindKoreanFont();
                    if (ko != null)
                    {
                        existing.fallbackFontAssetTable = new List<TMP_FontAsset> { ko };
                        EditorUtility.SetDirty(existing);
                        AssetDatabase.SaveAssets();
                    }
                }
                return existing;
            }
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) { Debug.LogWarning("[UI 디자인] Poppins-Bold.ttf를 찾지 못해 기존 글꼴을 씁니다."); return null; }

            TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic);
            if (fa == null) return null;
            fa.name = "Poppins-Bold SDF";
            Directory.CreateDirectory(OutDir);
            AssetDatabase.CreateAsset(fa, FontAssetPath);
            fa.atlasTextures[0].name = "Poppins-Bold SDF Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = "Poppins-Bold SDF Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);

            TMP_FontAsset korean = FindKoreanFont();
            if (korean != null) fa.fallbackFontAssetTable = new List<TMP_FontAsset> { korean };
            fa.TryAddCharacters("0123456789:.!/+- ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        // Poppins 글꼴 + 남색 테두리 재질(카운트다운용). TMP 재질 목록에 "Poppins-Bold SDF Outline"으로 나온다.
        private static Material OutlineMaterial()
        {
            string path = OutDir + "/Poppins-Bold SDF Outline.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(poppins.material) { name = "Poppins-Bold SDF Outline" };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture(ShaderUtilities.ID_MainTex, poppins.atlasTexture);
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.18f);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(3, 25, 46, 220));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static TMP_FontAsset FindKoreanFont()
        {
            foreach (TMP_Text t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.font != null && t.font.HasCharacter('공')) return t.font;
            return null;
        }

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
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                scaler.referencePixelsPerUnit = 100f;
                if (canvas != null) { Undo.RecordObject(canvas, "Canvas"); canvas.pixelPerfect = false; }
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
                SetColor(so, "accent", Cyan);
                SetColor(so, "subTextColor", new Color(TextSub.r, TextSub.g, TextSub.b, 0.85f));
                SetColor(so, "ringChargingColor", Color.Lerp(Cyan, Color.white, 0.45f));
                SetColor(so, "penaltyColor", Red);
                SetColor(so, "feedAIColor", Red);
                SetColor(so, "feedBackground", Panel);
                so.FindProperty("feedBackgroundSprite").objectReferenceValue = panel20.sprite;
                so.FindProperty("feedSpriteScale").floatValue = panel20.R / 12f;   // 모서리 12
                so.FindProperty("feedGlowOutset").floatValue = 0f;
                so.FindProperty("feedPadding").vector2Value = new Vector2(18f, 7f);
                so.ApplyModifiedProperties();

                var skill = so.FindProperty("skillRoot").objectReferenceValue as RectTransform;
                if (skill == null) continue;
                Transform hud = skill.parent;
                touched.Add(hud.gameObject);

                // 생존자 수: 남색 패널 + 사람 아이콘
                Transform players = hud.Find("PlayersPanel");
                if (players != null)
                {
                    HideImage(players);
                    AddSkin(players, panel20, 14f, Panel);
                    SetText(players.Find("Remaining"), TextMain);
                    Transform dot = players.Find("Dot");
                    if (dot != null && dot.TryGetComponent(out Image dotImg))
                    {
                        Undo.RecordObject(dotImg, "UI 디자인");
                        Undo.RecordObject(dot, "UI 디자인");
                        dotImg.sprite = userIcon;
                        dotImg.type = Image.Type.Simple;
                        dotImg.preserveAspect = true;
                        dotImg.color = Cyan;
                        ((RectTransform)dot).sizeDelta = new Vector2(26f, 26f);
                    }
                }

                // 공격 버튼: 파란 버튼(둥근 사각) + 하늘색 링
                Transform bg = skill.Find("Background");
                if (bg != null && bg.TryGetComponent(out Image bgImg))
                {
                    Undo.RecordObject(bgImg, "UI 디자인");
                    Undo.RecordObject(bg, "UI 디자인");
                    var rt = (RectTransform)bg;
                    if (bgImg.sprite != null && bgImg.sprite.name == "NeuCircle")   // Neumorphic에서 키운 크기를 되돌린다
                        rt.sizeDelta = rt.sizeDelta * 150f / 289f;
                    bgImg.enabled = true;
                    bgImg.sprite = button200.sprite;
                    bgImg.type = Image.Type.Sliced;
                    bgImg.pixelsPerUnitMultiplier = button200.R / 36f;
                    bgImg.preserveAspect = false;
                    bgImg.color = Color.white;
                }
                SetImageColor(skill.Find("RingBack"), new Color(1f, 1f, 1f, 0.18f));
                SetImageColor(skill.Find("RingFill"), Cyan);
                SetImageColor(skill.Find("CooldownFill"), new Color(0f, 0.02f, 0.06f, 0.5f));
                SetText(skill.Find("Center"), TextMain);
                Transform badge = skill.Find("KeyBadge");
                if (badge != null && badge.TryGetComponent(out Image badgeImg))
                {
                    Undo.RecordObject(badgeImg, "UI 디자인");
                    badgeImg.sprite = Load($"{Tex}/other/universal_panel_10.png");
                    badgeImg.type = Image.Type.Sliced;
                    badgeImg.pixelsPerUnitMultiplier = 1f;
                    badgeImg.color = Cyan;
                }
                SetText(skill.Find("KeyBadge/Hint"), new Color(0.02f, 0.1f, 0.2f));

                // 공격 패널티 알림
                Transform penalty = skill.Find("PenaltyNotice");
                if (penalty != null)
                {
                    HideImage(penalty.Find("Background"));
                    HideImage(penalty.Find("Frame"));
                    AddSkin(penalty, panel20, 12f, new Color(0.2f, 0.04f, 0.07f, 0.88f));
                    SetText(penalty.Find("Text"), TextMain);
                    var pt = penalty.Find("Text")?.GetComponent<TMP_Text>();
                    if (pt != null) { Undo.RecordObject(pt, "UI 디자인"); pt.text = UIView.PenaltyText(2f, Red); }
                }

                // 처치 알림
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
                    AddSkin(timer, panel20, 16f, Panel);
                    SetText(timer.Find("Label"), TextSub);
                    SetText(timer.Find("Time"), TextMain, poppins);
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
            SetColor(so, "accent", Cyan);
            SetColor(so, "clearColor", Green);
            SetColor(so, "slotColor", TextMain);
            so.ApplyModifiedProperties();

            var roulette = (so.FindProperty("rouletteGroup").objectReferenceValue as CanvasGroup)?.transform;
            if (roulette != null)
            {
                HideImage(roulette.Find("Background"));
                AddSkin(roulette, panel40, 26f, PanelDeep);
                SetText(roulette.Find("Title"), Cyan, poppins);
                SetImageColor(roulette.Find("Highlight"), new Color(Cyan.r, Cyan.g, Cyan.b, 0.9f));
                for (int i = 0; i < 8; i++)
                {
                    Transform reelBg = roulette.Find($"ReelBackground{i}");
                    if (reelBg == null) continue;
                    HideImage(reelBg);
                    AddSkin(reelBg, panel20, 14f, Inset);
                    Transform reel = roulette.Find($"Reel{i}");
                    if (reel != null)
                        foreach (TMP_Text t in reel.GetComponentsInChildren<TMP_Text>(true)) SetText(t.transform, TextMain);
                }
            }

            var label = (so.FindProperty("missionLabelGroup").objectReferenceValue as CanvasGroup)?.transform;
            if (label != null)
            {
                HideImage(label.Find("Background"));
                AddSkin(label, panel20, 16f, Panel);
                SetImageColor(label.Find("AccentBar"), Cyan);
                SetText(label.Find("Title"), Cyan, poppins);
                var rows = so.FindProperty("missionRows");
                for (int i = 0; i < rows.arraySize; i++)
                {
                    var row = rows.GetArrayElementAtIndex(i);
                    var text = row.FindPropertyRelative("text").objectReferenceValue as TMP_Text;
                    var bullet = row.FindPropertyRelative("bullet").objectReferenceValue as Image;
                    if (text != null) SetText(text.transform, TextMain);
                    if (bullet != null) SetImageColor(bullet.transform, Cyan);
                }
            }

            // 술래 가리개: 에셋 배경 그림으로 화면을 덮는다(화면 비율이 달라도 꽉 차게, 찌그러지지 않게)
            var cover = so.FindProperty("taggerCover").objectReferenceValue as CanvasGroup;
            if (cover != null && background != null)
            {
                HideImage(cover.transform);
                RemoveChild(cover.transform, "Skin");
                var go = new GameObject("Skin", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
                Undo.RegisterCreatedObjectUndo(go, "UI 디자인");
                var rt = (RectTransform)go.transform;
                rt.SetParent(cover.transform, false);
                rt.SetAsFirstSibling();
                var img = go.GetComponent<Image>();
                img.sprite = background;
                img.color = Color.white;
                img.raycastTarget = false;
                var fitter = go.GetComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = background.rect.width / background.rect.height;
            }
            var notice = (so.FindProperty("taggerNoticeGroup").objectReferenceValue as CanvasGroup)?.transform;
            if (notice != null)
            {
                HideImage(notice.Find("Background"));
                HideImage(notice.Find("Frame"));
                AddSkin(notice, panel40, 24f, PanelDeep);
                SetText(notice.Find("Text"), TextMain);
            }

            // 카운트다운: Poppins 굵은 숫자 + 남색 테두리
            var countdown = so.FindProperty("countdownText").objectReferenceValue as TMP_Text;
            if (countdown != null && poppins != null)
            {
                Undo.RecordObject(countdown, "UI 디자인");
                countdown.font = poppins;
                countdown.fontSharedMaterial = OutlineMaterial();   // 꺼진 오브젝트라 outlineWidth 대신 재질로 테두리를 준다
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
            SetColor(so, "taggerColor", TaggerOrange);
            SetColor(so, "runnerColor", Green);
            so.ApplyModifiedProperties();

            var result = (so.FindProperty("resultGroup").objectReferenceValue as CanvasGroup)?.transform;
            if (result == null) return;
            SetImageColor(result.Find("Dim"), new Color(0.01f, 0.04f, 0.09f, 0.6f));

            var panel = so.FindProperty("resultPanel").objectReferenceValue as RectTransform;
            if (panel == null) return;
            HideImage(panel.Find("Background"));
            HideImage(panel.Find("Frame"));
            AddSkin(panel, panel40, 36f, PanelDeep);
            SetText(panel.Find("Title"), Green);
            SetText(panel.Find("NamesLabel"), Green);
            SetText(panel.Find("Reason"), TextSub);
            SetImageColor(panel.Find("Divider"), new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f));
            SetText(panel.Find("Names"), TextMain);

            // 나가기 버튼: 에셋 버튼 그림(보통/올림/누름/비활성)을 그대로 쓴다
            var button = so.FindProperty("exitButton").objectReferenceValue as Button;
            if (button != null && button.TryGetComponent(out Image face))
            {
                RemoveChild(button.transform, "Neu");
                Undo.RecordObject(face, "UI 디자인");
                face.enabled = true;
                face.sprite = button100.sprite;
                face.type = Image.Type.Sliced;
                face.pixelsPerUnitMultiplier = button100.R / 16f;
                face.color = Color.white;
                face.raycastTarget = true;
                Undo.RecordObject(button, "UI 디자인");
                button.targetGraphic = face;
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState
                {
                    highlightedSprite = buttonHover,
                    pressedSprite = buttonDown,
                    selectedSprite = buttonHover,
                    disabledSprite = buttonInactive,
                };
                SetText(button.transform.Find("Text"), TextMain);
            }
        }

        // ───────────── 도우미 ─────────────

        // 배경 그림을 target 뒤에 깐다. radius = 화면에서의 모서리 반지름. 이전 디자인(Neu)은 지운다.
        private static Image AddSkin(Transform target, Shape shape, float radius, Color tint)
        {
            RemoveChild(target, "Neu");
            RemoveChild(target, "Skin");
            var go = new GameObject("Skin", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "UI 디자인");
            var rt = (RectTransform)go.transform;
            rt.SetParent(target, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = shape.sprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = shape.R / Mathf.Max(1f, radius);
            img.color = tint;
            img.raycastTarget = false;
            return img;
        }

        private static void RemoveChild(Transform t, string name)
        {
            Transform old = t.Find(name);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

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

        private static void SetText(Transform t, Color c, TMP_FontAsset font = null)
        {
            if (t == null || !t.TryGetComponent(out TMP_Text text)) return;
            Undo.RecordObject(text, "UI 디자인");
            if (font != null) text.font = font;
            text.color = c;
            if (text.font != null) text.fontSharedMaterial = text.font.material;
        }

        private static void SetColor(SerializedObject so, string name, Color c)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.colorValue = c;
        }

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
