using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EcoQuest.CityQuest
{
    public enum CityDetectiveState
    {
        NotStarted = 0,
        Active = 1,
        ReadyToTurnIn = 2,
        Completed = 3
    }

    public enum RobinDialogueState
    {
        Idle = 0,
        Entering = 1,
        Open = 2,
        Exiting = 3
    }

    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public class CityTrashDetectiveManager : MonoBehaviour
    {
        public static CityTrashDetectiveManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
            if (UIOrchestrator.IsCityScavengerActiveHook != null)
                UIOrchestrator.IsCityScavengerActiveHook = null;
            if (UIOrchestrator.IsCityScavengerOverlayOpenHook != null)
                UIOrchestrator.IsCityScavengerOverlayOpenHook = null;
        }

        [Serializable]
        public class ChecklistSlot
        {
            public CityTrashItemDef item;
            public CityDistrict district;
            public CityTrashSpawnPoint spawnPoint;
            public CityTrashPickupInstance pickupInstance;
            public bool collected;
        }

        [Header("Database & Scene References")]
        public CityTrashDatabase database;
        public Transform questRoot;
        public Transform districtsRoot;
        public Transform spawnPointsRoot;
        public Transform poolRoot;
        public CityTrashDetectiveUI ui;
        public CityBonusQuizAdapter bonusQuizAdapter;

        [Header("Quest Giver NPC (Explorer_Var2 Scene Instance)")]
        public Transform rangerRobinTransform;
        public NpcExplorerController rangerRobinController;
        public string npcDisplayName = "Ranger Robin";
        public float npcInteractRange = 3.0f;

        [Header("Cinematic & Dialogue Camera")]
        public Transform dialogueCameraPoint;
        public CinemachineCamera robinDialogueCam;
        public RectTransform topLetterboxBar;
        public RectTransform bottomLetterboxBar;

        [Header("Ranger Robin Dialogue Lines")]
        [TextArea(2, 4)]
        public string[] introDialogueLines = new string[]
        {
            "Hey there, Detective! Welcome to the City Eco-Watch station.",
            "Different neighborhoods around the city have 10 specific pieces of litter hiding in plain sight.",
            "Take my Field Checklist, explore the 5 districts before the 4:00 timer runs out, and press E to collect each item and learn its story!"
        };

        [TextArea(2, 3)]
        public string activeProgressDialogue =
            "You're doing great! Check your Field Checklist (TAB) or press F for a Compass Hint if you get stuck.";

        [TextArea(2, 3)]
        public string turnInDialogue =
            "Incredible detective work! You tracked down every single item on the Field Checklist! Let's tally your Star Rating and Bonus Quiz!";

        [TextArea(2, 3)]
        public string completedDialogue =
            "Thanks for keeping our city sparkling clean, Detective! Ready for a brand-new Field Checklist to beat your personal best?";

        [TextArea(2, 3)]
        public string litterbugConflictDialogue =
            "Looks like you're currently in the middle of the Guardian's Litterbug round! Finish or turn in that round first, then come right back to me.";

        [Header("Quest Settings")]
        public float questDurationSeconds = 240f;
        public float pickupInteractRange = 2.5f;
        public int maxHintCharges = 4;
        public float hintCooldownSeconds = 25f;
        public float hintActiveDuration = 6.0f;
        public int coinsPerPickup = 5;

        [Header("Runtime State (Read-Only)")]
        [SerializeField] private CityDetectiveState questState = CityDetectiveState.NotStarted;
        [SerializeField] private float remainingSeconds = 240f;
        [SerializeField] private int collectedCount = 0;
        [SerializeField] private int remainingHintCharges = 4;
        [SerializeField] private float currentHintCooldown = 0f;
        [SerializeField] private int currentAttemptSeed = 0;
        [SerializeField] private int currentRunPickupCoins = 0;
        [SerializeField] private int lastRunStars = 0;

        public CityDetectiveState QuestState => questState;
        public float RemainingSeconds => remainingSeconds;
        public int CollectedCount => collectedCount;
        public int RemainingHintCharges => remainingHintCharges;
        public float CurrentHintCooldown => currentHintCooldown;
        public int LastRunStars => lastRunStars;

        private readonly List<ChecklistSlot> activeChecklist = new List<ChecklistSlot>(10);
        public IReadOnlyList<ChecklistSlot> ActiveChecklist => activeChecklist;

        private readonly List<CityTrashPickupInstance> pickupPool = new List<CityTrashPickupInstance>(10);
        private readonly List<CityDistrictVolume> districtVolumes = new List<CityDistrictVolume>(5);
        private readonly List<CityTrashSpawnPoint> allSpawnPoints = new List<CityTrashSpawnPoint>(100);
        private readonly List<CityTrashItemDef> lastCollectedItemsForQuiz = new List<CityTrashItemDef>(10);

        private Transform playerTransform;
        private Camera mainCam;
        private Transform robinHeadMarker;
        private CityTrashPickupInstance activeHintTarget;
        private float hintActiveTimer = 0f;
        private bool cursorUnlockedByModal = false;
        private float modalCloseCooldownTimer = 0f;
        private float robinInteractionCooldownUntil = 0f;
        private Coroutine letterboxRoutine;
        private Coroutine dialogueWatchdogRoutine;
        private Coroutine activeDialogueSessionRoutine;
        private RobinDialogueState dialogueState = RobinDialogueState.Idle;
        public RobinDialogueState DialogueState => dialogueState;
        private bool isFinalizingQuest = false;

        public bool IsOverlayOrModalOpen =>
            (ui != null && ui.IsAnyModalOpen) ||
            (bonusQuizAdapter != null && bonusQuizAdapter.IsQuizRunning) ||
            (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue);

        private void Awake()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene())
            {
                enabled = false;
                return;
            }

            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            remainingSeconds = questDurationSeconds;
            remainingHintCharges = maxHintCharges;

            if (introDialogueLines != null)
            {
                for (int i = 0; i < introDialogueLines.Length; i++)
                {
                    if (!string.IsNullOrEmpty(introDialogueLines[i]) && introDialogueLines[i].Contains("3:00"))
                    {
                        introDialogueLines[i] = introDialogueLines[i].Replace("3:00", "4:00");
                    }
                }
            }

            // Wire UIOrchestrator hooks
            UIOrchestrator.IsCityScavengerActiveHook = () => questState == CityDetectiveState.Active || questState == CityDetectiveState.ReadyToTurnIn;
            UIOrchestrator.IsCityScavengerOverlayOpenHook = () => IsOverlayOrModalOpen;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                if (UIOrchestrator.IsCityScavengerActiveHook != null)
                    UIOrchestrator.IsCityScavengerActiveHook = null;
                if (UIOrchestrator.IsCityScavengerOverlayOpenHook != null)
                    UIOrchestrator.IsCityScavengerOverlayOpenHook = null;
            }
        }

        private void Start()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene()) return;

            FindSceneReferences();
            CacheDistrictVolumes();
            CacheSpawnPoints();
            CachePickupPool();
            EnsureRobinHeadMarker();
            UpdateRobinMarkerVisibility();
        }

        private void FindSceneReferences()
        {
            if (playerTransform == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) playerTransform = p.transform;
            }
            if (mainCam == null) mainCam = Camera.main;

            if (rangerRobinTransform == null)
            {
                GameObject robin = GameObject.Find("Explorer_Var2");
                if (robin != null)
                {
                    rangerRobinTransform = robin.transform;
                    rangerRobinController = robin.GetComponent<NpcExplorerController>();
                }
            }

            if (dialogueCameraPoint == null && rangerRobinTransform != null)
            {
                Transform found = rangerRobinTransform.Find("DialogueCameraPoint");
                if (found != null) dialogueCameraPoint = found;
            }
            if (dialogueCameraPoint != null && robinDialogueCam == null)
            {
                robinDialogueCam = dialogueCameraPoint.GetComponent<CinemachineCamera>();
            }

            if (topLetterboxBar == null)
            {
                var go = GameObject.Find("TopLetterboxBar");
                if (go != null) topLetterboxBar = go.GetComponent<RectTransform>();
            }
            if (bottomLetterboxBar == null)
            {
                var go = GameObject.Find("BottomLetterboxBar");
                if (go != null) bottomLetterboxBar = go.GetComponent<RectTransform>();
            }

            if (ui == null)
            {
                ui = FindFirstObjectByType<CityTrashDetectiveUI>(FindObjectsInactive.Include);
            }
            if (bonusQuizAdapter == null)
            {
                bonusQuizAdapter = GetComponentInChildren<CityBonusQuizAdapter>(true);
            }
        }

        private void CacheDistrictVolumes()
        {
            districtVolumes.Clear();
            if (districtsRoot != null)
            {
                districtsRoot.GetComponentsInChildren(true, districtVolumes);
            }
            else
            {
                var vols = FindObjectsByType<CityDistrictVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                districtVolumes.AddRange(vols);
            }
        }

        private void CacheSpawnPoints()
        {
            allSpawnPoints.Clear();
            if (spawnPointsRoot != null)
            {
                spawnPointsRoot.GetComponentsInChildren(true, allSpawnPoints);
            }
            else
            {
                var sps = FindObjectsByType<CityTrashSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                allSpawnPoints.AddRange(sps);
            }
        }

        private void CachePickupPool()
        {
            pickupPool.Clear();
            if (poolRoot != null)
            {
                poolRoot.GetComponentsInChildren(true, pickupPool);
            }
            else
            {
                var insts = FindObjectsByType<CityTrashPickupInstance>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                pickupPool.AddRange(insts);
            }

            for (int i = 0; i < pickupPool.Count; i++)
            {
                if (database != null) pickupPool[i].PrewarmVisuals(database);
                pickupPool[i].ReturnToPoolImmediate();
            }
        }

        private void EnsureRobinHeadMarker()
        {
            if (rangerRobinTransform == null || robinHeadMarker != null) return;

            Transform existing = rangerRobinTransform.Find("RobinQuestMarker");
            if (existing != null)
            {
                robinHeadMarker = existing;
                return;
            }

            GameObject markerRoot = new GameObject("RobinQuestMarker");
            markerRoot.transform.SetParent(rangerRobinTransform, false);
            markerRoot.transform.localPosition = new Vector3(0f, 2.25f, 0f);

            GameObject textGo = new GameObject("MarkerText");
            textGo.transform.SetParent(markerRoot.transform, false);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = "!";
            tmp.fontSize = 7.5f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(1f, 0.85f, 0.20f, 1f);

            robinHeadMarker = markerRoot.transform;
        }

        private void UpdateRobinMarkerVisibility()
        {
            if (robinHeadMarker == null) EnsureRobinHeadMarker();
            if (robinHeadMarker == null) return;

            bool isDialogue = (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue);
            bool show = !isDialogue && (questState == CityDetectiveState.NotStarted ||
                                        questState == CityDetectiveState.ReadyToTurnIn ||
                                        questState == CityDetectiveState.Completed);

            robinHeadMarker.gameObject.SetActive(show);

            var tmp = robinHeadMarker.GetComponentInChildren<TextMeshPro>(true);
            if (tmp != null)
            {
                if (questState == CityDetectiveState.ReadyToTurnIn)
                {
                    tmp.text = "?";
                    tmp.color = new Color(0.35f, 0.90f, 0.45f, 1f);
                }
                else
                {
                    tmp.text = "!";
                    tmp.color = new Color(1f, 0.85f, 0.20f, 1f);
                }
            }
        }

        private void Update()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene()) return;

            if (playerTransform == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) playerTransform = p.transform;
            }
            if (mainCam == null) mainCam = Camera.main;

            float time = Time.time;
            float unscaledTime = Time.unscaledTime;

            // Animate Robin's head marker billboard
            if (robinHeadMarker != null && robinHeadMarker.gameObject.activeSelf)
            {
                float bob = Mathf.Sin(unscaledTime * 3.2f) * 0.08f;
                robinHeadMarker.localPosition = new Vector3(0f, 2.25f + bob, 0f);
                if (mainCam != null)
                {
                    robinHeadMarker.rotation = Quaternion.LookRotation(robinHeadMarker.position - mainCam.transform.position);
                }
            }

            bool modalOrQuizOpen = IsOverlayOrModalOpen;
            ManageCursorForModals(modalOrQuizOpen);

            if (modalCloseCooldownTimer > 0f)
            {
                modalCloseCooldownTimer -= Time.unscaledDeltaTime;
            }

            if (modalOrQuizOpen)
            {
                WorldInteractionPrompt.Instance?.HidePrompt();
                return;
            }

            if (questState == CityDetectiveState.ReadyToTurnIn && !isFinalizingQuest)
            {
                StartCoroutine(CompleteQuestAllItemsFoundRoutine());
                return;
            }

            // Active quest timer, hint cooldown, and pickup animations
            if (questState == CityDetectiveState.Active)
            {
                bool timerPaused = IsTimerPaused();
                if (!timerPaused)
                {
                    remainingSeconds -= Time.deltaTime;
                    if (currentHintCooldown > 0f)
                    {
                        currentHintCooldown = Mathf.Max(0f, currentHintCooldown - Time.deltaTime);
                    }

                    if (remainingSeconds <= 0f)
                    {
                        remainingSeconds = 0f;
                        OnTimerExpired();
                        return;
                    }
                }

                if (ui != null)
                {
                    ui.TickTimerAndHintFooter(remainingSeconds, remainingHintCharges, currentHintCooldown, unscaledTime, timerPaused);
                }

                // Tick pooled item visuals
                Vector3 pPos = playerTransform != null ? playerTransform.position : Vector3.zero;
                for (int i = 0; i < activeChecklist.Count; i++)
                {
                    var inst = activeChecklist[i].pickupInstance;
                    if (inst != null && !activeChecklist[i].collected)
                    {
                        inst.TickVisuals(time, pPos);
                    }
                }

                // Hint key (F)
                if (modalCloseCooldownTimer <= 0f && Input.GetKeyDown(KeyCode.F))
                {
                    if (currentHintCooldown > 0f)
                    {
                        ui?.ShowHintToast($"Hint ready in {Mathf.CeilToInt(currentHintCooldown)}s");
                    }
                    else if (remainingHintCharges <= 0)
                    {
                        ui?.ShowHintToast("No hints left");
                    }
                    else
                    {
                        TryTriggerCompassHint();
                    }
                }

                // Update active compass arrow if running
                if (hintActiveTimer > 0f && activeHintTarget != null && !activeHintTarget.isCollected)
                {
                    hintActiveTimer -= Time.deltaTime;
                    Vector3 camFwd = mainCam != null ? mainCam.transform.forward : (playerTransform != null ? playerTransform.forward : Vector3.forward);
                    ui?.UpdateCompassHud(
                        hintActiveTimer > 0f,
                        pPos,
                        camFwd,
                        activeHintTarget.transform.position,
                        activeHintTarget.itemDef != null ? activeHintTarget.itemDef.displayName : "Trash",
                        activeHintTarget.spawnedDistrict,
                        hintActiveTimer);
                }
                else if (ui != null)
                {
                    ui.UpdateCompassHud(false, Vector3.zero, Vector3.forward, Vector3.zero, "", CityDistrict.Park, 0f);
                }
            }
            else if (ui != null)
            {
                ui.UpdateCompassHud(false, Vector3.zero, Vector3.forward, Vector3.zero, "", CityDistrict.Park, 0f);
            }

            if (dialogueState == RobinDialogueState.Idle)
            {
                HandlePlayerInteraction();
            }
        }

        private bool IsTimerPaused()
        {
            if (Time.timeScale <= 0.001f) return true;
            if (LitterbugQuestManager.IsDialogueOpen) return true;
            if (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalActuallyVisible()) return true;
            if (UIOrchestrator.Instance != null && (UIOrchestrator.Instance.AreModalsOpen || UIOrchestrator.Instance.IsOverlayActive)) return true;
            if (ui != null && ui.IsAnyModalOpen) return true;
            if (bonusQuizAdapter != null && bonusQuizAdapter.IsQuizRunning) return true;
            if (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue) return true;
            return false;
        }

        private void ManageCursorForModals(bool open)
        {
            if (open)
            {
                if (!cursorUnlockedByModal)
                {
                    cursorUnlockedByModal = true;
                    UIOrchestrator.Instance?.ApplyCurrentProfile(0.15f);
                }
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                var inputs = playerTransform != null ? playerTransform.GetComponent<StarterAssets.StarterAssetsInputs>() : FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
                if (inputs != null && (inputs.cursorLocked || inputs.cursorInputForLook))
                {
                    inputs.cursorLocked = false;
                    inputs.cursorInputForLook = false;
                }
                var fps = playerTransform != null ? playerTransform.GetComponent<StarterAssets.FirstPersonController>() : FindFirstObjectByType<StarterAssets.FirstPersonController>();
                if (fps != null && ((bonusQuizAdapter != null && bonusQuizAdapter.IsQuizRunning) || (ui != null && ui.IsAnyModalOpen)) && fps.enabled)
                {
                    fps.enabled = false;
                }
            }
            else if (!open && cursorUnlockedByModal)
            {
                cursorUnlockedByModal = false;
                if (!LitterbugQuestManager.IsDialogueOpen &&
                    (ModalManager.Instance == null || !ModalManager.Instance.IsAnyModalActuallyVisible()))
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    var inputs = playerTransform != null ? playerTransform.GetComponent<StarterAssets.StarterAssetsInputs>() : FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
                    if (inputs != null)
                    {
                        inputs.cursorLocked = true;
                        inputs.cursorInputForLook = true;
                    }
                    var fps = playerTransform != null ? playerTransform.GetComponent<StarterAssets.FirstPersonController>() : FindFirstObjectByType<StarterAssets.FirstPersonController>();
                    if (fps != null && dialogueState == RobinDialogueState.Idle)
                    {
                        fps.enabled = true;
                    }
                }
                UIOrchestrator.Instance?.ApplyCurrentProfile(0.15f);
            }
        }

        private void HandlePlayerInteraction()
        {
            if (playerTransform == null) return;
            if (LitterbugQuestManager.IsDialogueOpen) return;

            Vector3 pPos = playerTransform.position;

            // 1. Check nearest uncollected City Trash item if quest is Active
            if (questState == CityDetectiveState.Active)
            {
                int bestSlot = -1;
                float bestDistSq = pickupInteractRange * pickupInteractRange;

                for (int i = 0; i < activeChecklist.Count; i++)
                {
                    var slot = activeChecklist[i];
                    if (slot.collected || slot.pickupInstance == null || !slot.pickupInstance.gameObject.activeSelf)
                        continue;

                    Vector3 diff = slot.pickupInstance.transform.position - pPos;
                    diff.y *= 0.5f;
                    float dSq = diff.sqrMagnitude;
                    if (dSq <= bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestSlot = i;
                    }
                }

                if (bestSlot >= 0)
                {
                    var targetSlot = activeChecklist[bestSlot];
                    WorldInteractionPrompt.Instance?.ShowPrompt(
                        targetSlot.pickupInstance.transform,
                        targetSlot.item.displayName,
                        "Collect",
                        "E");

                    if (modalCloseCooldownTimer <= 0f && Input.GetKeyDown(KeyCode.E))
                    {
                        CollectChecklistSlot(bestSlot);
                    }
                    return;
                }
            }

            // 2. Check Ranger Robin distance
            if (rangerRobinTransform != null && Time.unscaledTime >= robinInteractionCooldownUntil)
            {
                Vector3 toRobin = rangerRobinTransform.position - pPos;
                toRobin.y = 0f;
                float distSq = toRobin.sqrMagnitude;
                if (distSq <= npcInteractRange * npcInteractRange)
                {
                    WorldInteractionPrompt.Instance?.ShowPrompt(
                        rangerRobinTransform,
                        npcDisplayName,
                        "Talk",
                        "E");

                    if (modalCloseCooldownTimer <= 0f && Input.GetKeyDown(KeyCode.E))
                    {
                        OpenRangerRobinDialogue();
                    }
                    return;
                }
            }

            // 3. If out of range of Robin and pickups, clear prompt only if owned by Robin or a CityTrash pickup
            if (WorldInteractionPrompt.Instance != null && WorldInteractionPrompt.Instance.ActiveTarget != null)
            {
                Transform activeTarget = WorldInteractionPrompt.Instance.ActiveTarget;
                bool ownedByCityDetective = (activeTarget == rangerRobinTransform);
                if (!ownedByCityDetective)
                {
                    for (int i = 0; i < pickupPool.Count; i++)
                    {
                        if (pickupPool[i] != null && pickupPool[i].transform == activeTarget)
                        {
                            ownedByCityDetective = true;
                            break;
                        }
                    }
                }
                if (ownedByCityDetective)
                {
                    WorldInteractionPrompt.Instance.HidePrompt();
                }
            }
        }

        // =====================================================================
        // RANGER ROBIN DIALOGUE FLOWS (Unified with DialoguePresenter & Guardian Look)
        // =====================================================================

        public void OpenRangerRobinDialogue()
        {
            if (Time.unscaledTime < robinInteractionCooldownUntil) return;
            if (dialogueState != RobinDialogueState.Idle || modalCloseCooldownTimer > 0f)
            {
#if UNITY_EDITOR
                Debug.Log($"[FlowTrace] OpenRangerRobinDialogue ignored: state={dialogueState}, cooldown={modalCloseCooldownTimer:F2}s");
#endif
                return;
            }

            if (DialoguePresenter.Instance == null)
            {
                Debug.LogWarning("[CityTrashDetectiveManager] DialoguePresenter.Instance is null!");
                return;
            }

            if (activeDialogueSessionRoutine != null)
            {
                StopCoroutine(activeDialogueSessionRoutine);
                activeDialogueSessionRoutine = null;
            }
            activeDialogueSessionRoutine = StartCoroutine(RobinDialogueSessionRoutine());
        }

        private IEnumerator RobinDialogueSessionRoutine()
        {
            dialogueState = RobinDialogueState.Entering;
            bool cleanupExecuted = false;

            try
            {
                // 1. Enter Cinematic Profile (idempotent overlay 'RangerRobin')
                UIOrchestrator.Instance?.EnterCinematicProfile("RangerRobin");
                SetPlayerControlFrozen(true);

                // 2. Rotate Ranger Robin horizontally toward player (yaw only)
                if (rangerRobinTransform != null && playerTransform != null)
                {
                    Vector3 dir = playerTransform.position - rangerRobinTransform.position;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.01f)
                    {
                        rangerRobinTransform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    }
                }

                // 3. Shared DialogueCameraRig: restores full player body/head, frames over-the-shoulder without backpack occlusion, starts Bokeh DOF on NPC head
                if (robinDialogueCam != null)
                {
                    robinDialogueCam.Priority.Value = 0; // Ensure static local cam never overrides shared rig
                }
                EcoQuest.Shared.DialogueCameraRig.Instance.EnterDialogue(rangerRobinTransform, playerTransform, 0.6f, "RangerRobin");

                // 4. Letterbox Enter (0.6s)
                if (letterboxRoutine != null) StopCoroutine(letterboxRoutine);
                letterboxRoutine = StartCoroutine(TransitionLetterbox(true, 0.6f));

                // 5. Head Look-At & Animations
                rangerRobinController?.SetLookAtTarget(playerTransform);
                rangerRobinController?.PlayWave();
                rangerRobinController?.PlayTalk();

                // 6. Hide markers & prompts
                WorldInteractionPrompt.Instance?.HidePrompt();
                if (robinHeadMarker != null) robinHeadMarker.gameObject.SetActive(false);

#if UNITY_EDITOR
                LogFlowTraceSnapshot("DialogueOpen");
#endif

                yield return new WaitForSecondsRealtime(0.20f);
                dialogueState = RobinDialogueState.Open;

                // Determine dialogue content & choices
                string[] lines;
                bool showChoices = true;
                string acceptLabel = "Accept";
                string declineLabel = "Close";
                Action onAccept = null;
                Action onDecline = null;

                if (LitterbugQuestManager.Instance != null &&
                    (LitterbugQuestManager.Instance.QuestState == LitterbugQuestState.Active ||
                     LitterbugQuestManager.Instance.QuestState == LitterbugQuestState.ReadyToTurnIn))
                {
                    lines = new string[] { litterbugConflictDialogue };
                    showChoices = false;
                }
                else if (questState == CityDetectiveState.NotStarted)
                {
                    lines = introDialogueLines;
                    bool hasPendingQuiz = PlayerPrefs.GetInt(EcoQuestSaveKeys.CityBonusPending, 0) == 1;
                    if (hasPendingQuiz)
                    {
                        acceptLabel = "Accept";
                        declineLabel = "Take Pending Bonus Quiz";
                        onAccept = () => StartNewAttempt();
                        onDecline = () => LaunchBonusQuiz();
                    }
                    else
                    {
                        acceptLabel = "Accept";
                        declineLabel = "Close";
                        onAccept = () => StartNewAttempt();
                        onDecline = null;
                    }
                }
                else if (questState == CityDetectiveState.Active)
                {
                    int sec = Mathf.CeilToInt(remainingSeconds);
                    lines = new string[]
                    {
                        activeProgressDialogue,
                        $"Current Progress: {collectedCount} / 10 Items Found. Time Remaining: {sec / 60:00}:{sec % 60:00}."
                    };
                    acceptLabel = "Keep Searching";
                    declineLabel = "Cancel Quest";
                    onAccept = null;
                    onDecline = () => CancelActiveAttempt();
                }
                else if (questState == CityDetectiveState.ReadyToTurnIn)
                {
                    rangerRobinController?.PlayReaction("Cheer");
                    lines = new string[] { turnInDialogue };
                    acceptLabel = "View Results";
                    declineLabel = "Close";
                    showChoices = false;
                    onAccept = () => FinalizeAttemptAndShowResults();
                }
                else // Completed
                {
                    int bestStars = PlayerPrefs.GetInt(EcoQuestSaveKeys.CityBestStars, 0);
                    int bestSec = Mathf.RoundToInt(PlayerPrefs.GetFloat(EcoQuestSaveKeys.CityBestTime, 0f));
                    bool hasPendingQuiz = PlayerPrefs.GetInt(EcoQuestSaveKeys.CityBonusPending, 0) == 1;

                    if (hasPendingQuiz)
                    {
                        lines = new string[]
                        {
                            completedDialogue,
                            $"Personal Best: {bestStars} Stars ({bestSec / 60:00}:{bestSec % 60:00} remaining). You have an unclaimed Bonus Quiz waiting!"
                        };
                        acceptLabel = "Take Bonus Quiz";
                        declineLabel = "Play Again";
                        onAccept = () => LaunchBonusQuiz();
                        onDecline = () => StartNewAttempt();
                    }
                    else
                    {
                        lines = new string[]
                        {
                            completedDialogue,
                            $"Personal Best: {bestStars} Stars ({bestSec / 60:00}:{bestSec % 60:00} remaining). Ready for a brand-new Field Checklist?"
                        };
                        acceptLabel = "Accept";
                        declineLabel = "Close";
                        onAccept = () => StartNewAttempt();
                        onDecline = null;
                    }
                }

                bool dialogueDone = false;
                DialoguePresenter.Instance.ShowDialogue(
                    speakerName: "RANGER ROBIN",
                    lines: lines,
                    showChoicesAtEnd: showChoices,
                    acceptLabel: acceptLabel,
                    declineLabel: declineLabel,
                    onAccept: () =>
                    {
                        if (dialogueDone) return;
                        dialogueDone = true;
                        onAccept?.Invoke();
                        CleanupRobinDialogueState("onAccept");
                        cleanupExecuted = true;
                    },
                    onDecline: () =>
                    {
                        if (dialogueDone) return;
                        dialogueDone = true;
                        rangerRobinController?.PlayReaction("Nod");
                        onDecline?.Invoke();
                        CleanupRobinDialogueState("onDecline");
                        cleanupExecuted = true;
                    },
                    onComplete: () =>
                    {
                        if (dialogueDone) return;
                        dialogueDone = true;
                        if (questState == CityDetectiveState.ReadyToTurnIn)
                        {
                            FinalizeAttemptAndShowResults();
                        }
                        CleanupRobinDialogueState("onComplete");
                        cleanupExecuted = true;
                    });

                // Wait while dialogue remains open so the finally block covers every exit path
                while (!dialogueDone &&
                       DialoguePresenter.Instance != null &&
                       (DialoguePresenter.Instance.IsInDialogue || DialoguePresenter.Instance.IsSuspended))
                {
                    yield return null;
                }
            }
            finally
            {
                activeDialogueSessionRoutine = null;
                if (!cleanupExecuted)
                {
                    CleanupRobinDialogueState("finally");
                }
            }
        }

        private void CloseRangerRobinDialogue()
        {
            CleanupRobinDialogueState("CloseRangerRobinDialogue");
        }

        // Same freeze as Guardian (NPCInteraction.FreezePlayerControls) / Litterbug (LockPlayerMovement):
        // no walking, looking, jumping or sprinting while Robin's dialogue is open. Cursor is left to DialoguePresenter/ModalManager.
        private void SetPlayerControlFrozen(bool frozen)
        {
            if (playerTransform == null) return;
            var fps = playerTransform.GetComponent<StarterAssets.FirstPersonController>();
            if (fps != null) fps.enabled = !frozen;
            var inputs = playerTransform.GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.move = Vector2.zero;
                inputs.look = Vector2.zero;
                inputs.jump = false;
                inputs.sprint = false;
                inputs.cursorInputForLook = !frozen;
            }
        }

        private void CleanupRobinDialogueState(string exitReason)
        {
            dialogueState = RobinDialogueState.Exiting;
            try
            {
                if (robinDialogueCam != null)
                {
                    robinDialogueCam.Priority.Value = 0;
                }

                EcoQuest.Shared.DialogueCameraRig.Instance.ExitDialogue("RangerRobin", 0.6f);

                if (letterboxRoutine != null) StopCoroutine(letterboxRoutine);
                if (isActiveAndEnabled)
                {
                    letterboxRoutine = StartCoroutine(TransitionLetterbox(false, 0.6f));
                }

                rangerRobinController?.StopTalk();
                rangerRobinController?.SetLookAtTarget(null);

                modalCloseCooldownTimer = 0.35f;
                UIOrchestrator.Instance?.ExitCinematicProfile("RangerRobin");

                if (questState == CityDetectiveState.Active && ui != null)
                {
                    bool userHidden = UIOrchestrator.Instance != null && UIOrchestrator.Instance.IsQuestPanelUserHidden;
                    if (!userHidden)
                    {
                        ui.ShowChecklist(activeChecklist, collectedCount, remainingSeconds, remainingHintCharges, currentHintCooldown);
                    }
                }

                UpdateRobinMarkerVisibility();
                UIOrchestrator.Instance?.ApplyCurrentProfile(0.20f);
            }
            finally
            {
                SetPlayerControlFrozen(false);
                dialogueState = RobinDialogueState.Idle;
                robinInteractionCooldownUntil = Time.unscaledTime + 2.0f;
#if UNITY_EDITOR
                if (isActiveAndEnabled && Application.isPlaying)
                {
                    StartCoroutine(TracePostCloseSnapshotsRoutine(exitReason));
                }
#endif
            }
        }

