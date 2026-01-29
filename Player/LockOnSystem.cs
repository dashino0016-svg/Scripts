using System;
using System.Collections.Generic;
using UnityEngine;

public class LockOnSystem : MonoBehaviour
{
    [Header("Lock On")]
    public float lockRange = 15f;
    public LayerMask enemyMask;

    [Header("Reference (usually your camera transform)")]
    [SerializeField] Transform viewTransform;

    [Header("Auto Relock")]
    [SerializeField] bool autoRelockOnTargetLost = true;

    // ✅ 对外：相机 / UI / 面向 等都应该用这个点（胶囊中心）
    public Transform CurrentTarget => (CurrentTargetStats != null) ? EnsureTargetPoint() : null;
    public bool IsLocked => CurrentTargetStats != null;

    public CombatStats CurrentTargetStats { get; private set; }        // UI/逻辑仍可用
    public event Action<CombatStats> OnTargetChanged;

    CharacterController selfCC;

    bool hadTarget;

    // 运行时的“锁定点”对象（跟随目标胶囊中心）
    Transform currentTargetPoint;

    const string TargetPointName = "__LockOnTargetPoint";

    void Awake()
    {
        selfCC = GetComponent<CharacterController>();

        if (viewTransform == null && Camera.main != null)
            viewTransform = Camera.main.transform;

        EnsureTargetPoint();
    }

    void Update()
    {
        // ===== 没目标（或目标被 Destroy 了导致 == null）=====
        if (CurrentTargetStats == null)
        {
            if (hadTarget)
            {
                if (autoRelockOnTargetLost)
                {
                    if (!TryLockNearestInternal(out _))
                        ClearLock();
                }
                else
                {
                    ClearLock();
                }
            }
            return;
        }

        hadTarget = true;

        // ✅ 每帧刷新锁定点位置：保证相机/UI 永远对准“胶囊中心”
        RefreshCurrentTargetPoint();

        // 目标死亡：自动锁最近存活敌人；没有就清锁
        if (CurrentTargetStats.CurrentHP <= 0f)
        {
            if (autoRelockOnTargetLost)
            {
                if (!TryLockNearestInternal(out _))
                    ClearLock();
            }
            else
            {
                ClearLock();
            }
            return;
        }

        // 超距：按“胶囊中心-胶囊中心”算距离
        float distSq = (GetWorldPoint(CurrentTargetStats) - GetSelfWorldPoint()).sqrMagnitude;
        if (distSq > lockRange * lockRange)
        {
            ClearLock();
            return;
        }
    }

    // ================= Capsule Center Point =================

    Vector3 GetSelfWorldPoint()
    {
        if (selfCC == null) selfCC = GetComponent<CharacterController>();
        if (selfCC != null) return selfCC.bounds.center;
        return transform.position;
    }

    Vector3 GetWorldPoint(CombatStats stats)
    {
        if (stats == null) return Vector3.zero;
        return LockTargetPointUtility.GetCapsuleCenter(stats.transform);
    }

    Transform EnsureTargetPoint()
    {
        if (currentTargetPoint != null) return currentTargetPoint;

        var go = new GameObject(TargetPointName);
        go.hideFlags = HideFlags.HideInHierarchy;
        currentTargetPoint = go.transform;
        currentTargetPoint.SetParent(transform, worldPositionStays: false);
        currentTargetPoint.localPosition = Vector3.zero;

        return currentTargetPoint;
    }

    void RefreshCurrentTargetPoint()
    {
        if (CurrentTargetStats == null) return;
        EnsureTargetPoint().position = GetWorldPoint(CurrentTargetStats);
    }

    // ================= Public API =================

    public void TryLockNearest()
    {
        TryLockNearestInternal(out _);
    }

    bool TryLockNearestInternal(out CombatStats locked)
    {
        locked = null;

        var candidates = GatherCandidates();
        if (candidates.Count == 0) return false;

        CombatStats nearest = null;
        float bestDistSq = float.MaxValue;

        Vector3 self = GetSelfWorldPoint();

        foreach (var s in candidates)
        {
            float dSq = (GetWorldPoint(s) - self).sqrMagnitude;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                nearest = s;
            }
        }

