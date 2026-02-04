using UnityEngine;

public interface IEnemyCombat
{
    void EnterCombat(Transform combatTarget);
    void ExitCombat();
    void SetTarget(Transform newTarget);
    void Tick();
}
