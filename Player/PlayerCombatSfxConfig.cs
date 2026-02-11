using System;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerCombatSfxConfig", menuName = "Audio/Player Combat SFX Config")]
public class PlayerCombatSfxConfig : ScriptableObject
{
    [Serializable]
    public struct AttackSfxEntry
    {
        public PlayerAttackSfxKey key;
        public AudioClip whoosh;
        public AudioClip impact;
    }

    [Header("Attack (12 groups)")]
    [SerializeField] AttackSfxEntry[] attackSfxEntries;

    [Header("Player as defender")]
    public AudioClip playerBlockedClip;
    public AudioClip playerPerfectBlockClip;
    public AudioClip playerGuardBreakClip;


    [Header("Abilities")]
    public AudioClip ability1Clip;
    public AudioClip ability2Clip;
    public AudioClip ability3Clip;
    public AudioClip ability4Clip;

    [Header("Ability3 BGM")]
    public AudioClip ability3BgmLoop;

    public bool TryGetWhoosh(PlayerAttackSfxKey key, out AudioClip clip)
        => TryGetEntryClip(key, true, out clip);

    public bool TryGetImpact(PlayerAttackSfxKey key, out AudioClip clip)
        => TryGetEntryClip(key, false, out clip);

    bool TryGetEntryClip(PlayerAttackSfxKey key, bool whoosh, out AudioClip clip)
    {
        clip = null;
        if (attackSfxEntries == null) return false;

        for (int i = 0; i < attackSfxEntries.Length; i++)
        {
            if (attackSfxEntries[i].key != key) continue;

            clip = whoosh ? attackSfxEntries[i].whoosh : attackSfxEntries[i].impact;
            return clip != null;
        }

        return false;
    }

    public AudioClip GetAbilityClip(PlayerAbilitySystem.AbilityType type)
    {
        return type switch
        {
            PlayerAbilitySystem.AbilityType.Ability1 => ability1Clip,
            PlayerAbilitySystem.AbilityType.Ability2 => ability2Clip,
            PlayerAbilitySystem.AbilityType.Ability3 => ability3Clip,
            PlayerAbilitySystem.AbilityType.Ability4 => ability4Clip,
            _ => null,
        };
    }
}
