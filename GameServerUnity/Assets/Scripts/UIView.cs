using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameServer
{
    public class UIView : MonoBehaviour
    {
        [SerializeField] private TMP_Text cooldownText;
        [SerializeField] private Image cooldownFill;
        [SerializeField] private TMP_Text remainingText;

        public void SetCooldown(float remaining, float duration)
        {
            if (remaining <= 0f)
            {
                cooldownText.text = "АјАн";
                if (cooldownFill) cooldownFill.fillAmount = 0f;
                return;
            }
            cooldownText.text = $"{remaining:0.0}s";
            if (cooldownFill) cooldownFill.fillAmount = remaining / duration;
        }

        public void SetRemainingPlayers(int remaining, int max)
        {
            remainingText.text = $"({remaining}/{max})";
        }
    }
}
