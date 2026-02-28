using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(EnemyMove))]
[RequireComponent(typeof(EnemyNavigator))]
public class RangeCombat : MonoBehaviour, IEnemyCombat
{
    [Header("Speed Level (EnemyMove)")]
    public int walkSpeedLevel = 1;
    public int runSpeedLevel = 2;
    public int sprintSpeedLevel = 3; 

    [Header("Zones (Far -> Near)")]
    [Tooltip("<= this distance: switch to melee logic (Combat-like)")]
    public float meleeZoneDistance = 2f;

    [Tooltip("(meleeZoneDistance, bufferZoneDistance]: buffer zone (too close): shoot + cooldown w/ retreat bias")]
    public float bufferZoneDistance = 5f;

    [Tooltip("(bufferZoneDistance, shootZoneDistance]: shoot zone: shoot + cooldown")]
    public float shootZoneDistance = 20f;

    [Tooltip("Zone hysteresis to prevent thrashing when distance fluctuates around boundaries.")]
    public float zoneHysteresis = 0.6f;

    [Header("Retreat (Guard Break)")]
    [Tooltip("true=Retreat(破防/昏厥)期间锁定当前朝向(不再转向玩家)；false=保持原逻辑(持续朝向玩家)。默认 false")]
    public bool retreatLockTurn = false;

    [Header("Attack Facing Gate")]
    [Tooltip("攻击前要求目标位于前方扇形内（范围与 CombatReceiver 的 Directional Block 一致）。")]
    public bool requireTargetInFrontToAttack = true;

    [Header("Rotate")]
    public float rotateSpeed = 4f;
    [Header("Attack Pre-Hit Turn Tracking (Melee)")]
    public bool enablePreHitTurnTracking = true;
    [Range(0f, 180f)]
    public float preHitTrackMaxYawFromStart = 70f;

    Vector3 lastNavDir;

    [Header("Ranged Decision")]
    [Tooltip("AI roll interval for ranged decisions (shoot OR reposition).")]
    public float rangedDecisionMinInterval = 0.25f;
    public float rangedDecisionMaxInterval = 0.55f;

    [Range(0f, 1f)]
    public float shootChanceInShootZone = 0.75f;

    [Range(0f, 1f)]
    public float shootChanceInBufferZone = 0.5f;

    [Header("Ranged Cooldown (Shoot Zone)")]
    public float rangedShootCooldownDuration = 2f;

    public bool rangedShootEnablePostures = true;
    public float rangedShootPostureMinTime = 1f;
    public float rangedShootPostureMaxTime = 2f;

    public float rangedShootIdleWeight = 2f;
    public float rangedShootWalkBackWeight = 1f;
    public float rangedShootWalkLeftWeight = 2f;
    public float rangedShootWalkRightWeight = 2f;
    public float rangedShootWalkForwardWeight = 0f;

    [Header("Ranged Cooldown (Buffer Zone)")]
    public float rangedBufferCooldownDuration = 2f;

    public bool rangedBufferEnablePostures = true;
    public float rangedBufferPostureMinTime = 1f;
    public float rangedBufferPostureMaxTime = 2f;

    public float rangedBufferIdleWeight = 0.5f;
    public float rangedBufferWalkBackWeight = 4.0f;
    public float rangedBufferWalkLeftWeight = 1.0f;
    public float rangedBufferWalkRightWeight = 1.0f;
    public float rangedBufferWalkForwardWeight = 0f;

    // =========================
    // Melee section (Combat-like)
    // =========================

    [Header("Melee Engage (Only One Action Gate)")]
    [Tooltip("Within this distance: allow block/normal/heavy decision (Combat style).")]
    public float attackDecisionDistance = 1.0f;

    [Header("Normal Attacks (A1~A4 / B1~B4)")]
    [Range(0f, 1f)] public float preferAChance = 0.7f;
    [Range(0f, 1f)] public float preferAWhenPlayerGuardBroken = 1f;

    public int maxComboA = 4;
    public int maxComboB = 4;

    [Header("Flexible Combo Continue Chances (Optional)")]
    public List<float> comboContinueChancesA = new List<float>();
    public List<float> comboContinueChancesB = new List<float>();

    [Header("Random Start Combo (Enemy Only)")]
    public bool enableRandomStartComboIndex = true;

    // ✅ 仅用于“起手招不重复”（A/B + 起手段）
    bool hasLastNormalPlan;
    bool lastPlanAttackA;
    int lastPlanStartCombo;

    [Header("HeavyAttack (No Distance)")]
    public bool enableHeavyAttackA = true;
    public bool enableHeavyAttackB = false;
    [Range(0f, 1f)] public float heavyUseBChance = 0.25f;
    public float heavyCooldown = 5f;
    [Range(0f, 1f)] public float heavyChance = 0.1f;
    [Range(0f, 1f)] public float heavyChanceWhenPlayerGuardBroken = 0.6f;

