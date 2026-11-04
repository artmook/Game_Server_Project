using GameServer;
using UnityEngine;

namespace GameServer
{
    [RequireComponent(typeof(CharacterMove))]
    [RequireComponent(typeof(CharacterAction))]
    [RequireComponent(typeof(CharacterCombat))]
    public class CharacterAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private float speedDampTime = 0.1f;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GroundedHash = Animator.StringToHash("IsGrounded");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int DieHash = Animator.StringToHash("Die");

        private CharacterMove characterMove;
        private CharacterAction characterAction;
        private CharacterCombat characterCombat;
        private ICommandSource inputSource;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();

            characterMove = GetComponent<CharacterMove>();
            characterAction = GetComponent<CharacterAction>();
            characterCombat = GetComponent<CharacterCombat>();
            inputSource = GetComponent<ICommandSource>();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            CharacterCommand command = inputSource != null ? inputSource.ReadCommand() : new CharacterCommand();

            if (command.ActionIndex > 0)
            {
                characterAction.TryPerform(command.ActionIndex, this);
            }

            characterAction.Tick(deltaTime);

            if (characterCombat != null)
            {
                characterCombat.Tick(deltaTime);
            }

            Vector3 moveInput = command.MoveDirection;
            bool runInput = command.Run;
            bool jumpInput = command.Jump;

            if (characterAction.LocksMovement)
            {
                moveInput = Vector3.zero;
                runInput = false;
                jumpInput = false;
            }

            bool jumped = characterMove.Move(moveInput, runInput, jumpInput, true, deltaTime);
            if (jumped)
            {
                PlayJump();
            }

            if (command.Attack && !characterAction.IsBusy)
            {
                if (characterCombat != null)
                {
                    characterCombat.TryAttack(command, this);
                }
                else
                {
                    PlayAttack();
                }
            }

            SetLocomotion(characterMove.PlanarSpeed, characterMove.IsGrounded, deltaTime);
        }

        public void SetLocomotion(float speed, bool grounded, float deltaTime)
        {
            animator.SetFloat(SpeedHash, speed, speedDampTime, deltaTime);
            animator.SetBool(GroundedHash, grounded);
        }

        public void PlayJump()
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

            if (!stateInfo.IsName("Jump"))
            {
                animator.ResetTrigger(JumpHash);
                animator.SetTrigger(JumpHash);
            }
        }

        public void PlayAttack() => animator.SetTrigger(AttackHash);
        public void PlayAction(string triggerName) => animator.SetTrigger(triggerName);
        public void PlayDie() => animator.SetTrigger(DieHash);

        public void ResetState()
        {
            animator.Rebind();
            animator.Update(0f);

            // 리셋 -> 쿨타임 초기화
            if (characterCombat != null)
            {
                characterCombat.ResetCooldown();
            }
        }
    }
}