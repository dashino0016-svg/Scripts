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

    [Header("Damage")]
    public int hpDamage;
    public int staminaDamage;

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
