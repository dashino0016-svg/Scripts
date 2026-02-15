using UnityEngine;

/// <summary>
/// Per-enemy XP value granted to player on death.
/// Attach on enemy root (same object as CombatStats / EnemyController).
/// </summary>
public class EnemyExperienceValue : MonoBehaviour
{
    [Tooltip("该敌人死亡时给予玩家的经验值。")]
    public int experienceValue = 30;

    void OnValidate()
    {
        if (experienceValue < 0) experienceValue = 0;
    }
}
