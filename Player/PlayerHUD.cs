using UnityEngine;
using UnityEngine.UI;

public class PlayerHUD : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] CombatStats stats;

    [Header("Image Bars (optional)")]
    [SerializeField] UIFillBar hpBar;
    [SerializeField] UIFillBar staminaBar;
    [SerializeField] UIFillBar specialBar;

    [Header("Runtime Build (optional)")]
    [Tooltip("If bars are not assigned, build a minimal HUD by code at runtime.")]
    [SerializeField] bool buildRuntimeUIIfMissing = true;

    [SerializeField] int runtimeSortingOrder = 50;
    [SerializeField] Vector2 referenceResolution = new Vector2(1920, 1080);

    [SerializeField] RuntimeUIBars.BarLayout hpLayout = RuntimeUIBars.BarLayout.BottomLeft(new Vector2(40f, 36f), new Vector2(260f, 10f));
    [SerializeField] RuntimeUIBars.BarLayout staminaLayout = RuntimeUIBars.BarLayout.BottomLeft(new Vector2(40f, 20f), new Vector2(260f, 10f));
    [SerializeField] RuntimeUIBars.BarLayout specialLayout = RuntimeUIBars.BarLayout.BottomLeft(new Vector2(40f, 4f), new Vector2(260f, 10f));

    [Header("Runtime Style")]
    [SerializeField] Color hpBg = new Color(0f, 0f, 0f, 0.35f);
    [SerializeField] Color staminaBg = new Color(0f, 0f, 0f, 0.35f);
    [SerializeField] Color specialBg = new Color(0f, 0f, 0f, 0.35f);

    [SerializeField] Color hpFill = new Color(1f, 0.2f, 0.2f, 1f);
    [SerializeField] Color staminaFill = new Color(0.2f, 0.75f, 1f, 1f);
    [SerializeField] Color specialFill = new Color(1f, 0.85f, 0.2f, 1f);

    [SerializeField, Min(0.01f)] float smoothSpeed = 18f;
    [SerializeField] bool useUnscaledTime = true;

    [Tooltip("If true, add StaminaBarColorEffect to the stamina fill when building runtime UI.")]
    [SerializeField] bool addStaminaColorEffect = true;

    RectTransform runtimeCanvas;

    void Awake()
    {
        if (stats == null)
            stats = GetComponent<CombatStats>();

        if (buildRuntimeUIIfMissing && (hpBar == null || staminaBar == null || specialBar == null))
            BuildRuntimeUI();

        // Make sure we start from correct values (no one-frame full bars).
        RefreshImmediate();
    }

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

    void RefreshImmediate()
    {
        if (stats == null) return;

        if (hpBar != null)
            hpBar.SetImmediate01(Safe01(stats.CurrentHP, stats.maxHP));

        if (staminaBar != null)
            staminaBar.SetImmediate01(Safe01(stats.CurrentStamina, stats.maxStamina));

        if (specialBar != null)
            specialBar.SetImmediate01(Safe01(stats.CurrentSpecial, stats.maxSpecial));
    }

    void BuildRuntimeUI()
    {
        runtimeCanvas = RuntimeUIBars.CreateOverlayCanvas(transform, "PlayerHUD_RuntimeCanvas", runtimeSortingOrder, referenceResolution);

        if (hpBar == null)
            hpBar = RuntimeUIBars.CreateMaskedBar("PlayerHP", runtimeCanvas, hpLayout, hpBg, hpFill, smoothSpeed, useUnscaledTime, out _, out _);

        if (staminaBar == null)
        {
            staminaBar = RuntimeUIBars.CreateMaskedBar("PlayerStamina", runtimeCanvas, staminaLayout, staminaBg, staminaFill, smoothSpeed, useUnscaledTime, out Image staminaFillImg, out _);

            if (addStaminaColorEffect && staminaFillImg != null)
                staminaFillImg.gameObject.AddComponent<StaminaBarColorEffect>();
        }

        if (specialBar == null)
            specialBar = RuntimeUIBars.CreateMaskedBar("PlayerSpecial", runtimeCanvas, specialLayout, specialBg, specialFill, smoothSpeed, useUnscaledTime, out _, out _);

        // Avoid blocking gameplay input.
        var raycaster = runtimeCanvas.GetComponent<GraphicRaycaster>();
        if (raycaster != null) raycaster.enabled = false;
    }

    float Safe01(float cur, float max)
    {
        if (max <= 0.0001f) return 0f;
        return Mathf.Clamp01(cur / max);
    }
}
