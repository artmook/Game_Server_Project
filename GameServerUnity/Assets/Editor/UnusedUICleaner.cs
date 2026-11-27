using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameServer.EditorTools
{
    // Tools > 안 쓰는 UI 정리 (Frame·RingBack·RingFill)
    // 씬의 UI(Canvas 아래)에서 테두리(Frame)와 공격 버튼 링(RingBack, RingFill)을 지운다.
    // 코드 쪽 연결(UIView.ringFill, RoundView.resultFrame)도 비운다. 둘 다 없어도 동작한다.
    // Ctrl+Z로 한 번에 되돌릴 수 있다.
    public static class UnusedUICleaner
    {
        private static readonly HashSet<string> Names = new HashSet<string> { "Frame", "RingBack", "RingFill" };

        [MenuItem("Tools/안 쓰는 UI 정리 (Frame·RingBack·RingFill)")]
        private static void Clean()
        {
            Undo.SetCurrentGroupName("안 쓰는 UI 정리");
            int group = Undo.GetCurrentGroup();

            foreach (UIView view in Object.FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(view);
                so.FindProperty("ringFill").objectReferenceValue = null;
                so.ApplyModifiedProperties();
            }
            foreach (RoundView view in Object.FindObjectsByType<RoundView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(view);
                so.FindProperty("resultFrame").objectReferenceValue = null;
                so.ApplyModifiedProperties();
            }

            var doomed = new List<GameObject>();
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (RectTransform rt in canvas.GetComponentsInChildren<RectTransform>(true))
                    if (Names.Contains(rt.name) && !doomed.Contains(rt.gameObject)) doomed.Add(rt.gameObject);

            var names = new List<string>();
            foreach (GameObject go in doomed)
            {
                if (go == null) continue;
                names.Add(go.transform.parent != null ? $"{go.transform.parent.name}/{go.name}" : go.name);
                Undo.DestroyObjectImmediate(go);
            }

            Undo.CollapseUndoOperations(group);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log($"[UI 정리] {names.Count}개 삭제: {string.Join(", ", names)}");
        }
    }
}
