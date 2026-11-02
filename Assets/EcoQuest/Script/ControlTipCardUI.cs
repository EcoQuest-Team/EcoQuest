using System.Collections;
using System.Collections.Generic;
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
    public enum BannerState
    {
        Hidden,
        Showing,
        Visible,
        Hiding
    }

    public BannerState CurrentState { get; private set; } = BannerState.Hidden;

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
    [SerializeField] private Color normalTextColor = new Color(0.890f, 0.960f, 0.890f, 1f); // #E3F5E3 (UITheme.textMuted)
    [SerializeField] private Color successTextColor = new Color(0.25f, 0.95f, 0.45f, 1f);

    [Header("Transition Settings (Unscaled Time)")]
    [SerializeField] private float openDuration = 0.28f;
    [SerializeField] private float closeDuration = 0.18f;
    [SerializeField] private float stepTransitionDelay = 0.18f;
    [SerializeField] private float startScale = 0.95f;
    [SerializeField] private AnimationCurve openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve closeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

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
        if (cardRect != null)
        {
            var le = cardRect.GetComponent<LayoutElement>();
            if (le == null) le = cardRect.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 520f;
            le.preferredWidth = 560f;
            le.minHeight = 120f;

            cardRect.anchorMin = new Vector2(1f, 1f);
            cardRect.anchorMax = new Vector2(1f, 1f);
            cardRect.pivot = new Vector2(1f, 1f);
            cardRect.localScale = Vector3.one;
            cardRect.sizeDelta = new Vector2(560f, Mathf.Max(120f, cardRect.sizeDelta.y));
            cardRect.anchoredPosition = new Vector2(-40f, -280f);
            baseAnchoredPosition = cardRect.anchoredPosition;
            hasBasePosition = true;
        }

        if (checkmarkRect != null)
        {
            checkmarkRect.anchorMin = new Vector2(1f, 1f);
            checkmarkRect.anchorMax = new Vector2(1f, 1f);
            checkmarkRect.pivot = new Vector2(1f, 1f);
            checkmarkRect.anchoredPosition = new Vector2(-16f, -16f);
            checkmarkRect.sizeDelta = new Vector2(44f, 44f);
        }

        playerInputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
        SetImmediateHidden();
    }

    private void Start()
    {
        if (cardCanvasGroup != null)
        {
            HudVisibilityService.Instance?.RegisterOwnedElement(cardCanvasGroup);
        }
    }

    private void OnDisable()
    {
        isIdlePulsing = false;
        isKeyHeld = false;
    }

    private void OnDestroy()
    {
        if (activeCardRoutine != null)
        {
            DialoguePresenter.StopRunningCoroutine(activeCardRoutine);
            activeCardRoutine = null;
        }
    }

    public void ResetVisualState()
    {
        CurrentState = BannerState.Hidden;
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
        if (cardRect != null)
        {
            cardRect.localScale = Vector3.one;
            if (hasBasePosition) cardRect.anchoredPosition = baseAnchoredPosition;
        }
        gameObject.SetActive(false);
    }

    public void SetImmediateHidden()
    {
        if (activeCardRoutine != null)
        {
            DialoguePresenter.StopRunningCoroutine(activeCardRoutine);
            activeCardRoutine = null;
        }
        ResetVisualState();
    }

    /// <summary>
    /// Restores the card's active visual state with the existing text and key bindings
    /// after an overlay, modal, or dialogue closes.
    /// </summary>
    public void RestoreVisible()
    {
        EnsureBannerActive();
        if (cardCanvasGroup != null)
        {
            cardCanvasGroup.alpha = 1f;
            cardCanvasGroup.interactable = true;
            cardCanvasGroup.blocksRaycasts = true;
        }
        if (textCanvasGroup != null) textCanvasGroup.alpha = 1f;
        if (cardRect != null)
        {
            cardRect.localScale = Vector3.one;
            if (hasBasePosition) cardRect.anchoredPosition = baseAnchoredPosition;
        }
        CurrentState = BannerState.Visible;
        isIdlePulsing = true;
    }

    public void EnsureBannerActive()
    {
        Transform p = transform.parent;
        while (p != null)
        {
            if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
            p = p.parent;
        }
        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    /// <summary>
    /// Displays a tip card with automatic renumbering and full entrance animation.
    /// Order: Content & Keycaps -> Layout Rebuild -> Activate -> Animate.
    /// </summary>
    public void ShowTip(ControlTipsData.TipConfig config, int stepNumber = 0, int totalSteps = 0)
    {
        bool hasTitle = config != null && !string.IsNullOrWhiteSpace(config.title);
        bool hasInstruction = config != null && !string.IsNullOrWhiteSpace(config.instruction);
        if (!hasTitle && !hasInstruction)
        {
            SetImmediateHidden();
            return;
        }

        if (activeCardRoutine != null)
        {
            DialoguePresenter.StopRunningCoroutine(activeCardRoutine);
            activeCardRoutine = null;
        }

        CurrentState = BannerState.Showing;

        if (!hasBasePosition && cardRect != null)
        {
            baseAnchoredPosition = cardRect.anchoredPosition;
            hasBasePosition = true;
        }

        // 1. Step Counter (Gold, auto-numbered)
        if (stepLabelText != null)
        {
            stepLabelText.color = goldStepColor;
            stepLabelText.fontSize = 18f;
            stepLabelText.textWrappingMode = TextWrappingModes.NoWrap;
            stepLabelText.overflowMode = TextOverflowModes.Overflow;
            if (stepNumber > 0 && totalSteps > 0)
            {
                stepLabelText.text = $"STEP {stepNumber} OF {totalSteps}";
            }
            else if (config != null && !string.IsNullOrWhiteSpace(config.stepLabel))
            {
                stepLabelText.text = config.stepLabel.ToUpper();
            }
        }

        // 2. Title & Instruction (support single-line text or standard two-line)
        if (hasTitle && !hasInstruction)
        {
            if (titleText != null)
            {
                titleText.gameObject.SetActive(true);
                titleText.text = config.title;
                titleText.fontSize = 28f;
                titleText.color = Color.white;
                titleText.enableAutoSizing = false;
                titleText.textWrappingMode = TextWrappingModes.Normal;
                titleText.overflowMode = TextOverflowModes.Overflow;
            }
            if (instructionText != null) instructionText.gameObject.SetActive(false);
        }
        else if (!hasTitle && hasInstruction)
        {
            if (titleText != null)
            {
                titleText.gameObject.SetActive(true);
                titleText.text = config.instruction;
                titleText.fontSize = 28f;
                titleText.color = Color.white;
                titleText.enableAutoSizing = false;
                titleText.textWrappingMode = TextWrappingModes.Normal;
                titleText.overflowMode = TextOverflowModes.Overflow;
            }
            if (instructionText != null) instructionText.gameObject.SetActive(false);
        }
        else
        {
            if (titleText != null)
            {
                titleText.gameObject.SetActive(true);
                titleText.enableAutoSizing = false;
                titleText.fontSize = 28f;
                titleText.color = Color.white;
                titleText.textWrappingMode = TextWrappingModes.Normal;
                titleText.overflowMode = TextOverflowModes.Overflow;
                titleText.text = config.title;
            }
            if (instructionText != null)
            {
                instructionText.gameObject.SetActive(true);
                instructionText.text = config.instruction;
                instructionText.color = normalTextColor;
                instructionText.fontSize = 22f;
                instructionText.enableAutoSizing = false;
                instructionText.textWrappingMode = TextWrappingModes.Normal;
                instructionText.overflowMode = TextOverflowModes.Overflow;
            }
        }

        // 3. Key setup: WASD Cluster vs Single Key vs No Key
        isWASDActive = config != null && config.isWASDCluster;
        bool hasKey = isWASDActive || (config != null && config.keyIcon != null) || (config != null && !string.IsNullOrEmpty(config.keyLabelOverride));

        if (keyImage != null)
        {
            keyImage.gameObject.SetActive(hasKey);
            keyImage.enabled = hasKey && !isWASDActive;
            if (keyRect != null) keyRect.localScale = Vector3.one;
        }

        if (isWASDActive)
        {
            if (wasdClusterRoot != null) wasdClusterRoot.SetActive(true);
            if (keyLabelText != null) keyLabelText.gameObject.SetActive(false);
        }
        else
        {
            if (wasdClusterRoot != null) wasdClusterRoot.SetActive(false);
            if (hasKey && keyImage != null)
            {
                Sprite targetSprite = config != null ? config.keyIcon : null;
                if (targetSprite != null)
                {
                    keyImage.sprite = targetSprite;
                    keyImage.color = normalKeyColor;
                    keyImage.preserveAspect = true;
                }
            }
            if (keyLabelText != null)
            {
                bool hasLabel = hasKey && !string.IsNullOrEmpty(config?.keyLabelOverride);
                // If a non-blank pre-labeled key sprite is used (e.g. key_tab, key_shift, key_space), suppress duplicate text
                if (hasLabel && config?.keyIcon != null && !config.keyIcon.name.ToLower().Contains("blank"))
                {
                    hasLabel = false;
                }
                keyLabelText.gameObject.SetActive(hasLabel);
                if (hasLabel)
                {
                    keyLabelText.text = config.keyLabelOverride;
                    keyLabelText.color = new Color(0.106f, 0.106f, 0.106f, 1f); // #1B1B1B matching KeyCapPrompt / E Continue prompt
                    keyLabelText.fontStyle = FontStyles.Bold;
                    keyLabelText.enableAutoSizing = true;
                    keyLabelText.fontSizeMin = 12f;
                    keyLabelText.fontSizeMax = 22f;
                    keyLabelText.alignment = TextAlignmentOptions.Center;
                    keyLabelText.margin = new Vector4(4f, 2f, 4f, 6f);
                }
                else
                {
                    keyLabelText.text = string.Empty;
                }
            }
        }

        // Apply dedicated card layout (strictly two columns, no progress dots)
        EnsureCardLayoutStructure(config, isWASDActive);

        if (checkmarkImage != null) checkmarkImage.gameObject.SetActive(false);
        if (flashOverlay != null) flashOverlay.color = new Color(0.2f, 0.9f, 0.3f, 0f);

        // Layout rebuild FIRST before activation
        var contentArea = transform.Find("ContentArea") as RectTransform;
        var keyContainer = transform.Find("KeyContainer") as RectTransform;
        if (contentArea != null) LayoutRebuilder.ForceRebuildLayoutImmediate(contentArea);
        if (keyContainer != null) LayoutRebuilder.ForceRebuildLayoutImmediate(keyContainer);
        if (cardRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(cardRect);

        // Register with HudVisibilityService as owned
        if (cardCanvasGroup != null)
        {
            HudVisibilityService.Instance?.RegisterOwnedElement(cardCanvasGroup);
        }

        // THEN activate
        EnsureBannerActive();

        // THEN animate
        activeCardRoutine = DialoguePresenter.RunCoroutine(EntranceRoutine());
    }

    private void EnsureCardLayoutStructure(ControlTipsData.TipConfig config, bool isWASD)
    {
        if (cardRect == null) cardRect = GetComponent<RectTransform>();

        // Clean up any progress dot rows completely
        var leftoverDots = transform.Find("ContentArea/DotsRow");
        if (leftoverDots != null) Destroy(leftoverDots.gameObject);
        leftoverDots = transform.Find("DotsRow");
        if (leftoverDots != null) Destroy(leftoverDots.gameObject);

        // 1. Root card HorizontalLayoutGroup (padding 20/18, spacing 20, MiddleLeft)
        var hlg = cardRect.GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = cardRect.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(20, 20, 18, 18);
        hlg.spacing = 20f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var csf = cardRect.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = cardRect.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        cardRect.anchorMin = new Vector2(1f, 1f);
        cardRect.anchorMax = new Vector2(1f, 1f);
        cardRect.pivot = new Vector2(1f, 1f);
        cardRect.anchoredPosition = new Vector2(-40f, -280f);
        baseAnchoredPosition = cardRect.anchoredPosition;
        hasBasePosition = true;
        cardRect.sizeDelta = new Vector2(560f, Mathf.Max(120f, cardRect.sizeDelta.y));

        var cardLe = cardRect.GetComponent<LayoutElement>();
        if (cardLe == null) cardLe = cardRect.gameObject.AddComponent<LayoutElement>();
        cardLe.minWidth = 520f;
        cardLe.preferredWidth = 560f;
        cardLe.minHeight = 120f;

        // 2. Ignore layout on overlay decorations
        if (flashOverlay != null)
        {
            var fle = flashOverlay.GetComponent<LayoutElement>();
            if (fle == null) fle = flashOverlay.gameObject.AddComponent<LayoutElement>();
            fle.ignoreLayout = true;
        }
        if (checkmarkImage != null)
        {
            var cle = checkmarkImage.GetComponent<LayoutElement>();
            if (cle == null) cle = checkmarkImage.gameObject.AddComponent<LayoutElement>();
            cle.ignoreLayout = true;
        }

        // 3. Left column: KeyContainer (KeyColumn)
        var keyContainer = transform.Find("KeyContainer") as RectTransform;
        if (keyContainer != null)
        {
            var kle = keyContainer.GetComponent<LayoutElement>();
            if (kle == null) kle = keyContainer.gameObject.AddComponent<LayoutElement>();
            kle.ignoreLayout = false;

            float keyColWidth = 56f;
            float keyColHeight = 56f;

            bool isWide = false;
            if (config != null)
            {
                if (!string.IsNullOrEmpty(config.keyLabelOverride))
                {
                    string lbl = config.keyLabelOverride.Trim().ToUpper();
                    if (lbl.Length == 1)
                    {
                        isWide = false;
                    }
                    else if (lbl.Contains("SHIFT") || lbl.Contains("CTRL") || lbl.Contains("TAB") || lbl.Contains("SPACE") || lbl.Contains("ESC"))
                    {
                        isWide = true;
                    }
                }
                else if (config.keyIcon != null)
                {
                    string iconName = config.keyIcon.name.ToUpper();
                    if (iconName.Contains("SHIFT") || iconName.Contains("CTRL") || iconName.Contains("TAB") || iconName.Contains("SPACE") || iconName.Contains("ESC"))
                    {
                        isWide = true;
                    }
                }
            }

            if (isWASD)
            {
                keyColWidth = 168f;
                keyColHeight = 112f;

                if (wasdClusterRoot != null)
                {
                    wasdClusterRoot.SetActive(true);
                    var wasdRt = wasdClusterRoot.GetComponent<RectTransform>();
                    if (wasdRt != null)
                    {
                        wasdRt.sizeDelta = new Vector2(168f, 112f);
                        wasdRt.localScale = Vector3.one;
                        wasdRt.anchoredPosition = Vector2.zero;
                    }

                    // 3-key arrangement: W top centered, A S D below (cells 52x52, spacing 6)
                    if (wKeyRect != null) { wKeyRect.sizeDelta = new Vector2(52f, 52f); wKeyRect.localScale = Vector3.one; wKeyRect.anchoredPosition = new Vector2(0f, 29f); }
                    if (aKeyRect != null) { aKeyRect.sizeDelta = new Vector2(52f, 52f); aKeyRect.localScale = Vector3.one; aKeyRect.anchoredPosition = new Vector2(-58f, -29f); }
                    if (sKeyRect != null) { sKeyRect.sizeDelta = new Vector2(52f, 52f); sKeyRect.localScale = Vector3.one; sKeyRect.anchoredPosition = new Vector2(0f, -29f); }
                    if (dKeyRect != null) { dKeyRect.sizeDelta = new Vector2(52f, 52f); dKeyRect.localScale = Vector3.one; dKeyRect.anchoredPosition = new Vector2(58f, -29f); }
                }
            }
            else if (config != null && (config.keyIcon != null || !string.IsNullOrEmpty(config.keyLabelOverride)))
            {
                keyColHeight = 56f;
                if (isWide)
                {
                    float autoWidth = 96f;
                    if (keyLabelText != null && keyLabelText.gameObject.activeSelf && !string.IsNullOrEmpty(config.keyLabelOverride))
                    {
                        autoWidth = Mathf.Max(96f, keyLabelText.preferredWidth + 32f);
                    }
                    else if (keyImage != null && keyImage.sprite != null && keyImage.sprite.rect.height > 0f)
                    {
                        float aspect = keyImage.sprite.rect.width / keyImage.sprite.rect.height;
                        autoWidth = Mathf.Max(96f, 56f * aspect);
                    }
                    keyColWidth = autoWidth;
                }
                else
                {
                    keyColWidth = 56f;
                }

                if (keyRect != null)
                {
                    keyRect.localScale = Vector3.one;
                    keyRect.sizeDelta = new Vector2(keyColWidth, keyColHeight);
                }
            }
            else
            {
                keyColWidth = 0f;
                keyColHeight = 0f;
            }

            kle.minWidth = keyColWidth;
            kle.preferredWidth = keyColWidth;
            kle.flexibleWidth = 0f;
            kle.minHeight = keyColHeight;
            kle.preferredHeight = keyColHeight;
            kle.flexibleHeight = 0f;

            keyContainer.sizeDelta = new Vector2(keyColWidth, keyColHeight);
            keyContainer.localScale = Vector3.one;
            keyContainer.gameObject.SetActive(keyColWidth > 0f);
        }

        // 4. Right column: ContentArea (TextColumn)
        var contentArea = transform.Find("ContentArea") as RectTransform;
        if (contentArea != null)
        {
            var cle = contentArea.GetComponent<LayoutElement>();
            if (cle == null) cle = contentArea.gameObject.AddComponent<LayoutElement>();
            cle.minWidth = 320f;
            cle.flexibleWidth = 1f;

            var vlg = contentArea.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = contentArea.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(0, 0, 0, 0);

            var ccsf = contentArea.GetComponent<ContentSizeFitter>();
            if (ccsf == null) ccsf = contentArea.gameObject.AddComponent<ContentSizeFitter>();
            ccsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            ccsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    private IEnumerator EntranceRoutine()
    {
        float duration = openDuration;
        float elapsed = 0f;

        Vector2 startPos = baseAnchoredPosition - new Vector2(0f, 14f);
        Vector3 beginScale = new Vector3(startScale, startScale, 1f);

        cardRect.anchoredPosition = startPos;
        cardRect.localScale = beginScale;
        cardCanvasGroup.alpha = 0f;
        cardCanvasGroup.interactable = false;
        cardCanvasGroup.blocksRaycasts = false;

        if (textCanvasGroup != null) textCanvasGroup.alpha = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Smooth ease-out
            float ease = 1f - Mathf.Pow(1f - t, 3f);

            cardCanvasGroup.alpha = Mathf.Lerp(0f, 1f, ease);
            cardRect.anchoredPosition = Vector2.Lerp(startPos, baseAnchoredPosition, ease);
            cardRect.localScale = Vector3.Lerp(beginScale, Vector3.one, ease);

            if (textCanvasGroup != null)
            {
                textCanvasGroup.alpha = Mathf.Clamp01(t * 1.5f);
            }

            yield return null;
        }

        cardCanvasGroup.alpha = 1f;
        cardCanvasGroup.interactable = true;
        cardCanvasGroup.blocksRaycasts = true;
        cardRect.anchoredPosition = baseAnchoredPosition;
        cardRect.localScale = Vector3.one;
        if (textCanvasGroup != null) textCanvasGroup.alpha = 1f;

        CurrentState = BannerState.Visible;
        isIdlePulsing = true;
        activeCardRoutine = null;
    }

    private void Update()
    {
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
        if (string.IsNullOrWhiteSpace(doneMessage))
        {
            doneMessage = "Great job!";
        }

        EnsureBannerActive();
        if (activeCardRoutine != null)
        {
            DialoguePresenter.StopRunningCoroutine(activeCardRoutine);
            activeCardRoutine = null;
        }

        CurrentState = BannerState.Hiding;
        activeCardRoutine = DialoguePresenter.RunCoroutine(CompleteRoutine(doneMessage, onFinished));
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

        // Green flash + checkmark bounce (0.40s)
        float popTime = 0.40f;
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

        // Keep cheer visible for 0.8s so completion message and checkmark are clearly seen
        yield return new WaitForSecondsRealtime(0.8f);

        // Fade out and scale/slide down over closeDuration (ease-in)
        float exitDuration = closeDuration;
        elapsed = 0f;
        Vector2 startPos = cardRect != null ? cardRect.anchoredPosition : baseAnchoredPosition;
        Vector2 endPos = baseAnchoredPosition - new Vector2(0f, 14f);
        Vector3 beginScale = cardRect != null ? cardRect.localScale : Vector3.one;
        Vector3 targetScale = new Vector3(startScale, startScale, 1f);
        if (cardCanvasGroup != null)
        {
            cardCanvasGroup.interactable = false;
            cardCanvasGroup.blocksRaycasts = false;
        }

        while (elapsed < exitDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / exitDuration);
            float ease = t * t; // Ease-in down

            if (cardCanvasGroup != null) cardCanvasGroup.alpha = Mathf.Lerp(1f, 0f, ease);
            if (cardRect != null)
            {
                cardRect.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);
                cardRect.localScale = Vector3.Lerp(beginScale, targetScale, ease);
            }

            yield return null;
        }

        ResetVisualState();

        // Gap before next step (0.18s)
        yield return new WaitForSecondsRealtime(stepTransitionDelay);

        activeCardRoutine = null;
        onFinished?.Invoke();
    }

    /// <summary>
    /// Smoothly fades out and slides down the card without checkmark/cheer (used when a step ends via external trigger).
    /// </summary>
    public void DismissTip(System.Action onFinished = null)
    {
        EnsureBannerActive();
        if (activeCardRoutine != null)
        {
            DialoguePresenter.StopRunningCoroutine(activeCardRoutine);
            activeCardRoutine = null;
        }

        CurrentState = BannerState.Hiding;
        activeCardRoutine = DialoguePresenter.RunCoroutine(DismissRoutine(onFinished));
    }

    private IEnumerator DismissRoutine(System.Action onFinished)
    {
        isIdlePulsing = false;
        isKeyHeld = false;

        float exitDuration = closeDuration;
        float elapsed = 0f;
        Vector2 startPos = cardRect != null ? cardRect.anchoredPosition : baseAnchoredPosition;
        Vector2 endPos = baseAnchoredPosition - new Vector2(0f, 14f);
        Vector3 beginScale = cardRect != null ? cardRect.localScale : Vector3.one;
        Vector3 targetScale = new Vector3(startScale, startScale, 1f);
        if (cardCanvasGroup != null)
        {
            cardCanvasGroup.interactable = false;
            cardCanvasGroup.blocksRaycasts = false;
        }

        while (elapsed < exitDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / exitDuration);
            float ease = t * t;

            if (cardCanvasGroup != null) cardCanvasGroup.alpha = Mathf.Lerp(1f, 0f, ease);
            if (cardRect != null)
            {
                cardRect.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);
                cardRect.localScale = Vector3.Lerp(beginScale, targetScale, ease);
            }

            yield return null;
        }

        ResetVisualState();

        if (stepTransitionDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(stepTransitionDelay);
        }

        activeCardRoutine = null;
        onFinished?.Invoke();
    }
}
