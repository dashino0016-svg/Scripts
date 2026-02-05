using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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
    float hitStopEndRealtime;
    float activeHitStopScale = 1f;

    bool isLocalHitStopActive;
    float localHitStopEndRealtime;
    Coroutine localHitStopCoroutine;
    readonly Dictionary<Animator, float> localAnimatorSpeedCache = new Dictionary<Animator, float>();

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
        // ⭐ 暂停中不允许卡肉
        if (isPaused) return;

        float clampedScale = Mathf.Clamp(timeScale, 0f, defaultTimeScale);
        float clampedDuration = Mathf.Max(0f, durationSeconds);
        float newEnd = Time.unscaledTime + clampedDuration;

        // ✅ 刷新机制：命中期间可延长卡肉；更强卡肉（更小 timeScale）可覆盖当前强度
        if (isHitStopActive)
        {
            hitStopEndRealtime = Mathf.Max(hitStopEndRealtime, newEnd);
            activeHitStopScale = Mathf.Min(activeHitStopScale, clampedScale);
            Time.timeScale = activeHitStopScale;
            return;
        }

        activeHitStopScale = clampedScale;
        hitStopEndRealtime = newEnd;
        hitStopCoroutine = StartCoroutine(HitStopRoutine());
    }


    public void HitStopLocal(Transform attacker, Transform victim, float durationSeconds)
    {
        if (isPaused) return;

        float duration = Mathf.Max(0f, durationSeconds);
        float newEnd = Time.unscaledTime + duration;

        RegisterAnimatorForLocalHitStop(attacker);
        RegisterAnimatorForLocalHitStop(victim);

        if (localAnimatorSpeedCache.Count == 0)
            return;

        if (isLocalHitStopActive)
        {
            localHitStopEndRealtime = Mathf.Max(localHitStopEndRealtime, newEnd);
            return;
        }

        localHitStopEndRealtime = newEnd;
        localHitStopCoroutine = StartCoroutine(LocalHitStopRoutine());
    }

    void RegisterAnimatorForLocalHitStop(Transform t)
    {
        if (t == null) return;

        Animator a = t.GetComponentInParent<Animator>();
        if (a == null) a = t.GetComponentInChildren<Animator>();
        if (a == null) return;

        if (!localAnimatorSpeedCache.ContainsKey(a))
            localAnimatorSpeedCache.Add(a, a.speed);
    }

    IEnumerator HitStopRoutine()
    {
        isHitStopActive = true;

        while (!isPaused && Time.unscaledTime < hitStopEndRealtime)
        {
            Time.timeScale = activeHitStopScale;
            yield return null;
        }

        if (!isPaused)
            Time.timeScale = defaultTimeScale;

        isHitStopActive = false;
        activeHitStopScale = defaultTimeScale;
        hitStopCoroutine = null;
    }

    IEnumerator LocalHitStopRoutine()
    {
        isLocalHitStopActive = true;

        while (!isPaused && Time.unscaledTime < localHitStopEndRealtime)
        {
            foreach (var kv in localAnimatorSpeedCache)
            {
                if (kv.Key != null)
                    kv.Key.speed = 0f;
            }
            yield return null;
        }

        foreach (var kv in localAnimatorSpeedCache)
        {
            if (kv.Key != null)
                kv.Key.speed = kv.Value;
        }

        localAnimatorSpeedCache.Clear();
        isLocalHitStopActive = false;
        localHitStopCoroutine = null;
    }
}
