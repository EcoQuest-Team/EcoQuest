using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Reusable UI component managing the kid-friendly control tip card animations:
/// - Entrance: fade in 0.25s + scale 0.9->1.0 (with overshoot) + slide up 20px; text follows 0.1s later.
/// - Idle: gentle breathing pulse.
/// - Single key press: sinks to scale 0.92, tint #D8D8D8 while held.
/// - WASD cluster: W above A S D, each key individually sinks and tints when pressed.
/// - Complete: green flash + checkmark pop, message for 1.2s, then fades out and slides down 0.3s.
/// Uses unscaled time, CanvasGroup, and smooth ease-out curves.
/// </summary>
public class ControlTipCardUI : MonoBehaviour
{
    [Header("Main UI References")]
    [SerializeField] private CanvasGroup cardCanvasGroup;
    [SerializeField] private CanvasGroup textCanvasGroup;
    [SerializeField] private RectTransform cardRect;

    [Header("Single Key References")]
    [SerializeField] private Image keyImage;
    [SerializeField] private RectTransform keyRect;
    [SerializeField] private TextMeshProUGUI keyLabelText;

    [Header("WASD Cluster References")]
    [SerializeField] private GameObject wasdClusterRoot;
    [SerializeField] private Image wKeyImage;
    [SerializeField] private Image aKeyImage;
    [SerializeField] private Image sKeyImage;
    [SerializeField] private Image dKeyImage;
    [SerializeField] private RectTransform wKeyRect;
    [SerializeField] private RectTransform aKeyRect;
    [SerializeField] private RectTransform sKeyRect;
    [SerializeField] private RectTransform dKeyRect;

    [Header("Content References")]
    [SerializeField] private TextMeshProUGUI stepLabelText;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI instructionText;
    [SerializeField] private Image checkmarkImage;
    [SerializeField] private RectTransform checkmarkRect;
    [SerializeField] private Image flashOverlay;

    [Header("Color Presets")]
    [SerializeField] private Color normalKeyColor = Color.white;
    [SerializeField] private Color heldKeyColor = new Color(0.847f, 0.847f, 0.847f, 1f); // #D8D8D8
    [SerializeField] private Color goldStepColor = new Color(0.96f, 0.73f, 0.22f, 1f);   // Rich gold
    [SerializeField] private Color normalTextColor = new Color(0.92f, 0.90f, 0.86f, 1f);
    [SerializeField] private Color successTextColor = new Color(0.25f, 0.95f, 0.45f, 1f);

    private Vector2 baseAnchoredPosition;
    private bool hasBasePosition = false;
    private Coroutine activeCardRoutine;
    private bool isIdlePulsing = false;
    private bool isKeyHeld = false;
    private bool isWASDActive = false;

    private StarterAssets.StarterAssetsInputs playerInputs;

    private void Awake()
    {
        if (cardCanvasGroup == null) cardCanvasGroup = GetComponent<CanvasGroup>();
        if (cardRect == null) cardRect = GetComponent<RectTransform>();
        if (cardRect != null && !hasBasePosition)
        {
            baseAnchoredPosition = cardRect.anchoredPosition;
            hasBasePosition = true;
        }

        playerInputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
        SetImmediateHidden();
    }

    public void SetImmediateHidden()
    {
        if (cardCanvasGroup != null)
        {
            cardCanvasGroup.alpha = 0f;
            cardCanvasGroup.interactable = false;
            cardCanvasGroup.blocksRaycasts = false;
        }
        if (textCanvasGroup != null) textCanvasGroup.alpha = 0f;
        if (checkmarkImage != null) checkmarkImage.gameObject.SetActive(false);
        if (flashOverlay != null) flashOverlay.color = new Color(0.2f, 0.9f, 0.3f, 0f);
        if (wasdClusterRoot != null) wasdClusterRoot.SetActive(false);
        isIdlePulsing = false;
        isKeyHeld = false;
        isWASDActive = false;
    }

