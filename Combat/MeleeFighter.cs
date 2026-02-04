using System;
using System.Collections.Generic;
using UnityEngine;

public enum AttackState
{
    Idle,
    Attacking,
    ComboWindow
}

public enum AttackMoveType
{
    None,
    Run,
    Sprint
}

public enum AttackCategory
{
    Normal,
    Heavy,
    Ability
}

[RequireComponent(typeof(Animator))]
public class MeleeFighter : MonoBehaviour
{
    Animator anim;

    // ✅ 用于判定受击锁，阻止“受击时仍能启动AttackLayer攻击”
    CombatReceiver receiver;
    const int ATTACK_LAYER = 1;

    int maxNormalComboA = 1;
    int maxNormalComboB = 1;

    AttackState state = AttackState.Idle;
    AttackCategory currentCategory = AttackCategory.Normal;

    bool isAttackA;
    int comboIndex;
    bool requestQueued;

    // ===== Heavy =====
    bool isHeavy;
    bool heavyAttackA;

    // ===== Charge -> Heavy 交接保护（防止 Charge 的 AttackEnd/End 尾巴把状态解锁）=====
    bool isChargingHeavy;
    float ignoreChargeTailEventsUntil;
    const float CHARGE_TAIL_IGNORE_SECONDS = 0.15f; // 覆盖 CrossFade 过渡即可

    // ===== Ability（攻击型：Ability1/Ability2）=====
    bool isAbility;
    AbilityType currentAbility; // ✅ 来自 CombatEums.cs（Ability1/Ability2）

    // ===== 当前攻击数据 =====
    AttackData currentAttackData;

    // Shared hit registry for THIS attacker and THIS attack window.
    readonly HashSet<IHittable> registeredHitTargets = new HashSet<IHittable>();

    [Header("HitBoxes")]
    [SerializeField] MonoBehaviour[] hitBoxBehaviours;

    IHitBox[] hitBoxes;

    [Header("Attack Configs")]
    [SerializeField] AttackConfig[] attackConfigs;

    Dictionary<(AttackSourceType, int), AttackConfig> configMap;

    bool IsInHitLock => receiver != null && receiver.IsInHitLock;

    [Header("Animator CrossFade")]
    [SerializeField, Range(0f, 0.15f)]
    float attackCrossFadeTime = 0f;

    void Awake()
    {
        anim = GetComponent<Animator>();
        receiver = GetComponent<CombatReceiver>();

        BuildConfigMap();

        hitBoxes = new IHitBox[hitBoxBehaviours.Length];
        for (int i = 0; i < hitBoxBehaviours.Length; i++)
        {
            hitBoxes[i] = hitBoxBehaviours[i] as IHitBox;
            if (hitBoxes[i] == null)
                Debug.LogError($"[MeleeFighter] HitBox {hitBoxBehaviours[i].name} does not implement IHitBox");
        }
    }

    void BuildConfigMap()
    {
        configMap = new Dictionary<(AttackSourceType, int), AttackConfig>();

        maxNormalComboA = 1;
        maxNormalComboB = 1;

        foreach (var cfg in attackConfigs)
        {
            if (cfg == null) continue;

            var key = (cfg.sourceType, cfg.comboIndex);
            if (!configMap.ContainsKey(key))
                configMap.Add(key, cfg);

            if (cfg.sourceType == AttackSourceType.AttackA)
                maxNormalComboA = Mathf.Max(maxNormalComboA, cfg.comboIndex);

            if (cfg.sourceType == AttackSourceType.AttackB)
                maxNormalComboB = Mathf.Max(maxNormalComboB, cfg.comboIndex);
        }
    }

    int GetMaxNormalCombo(bool attackA) => attackA ? maxNormalComboA : maxNormalComboB;

    bool CanPlayNextNormalCombo(bool attackA, int nextComboIndex)
    {
        if (nextComboIndex > GetMaxNormalCombo(attackA))
            return false;

        var type = attackA ? AttackSourceType.AttackA : AttackSourceType.AttackB;
        if (configMap == null || !configMap.ContainsKey((type, nextComboIndex)))
            return false;

        string stateName = (attackA ? "AttackA" : "AttackB") + nextComboIndex;
        if (anim == null || !anim.HasState(ATTACK_LAYER, Animator.StringToHash(stateName)))
            return false;

        return true;
    }

    /* ================= 对外接口 ================= */

