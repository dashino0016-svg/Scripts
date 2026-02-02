using System;
using UnityEngine;

public class AssistDroneController : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("整机/机身旋转的节点。为空则使用本物体 transform。")]
    [SerializeField] Transform bodyPivot;

    [SerializeField] Transform muzzle;

    [Header("Projectile (reuse RangeProjectile)")]
    [SerializeField] RangeProjectile projectilePrefab;
    [SerializeField] AttackConfig shotConfig;

    [Header("Follow")]
    public Vector3 followOffset = new Vector3(0f, 2.2f, 0f);
    public float followSmooth = 12f;

    [Header("Orbit")]
    public float orbitRadius = 0.6f;
    public float orbitSpeed = 2.0f; // radians per second

    [Header("Targeting")]
    public float detectRadius = 15f;
    public LayerMask enemyMask;
    public LayerMask obstacleMask;
    public float loseTargetDelay = 0f;

    [Header("Aiming (Whole Body)")]
    public float aimTurnSpeed = 10f;
    [Tooltip("只做水平转向（建议直升机开着，避免俯仰翻头）。")]
    public bool yawOnly = true;

    [Header("Firing")]
    public float fireRate = 4f; // shots per second
    public float fireRange = 30f;
    public bool requireLineOfSight = true;
    [SerializeField, Range(0f, 10f)] float spreadDegrees = 0.8f;

    [Header("Transition Firing")]
    [Tooltip("下降(Entering)阶段是否允许射击。默认关闭。")]
    [SerializeField] bool fireWhileEntering = false;

    [Tooltip("上升(Exiting)阶段是否允许射击。默认关闭。")]
    [SerializeField] bool fireWhileExiting = false;

    [Header("Visual - Rotors")]
    [SerializeField] Transform mainRotor;
    [SerializeField] Vector3 mainRotorLocalAxis = Vector3.up;
    [SerializeField] float mainRotorRPM = 1000f;

    [SerializeField] Transform tailRotor;
    [SerializeField] Vector3 tailRotorLocalAxis = Vector3.right;
    [SerializeField] float tailRotorRPM = 1000f;

    [SerializeField] bool spinRotorsInEntering = true;
    [SerializeField] bool spinRotorsInExiting = true;

    Transform owner;
    Transform attackerRoot;

    float orbitPhase;
    float nextFireTime;

    Transform currentTarget;
    float targetLostTimer;

    bool warnedMissingRefs;

    float highAltitudeHeight;
    float enterDuration;
    float exitDuration;
    float lifetime;

    float enterSpeed;
    float exitSpeed;

    float lifeTimer;

    Vector3 exitTargetPos; // ✅ 离场目标点：BeginExit 时锁定，不追随玩家

    enum DroneState
    {
        Entering,
        Active,
        Exiting
    }

    DroneState state = DroneState.Entering;

    Action onDespawned;

    // 由 PlayerAbilitySystem 调用
    public void Init(
        Transform ownerTransform,
        Vector3 hoverOffset,
        float highAltitude,
        float enterDurationSeconds,
        float exitDurationSeconds,
        float newLifetime,
        Action onDespawn
    )
    {
        owner = ownerTransform;
        followOffset = hoverOffset;
        highAltitudeHeight = Mathf.Max(0f, highAltitude);
        enterDuration = Mathf.Max(0f, enterDurationSeconds);
        exitDuration = Mathf.Max(0f, exitDurationSeconds);
        lifetime = Mathf.Max(0f, newLifetime);
        onDespawned = onDespawn;

        if (owner != null)
            transform.position = owner.position + Vector3.up * highAltitudeHeight;

        if (bodyPivot == null) bodyPivot = transform;

        orbitPhase = UnityEngine.Random.value * Mathf.PI * 2f;
        nextFireTime = 0f;

        // ✅ lifeTimer 从生成开始计时（Entering 也计入 lifetime）
        lifeTimer = 0f;

        state = DroneState.Entering;

        Vector3 hoverPos = GetHoverPosition();
        float distance = Vector3.Distance(transform.position, hoverPos);
        enterSpeed = enterDuration <= 0.0001f ? float.MaxValue : distance / enterDuration;

        SetAttackerRoot(owner);

        // 防止未初始化就 BeginExit 时 exitTargetPos 未赋值
        exitTargetPos = transform.position + Vector3.up * highAltitudeHeight;
    }

    void Update()
    {
        // ✅ 旋翼：放最前，避免 Entering/Exiting return 时不转
        UpdateRotors();

        if (!warnedMissingRefs)
        {
            if (muzzle == null || projectilePrefab == null || shotConfig == null)
            {
                Debug.LogWarning($"[AssistDrone] Missing refs on {name}: muzzle/projectilePrefab/shotConfig", this);
                warnedMissingRefs = true;
            }
        }

        // ✅ lifetime 计入 Entering + Active（不计入 Exiting）
        if (state != DroneState.Exiting)
        {
            lifeTimer += Time.deltaTime;
            if (lifeTimer >= lifetime)
            {
                BeginExit();
                // BeginExit 后让下面逻辑走 Exiting 分支
            }
        }

        if (state == DroneState.Entering)
        {
            UpdateEntering();
            return;
        }

        if (state == DroneState.Exiting)
        {
            UpdateExiting();
            return;
        }

        UpdateActive();
    }

    void LateUpdate()
    {
        // Active 一直瞄准；Entering/Exiting 只有勾选“过渡也开火”时才瞄准（跟射击保持一致）
        if (!IsAimEnabledNow())
            return;

        AimWholeBodyLate();
    }

    bool IsFireEnabledNow()
    {
        if (state == DroneState.Active) return true;
        if (state == DroneState.Entering) return fireWhileEntering;
        if (state == DroneState.Exiting) return fireWhileExiting;
        return false;
    }

    bool IsAimEnabledNow()
    {
        return IsFireEnabledNow();
    }

    void UpdateEntering()
    {
        if (owner == null)
        {
            BeginExit();
            return;
        }

        Vector3 targetPos = GetHoverPosition();

        if (enterSpeed == float.MaxValue)
        {
            transform.position = targetPos;
            EnterActive();
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, targetPos, enterSpeed * Time.deltaTime);

        AcquireOrKeepTarget();
        AutoFire(); // 是否真的发射由 fireWhileEntering 控制

        if (Vector3.Distance(transform.position, targetPos) <= 0.05f)
            EnterActive();
    }

    void EnterActive()
    {
        state = DroneState.Active;
        // ✅ 不再重置 lifeTimer（Entering 时间计入 lifetime）
    }

    void UpdateActive()
    {
        if (owner == null)
        {
            BeginExit();
            return;
        }

        FollowAndOrbit();
        AcquireOrKeepTarget();
        AutoFire();
    }

    void BeginExit()
    {
        if (state == DroneState.Exiting)
            return;

        state = DroneState.Exiting;

        // 只有不允许离场射击时才清目标
        if (!fireWhileExiting)
            currentTarget = null;

        // ✅ 离场不追玩家：锁定一次“垂直上升”目标点
        // 目标高度：owner 当前高度 + highAltitudeHeight；XZ 使用当前直升机位置
        float baseY = owner != null ? owner.position.y : transform.position.y;
        float targetY = baseY + highAltitudeHeight;
        exitTargetPos = new Vector3(transform.position.x, targetY, transform.position.z);

        float distance = Vector3.Distance(transform.position, exitTargetPos);
        exitSpeed = exitDuration <= 0.0001f ? float.MaxValue : distance / exitDuration;
    }

    void UpdateExiting()
    {
        Vector3 exitTarget = exitTargetPos;

        if (exitSpeed == float.MaxValue)
        {
            transform.position = exitTarget;
            DestroySelf();
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, exitTarget, exitSpeed * Time.deltaTime);

        // 离场过程中按需继续锁敌/开火
        AcquireOrKeepTarget();
        AutoFire(); // 是否真的发射由 fireWhileExiting 控制

        if (Vector3.Distance(transform.position, exitTarget) <= 0.05f)
            DestroySelf();
    }

    Vector3 GetHoverPosition()
    {
        if (owner == null)
            return transform.position;

        return owner.position + followOffset;
    }

    void FollowAndOrbit()
    {
        orbitPhase += orbitSpeed * Time.deltaTime;

        Vector3 orbitOffset = new Vector3(
            Mathf.Cos(orbitPhase) * orbitRadius,
            0f,
            Mathf.Sin(orbitPhase) * orbitRadius
        );

        Vector3 targetPos = GetHoverPosition() + orbitOffset;
        transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-followSmooth * Time.deltaTime));
    }

    void AcquireOrKeepTarget()
    {
        Transform best = FindClosestEnemy();

        if (best != null)
        {
            currentTarget = best;
            targetLostTimer = 0f;
            return;
        }

        if (currentTarget != null)
        {
            targetLostTimer += Time.deltaTime;
            if (targetLostTimer >= loseTargetDelay)
                currentTarget = null;
        }
    }

    Transform FindClosestEnemy()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, detectRadius, enemyMask, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        Transform best = null;

        for (int i = 0; i < hits.Length; i++)
        {
            Transform candidate = hits[i].transform;

            CombatStats stats = candidate.GetComponentInParent<CombatStats>();
            Transform root = stats != null ? stats.transform : candidate;

            Vector3 pos = LockTargetPointUtility.GetCapsuleCenter(root);
            float d = (pos - transform.position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = root;
            }
        }

        return best;
    }

    void AimWholeBodyLate()
    {
        if (bodyPivot == null) bodyPivot = transform;

        if (currentTarget == null)
            return;

        Vector3 aimPoint = GetAimPoint(currentTarget);
        Vector3 aimDir = aimPoint - bodyPivot.position;

        if (yawOnly)
            aimDir.y = 0f;

        if (aimDir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(aimDir.normalized, Vector3.up);
        float t = 1f - Mathf.Exp(-aimTurnSpeed * Time.deltaTime);
        bodyPivot.rotation = Quaternion.Slerp(bodyPivot.rotation, targetRot, t);
    }

    void AutoFire()
    {
        if (!IsFireEnabledNow()) return;
        if (projectilePrefab == null || muzzle == null || shotConfig == null) return;
        if (currentTarget == null) return;

        Vector3 aimPoint = GetAimPoint(currentTarget);
        Vector3 toTarget = aimPoint - muzzle.position;
        if (toTarget.sqrMagnitude > fireRange * fireRange) return;

        if (requireLineOfSight && !HasLineOfSight(aimPoint))
            return;

        float interval = Mathf.Max(0.02f, 1f / Mathf.Max(0.01f, fireRate));
        if (Time.time < nextFireTime) return;

        FireOnce(toTarget.normalized);
        nextFireTime = Time.time + interval;
    }

    Vector3 GetAimPoint(Transform targetRoot)
    {
        if (targetRoot == null)
            return transform.position + transform.forward;

        return LockTargetPointUtility.GetCapsuleCenter(targetRoot);
    }

    bool HasLineOfSight(Vector3 aimPoint)
    {
        if (muzzle == null) return false;

        Vector3 from = muzzle.position;
        Vector3 dir = aimPoint - from;
        float dist = dir.magnitude;
        if (dist < 0.01f) return true;

        int losMask = obstacleMask.value & ~enemyMask.value;
        if (losMask == 0) return true;

        return !Physics.Raycast(from, dir.normalized, dist, losMask, QueryTriggerInteraction.Ignore);
    }

    void FireOnce(Vector3 dir)
    {
        if (spreadDegrees > 0.001f)
        {
            float yaw = UnityEngine.Random.Range(-spreadDegrees, spreadDegrees);
            dir = Quaternion.Euler(0f, yaw, 0f) * dir;
            dir.Normalize();
        }

        AttackData data = BuildAttackDataFromConfig(shotConfig);

        RangeProjectile p = Instantiate(projectilePrefab, muzzle.position, Quaternion.LookRotation(dir));
        p.Init(attackerRoot != null ? attackerRoot : transform, dir, data);
    }

    AttackData BuildAttackDataFromConfig(AttackConfig cfg)
    {
        var data = new AttackData(
            attackerRoot != null ? attackerRoot : transform,
            cfg.sourceType,
            cfg.hitReaction,
            cfg.hpDamage,
            cfg.staminaDamage
        )
        {
            canBeBlocked = cfg.canBeBlocked,
            canBeParried = cfg.canBeParried,
            canBreakGuard = cfg.canBreakGuard,
            hasSuperArmor = cfg.hasSuperArmor
        };
        return data;
    }

    void SetAttackerRoot(Transform ownerTransform)
    {
        if (ownerTransform == null)
        {
            attackerRoot = null;
            return;
        }

        CombatStats stats = ownerTransform.GetComponentInParent<CombatStats>() ??
                            ownerTransform.GetComponentInChildren<CombatStats>();
        attackerRoot = stats != null ? stats.transform : ownerTransform;
    }

    void DestroySelf()
    {
        onDespawned?.Invoke();
        Destroy(gameObject);
    }

    void UpdateRotors()
    {
        if (state == DroneState.Entering && !spinRotorsInEntering) return;
        if (state == DroneState.Exiting && !spinRotorsInExiting) return;

        float dt = Time.deltaTime;

        // 用 TransformDirection + Space.World 更稳（模型有 -90° 等预旋转也不怕）
        if (mainRotor != null)
        {
            Vector3 axisW = mainRotor.TransformDirection(mainRotorLocalAxis).normalized;
            mainRotor.Rotate(axisW, mainRotorRPM * 6f * dt, Space.World);
        }

        if (tailRotor != null)
        {
            Vector3 axisW = tailRotor.TransformDirection(tailRotorLocalAxis).normalized;
            tailRotor.Rotate(axisW, tailRotorRPM * 6f * dt, Space.World);
        }
    }
}
