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
    [SerializeField] EnemyExperienceValue xpValue;

    bool awardedThisLife;
    bool lastDead;

    void Awake()
    {
        if (stats == null) stats = GetComponent<CombatStats>();
        if (xpValue == null) xpValue = GetComponent<EnemyExperienceValue>();
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

        int amount = (xpValue != null) ? Mathf.Max(0, xpValue.experienceValue) : 0;
        if (amount <= 0) return;

        var pe = PlayerExperience.Instance;
        if (pe == null)
            pe = FindObjectOfType<PlayerExperience>();

        if (pe != null)
            pe.AddXP(amount);
    }
}
