using System.Collections;
using UnityEngine;

/// <summary>
/// Coordinates the TAB and CTRL control tips after the bin intro card closes
/// (when the Quest panel is back), before practice drag-and-drop.
/// Completes each step when the player performs the action, or skips instantly if already done.
/// </summary>
public class GameplayControlTipsManager : MonoBehaviour
{
    public static GameplayControlTipsManager Instance { get; private set; }

    [Header("Data & UI References")]
    [SerializeField] private ControlTipsData tipsData;
    [SerializeField] private ControlTipCardUI tipCardUI;

    [Header("Controllers")]
    [SerializeField] private QuestPanelToggleController tabController;
    [SerializeField] private CursorQuickAccessController ctrlController;
    [SerializeField] private IntroSequenceController introController;

    private bool hasRunGameplayTips = false;
    private Coroutine tipsSequenceRoutine;
    private string activeFlowToken = null;
    private string currentOwnerToken = null;

    public bool HasCompletedAllTips => hasRunGameplayTips;
    public event System.Action OnTabTipCompleted;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void OnEnable()
    {
        NPCInteraction.OnFlowCompleted -= HandleFlowCompleted;
        NPCInteraction.OnFlowCompleted += HandleFlowCompleted;
    }

    private void OnDisable()
    {
        NPCInteraction.OnFlowCompleted -= HandleFlowCompleted;
        CleanupRunningTips();
    }

    private void Start()
    {
        ResolveReferences();
        NPCInteraction.OnFlowCompleted -= HandleFlowCompleted;
        NPCInteraction.OnFlowCompleted += HandleFlowCompleted;
    }

    private void OnDestroy()
    {
        NPCInteraction.OnFlowCompleted -= HandleFlowCompleted;
        CleanupRunningTips();
        if (Instance == this) Instance = null;
    }

    private void CleanupRunningTips()
    {
        if (tipsSequenceRoutine != null)
        {
            StopCoroutine(tipsSequenceRoutine);
            tipsSequenceRoutine = null;
        }
        if (!string.IsNullOrEmpty(currentOwnerToken))
        {
            UIOrchestrator.Instance?.ReleaseElementOwnership(UIOrchestrator.UIElement.TabTipCard, currentOwnerToken);
            UIOrchestrator.Instance?.ReleaseElementOwnership(UIOrchestrator.UIElement.TutorialCard, currentOwnerToken);
            currentOwnerToken = null;
        }
    }

    public void ResetTipsState()
    {
        CleanupRunningTips();
        hasRunGameplayTips = false;
        activeFlowToken = null;
        if (tipCardUI != null) tipCardUI.SetImmediateHidden();
    }

    public void ResolveReferences()
    {
        if (tipCardUI == null) tipCardUI = FindFirstObjectByType<ControlTipCardUI>(FindObjectsInactive.Include);
        if (tabController == null) tabController = FindFirstObjectByType<QuestPanelToggleController>(FindObjectsInactive.Include);
        if (ctrlController == null) ctrlController = FindFirstObjectByType<CursorQuickAccessController>(FindObjectsInactive.Include);
        if (introController == null) introController = FindFirstObjectByType<IntroSequenceController>(FindObjectsInactive.Include);
    }

    public void HandleFlowCompleted(string flowToken)
    {
        if (string.IsNullOrEmpty(flowToken)) return;
        // Starting it twice with the same flow token does nothing
        if (activeFlowToken == flowToken) return;

        // If the flow token changes, cancel previous routine
        if (activeFlowToken != null && activeFlowToken != flowToken)
        {
            if (tipsSequenceRoutine != null)
            {
                StopCoroutine(tipsSequenceRoutine);
                tipsSequenceRoutine = null;
            }
        }

        activeFlowToken = flowToken;
        StartGameplayTipsSequence();
    }

    /// <summary>
    /// Starts the TAB and CTRL tips sequence after bin intro.
    /// </summary>
    public void StartGameplayTipsSequence()
    {
        ResolveReferences();
        if (tipsSequenceRoutine != null) StopCoroutine(tipsSequenceRoutine);
        tipsSequenceRoutine = StartCoroutine(GameplayTipsRoutine());
    }

    private IEnumerator GameplayTipsRoutine()
    {
        // Returning player check: if already completed once, skip Step 5 (TabTutorial) and advance straight to Step 6 (FreePlay)
        if (PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0) == 1)
        {
            Debug.Log("[FlowState] Guardian -> FreePlay (reason: Returning player - EcoQuest_TabTipDone already 1, skipping TabTutorial)");
            hasRunGameplayTips = true;
            OnTabTipCompleted?.Invoke();
            yield break;
        }

        // Step 5: Enter TabTutorial profile (Quest Tab becomes visible here for the first time)
        UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.TabTutorial, $"Guardian dialogue ended (token: {activeFlowToken})");
#if UNITY_EDITOR
        Debug.Log($"[FlowTrace] Step 5 Enter: TabTutorial (token: {activeFlowToken})");
