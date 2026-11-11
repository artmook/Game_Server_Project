using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace GameServer
{
    public class CharacterPool : MonoBehaviour
    {
        [SerializeField] private Character prefab;
        [SerializeField] private int defaultCapacity = 30;
        [SerializeField] private int maxSize = 50;

        private ObjectPool<Character> pool;

        private void Awake()
        {
            pool = new ObjectPool<Character>(
                createFunc: Create,
                actionOnGet: c => c.gameObject.SetActive(true),
                actionOnRelease: c => c.gameObject.SetActive(false),
                actionOnDestroy: c => Destroy(c.gameObject),
                collectionCheck: true,
                defaultCapacity: defaultCapacity,
                maxSize: maxSize);
        }

        private Character Create()
        {
            Character instance = Instantiate(prefab, transform);
            instance.gameObject.SetActive(false);
            return instance;
        }

        public void Prewarm(int count)
        {
            var temp = new List<Character>(count);
            for (int i = 0; i < count; i++) temp.Add(pool.Get());
            foreach (Character c in temp) pool.Release(c);
        }

        public Character Spawn(Vector3 position, Quaternion rotation)
        {
            Character character = pool.Get();
            character.Spawn(position, rotation);
            return character;
        }

        public void Despawn(Character character) => pool.Release(character);
    }
}
