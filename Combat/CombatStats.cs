using UnityEngine;

public class CombatStats : MonoBehaviour
{
    [Header("HP")]
    public int maxHP = 100;
    public int CurrentHP { get; private set; }

    [Header("Stamina")]
    public int maxStamina = 100;
    public int CurrentStamina { get; private set; }

    [Header("Stamina Recovery")]
    [Tooltip("每秒恢复多少体力")]
    public float staminaRecoverRate = 15f;

    [Tooltip("松开防御/消耗体力后，延迟多久才开始恢复")]
    public float staminaRecoverDelayAfterBlock = 5f;

    [Tooltip("破防（体力=0）后，保持0的时间")]
    public float guardBreakRecoverDelay = 1f;

    [Range(0f, 1f)]
    [Tooltip("破防后体力恢复到该比例之前，不能再次防御（可调）")]
    public float guardBreakBlockThreshold = 1f;

    [Header("Special")]
    public int maxSpecial = 0;
    public int CurrentSpecial { get; private set; }

    public bool IsDead { get; private set; }
    public event System.Action OnDead;

    bool isBlocking;
    bool isGuardBroken;
    float recoverDelayTimer;

    // ✅ 用 float 做体力累加，避免 RoundToInt 抖动
    float staminaF;

    public bool IsGuardBroken => isGuardBroken;

    public bool CanBlock
    {
        get
        {
            // ✅ 关键：体力为0时不允许格挡（不然会卡恢复）
            if (CurrentStamina <= 0) return false;

            // 未破防：允许格挡（即使体力很低）
            if (!isGuardBroken) return true;

            // 破防后：阈值前禁止
            return CurrentStamina >= Mathf.RoundToInt(maxStamina * guardBreakBlockThreshold);
        }
    }



    void Awake()
    {
        CurrentHP = maxHP;
        CurrentStamina = maxStamina;
        staminaF = CurrentStamina;

        CurrentSpecial = 0;
    }

    void Update()
    {
        UpdateStaminaRecovery();
    }

    /* ================= HP ================= */

    public void TakeHPDamage(int value)
    {
        if (value <= 0 || IsDead) return;

        CurrentHP -= value;
        if (CurrentHP <= 0)
        {
            CurrentHP = 0;
            Die();
        }
    }

    public void HealHP(int value)
    {
        if (value <= 0 || IsDead) return;

        CurrentHP += value;
        if (CurrentHP > maxHP)
            CurrentHP = maxHP;
    }

    /* ================= Stamina ================= */

    public void TakeStaminaDamage(int value)
    {
        if (value <= 0) return;

        // ✅ 破防恢复期间（isGuardBroken=true）不再承受体力伤害，避免反复被打回 0 体力。
        if (isGuardBroken) return;

        staminaF -= value;
        if (staminaF <= 0f)
        {
            staminaF = 0f;
            SyncStaminaInt();

            EnterGuardBreak(); // 破防逻辑
            return;
        }

        SyncStaminaInt();
        ResetRecoverDelay(staminaRecoverDelayAfterBlock);
    }

    // ✅ 破防：强制把体力清零，并进入破防状态（用于“破防判定但未扣够体力”的场景）
    public void ForceGuardBreak()
    {
        staminaF = 0f;
        SyncStaminaInt();
        EnterGuardBreak();
    }

    public void ConsumeStamina(int value)
    {
        if (value <= 0) return;

        staminaF -= value;
        if (staminaF < 0f) staminaF = 0f;

        SyncStaminaInt();
        ResetRecoverDelay(staminaRecoverDelayAfterBlock);
    }

    void UpdateStaminaRecovery()
    {
        // ✅ 防御时不恢复（但破防时即使状态没同步，也允许恢复）
        if (isBlocking && !isGuardBroken)
            return;

        if (recoverDelayTimer > 0f)
        {
            recoverDelayTimer -= Time.deltaTime;
            return;
        }

        if (CurrentStamina >= maxStamina)
            return;

        staminaF += staminaRecoverRate * Time.deltaTime;
        if (staminaF > maxStamina) staminaF = maxStamina;

        SyncStaminaInt();

        // ✅ 恢复到阈值：解除破防
        if (isGuardBroken &&
            CurrentStamina >= Mathf.RoundToInt(maxStamina * guardBreakBlockThreshold))
        {
            isGuardBroken = false;
        }
    }

    void SyncStaminaInt()
    {
        // 用 Floor 更稳定（只增不减）
        int v = Mathf.FloorToInt(staminaF + 0.0001f);
        v = Mathf.Clamp(v, 0, maxStamina);
        CurrentStamina = v;
    }

    void ResetRecoverDelay(float delay)
    {
        // ✅ 取更大值，避免“短延迟覆盖破防长延迟”导致不稳定
        recoverDelayTimer = Mathf.Max(recoverDelayTimer, delay);
    }

    public void EnterGuardBreak()
    {
        isGuardBroken = true;

        // ✅ 体力为0后的“停顿时间”
        recoverDelayTimer = Mathf.Max(recoverDelayTimer, guardBreakRecoverDelay);
    }

    /* ================= Block ================= */

    public void SetBlocking(bool blocking)
    {
        if (isBlocking == blocking)
            return;

        isBlocking = blocking;

        // 松开防御后给延迟再恢复
        if (!blocking)
            ResetRecoverDelay(staminaRecoverDelayAfterBlock);
    }

    /* ================= Special ================= */

    public void AddSpecial(int value)
    {
        if (value <= 0) return;

        CurrentSpecial += value;
        if (CurrentSpecial > maxSpecial)
            CurrentSpecial = maxSpecial;
    }

    public bool ConsumeSpecial(int value)
    {
        if (CurrentSpecial < value)
            return false;

        CurrentSpecial -= value;
        return true;
    }




    public void RestoreStaminaToFull()
    {
        staminaF = maxStamina;
        CurrentStamina = maxStamina;
        isGuardBroken = false;
        recoverDelayTimer = 0f;
        isBlocking = false;
    }

    public void ReviveFullHPAndStamina()
    {
        ReviveFullHP();
        RestoreStaminaToFull();
    }

    public void ReviveFullHP()
    {
        IsDead = false;
        CurrentHP = maxHP;
    }

    void Die()
    {
        if (IsDead) return;
        IsDead = true;
        OnDead?.Invoke();
    }
}
