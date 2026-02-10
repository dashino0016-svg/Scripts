public enum AttackSourceType
{
    AttackA = 0,
    AttackB = 1,

    RunAttackA = 2,
    RunAttackB = 3,

    SprintAttackA = 4,
    SprintAttackB = 5,

    HeavyAttackA = 6,
    HeavyAttackB = 7,

    Ability1 = 8,
    Ability2 = 9,
    // ✅ 远程（追加，不破坏旧资源）
    RangeShot = 10,

    AirAttackA = 11,
    AirAttackB = 12,
}

public enum AbilityType
{
    // ✅ MeleeFighter 里用于“攻击型能力”选择（不包含时间暂停/回血）
    Ability1 = 0,
    Ability2 = 1,
}

public enum HitReactionType
{
    Light,
    Mid,
    Heavy,
    NoHit
}

public enum HitResultType
{
    PerfectBlock,
    Blocked,
    GuardBreak,
    Hit
}