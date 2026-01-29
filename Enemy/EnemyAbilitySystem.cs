using UnityEngine;

public class EnemyAbilitySystem : MonoBehaviour
{
    public enum AbilityType
    {
        ShockwaveCone = 0,
        ShockwaveAoe = 1,
        Heal = 2,
        Shockwave = 0,
        Heal = 1,
    }

    [Header("Refs")]
    [SerializeField] CombatStats stats;
    [SerializeField] Animator animator;

    CombatReceiver receiver;

    // =========================
    // Shockwave
    // =========================
    [Header("Shockwave")]
    [SerializeField] bool enableShockwave = true;
    [SerializeField] bool shockwaveUseCone = true;
    [SerializeField] bool shockwaveUseAoe = false;

    [SerializeField] AttackConfig shockwaveConeAttackConfig;
    [SerializeField] AttackConfig shockwaveAoeAttackConfig;

    [SerializeField] float shockwaveConeCooldown = 6f;
    [SerializeField] float shockwaveAoeCooldown = 6f;
    [SerializeField] int shockwaveCost = 100;
    [SerializeField] float shockwaveCooldown = 6f;

    [SerializeField] float shockwaveConeRange = 6f;
    [SerializeField, Range(10f, 180f)] float shockwaveConeAngle = 80f;

    [SerializeField] float shockwaveAoeRange = 6f;

    [SerializeField] string shockwaveTrigger = "Shockwave";

    // =========================
    // Heal
    // =========================
    [Header("Heal")]
    [SerializeField] bool enableHeal = true;
    [SerializeField] int healAmount = 20;
    [SerializeField] float healCooldown = 10f;
    [SerializeField] int healCost = 100;
    [SerializeField] float healCooldown = 10f;
    [SerializeField] string healTrigger = "Heal";

    AbilityType pending;
    bool hasPending;
    Transform pendingTarget;

    bool isInAbilityLock;

    float nextShockwaveConeAllowedTime;
    float nextShockwaveAoeAllowedTime;
    float nextShockwaveAllowedTime;
    float nextHealAllowedTime;

    public bool IsInAbilityLock => isInAbilityLock;

    public float ShockwaveConeRange => shockwaveConeRange;
    public float ShockwaveAoeRange => shockwaveAoeRange;
    public float ShockwaveDecisionRange
    {
        get
        {
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
        if (hasPending && IsInHitLock())
            CancelPending();
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
        return true;
    }

    public bool CanConeTarget(Transform target)
    {
        if (!enableShockwave || !shockwaveUseCone) return false;
        if (target == null) return false;
        if (stats == null) return false;

        return stats.CurrentSpecial >= GetCost(type);
    }

    public bool CanShockwaveTarget(Transform target)
    {
        if (!enableShockwave) return false;
        if (target == null) return false;
        if (!shockwaveUseCone && !shockwaveUseAoe) return false;

        Vector3 origin = transform.position;
        Vector3 targetPoint = LockTargetPointUtility.GetCapsuleCenter(target);
        Vector3 to = targetPoint - origin;
        to.y = 0f;

        float dist = to.magnitude;
        if (dist < 0.001f || dist > shockwaveConeRange) return false;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = transform.forward;

        float angle = Vector3.Angle(forward.normalized, to.normalized);
        return angle <= shockwaveConeAngle * 0.5f;
    }

    public bool CanAoeTarget(Transform target)
    {
        if (!enableShockwave || !shockwaveUseAoe) return false;
        if (target == null) return false;

        Vector3 origin = transform.position;
        Vector3 targetPoint = LockTargetPointUtility.GetCapsuleCenter(target);
        Vector3 to = targetPoint - origin;
        to.y = 0f;

        float dist = to.magnitude;
        return dist <= shockwaveAoeRange;

        bool inAoe = shockwaveUseAoe && dist <= shockwaveAoeRange;
        bool inCone = false;

        if (shockwaveUseCone && dist > 0.001f && dist <= shockwaveConeRange)
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = transform.forward;

            float angle = Vector3.Angle(forward.normalized, to.normalized);
            inCone = angle <= shockwaveConeAngle * 0.5f;
        }

        return inAoe || inCone;
    }

    public bool TryCast(AbilityType type, Transform target)
    {
        if (!CanTryCast(type)) return false;
        if (type == AbilityType.ShockwaveCone && !CanConeTarget(target)) return false;
        if (type == AbilityType.ShockwaveAoe && !CanAoeTarget(target)) return false;
        if (type == AbilityType.Shockwave && !CanShockwaveTarget(target)) return false;

        int cost = GetCost(type);
        if (!stats.ConsumeSpecial(cost))
            return false;

        pending = type;
        hasPending = true;
        pendingTarget = target;

        SetCooldown(type);
        TriggerAnimator(type);

        return true;
    }

    int GetCost(AbilityType type)
    {
        return type switch
        {
            AbilityType.Shockwave => shockwaveCost,
            AbilityType.Heal => healCost,
            _ => 0
        };
    }

    bool IsAbilityEnabled(AbilityType type)
    {
        return type switch
        {
            AbilityType.ShockwaveCone => enableShockwave && shockwaveUseCone,
            AbilityType.ShockwaveAoe => enableShockwave && shockwaveUseAoe,
            AbilityType.Shockwave => enableShockwave,
            AbilityType.Heal => enableHeal,
            _ => false
        };
    }

