using GameServer;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;

namespace GameServer
{
    [RequireComponent(typeof(NavMeshAgent), typeof(CharacterMove))]
    public class NavAIInputSource : MonoBehaviour, ICommandSource
    {
        [Header("이동 설정")]
        [SerializeField] private float wanderRadius = 15f;
        [SerializeField] private float maxMoveTime = 15f;

        [Header("행동 확률 (공격은 이동 중에 확률적으로 실행)")]
        [SerializeField, Range(0f, 1f)] private float attackChance = 0.05f;
        [SerializeField, Range(0f, 1f)] private float runChance = 0.3f;
        [SerializeField] private float jumpChancePerSecond = 0.1f;

        [Header("멈춤 (사람도 멈춰서 미션을 할 수 있게)")]
        [Tooltip("목적지에 닿거나 이동 시간이 끝났을 때 잠깐 서 있을 확률")]
        [SerializeField, Range(0f, 1f)] private float idleChance = 0.3f;
        [SerializeField] private Vector2 idleTime = new Vector2(1f, 4f);

        [Header("Smoothing / Stuck")]
        [SerializeField] private float directionSmooth = 1f;   // 방향 전환 부드러움
        [SerializeField] private float stuckTime = 1.0f;       // 못 움직이고 막힌 것으로 판정할 시간
        [SerializeField] private float stuckSpeed = 0.1f;      // 이거보다 느리면 끼인 것임

        private Vector3 smoothedDir;
        private float stuckTimer;
        private Vector3 lastPos;

        private NavMeshAgent agent;
        private CharacterMove motor;
        private CharacterHealth health;

        private float timer;
        private float idleTimer;
        private bool running;
        private bool pendingAttack;
        private bool warnedOffMesh;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            motor = GetComponent<CharacterMove>();
            health = GetComponent<CharacterHealth>();

            if (agent != null)
            {
                agent.updatePosition = false;
                agent.updateRotation = false;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.GoodQualityObstacleAvoidance;
                agent.radius = 0.35f;
                agent.height = 1.8f;
                agent.avoidancePriority = Random.Range(10, 99);
            }
        }

        private void OnEnable() { if (health != null) health.Died += HandleDied; }

        private void OnDisable()
        {
            if (health != null) health.Died -= HandleDied;
            if (agent != null && agent.enabled) agent.enabled = false;
        }

        public void ResetSource()
        {
            StopAllCoroutines();

            pendingAttack = false;
            warnedOffMesh = false;
            idleTimer = 0f;

            if (EnsureAgent())
            {
                agent.isStopped = false;
                agent.avoidancePriority = Random.Range(30, 70);
            }

            PickNewDestination();
        }

