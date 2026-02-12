using UnityEngine;
using UnityEngine.Serialization;

public class EnemyAbilitySystem : MonoBehaviour
{
    
    public enum AbilityType
    {
        Ability1,
        Ability2,
    }

    const string TriggerAbility1 = "Ability1";
    const string TriggerAbility2 = "Ability2";

    [Header("Refs")]
    [SerializeField] CombatStats stats;
    [SerializeField] Animator animator;

    [Header("Interrupt (Hit)")]
    [Tooltip("能力被受击打断时，强制切回的 Base Layer 状态（空=不强制切状态，仅清触发）。")]
    [SerializeField] string interruptFallbackStateName = "Idle";

    [Tooltip("能力被受击打断时，强制切状态的过渡时长。")]
    [SerializeField, Range(0f, 0.2f)] float interruptCrossFade = 0f;

    CombatReceiver receiver;

    // =========================
    // Shockwave (Ability1 / Ability2)
    // =========================
    [Header("Shockwave (Ability1 = Cone / Ability2 = AoE)")]
    [SerializeField] bool enableShockwave = true;

    [Tooltip("Ability1: 扇形冲击波（需要角度判定）")]
    [FormerlySerializedAs("shockwaveUseCone")]
    [SerializeField] bool shockwaveUseCone = true;

    [Tooltip("Ability2: 360°范围冲击波（只看距离）")]
    [FormerlySerializedAs("shockwaveUseAoe")]
    [SerializeField] bool shockwaveUseAoe = false;

    [FormerlySerializedAs("shockwaveConeAttackConfig")]
    [SerializeField] AttackConfig shockwaveConeAttackConfig;

    [FormerlySerializedAs("shockwaveAoeAttackConfig")]
    [SerializeField] AttackConfig shockwaveAoeAttackConfig;

    [FormerlySerializedAs("shockwaveCooldown")]
    [SerializeField] float shockwaveCooldown = 20f;

    [FormerlySerializedAs("shockwaveConeRange")]
    [SerializeField] float shockwaveConeRange = 6f;

    [FormerlySerializedAs("shockwaveConeAngle")]
    [SerializeField, Range(10f, 180f)] float shockwaveConeAngle = 80f;

    [FormerlySerializedAs("shockwaveAoeRange")]
    [SerializeField] float shockwaveAoeRange = 6f;

    // =========================
    // runtime
    // =========================
    AbilityType pending;
    bool hasPending;
    Transform pendingTarget;

    bool isInAbilityLock;

    float nextShockwaveAllowedTime;

    public bool IsInAbilityLock => isInAbilityLock;

    /// <summary>用于 Combat / RangeCombat 的粗判定（最大距离）。</summary>
    public float ShockwaveDecisionRange
    {
        get
        {
            if (!enableShockwave) return 0f;

            float range = 0f;
            if (shockwaveUseCone) range = Mathf.Max(range, shockwaveConeRange);
            if (shockwaveUseAoe) range = Mathf.Max(range, shockwaveAoeRange);
            return range;
        }
    }

    void Awake()
    {
        if (stats == null)
            stats = GetComponent<CombatStats>();

        if (animator == null)
            animator = GetComponent<Animator>();

        receiver = GetComponent<CombatReceiver>();
    }

    void Update()
    {
        if (!IsInHitLock()) return;

        // 受击打断：中止能力并从“被打断时刻”重新计冷却。
        if (hasPending || isInAbilityLock)
            InterruptAbilityByHit();
    }

    bool IsInHitLock()
    {
        return receiver != null && receiver.IsInHitLock;
    }

    public bool CanTryCast(AbilityType type)
    {
        if (IsInHitLock()) return false;
        if (isInAbilityLock || hasPending) return false;

        if (!IsAbilityEnabled(type)) return false;
        if (!IsCooldownReady(type)) return false;

        // 不消耗特殊槽：这里只做引用兜底
        if (stats == null) return false;

        // Shockwave 没绑 config 就不允许出（避免空放动画）
        if (type == AbilityType.Ability1 && shockwaveConeAttackConfig == null) return false;
        if (type == AbilityType.Ability2 && shockwaveAoeAttackConfig == null) return false;

        return true;
    }

    public bool CanAbility1Target(Transform target)
    {
        if (!enableShockwave || !shockwaveUseCone) return false;
        if (shockwaveConeAttackConfig == null) return false;
        if (target == null) return false;

        Vector3 origin = transform.position;
        Vector3 targetPoint = LockTargetPointUtility.GetCapsuleCenter(target);
        Vector3 to = targetPoint - origin;
        to.y = 0f;

        float dist = to.magnitude;
        if (dist > shockwaveConeRange) return false;
        if (dist <= 0.001f) return true;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = transform.forward;

        float angle = Vector3.Angle(forward.normalized, to.normalized);
        return angle <= shockwaveConeAngle * 0.5f;
    }

    public bool CanAbility2Target(Transform target)
    {
        if (!enableShockwave || !shockwaveUseAoe) return false;
        if (shockwaveAoeAttackConfig == null) return false;
        if (target == null) return false;

        Vector3 origin = transform.position;
        Vector3 targetPoint = LockTargetPointUtility.GetCapsuleCenter(target);
        Vector3 to = targetPoint - origin;
        to.y = 0f;

        float dist = to.magnitude;
        return dist <= shockwaveAoeRange;
    }

