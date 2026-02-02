using UnityEngine;

public class AssistDroneController : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("整机/机身旋转的节点。为空则使用本物体 transform。")]
    [SerializeField] private Transform bodyPivot;

    [SerializeField] private Transform muzzle;

    [Header("Projectile (reuse RangeProjectile)")]
    [SerializeField] private RangeProjectile projectilePrefab;
    [SerializeField] private AttackConfig shotConfig;

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
    [SerializeField, Range(0f, 10f)] private float spreadDegrees = 0.8f;

    [Header("Lifetime / Depart")]
    public float lifetime = 12f;
    public float departUpSpeed = 6f;
    public float departDuration = 1.2f;

    private Transform player;
    private float orbitPhase;
    private float nextFireTime;

    private Transform currentTarget;
    private float targetLostTimer;

    private bool departing;
    private float lifeTimer;

    private bool warnedMissingRefs;

    public void Init(Transform playerTransform, float newLifetime)
    {
        player = playerTransform;
        lifetime = newLifetime;

        departing = false;
        CancelInvoke(nameof(DestroySelf));

        currentTarget = null;
        targetLostTimer = 0f;

        lifeTimer = 0f;
        orbitPhase = Random.value * Mathf.PI * 2f;

        if (player != null)
            transform.position = player.position + followOffset;

        if (bodyPivot == null) bodyPivot = transform;
    }

    private void Update()
    {
        // 没初始化就等着（不自毁）
        if (player == null) return;

        if (!warnedMissingRefs)
        {
            if (muzzle == null || projectilePrefab == null || shotConfig == null)
            {
                Debug.LogWarning($"[AssistDrone] Missing refs on {name}: muzzle/projectilePrefab/shotConfig", this);
                warnedMissingRefs = true;
            }
        }

        if (departing) return;

        lifeTimer += Time.deltaTime;
        if (lifeTimer >= lifetime)
        {
            BeginDepart();
            return;
        }

        FollowAndOrbit();
        AcquireOrKeepTarget();

        // 开火在 Update（不受 Animator 覆盖影响）
        AutoFire();
    }

    private void LateUpdate()
    {
        if (player == null) return;

        if (departing)
        {
            transform.position += Vector3.up * (departUpSpeed * Time.deltaTime);
            return;
        }

        AimWholeBodyLate(); // ✅ 帧末整机转向，避免被动画覆盖
    }

    void FollowAndOrbit()
    {
        orbitPhase += orbitSpeed * Time.deltaTime;

        Vector3 orbitOffset = new Vector3(
            Mathf.Cos(orbitPhase) * orbitRadius,
            0f,
            Mathf.Sin(orbitPhase) * orbitRadius
        );

        Vector3 targetPos = player.position + followOffset + orbitOffset;
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
            Transform t = hits[i].transform;
            float d = (t.position - transform.position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = t;
            }
        }
        return best;
    }

    void AimWholeBodyLate()
    {
        if (bodyPivot == null) bodyPivot = transform;

        Vector3 aimDir;

        if (currentTarget == null)
        {
            // 没目标：保持当前朝向（你也可以在这里做“缓慢回正/朝玩家方向”）
            return;
        }

        aimDir = currentTarget.position - bodyPivot.position;

        if (yawOnly)
            aimDir.y = 0f;

        if (aimDir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(aimDir.normalized, Vector3.up);

        // 平滑旋转（指数平滑，手感更稳定）
        float t = 1f - Mathf.Exp(-aimTurnSpeed * Time.deltaTime);
        bodyPivot.rotation = Quaternion.Slerp(bodyPivot.rotation, targetRot, t);
    }

    void AutoFire()
    {
        if (projectilePrefab == null || muzzle == null || shotConfig == null) return;
        if (currentTarget == null) return;

        Vector3 aimPoint = currentTarget.position;
        Vector3 toTarget = aimPoint - muzzle.position;
        if (toTarget.sqrMagnitude > fireRange * fireRange) return;

        if (requireLineOfSight && !HasLineOfSight(aimPoint))
            return;

        float interval = Mathf.Max(0.02f, 1f / Mathf.Max(0.01f, fireRate));
        if (Time.time < nextFireTime) return;

        FireOnce(toTarget.normalized);
        nextFireTime = Time.time + interval;
    }

    bool HasLineOfSight(Vector3 aimPoint)
    {
        Vector3 from = muzzle.position;
        Vector3 dir = aimPoint - from;
        float dist = dir.magnitude;
        if (dist < 0.01f) return true;

        // obstacleMask 只放墙/地形，不要把敌人层放进去
        return !Physics.Raycast(from, dir.normalized, dist, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    void FireOnce(Vector3 dir)
    {
        // 轻微散布（只做 yaw 更像激光扫射）
        if (spreadDegrees > 0.001f)
        {
            float yaw = Random.Range(-spreadDegrees, spreadDegrees);
            dir = Quaternion.Euler(0f, yaw, 0f) * dir;
            dir.Normalize();
        }

        AttackData data = BuildAttackDataFromConfig(shotConfig);

        RangeProjectile p = Instantiate(projectilePrefab, muzzle.position, Quaternion.LookRotation(dir));
        p.Init(transform, dir, data); // ✅ 不 Init 子弹不会动
    }

    AttackData BuildAttackDataFromConfig(AttackConfig cfg)
    {
        var data = new AttackData(
            transform,
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

    void BeginDepart()
    {
        departing = true;
        currentTarget = null;
        Invoke(nameof(DestroySelf), departDuration);
    }

    void DestroySelf()
    {
        Destroy(gameObject);
    }
}
