using UnityEngine;

[RequireComponent(typeof(EnemyState))]
[RequireComponent(typeof(EnemyMove))]
[RequireComponent(typeof(EnemyNavigator))]
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
    EnemyNavigator navigator;
    EnemyController controller;
    CombatReceiver receiver;

    int currentPatrolIndex;
    float idleTimer;
    float currentIdleDuration;

    EnemyStateType lastState;

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
        navigator = GetComponent<EnemyNavigator>();
        controller = GetComponent<EnemyController>();
        receiver = GetComponent<CombatReceiver>();
        lastState = enemyState != null ? enemyState.Current : EnemyStateType.NotCombat;
    }

    void OnEnable()
    {
        EnterIdle();
        lastState = enemyState != null ? enemyState.Current : EnemyStateType.NotCombat;
    }

    void Update()
    {
        if (enemyState == null || move == null || navigator == null)
            return;
        // ✅ 只在 NotCombat 状态下运行本脚本
        if (enemyState.Current != EnemyStateType.NotCombat)
        {
            // ✅ 仅在“刚离开 NotCombat”的那一帧，停一次巡逻，避免带着巡逻惯性进入战斗/丢失目标
            if (lastState == EnemyStateType.NotCombat)
            {
                navigator.Stop();
                move.SetMoveDirection(Vector3.zero);
                move.SetMoveSpeedLevel(0);
            }

            lastState = enemyState.Current;
            return;
        }

        // ✅ 从其他状态回到 NotCombat：重新进入 Idle（可选，但更稳定）
        if (lastState != EnemyStateType.NotCombat)
        {
            EnterIdle();
        }
        lastState = enemyState.Current;

        // ⭐⭐⭐ 受击优先级最高：冻结 NotCombat
        if (receiver != null && receiver.IsInHitLock)
        {
            navigator.Stop();
            move.SetMoveDirection(Vector3.zero);
            move.SetMoveSpeedLevel(0);
            return;
        }

        if (controller != null && (controller.IsAirborne || controller.IsInLandLock))
        {
            navigator.Stop();
            move.SetMoveDirection(Vector3.zero);
            move.SetMoveSpeedLevel(0);
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

        navigator.SetTarget(patrolPoints[currentPatrolIndex].position);
    }

    void UpdateMove()
    {
        Transform target = patrolPoints[currentPatrolIndex];

        // 持续更新目标点（巡逻点不动也无妨）
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

        move.SetMoveDirection(dir.normalized);
        move.SetMoveSpeedLevel(1); // Walk
    }

    /* ================= Direction Helper ================= */

    Vector3 GetMoveDirection(Vector3 fallbackTarget)
    {
        Vector3 navDir = navigator.GetMoveDirection();

        if (navDir != Vector3.zero)
            return navDir;

        Vector3 dir = fallbackTarget - transform.position;
        dir.y = 0f;
        return dir.normalized;
    }
}
