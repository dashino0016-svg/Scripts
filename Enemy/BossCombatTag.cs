using UnityEngine;

[RequireComponent(typeof(EnemyState))]
public class BossCombatTag : MonoBehaviour
{
    static int bossCombatCount = 0;
    public static bool AnyBossInCombat => bossCombatCount > 0;

    EnemyState state;
    bool counted;

    void OnEnable()
    {
        state = GetComponent<EnemyState>();
        state.OnStateChanged += OnStateChanged;

        if (state.IsInCombat)
            CountIn();
    }

    void OnDisable()
    {
        if (state != null)
            state.OnStateChanged -= OnStateChanged;

        CountOut();
    }

    void OnDestroy()
    {
        CountOut();
    }

    void OnStateChanged(EnemyStateType prev, EnemyStateType next)
    {
        if (prev != EnemyStateType.Combat && next == EnemyStateType.Combat)
            CountIn();
        else if (prev == EnemyStateType.Combat && next != EnemyStateType.Combat)
            CountOut();
    }

    void CountIn()
    {
        if (counted) return;
        counted = true;
        bossCombatCount++;
    }

    void CountOut()
    {
        if (!counted) return;
        counted = false;
        bossCombatCount = Mathf.Max(0, bossCombatCount - 1);
    }
}
