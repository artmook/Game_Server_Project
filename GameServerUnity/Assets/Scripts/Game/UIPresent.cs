using UnityEngine;

namespace GameServer
{
    public class UIPresent : MonoBehaviour
    {
        [SerializeField] private UIView view;
        [SerializeField] private SurvivorCounter survivors;   // ← gameManager 대신
        [SerializeField] private Character player;
        [Tooltip("다른 사람의 잡기·미션 완료 알림용. 비워 두면 씬에서 찾는다.")]
        [SerializeField] private NetworkGameManager network;

        private void Awake()
        {
            if (network == null) network = FindFirstObjectByType<NetworkGameManager>();
        }

        private void OnEnable()
        {
            CharacterHealth.AnyDied += HandleAnyDied;
            if (player != null && player.TryGetComponent(out CharacterCombat c)) c.CooldownPenaltyApplied += view.ShowPenalty;
            if (network != null)
            {
                network.PlayerKilled += HandlePlayerKilled;
                network.MissionReported += HandleMissionReported;
            }
            if (survivors == null) return;
            survivors.RemainingPlayersChanged += view.SetRemainingPlayers;
        }

        private void OnDisable()
        {
            CharacterHealth.AnyDied -= HandleAnyDied;
            if (player != null && player.TryGetComponent(out CharacterCombat c)) c.CooldownPenaltyApplied -= view.ShowPenalty;
            if (network != null)
            {
                network.PlayerKilled -= HandlePlayerKilled;
                network.MissionReported -= HandleMissionReported;
            }
            if (survivors == null) return;
            survivors.RemainingPlayersChanged -= view.SetRemainingPlayers;
        }

        // 서버 KILL 알림(킬 로그). 내가 잡은 경우는 HandleAnyDied가 이미 띄우므로 나머지 사람에게만 띄운다.
        // 잡아도 시간이나 보상은 늘지 않는다(서버 규칙).
        private void HandlePlayerKilled(int attacker, int target, int remaining)
        {
            if (attacker == 0 || attacker == network.MyId) return;
            string runner = target == network.MyId ? "나" : network.GetNickname(target);
            view.ShowFeed(view.CatchText(network.GetNickname(attacker), runner));
        }

        private void HandleMissionReported(int id, int done, int runners)
        {
            string who = id == network.MyId ? "내가" : $"<noparse>{network.GetNickname(id)}</noparse>님이";
            view.ShowFeed($"{who} 미션을 모두 완료했습니다. <color=#FFFFFFAA>({done}/{runners})</color>");
        }

        // 내 캐릭터(술래)가 처치했을 때만 처치 알림을 띄운다. (도망자는 공격 피해가 없어서 처치할 수 없다.)
        // AI·원격 플레이어 모두 해당된다. 원격 플레이어는 서버 KILL이 공격자를 내 캐릭터로 알려 줄 때 뜬다.
        private void HandleAnyDied(CharacterHealth victim, HitInfo hit)
        {
            if (player == null || hit.Attacker != player.gameObject || victim.gameObject == player.gameObject) return;
            view.ShowKill(DisplayName(victim));
        }

        private string DisplayName(CharacterHealth victim)
        {
            if (!victim.CompareTag("Player")) return "AI";
            var character = victim.GetComponent<Character>();
            return survivors != null && character != null
                ? survivors.GetDisplayName(character)
                : victim.gameObject.name.Replace("(Clone)", "");
        }

        // Start는 필요 없습니다. SurvivorCounter.Start가 첫 값(3 / 3)을 보내 줍니다.

        private void Update()
        {
            if (player == null) return;
            CharacterCombat combat = player.Combat;
            view.SetCooldown(combat.CooldownRemaining, combat.CurrentCooldownDuration, combat.IsPenalized);
        }
    }
}
