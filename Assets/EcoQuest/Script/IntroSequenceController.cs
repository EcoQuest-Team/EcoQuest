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
    public static IntroSequenceController Instance { get; private set; }

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

    public IntroState CurrentState => currentState;
    public bool HasRunIntro => hasRunIntro;
    public event System.Action OnIntroCompleted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ResolveReferences();
    }

    private void OnEnable()
    {
        if (Instance == null) Instance = this;
        ResolveReferences();
        SubscribeEvents();
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
    }

    private void Start()
    {
        ResolveReferences();

        // Quest HUD starts HIDDEN at the beginning of the game.
        // It pops up when the player reads the last dialogue on trash bin introduction.
        if (questHud != null) questHud.SetVisibleImmediate(false);

        // Coin counter remains visible in gameplay
        if (coinHud != null) coinHud.SetVisibleImmediate(true);
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();
        if (Instance == this) Instance = null;
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

        if (tutorialManager != null)
        {
            tutorialManager.OnLastCardReached += HandleLastCardReached;
            tutorialManager.OnTutorialClosed += HandleTutorialClosed;
        }

        var tipsMgr = GameplayControlTipsManager.Instance != null 
            ? GameplayControlTipsManager.Instance 
            : FindFirstObjectByType<GameplayControlTipsManager>();
        if (tipsMgr != null)
        {
            tipsMgr.OnTabTipCompleted -= HandleTabTipCompleted;
            tipsMgr.OnTabTipCompleted += HandleTabTipCompleted;
            if (tipsMgr.HasCompletedAllTips && hasRunIntro)
            {
                HandleTabTipCompleted();
            }
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
    /// Called when dialogue with Guardian NPC ends (accept mission).
    /// State: Dialogue -> BinIntro.
    /// Enforces sequential transition: close dialogue -> keep player frozen & cursor unlocked -> StartPreQuiz -> OpenTutorialCards.
    /// </summary>
    public void HandleDialogueEnded()
    {
        if (currentState == IntroState.BinIntro) return;
        if (hasRunIntro && runOnlyOnce && currentState != IntroState.Dialogue) return;

        currentState = IntroState.BinIntro;

        // Step B: Keep player controls frozen and mouse cursor unlocked (Time.timeScale = 1f so camera blend finishes)
        if (Time.timeScale == 0f) Time.timeScale = 1f;
        SetPlayerControlsFrozen(true);
        SetCursorState(true);

        // Ensure UI_TutorialCards stays hidden while UI_Quiz is active
        if (tutorialManager != null)
        {
            if (tutorialManager.tutorialOverlay != null)
                tutorialManager.tutorialOverlay.SetActive(false);
            if (tutorialManager.tutorialCards != null)
            {
                foreach (var card in tutorialManager.tutorialCards)
                {
                    if (card != null) card.SetActive(false);
                }
            }
        }

        // Step C & D: Run Pre-Quiz first; only open the 3 Bin Educational Cards after Pre-Quiz closes
        QuizManager qm = QuizManager.Instance != null ? QuizManager.Instance : FindFirstObjectByType<QuizManager>();
        if (qm != null && !qm.IsPreQuizCompleted)
        {
            qm.StartPreQuiz(() =>
            {
                SetPlayerControlsFrozen(true);
                SetCursorState(true);
                OpenTutorialCards();
            });
        }
        else
        {
            OpenTutorialCards();
        }
    }

    private void OpenTutorialCards()
    {
        // Keep player frozen and cursor unlocked for the 3 bin cards
        SetPlayerControlsFrozen(true);
        SetCursorState(true);

        // Open Info Cards in selected mode (all cards or single card)
        if (tutorialManager != null)
        {
            if (showAllTutorialCards)
                tutorialManager.ShowTutorial(0, singleCard: false);
            else
                tutorialManager.ShowSingleCard(0);
        }
    }

    /// <summary>
    /// Called when the player reaches / reads the last dialogue card on trash bin introduction.
    /// Pops up the quest tab!
    /// </summary>
    public void HandleLastCardReached()
    {
        if (popUpOnLastBinCard)
        {
            ShowQuestTab();
        }
    }

    /// <summary>
    /// Smoothly pops up / fades in the quest tab.
    /// </summary>
    public void ShowQuestTab()
    {
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

        if (questHud != null) questHud.SetVisible(false, hudFadeDuration);
        if (coinHud != null) coinHud.SetVisible(false, hudFadeDuration);

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

        if (coinHud != null) coinHud.SetVisible(true, hudFadeDuration);

        if (acceptedQuest)
        {
            if (trashContainer != null)
            {
                trashContainer.SetActive(true);
            }
            MinimapIcon.SetTrashIconsActive(true);
            ShowQuestTab();
            if (QuestPanelToggleController.Instance != null)
            {
                QuestPanelToggleController.Instance.SetPanelVisible(true, true);
            }
        }
    }

    /// <summary>
    /// Called when the Info Card is closed by clicking anywhere or pressing E.
    /// State: BinIntro -> Gameplay.
    /// Re-locks cursor, unfreezes player, ensures HUD is visible, and spawns trash.
    /// </summary>
    public void HandleTutorialClosed()
    {
        if (currentState != IntroState.BinIntro) return;

        currentState = IntroState.Gameplay;
        hasRunIntro = true;

        // 1. Re-lock cursor and restore crosshair
        SetCursorState(false);

        // 2. Unfreeze player controls
        SetPlayerControlsFrozen(false);

        // 3. Ensure Quest panel and Coin counter are visible
        ShowQuestTab();
        if (coinHud != null) coinHud.SetVisible(true, hudFadeDuration);

        OnIntroCompleted?.Invoke();
    }

    public void HandleTabTipCompleted()
    {
        if (trashContainer != null)
        {
            trashContainer.SetActive(true);
        }
        MinimapIcon.SetTrashIconsActive(true);
    }

    /// <summary>
    /// Centralized single-owner cursor lock state management.
    /// </summary>
    public void SetCursorState(bool unlocked)
    {
        if (unlocked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (playerInputs != null)
            {
                playerInputs.cursorLocked = false;
                playerInputs.cursorInputForLook = false;
            }

            if (CenterScreenCursor.Instance != null)
            {
                CenterScreenCursor.Instance.SetVisible(false);
            }
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (playerInputs != null)
            {
                playerInputs.cursorLocked = true;
                playerInputs.cursorInputForLook = true;
            }

            if (CenterScreenCursor.Instance != null)
            {
                CenterScreenCursor.Instance.SetVisible(true);
            }
        }
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
