using System;
using System.Collections.Generic;
using UnityEngine;

public class LockOnSystem : MonoBehaviour
{
    [Header("Lock On")]
    public float lockRange = 15f;
    public LayerMask enemyMask;

    [Header("Line Of Sight")]
    [SerializeField] bool requireLineOfSight = true;
    [Tooltip("锁定时用于检测遮挡的层。建议包含地面/墙体，通常可保留默认值。")]
    [SerializeField] LayerMask lockOcclusionMask = ~0;

    [Header("Reference (usually your camera transform)")]
    [SerializeField] Transform viewTransform;

    [Header("Auto Relock")]
    [SerializeField] bool autoRelockOnTargetLost = true;

    public Transform CurrentTarget => CurrentTargetStats != null ? CurrentTargetStats.transform : null;
    public bool IsLocked => CurrentTargetStats != null;

    public CombatStats CurrentTargetStats { get; private set; }        // ✅ 仍保留（UI/逻辑用）
    public event Action<CombatStats> OnTargetChanged;

    Transform player;
    CharacterController selfCC;

    bool hadTarget;

    void Awake()
    {
        player = transform;
        selfCC = GetComponent<CharacterController>();

        if (viewTransform == null && Camera.main != null)
            viewTransform = Camera.main.transform;
    }

    void Update()
    {
        // 没锁定：不做自动锁（除非上一帧有目标且目标丢失）
        if (CurrentTargetStats == null)
        {
            if (hadTarget && autoRelockOnTargetLost)
            {
                if (!TryLockNearestInternal(out _))
                    ClearLock();
            }
            return;
        }

        hadTarget = true;

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

        // 被墙体/地形遮挡：不允许继续锁定
        if (!HasLineOfSight(CurrentTargetStats))
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
    }

    // ================= Capsule Center Point =================

    Vector3 GetSelfWorldPoint()
    {
        if (player == null) player = transform;
        if (selfCC == null) selfCC = GetComponent<CharacterController>();

        if (selfCC != null) return selfCC.bounds.center;
        return player.position;
    }

    Vector3 GetWorldPoint(CombatStats stats)
    {
        if (stats == null) return Vector3.zero;
        return LockTargetPointUtility.GetCapsuleCenter(stats.transform);
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
        if (CurrentTargetStats == null)
            return;
        CurrentTargetStats = null;

        hadTarget = false;

        OnTargetChanged?.Invoke(null);
    }

    public void SetTarget(CombatStats stats)
    {
        if (stats == null || stats.CurrentHP <= 0f || !HasLineOfSight(stats))
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
            if (!HasLineOfSight(stats)) continue;

            if (set.Add(stats))
                list.Add(stats);
        }

        return list;
    }


    bool HasLineOfSight(CombatStats target)
    {
        if (!requireLineOfSight)
            return true;

        if (target == null)
            return false;

        Vector3 from = GetSelfWorldPoint();
        Vector3 to = GetWorldPoint(target);
        Vector3 dir = to - from;
        float dist = dir.magnitude;

        if (dist <= 0.001f)
            return true;

        int mask = lockOcclusionMask.value;
        if (mask == 0)
            return true;

        var hits = Physics.RaycastAll(from, dir / dist, dist, mask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return true;

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        Transform selfRoot = transform.root;
        Transform targetRoot = target.transform.root;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null)
                continue;

            Transform hitRoot = c.transform.root;
            if (hitRoot == selfRoot)
                continue;
            if (hitRoot == targetRoot)
                return true;

            return false;
        }

        return true;
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
