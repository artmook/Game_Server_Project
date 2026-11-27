using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameServer.EditorTools
{
    // Tools > 달리기 먼지 이펙트 만들기
    // 1) 먼지 재질(Assets/UI/Effects/DustPuff.mat, 셰이더 GameServer/DustPuff)을 만들고
    // 2) 발밑 먼지 파티클 프리팹(Assets/Resources/RunDust.prefab)을 만든다.
    // 프리팹을 열고 RunDust를 선택하면 Scene 화면에서 먼지가 나오는 모습을 볼 수 있다(미리보기에서는 계속 나온다).
    // 먼지 양·크기·색은 프리팹의 Particle System에서, 먼지 모양(부드러움·울퉁불퉁함)은 DustPuff.mat에서 고친다.
    public static class RunDustBuilder
    {
        private const string PrefabPath = "Assets/Resources/RunDust.prefab";
        private const string MaterialPath = "Assets/UI/Effects/DustPuff.mat";
        private const string ShaderName = "GameServer/DustPuff";

        [MenuItem("Tools/달리기 먼지 이펙트 만들기")]
        private static void Build()
        {
            Material mat = SaveMaterial();
            if (mat == null) return;

            var root = new GameObject("RunDust", typeof(ParticleSystem));
            root.AddComponent<RunDust>();
            var ps = root.GetComponent<ParticleSystem>();
            root.transform.rotation = Quaternion.identity;

            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.93f, 0.87f, 0.76f, 0.75f), new Color(0.80f, 0.72f, 0.60f, 0.6f));
            main.gravityModifier = -0.04f;   // 아주 살짝 떠오른다
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // 캐릭터가 지나간 자리에 남는다
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 80;

            var emission = ps.emission;
            emission.enabled = true;          // 미리보기용. 게임에서는 RunDust가 달릴 때만 켠다.
            emission.rateOverTime = 14f;
            emission.rateOverDistance = 3f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.18f;
            shape.rotation = new Vector3(-90f, 0f, 0f);   // 반구가 위를 향하게

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            // x·y·z는 같은 방식(두 값 사이 무작위)이어야 한다.
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            vel.y = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.6f;
            limit.dampen = 0.15f;             // 퍼지다가 금방 느려진다

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.45f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1.35f)));

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f),
                        new GradientAlphaKey(0.6f, 0.55f), new GradientAlphaKey(0f, 1f) });
            color.color = g;

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-1f, 1f);   // 라디안/초, 천천히 돈다

            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = 10f;   // 별 이펙트 등 다른 반투명보다 뒤에

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            Debug.Log($"[달리기 먼지] {PrefabPath} 생성 완료. 플레이어가 달릴 때 발밑에 먼지가 납니다.");
        }

        private static Material SaveMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) return mat;
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[달리기 먼지] 셰이더 {ShaderName}를 찾을 수 없습니다. Assets/Shaders/DustPuff.shader를 확인하세요.");
                return null;
            }
            mat = new Material(shader) { name = "DustPuff" };
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            AssetDatabase.CreateAsset(mat, MaterialPath);
            return mat;
        }
    }
}
