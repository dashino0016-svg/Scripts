using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(EnemyMove))]
[RequireComponent(typeof(EnemyNavigator))]
public class Combat : MonoBehaviour, IEnemyCombat
{
    [Header("Random Start Combo (Enemy Only)")]
    public bool enableRandomStartComboIndex = true; // 默认 false：不破坏旧行为
                                                    // ✅ Avoid repeating same planned normal combo (signature)
    bool hasLastNormalPlan;
    bool lastPlanAttackA;
    int lastPlanStartCombo;

    [Header("Speed Level (EnemyMove)")]
    public int walkSpeedLevel = 1;
    public int runSpeedLevel = 2;
    public int sprintSpeedLevel = 3;

    [Header("Range (Chase <-> Engage)")]
    public float enterEngageDistance = 8f;
    public float exitEngageDistance = 10f;
    public float outOfRangeGraceTime = 0.6f;

    [Header("Engage Distances (Only One Action Gate)")]
    [Tooltip("只要进入该距离：允许防御/普通攻击/重攻击/反击等动作参与决策")]
    public float attackDecisionDistance = 1f;

    [Header("Rotate")]
    public float rotateSpeed = 4f;

    [Header("Engage Approach Burst (Occasionally Run In)")]
    [Tooltip("在 Engage 阶段（已进入 enterEngageDistance）但尚未达到 attackDecisionDistance 时，偶尔用 Run 快速贴近，避免永远 walk 慢慢挪过去被玩家先手。")]
    public bool enableEngageRunBurst = true;

    [Range(0f, 1f)]
    public float engageRunBurstChance = 0.5f;

    [Range(0f, 1f)]
    public float engageRunBurstChanceWhenPlayerGuardBroken = 1f;

    [Tooltip("距离 >= 该值时才允许触发 Run 贴近（避免贴脸来回切速度）。")]
    public float engageRunBurstMinDistance = 1f;

    [Tooltip("Run 贴近持续时间范围（秒）。")]
    public float engageRunBurstMinDuration = 2f;
    public float engageRunBurstMaxDuration = 4f;

    [Tooltip("Run 贴近的冷却时间（秒）。")]
    public float engageRunBurstCooldown = 0f;

    [Header("Sprint -> SprintAttack (Chase Only)")]
    public bool enableSprintAttackA = true;
    public bool enableSprintAttackB = false;

    [Header("Sprint -> SprintAttack Timeout")]
    public float sprintAttackArmingTimeout = 4f; // 冲刺多久还没贴到就取消（秒）

    float runAttackArmingStartTime;

    [Tooltip("两者都允许时，用 B 的概率（0=总是A，1=总是B）。")]
    [Range(0f, 1f)] public float sprintAttackUseBChance = 0f;

    public float sprintAttackMinDist = 5f;
    public float sprintAttackMaxDist = 10f;
    public float sprintAttackTriggerDist = 1.5f;

    public float sprintAttackCooldown = 10f;
    [Range(0f, 1f)] public float sprintAttackChance = 0.5f;

    public float sprintAttackArmDelayAfterEnterChase = 0.35f;

    [Header("Normal Attacks (A1~A4 / B1~B4)")]
    [Range(0f, 1f)] public float preferAChance = 0.7f;
    [Range(0f, 1f)] public float preferAWhenPlayerGuardBroken = 1f;

    public int maxComboA = 4;
    public int maxComboB = 4;

    [Header("Flexible Combo Continue Chances (Optional)")]
    public List<float> comboContinueChancesA = new List<float>();
    public List<float> comboContinueChancesB = new List<float>();

    [Header("HeavyAttack (No Distance)")]
    public bool enableHeavyAttackA = true;
    public bool enableHeavyAttackB = false;

    [Range(0f, 1f)] public float heavyUseBChance = 0.25f;

    public float heavyCooldown = 5f;

    [Range(0f, 1f)] public float heavyChance = 0.1f;
    [Range(0f, 1f)] public float heavyChanceWhenPlayerGuardBroken = 0.6f;

    [Header("Defense (Block) (No Distance)")]
    public bool enableDefense = true;

    [Tooltip("防御反应距离：只要玩家出手且距离 <= 该值，就允许进入防御。")]
    public float defenseDistance = 1.5f;

    [Range(0f, 1f)] public float blockChanceWhenPlayerAttacking = 1f;
    public float blockHoldMin = 1f;
    public float blockHoldMax = 2f;
    public float blockCooldown = 0f;
    public float stopBlockingWhenPlayerNotAttackingDelay = 0.5f;

    // Engage Run Burst runtime
    bool engageRunBurstActive;
    float engageRunBurstEndTime;
    float nextEngageRunBurstAllowedTime;
    float nextEngageRunBurstRollTime;

    [Header("Cooldown")]
    public float cooldownDuration = 2f;
    public float pressureCooldownWhenPlayerGuardBroken = 0f;

    [Header("Post-Attack -> Cooldown Chance")]
    [Range(0f, 1f)]
    public float cooldownAfterAttackChance = 0.2f;

    [Range(0f, 1f)]
    public float cooldownAfterAttackChanceWhenPlayerGuardBroken = 0f;

    // ✅ 你要的：Cooldown 四种姿态（只用于 cooldown，不影响其它状态）
    [Header("Cooldown Postures (Idle / WalkBack / WalkLeft / WalkRight)")]
    public bool enableCooldownPostures = true;
    public float cooldownPostureMinTime = 1f;
    public float cooldownPostureMaxTime = 2f;

