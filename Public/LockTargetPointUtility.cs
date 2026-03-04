using UnityEngine;

public static class LockTargetPointUtility
{
    // 统一约定：所有可锁定目标如需精确锁定点，请在目标层级下放置一个名为 LockPoint 的空物体。
    const string LockPointName = "LockPoint";

    public static Vector3 GetCapsuleCenter(Transform target)
    {
        if (target == null) return Vector3.zero;

        Transform root = target.root != null ? target.root : target;
        Transform lockPoint = FindDeepChildByName(root, LockPointName);
        if (lockPoint != null)
            return lockPoint.position;

        Collider col = target.GetComponentInParent<Collider>();
        if (col != null)
            return col.bounds.center;

        return root.position;
    }

    static Transform FindDeepChildByName(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrEmpty(childName)) return null;

        if (parent.name == childName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
                return child;

            Transform nested = FindDeepChildByName(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
