using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Cinemachine;

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
    public const int MaxCharsPerPage = 110;

    public static bool IsDialogueOpen { get; private set; }
    public static LitterbugQuestManager Instance { get; private set; }

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
    [SerializeField] private float questGiverInteractRadius = 2.5f;
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
    [SerializeField] private TextMeshProUGUI speakerNameText;
    [SerializeField] private TextMeshProUGUI dialogueBodyText;
    [SerializeField] private TextMeshProUGUI continueHintText;
    [SerializeField] private Button acceptQuestButton;

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
        QuestGiverTurnIn = 4
    }

    // 3 randomized intro variants (DESIGN 1)
    private static readonly string[] GuideIntroVariants =
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

    private Coroutine typewriterCoroutine;
    private Coroutine dialogueFadeCoroutine;
    private Coroutine counterPunchCoroutine;
    private Coroutine panelAnimCoroutine;

    public LitterbugQuestState QuestState => questState;
    public int CurrentRound => currentRound;
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
        IsDialogueOpen = false;
        questState = LitterbugQuestState.Available;
        currentRound = 0;
        completedRounds = 0;
        persuadedCount = 0;
        sessionRng = new System.Random(Environment.TickCount ^ Guid.NewGuid().GetHashCode());

        for (int c = 0; c < CategoryCount; c++)
            categoryShuffleBags[c] = new Queue<int>(3);

        if (questPanelRect != null && !capturedPanelPos)
        {
            questPanelShownAnchoredPos = questPanelRect.anchoredPosition;
            capturedPanelPos = true;
        }
    }

    private void Start()
    {
        CacheSceneReferences();
        InitializeHudDefaults();
        CreateQuestGiverOverheadMarker();

        if (minimapTracker != null)
        {
            minimapTracker.ConfigureSprites(exclamationSprite, greenCheckSprite, edgeArrowSprite);
            minimapTracker.ClearAll();
            minimapTracker.SetFountainMarker(questGiverTransform, visible: true, completedCheck: false);
        }
    }

    private void CacheSceneReferences()
    {
        mainCam = Camera.main;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            cachedFps = playerObj.GetComponent<StarterAssets.FirstPersonController>();
            cachedInputs = playerObj.GetComponent<StarterAssets.StarterAssetsInputs>();
        }
        if (cachedFps == null) cachedFps = FindFirstObjectByType<StarterAssets.FirstPersonController>();
        if (cachedInputs == null) cachedInputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
        if (playerTransform == null && cachedFps != null) playerTransform = cachedFps.transform;

        if (npcSpawner == null) npcSpawner = FindFirstObjectByType<NpcSpawner>();
        if (playerCurrency == null) playerCurrency = FindFirstObjectByType<PlayerCurrency>();
        if (minimapTracker == null) minimapTracker = GetComponent<LitterbugMinimapTracker>();

        if (acceptQuestButton != null)
        {
            acceptQuestButton.onClick.RemoveAllListeners();
            acceptQuestButton.onClick.AddListener(OnAcceptButtonClicked);
            acceptQuestButton.gameObject.SetActive(false);
        }

        if (dialogueVirtualCam != null)
        {
            dialogueVirtualCam.Priority.Value = 0;
            dialogueVirtualCam.gameObject.SetActive(false);
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
        if (roundTitleText != null) roundTitleText.text = "Round 1";
        if (questCounterText != null) questCounterText.text = "Enlighten the litterbugs: 0/5";
        if (distanceHintText != null) distanceHintText.text = "";
    }

    private void CreateQuestGiverOverheadMarker()
    {
        if (questGiverTransform == null) return;
        if (giverMarkerRoot != null) Destroy(giverMarkerRoot.gameObject);

        GameObject go = new GameObject("FountainQuestOverheadMarker");
        giverMarkerRoot = go.transform;
        giverMarkerRoot.SetParent(questGiverTransform, false);
        giverMarkerRoot.localPosition = new Vector3(0f, 2.25f, 0f);
        giverMarkerRoot.localScale = Vector3.one * 0.44f;

        giverMarkerRenderer = go.AddComponent<SpriteRenderer>();
        giverMarkerRenderer.sprite = exclamationSprite;
        giverMarkerRenderer.sortingOrder = 22;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
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

        if (playerTransform == null) return;

        bool modalBlocked = PauseMenuController.IsPaused
            || QuizUIController.IsAnyQuizOpen
            || (MinimapController.Instance != null && MinimapController.Instance.IsFullMapOpen);

        Vector3 playerPos = playerTransform.position;
        int interactSlot = -1;
        float bestInteractSqr = float.MaxValue;
        bool canInteractFountain = false;
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

            if (giverSqr <= giverInteractSqr && questState != LitterbugQuestState.Completed)
            {
                canInteractFountain = true;
            }
        }

        // 2. Check ONLY the 5 active target NPCs using squared distance
        float nearestActiveSqr = float.MaxValue;
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
                    nearestActiveSqr = sqrXZ;

                targetSlots[i].litterbug.TickFromManager(sqr3D, playerPos, false, dt);

                float slotInterSqr = targetSlots[i].litterbug.InteractRadiusSqr;
                if (targetSlots[i].litterbug.State == LitterbugState.Listening && sqrXZ <= slotInterSqr && sqrXZ < bestInteractSqr)
                {
                    bestInteractSqr = sqrXZ;
                    interactSlot = i;
                }
            }

            UpdateDistanceHintHud(playerPos, nearestActiveSqr);
        }

        // 3. Interaction prompt & E-key trigger
        bool showPrompt = !modalBlocked && (interactSlot >= 0 || canInteractFountain);
        if (interactPromptRoot != null && interactPromptRoot.activeSelf != showPrompt)
        {
            interactPromptRoot.SetActive(showPrompt);
            if (showPrompt && interactPromptText != null)
                interactPromptText.text = "Press E to talk";
        }

        if (showPrompt && CanConsumeEKeyThisFrame() && Input.GetKeyDown(KeyCode.E))
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

    private void UpdateDistanceHintHud(Vector3 playerPos, float nearestLitterbugSqr)
    {
        if (distanceHintText == null) return;

        bool returnPhase = questState == LitterbugQuestState.ReadyToTurnIn;
        float distMeters = 0f;

        if (returnPhase)
        {
            if (questGiverTransform != null)
            {
                Vector3 d = questGiverTransform.position - playerPos;
                d.y = 0f;
                distMeters = Mathf.Sqrt(d.sqrMagnitude);
            }
        }
        else if (nearestLitterbugSqr < float.MaxValue * 0.5f)
        {
            distMeters = Mathf.Sqrt(nearestLitterbugSqr);
        }

        int rounded = Mathf.RoundToInt(distMeters);
        if (rounded != lastDisplayedDistanceMeters || returnPhase != lastWasReturnHint)
        {
            lastDisplayedDistanceMeters = rounded;
            lastWasReturnHint = returnPhase;
            distanceHintText.text = returnPhase
                ? $"Guide: {rounded}m"
                : $"Nearest litterbug: {rounded}m";
        }
    }

    private void AnimateCleanlinessBar(float dt)
    {
        if (questState == LitterbugQuestState.Available) return;
        if (Mathf.Abs(displayedCleanliness - targetCleanliness) > 0.0005f)
        {
            displayedCleanliness = Mathf.MoveTowards(displayedCleanliness, targetCleanliness, dt * 0.85f);
            if (cleanlinessFillImage != null)
                cleanlinessFillImage.fillAmount = displayedCleanliness;
            if (cleanlinessPercentText != null)
                cleanlinessPercentText.text = $"{Mathf.RoundToInt(displayedCleanliness * 100f)}%";
        }
    }

    private void AnimateQuestGiverOverhead()
    {
        if (giverMarkerRoot == null) return;

        bool showMarker = questState == LitterbugQuestState.Available || questState == LitterbugQuestState.ReadyToTurnIn;
        if (giverMarkerRoot.gameObject.activeSelf != showMarker)
            giverMarkerRoot.gameObject.SetActive(showMarker);

        if (!showMarker) return;

        if (giverMarkerRenderer != null && giverMarkerRenderer.sprite != exclamationSprite)
            giverMarkerRenderer.sprite = exclamationSprite;

        float bob = Mathf.Sin(Time.time * 3.5f) * 0.09f;
        giverMarkerRoot.localPosition = new Vector3(0f, 2.25f + bob, 0f);

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
        displayedCleanliness = 0f;
        targetCleanliness = 0f;
        lastDisplayedDistanceMeters = -999;

        if (cleanlinessFillImage != null) cleanlinessFillImage.fillAmount = 0f;
        if (cleanlinessPercentText != null) cleanlinessPercentText.text = "0%";
        if (roundTitleText != null) roundTitleText.text = $"Round {currentRound}";
        if (questCounterText != null) questCounterText.text = "Enlighten the litterbugs: 0/5";
        if (questCounterRect != null) questCounterRect.localScale = Vector3.one;
        if (distanceHintText != null) distanceHintText.text = "";

        // Draw 1 random type per category using per-category shuffle bags (all 3 appear before any repeat)
        LitterTypeEntry[] roundEntries = DrawFiveCategoryEntries();
        ShuffleArray(roundEntries, sessionRng);

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
        if (panelAnimCoroutine != null) StopCoroutine(panelAnimCoroutine);
        panelAnimCoroutine = StartCoroutine(SlideInQuestPanelRoutine());
    }

    private LitterTypeEntry[] DrawFiveCategoryEntries()
    {
        var result = new LitterTypeEntry[CategoryCount];
        if (litterCatalog == null || litterCatalog.entries == null || litterCatalog.entries.Count == 0)
            return result;

        for (int catInt = 0; catInt < CategoryCount; catInt++)
        {
            LitterCategory cat = (LitterCategory)catInt;
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
            ShuffleList(pool, sessionRng);
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
            ShuffleList(freshPool, sessionRng);
            ShuffleList(fullPool, sessionRng);
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
        ShuffleList(pool, sessionRng);

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

        if (chosen == null) chosen = pool[0];
        BindNpcToSlot(slotIndex, chosen);
        return true;
    }

    private static void ShuffleList<T>(List<T> list, System.Random rng)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int r = rng.Next(i, list.Count);
            (list[i], list[r]) = (list[r], list[i]);
        }
    }

    private static void ShuffleArray<T>(T[] arr, System.Random rng)
    {
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
            AppendLineAsPages(pages, speaker, raw.Trim(), maxChars);
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

    private void ConsumeInputCooldown(float cooldownSeconds = 0.16f)
    {
        lastInputFrame = Time.frameCount;
        nextAllowedInputTime = Time.unscaledTime + cooldownSeconds;
    }

    public void OpenQuestGiverDialogue()
    {
        if (IsDialogueOpen || questState == LitterbugQuestState.Completed) return;

        if (questState == LitterbugQuestState.Available)
        {
            string randomIntro = GuideIntroVariants[sessionRng.Next(0, GuideIntroVariants.Length)];
            string[] lines = completedRounds > 0
                ? new[] { "Another round?", randomIntro }
                : new[] { randomIntro };
            string[] speakers = completedRounds > 0
                ? new[] { "Guide", "Guide" }
                : new[] { "Guide" };

            StartDialogueSession(DialogueMode.QuestGiverOffer, -1, questGiverTransform, speakers, lines);
            if (questGiverAnimator != null) questGiverAnimator.SetBool("IsTalking", true);
        }
        else if (questState == LitterbugQuestState.ReadyToTurnIn)
        {
            StartDialogueSession(DialogueMode.QuestGiverTurnIn, -1, questGiverTransform, new[] { "Guide", "Guide" }, TurnInLines);
            if (questGiverAnimator != null) questGiverAnimator.SetBool("IsTalking", true);
        }
        else
        {
            StartDialogueSession(DialogueMode.QuestGiverInProgress, -1, questGiverTransform, new[] { "Guide" }, InProgressLines);
            if (questGiverAnimator != null) questGiverAnimator.SetBool("IsTalking", true);
        }
    }

    public void OpenLitterbugDialogue(int slotIndex)
    {
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

        LockPlayerMovement(mode == DialogueMode.QuestGiverOffer);
        EaseCameraTowardSpeaker(speaker);

        if (dialogueFadeCoroutine != null) StopCoroutine(dialogueFadeCoroutine);
        dialogueFadeCoroutine = StartCoroutine(FadeDialoguePanelRoutine(1f, 0.25f));

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
            speakerNameText.text = page.speaker;
            speakerNameText.color = page.speaker == "YOU"
                ? new Color(0.45f, 0.88f, 1.0f, 1f)
                : new Color(0.46f, 0.96f, 0.60f, 1f);
        }

        bool isOfferFinalPage = (activeDialogueMode == DialogueMode.QuestGiverOffer) && (currentPageIndex == activePages.Count - 1);
        if (acceptQuestButton != null)
            acceptQuestButton.gameObject.SetActive(isOfferFinalPage);

        if (continueHintText != null)
        {
            continueHintText.text = isOfferFinalPage
                ? "Press [E] or click Accept"
                : (currentPageIndex == activePages.Count - 1 ? "Press [E] to finish" : "Press [E] to continue");
        }

        if (typewriterCoroutine != null) StopCoroutine(typewriterCoroutine);
        typewriterCoroutine = StartCoroutine(TypewriterRoutine(page.text));
    }

    private IEnumerator TypewriterRoutine(string fullText)
    {
        isTypingPage = true;
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

            if (i % 2 == 1 && !char.IsWhiteSpace(fullText[i - 1]))
                PlayDialogueBlipAudio();

            yield return new WaitForSecondsRealtime(0.022f);
        }

        if (dialogueBodyText != null)
            dialogueBodyText.maxVisibleCharacters = 99999;

        isTypingPage = false;
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

        if (Input.GetKeyDown(KeyCode.E))
        {
            ConsumeInputCooldown(0.16f);
            StepDialogueInput();
        }
    }

    private void OnAcceptButtonClicked()
    {
        if (!IsDialogueOpen || activeDialogueMode != DialogueMode.QuestGiverOffer) return;
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
                questCounterText.text = "Return to the Guide";

            if (minimapTracker != null)
                minimapTracker.SetFountainMarker(questGiverTransform, visible: true, completedCheck: false);
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
            distanceHintText.text = "Talk to Guide for another round";

        if (counterPunchCoroutine != null) StopCoroutine(counterPunchCoroutine);
        if (questCounterRect != null && questCounterRect.gameObject.activeInHierarchy)
            counterPunchCoroutine = StartCoroutine(PunchScaleRoutine(questCounterRect));

        if (panelAnimCoroutine != null) StopCoroutine(panelAnimCoroutine);
        panelAnimCoroutine = StartCoroutine(CompleteHoldAndFadeOutPanelRoutine());

        // Immediately transition Completed -> Available so Guide offers "Another round?" with no long cooldown
        questState = LitterbugQuestState.Available;
        if (minimapTracker != null)
        {
            minimapTracker.ClearAll();
            minimapTracker.SetFountainMarker(questGiverTransform, visible: true, completedCheck: false);
        }
    }

    private void CloseDialogueImmediateOrFade()
    {
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
        if (acceptQuestButton != null)
            acceptQuestButton.gameObject.SetActive(false);

        ReleaseCameraEase();
        UnlockPlayerMovement();

        if (dialogueFadeCoroutine != null) StopCoroutine(dialogueFadeCoroutine);
        if (isActiveAndEnabled && dialoguePanelGroup != null)
            dialogueFadeCoroutine = StartCoroutine(FadeDialoguePanelRoutine(0f, 0.25f));
    }

    // =====================================================================
    // CAMERA EASE, PLAYER LOCK SAFETY & UI PANEL ANIMATIONS
    // =====================================================================

    private void EaseCameraTowardSpeaker(Transform speaker)
    {
        if (speaker == null || mainCam == null || dialogueVirtualCam == null) return;

        Vector3 speakerChest = speaker.position + Vector3.up * 1.42f;
        Vector3 playerEye = playerTransform != null
            ? playerTransform.position + Vector3.up * 1.48f
            : mainCam.transform.position;

        Vector3 fromSpeakerToPlayer = playerEye - speaker.position;
        fromSpeakerToPlayer.y = 0f;
        if (fromSpeakerToPlayer.sqrMagnitude < 0.01f)
            fromSpeakerToPlayer = speaker.forward;
        fromSpeakerToPlayer.Normalize();

        Vector3 rightOffset = Vector3.Cross(Vector3.up, fromSpeakerToPlayer) * 0.28f;
        Vector3 shotPos = speaker.position + fromSpeakerToPlayer * 2.15f + rightOffset + Vector3.up * 1.50f;
        Quaternion shotRot = Quaternion.LookRotation((speakerChest - shotPos).normalized, Vector3.up);

        CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
        if (brain != null)
        {
            brain.IgnoreTimeScale = true;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.45f);
        }

        dialogueVirtualCam.transform.SetPositionAndRotation(shotPos, shotRot);
        dialogueVirtualCam.Lens.FieldOfView = 52f;
        dialogueVirtualCam.gameObject.SetActive(true);
        dialogueVirtualCam.Priority.Value = 35;
    }

    private void ReleaseCameraEase()
    {
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
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (CenterScreenCursor.Instance != null)
            CenterScreenCursor.Instance.SetVisible(false);
    }

    private void UnlockPlayerMovement()
    {
        if (!playerControlsLocked && cachedFps != null && cachedFps.enabled) return;
        playerControlsLocked = false;

        if (cachedFps != null) cachedFps.enabled = true;
        if (cachedInputs != null)
        {
            cachedInputs.move = Vector2.zero;
            cachedInputs.look = Vector2.zero;
            cachedInputs.cursorInputForLook = true;
            cachedInputs.cursorLocked = true;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (CenterScreenCursor.Instance != null)
            CenterScreenCursor.Instance.SetVisible(true);
    }

    private IEnumerator FadeDialoguePanelRoutine(float targetAlpha, float duration)
    {
        if (dialoguePanelGroup == null) yield break;

        if (targetAlpha > 0f && !dialoguePanelGroup.gameObject.activeSelf)
            dialoguePanelGroup.gameObject.SetActive(true);

        float startAlpha = dialoguePanelGroup.alpha;
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
            yield return null;
        }

        dialoguePanelGroup.alpha = targetAlpha;
        dialoguePanelGroup.interactable = interactive;
        dialoguePanelGroup.blocksRaycasts = interactive;

        if (targetAlpha <= 0.001f)
            dialoguePanelGroup.gameObject.SetActive(false);

        dialogueFadeCoroutine = null;
    }

    private IEnumerator SlideInQuestPanelRoutine()
    {
        if (questPanelGroup == null || questPanelRect == null) yield break;
        if (!capturedPanelPos)
        {
            questPanelShownAnchoredPos = questPanelRect.anchoredPosition;
            capturedPanelPos = true;
        }

        // Always reset active state, alpha, and slide offset cleanly on re-enter (DESIGN 8)
        questPanelGroup.gameObject.SetActive(true);
        questPanelGroup.alpha = 0f;
        questPanelGroup.interactable = false;
        questPanelGroup.blocksRaycasts = false;

        Vector2 hiddenPos = questPanelShownAnchoredPos + new Vector2(420f, 0f);
        questPanelRect.anchoredPosition = hiddenPos;

        float duration = 0.38f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            questPanelGroup.alpha = t;
            questPanelRect.anchoredPosition = Vector2.Lerp(hiddenPos, questPanelShownAnchoredPos, ease);
            yield return null;
        }

        questPanelGroup.alpha = 1f;
        questPanelRect.anchoredPosition = questPanelShownAnchoredPos;
        panelAnimCoroutine = null;
    }

    private IEnumerator CompleteHoldAndFadeOutPanelRoutine()
    {
        if (questPanelGroup == null) yield break;

        questPanelGroup.gameObject.SetActive(true);
        questPanelGroup.alpha = 1f;
        if (questPanelRect != null && capturedPanelPos)
            questPanelRect.anchoredPosition = questPanelShownAnchoredPos;

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
            sfxSource.PlayOneShot(dialogueBlipClip, 0.45f);
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

    private void OnDisable()
    {
        IsDialogueOpen = false;
        ReleaseCameraEase();
        UnlockPlayerMovement();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        IsDialogueOpen = false;
        ReleaseCameraEase();
        UnlockPlayerMovement();
    }
}
