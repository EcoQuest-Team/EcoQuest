using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Cinemachine;

public class NPCInteraction : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject interactPrompt; 
    [SerializeField] private GameObject dialogueBox;    
    [SerializeField] private TextMeshProUGUI dialogueText;
    [SerializeField] private GameObject questCanvas;
    
    [Tooltip("Drag your OverheadCanvas here")]
    [SerializeField] private GameObject overheadMarker; 

    [Header("Dialogue UI Extensions")]
    [SerializeField] private Button acceptMissionButton;
    [SerializeField] private Button notNowButton;
    [SerializeField] private TextMeshProUGUI continueHint;

    [Header("Dialogue Events")]
    public UnityEngine.Events.UnityEvent OnDialogueStarted;
    public UnityEngine.Events.UnityEvent OnDialogueEnded;
    public UnityEngine.Events.UnityEvent OnLastDialogueLine;
    public event System.Action OnDialogueStart;
    public event System.Action OnDialogueEnd;
    public event System.Action OnLastDialogueLineReached;

    [Header("Dialogue Camera & Framing")]
    [Tooltip("Dialogue shot position and framing transform child on NPC")]
    [SerializeField] private Transform dialogueCameraPoint;
    [Tooltip("Camera offset in local space of NPC if camera point is auto-created")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(-0.4f, 1.35f, 2.75f);
    [Tooltip("Look-at height (upper chest) relative to NPC origin")]
    [SerializeField] private float lookAtHeight = 1.25f;
    [Tooltip("Field of View for the dialogue shot (50 - 60)")]
    [Range(40f, 75f)]
    [SerializeField] private float dialogueFOV = 42f;
    [Tooltip("Camera transition blend duration in seconds")]
    [SerializeField] private float transitionTime = 0.35f;

    [Header("Cinematic Dialogue & Letterbox")]
    [SerializeField] private RectTransform topLetterboxBar;
    [SerializeField] private RectTransform bottomLetterboxBar;
    [SerializeField] private NpcDialogueSpeakerAnimator speakerAnimator;

    private Coroutine letterboxRoutine;
    private int savedCameraCullingMask;
    private bool cameraCullingMaskModified = false;
    private GameObject hiddenCarriedItem = null;

    [Header("Trash Management")]
    [SerializeField] private GameObject trashContainer; 

    [Header("Tutorial Settings")]
    [SerializeField] private GameObject tutorialNotification;
    [SerializeField] private TutorialManager tutorialManager;

    [Header("Dialogue Content & Settings")]
    [SerializeField] private string[] dialogueLines;
    [SerializeField] private string repeatOfferLine = "Great work, Guardian of the forest! Would you like to clean up again?";
    
    [SerializeField] private float interactDistance = 3.5f;
    [SerializeField] private float typingSpeed = 0.04f;
    [SerializeField] private float nextLineDelay = 0.5f;

    [Header("Repeat Quest Settings")]
    [Tooltip("If true, repeat quests also run the Post-Test at the end. If false (default), shows a simple completion message instead.")]
    public bool repeatQuestRunsPostTest = false;

    private int currentLineIndex = 0;
    private bool isPlayerNearby = false;
    private bool isTalking = false;
    private bool hasAcceptedQuest = false;
    private bool hasCompletedFirstQuest = false;
    private bool isRepeatOfferDialogue = false;
    private QuestManager[] allQuests;
    private Transform playerTransform;

    private Coroutine typingCoroutine;
    private Coroutine cameraBlendCoroutine;
    private bool isTyping = false;
    private float advanceLockTimer = 0f;
    private bool canContinue = false;

    // Fallback camera lerp state
    private Vector3 originalCamPos;
    private Quaternion originalCamRot;
    private float originalCamFOV;
    private static float globalNpcInteractCooldownUntil = 0f;

    public bool IsTalking => isTalking;
    public bool IsPlayerNearby => isPlayerNearby;
    public bool IsQuestActive => hasAcceptedQuest && !QuestManager.IsQuestComplete;
    public bool HasCompletedFirstQuest => hasCompletedFirstQuest;

    public static event System.Action OnQuestAccepted;

    public static void NotifyAllQuestCompleted()
    {
        NPCInteraction[] npcs = FindObjectsByType<NPCInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (NPCInteraction npc in npcs)
        {
            if (npc != null)
            {
                npc.hasCompletedFirstQuest = true;
                npc.hasAcceptedQuest = false;
            }
        }
        PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestQuestAccepted, 0);
        PlayerPrefs.Save();
        ObjectiveMarkerService.Instance?.RefreshTargetFromState();
    }

    private void Awake()
    {
        EnsureAnimatorSetup();
        EnsureCameraPointSetup();
        EnsureUIReferences();
    }

    private void Start()
    {
        EnsureAnimatorSetup();
        EnsureCameraPointSetup();
        EnsureUIReferences();

        SetLegacyInteractPromptActive(false);
        if (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue)
            DialoguePresenter.Instance.CloseDialogue();

        if (dialogueBox != null && DialoguePresenter.Instance == null) dialogueBox.SetActive(false);
        if (acceptMissionButton != null)
        {
            acceptMissionButton.onClick.RemoveAllListeners();
            acceptMissionButton.onClick.AddListener(AcceptMission);
            acceptMissionButton.gameObject.SetActive(false);
        }
        if (notNowButton != null)
        {
            notNowButton.onClick.RemoveAllListeners();
            notNowButton.onClick.AddListener(DeclineRepeatMission);
            notNowButton.gameObject.SetActive(false);
        }
        SetContinueHintActive(false);
        
        if (questCanvas != null && IntroSequenceController.Instance == null) questCanvas.SetActive(false);
        bool tabTipAlreadyDone = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0) == 1;
        bool questAlreadyAccepted = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestQuestAccepted, 0) == 1;
        if (tabTipAlreadyDone || questAlreadyAccepted)
        {
            hasAcceptedQuest = true;
        PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestQuestAccepted, 1);
        PlayerPrefs.Save();
        OnQuestAccepted?.Invoke();
        ObjectiveMarkerService.Instance?.RefreshTargetFromState();
            IsAnyQuestActive = true;
        }
        if (IntroSequenceController.Instance != null)
        {
            IntroSequenceController.Instance.SetTrashVisible(tabTipAlreadyDone, false, "NPCInteraction.Start");
        }
        else
        {
            TrashItem.AreTrashItemsAllowed = tabTipAlreadyDone;
            if (trashContainer != null) trashContainer.SetActive(tabTipAlreadyDone);
            MinimapIcon.SetTrashIconsActive(tabTipAlreadyDone);
        }

        if (tutorialNotification != null) tutorialNotification.SetActive(true);

        if (GetComponent<NpcWalker>() == null && GetComponent<NPCWaypointPointer>() == null)
        {
            gameObject.AddComponent<NPCWaypointPointer>();
        }

        if (GetComponent<MinimapIcon>() == null)
        {
            MinimapIcon icon = gameObject.AddComponent<MinimapIcon>();
            icon.type = MinimapIcon.IconType.NPC;
        }

        if (FindFirstObjectByType<CenterScreenCursor>() == null)
        {
            gameObject.AddComponent<CenterScreenCursor>();
        }

        if (overheadMarker != null) overheadMarker.SetActive(false);

        allQuests = FindObjectsByType<QuestManager>(FindObjectsSortMode.None);
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            EnsureAnimatorSetup();
            EnsureCameraPointSetup();
        }
        EnsureUIReferences();
    }

    private Transform ResolvePlayerTransform()
    {
        var fpc = FindFirstObjectByType<StarterAssets.FirstPersonController>();
        if (fpc != null) return fpc.transform;

        var cc = FindFirstObjectByType<CharacterController>();
        if (cc != null) return cc.transform;

        GameObject pc = GameObject.Find("PlayerCapsule");
        if (pc != null) return pc.transform;

        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            var childCc = p.GetComponentInChildren<CharacterController>();
            return childCc != null ? childCc.transform : p.transform;
        }

        return null;
    }

    private static bool IsInteractKeyPressed()
    {
        if (Input.GetKeyDown(KeyCode.E)) return true;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame) return true;
