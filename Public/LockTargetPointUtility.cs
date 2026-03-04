using UnityEngine;

public static class LockTargetPointUtility
{
    const string LockPointName = "LockPoint";

    public static Vector3 GetLockPoint(Transform target)
    {
        if (target == null) return Vector3.zero;

        Transform lockPoint = FindLockPoint(target);
        return lockPoint != null ? lockPoint.position : target.position;
    }

    // 兼容旧调用：统一改为 LockPoint 逻辑。
    public static Vector3 GetCapsuleCenter(Transform target)
    {
        return GetLockPoint(target);
    }

    static Transform FindLockPoint(Transform target)
    {
        Transform root = target.root;
        if (root == null) root = target;

        Transform lockPoint = FindChildRecursive(root, LockPointName);
        if (lockPoint != null) return lockPoint;

        return FindChildRecursive(target, LockPointName);
    }

    static Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent == null) return null;

        if (parent.name == childName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildRecursive(parent.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }
}