    public float cooldownIdleWeight = 1f;
    public float cooldownWalkBackWeight = 2f;
    public float cooldownWalkLeftWeight = 2f;
    public float cooldownWalkRightWeight = 2f;

    [Header("Cooldown Strafe Setup")]
    [Tooltip("Cooldown 横移/后退时，是否用“面向目标的基准轴”来生成左右/后退方向（更稳定）。")]
    public bool cooldownUseTargetBasis = true;

    [Tooltip("默认关闭：避免 Combat 和 EnemyMove 同时写 MoveX/MoveY 导致抽搐。若你确认 EnemyMove 不写 MoveX/MoveY，再打开。")]
    public bool driveAnimatorMoveParamsInCooldown = false;

    [Header("Smoothing")]
    public float speedLevelChangeRate = 5f;

    [Header("Ability - Decision (Probability + Distance)")]
    [Tooltip("AI will only roll ability usage at this interval to avoid spamming per-frame.")]
    public float abilityDecisionMinInterval = 10f;
    public float abilityDecisionMaxInterval = 20f;

    [Header("Ability - Shockwave (Ability1 = Cone / Ability2 = AoE)")]
    public bool enableAbilityShockwave = false;
    [Range(0f, 1f)] public float abilityShockwaveConeChance = 0.35f;
    [Range(0f, 1f)] public float abilityShockwaveAoeChance = 0.35f;

    EnemyMove move;
    EnemyNavigator navigator;
    EnemyController controller;
    Animator anim;
    CombatReceiver receiver;
    MeleeFighter fighter;
    EnemyAbilitySystem ability;

    BlockController block;
    CombatStats selfStats;

    Transform target;
    MeleeFighter targetFighter;
    CombatStats targetStats;

    bool active;

    enum RangeMode { Chase, Engage }
    RangeMode rangeMode = RangeMode.Chase;
    float outOfRangeTimer;
    float chaseEnterTime;

    enum State { Chase, Engage, Block, Attack, Retreat, Ability, Cooldown }
    State state = State.Chase;

    float stateTimer;
    float currentSpeedLevel;
    float dt;

    bool cachedPlayerGuardBroken;

    bool runAttackArming;
    bool runAttackPlanIsA = true;
    float nextRunAttackAllowedTime;
    bool runAttackRolledInBand;
    float nextAbilityDecisionTime;
    float nextHeavyAllowedTime;

    float blockReleaseTime;
    float nextBlockAllowedTime;
    float playerNotAttackingTimer;

    bool planIsHeavy;
    bool planAttackA;
    bool planHeavyAttackA = true;
    int planTargetCombo = 1;
    int planStartCombo = 1;
    int planQueuedAtComboIndex = 0;
    bool planStarted;
    bool planPendingStart;

    HashSet<int> hitConfirmedCombos = new HashSet<int>();

    static readonly int AnimIsRetreating = Animator.StringToHash("IsRetreating");
    static readonly int AnimMoveX = Animator.StringToHash("MoveX");
    static readonly int AnimMoveY = Animator.StringToHash("MoveY");

    bool cachedApplyRootMotion;
    bool cachedApplyRootMotionValid;

    // =========================
    // ✅ Cooldown posture runtime
    // =========================
    enum CooldownPosture { Idle, WalkBack, WalkLeft, WalkRight }
    CooldownPosture cooldownPosture = CooldownPosture.Idle;
    bool cooldownInited;
    float cooldownEndTime;
    float cooldownPostureEndTime;

    // =========================
    // ✅ Block return policy
    // =========================
    State blockReturnState = State.Cooldown;
    bool blockCameFromCooldown;

    bool cachedCooldownInited;
    float cachedCooldownEndTime;
    float cachedCooldownPostureEndTime;
    CooldownPosture cachedCooldownPosture;

    void Awake()
    {
        move = GetComponent<EnemyMove>();
        navigator = GetComponent<EnemyNavigator>();
        controller = GetComponent<EnemyController>();
        anim = GetComponent<Animator>();
        receiver = GetComponent<CombatReceiver>();
        fighter = GetComponent<MeleeFighter>();
        ability = GetComponent<EnemyAbilitySystem>();

        block = GetComponent<BlockController>();
        selfStats = GetComponent<CombatStats>();
    }

    void OnEnable()
    {
        if (receiver == null) receiver = GetComponent<CombatReceiver>();

        if (fighter == null) fighter = GetComponent<MeleeFighter>();
        if (fighter != null)
            fighter.OnHitLanded += HandleHitLanded;
    }

    void OnDisable()
    {
        if (fighter != null)
            fighter.OnHitLanded -= HandleHitLanded;
    }

    float GetDt()
    {
        float scale = (controller != null) ? controller.LocalTimeScale : 1f;
        return Time.deltaTime * scale;
    }