    /// <summary>
    /// Displays a tip card with automatic renumbering and full entrance animation.
    /// </summary>
    public void ShowTip(ControlTipsData.TipConfig config, int stepNumber = 0, int totalSteps = 0)
    {
        if (activeCardRoutine != null) StopCoroutine(activeCardRoutine);

        Transform p = transform.parent;
        while (p != null)
        {
            if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
            p = p.parent;
        }
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        if (!hasBasePosition && cardRect != null)
        {
            baseAnchoredPosition = cardRect.anchoredPosition;
            hasBasePosition = true;
        }

        // 1. Step Counter (Gold, auto-numbered)
        if (stepLabelText != null)
        {
            stepLabelText.color = goldStepColor;
            if (stepNumber > 0 && totalSteps > 0)
            {
                stepLabelText.text = $"STEP {stepNumber} OF {totalSteps}";
            }
            else
            {
                stepLabelText.text = config.stepLabel.ToUpper();
            }
        }

        // 2. Title & Instruction (support single-line text or standard two-line)
        bool hasTitle = !string.IsNullOrEmpty(config.title);
        bool hasInstruction = !string.IsNullOrEmpty(config.instruction);

        if (hasTitle && !hasInstruction)
        {
            if (titleText != null)
            {
                titleText.gameObject.SetActive(true);
                titleText.text = config.title;
                titleText.enableAutoSizing = true;
                titleText.fontSizeMin = 14;
                titleText.fontSizeMax = 20;
            }
            if (instructionText != null) instructionText.gameObject.SetActive(false);
        }
        else if (!hasTitle && hasInstruction)
        {
            if (titleText != null)
            {
                titleText.gameObject.SetActive(true);
                titleText.text = config.instruction;
                titleText.enableAutoSizing = true;
                titleText.fontSizeMin = 14;
                titleText.fontSizeMax = 20;
            }
            if (instructionText != null) instructionText.gameObject.SetActive(false);
        }
        else
        {
            if (titleText != null)
            {
                titleText.gameObject.SetActive(true);
                titleText.enableAutoSizing = false;
                titleText.fontSize = 24;
                titleText.text = config.title;
            }
            if (instructionText != null)
            {
                instructionText.gameObject.SetActive(true);
                instructionText.text = config.instruction;
                instructionText.color = normalTextColor;
            }
        }

        // 3. Key setup: WASD Cluster vs Single Key vs No Key
        isWASDActive = config.isWASDCluster;
        bool hasKey = isWASDActive || (config.keyIcon != null) || !string.IsNullOrEmpty(config.keyLabelOverride);

        if (keyImage != null)
        {
            keyImage.gameObject.SetActive(hasKey);
            keyImage.enabled = hasKey && !isWASDActive;
        }

        // Adjust ContentArea left margin if no key icon is present
        var contentArea = transform.Find("ContentArea") as RectTransform;
        if (contentArea != null)
        {
            contentArea.offsetMin = hasKey ? new Vector2(170f, 12f) : new Vector2(24f, 12f);
            contentArea.offsetMax = new Vector2(-18f, -12f);
        }

        if (isWASDActive)
        {
            if (wasdClusterRoot != null) wasdClusterRoot.SetActive(true);
            if (keyLabelText != null) keyLabelText.gameObject.SetActive(false);
        }
        else
        {
            if (wasdClusterRoot != null) wasdClusterRoot.SetActive(false);
            if (hasKey && keyImage != null && config.keyIcon != null)
            {
                keyImage.sprite = config.keyIcon;
                keyImage.color = normalKeyColor;
                keyImage.preserveAspect = true;
            }

            if (keyLabelText != null)
            {
                bool hasLabel = hasKey && !string.IsNullOrEmpty(config.keyLabelOverride);
                keyLabelText.gameObject.SetActive(hasLabel);
                if (hasLabel)
                {
                    keyLabelText.text = config.keyLabelOverride;
                    keyLabelText.fontStyle = FontStyles.Bold;
                }
            }
        }

        if (checkmarkImage != null) checkmarkImage.gameObject.SetActive(false);
        if (flashOverlay != null) flashOverlay.color = new Color(0.2f, 0.9f, 0.3f, 0f);

        activeCardRoutine = StartCoroutine(EntranceRoutine());
    }

