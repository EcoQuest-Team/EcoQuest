using System.Collections;
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
    [SerializeField] private float dialogueFOV = 55f;
    [Tooltip("Camera transition blend duration in seconds")]
    [SerializeField] private float transitionTime = 0.7f;

    [Header("Trash Management")]
    [SerializeField] private GameObject trashContainer; 

    [Header("Tutorial Settings")]
    [SerializeField] private GameObject tutorialNotification;
    [SerializeField] private TutorialManager tutorialManager;

    [Header("Dialogue Content & Settings")]
    [SerializeField] private string[] dialogueLines;
    [SerializeField] private string repeatOfferLine = "Great work, Guardian of the forest! Would you like to clean up again?";
    
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
    private bool canContinue = false;

    // Fallback camera lerp state
    private Vector3 originalCamPos;
    private Quaternion originalCamRot;
    private float originalCamFOV;

    public bool IsTalking => isTalking;
    public bool IsQuestActive => hasAcceptedQuest && !QuestManager.IsQuestComplete;
    public bool HasCompletedFirstQuest => hasCompletedFirstQuest;

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

        if (interactPrompt != null) interactPrompt.SetActive(false);
        if (dialogueBox != null) dialogueBox.SetActive(false);
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
        if (continueHint != null) continueHint.gameObject.SetActive(false);
        
        if (questCanvas != null && IntroSequenceController.Instance == null) questCanvas.SetActive(false);
        if (trashContainer != null) trashContainer.SetActive(false);

        if (tutorialNotification != null) tutorialNotification.SetActive(true);

        if (GetComponent<NPCWaypointPointer>() == null)
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

    private void Update()
    {
        if (playerTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
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

        HandleInteractionPrompt(isQuestActive || isModalBlocking);

        if (isPlayerNearby && !isTalking && !isQuestActive && !isModalBlocking && Input.GetKeyDown(KeyCode.E))
        {
            if (IsTutorialLocked())
            {
                // Locked until the tutorial tips are completed
                return;
            }
            FindAnyObjectByType<RangerAnimationDriver>()?.PlayInteract();
            StartDialogue();
        }

        // Support advancing dialogue via Click, E, Space, or Return
        if (isTalking)
        {
            bool isLastLine = isRepeatOfferDialogue || (dialogueLines != null && currentLineIndex >= dialogueLines.Length - 1);

            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                if (isTyping)
                {
                    CompleteLineInstantly();
                }
                else if (isLastLine)
                {
                    if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
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
            if (anim.speed == 0f) anim.speed = 1f;

            if (anim.runtimeAnimatorController == null)
            {
#if UNITY_EDITOR
                // Prefer GuardianAnimatorController if available
                RuntimeAnimatorController guardianCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/EcoQuest/Asset/GuardianAnimatorController.controller");
                if (guardianCtrl != null)
                {
                    anim.runtimeAnimatorController = guardianCtrl;
                }
                else
                {
                    string[] guids = UnityEditor.AssetDatabase.FindAssets("t:AnimatorController");
                    foreach (string guid in guids)
                    {
                        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                        if (path.Contains("Guardian") || path.Contains("MaleAnimator") || path.Contains("CivilMale"))
                        {
                            RuntimeAnimatorController ctrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
                            if (ctrl != null)
                            {
                                anim.runtimeAnimatorController = ctrl;
                                break;
                            }
                        }
                    }
                }
#endif
            }

            // Force Rebind to snap character out of T-Pose into animated pose
            if (anim.runtimeAnimatorController != null && Application.isPlaying)
            {
                anim.Rebind();
                anim.Update(0f);
            }
        }
    }

    private TextMeshProUGUI promptText;
    private string defaultPromptText = "Press E to Talk";

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

    private void HandleInteractionPrompt(bool isQuestActive)
    {
        if (interactPrompt == null) return;
        bool shouldShowPrompt = isPlayerNearby && !isTalking && !isQuestActive;
        
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
                    defaultPromptText = "Press E to Talk";
                }
                promptText.text = IsTutorialLocked() ? "Finish the tips first!" : defaultPromptText;
            }
        }
    }

    public void StartDialogue()
    {
        EnsureUIReferences();

        isTalking = true;
        isRepeatOfferDialogue = hasCompletedFirstQuest;
        currentLineIndex = 0;

        if (interactPrompt != null) interactPrompt.SetActive(false);
        if (dialogueBox != null) dialogueBox.SetActive(true);
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

        UpdateDialogueText();
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
            RectTransform rt = acceptMissionButton.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.66f, 0.08f);
                rt.anchorMax = new Vector2(0.96f, 0.36f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            Transform check = acceptMissionButton.transform.Find("CheckmarkIcon");
            if (check != null) check.gameObject.SetActive(true);

            TextMeshProUGUI txt = acceptMissionButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txt != null)
            {
                txt.text = repeatMode ? "Yes, let's go!" : "[E] ACCEPT\nMISSION";
                RectTransform trt = txt.GetComponent<RectTransform>();
                if (trt != null)
                {
                    trt.offsetMin = new Vector2(38f, 4f);
                    trt.offsetMax = new Vector2(-8f, -4f);
                }
            }

            acceptMissionButton.onClick.RemoveAllListeners();
            acceptMissionButton.onClick.AddListener(AcceptMission);
            acceptMissionButton.gameObject.SetActive(showButtons);
        }

        if (notNowButton != null)
        {
            RectTransform nrt = notNowButton.GetComponent<RectTransform>();
            if (nrt != null)
            {
                nrt.anchorMin = new Vector2(0.36f, 0.08f);
                nrt.anchorMax = new Vector2(0.64f, 0.36f);
                nrt.offsetMin = Vector2.zero;
                nrt.offsetMax = Vector2.zero;
            }

            Transform nCheck = notNowButton.transform.Find("CheckmarkIcon");
            if (nCheck != null) nCheck.gameObject.SetActive(false);

            TextMeshProUGUI nTxt = notNowButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (nTxt != null)
            {
                nTxt.text = "Not now";
                RectTransform ntrt = nTxt.GetComponent<RectTransform>();
                if (ntrt != null)
                {
                    ntrt.offsetMin = new Vector2(8f, 4f);
                    ntrt.offsetMax = new Vector2(-8f, -4f);
                }
            }

            notNowButton.onClick.RemoveAllListeners();
            notNowButton.onClick.AddListener(DeclineRepeatMission);
            notNowButton.gameObject.SetActive(repeatMode && showButtons);
        }
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
            if (continueHint != null) continueHint.gameObject.SetActive(false);

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

            if (continueHint != null)
            {
                continueHint.gameObject.SetActive(!isLastLine);
            }

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
        foreach (char c in line.ToCharArray())
        {
            if (dialogueText != null) dialogueText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }

        isTyping = false;
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
        
        StartCoroutine(WaitBeforeContinue());
    }

    private IEnumerator WaitBeforeContinue()
    {
        yield return new WaitForSeconds(nextLineDelay);
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
        if (!isTalking) return;
        isTalking = false;
        isTyping = false;
        isRepeatOfferDialogue = false;
        hasAcceptedQuest = false;

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        if (dialogueBox != null) dialogueBox.SetActive(false);
        if (acceptMissionButton != null) acceptMissionButton.gameObject.SetActive(false);
        if (notNowButton != null) notNowButton.gameObject.SetActive(false);
        if (continueHint != null) continueHint.gameObject.SetActive(false);

        DeactivateDialogueCamera();

        Animator anim = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.SetBool("IsTalking", false);
        }

        IntroSequenceController intro = IntroSequenceController.Instance != null
            ? IntroSequenceController.Instance
            : FindFirstObjectByType<IntroSequenceController>();

        if (intro != null)
        {
            intro.EndRepeatDialogue(acceptedQuest: false);
        }

        RestorePlayerControls();
    }

    public void EndDialogue()
    {
        if (!isTalking) return;
        bool wasRepeatOffer = isRepeatOfferDialogue;
        isTalking = false;
        isTyping = false;
        isRepeatOfferDialogue = false;
        hasAcceptedQuest = true;

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        // Step A: Close UI_Dialogue cleanly
        if (dialogueBox != null) dialogueBox.SetActive(false);
        if (acceptMissionButton != null) acceptMissionButton.gameObject.SetActive(false);
        if (notNowButton != null) notNowButton.gameObject.SetActive(false);
        if (continueHint != null) continueHint.gameObject.SetActive(false);

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

        // First-time quest: keep player controls frozen and mouse cursor unlocked during handoff to Pre-Quiz
        FreezePlayerControls();

        OnDialogueEnded?.Invoke();
        OnDialogueEnd?.Invoke();

        // Step C & D: Ensure IntroSequenceController runs the sequential Pre-Quiz -> 3 Bin Cards transition
        if (intro != null)
        {
            intro.HandleDialogueEnded();
        }
        else
        {
            QuizManager qm = QuizManager.Instance != null ? QuizManager.Instance : FindFirstObjectByType<QuizManager>();
            if (qm != null && !qm.IsPreQuizCompleted)
            {
                qm.StartPreQuiz(() =>
                {
                    if (tutorialManager != null)
                    {
                        tutorialManager.ShowTutorial(0);
                    }
                    else
                    {
                        RestorePlayerControls();
                        if (trashContainer != null) trashContainer.SetActive(true);
                        if (questCanvas != null) questCanvas.SetActive(true);
                        MinimapIcon.SetTrashIconsActive(true);
                    }
                });
            }
            else if (tutorialManager != null)
            {
                tutorialManager.ShowTutorial(0);
            }
            else
            {
                RestorePlayerControls();
                if (trashContainer != null) trashContainer.SetActive(true);
                if (questCanvas != null) questCanvas.SetActive(true);
                MinimapIcon.SetTrashIconsActive(true);
            }
        }
    }

    private void ActivateDialogueCamera()
    {
        EnsureCameraPointSetup();

        Camera mainCam = Camera.main;
        bool usedCinemachine = false;

        if (mainCam != null)
        {
            int hfLayer = LayerMask.NameToLayer("HiddenFromFirstPerson");
            if (hfLayer != -1) mainCam.cullingMask |= (1 << hfLayer);
            Transform vmCam = mainCam.transform.Find("ViewmodelCamera");
            if (vmCam != null) vmCam.gameObject.SetActive(false);

            CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
            if (brain != null && dialogueCameraPoint != null)
            {
                brain.IgnoreTimeScale = true;
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, transitionTime);
                CinemachineCamera cmCam = dialogueCameraPoint.GetComponent<CinemachineCamera>();
                if (cmCam != null)
                {
                    cmCam.Lens.FieldOfView = dialogueFOV;
                    cmCam.Priority.Value = 30; // Blend into dialogue shot
                    usedCinemachine = true;
                }
            }
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

    private void DeactivateDialogueCamera()
    {
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
            int hfLayer = LayerMask.NameToLayer("HiddenFromFirstPerson");
            if (hfLayer != -1) mainCam.cullingMask &= ~(1 << hfLayer);
            Transform vmCam = mainCam.transform.Find("ViewmodelCamera");
            if (vmCam != null) vmCam.gameObject.SetActive(false);
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
                cameraBlendCoroutine = StartCoroutine(LerpCamera(mainCam, mainCam.transform.position, mainCam.transform.rotation, mainCam.fieldOfView,
                    originalCamPos, originalCamRot, originalCamFOV, transitionTime));
            }
        }
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
            inputs.cursorInputForLook = false;
            inputs.cursorLocked = false;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (CenterScreenCursor.Instance != null)
        {
            CenterScreenCursor.Instance.SetVisible(false);
        }
    }

    private void RestorePlayerControls()
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
        if (inputs != null)
        {
            inputs.cursorInputForLook = true;
            inputs.cursorLocked = true;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (CenterScreenCursor.Instance != null)
        {
            CenterScreenCursor.Instance.SetVisible(true);
        }
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
            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(startRot, targetRot, elapsed / duration);
            yield return null;
        }
        transform.rotation = targetRot;
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
            if (interactPrompt != null) interactPrompt.SetActive(false);
            if (isTalking)
            {
                if (isRepeatOfferDialogue) DeclineRepeatMission();
                else EndDialogue();
            }
            if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        }
    }

    private void OnDisable()
    {
        if (isTalking)
        {
            if (isRepeatOfferDialogue) DeclineRepeatMission();
            else EndDialogue();
        }
    }

    private void OnDestroy()
    {
        if (isTalking)
        {
            if (isRepeatOfferDialogue) DeclineRepeatMission();
            else EndDialogue();
        }
    }
}