    public void EnterCombat(Transform combatTarget)
    {
        move.SetRotationEnabled(false);
        target = combatTarget;
        active = true;

        CacheTargetRefs();

        rangeMode = RangeMode.Chase;
        state = State.Chase;

        outOfRangeTimer = 0f;
        chaseEnterTime = Time.time;

        runAttackArming = false;
        runAttackRolledInBand = false;
        runAttackPlanIsA = true;

        ResetPlan();

        stateTimer = 0f;

        engageRunBurstActive = false;
        engageRunBurstEndTime = 0f;
        nextEngageRunBurstAllowedTime = Time.time;

        nextEngageRunBurstRollTime = Time.time;

        cooldownInited = false;
        nextAbilityDecisionTime = Time.time;

        if (anim != null)
        {
            anim.SetBool(AnimIsRetreating, false);
            RestoreRootMotionIfNeeded();
            ResetCooldownMove2D();
        }
    }

    public void ExitCombat()
    {
        move.SetRotationEnabled(true);
        active = false;
        target = null;
        targetFighter = null;
        targetStats = null;

        runAttackArming = false;
        ResetPlan();

        if (block != null)
            block.RequestBlock(false);

        engageRunBurstActive = false;
        engageRunBurstEndTime = 0f;

        cooldownInited = false;
        ExitCooldownPosture();

        if (anim != null)
        {
            anim.SetBool(AnimIsRetreating, false);
            RestoreRootMotionIfNeeded();
            ResetCooldownMove2D();
        }

        navigator.Stop();
        StopMove();
    }

    public void Tick()
    {
        if (!active || target == null) return;

        dt = GetDt();

        if (receiver != null && receiver.IsInHitLock)
        {
            StopMove();

            return;
        }

        if (controller != null && controller.IsInWeaponTransition)
        {
            return;
        }

        if (ability != null && ability.IsInAbilityLock)
        {
            StopMove();
            return;
        }

        if (targetFighter == null || targetStats == null) CacheTargetRefs();

        Vector3 toTarget = GetTargetPoint() - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        navigator.SyncPosition(transform.position);

        bool playerGuardBroken = (targetStats != null && targetStats.IsGuardBroken);
        cachedPlayerGuardBroken = playerGuardBroken;

        bool motionLock = (fighter != null && fighter.enabled &&
                           (fighter.IsInAttackLock || fighter.IsInComboWindow));

        if (state == State.Ability && (ability == null || !ability.IsInAbilityLock))
            EnterState(State.Cooldown);

        UpdateRangeMode(distance);

        bool retreatActive = (selfStats != null) && selfStats.IsGuardBroken;

        if (!retreatActive && state == State.Retreat)
        {
            EnterState(State.Cooldown);
        }

        // ✅ 破防 Retreat：保留原 Retreat 动画逻辑（IsRetreating + RootMotion）
        if (retreatActive)
        {
            if (block != null) block.RequestBlock(false);

            navigator.Stop();
            StopMove();

            RotateToTarget(toTarget);

            if (!motionLock && state != State.Retreat)
                EnterState(State.Retreat);

            return;
        }

        if (motionLock)
        {
            StopMove();

            if (state == State.Attack && !planIsHeavy)
                TryQueueNormalCombo();

            if (state == State.Attack && planStarted &&
                fighter != null && !fighter.IsInAttackLock && !fighter.IsInComboWindow &&
                !planPendingStart)
            {
                EnterCooldownOrEngageAfterAttack();
            }

            return;
        }

        stateTimer += dt;

        if (rangeMode == RangeMode.Chase)
        {
            if (state == State.Retreat && anim != null)
                anim.SetBool(AnimIsRetreating, false);

            ExitCooldownPosture();
            cooldownInited = false;

            state = State.Chase;
            UpdateChase(distance, toTarget);
            RotateToTarget(toTarget);
            return;
        }

        if (playerGuardBroken && block != null)
            block.RequestBlock(false);

        switch (state)
        {
            case State.Engage:
                UpdateEngage(distance, toTarget, playerGuardBroken);
                break;
            case State.Block:
                UpdateBlock(distance);
                break;
            case State.Attack:
                UpdateAttack();
                break;
            case State.Retreat:
                break;
            case State.Ability:
                StopMove();
                break;
            case State.Cooldown:
                UpdateCooldown(distance, toTarget, playerGuardBroken);
                break;
            default:
                EnterState(State.Engage);
                break;
        }

        bool attackLock2 = (fighter != null && fighter.enabled && fighter.IsInAttackLock);
        if (!attackLock2)
            RotateToTarget(toTarget);
    }

    void UpdateRangeMode(float distance)
    {
        if (rangeMode == RangeMode.Chase)
        {
            if (runAttackArming) return;

            if (distance <= enterEngageDistance)
            {
                rangeMode = RangeMode.Engage;
                outOfRangeTimer = 0f;
                EnterState(State.Engage);
            }
            return;
        }

        if (distance > exitEngageDistance)
        {
            outOfRangeTimer += dt;
            if (outOfRangeTimer >= outOfRangeGraceTime)
            {
                rangeMode = RangeMode.Chase;
                outOfRangeTimer = 0f;

                runAttackArming = false;
                chaseEnterTime = Time.time;
                runAttackRolledInBand = false;

                EnterState(State.Chase);
            }
        }
        else outOfRangeTimer = 0f;
    }

    bool CanUseSprintAttackA() => enableSprintAttackA;
    bool CanUseSprintAttackB() => enableSprintAttackB;

    bool ChooseSprintAttackIsA()
    {
        bool canA = CanUseSprintAttackA();
        bool canB = CanUseSprintAttackB();

        if (canA && !canB) return true;
        if (!canA && canB) return false;
        if (!canA && !canB) return true;

        return Random.value > sprintAttackUseBChance;
    }

