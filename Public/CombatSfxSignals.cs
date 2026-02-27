using UnityEngine;
using System;

public struct CombatSfxHitContext
{
    public readonly AttackData AttackData;
    public readonly HitResultType ResultType;
    public readonly bool AttackerIsPlayer;
    public readonly bool ReceiverIsPlayer;
    public readonly Transform Attacker;
    public readonly Transform Receiver;

    public CombatSfxHitContext(AttackData attackData, HitResultType resultType, bool attackerIsPlayer, bool receiverIsPlayer, Transform attacker, Transform receiver)
    {
        AttackData = attackData;
        ResultType = resultType;
        AttackerIsPlayer = attackerIsPlayer;
        ReceiverIsPlayer = receiverIsPlayer;
        Attacker = attacker;
        Receiver = receiver;
    }
}

public static class CombatSfxSignals
{
    public static event Action<CombatAttackSfxKey, Transform> OnAttackWhoosh;
    public static event Action<CombatSfxHitContext> OnHitResolved;
    public static event Action<int> OnAbilityTriggered;
    public static event Action OnAbility3TimeSlowBegin;
    public static event Action OnAbility3TimeSlowEnd;

    public static void RaiseAttackWhoosh(CombatAttackSfxKey key, Transform emitter)
        => OnAttackWhoosh?.Invoke(key, emitter);

    public static void RaiseHitResolved(AttackData attackData, HitResultType resultType, bool attackerIsPlayer, bool receiverIsPlayer, Transform attacker, Transform receiver)
        => OnHitResolved?.Invoke(new CombatSfxHitContext(attackData, resultType, attackerIsPlayer, receiverIsPlayer, attacker, receiver));

    public static void RaiseAbilityTriggered(int abilityId)
        => OnAbilityTriggered?.Invoke(abilityId);

    public static void RaiseAbility3TimeSlowBegin()
        => OnAbility3TimeSlowBegin?.Invoke();

    public static void RaiseAbility3TimeSlowEnd()
        => OnAbility3TimeSlowEnd?.Invoke();
}