    [Header("Defense (Block) (No Distance)")]
    public bool enableDefense = true;
    public float defenseDistance = 1.5f;
    [Range(0f, 1f)] public float blockChanceWhenPlayerAttacking = 1f;
    public float blockHoldMin = 1f;
    public float blockHoldMax = 2f;
    public float blockCooldown = 0f;
    public float stopBlockingWhenPlayerNotAttackingDelay = 0.5f;

    [Header("Melee Cooldown")]
    public float meleeCooldownDuration = 2f;
    public float pressureCooldownWhenPlayerGuardBroken = 0f;

    [Range(0f, 1f)] public float cooldownAfterAttackChance = 0.7f;
    [Range(0f, 1f)] public float cooldownAfterAttackChanceWhenPlayerGuardBroken = 0f;

    [Header("Melee Cooldown Postures (Idle / WalkBack / WalkLeft / WalkRight / WalkForward)")]
    public bool meleeEnableCooldownPostures = true;
    public float meleeCooldownPostureMinTime = 1f;
    public float meleeCooldownPostureMaxTime = 2f;

    public float meleeCooldownIdleWeight = 1f;
    public float meleeCooldownWalkBackWeight = 2f;
    public float meleeCooldownWalkLeftWeight = 1f;
    public float meleeCooldownWalkRightWeight = 1f;
    public float meleeCooldownWalkForwardWeight = 0f;

    [Header("Cooldown Strafe Setup")]
    public bool cooldownUseTargetBasis = true;
    public bool driveAnimatorMoveParamsInCooldown = false;

    [Header("Cooldown Safety Probe")]
    [Tooltip("Cooldown 侧移/后退前的前探距离（米）。")]
    public float cooldownProbeDistance = 0.8f;
    [Tooltip("NavMesh 采样半径（米）。")]
    public float cooldownProbeNavMeshRadius = 0.6f;

    [Header("Smoothing")]
    public float speedLevelChangeRate = 5f;
    // =========================
    // runtime refs
    // =========================

    EnemyMove move;
    EnemyNavigator navigator;
    EnemyController controller;
    Animator anim;
    CombatReceiver receiver;
    MeleeFighter meleeFighter;     // enemy melee weapon
    RangeFighter rangeFighter;     // enemy ranged weapon
    BlockController block;
    CombatStats selfStats;

    Transform target;
    MeleeFighter targetFighter;
    CombatStats targetStats;

    bool active;
    float dt;
    float stateTimer;
    float currentSpeedLevel;

    bool cachedPlayerGuardBroken;

    enum Zone { Approach, Shoot, Buffer, Melee }
    Zone zone = Zone.Approach;

    enum State { Approach, Shoot, RangedAttack, Engage, Block, Attack, Retreat, Cooldown }
    State state = State.Approach;

    // ranged decision gating
    float nextRangedDecisionTime;

    // melee plan runtime
    bool planIsHeavy;
    bool planAttackA;
    bool planHeavyAttackA = true;
    int planTargetCombo = 1;
    int planStartCombo = 1;
    int planQueuedAtComboIndex = 0;
    bool planStarted;
    bool planPendingStart;

    readonly HashSet<int> hitConfirmedCombos = new HashSet<int>();

    float nextHeavyAllowedTime;

    float blockReleaseTime;
    float nextBlockAllowedTime;
    float playerNotAttackingTimer;

    // cooldown runtime (shared)
    enum CooldownPosture { Idle, WalkBack, WalkLeft, WalkRight, WalkForward }
    CooldownPosture cooldownPosture = CooldownPosture.Idle;
    bool cooldownInited;
    float cooldownEndTime;
    float cooldownPostureEndTime;

    enum CooldownContext { Melee, ShootZone, BufferZone }
    CooldownContext cooldownContext = CooldownContext.Melee;
    State cooldownReturnState = State.Engage;

    // block return policy (melee only)
    State blockReturnState = State.Engage;
    bool blockCameFromCooldown;

    bool cachedCooldownInited;
    float cachedCooldownEndTime;
    float cachedCooldownPostureEndTime;
    CooldownPosture cachedCooldownPosture;

    // retreat root motion cache
    static readonly int AnimIsRetreating = Animator.StringToHash("IsRetreating");
    static readonly int AnimMoveX = Animator.StringToHash("MoveX");
    static readonly int AnimMoveY = Animator.StringToHash("MoveY");

    bool prevMeleeAttackLock;
    float meleeAttackStartYaw;

    bool pendingPreHitTurn;
    Vector3 pendingPreHitDir;
    bool pendingRangedTurn;
    Vector3 pendingRangedTurnDir;

    bool cachedApplyRootMotion;
    bool cachedApplyRootMotionValid;

    void Awake()
    {
        move = GetComponent<EnemyMove>();
        navigator = GetComponent<EnemyNavigator>();
        controller = GetComponent<EnemyController>();
        anim = GetComponent<Animator>();
        receiver = GetComponent<CombatReceiver>();
        meleeFighter = GetComponent<MeleeFighter>();
        rangeFighter = GetComponent<RangeFighter>();

        block = GetComponent<BlockController>();
        selfStats = GetComponent<CombatStats>();
    }

