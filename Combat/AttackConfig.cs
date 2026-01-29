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

    [Header("Super Armor")]
    [Tooltip("勾选后：执行该攻击时，被命中不会打断当前攻击（不调用 InterruptAttack）")]
    public bool hasSuperArmor = false;
}
