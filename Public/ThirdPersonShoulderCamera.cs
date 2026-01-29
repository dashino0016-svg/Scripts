using UnityEngine;

public class ThirdPersonShoulderCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Offset")]
    public Vector3 shoulderOffset = new Vector3(0.6f, 1.6f, -2.5f);
    public bool rightShoulder = true;

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

    [Header("Lock On Pitch")]
    public bool lockPitchWhenLocked = true;   // 锁定时冻结上下视角
    float lockedPitch;

    public float CurrentYaw => currentYaw;

    Vector3 posVelocity;

    float targetYaw;
    float targetPitch;
    float currentYaw;
    float currentPitch;
    float yawVel;
    float pitchVel;

    Transform lockTarget;

    public void SetLockTarget(Transform target)
    {
        // 从“未锁定 -> 锁定”时，记录当前 pitch
        if (lockPitchWhenLocked && lockTarget == null && target != null)
        {
            lockedPitch = targetPitch; // 或 currentPitch 都行，targetPitch 更贴近你当前输入状态
        }

        lockTarget = target;

        // 从“锁定 -> 解锁”时，防止 pitch 跳变
        if (lockPitchWhenLocked && lockTarget == null)
        {
            targetPitch = currentPitch;
        }
    }


    void Start()
    {
        Vector3 e = transform.eulerAngles;
        targetYaw = currentYaw = e.y;
        targetPitch = currentPitch = e.x;

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
            // 锁定时冻结 pitch
            targetPitch = Mathf.Clamp(lockedPitch, minPitch, maxPitch);
        }


        if (lockTarget != null)
        {
            Vector3 dir = lockTarget.position - target.position;
            dir.y = 0f;

            float desiredYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            targetYaw = Mathf.LerpAngle(
                targetYaw,
                desiredYaw,
                Time.deltaTime * lockRotateSpeed
            );
        }
        else
        {
            targetYaw += mx * mouseSensitivityX;
        }

        currentYaw = Mathf.SmoothDampAngle(
            currentYaw, targetYaw, ref yawVel, rotationSmoothTime);

        currentPitch = Mathf.SmoothDampAngle(
            currentPitch, targetPitch, ref pitchVel, rotationSmoothTime);
    }

    void UpdatePositionAndRotation()
    {
        Vector3 offset = shoulderOffset;
        offset.x *= rightShoulder ? 1f : -1f;

        Quaternion camRot = Quaternion.Euler(currentPitch, currentYaw, 0f);
        Vector3 desiredPos = target.position + camRot * offset;

        transform.position = Vector3.SmoothDamp(
            transform.position, desiredPos, ref posVelocity, positionSmoothTime);

        transform.rotation = camRot;
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
        // stats 为 null 表示清锁（敌人死/超距/手动取消）
        lockTarget = stats != null ? stats.transform : null;
    }

}
