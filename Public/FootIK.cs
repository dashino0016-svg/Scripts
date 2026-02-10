using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class FootIK : MonoBehaviour
{
    [Header("Ground Source")]
    [Tooltip("若角色上有 PlayerMove/EnemyMove，会优先使用它们的 groundMask；否则用这里的。")]
    public LayerMask groundMask = ~0;

    [Header("Probe")]
    [Tooltip("探测起点离脚的上移高度。")]
    [SerializeField] float probeUpOffset = 0.6f;

    [Tooltip("向下探测距离。")]
    [SerializeField] float probeDistance = 1.4f;

    [Tooltip("Ray 没命中时的 SphereCast 半径。")]
    [SerializeField] float probeSphereRadius = 0.08f;

    [Tooltip("脚底离地厚度（沿地面法线抬高）。")]
    [SerializeField] float footBottomOffset = 0.06f;

    [Tooltip("单帧最大允许抬脚高度（避免瞬间弹起）。")]
    [SerializeField] float maxStepHeight = 0.45f;

    [Header("Weight")]
    [Range(0f, 1f)]
    [SerializeField] float baseWeight = 1f;

    [Tooltip("IK 权重平滑速度。")]
    [SerializeField] float weightLerpSpeed = 14f;

    [Tooltip("脚离地超过该阈值开始衰减 IK（避免摆腿粘地）。")]
    [SerializeField] float unplantHeight = 0.15f;

    [Tooltip("脚水平速度超过该阈值开始衰减 IK。")]
    [SerializeField] float unplantSpeed = 1.8f;

    [Range(0f, 1f)]
    [Tooltip("速度因子对权重衰减的占比。")]
    [SerializeField] float speedAffect = 0.6f;

    [Header("Rotation")]
    [SerializeField] bool alignToSlope = true;

    [Range(0f, 1f)]
    [SerializeField] float slopeAlignWeight = 1f;

    [SerializeField] float footRotLerpSpeed = 18f;

    [Header("Pelvis")]
    [SerializeField] bool enablePelvisOffset = true;
    [SerializeField] float pelvisDownLimit = 0.1f;
    [SerializeField] float pelvisUpLimit = 0.03f;
    [SerializeField] float pelvisDeadZone = 0.01f;
    [SerializeField] float pelvisLerpSpeed = 10f;

    [Header("Auto Disable")]
    [SerializeField] bool disableWhenAirborne = true;
    [SerializeField] bool disableWhenAttackLock = true;
    [SerializeField] bool disableWhenHitLock = true;
    [SerializeField] bool disableWhenMoveControlLock = false;

    Animator anim;
    PlayerMove playerMove;
    EnemyMove enemyMove;
    PlayerController playerController;
    MeleeFighter fighter;
    CombatReceiver receiver;

    Transform leftFootBone;
    Transform rightFootBone;

    Vector3 prevLeftAnimPos;
    Vector3 prevRightAnimPos;
    bool prevReady;

    float leftWeight;
    float rightWeight;
    float pelvisOffset;

    struct FootSample
    {
        public bool hasGround;
        public Vector3 animPos;
        public Quaternion animRot;
        public Vector3 finalPos;
        public Quaternion finalRot;
        public float desiredWeight;
        public float yDelta;
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

        prevReady = false;
    }

    void OnEnable()
    {
        prevReady = false;
        leftWeight = 0f;
        rightWeight = 0f;
        pelvisOffset = 0f;
    }

    void LateUpdate()
    {
        if (!prevReady)
        {
            prevLeftAnimPos = anim.GetIKPosition(AvatarIKGoal.LeftFoot);
            prevRightAnimPos = anim.GetIKPosition(AvatarIKGoal.RightFoot);
            prevReady = true;
        }
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (anim == null || layerIndex != 0)
            return;

        if (!ShouldRunIK())
        {
            FadeOutIK();
            CacheAnimFootPos();
            return;
        }

        FootSample left = SampleFoot(AvatarIKGoal.LeftFoot, leftFootBone, prevLeftAnimPos);
        FootSample right = SampleFoot(AvatarIKGoal.RightFoot, rightFootBone, prevRightAnimPos);

        ApplyPelvis(left, right);
        ApplyFoot(AvatarIKGoal.LeftFoot, left, ref leftWeight);
        ApplyFoot(AvatarIKGoal.RightFoot, right, ref rightWeight);

        CacheAnimFootPos();
    }

    bool ShouldRunIK()
    {
        if (disableWhenAirborne && !GetGrounded()) return false;
        if (disableWhenAttackLock && fighter != null && fighter.IsInAttackLock) return false;
        if (disableWhenHitLock && receiver != null && receiver.IsInHitLock) return false;
        if (disableWhenMoveControlLock && playerController != null && playerController.IsInMoveControlLock) return false;
        return true;
    }

    bool GetGrounded()
    {
        if (playerMove != null) return playerMove.IsGrounded;
        if (enemyMove != null) return enemyMove.IsGrounded;
        return true;
    }

    FootSample SampleFoot(AvatarIKGoal goal, Transform footBone, Vector3 prevAnimPos)
    {
        FootSample s = default;

        Vector3 animPos = anim.GetIKPosition(goal);
        Quaternion animRot = anim.GetIKRotation(goal);

        if (animPos == Vector3.zero && footBone != null)
        {
            animPos = footBone.position;
            animRot = footBone.rotation;
        }

        s.animPos = animPos;
        s.animRot = animRot;

        Vector3 castOrigin = animPos + Vector3.up * probeUpOffset;

        bool hitOK = Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, probeDistance, groundMask, QueryTriggerInteraction.Ignore);
        if (!hitOK)
            hitOK = Physics.SphereCast(castOrigin, probeSphereRadius, Vector3.down, out hit, probeDistance, groundMask, QueryTriggerInteraction.Ignore);

        if (!hitOK)
        {
            s.hasGround = false;
            s.desiredWeight = 0f;
            s.finalPos = animPos;
            s.finalRot = animRot;
            return s;
        }

        Vector3 normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
        Vector3 targetPos = hit.point + normal * footBottomOffset;

        float stepUp = targetPos.y - animPos.y;
        if (stepUp > maxStepHeight)
            targetPos.y = animPos.y + maxStepHeight;

        Quaternion targetRot = animRot;
        if (alignToSlope)
        {
            Vector3 animFwd = Vector3.ProjectOnPlane(animRot * Vector3.forward, normal);
            if (animFwd.sqrMagnitude < 0.0001f)
                animFwd = Vector3.ProjectOnPlane(transform.forward, normal);

            Quaternion slopeRot = Quaternion.LookRotation(animFwd.normalized, normal);
            targetRot = Quaternion.Slerp(animRot, slopeRot, slopeAlignWeight);
        }

        float footHeight = Mathf.Max(0f, animPos.y - hit.point.y);
        float hFactor = 1f - Mathf.InverseLerp(0f, unplantHeight, footHeight);

        float footSpeed = 0f;
        float dt = Mathf.Max(0.0001f, Time.deltaTime);
        Vector3 horizontalDelta = animPos - prevAnimPos;
        horizontalDelta.y = 0f;
        footSpeed = horizontalDelta.magnitude / dt;
        float sFactor = 1f - Mathf.InverseLerp(0f, unplantSpeed, footSpeed);

        float planted = Mathf.Lerp(hFactor, Mathf.Min(hFactor, sFactor), speedAffect);

        s.hasGround = true;
        s.desiredWeight = Mathf.Clamp01(baseWeight * planted);
        s.finalPos = targetPos;
        s.finalRot = targetRot;
        s.yDelta = targetPos.y - animPos.y;
        return s;
    }

    void ApplyPelvis(FootSample left, FootSample right)
    {
        if (!enablePelvisOffset)
        {
            pelvisOffset = Damp(pelvisOffset, 0f, pelvisLerpSpeed);
            return;
        }

        float desired = 0f;
        bool useLeft = left.hasGround && left.desiredWeight > 0.2f;
        bool useRight = right.hasGround && right.desiredWeight > 0.2f;

        if (useLeft && useRight) desired = Mathf.Min(left.yDelta, right.yDelta);
        else if (useLeft) desired = left.yDelta;
        else if (useRight) desired = right.yDelta;

        if (Mathf.Abs(desired) < pelvisDeadZone)
            desired = 0f;

        desired = Mathf.Clamp(desired, -pelvisDownLimit, pelvisUpLimit);
        pelvisOffset = Damp(pelvisOffset, desired, pelvisLerpSpeed);

        Vector3 bodyPos = anim.bodyPosition;
        bodyPos.y += pelvisOffset;
        anim.bodyPosition = bodyPos;
    }

    void ApplyFoot(AvatarIKGoal goal, FootSample s, ref float weight)
    {
        float targetWeight = s.hasGround ? s.desiredWeight : 0f;
        weight = Damp(weight, targetWeight, weightLerpSpeed);

        anim.SetIKPositionWeight(goal, weight);
        anim.SetIKRotationWeight(goal, alignToSlope ? weight : 0f);

        if (weight <= 0.001f || !s.hasGround)
            return;

        anim.SetIKPosition(goal, s.finalPos);
        if (alignToSlope)
        {
            Quaternion current = anim.GetIKRotation(goal);
            Quaternion blended = Quaternion.Slerp(current, s.finalRot, 1f - Mathf.Exp(-footRotLerpSpeed * Time.deltaTime));
            anim.SetIKRotation(goal, blended);
        }
    }

    void FadeOutIK()
    {
        leftWeight = Damp(leftWeight, 0f, weightLerpSpeed);
        rightWeight = Damp(rightWeight, 0f, weightLerpSpeed);

        anim.SetIKPositionWeight(AvatarIKGoal.LeftFoot, leftWeight);
        anim.SetIKPositionWeight(AvatarIKGoal.RightFoot, rightWeight);
        anim.SetIKRotationWeight(AvatarIKGoal.LeftFoot, alignToSlope ? leftWeight : 0f);
        anim.SetIKRotationWeight(AvatarIKGoal.RightFoot, alignToSlope ? rightWeight : 0f);

        pelvisOffset = Damp(pelvisOffset, 0f, pelvisLerpSpeed);
        if (enablePelvisOffset)
        {
            Vector3 bodyPos = anim.bodyPosition;
            bodyPos.y += pelvisOffset;
            anim.bodyPosition = bodyPos;
        }
    }

    void CacheAnimFootPos()
    {
        prevLeftAnimPos = anim.GetIKPosition(AvatarIKGoal.LeftFoot);
        prevRightAnimPos = anim.GetIKPosition(AvatarIKGoal.RightFoot);

        if (prevLeftAnimPos == Vector3.zero && leftFootBone != null)
            prevLeftAnimPos = leftFootBone.position;

        if (prevRightAnimPos == Vector3.zero && rightFootBone != null)
            prevRightAnimPos = rightFootBone.position;
    }

    static float Damp(float current, float target, float speed)
    {
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
    }
}
