using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameServer
{
    public class CursorController : MonoBehaviour
    {
        [SerializeField] private bool lockOnStart = true;
        [SerializeField] private bool allowEscapeUnlock = true; // 테스트용

        // 마우스를 잠가도 되는지 판단한다. NetworkGameManager가 설정한다.
        // (대기실이거나 로비 UI 위를 클릭한 경우 잠그지 않아 버튼을 누를 수 있다.)
        public static Func<bool> CanLock;

        private static bool LockAllowed => CanLock == null || CanLock();

        private void Start()
        {
            if (lockOnStart && LockAllowed) Lock();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (allowEscapeUnlock && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                Unlock();
            else if (Cursor.lockState == CursorLockMode.Locked && !LockAllowed)
                Unlock(); // 게임이 끝나 대기실로 돌아오면 마우스를 돌려준다.
            else if (Cursor.lockState != CursorLockMode.Locked && mouse != null &&
                     mouse.leftButton.wasPressedThisFrame && LockAllowed)
                Lock();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && lockOnStart && LockAllowed) Lock();
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
