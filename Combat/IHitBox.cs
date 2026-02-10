public interface IHitBox
{
    void EnableHitBox(AttackData attackData);
    void DisableHitBox();
}

public interface ILimbTypedHitBox : IHitBox
{
    HitBoxLimb Limb { get; }
}

public interface ILimbTypedHitBox : IHitBox
{
    HitBoxLimb Limb { get; }
}
