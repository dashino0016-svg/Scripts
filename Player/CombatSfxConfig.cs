using System;
using UnityEngine;

[CreateAssetMenu(fileName = "CombatSfxConfig", menuName = "Audio/Combat SFX Config")]
public class CombatSfxConfig : ScriptableObject
{
    [Serializable]
    public struct AttackSfxEntry
    {
        public CombatSfxAttackGroup group;
        [Min(1)] public int variant;
        public AudioClip whoosh;
        public AudioClip impact;
    }

    [Serializable]
    public struct AbilitySfxEntry
    {
        [Min(1)] public int abilityId;
        public AudioClip clip;
    }

    [Header("Attack entries")]
    [SerializeField] AttackSfxEntry[] attackSfxEntries;

    [Header("Combo config")]
    [SerializeField, Min(0)] int comboCountA = 4;
    [SerializeField, Min(0)] int comboCountB = 4;

    [Header("Optional attack groups")]
    [SerializeField] bool enableHeavyAttackA = true;
    [SerializeField] bool enableHeavyAttackB = true;
    [SerializeField] bool enableSprintAttackA = true;
    [SerializeField] bool enableSprintAttackB = true;

    [Header("Defender")]
    public AudioClip blockedClip;
    public AudioClip perfectBlockClip;
    public AudioClip guardBreakClip;

    [Header("Abilities")]
    [SerializeField] AbilitySfxEntry[] abilitySfxEntries;

    [Header("Ability3 BGM")]
    public AudioClip ability3BgmLoop;

    public bool TryGetWhoosh(CombatAttackSfxKey key, out AudioClip clip)
        => TryGetEntryClip(key, true, out clip);

    public bool TryGetImpact(CombatAttackSfxKey key, out AudioClip clip)
        => TryGetEntryClip(key, false, out clip);

    bool TryGetEntryClip(CombatAttackSfxKey key, bool whoosh, out AudioClip clip)
    {
        clip = null;
        if (!IsGroupEnabled(key)) return false;
        if (attackSfxEntries == null) return false;

        for (int i = 0; i < attackSfxEntries.Length; i++)
        {
            if (attackSfxEntries[i].group != key.Group) continue;
            if (Mathf.Max(1, attackSfxEntries[i].variant) != key.Variant) continue;

            clip = whoosh ? attackSfxEntries[i].whoosh : attackSfxEntries[i].impact;
            return clip != null;
        }

        return false;
    }

    bool IsGroupEnabled(CombatAttackSfxKey key)
    {
        switch (key.Group)
        {
            case CombatSfxAttackGroup.ComboA:
                return comboCountA > 0 && key.Variant <= comboCountA;
            case CombatSfxAttackGroup.ComboB:
                return comboCountB > 0 && key.Variant <= comboCountB;
            case CombatSfxAttackGroup.HeavyAttackA:
                return enableHeavyAttackA;
            case CombatSfxAttackGroup.HeavyAttackB:
                return enableHeavyAttackB;
            case CombatSfxAttackGroup.SprintAttackA:
                return enableSprintAttackA;
            case CombatSfxAttackGroup.SprintAttackB:
                return enableSprintAttackB;
            default:
                return false;
        }
    }

    public AudioClip GetAbilityClip(int abilityId)
    {
        if (abilityId <= 0 || abilitySfxEntries == null)
            return null;

        for (int i = 0; i < abilitySfxEntries.Length; i++)
        {
            if (abilitySfxEntries[i].abilityId != abilityId)
                continue;

            return abilitySfxEntries[i].clip;
        }

        return null;
    }
}
