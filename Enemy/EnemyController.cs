using System.Collections;
using UnityEngine;

[RequireComponent(typeof(EnemyState))]
[RequireComponent(typeof(LostTarget))]
[RequireComponent(typeof(NotCombat))]
public class EnemyController : MonoBehaviour
{
    EnemyState enemyState;
    IEnemyCombat combatBrain;
    MonoBehaviour combatBrainBehaviour;
    Animator anim;
    CombatStats combatStats;
    SwordController sword;

    [Header("Sensor")]
    public float viewRadius = 10f;

    [Range(0f, 180f)]
    public float viewAngle = 120f;

    public float eyeHeight = 1.7f;

    public LayerMask targetMask;
    public LayerMask obstacleMask;

    [Header("Combat Lose Buffer")]
    public float combatLoseDelay = 6f;

    [Header("Combat Sticky Target (Recommended)")]
    public bool stickyTargetInCombat = true;

    [Range(1f, 2f)]
    public float combatMaintainRadiusMultiplier = 2f;

    Transform target;
    CombatStats targetStats;
    float loseTimer;
    bool targetVisibleThisFrame;

    [Header("Local Time Scale (Enemy Only)")]
    [Range(0.05f, 1f)]
    [SerializeField] float localTimeScale = 1f;
    public float LocalTimeScale => localTimeScale;
    Coroutine localTimeCoroutine;

    public bool IsInAssassinationLock { get; private set; }
    bool deathByAssassination;
    public void MarkDeathByAssassination() => deathByAssassination = true;

    EnemyMove cachedMove;
    EnemyNavigator cachedNavigator;
    bool cachedMoveEnabled;
    bool cachedNavigatorEnabled;

    float cachedAnimSpeedBeforeAssassination = 1f;
    bool cachedAnimSpeedValid;

    bool solidCollisionDisabledPermanently;

    [Header("Death Collision Timing")]
    [SerializeField] float deadAnimFallbackDelay = 3.0f;
    Coroutine deadFallbackCo;
    bool waitingDeadDelay;

    public bool IsInWeaponTransition { get; private set; }

    enum WeaponTransitionType { None, Draw, Sheath }
    WeaponTransitionType weaponTransitionType = WeaponTransitionType.None;

    bool weaponLockMoveEnabled;
    bool weaponLockNavigatorEnabled;

    bool weaponLockRootMotionCached;
    bool weaponLockRootMotionValid;

    // ================= Weapon Transition Lock =================

    void EnterWeaponTransitionLock(WeaponTransitionType type)
    {
        if (IsInWeaponTransition) return;

        IsInWeaponTransition = true;
        weaponTransitionType = type;

        if (cachedNavigator == null) cachedNavigator = GetComponent<EnemyNavigator>();
        if (cachedNavigator != null)
        {
            cachedNavigator.Stop();
            cachedNavigator.SyncPosition(transform.position);

            weaponLockNavigatorEnabled = cachedNavigator.enabled;
            cachedNavigator.enabled = false;
        }

        if (cachedMove == null) cachedMove = GetComponent<EnemyMove>();
        if (cachedMove != null)
        {
            cachedMove.SetMoveDirection(Vector3.zero);
            cachedMove.SetMoveSpeedLevel(0);

            weaponLockMoveEnabled = cachedMove.enabled;
            cachedMove.enabled = false;
        }

        if (anim != null)
        {
            if (!weaponLockRootMotionValid)
            {
                weaponLockRootMotionCached = anim.applyRootMotion;
                weaponLockRootMotionValid = true;
            }
            anim.applyRootMotion = false;
        }
    }

    void ExitWeaponTransitionLock()
    {
        if (IsInAssassinationLock)
        {
            IsInWeaponTransition = false;
            weaponTransitionType = WeaponTransitionType.None;
            return;
        }

        if (cachedNavigator != null)
            cachedNavigator.enabled = weaponLockNavigatorEnabled;

        if (cachedMove != null)
            cachedMove.enabled = weaponLockMoveEnabled;

        if (anim != null && weaponLockRootMotionValid)
            anim.applyRootMotion = weaponLockRootMotionCached;

        weaponLockRootMotionValid = false;

        IsInWeaponTransition = false;
        weaponTransitionType = WeaponTransitionType.None;
    }

