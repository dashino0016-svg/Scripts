using UnityEngine;

/// <summary>
/// Listens to enemy CombatStats.OnDead and grants XP to PlayerExperience.
/// This is intentionally decoupled from EnemyController to avoid touching combat/death pipeline.
/// </summary>
[DisallowMultipleComponent]
public class EnemyExperienceGiver : MonoBehaviour
{
    [Header("Bind")]
    [SerializeField] CombatStats stats;

    [Header("XP")]
    [Tooltip("该敌人死亡时给予玩家的经验值。")]
    [Min(0)]
    [SerializeField] int experienceValue = 30;

    [Header("Legacy (migration only)")]
    [Tooltip("旧版经验值组件；迁移期用于自动兼容读取。")]
    [SerializeField] EnemyExperienceValue xpValue;

    bool awardedThisLife;
    bool lastDead;

    void Awake()
    {
        if (stats == null) stats = GetComponent<CombatStats>();
        if (xpValue == null) xpValue = GetComponent<EnemyExperienceValue>();

        // Migration compatibility: if legacy component exists, use its value.
        if (xpValue != null)
            experienceValue = Mathf.Max(0, xpValue.experienceValue);
    }

    void OnEnable()
    {
        lastDead = (stats != null) && stats.IsDead;
        awardedThisLife = lastDead; // already dead => don't award again
        if (stats != null)
        {
            stats.OnDead -= OnDead;
            stats.OnDead += OnDead;
        }
    }

    void LateUpdate()
    {
        // If enemy is revived (checkpoint reset), allow awarding again on next death.
        if (stats == null) return;
        bool isDead = stats.IsDead;
        if (lastDead && !isDead)
            awardedThisLife = false;
        lastDead = isDead;
    }

    void OnDisable()
    {
        if (stats != null)
            stats.OnDead -= OnDead;
    }

    void OnDead()
    {
        if (awardedThisLife) return;
        awardedThisLife = true;

        int amount = Mathf.Max(0, experienceValue);

        // Fallback for legacy prefabs not yet migrated.
        if (amount <= 0 && xpValue != null)
            amount = Mathf.Max(0, xpValue.experienceValue);

        if (amount <= 0) return;

        var pe = PlayerExperience.Instance;
        if (pe == null)
            pe = FindObjectOfType<PlayerExperience>();

        if (pe != null)
            pe.AddXP(amount);
    }

    void OnValidate()
    {
        if (experienceValue < 0) experienceValue = 0;
    }
}
