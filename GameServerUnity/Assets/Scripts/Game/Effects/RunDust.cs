/*
[ 코드 설명 ]

RunDust (달리기 먼지)

1. 플레이어 캐릭터(Player 태그, 술래 포함)가 땅 위에서 달리면 발밑에서 뽀얀 먼지가 퐁퐁 일어난다.
   걷거나, 멈추거나, 공중에 있거나, 죽으면 먼지가 그친다(이미 난 먼지는 자연스럽게 사라진다).
2. 먼지 모양은 프리팹(Assets/Resources/RunDust.prefab)에 있다. Tools > 달리기 먼지 이펙트 만들기로 만든다.
   프리팹을 열고 파티클을 선택하면 게임을 실행하지 않아도 먼지 모양을 볼 수 있다.
3. 아무 데도 붙일 필요 없다. 게임이 시작되면 RunDustSpawner가 플레이어 캐릭터를 찾아 알아서 붙인다
   (나중에 들어오는 플레이어·봇도 0.5초마다 확인해서 붙인다).
4. AI에게는 붙이지 않는다. 술래가 "먼지 나는 캐릭터 = 플레이어"로 알아챌 수 있으니,
   AI에게도 보이게 하려면 RunDustSpawner.IncludeAI를 true로 바꾸면 된다.
*/

using UnityEngine;

namespace GameServer
{
    public class RunDust : MonoBehaviour
    {
        private Character owner;
        private CharacterMove motor;
        private ParticleSystem dust;
        private ParticleSystem.EmissionModule emission;

        public void Attach(Character target)
        {
            owner = target;
            motor = target.GetComponent<CharacterMove>();
            dust = GetComponentInChildren<ParticleSystem>();
            if (dust != null)
            {
                emission = dust.emission;
                emission.enabled = false;
                dust.Play();
            }
        }

        private void LateUpdate()
        {
            if (dust == null || owner == null || motor == null) return;
            emission.enabled = IsRunning();
        }

        private bool IsRunning()
        {
            if (!owner.IsAlive || !motor.IsGrounded) return false;
            // 걷기 속도와 달리기 속도의 중간보다 빠르면 달리는 중
            float threshold = Mathf.Lerp(motor.WalkSpeed, motor.RunSpeed, 0.5f);
            return motor.PlanarSpeed > threshold;
        }
    }

    // 플레이어 캐릭터에 RunDust 프리팹을 붙인다. 씬에 따로 둘 필요 없다.
    public class RunDustSpawner : MonoBehaviour
    {
        private const string PrefabPath = "RunDust";   // Assets/Resources/RunDust.prefab
        public static bool IncludeAI = false;

        private static GameObject prefab;
        private float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null) return;   // 아직 Tools > 달리기 먼지 이펙트 만들기를 안 했으면 아무것도 안 한다.
            var go = new GameObject("RunDustSpawner") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<RunDustSpawner>();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;

            foreach (Character c in FindObjectsByType<Character>(FindObjectsSortMode.None))
            {
                bool wanted = IncludeAI || c.CompareTag("Player");
                RunDust existing = c.GetComponentInChildren<RunDust>(true);
                if (wanted && existing == null) Add(c);
                else if (!wanted && existing != null) Destroy(existing.gameObject);
            }
        }

        private static void Add(Character c)
        {
            Transform t = c.transform;
            GameObject go = Instantiate(prefab, t);
            go.name = "RunDust";
            go.transform.position = t.position + Vector3.up * 0.05f;   // 발밑
            go.transform.rotation = Quaternion.identity;
            float scale = Mathf.Max(0.01f, t.lossyScale.x);
            go.transform.localScale = Vector3.one / scale;              // 캐릭터 크기와 상관없이 같은 먼지
            go.GetComponent<RunDust>().Attach(c);
        }
    }
}
