using System;
using UnityEngine;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(CombatStats))]
public class CombatReceiver : MonoBehaviour, IHittable
{
    // ✅ BlockHit 在 HitRecover 触发：供 AI 做“防御反击”决策
    public event Action<Transform> OnBlockedHitRecover;

    Animator anim;
    BlockController block;
    CombatStats stats;
    HitReactionFilter reactionFilter;
    bool selfIsPlayer;

    Transform lastAttacker;
    bool isInHitLock;

    [Header("Hit Lock Fallback")]
    [Tooltip("受击动画事件丢失时的受击锁超时兜底（秒）。")]
    [SerializeField] bool enableHitLockTimeout = true;
    [SerializeField] float hitLockTimeout = 1.0f;
    float hitLockStartTime = -999f;

    // ✅ 记录上一次受击结果是否为 Blocked（用于在 HitRecover 时精确触发反击窗口）
    bool lastHitWasBlocked;

    int reactLayer = -1;
    [SerializeField] float reactRestartFade = 0.02f;

    [Header("Hit Reaction Direction")]
    [Tooltip("0=Front, 1=Back。用于 LightHit/MidHit/HeavyHit 内部 BlendTree 选择正面/背面受击动画。")]
    [SerializeField] string hitDirParam = "HitDir";
    [Tooltip("前方受击有效角度（度），像有效防御角度一样：填 180 表示前方 180° 都算 Front，其余算 Back。范围 0~360。")]
    [SerializeField, Range(0f, 360f)] float hitFrontAngleDeg = 180f;

    bool animHasHitDirParam;
    bool lastHitFromBack;

    [Header("Hit Stop")]

    [SerializeField, Range(0f, 1f)] float hitStopHitScale = 0.06f;
    [SerializeField] float hitStopHitDuration = 0.06f;

    [SerializeField, Range(0f, 1f)] float guardBreakHitStopScale = 0.1f;
    [SerializeField] float guardBreakHitStopDuration = 0.1f;

    [SerializeField, Range(0f, 1f)] float perfectBlockHitStopScale = 0.07f;
    [SerializeField] float perfectBlockHitStopDuration = 0.07f;

    [SerializeField] bool useLocalHitStop = false;

    // ✅ 原来是 const PERFECT_BLOCK_SPECIAL_BONUS = 50;
    // ✅ 现在改为 Inspector 可调（默认值仍为 50，确保不改变现有行为）
    [Header("Special Gain")]
    [Tooltip("完美防御成功时，防御者获得的特殊能力槽奖励（默认50，保持原行为）。")]
    [SerializeField] int perfectBlockSpecialBonus = 50;

    // ✅ 新增：命中获得能量倍率（默认1=旧行为）
    [Tooltip("未防御命中(Hit)时：受击者获得的特殊能力槽倍率。增量=实际扣血量×倍率（四舍五入）。默认1=保持旧行为。")]
    [SerializeField, Range(0f, 5f)] float specialGainScaleVictim = 0f;

    [Tooltip("未防御命中(Hit)时：攻击者获得的特殊能力槽倍率。增量=实际扣血量×倍率（四舍五入）。默认1=保持旧行为。")]
    [SerializeField, Range(0f, 5f)] float specialGainScaleAttacker = 0.5f;

    [Header("Directional Block (Front Only)")]
    [SerializeField] bool enableDirectionalBlock = true;
    [SerializeField, Range(0f, 180f)] float blockEffectiveAngle = 90f;
    // ===== Public read-only access (for AI gating) =====
    public bool DirectionalBlockEnabled => enableDirectionalBlock;
    public float BlockEffectiveAngle => blockEffectiveAngle;

    /// <summary>
    /// 使用与防御完全一致的“前方扇形”判定：传入一个世界坐标点，判断它是否位于我前方有效角度内。
    /// </summary>
    public bool IsWorldPointInFront(Vector3 worldPoint)
    {
        if (!enableDirectionalBlock) return true;

        Vector3 dir = worldPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return true;
        dir.Normalize();

        float threshold = Mathf.Cos(0.5f * blockEffectiveAngle * Mathf.Deg2Rad);
        return Vector3.Dot(transform.forward, dir) >= threshold;
    }

