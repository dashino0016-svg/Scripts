using UnityEngine;

[DisallowMultipleComponent]
public class PlayerCombatSfxController : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] PlayerCombatSfxConfig config;

    [Header("Playback")]
    [SerializeField] AudioSource sfxSource;
    [SerializeField, Range(0f, 1f)] float oneShotVolume = 1f;

    [Header("Conflicts")]
    [SerializeField] bool suppressPlayerAttackImpactWhenPlayerDefending;

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
        CombatSfxSignals.OnPlayerAttackWhoosh += HandleWhoosh;
        CombatSfxSignals.OnHitResolved += HandleHitResolved;
        CombatSfxSignals.OnPlayerAbilityTriggered += HandleAbilityTriggered;
        CombatSfxSignals.OnPlayerAbility3TimeSlowBegin += HandleAbility3TimeSlowBegin;
        CombatSfxSignals.OnPlayerAbility3TimeSlowEnd += HandleAbility3TimeSlowEnd;
    }

    void OnDisable()
    {
        CombatSfxSignals.OnPlayerAttackWhoosh -= HandleWhoosh;
        CombatSfxSignals.OnHitResolved -= HandleHitResolved;
        CombatSfxSignals.OnPlayerAbilityTriggered -= HandleAbilityTriggered;
        CombatSfxSignals.OnPlayerAbility3TimeSlowBegin -= HandleAbility3TimeSlowBegin;
        CombatSfxSignals.OnPlayerAbility3TimeSlowEnd -= HandleAbility3TimeSlowEnd;
    }

    void HandleWhoosh(PlayerAttackSfxKey key)
    {
        if (config == null) return;
        if (!config.TryGetWhoosh(key, out var clip)) return;
        PlayOneShot(clip);
    }

    void HandleHitResolved(CombatSfxHitContext ctx)
    {
        if (config == null) return;

        bool hasPlayerDefenderSfx = ctx.ReceiverIsPlayer &&
                                   (ctx.ResultType == HitResultType.Blocked ||
                                    ctx.ResultType == HitResultType.PerfectBlock ||
                                    ctx.ResultType == HitResultType.GuardBreak);

        bool allowPlayerImpact = !(suppressPlayerAttackImpactWhenPlayerDefending && hasPlayerDefenderSfx);
        if (allowPlayerImpact && ctx.AttackerIsPlayer && IsImpactResult(ctx.ResultType) &&
            PlayerAttackSfxKeyUtility.TryGetKey(ctx.AttackData, out var key) &&
            config.TryGetImpact(key, out var impactClip))
        {
            PlayOneShot(impactClip);
        }

        if (hasPlayerDefenderSfx)
        {
            AudioClip defenderClip = ctx.ResultType switch
            {
                HitResultType.Blocked => config.playerBlockedClip,
                HitResultType.PerfectBlock => config.playerPerfectBlockClip,
                HitResultType.GuardBreak => config.playerGuardBreakClip,
                _ => null,
            };

            PlayOneShot(defenderClip);
        }

        if (ctx.AttackerIsPlayer && !ctx.ReceiverIsPlayer && ctx.ResultType == HitResultType.GuardBreak)
        {
            PlayOneShot(config.playerGuardBreakEnemyClip);
        }
    }

    void HandleAbilityTriggered(PlayerAbilitySystem.AbilityType abilityType)
    {
        if (config == null) return;
        PlayOneShot(config.GetAbilityClip(abilityType));
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
