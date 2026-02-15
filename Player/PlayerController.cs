using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    public KeyCode toggleSwordKey = KeyCode.F;
    public KeyCode rollKey = KeyCode.LeftAlt;
    public KeyCode blockKey = KeyCode.Mouse1;
    public KeyCode lockOnKey = KeyCode.G;
    public KeyCode switchLockLeftKey = KeyCode.Z;
    public KeyCode switchLockRightKey = KeyCode.X;
    public KeyCode runKey = KeyCode.LeftShift;
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode takedownKey = KeyCode.V;
    public KeyCode crouchKey = KeyCode.C;
    public KeyCode savePointInteractKey = KeyCode.E;
    public KeyCode attackAKey = KeyCode.Mouse0;
    public KeyCode attackBKey = KeyCode.E;

    [Header("Ability Keys")]
    public KeyCode ability1Key = KeyCode.Alpha1;
    public KeyCode ability2Key = KeyCode.Alpha2;
    public KeyCode ability3Key = KeyCode.Alpha3;
    public KeyCode ability4Key = KeyCode.H;
    public KeyCode helicopterKey = KeyCode.Y;

    [Header("Ability Triggers (Animator)")]
    [SerializeField] string ability1Trigger = "Ability1";
    [SerializeField] string ability2Trigger = "Ability2";
    [SerializeField] string ability3Trigger = "Ability3";
    [SerializeField] string ability4Trigger = "Ability4";

    [Header("Mini Drone Fire")]
    public KeyCode miniDroneFireKey = KeyCode.Q;
    [SerializeField] MiniAssistDroneController miniDrone;

    Animator anim;
    SwordController sword;
    MeleeFighter fighter;
    PlayerMove move;
    BlockController block;
    LockOnSystem lockOn;
    ThirdPersonShoulderCamera cam;
    PlayerStaminaActions staminaActions;
    PlayerAbilitySystem abilitySystem;
    PlayerDroneSummoner droneSummoner;
    CombatReceiver receiver; // ✅ 受击锁来源

    bool isBusy;
    bool isAttacking;
    bool isRolling;
    bool isDodging;
    bool isLanding;
    bool isBlocking;
    bool isAbility;
    bool isCheckpointFlow;

    Transform currentSavePointRoot;

    // ✅ crouch runtime
    bool isCrouching;

    // ✅ 给 PlayerMove 读取（胶囊切换真相）
    public bool IsCrouching => isCrouching;

    enum DodgeDir
    {
        Forward = 0,
        Back = 1,
        Left = 2,
        Right = 3
    }

    bool isHoldingAttack;
    bool heavyRequested;
    float holdTimer;
    const float HEAVY_THRESHOLD = 0.35f;

    Vector3 rollDirection;

    // Animator Hash
    int hashIsLocked;
    int hashLockMoveX;
    int hashLockMoveY;

    // ✅ crouch hash
    int hashIsCrouching;

    // ✅ AbilityLayer 强制打断相关
    [Header("Ability Layer Interrupt")]
    [SerializeField] string abilityLayerName = "AbilityLayer";   // 改成你 Animator 里 Ability Layer 的名字
    [SerializeField] string abilityEmptyStateName = "Empty";      // Ability Layer 的空状态名
    int abilityLayerIndex = -1;
    bool wasInHitLock;

    // ✅ Roll/Land 兜底回收（防止滚出平台/打断导致事件丢失）
    [Header("Roll/Land Fallback")]
    [Tooltip("落地锁事件丢失时的兜底超时（秒）。")]
    [SerializeField] float landingLockTimeout = 1.2f;
    float landingLockStartTime = -999f;

    [Header("Roll / Dodge Input")]
    [Tooltip("按住翻滚键超过该时长触发翻滚；短于该时长在松开时触发躲避。")]
    [SerializeField] float rollHoldThreshold = 0.18f;
    bool rollKeyHolding;
    float rollKeyHoldStartTime;
    bool rollActionTriggered;

    [Header("Attack Magnet (Player)")]
    [Range(0f, 180f)]
    [SerializeField] float attackMagnetMaxYawFromStart = 90f; // 前摇最多纠正角度（建议 60~120）

    bool prevFighterAttackLock;
    float attackStartYaw;

    public bool IsLocked => lockOn != null && lockOn.IsLocked;
    // ✅ 受击锁（CombatReceiver 权威）
    public bool IsInHitLock => receiver != null && receiver.IsInHitLock;

    /// <summary>
    /// “行为锁”（用于屏蔽某些输入），不包含 Block（允许边移动边防御），但包含受击锁。
    /// </summary>
    public bool IsInActionLock =>
    IsInAssassinationLock ||
    isBusy || isAttacking || isRolling || isDodging || isLanding || isAbility || IsInHitLock || isCheckpointFlow;

    public bool IsInMoveControlLock =>
        IsInAssassinationLock ||
        isBusy || isRolling || isDodging || isLanding || isAbility || IsInHitLock || isCheckpointFlow;

    AssassinationSystem assassination;
    public bool IsInAssassinationLock => assassination != null && assassination.IsAssassinating;

    void Awake()
    {
        anim = GetComponent<Animator>();
        sword = GetComponentInChildren<SwordController>();
        fighter = GetComponent<MeleeFighter>();
        move = GetComponent<PlayerMove>();
        block = GetComponent<BlockController>();
        lockOn = GetComponent<LockOnSystem>();
        if (lockOn != null)
            lockOn.OnTargetChanged += HandleLockTargetChanged;
        cam = FindObjectOfType<ThirdPersonShoulderCamera>();
        staminaActions = GetComponent<PlayerStaminaActions>(); // 仅玩家会有
        abilitySystem = GetComponent<PlayerAbilitySystem>();
        droneSummoner = GetComponent<PlayerDroneSummoner>();
        receiver = GetComponent<CombatReceiver>();
        assassination = GetComponent<AssassinationSystem>();

        if (miniDrone == null)
            miniDrone = GetComponentInChildren<MiniAssistDroneController>(true);

        if (miniDrone == null)
            miniDrone = FindObjectOfType<MiniAssistDroneController>(true);

        hashIsLocked = Animator.StringToHash("IsLocked");
        hashLockMoveX = Animator.StringToHash("LockMoveX");
        hashLockMoveY = Animator.StringToHash("LockMoveY");

        // ✅ crouch
        hashIsCrouching = Animator.StringToHash("IsCrouching");

        // ✅ 找到 AbilityLayer index（用于被打断时强制切 Empty）
        abilityLayerIndex = anim.GetLayerIndex(abilityLayerName);
        if (abilityLayerIndex < 0)
        {
            // 兼容：有人会叫 Ability Layer
            abilityLayerIndex = anim.GetLayerIndex("Ability Layer");
        }

        if (abilityLayerIndex < 0)
        {
            Debug.LogWarning($"[PlayerController] 找不到 Ability Layer：{abilityLayerName}（或 'Ability Layer'）。" +
                             $"被打断时将无法强制切回 Empty。请检查 Animator Layer 名称。");
        }
    }

    void Update()
    {
        if (IsInAssassinationLock)
            return;

        if (isCheckpointFlow)
            return;

        HandleMoveInput();
        HandleLockOnInput();
        HandleSwordInput();
        HandleCrouchInput();
        HandleRollInput();
        HandleAttackInput();
        HandleBlockInput();
        HandleAbilityInput();
        HandleTakedownInput();
        HandleSavePointInteractInput();
        HandleMiniDroneInput();
    }

    void LateUpdate()
    {
        // =========================================================
        // ✅ Roll/Land 兜底回收：解决“滚出平台 → 落地后永久无法操作”
        // =========================================================
        if (move != null)
        {
            // 1) 只要离地，就强制认为 Roll/Dodge 行为已结束（动画可能已被 Fall 打断，RollEnd 事件会丢）
            if (!move.IsGrounded)
            {
                isRolling = false;
                isDodging = false;
                // ✅ 防止 IFrameEnd 丢失导致永久无敌（Fall 打断 Roll/Dodge 时清无敌）
                if (receiver != null) receiver.ForceClearIFrame();
            }

            // 2) 落地锁超时回收（LandEnd 丢失时防止永久卡死）
            if (isLanding && Time.time - landingLockStartTime > landingLockTimeout)
            {
                isLanding = false;
            }
        }

        // ✅ 进入受击锁“边沿检测”：只在刚进入 HitLock 的那一刻打断 AbilityLayer
        bool hitNow = IsInHitLock;
        if (hitNow && !wasInHitLock)
        {
            OnEnterHitLock();
        }
        wasInHitLock = hitNow;

        // ✅ 兜底1：受击期间，清掉可能卡死的动作标记（避免锁定朝向停更）
        if (IsInHitLock)
        {
            isAttacking = false;
            isAbility = false;
        }

        // ✅ 兜底2：攻击被打断 / CrossFade 导致 AttackEnd 丢失时，避免 isAttacking 卡死
        if (fighter != null && isAttacking && !fighter.IsInAttackLock)
        {
            isAttacking = false;
        }

        UpdateAnimatorLockState();
        UpdateLockOnBlendTree();
        UpdateLockOnFacing();

        bool inLockLocomotion =
            lockOn != null &&
            lockOn.IsLocked &&
            move != null &&
            !move.IsRunning &&
            !move.IsSprinting;

        if (move != null)
        {
            // ✅ 修复点：锁定状态下“空中”允许转向（否则滚出平台期间朝向永远被禁，落地可能不对脸）
            bool airborne = !move.IsGrounded;

            // ✅ 锁定空中：禁止按 WASD 转向，交给 UpdateLockOnFacing 对脸
            if (lockOn != null && lockOn.IsLocked && airborne)
            {
                move.AllowRotate = false;
            }
            else
            {
                // 原有逻辑：锁定地面（inLockLocomotion）禁转向；非锁定或空中允许转向
                move.AllowRotate = !inLockLocomotion || airborne;
            }

            // ✅ 行动耗尽期间：禁止跳跃（纳入“无法施展行动”）
            bool staminaGateForActions = staminaActions == null || !staminaActions.IsActionExhausted;

            move.AllowJump =
                staminaGateForActions
                && !isBlocking
                && !isAbility
                && !isRolling
                && !isDodging
                && !isLanding
                && !isBusy
                && !IsInHitLock
                && !isCrouching; // ✅ crouch: 禁跳

            // ✅ C7：跑/冲刺门禁最终裁决集中在 PlayerController（staminaActions 只提供只读信号）
            bool staminaGate = staminaActions == null ? true : staminaActions.CanRunOrSprint;

            move.AllowRunSprint =
                staminaGate
                && !isBlocking
                && !isAbility
                && !isRolling
                && !isDodging
                && !isLanding
                && !isBusy
                && !IsInHitLock
                && !isCrouching; // ✅ crouch: 固定 walk（禁跑/冲刺）
        }

        // ✅ 每帧同步 Animator crouch bool（防止漏同步）
        if (anim != null)
            anim.SetBool(hashIsCrouching, isCrouching);
    }

    void HandleMoveInput()
    {
        if (move == null) return;

        // 1) 轴输入（Move 真相）
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        move.SetMoveInput(h, v);

        // 2) Run/Sprint（Shift）
        bool held = Input.GetKey(runKey);
        move.SetRunHeld(held);

        if (Input.GetKeyDown(runKey))
            move.NotifyRunKeyDown();

        // 3) Jump（本帧按下）
        if (Input.GetKeyDown(jumpKey))
            move.NotifyJumpPressed();
    }

    // ✅ 新增：按键只负责“转发按下”，门禁权威在 AssassinationSystem 内部
    void HandleTakedownInput()
    {
        if (assassination == null)
            return;

        if (!Input.GetKeyDown(takedownKey))
            return;

        assassination.NotifyTakedownPressed();
    }

    void HandleSavePointInteractInput()
    {
        if (!Input.GetKeyDown(savePointInteractKey)) return;
        if (currentSavePointRoot == null) return;
        if (SavePointManager.Instance == null) return;
        if (TimeController.Instance != null && TimeController.Instance.IsPaused) return;
        if (sword != null && sword.IsArmed) return;
        if (IsInActionLock) return;

        SavePointManager.Instance.BeginSaveFlow(currentSavePointRoot);
    }

    // ✅ 统一强制退出 crouch（用于受击/收剑/死亡等兜底）
    void ForceExitCrouch()
    {
        if (!isCrouching) return;

        // ✅ 安全：如果站立胶囊会顶到环境，就不要强制站起（避免顶进天花板/抖动）
        if (move != null && !move.CanStandUp())
            return;

        isCrouching = false;
        if (anim != null) anim.SetBool(hashIsCrouching, false);
    }

    // ✅ Crouch Toggle：只能在“拔剑 + 非锁定”入口进入蹲；蹲下后允许锁定并在锁定/非锁定蹲之间切换
    void HandleCrouchInput()
    {
        if (!Input.GetKeyDown(crouchKey))
            return;

        // 只允许拔剑
        if (sword == null || !sword.IsArmed)
            return;

        // 各种锁中不允许切换（避免状态抖动）
        if (IsInHitLock || isAbility || isRolling || isDodging || isLanding || isBusy)
            return;

        // 攻击中（含 ComboWindow）不允许切蹲
        if (fighter != null && (fighter.IsInAttackLock || fighter.IsInComboWindow))
            return;

        // 防御中不允许切蹲
        if (block != null && block.IsBlocking)
            return;

        // ✅ Toggle（从蹲伏 -> 站立必须通过空间检测）
        if (isCrouching)
        {
            if (move != null && !move.CanStandUp())
                return; // 顶到环境就不允许站起

            isCrouching = false;
        }
        else
        {
            isCrouching = true;

            // 进入 crouch：强制关闭格挡（确保“蹲不能防御”立即生效）
            if (block != null)
                block.RequestBlock(false);
        }

        if (anim != null)
            anim.SetBool(hashIsCrouching, isCrouching);
    }

    // ✅ 受击打断 AbilityLayer（表现层中断）+ 取消技能效果（逻辑层中断）
    void OnEnterHitLock()
    {
        // ✅ 受击：尝试退出 crouch（但如果顶到环境则保持蹲伏，避免顶进天花板）
        ForceExitCrouch();

        if (abilitySystem != null)
            abilitySystem.CancelPending();

        anim.ResetTrigger(ability1Trigger);
        anim.ResetTrigger(ability2Trigger);
        anim.ResetTrigger(ability3Trigger);
        anim.ResetTrigger(ability4Trigger);

        isAbility = false;
        isAttacking = false;

        if (abilityLayerIndex >= 0)
        {
            anim.CrossFadeInFixedTime(abilityEmptyStateName, 0.05f, abilityLayerIndex);
        }
    }

    /* ================== Lock On ================== */

    void HandleLockOnInput()
    {
        if (sword == null || lockOn == null)
            return;

        if (!sword.IsArmed)
            return;

        if (IsInHitLock)
            return;

        if (Input.GetKeyDown(switchLockLeftKey))
        {
            lockOn.SwitchTargetLeft();
        }
        else if (Input.GetKeyDown(switchLockRightKey))
        {
            lockOn.SwitchTargetRight();
        }

        if (!Input.GetKeyDown(lockOnKey))
            return;

        if (lockOn.IsLocked)
        {
            lockOn.ClearLock();
        }
        else
        {
            lockOn.TryLockNearest();
        }
    }

    void UpdateAnimatorLockState()
    {
        if (anim == null || lockOn == null || move == null)
            return;

        bool useLockLocomotion =
            lockOn.IsLocked &&
            move.IsGrounded &&
            !move.IsRunning &&
            !move.IsSprinting;

        anim.SetBool(hashIsLocked, useLockLocomotion);
    }

    void UpdateLockOnBlendTree()
    {
        if (!anim.GetBool(hashIsLocked))
        {
            anim.SetFloat(hashLockMoveX, 0f);
            anim.SetFloat(hashLockMoveY, 0f);
            return;
        }

        anim.SetFloat(hashLockMoveX, Input.GetAxisRaw("Horizontal"));
        anim.SetFloat(hashLockMoveY, Input.GetAxisRaw("Vertical"));
    }

    void UpdateLockOnFacing()
    {
        if (IsInAssassinationLock) return; // ✅ 暗杀期间绝对不对脸

        if (lockOn == null || move == null)
            return;

        if (!lockOn.IsLocked)
        {
            // ✅ 解除锁定时重置边沿检测
            prevFighterAttackLock = false;
            return;
        }

        // ✅ 这些状态下仍然不对脸
        if (IsInHitLock || isRolling || isDodging || isAbility)
            return;

        CombatStats targetStats = lockOn.CurrentTargetStats;
        if (targetStats == null)
            return;

        // =========================================================
        // ✅ 攻击吸附：前摇追踪、命中窗口锁死（复用 15f 旋转速度）
        // 规则：
        // - 仅在 fighter.IsInAttackLock 且 !fighter.IsInHitWindow 时允许纠正朝向
        // - 一旦进入 IsInHitWindow（AttackBegin~AttackEnd），立刻锁死不再纠正
        // =========================================================
        bool fighterAttackLockNow = (fighter != null && fighter.IsInAttackLock);

        if (fighterAttackLockNow && !prevFighterAttackLock)
        {
            // ✅ 记录“本次攻击开始时的朝向”，用于角度钳制（防追踪刀/瞬间大转身）
            attackStartYaw = transform.eulerAngles.y;
        }
        prevFighterAttackLock = fighterAttackLockNow;

        if (fighterAttackLockNow)
        {
            // ✅ 命中窗口锁死：AttackBegin~AttackEnd 不允许再纠正
            if (fighter != null && fighter.IsInHitWindow)
                return;

            Vector3 targetPoint = LockTargetPointUtility.GetCapsuleCenter(targetStats.transform);
            Vector3 dir = targetPoint - transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.0001f)
                return;

            float desiredYaw = Quaternion.LookRotation(dir.normalized).eulerAngles.y;

            float max = Mathf.Clamp(attackMagnetMaxYawFromStart, 0f, 180f);
            float delta = Mathf.DeltaAngle(attackStartYaw, desiredYaw);
            delta = Mathf.Clamp(delta, -max, max);

            float clampedYaw = attackStartYaw + delta;
            Quaternion targetRot = Quaternion.Euler(0f, clampedYaw, 0f);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                15f * Time.deltaTime
            );

            return;
        }

        // =========================================================
        // ✅ 非攻击：保持你原本的“锁定对脸”逻辑（不改行为）
        // =========================================================

        // （如果你仍想“跑/冲刺时不强制对脸”，只对地面做限制）
        if (move.IsGrounded)
        {
            if (move.IsRunning || move.IsSprinting)
                return;
        }

        // ✅ 保持原行为：攻击/翻滚/能力期间不走“普通对脸”
        if (isAttacking)
            return;

        Vector3 p = LockTargetPointUtility.GetCapsuleCenter(targetStats.transform);
        Vector3 d = p - transform.position;
        d.y = 0f;

        if (d.sqrMagnitude < 0.0001f)
            return;

        Quaternion rot = Quaternion.LookRotation(d.normalized);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            rot,
            15f * Time.deltaTime
        );
    }

    void OnDestroy()
    {
        if (lockOn != null)
            lockOn.OnTargetChanged -= HandleLockTargetChanged;
    }

    void HandleLockTargetChanged(CombatStats stats)
    {
        if (cam == null) return;
        cam.SetLockTarget(stats != null ? stats.transform : null);
    }

    /* ================== Sword ================== */

    void HandleSwordInput()
    {
        if (sword == null)
            return;

        // ✅ 空中/跳跃/下落：禁止拔剑/收剑
        if (move != null && !move.IsGrounded)
            return;

        // ✅ crouch：蹲伏期间禁止拔收剑
        if (isCrouching)
            return;

        if (!Input.GetKeyDown(toggleSwordKey)) return;

        // ✅ 收剑：尝试退出 crouch（顶到环境则保持蹲）
        if (sword != null && sword.IsArmed)
            ForceExitCrouch();

        if (IsInActionLock || isBlocking) return;

        if (lockOn != null && lockOn.IsLocked && sword.IsArmed)
            return;

        if (sword.IsArmed)
            RequestSheathSword();
        else
            RequestDrawSword();
    }

    void RequestDrawSword()
    {
        isBusy = true;
        anim.SetTrigger("DrawSword");
    }

    void RequestSheathSword()
    {
        ForceExitCrouch();

        isBusy = true;
        anim.SetTrigger("SheathSword");
    }

    DodgeDir GetLockDodgeDir(bool idleAsBack)
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        if (Mathf.Abs(h) < 0.1f && Mathf.Abs(v) < 0.1f)
        {
            return idleAsBack ? DodgeDir.Back : DodgeDir.Forward;
        }

        if (Mathf.Abs(v) >= Mathf.Abs(h))
        {
            return v > 0 ? DodgeDir.Forward : DodgeDir.Back;
        }

        return h > 0 ? DodgeDir.Right : DodgeDir.Left;
    }

    Vector3 GetLockLocomotionWorldDirectionOrDefault(Vector3 fallback)
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 dir = transform.right * h + transform.forward * v;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f)
            return fallback;

        return dir.normalized;
    }

    bool IsInLockOnRunState()
    {
        return lockOn != null && lockOn.IsLocked && move != null && (move.IsRunning || move.IsSprinting);
    }

    bool CanStartRollOrDodge()
    {
        if (move == null || !move.IsGrounded)
            return false;

        if (sword == null || !sword.IsArmed)
            return false;

        if (IsInHitLock)
            return false;

        if (fighter != null && (fighter.IsInAttackLock || fighter.IsInComboWindow))
            return false;

        if (isBlocking || isAbility || isBusy || isLanding || isRolling || isDodging)
            return false;

        return true;
    }

    void TryTriggerRollFromHold()
    {
        if (!CanStartRollOrDodge())
            return;

        bool locked = lockOn != null && lockOn.IsLocked;
        bool lockRun = IsInLockOnRunState();

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        if (locked && !lockRun)
            rollDirection = GetLockLocomotionWorldDirectionOrDefault(forward);
        else
            rollDirection = forward;

        if (!TrySpendForRoll())
            return;

        anim.SetTrigger("Roll");
        rollActionTriggered = true;
    }

    void TryTriggerDodgeFromTap()
    {
        if (!CanStartRollOrDodge())
            return;

        bool locked = lockOn != null && lockOn.IsLocked;
        bool lockRun = IsInLockOnRunState();

        DodgeDir dir;

        if (!locked)
            dir = DodgeDir.Forward;
        else if (lockRun)
            dir = DodgeDir.Forward;
        else
            dir = GetLockDodgeDir(idleAsBack: true);

        if (!TrySpendForDodge())
            return;

        rollDirection = Vector3.zero;
        anim.SetFloat("DodgeDir", (float)dir);
        anim.SetTrigger("Dodge");
        rollActionTriggered = true;
    }

    bool TrySpendForRoll() => staminaActions == null || staminaActions.TryRoll();
    bool TrySpendForDodge() => staminaActions == null || staminaActions.TryDodge();
    bool TrySpendForHeavy(bool attackA) => staminaActions == null || staminaActions.TryHeavy(attackA);
    bool TrySpendForRunAttack(bool attackA) => staminaActions == null || staminaActions.TryRunAttack(attackA);
    bool TrySpendForSprintAttack(bool attackA) => staminaActions == null || staminaActions.TrySprintAttack(attackA);

    void HandleRollInput()
    {
        if (Input.GetKeyDown(rollKey))
        {
            rollKeyHolding = true;
            rollActionTriggered = false;
            rollKeyHoldStartTime = Time.time;
        }

        if (!rollKeyHolding)
            return;

        float hold = Time.time - rollKeyHoldStartTime;
        float threshold = Mathf.Max(0.01f, rollHoldThreshold);

        if (!rollActionTriggered && hold >= threshold)
            TryTriggerRollFromHold();

        if (Input.GetKeyUp(rollKey))
        {
            if (!rollActionTriggered)
                TryTriggerDodgeFromTap();

            rollKeyHolding = false;
            rollActionTriggered = false;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (SavePointManager.Instance == null) return;
        if (SavePointManager.Instance.TryResolveSavePointRoot(other, out Transform savePointRoot))
            currentSavePointRoot = savePointRoot;
    }

    void OnTriggerExit(Collider other)
    {
        if (SavePointManager.Instance == null || currentSavePointRoot == null) return;
        if (SavePointManager.Instance.TryResolveSavePointRoot(other, out Transform savePointRoot) && savePointRoot == currentSavePointRoot)
            currentSavePointRoot = null;
    }

    void OnDisable()
    {
        rollKeyHolding = false;
        rollActionTriggered = false;
    }

    void HandleAttackInput()
    {
        if (isCrouching) return;
        if (fighter == null) return;
        if (sword == null || !sword.IsArmed) return;
        if (move == null) return;

        if (IsInHitLock) return;
        if (isAbility) return;
        if (isBlocking) return;

        if (isBusy || isLanding || isRolling || isDodging)
            return;

        if (!move.IsGrounded)
        {
            HandleAirAttackInput();
            return;
        }

        AttackMoveType moveType = AttackMoveType.None;

        if (move.IsSprinting) moveType = AttackMoveType.Sprint;
        else if (move.IsRunning) moveType = AttackMoveType.Run;

        HandleAttackKey(attackAKey, true, moveType);
        HandleAttackKey(attackBKey, false, moveType);
    }

    void HandleAirAttackInput()
    {
        // 空中攻击不参与蓄力重击逻辑，且落地后重置按住态
        isHoldingAttack = false;
        heavyRequested = false;
        holdTimer = 0f;

        if (attackAKey != KeyCode.None && Input.GetKeyDown(attackAKey))
            fighter.TryAirAttack(true);

        if (attackBKey != KeyCode.None && Input.GetKeyDown(attackBKey))
            fighter.TryAirAttack(false);
    }

    void HandleAttackKey(KeyCode key, bool attackA, AttackMoveType moveType)
    {
        if (key == KeyCode.None)
            return;

        if (Input.GetKeyDown(key))
        {
            isHoldingAttack = true;
            heavyRequested = false;
            holdTimer = 0f;
        }

        if (Input.GetKey(key) && isHoldingAttack)
        {
            holdTimer += Time.deltaTime;
            if (!heavyRequested && holdTimer >= HEAVY_THRESHOLD)
            {
                if (!TrySpendForHeavy(attackA))
                    return;

                fighter.RequestHeavy(attackA);
                heavyRequested = true;
            }
        }

        if (Input.GetKeyUp(key))
        {
            if (!heavyRequested)
            {
                if (moveType == AttackMoveType.Run)
                {
                    if (!TrySpendForRunAttack(attackA))
                    {
                        isHoldingAttack = false;
                        return;
                    }
                }
                else if (moveType == AttackMoveType.Sprint)
                {
                    if (!TrySpendForSprintAttack(attackA))
                    {
                        isHoldingAttack = false;
                        return;
                    }
                }

                fighter.TryAttack(attackA, moveType);
            }

            isHoldingAttack = false;
        }
    }

    void HandleBlockInput()
    { // ✅ 1) 空中（跳/下落）禁止防御
        if (block == null)
        {
            isBlocking = false;
            return;
        }

        if (move != null && !move.IsGrounded)
        {
            block.RequestBlock(false);
            isBlocking = false;
            return;
        }

        // ✅ 2) 落地动画期间禁止防御（LandBegin~LandEnd）
        if (isLanding)
        {
            block.RequestBlock(false);
            isBlocking = false;
            return;
        }
        // ✅ Roll / Dodge 期间禁止防御
        if (isRolling || isDodging)
        {
            block.RequestBlock(false);
            isBlocking = false;
            return;
        }

        if (isCrouching)
        {
            block.RequestBlock(false);
            isBlocking = false;
            return;
        }

        if (sword == null || !sword.IsArmed)
        {
            block.RequestBlock(false);
            isBlocking = false;
            return;
        }

        if (IsInHitLock)
        {
            isBlocking = block.IsBlocking;
            return;
        }

        if (isAbility)
        {
            block.RequestBlock(false);
            isBlocking = false;
            return;
        }

        if (staminaActions != null && staminaActions.IsActionExhausted)
        {
            block.RequestBlock(false);
            isBlocking = false;
            return;
        }

        bool wantBlock = Input.GetKey(blockKey);

        bool inAnyAttackAnim = fighter != null && (fighter.IsInAttackLock || fighter.IsInComboWindow);
        if (inAnyAttackAnim)
            wantBlock = false;

        block.RequestBlock(wantBlock);
        isBlocking = block.IsBlocking;
    }

    public void AbilityImpact()
    {
        if (IsInHitLock)
        {
            if (abilitySystem != null)
                abilitySystem.CancelPending();
            return;
        }

        if (abilitySystem != null && abilitySystem.ApplyPending(out var appliedAbility))
            CombatSfxSignals.RaiseAbilityTriggered(CombatSfxKeyUtility.ToAbilityId(appliedAbility));
    }

    void HandleAbilityInput()
    {
        if (isCrouching) return;

        if (!sword.IsArmed || isAbility) return;
        if (IsInHitLock) return;

        if (isBlocking || isRolling || isDodging || isLanding || isBusy)
            return;

        if (Input.GetKeyDown(ability1Key))
        {
            if (abilitySystem != null && abilitySystem.TryRequest(PlayerAbilitySystem.AbilityType.Ability1))
            {
                anim.SetTrigger(ability1Trigger);
            }
        }

        if (Input.GetKeyDown(ability2Key))
        {
            if (abilitySystem != null && abilitySystem.TryRequest(PlayerAbilitySystem.AbilityType.Ability2))
            {
                anim.SetTrigger(ability2Trigger);
            }
        }

        if (Input.GetKeyDown(ability3Key))
        {
            if (abilitySystem != null && abilitySystem.TryRequest(PlayerAbilitySystem.AbilityType.Ability3))
            {
                anim.SetTrigger(ability3Trigger);
            }
        }

        if (Input.GetKeyDown(ability4Key))
        {
            if (abilitySystem != null && abilitySystem.TryRequest(PlayerAbilitySystem.AbilityType.Ability4))
            {
                anim.SetTrigger(ability4Trigger);
            }
        }

        if (Input.GetKeyDown(helicopterKey))
        {
            if (droneSummoner != null)
                droneSummoner.TrySummon();
        }
    }

    void HandleMiniDroneInput()
    {
        if (miniDrone == null) return;

        if (Input.GetKeyDown(miniDroneFireKey))
            miniDrone.NotifyFirePressed();

        if (Input.GetKeyUp(miniDroneFireKey))
            miniDrone.NotifyFireReleased();
    }

    public void Begin()
    {
        ForceExitCrouch();
        isAttacking = true;
    }

    public void AttackEnd() => isAttacking = false;

    public void RollBegin()
    {
        isRolling = true;
        if (rollDirection.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(rollDirection);
    }

    public void RollEnd() => isRolling = false;

    public void DodgeBegin() => isDodging = true;

    public void DodgeEnd() => isDodging = false;

    public void LandBegin()
    {
        ForceExitCrouch();

        isLanding = true;
        landingLockStartTime = Time.time;

        if (receiver != null) receiver.ForceClearIFrame();
    }

    public void LandEnd() => isLanding = false;

    public void AbilityBegin()
    {
        ForceExitCrouch();
        isAbility = true;
    }

    public void AbilityEnd() => isAbility = false;

    public void AttachSwordToHand() => sword.AttachToHand();
    public void AttachSwordToWaist() => sword.AttachToWaist();

    public void OnDrawSwordEnd()
    {
        if (sword != null) sword.SetArmed(true);
        anim.SetBool("IsArmed", sword != null && sword.IsArmed);
        isBusy = false;
    }

    public void OnSheathSwordEnd()
    {
        if (sword != null) sword.SetArmed(false);
        anim.SetBool("IsArmed", sword != null && sword.IsArmed);
        isBusy = false;

        ForceExitCrouch();
    }

    public void SetCheckpointFlowLock(bool v)
    {
        isCheckpointFlow = v;

        if (!v)
            return;

        if (block != null)
            block.RequestBlock(false);

        isBusy = false;
        isAttacking = false;
        isRolling = false;
        isDodging = false;
        isLanding = false;
        isBlocking = false;
        isAbility = false;

        ForceExitCrouch();
    }
    // Animation Event receiver fallback:
    // some player prefabs may not挂载 PlayerCheckpointAnimEvents，
    // so we also expose these handlers on PlayerController (usually always present).
    public void Checkpoint_SaveEnd()
    {
        if (SavePointManager.Instance != null)
            SavePointManager.Instance.NotifySaveAnimEnd();
    }

    public void Checkpoint_ExitEnd()
    {
        if (SavePointManager.Instance != null)
            SavePointManager.Instance.NotifyExitAnimEnd();
    }
    void OnCharacterDead()
    {
        ForceExitCrouch();

        isBusy = true;
        isAttacking = false;
        isRolling = false;
        isDodging = false;
        isLanding = false;
        isBlocking = false;
        isAbility = false;

        if (block != null)
            block.RequestBlock(false);

        if (lockOn != null && lockOn.IsLocked)
        {
            lockOn.ClearLock();
            if (cam != null) cam.SetLockTarget(null);
        }

        if (anim != null)
        {
            anim.SetTrigger("Dead");
        }
    }
}
