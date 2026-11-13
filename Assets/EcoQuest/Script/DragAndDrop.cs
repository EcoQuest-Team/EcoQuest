using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class DragAndDrop : MonoBehaviour
{
    public enum ItemState
    {
        Idle,       // Resting in world, awaiting interaction
        Held,       // Grabbed and following HoldSocket
        Released,   // Free-falling or thrown under physics
        InBin,      // Successfully deposited and counted in bin
        Settled     // Rested securely on ground/surface
    }

    public enum GrabState
    {
        Idle,
        Held,
        Dropped
    }

    private static DragAndDrop _currentlyGrabbedItem;
    public static DragAndDrop CurrentlyGrabbedItem
    {
        get
        {
            if (_currentlyGrabbedItem != null)
            {
                if (!_currentlyGrabbedItem.gameObject.activeInHierarchy || !_currentlyGrabbedItem.isGrabbed || _currentlyGrabbedItem.CurrentItemState != ItemState.Held)
                {
#if UNITY_EDITOR
                    Debug.Log($"[SelfHeal] Cleared stale CurrentlyGrabbedItem '{_currentlyGrabbedItem.name}' (active={_currentlyGrabbedItem.gameObject.activeInHierarchy}, grabbed={_currentlyGrabbedItem.isGrabbed}, state={_currentlyGrabbedItem.CurrentItemState})");
#endif
                    _currentlyGrabbedItem = null;
                }
            }
            return _currentlyGrabbedItem;
        }
        private set => _currentlyGrabbedItem = value;
    }

    public static void SetCurrentlyGrabbedItemForTesting(DragAndDrop item) => _currentlyGrabbedItem = item;

    public static bool IsAnyItemGrabbed => CurrentlyGrabbedItem != null;

    public static void ReleaseCurrentlyGrabbedItem(string reason = "system")
    {
        if (CurrentlyGrabbedItem != null)
        {
            CurrentlyGrabbedItem.ReleaseObject(reason);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticOnSubsystemRegistration()
    {
        ResetStaticState();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStaticOnBeforeSceneLoad()
    {
        ResetStaticState();
    }

    public static void ResetStaticState()
    {
        _currentlyGrabbedItem = null;
        WatchdogRecoveryCount = 0;
        _lastCandidateFrame = -1;
        _cachedCandidate = null;
        _nextPeriodicSelfHealTime = 0f;
        _wasBlockedByUI = false;
        _savedMoveSpeed = -1f;
        _savedSprintSpeed = -1f;
        playerColliders = null;
        playerTransform = null;
        playerCC = null;
        lastHoveredBinRenderers = null;
        binPropBlock = null;
        System.Array.Clear(_rayHitsBuffer, 0, _rayHitsBuffer.Length);
        System.Array.Clear(_sphereHitsBuffer, 0, _sphereHitsBuffer.Length);
        System.Array.Clear(_overlapBuffer, 0, _overlapBuffer.Length);
    }

    public static int WatchdogRecoveryCount { get; set; } = 0;

    public ItemState CurrentItemState { get; private set; } = ItemState.Idle;
    public GrabState CurrentState => CurrentItemState == ItemState.Held ? GrabState.Held : (CurrentItemState == ItemState.Released ? GrabState.Dropped : GrabState.Idle);

    private bool isHoldDragging = false;
    private const float HoldDragThreshold = 0.35f;

    [Header("Tunable Gameplay Settings")]
    [Tooltip("Smoothing speed when following HoldSocket")]
    [SerializeField] public float holdSpeed = 25f;
    [Tooltip("Base throw force impulse")]
    [SerializeField] public float throwForce = 3.5f;
    [Tooltip("Maximum allowed throw / toss velocity")]
    [SerializeField] public float maxThrowSpeed = 10f;
    [Tooltip("Seconds low velocity required before Released automatically becomes Settled")]
    [SerializeField] public float settleQuietDuration = 0.25f;
    [Tooltip("Hard cap seconds in Released before automatically becoming Settled")]
    [SerializeField] public float settleTimeout = 1.0f;
    [Tooltip("Enable detailed transition logs")]
    [SerializeField] public bool verboseDebug = false;

    [Header("Deposit Animation")]
    [Tooltip("Duration of deposit animation moving into the bin")]
    [SerializeField] public float depositDuration = 0.32f;
    [Tooltip("Easing curve for deposit animation")]
    [SerializeField] public AnimationCurve depositCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Scale multiplier applied during deposit")]
    [SerializeField] public float depositShrinkScale = 0.70f;

    [Header("Layered Pickup Query Settings")]
    [Tooltip("Radius for primary center-screen SphereCast")]
    [SerializeField] public float primaryCastRadius = 0.22f;
    [Tooltip("Radius around camera for close-range overlap fallback (starts-inside-collider fix)")]
    [SerializeField] public float closeRangeRadius = 0.55f;
    [Tooltip("Forward distance in front of camera for close-range overlap capsule")]
    [SerializeField] public float closeRangeForwardDistance = 0.65f;
    [Tooltip("Maximum screen viewport distance from center (0.5, 0.5) to qualify for aim-assist (0.13 = ~13% screen)")]
    [SerializeField] public float aimAssistViewportRadius = 0.13f;
    [Tooltip("Maximum world distance for aim-assist pickup")]
    [SerializeField] public float aimAssistMaxDistance = 5.0f;

    [Header("Watchdog & Diagnostics")]
    [Tooltip("Seconds an unheld stationary item can remain kinematic or in Released before watchdog recovers it")]
    [SerializeField] public float watchdogTimeout = 1.5f;
    [Tooltip("Log pickup hit candidates and diagnostic details")]
    [SerializeField] public bool pickupDebugMode = false;

    private static int _lastDiagnosticClickFrame = -1;
    private static int _lastRejectionLogFrame = -1;
    public static bool logNextPickupClickOnly = false;
    public static string LastPickupRejectionReason { get; private set; } = string.Empty;

    // Preallocated NonAlloc physics buffers (zero allocations per frame / per click)
    private static readonly RaycastHit[] _rayHitsBuffer = new RaycastHit[32];
    private static readonly RaycastHit[] _sphereHitsBuffer = new RaycastHit[32];
    private static readonly Collider[] _overlapBuffer = new Collider[32];
    private static int _lastCandidateFrame = -1;
    private static DragAndDrop _cachedCandidate = null;
    private static bool _wasBlockedByUI = false;
    private static float _nextPeriodicSelfHealTime = 0f;

    private float _releasedElapsed = 0f;
    private float _releasedStillTimer = 0f;
    private float _watchdogTimer = 0f;
    private bool _colliderFitted = false;
    private Coroutine _depositCoroutine = null;

    private Vector3 _trackedVelocity = Vector3.zero;
    private Vector3 _lastFixedPosition = Vector3.zero;

    private NPCInteraction cachedNpc;
    private RangerAnimationDriver cachedAnimDriver;

    [System.Serializable]
    public struct OriginalItemState
    {
        public Transform parent;
        public Vector3 localScale;
        public Transform[] childTransforms;
        public int[] childLayers;
        public bool hasRb;
        public bool isKinematic;
        public bool useGravity;
        public RigidbodyConstraints constraints;
        public RigidbodyInterpolation interpolation;
        public CollisionDetectionMode collisionDetectionMode;
        public float linearDamping;
        public float angularDamping;
        public Collider[] colliders;
        public bool[] collidersEnabled;
    }

    [Header("Distance Settings")]
    [SerializeField] private float pickupRange = 5f;
    [SerializeField] private float defaultHoldDistance = 0.265f;
    [SerializeField] private float minHoldDistance = 0.22f;
    [SerializeField] private float maxHoldDistance = 0.32f;
    [SerializeField] private Vector3 holdOffsetNormalized = new Vector3(0.0f, -0.065f, 0.265f);

    [Header("Hold Socket & Offset Settings")]
    [SerializeField] public Vector3 holdOffset = new Vector3(0.0f, -0.10f, 0.42f);
    [SerializeField] public float maxItemSize = 0.16f;

    [Header("Physics & Motion")]
    [SerializeField] private float smoothTime = 0.05f;
    [SerializeField] private float wallSphereCastRadius = 0.15f;

    [Header("Audio Hooks (Silent by default)")]
    [SerializeField] private AudioClip pickupAudioClip;
    [SerializeField] private AudioClip dropAudioClip;
    [Range(0f, 1f)] [SerializeField] private float audioVolume = 0.6f;

    private Rigidbody rb;
    private Camera mainCamera;
    private Transform holdPoint;
    private bool isGrabbed = false;
    private float currentHoldDistance = 1.10f;
    private Vector3 velDamp;
    private float grabTime;
    private Vector3 grabStartPos;
    private Quaternion grabStartRot;
    private static float _savedMoveSpeed = -1f;
    private static float _savedSprintSpeed = -1f;
    private AudioSource audioSource;
    private Collider[] myColliders;
    private static Collider[] playerColliders;
    private static Transform playerTransform;
    private static CharacterController playerCC;

    // Visual highlight feedback
    private Renderer[] myRenderers;
    private MaterialPropertyBlock propBlock;
    private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");
    private bool isHovered = false;

    // Bin highlight while carrying
    private static Renderer[] lastHoveredBinRenderers;
    private static MaterialPropertyBlock binPropBlock;

    private float bobTimer = 0f;

    // Generic Data-Driven State & Overrides
    private HoldSettings _settings;
    private HoldProfile _holdProfile;
    private HoldSizeCategory _currentSizeCategory = HoldSizeCategory.Medium;
    private float _currentGripAmount = 0.5f;
    private float _currentArmDip = 0.0f;
    private Bounds _currentCombinedBounds;
    private Vector3 _localGripOffset = Vector3.zero;
    private Vector3 _heldScale = Vector3.one;
    private Coroutine _easeCoroutine;
    private OriginalItemState _originalState;
    private bool _hasOriginalState = false;

    public bool IsGrabbed => isGrabbed;
    public HoldSizeCategory CurrentSizeCategory => _currentSizeCategory;
    public float CurrentGripAmount => _currentGripAmount;
    public float CurrentArmDip => _currentArmDip;
    public Bounds CurrentBounds => _currentCombinedBounds;
    public Vector3 GripPointWorld => transform.TransformPoint(_localGripOffset);
    public OriginalItemState SavedOriginalState => _originalState;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        _holdProfile = GetComponent<HoldProfile>();
        _settings = HoldSettings.Instance;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
        }
        myColliders = GetComponentsInChildren<Collider>(true);
        myRenderers = GetComponentsInChildren<Renderer>(true);
        propBlock = new MaterialPropertyBlock();
        if (binPropBlock == null) binPropBlock = new MaterialPropertyBlock();
        currentHoldDistance = defaultHoldDistance;
        EnsurePickable(preserveReleasedState: false, logSelfHeal: false);
    }

    private void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (_holdProfile == null) _holdProfile = GetComponent<HoldProfile>();
        if (_settings == null) _settings = HoldSettings.Instance;
        mainCamera = Camera.main;
        CachePlayerReferences();
        cachedNpc = FindFirstObjectByType<NPCInteraction>();
        cachedAnimDriver = FindAnyObjectByType<RangerAnimationDriver>();
        EnsurePickable(preserveReleasedState: false, logSelfHeal: false);
    }

    public void SetState(ItemState newState, string reason)
    {
        if (newState == ItemState.Released || newState == ItemState.Idle || newState == ItemState.Settled)
        {
            _releasedElapsed = 0f;
            _releasedStillTimer = 0f;
            _watchdogTimer = 0f;
        }

        if (CurrentItemState == newState) return;
        ItemState oldState = CurrentItemState;
        CurrentItemState = newState;
        if (verboseDebug)
        {
            Debug.Log($"[DragDrop] {gameObject.name}: {oldState} -> {newState} ({reason})");
        }
        ApplyPhysicsForState(newState);
    }

    private void ApplyPhysicsForState(ItemState state)
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb == null) return;

        switch (state)
        {
            case ItemState.Idle:
            case ItemState.Settled:
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.WakeUp();
                break;

            case ItemState.Held:
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
                rb.useGravity = false;
                break;

            case ItemState.Released:
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.WakeUp();
                break;

            case ItemState.InBin:
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
                rb.useGravity = false;
                break;
        }
    }

    public bool EnsurePickable()
    {
        return EnsurePickable(preserveReleasedState: false, logSelfHeal: false);
    }

    public bool EnsurePickable(bool preserveReleasedState)
    {
        return EnsurePickable(preserveReleasedState, logSelfHeal: false);
    }

    public bool EnsurePickable(bool preserveReleasedState, bool logSelfHeal)
    {
        if (CurrentItemState == ItemState.InBin) return false;
        TrashItem trash = GetComponent<TrashItem>();

        // If actually held by player, don't modify
        if (isGrabbed && CurrentlyGrabbedItem == this) return false;

        System.Collections.Generic.List<string> fixes = null;
#if UNITY_EDITOR
        if (logSelfHeal) fixes = new System.Collections.Generic.List<string>(4);
#endif

        // Clear stale IsDeposited flag if item is not actually deposited in a bin
        if (trash != null && trash.IsDeposited && CurrentItemState != ItemState.InBin)
        {
            trash.IsDeposited = false;
#if UNITY_EDITOR
            fixes?.Add("cleared stale IsDeposited");
#endif
        }

        // 1. Unorphan stuck Held / grabbed state
        if (isGrabbed && CurrentlyGrabbedItem != this)
        {
            isGrabbed = false;
#if UNITY_EDITOR
            fixes?.Add("cleared orphaned isGrabbed");
#endif
        }

        if (CurrentItemState == ItemState.Held && CurrentlyGrabbedItem != this)
        {
            SetState(ItemState.Idle, "SelfHeal unorphaned Held");
#if UNITY_EDITOR
            fixes?.Add("restored orphaned Held -> Idle");
#endif
        }

        if (!preserveReleasedState || CurrentItemState != ItemState.Released)
        {
            if (CurrentItemState != ItemState.Idle && CurrentItemState != ItemState.Settled)
            {
                ItemState old = CurrentItemState;
                SetState(ItemState.Idle, "EnsurePickable");
#if UNITY_EDITOR
                fixes?.Add($"state {old} -> Idle");
#endif
            }
        }

        // 2. Fit Collider to mesh
        if (!_colliderFitted)
        {
            FitColliderToMesh();
            _colliderFitted = true;
#if UNITY_EDITOR
            fixes?.Add("fitted collider to mesh");
#endif
        }

        // 3. Rigidbody sanity
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (rb.isKinematic)
            {
                rb.isKinematic = false;
#if UNITY_EDITOR
                fixes?.Add("isKinematic = false");
#endif
            }
            if (!rb.useGravity)
            {
                rb.useGravity = true;
#if UNITY_EDITOR
                fixes?.Add("useGravity = true");
#endif
            }
            if (rb.constraints != RigidbodyConstraints.None)
            {
                rb.constraints = RigidbodyConstraints.None;
#if UNITY_EDITOR
                fixes?.Add("constraints cleared");
#endif
            }
            if (rb.IsSleeping())
            {
                rb.WakeUp();
            }
        }

        // 4. Solid colliders enabled & not triggers
        if (myColliders == null || myColliders.Length == 0)
        {
            myColliders = GetComponentsInChildren<Collider>(true);
        }
        if (myColliders != null)
        {
            for (int i = 0; i < myColliders.Length; i++)
            {
                Collider c = myColliders[i];
                if (c == null) continue;
                if (!c.enabled)
                {
                    c.enabled = true;
#if UNITY_EDITOR
                    fixes?.Add($"enabled collider '{c.name}'");
#endif
                }
                if (c.isTrigger)
                {
                    c.isTrigger = false;
#if UNITY_EDITOR
                    fixes?.Add($"isTrigger false on '{c.name}'");
#endif
                }
            }
        }

        SetPlayerCollisionIgnored(false);

        // 5. Layer check
        int trashLayer = LayerMask.NameToLayer("TrashItem");
        if (trashLayer != -1)
        {
            bool anyWrongLayer = false;
            if (gameObject.layer != trashLayer) anyWrongLayer = true;
            else if (myColliders != null)
            {
                for (int i = 0; i < myColliders.Length; i++)
                {
                    if (myColliders[i] != null && myColliders[i].gameObject.layer != trashLayer)
                    {
                        anyWrongLayer = true;
                        break;
                    }
                }
            }

            if (anyWrongLayer)
            {
                SetLayerRecursively(gameObject, trashLayer);
#if UNITY_EDITOR
                fixes?.Add("layer set to TrashItem recursively");
#endif
            }
        }

        // 6. Deposit Cooldown
        if (trash != null && trash.IsOnDepositCooldown)
        {
            trash.ClearDepositCooldown();
#if UNITY_EDITOR
            fixes?.Add("cleared deposit cooldown");
#endif
        }

