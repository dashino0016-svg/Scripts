using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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
    CombatReceiver receiver;
    SwordController sword;

    [Header("Sensor")]
    public float viewRadius = 10f;

    [Range(0f, 180f)]
    public float viewAngle = 120f;

    public float eyeHeight = 1.7f;

    public LayerMask targetMask;
    public LayerMask obstacleMask;

    [Header("Hearing")]
    [Tooltip("听觉检测的最大搜索半径（需 >= 角色发声半径）。0 表示关闭听觉。")]
    public float hearingRadius = 12f;

    [Header("Combat Lose Buffer")]
    public float combatLoseDelay = 4f;

    [Header("Combat Sticky Target (Recommended)")]
    public bool stickyTargetInCombat = true;

    [Range(1f, 2f)]
    public float combatMaintainRadiusMultiplier = 1.2f;

    [Header("Aggro")]
    public float threatAddPerSecond = 3f;
    public float attackedThreatAdd = 30f;
    public float threatDecayPerSecond = 1f;
    public float forgetAfter = 6f;
    public float switchDelta = 6f;
    public float retargetInterval = 0.25f;

    Transform target;
    CombatStats targetStats;
    float loseTimer;
    bool targetVisibleThisFrame;
    float lastHostileStimulusTime = float.NegativeInfinity;
    float nextRetargetTime;

    class AggroEntry
    {
        public CombatStats stats;
        public float threat;
        public float lastStimulusTime;
    }

    readonly Dictionary<CombatStats, AggroEntry> aggroTable = new Dictionary<CombatStats, AggroEntry>();
    [Header("Weapon Transition Guard")]
    public bool freezeTransformDuringWeaponTransition = true;
    public bool freezeRotationDuringWeaponTransition = true;

    Vector3 weaponTransFreezePos;
    Quaternion weaponTransFreezeRot;
    bool weaponTransFreezeActive;

    NavMeshAgent cachedAgent;

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

    public bool IsTargetingPlayer
    {
        get
        {
            if (targetStats == null || targetStats.IsDead) return false;
            return targetStats.GetComponentInParent<PlayerController>() != null;
        }
    }

    // ================= Weapon Transition Lock =================

    void EnterWeaponTransitionLock(WeaponTransitionType type)
    {
        if (IsInWeaponTransition) return;

        IsInWeaponTransition = true;
        weaponTransitionType = type;

        if (freezeTransformDuringWeaponTransition)
        {
            weaponTransFreezePos = transform.position;
            weaponTransFreezeRot = transform.rotation;
            weaponTransFreezeActive = true;
        }

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
        // 确保 agent 虚拟位置与 transform 对齐（即使 navigator 曾经被禁用）
        if (cachedAgent != null && cachedAgent.enabled && cachedAgent.isOnNavMesh && !cachedAgent.updatePosition)
            cachedAgent.nextPosition = transform.position;

        if (cachedNavigator != null)
            cachedNavigator.SyncPosition(transform.position);
        weaponTransitionType = WeaponTransitionType.None;
    }

    void Awake()
    {
        enemyState = GetComponent<EnemyState>();
        anim = GetComponent<Animator>();
        combatStats = GetComponent<CombatStats>();
        receiver = GetComponent<CombatReceiver>();
        sword = GetComponentInChildren<SwordController>();
        cachedAgent = GetComponent<NavMeshAgent>();
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
        DecayAggro(Time.deltaTime);
        RetargetIfNeeded(false);

        if (enemyState.Current == EnemyStateType.Combat)
        {
            combatBrain?.Tick();
            UpdateCombatLoseTimer();
        }
    }
    void LateUpdate()
    {
        // ===== 修复：WeaponTransition 期间禁止动画曲线改 root transform =====
        if (IsInWeaponTransition && weaponTransFreezeActive && freezeTransformDuringWeaponTransition)
        {
            if (freezeRotationDuringWeaponTransition)
                transform.SetPositionAndRotation(weaponTransFreezePos, weaponTransFreezeRot);
            else
                transform.position = weaponTransFreezePos;
        }
        // ===== 修复：即使 EnemyNavigator 被禁用，也持续同步 agent.nextPosition =====
        if (cachedAgent != null && cachedAgent.enabled && cachedAgent.isOnNavMesh && !cachedAgent.updatePosition)
        {
            cachedAgent.nextPosition = transform.position;
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

    void RestoreSolidCollisionAfterCheckpoint()
    {
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (c == null) continue;
            if (c.isTrigger) continue;
            c.enabled = true;
        }

        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = true;

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.detectCollisions = true;
            rb.isKinematic = false;
        }

        solidCollisionDisabledPermanently = false;
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

    // ================= Aggro =================

    bool IsHostile(CombatStats cs)
    {
        return cs != null && cs.gameObject.layer != gameObject.layer;
    }

    void AddAggro(CombatStats cs, float add)
    {
        if (cs == null || cs.IsDead) return;
        if (!IsHostile(cs)) return;
        if (cs == combatStats) return;

        if (!aggroTable.TryGetValue(cs, out AggroEntry entry))
        {
            entry = new AggroEntry
            {
                stats = cs,
                threat = 0f,
                lastStimulusTime = Time.time
            };
            aggroTable.Add(cs, entry);
        }

        entry.threat += add;
        entry.lastStimulusTime = Time.time;
        lastHostileStimulusTime = Time.time;
    }

    void DecayAggro(float dt)
    {
        if (aggroTable.Count == 0) return;

        List<CombatStats> toRemove = null;

        foreach (var pair in aggroTable)
        {
            var entry = pair.Value;
            if (entry == null || entry.stats == null || entry.stats.IsDead)
            {
                if (toRemove == null) toRemove = new List<CombatStats>();
                toRemove.Add(pair.Key);
                continue;
            }

            entry.threat = Mathf.Max(0f, entry.threat - threatDecayPerSecond * dt);

            if (Time.time - entry.lastStimulusTime > forgetAfter && entry.threat <= 0.01f)
            {
                if (toRemove == null) toRemove = new List<CombatStats>();
                toRemove.Add(pair.Key);
            }
        }

        if (toRemove == null) return;
        for (int i = 0; i < toRemove.Count; i++)
            aggroTable.Remove(toRemove[i]);
    }

    // ================= Target Dead Gate =================

    void CheckTargetDead()
    {
        if (target == null || targetStats == null)
            return;

        if (!targetStats.IsDead)
            return;

        aggroTable.Remove(targetStats);
        target = null;
        targetStats = null;
        targetVisibleThisFrame = false;
        loseTimer = 0f;

        if (enemyState.Current == EnemyStateType.Combat)
        {
            if (aggroTable.Count > 0)
            {
                RetargetIfNeeded(true);
            }
            else
            {
                enemyState.EnterLostTarget();
            }
        }
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
                CombatStats currentStats = targetStats != null ? targetStats : target.GetComponentInParent<CombatStats>();
                AddAggro(currentStats, threatAddPerSecond * Time.deltaTime);
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
        {
            TryHearTarget();
            return;
        }

        for (int i = 0; i < hits.Length; i++)
        {
            Transform candidate = hits[i].transform;

            // ✅ 统一归一到 CombatStats
            CombatStats cs = candidate.GetComponentInParent<CombatStats>();
            if (cs == null) continue;
            if (cs.IsDead) continue;
            if (!IsHostile(cs)) continue;
            if (cs == combatStats) continue;

            if (!CanSeePlayer(cs.transform))
                continue;

            targetVisibleThisFrame = true;
            AddAggro(cs, threatAddPerSecond * Time.deltaTime);
        }

        TryHearTarget();
    }

    bool TryHearTarget()
    {
        if (hearingRadius <= 0f)
            return false;

        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            hearingRadius,
            targetMask,
            QueryTriggerInteraction.Ignore
        );

        if (hits.Length == 0)
            return false;

        bool heardAny = false;

        for (int i = 0; i < hits.Length; i++)
        {
            Transform candidate = hits[i].transform;

            CombatStats cs = candidate.GetComponentInParent<CombatStats>();
            if (cs == null) continue;
            if (cs.IsDead) continue;
            if (!IsHostile(cs)) continue;
            if (cs == combatStats) continue;

            NoiseEmitter noise = cs.GetComponentInParent<NoiseEmitter>();
            if (noise == null)
                noise = cs.GetComponentInChildren<NoiseEmitter>();

            if (noise == null)
                continue;

            float radius = noise.CurrentNoiseRadius;
            if (radius <= 0f)
                continue;

            Vector3 targetPos = LockTargetPointUtility.GetCapsuleCenter(cs.transform);
            float distance = Vector3.Distance(transform.position, targetPos);
            if (distance > radius)
                continue;

            targetVisibleThisFrame = true;
            heardAny = true;
            AddAggro(cs, threatAddPerSecond * Time.deltaTime);
        }

        return heardAny;
    }

    void RetargetIfNeeded(bool force)
    {
        if (!force && Time.time < nextRetargetTime)
            return;

        nextRetargetTime = Time.time + retargetInterval;

        if (aggroTable.Count == 0)
            return;

        AggroEntry best = null;
        foreach (var entry in aggroTable.Values)
        {
            if (entry == null || entry.stats == null || entry.stats.IsDead)
                continue;
            if (!IsHostile(entry.stats))
                continue;

            if (best == null || entry.threat > best.threat)
                best = entry;
        }

        if (best == null)
            return;

        bool currentValid = targetStats != null && !targetStats.IsDead && IsHostile(targetStats);
        if (!currentValid)
        {
            ApplyTarget(best);
            return;
        }

        if (best.stats == targetStats)
            return;

        float currentThreat = 0f;
        if (aggroTable.TryGetValue(targetStats, out AggroEntry currentEntry))
            currentThreat = currentEntry.threat;

        if (best.threat - currentThreat < switchDelta)
            return;

        ApplyTarget(best);
    }

    void ApplyTarget(AggroEntry entry)
    {
        if (entry == null || entry.stats == null) return;

        target = entry.stats.transform;
        targetStats = entry.stats;
        targetVisibleThisFrame = true;

        if (enemyState.Current == EnemyStateType.Combat)
        {
            combatBrain?.SetTarget(target);
        }
        else
        {
            enemyState.EnterCombat();
        }
    }

    bool CanMaintainTargetInCombat(Transform t)
    {
        if (t == null) return false;
        CombatStats cs = t.GetComponentInParent<CombatStats>();
        if (cs != null && cs.IsDead) return false;
        if (cs != null && !IsHostile(cs)) return false;

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

    public void OnAttacked(Transform attacker)
    {
        if (attacker == null) return;

        CombatStats cs = attacker.GetComponentInParent<CombatStats>();
        if (cs == null) return;
        if (cs.IsDead) return;
        if (!IsHostile(cs)) return;

        // ✅ 被打后锁定点 = 攻击者胶囊中心
        AddAggro(cs, attackedThreatAdd);
        RetargetIfNeeded(true);
    }

    public void ResetToHomeForCheckpoint(Transform homePoint)
    {
        if (enemyState == null)
            return;


        if (deadFallbackCo != null)
        {
            StopCoroutine(deadFallbackCo);
            deadFallbackCo = null;
        }

        waitingDeadDelay = false;
        deathByAssassination = false;
        IsInAssassinationLock = false;
        IsInWeaponTransition = false;
        weaponTransitionType = WeaponTransitionType.None;
        weaponLockRootMotionValid = false;
        cachedAnimSpeedValid = false;

        combatBrain?.ExitCombat();

        target = null;
        targetStats = null;
        aggroTable.Clear();
        loseTimer = 0f;
        lastHostileStimulusTime = float.NegativeInfinity;
        nextRetargetTime = 0f;

        if (receiver == null) receiver = GetComponent<CombatReceiver>();
        if (receiver != null)
        {
            receiver.ForceClearHitLock();
            receiver.ForceClearIFrame();
            receiver.ForceSetInvincible(false);
        }

        if (combatStats == null) combatStats = GetComponent<CombatStats>();
        if (combatStats != null)
            combatStats.ReviveFullHPAndStamina();

        if (cachedNavigator == null) cachedNavigator = GetComponent<EnemyNavigator>();
        if (cachedMove == null) cachedMove = GetComponent<EnemyMove>();

        if (cachedNavigator != null)
        {
            cachedNavigator.enabled = true;
            cachedNavigator.Stop();
        }

        if (cachedMove != null)
        {
            cachedMove.enabled = true;
            cachedMove.SetMoveDirection(Vector3.zero);
            cachedMove.SetMoveSpeedLevel(0);
        }

        var melee = GetComponent<MeleeFighter>();
        if (melee != null && melee.enabled)
            melee.InterruptAttack();

        var range = GetComponent<RangeFighter>();
        if (range != null && range.enabled)
            range.InterruptShoot();

        var block = GetComponent<BlockController>();
        if (block != null && block.enabled)
            block.ForceReleaseBlock();

        var ability = GetComponent<EnemyAbilitySystem>();
        if (ability != null && ability.enabled)
            ability.ForceCancelForCheckpoint();

        if (sword != null)
        {
            sword.AttachToWaist();
            sword.SetArmed(false);
        }

        if (anim != null)
        {
            anim.Rebind();
            anim.Update(0f);
            anim.SetBool("IsArmed", false);
            anim.speed = 1f;
            anim.applyRootMotion = false;
        }

        RestoreSolidCollisionAfterCheckpoint();

        if (homePoint != null)
        {
            // 1) 取组件
            CharacterController cc = GetComponent<CharacterController>();
            NavMeshAgent agent = GetComponent<NavMeshAgent>();

            bool ccWasEnabled = (cc != null && cc.enabled);
            if (ccWasEnabled) cc.enabled = false;

            // 2) 先把 Transform 移到目标（避免 Warp 失败时至少视觉到位）
            Vector3 targetPos = homePoint.position;
            Quaternion targetRot = homePoint.rotation;
            transform.SetPositionAndRotation(targetPos, targetRot);

            // 3) 同步 NavMeshAgent 的“权威位置”
            if (agent != null)
            {
                // 先尝试直接 Warp 到 home 点
                bool warped = agent.Warp(targetPos);

                // 如果 home 点不在 NavMesh，上一步可能失败：再 Sample 一次
                if (!warped)
                {
                    if (NavMesh.SamplePosition(targetPos, out var hit, 2f, NavMesh.AllAreas))
                    {
                        agent.Warp(hit.position);
                        transform.position = hit.position;   // 保证 Transform 与 agent 对齐
                    }
                    else
                    {
                        // 实在采样不到，就让 agent 尝试 warp 到原点位（可能仍失败，但不会卡死）
                        agent.Warp(targetPos);
                    }
                }

                agent.ResetPath();
                agent.nextPosition = transform.position;
            }

            // 4) 让 Navigator 的 nextPosition 与当前 transform 再对齐一次
            if (cachedNavigator != null)
                cachedNavigator.SyncPosition(transform.position);

            // 5) 清 EnemyMove 残留速度（避免瞬移后弹飞/抖动）
            if (cachedMove != null)
                cachedMove.ResetMotionForTeleport();

            if (ccWasEnabled) cc.enabled = true;
        }
        else
        {
            // 没传 homePoint 时也至少保证 nextPosition 同步，避免 agent 内部漂移
            if (cachedNavigator != null)
                cachedNavigator.SyncPosition(transform.position);

            if (cachedMove != null)
                cachedMove.ResetMotionForTeleport();
        }

        var hitBox = GetComponentInChildren<EnemyHitBox>(true);
        if (hitBox != null) hitBox.enabled = true;

        var notCombat = GetComponent<NotCombat>();
        if (notCombat != null) notCombat.enabled = true;

        var lostTarget = GetComponent<LostTarget>();
        if (lostTarget != null) lostTarget.enabled = true;

        if (combatBrainBehaviour != null) combatBrainBehaviour.enabled = true;

        enabled = true;
        enemyState.ForceResetToNotCombatForCheckpoint();
    }

    void UpdateCombatLoseTimer()
    {
        if (Time.time - lastHostileStimulusTime <= combatLoseDelay)
        {
            loseTimer = 0f;
            return;
        }

        loseTimer += Time.deltaTime;

        if (loseTimer >= combatLoseDelay)
        {
            loseTimer = 0f;
            aggroTable.Clear();
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
