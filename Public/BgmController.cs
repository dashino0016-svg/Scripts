using UnityEngine;

public class BgmController : MonoBehaviour
{
    [Header("Exploration (single track, different style)")]
    public AudioClip explorationClip;

    [Header("Combat (stems: same length, seamless loop, can overlay)")]
    public AudioClip combatLayer2;
    public AudioClip bossLayer3;

    [Header("AudioSources (optional, can auto-create)")]
    public AudioSource explorationSource;
    public AudioSource s2;
    public AudioSource s3;

    [Header("Volumes")]
    [Range(0f, 1f)] public float explorationVolume = 1f;
    [Range(0f, 1f)] public float combat2MaxVolume = 1f;
    [Range(0f, 1f)] public float boss3MaxVolume = 1f;

    [Header("Fade Seconds")]
    [Min(0f)] public float fadeOutExploration = 1.0f; // 进入战斗时：1 淡出
    [Min(0f)] public float fadeInExploration = 1.0f;  // 退出战斗时：1 淡入

    [Min(0f)] public float fadeInCombat2 = 1.0f;       // 进入战斗时：2 淡入
    [Min(0f)] public float fadeOutCombat2 = 1.2f;      // 退出战斗时：2 淡出

    [Min(0f)] public float fadeInBoss3 = 1.0f;         // Boss：3 淡入
    [Min(0f)] public float fadeOutBoss3 = 1.2f;        // 非Boss：3 淡出

    [Header("Sync")]
    [Tooltip("进入战斗时，2/3 用 PlayScheduled 同相位从 0 开始。这个延迟给音频系统准备时间。")]
    [Min(0.0f)] public float startDelay = 0.06f;

    [Header("Stop Policy")]
    [Tooltip("当音量淡到 0 时是否 Stop（省资源）。探索曲会 Stop；战斗 stems 默认也 Stop。")]
    public bool stopWhenSilent = true;

    // 当前目标音量
    float tExplore;
    float t2;
    float t3;

    // 进入战斗：为了保证 “2 从开头开始并且淡入从播放开始时刻起算”
    double combatDspStart = -1;
    bool combatFadeArmed;
    bool bossWantedAtCombatStart;

    bool lastInCombat;

    void Awake()
    {
        // 单例
        var all = FindObjectsOfType<BgmController>();
        if (all.Length > 1)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);

        EnsureSources();
        SetupSource(explorationSource, explorationClip);
        SetupSource(s2, combatLayer2);
        SetupSource(s3, bossLayer3);

        // 初始：探索
        StartExplorationFromBeginning();

