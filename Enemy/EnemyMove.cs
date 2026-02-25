using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemyMove : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 1f;     // 对应 BlendTree 阈值 1
    public float runSpeed = 2f;      // 对应 BlendTree 阈值 2
    public float sprintSpeed = 4f;   // 对应 BlendTree 阈值 4

    [Header("Jump")]
    public float hardLandVelocity = -12f;

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
    public float fallDamageScale = 10f;
    [Tooltip("单次落地伤害上限（<=0 表示不上限）")]
    public int fallDamageMax = 9999;

    [Header("Animation")]
    public float speedDampTime = 0.02f;

    [Header("Fall Trigger Tuning")]
    [Tooltip("离地后至少持续该时长才触发 EnterFall，避免下坡接缝/台阶边缘的瞬时误触发")]
    [Min(0f)]
    public float enterFallMinAirTime = 0.08f;

    [Tooltip("触发 EnterFall 的最小下落速度（负值）。速度不够下落时，不进入坠落 Trigger")]
    public float enterFallMinDownwardVelocity = -1f;

    [Header("Debug")]
    [SerializeField] bool debugLanding;

    [Tooltip("是否驱动 2D 方向参数 MoveX/MoveY（用于 Walk 四向）。")]
    public bool driveMoveXY = true;

    [Tooltip("只在 SpeedLevel==1（walk）时驱动 MoveX/MoveY（符合你“只有 walk 四向”的设计）。")]
    public bool driveMoveXYOnlyWhenWalking = true;

    [Tooltip("MoveX/MoveY 的阻尼时间。")]
    public float moveDampTime = 0.06f;

    CharacterController controller;
    Animator anim;
    EnemyController enemyController;
    CombatStats combatStats;

    Vector3 desiredMoveDir;   // AI 给的期望移动方向（世界空间）
    float desiredSpeed;       // ✅ 真实速度值（0/1/2/4）
    int desiredSpeedLevel;    // ✅ 代号（0/1/2/3）

    float velocityY;
    float lastAirVelocityY;
    float lastImpactVelocityY;
    float turnVelocity;
    Vector3 airHorizontalVelocity;
    float airborneElapsed;
    bool pendingEnterFall;

    bool isGrounded;
    bool isGroundedRaw;
    bool wasGrounded;
    bool wasGroundedRaw;
    float lastGroundedTime = -999f;
    Vector3 groundNormal = Vector3.up;
    bool rotationEnabled = true;

    static readonly int AnimIsGrounded = Animator.StringToHash("IsGrounded");
    static readonly int AnimEnterFall = Animator.StringToHash("EnterFall");
    static readonly int AnimVerticalVelocity = Animator.StringToHash("VerticalVelocity");
    static readonly int AnimSpeed = Animator.StringToHash("Speed");
    static readonly int AnimMoveX = Animator.StringToHash("MoveX");
    static readonly int AnimMoveY = Animator.StringToHash("MoveY");


    /* ================= 对外接口 ================= */

    public void SetMoveDirection(Vector3 dir)
    {
        desiredMoveDir = dir.sqrMagnitude > 1f ? dir.normalized : dir;
    }

    // ✅ AI 传入“代号”，EnemyMove 内部映射到“真实速度”
    public void SetMoveSpeedLevel(int level)
    {
        desiredSpeedLevel = level;

        desiredSpeed = level switch
        {
            1 => walkSpeed,
            2 => runSpeed,
            3 => sprintSpeed,
            _ => 0f
        };
    }

    public void SetRotationEnabled(bool enabled)
    {
        rotationEnabled = enabled;
    }

    public bool IsGrounded => isGrounded;
    public bool IsGroundedRaw => isGroundedRaw;
    public int DesiredSpeedLevel => desiredSpeedLevel;
    public float LastImpactVelocityY => lastImpactVelocityY;
    /* ================= Unity ================= */

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        anim = GetComponent<Animator>();
        enemyController = GetComponent<EnemyController>();
        combatStats = GetComponent<CombatStats>();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (enemyController != null)
            dt *= enemyController.LocalTimeScale;

        wasGrounded = isGrounded;
        wasGroundedRaw = isGroundedRaw;

        bool groundedRawNow = CheckGroundedRaw();
        isGroundedRaw = groundedRawNow;

        if (groundedRawNow)
            lastGroundedTime = Time.time;

        isGrounded = groundedRawNow || (Time.time - lastGroundedTime <= groundedGraceTime);

        // 进入离地瞬间记录朝向：坠落+落地期间锁定该朝向
        if (wasGrounded && !isGrounded)
        {
            if (enemyController != null)
                enemyController.CaptureAirLandFacingLock(transform.rotation);

            // 先标记“待触发坠落”，由后续条件（离地时长+下落速度）决定是否真正触发
            airborneElapsed = 0f;
            pendingEnterFall = true;

            lastAirVelocityY = velocityY;
        }

        if (isGrounded)
        {
            airborneElapsed = 0f;
            pendingEnterFall = false;
        }
        else
        {
            airborneElapsed += dt;
        }

        if (pendingEnterFall && !isGrounded)
        {
            if (airborneElapsed >= enterFallMinAirTime && velocityY <= enterFallMinDownwardVelocity)
            {
                if (anim != null)
                    anim.SetTrigger(AnimEnterFall);

                pendingEnterFall = false;
            }
        }

        if (anim != null)
        {
            anim.SetBool(AnimIsGrounded, isGrounded);
            anim.SetFloat(AnimVerticalVelocity, velocityY);

            if (isGrounded)
                anim.ResetTrigger(AnimEnterFall);
        }

        bool landLock = enemyController != null && enemyController.IsInLandLock;
        Vector3 moveDir = landLock ? Vector3.zero : desiredMoveDir;
        float moveSpeed = landLock ? 0f : desiredSpeed;
        int speedLevel = landLock ? 0 : desiredSpeedLevel;

        bool airLandFacingLock = enemyController != null && enemyController.ShouldApplyAirLandFacingLock;
        if (airLandFacingLock)
            transform.rotation = enemyController.GetAirLandFacingLockRotation();

        if (rotationEnabled && !landLock && !airLandFacingLock)
            Rotate(moveDir, dt);

        Move(moveDir, moveSpeed, dt);

        if (anim != null)
        {
            // ✅ 关键：Animator 的 Speed 参数喂“真实速度值”(0/1/2/4)，匹配你 BlendTree 阈值
            // Sprint/Run 直接钉死，Walk/Idle 可阻尼（你也可以全都直接 set）
            if (speedLevel >= 2)
            {
                anim.SetFloat(AnimSpeed, moveSpeed);
            }
            else
            {
                anim.SetFloat(AnimSpeed, moveSpeed, speedDampTime, dt);
            }

            UpdateMoveXY(dt);
        }

        ApplyGravity(dt);
        HandleLanding();
    }

    /* ================= Ground ================= */

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

    void GetGroundCheckCast(out Vector3 castOrigin, out float radius, out float castDistance)
    {
        radius = Mathf.Max(0.01f, groundCheckRadius);
        castDistance = Mathf.Max(0.01f, groundCheckDistance);

        CharacterController cc = controller != null ? controller : GetComponent<CharacterController>();
        Vector3 bottom = GetCapsuleBottomWorld(cc);
        float centerUp = Mathf.Max(0.01f, radius + groundCheckOffset);
        castOrigin = bottom + transform.up * centerUp;
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

        bool ccGrounded = controller != null && controller.isGrounded;
        groundNormal = transform.up;
        return ccGrounded;
    }

    /* ================= Movement ================= */

    void Rotate(Vector3 dir, float dt)
    {
        if (dir.sqrMagnitude < 0.001f) return;

        float target = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        float angle = Mathf.SmoothDampAngle(
            transform.eulerAngles.y,
            target,
            ref turnVelocity,
            rotationSmoothTime,
            Mathf.Infinity,
            dt
        );

        transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }

    void Move(Vector3 dir, float speed, float dt)
    {
        Vector3 horizontal;

        if (isGrounded)
        {
            if (velocityY < groundedGravity)
                velocityY = groundedGravity;

            // ✅ 与玩家一致：地面移动先做坡面投影，保留贴坡分量
            horizontal = Vector3.ProjectOnPlane(dir * speed, groundNormal);

            // ✅ 与玩家一致：非跳跃离地时保留上一帧水平速度，降低下坡短暂离地抖动
            if (velocityY <= groundedGravity + 0.001f)
                airHorizontalVelocity = dir * speed;
        }
        else
        {
            // 敌人无主动空中机动：离地后仅保持离地瞬间的水平惯性，避免下坡边缘抖动。
            horizontal = airHorizontalVelocity;
        }

        Vector3 motion = horizontal;
        // ✅ 不覆盖 horizontal 的 Y（贴坡分量），而是叠加重力/竖直速度
        motion += Vector3.up * velocityY;

        controller.Move(motion * dt);
    }

    void UpdateMoveXY(float dt)
    {
        if (!driveMoveXY) return;

        int effectiveSpeedLevel = (enemyController != null && enemyController.IsInLandLock) ? 0 : desiredSpeedLevel;

        if (driveMoveXYOnlyWhenWalking && effectiveSpeedLevel != 1)
        {
            anim.SetFloat(AnimMoveX, 0f);
            anim.SetFloat(AnimMoveY, 0f);
            return;
        }

        if (effectiveSpeedLevel == 0 || desiredMoveDir.sqrMagnitude < 0.0001f)
        {
            anim.SetFloat(AnimMoveX, 0f, moveDampTime, dt);
            anim.SetFloat(AnimMoveY, 0f, moveDampTime, dt);
            return;
        }

        Vector3 local = transform.InverseTransformDirection(desiredMoveDir.normalized);

        float x = Mathf.Clamp(local.x, -1f, 1f);
        float y = Mathf.Clamp(local.z, -1f, 1f);

        anim.SetFloat(AnimMoveX, x, moveDampTime, dt);
        anim.SetFloat(AnimMoveY, y, moveDampTime, dt);
    }

    /* ================= Gravity ================= */

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

    void ApplyGravity(float dt)
    {
        if (!isGrounded)
        {
            velocityY += gravity * dt;
            velocityY = Mathf.Max(velocityY, terminalVelocity);
            lastAirVelocityY = velocityY;
        }
    }

    void HandleLanding()
    {
        if (!wasGrounded && isGrounded)
        {
            var melee = GetComponent<MeleeFighter>();
            if (melee != null)
                melee.InterruptAttack();

            var range = GetComponent<RangeFighter>();
            if (range != null)
                range.InterruptShoot();

            bool hardLand = (lastAirVelocityY <= hardLandVelocity);
            lastImpactVelocityY = lastAirVelocityY;

            // 预先进入落地锁，防止 AI 在动画事件触发前抢回攻击逻辑导致落地状态被打断。
            if (enemyController != null)
                enemyController.LandBegin();

            if (anim != null)
            {
                // 防止旧 Trigger 残留导致同帧或下一帧被错误消费。
                anim.ResetTrigger("HardLand");
                anim.ResetTrigger("SoftLand");

                // 按需求：落地完全使用 Trigger 触发，不使用 CrossFade。
                if (hardLand)
                    anim.SetTrigger("HardLand");
                else
                    anim.SetTrigger("SoftLand");
            }

            if (combatStats != null && !combatStats.IsDead)
            {
                int fallDamage = CalculateFallDamage(-lastAirVelocityY);
                if (fallDamage > 0)
                    combatStats.TakeHPDamage(fallDamage, DeathCause.Fall);
            }

            if (debugLanding)
            {
                Debug.Log(
                    $"[EnemyMove] Landing on {name} | hard={hardLand} | lastImpactY={lastImpactVelocityY:F3} | hardLandVelocity={hardLandVelocity:F3} | raw={isGroundedRaw} | grounded={isGrounded}",
                    this);
            }

            velocityY = groundedGravity;
            airHorizontalVelocity = Vector3.zero;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = isGrounded ? Color.green : Color.red;
        CharacterController cc = controller != null ? controller : GetComponent<CharacterController>();
        if (cc == null) return;

        GetGroundCheckCast(out Vector3 castOrigin, out float radius, out float castDistance);
        Vector3 castEnd = castOrigin - transform.up * castDistance;

        Gizmos.DrawWireSphere(castOrigin, radius);
        Gizmos.DrawLine(castOrigin, castEnd);
        Gizmos.DrawWireSphere(castEnd, radius);
    }
#endif
}