        private bool EnsureAgent()
        {
            if (agent.enabled && agent.isOnNavMesh) return true;

            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.GoodQualityObstacleAvoidance;
            agent.avoidancePriority = Random.Range(30, 70);

            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 20f, NavMesh.AllAreas))
            {
                if (!warnedOffMesh)
                {
                    warnedOffMesh = true;
                }
                return false;
            }

            agent.enabled = true;
            agent.Warp(hit.position);
            agent.speed = motor.WalkSpeed;
            warnedOffMesh = false;
            return agent.isOnNavMesh;
        }

        public CharacterCommand ReadCommand()
        {
            if (health != null && !health.IsAlive)
            {
                return new CharacterCommand();
            }

            var command = new CharacterCommand();

            if (!EnsureAgent())
            {
                command.MoveDirection = transform.forward;
                return command;
            }

            float dt = Time.deltaTime;

            agent.nextPosition = transform.position;

            // 제자리에 서 있는 중: 시간이 끝나면 새 목적지로 출발한다.
            if (idleTimer > 0f)
            {
                idleTimer -= dt;
                smoothedDir = Vector3.zero;
                lastPos = transform.position;
                if (idleTimer <= 0f) PickNewDestination();
                return command;
            }

            timer -= dt;
            bool legDone = HasArrived() || timer <= 0f;
            if (legDone && Random.value < idleChance)
            {
                idleTimer = Random.Range(idleTime.x, idleTime.y);
                agent.ResetPath();
                return command;
            }

            if (legDone || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                PickNewDestination();
            }

            Vector3 target = Vector3.zero;

            if (!agent.pathPending)
            {
                Vector3 desired = agent.desiredVelocity;
                desired.y = 0f;

                if (desired.sqrMagnitude < 0.01f)
                {
                    desired = agent.steeringTarget - transform.position;
                    desired.y = 0f;
                }

                if (desired.sqrMagnitude > 0.01f)
                    target = desired.normalized;
            }

            smoothedDir = Vector3.Lerp(smoothedDir, target, directionSmooth * dt);
            if (target == Vector3.zero && smoothedDir.sqrMagnitude < 0.01f)
                smoothedDir = Vector3.zero;

            if (smoothedDir.sqrMagnitude > 0.01f)
            {
                command.MoveDirection = smoothedDir.normalized;
                command.Run = running;

                bool blocked = Physics.SphereCast(transform.position + Vector3.up * 0.5f, 0.3f,
                                          command.MoveDirection, out _, 1.0f,
                                          ~0, QueryTriggerInteraction.Ignore);
                command.Jump = !blocked && Random.value < jumpChancePerSecond * dt;
            }

            Vector3 delta = transform.position - lastPos;
            delta.y = 0f;
            lastPos = transform.position;
            float speed = dt > 0f ? delta.magnitude / dt : 0f;

            if (target != Vector3.zero && speed < stuckSpeed)
            {
                stuckTimer += dt;
                if (stuckTimer > stuckTime)
                {
                    stuckTimer = 0f;
                    PickNewDestination();
                }
            }
            else stuckTimer = 0f;

            ProcessRandomAttack();

            if (pendingAttack) { command.Attack = true; pendingAttack = false; }

            return command;
        }

        private bool HasArrived() =>
            !agent.pathPending && agent.hasPath && agent.remainingDistance <= agent.stoppingDistance + 0.2f;

        private void PickNewDestination()
        {
            // 맵 전체에서 넓게 랜덤 위치
            if (NavMeshRandom.TryGetRandomPoint(out Vector3 globalDest))
            {
                if (agent.SetDestination(globalDest))
                {
                    SetupMoveState();
                    return;
                }
            }

            // 전체에서 실패하면 현재 위치 근처
            if (NavMeshRandom.TryGetRandomPointNear(transform.position, wanderRadius, out Vector3 dest))
            {
                if (agent.SetDestination(dest))
                {
                    SetupMoveState();
                    return;
                }
            }

            // 마지막
            Vector3 fallbackDest = transform.position + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f));
            if (NavMesh.SamplePosition(fallbackDest, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
            SetupMoveState();
        }

        private void SetupMoveState()
        {
            running = Random.value < runChance;
            agent.speed = running ? motor.RunSpeed : motor.WalkSpeed;
            timer = maxMoveTime;
        }

        private void ProcessRandomAttack()
        {
            if (pendingAttack) return;

            if (Random.value < attackChance * Time.deltaTime)
                pendingAttack = true;
        }

        private void HandleDied(HitInfo _)
        {
            pendingAttack = false;

            if (agent != null && agent.enabled)
            {
                if (agent.isOnNavMesh)
                {
                    agent.ResetPath();
                    agent.isStopped = true;
                }
                agent.enabled = false;
            }

            StartCoroutine(DespawnAfterDelay(5f));
        }

        private IEnumerator DespawnAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);

            CharacterPool pool = FindFirstObjectByType<CharacterPool>();
            Character characterComponent = GetComponent<Character>();

            if (pool != null && characterComponent != null)
            {
                pool.Despawn(characterComponent);
            }
            else
            {
                Debug.LogWarning($"{name}: CharacterPool 또는 Character 컴포넌트를 찾지 못했습니다!", this);
            }
        }
    }
}