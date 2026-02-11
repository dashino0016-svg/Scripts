using UnityEngine;

public enum PlayerAttackSfxKey
{
    A1,
    A2,
    A3,
    A4,
    B1,
    B2,
    B3,
    B4,
    HeavyAttackA,
    HeavyAttackB,
    MoveA,
    MoveB,
}

public static class PlayerAttackSfxKeyUtility
{
    public static bool TryGetKey(AttackData attackData, out PlayerAttackSfxKey key)
    {
        key = PlayerAttackSfxKey.A1;
        if (attackData == null) return false;

        switch (attackData.sourceType)
        {
            case AttackSourceType.AttackA:
                return TryGetComboKey(true, attackData.attackSfxVariant, out key);
            case AttackSourceType.AttackB:
                return TryGetComboKey(false, attackData.attackSfxVariant, out key);
            case AttackSourceType.HeavyAttackA:
                key = PlayerAttackSfxKey.HeavyAttackA;
                return true;
            case AttackSourceType.HeavyAttackB:
                key = PlayerAttackSfxKey.HeavyAttackB;
                return true;
            case AttackSourceType.RunAttackA:
            case AttackSourceType.SprintAttackA:
            case AttackSourceType.AirAttackA:
                key = PlayerAttackSfxKey.MoveA;
                return true;
            case AttackSourceType.RunAttackB:
            case AttackSourceType.SprintAttackB:
            case AttackSourceType.AirAttackB:
                key = PlayerAttackSfxKey.MoveB;
                return true;
            default:
                return false;
        }
    }

    static bool TryGetComboKey(bool attackA, int comboIndex, out PlayerAttackSfxKey key)
    {
        key = attackA ? PlayerAttackSfxKey.A1 : PlayerAttackSfxKey.B1;

        int idx = Mathf.Clamp(comboIndex, 1, 4);
        if (attackA)
        {
            key = idx switch
            {
                1 => PlayerAttackSfxKey.A1,
                2 => PlayerAttackSfxKey.A2,
                3 => PlayerAttackSfxKey.A3,
                _ => PlayerAttackSfxKey.A4
            };
            return true;
        }

        key = idx switch
        {
            1 => PlayerAttackSfxKey.B1,
            2 => PlayerAttackSfxKey.B2,
            3 => PlayerAttackSfxKey.B3,
            _ => PlayerAttackSfxKey.B4
        };
        return true;
    }
}