    void OnEnable()
    {
        if (meleeFighter == null) meleeFighter = GetComponent<MeleeFighter>();
        if (meleeFighter != null)
            meleeFighter.OnHitLanded += HandleHitLanded;
    }

    void OnDisable()
    {
        if (meleeFighter != null)
            meleeFighter.OnHitLanded -= HandleHitLanded;
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

        state = State.Approach;
        zone = Zone.Approach;

        ResetPlan();

        cooldownInited = false;
        ExitCooldownPosture();

        nextRangedDecisionTime = Time.time + Random.Range(rangedDecisionMinInterval, Mathf.Max(rangedDecisionMinInterval, rangedDecisionMaxInterval));

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

        ResetPlan();

        if (block != null)
            block.RequestBlock(false);

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

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        CacheTargetRefs();
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

        if (controller != null && (controller.IsAirborne || controller.IsInLandLock))
        {
            StopMove();
            navigator.Stop();
            if (block != null) block.RequestBlock(false);
            return;
        }

        if (targetFighter == null || targetStats == null) CacheTargetRefs();

        Vector3 toTarget3D = GetTargetPoint() - transform.position;
        float distanceXYZ = toTarget3D.magnitude;
        float distanceY = Mathf.Abs(toTarget3D.y);

        Vector3 toTarget = toTarget3D;  // ✅ 这个 toTarget 仍然用于转向/距离（平面）
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        bool withinAttackDistance3D = (distanceXYZ <= attackDecisionDistance) && (distanceY <= attackDecisionDistance);

        navigator.SyncPosition(transform.position);

        bool playerGuardBroken = (targetStats != null) && targetStats.IsGuardBroken;
        cachedPlayerGuardBroken = playerGuardBroken;

        // zone update with hysteresis
        zone = UpdateZone(zone, distance);        // self retreat (guard broken) has highest priority
        bool retreatActive = (selfStats != null) && selfStats.IsGuardBroken;
        if (retreatActive)
        {
            if (block != null) block.RequestBlock(false);

            navigator.Stop();
            StopMove();

            if (!retreatLockTurn)
                RotateToTarget(toTarget);

            if (state != State.Retreat)
                EnterState(State.Retreat);

            return;
        }
        else if (state == State.Retreat)
        {
            // leave retreat immediately
            EnterState(State.Cooldown);
            cooldownContext = CooldownContext.Melee;
            cooldownReturnState = State.Engage;
        }

        // motion locks (event authority)
        bool meleeMotionLock = (meleeFighter != null && meleeFighter.enabled &&
                               (meleeFighter.IsInAttackLock || meleeFighter.IsInComboWindow));
        bool rangedMotionLock = (rangeFighter != null && rangeFighter.enabled && rangeFighter.IsInAttackLock);
        // ✅ 记录“近战攻击起始朝向”（必须放在 motionLock return 之前）
        bool meleeAttackLockNow = (meleeFighter != null && meleeFighter.enabled && meleeFighter.IsInAttackLock);
        if (meleeAttackLockNow && !prevMeleeAttackLock)
            meleeAttackStartYaw = transform.eulerAngles.y;
        prevMeleeAttackLock = meleeAttackLockNow;

        if (meleeMotionLock || rangedMotionLock)
        {
            StopMove();

            // ✅ 远程射击：射击动画期间也要持续朝向目标
            if (rangedMotionLock && rangeFighter != null && rangeFighter.enabled)
            {
                pendingRangedTurnDir = toTarget;   // toTarget 已经是平面向量（y=0）
                pendingRangedTurn = true;
            }

            // ✅ 近战：前摇追踪（AttackBegin 前）；命中窗口锁死（LateUpdate 会再判一次）
            if (enablePreHitTurnTracking &&
                meleeFighter != null && meleeFighter.enabled &&
                meleeFighter.IsInAttackLock && !meleeFighter.IsInHitWindow)
            {
                QueuePreHitTurn(toTarget);
            }

            if (meleeMotionLock && state == State.Attack && !planIsHeavy)
                TryQueueNormalCombo();

            return;
        }

        stateTimer += dt;

        // force state by zone when not locked
        switch (zone)
        {
            case Zone.Approach:
                if (state != State.Approach)
                {
                    if (block != null) block.RequestBlock(false);
                    ExitCooldownPosture();
                    cooldownInited = false;
                    ResetPlan();
                    EnterState(State.Approach);
                }

                UpdateApproach(toTarget);

                if (lastNavDir.sqrMagnitude > 0.0001f)
                    RotateToTarget(lastNavDir);
                else
                    RotateToTarget(toTarget);

                return;

            case Zone.Shoot:
            case Zone.Buffer:
                if (state == State.Engage || state == State.Block || state == State.Attack || state == State.Retreat)
                {
                    if (block != null) block.RequestBlock(false);
                    ResetPlan();
                    EnterState(State.Shoot);
                }

                UpdateRanged(zone, toTarget, distance, withinAttackDistance3D);
                return;

            case Zone.Melee:
                if (state == State.Approach || state == State.Shoot || state == State.RangedAttack)
                {
                    if (block != null) block.RequestBlock(false);
                    ExitCooldownPosture();
                    cooldownInited = false;
                    ResetPlan();
                    EnterState(State.Engage);
                }

                UpdateMelee(distance, toTarget, playerGuardBroken, withinAttackDistance3D);
                return;
        }
    }

