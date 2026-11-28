/*
[ 코드 설명 ]

DizzyStars (기절 별)

1. 캐릭터가 쓰러지면(도망자 플레이어가 잡히거나, AI가 맞아 기절하면) 머리 위에서 노란 별들이 빙글빙글 돈다.
2. 죽은 캐릭터의 자식으로 붙기 때문에, 캐릭터가 사라지면(꺼지거나 지워지면) 같이 사라진다.
   같은 캐릭터가 다시 살아나면(리스폰, AI 기절 회복) 스스로 지운다.
3. 별 모양과 개수는 프리팹(Assets/Resources/DizzyStars.prefab)에 있다. Tools > 기절 별 이펙트 만들기로 만든다.
   프리팹을 열면 편집 화면에서도 별이 도는 모습을 볼 수 있다.
4. 아무 데도 붙일 필요 없다. 게임이 시작되면 DizzyStarsSpawner가 캐릭터 죽음을 듣고 알아서 붙인다.
*/

using UnityEngine;

namespace GameServer
{
    [ExecuteAlways]
    public class DizzyStars : MonoBehaviour
    {
        [SerializeField] private float radius = 0.45f;     // 머리 둘레 반지름
        [SerializeField] private float tilt = 0.18f;       // 원을 살짝 기울여 앞뒤 높이 차이를 준다
        [SerializeField] private float orbitSpeed = 200f;  // 도는 속도(도/초)
        [SerializeField] private float starSpin = 260f;    // 별 하나하나가 제자리에서 도는 속도
        [SerializeField] private float pulse = 0.12f;      // 크기 깜빡임

        private CharacterHealth owner;
        private Vector3[] baseScales;

        public void Attach(CharacterHealth target) => owner = target;

        private void OnEnable() => CacheScales();

        private void CacheScales()
        {
            baseScales = new Vector3[transform.childCount];
            for (int i = 0; i < baseScales.Length; i++) baseScales[i] = transform.GetChild(i).localScale;
        }

        private void LateUpdate()
        {
            // 같은 캐릭터가 다시 살아나면 별을 지운다.
            if (Application.isPlaying && owner != null && owner.IsAlive)
            {
                Destroy(gameObject);
                return;
            }

            float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            int n = transform.childCount;
            if (baseScales == null || baseScales.Length != n) CacheScales();

            Camera cam = Camera.main;
            for (int i = 0; i < n; i++)
            {
                Transform star = transform.GetChild(i);
                float angle = (time * orbitSpeed + i * 360f / n) * Mathf.Deg2Rad;
                star.localPosition = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * tilt, Mathf.Sin(angle) * radius);

                // 카메라를 바라보면서 제자리에서 빙글 돈다.
                Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
                star.rotation = face * Quaternion.Euler(0f, 0f, time * starSpin + i * 40f);

                float s = 1f + Mathf.Sin(time * 6f + i) * pulse;
                star.localScale = baseScales[i] * s;
            }

#if UNITY_EDITOR
            // 편집 화면(프리팹 보기)에서도 계속 돌아가게 화면을 갱신한다.
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }
    }

    // 플레이어가 죽으면 머리 위에 DizzyStars 프리팹을 붙인다. 씬에 따로 둘 필요 없다.
    public static class DizzyStarsSpawner
    {
        private const string PrefabPath = "DizzyStars";   // Assets/Resources/DizzyStars.prefab
        private const float AboveHead = 0.35f;
        private static GameObject prefab;
        private static bool warned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            CharacterHealth.AnyDied -= HandleDied;
            CharacterHealth.AnyDied += HandleDied;
        }

        private static void HandleDied(CharacterHealth victim, HitInfo hit)
        {
            if (victim == null) return;   // 도망자 플레이어와 AI 모두

            if (prefab == null) prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null)
            {
                if (!warned) Debug.LogWarning("기절 별 프리팹이 없습니다. Tools > 기절 별 이펙트 만들기를 실행하세요.");
                warned = true;
                return;
            }

            DizzyStars old = victim.GetComponentInChildren<DizzyStars>(true);
            if (old != null) Object.Destroy(old.gameObject);

            Transform t = victim.transform;
            GameObject go = Object.Instantiate(prefab, t);
            go.name = "DizzyStars";
            go.transform.position = new Vector3(t.position.x, HeadTop(victim) + AboveHead, t.position.z);
            go.transform.rotation = Quaternion.identity;
            // 캐릭터 크기(Scale)와 상관없이 별이 같은 크기로 보이게 한다.
            float scale = Mathf.Max(0.01f, t.lossyScale.x);
            go.transform.localScale = Vector3.one / scale;
            go.GetComponent<DizzyStars>()?.Attach(victim);
        }

        // 머리 꼭대기 높이(월드). 실제 보이는 캐릭터 모델(메시) 범위로 재고,
        // 메시가 없을 때만 CharacterController 높이를 쓴다. (컨트롤러 캡슐은 모델보다 훨씬 높게 잡혀 있음)
        private static float HeadTop(CharacterHealth victim)
        {
            Transform t = victim.transform;
            bool found = false;
            float top = 0f;
            foreach (Renderer r in victim.GetComponentsInChildren<Renderer>())
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                if (!r.enabled || r.GetComponentInParent<DizzyStars>() != null) continue;
                top = found ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y;
                found = true;
            }
            if (found) return top;

            if (victim.TryGetComponent(out CharacterController cc))
                return t.position.y + (cc.center.y + cc.height * 0.5f) * t.lossyScale.y;
            return t.position.y + 2f;
        }
    }
}
