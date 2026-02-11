using UnityEngine;

public class MiniAssistDroneController : MonoBehaviour
{
    public enum DroneState { Docked, Entering, Active, Returning }

    [Header("Anchors")]
    [SerializeField] Transform owner;
    [SerializeField] Transform dockAnchor;
    [SerializeField] Vector3 dockLocalPos;
    [SerializeField] Vector3 dockLocalEuler;

    [Header("Hover")]
    [SerializeField] Vector3 hoverOffset = new Vector3(0f, 2.0f, 0f);
    [SerializeField] float orbitRadius = 0.45f;
    [SerializeField] float orbitSpeed = 2.5f;

    [Header("Follow Lag")]
    [SerializeField, Min(0f)] float hoverCenterSmoothTime = 0.28f;
    [SerializeField, Min(0f)] float hoverCenterMaxSpeed = 30f;
    [SerializeField, Min(0f)] float teleportSnapDistance = 25f;

    [Header("Enter/Return Path")]
    [SerializeField, Min(0f)] float retreatDistance = 0.45f;
    [SerializeField, Min(0f)] float retreatTime = 0.12f;
    [SerializeField, Min(0f)] float ascendTime = 0.20f;
    [SerializeField, Min(0f)] float toOrbitTime = 0.22f;

    [SerializeField, Min(0f)] float fromOrbitTime = 0.18f;
    [SerializeField, Min(0f)] float descendTime = 0.20f;
    [SerializeField, Min(0f)] float forwardToDockTime = 0.12f;

    [Header("Facing")]
    [SerializeField] bool faceWhileFlying = true;
    [SerializeField] bool faceLockedTarget = true;
    [SerializeField, Min(0f)] float faceYawSpeed = 14f;

    [Header("Facing Reference (fallback)")]
    [SerializeField] Transform yawReference;
    [SerializeField] float yawOffsetDegrees = 0f;
    [SerializeField] bool snapYaw = false;

    [Header("Auto Target (when player NOT locked)")]
    [SerializeField] bool autoTargetWhenPlayerUnlocked = true;
    [SerializeField, Min(0f)] float autoTargetRange = 12f;
    [SerializeField] LayerMask autoTargetLayers = ~0;
    [SerializeField, Min(0.02f)] float autoTargetRefreshInterval = 0.12f;

    [Header("Muzzles (3)")]
    [SerializeField] Transform tapMuzzleA;
    [SerializeField] Transform tapMuzzleB;
    [SerializeField] Transform chargedMuzzle;

    [Tooltip("兼容：枪口都不填时用它。")]
    [SerializeField] Transform muzzleLegacy;

    [Header("Projectile Prefabs")]
    [SerializeField] GameObject tapProjectilePrefab;
    [SerializeField] GameObject chargedProjectilePrefab;
    [SerializeField] GameObject projectilePrefabLegacy;
    [SerializeField, Min(0f)] float spawnForwardOffset = 0.06f;

    [Header("Attack Configs (authoritative)")]
    [SerializeField] AttackConfig tapAttackConfig;         // 每颗普通子弹
    [SerializeField] AttackConfig chargedAttackConfig;     // 蓄力子弹（固定伤害）

    [Header("Charge Timing")]
    [Tooltip("按住超过该时间才进入“开始蓄力”（开始播放蓄力音）。未超过则视为点按普通射击。")]
    [SerializeField, Min(0f)] float longPressThreshold = 0.35f;

    [Tooltip("从“开始蓄力”到“蓄力完成”的时间。未完成前松开会取消；完成后进入静默 Ready，松开才发射。")]
    [SerializeField, Min(0.01f)] float maxChargeTime = 1.0f;

    [Header("Fire Cooldown")]
    [SerializeField, Min(0f)] float tapCooldown = 0.16f;
    [SerializeField, Min(0f)] float chargedCooldown = 0.30f;

    [Header("Energy (use player's CombatStats.Special)")]
    [SerializeField, Min(0)] int tapSpecialCost = 10;
    [SerializeField, Min(0)] int chargedSpecialCost = 40;

    [Header("SFX (Per Muzzle)")]
    [SerializeField] AudioClip tapShotClip;
    [SerializeField] AudioClip chargedShotClip;

