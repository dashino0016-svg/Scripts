using UnityEngine;

public static class LockTargetPointUtility
{
    public static Vector3 GetCapsuleCenter(Transform target)
    {
        if (target == null) return Vector3.zero;

        // 用 CharacterController.center 计算世界坐标中心点：
        // cc.enabled=false 时也稳定；不会像 cc.bounds.center 那样掉到脚底/地面下
        CharacterController cc = target.GetComponentInParent<CharacterController>();
        if (cc != null)
        {
            return cc.transform.TransformPoint(cc.center);
        }

        // fallback
        return target.position;
    }
}
