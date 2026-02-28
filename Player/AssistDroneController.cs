using System;
using UnityEngine;

public class AssistDroneController : MonoBehaviour
{
    [Header("SFX (Scheme1: Use AudioSource.clip)")]
    [SerializeField] AudioSource shotAudio; // 可不填，自动从 muzzle 上拿
    [SerializeField] Vector2 shotPitchRange = new Vector2(0.98f, 1.02f);

    [Header("Refs")]
    [Tooltip("整机/机身旋转的节点。为空则使用本物体 transform。")]
    [SerializeField] Transform bodyPivot;

    [SerializeField] Transform muzzle;

    [Header("Projectile (reuse RangeProjectile)")]
    [SerializeField] RangeProjectile projectilePrefab;
    [SerializeField] AttackConfig shotConfig;

    [Header("Follow")]
    public Vector3 followOffset = new Vector3(0f, 2.2f, 0f);
    public float followSmooth = 8f;

    [Header("Follow Lag")]
    [SerializeField, Min(0f)] float hoverCenterSmoothTime = 0.8f;
    [SerializeField, Min(0f)] float hoverCenterMaxSpeed = 30f;
    [SerializeField, Min(0f)] float teleportSnapDistance = 25f;

    [Header("Orbit")]
    public float orbitRadius = 8f;
    public float orbitSpeed = 0.5f; // radians per second

    [Header("Targeting")]
    public float detectRadius = 90f;
    public LayerMask enemyMask;
    public LayerMask obstacleMask;
    public float loseTargetDelay = 0f;

    [Header("Aiming (Whole Body)")]
    public float aimTurnSpeed = 2f;
    [Tooltip("只做水平转向（建议直升机开着，避免俯仰翻头）。")]
    public bool yawOnly = true;

    [Header("Firing")]
    public float fireRate = 10f; // shots per second
    public float fireRange = 30f;
    public bool requireLineOfSight = true;
    [SerializeField, Range(0f, 10f)] float spreadDegrees = 10f;

    [Header("Fire Angle Limit")]
    [SerializeField] bool limitFireAngle = true;
    [Tooltip("扇形总角度（例如 120 表示前方 ±60°）。")]
    [SerializeField, Range(1f, 180f)] float fireFovDegrees = 60f;
    [Tooltip("只限制水平扇形（忽略上下角度）。")]
    [SerializeField] bool fireAngleYawOnly = true;

    [Header("Transition Firing")]
    [SerializeField] bool fireWhileEntering = false;
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

    float exitSpeed;
    float lifeTimer;

    Vector3 exitTargetPos; // 离场目标点：BeginExit 时锁定，不追随玩家

    // ===== hover center lag =====
    Vector3 hoverCenter;
    Vector3 hoverCenterVel;
    bool hoverCenterInited;

    // ===== entering (direct to orbit) =====
    float enterStartTime;
    Vector3 enterStartOffset;        // 初始相对中心的 offset
    Vector3 enterTargetOrbitOffset;  // 目标轨道 offset（XZ 有半径，Y=0）

    enum DroneState
    {
        Entering,
        Active,
        Exiting
    }

    DroneState state = DroneState.Entering;
    Action onDespawned;

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

        if (bodyPivot == null) bodyPivot = transform;

        if (owner != null)
            transform.position = owner.position + Vector3.up * highAltitudeHeight;

        if (owner != null)
        {
            hoverCenter = owner.position + followOffset;
            hoverCenterVel = Vector3.zero;
            hoverCenterInited = true;
        }

        orbitPhase = UnityEngine.Random.value * Mathf.PI * 2f;

        // Entering：直接落到轨道点
        enterStartTime = Time.time;
        enterStartOffset = transform.position - hoverCenter;
        enterTargetOrbitOffset = new Vector3(
            Mathf.Cos(orbitPhase) * orbitRadius,
            0f,
            Mathf.Sin(orbitPhase) * orbitRadius
        );

        nextFireTime = 0f;
        lifeTimer = 0f;
        state = DroneState.Entering;

        SetAttackerRoot(owner);

        exitTargetPos = transform.position + Vector3.up * highAltitudeHeight;

