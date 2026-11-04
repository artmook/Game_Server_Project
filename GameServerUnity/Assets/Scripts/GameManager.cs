using GameServer;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameServer
{
    public class GameManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CharacterPool aiPool;
        [SerializeField] private Character localPlayer;

        [Header("Spawn Settings")]
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private float spawnScatterRadius = 5f;

        [Header("Settings")]
        [SerializeField] private int aiCount = 30;
        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private bool startOnPlay = true;

        private readonly List<Character> activeAI = new();
        private readonly List<Character> players = new();

        public bool IsPlaying { get; private set; }
        public int MaxPlayers => maxPlayers;
        public int RemainingPlayers { get; private set; }

        public event Action GameStarted;
        public event Action<int, int> RemainingPlayersChanged;

        private void Start()
        {
            if (localPlayer != null) Register_Player(localPlayer);
            if (startOnPlay) StartGame();
        }

        public void StartGame()
        {
            if (IsPlaying) return;
            if (!NavMeshRandom.Build()) return;     // NavMesh 데이터 캐싱

            aiPool.Prewarm(aiCount);
            for (int i = 0; i < aiCount; i++) Spawn_AI();

            IsPlaying = true;
            GameStarted?.Invoke();
        }

        public void End_Game()
        {
            foreach (Character ai in activeAI) aiPool.Despawn(ai);
            activeAI.Clear();
            IsPlaying = false;
        }

        public void Register_Player(Character player)
        {
            if (player == null || players.Contains(player)) return;
            players.Add(player);
            player.Died += Handle_Player_Died;
            RefreshRemainingPlayers();
        }

        public void Unregister_Player(Character player)
        {
            if (!players.Remove(player)) return;
            player.Died -= Handle_Player_Died;
            RefreshRemainingPlayers();
        }

        private void Spawn_AI()
        {
            Vector3 position = Vector3.zero;
            bool validPosition = false;
            int maxAttempts = 30;

            for (int i = 0; i < maxAttempts; i++)
            {
                Vector3 candidate = Vector3.zero;
                bool foundPoint = false;

                // 스폰 위치 등록되어 있으면 랜덤 선택
                if (spawnPoints != null && spawnPoints.Length > 0)
                {
                    Transform randomSpawnPoint = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Length)];
                    if (randomSpawnPoint != null)
                    {
                        foundPoint = NavMeshRandom.TryGetRandomPointNear(randomSpawnPoint.position, spawnScatterRadius, out candidate);
                    }
                }

                // 스폰 위치 없거나 못찾으면 맵 전체 랜덤 선택
                if (!foundPoint)
                {
                    foundPoint = NavMeshRandom.TryGetRandomPoint(out candidate);
                }

                if (foundPoint)
                {
                    bool tooClose = false;

                    // 다른 AI 사이 최소 거리 확인
                    foreach (var existingAI in activeAI)
                    {
                        if (existingAI != null && Vector3.Distance(candidate, existingAI.transform.position) < 3f)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    // 플레이어 사이 거리 확인
                    if (!tooClose && localPlayer != null && Vector3.Distance(candidate, localPlayer.transform.position) < 3f)
                    {
                        tooClose = true;
                    }

                    if (!tooClose)
                    {
                        position = candidate;
                        validPosition = true;
                        break;
                    }
                }
            }

            // 위치를 못 찾았으면
            if (!validPosition)
            {
                if (spawnPoints != null && spawnPoints.Length > 0 && spawnPoints[0] != null)
                    position = spawnPoints[0].position;
                else if (!NavMeshRandom.TryGetRandomPoint(out position))
                    return;
            }

            Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            Character ai = aiPool.Spawn(position, rotation);
            activeAI.Add(ai);
        }

        private void Handle_Player_Died(Character _) => RefreshRemainingPlayers();

        private void RefreshRemainingPlayers()
        {
            int alive = 0;
            foreach (Character p in players) if (p.IsAlive) alive++;
            RemainingPlayers = alive;
            RemainingPlayersChanged?.Invoke(RemainingPlayers, maxPlayers);
        }

        private void OnDestroy()
        {
            foreach (Character p in players) if (p != null) p.Died -= Handle_Player_Died;
        }
    }
}