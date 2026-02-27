using UnityEngine;

[DisallowMultipleComponent]
public class CombatSfxController : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] CombatSfxConfig config;

    [Header("Playback")]
    [SerializeField] AudioSource sfxSource;
    [SerializeField, Range(0f, 1f)] float oneShotVolume = 1f;

    [Header("Conflicts")]
    [SerializeField] bool suppressAttackImpactWhenDefending;

    [Header("Optional BGM")]
    [SerializeField] BgmController bgmController;

    void Awake()
    {
        if (sfxSource == null)
            sfxSource = GetComponent<AudioSource>();

        if (bgmController == null)
            bgmController = FindObjectOfType<BgmController>();
    }

    void OnEnable()
    {
        CombatSfxSignals.OnAttackWhoosh += HandleWhoosh;
        CombatSfxSignals.OnHitResolved += HandleHitResolved;
        CombatSfxSignals.OnAbilityTriggered += HandleAbilityTriggered;
        CombatSfxSignals.OnAbility3TimeSlowBegin += HandleAbility3TimeSlowBegin;
        CombatSfxSignals.OnAbility3TimeSlowEnd += HandleAbility3TimeSlowEnd;
    }

    void OnDisable()
    {
        CombatSfxSignals.OnAttackWhoosh -= HandleWhoosh;
        CombatSfxSignals.OnHitResolved -= HandleHitResolved;
        CombatSfxSignals.OnAbilityTriggered -= HandleAbilityTriggered;
        CombatSfxSignals.OnAbility3TimeSlowBegin -= HandleAbility3TimeSlowBegin;
        CombatSfxSignals.OnAbility3TimeSlowEnd -= HandleAbility3TimeSlowEnd;
    }

    void HandleWhoosh(CombatAttackSfxKey key)
    {
        if (config == null) return;
        if (!config.TryGetWhoosh(key, out var clip)) return;
        PlayOneShot(clip);
    }

    void HandleHitResolved(CombatSfxHitContext ctx)
    {
        if (config == null) return;

        bool hasDefenderSfx = ctx.ResultType == HitResultType.Blocked ||
                              ctx.ResultType == HitResultType.PerfectBlock ||
                              ctx.ResultType == HitResultType.GuardBreak;

        bool allowImpact = !(suppressAttackImpactWhenDefending && hasDefenderSfx);
        if (allowImpact && IsImpactResult(ctx.ResultType) &&
            CombatSfxKeyUtility.TryGetAttackKey(ctx.AttackData, out var key) &&
            config.TryGetImpact(key, out var impactClip))
        {
            PlayOneShot(impactClip);
        }

        if (hasDefenderSfx)
        {
            AudioClip defenderClip = ctx.ResultType switch
            {
                HitResultType.Blocked => config.blockedClip,
                HitResultType.PerfectBlock => config.perfectBlockClip,
                HitResultType.GuardBreak => config.guardBreakClip,
                _ => null,
            };

            PlayOneShot(defenderClip);
        }
    }

    void HandleAbilityTriggered(int abilityId)
    {
        if (config == null) return;
        PlayOneShot(config.GetAbilityClip(abilityId));
    }

    void HandleAbility3TimeSlowBegin()
    {
        if (bgmController == null || config == null) return;
        bgmController.BeginOverrideLoop(config.ability3BgmLoop);
    }

    void HandleAbility3TimeSlowEnd()
    {
        if (bgmController == null) return;
        bgmController.EndOverrideLoop();
    }

    static bool IsImpactResult(HitResultType type)
    {
        return type == HitResultType.Hit ||
               type == HitResultType.Blocked ||
               type == HitResultType.PerfectBlock ||
               type == HitResultType.GuardBreak;
    }

    void PlayOneShot(AudioClip clip)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip, oneShotVolume);
    }
}