    private IEnumerator EntranceRoutine()
    {
        float duration = 0.25f;
        float elapsed = 0f;

        Vector2 startPos = baseAnchoredPosition - new Vector2(0f, 20f);
        Vector3 startScale = new Vector3(0.9f, 0.9f, 1f);

        cardRect.anchoredPosition = startPos;
        cardRect.localScale = startScale;
        cardCanvasGroup.alpha = 0f;
        cardCanvasGroup.interactable = true;
        cardCanvasGroup.blocksRaycasts = true;

        if (textCanvasGroup != null) textCanvasGroup.alpha = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Ease-out with small overshoot
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            float overshootScale = Mathf.Sin(t * Mathf.PI * 0.5f) * 1.04f;
            if (t > 0.85f) overshootScale = Mathf.Lerp(1.04f, 1.0f, (t - 0.85f) / 0.15f);

            cardCanvasGroup.alpha = Mathf.Lerp(0f, 1f, ease);
            cardRect.anchoredPosition = Vector2.Lerp(startPos, baseAnchoredPosition, ease);
            cardRect.localScale = Vector3.Lerp(startScale, Vector3.one * overshootScale, ease);

            // Text follows 0.1s later
            if (textCanvasGroup != null && elapsed >= 0.1f)
            {
                float textT = Mathf.Clamp01((elapsed - 0.1f) / (duration - 0.1f));
                textCanvasGroup.alpha = textT;
            }

            yield return null;
        }

        cardCanvasGroup.alpha = 1f;
        cardRect.anchoredPosition = baseAnchoredPosition;
        cardRect.localScale = Vector3.one;
        if (textCanvasGroup != null) textCanvasGroup.alpha = 1f;

