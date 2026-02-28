using UnityEngine;

public class FrameRateConfig : MonoBehaviour
{
    [SerializeField] int targetFps = 60;
    [SerializeField] bool disableVsync = true;

    void Awake()
    {
        if (disableVsync) QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFps;
    }
}