        if (nearest != null)
        {
            SetTargetInternal(nearest);
            locked = nearest;
            return true;
        }

        return false;
    }

    public void ClearLock()
    {
        // ✅ 允许“目标被 Destroy 了”时也能正确清理 hadTarget，避免每帧重复尝试
        if (CurrentTargetStats == null && !hadTarget)
            return;

        CurrentTargetStats = null;
        hadTarget = false;

        OnTargetChanged?.Invoke(null);
    }

    public void SetTarget(CombatStats stats)
    {
        if (stats == null || stats.CurrentHP <= 0f)
        {
            ClearLock();
            return;
        }

        SetTargetInternal(stats);
    }

    // ===================== 切换目标 =====================

    public void SwitchTargetLeft() => SwitchTarget(-1);
    public void SwitchTargetRight() => SwitchTarget(+1);

    void SwitchTarget(int dir)
    {
        if (CurrentTargetStats == null)
        {
            TryLockNearest();
            return;
        }

        var candidates = GatherCandidates();
        if (candidates.Count <= 1) return;

        Vector3 refFwd = GetReferenceForward();
        Vector3 self = GetSelfWorldPoint();

        Vector3 curDir = GetWorldPoint(CurrentTargetStats) - self;
        curDir.y = 0f;
        if (curDir.sqrMagnitude < 0.0001f) return;
        curDir.Normalize();

        float curAngle = Vector3.SignedAngle(refFwd, curDir, Vector3.up);

        CombatStats best = null;
        float bestDelta = float.MaxValue;

        foreach (var s in candidates)
        {
            if (s == CurrentTargetStats) continue;

            Vector3 to = GetWorldPoint(s) - self;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) continue;
            to.Normalize();

            float ang = Vector3.SignedAngle(refFwd, to, Vector3.up);

            float delta = DeltaAngleDir(curAngle, ang, dir);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = s;
            }
        }

        if (best == null)
        {
            float wrapBest = float.MaxValue;
            foreach (var s in candidates)
            {
                if (s == CurrentTargetStats) continue;

                Vector3 to = GetWorldPoint(s) - self;
                to.y = 0f;
                if (to.sqrMagnitude < 0.0001f) continue;
                to.Normalize();

                float ang = Vector3.SignedAngle(refFwd, to, Vector3.up);
                float delta = DeltaAngleDir(curAngle, ang, dir, allowWrap: true);
                if (delta < wrapBest)
                {
                    wrapBest = delta;
                    best = s;
                }
            }
        }

        if (best != null)
            SetTargetInternal(best);
    }

    float DeltaAngleDir(float cur, float target, int dir, bool allowWrap = false)
    {
        float a = cur; if (a < 0) a += 360f;
        float b = target; if (b < 0) b += 360f;

        float delta = (b - a + 360f) % 360f;
        if (dir < 0) delta = (a - b + 360f) % 360f;

        if (!allowWrap)
        {
            if (delta > 180f) return float.MaxValue;
        }

        return delta;
    }

    // ================= Candidate Gather =================

    List<CombatStats> GatherCandidates()
    {
        Collider[] hits = Physics.OverlapSphere(
            GetSelfWorldPoint(),
            lockRange,
            enemyMask,
            QueryTriggerInteraction.Ignore
        );

        var set = new HashSet<CombatStats>();
        var list = new List<CombatStats>();

        for (int i = 0; i < hits.Length; i++)
        {
            var stats = hits[i].GetComponentInParent<CombatStats>();
            if (stats == null) continue;
            if (stats.transform.root == transform.root) continue;
            if (stats.CurrentHP <= 0f) continue;

            if (set.Add(stats))
                list.Add(stats);
        }

        return list;
    }

    Vector3 GetReferenceForward()
    {
        Vector3 fwd = (viewTransform != null) ? viewTransform.forward : transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
        return fwd.normalized;
    }

    void SetTargetInternal(CombatStats stats)
    {
        CurrentTargetStats = stats;
        hadTarget = true;

        RefreshCurrentTargetPoint();

        OnTargetChanged?.Invoke(CurrentTargetStats);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(GetSelfWorldPoint(), lockRange);
    }
#endif
}
