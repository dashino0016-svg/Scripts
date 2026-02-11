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
    public AudioSource overrideSource;

    [Header("Volumes")]
    [Range(0f, 1f)] public float explorationVolume = 1f;
    [Range(0f, 1f)] public float combat2MaxVolume = 1f;
    [Range(0f, 1f)] public float boss3MaxVolume = 1f;

    [Header("Fade Seconds")]
    [Min(0f)] public float fadeOutExploration = 1.0f;
    [Min(0f)] public float fadeInExploration = 1.0f;
    [Min(0f)] public float fadeInCombat2 = 1.0f;
    [Min(0f)] public float fadeOutCombat2 = 1.2f;
    [Min(0f)] public float fadeInBoss3 = 1.0f;
    [Min(0f)] public float fadeOutBoss3 = 1.2f;

    [Header("Ability3 Override")]
    [Range(0f, 1f)] public float overrideMaxVolume = 1f;
    [Range(0f, 1f)] public float stemDuckMultiplier = 0f;
    [Min(0f)] public float overrideFadeIn = 0.3f;
    [Min(0f)] public float overrideFadeOut = 0.4f;

    [Header("Sync")]
    [Min(0.0f)] public float startDelay = 0.06f;

    [Header("Stop Policy")]
    public bool stopWhenSilent = true;

    float tExplore;
    float t2;
    float t3;
    float tOverride;

    double combatDspStart = -1;
    bool combatFadeArmed;
    bool bossWantedAtCombatStart;

    bool lastInCombat;
    bool overrideActive;

    void Awake()
    {
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
        SetupSource(overrideSource, null);

        StartExplorationFromBeginning();
        lastInCombat = EnemyState.AnyEnemyInCombat;
    }

    void EnsureSources()
    {
        if (explorationSource == null) explorationSource = CreateChildSource("BGM_Explore");
        if (s2 == null) s2 = CreateChildSource("BGM_Combat_L2");
        if (s3 == null) s3 = CreateChildSource("BGM_Combat_L3");
        if (overrideSource == null) overrideSource = CreateChildSource("BGM_Override");
    }

    AudioSource CreateChildSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 0f;
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

        if (inCombat && !lastInCombat)
            BeginCombatCrossfade(bossCombat);
        else if (!inCombat && lastInCombat)
            BeginExplorationCrossfade();

        if (combatFadeArmed && combatDspStart > 0)
        {
            if (!inCombat)
            {
                combatFadeArmed = false;
                combatDspStart = -1;
                t2 = 0f;
                t3 = 0f;
            }
            else if (AudioSettings.dspTime >= combatDspStart - 0.01f)
            {
                combatFadeArmed = false;
                t2 = combat2MaxVolume;
                t3 = bossWantedAtCombatStart ? boss3MaxVolume : 0f;
            }
        }

        if (inCombat && !combatFadeArmed)
            t3 = bossCombat ? boss3MaxVolume : 0f;

        ApplyFades(Time.unscaledDeltaTime, inCombat);
        lastInCombat = inCombat;
    }

    public void BeginOverrideLoop(AudioClip loopClip)
    {
        if (overrideSource == null || loopClip == null)
            return;

        if (overrideSource.clip != loopClip)
        {
            overrideSource.Stop();
            overrideSource.clip = loopClip;
        }

        overrideSource.loop = true;
        overrideSource.pitch = 1f;
        if (!overrideSource.isPlaying)
        {
            overrideSource.volume = 0f;
            overrideSource.Play();
        }

        overrideActive = true;
        tOverride = overrideMaxVolume;
    }

    public void EndOverrideLoop()
    {
        overrideActive = false;
        tOverride = 0f;
    }

    void StartExplorationFromBeginning()
    {
        combatFadeArmed = false;
        combatDspStart = -1;
        StopSourceImmediate(s2);
        StopSourceImmediate(s3);

        if (explorationSource != null && explorationSource.clip != null)
        {
            explorationSource.Stop();
            explorationSource.volume = explorationVolume;
            explorationSource.Play();
        }

        tExplore = explorationVolume;
        t2 = 0f;
        t3 = 0f;
        overrideActive = false;
        tOverride = 0f;
        StopSourceImmediate(overrideSource);
    }

    void BeginExplorationCrossfade()
    {
        combatFadeArmed = false;
        combatDspStart = -1;

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
        tExplore = 0f;

        if (s2 == null || s3 == null || s2.clip == null || s3.clip == null)
        {
            combatFadeArmed = false;
            combatDspStart = -1;

            if (s2 != null && s2.clip != null)
            {
                s2.Stop();
                s2.volume = 0f;
                s2.Play();
                t2 = combat2MaxVolume;
            }
            else t2 = 0f;

            if (s3 != null && s3.clip != null)
            {
                s3.Stop();
                s3.volume = 0f;
                s3.Play();
                t3 = bossNow ? boss3MaxVolume : 0f;
            }
            else t3 = 0f;
            return;
        }

        double dsp = AudioSettings.dspTime + startDelay;
        combatDspStart = dsp;
        combatFadeArmed = true;

        t2 = 0f;
        t3 = 0f;

        s2.Stop();
        s3.Stop();
        s2.volume = 0f;
        s3.volume = 0f;
        s2.PlayScheduled(dsp);
        s3.PlayScheduled(dsp);
    }

    void ApplyFades(float dtUnscaled, bool inCombat)
    {
        float stemMultiplier = overrideActive ? stemDuckMultiplier : 1f;
        float exploreTarget = tExplore * stemMultiplier;
        float combat2Target = t2 * stemMultiplier;
        float boss3Target = t3 * stemMultiplier;

        if (explorationSource != null)
        {
            float step = Step(dtUnscaled, (exploreTarget > explorationSource.volume) ? fadeInExploration : fadeOutExploration);
            explorationSource.volume = Mathf.MoveTowards(explorationSource.volume, exploreTarget, step);

            if (stopWhenSilent && exploreTarget <= 0.0001f && explorationSource.volume <= 0.0001f && explorationSource.isPlaying)
                explorationSource.Stop();
        }

        if (s2 != null)
        {
            float step = Step(dtUnscaled, (combat2Target > s2.volume) ? fadeInCombat2 : fadeOutCombat2);
            s2.volume = Mathf.MoveTowards(s2.volume, combat2Target, step);

            if (stopWhenSilent && !inCombat && !combatFadeArmed && combat2Target <= 0.0001f && s2.volume <= 0.0001f && s2.isPlaying)
                s2.Stop();
        }

        if (s3 != null)
        {
            float step = Step(dtUnscaled, (boss3Target > s3.volume) ? fadeInBoss3 : fadeOutBoss3);
            s3.volume = Mathf.MoveTowards(s3.volume, boss3Target, step);

            if (stopWhenSilent && !inCombat && !combatFadeArmed && boss3Target <= 0.0001f && s3.volume <= 0.0001f && s3.isPlaying)
                s3.Stop();
        }

        if (overrideSource != null)
        {
            float step = Step(dtUnscaled, (tOverride > overrideSource.volume) ? overrideFadeIn : overrideFadeOut);
            overrideSource.volume = Mathf.MoveTowards(overrideSource.volume, tOverride, step);

            if (stopWhenSilent && !overrideActive && tOverride <= 0.0001f && overrideSource.volume <= 0.0001f && overrideSource.isPlaying)
                overrideSource.Stop();
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
