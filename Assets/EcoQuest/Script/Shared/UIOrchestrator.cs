using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of truth for UI element visibility across all game phases.
/// Matrix enforcement:
/// Phase: Tutorial -> FreePlay -> QuestActive
/// Modal counter overlays all phases (all HUD hidden while any modal is open).
/// ProfileValidator runs every 1.0s, snaps un-animating mismatches, rate-limits logs.
/// </summary>
public class UIOrchestrator : MonoBehaviour
{
    private static UIOrchestrator instance;
    public static UIOrchestrator Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<UIOrchestrator>(FindObjectsInactive.Include);
                if (instance == null)
                {
                    var go = GameObject.Find("[UIOrchestrator]");
                    if (go == null) go = new GameObject("[UIOrchestrator]");
                    if (Application.isPlaying && go.transform.parent == null) DontDestroyOnLoad(go);
                    instance = go.GetComponent<UIOrchestrator>() ?? go.AddComponent<UIOrchestrator>();
                }
            }
            return instance;
        }
    }

    public enum GamePhase
    {
        Tutorial = 0,
        FreePlay = 1,
        QuestActive = 2,
        QuestComplete = 3
    }

    public enum HudProfile
    {
        Title = 0,
        ForestExploring = 1,
        BinSorting = 2,
        CityLitterbug = 3,
        CityScavenger = 4,
        Inventory = 5
    }

    // Optional City Trash Detective (CityScavenger) additive hooks (default null outside City Scene)
    public static Func<bool> IsCityScavengerActiveHook;
    public static Func<bool> IsCityScavengerOverlayOpenHook;

    public static bool IsCityScene()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        return scene.name == "City Scene" || scene.path.Contains("City Scene");
    }

    public GamePhase EvaluateLivePhase(string caller = "")
    {
        if (IsCityScene())
        {
            if (IsCityScavengerActiveHook != null && IsCityScavengerActiveHook())
            {
                return GamePhase.QuestActive;
            }
            if (LitterbugQuestManager.Instance != null)
            {
                var qState = LitterbugQuestManager.Instance.QuestState;
                if (qState == LitterbugQuestState.Active || qState == LitterbugQuestState.ReadyToTurnIn)
                {
                    return GamePhase.QuestActive;
                }
                if (qState == LitterbugQuestState.Completed)
                {
                    return GamePhase.QuestComplete;
                }
            }
            return GamePhase.FreePlay;
        }
        else
        {
            bool tabTipDone = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0) == 1;
            if (tabTipDone || (NPCInteraction.IsAnyQuestActiveSignal && !NPCInteraction.IsAnyGuardianFlowRunning))
            {
                return GamePhase.QuestActive;
            }
            if (SequentialWASDTutorial.Instance != null && !SequentialWASDTutorial.IsTutorialCompleted)
            {
                return GamePhase.Tutorial;
            }
            return GamePhase.FreePlay;
        }
    }

    /// <summary>
    /// Explicit tutorial and gameplay flow steps (Single Source of Truth):
    /// Step 0: Tutorial (initial movement & map controls)
    /// Step 1: Intro (Intro dialogue with Guardian NPC)
    /// Step 2: InfoCards (BIN INFO panels, one per bin)
    /// Step 3: Quiz (Questions panel 1-6 PreQuiz)
    /// Step 4: Guardian (Guardian quest offer dialogue)
    /// Step 5: TabTutorial (TAB tutorial teaching hide/show quest tab)
    /// Step 6: FreePlay (Quest tab available, trash items enabled)
    /// </summary>
    public enum FlowStep
    {
        Tutorial = 0,
        Intro = 1,
        InfoCards = 2,
        Quiz = 3,
        Guardian = 4,
        TabTutorial = 5,
        FreePlay = 6,
        ExplorerTravel = 7
    }

    public enum UIElement
    {
        Gear,
        Minimap,
        Coins,
        Crosshair,
        QuestPanel,
        TutorialCard,
        TabTipCard,
        TipCards,
        InteractPrompt
    }

    public static event System.Action<FlowStep, GamePhase> OnFlowStateChanged;

    [Header("Phase & Flow Step Configuration")]
    [SerializeField] private GamePhase currentPhase = GamePhase.Tutorial;
    public GamePhase CurrentPhase => currentPhase;

    [SerializeField] private FlowStep currentFlowStep = FlowStep.Tutorial;
    public FlowStep CurrentFlowStep => currentFlowStep;

    [Header("Watchdog / Profile Validator")]
    [SerializeField] private bool watchdogEnabled = true;
    public bool WatchdogEnabled { get => watchdogEnabled; set => watchdogEnabled = value; }

    [Header("Tracked Elements")]
    [SerializeField] private CanvasGroup gearGroup;
    [SerializeField] private CanvasGroup minimapGroup;
    [SerializeField] private CanvasGroup coinsGroup;
    [SerializeField] private CanvasGroup crosshairGroup;
    [SerializeField] private CanvasGroup questPanelGroup;
    [SerializeField] private CanvasGroup tutorialCardGroup;
    [SerializeField] private CanvasGroup interactPromptGroup;

    private readonly Dictionary<UIElement, CanvasGroup> elementGroups = new Dictionary<UIElement, CanvasGroup>();
    private readonly Dictionary<CanvasGroup, float> lastLogTimes = new Dictionary<CanvasGroup, float>();
    private readonly Dictionary<CanvasGroup, float> lastSnapTimes = new Dictionary<CanvasGroup, float>();
    private readonly Dictionary<UIElement, string> elementOwnerTokens = new Dictionary<UIElement, string>();
    private readonly Dictionary<UIElement, bool> elementOwnerRequestedStates = new Dictionary<UIElement, bool>();
    private readonly Dictionary<UIElement, float> mismatchStartTimes = new Dictionary<UIElement, float>();
    private readonly HashSet<UIElement> correctedElements = new HashSet<UIElement>();
    private readonly HashSet<UIElement> loggedConflictElements = new HashSet<UIElement>();

    private bool isQuestPanelUserHidden = false;
    public bool IsQuestPanelUserHidden => isQuestPanelUserHidden;
    public string LastWriter { get; private set; } = "Initial";

    public static readonly Vector2 SharedQuestPanelHomePos = new Vector2(40f, -192f);
    public static readonly Vector2 SharedQuestPanelHiddenPos = new Vector2(-400f, -192f);

    private Coroutine questPanelSlideRoutine;
    private Vector2 questPanelBasePos;
    private bool hasQuestPanelBasePos = false;

    public void ToggleQuestPanel(string caller = "User")
    {
        if (IsCityScene())
        {
            if (activeOverlays.Count == 0 && currentFlowStep == FlowStep.ExplorerTravel)
            {
                currentFlowStep = FlowStep.FreePlay;
            }
            currentPhase = EvaluateLivePhase("ToggleQuestPanel");
        }

        bool allowedByStep;
        if (IsCityScene())
        {
            allowedByStep = (currentPhase == GamePhase.QuestActive || currentPhase == GamePhase.QuestComplete);
        }
        else
        {
            allowedByStep = (currentFlowStep == FlowStep.TabTutorial || currentFlowStep == FlowStep.FreePlay) && currentPhase == GamePhase.QuestActive;
        }
        if (!allowedByStep) return;
        bool tabTipActive = IsElementOwned(UIElement.TabTipCard);
        if (AreModalsOpen && !tabTipActive) return;

        isQuestPanelUserHidden = !isQuestPanelUserHidden;
        LastWriter = $"{caller}:ToggleQuestPanel(hidden={isQuestPanelUserHidden})";

        ResolveReferences();
        bool panelVisible = GetExpectedVisibility(UIElement.QuestPanel);
#if UNITY_EDITOR
        Debug.Log($"[FlowTrace] TAB: toggle -> userHidden={isQuestPanelUserHidden}, panelVisible={panelVisible}, phase={currentPhase}, step={currentFlowStep}, overlays=[{ActiveOverlaysSummary}]");
#endif

        // TAB toggles ONLY the quest tab, nothing else
        if (questPanelGroup != null)
        {
            AnimateQuestPanel(questPanelGroup, panelVisible, 0.25f);
        }
        else
        {
            ApplyCurrentProfile(0.25f);
        }
    }

    private void Update()
    {
        EcoQuest.CityQuest.CityTrashDetectiveManager.TickRobinCooldownGlobal();
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (IsCityScene() || QuestPanelToggleController.Instance == null || !QuestPanelToggleController.Instance.gameObject.activeInHierarchy)
            {
                ToggleQuestPanel("TAB");
            }
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus) EcoQuest.CityQuest.CityTrashDetectiveManager.FlushRobinCooldownSave();
    }

    private void OnApplicationQuit()
    {
        EcoQuest.CityQuest.CityTrashDetectiveManager.FlushRobinCooldownSave();
    }

    public void SetQuestPanelUserHidden(bool hidden, string caller = "System")
    {
        if (isQuestPanelUserHidden == hidden) return;
        isQuestPanelUserHidden = hidden;
        LastWriter = $"{caller}:SetQuestPanelUserHidden({hidden})";
        ApplyCurrentProfile(0.25f);
    }

    public void AcquireElementOwnership(UIElement element, string ownerToken, bool requestedVisible = true)
    {
        elementOwnerTokens[element] = ownerToken;
        elementOwnerRequestedStates[element] = requestedVisible;
        ApplyCurrentProfile(0.20f);
    }

    public void ReleaseElementOwnership(UIElement element, string ownerToken)
    {
        if (elementOwnerTokens.TryGetValue(element, out string current) && current == ownerToken)
        {
            elementOwnerTokens.Remove(element);
            elementOwnerRequestedStates.Remove(element);
            ApplyCurrentProfile(0.20f);
        }
    }

    public bool IsElementOwned(UIElement element) => elementOwnerTokens.ContainsKey(element);

    private bool modalsOpen = false;
    public bool AreModalsOpen => (ModalManager.Instance != null) ? ModalManager.Instance.IsAnyModalActuallyVisible() : modalsOpen;

    private Coroutine validatorRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize()
    {
        if (instance == null)
        {
            var go = new GameObject("[UIOrchestrator]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<UIOrchestrator>();
        }
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

        ResolveReferences();
        activeOverlays.Clear();
        overlayOrder.Clear();
    }

    private void OnDestroy()
    {
        EcoQuest.CityQuest.CityTrashDetectiveManager.FlushRobinCooldownSave();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        hasQuestPanelBasePos = false;
        ResolveReferences();
        if (scene.name == "City Scene" || scene.path.Contains("City Scene"))
        {
            ClearAllOverlays();
            isQuestPanelUserHidden = false;
            SetFlowStep(FlowStep.FreePlay, "City Scene Loaded");
            SetPhase(EvaluateLivePhase("City Scene Loaded"), "City Scene Loaded");
        }
        ApplyCurrentProfile(0f);
    }

    public void ClearAllOverlays()
    {
        activeOverlays.Clear();
        overlayOrder.Clear();
        if (currentFlowStep == FlowStep.ExplorerTravel)
        {
            currentFlowStep = FlowStep.FreePlay;
        }
        currentPhase = EvaluateLivePhase("ClearAllOverlays");
        ApplyCurrentProfile(0f);
    }

    private void Start()
    {
        ResolveReferences();
        EvaluateInitialPhase();
        ApplyCurrentProfile(0f);

        if (validatorRoutine != null) StopCoroutine(validatorRoutine);
        validatorRoutine = StartCoroutine(ProfileValidatorLoop());
    }

    private void OnEnable()
    {
        if (validatorRoutine == null && Application.isPlaying)
        {
            validatorRoutine = StartCoroutine(ProfileValidatorLoop());
        }
    }

    private void OnDisable()
    {
        if (validatorRoutine != null)
        {
            StopCoroutine(validatorRoutine);
            validatorRoutine = null;
        }
    }

    public void ResolveReferences()
    {
        elementGroups.Clear();
        var uiRoot = GameObject.Find("--- UI ---");
        if (uiRoot == null) return;

        gearGroup = FindOrAddCG(uiRoot, "UI_HUD_SettingsButton");
        minimapGroup = FindOrAddCG(uiRoot, "UI_Minimap");
        coinsGroup = FindOrAddCG(uiRoot, "UI_Coins");
        crosshairGroup = FindOrAddCG(uiRoot, "UI_Crosshair");
        if (crosshairGroup == null)
        {
            var dotCanvas = GameObject.Find("CenterCursorCanvas");
            if (dotCanvas != null)
            {
                crosshairGroup = dotCanvas.GetComponent<CanvasGroup>() ?? dotCanvas.AddComponent<CanvasGroup>();
            }
        }
        questPanelGroup = FindOrAddCG(uiRoot, "UI_Quest/QuestPanel");
        if (questPanelGroup == null)
        {
            bool cityScavengerActive = IsCityScene() && IsCityScavengerActiveHook != null && IsCityScavengerActiveHook();
            if (cityScavengerActive)
            {
                var cityPanel = FindOrAddCG(uiRoot, "UI_CityTrashDetective/ChecklistPanel");
                if (cityPanel != null && questPanelGroup != cityPanel)
                {
                    hasQuestPanelBasePos = false;
                    questPanelGroup = cityPanel;
                }
            }
            if (questPanelGroup == null)
            {
                var lbPanel = FindOrAddCG(uiRoot, "UI_LitterbugQuest/QuestPanel");
                if (lbPanel != null && questPanelGroup != lbPanel)
                {
                    hasQuestPanelBasePos = false;
                    questPanelGroup = lbPanel;
                }
            }
        }
        questPanelBasePos = SharedQuestPanelHomePos;
        hasQuestPanelBasePos = true;
        if (questPanelGroup != null)
        {
            var rt = questPanelGroup.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = questPanelBasePos;
            }
        }
        tutorialCardGroup = FindOrAddCG(uiRoot, "UI_Notification/Control Tip Banner");
        interactPromptGroup = FindOrAddCG(uiRoot, "UI_InteractionPrompt");
        if (interactPromptGroup == null)
        {
            interactPromptGroup = FindOrAddCG(uiRoot, "UI_InteractPrompt");
        }

        if (gearGroup != null) elementGroups[UIElement.Gear] = gearGroup;
        if (minimapGroup != null) elementGroups[UIElement.Minimap] = minimapGroup;
        if (coinsGroup != null) elementGroups[UIElement.Coins] = coinsGroup;
        if (crosshairGroup != null) elementGroups[UIElement.Crosshair] = crosshairGroup;
        if (questPanelGroup != null) elementGroups[UIElement.QuestPanel] = questPanelGroup;
        if (tutorialCardGroup != null) elementGroups[UIElement.TutorialCard] = tutorialCardGroup;
        // UI_InteractPrompt is a contextual world-anchored prompt managed by WorldInteractionPrompt,
        // not a persistent HUD element. Do not register in elementGroups to prevent HUD snapping conflicts.

        // Register elements in HudVisibilityService so legacy watchdog won't fight
        if (HudVisibilityService.Instance != null)
        {
            if (tutorialCardGroup != null) HudVisibilityService.Instance.RegisterOwnedElement(tutorialCardGroup);
            if (gearGroup != null) HudVisibilityService.Instance.RegisterOwnedElement(gearGroup);
            if (questPanelGroup != null) HudVisibilityService.Instance.RegisterOwnedElement(questPanelGroup);
            if (minimapGroup != null) HudVisibilityService.Instance.RegisterOwnedElement(minimapGroup);
        }
    }

    private CanvasGroup FindOrAddCG(GameObject root, string path)
    {
        var t = root.transform.Find(path);
        if (t == null) return null;
        var cg = t.GetComponent<CanvasGroup>();
        if (cg == null) cg = t.gameObject.AddComponent<CanvasGroup>();
        return cg;
    }

    public void EvaluateInitialPhase()
    {
        if (IsCityScene())
        {
            currentFlowStep = FlowStep.FreePlay;
            currentPhase = EvaluateLivePhase("City EvaluateInitialPhase");
            return;
        }

        bool tabTipDone = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0) == 1;
        if (tabTipDone)
        {
            currentFlowStep = FlowStep.FreePlay;
            currentPhase = GamePhase.QuestActive;
        }
        else if (SequentialWASDTutorial.Instance != null && !SequentialWASDTutorial.IsTutorialCompleted)
        {
            currentFlowStep = FlowStep.Tutorial;
            currentPhase = GamePhase.Tutorial;
        }
        else if (NPCInteraction.IsAnyQuestActiveSignal && !NPCInteraction.IsAnyGuardianFlowRunning)
        {
            currentFlowStep = FlowStep.FreePlay;
            currentPhase = GamePhase.QuestActive;
        }
        else
        {
            currentFlowStep = FlowStep.Tutorial;
            currentPhase = GamePhase.FreePlay;
        }
    }

    /// <summary>
    /// Transitions to an explicit FlowStep, logs a single [FlowState] transition line,
    /// applies the step's HUD profile, and validates HUD & trash state against the profile.
    /// </summary>
    public void SetFlowStep(FlowStep newStep, string reason = "")
    {
        FlowStep prevStep = currentFlowStep;
        currentFlowStep = newStep;

        string reasonStr = string.IsNullOrEmpty(reason) ? "Manual transition" : reason;
        Debug.Log($"[FlowState] {prevStep} -> {newStep} (reason: {reasonStr})");

        switch (newStep)
        {
            case FlowStep.Tutorial:
                currentPhase = GamePhase.Tutorial;
                break;

            case FlowStep.Intro:
            case FlowStep.InfoCards:
            case FlowStep.Quiz:
            case FlowStep.Guardian:
                currentPhase = GamePhase.FreePlay;
                break;

            case FlowStep.TabTutorial:
                currentPhase = GamePhase.QuestActive;
                isQuestPanelUserHidden = false;
                modalsOpen = false;
                break;

            case FlowStep.FreePlay:
                currentPhase = GamePhase.QuestActive;
                if (ModalManager.Instance == null || !ModalManager.Instance.IsAnyModalActuallyVisible())
                {
                    modalsOpen = false;
                }
                break;

            case FlowStep.ExplorerTravel:
                currentPhase = GamePhase.QuestActive;
                modalsOpen = false;
                break;
        }

        LastWriter = $"SetFlowStep({newStep})";
        ApplyCurrentProfile(0.20f);
        ValidateStepState(newStep);
        OnFlowStateChanged?.Invoke(currentFlowStep, currentPhase);
    }

    public struct OverlaySnapshot
    {
        public string overlayId;
        public FlowStep flowStep;
        public GamePhase phase;
        public bool isQuestPanelUserHidden;
        public bool modalsOpen;
    }

    private readonly Dictionary<string, OverlaySnapshot> activeOverlays = new Dictionary<string, OverlaySnapshot>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> overlayOrder = new List<string>();
    private Coroutine nextFrameVerifyRoutine;
    private float noOverlayOrModalDuration = 0f;

    public bool IsOverlayActive => activeOverlays.Count > 0;
    public int ActiveOverlayCount => activeOverlays.Count;
    public string ActiveOverlaysSummary => activeOverlays.Count == 0 ? "none" : string.Join(",", overlayOrder);
    public IReadOnlyCollection<string> ActiveOverlayIds => overlayOrder;

    /// <summary>
    /// Idempotent overlay push per overlayName: pushing an overlay id already in the set is a no-op.
    /// </summary>
    public void PushOverlay(string overlayName)
    {
        if (string.IsNullOrWhiteSpace(overlayName)) overlayName = "DefaultOverlay";

        if (activeOverlays.ContainsKey(overlayName))
        {
#if UNITY_EDITOR
            Debug.Log($"[FlowTrace] PushOverlay('{overlayName}') no-op (already active; set=[{ActiveOverlaysSummary}])");
#endif
            return;
        }

        FlowStep baseStep = currentFlowStep != FlowStep.ExplorerTravel ? currentFlowStep : FlowStep.FreePlay;
        if (overlayOrder.Count > 0 && activeOverlays.TryGetValue(overlayOrder[0], out var firstSnap))
        {
            baseStep = firstSnap.flowStep;
        }

        activeOverlays[overlayName] = new OverlaySnapshot
        {
            overlayId = overlayName,
            flowStep = baseStep,
            phase = currentPhase,
            isQuestPanelUserHidden = isQuestPanelUserHidden,
            modalsOpen = modalsOpen
        };
        overlayOrder.Add(overlayName);

        FlowStep prevStep = currentFlowStep;
        currentFlowStep = FlowStep.ExplorerTravel;
        LastWriter = $"PushOverlay({overlayName})";
        Debug.Log($"[FlowState] Push overlay '{overlayName}' (base step: {prevStep})");
#if UNITY_EDITOR
        Debug.Log($"[FlowTrace] PushOverlay('{overlayName}') -> overlays=[{ActiveOverlaysSummary}], step={currentFlowStep}, phase={currentPhase}, modals={AreModalsOpen}");
#endif
        ApplyCurrentProfile(0.20f);
    }

    /// <summary>
    /// Idempotent overlay pop: removes overlayName (or last pushed if null).
    /// Popping an id not in the set is a safe no-op.
    /// When the overlay set becomes empty, immediately restores the live gameplay HUD profile and verifies next frame.
    /// </summary>
    public void PopOverlay(string overlayName = null)
    {
        if (activeOverlays.Count == 0)
        {
            if (currentFlowStep == FlowStep.ExplorerTravel)
            {
                currentFlowStep = FlowStep.FreePlay;
            }
            currentPhase = EvaluateLivePhase("PopOverlayEmptySet");
#if UNITY_EDITOR
            Debug.Log($"[FlowTrace] PopOverlay('{overlayName ?? "*"}') no-op (set already empty) -> step={currentFlowStep}, phase={currentPhase}");
#endif
            ApplyCurrentProfile(0.20f);
            OnFlowStateChanged?.Invoke(currentFlowStep, currentPhase);
            return;
        }

        string targetKey = overlayName;
        if (string.IsNullOrWhiteSpace(targetKey))
        {
            targetKey = overlayOrder[overlayOrder.Count - 1];
        }

        if (!activeOverlays.TryGetValue(targetKey, out var snap))
        {
#if UNITY_EDITOR
            Debug.Log($"[FlowTrace] PopOverlay('{targetKey}') no-op (id not in active set=[{ActiveOverlaysSummary}])");
#endif
            return;
        }

        activeOverlays.Remove(targetKey);
        overlayOrder.RemoveAll(k => string.Equals(k, targetKey, StringComparison.OrdinalIgnoreCase));

        if (activeOverlays.Count == 0)
        {
            FlowStep overlayStep = currentFlowStep;
            currentFlowStep = (snap.flowStep == FlowStep.ExplorerTravel) ? FlowStep.FreePlay : snap.flowStep;
            if (IsCityScene() && currentFlowStep != FlowStep.FreePlay)
            {
                currentFlowStep = FlowStep.FreePlay;
            }

            currentPhase = EvaluateLivePhase("PopOverlay");
            isQuestPanelUserHidden = snap.isQuestPanelUserHidden;
            if (ModalManager.Instance == null || !ModalManager.Instance.IsAnyModalActuallyVisible())
            {
                modalsOpen = false;
            }

            LastWriter = $"PopOverlay({targetKey})";
            Debug.Log($"[FlowState] Pop overlay '{targetKey}' -> restored step {currentFlowStep}, live phase {currentPhase} (from {overlayStep})");
#if UNITY_EDITOR
            Debug.Log($"[FlowTrace] PopOverlay('{targetKey}') -> set empty, restored step={currentFlowStep}, phase={currentPhase}, userHidden={isQuestPanelUserHidden}");
#endif
            ApplyCurrentProfile(0.20f);
            ValidateStepState(currentFlowStep);
            OnFlowStateChanged?.Invoke(currentFlowStep, currentPhase);

            if (isActiveAndEnabled && Application.isPlaying)
            {
                if (nextFrameVerifyRoutine != null) StopCoroutine(nextFrameVerifyRoutine);
                nextFrameVerifyRoutine = StartCoroutine(VerifyHudRestoredNextFrameRoutine(targetKey));
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.Log($"[FlowTrace] PopOverlay('{targetKey}') -> remaining overlays=[{ActiveOverlaysSummary}]");
#endif
        }
    }

    private IEnumerator VerifyHudRestoredNextFrameRoutine(string poppedOverlay)
    {
        yield return null;
        if (activeOverlays.Count > 0 || AreModalsOpen) yield break;

        ResolveReferences();
        foreach (var kvp in elementGroups)
        {
            var element = kvp.Key;
            var cg = kvp.Value;
            if (cg == null) continue;

            bool expected = GetExpectedVisibility(element);
            if (expected)
            {
                if (!cg.gameObject.activeSelf) cg.gameObject.SetActive(true);
                var anim = cg.GetComponent<UIAnimator>();
                bool isAnim = (anim != null && anim.IsAnimating) || (element == UIElement.QuestPanel && questPanelSlideRoutine != null);
                if (!isAnim && cg.alpha < 0.99f)
                {
                    SnapElement(cg, true);
                }
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
        }
#if UNITY_EDITOR
        Debug.Log($"[FlowTrace] NextFrameVerify after '{poppedOverlay}': Gear={(gearGroup != null ? gearGroup.alpha : -1f):F2}, Minimap={(minimapGroup != null ? minimapGroup.alpha : -1f):F2}, Coins={(coinsGroup != null ? coinsGroup.alpha : -1f):F2}, Crosshair={(crosshairGroup != null ? crosshairGroup.alpha : -1f):F2}, QuestPanel={(questPanelGroup != null ? questPanelGroup.alpha : -1f):F2}");
#endif
        nextFrameVerifyRoutine = null;
    }

    public void EnterCinematicProfile(string caller = "ExplorerGate")
    {
        PushOverlay(caller);
        WorldInteractionPrompt.Instance?.SetBlocked(true);
    }

    public void ExitCinematicProfile(string caller = "ExplorerGate")
    {
        PopOverlay(caller);
        if (!IsOverlayActive)
        {
            WorldInteractionPrompt.Instance?.SetBlocked(false);
        }
    }

    /// <summary>
    /// Smart Safeguard 3: Validates at each step start that HUD state and trash visibility
    /// match the expected profile for that step, logging a warning if mismatched.
    /// </summary>
    public void ValidateStepState(FlowStep step)
    {
        bool questExpected = (step == FlowStep.TabTutorial || step == FlowStep.FreePlay) && !isQuestPanelUserHidden && !AreModalsOpen;
        bool questActual = questPanelGroup != null && questPanelGroup.gameObject.activeInHierarchy && questPanelGroup.alpha > 0.5f;
        if (step <= FlowStep.Guardian && questActual)
        {
            Debug.LogWarning($"[FlowState Validator] WARNING at Step '{step}': QuestPanel is visible (alpha={questPanelGroup.alpha:F2}) when it MUST be hidden during Steps 1-4! Forcing hidden.");
            SnapElement(questPanelGroup, false);
        }

        bool minimapExpected = (step != FlowStep.ExplorerTravel) && !(EcoQuest.Inventory.InventoryController.Instance != null && EcoQuest.Inventory.InventoryController.Instance.IsOpen);
        bool minimapActual = minimapGroup == null || (minimapGroup.gameObject.activeInHierarchy && minimapGroup.alpha > 0.5f);
        if (minimapExpected && !minimapActual && minimapGroup != null)
        {
            Debug.LogWarning($"[FlowState Validator] WARNING at Step '{step}': Minimap was hidden (alpha={minimapGroup.alpha:F2})! Restoring Minimap immediately.");
            SnapElement(minimapGroup, true);
        }

        // Trash visibility is a pure function of persistent quest state (independent of temporary cinematic/dialogue profiles)
        bool trashExpected = TrashVisibilityController.ShouldTrashBeVisible();
        bool trashActual = TrashItem.AreTrashItemsAllowed || MinimapController.AreTrashIconsActive;
        if (!trashExpected && trashActual && step < FlowStep.TabTutorial)
        {
            Debug.LogWarning($"[FlowState Validator] WARNING at Step '{step}': Trash visibility is enabled before quest start! Hiding trash.");
            TrashVisibilityController.Refresh($"Validator correction at {step}");
        }
        else if (trashExpected && !trashActual && Application.isPlaying)
        {
            TrashVisibilityController.Refresh($"Validator restore at {step}");
        }
    }

    public void SetPhase(GamePhase newPhase, string reason = "")
    {
        if (currentPhase == newPhase) return;
        Debug.Log($"[UIOrchestrator] Phase changed: {currentPhase} -> {newPhase}. Reason: {(string.IsNullOrEmpty(reason) ? "Manual call" : reason)}");
        currentPhase = newPhase;
        if (newPhase == GamePhase.QuestActive && currentFlowStep < FlowStep.TabTutorial && !NPCInteraction.IsAnyGuardianFlowRunning)
        {
            currentFlowStep = FlowStep.FreePlay;
        }
        if (newPhase == GamePhase.QuestActive && (ModalManager.Instance == null || !ModalManager.Instance.IsAnyModalActuallyVisible()))
        {
            modalsOpen = false;
        }
        ApplyCurrentProfile(0.20f);
        OnFlowStateChanged?.Invoke(currentFlowStep, currentPhase);
    }

    public void SetModalsOpen(bool open)
    {
        modalsOpen = open;
        // Failsafe: when a modal closes, always restore the CURRENT step's HUD profile instead of showing everything
        ApplyCurrentProfile(0.20f);
    }

    /// <summary>
    /// Checks whether an element is expected to be visible according to the current FlowStep HUD profile.
    /// </summary>
    public bool GetExpectedVisibility(UIElement element)
    {
        if (EcoQuest.Inventory.InventoryController.Instance != null && EcoQuest.Inventory.InventoryController.Instance.IsOpen)
        {
            if (element == UIElement.Coins)
            {
                return EcoQuest.Inventory.InventoryController.Instance.KeepVitalsVisible;
            }
            return false;
        }

        // Step 7: ExplorerTravel (Cinematic HUD profile hides all gameplay elements during dialogue and travel)
        if (currentFlowStep == FlowStep.ExplorerTravel)
        {
            return false;
        }

        // Rule: The minimap stays visible through the whole flow and is NEVER hidden by any modal or the TAB toggle.
        if (element == UIElement.Minimap)
        {
            return true;
        }

        if (elementOwnerTokens.ContainsKey(element))
        {
            return elementOwnerRequestedStates[element];
        }

        // TabTip is an owner-controlled overlay, NOT a modal.
        bool tabTipActive = IsElementOwned(UIElement.TabTipCard);

        // Steps 1-4 (Intro, InfoCards, Quiz, Guardian) have explicit modal HUD profiles:
        // Quest Tab, Coins, Gear, Crosshair, and InteractPrompt stay hidden throughout Steps 1-4
        // (even during the 0.15-0.20s transition gaps between steps).
        bool inGuardianModalFlow = (currentFlowStep == FlowStep.Intro ||
                                    currentFlowStep == FlowStep.InfoCards ||
                                    currentFlowStep == FlowStep.Quiz ||
                                    currentFlowStep == FlowStep.Guardian) ||
                                   NPCInteraction.IsAnyGuardianFlowRunning;

        if (inGuardianModalFlow)
        {
            return false;
        }

        if (AreModalsOpen && !tabTipActive)
        {
            return false;
        }

        // Cinematic / Dialogue / Pause / Settings overlay check: Gear, Coins, Crosshair, and InteractPrompt stay consistently hidden
        bool isCinematicOrDialogue = (HudVisibilityService.Instance != null && HudVisibilityService.Instance.IsHUDHidden) ||
                                     (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue) ||
                                     LitterbugQuestManager.IsDialogueOpen ||
                                     PauseMenuController.IsPaused ||
                                     SettingsPanelUI.IsAnySettingsOpen ||
                                     (IsCityScavengerOverlayOpenHook != null && IsCityScavengerOverlayOpenHook()) ||
                                     activeOverlays.Count > 0;

        if (isCinematicOrDialogue)
        {
            if (element == UIElement.Gear || element == UIElement.Coins || element == UIElement.Crosshair || element == UIElement.InteractPrompt)
            {
                return false;
            }
        }

        switch (element)
        {
            case UIElement.Gear:
            case UIElement.Coins:
            case UIElement.Crosshair:
            case UIElement.InteractPrompt:
                return true;

            case UIElement.QuestPanel:
                if (IsCityScene())
                {
                    if (IsCityScavengerOverlayOpenHook != null && IsCityScavengerOverlayOpenHook())
                    {
                        return false;
                    }
                    bool cityQuestActive = (currentPhase == GamePhase.QuestActive || currentPhase == GamePhase.QuestComplete) ||
                                           (IsCityScavengerActiveHook != null && IsCityScavengerActiveHook());
                    if (!cityQuestActive && LitterbugQuestManager.Instance != null)
                    {
                        var qState = LitterbugQuestManager.Instance.QuestState;
                        if (qState == LitterbugQuestState.Active || qState == LitterbugQuestState.ReadyToTurnIn || qState == LitterbugQuestState.Completed)
                        {
                            cityQuestActive = true;
                        }
                    }
                    return cityQuestActive && !AreModalsOpen && !isQuestPanelUserHidden;
                }
                else
                {
                    // Quest tab stays hidden during Steps 0-4 and appears ONLY in Step 5 (TabTutorial) and Step 6 (FreePlay)
                    bool stepAllowsQuest = (currentFlowStep == FlowStep.TabTutorial || currentFlowStep == FlowStep.FreePlay) &&
                                           (currentPhase == GamePhase.QuestActive);
                    bool profileAllows = stepAllowsQuest && (!AreModalsOpen || tabTipActive);
                    return profileAllows && !isQuestPanelUserHidden;
                }

            case UIElement.TutorialCard:
                if (tabTipActive) return true;
                return currentFlowStep == FlowStep.Tutorial || currentPhase == GamePhase.Tutorial;

            case UIElement.TabTipCard:
                return tabTipActive && elementOwnerRequestedStates[element];

            case UIElement.TipCards:
                return currentFlowStep == FlowStep.TabTutorial || currentFlowStep == FlowStep.FreePlay;

            default:
                return true;
        }
    }

    /// <summary>
    /// Checks if TIP cards are permitted in current phase.
    /// </summary>
    public bool AreTipCardsAllowed()
    {
        if (modalsOpen) return false;
        return currentPhase != GamePhase.Tutorial;
    }

    /// <summary>
    /// Special rule: TAB tip is allowed only when QuestPanel is active (QuestActive phase).
    /// </summary>
    public bool IsTabTipAllowed()
    {
        if (modalsOpen) return false;
        return currentPhase == GamePhase.QuestActive && questPanelGroup != null && questPanelGroup.alpha > 0.5f && questPanelGroup.gameObject.activeSelf;
    }

    private void AnimateQuestPanel(CanvasGroup cg, bool visible, float duration)
    {
        if (cg == null) return;
        var rt = cg.GetComponent<RectTransform>();
        if (rt == null)
        {
            SnapElement(cg, visible);
            return;
        }

        questPanelBasePos = SharedQuestPanelHomePos;
        hasQuestPanelBasePos = true;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);

        if (!cg.gameObject.activeSelf) cg.gameObject.SetActive(true);

        if (questPanelSlideRoutine != null)
        {
            StopCoroutine(questPanelSlideRoutine);
            questPanelSlideRoutine = null;
        }

        if (duration <= 0.01f || !Application.isPlaying)
        {
            cg.alpha = visible ? 1f : 0f;
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
            rt.localScale = Vector3.one;
            rt.anchoredPosition = questPanelBasePos;
            if (!visible && currentPhase != GamePhase.QuestActive) cg.gameObject.SetActive(false);
            return;
        }

        questPanelSlideRoutine = StartCoroutine(QuestPanelSlideRoutine(cg, rt, visible, duration));
    }

    private IEnumerator QuestPanelSlideRoutine(CanvasGroup cg, RectTransform rt, bool visible, float duration)
    {
        float startAlpha = cg.alpha;
        float targetAlpha = visible ? 1f : 0f;
        Vector2 targetPos = questPanelBasePos;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 2.5f);
            cg.alpha = Mathf.Lerp(startAlpha, targetAlpha, ease);
            rt.anchoredPosition = targetPos;
            rt.localScale = Vector3.one;
            yield return null;
        }

        cg.alpha = targetAlpha;
        cg.interactable = visible;
        cg.blocksRaycasts = visible;
        rt.anchoredPosition = targetPos;
        rt.localScale = Vector3.one;

        if (!visible && currentPhase != GamePhase.QuestActive)
        {
            cg.gameObject.SetActive(false);
        }

        questPanelSlideRoutine = null;
    }

    /// <summary>
    /// Applies profile visibility across all elements with optional duration.
    /// </summary>
    public void ApplyCurrentProfile(float duration = 0.20f)
    {
        ResolveReferences();

        foreach (var kvp in elementGroups)
        {
            var element = kvp.Key;
            var cg = kvp.Value;
            if (cg == null) continue;

            bool expected = GetExpectedVisibility(element);

            // TutorialCard is driven by TutorialController state machine; don't force active if currently inactive in tutorial
            if (element == UIElement.TutorialCard && expected && !cg.gameObject.activeSelf)
            {
                continue;
            }

            if (element == UIElement.QuestPanel)
            {
                AnimateQuestPanel(cg, expected, duration);
                continue;
            }

            var animator = cg.GetComponent<UIAnimator>();
            if (animator != null && duration > 0.01f)
            {
                if (expected)
                {
                    animator.Fade(1f, duration);
                }
                else
                {
                    animator.Fade(0f, duration);
                }
            }
            else
            {
                SnapElement(cg, expected);
            }
        }
    }

    private void SnapElement(CanvasGroup cg, bool visible)
    {
        if (cg == null) return;
        if (visible && !cg.gameObject.activeSelf) cg.gameObject.SetActive(true);
        cg.alpha = visible ? 1f : 0f;
        cg.interactable = visible;
        cg.blocksRaycasts = visible;
        if (cg.gameObject.name == "QuestPanel" || cg.gameObject.name == "ChecklistPanel")
        {
            var rt = cg.GetComponent<RectTransform>();
            if (rt != null)
            {
                if (!hasQuestPanelBasePos)
                {
                    questPanelBasePos = SharedQuestPanelHomePos;
                    hasQuestPanelBasePos = true;
                }
                rt.anchoredPosition = visible ? questPanelBasePos : (questPanelBasePos + new Vector2(40f, 0f));
                rt.localScale = Vector3.one;
            }
            if (!visible && currentPhase != GamePhase.QuestActive) cg.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// ProfileValidator running every 0.1s.
    /// Only corrects a mismatch after it has persisted for 0.5s with no animation running.
    /// If an element is still mismatched after a correction, DO NOT fix it again; log once CONFLICT.
    /// </summary>
    private IEnumerator ProfileValidatorLoop()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(0.10f);

            if (!watchdogEnabled) continue;

            if (IsCityScene())
            {
                bool scavengerActive = IsCityScavengerActiveHook != null && IsCityScavengerActiveHook();
                if (scavengerActive)
                {
                    if (currentPhase != GamePhase.QuestActive)
                    {
                        SetPhase(GamePhase.QuestActive, "Polling: CityScavenger is Active");
                    }
                }
                else if (LitterbugQuestManager.Instance != null)
                {
                    var qState = LitterbugQuestManager.Instance.QuestState;
                    if ((qState == LitterbugQuestState.Active || qState == LitterbugQuestState.ReadyToTurnIn) && currentPhase != GamePhase.QuestActive)
                    {
                        SetPhase(GamePhase.QuestActive, "Polling: City LitterbugQuestState is Active");
                    }
                    else if (qState == LitterbugQuestState.Completed && currentPhase != GamePhase.QuestComplete)
                    {
                        SetPhase(GamePhase.QuestComplete, "Polling: City LitterbugQuestState is Completed");
                    }
                    else if (qState == LitterbugQuestState.Available && currentPhase != GamePhase.FreePlay && activeOverlays.Count == 0)
                    {
                        SetPhase(GamePhase.FreePlay, "Polling: City LitterbugQuestState is Available");
                    }
                }
            }
            else
            {
                // Poll REAL quest state fallback ONLY when not in Steps 1-4
                if (NPCInteraction.IsAnyQuestActiveSignal && !NPCInteraction.IsAnyGuardianFlowRunning)
                {
                    if (currentPhase != GamePhase.QuestActive && (currentFlowStep == FlowStep.TabTutorial || currentFlowStep == FlowStep.FreePlay))
                    {
                        SetPhase(GamePhase.QuestActive, "Polling: NPCInteraction.IsAnyQuestActiveSignal == true");
                    }
                }
            }

            // Check if any modal is genuinely open and visible according to ModalManager
            if (ModalManager.Instance != null)
            {
                modalsOpen = ModalManager.Instance.IsAnyModalActuallyVisible();
            }

            // 0.5s Leak Watchdog: if no modal is open and overlay set is empty for 0.5s, detect and force-restore any leaked hide reason or stuck flowStep
            bool anyDialogueActive = (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue) ||
                                     LitterbugQuestManager.IsDialogueOpen ||
                                     (IsCityScavengerOverlayOpenHook != null && IsCityScavengerOverlayOpenHook()) ||
                                     NPCInteraction.IsAnyGuardianFlowRunning;

            if (!modalsOpen && activeOverlays.Count == 0 && !anyDialogueActive)
            {
                noOverlayOrModalDuration += 0.10f;
                if (noOverlayOrModalDuration >= 0.50f)
                {
                    if (currentFlowStep == FlowStep.ExplorerTravel)
                    {
                        Debug.LogWarning($"LEAK: FlowStep.ExplorerTravel stuck with empty overlay set owner={LastWriter}");
                        currentFlowStep = FlowStep.FreePlay;
                        currentPhase = EvaluateLivePhase("LeakWatchdog");
                        ApplyCurrentProfile(0f);
                    }
                    if (HudVisibilityService.Instance != null && HudVisibilityService.Instance.IsHUDHidden)
                    {
                        foreach (var leakedReason in new List<string>(HudVisibilityService.Instance.ActiveReasons))
                        {
                            Debug.LogWarning($"LEAK: {leakedReason} owner=HudVisibilityService");
                        }
                        HudVisibilityService.Instance.ClearAllReasons(0f);
                    }
                }
            }
            else
            {
                noOverlayOrModalDuration = 0f;
            }

            foreach (var kvp in elementGroups)
            {
                var element = kvp.Key;
                var cg = kvp.Value;
                if (cg == null) continue;

                if (IsElementOwned(element)) continue;
                if (element == UIElement.TutorialCard && (IsElementOwned(UIElement.TutorialCard) || IsElementOwned(UIElement.TabTipCard))) continue;

                var animator = cg.GetComponent<UIAnimator>();
                if (animator != null && animator.IsAnimating)
                {
                    mismatchStartTimes.Remove(element);
                    continue; // Never fight active animation
                }

                if (element == UIElement.QuestPanel)
                {
                    if (questPanelSlideRoutine != null)
                    {
                        mismatchStartTimes.Remove(element);
                        continue; // Never fight active slide
                    }
                    if (LitterbugQuestManager.Instance != null && LitterbugQuestManager.Instance.IsPanelAnimating)
                    {
                        mismatchStartTimes.Remove(element);
                        continue; // Never fight active City panel animation
                    }
                }

                bool expected = GetExpectedVisibility(element);
                bool actualVisible = cg.alpha > 0.5f && cg.gameObject.activeInHierarchy;

                // Special allowance for TutorialCard when step is exiting/gap
                if (element == UIElement.TutorialCard && expected && !actualVisible)
                {
                    continue; // Controlled by TutorialController
                }

                if (actualVisible == expected)
                {
                    mismatchStartTimes.Remove(element);
                    correctedElements.Remove(element);
                    loggedConflictElements.Remove(element);
                }
                else
                {
                    if (!mismatchStartTimes.ContainsKey(element))
                    {
                        mismatchStartTimes[element] = Time.unscaledTime;
                    }
                    else if (Time.unscaledTime - mismatchStartTimes[element] >= 0.5f)
                    {
                        if (!correctedElements.Contains(element))
                        {
                            correctedElements.Add(element);
                            if (expected && !actualVisible)
                            {
                                Debug.LogWarning($"LEAK: {element} hidden for 0.5s owner={LastWriter}");
                            }
                        }
                        else if (!loggedConflictElements.Contains(element))
                        {
                            loggedConflictElements.Add(element);
                            Debug.LogWarning($"[UIOrchestrator ProfileValidator] CONFLICT: {element} last written by '{LastWriter}'");
                        }
                        SnapElement(cg, expected);
                    }
                }
            }
        }
    }
}
