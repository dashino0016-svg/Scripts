using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Optional stamina color effect:
/// - Exhausted (guard broken) or player action exhausted => change fill color
/// - Can also be used on the lock-on enemy stamina bar by reading LockOnSystem.CurrentTargetStats
///
/// Attach it to the *Fill* Image object.
/// </summary>
[DisallowMultipleComponent]
public class StaminaBarColorEffect : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] Image fillImage;

    [Tooltip("Direct stats source (non lock-on mode).")]
    [SerializeField] CombatStats stats;

    [Tooltip("Player-only: used to detect action exhausted (sprint/roll/heavy etc).")]
    [SerializeField] PlayerStaminaActions playerActions;

    [Header("LockOn Target (for Enemy HUD)")]
    [SerializeField] bool useLockOnTargetStats = false;
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

        if (!useLockOnTargetStats && stats == null)
            stats = GetComponentInParent<CombatStats>();

        if (playerActions == null)
            playerActions = GetComponentInParent<PlayerStaminaActions>();

        if (useLockOnTargetStats && lockOn == null)
            lockOn = FindObjectOfType<LockOnSystem>();

        CacheOriginalIfNeeded();
        ForceRefresh();
    }

    void OnEnable() => ForceRefresh();

    void Update()
    {
        if (fillImage == null) return;

        CombatStats s = ResolveStats();

        if (s != lastStatsRef)
        {
            lastStatsRef = s;
            Apply(GetExhausted(s));
            return;
        }

        bool exhausted = GetExhausted(s);
        if (exhausted == lastExhausted) return;

        Apply(exhausted);
    }

    /// <summary>
    /// Runtime helper: configure this effect to follow LockOnSystem.CurrentTargetStats.
    /// Useful when the bar is created by code.
    /// </summary>
    public void ConfigureForLockOn(LockOnSystem lockOnSystem)
    {
        useLockOnTargetStats = true;
        lockOn = lockOnSystem;
        stats = null;
        ForceRefresh();
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
        if (s == null) return false;

        bool guardBroken = s.IsGuardBroken;

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

    void Apply(bool exhausted)
    {
        CacheOriginalIfNeeded();
        Color n = (useOriginalAsNormal && hasCached) ? cachedOriginal : normalColor;
        fillImage.color = exhausted ? exhaustedColor : n;
        lastExhausted = exhausted;
    }

    public void ForceRefresh()
    {
        lastStatsRef = ResolveStats();
        Apply(GetExhausted(lastStatsRef));
    }
}
