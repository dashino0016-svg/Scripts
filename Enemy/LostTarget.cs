using UnityEngine;

[RequireComponent(typeof(EnemyState))]
[RequireComponent(typeof(EnemyMove))]
[RequireComponent(typeof(EnemyNavigator))]
public class LostTarget : MonoBehaviour
{
    [Header("Return Home")]
    public Transform homePoint;
    public float homeArriveDistance = 0.3f;

    [Header("Move")]
    [Tooltip("回家阶段的速度档位（EnemyMove 里一般 1=Walk）。")]
    public int returnHomeSpeedLevel = 1;

    EnemyState enemyState;
    EnemyMove move;
    EnemyController controller;
    EnemyNavigator navigator;
    CombatReceiver receiver;
    SwordController sword;
    MeleeFighter meleeFighter;
    RangeFighter rangeFighter;
    EnemyAbilitySystem abilitySystem;
    BlockController block;

    bool sheathRequested;

    void Awake()
    {
        enemyState = GetComponent<EnemyState>();
        move = GetComponent<EnemyMove>();
        controller = GetComponent<EnemyController>();
        navigator = GetComponent<EnemyNavigator>();
        receiver = GetComponent<CombatReceiver>();
        sword = GetComponentInChildren<SwordController>();
        meleeFighter = GetComponent<MeleeFighter>();
        rangeFighter = GetComponent<RangeFighter>();
        abilitySystem = GetComponent<EnemyAbilitySystem>();
        block = GetComponent<BlockController>();
    }

    void OnEnable()
    {
        enemyState.OnStateChanged += OnStateChanged;
    }

    void OnDisable()
    {
        enemyState.OnStateChanged -= OnStateChanged;
    }

    void OnStateChanged(EnemyStateType prev, EnemyStateType next)
    {
        if (next != EnemyStateType.LostTarget)
            return;

        sheathRequested = false;

        navigator.Stop();
        move.SetMoveDirection(Vector3.zero);
        move.SetMoveSpeedLevel(0);
    }

    void Update()
    {
        if (enemyState.Current != EnemyStateType.LostTarget)
            return;

        // 受击优先级最高：停住
        if (receiver != null && receiver.IsInHitLock)
        {
            StopAllMove();
            return;
        }

        if (controller != null && controller.IsInLandLock)
        {
            StopAllMove();
            return;
        }

        if (controller != null && controller.IsAirborne)
        {
            navigator.Stop();
            return;
        }

        navigator.SyncPosition(transform.position);

        if (enemyState.LostTargetPhase == LostTargetPhase.Armed)
            UpdateSheathThenSwitchPhase();
        else
            UpdateReturnHome();
    }

    // ✅ 丢失目标后：不巡逻不乱走，直接收刀
    void UpdateSheathThenSwitchPhase()
    {
        StopAllMove();

        // 等待武器过渡（拔/收刀）结束
        if (controller != null && controller.IsInWeaponTransition)
            return;

        // ✅ 关键：玩家死亡后先让敌人把当前动作完整做完，再开始收刀
        if (IsInCombatActionLock())
            return;

        // 如果已经处于收刀状态（或本来就没武装），直接推进到 ReturnHome
        if (sword != null && !sword.IsArmed)
        {
            enemyState.OnSheathSwordEnd();
            return;
        }

        // 只请求一次收刀动画
        if (!sheathRequested)
        {
            sheathRequested = true;
            controller.RequestSheathSword();
        }
    }

    // ✅ 收刀完成后：回到 homePoint
    void UpdateReturnHome()
    {
        if (controller != null && controller.IsInWeaponTransition)
        {
            return;
        }

        if (homePoint == null)
        {
            StopAllMove();
            return;
        }

        Vector3 toHome = homePoint.position - transform.position;
        toHome.y = 0f;
        float dist = toHome.magnitude;

        if (dist <= homeArriveDistance)
        {
            StopAllMove();
            enemyState.OnReturnHomeReached();
            return;
        }

        navigator.SetTarget(homePoint.position);

        Vector3 dir = navigator.GetMoveDirection();
        if (dir == Vector3.zero) dir = toHome.normalized;

        move.SetMoveDirection(dir.normalized);
        move.SetMoveSpeedLevel(returnHomeSpeedLevel);
    }

    bool IsInCombatActionLock()
    {
        if (receiver != null && receiver.IsInHitLock)
            return true;

        if (meleeFighter != null && meleeFighter.enabled)
        {
            if (meleeFighter.IsInAttackLock || meleeFighter.IsInComboWindow || meleeFighter.IsInHitWindow)
                return true;
        }

        if (rangeFighter != null && rangeFighter.enabled && rangeFighter.IsInAttackLock)
            return true;

        if (abilitySystem != null && abilitySystem.enabled && abilitySystem.IsInAbilityLock)
            return true;

        if (block != null && block.enabled && block.IsBlocking)
            return true;

        return false;
    }

    void StopAllMove()
    {
        navigator.Stop();
        move.SetMoveDirection(Vector3.zero);
        move.SetMoveSpeedLevel(0);
    }
}
