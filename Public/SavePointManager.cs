using System.Collections;
using UnityEngine;

public class SavePointManager : MonoBehaviour
{
    public static SavePointManager Instance { get; private set; }

    enum SaveFlowState
    {
        Idle,
        SavingAnim,
        InUI,
        ExitingAnim
    }

    [Header("References")]
    [SerializeField] Transform playerRoot;
    [SerializeField] Animator playerAnimator;
    [SerializeField] PlayerController playerController;
    [SerializeField] CombatReceiver playerReceiver;
    [SerializeField] CombatStats playerStats;
    [SerializeField] UpgradeUIManager upgradeUIManager;
    [SerializeField] CharacterController playerCharacterController;

    [Header("Death Respawn")]
    [SerializeField] Transform defaultRespawnAnchor;
    [SerializeField, Range(0.1f, 3f)] float deathDelay = 1f;
    [SerializeField, Range(0.2f, 3f)] float exitAnimFailSafeSeconds = 1.2f;

    [Header("Animator Triggers")]
    [SerializeField] string saveTrigger = "Save";
    [SerializeField] string exitSaveTrigger = "Exit";

    [Header("Fader")]
    [SerializeField, Range(0f, 2f)] float fadeOut = 0.35f;
    [SerializeField, Range(0f, 2f)] float fadeIn = 0.35f;
    [SerializeField, Range(0f, 1f)] float blackHold = 0.05f;

    SaveFlowState state = SaveFlowState.Idle;
    SavePoint currentSavePoint;
    SavePoint lastSavePoint;

    Coroutine deathRespawnCo;
    Coroutine exitAnimFailSafeCo;

