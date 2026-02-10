using System;

[Flags]
public enum HitBoxLimb
{
    None = 0,
    LeftHand = 1 << 0,
    RightHand = 1 << 1,
    LeftLeg = 1 << 2,
    RightLeg = 1 << 3,

    // 便于向后兼容：旧配置可先用 Hand/Leg 聚合，再逐步细分到左右。
    Hand = LeftHand | RightHand,
    Leg = LeftLeg | RightLeg,
    All = Hand | Leg,
}

