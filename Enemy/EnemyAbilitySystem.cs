using UnityEngine;

public class EnemyAbilitySystem : MonoBehaviour
{
    public enum AbilityType
    {
        ShockwaveCone = 0,
        ShockwaveAoe = 1,
        Heal = 2,
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

    [SerializeField] float shockwaveConeRange = 6f;
    [SerializeField, Range(10f, 180f)] float shockwaveConeAngle = 80f;

    [SerializeField] float shockwaveAoeRange = 6f;

    // =========================
    // Heal
    // =========================
    [Header("Heal")]
    [SerializeField] bool enableHeal = true;
    [SerializeField] int healAmount = 20;
    [SerializeField] float healCooldown = 10f;

    AbilityType pending;
    bool hasPending;
    Transform pendingTarget;

    bool isInAbilityLock;

    float nextShockwaveConeAllowedTime;
    float nextShockwaveAoeAllowedTime;
    float nextHealAllowedTime;

    public bool IsInAbilityLock => isInAbilityLock;

    public float ShockwaveConeRange => shockwaveConeRange;
    public float ShockwaveAoeRange => shockwaveAoeRange;

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
    }

    public bool TryCast(AbilityType type, Transform target)
    {
        if (!CanTryCast(type)) return false;
        if (type == AbilityType.ShockwaveCone && !CanConeTarget(target)) return false;
        if (type == AbilityType.ShockwaveAoe && !CanAoeTarget(target)) return false;

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
            AbilityType.ShockwaveCone => enableShockwave && shockwaveUseCone,
            AbilityType.ShockwaveAoe => enableShockwave && shockwaveUseAoe,
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