        lastInCombat = EnemyState.AnyEnemyInCombat;
    }

    void EnsureSources()
    {
        if (explorationSource == null) explorationSource = CreateChildSource("BGM_Explore");
        if (s2 == null) s2 = CreateChildSource("BGM_Combat_L2");
        if (s3 == null) s3 = CreateChildSource("BGM_Combat_L3");
    }

    AudioSource CreateChildSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 0f; // BGM 2D
        src.dopplerLevel = 0f;
        src.playOnAwake = false;
        src.loop = true;
        return src;
    }

    void SetupSource(AudioSource src, AudioClip clip)
    {
        if (src == null) return;
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.volume = 0f;
        src.pitch = 1f;
    }

    void Update()
    {
        bool inCombat = EnemyState.AnyEnemyInCombat;
        bool bossCombat = BossCombatTag.AnyBossInCombat;

        // 1) 状态切换：探索 <-> 战斗
        if (inCombat && !lastInCombat)
        {
            BeginCombatCrossfade(bossCombat);
        }
        else if (!inCombat && lastInCombat)
        {
            BeginExplorationCrossfade();
        }

        // 2) 若战斗播放已到 dspStart，则从“那一刻”开始淡入 2/3（确保 2 从开头播放且淡入不提前）
        if (combatFadeArmed && combatDspStart > 0)
        {
            if (!inCombat)
            {
                // 战斗很快结束：取消这次战斗淡入
                combatFadeArmed = false;
                combatDspStart = -1;
                t2 = 0f;
                t3 = 0f;
            }
            else if (AudioSettings.dspTime >= combatDspStart - 0.01f)
            {
                combatFadeArmed = false;
                // 2 开始淡入
                t2 = combat2MaxVolume;
                // 3 是否淡入取决于 boss
                t3 = (bossWantedAtCombatStart ? boss3MaxVolume : 0f);
            }
        }

        // 3) 战斗中 boss 状态变化：只控制 3 的目标音量（2 始终维持）
        if (inCombat && !combatFadeArmed)
        {
            t3 = bossCombat ? boss3MaxVolume : 0f;
        }

        // 4) 执行淡入淡出（用 unscaled，避免 HitStop/TimeScale 影响）
        ApplyFades(Time.unscaledDeltaTime, inCombat);

        lastInCombat = inCombat;
    }

    // ============ Transitions ============

    void StartExplorationFromBeginning()
    {
        combatFadeArmed = false;
        combatDspStart = -1;

        // 停战斗 stems
        StopSourceImmediate(s2);
        StopSourceImmediate(s3);

        // 播探索从头
        if (explorationSource != null && explorationSource.clip != null)
        {
            explorationSource.Stop();
            explorationSource.volume = explorationVolume;
            explorationSource.Play();
        }

        tExplore = explorationVolume;
        t2 = 0f;
        t3 = 0f;
    }

    void BeginExplorationCrossfade()
    {
        combatFadeArmed = false;
        combatDspStart = -1;

        // 探索从头开始并淡入
        if (explorationSource != null && explorationSource.clip != null)
        {
            explorationSource.Stop();
            explorationSource.volume = 0f;
            explorationSource.Play();
        }

        tExplore = explorationVolume;
        t2 = 0f;
        t3 = 0f;
    }

    void BeginCombatCrossfade(bool bossNow)
    {
        bossWantedAtCombatStart = bossNow;

        // 进入战斗：探索 1 立刻开始淡出（可以中途停）
        tExplore = 0f;

        // 战斗 stems ②/③ 必须从开头开始，并且②+③同相位
        if (s2 == null || s3 == null || s2.clip == null || s3.clip == null)
        {
            // 没配齐也尽量降级：只要有②就播②，否则啥也不做
            combatFadeArmed = false;
            combatDspStart = -1;

            if (s2 != null && s2.clip != null)
            {
                s2.Stop();
                s2.volume = 0f;
                s2.Play();
                t2 = combat2MaxVolume;
            }
            else
            {
                t2 = 0f;
            }

            // ③ 只有在存在并且 boss 才淡入
            if (s3 != null && s3.clip != null)
            {
                s3.Stop();
                s3.volume = 0f;
                s3.Play();
                t3 = bossNow ? boss3MaxVolume : 0f;
            }
            else
            {
                t3 = 0f;
            }
            return;
        }

        // 用 scheduled 确保 ②/③同帧同相位从 0 开始
        double dsp = AudioSettings.dspTime + startDelay;
        combatDspStart = dsp;
        combatFadeArmed = true;

        // 先把目标音量设为 0，等到 dspStart 再开始淡入（避免淡入提前）
        t2 = 0f;
        t3 = 0f;

        s2.Stop();
        s3.Stop();

        s2.volume = 0f;
        s3.volume = 0f;

        s2.PlayScheduled(dsp);
        s3.PlayScheduled(dsp);
    }

    // ============ Fades & Stops ============

    void ApplyFades(float dtUnscaled, bool inCombat)
    {
        // 探索
        if (explorationSource != null)
        {
            float step = Step(dtUnscaled, (tExplore > explorationSource.volume) ? fadeInExploration : fadeOutExploration);
            explorationSource.volume = Mathf.MoveTowards(explorationSource.volume, tExplore, step);

            if (stopWhenSilent && tExplore <= 0.0001f && explorationSource.volume <= 0.0001f && explorationSource.isPlaying)
                explorationSource.Stop();
        }

        // Combat 2
        if (s2 != null)
        {
            float step = Step(dtUnscaled, (t2 > s2.volume) ? fadeInCombat2 : fadeOutCombat2);
            s2.volume = Mathf.MoveTowards(s2.volume, t2, step);

            // 退出战斗后，淡到 0 就停（下一次进入战斗必须从开头）
            if (stopWhenSilent && !inCombat && !combatFadeArmed && t2 <= 0.0001f && s2.volume <= 0.0001f && s2.isPlaying)
                s2.Stop();
        }

        // Boss 3
        if (s3 != null)
        {
            float step = Step(dtUnscaled, (t3 > s3.volume) ? fadeInBoss3 : fadeOutBoss3);
            s3.volume = Mathf.MoveTowards(s3.volume, t3, step);

            if (stopWhenSilent && !inCombat && !combatFadeArmed && t3 <= 0.0001f && s3.volume <= 0.0001f && s3.isPlaying)
                s3.Stop();
        }
    }

    static float Step(float dt, float seconds)
    {
        if (seconds <= 0f) return 1f;
        return dt / seconds;
    }

    static void StopSourceImmediate(AudioSource src)
    {
        if (src == null) return;
        if (src.isPlaying) src.Stop();
        src.volume = 0f;
    }
}
