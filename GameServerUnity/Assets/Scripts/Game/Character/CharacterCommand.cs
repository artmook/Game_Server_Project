using UnityEngine;

namespace GameServer
{
    public struct CharacterCommand
    {
        public Vector3 MoveDirection;
        public bool Run;
        public bool Jump;
        public bool Attack;
        public bool HasAim;
        public Ray AimRay;            // 공격 판정 Ray
    }
}