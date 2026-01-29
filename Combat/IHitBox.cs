public interface IHitBox
{
    void EnableHitBox(AttackData attackData);
    void DisableHitBox();
}

// Optional extension: allows MeleeFighter to enable only matching hitboxes for A/B attacks.
// HitBoxType enum is defined as a public enum (currently in PlayerHitBox.cs).
public interface IAttackTypedHitBox : IHitBox
{
    HitBoxType HitBoxType { get; }
}