    [Tooltip("蓄力音频（开始蓄力后播放，循环；蓄力完成后停止并进入静默）。")]
    [SerializeField] AudioClip chargeLoopClip;

    [SerializeField] AudioClip noEnergyClip;

    [Tooltip("普通枪口A的发射音源（建议挂在 muzzleA 上，3D）。")]
    [SerializeField] AudioSource tapShotSourceA;

    [Tooltip("普通枪口B的发射音源（建议挂在 muzzleB 上，3D）。")]
    [SerializeField] AudioSource tapShotSourceB;

    [Tooltip("蓄力枪口的发射音源（建议挂在 chargedMuzzle 上，3D）。")]
    [SerializeField] AudioSource chargedShotSource;

    [Tooltip("蓄力音播放用音源（建议挂在无人机本体）。")]
    [SerializeField] AudioSource chargeAudioSource;
    // =========================
    // ✅ Voice Announcer (Drone)
    // =========================

    public enum DroneVoiceEvent
    {
        EnterCombat,
        KillEnemy,
        PlayerKilled,
        Unblockable,
        PlayerHitByEnemy,
        PlayerHitEnemy,
        PlayerGuardBreakEnemy,
        EnemyGuardBreakPlayer,
    }

    [System.Serializable]
    public class VoiceEventConfig
    {
        public DroneVoiceEvent eventType;

        [Range(0f, 1f)]
        public float chance = 1f;

        [Min(0f)]
        public float cooldown = 3f;

        [Tooltip("优先级越高越容易压过其它播报（用于避免击杀被普通命中刷掉）。")]
        public int priority = 0;

        [Tooltip("当前正在播报时，是否允许打断并立刻播本条。")]
        public bool allowInterrupt = false;

        public AudioClip[] clips;
    }

    [Header("Voice Announcer")]
    [SerializeField] bool enableVoice = true;

    [Tooltip("无人机播报用的 AudioSource（建议 3D，挂在无人机本体）。")]
    [SerializeField] AudioSource voiceSource;

    [Tooltip("任意两次播报之间的最短间隔（防连触发刷屏）。")]
    [SerializeField, Min(0f)] float globalMinInterval = 0.25f;

    [SerializeField] VoiceEventConfig[] voiceConfigs;

    float lastAnyVoiceTime = -999f;
    int currentPlayingPriority = int.MinValue;

    // 只保留一个“待播报”（永远保留优先级更高的）
    bool hasQueued;
    DroneVoiceEvent queuedEvent;
    int queuedPriority;

    [Header("Charge VFX (Optional)")]
    [SerializeField] ParticleSystem chargeVfx;

    public DroneState State => state;
    DroneState state = DroneState.Docked;

    // hover center
    Vector3 hoverCenter;
    Vector3 hoverCenterVel;
    bool hoverCenterInited;

    // orbit
    float orbitPhase;

    // entering
    float enterStartTime;
    Vector3 enterStartPos;
    Vector3 enterRetreatPos;

    // returning
    float returnStartTime;
    Vector3 returnStartPos;
    Vector3 returnBehindPos;

    // refs
    Transform playerRoot;
    LockOnSystem lockOn;
    CombatStats ownerStats;

    // auto target
    CombatStats autoTarget;
    float nextAutoTargetTime;
    readonly Collider[] overlapBuffer = new Collider[32];

    // input (from PlayerController)
    bool fireHolding;
    float fireDownTime;

    // charge state
    bool charging;                 // 已进入“开始蓄力”
    float chargeStartTime;         // 开始蓄力时刻
    bool chargeReady;              // 蓄力完成（静默 ready）

    // pending fire (execute after facing/position updated)
    bool pendingTap;
    bool pendingCharged;

    float lastShotTime = -999f;

