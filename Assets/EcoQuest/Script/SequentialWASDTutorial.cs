using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Controls the initial 4-step EcoQuest tutorial:
/// 1. MOVE (W A S D cluster): "Move Around!" / "Press W A S D to walk."
/// 2. JUMP (SPACE key): "Jump!" / "Press SPACE to jump over things."
/// 3. SPRINT (SHIFT key): "Run Faster!" / "Hold SHIFT while you walk to run super fast." (x1.6 speed)
/// 4. FREE MOUSE (CTRL key): "Use Your Mouse" / "Hold CTRL to show your mouse. Let go to look around again."
/// All steps use the pop-up card style with auto-renumbered "STEP X OF 4", idle pulse,
/// key sinking/tinting, green flash celebration, and complete instantly with cheer if already done.
/// </summary>
public class SequentialWASDTutorial : MonoBehaviour
{
    private static SequentialWASDTutorial _instance;
    private static int _instanceEpoch;
    public static SequentialWASDTutorial Instance
    {
        get => SceneSingletonLookup.GetOrFind(ref _instance, ref _instanceEpoch);
        private set
        {
            _instance = value;
            if (value == null) _instanceEpoch = 0;
        }
    }

    public enum TutorialState { None, Move, Jump, Sprint, FreeMouse, All }
    public static TutorialState AllowedState { get; set; } = TutorialState.All;

    public static bool IsTutorialCompleted => (Instance != null ? Instance.isActionTipsCompleted : true);
    public static bool IsTutorialCursorUnlocked { get; private set; }
    public bool IsCompleted => isActionTipsCompleted;
    public bool IsFullTutorialCompleted => isCompleted;

    [Header("Title Intro Setup")]
    public CanvasGroup titleGroup;
    public float titleHoldDuration = 0.8f;

    [Header("Control Tips Integration")]
    public ControlTipsData tipsData;
    public ControlTipCardUI tipCardUI;

    [Header("Legacy UI References (Disabled automatically)")]
    public GameObject mainTutorialParent;
    public CanvasGroup wKeyGroup;
    public CanvasGroup aKeyGroup;
    public CanvasGroup sKeyGroup;
    public CanvasGroup dKeyGroup;
    public CanvasGroup spaceKeyGroup;
    public TMP_Text tutorialText;
    public CanvasGroup textCanvasGroup;
    public GameObject npcExclamation;

    public event System.Action OnTutorialCompletedEvent;
    public static event System.Action OnStep6Reached;