    void UpdateChase(float distance, Vector3 toTarget)
    {
        bool playerAttacking = (targetFighter != null) && targetFighter.IsInAttackLock;

        if (runAttackArming)
        {
            // 超时仍未进入触发距离：取消本次冲刺攻击计划
            if (Time.time - runAttackArmingStartTime >= sprintAttackArmingTimeout)
            {
                runAttackArming = false;

                // 取消后的节流：别立刻再次 arming（否则会抖动）
                nextRunAttackAllowedTime = Time.time + 0.6f;

                // ✅ 选项A（推荐）：保持 runAttackRolledInBand=true
                // 这样在同一距离带内不会反复roll，直到离开距离带才再roll
                // runAttackRolledInBand 不用改

                // ✅ 可选：如果你希望超时后“仍在带内也能再次roll”，那就：
                // runAttackRolledInBand = false;
            }
        }

        bool inBand = distance >= sprintAttackMinDist && distance <= sprintAttackMaxDist;

        if (!inBand)
        {
            runAttackRolledInBand = false;
        }
        else
        {
            bool anySprintAttackEnabled = CanUseSprintAttackA() || CanUseSprintAttackB();

            if (anySprintAttackEnabled &&
                !runAttackArming &&
                !runAttackRolledInBand &&
                Time.time >= nextRunAttackAllowedTime &&
                Time.time >= chaseEnterTime + sprintAttackArmDelayAfterEnterChase &&
                !playerAttacking)
            {
                runAttackRolledInBand = true;
                if (Random.value <= sprintAttackChance)
                {
                    runAttackArming = true;
                    runAttackPlanIsA = ChooseSprintAttackIsA();
                    runAttackArmingStartTime = Time.time;
                }
            }
        }

        navigator.SetTarget(GetTargetPoint());

        Vector3 dir = navigator.GetMoveDirection();
        if (dir == Vector3.zero) dir = toTarget.normalized;

        int targetSpeed = runAttackArming ? sprintSpeedLevel : runSpeedLevel;

        currentSpeedLevel = Mathf.MoveTowards(
            currentSpeedLevel, targetSpeed, speedLevelChangeRate * dt);

        move.SetMoveDirection(dir);
        move.SetMoveSpeedLevel(Mathf.RoundToInt(currentSpeedLevel));

        if (runAttackArming && distance <= sprintAttackTriggerDist)
        {
            runAttackArming = false;
            nextRunAttackAllowedTime = Time.time + sprintAttackCooldown;

            if (fighter != null && fighter.enabled)
                fighter.TryAttack(runAttackPlanIsA, AttackMoveType.Sprint);

            rangeMode = RangeMode.Engage;
            EnterCooldownOrEngageAfterAttack();

            StopMove();
        }
    }

    void UpdateEngage(float distance, Vector3 toTarget, bool playerGuardBroken)
    {
        if (TryStartAbility(distance))
            return;

        if (!playerGuardBroken && distance <= defenseDistance && ShouldStartBlock(distance))
        {
            EnterState(State.Block);
            StartBlock();
            return;
        }

        if (distance > attackDecisionDistance)
        {
            ApproachUntilDecision(distance, toTarget, playerGuardBroken);
            return;
        }
        engageRunBurstActive = false;

        StopMove();

        if (!playerGuardBroken && ShouldStartBlock(distance))
        {
            EnterState(State.Block);
            StartBlock();
            return;
        }

        if (ShouldStartHeavy(playerGuardBroken, out bool heavyIsA))
        {
            StartHeavyAttack(heavyIsA);
            EnterState(State.Attack);
            return;
        }

        StartNormalPlan(playerGuardBroken);
        EnterState(State.Attack);
    }

    bool TryStartAbility(float distance)
    {
        if (ability == null) return false;

        // ✅ 概率 + 距离：为了避免每帧都 roll，这里加一个 decision interval gate
        if (Time.time < nextAbilityDecisionTime)
            return false;

        float minI = Mathf.Max(0.05f, abilityDecisionMinInterval);
        float maxI = Mathf.Max(minI, abilityDecisionMaxInterval);
        nextAbilityDecisionTime = Time.time + Random.Range(minI, maxI);

        // =========================
        // Ability1/2：Shockwave（Cone / AoE）
        // =========================
        if (!enableAbilityShockwave)
            return false;

        float maxShockRange = ability.ShockwaveDecisionRange;
        if (maxShockRange > 0f && distance > maxShockRange)
            return false;

        // 近距离优先 AoE（Ability2）
        if (ShouldStartShockwaveAoe() && Random.value <= abilityShockwaveAoeChance)
        {
            if (ability.TryCast(EnemyAbilitySystem.AbilityType.Ability2, target))
            {
                if (block != null) block.RequestBlock(false);
                ResetPlan();
                StopMove();
                EnterState(State.Ability);
                return true;
            }
        }

        // 扇形（Ability1）
        if (ShouldStartShockwaveCone() && Random.value <= abilityShockwaveConeChance)
        {
            if (ability.TryCast(EnemyAbilitySystem.AbilityType.Ability1, target))
            {
                if (block != null) block.RequestBlock(false);
                ResetPlan();
                StopMove();
                EnterState(State.Ability);
                return true;
            }
        }

        return false;
    }

