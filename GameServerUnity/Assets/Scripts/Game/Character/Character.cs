using GameServer;
using System;
using UnityEngine;

namespace GameServer
{
    [RequireComponent(typeof(CharacterMove), typeof(CharacterHealth))]
    public class Character : MonoBehaviour
    {
        private CharacterMove motor;
        private CharacterAnimator characterAnimator;
        private CharacterCombat combat;
        private CharacterHealth health;
        private ICommandSource commandSource;

        public CharacterHealth Health => health;
        public CharacterCombat Combat => combat;
        public bool IsAlive => health.IsAlive;

        public event Action<Character> Died;

        private void Awake()
        {
            motor = GetComponent<CharacterMove>();
            characterAnimator = GetComponent<CharacterAnimator>();
            combat = GetComponent<CharacterCombat>();
            health = GetComponent<CharacterHealth>();
            commandSource = GetComponent<ICommandSource>();
        }

        private void OnEnable() => health.Died += HandleDied;
        private void OnDisable() => health.Died -= HandleDied;

        public void SetCommandSource(ICommandSource source) => commandSource = source;

        private void Update()
        {
            float dt = Time.deltaTime;
            combat.Tick(dt);

            if (!health.IsAlive)
            {
                motor.Move(Vector3.zero, false, false, false, dt);
                return;
            }

            // GameFlow.ControlsLocked: start intro (mission roulette, countdown) - no input until Start!
            CharacterCommand command = commandSource != null && !GameFlow.ControlsLocked
                ? commandSource.ReadCommand() : default;

            if (command.Attack &&
                combat.TryAttack(command, characterAnimator) && command.HasAim)
            {
                motor.FaceDirection(command.AimRay.direction);
            }

            bool useCamera = commandSource is PlayerInputSource;
            bool jumped = motor.Move(command.MoveDirection, command.Run, command.Jump, useCamera, dt);


            if (jumped) characterAnimator.PlayJump();
            characterAnimator.SetLocomotion(motor.PlanarSpeed, motor.IsGrounded, dt);
        }

        private void HandleDied(HitInfo hit)
        {
            characterAnimator.PlayDie();
            Died?.Invoke(this);
        }

        // AI 리스폰 호출
        public void Spawn(Vector3 position, Quaternion rotation)
        {
            motor.Teleport(position, rotation);
            health.ResetHealth();
            combat.ResetCooldown();
            characterAnimator.ResetState();
            commandSource?.ResetSource();
        }

        private void OnDestroy()
        {
            var counter = FindFirstObjectByType<SurvivorCounter>();
            if (counter != null) counter.Unregister(this);
        }

    }
}