    void Awake()
    {
        enemyState = GetComponent<EnemyState>();
        anim = GetComponent<Animator>();
        combatStats = GetComponent<CombatStats>();
        sword = GetComponentInChildren<SwordController>();

        cachedMove = GetComponent<EnemyMove>();
        cachedNavigator = GetComponent<EnemyNavigator>();

        ResolveCombatBrain();
    }

    void ResolveCombatBrain()
    {
        combatBrain = null;
        combatBrainBehaviour = null;

        var behaviours = GetComponents<MonoBehaviour>();
        int found = 0;

        for (int i = 0; i < behaviours.Length; i++)
        {
            var b = behaviours[i];
            if (b == null) continue;

            if (b is IEnemyCombat ec)
            {
                found++;
                combatBrain = ec;
                combatBrainBehaviour = b;
            }
        }

        if (found == 0)
        {
            Debug.LogError($"[{name}] Missing IEnemyCombat. Add ONLY ONE: Combat (melee) OR RangeCombat (ranged).", this);
            enabled = false;
            return;
        }

        if (found > 1)
        {
            Debug.LogError($"[{name}] Multiple IEnemyCombat found. Keep ONLY ONE: Combat OR RangeCombat.", this);
            enabled = false;
            return;
        }
    }

    void OnEnable()
    {
        enemyState.OnStateChanged += OnStateChanged;
        if (combatStats != null)
            combatStats.OnDead += OnCharacterDead;
    }

    void OnDisable()
    {
        enemyState.OnStateChanged -= OnStateChanged;
        if (combatStats != null)
            combatStats.OnDead -= OnCharacterDead;
    }

    void Update()
    {
        if (enemyState != null && enemyState.Current == EnemyStateType.Dead)
            return;

        if (IsInAssassinationLock)
            return;

        CheckTargetDead();

        UpdateSensor();

        if (enemyState.Current == EnemyStateType.Combat)
        {
            combatBrain?.Tick();
            UpdateCombatLoseTimer();
        }
    }

    // ================= Assassination Lock =================

    public void EnterAssassinationLock()
    {
        if (IsInAssassinationLock) return;
        IsInAssassinationLock = true;

        combatBrain?.ExitCombat();

        if (cachedNavigator == null) cachedNavigator = GetComponent<EnemyNavigator>();
        if (cachedNavigator != null)
        {
            cachedNavigator.Stop();
            cachedNavigator.SyncPosition(transform.position);

            cachedNavigatorEnabled = cachedNavigator.enabled;
            cachedNavigator.enabled = false;
        }

        if (cachedMove == null) cachedMove = GetComponent<EnemyMove>();
        if (cachedMove != null)
        {
            cachedMove.SetMoveDirection(Vector3.zero);
            cachedMove.SetMoveSpeedLevel(0);

            cachedMoveEnabled = cachedMove.enabled;
            cachedMove.enabled = false;
        }

        var hitBox = GetComponentInChildren<EnemyHitBox>();
        if (hitBox != null) hitBox.enabled = false;

        if (anim != null) anim.applyRootMotion = false;

        if (anim != null)
        {
            cachedAnimSpeedBeforeAssassination = anim.speed;
            cachedAnimSpeedValid = true;
            anim.speed = 1f;
        }

        DisableSolidCollisionPermanently();
    }

    public void ExitAssassinationLock()
    {
        IsInAssassinationLock = false;

        if (cachedNavigator != null)
            cachedNavigator.enabled = cachedNavigatorEnabled;

        if (cachedMove != null)
            cachedMove.enabled = cachedMoveEnabled;

        if (anim != null && cachedAnimSpeedValid)
        {
            anim.speed = localTimeScale;
            cachedAnimSpeedValid = false;
        }
    }

