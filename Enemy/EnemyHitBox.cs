using UnityEngine;

public class EnemyHitBox : AttackHitBoxBase
{
    protected override void OnHitConfirmed(AttackData attackData)
    {
        if (Fighter != null)
            Fighter.NotifyHitLanded(attackData);
    }
}
