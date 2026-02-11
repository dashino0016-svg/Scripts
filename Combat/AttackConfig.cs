using UnityEngine;

[CreateAssetMenu(menuName = "Combat/AttackConfig")]
public class AttackConfig : ScriptableObject
{
    [Header("Identity")]
    public AttackSourceType sourceType;

    [Header("Combo")]
    public int comboIndex;

    [Header("Hit Reaction (Non-Block)")]
    public HitReactionType hitReaction;

    [Header("Active Limbs")]
    [Tooltip("本段攻击允许生效的肢体判定。可选左右手/左右腿；默认 All 保持旧行为。")]
    public HitBoxLimb activeLimbMask = HitBoxLimb.All;

    [Header("Damage")]
    public int hpDamage;
    public int staminaDamage;
    [Tooltip("命中防御目标（造成体力伤害）时，额外造成的生命连带伤害。")]
    public int hpPenetrationDamage;
    [Tooltip("命中未防御目标（造成生命伤害）时，额外造成的体力连带伤害。")]
    public int staminaPenetrationDamage;

    [Header("Rules")]
    public bool canBeBlocked = true;
    public bool canBeParried = true;
    public bool canBreakGuard = false;

    [Header("Hit Stop")]
    [Tooltip("Hit stop weight (1 = default, <1 lighter, >1 heavier).")]
    [Min(0f)] public float hitStopWeight = 1f;

    [Header("Super Armor")]
    [Tooltip("勾选后：执行该攻击时，被命中不会打断当前攻击（不调用 InterruptAttack）")]
    public bool hasSuperArmor = false;
}
