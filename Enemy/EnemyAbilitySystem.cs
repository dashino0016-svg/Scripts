using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class EnemyAbilitySystem : MonoBehaviour
{
    public enum AbilityType
    {
        Ability1Short,
        Ability1Long,
    }

    const string TriggerAbility1 = "Ability1";
    const string AbilityBlendParam = "AbilityBlendParam";

    [Header("Refs")]
    [SerializeField] CombatStats stats;
    [SerializeField] Animator animator;

    [Header("Interrupt (Hit)")]
    [SerializeField] string interruptFallbackStateName = "Idle";
    [SerializeField, Range(0f, 0.2f)] float interruptCrossFade = 0f;

    CombatReceiver receiver;

    [Header("Shockwave (Ability1Short=Cone / Ability1Long=AoE)")]
    [SerializeField] bool enableShockwave = true;
    [FormerlySerializedAs("shockwaveUseCone")]
    [SerializeField] bool shockwaveUseShort = true;
    [FormerlySerializedAs("shockwaveUseAoe")]
    [SerializeField] bool shockwaveUseLong = false;

    [FormerlySerializedAs("shockwaveConeAttackConfig")]
    [SerializeField] AttackConfig shockwaveShortAttackConfig;
    [FormerlySerializedAs("shockwaveAoeAttackConfig")]
    [SerializeField] AttackConfig shockwaveLongAttackConfig;

    [SerializeField] float shockwaveCooldown = 20f;
    [FormerlySerializedAs("shockwaveConeRange")]
    [SerializeField] float shockwaveShortRange = 6f;
    [FormerlySerializedAs("shockwaveConeAngle")]
    [SerializeField, Range(10f, 180f)] float shockwaveShortAngle = 80f;
    [FormerlySerializedAs("shockwaveAoeRange")]
    [SerializeField] float shockwaveLongRange = 6f;

    [Header("Target Filter")]
    [Tooltip("能力命中检测层。0 表示不过滤层，仅按阵营(不同 layer)筛选。")]
    [SerializeField] LayerMask abilityTargetMask;

    AbilityType pending;
    bool hasPending;
    Transform pendingTarget;

    bool isInAbilityLock;
    float nextShockwaveAllowedTime;

    public bool IsInAbilityLock => isInAbilityLock;

    public float ShockwaveDecisionRange
    {
        get
        {
            if (!enableShockwave) return 0f;
            float range = 0f;
            if (shockwaveUseShort) range = Mathf.Max(range, shockwaveShortRange);
            if (shockwaveUseLong) range = Mathf.Max(range, shockwaveLongRange);
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
        if (stats == null) return false;

        if (type == AbilityType.Ability1Short && shockwaveShortAttackConfig == null) return false;
        if (type == AbilityType.Ability1Long && shockwaveLongAttackConfig == null) return false;

        return true;
    }

    public bool CanAbility1ShortTarget(Transform target)
    {
        if (!enableShockwave || !shockwaveUseShort) return false;
        if (shockwaveShortAttackConfig == null) return false;
        if (target == null) return false;

        Vector3 origin = transform.position;
        Vector3 targetPoint = LockTargetPointUtility.GetLockPoint(target);
        Vector3 to = targetPoint - origin;
        to.y = 0f;

        float dist = to.magnitude;
        if (dist > shockwaveShortRange) return false;
        if (dist <= 0.001f) return true;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = transform.forward;

        float angle = Vector3.Angle(forward.normalized, to.normalized);
        return angle <= shockwaveShortAngle * 0.5f;
    }

    public bool CanAbility1LongTarget(Transform target)
    {
        if (!enableShockwave || !shockwaveUseLong) return false;
        if (shockwaveLongAttackConfig == null) return false;
        if (target == null) return false;

        Vector3 origin = transform.position;
        Vector3 targetPoint = LockTargetPointUtility.GetLockPoint(target);
        Vector3 to = targetPoint - origin;
        to.y = 0f;
        return to.magnitude <= shockwaveLongRange;
    }

    public bool TryCast(AbilityType type, Transform target)
    {
        if (!CanTryCast(type)) return false;

        if (type == AbilityType.Ability1Short && !CanAbility1ShortTarget(target)) return false;
        if (type == AbilityType.Ability1Long && !CanAbility1LongTarget(target)) return false;

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
            AbilityType.Ability1Short => enableShockwave && shockwaveUseShort,
            AbilityType.Ability1Long => enableShockwave && shockwaveUseLong,
            _ => false
        };
    }

    bool IsCooldownReady(AbilityType type)
    {
        return type switch
        {
            AbilityType.Ability1Short => Time.time >= nextShockwaveAllowedTime,
            AbilityType.Ability1Long => Time.time >= nextShockwaveAllowedTime,
            _ => false
        };
    }

    void SetCooldown(AbilityType type)
    {
        switch (type)
        {
            case AbilityType.Ability1Short:
            case AbilityType.Ability1Long:
                nextShockwaveAllowedTime = Time.time + Mathf.Max(0f, shockwaveCooldown);
                break;
        }
    }

    void TriggerAnimator(AbilityType type)
    {
        if (animator == null) return;

        animator.SetFloat(AbilityBlendParam, type == AbilityType.Ability1Long ? 1f : 0f);
        animator.SetTrigger(TriggerAbility1);
    }

    public void CancelPending()
    {
        hasPending = false;
        pendingTarget = null;
    }

    public void ForceCancelForCheckpoint()
    {
        ForceExitAbilityAnimation();
        CancelPending();
        isInAbilityLock = false;
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

        animator.ResetTrigger(TriggerAbility1);

        if (string.IsNullOrWhiteSpace(interruptFallbackStateName))
            return;

        int layer = 0;
        int hash = Animator.StringToHash(interruptFallbackStateName);
        if (!animator.HasState(layer, hash))
            return;

        animator.CrossFadeInFixedTime(interruptFallbackStateName, interruptCrossFade, layer, 0f);
    }

    public void AbilityBegin()
    {
        isInAbilityLock = true;
    }

    public void AbilityImpact()
    {
        if (!hasPending) return;

        if (IsInHitLock())
        {
            CancelPending();
            return;
        }

        CombatSfxSignals.RaiseAbilityTriggered(ToSfxAbilityId(pending));

        switch (pending)
        {
            case AbilityType.Ability1Short:
                PerformShockwaveShort(pendingTarget);
                break;
            case AbilityType.Ability1Long:
                PerformShockwaveLong(pendingTarget);
                break;
        }

        hasPending = false;
    }

    static int ToSfxAbilityId(AbilityType type)
    {
        return type switch
        {
            AbilityType.Ability1Short => 1,
            AbilityType.Ability1Long => 1,
            _ => 1,
        };
    }

    public void AbilityEnd()
    {
        isInAbilityLock = false;
    }

    void PerformShockwaveShort(Transform target)
    {
        if (!CanAbility1ShortTarget(target)) return;

        Vector3 origin = transform.position;

        int mask = abilityTargetMask.value != 0 ? abilityTargetMask.value : Physics.AllLayers;
        Collider[] hits = Physics.OverlapSphere(
            origin,
            shockwaveShortRange,
            mask,
            QueryTriggerInteraction.Collide
        );

        HashSet<CombatStats> unique = new HashSet<CombatStats>();
        AttackData template = BuildAttackDataFromConfig(shockwaveShortAttackConfig);
        template.attacker = transform;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = transform.forward;
        forward.Normalize();

        foreach (var col in hits)
        {
            CombatStats targetStats = col.GetComponentInParent<CombatStats>();
            if (targetStats == null) continue;
            if (targetStats == stats) continue;
            if (targetStats.IsDead) continue;
            if (targetStats.gameObject.layer == gameObject.layer) continue;
            if (!unique.Add(targetStats)) continue;

            Vector3 targetPoint = LockTargetPointUtility.GetLockPoint(targetStats.transform);
            Vector3 to = targetPoint - origin;
            to.y = 0f;

            float dist = to.magnitude;
            if (dist > shockwaveShortRange || dist < 0.001f) continue;

            float angle = Vector3.Angle(forward, to.normalized);
            if (angle > shockwaveShortAngle * 0.5f) continue;

            IHittable hittable =
                targetStats.GetComponent<IHittable>() ??
                targetStats.GetComponentInParent<IHittable>();

            if (hittable == null) continue;

            AttackData data = template;
            data.attacker = transform;
            hittable.OnHit(data);
        }
    }

    void PerformShockwaveLong(Transform target)
    {
        if (!CanAbility1LongTarget(target)) return;

        Vector3 origin = transform.position;

        int mask = abilityTargetMask.value != 0 ? abilityTargetMask.value : Physics.AllLayers;
        Collider[] hits = Physics.OverlapSphere(
            origin,
            shockwaveLongRange,
            mask,
            QueryTriggerInteraction.Collide
        );

        HashSet<CombatStats> unique = new HashSet<CombatStats>();
        AttackData template = BuildAttackDataFromConfig(shockwaveLongAttackConfig);
        template.attacker = transform;

        foreach (var col in hits)
        {
            CombatStats targetStats = col.GetComponentInParent<CombatStats>();
            if (targetStats == null) continue;
            if (targetStats == stats) continue;
            if (targetStats.IsDead) continue;
            if (targetStats.gameObject.layer == gameObject.layer) continue;
            if (!unique.Add(targetStats)) continue;

            Vector3 targetPoint = LockTargetPointUtility.GetLockPoint(targetStats.transform);
            Vector3 to = targetPoint - origin;
            to.y = 0f;
            if (to.magnitude > shockwaveLongRange) continue;

            IHittable hittable =
                targetStats.GetComponent<IHittable>() ??
                targetStats.GetComponentInParent<IHittable>();

            if (hittable == null) continue;

            AttackData data = template;
            data.attacker = transform;
            hittable.OnHit(data);
        }
    }

    AttackData BuildAttackDataFromConfig(AttackConfig cfg)
    {
        AttackSourceType sourceType = AttackSourceType.Ability1Short;
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
