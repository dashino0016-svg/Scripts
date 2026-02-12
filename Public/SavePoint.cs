using UnityEngine;

public class SavePoint : MonoBehaviour
{
    [Header("Anchors")]
    [Tooltip("Player align anchor for entering save flow.")]
    public Transform interactAnchor;

    [Tooltip("Optional respawn anchor for future death rewind. If empty, use interactAnchor.")]
    public Transform respawnAnchor;

    public Transform RespawnAnchor => respawnAnchor != null ? respawnAnchor : interactAnchor;
}
