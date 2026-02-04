using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyNavigator : MonoBehaviour
{
    NavMeshAgent agent;
    EnemyController enemyController;

    [Header("NavMesh")]
    [SerializeField] float navMeshSampleRadius = 2f;
    [SerializeField] int navMeshAreaMask = NavMesh.AllAreas;
    [SerializeField] float fallbackSpeed = 2f;
    [SerializeField] float fallbackAngularSpeed = 120f;
    [SerializeField] float fallbackAcceleration = 8f;
    [Header("Debug")]
    [SerializeField] bool enableDebugLogs = false;
    [SerializeField] float debugLogInterval = 1f;

    // ✅ 基准参数（未缩放）
    float baseSpeed;
    float baseAngularSpeed;
    float baseAcceleration;
    float baseStoppingDistance;

    public bool HasPath =>
        agent != null &&
        agent.isOnNavMesh &&
        agent.hasPath &&
        !agent.pathPending;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        enemyController = GetComponent<EnemyController>();

        agent.updatePosition = false;
        agent.updateRotation = false;

        ApplyFallbackAgentDefaults();

        // 记录基准
        CaptureBaseFromAgent();
    }

    void OnEnable()
    {
        EnsureAgentOnNavMesh(transform.position);
        if (agent != null) agent.isStopped = false;
    }

    void Update()
    {
        if (agent == null) return;

        float scale = (enemyController != null) ? enemyController.LocalTimeScale : 1f;

        // scale=1 时允许外部逻辑修改 agent 参数（我们同步更新基准）
        if (Mathf.Abs(scale - 1f) < 0.0001f)
        {
            CaptureBaseFromAgent();
            return;
        }

        // scale!=1 时，用“基准 * scale”
        agent.speed = baseSpeed * scale;
        agent.angularSpeed = baseAngularSpeed * scale;
        agent.acceleration = baseAcceleration * scale;

        // stoppingDistance 通常不应缩放（距离是空间量），保持基准
        agent.stoppingDistance = baseStoppingDistance;

        DebugNavigatorState();
    }

    void CaptureBaseFromAgent()
    {
        baseSpeed = agent.speed;
        baseAngularSpeed = agent.angularSpeed;
        baseAcceleration = agent.acceleration;
        baseStoppingDistance = agent.stoppingDistance;
    }

    void ApplyFallbackAgentDefaults()
    {
        if (agent == null) return;

        if (agent.speed <= 0f)
            agent.speed = Mathf.Max(0.01f, fallbackSpeed);

        if (agent.angularSpeed <= 0f)
            agent.angularSpeed = Mathf.Max(0.01f, fallbackAngularSpeed);

        if (agent.acceleration <= 0f)
            agent.acceleration = Mathf.Max(0.01f, fallbackAcceleration);
    }

    /* ================= Public API ================= */

    public void SetTarget(Vector3 worldPos)
    {
        EnsureAgentOnNavMesh(transform.position);

        if (!IsAgentReady())
            return;

        Vector3 targetPos = worldPos;
        if (TryGetNavMeshPosition(worldPos, out var sampled))
            targetPos = sampled;

        agent.isStopped = false;
        agent.SetDestination(targetPos);

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[EnemyNavigator] SetTarget {name} -> {targetPos} " +
                $"(onMesh={agent.isOnNavMesh}, pending={agent.pathPending}, status={agent.pathStatus})",
                this);
        }
    }

    public void Stop()
    {
        if (!IsAgentReady())
            return;

        agent.ResetPath();
    }

    public Vector3 GetMoveDirection()
    {
        if (!HasPath)
            return Vector3.zero;

        Vector3 desired = agent.desiredVelocity;
        desired.y = 0f;

        if (desired.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        return desired.normalized;
    }

    public void SyncPosition(Vector3 worldPos)
    {
        if (agent == null)
            return;

        EnsureAgentOnNavMesh(worldPos);

        if (!agent.isOnNavMesh)
            return;

        agent.nextPosition = worldPos;
    }

    /* ================= Internal ================= */

    bool IsAgentReady()
    {
        return agent != null && agent.isOnNavMesh;
    }

    void EnsureAgentOnNavMesh(Vector3 worldPos)
    {
        if (agent == null || agent.isOnNavMesh)
            return;

        if (TryGetNavMeshPosition(worldPos, out var sampled))
        {
            agent.Warp(sampled);
        }
        else
        {
            agent.Warp(worldPos);
        }
    }

    bool TryGetNavMeshPosition(Vector3 worldPos, out Vector3 sampled)
    {
        if (NavMesh.SamplePosition(worldPos, out var hit, navMeshSampleRadius, navMeshAreaMask))
        {
            sampled = hit.position;
            return true;
        }

        sampled = worldPos;
        return false;
    }

    void DebugNavigatorState()
    {
        if (!enableDebugLogs) return;

        float interval = Mathf.Max(0.1f, debugLogInterval);
        if (Time.time < nextDebugLogTime) return;
        nextDebugLogTime = Time.time + interval;

        Debug.Log(
            $"[EnemyNavigator] {name} onMesh={agent.isOnNavMesh} hasPath={agent.hasPath} " +
            $"pending={agent.pathPending} status={agent.pathStatus} " +
            $"desired={agent.desiredVelocity} nextPos={agent.nextPosition} pos={transform.position}",
            this);
    }

    float nextDebugLogTime;

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!HasPath)
            return;

        Gizmos.color = Color.green;
        Vector3 prev = transform.position;

        foreach (var c in agent.path.corners)
        {
            Gizmos.DrawLine(prev, c);
            prev = c;
        }
    }
#endif
}