#endif
        return false;
    }

    private void Update()
    {
        AnimateOverheadMarker();

        if (playerTransform == null || playerTransform.GetComponent<CharacterController>() == null)
        {
            playerTransform = ResolvePlayerTransform();
        }

        float playerDist = playerTransform != null ? Vector3.Distance(transform.position, playerTransform.position) : 999f;
        // Self-heal: ensure trigger enter/exit missed by CharacterController does not desync isPlayerNearby
        if (isPlayerNearby && playerDist > interactDistance + 0.5f)
        {
            isPlayerNearby = false;
        }
        else if (!isPlayerNearby && playerDist <= interactDistance && !isTalking)
        {
            isPlayerNearby = true;
        }

        // Smoothly rotate NPC to face player when nearby and not in active dialogue
        if (isPlayerNearby && !isTalking && playerTransform != null)
        {
            Vector3 lookTarget = new Vector3(playerTransform.position.x, transform.position.y, playerTransform.position.z);
            if (Vector3.Distance(transform.position, lookTarget) > 0.1f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookTarget - transform.position), Time.deltaTime * 4f);
            }
        }

        if (hasAcceptedQuest && QuestManager.IsQuestComplete)
        {
            hasCompletedFirstQuest = true;
            hasAcceptedQuest = false;
        }

        bool isModalBlocking = QuizUIController.IsAnyQuizOpen || PauseMenuController.IsPaused || (MinimapController.Instance != null && MinimapController.Instance.IsFullMapOpen);
        bool isQuestActive = hasAcceptedQuest && !QuestManager.IsQuestComplete;
        bool isHoldingTrash = DragAndDrop.CurrentlyGrabbedItem != null;

        HandleInteractionPrompt(isQuestActive || isModalBlocking || isHoldingTrash || playerDist > interactDistance || Time.unscaledTime < globalNpcInteractCooldownUntil);

        if (isPlayerNearby && !isTalking && !isQuestActive && !isModalBlocking && !isHoldingTrash && playerDist <= interactDistance && Time.unscaledTime >= globalNpcInteractCooldownUntil && IsInteractKeyPressed())
        {
            if (IsTutorialLocked())
            {
                // Locked until the tutorial tips are completed
                return;
            }
            FindAnyObjectByType<RangerAnimationDriver>()?.PlayInteract();
            StartDialogue();
        }

        // Support advancing dialogue via Click, E, Space, or Return, and declining with Q
        if (isTalking)
        {
            if (advanceLockTimer > 0f)
            {
                advanceLockTimer -= Time.unscaledDeltaTime;
                return;
            }

            bool isLastLine = isRepeatOfferDialogue || (dialogueLines != null && currentLineIndex >= dialogueLines.Length - 1);

            if (isLastLine && !isTyping && Input.GetKeyDown(KeyCode.Q))
            {
                DeclineRepeatMission();
                return;
            }

            if (Input.GetMouseButtonDown(0) || IsInteractKeyPressed() || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                if (isTyping)
                {
                    CompleteLineInstantly();
                }
                else if (isLastLine)
                {
                    if (IsInteractKeyPressed() || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                    {
                        AcceptMission();
                    }
                }
                else if (canContinue)
                {
                    DisplayNextLine();
                }
            }
        }
    }

    /// <summary>
    /// Guarantees UI references for Accept button, Not Now button, and Continue hint are resolved.
    /// </summary>
    public void EnsureUIReferences()
    {
        if (dialogueBox == null)
        {
            GameObject dObj = GameObject.Find("UI_Dialogue");
            if (dObj != null) dialogueBox = dObj;
        }
        if (interactPrompt == null)
        {
            GameObject pObj = GameObject.Find("UI_InteractPrompt");
            if (pObj != null) interactPrompt = pObj;
        }
        if (dialogueText == null && dialogueBox != null)
        {
            dialogueText = dialogueBox.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        if (dialogueBox != null)
        {
            if (acceptMissionButton == null)
            {
                Button[] btns = dialogueBox.GetComponentsInChildren<Button>(true);
                foreach (var b in btns)
                {
                    if (b != null && b.gameObject.name != "NotNowButton")
                    {
                        acceptMissionButton = b;
                        break;
                    }
                }
            }

            if (notNowButton == null && acceptMissionButton != null && acceptMissionButton.transform.parent != null)
            {
                Transform existingNotNow = acceptMissionButton.transform.parent.Find("NotNowButton");
                if (existingNotNow != null)
                {
                    notNowButton = existingNotNow.GetComponent<Button>();
                }
                else if (Application.isPlaying)
                {
                    GameObject clone = Instantiate(acceptMissionButton.gameObject, acceptMissionButton.transform.parent);
                    clone.name = "NotNowButton";
                    notNowButton = clone.GetComponent<Button>();
                    if (notNowButton != null)
                    {
                        notNowButton.onClick.RemoveAllListeners();
                        notNowButton.onClick.AddListener(DeclineRepeatMission);
                    }
                    clone.SetActive(false);
                }
            }

            if (continueHint == null)
            {
                TextMeshProUGUI[] tmps = dialogueBox.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var t in tmps)
                {
                    if (t.gameObject.name.Contains("Continue") || t.gameObject.name.Contains("Hint") || t.text.ToLower().Contains("continue"))
                    {
                        continueHint = t;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Guarantees the NPC has a DialogueCameraPoint child with CinemachineCamera configured.
    /// </summary>
    public void EnsureCameraPointSetup()
    {
        if (dialogueCameraPoint == null)
        {
            Transform found = transform.Find("DialogueCameraPoint");
            if (found != null)
            {
                dialogueCameraPoint = found;
            }
            else
            {
                GameObject camObj = new GameObject("DialogueCameraPoint");
                camObj.transform.SetParent(transform, false);
                camObj.transform.localPosition = cameraOffset;

                Vector3 localLook = new Vector3(0.35f, lookAtHeight, 0f);
                camObj.transform.LookAt(transform.TransformPoint(localLook));
                dialogueCameraPoint = camObj.transform;
            }
        }

        if (dialogueCameraPoint != null)
        {
            CinemachineCamera cmCam = dialogueCameraPoint.GetComponent<CinemachineCamera>();
            if (cmCam == null)
            {
                cmCam = dialogueCameraPoint.gameObject.AddComponent<CinemachineCamera>();
            }
            cmCam.Lens.FieldOfView = dialogueFOV;
            cmCam.Lens.NearClipPlane = 0.03f;
            cmCam.Priority.Value = 0;
        }
    }

    /// <summary>
    /// Guarantees the NPC has an active Animator component with a valid controller assigned.
    /// Snaps the NPC out of T-Pose and into its animated state.
    /// </summary>
    public void EnsureAnimatorSetup()
    {
        Animator anim = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>(true);

        if (anim != null)
        {
            anim.enabled = true;
            anim.applyRootMotion = false;
            if (anim.speed == 0f) anim.speed = 1f;

            var brain = GetComponent<NpcBrain>();
            if (brain != null)
            {
                brain.ApplyGenderController();
            }
            else if (anim.runtimeAnimatorController == null)
            {
#if UNITY_EDITOR
                string n = gameObject.name.ToLowerInvariant();
                bool isFemale = n.Contains("female") || n.Contains("woman") || n.Contains("girl");
                string overridePath = isFemale
                    ? "Assets/EcoQuest/Asset/NPC_Female_Override.overrideController"
                    : "Assets/EcoQuest/Asset/NPC_Male_Override.overrideController";
                RuntimeAnimatorController overrideCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(overridePath);
                if (overrideCtrl != null)
                {
                    anim.runtimeAnimatorController = overrideCtrl;
                }
                else
                {
                    RuntimeAnimatorController guardianCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/EcoQuest/Asset/GuardianAnimatorController.controller");
                    if (guardianCtrl != null)
                    {
                        anim.runtimeAnimatorController = guardianCtrl;
                    }
                }
#endif
            }

            // Force Rebind to snap character out of T-Pose into animated pose
            if (anim.runtimeAnimatorController != null && Application.isPlaying && anim.gameObject.activeInHierarchy && anim.enabled)
            {
                anim.Rebind();
                anim.Update(0f);
            }
        }
    }

    private TextMeshProUGUI promptText;
    private string defaultPromptText = "Talk";

    public bool IsTutorialLocked()
    {
        if (SequentialWASDTutorial.Instance != null)
        {
            return !SequentialWASDTutorial.Instance.IsCompleted;
        }
        var tut = FindFirstObjectByType<SequentialWASDTutorial>();
        if (tut != null)
        {
            return !tut.IsCompleted;
        }
        return false; // If tutorial does not exist in scene, never stay locked!
    }

    public void OnTutorialUnlocked()
    {
        if (promptText != null)
        {
            promptText.text = defaultPromptText;
        }
    }

    private void AnimateOverheadMarker()
    {
        if (overheadMarker == null) return;

        float distToPlayer = (playerTransform != null)
            ? Vector3.Distance(transform.position, playerTransform.position)
            : 999f;

        // Hide marker during dialogue, when quest is actively running, when travel is unlocked, or within interactDistance + 1.0m
        bool isQuestActive = hasAcceptedQuest && !QuestManager.IsQuestComplete;
        bool isCityTravelUnlocked = TravelGate.CanTravelToCity;
        bool isSuppressedByService = ObjectiveMarkerService.Instance != null && ObjectiveMarkerService.Instance.CurrentTarget != transform;
        bool shouldHide = isTalking || isQuestActive || isCityTravelUnlocked || isSuppressedByService || (distToPlayer <= (interactDistance + 1.0f));

        if (shouldHide)
        {
            if (overheadMarker.activeSelf)
                overheadMarker.SetActive(false);
            return;
        }

        if (!overheadMarker.activeSelf)
            overheadMarker.SetActive(true);

        float bob = Mathf.Sin(Time.time * 2.8f) * 0.07f;
        overheadMarker.transform.localPosition = new Vector3(0f, 2.3f + bob, 0f);

        // Scale with distance: 0.60 to 1.0, capped close
        float distFactor = Mathf.Clamp01((distToPlayer - 4f) / 16f);
        float distScale = Mathf.Lerp(0.60f, 1.0f, distFactor);
        overheadMarker.transform.localScale = Vector3.one * (0.24f * distScale);

        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 toCam = overheadMarker.transform.position - cam.transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.001f)
                overheadMarker.transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        }
    }

    private void SetLegacyInteractPromptActive(bool active)
    {
        if (interactPrompt == null) return;
        if (interactPrompt.GetComponent<WorldInteractionPrompt>() != null) return;
        if (interactPrompt.activeSelf != active) interactPrompt.SetActive(active);
    }

    private void SetContinueHintActive(bool active)
    {
        if (continueHint == null) return;
        KeyCapPrompt kcp = continueHint.GetComponentInParent<KeyCapPrompt>(true);
        if (kcp != null && kcp.transform.parent != null && kcp.transform.parent.name == "ContinuePromptRoot")
        {
            kcp.transform.parent.gameObject.SetActive(active);
        }
        else if (kcp != null)
        {
            kcp.gameObject.SetActive(active);
        }
        else
        {
            continueHint.gameObject.SetActive(active);
        }
    }

    private void HandleInteractionPrompt(bool isBlocked)
    {
        float playerDist = playerTransform != null ? Vector3.Distance(transform.position, playerTransform.position) : 999f;
        bool isHoldingTrash = DragAndDrop.CurrentlyGrabbedItem != null;
        bool shouldShowPrompt = isPlayerNearby && !isTalking && !isBlocked && !isHoldingTrash && playerDist <= interactDistance;
        if (shouldShowPrompt)
        {
            string act = IsTutorialLocked() ? "Tips locked" : "Talk";
            WorldInteractionPrompt.Instance?.ShowPrompt(transform, "Guardian", act, "E");
        }
        else if (WorldInteractionPrompt.Instance != null && WorldInteractionPrompt.Instance.ActiveTarget == transform)
        {
            WorldInteractionPrompt.Instance.HidePrompt();
        }

        if (interactPrompt == null || interactPrompt.GetComponent<WorldInteractionPrompt>() != null) return;
        
        if (interactPrompt.activeSelf != shouldShowPrompt)
        {
            interactPrompt.SetActive(shouldShowPrompt);
        }

        if (shouldShowPrompt)
        {
            if (promptText == null) promptText = interactPrompt.GetComponentInChildren<TextMeshProUGUI>(true);
            if (promptText != null)
            {
                if (string.IsNullOrEmpty(defaultPromptText) || defaultPromptText == "Finish the tips first!")
                {
                    defaultPromptText = "Talk";
                }
                promptText.text = IsTutorialLocked() ? "Finish the tips first!" : defaultPromptText;
            }
        }
    }

    public void StartDialogue()
    {
        if (Time.unscaledTime < globalNpcInteractCooldownUntil) return;
        EnsureUIReferences();

        isTalking = true;
        isRepeatOfferDialogue = hasCompletedFirstQuest;
        currentLineIndex = 0;
        advanceLockTimer = 0.5f;

        WorldInteractionPrompt.Instance?.SetBlocked(true);
        SetLegacyInteractPromptActive(false);
        if (DialoguePresenter.Instance != null)
        {
            DialoguePresenter.Instance.EnsureActiveHierarchy();
        }
        else if (dialogueBox != null)
        {
            dialogueBox.SetActive(true);
        }
        StartCoroutine(DialogueFailSafeRoutine());
        bool hasIntroController = (IntroSequenceController.Instance != null || FindFirstObjectByType<IntroSequenceController>() != null);
        if (questCanvas != null && !hasIntroController && !isRepeatOfferDialogue) questCanvas.SetActive(true);
        
        if (tutorialNotification != null) tutorialNotification.SetActive(false);

        // 1. Freeze player movement and look input, unlock cursor, hide crosshair
        FreezePlayerControls();

        // 2. Smoothly transition camera to dialogue shot
        ActivateDialogueCamera();

        // 3. Smoothly rotate NPC to face player on Y axis
        StartCoroutine(SmoothTurnToPlayer());

        // 4. Play talk animation
        Animator anim = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.SetBool("IsTalking", true);
        }

        if (isRepeatOfferDialogue)
        {
            IntroSequenceController intro = IntroSequenceController.Instance != null
                ? IntroSequenceController.Instance
                : FindFirstObjectByType<IntroSequenceController>();
            if (intro != null)
            {
                intro.BeginRepeatDialogue();
            }
        }

        OnDialogueStarted?.Invoke();
        OnDialogueStart?.Invoke();

        if (DialoguePresenter.Instance != null)
        {
            int total = QuestManager.GetTotalRequiredWaste();
            int done = QuestManager.GetTotalSortedWaste();
            int reward = 100;

            List<string> linesToShow;
            bool showChoices = false;
            string acceptLabel = isRepeatOfferDialogue ? "Yes, let's go!" : "Accept Quest";
            string declineLabel = "Not now";
            Action onAcceptAction = null;
            Action onDeclineAction = null;

            if (hasAcceptedQuest && !QuestManager.IsQuestComplete)
            {
                linesToShow = DialogueData.GetForestInProgress(done, total);
            }
            else if (hasAcceptedQuest && QuestManager.IsQuestComplete)
            {
                linesToShow = DialogueData.GetForestComplete(reward);
                onAcceptAction = () => EndDialogue();
            }
            else
            {
                // Exclusive GuardianFlowRoutine: [intro dialogue] -> out -> [info card(s)] -> out -> [quiz] -> out -> [quest dialogue] -> out -> quest starts
                DialoguePresenter.RunCoroutine(GuardianFlowRoutine(total));
                return;
            }

            DialoguePresenter.Instance.ShowDialogue(
                speakerName: "Guardian",
                lines: linesToShow,
                showChoicesAtEnd: showChoices,
                acceptLabel: acceptLabel,
                declineLabel: declineLabel,
                onAccept: onAcceptAction,
                onDecline: onDeclineAction,
                onComplete: () => {
                    EndDialogue();
                }
            );
            return;
        }

        UpdateDialogueText();
    }

    public bool IsRunningGuardianFlow { get; private set; } = false;
    public static event Action<string> OnFlowCompleted;
    public static string CurrentFlowStep { get; private set; } = "Idle";
    public static bool IsAnyQuestActive { get; set; } = false;
    public static bool IsAnyGuardianFlowRunning { get; private set; } = false;
    public static bool IsAnyQuestActiveSignal => IsAnyQuestActive && !QuestManager.IsQuestComplete;

    private Coroutine flowWatchdogRoutine;
    private string activeGuardianFlowToken = null;
    private bool flowEndExecuted = false;

    private IEnumerator GuardianFlowRoutine(int total)
    {
        if (IsRunningGuardianFlow || IsAnyGuardianFlowRunning)
        {
            Debug.LogWarning("[FlowState] Blocked duplicate GuardianFlowRoutine start because a flow is already active.");
            yield break;
        }

        string flowToken = System.Guid.NewGuid().ToString("N");
        activeGuardianFlowToken = flowToken;
        IsRunningGuardianFlow = true;
        IsAnyGuardianFlowRunning = true;
        flowEndExecuted = false;
        bool accepted = false;

        // Ensure trash and quest tab remain strictly hidden throughout Steps 1-4
        IntroSequenceController.Instance?.SetTrashVisible(false, false, $"GuardianFlow Start (token: {flowToken})");

        if (flowWatchdogRoutine != null) StopCoroutine(flowWatchdogRoutine);
        flowWatchdogRoutine = StartCoroutine(GuardianFlowWatchdogRoutine(flowToken));

        try
        {
            // =========================================================================
            // Step 1: Intro dialogue (NPC)
            // =========================================================================
            if (activeGuardianFlowToken != flowToken) yield break;
            CurrentFlowStep = "IntroDialogue";
            UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.Intro, $"Step 1 Enter: IntroDialogue (token: {flowToken})");
#if UNITY_EDITOR
            Debug.Log($"[FlowTrace] Step 1 Enter: IntroDialogue (token: {flowToken})");
#endif
            FreezePlayerControls();
            bool introDone = false;
            DialoguePresenter.Instance.ShowDialogue(
                speakerName: "Guardian",
                lines: DialogueData.GetForestIntro(total),
                showChoicesAtEnd: false,
                onComplete: () => { introDone = true; }
            );

            while (!introDone)
            {
                if (activeGuardianFlowToken != flowToken) yield break;
                yield return null;
            }

            // Fade OUT dialogue fully (0.20s: alpha 0 AND inactive) before Step 2 starts
            DialoguePresenter.Instance.CloseDialogue();
            yield return new WaitForSecondsRealtime(0.20f);
            while (DialoguePresenter.Instance != null && DialoguePresenter.Instance.CurrentAlpha > 0.01f)
            {
                yield return null;
            }

            // =========================================================================
            // Step 2: Info cards ("BIN INFO" panels, one per bin)
            // =========================================================================
            if (activeGuardianFlowToken != flowToken) yield break;
            CurrentFlowStep = "InfoCards";
            UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.InfoCards, $"Step 2 Enter: InfoCards (token: {flowToken})");
#if UNITY_EDITOR
            Debug.Log($"[FlowTrace] Step 2 Enter: InfoCards (token: {flowToken})");
#endif
            // Keep world moving (Time.timeScale = 1f) while player input remains frozen
            Time.timeScale = 1f;
            FreezePlayerControls();

            var tm = FindFirstObjectByType<TutorialManager>();
            if (tm != null)
            {
                bool cardsDone = false;
                Action onCardsClosed = () => { cardsDone = true; };
                tm.OnTutorialClosed += onCardsClosed;

                tm.ShowTutorial(0, singleCard: false, flowToken: flowToken);

                while (!cardsDone)
                {
                    if (activeGuardianFlowToken != flowToken)
                    {
                        tm.OnTutorialClosed -= onCardsClosed;
                        yield break;
                    }
                    yield return null;
                }
                tm.OnTutorialClosed -= onCardsClosed;
                yield return new WaitForSecondsRealtime(0.20f); // Wait for InfoCards close transition
            }

            // =========================================================================
            // Step 3: Questions panel (the 1-6 Pre-Quiz)
            // =========================================================================
            if (activeGuardianFlowToken != flowToken) yield break;
            CurrentFlowStep = "PreQuiz";
            UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.Quiz, $"Step 3 Enter: PreQuiz (token: {flowToken})");
#if UNITY_EDITOR
            Debug.Log($"[FlowTrace] Step 3 Enter: PreQuiz (token: {flowToken})");
#endif
            Time.timeScale = 1f;
            FreezePlayerControls();

            var qm = QuizManager.Instance != null ? QuizManager.Instance : FindFirstObjectByType<QuizManager>();
            if (qm != null)
            {
                if (qm.IsPreQuizCompleted && PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0) == 0)
                {
                    qm.ResetQuizzes();
                }

                if (!qm.IsPreQuizCompleted)
                {
                    bool quizDone = false;
                    qm.StartPreQuiz(() => { quizDone = true; });

                    while (!quizDone)
                    {
                        if (activeGuardianFlowToken != flowToken) yield break;
                        yield return null;
                    }
                    yield return new WaitForSecondsRealtime(0.20f); // Wait for Quiz close transition
                }
            }

            // =========================================================================
            // Step 4: Guardian dialogue (QuestOffer)
            // =========================================================================
            if (activeGuardianFlowToken != flowToken) yield break;
            CurrentFlowStep = "QuestOffer";
            UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.Guardian, $"Step 4 Enter: QuestOffer (token: {flowToken})");
