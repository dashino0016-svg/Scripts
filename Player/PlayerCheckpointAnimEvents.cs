using UnityEngine;

public class PlayerCheckpointAnimEvents : MonoBehaviour
{
    public void Checkpoint_SaveEnd()
    {
        if (SavePointManager.Instance != null)
            SavePointManager.Instance.NotifySaveAnimEnd();
    }

    public void Checkpoint_ExitEnd()
    {
        if (SavePointManager.Instance != null)
            SavePointManager.Instance.NotifyExitAnimEnd();
    }
}
