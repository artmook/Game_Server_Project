using UnityEngine;

namespace GameServer
{
    public struct CharacterCommand
    {
        public Vector3 MoveDirection;
        public bool Run;
        public bool Jump;
        public bool Attack;
        public int ActionIndex;       // 1~4 행동하기 번호
        public bool HasAim;
        public Ray AimRay;            // 공격 판정 Ray
    }
}