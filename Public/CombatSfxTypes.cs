using UnityEngine;

public enum CombatSfxAttackGroup
{
    ComboA,
    ComboB,
    HeavyAttackA,
    HeavyAttackB,
    SprintAttackA,
    SprintAttackB,
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
            case AttackSourceType.SprintAttackA:
            case AttackSourceType.RunAttackA:
            case AttackSourceType.AirAttackA:
                key = new CombatAttackSfxKey(CombatSfxAttackGroup.SprintAttackA, 1);
                return true;
            case AttackSourceType.SprintAttackB:
            case AttackSourceType.RunAttackB:
            case AttackSourceType.AirAttackB:
                key = new CombatAttackSfxKey(CombatSfxAttackGroup.SprintAttackB, 1);
                return true;
            default:
                return false;
        }
    }

    public static int ToAbilityId(PlayerAbilitySystem.AbilityType abilityType)
        => (int)abilityType + 1;
}
