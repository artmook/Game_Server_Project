using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer.EditorTools
{
    // Tools > 로비 UI 만들기: Canvas 아래에 대기실(접속·메뉴·방 화면) UI를 만들고 LobbyView 참조를 연결한다.
    // 다시 실행하면 기존 Lobby를 지우고 새로 만든다(Ctrl+Z로 되돌릴 수 있음).
    public static class LobbyUIBuilder
    {
        private static readonly Color Accent = new Color(1f, 0.62f, 0.25f);
        private static readonly Color Panel = new Color(0.08f, 0.06f, 0.05f, 0.9f);
        private static readonly Color Field = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color Secondary = new Color(1f, 1f, 1f, 0.18f);

        private const float Width = 460f;

        [MenuItem("Tools/로비 UI 만들기")]
        private static void Build()
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("로비 UI 만들기", "씬에 Canvas가 없습니다.", "확인");
                return;
            }

            Sprite round = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/HUD/Circle.png");
            TMP_FontAsset font = FindKoreanFont(canvas);

            Transform old = canvas.transform.Find("Lobby");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject("Lobby", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "로비 UI 만들기");
            var rootRt = (RectTransform)root.transform;
            rootRt.SetParent(canvas.transform, false);
            Stretch(rootRt);
            rootRt.SetAsLastSibling();
            var view = root.AddComponent<LobbyView>();

            // 대기실 화면 (게임 중에는 LobbyView가 숨긴다)
            var screenGo = new GameObject("Screen", typeof(RectTransform), typeof(CanvasGroup));
            var screen = (RectTransform)screenGo.transform;
            screen.SetParent(rootRt, false);
            Stretch(screen);

            // 뒤 HUD(공격 버튼·생존 수)가 겹쳐 보이지 않게 화면 전체를 어둡게 덮는다.
            Image dim = NewImage("Dim", screen, null, new Color(0f, 0f, 0f, 0.55f));
            Stretch(dim.rectTransform);
            dim.raycastTarget = true;   // 대기실 뒤 HUD 버튼이 눌리지 않게

            // 가운데 패널
            RectTransform panel = NewRect("Panel", screen);
            panel.sizeDelta = new Vector2(Width, 0f);
            Background(panel, round).raycastTarget = true;   // 패널 클릭이 뒤로 새지 않게
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 28, 28);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;   // 화면마다 높이가 달라서

            TMP_Text title = NewText("Title", panel, 34, font, 48f);
            title.text = "대기실";
            title.fontStyle = FontStyles.Bold;
            title.color = Accent;

            // ───────── 접속 화면 ─────────
            RectTransform connect = NewGroup("ConnectPanel", panel);
            TMP_Text status = NewText("Status", connect, 20, font, 80f);
            status.text = "127.0.0.1:7777 접속 중...";
            status.textWrappingMode = TextWrappingModes.Normal;
            Button reconnect = NewButton("ReconnectButton", connect, "다시 접속", round, Accent, font);

            // ───────── 메뉴 화면 ─────────
            RectTransform menu = NewGroup("MenuPanel", panel);
            TMP_Text nickLabel = NewText("NicknameLabel", menu, 18, font, 24f);
            nickLabel.text = "닉네임";
            nickLabel.color = new Color(1f, 1f, 1f, 0.7f);
            TMP_InputField nickInput = NewInput("NicknameInput", menu, round, font, "닉네임 입력 (최대 10자)", TMP_InputField.ContentType.Standard, NetworkGameManager.MaxNicknameLength);
            Button create = NewButton("CreateButton", menu, "방 만들기", round, Accent, font);
            TMP_Text or = NewText("JoinLabel", menu, 18, font, 34f);
            or.text = "초대 코드로 참가";
            or.color = new Color(1f, 1f, 1f, 0.7f);
            TMP_InputField input = NewInput("CodeInput", menu, round, font, "초대 코드 입력", TMP_InputField.ContentType.IntegerNumber, 9);   // 9자리: int 범위 안
            Button join = NewButton("JoinButton", menu, "참가", round, Secondary, font);

            // ───────── 방 화면 ─────────
            RectTransform room = NewGroup("RoomPanel", panel);
            TMP_Text codeLabel = NewText("CodeLabel", room, 16, font, 22f);
            codeLabel.text = "초대 코드";
            codeLabel.color = new Color(1f, 1f, 1f, 0.7f);
            var codeRow = new GameObject("CodeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            codeRow.transform.SetParent(room, false);
            codeRow.GetComponent<LayoutElement>().preferredHeight = 56f;
            var rowLayout = codeRow.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childControlWidth = rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            TMP_Text code = NewText("Code", codeRow.transform, 44, font, 56f);
            code.text = "1";
            code.fontStyle = FontStyles.Bold;
            code.GetComponent<LayoutElement>().flexibleWidth = 1f;
            Button copy = NewButton("CopyButton", codeRow.transform, "복사", round, Secondary, font);
            copy.GetComponent<LayoutElement>().preferredWidth = 90f;

            TMP_Text count = NewText("Count", room, 20, font, 30f);
            count.alignment = TextAlignmentOptions.Left;
            count.text = "참가 인원 <color=#FF9E40>1</color> / 4명";
            TMP_Text membersText = NewText("Members", room, 22, font, 120f);
            membersText.alignment = TextAlignmentOptions.TopLeft;
            membersText.lineSpacing = 12f;
            membersText.text = "<color=#FF9E40>닉네임</color> <size=80%><color=#FFD35A>[방장]</color></size> <size=80%>(나)</size>";
            Button start = NewButton("StartButton", room, "게임 시작", round, Accent, font);
            TMP_Text hint = NewText("StartHint", room, 16, font, 22f);
            hint.text = "2명 이상 모이면 시작할 수 있어요";
            hint.color = new Color(1f, 1f, 1f, 0.7f);
            Button leave = NewButton("LeaveButton", room, "나가기", round, Secondary, font);

            // ───────── 공통 오류 ─────────
            TMP_Text error = NewText("Error", panel, 18, font, 26f);
            error.color = new Color(1f, 0.45f, 0.4f);

            // 편집 화면 미리보기는 방 화면만 보이게 한다(실행하면 LobbyView가 상태에 맞춰 바꾼다).
            connect.gameObject.SetActive(false);
            menu.gameObject.SetActive(false);

            // ───────── 역할 안내 (게임 시작 때 위쪽에 잠깐) ─────────
            // 왼쪽 위 미션 표시·오른쪽 위 생존 수 아래에 둔다(가운데 룰렛보다는 위).
            RectTransform role = NewRect("RoleBanner", rootRt);
            role.anchorMin = role.anchorMax = role.pivot = new Vector2(0.5f, 1f);
            role.anchoredPosition = new Vector2(0f, -140f);
            role.sizeDelta = new Vector2(560f, 0f);
            Background(role, round);
            var roleGroup = role.gameObject.AddComponent<CanvasGroup>();
            roleGroup.blocksRaycasts = roleGroup.interactable = false;
            var roleLayout = role.gameObject.AddComponent<VerticalLayoutGroup>();
            roleLayout.padding = new RectOffset(28, 28, 18, 18);
            roleLayout.spacing = 6f;
            roleLayout.childControlWidth = roleLayout.childControlHeight = true;
            roleLayout.childForceExpandWidth = true;
            roleLayout.childForceExpandHeight = false;
            role.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            TMP_Text roleTitle = NewText("Title", role, 36, font, 48f);
            roleTitle.text = "당신은 <color=#FF5A4E>술래</color>입니다!";
            roleTitle.fontStyle = FontStyles.Bold;
            TMP_Text roleDetail = NewText("Detail", role, 20, font, -1f);   // -1: 줄 수에 맞춰 높이를 정한다.
            roleDetail.textWrappingMode = TextWrappingModes.Normal;
            roleDetail.text = "술래  <color=#FF5A4E>닉네임</color>\n도망자  <color=#FF9E40>닉네임, 닉네임, 닉네임</color>";

            var so = new SerializedObject(view);
            so.FindProperty("network").objectReferenceValue = Object.FindFirstObjectByType<NetworkGameManager>();
            so.FindProperty("group").objectReferenceValue = screenGo.GetComponent<CanvasGroup>();
            so.FindProperty("nicknameInput").objectReferenceValue = nickInput;
            so.FindProperty("countText").objectReferenceValue = count;
            so.FindProperty("roleGroup").objectReferenceValue = roleGroup;
            so.FindProperty("roleTitle").objectReferenceValue = roleTitle;
            so.FindProperty("roleDetail").objectReferenceValue = roleDetail;
            so.FindProperty("connectPanel").objectReferenceValue = connect.gameObject;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("reconnectButton").objectReferenceValue = reconnect;
            so.FindProperty("menuPanel").objectReferenceValue = menu.gameObject;
            so.FindProperty("createButton").objectReferenceValue = create;
            so.FindProperty("codeInput").objectReferenceValue = input;
            so.FindProperty("joinButton").objectReferenceValue = join;
            so.FindProperty("roomPanel").objectReferenceValue = room.gameObject;
            so.FindProperty("codeText").objectReferenceValue = code;
            so.FindProperty("copyButton").objectReferenceValue = copy;
            so.FindProperty("membersText").objectReferenceValue = membersText;
            so.FindProperty("startButton").objectReferenceValue = start;
            so.FindProperty("startHint").objectReferenceValue = hint;
            so.FindProperty("leaveButton").objectReferenceValue = leave;
            so.FindProperty("errorText").objectReferenceValue = error;
            so.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = root;
            Debug.Log(font != null
                ? $"[로비 UI] 생성 완료 (폰트: {font.name})."
                : "[로비 UI] 생성 완료. 한글 폰트를 찾지 못했습니다. 텍스트에 한글 TMP 폰트를 넣으세요.");
        }

        private static RectTransform NewGroup(string n, Transform parent)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return (RectTransform)go.transform;
        }

        private static Button NewButton(string n, Transform parent, string label, Sprite s, Color c, TMP_FontAsset font)
        {
            Image img = NewImage(n, parent, s, c);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 4f;
            img.raycastTarget = true;
            img.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            ColorBlock colors = button.colors;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            button.colors = colors;

            TMP_Text t = NewText("Label", img.transform, 22, font, 0f);
            Object.DestroyImmediate(t.GetComponent<LayoutElement>());
            Stretch(t.rectTransform);
            t.text = label;
            t.fontStyle = FontStyles.Bold;
            return button;
        }

        private static TMP_InputField NewInput(string n, Transform parent, Sprite s, TMP_FontAsset font,
                                               string hint, TMP_InputField.ContentType type, int limit)
        {
            Image img = NewImage(n, parent, s, Field);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 4f;
            img.raycastTarget = true;
            img.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;

            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var areaRt = (RectTransform)area.transform;
            areaRt.SetParent(img.transform, false);
            Stretch(areaRt);
            areaRt.offsetMin = new Vector2(14f, 4f);
            areaRt.offsetMax = new Vector2(-14f, -4f);

            TMP_Text placeholder = NewText("Placeholder", areaRt, 22, font, 0f);
            Object.DestroyImmediate(placeholder.GetComponent<LayoutElement>());
            Stretch(placeholder.rectTransform);
            placeholder.text = hint;
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            TMP_Text text = NewText("Text", areaRt, 24, font, 0f);
            Object.DestroyImmediate(text.GetComponent<LayoutElement>());
            Stretch(text.rectTransform);

            var input = img.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = img;
            input.textViewport = areaRt;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.contentType = type;
            input.characterLimit = limit;
            if (font != null) input.fontAsset = font;
            return input;
        }

        private static RectTransform NewRect(string n, Transform parent)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // 배경 이미지를 레이아웃에서 빼 둔 자식으로 깐다.
        // 같은 오브젝트에 두면 스프라이트 원래 크기가 ContentSizeFitter 높이에 섞여 상자가 글자보다 커진다.
        private static Image Background(RectTransform parent, Sprite s)
        {
            Image img = NewImage("Background", parent, s, Panel);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 4f;
            img.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Stretch(img.rectTransform);
            img.transform.SetAsFirstSibling();
            return img;
        }

        private static Image NewImage(string n, Transform parent, Sprite s, Color c)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.sprite = s; img.color = c; img.raycastTarget = false;
            return img;
        }

        private static TMP_Text NewText(string n, Transform parent, float size, TMP_FontAsset font, float height)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // HUD의 UIView 폰트 → Canvas 안 다른 텍스트 폰트 순서로 한글 폰트를 찾는다. (MissionUIBuilder와 같은 규칙)
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
    }
}
