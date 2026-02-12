using System;
using UnityEngine;

public class UpgradeUIManager : MonoBehaviour
{
    public event Action ExitRequested;
    public event Action UnlockDroneBurstRequested;

    [Header("UI Root (scene instance)")]
    [SerializeField] GameObject uiRoot;

    [SerializeField] CyberpunkAttributeUIScreen uiScreen;

    [Header("Audio")]
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

        uiScreen.onExit.RemoveListener(RequestClose);
        uiScreen.onUnlockDroneBurst.RemoveListener(OnClickUnlockDroneBurst);

        uiScreen.onExit.AddListener(RequestClose);
        uiScreen.onUnlockDroneBurst.AddListener(OnClickUnlockDroneBurst);
    }

    public void Open()
    {
        if (isOpen) return;
        if (ScreenFader.Instance == null)
        {
            Debug.LogError("[UpgradeUIManager] ScreenFader.Instance not found. Please add ScreenFader to scene.");
            return;
        }

        ScreenFader.Instance.FadeOutIn(
            midAction: OpenImmediate,
            outDuration: fadeOut,
            inDuration: fadeIn,
            blackHoldSeconds: blackHold
        );
    }

    public void RequestClose()
    {
        if (!isOpen) return;

        if (ExitRequested != null)
        {
            ExitRequested.Invoke();
            return;
        }

        CloseWithFadeFallback();
    }

    public void OpenImmediate()
    {
        if (isOpen) return;

        if (uiRoot != null) uiRoot.SetActive(true);
        isOpen = true;

        if (TimeController.Instance != null) TimeController.Instance.Pause();
        else Time.timeScale = 0f;

        if (bgm != null && uiBgmLoop != null)
            bgm.BeginOverrideLoop(uiBgmLoop);
    }

    public void CloseImmediate()
    {
        if (!isOpen) return;

        if (bgm != null)
            bgm.EndOverrideLoop();

        if (TimeController.Instance != null) TimeController.Instance.Resume();
        else Time.timeScale = 1f;

        if (uiRoot != null) uiRoot.SetActive(false);
        isOpen = false;
    }

    void CloseWithFadeFallback()
    {
        if (ScreenFader.Instance == null)
        {
            Debug.LogError("[UpgradeUIManager] ScreenFader.Instance not found. Please add ScreenFader to scene.");
            return;
        }

        ScreenFader.Instance.FadeOutIn(
            midAction: CloseImmediate,
            outDuration: fadeOut,
            inDuration: fadeIn,
            blackHoldSeconds: blackHold
        );
    }

    void OnClickUnlockDroneBurst()
    {
        UnlockDroneBurstRequested?.Invoke();
        Debug.Log("[UpgradeUIManager] Unlock Drone Burst clicked (placeholder).");
    }
}
