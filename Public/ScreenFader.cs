using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ScreenFader : MonoBehaviour
{
    public static ScreenFader Instance { get; private set; }

    [Header("Canvas")]
    [Tooltip("Canvas sortingOrder. Make sure it's higher than any other UI.")]
    [SerializeField] int sortingOrder = 10000;

    [Tooltip("Keep this fader across scenes.")]
    [SerializeField] bool dontDestroyOnLoad = true;

    [Header("Fade")]
    [SerializeField, Range(0f, 2f)] float defaultFadeDuration = 0.35f;

    [Tooltip("If true, use unscaled time so fade still works while game is paused.")]
    [SerializeField] bool useUnscaledTime = true;

    [Tooltip("If true, block UI clicks during fade (when alpha > 0).")]
    [SerializeField] bool blockInputDuringFade = true;

    [Tooltip("Easing curve for fade. If empty, linear.")]
    [SerializeField] AnimationCurve fadeCurve;

    Canvas _canvas;
    CanvasScaler _scaler;
    GraphicRaycaster _raycaster;

    Image _fadeImage;
    CanvasGroup _fadeGroup;

    Coroutine _running;
    float _alpha;

    public bool IsFading { get; private set; }
    public float Alpha => _alpha;

    void Awake()
    {
        // Singleton (optional but convenient)
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);

        EnsureRuntimeUI();
        SetAlphaInstant(0f);
    }

    void EnsureRuntimeUI()
    {
        // Canvas on this GO
        _canvas = GetComponent<Canvas>();
        if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = sortingOrder;

        _scaler = GetComponent<CanvasScaler>();
        if (_scaler == null) _scaler = gameObject.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.referenceResolution = new Vector2(1920, 1080);
        _scaler.matchWidthOrHeight = 0.5f;

        _raycaster = GetComponent<GraphicRaycaster>();
        if (_raycaster == null) _raycaster = gameObject.AddComponent<GraphicRaycaster>();

        // Child "Fade"
        Transform child = transform.Find("Fade");
        if (child == null)
        {
            var go = new GameObject("Fade", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            child = go.transform;
        }

        var rt = child.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _fadeImage = child.GetComponent<Image>();
        if (_fadeImage == null) _fadeImage = child.gameObject.AddComponent<Image>();
        _fadeImage.color = Color.black;
        _fadeImage.raycastTarget = true;

        _fadeGroup = child.GetComponent<CanvasGroup>();
        if (_fadeGroup == null) _fadeGroup = child.gameObject.AddComponent<CanvasGroup>();
        _fadeGroup.interactable = false;
    }

    // ===== Public API =====

    public Coroutine FadeOut(float? duration = null, Action onComplete = null)
        => FadeTo(1f, duration ?? defaultFadeDuration, onComplete);

    public Coroutine FadeIn(float? duration = null, Action onComplete = null)
        => FadeTo(0f, duration ?? defaultFadeDuration, onComplete);

    /// <summary>
    /// Fade to black, call midAction while fully black, then fade in.
    /// Useful for teleport / switching UI / respawn.
    /// </summary>
    public Coroutine FadeOutIn(Action midAction, float? outDuration = null, float? inDuration = null, float blackHoldSeconds = 0f, Action onComplete = null)
    {
        float od = outDuration ?? defaultFadeDuration;
        float id = inDuration ?? defaultFadeDuration;

        StopRunning();
        _running = StartCoroutine(CoFadeOutIn(midAction, od, id, blackHoldSeconds, onComplete));
        return _running;
    }

    public void SetAlphaInstant(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        _alpha = alpha;
        _fadeGroup.alpha = alpha;
        UpdateBlocking(alpha);
        IsFading = false;
    }

    // ===== Core =====

    public Coroutine FadeTo(float targetAlpha, float duration, Action onComplete = null)
    {
        targetAlpha = Mathf.Clamp01(targetAlpha);

        StopRunning();
        _running = StartCoroutine(CoFadeTo(targetAlpha, duration, onComplete));
        return _running;
    }

    void StopRunning()
    {
        if (_running != null)
        {
            StopCoroutine(_running);
            _running = null;
        }
        IsFading = false;
    }

    IEnumerator CoFadeOutIn(Action midAction, float outDuration, float inDuration, float hold, Action onComplete)
    {
        yield return CoFadeTo(1f, outDuration, null);

        midAction?.Invoke();

        if (hold > 0f)
        {
            float t = 0f;
            while (t < hold)
            {
                t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                yield return null;
            }
        }

        yield return CoFadeTo(0f, inDuration, null);

        onComplete?.Invoke();
        _running = null;
    }

    IEnumerator CoFadeTo(float targetAlpha, float duration, Action onComplete)
    {
        IsFading = true;

        float start = _alpha;
        float end = targetAlpha;

        if (duration <= 0f)
        {
            SetAlphaInstant(end);
            onComplete?.Invoke();
            _running = null;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float n = Mathf.Clamp01(t / duration);

            if (fadeCurve != null && fadeCurve.length > 0)
                n = Mathf.Clamp01(fadeCurve.Evaluate(n));

            _alpha = Mathf.Lerp(start, end, n);
            _fadeGroup.alpha = _alpha;
            UpdateBlocking(_alpha);

            yield return null;
        }

        _alpha = end;
        _fadeGroup.alpha = _alpha;
        UpdateBlocking(_alpha);

        IsFading = false;
        onComplete?.Invoke();
        _running = null;
    }

    void UpdateBlocking(float alpha)
    {
        if (!blockInputDuringFade)
        {
            _fadeGroup.blocksRaycasts = false;
            return;
        }

        // alpha > tiny threshold => block
        _fadeGroup.blocksRaycasts = alpha > 0.001f;
    }
}
