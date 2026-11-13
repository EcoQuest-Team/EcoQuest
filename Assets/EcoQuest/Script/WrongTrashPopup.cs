using System;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Single unified Wrong-Trash notification toast styled from UITheme:
/// dark forest panel (#0B2419 @ 97%), green border (#6DBE6D), pill tab ("WRONG BIN"),
/// bin-colored category badge, friendly header, wrapped educational hint, and 3-dot mistake counter.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class WrongTrashPopup : MonoBehaviour
{
    public enum BinCategory
    {
        Generic = 0,
        Biodegradable = 1,
        NonBiodegradable = 2,
        Recyclable = 3,
        Penalty = 4
    }

    public struct ParsedWrongTrashMessage
    {
        public BinCategory Category;
        public string Header;
        public string Body;
        public int MistakeCount;
        public int MaxMistakes;
        public string MistakeLabel;
        public bool IsPenalty;
    }

    private static WrongTrashPopup _instance;
    public static WrongTrashPopup Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<WrongTrashPopup>(FindObjectsInactive.Include);
                if (_instance != null && !_instance.gameObject.activeSelf)
                {
                    _instance.gameObject.SetActive(true);
                }
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapOnSceneLoad()
    {
        if (_instance == null)
        {
            _instance = FindFirstObjectByType<WrongTrashPopup>(FindObjectsInactive.Include);
        }
        if (_instance != null && !_instance.gameObject.activeSelf)
        {
            _instance.gameObject.SetActive(true);
        }
    }

    [Header("UI References")]
    [SerializeField] private Button closeButton;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text hintText;
    [SerializeField] private GameObject hintRibbon;

    [Header("Timing (Unscaled Time)")]
    [SerializeField] private float showDuration = 4.0f;
    [SerializeField] private float popInDuration = 0.22f;
    [SerializeField] private float popOutDuration = 0.22f;

    [Header("Slide & Scale Settings")]
    [SerializeField] private float slideDistance = 36f;
    [SerializeField] private float popOutSlideDistance = 24f;
    [SerializeField] private float startScale = 0.96f;
    [SerializeField] private float popOutScale = 0.97f;

    // Preserved serialized fields for existing scene asset compatibility (not used for jitter float)
    [SerializeField, HideInInspector] private float floatAmplitude = 0f;
    [SerializeField, HideInInspector] private float floatSpeed = 0f;
    [SerializeField] private AnimationCurve popInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve popOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private static readonly Regex MistakeFractionRegex = new Regex(@"Mistake\s*(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PenaltyHeaderRegex = new Regex(@"^\s*3\s*Mistakes\s*reached\s*[!.]*\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LeadingHintRegex = new Regex(@"^\s*(?:Hint\s*:\s*|Oops\s*[!.]*\s*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const float MinCardHeight = 122f;
    private const float MaxCardHeight = 200f;
    private const float TopRestingOffset = -34f;

    private RectTransform rectTransform;
    private Image cardBackgroundImage;
    private Image pillTabImage;
    private TMP_Text pillTabText;
    private Image badgeBgImage;
    private Image badgeIconImage;
    private TMP_Text headerText;
    private TMP_Text bodyText;
    private TMP_Text mistakeLabelText;
    private Image[] mistakeDots;

    private Vector2 restingPosition;
    private Coroutine activeAnimationCoroutine;
    private bool isVisible = false;
    public bool IsVisible => isVisible;
    public static bool IsAnyVisible => _instance != null && _instance.isVisible && _instance.gameObject.activeInHierarchy;
    private int lastKnownMistakeCount = 0;
    private ParsedWrongTrashMessage lastDisplayedMessage;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        if (closeButton != null && closeButton.gameObject != gameObject)
        {
            Destroy(closeButton.gameObject);
            closeButton = null;
        }

        if (hintRibbon != null && hintRibbon != gameObject)
        {
            Destroy(hintRibbon);
            hintRibbon = null;
            hintText = null;
        }

        rectTransform = GetComponent<RectTransform>();
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        EnsureCardVisuals();
        ApplyParsedContent(ParseNotification("Put this in the matching colored bin!", 0));
        ResetToHidden();
    }

    private void OnEnable()
    {
        EnsureCardVisuals();
        if (!isVisible && string.IsNullOrEmpty(lastDisplayedMessage.Body))
        {
            ApplyParsedContent(ParseNotification("Put this in the matching colored bin!", lastKnownMistakeCount));
        }
    }

    private void OnDisable()
    {
        StopActiveAnimation();
        isVisible = false;
    }

    private void OnDestroy()
    {
        StopActiveAnimation();
        if (_instance == this)
        {
            _instance = null;
        }
    }

    public void Show(string message = null)
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        }

        EnsureCardVisuals();

        ParsedWrongTrashMessage parsed = ParseNotification(message, lastKnownMistakeCount);
        lastKnownMistakeCount = parsed.IsPenalty ? 0 : parsed.MistakeCount;
        lastDisplayedMessage = parsed;
        ApplyParsedContent(parsed);

        StopActiveAnimation();
        AudioManager.Instance?.PlayTrashDepositWrong();
        if (isVisible)
        {
            activeAnimationCoroutine = StartCoroutine(PulseAndHoldRoutine());
        }
        else
        {
            activeAnimationCoroutine = StartCoroutine(PopInSequenceRoutine());
        }
    }

    public void Hide()
    {
        if (!isVisible) return;
        isVisible = false;
        StopActiveAnimation();

        if (gameObject.activeInHierarchy)
        {
            activeAnimationCoroutine = StartCoroutine(PopOutRoutine());
        }
        else
        {
            ResetToHidden();
        }
    }

    public ParsedWrongTrashMessage GetCurrentStateForTest() => lastDisplayedMessage;

    public static ParsedWrongTrashMessage ParseNotification(string rawMessage, int previousMistakeCount = 0)
    {
        string sanitized = SanitizeSupportedGlyphs(rawMessage);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Put this trash in its matching colored bin.";
        }

        int mistakeCount = Mathf.Clamp(previousMistakeCount, 0, 3);
        int maxMistakes = 3;
        bool isPenalty = sanitized.IndexOf("3 Mistakes reached", StringComparison.OrdinalIgnoreCase) >= 0
            || sanitized.IndexOf("Eco-Coins deducted", StringComparison.OrdinalIgnoreCase) >= 0;

        Match m = MistakeFractionRegex.Match(sanitized);
        if (m.Success)
        {
            if (int.TryParse(m.Groups[1].Value, out int parsedCount))
                mistakeCount = Mathf.Clamp(parsedCount, 0, 3);
            if (int.TryParse(m.Groups[2].Value, out int parsedMax) && parsedMax > 0)
                maxMistakes = parsedMax;

            sanitized = MistakeFractionRegex.Replace(sanitized, string.Empty);
        }
        else if (isPenalty)
        {
            mistakeCount = 3;
        }

        if (isPenalty)
        {
            sanitized = PenaltyHeaderRegex.Replace(sanitized, string.Empty);
        }

        sanitized = LeadingHintRegex.Replace(sanitized, string.Empty);
        sanitized = Regex.Replace(sanitized, @"\s*[•|]\s*$", string.Empty).Trim();
        sanitized = Regex.Replace(sanitized, @"\s*[•|—-]+\s*$", string.Empty).Trim();
        sanitized = Regex.Replace(sanitized, @"\s{2,}", " ");

        if (sanitized.EndsWith("!", StringComparison.Ordinal))
        {
            sanitized = sanitized.TrimEnd('!').TrimEnd() + ".";
        }
        else if (sanitized.Length > 0 && sanitized[sanitized.Length - 1] != '.' && sanitized[sanitized.Length - 1] != '?')
        {
            sanitized += ".";
        }

        BinCategory category = DetectCategory(sanitized, isPenalty);
        string header = isPenalty ? "Oops! 3 mistakes reached." : "Oops! Not this bin.";
        string mistakeLabel = mistakeCount > 0
            ? (isPenalty ? "Mistake 3 of 3" : $"Mistake {mistakeCount} of {maxMistakes}")
            : $"0 of {maxMistakes} mistakes";

        return new ParsedWrongTrashMessage
        {
            Category = category,
            Header = header,
            Body = sanitized,
            MistakeCount = mistakeCount,
            MaxMistakes = maxMistakes,
            MistakeLabel = mistakeLabel,
            IsPenalty = isPenalty
        };
    }

    private static BinCategory DetectCategory(string message, bool isPenalty)
    {
        if (isPenalty) return BinCategory.Penalty;
        if (string.IsNullOrEmpty(message)) return BinCategory.Generic;

        string lower = message.ToLowerInvariant();
        if (lower.Contains("non-biodegradable") || lower.Contains("yellow bin") || lower.Contains("hindi nabubulok"))
            return BinCategory.NonBiodegradable;
        if (lower.Contains("biodegradable") || lower.Contains("green bin") || lower.Contains("nabubulok"))
            return BinCategory.Biodegradable;
        if (lower.Contains("recyclable") || lower.Contains("blue bin") || lower.Contains("pwedeng gamitin"))
            return BinCategory.Recyclable;

        return BinCategory.Generic;
    }

    private static string SanitizeSupportedGlyphs(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsSurrogate(c))
            {
                if (i + 1 < input.Length && char.IsLowSurrogate(input[i + 1])) i++;
                continue;
            }
            if (c == '\uFE0F' || c == '\u26A0') continue;
            if (c == '\u2014' || c == '\u2013')
            {
                sb.Append('-');
                continue;
            }
            if (c <= 0x00FF)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private void EnsureCardVisuals()
    {
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null) return;

        // Clean any leftover legacy child (e.g. old CloseButton or runtime HintRibbon)
        Transform legacyClose = transform.Find("CloseButton");
        if (legacyClose != null) Destroy(legacyClose.gameObject);
        Transform legacyRibbon = transform.Find("HintRibbon");
        if (legacyRibbon != null) Destroy(legacyRibbon.gameObject);

        float parentW = rectTransform.parent is RectTransform prt && prt.rect.width > 10f ? prt.rect.width : 1920f;
        float cardWidth = Mathf.Clamp(parentW * 0.48f, 560f, 670f);

        rectTransform.anchorMin = new Vector2(0.5f, 1f);
        rectTransform.anchorMax = new Vector2(0.5f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 1f);
        restingPosition = new Vector2(0f, TopRestingOffset);
        rectTransform.sizeDelta = new Vector2(cardWidth, Mathf.Max(MinCardHeight, rectTransform.sizeDelta.y));

        cardBackgroundImage = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        if (UITheme.Instance != null && UITheme.Instance.panelBackgroundSprite != null)
        {
            cardBackgroundImage.sprite = UITheme.Instance.panelBackgroundSprite;
            cardBackgroundImage.type = Image.Type.Sliced;
            cardBackgroundImage.color = Color.white;
        }
        else
        {
            cardBackgroundImage.color = new Color(0.043f, 0.141f, 0.098f, 0.97f);
        }
        cardBackgroundImage.preserveAspect = false;
        cardBackgroundImage.raycastTarget = false;

        if (headerText != null && bodyText != null && pillTabImage != null && mistakeDots != null)
        {
            return;
        }

        // Remove any stray unowned TMP children before creating clean structure once
        Sprite roundedSprite = UITheme.Instance != null && UITheme.Instance.roundedButtonSprite != null
            ? UITheme.Instance.roundedButtonSprite
            : ResponsiveTextButton.GetOrCreateRuntimeRoundedSprite();
        TMP_FontAsset titleFont = UITheme.Instance != null ? UITheme.Instance.GetTitleFont() : null;
        TMP_FontAsset bodyFont = UITheme.Instance != null ? UITheme.Instance.GetBodyFont() : null;

        // 1. Pill Tab ("WRONG BIN") overlapping top-left corner matching Dialogue & Quiz
        pillTabImage = FindOrCreateImageChild("PillTab", rectTransform, out RectTransform tabRt);
        tabRt.anchorMin = new Vector2(0f, 1f);
        tabRt.anchorMax = new Vector2(0f, 1f);
        tabRt.pivot = new Vector2(0f, 0.5f);
        tabRt.anchoredPosition = new Vector2(24f, 0f);
        tabRt.sizeDelta = new Vector2(168f, 34f);
        if (UITheme.Instance != null && UITheme.Instance.speakerTabGreenSprite != null)
        {
            pillTabImage.sprite = UITheme.Instance.speakerTabGreenSprite;
            pillTabImage.type = Image.Type.Sliced;
            pillTabImage.color = Color.white;
        }
        else
        {
            pillTabImage.sprite = roundedSprite;
            pillTabImage.type = Image.Type.Sliced;
            pillTabImage.color = UITheme.Instance != null ? UITheme.Instance.accentColor : new Color(0.180f, 0.490f, 0.310f, 1f);
        }

        pillTabText = FindOrCreateTmpChild("TabText", tabRt, out RectTransform tabTxtRt);
        StretchFill(tabTxtRt, 8f, 8f, 2f, 2f);
        if (titleFont != null) pillTabText.font = titleFont;
        pillTabText.fontSize = 18f;
        pillTabText.fontStyle = FontStyles.Bold;
        pillTabText.alignment = TextAlignmentOptions.Center;
        pillTabText.color = Color.white;
        pillTabText.text = "WRONG BIN";

        // 2. Left Bin-Colored Icon Badge (58x58)
        badgeBgImage = FindOrCreateImageChild("CategoryBadge", rectTransform, out RectTransform badgeRt);
        badgeRt.anchorMin = new Vector2(0f, 0.5f);
        badgeRt.anchorMax = new Vector2(0f, 0.5f);
        badgeRt.pivot = new Vector2(0f, 0.5f);
        badgeRt.anchoredPosition = new Vector2(22f, -6f);
        badgeRt.sizeDelta = new Vector2(58f, 58f);
        badgeBgImage.sprite = roundedSprite;
        badgeBgImage.type = Image.Type.Sliced;

        badgeIconImage = FindOrCreateImageChild("CategoryIcon", badgeRt, out RectTransform iconRt);
        StretchFill(iconRt, 11f, 11f, 11f, 11f);
        badgeIconImage.preserveAspect = true;
        badgeIconImage.color = Color.white;

        // 3. Header ("Oops! Not this bin.")
        headerText = FindOrCreateTmpChild("HeaderText", rectTransform, out RectTransform headRt);
        headRt.anchorMin = new Vector2(0f, 1f);
        headRt.anchorMax = new Vector2(1f, 1f);
        headRt.pivot = new Vector2(0.5f, 1f);
        headRt.offsetMin = new Vector2(96f, -54f);
        headRt.offsetMax = new Vector2(-156f, -22f);
        if (titleFont != null) headerText.font = titleFont;
        headerText.fontSize = 22f;
        headerText.fontStyle = FontStyles.Bold;
        headerText.alignment = TextAlignmentOptions.MidlineLeft;
        headerText.color = Color.white;
        headerText.textWrappingMode = TextWrappingModes.NoWrap;
        headerText.overflowMode = TextOverflowModes.Ellipsis;

        // 4. Educational Hint Body (Category + Filipino wording, Item 6 responsive wrap + height expansion)
        bodyText = FindOrCreateTmpChild("BodyText", rectTransform, out RectTransform bodyRt);
        bodyRt.anchorMin = new Vector2(0f, 0f);
        bodyRt.anchorMax = new Vector2(1f, 1f);
        bodyRt.pivot = new Vector2(0.5f, 0.5f);
        bodyRt.offsetMin = new Vector2(96f, 14f);
        bodyRt.offsetMax = new Vector2(-156f, -56f);
        if (bodyFont != null) bodyText.font = bodyFont;
        bodyText.fontSize = 18f;
        bodyText.enableAutoSizing = true;
        bodyText.fontSizeMin = 14.5f;
        bodyText.fontSizeMax = 18.5f;
        bodyText.lineSpacing = 1f;
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.color = UITheme.Instance != null ? UITheme.Instance.textMuted : new Color(0.89f, 0.96f, 0.89f, 1f);
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.overflowMode = TextOverflowModes.Ellipsis;
        hintText = bodyText;

        // 5. Right Mistake Counter Column (3 Dots + "Mistake X of 3")
        Transform meterTr = transform.Find("MistakeMeter");
        RectTransform meterRt;
        if (meterTr != null)
        {
            meterRt = meterTr as RectTransform;
        }
        else
        {
            var meterGo = new GameObject("MistakeMeter", typeof(RectTransform));
            meterGo.transform.SetParent(rectTransform, false);
            meterRt = meterGo.GetComponent<RectTransform>();
        }
        meterRt.anchorMin = new Vector2(1f, 0.5f);
        meterRt.anchorMax = new Vector2(1f, 0.5f);
        meterRt.pivot = new Vector2(1f, 0.5f);
        meterRt.anchoredPosition = new Vector2(-20f, -4f);
        meterRt.sizeDelta = new Vector2(126f, 64f);

        mistakeDots = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            mistakeDots[i] = FindOrCreateImageChild($"Dot_{i}", meterRt, out RectTransform dotRt);
            dotRt.anchorMin = new Vector2(0.5f, 0.5f);
            dotRt.anchorMax = new Vector2(0.5f, 0.5f);
            dotRt.pivot = new Vector2(0.5f, 0.5f);
            dotRt.anchoredPosition = new Vector2((i - 1) * 26f, 12f);
            dotRt.sizeDelta = new Vector2(16f, 16f);
            mistakeDots[i].sprite = roundedSprite;
            mistakeDots[i].type = Image.Type.Sliced;
        }

        mistakeLabelText = FindOrCreateTmpChild("MistakeLabel", meterRt, out RectTransform lblRt);
        lblRt.anchorMin = new Vector2(0f, 0f);
        lblRt.anchorMax = new Vector2(1f, 0.5f);
        lblRt.offsetMin = Vector2.zero;
        lblRt.offsetMax = new Vector2(0f, -2f);
        if (bodyFont != null) mistakeLabelText.font = bodyFont;
        mistakeLabelText.fontSize = 15f;
        mistakeLabelText.fontStyle = FontStyles.Bold;
        mistakeLabelText.alignment = TextAlignmentOptions.Center;
        mistakeLabelText.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private void ApplyParsedContent(ParsedWrongTrashMessage parsed)
    {
        if (headerText != null) headerText.text = parsed.Header;
        if (bodyText != null) bodyText.text = parsed.Body;

        Color accent = ResolveCategoryColor(parsed.Category);
        if (badgeBgImage != null) badgeBgImage.color = accent;
        if (badgeIconImage != null)
        {
            Sprite icon = ResolveCategoryIcon(parsed.Category);
            badgeIconImage.sprite = icon;
            badgeIconImage.enabled = icon != null;
        }

        if (mistakeLabelText != null)
        {
            mistakeLabelText.text = parsed.MistakeLabel;
            mistakeLabelText.color = parsed.IsPenalty || parsed.MistakeCount >= 3
                ? new Color(0.95f, 0.50f, 0.45f, 1f)
                : (UITheme.Instance != null ? UITheme.Instance.warnColor : new Color(0.949f, 0.757f, 0.306f, 1f));
        }

        if (mistakeDots != null)
        {
            Color activeDotColor = parsed.IsPenalty || parsed.MistakeCount >= 3
                ? new Color(0.878f, 0.353f, 0.278f, 1f)
                : (UITheme.Instance != null ? UITheme.Instance.warnColor : new Color(0.949f, 0.757f, 0.306f, 1f));
            Color dimDotColor = new Color(0.18f, 0.34f, 0.26f, 0.85f);

            for (int i = 0; i < mistakeDots.Length; i++)
            {
                if (mistakeDots[i] == null) continue;
                mistakeDots[i].color = i < parsed.MistakeCount ? activeDotColor : dimDotColor;
            }
        }

        // Responsive card height if body text wraps onto >2 lines (never clips)
        if (rectTransform != null && bodyText != null)
        {
            float cardW = rectTransform.sizeDelta.x > 10f ? rectTransform.sizeDelta.x : 660f;
            float bodyAvailW = Mathf.Max(220f, cardW - 96f - 156f);
            bodyText.enableAutoSizing = false;
            bodyText.fontSize = 15.5f;
            float neededH = bodyText.GetPreferredValues(parsed.Body, bodyAvailW, 0f).y;
            bodyText.enableAutoSizing = true;

            float targetH = Mathf.Clamp(Mathf.Ceil(neededH) + 74f, MinCardHeight, MaxCardHeight);
            rectTransform.sizeDelta = new Vector2(cardW, targetH);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }
    }

    private static Color ResolveCategoryColor(BinCategory cat)
    {
        switch (cat)
        {
            case BinCategory.Biodegradable:
                return new Color(0.180f, 0.545f, 0.341f, 1f); // Green
            case BinCategory.Recyclable:
                return new Color(0.176f, 0.498f, 0.827f, 1f); // Blue
            case BinCategory.NonBiodegradable:
                return new Color(0.851f, 0.608f, 0.149f, 1f); // Amber/Yellow
            case BinCategory.Penalty:
            case BinCategory.Generic:
            default:
                return UITheme.Instance != null ? UITheme.Instance.errorColor : new Color(0.702f, 0.255f, 0.235f, 1f);
        }
    }

    private static Sprite ResolveCategoryIcon(BinCategory cat)
    {
        if (UITheme.Instance == null) return null;
        switch (cat)
        {
            case BinCategory.Biodegradable:
                return UITheme.Instance.categoryBioIcon;
            case BinCategory.NonBiodegradable:
                return UITheme.Instance.categoryNonBioIcon;
            case BinCategory.Recyclable:
                return UITheme.Instance.categoryRecycleIcon;
            case BinCategory.Penalty:
            case BinCategory.Generic:
            default:
                return UITheme.Instance.categoryHazardIcon;
        }
    }

    private void StopActiveAnimation()
    {
        if (activeAnimationCoroutine != null)
        {
            StopCoroutine(activeAnimationCoroutine);
            activeAnimationCoroutine = null;
        }
    }

    private IEnumerator PopInSequenceRoutine()
    {
        isVisible = true;
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        Vector2 startPos = restingPosition + new Vector2(0f, slideDistance);
        Vector3 initialScale = Vector3.one * startScale;

        rectTransform.anchoredPosition = startPos;
        rectTransform.localScale = initialScale;
        if (canvasGroup != null) canvasGroup.alpha = 0f;

        float elapsed = 0f;
        while (elapsed < popInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, popInDuration));
            float curveT = popInCurve != null ? popInCurve.Evaluate(t) : t;

            rectTransform.anchoredPosition = Vector2.LerpUnclamped(startPos, restingPosition, curveT);
            rectTransform.localScale = Vector3.LerpUnclamped(initialScale, Vector3.one, curveT);
            if (canvasGroup != null) canvasGroup.alpha = Mathf.Clamp01(t);
            yield return null;
        }

        rectTransform.anchoredPosition = restingPosition;
        rectTransform.localScale = Vector3.one;
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        yield return HoldRoutine(showDuration);
        yield return PopOutRoutine();
    }

    private IEnumerator PulseAndHoldRoutine()
    {
        isVisible = true;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        const float pulseDuration = 0.16f;
        float elapsed = 0f;
        Vector2 startPos = rectTransform.anchoredPosition;

        while (elapsed < pulseDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / pulseDuration);
            float punch = 4f * t * (1f - t);
            rectTransform.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 1.045f, punch);
            rectTransform.anchoredPosition = Vector2.Lerp(startPos, restingPosition, t);
            yield return null;
        }

        rectTransform.localScale = Vector3.one;
        rectTransform.anchoredPosition = restingPosition;

        yield return HoldRoutine(showDuration);
        yield return PopOutRoutine();
    }

    private static IEnumerator HoldRoutine(float duration)
    {
        float remaining = Mathf.Max(0.1f, duration);
        float elapsed = 0f;
        while (elapsed < remaining)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private IEnumerator PopOutRoutine()
    {
        Vector2 startPos = rectTransform.anchoredPosition;
        Vector2 endPos = restingPosition + new Vector2(0f, popOutSlideDistance);
        Vector3 startScaleVec = rectTransform.localScale;
        Vector3 endScaleVec = Vector3.one * popOutScale;
        float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;

        float elapsed = 0f;
        while (elapsed < popOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, popOutDuration));
            float curveT = popOutCurve != null ? popOutCurve.Evaluate(t) : t;

            rectTransform.anchoredPosition = Vector2.LerpUnclamped(startPos, endPos, curveT);
            rectTransform.localScale = Vector3.LerpUnclamped(startScaleVec, endScaleVec, curveT);
            if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, curveT);
            yield return null;
        }

        ResetToHidden();
        activeAnimationCoroutine = null;
    }

    private void ResetToHidden()
    {
        isVisible = false;
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = restingPosition + new Vector2(0f, slideDistance);
            rectTransform.localScale = Vector3.one * startScale;
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private static Image FindOrCreateImageChild(string childName, RectTransform parent, out RectTransform rt)
    {
        Transform existing = parent.Find(childName);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
        }
        rt = go.GetComponent<RectTransform>();
        Image img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    private static TMP_Text FindOrCreateTmpChild(string childName, RectTransform parent, out RectTransform rt)
    {
        Transform existing = parent.Find(childName);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
        }
        rt = go.GetComponent<RectTransform>();
        TMP_Text tmp = go.GetComponent<TMP_Text>() ?? go.AddComponent<TextMeshProUGUI>();
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void StretchFill(RectTransform rt, float left, float right, float top, float bottom)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }
}
