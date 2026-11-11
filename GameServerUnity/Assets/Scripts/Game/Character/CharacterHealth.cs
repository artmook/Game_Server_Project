using GameServer;
using System;
using UnityEngine;

namespace GameServer
{
    public class CharacterHealth : MonoBehaviour, IDamageable
    {
        // 켜면 로컬 피격으로 죽지 않고 ApplyServerDeath(서버 KILL)로만 죽는다. 원격 플레이어용이다.
        [SerializeField] private bool serverAuthoritative;

        public bool IsAlive { get; private set; } = true;
        public bool ServerAuthoritative { get => serverAuthoritative; set => serverAuthoritative = value; }
        public event Action<HitInfo> Died;

        public void TakeHit(in HitInfo hit)
        {
            if (!IsAlive) return;
            // 원격 플레이어의 생사는 서버가 판정한다. 맞힌 사실은 CharacterCombat.TargetHit으로 서버에 보낸다.
            if (serverAuthoritative) return;
            Die(hit, true);
        }

        public void ApplyServerDeath(GameObject attacker, bool playEffect)
        {
            if (!IsAlive) return;
            Die(new HitInfo(attacker, transform.position, Vector3.up), playEffect);
        }

        private void Die(in HitInfo hit, bool playEffect)
        {
            IsAlive = false;

            if (playEffect && ScreenVignetteManager.Instance != null)
            {
                // 플레이어가 죽으면 초록색, AI가 죽으면 빨간색 (기존 동작)
                Color color = CompareTag("Player") ? Color.green : Color.red;
                ScreenVignetteManager.Instance.PlayHitEffect(color, 0.55f, 1.5f);
            }

            Died?.Invoke(hit);
        }

        public void ResetHealth() => IsAlive = true;
    }
}
