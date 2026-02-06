using UnityEngine;

public class ThirdPersonShoulderCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Pivot (look-at point on the character)")]
    public Vector3 pivotOffset = new Vector3(0f, 1.55f, 0f); // 人物胸口/头部附近
    public float distance = 3.2f;                            // 默认相机距离

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
    public LayerMask collisionMask = ~0;     // 建议只勾 Environment/Default 等，不要勾 Player
    public float collisionRadius = 0.25f;    // 相机“体积”
    public float collisionBuffer = 0.10f;    // 离墙留一点缝
    public float minDistance = 0.6f;         // 最近不要贴太脸

    [Header("Lock On Pitch")]
    public bool lockPitchWhenLocked = true;
    float lockedPitch;

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
        // 未锁->锁：记录当前 pitch
        if (lockPitchWhenLocked && lockTarget == null && newTarget != null)
            lockedPitch = targetPitch;

        lockTarget = newTarget;

        // 锁->未锁：避免 pitch 跳变
        if (lockPitchWhenLocked && lockTarget == null)
            targetPitch = currentPitch;
    }

    void Start()
    {
        Vector3 e = transform.eulerAngles;
        targetYaw = currentYaw = e.y;
        targetPitch = currentPitch = e.x;

        currentDistance = Mathf.Max(minDistance, distance);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate()
    {
        if (!target) return;

        UpdateRotation();
        UpdatePositionAndRotation();
    }

    void UpdateRotation()
    {
        float mx = Input.GetAxis("Mouse X");
        float my = Input.GetAxis("Mouse Y");

        if (lockTarget == null || !lockPitchWhenLocked)
        {
            targetPitch += (invertY ? my : -my) * mouseSensitivityY;
            targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
        }
        else
        {
            targetPitch = Mathf.Clamp(lockedPitch, minPitch, maxPitch);
        }

        if (lockTarget != null)
        {
            Vector3 dir = lockTarget.position - target.position;
            dir.y = 0f;

            if (dir.sqrMagnitude > 0.0001f)
            {
                float desiredYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                targetYaw = Mathf.LerpAngle(targetYaw, desiredYaw, Time.deltaTime * lockRotateSpeed);
            }
        }
        else
        {
            targetYaw += mx * mouseSensitivityX;
        }

        currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVel, rotationSmoothTime);
        currentPitch = Mathf.SmoothDampAngle(currentPitch, targetPitch, ref pitchVel, rotationSmoothTime);
    }

    void UpdatePositionAndRotation()
    {
        Quaternion orbitRot = Quaternion.Euler(currentPitch, currentYaw, 0f);

        Vector3 pivot = target.position + pivotOffset;

        // 理想相机位置（人物居中）
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
            // 尽量别打到玩家自己
            mask &= ~(1 << target.gameObject.layer);

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

        currentDistance = Mathf.SmoothDamp(currentDistance, targetDist, ref distanceVel, distanceSmoothTime);

        Vector3 finalPos = pivot + dir * currentDistance;

        transform.position = Vector3.SmoothDamp(transform.position, finalPos, ref posVelocity, positionSmoothTime);
        transform.rotation = orbitRot;
    }

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
