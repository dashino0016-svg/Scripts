using UnityEngine;

public class EnemyHitBox : AttackHitBoxBase
{
    protected override bool CanHitTarget(Collider other, IHittable hittable, AttackData attackData)
    {
        // 仅对近战关闭同阵营友伤；远程攻击（RangeShot）保持原有友伤逻辑。
        if (attackData == null || attackData.sourceType == AttackSourceType.RangeShot)
            return true;

        EnemyController selfEnemy = GetComponentInParent<EnemyController>();
        if (selfEnemy == null)
            return true;

        EnemyController targetEnemy = other.GetComponentInParent<EnemyController>();
        if (targetEnemy == null)
            return true;

        // 同层视为同阵营：近战不友伤；不同层为敌对阵营，允许互相近战命中。
        int selfLayer = selfEnemy.gameObject.layer;
        int targetLayer = targetEnemy.gameObject.layer;
        return selfLayer != targetLayer;
        if (targetEnemy != null)
            return false;

        return true;
    }

    protected override void OnHitConfirmed(AttackData attackData)
    {
        if (Fighter != null)
            Fighter.NotifyHitLanded(attackData);
    }
}
