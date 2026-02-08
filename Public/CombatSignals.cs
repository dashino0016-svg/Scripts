using System;

public static class CombatSignals
{
    // 现有：不可防御攻击提示（用于红圈与语音）
    public static event Action<float> OnPlayerUnblockableWarning;
    public static void RaisePlayerUnblockableWarning(float duration)
        => OnPlayerUnblockableWarning?.Invoke(duration);

    // ✅ 新增：语音播报触发点（从玩家视角）
    public static event Action OnPlayerEnterCombat;
    public static void RaisePlayerEnterCombat()
        => OnPlayerEnterCombat?.Invoke();

    public static event Action OnPlayerKillEnemy;
    public static void RaisePlayerKillEnemy()
        => OnPlayerKillEnemy?.Invoke();

    public static event Action OnPlayerKilledByEnemy;
    public static void RaisePlayerKilledByEnemy()
        => OnPlayerKilledByEnemy?.Invoke();

    public static event Action OnPlayerHitEnemy;
    public static void RaisePlayerHitEnemy()
        => OnPlayerHitEnemy?.Invoke();

    public static event Action OnEnemyHitPlayer;
    public static void RaiseEnemyHitPlayer()
        => OnEnemyHitPlayer?.Invoke();

    public static event Action OnPlayerGuardBreakEnemy;
    public static void RaisePlayerGuardBreakEnemy()
        => OnPlayerGuardBreakEnemy?.Invoke();

    public static event Action OnEnemyGuardBreakPlayer;
    public static void RaiseEnemyGuardBreakPlayer()
        => OnEnemyGuardBreakPlayer?.Invoke();
}
