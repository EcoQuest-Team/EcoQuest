using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Cinemachine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum LitterbugQuestState
{
    Available = 0,
    Active = 1,
    ReadyToTurnIn = 2,
    Completed = 3
}

public class LitterbugQuestManager : MonoBehaviour
{
    public const int TargetCount = 5;
    public const int CategoryCount = 5;
    public const int BaseCoinReward = 150;
    public const int BonusPerRound = 25;
    public const int MaxBonusCoins = 100;
    public const float MinDistBetweenPicks = 40f;
    public const float MinDistFromPlayer = 25f;
    public const int MaxCharsPerPage = 95;

    public static bool IsDialogueOpen { get; private set; }
    private static LitterbugQuestManager instance;
    public static LitterbugQuestManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<LitterbugQuestManager>(FindObjectsInactive.Include);
            }
            return instance;
        }
        private set => instance = value;
    }

    public static void ResetStaticState()
    {
        IsDialogueOpen = false;
        LitterProp.ShrinkAllActiveWorldLitter();
    }

    [Header("Scene References (Set Quest Giver & Sprites in Inspector)")]
    [SerializeField] private Transform questGiverTransform;
    [SerializeField] private Animator questGiverAnimator;
    [SerializeField] private NpcSpawner npcSpawner;
    [SerializeField] private PlayerCurrency playerCurrency;
    [SerializeField] private LitterbugMinimapTracker minimapTracker;
    [SerializeField] private CinemachineCamera dialogueVirtualCam;

    [Header("15 Litter Types Catalog")]
    [SerializeField] private LitterCatalog litterCatalog;

    [Header("Radii (Inspector Configurable)")]
    [SerializeField] private float questGiverInteractRadius = 3.5f;
    [SerializeField] private float litterbugTriggerRadius = 20f;
    [SerializeField] private float litterbugTriggerExitBuffer = 2f;
    [SerializeField] private float litterbugInteractRadius = 2.5f;

    [Header("Sprites (Assigned in Inspector — Never Application.dataPath)")]
    [SerializeField] private Sprite exclamationSprite;
    [SerializeField] private Sprite greenCheckSprite;
    [SerializeField] private Sprite edgeArrowSprite;

    [Header("Optional Throw Animation Override")]
    [SerializeField] private AnimationClip optionalThrowClip;

    [Header("HUD & Dialogue UI (ScaleWithScreenSize 1920x1080, match 0.5)")]
    [SerializeField] private CanvasGroup questPanelGroup;
    [SerializeField] private RectTransform questPanelRect;
    [SerializeField] private TextMeshProUGUI roundTitleText;
    [SerializeField] private TextMeshProUGUI questCounterText;
    [SerializeField] private RectTransform questCounterRect;
    [SerializeField] private Image cleanlinessFillImage;
    [SerializeField] private TextMeshProUGUI cleanlinessPercentText;
    [SerializeField] private TextMeshProUGUI distanceHintText;
    [SerializeField] private GameObject interactPromptRoot;
    [SerializeField] private TextMeshProUGUI interactPromptText;

    [SerializeField] private CanvasGroup dialoguePanelGroup;
    [SerializeField] private RectTransform dialoguePanelRect;
    [SerializeField] private TextMeshProUGUI speakerNameText;
    [SerializeField] private RectTransform speakerTabRect;
    [SerializeField] private Image speakerPillImage;
    [SerializeField] private Sprite speakerPillGreenSprite;
    [SerializeField] private Sprite speakerPillTealSprite;
    [SerializeField] private TextMeshProUGUI dialogueBodyText;
    [SerializeField] private TextMeshProUGUI continueHintText;
    [SerializeField] private RectTransform continueKeycapRect;
    [SerializeField] private Button acceptQuestButton;
    [SerializeField] private GameObject choiceButtonsRoot;
    [SerializeField] private Button notNowButton;
    [SerializeField] private GameObject continuePromptRoot;
    [SerializeField] private KeyCapPrompt continueKeyCapPrompt;
    [SerializeField] private KeyCapPrompt acceptKeyCapPrompt;
    [SerializeField] private KeyCapPrompt notNowKeyCapPrompt;
    [SerializeField] private float advanceDelay = 0.25f;
    [SerializeField] private GameObject dialogueNextTriangle;
    [SerializeField] private RectTransform topLetterboxBar;
    [SerializeField] private RectTransform bottomLetterboxBar;
    [SerializeField] private RectTransform distanceArrowRect;

    private float advanceLockTimer = 0f;
    private NpcDialogueSpeakerAnimator activeSpeakerAnimator;
    private Coroutine letterboxRoutine;
    private Coroutine hudFadeRoutine;
    private Vector3 triangleBaseLocalPos;

    [Header("Null-Safe Audio Hooks")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip throwClip;
    [SerializeField] private AudioClip landClip;
    [SerializeField] private AudioClip dialogueBlipClip;
    [SerializeField] private AudioClip coinClip;

    private struct TargetSlot
    {
        public LitterTypeEntry entry;
        public NpcBrain brain;
        public LitterbugNpc litterbug;
        public bool persuaded;
    }

    public struct DialoguePage
    {
        public string speaker;
        public string text;
    }

    private enum DialogueMode
    {
        None = 0,
        QuestGiverOffer = 1,
        QuestGiverInProgress = 2,
        LitterbugPersuade = 3,
        QuestGiverTurnIn = 4,
        QuestGiverConflict = 5
    }

    // 3 randomized intro variants (DESIGN 1)
    private static readonly string[] GuardianIntroVariants =
    {
        "Five citizens around the city are tossing trash right onto the sidewalks. Will you enlighten them and help clean up?",
        "Careless littering is cropping up across five blocks! Can you track down the litterbugs and explain where each item belongs?",
        "Our streets need an Eco-Guardian—five passersby are dropping waste without a second thought. Ready to talk some sense into them?"
    };

    private static readonly string[] InProgressLines =
    {
        "Keep going! Check your minimap to find and enlighten all five litterbugs, then return here."
    };

    private static readonly string[] TurnInLines =
    {
        "Fantastic work! Every single litterbug learned how to sort their waste properly.",
        "Here is your Eco-Coin reward. Talk to me anytime if you're up for another round!"
    };

    private readonly TargetSlot[] targetSlots = new TargetSlot[TargetCount];
    private readonly HashSet<NpcBrain> lastRoundNpcs = new HashSet<NpcBrain>();
    private readonly Queue<int>[] categoryShuffleBags = new Queue<int>[CategoryCount];
    private readonly int[] lastCategoryPickId = { -1, -1, -1, -1, -1 };
    private System.Random sessionRng;

    private Transform playerTransform;
    private Camera mainCam;
    private StarterAssets.FirstPersonController cachedFps;
    private StarterAssets.StarterAssetsInputs cachedInputs;
    private bool playerControlsLocked;

    private Transform giverMarkerRoot;
    private SpriteRenderer giverMarkerRenderer;

    private LitterbugQuestState questState = LitterbugQuestState.Available;
    private int currentRound; // 1-based once Round 1 starts
    private int completedRounds;
    private int persuadedCount;
    private float displayedCleanliness;
    private float targetCleanliness;
    private int lastDisplayedDistanceMeters = -999;
    private bool lastWasReturnHint;
    private Vector2 questPanelShownAnchoredPos;
    private bool capturedPanelPos;

    private DialogueMode activeDialogueMode = DialogueMode.None;
    private int activeSlotIndex = -1;
    private Transform activeSpeakerTransform;
    private readonly List<DialoguePage> activePages = new List<DialoguePage>(12);
    private int currentPageIndex;
    private bool isTypingPage;
    private int lastInputFrame = -10;
    private float nextAllowedInputTime;
    private float npcInteractCooldownUntil = 0f;

    private Coroutine typewriterCoroutine;
    private Coroutine dialogueFadeCoroutine;
    private Coroutine counterPunchCoroutine;
    private Coroutine panelAnimCoroutine;
    private Coroutine speakerPunchCoroutine;
    private float baseDialoguePosY = 40f;
    private bool capturedDialoguePos;
    private int savedCameraCullingMask;
    private bool cameraCullingMaskModified;
    private GameObject hiddenCarriedItem;

    public static event System.Action OnCityQuestStateChanged;

    private bool lastGuardianCooldownActive;
    private float nextCooldownCheckTime;

    public LitterbugQuestState QuestState => questState;
    public bool IsPanelAnimating => panelAnimCoroutine != null;
    public int CurrentRound => currentRound;
    public Transform PlayerTransform => playerTransform;
    public Transform QuestGiverTransform => questGiverTransform;
    // Fountain Guardian's city quest is optional; only show the guide arrow when returning to turn in an accepted round.
    public bool IsQuestGiverReadyForArrow => questState == LitterbugQuestState.ReadyToTurnIn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLitterbugEvents()
    {
        OnCityQuestStateChanged = null;
    }
    public float QuestGiverInteractRadius => questGiverInteractRadius;
    public float AdvanceLockTimer => advanceLockTimer;
    public bool CanInteractFountain => canInteractFountain;
    public StarterAssets.FirstPersonController CachedFps => cachedFps;

    private bool canInteractFountain = false;
    public int CompletedRounds => completedRounds;
    public int PersuadedCount => persuadedCount;
    public int ActivePageCount => activePages.Count;

    public NpcBrain GetSlotBrain(int index) => (index >= 0 && index < TargetCount) ? targetSlots[index].brain : null;
    public LitterbugNpc GetSlotLitterbug(int index) => (index >= 0 && index < TargetCount) ? targetSlots[index].litterbug : null;
    public LitterTypeEntry GetSlotEntry(int index) => (index >= 0 && index < TargetCount) ? targetSlots[index].entry : null;

    public static int CalculateRewardForRound(int roundOneBased)
    {
        int bonus = Mathf.Clamp((Mathf.Max(1, roundOneBased) - 1) * BonusPerRound, 0, MaxBonusCoins);
        return BaseCoinReward + bonus;
    }

    private void Awake()
    {
        Instance = this;
        PerformSafetyReset();
        IsDialogueOpen = false;
        questState = LitterbugQuestState.Available;
        currentRound = 0;
        completedRounds = 0;
        persuadedCount = 0;
        sessionRng = new System.Random(Environment.TickCount ^ Guid.NewGuid().GetHashCode());

        for (int c = 0; c < CategoryCount; c++)
            categoryShuffleBags[c] = new Queue<int>(3);

        questPanelShownAnchoredPos = UIOrchestrator.SharedQuestPanelHomePos;
        if (questPanelRect != null)
        {
            questPanelRect.anchorMin = new Vector2(0f, 1f);
            questPanelRect.anchorMax = new Vector2(0f, 1f);
            questPanelRect.pivot = new Vector2(0f, 1f);
            questPanelRect.anchoredPosition = questPanelShownAnchoredPos;
            questPanelRect.sizeDelta = new Vector2(395f, questPanelRect.sizeDelta.y);
            capturedPanelPos = true;
        }
    }

    private void Start()
    {
        CacheSceneReferences();
        PerformSafetyReset();
        InitializeHudDefaults();
        CreateQuestGiverOverheadMarker();

        if (minimapTracker != null)
        {
            minimapTracker.ConfigureSprites(exclamationSprite, greenCheckSprite, edgeArrowSprite);
            minimapTracker.ClearAll();
            minimapTracker.SetFountainMarker(questGiverTransform, visible: true, completedCheck: false);
        }

        lastGuardianCooldownActive = IsOnGuardianCooldown(out _);
        OnCityQuestStateChanged?.Invoke();
        ObjectiveMarkerService.Instance?.RefreshTargetFromState();
    }

    private void CacheSceneReferences()
    {
        mainCam = WorldInteractionPrompt.GetActiveWorldCamera();

        if (cachedFps == null) cachedFps = FindFirstObjectByType<StarterAssets.FirstPersonController>();
        if (cachedFps != null)
        {
            playerTransform = cachedFps.transform;
            cachedInputs = cachedFps.GetComponent<StarterAssets.StarterAssetsInputs>();
        }
        else
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
                cachedFps = playerObj.GetComponent<StarterAssets.FirstPersonController>();
                cachedInputs = playerObj.GetComponent<StarterAssets.StarterAssetsInputs>();
            }
        }

        if (npcSpawner == null) npcSpawner = FindFirstObjectByType<NpcSpawner>();
        if (playerCurrency == null) playerCurrency = FindFirstObjectByType<PlayerCurrency>();
        if (minimapTracker == null) minimapTracker = GetComponent<LitterbugMinimapTracker>();

        if (acceptQuestButton != null)
        {
            acceptQuestButton.onClick.RemoveAllListeners();
            acceptQuestButton.onClick.AddListener(OnAcceptButtonClicked);
        }

        if (dialoguePanelRect == null && dialoguePanelGroup != null)
            dialoguePanelRect = dialoguePanelGroup.GetComponent<RectTransform>();

        if (dialoguePanelRect != null)
        {
            if (choiceButtonsRoot == null)
            {
                Transform cbr = dialoguePanelRect.Find("ChoiceButtonsRoot");
                if (cbr != null) choiceButtonsRoot = cbr.gameObject;
            }
            if (choiceButtonsRoot != null)
            {
                if (acceptQuestButton == null)
                {
                    Transform ab = choiceButtonsRoot.transform.Find("AcceptQuestButton");
                    if (ab != null) acceptQuestButton = ab.GetComponent<Button>();
                }
                if (notNowButton == null)
                {
                    Transform nnb = choiceButtonsRoot.transform.Find("NotNowButton");
                    if (nnb != null) notNowButton = nnb.GetComponent<Button>();
                }

                if (acceptQuestButton != null)
                {
                    acceptQuestButton.onClick.RemoveAllListeners();
                    acceptQuestButton.onClick.AddListener(OnAcceptButtonClicked);
                    acceptQuestButton.gameObject.SetActive(true);
                    if (acceptKeyCapPrompt == null) acceptKeyCapPrompt = acceptQuestButton.GetComponentInChildren<KeyCapPrompt>();
                }
                if (notNowButton != null)
                {
                    notNowButton.onClick.RemoveAllListeners();
                    notNowButton.onClick.AddListener(OnNotNowButtonClicked);
                    notNowButton.gameObject.SetActive(true);
                    if (notNowKeyCapPrompt == null) notNowKeyCapPrompt = notNowButton.GetComponentInChildren<KeyCapPrompt>();
                }
            }

            if (continuePromptRoot == null)
            {
                Transform cpr = dialoguePanelRect.Find("ContinuePromptRoot");
                if (cpr != null) continuePromptRoot = cpr.gameObject;
            }
            if (continuePromptRoot != null && continueKeyCapPrompt == null)
            {
                continueKeyCapPrompt = continuePromptRoot.GetComponent<KeyCapPrompt>();
            }

            if (dialogueNextTriangle == null)
            {
                Transform dnt = dialoguePanelRect.Find("DialogueNextTriangle");
                if (dnt != null) dialogueNextTriangle = dnt.gameObject;
            }
            if (dialogueNextTriangle != null)
            {
                triangleBaseLocalPos = dialogueNextTriangle.transform.localPosition;
                dialogueNextTriangle.SetActive(false);
            }
        }

        if (notNowButton != null)
        {
            notNowButton.onClick.RemoveAllListeners();
            notNowButton.onClick.AddListener(OnNotNowButtonClicked);
        }

        if (acceptQuestButton != null)
        {
            acceptQuestButton.onClick.RemoveAllListeners();
            acceptQuestButton.onClick.AddListener(OnAcceptButtonClicked);
        }

        if (topLetterboxBar == null)
        {
            GameObject tlb = GameObject.Find("TopLetterboxBar");
            if (tlb != null) topLetterboxBar = tlb.GetComponent<RectTransform>();
        }
        if (bottomLetterboxBar == null)
        {
            GameObject blb = GameObject.Find("BottomLetterboxBar");
            if (blb != null) bottomLetterboxBar = blb.GetComponent<RectTransform>();
        }

        if (dialogueVirtualCam != null)
        {
            dialogueVirtualCam.Priority.Value = 0;
            dialogueVirtualCam.gameObject.SetActive(false);
        }

        if (dialoguePanelRect == null && dialoguePanelGroup != null)
            dialoguePanelRect = dialoguePanelGroup.GetComponent<RectTransform>();
        if (dialoguePanelRect != null && !capturedDialoguePos)
        {
            baseDialoguePosY = dialoguePanelRect.anchoredPosition.y;
            capturedDialoguePos = true;
        }
        if (speakerTabRect == null && speakerNameText != null)
            speakerTabRect = speakerNameText.transform.parent as RectTransform;
        if (speakerPillImage == null && speakerTabRect != null)
            speakerPillImage = speakerTabRect.GetComponent<Image>();
        if (distanceArrowRect == null && distanceHintText != null && distanceHintText.transform.parent != null)
        {
            Transform arrowT = distanceHintText.transform.parent.Find("DistanceArrow") ?? distanceHintText.transform.Find("DistanceArrow");
            if (arrowT != null) distanceArrowRect = arrowT as RectTransform;
        }
        if (continueKeycapRect == null && continueHintText != null && continueHintText.transform.parent != null)
        {
            Transform keyT = continueHintText.transform.parent.Find("ContinueKeycap") ?? continueHintText.transform.Find("ContinueKeycap");
            if (keyT != null) continueKeycapRect = keyT as RectTransform;
        }
    }

    private void InitializeHudDefaults()
    {
        if (questPanelGroup != null)
        {
            questPanelGroup.alpha = 0f;
            questPanelGroup.interactable = false;
            questPanelGroup.blocksRaycasts = false;
            questPanelGroup.gameObject.SetActive(false);
        }

        if (dialoguePanelGroup != null)
        {
            dialoguePanelGroup.alpha = 0f;
            dialoguePanelGroup.interactable = false;
            dialoguePanelGroup.blocksRaycasts = false;
            dialoguePanelGroup.gameObject.SetActive(false);
        }

        if (interactPromptRoot != null)
            interactPromptRoot.SetActive(false);

        displayedCleanliness = 0f;
        targetCleanliness = 0f;
        if (cleanlinessFillImage != null) cleanlinessFillImage.fillAmount = 0f;
        if (cleanlinessPercentText != null) cleanlinessPercentText.text = "0%";
        if (roundTitleText != null) roundTitleText.text = "Quest";
        if (questCounterText != null) questCounterText.text = "Enlighten the litterbugs: 0/5";
        if (distanceHintText != null) distanceHintText.text = "";
    }

    private void CreateQuestGiverOverheadMarker()
    {
        if (questGiverTransform == null) return;

        Transform existing = questGiverTransform.Find("FountainQuestOverheadMarker");
        if (existing != null)
        {
            giverMarkerRoot = existing;
            giverMarkerRenderer = existing.GetComponent<SpriteRenderer>();
        }
        else
        {
            GameObject go = new GameObject("FountainQuestOverheadMarker");
            giverMarkerRoot = go.transform;
            giverMarkerRoot.SetParent(questGiverTransform, false);
            giverMarkerRoot.localPosition = new Vector3(0f, 2.25f, 0f);
            giverMarkerRoot.localScale = Vector3.one * 0.26f;

            giverMarkerRenderer = go.AddComponent<SpriteRenderer>();
            giverMarkerRenderer.sprite = exclamationSprite;
            giverMarkerRenderer.sortingOrder = 22;
        }

        if (giverMarkerRenderer != null && giverMarkerRenderer.sprite == null && exclamationSprite != null)
        {
            giverMarkerRenderer.sprite = exclamationSprite;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (lastGuardianCooldownActive && Time.unscaledTime >= nextCooldownCheckTime)
        {
            nextCooldownCheckTime = Time.unscaledTime + 0.5f;
            if (!IsOnGuardianCooldown(out _))
            {
                lastGuardianCooldownActive = false;
                OnCityQuestStateChanged?.Invoke();
            }
        }
        AnimateQuestGiverOverhead();
        AnimateCleanlinessBar(dt);

        if (IsDialogueOpen)
        {
            if (activeSpeakerTransform != null && playerTransform != null)
            {
                Vector3 toPlayer = playerTransform.position - activeSpeakerTransform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.001f)
                {
                    Quaternion rot = Quaternion.LookRotation(toPlayer.normalized);
                    activeSpeakerTransform.rotation = Quaternion.Slerp(activeSpeakerTransform.rotation, rot, dt * 7f);
                }
            }

            if ((questState == LitterbugQuestState.Active || questState == LitterbugQuestState.ReadyToTurnIn) && playerTransform != null)
            {
                Vector3 pPos = playerTransform.position;
                for (int i = 0; i < TargetCount; i++)
                {
                    if (targetSlots[i].persuaded || targetSlots[i].litterbug == null || targetSlots[i].brain == null) continue;
                    float sqr = (targetSlots[i].brain.transform.position - pPos).sqrMagnitude;
                    targetSlots[i].litterbug.TickFromManager(sqr, pPos, i == activeSlotIndex, dt);
                }
            }

            HandleDialogueInput();
            return;
        }

        if (cachedFps == null) cachedFps = FindFirstObjectByType<StarterAssets.FirstPersonController>();
        if (cachedFps != null && playerTransform != cachedFps.transform)
        {
            playerTransform = cachedFps.transform;
        }

        if (playerTransform == null) return;

        bool modalBlocked = PauseMenuController.IsPaused
            || QuizUIController.IsAnyQuizOpen
            || (MinimapController.Instance != null && MinimapController.Instance.IsFullMapOpen);

        Vector3 playerPos = playerTransform.position;
        int interactSlot = -1;
        float bestInteractSqr = float.MaxValue;
        canInteractFountain = false;
        float giverInteractSqr = questGiverInteractRadius * questGiverInteractRadius;

        // 1. Check Fountain Quest Giver squared distance
        if (questGiverTransform != null)
        {
            Vector3 diffGiver = questGiverTransform.position - playerPos;
            diffGiver.y = 0f;
            float giverSqr = diffGiver.sqrMagnitude;

            if (giverSqr <= 12.25f)
            {
                Vector3 toPlayer = -diffGiver;
                if (toPlayer.sqrMagnitude > 0.001f)
                {
                    questGiverTransform.rotation = Quaternion.Slerp(
                        questGiverTransform.rotation,
                        Quaternion.LookRotation(toPlayer.normalized),
                        dt * 5f);
                }
            }

            if (giverSqr <= giverInteractSqr && questState != LitterbugQuestState.Completed && Time.unscaledTime >= npcInteractCooldownUntil)
            {
                canInteractFountain = true;
            }
        }

        // 2. Check ONLY the 5 active target NPCs using squared distance
        float nearestActiveSqr = float.MaxValue;
        Transform nearestActiveTarget = null;
        if (questState == LitterbugQuestState.Active || questState == LitterbugQuestState.ReadyToTurnIn)
        {
            for (int i = 0; i < TargetCount; i++)
            {
                if (targetSlots[i].persuaded) continue;

                if (targetSlots[i].brain == null || !targetSlots[i].brain.gameObject.activeInHierarchy || targetSlots[i].litterbug == null)
                {
                    ReassignLostSlot(i);
                }

                if (targetSlots[i].brain == null || targetSlots[i].litterbug == null) continue;

                Vector3 diff = targetSlots[i].brain.transform.position - playerPos;
                float sqr3D = diff.sqrMagnitude;
                diff.y = 0f;
                float sqrXZ = diff.sqrMagnitude;

                if (sqrXZ < nearestActiveSqr)
                {
                    nearestActiveSqr = sqrXZ;
                    nearestActiveTarget = targetSlots[i].brain.transform;
                }

                targetSlots[i].litterbug.TickFromManager(sqr3D, playerPos, false, dt);

                float slotInterSqr = targetSlots[i].litterbug.InteractRadiusSqr;
                if (Time.unscaledTime >= npcInteractCooldownUntil && targetSlots[i].litterbug.State == LitterbugState.Listening && sqrXZ <= slotInterSqr && sqrXZ < bestInteractSqr)
                {
                    bestInteractSqr = sqrXZ;
                    interactSlot = i;
                }
            }

            UpdateDistanceHintHud(playerPos, nearestActiveSqr, nearestActiveTarget);
        }

        // Continue keycap gentle pulse
        if (continueKeycapRect != null && IsDialogueOpen)
        {
            float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 5f);
            continueKeycapRect.localScale = new Vector3(pulse, pulse, 1f);
        }

        // Bouncing dialogue triangle animation
        if (dialogueNextTriangle != null && dialogueNextTriangle.activeSelf)
        {
            float bob = Mathf.Sin(Time.unscaledTime * 6f) * 3f;
            dialogueNextTriangle.transform.localPosition = new Vector3(triangleBaseLocalPos.x, triangleBaseLocalPos.y + bob, 0f);
        }

        // 3. Interaction prompt & E-key trigger (World-anchored)
        bool showPrompt = !modalBlocked && (interactSlot >= 0 || canInteractFountain);
        if (showPrompt)
        {
            if (interactSlot >= 0 && targetSlots[interactSlot].brain != null)
            {
                Transform t = targetSlots[interactSlot].brain.transform;
                WorldInteractionPrompt.Instance?.ShowPrompt(t, "Litterbug", "Talk", "E");
            }
            else if (canInteractFountain && questGiverTransform != null)
            {
                WorldInteractionPrompt.Instance?.ShowPrompt(questGiverTransform, "Guardian", "Talk", "E");
            }
        }
        else if (WorldInteractionPrompt.Instance != null && WorldInteractionPrompt.Instance.ActiveTarget != null)
        {
            Transform activePromptTarget = WorldInteractionPrompt.Instance.ActiveTarget;
            bool isOwnedByLitterbugQuest = (activePromptTarget == questGiverTransform);
            if (!isOwnedByLitterbugQuest && targetSlots != null)
            {
                for (int i = 0; i < targetSlots.Length; i++)
                {
                    if (targetSlots[i].brain != null && targetSlots[i].brain.transform == activePromptTarget)
                    {
                        isOwnedByLitterbugQuest = true;
                        break;
                    }
                }
            }
            if (isOwnedByLitterbugQuest)
            {
                WorldInteractionPrompt.Instance.HidePrompt();
            }
        }

        if (interactPromptRoot != null && interactPromptRoot.activeSelf)
            interactPromptRoot.SetActive(false);

        if (showPrompt && CanConsumeEKeyThisFrame() && IsEKeyPressedThisFrame())
        {
            ConsumeInputCooldown(0.20f);
            if (interactSlot >= 0)
            {
                OpenLitterbugDialogue(interactSlot);
            }
            else if (canInteractFountain)
            {
                OpenQuestGiverDialogue();
            }
        }
    }

    private void UpdateDistanceHintHud(Vector3 playerPos, float nearestLitterbugSqr, Transform nearestActiveTarget)
    {
        if (distanceHintText == null) return;

        bool returnPhase = questState == LitterbugQuestState.ReadyToTurnIn;
        float distMeters = 0f;
        Vector3 targetPos = Vector3.zero;
        bool hasTarget = false;

        if (returnPhase)
        {
            if (questGiverTransform != null)
            {
                targetPos = questGiverTransform.position;
                Vector3 d = targetPos - playerPos;
                d.y = 0f;
                distMeters = Mathf.Sqrt(d.sqrMagnitude);
                hasTarget = true;
            }
        }
        else if (nearestLitterbugSqr < float.MaxValue * 0.5f)
        {
            distMeters = Mathf.Sqrt(nearestLitterbugSqr);
            if (nearestActiveTarget != null)
            {
                targetPos = nearestActiveTarget.position;
                hasTarget = true;
            }
        }

        int rounded = Mathf.RoundToInt(distMeters);
        if (rounded != lastDisplayedDistanceMeters || returnPhase != lastWasReturnHint)
        {
            lastDisplayedDistanceMeters = rounded;
            lastWasReturnHint = returnPhase;
            distanceHintText.text = returnPhase
                ? $"Guardian: {rounded}m"
                : $"Nearest litterbug: {rounded}m";
        }

        if (distanceArrowRect != null && hasTarget && mainCam != null)
        {
            Vector3 toTarget = targetPos - playerPos;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.05f)
            {
                Vector3 camFwd = mainCam.transform.forward;
                camFwd.y = 0f;
                camFwd.Normalize();
                float angle = Vector3.SignedAngle(camFwd, toTarget, Vector3.up);
                distanceArrowRect.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }
        }
    }

    private void AnimateCleanlinessBar(float dt)
    {
        if (questState == LitterbugQuestState.Available) return;
        if (Mathf.Abs(displayedCleanliness - targetCleanliness) > 0.0005f)
        {
            displayedCleanliness = Mathf.MoveTowards(displayedCleanliness, targetCleanliness, dt / 0.4f);
            if (cleanlinessFillImage != null)
                cleanlinessFillImage.fillAmount = displayedCleanliness;
            if (cleanlinessPercentText != null)
                cleanlinessPercentText.text = $"{Mathf.RoundToInt(displayedCleanliness * 100f)}%";
        }
    }

    private void AnimateQuestGiverOverhead()
    {
        if (giverMarkerRoot == null) return;

        float distToPlayer = (playerTransform != null && questGiverTransform != null)
            ? Vector3.Distance(questGiverTransform.position, playerTransform.position)
            : 999f;

        // Hide marker during dialogue, blocking UI, cooldown, when not available/ready, or within InteractRadius + 1.0m
        bool onCooldown = questState == LitterbugQuestState.Available && IsOnGuardianCooldown(out _);
        bool shouldHide = IsDialogueOpen
            || CursorState.IsAnyBlockingUIOpen()
            || onCooldown
            || (questState != LitterbugQuestState.Available && questState != LitterbugQuestState.ReadyToTurnIn)
            || (distToPlayer <= (questGiverInteractRadius + 1.0f));

        if (shouldHide)
        {
            if (giverMarkerRoot.gameObject.activeSelf)
                giverMarkerRoot.gameObject.SetActive(false);
            return;
        }

        if (!giverMarkerRoot.gameObject.activeSelf)
            giverMarkerRoot.gameObject.SetActive(true);

        if (giverMarkerRenderer != null && giverMarkerRenderer.sprite != exclamationSprite)
            giverMarkerRenderer.sprite = exclamationSprite;

        float bob = Mathf.Sin(Time.time * 2.8f) * 0.07f;
        giverMarkerRoot.localPosition = new Vector3(0f, 2.25f + bob, 0f);

        // Scale with distance: 0.5 to 1.0, capped at ~60% base scale at close range
        float distFactor = Mathf.Clamp01((distToPlayer - 4f) / 16f);
        float distScale = Mathf.Lerp(0.60f, 1.0f, distFactor);
        giverMarkerRoot.localScale = Vector3.one * (0.24f * distScale);

        if (mainCam != null)
        {
            Vector3 toCam = giverMarkerRoot.position - mainCam.transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.001f)
                giverMarkerRoot.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        }
    }

    // =====================================================================
    // ROUND LIFECYCLE, SHUFFLE BAGS & ASSIGNMENT (DESIGN 1, 2, 6, 8)
    // =====================================================================

    private void CleanupPreviousRoundState()
    {
        lastRoundNpcs.Clear();
        for (int i = 0; i < TargetCount; i++)
        {
            if (targetSlots[i].brain != null)
                lastRoundNpcs.Add(targetSlots[i].brain);

            if (targetSlots[i].litterbug != null)
            {
                targetSlots[i].litterbug.ReleaseForNewRound();
            }
            else if (targetSlots[i].brain != null)
            {
                var lb = targetSlots[i].brain.GetComponent<LitterbugNpc>();
                if (lb != null) lb.ReleaseForNewRound();
                else if (targetSlots[i].brain.IsQuestTarget) targetSlots[i].brain.ReleaseFromQuestTarget();
            }

            targetSlots[i].brain = null;
            targetSlots[i].litterbug = null;
            targetSlots[i].entry = null;
            targetSlots[i].persuaded = false;
        }

        // Clear any leftover world litter with shrink + sparkle effect
        LitterProp.ShrinkAllActiveWorldLitter();

        if (minimapTracker != null)
            minimapTracker.ClearAll();
    }

    public void StartNewRound()
    {
        CleanupPreviousRoundState();

        currentRound = completedRounds + 1;
        persuadedCount = 0;
        questState = LitterbugQuestState.Active;
        OnCityQuestStateChanged?.Invoke();
        displayedCleanliness = 0f;
        targetCleanliness = 0f;
        lastDisplayedDistanceMeters = -999;

        if (cleanlinessFillImage != null) cleanlinessFillImage.fillAmount = 0f;
        if (cleanlinessPercentText != null) cleanlinessPercentText.text = "0%";
        if (roundTitleText != null) roundTitleText.text = "Quest";
        if (questCounterText != null) questCounterText.text = "Enlighten the litterbugs: 0/5";
        if (questCounterRect != null) questCounterRect.localScale = Vector3.one;
        if (distanceHintText != null) distanceHintText.text = "";

        // Draw 1 random type per category using per-category shuffle bags (all 3 appear before any repeat)
        LitterTypeEntry[] roundEntries = DrawFiveCategoryEntries();
        ShuffleArray(roundEntries, EnsureSessionRng());

        List<NpcBrain> chosen = PickValidWalkingNpcsForRound(TargetCount);

        if (minimapTracker != null)
            minimapTracker.SetFountainMarker(questGiverTransform, visible: false);

        for (int i = 0; i < TargetCount; i++)
        {
            targetSlots[i].entry = roundEntries[i];
            targetSlots[i].persuaded = false;

            if (i < chosen.Count && chosen[i] != null)
            {
                BindNpcToSlot(i, chosen[i]);
            }
            else
            {
                ReassignLostSlot(i);
            }
        }

        // Reset alpha & state on re-enter, then slide/fade Quest Panel in
        UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.QuestActive, "City quest started");
        UIOrchestrator.Instance?.SetQuestPanelUserHidden(false, "LitterbugQuestManager");
        UIOrchestrator.Instance?.ApplyCurrentProfile(0.35f);

        if (panelAnimCoroutine != null) StopCoroutine(panelAnimCoroutine);
        panelAnimCoroutine = StartCoroutine(SlideInQuestPanelRoutine());

        TravelGate.RunPostTravelHealthCheck(out string reportQuestStart);
        Debug.Log($"[Post-Travel Health Check - QuestStart] {reportQuestStart}");
    }

    private System.Random EnsureSessionRng()
    {
        if (sessionRng == null)
            sessionRng = new System.Random(Environment.TickCount ^ Guid.NewGuid().GetHashCode());
        return sessionRng;
    }

    private LitterTypeEntry[] DrawFiveCategoryEntries()
    {
        var result = new LitterTypeEntry[CategoryCount];
        if (litterCatalog == null || litterCatalog.entries == null || litterCatalog.entries.Count == 0)
            return result;

        EnsureSessionRng();

        for (int catInt = 0; catInt < CategoryCount; catInt++)
        {
            LitterCategory cat = (LitterCategory)catInt;
            if (categoryShuffleBags[catInt] == null)
                categoryShuffleBags[catInt] = new Queue<int>(3);
            Queue<int> bag = categoryShuffleBags[catInt];

            if (bag.Count == 0)
            {
                List<int> catIndices = new List<int>(3);
                for (int i = 0; i < litterCatalog.entries.Count; i++)
                {
                    if (litterCatalog.entries[i] != null && litterCatalog.entries[i].category == cat)
                        catIndices.Add(i);
                }

                ShuffleList(catIndices, sessionRng);
                if (catIndices.Count > 1 && litterCatalog.entries[catIndices[0]].id == lastCategoryPickId[catInt])
                    (catIndices[0], catIndices[catIndices.Count - 1]) = (catIndices[catIndices.Count - 1], catIndices[0]);
                for (int i = 0; i < catIndices.Count; i++)
                    bag.Enqueue(catIndices[i]);
            }

            if (bag.Count > 0)
            {
                int entryIdx = bag.Dequeue();
                result[catInt] = litterCatalog.entries[entryIdx];
                lastCategoryPickId[catInt] = result[catInt].id;
            }
        }

        return result;
    }

    private List<NpcBrain> PickValidWalkingNpcsForRound(int count)
    {
        List<NpcBrain> freshPool = GatherEligiblePool(excludeLastRound: true);
        List<NpcBrain> fullPool = GatherEligiblePool(excludeLastRound: false);

        Vector3 playerPos = playerTransform != null
            ? playerTransform.position
            : (questGiverTransform != null ? questGiverTransform.position : Vector3.zero);

        // 1. Try strict (40m apart, 25m player) using ONLY NPCs not in last round
        if (freshPool.Count >= count)
        {
            List<NpcBrain> strictFresh = TryPickSubset(freshPool, count, MinDistBetweenPicks, MinDistFromPlayer, playerPos, 64);
            if (strictFresh.Count == count)
                return strictFresh;
        }

        // 2. Build as many strict picks as possible from freshPool, then top up from lastRoundNpcs if pool is too tight for 40m geometry
        List<NpcBrain> combinedStrict = TryPickWithFallbackReuse(freshPool, fullPool, count, MinDistBetweenPicks, MinDistFromPlayer, playerPos, 64);
        if (combinedStrict.Count == count)
            return combinedStrict;

        // 3. Safety relaxation tiers so 5 targets are always assigned
        float[,] fallbackTiers = { { 32f, 22f }, { 20f, 15f }, { 0f, 0f } };
        for (int t = 0; t < fallbackTiers.GetLength(0); t++)
        {
            List<NpcBrain> relaxed = TryPickWithFallbackReuse(freshPool, fullPool, count, fallbackTiers[t, 0], fallbackTiers[t, 1], playerPos, 24);
            if (relaxed.Count == count)
                return relaxed;
        }

        return TryPickSubset(fullPool, count, 0f, 0f, playerPos, 1);
    }

    private List<NpcBrain> TryPickSubset(List<NpcBrain> pool, int count, float minBetween, float minPlayer, Vector3 playerPos, int attempts)
    {
        float minBetweenSqr = minBetween * minBetween;
        float minPlayerSqr = minPlayer * minPlayer;
        List<NpcBrain> best = new List<NpcBrain>(count);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            ShuffleList(pool, EnsureSessionRng());
            List<NpcBrain> picked = new List<NpcBrain>(count);

            for (int i = 0; i < pool.Count && picked.Count < count; i++)
            {
                if (CanAddCandidate(pool[i], picked, playerPos, minBetweenSqr, minPlayerSqr))
                    picked.Add(pool[i]);
            }

            if (picked.Count == count) return picked;
            if (picked.Count > best.Count) best = picked;
        }
        return best;
    }

    private List<NpcBrain> TryPickWithFallbackReuse(List<NpcBrain> freshPool, List<NpcBrain> fullPool, int count, float minBetween, float minPlayer, Vector3 playerPos, int attempts)
    {
        float minBetweenSqr = minBetween * minBetween;
        float minPlayerSqr = minPlayer * minPlayer;
        List<NpcBrain> best = new List<NpcBrain>(count);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            var rng = EnsureSessionRng();
            ShuffleList(freshPool, rng);
            ShuffleList(fullPool, rng);
            List<NpcBrain> picked = new List<NpcBrain>(count);

            for (int i = 0; i < freshPool.Count && picked.Count < count; i++)
            {
                if (CanAddCandidate(freshPool[i], picked, playerPos, minBetweenSqr, minPlayerSqr))
                    picked.Add(freshPool[i]);
            }

            for (int i = 0; i < fullPool.Count && picked.Count < count; i++)
            {
                if (!picked.Contains(fullPool[i]) && CanAddCandidate(fullPool[i], picked, playerPos, minBetweenSqr, minPlayerSqr))
                    picked.Add(fullPool[i]);
            }

            if (picked.Count == count) return picked;
            if (picked.Count > best.Count) best = picked;
        }
        return best;
    }

    private static bool CanAddCandidate(NpcBrain cand, List<NpcBrain> picked, Vector3 playerPos, float minBetweenSqr, float minPlayerSqr)
    {
        Vector3 pos = cand.transform.position;
        Vector3 dp = pos - playerPos;
        dp.y = 0f;
        if (dp.sqrMagnitude < minPlayerSqr) return false;

        for (int j = 0; j < picked.Count; j++)
        {
            Vector3 dn = pos - picked[j].transform.position;
            dn.y = 0f;
            if (dn.sqrMagnitude < minBetweenSqr) return false;
        }
        return true;
    }

    private List<NpcBrain> GatherEligiblePool(bool excludeLastRound)
    {
        List<NpcBrain> eligible = new List<NpcBrain>();
        if (npcSpawner == null || npcSpawner.SpawnedNpcs == null) return eligible;

        List<NpcBrain> all = npcSpawner.SpawnedNpcs;
        for (int i = 0; i < all.Count; i++)
        {
            NpcBrain b = all[i];
            if (b == null || !b.gameObject.activeInHierarchy) continue;
            if (b.IsQuestTarget) continue;
            if (b.InConversation) continue;
            if (b.IsOnCrosswalkOrRoad()) continue;
            if (questGiverTransform != null && b.transform == questGiverTransform) continue;
            if (excludeLastRound && lastRoundNpcs.Contains(b)) continue;

            eligible.Add(b);
        }
        return eligible;
    }

    private void BindNpcToSlot(int slotIndex, NpcBrain brain)
    {
        LitterbugNpc lb = brain.GetComponent<LitterbugNpc>();
        if (lb == null) lb = brain.gameObject.AddComponent<LitterbugNpc>();

        LitterTypeEntry entry = targetSlots[slotIndex].entry;
        lb.Initialize(
            this,
            brain,
            entry,
            litterbugTriggerRadius,
            litterbugTriggerExitBuffer,
            litterbugInteractRadius,
            exclamationSprite,
            greenCheckSprite,
            optionalThrowClip,
            mainCam != null ? mainCam.transform : null);

        targetSlots[slotIndex].brain = brain;
        targetSlots[slotIndex].litterbug = lb;

        if (minimapTracker != null)
        {
            minimapTracker.SetSlot(slotIndex, brain.transform, visible: true, completedCheck: false);
        }

        string entryLabel = entry != null ? $"#{entry.id} {entry.displayName} ({entry.category})" : "null";
        Debug.Log($"[LitterbugQuest R{currentRound}] Slot {slotIndex + 1}/5 -> {brain.name} | {entryLabel} | FallbackThrow: {lb.UsesProceduralFallback}");
    }

    public bool ReassignLostSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= TargetCount || targetSlots[slotIndex].persuaded) return false;

        if (targetSlots[slotIndex].litterbug != null)
            targetSlots[slotIndex].litterbug.CleanupIfReassigned();

        targetSlots[slotIndex].brain = null;
        targetSlots[slotIndex].litterbug = null;

        List<NpcBrain> pool = GatherEligiblePool(excludeLastRound: false);
        if (pool.Count == 0) return false;

        Vector3 playerPos = playerTransform != null ? playerTransform.position : Vector3.zero;
        ShuffleList(pool, EnsureSessionRng());

        NpcBrain chosen = null;
        float[] minBetweenTiers = { MinDistBetweenPicks, 25f, 0f };
        float[] minPlayerTiers = { MinDistFromPlayer, 15f, 0f };

        for (int t = 0; t < minBetweenTiers.Length && chosen == null; t++)
        {
            float bSqr = minBetweenTiers[t] * minBetweenTiers[t];
            float pSqr = minPlayerTiers[t] * minPlayerTiers[t];

            for (int i = 0; i < pool.Count; i++)
            {
                NpcBrain cand = pool[i];
                if (cand == null) continue;
                Vector3 pos = cand.transform.position;
                Vector3 dp = pos - playerPos; dp.y = 0f;
                if (dp.sqrMagnitude < pSqr) continue;

                bool conflict = false;
                for (int s = 0; s < TargetCount; s++)
                {
                    if (s == slotIndex || targetSlots[s].persuaded || targetSlots[s].brain == null) continue;
                    Vector3 dn = pos - targetSlots[s].brain.transform.position; dn.y = 0f;
                    if (dn.sqrMagnitude < bSqr) { conflict = true; break; }
                }
                if (!conflict) { chosen = cand; break; }
            }
        }

        if (chosen == null)
        {
            for (int i = 0; i < pool.Count && chosen == null; i++)
                if (pool[i] != null) chosen = pool[i];
        }
        if (chosen == null) return false;
        BindNpcToSlot(slotIndex, chosen);
        return true;
    }

    private static void ShuffleList<T>(List<T> list, System.Random rng)
    {
        if (list == null || list.Count <= 1) return;
        if (rng == null) rng = new System.Random();
        for (int i = 0; i < list.Count; i++)
        {
            int r = rng.Next(i, list.Count);
            (list[i], list[r]) = (list[r], list[i]);
        }
    }

    private static void ShuffleArray<T>(T[] arr, System.Random rng)
    {
        if (arr == null || arr.Length <= 1) return;
        if (rng == null) rng = new System.Random();
        for (int i = 0; i < arr.Length; i++)
        {
            int r = rng.Next(i, arr.Length);
            (arr[i], arr[r]) = (arr[r], arr[i]);
        }
    }

    // =====================================================================
    // DIALOGUE PAGINATION (~110 CHARS), TYPEWRITER & DEBOUNCE (DESIGN 7, 11)
    // =====================================================================

    public static List<DialoguePage> BuildPaginatedDialogue(string[] speakers, string[] lines, int maxChars = MaxCharsPerPage)
    {
        var pages = new List<DialoguePage>((lines != null ? lines.Length : 1) + 2);
        if (lines == null) return pages;

        for (int i = 0; i < lines.Length; i++)
        {
            string speaker = (speakers != null && i < speakers.Length) ? speakers[i] : "NPC";
            string raw = lines[i] ?? string.Empty;
            var subPages = DialoguePresenter.SplitIntoPagesWithFitGuard(raw.Trim(), null, 1154f, 3, 34f);
            foreach (var sp in subPages)
            {
                pages.Add(new DialoguePage { speaker = speaker, text = sp });
            }
        }
        return pages;
    }

    private static void AppendLineAsPages(List<DialoguePage> dest, string speaker, string remaining, int maxChars)
    {
        while (remaining.Length > maxChars)
        {
            int splitAt = FindBestPageSplitIndex(remaining, maxChars);
            string head = remaining.Substring(0, splitAt).TrimEnd();
            dest.Add(new DialoguePage { speaker = speaker, text = head });
            remaining = remaining.Substring(splitAt).TrimStart();
        }

        if (remaining.Length > 0 || dest.Count == 0)
        {
            dest.Add(new DialoguePage { speaker = speaker, text = remaining });
        }
    }

    private static int FindBestPageSplitIndex(string text, int maxChars)
    {
        // Prefer sentence end (. ! ?) within [32..maxChars]
        for (int i = maxChars; i >= 32; i--)
        {
            char prev = text[i - 1];
            if ((prev == '.' || prev == '!' || prev == '?') && char.IsWhiteSpace(text[i]))
                return i;
        }
        // Next prefer comma/clause break within [45..maxChars]
        for (int i = maxChars; i >= 45; i--)
        {
            if (text[i - 1] == ',' && char.IsWhiteSpace(text[i]))
                return i;
        }
        // Fallback: last word space <= maxChars
        for (int i = maxChars; i >= 1; i--)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }
        return maxChars;
    }

    private bool CanConsumeEKeyThisFrame()
    {
        return Time.frameCount > lastInputFrame && Time.unscaledTime >= nextAllowedInputTime;
    }

    public bool IsEKeyPressedThisFrame()
    {
        if (Input.GetKeyDown(KeyCode.E)) return true;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) return true;
        if (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.buttonWest.wasPressedThisFrame)) return true;
