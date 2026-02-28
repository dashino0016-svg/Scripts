using UnityEngine;

[System.Serializable]
public class AttackData
{
    public Transform attacker;
    public AttackSourceType sourceType;
    public HitReactionType hitReaction;
    public HitBoxLimb activeLimbMask;

    [Header("Damage")]
    public int hpDamage;
    public int staminaDamage;
    public int staminaPenetrationDamage;
    public int hpPenetrationDamage;

    [Header("Rules")]
    public bool canBeBlocked = true;
    public bool canBeParried = true;
    public bool canBreakGuard = false;

    [Header("Super Armor")]
    public bool hasSuperArmor = false;

    [Header("Hit Stop")]
    public float hitStopWeight = 1f;

    [Header("Special Gain")]
    public int specialGainOnHit = 0;
    public int specialGainOnPerfectBlock = 0;
    public bool grantSpecialToAttackerOnHit = true;

    [Header("SFX")]
    public int attackSfxVariant = 1;

    public AttackData(
        Transform attacker,
        AttackSourceType sourceType,
        HitReactionType hitReaction,
        int hpDamage,
        int staminaDamage,
        HitBoxLimb activeLimbMask = HitBoxLimb.All,
        int staminaPenetrationDamage = 0,
        int hpPenetrationDamage = 0
    )
    {
        this.attacker = attacker;
        this.sourceType = sourceType;
        this.hitReaction = hitReaction;
        this.hpDamage = hpDamage;
        this.staminaDamage = staminaDamage;
        this.activeLimbMask = activeLimbMask;
        this.staminaPenetrationDamage = staminaPenetrationDamage;
        this.hpPenetrationDamage = hpPenetrationDamage;
    }
}
