using System;
using UnityEngine;
using CyberpunkGenericHexUI;

public class UpgradeUIManager : MonoBehaviour
{
    public event Action CloseRequested;

    public event Action Button1Requested;
    public event Action Button2Requested;
    public event Action Button3Requested;
    public event Action Button4Requested;
    public event Action Button5Requested;
    public event Action Button6Requested;

    [Header("UI Root (scene instance)")]
    [SerializeField] GameObject uiRoot;
    [SerializeField] CyberpunkGenericHexUIScreen uiScreen;

    [Header("Audio")]
    [SerializeField] AudioClip uiBgmLoop;

    [Header("Fade")]
    [SerializeField, Range(0f, 2f)] float fadeOut = 0.35f;
    [SerializeField, Range(0f, 2f)] float fadeIn = 0.35f;
    [SerializeField, Range(0f, 1f)] float blackHold = 0.05f;

    [Header("Keyboard")]
    [SerializeField] KeyCode closeKey = KeyCode.Escape;

    bool isOpen;
    BgmController bgm;

    void Awake()
    {
        if (uiRoot == null)
        {
            Debug.LogWarning("[UpgradeUIManager] uiRoot is null. Please assign the UI root GameObject in scene.");
        }
        else
        {
            if (uiScreen == null)
                uiScreen = uiRoot.GetComponentInChildren<CyberpunkGenericHexUIScreen>(true);

            uiRoot.SetActive(false);
        }

        bgm = FindFirstObjectByType<BgmController>();
        WireUIEvents();
    }

    void OnEnable() => WireUIEvents();

    void Update()
    {
        if (isOpen && Input.GetKeyDown(closeKey))
            RequestClose();
    }

    void WireUIEvents()
    {
        if (uiScreen == null) return;

        uiScreen.onClose.RemoveListener(RequestClose);
        uiScreen.onClose.AddListener(RequestClose);

        uiScreen.onButton1.RemoveListener(OnClickButton1);
        uiScreen.onButton2.RemoveListener(OnClickButton2);
        uiScreen.onButton3.RemoveListener(OnClickButton3);
        uiScreen.onButton4.RemoveListener(OnClickButton4);
        uiScreen.onButton5.RemoveListener(OnClickButton5);
        uiScreen.onButton6.RemoveListener(OnClickButton6);

        uiScreen.onButton1.AddListener(OnClickButton1);
        uiScreen.onButton2.AddListener(OnClickButton2);
        uiScreen.onButton3.AddListener(OnClickButton3);
        uiScreen.onButton4.AddListener(OnClickButton4);
        uiScreen.onButton5.AddListener(OnClickButton5);
        uiScreen.onButton6.AddListener(OnClickButton6);
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

        if (CloseRequested != null)
        {
            CloseRequested.Invoke();
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

    void OnClickButton1() => Button1Requested?.Invoke();
    void OnClickButton2() => Button2Requested?.Invoke();
    void OnClickButton3() => Button3Requested?.Invoke();
    void OnClickButton4() => Button4Requested?.Invoke();
    void OnClickButton5() => Button5Requested?.Invoke();
    void OnClickButton6() => Button6Requested?.Invoke();
}
