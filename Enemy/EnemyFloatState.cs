using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemyFloatState : MonoBehaviour
{
    enum FloatPhase
    {
        None,
        Rising,
        Floating,
        Falling,
    }

    [Header("Animator")]
    [SerializeField] string floatStateName = "Float";
    [SerializeField] float floatCrossFade = 0.05f;
    [SerializeField] string floatBoolParam = "IsFloating";

    [Header("Fall")]
    [SerializeField] bool debugLog;

    CharacterController cc;
    Animator anim;
    EnemyController enemyController;
    EnemyMove enemyMove;
    EnemyNavigator enemyNavigator;
    Combat combat;
    RangeCombat rangeCombat;
    MeleeFighter melee;
    RangeFighter range;
    BlockController block;
    CombatStats stats;

    FloatPhase phase = FloatPhase.None;
    float floatEndTime;
    float targetRiseY;
    float riseSpeed;
    float fallVelocityY;
    Transform caster;

    bool pendingFallDead;
    bool pendingGuardBreakAfterLand;
    bool cachedEnemyMoveEnabled;
    bool cachedRootMotion;
    bool cachedRootMotionValid;
    float cachedAnimSpeed = 1f;
    bool cachedAnimSpeedValid;
    bool animHasFloatBool;
    float groundedConfirmTimer;

    [Header("Landing Stability")]
    [SerializeField, Tooltip("Falling 阶段检测到 grounded 后，持续该时间才视为真正落地，避免边缘抖动反复落地/起跳。")]
    float groundedConfirmDuration = 0.06f;

    public bool IsFloating => phase != FloatPhase.None;
    public bool IsInFloatOrFalling => phase == FloatPhase.Rising || phase == FloatPhase.Floating || phase == FloatPhase.Falling;
    public bool SuppressHitReaction => IsInFloatOrFalling;
    public bool SuppressGuardBreakReaction => IsInFloatOrFalling;
    public bool SuppressDeadAnimation => IsInFloatOrFalling;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        anim = GetComponent<Animator>();
        enemyController = GetComponent<EnemyController>();
        enemyMove = GetComponent<EnemyMove>();
        enemyNavigator = GetComponent<EnemyNavigator>();
        combat = GetComponent<Combat>();
        rangeCombat = GetComponent<RangeCombat>();
        melee = GetComponent<MeleeFighter>();
        range = GetComponent<RangeFighter>();
        block = GetComponent<BlockController>();
        stats = GetComponent<CombatStats>();

        if (anim != null && !string.IsNullOrWhiteSpace(floatBoolParam))
            animHasFloatBool = HasAnimBool(anim, floatBoolParam);
    }

    void Update()
    {
        if (!IsFloating)
            return;

        switch (phase)
        {
            case FloatPhase.Rising:
                TickRising();
                break;
            case FloatPhase.Floating:
                TickFloating();
                break;
            case FloatPhase.Falling:
                TickFalling();
                break;
        }
    }

    public bool TryStartFloat(float riseHeight, float riseSpeedValue, float duration, float initialFallVelocity, Transform casterTransform)
    {
        if (IsFloating)
            return false;

        if (stats == null || stats.IsDead)
            return false;

        caster = casterTransform;
        pendingFallDead = false;
        pendingGuardBreakAfterLand = false;

        ForceEnterFloatAnim();
        DisableEnemyBehaviours();

        Vector3 pos = transform.position;
        targetRiseY = pos.y + Mathf.Max(0f, riseHeight);
        riseSpeed = Mathf.Max(0.01f, riseSpeedValue);
        floatEndTime = Time.time + Mathf.Max(0f, duration);

        float configuredFall = Mathf.Min(-0.01f, initialFallVelocity);
        if (enemyMove != null)
            configuredFall = Mathf.Max(configuredFall, enemyMove.hardLandVelocity);

        fallVelocityY = configuredFall;

        phase = FloatPhase.Rising;
        groundedConfirmTimer = 0f;

        SetFloatAnimatorFlag(true);

        if (debugLog)
            Debug.Log($"[EnemyFloatState] Start float {name} riseH={riseHeight} duration={duration} fallV={fallVelocityY}", this);

        return true;
    }

    public void NotifyHitResult(HitResultType resultType)
    {
        if (!IsInFloatOrFalling)
            return;

        if (resultType == HitResultType.GuardBreak)
            pendingGuardBreakAfterLand = true;
    }

    public void NotifyHpAfterHit()
    {
        if (!IsInFloatOrFalling)
            return;

        if (stats == null)
            return;

        if (stats.CurrentHP <= 0)
            BeginFalling(immediateFallDead: true);
    }

    void TickRising()
    {
        Vector3 pos = transform.position;
        float nextY = Mathf.MoveTowards(pos.y, targetRiseY, riseSpeed * Time.deltaTime);
        Vector3 delta = new Vector3(0f, nextY - pos.y, 0f);
        cc.Move(delta);

        if (Mathf.Abs(targetRiseY - nextY) <= 0.01f)
            phase = FloatPhase.Floating;

        KeepFloatAnimLoop();

        if (stats != null && stats.CurrentHP <= 0)
            BeginFalling(immediateFallDead: true);
    }

    void TickFloating()
    {
        KeepFloatAnimLoop();

        if (stats != null && stats.CurrentHP <= 0)
        {
            BeginFalling(immediateFallDead: true);
            return;
        }

        if (Time.time >= floatEndTime)
            BeginFalling(immediateFallDead: false);
    }

    void TickFalling()
    {
        KeepFloatAnimLoop();

        if (cc.isGrounded && fallVelocityY <= 0f)
        {
            groundedConfirmTimer += Time.deltaTime;
            if (groundedConfirmTimer >= Mathf.Max(0f, groundedConfirmDuration))
            {
                FinishFloatingOnLand();
                return;
            }
        }
        else
        {
            groundedConfirmTimer = 0f;
        }

        float gravity = (enemyMove != null) ? enemyMove.gravity : -15f;
        float terminal = (enemyMove != null) ? enemyMove.terminalVelocity : -50f;

        fallVelocityY += gravity * Time.deltaTime;
        fallVelocityY = Mathf.Max(fallVelocityY, terminal);

        cc.Move(Vector3.up * (fallVelocityY * Time.deltaTime));
    }

    void BeginFalling(bool immediateFallDead)
    {
        if (phase == FloatPhase.Falling)
            return;

        phase = FloatPhase.Falling;
        groundedConfirmTimer = 0f;

        if (enemyMove != null)
            enemyMove.SetVerticalVelocity(fallVelocityY);

        if (immediateFallDead)
            pendingFallDead = true;

        KeepFloatAnimLoop();
    }

    void FinishFloatingOnLand()
    {
        if (enemyController != null)
            enemyController.LandBegin();

        float impactDownwardSpeed = Mathf.Abs(fallVelocityY);

        if (stats != null && !stats.IsDead)
        {
            int fallDamage = CalculateFallDamage(impactDownwardSpeed);
            if (fallDamage > 0)
                stats.TakeHPDamage(fallDamage, DeathCause.Fall);
        }

        bool deadOnLand = pendingFallDead || (stats != null && stats.CurrentHP <= 0);

        if (deadOnLand)
        {
            if (stats != null && !stats.IsDead)
                stats.TakeHPDamage(999999, DeathCause.Fall);

            if (enemyController != null)
                enemyController.ForceNextDeathToFallDeadAnimation();
        }
        else if (anim != null)
        {
            anim.SetTrigger("HardLand");
        }

        EnableEnemyBehaviours();

        if (!deadOnLand && pendingGuardBreakAfterLand && anim != null)
            anim.SetTrigger("HeavyHit");

        if (enemyController != null && !deadOnLand)
            enemyController.LandEnd();

        phase = FloatPhase.None;
        pendingGuardBreakAfterLand = false;
        groundedConfirmTimer = 0f;

        SetFloatAnimatorFlag(false);

        if (deadOnLand && enemyController != null)
            enemyController.OnCharacterDead();
    }

    void DisableEnemyBehaviours()
    {
        if (enemyNavigator != null)
            enemyNavigator.Stop();

        if (block != null)
            block.RequestBlock(false);

        if (melee != null)
            melee.InterruptAttack();

        if (range != null)
            range.InterruptShoot();

        if (combat != null)
            combat.enabled = false;

        if (rangeCombat != null)
            rangeCombat.enabled = false;

        if (enemyController != null)
            enemyController.SetFloatControlLock(true);

        if (enemyMove != null)
        {
            cachedEnemyMoveEnabled = enemyMove.enabled;
            enemyMove.enabled = false;
            enemyMove.SetMoveDirection(Vector3.zero);
            enemyMove.SetMoveSpeedLevel(0);
        }

        if (anim != null)
        {
            if (!cachedRootMotionValid)
            {
                cachedRootMotion = anim.applyRootMotion;
                cachedRootMotionValid = true;
            }

            if (!cachedAnimSpeedValid)
            {
                cachedAnimSpeed = anim.speed;
                cachedAnimSpeedValid = true;
            }

            anim.applyRootMotion = false;
            // 浮空期间固定动画速率，避免受本地时间缩放影响导致看起来“卡在首帧”。
            anim.speed = 1f;
        }
    }

    void EnableEnemyBehaviours()
    {
        if (enemyController != null)
            enemyController.SetFloatControlLock(false);

        if (enemyMove != null)
            enemyMove.enabled = cachedEnemyMoveEnabled;

        if (anim != null && cachedRootMotionValid)
            anim.applyRootMotion = cachedRootMotion;

        if (anim != null && cachedAnimSpeedValid)
            anim.speed = cachedAnimSpeed;

        if (combat != null && !IsDeadNow())
            combat.enabled = true;

        if (rangeCombat != null && !IsDeadNow())
            rangeCombat.enabled = true;
    }

    bool IsDeadNow()
    {
        return stats != null && stats.IsDead;
    }

    void ForceEnterFloatAnim()
    {
        if (anim == null)
            return;

        anim.CrossFadeInFixedTime(floatStateName, floatCrossFade, 0, 0f);
    }

    void KeepFloatAnimLoop()
    {
        if (anim == null)
            return;

        // 仅维持播放速率，不在每帧强制 CrossFade，避免一直回到 Float 首帧导致“静止”。
        if (!Mathf.Approximately(anim.speed, 1f))
            anim.speed = 1f;
    }

    void SetFloatAnimatorFlag(bool floating)
    {
        if (anim == null || !animHasFloatBool)
            return;

        anim.SetBool(floatBoolParam, floating);
    }

    static bool HasAnimBool(Animator animator, string paramName)
    {
        foreach (var p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool && p.name == paramName)
                return true;
        }

        return false;
    }

    int CalculateFallDamage(float downwardSpeed)
    {
        if (enemyMove == null || !enemyMove.enableFallDamage)
            return 0;

        float speed = Mathf.Max(0f, downwardSpeed);
        if (speed <= enemyMove.fallDamageThresholdSpeed)
            return 0;

        float excess = speed - enemyMove.fallDamageThresholdSpeed;
        int damage = Mathf.CeilToInt(excess * Mathf.Max(0f, enemyMove.fallDamageScale));

        if (enemyMove.fallDamageMax > 0)
            damage = Mathf.Min(damage, enemyMove.fallDamageMax);

        return Mathf.Max(0, damage);
    }
}
