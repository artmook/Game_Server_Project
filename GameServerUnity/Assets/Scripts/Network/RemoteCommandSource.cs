/*
[ 코드 설명 ]

RemoteCommandSource

1. 다른 플레이어 캐릭터를 서버에서 받은 위치·행동으로 움직이는 ICommandSource이다.
2. 보고된 위치로 걸어가거나 뛰어가게 해서 기존 이동·애니메이션 코드를 그대로 쓴다.
3. 너무 멀어지면 순간이동으로 맞추고, 멈춰 있을 때는 보고된 방향을 바라본다.
4. 행동·점프·공격은 한 번만 실행되도록 프레임 단위로 소비한다.
*/

using UnityEngine;

namespace GameServer
{
    [RequireComponent(typeof(CharacterMove))]
    public class RemoteCommandSource : MonoBehaviour, ICommandSource
    {
        [SerializeField] private float stopDistance = 0.15f;  // 이 거리 안이면 도착으로 본다.
        [SerializeField] private float snapDistance = 3f;     // 이보다 벌어지면 순간이동한다.

        private CharacterMove motor;
        private CharacterCombat combat;
        private Vector3 targetPosition;
        private float targetRotation;
        private float reportedSpeed;
        private bool hasTarget;

        private int pendingAction;
        private bool pendingJump;
        private bool pendingAttack;
        private int commandFrame = -1;
        private CharacterCommand frameCommand;

        public int PlayerId { get; set; }

        private void Awake()
        {
            motor = GetComponent<CharacterMove>();
            combat = GetComponent<CharacterCombat>();
        }

        public void SetState(Vector3 position, float rotationY, float speed)
        {
            targetPosition = position;
            targetRotation = rotationY;
            reportedSpeed = speed;
            Vector3 gap = position - transform.position;
            if (!hasTarget || gap.sqrMagnitude > snapDistance * snapDistance)
                motor.Teleport(position, Quaternion.Euler(0f, rotationY, 0f));
            hasTarget = true;
        }

        public void QueueAction(int index) => pendingAction = index;
        public void QueueJump() => pendingJump = true;
        public void QueueAttack() => pendingAttack = true;

        // 한 프레임에 여러 번 불려도 같은 명령을 돌려준다.
        public CharacterCommand ReadCommand()
        {
            int frame = Time.frameCount;
            if (commandFrame == frame) return frameCommand;
            commandFrame = frame;

            var command = new CharacterCommand();
            if (hasTarget)
            {
                Vector3 delta = targetPosition - transform.position;
                delta.y = 0f;
                if (delta.magnitude > stopDistance)
                {
                    command.MoveDirection = delta.normalized;
                    command.Run = reportedSpeed > (motor.WalkSpeed + motor.RunSpeed) * 0.5f;
                }
                else
                {
                    motor.FaceDirection(Quaternion.Euler(0f, targetRotation, 0f) * Vector3.forward);
                }
            }

            if (pendingAction > 0) { command.ActionIndex = pendingAction; pendingAction = 0; }
            if (pendingJump) { command.Jump = true; pendingJump = false; }
            if (pendingAttack)
            {
                // 쿨타임은 서버가 판정했으므로 동작이 항상 보이게 한다. 조준이 없어 피해는 주지 않는다.
                if (combat != null) combat.ResetCooldown();
                command.Attack = true;
                pendingAttack = false;
            }
            frameCommand = command;
            return command;
        }

        public void ResetSource()
        {
            hasTarget = false;
            pendingAction = 0;
            pendingJump = false;
            pendingAttack = false;
            commandFrame = -1;
        }
    }
}