    public void InterruptAttack()
    {
        if (state != AttackState.Attacking &&
            state != AttackState.ComboWindow)
            return;

        ResetAttack();

        // 立刻退出 Attack Layer
        anim.CrossFadeInFixedTime("Empty", attackCrossFadeTime, ATTACK_LAYER);
    }

    // ✅ AI / gameplay can listen to hit-confirm (hit OR blocked OR guardbreak OR perfectblock)
    public event Action<AttackData> OnHitLanded;

    public void NotifyHitLanded(AttackData data)
    {
        OnHitLanded?.Invoke(data);
    }

    public void TryAttack(bool attackA, AttackMoveType moveType)
    {
        // ✅ 受击锁期间禁止启动攻击
        if (IsInHitLock) return;

        // ===== 跑 / 冲刺攻击 =====
        if (moveType != AttackMoveType.None)
        {
            if (state != AttackState.Idle) return;

            currentCategory = AttackCategory.Normal;
            isAttackA = attackA;

            CreateMoveAttackData(attackA, moveType);
            PlayMoveAttack(attackA, moveType);
            state = AttackState.Attacking;
            return;
        }

        // ===== 普通连段 =====
        if (state == AttackState.Idle)
        {
            currentCategory = AttackCategory.Normal;
            isAttackA = attackA;
            comboIndex = 1;

            CreateComboAttackData(comboIndex);
            PlayComboAttack(isAttackA, comboIndex);
            state = AttackState.Attacking;
            return;
        }

        // Attacking：只缓存请求，等 Combo 事件释放
        if (state == AttackState.Attacking)
        {
            if (attackA != isAttackA) return;

            int next = comboIndex + 1;
            if (!CanPlayNextNormalCombo(isAttackA, next)) return;

            requestQueued = true;
            return;
        }

        // ComboWindow：立刻起下一段（仍保留旧体验）
        if (state == AttackState.ComboWindow)
        {
            if (attackA != isAttackA) return;

            int next = comboIndex + 1;
            if (!CanPlayNextNormalCombo(isAttackA, next)) return;

            comboIndex = next;
            CreateComboAttackData(comboIndex);
            PlayComboAttack(isAttackA, comboIndex);
            state = AttackState.Attacking;
        }
    }

    // ✅ 仅用于 AI：从任意连段段数起手（A2/A3/A4... 或 B2/B3...）
    public bool TryStartNormalAt(bool attackA, int startComboIndex)
    {
        if (IsInHitLock) return false;
        if (state != AttackState.Idle) return false;

        if (startComboIndex < 1) startComboIndex = 1;

        currentCategory = AttackCategory.Normal;
        isAttackA = attackA;
        requestQueued = false;

        if (!CanPlayNextNormalCombo(isAttackA, startComboIndex))
            return false;

        comboIndex = startComboIndex;

        CreateComboAttackData(comboIndex);
        PlayComboAttack(isAttackA, comboIndex);
        state = AttackState.Attacking;
        return true;
    }

    // ===== Heavy =====
    [SerializeField] bool heavyUseCharge = true; // 默认兼容旧流程；敌人没有蓄力就关掉
    public void RequestHeavy(bool attackA)
    {
        if (IsInHitLock) return;

        if (state != AttackState.Idle && state != AttackState.ComboWindow)
            return;

        currentCategory = AttackCategory.Heavy;
        isHeavy = true;
        heavyAttackA = attackA;

        if (heavyUseCharge && HasAttackState(attackA ? "ChargeA" : "ChargeB"))
        {
            isChargingHeavy = true;
            ignoreChargeTailEventsUntil = 0f;

            currentAttackData = null;          // 蓄力阶段不带 AttackData
            registeredHitTargets.Clear();
            PlayChargeAttack(attackA);
            state = AttackState.Attacking;     // 蓄力也锁移动
            return;
        }

        CreateHeavyAttackData(attackA);
        PlayHeavyAttack(attackA);
        state = AttackState.Attacking;
    }

    bool HasAttackState(string stateName)
    {
        return anim != null && anim.HasState(ATTACK_LAYER, Animator.StringToHash(stateName));
    }

    // ===== Ability（攻击型：Ability1/Ability2）=====
    public void RequestAbility(AbilityType type)
    {
        if (IsInHitLock) return;
        if (state != AttackState.Idle) return;

        currentCategory = AttackCategory.Ability;
        isAbility = true;
        currentAbility = type;

        CreateAbilityAttackData(type);
        PlayAbilityAttack(type);
        state = AttackState.Attacking;
    }