#if UNITY_EDITOR
            Debug.Log($"[FlowTrace] Step 4 Enter: QuestOffer (token: {flowToken})");
#endif
            Time.timeScale = 1f;
            FreezePlayerControls();

            bool choiceMade = false;

            DialoguePresenter.Instance.ShowDialogue(
                speakerName: "Guardian",
                lines: DialogueData.GetForestAccepted(total),
                showChoicesAtEnd: false,
                acceptLabel: "Accept Quest",
                declineLabel: "Not now",
                onAccept: () => {
                    accepted = true;
                    choiceMade = true;
                },
                onDecline: () => {
                    accepted = false;
                    choiceMade = true;
                },
                onComplete: () => {
                    if (!choiceMade) { accepted = true; choiceMade = true; }
                }
            );

            while (!choiceMade)
            {
                if (activeGuardianFlowToken != flowToken) yield break;
                yield return null;
            }

            // Fade OUT Guardian dialogue fully (0.20s)
            DialoguePresenter.Instance.CloseDialogue();
            yield return new WaitForSecondsRealtime(0.20f);
            while (DialoguePresenter.Instance != null && DialoguePresenter.Instance.CurrentAlpha > 0.01f)
            {
                yield return null;
            }
        }
        finally
        {
            EndGuardianFlow(accepted, flowToken);
        }
    }

    public void EndGuardianFlow(bool accepted, string flowToken)
    {
        if (flowEndExecuted) return;
        flowEndExecuted = true;

        if (flowWatchdogRoutine != null)
        {
            StopCoroutine(flowWatchdogRoutine);
            flowWatchdogRoutine = null;
        }

        IsRunningGuardianFlow = false;
        IsAnyGuardianFlowRunning = false;
        CurrentFlowStep = "End";

        // 1. Exit dialogue camera return to first-person view
        DeactivateDialogueCamera();

        // 2. Reset modal stack and blur service completely (weight 0, volume disabled)
        ModalManager.Instance?.ForceCloseAllAndRestore();
        ModalManager.Instance?.BlurFailsafeReset();

        // 3. Restore controls and lock cursor
        RestorePlayerControls();

        // 4. Reset NPC state
        Animator anim = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.SetBool("IsTalking", false);
        isTalking = false;
        Time.timeScale = 1f;
        WorldInteractionPrompt.Instance?.SetBlocked(false);

        // 5. Release EVERY hide reason registered with HudVisibilityService/Orchestrator
        var released = new List<string>();
        if (HudVisibilityService.Instance != null)
        {
            foreach (var r in new List<string>(HudVisibilityService.Instance.ActiveReasons))
            {
                HudVisibilityService.Instance.ShowHUD(r);
                released.Add(r);
            }
            HudVisibilityService.Instance.ClearAllReasons();
        }
        UIOrchestrator.Instance?.SetModalsOpen(false);

        // 6. Advance to Step 5 (TabTutorial) or Step 6 (FreePlay for returning player)
        if (accepted)
        {
            hasAcceptedQuest = true;
        PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestQuestAccepted, 1);
        PlayerPrefs.Save();
        OnQuestAccepted?.Invoke();
        ObjectiveMarkerService.Instance?.RefreshTargetFromState();
            IsAnyQuestActive = true;
            PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestQuestAccepted, 1);
            PlayerPrefs.Save();
            QuestManager.ResetAllQuestCountsToZero();
            OnQuestAccepted?.Invoke();
            ObjectiveMarkerService.Instance?.RefreshTargetFromState();

            var intro = IntroSequenceController.Instance != null ? IntroSequenceController.Instance : FindFirstObjectByType<IntroSequenceController>();
            if (intro != null)
            {
                intro.HandleTutorialClosed();
            }

            bool tabTipAlreadyDone = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0) == 1;
            if (!tabTipAlreadyDone)
            {
                // Step 5: TabTutorial (Quest Tab appears; Trash remains hidden until TAB tip completes)
                IntroSequenceController.Instance?.SetTrashVisible(false, false, "Step 5 (TabTutorial) - trash stays hidden until TAB tip finishes");
                UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.TabTutorial, $"Step 4 Guardian completed -> Step 5 TabTutorial (token: {flowToken})");

                if (QuestPanelToggleController.Instance != null)
                {
                    QuestPanelToggleController.Instance.ResetTabCycle();
                    QuestPanelToggleController.Instance.SlideInFromLeft(0.4f);
                }

                // Raise FlowCompleted to trigger GameplayControlTipsManager (Step 5 -> Step 6 on completion)
                OnFlowCompleted?.Invoke(flowToken);
            }
            else
            {
                // Returning player: skip Step 5 (TabTutorial) and go straight to Step 6 (FreePlay) with quest tab & trash enabled
                UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, $"Returning player (EcoQuest_TabTipDone == 1) -> Step 6 FreePlay (token: {flowToken})");
                if (QuestPanelToggleController.Instance != null)
                {
                    QuestPanelToggleController.Instance.ResetTabCycle();
                    QuestPanelToggleController.Instance.SlideInFromLeft(0.4f);
                }
                IntroSequenceController.Instance?.HandleTabTipCompleted();
            }
        }
        else
        {
            hasAcceptedQuest = false;
            IsAnyQuestActive = false;
            UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.FreePlay, "GuardianFlow Declined");
            DeclineRepeatMission();
        }