    void DisableSolidCollisionPermanently()
    {
        if (solidCollisionDisabledPermanently) return;
        solidCollisionDisabledPermanently = true;

        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (c == null) continue;
            if (c.isTrigger) continue;
            c.enabled = false;
        }

        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.detectCollisions = false;
            rb.isKinematic = true;
        }
    }

    IEnumerator DeadCollisionDelay()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, deadAnimFallbackDelay));

        if (!waitingDeadDelay)
            yield break;

        waitingDeadDelay = false;
        deadFallbackCo = null;

        DisableSolidCollisionPermanently();
        enabled = false;
    }

    // ================= Target Dead Gate =================

    void CheckTargetDead()
    {
        if (target == null || targetStats == null)
            return;

        if (!targetStats.IsDead)
            return;

        target = null;
        targetStats = null;
        targetVisibleThisFrame = false;
        loseTimer = 0f;

        if (enemyState.Current == EnemyStateType.Combat)
            enemyState.EnterLostTarget();
    }

    // ================= Sensor =================

    void UpdateSensor()
    {
        targetVisibleThisFrame = false;

        if (stickyTargetInCombat &&
            enemyState.Current == EnemyStateType.Combat &&
            target != null)
        {
            if (CanMaintainTargetInCombat(target))
            {
                targetVisibleThisFrame = true;
                return;
            }
        }

        // ✅ 忽略 Trigger：避免玩家防御/武器/判定触发器被当作目标
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            viewRadius,
            targetMask,
            QueryTriggerInteraction.Ignore
        );

        if (hits.Length == 0)
            return;

        for (int i = 0; i < hits.Length; i++)
        {
            Transform candidate = hits[i].transform;

            // ✅ 统一归一到 CombatStats
            CombatStats cs = candidate.GetComponentInParent<CombatStats>();
            if (cs == null) continue;
            if (cs.IsDead) continue;

            if (!CanSeePlayer(cs.transform))
                continue;

            target = cs.transform;
            targetStats = cs;
            targetVisibleThisFrame = true;

            if (enemyState.Current != EnemyStateType.Combat)
                enemyState.EnterCombat();

            return;
        }
    }

    bool CanMaintainTargetInCombat(Transform t)
    {
        CombatStats cs = t != null ? t.GetComponentInParent<CombatStats>() : null;
        if (t == null) return false;
        if (cs != null && cs.IsDead) return false;

        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPos = LockTargetPointUtility.GetCapsuleCenter(t);

        Vector3 dir = targetPos - eyePos;
        dir.y = 0f;

        float distance = dir.magnitude;
        float maxDist = viewRadius * combatMaintainRadiusMultiplier;
        if (distance > maxDist)
            return false;

        if (Physics.Raycast(eyePos, dir.normalized, distance, obstacleMask))
            return false;

        return true;
    }

    bool CanSeePlayer(Transform playerPoint)
    {
        CombatStats cs = playerPoint != null ? playerPoint.GetComponentInParent<CombatStats>() : null;
        if (cs != null && cs.IsDead) return false;

        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPos = LockTargetPointUtility.GetCapsuleCenter(playerPoint);

        Vector3 dirToTarget = targetPos - eyePos;
        dirToTarget.y = 0f;

        float distance = dirToTarget.magnitude;
        if (distance > viewRadius)
            return false;

        float angle = Vector3.Angle(transform.forward, dirToTarget.normalized);
        if (angle > viewAngle * 0.5f)
            return false;

        if (Physics.Raycast(eyePos, dirToTarget.normalized, distance, obstacleMask))
            return false;

        return true;
    }

    void SetTarget(CombatStats cs)
    {
        if (cs == null || cs.IsDead) return;

        target = cs.transform;
        targetStats = cs;

        loseTimer = 0f;
        targetVisibleThisFrame = true;
    }

    public void OnAttacked(Transform attacker)
    {
        if (attacker == null) return;

        CombatStats cs = attacker.GetComponentInParent<CombatStats>();
        if (cs == null) return;
        if (cs.IsDead) return;

        // ✅ 被打后锁定点 = 攻击者胶囊中心
        SetTarget(cs);

        if (enemyState != null)
            enemyState.ForceEnterCombat();
    }

    void UpdateCombatLoseTimer()
    {
        if (targetVisibleThisFrame)
        {
            loseTimer = 0f;
            return;
        }

        loseTimer += Time.deltaTime;

        if (loseTimer >= combatLoseDelay)
        {
            loseTimer = 0f;
            target = null;
            targetStats = null;
            enemyState.EnterLostTarget();
        }
    }

    public void ApplyLocalTimeScale(float scale, float durationSeconds)
    {
        if (localTimeCoroutine != null)
            StopCoroutine(localTimeCoroutine);

        localTimeCoroutine = StartCoroutine(LocalTimeScaleRoutine(scale, durationSeconds));
    }

    IEnumerator LocalTimeScaleRoutine(float scale, float durationSeconds)
    {
        localTimeScale = Mathf.Clamp(scale, 0.05f, 1f);

        if (anim != null)
            anim.speed = localTimeScale;

        yield return new WaitForSecondsRealtime(durationSeconds);

        localTimeScale = 1f;
        if (anim != null)
            anim.speed = 1f;

        localTimeCoroutine = null;
    }

    // ================= Weapon Requests / Events =================

    public void RequestDrawSword()
    {
        if (IsInWeaponTransition) return;

        EnterWeaponTransitionLock(WeaponTransitionType.Draw);
        anim.SetTrigger("DrawSword");
    }

    public void RequestSheathSword()
    {
        if (IsInWeaponTransition) return;

        EnterWeaponTransitionLock(WeaponTransitionType.Sheath);
        anim.SetTrigger("SheathSword");
    }

    public void AttachSwordToHand()
    {
        if (sword != null) sword.AttachToHand();
    }

    public void AttachSwordToWaist()
    {
        if (sword != null) sword.AttachToWaist();
    }

    public void OnDrawSwordEnd()
    {
        ExitWeaponTransitionLock();

        if (sword != null) sword.SetArmed(true);
        anim.SetBool("IsArmed", sword != null ? sword.IsArmed : true);

        if (enemyState.Current == EnemyStateType.Combat && target != null)
            combatBrain?.EnterCombat(target);
    }

    public void OnSheathSwordEnd()
    {
        ExitWeaponTransitionLock();

        if (enemyState != null && enemyState.Current == EnemyStateType.Combat && target != null)
        {
            RequestDrawSword();
            return;
        }

        if (sword != null) sword.SetArmed(false);
        anim.SetBool("IsArmed", sword != null ? sword.IsArmed : false);

        enemyState.OnSheathSwordEnd();
    }

    public void OnCharacterDead()
    {
        if (enemyState.Current == EnemyStateType.Dead)
            return;

        enemyState.EnterDead();
    }

    // ================= State Change =================

    void OnStateChanged(EnemyStateType prev, EnemyStateType next)
    {
        if (prev == EnemyStateType.Combat && next != EnemyStateType.Combat)
        {
            combatBrain?.ExitCombat();
        }

        if (next == EnemyStateType.Combat)
        {
            bool armed = (sword != null) ? sword.IsArmed : anim.GetBool("IsArmed");
            if (!armed && !IsInWeaponTransition)
            {
                RequestDrawSword();
            }

            if (target != null)
            {
                combatBrain?.EnterCombat(target);
            }
        }

        if (next == EnemyStateType.Dead)
        {
            combatBrain?.ExitCombat();

            var notCombat = GetComponent<NotCombat>();
            if (notCombat != null) notCombat.enabled = false;

            var lostTarget = GetComponent<LostTarget>();
            if (lostTarget != null) lostTarget.enabled = false;

            if (combatBrainBehaviour != null) combatBrainBehaviour.enabled = false;

            var move = GetComponent<EnemyMove>();
            if (move != null) move.enabled = false;

            var navigator = GetComponent<EnemyNavigator>();
            if (navigator != null)
            {
                navigator.Stop();
                navigator.enabled = false;
            }

            var hitBox = GetComponentInChildren<EnemyHitBox>();
            if (hitBox != null) hitBox.enabled = false;

            if (!deathByAssassination)
                anim.SetTrigger("Dead");

            if (deathByAssassination || IsInAssassinationLock || solidCollisionDisabledPermanently)
            {
                DisableSolidCollisionPermanently();
                enabled = false;
                return;
            }

            waitingDeadDelay = true;

            if (deadFallbackCo != null)
                StopCoroutine(deadFallbackCo);

            deadFallbackCo = StartCoroutine(DeadCollisionDelay());
            return;
        }
    }
}
