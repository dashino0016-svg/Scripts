using UnityEngine;

[RequireComponent(typeof(EnemyState))]
[RequireComponent(typeof(EnemyMove))]
[RequireComponent(typeof(EnemyNavigator))] // ⭐ 新增
public class NotCombat : MonoBehaviour
{
    [Header("Idle")]
    public float idleTimeMin = 2f;
    public float idleTimeMax = 5f;

    [Header("Patrol")]
    public Transform[] patrolPoints;
    public float arriveDistance = 0.3f;

    EnemyState enemyState;
    EnemyMove move;
    EnemyNavigator navigator;   // ⭐ 新增
    CombatReceiver receiver;

    int currentPatrolIndex;
    float idleTimer;
    float currentIdleDuration;

    enum Phase
    {
        Idle,
        Move
    }

    Phase phase;

    void Awake()
    {
        enemyState = GetComponent<EnemyState>();
        move = GetComponent<EnemyMove>();
        navigator = GetComponent<EnemyNavigator>(); // ⭐ 新增
        receiver = GetComponent<CombatReceiver>();
    }

    void OnEnable()
    {
        EnterIdle();
    }

    void Update()
    {
        if (enemyState == null || move == null)
            return;

        // ⭐⭐⭐ 受击优先级最高：完全冻结 NotCombat
        if (receiver != null && receiver.IsInHitLock)
        {
            navigator.Stop();
            move.SetMoveDirection(Vector3.zero);
            move.SetMoveSpeedLevel(0);
            return;
        }

        // ⭐ 一旦进入 Combat / LostTarget，立即停掉巡逻
        if (enemyState.IsInCombat)
        {
            navigator.Stop();
            return;
        }

        // ⭐ 同步 NavMesh 的虚拟位置
        navigator.SyncPosition(transform.position);

        switch (phase)
        {
            case Phase.Idle:
                UpdateIdle();
                break;

            case Phase.Move:
                UpdateMove();
                break;
        }
    }


    /* ================= Idle ================= */

    void EnterIdle()
    {
        phase = Phase.Idle;
        idleTimer = 0f;
        currentIdleDuration = Random.Range(idleTimeMin, idleTimeMax);

        navigator.Stop();
        move.SetMoveDirection(Vector3.zero);
        move.SetMoveSpeedLevel(0);
    }

    void UpdateIdle()
    {
        idleTimer += Time.deltaTime;

        if (idleTimer >= currentIdleDuration)
        {
            EnterMove();
        }
    }

    /* ================= Patrol ================= */

    void EnterMove()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            EnterIdle();
            return;
        }

        phase = Phase.Move;

        // ⭐ 设置 NavMesh 目标点
        navigator.SetTarget(patrolPoints[currentPatrolIndex].position);
    }

    void UpdateMove()
    {
        Transform target = patrolPoints[currentPatrolIndex];

        // ⭐ 告诉 NavMesh 当前要去的点（防止中途被打断）
        navigator.SetTarget(target.position);

        Vector3 dir = GetMoveDirection(target.position);

        float distance = Vector3.Distance(
            transform.position,
            target.position
        );

        if (distance <= arriveDistance)
        {
            currentPatrolIndex =
                (currentPatrolIndex + 1) % patrolPoints.Length;

            EnterIdle();
            return;
        }

        if (dir == Vector3.zero)
        {
            move.SetMoveDirection(Vector3.zero);
            move.SetMoveSpeedLevel(0);
            return;
        }

        move.SetMoveDirection(dir.normalized);
        move.SetMoveSpeedLevel(1); // Walk
    }

    /* ================= Direction Helper ================= */

    Vector3 GetMoveDirection(Vector3 fallbackTarget)
    {
        Vector3 navDir = navigator.GetMoveDirection();

        if (navDir != Vector3.zero)
            return navDir;

        return Vector3.zero;
    }
}