#if UNITY_EDITOR
        Debug.Log($"[FlowTrace] End: released reasons=[{string.Join(", ", released)}] step={UIOrchestrator.Instance?.CurrentFlowStep}");
#endif

        if (HudVisibilityService.Instance != null && HudVisibilityService.Instance.HideReasonCount > 0)
        {
            Debug.LogWarning($"[GuardianFlow] Stale hide reasons remaining after End: {string.Join(", ", HudVisibilityService.Instance.ActiveReasons)}");
        }

        CurrentFlowStep = "Completed";
    }

    private IEnumerator GuardianFlowWatchdogRoutine(string flowToken)
    {
        float invisibleTimer = 0f;
        while (IsRunningGuardianFlow && activeGuardianFlowToken == flowToken)
        {
            yield return new WaitForSecondsRealtime(0.20f);

            bool popupVisible = (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalActuallyVisible())
                || (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue && !DialoguePresenter.Instance.IsSuspended && DialoguePresenter.Instance.CurrentAlpha > 0.01f);

            if (!popupVisible)
            {
                invisibleTimer += 0.20f;
                if (invisibleTimer >= 2.0f)
                {
                    Debug.LogWarning($"[GuardianFlow Watchdog] Flow active at step '{CurrentFlowStep}' but no visible popup for {invisibleTimer:F2}s! Forcing End step.");
                    EndGuardianFlow(accepted: true, flowToken: flowToken);
                    yield break;
                }
            }
            else
            {
                invisibleTimer = 0f;
            }
        }
    }

    private void ShowAcceptedFollowup(int total)
    {
        if (DialoguePresenter.Instance != null)
        {
            DialoguePresenter.Instance.ShowDialogue(
                speakerName: "Guardian",
                lines: DialogueData.GetForestAccepted(total),
                showChoicesAtEnd: false,
                onComplete: () => {
                    EndDialogue();
                }
            );
        }
    }

    private void ShowDeclinedFollowup()
    {
        if (DialoguePresenter.Instance != null)
        {
            DialoguePresenter.Instance.ShowDialogue(
                speakerName: "Guardian",
                lines: DialogueData.GetForestDeclined(),
                showChoicesAtEnd: false,
                onComplete: () => {
                    DeclineRepeatMission();
                }
            );
        }
    }

    private IEnumerator DialogueFailSafeRoutine()
    {
        yield return new WaitForSecondsRealtime(0.3f);
        if (isTalking)
        {
            bool presenterShowing = DialoguePresenter.Instance != null && 
                                   DialoguePresenter.Instance.IsInDialogue && 
                                   DialoguePresenter.Instance.CurrentAlpha > 0.01f;
            bool legacyShowing = dialogueBox != null && dialogueBox.activeInHierarchy;
            if (!presenterShowing && !legacyShowing)
            {
                Debug.LogError($"[DialogueFailSafe] Dialogue failed to display within 0.3s for NPC '{gameObject.name}'! Aborting dialogue and restoring controls.");
                EndDialogue();
                RestorePlayerControls();
                DeactivateDialogueCamera();
                Time.timeScale = 1f;
                WorldInteractionPrompt.Instance?.SetBlocked(false);
                if (dialogueBox != null && DialoguePresenter.Instance == null) dialogueBox.SetActive(false);
            }
        }
    }

    public void DisplayNextLine()
    {
        if (isRepeatOfferDialogue)
        {
            return;
        }

        currentLineIndex++;
        if (dialogueLines != null && currentLineIndex < dialogueLines.Length)
        {
            UpdateDialogueText();
        }
        else
        {
            EndDialogue();
        }
    }

    private void ConfigureDialogueButtons(bool repeatMode, bool showButtons)
    {
        EnsureUIReferences();

        if (acceptMissionButton != null)
        {
            string acceptLabel = "Accept";
            KeyCapPrompt kcp = acceptMissionButton.GetComponentInChildren<KeyCapPrompt>(true);
            if (kcp != null)
            {
                kcp.SetPrompt("E", acceptLabel);
            }
            else
            {
                TextMeshProUGUI txt = acceptMissionButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (txt != null)
                {
                    txt.text = acceptLabel;
                }
            }

            var rt = acceptMissionButton.GetComponent<RectTransform>();
            if (rt != null)
            {
                float textWidth = kcp?.ActionLabelText != null ? kcp.ActionLabelText.GetPreferredValues(acceptLabel).x : 60f;
                float w = Mathf.Max(140f, textWidth + 76f);
                rt.sizeDelta = new Vector2(w, rt.sizeDelta.y > 10f ? rt.sizeDelta.y : 52f);
            }

            acceptMissionButton.onClick.RemoveAllListeners();
            acceptMissionButton.onClick.AddListener(AcceptMission);
            acceptMissionButton.gameObject.SetActive(showButtons);
        }

        if (notNowButton != null)
        {
            string declineLabel = "Close";
            KeyCapPrompt kcp = notNowButton.GetComponentInChildren<KeyCapPrompt>(true);
            if (kcp != null)
            {
                kcp.SetPrompt("Q", declineLabel);
            }
            else
            {
                TextMeshProUGUI nTxt = notNowButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (nTxt != null)
                {
                    nTxt.text = declineLabel;
                }
            }

            var rt = notNowButton.GetComponent<RectTransform>();
            if (rt != null)
            {
                float textWidth = kcp?.ActionLabelText != null ? kcp.ActionLabelText.GetPreferredValues(declineLabel).x : 60f;
                float w = Mathf.Max(140f, textWidth + 76f);
                rt.sizeDelta = new Vector2(w, rt.sizeDelta.y > 10f ? rt.sizeDelta.y : 52f);
            }

            notNowButton.onClick.RemoveAllListeners();
            notNowButton.onClick.AddListener(DeclineRepeatMission);
            notNowButton.gameObject.SetActive(showButtons);
        }

        SetContinueHintActive(!showButtons);
    }

    private string GetCurrentDialogueLine()
    {
        if (isRepeatOfferDialogue)
        {
            return string.IsNullOrEmpty(repeatOfferLine)
                ? "Great work, Guardian of the forest! Would you like to clean up again?"
                : repeatOfferLine;
        }
        if (dialogueLines != null && currentLineIndex >= 0 && currentLineIndex < dialogueLines.Length)
        {
            return dialogueLines[currentLineIndex];
        }
        return "";
    }

    private void UpdateDialogueText()
    {
        if (isRepeatOfferDialogue)
        {
            ConfigureDialogueButtons(repeatMode: true, showButtons: true);
            SetContinueHintActive(false);

            if (typingCoroutine != null) StopCoroutine(typingCoroutine);
            typingCoroutine = StartCoroutine(TypeLine());
            return;
        }

        if (dialogueLines != null && dialogueLines.Length > 0 && currentLineIndex < dialogueLines.Length)
        {
            bool isLastLine = (currentLineIndex == dialogueLines.Length - 1);
            if (isLastLine)
            {
                OnLastDialogueLineReached?.Invoke();
                OnLastDialogueLine?.Invoke();
            }

            ConfigureDialogueButtons(repeatMode: false, showButtons: isLastLine);
            SetContinueHintActive(!isLastLine);

            if (typingCoroutine != null) StopCoroutine(typingCoroutine);
            
            typingCoroutine = StartCoroutine(TypeLine());
        }
        else
        {
            EndDialogue();
        }
    }

    private IEnumerator TypeLine()
    {
        isTyping = true;
        canContinue = false;
        if (dialogueText != null) dialogueText.text = "";

        string line = GetCurrentDialogueLine();
        float speed = 0.025f; // ~40 chars/sec
        foreach (char c in line.ToCharArray())
        {
            if (dialogueText != null) dialogueText.text += c;
            if (c == '.' || c == ',' || c == '!' || c == '?')
                yield return new WaitForSecondsRealtime(0.12f);
            else
                yield return new WaitForSecondsRealtime(speed);
        }

        isTyping = false;
        advanceLockTimer = 0.5f;
        StartCoroutine(WaitBeforeContinue());
    }

    private void CompleteLineInstantly()
    {
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        
        if (dialogueText != null)
        {
            dialogueText.text = GetCurrentDialogueLine();
        }
        isTyping = false;
        advanceLockTimer = 0.5f;
        
        StartCoroutine(WaitBeforeContinue());
    }

    private IEnumerator WaitBeforeContinue()
    {
        yield return new WaitForSecondsRealtime(nextLineDelay);
        canContinue = true;
    }

    public void AcceptMission()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
        isTyping = false;
        EndDialogue();
    }

    public void DeclineRepeatMission()
    {
        globalNpcInteractCooldownUntil = Time.unscaledTime + 2.0f;
        if (!isTalking) return;
        bool wasRepeat = isRepeatOfferDialogue;
        isTalking = false;
        isTyping = false;
        isRepeatOfferDialogue = false;
        hasAcceptedQuest = false;

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        if (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue)
            DialoguePresenter.Instance.CloseDialogue();

        if (dialogueBox != null && DialoguePresenter.Instance == null) dialogueBox.SetActive(false);
        if (acceptMissionButton != null) acceptMissionButton.gameObject.SetActive(false);
        if (notNowButton != null) notNowButton.gameObject.SetActive(false);
        SetContinueHintActive(false);
        WorldInteractionPrompt.Instance?.SetBlocked(false);

        DeactivateDialogueCamera();

        Animator anim = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.SetBool("IsTalking", false);
        }

        if (wasRepeat)
        {
            IntroSequenceController intro = IntroSequenceController.Instance != null
                ? IntroSequenceController.Instance
                : FindFirstObjectByType<IntroSequenceController>();

            if (intro != null)
            {
                intro.EndRepeatDialogue(acceptedQuest: false);
            }
        }

        RestorePlayerControls();
    }

    public void EndDialogue()
    {
        globalNpcInteractCooldownUntil = Time.unscaledTime + 2.0f;
        if (!isTalking) return;
        bool wasRepeatOffer = isRepeatOfferDialogue;
        isTalking = false;
        isTyping = false;
        isRepeatOfferDialogue = false;
        IsAnyGuardianFlowRunning = false;
        hasAcceptedQuest = true;
        PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestQuestAccepted, 1);
        PlayerPrefs.Save();
        OnQuestAccepted?.Invoke();
        ObjectiveMarkerService.Instance?.RefreshTargetFromState();

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        // Step A: Close UI_Dialogue cleanly
        if (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue)
            DialoguePresenter.Instance.CloseDialogue();

        if (dialogueBox != null && DialoguePresenter.Instance == null) dialogueBox.SetActive(false);
        if (acceptMissionButton != null) acceptMissionButton.gameObject.SetActive(false);
        if (notNowButton != null) notNowButton.gameObject.SetActive(false);
        SetContinueHintActive(false);
        WorldInteractionPrompt.Instance?.SetBlocked(false);

        // 1. Return camera smoothly to first-person view (unscaled blend)
        DeactivateDialogueCamera();

        // 2. Return NPC animator to Idle
        Animator anim = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.SetBool("IsTalking", false);
        }

        if (wasRepeatOffer)
        {
            QuestManager.IsRepeatQuest = true;
            if (trashContainer != null) trashContainer.SetActive(true);
        }
        else
        {
            QuestManager.IsRepeatQuest = false;
        }

        // Reset quests
        if (allQuests == null || allQuests.Length == 0)
        {
            allQuests = FindObjectsByType<QuestManager>(FindObjectsSortMode.None);
        }

        foreach (QuestManager qm in allQuests)
        {
            if (qm != null) qm.ResetQuest(); 
        }

        if (overheadMarker != null)
        {
            overheadMarker.SetActive(false);
        }

        IntroSequenceController intro = IntroSequenceController.Instance != null
            ? IntroSequenceController.Instance
            : FindFirstObjectByType<IntroSequenceController>();

        if (wasRepeatOffer)
        {
            // Repeat quest: skip tutorial, Pre-Test, and lesson cards, show UI_Quest, and return control
            if (trashContainer != null) trashContainer.SetActive(true);
            if (questCanvas != null) questCanvas.SetActive(true);
            MinimapIcon.SetTrashIconsActive(true);

            if (intro != null)
            {
                intro.EndRepeatDialogue(acceptedQuest: true);
            }

            RestorePlayerControls();

            OnDialogueEnded?.Invoke();
            OnDialogueEnd?.Invoke();
            return;
        }

        // Step C & D: Info cards and quiz are owned exclusively by GuardianFlowRoutine (Single Owner)
        RestorePlayerControls();

        OnDialogueEnded?.Invoke();
        OnDialogueEnd?.Invoke();
    }

    private void ActivateDialogueCamera()
    {
        EnsureCameraPointSetup();
        EcoQuest.Shared.DialogueCameraRig.Instance.EnterDialogue(transform, playerTransform, 0.6f, "Guardian");

        Camera mainCam = Camera.main;
        bool usedCinemachine = true;

        if (false && mainCam != null)
        {
            if (!cameraCullingMaskModified)
            {
                savedCameraCullingMask = mainCam.cullingMask;
                int hfLayer = LayerMask.NameToLayer("HiddenFromFirstPerson");
                if (hfLayer != -1) mainCam.cullingMask |= (1 << hfLayer);
                else mainCam.cullingMask |= (1 << 7);
                cameraCullingMaskModified = true;
            }

            Transform vmCam = mainCam.transform.Find("ViewmodelCamera");
            if (vmCam != null) vmCam.gameObject.SetActive(false);

            if (DragAndDrop.CurrentlyGrabbedItem != null)
            {
                hiddenCarriedItem = DragAndDrop.CurrentlyGrabbedItem.gameObject;
                hiddenCarriedItem.SetActive(false);
            }

            if (playerTransform == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) playerTransform = p.transform;
            }
            Transform playerTrans = playerTransform != null ? playerTransform : (mainCam != null ? mainCam.transform : null);
            Vector3 playerPos = playerTrans != null ? playerTrans.position : (mainCam.transform.position - Vector3.up * 1.5f);
            Vector3 speakerPos = transform.position;

            Vector3 toSpeaker = speakerPos - playerPos;
            toSpeaker.y = 0f;
            if (toSpeaker.sqrMagnitude < 0.01f)
                toSpeaker = playerTrans != null ? playerTrans.forward : Vector3.forward;
            toSpeaker.Normalize();

            Vector3 rightOffset = Vector3.Cross(Vector3.up, toSpeaker) * 0.42f;
            Vector3 playerEye = playerPos + Vector3.up * 1.52f;
            Vector3 desiredShotPos = playerPos - toSpeaker * 0.85f + rightOffset + Vector3.up * 1.58f;

            Vector3 rayDir = desiredShotPos - playerEye;
            float rayDist = rayDir.magnitude;
            if (rayDist > 0.05f)
            {
                rayDir /= rayDist;
                int mask = ~((1 << 8) | (1 << 2)); // Ignore Player (8) and IgnoreRaycast (2)
                if (Physics.SphereCast(playerEye, 0.20f, rayDir, out RaycastHit hit, rayDist, mask, QueryTriggerInteraction.Ignore))
                {
                    desiredShotPos = playerEye + rayDir * Mathf.Max(0.35f, hit.distance - 0.1f);
                }
            }

            Vector3 speakerChest = speakerPos + Vector3.up * 1.35f;
            Quaternion shotRot = Quaternion.LookRotation((speakerChest - desiredShotPos).normalized, Vector3.up);

            if (dialogueCameraPoint != null)
            {
                dialogueCameraPoint.SetPositionAndRotation(desiredShotPos, shotRot);
            }

            CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
            if (brain != null && dialogueCameraPoint != null)
            {
                brain.IgnoreTimeScale = true;
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, transitionTime);
                CinemachineCamera cmCam = dialogueCameraPoint.GetComponent<CinemachineCamera>();
                if (cmCam != null)
                {
                    cmCam.Lens.FieldOfView = dialogueFOV;
                    cmCam.Lens.NearClipPlane = 0.03f;
                    cmCam.Priority.Value = 35; // Blend into dialogue shot
                    usedCinemachine = true;
                }
            }
        }

        // Slide in letterbox bars (0.6s)
        if (topLetterboxBar == null)
        {
            var tlb = GameObject.Find("TopLetterboxBar");
            if (tlb != null) topLetterboxBar = tlb.GetComponent<RectTransform>();
        }
        if (bottomLetterboxBar == null)
        {
            var blb = GameObject.Find("BottomLetterboxBar");
            if (blb != null) bottomLetterboxBar = blb.GetComponent<RectTransform>();
        }

        if (letterboxRoutine != null) StopCoroutine(letterboxRoutine);
        if (isActiveAndEnabled && gameObject.scene.isLoaded)
            letterboxRoutine = StartCoroutine(TransitionDialogueEnterRoutine());

        // Fade HUD
        HudVisibilityService.Instance?.HideHUD("DialogueCam", 0.25f);

        // Engage speaker animator
        if (speakerAnimator == null) speakerAnimator = GetComponent<NpcDialogueSpeakerAnimator>();
        if (speakerAnimator != null)
        {
            Transform pTarget = playerTransform != null ? playerTransform : (mainCam != null ? mainCam.transform : null);
            speakerAnimator.BeginDialogue(pTarget, guideNpc: true);
        }

        // Subscribe to typing state changed
        if (DialoguePresenter.Instance != null)
        {
            DialoguePresenter.Instance.OnTypingStateChanged -= OnDialogueTypingStateChanged;
            DialoguePresenter.Instance.OnTypingStateChanged += OnDialogueTypingStateChanged;
        }

        // Fallback smooth Lerp if CinemachineBrain is not present
        if (!usedCinemachine && mainCam != null && dialogueCameraPoint != null)
        {
            if (cameraBlendCoroutine != null) StopCoroutine(cameraBlendCoroutine);
            originalCamPos = mainCam.transform.position;
            originalCamRot = mainCam.transform.rotation;
            originalCamFOV = mainCam.fieldOfView;
            cameraBlendCoroutine = StartCoroutine(LerpCamera(mainCam, originalCamPos, originalCamRot, originalCamFOV,
                dialogueCameraPoint.position, dialogueCameraPoint.rotation, dialogueFOV, transitionTime));
        }
    }

    public void DeactivateDialogueCamera()
    {
        EcoQuest.Shared.DialogueCameraRig.Instance.ExitDialogue("Guardian", 0.6f);

        if (dialogueCameraPoint != null)
        {
            CinemachineCamera cmCam = dialogueCameraPoint.GetComponent<CinemachineCamera>();
            if (cmCam != null)
            {
                cmCam.Priority.Value = 0; // Blend back to player follow camera
            }
        }

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            if (cameraCullingMaskModified)
            {
                mainCam.cullingMask = savedCameraCullingMask;
                cameraCullingMaskModified = false;
            }
            Transform vmCam = mainCam.transform.Find("ViewmodelCamera");
            if (vmCam != null) vmCam.gameObject.SetActive(true);
        }

        if (hiddenCarriedItem != null)
        {
            hiddenCarriedItem.SetActive(true);
            hiddenCarriedItem = null;
        }

        // Slide out letterbox bars (0.6s)
        if (letterboxRoutine != null) StopCoroutine(letterboxRoutine);
        if (isActiveAndEnabled && gameObject.scene.isLoaded)
            letterboxRoutine = StartCoroutine(TransitionDialogueExitRoutine());
        else
            ResetLetterboxBarsImmediate();

        // Restore HUD
        HudVisibilityService.Instance?.ShowHUD("DialogueCam", 0.25f);

        // Unsubscribe typing state
        if (DialoguePresenter.Instance != null)
        {
            DialoguePresenter.Instance.OnTypingStateChanged -= OnDialogueTypingStateChanged;
        }

        // Release speaker animator
        if (speakerAnimator != null)
        {
            bool accepted = hasAcceptedQuest || QuestManager.IsQuestComplete;
            speakerAnimator.EndDialogue(accepted);
        }

        if (mainCam != null && dialogueCameraPoint != null)
        {
            CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
            if (brain != null)
            {
                brain.IgnoreTimeScale = true;
            }
            else if (originalCamPos != Vector3.zero)
            {
                if (cameraBlendCoroutine != null) StopCoroutine(cameraBlendCoroutine);
                if (isActiveAndEnabled && gameObject.scene.isLoaded)
                {
                    cameraBlendCoroutine = StartCoroutine(LerpCamera(mainCam, mainCam.transform.position, mainCam.transform.rotation, mainCam.fieldOfView,
                        originalCamPos, originalCamRot, originalCamFOV, transitionTime));
                }
                else
                {
                    mainCam.transform.position = originalCamPos;
                    mainCam.transform.rotation = originalCamRot;
                    mainCam.fieldOfView = originalCamFOV;
                }
            }
        }
    }

    private void OnDialogueTypingStateChanged(bool isTyping)
    {
        if (speakerAnimator != null)
        {
            speakerAnimator.SetTyping(isTyping);
        }
    }

    private IEnumerator TransitionDialogueEnterRoutine()
    {
        float elapsed = 0f;
        float dur = 0.6f;
        Vector2 topStart = new Vector2(0f, 86f);
        Vector2 topEnd = Vector2.zero;
        Vector2 botStart = new Vector2(0f, -86f);
        Vector2 botEnd = Vector2.zero;

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            if (topLetterboxBar != null) topLetterboxBar.anchoredPosition = Vector2.Lerp(topStart, topEnd, smooth);
            if (bottomLetterboxBar != null) bottomLetterboxBar.anchoredPosition = Vector2.Lerp(botStart, botEnd, smooth);
            yield return null;
        }

        if (topLetterboxBar != null) topLetterboxBar.anchoredPosition = topEnd;
        if (bottomLetterboxBar != null) bottomLetterboxBar.anchoredPosition = botEnd;
        letterboxRoutine = null;
    }

    private IEnumerator TransitionDialogueExitRoutine()
    {
        float elapsed = 0f;
        float dur = 0.6f;
        Vector2 topStart = topLetterboxBar != null ? topLetterboxBar.anchoredPosition : Vector2.zero;
        Vector2 topEnd = new Vector2(0f, 86f);
        Vector2 botStart = bottomLetterboxBar != null ? bottomLetterboxBar.anchoredPosition : Vector2.zero;
        Vector2 botEnd = new Vector2(0f, -86f);

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            if (topLetterboxBar != null) topLetterboxBar.anchoredPosition = Vector2.Lerp(topStart, topEnd, smooth);
            if (bottomLetterboxBar != null) bottomLetterboxBar.anchoredPosition = Vector2.Lerp(botStart, botEnd, smooth);
            yield return null;
        }

        if (topLetterboxBar != null) topLetterboxBar.anchoredPosition = topEnd;
        if (bottomLetterboxBar != null) bottomLetterboxBar.anchoredPosition = botEnd;
        letterboxRoutine = null;
    }

    private void ResetLetterboxBarsImmediate()
    {
        if (topLetterboxBar != null) topLetterboxBar.anchoredPosition = new Vector2(0f, 86f);
        if (bottomLetterboxBar != null) bottomLetterboxBar.anchoredPosition = new Vector2(0f, -86f);
    }

    private IEnumerator LerpCamera(Camera cam, Vector3 startPos, Quaternion startRot, float startFOV, Vector3 endPos, Quaternion endRot, float endFOV, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            cam.transform.position = Vector3.Lerp(startPos, endPos, t);
            cam.transform.rotation = Quaternion.Slerp(startRot, endRot, t);
            cam.fieldOfView = Mathf.Lerp(startFOV, endFOV, t);
            yield return null;
        }
        cam.transform.position = endPos;
        cam.transform.rotation = endRot;
        cam.fieldOfView = endFOV;
    }

    private void FreezePlayerControls()
    {
        if (playerTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
        }

        StarterAssets.FirstPersonController fps = playerTransform != null 
            ? playerTransform.GetComponent<StarterAssets.FirstPersonController>() 
            : FindFirstObjectByType<StarterAssets.FirstPersonController>();
        if (fps != null) fps.enabled = false;

        StarterAssets.StarterAssetsInputs inputs = playerTransform != null 
            ? playerTransform.GetComponent<StarterAssets.StarterAssetsInputs>() 
            : FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
        if (inputs != null)
        {
            inputs.move = Vector2.zero;
            inputs.look = Vector2.zero;
        }

        CursorState.SetLocked(false, inputs);
    }

    public void RestorePlayerControls()
    {
        if (playerTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
        }

        StarterAssets.FirstPersonController fps = playerTransform != null 
            ? playerTransform.GetComponent<StarterAssets.FirstPersonController>() 
            : FindFirstObjectByType<StarterAssets.FirstPersonController>();
        if (fps != null) fps.enabled = true;

        StarterAssets.StarterAssetsInputs inputs = playerTransform != null 
            ? playerTransform.GetComponent<StarterAssets.StarterAssetsInputs>() 
            : FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
        CursorState.SetLocked(true, inputs);
    }

    private IEnumerator SmoothTurnToPlayer()
    {
        if (playerTransform == null) yield break;
        Vector3 dir = playerTransform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) yield break;

        Quaternion startRot = transform.rotation;
        Quaternion targetRot = Quaternion.LookRotation(dir);
        float elapsed = 0f;
        float duration = 0.5f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            transform.rotation = Quaternion.Slerp(startRot, targetRot, elapsed / duration);
            yield return null;
        }
        transform.rotation = targetRot;
    }

    /// <summary>
    /// Smart Safeguard 2: Clears all saved tutorial/quiz flags so the full tutorial flow (Steps 1-6)
    /// can be tested from a fresh start. Accessible via EcoQuest > Reset Tutorial or component context menu.
    /// </summary>
    [ContextMenu("Reset Tutorial")]
    public void ResetTutorialContextMenu()
    {
        ResetAllTutorialFlags();
    }

    public static void ResetStaticFlowState()
    {
        IsAnyQuestActive = false;
        IsAnyGuardianFlowRunning = false;
        CurrentFlowStep = "Idle";
        OnFlowCompleted = null;
        globalNpcInteractCooldownUntil = 0f;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("EcoQuest/Reset Tutorial")]
    [UnityEditor.MenuItem("Tools/EcoQuest/Reset Tutorial")]
