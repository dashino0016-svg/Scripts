using UnityEngine;

public class EnemyHUD : MonoBehaviour
{
    [Header("LockOn (on Player)")]
    [SerializeField] LockOnSystem lockOn;

    [Header("Image Bars")]
    [SerializeField] UIFillBar hpBar;
    [SerializeField] UIFillBar staminaBar;

    [Header("Hide When No Target")]
    [SerializeField] CanvasGroup canvasGroup;

    void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    void Update()
    {
        CombatStats enemyStats = lockOn != null ? lockOn.CurrentTargetStats : null;
        bool hasTarget = enemyStats != null && enemyStats.CurrentHP > 0f;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = hasTarget ? 1f : 0f;
            canvasGroup.blocksRaycasts = hasTarget;
            canvasGroup.interactable = hasTarget;
        }

        if (!hasTarget)
        {
            if (hpBar != null) hpBar.SetImmediate01(0f);
            if (staminaBar != null) staminaBar.SetImmediate01(0f);
            return;
        }

        if (hpBar != null)
            hpBar.Set01(Safe01(enemyStats.CurrentHP, enemyStats.maxHP));

        if (staminaBar != null)
            staminaBar.Set01(Safe01(enemyStats.CurrentStamina, enemyStats.maxStamina));
    }

    float Safe01(float cur, float max)
    {
        if (max <= 0.0001f) return 0f;
        return Mathf.Clamp01(cur / max);
    }
}
