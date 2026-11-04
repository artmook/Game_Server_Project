using GameServer;
using UnityEngine;

namespace GameServer
{
    public class UIPresent : MonoBehaviour
    {
        [SerializeField] private UIView view;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private Character player;

        private float lastShownCooldown = -1f;

        private void OnEnable()
        {
            if (gameManager != null) gameManager.RemainingPlayersChanged += view.SetRemainingPlayers;
        }

        private void OnDisable()
        {
            if (gameManager != null) gameManager.RemainingPlayersChanged -= view.SetRemainingPlayers;
        }

        private void Start()
        {
            view.SetRemainingPlayers(gameManager.RemainingPlayers, gameManager.MaxPlayers);
            view.SetCooldown(0f, player.Combat.CooldownDuration);
        }

        private void Update()
        {
            CharacterCombat combat = player.Combat;
            float rounded = Mathf.Ceil(combat.CooldownRemaining * 10f) / 10f;
            if (Mathf.Approximately(rounded, lastShownCooldown)) return;

            lastShownCooldown = rounded;
            view.SetCooldown(combat.CooldownRemaining, combat.CooldownDuration);
        }
    }
}
