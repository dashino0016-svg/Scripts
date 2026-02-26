using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class PlayerAbilitySystem : MonoBehaviour
{
    public enum AbilityType
    {
        Ability1 = 0,
        Ability2 = 1,
        Ability3 = 2,
        Ability4 = 3,
    }

    [Header("Refs")]
    [SerializeField] CombatStats stats;
    [SerializeField] LockOnSystem lockOn;

    CombatReceiver receiver;

    // =========================
    // Ability1 - Shockwave (Short / Long)
    // =========================
    [Header("Ability1 - Shockwave (Short=Cone / Long=360)")]
    [FormerlySerializedAs("ability1AttackConfig")]
    [SerializeField] AttackConfig ability1ShortAttackConfig;
    [FormerlySerializedAs("ability2AttackConfig")]
    [SerializeField] AttackConfig ability1LongAttackConfig;

    [Header("Ability1 Cost")]
    [FormerlySerializedAs("costAbility1")]
    [SerializeField] int costAbility1 = 100;

    [SerializeField] int costAbility1Long = 150;

    [FormerlySerializedAs("ability1Range")]
    [SerializeField] float ability1ShortRange = 6f;
    [FormerlySerializedAs("ability1ConeAngle")]
    [SerializeField, Range(10f, 180f)] float ability1ShortConeAngle = 80f;

    [SerializeField] float ability1LongRange = 6f;

    [SerializeField] LayerMask enemyMask;

    // =========================
    // Ability2 - Lift & Float
    // =========================
    [Header("Ability2 - Lift & Float")]
    [FormerlySerializedAs("costAbility2")]
    [SerializeField] int costAbility2 = 200;

    [SerializeField] int costAbility2Long = 300;

    [SerializeField] float ability2Range = 6f;
    [SerializeField] LayerMask ability2EnemyMask;
    [SerializeField] float ability2RiseHeight = 2.5f;
    [SerializeField] float ability2RiseSpeed = 6f;
    [SerializeField] float ability2FloatDuration = 1.5f;

    [SerializeField, Tooltip("向下初速度（负值）。实际会在 EnemyMove.hardLandVelocity 处做安全钳制。")]
    float ability2InitialFallVelocity = -10f;

    [Header("Ability Shockwave Hit Stop")]
    [SerializeField] float abilityShockwaveHitStopTime = 0.05f;

    // =========================
    // Ability3 - Time Slow (Enemy Only)
    // =========================
    [Header("Ability3 - Time Slow (Enemy Only)")]
    [SerializeField] int costAbility3 = 120;

    [SerializeField] float ability3Radius = 6f;

    [SerializeField] float ability3Duration = 2.5f;

    [SerializeField, Range(0.05f, 1f)] float ability3EnemyScale = 0.25f;

    [SerializeField] LayerMask ability3EnemyMask;

    // =========================
    // Ability4 - Heal
    // =========================
    [Header("Ability4 - Heal")]
    [SerializeField] int ability4HealAmount = 20;

    [SerializeField] int costAbility4 = 100;


    AbilityType pending;
    bool pendingLongPress;
    bool hasPending;
    Coroutine ability3Routine;

    void Awake()
    {
        if (stats == null)
            stats = GetComponent<CombatStats>();

        if (lockOn == null)
            lockOn = GetComponent<LockOnSystem>();

        receiver = GetComponent<CombatReceiver>();
    }

    void Update()
    {
        if (hasPending && IsInHitLock())
            CancelPending();
    }

    void OnDisable()
    {
        if (ability3Routine == null)
            return;

        StopCoroutine(ability3Routine);
        ability3Routine = null;
        CombatSfxSignals.RaiseAbility3TimeSlowEnd();
    }

    bool IsInHitLock()
    {
        return receiver != null && receiver.IsInHitLock;
    }

    public void CancelPending()
    {
        hasPending = false;
    }

    public bool IsImplemented(AbilityType type)
    {
        return type == AbilityType.Ability1 ||
               type == AbilityType.Ability2 ||
               type == AbilityType.Ability3 ||
               type == AbilityType.Ability4;
    }

    public bool TryRequest(AbilityType type, bool longPress = false)
    {
        if (stats == null) return false;
        if (!IsImplemented(type)) return false;

        if (IsInHitLock())
            return false;

        if (!CanRequest(type, longPress))
            return false;

        int cost = GetCost(type, longPress);
        if (!stats.ConsumeSpecial(cost))
            return false;

        pending = type;
        pendingLongPress = longPress;
        hasPending = true;
        return true;
    }

    bool CanRequest(AbilityType type, bool longPress)
    {
        switch (type)
        {
            case AbilityType.Ability2:
                return longPress ? HasAnyEnemyInAbility2Range() : HasValidLockTargetForAbility2();
            default:
                return true;
        }
    }

    int GetCost(AbilityType type, bool longPress)
    {
        return type switch
        {
            AbilityType.Ability1 => longPress ? costAbility1Long : costAbility1,
            AbilityType.Ability2 => longPress ? costAbility2Long : costAbility2,
            AbilityType.Ability3 => costAbility3,
            AbilityType.Ability4 => costAbility4,
            _ => 0
        };
    }

    // 动画事件：AbilityImpact
    public bool ApplyPending(out AbilityType appliedAbility)
    {
        appliedAbility = default;
        if (!hasPending) return false;

        if (IsInHitLock())
        {
            CancelPending();
            return false;
        }

        appliedAbility = pending;

        switch (pending)
        {
            case AbilityType.Ability1:
                PerformAbility1(pendingLongPress);
                break;
            case AbilityType.Ability2:
                PerformAbility2(pendingLongPress);
                break;
            case AbilityType.Ability3:
                PerformAbility3();
                break;
            case AbilityType.Ability4:
                if (stats != null)
                    stats.HealHP(ability4HealAmount);
                break;
        }

        hasPending = false;
        return true;
    }

    void PerformAbility3()
    {
        Vector3 origin = transform.position;

        LayerMask mask = (ability3EnemyMask.value != 0) ? ability3EnemyMask : enemyMask;

        Collider[] hits = Physics.OverlapSphere(
            origin,
            ability3Radius,
            mask,
            QueryTriggerInteraction.Collide
        );

        HashSet<EnemyController> unique = new HashSet<EnemyController>();

        foreach (var col in hits)
        {
            EnemyController ec = col.GetComponentInParent<EnemyController>();
            if (ec == null) continue;

            if (!unique.Add(ec))
                continue;

            ec.ApplyLocalTimeScale(ability3EnemyScale, ability3Duration);
        }

        if (ability3Routine != null)
            StopCoroutine(ability3Routine);

        ability3Routine = StartCoroutine(Ability3BgmOverrideRoutine(ability3Duration));
    }

    System.Collections.IEnumerator Ability3BgmOverrideRoutine(float duration)
    {
        CombatSfxSignals.RaiseAbility3TimeSlowBegin();

        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        CombatSfxSignals.RaiseAbility3TimeSlowEnd();
        ability3Routine = null;
    }

    void PerformAbility1(bool longPress)
    {
        if (TimeController.Instance != null)
            TimeController.Instance.HitStop(abilityShockwaveHitStopTime, abilityShockwaveHitStopTime);

        if (longPress)
            PerformAbility1Long();
        else
            PerformAbility1Short();
    }

    void PerformAbility1Short()
    {
        if (ability1ShortAttackConfig == null)
        {
            Debug.LogError("[PlayerAbilitySystem] Ability1Short Attack Config 未绑定。Ability1 不执行。");
            return;
        }

        Vector3 origin = transform.position;
        Vector3 forward = transform.forward;

        Collider[] hits = Physics.OverlapSphere(
            origin,
            ability1ShortRange,
            enemyMask,
            QueryTriggerInteraction.Collide
        );

        HashSet<CombatStats> unique = new HashSet<CombatStats>();
        AttackData template = BuildAttackDataFromConfig(ability1ShortAttackConfig);

        foreach (var col in hits)
        {
            CombatStats targetStats = col.GetComponentInParent<CombatStats>();
            if (targetStats == null) continue;
            if (targetStats == stats) continue;

            if (!unique.Add(targetStats))
                continue;

            Vector3 to = targetStats.transform.position - origin;
            to.y = 0f;

            float dist = to.magnitude;
            if (dist > ability1ShortRange || dist < 0.001f) continue;

            float angle = Vector3.Angle(forward, to.normalized);
            if (angle > ability1ShortConeAngle * 0.5f) continue;

            IHittable hittable =
                targetStats.GetComponent<IHittable>() ??
                targetStats.GetComponentInParent<IHittable>();

            if (hittable == null) continue;

            AttackData data = template;
            data.attacker = transform;

            hittable.OnHit(data);
        }
    }

    void PerformAbility1Long()
    {
        if (ability1LongAttackConfig == null)
        {
            Debug.LogError("[PlayerAbilitySystem] Ability1Long Attack Config 未绑定。Ability1 不执行。");
            return;
        }

        Vector3 origin = transform.position;

        Collider[] hits = Physics.OverlapSphere(
            origin,
            ability1LongRange,
            enemyMask,
            QueryTriggerInteraction.Collide
        );

        HashSet<CombatStats> unique = new HashSet<CombatStats>();
        AttackData template = BuildAttackDataFromConfig(ability1LongAttackConfig);

        foreach (var col in hits)
        {
            CombatStats targetStats = col.GetComponentInParent<CombatStats>();
            if (targetStats == null) continue;
            if (targetStats == stats) continue;

            if (!unique.Add(targetStats))
                continue;

            IHittable hittable =
                targetStats.GetComponent<IHittable>() ??
                targetStats.GetComponentInParent<IHittable>();

            if (hittable == null) continue;

            AttackData data = template;
            data.attacker = transform;

            hittable.OnHit(data);
        }
    }

    void PerformAbility2(bool longPress)
    {
        if (TimeController.Instance != null)
            TimeController.Instance.HitStop(abilityShockwaveHitStopTime, abilityShockwaveHitStopTime);

        if (longPress)
            PerformAbility2Long();
        else
            PerformAbility2Short();
    }

    void PerformAbility2Short()
    {
        if (!HasValidLockTargetForAbility2())
            return;

        Transform lockTarget = lockOn.CurrentTarget;
        if (lockTarget == null)
            return;

        EnemyFloatState floatState = lockTarget.GetComponentInParent<EnemyFloatState>();
        if (floatState == null)
            return;

        floatState.TryStartFloat(
            ability2RiseHeight,
            ability2RiseSpeed,
            ability2FloatDuration,
            ability2InitialFallVelocity,
            transform
        );
    }

    void PerformAbility2Long()
    {
        Vector3 origin = transform.position;

        LayerMask mask = (ability2EnemyMask.value != 0) ? ability2EnemyMask : enemyMask;
        Collider[] hits = Physics.OverlapSphere(
            origin,
            ability2Range,
            mask,
            QueryTriggerInteraction.Collide
        );

        HashSet<EnemyFloatState> unique = new HashSet<EnemyFloatState>();

        foreach (var col in hits)
        {
            EnemyFloatState floatState = col.GetComponentInParent<EnemyFloatState>();
            if (floatState == null) continue;

            if (!unique.Add(floatState))
                continue;

            floatState.TryStartFloat(
                ability2RiseHeight,
                ability2RiseSpeed,
                ability2FloatDuration,
                ability2InitialFallVelocity,
                transform
            );
        }
    }

    bool HasValidLockTargetForAbility2()
    {
        if (lockOn == null || !lockOn.IsLocked)
            return false;

        Transform lockTarget = lockOn.CurrentTarget;
        if (lockTarget == null)
            return false;

        EnemyFloatState floatState = lockTarget.GetComponentInParent<EnemyFloatState>();
        if (floatState == null || floatState.IsFloating)
            return false;

        CombatStats targetStats = floatState.GetComponent<CombatStats>();
        if (targetStats == null || targetStats == stats || targetStats.IsDead)
            return false;

        float sqrRange = ability2Range * ability2Range;
        Vector3 to = LockTargetPointUtility.GetCapsuleCenter(floatState.transform) - transform.position;
        to.y = 0f;
        return to.sqrMagnitude <= sqrRange;
    }

    bool HasAnyEnemyInAbility2Range()
    {
        LayerMask mask = (ability2EnemyMask.value != 0) ? ability2EnemyMask : enemyMask;
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            ability2Range,
            mask,
            QueryTriggerInteraction.Collide
        );

        foreach (var col in hits)
        {
            EnemyFloatState floatState = col.GetComponentInParent<EnemyFloatState>();
            if (floatState == null) continue;
            if (floatState.IsFloating) continue;

            CombatStats targetStats = floatState.GetComponent<CombatStats>();
            if (targetStats == null || targetStats == stats || targetStats.IsDead) continue;
            return true;
        }

        return false;
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
