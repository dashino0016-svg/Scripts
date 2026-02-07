using UnityEngine;

public class MiniAssistantDroneController : MonoBehaviour
{
    public enum DroneState { Docked, Entering, Active, Returning }

    [Header("Anchors")]
    [SerializeField] Transform owner;          // 玩家根
    [SerializeField] Transform dockAnchor;     // 背部挂点
    [SerializeField] Vector3 dockLocalPos;     // 挂点微调
    [SerializeField] Vector3 dockLocalEuler;   // 挂点微调（保证 Docked 面向玩家前方）

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

    [SerializeField, Min(0f)] float retreatTime = 0.12f;
    [SerializeField, Min(0f)] float ascendTime = 0.20f;
    [SerializeField, Min(0f)] float toOrbitTime = 0.22f;

    [SerializeField, Min(0f)] float fromOrbitTime = 0.18f;
    [SerializeField, Min(0f)] float descendTime = 0.20f;
    [SerializeField, Min(0f)] float forwardToDockTime = 0.12f;

    [Header("Rotation")]
    [Tooltip("飞行中让无人机朝向跟随玩家（仅 yaw）。")]
    [SerializeField] bool yawFollowOwnerWhileFlying = true;
    [SerializeField, Min(0f)] float yawFollowSpeed = 14f;

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
    Vector3 enterAscendPos;
    Vector3 enterOrbitPos;

    // returning
    float returnStartTime;
    Vector3 returnStartPos;
    Vector3 returnBehindPos;
    Vector3 returnDockApproachPos;

    void Awake()
    {
        orbitPhase = Random.value * Mathf.PI * 2f;
    }

    void Update()
    {
        if (owner == null || dockAnchor == null) return;

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

        switch (state)
        {
            case DroneState.Docked:
                SnapToDock();
                break;

            case DroneState.Entering:
                TickEnter();
                break;

            case DroneState.Active:
                TickActiveOrbit();
                break;

            case DroneState.Returning:
                TickReturn();
                break;
        }

        if (yawFollowOwnerWhileFlying && state != DroneState.Docked)
            TickYawFollowOwner();
    }

    // ================= Dock =================

    void SnapToDock()
    {
        transform.position = GetDockWorldPos();
        transform.rotation = GetDockWorldRot();
    }

    Vector3 GetDockWorldPos() => dockAnchor.TransformPoint(dockLocalPos);
    Quaternion GetDockWorldRot() => dockAnchor.rotation * Quaternion.Euler(dockLocalEuler);

    // ================= Hover Center =================

    void UpdateHoverCenter(bool forceSnap)
    {
        Vector3 desired = owner.position + hoverOffset;

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

    Vector3 GetBehindOffset() => (-owner.forward) * retreatDistance;

    // ================= Entering: retreat -> ascend -> to orbit =================

    void BeginEnter()
    {
        state = DroneState.Entering;

        UpdateHoverCenter(true);

        enterStartTime = Time.time;
        enterStartPos = transform.position;

        Vector3 behind = GetBehindOffset();

        // 1) 后退目标：以“背部当前点”为起点，往玩家 backward 移
        enterRetreatPos = GetDockWorldPos() + behind;

        // 2) 上升目标：到 hover 高度，但仍在身后（用 hoverCenter 的 Y）
        enterAscendPos = new Vector3(
            hoverCenter.x + behind.x,
            hoverCenter.y,
            hoverCenter.z + behind.z
        );

        // 3) 入轨目标：hoverCenter + orbitOffset（直接落到轨道点，避免突然侧移）
        Vector3 orbitOffset = new Vector3(Mathf.Cos(orbitPhase) * orbitRadius, 0f, Mathf.Sin(orbitPhase) * orbitRadius);
        enterOrbitPos = hoverCenter + orbitOffset;
    }

    void TickEnter()
    {
        if (owner == null) { state = DroneState.Docked; return; }

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
        if (elapsed < t1)
        {
            float t = Smooth01(elapsed / t1);

            // 上升段：终点的 XZ 跟随 hoverCenter（有滞后），保持“在身后”感觉
            Vector3 behind = GetBehindOffset();
            Vector3 ascendTarget = new Vector3(hoverCenter.x + behind.x, hoverCenter.y, hoverCenter.z + behind.z);

            transform.position = Vector3.Lerp(enterRetreatPos, ascendTarget, t);
            return;
        }

        elapsed -= t1;
        {
            float t = Smooth01(Mathf.Clamp01(elapsed / t2));

            // 入轨段：终点 orbitPos 每帧随 hoverCenter 更新（依然有滞后）
            Vector3 orbitOffset = new Vector3(Mathf.Cos(orbitPhase) * orbitRadius, 0f, Mathf.Sin(orbitPhase) * orbitRadius);
            Vector3 orbitTarget = hoverCenter + orbitOffset;

            transform.position = Vector3.Lerp(transform.position, orbitTarget, t);

            if (t >= 0.9999f)
                state = DroneState.Active;
        }
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

        // 轻量指数 Lerp（风格接近你直升机）
        transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-10f * Time.deltaTime));
    }

    // ================= Returning: from orbit -> descend -> forward to dock =================

    void BeginReturn()
    {
        state = DroneState.Returning;

        UpdateHoverCenter(false);

        returnStartTime = Time.time;
        returnStartPos = transform.position;

        Vector3 behind = GetBehindOffset();

        // 1) 先退到“身后上方”的点（可控）
        returnBehindPos = new Vector3(
            hoverCenter.x + behind.x,
            hoverCenter.y,
            hoverCenter.z + behind.z
        );

        // 2) 下降到“背部高度的身后点”
        returnDockApproachPos = GetDockWorldPos() + behind;
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
        if (elapsed < b)
        {
            float t = Smooth01(elapsed / b);

            // 下降段：目标点随 dockAnchor 变化（玩家移动时不会“丢”太多）
            Vector3 behind = GetBehindOffset();
            Vector3 approach = GetDockWorldPos() + behind;

            transform.position = Vector3.Lerp(returnBehindPos, approach, t);
            return;
        }

        elapsed -= b;
        {
            float t = Smooth01(Mathf.Clamp01(elapsed / c));

            Vector3 dockPos = GetDockWorldPos();
            Quaternion dockRot = GetDockWorldRot();

            transform.position = Vector3.Lerp(transform.position, dockPos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, dockRot, t);

            if (t >= 0.9999f)
            {
                state = DroneState.Docked;
                SnapToDock();
            }
        }
    }

    // ================= Rotation =================

    void TickYawFollowOwner()
    {
        Vector3 fwd = owner.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) return;

        Quaternion target = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        float k = 1f - Mathf.Exp(-yawFollowSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, k);
    }

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
