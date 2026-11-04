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
        private CharacterAction actions;
        private CharacterCombat combat;
        private CharacterHealth health;
        private ICommandSource commandSource;

        public CharacterHealth Health => health;
        public CharacterCombat Combat => combat;
        public CharacterAction Actions => actions;
        public bool IsAlive => health.IsAlive;

        public event Action<Character> Died;

        private void Awake()
        {
            motor = GetComponent<CharacterMove>();
            characterAnimator = GetComponent<CharacterAnimator>();
            actions = GetComponent<CharacterAction>();
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
            actions.Tick(dt);

            if (!health.IsAlive)
            {
                motor.Move(Vector3.zero, false, false, false, dt);
                return;
            }

            CharacterCommand command = commandSource != null ? commandSource.ReadCommand() : default;

            if (command.ActionIndex > 0)
                actions.TryPerform(command.ActionIndex, characterAnimator);

            if (command.Attack && !actions.IsBusy &&
                combat.TryAttack(command, characterAnimator) && command.HasAim)
            {
                motor.FaceDirection(command.AimRay.direction);
            }

            bool locked = actions.LocksMovement;
            Vector3 move = locked ? Vector3.zero : command.MoveDirection;

            bool useCamera = commandSource is PlayerInputSource;
            bool jumped = motor.Move(move, command.Run, command.Jump && !locked, useCamera, dt);

            if (jumped) characterAnimator.PlayJump();
            characterAnimator.SetLocomotion(motor.PlanarSpeed, motor.IsGrounded, dt);
        }

        private void HandleDied(HitInfo hit)
        {
            actions.Cancel();
            characterAnimator.PlayDie();
            Died?.Invoke(this);
        }

        // AI 리스폰 호출
        public void Spawn(Vector3 position, Quaternion rotation)
        {
            motor.Teleport(position, rotation);
            health.ResetHealth();
            actions.Cancel();
            combat.ResetCooldown();
            characterAnimator.ResetState();
            commandSource?.ResetSource();
        }
    }
}
