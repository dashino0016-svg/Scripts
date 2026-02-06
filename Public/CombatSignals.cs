using System;

public static class CombatSignals
{
    public static event Action<float> OnPlayerUnblockableWarning;
    public static void RaisePlayerUnblockableWarning(float duration)
        => OnPlayerUnblockableWarning?.Invoke(duration);
}
