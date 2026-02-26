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

        if (cc.isGrounded)
        {
            FinishFloatingOnLand();
            return;
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
    }

    void EnableEnemyBehaviours()
    {
        if (enemyController != null)
            enemyController.SetFloatControlLock(false);

        if (enemyMove != null)
            enemyMove.enabled = cachedEnemyMoveEnabled;

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

        int stateHash = Animator.StringToHash(floatStateName);
        if (!anim.HasState(0, stateHash))
            return;

        AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(0);
        if (!st.IsName(floatStateName) && !st.IsName($"Base Layer.{floatStateName}"))
            anim.CrossFadeInFixedTime(floatStateName, floatCrossFade, 0, 0f);
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