#if UNITY_EDITOR
        if (fixes != null && fixes.Count > 0)
        {
            Debug.Log($"[SelfHeal] Healed '{gameObject.name}': {string.Join(", ", fixes)}");
            return true;
        }
#endif
        return false;
    }

    public static void SelfHealAllUnheldItems(string reason = "")
    {
        var allItems = TrashItem.AllTrashItems;
        for (int i = 0; i < allItems.Count; i++)
        {
            TrashItem ti = allItems[i];
            if (ti == null || !ti.gameObject.activeInHierarchy || ti.IsDeposited) continue;
            DragAndDrop dad = ti.GetComponent<DragAndDrop>();
            if (dad != null && dad != CurrentlyGrabbedItem)
            {
                dad.EnsurePickable(preserveReleasedState: dad.CurrentItemState == ItemState.Released, logSelfHeal: true);
            }
        }
    }

    public void FitColliderToMesh()
    {
        BoxCollider bc = GetComponent<BoxCollider>();
        MeshFilter mf = GetComponentInChildren<MeshFilter>(true);
        if (bc == null || mf == null || mf.sharedMesh == null) return;

        Bounds mb = mf.sharedMesh.bounds;
        if (mb.size.sqrMagnitude < 0.00001f) return;

        // If mesh is on a child transform, transform its 8 corners into root local space
        if (mf.transform != transform)
        {
            Vector3 mc = mb.center;
            Vector3 me = mb.extents;
            bool first = true;
            Bounds rootLocal = default;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 ptWorld = mf.transform.TransformPoint(mc + new Vector3(me.x * x, me.y * y, me.z * z));
                Vector3 ptRoot = transform.InverseTransformPoint(ptWorld);
                if (first) { rootLocal = new Bounds(ptRoot, Vector3.zero); first = false; }
                else rootLocal.Encapsulate(ptRoot);
            }
            mb = rootLocal;
        }

        Vector3 newSize = new Vector3(
            Mathf.Max(mb.size.x, 0.18f),
            Mathf.Max(mb.size.y, 0.08f),
            Mathf.Max(mb.size.z, 0.18f)
        );
        Vector3 newCenter = mb.center;
        if (newSize.y > mb.size.y && (mb.center.y - mb.extents.y) >= -0.005f)
        {
            newCenter.y = (mb.center.y - mb.extents.y) + newSize.y * 0.5f;
        }

        bc.center = newCenter;
        bc.size = newSize;
        bc.isTrigger = false;
        bc.enabled = true;
    }

    public Bounds GetWorldBounds()
    {
        if (myColliders == null || myColliders.Length == 0)
            myColliders = GetComponentsInChildren<Collider>(true);

        bool hasBounds = false;
        Bounds combined = default;
        if (myColliders != null)
        {
            for (int i = 0; i < myColliders.Length; i++)
            {
                Collider c = myColliders[i];
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (!hasBounds)
                {
                    combined = c.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(c.bounds);
                }
            }
        }

        if (hasBounds && combined.size.sqrMagnitude > 0.00001f)
            return combined;

        return CalculateCombinedBounds(gameObject);
    }

    public static void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;
        obj.layer = newLayer;
        for (int i = 0; i < obj.transform.childCount; i++)
        {
            SetLayerRecursively(obj.transform.GetChild(i).gameObject, newLayer);
        }
    }

    private void FixedUpdate()
    {
        if (isGrabbed && CurrentItemState == ItemState.Held)
        {
            if (_lastFixedPosition != Vector3.zero && Time.fixedDeltaTime > 0f)
            {
                _trackedVelocity = (transform.position - _lastFixedPosition) / Time.fixedDeltaTime;
            }
            _lastFixedPosition = transform.position;
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        if (TrashItem.AreTrashItemsAllowed)
        {
            EnsurePickable(preserveReleasedState: false, logSelfHeal: false);
        }
    }

    private void OnDisable()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (isGrabbed) ForceRelease();
        if (_depositCoroutine != null)
        {
            StopCoroutine(_depositCoroutine);
            _depositCoroutine = null;
        }
        SetHighlight(false);
        ClearBinHighlight();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (TrashItem.AreTrashItemsAllowed)
        {
            EnsurePickable(preserveReleasedState: false, logSelfHeal: false);
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (isGrabbed) ForceRelease();
        if (_depositCoroutine != null)
        {
            StopCoroutine(_depositCoroutine);
            _depositCoroutine = null;
        }
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (isGrabbed) ForceRelease();
    }

    private void OnActiveSceneChanged(Scene current, Scene next)
    {
        if (isGrabbed) ForceRelease();
    }

    private void CachePlayerReferences()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
            playerColliders = player.GetComponentsInChildren<Collider>();
            playerCC = player.GetComponent<CharacterController>();
        }
    }

    private void Update()
    {
        if (cachedNpc == null) cachedNpc = FindFirstObjectByType<NPCInteraction>();
        bool isDialogue = (cachedNpc != null && cachedNpc.IsTalking) ||
                          (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue);

        bool isModalOpen = (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalOpen) ||
                           (UIOrchestrator.Instance != null && UIOrchestrator.Instance.AreModalsOpen) ||
                           QuizUIController.IsAnyQuizOpen ||
                           PauseMenuController.IsPaused;

        bool isBlockedByUI = isDialogue || isModalOpen;

        // Self-heal when modal closes or pause resumes
        if (_wasBlockedByUI && !isBlockedByUI && TrashItem.AreTrashItemsAllowed)
        {
            SelfHealAllUnheldItems("modal close / pause resume");
        }
        _wasBlockedByUI = isBlockedByUI;

        // Periodic lightweight check (~0.5s) for unheld items (zero allocations, no Find calls)
        if (TrashItem.AreTrashItemsAllowed && Time.unscaledTime >= _nextPeriodicSelfHealTime)
        {
            _nextPeriodicSelfHealTime = Time.unscaledTime + 0.5f;
            SelfHealAllUnheldItems("0.5s periodic check");
        }

        if (!TrashItem.AreTrashItemsAllowed)
        {
            if (isGrabbed) ReleaseObject();
            SetHighlight(false);
            ClearBinHighlight();
            return;
        }

        if (isGrabbed && (isDialogue || isModalOpen))
        {
            ReleaseObject();
            SetHighlight(false);
            ClearBinHighlight();
            return;
        }

        if (isDialogue || isModalOpen)
        {
            SetHighlight(false);
            ClearBinHighlight();
            return;
        }

        if (mainCamera == null) mainCamera = Camera.main;
        if (holdPoint == null)
        {
            holdPoint = GetHoldSocket();
        }

        UpdateHoverFeedback();

        if (isGrabbed)
        {
            // Auto-drop if player is too far (> 4m)
            if (playerTransform != null && Vector3.Distance(transform.position, playerTransform.position) > 4.0f)
            {
                ReleaseObject();
                return;
            }

            // Mouse scroll wheel distance adjustment (0.8m to 2.0m)
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                currentHoldDistance = Mathf.Clamp(currentHoldDistance + scroll * 1.5f, minHoldDistance, maxHoldDistance);
            }

            // Bin aim highlight while carrying
            UpdateBinHighlightFeedback();

            // Track if player is performing a continuous hold-and-drag
            if (Input.GetMouseButton(0) && (Time.time - grabTime) >= HoldDragThreshold)
            {
                isHoldDragging = true;
            }

            // Drop triggers
            // 1. MouseUp drops ONLY if hold-and-drag was active (prevents normal clicks from immediately dropping)
            if (Input.GetMouseButtonUp(0) && isHoldDragging)
            {
                ReleaseObject();
            }
            // 2. Click release: a subsequent click after pickup
            else if (Input.GetMouseButtonDown(0) && Time.time > grabTime + 0.2f)
            {
                ReleaseObject();
            }
            // 3. Drop hotkeys: G key or E key
            else if (Input.GetKeyDown(KeyCode.G) || (Input.GetKeyDown(KeyCode.E) && Time.time > grabTime + 0.2f))
            {
                ReleaseObject();
            }
        }
        else
        {
            if (CurrentlyGrabbedItem == null)
            {
                if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E))
                {
                    TryGrabObject();
                }
            }
        }

        StepPhysicsAndSettle(Time.deltaTime);
    }

    public void StepPhysicsAndSettle(float dt)
    {
        if (isGrabbed || CurrentItemState == ItemState.Held || CurrentItemState == ItemState.InBin)
        {
            _releasedElapsed = 0f;
            _releasedStillTimer = 0f;
            _watchdogTimer = 0f;
            return;
        }

        if (rb == null) rb = GetComponent<Rigidbody>();

        // 1. Automatic Released -> Settled transition (velocity under threshold for ~0.25s, or IsSleeping, or hard cap ~1.0s)
        if (CurrentItemState == ItemState.Released)
        {
            _releasedElapsed += dt;
            bool isSlowOrSleeping = rb == null || rb.isKinematic || rb.IsSleeping() ||
                                    (rb.linearVelocity.sqrMagnitude < 0.04f && rb.angularVelocity.sqrMagnitude < 0.25f);

            if (isSlowOrSleeping)
            {
                _releasedStillTimer += dt;
            }
            else
            {
                _releasedStillTimer = 0f;
            }

            float quietTarget = settleQuietDuration > 0.01f ? settleQuietDuration : 0.25f;
            float hardCap = (settleTimeout > 0.1f && settleTimeout <= 1.2f) ? settleTimeout : 1.0f;

            if ((rb != null && rb.IsSleeping()) || _releasedStillTimer >= quietTarget || _releasedElapsed >= hardCap)
            {
                SetState(ItemState.Settled, _releasedElapsed >= hardCap ? "settled (hard cap)" : "settled automatically");
                EnsurePickable();
                return;
            }
        }

        // 2. Watchdog safety net: recover any unheld stationary item left kinematic or in a non-resting state for >= watchdogTimeout
        if (rb != null && (rb.isKinematic || (CurrentItemState != ItemState.Idle && CurrentItemState != ItemState.Settled)))
        {
            if (rb.linearVelocity.sqrMagnitude < 0.01f)
            {
                _watchdogTimer += dt;
                float wdLimit = watchdogTimeout > 1.2f ? watchdogTimeout : 1.5f;
                if (_watchdogTimer >= wdLimit)
                {
                    ItemState oldState = CurrentItemState;
                    _watchdogTimer = 0f;
                    WatchdogRecoveryCount++;
                    EnsurePickable();
#if UNITY_EDITOR
                    Debug.LogWarning($"[DragAndDrop Watchdog] Recovered '{gameObject.name}' from state '{oldState}' after {wdLimit:F1}s.");
#endif
                }
            }
            else
            {
                _watchdogTimer = 0f;
            }
        }
        else
        {
            _watchdogTimer = 0f;
        }
    }

    private Transform GetHoldSocket()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Transform socket = mainCamera.transform.Find("HoldSocket");
            if (socket == null)
            {
                GameObject go = new GameObject("HoldSocket");
                go.transform.SetParent(mainCamera.transform, false);
                go.transform.localPosition = holdOffset;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                socket = go.transform;
            }
            else
            {
                socket.localPosition = holdOffset;
            }
            return socket;
        }

        if (playerTransform == null) CachePlayerReferences();
        if (playerTransform != null)
        {
            Transform camRoot = playerTransform.Find("PlayerCameraRoot") ?? playerTransform.Find("Visual/Armature/Hips/Spine/Chest/Neck/Head/CameraRoot");
            if (camRoot != null)
            {
                Transform socket = camRoot.Find("HoldSocket");
                if (socket == null)
                {
                    GameObject go = new GameObject("HoldSocket");
                    go.transform.SetParent(camRoot, false);
                    go.transform.localPosition = holdOffset;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    socket = go.transform;
                }
                return socket;
            }
        }
        return null;
    }

    private void LateUpdate()
    {
        if (!isGrabbed) return;
        if (holdPoint == null) holdPoint = GetHoldSocket();
        if (holdPoint == null) return;
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera != null && holdPoint.parent == mainCamera.transform)
        {
            holdPoint.localPosition = holdOffset;
        }

        // Position at HoldSocket
        Vector3 targetHoldPos = holdPoint.position;

        // Subtle walk sway and bob that matches player movement
        float speed = 0f;
        if (playerCC != null) speed = new Vector2(playerCC.velocity.x, playerCC.velocity.z).magnitude;
        float moveFactor = Mathf.Clamp01(speed / 4.0f);
        if (moveFactor > 0.01f)
        {
            bobTimer += Time.deltaTime * 8f;
        }
        if (mainCamera != null)
        {
            Vector3 bobOffset = new Vector3(
                Mathf.Cos(bobTimer * 0.5f) * 0.008f,
                Mathf.Sin(bobTimer) * 0.006f,
                0f
            ) * moveFactor;
            targetHoldPos += mainCamera.transform.TransformVector(bobOffset);
        }

        // Camera-aligned orientation
        Quaternion targetRot = (mainCamera != null ? mainCamera.transform.rotation * Quaternion.Euler(6f, -8f, 0f) : holdPoint.rotation);
        if (_holdProfile != null && _holdProfile.rotationOffset != Vector3.zero)
        {
            targetRot *= Quaternion.Euler(_holdProfile.rotationOffset);
        }
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 18f);

        // Align grip point with targetHoldPos
        Vector3 scaleFactor = Vector3.one;
        if (_hasOriginalState && _originalState.localScale.x != 0f && _originalState.localScale.y != 0f && _originalState.localScale.z != 0f)
        {
            scaleFactor = new Vector3(
                transform.localScale.x / _originalState.localScale.x,
                transform.localScale.y / _originalState.localScale.y,
                transform.localScale.z / _originalState.localScale.z
            );
        }
        Vector3 scaledGripOffset = Vector3.Scale(_localGripOffset, scaleFactor);
        Vector3 desiredPos = targetHoldPos - transform.rotation * scaledGripOffset;

        // Critically damped follow
        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref velDamp, smoothTime);

        // Keep Rigidbody in sync without physics drift
        if (rb != null)
        {
            rb.position = transform.position;
            rb.rotation = transform.rotation;
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    private void UpdateHoverFeedback()
    {
        if (isGrabbed)
        {
            if (isHovered) SetHighlight(false);
            return;
        }

        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;

        int layerMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "BinSolid");
        Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        DragAndDrop candidate = FindGrabbableCandidate(ray, mainCamera, pickupRange, layerMask);

        bool aimingAtMe = (candidate == this);

        if (aimingAtMe != isHovered)
        {
            SetHighlight(aimingAtMe);
        }
    }

    private void SetHighlight(bool highlight)
    {
        isHovered = highlight;
        if (myRenderers == null) myRenderers = GetComponentsInChildren<Renderer>(true);
        if (propBlock == null) propBlock = new MaterialPropertyBlock();

        Color emColor = highlight ? new Color(0.18f, 0.40f, 0.18f) : Color.black;
        for (int i = 0; i < myRenderers.Length; i++)
        {
            if (myRenderers[i] != null)
            {
                myRenderers[i].GetPropertyBlock(propBlock);
                propBlock.SetColor(EmissionColorProp, emColor);
                myRenderers[i].SetPropertyBlock(propBlock);
            }
        }
    }

    private void UpdateBinHighlightFeedback()
    {
        if (mainCamera == null) return;

        Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, 5.0f))
        {
            Transform hitT = hit.collider.transform;
            bool isBin = hit.collider.GetComponentInParent<TrashBin>() != null ||
                         hitT.CompareTag("Biodegradable") || hitT.CompareTag("Recyclable") || hitT.CompareTag("NonBiodegradable") ||
                         hit.collider.gameObject.layer == LayerMask.NameToLayer("BinSolid");

            if (isBin)
            {
                Transform binRoot = hit.collider.GetComponentInParent<TrashBin>()?.transform ?? hitT.parent ?? hitT;
                Renderer[] binRends = binRoot.GetComponentsInChildren<Renderer>();
                if (binRends != lastHoveredBinRenderers)
                {
                    ClearBinHighlight();
                    lastHoveredBinRenderers = binRends;
                    SetBinRenderersHighlight(binRends, true);
                }
                return;
            }
        }

        ClearBinHighlight();
    }

    private static void ClearBinHighlight()
    {
        if (lastHoveredBinRenderers != null)
        {
            SetBinRenderersHighlight(lastHoveredBinRenderers, false);
            lastHoveredBinRenderers = null;
        }
    }

    private static void SetBinRenderersHighlight(Renderer[] rends, bool highlight)
    {
        if (rends == null) return;
        if (binPropBlock == null) binPropBlock = new MaterialPropertyBlock();
        Color emColor = highlight ? new Color(0.12f, 0.28f, 0.12f) : Color.black;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null)
            {
                rends[i].GetPropertyBlock(binPropBlock);
                binPropBlock.SetColor(EmissionColorProp, emColor);
                rends[i].SetPropertyBlock(binPropBlock);
            }
        }
    }

    private static bool IsValidCandidate(DragAndDrop dad)
    {
        if (dad == null || !dad.gameObject.activeInHierarchy) return false;
        if (dad.isGrabbed || dad.CurrentItemState == ItemState.Held || dad.CurrentItemState == ItemState.InBin) return false;
        TrashItem ti = dad.GetComponent<TrashItem>();
        if (ti != null && ti.IsDeposited) return false;
        return true;
    }

    public static void EnsureAllUnheldItemsPickable()
    {
        var allItems = TrashItem.AllTrashItems;
        for (int i = 0; i < allItems.Count; i++)
        {
            TrashItem ti = allItems[i];
            if (ti == null || !ti.gameObject.activeInHierarchy || ti.IsDeposited) continue;
            DragAndDrop dad = ti.GetComponent<DragAndDrop>();
            if (dad != null && !dad.isGrabbed && dad.CurrentItemState != ItemState.Held && dad.CurrentItemState != ItemState.InBin)
            {
                dad.EnsurePickable(preserveReleasedState: true);
            }
        }
        Physics.SyncTransforms();
    }

    public static DragAndDrop FindGrabbableCandidate(Ray ray, Camera cam, float range, int layerMask, bool debugMode = false)
    {
        if (!debugMode && Time.frameCount == _lastCandidateFrame && Application.isPlaying)
        {
            return _cachedCandidate;
        }

        // Ensure all non-Held, non-InBin items are pickable before casting
        EnsureAllUnheldItemsPickable();

        Vector3 camPos = ray.origin;
        Vector3 camFwd = ray.direction.sqrMagnitude > 0.0001f ? ray.direction.normalized : (cam != null ? cam.transform.forward : Vector3.forward);

        // LAYER 1: Primary Query (Center-screen RaycastNonAlloc + SphereCastNonAlloc with queriesHitBackfaces = true)
        bool prevBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        DragAndDrop candidate = null;
        float closestDist = float.MaxValue;

        try
        {
            int rayHitsCount = Physics.RaycastNonAlloc(ray, _rayHitsBuffer, range, layerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < rayHitsCount; i++)
            {
                Collider col = _rayHitsBuffer[i].collider;
                if (col == null || col.isTrigger) continue;
                DragAndDrop dad = col.GetComponentInParent<DragAndDrop>();
                if (IsValidCandidate(dad))
                {
                    if (_rayHitsBuffer[i].distance < closestDist)
                    {
                        closestDist = _rayHitsBuffer[i].distance;
                        candidate = dad;
                    }
                }
            }

            if (candidate != null)
            {
                if (debugMode) Debug.Log($"[DragAndDrop] Layer 1 Candidate via RaycastNonAlloc: '{candidate.name}' at {closestDist:F2}m");
                _lastCandidateFrame = Time.frameCount;
                _cachedCandidate = candidate;
                LastPickupRejectionReason = string.Empty;
                return candidate;
            }

            // Narrow SphereCast from slightly backed-up origin so items overlapping ray.origin at 0.3m are hit cleanly
            float castRadius = 0.12f;
            var allTrash = TrashItem.AllTrashItems;
            if (allTrash.Count > 0 && allTrash[0] != null)
            {
                var refDad = allTrash[0].GetComponent<DragAndDrop>();
                if (refDad != null) castRadius = refDad.primaryCastRadius;
            }

            Ray backedRay = new Ray(camPos - camFwd * castRadius, camFwd);
            int sphereHitsCount = Physics.SphereCastNonAlloc(backedRay, castRadius, _sphereHitsBuffer, range + castRadius, layerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < sphereHitsCount; i++)
            {
                Collider col = _sphereHitsBuffer[i].collider;
                if (col == null || col.isTrigger) continue;
                DragAndDrop dad = col.GetComponentInParent<DragAndDrop>();
                if (IsValidCandidate(dad))
                {
                    float effDist = Mathf.Max(0f, _sphereHitsBuffer[i].distance - castRadius);
                    if (effDist < closestDist)
                    {
                        closestDist = effDist;
                        candidate = dad;
                    }
                }
            }

            if (candidate != null)
            {
                if (debugMode) Debug.Log($"[DragAndDrop] Layer 1 Candidate via SphereCastNonAlloc: '{candidate.name}' at {closestDist:F2}m");
                _lastCandidateFrame = Time.frameCount;
                _cachedCandidate = candidate;
                LastPickupRejectionReason = string.Empty;
                return candidate;
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = prevBackfaces;
        }

        // LAYER 2: Close-Range OverlapSphere / OverlapCapsule Fallback at Camera (for items <= 0.8m away)
        float crRadius = 0.45f;
        float crForwardDist = 0.65f;
        if (TrashItem.AllTrashItems.Count > 0 && TrashItem.AllTrashItems[0] != null)
        {
            var dadInstance = TrashItem.AllTrashItems[0].GetComponent<DragAndDrop>();
            if (dadInstance != null)
            {
                crRadius = dadInstance.closeRangeRadius;
                crForwardDist = dadInstance.closeRangeForwardDistance;
            }
        }

        Vector3 p0 = camPos;
        Vector3 p1 = camPos + camFwd * crForwardDist;
        int overlapCount = Physics.OverlapCapsuleNonAlloc(p0, p1, crRadius, _overlapBuffer, layerMask, QueryTriggerInteraction.Ignore);

        DragAndDrop bestClose = null;
        float minAngle = float.MaxValue;
        float minCloseDist = float.MaxValue;

        for (int i = 0; i < overlapCount; i++)
        {
            Collider col = _overlapBuffer[i];
            if (col == null || col.isTrigger) continue;
            DragAndDrop dad = col.GetComponentInParent<DragAndDrop>();
            if (IsValidCandidate(dad))
            {
                Bounds cb = col.bounds;
                Vector3 toCenter = cb.center - camPos;
                float dist = toCenter.magnitude;
                float angleCenter = dist > 0.001f ? Vector3.Angle(camFwd, toCenter) : 0f;

                Vector3 rayClosestPt = camPos + camFwd * Mathf.Clamp(Vector3.Dot(toCenter, camFwd), 0f, crForwardDist);
                Vector3 boxClosestPt = cb.ClosestPoint(rayClosestPt);
                Vector3 toClosest = boxClosestPt - camPos;
                float angleClosest = toClosest.sqrMagnitude > 0.0001f ? Vector3.Angle(camFwd, toClosest) : 0f;
                float angle = Mathf.Min(angleCenter, angleClosest);

                if (angle < 75f)
                {
                    if (angle < minAngle || (Mathf.Abs(angle - minAngle) < 5f && dist < minCloseDist))
                    {
                        minAngle = angle;
                        minCloseDist = dist;
                        bestClose = dad;
                    }
                }
            }
        }

        if (bestClose != null)
        {
            if (debugMode) Debug.Log($"[DragAndDrop] Layer 2 Candidate via Close-Range Overlap: '{bestClose.name}' (angle={minAngle:F1}deg, dist={minCloseDist:F2}m)");
            _lastCandidateFrame = Time.frameCount;
            _cachedCandidate = bestClose;
            LastPickupRejectionReason = string.Empty;
            return bestClose;
        }

        // LAYER 3: Aim-Assist Fallback (Live GetWorldBounds() + Screen-space / Ray-distance + Line of Sight)
        float aaViewportRadius = 0.09f;
        float aaMaxDist = range;
        if (TrashItem.AllTrashItems.Count > 0 && TrashItem.AllTrashItems[0] != null)
        {
            var dadInstance = TrashItem.AllTrashItems[0].GetComponent<DragAndDrop>();
            if (dadInstance != null)
            {
                aaViewportRadius = dadInstance.aimAssistViewportRadius;
                aaMaxDist = Mathf.Min(range, dadInstance.aimAssistMaxDistance);
            }
        }

        var itemsList = TrashItem.AllTrashItems;
        DragAndDrop bestAimAssist = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < itemsList.Count; i++)
        {
            TrashItem ti = itemsList[i];
            if (ti == null || !ti.gameObject.activeInHierarchy) continue;
            DragAndDrop dad = ti.GetComponent<DragAndDrop>();
            if (!IsValidCandidate(dad)) continue;

            Bounds b = dad.GetWorldBounds();
            Vector3 targetCenter = b.center;
            Vector3 camToTarget = targetCenter - camPos;
            float wDist = camToTarget.magnitude;
            if (wDist > aaMaxDist) continue;

            float forwardProj = Vector3.Dot(camToTarget, camFwd);
            if (forwardProj <= -0.05f) continue;

            // Check both viewport distance (if camera matches ray) and world ray-to-bounds distance
            bool withinAimAssist = false;
            float score = float.MaxValue;

            if (cam != null && Vector3.Distance(cam.transform.position, camPos) < 0.05f && Vector3.Angle(cam.transform.forward, camFwd) < 2f)
            {
                Vector3 vp = cam.WorldToViewportPoint(targetCenter);
                if (vp.z > 0f && vp.z <= aaMaxDist)
                {
                    float vpDist = Vector2.Distance(new Vector2(vp.x, vp.y), new Vector2(0.5f, 0.5f));
                    if (vpDist <= aaViewportRadius)
                    {
                        withinAimAssist = true;
                        score = vpDist;
                    }
                }
            }

            if (!withinAimAssist)
            {
                Vector3 ptOnRay = camPos + camFwd * Mathf.Clamp(forwardProj, 0f, aaMaxDist);
                Vector3 closestOnBounds = b.ClosestPoint(ptOnRay);
                float perpDist = Vector3.Distance(ptOnRay, closestOnBounds);
                if (perpDist <= 0.18f)
                {
                    withinAimAssist = true;
                    score = perpDist;
                }
            }

            if (withinAimAssist && score < bestScore)
            {
                bool blocked = false;
                if (wDist > 0.15f)
                {
                    Vector3 dir = camToTarget / wDist;
                    float checkDist = Mathf.Max(0.05f, wDist - b.extents.magnitude - 0.05f);
                    int occluderMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "TrashItem", "BinSolid");
                    if (Physics.Raycast(camPos, dir, out RaycastHit losHit, checkDist, occluderMask, QueryTriggerInteraction.Ignore))
                    {
                        blocked = true;
                    }
                }

                if (!blocked)
                {
                    bestScore = score;
                    bestAimAssist = dad;
                }
            }
        }

        if (bestAimAssist != null)
        {
            if (debugMode) Debug.Log($"[DragAndDrop] Layer 3 Candidate via Aim-Assist: '{bestAimAssist.name}' (score={bestScore:F3})");
            _lastCandidateFrame = Time.frameCount;
            _cachedCandidate = bestAimAssist;
            LastPickupRejectionReason = string.Empty;
            return bestAimAssist;
        }

        LastPickupRejectionReason = ClassifyPickupRejectionReason(ray, range, layerMask);
        _lastCandidateFrame = Time.frameCount;
        _cachedCandidate = null;
        return null;
    }

    private static string ClassifyPickupRejectionReason(Ray ray, float range, int layerMask)
    {
        // Check if ray hits a trash item that is in an unpickable state (Held or InBin) or beyond pickup range
        int allLayers = ~LayerMask.GetMask("Player", "Ignore Raycast");
        int hits = Physics.RaycastNonAlloc(ray, _rayHitsBuffer, range * 2.5f, allLayers, QueryTriggerInteraction.Collide);
        Collider firstNonTrashHit = null;
        float firstNonTrashDist = float.MaxValue;

        for (int i = 0; i < hits; i++)
        {
            Collider col = _rayHitsBuffer[i].collider;
            if (col == null) continue;
            DragAndDrop dad = col.GetComponentInParent<DragAndDrop>();
            TrashItem ti = col.GetComponentInParent<TrashItem>();
            if (dad != null || ti != null)
            {
                if (dad != null && (dad.isGrabbed || dad.CurrentItemState == ItemState.Held))
                    return $"Rejected_TargetAlreadyHeld (item='{dad.name}')";
                if ((dad != null && dad.CurrentItemState == ItemState.InBin) || (ti != null && ti.IsDeposited))
                    return $"Rejected_TargetAlreadyDepositedInBin (item='{(dad != null ? dad.name : ti.name)}')";
                if (_rayHitsBuffer[i].distance > range)
                    return $"Rejected_OutOfRange (item='{(dad != null ? dad.name : ti.name)}', dist={_rayHitsBuffer[i].distance:F2}m > max={range:F1}m)";
            }
            else if (!col.isTrigger && _rayHitsBuffer[i].distance < firstNonTrashDist)
            {
                firstNonTrashDist = _rayHitsBuffer[i].distance;
                firstNonTrashHit = col;
            }
        }

        // Check nearest trash item in scene to see if it's out of range or occluded
        var allItems = TrashItem.AllTrashItems;
        TrashItem closestItem = null;
        float closestAngle = float.MaxValue;
        float closestDist = float.MaxValue;
        for (int i = 0; i < allItems.Count; i++)
        {
            TrashItem ti = allItems[i];
            if (ti == null || !ti.gameObject.activeInHierarchy || ti.IsDeposited) continue;
            Vector3 toItem = ti.transform.position - ray.origin;
            float d = toItem.magnitude;
            float ang = d > 0.001f ? Vector3.Angle(ray.direction, toItem) : 0f;
            if (ang < closestAngle)
            {
                closestAngle = ang;
                closestDist = d;
                closestItem = ti;
            }
        }

        if (closestItem != null && closestAngle < 15f && closestDist > range)
        {
            return $"Rejected_OutOfRange (item='{closestItem.name}', dist={closestDist:F2}m > max={range:F1}m)";
        }

        if (firstNonTrashHit != null && firstNonTrashDist <= range)
        {
            if (closestItem != null && closestAngle < 15f && closestDist <= range && firstNonTrashDist < closestDist)
            {
                return $"Rejected_OccludedByObstacle (target='{closestItem.name}' at {closestDist:F2}m blocked by '{firstNonTrashHit.name}' [layer={LayerMask.LayerToName(firstNonTrashHit.gameObject.layer)}] at {firstNonTrashDist:F2}m)";
            }
            return $"Rejected_AimedAtNonTrashSurface (hit='{firstNonTrashHit.name}', layer='{LayerMask.LayerToName(firstNonTrashHit.gameObject.layer)}', dist={firstNonTrashDist:F2}m)";
        }

        return $"Rejected_NoTrashInAimCone (nearest='{(closestItem != null ? closestItem.name : "none")}', angle={closestAngle:F1}deg, dist={closestDist:F2}m)";
    }

    public static DragAndDrop FindGrabbableCandidate(Ray ray, float range, int layerMask, bool debugMode = false)
    {
        return FindGrabbableCandidate(ray, Camera.main, range, layerMask, debugMode);
    }

    public static DragAndDrop FindBadStateTrashUnderCrosshair(Ray ray, Camera cam, float range)
    {
        Vector3 camPos = cam != null ? cam.transform.position : ray.origin;
        Vector3 camFwd = ray.direction;

        int allMask = ~LayerMask.GetMask("Player", "Ignore Raycast");

        // 1. Raycast with Collide triggers on all surfaces
        int hitCount = Physics.RaycastNonAlloc(ray, _rayHitsBuffer, range, allMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _rayHitsBuffer[i].collider;
            if (col == null) continue;
            DragAndDrop dad = col.GetComponentInParent<DragAndDrop>();
            if (dad != null && dad.CurrentItemState != ItemState.InBin)
            {
                return dad;
            }
        }

        // 2. SphereCast with Collide triggers
        int sphereCount = Physics.SphereCastNonAlloc(ray, 0.18f, _sphereHitsBuffer, range, allMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < sphereCount; i++)
        {
            Collider col = _sphereHitsBuffer[i].collider;
            if (col == null) continue;
            DragAndDrop dad = col.GetComponentInParent<DragAndDrop>();
            if (dad != null && dad.CurrentItemState != ItemState.InBin)
            {
                return dad;
            }
        }

        // 3. Close-range OverlapSphere at camera
        int overlapCount = Physics.OverlapSphereNonAlloc(camPos + camFwd * 0.4f, 0.55f, _overlapBuffer, allMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < overlapCount; i++)
        {
            Collider col = _overlapBuffer[i];
            if (col == null) continue;
            DragAndDrop dad = col.GetComponentInParent<DragAndDrop>();
            if (dad != null && dad.CurrentItemState != ItemState.InBin)
            {
                return dad;
            }
        }

        // 4. Ray-to-Item Geometric Distance Scan (Works even if all colliders are disabled!)
        var allItems = TrashItem.AllTrashItems;
        DragAndDrop bestCandidate = null;
        float bestDistToRay = float.MaxValue;

        for (int i = 0; i < allItems.Count; i++)
        {
            TrashItem ti = allItems[i];
            if (ti == null || !ti.gameObject.activeInHierarchy) continue;
            DragAndDrop dad = ti.GetComponent<DragAndDrop>();
            if (dad == null || dad.CurrentItemState == ItemState.InBin) continue;

            BoxCollider bc = dad.GetComponent<BoxCollider>();
            Vector3 targetCenter = bc != null ? dad.transform.TransformPoint(bc.center) : dad.transform.position;
            Vector3 camToTarget = targetCenter - camPos;
            float forwardProj = Vector3.Dot(camToTarget, camFwd);
            if (forwardProj <= 0.05f || forwardProj > range) continue;

            Vector3 ptOnRay = camPos + camFwd * forwardProj;
            float distToRay = Vector3.Distance(ptOnRay, targetCenter);
            float maxThreshold = 0.35f;
            if (bc != null)
            {
                maxThreshold = Mathf.Max(maxThreshold, Vector3.Scale(bc.size * 0.5f, dad.transform.lossyScale).magnitude * 1.5f);
            }

            if (distToRay <= maxThreshold && distToRay < bestDistToRay)
            {
                bestDistToRay = distToRay;
                bestCandidate = dad;
            }
        }

        return bestCandidate;
    }

    public void TryGrabObject()
    {
        if (!TrashItem.AreTrashItemsAllowed)
        {
            LastPickupRejectionReason = "Rejected_TutorialGated (TrashItem.AreTrashItemsAllowed is false until FreePlay)";
            if (pickupDebugMode && Time.frameCount != _lastRejectionLogFrame)
            {
                _lastRejectionLogFrame = Time.frameCount;
                Debug.Log($"[DragAndDrop] Pickup rejected: {LastPickupRejectionReason}");
            }
            return;
        }
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null)
        {
            LastPickupRejectionReason = "Rejected_MissingMainCamera";
            if (pickupDebugMode && Time.frameCount != _lastRejectionLogFrame)
            {
                _lastRejectionLogFrame = Time.frameCount;
                Debug.Log($"[DragAndDrop] Pickup rejected: {LastPickupRejectionReason}");
            }
            return;
        }

        if (cachedNpc == null) cachedNpc = FindFirstObjectByType<NPCInteraction>();
        bool isDialogue = (cachedNpc != null && cachedNpc.IsTalking) ||
                          (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue);

        bool isModalOpen = (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalOpen) ||
                           (UIOrchestrator.Instance != null && UIOrchestrator.Instance.AreModalsOpen) ||
                           QuizUIController.IsAnyQuizOpen ||
                           PauseMenuController.IsPaused;

        if (isDialogue || isModalOpen)
        {
            LastPickupRejectionReason = $"Rejected_UIModalOrDialogueOpen (dialogue={isDialogue}, modal={isModalOpen})";
            if (pickupDebugMode && Time.frameCount != _lastRejectionLogFrame)
            {
                _lastRejectionLogFrame = Time.frameCount;
                Debug.Log($"[DragAndDrop] Pickup rejected: {LastPickupRejectionReason}");
            }
            return;
        }

        // Validate CurrentlyGrabbedItem: clear phantom / stale references
        if (CurrentlyGrabbedItem != null && (!CurrentlyGrabbedItem.isGrabbed || CurrentlyGrabbedItem.CurrentItemState != ItemState.Held || !CurrentlyGrabbedItem.gameObject.activeInHierarchy))
        {
#if UNITY_EDITOR
            Debug.Log($"[SelfHeal] Cleared desynced CurrentlyGrabbedItem '{CurrentlyGrabbedItem.name}' before grab");
#endif
            CurrentlyGrabbedItem = null;
        }

        if (CurrentlyGrabbedItem != null)
        {
            LastPickupRejectionReason = $"Rejected_AlreadyHoldingItem (holding='{CurrentlyGrabbedItem.name}')";
            if (pickupDebugMode && Time.frameCount != _lastRejectionLogFrame)
            {
                _lastRejectionLogFrame = Time.frameCount;
                Debug.Log($"[DragAndDrop] Pickup rejected: {LastPickupRejectionReason}");
            }
            return;
        }

        int layerMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "BinSolid");
        Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));

