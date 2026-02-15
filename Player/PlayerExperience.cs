using System;
using UnityEngine;

/// <summary>
/// Player experience model.
/// - Enemy death adds XP.
/// - When XP reaches maxXP, grants 1 XP Point and carries overflow.
/// - Player death clears current XP (bar), but XP Points persist.
/// </summary>
public class PlayerExperience : MonoBehaviour
{
    public static PlayerExperience Instance { get; private set; }

    [Header("XP")]
    [SerializeField] int maxXP = 100;
    [SerializeField] int currentXP = 0;

    [Header("XP Points")]
    [SerializeField] int xpPoints = 0;

    [Header("Auto Bind")]
    [SerializeField] CombatStats playerStats;

    public int MaxXP => Mathf.Max(1, maxXP);
    public int CurrentXP => Mathf.Clamp(currentXP, 0, MaxXP);
    public int XPPoints => Mathf.Max(0, xpPoints);

    /// <summary>旧事件：gainedAmount, currentXP, maxXP, xpPoints。</summary>
    public event Action<int, int, int, int> OnXPGained;

    /// <summary>新事件：amount, oldXP, newXP, maxXP, oldPoints, newPoints, levelUps</summary>
    public event Action<int, int, int, int, int, int, int> OnXPGainedDetailed;

    /// <summary>当经验状态变化时触发：currentXP, maxXP, xpPoints。</summary>
    public event Action<int, int, int> OnXPStateChanged;

    /// <summary>当玩家死亡导致经验条清空时触发。</summary>
    public event Action OnXPResetByDeath;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        AutoBind();

        maxXP = Mathf.Max(1, maxXP);
        currentXP = Mathf.Clamp(currentXP, 0, maxXP);
        xpPoints = Mathf.Max(0, xpPoints);
    }

    void OnEnable()
    {
        AutoBind();
        BindPlayerDead();
        RaiseStateChanged();
    }

    void OnDisable()
    {
        UnbindPlayerDead();
    }

    void AutoBind()
    {
        if (playerStats == null)
            playerStats = GetComponent<CombatStats>();
    }

    void BindPlayerDead()
    {
        if (playerStats == null) return;
        playerStats.OnDead -= HandlePlayerDead;
        playerStats.OnDead += HandlePlayerDead;
    }

    void UnbindPlayerDead()
    {
        if (playerStats == null) return;
        playerStats.OnDead -= HandlePlayerDead;
    }

    void HandlePlayerDead()
    {
        currentXP = 0;
        OnXPResetByDeath?.Invoke();
        RaiseStateChanged();
    }

    public void AddXP(int amount)
    {
        if (amount <= 0) return;

        maxXP = Mathf.Max(1, maxXP);

        int oldXP = currentXP;
        int oldPoints = xpPoints;

        int total = currentXP + amount;
        int levelUps = total / maxXP;

        xpPoints += levelUps;
        currentXP = total % maxXP;

        currentXP = Mathf.Clamp(currentXP, 0, maxXP);
        xpPoints = Mathf.Max(0, xpPoints);

        OnXPGainedDetailed?.Invoke(amount, oldXP, currentXP, maxXP, oldPoints, xpPoints, levelUps);
        OnXPGained?.Invoke(amount, currentXP, maxXP, xpPoints);

        RaiseStateChanged();
    }

    public void SetMaxXP(int newMax)
    {
        maxXP = Mathf.Max(1, newMax);
        currentXP = Mathf.Clamp(currentXP, 0, maxXP);
        RaiseStateChanged();
    }

    public void AddXPPoints(int points)
    {
        if (points <= 0) return;
        xpPoints += points;
        RaiseStateChanged();
    }

    void RaiseStateChanged()
    {
        OnXPStateChanged?.Invoke(CurrentXP, MaxXP, XPPoints);
    }
}
