using UnityEngine;
using System.Collections;

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
    [SerializeField] SavePoint initialSavePoint;
    [SerializeField] Transform playerRoot;
    [SerializeField] Animator playerAnimator;
    [SerializeField] PlayerController playerController;
    [SerializeField] CombatReceiver playerReceiver;
    [SerializeField] CombatStats playerStats;
    [SerializeField] UpgradeUIManager upgradeUIManager;
    [SerializeField] CharacterController playerCharacterController;

    [Header("Animator Triggers")]
    [SerializeField] string saveTrigger = "Checkpoint_Save";
    [SerializeField] string exitSaveTrigger = "Checkpoint_Exit";

    [Header("Fader")]
    [SerializeField, Range(0f, 2f)] float fadeOut = 0.35f;
    [SerializeField, Range(0f, 2f)] float fadeIn = 0.35f;
    [SerializeField, Range(0f, 1f)] float blackHold = 0.05f;

    [Header("Death Respawn (Step2)")]
    [SerializeField, Range(0f, 3f)] float deathRespawnDelay = 0.6f;

    SaveFlowState state = SaveFlowState.Idle;
    SavePoint currentSavePoint;
    SavePoint lastSavePoint;
    bool deathRespawnPending;

    public SavePoint LastSavePoint => lastSavePoint;
    public SavePoint InitialSavePoint => initialSavePoint;

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
        BindPlayerDeathEvent();

        if (upgradeUIManager != null)
        {
            upgradeUIManager.ExitRequested -= OnUIExitRequested;
            upgradeUIManager.ExitRequested += OnUIExitRequested;

            upgradeUIManager.UnlockDroneBurstRequested -= OnUnlockDroneBurstRequested;
            upgradeUIManager.UnlockDroneBurstRequested += OnUnlockDroneBurstRequested;
        }
    }

    void OnDisable()
    {
        UnbindPlayerDeathEvent();

        if (upgradeUIManager != null)
        {
            upgradeUIManager.ExitRequested -= OnUIExitRequested;
            upgradeUIManager.UnlockDroneBurstRequested -= OnUnlockDroneBurstRequested;
        }
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

    public SavePoint GetRespawnSavePoint()
    {
        if (lastSavePoint != null)
            return lastSavePoint;

        return initialSavePoint;
    }

    public Transform GetRespawnAnchor()
    {
        SavePoint respawnSavePoint = GetRespawnSavePoint();
        if (respawnSavePoint == null)
            return null;

        return respawnSavePoint.RespawnAnchor;
    }

    public bool ShouldPlayCheckpointExitOnRespawn()
    {
        // single-slot save rule:
        // only after touching a save point should death-respawn use checkpoint exit pose.
        return lastSavePoint != null;
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

        if (playerReceiver != null)
            playerReceiver.ForceSetInvincible(false);

        if (playerController != null)
            playerController.SetCheckpointFlowLock(false);

        if (playerStats != null && !playerStats.IsDead)
            playerStats.HealHP(playerStats.maxHP);

        // TODO: Respawn enemies to their HomePoint (NotCombat state).

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

                if (playerAnimator != null)
                {
                    playerAnimator.ResetTrigger(saveTrigger);
                    playerAnimator.SetTrigger(exitSaveTrigger);
                }

                state = SaveFlowState.ExitingAnim;
            },
            outDuration: fadeOut,
            inDuration: fadeIn,
            blackHoldSeconds: blackHold
        );
    }

    void OnUnlockDroneBurstRequested()
    {
        Debug.Log("[SavePointManager] UnlockDroneBurst requested (placeholder).", this);
    }

    void OnPlayerDead()
    {
        if (deathRespawnPending)
            return;

        if (ScreenFader.Instance == null)
        {
            Debug.LogError("[SavePointManager] ScreenFader.Instance not found for death respawn.");
            return;
        }

        deathRespawnPending = true;
        StartCoroutine(CoDeathRespawnEntry());
    }

    IEnumerator CoDeathRespawnEntry()
    {
        if (deathRespawnDelay > 0f)
            yield return new WaitForSecondsRealtime(deathRespawnDelay);

        ScreenFader.Instance.FadeOutIn(
            midAction: () =>
            {
                Transform respawnAnchor = GetRespawnAnchor();
                if (respawnAnchor != null)
                    AlignPlayerToAnchor(respawnAnchor);
                else
                    Debug.LogWarning("[SavePointManager] Respawn anchor is missing (both lastSavePoint and initialSavePoint are null).");

                RefreshEnemiesDuringBlackScreen();

                Debug.Log($"[SavePointManager] Death respawn entry done. playCheckpointExit={ShouldPlayCheckpointExitOnRespawn()}");
            },
            outDuration: fadeOut,
            inDuration: fadeIn,
            blackHoldSeconds: blackHold,
            onComplete: () => deathRespawnPending = false
        );
    }

    void RefreshEnemiesDuringBlackScreen()
    {
        // TODO(step6): Respawn enemies to their HomePoint (NotCombat state).
    }

    void AlignPlayerToAnchor(Transform anchor)
    {
        if (anchor == null || playerRoot == null) return;

        bool hadCC = playerCharacterController != null;
        if (hadCC) playerCharacterController.enabled = false;

        playerRoot.SetPositionAndRotation(anchor.position, anchor.rotation);

        if (hadCC) playerCharacterController.enabled = true;
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

    void BindPlayerDeathEvent()
    {
        if (playerStats == null)
            return;

        playerStats.OnDead -= OnPlayerDead;
        playerStats.OnDead += OnPlayerDead;
    }

    void UnbindPlayerDeathEvent()
    {
        if (playerStats == null)
            return;

        playerStats.OnDead -= OnPlayerDead;
    }
}
