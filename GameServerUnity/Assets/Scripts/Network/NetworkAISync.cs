/*
[ 코드 설명 ]

NetworkAISync (AI 위치 동기화, 호스트 권한)

1. 서버가 정한 AI 호스트(게임에 남은 가장 작은 ID) 한 명만 AI를 실제로 움직인다(NavAIInputSource).
   호스트는 초당 10회 모든 AI의 위치·방향·속도·기절 여부를 한 메시지로 묶어 보낸다.
   GAME|AI|번호:x:y:z:회전:속도:기절(0/1),번호:...
2. 나머지 클라이언트의 AI는 꼭두각시이다. NavAIInputSource를 끄고 RemoteCommandSource로 바꿔,
   받은 위치로 걸어가게(멀면 순간이동) 해서 기존 이동·애니메이션 코드를 그대로 쓴다.
3. AI 번호는 GameManager.ActiveAI의 인덱스이다. 모든 클라이언트가 같은 시드·같은 순서로 AI를 만든다.
4. 기절: 술래가 AI를 때리면 자기 화면에서 바로 쓰러뜨리고 GAME|AISTUN|번호를 보낸다.
   서버가 다른 사람에게 알리면 호스트는 그 AI를 기절시키고(8초 뒤 일어남), 꼭두각시 화면도 바로 쓰러뜨린다.
   이후 기절·회복은 호스트의 묶음(기절 0/1)을 따른다. 방금 쓰러뜨린 AI는 호스트 반영 전의 묶음에
   "기절 아님"이 와도 잠깐(stunGrace) 무시한다.
5. 호스트가 바뀌면(AIHOST) 새 호스트는 꼭두각시를 다시 NavAIInputSource로 돌려 이어서 움직인다.
   게임이 끝나면 모든 AI를 원래(NavAIInputSource)대로 돌린다.

NetworkGameManager가 실행할 때 이 컴포넌트를 붙이고 메시지를 넘겨준다(씬 수정 불필요).
*/

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace GameServer
{
    public class NetworkAISync : MonoBehaviour
    {
        private const int MaxPayloadLength = 4000;   // 서버 입력 제한(4096바이트) 안

        [SerializeField] private float sendInterval = 0.1f;
        [Tooltip("내 화면에서 방금 쓰러뜨린 AI는 호스트가 반영하기 전까지 이 시간 동안 '기절 아님'을 무시한다.")]
        [SerializeField] private float stunGrace = 1.5f;

        private NetworkGameManager network;
        private GameManager gameManager;
        private readonly Dictionary<int, float> stunnedAt = new Dictionary<int, float>();
        private readonly StringBuilder payload = new StringBuilder(1024);
        private float sendTimer;
        private bool active;

        public int HostId { get; private set; }
        public bool IsHost => active && network != null && HostId != 0 && HostId == network.MyId;

        public void Init(NetworkGameManager owner, GameManager manager)
        {
            network = owner;
            gameManager = manager;
        }

        // ───────────── 게임 시작·끝·호스트 변경 (NetworkGameManager가 부른다) ─────────────

        public void BeginGame(int hostId)
        {
            active = true;
            HostId = hostId;
            stunnedAt.Clear();
            sendTimer = 0f;
            ApplyMode();
        }

        public void EndGame()
        {
            if (gameManager != null)
                foreach (Character ai in gameManager.ActiveAI)
                    if (ai != null) RestoreNav(ai, spawnIfDown: false);   // 곧 풀에 반납되며, 다음 Spawn이 상태를 되돌린다.
            active = false;
            HostId = 0;
            stunnedAt.Clear();
        }

        public void SetHost(int hostId)
        {
            if (!active) return;
            HostId = hostId;
            sendTimer = 0f;
            ApplyMode();
        }

        private void ApplyMode()
        {
            if (gameManager == null) return;
            foreach (Character ai in gameManager.ActiveAI)
            {
                if (ai == null) continue;
                if (IsHost) RestoreNav(ai, spawnIfDown: true);
                else MakePuppet(ai);
            }
        }

        // 원래대로: NavAIInputSource가 AI를 움직인다.
        private static void RestoreNav(Character ai, bool spawnIfDown)
        {
            if (!ai.TryGetComponent(out NavAIInputSource nav)) return;
            ai.SetCommandSource(nav);
            if (nav.enabled) return;
            nav.enabled = true;   // OnEnable이 기절 이벤트를 다시 구독한다.
            if (!ai.gameObject.activeInHierarchy) return;
            // 꼭두각시였던 AI를 이어받는다. 쓰러져 있던 AI는 회복 타이머가 없으므로 바로 일으킨다.
            if (!ai.IsAlive) { if (spawnIfDown) ai.Spawn(ai.transform.position, ai.transform.rotation); }
            else nav.ResetSource();
        }

        // 꼭두각시: 받은 위치로만 움직인다.
        private static RemoteCommandSource MakePuppet(Character ai)
        {
            if (ai.TryGetComponent(out NavAIInputSource nav) && nav.enabled)
                nav.enabled = false;   // OnDisable이 NavMeshAgent를 끈다.
            if (!ai.TryGetComponent(out RemoteCommandSource source))
            {
                source = ai.gameObject.AddComponent<RemoteCommandSource>();
                source.ResetSource();
            }
            ai.SetCommandSource(source);
            return source;
        }

        // ───────────── 호스트: 보내기 ─────────────

        private void Update()
        {
            if (!IsHost || !network.InGame || gameManager == null) return;
            sendTimer -= Time.deltaTime;
            if (sendTimer > 0f) return;
            sendTimer = sendInterval;

            payload.Clear();
            IReadOnlyList<Character> list = gameManager.ActiveAI;
            for (int i = 0; i < list.Count; i++)
            {
                Character ai = list[i];
                if (ai == null || !ai.gameObject.activeInHierarchy) continue;
                Transform t = ai.transform;
                Vector3 p = t.position;
                float speed = ai.TryGetComponent(out CharacterMove motor) ? Mathf.Clamp(motor.PlanarSpeed, 0f, 20f) : 0f;
                string entry = string.Concat(
                    i.ToString(CultureInfo.InvariantCulture), ":", F(p.x), ":", F(p.y), ":", F(p.z), ":",
                    Mathf.RoundToInt(t.eulerAngles.y).ToString(CultureInfo.InvariantCulture), ":", F(speed), ":",
                    ai.IsAlive ? "0" : "1");
                if (payload.Length + entry.Length + 1 > MaxPayloadLength) break;   // AI가 아주 많을 때만 해당
                if (payload.Length > 0) payload.Append(',');
                payload.Append(entry);
            }
            if (payload.Length > 0) network.Send("GAME|AI|" + payload);
        }

        // ───────────── 나머지: 받기 ─────────────

        public void ApplySnapshot(string data)
        {
            if (!active || IsHost || gameManager == null) return;
            IReadOnlyList<Character> list = gameManager.ActiveAI;
            foreach (string entry in data.Split(','))
            {
                string[] v = entry.Split(':');
                if (v.Length != 7) continue;
                if (!int.TryParse(v[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
                    index < 0 || index >= list.Count) continue;
                Character ai = list[index];
                if (ai == null || !ai.gameObject.activeInHierarchy) continue;
                if (!TryFloat(v[1], out float x) || !TryFloat(v[2], out float y) || !TryFloat(v[3], out float z) ||
                    !TryFloat(v[4], out float rotation) || !TryFloat(v[5], out float speed)) continue;
                bool stunned = v[6] == "1";

                RemoteCommandSource source = MakePuppet(ai);
                if (stunned)
                {
                    if (ai.IsAlive) KnockDown(index, ai);
                    continue;
                }
                if (!ai.IsAlive)
                {
                    // 호스트에서 일어났다. 방금 내가 쓰러뜨린 AI면 호스트 반영을 조금 기다린다.
                    if (stunnedAt.TryGetValue(index, out float at) && Time.time - at < stunGrace) continue;
                    ai.Spawn(ai.transform.position, ai.transform.rotation);   // Idle로 일어나고 다음 위치로 순간이동
                }
                source.SetState(new Vector3(x, y, z), rotation, speed);
            }
        }

        // 다른 사람(술래)이 AI를 때렸다. 호스트는 실제로 기절시키고(8초 뒤 NavAIInputSource가 일으킴),
        // 꼭두각시 화면은 바로 쓰러뜨린다.
        public void ApplyRemoteStun(int index)
        {
            if (!active || gameManager == null) return;
            IReadOnlyList<Character> list = gameManager.ActiveAI;
            if (index < 0 || index >= list.Count) return;
            Character ai = list[index];
            if (ai != null && ai.gameObject.activeInHierarchy && ai.IsAlive) KnockDown(index, ai);
        }

        // 내 캐릭터(술래)가 AI를 맞혔다. 내 화면에서는 이미 쓰러졌으므로 다른 사람에게만 알린다.
        public void ReportLocalHit(CharacterHealth health)
        {
            if (!active || gameManager == null || health == null) return;
            IReadOnlyList<Character> list = gameManager.ActiveAI;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null || list[i].Health != health) continue;
                stunnedAt[i] = Time.time;
                network.Send("GAME|AISTUN|" + i.ToString(CultureInfo.InvariantCulture));
                return;
            }
        }

        private void KnockDown(int index, Character ai)
        {
            stunnedAt[index] = Time.time;
            ai.Health.ApplyServerDeath(null, playEffect: false);   // Die 애니메이션으로 쓰러짐
        }

        private static bool TryFloat(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);

        private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
