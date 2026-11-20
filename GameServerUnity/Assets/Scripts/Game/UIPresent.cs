using UnityEngine;

namespace GameServer
{
    public class UIPresent : MonoBehaviour
    {
        [SerializeField] private UIView view;
        [SerializeField] private SurvivorCounter survivors;   // ← gameManager 대신
        [SerializeField] private Character player;

        private void OnEnable()
        {
            CharacterHealth.AnyDied += HandleAnyDied;
            if (survivors == null) return;
            survivors.RemainingPlayersChanged += view.SetRemainingPlayers;
        }

        private void OnDisable()
        {
            CharacterHealth.AnyDied -= HandleAnyDied;
            if (survivors == null) return;
            survivors.RemainingPlayersChanged -= view.SetRemainingPlayers;
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
            view.SetCooldown(combat.CooldownRemaining, combat.CooldownDuration);
        }
    }
}
