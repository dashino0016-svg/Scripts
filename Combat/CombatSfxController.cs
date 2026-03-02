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

    [Header("Time Slow")]
    [SerializeField] bool slowSfxWhenOwnerTimeSlowed = true;
    [SerializeField, Range(0.05f, 1f)] float minSlowedPitch = 0.35f;

    EnemyController enemyOwner;

    void Awake()
    {
        if (sfxSource == null)
            sfxSource = GetComponent<AudioSource>();

        if (bgmController == null)
            bgmController = FindObjectOfType<BgmController>();

        enemyOwner = GetComponent<EnemyController>();
    }

    void Update()
    {
        if (sfxSource == null)
            return;

        if (!slowSfxWhenOwnerTimeSlowed || enemyOwner == null)
        {
            sfxSource.pitch = 1f;
            return;
        }

        float targetPitch = enemyOwner.LocalTimeScale < 0.999f
            ? Mathf.Clamp(enemyOwner.LocalTimeScale, minSlowedPitch, 1f)
            : 1f;

        sfxSource.pitch = targetPitch;
    }

    void OnEnable()
    {
        CombatSfxSignals.OnAttackWhoosh += HandleWhoosh;
        CombatSfxSignals.OnHitResolved += HandleHitResolved;
        CombatSfxSignals.OnAbilityTriggered += HandleAbilityTriggered;
        CombatSfxSignals.OnAbility3TimeSlowBegin += HandleAbility3TimeSlowBegin;
        CombatSfxSignals.OnAbility3TimeSlowEnd += HandleAbility3TimeSlowEnd;
        CombatSfxSignals.OnTakedownStart += HandleTakedownStart;
    }

    void OnDisable()
    {
        CombatSfxSignals.OnAttackWhoosh -= HandleWhoosh;
        CombatSfxSignals.OnHitResolved -= HandleHitResolved;
        CombatSfxSignals.OnAbilityTriggered -= HandleAbilityTriggered;
        CombatSfxSignals.OnAbility3TimeSlowBegin -= HandleAbility3TimeSlowBegin;
        CombatSfxSignals.OnAbility3TimeSlowEnd -= HandleAbility3TimeSlowEnd;
        CombatSfxSignals.OnTakedownStart -= HandleTakedownStart;
    }

    void HandleWhoosh(CombatAttackSfxKey key, Transform emitter)
    {
        if (emitter != transform) return;
        if (config == null) return;
        if (!config.TryGetWhoosh(key, out var clip)) return;
        PlayOneShot(clip);
    }

    void HandleHitResolved(CombatSfxHitContext ctx)
    {
        if (ctx.Attacker != transform && ctx.Receiver != transform) return;
        if (config == null) return;

        bool hasDefenderSfx = ctx.ResultType == HitResultType.Blocked ||
                              ctx.ResultType == HitResultType.PerfectBlock ||
                              ctx.ResultType == HitResultType.GuardBreak;

        bool allowImpact = !(suppressAttackImpactWhenDefending && hasDefenderSfx);
        bool isAttacker = ctx.Attacker == transform;
        bool isReceiver = ctx.Receiver == transform;

        if (isAttacker && allowImpact && IsImpactResult(ctx.ResultType) &&
            CombatSfxKeyUtility.TryGetAttackKey(ctx.AttackData, out var key) &&
            config.TryGetImpact(key, out var impactClip))
        {
            PlayOneShot(impactClip);
        }

        if (isReceiver && hasDefenderSfx)
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

    void HandleTakedownStart(CombatSfxTakedownType type, Transform emitter)
    {
        if (emitter != transform) return;
        if (config == null) return;
        PlayOneShot(config.GetTakedownClip(type));
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
