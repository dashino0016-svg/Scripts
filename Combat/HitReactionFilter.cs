using UnityEngine;

public class HitReactionFilter : MonoBehaviour
{
    [Header("Convert ignored reactions to NoHit")]
    [Tooltip("把被忽略的受击类型转换为 NoHit（=不播受击/不打断/不HitStop/不触发HitLock），但仍扣血、仍进战斗。")]
    public bool convertToNoHit = true;

    [Header("Ignore which reactions")]
    public bool ignoreLight = true;
    public bool ignoreMid = true;
    public bool ignoreHeavy = false;

    public HitReactionType Filter(HitReactionType reaction)
    {
        if (reaction == HitReactionType.NoHit) return HitReactionType.NoHit;

        bool ignored =
            (ignoreLight && reaction == HitReactionType.Light) ||
            (ignoreMid && reaction == HitReactionType.Mid) ||
            (ignoreHeavy && reaction == HitReactionType.Heavy);

        if (!ignored) return reaction;

        return convertToNoHit ? HitReactionType.NoHit : reaction;
    }
}
