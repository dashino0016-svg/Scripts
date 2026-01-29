using UnityEngine;

public struct HitResult
{
    public HitResultType resultType;

    // ⭐ 只在 HitResultType.Hit 时有意义
    public HitReactionType reactionType;

    public HitResult(HitResultType type, HitReactionType reaction = HitReactionType.Light)
    {
        resultType = type;
        reactionType = reaction;
    }
}