    /* ================= Animation Events ================= */

    public void Begin()
    {
        // 事件权威：Begin 本身不切状态（状态在 TryAttack/RequestHeavy/RequestAbility 已设置）
    }

    public void AttackBegin()
    {
        // ✅ 保险：若这一帧进入受击锁且非霸体，绝不允许开 HitBox
        if (IsInHitLock && !IsInSuperArmor)
        {
            foreach (var box in hitBoxes)
                box?.DisableHitBox();
            return;
        }

        if (currentAttackData == null)
            return;

        // New attack window => clear registry.
        registeredHitTargets.Clear();

        bool hasRequiredType = TryGetABHitBoxType(currentAttackData.sourceType, out HitBoxType requiredType);

        foreach (var box in hitBoxes)
        {
            if (box == null) continue;

            // typed hitbox filter (A/B)
            if (hasRequiredType && box is IAttackTypedHitBox typedBox)
            {
                if (typedBox.HitBoxType != requiredType)
                {
                    box.DisableHitBox();
                    continue;
                }
            }

            box.EnableHitBox(currentAttackData);
        }
    }

    public void AttackImpact() { }

    public void AttackEnd()
    {
        foreach (var box in hitBoxes)
            box?.DisableHitBox();

        // AttackEnd 只关判定窗口，不做任何状态/连招/移动锁处理
    }

    // ✅ 兼容：如果某些动画还在用 float 参数事件，等价于无参 AttackEnd（不再区分 isFinal）
    public void AttackEnd(float _)
    {
        AttackEnd();
    }

    // ✅ 新事件：Combo（唯一职责：开启连招窗 + 解除移动锁 + 释放缓存连段）
    public void Combo()
    {
        // ✅ 蓄力阶段/Charge尾巴期间，忽略 Combo（避免错误解锁/开窗）
        if (isChargingHeavy || (ignoreChargeTailEventsUntil > 0f && Time.time < ignoreChargeTailEventsUntil))
            return;

        state = AttackState.ComboWindow;

        // ✅ 释放“缓存连段输入”
        if (requestQueued && currentCategory == AttackCategory.Normal)
        {
            requestQueued = false;

            int next = comboIndex + 1;
            if (CanPlayNextNormalCombo(isAttackA, next))
            {
                comboIndex = next;
                CreateComboAttackData(comboIndex);
                PlayComboAttack(isAttackA, comboIndex);
                state = AttackState.Attacking;
            }
        }
    }

    public void End()
    {
        // ✅ Charge 尾巴 End 必须忽略，否则会 ResetAttack -> Idle 导致解锁
        if (isChargingHeavy || (ignoreChargeTailEventsUntil > 0f && Time.time < ignoreChargeTailEventsUntil))
            return;

        ignoreChargeTailEventsUntil = 0f;
        ResetAttack();
    }

    public void OnChargeEnd()
    {
        if (!isHeavy) return;

        // ✅ Charge -> Heavy 交接开始：短时间忽略 Charge 的 AttackEnd/End 尾巴事件
        isChargingHeavy = false;
        ignoreChargeTailEventsUntil = Time.time + CHARGE_TAIL_IGNORE_SECONDS;

        // ✅ 重击阶段必定处于 Attacking（锁移动）
        state = AttackState.Attacking;
        requestQueued = false;

        // ✅ 到真正出手这一刻才创建 Heavy 的 AttackData（霸体从这里开始）
        CreateHeavyAttackData(heavyAttackA);
        PlayHeavyAttack(heavyAttackA);
    }

    void ResetAttack()
    {
        isChargingHeavy = false;
        ignoreChargeTailEventsUntil = 0f;

        state = AttackState.Idle;
        comboIndex = 0;
        requestQueued = false;

        isHeavy = false;
        isAbility = false;
        currentCategory = AttackCategory.Normal;
        currentAttackData = null;

        registeredHitTargets.Clear();

        foreach (var box in hitBoxes)
            box?.DisableHitBox();
    }

    /* ================= Hit De-dup ================= */

    public bool TryRegisterHit(IHittable target)
    {
        if (target == null) return false;
        return registeredHitTargets.Add(target);
    }