    bool IsAttackFromFront(Transform attacker)
    {
        if (!enableDirectionalBlock) return true;
        if (attacker == null) return true;

        Vector3 dir = attacker.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return true;
        dir.Normalize();

        float dot = Vector3.Dot(transform.forward, dir);
        float threshold = Mathf.Cos(0.5f * blockEffectiveAngle * Mathf.Deg2Rad);
        return dot >= threshold;
    }

    bool ComputeHitFromBack(Transform attacker, Transform projectile)
    {
        Transform src = projectile != null ? projectile : attacker;
        if (src == null) return false;

        Vector3 dir = src.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return false;
        dir.Normalize();

        // 特判：360° 代表永远算 Front；0° 代表永远算 Back
        float a = Mathf.Clamp(hitFrontAngleDeg, 0f, 360f);
        if (a >= 359.999f) return false;
        if (a <= 0.001f) return true;

        float dot = Vector3.Dot(transform.forward, dir);
        float cosHalf = Mathf.Cos((a * 0.5f) * Mathf.Deg2Rad);

        bool isFront = dot >= cosHalf;
        return !isFront; // Back
    }

    static bool HasAnimParam(Animator animator, string paramName, AnimatorControllerParameterType type)
    {
        if (animator == null || string.IsNullOrEmpty(paramName)) return false;
        foreach (var p in animator.parameters)
        {
            if (p.name == paramName && p.type == type) return true;
        }
        return false;
    }

    // =========================
    // ✅ PerfectBlock -> Counter rule
    // =========================
    [Header("Perfect Block -> Counter")]
    [Tooltip("当完美防御的惩罚导致攻击者体力<=0时，防御者播放 Counter（否则播放 PerfectBlock）。")]
    [SerializeField] string counterStateName = "Counter";

    [Tooltip("当触发 Counter 条件时，让攻击者播放的受击状态（通常为 HeavyHit）。")]
    [SerializeField] string counterPunishAttackerState = "HeavyHit";

    // 本次受击是否触发了 Counter（只对 PerfectBlock 那一帧有效）
    bool lastPerfectBlockTriggeredCounter;

    [Header("Perfect Block Punish Attacker")]
    [Tooltip("完美防御成功后是否惩罚攻击者（扣体力；若打到0则触发 Counter+HeavyHit）。")]
    [SerializeField] bool punishAttackerOnPerfectBlock = true;

    [Tooltip("完美防御惩罚攻击者扣多少体力（建议 10~60）。")]
    [SerializeField] int perfectBlockPunishStamina = 30;

    [Tooltip("攻击者受击播放所在层名（默认 React Layer）。")]
    [SerializeField] string attackerReactLayerName = "React Layer";

    // =========================================================
    // ✅ I-Frame：由翻滚/躲避动画事件决定无敌窗口（事件权威）
    // =========================================================
    [Header("I-Frame (Invincibility)")]
    [Tooltip("当前是否处于无敌帧（由动画事件 IFrameBegin/IFrameEnd 控制）")]
    [SerializeField] bool isInvincible;

    public bool IsInvincible => isInvincible;

    // ✅ 外部强制无敌（暗杀/剧情等）
    public void ForceSetInvincible(bool v) => isInvincible = v;

    // 动画事件：无敌开始
    public void IFrameBegin() => isInvincible = true;

    // 动画事件：无敌结束
    public void IFrameEnd() => isInvincible = false;

    // 兜底：外部需要时清掉无敌（例如死亡/强制重置）
    public void ForceClearIFrame() => isInvincible = false;

    // 兜底：外部需要时清掉受击锁（例如死亡回溯后的强制恢复）
    public void ForceClearHitLock()
    {
        isInHitLock = false;
        hitLockStartTime = -999f;
        lastHitWasBlocked = false;
    }

    void Update()
    {
        if (!enableHitLockTimeout)
            return;

        if (!isInHitLock)
            return;

        if (stats != null && stats.IsDead)
            return;

        if (Time.time - hitLockStartTime >= Mathf.Max(0.05f, hitLockTimeout))
            ForceClearHitLock();
    }