#endif

        // Ensure QuestPanel slides in cleanly at the start of Step 5
        if (tabController != null)
        {
            tabController.ResetTabCycle();
            tabController.SlideInFromLeft(0.4f);
        }

        // Pacing: Starts 0.5s after quest panel slides in
        yield return new WaitForSecondsRealtime(0.5f);

        // Ensure UIOrchestrator permits the TAB tip (only during QuestActive with QuestPanel showing, and no modal open)
        float waitStart = Time.unscaledTime;
        while (UIOrchestrator.Instance != null && UIOrchestrator.Instance.CurrentPhase != UIOrchestrator.GamePhase.QuestActive)
        {
            if (Time.unscaledTime - waitStart > 3f) break;
            yield return null;
        }
        waitStart = Time.unscaledTime;
        while (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalActuallyVisible())
        {
            if (Time.unscaledTime - waitStart > 3f) break;
            yield return null;
        }
        if (tabController != null && !tabController.gameObject.activeSelf)
        {
            tabController.SlideInFromLeft(0.4f);
        }
        waitStart = Time.unscaledTime;
        while (tabController != null && !tabController.IsPanelVisible)
        {
            if (Time.unscaledTime - waitStart > 2.5f) break;
            yield return null;
        }

        if (tipCardUI == null || tipsData == null)
        {
            hasRunGameplayTips = true;
            OnTabTipCompleted?.Invoke();
            yield break;
        }

        string ownerToken = System.Guid.NewGuid().ToString("N");
        currentOwnerToken = ownerToken;
        UIOrchestrator.Instance?.AcquireElementOwnership(UIOrchestrator.UIElement.TabTipCard, ownerToken, true);
        UIOrchestrator.Instance?.AcquireElementOwnership(UIOrchestrator.UIElement.TutorialCard, ownerToken, true);

#if UNITY_EDITOR
        Debug.Log($"[FlowTrace] TabTip: start owner={ownerToken}");
#endif

        try
        {
            // ==============================================================
            // Guided TAB Tip (2-step cycle: Hide then Show)
            // ==============================================================

            // STEP A: "Hide your quest list" / "Press TAB to hide it."
            var stepACfg = new ControlTipsData.TipConfig
            {
                stepLabel = "TIP",
                title = "Hide your quest list",
                instruction = "Press TAB to hide it.",
                doneMessage = "Hidden!",
                keyIcon = tipsData.tabTip != null ? tipsData.tabTip.keyIcon : null,
                keyLabelOverride = string.Empty,
                isWASDCluster = false
            };
            tipCardUI.ShowTip(stepACfg);

            float timer = 0f;
            bool timedOut = false;
            while (tabController != null && tabController.IsPanelVisible)
            {
                timer += Time.unscaledDeltaTime;
                // 15s idle pulse keycap (no auto-skip)
                if (timer >= 15f && timer < 16f)
                {
                    tipCardUI.SetKeyHeld((Mathf.Sin(Time.unscaledTime * 8f) > 0f));
                }
                else
                {
                    tipCardUI.SetKeyHeld(Input.GetKey(KeyCode.Tab));
                }

                if (timer >= 120f)
                {
                    Debug.LogError("[TabTip] Failsafe: TAB tip Step A timed out after 120s without response.");
                    timedOut = true;
                    break;
                }
                yield return null;
            }

            if (timedOut)
            {
                tipCardUI.DismissTip();
                tipsSequenceRoutine = null;
                hasRunGameplayTips = true;
                OnTabTipCompleted?.Invoke();
                yield break;
            }

            // Success pop for Step A
            bool popADone = false;
            tipCardUI.CompleteTip("Hidden!", () => popADone = true);
            while (!popADone) yield return null;

            // STEP B: "Show it again" / "Press TAB again to bring it back."
            var stepBCfg = new ControlTipsData.TipConfig
            {
                stepLabel = "TIP",
                title = "Show it again",
                instruction = "Press TAB again to bring it back.",
                doneMessage = "Nice!",
                keyIcon = tipsData.tabTip != null ? tipsData.tabTip.keyIcon : null,
                keyLabelOverride = string.Empty,
                isWASDCluster = false
            };
            tipCardUI.ShowTip(stepBCfg);

            timer = 0f;
            timedOut = false;
            while (tabController != null && !tabController.IsPanelVisible)
            {
                timer += Time.unscaledDeltaTime;
                // 15s idle pulse keycap (no auto-skip)
                if (timer >= 15f && timer < 16f)
                {
                    tipCardUI.SetKeyHeld((Mathf.Sin(Time.unscaledTime * 8f) > 0f));
                }
                else
                {
                    tipCardUI.SetKeyHeld(Input.GetKey(KeyCode.Tab));
                }

                if (timer >= 120f)
                {
                    Debug.LogError("[TabTip] Failsafe: TAB tip Step B timed out after 120s without response.");
                    timedOut = true;
                    break;
                }
                yield return null;
            }

            if (timedOut)
            {
                tipCardUI.DismissTip();
                tipsSequenceRoutine = null;
                hasRunGameplayTips = true;
                OnTabTipCompleted?.Invoke();
                yield break;
            }

            // Success pop for Step B: "Nice!"
            bool popBDone = false;
            tipCardUI.CompleteTip("Nice!", () => popBDone = true);
            while (!popBDone) yield return null;

            // Persist tabTipDone flag ONLY after BOTH presses succeeded
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);
            PlayerPrefs.Save();

            hasRunGameplayTips = true;
            tipsSequenceRoutine = null;
            OnTabTipCompleted?.Invoke();
        }
        finally
        {
            if (currentOwnerToken == ownerToken) currentOwnerToken = null;
            UIOrchestrator.Instance?.ReleaseElementOwnership(UIOrchestrator.UIElement.TabTipCard, ownerToken);
            UIOrchestrator.Instance?.ReleaseElementOwnership(UIOrchestrator.UIElement.TutorialCard, ownerToken);
#if UNITY_EDITOR
            Debug.Log("[FlowTrace] TabTip: completed");
            Debug.Log($"[FlowTrace] Step 6 Enter: FreePlay (token: {activeFlowToken})");
#endif
        }
    }
}