#if UNITY_EDITOR
        if ((pickupDebugMode || logNextPickupClickOnly) && Time.frameCount != _lastDiagnosticClickFrame)
        {
            _lastDiagnosticClickFrame = Time.frameCount;
            RunPickupDiagnostic(ray, mainCamera, pickupRange, "Debug Mode / Diagnostic Triggered");
            logNextPickupClickOnly = false;
        }
#endif

        DragAndDrop candidate = FindGrabbableCandidate(ray, mainCamera, pickupRange, layerMask, pickupDebugMode);

        // Self-heal and retry immediately in the same click if an item with a bad state was targeted
        if (candidate == null)
        {
            DragAndDrop badItem = FindBadStateTrashUnderCrosshair(ray, mainCamera, pickupRange);
            if (badItem != null)
            {
                badItem.EnsurePickable(preserveReleasedState: false, logSelfHeal: true);
                Physics.SyncTransforms();
                _lastCandidateFrame = -1;
                _cachedCandidate = null;
                candidate = FindGrabbableCandidate(ray, mainCamera, pickupRange, layerMask, pickupDebugMode);
            }
        }

        if (candidate == null)
        {
            if (pickupDebugMode && Time.frameCount != _lastRejectionLogFrame)
            {
                _lastRejectionLogFrame = Time.frameCount;
                Debug.Log($"[DragAndDrop] Pickup rejected: {LastPickupRejectionReason}");
            }
            return;
        }

        candidate.GrabObject();
    }