        RefreshShotAudio();
    }

    void Awake()
    {
        RefreshShotAudio();
    }

    void RefreshShotAudio()
    {
        if (shotAudio == null)
        {
            var host = (muzzle != null) ? muzzle.gameObject : gameObject;
            shotAudio = host.GetComponent<AudioSource>();
            if (shotAudio == null) shotAudio = host.AddComponent<AudioSource>();
        }

        shotAudio.playOnAwake = false;
        shotAudio.spatialBlend = 1f; // 3D
    }

    void Update()
    {
        UpdateRotors();

        if (!warnedMissingRefs)
        {
            if (muzzle == null || projectilePrefab == null || shotConfig == null)
            {
                Debug.LogWarning($"[AssistDrone] Missing refs on {name}: muzzle/projectilePrefab/shotConfig", this);
                warnedMissingRefs = true;
            }
        }

        // lifetime 计入 Entering + Active（不计入 Exiting）
        if (state != DroneState.Exiting)
        {
            lifeTimer += Time.deltaTime;
            if (lifeTimer >= lifetime)
                BeginExit();
        }

        if (state == DroneState.Entering)
        {
            UpdateEntering();
            AcquireOrKeepTarget(); // ✅ 目标判定放 Update
            return;
        }

        if (state == DroneState.Exiting)
        {
            UpdateExiting();
            AcquireOrKeepTarget(); // ✅ 目标判定放 Update
            return;
        }

        UpdateActive();
        AcquireOrKeepTarget();     // ✅ 目标判定放 Update
    }

    void LateUpdate()
    {
        // ✅ 关键：先转向，再开火（同一帧换目标不会立刻开火）
        if (!IsAimEnabledNow() && !IsFireEnabledNow()) return;

        if (IsAimEnabledNow())
            AimWholeBodyNow();

        if (IsFireEnabledNow())
            AutoFireAfterAim();
    }

    bool IsFireEnabledNow()
    {
        if (state == DroneState.Active) return true;
        if (state == DroneState.Entering) return fireWhileEntering;
        if (state == DroneState.Exiting) return fireWhileExiting;
        return false;
    }

    bool IsAimEnabledNow() => IsFireEnabledNow();

    // =========================
    // Entering：直接降到轨道点
    // =========================
    void UpdateEntering()
    {
        if (owner == null)
        {
            BeginExit();
            return;
        }

        UpdateHoverCenter();

        float t = (enterDuration <= 0.0001f) ? 1f : Mathf.Clamp01((Time.time - enterStartTime) / enterDuration);
        t = t * t * (3f - 2f * t); // smoothstep

        Vector3 offset = Vector3.Lerp(enterStartOffset, enterTargetOrbitOffset, t);
        transform.position = hoverCenter + offset;

        if (t >= 0.9999f)
            EnterActive();
    }

    void EnterActive()
    {
        state = DroneState.Active;

        orbitPhase = Mathf.Atan2(enterTargetOrbitOffset.z, enterTargetOrbitOffset.x);
        if (orbitPhase < 0f) orbitPhase += Mathf.PI * 2f;
    }

    // =========================
    // Active：盘旋 + 滞后跟随
    // =========================
    void UpdateActive()
    {
        if (owner == null)
        {
            BeginExit();
            return;
        }

        FollowAndOrbit();
    }

    void UpdateHoverCenter()
    {
        if (owner == null) return;

        Vector3 desired = owner.position + followOffset;

        if (!hoverCenterInited)
        {
            hoverCenter = desired;
            hoverCenterVel = Vector3.zero;
            hoverCenterInited = true;
            return;
        }

        if ((desired - hoverCenter).sqrMagnitude > teleportSnapDistance * teleportSnapDistance)
        {
            hoverCenter = desired;
            hoverCenterVel = Vector3.zero;
            return;
        }

        hoverCenter = Vector3.SmoothDamp(
            hoverCenter,
            desired,
            ref hoverCenterVel,
            hoverCenterSmoothTime,
            hoverCenterMaxSpeed,
            Time.deltaTime
        );
    }

    void FollowAndOrbit()
    {
        UpdateHoverCenter();

        orbitPhase += orbitSpeed * Time.deltaTime;

        Vector3 orbitOffset = new Vector3(
            Mathf.Cos(orbitPhase) * orbitRadius,
            0f,
            Mathf.Sin(orbitPhase) * orbitRadius
        );

        Vector3 targetPos = hoverCenter + orbitOffset;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPos,
            1f - Mathf.Exp(-followSmooth * Time.deltaTime)
        );
    }

    // =========================
    // Exiting：锁定XZ上升离场
    // =========================
    void BeginExit()
    {
        if (state == DroneState.Exiting) return;

        state = DroneState.Exiting;

        if (!fireWhileExiting)
            currentTarget = null;

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

        if (Vector3.Distance(transform.position, exitTarget) <= 0.05f)
            DestroySelf();
    }

    // =========================
    // Target
    // =========================
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

    // =========================
    // Aim + Fire (after aim)
    // =========================
    void AimWholeBodyNow()
    {
        if (bodyPivot == null) bodyPivot = transform;
        if (currentTarget == null) return;

        Vector3 aimPoint = GetAimPoint(currentTarget);
        Vector3 aimDir = aimPoint - bodyPivot.position;

        if (yawOnly) aimDir.y = 0f;
        if (aimDir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(aimDir.normalized, Vector3.up);
        float k = 1f - Mathf.Exp(-aimTurnSpeed * Time.deltaTime);
        bodyPivot.rotation = Quaternion.Slerp(bodyPivot.rotation, targetRot, k);
    }

    Vector3 GetAimPoint(Transform targetRoot)
    {
        if (targetRoot == null)
            return transform.position + transform.forward;

        return LockTargetPointUtility.GetCapsuleCenter(targetRoot);
    }

    Vector3 GetFireForward()
    {
        // ✅ 用机身朝向作为门禁参考（避免 muzzle 有俯仰导致投影变0）
        Vector3 fwd = (bodyPivot != null ? bodyPivot.forward : transform.forward);

        if (fireAngleYawOnly)
        {
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f)
            {
                // 兜底：再用 transform.forward 的水平分量
                fwd = transform.forward;
                fwd.y = 0f;
            }
        }

        if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
        return fwd.normalized;
    }

    bool IsWithinFireAngle(Vector3 dirToTargetNormalized)
    {
        if (!limitFireAngle) return true;

        Vector3 fwd = GetFireForward();
        Vector3 dir = dirToTargetNormalized;

        if (fireAngleYawOnly)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return true;
            dir.Normalize();
        }

        float halfRad = Mathf.Deg2Rad * Mathf.Clamp(fireFovDegrees, 1f, 180f) * 0.5f;
        float cosHalf = Mathf.Cos(halfRad);

        return Vector3.Dot(fwd, dir) >= cosHalf;
    }

    void AutoFireAfterAim()
    {
        if (projectilePrefab == null || muzzle == null || shotConfig == null) return;
        if (currentTarget == null) return;

        Vector3 aimPoint = GetAimPoint(currentTarget);
        Vector3 toTarget = aimPoint - muzzle.position;

        float sqrDist = toTarget.sqrMagnitude;
        if (sqrDist > fireRange * fireRange) return;

        Vector3 dir = (sqrDist < 0.0001f) ? muzzle.forward : toTarget.normalized;

        // ✅ 角度门禁：现在参考的是 bodyPivot.forward，而且发生在转向之后
        if (!IsWithinFireAngle(dir)) return;

        if (requireLineOfSight && !HasLineOfSight(aimPoint))
            return;

        float interval = Mathf.Max(0.02f, 1f / Mathf.Max(0.01f, fireRate));
        if (Time.time < nextFireTime) return;

        FireOnce(dir);
        nextFireTime = Time.time + interval;
    }

    bool HasLineOfSight(Vector3 aimPoint)
    {
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

        if (shotAudio == null) RefreshShotAudio();
        if (shotAudio != null && shotAudio.clip != null)
        {
            shotAudio.pitch = UnityEngine.Random.Range(shotPitchRange.x, shotPitchRange.y);
            shotAudio.PlayOneShot(shotAudio.clip);
        }

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
            hasSuperArmor = cfg.hasSuperArmor,
            hitStopWeight = cfg.hitStopWeight,
            staminaPenetrationDamage = cfg.staminaPenetrationDamage,
            hpPenetrationDamage = cfg.hpPenetrationDamage,
            grantSpecialToAttackerOnHit = false
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
