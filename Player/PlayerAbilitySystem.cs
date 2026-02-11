using System;
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

    CombatReceiver receiver;

    // =========================
    // Ability1 - Force Push (Front Cone)
    // =========================
    [Header("Ability1 - Force Push (Front Cone)")]
    [SerializeField] AttackConfig ability1AttackConfig;

    [SerializeField] int costAbility1 = 100;

    [SerializeField] float ability1Range = 6f;

    [SerializeField, Range(10f, 180f)] float ability1ConeAngle = 80f;

    [SerializeField] LayerMask enemyMask;

    // =========================
    // Ability2 - Force Push (360 AoE)
    // =========================
    [Header("Ability2 - Force Push (360 AoE)")]
    [SerializeField] AttackConfig ability2AttackConfig;

    [SerializeField] int costAbility2 = 200;

    [SerializeField] float ability2Range = 6f;

    [SerializeField] LayerMask ability2EnemyMask;

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
    bool hasPending;
    Coroutine ability3Routine;

    void Awake()
    {
        if (stats == null)
            stats = GetComponent<CombatStats>();

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
        CombatSfxSignals.RaisePlayerAbility3TimeSlowEnd();
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

    public bool TryRequest(AbilityType type)
    {
        if (stats == null) return false;
        if (!IsImplemented(type)) return false;

        if (IsInHitLock())
            return false;

        int cost = GetCost(type);
        if (!stats.ConsumeSpecial(cost))
            return false;

        pending = type;
        hasPending = true;
        return true;
    }


    int GetCost(AbilityType type)
    {
        return type switch
        {
            AbilityType.Ability1 => costAbility1,
            AbilityType.Ability2 => costAbility2,
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
                PerformAbility1();
                break;
            case AbilityType.Ability2:
                PerformAbility2();
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
        CombatSfxSignals.RaisePlayerAbility3TimeSlowBegin();

        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        CombatSfxSignals.RaisePlayerAbility3TimeSlowEnd();
        ability3Routine = null;
    }

    void PerformAbility1()
    {
        if (TimeController.Instance != null)
            TimeController.Instance.HitStop(abilityShockwaveHitStopTime, abilityShockwaveHitStopTime);

        if (ability1AttackConfig == null)
        {
            Debug.LogError("[PlayerAbilitySystem] Ability1 Attack Config 未绑定。Ability1 不执行。");
            return;
        }

        Vector3 origin = transform.position;
        Vector3 forward = transform.forward;

        Collider[] hits = Physics.OverlapSphere(
            origin,
            ability1Range,
            enemyMask,
            QueryTriggerInteraction.Collide
        );

        HashSet<CombatStats> unique = new HashSet<CombatStats>();
        AttackData template = BuildAttackDataFromConfig(ability1AttackConfig);

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
            if (dist > ability1Range || dist < 0.001f) continue;

            float angle = Vector3.Angle(forward, to.normalized);
            if (angle > ability1ConeAngle * 0.5f) continue;

            IHittable hittable =
                targetStats.GetComponent<IHittable>() ??
                targetStats.GetComponentInParent<IHittable>();

            if (hittable == null) continue;

            AttackData data = template;
            data.attacker = transform;

            hittable.OnHit(data);
        }
    }

    void PerformAbility2()
    {
        if (TimeController.Instance != null)
            TimeController.Instance.HitStop(abilityShockwaveHitStopTime, abilityShockwaveHitStopTime);

        if (ability2AttackConfig == null)
        {
            Debug.LogError("[PlayerAbilitySystem] Ability2 Attack Config 未绑定。Ability2 不执行。");
            return;
        }

        Vector3 origin = transform.position;

        LayerMask mask = (ability2EnemyMask.value != 0) ? ability2EnemyMask : enemyMask;
        Collider[] hits = Physics.OverlapSphere(
            origin,
            ability2Range,
            mask,
            QueryTriggerInteraction.Collide
        );

        HashSet<CombatStats> unique = new HashSet<CombatStats>();
        AttackData template = BuildAttackDataFromConfig(ability2AttackConfig);

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