    private StarterAssets.StarterAssetsInputs playerInputs;
    private bool isCompleted = false;
    private bool isActionTipsCompleted = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        AllowedState = TutorialState.All;
        IsTutorialCursorUnlocked = false;
        OnStep6Reached = null;
    }

    public void SetActionTipsCompletedForTest(bool completed)
    {
        isActionTipsCompleted = completed;
        ObjectiveMarkerService.Instance?.RefreshTargetFromState();
    }

    private void NotifyStep6Unlocked()
    {
        OnStep6Reached?.Invoke();
        ObjectiveMarkerService.Instance?.RefreshTargetFromState();
    }

    private static bool HasPersistedTutorialCompletion()
    {
        return PlayerPrefs.GetInt(EcoQuestSaveKeys.TabTipDone, 0) == 1 ||
               PlayerPrefs.GetInt(EcoQuestSaveKeys.SequentialWASD, 0) == 1;
    }

    private void ApplyCompletedFlags(bool persistPref)
    {
        isActionTipsCompleted = true;
        isCompleted = true;
        AllowedState = TutorialState.All;
        MinimapController.IsMapTutorialUnlocked = true;
        if (persistPref && PlayerPrefs.GetInt(EcoQuestSaveKeys.SequentialWASD, 0) != 1)
        {
            PlayerPrefs.SetInt(EcoQuestSaveKeys.SequentialWASD, 1);
            PlayerPrefs.Save();
        }
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        AllowedState = TutorialState.All;
        if (HasPersistedTutorialCompletion())
        {
            ApplyCompletedFlags(persistPref: false);
        }
    }

    private void OnDisable()
    {
        IsTutorialCursorUnlocked = false;
    }

    private void OnDestroy()
    {
        IsTutorialCursorUnlocked = false;
        if (_instance == this) _instance = null;
    }

    private void Start()
    {
        // 1. Cleanly disable old legacy UI keys so they never clutter the screen
        // NOTE: NEVER deactivate WASDTutorial itself!
        if (mainTutorialParent != null && mainTutorialParent != gameObject)
        {
            mainTutorialParent.SetActive(false);
        }
        if (wKeyGroup != null) wKeyGroup.gameObject.SetActive(false);
        if (aKeyGroup != null) aKeyGroup.gameObject.SetActive(false);
        if (sKeyGroup != null) sKeyGroup.gameObject.SetActive(false);
        if (dKeyGroup != null) dKeyGroup.gameObject.SetActive(false);
        if (spaceKeyGroup != null) spaceKeyGroup.gameObject.SetActive(false);
        if (textCanvasGroup != null) textCanvasGroup.alpha = 0f;
        if (npcExclamation != null) npcExclamation.SetActive(false);

        // Ensure this GameObject and its parent canvas remain active
        gameObject.SetActive(true);
        if (transform.parent != null) transform.parent.gameObject.SetActive(true);

        // 2. Resolve references
        if (playerInputs == null) playerInputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
        if (tipCardUI == null) tipCardUI = FindFirstObjectByType<ControlTipCardUI>(FindObjectsInactive.Include);
        if (tipsData == null)
        {
#if UNITY_EDITOR
            tipsData = UnityEditor.AssetDatabase.LoadAssetAtPath<ControlTipsData>("Assets/EcoQuest/Asset/ControlTipsData.asset");
#endif
        }

        // Enable full movement capability
        AllowedState = TutorialState.All;

        if (HasPersistedTutorialCompletion())
        {
            ApplyCompletedFlags(persistPref: true);
            if (titleGroup != null) titleGroup.alpha = 0f;
            if (tipCardUI != null) tipCardUI.SetImmediateHidden();
            var npc = FindFirstObjectByType<NPCInteraction>();
            if (npc != null) npc.OnTutorialUnlocked();
            NotifyStep6Unlocked();
            return;
        }

        StartCoroutine(RunTutorialSequence());
    }

    private void Update()
    {
#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.F4))
        {
            SkipTutorialForEditor();
            Debug.Log("[SequentialWASDTutorial] F4 pressed: Tutorial skipped via editor shortcut.");
        }
#endif
    }

#if UNITY_EDITOR
    private void SkipTutorialForEditor()
    {
        StopAllCoroutines();
        MinimapController.IsMapStepActive = false;
        MinimapController.IsMapTutorialUnlocked = true;
        if (MinimapController.Instance != null)
        {
            MinimapController.Instance.SetTutorialHighlightActive(false, immediate: true);
        }
        if (IntroSequenceController.Instance != null && IntroSequenceController.Instance.CurrentState == IntroSequenceController.IntroState.Gameplay)
        {
            IntroSequenceController.Instance.SetCursorState(false);
            IntroSequenceController.Instance.SetPlayerControlsFrozen(false);
        }
        isActionTipsCompleted = true;
        isCompleted = true;
        AllowedState = TutorialState.All;
        if (tipCardUI == null) tipCardUI = FindFirstObjectByType<ControlTipCardUI>(FindObjectsInactive.Include);
        if (tipCardUI != null) tipCardUI.SetImmediateHidden();
        var npc = FindFirstObjectByType<NPCInteraction>();
        if (npc != null) npc.OnTutorialUnlocked();
        NotifyStep6Unlocked();
    }
