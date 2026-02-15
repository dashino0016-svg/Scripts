using UnityEngine;

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

        // 默认满宽=当前宽，避免起始为0导致看不见
        current01 = Mathf.Clamp01(fullWidth <= 0f ? 0f : (fillRect.sizeDelta.x / fullWidth));
        target01 = current01;
        SetWidth(current01);
    }

    void Update()
    {
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (Mathf.Approximately(current01, target01)) return;

        current01 = Mathf.Lerp(current01, target01, dt * smoothSpeed);

        // 逼近收敛
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

    /// <summary>设置目标值（会动画过去）</summary>
    public void SetTarget01(float value01)
    {
        target01 = Mathf.Clamp01(value01);
    }

    /// <summary>立即设置（不动画）</summary>
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
