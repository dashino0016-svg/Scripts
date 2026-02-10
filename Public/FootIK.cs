using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class FootIK : MonoBehaviour
{
    [Header("Ground")]
    [Tooltip("若角色上有 PlayerMove/EnemyMove，会优先自动使用它们的 groundMask；否则使用这里的 LayerMask")]
    public LayerMask groundMask = ~0;

    [Header("Cast")]
    [Tooltip("从脚骨骼上方多少米开始向下探测")]
    public float castStartHeight = 0.75f;

    [Tooltip("向下探测总距离")]
    public float castDistance = 1.4f;

    [Tooltip("先 Raycast，失败时使用 SphereCast 兜底")]
    public float sphereCastRadius = 0.1f;

    [Tooltip("在脚底基础上额外抬高，防止陷入地面")]
    public float extraFootHeight = 0.015f;

    [Header("IK Weight")]
    [Range(0f, 1f)] public float baseIKWeight = 1f;

    [Tooltip("IK 权重平滑速度")]
    public float weightSmoothSpeed = 14f;

    [Header("Auto Contact Heuristic")]
    [Tooltip("动画脚离地超过阈值时降低 IK，避免摆腿阶段粘地")]
    public float liftThreshold = 0.16f;

    [Tooltip("脚移动速度过大时降低 IK，避免滑步")]
    public float footSpeedThreshold = 1.8f;

    [Range(0f, 1f)]
    [Tooltip("速度抑制强度：0=只看离地高度，1=高度+速度都强抑制")]
    public float footSpeedAffect = 0.65f;

    [Header("Rotation")]
    public bool enableFootRotation = true;

    [Range(0f, 1f)]
    [Tooltip("脚掌对齐地面法线的强度")]
    public float rotationMatch = 1f;

    [Tooltip("脚掌旋转平滑速度")]
    public float rotationSmoothSpeed = 16f;

    [Header("Pelvis")]
    public bool enablePelvis = true;

    [Tooltip("骨盆最大下沉值")]
    public float pelvisDownClamp = 0.1f;

    [Tooltip("骨盆最大上抬值")]
    public float pelvisUpClamp = 0.02f;

    [Tooltip("骨盆死区，减少小抖动")]
    public float pelvisDeadZone = 0.01f;

    [Tooltip("骨盆平滑速度")]
    public float pelvisSmoothSpeed = 10f;

    [Header("Gates")]
    public bool disableWhenNotGrounded = true;
    public bool disableWhenAttackLocked = true;
    public bool disableWhenHitLocked = true;
    public bool disableWhenMoveControlLocked = false;

    Animator anim;

    PlayerMove playerMove;
    EnemyMove enemyMove;
    PlayerController playerController;
    MeleeFighter fighter;
    CombatReceiver receiver;

    Transform leftFootBone;
    Transform rightFootBone;

    float leftWeight;
    float rightWeight;
    float pelvisOffsetSmoothed;

    Vector3 leftFootPrevPos;
    Vector3 rightFootPrevPos;

    struct FootSolve
    {
        public bool valid;
        public Vector3 animPos;
        public Quaternion animRot;

        public Vector3 targetPos;
        public Quaternion targetRot;

        public Vector3 groundPoint;
        public Vector3 groundNormal;

        public float targetWeight;
        public float deltaY;
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
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (anim == null || layerIndex != 0) return;

        if (!AllowIK())
        {
            ApplyZeroIK();
            CacheFootPos();
            return;
        }

        FootSolve left = SolveFoot(AvatarIKGoal.LeftFoot, leftFootBone);
        FootSolve right = SolveFoot(AvatarIKGoal.RightFoot, rightFootBone);

        if (enablePelvis) ApplyPelvis(left, right);
        else
            pelvisOffsetSmoothed = SmoothExp(pelvisOffsetSmoothed, 0f, pelvisSmoothSpeed);

        ApplyFoot(AvatarIKGoal.LeftFoot, left, ref leftWeight);
        ApplyFoot(AvatarIKGoal.RightFoot, right, ref rightWeight);

        CacheFootPos();
    }

    bool AllowIK()
    {
        if (disableWhenNotGrounded && !GetIsGrounded()) return false;
        if (disableWhenAttackLocked && fighter != null && fighter.IsInAttackLock) return false;
        if (disableWhenHitLocked && receiver != null && receiver.IsInHitLock) return false;
        if (disableWhenMoveControlLocked && playerController != null && playerController.IsInMoveControlLock) return false;
        return true;
    }

    bool GetIsGrounded()
    {
        if (playerMove != null) return playerMove.IsGrounded;
        if (enemyMove != null) return enemyMove.IsGrounded;
        return true;
    }

    FootSolve SolveFoot(AvatarIKGoal goal, Transform footBone)
    {
        FootSolve s = default;
        if (footBone == null) return s;

        s.animPos = footBone.position;
        s.animRot = footBone.rotation;

        Vector3 origin = s.animPos + Vector3.up * castStartHeight;

        bool hitSomething = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, castDistance, groundMask, QueryTriggerInteraction.Ignore);
        if (!hitSomething)
            hitSomething = Physics.SphereCast(origin, sphereCastRadius, Vector3.down, out hit, castDistance, groundMask, QueryTriggerInteraction.Ignore);

        if (!hitSomething)
        {
            s.valid = false;
            s.targetWeight = 0f;
            return s;
        }

        s.valid = true;
        s.groundPoint = hit.point;
        s.groundNormal = (hit.normal.sqrMagnitude > 0.0001f) ? hit.normal.normalized : Vector3.up;

        float footBottom = goal == AvatarIKGoal.LeftFoot ? anim.leftFeetBottomHeight : anim.rightFeetBottomHeight;
        float footOffset = Mathf.Max(footBottom + extraFootHeight, 0f);

        s.targetPos = s.groundPoint + Vector3.up * footOffset;

        if (enableFootRotation)
        {
            Vector3 fwdOnPlane = Vector3.ProjectOnPlane(s.animRot * Vector3.forward, s.groundNormal);
            if (fwdOnPlane.sqrMagnitude < 0.0001f)
                fwdOnPlane = Vector3.ProjectOnPlane(transform.forward, s.groundNormal);

            Quaternion aligned = Quaternion.LookRotation(fwdOnPlane.normalized, s.groundNormal);
            s.targetRot = Quaternion.Slerp(s.animRot, aligned, rotationMatch);
        }
        else
        {
            s.targetRot = s.animRot;
        }

        float lift = Mathf.Max(0f, s.animPos.y - s.groundPoint.y);
        float byHeight = 1f - Mathf.InverseLerp(0f, Mathf.Max(0.001f, liftThreshold), lift);

        float speed = GetFootSpeed(goal, s.animPos);
        float bySpeed = 1f - Mathf.InverseLerp(0f, Mathf.Max(0.001f, footSpeedThreshold), speed);

        float contact = Mathf.Lerp(byHeight, Mathf.Min(byHeight, bySpeed), footSpeedAffect);
        s.targetWeight = baseIKWeight * Mathf.Clamp01(contact);

        s.deltaY = s.targetPos.y - s.animPos.y;
        return s;
    }

    float GetFootSpeed(AvatarIKGoal goal, Vector3 currentPos)
    {
        Vector3 prev = goal == AvatarIKGoal.LeftFoot ? leftFootPrevPos : rightFootPrevPos;
        float dt = Mathf.Max(0.0001f, Time.deltaTime);
        return (currentPos - prev).magnitude / dt;
    }

    void ApplyPelvis(FootSolve left, FootSolve right)
    {
        bool leftUse = left.valid && left.targetWeight > 0.25f;
        bool rightUse = right.valid && right.targetWeight > 0.25f;

        float desired = 0f;

        if (leftUse && rightUse)
            desired = Mathf.Min(left.deltaY, right.deltaY);
        else if (leftUse)
            desired = left.deltaY;
        else if (rightUse)
            desired = right.deltaY;

        if (Mathf.Abs(desired) < pelvisDeadZone) desired = 0f;

        desired = Mathf.Clamp(desired, -pelvisDownClamp, pelvisUpClamp);
        pelvisOffsetSmoothed = SmoothExp(pelvisOffsetSmoothed, desired, pelvisSmoothSpeed);

        Vector3 body = anim.bodyPosition;
        body.y += pelvisOffsetSmoothed;
        anim.bodyPosition = body;
    }

    void ApplyFoot(AvatarIKGoal goal, FootSolve s, ref float weight)
    {
        float targetWeight = s.valid ? s.targetWeight : 0f;
        weight = Mathf.MoveTowards(weight, targetWeight, weightSmoothSpeed * Time.deltaTime);

        anim.SetIKPositionWeight(goal, weight);
        anim.SetIKRotationWeight(goal, (enableFootRotation ? weight : 0f));

        if (!s.valid || weight <= 0.001f) return;

        anim.SetIKPosition(goal, s.targetPos);

        if (enableFootRotation)
        {
            Quaternion cur = anim.GetIKRotation(goal);
            Quaternion smooth = Quaternion.Slerp(cur, s.targetRot, 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime));
            anim.SetIKRotation(goal, smooth);
        }
    }

    void ApplyZeroIK()
    {
        leftWeight = Mathf.MoveTowards(leftWeight, 0f, weightSmoothSpeed * Time.deltaTime);
        rightWeight = Mathf.MoveTowards(rightWeight, 0f, weightSmoothSpeed * Time.deltaTime);

        anim.SetIKPositionWeight(AvatarIKGoal.LeftFoot, leftWeight);
        anim.SetIKPositionWeight(AvatarIKGoal.RightFoot, rightWeight);

        anim.SetIKRotationWeight(AvatarIKGoal.LeftFoot, enableFootRotation ? leftWeight : 0f);
        anim.SetIKRotationWeight(AvatarIKGoal.RightFoot, enableFootRotation ? rightWeight : 0f);
    }

    void CacheFootPos()
    {
        if (leftFootBone != null) leftFootPrevPos = leftFootBone.position;
        if (rightFootBone != null) rightFootPrevPos = rightFootBone.position;
    }

    static float SmoothExp(float current, float target, float speed)
    {
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
    }
}
