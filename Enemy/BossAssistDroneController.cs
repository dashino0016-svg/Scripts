using UnityEngine;

[DisallowMultipleComponent]
public class BossAssistDroneController : MonoBehaviour
{
    public enum DroneState { Docked, Entering, Active, Returning }

    [Header("Refs")]
    [SerializeField] Transform bossRoot;
    [SerializeField] CombatStats bossStats;
    [SerializeField] EnemyController bossController;

    [Header("Anchors")]
    [Tooltip("无人机停靠点（Boss 背上挂点）。")]
    [SerializeField] Transform dockAnchor;
    [Tooltip("无人机环绕中心（可与 dockAnchor 相同）。")]
    [SerializeField] Transform activeCenter;

    [Header("Dock Pose")]
    [SerializeField] Vector3 dockLocalPos;
    [SerializeField] Vector3 dockLocalEuler;

    [Header("Active Hover")]
    [SerializeField] Vector3 activeOffset = new Vector3(0f, 1.8f, -0.2f);
    [SerializeField, Min(0f)] float orbitRadius = 0.45f;
    [SerializeField, Min(0f)] float orbitSpeed = 2.2f;
    [SerializeField, Min(0f)] float followSmooth = 10f;

    [Header("Deploy Condition")]
    [Range(0.05f, 1f)]
    [SerializeField] float deployHpRatio = 0.5f;

    [Header("Enter / Return")]
    [SerializeField, Min(0f)] float enterDuration = 0.35f;
    [SerializeField, Min(0f)] float returnDuration = 0.30f;

    [Header("Targeting")]
    [SerializeField] LayerMask targetMask;
    [SerializeField, Min(0.1f)] float targetRange = 20f;
    [SerializeField, Min(0.02f)] float targetRefreshInterval = 0.15f;
    [SerializeField, Min(0f)] float faceYawSpeed = 12f;

    [Header("Facing Debug / Reference")]
    [SerializeField] bool faceLockedTarget = true;
    [SerializeField] Transform yawReference;
    [SerializeField] float yawOffsetDegrees = 0f;
    [SerializeField] bool snapYaw = false;

    [Header("Muzzles")]
    [SerializeField] Transform tapMuzzleA;
    [SerializeField] Transform tapMuzzleB;
    [SerializeField] Transform chargedMuzzle;
    [SerializeField] Transform muzzleLegacy;

    [Header("Projectile")]
    [SerializeField] GameObject tapProjectilePrefab;
    [SerializeField] GameObject chargedProjectilePrefab;
    [SerializeField] AttackConfig tapAttackConfig;
    [SerializeField] AttackConfig chargedAttackConfig;
    [SerializeField, Min(0f)] float spawnForwardOffset = 0.06f;

    [Header("Fire Decision")]
    [SerializeField, Min(0f)] float tapCooldown = 0.25f;
    [SerializeField, Min(0f)] float chargedCooldown = 1.2f;
    [SerializeField, Min(0.02f)] float decisionInterval = 0.2f;
    [SerializeField, Range(0f, 1f)] float tapChancePerDecision = 0.65f;
    [SerializeField, Range(0f, 1f)] float chargedChancePerDecision = 0.20f;

    [Header("Charged Shot Timing")]
    [Tooltip("播放蓄力/蓄力射击音效后，延迟多久再真正发射子弹（用于对齐音效里后半段射击点）。")]
    [SerializeField, Min(0f)] float chargedFireDelay = 0.25f;

    [Header("SFX")]
    [Tooltip("普通点射音效。")]
    [SerializeField] AudioClip tapShotClip;
    [Tooltip("Boss 版：蓄力与蓄力射击共用同一音效（先播，延迟后发弹）。")]
    [SerializeField] AudioClip chargedShotClip;
    [SerializeField] AudioSource tapShotSourceA;
    [SerializeField] AudioSource tapShotSourceB;
    [SerializeField] AudioSource chargedShotSource;

    DroneState state = DroneState.Docked;
    Transform currentTarget;

    float orbitPhase;
    float nextTargetRefreshTime;
    float nextDecisionTime;
    float nextTapAllowedTime;
    float nextChargedAllowedTime;

    bool chargedPending;
    float chargedFireAt;
    Transform chargedTarget;

    Vector3 transitionStartPos;
    Quaternion transitionStartRot;
    Vector3 transitionEndPos;
    Quaternion transitionEndRot;
    float transitionStartTime;
    float transitionDuration;

    void Awake()
    {
        if (bossRoot == null) bossRoot = transform.root;
        if (bossStats == null) bossStats = bossRoot != null ? bossRoot.GetComponent<CombatStats>() : null;
        if (bossController == null) bossController = bossRoot != null ? bossRoot.GetComponent<EnemyController>() : null;

        if (activeCenter == null) activeCenter = dockAnchor;

        SnapToDockPose();
    }

