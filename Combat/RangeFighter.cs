using UnityEngine;

[RequireComponent(typeof(Animator))]
public class RangeFighter : MonoBehaviour
{
    // йAttack Layer = 1MeleeFighter Ҳ
    const int ATTACK_LAYER = 1;

    [Header("Refs")]
    [SerializeField] Animator anim;
    [SerializeField] Transform muzzle;                 // 枪口（子弹生成点）
    [SerializeField] RangeProjectile projectilePrefab; // 你已改名的子弹脚本
    [SerializeField] AttackConfig shotConfig;          // 远程单发的攻击配置（伤害/体力/可格挡/可完美等）

    [Header("Shoot")]
    [SerializeField] float cooldownSeconds = 0f;     // 开火冷却
    [SerializeField, Range(0f, 10f)] float spreadDegrees = 1.5f; // 散布（星战味道）

    [Header("Animator")]
    [Tooltip("AttackLayer 上的开火状态名（例如 Shoot / Fire / BlasterShot）。")]
    [SerializeField] string shootStateName = "Shoot";

    [Tooltip("AttackLayer 的空状态名。")]
    [SerializeField] string emptyStateName = "Empty";

    [SerializeField, Range(0f, 0.15f)] float crossFade = 0f;

    // ===== runtime =====
    float nextShootTime;
    bool isInAttackLock;
    bool pendingShoot;         // 是否已请求射击，等待 AttackBegin 动画事件真正发射
    Vector3 pendingAimDir;     // 外部喂入的瞄准方向（世界空间）
    Transform pendingAimTarget;// 可选：目标（用于反弹回瞄/AI）

    // 对外只读：给 AI/Controller 做门禁
    public bool IsInAttackLock => isInAttackLock;
    public Transform Muzzle => muzzle;

    void Awake()
    {
        if (anim == null) anim = GetComponent<Animator>();
    }

    /// <summary>
    /// 外部请求射击（玩家/敌人通用）。
    /// RangeFighter 不读输入、不管相机；外部必须提供 aimDirection（世界空间）。
    /// 返回 true 代表已成功进入“等待动画事件发射”阶段。
    /// </summary>
    public bool TryShoot(Vector3 aimDirection, Transform aimTarget = null)
    {
        if (Time.time < nextShootTime) return false;
        if (isInAttackLock) return false;
        if (pendingShoot) return false;

        if (muzzle == null || projectilePrefab == null || shotConfig == null)
        {
            Debug.LogWarning($"[RangeFighter] Missing refs: muzzle/projectilePrefab/shotConfig.");
            return false;
        }

        if (aimDirection.sqrMagnitude < 0.0001f)
            aimDirection = muzzle.forward;

        pendingAimDir = aimDirection.normalized;
        pendingAimTarget = aimTarget;

        pendingShoot = true;

        // 进入攻击锁：权威由动画事件 Begin~End 维持，但这里先锁住，避免多次触发
        isInAttackLock = true;

        // 播放 AttackLayer 射击动画
        PlayShootAnimation();

        nextShootTime = Time.time + cooldownSeconds;
        return true;
    }

    void PlayShootAnimation()
    {
        if (anim == null) return;

        int hash = Animator.StringToHash(shootStateName);
        if (anim.HasState(ATTACK_LAYER, hash))
        {
            anim.CrossFadeInFixedTime(shootStateName, crossFade, ATTACK_LAYER, 0f);
        }
        else
        {
            // 兜底：如果你用 Trigger 流
            anim.SetTrigger(shootStateName);
        }
    }

    // ======================
    // Animation Events（五段铁律）
    // ======================

    // Begin：动画开始（你体系用它做攻击锁开始点）
    public void Begin()
    {
        // 这里不强制置 pendingShoot（已在 TryShoot 置）
        isInAttackLock = true;
    }

    // AttackBegin：开火时机点（权威：在这里生成子弹）
    public void AttackBegin()
    {
        if (!pendingShoot)
            return;

        FireProjectile();
    }

    // AttackImpact：保留给未来做“枪口火光/后坐/格挡火花”等
    public void AttackImpact() { }

    // AttackEnd：后摇结束，进入连发/输入窗口（你后面要连发可在这里扩展）
    public void AttackEnd()
    {
        // 单发版不做 ComboWindow；保持 AttackLock 直到 End
    }

    // End：动画结束（解锁点）
    public void End()
    {
        pendingShoot = false;
        pendingAimTarget = null;
        isInAttackLock = false;

        // 退出 AttackLayer（可选）
        ForceEmptyState();
    }

    void ForceEmptyState()
    {
        if (anim == null) return;

        int hash = Animator.StringToHash(emptyStateName);
        if (anim.HasState(ATTACK_LAYER, hash))
            anim.CrossFadeInFixedTime(emptyStateName, crossFade, ATTACK_LAYER, 0f);
    }

    // ======================
    // Fire
    // ======================

    void FireProjectile()
    {
        if (muzzle == null || projectilePrefab == null) return;

        Vector3 dir = pendingAimDir;

        // 星战风格：加一点散布（敌我通用）
        if (spreadDegrees > 0.001f)
        {
            float yaw = Random.Range(-spreadDegrees, spreadDegrees);
            dir = Quaternion.Euler(0f, yaw, 0f) * dir;
            dir.Normalize();
        }

        // 由 AttackConfig 构建 AttackData（与你近战一致：数据注入）
        AttackData data = BuildAttackDataFromConfig(shotConfig);

        // Instantiate
        RangeProjectile p = Instantiate(projectilePrefab, muzzle.position, Quaternion.LookRotation(dir));
        p.Init(transform, dir, data);
    }

    AttackData BuildAttackDataFromConfig(AttackConfig cfg)
    {
        // 目前先复用 cfg.sourceType（你后面会新增真正的 Ranged 枚举值再迁移）
        var data = new AttackData(
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
            hasSuperArmor = cfg.hasSuperArmor
        };

        return data;
    }
}
