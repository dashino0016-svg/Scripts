using System;

public struct CombatSfxHitContext
{
    public readonly AttackData AttackData;
    public readonly HitResultType ResultType;
    public readonly bool AttackerIsPlayer;
    public readonly bool ReceiverIsPlayer;

    public CombatSfxHitContext(AttackData attackData, HitResultType resultType, bool attackerIsPlayer, bool receiverIsPlayer)
    {
        AttackData = attackData;
        ResultType = resultType;
        AttackerIsPlayer = attackerIsPlayer;
        ReceiverIsPlayer = receiverIsPlayer;
    }
}

public static class CombatSfxSignals
{
    public static event Action<PlayerAttackSfxKey> OnPlayerAttackWhoosh;
    public static event Action<CombatSfxHitContext> OnHitResolved;
    public static event Action<PlayerAbilitySystem.AbilityType> OnPlayerAbilityTriggered;
    public static event Action OnPlayerAbility3TimeSlowBegin;
    public static event Action OnPlayerAbility3TimeSlowEnd;

    public static void RaisePlayerAttackWhoosh(PlayerAttackSfxKey key)
        => OnPlayerAttackWhoosh?.Invoke(key);

    public static void RaiseHitResolved(AttackData attackData, HitResultType resultType, bool attackerIsPlayer, bool receiverIsPlayer)
        => OnHitResolved?.Invoke(new CombatSfxHitContext(attackData, resultType, attackerIsPlayer, receiverIsPlayer));

    public static void RaisePlayerAbilityTriggered(PlayerAbilitySystem.AbilityType abilityType)
        => OnPlayerAbilityTriggered?.Invoke(abilityType);

    public static void RaisePlayerAbility3TimeSlowBegin()
        => OnPlayerAbility3TimeSlowBegin?.Invoke();

    public static void RaisePlayerAbility3TimeSlowEnd()
        => OnPlayerAbility3TimeSlowEnd?.Invoke();
}