    void Awake()
    {
        anim = GetComponent<Animator>();
        block = GetComponent<BlockController>();
        stats = GetComponent<CombatStats>();
        reactionFilter = GetComponent<HitReactionFilter>();

        reactLayer = anim.GetLayerIndex("React Layer");
        if (reactLayer < 0) reactLayer = 0; // 找不到就回退到0层

        stats.OnDead += OnDead;
        selfIsPlayer = (GetComponentInParent<PlayerController>() != null);

        // ✅ 可选：若 Animator 没有 HitDir 参数，后续不 SetFloat，避免控制台刷参数不存在的警告。
        animHasHitDirParam = HasAnimParam(anim, hitDirParam, AnimatorControllerParameterType.Float);
    }

    /// <summary>
    /// ✅ 近战/通用命中入口（保持不变）
    /// </summary>
    public void OnHit(AttackData attackData)
    {
        ProcessHit(attackData, null);
    }

    /// <summary>
    /// ✅ 远程弹丸命中入口：返回结算结果给子弹（用于 PerfectBlock 反弹）
    /// </summary>
    public HitResultType ReceiveProjectile(AttackData attackData, Transform projectile)
    {
        HitResult r = ProcessHit(attackData, projectile);
        return r.resultType;
    }

    /// <summary>
    /// 内部统一处理：复用你现有的所有裁决/结算/动画/打断/HitStop/进战斗逻辑。
    /// projectile 参数目前仅用于扩展（例如未来做“命中同一子弹只处理一次”等），不改变现有行为。
    /// </summary>
    HitResult ProcessHit(AttackData attackData, Transform projectile)
    {
        // ✅ 无敌帧：直接无视该次攻击（不扣血/体力/特殊，不播受击，不进战斗）
        if (isInvincible)
            return new HitResult(HitResultType.Hit);

        // ✅ 尸体不接受任何命中结算（防止打尸体攒特殊值/播受击/进战斗）
        if (stats != null && stats.IsDead)
            return new HitResult(HitResultType.Hit);

        lastAttacker = attackData.attacker;

        // ✅ 受击方向（Front/Back）：不改变原有 Light/Mid/Heavy 的逻辑，仅提供 Animator 内部分流。
        // ✅ 只有当 Animator 已配置 HitDir(float) 时才启用（否则保持旧行为）。
        if (animHasHitDirParam)
        {
            lastHitFromBack = ComputeHitFromBack(attackData.attacker, projectile);
            anim.SetFloat(hitDirParam, lastHitFromBack ? 1f : 0f);
        }
        else
        {
            lastHitFromBack = false;
        }

        // 每次处理新命中前清空本次 PerfectBlock 的 Counter 标记
        lastPerfectBlockTriggeredCounter = false;

        HitResult result = ResolveHit(attackData);
        bool wasGuardBrokenBeforeApply = stats != null && stats.IsGuardBroken;

        // ✅ 记录本次是否为“防御命中”
        lastHitWasBlocked = (result.resultType == HitResultType.Blocked);

        ApplyResultToStats(result, attackData);
        PromoteResultToGuardBreakIfNeeded(ref result, wasGuardBrokenBeforeApply);

        EnemyFloatState floatState = GetComponent<EnemyFloatState>();
        if (floatState != null)
        {
            floatState.NotifyHitResult(result.resultType);
            floatState.NotifyHpAfterHit();
        }

        if (result.resultType == HitResultType.PerfectBlock)
        {
            // ✅ 仅近战完美防御才惩罚攻击者体力 / 触发 Counter
            // projectile != null 代表走 ReceiveProjectile 的远程弹丸入口:contentReference[oaicite:4]{index=4}
            bool isProjectileHit = (projectile != null);

            // 额外保险：用枚举明确远程来源（你已定义 RangeShot）:contentReference[oaicite:5]{index=5}
            bool isRangeShot = (attackData.sourceType == AttackSourceType.RangeShot);

            if (!isProjectileHit && !isRangeShot)
            {
                PerfectBlockPunishAttacker(attackData); // 这里面会扣体力并可能触发 Counter:contentReference[oaicite:6]{index=6}
            }
            // else: 远程子弹完美防御只负责“判定为 PerfectBlock”（用于反弹），不扣攻击者体力、不触发 Counter
        }

        bool suppressByFloat = floatState != null && floatState.SuppressHitReaction;

        bool isNoHit = (result.resultType == HitResultType.Hit &&
                result.reactionType == HitReactionType.NoHit);

        // ✅ NoHit：不播受击/不打断（保持霸体/免反应表现），但仍允许卡肉提升打击感。
        if (!isNoHit && !suppressByFloat)
        {
            PlayHitReaction(result);
            TryInterruptAttack(result);
        }

        TryHitStop(result, attackData);

        if (result.resultType == HitResultType.Hit ||
            result.resultType == HitResultType.GuardBreak)
        {
            NotifyEnterCombat(attackData.attacker);
        }

        bool attackerIsPlayer = IsPlayerAttacker(attackData.attacker);
        CombatSfxSignals.RaiseHitResolved(attackData, result.resultType, attackerIsPlayer, selfIsPlayer, attackData.attacker, transform);

        return result;
    }