    // =========================
    // Zone logic
    // =========================

    Zone GetRawZone(float distance)
    {
        if (distance <= meleeZoneDistance) return Zone.Melee;
        if (distance <= bufferZoneDistance) return Zone.Buffer;
        if (distance <= shootZoneDistance) return Zone.Shoot;
        return Zone.Approach;
    }

    Zone UpdateZone(Zone current, float distance)
    {
        Zone raw = GetRawZone(distance);
        if (raw == current) return current;

        float h = Mathf.Max(0f, zoneHysteresis);

        switch (current)
        {
            case Zone.Approach:
                if (distance <= shootZoneDistance - h) return raw;
                return Zone.Approach;

            case Zone.Shoot:
                if (raw == Zone.Approach)
                {
                    if (distance >= shootZoneDistance + h) return Zone.Approach;
                    return Zone.Shoot;
                }
                if (distance <= bufferZoneDistance - h) return raw;
                return Zone.Shoot;

            case Zone.Buffer:
                if (raw == Zone.Shoot)
                {
                    if (distance >= bufferZoneDistance + h) return Zone.Shoot;
                    return Zone.Buffer;
                }
                if (raw == Zone.Melee)
                {
                    if (distance <= meleeZoneDistance - h) return Zone.Melee;
                    return Zone.Buffer;
                }
                return Zone.Buffer;

            case Zone.Melee:
                if (raw != Zone.Melee)
                {
                    if (distance >= meleeZoneDistance + h) return raw;
                    return Zone.Melee;
                }
                return Zone.Melee;
        }

        return raw;
    }

    // =========================
    // Ranged
    // =========================

    void UpdateApproach(Vector3 toTarget)
    {
        navigator.SetTarget(GetTargetPoint());

        Vector3 dir = navigator.GetMoveDirection();
        if (dir == Vector3.zero)
            dir = (toTarget.sqrMagnitude < 0.0001f) ? transform.forward : toTarget.normalized;
        lastNavDir = dir;

        currentSpeedLevel = Mathf.MoveTowards(currentSpeedLevel, sprintSpeedLevel, speedLevelChangeRate * dt);
        move.SetMoveDirection(dir);
        move.SetMoveSpeedLevel(Mathf.RoundToInt(currentSpeedLevel));
    }

    void UpdateRanged(Zone z, Vector3 toTarget, float distance, bool withinAttackDistance3D)
    {
        if (state == State.Cooldown)
        {
            cooldownContext = (z == Zone.Buffer) ? CooldownContext.BufferZone : CooldownContext.ShootZone;
            cooldownReturnState = State.Shoot;

            UpdateCooldown(distance, toTarget, cachedPlayerGuardBroken);
            return;
        }
        if (state == State.RangedAttack)
        {
            navigator.Stop();
            StopMove();
            RotateToTarget(toTarget);

            if (rangeFighter == null || !rangeFighter.enabled || !rangeFighter.IsInAttackLock)
            {
                EnterRangedCooldown(z);
            }
            return;
        }

        if (state != State.Shoot)
            EnterState(State.Shoot);

        navigator.Stop();
        StopMove();
        RotateToTarget(toTarget);
        if (!CanStartAttackFacingGate())
            return;
        if (Time.time < nextRangedDecisionTime)
            return;

        float minI = Mathf.Max(0.05f, rangedDecisionMinInterval);
        float maxI = Mathf.Max(minI, rangedDecisionMaxInterval);
        nextRangedDecisionTime = Time.time + Random.Range(minI, maxI);

        float shootChance = (z == Zone.Buffer) ? shootChanceInBufferZone : shootChanceInShootZone;
        shootChance = Mathf.Clamp01(shootChance);

        bool doShoot = (rangeFighter != null && rangeFighter.enabled) && (Random.value <= shootChance);

        if (doShoot)
        {
            Vector3 aim3D = GetAimDir3D(); // ✅ 不抹 y：从枪口指向目标胶囊中心
            if (rangeFighter.TryShoot(aim3D, target))
            {
                EnterState(State.RangedAttack);
                return;
            }
        }

        EnterRangedCooldown(z);
    }

    void EnterRangedCooldown(Zone z)
    {
        cooldownContext = (z == Zone.Buffer) ? CooldownContext.BufferZone : CooldownContext.ShootZone;
        cooldownReturnState = State.Shoot;
        EnterState(State.Cooldown);
    }

    // =========================
    // Melee (Combat-like)
    // =========================

