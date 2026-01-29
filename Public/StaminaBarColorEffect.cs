using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class StaminaBarColorEffect : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] Image fillImage;

    [Tooltip("固定来源：直接读取这个 CombatStats（适合：每个单位各自的UI条）。")]
    [SerializeField] CombatStats stats;

    [Tooltip("仅玩家需要。用于行动耗尽(跑/翻滚/重击)导致的体力=0 阶段变色。敌人可以不填。")]
    [SerializeField] PlayerStaminaActions playerActions;

    [Header("LockOn Target (for Enemy HUD)")]
    [Tooltip("如果这是“锁定目标敌人HUD”的体力条，请勾选：颜色将跟随 LockOnSystem.CurrentTargetStats。")]
    [SerializeField] bool useLockOnTargetStats = false;

    [Tooltip("玩家身上的 LockOnSystem。用于读取当前锁定目标的 CombatStats。")]
    [SerializeField] LockOnSystem lockOn;

    [Header("Colors")]
    [SerializeField] bool useOriginalAsNormal = true;
    [SerializeField] Color normalColor = Color.white;
    [SerializeField] Color exhaustedColor = new Color(1f, 0.65f, 0.2f, 1f);

    Color cachedOriginal;
    bool hasCached;

    bool lastExhausted;
    CombatStats lastStatsRef;

    void Awake()
    {
        if (fillImage == null)
            fillImage = GetComponent<Image>();

        // 固定 stats 模式：默认从父级抓（玩家/每个单位独立UI时有用）
        if (!useLockOnTargetStats && stats == null)
            stats = GetComponentInParent<CombatStats>();

        // 玩家 actions：从父级抓
        if (playerActions == null)
            playerActions = GetComponentInParent<PlayerStaminaActions>();

        // 锁定目标模式：尽量自动抓一次（也建议你在 Inspector 手动拖引用）
        if (useLockOnTargetStats && lockOn == null)
            lockOn = FindObjectOfType<LockOnSystem>();

        CacheOriginalIfNeeded();

        // 初次强制刷新（避免沿用编辑器里残留颜色）
        ForceRefresh();
    }

    void OnEnable()
    {
        // UI 重新启用时也刷新一次，避免残留上次目标颜色
        ForceRefresh();
    }

    void Update()
    {
        if (fillImage == null) return;

        CombatStats s = ResolveStats();

        // 目标切换（或从有目标->无目标）：强制刷新
        if (s != lastStatsRef)
        {
            lastStatsRef = s;
            Apply(GetExhausted(s), true);
            return;
        }

        bool exhausted = GetExhausted(s);
        if (exhausted == lastExhausted) return;

        Apply(exhausted, false);
    }

    CombatStats ResolveStats()
    {
        if (useLockOnTargetStats)
        {
            if (lockOn == null)
                lockOn = FindObjectOfType<LockOnSystem>();

            return lockOn != null ? lockOn.CurrentTargetStats : null;
        }

        if (stats == null)
            stats = GetComponentInParent<CombatStats>();

        return stats;
    }

    bool GetExhausted(CombatStats s)
    {
        // 没目标/没stats：视为不耗尽，恢复正常色
        if (s == null) return false;

        // 破防恢复期：体力=0 后到阈值前
        bool guardBroken = s.IsGuardBroken;

        // 玩家行动耗尽期：只在“非锁定目标模式”下才允许影响颜色
        bool actionExhausted =
            !useLockOnTargetStats &&
            playerActions != null &&
            playerActions.IsActionExhausted;

        return guardBroken || actionExhausted;
    }

    void CacheOriginalIfNeeded()
    {
        if (!useOriginalAsNormal || fillImage == null) return;
        if (hasCached) return;

        cachedOriginal = fillImage.color;
        hasCached = true;
    }

    void Apply(bool exhausted, bool force)
    {
        CacheOriginalIfNeeded();

        Color n = (useOriginalAsNormal && hasCached) ? cachedOriginal : normalColor;
        fillImage.color = exhausted ? exhaustedColor : n;

        lastExhausted = exhausted;
    }

    void ForceRefresh()
    {
        lastStatsRef = ResolveStats();
        Apply(GetExhausted(lastStatsRef), true);
    }
}