    HitResult ResolveHit(AttackData attackData)
    {
        bool facingOk = IsAttackFromFront(attackData.attacker);

        // 1 完美防御
        if (block != null && block.IsInPerfectWindow && attackData.canBeParried && facingOk)
            return new HitResult(HitResultType.PerfectBlock);

        // 2 普通防御/破防
        else if (block != null && block.IsBlocking && attackData.canBeBlocked && facingOk)
        {
            bool willBreak =
                stats.CurrentStamina <= attackData.staminaDamage ||
                attackData.canBreakGuard;

            if (willBreak) return new HitResult(HitResultType.GuardBreak);
            return new HitResult(HitResultType.Blocked);
        }

        // 3 命中
        HitReactionType reaction = attackData.hitReaction;
        if (reactionFilter != null)
            reaction = reactionFilter.Filter(reaction);

        return new HitResult(HitResultType.Hit, reaction);

    }

    void PromoteResultToGuardBreakIfNeeded(ref HitResult result, bool wasGuardBrokenBeforeApply)
    {
        if (stats == null) return;
        if (wasGuardBrokenBeforeApply) return;
        if (!stats.IsGuardBroken) return;

        // ✅ 仅当“本次命中新触发破防”时，才统一走 GuardBreak 表现链路（HeavyHit）。
        // ✅ 若目标本来就处于破防恢复阶段，则保持本次命中的原始结果（Hit/Blocked）。
        if (result.resultType == HitResultType.Hit || result.resultType == HitResultType.Blocked)
            result = new HitResult(HitResultType.GuardBreak);
    }

    // ✅ 攻击型能力（Ability1/Ability2）造成的伤害不计入特殊值积累
    bool IsSpecialGainAttackSource(AttackData attackData)
    {
        if (attackData == null)
            return false;

        return attackData.sourceType != AttackSourceType.Ability1Short &&
               attackData.sourceType != AttackSourceType.Ability1Long;
    }

    bool ShouldGrantVictimSpecialFromThisAttack(AttackData attackData)
    {
        return IsSpecialGainAttackSource(attackData);
    }

    bool ShouldGrantAttackerSpecialFromThisAttack(AttackData attackData)
    {
        if (!IsSpecialGainAttackSource(attackData))
            return false;

        return attackData.grantSpecialToAttackerOnHit;
    }

