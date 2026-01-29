using System.Collections;
using UnityEngine;

public class AssassinationSystem : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] LockOnSystem lockOn;
    [SerializeField] PlayerController playerController;
    [SerializeField] CombatReceiver playerReceiver;
    [SerializeField] LockMarkerFollower lockMarkerFollower;

    [Tooltip("玩家 CombatStats（加 Special）。不填则自动抓。")]
    [SerializeField] CombatStats playerStats;

    [Header("Anim (Triggers)")]
    [SerializeField] string playerAssassinateTrigger = "Assassinate";
    [SerializeField] string enemyAssassinatedTrigger = "Assassinated";
    [SerializeField] string playerExecuteTrigger = "Execute";
    [SerializeField] string enemyExecutedTrigger = "Executed";

    [Header("Anim (State Fallback Names)")]
    [Tooltip("Trigger 未能切入时的兜底状态名（Base Layer）。通常就和动画 State 名一致。")]
    [SerializeField] string playerAssassinateStateName = "Assassinate";
    [SerializeField] string playerExecuteStateName = "Execute";
    [SerializeField] string enemyAssassinatedStateName = "Assassinated";
    [SerializeField] string enemyExecutedStateName = "Executed";

    [Header("Animator Params")]
    [SerializeField] string crouchBoolName = "IsCrouching";

    [Header("Snap (Anchor Truth)")]
    public bool snapOnStart = true;
    public Vector3 snapWorldOffset = Vector3.zero;
    public bool snapRotationFull = true;

    [Header("Optional Back Angle Gate (Assassination Only)")]
    public bool useBackAngleGate = false;
    [Range(0f, 180f)] public float backAngle = 60f;

    [Header("Special Gain (Both)")]
    public int assassinationSpecialGain = 40;
    public int executeSpecialGain = 60;

    [Header("Transition Fallback")]
    [Tooltip("收到输入后，等待这么久仍未进入目标状态，则强制 CrossFade。建议 0.02~0.08")]
    [SerializeField] float forceStateDelay = 0.04f;

    Animator anim;
    BlockController block;
    PlayerAbilitySystem abilitySystem;

    CharacterController cc;
    PlayerMove move;
    SwordController sword;

    // RootMotion/CC 缓存：用于开始时冻结位移，结束时恢复
    bool cachedApplyRootMotion;
    bool cachedCcEnabled;
    bool cachedMoveEnabled;

    // runtime
    bool isAssassinating;
    public bool IsAssassinating => isAssassinating;

    enum TakedownType { None, Assassinate, Execute }
    TakedownType currentType = TakedownType.None;

    CombatStats currentTargetStats;
    EnemyController currentEnemyController;
    AssassinationTarget currentTargetCfg;
    Animator currentEnemyAnim;

    // ✅ 输入由 PlayerController 统一管理：这里仅接收“本帧按下”
    bool takedownPressedThisFrame;

    public void NotifyTakedownPressed()
    {
        takedownPressedThisFrame = true;
    }

    void Awake()
    {
        if (lockOn == null) lockOn = GetComponent<LockOnSystem>();
        if (playerController == null) playerController = GetComponent<PlayerController>();
        if (playerReceiver == null) playerReceiver = GetComponent<CombatReceiver>();
        if (lockMarkerFollower == null) lockMarkerFollower = FindObjectOfType<LockMarkerFollower>();

        anim = GetComponent<Animator>();
        block = GetComponent<BlockController>();
        abilitySystem = GetComponent<PlayerAbilitySystem>();

        cc = GetComponent<CharacterController>();
        move = GetComponent<PlayerMove>();
        sword = GetComponentInChildren<SwordController>();

        if (playerStats == null) playerStats = GetComponent<CombatStats>();

        if (lockMarkerFollower != null)
            lockMarkerFollower.SetAssassinationReady(false);
    }

    void Update()
    {
        // ✅ 每帧结束时清掉“本帧按下”
        bool pressed = takedownPressedThisFrame;
        takedownPressedThisFrame = false;

        if (isAssassinating)
        {
            if (lockMarkerFollower != null)
                lockMarkerFollower.SetAssassinationReady(true);
            return;
        }

        bool can = EvaluateCanTakedown(out var type, out var cfg, out var targetStats);

        if (lockMarkerFollower != null)
            lockMarkerFollower.SetAssassinationReady(can);

        if (!pressed)
            return;

        if (!can)
            return;

        StartTakedown(type, cfg, targetStats);
    }

    bool EvaluateCanTakedown(out TakedownType type, out AssassinationTarget cfg, out CombatStats targetStats)
    {
        type = TakedownType.None;
        cfg = null;
        targetStats = null;

        if (lockOn == null || !lockOn.IsLocked || lockOn.CurrentTargetStats == null)
            return false;

        targetStats = lockOn.CurrentTargetStats;

        // 动作门禁：暗杀/处决都要求“非受击/非动作锁”
        if (playerController != null && (playerController.IsInHitLock || playerController.IsInActionLock))
            return false;

        if (targetStats.IsDead || targetStats.CurrentHP <= 0)
            return false;

        cfg = targetStats.GetComponentInParent<AssassinationTarget>();
        if (cfg == null)
            return false;

        // 暗杀优先（蹲 + 敌人非战斗）
        if (CanAssassinateInternal(cfg, targetStats))
        {
            Transform a = cfg.GetAnchorOrSelf(forExecute: false);
            if (Vector3.Distance(transform.position, a.position) <= cfg.maxDistance)
            {
                type = TakedownType.Assassinate;
                return true;
            }
        }

        // 处决（非蹲 + 持剑 + 敌人战斗中 + 破防恢复期 + 非防御）
        if (CanExecuteInternal(cfg, targetStats))
        {
            Transform a = cfg.GetAnchorOrSelf(forExecute: true);
            if (Vector3.Distance(transform.position, a.position) <= cfg.maxDistance)
            {
                type = TakedownType.Execute;
                return true;
            }
        }

        return false;
    }

    bool CanAssassinateInternal(AssassinationTarget cfg, CombatStats targetStats)
    {
        if (!cfg.canBeAssassinated) return false;
        if (anim == null || !anim.GetBool(crouchBoolName)) return false;

        var enemyState = targetStats.GetComponentInParent<EnemyState>();
        if (enemyState != null && enemyState.IsInCombat) return false;

        if (useBackAngleGate)
        {
            Vector3 enemyForward = targetStats.transform.forward; enemyForward.y = 0f;
            Vector3 toPlayer = transform.position - targetStats.transform.position; toPlayer.y = 0f;

            if (enemyForward.sqrMagnitude > 0.0001f && toPlayer.sqrMagnitude > 0.0001f)
            {
                float angle = Vector3.Angle(enemyForward.normalized, toPlayer.normalized);
                if (angle < (180f - backAngle)) return false;
            }
        }

        return true;
    }

    bool CanExecuteInternal(AssassinationTarget cfg, CombatStats targetStats)
    {
        if (!cfg.canBeExecuted) return false;
        if (lockOn == null || !lockOn.IsLocked) return false;

        if (sword == null || !sword.IsArmed) return false;

        // 必须非蹲
        if (anim != null && anim.GetBool(crouchBoolName)) return false;

        // 防御中不能处决（IsInActionLock 不含 block，所以这里单独挡）
        if (block != null && block.IsBlocking) return false;

        var enemyState = targetStats.GetComponentInParent<EnemyState>();
        if (enemyState == null || !enemyState.IsInCombat) return false;

        if (!targetStats.IsGuardBroken) return false;

        return true;
    }

    void StartTakedown(TakedownType type, AssassinationTarget cfg, CombatStats targetStats)
    {
        if (isAssassinating) return;

        currentType = type;
        currentTargetCfg = cfg;
        currentTargetStats = targetStats;
        currentEnemyController = (targetStats != null) ? targetStats.GetComponentInParent<EnemyController>() : null;
        currentEnemyAnim = (currentEnemyController != null) ? currentEnemyController.GetComponent<Animator>() : null;

        isAssassinating = true;

        if (playerReceiver != null) playerReceiver.ForceSetInvincible(true);
        if (block != null) block.RequestBlock(false);
        if (abilitySystem != null) abilitySystem.CancelPending();

        // 冻结位移：先冻结再贴合
        cachedApplyRootMotion = (anim != null) && anim.applyRootMotion;
        if (anim != null) anim.applyRootMotion = false;

        cachedCcEnabled = (cc != null) && cc.enabled;
        if (cc != null) cc.enabled = false;

        if (move != null)
        {
            cachedMoveEnabled = move.enabled;
            move.enabled = false;
        }

        if (currentEnemyController != null)
            currentEnemyController.EnterAssassinationLock();

        if (snapOnStart && currentTargetCfg != null)
        {
            Transform anchor = currentTargetCfg.GetAnchorOrSelf(forExecute: currentType == TakedownType.Execute);
            TeleportToAnchor_NoCC(anchor);
        }

        TriggerAnimByType();
        StartCoroutine(ForceEnterStateIfNeeded());

        if (lockMarkerFollower != null)
            lockMarkerFollower.SetAssassinationReady(true);
    }

    void TeleportToAnchor_NoCC(Transform anchor)
    {
        if (anchor == null) return;

        transform.position = anchor.position + snapWorldOffset;

        if (snapRotationFull) transform.rotation = anchor.rotation;
        else
        {
            Vector3 e = transform.eulerAngles;
            Vector3 a = anchor.eulerAngles;
            transform.rotation = Quaternion.Euler(e.x, a.y, e.z);
        }

        Physics.SyncTransforms();
    }

    void TriggerAnimByType()
    {
        if (anim != null)
        {
            if (!string.IsNullOrEmpty(playerAssassinateTrigger)) anim.ResetTrigger(playerAssassinateTrigger);
            if (!string.IsNullOrEmpty(playerExecuteTrigger)) anim.ResetTrigger(playerExecuteTrigger);
        }

        if (currentType == TakedownType.Assassinate)
        {
            if (anim != null && !string.IsNullOrEmpty(playerAssassinateTrigger))
                anim.SetTrigger(playerAssassinateTrigger);

            if (currentEnemyAnim != null && !string.IsNullOrEmpty(enemyAssassinatedTrigger))
            {
                currentEnemyAnim.ResetTrigger(enemyAssassinatedTrigger);
                currentEnemyAnim.SetTrigger(enemyAssassinatedTrigger);
            }
        }
        else if (currentType == TakedownType.Execute)
        {
            if (anim != null && !string.IsNullOrEmpty(playerExecuteTrigger))
                anim.SetTrigger(playerExecuteTrigger);

            if (currentEnemyAnim != null && !string.IsNullOrEmpty(enemyExecutedTrigger))
            {
                currentEnemyAnim.ResetTrigger(enemyExecutedTrigger);
                currentEnemyAnim.SetTrigger(enemyExecutedTrigger);
            }
        }
    }

    IEnumerator ForceEnterStateIfNeeded()
    {
        float t = Mathf.Max(0f, forceStateDelay);
        if (t > 0f) yield return new WaitForSecondsRealtime(t);
        else yield return null;

        if (anim != null)
        {
            string want = (currentType == TakedownType.Execute) ? playerExecuteStateName : playerAssassinateStateName;
            if (!string.IsNullOrEmpty(want))
            {
                int layer = 0;
                int hash = Animator.StringToHash(want);
                var info = anim.GetCurrentAnimatorStateInfo(layer);

                bool alreadyIn = (info.shortNameHash == hash && info.normalizedTime < 0.98f);
                if (!alreadyIn && anim.HasState(layer, hash))
                    anim.CrossFadeInFixedTime(want, 0.02f, layer, 0f);
            }
        }

        if (currentEnemyAnim != null)
        {
            string want = (currentType == TakedownType.Execute) ? enemyExecutedStateName : enemyAssassinatedStateName;
            if (!string.IsNullOrEmpty(want))
            {
                int layer = 0;
                int hash = Animator.StringToHash(want);
                var info = currentEnemyAnim.GetCurrentAnimatorStateInfo(layer);

                bool alreadyIn = (info.shortNameHash == hash && info.normalizedTime < 0.98f);
                if (!alreadyIn && currentEnemyAnim.HasState(layer, hash))
                    currentEnemyAnim.CrossFadeInFixedTime(want, 0.02f, layer, 0f);
            }
        }
    }

    // ================= Animation Events =================
    public void AssassinateImpact()
    {
        if (!isAssassinating) return;
        if (currentType != TakedownType.Assassinate) return;
        DoTakedownImpact(assassinationSpecialGain);
    }

    public void ExecuteImpact()
    {
        if (!isAssassinating) return;
        if (currentType != TakedownType.Execute) return;
        DoTakedownImpact(executeSpecialGain);
    }

    void DoTakedownImpact(int specialGain)
    {
        if (currentTargetStats == null) return;

        if (currentEnemyController != null)
            currentEnemyController.MarkDeathByAssassination();

        int hp = currentTargetStats.CurrentHP;
        if (hp > 0)
            currentTargetStats.TakeHPDamage(hp);

        if (playerStats != null && specialGain > 0)
            playerStats.AddSpecial(specialGain);
    }

    public void AssassinateEnd()
    {
        if (!isAssassinating) return;
        if (currentType != TakedownType.Assassinate) return;
        EndTakedown(forceCrouchTrue: true);
    }

    public void ExecuteEnd()
    {
        if (!isAssassinating) return;
        if (currentType != TakedownType.Execute) return;
        EndTakedown(forceCrouchTrue: false);
    }

    void EndTakedown(bool forceCrouchTrue)
    {
        isAssassinating = false;

        if (playerReceiver != null)
            playerReceiver.ForceSetInvincible(false);

        if (currentTargetStats != null && !currentTargetStats.IsDead)
        {
            if (currentEnemyController != null)
                currentEnemyController.ExitAssassinationLock();
        }

        currentType = TakedownType.None;
        currentTargetCfg = null;
        currentEnemyController = null;
        currentEnemyAnim = null;
        currentTargetStats = null;

        if (lockMarkerFollower != null)
            lockMarkerFollower.SetAssassinationReady(false);

        if (forceCrouchTrue && anim != null)
            anim.SetBool(crouchBoolName, true);

        if (cc != null) cc.enabled = cachedCcEnabled;
        if (anim != null) anim.applyRootMotion = cachedApplyRootMotion;
        if (move != null) move.enabled = cachedMoveEnabled;
    }
}
