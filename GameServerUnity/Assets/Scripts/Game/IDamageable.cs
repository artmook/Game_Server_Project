using UnityEngine;

namespace GameServer
{
    public readonly struct HitInfo
    {
        public readonly GameObject Attacker;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;

        public HitInfo(GameObject attacker, Vector3 point, Vector3 normal)
        {
            Attacker = attacker; Point = point; Normal = normal;
        }
    }

    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeHit(in HitInfo hit);
    }
}
