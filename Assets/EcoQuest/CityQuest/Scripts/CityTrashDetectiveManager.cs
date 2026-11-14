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
        private static CityTrashDetectiveManager _instance;
        private static int _instanceEpoch;
        public static CityTrashDetectiveManager Instance
        {
            get => SceneSingletonLookup.GetOrFind(ref _instance, ref _instanceEpoch, includeInactive: false);
            private set
            {
                _instance = value;
                if (value == null) _instanceEpoch = 0;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
            IsRobinCooldownDialogueOpen = false;
            _cooldownInitialized = false;
            _cooldownRemainingSeconds = 0f;
            _lastCooldownTickFrame = -1;
            _lastCooldownSaveUnscaledTime = -100f;
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
        public float npcInteractRange = 3.8f;

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
        public float pickupInteractRange = 3.0f;
        [Tooltip("Cooldown duration in seconds after Trash Detective ends (default 300s = 5:00).")]
        public float repeatCooldownSeconds = 300f;
        [HideInInspector]
        public float repeatCooldownMinutes = 5f;
        public int maxHintCharges = 4;
        public float hintCooldownSeconds = 25f;
        public float hintActiveDuration = 6.0f;
        public int coinsPerPickup = 5;

        [Header("Marker Assets")]
        public Sprite hourglassMarkerSprite;

        [Header("Runtime State (Read-Only)")]
        [SerializeField] private CityDetectiveState questState = CityDetectiveState.NotStarted;
        [SerializeField] private float remainingSeconds = 240f;
        [SerializeField] private int collectedCount = 0;
        [SerializeField] private int remainingHintCharges = 4;
        [SerializeField] private float currentHintCooldown = 0f;
        [SerializeField] private int currentAttemptSeed = 0;
        [SerializeField] private int currentRunPickupCoins = 0;
        [SerializeField] private int lastRunStars = 0;

        public static event System.Action OnDetectiveQuestStateChanged;

        public CityDetectiveState QuestState => questState;
        // Ranger Robin's city quest is optional; only show the guide arrow when returning to turn in an active run.
        public bool IsRobinReadyForArrow => questState == CityDetectiveState.ReadyToTurnIn;
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
        private float lastMidQuestSaveUnscaledTime = -100f;

        [Serializable]
        private struct SavedRobinSlot
        {
            public string itemId;
            public int district;
            public string spawnName;
            public float x;
            public float y;
            public float z;
            public bool collected;
        }

        [Serializable]
        private struct SavedRobinState
        {
            public int attemptSeed;
            public List<SavedRobinSlot> slots;
        }

        private static bool _cooldownInitialized = false;
        private static float _cooldownRemainingSeconds = 0f;
        private static int _lastCooldownTickFrame = -1;
        private static float _lastCooldownSaveUnscaledTime = -100f;
        private static Sprite _fallbackHourglassSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDetectiveEvents()
        {
            OnDetectiveQuestStateChanged = null;
        }

        public static bool IsRobinCooldownDialogueOpen { get; private set; }

        public bool IsOverlayOrModalOpen =>
            (ui != null && ui.IsAnyModalOpen) ||
            (bonusQuizAdapter != null && bonusQuizAdapter.IsQuizRunning) ||
            (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue);

        private void Awake()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene(gameObject.scene))
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

        private void OnDisable()
        {
            StopAllCoroutines();
            activeDialogueSessionRoutine = null;
            letterboxRoutine = null;
            dialogueWatchdogRoutine = null;
            IsRobinCooldownDialogueOpen = false;
            FlushRobinCooldownSave();
            FlushMidQuestSaveNow();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                FlushRobinCooldownSave();
                FlushMidQuestSaveNow();
            }
        }

        private void OnApplicationQuit()
        {
            FlushRobinCooldownSave();
            FlushMidQuestSaveNow();
        }

        private void OnDestroy()
        {
            FlushRobinCooldownSave();
            FlushMidQuestSaveNow();
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
            if (!CityDistrictVolume.IsInAllowedCityScene(gameObject.scene)) return;

            FindSceneReferences();
            CacheDistrictVolumes();
            CacheSpawnPoints();
            CachePickupPool();
            EnsureRobinHeadMarker();
            bool restored = RestoreMidQuestProgressIfSaved();
            UpdateRobinMarkerVisibility();
            OnDetectiveQuestStateChanged?.Invoke();
            ObjectiveMarkerService.Instance?.RefreshTargetFromState();
            if (restored)
            {
                RefreshRestoredQuestHudAndMarkers();
            }
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

        private bool IsInteractKeyPressed()
        {
            if (Input.GetKeyDown(KeyCode.E)) return true;
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame) return true;
#endif
            return false;
        }

        private void FindSceneReferences()
        {
            if (playerTransform == null || playerTransform.GetComponent<CharacterController>() == null)
            {
                playerTransform = ResolvePlayerTransform();
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

            if (districtVolumes.Count == 0)
            {
                var vols = FindObjectsByType<CityDistrictVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < vols.Length; i++)
                {
                    if (vols[i] != null && vols[i].gameObject.scene == gameObject.scene)
                    {
                        districtVolumes.Add(vols[i]);
                    }
                }
            }
        }

        private void CacheSpawnPoints()
        {
            allSpawnPoints.Clear();
            if (spawnPointsRoot != null)
            {
                spawnPointsRoot.GetComponentsInChildren(true, allSpawnPoints);
            }

            if (allSpawnPoints.Count == 0)
            {
                var sps = FindObjectsByType<CityTrashSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < sps.Length; i++)
                {
                    if (sps[i] != null && sps[i].gameObject.scene == gameObject.scene)
                    {
                        allSpawnPoints.Add(sps[i]);
                    }
                }
            }
        }

        private void CachePickupPool()
        {
            pickupPool.Clear();
            if (poolRoot != null)
            {
                poolRoot.GetComponentsInChildren(true, pickupPool);
            }

            if (pickupPool.Count == 0)
            {
                var insts = FindObjectsByType<CityTrashPickupInstance>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < insts.Length; i++)
                {
                    if (insts[i] != null && insts[i].gameObject.scene == gameObject.scene)
                    {
                        pickupPool.Add(insts[i]);
                    }
                }
            }

            if (pickupPool.Count < 10)
            {
                Transform parent = poolRoot != null ? poolRoot : transform;
                while (pickupPool.Count < 10)
                {
                    var go = new GameObject($"RuntimePickupInstance_{pickupPool.Count}");
                    go.transform.SetParent(parent, false);
                    var inst = go.AddComponent<CityTrashPickupInstance>();
                    pickupPool.Add(inst);
                }
            }

            for (int i = 0; i < pickupPool.Count; i++)
            {
                if (database != null) pickupPool[i].PrewarmVisuals(database);
                pickupPool[i].ReturnToPoolImmediate();
            }
        }

        private void EnsureRobinHeadMarker()
        {
            if (rangerRobinTransform == null) return;

            Transform existing = rangerRobinTransform.Find("RobinQuestMarker");
            GameObject markerRoot = existing != null ? existing.gameObject : null;
            if (markerRoot == null)
            {
                markerRoot = new GameObject("RobinQuestMarker");
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
            }

            robinHeadMarker = markerRoot.transform;

            Transform hourglassTr = robinHeadMarker.Find("HourglassIcon");
            if (hourglassTr == null)
            {
                GameObject hgGo = new GameObject("HourglassIcon");
                hgGo.transform.SetParent(robinHeadMarker, false);
                hgGo.transform.localPosition = Vector3.zero;
                hgGo.transform.localScale = Vector3.one * 0.62f;
                var sr = hgGo.AddComponent<SpriteRenderer>();
                sr.sprite = GetOrCreateHourglassSprite();
                sr.color = Color.white;
                sr.sortingOrder = 10;
                hgGo.SetActive(false);
            }
            else
            {
                var sr = hourglassTr.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite == null)
                {
                    sr.sprite = GetOrCreateHourglassSprite();
                }
            }
        }

        private Sprite GetOrCreateHourglassSprite()
        {
            if (hourglassMarkerSprite != null) return hourglassMarkerSprite;
#if UNITY_EDITOR
            var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/EcoQuest/UI/Icons/Icon_Hourglass.png");
            if (loaded != null)
            {
                hourglassMarkerSprite = loaded;
                return loaded;
            }
#endif
            if (_fallbackHourglassSprite != null) return _fallbackHourglassSprite;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color clear = new Color(0f, 0f, 0f, 0f);
            Color outline = new Color(0.10f, 0.14f, 0.10f, 0.90f);
            Color frame = new Color(1f, 0.96f, 0.80f, 1f);
            Color sand = new Color(1f, 0.85f, 0.28f, 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx = x + 0.5f;
                    float cy = y + 0.5f;
                    Color px = clear;
                    bool topCap = (cy >= 48f && cy <= 54f && cx >= 14f && cx <= 50f);
                    bool botCap = (cy >= 10f && cy <= 16f && cx >= 14f && cx <= 50f);
                    bool inGlass = (cy >= 16f && cy <= 48f);
                    float dx = Mathf.Abs(cx - 32f);
                    float t = Mathf.Abs(cy - 32f) / 16f;
                    float halfW = 3f + 13f * Mathf.Pow(t, 0.72f);

                    if (topCap || botCap) px = frame;
                    else if (inGlass && dx <= halfW && dx >= halfW - 2.2f) px = frame;
                    else if (inGlass && dx < halfW - 2.2f && ((cy >= 32f && cy <= 40f) || (cy >= 16f && cy <= 23f && dx <= (23f - cy) * 1.4f))) px = sand;
                    else if (inGlass && cy >= 23f && cy <= 32f && dx <= 1.2f) px = sand;
                    else if ((cy >= 46f && cy <= 56f && cx >= 12f && cx <= 52f) || (cy >= 8f && cy <= 18f && cx >= 12f && cx <= 52f) || (inGlass && dx <= halfW + 1.8f && dx > halfW - 2.5f)) px = outline;

                    tex.SetPixel(x, y, px);
                }
            }
            tex.Apply();
            _fallbackHourglassSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size / 1.2f);
            return _fallbackHourglassSprite;
        }

        private void UpdateRobinMarkerVisibility()
        {
            if (robinHeadMarker == null) EnsureRobinHeadMarker();
            if (robinHeadMarker == null) return;

            bool isBlocked = (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue)
                || CursorState.IsAnyBlockingUIOpen();
            bool show = !isBlocked && (questState == CityDetectiveState.NotStarted ||
                                       questState == CityDetectiveState.ReadyToTurnIn ||
                                       questState == CityDetectiveState.Completed);

            robinHeadMarker.gameObject.SetActive(show);

            bool onCooldown = questState != CityDetectiveState.ReadyToTurnIn && IsOnRobinCooldown(out _);

            var tmp = robinHeadMarker.GetComponentInChildren<TextMeshPro>(true);
            if (tmp != null)
            {
                tmp.gameObject.SetActive(!onCooldown);
                if (!onCooldown)
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

            Transform hg = robinHeadMarker.Find("HourglassIcon");
            if (hg != null)
            {
                hg.gameObject.SetActive(show && onCooldown);
            }
        }

        private void Update()
        {
            TickRobinCooldownGlobal();

            if (!CityDistrictVolume.IsInAllowedCityScene(gameObject.scene)) return;

            if (playerTransform == null || playerTransform.GetComponent<CharacterController>() == null)
            {
                playerTransform = ResolvePlayerTransform();
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

                    if (unscaledTime - lastMidQuestSaveUnscaledTime >= 1.0f)
                    {
                        SaveMidQuestProgress(saveToDisk: false);
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
                var inputs = playerTransform != null ? playerTransform.GetComponent<StarterAssets.StarterAssetsInputs>() : FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
                if (Cursor.lockState != CursorLockMode.None || !Cursor.visible || (inputs != null && (inputs.cursorLocked || inputs.cursorInputForLook)))
                {
                    CursorState.SetLocked(false, inputs);
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
                    var inputs = playerTransform != null ? playerTransform.GetComponent<StarterAssets.StarterAssetsInputs>() : FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
                    CursorState.SetLocked(true, inputs);
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

                    if (modalCloseCooldownTimer <= 0f && IsInteractKeyPressed())
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
                float effectiveRange = Mathf.Max(npcInteractRange, 3.8f);
                if (distSq <= effectiveRange * effectiveRange)
                {
                    WorldInteractionPrompt.Instance?.ShowPrompt(
                        rangerRobinTransform,
                        npcDisplayName,
                        "Talk",
                        "E");

                    if (modalCloseCooldownTimer <= 0f && IsInteractKeyPressed())
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

        private static void EnsureCooldownLoaded()
        {
            if (_cooldownInitialized) return;
            _cooldownInitialized = true;
            if (PlayerPrefs.HasKey(EcoQuestSaveKeys.CityRobinCooldownLegacyUntil))
            {
                PlayerPrefs.DeleteKey(EcoQuestSaveKeys.CityRobinCooldownLegacyUntil);
            }
            _cooldownRemainingSeconds = Mathf.Max(0f, PlayerPrefs.GetFloat(EcoQuestSaveKeys.CityRobinCooldownRemaining, 0f));
        }

        public static void ClearCooldownCache()
        {
            _cooldownInitialized = false;
            _cooldownRemainingSeconds = 0f;
            _lastCooldownTickFrame = -1;
            _lastCooldownSaveUnscaledTime = -100f;
        }

        public float GetConfiguredCooldownDurationSeconds()
        {
            if (repeatCooldownSeconds > 0f) return repeatCooldownSeconds;
            return Mathf.Max(0f, repeatCooldownMinutes * 60f);
        }

        public static string FormatCooldownMS(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        public static bool IsRobinCooldownActive(out float remainingSeconds)
        {
            EnsureCooldownLoaded();
            remainingSeconds = _cooldownRemainingSeconds;
            return remainingSeconds > 0.01f;
        }

        public bool IsOnRobinCooldown(out TimeSpan remaining)
        {
            if (IsRobinCooldownActive(out float sec))
            {
                remaining = TimeSpan.FromSeconds(sec);
                return true;
            }
            remaining = TimeSpan.Zero;
            return false;
        }

        public void StartRobinCooldown(float? overrideSeconds = null)
        {
            float duration = overrideSeconds.HasValue ? Mathf.Max(0f, overrideSeconds.Value) : GetConfiguredCooldownDurationSeconds();
            _cooldownInitialized = true;
            _cooldownRemainingSeconds = duration;
            _lastCooldownSaveUnscaledTime = Time.unscaledTime;
            SaveCooldownRemainingImmediate();
            UpdateRobinMarkerVisibility();
            OnDetectiveQuestStateChanged?.Invoke();
        }

        public static void SkipRobinCooldown()
        {
            _cooldownInitialized = true;
            _cooldownRemainingSeconds = 0f;
            if (PlayerPrefs.HasKey(EcoQuestSaveKeys.CityRobinCooldownRemaining))
            {
                PlayerPrefs.DeleteKey(EcoQuestSaveKeys.CityRobinCooldownRemaining);
            }
            if (PlayerPrefs.HasKey(EcoQuestSaveKeys.CityRobinCooldownLegacyUntil))
            {
                PlayerPrefs.DeleteKey(EcoQuestSaveKeys.CityRobinCooldownLegacyUntil);
            }
            PlayerPrefs.Save();
            if (Instance != null)
            {
                Instance.UpdateRobinMarkerVisibility();
            }
            OnDetectiveQuestStateChanged?.Invoke();
        }

        public static void FlushRobinCooldownSave()
        {
            if (!_cooldownInitialized) return;
            SaveCooldownRemainingImmediate();
        }

        private static void SaveCooldownRemainingImmediate()
        {
            if (_cooldownRemainingSeconds > 0.01f)
            {
                PlayerPrefs.SetFloat(EcoQuestSaveKeys.CityRobinCooldownRemaining, _cooldownRemainingSeconds);
            }
            else if (PlayerPrefs.HasKey(EcoQuestSaveKeys.CityRobinCooldownRemaining))
            {
                PlayerPrefs.DeleteKey(EcoQuestSaveKeys.CityRobinCooldownRemaining);
            }
            PlayerPrefs.Save();
        }

        public static bool ShouldTickRobinCooldown()
        {
            if (!Application.isPlaying) return false;
            if (PauseMenuController.IsPaused || Time.timeScale <= 0.001f) return false;
            if (LoadingScreenController.IsLoading) return false;
            if (EcoQuest.Inventory.InventoryController.Instance != null && EcoQuest.Inventory.InventoryController.Instance.IsOpen) return false;
            if (EcoQuest.Inventory.UI_EcoShop.Instance != null && EcoQuest.Inventory.UI_EcoShop.Instance.IsOpen) return false;
            if (LitterbugQuestManager.IsDialogueOpen || NPCInteraction.IsAnyGuardianFlowRunning) return false;
            if (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalActuallyVisible()) return false;

            if (Instance != null && ((Instance.ui != null && Instance.ui.IsAnyModalOpen) ||
                                     (Instance.bonusQuizAdapter != null && Instance.bonusQuizAdapter.IsQuizRunning)))
            {
                return false;
            }

            if (DialoguePresenter.Instance != null && (DialoguePresenter.Instance.IsInDialogue || DialoguePresenter.Instance.IsSuspended))
            {
                if (!IsRobinCooldownDialogueOpen) return false;
            }

            if (UIOrchestrator.Instance != null)
            {
                if (UIOrchestrator.Instance.AreModalsOpen) return false;
                if (UIOrchestrator.Instance.IsOverlayActive && !IsRobinCooldownDialogueOpen) return false;
            }

            return true;
        }

        public static void TickRobinCooldownGlobal(float? explicitUnscaledDelta = null)
        {
            int frame = Time.frameCount;
            if (!explicitUnscaledDelta.HasValue && frame == _lastCooldownTickFrame) return;
            _lastCooldownTickFrame = frame;

            EnsureCooldownLoaded();
            if (_cooldownRemainingSeconds <= 0f) return;
            if (!explicitUnscaledDelta.HasValue && !ShouldTickRobinCooldown()) return;

            float dt = explicitUnscaledDelta.HasValue ? Mathf.Max(0f, explicitUnscaledDelta.Value) : Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            _cooldownRemainingSeconds = Mathf.Max(0f, _cooldownRemainingSeconds - dt);

            if (_cooldownRemainingSeconds <= 0.001f)
            {
                _cooldownRemainingSeconds = 0f;
                SaveCooldownRemainingImmediate();
                if (Instance != null)
                {
                    Instance.UpdateRobinMarkerVisibility();
                }
                OnDetectiveQuestStateChanged?.Invoke();
            }
            else if (Time.unscaledTime - _lastCooldownSaveUnscaledTime >= 1.0f)
            {
                _lastCooldownSaveUnscaledTime = Time.unscaledTime;
                PlayerPrefs.SetFloat(EcoQuestSaveKeys.CityRobinCooldownRemaining, _cooldownRemainingSeconds);
            }
        }

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

                bool isCooldownDialogue = false;
                if (LitterbugQuestManager.Instance != null &&
                    (LitterbugQuestManager.Instance.QuestState == LitterbugQuestState.Active ||
                     LitterbugQuestManager.Instance.QuestState == LitterbugQuestState.ReadyToTurnIn))
                {
                    lines = new string[] { litterbugConflictDialogue };
                    showChoices = false;
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
                else if (IsRobinCooldownActive(out float cooldownRemaining))
                {
                    isCooldownDialogue = true;
                    IsRobinCooldownDialogueOpen = true;
                    bool hasPendingQuiz = PlayerPrefs.GetInt(EcoQuestSaveKeys.CityBonusPending, 0) == 1;
                    lines = new string[]
                    {
                        $"I'm sorting my field notes. Come back in {FormatCooldownMS(cooldownRemaining)}!"
                    };
                    if (hasPendingQuiz)
                    {
                        acceptLabel = "Take Bonus Quiz";
                        declineLabel = "Take Quiz Later";
                        showChoices = true;
                        onAccept = () => LaunchBonusQuiz();
                        onDecline = null;
                    }
                    else
                    {
                        showChoices = false;
                        acceptLabel = "OK";
                        declineLabel = "Close";
                        onAccept = null;
                        onDecline = null;
                    }
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
                else // Completed and cooldown elapsed
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

                int lastDisplayedCooldownSec = -1;
                // Wait while dialogue remains open so the finally block covers every exit path
                while (!dialogueDone &&
                       DialoguePresenter.Instance != null &&
                       (DialoguePresenter.Instance.IsInDialogue || DialoguePresenter.Instance.IsSuspended))
                {
                    if (isCooldownDialogue)
                    {
                        bool active = IsRobinCooldownActive(out float remSec);
                        int dispSec = active ? Mathf.CeilToInt(remSec) : 0;
                        if (dispSec != lastDisplayedCooldownSec)
                        {
                            lastDisplayedCooldownSec = dispSec;
                            string liveLine = dispSec > 0
                                ? $"I'm sorting my field notes. Come back in {dispSec / 60}:{dispSec % 60:00}!"
                                : "My field notes are all sorted! Talk to me anytime for a new Field Checklist.";
                            DialoguePresenter.Instance.UpdateLiveCurrentLineText(liveLine);
                        }
                    }
                    yield return null;
                }
            }
            finally
            {
                IsRobinCooldownDialogueOpen = false;
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
                CursorState.SyncLookAction(inputs);
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

            if (allSpawnPoints.Count == 0) CacheSpawnPoints();
            if (pickupPool.Count < 10) CachePickupPool();

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
            bool generated = GenerateChecklistAndSpawns(currentAttemptSeed, database, allSpawnPoints, activeChecklist);
            if (!generated || activeChecklist.Count == 0)
            {
                Debug.LogError("[CityTrashDetectiveManager] Failed to generate checklist or spawn points; aborting attempt cleanly.");
                questState = CityDetectiveState.NotStarted;
                ClearSavedMidQuestProgress(saveToDisk: true);
                return;
            }

            // Spawn 10 pooled trash items
            int spawnedCount = 0;
            for (int i = 0; i < activeChecklist.Count; i++)
            {
                var slot = activeChecklist[i];
                if (i < pickupPool.Count)
                {
                    slot.pickupInstance = pickupPool[i];
                    slot.pickupInstance.ActivateAtSpawn(i, slot.item, slot.spawnPoint, currentAttemptSeed + i * 31);
                    if (slot.pickupInstance.gameObject.activeSelf)
                    {
                        spawnedCount++;
                    }
                }
            }

            if (spawnedCount == 0)
            {
                Debug.LogError("[CityTrashDetectiveManager] Zero trash pickups became active; aborting attempt cleanly.");
                activeChecklist.Clear();
                questState = CityDetectiveState.NotStarted;
                ClearSavedMidQuestProgress(saveToDisk: true);
                return;
            }

            questState = CityDetectiveState.Active;
            SaveMidQuestProgress(saveToDisk: true);
            UpdateRobinMarkerVisibility();
            OnDetectiveQuestStateChanged?.Invoke();

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
            ClearSavedMidQuestProgress(saveToDisk: true);

            ui?.HideChecklist();
            ui?.UpdateCompassHud(false, Vector3.zero, Vector3.forward, Vector3.zero, "", CityDistrict.Park, 0f);
            UpdateRobinMarkerVisibility();
            OnDetectiveQuestStateChanged?.Invoke();

            if (UIOrchestrator.Instance != null)
            {
                UIOrchestrator.Instance.SetPhase(UIOrchestrator.GamePhase.FreePlay, "CityScavenger Cancelled");
            }
        }

        // =====================================================================
        // MID-QUEST PERSISTENCE
        // =====================================================================

        public void FlushMidQuestSaveNow()
        {
            if (questState == CityDetectiveState.Active || questState == CityDetectiveState.ReadyToTurnIn)
            {
                SaveMidQuestProgress(saveToDisk: true);
            }
        }

        public void RefreshRestoredQuestHudAndMarkers()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene(gameObject.scene)) return;

            if (questState == CityDetectiveState.Active || questState == CityDetectiveState.ReadyToTurnIn)
            {
                if (UIOrchestrator.Instance != null)
                {
                    UIOrchestrator.Instance.SetQuestPanelUserHidden(false, "CityScavenger Restored");
                    UIOrchestrator.Instance.SetPhase(UIOrchestrator.GamePhase.QuestActive, "CityScavenger Restored");
                }

                if (ui != null && questState == CityDetectiveState.Active)
                {
                    ui.ShowChecklist(activeChecklist, collectedCount, remainingSeconds, remainingHintCharges, currentHintCooldown);
                }

                UpdateRobinMarkerVisibility();
                OnDetectiveQuestStateChanged?.Invoke();
                ObjectiveMarkerService.Instance?.RefreshTargetFromState();
            }
        }

        private void SaveMidQuestProgress(bool saveToDisk)
        {
            if ((questState != CityDetectiveState.Active && questState != CityDetectiveState.ReadyToTurnIn) ||
                activeChecklist.Count == 0)
            {
                return;
            }

            var payload = new SavedRobinState
            {
                attemptSeed = currentAttemptSeed,
                slots = new List<SavedRobinSlot>(activeChecklist.Count)
            };

            for (int i = 0; i < activeChecklist.Count; i++)
            {
                var slot = activeChecklist[i];
                if (slot == null || slot.item == null) continue;
                Vector3 pos = slot.spawnPoint != null ? slot.spawnPoint.transform.position : Vector3.zero;
                payload.slots.Add(new SavedRobinSlot
                {
                    itemId = slot.item.id,
                    district = (int)slot.district,
                    spawnName = slot.spawnPoint != null ? slot.spawnPoint.gameObject.name : string.Empty,
                    x = pos.x,
                    y = pos.y,
                    z = pos.z,
                    collected = slot.collected
                });
            }

            if (payload.slots.Count == 0) return;

            PlayerPrefs.SetInt(EcoQuestSaveKeys.CityRobinHasSavedProgress, 1);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.CityRobinQuestState, (int)questState);
            PlayerPrefs.SetFloat(EcoQuestSaveKeys.CityRobinRemainingSeconds, Mathf.Max(0f, remainingSeconds));
            PlayerPrefs.SetInt(EcoQuestSaveKeys.CityRobinHintCharges, remainingHintCharges);
            PlayerPrefs.SetFloat(EcoQuestSaveKeys.CityRobinHintCooldown, Mathf.Max(0f, currentHintCooldown));
            PlayerPrefs.SetInt(EcoQuestSaveKeys.CityRobinPickupCoins, currentRunPickupCoins);
            PlayerPrefs.SetString(EcoQuestSaveKeys.CityRobinSlotData, JsonUtility.ToJson(payload));
            lastMidQuestSaveUnscaledTime = Time.unscaledTime;

            if (saveToDisk)
            {
                PlayerPrefs.Save();
            }
        }

        private void ClearSavedMidQuestProgress(bool saveToDisk)
        {
            PlayerPrefs.DeleteKey(EcoQuestSaveKeys.CityRobinHasSavedProgress);
            PlayerPrefs.DeleteKey(EcoQuestSaveKeys.CityRobinQuestState);
            PlayerPrefs.DeleteKey(EcoQuestSaveKeys.CityRobinRemainingSeconds);
            PlayerPrefs.DeleteKey(EcoQuestSaveKeys.CityRobinHintCharges);
            PlayerPrefs.DeleteKey(EcoQuestSaveKeys.CityRobinHintCooldown);
            PlayerPrefs.DeleteKey(EcoQuestSaveKeys.CityRobinPickupCoins);
            PlayerPrefs.DeleteKey(EcoQuestSaveKeys.CityRobinSlotData);
            if (saveToDisk)
            {
                PlayerPrefs.Save();
            }
        }

        private bool RestoreMidQuestProgressIfSaved()
        {
            if (PlayerPrefs.GetInt(EcoQuestSaveKeys.CityRobinHasSavedProgress, 0) != 1)
                return false;

            var savedState = (CityDetectiveState)PlayerPrefs.GetInt(
                EcoQuestSaveKeys.CityRobinQuestState,
                (int)CityDetectiveState.NotStarted);
            if (savedState != CityDetectiveState.Active && savedState != CityDetectiveState.ReadyToTurnIn)
            {
                ClearSavedMidQuestProgress(saveToDisk: true);
                return false;
            }

            string json = PlayerPrefs.GetString(EcoQuestSaveKeys.CityRobinSlotData, string.Empty);
            if (string.IsNullOrEmpty(json) || database == null || database.items == null)
            {
                ClearSavedMidQuestProgress(saveToDisk: true);
                return false;
            }

            SavedRobinState parsed;
            try
            {
                parsed = JsonUtility.FromJson<SavedRobinState>(json);
            }
            catch
            {
                ClearSavedMidQuestProgress(saveToDisk: true);
                return false;
            }

            if (parsed.slots == null || parsed.slots.Count == 0)
            {
                ClearSavedMidQuestProgress(saveToDisk: true);
                return false;
            }

            if (allSpawnPoints.Count == 0) CacheSpawnPoints();
            if (pickupPool.Count < parsed.slots.Count) CachePickupPool();

            for (int i = 0; i < pickupPool.Count; i++)
            {
                if (database != null) pickupPool[i].PrewarmVisuals(database);
                pickupPool[i].ReturnToPoolImmediate();
            }

            activeChecklist.Clear();
            lastCollectedItemsForQuiz.Clear();

            var usedSpawns = new HashSet<CityTrashSpawnPoint>();
            int restoredCollected = 0;
            for (int i = 0; i < parsed.slots.Count; i++)
            {
                SavedRobinSlot s = parsed.slots[i];
                CityTrashItemDef def = database.GetById(s.itemId);
                if (def == null)
                {
                    activeChecklist.Clear();
                    lastCollectedItemsForQuiz.Clear();
                    ClearSavedMidQuestProgress(saveToDisk: true);
                    return false;
                }

                CityDistrict dist = (CityDistrict)s.district;
                CityTrashSpawnPoint sp = FindMatchingSpawnPoint(s.spawnName, new Vector3(s.x, s.y, s.z), dist, usedSpawns);
                if (sp != null) usedSpawns.Add(sp);

                var slot = new ChecklistSlot
                {
                    item = def,
                    district = dist,
                    spawnPoint = sp,
                    pickupInstance = null,
                    collected = s.collected
                };
                activeChecklist.Add(slot);

                if (s.collected)
                {
                    restoredCollected++;
                    lastCollectedItemsForQuiz.Add(def);
                }
            }

            currentAttemptSeed = parsed.attemptSeed;
            collectedCount = restoredCollected;
            if (restoredCollected >= activeChecklist.Count)
            {
                savedState = CityDetectiveState.ReadyToTurnIn;
            }

            for (int i = 0; i < activeChecklist.Count; i++)
            {
                var slot = activeChecklist[i];
                if (!slot.collected && i < pickupPool.Count)
                {
                    slot.pickupInstance = pickupPool[i];
                    slot.pickupInstance.ActivateAtSpawn(i, slot.item, slot.spawnPoint, currentAttemptSeed + i * 31);
                }
            }

            questState = savedState;
            float savedRemSec = PlayerPrefs.GetFloat(EcoQuestSaveKeys.CityRobinRemainingSeconds, questDurationSeconds);
            remainingSeconds = savedState == CityDetectiveState.Active
                ? Mathf.Clamp(savedRemSec, 0.5f, questDurationSeconds)
                : Mathf.Clamp(savedRemSec, 0f, questDurationSeconds);
            remainingHintCharges = Mathf.Clamp(PlayerPrefs.GetInt(EcoQuestSaveKeys.CityRobinHintCharges, maxHintCharges), 0, maxHintCharges);
            currentHintCooldown = Mathf.Max(0f, PlayerPrefs.GetFloat(EcoQuestSaveKeys.CityRobinHintCooldown, 0f));
            currentRunPickupCoins = Mathf.Max(0, PlayerPrefs.GetInt(EcoQuestSaveKeys.CityRobinPickupCoins, 0));
            isFinalizingQuest = false;
            hintActiveTimer = 0f;
            activeHintTarget = null;
            lastMidQuestSaveUnscaledTime = Time.unscaledTime;

            Debug.Log($"[CityTrashDetectiveManager] Restored mid-quest progress: state={questState}, collected={collectedCount}/{activeChecklist.Count}, time={remainingSeconds:F1}s");
            return true;
        }

        private CityTrashSpawnPoint FindMatchingSpawnPoint(
            string spawnName,
            Vector3 savedPos,
            CityDistrict fallbackDistrict,
            HashSet<CityTrashSpawnPoint> usedSpawns)
        {
            for (int i = 0; i < allSpawnPoints.Count; i++)
            {
                var sp = allSpawnPoints[i];
                if (sp == null || usedSpawns.Contains(sp)) continue;
                if (sp.gameObject.name == spawnName && (sp.transform.position - savedPos).sqrMagnitude < 0.05f)
                    return sp;
            }

            CityTrashSpawnPoint closest = null;
            float closestDistSq = 0.25f;
            for (int i = 0; i < allSpawnPoints.Count; i++)
            {
                var sp = allSpawnPoints[i];
                if (sp == null || usedSpawns.Contains(sp)) continue;
                float dSq = (sp.transform.position - savedPos).sqrMagnitude;
                if (dSq < closestDistSq)
                {
                    closestDistSq = dSq;
                    closest = sp;
                }
            }
            if (closest != null) return closest;

            if (!string.IsNullOrEmpty(spawnName))
            {
                for (int i = 0; i < allSpawnPoints.Count; i++)
                {
                    var sp = allSpawnPoints[i];
                    if (sp != null && !usedSpawns.Contains(sp) && sp.gameObject.name == spawnName)
                        return sp;
                }
            }

            for (int i = 0; i < allSpawnPoints.Count; i++)
            {
                var sp = allSpawnPoints[i];
                if (sp != null && !usedSpawns.Contains(sp) && sp.district == fallbackDistrict)
                    return sp;
            }

            for (int i = 0; i < allSpawnPoints.Count; i++)
            {
                var sp = allSpawnPoints[i];
                if (sp != null && !usedSpawns.Contains(sp))
                    return sp;
            }

            return null;
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

                if (chosenSp == null && spawnPoints != null)
                {
                    for (int s = 0; s < spawnPoints.Count; s++)
                    {
                        var candidate = spawnPoints[s];
                        if (candidate != null && candidate.isVerifiedValid && !usedSpawns.Contains(candidate))
                        {
                            chosenSp = candidate;
                            usedSpawns.Add(chosenSp);
                            d = candidate.district;
                            break;
                        }
                    }

                    if (chosenSp == null)
                    {
                        for (int s = 0; s < spawnPoints.Count; s++)
                        {
                            var candidate = spawnPoints[s];
                            if (candidate != null && !usedSpawns.Contains(candidate))
                            {
                                chosenSp = candidate;
                                usedSpawns.Add(chosenSp);
                                d = candidate.district;
                                break;
                            }
                        }
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
                AudioManager.Instance?.PlayCoinEarn();
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
                SaveMidQuestProgress(saveToDisk: true);
                UpdateRobinMarkerVisibility();
                OnDetectiveQuestStateChanged?.Invoke();
                rangerRobinController?.PlayReaction("Cheer");
                if (!isFinalizingQuest)
                {
                    StartCoroutine(CompleteQuestAllItemsFoundRoutine());
                }
            }
            else
            {
                SaveMidQuestProgress(saveToDisk: true);
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
            SaveMidQuestProgress(saveToDisk: true);

            if (UITheme.Instance != null && UITheme.Instance.modalOpenSound != null)
            {
                UITheme.PlaySound(UITheme.Instance.modalOpenSound, 0.7f);
            }
            else
            {
                AudioManager.Instance?.PlayNotification();
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

            questState = (stars >= 1) ? CityDetectiveState.Completed : CityDetectiveState.NotStarted;
            ClearSavedMidQuestProgress(saveToDisk: true);
            // Start 5:00 cooldown on BOTH completion and timeout/0-star attempt (persisted as unpaused remaining seconds)
            StartRobinCooldown();
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
                        if (IsRobinCooldownActive(out _)) return;
                        ui.HideResultsModal();
                        modalCloseCooldownTimer = 0.25f;
                        questState = CityDetectiveState.NotStarted;
                        UpdateRobinMarkerVisibility();
                        OnDetectiveQuestStateChanged?.Invoke();
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