    void UpdateMelee(float distance, Vector3 toTarget, bool playerGuardBroken, bool withinAttackDistance3D)
    {
        if (playerGuardBroken && block != null)
            block.RequestBlock(false);

        switch (state)
        {
            case State.Engage:
                UpdateMeleeEngage(distance, toTarget, playerGuardBroken, withinAttackDistance3D);
                break;
            case State.Block:
                UpdateBlock(distance);
                break;
            case State.Attack:
                UpdateAttack();
                break;
            case State.Cooldown:
                cooldownContext = CooldownContext.Melee;
                cooldownReturnState = State.Engage;
                UpdateCooldown(distance, toTarget, playerGuardBroken);
                break;
            default:
                EnterState(State.Engage);
                break;
        }

        // ✅ 非 motion lock：正常转向（近战贴近绕障时朝向 lastNavDir）
        bool approachingForward = (state == State.Engage && distance > attackDecisionDistance);

        if (approachingForward && lastNavDir.sqrMagnitude > 0.0001f)
            RotateToTarget(lastNavDir);
        else
            RotateToTarget(toTarget);
    }

    void UpdateMeleeEngage(float distance, Vector3 toTarget, bool playerGuardBroken, bool withinAttackDistance3D)
    {
        if (!playerGuardBroken && distance <= defenseDistance && ShouldStartBlock(distance))
        {
            EnterState(State.Block);
            StartBlock();
            return;
        }

        if (distance > attackDecisionDistance || !withinAttackDistance3D)
        {
            WalkApproach(toTarget);
            return;
        }

        StopMove();

        if (!CanStartAttackFacingGate())
            return;

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

        if (!withinAttackDistance3D)
            return;

        StartNormalPlan(playerGuardBroken);
        EnterState(State.Attack);
    }   
    
    void WalkApproach(Vector3 toTarget)
    {
        navigator.SetTarget(GetTargetPoint());

        Vector3 dir = navigator.GetMoveDirection();
        if (dir == Vector3.zero) dir = (toTarget.sqrMagnitude < 0.0001f) ? transform.forward : toTarget.normalized;
        lastNavDir = dir;

        currentSpeedLevel = Mathf.MoveTowards(currentSpeedLevel, walkSpeedLevel, speedLevelChangeRate * dt);

        move.SetMoveDirection(dir);
        move.SetMoveSpeedLevel(Mathf.RoundToInt(currentSpeedLevel));
    }

    // ✅ run / sprint 时不允许防御（这里 sprint 等价为跑动速度等级>=runSpeedLevel）
    bool IsRunningOrSprintingNow()
    {
        if (state == State.Approach) return true;
        return Mathf.RoundToInt(currentSpeedLevel) >= runSpeedLevel;
    }

    bool ShouldStartBlock(float distance)
    {
        if (!enableDefense || block == null) return false;
        if (distance > defenseDistance) return false;
        if (selfStats != null && (selfStats.IsGuardBroken || !selfStats.CanBlock)) return false;
        if (Time.time < nextBlockAllowedTime) return false;

        // ✅ Run / Sprint 时不进入防御
        if (IsRunningOrSprintingNow()) return false;

        bool playerAttacking = (targetFighter != null) && targetFighter.IsInAttackLock;
        if (!playerAttacking) return false;

        if (meleeFighter != null && meleeFighter.enabled && meleeFighter.IsInAttackLock) return false;

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
            EnterMeleeCooldown();
            return;
        }

