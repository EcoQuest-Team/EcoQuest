using UnityEngine;
using UnityEngine.UI;

public class CenterScreenCursor : MonoBehaviour
{
    [Header("Cursor Styling")]
    public Color normalColor = new Color(1f, 1f, 1f, 0.85f);
    public Color hoverColor = new Color(0.2f, 1f, 0.4f, 1f); // Vibrant Green when aiming at interactables
    public float normalSize = 12f;
    public float hoverSize = 18f;

    private Canvas cursorCanvas;
    private Image cursorImage;
    private RectTransform cursorRect;
    private Camera mainCam;
    public static CenterScreenCursor Instance { get; private set; }

    private bool requestedVisible = true;

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
        Instance = this;
        CreateCursorUI();
    }

    private void Start()
    {
        mainCam = Camera.main;
    }

    private void Update()
    {
        bool modalsOpen = (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalOpen)
            || (UIOrchestrator.Instance != null && UIOrchestrator.Instance.AreModalsOpen)
            || QuizUIController.IsAnyQuizOpen
            || (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue);

        bool shouldShow = requestedVisible
            && !modalsOpen
            && Cursor.lockState == CursorLockMode.Locked
            && (IntroSequenceController.Instance == null || IntroSequenceController.Instance.CurrentState == IntroSequenceController.IntroState.Gameplay);

        if (cursorCanvas != null && cursorCanvas.enabled != shouldShow)
        {
            cursorCanvas.enabled = shouldShow;
        }

        if (cursorCanvas != null && !cursorCanvas.enabled) return;
        if (cursorImage == null) return;
        if (mainCam == null) mainCam = Camera.main;

        // Check if ray from center of screen hits an interactable object (NPC or Trash)
        bool isHoveringInteractable = false;
        if (mainCam != null)
        {
            Ray ray = mainCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int layerMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "BinSolid");
            if (Physics.Raycast(ray, out RaycastHit hit, 8f, layerMask, QueryTriggerInteraction.Ignore))
            {
                string tag = hit.collider.tag;
                if (tag == "Biodegradable" || tag == "Recyclable" || tag == "NonBiodegradable" ||
                    hit.collider.GetComponentInParent<NPCInteraction>() != null ||
                    hit.collider.GetComponentInParent<TrashItem>() != null ||
                    hit.collider.GetComponentInParent<DragAndDrop>() != null)
                {
                    isHoveringInteractable = true;
                }
            }
        }

        // Smoothly lerp size and color
        float targetSize = isHoveringInteractable ? hoverSize : normalSize;
        Color targetColor = isHoveringInteractable ? hoverColor : normalColor;

        cursorRect.sizeDelta = Vector2.Lerp(cursorRect.sizeDelta, new Vector2(targetSize, targetSize), Time.deltaTime * 15f);
        cursorImage.color = Color.Lerp(cursorImage.color, targetColor, Time.deltaTime * 15f);
    }

    private void CreateCursorUI()
    {
        // 1. Create UI Canvas overlay
        GameObject canvasObj = new GameObject("CenterCursorCanvas");
        cursorCanvas = canvasObj.AddComponent<Canvas>();
        cursorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        cursorCanvas.sortingOrder = 999; // Always on top
        canvasObj.AddComponent<CanvasGroup>();

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // 2. Create Center Dot Image
        GameObject dotObj = new GameObject("CenterCrosshairDot");
        dotObj.transform.SetParent(canvasObj.transform, false);

        cursorImage = dotObj.AddComponent<Image>();
        cursorImage.sprite = CreateCircleSprite();
        cursorImage.color = normalColor;
        cursorImage.raycastTarget = false;

        cursorRect = dotObj.GetComponent<RectTransform>();
        cursorRect.anchorMin = new Vector2(0.5f, 0.5f);
        cursorRect.anchorMax = new Vector2(0.5f, 0.5f);
        cursorRect.pivot = new Vector2(0.5f, 0.5f);
        cursorRect.anchoredPosition = Vector2.zero;
        cursorRect.sizeDelta = new Vector2(normalSize, normalSize);

        DontDestroyOnLoad(canvasObj);
    }

    private Sprite CreateCircleSprite()
    {
        int res = 64;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        float radius = res * 0.45f;
        Vector2 center = new Vector2(res / 2f, res / 2f);

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                if (dist <= radius)
                {
                    // Soft anti-aliased circle dot with a subtle outer border
                    float alpha = Mathf.Clamp01((radius - dist) / 2f);
                    float border = (dist > radius - 4f) ? 0.6f : 1f;
                    tex.SetPixel(x, y, new Color(border, border, border, alpha));
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
    }
}
