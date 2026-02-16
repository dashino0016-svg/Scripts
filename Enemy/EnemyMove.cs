using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemyMove : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 1f;     // 对应 BlendTree 阈值 1
    public float runSpeed = 2f;      // 对应 BlendTree 阈值 2
    public float sprintSpeed = 4f;   // 对应 BlendTree 阈值 4

    [Header("Jump")]
    public float hardLandVelocity = -10f;

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

    [Header("Animation")]
    public float speedDampTime = 0.02f;

    [Tooltip("是否驱动 2D 方向参数 MoveX/MoveY（用于 Walk 四向）。")]
    public bool driveMoveXY = true;

    [Tooltip("只在 SpeedLevel==1（walk）时驱动 MoveX/MoveY（符合你“只有 walk 四向”的设计）。")]
    public bool driveMoveXYOnlyWhenWalking = true;

    [Tooltip("MoveX/MoveY 的阻尼时间。")]
    public float moveDampTime = 0.06f;

    CharacterController controller;
    Animator anim;
    EnemyController enemyController;

    Vector3 desiredMoveDir;   // AI 给的期望移动方向（世界空间）
    float desiredSpeed;       // ✅ 真实速度值（0/1/2/4）
    int desiredSpeedLevel;    // ✅ 代号（0/1/2/3）

    float velocityY;
    float lastAirVelocityY;
    float turnVelocity;

    bool isGrounded;
    bool wasGrounded;
    float lastGroundedTime = -999f;
    Vector3 groundNormal = Vector3.up;
    bool rotationEnabled = true;

    static readonly int AnimIsGrounded = Animator.StringToHash("IsGrounded");
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
    public int DesiredSpeedLevel => desiredSpeedLevel;
    /* ================= Unity ================= */

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        anim = GetComponent<Animator>();
        enemyController = GetComponent<EnemyController>();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (enemyController != null)
            dt *= enemyController.LocalTimeScale;

        wasGrounded = isGrounded;

        bool groundedNow = CheckGroundedRaw();
        if (groundedNow)
            lastGroundedTime = Time.time;

        isGrounded = groundedNow || (Time.time - lastGroundedTime <= groundedGraceTime);

        // 进入离地瞬间记录朝向：坠落+落地期间锁定该朝向
        if (wasGrounded && !isGrounded && enemyController != null)
            enemyController.CaptureAirLandFacingLock(transform.rotation);

        if (anim != null)
        {
            anim.SetBool(AnimIsGrounded, isGrounded);
            anim.SetFloat(AnimVerticalVelocity, velocityY);
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

    Vector3 GetCapsuleBottomWorld()
    {
        Vector3 centerWorld = transform.TransformPoint(controller.center);
        float halfHeight = Mathf.Max(controller.height * 0.5f, controller.radius);
        float bottomOffset = halfHeight - controller.radius;
        return centerWorld - transform.up * bottomOffset;
    }

    bool CheckGroundedRaw()
    {
        if (controller != null && controller.isGrounded)
        {
            groundNormal = transform.up;
            return true;
        }

        float radius = Mathf.Max(0.01f, groundCheckRadius);
        float castDistance = Mathf.Max(0.01f, groundCheckDistance + Mathf.Max(0f, -groundCheckOffset));

        Vector3 bottom = GetCapsuleBottomWorld();
        Vector3 castOrigin = bottom + transform.up * radius;

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

        groundNormal = transform.up;
        return false;
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
        Vector3 horizontal = dir * speed;

        if (isGrounded)
        {
            if (velocityY < groundedGravity)
                velocityY = groundedGravity;

            horizontal = Vector3.ProjectOnPlane(horizontal, groundNormal);
        }

        Vector3 motion = horizontal;
        motion.y = velocityY;

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

            // 预先进入落地锁，防止 AI 在动画事件触发前抢回攻击逻辑导致落地状态被打断。
            if (enemyController != null)
                enemyController.LandBegin();

            if (anim != null)
            {
                // 按需求：落地完全使用 Trigger 触发，不使用 CrossFade。
                if (hardLand)
                    anim.SetTrigger("HardLand");
                else
                    anim.SetTrigger("SoftLand");
            }

            velocityY = groundedGravity;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = isGrounded ? Color.green : Color.red;
        CharacterController cc = controller != null ? controller : GetComponent<CharacterController>();
        if (cc == null) return;

        float radius = Mathf.Max(0.01f, groundCheckRadius);
        float castDistance = Mathf.Max(0.01f, groundCheckDistance + Mathf.Max(0f, -groundCheckOffset));

        Vector3 centerWorld = transform.TransformPoint(cc.center);
        float halfHeight = Mathf.Max(cc.height * 0.5f, cc.radius);
        float bottomOffset = halfHeight - cc.radius;
        Vector3 bottom = centerWorld - transform.up * bottomOffset;

        Vector3 castOrigin = bottom + transform.up * radius;
        Vector3 castEnd = castOrigin - transform.up * castDistance;

        Gizmos.DrawWireSphere(castOrigin, radius);
        Gizmos.DrawLine(castOrigin, castEnd);
        Gizmos.DrawWireSphere(castEnd, radius);
    }
#endif
}
