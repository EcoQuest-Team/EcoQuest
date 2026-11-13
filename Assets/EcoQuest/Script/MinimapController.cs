using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MinimapController : MonoBehaviour
{
    public static MinimapController Instance { get; private set; }

    public static float CurrentCameraWorldY { get; private set; } = 60f;
    public static float CurrentIconScaleMultiplier { get; private set; } = 1f;
    public static bool AreTrashIconsActive { get; set; } = false;
    public static bool IsMapTutorialUnlocked { get; set; } = false;
    public static bool IsMapStepActive { get; set; } = false;

    public static bool IsMapOpenOrTutorialCursorUnlocked =>
        (Instance != null && (Instance.isFullMapOpen || Instance.transitionProgress > 0.001f)) || IsMapStepActive;

    [Header("Minimap (HUD) Settings")]
    [Tooltip("Fixed, tighter orthographic zoom size for HUD minimap")]
    public float minimapOrthoSize = 20f;
    public Transform player;
    public float cameraHeight = 60f;

    [Header("Full Map Auto-Fit & Bounds")]
    public float fullMapCameraHeight = 350f;
    [Tooltip("Padding ratio added to city extents for auto-fit (0.08 = 8%)")]
    public float autoFitPaddingRatio = 0.08f;
    [Tooltip("World padding added beyond city bounds for pan clamping")]
    public float mapPanPadding = 40f;
    public float transitionDuration = 0.28f;
    public Vector2 fullMapDisplaySize = new Vector2(740f, 740f);
    public Vector2 fullMapBorderPadding = new Vector2(18f, 18f);

    [Header("Full Map Zoom & Pan Controls")]
    [Tooltip("Minimum orthographic size (closest zoom)")]
    public float minZoom = 25f;
    [Tooltip("Maximum orthographic size (furthest zoom)")]
    public float maxZoom = 450f;
    [Tooltip("Step factor per zoom button click (e.g. 1.25)")]
    public float zoomStepFactor = 1.25f;
    [Tooltip("Zoom sensitivity for mouse wheel")]
    public float zoomSensitivity = 18f;
    [Tooltip("Pan movement smoothing speed")]
    public float panSmoothing = 12f;
    [Tooltip("Zoom smoothing speed")]
    public float zoomSmoothing = 10f;

    [Header("Icon Settings")]
    public float minimapIconSize = 36f;
    public float fullMapIconSize = 54f;

    [Header("Default Icon Sprites")]
    public Sprite playerIconSprite;
    public Sprite npcIconSprite;
    public Sprite trashIconSprite;

    private Camera minimapCam;
    private Camera fullMapCam;
    private UniversalAdditionalCameraData uac;
    private UniversalAdditionalCameraData fullMapUac;
    private RenderTexture minimapRenderTexture;
    private RenderTexture fullMapRenderTexture;

    private Canvas minimapCanvas;
    private RectTransform minimapCanvasRect;
    private RectTransform borderRect;
    private RectTransform displayRect;
    private RawImage displayRawImage;
    private Image fullMapBackdropImage;
    private RectTransform mapIconsContainer;
    private RectTransform mapControlsRoot;

    // Saved original minimap UI layout
    private Vector2 origBorderAnchoredPos;
    private Vector2 origBorderSizeDelta;
    private Vector2 origDisplayAnchoredPos;
    private Vector2 origDisplaySizeDelta;
    private float origOrthoSize = 20f;
    private bool origCanvasOverrideSorting = false;
    private int origCanvasSortingOrder = 0;

    // Calculated city bounds & dynamic pan/zoom state
    private Bounds cityBounds;
    private float autoFitOrthoSize = 155f;
    private Vector3 fullMapCenter = new Vector3(-35.45f, 0f, -1.09f);
    private float fullMapOrthoSize = 155f;
    private Vector3 targetCamPos = new Vector3(-35.45f, 180f, -1.09f);
    private Vector3 currentCamPos = new Vector3(-35.45f, 180f, -1.09f);
    private float targetOrthoSize = 155f;
    private float currentOrthoSize = 155f;

    // Pan & Zoom input tracking
    private bool isPointerDownOnMap = false;
    private bool isDraggingMap = false;
    private Vector2 pointerDownPos;
    private Vector2 lastMousePos;
    private float lastClickTime = -10f;
    private const float DoubleClickWindow = 0.35f;
    private const float DragPixelThreshold = 5f;

    // Map open/close state
    private bool isFullMapOpen = false;
    private float transitionProgress = 0f; // 0 = minimap, 1 = full map
    private float openedAtUnscaledTime = -10f;
    private bool hasOpenedMapOnce = false;
    private bool hasCompletedMapCycle = false;

    // Fog & Camera Rendering management
    private bool wasFogEnabled = false;
    private bool isRenderingMinimapCam = false;
    private float savedLodBias = 1f;
    private int lastZoomButtonClickFrame = -1;
    private bool isMinimapViewActive = false;
    private bool forceNextMinimapRender = true;
    private int framesSinceMinimapRender = 0;
    private Vector3 lastMinimapRenderPos;
    private float lastMinimapRenderYaw = float.NaN;

    // Saved cursor & player control state prior to opening full map
    private CursorLockMode savedCursorLockMode = CursorLockMode.Locked;
    private bool savedCursorVisible = false;
    private bool savedFpcEnabled = true;
    private bool savedInputsCursorLocked = true;
    private bool savedInputsCursorLook = true;
    private bool savedWasHoldingCtrl = false;
    private bool hasSavedControlState = false;

    private StarterAssets.FirstPersonController fpc;
    private StarterAssets.StarterAssetsInputs playerInputs;

    // Tutorial highlight elements
    private Canvas tutorialTintCanvas;
    private CanvasGroup tutorialTintCanvasGroup;
    private RectTransform tutorialGlowRect;
    private CanvasGroup tutorialGlowCanvasGroup;
    private RectTransform tutorialArrowRect;
    private CanvasGroup tutorialArrowCanvasGroup;
    private Vector2 arrowBaseAnchoredPos;
    private float tutorialHighlightAlpha = 0f;
    private Coroutine tutorialHighlightRoutine;

    // Registered MinimapIcon list & UI element cache
    private static readonly List<MinimapIcon> registeredIcons = new List<MinimapIcon>();
    private readonly Dictionary<MinimapIcon, RectTransform> iconUIRects = new Dictionary<MinimapIcon, RectTransform>();
    private readonly Dictionary<MinimapIcon, Image> iconUIImages = new Dictionary<MinimapIcon, Image>();
    private readonly List<VisibleIconInfo> visibleIcons = new List<VisibleIconInfo>(32);
    private NPCInteraction[] cachedNpcInteractions;
    private TutorialManager cachedTutorialManager;
    private int cachedSceneHandle = int.MinValue;

    public bool IsFullMapOpen => isFullMapOpen;
    public bool HasOpenedMapOnce => hasOpenedMapOnce;
    public bool HasCompletedMapCycle => hasCompletedMapCycle;
    public Camera ActiveMapCamera => (isFullMapOpen || transitionProgress > 0.001f) ? (fullMapCam != null ? fullMapCam : minimapCam) : minimapCam;
    public RectTransform MapIconsContainer => mapIconsContainer;
    public float MinimapIconSize => minimapIconSize;
    public float FullMapIconSize => fullMapIconSize;
    public float TransitionProgress => transitionProgress;

    public static void RegisterIcon(MinimapIcon icon)
    {
        if (icon == null) return;
        if (icon.type == MinimapIcon.IconType.Trash && icon.GetComponent<TrashItem>() == null) return;
        if (!registeredIcons.Contains(icon))
        {
            registeredIcons.Add(icon);
        }
    }

    public static void UnregisterIcon(MinimapIcon icon)
    {
        if (icon == null) return;
        registeredIcons.Remove(icon);
        if (Instance != null)
        {
            Instance.RemoveIconUI(icon);
        }
    }

    public static Sprite GetDefaultSprite(MinimapIcon.IconType type)
    {
        if (Instance != null)
        {
            if (type == MinimapIcon.IconType.Player && Instance.playerIconSprite != null) return Instance.playerIconSprite;
            if (type == MinimapIcon.IconType.NPC && Instance.npcIconSprite != null) return Instance.npcIconSprite;
            if (type == MinimapIcon.IconType.Trash && Instance.trashIconSprite != null) return Instance.trashIconSprite;
        }

        string fnameNoExt = (type == MinimapIcon.IconType.Player) ? "map_person_pin_tan"
            : (type == MinimapIcon.IconType.NPC) ? "map_npc_guardian_elder"
            : "map_trash_pin_alert";

        Sprite resSprite = Resources.Load<Sprite>($"MapIcons/{fnameNoExt}");
        if (resSprite != null) return resSprite;

#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/EcoQuest/Sprites/MapIcons/{fnameNoExt}.png");
#else
        return null;
#endif
    }

    public void NotifyTrashIconsActiveChanged()
    {
        // Forces update on next LateUpdate
    }

    private void Awake()
    {
        Instance = this;
        AreTrashIconsActive = false;
        IsMapTutorialUnlocked = false;
        IsMapStepActive = false;
        CurrentCameraWorldY = cameraHeight;
        CurrentIconScaleMultiplier = 1f;

        EnsureSpritesAssigned();
    }

    private void EnsureSpritesAssigned()
    {
#if UNITY_EDITOR
        if (playerIconSprite == null)
            playerIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/EcoQuest/Sprites/MapIcons/map_person_pin_tan.png");
        if (npcIconSprite == null)
            npcIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/EcoQuest/Sprites/MapIcons/map_npc_guardian_elder.png");
        if (trashIconSprite == null)
            trashIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/EcoQuest/Sprites/MapIcons/map_trash_pin_alert.png");
#endif
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        if (isFullMapOpen)
        {
            QualitySettings.lodBias = savedLodBias;
        }
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == minimapCam || camera == fullMapCam)
        {
            isRenderingMinimapCam = true;
            wasFogEnabled = RenderSettings.fog;
            RenderSettings.fog = false;
        }
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if ((camera == minimapCam || camera == fullMapCam) && isRenderingMinimapCam)
        {
            RenderSettings.fog = wasFogEnabled;
            isRenderingMinimapCam = false;
        }
    }

    private void Start()
    {
        minimapCam = GetComponent<Camera>();
        if (minimapCam != null)
        {
            origOrthoSize = minimapOrthoSize;
            minimapCam.orthographic = true;
            minimapCam.orthographicSize = minimapOrthoSize;
            minimapCam.clearFlags = CameraClearFlags.SolidColor;
            minimapCam.backgroundColor = new Color(0.18f, 0.28f, 0.18f, 1f);
            minimapCam.allowMSAA = false;
            minimapCam.useOcclusionCulling = false;
            minimapCam.farClipPlane = Mathf.Max(250f, cameraHeight + 150f);
            minimapCam.cullingMask = (1 << 0) | (1 << 4) | (1 << 6); // Default, Water, MinimapOnly

            uac = minimapCam.GetComponent<UniversalAdditionalCameraData>();
            ConfigureMapCameraData(uac);

            minimapCam.enabled = false;
            isMinimapViewActive = true;
            forceNextMinimapRender = true;

            minimapRenderTexture = minimapCam.targetTexture;
            if (minimapRenderTexture != null)
            {
                minimapRenderTexture.filterMode = FilterMode.Bilinear;
            }
        }

        // Clean up any obsolete legacy player marker child on MinimapCamera
        Transform legacyMark = transform.Find("Player Mark");
        if (legacyMark != null)
        {
            if (Application.isPlaying) Destroy(legacyMark.gameObject);
            else DestroyImmediate(legacyMark.gameObject);
        }

        if (player == null)
        {
            var pGo = GameObject.FindGameObjectWithTag("Player");
            if (pGo != null) player = pGo.transform;
        }

        EnsurePlayerMinimapMarker();
        ResolveUIReferences();
        CreateFullMapRenderTexture();
        EnsureFullMapCamera();
        CalculatePlayAreaBounds();
    }

    private void EnsureFullMapCamera()
    {
        if (fullMapCam == null)
        {
            Transform parent = transform.parent;
            Transform fullCamTr = parent != null ? parent.Find("FullMapCamera") : null;
            if (fullCamTr == null)
            {
                GameObject go = new GameObject("FullMapCamera");
                if (parent != null) go.transform.SetParent(parent, false);
                fullMapCam = go.AddComponent<Camera>();
                fullMapUac = go.AddComponent<UniversalAdditionalCameraData>();
            }
            else
            {
                fullMapCam = fullCamTr.GetComponent<Camera>();
                fullMapUac = fullCamTr.GetComponent<UniversalAdditionalCameraData>();
            }
        }

        if (fullMapCam != null)
        {
            if (minimapCam != null)
            {
                fullMapCam.CopyFrom(minimapCam);
            }
            fullMapCam.orthographic = true;
            fullMapCam.orthographicSize = currentOrthoSize > 0.001f ? currentOrthoSize : autoFitOrthoSize;
            fullMapCam.nearClipPlane = 0.1f;
            fullMapCam.farClipPlane = Mathf.Max(500f, fullMapCameraHeight + 150f);
            fullMapCam.allowMSAA = false;
            fullMapCam.useOcclusionCulling = false;
            fullMapCam.layerCullDistances = new float[32];
            fullMapCam.clearFlags = CameraClearFlags.SolidColor;
            fullMapCam.backgroundColor = new Color(0.12f, 0.15f, 0.13f, 1f);

            int uiLayer = LayerMask.NameToLayer("UI");
            int vmLayer = LayerMask.NameToLayer("Viewmodel");
            int mask = ~0;
            if (uiLayer >= 0) mask &= ~(1 << uiLayer);
            if (vmLayer >= 0) mask &= ~(1 << vmLayer);
            fullMapCam.cullingMask = mask;

            CreateFullMapRenderTexture();
            fullMapCam.targetTexture = fullMapRenderTexture;
            fullMapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            fullMapCam.transform.position = new Vector3(currentCamPos.x, fullMapCameraHeight, currentCamPos.z);

            ConfigureMapCameraData(fullMapUac);

            fullMapCam.enabled = false;
        }
    }

    private static void ConfigureMapCameraData(UniversalAdditionalCameraData camData)
    {
        if (camData == null) return;
        camData.SetRenderer(1);
        camData.renderPostProcessing = false;
        camData.volumeLayerMask = 0; // Nothing
        camData.renderShadows = false;
        camData.antialiasing = AntialiasingMode.None;
        camData.requiresColorOption = CameraOverrideOption.Off;
        camData.requiresDepthOption = CameraOverrideOption.Off;
    }


    private void OnDestroy()
    {
        if (isFullMapOpen)
        {
            QualitySettings.lodBias = savedLodBias;
        }

        if (fullMapRenderTexture != null)
        {
            if (fullMapCam != null && fullMapCam.targetTexture == fullMapRenderTexture)
            {
                fullMapCam.targetTexture = null;
            }
            if (displayRawImage != null && displayRawImage.texture == fullMapRenderTexture)
            {
                displayRawImage.texture = minimapRenderTexture;
            }
            fullMapRenderTexture.Release();
            Destroy(fullMapRenderTexture);
            fullMapRenderTexture = null;
        }

        if (Instance == this)
        {
            IsMapStepActive = false;
            Instance = null;
        }
    }

    private void EnsurePlayerMinimapMarker()
    {
        if (player == null) return;
        MinimapIcon playerIcon = player.GetComponent<MinimapIcon>();
        if (playerIcon == null)
        {
            playerIcon = player.gameObject.AddComponent<MinimapIcon>();
            playerIcon.type = MinimapIcon.IconType.Player;
        }
        else
        {
            playerIcon.type = MinimapIcon.IconType.Player;
            playerIcon.CleanLegacy3DQuads();
        }
    }

    private void ResolveUIReferences()
    {
        GameObject uiMinimapGo = GameObject.Find("UI_Minimap");
        if (uiMinimapGo == null) return;

        minimapCanvas = uiMinimapGo.GetComponent<Canvas>();
        minimapCanvasRect = uiMinimapGo.GetComponent<RectTransform>();
        if (minimapCanvas != null)
        {
            origCanvasOverrideSorting = minimapCanvas.overrideSorting;
            origCanvasSortingOrder = minimapCanvas.sortingOrder;
            minimapCanvas.overrideSorting = true;
            minimapCanvas.sortingOrder = 30; // Above default HUD elements
        }

        Transform imgTr = uiMinimapGo.transform.Find("Image");
        Transform dispTr = uiMinimapGo.transform.Find("Minimap Display");

        if (imgTr != null)
        {
            borderRect = imgTr as RectTransform;
            origBorderAnchoredPos = borderRect.anchoredPosition;
            origBorderSizeDelta = borderRect.sizeDelta;
            if (imgTr.GetComponent<MinimapClickForwarder>() == null)
            {
                imgTr.gameObject.AddComponent<MinimapClickForwarder>().controller = this;
            }
        }

        if (dispTr != null)
        {
            displayRect = dispTr as RectTransform;
            origDisplayAnchoredPos = displayRect.anchoredPosition;
            origDisplaySizeDelta = displayRect.sizeDelta;
            displayRawImage = dispTr.GetComponent<RawImage>();
            if (displayRawImage != null)
            {
                displayRawImage.color = Color.white;
                displayRawImage.raycastTarget = true;
                if (minimapRenderTexture == null)
                {
                    minimapRenderTexture = displayRawImage.texture as RenderTexture;
                }
            }

            // Ensure RectMask2D clips icons cleanly at map boundaries
            if (dispTr.GetComponent<RectMask2D>() == null)
            {
                dispTr.gameObject.AddComponent<RectMask2D>();
            }

            // Create or resolve MapIconsContainer
            Transform existingContainer = dispTr.Find("MapIconsContainer");
            if (existingContainer == null)
            {
                GameObject containerGo = new GameObject("MapIconsContainer", typeof(RectTransform));
                containerGo.transform.SetParent(dispTr, false);
                mapIconsContainer = containerGo.GetComponent<RectTransform>();
                mapIconsContainer.anchorMin = Vector2.zero;
                mapIconsContainer.anchorMax = Vector2.one;
                mapIconsContainer.offsetMin = Vector2.zero;
                mapIconsContainer.offsetMax = Vector2.zero;
            }
            else
            {
                mapIconsContainer = existingContainer as RectTransform;
            }

            if (dispTr.GetComponent<MinimapClickForwarder>() == null)
            {
                dispTr.gameObject.AddComponent<MinimapClickForwarder>().controller = this;
            }
        }

        // Create soft backdrop behind the expanded full map on UI_Minimap
        Transform existingBackdrop = uiMinimapGo.transform.Find("FullMapBackdrop");
        if (existingBackdrop == null)
        {
            GameObject backdropGo = new GameObject("FullMapBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            backdropGo.transform.SetParent(uiMinimapGo.transform, false);
            backdropGo.transform.SetAsFirstSibling();
            RectTransform rt = backdropGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            fullMapBackdropImage = backdropGo.GetComponent<Image>();
            fullMapBackdropImage.color = new Color(0f, 0f, 0f, 0f);
            fullMapBackdropImage.raycastTarget = true;
            var fwd = backdropGo.AddComponent<MinimapClickForwarder>();
            fwd.controller = this;
            fwd.isBackdrop = true;
            backdropGo.SetActive(false);
        }
        else
        {
            fullMapBackdropImage = existingBackdrop.GetComponent<Image>();
            var fwd = existingBackdrop.GetComponent<MinimapClickForwarder>();
            if (fwd == null) fwd = existingBackdrop.gameObject.AddComponent<MinimapClickForwarder>();
            fwd.controller = this;
            fwd.isBackdrop = true;
        }

        EnsureMapControlsUI();
        EnsureTutorialHighlightObjects(uiMinimapGo);
    }

    private void EnsureMapControlsUI()
    {
        if (displayRect == null) return;

        Transform existing = displayRect.Find("FullMapControlsPanel");
        GameObject panelGo;
        if (existing != null)
        {
            panelGo = existing.gameObject;
            mapControlsRoot = existing as RectTransform;
        }
        else
        {
            panelGo = new GameObject("FullMapControlsPanel", typeof(RectTransform));
            panelGo.transform.SetParent(displayRect, false);
            mapControlsRoot = panelGo.GetComponent<RectTransform>();
            mapControlsRoot.anchorMin = new Vector2(1f, 0.5f);
            mapControlsRoot.anchorMax = new Vector2(1f, 0.5f);
            mapControlsRoot.pivot = new Vector2(1f, 0.5f);
            mapControlsRoot.anchoredPosition = new Vector2(-16f, 0f);
            mapControlsRoot.sizeDelta = new Vector2(44f, 190f);

            var vlg = panelGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        SetupControlButton(panelGo.transform, "Btn_ZoomIn", "+", () => OnZoomButtonClick(-1));
        SetupControlButton(panelGo.transform, "Btn_ZoomOut", "-", () => OnZoomButtonClick(1));
        SetupControlButton(panelGo.transform, "Btn_Recenter", "O", RecenterMap);
        SetupControlButton(panelGo.transform, "Btn_Close", "X", CloseFullMap);

        panelGo.SetActive(false);
    }

    private void SetupControlButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
    {
        Transform child = parent.Find(name);
        GameObject btnGo;
        if (child != null)
        {
            btnGo = child.gameObject;
        }
        else
        {
            btnGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);

            RectTransform rt = btnGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(38f, 38f);

            var layout = btnGo.AddComponent<LayoutElement>();
            layout.minWidth = 38f;
            layout.minHeight = 38f;
            layout.preferredWidth = 38f;
            layout.preferredHeight = 38f;

            Image img = btnGo.GetComponent<Image>();
            img.color = new Color(0.12f, 0.16f, 0.14f, 0.88f);
            img.raycastTarget = true;

            Button btn = btnGo.GetComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.12f, 0.16f, 0.14f, 0.88f);
            cb.highlightedColor = new Color(0.24f, 0.32f, 0.26f, 0.95f);
            cb.pressedColor = new Color(0.08f, 0.10f, 0.09f, 1f);
            btn.colors = cb;

            GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
            textGo.transform.SetParent(btnGo.transform, false);
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var tmp = textGo.GetComponent<TMPro.TextMeshProUGUI>();
            tmp.text = label;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.fontSize = 20f;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
        }

        Button buttonComp = btnGo.GetComponent<Button>();
        if (buttonComp != null)
        {
            buttonComp.onClick.RemoveAllListeners();
            buttonComp.onClick.AddListener(onClick);
        }
    }

    private void OnZoomButtonClick(int direction)
    {
        if (Time.frameCount == lastZoomButtonClickFrame) return; // Prevent double-fire in same frame
        lastZoomButtonClickFrame = Time.frameCount;

        if (direction < 0)
        {
            ApplyZoomTarget(targetOrthoSize / zoomStepFactor);
        }
        else
        {
            ApplyZoomTarget(targetOrthoSize * zoomStepFactor);
        }
    }

    private void CreateFullMapRenderTexture()
    {
        if (fullMapRenderTexture != null) return;
        fullMapRenderTexture = new RenderTexture(1024, 1024, 16, RenderTextureFormat.ARGB32);
        fullMapRenderTexture.name = "FullMapRuntimeRT";
        fullMapRenderTexture.antiAliasing = 1;
        fullMapRenderTexture.filterMode = FilterMode.Bilinear;
        fullMapRenderTexture.wrapMode = TextureWrapMode.Clamp;
        fullMapRenderTexture.useMipMap = false;
        fullMapRenderTexture.autoGenerateMips = false;
        fullMapRenderTexture.Create();
    }

    public void CalculatePlayAreaBounds()
    {
        Bounds bounds = new Bounds();
        bool hasBounds = false;

        void Encapsulate(Bounds b)
        {
            if (!hasBounds) { bounds = b; hasBounds = true; }
            else bounds.Encapsulate(b);
        }

        GameObject bldRoot = GameObject.Find("Buildings");
        GameObject roadRoot = GameObject.Find("Road and Ground");

        if (bldRoot != null)
        {
            foreach (var r in bldRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                Encapsulate(r.bounds);
            }
        }

        if (roadRoot != null)
        {
            foreach (var r in roadRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                Encapsulate(r.bounds);
            }
        }

        if (!hasBounds)
        {
            var allRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            foreach (var r in allRenderers)
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                int l = r.gameObject.layer;
                string lName = LayerMask.LayerToName(l);
                if (lName == "UI" || lName == "Viewmodel" || lName == "HiddenFromFirstPerson") continue;
                if (r.gameObject.name.StartsWith("UIMapIcon_")) continue;
                Encapsulate(r.bounds);
            }
        }

        if (!hasBounds)
        {
            bounds = new Bounds(new Vector3(-41.20f, 31.91f, -7.70f), new Vector3(550f, 70f, 480f));
        }

        cityBounds = bounds;
        fullMapCenter = new Vector3(bounds.center.x, 0f, bounds.center.z);

        float aspect = GetMapAspect();
        autoFitOrthoSize = Mathf.Max(bounds.extents.z, bounds.extents.x / aspect) * (1f + autoFitPaddingRatio);
        fullMapOrthoSize = autoFitOrthoSize;
        maxZoom = Mathf.Max(maxZoom, autoFitOrthoSize * 1.35f);
    }

    public float GetMapAspect()
    {
        if (displayRect != null && displayRect.rect.height > 1f)
        {
            return displayRect.rect.width / displayRect.rect.height;
        }
        return 1.0f;
    }

    public bool CanToggleMap()
    {
        if (LitterbugQuestManager.IsDialogueOpen)
            return false;

        if (SceneTransition.Instance != null && SceneTransition.Instance.IsTransitioning)
            return false;

        if (QuizUIController.IsAnyQuizOpen)
            return false;

        if (PauseMenuController.IsPaused)
            return false;
        if (PauseMenuController.Instance != null && PauseMenuController.Instance.settingsPanelObject != null &&
            PauseMenuController.Instance.settingsPanelObject.activeInHierarchy)
            return false;

        if (IntroSequenceController.Instance != null)
        {
            var state = IntroSequenceController.Instance.CurrentState;
            if (state == IntroSequenceController.IntroState.Dialogue ||
                state == IntroSequenceController.IntroState.BinIntro)
            {
                return false;
            }
        }

        int activeSceneHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (cachedSceneHandle != activeSceneHandle)
        {
            cachedSceneHandle = activeSceneHandle;
            cachedNpcInteractions = FindObjectsByType<NPCInteraction>(FindObjectsSortMode.None);
            cachedTutorialManager = FindFirstObjectByType<TutorialManager>();
        }

        if (cachedNpcInteractions != null)
        {
            for (int i = 0; i < cachedNpcInteractions.Length; i++)
            {
                if (cachedNpcInteractions[i] != null && cachedNpcInteractions[i].IsTalking)
                    return false;
            }
        }

        if (cachedTutorialManager != null && cachedTutorialManager.tutorialOverlay != null && cachedTutorialManager.tutorialOverlay.activeInHierarchy)
            return false;

        if (SequentialWASDTutorial.Instance != null && !SequentialWASDTutorial.Instance.IsCompleted && !IsMapTutorialUnlocked)
        {
            return false;
        }

        return true;
    }

    private void Update()
    {
        if (isFullMapOpen && !CanToggleMap())
        {
            ForceCloseMapImmediate();
            return;
        }

        bool mPressed = Input.GetKeyDown(KeyCode.M);

        if (!isFullMapOpen)
        {
            if (CanToggleMap())
            {
                if (mPressed || WasMinimapClickedThisFrame())
                {
                    OpenFullMap();
                }
            }
        }
        else
        {
            HandleFullMapInput();
        }

        UpdateMapTransition();
        UpdateTutorialHighlightVisuals();
    }

    private void HandleFullMapInput()
    {
        bool mPressed = Input.GetKeyDown(KeyCode.M);
        bool escPressed = PauseMenuController.Instance == null && Input.GetKeyDown(KeyCode.Escape);

        if (mPressed || escPressed)
        {
            CloseFullMap();
            return;
        }

        // Click outside map on backdrop closes full map
        if (Input.GetMouseButtonDown(0))
        {
            bool insideMap = displayRect != null && RectTransformUtility.RectangleContainsScreenPoint(displayRect, Input.mousePosition, null);
            if (!insideMap && (Time.unscaledTime - openedAtUnscaledTime > 0.2f))
            {
                CloseFullMap();
                return;
            }
        }

        // Mouse wheel zoom toward cursor
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            bool mouseOverMap = displayRect != null && RectTransformUtility.RectangleContainsScreenPoint(displayRect, Input.mousePosition, null);
            if (mouseOverMap)
            {
                float factor = Mathf.Pow(zoomStepFactor, -scroll * 5f);
                ApplyZoomTarget(targetOrthoSize * factor, Input.mousePosition);
            }
        }

        // Key zoom (+ and -) & Recenter (R)
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            OnZoomButtonClick(-1);
        }
        else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            OnZoomButtonClick(1);
        }
        else if (Input.GetKeyDown(KeyCode.R))
        {
            RecenterMap();
        }

        // Mobile pinch zoom
        if (Input.touchCount == 2)
        {
            Touch t0 = Input.GetTouch(0);
            Touch t1 = Input.GetTouch(1);
            Vector2 prevPos0 = t0.position - t0.deltaPosition;
            Vector2 prevPos1 = t1.position - t1.deltaPosition;
            float prevDist = (prevPos0 - prevPos1).magnitude;
            float curDist = (t0.position - t1.position).magnitude;
            if (curDist > 10f && prevDist > 10f)
            {
                Vector2 midPoint = (t0.position + t1.position) * 0.5f;
                float pinchFactor = prevDist / curDist;
                ApplyZoomTarget(targetOrthoSize * pinchFactor, midPoint);
            }
        }

        // Drag to pan & double-click
        if (Input.GetMouseButtonDown(0))
        {
            if (displayRect != null && RectTransformUtility.RectangleContainsScreenPoint(displayRect, Input.mousePosition, null))
            {
                isPointerDownOnMap = true;
                isDraggingMap = false;
                pointerDownPos = Input.mousePosition;
                lastMousePos = Input.mousePosition;
            }
        }
        else if (Input.GetMouseButton(0) && isPointerDownOnMap)
        {
            Vector2 curMouse = Input.mousePosition;
            Vector2 delta = curMouse - lastMousePos;
            if (!isDraggingMap && (curMouse - pointerDownPos).sqrMagnitude > DragPixelThreshold * DragPixelThreshold)
            {
                isDraggingMap = true;
            }

            if (isDraggingMap && delta.sqrMagnitude > 0.001f)
            {
                float aspect = GetMapAspect();
                float w = displayRect.rect.width;
                float h = displayRect.rect.height;
                if (w > 1f && h > 1f)
                {
                    float worldDeltaX = -(delta.x / w) * (2f * targetOrthoSize * aspect);
                    float worldDeltaZ = -(delta.y / h) * (2f * targetOrthoSize);
                    targetCamPos.x += worldDeltaX;
                    targetCamPos.z += worldDeltaZ;
                    ClampTargetCamPos();
                }
            }
            lastMousePos = curMouse;
        }
        else if (Input.GetMouseButtonUp(0) && isPointerDownOnMap)
        {
            isPointerDownOnMap = false;
            if (!isDraggingMap)
            {
                if (Time.unscaledTime - lastClickTime < DoubleClickWindow)
                {
                    RecenterMap();
                    lastClickTime = -10f;
                }
                else
                {
                    lastClickTime = Time.unscaledTime;
                }
            }
            isDraggingMap = false;
        }
    }

    public void ApplyZoomTarget(float newSize, Vector2? screenPivot = null)
    {
        if (float.IsNaN(newSize) || float.IsInfinity(newSize) || newSize <= 0.001f)
        {
            newSize = autoFitOrthoSize;
        }

        float oldTargetSize = targetOrthoSize;
        float clampedSize = Mathf.Clamp(newSize, minZoom, maxZoom);

        if (screenPivot.HasValue && displayRect != null && oldTargetSize > 0.001f)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(displayRect, screenPivot.Value, null, out Vector2 localPt))
            {
                float w = displayRect.rect.width;
                float h = displayRect.rect.height;
                if (w > 1f && h > 1f)
                {
                    float aspect = GetMapAspect();
                    Vector2 norm = new Vector2(localPt.x / w, localPt.y / h);
                    float worldX = targetCamPos.x + norm.x * (2f * oldTargetSize * aspect);
                    float worldZ = targetCamPos.z + norm.y * (2f * oldTargetSize);

                    targetOrthoSize = clampedSize;
                    targetCamPos.x = worldX - norm.x * (2f * targetOrthoSize * aspect);
                    targetCamPos.z = worldZ - norm.y * (2f * targetOrthoSize);
                    ClampTargetCamPos();
                    return;
                }
            }
        }

        targetOrthoSize = clampedSize;
        ClampTargetCamPos();
    }

    public void ZoomAtScreenPoint(Vector2 screenPos, float zoomDelta)
    {
        float factor = Mathf.Pow(zoomStepFactor, zoomDelta / zoomSensitivity);
        ApplyZoomTarget(targetOrthoSize * factor, screenPos);
    }

    public void ZoomAtCenter(float zoomDelta)
    {
        float factor = zoomDelta < 0f ? (1f / zoomStepFactor) : zoomStepFactor;
        ApplyZoomTarget(targetOrthoSize * factor);
    }

    private void ClampTargetCamPos()
    {
        float aspect = GetMapAspect();
        float halfW = targetOrthoSize * aspect;
        float halfH = targetOrthoSize;

        float minX = cityBounds.min.x - mapPanPadding;
        float maxX = cityBounds.max.x + mapPanPadding;
        float minZ = cityBounds.min.z - mapPanPadding;
        float maxZ = cityBounds.max.z + mapPanPadding;

        if (halfW * 2f >= (maxX - minX))
        {
            targetCamPos.x = cityBounds.center.x;
        }
        else
        {
            targetCamPos.x = Mathf.Clamp(targetCamPos.x, minX + halfW, maxX - halfW);
        }

        if (halfH * 2f >= (maxZ - minZ))
        {
            targetCamPos.z = cityBounds.center.z;
        }
        else
        {
            targetCamPos.z = Mathf.Clamp(targetCamPos.z, minZ + halfH, maxZ - halfH);
        }

        targetCamPos.y = fullMapCameraHeight;
    }

    public void RecenterMap()
    {
        CalculatePlayAreaBounds();
        targetOrthoSize = autoFitOrthoSize;
        if (player != null)
        {
            targetCamPos = new Vector3(player.position.x, fullMapCameraHeight, player.position.z);
        }
        else
        {
            targetCamPos = new Vector3(cityBounds.center.x, fullMapCameraHeight, cityBounds.center.z);
        }
        ClampTargetCamPos();
    }

    private bool WasMinimapClickedThisFrame()
    {
        if (!Input.GetMouseButtonDown(0)) return false;

        Vector2 mousePos = Input.mousePosition;
        if (displayRect != null && RectTransformUtility.RectangleContainsScreenPoint(displayRect, mousePos, null))
            return true;
        if (borderRect != null && RectTransformUtility.RectangleContainsScreenPoint(borderRect, mousePos, null))
            return true;

        return false;
    }

    public void OpenFullMap()
    {
        if (isFullMapOpen || !CanToggleMap()) return;

        ResolvePlayerReferences();

        if (!hasSavedControlState && transitionProgress <= 0.001f)
        {
            savedWasHoldingCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            savedCursorLockMode = Cursor.lockState;
            savedCursorVisible = Cursor.visible;
            savedFpcEnabled = fpc != null ? fpc.enabled : true;
            savedInputsCursorLocked = playerInputs != null ? playerInputs.cursorLocked : true;
            savedInputsCursorLook = playerInputs != null ? playerInputs.cursorInputForLook : true;
            hasSavedControlState = true;
        }

        isFullMapOpen = true;
        hasOpenedMapOnce = true;
        openedAtUnscaledTime = Time.unscaledTime;
        AudioManager.Instance?.PlayPanelWhoosh();

        savedLodBias = QualitySettings.lodBias;
        QualitySettings.lodBias = 100f; // Force highest LOD for full map view

        if (fpc != null) fpc.enabled = false;
        if (playerInputs != null)
        {
            playerInputs.move = Vector2.zero;
        }

        CursorState.SetLocked(false, playerInputs);

        CalculatePlayAreaBounds();
        targetOrthoSize = autoFitOrthoSize;
        targetCamPos = new Vector3(cityBounds.center.x, fullMapCameraHeight, cityBounds.center.z);
        currentCamPos = targetCamPos;
        currentOrthoSize = autoFitOrthoSize;

        EnsureFullMapCamera();
        if (fullMapCam != null)
        {
            fullMapCam.transform.position = currentCamPos;
            fullMapCam.orthographicSize = currentOrthoSize;
            fullMapCam.enabled = true;
            fullMapCam.Render(); // Render at least one frame immediately to avoid black flash
        }
        isMinimapViewActive = false;
        if (minimapCam != null)
        {
            minimapCam.enabled = false;
        }

        if (displayRawImage != null && fullMapRenderTexture != null)
        {
            displayRawImage.texture = fullMapRenderTexture;
        }

        if (mapControlsRoot != null)
        {
            mapControlsRoot.gameObject.SetActive(true);
        }
    }

    public void CloseFullMap()
    {
        if (!isFullMapOpen) return;
        isFullMapOpen = false;
        QualitySettings.lodBias = savedLodBias;
        AudioManager.Instance?.PlayPanelWhoosh();

        if (hasOpenedMapOnce)
        {
            hasCompletedMapCycle = true;
        }

        RestorePreviousControlState();
    }

    private void ForceCloseMapImmediate()
    {
        isFullMapOpen = false;
        QualitySettings.lodBias = savedLodBias;
        transitionProgress = 0f;
        ApplyMapLayout(0f);
        SwitchToMinimapRenderTexture();
        hasSavedControlState = false;
    }

    private void RestorePreviousControlState()
    {
        if (!hasSavedControlState) return;
        ResolvePlayerReferences();

        if (IsMapStepActive)
        {
            if (fpc != null) fpc.enabled = savedFpcEnabled;
            CursorState.SetLocked(false, playerInputs);
            hasSavedControlState = false;
            return;
        }

        bool currentlyHoldingCtrl = (UnityEngine.InputSystem.Keyboard.current != null &&
            (UnityEngine.InputSystem.Keyboard.current.leftCtrlKey.isPressed || UnityEngine.InputSystem.Keyboard.current.rightCtrlKey.isPressed))
            || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        if (currentlyHoldingCtrl)
        {
            if (fpc != null) fpc.enabled = true;
            CursorState.SetLocked(false, playerInputs);
        }
        else if (savedWasHoldingCtrl)
        {
            if (fpc != null) fpc.enabled = true;
            CursorState.SetLocked(true, playerInputs);
        }
        else
        {
            if (fpc != null) fpc.enabled = savedFpcEnabled;
            CursorState.SetLocked(savedCursorLockMode == CursorLockMode.Locked || Application.isPlaying, playerInputs);
        }

        hasSavedControlState = false;
    }

    private void UpdateMapTransition()
    {
        float target = isFullMapOpen ? 1f : 0f;
        if (!Mathf.Approximately(transitionProgress, target))
        {
            float dur = Mathf.Max(0.05f, transitionDuration);
            transitionProgress = Mathf.MoveTowards(transitionProgress, target, Time.unscaledDeltaTime / dur);
            float eased = SmoothEase(transitionProgress);
            ApplyMapLayout(eased);

            if (Mathf.Approximately(transitionProgress, 0f))
            {
                SwitchToMinimapRenderTexture();
            }
        }
    }

    private float SmoothEase(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }

    private void ApplyMapLayout(float easedT)
    {
        if (minimapCanvasRect == null || borderRect == null || displayRect == null) return;

        Vector2 canvasSize = minimapCanvasRect.rect.size;
        if (canvasSize.x <= 1f || canvasSize.y <= 1f)
        {
            canvasSize = new Vector2(1920f, 1080f);
        }

        float maxSquare = Mathf.Min(canvasSize.x * 0.72f, canvasSize.y * 0.72f, fullMapDisplaySize.y);
        Vector2 targetDispSize = new Vector2(maxSquare, maxSquare);
        Vector2 targetBorderSize = targetDispSize + fullMapBorderPadding;

        Vector2 centerInTopRightAnchor = new Vector2(-canvasSize.x * 0.5f, -canvasSize.y * 0.5f);
        Vector2 targetBorderPos = centerInTopRightAnchor;
        Vector2 targetDispPos = centerInTopRightAnchor + targetDispSize * 0.5f;

        borderRect.anchoredPosition = Vector2.LerpUnclamped(origBorderAnchoredPos, targetBorderPos, easedT);
        borderRect.sizeDelta = Vector2.LerpUnclamped(origBorderSizeDelta, targetBorderSize, easedT);

        displayRect.anchoredPosition = Vector2.LerpUnclamped(origDisplayAnchoredPos, targetDispPos, easedT);
        displayRect.sizeDelta = Vector2.LerpUnclamped(origDisplaySizeDelta, targetDispSize, easedT);

        if (fullMapBackdropImage != null)
        {
            bool showBackdrop = easedT > 0.001f;
            if (fullMapBackdropImage.gameObject.activeSelf != showBackdrop)
                fullMapBackdropImage.gameObject.SetActive(showBackdrop);
            if (showBackdrop)
                fullMapBackdropImage.color = new Color(0f, 0f, 0f, 0.45f * easedT);
        }

        if (mapControlsRoot != null)
        {
            bool showControls = easedT > 0.85f;
            if (mapControlsRoot.gameObject.activeSelf != showControls)
                mapControlsRoot.gameObject.SetActive(showControls);
        }

        UpdateMinimapCanvasSorting();
    }

    private void SwitchToMinimapRenderTexture()
    {
        if (fullMapCam != null)
        {
            fullMapCam.enabled = false;
        }
        QualitySettings.lodBias = savedLodBias;
        isMinimapViewActive = true;
        forceNextMinimapRender = true;
        if (minimapCam != null)
        {
            minimapCam.enabled = false;
            minimapCam.orthographicSize = minimapOrthoSize;
            if (minimapRenderTexture != null)
            {
                minimapCam.targetTexture = minimapRenderTexture;
            }
        }
        if (displayRawImage != null && minimapRenderTexture != null)
        {
            displayRawImage.texture = minimapRenderTexture;
        }
        if (mapControlsRoot != null)
        {
            mapControlsRoot.gameObject.SetActive(false);
        }
        UpdateMinimapCanvasSorting();
    }

    private void UpdateMinimapCanvasSorting()
    {
        if (minimapCanvas == null) return;

        bool raiseCanvas = (transitionProgress > 0.001f) || (tutorialHighlightAlpha > 0.001f) || IsMapStepActive;
        if (raiseCanvas)
        {
            minimapCanvas.overrideSorting = true;
            minimapCanvas.sortingOrder = (transitionProgress > 0.001f) ? 100 : 30;
        }
        else
        {
            minimapCanvas.overrideSorting = true;
            minimapCanvas.sortingOrder = 30;
        }
    }

    private void LateUpdate()
    {
        if (player == null) return;

        float easedT = SmoothEase(transitionProgress);

        // 1. Update Minimap Camera (tighter zoom following player) on cadence
        if (minimapCam != null && isMinimapViewActive)
        {
            Vector3 playerPos = player.position;
            float playerYaw = player.eulerAngles.y;
            minimapCam.transform.position = new Vector3(playerPos.x, cameraHeight, playerPos.z);
            minimapCam.transform.rotation = Quaternion.Euler(90f, playerYaw, 0f);
            minimapCam.orthographicSize = minimapOrthoSize;

            framesSinceMinimapRender++;
            bool movedOrTurned = (playerPos - lastMinimapRenderPos).sqrMagnitude > 0.0025f
                || Mathf.Abs(Mathf.DeltaAngle(lastMinimapRenderYaw, playerYaw)) > 0.5f;
            int renderInterval = movedOrTurned ? 2 : 6;
            if (forceNextMinimapRender || framesSinceMinimapRender >= renderInterval)
            {
                minimapCam.Render();
                framesSinceMinimapRender = 0;
                forceNextMinimapRender = false;
                lastMinimapRenderPos = playerPos;
                lastMinimapRenderYaw = playerYaw;
            }
        }

        // 2. Update Full Map Camera (ONE smoothing path for targetCamPos and targetOrthoSize)
        if (fullMapCam != null && fullMapCam.enabled)
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.1f) dt = 0.1f;
            float panT = 1f - Mathf.Exp(-panSmoothing * dt);
            float zoomT = 1f - Mathf.Exp(-zoomSmoothing * dt);

            currentCamPos = Vector3.Lerp(currentCamPos, targetCamPos, panT);
            currentOrthoSize = Mathf.Lerp(currentOrthoSize, targetOrthoSize, zoomT);

            fullMapCam.transform.position = new Vector3(currentCamPos.x, fullMapCameraHeight, currentCamPos.z);
            fullMapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Top-down North-up
            fullMapCam.orthographicSize = currentOrthoSize;
        }

        CurrentCameraWorldY = (isFullMapOpen || easedT > 0.001f) ? fullMapCameraHeight : cameraHeight;
        CurrentIconScaleMultiplier = 1f;

        // 3. Update all UI Map Icons from final camera values
        UpdateUIMapIcons(easedT);
    }

    // =====================================================================
    // UI MAP ICONS RENDERING & OVERLAP RESOLUTION
    // =====================================================================

    private RectTransform GetOrCreateIconUI(MinimapIcon icon)
    {
        if (mapIconsContainer == null) return null;

        if (iconUIRects.TryGetValue(icon, out RectTransform rt) && rt != null)
        {
            return rt;
        }

        GameObject iconGo = new GameObject($"UIMapIcon_{icon.type}_{icon.gameObject.name}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconGo.transform.SetParent(mapIconsContainer, false);

        rt = iconGo.GetComponent<RectTransform>();
        Image img = iconGo.GetComponent<Image>();
        img.sprite = icon.GetResolvedSprite();
        img.preserveAspect = true;
        img.raycastTarget = false;

        // Set pivot: Bottom tip for pin icons, Center for badge
        if (icon.type == MinimapIcon.IconType.Player || icon.type == MinimapIcon.IconType.Trash)
        {
            rt.pivot = new Vector2(0.5f, 0.0f);
        }
        else
        {
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        iconUIRects[icon] = rt;
        iconUIImages[icon] = img;

        return rt;
    }

    private void RemoveIconUI(MinimapIcon icon)
    {
        if (iconUIRects.TryGetValue(icon, out RectTransform rt))
        {
            if (rt != null && rt.gameObject != null)
            {
                Destroy(rt.gameObject);
            }
            iconUIRects.Remove(icon);
        }
        iconUIImages.Remove(icon);
    }

    private struct VisibleIconInfo
    {
        public MinimapIcon icon;
        public RectTransform rt;
        public Vector2 idealPos;
        public Vector2 resolvedPos;
    }

    private void UpdateUIMapIcons(float easedT)
    {
        Camera activeCam = (isFullMapOpen || easedT > 0.001f) ? (fullMapCam != null ? fullMapCam : minimapCam) : minimapCam;
        if (activeCam == null || mapIconsContainer == null) return;

        float currentIconSize = Mathf.Lerp(minimapIconSize, fullMapIconSize, easedT);
        Vector2 containerSize = mapIconsContainer.rect.size;

        visibleIcons.Clear();
        RectTransform playerRt = null;
        RectTransform npcRt = null;

        for (int i = registeredIcons.Count - 1; i >= 0; i--)
        {
            MinimapIcon icon = registeredIcons[i];
            if (icon == null || icon.gameObject == null)
            {
                registeredIcons.RemoveAt(i);
                continue;
            }

            RectTransform rt = GetOrCreateIconUI(icon);
            if (rt == null) continue;

            // Visibility checks
            bool isActiveInScene = icon.gameObject.activeInHierarchy;
            bool isTrashAllowed = (icon.type != MinimapIcon.IconType.Trash) || AreTrashIconsActive;

            if (!isActiveInScene || !isTrashAllowed)
            {
                if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false);
                continue;
            }

            // Project world position to active camera viewport
            Vector3 vp = activeCam.WorldToViewportPoint(icon.transform.position);
            bool inFrustum = vp.z > 0f && vp.x >= -0.15f && vp.x <= 1.15f && vp.y >= -0.15f && vp.y <= 1.15f;

            if (!inFrustum)
            {
                if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false);
                continue;
            }

            if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);

            Vector2 idealLocal = new Vector2((vp.x - 0.5f) * containerSize.x, (vp.y - 0.5f) * containerSize.y);

            // Update sprite in case it was set dynamically
            if (iconUIImages.TryGetValue(icon, out Image img) && img != null)
            {
                Sprite targetSprite = icon.GetResolvedSprite();
                if (img.sprite != targetSprite)
                {
                    img.sprite = targetSprite;
                }
            }

            if (icon.type == MinimapIcon.IconType.Player)
            {
                playerRt = rt;
            }
            else if (icon.type == MinimapIcon.IconType.NPC)
            {
                npcRt = rt;
            }

            visibleIcons.Add(new VisibleIconInfo
            {
                icon = icon,
                rt = rt,
                idealPos = idealLocal,
                resolvedPos = idealLocal
            });
        }

        // Overlap Resolution: If two trash items (or trash and NPC) are too close in UI space,
        // nudge them slightly apart so both pins/badges are distinctly readable.
        float minSeparation = currentIconSize * 0.58f;
        for (int i = 0; i < visibleIcons.Count; i++)
        {
            var infoA = visibleIcons[i];
            if (infoA.icon.type == MinimapIcon.IconType.Player) continue;

            for (int j = i + 1; j < visibleIcons.Count; j++)
            {
                var infoB = visibleIcons[j];
                if (infoB.icon.type == MinimapIcon.IconType.Player) continue;

                Vector2 diff = infoB.resolvedPos - infoA.resolvedPos;
                float dist = diff.magnitude;

                if (dist < minSeparation)
                {
                    Vector2 dir = dist > 0.001f ? (diff / dist) : new Vector2(1f, 0.2f).normalized;
                    float overlap = (minSeparation - dist) * 0.5f;

                    infoA.resolvedPos -= dir * overlap;
                    infoB.resolvedPos += dir * overlap;

                    visibleIcons[i] = infoA;
                    visibleIcons[j] = infoB;
                }
            }
        }

        // Apply final UI positions, sizes, and orientations
        for (int i = 0; i < visibleIcons.Count; i++)
        {
            var info = visibleIcons[i];
            info.rt.anchoredPosition = info.resolvedPos;
            info.rt.sizeDelta = new Vector2(currentIconSize, currentIconSize);

            // Player icon points in heading direction on full map (North-up)
            if (info.icon.type == MinimapIcon.IconType.Player && player != null)
            {
                float targetYaw = (activeCam == fullMapCam || isFullMapOpen) ? -player.eulerAngles.y : 0f;
                info.rt.localRotation = Quaternion.Euler(0f, 0f, targetYaw);
            }
            else
            {
                info.rt.localRotation = Quaternion.identity;
            }
        }

        // Enforce strict layer sorting:
        // 1. Trash items at bottom
        // 2. NPC above trash items
        // 3. Player ALWAYS on top
        if (npcRt != null)
        {
            npcRt.SetAsLastSibling();
        }
        if (playerRt != null)
        {
            playerRt.SetAsLastSibling();
        }
    }

    // =====================================================================
    // TUTORIAL STEP HIGHLIGHT (BLUR + DARK TINT + GLOW RING + BOBBING ARROW)
    // =====================================================================

    private void EnsureTutorialHighlightObjects(GameObject uiMinimapGo)
    {
        if (tutorialTintCanvas == null)
        {
            GameObject tintCanvasGo = new GameObject("UI_MapTutorialTintCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            tintCanvasGo.transform.SetParent(uiMinimapGo.transform.parent, false);
            tutorialTintCanvas = tintCanvasGo.GetComponent<Canvas>();
            tutorialTintCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            tutorialTintCanvas.overrideSorting = true;
            tutorialTintCanvas.sortingOrder = 15;

            var scaler = tintCanvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            tutorialTintCanvasGroup = tintCanvasGo.GetComponent<CanvasGroup>();
            tutorialTintCanvasGroup.alpha = 0f;
            tutorialTintCanvasGroup.interactable = false;
            tutorialTintCanvasGroup.blocksRaycasts = false;

            GameObject tintImgGo = new GameObject("SoftDarkTint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            tintImgGo.transform.SetParent(tintCanvasGo.transform, false);
            RectTransform tintRt = tintImgGo.GetComponent<RectTransform>();
            tintRt.anchorMin = Vector2.zero;
            tintRt.anchorMax = Vector2.one;
            tintRt.offsetMin = Vector2.zero;
            tintRt.offsetMax = Vector2.zero;
            Image tintImg = tintImgGo.GetComponent<Image>();
            tintImg.color = new Color(0.02f, 0.05f, 0.03f, 0.58f);
            tintImg.raycastTarget = false;

            tintCanvasGo.SetActive(false);
        }

        if (tutorialGlowRect == null && borderRect != null)
        {
            GameObject glowGo = new GameObject("MinimapTutorialGlow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            glowGo.transform.SetParent(uiMinimapGo.transform, false);
            glowGo.transform.SetSiblingIndex(borderRect.GetSiblingIndex());

            tutorialGlowRect = glowGo.GetComponent<RectTransform>();
            tutorialGlowRect.anchorMin = borderRect.anchorMin;
            tutorialGlowRect.anchorMax = borderRect.anchorMax;
            tutorialGlowRect.pivot = borderRect.pivot;
            tutorialGlowRect.anchoredPosition = origBorderAnchoredPos;
            tutorialGlowRect.sizeDelta = origBorderSizeDelta + new Vector2(28f, 28f);

            Image glowImg = glowGo.GetComponent<Image>();
            glowImg.sprite = CreateGlowRingSprite();
            glowImg.color = new Color(1f, 0.88f, 0.25f, 0.95f);
            glowImg.raycastTarget = false;

            tutorialGlowCanvasGroup = glowGo.GetComponent<CanvasGroup>();
            tutorialGlowCanvasGroup.alpha = 0f;
            tutorialGlowCanvasGroup.blocksRaycasts = false;
            glowGo.SetActive(false);
        }

        if (tutorialArrowRect == null && borderRect != null)
        {
            GameObject arrowGo = new GameObject("MinimapTutorialArrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            arrowGo.transform.SetParent(uiMinimapGo.transform, false);
            arrowGo.transform.SetAsLastSibling();

            tutorialArrowRect = arrowGo.GetComponent<RectTransform>();
            tutorialArrowRect.anchorMin = new Vector2(1f, 1f);
            tutorialArrowRect.anchorMax = new Vector2(1f, 1f);
            tutorialArrowRect.pivot = new Vector2(0.5f, 0.5f);
            arrowBaseAnchoredPos = new Vector2(origBorderAnchoredPos.x - origBorderSizeDelta.x * 0.5f - 58f, origBorderAnchoredPos.y);
            tutorialArrowRect.anchoredPosition = arrowBaseAnchoredPos;
            tutorialArrowRect.sizeDelta = new Vector2(76f, 64f);

            Image arrowImg = arrowGo.GetComponent<Image>();
            arrowImg.sprite = CreateRightArrowSprite();
            arrowImg.color = new Color(1f, 0.86f, 0.18f, 1f);
            arrowImg.raycastTarget = false;

            tutorialArrowCanvasGroup = arrowGo.GetComponent<CanvasGroup>();
            tutorialArrowCanvasGroup.alpha = 0f;
            tutorialArrowCanvasGroup.blocksRaycasts = false;
            arrowGo.SetActive(false);
        }
    }

    public void SetTutorialHighlightActive(bool active, bool immediate = false, Action onComplete = null)
    {
        if (tutorialHighlightRoutine != null)
        {
            StopCoroutine(tutorialHighlightRoutine);
            tutorialHighlightRoutine = null;
        }

        if (immediate)
        {
            ApplyTutorialHighlightWeight(active ? 1f : 0f);
            onComplete?.Invoke();
            return;
        }

        tutorialHighlightRoutine = StartCoroutine(AnimateTutorialHighlight(active ? 1f : 0f, 0.35f, onComplete));
    }

    private IEnumerator AnimateTutorialHighlight(float targetWeight, float duration, Action onComplete)
    {
        float startWeight = tutorialHighlightAlpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = SmoothEase(t);
            ApplyTutorialHighlightWeight(Mathf.Lerp(startWeight, targetWeight, ease));
            yield return null;
        }

        ApplyTutorialHighlightWeight(targetWeight);
        tutorialHighlightRoutine = null;
        onComplete?.Invoke();
    }

    private void ApplyTutorialHighlightWeight(float weight)
    {
        tutorialHighlightAlpha = Mathf.Clamp01(weight);
        bool show = tutorialHighlightAlpha > 0.001f;

        if (!PauseMenuController.IsPaused)
        {
            if (show) UIBackgroundBlur.SetWeight(tutorialHighlightAlpha);
            else UIBackgroundBlur.Cleanup();
        }

        if (tutorialTintCanvas != null)
        {
            if (tutorialTintCanvas.gameObject.activeSelf != show)
                tutorialTintCanvas.gameObject.SetActive(show);
            if (tutorialTintCanvasGroup != null)
                tutorialTintCanvasGroup.alpha = tutorialHighlightAlpha;
        }

        if (tutorialGlowRect != null)
        {
            if (tutorialGlowRect.gameObject.activeSelf != show)
                tutorialGlowRect.gameObject.SetActive(show);
        }

        if (tutorialArrowRect != null)
        {
            if (tutorialArrowRect.gameObject.activeSelf != show)
                tutorialArrowRect.gameObject.SetActive(show);
        }

        UpdateMinimapCanvasSorting();
        UpdateTutorialHighlightVisuals();
    }

    private void UpdateTutorialHighlightVisuals()
    {
        if (tutorialHighlightAlpha <= 0.001f) return;

        float pulse = 0.85f + Mathf.Sin(Time.unscaledTime * 5f) * 0.15f;

        if (tutorialGlowRect != null && borderRect != null)
        {
            tutorialGlowRect.anchoredPosition = borderRect.anchoredPosition;
            float pad = Mathf.Lerp(26f, 34f, (pulse - 0.7f) / 0.3f);
            tutorialGlowRect.sizeDelta = borderRect.sizeDelta + new Vector2(pad, pad);
            if (tutorialGlowCanvasGroup != null)
            {
                tutorialGlowCanvasGroup.alpha = tutorialHighlightAlpha * pulse;
            }
        }

        if (tutorialArrowRect != null)
        {
            float bob = Mathf.Sin(Time.unscaledTime * 6.5f) * 12f;
            tutorialArrowRect.anchoredPosition = arrowBaseAnchoredPos + new Vector2(bob, 0f);
            if (tutorialArrowCanvasGroup != null)
            {
                tutorialArrowCanvasGroup.alpha = tutorialHighlightAlpha * (1f - SmoothEase(transitionProgress));
            }
        }
    }

    private Sprite CreateGlowRingSprite()
    {
        int res = 64;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dx = Mathf.Min(x, res - 1 - x);
                float dy = Mathf.Min(y, res - 1 - y);
                float edgeDist = Mathf.Min(dx, dy);
                float alpha = 0f;
                if (edgeDist <= 10f)
                {
                    alpha = Mathf.Sin((edgeDist / 10f) * Mathf.PI);
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(12, 12, 12, 12));
    }

    private Sprite CreateRightArrowSprite()
    {
        int w = 64, h = 64;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        float cy = h * 0.5f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dy = Mathf.Abs(y - cy);
                bool inShaft = (x >= 6 && x <= 34 && dy <= 10f);
                bool inHead = (x >= 30 && x <= 58 && dy <= (58 - x) * 0.85f);

                if (inShaft || inHead)
                {
                    bool isBorder = (x <= 8 || (inShaft && !inHead && dy >= 7.5f) || (inHead && dy >= (58 - x) * 0.85f - 3f));
                    tex.SetPixel(x, y, isBorder ? new Color(0.25f, 0.16f, 0.04f, 1f) : Color.white);
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
    }

    public void ResetTutorialCycle()
    {
        hasOpenedMapOnce = false;
        hasCompletedMapCycle = false;
    }

    private void ResolvePlayerReferences()
    {
        if (fpc == null) fpc = FindFirstObjectByType<StarterAssets.FirstPersonController>();
        if (playerInputs == null) playerInputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
    }

    public void OnMinimapPointerClick()
    {
        if (!isFullMapOpen && CanToggleMap())
        {
            OpenFullMap();
        }
    }

    public void OnBackdropPointerClick()
    {
        if (isFullMapOpen && Time.unscaledTime - openedAtUnscaledTime > 0.2f)
        {
            CloseFullMap();
        }
    }

    public class MinimapClickForwarder : MonoBehaviour, IPointerClickHandler
    {
        public MinimapController controller;
        public bool isBackdrop = false;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (controller == null) controller = MinimapController.Instance;
            if (controller == null) return;

            if (isBackdrop)
            {
                controller.OnBackdropPointerClick();
            }
            else
            {
                controller.OnMinimapPointerClick();
            }
        }
    }
}