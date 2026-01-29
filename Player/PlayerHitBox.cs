using UnityEngine;
using System.Collections.Generic;

public enum HitBoxType
{
    A,
    B
}

[RequireComponent(typeof(Collider))]
public class PlayerHitBox : MonoBehaviour, IAttackTypedHitBox
{
    Collider col;

    [Header("HitBox Type")]
    public HitBoxType hitBoxType;

    AttackData currentAttack;
    HashSet<IHittable> hitTargets = new HashSet<IHittable>();

    MeleeFighter fighter;

    public HitBoxType HitBoxType => hitBoxType;

    void Awake()
    {
        col = GetComponent<Collider>();
        col.isTrigger = true;
        col.enabled = false;

        // Root usually owns MeleeFighter; this lets multiple hitboxes share a single "already hit" registry.
        fighter = GetComponentInParent<MeleeFighter>();
    }

    public void EnableHitBox(AttackData attackData)
    {
        currentAttack = attackData;
        hitTargets.Clear();
        col.enabled = true;
    }

    public void DisableHitBox()
    {
        col.enabled = false;
        currentAttack = null;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!col.enabled || currentAttack == null)
            return;

        if (other.transform.root == transform.root)
            return;

        // Support multiple colliders on the target (IHittable usually lives on the root).
        var hittable = other.GetComponentInParent<IHittable>();
        if (hittable == null) return;

        // Local guard (same hitbox) + shared guard (across all hitboxes of this attacker).
        if (hitTargets.Contains(hittable)) return;
        if (fighter != null && !fighter.TryRegisterHit(hittable)) return;

        hitTargets.Add(hittable);
        hittable.OnHit(currentAttack);
    }
}
