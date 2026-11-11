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
            if (survivors == null) return;
            survivors.RemainingPlayersChanged += view.SetRemainingPlayers;
            survivors.CharacterDied += view.ShowDeath;
        }

        private void OnDisable()
        {
            if (survivors == null) return;
            survivors.RemainingPlayersChanged -= view.SetRemainingPlayers;
            survivors.CharacterDied -= view.ShowDeath;
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
