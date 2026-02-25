using UnityEngine;

public class ThirdPersonShoulderCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Pivot (look-at point on the character)")]
    public Vector3 pivotOffset = new Vector3(0f, 1.55f, 0f);
    public float distance = 3.2f;

    [Header("Mouse")]
    public float mouseSensitivityX = 3f;
    public float mouseSensitivityY = 3f;
    public bool invertY;
    public float minPitch = -35f;
    public float maxPitch = 65f;

    [Header("Lock On")]
    public float lockRotateSpeed = 12f;

    [Header("LockOn Source")]
    [SerializeField] LockOnSystem lockOn;

    [Header("Smoothing")]
    public float positionSmoothTime = 0.08f;
    public float rotationSmoothTime = 0.12f;
    public float distanceSmoothTime = 0.06f;

    [Header("Camera Collision")]
    public bool enableCollision = true;
    public LayerMask collisionMask = ~0;
    public float collisionRadius = 0.25f;
    public float collisionBuffer = 0.10f;
    public float minDistance = 0.6f;

    [Header("Lock On Pitch")]
    [Tooltip("锁定时：由目标驱动 Pitch，使目标保持在屏幕中央（动态随高度变化）。")]
    public bool lockPitchWhenLocked = true;

    [Tooltip("锁定时允许的最小俯仰角（限制过度俯视）。")]
    public float lockMinPitch = -20f;

    [Tooltip("锁定时允许的最大俯仰角（限制过度仰视）。")]
    public float lockMaxPitch = 35f;

    public float CurrentYaw => currentYaw;

    Vector3 posVelocity;

    float targetYaw;
    float targetPitch;
    float currentYaw;
    float currentPitch;
    float yawVel;
    float pitchVel;

    float currentDistance;
    float distanceVel;

    Transform lockTarget;

    public void SetLockTarget(Transform newTarget)
    {
        lockTarget = newTarget;
    }

    void Start()
    {
        ResetStateSafe(forceSnapTransform: true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate()
    {
        if (!target) return;

        if (IsGamePaused())
            return;

        if (!IsFinite(currentYaw) || !IsFinite(currentPitch) ||
            !IsFinite(targetYaw) || !IsFinite(targetPitch) ||
            !IsFinite(transform.position) || !IsFinite(transform.rotation))
        {
            ResetStateSafe(forceSnapTransform: true);
            return;
        }

        UpdateRotation();
        UpdatePositionAndRotation();
    }

    bool IsGamePaused()
    {
        if (TimeController.Instance != null && TimeController.Instance.IsPaused)
            return true;

        return Time.timeScale <= 0f;
    }

    void UpdateRotation()
    {
        ClampLockPitchRange();

        float mx = Input.GetAxis("Mouse X");
        float my = Input.GetAxis("Mouse Y");

        // 1) Pitch：非锁定（或锁定但允许鼠标） => 鼠标控制
        if (lockTarget == null || !lockPitchWhenLocked)
        {
            targetPitch += (invertY ? my : -my) * mouseSensitivityY;
            targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
        }

        // 2) Yaw/Pitch：锁定 => 由目标驱动（核心修复：考虑Y，并动态更新Pitch以居中）
        if (lockTarget != null)
        {
            Vector3 pivot = target.position + pivotOffset;

            // 用胶囊中心做锁定点（与 LockOnSystem 距离判定一致）
            Vector3 lockPoint = LockTargetPointUtility.GetCapsuleCenter(lockTarget);

            Vector3 dir = lockPoint - pivot;
            if (dir.sqrMagnitude > 0.0001f)
            {
                // Yaw：绕Y轴
                float desiredYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

                // Pitch：绕X轴（Unity约定：pitch 正值表示向下看，所以是 -dir.y）
                float planar = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
                float desiredPitch = Mathf.Atan2(-dir.y, Mathf.Max(0.0001f, planar)) * Mathf.Rad2Deg;
                float activeMinPitch = (lockTarget != null && lockPitchWhenLocked) ? lockMinPitch : minPitch;
                float activeMaxPitch = (lockTarget != null && lockPitchWhenLocked) ? lockMaxPitch : maxPitch;
                desiredPitch = Mathf.Clamp(desiredPitch, activeMinPitch, activeMaxPitch);

                float t = Time.deltaTime * lockRotateSpeed;

                targetYaw = Mathf.LerpAngle(targetYaw, desiredYaw, t);

                if (lockPitchWhenLocked)
                    targetPitch = Mathf.Lerp(targetPitch, desiredPitch, t);
            }
        }
        else
        {
            // 非锁定：Yaw 由鼠标控制
            targetYaw += mx * mouseSensitivityX;
        }

        // 3) 平滑到 currentYaw/currentPitch
        if (rotationSmoothTime <= 0f)
        {
            currentYaw = targetYaw;
            currentPitch = targetPitch;
            yawVel = 0f;
            pitchVel = 0f;
        }
        else
        {
            currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVel, rotationSmoothTime);
            currentPitch = Mathf.SmoothDampAngle(currentPitch, targetPitch, ref pitchVel, rotationSmoothTime);
        }

        if (lockTarget != null && lockPitchWhenLocked)
        {
            currentPitch = Mathf.Clamp(currentPitch, lockMinPitch, lockMaxPitch);
            targetPitch = Mathf.Clamp(targetPitch, lockMinPitch, lockMaxPitch);
        }
        else
        {
            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
            targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
        }
    }

    void ClampLockPitchRange()
    {
        if (lockMinPitch > lockMaxPitch)
        {
            float t = lockMinPitch;
            lockMinPitch = lockMaxPitch;
            lockMaxPitch = t;
        }

        lockMinPitch = Mathf.Clamp(lockMinPitch, minPitch, maxPitch);
        lockMaxPitch = Mathf.Clamp(lockMaxPitch, minPitch, maxPitch);
    }

    void UpdatePositionAndRotation()
    {
        Quaternion orbitRot = Quaternion.Euler(currentPitch, currentYaw, 0f);
        Vector3 pivot = target.position + pivotOffset;

        Vector3 desiredPos = pivot + orbitRot * new Vector3(0f, 0f, -distance);
        Vector3 dir = desiredPos - pivot;
        float desiredDist = dir.magnitude;

        if (desiredDist < 0.0001f)
            return;

        dir /= desiredDist;

        float targetDist = Mathf.Max(minDistance, distance);

        if (enableCollision)
        {
            int mask = collisionMask;
            mask &= ~(1 << target.gameObject.layer);

            // 锁定目标不应参与镜头避障，否则近距离锁定时会把相机误判“撞到目标”而突然前推。
            if (lockTarget != null)
                mask &= ~(1 << lockTarget.gameObject.layer);

            if (Physics.SphereCast(
                    pivot,
                    collisionRadius,
                    dir,
                    out RaycastHit hit,
                    desiredDist,
                    mask,
                    QueryTriggerInteraction.Ignore))
            {
                float hitDist = Mathf.Max(minDistance, hit.distance - collisionBuffer);
                targetDist = Mathf.Min(targetDist, hitDist);
            }
        }

        if (distanceSmoothTime <= 0f)
        {
            currentDistance = targetDist;
            distanceVel = 0f;
        }
        else
        {
            currentDistance = Mathf.SmoothDamp(currentDistance, targetDist, ref distanceVel, distanceSmoothTime);
        }

        Vector3 finalPos = pivot + dir * currentDistance;

        if (positionSmoothTime <= 0f)
        {
            transform.position = finalPos;
            posVelocity = Vector3.zero;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(transform.position, finalPos, ref posVelocity, positionSmoothTime);
        }

        // 仍保持 orbitRot：因为我们让 yaw+pitch 对齐 pivot->lockPoint 方向，
        // 所以“相机视线（穿过pivot）”会自动穿过 lockPoint，实现目标居中。
        transform.rotation = orbitRot;
    }

    void ResetStateSafe(bool forceSnapTransform)
    {
        float yawBase = target != null ? target.eulerAngles.y : 0f;

        targetYaw = currentYaw = yawBase;
        targetPitch = currentPitch = Mathf.Clamp(10f, minPitch, maxPitch);

        yawVel = 0f;
        pitchVel = 0f;
        distanceVel = 0f;
        posVelocity = Vector3.zero;

        currentDistance = Mathf.Max(minDistance, distance);

        if (forceSnapTransform && target != null)
        {
            Quaternion rot = Quaternion.Euler(currentPitch, currentYaw, 0f);
            Vector3 pivot = target.position + pivotOffset;
            Vector3 pos = pivot + rot * new Vector3(0f, 0f, -currentDistance);

            transform.position = pos;
            transform.rotation = rot;
        }
    }

    static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    static bool IsFinite(Quaternion q) => IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w);

    void OnEnable()
    {
        if (lockOn != null)
            lockOn.OnTargetChanged += HandleTargetChanged;
    }

    void OnDisable()
    {
        if (lockOn != null)
            lockOn.OnTargetChanged -= HandleTargetChanged;
    }

    void HandleTargetChanged(CombatStats stats)
    {
        SetLockTarget(stats != null ? stats.transform : null);
    }
}
