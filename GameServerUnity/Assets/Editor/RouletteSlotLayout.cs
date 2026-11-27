using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer.EditorTools
{
    // Tools > 미션 룰렛 가로 배치 (슬롯머신)
    // 미션 추첨 룰렛을 슬롯머신처럼 바꾼다: 세로로 도는 칸 3개를 가로로 나란히 두고, 왼쪽부터 차례로 멈춘다.
    //  - 룰렛 영역을 넓힌다(1180 x 410). 칸마다 위아래 미션이 흐릿하게 보이며 돌아간다.
    //  - 가운데 줄에 은은한 하늘색 띠(Payline)를 깔아 "여기에 걸린다"를 보여 준다.
    //  - 멈출 때 반짝이던 테두리(Highlight)는 지운다.
    // 배경 그림·색은 그대로 둔다(UI 디자인 적용 메뉴가 입힌 Skin 유지). 다시 실행해도 같은 결과. Ctrl+Z로 되돌릴 수 있다.
    public static class RouletteSlotLayout
    {
        public const float ColumnWidth = 340f;   // 칸 하나 너비
        public const float ColumnGap = 30f;
        public const float SlotHeight = 84f;     // 미션 한 칸 높이
        public const float WindowHeight = 236f;  // 보이는 창 높이: 가운데 칸 + 위아래 칸 일부
        public const float Top = 82f;            // 제목 영역

        [MenuItem("Tools/미션 룰렛 가로 배치 (슬롯머신)")]
        private static void Menu()
        {
            var view = Object.FindFirstObjectByType<MissionIntroView>(FindObjectsInactive.Include);
            if (view == null)
            {
                EditorUtility.DisplayDialog("미션 룰렛 가로 배치", "MissionIntro가 없습니다. Game 씬을 열고 실행하세요.", "확인");
                return;
            }
            Undo.SetCurrentGroupName("미션 룰렛 가로 배치");
            int group = Undo.GetCurrentGroup();
            Arrange(view);
            Undo.CollapseUndoOperations(group);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            Debug.Log("[미션 룰렛] 가로 3칸 슬롯머신 배치 완료.");
        }

        public static void Arrange(MissionIntroView view)
        {
            var so = new SerializedObject(view);
            var group = so.FindProperty("rouletteGroup").objectReferenceValue as CanvasGroup;
            if (group == null) return;
            var roulette = (RectTransform)group.transform;
            Color accent = so.FindProperty("accent").colorValue;

            // 반짝이던 테두리 삭제
            var hl = so.FindProperty("highlight").objectReferenceValue as Image;
            if (hl != null) Undo.DestroyObjectImmediate(hl.gameObject);
            Transform hlByName = roulette.Find("Highlight");
            if (hlByName != null) Undo.DestroyObjectImmediate(hlByName.gameObject);
            so.FindProperty("highlight").objectReferenceValue = null;

            SerializedProperty reels = so.FindProperty("reels");
            int count = reels.arraySize;
            float width = count * ColumnWidth + (count - 1) * ColumnGap + 80f;
            float height = Top + WindowHeight + 52f;

            Undo.RecordObject(roulette, "미션 룰렛");
            roulette.sizeDelta = new Vector2(width, height);

            Transform title = roulette.Find("Title");
            if (title != null)
            {
                var trt = (RectTransform)title;
                Undo.RecordObject(trt, "미션 룰렛");
                trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 1f);
                trt.anchoredPosition = new Vector2(0f, -30f);
                trt.sizeDelta = new Vector2(width - 80f, 40f);
            }

            float y = (height * 0.5f - Top) - (WindowHeight + 20f) * 0.5f;   // 제목 아래 영역의 가운데
            Sprite band = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/TWD/FreeButtonSet/Textures/other/universal_panel_20.png");

            for (int r = 0; r < count; r++)
            {
                float x = (r - (count - 1) * 0.5f) * (ColumnWidth + ColumnGap);
                SerializedProperty reel = reels.GetArrayElementAtIndex(r);
                var viewport = reel.FindPropertyRelative("viewport").objectReferenceValue as RectTransform;

                // 칸 배경
                var bg = roulette.Find($"ReelBackground{r}") as RectTransform;
                if (bg != null)
                {
                    Undo.RecordObject(bg, "미션 룰렛");
                    bg.anchorMin = bg.anchorMax = bg.pivot = new Vector2(0.5f, 0.5f);
                    bg.anchoredPosition = new Vector2(x, y);
                    bg.sizeDelta = new Vector2(ColumnWidth, WindowHeight + 20f);

                    // 가운데 줄 띠(Payline)
                    Transform old = bg.Find("Payline");
                    if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
                    var lineGo = new GameObject("Payline", typeof(RectTransform), typeof(Image));
                    Undo.RegisterCreatedObjectUndo(lineGo, "미션 룰렛");
                    var lrt = (RectTransform)lineGo.transform;
                    lrt.SetParent(bg, false);
                    lrt.SetAsLastSibling();
                    lrt.sizeDelta = new Vector2(ColumnWidth - 16f, SlotHeight);
                    var line = lineGo.GetComponent<Image>();
                    line.sprite = band;
                    if (band != null) { line.type = Image.Type.Sliced; line.pixelsPerUnitMultiplier = 20f / 12f; }
                    line.color = new Color(accent.r, accent.g, accent.b, 0.16f);
                    line.raycastTarget = false;
                }

                if (viewport == null) continue;
                Undo.RecordObject(viewport, "미션 룰렛");
                viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(0.5f, 0.5f);
                viewport.anchoredPosition = new Vector2(x, y);
                viewport.sizeDelta = new Vector2(ColumnWidth - 12f, WindowHeight);
                if (viewport.TryGetComponent(out RectMask2D mask))
                {
                    Undo.RecordObject(mask, "미션 룰렛");
                    mask.softness = new Vector2Int(0, 44);   // 위아래로 갈수록 흐려지며 잘린다
                }
                foreach (string n in new[] { "LineTop", "LineBottom" })
                {
                    var l = viewport.Find(n) as RectTransform;
                    if (l == null) continue;
                    Undo.RecordObject(l, "미션 룰렛");
                    l.sizeDelta = new Vector2(ColumnWidth - 40f, 2f);
                    l.anchoredPosition = new Vector2(0f, (n == "LineTop" ? 0.5f : -0.5f) * SlotHeight);
                }

                // 미션 칸: 좁아진 너비에 맞춰 한 줄로 쓰고, 길면 글자를 줄인다.
                SerializedProperty slots = reel.FindPropertyRelative("slots");
                for (int i = 0; i < slots.arraySize; i++)
                {
                    var slot = slots.GetArrayElementAtIndex(i).objectReferenceValue as TMP_Text;
                    if (slot == null) continue;
                    Undo.RecordObject(slot, "미션 룰렛");
                    Undo.RecordObject(slot.rectTransform, "미션 룰렛");
                    int offset = slots.arraySize / 2 - i;
                    slot.rectTransform.anchorMin = slot.rectTransform.anchorMax = slot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    slot.rectTransform.sizeDelta = new Vector2(ColumnWidth - 40f, SlotHeight);
                    slot.rectTransform.anchoredPosition = new Vector2(0f, offset * SlotHeight);
                    slot.rectTransform.localScale = Vector3.one * (offset == 0 ? 1f : 0.8f);
                    slot.textWrappingMode = TextWrappingModes.NoWrap;   // 한 줄로, 길면 글자를 줄여 맞춘다
                    slot.enableAutoSizing = true;
                    slot.fontSizeMin = 18f;
                    slot.fontSizeMax = 30f;
                    slot.alignment = TextAlignmentOptions.Center;
                    // 미리보기: 가운데는 또렷하게, 위아래는 흐리게
                    Color c = offset == 0 ? accent : slot.color;
                    c.a = offset == 0 ? 1f : Mathf.Abs(offset) == 1 ? 0.35f : 0f;
                    slot.color = c;
                }
            }

            so.FindProperty("slotHeight").floatValue = SlotHeight;
            so.FindProperty("neighborFade").floatValue = 0.65f;   // 한 칸 위아래는 35%로 보인다
            so.ApplyModifiedProperties();
        }
    }
}
