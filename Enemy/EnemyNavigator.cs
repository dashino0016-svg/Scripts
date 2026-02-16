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

    [Header("Follow Owner")]
    [Tooltip("开启后，NavMeshAgent 每帧都会强同步到角色 Transform，避免高低落差后与角色脱节。")]
    [SerializeField] bool alwaysSyncAgentToTransform = true;

    [Tooltip("当 Agent 与角色位置偏差超过该值时，使用 Warp 强制回贴（米）。")]
    [SerializeField] float hardSnapDistance = 1.5f;

    [Tooltip("脱离 NavMesh 时用于重挂的扩展采样半径（米）。")]
    [SerializeField] float navMeshReattachRadius = 12f;

    // ✅ 基准参数（未缩放）
    float baseSpeed;
    float baseAngularSpeed;
    float baseAcceleration;
    float baseStoppingDistance;

    // ⚠️ 注意：外部逻辑（Combat/NotCombat/LostTarget）会频繁 SetDestination
    // NavMeshAgent 在刚 SetDestination 时常出现 pathPending=true
    // 如果这里把 pathPending 当成“没路”，会导致永远拿不到方向 -> 外部 fallback 直线走
    public bool HasPath =>
        agent != null &&
        agent.isOnNavMesh &&
        (agent.hasPath || agent.pathPending);

    // 仅用于 Gizmos 绘制：必须有已计算完成的路径
    bool HasValidDrawPath =>
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

        // 记录基准
        CaptureBaseFromAgent();
    }

    void OnEnable()
    {
        EnsureAgentOnNavMesh(transform.position);
    }

    void Update()
    {
        if (agent == null) return;

        float scale = (enemyController != null) ? enemyController.LocalTimeScale : 1f;

        // scale=1 时允许外部逻辑修改 agent 参数（我们同步更新基准）
        if (Mathf.Abs(scale - 1f) < 0.0001f)
        {
            CaptureBaseFromAgent();
        }
        else
        {
            // scale!=1 时，用“基准 * scale”
            agent.speed = baseSpeed * scale;
            agent.angularSpeed = baseAngularSpeed * scale;
            agent.acceleration = baseAcceleration * scale;

            // stoppingDistance 通常不应缩放（距离是空间量），保持基准
            agent.stoppingDistance = baseStoppingDistance;
        }

        if (alwaysSyncAgentToTransform)
            SyncPosition(transform.position);
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
        if (agent == null || !agent.isOnNavMesh)
            return Vector3.zero;

        // 1) 首选 desiredVelocity（正常情况下最稳定）
        Vector3 desired = agent.desiredVelocity;
        desired.y = 0f;

        if (desired.sqrMagnitude >= 0.0001f)
            return desired.normalized;

        // 2) desiredVelocity 可能在刚 SetDestination / pathPending 时为 0，尝试 steeringTarget
        Vector3 steer = agent.steeringTarget - transform.position;
        steer.y = 0f;
        if (steer.sqrMagnitude >= 0.0001f)
            return steer.normalized;

        // 3) 兜底：如果已经有 path，尝试走向下一段 corner
        if (agent.hasPath)
        {
            var corners = agent.path.corners;
            if (corners != null && corners.Length > 1)
            {
                Vector3 toCorner = corners[1] - transform.position;
                toCorner.y = 0f;
                if (toCorner.sqrMagnitude >= 0.0001f)
                    return toCorner.normalized;
            }
        }

        return Vector3.zero;
    }

    public void SyncPosition(Vector3 worldPos)
    {
        if (agent == null)
            return;

        EnsureAgentOnNavMesh(worldPos);

        if (!agent.isOnNavMesh)
            return;

        Vector3 flat = worldPos;
        flat.y = agent.nextPosition.y;

        float planarDelta = (agent.nextPosition - flat).sqrMagnitude;
        if (planarDelta > hardSnapDistance * hardSnapDistance)
        {
            agent.Warp(worldPos);
            return;
        }

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
            return;
        }

        // 高落差场景：首次半径命不中时，用更大半径再尝试重挂
        float reattach = Mathf.Max(navMeshSampleRadius, navMeshReattachRadius);
        if (reattach > navMeshSampleRadius &&
            NavMesh.SamplePosition(worldPos, out var farHit, reattach, navMeshAreaMask))
        {
            agent.Warp(farHit.position);
            return;
        }

        // 最后兜底：仍然尝试贴到当前点（若不可用则保持未上网格）
        agent.Warp(worldPos);
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
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        if (!HasValidDrawPath)
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