    public SavePoint LastSavePoint => lastSavePoint;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        AutoBind();
    }

    void OnEnable()
    {
        AutoBind();

        if (upgradeUIManager != null)
        {
            upgradeUIManager.ExitRequested -= OnUIExitRequested;
            upgradeUIManager.ExitRequested += OnUIExitRequested;

            upgradeUIManager.UnlockDroneBurstRequested -= OnUnlockDroneBurstRequested;
            upgradeUIManager.UnlockDroneBurstRequested += OnUnlockDroneBurstRequested;
        }

        if (playerStats != null)
        {
            playerStats.OnDead -= OnPlayerDead;
            playerStats.OnDead += OnPlayerDead;
        }
    }

    void OnDisable()
    {
        if (upgradeUIManager != null)
        {
            upgradeUIManager.ExitRequested -= OnUIExitRequested;
            upgradeUIManager.UnlockDroneBurstRequested -= OnUnlockDroneBurstRequested;
        }

        if (playerStats != null)
            playerStats.OnDead -= OnPlayerDead;

        if (deathRespawnCo != null)
        {
            StopCoroutine(deathRespawnCo);
            deathRespawnCo = null;
        }

        StopExitAnimFailSafe();
    }

    public bool BeginSaveFlow(SavePoint savePoint)
    {
        if (state != SaveFlowState.Idle) return false;
        if (savePoint == null || savePoint.interactAnchor == null)
        {
            Debug.LogWarning("[SavePointManager] SavePoint/interactAnchor missing.");
            return false;
        }

        AutoBind();
        if (playerRoot == null || playerAnimator == null || playerController == null || playerReceiver == null)
        {
            Debug.LogError("[SavePointManager] Missing player references.");
            return false;
        }

        currentSavePoint = savePoint;
        lastSavePoint = savePoint; // single-slot save

        AlignPlayerToAnchor(savePoint.interactAnchor);

        playerController.SetCheckpointFlowLock(true);
        playerReceiver.ForceSetInvincible(true);

        playerAnimator.ResetTrigger(exitSaveTrigger);
        playerAnimator.SetTrigger(saveTrigger);
        state = SaveFlowState.SavingAnim;
        return true;
    }

    public void NotifySaveAnimEnd()
    {
        if (state != SaveFlowState.SavingAnim) return;
        if (ScreenFader.Instance == null)
        {
            Debug.LogError("[SavePointManager] ScreenFader.Instance not found.");
            return;
        }

        ScreenFader.Instance.FadeOutIn(
            midAction: () =>
            {
                if (upgradeUIManager != null)
                    upgradeUIManager.OpenImmediate();
            },
            outDuration: fadeOut,
            inDuration: fadeIn,
            blackHoldSeconds: blackHold,
            onComplete: () => state = SaveFlowState.InUI
        );
    }

    public void NotifyExitAnimEnd()
    {
        if (state != SaveFlowState.ExitingAnim) return;

        StopExitAnimFailSafe();
        deathRespawnCo = null;

        if (playerReceiver != null)
            playerReceiver.ForceSetInvincible(false);

        if (playerController != null)
            playerController.SetCheckpointFlowLock(false);

        if (playerStats != null)
            playerStats.RespawnFull(keepSpecial: true);

        if (playerController != null)
            playerController.ResetAfterRespawn();

        RespawnAllEnemiesToHome();

        state = SaveFlowState.Idle;
    }

    void OnUIExitRequested()
    {
        if (state != SaveFlowState.InUI) return;
        if (ScreenFader.Instance == null)
        {
            Debug.LogError("[SavePointManager] ScreenFader.Instance not found.");
            return;
        }

        ScreenFader.Instance.FadeOutIn(
            midAction: () =>
            {
                if (upgradeUIManager != null)
                    upgradeUIManager.CloseImmediate();

                BeginExitAnimationFlow();
            },
            outDuration: fadeOut,
            inDuration: fadeIn,
            blackHoldSeconds: blackHold
        );
    }

    void OnPlayerDead()
    {
        if (deathRespawnCo != null)
            StopCoroutine(deathRespawnCo);

        deathRespawnCo = StartCoroutine(CoRespawnAfterDeath());
    }

    IEnumerator CoRespawnAfterDeath()
    {
        yield return new WaitForSecondsRealtime(deathDelay);

        if (ScreenFader.Instance == null)
        {
            Debug.LogError("[SavePointManager] ScreenFader.Instance not found.");
            yield break;
        }

        ScreenFader.Instance.FadeOutIn(
            midAction: () =>
            {
                Transform anchor = lastSavePoint != null ? lastSavePoint.RespawnAnchor : defaultRespawnAnchor;
                if (anchor != null)
                    AlignPlayerToAnchor(anchor);

                if (playerController != null)
                    playerController.SetCheckpointFlowLock(true);

                if (playerReceiver != null)
                {
                    playerReceiver.ForceSetInvincible(true);
                    playerReceiver.HitEnd();
                }

                if (playerController != null)
                    playerController.ResetAfterRespawn();

                if (playerStats != null)
                    playerStats.RespawnFull(keepSpecial: true);

                BeginExitAnimationFlow();
            },
            outDuration: fadeOut,
            inDuration: fadeIn,
            blackHoldSeconds: blackHold
        );

        deathRespawnCo = null;
    }

    void BeginExitAnimationFlow()
    {
        if (playerAnimator != null)
        {
            playerAnimator.Rebind();
            playerAnimator.Update(0f);
            playerAnimator.ResetTrigger(saveTrigger);
            playerAnimator.ResetTrigger("Dead");
            playerAnimator.SetTrigger(exitSaveTrigger);
        }

        state = SaveFlowState.ExitingAnim;
        StartExitAnimFailSafe();
    }

    void StartExitAnimFailSafe()
    {
        StopExitAnimFailSafe();
        exitAnimFailSafeCo = StartCoroutine(CoExitAnimFailSafe());
    }

    void StopExitAnimFailSafe()
    {
        if (exitAnimFailSafeCo == null)
            return;

        StopCoroutine(exitAnimFailSafeCo);
        exitAnimFailSafeCo = null;
    }

    IEnumerator CoExitAnimFailSafe()
    {
        yield return new WaitForSecondsRealtime(exitAnimFailSafeSeconds);

        if (state == SaveFlowState.ExitingAnim)
        {
            Debug.LogWarning("[SavePointManager] Exit animation event timeout, forcing flow completion.", this);
            NotifyExitAnimEnd();
        }

        exitAnimFailSafeCo = null;
    }

    void OnUnlockDroneBurstRequested()
    {
        Debug.Log("[SavePointManager] UnlockDroneBurst requested (placeholder).", this);
    }

    void AlignPlayerToAnchor(Transform anchor)
    {
        if (anchor == null || playerRoot == null) return;

        bool hadCC = playerCharacterController != null;
        if (hadCC) playerCharacterController.enabled = false;

        playerRoot.SetPositionAndRotation(anchor.position, anchor.rotation);

        if (hadCC) playerCharacterController.enabled = true;
    }

    static void RespawnAllEnemiesToHome()
    {
        EnemyController[] enemies = Object.FindObjectsOfType<EnemyController>(true);
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyController enemy = enemies[i];
            if (enemy == null) continue;
            enemy.RespawnToHome();
        }
    }

    void AutoBind()
    {
        if (playerRoot == null && playerController != null)
            playerRoot = playerController.transform;

        if (playerController == null)
            playerController = FindFirstObjectByType<PlayerController>();

        if (playerRoot == null && playerController != null)
            playerRoot = playerController.transform;

        if (playerAnimator == null && playerRoot != null)
            playerAnimator = playerRoot.GetComponent<Animator>();

        if (playerReceiver == null && playerRoot != null)
            playerReceiver = playerRoot.GetComponent<CombatReceiver>();

        if (playerStats == null && playerRoot != null)
            playerStats = playerRoot.GetComponent<CombatStats>();

        if (playerCharacterController == null && playerRoot != null)
            playerCharacterController = playerRoot.GetComponent<CharacterController>();

        if (upgradeUIManager == null)
            upgradeUIManager = FindFirstObjectByType<UpgradeUIManager>();
    }
}
