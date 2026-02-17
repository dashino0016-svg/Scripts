using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMove : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 1f;
    public float runSpeed = 3f;
    public float sprintSpeed = 5f;

    [Header("Jump")]
    public float jumpForce = 5f;
    public float hardLandVelocity = -10f;

    [Header("Jump (Lock-On Tuning)")]
    [Range(0.1f, 1f)]
    public float lockOnJumpForceScale = 1f;

    [Tooltip("起跳时额外的水平向前冲量")]
    public float jumpForwardBoost = 0f;

    [Tooltip("空中方向修正强度（0=完全锁死，1=完全自由）")]
    public float airControl = 0.2f;

    [Header("Rotation")]
    public float rotationSmoothTime = 0.08f;

    [Header("Gravity")]
    public float gravity = -15f;
    public float groundedGravity = -0.5f;
    public float terminalVelocity = -50f;

    [Header("Ground Check")]
    public float groundCheckRadius = 0.15f;
    public float groundCheckOffset = -0.1f;
    public float groundCheckDistance = 0.25f;
    [Min(0f)] public float groundedGraceTime = 0.08f;
    public LayerMask groundMask;

    [Header("Fall Damage")]
    public bool enableFallDamage = true;
    [Tooltip("落地伤害起算速度阈值（向下速度绝对值）")]
    public float fallDamageThresholdSpeed = 12f;
    [Tooltip("超过阈值后的伤害换算系数")]
    public float fallDamageScale = 2f;
    [Tooltip("单次落地伤害上限（<=0 表示不上限）")]
    public int fallDamageMax = 9999;

    [Header("Animation")]
    public float speedDampTime = 0.02f;

    [Header("Landing")]
    [Tooltip("落地动画触发后的短时间内，禁止再次触发 Land（用于防止斜坡短暂离地导致二次 SoftLand 插入）。")]
    [SerializeField, Min(0f)] float landRetriggerBlockTime = 0.35f;

    [Header("Root Motion (Player)")]
    [Tooltip("开启后：空中阶段不吃任何 Root Motion 位移，避免与跳跃物理叠加导致跳高飘移。")]
    [SerializeField] bool disableRootMotionTranslationWhenAirborne = true;

    [Tooltip("开启后：无论地面/空中都忽略 Root Motion 的 Y 分量，垂直位移完全由 jump/gravity 决定。")]
    [SerializeField] bool ignoreRootMotionY = true;

    // =========================
    // ✅ Crouch Capsule (CharacterController)
    // =========================
    [Header("Crouch Capsule (CharacterController)")]
    [Tooltip("站立胶囊参数：Start() 会自动从当前 CharacterController 读取并覆盖这里的值。")]
    [SerializeField] float standHeight = 1.8f;
    [SerializeField] float standRadius = 0.2f;
    [SerializeField] float standCenterY = 0.95f;

    [Tooltip("蹲伏胶囊参数（建议高度为站立的 0.6~0.75）")]
    [SerializeField] float crouchHeight = 1.2f;
    [SerializeField] float crouchRadius = 0.2f;
    [SerializeField] float crouchCenterY = 0.65f;

    [Tooltip("胶囊切换平滑速度（0=立刻切换）")]
    [SerializeField] float capsuleLerpSpeed = 20f;

    [Tooltip("站起空间检测的环境层（不要包含 Player/Enemy/HitBox/HurtBox），否则贴着敌人会永远站不起来")]
    [SerializeField] LayerMask standCheckMask = ~0;

    bool lastCrouchFlag;

    CharacterController controller;
    Animator anim;
    ThirdPersonShoulderCamera cam;
    PlayerController controllerLogic;
    MeleeFighter fighter;

    // ✅ 跳跃扣体力（起跳真相点）
    PlayerStaminaActions staminaActions;
    CombatStats combatStats;

    // =========================
    // ✅ 输入由 PlayerController 注入（PlayerMove 不再读 Input / KeyCode）
    // =========================
    Vector3 input;                 // x/z in [ -1, 1 ]
    bool shiftHeld;                // run key held
    bool runKeyDownThisFrame;      // run key pressed this frame (for double-tap)
    bool jumpPressedThisFrame;     // jump pressed this frame

    public void SetMoveInput(float x, float y)
    {
        input.x = x;
        input.z = y;
        input = Vector3.ClampMagnitude(input, 1f);
    }

    public void SetRunHeld(bool held)
    {
        shiftHeld = held;
    }

    public void NotifyRunKeyDown()
    {
        runKeyDownThisFrame = true;
    }

    public void NotifyJumpPressed()
    {
        jumpPressedThisFrame = true;
    }

    Vector3 airHorizontalVelocity;

    float velocityY;
    float lastAirVelocityY;
    float lastLandTriggerTime = -999f;
    float turnVelocity;

    bool isGrounded;
    bool wasGrounded;
    float lastGroundedTime = -999f;
    Vector3 groundNormal = Vector3.up;

    bool isSprinting;
    float lastShiftTime = -10f;
    bool forceWalkThisFrame;

    public bool AllowRotate = true;
    public bool AllowJump = true;
    public bool AllowRunSprint = true;

    public bool IsSprinting => isSprinting;
    public bool IsRunning => shiftHeld && !isSprinting && input.sqrMagnitude > 0.1f;
    public bool IsGrounded => isGrounded;
    public bool IsWalking => input.sqrMagnitude > 0.01f && !isSprinting && (!shiftHeld || forceWalkThisFrame);
    public bool IsMoving => input.sqrMagnitude > 0.01f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        anim = GetComponent<Animator>();
        cam = FindObjectOfType<ThirdPersonShoulderCamera>();
        controllerLogic = GetComponent<PlayerController>();
        fighter = GetComponent<MeleeFighter>();

        staminaActions = GetComponent<PlayerStaminaActions>();
        combatStats = GetComponent<CombatStats>();

        // ✅ 自动把当前 CC 参数当作“站立胶囊”
        standHeight = controller.height;
        standRadius = controller.radius;
        standCenterY = controller.center.y;

        // ✅ 默认给一套合理 crouch（你也可在 Inspector 覆盖）
        if (crouchHeight <= 0.01f) crouchHeight = standHeight * 0.65f;
        if (crouchRadius <= 0.01f) crouchRadius = standRadius;
        if (crouchCenterY <= 0.01f) crouchCenterY = crouchHeight * 0.5f;

        lastCrouchFlag = controllerLogic != null && controllerLogic.IsCrouching;

        // 启动时同步一次胶囊
        if (lastCrouchFlag)
            ApplyCapsuleImmediate(crouchHeight, crouchRadius, crouchCenterY);
        else
            ApplyCapsuleImmediate(standHeight, standRadius, standCenterY);
    }

    void Update()
    {
        // ✅ 先更新胶囊（影响 grounded 检测）
        UpdateCrouchCapsule();

        wasGrounded = isGrounded;
        bool groundedNow = CheckGroundedRaw();
        if (groundedNow)
            lastGroundedTime = Time.time;

        isGrounded = groundedNow || (Time.time - lastGroundedTime <= groundedGraceTime);

        if (isGrounded && velocityY < groundedGravity)
            velocityY = groundedGravity;

        anim.SetBool("IsGrounded", isGrounded);
        anim.SetFloat("VerticalVelocity", velocityY);

        // ✅ 移动锁：由“攻击锁 + 控制锁”组成
        // 空中攻击例外：允许沿已有空中轨迹继续运动（仅禁止 root motion，不冻结物理位移）
        bool airborneAirAttack = fighter != null && fighter.IsInAirAttack && !isGrounded;
        bool lockedByAttack = fighter != null && fighter.IsInAttackLock && !airborneAirAttack;
        bool lockedByController = controllerLogic != null && controllerLogic.IsInMoveControlLock;
        bool locked = lockedByAttack || lockedByController;

        if (locked)
        {
            // 锁定时冻结（仍然保留重力/落地）
            input = Vector3.zero;
            isSprinting = false;
            shiftHeld = false;
            turnVelocity = 0f;

            anim.SetFloat("Speed", 0f, speedDampTime, Time.deltaTime);

            ApplyGravity();
            MoveVerticalOnly();
            HandleLanding();

            // ✅ 清本帧按下
            runKeyDownThisFrame = false;
            jumpPressedThisFrame = false;
            forceWalkThisFrame = false;
            return;
        }

        HandleSprintInput_Injected();
        HandleJumpInput_Injected();

        Vector3 moveDir = CalculateMoveDirection();
        float speed = GetMoveSpeed(moveDir);

        Rotate(moveDir);
        Move(moveDir, speed);

        anim.SetFloat("Speed", speed, speedDampTime, Time.deltaTime);

        ApplyGravity();
        HandleLanding();

        // ✅ 清本帧按下（只消费一次）
        runKeyDownThisFrame = false;
        jumpPressedThisFrame = false;
        forceWalkThisFrame = false;
    }

    /* ================= Crouch Capsule ================= */

    void UpdateCrouchCapsule()
    {
        bool isCrouching = controllerLogic != null && controllerLogic.IsCrouching;

        // 状态变化时，立刻切一次（减少半帧卡住）
        if (isCrouching != lastCrouchFlag)
        {
            if (isCrouching)
                ApplyCapsuleImmediate(crouchHeight, crouchRadius, crouchCenterY);

            // 站立的“立刻切换”不在这里做：站立由 PlayerController 在通过 CanStandUp 后切换 crouch bool
            lastCrouchFlag = isCrouching;
        }

        if (capsuleLerpSpeed > 0f)
        {
            if (isCrouching)
                LerpCapsule(crouchHeight, crouchRadius, crouchCenterY);
            else
                LerpCapsule(standHeight, standRadius, standCenterY);
        }
        else
        {
            if (isCrouching)
                ApplyCapsuleImmediate(crouchHeight, crouchRadius, crouchCenterY);
            else
                ApplyCapsuleImmediate(standHeight, standRadius, standCenterY);
        }
    }

    void ApplyCapsuleImmediate(float height, float radius, float centerY)
    {
        controller.height = height;
        controller.radius = radius;
        Vector3 c = controller.center;
        c.y = centerY;
        controller.center = c;
    }

    void LerpCapsule(float height, float radius, float centerY)
    {
        controller.height = Mathf.Lerp(controller.height, height, Time.deltaTime * capsuleLerpSpeed);
        controller.radius = Mathf.Lerp(controller.radius, radius, Time.deltaTime * capsuleLerpSpeed);

        Vector3 c = controller.center;
        c.y = Mathf.Lerp(c.y, centerY, Time.deltaTime * capsuleLerpSpeed);
        controller.center = c;
    }

    // ✅ 给 PlayerController 调用：是否能从蹲伏站起来（站立胶囊是否会顶到环境）
    public bool CanStandUp()
    {
        float radius = Mathf.Max(0.01f, standRadius - 0.01f);
        float height = Mathf.Max(standHeight, radius * 2f);

        // 站立时的世界中心（用站立 centerY）
        Vector3 worldCenter = transform.TransformPoint(new Vector3(controller.center.x, standCenterY, controller.center.z));

        float half = height * 0.5f;
        float cylinder = Mathf.Max(0f, half - radius);

        Vector3 up = transform.up;
        Vector3 p1 = worldCenter + up * cylinder;
        Vector3 p2 = worldCenter - up * cylinder;

        return !Physics.CheckCapsule(p1, p2, radius, standCheckMask, QueryTriggerInteraction.Ignore);
    }

    /* ================= Ground ================= */

    void GetGroundCheckCast(out Vector3 castOrigin, out float radius, out float castDistance)
    {
        radius = Mathf.Max(0.01f, groundCheckRadius);
        castDistance = Mathf.Max(0.01f, groundCheckDistance);

        // 兼容：编辑器 Gizmos 可能在 Start() 之前调用，此时 controller 字段还没缓存
        CharacterController cc = controller != null ? controller : GetComponent<CharacterController>();
        Vector3 bottom = GetCapsuleBottomWorld(cc);

        // ✅ groundCheckOffset：真正用于“上下移动检测球中心”
        // - 负数：球心更靠近地面（甚至略低于胶囊底点），更“黏地”
        // - 正数：球心更远离地面（减少误判，但更容易在下坡/台阶边缘丢地）
        float centerUp = Mathf.Max(0.01f, radius + groundCheckOffset);
        castOrigin = bottom + transform.up * centerUp;
    }

    Vector3 GetCapsuleBottomWorld(CharacterController cc)
    {
        if (cc == null) return transform.position;

        Vector3 centerWorld = transform.TransformPoint(cc.center);
        float halfHeight = Mathf.Max(cc.height * 0.5f, cc.radius);
        float bottomOffset = halfHeight - cc.radius;
        return centerWorld - transform.up * bottomOffset;
    }

    Vector3 GetCapsuleBottomWorld()
    {
        return GetCapsuleBottomWorld(controller);
    }

    bool CheckGroundedRaw()
    {
        GetGroundCheckCast(out Vector3 castOrigin, out float radius, out float castDistance);

        if (Physics.SphereCast(
                castOrigin,
                radius,
                -transform.up,
                out RaycastHit hit,
                castDistance,
                groundMask,
                QueryTriggerInteraction.Ignore))
        {
            groundNormal = hit.normal;
            return true;
        }

        // SphereCast 没命中时，允许用 CharacterController.isGrounded 兜底（极少数边界情况）
        bool ccGrounded = controller != null && controller.isGrounded;
        groundNormal = transform.up;
        return ccGrounded;
    }

    /* ================= Sprint / Jump (Injected Input) ================= */

    void HandleSprintInput_Injected()
    {
        if (!AllowRunSprint)
        {
            shiftHeld = false;

            if (isSprinting)
            {
                isSprinting = false;
                forceWalkThisFrame = true;
            }

            return;
        }

        // ✅ 双击 Shift 开冲刺：用“本帧按下”信号
        if (runKeyDownThisFrame)
        {
            if (Time.time - lastShiftTime <= 0.3f)
                isSprinting = true;

            lastShiftTime = Time.time;
        }

        if (isSprinting && (!shiftHeld || input.sqrMagnitude < 0.01f))
        {
            isSprinting = false;
            forceWalkThisFrame = true;
        }
    }

    void HandleJumpInput_Injected()
    {
        if (!isGrounded) return;
        if (!AllowJump) return;

        if (!jumpPressedThisFrame)
            return;

        // ✅ 起跳真相点扣体力：扣不起就不跳
        if (staminaActions != null && !staminaActions.TryJump())
            return;

        float jf = jumpForce;

        // ✅ 锁定时用倍率（避免锁定跳过高）
        if (controllerLogic != null && controllerLogic.IsLocked)
            jf *= lockOnJumpForceScale;

        velocityY = jf;

        Vector3 dir = CalculateMoveDirection();
        airHorizontalVelocity = dir * GetMoveSpeed(dir);

        if (input.sqrMagnitude > 0.01f)
        {
            Vector3 forward = airHorizontalVelocity.normalized;
            airHorizontalVelocity += forward * jumpForwardBoost;
        }

        anim.SetTrigger("Jump");
    }

    /* ================= Movement ================= */

    Vector3 CalculateMoveDirection()
    {
        if (input.sqrMagnitude < 0.001f || cam == null)
            return Vector3.zero;

        Quaternion yaw = Quaternion.Euler(0f, cam.CurrentYaw, 0f);
        return yaw * input;
    }

    float GetMoveSpeed(Vector3 dir)
    {
        if (dir == Vector3.zero) return 0f;
        if (isSprinting) return sprintSpeed;
        if (forceWalkThisFrame) return walkSpeed;
        return shiftHeld ? runSpeed : walkSpeed;
    }

    void Rotate(Vector3 dir)
    {
        if (!AllowRotate)
            return;

        if (dir.sqrMagnitude < 0.001f) return;

        float target = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float angle = Mathf.SmoothDampAngle(
            transform.eulerAngles.y,
            target,
            ref turnVelocity,
            rotationSmoothTime
        );

        transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }

    void Move(Vector3 dir, float speed)
    {
        Vector3 horizontal;

        if (isGrounded)
        {
            if (velocityY < groundedGravity)
                velocityY = groundedGravity;

            // ✅ 关键修复：保持贴坡移动（保留投影后的 Y 分量），避免下坡高速时“飞出去→丢地”
            horizontal = Vector3.ProjectOnPlane(dir * speed, groundNormal);

            // ✅ 关键修复：非跳跃离地（比如下坡/台阶边缘短暂离地）时，空中仍保留上一帧水平速度
            // 注意：跳跃当帧 velocityY 为正，不能覆盖 HandleJump 里设置的 airHorizontalVelocity（含 jumpForwardBoost）
            if (velocityY <= groundedGravity + 0.001f)
                airHorizontalVelocity = dir * speed; // dir 本身无 Y，符合“空中水平速度”语义
        }
        else
        {
            if (dir.sqrMagnitude > 0.001f)
            {
                Vector3 targetDir = dir.normalized;
                Vector3 currentDir = airHorizontalVelocity.sqrMagnitude > 0.0001f ? airHorizontalVelocity.normalized : targetDir;

                Vector3 blendedDir = Vector3.Lerp(
                    currentDir,
                    targetDir,
                    airControl * Time.deltaTime * 10f
                ).normalized;

                airHorizontalVelocity = blendedDir * airHorizontalVelocity.magnitude;
            }

            horizontal = airHorizontalVelocity;
        }

        Vector3 motion = horizontal;
        // ✅ 关键修复：不要把 horizontal 的 Y 覆盖掉，否则贴坡投影失效（下坡更容易丢地）
        motion += Vector3.up * velocityY;
        controller.Move(motion * Time.deltaTime);
    }

    void MoveVerticalOnly()
    {
        if (isGrounded && velocityY < groundedGravity)
            velocityY = groundedGravity;

        Vector3 motion = Vector3.zero;
        motion.y = velocityY;
        controller.Move(motion * Time.deltaTime);
    }

    void OnAnimatorMove()
    {
        if (anim == null || controller == null)
            return;

        if (!anim.applyRootMotion)
            return;

        Vector3 delta = anim.deltaPosition;

        // 跳跃/下落阶段：位移由 CharacterController + velocityY 统一控制
        if (disableRootMotionTranslationWhenAirborne && !isGrounded)
            delta = Vector3.zero;

        // 垂直位移统一走物理，防止动画 Y 叠加导致“突然上窜/跳高不一致”
        if (ignoreRootMotionY)
            delta.y = 0f;

        if (delta.sqrMagnitude > 0f)
            controller.Move(delta);
    }

    int CalculateFallDamage(float downwardSpeed)
    {
        if (!enableFallDamage)
            return 0;

        float speed = Mathf.Max(0f, downwardSpeed);
        if (speed <= fallDamageThresholdSpeed)
            return 0;

        float excess = speed - fallDamageThresholdSpeed;
        int damage = Mathf.CeilToInt(excess * Mathf.Max(0f, fallDamageScale));

        if (fallDamageMax > 0)
            damage = Mathf.Min(damage, fallDamageMax);

        return Mathf.Max(0, damage);
    }

    void ApplyGravity()
    {
        if (!isGrounded)
        {
            velocityY += gravity * Time.deltaTime;
            velocityY = Mathf.Max(velocityY, terminalVelocity);
            lastAirVelocityY = velocityY;
        }
    }

    void HandleLanding()
    {
        if (!wasGrounded && isGrounded)
        {
            bool landingAnimationPlaying = IsLandingAnimationPlaying();
            bool landingTriggerBlocked = (Time.time - lastLandTriggerTime) <= landRetriggerBlockTime;

            // 关键：防止 HardLand 在下坡短暂离地后，马上被二次 SoftLand 打断。
            if (landingAnimationPlaying || landingTriggerBlocked)
            {
                velocityY = groundedGravity;
                airHorizontalVelocity = Vector3.zero;
                return;
            }

            // 空中攻击未播完时，落地按“强打断”处理（与受击打断同思路）
            if (fighter != null && fighter.IsInAirAttack)
                fighter.InterruptAttack();

            // 防止旧 Trigger 残留导致同帧或下一帧被错误消费。
            anim.ResetTrigger("HardLand");
            anim.ResetTrigger("SoftLand");

            if (lastAirVelocityY <= hardLandVelocity)
                anim.SetTrigger("HardLand");
            else
                anim.SetTrigger("SoftLand");

            lastLandTriggerTime = Time.time;

            if (combatStats != null && !combatStats.IsDead)
            {
                int fallDamage = CalculateFallDamage(-lastAirVelocityY);
                if (fallDamage > 0)
                    combatStats.TakeHPDamage(fallDamage, DeathCause.Fall);
            }

            velocityY = groundedGravity;
            airHorizontalVelocity = Vector3.zero;
        }
    }

    bool IsLandingAnimationPlaying()
    {
        if (anim == null) return false;

        AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(0);
        if (IsLandingState(st)) return true;

        if (anim.IsInTransition(0))
        {
            AnimatorStateInfo next = anim.GetNextAnimatorStateInfo(0);
            if (IsLandingState(next)) return true;
        }

        return false;
    }

    bool IsLandingState(AnimatorStateInfo st)
    {
        int hard = Animator.StringToHash("HardLand");
        int soft = Animator.StringToHash("SoftLand");

        if (st.shortNameHash == hard || st.shortNameHash == soft)
            return true;

        return st.IsName("HardLand") || st.IsName("SoftLand") ||
               st.IsName("Base Layer.HardLand") || st.IsName("Base Layer.SoftLand") ||
               st.IsTag("HardLand") || st.IsTag("SoftLand") || st.IsTag("Land");
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = isGrounded ? Color.green : Color.red;
        CharacterController cc = controller != null ? controller : GetComponent<CharacterController>();
        if (cc == null) return;

        GetGroundCheckCast(out Vector3 castOrigin, out float radius, out float castDistance);

        // Gizmos 同步：与实际检测使用同一组 origin/radius/distance
        Vector3 castEnd = castOrigin - transform.up * castDistance;

        Gizmos.DrawWireSphere(castOrigin, radius);
        Gizmos.DrawLine(castOrigin, castEnd);
        Gizmos.DrawWireSphere(castEnd, radius);
    }
#endif
}
