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
    public static SequentialWASDTutorial Instance { get; private set; }

    public enum TutorialState { None, Move, Jump, Sprint, FreeMouse, All }
    public static TutorialState AllowedState { get; set; } = TutorialState.All;

    public static bool IsTutorialCompleted => (Instance != null ? Instance.isActionTipsCompleted : true);
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

    private StarterAssets.StarterAssetsInputs playerInputs;
    private bool isCompleted = false;
    private bool isActionTipsCompleted = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        AllowedState = TutorialState.All;
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

        if (SettingsManager.Instance != null && !SettingsManager.Instance.TutorialHints)
        {
            ApplyTutorialHintsSetting(false);
            return;
        }

        StartCoroutine(RunTutorialSequence());
    }

    private bool wasSkippedBySettings = false;

    public void ApplyTutorialHintsSetting(bool enabled)
    {
        if (!enabled)
        {
            if (!isCompleted) wasSkippedBySettings = true;
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
        }
        else if (wasSkippedBySettings)
        {
            wasSkippedBySettings = false;
            isActionTipsCompleted = false;
            isCompleted = false;
            AllowedState = TutorialState.All;
            StopAllCoroutines();
            StartCoroutine(RunTutorialSequence());
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

        // -------------------------------------------------------------
        // STEP 1 OF 6: MOVE (W A S D)
        // -------------------------------------------------------------
        yield return StartCoroutine(RunMoveStep());

        // -------------------------------------------------------------
        // STEP 2 OF 6: JUMP (SPACE)
        // -------------------------------------------------------------
        yield return StartCoroutine(RunJumpStep());

        // -------------------------------------------------------------
        // STEP 3 OF 6: SPRINT (SHIFT)
        // -------------------------------------------------------------
        yield return StartCoroutine(RunSprintStep());

        // -------------------------------------------------------------
        // STEP 4 OF 6: FREE MOUSE (CTRL)
        // -------------------------------------------------------------
        yield return StartCoroutine(RunCtrlStep());

        // -------------------------------------------------------------
        // STEP 5 OF 6: OPEN THE MAP (M / CLICK MINIMAP)
        // -------------------------------------------------------------
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

        // -------------------------------------------------------------
        // STEP 6 OF 6: TALK TO NPC (Follow arrow, ends when dialogue begins)
        // -------------------------------------------------------------
        yield return StartCoroutine(RunTalkStep());

        // -------------------------------------------------------------
        // ALL 6 STEPS COMPLETED
        // -------------------------------------------------------------
        isCompleted = true;
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
        yield return StartCoroutine(RunMapStep());

        isActionTipsCompleted = true;
        AllowedState = TutorialState.All;
        MinimapController.IsMapTutorialUnlocked = true;

        var npc = FindFirstObjectByType<NPCInteraction>();
        if (npc != null)
        {
            npc.OnTutorialUnlocked();
        }

        yield return StartCoroutine(RunTalkStep());

        isCompleted = true;
        OnTutorialCompletedEvent?.Invoke();
    }

    private IEnumerator RunMoveStep()
    {
        if (tipCardUI == null || tipsData == null) yield break;

        tipCardUI.ShowTip(tipsData.moveTip, 1, 6);

        float moveTimer = 0f;
        float reqDuration = tipsData.moveTip.requiredDuration > 0f ? tipsData.moveTip.requiredDuration : 1.0f;
        bool isDone = false;

        while (!isDone)
        {
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

        bool isDone = false;
        float timer = 0f;

        while (!isDone)
        {
            bool jumpPressed = (playerInputs != null && playerInputs.jump) || Input.GetKey(KeyCode.Space);
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

        // If player already completed sprint previously, complete after a brief display
        if (sprintCtrl != null && sprintCtrl.HasCompletedSprintStep)
        {
            yield return new WaitForSecondsRealtime(0.4f);
        }
        else
        {
            float sprintTimer = 0f;
            float reqTime = tipsData.sprintTip.requiredDuration > 0f ? tipsData.sprintTip.requiredDuration : 1.5f;
            bool isDone = false;

            while (!isDone)
            {
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

        // If player already completed CTRL hold previously, complete after a brief display
        if (ctrl != null && ctrl.HasCompletedHoldStep)
        {
            yield return new WaitForSecondsRealtime(0.4f);
        }
        else
        {
            float ctrlHoldTimer = 0f;
            float reqTime = tipsData.ctrlTip.requiredDuration > 0f ? tipsData.ctrlTip.requiredDuration : 1.0f;
            bool isDone = false;

            while (!isDone)
            {
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
        if (IntroSequenceController.Instance != null)
        {
            IntroSequenceController.Instance.SetCursorState(true);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (playerInputs != null)
            {
                playerInputs.cursorLocked = false;
                playerInputs.cursorInputForLook = false;
            }
            if (CenterScreenCursor.Instance != null)
                CenterScreenCursor.Instance.SetVisible(false);
        }

        // Blur game world, add soft dark tint, raise minimap, show glow ring and bobbing arrow
        if (mapCtrl != null)
        {
            mapCtrl.SetTutorialHighlightActive(true);
        }

        bool isDone = false;
        while (!isDone)
        {
            bool mHeld = Input.GetKey(KeyCode.M);
            tipCardUI.SetKeyHeld(mHeld);

            // Keep cursor unlocked during the step unless another modal takes over
            if (IntroSequenceController.Instance == null ||
                IntroSequenceController.Instance.CurrentState == IntroSequenceController.IntroState.Gameplay)
            {
                if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                if (playerInputs != null)
                {
                    playerInputs.cursorLocked = false;
                    playerInputs.cursorInputForLook = false;
                }
            }

            if (mapCtrl != null && mapCtrl.HasCompletedMapCycle && !mapCtrl.IsFullMapOpen)
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
        bool holdingCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
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
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                if (playerInputs != null)
                {
                    playerInputs.cursorLocked = true;
                    playerInputs.cursorInputForLook = true;
                }
                if (CenterScreenCursor.Instance != null)
                    CenterScreenCursor.Instance.SetVisible(true);
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