#if UNITY_EDITOR
        private void LogFlowTraceSnapshot(string label)
        {
            var orch = UIOrchestrator.Instance;
            var hud = HudVisibilityService.Instance;
            var modal = ModalManager.Instance;
            string reasons = hud != null ? string.Join(",", hud.ActiveReasons) : "none";
            string overlays = orch != null ? orch.ActiveOverlaysSummary : "none";
            int modalCount = modal != null ? ((modal.ActiveModal != null ? 1 : 0) + modal.ModalStackCount) : 0;
            bool clActive = ui != null && ui.checklistCanvasGroup != null && ui.checklistCanvasGroup.gameObject.activeInHierarchy;
            float clAlpha = ui != null && ui.checklistCanvasGroup != null ? ui.checklistCanvasGroup.alpha : -1f;
            Debug.Log($"[FlowTrace] [{label}] robinState={dialogueState}, overlays=[{overlays}], flowStep={(orch != null ? orch.CurrentFlowStep.ToString() : "null")}, phase={(orch != null ? orch.CurrentPhase.ToString() : "null")}, modalCount={modalCount}, hudReasons=[{reasons}], blurWeight={UIBackgroundBlur.GetWeight():F2}, checklist(active={clActive}, alpha={clAlpha:F2}, userHidden={(orch != null && orch.IsQuestPanelUserHidden)})");
        }

        private IEnumerator TracePostCloseSnapshotsRoutine(string exitReason)
        {
            yield return new WaitForSecondsRealtime(0.20f);
            LogFlowTraceSnapshot($"Close+0.2s({exitReason})");
            yield return new WaitForSecondsRealtime(0.80f);
            LogFlowTraceSnapshot($"Close+1.0s({exitReason})");
        }
