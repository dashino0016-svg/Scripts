using UnityEngine;

public class BgmController : MonoBehaviour
{
    [Header("Clips (same length, seamless loop)")]
    public AudioClip layer1Exploration;
    public AudioClip layer2Combat;
    public AudioClip layer3Boss;

    [Header("Sources (optional, can auto-create)")]
    public AudioSource s1;
    public AudioSource s2;
    public AudioSource s3;

    [Header("Mix Volumes")]
    [Range(0f, 1f)] public float layer1Volume = 1f;
    [Range(0f, 1f)] public float layer2MaxVolume = 1f;
    [Range(0f, 1f)] public float layer3MaxVolume = 1f;

    [Header("Fade Seconds")]
    [Min(0f)] public float fadeInCombat = 1.0f;
    [Min(0f)] public float fadeOutCombat = 1.2f;
    [Min(0f)] public float fadeInBoss = 1.0f;
    [Min(0f)] public float fadeOutBoss = 1.2f;

    [Header("Sync")]
    [Min(0.01f)] public float startDelay = 0.08f; // 给音频系统一点准备时间
    public bool keepLayersPlayingMuted = true;

    bool started;
    bool lastCombat;
    bool lastBoss;

    void Awake()
    {
        // 单例
        var existing = FindObjectsOfType<BgmController>();
        if (existing.Length > 1)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);

        EnsureSources();
        SetupSource(s1, layer1Exploration);
        SetupSource(s2, layer2Combat);
        SetupSource(s3, layer3Boss);

        StartSynced();

        // 初始音量：探索=1，战斗/ Boss =0
        s1.volume = layer1Volume;
        s2.volume = 0f;
        s3.volume = 0f;

        lastCombat = EnemyState.AnyEnemyInCombat;
        lastBoss = BossCombatTag.AnyBossInCombat;
    }

    void EnsureSources()
    {
        if (s1 == null) s1 = CreateChildSource("BGM_L1");
        if (s2 == null) s2 = CreateChildSource("BGM_L2");
        if (s3 == null) s3 = CreateChildSource("BGM_L3");
    }

    AudioSource CreateChildSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        return src;
    }

    void SetupSource(AudioSource src, AudioClip clip)
    {
        if (src == null) return;

        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;     // BGM 默认 2D
        src.dopplerLevel = 0f;
        src.pitch = 1f;
    }

    void StartSynced()
    {
        if (started) return;
        if (s1.clip == null || s2.clip == null || s3.clip == null)
        {
            Debug.LogWarning("[LayeredBgmController] Missing clips. Please assign 3 clips.");
            return;
        }

        double dsp = AudioSettings.dspTime + startDelay;

        s1.PlayScheduled(dsp);
        s2.PlayScheduled(dsp);
        s3.PlayScheduled(dsp);

        started = true;
    }

    void Update()
    {
        if (!started) return;

        bool inCombat = EnemyState.AnyEnemyInCombat;
        bool bossCombat = BossCombatTag.AnyBossInCombat;

        // 目标音量
        float t1 = layer1Volume;
        float t2 = inCombat ? layer2MaxVolume : 0f;
        float t3 = (inCombat && bossCombat) ? layer3MaxVolume : 0f;

        // fade 使用 unscaled（不受 HitStop/TimeScale 影响）
        float dt = Time.unscaledDeltaTime;

        s1.volume = MoveTo(s1.volume, t1, dt / 0.001f); // L1 常驻，不需要慢慢变；这里近似保持
        s2.volume = MoveTo(s2.volume, t2, dt / Mathf.Max(0.001f, inCombat ? fadeInCombat : fadeOutCombat));
        s3.volume = MoveTo(s3.volume, t3, dt / Mathf.Max(0.001f, (inCombat && bossCombat) ? fadeInBoss : fadeOutBoss));

        // 可选：如果你不想 muted 时还在播放，可在完全 0 后停掉（但会破坏同步，不建议）
        if (!keepLayersPlayingMuted)
        {
            HandleStopIfSilent(s2);
            HandleStopIfSilent(s3);
        }

        lastCombat = inCombat;
        lastBoss = bossCombat;
    }

    static float MoveTo(float current, float target, float step)
    {
        // step=每帧最多变化量（这里用 dt/fadeSeconds 得到 0~1 的变化速度）
        return Mathf.MoveTowards(current, target, Mathf.Clamp01(step));
    }

    void HandleStopIfSilent(AudioSource src)
    {
        if (src == null) return;
        if (src.volume <= 0.0001f && src.isPlaying) src.Pause();
        if (src.volume > 0.0001f && !src.isPlaying) src.UnPause();
    }
}
