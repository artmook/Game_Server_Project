using GameServer;
using UnityEngine;

namespace GameServer
{
    [RequireComponent(typeof(ParticleSystem))]
    public class PooledEffect : MonoBehaviour
    {
        private ParticleSystem particles;
        private HitEffectPool owner;

        private void Awake()
        {
            particles = GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.stopAction = ParticleSystemStopAction.Callback;
            main.playOnAwake = false;
        }

        public void Bind(HitEffectPool pool) => owner = pool;

        public void Play()
        {
            particles.Clear(true);
            particles.Play(true);
        }

        private void OnParticleSystemStopped()
        {
            if (owner != null) owner.Release(this);
        }
    }
}