#endif
        return false;
    }

    public bool IsQKeyPressedThisFrame()
    {
        if (Input.GetKeyDown(KeyCode.Q)) return true;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame) return true;
        if (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame) return true;
#endif
        return false;
    }

    public bool IsAdvanceOrConfirmPressedThisFrame()
    {
        if (IsEKeyPressedThisFrame() || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
            return true;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) return true;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
#endif
        return false;
    }

    private void ConsumeInputCooldown(float cooldownSeconds = 0.16f)
    {
        lastInputFrame = Time.frameCount;
        nextAllowedInputTime = Time.unscaledTime + cooldownSeconds;
    }

    public bool IsOnGuardianCooldown(out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        string saved = PlayerPrefs.GetString("EcoQuest_GuardianCooldownUntil", string.Empty);
        if (!string.IsNullOrEmpty(saved) && long.TryParse(saved, out long bin))
        {
            try
            {
                DateTime until = DateTime.FromBinary(bin);
                if (DateTime.UtcNow < until)
                {
                    remaining = until - DateTime.UtcNow;
                    return true;
                }
            }
            catch { }
        }
        return false;
    }

    public void OpenQuestGiverDialogue()
    {
        if (Time.unscaledTime < npcInteractCooldownUntil) return;
        if (IsDialogueOpen || questState == LitterbugQuestState.Completed) return;

        bool isRobinQuestBusy = EcoQuest.CityQuest.CityTrashDetectiveManager.Instance != null &&
            (EcoQuest.CityQuest.CityTrashDetectiveManager.Instance.QuestState == EcoQuest.CityQuest.CityDetectiveState.Active ||
             EcoQuest.CityQuest.CityTrashDetectiveManager.Instance.QuestState == EcoQuest.CityQuest.CityDetectiveState.ReadyToTurnIn);

        if (isRobinQuestBusy)
        {
            string conflictLine = "Looks like you're currently working on Ranger Robin's Field Checklist! Finish that investigation first, then return to me for the Litterbug patrol.";
            string[] lines = new string[] { conflictLine };
            string[] speakers = new string[] { "Guardian" };

            StartDialogueSession(DialogueMode.QuestGiverConflict, -1, questGiverTransform, speakers, lines);
            if (questGiverAnimator != null) questGiverAnimator.SetBool("IsTalking", true);
            return;
        }

        if (questState == LitterbugQuestState.Available)
        {
            if (IsOnGuardianCooldown(out TimeSpan cooldownRemaining))
            {
                int mm = (int)cooldownRemaining.TotalMinutes;
                int ss = cooldownRemaining.Seconds;
                string cooldownLine = $"Thank you for keeping our city clean, Explorer! The plaza is currently peaceful and well-monitored. Please check back in {mm:00}:{ss:00} for the next patrol assignment!";
                string[] cdLines = new string[] { cooldownLine };
                string[] cdSpeakers = new string[] { "Guardian" };

                StartDialogueSession(DialogueMode.QuestGiverConflict, -1, questGiverTransform, cdSpeakers, cdLines);
                if (questGiverAnimator != null) questGiverAnimator.SetBool("IsTalking", true);
                return;
            }

            List<string> offerLines = DialogueData.GetCityOffer(TargetCount);
            string[] lines = offerLines.ToArray();
            string[] speakers = new string[lines.Length];
            for (int i = 0; i < speakers.Length; i++) speakers[i] = "Guardian";

            StartDialogueSession(DialogueMode.QuestGiverOffer, -1, questGiverTransform, speakers, lines);
            if (questGiverAnimator != null) questGiverAnimator.SetBool("IsTalking", true);
        }
        else if (questState == LitterbugQuestState.ReadyToTurnIn)
        {
            int roundReward = CalculateRewardForRound(currentRound);
            List<string> turnInLines = DialogueData.GetCityTurnIn(roundReward);
            string[] lines = turnInLines.ToArray();
            string[] speakers = new string[lines.Length];
            for (int i = 0; i < speakers.Length; i++) speakers[i] = "Guardian";

            StartDialogueSession(DialogueMode.QuestGiverTurnIn, -1, questGiverTransform, speakers, lines);
            if (questGiverAnimator != null) questGiverAnimator.SetBool("IsTalking", true);
        }
        else
        {
            List<string> inProgressLines = DialogueData.GetCityInProgress(persuadedCount, TargetCount);
            string[] lines = inProgressLines.ToArray();
            string[] speakers = new string[lines.Length];
            for (int i = 0; i < speakers.Length; i++) speakers[i] = "Guardian";

            StartDialogueSession(DialogueMode.QuestGiverInProgress, -1, questGiverTransform, speakers, lines);
            if (questGiverAnimator != null) questGiverAnimator.SetBool("IsTalking", true);
        }
    }

    public void OpenLitterbugDialogue(int slotIndex)
    {
        if (Time.unscaledTime < npcInteractCooldownUntil) return;
        if (IsDialogueOpen) return;
        if (slotIndex < 0 || slotIndex >= TargetCount) return;
        if (targetSlots[slotIndex].persuaded || targetSlots[slotIndex].litterbug == null || targetSlots[slotIndex].entry == null) return;

        NpcBrain brain = targetSlots[slotIndex].brain;
        if (brain != null) brain.SetTalking(true);

        StartDialogueSession(
            DialogueMode.LitterbugPersuade,
            slotIndex,
            brain != null ? brain.transform : null,
            LitterCatalog.FiveLineSpeakers,
            targetSlots[slotIndex].entry.dialogueLines);
    }

    private void StartDialogueSession(DialogueMode mode, int slotIdx, Transform speaker, string[] speakers, string[] lines)
    {
        IsDialogueOpen = true;
        activeDialogueMode = mode;
        activeSlotIndex = slotIdx;
        activeSpeakerTransform = speaker;

        activePages.Clear();
        activePages.AddRange(BuildPaginatedDialogue(speakers, lines, MaxCharsPerPage));
        currentPageIndex = 0;

        if (interactPromptRoot != null) interactPromptRoot.SetActive(false);
        WorldInteractionPrompt.Instance?.SetBlocked(true);

        HudVisibilityService.Instance?.HideHUD("CityDialogue", 0.25f);
        UIOrchestrator.Instance?.EnterCinematicProfile("CityDialogue");

        if (speaker != null)
        {
            activeSpeakerAnimator = speaker.GetComponent<NpcDialogueSpeakerAnimator>();
            if (activeSpeakerAnimator == null) activeSpeakerAnimator = speaker.gameObject.AddComponent<NpcDialogueSpeakerAnimator>();
            activeSpeakerAnimator.BeginDialogue(playerTransform != null ? playerTransform : (mainCam != null ? mainCam.transform : null), mode == DialogueMode.QuestGiverOffer || mode == DialogueMode.QuestGiverTurnIn);
        }

        if (slotIdx >= 0 && slotIdx < TargetCount && targetSlots[slotIdx].litterbug != null)
        {
            targetSlots[slotIdx].litterbug.SetDialogueActive(true);
        }

        LockPlayerMovement(mode == DialogueMode.QuestGiverOffer);
        EaseCameraTowardSpeaker(speaker);

        if (letterboxRoutine != null) StopCoroutine(letterboxRoutine);
        letterboxRoutine = StartCoroutine(TransitionDialogueEnterRoutine());

        if (dialogueFadeCoroutine != null) StopCoroutine(dialogueFadeCoroutine);
        dialogueFadeCoroutine = StartCoroutine(FadeDialoguePanelRoutine(1f, 0.30f));

        StartAdvanceLock();
        ShowCurrentDialoguePage();
    }

    private void ShowCurrentDialoguePage()
    {
        if (currentPageIndex >= activePages.Count)
        {
            FinishDialogueSession();
            return;
        }

        DialoguePage page = activePages[currentPageIndex];
        if (speakerNameText != null)
        {
            string speaker = page.speaker;
            if (activeDialogueMode == DialogueMode.LitterbugPersuade)
            {
                if (!speaker.Equals("YOU", System.StringComparison.OrdinalIgnoreCase))
                    speaker = "Litterbug";
            }
            else if (activeDialogueMode == DialogueMode.QuestGiverOffer || activeDialogueMode == DialogueMode.QuestGiverTurnIn || activeDialogueMode == DialogueMode.QuestGiverInProgress)
            {
                if (!speaker.Equals("YOU", System.StringComparison.OrdinalIgnoreCase))
                    speaker = "Guardian";
            }

            speakerNameText.text = speaker.ToUpperInvariant();
            bool isPlayer = speaker.Equals("YOU", System.StringComparison.OrdinalIgnoreCase);
            speakerNameText.color = isPlayer
                ? new Color(0.85f, 1.0f, 0.95f, 1f)
                : Color.white;

            if (speakerPillImage != null)
            {
                if (speakerPillTealSprite != null && speakerPillGreenSprite != null)
                {
                    speakerPillImage.sprite = isPlayer ? speakerPillTealSprite : speakerPillGreenSprite;
                }
                else
                {
                    speakerPillImage.color = isPlayer
                        ? new Color(0.18f, 0.55f, 0.46f, 1f)
                        : new Color(0.36f, 0.67f, 0.35f, 1f);
                }
            }
        }

        if (speakerPunchCoroutine != null) StopCoroutine(speakerPunchCoroutine);
        speakerPunchCoroutine = StartCoroutine(PunchSpeakerTabRoutine());

        if (activeSpeakerAnimator != null)
            activeSpeakerAnimator.TriggerReactionBeat(currentPageIndex);

        bool isOfferFinalPage = (activeDialogueMode == DialogueMode.QuestGiverOffer) && (currentPageIndex == activePages.Count - 1);
        if (activeDialogueMode == DialogueMode.QuestGiverOffer)
        {
            AutoFitChoiceButton(acceptQuestButton, acceptKeyCapPrompt, "E", "Accept");
            AutoFitChoiceButton(notNowButton, notNowKeyCapPrompt, "Q", "Close");
        }

        if (choiceButtonsRoot != null)
        {
            choiceButtonsRoot.SetActive(false); // Only shows after typewriter finishes
        }
        else if (acceptQuestButton != null)
        {
            acceptQuestButton.gameObject.SetActive(false);
        }

        if (continuePromptRoot != null)
        {
            continuePromptRoot.SetActive(!isOfferFinalPage);
        }

        if (continueKeyCapPrompt != null)
        {
            string act = (currentPageIndex == activePages.Count - 1)
                ? ((activeDialogueMode == DialogueMode.QuestGiverConflict) ? "Close" : "Finish")
                : "Continue";
            continueKeyCapPrompt.SetPrompt("E", act);
            continueKeyCapPrompt.SetLocked(false);
        }

        if (dialogueNextTriangle != null)
            dialogueNextTriangle.SetActive(false);

        if (typewriterCoroutine != null) StopCoroutine(typewriterCoroutine);
        typewriterCoroutine = StartCoroutine(TypewriterRoutine(page.text));
    }

    private IEnumerator PunchSpeakerTabRoutine()
    {
        if (speakerTabRect == null) yield break;
        float elapsed = 0f;
        float dur = 0.22f;
        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float s = 1f + 0.12f * Mathf.Sin(t * Mathf.PI);
            speakerTabRect.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        speakerTabRect.localScale = Vector3.one;
        speakerPunchCoroutine = null;
    }

    private IEnumerator TypewriterRoutine(string fullText)
    {
        isTypingPage = true;
        if (activeSpeakerAnimator != null) activeSpeakerAnimator.SetTyping(true);
        if (dialogueNextTriangle != null) dialogueNextTriangle.SetActive(false);

        if (dialogueBodyText != null)
        {
            dialogueBodyText.text = fullText;
            dialogueBodyText.maxVisibleCharacters = 0;
        }

        int totalChars = fullText.Length;
        for (int i = 1; i <= totalChars; i++)
        {
            if (dialogueBodyText != null)
                dialogueBodyText.maxVisibleCharacters = i;

            char c = fullText[i - 1];
            if (i % 2 == 1 && !char.IsWhiteSpace(c))
                PlayDialogueBlipAudio();

            if (c == ',' || c == '.' || c == '?' || c == '!')
            {
                if (activeSpeakerAnimator != null) activeSpeakerAnimator.TriggerPunctuationNod();
                yield return new WaitForSecondsRealtime(0.12f);
            }
            else
            {
                yield return new WaitForSecondsRealtime(0.025f); // ~40 chars/s
            }
        }

        if (dialogueBodyText != null)
            dialogueBodyText.maxVisibleCharacters = 99999;

        isTypingPage = false;
        if (activeSpeakerAnimator != null) activeSpeakerAnimator.SetTyping(false);
        bool isOfferFinalPage = (activeDialogueMode == DialogueMode.QuestGiverOffer) && (currentPageIndex == activePages.Count - 1);
        if (isOfferFinalPage)
        {
            AutoFitChoiceButton(acceptQuestButton, acceptKeyCapPrompt, "E", "Accept");
            AutoFitChoiceButton(notNowButton, notNowKeyCapPrompt, "Q", "Close");

            if (choiceButtonsRoot != null)
            {
                choiceButtonsRoot.SetActive(true);
                if (acceptQuestButton != null) acceptQuestButton.gameObject.SetActive(true);
                if (notNowButton != null) notNowButton.gameObject.SetActive(true);
            }
            else if (acceptQuestButton != null)
            {
                acceptQuestButton.gameObject.SetActive(true);
            }
            if (continuePromptRoot != null) continuePromptRoot.SetActive(false);
        }
        else
        {
            if (dialogueNextTriangle != null)
                dialogueNextTriangle.SetActive(false);
        }

        StartAdvanceLock();
        typewriterCoroutine = null;
    }

    public void StepDialogueInput()
    {
        if (!IsDialogueOpen) return;

        if (isTypingPage)
        {
            if (typewriterCoroutine != null)
            {
                StopCoroutine(typewriterCoroutine);
                typewriterCoroutine = null;
            }
            if (dialogueBodyText != null && currentPageIndex < activePages.Count)
            {
                dialogueBodyText.text = activePages[currentPageIndex].text;
                dialogueBodyText.maxVisibleCharacters = 99999;
            }
            isTypingPage = false;
            if (activeSpeakerAnimator != null) activeSpeakerAnimator.SetTyping(false);

            bool isOfferFinal = (activeDialogueMode == DialogueMode.QuestGiverOffer) && (currentPageIndex == activePages.Count - 1);
            if (isOfferFinal)
            {
                AutoFitChoiceButton(acceptQuestButton, acceptKeyCapPrompt, "E", "Accept");
                AutoFitChoiceButton(notNowButton, notNowKeyCapPrompt, "Q", "Close");

                if (choiceButtonsRoot != null)
                {
                    choiceButtonsRoot.SetActive(true);
                    if (acceptQuestButton != null) acceptQuestButton.gameObject.SetActive(true);
                    if (notNowButton != null) notNowButton.gameObject.SetActive(true);
                }
                else if (acceptQuestButton != null)
                {
                    acceptQuestButton.gameObject.SetActive(true);
                }
                if (continuePromptRoot != null) continuePromptRoot.SetActive(false);
            }
            else
            {
                if (dialogueNextTriangle != null) dialogueNextTriangle.SetActive(false);
            }

            StartAdvanceLock();
            return;
        }

        if (advanceLockTimer > 0f) return;

        bool isOfferFinalPage = (activeDialogueMode == DialogueMode.QuestGiverOffer) && (currentPageIndex == activePages.Count - 1);
        if (isOfferFinalPage)
        {
            OnAcceptButtonClicked();
            return;
        }

        currentPageIndex++;
        if (currentPageIndex < activePages.Count)
        {
            ShowCurrentDialoguePage();
        }
        else
        {
            FinishDialogueSession();
        }
    }

    private void HandleDialogueInput()
    {
        if (!CanConsumeEKeyThisFrame()) return;

        if (advanceLockTimer > 0f)
        {
            advanceLockTimer -= Time.unscaledDeltaTime;
            if (advanceLockTimer <= 0f)
            {
                if (continueKeyCapPrompt != null) continueKeyCapPrompt.SetLocked(false);
                if (acceptKeyCapPrompt != null) acceptKeyCapPrompt.SetLocked(false);
                if (notNowKeyCapPrompt != null) notNowKeyCapPrompt.SetLocked(false);
            }
            return;
        }

        bool isOfferFinalPage = (activeDialogueMode == DialogueMode.QuestGiverOffer) && (currentPageIndex == activePages.Count - 1);
        if (isOfferFinalPage && !isTypingPage)
        {
            if (IsQKeyPressedThisFrame() || Input.GetKeyDown(KeyCode.Escape))
            {
                if (Input.GetKeyDown(KeyCode.Escape)) PauseMenuController.NotifyEscapeConsumed();
                ConsumeInputCooldown(0.20f);
                OnNotNowButtonClicked();
                return;
            }

            if (IsEKeyPressedThisFrame() || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                ConsumeInputCooldown(0.20f);
                OnAcceptButtonClicked();
                return;
            }

            // Note: Mouse clicks are handled by the UI button listeners (acceptQuestButton, notNowButton).
            // Do not call IsAdvanceOrConfirmPressedThisFrame() here, as that triggers on any left mouse click
            // anywhere on screen, which would incorrectly accept the quest when clicking Close.
            return;
        }

        if (IsAdvanceOrConfirmPressedThisFrame())
        {
            ConsumeInputCooldown(0.16f);
            StepDialogueInput();
        }
    }

    private void StartAdvanceLock()
    {
        advanceLockTimer = advanceDelay;
        if (continueKeyCapPrompt != null) continueKeyCapPrompt.SetLocked(true);
        if (acceptKeyCapPrompt != null) acceptKeyCapPrompt.SetLocked(true);
        if (notNowKeyCapPrompt != null) notNowKeyCapPrompt.SetLocked(true);
    }

    private void OnNotNowButtonClicked()
    {
        if (!IsDialogueOpen || advanceLockTimer > 0f) return;
        ConsumeInputCooldown(0.20f);
        if (typewriterCoroutine != null)
        {
            StopCoroutine(typewriterCoroutine);
            typewriterCoroutine = null;
        }
        isTypingPage = false;
        CloseDialogueImmediateOrFade();
    }

    private void OnAcceptButtonClicked()
    {
        if (!IsDialogueOpen || activeDialogueMode != DialogueMode.QuestGiverOffer || advanceLockTimer > 0f) return;
        ConsumeInputCooldown(0.20f);
        if (typewriterCoroutine != null)
        {
            StopCoroutine(typewriterCoroutine);
            typewriterCoroutine = null;
        }
        isTypingPage = false;
        FinishDialogueSession();
    }

    private void FinishDialogueSession()
    {
        DialogueMode finishedMode = activeDialogueMode;
        int finishedSlot = activeSlotIndex;

        if (questGiverAnimator != null) questGiverAnimator.SetBool("IsTalking", false);
        if (finishedSlot >= 0 && finishedSlot < TargetCount && targetSlots[finishedSlot].brain != null)
            targetSlots[finishedSlot].brain.SetTalking(false);

        CloseDialogueImmediateOrFade();

        if (finishedMode == DialogueMode.QuestGiverOffer)
        {
            StartNewRound();
        }
        else if (finishedMode == DialogueMode.LitterbugPersuade)
        {
            CompletePersuasionForSlot(finishedSlot);
        }
        else if (finishedMode == DialogueMode.QuestGiverTurnIn)
        {
            ClaimRoundReward();
        }
    }

    private void CompletePersuasionForSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= TargetCount) return;
        if (targetSlots[slotIndex].persuaded) return;

        LitterbugNpc lb = targetSlots[slotIndex].litterbug;
        if (lb == null || !lb.MarkPersuaded()) return;

        targetSlots[slotIndex].persuaded = true;
        persuadedCount = Mathf.Clamp(persuadedCount + 1, 0, TargetCount);
        targetCleanliness = Mathf.Clamp01(persuadedCount * 0.20f);

        if (minimapTracker != null && targetSlots[slotIndex].brain != null)
        {
            minimapTracker.SetSlot(slotIndex, targetSlots[slotIndex].brain.transform, visible: true, completedCheck: true);
        }

        if (persuadedCount >= TargetCount)
        {
            questState = LitterbugQuestState.ReadyToTurnIn;
            if (questCounterText != null)
                questCounterText.text = "Return to the Guardian";

            if (minimapTracker != null)
                minimapTracker.SetFountainMarker(questGiverTransform, visible: true, completedCheck: false);

            OnCityQuestStateChanged?.Invoke();
        }
        else
        {
            if (questCounterText != null)
                questCounterText.text = $"Enlighten the litterbugs: {persuadedCount}/{TargetCount}";
        }

        if (counterPunchCoroutine != null) StopCoroutine(counterPunchCoroutine);
        if (questCounterRect != null && questCounterRect.gameObject.activeInHierarchy)
            counterPunchCoroutine = StartCoroutine(PunchScaleRoutine(questCounterRect));
    }

    private void ClaimRoundReward()
    {
        if (questState != LitterbugQuestState.ReadyToTurnIn) return;

        // State cycle: ReadyToTurnIn -> Completed -> Available (no long cooldown)
        questState = LitterbugQuestState.Completed;
        int reward = CalculateRewardForRound(currentRound);
        completedRounds++;

        if (playerCurrency != null)
            playerCurrency.AddGoldAnimated(reward, 0.95f);

        PlayCoinAudio();
        if (questGiverTransform != null)
            SpawnCoinBurstParticles(questGiverTransform.position + Vector3.up * 1.65f);

        if (questCounterText != null)
            questCounterText.text = $"Complete! +{reward}";
        if (distanceHintText != null)
            distanceHintText.text = "Talk to Guardian for another round";

        if (counterPunchCoroutine != null) StopCoroutine(counterPunchCoroutine);
        if (questCounterRect != null && questCounterRect.gameObject.activeInHierarchy)
            counterPunchCoroutine = StartCoroutine(PunchScaleRoutine(questCounterRect));

        if (panelAnimCoroutine != null) StopCoroutine(panelAnimCoroutine);
        panelAnimCoroutine = StartCoroutine(CompleteHoldAndFadeOutPanelRoutine());

        // Set 10-minute cooldown on repeat quest to balance game economy
        DateTime guardianCooldownUntil = DateTime.UtcNow.AddMinutes(10);
        PlayerPrefs.SetString("EcoQuest_GuardianCooldownUntil", guardianCooldownUntil.ToBinary().ToString());
        PlayerPrefs.Save();

        // Immediately transition Completed -> Available so Guide checks cooldown when talked to
        questState = LitterbugQuestState.Available;
        lastGuardianCooldownActive = true;
        if (minimapTracker != null)
        {
            minimapTracker.ClearAll();
            minimapTracker.SetFountainMarker(questGiverTransform, visible: true, completedCheck: false);
        }
        OnCityQuestStateChanged?.Invoke();
    }

    private void CloseDialogueImmediateOrFade()
    {
        npcInteractCooldownUntil = Time.unscaledTime + 2.0f;
        IsDialogueOpen = false;
        activeDialogueMode = DialogueMode.None;
        activeSlotIndex = -1;
        activeSpeakerTransform = null;
        isTypingPage = false;

        if (typewriterCoroutine != null)
        {
            StopCoroutine(typewriterCoroutine);
            typewriterCoroutine = null;
        }

        if (choiceButtonsRoot != null)
            choiceButtonsRoot.SetActive(false);
        else if (acceptQuestButton != null)
            acceptQuestButton.gameObject.SetActive(false);

        if (continuePromptRoot != null)
            continuePromptRoot.SetActive(false);

        if (dialogueNextTriangle != null)
            dialogueNextTriangle.SetActive(false);

        if (activeSpeakerAnimator != null)
        {
            activeSpeakerAnimator.EndDialogue();
            activeSpeakerAnimator = null;
        }

        for (int i = 0; i < TargetCount; i++)
        {
            if (targetSlots[i].litterbug != null)
                targetSlots[i].litterbug.SetDialogueActive(false);
        }

        WorldInteractionPrompt.Instance?.SetBlocked(false);

        HudVisibilityService.Instance?.ShowHUD("CityDialogue", 0.25f);
        UIOrchestrator.Instance?.ExitCinematicProfile("CityDialogue");

        ReleaseCameraEase();
        UnlockPlayerMovement();

        if (letterboxRoutine != null) StopCoroutine(letterboxRoutine);
        if (isActiveAndEnabled)
            letterboxRoutine = StartCoroutine(TransitionDialogueExitRoutine());

        if (dialogueFadeCoroutine != null) StopCoroutine(dialogueFadeCoroutine);
        if (isActiveAndEnabled && dialoguePanelGroup != null)
            dialogueFadeCoroutine = StartCoroutine(FadeDialoguePanelRoutine(0f, 0.25f));

        TravelGate.RunPostTravelHealthCheck(out string reportDialogueEnd);
        Debug.Log($"[Post-Travel Health Check - DialogueEnd] {reportDialogueEnd}");
    }

    // =====================================================================
    // CAMERA EASE, PLAYER LOCK SAFETY & UI PANEL ANIMATIONS
    // =====================================================================

    private void EaseCameraTowardSpeaker(Transform speaker)
    {
        if (speaker == null) return;
        EcoQuest.Shared.DialogueCameraRig.Instance.EnterDialogue(speaker, playerTransform, 0.6f, "CityDialogue");
    }

    private void ReleaseCameraEase()
    {
        EcoQuest.Shared.DialogueCameraRig.Instance.ExitDialogue("CityDialogue", 0.6f);
        if (cameraCullingMaskModified && mainCam != null)
        {
            mainCam.cullingMask = savedCameraCullingMask;
            cameraCullingMaskModified = false;
        }

        if (hiddenCarriedItem != null)
        {
            hiddenCarriedItem.SetActive(true);
            hiddenCarriedItem = null;
        }

        if (dialogueVirtualCam != null)
        {
            dialogueVirtualCam.Priority.Value = 0;
            dialogueVirtualCam.gameObject.SetActive(false);
        }
    }

    private void LockPlayerMovement(bool unlockMouseForAcceptButton)
    {
        playerControlsLocked = true;

        if (cachedFps != null) cachedFps.enabled = false;
        if (cachedInputs != null)
        {
            cachedInputs.move = Vector2.zero;
            cachedInputs.look = Vector2.zero;
            cachedInputs.sprint = false;
            cachedInputs.jump = false;
            cachedInputs.cursorInputForLook = false;
            cachedInputs.cursorLocked = !unlockMouseForAcceptButton;
        }

        if (unlockMouseForAcceptButton)
        {
            CursorState.SetLocked(false, cachedInputs);
        }
        else
        {
            CursorState.SyncLookAction(cachedInputs);
            if (CenterScreenCursor.Instance != null)
                CenterScreenCursor.Instance.SetVisible(false);
        }
    }

    private void UnlockPlayerMovement()
    {
        playerControlsLocked = false;

        if (cachedFps != null) cachedFps.enabled = true;
        if (cachedInputs != null)
        {
            cachedInputs.move = Vector2.zero;
            cachedInputs.look = Vector2.zero;
        }

#if ENABLE_INPUT_SYSTEM
        var playerInput = cachedFps != null ? cachedFps.GetComponent<PlayerInput>() : FindFirstObjectByType<PlayerInput>();
        if (playerInput != null && playerInput.isActiveAndEnabled && playerInput.inputIsActive && playerInput.actions != null)
        {
            playerInput.SwitchCurrentActionMap("Player");
            playerInput.currentActionMap?.Enable();
        }
#endif

        CursorState.SetLocked(true, cachedInputs);
    }

    private IEnumerator FadeDialoguePanelRoutine(float targetAlpha, float duration)
    {
        if (dialoguePanelGroup == null) yield break;

        RectTransform panelRT = dialoguePanelRect != null ? dialoguePanelRect : dialoguePanelGroup.GetComponent<RectTransform>();
        if (panelRT != null && !capturedDialoguePos)
        {
            baseDialoguePosY = panelRT.anchoredPosition.y;
            capturedDialoguePos = true;
        }

        if (targetAlpha > 0f && !dialoguePanelGroup.gameObject.activeSelf)
        {
            dialoguePanelGroup.gameObject.SetActive(true);
            if (panelRT != null)
                panelRT.anchoredPosition = new Vector2(panelRT.anchoredPosition.x, baseDialoguePosY - 30f);
        }

        float startAlpha = dialoguePanelGroup.alpha;
        float startPosY = panelRT != null ? panelRT.anchoredPosition.y : baseDialoguePosY;
        float endPosY = (targetAlpha > 0f) ? baseDialoguePosY : (baseDialoguePosY - 30f);

        bool interactive = targetAlpha > 0.5f;
        if (!interactive)
        {
            dialoguePanelGroup.interactable = false;
            dialoguePanelGroup.blocksRaycasts = false;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            dialoguePanelGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            if (panelRT != null)
            {
                panelRT.anchoredPosition = new Vector2(panelRT.anchoredPosition.x, Mathf.Lerp(startPosY, endPosY, t));
            }
            yield return null;
        }

        dialoguePanelGroup.alpha = targetAlpha;
        if (panelRT != null)
        {
            panelRT.anchoredPosition = new Vector2(panelRT.anchoredPosition.x, endPosY);
        }
        dialoguePanelGroup.interactable = interactive;
        dialoguePanelGroup.blocksRaycasts = interactive;

        if (targetAlpha <= 0.001f)
            dialoguePanelGroup.gameObject.SetActive(false);

        dialogueFadeCoroutine = null;
    }

    private void SetHudVisibility(bool visible, float duration = 0.25f)
    {
        if (visible)
        {
            HudVisibilityService.Instance?.ShowHUD("CityDialogue", duration);
            UIOrchestrator.Instance?.ApplyCurrentProfile(duration);
        }
        else
        {
            HudVisibilityService.Instance?.HideHUD("CityDialogue", duration);
            UIOrchestrator.Instance?.ApplyCurrentProfile(duration);
        }
    }

    private IEnumerator HudFadeRoutine(bool visible, float duration)
    {
        SetHudVisibility(visible, duration);
        yield break;
    }

    private IEnumerator TransitionDialogueEnterRoutine()
    {
        SetHudVisibility(false, 0.25f);

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
        SetHudVisibility(true, 0.25f);

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

    private IEnumerator SlideInQuestPanelRoutine()
    {
        if (questPanelGroup == null || questPanelRect == null) yield break;
        questPanelShownAnchoredPos = UIOrchestrator.SharedQuestPanelHomePos;
        questPanelRect.anchorMin = new Vector2(0f, 1f);
        questPanelRect.anchorMax = new Vector2(0f, 1f);
        questPanelRect.pivot = new Vector2(0f, 1f);
        questPanelRect.anchoredPosition = questPanelShownAnchoredPos;
        questPanelRect.sizeDelta = new Vector2(395f, questPanelRect.sizeDelta.y);
        capturedPanelPos = true;

        questPanelGroup.gameObject.SetActive(true);
        questPanelGroup.alpha = 0f;
        questPanelGroup.interactable = false;
        questPanelGroup.blocksRaycasts = false;

        float duration = 0.25f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 2.5f);
            questPanelGroup.alpha = ease;
            questPanelRect.anchoredPosition = questPanelShownAnchoredPos;
            yield return null;
        }

        questPanelGroup.alpha = 1f;
        questPanelGroup.interactable = true;
        questPanelGroup.blocksRaycasts = true;
        questPanelRect.anchoredPosition = questPanelShownAnchoredPos;
        panelAnimCoroutine = null;
    }

    private IEnumerator CompleteHoldAndFadeOutPanelRoutine()
    {
        if (questPanelGroup == null) yield break;

        questPanelShownAnchoredPos = UIOrchestrator.SharedQuestPanelHomePos;
        questPanelGroup.gameObject.SetActive(true);
        questPanelGroup.alpha = 1f;
        if (questPanelRect != null)
        {
            questPanelRect.anchorMin = new Vector2(0f, 1f);
            questPanelRect.anchorMax = new Vector2(0f, 1f);
            questPanelRect.pivot = new Vector2(0f, 1f);
            questPanelRect.anchoredPosition = questPanelShownAnchoredPos;
            questPanelRect.sizeDelta = new Vector2(395f, questPanelRect.sizeDelta.y);
        }

        // Show "Complete! +reward" for 2s, then fade out over 0.6s and disable (DESIGN 8)
        yield return new WaitForSecondsRealtime(2.0f);

        float duration = 0.6f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            questPanelGroup.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        questPanelGroup.alpha = 0f;
        questPanelGroup.gameObject.SetActive(false);
        panelAnimCoroutine = null;
        UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.FreePlay, "City quest completed and rewarded");
    }

    private IEnumerator PunchScaleRoutine(RectTransform targetRect)
    {
        Vector3 baseScale = Vector3.one;
        float duration = 0.28f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float punch = 1f + 0.30f * Mathf.Sin(t * Mathf.PI);
            if (targetRect != null) targetRect.localScale = baseScale * punch;
            yield return null;
        }
        if (targetRect != null) targetRect.localScale = baseScale;
        counterPunchCoroutine = null;
    }

    private static void SpawnCoinBurstParticles(Vector3 worldPos)
    {
        GameObject fxGo = new GameObject("LitterbugCoinBurstFX");
        fxGo.transform.position = worldPos;

        ParticleSystem ps = fxGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1.0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.75f, 1.25f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 4.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.16f);
        main.gravityModifier = 1.15f;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.86f, 0.15f, 1f),
            new Color(1f, 0.96f, 0.55f, 1f));

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 38f;
        shape.radius = 0.18f;
        fxGo.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        var psr = fxGo.GetComponent<ParticleSystemRenderer>();
        Material mat = LitterProp.GetSharedParticleMaterial();
        if (mat != null) psr.sharedMaterial = mat;

        ps.Play();
        Destroy(fxGo, 1.8f);
    }

    // =====================================================================
    // NULL-SAFE AUDIO HOOKS & SELF-CHECK
    // =====================================================================

    public void PlayThrowAudio()
    {
        if (sfxSource != null && throwClip != null)
            sfxSource.PlayOneShot(throwClip, 0.8f);
    }

    public void PlayLandAudio()
    {
        if (sfxSource != null && landClip != null)
            sfxSource.PlayOneShot(landClip, 0.75f);
    }

    public void PlayDialogueBlipAudio()
    {
        if (sfxSource != null && dialogueBlipClip != null)
        {
            sfxSource.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            sfxSource.PlayOneShot(dialogueBlipClip, 0.45f);
        }
    }

    public void PlayCoinAudio()
    {
        if (sfxSource != null && coinClip != null)
            sfxSource.PlayOneShot(coinClip, 0.95f);
    }

    [ContextMenu("Run Self Check")]
    public void RunSelfCheck()
    {
        Debug.Assert(questGiverTransform != null, "Fountain Quest Giver missing");
        Debug.Assert(litterCatalog != null && litterCatalog.entries.Count == 15, "LitterCatalog must contain all 15 entries");
        for (int i = 0; i < litterCatalog.entries.Count; i++)
        {
            var e = litterCatalog.entries[i];
            Debug.Assert(e.propPrefab != null && e.dialogueLines != null && e.dialogueLines.Length == 5, $"Invalid entry #{e.id}");
        }
        Debug.Assert(exclamationSprite != null && greenCheckSprite != null && edgeArrowSprite != null, "Sprites missing");
        Debug.Assert(CalculateRewardForRound(1) == 150 && CalculateRewardForRound(2) == 175 && CalculateRewardForRound(5) == 250 && CalculateRewardForRound(8) == 250, "Reward curve mismatch");
        Debug.Log("[LitterbugQuest] 15-type repeatable self-check passed.");
    }

    public void PerformSafetyReset()
    {
        IsDialogueOpen = false;
        advanceLockTimer = 0f;
        isTypingPage = false;
        activeDialogueMode = DialogueMode.None;
        activeSlotIndex = -1;
        activeSpeakerTransform = null;

        if (typewriterCoroutine != null)
        {
            StopCoroutine(typewriterCoroutine);
            typewriterCoroutine = null;
        }

        if (choiceButtonsRoot != null) choiceButtonsRoot.SetActive(false);
        if (acceptQuestButton != null) acceptQuestButton.gameObject.SetActive(false);
        if (notNowButton != null) notNowButton.gameObject.SetActive(false);
        if (continuePromptRoot != null) continuePromptRoot.SetActive(false);
        if (dialogueNextTriangle != null) dialogueNextTriangle.SetActive(false);

        if (activeSpeakerAnimator != null)
        {
            activeSpeakerAnimator.EndDialogue();
            activeSpeakerAnimator = null;
        }

        for (int i = 0; i < TargetCount; i++)
        {
            if (targetSlots[i].litterbug != null)
                targetSlots[i].litterbug.SetDialogueActive(false);
        }

        ReleaseCameraEase();
        UnlockPlayerMovement();

        if (topLetterboxBar != null) topLetterboxBar.anchoredPosition = new Vector2(0f, 86f);
        if (bottomLetterboxBar != null) bottomLetterboxBar.anchoredPosition = new Vector2(0f, -86f);

        if (dialoguePanelGroup != null)
        {
            dialoguePanelGroup.alpha = 0f;
            dialoguePanelGroup.blocksRaycasts = false;
            dialoguePanelGroup.interactable = false;
            dialoguePanelGroup.gameObject.SetActive(false);
        }

        SetHudVisibility(true, 0f);
        Time.timeScale = 1f;
        WorldInteractionPrompt.Instance?.SetBlocked(false);
    }

    private void AutoFitChoiceButton(Button btn, KeyCapPrompt kcp, string fallbackKey, string actionText)
    {
        if (btn == null) return;
        if (kcp != null)
        {
            kcp.SetPrompt(fallbackKey, actionText);
            kcp.SetLocked(false);
        }

        var rt = btn.GetComponent<RectTransform>();
        if (rt != null)
        {
            float textWidth = 60f;
            var labelTmp = btn.transform.Find("KeyCapPrompt/ActionLabel")?.GetComponent<TMPro.TextMeshProUGUI>();
            if (labelTmp != null)
            {
                textWidth = labelTmp.GetPreferredValues(actionText).x;
            }
            // Keycap (34px) + spacing (10px) + textWidth + horizontal margins (32px) = textWidth + 76px
            float w = Mathf.Max(140f, textWidth + 76f);
            rt.sizeDelta = new Vector2(w, rt.sizeDelta.y > 10f ? rt.sizeDelta.y : 60f);

            var le = btn.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.minWidth = w;
                le.preferredWidth = w;
            }

            var kcpRt = btn.transform.Find("KeyCapPrompt")?.GetComponent<RectTransform>();
            if (kcpRt != null)
            {
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(kcpRt);
            }
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    // Skip the reset during app quit / exiting Play Mode / scene unload: it would re-create
    // singletons (UIOrchestrator, HudVisibilityService, UI_Blur_Volume) while the scene is being torn down.
    private bool isQuitting;
    private void OnApplicationQuit() { isQuitting = true; }

    private void OnDisable()
    {
        if (isQuitting || !gameObject.scene.isLoaded) return;
        PerformSafetyReset();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (isQuitting || !gameObject.scene.isLoaded) return;
        PerformSafetyReset();
    }
}
