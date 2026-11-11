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
        // 네트워크 게임: 시드로 만든 플레이어 시작 위치. 모든 클라이언트에서 같다.
        private readonly List<Vector3> playerSpawnPositions = new();
        private const float PlayerSpawnSpacing = 5f;

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

        public void StartGame() => StartGameInternal(null);

        // 서버 STARTED의 시드로 시작한다. 진행 중이던 게임은 먼저 정리한다.
        public bool StartNetworkGame(int seed)
        {
            if (IsPlaying) End_Game();
            return StartGameInternal(seed);
        }

        private bool StartGameInternal(int? seed)
        {
            if (IsPlaying) return false;
            if (!NavMeshRandom.Build()) return false;     // NavMesh 데이터 캐싱

            // 풀 생성(Awake의 난수 사용)은 시드 적용 전에 끝낸다. PC마다 풀 상태가 달라도 이후 난수 순서가 같아진다.
            aiPool.Prewarm(aiCount);
            playerSpawnPositions.Clear();
            if (seed.HasValue)
            {
                UnityEngine.Random.InitState(seed.Value);
                BuildPlayerSpawnPositions();
            }
            for (int i = 0; i < aiCount; i++) Spawn_AI(seed.HasValue);

            IsPlaying = true;
            GameStarted?.Invoke();
            return true;
        }

        // 서버가 준 스폰 번호의 시작 위치. spawnPoints가 있으면 그 위치, 없으면 시드로 만든 위치이다.
        public bool TryGetPlayerSpawn(int index, out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (playerSpawnPositions.Count == 0)
            {
                position = localPlayer != null ? localPlayer.transform.position : Vector3.zero;
                return false;
            }
            int i = ((index % playerSpawnPositions.Count) + playerSpawnPositions.Count) % playerSpawnPositions.Count;
            position = playerSpawnPositions[i];
            if (HasSpawnPoints() && spawnPoints[i % spawnPoints.Length] != null)
                rotation = spawnPoints[i % spawnPoints.Length].rotation;
            return true;
        }

        private bool HasSpawnPoints() => spawnPoints != null && spawnPoints.Length > 0;

        private void BuildPlayerSpawnPositions()
        {
            for (int i = 0; i < maxPlayers; i++)
            {
                if (HasSpawnPoints() && spawnPoints[i % spawnPoints.Length] != null)
                {
                    playerSpawnPositions.Add(spawnPoints[i % spawnPoints.Length].position);
                    continue;
                }

                Vector3 chosen = Vector3.zero;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    if (!NavMeshRandom.TryGetRandomPoint(out Vector3 candidate)) break;
                    chosen = candidate;
                    bool tooClose = false;
                    foreach (Vector3 other in playerSpawnPositions)
                    {
                        if (Vector3.Distance(candidate, other) < PlayerSpawnSpacing) { tooClose = true; break; }
                    }
                    if (!tooClose) break;
                }
                playerSpawnPositions.Add(chosen);
            }
        }

        public void End_Game()
        {
            // 죽은 AI는 NavAIInputSource가 이미 풀에 반납했을 수 있다. 두 번 반납하면 ObjectPool이 예외를 낸다.
            foreach (Character ai in activeAI)
                if (ai != null && ai.gameObject.activeSelf) aiPool.Despawn(ai);
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

        // networked: 로컬 플레이어 위치 대신 모든 플레이어 시작 위치를 피한다(PC마다 같은 결과가 나오게).
        private void Spawn_AI(bool networked)
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
                    if (!tooClose && networked)
                    {
                        foreach (Vector3 spawn in playerSpawnPositions)
                        {
                            if (Vector3.Distance(candidate, spawn) < 3f) { tooClose = true; break; }
                        }
                    }
                    else if (!tooClose && localPlayer != null && Vector3.Distance(candidate, localPlayer.transform.position) < 3f)
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