    bool ShouldStartShockwaveCone()
    {
        if (!ability.CanTryCast(EnemyAbilitySystem.AbilityType.Ability1)) return false;
        return ability.CanAbility1Target(target);
    }

    bool ShouldStartShockwaveAoe()
    {
        if (!ability.CanTryCast(EnemyAbilitySystem.AbilityType.Ability2)) return false;
        return ability.CanAbility2Target(target);
    }

    void HandleHitLanded(AttackData data)
    {
        if (state != State.Attack) return;
        if (planIsHeavy) return;
        if (fighter == null || !fighter.enabled) return;
        if (fighter.CurrentAttackCategory != AttackCategory.Normal) return;

        int cur = fighter.CurrentComboIndex;
        if (cur > 0)
            hitConfirmedCombos.Add(cur);
    }

    void WalkApproach(Vector3 toTarget)
    {
        navigator.SetTarget(GetTargetPoint());

        Vector3 dir = navigator.GetMoveDirection();
        if (dir == Vector3.zero) dir = toTarget.normalized;

        currentSpeedLevel = Mathf.MoveTowards(
            currentSpeedLevel, walkSpeedLevel, speedLevelChangeRate * dt);

        move.SetMoveDirection(dir);
        move.SetMoveSpeedLevel(Mathf.RoundToInt(currentSpeedLevel));
    }

    void RunApproach(Vector3 toTarget)
    {
        navigator.SetTarget(GetTargetPoint());

        Vector3 dir = navigator.GetMoveDirection();
        if (dir == Vector3.zero) dir = toTarget.normalized;

        currentSpeedLevel = Mathf.MoveTowards(
            currentSpeedLevel, runSpeedLevel, speedLevelChangeRate * dt);

        move.SetMoveDirection(dir);
        move.SetMoveSpeedLevel(Mathf.RoundToInt(currentSpeedLevel));
    }

    void ApproachUntilDecision(float distance, Vector3 toTarget, bool playerGuardBroken)
    {
        if (!enableEngageRunBurst)
        {
            WalkApproach(toTarget);
            return;
        }

        if (engageRunBurstActive)
        {
            if (distance <= attackDecisionDistance || Time.time >= engageRunBurstEndTime)
                engageRunBurstActive = false;
        }

        if (!engageRunBurstActive &&
            distance >= engageRunBurstMinDistance &&
            Time.time >= nextEngageRunBurstAllowedTime &&
            Time.time >= nextEngageRunBurstRollTime)
        {
            nextEngageRunBurstRollTime = Time.time + 1f;

            float ch = playerGuardBroken ? engageRunBurstChanceWhenPlayerGuardBroken : engageRunBurstChance;

            if (Random.value <= ch)
            {
                engageRunBurstActive = true;

                float minD = Mathf.Max(0.01f, engageRunBurstMinDuration);
                float maxD = Mathf.Max(minD, engageRunBurstMaxDuration);
                engageRunBurstEndTime = Time.time + Random.Range(minD, maxD);

                nextEngageRunBurstAllowedTime = Time.time + Mathf.Max(0f, engageRunBurstCooldown);
            }
        }

        if (engageRunBurstActive) RunApproach(toTarget);
        else WalkApproach(toTarget);
    }

    bool IsRunningOrSprintingNow()
    {
        // Chase：冲刺攻击预备（sprint）
        if (runAttackArming) return true;

        // Engage：RunBurst 贴近（run）
        if (engageRunBurstActive) return true;

        // 当前速度等级 >= runSpeedLevel，也视为在 run/sprint
        return Mathf.RoundToInt(currentSpeedLevel) >= runSpeedLevel;
    }

    bool ShouldStartBlock(float distance)
    {
        if (!enableDefense || block == null) return false;
        if (distance > defenseDistance) return false;
        if (selfStats != null && (selfStats.IsGuardBroken || !selfStats.CanBlock)) return false;
        if (Time.time < nextBlockAllowedTime) return false;
        // ✅ Run / Sprint 时不允许进入防御
        if (IsRunningOrSprintingNow()) return false;
        bool playerAttacking = (targetFighter != null) && targetFighter.IsInAttackLock;
        if (!playerAttacking) return false;

        if (fighter != null && fighter.enabled && fighter.IsInAttackLock) return false;

        return Random.value <= blockChanceWhenPlayerAttacking;
    }

    void StartBlock()
    {
        ExitCooldownPosture();
        if (!blockCameFromCooldown)
            cooldownInited = false;

        block.RequestBlock(true);
        blockReleaseTime = Time.time + Random.Range(blockHoldMin, blockHoldMax);
        playerNotAttackingTimer = 0f;
    }

    void UpdateBlock(float distance)
    {
        if (block == null)
        {
            EnterState(State.Cooldown);
            return;
        }

        if (selfStats != null && (selfStats.IsGuardBroken || !selfStats.CanBlock))
        {
            block.RequestBlock(false);
            nextBlockAllowedTime = Time.time + blockCooldown;
            EnterState(State.Cooldown);
            return;
        }

        bool playerAttacking = (targetFighter != null) && targetFighter.IsInAttackLock;

        if (!playerAttacking) playerNotAttackingTimer += dt;
        else playerNotAttackingTimer = 0f;

        bool timeUp = Time.time >= blockReleaseTime;
        bool playerStopped = playerNotAttackingTimer >= stopBlockingWhenPlayerNotAttackingDelay;

        float maxKeepBlockDist = Mathf.Max(defenseDistance, attackDecisionDistance) * 1.2f;

        if (timeUp || playerStopped || distance > maxKeepBlockDist)
        {
            block.RequestBlock(false);
            nextBlockAllowedTime = Time.time + blockCooldown;

            if (blockReturnState == State.Engage)
            {
                EnterState(State.Engage);
            }
            else
            {
                ReturnToCooldownAfterBlock();
            }
            return;
        }
    }

