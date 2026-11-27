/*
[ 코드 설명 ]

MissionDefinition / MissionTracker

1. MissionDefinition: 미션 한 개. 화면 글자(text), 종류(goal), 목표값(amount)을 가진다.
   인스펙터에서 목록을 고치면 룰렛과 판정이 같이 바뀐다.
2. MissionTracker: 내 캐릭터를 보고 미션 진행도를 센다. "Start!" 이후, 살아 있는 동안만 센다.
   - Jump            : 점프 횟수 (CharacterMove.Jumped)
   - RunSeconds      : 달린 시간 합계
   - StandStillSeconds: 이어서 가만히 있은 시간 (움직이거나 공중이면 처음부터)
   - WalkOnlySeconds : 걸은 시간 합계, 달리면 처음부터
   - ConsecutiveJumps: 끊지 않고 이어서 한 점프 수. 착지 후 ChainWindow 안에 다시 뛰어야 이어지고,
                       늦으면 0으로 돌아간다(도망치며 띄엄띄엄 뛴 점프로는 끝나지 않게).
3. 진행도가 바뀌면 ProgressChanged, 목표에 닿으면 Cleared를 한 번 알린다.
*/

using System;
using UnityEngine;

namespace GameServer
{
    // 숫자는 씬에 저장되는 값이라 고정한다. (1번은 지운 '공격' 미션 자리라 비워 둔다.)
    public enum MissionGoal { Jump = 0, RunSeconds = 2, StandStillSeconds = 3, WalkOnlySeconds = 4, ConsecutiveJumps = 5 }

    [Serializable]
    public class MissionDefinition
    {
        public string text = "점프 10번 하기";
        public MissionGoal goal = MissionGoal.Jump;
        [Min(1f)] public float amount = 10f;

        public MissionDefinition() { }
        public MissionDefinition(string text, MissionGoal goal, float amount)
        {
            this.text = text;
            this.goal = goal;
            this.amount = amount;
        }

        public bool IsTimed => goal == MissionGoal.RunSeconds || goal == MissionGoal.StandStillSeconds ||
                               goal == MissionGoal.WalkOnlySeconds;
    }

    public sealed class MissionTracker
    {
        private const float MovingSpeed = 0.15f;   // 이보다 느리면 멈춰 있는 것으로 본다.
        private const float ChainWindow = 0.5f;    // 연속 점프: 착지 후 이 시간 안에 다시 뛰어야 이어진다.
        private float chainTime;                   // 연속 점프: 마지막 점프·착지 시각
        private bool wasGrounded = true;

        private MissionDefinition mission;
        private CharacterMove motor;
        private CharacterHealth health;
        private float progress;
        private int lastShown = -1;

        public bool IsTracking { get; private set; }
        public bool IsCleared { get; private set; }
        public float Progress => progress;
        public MissionDefinition Mission => mission;

        public event Action<float, float> ProgressChanged;   // 현재, 목표
        public event Action Cleared;

        public void Begin(MissionDefinition target, Character player)
        {
            Stop();
            if (target == null || player == null) return;

            mission = target;
            motor = player.GetComponent<CharacterMove>();
            health = player.GetComponent<CharacterHealth>();
            progress = 0f;
            lastShown = -1;
            chainTime = Time.time;
            wasGrounded = true;
            IsCleared = false;
            IsTracking = true;

            if (motor != null) motor.Jumped += HandleJumped;
            Report();
        }

        public void Stop()
        {
            if (motor != null) motor.Jumped -= HandleJumped;
            IsTracking = false;
        }

        // MissionIntro.Update에서 매 프레임 호출한다.
        public void Tick(float dt)
        {
            if (!CanCount() || motor == null) return;

            float speed = motor.PlanarSpeed;
            bool moving = speed > MovingSpeed;
            bool running = speed > (motor.WalkSpeed + motor.RunSpeed) * 0.5f;

            switch (mission.goal)
            {
                case MissionGoal.RunSeconds:
                    if (running) Add(dt);
                    break;
                case MissionGoal.StandStillSeconds:
                    if (!moving && motor.IsGrounded) Add(dt); else ResetProgress();
                    break;
                case MissionGoal.WalkOnlySeconds:
                    if (running) ResetProgress(); else if (moving) Add(dt);
                    break;
                case MissionGoal.ConsecutiveJumps:
                    if (motor.IsGrounded && !wasGrounded) chainTime = Time.time;   // 착지한 순간부터 다음 점프를 기다린다.
                    wasGrounded = motor.IsGrounded;
                    if (motor.IsGrounded && Time.time - chainTime > ChainWindow) ResetProgress();
                    break;
            }
        }

        private void HandleJumped()
        {
            if (!CanCount()) return;
            if (mission.goal == MissionGoal.Jump) Add(1f);
            else if (mission.goal == MissionGoal.ConsecutiveJumps)
            {
                // 첫 점프이거나 착지 후 ChainWindow 안이면 이어진다. Tick이 늦은 점프를 이미 0으로 돌려놓는다.
                chainTime = Time.time;   // 뛰는 순간 아직 땅으로 판정되는 프레임에 끊기지 않게
                Add(1f);
            }
        }

        private bool CanCount() =>
            IsTracking && !IsCleared && !GameFlow.ControlsLocked && (health == null || health.IsAlive);

        private void Add(float amount)
        {
            progress = Mathf.Min(mission.amount, progress + amount);
            Report();
            if (progress >= mission.amount)
            {
                IsCleared = true;
                Stop();
                Cleared?.Invoke();
            }
        }

        private void ResetProgress()
        {
            if (progress <= 0f) return;
            progress = 0f;
            Report();
        }

        // 화면 숫자가 바뀔 때만 알린다(초 단위 미션이 매 프레임 글자를 새로 만들지 않게).
        private void Report()
        {
            int shown = Mathf.FloorToInt(progress);
            if (shown == lastShown && progress < mission.amount) return;
            lastShown = shown;
            ProgressChanged?.Invoke(progress, mission.amount);
        }
    }
}