#endif
    public static void ResetAllTutorialFlags()
    {
        EcoQuestSaveKeys.ClearAllTutorialProgress();
        ResetStaticFlowState();

        if (Application.isPlaying)
        {
            QuizManager.Instance?.ResetQuizzes();
            GameplayControlTipsManager.Instance?.ResetTipsState();
            QuestManager.ResetAllQuestCountsToZero();
            IntroSequenceController.Instance?.SetTrashVisible(false, false, "Reset Tutorial");
            UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.Tutorial, "Reset Tutorial command invoked");
        }

        Debug.Log("[FlowState] Tutorial flags reset (EcoQuest_TabTipDone=0, EcoQuest_SeenInfoCards='', EcoQuest_PreQuizScore cleared). Ready for fresh start!");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) isPlayerNearby = true;
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Player")) isPlayerNearby = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNearby = false;
            SetLegacyInteractPromptActive(false);
            if (isTalking)
            {
                DeclineRepeatMission();
            }
            if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        }
    }

    private void CleanupCoroutinesAndState()
    {
        if (DialoguePresenter.Instance != null)
        {
            DialoguePresenter.Instance.OnTypingStateChanged -= OnDialogueTypingStateChanged;
        }
        if (letterboxRoutine != null)
        {
            StopCoroutine(letterboxRoutine);
            letterboxRoutine = null;
        }
        if (cameraCullingMaskModified && Camera.main != null)
        {
            Camera.main.cullingMask = savedCameraCullingMask;
            cameraCullingMaskModified = false;
        }
        if (hiddenCarriedItem != null)
        {
            hiddenCarriedItem.SetActive(true);
            hiddenCarriedItem = null;
        }
        ResetLetterboxBarsImmediate();

        if (flowWatchdogRoutine != null)
        {
            StopCoroutine(flowWatchdogRoutine);
            flowWatchdogRoutine = null;
        }
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
        if (cameraBlendCoroutine != null)
        {
            StopCoroutine(cameraBlendCoroutine);
            cameraBlendCoroutine = null;
        }
        if (IsRunningGuardianFlow)
        {
            IsRunningGuardianFlow = false;
            IsAnyGuardianFlowRunning = false;
            activeGuardianFlowToken = null;
        }
        if (isTalking && gameObject.scene.isLoaded)
        {
            DeclineRepeatMission();
        }
    }

    private void OnDisable()
    {
        CleanupCoroutinesAndState();
    }

    private void OnDestroy()
    {
        CleanupCoroutinesAndState();
    }
}