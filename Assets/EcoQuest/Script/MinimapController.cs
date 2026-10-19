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

    public Transform player;
    public float cameraHeight = 60f;

    [Header("Full Map Settings")]
    public float fullMapCameraHeight = 180f;
    public float fullMapPadding = 24f;
    public float transitionDuration = 0.28f;
    public Vector2 fullMapDisplaySize = new Vector2(740f, 740f);
    public Vector2 fullMapBorderPadding = new Vector2(18f, 18f);

    [Header("Icon Settings")]
    public float minimapIconSize = 36f;
    public float fullMapIconSize = 64f;

    [Header("Default Icon Sprites")]
    public Sprite playerIconSprite;
    public Sprite npcIconSprite;
    public Sprite trashIconSprite;

    private Camera minimapCam;
    private UniversalAdditionalCameraData uac;
    private RenderTexture minimapRenderTexture;
    private RenderTexture fullMapRenderTexture;

    private Canvas minimapCanvas;
    private RectTransform minimapCanvasRect;
    private RectTransform borderRect;
    private RectTransform displayRect;
    private RawImage displayRawImage;
    private Image fullMapBackdropImage;
    private RectTransform mapIconsContainer;

    // Saved original minimap UI layout
    private Vector2 origBorderAnchoredPos;
    private Vector2 origBorderSizeDelta;
    private Vector2 origDisplayAnchoredPos;
    private Vector2 origDisplaySizeDelta;
    private float origOrthoSize = 20f;
    private bool origCanvasOverrideSorting = false;
    private int origCanvasSortingOrder = 0;

    // Calculated full-map play area bounds
    private Vector3 fullMapCenter = new Vector3(494f, 0f, 427f);
    private float fullMapOrthoSize = 95f;

    // Map open/close state
    private bool isFullMapOpen = false;
    private float transitionProgress = 0f; // 0 = minimap, 1 = full map
    private float openedAtUnscaledTime = -10f;
    private bool hasOpenedMapOnce = false;
    private bool hasCompletedMapCycle = false;

    // Fog & Camera Rendering management
    private bool wasFogEnabled = false;
    private bool isRenderingMinimapCam = false;

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

    public bool IsFullMapOpen => isFullMapOpen;
    public bool HasOpenedMapOnce => hasOpenedMapOnce;
    public bool HasCompletedMapCycle => hasCompletedMapCycle;

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

