using System;
using UnityEngine;

namespace GameServer
{
    [RequireComponent(typeof(CharacterController))]

    public class CharacterMove : MonoBehaviour
    {
        [SerializeField] private float walkSpeed = 2f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float rotationSpeed = 720f;

        [Header("Jump Assist")]
        [Tooltip("착지 직전에 점프를 눌러도 이 시간(초) 안에 땅에 닿으면 점프한다. 원격 JUMP 메시지도 같은 방식으로 보관된다.")]
        [SerializeField] private float jumpBufferTime = 0.15f;
        [Tooltip("땅에서 떨어진 뒤에도 이 시간(초) 동안은 땅에 있는 것으로 본다. 높은 FPS에서 isGrounded가 깜빡이는 것을 보정한다.")]
        [SerializeField] private float coyoteTime = 0.12f;
        [Tooltip("CharacterController.isGrounded 외에 발밑을 직접 검사하는 거리")]
        [SerializeField] private float groundProbeDistance = 0.1f;
        [SerializeField] private LayerMask groundMask = ~0;

        private Vector3 pushOff;
        private CharacterController controller;
        private CharacterHealth health;
        private float verticalVelocity;

        private bool isStandingOnCharacter;

        private bool grounded;          // 보정된 접지 상태
        private float coyoteTimer;      // 남은 코요테 시간
        private float jumpBufferTimer;  // 남은 점프 입력 보관 시간
        private readonly RaycastHit[] probeHits = new RaycastHit[8];

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public bool IsGrounded => grounded;
        public float PlanarSpeed { get; private set; }
        public event Action Jumped; // 네트워크 JUMP 송신용

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<CharacterHealth>();
        }

        public bool Move(Vector3 inputDir, bool run, bool jump, float deltaTime)
        {
            return ExecuteMove(inputDir, run, jump, false, deltaTime);
        }

        public bool Move(Vector3 inputDir, bool run, bool jump, bool useCameraRelative, float deltaTime)
        {
            return ExecuteMove(inputDir, run, jump, useCameraRelative, deltaTime);
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.collider.GetComponent<CharacterController>() != null)
            {
                if (hit.normal.y > 0.7f)
                {
                    isStandingOnCharacter = true;
                    return;
                }

                Vector3 side = transform.position - hit.collider.transform.position;
                side.y = 0f;
                pushOff = side.normalized * 1.5f;
            }
        }

        private bool ExecuteMove(Vector3 inputDir, bool run, bool jump, bool useCameraRelative, float deltaTime)
        {
            if (health != null && !health.IsAlive)
            {
                PlanarSpeed = 0f;
                jumpBufferTimer = 0f;
                return false;
            }

            bool jumped = false;

            // 1) 점프 입력 보관: 눌린 한 프레임에 땅 판정이 빠져도 잠시 기억한다.
            jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - deltaTime);
            if (jump) jumpBufferTimer = jumpBufferTime;

            // 2) 접지 판정 보정: isGrounded + 발밑 검사 + 코요테 시간
            grounded = verticalVelocity <= 0f && (controller.isGrounded || ProbeGround());
            if (grounded)
            {
                coyoteTimer = coyoteTime;
                if (verticalVelocity < 0f) verticalVelocity = -2f;
            }
            else
            {
                coyoteTimer = Mathf.Max(0f, coyoteTimer - deltaTime);
            }

            bool canJump = coyoteTimer > 0f && verticalVelocity <= 0f && !isStandingOnCharacter;
            if (jumpBufferTimer > 0f && canJump)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                jumped = true;
                grounded = false;
                jumpBufferTimer = 0f;
                coyoteTimer = 0f; // 공중에서 한 번 더 뛰지 않게 한다.
                Jumped?.Invoke();
            }

            isStandingOnCharacter = false;

            verticalVelocity += gravity * deltaTime;

            Vector3 moveDirection = Vector3.zero;

            if (useCameraRelative && Camera.main != null)
            {
                Transform cam = Camera.main.transform;
                Vector3 camForward = cam.forward;
                Vector3 camRight = cam.right;
                camForward.y = 0f;
                camRight.y = 0f;
                camForward.Normalize();
                camRight.Normalize();
                moveDirection = (camForward * inputDir.z) + (camRight * inputDir.x);
            }
            else
            {
                moveDirection = new Vector3(inputDir.x, 0f, inputDir.z);
            }

            if (moveDirection.sqrMagnitude > 1f)
                moveDirection.Normalize();

            bool hasInput = moveDirection.sqrMagnitude > 0.0001f;
            float speed = hasInput ? (run ? runSpeed : walkSpeed) : 0f;
            Vector3 planarVelocity = moveDirection * speed;
            PlanarSpeed = planarVelocity.magnitude;

            if (hasInput)
            {
                RotateTowards(moveDirection, deltaTime);
            }

            planarVelocity += pushOff;
            pushOff = Vector3.MoveTowards(pushOff, Vector3.zero, 8f * deltaTime);

            Vector3 finalMove = (planarVelocity + Vector3.up * verticalVelocity) * deltaTime;
            controller.Move(finalMove);

            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
                grounded = true;
                coyoteTimer = coyoteTime;
            }

            return jumped;
        }

        // 발밑을 짧게 SphereCast 해서 땅이 있는지 본다. 자기 자신의 콜라이더와 트리거는 무시한다.
        private bool ProbeGround()
        {
            Vector3 scale = transform.lossyScale;
            float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfHeight = Mathf.Max(controller.height * Mathf.Abs(scale.y) * 0.5f, radius);
            Vector3 center = transform.TransformPoint(controller.center);

            float castRadius = radius * 0.9f;
            float lift = radius - castRadius + 0.05f; // 발바닥보다 살짝 위에서 시작한다.
            Vector3 origin = center + Vector3.down * (halfHeight - radius) + Vector3.up * lift;
            float distance = lift + controller.skinWidth + groundProbeDistance;

            int count = Physics.SphereCastNonAlloc(origin, castRadius, Vector3.down, probeHits,
                                                   distance, groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = probeHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance <= 0f) continue;          // 시작부터 겹친 콜라이더는 신뢰하지 않는다.
                if (hit.normal.y < 0.5f) continue;         // 벽이나 급경사는 땅으로 보지 않는다.
                return true;
            }
            return false;
        }

        public void FaceDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(direction);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
            verticalVelocity = 0f;
            PlanarSpeed = 0f;
            isStandingOnCharacter = false;
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            grounded = false;
        }

        private void RotateTowards(Vector3 direction, float deltaTime)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * deltaTime);
        }
    }
}
