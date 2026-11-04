using UnityEngine;
using UnityEngine.Pool;

namespace GameServer
{
    public class HitEffectPool : MonoBehaviour
    {
        [SerializeField] private PooledEffect prefab;
        [SerializeField] private int defaultCapacity = 8;
        [SerializeField] private int maxSize = 32;

        private ObjectPool<PooledEffect> pool;

        private void Awake()
        {
            pool = new ObjectPool<PooledEffect>(
                () => { PooledEffect e = Instantiate(prefab, transform); e.Bind(this); return e; },
                e => e.gameObject.SetActive(true),
                e => e.gameObject.SetActive(false),
                e => Destroy(e.gameObject),
                true, defaultCapacity, maxSize);
        }

        public void Play(Vector3 position, Vector3 normal)
        {
            PooledEffect effect = pool.Get();
            Quaternion rotation = normal.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
            effect.transform.SetPositionAndRotation(position, rotation);
            effect.Play();
        }

        internal void Release(PooledEffect effect) => pool.Release(effect);
    }
}
