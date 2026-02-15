using UnityEngine;

/// <summary>
/// Simple 0~1 UI fill controller.
/// - Works by resizing a RectTransform's width (sizeDelta.x)
/// - Supports smooth lerp
///
/// Typical hierarchy:
///   BG (Image + Mask)
///     Fill (Image + UIFillBar)
///
/// UIFillBar is usually placed on the Fill object.
/// </summary>
public class UIFillBar : MonoBehaviour
{
    [SerializeField] RectTransform fillRect;
    [SerializeField, Min(0.01f)] float smoothSpeed = 12f;
    [SerializeField] bool useUnscaledTime = true;

    float fullWidth;
    float current01 = 0f;
    float target01 = 0f;

    public float SmoothSpeed
    {
        get => smoothSpeed;
        set => smoothSpeed = Mathf.Max(0.01f, value);
    }

    public bool UseUnscaledTime
    {
        get => useUnscaledTime;
        set => useUnscaledTime = value;
    }

    public float Current01 => current01;
    public float Target01 => target01;

    void Awake()
    {
        if (fillRect == null)
            fillRect = GetComponent<RectTransform>();

        RecalculateFullWidth();

        // Initialize to current width, so it won't start at 0 by mistake.
        current01 = Mathf.Clamp01(fullWidth <= 0f ? 0f : (fillRect.sizeDelta.x / fullWidth));
        target01 = current01;
        SetWidth(current01);
    }

    void Update()
    {
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (Mathf.Approximately(current01, target01)) return;

        current01 = Mathf.Lerp(current01, target01, dt * smoothSpeed);

        // Snap when close.
        if (Mathf.Abs(current01 - target01) < 0.0015f)
            current01 = target01;

        SetWidth(current01);
    }

    public void RecalculateFullWidth()
    {
        if (fillRect == null)
            fillRect = GetComponent<RectTransform>();

        fullWidth = fillRect.sizeDelta.x;
        if (fullWidth < 0.0001f) fullWidth = 1f;
    }

    /// <summary>Set target value (smooth).</summary>
    public void SetTarget01(float value01)
    {
        target01 = Mathf.Clamp01(value01);
    }

    /// <summary>Backwards-compatible API used by existing HUD scripts (smooth).</summary>
    public void Set01(float value01)
    {
        SetTarget01(value01);
    }

    /// <summary>Set immediately (no smooth).</summary>
    public void SetImmediate01(float value01)
    {
        current01 = target01 = Mathf.Clamp01(value01);
        SetWidth(current01);
    }

    void SetWidth(float value01)
    {
        if (fillRect == null) return;
        var size = fillRect.sizeDelta;
        size.x = fullWidth * Mathf.Clamp01(value01);
        fillRect.sizeDelta = size;
    }
}
