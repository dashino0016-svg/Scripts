using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimal runtime UI builder helpers (uGUI).
/// Purpose: allow "pure code" construction of simple HUD bars without requiring sprite assets.
/// </summary>
public static class RuntimeUIBars
{
    [Serializable]
    public struct BarLayout
    {
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 pivot;
        public Vector2 anchoredPosition;
        public Vector2 size;

        public static BarLayout BottomLeft(Vector2 pos, Vector2 size)
        {
            return new BarLayout
            {
                anchorMin = new Vector2(0f, 0f),
                anchorMax = new Vector2(0f, 0f),
                pivot = new Vector2(0f, 0f),
                anchoredPosition = pos,
                size = size
            };
        }

        public static BarLayout TopCenter(Vector2 pos, Vector2 size)
        {
            return new BarLayout
            {
                anchorMin = new Vector2(0.5f, 1f),
                anchorMax = new Vector2(0.5f, 1f),
                pivot = new Vector2(0.5f, 1f),
                anchoredPosition = pos,
                size = size
            };
        }
    }

    static Sprite capsuleSprite;

    public static Sprite CapsuleSprite
    {
        get
        {
            if (capsuleSprite == null)
                capsuleSprite = BuildCapsuleSprite(512, 64);
            return capsuleSprite;
        }
    }

    /// <summary>Create a basic overlay Canvas+Scaler under host.</summary>
    public static RectTransform CreateOverlayCanvas(Transform host, string name, int sortingOrder, Vector2 referenceResolution)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(host, false);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.matchWidthOrHeight = 0.5f;

        return go.GetComponent<RectTransform>();
    }

    /// <summary>
    /// Creates a masked capsule bar.
    /// Hierarchy: Root -> BG(Image+Mask) -> Fill(Image+UIFillBar)
    /// Returns UIFillBar (on Fill) and fill Image.
    /// </summary>
    public static UIFillBar CreateMaskedBar(
        string name,
        Transform parent,
        BarLayout layout,
        Color bgColor,
        Color fillColor,
        float smoothSpeed,
        bool useUnscaledTime,
        out Image fillImage,
        out RectTransform rootRect)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        rootRect = root.GetComponent<RectTransform>();
        ApplyLayout(rootRect, layout);

        var bgGO = new GameObject("BG", typeof(RectTransform), typeof(Image), typeof(Mask));
        bgGO.transform.SetParent(root.transform, false);
        var bgRt = bgGO.GetComponent<RectTransform>();
        Stretch(bgRt);

        var bgImg = bgGO.GetComponent<Image>();
        bgImg.sprite = CapsuleSprite;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = bgColor;

        var mask = bgGO.GetComponent<Mask>();
        mask.showMaskGraphic = true;

        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image), typeof(UIFillBar));
        fillGO.transform.SetParent(bgGO.transform, false);

        var fillRt = fillGO.GetComponent<RectTransform>();
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.anchoredPosition = Vector2.zero;
        fillRt.sizeDelta = new Vector2(layout.size.x, 0f);

        fillImage = fillGO.GetComponent<Image>();
        fillImage.sprite = CapsuleSprite;
        fillImage.type = Image.Type.Sliced;
        fillImage.color = fillColor;

        var bar = fillGO.GetComponent<UIFillBar>();
        bar.SmoothSpeed = smoothSpeed;
        bar.UseUnscaledTime = useUnscaledTime;
        bar.RecalculateFullWidth();
        bar.SetImmediate01(1f);

        return bar;
    }

    public static void ApplyLayout(RectTransform rt, BarLayout layout)
    {
        rt.anchorMin = layout.anchorMin;
        rt.anchorMax = layout.anchorMax;
        rt.pivot = layout.pivot;
        rt.anchoredPosition = layout.anchoredPosition;
        rt.sizeDelta = layout.size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static Sprite BuildCapsuleSprite(int width, int height)
    {
        width = Mathf.Max(8, width);
        height = Mathf.Max(8, height);

        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.name = "RuntimeCapsule";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        var pixels = new Color32[width * height];
        int r = height / 2;
        int left = r;
        int right = width - r - 1;
        int cy = r;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool inside;
                if (x < left)
                {
                    int dx = x - left;
                    int dy = y - cy;
                    inside = (dx * dx + dy * dy) <= r * r;
                }
                else if (x > right)
                {
                    int dx = x - right;
                    int dy = y - cy;
                    inside = (dx * dx + dy * dy) <= r * r;
                }
                else
                {
                    inside = true;
                }

                pixels[y * width + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        // Border for sliced mode: keep round ends.
        float border = r;
        var sprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        sprite.name = "RuntimeCapsuleSprite";
        return sprite;
    }
}
