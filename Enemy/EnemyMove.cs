using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemyMove : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 1f;     // 对应 BlendTree 阈值 1
    public float runSpeed = 2f;      // 对应 BlendTree 阈值 2
    public float sprintSpeed = 4f;   // 对应 BlendTree 阈值 4

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
    float turnVelocity;

    bool isGrounded;
    bool wasGrounded;
    float lastGroundedTime = -999f;
    Vector3 groundNormal = Vector3.up;
    bool rotationEnabled = true;

    static readonly int AnimIsGrounded = Animator.StringToHash("IsGrounded");
    static readonly int AnimSpeed = Animator.StringToHash("Speed");
    static readonly int AnimMoveX = Animator.StringToHash("MoveX");
    static readonly int AnimMoveY = Animator.StringToHash("MoveY");

    /* ================= 对外接口 ================= */
    public void ResetMotionForTeleport()
    {
        // 清空运动输入
        desiredMoveDir = Vector3.zero;
        desiredSpeed = 0f;
        desiredSpeedLevel = 0;

        // 清空转向/重力残留
        turnVelocity = 0f;
        velocityY = 0f;

        // 让下一帧重新做 grounded 判定
        isGrounded = false;
        wasGrounded = false;
        lastGroundedTime = -999f;
        groundNormal = Vector3.up;
    }

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
        if (anim != null)
            anim.SetBool(AnimIsGrounded, isGrounded);

        if (rotationEnabled)
            Rotate(desiredMoveDir, dt);

        Move(desiredMoveDir, desiredSpeed, dt);

        if (anim != null)
        {
            // ✅ 关键：Animator 的 Speed 参数喂“真实速度值”(0/1/2/4)，匹配你 BlendTree 阈值
            // Sprint/Run 直接钉死，Walk/Idle 可阻尼（你也可以全都直接 set）
            if (desiredSpeedLevel >= 2)
            {
                anim.SetFloat(AnimSpeed, desiredSpeed);
            }
            else
            {
                anim.SetFloat(AnimSpeed, desiredSpeed, speedDampTime, dt);
            }

            UpdateMoveXY(dt);
        }

        ApplyGravity(dt);
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

        if (driveMoveXYOnlyWhenWalking && desiredSpeedLevel != 1)
        {
            anim.SetFloat(AnimMoveX, 0f);
            anim.SetFloat(AnimMoveY, 0f);
            return;
        }

        if (desiredSpeedLevel == 0 || desiredMoveDir.sqrMagnitude < 0.0001f)
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
