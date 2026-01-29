using UnityEngine;

public class PlayerHUD : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] CombatStats stats;

    [Header("Image Bars")]
    [SerializeField] UIFillBar hpBar;
    [SerializeField] UIFillBar staminaBar;
    [SerializeField] UIFillBar specialBar;

    void Update()
    {
        if (stats == null) return;

        if (hpBar != null)
            hpBar.Set01(Safe01(stats.CurrentHP, stats.maxHP));

        if (staminaBar != null)
            staminaBar.Set01(Safe01(stats.CurrentStamina, stats.maxStamina));

        if (specialBar != null)
            specialBar.Set01(Safe01(stats.CurrentSpecial, stats.maxSpecial));
    }

    float Safe01(float cur, float max)
    {
        if (max <= 0.0001f) return 0f;
        return Mathf.Clamp01(cur / max);
    }
}