    bool ShouldStartHeavy(bool playerGuardBroken, out bool heavyIsA)
    {
        heavyIsA = true;

        bool canA = enableHeavyAttackA;
        bool canB = enableHeavyAttackB;
        bool any = canA || canB;

        if (!any) return false;
        if (fighter == null || !fighter.enabled) return false;
        if (Time.time < nextHeavyAllowedTime) return false;
        if (block != null && block.IsBlocking) return false;

        float ch = playerGuardBroken ? heavyChanceWhenPlayerGuardBroken : heavyChance;
        if (Random.value > ch) return false;

        if (canA && !canB) heavyIsA = true;
        else if (!canA && canB) heavyIsA = false;
        else heavyIsA = Random.value > heavyUseBChance;

        return true;
    }

    void StartHeavyAttack(bool heavyIsA)
    {
        if (fighter == null || !fighter.enabled) return;

        nextHeavyAllowedTime = Time.time + heavyCooldown;

        ResetPlan();
        planIsHeavy = true;
        planHeavyAttackA = heavyIsA;
        planStarted = true;

        fighter.RequestHeavy(planHeavyAttackA);
        StopMove();
    }

    float GetContinueChance(bool attackA, int fromComboIndex)
    {
        var list = attackA ? comboContinueChancesA : comboContinueChancesB;
        if (list == null) return 0f;

        int idx = fromComboIndex - 1;
        if (idx < 0 || idx >= list.Count) return 0f;

        return Mathf.Clamp01(list[idx]);
    }

    int GetPlannedMaxCombo(bool attackA)
    {
        int m = Mathf.Max(1, attackA ? maxComboA : maxComboB);

        if (fighter != null && fighter.enabled)
        {
            int fighterMax = attackA ? fighter.MaxNormalComboA : fighter.MaxNormalComboB;
            m = Mathf.Min(m, Mathf.Max(1, fighterMax));
        }

        return m;
    }

    void StartNormalPlan(bool playerGuardBroken)
    {
        ResetPlan();
        planIsHeavy = false;

        const int maxReroll = 10;
        int reroll = 0;

        int m = 1;

        while (true)
        {
            bool useA = playerGuardBroken
                ? (Random.value <= preferAWhenPlayerGuardBroken)
                : (Random.value <= preferAChance);

            planAttackA = useA;

            m = GetPlannedMaxCombo(planAttackA);

            // ✅ 起手段：默认 1；开启后可 1..m
            planStartCombo = enableRandomStartComboIndex ? Random.Range(1, m + 1) : 1;

            // ✅ 只避免“起手招”连续相同（A/B + 起手段）
            bool sameOpenerAsLast =
                hasLastNormalPlan &&
                planAttackA == lastPlanAttackA &&
                planStartCombo == lastPlanStartCombo;

            if (!sameOpenerAsLast || reroll >= maxReroll)
                break;

            reroll++;
        }

        // ✅ 目标段：按你原逻辑继续（不参与防重复）
        if (playerGuardBroken)
        {
            planTargetCombo = m;
        }
        else
        {
            planTargetCombo = planStartCombo;
            for (int from = planStartCombo; from < m; from++)
            {
                float ch = GetContinueChance(planAttackA, from);
                if (Random.value <= ch) planTargetCombo = from + 1;
                else break;
            }
        }

        if (fighter != null && fighter.enabled && !fighter.IsInAttackLock && !fighter.IsInComboWindow)
            StartPlanFirstHit();
        else
            planPendingStart = true;

        // ✅ 记录“上一次起手招”
        hasLastNormalPlan = true;
        lastPlanAttackA = planAttackA;
        lastPlanStartCombo = planStartCombo;

        StopMove();
    }

    void UpdateAttack()
    {
        if (!planStarted && planPendingStart && fighter != null && fighter.enabled &&
            !fighter.IsInAttackLock && !fighter.IsInComboWindow)
        {
            planPendingStart = false;
            StartPlanFirstHit();
        }

        if (planStarted &&
            fighter != null && fighter.enabled &&
            !fighter.IsInAttackLock && !fighter.IsInComboWindow &&
            !planPendingStart)
        {
            EnterCooldownOrEngageAfterAttack();
        }
    }

    void StartPlanFirstHit()
    {
        if (fighter == null || !fighter.enabled) return;
        hitConfirmedCombos.Clear();
        planStarted = true;
        planQueuedAtComboIndex = 0;
        // ✅ 起手段数 = 1 走旧逻辑；>1 走新接口
        if (planStartCombo <= 1)
        {
            fighter.TryAttack(planAttackA, AttackMoveType.None);
        }
        else
        {
            // 如果该段不存在（没配 AttackConfig 或没动画 State），就兜底从 1 起手，避免卡死
            if (!fighter.TryStartNormalAt(planAttackA, planStartCombo))
                fighter.TryAttack(planAttackA, AttackMoveType.None);
        }
    }

