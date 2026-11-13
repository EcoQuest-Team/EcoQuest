using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Single unified center-screen crosshair controller shared across Forest and City scenes.
/// Renders a small crisp pure-white center dot (~8.5px visible disc with thin dark outline at 1920x1080)
/// and a subtle pure-white hover ring/scale-up on interactables. Zero green glow; zero Find calls in Update.
/// </summary>
public class CenterScreenCursor : MonoBehaviour
{
    [Header("Crosshair Styling (1920x1080 Reference)")]
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.92f);
    [SerializeField] private Color hoverColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color holdingColor = new Color(1f, 1f, 1f, 0.62f);
    [SerializeField] private float normalSize = 12f;
    [SerializeField] private float hoverSize = 13.5f;
    [SerializeField] private float holdingSize = 10.5f;
    [SerializeField] private float hoverRingMaxAlpha = 0.72f;
    [SerializeField] private float interactRange = 8f;

    [Header("Prefab References")]
    [SerializeField] private Canvas cursorCanvas;
    [SerializeField] private CanvasGroup cursorGroup;
    [SerializeField] private Image cursorImage;
    [SerializeField] private RectTransform cursorRect;
    [SerializeField] private Image hoverRingImage;
    [SerializeField] private RectTransform hoverRingRect;
    [SerializeField] private Sprite dotSprite;
    [SerializeField] private Sprite hoverRingSprite;

    private Camera mainCam;
    private ModalManager cachedModalManager;
    private UIOrchestrator cachedOrchestrator;
    private DialoguePresenter cachedDialoguePresenter;
    private IntroSequenceController cachedIntroController;
    private int interactLayerMask;
    private bool requestedVisible = true;
    private float currentRingAlpha;

    public static CenterScreenCursor Instance { get; private set; }

    public Color NormalColor => normalColor;
    public Color HoverColor => hoverColor;
    public Color HoldingColor => holdingColor;
    public float NormalSize => normalSize;
    public float HoverSize => hoverSize;
    public RectTransform CursorRect => cursorRect;
    public Image CursorImage => cursorImage;
    public Image HoverRingImage => hoverRingImage;

    public void SetVisible(bool isVisible)
    {
        requestedVisible = isVisible;
        if (cursorCanvas != null)
        {
            cursorCanvas.enabled = isVisible && !QuizUIController.IsAnyQuizOpen;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        interactLayerMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "BinSolid");
        BindOrBuildCrosshairUI();
    }

    private void Start()
    {
        mainCam = Camera.main;
        CacheSceneManagers();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void CacheSceneManagers()
    {
        cachedModalManager = FindFirstObjectByType<ModalManager>(FindObjectsInactive.Include);
        cachedOrchestrator = FindFirstObjectByType<UIOrchestrator>(FindObjectsInactive.Include);
        cachedDialoguePresenter = FindFirstObjectByType<DialoguePresenter>(FindObjectsInactive.Include);
        cachedIntroController = FindFirstObjectByType<IntroSequenceController>(FindObjectsInactive.Include);
    }

    private void Update()
    {
        bool modalsOpen = (cachedModalManager != null && cachedModalManager.IsAnyModalOpen)
            || (cachedOrchestrator != null && cachedOrchestrator.AreModalsOpen)
            || QuizUIController.IsAnyQuizOpen
            || (cachedDialoguePresenter != null && cachedDialoguePresenter.IsInDialogue);

        bool introReady = cachedIntroController == null
            || cachedIntroController.CurrentState == IntroSequenceController.IntroState.Gameplay;

        bool shouldShow = requestedVisible
            && !modalsOpen
            && Cursor.lockState == CursorLockMode.Locked
            && introReady;

        if (cursorCanvas != null && cursorCanvas.enabled != shouldShow)
        {
            cursorCanvas.enabled = shouldShow;
        }

        if (cursorCanvas != null && !cursorCanvas.enabled) return;
        if (cursorImage == null || cursorRect == null) return;
        if (mainCam == null) mainCam = Camera.main;

        bool isHoldingItem = DragAndDrop.CurrentlyGrabbedItem != null;
        bool isHoveringInteractable = !isHoldingItem && CheckInteractableAim();

        float targetSize = isHoldingItem ? holdingSize : (isHoveringInteractable ? hoverSize : normalSize);
        Color targetColor = isHoldingItem ? holdingColor : (isHoveringInteractable ? hoverColor : normalColor);
        float targetRingAlpha = isHoveringInteractable ? hoverRingMaxAlpha : 0f;

        float step = Time.unscaledDeltaTime * 15f;
        cursorRect.sizeDelta = Vector2.Lerp(cursorRect.sizeDelta, new Vector2(targetSize, targetSize), step);
        cursorImage.color = Color.Lerp(cursorImage.color, targetColor, step);

        if (hoverRingImage != null)
        {
            currentRingAlpha = Mathf.Lerp(currentRingAlpha, targetRingAlpha, step);
            hoverRingImage.color = new Color(1f, 1f, 1f, currentRingAlpha);
            if (hoverRingRect != null)
            {
                float ringSize = isHoveringInteractable ? 18f : 15f;
                hoverRingRect.sizeDelta = Vector2.Lerp(hoverRingRect.sizeDelta, new Vector2(ringSize, ringSize), step);
            }
        }
    }

    private bool CheckInteractableAim()
    {
        if (mainCam == null) return false;
        Ray ray = mainCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactLayerMask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        string tag = hit.collider.tag;
        if (tag == "Biodegradable" || tag == "Recyclable" || tag == "NonBiodegradable" ||
            tag == "Hazardous" || tag == "Electronic" || tag == "Hazmat")
        {
            return true;
        }

        return hit.collider.GetComponentInParent<NPCInteraction>() != null
            || hit.collider.GetComponentInParent<TrashItem>() != null
            || hit.collider.GetComponentInParent<DragAndDrop>() != null;
    }

    private void BindOrBuildCrosshairUI()
    {
        if (cursorCanvas == null) cursorCanvas = GetComponent<Canvas>();
        if (cursorGroup == null) cursorGroup = GetComponent<CanvasGroup>();

        if (cursorCanvas != null)
        {
            Transform dotT = transform.Find("Crosshair");
            if (cursorImage == null && dotT != null) cursorImage = dotT.GetComponent<Image>();
            if (cursorRect == null && dotT != null) cursorRect = dotT.GetComponent<RectTransform>();

            Transform ringT = transform.Find("HoverRing");
            if (hoverRingImage == null && ringT != null) hoverRingImage = ringT.GetComponent<Image>();
            if (hoverRingRect == null && ringT != null) hoverRingRect = ringT.GetComponent<RectTransform>();
        }

        if (cursorCanvas == null)
        {
            GameObject existing = GameObject.Find("--- UI ---/UI_Crosshair");
            if (existing != null)
            {
                cursorCanvas = existing.GetComponent<Canvas>();
                cursorGroup = existing.GetComponent<CanvasGroup>();
                Transform dotT = existing.transform.Find("Crosshair");
                if (dotT != null)
                {
                    cursorImage = dotT.GetComponent<Image>();
                    cursorRect = dotT.GetComponent<RectTransform>();
                }
                Transform ringT = existing.transform.Find("HoverRing");
                if (ringT != null)
                {
                    hoverRingImage = ringT.GetComponent<Image>();
                    hoverRingRect = ringT.GetComponent<RectTransform>();
                }
            }
        }

        if (cursorCanvas == null)
        {
            GameObject canvasObj = new GameObject("UI_Crosshair");
            cursorCanvas = canvasObj.AddComponent<Canvas>();
            cursorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            cursorCanvas.sortingOrder = 15;
            cursorGroup = canvasObj.AddComponent<CanvasGroup>();
            cursorGroup.interactable = false;
            cursorGroup.blocksRaycasts = false;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject dotObj = new GameObject("Crosshair");
            dotObj.transform.SetParent(canvasObj.transform, false);
            cursorImage = dotObj.AddComponent<Image>();
            cursorImage.raycastTarget = false;
            cursorRect = dotObj.GetComponent<RectTransform>();
            cursorRect.anchorMin = new Vector2(0.5f, 0.5f);
            cursorRect.anchorMax = new Vector2(0.5f, 0.5f);
            cursorRect.pivot = new Vector2(0.5f, 0.5f);
            cursorRect.anchoredPosition = Vector2.zero;
            cursorRect.sizeDelta = new Vector2(normalSize, normalSize);

            GameObject ringObj = new GameObject("HoverRing");
            ringObj.transform.SetParent(canvasObj.transform, false);
            hoverRingImage = ringObj.AddComponent<Image>();
            hoverRingImage.raycastTarget = false;
            hoverRingImage.color = new Color(1f, 1f, 1f, 0f);
            hoverRingRect = ringObj.GetComponent<RectTransform>();
            hoverRingRect.anchorMin = new Vector2(0.5f, 0.5f);
            hoverRingRect.anchorMax = new Vector2(0.5f, 0.5f);
            hoverRingRect.pivot = new Vector2(0.5f, 0.5f);
            hoverRingRect.anchoredPosition = Vector2.zero;
            hoverRingRect.sizeDelta = new Vector2(18f, 18f);
        }

        if (cursorImage != null)
        {
            if (cursorImage.sprite == null)
            {
                cursorImage.sprite = dotSprite != null ? dotSprite : CreateCrispDotSprite();
            }
            cursorImage.color = normalColor;
            cursorImage.raycastTarget = false;
        }
        if (cursorRect != null)
        {
            cursorRect.sizeDelta = new Vector2(normalSize, normalSize);
        }
        if (hoverRingImage != null)
        {
            if (hoverRingImage.sprite == null)
            {
                hoverRingImage.sprite = hoverRingSprite != null ? hoverRingSprite : CreateCrispRingSprite();
            }
            hoverRingImage.color = new Color(1f, 1f, 1f, 0f);
            hoverRingImage.raycastTarget = false;
        }
    }

    private static Sprite CreateCrispDotSprite()
    {
        const int res = 64;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Vector2 center = new Vector2(res * 0.5f, res * 0.5f);

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                Color c;
                if (dist <= 19.5f)
                {
                    c = Color.white;
                }
                else if (dist <= 22.0f)
                {
                    float t = Mathf.InverseLerp(22.0f, 19.5f, dist);
                    c = new Color(t, t, t, Mathf.Lerp(0.75f, 1f, t));
                }
                else if (dist <= 27.5f)
                {
                    float a = Mathf.InverseLerp(27.5f, 22.0f, dist) * 0.72f;
                    c = new Color(0.04f, 0.06f, 0.05f, a);
                }
                else
                {
                    c = Color.clear;
                }
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite CreateCrispRingSprite()
    {
        const int res = 64;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Vector2 center = new Vector2(res * 0.5f, res * 0.5f);

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float ringBand = 1f - Mathf.Clamp01(Mathf.Abs(dist - 24f) / 2.2f);
                float shadowBand = 1f - Mathf.Clamp01(Mathf.Abs(dist - 24f) / 4.0f);
                if (ringBand > 0.01f)
                {
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, ringBand));
                }
                else if (shadowBand > 0.01f)
                {
                    tex.SetPixel(x, y, new Color(0.04f, 0.06f, 0.05f, shadowBand * 0.45f));
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
    }
}
