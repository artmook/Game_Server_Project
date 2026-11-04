using UnityEngine;
using UnityEngine.InputSystem;

namespace GameServer
{
    public class PlayerInputSource : MonoBehaviour, ICommandSource
    {
        [SerializeField] private Transform cameraTransform;

        private static readonly Key[] ActionKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };

        private void Awake()
        {
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        public CharacterCommand ReadCommand()
        {
            var command = new CharacterCommand();
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null) return command;

            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;

            Vector2 input = Vector2.zero;

            if (keyboard.wKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;

            command.MoveDirection = new Vector3(input.x, 0f, input.y);
            if (command.MoveDirection.sqrMagnitude > 1f)
                command.MoveDirection.Normalize();

            command.Run = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            command.Jump = keyboard.spaceKey.wasPressedThisFrame;

            for (int i = 0; i < ActionKeys.Length; i++)
            {
                if (keyboard[ActionKeys[i]].wasPressedThisFrame)
                {
                    command.ActionIndex = i + 1;
                    break;
                }
            }

            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
                command.Attack = mouse.leftButton.wasPressedThisFrame;

            if (cameraTransform != null)
            {
                command.HasAim = true;
                command.AimRay = new Ray(cameraTransform.position, cameraTransform.forward);
            }
            return command;
        }

        public void ResetSource() { }
    }
}