    void TryQueueNormalCombo()
    {
        if (fighter == null || !fighter.enabled) return;
        if (fighter.CurrentAttackCategory != AttackCategory.Normal) return;
        if (fighter.CurrentIsAttackA != planAttackA) return;

        int cur = fighter.CurrentComboIndex;
        if (cur <= 0) return;

        if (cur < planTargetCombo && planQueuedAtComboIndex != cur)
        {
            if (!hitConfirmedCombos.Contains(cur))
                return;

            fighter.TryAttack(planAttackA, AttackMoveType.None);
            planQueuedAtComboIndex = cur;
        }
    }

    void EnterState(State s)
    {
        State prev = state;
        state = s;
        stateTimer = 0f;

        if (s == State.Engage)
        {
            nextEngageRunBurstRollTime = Time.time;
        }

        if (s == State.Block)
        {
            blockCameFromCooldown = (prev == State.Cooldown);
            blockReturnState = blockCameFromCooldown ? State.Cooldown : State.Engage;

            if (blockCameFromCooldown)
            {
                cachedCooldownInited = cooldownInited;
                cachedCooldownEndTime = cooldownEndTime;
                cachedCooldownPostureEndTime = cooldownPostureEndTime;
                cachedCooldownPosture = cooldownPosture;
            }
        }

        if (prev == State.Retreat && s != State.Retreat)
        {
            if (anim != null) anim.SetBool(AnimIsRetreating, false);
            RestoreRootMotionIfNeeded();
        }

        if (s == State.Retreat)
        {
            navigator.Stop();
            StopMove();

            ExitCooldownPosture();
            cooldownInited = false;

            if (anim != null) anim.SetBool(AnimIsRetreating, true);
            EnableRootMotionForRetreat();
        }

        if (s == State.Cooldown)
        {
            cooldownInited = false;
            ExitCooldownPosture();
        }

        if (s == State.Ability)
        {
            navigator.Stop();
            StopMove();
            ExitCooldownPosture();
            cooldownInited = false;
        }

        if (s != State.Attack)
            ResetPlan();
    }

    void EnterCooldownOrEngageAfterAttack()
    {
        float ch = cachedPlayerGuardBroken ? cooldownAfterAttackChanceWhenPlayerGuardBroken : cooldownAfterAttackChance;
        ch = Mathf.Clamp01(ch);

        if (Random.value <= ch)
            EnterState(State.Cooldown);
        else
            EnterState(State.Engage);
    }

    // ✅ 新 Cooldown：Idle / WalkBack / WalkLeft / WalkRight
    void UpdateCooldown(float distance, Vector3 toTarget, bool playerGuardBroken)
    {
        float cd = cooldownDuration;
        if (playerGuardBroken) cd = Mathf.Min(cd, pressureCooldownWhenPlayerGuardBroken);

        if (cd <= 0f)
        {
            ExitCooldownPosture();
            EnterState(State.Engage);
            return;
        }

        if (!cooldownInited)
        {
            cooldownInited = true;
            cooldownEndTime = Time.time + cd;
            PickNextCooldownPosture();
        }

        if (!playerGuardBroken && distance <= defenseDistance && ShouldStartBlock(distance))
        {
            EnterState(State.Block);
            StartBlock();
            return;
        }

        if (Time.time >= cooldownEndTime)
        {
            ExitCooldownPosture();
            EnterState(State.Engage);
            return;
        }

        if (!enableCooldownPostures)
        {
            ExitCooldownPosture();
            RotateToTarget(toTarget);
            StopMove();
            ResetCooldownMove2D();
            return;
        }

        if (Time.time >= cooldownPostureEndTime)
            PickNextCooldownPosture();

        // ✅ cooldown 不允许使用 IsRetreating（那是破防 Retreat 的专用动画）
        if (anim != null) anim.SetBool(AnimIsRetreating, false);
        RestoreRootMotionIfNeeded();

        if (cooldownPosture == CooldownPosture.Idle)
        {
            navigator.Stop();
            StopMove();
            RotateToTarget(toTarget);
            ResetCooldownMove2D();
            return;
        }

        // WalkBack/Left/Right：普通走（用 EnemyMove 移动），保持面向玩家
        navigator.Stop();
        RotateToTarget(toTarget);
        ApplyCooldownWalk(toTarget, cooldownPosture);
    }

    void PickNextCooldownPosture()
    {
        float minT = Mathf.Max(0.01f, cooldownPostureMinTime);
        float maxT = Mathf.Max(minT, cooldownPostureMaxTime);
        float dur = Random.Range(minT, maxT);
        cooldownPostureEndTime = Time.time + dur;

        float wIdle = Mathf.Max(0f, cooldownIdleWeight);
        float wBack = Mathf.Max(0f, cooldownWalkBackWeight);
        float wL = Mathf.Max(0f, cooldownWalkLeftWeight);
        float wR = Mathf.Max(0f, cooldownWalkRightWeight);
        float sum = wIdle + wBack + wL + wR;

        if (sum <= 0.0001f)
        {
            cooldownPosture = CooldownPosture.Idle;
            return;
        }

        float r = Random.value * sum;
        if (r < wIdle) cooldownPosture = CooldownPosture.Idle;
        else if ((r -= wIdle) < wBack) cooldownPosture = CooldownPosture.WalkBack;
        else if ((r -= wBack) < wL) cooldownPosture = CooldownPosture.WalkLeft;
        else cooldownPosture = CooldownPosture.WalkRight;

        // 一旦选了移动 posture，按你的设定：不混合、不对角
        if (cooldownPosture == CooldownPosture.Idle)
            ResetCooldownMove2D();
        else
            SetCooldownMove2D(cooldownPosture);
    }

