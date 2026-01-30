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

        // 你的原始初始化
        agent.speed = 0f;
        agent.angularSpeed = 0f;
        agent.acceleration = 0f;
        agent.stoppingDistance = 0f;

        // 记录基准
        CaptureBaseFromAgent();
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
    }

    void CaptureBaseFromAgent()
    {
        baseSpeed = agent.speed;
        baseAngularSpeed = agent.angularSpeed;
        baseAcceleration = agent.acceleration;
        baseStoppingDistance = agent.stoppingDistance;
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

        agent.SetDestination(targetPos);
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
