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

        private Vector3 pushOff;
        private CharacterController controller;
        private CharacterHealth health;
        private float verticalVelocity;

        private bool isStandingOnCharacter;

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public bool IsGrounded => controller.isGrounded;
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
                return false;
            }

            bool jumped = false;

            if (controller.isGrounded)
            {
                if (verticalVelocity < 0f) verticalVelocity = -2f;

                if (jump && !isStandingOnCharacter)
                {
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    jumped = true;
                    Jumped?.Invoke();
                }
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
            }

            return jumped;
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
        }

        private void RotateTowards(Vector3 direction, float deltaTime)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * deltaTime);
        }
    }
}
