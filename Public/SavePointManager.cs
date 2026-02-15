using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

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

    [System.Serializable]
    public class SavePointData
    {
        public Transform triggerRoot;
        public Transform interactAnchor;
        public Transform respawnAnchor;

        public Transform RespawnAnchor => respawnAnchor != null ? respawnAnchor : interactAnchor;
    }

    [Header("References")]
    [SerializeField] int initialSavePointIndex = -1;
    [SerializeField] List<SavePointData> savePoints = new List<SavePointData>();
    [SerializeField] Transform playerRoot;
    [SerializeField] Animator playerAnimator;
    [SerializeField] PlayerController playerController;
    [SerializeField] CombatReceiver playerReceiver;
    [SerializeField] CombatStats playerStats;
    [SerializeField] UpgradeUIManager upgradeUIManager;
    [SerializeField] CharacterController playerCharacterController;

    [Header("Animator Triggers")]
    [SerializeField] string enterTrigger = "Save";
    [SerializeField] string exitTrigger = "Exit";
    [SerializeField] string exitStateName = "Exit";
    [SerializeField] string idleStateName = "UnarmedLocomotion";

    [Header("Fader")]
    [SerializeField, Range(0f, 2f)] float fadeOut = 0.35f;
    [SerializeField, Range(0f, 2f)] float fadeIn = 0.35f;
    [SerializeField, Range(0f, 1f)] float blackHold = 0.05f;

    [Header("Death Respawn (Step2)")]
    [SerializeField, Range(0f, 3f)] float deathRespawnDelay = 0.6f;

    [Header("Enemy Checkpoint Refresh")]
    [SerializeField, Range(0f, 5f)] float checkpointReadyTimeout = 1.2f;
    [SerializeField, Range(0f, 3f)] float navMeshSampleRadius = 2.5f;
    [SerializeField] int navMeshAreaMask = NavMesh.AllAreas;

    SaveFlowState state = SaveFlowState.Idle;
    SavePointData currentSavePoint;
    SavePointData lastSavePoint;
    bool deathRespawnPending;

    public SavePointData LastSavePoint => lastSavePoint;
    public SavePointData InitialSavePoint => GetInitialSavePoint();

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
            // 通用：关闭
            upgradeUIManager.CloseRequested -= OnUICloseRequested;
            upgradeUIManager.CloseRequested += OnUICloseRequested;

            // 通用：6个按钮
            upgradeUIManager.Button1Requested -= OnUIButton1Requested;
            upgradeUIManager.Button2Requested -= OnUIButton2Requested;
            upgradeUIManager.Button3Requested -= OnUIButton3Requested;
            upgradeUIManager.Button4Requested -= OnUIButton4Requested;
            upgradeUIManager.Button5Requested -= OnUIButton5Requested;
            upgradeUIManager.Button6Requested -= OnUIButton6Requested;

            upgradeUIManager.Button1Requested += OnUIButton1Requested;
            upgradeUIManager.Button2Requested += OnUIButton2Requested;
            upgradeUIManager.Button3Requested += OnUIButton3Requested;
            upgradeUIManager.Button4Requested += OnUIButton4Requested;
            upgradeUIManager.Button5Requested += OnUIButton5Requested;
            upgradeUIManager.Button6Requested += OnUIButton6Requested;
        }
    }

    void OnDisable()
    {
        UnbindPlayerDeathEvent();

        if (upgradeUIManager != null)
        {
            upgradeUIManager.CloseRequested -= OnUICloseRequested;

            upgradeUIManager.Button1Requested -= OnUIButton1Requested;
            upgradeUIManager.Button2Requested -= OnUIButton2Requested;
            upgradeUIManager.Button3Requested -= OnUIButton3Requested;
            upgradeUIManager.Button4Requested -= OnUIButton4Requested;
            upgradeUIManager.Button5Requested -= OnUIButton5Requested;
            upgradeUIManager.Button6Requested -= OnUIButton6Requested;
        }
    }

    public bool BeginSaveFlow(Transform savePointRoot)
    {
        if (state != SaveFlowState.Idle) return false;
        SavePointData savePoint = GetSavePointByRoot(savePointRoot);
        if (savePoint == null || savePoint.interactAnchor == null)
        {
            Debug.LogWarning("[SavePointManager] SavePointData/interactAnchor missing.");
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

        playerAnimator.ResetTrigger(exitTrigger);
        playerAnimator.SetTrigger(enterTrigger);
        state = SaveFlowState.SavingAnim;
        return true;
    }

    public SavePointData GetRespawnSavePoint()
    {
        if (lastSavePoint != null)
            return lastSavePoint;

        return GetInitialSavePoint();
    }

    public Transform GetRespawnAnchor()
    {
        SavePointData respawnSavePoint = GetRespawnSavePoint();
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

    SavePointData GetInitialSavePoint()
    {
        if (initialSavePointIndex < 0 || initialSavePointIndex >= savePoints.Count)
            return null;
        return savePoints[initialSavePointIndex];
    }

    SavePointData GetSavePointByRoot(Transform savePointRoot)
    {
        if (savePointRoot == null) return null;
        for (int i = 0; i < savePoints.Count; i++)
        {
            SavePointData sp = savePoints[i];
            if (sp == null || sp.triggerRoot == null) continue;
            if (sp.triggerRoot == savePointRoot) return sp;
        }
        return null;
    }

    public bool TryResolveSavePointRoot(Collider other, out Transform savePointRoot)
    {
        savePointRoot = null;
        if (other == null) return false;

        Transform t = other.transform;
        for (int i = 0; i < savePoints.Count; i++)
        {
            SavePointData sp = savePoints[i];
            if (sp == null || sp.triggerRoot == null) continue;
            if (t == sp.triggerRoot || t.IsChildOf(sp.triggerRoot))
            {
                savePointRoot = sp.triggerRoot;
                return true;
            }
        }
        return false;
    }

    public void NotifySaveAnimEnd()
    {
        if (state != SaveFlowState.SavingAnim) return;
        if (ScreenFader.Instance == null)
        {
            Debug.LogError("[SavePointManager] ScreenFader.Instance not found.");
            return;
        }

        StartCoroutine(CoFadeOutInWithCheckpointRefresh(
            blackAction: () =>
            {
                if (upgradeUIManager != null)
                    upgradeUIManager.OpenImmediate();
            },
            onComplete: () => state = SaveFlowState.InUI
        ));
    }

    public void NotifyExitAnimEnd()
    {
        if (state != SaveFlowState.ExitingAnim) return;

        // NOTE: HP refill for "exit save point" should happen during the black screen (OnUICloseRequested midAction),
        // not during/after the exit animation. Here we only release locks and end invincibility.
        PrepareRespawnVisualBaseline();

        if (playerReceiver != null)
        {
            playerReceiver.ForceClearHitLock();
            playerReceiver.ForceClearIFrame();
            playerReceiver.ForceSetInvincible(false);
        }

        if (playerController != null)
        {
            // Pulse checkpoint flow lock to reuse existing clear-lock logic, then release.
            playerController.SetCheckpointFlowLock(true);
            playerController.SetCheckpointFlowLock(false);
        }

        state = SaveFlowState.Idle;
    }

    // ========= 通用：关闭请求 =========
    void OnUICloseRequested()
    {
        if (state != SaveFlowState.InUI) return;
        if (ScreenFader.Instance == null)
        {
            Debug.LogError("[SavePointManager] ScreenFader.Instance not found.");
            return;
        }

        StartCoroutine(CoFadeOutInWithCheckpointRefresh(
            blackAction: () =>
            {
                if (upgradeUIManager != null)
                    upgradeUIManager.CloseImmediate();

                // ✅ Refill player HP during the black screen (energy/special stays unchanged).
                if (playerStats != null)
                    playerStats.ReviveFullHP();

                // Keep the player protected/locked while we play the exit animation.
                PrepareRespawnVisualBaseline();

                if (playerReceiver != null)
                {
                    playerReceiver.ForceClearHitLock();
                    playerReceiver.ForceClearIFrame();
                    playerReceiver.ForceSetInvincible(true);
                }

                if (playerController != null)
                    playerController.SetCheckpointFlowLock(true);

                if (playerAnimator != null)
                {
                    playerAnimator.ResetTrigger(enterTrigger);
                    playerAnimator.SetTrigger(exitTrigger);
                }

                state = SaveFlowState.ExitingAnim;
            }
        ));
    }

    // ========= 通用：6个按钮占位接口（以后你接入具体功能） =========
    void OnUIButton1Requested() => Debug.Log("[SavePointManager] Button1 requested (placeholder).", this);
    void OnUIButton2Requested() => Debug.Log("[SavePointManager] Button2 requested (placeholder).", this);
    void OnUIButton3Requested() => Debug.Log("[SavePointManager] Button3 requested (placeholder).", this);
    void OnUIButton4Requested() => Debug.Log("[SavePointManager] Button4 requested (placeholder).", this);
    void OnUIButton5Requested() => Debug.Log("[SavePointManager] Button5 requested (placeholder).", this);
    void OnUIButton6Requested() => Debug.Log("[SavePointManager] Button6 requested (placeholder).", this);

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

        StartCoroutine(CoFadeOutInWithCheckpointRefresh(
            blackAction: ExecuteDeathRespawnDuringBlack,
            onComplete: () => deathRespawnPending = false
        ));
    }

    void ExecuteDeathRespawnDuringBlack()
    {
        Transform respawnAnchor = GetRespawnAnchor();
        if (respawnAnchor != null)
            AlignPlayerToAnchor(respawnAnchor);
        else
            Debug.LogWarning("[SavePointManager] Respawn anchor is missing (both lastSavePoint and configured initial save point are null).");

        if (ShouldPlayCheckpointExitOnRespawn())
            PrepareCheckpointExitRespawnInBlack();
        else
            PrepareIdleRespawnInBlack();
    }

    void PrepareCheckpointExitRespawnInBlack()
    {
        PrepareRespawnVisualBaseline();

        if (playerReceiver != null)
        {
            playerReceiver.ForceClearHitLock();
            playerReceiver.ForceClearIFrame();
            playerReceiver.ForceSetInvincible(true);
        }

        if (playerStats != null)
            playerStats.ReviveFullHP();

        if (playerController != null)
            playerController.SetCheckpointFlowLock(true);

        if (playerAnimator != null)
        {
            playerAnimator.Rebind();
            playerAnimator.Update(0f);

            if (HasBaseLayerState(exitStateName))
                playerAnimator.CrossFade(exitStateName, 0.03f, 0, 0f);
            else
            {
                playerAnimator.ResetTrigger(enterTrigger);
                playerAnimator.SetTrigger(exitTrigger);
            }
        }

        state = SaveFlowState.ExitingAnim;
    }

    void PrepareIdleRespawnInBlack()
    {
        ApplyRespawnRecovery();

        if (playerAnimator != null)
        {
            playerAnimator.Rebind();
            playerAnimator.Update(0f);

            if (HasBaseLayerState(idleStateName))
                playerAnimator.CrossFade(idleStateName, 0.03f, 0, 0f);
            else if (!string.IsNullOrEmpty(idleStateName))
                Debug.LogWarning($"[SavePointManager] Idle state '{idleStateName}' not found on Base Layer. Skipping CrossFade.");
        }

        state = SaveFlowState.Idle;
    }

    void PrepareRespawnVisualBaseline()
    {
        if (playerAnimator != null)
        {
            playerAnimator.ResetTrigger(enterTrigger);
            playerAnimator.ResetTrigger(exitTrigger);
            playerAnimator.SetBool("IsArmed", false);
        }

        if (playerRoot != null)
        {
            SwordController sword = playerRoot.GetComponentInChildren<SwordController>(true);
            if (sword != null)
            {
                sword.AttachToWaist();
                sword.SetArmed(false);
            }
        }
    }

    void ApplyRespawnRecovery()
    {
        PrepareRespawnVisualBaseline();

        if (playerReceiver != null)
        {
            playerReceiver.ForceClearHitLock();
            playerReceiver.ForceClearIFrame();
            playerReceiver.ForceSetInvincible(false);
        }

        if (playerController != null)
        {
            // Death sets internal action locks (e.g. isBusy) in PlayerController.
            // Pulse checkpoint flow lock to reuse existing clear-lock logic, then release.
            playerController.SetCheckpointFlowLock(true);
            playerController.SetCheckpointFlowLock(false);
        }

        if (playerStats != null)
            playerStats.ReviveFullHP();
    }

    bool HasBaseLayerState(string stateName)
    {
        if (playerAnimator == null || string.IsNullOrEmpty(stateName))
            return false;

        int hash = Animator.StringToHash(stateName);
        return playerAnimator.HasState(0, hash);
    }

    void AlignPlayerToAnchor(Transform anchor)
    {
        if (anchor == null || playerRoot == null) return;

        bool hadCC = playerCharacterController != null;
        if (hadCC) playerCharacterController.enabled = false;

        playerRoot.SetPositionAndRotation(anchor.position, anchor.rotation);

        if (hadCC) playerCharacterController.enabled = true;
    }

    IEnumerator CoFadeOutInWithCheckpointRefresh(System.Action blackAction, System.Action onComplete = null)
    {
        if (ScreenFader.Instance == null)
            yield break;

        ScreenFader.Instance.FadeOut(fadeOut);
        while (ScreenFader.Instance.IsFading)
            yield return null;

        yield return CoCheckpointEnemyRefresh(blackAction);

        if (blackHold > 0f)
            yield return new WaitForSecondsRealtime(blackHold);

        ScreenFader.Instance.FadeIn(fadeIn);
        while (ScreenFader.Instance.IsFading)
            yield return null;

        onComplete?.Invoke();
    }

    IEnumerator CoCheckpointEnemyRefresh(System.Action postRefreshAction)
    {
        EnemyController[] enemies = FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        List<EnemyController> valid = new List<EnemyController>(enemies.Length);

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyController e = enemies[i];
            if (e == null) continue;
            valid.Add(e);
            e.BeginCheckpointFreeze();
        }

        for (int i = 0; i < valid.Count; i++)
        {
            EnemyController enemy = valid[i];
            if (enemy == null) continue;

            Transform home = null;
            LostTarget lost = enemy.GetComponent<LostTarget>();
            if (lost != null)
                home = lost.homePoint;

            Vector3 pos = enemy.transform.position;
            Quaternion rot = enemy.transform.rotation;
            if (home != null)
            {
                pos = home.position;
                rot = home.rotation;
            }

            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, navMeshSampleRadius, navMeshAreaMask))
                pos = hit.position;

            enemy.ApplyCheckpointResetDuringFreeze(pos, rot);
        }

        yield return null;

        for (int i = 0; i < valid.Count; i++)
            valid[i]?.CheckpointSettleSync();

        yield return null;

        float timer = 0f;
        while (timer < checkpointReadyTimeout)
        {
            bool allReady = true;
            for (int i = 0; i < valid.Count; i++)
            {
                EnemyController enemy = valid[i];
                if (enemy == null) continue;
                enemy.CheckpointSettleSync();
                if (!enemy.CheckpointReady())
                {
                    allReady = false;
                    break;
                }
            }

            if (allReady)
                break;

            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        for (int i = 0; i < valid.Count; i++)
            valid[i]?.EndCheckpointFreeze();

        postRefreshAction?.Invoke();
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