    void Update()
    {
        if (bossStats == null || bossStats.IsDead)
        {
            if (state != DroneState.Docked)
                BeginReturn();
            UpdateState();
            return;
        }

        bool shouldDeploy = bossStats.CurrentHP > 0 && bossStats.CurrentHP <= Mathf.CeilToInt(bossStats.maxHP * deployHpRatio);

        if (shouldDeploy)
        {
            if (state == DroneState.Docked)
                BeginEnter();
        }
        else
        {
            if (state == DroneState.Active)
                BeginReturn();
        }

        UpdateState();
    }

    void UpdateState()
    {
        switch (state)
        {
            case DroneState.Docked:
                SnapToDockPose();
                break;
            case DroneState.Entering:
                UpdateTransition(DroneState.Active);
                break;
            case DroneState.Active:
                UpdateActive();
                break;
            case DroneState.Returning:
                UpdateTransition(DroneState.Docked);
                break;
        }
    }

    void UpdateTransition(DroneState endState)
    {
        float t = transitionDuration <= 0.0001f ? 1f : Mathf.Clamp01((Time.time - transitionStartTime) / transitionDuration);
        t = t * t * (3f - 2f * t);

        transform.position = Vector3.Lerp(transitionStartPos, transitionEndPos, t);
        transform.rotation = Quaternion.Slerp(transitionStartRot, transitionEndRot, t);

        if (t < 1f) return;

        state = endState;
        if (endState == DroneState.Docked)
            SnapToDockPose();
    }

