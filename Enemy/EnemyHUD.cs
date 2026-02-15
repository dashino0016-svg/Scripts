using UnityEngine;
using UnityEngine.UI;

public class EnemyHUD : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] LockOnSystem lockOn;

    [Header("Image Bars (optional)")]
    [SerializeField] UIFillBar hpBar;
    [SerializeField] UIFillBar staminaBar;
    [SerializeField] CanvasGroup canvasGroup;

    [Header("Runtime Build (optional)")]
    [Tooltip("If bars are not assigned, build a minimal HUD by code at runtime.")]
    [SerializeField] bool buildRuntimeUIIfMissing = true;

    [SerializeField] int runtimeSortingOrder = 60;
    [SerializeField] Vector2 referenceResolution = new Vector2(1920, 1080);

    // Default: a bit below the very top.
    [SerializeField] RuntimeUIBars.BarLayout hpLayout = RuntimeUIBars.BarLayout.TopCenter(new Vector2(0f, -40f), new Vector2(320f, 10f));
    [SerializeField] RuntimeUIBars.BarLayout staminaLayout = RuntimeUIBars.BarLayout.TopCenter(new Vector2(0f, -56f), new Vector2(320f, 10f));

    [Header("Runtime Style")]
    [SerializeField] Color hpBg = new Color(0f, 0f, 0f, 0.35f);
    [SerializeField] Color staminaBg = new Color(0f, 0f, 0f, 0.35f);

    [SerializeField] Color hpFill = new Color(1f, 0.2f, 0.2f, 1f);
    [SerializeField] Color staminaFill = new Color(0.2f, 0.75f, 1f, 1f);

    [SerializeField, Min(0.01f)] float smoothSpeed = 18f;
    [SerializeField] bool useUnscaledTime = true;

    [Tooltip("If true, add StaminaBarColorEffect to the stamina fill (lock-on target mode) when building runtime UI.")]
    [SerializeField] bool addStaminaColorEffect = true;

    RectTransform runtimeCanvas;

    void Awake()
    {
        if (lockOn == null)
            lockOn = FindObjectOfType<LockOnSystem>();

        if (buildRuntimeUIIfMissing && (hpBar == null || staminaBar == null || canvasGroup == null))
            BuildRuntimeUI();

        if (canvasGroup == null)
            canvasGroup = GetComponentInChildren<CanvasGroup>();

        SetVisible(false);
    }

    void Update()
    {
        if (lockOn == null)
        {
            SetVisible(false);
            return;
        }

        CombatStats target = lockOn.CurrentTargetStats;

        if (target == null || target.IsDead)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        if (hpBar != null)
            hpBar.Set01(Safe01(target.CurrentHP, target.maxHP));

        if (staminaBar != null)
            staminaBar.Set01(Safe01(target.CurrentStamina, target.maxStamina));
    }

    void BuildRuntimeUI()
    {
        runtimeCanvas = RuntimeUIBars.CreateOverlayCanvas(transform, "EnemyHUD_RuntimeCanvas", runtimeSortingOrder, referenceResolution);

        // CanvasGroup to fade/disable when no target.
        canvasGroup = runtimeCanvas.gameObject.AddComponent<CanvasGroup>();

        if (hpBar == null)
            hpBar = RuntimeUIBars.CreateMaskedBar("EnemyHP", runtimeCanvas, hpLayout, hpBg, hpFill, smoothSpeed, useUnscaledTime, out _, out _);

        if (staminaBar == null)
        {
            staminaBar = RuntimeUIBars.CreateMaskedBar("EnemyStamina", runtimeCanvas, staminaLayout, staminaBg, staminaFill, smoothSpeed, useUnscaledTime, out Image staminaFillImg, out _);

            if (addStaminaColorEffect && staminaFillImg != null)
            {
                var eff = staminaFillImg.gameObject.AddComponent<StaminaBarColorEffect>();
                eff.ConfigureForLockOn(lockOn);
            }
        }

        // Avoid blocking gameplay input.
        var raycaster = runtimeCanvas.GetComponent<GraphicRaycaster>();
        if (raycaster != null) raycaster.enabled = false;
    }

    void SetVisible(bool visible)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    float Safe01(float cur, float max)
    {
        if (max <= 0.0001f) return 0f;
        return Mathf.Clamp01(cur / max);
    }
}
