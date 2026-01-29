using UnityEngine;

[DisallowMultipleComponent]
public class PlayerStaminaActions : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] CombatStats stats;
    [SerializeField] PlayerMove move;

    [Header("Costs (Instant)")]
    [SerializeField] int rollCost = 25;
    [SerializeField] int dodgeCost = 20;

    // ✅ 新增：Jump
    [SerializeField] int jumpCost = 15;

    [Header("Heavy Costs (Instant)")]
    [SerializeField] int heavyCostA = 30;
    [SerializeField] int heavyCostB = 30;

    [Header("Run Attack Costs (Instant)")]
    [SerializeField] int runAttackCostA = 15;
    [SerializeField] int runAttackCostB = 15;

    [Header("Sprint Attack Costs (Instant)")]
    [SerializeField] int sprintAttackCostA = 20;
    [SerializeField] int sprintAttackCostB = 20;

    [Header("Costs (Per Second)")]
    [Tooltip("跑步(按住Shift)每秒消耗体力")]
    [SerializeField] float runDrainPerSecond = 6f;

    [Tooltip("冲刺(双击Shift)每秒消耗体力")]
    [SerializeField] float sprintDrainPerSecond = 12f;

    [Header("Action Exhaust Gate")]
    [Range(0f, 1f)]
    [Tooltip("行动把体力耗到0后，需要恢复到该比例才能再次做动作（仅玩家行动，不影响破防阈值）")]
    [SerializeField] float actionResumeThreshold01 = 0.25f;

    // ✅ 新增：没有敌人处于 Combat 时，体力动作不消耗
    [Header("Peace Time Rule")]
    [Tooltip("没有任何敌人处于 Combat 状态时：所有体力消耗动作不扣体力（翻滚/闪避/跳跃/重击/跑冲刺/跑冲刺攻击）。")]
    [SerializeField] bool noStaminaCostWhenNoEnemyInCombat = true;

    bool isActionExhausted;
    float movementDrainAcc;

    void Awake()
    {
        if (stats == null) stats = GetComponent<CombatStats>();
        if (move == null) move = GetComponent<PlayerMove>();
    }

    void Update()
    {
        if (stats == null) return;

        // 体力为0：必定处于行动耗尽
        if (stats.CurrentStamina <= 0)
            isActionExhausted = true;

        // 行动耗尽：回到阈值才解除
        if (isActionExhausted)
        {
            int need = Mathf.RoundToInt(stats.maxStamina * actionResumeThreshold01);
            if (stats.CurrentStamina >= need)
                isActionExhausted = false;
        }
    }

    void LateUpdate()
    {
        TickMovementDrain(Time.deltaTime);
    }

    bool ShouldSkipStaminaCost()
    {
        if (!noStaminaCostWhenNoEnemyInCombat)
            return false;

        // ✅ 关键：只要场上没有任何敌人处于 Combat，就不消耗体力
        return !EnemyState.AnyEnemyInCombat;
    }

    void TickMovementDrain(float dt)
    {
        if (stats == null || move == null) return;
        if (isActionExhausted) return;

        // ✅ 和平期不扣跑/冲刺持续耗体力
        if (ShouldSkipStaminaCost())
            return;

        float rate = 0f;
        if (move.IsSprinting) rate = sprintDrainPerSecond;
        else if (move.IsRunning) rate = runDrainPerSecond;

        if (rate <= 0f) return;

        if (stats.CurrentStamina <= 0)
        {
            isActionExhausted = true;
            return;
        }

        movementDrainAcc += rate * dt;
        int spend = Mathf.FloorToInt(movementDrainAcc);
        if (spend <= 0) return;

        spend = Mathf.Min(spend, stats.CurrentStamina);
        stats.ConsumeStamina(spend);
        movementDrainAcc -= spend;

        if (stats.CurrentStamina <= 0)
            isActionExhausted = true;
    }

    public bool IsActionExhausted => isActionExhausted;

    // ✅ C7：只读信号——“体力角度是否允许跑/冲刺”
    public bool CanRunOrSprint
    {
        get
        {
            if (stats == null) return false;
            if (isActionExhausted) return false;

            // ✅ 和平期允许跑/冲刺（因为不扣体力）
            if (ShouldSkipStaminaCost())
                return true;

            return stats.CurrentStamina > 0;
        }
    }

    public bool CanStartAction()
    {
        if (stats == null) return false;
        if (isActionExhausted) return false;

        // ✅ 和平期允许启动动作（因为不扣体力）
        if (ShouldSkipStaminaCost())
            return true;

        return stats.CurrentStamina > 0;
    }

    public bool TryRoll() => TrySpendToZeroIfNeeded(rollCost);
    public bool TryDodge() => TrySpendToZeroIfNeeded(dodgeCost);

    // ✅ 新增：跳跃扣体力（起跳真相点在 PlayerMove 调用）
    public bool TryJump() => TrySpendToZeroIfNeeded(jumpCost);

    public bool TryHeavy(bool attackA)
    {
        int cost = attackA ? heavyCostA : heavyCostB;
        return TrySpendToZeroIfNeeded(cost);
    }

    public bool TryRunAttack(bool attackA)
    {
        int cost = attackA ? runAttackCostA : runAttackCostB;
        return TrySpendToZeroIfNeeded(cost);
    }

    public bool TrySprintAttack(bool attackA)
    {
        int cost = attackA ? sprintAttackCostA : sprintAttackCostB;
        return TrySpendToZeroIfNeeded(cost);
    }

    bool TrySpendToZeroIfNeeded(int cost)
    {
        if (!CanStartAction())
            return false;

        // ✅ 和平期：动作照样允许，但不消耗体力、不触发耗尽
        if (ShouldSkipStaminaCost())
            return true;

        int spend = cost <= 0 ? 0 : Mathf.Min(cost, stats.CurrentStamina);
        if (spend > 0)
            stats.ConsumeStamina(spend);

        if (stats.CurrentStamina <= 0)
            isActionExhausted = true;

        return true;
    }
}
