using UnityEngine;

[System.Serializable]
public class AttackData
{
    public Transform attacker;
    public AttackSourceType sourceType;
    public HitReactionType hitReaction;

    [Header("Damage")]
    public int hpDamage;
    public int staminaDamage;

    [Header("Rules")]
    public bool canBeBlocked = true;
    public bool canBeParried = true;
    public bool canBreakGuard = false;

    [Header("Super Armor")]
    public bool hasSuperArmor = false;

    [Header("Special Gain")]
    public int specialGainOnHit = 0;
    public int specialGainOnPerfectBlock = 0;

    public AttackData(
        Transform attacker,
        AttackSourceType sourceType,
        HitReactionType hitReaction,
        int hpDamage,
        int staminaDamage
    )
    {
        this.attacker = attacker;
        this.sourceType = sourceType;
        this.hitReaction = hitReaction;
        this.hpDamage = hpDamage;
        this.staminaDamage = staminaDamage;
    }
}
