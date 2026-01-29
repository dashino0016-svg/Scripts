using UnityEngine;
using System.Collections;

public class TimeController : MonoBehaviour
{
    public static TimeController Instance;

    [Header("Default Time")]
    public float defaultTimeScale = 1f;

    [Header("Pause")]
    public KeyCode pauseKey = KeyCode.Tab;

    bool isPaused;
    bool isHitStopActive;
    Coroutine hitStopCoroutine;

    public bool IsPaused => isPaused;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Time.timeScale = defaultTimeScale;
    }

    void Update()
    {
        HandlePauseInput();
    }

    // =========================
    // 暂停（调试用）
    // =========================
    void HandlePauseInput()
    {
        if (!Input.GetKeyDown(pauseKey))
            return;

        if (isPaused)
            Resume();
        else
            Pause();
    }

    public void Pause()
    {
        if (isPaused) return;

        isPaused = true;
        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Resume()
    {
        if (!isPaused) return;

        isPaused = false;
        Time.timeScale = defaultTimeScale;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // =========================
    // Hit Stop（卡肉）
    // =========================
    public void HitStop(float timeScale, float durationSeconds)
    {
        // ⭐ 暂停中不允许卡肉；同一时间只允许一个 HitStop 生效
        if (isPaused || isHitStopActive) return;

        hitStopCoroutine = StartCoroutine(HitStopRoutine(timeScale, durationSeconds));
    }

    IEnumerator HitStopRoutine(float timeScale, float durationSeconds)
    {
        isHitStopActive = true;

        Time.timeScale = timeScale;

        yield return new WaitForSecondsRealtime(durationSeconds);

        if (!isPaused)
            Time.timeScale = defaultTimeScale;

        isHitStopActive = false;
        hitStopCoroutine = null;
    }
}
