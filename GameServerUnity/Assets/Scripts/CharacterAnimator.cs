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

        private CharacterCombat characterCombat;

        // Update는 제거했다. 이동·공격·행동은 Character.Update가 한 번만 처리한다.
        // (기존에는 두 곳에서 모두 처리해 한 프레임에 두 번 움직이고 명령을 두 번 읽었다.)
        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            characterCombat = GetComponent<CharacterCombat>();
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