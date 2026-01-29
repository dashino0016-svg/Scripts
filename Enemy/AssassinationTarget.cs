using UnityEngine;

public class AssassinationTarget : MonoBehaviour
{
    [Header("Config")]
    public bool canBeAssassinated = true;
    public bool canBeExecuted = true;

    [Header("Anchors")]
    [Tooltip("暗杀贴合用锚点（可选）。空则回退到自身 transform。")]
    public Transform assassinateAnchor;

    [Tooltip("处决贴合用锚点（可选）。空则回退到暗杀锚点，再回退到自身 transform。")]
    public Transform executeAnchor;

    [Header("Range")]
    public float maxDistance = 1.5f;

    public Transform GetAnchorOrSelf(bool forExecute)
    {
        if (forExecute)
        {
            if (executeAnchor != null) return executeAnchor;
            if (assassinateAnchor != null) return assassinateAnchor;
            return transform;
        }
        else
        {
            if (assassinateAnchor != null) return assassinateAnchor;
            return transform;
        }
    }
}
