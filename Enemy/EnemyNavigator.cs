using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyNavigator : MonoBehaviour
{
    [Header("Repath")]
    [SerializeField] float repathInterval = 0.2f;
    [SerializeField] float destinationChangeThreshold = 0.25f;

    [Header("Destination Sampling")]
    [SerializeField] float sampleRadius = 2f;

    [Header("Sync")]
    [SerializeField] float warpThreshold = 1.0f;

    NavMeshAgent agent;
    EnemyController enemyController;

    float baseSpeed;
    float baseAngularSpeed;
    float baseAcceleration;
    float baseStoppingDistance;

    float nextRepathTime;
    Vector3 lastDestination;

    public bool HasPath =>
        agent != null &&
        agent.isOnNavMesh &&
        agent.hasPath &&
        !agent.pathPending;

    public bool HasCompletePath =>
        HasPath && agent.pathStatus == NavMeshPathStatus.PathComplete;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        enemyController = GetComponent<EnemyController>();

        // 我们自己移动（CharacterController），NavMeshAgent 只负责算路
        agent.updatePosition = false;
        agent.updateRotation = false;

        // ✅ 不要把 speed/acceleration 设 0，否则 desiredVelocity 永远是 0
        CaptureBaseFromAgent();
        lastDestination = transform.position;
    }

    void Update()
    {
        if (agent == null) return;

        float scale = (enemyController != null) ? enemyController.LocalTimeScale : 1f;

        agent.speed = baseSpeed * scale;
        agent.angularSpeed = baseAngularSpeed * scale;
        agent.acceleration = baseAcceleration * scale;
        agent.stoppingDistance = baseStoppingDistance;
    }

    void LateUpdate()
    {
        if (agent == null) return;

        // ✅ 把 agent 的“内部位置”拉回真实位置，避免走散
        Vector3 pos = transform.position;

        if (!agent.isOnNavMesh)
        {
            agent.Warp(pos);
            return;
        }

        float sqr = (agent.nextPosition - pos).sqrMagnitude;
        if (sqr > warpThreshold * warpThreshold)
            agent.Warp(pos);
        else
            agent.nextPosition = pos;
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
        if (!IsAgentReady())
            return;

        // 降低 SetDestination 频率（性能 + 抖动）
        if (Time.time < nextRepathTime)
            return;

        // 目标点变化不大就不重算
        if ((worldPos - lastDestination).sqrMagnitude < destinationChangeThreshold * destinationChangeThreshold)
            return;

        nextRepathTime = Time.time + repathInterval;
        lastDestination = worldPos;

        // ✅ 把目标投影到 NavMesh 上（避免目标点在空中/不可走面）
        if (NavMesh.SamplePosition(worldPos, out var hit, sampleRadius, agent.areaMask))
            worldPos = hit.position;

        agent.SetDestination(worldPos);
    }

    public void Stop()
    {
        if (!IsAgentReady())
            return;

        agent.ResetPath();
    }

    public Vector3 GetMoveDirection()
    {
        if (!HasCompletePath)
            return Vector3.zero;

        // ✅ 用 steeringTarget 作为下一步路点（不依赖 desiredVelocity）
        Vector3 to = agent.steeringTarget - transform.position;
        to.y = 0f;

        if (to.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        return to.normalized;
    }

    public void SyncPosition(Vector3 worldPos)
    {
        if (agent == null) return;

        if (!agent.isOnNavMesh)
        {
            agent.Warp(worldPos);
            return;
        }

        agent.nextPosition = worldPos;
    }

    bool IsAgentReady()
    {
        return agent != null && agent.isOnNavMesh;
    }
}
