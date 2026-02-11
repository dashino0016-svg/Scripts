using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public abstract class AttackHitBoxBase : MonoBehaviour, ILimbTypedHitBox
{
    [Header("Limb")]
    [SerializeField] HitBoxLimb limb = HitBoxLimb.All;

    Collider col;
    AttackData currentAttack;
    readonly HashSet<IHittable> hitTargets = new HashSet<IHittable>();
    MeleeFighter fighter;

    public HitBoxLimb Limb => limb;

    protected MeleeFighter Fighter => fighter;

    protected virtual void Awake()
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

        if (!CanHitTarget(other, hittable, currentAttack))
            return;

        // Local guard (same hitbox) + shared guard (across all hitboxes of this attacker).
        if (hitTargets.Contains(hittable)) return;
        if (fighter != null && !fighter.TryRegisterHit(hittable)) return;

        hitTargets.Add(hittable);
        hittable.OnHit(currentAttack);

        OnHitConfirmed(currentAttack);
    }

    protected virtual bool CanHitTarget(Collider other, IHittable hittable, AttackData attackData)
    {
        return true;
    }

    protected virtual void OnHitConfirmed(AttackData attackData)
    {
    }
}
