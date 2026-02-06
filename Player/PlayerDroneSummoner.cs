using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDroneSummoner : MonoBehaviour
{
    [Header("Drone Prefab")]
    [SerializeField] AssistDroneController dronePrefab;

    [Header("Drone Flight")]
    [SerializeField] Vector3 hoverOffset = new Vector3(0f, 2.2f, 0f);
    [SerializeField] float lifetime = 15f;
    [SerializeField] float highAltitude = 18f;
    [SerializeField] float enterDuration = 3f;
    [SerializeField] float exitDuration = 3f;

    [Header("Cooldown")]
    [SerializeField] float summonCooldown = 60f;

    AssistDroneController currentDrone;
    float nextSummonAllowedTime;

    public bool IsOnCooldown => Time.time < nextSummonAllowedTime;
    public float CooldownRemaining => Mathf.Max(0f, nextSummonAllowedTime - Time.time);

    public bool TrySummon()
    {
        if (dronePrefab == null)
            return false;

        if (currentDrone != null)
            return false;

        if (Time.time < nextSummonAllowedTime)
            return false;

        Vector3 spawnPos = transform.position + Vector3.up * highAltitude;
        AssistDroneController instance = Instantiate(dronePrefab, spawnPos, Quaternion.identity);
        if (instance == null)
            return false;

        currentDrone = instance;
        Transform owner = transform;
        instance.Init(
            owner,
            hoverOffset,
            highAltitude,
            enterDuration,
            exitDuration,
            lifetime,
            () =>
            {
                if (currentDrone == instance)
                    currentDrone = null;
            }
        );

        nextSummonAllowedTime = Time.time + Mathf.Max(0f, summonCooldown);
        return true;
    }
}
