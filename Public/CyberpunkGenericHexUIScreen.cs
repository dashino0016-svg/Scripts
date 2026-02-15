using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CyberpunkGenericHexUI
{
    [DisallowMultipleComponent]
    public class CyberpunkGenericHexUIScreen : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] Vector2 referenceResolution = new Vector2(1920, 1080);
        [SerializeField] Vector2 hexSize = new Vector2(270, 270);
        [SerializeField] float verticalRadius = 250f;
        [SerializeField] float horizontalRadius = 305f;
        [SerializeField] float diagonalY = 135f;

        [Header("Glow Tuning")]
        [Tooltip("Glow层比按钮本体外扩多少UI单位。越小越贴边、不模糊。")]
        [SerializeField, Range(0f, 20f)] float glowPadding = 4f;

        [Tooltip("Glow贴图的扩散范围（SDF falloff）。越小越收敛。")]
        [SerializeField, Range(0.05f, 0.5f)] float glowFalloff = 0.12f;

        [Tooltip("Glow衰减指数。越大越集中、边缘越硬。")]
        [SerializeField, Range(1.0f, 6.0f)] float glowPower = 2.8f;

        [Header("Style (cyberpunk-ish)")]
        [SerializeField] Color fillColor = new Color(0.06f, 0.07f, 0.08f, 0.92f);
        [SerializeField] Color outlineColor = new Color(1f, 0.25f, 0.25f, 0.88f);

        // 这里把默认光晕再压一点，避免泛糊
        [SerializeField] Color glowColor = new Color(1f, 0.25f, 0.25f, 0.14f);

        [SerializeField] Color hoverOutlineColor = new Color(0.25f, 0.95f, 0.95f, 0.95f);
        [SerializeField] Color hoverGlowColor = new Color(0.25f, 0.95f, 0.95f, 0.22f);

        [SerializeField] Color pressedOutlineColor = new Color(1f, 0.35f, 0.35f, 1f);
        [SerializeField] Color pressedGlowColor = new Color(1f, 0.35f, 0.35f, 0.28f);

        [Header("Generic Button Events")]
        public UnityEvent onButton1 = new UnityEvent();
        public UnityEvent onButton2 = new UnityEvent();
        public UnityEvent onButton3 = new UnityEvent();
        public UnityEvent onButton4 = new UnityEvent();
        public UnityEvent onButton5 = new UnityEvent();
        public UnityEvent onButton6 = new UnityEvent();

        [Header("Close (optional)")]
        public bool showCornerCloseButton = true;
        public UnityEvent onClose = new UnityEvent();

        // Generated sprites (runtime)
        Sprite bgSprite;
        Sprite hexFillSprite;
        Sprite hexOutlineSprite;
        Sprite hexGlowSprite;

        Canvas canvas;
        RectTransform root;
        bool built;

        void OnEnable()
        {
            EnsureEventSystem();
            EnsureRuntimeSprites();
            EnsureBuilt();
        }

        [ContextMenu("Rebuild UI")]
        public void Rebuild()
        {
            DestroyBuilt();
            built = false;

            // 重新生成贴图（因为 glowFalloff/glowPower 可能改了）
            bgSprite = null;
            hexFillSprite = null;
            hexOutlineSprite = null;
            hexGlowSprite = null;

            EnsureRuntimeSprites();
            EnsureBuilt();
        }

        void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var esGO = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(esGO);
        }

        void EnsureRuntimeSprites()
        {
            if (bgSprite != null && hexFillSprite != null && hexOutlineSprite != null && hexGlowSprite != null)
                return;

            bgSprite = BuildBackgroundSprite(1024, 576);

            hexFillSprite = BuildHexSprite(512, HexSpriteMode.Fill, glowFalloff, glowPower);
            hexOutlineSprite = BuildHexSprite(512, HexSpriteMode.Outline, glowFalloff, glowPower);
            hexGlowSprite = BuildHexSprite(512, HexSpriteMode.Glow, glowFalloff, glowPower);
        }

        void EnsureBuilt()
        {
            if (built) return;

            var canvasGO = new GameObject("CyberpunkGenericHexUI_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);

            canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            root = canvasGO.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            // BG (opaque)
            var bgRT = CreateUI("BG", root);
            var bgImg = bgRT.gameObject.AddComponent<Image>();
            bgImg.sprite = bgSprite;
            bgImg.preserveAspect = false;
            bgImg.color = Color.white;
            bgImg.raycastTarget = false;
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            // Center
            var center = CreateUI("Center", root);
            center.anchorMin = center.anchorMax = new Vector2(0.5f, 0.5f);
            center.anchoredPosition = Vector2.zero;

            CreateHexButton(center, "BUTTON 1", new Vector2(0f, verticalRadius), () => onButton1.Invoke());
            CreateHexButton(center, "BUTTON 2", new Vector2(-horizontalRadius, diagonalY), () => onButton2.Invoke());
            CreateHexButton(center, "BUTTON 3", new Vector2(horizontalRadius, diagonalY), () => onButton3.Invoke());
            CreateHexButton(center, "BUTTON 4", new Vector2(-horizontalRadius, -diagonalY), () => onButton4.Invoke());
            CreateHexButton(center, "BUTTON 5", new Vector2(horizontalRadius, -diagonalY), () => onButton5.Invoke());
            CreateHexButton(center, "BUTTON 6", new Vector2(0f, -verticalRadius), () => onButton6.Invoke());

            if (showCornerCloseButton)
                CreateCornerCloseButton(root);

            built = true;
        }

        void DestroyBuilt()
        {
            if (root != null)
            {
                DestroyImmediate(root.gameObject);
                root = null;
                canvas = null;
            }
        }

        RectTransform CreateUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            return rt;
        }

        void CreateCornerCloseButton(RectTransform parent)
        {
            var rt = CreateUI("CloseButton", parent);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-30f, 26f);
            rt.sizeDelta = new Vector2(180f, 52f);

            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0);

            var btn = rt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClose.Invoke());

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var lrt = labelGO.GetComponent<RectTransform>();
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

            var t = labelGO.GetComponent<Text>();
            t.text = "CLOSE";
            t.alignment = TextAnchor.MiddleRight;
            t.fontSize = 18;
            t.color = new Color(0.65f, 0.95f, 0.95f, 0.95f);

            var lineRT = CreateUI("Line", rt);
            lineRT.anchorMin = new Vector2(0f, 0f);
            lineRT.anchorMax = new Vector2(1f, 0f);
            lineRT.pivot = new Vector2(0.5f, 0f);
            lineRT.anchoredPosition = new Vector2(0f, 6f);
            lineRT.sizeDelta = new Vector2(0f, 3f);

            var lineImg = lineRT.gameObject.AddComponent<Image>();
            lineImg.color = new Color(1f, 0.25f, 0.25f, 0.95f);

            var hover = rt.gameObject.AddComponent<GenericSimpleHover>();
            hover.label = t;
            hover.line = lineImg;
        }

        void CreateHexButton(RectTransform parent, string title, Vector2 anchoredPos, UnityAction onClick)
        {
            var rt = CreateUI(title, parent);
            rt.sizeDelta = hexSize;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;

            // Glow (缩小外扩，减少模糊)
            var glowGO = new GameObject("Glow", typeof(RectTransform), typeof(Image));
            var glowRT = glowGO.GetComponent<RectTransform>();
            glowRT.SetParent(rt, false);
            glowRT.anchorMin = Vector2.zero; glowRT.anchorMax = Vector2.one;
            glowRT.offsetMin = new Vector2(-glowPadding, -glowPadding);
            glowRT.offsetMax = new Vector2(glowPadding, glowPadding);

            var glowImg = glowGO.GetComponent<Image>();
            glowImg.sprite = hexGlowSprite;
            glowImg.preserveAspect = true;
            glowImg.color = glowColor;
            glowImg.raycastTarget = false;

            // Fill (click target)
            var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var fillRT = fillGO.GetComponent<RectTransform>();
            fillRT.SetParent(rt, false);
            fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;

            var fillImg = fillGO.GetComponent<Image>();
            fillImg.sprite = hexFillSprite;
            fillImg.preserveAspect = true;
            fillImg.color = fillColor;

            var btn = fillGO.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(onClick);

            // Outline
            var outlineGO = new GameObject("Outline", typeof(RectTransform), typeof(Image));
            var outlineRT = outlineGO.GetComponent<RectTransform>();
            outlineRT.SetParent(rt, false);
            outlineRT.anchorMin = Vector2.zero; outlineRT.anchorMax = Vector2.one;
            outlineRT.offsetMin = Vector2.zero; outlineRT.offsetMax = Vector2.zero;

            var outlineImg = outlineGO.GetComponent<Image>();
            outlineImg.sprite = hexOutlineSprite;
            outlineImg.preserveAspect = true;
            outlineImg.color = outlineColor;
            outlineImg.raycastTarget = false;

            // Title
            var titleGO = new GameObject("Title", typeof(RectTransform), typeof(Text));
            var titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.SetParent(rt, false);
            titleRT.anchorMin = titleRT.anchorMax = new Vector2(0.5f, 0.5f);
            titleRT.anchoredPosition = new Vector2(0, 8);
            titleRT.sizeDelta = new Vector2(hexSize.x * 0.92f, 40);

            var tt = titleGO.GetComponent<Text>();
            tt.text = title;
            tt.alignment = TextAnchor.MiddleCenter;
            tt.fontSize = 18;
            tt.color = new Color(0.65f, 0.95f, 0.95f, 0.92f);
            tt.resizeTextForBestFit = true;
            tt.resizeTextMinSize = 12;
            tt.resizeTextMaxSize = 24;

            // Hover/Pressed visuals
            var vis = rt.gameObject.AddComponent<GenericHexButtonVisual>();
            vis.Target = rt;
            vis.Outline = outlineImg;
            vis.Glow = glowImg;
            vis.NormalOutline = outlineColor;
            vis.NormalGlow = glowColor;
            vis.HoverOutline = hoverOutlineColor;
            vis.HoverGlow = hoverGlowColor;
            vis.PressedOutline = pressedOutlineColor;
            vis.PressedGlow = pressedGlowColor;
        }

        enum HexSpriteMode { Fill, Outline, Glow }

        static Sprite BuildHexSprite(int size, HexSpriteMode mode, float glowW, float glowPower)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];

            float r = 0.78f;
            float aa = 0.015f;
            float outlineW = 0.028f;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;

                    float sd = SdHex(new Vector2(u, v), r);

                    float fill = 1f - Smoothstep(-aa, aa, sd);
                    float ring = 1f - Smoothstep(-aa, aa, Mathf.Abs(sd) - outlineW);

                    // glow（更收敛）
                    float glow = 1f - Smoothstep(outlineW, outlineW + glowW, Mathf.Abs(sd));
                    glow = Mathf.Pow(Mathf.Clamp01(glow), glowPower);

                    float a = 0f;
                    switch (mode)
                    {
                        case HexSpriteMode.Fill: a = fill; break;
                        case HexSpriteMode.Outline: a = ring; break;
                        case HexSpriteMode.Glow: a = glow; break;
                    }

                    byte A = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    pixels[y * size + x] = new Color32(255, 255, 255, A);
                }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        static Sprite BuildBackgroundSprite(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[w * h];

            // 更接近你示例：整体更黑，红只在上方轻轻带一点
            Color top = new Color(0.10f, 0.03f, 0.05f, 1f);
            Color bottom = new Color(0.02f, 0.02f, 0.03f, 1f);

            for (int y = 0; y < h; y++)
            {
                float t = (float)y / (h - 1);
                float e = t * t;
                Color baseCol = Color.Lerp(top, bottom, e);
                baseCol.a = 1f;

                // 轻微扫描线（保留赛博感）
                float scan = ((y % 4) == 0) ? 0.015f : 0f;
                float topLine = (y < 2) ? 0.20f : 0f;

                for (int x = 0; x < w; x++)
                {
                    Color c = baseCol;
                    c.r += scan + topLine;
                    c.g += scan * 0.18f;
                    c.b += scan * 0.18f;
                    c.a = 1f;

                    pixels[y * w + x] = (Color32)c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        static float SdHex(Vector2 p, float r)
        {
            p = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
            float k = Mathf.Max(p.x * 0.8660254f + p.y * 0.5f, p.y);
            return k - r;
        }

        static float Smoothstep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }

    public class GenericSimpleHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Text label;
        public Image line;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (label != null) label.color = new Color(1f, 0.25f, 0.25f, 0.95f);
            if (line != null) line.color = new Color(0.25f, 0.95f, 0.95f, 0.95f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (label != null) label.color = new Color(0.65f, 0.95f, 0.95f, 0.95f);
            if (line != null) line.color = new Color(1f, 0.25f, 0.25f, 0.95f);
        }
    }

    public class GenericHexButtonVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public RectTransform Target;
        public Image Outline;
        public Image Glow;

        public Color NormalOutline = Color.white;
        public Color NormalGlow = new Color(1, 1, 1, 0.2f);
        public Color HoverOutline = new Color(0.2f, 1f, 1f, 1f);
        public Color HoverGlow = new Color(0.2f, 1f, 1f, 0.35f);
        public Color PressedOutline = new Color(1f, 0.35f, 0.35f, 1f);
        public Color PressedGlow = new Color(1f, 0.35f, 0.35f, 0.40f);

        public float HoverScale = 1.04f;
        public float PressedScale = 0.98f;

        Vector3 baseScale;

        void Awake()
        {
            if (Target == null) Target = transform as RectTransform;
            baseScale = Target != null ? Target.localScale : Vector3.one;
            ApplyNormal();
        }

        public void OnPointerEnter(PointerEventData eventData) => ApplyHover();
        public void OnPointerExit(PointerEventData eventData) => ApplyNormal();
        public void OnPointerDown(PointerEventData eventData) => ApplyPressed();

        public void OnPointerUp(PointerEventData eventData)
        {
            bool hovered = RectTransformUtility.RectangleContainsScreenPoint(Target, eventData.position, eventData.enterEventCamera);
            if (hovered) ApplyHover(); else ApplyNormal();
        }

        void ApplyNormal()
        {
            if (Outline != null) Outline.color = NormalOutline;
            if (Glow != null) Glow.color = NormalGlow;
            if (Target != null) Target.localScale = baseScale;
        }

        void ApplyHover()
        {
            if (Outline != null) Outline.color = HoverOutline;
            if (Glow != null) Glow.color = HoverGlow;
            if (Target != null) Target.localScale = baseScale * HoverScale;
        }

        void ApplyPressed()
        {
            if (Outline != null) Outline.color = PressedOutline;
            if (Glow != null) Glow.color = PressedGlow;
            if (Target != null) Target.localScale = baseScale * PressedScale;
        }
    }
}
