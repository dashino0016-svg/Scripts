using System.Collections;
using UnityEngine;

public class BgmManager : MonoBehaviour
{
    [Header("AudioSources (both Output -> Mixer/BGM)")]
    [SerializeField] private AudioSource a;
    [SerializeField] private AudioSource b;

    [Header("Clips")]
    [SerializeField] private AudioClip explorationClip;
    [SerializeField] private AudioClip combatClip;

    [Header("Tuning")]
    [SerializeField] private float crossFadeTime = 1.5f;
    [SerializeField] private float exitCombatDelay = 2.0f; // 脱战后延迟回常规，防抖

    private bool isCombat;
    private Coroutine co;

    private void Awake()
    {
        // 可选：需要跨场景就打开
        // DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 初始播探索
        PlayImmediate(explorationClip, 1f);
        isCombat = false;
    }

    private void Update()
    {
        // 你项目里已有这个全局条件：AnyEnemyInCombat
        bool shouldCombat = EnemyState.AnyEnemyInCombat;
        if (shouldCombat != isCombat)
            Debug.Log($"[Bgm] switch {isCombat} -> {shouldCombat}  AnyEnemyInCombat={EnemyState.AnyEnemyInCombat}", this);
            SetCombat(shouldCombat);
    }

    public void SetCombat(bool toCombat)
    {
        isCombat = toCombat;

        if (co != null) StopCoroutine(co);
        co = StartCoroutine(SwitchRoutine(toCombat));
    }

    private IEnumerator SwitchRoutine(bool toCombat)
    {
        if (!toCombat)
        {
            // 脱战延迟，期间如果又进战则取消这次切换
            float tDelay = 0f;
            while (tDelay < exitCombatDelay)
            {
                if (EnemyState.AnyEnemyInCombat) yield break;
                tDelay += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        AudioClip target = toCombat ? combatClip : explorationClip;
        if (!target) yield break;

        // 选择“当前在响的”为 from，另一条为 to
        AudioSource from = (a.isPlaying && a.volume > 0.01f) ? a : b;
        AudioSource to = (from == a) ? b : a;

        // 如果目标曲子已经在播放且音量正常，就不重复切
        if (to.clip == target && to.isPlaying && to.volume > 0.99f) yield break;

        to.clip = target;
        to.loop = true;
        to.volume = 0f;
        to.Play();

        float t = 0f;
        while (t < crossFadeTime)
        {
            // 用 unscaled 避免你项目里 HitStop/TimeScale 影响 BGM 淡化
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / crossFadeTime);

            to.volume = k;
            from.volume = 1f - k;

            yield return null;
        }

        from.Stop();
        from.volume = 1f;
        to.volume = 1f;
    }

    private void PlayImmediate(AudioClip clip, float volume)
    {
        if (!clip) return;
        a.clip = clip;
        a.loop = true;
        a.volume = volume;
        a.Play();

        b.Stop();
        b.volume = 0f;
    }
}
