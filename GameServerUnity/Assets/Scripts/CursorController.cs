using UnityEngine;
using UnityEngine.InputSystem;

namespace GameServer
{
    public class CursorController : MonoBehaviour
    {
        [SerializeField] private bool lockOnStart = true;
        [SerializeField] private bool allowEscapeUnlock = true; // 테스트용

        private void Start()
        {
            if (lockOnStart) Lock();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (allowEscapeUnlock && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                Unlock();
            else if (Cursor.lockState != CursorLockMode.Locked && mouse != null && mouse.leftButton.wasPressedThisFrame)
                Lock();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && lockOnStart) Lock();
        }

        public static void Lock()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public static void Unlock()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