    bool TryGetABHitBoxType(AttackSourceType sourceType, out HitBoxType type)
    {
        switch (sourceType)
        {
            case AttackSourceType.AttackA:
            case AttackSourceType.RunAttackA:
            case AttackSourceType.SprintAttackA:
            case AttackSourceType.HeavyAttackA:
                type = HitBoxType.A;
                return true;

            case AttackSourceType.AttackB:
            case AttackSourceType.RunAttackB:
            case AttackSourceType.SprintAttackB:
            case AttackSourceType.HeavyAttackB:
                type = HitBoxType.B;
                return true;
        }

        type = default;
        return false;
    }

    /* ================= AttackData 构建 ================= */

    void CreateComboAttackData(int combo)
    {
        CreateFromConfig(
            isAttackA ? AttackSourceType.AttackA : AttackSourceType.AttackB,
            combo
        );
    }

    void CreateMoveAttackData(bool attackA, AttackMoveType moveType)
    {
        AttackSourceType type =
            moveType == AttackMoveType.Run
                ? (attackA ? AttackSourceType.RunAttackA : AttackSourceType.RunAttackB)
                : (attackA ? AttackSourceType.SprintAttackA : AttackSourceType.SprintAttackB);

        CreateFromConfig(type, 1);
    }

    void CreateHeavyAttackData(bool attackA)
    {
        CreateFromConfig(
            attackA ? AttackSourceType.HeavyAttackA : AttackSourceType.HeavyAttackB,
            1
        );
    }

    void CreateAbilityAttackData(AbilityType type)
    {
        AttackSourceType source = type == AbilityType.Ability1
            ? AttackSourceType.Ability1
            : AttackSourceType.Ability2;

        CreateFromConfig(source, 1);
    }

    void CreateFromConfig(AttackSourceType sourceType, int combo)
    {
        if (!configMap.TryGetValue((sourceType, combo), out var cfg))
        {
            Debug.LogWarning($"[MeleeFighter] Missing AttackConfig: {sourceType} combo {combo}");
            currentAttackData = null;
            return;
        }

        currentAttackData = new AttackData(
            transform,
            cfg.sourceType,
            cfg.hitReaction,
            cfg.hpDamage,
            cfg.staminaDamage
        )
        {
            canBeBlocked = cfg.canBeBlocked,
            canBeParried = cfg.canBeParried,
            canBreakGuard = cfg.canBreakGuard,
            hasSuperArmor = cfg.hasSuperArmor,
        };
    }

    /* ================= 播放 ================= */

    void PlayComboAttack(bool attackA, int combo)
    {
        anim.CrossFadeInFixedTime(
            (attackA ? "AttackA" : "AttackB") + combo,
            attackCrossFadeTime,
            ATTACK_LAYER
        );
    }

    void PlayMoveAttack(bool attackA, AttackMoveType moveType)
    {
        string stateName = moveType switch
        {
            AttackMoveType.Run => attackA ? "RunAttackA" : "RunAttackB",
            AttackMoveType.Sprint => attackA ? "SprintAttackA" : "SprintAttackB",
            _ => ""
        };

        anim.CrossFadeInFixedTime(stateName, attackCrossFadeTime, ATTACK_LAYER);
    }

    void PlayChargeAttack(bool attackA)
    {
        anim.CrossFadeInFixedTime(
            attackA ? "ChargeA" : "ChargeB",
            attackCrossFadeTime,
            ATTACK_LAYER
        );
    }

    void PlayHeavyAttack(bool attackA)
    {
        anim.CrossFadeInFixedTime(
            attackA ? "HeavyAttackA" : "HeavyAttackB",
            attackCrossFadeTime,
            ATTACK_LAYER
        );
    }

    void PlayAbilityAttack(AbilityType type)
    {
        string name = type == AbilityType.Ability1 ? "Ability1" : "Ability2";
        anim.CrossFadeInFixedTime(name, attackCrossFadeTime, ATTACK_LAYER);
    }

    /* ================= Readonly State ================= */

    public bool IsInAttackLock => state == AttackState.Attacking;

    // ✅ 霸体真相：只有“正在攻击中”且当前 AttackData 标记了霸体才算
    public bool IsInSuperArmor => IsInAttackLock && currentAttackData != null && currentAttackData.hasSuperArmor;

    public bool IsInComboWindow => state == AttackState.ComboWindow;
    public int CurrentComboIndex => comboIndex;
    public bool CurrentIsAttackA => isAttackA;
    public AttackCategory CurrentAttackCategory => currentCategory;
    public int MaxNormalComboA => maxNormalComboA;
    public int MaxNormalComboB => maxNormalComboB;
}