    void ApplyResultToStats(HitResult result, AttackData attackData)
    {
        switch (result.resultType)
        {
            case HitResultType.Hit:
                {
                    // ✅ 未防御命中：主伤害扣血 + 生命命中时的体力穿透伤害
                    int hpRequest = Mathf.Max(0, attackData.hpDamage);

                    // ✅ 用“实际造成的生命伤害”计算特殊值（避免已死/溢出）
                    int beforeHP = stats.CurrentHP;
                    stats.TakeHPDamage(hpRequest);
                    int actualHpDamage = Mathf.Max(0, beforeHP - stats.CurrentHP);

                    int staminaPenetration = Mathf.Max(0, attackData.staminaPenetrationDamage);
                    if (staminaPenetration > 0)
                    {
                        bool wasGuardBroken = stats.IsGuardBroken;
                        stats.TakeStaminaDamage(staminaPenetration);
                        if (!wasGuardBroken && stats.IsGuardBroken)
                            RaiseVoiceSignals_OnGuardBreak(attackData);
                    }

                    // ✅ 语音事件：命中/击杀（只在实际扣血>0时触发命中）
                    if (actualHpDamage > 0)
                        RaiseVoiceSignals_OnHpHit(attackData);

                    if (stats.IsDead)
                        RaiseVoiceSignals_OnKilled(attackData);
                    if (actualHpDamage > 0)
                    {
                        // ✅ 改动：能量增量 = 实际扣血量 × 比例（四舍五入）
                        int victimGain = Mathf.RoundToInt(actualHpDamage * specialGainScaleVictim);
                        int attackerGain = Mathf.RoundToInt(actualHpDamage * specialGainScaleAttacker);

                        // 被打者加特殊（受攻击来源限制）
                        if (victimGain > 0 && ShouldGrantVictimSpecialFromThisAttack(attackData))
                            stats.AddSpecial(victimGain);

                        // 攻击者加特殊（可按攻击数据单独关闭）
                        if (attackerGain > 0 && ShouldGrantAttackerSpecialFromThisAttack(attackData))
                            TryAddSpecialToAttacker(attackData.attacker, attackerGain);
                    }

                    break;
                }

            case HitResultType.Blocked:
                {
                    // ✅ 防御命中：主伤害扣体力 + 防御命中时的生命穿透伤害
                    int st = Mathf.Max(0, attackData.staminaDamage);
                    stats.TakeStaminaDamage(st);

                    int hpPenetration = Mathf.Max(0, attackData.hpPenetrationDamage);
                    if (hpPenetration > 0)
                    {
                        stats.TakeHPDamage(hpPenetration);
                        if (!stats.IsDead)
                            RaiseVoiceSignals_OnHpHit(attackData);
                        else
                            RaiseVoiceSignals_OnKilled(attackData);
                    }

                    break;
                }

            case HitResultType.GuardBreak:
                {
                    // ✅ 破防：主伤害扣体力（可强制归零）+ 防御命中时的生命穿透伤害
                    if (attackData.canBreakGuard)
                        stats.ForceGuardBreak();
                    else
                        stats.TakeStaminaDamage(Mathf.Max(0, attackData.staminaDamage)); // 扣到0自动破防

                    int hpPenetration = Mathf.Max(0, attackData.hpPenetrationDamage);
                    if (hpPenetration > 0)
                    {
                        stats.TakeHPDamage(hpPenetration);
                        if (!stats.IsDead)
                            RaiseVoiceSignals_OnHpHit(attackData);
                        else
                            RaiseVoiceSignals_OnKilled(attackData);
                    }

                    if (block != null)
                        block.ForceReleaseBlock();
                    // ✅ 语音事件：破防
                    RaiseVoiceSignals_OnGuardBreak(attackData);
                    break;
                }

            case HitResultType.PerfectBlock:
                {
                    // ✅ 完美防御固定奖励（现在可在 Inspector 调整）
                    int bonus = Mathf.Max(0, perfectBlockSpecialBonus);
                    if (bonus > 0)
                        stats.AddSpecial(bonus);
                    break;
                }
        }
    }

