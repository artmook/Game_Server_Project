using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer.EditorTools
{
    // Tools > 처치 알림 미리보기 만들기
    // HUD의 KillFeed(생존자 수 아래) 밑에 "AI를 처리했습니다." 처치 알림 한 줄을 만들어 둔다.
    // 편집 화면에서 모양을 바로 볼 수 있고, 게임을 실행하면 숨겨진 채 복제 원본으로 쓰인다.
    // KillFeed가 없으면 HUD 만들기와 같은 위치·모양으로 다시 만든다.
    // 다시 실행하면 기존 미리보기를 지우고 새로 만든다(Ctrl+Z로 되돌릴 수 있음).
    public static class KillFeedPreviewBuilder
    {
        [MenuItem("Tools/처치 알림 미리보기 만들기")]
        private static void Build()
        {
            int made = 0;
            foreach (UIView view in Object.FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(view);
                // 생존자 수 표시가 연결된 UIView만 진짜 HUD로 본다(빈 UIView는 건너뜀).
                if (so.FindProperty("remainingText").objectReferenceValue == null) continue;

                Undo.RecordObject(view, "처치 알림 미리보기 만들기");
                EnsureKillFeed(view, so);

                var root = so.FindProperty("killFeedRoot").objectReferenceValue as RectTransform;
                Transform old = root.Find("KillFeedItemTemplate");
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

                RectTransform preview = view.CreateKillFeedPreview();
                if (preview == null) continue;
                Undo.RegisterCreatedObjectUndo(preview.gameObject, "처치 알림 미리보기 만들기");
                preview.SetAsFirstSibling();
                EditorUtility.SetDirty(view);
                Selection.activeGameObject = preview.gameObject;
                made++;

                // 편집 화면에서는 새 글자 모양이 바로 그려지지 않을 수 있어 한 프레임 뒤 다시 그린다.
                EditorApplication.delayCall += () =>
                {
                    if (preview == null) return;
                    foreach (var t in preview.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate(true, true);
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                };
            }

            if (made == 0)
                EditorUtility.DisplayDialog("처치 알림 미리보기 만들기",
                    "생존자 수가 연결된 HUD(UIView)를 찾지 못했습니다. Tools > HUD 만들기를 먼저 실행하세요.", "확인");
            else
                Debug.Log("[처치 알림] 미리보기 생성 완료. 편집 화면에서 보이고, 실행하면 숨긴 뒤 복제해서 씁니다.");
        }

        // KillFeed(알림이 쌓이는 자리)와 글자 템플릿이 없으면 HUD 만들기와 같은 모양으로 만든다.
        private static void EnsureKillFeed(UIView view, SerializedObject so)
        {
            SerializedProperty rootProp = so.FindProperty("killFeedRoot");
            SerializedProperty templateProp = so.FindProperty("feedTemplate");
            var root = rootProp.objectReferenceValue as RectTransform;

            if (root == null)
            {
                var feedGo = new GameObject("KillFeed", typeof(RectTransform), typeof(VerticalLayoutGroup));
                Undo.RegisterCreatedObjectUndo(feedGo, "처치 알림 미리보기 만들기");
                root = (RectTransform)feedGo.transform;
                root.SetParent(view.transform, false);
                root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 1f);
                root.anchoredPosition = new Vector2(-30f, -110f);   // 생존자 수 패널 바로 아래
                root.sizeDelta = new Vector2(400f, 220f);
                var layout = feedGo.GetComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.UpperRight;
                layout.spacing = 6f;
                layout.childControlWidth = layout.childControlHeight = false;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                rootProp.objectReferenceValue = root;
                Debug.Log("[처치 알림] HUD에 KillFeed가 없어 다시 만들었습니다.");
            }

            if (templateProp.objectReferenceValue == null)
            {
                var go = new GameObject("FeedTemplate", typeof(RectTransform), typeof(TextMeshProUGUI));
                Undo.RegisterCreatedObjectUndo(go, "처치 알림 미리보기 만들기");
                go.transform.SetParent(root, false);
                var t = go.GetComponent<TextMeshProUGUI>();
                TMP_FontAsset font = FindKoreanFont(view);
                if (font != null) t.font = font;
                t.fontSize = 26;
                t.alignment = TextAlignmentOptions.Right;
                t.color = Color.white;
                t.raycastTarget = false;
                t.richText = true;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.outlineWidth = 0.15f;
                t.outlineColor = new Color32(0, 0, 0, 160);
                t.rectTransform.sizeDelta = new Vector2(400f, 36f);
                t.text = "<color=#FF6B6B>AI</color>를 처리했습니다.";
                go.SetActive(false);   // 글자 스타일 원본. 꺼진 오브젝트는 레이아웃에서 빠진다.
                templateProp.objectReferenceValue = t;
            }

            so.ApplyModifiedProperties();
        }

        private static TMP_FontAsset FindKoreanFont(UIView view)
        {
            foreach (TMP_Text t in view.GetComponentsInChildren<TMP_Text>(true))
                if (t.font != null && t.font.HasCharacter('공')) return t.font;
            foreach (TMP_Text t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.font != null && t.font.HasCharacter('공')) return t.font;
            return null;
        }
    }
}
