using GameServer;
using System;
using UnityEngine;

namespace GameServer
{
    public class CharacterCombat : MonoBehaviour
    {
        [SerializeField] private float cooldown = 2f;
        [SerializeField] private float attackRange = 3f;      // 공격 사거리
        [SerializeField] private float maxAimDistance = 50f;  // 카메라 Ray 길이
        [SerializeField] private LayerMask hittableLayers = ~0;
        [SerializeField] private bool canDealDamage = true;
        [SerializeField] private HitEffectPool hitEffectPool;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        private float cooldownRemaining;
        private float currentDuration;   // this cycle's full cooldown (base + penalty), for the UI ring

        public float CooldownDuration => cooldown;
        public float CurrentCooldownDuration => currentDuration > 0f ? currentDuration : cooldown;
        public bool IsPenalized { get; private set; }   // cooldown was extended by a penalty this cycle
        public float CooldownRemaining => cooldownRemaining;
        public bool IsReady => cooldownRemaining <= 0f;
        // false: attack animation/cooldown only, no hit (MissionIntro turns this off for non-taggers)
        public bool CanDealDamage { get => canDealDamage; set => canDealDamage = value; }

        public event Action AttackPerformed;
        public event Action<IDamageable> TargetHit;
        public event Action<float> CooldownPenaltyApplied;   // added seconds

        // Extend the current cooldown (e.g. tagger killed an AI). If idle, starts a cooldown of 'seconds'.
        public void AddCooldownPenalty(float seconds)
        {
            if (seconds <= 0f) return;
            currentDuration = cooldownRemaining > 0f ? CurrentCooldownDuration + seconds : seconds;
            cooldownRemaining += seconds;
            IsPenalized = true;
            CooldownPenaltyApplied?.Invoke(seconds);
        }

        public void Tick(float deltaTime)
        {
            if (cooldownRemaining > 0f)
            {
                cooldownRemaining = Mathf.Max(0f, cooldownRemaining - deltaTime);
                if (cooldownRemaining <= 0f) IsPenalized = false;
            }
        }

        public void ResetCooldown()
        {
            cooldownRemaining = 0f;
            IsPenalized = false;
        }

        public bool TryAttack(in CharacterCommand command, CharacterAnimator characterAnimator)
        {
            if (!IsReady) return false;

            // Runner (canDealDamage off): attack motion only, so no cooldown at all.
            if (canDealDamage)
            {
                cooldownRemaining = cooldown;
                currentDuration = cooldown;
                IsPenalized = false;
            }
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
        }
    }
}