    void PerfectBlockPunishAttacker(AttackData attackData)
    {
        if (!punishAttackerOnPerfectBlock) return;

        Transform attacker = attackData.attacker;
        if (attacker == null) return;
        if (attacker.root == transform.root) return;

        var attackerStats = attacker.GetComponentInParent<CombatStats>();
        if (attackerStats == null || attackerStats.IsDead) return;

        int st = Mathf.Max(0, perfectBlockPunishStamina);

        // ✅ 判断“这次惩罚是否把攻击者体力打到0”
        int before = attackerStats.CurrentStamina;

        if (st > 0)
            attackerStats.TakeStaminaDamage(st);

        int after = attackerStats.CurrentStamina;
        bool brokeToZero = (before > 0 && after <= 0);

        // ✅ PerfectBlock -> Counter 条件
        lastPerfectBlockTriggeredCounter = brokeToZero;

        // ✅ 只有 Counter 才让攻击者播 HeavyHit，并且【必定】打断攻击
        if (brokeToZero)
        {
            // 1) 攻击者播放 HeavyHit（或你配置的 counterPunishAttackerState）
            string stateToPlay = counterPunishAttackerState;

            if (!string.IsNullOrEmpty(stateToPlay))
            {
                var attackerAnim = attacker.GetComponentInParent<Animator>();
                if (attackerAnim != null)
                {
                    int layer = attackerAnim.GetLayerIndex(attackerReactLayerName);
                    if (layer < 0) layer = 0;

                    int hash = Animator.StringToHash(stateToPlay);
                    var info = attackerAnim.GetCurrentAnimatorStateInfo(layer);

                    bool isSameStatePlaying = (info.shortNameHash == hash && info.normalizedTime < 0.95f);
                    if (!isSameStatePlaying)
                    {
                        if (attackerAnim.HasState(layer, hash))
                            attackerAnim.CrossFadeInFixedTime(stateToPlay, reactRestartFade, layer, 0f);
                        else
                            attackerAnim.SetTrigger(stateToPlay);
                    }
                }
            }

            // 2) ✅ Counter 必定打断攻击者
            var attackerFighter = attacker.GetComponentInParent<MeleeFighter>();
            if (attackerFighter != null)
                attackerFighter.InterruptAttack();
        }
    }

    void TryAddSpecialToAttacker(Transform attacker, int value)
    {
        if (attacker == null || value <= 0) return;
        if (attacker.root == transform.root) return;

        var attackerStats = attacker.GetComponentInParent<CombatStats>();
        if (attackerStats == null) return;

        attackerStats.AddSpecial(value);
    }

    void PlayHitReaction(HitResult result)
    {
        // ✅ 霸体：普通命中不播受击反应（否则会触发HitBegin->受击锁，视觉/功能都像被打断）
        if (result.resultType == HitResultType.Hit)
        {
            var fighter = GetComponent<MeleeFighter>();
            if (fighter != null && fighter.IsInSuperArmor)
                return;
        }

        string stateName = null;

        switch (result.resultType)
        {
            case HitResultType.PerfectBlock:
                // ✅ 若此次完美防御把攻击者体力打到0 -> 播 Counter；否则播 PerfectBlock
                stateName = lastPerfectBlockTriggeredCounter ? counterStateName : "PerfectBlock";
                break;

            case HitResultType.Blocked:
                stateName = "BlockHit";
                break;

            case HitResultType.GuardBreak:
                stateName = "HeavyHit";
                break;

            case HitResultType.Hit:
                stateName = result.reactionType == HitReactionType.Light ? "LightHit" :
                            result.reactionType == HitReactionType.Mid ? "MidHit" :
                                                                          "HeavyHit";
                break;
        }

        if (string.IsNullOrEmpty(stateName))
            return;

        int hash = Animator.StringToHash(stateName);
        if (anim.HasState(reactLayer, hash))
        {
            anim.CrossFadeInFixedTime(stateName, reactRestartFade, reactLayer, 0f);
        }
        else
        {
            anim.SetTrigger(stateName);
        }
    }

    void TryInterruptAttack(HitResult result)
    {
        if (result.resultType != HitResultType.Hit &&
            result.resultType != HitResultType.GuardBreak)
            return;

        // ===== Melee =====
        var melee = GetComponent<MeleeFighter>();
        if (melee != null)
        {
            // ✅ 霸体：普通命中不打断；GuardBreak 仍打断
            if (!(result.resultType == HitResultType.Hit && melee.IsInSuperArmor))
                melee.InterruptAttack();
        }

        // ===== Range =====
        var range = GetComponent<RangeFighter>();
        if (range != null)
        {
            // ✅ 同理：若未来远程也做霸体，这里也能复用；GuardBreak 仍打断
            if (!(result.resultType == HitResultType.Hit && range.IsInSuperArmor))
                range.InterruptShoot();
        }
    }

