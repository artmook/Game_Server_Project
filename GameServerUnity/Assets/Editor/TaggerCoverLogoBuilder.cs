using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer.EditorTools
{
    // Tools > 술래 가리개 로고 배경 만들기
    // MissionIntro/TaggerCover 배경을 Assets/Logo.png 로고가 반복해서 흘러가는 무늬로 바꾼다.
    //  - TaggerCover 아래 "LogoPattern"(LogoPatternBackground)을 만든다. 기존 배경(Skin)은 지우지 않고 꺼 둔다.
    //  - 로고 그림 가져오기 설정: 작게 줄여도 깨지지 않게 밉맵을 켜고, 압축을 끈다.
    // 색·크기·속도는 LogoPattern의 Inspector에서 바꾸면 된다. 다시 실행해도 같은 결과. Ctrl+Z로 되돌릴 수 있다.
    public static class TaggerCoverLogoBuilder
    {
        private const string LogoPath = "Assets/Logo.png";
        private const string ShaderName = "GameServer/UI/LogoPattern";

        [MenuItem("Tools/술래 가리개 로고 배경 만들기")]
        private static void Build()
        {
            var view = Object.FindFirstObjectByType<MissionIntroView>(FindObjectsInactive.Include);
            var cover = view != null ? new SerializedObject(view).FindProperty("taggerCover").objectReferenceValue as CanvasGroup : null;
            if (cover == null)
            {
                EditorUtility.DisplayDialog("술래 가리개 로고 배경", "MissionIntro/TaggerCover를 찾지 못했습니다. Game 씬을 열고 실행하세요.", "확인");
                return;
            }
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("술래 가리개 로고 배경", "셰이더 GameServer/UI/LogoPattern을 찾지 못했습니다.", "확인");
                return;
            }

            // 로고 그림 설정: 작게 반복해 그리므로 밉맵을 켠다(지글거림 방지).
            var importer = AssetImporter.GetAtPath(LogoPath) as TextureImporter;
            if (importer != null)
            {
                importer.mipmapEnabled = true;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            var logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);

            Undo.SetCurrentGroupName("술래 가리개 로고 배경");
            int group = Undo.GetCurrentGroup();
            Transform root = cover.transform;

            // 기존 배경은 끄기만 한다.
            if (root.TryGetComponent(out Image coverImage)) { Undo.RecordObject(coverImage, "로고 배경"); coverImage.enabled = false; }
            Transform skin = root.Find("Skin");
            if (skin != null) { Undo.RecordObject(skin.gameObject, "로고 배경"); skin.gameObject.SetActive(false); }

            Transform old = root.Find("LogoPattern");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var go = new GameObject("LogoPattern", typeof(RectTransform), typeof(CanvasRenderer));
            Undo.RegisterCreatedObjectUndo(go, "로고 배경");
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var pattern = go.AddComponent<LogoPatternBackground>();
            var so = new SerializedObject(pattern);
            so.FindProperty("logo").objectReferenceValue = logo;
            so.FindProperty("shader").objectReferenceValue = shader;
            so.FindProperty("m_RaycastTarget").boolValue = false;
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
            Debug.Log("[술래 가리개] 로고 반복 배경(LogoPattern) 생성 완료. 색·크기·속도는 Inspector에서 바꿀 수 있습니다.");
        }
    }
}