    void Awake()
    {
        orbitPhase = Random.value * Mathf.PI * 2f;

        if (owner != null)
        {
            var pc = owner.GetComponentInParent<PlayerController>();
            playerRoot = pc != null ? pc.transform : owner;
            lockOn = (pc != null) ? pc.GetComponent<LockOnSystem>() : owner.GetComponentInParent<LockOnSystem>();
            ownerStats = (pc != null) ? pc.GetComponent<CombatStats>() : owner.GetComponentInParent<CombatStats>();
        }

        if (yawReference == null) yawReference = playerRoot != null ? playerRoot : owner;

        if (chargeAudioSource == null)
            chargeAudioSource = GetComponent<AudioSource>();

        if (voiceSource == null)
            voiceSource = GetComponent<AudioSource>();
    }

    void OnEnable()
    {
        if (state == DroneState.Docked)
            AttachToDockImmediate();

        ResetCharge(true);
        ClearPendingFire();
        SubscribeVoiceSignals();
    }

    void OnDisable()
    {
        ResetCharge(true);
        ClearPendingFire();
        UnsubscribeVoiceSignals();
    }

    void LateUpdate()
    {
        if (owner == null || dockAnchor == null) return;

        EnsureRefs();

        bool shouldDeploy = ShouldDeployFromCombatOrLock();

        if (shouldDeploy)
        {
            if (state == DroneState.Docked || state == DroneState.Returning)
                BeginEnter();
        }
        else
        {
            if (state == DroneState.Active || state == DroneState.Entering)
                BeginReturn();
        }

        UpdateAutoTargetIfNeeded();

        switch (state)
        {
            case DroneState.Docked:
                AttachToDockImmediate();
                ResetCharge(false);
                ClearPendingFire();
                break;

            case DroneState.Entering:
                DetachFromDock();
                TickEnter();
                if (faceWhileFlying) TickFacing();
                break;

            case DroneState.Active:
                DetachFromDock();
                TickActiveOrbit();
                if (faceWhileFlying) TickFacing();

                TickChargeStateWhileHolding();
                FlushPendingFire();
                break;

            case DroneState.Returning:
                DetachFromDock();
                TickReturn();
                if (faceWhileFlying) TickFacing();
                ResetCharge(false);
                ClearPendingFire();
                break;
        }
    }

    bool ShouldDeployFromCombatOrLock()
    {
        if (EnemyState.AnyEnemyInCombat)
            return true;

        if (lockOn == null || !lockOn.IsLocked)
            return false;

        CombatStats lockedTarget = lockOn.CurrentTargetStats;
        return lockedTarget != null && !lockedTarget.IsDead;
    }

    void EnsureRefs()
    {
        if (playerRoot != null) return;

        var pc = owner.GetComponentInParent<PlayerController>();
        playerRoot = pc != null ? pc.transform : owner;

        if (yawReference == null) yawReference = playerRoot;

        if (lockOn == null && pc != null) lockOn = pc.GetComponent<LockOnSystem>();

        if (ownerStats == null && pc != null) ownerStats = pc.GetComponent<CombatStats>();
        if (ownerStats == null) ownerStats = owner.GetComponentInParent<CombatStats>();
    }

    // ================= Input API (called by PlayerController) =================

    public void NotifyFirePressed()
    {
        if (state != DroneState.Active) return;

        fireHolding = true;
        fireDownTime = Time.time;

        // 不在按下就播音/特效：只有真正进入“开始蓄力”才播（避免点按吵）
        charging = false;
        chargeReady = false;
        chargeStartTime = 0f;
    }

    public void NotifyFireReleased()
    {
        if (state != DroneState.Active) return;
        if (!fireHolding) return;

        fireHolding = false;

        float held = Time.time - fireDownTime;

        // 1) 点按：普通射击
        if (held < longPressThreshold)
        {
            pendingTap = true;
            ResetCharge(false);
            return;
        }

        // 2) 长按但未蓄力完成：取消（不发射/不耗能）
        if (!chargeReady)
        {
            ResetCharge(false);
            return;
        }

        // 3) 蓄力完成：松开才发射蓄力弹（此时才耗能）
        pendingCharged = true;
        ResetCharge(false);
    }

    // ================= Simplified Charge Logic =================

