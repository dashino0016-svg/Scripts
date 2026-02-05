using UnityEngine;

[RequireComponent(typeof(Collider))]
public class RangeProjectile : MonoBehaviour
{
    [Header("Motion")]
    [SerializeField] float speed = 25f;
    [SerializeField] float lifeTime = 4f;
    [Tooltip("If true: move in Update by transform. If false and RB exists: move in FixedUpdate by RB.MovePosition.")]
    [SerializeField] bool useManualMove = true;

    [Header("Local Time Scale (Attacker)")]
    [Tooltip("If true: projectile move speed will be scaled by attacker's EnemyController.LocalTimeScale.")]
    [SerializeField] bool useAttackerLocalTimeScale = true;

    [Tooltip("If true: projectile lifetime countdown will also be scaled by attacker's LocalTimeScale.")]
    [SerializeField] bool scaleLifetimeWithAttacker = true;

    [SerializeField, Range(0.05f, 1f)] float minAttackerScale = 0.05f;

    [Header("Headshot (Tag)")]
    [SerializeField] bool enableHeadshot = true;
    [SerializeField] string headTag = "Head";
    [SerializeField, Range(1f, 10f)] float headshotHpMultiplier = 2f;

    [Header("Collision")]
    [Tooltip("Layers that projectile can hit (recommend HurtBox / Body layers, not Default Everything).")]
    [SerializeField] LayerMask hitMask = ~0;
    [Tooltip("Ignore collisions with current attacker root for a short time after spawn/reflect.")]
    [SerializeField] float ignoreOwnerSeconds = 0.08f;
    [Tooltip("Extra sweep test to avoid tunneling.")]
    [SerializeField] bool useRaycastBetweenFrames = true;

    [Header("Reflect")]
    [Tooltip("PerfectBlock reflect will aim back to original attacker if exists.")]
    [SerializeField] bool aimBackToOriginalAttacker = true;
    [Tooltip("When reflecting, add a little spread (degrees) for style. 0 = precise.")]
    [SerializeField] float reflectSpreadDegrees = 2f;

    [Header("Impact VFX (Optional)")]
    [SerializeField] GameObject hitVfxPrefab;
    [SerializeField] GameObject blockVfxPrefab;
    [SerializeField] GameObject perfectBlockVfxPrefab;

    bool initialized;
    Transform attacker;
    Transform originalAttacker;
    AttackData attackData;
    Vector3 dir;

    // 用 remaining 方式做寿命：可随 LocalTimeScale 变慢
    float lifeRemaining;
    float ignoreOwnerUntil;

    Collider col;
    Rigidbody rb;
    Vector3 lastPos;

    public void Init(Transform attackerTransform, Vector3 direction, AttackData data)
    {
        attacker = attackerTransform;
        if (originalAttacker == null) originalAttacker = attackerTransform;

        attackData = data;
        attackData.attacker = attackerTransform;

        dir = direction.sqrMagnitude < 0.0001f ? transform.forward : direction.normalized;

        lifeRemaining = lifeTime;
        ignoreOwnerUntil = Time.time + ignoreOwnerSeconds;

        lastPos = transform.position;

        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir);

        initialized = true;

