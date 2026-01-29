using UnityEngine;

public static class LockTargetPointUtility
{
    public static Vector3 GetCapsuleCenter(Transform target)
    {
        if (target == null) return Vector3.zero;

        CharacterController cc = target.GetComponentInParent<CharacterController>();
        if (cc != null) return cc.bounds.center;

        return target.position;
    }
}
