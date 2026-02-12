using UnityEngine;

public class UpgradeUIManager : MonoBehaviour
{
    [Header("UI Root (scene instance)")]
    [Tooltip("拖入场景里的 CyberpunkAttributeUIScreen 所在根物体（建议整个 prefab 根物体）。")]
    [SerializeField] GameObject uiRoot;

    [Tooltip("场景里的 CyberpunkAttributeUIScreen 组件（可不填，运行时会从 uiRoot 里找）。")]
    [SerializeField] CyberpunkAttributeUIScreen uiScreen;

    [Header("Audio")]
    [Tooltip("升级界面专属 BGM（循环）。")]
    [SerializeField] AudioClip uiBgmLoop;

    [Header("Fade")]
    [SerializeField, Range(0f, 2f)] float fadeOut = 0.35f;
    [SerializeField, Range(0f, 2f)] float fadeIn = 0.35f;
    [SerializeField, Range(0f, 1f)] float blackHold = 0.05f;

    [Header("Debug")]
    [SerializeField] bool enableDebugHotkeys = true;
    [SerializeField] KeyCode openKey = KeyCode.F1;
    [SerializeField] KeyCode closeKey = KeyCode.Escape;

    bool isOpen;
    BgmController bgm;

    void Awake()
    {
        if (uiRoot == null)
        {
            Debug.LogWarning("[UpgradeUIManager] uiRoot is null. Please assign the UI prefab instance in scene.");
        }
        else
        {
            if (uiScreen == null)
                uiScreen = uiRoot.GetComponentInChildren<CyberpunkAttributeUIScreen>(true);

            // 默认关掉 UI
            uiRoot.SetActive(false);
        }

        bgm = FindFirstObjectByType<BgmController>();
        WireUIEvents();
    }

    void OnEnable()
    {
        WireUIEvents();
    }

    void Update()
    {
        if (!enableDebugHotkeys) return;

        if (!isOpen && Input.GetKeyDown(openKey))
            Open();

        if (isOpen && Input.GetKeyDown(closeKey))
            RequestClose();
    }

    void WireUIEvents()
    {
        if (uiScreen == null) return;

        // 防止重复绑定
        uiScreen.onExit.RemoveListener(RequestClose);
        uiScreen.onUnlockDroneBurst.RemoveListener(OnClickUnlockDroneBurst);

        uiScreen.onExit.AddListener(RequestClose);
        uiScreen.onUnlockDroneBurst.AddListener(OnClickUnlockDroneBurst);
    }

    // =========================
    // Public API
    // =========================

    public void Open()
    {
        if (isOpen) return;
        if (ScreenFader.Instance == null)
        {
            Debug.LogError("[UpgradeUIManager] ScreenFader.Instance not found. Please add ScreenFader to scene.");
            return;
        }

        ScreenFader.Instance.FadeOutIn(
            midAction: () =>
            {
                // 1) 打开 UI
                if (uiRoot != null) uiRoot.SetActive(true);
                isOpen = true;

                // 2) 暂停游戏（会显示鼠标）
                if (TimeController.Instance != null) TimeController.Instance.Pause();
                else Time.timeScale = 0f;

                // 3) UI 专属 BGM（覆盖/压制其它 stem）
                if (bgm != null && uiBgmLoop != null)
                    bgm.BeginOverrideLoop(uiBgmLoop);
            },
            outDuration: fadeOut,
            inDuration: fadeIn,
            blackHoldSeconds: blackHold
        );
    }

    public void RequestClose()
    {
        if (!isOpen) return;
        if (ScreenFader.Instance == null)
        {
            Debug.LogError("[UpgradeUIManager] ScreenFader.Instance not found. Please add ScreenFader to scene.");
            return;
        }

        ScreenFader.Instance.FadeOutIn(
            midAction: () =>
            {
                // 1) 停止 UI BGM 覆盖
                if (bgm != null)
                    bgm.EndOverrideLoop();

                // 2) 恢复时间
                if (TimeController.Instance != null) TimeController.Instance.Resume();
                else Time.timeScale = 1f;

                // 3) 关闭 UI
                if (uiRoot != null) uiRoot.SetActive(false);
                isOpen = false;
            },
            outDuration: fadeOut,
            inDuration: fadeIn,
            blackHoldSeconds: blackHold
        );
    }

    // =========================
    // Buttons
    // =========================

    void OnClickUnlockDroneBurst()
    {
        // 现在只是占位：后面接“无人机连射解锁”系统
        Debug.Log("[UpgradeUIManager] Unlock Drone Burst clicked (placeholder).");
    }
}