#endif

        private IEnumerator TransitionLetterbox(bool enter, float dur)
        {
            if (topLetterboxBar == null || bottomLetterboxBar == null) yield break;

            Vector2 topStart = topLetterboxBar.anchoredPosition;
            Vector2 topEnd = enter ? Vector2.zero : new Vector2(0f, 86f);
            Vector2 botStart = bottomLetterboxBar.anchoredPosition;
            Vector2 botEnd = enter ? Vector2.zero : new Vector2(0f, -86f);

            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / dur);
                float smooth = Mathf.SmoothStep(0f, 1f, t);
                topLetterboxBar.anchoredPosition = Vector2.Lerp(topStart, topEnd, smooth);
                bottomLetterboxBar.anchoredPosition = Vector2.Lerp(botStart, botEnd, smooth);
                yield return null;
            }

            topLetterboxBar.anchoredPosition = topEnd;
            bottomLetterboxBar.anchoredPosition = botEnd;
            letterboxRoutine = null;
        }

        // =====================================================================
        // ATTEMPT LIFECYCLE
        // =====================================================================

        public void StartNewAttempt()
        {
            if (database == null || database.items == null || database.items.Count < 10)
            {
                Debug.LogError("[CityTrashDetectiveManager] Database missing or contains fewer than 10 items!");
                return;
            }

            currentAttemptSeed = Environment.TickCount;
            PlayerPrefs.SetInt(EcoQuestSaveKeys.CityLastSeed, currentAttemptSeed);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.CityBonusPending, 0);
            PlayerPrefs.Save();

            // Clear old attempt
            for (int i = 0; i < pickupPool.Count; i++)
            {
                if (database != null) pickupPool[i].PrewarmVisuals(database);
                pickupPool[i].ReturnToPoolImmediate();
            }
            activeChecklist.Clear();
            lastCollectedItemsForQuiz.Clear();

            // Reset timers & charges
            remainingSeconds = questDurationSeconds;
            collectedCount = 0;
            isFinalizingQuest = false;
            remainingHintCharges = maxHintCharges;
            currentHintCooldown = 0f;
            currentRunPickupCoins = 0;
            hintActiveTimer = 0f;
            activeHintTarget = null;

            // Generate checklist & assigns spawn points
            GenerateChecklistAndSpawns(currentAttemptSeed, database, allSpawnPoints, activeChecklist);

            // Spawn 10 pooled trash items
            for (int i = 0; i < activeChecklist.Count; i++)
            {
                var slot = activeChecklist[i];
                if (i < pickupPool.Count)
                {
                    slot.pickupInstance = pickupPool[i];
                    slot.pickupInstance.ActivateAtSpawn(i, slot.item, slot.spawnPoint, currentAttemptSeed + i * 31);
                }
            }

            questState = CityDetectiveState.Active;
            UpdateRobinMarkerVisibility();

            if (UIOrchestrator.Instance != null)
            {
                UIOrchestrator.Instance.SetQuestPanelUserHidden(false, "CityScavenger Attempt Started");
                UIOrchestrator.Instance.SetPhase(UIOrchestrator.GamePhase.QuestActive, "CityScavenger Attempt Started");
            }

            if (ui != null)
            {
                ui.ShowChecklist(activeChecklist, collectedCount, remainingSeconds, remainingHintCharges, currentHintCooldown);
            }

            // First-time non-blocking tip
            if (PlayerPrefs.GetInt("EcoQuest_CityTip_Checklist", 0) == 0)
            {
                PlayerPrefs.SetInt("EcoQuest_CityTip_Checklist", 1);
                PlayerPrefs.Save();
                ui?.ShowHintToast("Find the items on your list. Press E to collect.", 3.5f);
            }
        }

        public void CancelActiveAttempt()
        {
            for (int i = 0; i < pickupPool.Count; i++)
            {
                if (database != null) pickupPool[i].PrewarmVisuals(database);
                pickupPool[i].ReturnToPoolImmediate();
            }
            activeChecklist.Clear();
            hintActiveTimer = 0f;
            activeHintTarget = null;
            questState = CityDetectiveState.NotStarted;

            ui?.HideChecklist();
            ui?.UpdateCompassHud(false, Vector3.zero, Vector3.forward, Vector3.zero, "", CityDistrict.Park, 0f);
            UpdateRobinMarkerVisibility();

            if (UIOrchestrator.Instance != null)
            {
                UIOrchestrator.Instance.SetPhase(UIOrchestrator.GamePhase.FreePlay, "CityScavenger Cancelled");
            }
        }

        // =====================================================================
        // CHECKLIST GENERATION ALGORITHM
        // =====================================================================

        public static bool GenerateChecklistAndSpawns(
            int seed,
            CityTrashDatabase db,
            List<CityTrashSpawnPoint> spawnPoints,
            List<ChecklistSlot> outSlots)
        {
            outSlots.Clear();
            if (db == null || db.items == null || db.items.Count < 10) return false;

            System.Random rng = new System.Random(seed);

            List<CityTrashItemDef> bio = new List<CityTrashItemDef>();
            List<CityTrashItemDef> rec = new List<CityTrashItemDef>();
            List<CityTrashItemDef> nonBio = new List<CityTrashItemDef>();
            List<CityTrashItemDef> hazRare = new List<CityTrashItemDef>();

            for (int i = 0; i < db.items.Count; i++)
            {
                var it = db.items[i];
                if (it == null) continue;
                switch (it.category)
                {
                    case LitterCategory.Biodegradable: bio.Add(it); break;
                    case LitterCategory.Recyclable: rec.Add(it); break;
                    case LitterCategory.NonBiodegradable: nonBio.Add(it); break;
                    case LitterCategory.Hazardous: hazRare.Add(it); break;
                }
            }

            ShuffleList(bio, rng);
            ShuffleList(rec, rng);
            ShuffleList(nonBio, rng);
            ShuffleList(hazRare, rng);

            int hazCount = (rng.Next(2) == 0) ? 1 : 2;
            hazCount = Mathf.Min(hazCount, hazRare.Count);

            int bioCount = 3;
            int recCount = 3;
            int nonBioCount = 10 - hazCount - bioCount - recCount;

            List<CityTrashItemDef> chosenItems = new List<CityTrashItemDef>(10);
            for (int i = 0; i < bioCount && i < bio.Count; i++) chosenItems.Add(bio[i]);
            for (int i = 0; i < recCount && i < rec.Count; i++) chosenItems.Add(rec[i]);
            for (int i = 0; i < nonBioCount && i < nonBio.Count; i++) chosenItems.Add(nonBio[i]);
            for (int i = 0; i < hazCount && i < hazRare.Count; i++) chosenItems.Add(hazRare[i]);

            if (chosenItems.Count < 10)
            {
                for (int i = 0; i < db.items.Count && chosenItems.Count < 10; i++)
                {
                    if (!chosenItems.Contains(db.items[i])) chosenItems.Add(db.items[i]);
                }
            }

            ShuffleList(chosenItems, rng);

            Dictionary<CityDistrict, List<CityTrashSpawnPoint>> districtSpawns = new Dictionary<CityDistrict, List<CityTrashSpawnPoint>>();
            foreach (CityDistrict d in Enum.GetValues(typeof(CityDistrict)))
            {
                districtSpawns[d] = new List<CityTrashSpawnPoint>();
            }
            if (spawnPoints != null)
            {
                for (int i = 0; i < spawnPoints.Count; i++)
                {
                    var sp = spawnPoints[i];
                    if (sp != null && sp.isVerifiedValid)
                    {
                        districtSpawns[sp.district].Add(sp);
                    }
                }
            }
            foreach (var kv in districtSpawns)
            {
                ShuffleList(kv.Value, rng);
            }

            int[] districtCounts = new int[5];
            CityDistrict[] assignedDistricts = new CityDistrict[chosenItems.Count];

            List<CityDistrict> initialDistricts = new List<CityDistrict>
            {
                CityDistrict.Park,
                CityDistrict.MainStreet,
                CityDistrict.Market,
                CityDistrict.Riverside,
                CityDistrict.Residential
            };
            ShuffleList(initialDistricts, rng);

            bool[] itemAssigned = new bool[chosenItems.Count];
            for (int dIdx = 0; dIdx < initialDistricts.Count && dIdx < chosenItems.Count; dIdx++)
            {
                CityDistrict targetD = initialDistricts[dIdx];
                int bestItemIdx = -1;
                float bestW = -1f;
                for (int i = 0; i < chosenItems.Count; i++)
                {
                    if (itemAssigned[i]) continue;
                    float w = chosenItems[i].GetWeightForDistrict(targetD) + (float)rng.NextDouble() * 0.25f;
                    if (w > bestW)
                    {
                        bestW = w;
                        bestItemIdx = i;
                    }
                }
                if (bestItemIdx >= 0)
                {
                    itemAssigned[bestItemIdx] = true;
                    assignedDistricts[bestItemIdx] = targetD;
                    districtCounts[(int)targetD]++;
                }
            }

            for (int i = 0; i < chosenItems.Count; i++)
            {
                if (itemAssigned[i]) continue;

                CityDistrict bestD = CityDistrict.Park;
                float bestScore = -1f;
                foreach (CityDistrict d in Enum.GetValues(typeof(CityDistrict)))
                {
                    if (districtCounts[(int)d] >= 3) continue;
                    if (districtSpawns[d].Count <= districtCounts[(int)d] && spawnPoints != null && spawnPoints.Count > 0) continue;

                    float w = chosenItems[i].GetWeightForDistrict(d) * (1f + (float)rng.NextDouble() * 0.35f);
                    if (w > bestScore)
                    {
                        bestScore = w;
                        bestD = d;
                    }
                }

                if (bestScore < 0f)
                {
                    foreach (CityDistrict d in Enum.GetValues(typeof(CityDistrict)))
                    {
                        if (districtCounts[(int)d] < 3)
                        {
                            bestD = d;
                            break;
                        }
                    }
                }

                itemAssigned[i] = true;
                assignedDistricts[i] = bestD;
                districtCounts[(int)bestD]++;
            }

            HashSet<CityTrashSpawnPoint> usedSpawns = new HashSet<CityTrashSpawnPoint>();
            for (int i = 0; i < chosenItems.Count; i++)
            {
                CityDistrict d = assignedDistricts[i];
                CityTrashSpawnPoint chosenSp = null;
                var list = districtSpawns[d];
                for (int s = 0; s < list.Count; s++)
                {
                    if (!usedSpawns.Contains(list[s]))
                    {
                        chosenSp = list[s];
                        usedSpawns.Add(chosenSp);
                        break;
                    }
                }

                outSlots.Add(new ChecklistSlot
                {
                    item = chosenItems[i],
                    district = d,
                    spawnPoint = chosenSp,
                    pickupInstance = null,
                    collected = false
                });
            }

            return outSlots.Count == 10;
        }

        private static void ShuffleList<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        // =====================================================================
        // PICKUP & COLLECTION
        // =====================================================================

        public void CollectChecklistSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= activeChecklist.Count) return;
            var slot = activeChecklist[slotIndex];
            if (slot.collected) return;

            slot.collected = true;
            collectedCount++;
            currentRunPickupCoins += coinsPerPickup;
            AwardCoins(coinsPerPickup);

            lastCollectedItemsForQuiz.Add(slot.item);

            if (slot.pickupInstance != null)
            {
                slot.pickupInstance.PlayPickupAndPool();
            }

            if (UITheme.Instance != null && UITheme.Instance.coinPickupSound != null)
            {
                UITheme.PlaySound(UITheme.Instance.coinPickupSound, 0.9f);
            }
            else
            {
                AudioManager.Instance?.PlayButtonClick();
            }
            WorldInteractionPrompt.Instance?.HidePrompt();

            if (ui != null)
            {
                ui.RefreshChecklistRows(activeChecklist, collectedCount);
                ui.EnqueueTrashIdCard(slot.item, coinsPerPickup);
            }

            if (collectedCount >= 10)
            {
                questState = CityDetectiveState.ReadyToTurnIn;
                UpdateRobinMarkerVisibility();
                rangerRobinController?.PlayReaction("Cheer");
                if (!isFinalizingQuest)
                {
                    StartCoroutine(CompleteQuestAllItemsFoundRoutine());
                }
            }
        }

        private IEnumerator CompleteQuestAllItemsFoundRoutine()
        {
            if (isFinalizingQuest) yield break;
            isFinalizingQuest = true;

            // Brief pause so the player sees the 10th row checked off and the pickup toast
            yield return new WaitForSeconds(0.85f);

            FinalizeAttemptAndShowResults();
            isFinalizingQuest = false;
        }

        // =====================================================================
        // COMPASS HINTS
        // =====================================================================

        public void TryTriggerCompassHint()
        {
            if (remainingHintCharges <= 0 || currentHintCooldown > 0f) return;

            int bestSlot = -1;
            float bestDist = float.MaxValue;
            Vector3 pPos = playerTransform != null ? playerTransform.position : Vector3.zero;

            for (int i = 0; i < activeChecklist.Count; i++)
            {
                var slot = activeChecklist[i];
                if (slot.collected || slot.pickupInstance == null || !slot.pickupInstance.gameObject.activeSelf)
                    continue;

                float d = Vector3.Distance(pPos, slot.pickupInstance.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestSlot = i;
                }
            }

            if (bestSlot < 0) return;

            remainingHintCharges--;
            currentHintCooldown = hintCooldownSeconds;

            var targetSlot = activeChecklist[bestSlot];
            activeHintTarget = targetSlot.pickupInstance;
            hintActiveTimer = hintActiveDuration;

            if (UITheme.Instance != null && UITheme.Instance.modalOpenSound != null)
            {
                UITheme.PlaySound(UITheme.Instance.modalOpenSound, 0.7f);
            }
            else
            {
                AudioManager.Instance?.PlayButtonClick();
            }

            if (ui != null)
            {
                ui.HighlightHintRow(bestSlot, hintActiveDuration, activeChecklist, collectedCount);
            }

            // First-time hint tip
            if (PlayerPrefs.GetInt("EcoQuest_CityTip_Hint", 0) == 0)
            {
                PlayerPrefs.SetInt("EcoQuest_CityTip_Hint", 1);
                PlayerPrefs.Save();
            }
        }

        // =====================================================================
        // TIMER EXPIRY, STAR RATING & BONUS QUIZ LAUNCH
        // =====================================================================

        private void OnTimerExpired()
        {
            for (int i = 0; i < pickupPool.Count; i++)
            {
                if (database != null) pickupPool[i].PrewarmVisuals(database);
                pickupPool[i].ReturnToPoolImmediate();
            }
            hintActiveTimer = 0f;
            activeHintTarget = null;
            FinalizeAttemptAndShowResults();
        }

        public static void EvaluateStarsAndBonus(int foundCount, float secondsLeft, out int stars, out int bonusCoins)
        {
            // Scaled star threshold: 60s * (4/3) = 80s
            if (foundCount >= 10 && secondsLeft >= 80f)
            {
                stars = 3;
                bonusCoins = 60;
            }
            else if (foundCount >= 10)
            {
                stars = 2;
                bonusCoins = 35;
            }
            else if (foundCount >= 7)
            {
                stars = 1;
                bonusCoins = 15;
            }
            else
            {
                stars = 0;
                bonusCoins = 0;
            }
        }

        private void FinalizeAttemptAndShowResults()
        {
            EvaluateStarsAndBonus(collectedCount, remainingSeconds, out int stars, out int starBonusCoins);
            lastRunStars = stars;

            if (starBonusCoins > 0)
            {
                AwardCoins(starBonusCoins);
            }

            // Check & save Personal Best
            int prevBestStars = PlayerPrefs.GetInt(EcoQuestSaveKeys.CityBestStars, 0);
            float prevBestTime = PlayerPrefs.GetFloat(EcoQuestSaveKeys.CityBestTime, 0f);
            bool isNewBest = false;

            if (stars > prevBestStars || (stars == prevBestStars && stars > 0 && remainingSeconds > prevBestTime))
            {
                isNewBest = true;
                PlayerPrefs.SetInt(EcoQuestSaveKeys.CityBestStars, stars);
                PlayerPrefs.SetFloat(EcoQuestSaveKeys.CityBestTime, remainingSeconds);
            }

            bool canTakeBonusQuiz = (stars >= 1);
            if (canTakeBonusQuiz)
            {
                PlayerPrefs.SetInt(EcoQuestSaveKeys.CityBonusPending, 1);
            }
            PlayerPrefs.Save();

            questState = (stars >= 1) ? CityDetectiveState.Completed : CityDetectiveState.NotStarted;
            ui?.HideChecklist();
            UpdateRobinMarkerVisibility();

            if (UIOrchestrator.Instance != null)
            {
                UIOrchestrator.Instance.SetPhase(UIOrchestrator.Instance.EvaluateLivePhase("CityScavenger Finished"), "CityScavenger Finished");
            }

            if (ui != null)
            {
                ui.ShowResultsModal(
                    collectedCount,
                    remainingSeconds,
                    stars,
                    currentRunPickupCoins,
                    starBonusCoins,
                    isNewBest,
                    canTakeBonusQuiz,
                    () =>
                    {
                        ui.HideResultsModal();
                        modalCloseCooldownTimer = 0.25f;
                        LaunchBonusQuiz();
                    },
                    () =>
                    {
                        ui.HideResultsModal();
                        modalCloseCooldownTimer = 0.25f;
                        questState = CityDetectiveState.NotStarted;
                        UpdateRobinMarkerVisibility();
                        UIOrchestrator.Instance?.ApplyCurrentProfile(0.20f);
                    },
                    () =>
                    {
                        ui.HideResultsModal();
                        modalCloseCooldownTimer = 0.25f;
                        UIOrchestrator.Instance?.ApplyCurrentProfile(0.20f);
                    });
            }
        }

        public void LaunchBonusQuiz()
        {
            if (bonusQuizAdapter == null)
            {
                bonusQuizAdapter = GetComponentInChildren<CityBonusQuizAdapter>(true);
            }
            if (bonusQuizAdapter == null) return;

            bonusQuizAdapter.StartBonusQuiz(lastCollectedItemsForQuiz, database, () =>
            {
                modalCloseCooldownTimer = 0.25f;
                UIOrchestrator.Instance?.ApplyCurrentProfile(0.20f);
            });
        }

        private static void AwardCoins(int amount)
        {
            if (amount <= 0) return;
            var currency = PlayerCurrency.Instance ?? UnityEngine.Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            if (currency != null)
            {
                if (amount >= 10)
                {
                    currency.AddGoldAnimated(amount, 0.75f);
                }
                else
                {
                    currency.AddGold(amount);
                }
            }
            else
            {
                int cur = PlayerPrefs.GetInt("PlayerGold", PlayerPrefs.GetInt("PlayerCoins", 0));
                cur += amount;
                PlayerPrefs.SetInt("PlayerGold", cur);
                PlayerPrefs.SetInt("PlayerCoins", cur);
                PlayerPrefs.Save();
            }
        }
    }
}
