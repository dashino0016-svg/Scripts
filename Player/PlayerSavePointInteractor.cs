using UnityEngine;

public class PlayerSavePointInteractor : MonoBehaviour
{
    [SerializeField] KeyCode interactKey = KeyCode.E;

    SavePoint currentSavePoint;
    SwordController sword;
    PlayerController playerController;

    void Awake()
    {
        sword = GetComponentInChildren<SwordController>();
        playerController = GetComponent<PlayerController>();
    }

    void Update()
    {
        if (!Input.GetKeyDown(interactKey)) return;
        if (currentSavePoint == null) return;
        if (SavePointManager.Instance == null) return;
        if (TimeController.Instance != null && TimeController.Instance.IsPaused) return;
        if (sword != null && sword.IsArmed) return;
        if (playerController != null && playerController.IsInActionLock) return;

        SavePointManager.Instance.BeginSaveFlow(currentSavePoint);
    }

    void OnTriggerEnter(Collider other)
    {
        SavePoint sp = other.GetComponentInParent<SavePoint>();
        if (sp != null)
            currentSavePoint = sp;
    }

    void OnTriggerExit(Collider other)
    {
        SavePoint sp = other.GetComponentInParent<SavePoint>();
        if (sp != null && sp == currentSavePoint)
            currentSavePoint = null;
    }
}
