using UnityEngine;

public class MiniAssistantDroneController : MonoBehaviour
{
    public enum DroneState { Docked, Entering, Active, Returning }

    [Header("Anchors")]
    [SerializeField] Transform owner;          // 玩家根（或任意在玩家层级下的物体）
    [SerializeField] Transform dockAnchor;     // 背部挂点（Bone/Socket）
    [SerializeField] Vector3 dockLocalPos;     // Docked 时 local 微调
    [SerializeField] Vector3 dockLocalEuler;   // Docked 时 local 微调

    [Header("Hover (like helicopter)")]
    [SerializeField] Vector3 hoverOffset = new Vector3(0f, 2.0f, 0f);
    [SerializeField] float orbitRadius = 0.45f;
    [SerializeField] float orbitSpeed = 2.5f; // rad/s

    [Header("Follow Lag")]
    [SerializeField, Min(0f)] float hoverCenterSmoothTime = 0.28f;
    [SerializeField, Min(0f)] float hoverCenterMaxSpeed = 30f;
    [SerializeField, Min(0f)] float teleportSnapDistance = 25f;

    [Header("Enter/Return Path")]
    [Tooltip("出背先后退的距离（沿玩家 backward）。")]
    [SerializeField, Min(0f)] float retreatDistance = 0.45f;

    [SerializeField, Min(0f)] float retreatTime = 0.12f;  // 后退
    [SerializeField, Min(0f)] float ascendTime = 0.20f;   // 上升
    [SerializeField, Min(0f)] float toOrbitTime = 0.22f;  // 入轨

    [SerializeField, Min(0f)] float fromOrbitTime = 0.18f;
    [SerializeField, Min(0f)] float descendTime = 0.20f;
    [SerializeField, Min(0f)] float forwardToDockTime = 0.12f;

    [Header("Facing")]
    [Tooltip("飞行中是否旋转朝向。Docked 不会改朝向（由 dockAnchor 决定）。")]
    [SerializeField] bool faceWhileFlying = true;

    [Tooltip("锁定时始终面向锁定目标（推荐开）。")]
    [SerializeField] bool faceLockedTarget = true;

    [SerializeField, Min(0f)] float faceYawSpeed = 14f;

    [Header("Facing Reference (fallback)")]
    [SerializeField] Transform yawReference;        // 未锁定时的朝向参考（建议玩家根）
    [SerializeField] float yawOffsetDegrees = 0f;   // 模型固定偏角修正（0/90/-90/180）
    [SerializeField] bool snapYaw = false;          // true=不平滑直接对齐

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
    Transform playerRoot;     // PlayerController 根（更稳定）
    LockOnSystem lockOn;

    void Awake()
    {
        orbitPhase = Random.value * Mathf.PI * 2f;

        // 自动找玩家根（不强耦合）
        if (owner != null)
        {
            var pc = owner.GetComponentInParent<PlayerController>();
            playerRoot = pc != null ? pc.transform : owner;
            lockOn = (pc != null) ? pc.GetComponent<LockOnSystem>() : owner.GetComponentInParent<LockOnSystem>();
        }
        else
        {
            playerRoot = null;
            lockOn = null;
        }

        if (yawReference == null) yawReference = playerRoot != null ? playerRoot : owner;
    }

    void OnEnable()
    {
        // 初始确保 Docked 贴合（避免第一帧抖一下）
        if (state == DroneState.Docked)
            AttachToDockImmediate();
    }

    void LateUpdate()
    {
        if (owner == null || dockAnchor == null) return;

        if (playerRoot == null)
        {
            var pc = owner.GetComponentInParent<PlayerController>();
            playerRoot = pc != null ? pc.transform : owner;
            if (yawReference == null) yawReference = playerRoot;
            if (lockOn == null && pc != null) lockOn = pc.GetComponent<LockOnSystem>();
        }

        // === 状态切换放 LateUpdate：拿到动画结算后的 dockAnchor 位置，抖动最少 ===
        bool inCombat = EnemyState.AnyEnemyInCombat;

        if (inCombat)
        {
            if (state == DroneState.Docked || state == DroneState.Returning)
                BeginEnter();
        }
        else
        {
            if (state == DroneState.Active || state == DroneState.Entering)
                BeginReturn();
        }

        // === 执行状态 ===
        switch (state)
        {
            case DroneState.Docked:
                // ✅ 关键：Docked 直接挂到 dockAnchor，写 local，不追世界坐标 → 消灭抖动/闪烁
                AttachToDockImmediate();
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
                break;

            case DroneState.Returning:
                DetachFromDock();
                TickReturn();
                if (faceWhileFlying) TickFacing();
                break;
        }
    }

    // ================= Dock parenting (fix jitter) =================

    void AttachToDockImmediate()
    {
        if (transform.parent != dockAnchor)
            transform.SetParent(dockAnchor, false);

        transform.localPosition = dockLocalPos;
        transform.localRotation = Quaternion.Euler(dockLocalEuler);
    }

    void DetachFromDock()
    {
        // 飞行时必须脱离 dockAnchor（否则会被骨骼动画/转身带着抖）
        if (transform.parent == dockAnchor)
            transform.SetParent(null, true); // 保持世界坐标
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

    // ================= Entering: retreat -> ascend -> to orbit =================

    void BeginEnter()
    {
        state = DroneState.Entering;

        UpdateHoverCenter(true);

        enterStartTime = Time.time;
        enterStartPos = transform.position;

        Vector3 behind = GetBehindOffset();

        // 后退目标：以背部挂点为基准往后
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

        // 1) 后退
        if (elapsed < t0)
        {
            float t = Smooth01(elapsed / t0);
            transform.position = Vector3.Lerp(enterStartPos, enterRetreatPos, t);
            return;
        }

        // 2) 上升到 hover 高度（仍在身后）
        elapsed -= t0;
        Vector3 behind = GetBehindOffset();
        Vector3 ascendTarget = new Vector3(hoverCenter.x + behind.x, hoverCenter.y, hoverCenter.z + behind.z);

        if (elapsed < t1)
        {
            float t = Smooth01(elapsed / t1);
            transform.position = Vector3.Lerp(enterRetreatPos, ascendTarget, t);
            return;
        }

        // 3) 入轨
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

        // 1) 回到身后上方
        if (elapsed < a)
        {
            float t = Smooth01(elapsed / a);
            transform.position = Vector3.Lerp(returnStartPos, returnBehindPos, t);
            return;
        }

        // 2) 下降到背部高度（仍在身后）
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

        // 3) 前移回背部并贴合旋转
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

    // ================= Facing (lock-on fix) =================

    void TickFacing()
    {
        // 锁定目标：始终面向目标（yaw-only）
        if (faceLockedTarget && lockOn != null && lockOn.IsLocked && lockOn.CurrentTargetStats != null)
        {
            Vector3 aim = LockTargetPointUtility.GetCapsuleCenter(lockOn.CurrentTargetStats.transform);
            Vector3 dir = aim - transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up);
                target *= Quaternion.Euler(0f, yawOffsetDegrees, 0f); // ✅ 让锁定也吃同一个偏移
                ApplyYawRotation(target);
                return;
            }
        }

        // 未锁定：按 yawReference 对齐（yaw-only）
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

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
