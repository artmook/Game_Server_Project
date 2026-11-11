using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameServer
{
    public class SurvivorCounter : MonoBehaviour
    {
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private bool countSceneCharactersOnStart = true; // 오프라인 테스트용

        private readonly HashSet<Character> alive = new();
        private readonly HashSet<Character> tracked = new();
        private readonly Dictionary<Character, string> names = new();

        private bool useServerCount;
        private int serverRemaining, serverMax;

        public int MaxPlayers => useServerCount ? serverMax : tracked.Count;
        public int RemainingPlayers => useServerCount ? serverRemaining : alive.Count;

        public event Action<int, int> RemainingPlayersChanged;
        public event Action<string> CharacterDied;

        private void Start()
        {
            if (countSceneCharactersOnStart)
                foreach (var c in FindObjectsByType<Character>(FindObjectsSortMode.None))
                    Register(c);
            Notify();
        }

        // 네트워크로 캐릭터가 생성될 때 호출 (닉네임도 같이 넘기기)
        public void Register(Character c, string displayName = null)
        {
            if (c == null || !tracked.Add(c)) return;
            if (displayName != null) names[c] = displayName;
            if (c.IsAlive) alive.Add(c);
            c.Died += HandleDied;
            Notify();
        }

        // 접속 종료나 오브젝트 제거 시 호출
        public void Unregister(Character c)
        {
            if (c == null || !tracked.Remove(c)) return;
            alive.Remove(c);
            names.Remove(c);
            c.Died -= HandleDied;
            Notify();
        }

        // 서버가 생존자 수를 보내 주면 호출 → 이후로는 서버 값이 기준
        public void SetFromServer(int remaining, int max)
        {
            useServerCount = true;
            serverRemaining = remaining;
            serverMax = max;
            Notify();
        }

        public void MarkAlive(Character c)
        {
            if (tracked.Contains(c) && alive.Add(c)) Notify();
        }

        private void HandleDied(Character c)
        {
            if (!alive.Remove(c)) return;
            CharacterDied?.Invoke(GetDisplayName(c));
            Notify();
        }

        public string GetDisplayName(Character c)
        {
            if (!c.CompareTag(playerTag)) return "AI";
            return names.TryGetValue(c, out var n) ? n : c.gameObject.name.Replace("(Clone)", "");
        }

        private void OnDestroy()
        {
            foreach (var c in tracked) if (c != null) c.Died -= HandleDied;
        }

        private void Notify() => RemainingPlayersChanged?.Invoke(RemainingPlayers, MaxPlayers);
    }
}
