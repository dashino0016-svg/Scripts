using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player HUD (HP/Stamina/Special).
/// - Can be wired to existing UIFillBar references in scene
/// - Or can build a minimal HUD by code at runtime (no sprite assets required)
/// - Supports external show/hide (for example: hide while Upgrade UI is open)
/// </summary>
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

    [Header("Visibility")]
    [Tooltip("Optional: if set, visibility changes will be applied to this root instead of the runtime canvas.")]
    [SerializeField] GameObject visibilityRootOverride;

    [Tooltip("Optional: if set, will fade/disable HUD using this CanvasGroup. If empty, it will be created on the visibility root when possible.")]
    [SerializeField] CanvasGroup visibilityGroup;

    RectTransform runtimeCanvas;
    bool visibilityInitialized;

    void Awake()
    {
        if (stats == null)
            stats = GetComponent<CombatStats>();

        if (buildRuntimeUIIfMissing && (hpBar == null || staminaBar == null || specialBar == null))
            BuildRuntimeUI();

        EnsureVisibilityGroup();

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

    /// <summary>
    /// External API: show/hide the player HUD (used to prevent overlap with Upgrade UI).
    /// Uses CanvasGroup when possible.
    /// </summary>
    public void SetVisible(bool visible)
    {
        EnsureVisibilityGroup();

        if (visibilityGroup != null)
        {
            visibilityGroup.alpha = visible ? 1f : 0f;
            visibilityGroup.interactable = false;
            visibilityGroup.blocksRaycasts = false;
            return;
        }

        // Fallback: toggle runtime canvas if exists, otherwise toggle individual bars.
        if (runtimeCanvas != null)
        {
            runtimeCanvas.gameObject.SetActive(visible);
            return;
        }

        if (hpBar != null) hpBar.gameObject.SetActive(visible);
        if (staminaBar != null) staminaBar.gameObject.SetActive(visible);
        if (specialBar != null) specialBar.gameObject.SetActive(visible);
    }

    void EnsureVisibilityGroup()
    {
        if (visibilityInitialized) return;
        visibilityInitialized = true;

        if (visibilityGroup != null)
            return;

        GameObject rootGO = null;
        if (visibilityRootOverride != null)
            rootGO = visibilityRootOverride;
        else if (runtimeCanvas != null)
            rootGO = runtimeCanvas.gameObject;

        if (rootGO != null)
            visibilityGroup = rootGO.GetComponent<CanvasGroup>() ?? rootGO.AddComponent<CanvasGroup>();
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

        // Default: visibility controlled on runtime canvas.
        if (visibilityRootOverride == null)
            visibilityRootOverride = runtimeCanvas.gameObject;
    }

    float Safe01(float cur, float max)
    {
        if (max <= 0.0001f) return 0f;
        return Mathf.Clamp01(cur / max);
    }
}
