using UnityEngine;

[RequireComponent(typeof(Animator))]
public class RangeFighter : MonoBehaviour
{
    // 攻击层（和 MeleeFighter 对齐）
    const int ATTACK_LAYER = 1;

    [Header("Refs")]
    [SerializeField] Animator anim;
    [SerializeField] Transform muzzle;                 // 枪口（子弹生成点）
    [SerializeField] RangeProjectile projectilePrefab; // 子弹脚本
    [SerializeField] AttackConfig shotConfig;          // 远程单发攻击配置（伤害/体力/可格挡等）

    [Header("Shoot")]
    [SerializeField] float cooldownSeconds = 0f;     // 开火冷却
    [SerializeField, Range(0f, 10f)] float spreadDegrees = 1.5f; // 散布

    [Header("SFX (Scheme1: Use AudioSource.clip)")]
    [SerializeField] AudioSource shotAudio;                 // 可不填，自动从 muzzle 上拿
    [SerializeField] Vector2 shotPitchRange = new Vector2(0.98f, 1.02f);

    [Header("Animator")]
    [Tooltip("AttackLayer 上的开火状态名（例如 Shoot / Fire / BlasterShot）。")]
    [SerializeField] string shootStateName = "Shoot";

    [Tooltip("AttackLayer 的空状态名。")]
    [SerializeField] string emptyStateName = "Empty";

    [SerializeField, Range(0f, 0.15f)] float crossFade = 0f;

    // ===== runtime =====
    float nextShootTime;
    bool isInAttackLock;
    bool pendingShoot;          // 是否已请求射击，等待 AttackBegin 动画事件真正发射
    Vector3 pendingAimDir;      // 外部喂入的瞄准方向（世界空间）
    Transform pendingAimTarget; // 可选：目标（用于反弹回瞄/AI）

    public bool IsInAttackLock => isInAttackLock;
    public bool IsInSuperArmor => isInAttackLock && shotConfig != null && shotConfig.hasSuperArmor;
    public Transform Muzzle => muzzle;

    void Awake()
    {
        if (anim == null) anim = GetComponent<Animator>();
        RefreshShotAudio();
    }

    void RefreshShotAudio()
    {
        if (shotAudio == null)
        {
            var host = (muzzle != null) ? muzzle.gameObject : gameObject;
            shotAudio = host.GetComponent<AudioSource>();
            if (shotAudio == null) shotAudio = host.AddComponent<AudioSource>();
        }

        shotAudio.playOnAwake = false;
        shotAudio.spatialBlend = 1f; // 3D
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

        // 与近战互斥：存在 MeleeFighter 且处于攻击/判定/连招窗时，拒绝开火
        MeleeFighter melee = GetComponent<MeleeFighter>();
        if (melee != null && melee.enabled)
        {
            if (melee.IsInAttackLock || melee.IsInComboWindow || melee.IsInHitWindow)
                return false;
        }

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

        // 播放 AttackLayer 射击动画（CrossFade 流）
        if (!PlayShootAnimation())
        {
            // 找不到状态就立刻回滚，避免 pendingShoot / isInAttackLock 卡死（因为不会有动画事件 End）
            pendingShoot = false;
            pendingAimTarget = null;
            isInAttackLock = false;
            return false;
        }

        nextShootTime = Time.time + cooldownSeconds;
        return true;
    }

    bool PlayShootAnimation()
    {
        if (anim == null) return false;

        int hash = Animator.StringToHash(shootStateName);
        if (!anim.HasState(ATTACK_LAYER, hash))
        {
            Debug.LogWarning($"[RangeFighter] AttackLayer({ATTACK_LAYER}) state '{shootStateName}' not found. CrossFade aborted.");
            return false;
        }

        anim.CrossFadeInFixedTime(shootStateName, crossFade, ATTACK_LAYER, 0f);
        return true;
    }

    // ======================
    // Animation Events（五段铁律）
    // ======================

    public void Begin()
    {
        // 被打断/取消后，动画事件 Begin 可能仍会被调用；这里必须门禁
        if (!pendingShoot)
            return;

        isInAttackLock = true;
    }

    public void AttackBegin()
    {
        if (!pendingShoot)
            return;

        FireProjectile();
    }

    public void AttackImpact() { }

    public void AttackEnd()
    {
        // 单发版不做 ComboWindow；保持 AttackLock 直到 End
    }

    public void End()
    {
        pendingShoot = false;
        pendingAimTarget = null;
        isInAttackLock = false;

        ForceEmptyState();
    }

    void ForceEmptyState()
    {
        if (anim == null) return;

        int hash = Animator.StringToHash(emptyStateName);
        if (anim.HasState(ATTACK_LAYER, hash))
            anim.CrossFadeInFixedTime(emptyStateName, crossFade, ATTACK_LAYER, 0f);
    }

    public void InterruptShoot()
    {
        if (!pendingShoot && !isInAttackLock)
            return;

        pendingShoot = false;
        pendingAimTarget = null;
        isInAttackLock = false;

        // 强制退出 AttackLayer，避免后续 AttackBegin 事件继续跑
        ForceEmptyState();

        // ✅ 已移除 Trigger 相关逻辑（ResetTrigger / SetTrigger）
    }

    // ======================
    // Fire
    // ======================

    void FireProjectile()
    {
        if (muzzle == null || projectilePrefab == null) return;

        if (shotAudio == null) RefreshShotAudio();

        Vector3 dir = pendingAimDir;

        if (pendingAimTarget != null)
        {
            Vector3 aimPoint = pendingAimTarget.position;

            CombatStats s = pendingAimTarget.GetComponentInParent<CombatStats>();
            if (s != null)
                aimPoint = LockTargetPointUtility.GetCapsuleCenter(s.transform);

            Vector3 d3 = aimPoint - muzzle.position;
            if (d3.sqrMagnitude > 0.0001f)
                dir = d3.normalized;
        }

        pendingAimDir = dir;

        if (spreadDegrees > 0.001f)
        {
            float yaw = Random.Range(-spreadDegrees, spreadDegrees);
            dir = Quaternion.Euler(0f, yaw, 0f) * dir;
            dir.Normalize();
        }

        AttackData data = BuildAttackDataFromConfig(shotConfig);

        if (shotAudio != null && shotAudio.clip != null)
        {
            shotAudio.pitch = Random.Range(shotPitchRange.x, shotPitchRange.y);
            shotAudio.PlayOneShot(shotAudio.clip);
        }

        RangeProjectile p = Instantiate(projectilePrefab, muzzle.position, Quaternion.LookRotation(dir));
        p.Init(transform, dir, data);
    }

    AttackData BuildAttackDataFromConfig(AttackConfig cfg)
    {
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
            hasSuperArmor = cfg.hasSuperArmor,
            hitStopWeight = cfg.hitStopWeight,
            staminaPenetrationDamage = cfg.staminaPenetrationDamage,
            hpPenetrationDamage = cfg.hpPenetrationDamage
        };

        return data;
    }
}
