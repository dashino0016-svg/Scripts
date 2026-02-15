using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ExperienceUI : MonoBehaviour
{
    [Header("Bind")]
    [SerializeField] PlayerExperience experience;
    [SerializeField] bool autoFindExperience = true;

    [Header("Layout")]
    [SerializeField] Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] Vector2 anchoredPosition = new Vector2(0f, -40f);
    [SerializeField] float barWidth = 420f;
    [SerializeField] float barHeight = 12f;
    [SerializeField] float numberWidth = 70f;
    [SerializeField] float spacing = 14f;

    [Header("Style")]
    [SerializeField] Color barBackColor = new Color(1f, 1f, 1f, 0.10f);
    [SerializeField] Color barFillColor = new Color(0.95f, 0.75f, 0.55f, 0.95f);
    [SerializeField] Color pointsTextColor = new Color(1f, 1f, 1f, 0.95f);

    [Header("Show/Hide")]
    [SerializeField] float fadeInTime = 0.16f;
    [SerializeField] float holdTime = 1.6f;
    [SerializeField] float fadeOutTime = 0.30f;
    [SerializeField] bool useUnscaledTime = true;

    [Header("Fill Smoothing")]
    [SerializeField] float fillSmoothSpeed = 14f;

    CanvasGroup group;
    Text pointsText;
    UIFillBar fillBar;
    bool built;

    Sprite capsuleSprite;
    Coroutine showHideCo;
    Coroutine fillAnimCo;

    void OnEnable()
    {
        if (autoFindExperience && experience == null)
        {
            experience = PlayerExperience.Instance;
            if (experience == null)
                experience = FindObjectOfType<PlayerExperience>();
        }

        EnsureBuilt();
        Bind();

        SetAlpha(0f);
        RefreshImmediate();
    }

    void OnDisable()
    {
        Unbind();
    }

    void Bind()
    {
        if (experience == null) return;

        experience.OnXPGainedDetailed -= OnXPGainedDetailed;
        experience.OnXPGainedDetailed += OnXPGainedDetailed;

        experience.OnXPStateChanged -= OnXPStateChanged;
        experience.OnXPStateChanged += OnXPStateChanged;

        experience.OnXPResetByDeath -= OnXPResetByDeath;
        experience.OnXPResetByDeath += OnXPResetByDeath;
    }

    void Unbind()
    {
        if (experience == null) return;
        experience.OnXPGainedDetailed -= OnXPGainedDetailed;
        experience.OnXPStateChanged -= OnXPStateChanged;
        experience.OnXPResetByDeath -= OnXPResetByDeath;
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;

        capsuleSprite = BuildCapsuleSprite(512, 64);

        var canvasGO = new GameObject("ExperienceUI_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var root = canvasGO.GetComponent<RectTransform>();
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = anchoredPosition;
        root.sizeDelta = new Vector2(numberWidth + spacing + barWidth, 40f);

        group = canvasGO.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        // Points text
        var pointsGO = new GameObject("XPPoints", typeof(RectTransform), typeof(Text));
        var prt = pointsGO.GetComponent<RectTransform>();
        prt.SetParent(root, false);
        prt.anchorMin = new Vector2(0f, 0.5f);
        prt.anchorMax = new Vector2(0f, 0.5f);
        prt.pivot = new Vector2(0f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(numberWidth, 28f);

        pointsText = pointsGO.GetComponent<Text>();
        pointsText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        pointsText.text = "0";
        pointsText.alignment = TextAnchor.MiddleLeft;
        pointsText.fontSize = 22;
        pointsText.color = pointsTextColor;
        pointsText.horizontalOverflow = HorizontalWrapMode.Overflow;
        pointsText.raycastTarget = false;

        // Bar root
        var barRootGO = new GameObject("XPBar", typeof(RectTransform));
        var brt = barRootGO.GetComponent<RectTransform>();
        brt.SetParent(root, false);
        brt.anchorMin = new Vector2(0f, 0.5f);
        brt.anchorMax = new Vector2(0f, 0.5f);
        brt.pivot = new Vector2(0f, 0.5f);
        brt.anchoredPosition = new Vector2(numberWidth + spacing, 0f);
        brt.sizeDelta = new Vector2(barWidth, barHeight);

        // Mask (capsule)
        var maskGO = new GameObject("Mask", typeof(RectTransform), typeof(Image), typeof(Mask));
        var mrt = maskGO.GetComponent<RectTransform>();
        mrt.SetParent(brt, false);
        mrt.anchorMin = Vector2.zero;
        mrt.anchorMax = Vector2.one;
        mrt.offsetMin = Vector2.zero;
        mrt.offsetMax = Vector2.zero;

        var maskImg = maskGO.GetComponent<Image>();
        maskImg.sprite = capsuleSprite;
        maskImg.type = Image.Type.Sliced;
        maskImg.color = barBackColor;

        var mask = maskGO.GetComponent<Mask>();
        mask.showMaskGraphic = true;

        // Fill
        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image), typeof(UIFillBar));
        var frt = fillGO.GetComponent<RectTransform>();
        frt.SetParent(mrt, false);
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(0f, 1f);
        frt.pivot = new Vector2(0f, 0.5f);
        frt.anchoredPosition = Vector2.zero;
        frt.sizeDelta = new Vector2(barWidth, 0f);

        var fillImg = fillGO.GetComponent<Image>();
        fillImg.sprite = capsuleSprite;
        fillImg.type = Image.Type.Sliced;
        fillImg.color = barFillColor;
        fillImg.raycastTarget = false;

        fillBar = fillGO.GetComponent<UIFillBar>();
        fillBar.RecalculateFullWidth();
        fillBar.SmoothSpeed = fillSmoothSpeed;
        fillBar.UseUnscaledTime = useUnscaledTime;
    }

    void RefreshImmediate()
    {
        if (experience == null) return;
        if (pointsText != null) pointsText.text = experience.XPPoints.ToString();
        if (fillBar != null)
        {
            float v = (experience.MaxXP <= 0) ? 0f : (float)experience.CurrentXP / experience.MaxXP;
            fillBar.SetImmediate01(v);
        }
    }

    // ✅ 用详细事件做“真正动画”，并支持溢出升级表现
    void OnXPGainedDetailed(int gained, int oldXP, int newXP, int maxXP, int oldPoints, int newPoints, int levelUps)
    {
        if (pointsText != null) pointsText.text = newPoints.ToString();

        if (fillAnimCo != null) StopCoroutine(fillAnimCo);
        fillAnimCo = StartCoroutine(CoAnimateGain(oldXP, newXP, maxXP, levelUps));

        if (showHideCo != null) StopCoroutine(showHideCo);
        showHideCo = StartCoroutine(ShowThenHide());
    }

    // ✅ 状态同步不要再 SetImmediate01，否则会把动画打断
    void OnXPStateChanged(int curXP, int maxXP, int points)
    {
        if (pointsText != null) pointsText.text = points.ToString();
        if (fillBar != null)
        {
            float v = (maxXP <= 0) ? 0f : Mathf.Clamp01((float)curXP / maxXP);
            fillBar.SetTarget01(v);
        }
    }

    void OnXPResetByDeath()
    {
        if (showHideCo != null) StopCoroutine(showHideCo);
        if (fillAnimCo != null) StopCoroutine(fillAnimCo);
        showHideCo = null;
        fillAnimCo = null;

        if (fillBar != null) fillBar.SetImmediate01(0f);
        SetAlpha(0f);
    }

    IEnumerator CoAnimateGain(int oldXP, int newXP, int maxXP, int levelUps)
    {
        if (fillBar == null || maxXP <= 0)
            yield break;

        float old01 = Mathf.Clamp01((float)oldXP / maxXP);
        float new01 = Mathf.Clamp01((float)newXP / maxXP);

        // 先确保起点一致（避免中途状态导致跳动）
        fillBar.SetImmediate01(old01);

        if (levelUps <= 0)
        {
            fillBar.SetTarget01(new01);
            yield break;
        }

        // 溢出：做 “填满->归零” 多次，再填到余量
        for (int i = 0; i < levelUps; i++)
        {
            fillBar.SetTarget01(1f);
            yield return WaitUntilNear(1f, 0.004f);

            // 瞬间归零（符合你描述：满后清0再加剩余）
            fillBar.SetImmediate01(0f);
            yield return null;
        }

        fillBar.SetTarget01(new01);
    }

    IEnumerator WaitUntilNear(float target, float eps)
    {
        // 等到 fillBar.Current01 接近 target
        while (fillBar != null && Mathf.Abs(fillBar.Current01 - target) > eps)
            yield return null;
    }

    IEnumerator ShowThenHide()
    {
        yield return FadeTo(1f, fadeInTime);
        yield return Wait(holdTime);
        yield return FadeTo(0f, fadeOutTime);
    }

    IEnumerator FadeTo(float target, float time)
    {
        if (group == null) yield break;

        if (time <= 0.0001f)
        {
            SetAlpha(target);
            yield break;
        }

        float start = group.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Dt() / time;
            SetAlpha(Mathf.Lerp(start, target, Mathf.Clamp01(t)));
            yield return null;
        }
        SetAlpha(target);
    }

    IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Dt();
            yield return null;
        }
    }

    float Dt() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    void SetAlpha(float a)
    {
        if (group != null)
            group.alpha = Mathf.Clamp01(a);
    }

    static Sprite BuildCapsuleSprite(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        var pixels = new Color32[w * h];

        float radius = h * 0.5f;
        Vector2 left = new Vector2(radius, radius);
        Vector2 right = new Vector2(w - radius, radius);

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);

            float d;
            if (p.x < left.x) d = Vector2.Distance(p, left) - radius;
            else if (p.x > right.x) d = Vector2.Distance(p, right) - radius;
            else d = Mathf.Abs(p.y - radius) - radius;

            float aa = 1.2f;
            float a = Mathf.Clamp01(1f - (d / aa));
            byte A = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);

            pixels[y * w + x] = new Color32(255, 255, 255, A);
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }
}
