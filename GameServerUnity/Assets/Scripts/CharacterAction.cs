using GameServer;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameServer
{
    [Serializable]
    public class ActionSlot
    {
        public string displayName = "Action";
        public string animatorTrigger = "Action1";
        public bool enabled;
        public float duration = 2f;        // 지속 시간
        public bool lockMovement = true;   // 행동 중 이동x
    }

    public class CharacterAction : MonoBehaviour
    {
        public const int SlotCount = 4;

        [SerializeField]
        private ActionSlot[] slots =
        {
            new ActionSlot { displayName = "행동1", animatorTrigger = "Action1", enabled = true  },
            new ActionSlot { displayName = "행동2", animatorTrigger = "Action2", enabled = false },
            new ActionSlot { displayName = "행동3", animatorTrigger = "Action3", enabled = false },
            new ActionSlot { displayName = "행동4", animatorTrigger = "Action4", enabled = false },
        };

        private readonly List<int> enabledBuffer = new(SlotCount);
        private float busyTimer;
        private ActionSlot current;

        public bool IsBusy => busyTimer > 0f;
        public bool LocksMovement => IsBusy && current != null && current.lockMovement;
        public event Action<int> ActionStarted;

        public bool IsSlotEnabled(int index) =>
            index >= 1 && index <= slots.Length &&
            slots[index - 1].enabled &&
            !string.IsNullOrEmpty(slots[index - 1].animatorTrigger);

        public float GetDuration(int index) => IsSlotEnabled(index) ? slots[index - 1].duration : 0f;

        public bool TryPerform(int index, CharacterAnimator characterAnimator)
        {
            if (IsBusy || !IsSlotEnabled(index)) return false;

            current = slots[index - 1];
            busyTimer = current.duration;
            characterAnimator.PlayAction(current.animatorTrigger);
            ActionStarted?.Invoke(index);
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (busyTimer > 0f) busyTimer -= deltaTime;
        }

        public void Cancel() { busyTimer = 0f; current = null; }

        // AI 행동하기(랜덤)
        public int GetRandomEnabledIndex()
        {
            enabledBuffer.Clear();
            for (int i = 1; i <= slots.Length; i++)
                if (IsSlotEnabled(i)) enabledBuffer.Add(i);
            return enabledBuffer.Count == 0 ? 0 : enabledBuffer[UnityEngine.Random.Range(0, enabledBuffer.Count)];
        }
    }
}
