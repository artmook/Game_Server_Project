using GameServer;
using System;
using UnityEngine;

namespace GameServer
{
    public class CharacterHealth : MonoBehaviour, IDamageable
    {
        public bool IsAlive { get; private set; } = true;
        public event Action<HitInfo> Died;

        public void TakeHit(in HitInfo hit)
        {
            if (!IsAlive) return;
            IsAlive = false;

            bool isPlayer = CompareTag("Player");

            if (!isPlayer)
            {
                if (ScreenVignetteManager.Instance != null)
                {
                    ScreenVignetteManager.Instance.PlayHitEffect(Color.red, 0.55f, 1.5f);
                }
            }
            else if (isPlayer)
            {
                if (ScreenVignetteManager.Instance != null)
                {
                    ScreenVignetteManager.Instance.PlayHitEffect(Color.green, 0.55f, 1.5f);
                }
            }

            Died?.Invoke(hit);
        }

        public void ResetHealth() => IsAlive = true;
    }
}