    void TickChargeStateWhileHolding()
    {
        if (!fireHolding) return;

        // 没目标或没蓄力配置，直接不进入蓄力（长按松开将视为取消）
        if (!CanStartCharged())
            return;

        float held = Time.time - fireDownTime;

        // 到阈值才开始蓄力（开始播蓄力音/特效）
        if (!charging && held >= longPressThreshold)
        {
            charging = true;
            chargeReady = false;
            chargeStartTime = Time.time;

            StartChargeAudio();
            StartChargeVfx();
            return;
        }

        if (!charging || chargeReady) return;

        // 蓄力完成：立刻静默（停止蓄力音），等待松开触发发射
        if (Time.time - chargeStartTime >= maxChargeTime)
        {
            chargeReady = true;
            StopChargeAudio(); // ✅ 完成后无声
            // VFX 是否继续由你决定：这里默认继续保持“蓄力完成”的视觉状态直到松开
        }
    }

    bool CanStartCharged()
    {
        if (chargedAttackConfig == null) return false;
        if (GetChargedPrefab() == null) return false;

        CombatStats t = GetCurrentAimTarget();
        if (t == null || t.IsDead) return false;

        return true;
    }

    void ResetCharge(bool forceStopVfx)
    {
        charging = false;
        chargeReady = false;
        chargeStartTime = 0f;

        StopChargeAudio();

        if (forceStopVfx && chargeVfx != null)
            chargeVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        else if (chargeVfx != null && chargeVfx.isPlaying)
            chargeVfx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void StartChargeAudio()
    {
        if (chargeAudioSource == null || chargeLoopClip == null) return;

        chargeAudioSource.clip = chargeLoopClip;
        chargeAudioSource.loop = true;
        chargeAudioSource.pitch = 1f;
        if (!chargeAudioSource.isPlaying)
            chargeAudioSource.Play();
    }

    void StopChargeAudio()
    {
        if (chargeAudioSource == null) return;

        if (chargeAudioSource.clip == chargeLoopClip)
        {
            chargeAudioSource.Stop();
            chargeAudioSource.clip = null;
            chargeAudioSource.loop = false;
            chargeAudioSource.pitch = 1f;
        }
    }

    void StartChargeVfx()
    {
        if (chargeVfx != null && !chargeVfx.isPlaying)
            chargeVfx.Play(true);
    }

    // ================= Pending Fire =================

    void FlushPendingFire()
    {
        if (!pendingTap && !pendingCharged) return;

        CombatStats target = GetCurrentAimTarget();
        if (target == null || target.IsDead)
        {
            ClearPendingFire();
            return;
        }

        if (pendingTap) TryFireTap(target);
        if (pendingCharged) TryFireCharged(target);

        ClearPendingFire();
    }

    void ClearPendingFire()
    {
        pendingTap = false;
        pendingCharged = false;
    }

    // ================= Auto Target =================

    void UpdateAutoTargetIfNeeded()
    {
        if (lockOn != null && lockOn.IsLocked && lockOn.CurrentTargetStats != null)
        {
            autoTarget = null;
            return;
        }

        if (!autoTargetWhenPlayerUnlocked)
        {
            autoTarget = null;
            return;
        }

        if (Time.time < nextAutoTargetTime)
            return;

        nextAutoTargetTime = Time.time + autoTargetRefreshInterval;

        Transform root = playerRoot != null ? playerRoot : owner;
        Vector3 center = root.position;

        int count = Physics.OverlapSphereNonAlloc(
            center,
            autoTargetRange,
            overlapBuffer,
            autoTargetLayers,
            QueryTriggerInteraction.Collide
        );

        CombatStats best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider c = overlapBuffer[i];
            if (c == null) continue;

            CombatStats s = c.GetComponentInParent<CombatStats>();
            if (s == null) continue;
            if (s == ownerStats) continue;
            if (s.IsDead) continue;

            float sqr = (LockTargetPointUtility.GetCapsuleCenter(s.transform) - center).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = s;
            }
        }

