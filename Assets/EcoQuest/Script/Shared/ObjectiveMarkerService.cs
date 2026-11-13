using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Unified single source of truth for all objective markers and guidance in EcoQuest.
/// Manages exactly ONE active target at a time across 3D world arrows,
/// screen-edge off-screen indicators, and minimap icons.
/// Event-driven, cache-backed, with zero per-frame Find calls.
/// </summary>
[DisallowMultipleComponent]
public class ObjectiveMarkerService : MonoBehaviour
{
    private static ObjectiveMarkerService instance;
    public static ObjectiveMarkerService Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<ObjectiveMarkerService>(FindObjectsInactive.Include);
                if (instance == null && Application.isPlaying)
                {
                    GameObject go = new GameObject("[ObjectiveMarkerService]");
                    instance = go.AddComponent<ObjectiveMarkerService>();
                }
            }
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [Header("Current Target & Reason")]
    [SerializeField] private Transform currentTarget;
    [SerializeField] private string currentLabel = "";
    [SerializeField] private string currentReason = "";
    public Transform CurrentTarget => currentTarget;
    public string CurrentLabel => currentLabel;
    public string CurrentReason => currentReason;

    [Header("Guidance & Fading Settings")]
    [Tooltip("Hide arrow when player is within this distance (meters) to let interact prompt take over")]
    [SerializeField] private float hideDistanceThreshold = 2.5f;
    [Tooltip("Speed of smooth proximity alpha and scale fade")]
    [SerializeField] private float fadeSpeed = 4.5f;
    [Tooltip("Seconds of inactivity before subtle reminder pulse activates on pending objective")]
    [SerializeField] private float reminderDelay = 45f;

    public float HideDistanceThreshold { get => hideDistanceThreshold; set => hideDistanceThreshold = value; }
    public float FadeSpeed { get => fadeSpeed; set => fadeSpeed = value; }
    public float ReminderDelay { get => reminderDelay; set => reminderDelay = value; }

    [Header("3D World Arrow Settings")]
    public Vector3 overheadOffset = new Vector3(0f, 2.5f, 0f);
    public Color themeGold = new Color(1.0f, 0.82f, 0.15f);
    public Color themeGreenEmission = new Color(0.25f, 0.85f, 0.35f);
    public float baseScale = 0.6f;
    public float bounceSpeed = 3.5f;
    public float bounceHeight = 0.22f;
    public float rotationSpeed = 45f;

    [Header("Screen Edge Indicator Settings")]
    public float edgePadding = 48f;

    // Visual instances
    private GameObject worldArrowObj;
    private MeshFilter arrowFilter;
    private Mesh arrowMesh;
    private MeshRenderer arrowRenderer;
    private Material arrowMaterial;
    private float currentArrowScaleFactor = 0f;
    private float currentAlpha = 0f;
    private bool hadActiveTargetLastFrame = false;
    private bool eventsSubscribed = false;

    private Canvas edgeCanvas;
    private GameObject edgeIndicatorRoot;
    private RectTransform edgeIndicatorRect;
    private RectTransform edgePointerRect;
    private TextMeshProUGUI edgeDistanceText;

    // Cached References (NO Find in Update)
    private Camera mainCam;
    private Transform playerTransform;
    private Vector3 lastPlayerPosition;
    private float idleTimer = 0f;

    private Transform cachedGuardian;
    private NPCInteraction cachedGuardianNpc;
    private MinimapIcon cachedGuardianMinimap;
    private Transform cachedGuardianOverheadMarker;

    private Transform cachedExplorer;
    private ExplorerGateController cachedExplorerGate;
    private MinimapIcon cachedExplorerMinimap;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            DestroySafe(gameObject);
            return;
        }
        instance = this;

        if (Application.isPlaying)
        {
            if (transform.parent != null)
            {
                transform.SetParent(null, true);
            }
            DontDestroyOnLoad(gameObject);
        }

        CleanLegacyObjects();
        CreateWorldArrow();
        CreateEdgeIndicatorUI();
        SubscribeEvents();
    }

    private void Start()
    {
        CacheSceneReferences();
        RefreshTargetFromState();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (worldArrowObj != null) worldArrowObj.SetActive(false);
        if (edgeIndicatorRoot != null) edgeIndicatorRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();

        DestroySafe(arrowMaterial);
        arrowMaterial = null;
        DestroySafe(arrowMesh);
        arrowMesh = null;

        if (worldArrowObj != null) DestroySafe(worldArrowObj);
        if (edgeCanvas != null) DestroySafe(edgeCanvas.gameObject);

        if (instance == this) instance = null;
    }

    private void SubscribeEvents()
    {
        if (eventsSubscribed) return;
        eventsSubscribed = true;

        SceneManager.sceneLoaded += HandleSceneLoaded;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
        TravelGate.OnGateUnlocked += HandleGateUnlocked;
        UIOrchestrator.OnFlowStateChanged += HandleFlowStateChanged;
        NPCInteraction.OnQuestAccepted += HandleQuestAccepted;
        SequentialWASDTutorial.OnStep6Reached += HandleTutorialStep6Reached;
    }

    private void UnsubscribeEvents()
    {
        if (!eventsSubscribed) return;
        eventsSubscribed = false;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        TravelGate.OnGateUnlocked -= HandleGateUnlocked;
        UIOrchestrator.OnFlowStateChanged -= HandleFlowStateChanged;
        NPCInteraction.OnQuestAccepted -= HandleQuestAccepted;
        SequentialWASDTutorial.OnStep6Reached -= HandleTutorialStep6Reached;
    }

    private static void DestroySafe(UnityEngine.Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    private void CleanLegacyObjects()
    {
        // Remove empty leftover TutorialObjectiveArrow or stale NPCWaypointArrow if present
        GameObject legacyArrow = GameObject.Find("TutorialObjectiveArrow");
        if (legacyArrow != null) DestroySafe(legacyArrow);

        GameObject legacyPointerArrow = GameObject.Find("NPCWaypointArrow");
        if (legacyPointerArrow != null) DestroySafe(legacyPointerArrow);
    }

    private void ClearSceneReferences()
    {
        mainCam = null;
        playerTransform = null;
        cachedGuardian = null;
        cachedGuardianNpc = null;
        cachedGuardianMinimap = null;
        cachedGuardianOverheadMarker = null;
        cachedExplorer = null;
        cachedExplorerGate = null;
        cachedExplorerMinimap = null;
    }

    public void CacheSceneReferences()
    {
        ClearSceneReferences();

        mainCam = Camera.main;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
        else
        {
            var fpc = FindFirstObjectByType<StarterAssets.FirstPersonController>();
            if (fpc != null) playerTransform = fpc.transform;
        }

        if (playerTransform != null)
        {
            lastPlayerPosition = playerTransform.position;
        }

        // Cache Guardian references
        GameObject guardianGo = GameObject.Find("GuardianNPC");
        if (guardianGo != null)
        {
            cachedGuardian = guardianGo.transform;
            cachedGuardianNpc = guardianGo.GetComponent<NPCInteraction>();
            cachedGuardianMinimap = guardianGo.GetComponent<MinimapIcon>();
            cachedGuardianOverheadMarker = guardianGo.transform.Find("GuardianOverheadMarker");
        }

        // Cache Explorer references
        GameObject explorerGo = GameObject.Find("Explorer_Var1");
        if (explorerGo == null) explorerGo = GameObject.Find("Explorer");
        if (explorerGo != null)
        {
            cachedExplorer = explorerGo.transform;
            cachedExplorerGate = explorerGo.GetComponent<ExplorerGateController>();
            cachedExplorerMinimap = explorerGo.GetComponent<MinimapIcon>();
        }
    }

    // -------------------------------------------------------------------------
    // Event Handlers
    // -------------------------------------------------------------------------

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CacheSceneReferences();
        if (scene.name != "Forest Scene")
        {
            ClearTarget();
        }
        else
        {
            RefreshTargetFromState();
        }
    }

    private void HandleSceneUnloaded(Scene scene)
    {
        ClearTarget();
        ClearSceneReferences();
    }

    private void HandleGateUnlocked()
    {
        RefreshTargetFromState();
    }

    private void HandleFlowStateChanged(UIOrchestrator.FlowStep step, UIOrchestrator.GamePhase phase)
    {
        RefreshTargetFromState();
    }

    private void HandleQuestAccepted()
    {
        RefreshTargetFromState();
    }

    private void HandleTutorialStep6Reached()
    {
        RefreshTargetFromState();
    }

    // -------------------------------------------------------------------------
    // Single Source of Truth Target Gating
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes the active objective target strictly from persistent and gameplay state.
    /// Never depends on temporary dialogue or cinematic overlay state.
    /// </summary>
    public void RefreshTargetFromState()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName != "Forest Scene")
        {
            currentReason = $"Scene is '{sceneName}' (Not Forest) -> No markers";
            ClearTarget();
            return;
        }

        // Ensure cached references are populated
        if (cachedGuardian == null || cachedExplorer == null)
        {
            CacheSceneReferences();
        }

        // ---------------------------------------------------------------------
        // Decision Rule 1: Quest Completed, Post-Quiz Done, Reward Claimed
        // Gating: TravelGate.CanTravelToCity == true -> Explorer ONLY
        // ---------------------------------------------------------------------
        if (TravelGate.CanTravelToCity)
        {
            if (cachedExplorer != null)
            {
                currentReason = "All Forest objectives completed (CanTravelToCity=True) -> Guide to Explorer";
                SetTarget(cachedExplorer, "Talk to Explorer");
                return;
            }
        }

        // ---------------------------------------------------------------------
        // Decision Rule 2: Quest ACCEPTED / QuestActive
        // The quest tab is the guide. NO NPC arrow, NO Guardian minimap icon.
        // ---------------------------------------------------------------------
        bool isQuestAccepted = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestQuestAccepted, 0) == 1
            || (UIOrchestrator.Instance != null && UIOrchestrator.Instance.CurrentPhase == UIOrchestrator.GamePhase.QuestActive)
            || (UIOrchestrator.Instance != null && (UIOrchestrator.Instance.CurrentFlowStep == UIOrchestrator.FlowStep.TabTutorial || UIOrchestrator.Instance.CurrentFlowStep == UIOrchestrator.FlowStep.FreePlay))
            || (cachedGuardianNpc != null && cachedGuardianNpc.IsQuestActive)
            || TrashItem.AreTrashItemsAllowed;

        if (isQuestAccepted)
        {
            currentReason = "Quest is active (sorting trash) -> Quest Tab is guide; No NPC arrow";
            ClearTarget();
            return;
        }

        // ---------------------------------------------------------------------
        // Decision Rule 3: Tutorial Step 6 ("Follow the arrow and talk to the NPC")
        // Hide arrow during Steps 1-5 until Step 6 unlocks Guardian talk, unless
        // tutorial/tab progress is already saved.
        // ---------------------------------------------------------------------
        bool tutorialBlockingGuardian =
            SequentialWASDTutorial.Instance != null
            && !SequentialWASDTutorial.IsTutorialCompleted
            && PlayerPrefs.GetInt(EcoQuestSaveKeys.TabTipDone, 0) == 0
            && PlayerPrefs.GetInt(EcoQuestSaveKeys.SequentialWASD, 0) == 0;

        if (tutorialBlockingGuardian)
        {
            currentReason = "Tutorial Steps 1-5 in progress -> Hide Guardian arrow until Step 6";
            ClearTarget();
            return;
        }

        if (!TravelGate.CanTravelToCity && !isQuestAccepted)
        {
            if (cachedGuardian != null)
            {
                currentReason = "Step 6 / Talk to Guardian objective -> Guide to Guardian";
                SetTarget(cachedGuardian, "Talk to Guardian");
                return;
            }
        }

        // Fallback: No pending objective
        currentReason = "No pending objective";
        ClearTarget();
    }

    /// <summary>
    /// Explicitly sets the active objective target, automatically updating minimap and clearing old markers.
    /// </summary>
    public void SetTarget(Transform target, string label = "")
    {
        if (target == null)
        {
            ClearTarget();
            return;
        }

        currentTarget = target;
        currentLabel = label;
        hadActiveTargetLastFrame = true;
        idleTimer = 0f;

        SyncMinimapAndOverheadMarkers();
    }

    /// <summary>
    /// Clears the active target and immediately hides all guidance visuals.
    /// </summary>
    public void ClearTarget()
    {
        currentTarget = null;
        currentLabel = "";
        hadActiveTargetLastFrame = false;
        idleTimer = 0f;
        currentAlpha = 0f;
        currentArrowScaleFactor = 0f;

        if (worldArrowObj != null) worldArrowObj.SetActive(false);
        if (edgeIndicatorRoot != null) edgeIndicatorRoot.SetActive(false);

        SyncMinimapAndOverheadMarkers();
    }

    private void SyncMinimapAndOverheadMarkers()
    {
        // 1. Guardian Minimap Icon and Overhead Marker
        if (cachedGuardianMinimap != null)
        {
            bool isGuardianTarget = (currentTarget != null && currentTarget == cachedGuardian);
            cachedGuardianMinimap.enabled = isGuardianTarget;
        }

        if (cachedGuardianOverheadMarker != null)
        {
            bool isGuardianTarget = (currentTarget != null && currentTarget == cachedGuardian);
            cachedGuardianOverheadMarker.gameObject.SetActive(isGuardianTarget);
        }

        // 2. Explorer Minimap Icon
        if (cachedExplorerMinimap != null)
        {
            bool isExplorerTarget = (currentTarget != null && currentTarget == cachedExplorer);
            cachedExplorerMinimap.enabled = isExplorerTarget;
        }
    }

    // -------------------------------------------------------------------------
    // Per-frame Animation & Positioning
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy)
        {
            if (hadActiveTargetLastFrame)
            {
                ClearTarget();
            }
            else
            {
                if (worldArrowObj != null && worldArrowObj.activeSelf) worldArrowObj.SetActive(false);
                if (edgeIndicatorRoot != null && edgeIndicatorRoot.activeSelf) edgeIndicatorRoot.SetActive(false);
            }
            return;
        }

        hadActiveTargetLastFrame = true;

        // Check dialogue and overlay suppression
        bool isSuppressed = IsOverlayOrDialogueActive();
        if (isSuppressed)
        {
            currentAlpha = Mathf.MoveTowards(currentAlpha, 0f, Time.deltaTime * (fadeSpeed * 2f));
            currentArrowScaleFactor = Mathf.MoveTowards(currentArrowScaleFactor, 0f, Time.deltaTime * (fadeSpeed * 2f));
            if (worldArrowObj != null && worldArrowObj.activeSelf) worldArrowObj.SetActive(false);
            if (edgeIndicatorRoot != null && edgeIndicatorRoot.activeSelf) edgeIndicatorRoot.SetActive(false);
            return;
        }

        Vector3 targetPos = currentTarget.position;
        Vector3 playerPos = playerTransform != null ? playerTransform.position : (mainCam != null ? mainCam.transform.position : targetPos);
        float distance = Vector3.Distance(playerPos, targetPos);

        // Track player movement for idle reminder
        if (playerTransform != null)
        {
            if (Vector3.Distance(playerPos, lastPlayerPosition) > 0.1f)
            {
                lastPlayerPosition = playerPos;
                idleTimer = 0f;
            }
            else
            {
                idleTimer += Time.deltaTime;
            }
        }

        // Smooth proximity fade & hide within interaction range (2.5m)
        bool isClose = distance <= hideDistanceThreshold;
        float targetAlpha = isClose ? 0f : 1f;
        currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);
        currentArrowScaleFactor = Mathf.MoveTowards(currentArrowScaleFactor, targetAlpha, Time.deltaTime * fadeSpeed);

        if (currentAlpha <= 0.01f)
        {
            if (worldArrowObj != null && worldArrowObj.activeSelf) worldArrowObj.SetActive(false);
            if (edgeIndicatorRoot != null && edgeIndicatorRoot.activeSelf) edgeIndicatorRoot.SetActive(false);
            return;
        }

        // Determine if target is visible on screen
        Vector3 viewportPoint = Vector3.zero;
        bool isOnScreen = false;
        if (mainCam != null)
        {
            viewportPoint = mainCam.WorldToViewportPoint(targetPos + overheadOffset);
            bool inFront = viewportPoint.z > 0f;
            isOnScreen = inFront && (viewportPoint.x >= 0.06f && viewportPoint.x <= 0.94f)
                                 && (viewportPoint.y >= 0.06f && viewportPoint.y <= 0.94f);
        }

        // Idle reminder subtle pulse if player has not made progress for reminderDelay
        float idlePulse = 1f;
        if (idleTimer >= reminderDelay)
        {
            idlePulse = 1f + Mathf.Sin(Time.time * 4f) * 0.12f;
        }

        // Animate 3D World Arrow
        if (worldArrowObj != null)
        {
            worldArrowObj.SetActive(true);

            // Distance-aware subtle scaling (0.5 to 1.15)
            float distScale = Mathf.Clamp(0.55f + (distance - hideDistanceThreshold) * 0.015f, 0.5f, 1.15f);
            worldArrowObj.transform.localScale = Vector3.one * (baseScale * distScale * currentArrowScaleFactor * idlePulse);

            float bounce = Mathf.Sin(Time.time * bounceSpeed) * bounceHeight;
            worldArrowObj.transform.position = targetPos + overheadOffset + new Vector3(0f, bounce, 0f);
            worldArrowObj.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

            // Update material transparency if faded
            if (arrowMaterial != null && arrowMaterial.HasProperty("_BaseColor"))
            {
                Color c = themeGold;
                c.a = currentAlpha;
                arrowMaterial.SetColor("_BaseColor", c);
            }
        }

        // Update Off-Screen Screen-Edge Indicator
        if (edgeIndicatorRoot != null)
        {
            if (!isOnScreen && mainCam != null && currentAlpha > 0.1f)
            {
                edgeIndicatorRoot.SetActive(true);
                UpdateEdgeIndicatorPosition(viewportPoint, distance);
            }
            else
            {
                edgeIndicatorRoot.SetActive(false);
            }
        }
    }

    private bool IsOverlayOrDialogueActive()
    {
        if (TravelGate.IsTraveling) return true;

        if (UIOrchestrator.Instance != null && UIOrchestrator.Instance.IsOverlayActive)
            return true;

        if (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue)
            return true;

        if (currentTarget != null)
        {
            if (cachedGuardian != null && currentTarget == cachedGuardian && cachedGuardianNpc != null && cachedGuardianNpc.IsTalking)
                return true;

            if (cachedExplorer != null && currentTarget == cachedExplorer && cachedExplorerGate != null && cachedExplorerGate.IsInDialogue)
                return true;
        }

        return false;
    }

    private void UpdateEdgeIndicatorPosition(Vector3 vp, float distance)
    {
        if (edgeIndicatorRect == null) return;

        Vector2 screenDir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
        if (vp.z < 0f)
        {
            screenDir = -screenDir;
        }

        if (screenDir.sqrMagnitude < 0.001f) screenDir = Vector2.up;
        screenDir.Normalize();

        float screenW = Screen.width;
        float screenH = Screen.height;
        float halfW = (screenW * 0.5f) - edgePadding;
        float halfH = (screenH * 0.5f) - edgePadding;

        float angle = Mathf.Atan2(screenDir.y, screenDir.x);
        float edgeX = Mathf.Clamp(Mathf.Cos(angle) * halfW, -halfW, halfW);
        float edgeY = Mathf.Clamp(Mathf.Sin(angle) * halfH, -halfH, halfH);

        edgeIndicatorRect.anchoredPosition = new Vector2(edgeX, edgeY);

        if (edgePointerRect != null)
        {
            float deg = angle * Mathf.Rad2Deg - 90f;
            edgePointerRect.localRotation = Quaternion.Euler(0f, 0f, deg);
        }

        if (edgeDistanceText != null)
        {
            edgeDistanceText.text = $"{Mathf.RoundToInt(distance)}m";
        }
    }

    // -------------------------------------------------------------------------
    // Visual Construction (3D Arrow & UI Indicator)
    // -------------------------------------------------------------------------

    private void CreateWorldArrow()
    {
        worldArrowObj = new GameObject("Unified_Objective_Arrow");
        worldArrowObj.transform.SetParent(transform);

        MeshFilter filter = worldArrowObj.AddComponent<MeshFilter>();
        arrowFilter = filter;
        arrowRenderer = worldArrowObj.AddComponent<MeshRenderer>();
        arrowMesh = GenerateArrowMesh();
        filter.mesh = arrowMesh;

        Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpShader == null) urpShader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (urpShader == null) urpShader = Shader.Find("Sprites/Default");

        arrowMaterial = new Material(urpShader) { name = "ObjectiveArrow_Mat" };

        if (arrowMaterial.HasProperty("_BaseColor")) arrowMaterial.SetColor("_BaseColor", themeGold);
        else arrowMaterial.color = themeGold;

        if (arrowMaterial.HasProperty("_EmissionColor"))
        {
            arrowMaterial.EnableKeyword("_EMISSION");
            arrowMaterial.SetColor("_EmissionColor", themeGreenEmission * 0.5f);
        }

        arrowMaterial.renderQueue = 3200;
        arrowRenderer.material = arrowMaterial;
        worldArrowObj.SetActive(false);
    }

    private Mesh GenerateArrowMesh()
    {
        Mesh mesh = new Mesh { name = "UnifiedArrowMesh" };

        Vector3[] vertices = new Vector3[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(-0.35f, 0.55f, -0.35f),
            new Vector3( 0.35f, 0.55f, -0.35f),
            new Vector3( 0.35f, 0.55f,  0.35f),
            new Vector3(-0.35f, 0.55f,  0.35f),

            new Vector3(-0.14f, 0.55f, -0.14f),
            new Vector3( 0.14f, 0.55f, -0.14f),
            new Vector3( 0.14f, 0.55f,  0.14f),
            new Vector3(-0.14f, 0.55f,  0.14f),

            new Vector3(-0.14f, 1.15f, -0.14f),
            new Vector3( 0.14f, 1.15f, -0.14f),
            new Vector3( 0.14f, 1.15f,  0.14f),
            new Vector3(-0.14f, 1.15f,  0.14f)
        };

        int[] triangles = new int[]
        {
            0, 2, 1,   0, 3, 2,   0, 4, 3,   0, 1, 4,
            1, 2, 3,   1, 3, 4,
            5, 9, 6,   6, 9, 10,
            6, 10, 7,  7, 10, 11,
            7, 11, 8,  8, 11, 12,
            8, 12, 5,  5, 12, 9,
            9, 11, 10, 9, 12, 11
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void CreateEdgeIndicatorUI()
    {
        GameObject canvasGo = new GameObject("ObjectiveEdgeCanvas");
        canvasGo.transform.SetParent(transform);

        edgeCanvas = canvasGo.AddComponent<Canvas>();
        edgeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        edgeCanvas.sortingOrder = 130;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        edgeIndicatorRoot = new GameObject("EdgeIndicatorRoot");
        edgeIndicatorRoot.transform.SetParent(canvasGo.transform, false);

        edgeIndicatorRect = edgeIndicatorRoot.AddComponent<RectTransform>();
        edgeIndicatorRect.sizeDelta = new Vector2(80f, 80f);

        GameObject pointerGo = new GameObject("PointerIcon");
        pointerGo.transform.SetParent(edgeIndicatorRoot.transform, false);
        edgePointerRect = pointerGo.AddComponent<RectTransform>();
        edgePointerRect.sizeDelta = new Vector2(32f, 32f);

        Image pointerImg = pointerGo.AddComponent<Image>();
        pointerImg.color = themeGold;

        GameObject textGo = new GameObject("DistanceText");
        textGo.transform.SetParent(edgeIndicatorRoot.transform, false);
        RectTransform textRect = textGo.AddComponent<RectTransform>();
        textRect.anchoredPosition = new Vector2(0f, -24f);
        textRect.sizeDelta = new Vector2(100f, 24f);

        edgeDistanceText = textGo.AddComponent<TextMeshProUGUI>();
        edgeDistanceText.alignment = TextAlignmentOptions.Center;
        edgeDistanceText.fontSize = 16f;
        edgeDistanceText.fontStyle = FontStyles.Bold;
        edgeDistanceText.color = Color.white;

        edgeIndicatorRoot.SetActive(false);
    }
}
