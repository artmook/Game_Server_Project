using GameServer;
using System;
using UnityEngine;

namespace GameServer
{
    public class CharacterCombat : MonoBehaviour
    {
        [SerializeField] private float cooldown = 2f;
        [SerializeField] private float wrongHitCooldown = 8f;   // AI를 잘못 때리면 이만큼 기다린다 (오인 공격 페널티)
        [SerializeField] private float attackRange = 3f;      // 공격 사거리
        [SerializeField] private float maxAimDistance = 50f;  // 카메라 Ray 길이
        [SerializeField] private LayerMask hittableLayers = ~0;
        [SerializeField] private bool canDealDamage = true;
        [SerializeField] private HitEffectPool hitEffectPool;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        private float cooldownRemaining;
        private float lastCooldown;   // 지금 돌고 있는 쿨타임 길이 (UI 게이지용)

        public float CooldownDuration => lastCooldown > 0f ? lastCooldown : cooldown;
        public float CooldownRemaining => cooldownRemaining;
        public bool IsReady => cooldownRemaining <= 0f;
        // false: attack animation/cooldown only, no hit (MissionIntro turns this off for non-taggers)
        public bool CanDealDamage { get => canDealDamage; set => canDealDamage = value; }

        public event Action AttackPerformed;
        public event Action<IDamageable> TargetHit;
        public event Action WrongHit;   // 사람이 아닌 AI를 맞혔을 때

        public void Tick(float deltaTime)
        {
            if (cooldownRemaining > 0f)
                cooldownRemaining = Mathf.Max(0f, cooldownRemaining - deltaTime);
        }

        public void ResetCooldown() => cooldownRemaining = 0f;

        public bool TryAttack(in CharacterCommand command, CharacterAnimator characterAnimator)
        {
            if (!IsReady) return false;

            cooldownRemaining = lastCooldown = cooldown;
            characterAnimator.PlayAttack();
            AttackPerformed?.Invoke();

            if (canDealDamage && command.HasAim)
                TryHit(command.AimRay);

            return true;
        }

        private void TryHit(Ray ray)
        {
            int count = Physics.RaycastNonAlloc(ray, hitBuffer, maxAimDistance,
                                                hittableLayers, QueryTriggerInteraction.Ignore);

            bool found = false;
            RaycastHit closest = default;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance < closestDistance)
                {
                    closestDistance = hit.distance;
                    closest = hit;
                    found = true;
                }
            }
            if (!found) return;

            // 사거리
            if (Vector3.Distance(transform.position, closest.point) > attackRange) return;

            IDamageable target = closest.collider.GetComponentInParent<IDamageable>();
            if (target == null || !target.IsAlive) return;

            target.TakeHit(new HitInfo(gameObject, closest.point, closest.normal));
            if (hitEffectPool != null) hitEffectPool.Play(closest.point, closest.normal);
            TargetHit?.Invoke(target);

            // 오인 공격: AI를 맞히면 다음 공격까지 더 오래 기다린다. (원격 플레이어는 Player 태그)
            if (target is Component hitComponent && !hitComponent.CompareTag("Player"))
            {
                cooldownRemaining = lastCooldown = wrongHitCooldown;
                WrongHit?.Invoke();
            }
        }
    }
}
