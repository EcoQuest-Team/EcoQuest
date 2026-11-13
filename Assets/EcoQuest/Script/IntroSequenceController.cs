using System.Collections;
using UnityEngine;

/// <summary>
/// Coordinates the intro sequence flow: Gameplay -> Dialogue -> BinIntro -> Gameplay.
/// Hides the Quest panel and Coin counter during dialogue and bin intro,
/// manages cursor and player freeze in one place, opens the 3-bins card,
/// and begins the quest upon closing the card.
/// </summary>
public class IntroSequenceController : MonoBehaviour
{
    private static IntroSequenceController instance;
    public static IntroSequenceController Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<IntroSequenceController>(FindObjectsInactive.Include);
            }
            return instance;
        }
        private set => instance = value;
    }

    public enum IntroState
    {
        Gameplay,
        Dialogue,
        BinIntro
    }

    [Header("Current State")]
    [SerializeField] private IntroState currentState = IntroState.Gameplay;

    [Header("Core References")]
    [Tooltip("The Guardian NPC interaction component")]
    [SerializeField] private NPCInteraction guardianNPC;

    [Tooltip("The Tutorial Manager controlling the Info Cards")]
    [SerializeField] private TutorialManager tutorialManager;

    [Tooltip("The trash parent object to activate after intro completes")]
    [SerializeField] private GameObject trashContainer;

    [Header("HUD Elements (Fade targets)")]
    [SerializeField] private HudVisibility questHud;
    [SerializeField] private HudVisibility coinHud;
    [SerializeField] private float hudFadeDuration = 0.2f;

    [Header("Sequence Settings")]
    [Tooltip("If true, the intro sequence (HUD hiding & bin card) only runs on the first dialogue with Guardian")]
    [SerializeField] private bool runOnlyOnce = true;

    [Tooltip("If true, cycles through all tutorial cards (0..3). If false, shows only card 0.")]
    [SerializeField] private bool showAllTutorialCards = true;

    [Tooltip("If true, pops up quest tab when reading the last trash bin card")]
    [SerializeField] private bool popUpOnLastBinCard = true;

    [Tooltip("If true, also pops up quest tab when reading the last Guardian dialogue line")]
    [SerializeField] private bool popUpOnLastDialogueLine = false;

    [Header("Repeat Quest Settings")]
    [Tooltip("If true, repeat quests also run the Post-Test at the end. If false (default), shows a simple completion message instead.")]
    public bool repeatQuestRunsPostTest = false;

    [Header("Player Controller References")]
    [SerializeField] private StarterAssets.FirstPersonController playerController;
    [SerializeField] private StarterAssets.StarterAssetsInputs playerInputs;

    private bool hasRunIntro = false;
    private readonly System.Collections.Generic.List<TrashItem> cachedTrashItems = new System.Collections.Generic.List<TrashItem>();

    public IntroState CurrentState => currentState;
    public bool HasRunIntro => hasRunIntro;
    public bool IsTrashVisible { get; private set; } = false;
    public event System.Action OnIntroCompleted;
    public event System.Action OnTutorialCompleted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ResolveReferences();
        CacheTrashItemsOnce();

        bool alreadyDone = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0) == 1;
        SetTrashVisible(alreadyDone, false, alreadyDone
            ? "Returning player at Awake (EcoQuest_TabTipDone == 1)"
            : "Fresh start at Awake - hiding trash until Step 6 (FreePlay)");
    }

    private void OnEnable()
    {
        if (Instance == null) Instance = this;
        ResolveReferences();
        SubscribeEvents();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        UnsubscribeEvents();
    }

    private void Start()
    {
        ResolveReferences();
        CacheTrashItemsOnce();

        // Trash & Flow gating:
        // Fresh start (EcoQuest_TabTipDone == 0): trash and quest tab stay hidden for Steps 1-5 until Step 6.
        // Returning player (EcoQuest_TabTipDone == 1): skip tutorial and enable quest tab & trash right at scene start.
        bool alreadyDone = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0) == 1;
        if (alreadyDone)
        {
            hasRunIntro = true;
            NPCInteraction.IsAnyQuestActive = true;
            SetTrashVisible(true, false, "Returning player at Scene Start (EcoQuest_TabTipDone == 1)");
            UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "Returning player at Scene Start (EcoQuest_TabTipDone == 1)");
        }
        else
        {
            SetTrashVisible(false, false, "Fresh start at Scene Start - hiding trash until Step 6");
            if (questHud != null) questHud.SetVisibleImmediate(false);
        }

        if (coinHud != null) coinHud.SetVisibleImmediate(true);
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        UnsubscribeEvents();
        if (Instance == this) Instance = null;
    }

    private void CacheTrashItemsOnce()
    {
        if (cachedTrashItems.Count > 0) return;

        var found = FindObjectsByType<TrashItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var item in found)
        {
            if (item != null && !cachedTrashItems.Contains(item))
            {
                cachedTrashItems.Add(item);
            }
        }
    }

    /// <summary>
    /// Single authority controlling trash visibility across the 3D world, minimap/full-map icons,
    /// pickup/drag interaction, and quest progress counters.
    /// Never deactivates IntroSequenceController itself.
    /// </summary>
    public void SetTrashVisible(bool visible, bool animateReveal = false, string reason = "")
    {
        CacheTrashItemsOnce();

        IsTrashVisible = visible;

        if (trashContainer != null && trashContainer != gameObject)
        {
            trashContainer.SetActive(visible);
        }

        for (int i = 0; i < cachedTrashItems.Count; i++)
        {
            var item = cachedTrashItems[i];
            if (item == null) continue;

            if (!visible)
            {
                var drag = item.GetComponent<DragAndDrop>();
                if (drag != null && drag.IsGrabbed)
                {
                    drag.ForceRelease();
                }
            }

            if (item.IsDeposited)
            {
                // Deposited items are managed inside bins and stay visible in the bin
                continue;
            }

            if (item.gameObject.activeSelf != visible)
            {
                item.gameObject.SetActive(visible);
            }
        }

        TrashItem.AreTrashItemsAllowed = visible;
        MinimapIcon.SetTrashIconsActive(visible);

        if (!visible && (reason == "Reset Tutorial" || reason.Contains("Fresh start")))
        {
            QuestManager.ResetAllQuestCountsToZero();
        }
        else if (visible)
        {
            DragAndDrop.SelfHealAllUnheldItems("trash unlocked and enabled");
        }

        if (visible && animateReveal && isActiveAndEnabled)
        {
            StartCoroutine(StaggeredTrashRevealRoutine());
        }
    }

    /// <summary>
    /// Idempotently applies trash visibility state based on pure persistent requirements.
    /// </summary>
    public void ApplyTrashVisibilityState(bool visible, string reason = "")
    {
        CacheTrashItemsOnce();
        bool containerMatches = (trashContainer == null || trashContainer == gameObject || trashContainer.activeSelf == visible);
        bool stateMatches = (IsTrashVisible == visible) && (TrashItem.AreTrashItemsAllowed == visible) && containerMatches;

        if (stateMatches) return;

        SetTrashVisible(visible, false, reason);
    }

    public void SubscribeEvents()
    {
        UnsubscribeEvents();

        if (guardianNPC != null)
        {
            guardianNPC.OnDialogueStart += HandleDialogueStarted;
            guardianNPC.OnDialogueEnd += HandleDialogueEnded;
            guardianNPC.OnLastDialogueLineReached += HandleLastDialogueReached;
        }

        // NOTE: Do NOT subscribe tutorialManager.OnLastCardReached or OnTutorialClosed here!
        // Step 2 (InfoCards) -> Step 3 (Quiz) is driven strictly sequentially by NPCInteraction.GuardianFlowRoutine,
        // and the Quest Tab must remain hidden during Steps 1-4 until Step 5 (TabTutorial).

        var tipsMgr = GameplayControlTipsManager.Instance != null 
            ? GameplayControlTipsManager.Instance 
            : FindFirstObjectByType<GameplayControlTipsManager>();
        if (tipsMgr != null)
        {
            tipsMgr.OnTabTipCompleted -= HandleTabTipCompleted;
            tipsMgr.OnTabTipCompleted += HandleTabTipCompleted;
        }
    }

    public void UnsubscribeEvents()
    {
        if (guardianNPC != null)
        {
            guardianNPC.OnDialogueStart -= HandleDialogueStarted;
            guardianNPC.OnDialogueEnd -= HandleDialogueEnded;
            guardianNPC.OnLastDialogueLineReached -= HandleLastDialogueReached;
        }

        if (tutorialManager != null)
        {
            tutorialManager.OnLastCardReached -= HandleLastCardReached;
            tutorialManager.OnTutorialClosed -= HandleTutorialClosed;
        }

        var tipsMgr = GameplayControlTipsManager.Instance != null 
            ? GameplayControlTipsManager.Instance 
            : FindFirstObjectByType<GameplayControlTipsManager>();
        if (tipsMgr != null)
        {
            tipsMgr.OnTabTipCompleted -= HandleTabTipCompleted;
        }
    }

    /// <summary>
    /// Auto-resolves missing inspector references if not manually set.
    /// </summary>
    public void ResolveReferences()
    {
        if (guardianNPC == null)
            guardianNPC = FindFirstObjectByType<NPCInteraction>();

        if (tutorialManager == null)
            tutorialManager = FindFirstObjectByType<TutorialManager>();

        if (playerController == null)
            playerController = FindFirstObjectByType<StarterAssets.FirstPersonController>();

        if (playerInputs == null)
            playerInputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();

        if (trashContainer == null)
        {
            var gameplay = GameObject.Find("--- GAMEPLAY ---");
            if (gameplay != null)
            {
                var tr = gameplay.transform.Find("TrashItemsGroup");
                if (tr != null) trashContainer = tr.gameObject;
            }
            if (trashContainer == null)
            {
                var allGos = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (var go in allGos)
                {
                    if (go.name == "TrashItemsGroup" && go.scene.isLoaded)
                    {
                        trashContainer = go;
                        break;
                    }
                }
            }
        }

        if (questHud == null)
        {
            var qCanvas = GameObject.Find("UI_Quest");
            if (qCanvas == null) qCanvas = GameObject.Find("Quest System Canvas"); // Fallback
            if (qCanvas != null)
            {
                var qPanel = qCanvas.transform.Find("QuestPanel");
                if (qPanel != null)
                {
                    questHud = qPanel.GetComponent<HudVisibility>();
                    if (questHud == null) questHud = qPanel.gameObject.AddComponent<HudVisibility>();
                }
            }
        }

        if (coinHud == null)
        {
            var coinObj = GameObject.Find("UI_Coins");
            if (coinObj == null) coinObj = GameObject.Find("Coin UI"); // Fallback
            if (coinObj != null)
            {
                coinHud = coinObj.GetComponent<HudVisibility>();
                if (coinHud == null) coinHud = coinObj.AddComponent<HudVisibility>();
            }
        }
    }

    /// <summary>
    /// Called when player starts dialogue with Guardian NPC.
    /// State: Gameplay -> Dialogue.
    /// Hides Quest panel and Coin counter.
    /// </summary>
    public void HandleDialogueStarted()
    {
        if (hasRunIntro && runOnlyOnce) return;

        currentState = IntroState.Dialogue;

        // 1. Smoothly fade out Quest panel and Coin counter
        if (questHud != null) questHud.SetVisible(false, hudFadeDuration);
        if (coinHud != null) coinHud.SetVisible(false, hudFadeDuration);

        // 2. Freeze player and unlock cursor
        SetPlayerControlsFrozen(true);
        SetCursorState(true);
    }

    /// <summary>
    /// Called when the player reaches the final dialogue line of Guardian NPC.
    /// </summary>
    public void HandleLastDialogueReached()
    {
        if (popUpOnLastDialogueLine)
        {
            ShowQuestTab();
        }
    }

    /// <summary>
    /// Legacy dialogue ended callback.
    /// Info cards and pre-quiz are owned exclusively by NPCInteraction.GuardianFlowRoutine.
    /// </summary>
    public void HandleDialogueEnded()
    {
        // Single Owner: GuardianFlowRoutine manages the exclusive flow
        return;
    }

    private void OpenTutorialCards()
    {
        // Disabled: Info cards are owned exclusively by GuardianFlowRoutine
    }

    /// <summary>
    /// Called when the player reaches / reads the last dialogue card on trash bin introduction.
    /// Pops up the quest tab!
    /// </summary>
    public void HandleLastCardReached()
    {
        // Only show quest tab if no modal is actively open; otherwise let HandleTutorialClosed show it
        if (popUpOnLastBinCard && (ModalManager.Instance == null || !ModalManager.Instance.IsAnyModalOpen))
        {
            ShowQuestTab();
        }
    }

    /// <summary>
    /// Smoothly pops up / fades in the quest tab.
    /// </summary>
    public void ShowQuestTab()
    {
        // Defer if a modal is currently open so quest panel does not bleed behind modals
        if (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalOpen)
        {
            return;
        }

        if (questHud != null)
        {
            if (questHud.transform.parent != null && !questHud.transform.parent.gameObject.activeSelf)
            {
                questHud.transform.parent.gameObject.SetActive(true);
            }
            if (!questHud.gameObject.activeSelf)
            {
                questHud.gameObject.SetActive(true);
            }
            questHud.SetVisible(true, hudFadeDuration);
        }
        if (QuestPanelToggleController.Instance != null)
        {
            QuestPanelToggleController.Instance.SetPanelVisible(true);
        }
    }

    /// <summary>
    /// Smoothly hides the quest tab when a quest is completed.
    /// </summary>
    public void HideQuestTab()
    {
        if (questHud != null)
        {
            questHud.SetVisible(false, hudFadeDuration);
        }
        if (QuestPanelToggleController.Instance != null)
        {
            QuestPanelToggleController.Instance.SetPanelVisible(false);
        }
    }

    /// <summary>
    /// Enters Dialogue state for repeat conversations with the Guardian after the first quest is complete.
    /// </summary>
    public void BeginRepeatDialogue()
    {
        currentState = IntroState.Dialogue;

        HudVisibilityService.Instance?.HideHUD("Cutscene", hudFadeDuration);

        SetPlayerControlsFrozen(true);
        SetCursorState(true);
    }

    /// <summary>
    /// Ends repeat dialogue with the Guardian, either starting a repeat quest (skipping Pre-Test and lesson cards)
    /// or returning directly to free-roam gameplay.
    /// </summary>
    public void EndRepeatDialogue(bool acceptedQuest)
    {
        currentState = IntroState.Gameplay;
        if (Time.timeScale == 0f) Time.timeScale = 1f;

        SetCursorState(false);
        SetPlayerControlsFrozen(false);

        HudVisibilityService.Instance?.ShowHUD("Cutscene", hudFadeDuration);

        if (acceptedQuest)
        {
            SetTrashVisible(true, false, "Repeat quest accepted");
            UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "Repeat quest accepted");
            ShowQuestTab();
            if (QuestPanelToggleController.Instance != null)
            {
                QuestPanelToggleController.Instance.SetPanelVisible(true, true);
            }
        }
    }

    /// <summary>
    /// Called when the Guardian tutorial sequence (Steps 1-4) finishes.
    /// Does NOT prematurely enable trash before Step 6 (FreePlay).
    /// </summary>
    public void HandleTutorialClosed()
    {
        currentState = IntroState.Gameplay;
        hasRunIntro = true;

        // 1. Re-lock cursor and restore crosshair
        SetCursorState(false);

        // 2. Unfreeze player controls
        SetPlayerControlsFrozen(false);

        if (coinHud != null) coinHud.SetVisible(true, hudFadeDuration);

        OnIntroCompleted?.Invoke();
    }

    public void HandleTabTipCompleted()
    {
        QuestManager.ResetAllQuestCountsToZero();
        SetTrashVisible(true, animateReveal: true, "Step 6 (FreePlay): TAB tutorial completed");
        UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "TAB tutorial completed -> FreePlay (trash enabled)");
        OnTutorialCompleted?.Invoke();
        HudVisibilityService.Instance?.ShowToast("Find the trash and sort it into the right bin!", 3.5f);
        UITheme.PlaySound(UITheme.Instance != null ? UITheme.Instance.buttonClickSound : null);
    }

    private IEnumerator StaggeredTrashRevealRoutine()
    {
        if (trashContainer == null) yield break;
        int childCount = trashContainer.transform.childCount;
        var initialScales = new Vector3[childCount];
        for (int i = 0; i < childCount; i++)
        {
            var child = trashContainer.transform.GetChild(i);
            initialScales[i] = child.localScale;
            child.localScale = Vector3.zero;
        }

        for (int i = 0; i < childCount; i++)
        {
            var child = trashContainer.transform.GetChild(i);
            StartCoroutine(ScaleChildPop(child, initialScales[i], 0.25f));
            yield return new WaitForSecondsRealtime(0.03f);
        }
    }

    private IEnumerator ScaleChildPop(Transform t, Vector3 targetScale, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && t != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float tNorm = Mathf.Clamp01(elapsed / duration);
            float s = Mathf.Sin(tNorm * Mathf.PI * 0.5f);
            t.localScale = targetScale * s;
            yield return null;
        }
        if (t != null)
        {
            t.localScale = targetScale;
            t.GetComponent<DragAndDrop>()?.EnsurePickable(preserveReleasedState: true);
        }
    }

    /// <summary>
    /// Centralized single-owner cursor lock state management.
    /// </summary>
    public void SetCursorState(bool unlocked)
    {
        CursorState.SetLocked(!unlocked, playerInputs);
    }

    /// <summary>
    /// Freezes or unfreezes player movement and look input.
    /// </summary>
    public void SetPlayerControlsFrozen(bool frozen)
    {
        if (playerController != null)
        {
            playerController.enabled = !frozen;
        }

        if (playerInputs != null && frozen)
        {
            playerInputs.move = Vector2.zero;
            playerInputs.look = Vector2.zero;
        }
    }
}