    void ExitCooldownPosture()
    {
        // cooldown 自己永远不占用 IsRetreating
        if (anim != null)
            anim.SetBool(AnimIsRetreating, false);

        RestoreRootMotionIfNeeded();
        ResetCooldownMove2D();
    }

    void ReturnToCooldownAfterBlock()
    {
        EnterState(State.Cooldown);

        cooldownInited = cachedCooldownInited;
        cooldownEndTime = cachedCooldownEndTime;
        cooldownPostureEndTime = cachedCooldownPostureEndTime;
        cooldownPosture = cachedCooldownPosture;

        // 恢复 posture 对应的 MoveX/MoveY（如果你开启了 driveAnimatorMoveParamsInCooldown）
        if (cooldownPosture == CooldownPosture.Idle) ResetCooldownMove2D();
        else SetCooldownMove2D(cooldownPosture);
    }

    void CacheTargetRefs()
    {
        targetFighter = null;
        targetStats = null;
        if (target == null) return;

        targetFighter = target.GetComponentInParent<MeleeFighter>();
        if (targetFighter == null) targetFighter = target.GetComponentInChildren<MeleeFighter>();

        targetStats = target.GetComponentInParent<CombatStats>();
        if (targetStats == null) targetStats = target.GetComponentInChildren<CombatStats>();
    }

    void ResetPlan()
    {
        planIsHeavy = false;
        planAttackA = true;
        planHeavyAttackA = true;
        planTargetCombo = 1;
        planStartCombo = 1;
        planQueuedAtComboIndex = 0;
        planStarted = false;
        planPendingStart = false;
        hitConfirmedCombos.Clear();
    }

    void StopMove()
    {
        currentSpeedLevel = 0f;
        move.SetMoveDirection(Vector3.zero);
        move.SetMoveSpeedLevel(0);
    }

    void ApplyCooldownWalk(Vector3 toTarget, CooldownPosture posture)
    {
        // 用“面向玩家”的轴作为基准，保证左右/后退稳定
        Vector3 fwd = toTarget;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
        else fwd.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        Vector3 worldDir;
        switch (posture)
        {
            case CooldownPosture.WalkBack:
                worldDir = cooldownUseTargetBasis ? -fwd : -transform.forward;
                break;
            case CooldownPosture.WalkLeft:
                worldDir = cooldownUseTargetBasis ? -right : -transform.right;
                break;
            case CooldownPosture.WalkRight:
                worldDir = cooldownUseTargetBasis ? right : transform.right;
                break;
            default:
                worldDir = Vector3.zero;
                break;
        }

        // 走路速度（你的设计：walk = 1）
        currentSpeedLevel = Mathf.MoveTowards(currentSpeedLevel, walkSpeedLevel, speedLevelChangeRate * dt);
        move.SetMoveDirection(worldDir);
        move.SetMoveSpeedLevel(Mathf.RoundToInt(currentSpeedLevel));
    }

    void RotateToTarget(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion rot = Quaternion.LookRotation(dir);

        // ✅ 防止 dt*rotateSpeed > 1 直接 snap（抖动/跳转的常见诱因）
        float t = Mathf.Clamp01(dt * rotateSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, rot, t);
    }

    void SetCooldownMove2D(CooldownPosture posture)
    {
        if (!driveAnimatorMoveParamsInCooldown || anim == null) return;

        float x = 0f, y = 0f;
        switch (posture)
        {
            case CooldownPosture.WalkBack: x = 0f; y = -1f; break;
            case CooldownPosture.WalkLeft: x = -1f; y = 0f; break;
            case CooldownPosture.WalkRight: x = 1f; y = 0f; break;
        }

        anim.SetFloat(AnimMoveX, x);
        anim.SetFloat(AnimMoveY, y);
    }

    void ResetCooldownMove2D()
    {
        if (anim == null) return;

        // 就算你不开 driveAnimatorMoveParamsInCooldown，这里清零也不会造成“抢写抖动”
        // 因为 Idle 时一般没有其它脚本在疯狂写 MoveX/MoveY。
        if (!driveAnimatorMoveParamsInCooldown)
        {
            anim.SetFloat(AnimMoveX, 0f);
            anim.SetFloat(AnimMoveY, 0f);
            return;
        }

        anim.SetFloat(AnimMoveX, 0f);
        anim.SetFloat(AnimMoveY, 0f);
    }

    Vector3 GetTargetPoint()
    {
        return LockTargetPointUtility.GetCapsuleCenter(target);
    }

    void EnableRootMotionForRetreat()
    {
        if (anim == null) return;

        if (!cachedApplyRootMotionValid)
        {
            cachedApplyRootMotion = anim.applyRootMotion;
            cachedApplyRootMotionValid = true;
        }

        anim.applyRootMotion = true;
    }

    void RestoreRootMotionIfNeeded()
    {
        if (anim == null) return;
        if (!cachedApplyRootMotionValid) return;

        anim.applyRootMotion = cachedApplyRootMotion;
    }
}