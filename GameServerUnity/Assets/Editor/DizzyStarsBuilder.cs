using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameServer.EditorTools
{
    // Tools > 기절 별 이펙트 만들기
    // 1) 별 그림(Assets/UI/Effects/Star.png)을 만들고 (알파 = 가장자리까지 거리. 빛 번짐 셰이더용)
    // 2) 빛나는 별 재질(Assets/UI/Effects/StarGlow.mat, 셰이더 GameServer/StarGlow)을 만들고
    // 3) 크기가 제각각인 노란 별 5개가 빙글빙글 도는 프리팹(Assets/Resources/DizzyStars.prefab)을 만든다.
    // 프리팹을 더블클릭해서 열면 게임을 실행하지 않아도 별이 도는 모습을 볼 수 있다.
    // 별 크기·개수는 프리팹 안의 Star 오브젝트, 색·빛 세기는 StarGlow.mat에서 고치면 된다(다시 실행하면 새로 만든다).
    public static class DizzyStarsBuilder
    {
        private const string StarPath = "Assets/UI/Effects/Star.png";
        private const string PrefabPath = "Assets/Resources/DizzyStars.prefab";
        private const string MaterialPath = "Assets/UI/Effects/StarGlow.mat";
        private const string ShaderName = "GameServer/StarGlow";
        // 별마다 크기(그림 한 장의 월드 크기). 별 몸통은 그림의 약 60%이고 나머지는 빛 번짐 영역이다.
        private static readonly float[] StarSizes = { 0.30f, 0.19f, 0.25f, 0.16f, 0.22f };

        // 별 모양(그림 안 비율). 바깥 빛이 들어갈 여백을 남기려고 별을 작게 그린다.
        private const float OuterRadius = 0.30f;
        private const float InnerRadius = 0.13f;
        private const float GlowSpread = 0.19f;   // 가장자리 바깥으로 빛이 퍼지는 거리(그림 비율)

        [MenuItem("Tools/기절 별 이펙트 만들기")]
        private static void Build()
        {
            Sprite star = SaveStarSprite();
            Material mat = SaveMaterial();
            if (mat == null) return;

            var root = new GameObject("DizzyStars");
            root.AddComponent<DizzyStars>();
            int count = StarSizes.Length;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"Star{i}", typeof(SpriteRenderer));
                go.transform.SetParent(root.transform, false);
                float a = i * Mathf.PI * 2f / count;
                go.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.45f, 0f, Mathf.Sin(a) * 0.45f);
                go.transform.localScale = Vector3.one * StarSizes[i];
                var sr = go.GetComponent<SpriteRenderer>();
                sr.sprite = star;
                sr.sharedMaterial = mat;
                sr.color = Color.white;   // 색은 재질(StarGlow.mat)에서 정한다
                sr.sortingOrder = 10;
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sr.receiveShadows = false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            Debug.Log($"[기절 별] {PrefabPath} 생성 완료. 더블클릭해서 열면 도는 모습을 볼 수 있습니다.");
        }

        // 빛나는 별 재질. 이미 있으면 그대로 쓴다(직접 바꾼 색 유지).
        private static Material SaveMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) return mat;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[기절 별] 셰이더 {ShaderName}를 찾을 수 없습니다. Assets/Shaders/StarGlow.shader를 확인하세요.");
                return null;
            }
            mat = new Material(shader) { name = "StarGlow" };
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            AssetDatabase.CreateAsset(mat, MaterialPath);
            return mat;
        }

        // 256x256 별 그림. RGB는 흰색, 알파에는 "별 가장자리까지의 거리"를 넣는다.
        // 0.5 = 가장자리, 안쪽으로 갈수록 1, 바깥으로 GlowSpread만큼 가면 0. 셰이더가 이 값으로 림라이트와 빛 번짐을 그린다.
        private static Sprite SaveStarSprite()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];

            Vector2[] poly = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float r = i % 2 == 0 ? OuterRadius : InnerRadius;   // 바깥 꼭짓점 / 안쪽 꼭짓점
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                poly[i] = new Vector2(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r);
            }

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    float dist = EdgeDistance(p, poly);
                    if (!InPolygon(p, poly)) dist = -dist;
                    float v = Mathf.Clamp01(0.5f + dist * (0.5f / GlowSpread));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(v * 255f));
                }

            tex.SetPixels32(px);
            Directory.CreateDirectory(Path.GetDirectoryName(StarPath));
            File.WriteAllBytes(StarPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(StarPath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(StarPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size;   // 별 하나 = 1 유닛
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;   // 알파는 거리 값이라 가장자리 색 보정을 하지 않는다
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;   // 빛 번짐 영역이 잘리지 않게 사각형 그대로 쓴다
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(StarPath);
        }

        private static float EdgeDistance(Vector2 p, Vector2[] poly)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        private static bool InPolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
