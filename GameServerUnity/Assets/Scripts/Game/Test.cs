using UnityEngine;

namespace GameServer
{
    [DefaultExecutionOrder(1000)]
    public class Test : MonoBehaviour
    {
        [SerializeField] private bool offlineTest = true;

        private void Start()
        {

#if !UNITY_EDITOR
    offlineTest = false;   // ºôµå¿¡¼­´Â Ç×»ó ²¨Áü
#endif
            if (!offlineTest) return;

            CursorController.CanLock = () => true;
            CursorController.Lock();

            foreach (var combat in FindObjectsByType<CharacterCombat>(FindObjectsSortMode.None))
            {
                GameObject attacker = combat.gameObject;
                combat.TargetHit += target =>
                {
                    if (target is CharacterHealth h && h.ServerAuthoritative)
                        h.ApplyServerDeath(attacker, true);
                };
            }
            Debug.Log("Test: ¼­¹ö KILL ÄÑÁø »óÅÂ");
        }

        private void LateUpdate()
        {
            if (offlineTest && CursorController.CanLock != null && !CursorController.CanLock())
                CursorController.CanLock = () => true;
        }


    }
}
