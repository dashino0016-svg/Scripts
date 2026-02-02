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
    public float detectRadius = 12f;
    public LayerMask enemyMask;
    public LayerMask obstacleMask;
    public float loseTargetDelay = 1.0f;

    [Header("Aiming (Whole Body)")]
    public float aimTurnSpeed = 10f;
    [Tooltip("只做水平转向（建议直升机开着，避免俯仰翻头）。")]
    public bool yawOnly = true;

    [Header("Firing")]
    public float fireRate = 4f; // shots per second
    public float fireRange = 30f;
    public bool requireLineOfSight = true;
    [SerializeField, Range(0f, 10f)] float spreadDegrees = 0.8f;

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

    bool canFire;

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

        if (owner != null)
        {
            transform.position = owner.position + Vector3.up * highAltitudeHeight;
        }

        if (bodyPivot == null) bodyPivot = transform;

        orbitPhase = UnityEngine.Random.value * Mathf.PI * 2f;
        nextFireTime = 0f;
        lifeTimer = 0f;
        canFire = false;
        state = DroneState.Entering;

        Vector3 hoverPos = GetHoverPosition();
        float distance = Vector3.Distance(transform.position, hoverPos);
        enterSpeed = enterDuration <= 0.0001f ? float.MaxValue : distance / enterDuration;

        SetAttackerRoot(owner);
    }

    void Update()
    {
        if (!warnedMissingRefs)
        {
            if (muzzle == null || projectilePrefab == null || shotConfig == null)
            {
                Debug.LogWarning($"[AssistDrone] Missing refs on {name}: muzzle/projectilePrefab/shotConfig", this);
                warnedMissingRefs = true;
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
        if (state != DroneState.Active)
            return;

        AimWholeBodyLate();
    }

    void UpdateEntering()
    {
        canFire = false;

        Vector3 targetPos = GetHoverPosition();
        if (enterSpeed == float.MaxValue)
        {
            transform.position = targetPos;
            EnterActive();
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, targetPos, enterSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPos) <= 0.05f)
            EnterActive();
    }

    void EnterActive()
    {
        state = DroneState.Active;
        canFire = true;
        lifeTimer = 0f;
        currentTarget = null;
        targetLostTimer = 0f;
    }

    void UpdateActive()
    {
        if (owner == null)
        {
            BeginExit();
            return;
        }

        lifeTimer += Time.deltaTime;
        if (lifeTimer >= lifetime)
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
        canFire = false;
        currentTarget = null;

        Vector3 exitTarget = GetHighAltitudePosition();
        float distance = Vector3.Distance(transform.position, exitTarget);
        exitSpeed = exitDuration <= 0.0001f ? float.MaxValue : distance / exitDuration;
    }

    void UpdateExiting()
    {
        Vector3 exitTarget = GetHighAltitudePosition();

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

    Vector3 GetHoverPosition()
    {
        if (owner == null)
            return transform.position;

        return owner.position + followOffset;
    }

    Vector3 GetHighAltitudePosition()
    {
        if (owner == null)
            return transform.position + Vector3.up * highAltitudeHeight;

        return owner.position + Vector3.up * highAltitudeHeight;
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
        if (!canFire) return;
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
}
