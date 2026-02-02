using UnityEngine;

public class DroneSummoner : MonoBehaviour
{
    [Header("Drone Prefab (with AssistDroneController)")]
    [SerializeField] private AssistDroneController dronePrefab;

    [Header("Summon")]
    [SerializeField] private float droneLifetime = 12f;
    [SerializeField] private KeyCode summonKey = KeyCode.T; // œ»”√ T ≤‚ ‘
    [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 2.2f, 0f);

    private AssistDroneController current;

    void Update()
    {
        if (Input.GetKeyDown(summonKey))
        {
            SummonOrRefresh();
        }
    }

    public void SummonOrRefresh()
    {
        if (dronePrefab == null) return;

        if (current == null)
        {
            Vector3 pos = transform.position + spawnOffset;
            current = Instantiate(dronePrefab, pos, Quaternion.identity);
        }

        current.Init(transform, droneLifetime);
    }
}