        autoTarget = best;
    }

    CombatStats GetCurrentAimTarget()
    {
        if (lockOn != null && lockOn.IsLocked && lockOn.CurrentTargetStats != null)
            return lockOn.CurrentTargetStats;

        return autoTarget;
    }

    // ================= Dock parenting =================

    void AttachToDockImmediate()
    {
        if (transform.parent != dockAnchor)
            transform.SetParent(dockAnchor, false);

        transform.localPosition = dockLocalPos;
        transform.localRotation = Quaternion.Euler(dockLocalEuler);
    }

    void DetachFromDock()
    {
        if (transform.parent == dockAnchor)
            transform.SetParent(null, true);
    }

    // ================= Hover center =================

    void UpdateHoverCenter(bool forceSnap)
    {
        Transform root = playerRoot != null ? playerRoot : owner;
        Vector3 desired = root.position + hoverOffset;

        if (!hoverCenterInited || forceSnap)
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

    Vector3 GetBehindOffset()
    {
        Transform root = playerRoot != null ? playerRoot : owner;
        return (-root.forward) * retreatDistance;
    }

    // ================= Entering =================

    void BeginEnter()
    {
        state = DroneState.Entering;

        UpdateHoverCenter(true);

        enterStartTime = Time.time;
        enterStartPos = transform.position;

        Vector3 behind = GetBehindOffset();
        Vector3 dockWorld = dockAnchor.TransformPoint(dockLocalPos);
        enterRetreatPos = dockWorld + behind;
    }

    void TickEnter()
    {
        UpdateHoverCenter(false);

        float t0 = Mathf.Max(0.0001f, retreatTime);
        float t1 = Mathf.Max(0.0001f, ascendTime);
        float t2 = Mathf.Max(0.0001f, toOrbitTime);

        float elapsed = Time.time - enterStartTime;

        if (elapsed < t0)
        {
            float t = Smooth01(elapsed / t0);
            transform.position = Vector3.Lerp(enterStartPos, enterRetreatPos, t);
            return;
        }

        elapsed -= t0;
        Vector3 behind = GetBehindOffset();
        Vector3 ascendTarget = new Vector3(hoverCenter.x + behind.x, hoverCenter.y, hoverCenter.z + behind.z);

        if (elapsed < t1)
        {
            float t = Smooth01(elapsed / t1);
            transform.position = Vector3.Lerp(enterRetreatPos, ascendTarget, t);
            return;
        }

        elapsed -= t1;
        float tIn = Smooth01(Mathf.Clamp01(elapsed / t2));

        orbitPhase += orbitSpeed * Time.deltaTime;
        Vector3 orbitOffset = new Vector3(Mathf.Cos(orbitPhase) * orbitRadius, 0f, Mathf.Sin(orbitPhase) * orbitRadius);
        Vector3 orbitTarget = hoverCenter + orbitOffset;

        transform.position = Vector3.Lerp(transform.position, orbitTarget, tIn);

        if (tIn >= 0.9999f)
            state = DroneState.Active;
    }

    // ================= Active Orbit =================

    void TickActiveOrbit()
    {
        UpdateHoverCenter(false);

        orbitPhase += orbitSpeed * Time.deltaTime;

        Vector3 orbitOffset = new Vector3(
            Mathf.Cos(orbitPhase) * orbitRadius,
            0f,
            Mathf.Sin(orbitPhase) * orbitRadius
        );

        Vector3 targetPos = hoverCenter + orbitOffset;

        transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-10f * Time.deltaTime));
    }

    // ================= Returning =================

    void BeginReturn()
    {
        state = DroneState.Returning;

        UpdateHoverCenter(false);

        returnStartTime = Time.time;
        returnStartPos = transform.position;

        Vector3 behind = GetBehindOffset();
        returnBehindPos = new Vector3(
            hoverCenter.x + behind.x,
            hoverCenter.y,
            hoverCenter.z + behind.z
        );
    }

    void TickReturn()
    {
        float a = Mathf.Max(0.0001f, fromOrbitTime);
        float b = Mathf.Max(0.0001f, descendTime);
        float c = Mathf.Max(0.0001f, forwardToDockTime);

        float elapsed = Time.time - returnStartTime;

        if (elapsed < a)
        {
            float t = Smooth01(elapsed / a);
            transform.position = Vector3.Lerp(returnStartPos, returnBehindPos, t);
            return;
        }

        elapsed -= a;
        Vector3 behind = GetBehindOffset();
        Vector3 dockWorld = dockAnchor.TransformPoint(dockLocalPos);
        Vector3 approach = dockWorld + behind;

        if (elapsed < b)
        {
            float t = Smooth01(elapsed / b);
            transform.position = Vector3.Lerp(returnBehindPos, approach, t);
            return;
        }

        elapsed -= b;
        float tF = Smooth01(Mathf.Clamp01(elapsed / c));

        Quaternion dockRot = dockAnchor.rotation * Quaternion.Euler(dockLocalEuler);

        transform.position = Vector3.Lerp(transform.position, dockWorld, tF);
        transform.rotation = Quaternion.Slerp(transform.rotation, dockRot, tF);

        if (tF >= 0.9999f)
        {
            state = DroneState.Docked;
            AttachToDockImmediate();
        }
    }

    // ================= Facing =================

    void TickFacing()
    {
        CombatStats targetStats = null;
        if (faceLockedTarget)
            targetStats = GetCurrentAimTarget();

        if (targetStats != null)
        {
            Vector3 aim = LockTargetPointUtility.GetCapsuleCenter(targetStats.transform);
            Vector3 dir = aim - transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up);
                target *= Quaternion.Euler(0f, yawOffsetDegrees, 0f);
                ApplyYawRotation(target);
                return;
            }
        }

        if (yawReference == null) yawReference = playerRoot != null ? playerRoot : owner;
        if (yawReference == null) return;

        float yaw = yawReference.eulerAngles.y + yawOffsetDegrees;
        Quaternion fallback = Quaternion.Euler(0f, yaw, 0f);
        ApplyYawRotation(fallback);
    }

    void ApplyYawRotation(Quaternion target)
    {
        if (snapYaw || faceYawSpeed <= 0f)
        {
            transform.rotation = target;
            return;
        }

        float k = 1f - Mathf.Exp(-faceYawSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, k);
    }

    // ================= Shooting =================

    GameObject GetTapPrefab()
    {
        if (tapProjectilePrefab != null) return tapProjectilePrefab;
        return projectilePrefabLegacy;
    }

    GameObject GetChargedPrefab()
    {
        if (chargedProjectilePrefab != null) return chargedProjectilePrefab;
        if (tapProjectilePrefab != null) return tapProjectilePrefab;
        return projectilePrefabLegacy;
    }

    void TryFireTap(CombatStats target)
    {
        if (tapAttackConfig == null) return;
        if (Time.time < lastShotTime + tapCooldown) return;

        GameObject prefab = GetTapPrefab();
        if (prefab == null) return;

        if (!TryConsumeSpecial(Mathf.Max(0, tapSpecialCost)))
        {
            PlayOneShot(chargeAudioSource, noEnergyClip, transform.position);
            return;
        }

        lastShotTime = Time.time;

        Transform mA = tapMuzzleA != null ? tapMuzzleA : muzzleLegacy;
        Transform mB = tapMuzzleB;

        int fired = 0;
        if (FireProjectileFromMuzzle(prefab, mA, target, tapAttackConfig)) fired++;
        if (FireProjectileFromMuzzle(prefab, mB, target, tapAttackConfig)) fired++;

        if (fired == 0)
            FireProjectileFromMuzzle(prefab, null, target, tapAttackConfig);

        if (tapShotClip != null)
        {
            if (mA != null) PlayOneShot(tapShotSourceA, tapShotClip, mA.position);
            if (mB != null) PlayOneShot(tapShotSourceB, tapShotClip, mB.position);
            if (mA == null && mB == null) PlayOneShot(tapShotSourceA, tapShotClip, transform.position);
        }
    }

    void TryFireCharged(CombatStats target)
    {
        if (chargedAttackConfig == null) return;
        if (Time.time < lastShotTime + chargedCooldown) return;

        GameObject prefab = GetChargedPrefab();
        if (prefab == null) return;

        if (!TryConsumeSpecial(Mathf.Max(0, chargedSpecialCost)))
        {
            PlayOneShot(chargeAudioSource, noEnergyClip, transform.position);
            return;
        }

        lastShotTime = Time.time;

        Transform m = chargedMuzzle != null ? chargedMuzzle : muzzleLegacy;
        bool ok = FireProjectileFromMuzzle(prefab, m, target, chargedAttackConfig);

        if (ok)
        {
            AudioClip clip = chargedShotClip != null ? chargedShotClip : tapShotClip;
            if (clip != null)
                PlayOneShot(chargedShotSource, clip, m != null ? m.position : transform.position);
        }
    }

    bool TryConsumeSpecial(int cost)
    {
        if (cost <= 0) return true;
        if (ownerStats == null || ownerStats.maxSpecial <= 0) return true;
        return ownerStats.ConsumeSpecial(cost);
    }

    bool FireProjectileFromMuzzle(GameObject prefab, Transform muzzle, CombatStats target, AttackConfig cfg)
    {
        if (prefab == null || cfg == null || target == null) return false;

        Transform attacker = playerRoot != null ? playerRoot : owner;

        Vector3 origin = (muzzle != null) ? muzzle.position : transform.position;

        Vector3 aim = LockTargetPointUtility.GetCapsuleCenter(target.transform);
        Vector3 dir = aim - origin;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;

        Vector3 d = dir.normalized;
        Vector3 spawnPos = origin + d * spawnForwardOffset;

        Quaternion rot = Quaternion.LookRotation(d, Vector3.up);
        GameObject go = Instantiate(prefab, spawnPos, rot);

        RangeProjectile proj = go.GetComponent<RangeProjectile>();
        if (proj == null) return true;

        AttackData data = BuildAttackDataFromConfig(cfg, attacker);
        proj.Init(attacker, d, data);
        return true;
    }

    static AttackData BuildAttackDataFromConfig(AttackConfig cfg, Transform attacker)
    {
        AttackData d = new AttackData(attacker, cfg.sourceType, cfg.hitReaction, cfg.hpDamage, cfg.staminaDamage);
        d.canBeBlocked = cfg.canBeBlocked;
        d.canBeParried = cfg.canBeParried;
        d.canBreakGuard = cfg.canBreakGuard;
        d.hasSuperArmor = cfg.hasSuperArmor;
        d.hitStopWeight = cfg.hitStopWeight;
        d.staminaPenetrationDamage = cfg.staminaPenetrationDamage;
        d.hpPenetrationDamage = cfg.hpPenetrationDamage;
        return d;
    }

    static void PlayOneShot(AudioSource src, AudioClip clip, Vector3 posFallback)
    {
        if (clip == null) return;

        if (src != null)
        {
            src.PlayOneShot(clip);
            return;
        }

        AudioSource.PlayClipAtPoint(clip, posFallback);
    }

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    void SubscribeVoiceSignals()
    {
        CombatSignals.OnPlayerEnterCombat += OnVoice_EnterCombat;
        CombatSignals.OnPlayerKillEnemy += OnVoice_KillEnemy;
        CombatSignals.OnPlayerKilledByEnemy += OnVoice_PlayerKilled;
        CombatSignals.OnPlayerHitEnemy += OnVoice_PlayerHitEnemy;
        CombatSignals.OnEnemyHitPlayer += OnVoice_PlayerHitByEnemy;
        CombatSignals.OnPlayerGuardBreakEnemy += OnVoice_PlayerGuardBreakEnemy;
        CombatSignals.OnEnemyGuardBreakPlayer += OnVoice_EnemyGuardBreakPlayer;
        CombatSignals.OnPlayerUnblockableWarning += OnVoice_Unblockable;
    }

    void UnsubscribeVoiceSignals()
    {
        CombatSignals.OnPlayerEnterCombat -= OnVoice_EnterCombat;
        CombatSignals.OnPlayerKillEnemy -= OnVoice_KillEnemy;
        CombatSignals.OnPlayerKilledByEnemy -= OnVoice_PlayerKilled;
        CombatSignals.OnPlayerHitEnemy -= OnVoice_PlayerHitEnemy;
        CombatSignals.OnEnemyHitPlayer -= OnVoice_PlayerHitByEnemy;
        CombatSignals.OnPlayerGuardBreakEnemy -= OnVoice_PlayerGuardBreakEnemy;
        CombatSignals.OnEnemyGuardBreakPlayer -= OnVoice_EnemyGuardBreakPlayer;
        CombatSignals.OnPlayerUnblockableWarning -= OnVoice_Unblockable;
    }
    void OnVoice_EnterCombat() => TryPlayVoice(DroneVoiceEvent.EnterCombat);
    void OnVoice_KillEnemy() => TryPlayVoice(DroneVoiceEvent.KillEnemy);
    void OnVoice_PlayerKilled() => TryPlayVoice(DroneVoiceEvent.PlayerKilled);
    void OnVoice_PlayerHitEnemy() => TryPlayVoice(DroneVoiceEvent.PlayerHitEnemy);
    void OnVoice_PlayerHitByEnemy() => TryPlayVoice(DroneVoiceEvent.PlayerHitByEnemy);
    void OnVoice_PlayerGuardBreakEnemy() => TryPlayVoice(DroneVoiceEvent.PlayerGuardBreakEnemy);
    void OnVoice_EnemyGuardBreakPlayer() => TryPlayVoice(DroneVoiceEvent.EnemyGuardBreakPlayer);
    void OnVoice_Unblockable(float _duration) => TryPlayVoice(DroneVoiceEvent.Unblockable);

    void TryPlayVoice(DroneVoiceEvent e)
    {
        if (!enableVoice) return;
        if (voiceSource == null) return;

        VoiceEventConfig cfg = FindVoiceConfig(e);
        if (cfg == null || cfg.clips == null || cfg.clips.Length == 0) return;

        // 概率门禁
        if (cfg.chance < 1f && Random.value > cfg.chance)
            return;

        // 触发点自身冷却
        if (IsEventOnCooldown(e, cfg.cooldown))
            return;

        // 全局最短间隔（防短时间多触发刷屏）
        if (Time.time < lastAnyVoiceTime + globalMinInterval)
            return;

        // 正在播报时：要么打断，要么排队（仅保留最高优先级）
        if (voiceSource.isPlaying)
        {
            if (cfg.allowInterrupt && cfg.priority >= currentPlayingPriority)
            {
                voiceSource.Stop();
            }
            else
            {
                QueueVoice(e, cfg.priority);
                return;
            }
        }

        // 播放
        AudioClip clip = cfg.clips[Random.Range(0, cfg.clips.Length)];
        if (clip == null) return;

        voiceSource.PlayOneShot(clip);

        lastAnyVoiceTime = Time.time;
        currentPlayingPriority = cfg.priority;

        MarkEventPlayed(e);

        hasQueued = false;
    }

    VoiceEventConfig FindVoiceConfig(DroneVoiceEvent e)
    {
        if (voiceConfigs == null) return null;
        for (int i = 0; i < voiceConfigs.Length; i++)
        {
            if (voiceConfigs[i] != null && voiceConfigs[i].eventType == e)
                return voiceConfigs[i];
        }
        return null;
    }

    // ===== 冷却：用一个小字典（不用你手动维护 lastPlayTime）=====
    System.Collections.Generic.Dictionary<DroneVoiceEvent, float> lastEventPlayTime;

    bool IsEventOnCooldown(DroneVoiceEvent e, float cooldown)
    {
        if (cooldown <= 0f) return false;
        if (lastEventPlayTime == null) lastEventPlayTime = new System.Collections.Generic.Dictionary<DroneVoiceEvent, float>();
        if (!lastEventPlayTime.TryGetValue(e, out float t)) return false;
        return Time.time < t + cooldown;
    }

    void MarkEventPlayed(DroneVoiceEvent e)
    {
        if (lastEventPlayTime == null) lastEventPlayTime = new System.Collections.Generic.Dictionary<DroneVoiceEvent, float>();
        lastEventPlayTime[e] = Time.time;
    }

    void QueueVoice(DroneVoiceEvent e, int priority)
    {
        if (!hasQueued || priority > queuedPriority)
        {
            hasQueued = true;
            queuedEvent = e;
            queuedPriority = priority;
        }
    }

    // ✅ 每帧检查：播报结束后，自动播排队的最高优先级那条
    void Update()
    {
        if (!enableVoice) return;
        if (!hasQueued) return;
        if (voiceSource == null) return;

        if (!voiceSource.isPlaying)
        {
            // 播排队的
            TryPlayVoice(queuedEvent);
        }
    }

}
