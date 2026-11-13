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
    public float edgePadding = 64f;
    [SerializeField] private Sprite edgeArrowSprite;

    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;

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
    private bool lastIsArrowOccluded = false;
    private bool lastIsOnScreen = false;
    private int lastDisplayedMeters = -1;

    private Canvas edgeCanvas;
    private RectTransform edgeCanvasRect;
    private GameObject edgeIndicatorRoot;
    private RectTransform edgeIndicatorRect;
    private CanvasGroup edgeCanvasGroup;
    private RectTransform edgePointerRect;
    private RectTransform distanceBadgeRect;
    private TextMeshProUGUI edgeDistanceText;
    private Sprite runtimeFallbackArrowSprite;
    private Texture2D runtimeFallbackArrowTex;
    private Sprite runtimePillSprite;
    private Texture2D runtimePillTex;

    private readonly RaycastHit[] occlusionHits = new RaycastHit[16];

    public bool IsWorldArrowActive => worldArrowObj != null && worldArrowObj.activeSelf;
    public bool IsScreenIndicatorActive => edgeIndicatorRoot != null && edgeIndicatorRoot.activeSelf;
    public bool IsScreenArrowActive => edgePointerRect != null && edgePointerRect.gameObject.activeSelf && IsScreenIndicatorActive;
    public bool IsDistanceTextActive => edgeDistanceText != null && edgeDistanceText.gameObject.activeInHierarchy && IsScreenIndicatorActive;
    public string CurrentDistanceText => edgeDistanceText != null ? edgeDistanceText.text : string.Empty;
    public Vector2 CurrentIndicatorCanvasPosition => edgeIndicatorRect != null ? edgeIndicatorRect.anchoredPosition : Vector2.zero;
    public bool LastIsArrowOccluded => lastIsArrowOccluded;
    public bool LastIsOnScreen => lastIsOnScreen;
    public bool IsSuppressed => IsOverlayOrDialogueActive();

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
        DestroySafe(runtimeFallbackArrowSprite);
        runtimeFallbackArrowSprite = null;
        DestroySafe(runtimeFallbackArrowTex);
        runtimeFallbackArrowTex = null;
        DestroySafe(runtimePillSprite);
        runtimePillSprite = null;
        DestroySafe(runtimePillTex);
        runtimePillTex = null;

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
        lastDisplayedMeters = -1;

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
        lastIsArrowOccluded = false;
        lastIsOnScreen = false;
        lastDisplayedMeters = -1;

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
            bool isGuardianTarget = (currentTarget != null && currentTarget == cachedGuardian) && !IsOverlayOrDialogueActive();
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

    private void LateUpdate()
    {
        TickMarkerVisuals(Time.unscaledDeltaTime);
    }

    public void TickMarkerVisuals(float deltaTime)
    {
        float dt = Mathf.Max(0f, deltaTime);

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

        // Re-resolve active camera/player only when missing or disabled (no per-frame Find when valid)
        if (mainCam == null || !mainCam.isActiveAndEnabled)
        {
            mainCam = Camera.main;
        }
        if (playerTransform == null)
        {
            ResolvePlayerTransform();
        }

        // Check dialogue and overlay suppression
        bool isSuppressed = IsOverlayOrDialogueActive();
        if (isSuppressed)
        {
            currentAlpha = Mathf.MoveTowards(currentAlpha, 0f, dt * (fadeSpeed * 2f));
            currentArrowScaleFactor = Mathf.MoveTowards(currentArrowScaleFactor, 0f, dt * (fadeSpeed * 2f));
            if (worldArrowObj != null && worldArrowObj.activeSelf) worldArrowObj.SetActive(false);
            if (edgeIndicatorRoot != null && edgeIndicatorRoot.activeSelf) edgeIndicatorRoot.SetActive(false);
            if (cachedGuardianOverheadMarker != null && cachedGuardianOverheadMarker.gameObject.activeSelf)
            {
                cachedGuardianOverheadMarker.gameObject.SetActive(false);
            }
            return;
        }

        Vector3 targetPos = currentTarget.position;
        Vector3 anchorPos = targetPos + overheadOffset;
        Vector3 playerPos = playerTransform != null ? playerTransform.position : (mainCam != null ? mainCam.transform.position : targetPos);
        float distance = Vector3.Distance(playerPos, targetPos);
        if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f) distance = 0f;

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
        currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, dt * fadeSpeed);
        currentArrowScaleFactor = Mathf.MoveTowards(currentArrowScaleFactor, targetAlpha, dt * fadeSpeed);

        if (currentAlpha <= 0.01f)
        {
            if (worldArrowObj != null && worldArrowObj.activeSelf) worldArrowObj.SetActive(false);
            if (edgeIndicatorRoot != null && edgeIndicatorRoot.activeSelf) edgeIndicatorRoot.SetActive(false);
            return;
        }

        // Idle reminder subtle pulse if player has not made progress for reminderDelay
        float idlePulse = 1f;
        if (idleTimer >= reminderDelay)
        {
            idlePulse = 1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.12f;
        }

        Vector3 vp = Vector3.zero;
        bool inFront = false;
        bool isOnScreen = false;
        bool isOccluded = false;
        float dotRight = 0f;
        float dotUp = 0f;

        if (mainCam != null)
        {
            Transform camT = mainCam.transform;
            Vector3 camDelta = anchorPos - camT.position;
            float dotForward = Vector3.Dot(camDelta, camT.forward);
            dotRight = Vector3.Dot(camDelta, camT.right);
            dotUp = Vector3.Dot(camDelta, camT.up);

            vp = mainCam.WorldToViewportPoint(anchorPos);
            bool validVp = !float.IsNaN(vp.x) && !float.IsInfinity(vp.x) && !float.IsNaN(vp.y) && !float.IsInfinity(vp.y);
            inFront = dotForward > 0.05f && vp.z > 0.05f && validVp;
            isOnScreen = inFront && (vp.x >= 0.06f && vp.x <= 0.94f)
                                 && (vp.y >= 0.06f && vp.y <= 0.94f);
            isOccluded = IsAnchorOccludedByGeometry(camT.position, anchorPos);
        }

        lastIsOnScreen = isOnScreen;
        lastIsArrowOccluded = isOccluded;

        // Show 3D World Arrow only when active & not occluded behind geometry
        if (worldArrowObj != null)
        {
            bool showWorldArrow = mainCam != null ? !isOccluded : true;
            worldArrowObj.SetActive(showWorldArrow);

            if (showWorldArrow)
            {
                float distScale = Mathf.Clamp(0.55f + (distance - hideDistanceThreshold) * 0.015f, 0.5f, 1.15f);
                worldArrowObj.transform.localScale = Vector3.one * (baseScale * distScale * currentArrowScaleFactor * idlePulse);

                float bounce = Mathf.Sin(Time.unscaledTime * bounceSpeed) * bounceHeight;
                worldArrowObj.transform.position = anchorPos + new Vector3(0f, bounce, 0f);
                worldArrowObj.transform.Rotate(Vector3.up, rotationSpeed * dt, Space.World);
            }
        }

        // Update Screen-Space Overlay Marker + Always-Visible Distance Meter
        if (edgeIndicatorRoot != null)
        {
            if (mainCam != null)
            {
                edgeIndicatorRoot.SetActive(true);
                UpdateScreenIndicator(vp, dotRight, dotUp, inFront, isOnScreen, isOccluded, distance, idlePulse);
            }
            else
            {
                edgeIndicatorRoot.SetActive(false);
            }
        }
    }

    private void ResolvePlayerTransform()
    {
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
    }

    private bool IsAnchorOccludedByGeometry(Vector3 camPos, Vector3 anchorPos)
    {
        Vector3 toAnchor = anchorPos - camPos;
        float dist = toAnchor.magnitude;
        if (dist <= 0.35f) return false;

        int hitCount = Physics.RaycastNonAlloc(
            camPos,
            toAnchor / dist,
            occlusionHits,
            dist - 0.30f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = occlusionHits[i].collider;
            if (col == null) continue;
            Transform hitT = col.transform;
            if (playerTransform != null && hitT.IsChildOf(playerTransform)) continue;
            if (mainCam != null && hitT.IsChildOf(mainCam.transform)) continue;
            if (currentTarget != null && hitT.IsChildOf(currentTarget)) continue;
            return true;
        }

        return false;
    }

    private bool IsOverlayOrDialogueActive()
    {
        if (TravelGate.IsTraveling) return true;
        if (CursorState.IsAnyBlockingUIOpen()) return true;

        if (currentTarget != null)
        {
            if (cachedGuardian != null && currentTarget == cachedGuardian && cachedGuardianNpc != null && cachedGuardianNpc.IsTalking)
                return true;

            if (cachedExplorer != null && currentTarget == cachedExplorer && cachedExplorerGate != null && cachedExplorerGate.IsInDialogue)
                return true;
        }

        return false;
    }

    private void UpdateScreenIndicator(
        Vector3 vp,
        float dotRight,
        float dotUp,
        bool inFront,
        bool isOnScreen,
        bool isOccluded,
        float distance,
        float idlePulse)
    {
        if (edgeIndicatorRect == null) return;

        Vector2 canvasSize = edgeCanvasRect != null && edgeCanvasRect.rect.width > 1f && edgeCanvasRect.rect.height > 1f
            ? edgeCanvasRect.rect.size
            : new Vector2(ReferenceWidth, ReferenceHeight);

        float scaleX = canvasSize.x / ReferenceWidth;
        float scaleY = canvasSize.y / ReferenceHeight;
        float minScale = Mathf.Min(scaleX, scaleY);
        float padX = Mathf.Max(48f, edgePadding * minScale);
        float padY = Mathf.Max(52f, edgePadding * minScale);
        float halfW = Mathf.Max(80f, canvasSize.x * 0.5f - padX);
        float halfH = Mathf.Max(80f, canvasSize.y * 0.5f - padY);

        Vector2 finalPos;
        if (isOnScreen)
        {
            float targetX = (vp.x - 0.5f) * canvasSize.x;
            float targetY = (vp.y - 0.5f) * canvasSize.y;

            if (edgePointerRect != null)
            {
                edgePointerRect.gameObject.SetActive(isOccluded);
                if (isOccluded)
                {
                    // Point downward above occluded target and bob subtly
                    float bob = Mathf.Sin(Time.unscaledTime * bounceSpeed) * 4f;
                    edgePointerRect.anchoredPosition = new Vector2(0f, 12f + bob);
                    edgePointerRect.localRotation = Quaternion.Euler(0f, 0f, 180f);
                }
            }

            finalPos = new Vector2(targetX, isOccluded ? targetY : (targetY - 12f));
            finalPos.x = Mathf.Clamp(finalPos.x, -halfW, halfW);
            finalPos.y = Mathf.Clamp(finalPos.y, -halfH, halfH);
            AvoidCornerHudZones(ref finalPos, new Vector2(finalPos.x, finalPos.y).normalized, canvasSize, halfW, halfH, scaleX, scaleY);
        }
        else
        {
            Vector2 screenDir = inFront
                ? new Vector2((vp.x - 0.5f) * canvasSize.x, (vp.y - 0.5f) * canvasSize.y)
                : new Vector2(dotRight, dotUp);

            if (float.IsNaN(screenDir.x) || float.IsNaN(screenDir.y) || screenDir.sqrMagnitude < 0.0001f)
            {
                screenDir = Vector2.down;
            }
            screenDir.Normalize();

            float tX = Mathf.Abs(screenDir.x) > 0.0001f ? halfW / Mathf.Abs(screenDir.x) : float.MaxValue;
            float tY = Mathf.Abs(screenDir.y) > 0.0001f ? halfH / Mathf.Abs(screenDir.y) : float.MaxValue;
            finalPos = screenDir * Mathf.Min(tX, tY);
            AvoidCornerHudZones(ref finalPos, screenDir, canvasSize, halfW, halfH, scaleX, scaleY);

            if (edgePointerRect != null)
            {
                edgePointerRect.gameObject.SetActive(true);
                edgePointerRect.anchoredPosition = Vector2.zero;
                float deg = Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg - 90f;
                edgePointerRect.localRotation = Quaternion.Euler(0f, 0f, deg);
            }
        }

        edgeIndicatorRect.anchoredPosition = finalPos;
        edgeIndicatorRect.localScale = Vector3.one * idlePulse;

        if (edgeCanvasGroup != null)
        {
            edgeCanvasGroup.alpha = currentAlpha;
        }

        if (distanceBadgeRect != null)
        {
            bool badgeBelowArrow = !(finalPos.y < -halfH + 42f);
            float offsetY = isOnScreen && !isOccluded ? 0f : (badgeBelowArrow ? -32f : 32f);
            distanceBadgeRect.anchoredPosition = new Vector2(0f, offsetY);
        }

        int meters = Mathf.Max(0, Mathf.RoundToInt(distance));
        if (meters != lastDisplayedMeters && edgeDistanceText != null)
        {
            lastDisplayedMeters = meters;
            edgeDistanceText.text = $"{meters}m";
        }
    }

    private static void AvoidCornerHudZones(
        ref Vector2 pos,
        Vector2 dir,
        Vector2 canvasSize,
        float halfW,
        float halfH,
        float scaleX,
        float scaleY)
    {
        float rawHalfW = canvasSize.x * 0.5f;
        float rawHalfH = canvasSize.y * 0.5f;

        // Top-left (Hunger / Stamina bars + Quest panel header)
        float tlX = -rawHalfW + 390f * scaleX;
        float tlY = rawHalfH - 145f * scaleY;
        if (pos.x < tlX && pos.y > tlY)
        {
            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x)) pos.x = Mathf.Min(tlX, halfW);
            else pos.y = Mathf.Max(tlY, -halfH);
        }

        // Top-right (Minimap + Settings gear)
        float trX = rawHalfW - 350f * scaleX;
        float trY = rawHalfH - 235f * scaleY;
        if (pos.x > trX && pos.y > trY)
        {
            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x)) pos.x = Mathf.Max(trX, -halfW);
            else pos.y = Mathf.Max(trY, -halfH);
        }

        // Bottom-left (Coins counter)
        float blX = -rawHalfW + 270f * scaleX;
        float blY = -rawHalfH + 105f * scaleY;
        if (pos.x < blX && pos.y < blY)
        {
            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x)) pos.x = Mathf.Min(blX, halfW);
            else pos.y = Mathf.Min(blY, halfH);
        }

        // Bottom-right (Inventory bag button)
        float brX = rawHalfW - 155f * scaleX;
        float brY = -rawHalfH + 145f * scaleY;
        if (pos.x > brX && pos.y < brY)
        {
            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x)) pos.x = Mathf.Max(brX, -halfW);
            else pos.y = Mathf.Min(brY, halfH);
        }

        pos.x = Mathf.Clamp(pos.x, -halfW, halfW);
        pos.y = Mathf.Clamp(pos.y, -halfH, halfH);
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
        edgeCanvasRect = canvasGo.GetComponent<RectTransform>();

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        edgeIndicatorRoot = new GameObject("EdgeIndicatorRoot");
        edgeIndicatorRoot.transform.SetParent(canvasGo.transform, false);

        edgeIndicatorRect = edgeIndicatorRoot.AddComponent<RectTransform>();
        edgeIndicatorRect.sizeDelta = new Vector2(96f, 96f);
        edgeCanvasGroup = edgeIndicatorRoot.AddComponent<CanvasGroup>();
        edgeCanvasGroup.interactable = false;
        edgeCanvasGroup.blocksRaycasts = false;

        GameObject pointerGo = new GameObject("PointerIcon");
        pointerGo.transform.SetParent(edgeIndicatorRoot.transform, false);
        edgePointerRect = pointerGo.AddComponent<RectTransform>();
        edgePointerRect.sizeDelta = new Vector2(36f, 36f);

        Image pointerImg = pointerGo.AddComponent<Image>();
        pointerImg.raycastTarget = false;
        pointerImg.preserveAspect = true;
        bool usedProjectSprite;
        pointerImg.sprite = ResolveArrowSprite(out usedProjectSprite);
        pointerImg.color = usedProjectSprite ? Color.white : themeGold;

        GameObject badgeGo = new GameObject("DistanceBadge");
        badgeGo.transform.SetParent(edgeIndicatorRoot.transform, false);
        distanceBadgeRect = badgeGo.AddComponent<RectTransform>();
        distanceBadgeRect.anchoredPosition = new Vector2(0f, -32f);
        distanceBadgeRect.sizeDelta = new Vector2(76f, 28f);

        Image badgeImg = badgeGo.AddComponent<Image>();
        badgeImg.raycastTarget = false;
        badgeImg.sprite = CreateRoundedPillSprite();
        badgeImg.type = Image.Type.Sliced;
        badgeImg.color = new Color(0.043f, 0.141f, 0.098f, 0.88f);

        Outline badgeOutline = badgeGo.AddComponent<Outline>();
        badgeOutline.effectColor = new Color(themeGold.r, themeGold.g, themeGold.b, 0.85f);
        badgeOutline.effectDistance = new Vector2(1.4f, -1.4f);

        GameObject textGo = new GameObject("DistanceText");
        textGo.transform.SetParent(badgeGo.transform, false);
        RectTransform textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(4f, 0f);
        textRect.offsetMax = new Vector2(-4f, 0f);

        edgeDistanceText = textGo.AddComponent<TextMeshProUGUI>();
        edgeDistanceText.raycastTarget = false;
        edgeDistanceText.alignment = TextAlignmentOptions.Center;
        edgeDistanceText.fontSize = 18f;
        edgeDistanceText.fontStyle = FontStyles.Bold;
        edgeDistanceText.textWrappingMode = TextWrappingModes.NoWrap;
        edgeDistanceText.color = Color.white;

        Shadow textShadow = textGo.AddComponent<Shadow>();
        textShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        textShadow.effectDistance = new Vector2(1.2f, -1.2f);

        edgeIndicatorRoot.SetActive(false);
    }

    private Sprite ResolveArrowSprite(out bool usedProjectSprite)
    {
        if (edgeArrowSprite != null)
        {
            usedProjectSprite = true;
            return edgeArrowSprite;
        }

        Sprite res = Resources.Load<Sprite>("UI/map_quest_edge_arrow");
        if (res != null)
        {
            usedProjectSprite = true;
            return res;
        }

#if UNITY_EDITOR
        Sprite editorSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/EcoQuest/Sprites/MapIcons/map_quest_edge_arrow.png");
        if (editorSprite != null)
        {
            usedProjectSprite = true;
            return editorSprite;
        }
#endif

        usedProjectSprite = false;
        return CreateFallbackChevronSprite();
    }

    private Sprite CreateFallbackChevronSprite()
    {
        if (runtimeFallbackArrowSprite != null) return runtimeFallbackArrowSprite;

        const int size = 32;
        runtimeFallbackArrowTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = (float)y / (size - 1);
            for (int x = 0; x < size; x++)
            {
                float nx = Mathf.Abs(((float)x / (size - 1)) - 0.5f) * 2f;
                bool insideOuter = ny >= 0.12f && ny <= 0.94f && nx <= (0.94f - ny) * 1.15f;
                bool insideNotch = ny < 0.36f && nx <= (0.36f - ny) * 1.15f;
                pixels[y * size + x] = (insideOuter && !insideNotch) ? Color.white : clear;
            }
        }

        runtimeFallbackArrowTex.SetPixels(pixels);
        runtimeFallbackArrowTex.Apply();
        runtimeFallbackArrowSprite = Sprite.Create(runtimeFallbackArrowTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return runtimeFallbackArrowSprite;
    }

    private Sprite CreateRoundedPillSprite()
    {
        if (runtimePillSprite != null) return runtimePillSprite;

        const int w = 24;
        const int h = 24;
        const float radius = 8f;
        runtimePillTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float cx = x < radius ? radius - 0.5f : (x >= w - radius ? w - radius - 0.5f : x);
                float cy = y < radius ? radius - 0.5f : (y >= h - radius ? h - radius - 0.5f : y);
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                float alpha = Mathf.Clamp01(radius + 0.5f - d);
                pixels[y * w + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        runtimePillTex.SetPixels(pixels);
        runtimePillTex.Apply();
        runtimePillSprite = Sprite.Create(runtimePillTex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(8f, 8f, 8f, 8f));
        return runtimePillSprite;
    }
}