        if (col != null)
        {
            col.isTrigger = true;
            col.enabled = true;
        }
    }

    void Awake()
    {
        col = GetComponent<Collider>();
        rb = GetComponent<Rigidbody>();

        if (col != null) col.isTrigger = true;

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    void OnEnable()
    {
        initialized = false;

        if (col != null)
            col.enabled = false;

        lastPos = transform.position;
        lifeRemaining = 0f;
    }

    float GetAttackerScale()
    {
        if (!useAttackerLocalTimeScale) return 1f;
        if (attacker == null) return 1f;

        // 你的时间减缓能力目前是改 EnemyController.LocalTimeScale
        EnemyController ec = attacker.GetComponentInParent<EnemyController>();
        if (ec == null) return 1f;

        return Mathf.Clamp(ec.LocalTimeScale, minAttackerScale, 1f);
    }

    bool StepLifetime(float scaledDelta)
    {
        if (!scaleLifetimeWithAttacker)
        {
            // 不缩放寿命时，用真实时间扣
            lifeRemaining -= Time.deltaTime;
        }
        else
        {
            lifeRemaining -= scaledDelta;
        }

        if (lifeRemaining <= 0f)
        {
            Despawn();
            return true;
        }

        return false;
    }

    void Update()
    {
        if (!initialized) return;

        if (useManualMove)
        {
            float s = GetAttackerScale();

            // 寿命（只在 Update 驱动的模式下扣一次）
            if (StepLifetime(Time.deltaTime * s)) return;

            Vector3 newPos = transform.position + dir * speed * Time.deltaTime * s;

            if (useRaycastBetweenFrames)
                SweepBetween(lastPos, newPos);

            transform.position = newPos;
            lastPos = newPos;
        }
    }

    void FixedUpdate()
    {
        if (!initialized) return;
        if (useManualMove) return;

        float s = GetAttackerScale();

        // ✅ 先扣寿命，避免 rb == null 时永远不销毁
        if (StepLifetime(Time.fixedDeltaTime * s)) return;

        if (rb == null) return;

        Vector3 newPos = rb.position + dir * speed * Time.fixedDeltaTime * s;

        if (useRaycastBetweenFrames)
            SweepBetween(rb.position, newPos);

        rb.MovePosition(newPos);
        lastPos = newPos;
    }

    void SweepBetween(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist < 0.0001f) return;

        Vector3 d = delta / dist;
        float radius = EstimateSweepRadius();

        if (Physics.SphereCast(from, radius, d, out RaycastHit hit, dist, hitMask, QueryTriggerInteraction.Collide))
        {
            HandleHitCollider(hit.collider, hit.point, hit.normal);
        }
    }

    float EstimateSweepRadius()
    {
        float r = 0.03f;
        if (col is SphereCollider sc)
        {
            float s = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
            r = Mathf.Max(r, sc.radius * s);
        }
        else if (col is CapsuleCollider cc)
        {
            float s = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            r = Mathf.Max(r, cc.radius * s);
        }
        return r;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!initialized) return;
        HandleHitCollider(other, transform.position, -dir);
    }

    void HandleHitCollider(Collider other, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (!initialized) return;
        if (other == null) return;

        if (((1 << other.gameObject.layer) & hitMask.value) == 0)
            return;

        if (Time.time < ignoreOwnerUntil)
        {
            if (attacker != null && other.transform.root == attacker.root)
                return;
        }

        CombatReceiver receiver = other.GetComponentInParent<CombatReceiver>();
        if (receiver == null)
        {
            SpawnVfx(hitVfxPrefab, hitPoint, hitNormal);
            Despawn();
            return;
        }

        if (Time.time < ignoreOwnerUntil)
        {
            if (attacker != null && receiver.transform.root == attacker.root)
                return;
        }

        // ===== Headshot HP multiplier (Tag) =====
        AttackData dataForThisHit = attackData;

        bool isHead = false;
        if (enableHeadshot && !string.IsNullOrEmpty(headTag))
        {
            try { isHead = other.CompareTag(headTag); }
            catch { isHead = false; } // tag 未定义时防止 UnityException
        }

        if (isHead && headshotHpMultiplier > 1.0001f)
        {
            int scaledHp = Mathf.RoundToInt(attackData.hpDamage * headshotHpMultiplier);

            // AttackData 是 class：不能直接改原对象，否则会污染后续命中/反弹
            dataForThisHit = new AttackData(
                attackData.attacker,
                attackData.sourceType,
                attackData.hitReaction,
                scaledHp,
                attackData.staminaDamage
            )
            {
                canBeBlocked = attackData.canBeBlocked,
                canBeParried = attackData.canBeParried,
                canBreakGuard = attackData.canBreakGuard,
                hasSuperArmor = attackData.hasSuperArmor,
                hitStopWeight = attackData.hitStopWeight
            };
        }

        HitResultType resultType = receiver.ReceiveProjectile(dataForThisHit, transform);

        switch (resultType)
        {
            case HitResultType.PerfectBlock:
                SpawnVfx(perfectBlockVfxPrefab, hitPoint, hitNormal);
                Reflect(receiver.transform);
                return;

            case HitResultType.Blocked:
                SpawnVfx(blockVfxPrefab, hitPoint, hitNormal);
                Despawn();
                return;

            case HitResultType.GuardBreak:
            case HitResultType.Hit:
            default:
                SpawnVfx(hitVfxPrefab, hitPoint, hitNormal);
                Despawn();
                return;
        }
    }

    void Reflect(Transform defender)
    {
        attacker = defender;
        attackData.attacker = defender;

        Vector3 newDir;

        if (aimBackToOriginalAttacker && originalAttacker != null)
        {
            Vector3 aimPos = LockTargetPointUtility.GetCapsuleCenter(originalAttacker);
            newDir = (aimPos - transform.position);   // ✅ 不抹 y
            if (newDir.sqrMagnitude < 0.001f)
                newDir = -dir;
        }
        else
        {
            newDir = -dir;
        }

        newDir.Normalize();

        if (reflectSpreadDegrees > 0.001f)
        {
            float yaw = Random.Range(-reflectSpreadDegrees, reflectSpreadDegrees);
            newDir = Quaternion.Euler(0f, yaw, 0f) * newDir;
            newDir.Normalize();
        }

        dir = newDir;
        transform.rotation = Quaternion.LookRotation(dir);

        ignoreOwnerUntil = Time.time + ignoreOwnerSeconds;

        // 反弹后给一段“新寿命”（与原逻辑一致：lifeTime*0.75，但至少1秒）
        lifeRemaining = Mathf.Max(lifeTime * 0.75f, 1.0f);
    }

    void SpawnVfx(GameObject prefab, Vector3 pos, Vector3 normal)
    {
        if (prefab == null) return;
        Quaternion rot = normal.sqrMagnitude > 0.001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
        Instantiate(prefab, pos, rot);
    }

    void Despawn()
    {
        Destroy(gameObject);
    }
}
