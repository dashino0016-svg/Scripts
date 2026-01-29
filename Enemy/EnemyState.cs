using System;
using UnityEngine;

public enum EnemyStateType
{
    NotCombat,
    Combat,
    LostTarget,
    Hit,
    Dead
}

public enum LostTargetPhase
{
    Armed,
    ReturnHome
}

public class EnemyState : MonoBehaviour
{
    public EnemyStateType Current { get; private set; } = EnemyStateType.NotCombat;
    public LostTargetPhase LostTargetPhase { get; private set; } = LostTargetPhase.Armed;

    public event Action<EnemyStateType, EnemyStateType> OnStateChanged;

    public bool IsInCombat => Current == EnemyStateType.Combat;

    public bool IsNotCombat => Current == EnemyStateType.NotCombat;

    public bool IsDead => Current == EnemyStateType.Dead;

    // =========================
    // ✅ Global combat presence
    // =========================
    static int globalCombatCount = 0;
    bool countedAsCombat;

    public static bool AnyEnemyInCombat => globalCombatCount > 0;

    void OnDestroy()
    {
        // ✅ 防止对象销毁时计数残留
        if (countedAsCombat)
        {
            globalCombatCount = Mathf.Max(0, globalCombatCount - 1);
            countedAsCombat = false;
        }
    }

    void ForceCountEnterCombatIfNeeded()
    {
        if (countedAsCombat) return;
        countedAsCombat = true;
        globalCombatCount++;
    }

    void ForceCountExitCombatIfNeeded()
    {
        if (!countedAsCombat) return;
        countedAsCombat = false;
        globalCombatCount = Mathf.Max(0, globalCombatCount - 1);
    }

    public void ForceEnterCombat()
    {
        if (Current == EnemyStateType.Dead)
            return;

        if (Current == EnemyStateType.Combat)
            return;

        ChangeState(EnemyStateType.Combat);
    }

    public void EnterCombat()
    {
        if (Current == EnemyStateType.Dead)
            return;

        ChangeState(EnemyStateType.Combat);
    }

    public void EnterLostTarget()
    {
        if (Current == EnemyStateType.Dead)
            return;

        ChangeState(EnemyStateType.LostTarget);
        SetLostTargetPhase(LostTargetPhase.Armed);
    }

    public void OnSheathSwordEnd()
    {
        if (Current == EnemyStateType.LostTarget)
            SetLostTargetPhase(LostTargetPhase.ReturnHome);
    }

    public void OnReturnHomeReached()
    {
        if (Current == EnemyStateType.Dead)
            return;

        ChangeState(EnemyStateType.NotCombat);
        SetLostTargetPhase(LostTargetPhase.Armed);
    }

    public void EnterDead()
    {
        if (Current == EnemyStateType.Dead)
            return;

        ChangeState(EnemyStateType.Dead);
    }

    void SetLostTargetPhase(LostTargetPhase phase)
    {
        LostTargetPhase = phase;
    }

    void ChangeState(EnemyStateType next)
    {
        // ✅ Dead 硬锁：一旦进入 Dead，永远不允许切回其他状态
        if (Current == EnemyStateType.Dead && next != EnemyStateType.Dead)
            return;

        if (Current == next)
            return;

        EnemyStateType prev = Current;
        Current = next;

        // ✅ 维护全局“Combat 敌人数量”
        if (prev != EnemyStateType.Combat && next == EnemyStateType.Combat)
            ForceCountEnterCombatIfNeeded();
        else if (prev == EnemyStateType.Combat && next != EnemyStateType.Combat)
            ForceCountExitCombatIfNeeded();

        OnStateChanged?.Invoke(prev, next);
    }
}
