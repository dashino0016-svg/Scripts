using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class BlockController : MonoBehaviour
{
    [Header("Perfect Block")]
    [SerializeField] float perfectWindowDuration = 0f;

    [Header("Guard Break Behavior")]
    [SerializeField] bool requireReleaseAfterGuardBreak = true;

    Animator anim;
    CombatStats stats;

    public bool IsBlocking { get; private set; }

    bool perfectWindow;
    Coroutine perfectCo;

    bool waitingRelease; // 破防后需要松开一次防御键

    void Awake()
    {
        anim = GetComponent<Animator>();
        stats = GetComponent<CombatStats>();
    }

    void Update()
    {
        if (!requireReleaseAfterGuardBreak || stats == null) return;

        // 一旦进入破防，要求松开一次键
        if (stats.IsGuardBroken)
            waitingRelease = true;
    }

    public void RequestBlock(bool wantBlock)
    {
        // 破防后：必须先松开一次键（你的 requireReleaseAfterGuardBreak=true 时）
        if (requireReleaseAfterGuardBreak && waitingRelease)
        {
            if (!wantBlock) waitingRelease = false;
            ForceReleaseBlock();
            return;
        }

        // ✅ 任何时候只要玩家想保持/进入防御，就必须满足 CanBlock
        if (wantBlock)
        {
            if (stats != null && !stats.CanBlock)
            {
                ForceReleaseBlock();
                return;
            }
        }

        if (IsBlocking == wantBlock)
            return;

        IsBlocking = wantBlock;
        anim.SetBool("IsBlocking", IsBlocking);

        if (stats != null)
            stats.SetBlocking(IsBlocking);

        if (IsBlocking)
        {
            if (perfectCo != null) StopCoroutine(perfectCo);
            perfectWindow = true;
            perfectCo = StartCoroutine(ClosePerfectWindowRealtime());
        }
        else
        {
            perfectWindow = false;
            if (perfectCo != null) StopCoroutine(perfectCo);
            perfectCo = null;
        }
    }


    public void ForceReleaseBlock()
    {
        if (!IsBlocking && !perfectWindow)
            return;

        IsBlocking = false;
        perfectWindow = false;

        if (perfectCo != null) StopCoroutine(perfectCo);
        perfectCo = null;

        anim.SetBool("IsBlocking", false);

        if (stats != null)
            stats.SetBlocking(false);
    }

    IEnumerator ClosePerfectWindowRealtime()
    {
        yield return new WaitForSecondsRealtime(perfectWindowDuration);
        perfectWindow = false;
        perfectCo = null;
    }

    public bool IsInPerfectWindow => IsBlocking && perfectWindow;
}