        isIdlePulsing = true;
    }

    private void Update()
    {
        if (playerInputs == null) playerInputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();

        // Gentle breathing pulse while idle
        if (isIdlePulsing && cardRect != null)
        {
            float pulse = 1.0f + Mathf.Sin(Time.unscaledTime * 3.5f) * 0.015f;
            cardRect.localScale = new Vector3(pulse, pulse, 1f);
        }

        // WASD cluster key animations
        if (isWASDActive && wasdClusterRoot != null && wasdClusterRoot.activeInHierarchy)
        {
            bool wPressed = Input.GetKey(KeyCode.W) || (playerInputs != null && playerInputs.move.y > 0.1f);
            bool aPressed = Input.GetKey(KeyCode.A) || (playerInputs != null && playerInputs.move.x < -0.1f);
            bool sPressed = Input.GetKey(KeyCode.S) || (playerInputs != null && playerInputs.move.y < -0.1f);
            bool dPressed = Input.GetKey(KeyCode.D) || (playerInputs != null && playerInputs.move.x > 0.1f);

            UpdateKeySink(wKeyRect, wKeyImage, wPressed);
            UpdateKeySink(aKeyRect, aKeyImage, aPressed);
            UpdateKeySink(sKeyRect, sKeyImage, sPressed);
            UpdateKeySink(dKeyRect, dKeyImage, dPressed);
        }
        else
        {
            // Single key press visual feedback (sinks to 0.92, tint #D8D8D8)
            if (keyRect != null)
            {
                Vector3 targetKeyScale = isKeyHeld ? (Vector3.one * 0.92f) : Vector3.one;
                keyRect.localScale = Vector3.Lerp(keyRect.localScale, targetKeyScale, Time.unscaledDeltaTime * 15f);
            }

            if (keyImage != null)
            {
                Color targetKeyColor = isKeyHeld ? heldKeyColor : normalKeyColor;
                keyImage.color = Color.Lerp(keyImage.color, targetKeyColor, Time.unscaledDeltaTime * 15f);
            }
        }
    }

    private void UpdateKeySink(RectTransform rect, Image img, bool isPressed)
    {
        if (rect != null)
        {
            Vector3 targetScale = isPressed ? (Vector3.one * 0.92f) : Vector3.one;
            rect.localScale = Vector3.Lerp(rect.localScale, targetScale, Time.unscaledDeltaTime * 15f);
        }
        if (img != null)
        {
            Color targetColor = isPressed ? heldKeyColor : normalKeyColor;
            img.color = Color.Lerp(img.color, targetColor, Time.unscaledDeltaTime * 15f);
        }
    }

    /// <summary>
    /// Call while the player is actively holding or pressing the requested single key.
    /// </summary>
    public void SetKeyHeld(bool held)
    {
        isKeyHeld = held;
    }

    /// <summary>
    /// Plays the complete celebration (green flash + checkmark pop + cheer for 1.2s),
    /// then slides down and fades out over 0.3s.
    /// </summary>
    public void CompleteTip(string doneMessage, System.Action onFinished = null)
    {
        if (activeCardRoutine != null) StopCoroutine(activeCardRoutine);
        activeCardRoutine = StartCoroutine(CompleteRoutine(doneMessage, onFinished));
    }

    private IEnumerator CompleteRoutine(string doneMessage, System.Action onFinished)
    {
        isIdlePulsing = false;
        isKeyHeld = false;
        if (cardRect != null) cardRect.localScale = Vector3.one;

        // Show cheer/done message
        if (instructionText != null)
        {
            instructionText.text = doneMessage;
            instructionText.color = successTextColor;
        }

        // Checkmark pop animation
        if (checkmarkImage != null && checkmarkRect != null)
        {
            checkmarkImage.gameObject.SetActive(true);
            checkmarkRect.localScale = Vector3.zero;
        }

        // Green flash + checkmark bounce
        float popTime = 0.25f;
        float elapsed = 0f;
        while (elapsed < popTime)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / popTime);

            if (flashOverlay != null)
            {
                float flashA = Mathf.Sin(t * Mathf.PI) * 0.65f;
                flashOverlay.color = new Color(0.2f, 0.95f, 0.4f, flashA);
            }

            if (checkmarkRect != null)
            {
                float checkScale = Mathf.Sin(t * Mathf.PI * 0.5f) * 1.3f;
                checkmarkRect.localScale = Vector3.one * checkScale;
            }

            yield return null;
        }

        if (checkmarkRect != null) checkmarkRect.localScale = Vector3.one;
        if (flashOverlay != null) flashOverlay.color = new Color(0.2f, 0.95f, 0.4f, 0f);

        // Keep cheer visible for 1.2 seconds
        yield return new WaitForSecondsRealtime(1.2f);

        // Fade out and slide down over 0.3s
        float exitDuration = 0.3f;
        elapsed = 0f;
        Vector2 startPos = cardRect != null ? cardRect.anchoredPosition : baseAnchoredPosition;
        Vector2 endPos = baseAnchoredPosition - new Vector2(0f, 20f);

        while (elapsed < exitDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / exitDuration);
            float ease = t * t; // Ease-in down

            if (cardCanvasGroup != null) cardCanvasGroup.alpha = Mathf.Lerp(1f, 0f, ease);
            if (cardRect != null) cardRect.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);

            yield return null;
        }

        SetImmediateHidden();

        // 0.3s gap before next step
        yield return new WaitForSecondsRealtime(0.3f);

        onFinished?.Invoke();
    }

    /// <summary>
    /// Smoothly fades out and slides down the card without checkmark/cheer (used when a step ends via external trigger).
    /// </summary>
    public void DismissTip(System.Action onFinished = null)
    {
        if (activeCardRoutine != null) StopCoroutine(activeCardRoutine);
        activeCardRoutine = StartCoroutine(DismissRoutine(onFinished));
    }

    private IEnumerator DismissRoutine(System.Action onFinished)
    {
        isIdlePulsing = false;
        isKeyHeld = false;
        if (cardRect != null) cardRect.localScale = Vector3.one;

        float exitDuration = 0.3f;
        float elapsed = 0f;
        Vector2 startPos = cardRect != null ? cardRect.anchoredPosition : baseAnchoredPosition;
        Vector2 endPos = baseAnchoredPosition - new Vector2(0f, 20f);

        while (elapsed < exitDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / exitDuration);
            float ease = t * t;

            if (cardCanvasGroup != null) cardCanvasGroup.alpha = Mathf.Lerp(1f, 0f, ease);
            if (cardRect != null) cardRect.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);

            yield return null;
        }

        SetImmediateHidden();
        onFinished?.Invoke();
    }
}
