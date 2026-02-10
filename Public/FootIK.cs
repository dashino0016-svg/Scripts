using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class FootIK : MonoBehaviour
{
    [Header("Ground")]
    [Tooltip("若角色上有 PlayerMove/EnemyMove，会优先自动使用它们的 groundMask；否则用这里的。")]
    public LayerMask groundMask = ~0;

    [Header("Cast (Slope Only)")]
    [Tooltip("从脚部上方多高开始向下探测。斜面建议 0.6~0.9")]
    public float castStartHeight = 0.8f;

    [Tooltip("向下探测距离。斜面建议 1.0~1.4")]
    public float castDistance = 1.3f;

    [Tooltip("用于兜底的 SphereCast 半径（减少斜面边缘抖动）。")]
    public float sphereRadius = 0.10f;

    [Tooltip("脚底离地厚度补偿：沿世界 Up 抬高（斜面不陷脚的关键）。通常 0.06~0.12")]
    public float footHeightOffset = 0.10f;

    [Header("IK Weights")]
    [Range(0f, 1f)] public float baseIKWeight = 1f;

    [Tooltip("权重变化速度（越大越跟手）。")]
    public float weightSmoothSpeed = 18f;

    [Header("Auto Contact Heuristic (No Curves)")]
    [Tooltip("动画脚抬离地面超过该高度 -> 自动降低 IK 权重（避免粘地滑步）。斜面建议 0.12~0.20")]
    public float liftThreshold = 0f;

    [Tooltip("脚移动速度超过该值 -> 自动降低 IK 权重（摆腿阶段别硬贴）。")]
    public float footSpeedThreshold = 1.6f;

    [Range(0f, 1f)]
    [Tooltip("脚移动速度对权重的影响强度。0=不考虑速度，1=强考虑速度")]
    public float footSpeedAffect = 0.75f;

    [Header("Foot Rotation (Slope)")]
    public bool enableFootRotation = true;

    [Range(0f, 1f)]
    [Tooltip("旋转贴合法线强度（0=不贴，1=完全贴）。")]
    public float rotationMatch = 1f;

    [Tooltip("旋转平滑速度")]
    public float rotationSmoothSpeed = 14f;

    [Header("Pelvis (Visual Only)")]
    public bool enablePelvis = true;

    [Tooltip("骨盆最多下沉（m）。斜面建议 0.06~0.12")]
    public float pelvisDownClamp = 0.10f;

    [Tooltip("骨盆最多上抬（m）。通常 0.00~0.04（过大会悬空感）。")]
    public float pelvisUpClamp = 0.02f;

    [Tooltip("骨盆调整死区（m）。站立更稳。")]
    public float pelvisDeadZone = 0.015f;

    [Tooltip("骨盆平滑速度")]
    public float pelvisSmoothSpeed = 10f;

    [Header("Gates")]
    public bool disableWhenNotGrounded = true;
    public bool disableWhenAttackLocked = true;
    public bool disableWhenHitLocked = true;
    public bool disableWhenMoveControlLocked = false;

    Animator anim;

    // Optional deps (你的工程)
    PlayerMove playerMove;
    EnemyMove enemyMove;
    PlayerController playerController;
    MeleeFighter fighter;
    CombatReceiver receiver;

    Transform leftFootBone;
    Transform rightFootBone;

    float leftW, rightW;
    float pelvisOffsetSmoothed;

    Vector3 leftFootPrevPos;
    Vector3 rightFootPrevPos;
    bool footPrevInited;

    struct FootSolve
    {
        public bool valid;
        public Vector3 animPos;
        public Quaternion animRot;

        public Vector3 targetPos;
        public Quaternion targetRot;

        public float targetWeight;
        public float deltaY; // targetPos.y - animPos.y
        public Vector3 groundPoint;
        public Vector3 groundNormal;
    }

    void Awake()
    {
        anim = GetComponent<Animator>();

        playerMove = GetComponentInParent<PlayerMove>();
        enemyMove = GetComponentInParent<EnemyMove>();
        playerController = GetComponentInParent<PlayerController>();
        fighter = GetComponentInParent<MeleeFighter>();
        receiver = GetComponentInParent<CombatReceiver>();

        if (playerMove != null) groundMask = playerMove.groundMask;
        else if (enemyMove != null) groundMask = enemyMove.groundMask;

        leftFootBone = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFootBone = anim.GetBoneTransform(HumanBodyBones.RightFoot);

        if (leftFootBone != null) leftFootPrevPos = leftFootBone.position;
        if (rightFootBone != null) rightFootPrevPos = rightFootBone.position;
        footPrevInited = true;
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (anim == null) return;
        if (layerIndex != 0) return; // Base Layer only

        if (!AllowIK())
        {
            ApplyZeroIK();
            UpdateFootPrev();
            return;
        }

        FootSolve left = SolveFoot(AvatarIKGoal.LeftFoot, leftFootBone);
        FootSolve right = SolveFoot(AvatarIKGoal.RightFoot, rightFootBone);

        if (enablePelvis) ApplyPelvis(left, right);
        else pelvisOffsetSmoothed = SmoothExp(pelvisOffsetSmoothed, 0f, pelvisSmoothSpeed);

        ApplyFootIK(AvatarIKGoal.LeftFoot, left, ref leftW);
        ApplyFootIK(AvatarIKGoal.RightFoot, right, ref rightW);

        UpdateFootPrev();
    }

    bool AllowIK()
    {
        bool grounded = GetIsGrounded();
        if (disableWhenNotGrounded && !grounded) return false;
        if (disableWhenAttackLocked && fighter != null && fighter.IsInAttackLock) return false;
        if (disableWhenHitLocked && receiver != null && receiver.IsInHitLock) return false;
        if (disableWhenMoveControlLocked && playerController != null && playerController.IsInMoveControlLock) return false;
        return true;
    }

    bool GetIsGrounded()
    {
        if (playerMove != null) return playerMove.IsGrounded;
        if (enemyMove != null) return enemyMove.IsGrounded;
        return false;
    }

    void ApplyZeroIK()
    {
        leftW = Mathf.MoveTowards(leftW, 0f, weightSmoothSpeed * Time.deltaTime);
        rightW = Mathf.MoveTowards(rightW, 0f, weightSmoothSpeed * Time.deltaTime);

        anim.SetIKPositionWeight(AvatarIKGoal.LeftFoot, leftW);
        anim.SetIKPositionWeight(AvatarIKGoal.RightFoot, rightW);

        anim.SetIKRotationWeight(AvatarIKGoal.LeftFoot, enableFootRotation ? leftW : 0f);
        anim.SetIKRotationWeight(AvatarIKGoal.RightFoot, enableFootRotation ? rightW : 0f);

        if (enablePelvis)
        {
            Vector3 bodyPos = anim.bodyPosition;
            bodyPos.y += pelvisOffsetSmoothed;
            anim.bodyPosition = bodyPos;
        }
    }

    FootSolve SolveFoot(AvatarIKGoal goal, Transform footBone)
    {
        FootSolve s = default;
        if (footBone == null) return s;

        s.animPos = footBone.position;
        s.animRot = footBone.rotation;

        Vector3 origin = s.animPos + Vector3.up * castStartHeight;

        // 1) Raycast precision
        bool hitSomething = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, castDistance, groundMask, QueryTriggerInteraction.Ignore);

        // 2) SphereCast fallback (better stability on uneven triangles)
        if (!hitSomething)
            hitSomething = Physics.SphereCast(origin, sphereRadius, Vector3.down, out hit, castDistance, groundMask, QueryTriggerInteraction.Ignore);

        if (!hitSomething)
        {
            s.valid = false;
            s.targetWeight = 0f;
            return s;
        }

        s.valid = true;
        s.groundPoint = hit.point;
        s.groundNormal = hit.normal.sqrMagnitude < 0.001f ? Vector3.up : hit.normal;

        // 斜面核心：位置 offset 必须沿 Up（否则陡坡必陷脚）
        s.targetPos = s.groundPoint + Vector3.up * footHeightOffset;

        if (enableFootRotation)
        {
            // 把脚的 Up 对齐 groundNormal，但保持 forward 投影在地面上
            Vector3 fwd = Vector3.ProjectOnPlane(s.animRot * Vector3.forward, s.groundNormal);
            if (fwd.sqrMagnitude < 0.0001f)
                fwd = Vector3.ProjectOnPlane(transform.forward, s.groundNormal);

            Quaternion align = Quaternion.LookRotation(fwd.normalized, s.groundNormal.normalized);
            s.targetRot = Quaternion.Slerp(s.animRot, align, rotationMatch);
        }
        else
        {
            s.targetRot = s.animRot;
        }

        // 权重：高度差 + 脚速度
        float lift = s.animPos.y - s.groundPoint.y; // 动画脚离地高度
        float byHeight = 1f - Mathf.InverseLerp(0f, liftThreshold, Mathf.Max(0f, lift));
        byHeight = Mathf.Clamp01(byHeight);

        float speed = GetFootSpeed(footBone, goal);
        float bySpeed = 1f - Mathf.InverseLerp(0f, footSpeedThreshold, speed);
        bySpeed = Mathf.Clamp01(bySpeed);

        float contact = Mathf.Lerp(byHeight, Mathf.Min(byHeight, bySpeed), footSpeedAffect);
        s.targetWeight = baseIKWeight * contact;

        s.deltaY = s.targetPos.y - s.animPos.y;
        return s;
    }

    float GetFootSpeed(Transform footBone, AvatarIKGoal goal)
    {
        if (!footPrevInited || footBone == null) return 0f;

        Vector3 prev = (goal == AvatarIKGoal.LeftFoot) ? leftFootPrevPos : rightFootPrevPos;
        Vector3 cur = footBone.position;

        float dt = Mathf.Max(0.0001f, Time.deltaTime);
        return (cur - prev).magnitude / dt;
    }

    void ApplyPelvis(FootSolve left, FootSolve right)
    {
        // 斜面策略：骨盆主要解决“伸腿够不着”，所以取两脚中更需要下沉的那只（负值更大）
        // 同时：只参考权重较高的脚，避免摆腿时把骨盆拉来拉去。
        float desired = 0f;
        bool has = false;

        bool lUse = left.valid && left.targetWeight > 0.25f;
        bool rUse = right.valid && right.targetWeight > 0.25f;

        if (lUse && rUse)
        {
            desired = Mathf.Min(left.deltaY, right.deltaY);
            has = true;
        }
        else if (lUse) { desired = left.deltaY; has = true; }
        else if (rUse) { desired = right.deltaY; has = true; }

        if (!has) desired = 0f;
        if (Mathf.Abs(desired) < pelvisDeadZone) desired = 0f;

        desired = Mathf.Clamp(desired, -pelvisDownClamp, pelvisUpClamp);
        pelvisOffsetSmoothed = SmoothExp(pelvisOffsetSmoothed, desired, pelvisSmoothSpeed);

        Vector3 bodyPos = anim.bodyPosition;
        bodyPos.y += pelvisOffsetSmoothed;
        anim.bodyPosition = bodyPos;
    }

    void ApplyFootIK(AvatarIKGoal goal, FootSolve s, ref float w)
    {
        float target = (s.valid) ? s.targetWeight : 0f;
        w = Mathf.MoveTowards(w, target, weightSmoothSpeed * Time.deltaTime);

        anim.SetIKPositionWeight(goal, w);
        anim.SetIKRotationWeight(goal, enableFootRotation ? w : 0f);

        if (s.valid && w > 0.001f)
        {
            anim.SetIKPosition(goal, s.targetPos);

            if (enableFootRotation)
            {
                Quaternion cur = anim.GetIKRotation(goal);
                Quaternion blended = Quaternion.Slerp(cur, s.targetRot, 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime));
                anim.SetIKRotation(goal, blended);
            }
        }
    }

    void UpdateFootPrev()
    {
        if (leftFootBone != null) leftFootPrevPos = leftFootBone.position;
        if (rightFootBone != null) rightFootPrevPos = rightFootBone.position;
        footPrevInited = true;
    }

    static float SmoothExp(float current, float target, float speed)
    {
        // 指数平滑：帧率无关
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
    }
}