#if UNITY_EDITOR
    private static void RunPickupDiagnostic(Ray ray, Camera cam, float range, string triggerReason = "User Click")
    {
        Vector3 camPos = cam != null ? cam.transform.position : ray.origin;
        Vector3 rayDir = ray.direction;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<color=#FFD700>=== [DragAndDrop Pickup Diagnostic] ===</color>");
        sb.AppendLine($"1. Camera/Ray: Trigger='{triggerReason}', Pos={camPos}, Origin={ray.origin}, Dir={rayDir}, MaxRange={range:F1}m");

        int allMask = ~LayerMask.GetMask("Player", "Ignore Raycast");
        RaycastHit[] rayHits = Physics.RaycastAll(ray, range, allMask, QueryTriggerInteraction.Collide);
        RaycastHit[] sphereHits = Physics.SphereCastAll(ray, 0.12f, range, allMask, QueryTriggerInteraction.Collide);

        sb.Append($"2. First hit: {(rayHits.Length > 0 ? $"'{rayHits[0].collider.name}' ({rayHits[0].distance:F2}m)" : "None")}\n");
        sb.Append($"   RaycastAll ({rayHits.Length} hit(s)): ");
        for (int i = 0; i < rayHits.Length; i++)
        {
            sb.Append($"['{rayHits[i].collider.name}', layer={LayerMask.LayerToName(rayHits[i].collider.gameObject.layer)}, trig={rayHits[i].collider.isTrigger}, dist={rayHits[i].distance:F2}m] ");
        }
        sb.AppendLine();

        sb.Append($"   SphereCastAll ({sphereHits.Length} hit(s)): ");
        for (int i = 0; i < sphereHits.Length; i++)
        {
            sb.Append($"['{sphereHits[i].collider.name}', layer={LayerMask.LayerToName(sphereHits[i].collider.gameObject.layer)}, trig={sphereHits[i].collider.isTrigger}, dist={sphereHits[i].distance:F2}m] ");
        }
        sb.AppendLine();

        Collider[] insideCols = Physics.OverlapSphere(ray.origin, 0.05f, LayerMask.GetMask("TrashItem"), QueryTriggerInteraction.Collide);
        sb.Append($"3. Ray origin inside trash collider: {(insideCols.Length > 0 ? "YES" : "NO")}");
        if (insideCols.Length > 0)
        {
            sb.Append(" (inside: ");
            foreach (var c in insideCols) sb.Append($"'{c.name}', ");
            sb.Append(")");
        }
        sb.AppendLine();

        int mask = ~LayerMask.GetMask("Player", "Ignore Raycast", "BinSolid");
        DragAndDrop cand = FindGrabbableCandidate(ray, cam, range, mask, false);
        string rejectReason = "None (Grab successful)";
        if (!TrashItem.AreTrashItemsAllowed) rejectReason = "Rejected_TutorialGated";
        else if (CurrentlyGrabbedItem != null) rejectReason = $"Rejected_AlreadyHoldingItem ('{CurrentlyGrabbedItem.name}')";
        else if (cand == null) rejectReason = ClassifyPickupRejectionReason(ray, range, mask);

        if (cand != null)
        {
            TrashItem ti = cand.GetComponent<TrashItem>();
            Rigidbody candRb = cand.GetComponent<Rigidbody>();
            Collider[] cCols = cand.GetComponentsInChildren<Collider>(true);
            bool allTriggers = true;
            foreach (var c in cCols) if (c != null && !c.isTrigger) allTriggers = false;

            sb.AppendLine($"4. Target Candidate '{cand.name}': Layer={LayerMask.LayerToName(cand.gameObject.layer)}, Tag={cand.tag}, IsTriggerAll={allTriggers}");
            sb.AppendLine($"   Rigidbody: kin={(candRb != null && candRb.isKinematic)}, grav={(candRb != null && candRb.useGravity)}, constraints={(candRb != null ? candRb.constraints.ToString() : "None")}, vel={(candRb != null ? candRb.linearVelocity.ToString() : "N/A")}, sleeping={(candRb != null && candRb.IsSleeping())}");
            sb.AppendLine($"   State: ItemState={cand.CurrentItemState}, isDeposited={(ti != null && ti.IsDeposited)}, cooldown={(ti != null && ti.IsOnDepositCooldown)}");
        }
        else
        {
            sb.AppendLine("4. Target Candidate: None found by any layered query (Primary, Close-Range, Aim-Assist)");
        }

        sb.AppendLine($"5. Reject Point: {rejectReason}");
        sb.Append("<color=#FFD700>=======================================</color>");

        Debug.Log(sb.ToString());
    }

    [ContextMenu("EcoQuest: Run Pickup Diagnostic")]
    private void ContextMenuRunPickupDiagnostic()
    {
        Camera cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();
        if (cam != null)
        {
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RunPickupDiagnostic(ray, cam, pickupRange, "Manual ContextMenu trigger");
        }
        else
        {
            Debug.LogWarning("[DragAndDrop] No main camera found for diagnostic.");
        }
    }

    [ContextMenu("EcoQuest: Ensure Pickable")]
    private void ContextMenuEnsurePickable()
    {
        EnsurePickable();
        Debug.Log($"[DragAndDrop] EnsurePickable invoked manually on '{gameObject.name}'.");
    }

    [ContextMenu("EcoQuest: Fit Collider To Renderer")]
    private void ContextMenuFitColliderToRenderer()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null) UnityEditor.Undo.RecordObject(box, "Fit Collider To Mesh");
        FitColliderToMesh();
        if (box != null)
        {
            Debug.Log($"[DragAndDrop] BoxCollider on '{gameObject.name}' fitted to Mesh bounds: center={box.center}, size={box.size}");
        }
    }