    void UpdateActive()
    {
        if (activeCenter == null)
        {
            BeginReturn();
            return;
        }

        orbitPhase += orbitSpeed * Time.deltaTime;
        Vector3 center = activeCenter.position + activeOffset;
        Vector3 orbitOffset = new Vector3(Mathf.Cos(orbitPhase), 0f, Mathf.Sin(orbitPhase)) * orbitRadius;
        Vector3 targetPos = center + orbitOffset;

        transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-followSmooth * Time.deltaTime));

        if (Time.time >= nextTargetRefreshTime)
        {
            nextTargetRefreshTime = Time.time + targetRefreshInterval;
            RefreshTarget();
        }

        FaceTargetOrBossForward();
        TickFireDecision();
        TickChargedPending();
    }

    void TickFireDecision()
    {
        if (chargedPending) return;
        if (Time.time < nextDecisionTime) return;
        nextDecisionTime = Time.time + decisionInterval;

        if (currentTarget == null) return;

        if (Time.time >= nextChargedAllowedTime && Random.value <= chargedChancePerDecision)
        {
            TryStartCharged(currentTarget);
            return;
        }

        if (Time.time >= nextTapAllowedTime && Random.value <= tapChancePerDecision)
            TryFireTap(currentTarget);
    }

    void TickChargedPending()
    {
        if (!chargedPending) return;
        if (Time.time < chargedFireAt) return;

        chargedPending = false;

        if (chargedTarget == null)
            chargedTarget = currentTarget;

        FireChargedNow(chargedTarget);
        chargedTarget = null;
    }

    void TryStartCharged(Transform target)
    {
        if (chargedAttackConfig == null) return;

        nextChargedAllowedTime = Time.time + chargedCooldown;

        // Boss 需求：蓄力与蓄力射击同一音效；先播，再延迟发射
        PlayOneShot(chargedShotSource, chargedShotClip, transform.position);

        chargedPending = true;
        chargedFireAt = Time.time + Mathf.Max(0f, chargedFireDelay);
        chargedTarget = target;
    }

    void FireChargedNow(Transform target)
    {
        GameObject prefab = chargedProjectilePrefab != null ? chargedProjectilePrefab : tapProjectilePrefab;
        if (prefab == null || chargedAttackConfig == null) return;

        Transform muzzle = chargedMuzzle != null ? chargedMuzzle : muzzleLegacy;
        FireProjectileFromMuzzle(prefab, muzzle, target, chargedAttackConfig);
    }

    void TryFireTap(Transform target)
    {
        if (tapAttackConfig == null || tapProjectilePrefab == null) return;

        nextTapAllowedTime = Time.time + tapCooldown;

        Transform mA = tapMuzzleA != null ? tapMuzzleA : muzzleLegacy;
        Transform mB = tapMuzzleB;

        bool firedA = FireProjectileFromMuzzle(tapProjectilePrefab, mA, target, tapAttackConfig);
        bool firedB = FireProjectileFromMuzzle(tapProjectilePrefab, mB, target, tapAttackConfig);

        if (tapShotClip != null)
        {
            if (firedA) PlayOneShot(tapShotSourceA, tapShotClip, mA != null ? mA.position : transform.position);
            if (firedB) PlayOneShot(tapShotSourceB, tapShotClip, mB != null ? mB.position : transform.position);
            if (!firedA && !firedB)
                PlayOneShot(tapShotSourceA, tapShotClip, transform.position);
        }
    }

    bool FireProjectileFromMuzzle(GameObject prefab, Transform muzzle, Transform target, AttackConfig cfg)
    {
        if (prefab == null || cfg == null || target == null) return false;

        Vector3 origin = muzzle != null ? muzzle.position : transform.position;

        Vector3 aim = LockTargetPointUtility.GetCapsuleCenter(target);
        Vector3 dir = aim - origin;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;

        Vector3 d = dir.normalized;
        Vector3 spawnPos = origin + d * spawnForwardOffset;
        Quaternion rot = Quaternion.LookRotation(d, Vector3.up);

        GameObject go = Instantiate(prefab, spawnPos, rot);
        RangeProjectile proj = go.GetComponent<RangeProjectile>();
        if (proj == null) return true;

        // 关键：攻击者设为 Boss 本体 -> 完美弹反回飞会瞄准 Boss 本人
        Transform attacker = bossRoot != null ? bossRoot : transform;
        AttackData data = BuildAttackDataFromConfig(cfg, attacker);
        proj.Init(attacker, d, data);
        return true;
    }

    static AttackData BuildAttackDataFromConfig(AttackConfig cfg, Transform attacker)
    {
        AttackData d = new AttackData(attacker, cfg.sourceType, cfg.hitReaction, cfg.hpDamage, cfg.staminaDamage);
        d.canBeBlocked = cfg.canBeBlocked;
        d.canBeParried = cfg.canBeParried;
        d.canBreakGuard = cfg.canBreakGuard;
        d.hasSuperArmor = cfg.hasSuperArmor;
        d.hitStopWeight = cfg.hitStopWeight;
        d.staminaPenetrationDamage = cfg.staminaPenetrationDamage;
        d.hpPenetrationDamage = cfg.hpPenetrationDamage;
        return d;
    }

    void RefreshTarget()
    {
        currentTarget = null;

        Vector3 center = transform.position;
        Collider[] hits = Physics.OverlapSphere(center, targetRange, targetMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return;

        float bestSqr = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            CombatStats stats = hits[i].GetComponentInParent<CombatStats>();
            if (stats == null || stats.IsDead) continue;

            // 只打敌对层（复用 CombatStats 层级判断）
            if (bossStats != null && stats.gameObject.layer == bossStats.gameObject.layer)
                continue;

            float sqr = (LockTargetPointUtility.GetCapsuleCenter(stats.transform) - center).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                currentTarget = stats.transform;
            }
        }
    }

    void FaceTargetOrBossForward()
    {
        if (faceLockedTarget && currentTarget != null)
        {
            Vector3 to = LockTargetPointUtility.GetCapsuleCenter(currentTarget) - transform.position;
            to.y = 0f;

            if (to.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(to.normalized, Vector3.up);
                targetRot *= Quaternion.Euler(0f, yawOffsetDegrees, 0f);
                ApplyYawRotation(targetRot);
                return;
            }
        }

        if (yawReference == null)
            yawReference = bossRoot != null ? bossRoot : transform;

        if (yawReference == null)
            return;

        float yaw = yawReference.eulerAngles.y + yawOffsetDegrees;
        Quaternion fallback = Quaternion.Euler(0f, yaw, 0f);
        ApplyYawRotation(fallback);
    }

    void ApplyYawRotation(Quaternion target)
    {
        if (snapYaw || faceYawSpeed <= 0f)
        {
            transform.rotation = target;
            return;
        }

        float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, faceYawSpeed) * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, k);
    }

    void BeginEnter()
    {
        if (state == DroneState.Entering || state == DroneState.Active)
            return;

        transitionStartPos = transform.position;
        transitionStartRot = transform.rotation;

        Vector3 center = activeCenter != null ? activeCenter.position : transform.position;
        Vector3 endPos = center + activeOffset + new Vector3(orbitRadius, 0f, 0f);

        transitionEndPos = endPos;
        transitionEndRot = Quaternion.LookRotation((bossRoot != null ? bossRoot.forward : transform.forward), Vector3.up);
        transitionStartTime = Time.time;
        transitionDuration = enterDuration;
        state = DroneState.Entering;
    }

    void BeginReturn()
    {
        if (state == DroneState.Returning || state == DroneState.Docked)
            return;

        chargedPending = false;
        chargedTarget = null;

        transitionStartPos = transform.position;
        transitionStartRot = transform.rotation;

        Vector3 dockPos = GetDockWorldPos();
        Quaternion dockRot = GetDockWorldRot();

        transitionEndPos = dockPos;
        transitionEndRot = dockRot;
        transitionStartTime = Time.time;
        transitionDuration = returnDuration;
        state = DroneState.Returning;
    }

    void SnapToDockPose()
    {
        transform.position = GetDockWorldPos();
        transform.rotation = GetDockWorldRot();
        chargedPending = false;
        chargedTarget = null;
    }

    Vector3 GetDockWorldPos()
    {
        if (dockAnchor == null)
            return transform.position;

        return dockAnchor.TransformPoint(dockLocalPos);
    }

    Quaternion GetDockWorldRot()
    {
        if (dockAnchor == null)
            return transform.rotation;

        return dockAnchor.rotation * Quaternion.Euler(dockLocalEuler);
    }

    static void PlayOneShot(AudioSource src, AudioClip clip, Vector3 posFallback)
    {
        if (clip == null) return;

        if (src != null)
        {
            src.PlayOneShot(clip);
            return;
        }

        AudioSource.PlayClipAtPoint(clip, posFallback);
    }
}