    bool IsCooldownReady(AbilityType type)
    {
        return type switch
        {
            AbilityType.ShockwaveCone => Time.time >= nextShockwaveConeAllowedTime,
            AbilityType.ShockwaveAoe => Time.time >= nextShockwaveAoeAllowedTime,
            AbilityType.Shockwave => Time.time >= nextShockwaveAllowedTime,
            AbilityType.Heal => Time.time >= nextHealAllowedTime,
            _ => false
        };
    }

    void SetCooldown(AbilityType type)
    {
        switch (type)
        {
            case AbilityType.ShockwaveCone:
                nextShockwaveConeAllowedTime = Time.time + Mathf.Max(0f, shockwaveConeCooldown);
                break;
            case AbilityType.ShockwaveAoe:
                nextShockwaveAoeAllowedTime = Time.time + Mathf.Max(0f, shockwaveAoeCooldown);
            case AbilityType.Shockwave:
                nextShockwaveAllowedTime = Time.time + Mathf.Max(0f, shockwaveCooldown);
                break;
            case AbilityType.Heal:
                nextHealAllowedTime = Time.time + Mathf.Max(0f, healCooldown);
                break;
        }
    }

    void TriggerAnimator(AbilityType type)
    {
        if (animator == null) return;

        switch (type)
        {
            case AbilityType.ShockwaveCone:
                animator.SetTrigger("Ability1");
                break;
            case AbilityType.ShockwaveAoe:
                animator.SetTrigger("Ability2");
                break;
            case AbilityType.Heal:
                animator.SetTrigger("Ability3");
                break;
        }
        string trigger = type switch
        {
            AbilityType.Shockwave => shockwaveTrigger,
            AbilityType.Heal => healTrigger,
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
            case AbilityType.ShockwaveCone:
                PerformCone(pendingTarget);
                break;
            case AbilityType.ShockwaveAoe:
                PerformAoe(pendingTarget);
            case AbilityType.Shockwave:
                PerformShockwave(pendingTarget);
                break;
            case AbilityType.Heal:
                if (stats != null)
                    stats.HealHP(healAmount);
                break;
        }

        hasPending = false;
    }

    // 动画事件：AbilityBegin
    public void AbilityBegin()
    {
        isInAbilityLock = true;
    }

    // 动画事件：AbilityEnd
    public void AbilityEnd()
    {
        isInAbilityLock = false;
    }

    void PerformCone(Transform target)
    {
        if (target == null) return;
        if (!enableShockwave || !shockwaveUseCone) return;
        if (!CanConeTarget(target)) return;

        AttackConfig config = shockwaveConeAttackConfig;

        if (config == null)
        {
            Debug.LogError("[EnemyAbilitySystem] Shockwave Cone Attack Config 未绑定。Shockwave 不执行。");
            return;
        }

        IHittable hittable =
            target.GetComponentInParent<IHittable>() ??
            target.GetComponentInChildren<IHittable>();

        if (hittable == null) return;

        AttackData data = BuildAttackDataFromConfig(config);
        data.attacker = transform;

        hittable.OnHit(data);
    }

    void PerformAoe(Transform target)
    {
        if (target == null) return;
        if (!enableShockwave || !shockwaveUseAoe) return;
        if (!CanAoeTarget(target)) return;

        AttackConfig config = shockwaveAoeAttackConfig;

        if (config == null)
        {
            Debug.LogError("[EnemyAbilitySystem] Shockwave AOE Attack Config 未绑定。Shockwave 不执行。");
    void PerformShockwave(Transform target)
    {
        if (target == null) return;
        if (!shockwaveUseCone && !shockwaveUseAoe) return;

        Vector3 origin = transform.position;
        Vector3 targetPoint = LockTargetPointUtility.GetCapsuleCenter(target);
        Vector3 to = targetPoint - origin;
        to.y = 0f;
        float dist = to.magnitude;

        bool inAoe = shockwaveUseAoe && dist <= shockwaveAoeRange;
        bool inCone = false;

        if (shockwaveUseCone && dist > 0.001f && dist <= shockwaveConeRange)
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = transform.forward;

            float angle = Vector3.Angle(forward.normalized, to.normalized);
            inCone = angle <= shockwaveConeAngle * 0.5f;
        }

        if (!inCone && !inAoe)
            return;

        AttackConfig config = null;
        if (inCone && shockwaveConeAttackConfig != null)
            config = shockwaveConeAttackConfig;
        else if (inAoe && shockwaveAoeAttackConfig != null)
            config = shockwaveAoeAttackConfig;
        else
            config = shockwaveConeAttackConfig != null ? shockwaveConeAttackConfig : shockwaveAoeAttackConfig;

        if (config == null)
        {
            Debug.LogError("[EnemyAbilitySystem] Shockwave Attack Config 未绑定。Shockwave 不执行。");
            return;
        }

        IHittable hittable =
            target.GetComponentInParent<IHittable>() ??
            target.GetComponentInChildren<IHittable>();

        if (hittable == null) return;

        AttackData data = BuildAttackDataFromConfig(config);
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
        };
    }
}