#if UNITY_EDITOR
        string fname = (type == MinimapIcon.IconType.Player) ? "map_person_pin_tan.png"
            : (type == MinimapIcon.IconType.NPC) ? "map_npc_guardian_elder.png"
            : "map_trash_pin_alert.png";
        return AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/EcoQuest/Sprites/MapIcons/{fname}");
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
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == minimapCam)
        {
            isRenderingMinimapCam = true;
            wasFogEnabled = RenderSettings.fog;
            RenderSettings.fog = false;
        }
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == minimapCam && isRenderingMinimapCam)
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
            origOrthoSize = minimapCam.orthographicSize > 0.1f ? minimapCam.orthographicSize : 20f;
            minimapCam.orthographic = true;
            minimapCam.clearFlags = CameraClearFlags.SolidColor;
            minimapCam.backgroundColor = new Color(0.18f, 0.28f, 0.18f, 1f);
            minimapCam.allowMSAA = true;
            minimapCam.cullingMask = (1 << 0) | (1 << 4) | (1 << 6); // Default, Water, MinimapOnly

            uac = minimapCam.GetComponent<UniversalAdditionalCameraData>();
            if (uac != null)
            {
                uac.renderPostProcessing = false;
                uac.volumeLayerMask = 0; // Nothing
                uac.renderShadows = true;
            }

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
        CalculatePlayAreaBounds();
        CreateFullMapRenderTexture();
    }

    private void OnDestroy()
    {
        if (fullMapRenderTexture != null)
        {
            if (minimapCam != null && minimapCam.targetTexture == fullMapRenderTexture)
            {
                minimapCam.targetTexture = minimapRenderTexture;
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
            backdropGo.AddComponent<MinimapClickForwarder>().controller = this;
            backdropGo.SetActive(false);
        }
        else
        {
            fullMapBackdropImage = existingBackdrop.GetComponent<Image>();
            if (existingBackdrop.GetComponent<MinimapClickForwarder>() == null)
            {
                existingBackdrop.gameObject.AddComponent<MinimapClickForwarder>().controller = this;
            }
        }

        EnsureTutorialHighlightObjects(uiMinimapGo);
    }

    private void CreateFullMapRenderTexture()
    {
        if (fullMapRenderTexture != null) return;
        fullMapRenderTexture = new RenderTexture(2048, 2048, 24, RenderTextureFormat.ARGB32);
        fullMapRenderTexture.name = "FullMapRuntimeRT";
        fullMapRenderTexture.antiAliasing = 8;
        fullMapRenderTexture.filterMode = FilterMode.Bilinear;
        fullMapRenderTexture.useMipMap = false;
        fullMapRenderTexture.autoGenerateMips = false;
        fullMapRenderTexture.Create();
    }

    public void CalculatePlayAreaBounds()
    {
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        void EncapsulatePoint(Vector3 pt)
        {
            if (!hasBounds)
            {
                bounds = new Bounds(pt, Vector3.zero);
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(pt);
            }
        }

        var gpRoot = GameObject.Find("--- GAMEPLAY ---");
        if (gpRoot != null)
        {
            foreach (var r in gpRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.gameObject.name.StartsWith("MinimapIcon_")) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }
            foreach (var ti in gpRoot.GetComponentsInChildren<TrashItem>(true))
            {
                if (ti != null) EncapsulatePoint(ti.transform.position);
            }
        }

        var rocksGroup = GameObject.Find("RocksGroup");
        if (rocksGroup != null)
        {
            foreach (var r in rocksGroup.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }
        }

        var cityRoads = GameObject.Find("Road and Ground");
        if (cityRoads != null)
        {
            foreach (var r in cityRoads.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }
        }

        if (hasBounds)
        {
            fullMapCenter = new Vector3(bounds.center.x, 0f, bounds.center.z);
            float halfExtent = Mathf.Max(bounds.extents.x, bounds.extents.z) + fullMapPadding;
            fullMapOrthoSize = Mathf.Clamp(halfExtent, 50f, 350f);
        }
        else
        {
            fullMapCenter = new Vector3(494f, 0f, 427f);
            fullMapOrthoSize = 95f;
        }
    }

    public bool CanToggleMap()
    {
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

        var npcs = FindObjectsByType<NPCInteraction>(FindObjectsSortMode.None);
        for (int i = 0; i < npcs.Length; i++)
        {
            if (npcs[i] != null && npcs[i].IsTalking)
                return false;
        }

        var tm = FindFirstObjectByType<TutorialManager>();
        if (tm != null && tm.tutorialOverlay != null && tm.tutorialOverlay.activeInHierarchy)
            return false;

        if (SequentialWASDTutorial.Instance != null && !SequentialWASDTutorial.Instance.IsCompleted && !IsMapTutorialUnlocked)
        {
            if (SettingsManager.Instance == null || SettingsManager.Instance.TutorialHints)
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
            bool escPressed = PauseMenuController.Instance == null && Input.GetKeyDown(KeyCode.Escape);
            bool clickedAnywhere = (Time.unscaledTime - openedAtUnscaledTime > 0.05f) && Input.GetMouseButtonDown(0);

            if (mPressed || escPressed || clickedAnywhere)
            {
                CloseFullMap();
            }
        }

        UpdateMapTransition();
        UpdateTutorialHighlightVisuals();
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

        if (fpc != null) fpc.enabled = false;
        if (playerInputs != null)
        {
            playerInputs.move = Vector2.zero;
            playerInputs.look = Vector2.zero;
            playerInputs.cursorLocked = false;
            playerInputs.cursorInputForLook = false;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (CenterScreenCursor.Instance != null)
        {
            CenterScreenCursor.Instance.SetVisible(false);
        }

        if (minimapCam != null && fullMapRenderTexture != null)
        {
            if (minimapRenderTexture != null && minimapCam.targetTexture == minimapRenderTexture)
            {
                minimapCam.Render();
            }
            minimapCam.targetTexture = fullMapRenderTexture;
            if (displayRawImage != null)
            {
                displayRawImage.texture = fullMapRenderTexture;
            }
        }
    }

    public void CloseFullMap()
    {
        if (!isFullMapOpen) return;
        isFullMapOpen = false;

        if (hasOpenedMapOnce)
        {
            hasCompletedMapCycle = true;
        }

        RestorePreviousControlState();
    }

    private void ForceCloseMapImmediate()
    {
        isFullMapOpen = false;
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
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (playerInputs != null)
            {
                playerInputs.cursorLocked = false;
                playerInputs.cursorInputForLook = false;
            }
            if (CenterScreenCursor.Instance != null)
            {
                CenterScreenCursor.Instance.SetVisible(false);
            }
            hasSavedControlState = false;
            return;
        }

        bool currentlyHoldingCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        if (currentlyHoldingCtrl)
        {
            if (fpc != null) fpc.enabled = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (playerInputs != null)
            {
                playerInputs.cursorLocked = false;
                playerInputs.cursorInputForLook = false;
            }
            if (CenterScreenCursor.Instance != null)
                CenterScreenCursor.Instance.SetVisible(false);
        }
        else if (savedWasHoldingCtrl)
        {
            if (fpc != null) fpc.enabled = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (playerInputs != null)
            {
                playerInputs.cursorLocked = true;
                playerInputs.cursorInputForLook = true;
            }
            if (CenterScreenCursor.Instance != null)
                CenterScreenCursor.Instance.SetVisible(true);
        }
        else
        {
            if (fpc != null) fpc.enabled = savedFpcEnabled;
            Cursor.lockState = savedCursorLockMode;
            Cursor.visible = savedCursorVisible;
            if (playerInputs != null)
            {
                playerInputs.cursorLocked = savedInputsCursorLocked;
                playerInputs.cursorInputForLook = savedInputsCursorLook;
            }
            if (CenterScreenCursor.Instance != null)
                CenterScreenCursor.Instance.SetVisible(savedCursorLockMode == CursorLockMode.Locked);
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

        UpdateMinimapCanvasSorting();
    }

    private void SwitchToMinimapRenderTexture()
    {
        if (minimapCam != null && minimapRenderTexture != null)
        {
            minimapCam.targetTexture = minimapRenderTexture;
        }
        if (displayRawImage != null && minimapRenderTexture != null)
        {
            displayRawImage.texture = minimapRenderTexture;
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

        Vector3 miniPos = new Vector3(player.position.x, cameraHeight, player.position.z);
        Quaternion miniRot = Quaternion.Euler(90f, player.eulerAngles.y, 0f);

        Vector3 fullPos = new Vector3(fullMapCenter.x, fullMapCameraHeight, fullMapCenter.z);
        Quaternion fullRot = Quaternion.Euler(90f, 0f, 0f); // North-up top-down view

        transform.position = Vector3.Lerp(miniPos, fullPos, easedT);
        transform.rotation = Quaternion.Slerp(miniRot, fullRot, easedT);

        CurrentCameraWorldY = transform.position.y;

        if (minimapCam != null)
        {
            float currentOrtho = Mathf.Lerp(origOrthoSize, fullMapOrthoSize, easedT);
            minimapCam.orthographicSize = currentOrtho;
            CurrentIconScaleMultiplier = 1f; // Icons are rendered on UI, constant pixel size
        }

        // Update all UI Map Icons
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
        if (minimapCam == null || mapIconsContainer == null) return;

        float currentIconSize = Mathf.Lerp(minimapIconSize, fullMapIconSize, easedT);
        Vector2 containerSize = mapIconsContainer.rect.size;

        List<VisibleIconInfo> visibleIcons = new List<VisibleIconInfo>();
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

            // Project world position to minimap camera viewport
            Vector3 vp = minimapCam.WorldToViewportPoint(icon.transform.position);
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
            info.rt.localRotation = Quaternion.identity; // Keep upright and not rotating
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
        if (isFullMapOpen)
        {
            if (Time.unscaledTime - openedAtUnscaledTime > 0.25f)
            {
                CloseFullMap();
            }
        }
        else if (CanToggleMap())
        {
            OpenFullMap();
        }
    }

    public class MinimapClickForwarder : MonoBehaviour, IPointerClickHandler
    {
        public MinimapController controller;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (controller != null)
            {
                controller.OnMinimapPointerClick();
            }
            else if (MinimapController.Instance != null)
            {
                MinimapController.Instance.OnMinimapPointerClick();
            }
        }
    }
}