    void TryHitStop(HitResult result, AttackData attackData)
    {
        if (TimeController.Instance == null) return;

        float scale;
        float duration;

        if (result.resultType == HitResultType.GuardBreak)
        {
            scale = guardBreakHitStopScale;
            duration = guardBreakHitStopDuration;
        }
        else if (result.resultType == HitResultType.PerfectBlock)
        {
            scale = perfectBlockHitStopScale;
            duration = perfectBlockHitStopDuration;
        }
        else if (result.resultType == HitResultType.Hit)
        {
            // ✅ 统一策略：命中卡肉只用基线（hitStopHitScale/duration），轻重完全交给 AttackConfig.hitStopWeight。
            // 例如：0=无卡肉，0.3=很轻，1=默认，2=更重（更强减速+更长停顿）
            scale = hitStopHitScale;
            duration = hitStopHitDuration;
        }
        else
        {
            return;
        }

        float weight = attackData != null ? Mathf.Max(0f, attackData.hitStopWeight) : 1f;
        if (weight <= 0f) return;

        // 配置化命中权重：weight>1 更重（scale更小、duration更长）；<1 更轻。
        scale = Mathf.Clamp01(scale / weight);
        duration = duration * weight;

        if (useLocalHitStop)
            TimeController.Instance.HitStopLocal(attackData != null ? attackData.attacker : null, transform, duration);
        else
            TimeController.Instance.HitStop(scale, duration);
    }

    void OnDead()
    {
        isInHitLock = true;
        isInvincible = false; // ✅ 死亡兜底：清无敌
        SendMessage("OnCharacterDead", SendMessageOptions.DontRequireReceiver);
    }

    void NotifyEnterCombat(Transform attacker)
    {
        EnemyController controller = GetComponent<EnemyController>();
        if (controller != null)
            controller.OnAttacked(attacker);
    }

    public void HitBegin()
    {
        isInHitLock = true;
        hitLockStartTime = Time.time;
        FaceAttacker();
    }

    public void HitRecover()
    {
        isInHitLock = false;
        hitLockStartTime = -999f;

        // ✅ 只有“BlockHit”的那次 HitRecover 才触发反击窗口
        if (lastHitWasBlocked)
        {
            OnBlockedHitRecover?.Invoke(lastAttacker);
            lastHitWasBlocked = false;
        }
    }

    public void HitEnd()
    {
        isInHitLock = false;
        hitLockStartTime = -999f;
        lastHitWasBlocked = false;
    }

    void FaceAttacker()
    {
        if (lastAttacker == null) return;

        // ✅ 背后受击：不强制转身面对攻击者，否则会破坏“背后受击动画”的视觉方向感。
        // ✅ 仅在启用 HitDir 分流时生效（否则保持旧行为）。
        if (animHasHitDirParam && lastHitFromBack) return;

        Vector3 dir = lastAttacker.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(dir);
    }

    public bool IsInHitLock => isInHitLock;

    static bool IsPlayerAttacker(Transform t)
    {
        return t != null && t.GetComponentInParent<PlayerController>() != null;
    }

    void RaiseVoiceSignals_OnHpHit(AttackData attackData)
    {
        bool attackerIsPlayer = IsPlayerAttacker(attackData.attacker);

        // 玩家打中敌人
        if (!selfIsPlayer && attackerIsPlayer)
            CombatSignals.RaisePlayerHitEnemy();

        // 敌人打中玩家
        if (selfIsPlayer && !attackerIsPlayer)
            CombatSignals.RaiseEnemyHitPlayer();
    }

    void RaiseVoiceSignals_OnKilled(AttackData attackData)
    {
        bool attackerIsPlayer = IsPlayerAttacker(attackData.attacker);

        // 玩家击杀敌人
        if (!selfIsPlayer && attackerIsPlayer)
            CombatSignals.RaisePlayerKillEnemy();

        // 敌人击杀玩家
        if (selfIsPlayer && !attackerIsPlayer)
            CombatSignals.RaisePlayerKilledByEnemy();
    }

    void RaiseVoiceSignals_OnGuardBreak(AttackData attackData)
    {
        bool attackerIsPlayer = IsPlayerAttacker(attackData.attacker);

        // 玩家破防敌人
        if (!selfIsPlayer && attackerIsPlayer)
            CombatSignals.RaisePlayerGuardBreakEnemy();

        // 敌人破防玩家
        if (selfIsPlayer && !attackerIsPlayer)
            CombatSignals.RaiseEnemyGuardBreakPlayer();
    }

}
