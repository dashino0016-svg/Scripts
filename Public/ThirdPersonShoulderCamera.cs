﻿using UnityEngine;

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
        if (lockPitchWhenLocked && lockTarget == null && newTarget != null)
            lockedPitch = targetPitch;

        lockTarget = newTarget;

        if (lockPitchWhenLocked && lockTarget == null)
            targetPitch = currentPitch;
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

        // ✅ 关键修复：暂停时不更新相机，避免 SmoothDamp 在 deltaTime=0 时产生 NaN
        if (IsGamePaused())
            return;

        // ✅ 若曾经变成 NaN，自动恢复
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
        // 兼容你项目的 TimeController 暂停
        if (TimeController.Instance != null && TimeController.Instance.IsPaused)
            return true;

        // 兜底：timeScale==0 也视为暂停
        return Time.timeScale <= 0f;
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

                // rotationSmoothTime<=0 时，直接瞬间对齐（避免 SmoothDamp/插值异常）
                float lerpT = Time.deltaTime * lockRotateSpeed;
                targetYaw = Mathf.LerpAngle(targetYaw, desiredYaw, lerpT);
            }
        }
        else
        {
            targetYaw += mx * mouseSensitivityX;
        }

        // ✅ smoothTime 为 0 时，直接赋值，避免 SmoothDampAngle 内部除 0/Inf*0
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

        transform.rotation = orbitRot;
    }

    void ResetStateSafe(bool forceSnapTransform)
    {
        // 用 target 的朝向作为 yaw 基准，避免 transform 已经 NaN 时取 eulerAngles 继续 NaN
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
