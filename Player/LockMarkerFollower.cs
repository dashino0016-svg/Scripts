using UnityEngine;

public class LockMarkerFollower : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] LockOnSystem lockOn;
    [SerializeField] Camera cam;

    [Header("Follow")]
    public Vector3 worldOffset = Vector3.zero;

    [Tooltip("如果勾选：锁定目标时会计算一次“渲染器中心偏移”，之后不再每帧重新算（避免抖动）")]
    public bool useRendererCenterOnce = false;

    [Tooltip("跟随平滑时间（0=不平滑，最稳）")]
    [Range(0f, 0.3f)]
    public float smoothTime = 0.03f;

    [Header("Billboard")]
    public bool faceCamera = true;
    public bool lockUpright = true;

    [Header("Size")]
    [Tooltip("开启后：圆圈在屏幕上大小恒定，不会远小近大")]
    public bool constantScreenSize = true;

    [Tooltip("圆圈在屏幕上的目标像素高度（大概）")]
    public float screenSizePx = 70f;

    [Header("Visibility")]
    [SerializeField] Transform visualRoot; // 你的 Ring
    public bool hideWhenNoTarget = true;

    [Header("Assassination Visual")]
    [SerializeField] GameObject assassinationDot; // 拖 Assassinatedot 进来

    Transform _target;
    Vector3 _vel;                 // SmoothDamp 用
    Vector3 _cachedCenterOffset;  // 锁定瞬间缓存一次
    bool _hasCachedOffset;

    void Reset()
    {
        visualRoot = transform;
    }

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (visualRoot == null) visualRoot = transform;

        if (lockOn != null)
            lockOn.OnTargetChanged += HandleTargetChanged;

        if (assassinationDot != null) assassinationDot.SetActive(false);

        ApplyVisible(false);
    }

    void OnDestroy()
    {
        if (lockOn != null)
            lockOn.OnTargetChanged -= HandleTargetChanged;
    }

    void HandleTargetChanged(CombatStats stats)
    {
        _hasCachedOffset = false;
        _cachedCenterOffset = Vector3.zero;

        _target = (stats != null) ? stats.transform : null;
        ApplyVisible(_target != null);
        // ✅ 新增：目标切换/清锁时默认关闭红点，避免残留
        SetAssassinationReady(false);
        // 锁定瞬间缓存一次渲染器中心偏移（可选）
        if (_target != null && useRendererCenterOnce)
        {
            CacheRendererCenterOffsetOnce(_target);
        }
    }

    void LateUpdate()
    {
        if (lockOn == null)
            return;

        if (_target == null)
        {
            SetAssassinationReady(false); // ✅ 没目标必定关
            if (!hideWhenNoTarget) ApplyVisible(true);
            return;
        }

        // 目标点 = 目标位置 + 偏移（+ 可选缓存中心偏移）
        Vector3 targetPos = LockTargetPointUtility.GetCapsuleCenter(_target) + worldOffset;
        if (useRendererCenterOnce && _hasCachedOffset)
            targetPos += _cachedCenterOffset;

        // 跟随：smoothTime=0 最稳（完全不抖，只会“硬跟随”）
        if (smoothTime <= 0f)
        {
            transform.position = targetPos;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref _vel, smoothTime);
        }

        // 面向相机（Billboard）
        if (faceCamera && cam != null)
        {
            Vector3 forward = (transform.position - cam.transform.position);
            if (lockUpright) forward.y = 0f;

            if (forward.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        // 恒定屏幕尺寸（解决远小近大）
        if (constantScreenSize && cam != null)
        {
            float dist = Vector3.Distance(cam.transform.position, transform.position);
            float worldHeight = GetWorldSizeFromScreenPx(screenSizePx, dist, cam);
            transform.localScale = new Vector3(worldHeight, worldHeight, worldHeight);
        }
    }

    void CacheRendererCenterOffsetOnce(Transform t)
    {
        // 合并所有 Renderer 的 bounds，只算一次
        var rends = t.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0)
        {
            _hasCachedOffset = false;
            return;
        }

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);

        // 我们想要的是：中心点相对 root 的偏移（世界空间）
        // 注意：bounds.center 在世界空间
        _cachedCenterOffset = (b.center - t.position);
        _hasCachedOffset = true;
    }

    float GetWorldSizeFromScreenPx(float px, float distance, Camera camera)
    {
        // 透视：屏幕高度对应的世界高度 = 2 * d * tan(fov/2)
        // px 占比乘一下即可
        float frustumHeight = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float ratio = px / Mathf.Max(1f, (float)Screen.height);
        return frustumHeight * ratio;
    }

    public void SetAssassinationReady(bool ready)
    {
        if (assassinationDot != null)
            assassinationDot.SetActive(ready);
    }

    void ApplyVisible(bool v)
    {
        if (visualRoot != null)
            visualRoot.gameObject.SetActive(v);
    }
}
