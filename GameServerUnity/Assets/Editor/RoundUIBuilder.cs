using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer.EditorTools
{
    // Tools > 라운드 타이머·결과 화면 만들기
    //   HUD/RoundTimer      : 화면 위쪽 가운데 남은 시간(04:00)
    //   Canvas/Round        : RoundManager(시간·승패 판정) + RoundView(보여 주기)
    //   Canvas/Round/Result : 승패 결과 화면(술래 승리!/도망자 승리! + 이름 + 나가기 버튼)
    // Tools > 술래 대기 화면 가리개 만들기
    //   MissionIntro/TaggerCover : 술래 안내 동안 화면 전체를 색으로 덮는다. 안내 글자는 화면 가운데로 옮긴다.
    // 편집 화면에서 바로 보인다. 다시 실행하면 그 부분만 새로 만든다(Ctrl+Z로 되돌릴 수 있음).
    public static class RoundUIBuilder
    {
        private static readonly Color Panel = new Color(0.08f, 0.09f, 0.12f, 0.94f);
        private static readonly Color TimerBg = new Color(0.08f, 0.09f, 0.12f, 0.78f);
        private static readonly Color Gray = new Color(0.72f, 0.75f, 0.8f);
        private static readonly Color Accent = new Color(1f, 0.62f, 0.25f);
        private static readonly Color RunnerGreen = new Color(0.36f, 0.86f, 0.42f);
        private static readonly Color CoverColor = new Color(0.06f, 0.07f, 0.1f, 1f);

        // ───────────── 타이머 · 결과 화면 ─────────────

        [MenuItem("Tools/라운드 타이머·결과 화면 만들기")]
        private static void BuildRound()
        {
            UIView hudView = FindHud(out RectTransform skill);
            if (hudView == null || skill == null)
            {
                EditorUtility.DisplayDialog("라운드 UI 만들기", "공격 버튼(SkillButton)이 연결된 HUD를 찾지 못했습니다. Tools > HUD 만들기를 먼저 실행하세요.", "확인");
                return;
            }
            Transform hud = skill.parent;
            Canvas canvas = skill.GetComponentInParent<Canvas>().rootCanvas;
            TMP_FontAsset font = FindKoreanFont(canvas.transform);
            Sprite circle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/HUD/Circle.png");
            Sprite squareRing = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/HUD/SquareRing.png");

            DestroyChild(hud, "RoundTimer");
            DestroyChild(canvas.transform, "Round");

            // ── 타이머 (HUD 안: 시작 연출·가리개보다 아래에 그려진다) ──
            var timerGo = new GameObject("RoundTimer", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(timerGo, "라운드 UI 만들기");
            var timer = (RectTransform)timerGo.transform;
            timer.SetParent(hud, false);
            timer.anchorMin = timer.anchorMax = timer.pivot = new Vector2(0.5f, 1f);
            timer.anchoredPosition = new Vector2(0f, -20f);
            timer.sizeDelta = new Vector2(190f, 78f);

            Image timerBg = NewImage("Background", timer, circle, TimerBg, 4f);
            Stretch(timerBg.rectTransform);

            TMP_Text timerLabel = NewText("Label", timer, 16, font);
            timerLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            timerLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            timerLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            timerLabel.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            timerLabel.rectTransform.sizeDelta = new Vector2(0f, 22f);
            timerLabel.color = Gray;
            timerLabel.text = "남은 시간";

            TMP_Text timerText = NewText("Time", timer, 42, font);
            Stretch(timerText.rectTransform);
            timerText.rectTransform.offsetMax = new Vector2(0f, -22f);
            timerText.fontStyle = FontStyles.Bold;
            timerText.characterSpacing = 4f;
            timerText.text = RoundView.TimeText(240f);

            // ── 판정 + 결과 화면 (Canvas 맨 위) ──
            var roundGo = new GameObject("Round", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(roundGo, "라운드 UI 만들기");
            var round = (RectTransform)roundGo.transform;
            round.SetParent(canvas.transform, false);
            Stretch(round);
            round.SetAsLastSibling();
            var view = roundGo.AddComponent<RoundView>();
            var manager = roundGo.AddComponent<RoundManager>();

            var resultGo = new GameObject("Result", typeof(RectTransform), typeof(CanvasGroup));
            var result = (RectTransform)resultGo.transform;
            result.SetParent(round, false);
            Stretch(result);
            var resultGroup = resultGo.GetComponent<CanvasGroup>();

            Image dim = NewImage("Dim", result, null, new Color(0f, 0f, 0f, 0.62f), 1f);
            dim.raycastTarget = true;   // 결과 화면 뒤를 누르지 못하게
            Stretch(dim.rectTransform);

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            var panel = (RectTransform)panelGo.transform;
            panel.SetParent(result, false);
            panel.sizeDelta = new Vector2(640f, 460f);
            Image panelBg = NewImage("Background", panel, circle, Panel, 3f);
            Stretch(panelBg.rectTransform);

            TMP_Text title = NewText("Title", panel, 76, font);
            TopAnchored(title.rectTransform, -34f, 100f);
            title.fontStyle = FontStyles.Bold;
            title.color = RunnerGreen;
            title.text = RoundView.TitleText(false);

            TMP_Text reason = NewText("Reason", panel, 24, font);
            TopAnchored(reason.rectTransform, -136f, 34f);
            reason.color = Gray;
            reason.text = "제한 시간 동안 살아남았습니다!";

            Image divider = NewImage("Divider", panel, null, new Color(1f, 1f, 1f, 0.14f), 1f);
            divider.rectTransform.anchorMin = divider.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            divider.rectTransform.pivot = new Vector2(0.5f, 1f);
            divider.rectTransform.anchoredPosition = new Vector2(0f, -188f);
            divider.rectTransform.sizeDelta = new Vector2(480f, 2f);

            TMP_Text namesLabel = NewText("NamesLabel", panel, 20, font);
            TopAnchored(namesLabel.rectTransform, -202f, 28f);
            namesLabel.color = RunnerGreen;
            namesLabel.text = RoundView.NamesLabelText(false);

            TMP_Text names = NewText("Names", panel, 30, font);
            TopAnchored(names.rectTransform, -232f, 112f);
            names.lineSpacing = -4f;
            names.text = "Player <size=70%>(나)</size>\nPlayer 3\n<color=#8A8F99><s>Player 4</s> <size=70%>탈락</size></color>";

            // 나가기 버튼
            var buttonGo = new GameObject("ExitButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var buttonRt = (RectTransform)buttonGo.transform;
            buttonRt.SetParent(panel, false);
            buttonRt.anchorMin = buttonRt.anchorMax = buttonRt.pivot = new Vector2(0.5f, 0f);
            buttonRt.anchoredPosition = new Vector2(0f, 34f);
            buttonRt.sizeDelta = new Vector2(220f, 62f);
            var buttonImg = buttonGo.GetComponent<Image>();
            buttonImg.sprite = circle;
            buttonImg.color = Accent;
            if (circle != null) { buttonImg.type = Image.Type.Sliced; buttonImg.pixelsPerUnitMultiplier = 3f; }
            var button = buttonGo.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            TMP_Text buttonText = NewText("Text", buttonRt, 28, font);
            Stretch(buttonText.rectTransform);
            buttonText.fontStyle = FontStyles.Bold;
            buttonText.color = new Color(0.12f, 0.08f, 0.04f);
            buttonText.text = "나가기";

            // ── 참조 연결 ──
            var so = new SerializedObject(view);
            so.FindProperty("timerRoot").objectReferenceValue = timer;
            so.FindProperty("timerText").objectReferenceValue = timerText;
            so.FindProperty("resultGroup").objectReferenceValue = resultGroup;
            so.FindProperty("resultPanel").objectReferenceValue = panel;
            so.FindProperty("resultTitle").objectReferenceValue = title;
            so.FindProperty("resultReason").objectReferenceValue = reason;
            so.FindProperty("resultNamesLabel").objectReferenceValue = namesLabel;
            so.FindProperty("resultNames").objectReferenceValue = names;
            so.FindProperty("exitButton").objectReferenceValue = button;
            so.ApplyModifiedProperties();

            var mso = new SerializedObject(manager);
            mso.FindProperty("view").objectReferenceValue = view;
            mso.FindProperty("intro").objectReferenceValue = Object.FindFirstObjectByType<MissionIntro>(FindObjectsInactive.Include);
            mso.FindProperty("survivors").objectReferenceValue = Object.FindFirstObjectByType<SurvivorCounter>(FindObjectsInactive.Include);
            var input = Object.FindFirstObjectByType<PlayerInputSource>(FindObjectsInactive.Include);
            if (input != null) mso.FindProperty("localPlayer").objectReferenceValue = input.GetComponent<Character>();
            mso.ApplyModifiedProperties();

            Selection.activeGameObject = roundGo;
            Refresh(timerGo, roundGo);
            Debug.Log("[라운드 UI] 타이머(HUD/RoundTimer)와 결과 화면(Canvas/Round/Result) 생성 완료. 결과 화면 미리보기는 Result를 꺼 둬도 실행 중엔 알아서 켜집니다.");
        }

        // ───────────── 술래 대기 가리개 ─────────────

        [MenuItem("Tools/술래 대기 화면 가리개 만들기")]
        private static void BuildCover()
        {
            var view = Object.FindFirstObjectByType<MissionIntroView>(FindObjectsInactive.Include);
            if (view == null)
            {
                EditorUtility.DisplayDialog("술래 가리개 만들기", "MissionIntro가 없습니다. Tools > 미션 UI 만들기를 먼저 실행하세요.", "확인");
                return;
            }
            var so = new SerializedObject(view);
            var notice = so.FindProperty("taggerNoticeGroup").objectReferenceValue as CanvasGroup;
            if (notice == null)
            {
                EditorUtility.DisplayDialog("술래 가리개 만들기", "술래 안내(TaggerNotice)가 연결되어 있지 않습니다.", "확인");
                return;
            }
            Transform root = notice.transform.parent;
            DestroyChild(root, "TaggerCover");

            var go = new GameObject("TaggerCover", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "술래 가리개 만들기");
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            Stretch(rt);
            rt.SetSiblingIndex(notice.transform.GetSiblingIndex());   // 안내 글자 바로 뒤(아래)에 그린다
            var img = go.GetComponent<Image>();
            img.color = CoverColor;
            img.raycastTarget = false;
            var group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // 안내 글자를 화면 가운데로
            var noticeRt = (RectTransform)notice.transform;
            Undo.RecordObject(noticeRt, "술래 가리개 만들기");
            noticeRt.anchorMin = noticeRt.anchorMax = noticeRt.pivot = new Vector2(0.5f, 0.5f);
            noticeRt.anchoredPosition = Vector2.zero;

            so.FindProperty("taggerCover").objectReferenceValue = group;
            so.ApplyModifiedProperties();
            Selection.activeGameObject = go;
            Refresh(notice.gameObject);
            Debug.Log("[술래 가리개] TaggerCover 생성 완료. 색은 TaggerCover의 Image 색으로 바꿀 수 있습니다.");
        }

        // ───────────── 도우미 ─────────────

        private static UIView FindHud(out RectTransform skill)
        {
            foreach (UIView v in Object.FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                skill = new SerializedObject(v).FindProperty("skillRoot").objectReferenceValue as RectTransform;
                if (skill != null) return v;
            }
            skill = null;
            return null;
        }

        private static void DestroyChild(Transform parent, string name)
        {
            Transform old = parent.Find(name);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        private static Image NewImage(string n, Transform parent, Sprite s, Color c, float ppuMultiplier)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = s;
            img.color = c;
            img.raycastTarget = false;
            if (s != null) { img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = ppuMultiplier; }
            return img;
        }

        private static TMP_Text NewText(string n, Transform parent, float size, TMP_FontAsset font)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) { t.font = font; t.fontSharedMaterial = font.material; }
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        private static void TopAnchored(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(-40f, height);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static TMP_FontAsset FindKoreanFont(Transform root)
        {
            foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
                if (t.font != null && t.font.HasCharacter('공')) return t.font;
            return null;
        }

        // 편집 화면에서 새 글자가 바로 그려지지 않을 수 있어 한 프레임 뒤 껐다 켜서 다시 그린다.
        private static void Refresh(params GameObject[] roots)
        {
            EditorApplication.delayCall += () =>
            {
                foreach (GameObject go in roots)
                {
                    if (go == null) continue;
                    bool was = go.activeSelf;
                    go.SetActive(!was);
                    go.SetActive(was);
                    foreach (TMP_Text t in go.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate(true, true);
                }
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            };
        }
    }
}