#endif

    private IEnumerator WaitForModals()
    {
        bool wasModalOpen = false;
        while ((ModalManager.Instance != null && ModalManager.Instance.IsAnyModalOpen) ||
               PauseMenuController.IsPaused ||
               (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue) ||
               (UIOrchestrator.Instance != null && UIOrchestrator.Instance.IsOverlayActive) ||
               (ExplorerGateController.Instance != null && ExplorerGateController.Instance.IsInDialogue))
        {
            wasModalOpen = true;
            if (tipCardUI != null && tipCardUI.gameObject.activeSelf)
            {
                tipCardUI.SetImmediateHidden();
            }
            yield return null;
        }

        if (wasModalOpen && tipCardUI != null && !isCompleted)
        {
            tipCardUI.RestoreVisible();
        }
    }

    private IEnumerator RunTutorialSequence()
    {
        // Short breath / title intro
        if (titleGroup != null && titleGroup.gameObject.activeInHierarchy)
        {
            yield return StartCoroutine(FadeSingleTo(titleGroup, 1f, 0.3f));
            yield return new WaitForSecondsRealtime(titleHoldDuration);
            yield return StartCoroutine(FadeSingleTo(titleGroup, 0f, 0.3f));
            yield return new WaitForSecondsRealtime(0.2f);
        }
        else
        {
            yield return new WaitForSecondsRealtime(0.3f);
        }

        if (tipCardUI == null) tipCardUI = FindFirstObjectByType<ControlTipCardUI>(FindObjectsInactive.Include);
        if (tipCardUI != null) tipCardUI.EnsureBannerActive();

        // -------------------------------------------------------------
        // STEP 1 OF 6: MOVE (W A S D)
        // -------------------------------------------------------------
        yield return StartCoroutine(WaitForModals());
        yield return StartCoroutine(RunMoveStep());

        // -------------------------------------------------------------
        // STEP 2 OF 6: JUMP (SPACE)
        // -------------------------------------------------------------
        yield return StartCoroutine(WaitForModals());
        yield return StartCoroutine(RunJumpStep());

        // -------------------------------------------------------------
        // STEP 3 OF 6: SPRINT (SHIFT)
        // -------------------------------------------------------------
        yield return StartCoroutine(WaitForModals());
        yield return StartCoroutine(RunSprintStep());

        // -------------------------------------------------------------
        // STEP 4 OF 6: FREE MOUSE (CTRL)
        // -------------------------------------------------------------
        yield return StartCoroutine(WaitForModals());
        yield return StartCoroutine(RunCtrlStep());

        // -------------------------------------------------------------
        // STEP 5 OF 6: OPEN THE MAP (M / CLICK MINIMAP)
        // -------------------------------------------------------------
        yield return StartCoroutine(WaitForModals());
        yield return StartCoroutine(RunMapStep());

        // -------------------------------------------------------------
        // MAP STEP COMPLETED -> UNLOCK NPC & OVERHEAD ARROW
        // -------------------------------------------------------------
        isActionTipsCompleted = true;
        AllowedState = TutorialState.All;
        MinimapController.IsMapTutorialUnlocked = true;

        var npc = FindFirstObjectByType<NPCInteraction>();
        if (npc != null)
        {
            npc.OnTutorialUnlocked();
        }

        NotifyStep6Unlocked();

        // -------------------------------------------------------------
        // STEP 6 OF 6: TALK TO NPC (Follow arrow, ends when dialogue begins)
        // -------------------------------------------------------------
        yield return StartCoroutine(WaitForModals());
        yield return StartCoroutine(RunTalkStep());

        // -------------------------------------------------------------
        // ALL 6 STEPS COMPLETED
        // -------------------------------------------------------------
        ApplyCompletedFlags(persistPref: true);
        if (!NPCInteraction.IsAnyGuardianFlowRunning)
        {
            HudVisibilityService.Instance?.ShowToast("Tutorial complete! Talk to the Guardian", 3.0f);
            UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.FreePlay);
        }
        OnTutorialCompletedEvent?.Invoke();
    }

    public void StartFromMapStep()
    {
        StopAllCoroutines();
        StartCoroutine(RunFromMapStepRoutine());
    }

    private IEnumerator RunFromMapStepRoutine()
    {
        if (tipCardUI == null) tipCardUI = FindFirstObjectByType<ControlTipCardUI>(FindObjectsInactive.Include);
        if (tipCardUI != null) tipCardUI.EnsureBannerActive();

        yield return StartCoroutine(WaitForModals());
        yield return StartCoroutine(RunMapStep());

        isActionTipsCompleted = true;
        AllowedState = TutorialState.All;
        MinimapController.IsMapTutorialUnlocked = true;

        var npc = FindFirstObjectByType<NPCInteraction>();
        if (npc != null)
        {
            npc.OnTutorialUnlocked();
        }

        NotifyStep6Unlocked();

        yield return StartCoroutine(WaitForModals());
        yield return StartCoroutine(RunTalkStep());

        ApplyCompletedFlags(persistPref: true);
        OnTutorialCompletedEvent?.Invoke();
    }

    private IEnumerator RunMoveStep()
    {
        if (tipCardUI == null || tipsData == null) yield break;

        tipCardUI.ShowTip(tipsData.moveTip, 1, 6);

        float moveTimer = 0f;
        float displayTimer = 0f;
        float reqDuration = tipsData.moveTip.requiredDuration > 0f ? tipsData.moveTip.requiredDuration : 1.0f;
        bool isDone = false;

        while (!isDone || displayTimer < 1.2f)
        {
            yield return StartCoroutine(WaitForModals());
            displayTimer += Time.unscaledDeltaTime;

            bool isMoving = (playerInputs != null && playerInputs.move.sqrMagnitude > 0.05f) ||
                            Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) ||
                            Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);

            if (isMoving)
            {
                moveTimer += Time.unscaledDeltaTime;
                if (moveTimer >= reqDuration)
                {
                    isDone = true;
                }
            }

            yield return null;
        }

        bool stepFinished = false;
        tipCardUI.CompleteTip(tipsData.moveTip.doneMessage, () => stepFinished = true);
        while (!stepFinished) yield return null;
    }

    private IEnumerator RunJumpStep()
    {
        if (tipCardUI == null || tipsData == null) yield break;

        tipCardUI.ShowTip(tipsData.jumpTip, 2, 6);

        var fpc = FindFirstObjectByType<StarterAssets.FirstPersonController>();
        bool isDone = false;
        float timer = 0f;
        float displayTimer = 0f;

        while (!isDone || displayTimer < 1.2f)
        {
            yield return StartCoroutine(WaitForModals());
            displayTimer += Time.unscaledDeltaTime;

            bool jumpPressed = (playerInputs != null && playerInputs.jump) ||
                               Input.GetKey(KeyCode.Space) ||
                               (fpc != null && !fpc.Grounded);
            tipCardUI.SetKeyHeld(jumpPressed);

            if (jumpPressed)
            {
                isDone = true;
            }

            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        bool stepFinished = false;
        tipCardUI.CompleteTip(tipsData.jumpTip.doneMessage, () => stepFinished = true);
        while (!stepFinished) yield return null;
    }

    private IEnumerator RunSprintStep()
    {
        if (tipCardUI == null || tipsData == null) yield break;

        var sprintCtrl = PlayerSprintController.Instance != null 
            ? PlayerSprintController.Instance 
            : FindFirstObjectByType<PlayerSprintController>();

        tipCardUI.ShowTip(tipsData.sprintTip, 3, 6);

        float sprintTimer = 0f;
        float displayTimer = 0f;
        float reqTime = tipsData.sprintTip.requiredDuration > 0f ? tipsData.sprintTip.requiredDuration : 1.5f;
        bool isDone = false;

        while (!isDone || displayTimer < 1.2f)
        {
            yield return StartCoroutine(WaitForModals());
            displayTimer += Time.unscaledDeltaTime;

            bool isSprinting = (sprintCtrl != null && sprintCtrl.IsSprinting) ||
                               (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

            tipCardUI.SetKeyHeld(isSprinting);

            if (isSprinting && playerInputs != null && playerInputs.move.sqrMagnitude > 0.01f)
            {
                sprintTimer += Time.unscaledDeltaTime;
                if (sprintTimer >= reqTime)
                {
                    isDone = true;
                }
            }
            else
            {
                sprintTimer = Mathf.Max(0f, sprintTimer - Time.unscaledDeltaTime * 0.5f);
            }

            if (sprintCtrl != null && sprintCtrl.HasCompletedSprintStep)
            {
                isDone = true;
            }

            yield return null;
        }

        bool stepFinished = false;
        tipCardUI.CompleteTip(tipsData.sprintTip.doneMessage, () => stepFinished = true);
        while (!stepFinished) yield return null;
    }

    private IEnumerator RunCtrlStep()
    {
        if (tipCardUI == null || tipsData == null) yield break;

        var ctrl = CursorQuickAccessController.Instance != null
            ? CursorQuickAccessController.Instance
            : FindFirstObjectByType<CursorQuickAccessController>();

        tipCardUI.ShowTip(tipsData.ctrlTip, 4, 6);

        float ctrlHoldTimer = 0f;
        float displayTimer = 0f;
        float reqTime = tipsData.ctrlTip.requiredDuration > 0f ? tipsData.ctrlTip.requiredDuration : 1.0f;
        bool isDone = false;

        while (!isDone || displayTimer < 1.2f)
        {
            yield return StartCoroutine(WaitForModals());
            displayTimer += Time.unscaledDeltaTime;

            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            tipCardUI.SetKeyHeld(ctrlHeld);

            if (ctrlHeld)
            {
                ctrlHoldTimer += Time.unscaledDeltaTime;
                if (ctrlHoldTimer >= reqTime)
                {
                    isDone = true;
                }
            }
            else
            {
                ctrlHoldTimer = 0f;
            }

            if (ctrl != null && ctrl.HasCompletedHoldStep)
            {
                isDone = true;
            }

            yield return null;
        }

        bool stepFinished = false;
        tipCardUI.CompleteTip(tipsData.ctrlTip.doneMessage, () => stepFinished = true);
        while (!stepFinished) yield return null;
    }

    private IEnumerator RunMapStep()
    {
        if (tipCardUI == null || tipsData == null) yield break;

        // Respect IntroSequenceController states: wait until in Gameplay state
        while (IntroSequenceController.Instance != null &&
               IntroSequenceController.Instance.CurrentState != IntroSequenceController.IntroState.Gameplay)
        {
            yield return null;
        }

        var mapCtrl = MinimapController.Instance != null
            ? MinimapController.Instance
            : FindFirstObjectByType<MinimapController>();

        if (mapCtrl != null)
        {
            mapCtrl.ResetTutorialCycle();
        }

        MinimapController.IsMapTutorialUnlocked = true;
        MinimapController.IsMapStepActive = true;

        // Ensure mapTip uses the blank key icon if not assigned in asset
        if (tipsData.mapTip == null)
        {
            tipsData.mapTip = new ControlTipsData.TipConfig();
        }
        tipsData.mapTip.stepLabel = "STEP 5 OF 6";
        tipsData.mapTip.title = "Open the Map!";
        tipsData.mapTip.instruction = "Press M or click the minimap.";
        if (string.IsNullOrEmpty(tipsData.mapTip.doneMessage))
        {
            tipsData.mapTip.doneMessage = "Awesome! You can check the map anytime!";
        }
        tipsData.mapTip.keyLabelOverride = "M";
        if (tipsData.mapTip.keyIcon == null && tipsData.ctrlTip != null)
        {
            tipsData.mapTip.keyIcon = tipsData.ctrlTip.keyIcon;
        }

        tipCardUI.ShowTip(tipsData.mapTip, 5, 6);

        // Unlock cursor for the step and freeze camera look so the player can easily click the minimap
        IsTutorialCursorUnlocked = true;
        if (IntroSequenceController.Instance != null)
        {
            IntroSequenceController.Instance.SetCursorState(true);
        }
        else
        {
            CursorState.SetLocked(false, playerInputs);
        }

        // Blur game world, add soft dark tint, raise minimap, show glow ring and bobbing arrow
        if (mapCtrl != null)
        {
            mapCtrl.SetTutorialHighlightActive(true);
        }

        bool isDone = false;
        float displayTimer = 0f;
        while (!isDone || displayTimer < 1.2f)
        {
            yield return StartCoroutine(WaitForModals());
            displayTimer += Time.unscaledDeltaTime;

            bool mHeld = Input.GetKey(KeyCode.M);
            tipCardUI.SetKeyHeld(mHeld);

            // Keep cursor unlocked during the step unless another modal takes over
            if (IntroSequenceController.Instance == null ||
                IntroSequenceController.Instance.CurrentState == IntroSequenceController.IntroState.Gameplay)
            {
                if (Cursor.lockState != CursorLockMode.None || !Cursor.visible || (playerInputs != null && (playerInputs.cursorLocked || playerInputs.cursorInputForLook)))
                {
                    CursorState.SetLocked(false, playerInputs);
                }
            }

            if (mapCtrl == null || (mapCtrl.HasCompletedMapCycle && !mapCtrl.IsFullMapOpen))
            {
                isDone = true;
            }

            yield return null;
        }

        MinimapController.IsMapStepActive = false;

        // Smoothly fade out blur, dark tint, arrow, and glow
        bool fadeOutDone = false;
        if (mapCtrl != null)
        {
            mapCtrl.SetTutorialHighlightActive(false, immediate: false, onComplete: () => fadeOutDone = true);
        }
        else
        {
            fadeOutDone = true;
        }

        // Relock cursor and restore camera look if still in Gameplay
        IsTutorialCursorUnlocked = false;
        bool holdingCtrl = (UnityEngine.InputSystem.Keyboard.current != null &&
            (UnityEngine.InputSystem.Keyboard.current.leftCtrlKey.isPressed || UnityEngine.InputSystem.Keyboard.current.rightCtrlKey.isPressed))
            || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (!holdingCtrl && (IntroSequenceController.Instance == null ||
            IntroSequenceController.Instance.CurrentState == IntroSequenceController.IntroState.Gameplay))
        {
            if (IntroSequenceController.Instance != null)
            {
                IntroSequenceController.Instance.SetCursorState(false);
                IntroSequenceController.Instance.SetPlayerControlsFrozen(false);
            }
            else
            {
                CursorState.SetLocked(true, playerInputs);
            }
        }

        bool stepFinished = false;
        tipCardUI.CompleteTip(tipsData.mapTip.doneMessage, () => stepFinished = true);
        while (!stepFinished || !fadeOutDone) yield return null;
    }

    private IEnumerator RunTalkStep()
    {
        if (tipCardUI == null || tipsData == null) yield break;

        var talkConfig = tipsData.npcTip;
        if (talkConfig == null) yield break;

        tipCardUI.ShowTip(talkConfig, 6, 6);

        NPCInteraction npc = FindFirstObjectByType<NPCInteraction>();
        bool dialogueStarted = false;

        UnityEngine.Events.UnityAction onDialogueStart = () => dialogueStarted = true;
        if (npc != null) npc.OnDialogueStarted.AddListener(onDialogueStart);

        while (!dialogueStarted)
        {
            yield return StartCoroutine(WaitForModals());
            if (npc != null && npc.IsTalking)
            {
                dialogueStarted = true;
                break;
            }
            yield return null;
        }

        if (npc != null) npc.OnDialogueStarted.RemoveListener(onDialogueStart);

        bool cardDismissed = false;
        tipCardUI.DismissTip(() => cardDismissed = true);
        while (!cardDismissed) yield return null;
    }

    private IEnumerator FadeSingleTo(CanvasGroup cg, float targetAlpha, float duration = 0.3f)
    {
        if (cg == null) yield break;
        float startAlpha = cg.alpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            yield return null;
        }
        cg.alpha = targetAlpha;
    }
}