    public bool CanShockwaveTarget(Transform target)
    {
        return CanAbility1Target(target) || CanAbility2Target(target);
    }

    public bool TryCast(AbilityType type, Transform target)
    {
        if (!CanTryCast(type)) return false;

        // 距离/角度门禁
        if (type == AbilityType.Ability1 && !CanAbility1Target(target)) return false;
        if (type == AbilityType.Ability2 && !CanAbility2Target(target)) return false;

        pending = type;
        hasPending = true;
        pendingTarget = target;

        SetCooldown(type);
        TriggerAnimator(type);

        return true;
    }

    bool IsAbilityEnabled(AbilityType type)
    {
        return type switch
        {
            AbilityType.Ability1 => enableShockwave && shockwaveUseCone,
            AbilityType.Ability2 => enableShockwave && shockwaveUseAoe,
            _ => false
        };
    }

    bool IsCooldownReady(AbilityType type)
    {
        return type switch
        {
            AbilityType.Ability1 => Time.time >= nextShockwaveAllowedTime,
            AbilityType.Ability2 => Time.time >= nextShockwaveAllowedTime,
            _ => false
        };
    }

    void SetCooldown(AbilityType type)
    {
        switch (type)
        {
            case AbilityType.Ability1:
            case AbilityType.Ability2:
                nextShockwaveAllowedTime = Time.time + Mathf.Max(0f, shockwaveCooldown);
                break;
        }
    }

    void TriggerAnimator(AbilityType type)
    {
        if (animator == null) return;

        string trigger = type switch
        {
            AbilityType.Ability1 => TriggerAbility1,
            AbilityType.Ability2 => TriggerAbility2,
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(trigger))
            animator.SetTrigger(trigger);
    }

    public void CancelPending()
    {
        hasPending = false;
        pendingTarget = null;
    }

    void InterruptAbilityByHit()
    {
        ForceExitAbilityAnimation();
        CancelPending();
        isInAbilityLock = false;
        SetCooldown(pending);
    }

    void ForceExitAbilityAnimation()
    {
        if (animator == null) return;

        // 先清触发，避免被打断后 Trigger 残留再次拉回能力状态
        animator.ResetTrigger(TriggerAbility1);
        animator.ResetTrigger(TriggerAbility2);

        if (string.IsNullOrWhiteSpace(interruptFallbackStateName))
            return;

        int layer = 0;
        int hash = Animator.StringToHash(interruptFallbackStateName);
        if (!animator.HasState(layer, hash))
            return;

        animator.CrossFadeInFixedTime(interruptFallbackStateName, interruptCrossFade, layer, 0f);
    }

    // =========================
    // Animation Events (Authority)
    // =========================

    // 动画事件：AbilityBegin
    public void AbilityBegin()
    {
        isInAbilityLock = true;
    }

    // 动画事件：AbilityImpact
    public void AbilityImpact()
    {
        if (!hasPending) return;

        if (IsInHitLock())
        {
            CancelPending();
            return;
        }

        switch (pending)
        {
            case AbilityType.Ability1:
                PerformShockwaveCone(pendingTarget);
                break;
            case AbilityType.Ability2:
                PerformShockwaveAoe(pendingTarget);
                break;
        }

        hasPending = false;
    }

    // 动画事件：AbilityEnd
    public void AbilityEnd()
    {
        isInAbilityLock = false;
    }

    // =========================
    // Ability execution
    // =========================

    void PerformShockwaveCone(Transform target)
    {
        if (!CanAbility1Target(target)) return;

        IHittable hittable =
            target.GetComponentInParent<IHittable>() ??
            target.GetComponentInChildren<IHittable>();

        if (hittable == null) return;

        AttackData data = BuildAttackDataFromConfig(shockwaveConeAttackConfig);
        data.attacker = transform;

        hittable.OnHit(data);
    }

    void PerformShockwaveAoe(Transform target)
    {
        if (!CanAbility2Target(target)) return;

        IHittable hittable =
            target.GetComponentInParent<IHittable>() ??
            target.GetComponentInChildren<IHittable>();

        if (hittable == null) return;

        AttackData data = BuildAttackDataFromConfig(shockwaveAoeAttackConfig);
        data.attacker = transform;

        hittable.OnHit(data);
    }

    AttackData BuildAttackDataFromConfig(AttackConfig cfg)
    {
        AttackSourceType sourceType = AttackSourceType.Ability1;
        if (cfg != null)
            sourceType = cfg.sourceType;

        return new AttackData(
            transform,
            sourceType,
            cfg != null ? cfg.hitReaction : HitReactionType.Light,
            cfg != null ? cfg.hpDamage : 0,
            cfg != null ? cfg.staminaDamage : 0
        )
        {
            canBeBlocked = cfg != null && cfg.canBeBlocked,
            canBeParried = cfg != null && cfg.canBeParried,
            canBreakGuard = cfg != null && cfg.canBreakGuard,
            hasSuperArmor = cfg != null && cfg.hasSuperArmor,
            hitStopWeight = cfg != null ? cfg.hitStopWeight : 1f,
            staminaPenetrationDamage = cfg != null ? cfg.staminaPenetrationDamage : 0,
            hpPenetrationDamage = cfg != null ? cfg.hpPenetrationDamage : 0,
        };
    }
}
