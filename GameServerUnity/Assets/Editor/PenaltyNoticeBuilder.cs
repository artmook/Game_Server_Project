using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer.EditorTools
{
    // Tools > 공격 패널티 표시 만들기
    // HUD의 공격 버튼(SkillButton) 바로 위에 "AI 처치 패널티 / 쿨타임 +2초" 표시를 만든다.
    // 편집 화면에서 모양을 바로 볼 수 있고, 게임을 실행하면 숨겼다가 술래가 AI를 죽였을 때만 나온다.
    // 공격 버튼의 자식이라 버튼을 옮기면 같이 움직인다. 다시 실행하면 새로 만든다(Ctrl+Z로 되돌릴 수 있음).
    public static class PenaltyNoticeBuilder
    {
        private static readonly Color Panel = new Color(0.12f, 0.04f, 0.04f, 0.82f);
        private static readonly Color Red = new Color(1f, 0.36f, 0.36f);

        [MenuItem("Tools/공격 패널티 표시 만들기")]
        private static void Build()
        {
            int made = 0;
            foreach (UIView view in Object.FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(view);
                var skill = so.FindProperty("skillRoot").objectReferenceValue as RectTransform;
                if (skill == null) continue;   // 공격 버튼이 연결된 진짜 HUD만

                Transform old = skill.Find("PenaltyNotice");
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

                var go = new GameObject("PenaltyNotice", typeof(RectTransform), typeof(CanvasGroup));
                Undo.RegisterCreatedObjectUndo(go, "공격 패널티 표시 만들기");
                var rt = (RectTransform)go.transform;
                rt.SetParent(skill, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);   // 버튼 위쪽 가운데
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 44f);   // 버튼 둘레 빛(Glow)과 겹치지 않게
                rt.sizeDelta = new Vector2(190f, 64f);
                var group = go.GetComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;

                Sprite circle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/HUD/Circle.png");
                Image bg = NewImage("Background", rt, circle, Panel, 6f);
                Stretch(bg.rectTransform);

                var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                textGo.transform.SetParent(rt, false);
                var text = textGo.GetComponent<TextMeshProUGUI>();
                TMP_FontAsset font = FindKoreanFont(view);
                if (font != null) text.font = font;
                text.fontSize = 24;
                text.alignment = TextAlignmentOptions.Center;
                text.color = Color.white;
                text.raycastTarget = false;
                text.richText = true;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.lineSpacing = -8f;
                text.text = UIView.PenaltyText(2f, Red);
                Stretch(text.rectTransform);

                so.FindProperty("penaltyGroup").objectReferenceValue = group;
                so.FindProperty("penaltyText").objectReferenceValue = text;
                so.ApplyModifiedProperties();
                Selection.activeGameObject = go;
                made++;

                // 편집 화면에서 새 글자가 바로 그려지지 않을 수 있어 한 프레임 뒤 껐다 켜서 다시 그린다.
                EditorApplication.delayCall += () =>
                {
                    if (go == null) return;
                    go.SetActive(false);
                    go.SetActive(true);
                    text.ForceMeshUpdate(true, true);
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                };
            }

            if (made == 0)
                EditorUtility.DisplayDialog("공격 패널티 표시 만들기",
                    "공격 버튼(SkillButton)이 연결된 HUD를 찾지 못했습니다. Tools > HUD 만들기를 먼저 실행하세요.", "확인");
            else
                Debug.Log("[공격 패널티] 표시 생성 완료. 편집 화면에서 보이고, 실행하면 패널티 중에만 나옵니다.");
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

        private static TMP_FontAsset FindKoreanFont(UIView view)
        {
            foreach (TMP_Text t in view.GetComponentsInChildren<TMP_Text>(true))
                if (t.font != null && t.font.HasCharacter('공')) return t.font;
            return null;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