#endif

    public void GrabObject()
    {
        if (isGrabbed || CurrentItemState == ItemState.InBin) return;
        TrashItem myTrash = GetComponent<TrashItem>();
        if (myTrash != null && myTrash.IsDeposited) return;

        if (mainCamera == null) mainCamera = Camera.main;
        if (CurrentlyGrabbedItem != null && CurrentlyGrabbedItem != this)
        {
            CurrentlyGrabbedItem.ForceRelease();
        }

        if (_easeCoroutine != null)
        {
            StopCoroutine(_easeCoroutine);
            _easeCoroutine = null;
        }

        if (rb == null) rb = GetComponent<Rigidbody>();
        if (_holdProfile == null) _holdProfile = GetComponent<HoldProfile>();
        if (_settings == null) _settings = HoldSettings.Instance;

        // Ensure item state, colliders, layers, Rigidbody, and flags are clean before capturing original state
        EnsurePickable();

        // 1. Capture exact original state before any modifications
        CaptureOriginalState();

        isGrabbed = true;
        SetState(ItemState.Held, "pickup");
        CurrentlyGrabbedItem = this;
        grabTime = Time.time;
        isHoldDragging = false;
        grabStartPos = transform.position;
        grabStartRot = transform.rotation;
        velDamp = Vector3.zero;
        currentHoldDistance = holdPoint != null ? holdPoint.localPosition.z : defaultHoldDistance;

        // 2. Data-driven bounds & grip calculation
        _currentCombinedBounds = CalculateCombinedBounds(gameObject);
        float longestSide = Mathf.Max(_currentCombinedBounds.size.x, Mathf.Max(_currentCombinedBounds.size.y, _currentCombinedBounds.size.z));

        // Size category
        if (_holdProfile != null && _holdProfile.overrideSizeCategory && _holdProfile.sizeCategory != HoldSizeCategory.Automatic)
        {
            _currentSizeCategory = _holdProfile.sizeCategory;
        }
        else
        {
            float epsilon = 0.002f;
            if (longestSide <= _settings.smallMaxDistance + epsilon) _currentSizeCategory = HoldSizeCategory.Small;
            else if (longestSide <= _settings.mediumMaxDistance + epsilon) _currentSizeCategory = HoldSizeCategory.Medium;
            else _currentSizeCategory = HoldSizeCategory.Large;
        }

        // Size fitting: downscale oversized items relative to camera FOV so they never fill the screen
        float fitScale = longestSide > maxItemSize ? Mathf.Clamp(maxItemSize / longestSide, 0.08f, 1.0f) : 1.0f;
        Vector3 desiredWorldScale = transform.lossyScale * fitScale;
        _heldScale = _originalState.localScale * fitScale;

        // Grip point centered on item bounds
        Vector3 gripWorld = _currentCombinedBounds.center;
        if (_holdProfile != null && _holdProfile.gripOffset != Vector3.zero)
        {
            gripWorld += transform.TransformDirection(_holdProfile.gripOffset);
        }
        _localGripOffset = transform.InverseTransformPoint(gripWorld);

        // Grip amount (finger curl from narrowest horizontal width)
        float narrowestHoriz = Mathf.Min(_currentCombinedBounds.size.x, _currentCombinedBounds.size.z);
        _currentGripAmount = Mathf.Clamp01(1.0f - Mathf.InverseLerp(_settings.minGripWidth, _settings.maxGripWidth, narrowestHoriz));

        // Arm weight dip derived from Rigidbody mass
        float mass = rb != null ? rb.mass : _settings.defaultMass;
        float massRatio = Mathf.Clamp01(Mathf.InverseLerp(_settings.minMass, _settings.maxMass, mass));
        _currentArmDip = massRatio * _settings.maxArmDipMeters;

        // Keep colliders enabled for bin triggers, but ignore collision with player capsule
        if (myColliders == null || myColliders.Length == 0) myColliders = GetComponentsInChildren<Collider>(true);
        SetPlayerCollisionIgnored(true);

        if (rb != null)
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Parent to HoldSocket
        holdPoint = GetHoldSocket();
        if (holdPoint != null)
        {
            Vector3 ps = holdPoint.lossyScale;
            _heldScale = new Vector3(
                ps.x != 0f ? desiredWorldScale.x / ps.x : desiredWorldScale.x,
                ps.y != 0f ? desiredWorldScale.y / ps.y : desiredWorldScale.y,
                ps.z != 0f ? desiredWorldScale.z / ps.z : desiredWorldScale.z
            );
            transform.SetParent(holdPoint, true);
            transform.localScale = _heldScale;

            Vector3 initialHoldPos = holdPoint.position;
            Quaternion initialRot = (mainCamera != null ? mainCamera.transform.rotation * Quaternion.Euler(6f, -8f, 0f) : holdPoint.rotation);
            if (_holdProfile != null && _holdProfile.rotationOffset != Vector3.zero) initialRot *= Quaternion.Euler(_holdProfile.rotationOffset);
            transform.rotation = initialRot;
            transform.position = initialHoldPos - initialRot * Vector3.Scale(_localGripOffset, _heldScale);
        }

        SetHighlight(false);
        SetPlayerCollisionIgnored(true);

        // Start smooth ease coroutine with subtle scale pop
        if (Application.isPlaying && gameObject.activeInHierarchy)
        {
            _easeCoroutine = StartCoroutine(GrabEaseRoutine());
        }

        // Notify animation driver
        if (cachedAnimDriver == null) cachedAnimDriver = FindAnyObjectByType<RangerAnimationDriver>();
        cachedAnimDriver?.SetCarrying(true);
        cachedAnimDriver?.PlayPickUp();

        // 15% slower walk and sprint speed while carrying
        if (playerCC != null)
        {
            var fpc = playerCC.GetComponent<StarterAssets.FirstPersonController>();
            if (fpc != null)
            {
                if (_savedMoveSpeed < 0f) _savedMoveSpeed = fpc.MoveSpeed;
                if (_savedSprintSpeed < 0f) _savedSprintSpeed = fpc.SprintSpeed;
                fpc.MoveSpeed = _savedMoveSpeed * 0.85f;
                fpc.SprintSpeed = _savedSprintSpeed * 0.85f;
            }
        }

        if (pickupAudioClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(pickupAudioClip, audioVolume);
        }
        else if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayTrashPickup();
        }
    }

    private IEnumerator GrabEaseRoutine()
    {
        float duration = 0.22f;
        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            float pop = 1.0f + Mathf.Sin(t * Mathf.PI) * 0.08f;
            transform.localScale = _heldScale * pop;
            yield return null;
        }
        transform.localScale = _heldScale;
        _easeCoroutine = null;
    }

    public void ReleaseObject(string reason = "drop")
    {
        if (!isGrabbed && CurrentItemState != ItemState.Held) return;
        isGrabbed = false;
        if (CurrentlyGrabbedItem == this) CurrentlyGrabbedItem = null;
        isHoldDragging = false;

        if (_easeCoroutine != null)
        {
            StopCoroutine(_easeCoroutine);
            _easeCoroutine = null;
        }

        // 1. Direct deposit check if released inside or directly above a bin
        QuestManager targetBin = FindOverlappingBin();
        TrashItem trash = GetComponent<TrashItem>();
        if (targetBin != null && trash != null && TrashItem.AreTrashItemsAllowed)
        {
            RestoreOriginalState(restoreCollidersOnly: true);
            bool processed = targetBin.TryProcessTrashItem(trash);
            if (processed)
            {
                FinalizeReleaseVisualsAndAudio();
                return;
            }
        }

        // 2. Normal drop into world
        RestoreOriginalState();
        SetState(ItemState.Released, reason);

        // Inherit throw velocity and player momentum
        Vector3 throwDir = mainCamera != null ? mainCamera.transform.forward : transform.forward;
        Vector3 throwVel = (throwDir * throwForce) + _trackedVelocity;
        if (throwVel.magnitude > maxThrowSpeed)
        {
            throwVel = throwVel.normalized * maxThrowSpeed;
        }
        if (rb != null)
        {
            rb.linearVelocity = throwVel;
            rb.angularVelocity = Random.insideUnitSphere * 2f;
            rb.WakeUp();
        }

        FinalizeReleaseVisualsAndAudio();
        EnsurePickable(preserveReleasedState: true, logSelfHeal: true);

        if (Application.isPlaying && gameObject.activeInHierarchy)
        {
            StartCoroutine(SettleFailsafeRoutine());
        }
    }

    public void ReleaseObject()
    {
        ReleaseObject("drop");
    }

    private void FinalizeReleaseVisualsAndAudio()
    {
        ClearBinHighlight();

        if (cachedAnimDriver == null) cachedAnimDriver = FindAnyObjectByType<RangerAnimationDriver>();
        cachedAnimDriver?.SetCarrying(false);
        cachedAnimDriver?.PlayDrop();

        // Restore walk and sprint speed
        if (playerCC != null && _savedMoveSpeed > 0f)
        {
            var fpc = playerCC.GetComponent<StarterAssets.FirstPersonController>();
            if (fpc != null)
            {
                fpc.MoveSpeed = _savedMoveSpeed;
                fpc.SprintSpeed = _savedSprintSpeed;
            }
        }

        if (dropAudioClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(dropAudioClip, audioVolume);
        }

        if (Application.isPlaying && gameObject.activeInHierarchy)
        {
            StartCoroutine(ReenablePlayerCollisionDelayed(0.35f));
        }
        else
        {
            SetPlayerCollisionIgnored(false);
        }
    }

    public void ForceRelease()
    {
        ReleaseObject("force release");
    }

    public void OnDepositedCorrectly()
    {
        OnDepositedCorrectly(transform.position + Vector3.down * 0.25f);
    }

    public void OnDepositedCorrectly(Vector3 targetDepositWorldPos, Quaternion? targetDepositWorldRot = null, QuestManager binManager = null)
    {
        if (CurrentlyGrabbedItem == this) CurrentlyGrabbedItem = null;
        isGrabbed = false;

        if (_easeCoroutine != null)
        {
            StopCoroutine(_easeCoroutine);
            _easeCoroutine = null;
        }

        SetState(ItemState.InBin, "correct deposit");
        SetPlayerCollisionIgnored(true);

        var trash = GetComponent<TrashItem>();
        if (trash != null) trash.IsDeposited = true;

        var allCols = GetComponentsInChildren<Collider>(true);
        foreach (var c in allCols) c.enabled = false;

        if (_depositCoroutine != null)
        {
            StopCoroutine(_depositCoroutine);
            _depositCoroutine = null;
        }

        if (!Application.isPlaying)
        {
            transform.position = targetDepositWorldPos;
            transform.rotation = targetDepositWorldRot ?? Quaternion.Euler(0f, transform.rotation.eulerAngles.y, 0f);
            transform.localScale = transform.localScale * depositShrinkScale;

            bool keepVis = binManager == null || binManager.KeepDepositedItemsVisible;
            if (keepVis)
            {
                Transform parentTarget = (binManager != null && binManager.DepositPointTransform != null) 
                    ? binManager.DepositPointTransform 
                    : (binManager != null ? binManager.transform : null);
                if (parentTarget != null)
                {
                    transform.SetParent(parentTarget, true);
                }

                if (rb != null)
                {
                    if (!rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }

                foreach (var c in allCols) c.enabled = false;
                gameObject.SetActive(true);
            }
            else
            {
                EnsurePickable();
                gameObject.SetActive(false);
            }
            return;
        }

        if (gameObject.activeInHierarchy)
        {
            _depositCoroutine = StartCoroutine(DepositAnimationRoutine(targetDepositWorldPos, targetDepositWorldRot, binManager));
        }
        else
        {
            bool keepVis = binManager == null || binManager.KeepDepositedItemsVisible;
            if (!keepVis)
            {
                EnsurePickable();
                gameObject.SetActive(false);
            }
        }
    }

    private IEnumerator DepositAnimationRoutine(Vector3 targetDepositWorldPos, Quaternion? targetDepositWorldRot = null, QuestManager binManager = null)
    {
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        Vector3 startScale = transform.localScale;

        Quaternion targetRot = targetDepositWorldRot ?? Quaternion.Euler(0f, startRot.eulerAngles.y, 0f);
        Vector3 targetScale = startScale * depositShrinkScale;

        // Entry waypoint above the opening so the trajectory enters naturally from above and does not clip through bin walls
        Vector3 controlPoint = new Vector3(
            (startPos.x + targetDepositWorldPos.x) * 0.5f,
            Mathf.Max(startPos.y, targetDepositWorldPos.y + 0.35f) + 0.15f,
            (startPos.z + targetDepositWorldPos.z) * 0.5f
        );

        if (rb != null)
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        float duration = Mathf.Max(0.15f, depositDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curveT = depositCurve != null ? depositCurve.Evaluate(t) : t;

            // Quadratic Bezier arc: (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
            float u = 1f - curveT;
            Vector3 currentPos = (u * u * startPos) + (2f * u * curveT * controlPoint) + (curveT * curveT * targetDepositWorldPos);

            transform.position = currentPos;
            transform.rotation = Quaternion.Slerp(startRot, targetRot, curveT);
            transform.localScale = Vector3.Lerp(startScale, targetScale, curveT);

            yield return null;
        }

        transform.position = targetDepositWorldPos;
        transform.rotation = targetRot;
        transform.localScale = targetScale;

        yield return new WaitForSeconds(0.08f);

        bool keepVisible = binManager == null || binManager.KeepDepositedItemsVisible;
        if (keepVisible)
        {
            Transform parentTarget = (binManager != null && binManager.DepositPointTransform != null) 
                ? binManager.DepositPointTransform 
                : (binManager != null ? binManager.transform : null);
            if (parentTarget != null)
            {
                transform.SetParent(parentTarget, true);
            }

            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            var allCols = GetComponentsInChildren<Collider>(true);
            foreach (var c in allCols) c.enabled = false;

            gameObject.SetActive(true);
        }
        else
        {
            EnsurePickable();
            gameObject.SetActive(false);
        }

        _depositCoroutine = null;
    }

    public void OnEjectedFromBin(Transform binTransform)
    {
        if (CurrentlyGrabbedItem == this) CurrentlyGrabbedItem = null;
        isGrabbed = false;
        RestoreOriginalState();
        EnsurePickable(preserveReleasedState: true, logSelfHeal: true);
        SetPlayerCollisionIgnored(false);
        SetState(ItemState.Released, "wrong eject");

        transform.position = binTransform.position + binTransform.forward * 0.75f + Vector3.up * 0.85f;
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce((binTransform.forward * 2.2f + Vector3.up * 2.6f), ForceMode.Impulse);
            rb.WakeUp();
        }

        if (Application.isPlaying && gameObject.activeInHierarchy)
        {
            StartCoroutine(SettleFailsafeRoutine());
        }
    }

    public QuestManager FindOverlappingBin()
    {
        Bounds myBounds = CalculateCombinedBounds(gameObject);
        QuestManager[] allBins = Object.FindObjectsByType<QuestManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var bin in allBins)
        {
            var binCols = bin.GetComponents<Collider>();
            foreach (var bc in binCols)
            {
                if (bc.bounds.Intersects(myBounds))
                {
                    return bin;
                }
            }
        }

        Vector3 center = myBounds.center;
        int binLayerMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "TrashItem");
        if (Physics.Raycast(center, Vector3.down, out RaycastHit hit, 0.75f, binLayerMask, QueryTriggerInteraction.Collide))
        {
            QuestManager bin = hit.collider.GetComponentInParent<QuestManager>();
            if (bin != null) return bin;
        }

        return null;
    }

    private IEnumerator SettleFailsafeRoutine()
    {
        float stillTimer = 0f;
        float elapsed = 0f;
        float quietTarget = settleQuietDuration > 0.01f ? settleQuietDuration : 0.25f;
        float hardCap = (settleTimeout > 0.1f && settleTimeout <= 1.2f) ? settleTimeout : 1.0f;

        while (CurrentItemState == ItemState.Released && rb != null)
        {
            yield return new WaitForSeconds(0.05f);
            if (rb == null || !gameObject.activeInHierarchy || CurrentItemState != ItemState.Released) yield break;

            elapsed += 0.05f;
            bool isSlow = rb.IsSleeping() || (rb.linearVelocity.sqrMagnitude < 0.04f && rb.angularVelocity.sqrMagnitude < 0.25f);
            if (isSlow)
            {
                stillTimer += 0.05f;
                if (rb.IsSleeping() || stillTimer >= quietTarget || elapsed >= hardCap)
                {
                    Bounds wb = GetWorldBounds();
                    Vector3 castOrigin = wb.center + Vector3.up * 0.1f;
                    float castDist = wb.extents.y + 0.45f;
                    int groundMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "TrashItem");
                    if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit gHit, castDist, groundMask, QueryTriggerInteraction.Ignore) || elapsed >= hardCap)
                    {
                        SetState(ItemState.Settled, "settled on ground");
                        EnsurePickable();
                        yield break;
                    }
                    else
                    {
                        rb.isKinematic = false;
                        rb.useGravity = true;
                        rb.WakeUp();
                        rb.AddForce(Vector3.down * 1.5f, ForceMode.VelocityChange);
                        stillTimer = 0f;
                    }
                }
            }
            else
            {
                stillTimer = 0f;
                if (elapsed >= hardCap)
                {
                    SetState(ItemState.Settled, "settled (hard cap)");
                    EnsurePickable();
                    yield break;
                }
            }
        }
    }

    private void CaptureOriginalState()
    {
        _hasOriginalState = true;
        _originalState = new OriginalItemState
        {
            parent = transform.parent,
            localScale = transform.localScale
        };

        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        _originalState.childTransforms = allChildren;
        _originalState.childLayers = new int[allChildren.Length];
        for (int i = 0; i < allChildren.Length; i++)
        {
            _originalState.childLayers[i] = allChildren[i].gameObject.layer;
        }

        Collider[] cols = GetComponentsInChildren<Collider>(true);
        _originalState.colliders = cols;
        _originalState.collidersEnabled = new bool[cols.Length];
        for (int i = 0; i < cols.Length; i++)
        {
            _originalState.collidersEnabled[i] = cols[i] != null && cols[i].enabled;
        }

        if (rb != null)
        {
            _originalState.hasRb = true;
            _originalState.isKinematic = rb.isKinematic;
            _originalState.useGravity = rb.useGravity;
            _originalState.constraints = rb.constraints;
            _originalState.interpolation = rb.interpolation;
            _originalState.collisionDetectionMode = rb.collisionDetectionMode;
            _originalState.linearDamping = rb.linearDamping;
            _originalState.angularDamping = rb.angularDamping;
        }
        else
        {
            _originalState.hasRb = false;
        }
    }

    public void RestoreOriginalState(bool restoreCollidersOnly = false)
    {
        if (!_hasOriginalState) return;

        if (transform.parent == null || transform.parent.gameObject.activeInHierarchy)
            transform.SetParent(_originalState.parent, true);
        transform.localScale = _originalState.localScale;

        if (_originalState.childTransforms != null)
        {
            for (int i = 0; i < _originalState.childTransforms.Length; i++)
            {
                if (_originalState.childTransforms[i] != null)
                {
                    _originalState.childTransforms[i].gameObject.layer = _originalState.childLayers[i];
                }
            }
        }

        if (_originalState.colliders != null)
        {
            for (int i = 0; i < _originalState.colliders.Length; i++)
            {
                if (_originalState.colliders[i] != null)
                {
                    _originalState.colliders[i].enabled = _originalState.collidersEnabled[i];
                }
            }
        }

        if (!restoreCollidersOnly && _originalState.hasRb && rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.constraints = _originalState.constraints;
            rb.interpolation = _originalState.interpolation;
            rb.collisionDetectionMode = _originalState.collisionDetectionMode;
            rb.linearDamping = _originalState.linearDamping;
            rb.angularDamping = _originalState.angularDamping;
            rb.WakeUp();
        }

        _hasOriginalState = false;
    }

    public static Bounds CalculateCombinedBounds(GameObject target)
    {
        if (target == null) return new Bounds(Vector3.zero, Vector3.one * 0.2f);

        Physics.SyncTransforms();

        Bounds combined = new Bounds();
        bool hasBounds = false;

        Renderer[] rends = target.GetComponentsInChildren<Renderer>(false);
        for (int i = 0; i < rends.Length; i++)
        {
            Renderer r = rends[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;

            if (!hasBounds)
            {
                combined = r.bounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(r.bounds);
            }
        }

        Collider[] cols = target.GetComponentsInChildren<Collider>(false);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider c = cols[i];
            if (c == null || !c.enabled || c.isTrigger) continue;

            if (!hasBounds)
            {
                combined = c.bounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(c.bounds);
            }
        }

        if (!hasBounds)
        {
            combined = new Bounds(target.transform.position, Vector3.one * 0.2f);
        }

        Vector3 sz = combined.size;
        bool clamped = false;
        if (sz.x < 0.02f) { sz.x = 0.02f; clamped = true; }
        if (sz.y < 0.02f) { sz.y = 0.02f; clamped = true; }
        if (sz.z < 0.02f) { sz.z = 0.02f; clamped = true; }
        if (clamped) combined.size = sz;

        return combined;
    }

    public static bool TryGetHeldGrabPoints(Transform cam, out Vector3 leftSurface, out Vector3 rightSurface)
    {
        leftSurface = Vector3.zero;
        rightSurface = Vector3.zero;

        GameObject held = null;
        if (CurrentlyGrabbedItem != null && CurrentlyGrabbedItem.isGrabbed)
            held = CurrentlyGrabbedItem.gameObject;
        else if (ItemCarry.ActiveCarriedItem != null)
            held = ItemCarry.ActiveCarriedItem;

        if (held == null) return false;

        // 1. Explicit grab points if present on the item
        Transform gpL = held.transform.Find("GrabPoint_L");
        Transform gpR = held.transform.Find("GrabPoint_R");
        if (gpL != null && gpR != null)
        {
            leftSurface = gpL.position;
            rightSurface = gpR.position;
            return true;
        }

        // 2. Compute camera-space tight bounds from mesh/renderers
        if (cam == null) cam = Camera.main != null ? Camera.main.transform : null;
        if (cam == null) return false;

        bool hasBounds = false;
        Vector3 minCam = Vector3.zero;
        Vector3 maxCam = Vector3.zero;

        MeshFilter[] mfs = held.GetComponentsInChildren<MeshFilter>(false);
        for (int i = 0; i < mfs.Length; i++)
        {
            MeshFilter mf = mfs[i];
            if (mf == null || mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            Vector3 c = mb.center;
            Vector3 e = mb.extents;
            Transform t = mf.transform;

            for (int dx = -1; dx <= 1; dx += 2)
            {
                for (int dy = -1; dy <= 1; dy += 2)
                {
                    for (int dz = -1; dz <= 1; dz += 2)
                    {
                        Vector3 cornerLocal = c + new Vector3(e.x * dx, e.y * dy, e.z * dz);
                        Vector3 cornerWorld = t.TransformPoint(cornerLocal);
                        Vector3 cornerCam = cam.InverseTransformPoint(cornerWorld);

                        if (!hasBounds)
                        {
                            minCam = cornerCam;
                            maxCam = cornerCam;
                            hasBounds = true;
                        }
                        else
                        {
                            minCam = Vector3.Min(minCam, cornerCam);
                            maxCam = Vector3.Max(maxCam, cornerCam);
                        }
                    }
                }
            }
        }

        if (!hasBounds)
        {
            Renderer[] rends = held.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < rends.Length; i++)
            {
                Renderer r = rends[i];
                if (r == null || !r.enabled) continue;
                Bounds b = r.bounds;
                Vector3 c = b.center;
                Vector3 e = b.extents;

                for (int dx = -1; dx <= 1; dx += 2)
                {
                    for (int dy = -1; dy <= 1; dy += 2)
                    {
                        for (int dz = -1; dz <= 1; dz += 2)
                        {
                            Vector3 cornerWorld = c + new Vector3(e.x * dx, e.y * dy, e.z * dz);
                            Vector3 cornerCam = cam.InverseTransformPoint(cornerWorld);

                            if (!hasBounds)
                            {
                                minCam = cornerCam;
                                maxCam = cornerCam;
                                hasBounds = true;
                            }
                            else
                            {
                                minCam = Vector3.Min(minCam, cornerCam);
                                maxCam = Vector3.Max(maxCam, cornerCam);
                            }
                        }
                    }
                }
            }
        }

        if (!hasBounds) return false;

        float midY = (minCam.y + maxCam.y) * 0.5f;
        float midZ = (minCam.z + maxCam.z) * 0.5f;

        Vector3 leftCam = new Vector3(minCam.x, midY, midZ);
        Vector3 rightCam = new Vector3(maxCam.x, midY, midZ);

        leftSurface = cam.TransformPoint(leftCam);
        rightSurface = cam.TransformPoint(rightCam);
        return true;
    }

    private IEnumerator ReenablePlayerCollisionDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (!isGrabbed)
        {
            SetPlayerCollisionIgnored(false);
        }
    }

    private void SetPlayerCollisionIgnored(bool ignore)
    {
        if (myColliders == null || myColliders.Length == 0) myColliders = GetComponentsInChildren<Collider>(true);
        CachePlayerReferences();

        if (myColliders == null || playerColliders == null) return;

        for (int i = 0; i < myColliders.Length; i++)
        {
            Collider myCol = myColliders[i];
            if (myCol == null) continue;
            for (int j = 0; j < playerColliders.Length; j++)
            {
                Collider pCol = playerColliders[j];
                if (pCol != null)
                {
                    Physics.IgnoreCollision(myCol, pCol, ignore);
                }
            }
        }
    }
}