        if (selfStats != null && (selfStats.IsGuardBroken || !selfStats.CanBlock))
        {
            block.RequestBlock(false);
            nextBlockAllowedTime = Time.time + blockCooldown;
            EnterMeleeCooldown();
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
        }
    }

    bool ShouldStartHeavy(bool playerGuardBroken, out bool heavyIsA)
    {
        heavyIsA = true;

        bool canA = enableHeavyAttackA;
        bool canB = enableHeavyAttackB;
        if (!canA && !canB) return false;

        if (meleeFighter == null || !meleeFighter.enabled) return false;
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
        if (meleeFighter == null || !meleeFighter.enabled) return;

        nextHeavyAllowedTime = Time.time + heavyCooldown;

        ResetPlan();
        planIsHeavy = true;
        planHeavyAttackA = heavyIsA;
        planStarted = true;

        meleeFighter.RequestHeavy(planHeavyAttackA);
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

        if (meleeFighter != null && meleeFighter.enabled)
        {
            int fighterMax = attackA ? meleeFighter.MaxNormalComboA : meleeFighter.MaxNormalComboB;
            m = Mathf.Min(m, Mathf.Max(1, fighterMax));
        }

        return m;
    }

    // ✅ 仅避免“起手招”连续相同（A/B + 起手段）
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
            planStartCombo = enableRandomStartComboIndex ? Random.Range(1, m + 1) : 1;

            bool sameOpenerAsLast =
                hasLastNormalPlan &&
                planAttackA == lastPlanAttackA &&
                planStartCombo == lastPlanStartCombo;

            if (!sameOpenerAsLast || reroll >= maxReroll)
                break;

            reroll++;
        }

        // 目标段照旧（不参与防重复要求）
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

        if (meleeFighter != null && meleeFighter.enabled && !meleeFighter.IsInAttackLock && !meleeFighter.IsInComboWindow)
            StartPlanFirstHit();
        else
            planPendingStart = true;

        hasLastNormalPlan = true;
        lastPlanAttackA = planAttackA;
        lastPlanStartCombo = planStartCombo;

        StopMove();
    }

    void UpdateAttack()
    {
        if (!planStarted && planPendingStart && meleeFighter != null && meleeFighter.enabled &&
            !meleeFighter.IsInAttackLock && !meleeFighter.IsInComboWindow)
        {
            planPendingStart = false;
            StartPlanFirstHit();
        }

        if (planStarted &&
            meleeFighter != null && meleeFighter.enabled &&
            !meleeFighter.IsInAttackLock && !meleeFighter.IsInComboWindow &&
            !planPendingStart)
        {
            EnterCooldownOrEngageAfterAttack();
        }
    }

    void StartPlanFirstHit()
    {
        if (meleeFighter == null || !meleeFighter.enabled) return;

        hitConfirmedCombos.Clear();
        planStarted = true;
        planQueuedAtComboIndex = 0;

        if (planStartCombo <= 1)
        {
            meleeFighter.TryAttack(planAttackA, AttackMoveType.None);
        }
        else
        {
            if (!meleeFighter.TryStartNormalAt(planAttackA, planStartCombo))
                meleeFighter.TryAttack(planAttackA, AttackMoveType.None);
        }
    }

    void TryQueueNormalCombo()
    {
        if (meleeFighter == null || !meleeFighter.enabled) return;
        if (meleeFighter.CurrentAttackCategory != AttackCategory.Normal) return;
        if (meleeFighter.CurrentIsAttackA != planAttackA) return;

        int cur = meleeFighter.CurrentComboIndex;
        if (cur <= 0) return;

        if (cur < planTargetCombo && planQueuedAtComboIndex != cur)
        {
            if (!hitConfirmedCombos.Contains(cur))
                return;

            meleeFighter.TryAttack(planAttackA, AttackMoveType.None);
            planQueuedAtComboIndex = cur;
        }
    }

    void HandleHitLanded(AttackData data)
    {
        if (state != State.Attack) return;
        if (planIsHeavy) return;
        if (meleeFighter == null || !meleeFighter.enabled) return;
        if (meleeFighter.CurrentAttackCategory != AttackCategory.Normal) return;

        int cur = meleeFighter.CurrentComboIndex;
        if (cur > 0)
            hitConfirmedCombos.Add(cur);
    }

    void EnterCooldownOrEngageAfterAttack()
    {
        float ch = cachedPlayerGuardBroken ? cooldownAfterAttackChanceWhenPlayerGuardBroken : cooldownAfterAttackChance;
        ch = Mathf.Clamp01(ch);

        if (Random.value <= ch)
            EnterMeleeCooldown();
        else
            EnterState(State.Engage);
    }

    void EnterMeleeCooldown()
    {
        cooldownContext = CooldownContext.Melee;
        cooldownReturnState = State.Engage;
        EnterState(State.Cooldown);
    }

    // =========================
    // State core
    // =========================

    void EnterState(State s)
    {
        State prev = state;
        state = s;
        stateTimer = 0f;

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

            if (block != null) block.RequestBlock(false);
        }

        if (s == State.Cooldown)
        {
            cooldownInited = false;
            ExitCooldownPosture();
        }        // only keep plan when actively in melee Attack state
        if (s != State.Attack)
            ResetPlan();
    }

    void ReturnToCooldownAfterBlock()
    {
        EnterState(State.Cooldown);

        cooldownInited = cachedCooldownInited;
        cooldownEndTime = cachedCooldownEndTime;
        cooldownPostureEndTime = cachedCooldownPostureEndTime;
        cooldownPosture = cachedCooldownPosture;

        if (cooldownPosture == CooldownPosture.Idle) ResetCooldownMove2D();
        else SetCooldownMove2D(cooldownPosture);
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

    // =========================
    // Cooldown (shared)
    // =========================

    void UpdateCooldown(float distance, Vector3 toTarget, bool playerGuardBroken)
    {
        float cd;
        bool enablePostures;
        float postureMin;
        float postureMax;
        float wIdle, wBack, wL, wR, wF;

        if (cooldownContext == CooldownContext.Melee)
        {
            cd = meleeCooldownDuration;
            if (playerGuardBroken) cd = Mathf.Min(cd, pressureCooldownWhenPlayerGuardBroken);

            enablePostures = meleeEnableCooldownPostures;
            postureMin = meleeCooldownPostureMinTime;
            postureMax = meleeCooldownPostureMaxTime;

            wIdle = meleeCooldownIdleWeight;
            wBack = meleeCooldownWalkBackWeight;
            wL = meleeCooldownWalkLeftWeight;
            wR = meleeCooldownWalkRightWeight;
            wF = meleeCooldownWalkForwardWeight;
        }
        else if (cooldownContext == CooldownContext.BufferZone)
        {
            cd = rangedBufferCooldownDuration;

            enablePostures = rangedBufferEnablePostures;
            postureMin = rangedBufferPostureMinTime;
            postureMax = rangedBufferPostureMaxTime;

            wIdle = rangedBufferIdleWeight;
            wBack = rangedBufferWalkBackWeight;
            wL = rangedBufferWalkLeftWeight;
            wR = rangedBufferWalkRightWeight;
            wF = rangedBufferWalkForwardWeight;
        }
        else
        {
            cd = rangedShootCooldownDuration;

            enablePostures = rangedShootEnablePostures;
            postureMin = rangedShootPostureMinTime;
            postureMax = rangedShootPostureMaxTime;

            wIdle = rangedShootIdleWeight;
            wBack = rangedShootWalkBackWeight;
            wL = rangedShootWalkLeftWeight;
            wR = rangedShootWalkRightWeight;
            wF = rangedShootWalkForwardWeight;
        }

        cd = Mathf.Max(0f, cd);

        if (cd <= 0f)
        {
            ExitCooldownPosture();
            EnterState(cooldownReturnState);
            return;
        }

        if (!cooldownInited)
        {
            cooldownInited = true;
            cooldownEndTime = Time.time + cd;
            PickNextCooldownPosture(postureMin, postureMax, wIdle, wBack, wL, wR, wF, toTarget);
        }

        if (cooldownContext == CooldownContext.Melee
            && cooldownPosture == CooldownPosture.WalkForward
            && distance <= attackDecisionDistance)
        {
            ExitCooldownPosture();
            EnterState(State.Engage);
            return;
        }

        if (Time.time >= cooldownEndTime)
        {
            ExitCooldownPosture();
            EnterState(cooldownReturnState);
            return;
        }

        if (!enablePostures)
        {
            ExitCooldownPosture();
            RotateToTarget(toTarget);
            StopMove();
            ResetCooldownMove2D();
            return;
        }

        if (Time.time >= cooldownPostureEndTime)
            PickNextCooldownPosture(postureMin, postureMax, wIdle, wBack, wL, wR, wF, toTarget);

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

        navigator.Stop();
        RotateToTarget(toTarget);
        ApplyCooldownWalk(toTarget, cooldownPosture);
    }

    void PickNextCooldownPosture(float postureMin, float postureMax, float wIdle, float wBack, float wL, float wR, float wF, Vector3 toTarget)
    {
        float minT = Mathf.Max(0.01f, postureMin);
        float maxT = Mathf.Max(minT, postureMax);
        float dur = Random.Range(minT, maxT);
        cooldownPostureEndTime = Time.time + dur;

        wIdle = Mathf.Max(0f, wIdle);
        wBack = Mathf.Max(0f, wBack);
        wL = Mathf.Max(0f, wL);
        wR = Mathf.Max(0f, wR);
        wF = Mathf.Max(0f, wF);

        float safeBack = IsCooldownPostureSafe(CooldownPosture.WalkBack, toTarget) ? wBack : 0f;
        float safeLeft = IsCooldownPostureSafe(CooldownPosture.WalkLeft, toTarget) ? wL : 0f;
        float safeRight = IsCooldownPostureSafe(CooldownPosture.WalkRight, toTarget) ? wR : 0f;
        float safeForward = IsCooldownPostureSafe(CooldownPosture.WalkForward, toTarget) ? wF : 0f;

        float sum = wIdle + safeBack + safeLeft + safeRight + safeForward;
        if (sum <= 0.0001f)
        {
            cooldownPosture = CooldownPosture.Idle;
            ResetCooldownMove2D();
            return;
        }

        float r = Random.value * sum;
        if (r < wIdle) cooldownPosture = CooldownPosture.Idle;
        else if ((r -= wIdle) < safeBack) cooldownPosture = CooldownPosture.WalkBack;
        else if ((r -= safeBack) < safeLeft) cooldownPosture = CooldownPosture.WalkLeft;
        else if ((r -= safeLeft) < safeRight) cooldownPosture = CooldownPosture.WalkRight;
        else cooldownPosture = CooldownPosture.WalkForward;

        if (cooldownPosture == CooldownPosture.Idle)
            ResetCooldownMove2D();
        else
            SetCooldownMove2D(cooldownPosture);
    }

    bool IsCooldownPostureSafe(CooldownPosture posture, Vector3 toTarget)
    {
        Vector3 worldDir = GetCooldownWorldDirection(toTarget, posture);
        if (worldDir.sqrMagnitude < 0.0001f)
            return posture == CooldownPosture.Idle;

        float probeDistance = Mathf.Max(0.1f, cooldownProbeDistance);
        float sampleRadius = Mathf.Max(0.1f, cooldownProbeNavMeshRadius);

        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit fromHit, sampleRadius, NavMesh.AllAreas))
            return false;

        Vector3 probePos = transform.position + worldDir * probeDistance;
        if (!NavMesh.SamplePosition(probePos, out NavMeshHit toHit, sampleRadius, NavMesh.AllAreas))
            return false;

        return !NavMesh.Raycast(fromHit.position, toHit.position, out _, NavMesh.AllAreas);
    }

    Vector3 GetCooldownWorldDirection(Vector3 toTarget, CooldownPosture posture)
    {
        Vector3 fwd = toTarget;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
        else fwd.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        return posture switch
        {
            CooldownPosture.WalkBack => cooldownUseTargetBasis ? -fwd : -transform.forward,
            CooldownPosture.WalkLeft => cooldownUseTargetBasis ? -right : -transform.right,
            CooldownPosture.WalkRight => cooldownUseTargetBasis ? right : transform.right,
            CooldownPosture.WalkForward => cooldownUseTargetBasis ? fwd : transform.forward,
            _ => Vector3.zero
        };
    }

    void ApplyCooldownWalk(Vector3 toTarget, CooldownPosture posture)
    {
        Vector3 worldDir = GetCooldownWorldDirection(toTarget, posture);

        currentSpeedLevel = Mathf.MoveTowards(currentSpeedLevel, walkSpeedLevel, speedLevelChangeRate * dt);
        move.SetMoveDirection(worldDir);
        move.SetMoveSpeedLevel(Mathf.RoundToInt(currentSpeedLevel));
    }

    void ExitCooldownPosture()
    {
        if (anim != null)
            anim.SetBool(AnimIsRetreating, false);

        RestoreRootMotionIfNeeded();
        ResetCooldownMove2D();
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
            case CooldownPosture.WalkForward: x = 0f; y = 1f; break;
        }

        anim.SetFloat(AnimMoveX, x);
        anim.SetFloat(AnimMoveY, y);
    }

    void ResetCooldownMove2D()
    {
        if (anim == null) return;
        anim.SetFloat(AnimMoveX, 0f);
        anim.SetFloat(AnimMoveY, 0f);
    }

    // =========================
    // Helpers
    // =========================
    Vector3 GetMuzzlePos()
    {
        if (rangeFighter != null && rangeFighter.Muzzle != null)
            return rangeFighter.Muzzle.position;

        // 没 muzzle 就用一个大概胸口高度兜底
        return transform.position + Vector3.up * 1.4f;
    }

    Vector3 GetTargetPoint()
    {
        return LockTargetPointUtility.GetCapsuleCenter(target);
    }

    Vector3 GetAimDir3D()
    {
        Vector3 from = GetMuzzlePos();
        Vector3 to = target != null ? GetTargetPoint() : (transform.position + transform.forward);
        Vector3 dir = to - from;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
        return dir.normalized;
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

    void StopMove()
    {
        currentSpeedLevel = 0f;
        lastNavDir = Vector3.zero;
        move.SetMoveDirection(Vector3.zero);
        move.SetMoveSpeedLevel(0);
    }

    bool CanStartAttackFacingGate()
    {
        if (!requireTargetInFrontToAttack) return true;
        if (receiver == null) return true;
        if (!receiver.DirectionalBlockEnabled) return true;

        return receiver.IsWorldPointInFront(GetTargetPoint());
    }

    void RotateToTarget(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion rot = Quaternion.LookRotation(dir);
        float t = Mathf.Clamp01(dt * rotateSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, rot, t);
    }
    void QueuePreHitTurn(Vector3 toTarget)
    {
        if (toTarget.sqrMagnitude < 0.0001f) return;

        float desiredYaw = Quaternion.LookRotation(toTarget).eulerAngles.y;

        float max = Mathf.Clamp(preHitTrackMaxYawFromStart, 0f, 180f);
        float delta = Mathf.DeltaAngle(meleeAttackStartYaw, desiredYaw);
        delta = Mathf.Clamp(delta, -max, max);

        float clampedYaw = meleeAttackStartYaw + delta;

        pendingPreHitDir = Quaternion.Euler(0f, clampedYaw, 0f) * Vector3.forward;
        pendingPreHitTurn = true;
    }

    void LateUpdate()
    {
        if (!active)
        {
            pendingPreHitTurn = false;
            pendingRangedTurn = false;
            return;
        }

        // ✅ 优先级：近战前摇修正（只在 !HitWindow 时允许）
        if (pendingPreHitTurn)
        {
            if (meleeFighter != null && meleeFighter.enabled && meleeFighter.IsInHitWindow)
            {
                pendingPreHitTurn = false;
            }
            else
            {
                RotateToTarget(pendingPreHitDir); // 复用 rotateSpeed
                pendingPreHitTurn = false;
            }

            // 近战/远程不会同时锁，但这里顺便清掉
            pendingRangedTurn = false;
            return;
        }

        // ✅ 远程射击：射击动画期间持续对脸
        if (pendingRangedTurn)
        {
            RotateToTarget(pendingRangedTurnDir); // 复用 rotateSpeed
            pendingRangedTurn = false;
        }
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
