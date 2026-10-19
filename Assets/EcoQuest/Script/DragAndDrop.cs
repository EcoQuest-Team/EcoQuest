using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class DragAndDrop : MonoBehaviour
{
    public static DragAndDrop CurrentlyGrabbedItem { get; private set; }
    public static bool IsAnyItemGrabbed => CurrentlyGrabbedItem != null;

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
    [SerializeField] private float defaultHoldDistance = 1.10f;
    [SerializeField] private float minHoldDistance = 0.65f;
    [SerializeField] private float maxHoldDistance = 1.8f;
    [SerializeField] private Vector3 holdOffsetNormalized = new Vector3(0.28f, -0.22f, 1.10f);

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

    // Crosshair feedback
    private static RectTransform crosshairRect;
    private static Image crosshairImage;
    private static Color crosshairBaseColor = Color.white;
    private static bool crosshairFound = false;

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
    }

    private void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (_holdProfile == null) _holdProfile = GetComponent<HoldProfile>();
        if (_settings == null) _settings = HoldSettings.Instance;
        mainCamera = Camera.main;
        CachePlayerReferences();
        CacheCrosshair();
    }

    private void OnEnable()
    {
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDisable()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (isGrabbed) ForceRelease();
        SetHighlight(false);
        ClearBinHighlight();
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (isGrabbed) ForceRelease();
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

    private static void CacheCrosshair()
    {
        if (crosshairFound && crosshairRect != null) return;
        GameObject crossGo = GameObject.Find("--- UI ---/UI_Crosshair/Crosshair");
        if (crossGo == null) crossGo = GameObject.Find("Crosshair");
        if (crossGo != null)
        {
            crosshairRect = crossGo.GetComponent<RectTransform>();
            crosshairImage = crossGo.GetComponent<Image>();
            if (crosshairImage != null) crosshairBaseColor = crosshairImage.color;
            crosshairFound = true;
        }
    }

    private void Update()
    {
        bool isDialogue = false;
        var npc = FindFirstObjectByType<NPCInteraction>();
        if (npc != null && npc.IsTalking) isDialogue = true;

        if (PauseMenuController.IsPaused || QuizUIController.IsAnyQuizOpen || isDialogue)
        {
            SetHighlight(false);
            ClearBinHighlight();
            return;
        }

        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera != null && holdPoint == null)
        {
            holdPoint = mainCamera.transform.Find("HoldPoint");
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

            // Crosshair carry state
            UpdateCrosshairFeedback(true, false);

            // Drop triggers: click release, G key, or E key
            if (Input.GetMouseButtonDown(0) && Time.time > grabTime + 0.2f)
            {
                ReleaseObject();
            }
            else if (Input.GetMouseButtonUp(0) && Time.time > grabTime + 0.25f)
            {
                ReleaseObject();
            }
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
    }

    private void LateUpdate()
    {
        if (!isGrabbed) return;
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;

        // Calculate hold position relative to camera / HoldPoint
        Vector3 localOffset = holdPoint != null ? holdPoint.localPosition : new Vector3(
            holdOffsetNormalized.x * (currentHoldDistance / defaultHoldDistance),
            holdOffsetNormalized.y * (currentHoldDistance / defaultHoldDistance),
            currentHoldDistance
        );

        // Apply arm weight dip derived from mass
        localOffset.y -= _currentArmDip;

        // Subtle walk sway and bob that matches player movement
        float speed = 0f;
        if (playerCC != null) speed = new Vector2(playerCC.velocity.x, playerCC.velocity.z).magnitude;
        float moveFactor = Mathf.Clamp01(speed / 4.0f);
        if (moveFactor > 0.01f)
        {
            bobTimer += Time.deltaTime * 8f;
        }
        Vector3 bobOffset = new Vector3(
            Mathf.Cos(bobTimer * 0.5f) * 0.018f,
            Mathf.Sin(bobTimer) * 0.014f,
            0f
        ) * moveFactor;

        Vector3 camPos = mainCamera.transform.position;
        Vector3 targetHoldPos = mainCamera.transform.TransformPoint(localOffset + bobOffset);

        // Wall safety spherecast
        Vector3 rayDir = targetHoldPos - camPos;
        float rayDist = rayDir.magnitude;
        if (rayDist > 0.01f)
        {
            rayDir /= rayDist;
            int ignoreMask = (1 << 2); // Ignore Raycast
            int pLayer = LayerMask.NameToLayer("Player");
            if (pLayer != -1) ignoreMask |= (1 << pLayer);
            int tLayer = LayerMask.NameToLayer("TrashItem");
            if (tLayer != -1) ignoreMask |= (1 << tLayer);
            int bsLayer = LayerMask.NameToLayer("BinSolid");
            if (bsLayer != -1) ignoreMask |= (1 << bsLayer);
            int hfLayer = LayerMask.NameToLayer("HiddenFromFirstPerson");
            if (hfLayer != -1) ignoreMask |= (1 << hfLayer);
            int vmLayer = LayerMask.NameToLayer("Viewmodel");
            if (vmLayer != -1) ignoreMask |= (1 << vmLayer);

            if (Physics.SphereCast(camPos, wallSphereCastRadius, rayDir, out RaycastHit hit, rayDist, ~ignoreMask, QueryTriggerInteraction.Ignore))
            {
                float safeDist = Mathf.Clamp(hit.distance - 0.15f, 0.35f, rayDist);
                targetHoldPos = camPos + rayDir * safeDist;
            }
        }

        // Upright camera-yaw orientation with subtle dynamic movement tilts
        Vector3 moveVel = playerCC != null ? playerCC.velocity : Vector3.zero;
        float forwardTilt = Mathf.Clamp(Vector3.Dot(moveVel, mainCamera.transform.forward) * 1.5f, -8f, 8f);
        float sideTilt = Mathf.Clamp(-Vector3.Dot(moveVel, mainCamera.transform.right) * 2.0f, -10f, 10f);

        Quaternion targetRot = Quaternion.Euler(10f - forwardTilt, mainCamera.transform.eulerAngles.y - 14f, 4f + sideTilt);
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
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
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

        bool aimingAtMe = false;
        Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, pickupRange))
        {
            if (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform))
            {
                aimingAtMe = true;
            }
        }

        if (aimingAtMe != isHovered)
        {
            SetHighlight(aimingAtMe);
        }

        if (aimingAtMe)
        {
            UpdateCrosshairFeedback(false, true);
        }
        else if (CurrentlyGrabbedItem == null)
        {
            UpdateCrosshairFeedback(false, false);
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

    private static void UpdateCrosshairFeedback(bool holding, bool aimingGrabbable)
    {
        CacheCrosshair();
        if (crosshairRect == null) return;

        if (holding)
        {
            crosshairRect.localScale = Vector3.Lerp(crosshairRect.localScale, new Vector3(0.85f, 0.85f, 1f), Time.deltaTime * 15f);
            if (crosshairImage != null) crosshairImage.color = new Color(crosshairBaseColor.r, crosshairBaseColor.g, crosshairBaseColor.b, 0.65f);
        }
        else if (aimingGrabbable)
        {
            crosshairRect.localScale = Vector3.Lerp(crosshairRect.localScale, new Vector3(1.25f, 1.25f, 1f), Time.deltaTime * 15f);
            if (crosshairImage != null) crosshairImage.color = new Color(0.4f, 1.0f, 0.4f, 0.95f);
        }
        else
        {
            crosshairRect.localScale = Vector3.Lerp(crosshairRect.localScale, Vector3.one, Time.deltaTime * 15f);
            if (crosshairImage != null) crosshairImage.color = crosshairBaseColor;
        }
    }

    public void TryGrabObject()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;

        Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (Physics.Raycast(ray, out RaycastHit hit, pickupRange))
        {
            if (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform))
            {
                GrabObject();
            }
        }
    }

    public void GrabObject()
    {
        if (isGrabbed) return;
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

        // 1. Capture exact original state before any modifications
        CaptureOriginalState();

        isGrabbed = true;
        CurrentlyGrabbedItem = this;
        grabTime = Time.time;
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

        // Size fitting: downscale oversized items (> 0.9m)
        float fitScale = 1.0f;
        if (longestSide > _settings.largeMaxDistance)
        {
            fitScale = Mathf.Clamp(_settings.largeMaxDistance / longestSide, _settings.minUniformScaleDown, 1.0f);
        }
        _heldScale = _originalState.localScale * fitScale;

        // Grip point: lower-middle (30% to 40% up from bottom, centered)
        float gripY = _currentCombinedBounds.min.y + _currentCombinedBounds.size.y * _settings.gripHeightFraction;
        Vector3 gripWorld = new Vector3(_currentCombinedBounds.center.x, gripY, _currentCombinedBounds.center.z);
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

        // Rigidbody configuration for held state
        if (rb != null)
        {
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        SetHighlight(false);
        SetPlayerCollisionIgnored(true);

        // Start smooth ease coroutine with subtle scale pop
        _easeCoroutine = StartCoroutine(GrabEaseRoutine());

        // Notify animation driver & viewmodel
        var animDriver = FindAnyObjectByType<RangerAnimationDriver>();
        animDriver?.SetCarrying(true);
        animDriver?.PlayPickUp();
        ViewmodelBob.SetHoldingTrash(true);

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

    public void ReleaseObject()
    {
        if (!isGrabbed) return;
        isGrabbed = false;
        if (CurrentlyGrabbedItem == this) CurrentlyGrabbedItem = null;

        if (_easeCoroutine != null)
        {
            StopCoroutine(_easeCoroutine);
            _easeCoroutine = null;
        }

        // Restore exact original state
        RestoreOriginalState();

        ClearBinHighlight();

        var animDriver = FindAnyObjectByType<RangerAnimationDriver>();
        animDriver?.SetCarrying(false);
        animDriver?.PlayDrop();
        ViewmodelBob.SetHoldingTrash(false);

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

        if (gameObject.activeInHierarchy)
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
        ReleaseObject();
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

    private void RestoreOriginalState()
    {
        if (!_hasOriginalState) return;

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

        if (_originalState.hasRb && rb != null)
        {
            rb.isKinematic = _originalState.isKinematic;
            rb.useGravity = _originalState.useGravity;
            rb.constraints = _originalState.constraints;
            rb.interpolation = _originalState.interpolation;
            rb.collisionDetectionMode = _originalState.collisionDetectionMode;
            rb.linearDamping = _originalState.linearDamping;
            rb.angularDamping = _originalState.angularDamping;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
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