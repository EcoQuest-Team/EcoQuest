using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// World-anchored interaction prompt pinned near target NPC shoulder/head in screen space.
/// Displays target name (muted text) above and [Keycap] + Action below.
/// Includes 0.2s fade & scale pop transitions, screen clamping, and distance scaling (0.85 - 1.0).
/// </summary>
public class WorldInteractionPrompt : MonoBehaviour
{
    private static WorldInteractionPrompt _instance;
    public static WorldInteractionPrompt Instance
    {
        get
        {
            if (_instance == null) _instance = FindFirstObjectByType<WorldInteractionPrompt>();
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("UI References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform promptPanelRect;
    [SerializeField] private TextMeshProUGUI targetNameText;
    [SerializeField] private KeyCapPrompt keyCapPrompt;

    [Header("Positioning")]
    [SerializeField] private float shoulderHeight = 1.45f;
    [SerializeField] private float rightOffset = 0.35f;
    [SerializeField] private float screenPadding = 48f;

    [Header("Transitions & Scaling")]
    [SerializeField] private float transitionDuration = 0.20f;
    [SerializeField] private float minDistanceScale = 0.85f;
    [SerializeField] private float maxDistanceScale = 1.0f;
    [SerializeField] private float maxScaleDistance = 12f;

    private Camera mainCam;
    private Transform activeTarget;
    private bool isManuallyHidden = false;
    private float currentAlpha = 0f;
    private float targetAlpha = 0f;
    private bool isBehindCamera = false;
    private bool isOffScreen = false;
    private Vector3 lastScreenPos = Vector3.zero;
    private int lastShowFrame = -1;

    public bool IsVisible => currentAlpha > 0.1f;
    public Camera MainCam => mainCam != null ? mainCam : GetActiveWorldCamera();
    public CanvasGroup CanvasGroup => canvasGroup;
    public RectTransform PromptPanelRect => promptPanelRect;
    public Transform ActiveTarget => activeTarget;
    public bool IsManuallyHidden => isManuallyHidden;
    public float CurrentAlpha => currentAlpha;
    public bool IsProjectedBehindCamera => isBehindCamera;
    public bool IsProjectedOffScreen => isOffScreen;
    public Vector3 LastScreenPos => lastScreenPos;

    /// <summary>
    /// Finds the active player rendering camera with safe fallbacks and a loud warning if none found.
    /// </summary>
    public static Camera GetActiveWorldCamera()
    {
        if (Camera.main != null && Camera.main.isActiveAndEnabled)
            return Camera.main;

        GameObject[] mainCams = GameObject.FindGameObjectsWithTag("MainCamera");
        foreach (var go in mainCams)
        {
            var cam = go.GetComponent<Camera>();
            if (cam != null && cam.isActiveAndEnabled) return cam;
        }

        Camera[] allCameras = Camera.allCameras;
        foreach (var cam in allCameras)
        {
            if (cam != null && cam.isActiveAndEnabled && !cam.name.Contains("Minimap") && !cam.name.Contains("FullMap") && !cam.name.Contains("UI"))
            {
                return cam;
            }
        }

        Debug.LogWarning("[WorldInteractionPrompt] No valid active world camera found for prompt projection!");
        return null;
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (promptPanelRect == null) promptPanelRect = transform as RectTransform;
        if (keyCapPrompt == null) keyCapPrompt = GetComponentInChildren<KeyCapPrompt>(true);
        mainCam = GetActiveWorldCamera();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        if (promptPanelRect != null)
        {
            promptPanelRect.gameObject.SetActive(false);
        }

        currentAlpha = 0f;
        targetAlpha = 0f;
        activeTarget = null;

        EnsureCenteredLayout();
    }

    private void OnEnable()
    {
        EnsureCenteredLayout();
    }

    /// <summary>
    /// Guarantees that TargetNameText and ActionLabel are centered within the interaction prompt.
    /// </summary>
    public void EnsureCenteredLayout()
    {
        if (targetNameText != null)
        {
            targetNameText.alignment = TextAlignmentOptions.Center;
            targetNameText.horizontalAlignment = HorizontalAlignmentOptions.Center;
            targetNameText.textWrappingMode = TextWrappingModes.NoWrap;
            targetNameText.overflowMode = TextOverflowModes.Overflow;
            targetNameText.color = new Color(0.847f, 0.953f, 0.863f, 0.95f); // #D8F3DC
        }

        if (promptPanelRect != null)
        {
            var panelImg = promptPanelRect.GetComponent<Image>();
            if (panelImg != null && panelImg.sprite != null && panelImg.sprite.name.Contains("panel_rounded_forest_green"))
            {
                // Sprite already contains #0F2A1E (92% alpha) fill and 2px #6DBE6D border; white tint prevents double-darkening into gray
                panelImg.color = Color.white;
            }

            var hlg = promptPanelRect.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = false;
            }

            var keycap = promptPanelRect.Find("Keycap");
            if (keycap != null)
            {
                var le = keycap.GetComponent<LayoutElement>();
                if (le == null) le = keycap.gameObject.AddComponent<LayoutElement>();
                var krt = keycap as RectTransform;
                float kw = krt != null ? krt.sizeDelta.x : 46f;
                float kh = krt != null ? krt.sizeDelta.y : 46f;
                if (kw < 40f) kw = 46f;
                if (kh < 40f) kh = 46f;
                le.minWidth = kw;
                le.preferredWidth = kw;
                le.minHeight = kh;
                le.preferredHeight = kh;
            }

            var textCol = promptPanelRect.Find("TextCol") as RectTransform;
            if (textCol != null)
            {
                var vlg = textCol.GetComponent<VerticalLayoutGroup>();
                if (vlg != null)
                {
                    vlg.childAlignment = TextAnchor.MiddleCenter;
                    vlg.childControlWidth = true;
                    vlg.childControlHeight = true;
                    vlg.childForceExpandWidth = true;
                    vlg.childForceExpandHeight = false;
                }

                var csf = textCol.GetComponent<ContentSizeFitter>();
                if (csf == null) csf = textCol.gameObject.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var act = textCol.Find("ActionLabel")?.GetComponent<TextMeshProUGUI>();
                if (act != null)
                {
                    act.alignment = TextAlignmentOptions.Center;
                    act.horizontalAlignment = HorizontalAlignmentOptions.Center;
                    act.textWrappingMode = TextWrappingModes.NoWrap;
                    act.overflowMode = TextOverflowModes.Overflow;
                    act.color = Color.white;
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Formats display name cleanly, stripping GameObject clone artifacts and enforcing clean NPC roles.
    /// </summary>
    public static string FormatCleanName(string rawName, bool isLitterbug = false, bool isGuide = false)
    {
        if (isGuide) return "Guardian";
        if (isLitterbug) return "Litterbug";
        if (string.IsNullOrEmpty(rawName)) return "Citizen";

        string clean = rawName;
        if (clean.Contains("(Litterbug)")) return "Litterbug";
        if (clean.Contains("Guide") || clean.Contains("Guardian") || clean.Contains("Fountain")) return "Guardian";

        // Strip Unity clone suffix and underscores
        if (clean.Contains("(Clone)")) clean = clean.Replace("(Clone)", "");
        clean = clean.Trim();

        // If raw GameObject name (e.g. NPC_Poly_WomanPirate_Walker_Island9)
        if (clean.StartsWith("NPC_") || clean.StartsWith("NPC"))
        {
            string[] parts = clean.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.StartsWith("Man") || p.StartsWith("Woman") || p.StartsWith("Boy") || p.StartsWith("Girl"))
                {
                    return p;
                }
            }
            return "Citizen";
        }

        return clean;
    }

    /// <summary>
    /// Displays prompt for target interactable.
    /// </summary>
    public void ShowPrompt(Transform target, string targetName, string actionName, string key = "E")
    {
        if (target == null || isManuallyHidden || DragAndDrop.CurrentlyGrabbedItem != null)
        {
            HidePrompt();
            return;
        }

        activeTarget = target;
        isManuallyHidden = false;
        lastShowFrame = Time.frameCount;

        string cleanName = targetName;
        if (targetName.Contains("Litterbug")) cleanName = "Litterbug";
        else if (targetName.Contains("Guide") || targetName.Contains("Guardian") || targetName.Contains("Fountain")) cleanName = "Guardian";
        else cleanName = FormatCleanName(targetName);

        if (targetNameText != null)
        {
            if (!string.IsNullOrEmpty(cleanName))
            {
                targetNameText.gameObject.SetActive(true);
                targetNameText.text = cleanName.Trim();
                targetNameText.alignment = TextAlignmentOptions.Center;
            }
            else
            {
                targetNameText.gameObject.SetActive(false);
            }
        }
        if (keyCapPrompt != null) keyCapPrompt.SetPrompt(key, actionName);

        EnsureCenteredLayout();

        if (promptPanelRect != null)
        {
            if (!promptPanelRect.gameObject.activeSelf) promptPanelRect.gameObject.SetActive(true);
            var textCol = promptPanelRect.Find("TextCol") as RectTransform;
            if (textCol != null) LayoutRebuilder.ForceRebuildLayoutImmediate(textCol);
            LayoutRebuilder.ForceRebuildLayoutImmediate(promptPanelRect);
        }

        targetAlpha = 1f;
        currentAlpha = 1f;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }
    }

    /// <summary>
    /// Hides the prompt only if it is currently showing for the specified target.
    /// </summary>
    public void HidePromptIfTarget(Transform target)
    {
        if (activeTarget == target)
        {
            HidePrompt();
        }
    }

    /// <summary>
    /// Hides the prompt with smooth fade/scale pop.
    /// </summary>
    public void HidePrompt()
    {
        if (Application.isPlaying && lastShowFrame == Time.frameCount && !isManuallyHidden)
        {
            return;
        }
        targetAlpha = 0f;
        currentAlpha = 0f;
        activeTarget = null;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }
        if (promptPanelRect != null)
        {
            promptPanelRect.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Blocks prompt visibility during dialogue, pause, etc.
    /// </summary>
    public void SetBlocked(bool blocked)
    {
        isManuallyHidden = blocked;
        if (blocked)
        {
            targetAlpha = 0f;
            currentAlpha = 0f;
            activeTarget = null;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
            }
        }
    }

    private void LateUpdate()
    {
        mainCam = GetActiveWorldCamera();
        if (mainCam == null) return;

        bool isHoldingTrash = DragAndDrop.CurrentlyGrabbedItem != null;
        bool isDialogueOrModalOpen = (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue) ||
                                     LitterbugQuestManager.IsDialogueOpen ||
                                     (UIOrchestrator.Instance != null && UIOrchestrator.Instance.IsOverlayActive) ||
                                     (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalOpen);
        bool canShow = !isManuallyHidden && !isHoldingTrash && !isDialogueOrModalOpen && activeTarget != null && activeTarget.gameObject.activeInHierarchy;
        Vector3 screenPos = Vector3.zero;
        float targetDistance = 2f;
        isBehindCamera = false;
        isOffScreen = false;

        if (canShow)
        {
            targetDistance = Vector3.Distance(mainCam.transform.position, activeTarget.position);
            // Defensive distance guard: interaction prompt should never show for distant targets
            if (targetDistance > 4.5f)
            {
                canShow = false;
            }
            else
            {
                Vector3 toTarget = (activeTarget.position - mainCam.transform.position).normalized;
                float dot = Vector3.Dot(mainCam.transform.forward, toTarget);
                if (dot < 0.25f)
                {
                    // Target is behind or to the side of the camera (player looking away or down into bin)
                    isBehindCamera = true;
                    canShow = false;
                }
                else
                {
                    bool isPickupItem = activeTarget.GetComponent<EcoQuest.CityQuest.CityTrashPickupInstance>() != null ||
                                        activeTarget.name.StartsWith("PooledCityTrash") ||
                                        activeTarget.name.StartsWith("CityTrash") ||
                                        activeTarget.CompareTag("Trash");
                    float targetH = isPickupItem ? 0.30f : shoulderHeight;
                    float targetR = isPickupItem ? 0f : rightOffset;
                    Vector3 worldPos = activeTarget.position + Vector3.up * targetH + mainCam.transform.right * targetR;
                    screenPos = mainCam.WorldToScreenPoint(worldPos);
                    lastScreenPos = screenPos;

                    if (screenPos.z <= 0.05f)
                    {
                        isBehindCamera = true;
                        canShow = false;
                    }
                    else if (screenPos.x < 0f || screenPos.x > Screen.width || screenPos.y < 0f || screenPos.y > Screen.height)
                    {
                        isOffScreen = true;
                        canShow = false;
                    }
                }
            }
        }

        if (!canShow)
        {
            targetAlpha = 0f;
            // Immediate cut when player turns away, is blocked, holds trash, or activeTarget is null: never freeze at screen center
            if (activeTarget == null || isBehindCamera || isOffScreen || isHoldingTrash || isManuallyHidden)
            {
                currentAlpha = 0f;
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 0f;
                    canvasGroup.blocksRaycasts = false;
                }
            }
        }

        // Animate alpha and subtle scale pop
        if (Mathf.Abs(currentAlpha - targetAlpha) > 0.001f)
        {
            float rate = transitionDuration > 0f ? (1f / transitionDuration) : 100f;
            currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, Time.unscaledDeltaTime * rate);
            if (canvasGroup != null) canvasGroup.alpha = currentAlpha;
        }
        else if (canvasGroup != null && canvasGroup.alpha != currentAlpha)
        {
            canvasGroup.alpha = currentAlpha;
            canvasGroup.blocksRaycasts = (currentAlpha > 0.5f);
        }

        if (promptPanelRect != null)
        {
            bool shouldPanelBeActive = canShow || currentAlpha > 0.01f;
            if (promptPanelRect.gameObject.activeSelf != shouldPanelBeActive)
            {
                promptPanelRect.gameObject.SetActive(shouldPanelBeActive);
            }
        }

        if (promptPanelRect != null)
        {
            float popScale = Mathf.Lerp(0.85f, 1.0f, Mathf.SmoothStep(0f, 1f, currentAlpha));
            float distFactor = Mathf.Clamp01((targetDistance - 2f) / Mathf.Max(1f, maxScaleDistance));
            float distScale = Mathf.Lerp(maxDistanceScale, minDistanceScale, distFactor);
            float finalScale = popScale * distScale;
            promptPanelRect.localScale = new Vector3(finalScale, finalScale, 1f);
        }

        // Position clamping via Canvas local space (handles CanvasScaler / resolution differences)
        if (canShow && promptPanelRect != null && currentAlpha > 0.01f)
        {
            float minX = screenPadding;
            float maxX = Screen.width - screenPadding;
            float minY = screenPadding;
            float maxY = Screen.height - screenPadding;

            screenPos.x = Mathf.Clamp(screenPos.x, minX, maxX);
            screenPos.y = Mathf.Clamp(screenPos.y, minY, maxY);

            Canvas parentCanvas = GetComponentInParent<Canvas>();
            RectTransform parentCanvasRect = parentCanvas != null ? parentCanvas.transform as RectTransform : promptPanelRect.parent as RectTransform;

            if (parentCanvasRect != null)
            {
                Camera camForRect = (parentCanvas != null && parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : mainCam;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentCanvasRect, screenPos, camForRect, out Vector2 localPoint))
                {
                    promptPanelRect.anchoredPosition = localPoint;
                }
            }
            else
            {
                promptPanelRect.position = screenPos;
            }
        }
    }
}
