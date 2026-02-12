using UnityEngine;

public enum CombatSfxAttackGroup
{
    ComboA,
    ComboB,
    HeavyAttackA,
    HeavyAttackB,
    RunAttackA,
    RunAttackB,
    SprintAttackA,
    SprintAttackB,
    AirAttackA,
    AirAttackB,
}

public readonly struct CombatAttackSfxKey
{
    public readonly CombatSfxAttackGroup Group;
    public readonly int Variant;

    public CombatAttackSfxKey(CombatSfxAttackGroup group, int variant)
    {
        Group = group;
        Variant = Mathf.Max(1, variant);
    }
}

public static class CombatSfxKeyUtility
{
    public static bool TryGetAttackKey(AttackData attackData, out CombatAttackSfxKey key)
    {
        key = new CombatAttackSfxKey(CombatSfxAttackGroup.ComboA, 1);
        if (attackData == null) return false;

        switch (attackData.sourceType)
        {
            case AttackSourceType.AttackA:
                key = new CombatAttackSfxKey(CombatSfxAttackGroup.ComboA, attackData.attackSfxVariant);
                return true;
            case AttackSourceType.AttackB:
                key = new CombatAttackSfxKey(CombatSfxAttackGroup.ComboB, attackData.attackSfxVariant);
                return true;
            case AttackSourceType.HeavyAttackA:
                key = new CombatAttackSfxKey(CombatSfxAttackGroup.HeavyAttackA, 1);
                return true;
            case AttackSourceType.HeavyAttackB:
                key = new CombatAttackSfxKey(CombatSfxAttackGroup.HeavyAttackB, 1);
                return true;
            case AttackSourceType.RunAttackA:
                key = new CombatAttackSfxKey(CombatSfxAttackGroup.RunAttackA, 1);
                return true;
            case AttackSourceType.RunAttackB:
                key = new CombatAttackSfxKey(CombatSfxAttackGroup.RunAttackB, 1);
                return true;
            case AttackSourceType.SprintAttackA:
                key = new CombatAttackSfxKey(CombatSfxAttackGroup.SprintAttackA, 1);
                return true;
            case AttackSourceType.SprintAttackB:
                key = new CombatAttackSfxKey(CombatSfxAttackGroup.SprintAttackB, 1);
                return true;
            case AttackSourceType.AirAttackA:
                key = new CombatAttackSfxKey(CombatSfxAttackGroup.AirAttackA, 1);
                return true;
            case AttackSourceType.AirAttackB:
                key = new CombatAttackSfxKey(CombatSfxAttackGroup.AirAttackB, 1);
                return true;
            default:
                return false;
        }
    }

    public static int ToAbilityId(PlayerAbilitySystem.AbilityType abilityType)
        => (int)abilityType + 1;
}
