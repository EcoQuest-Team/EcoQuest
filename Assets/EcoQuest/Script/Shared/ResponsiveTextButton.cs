using System;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared responsive button helper that keeps 9-sliced backgrounds crisp, wraps label text,
/// auto-sizes down to a readable minimum before expanding height/width via LayoutElement,
/// falls back to ellipsis beyond maxHeight, and immediately rebuilds parent layouts.
/// </summary>
[DisallowMultipleComponent]
public class ResponsiveTextButton : MonoBehaviour
{
    public struct FitMetrics
    {
        public float TargetWidth;
        public float TargetHeight;
        public float FontSize;
        public bool RequiresEllipsis;
        public Vector2 MeasuredTextSize;
    }

    public struct ValidationResult
    {
        public bool Passed;
        public string Summary;
    }

    [Header("References")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private LayoutElement layoutElement;
    [SerializeField] private Sprite fallbackSlicedSprite;

    [Header("Typography & Wrapping")]
    [SerializeField] private float fontSizeMin = 16f;
    [SerializeField] private float fontSizeMax = 24f;
    [SerializeField] private bool forceFontFromTheme = false;

    [Header("Responsive Sizing")]
    [SerializeField] private bool fitWidthToText = false;
    [SerializeField] private float minWidth = 140f;
    [SerializeField] private float maxWidth = 880f;
    [SerializeField] private bool allowHeightExpansion = true;
    [SerializeField] private float minHeight = 52f;
    [SerializeField] private float maxHeight = 160f;
    [SerializeField] private bool applyRectSizeWhenUncontrolled = true;

    [Header("Internal Padding")]
    [SerializeField] private bool manageLabelMargins = true;
    [SerializeField] private float horizontalPaddingLeft = 22f;
    [SerializeField] private float horizontalPaddingRight = 22f;
    [SerializeField] private float verticalPadding = 14f;

    [Header("Background")]
    [SerializeField] private bool enforceSlicedBackground = true;
    [SerializeField] private bool replaceUnslicedSprite = true;
    [SerializeField] private bool forceSharedRoundedSprite = false;

    private static readonly Regex RichTextTagRegex = new Regex("<.*?>", RegexOptions.Compiled);
    private static Sprite _runtimeRoundedSprite;

    private bool _isRefitting;
    private string _lastAppliedText;
    private float _lastAppliedWidth = -1f;
    private FitMetrics _lastFitMetrics;

    public TMP_Text Label => label != null ? label : (label = GetComponentInChildren<TMP_Text>(true));
    public float MinHeight => minHeight;
    public float MaxHeight => maxHeight;
    public float MinWidth => minWidth;
    public float MaxWidth => maxWidth;
    public FitMetrics LastMetrics => _lastFitMetrics;

    public static ResponsiveTextButton AttachOrGet(Component target)
    {
        if (target == null) return null;
        var existing = target.GetComponent<ResponsiveTextButton>();
        return existing != null ? existing : target.gameObject.AddComponent<ResponsiveTextButton>();
    }

    private void Reset()
    {
        AutoBind();
    }

    private void Awake()
    {
        AutoBind();
        EnsureSlicedBackground();
        ApplyThemeFontIfRequested();
    }

    private void OnEnable()
    {
        RefreshFit();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (_isRefitting || !isActiveAndEnabled) return;
        var rt = transform as RectTransform;
        if (rt == null) return;

        float currentWidth = rt.rect.width;
        if (currentWidth > 1f && Mathf.Abs(currentWidth - _lastAppliedWidth) > 0.5f)
        {
            RefreshFit();
        }
    }

    public void Configure(
        TMP_Text labelRef = null,
        Image backgroundRef = null,
        float? minFont = null,
        float? maxFont = null,
        float? minBtnHeight = null,
        float? maxBtnHeight = null,
        float? minBtnWidth = null,
        float? maxBtnWidth = null,
        bool? fitWidth = null,
        bool? allowHeightGrow = null,
        float? padLeft = null,
        float? padRight = null,
        float? padVertical = null,
        bool? manageMargins = null,
        bool? useSharedRounded = null,
        Sprite slicedSprite = null)
    {
        if (labelRef != null) label = labelRef;
        if (backgroundRef != null) backgroundImage = backgroundRef;
        if (minFont.HasValue) fontSizeMin = minFont.Value;
        if (maxFont.HasValue) fontSizeMax = maxFont.Value;
        if (minBtnHeight.HasValue) minHeight = minBtnHeight.Value;
        if (maxBtnHeight.HasValue) maxHeight = Mathf.Max(minHeight, maxBtnHeight.Value);
        if (minBtnWidth.HasValue) minWidth = minBtnWidth.Value;
        if (maxBtnWidth.HasValue) maxWidth = Mathf.Max(minWidth, maxBtnWidth.Value);
        if (fitWidth.HasValue) fitWidthToText = fitWidth.Value;
        if (allowHeightGrow.HasValue) allowHeightExpansion = allowHeightGrow.Value;
        if (padLeft.HasValue) horizontalPaddingLeft = padLeft.Value;
        if (padRight.HasValue) horizontalPaddingRight = padRight.Value;
        if (padVertical.HasValue) verticalPadding = padVertical.Value;
        if (manageMargins.HasValue) manageLabelMargins = manageMargins.Value;
        if (useSharedRounded.HasValue) forceSharedRoundedSprite = useSharedRounded.Value;
        if (slicedSprite != null) fallbackSlicedSprite = slicedSprite;

        AutoBind();
        EnsureSlicedBackground();
        RefreshFit();
    }

    public void SetText(string text)
    {
        AutoBind();
        if (label != null)
        {
            label.text = text ?? string.Empty;
        }
        RefreshFit();
    }

    public FitMetrics RefreshFit()
    {
        FitMetrics result = default;
        if (_isRefitting) return result;

        AutoBind();
        if (label == null) return result;

        var rt = transform as RectTransform;
        if (rt == null) return result;

        _isRefitting = true;
        try
        {
            EnsureSlicedBackground();
            ApplyThemeFontIfRequested();
            ApplyLabelBoxMargins();

            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;

            float baseWidth = ResolveBaseButtonWidth(rt);
            float totalHPad = Mathf.Max(0f, horizontalPaddingLeft + horizontalPaddingRight);

            string textToMeasure = label.text ?? string.Empty;

            result = ComputeFitMetrics(
                textToMeasure,
                baseWidth,
                minWidth,
                maxWidth,
                minHeight,
                maxHeight,
                fontSizeMin,
                fontSizeMax,
                totalHPad,
                verticalPadding,
                fitWidthToText,
                allowHeightExpansion,
                (size, wrapWidth) => MeasureTmpSize(textToMeasure, size, wrapWidth),
                (word, size) => MeasureTmpSize(word, size, float.PositiveInfinity).x);

            label.fontSize = result.FontSize;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = result.RequiresEllipsis ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            label.ForceMeshUpdate(true);

            ApplySizing(rt, result.TargetWidth, result.TargetHeight, result.MeasuredTextSize);

            _lastFitMetrics = result;
            _lastAppliedText = textToMeasure;
            _lastAppliedWidth = result.TargetWidth;

            RebuildHierarchy(rt);
        }
        finally
        {
            _isRefitting = false;
        }

        return result;
    }

    /// <summary>
    /// Verifies that the fitted label neither truncates unexpectedly nor overflows button bounds.
    /// </summary>
    public ValidationResult ValidateFit()
    {
        AutoBind();
        if (label == null)
        {
            return new ValidationResult { Passed = false, Summary = $"{name}: missing TMP_Text label" };
        }

        var metrics = RefreshFit();
        var rt = transform as RectTransform;
        float rectW = rt != null ? rt.rect.width : 0f;
        float rectH = rt != null ? rt.rect.height : 0f;
        if (rectW <= 1f && rt != null) rectW = rt.sizeDelta.x;
        if (rectH <= 1f && rt != null) rectH = rt.sizeDelta.y;

        if (backgroundImage != null && enforceSlicedBackground && backgroundImage.sprite != null)
        {
            if (backgroundImage.type != Image.Type.Sliced || backgroundImage.sprite.border == Vector4.zero)
            {
                return new ValidationResult
                {
                    Passed = false,
                    Summary = $"{name}: background Image is not using a 9-sliced sprite"
                };
            }
        }

        if (metrics.RequiresEllipsis)
        {
            return new ValidationResult
            {
                Passed = false,
                Summary = $"{name}: text exceeded maxHeight={maxHeight:0.#} and hit Ellipsis fallback ({label.text?.Length ?? 0} chars)"
            };
        }

        float availTextW = Mathf.Max(20f, rectW - (horizontalPaddingLeft + horizontalPaddingRight));
        float availTextH = Mathf.Max(16f, rectH - verticalPadding);
        if (metrics.MeasuredTextSize.x > availTextW + 2f || metrics.MeasuredTextSize.y > availTextH + 2f)
        {
            return new ValidationResult
            {
                Passed = false,
                Summary = $"{name}: measured text ({metrics.MeasuredTextSize.x:0.#}x{metrics.MeasuredTextSize.y:0.#}) exceeds box ({availTextW:0.#}x{availTextH:0.#})"
            };
        }

        return new ValidationResult
        {
            Passed = true,
            Summary = $"{name}: OK ({rectW:0.#}x{rectH:0.#}, font={label.fontSize:0.#})"
        };
    }

    public static FitMetrics ComputeFitMetrics(
        string text,
        float currentButtonWidth,
        float minBtnWidth,
        float maxBtnWidth,
        float minBtnHeight,
        float maxBtnHeight,
        float minFontSize,
        float maxFontSize,
        float totalHorizontalPadding,
        float totalVerticalPadding,
        bool fitWidth,
        bool allowHeightGrow,
        Func<float, float, Vector2> measureWrapped,
        Func<string, float, float> measureSingleWord = null)
    {
        float safeMinFont = Mathf.Max(8f, Mathf.Min(minFontSize, maxFontSize));
        float safeMaxFont = Mathf.Max(safeMinFont, maxFontSize);
        float safeMinH = Mathf.Max(24f, minBtnHeight);
        float safeMaxH = Mathf.Max(safeMinH, maxBtnHeight);
        float safeMinW = Mathf.Max(48f, minBtnWidth);
        float safeMaxW = Mathf.Max(safeMinW, maxBtnWidth);
        float safeHPad = Mathf.Max(0f, totalHorizontalPadding);
        float safeVPad = Mathf.Max(0f, totalVerticalPadding);

        float targetWidth = currentButtonWidth > 1f
            ? Mathf.Clamp(currentButtonWidth, safeMinW, safeMaxW)
            : safeMinW;

        if (fitWidth && measureWrapped != null)
        {
            Vector2 unwrapped = measureWrapped(safeMaxFont, float.PositiveInfinity);
            targetWidth = Mathf.Clamp(Mathf.Ceil(unwrapped.x + safeHPad), safeMinW, safeMaxW);
        }

        float availTextWidth = Mathf.Max(24f, targetWidth - safeHPad);
        float minTextHeightBudget = Mathf.Max(14f, safeMinH - safeVPad);
        string longestWord = ExtractLongestWord(text);

        // Phase 1: Try shrinking font size down to minFontSize within minHeight BEFORE expanding height.
        for (float fs = safeMaxFont; fs >= safeMinFont - 0.01f; fs -= 0.5f)
        {
            float candidate = Mathf.Max(safeMinFont, fs);
            if (!LongestWordFits(longestWord, candidate, availTextWidth, measureSingleWord))
            {
                if (candidate <= safeMinFont + 0.01f) break;
                continue;
            }

            Vector2 sizeAtMinH = measureWrapped != null
                ? measureWrapped(candidate, availTextWidth)
                : Vector2.zero;

            if (sizeAtMinH.y <= minTextHeightBudget + 0.5f && sizeAtMinH.x <= availTextWidth + 0.5f)
            {
                return new FitMetrics
                {
                    TargetWidth = targetWidth,
                    TargetHeight = safeMinH,
                    FontSize = candidate,
                    RequiresEllipsis = false,
                    MeasuredTextSize = sizeAtMinH
                };
            }

            if (candidate <= safeMinFont + 0.01f) break;
        }

        // Phase 2: Wrapped text did not fit inside minHeight at minFontSize -> grow height at minFontSize up to maxHeight.
        Vector2 sizeAtMinFont = measureWrapped != null
            ? measureWrapped(safeMinFont, availTextWidth)
            : new Vector2(availTextWidth, minTextHeightBudget);

        bool longestExceedsWidth = !LongestWordFits(longestWord, safeMinFont, availTextWidth, measureSingleWord);
        float desiredHeight = Mathf.Max(safeMinH, Mathf.Ceil(sizeAtMinFont.y + safeVPad));

        if (!allowHeightGrow)
        {
            return new FitMetrics
            {
                TargetWidth = targetWidth,
                TargetHeight = safeMinH,
                FontSize = safeMinFont,
                RequiresEllipsis = longestExceedsWidth || desiredHeight > safeMinH + 0.5f,
                MeasuredTextSize = sizeAtMinFont
            };
        }

        bool clippedAtMax = longestExceedsWidth || desiredHeight > safeMaxH + 0.5f;
        float clampedHeight = Mathf.Min(safeMaxH, desiredHeight);

        return new FitMetrics
        {
            TargetWidth = targetWidth,
            TargetHeight = clampedHeight,
            FontSize = safeMinFont,
            RequiresEllipsis = clippedAtMax,
            MeasuredTextSize = sizeAtMinFont
        };
    }

    private void AutoBind()
    {
        if (label == null) label = GetComponentInChildren<TMP_Text>(true);
        if (backgroundImage == null) backgroundImage = GetComponent<Image>();
        if (layoutElement == null) layoutElement = GetComponent<LayoutElement>();
        if (layoutElement == null) layoutElement = gameObject.AddComponent<LayoutElement>();
    }

    private void EnsureSlicedBackground()
    {
        if (!enforceSlicedBackground || backgroundImage == null) return;

        bool needsSprite = forceSharedRoundedSprite
            || backgroundImage.sprite == null
            || (replaceUnslicedSprite && backgroundImage.sprite.border == Vector4.zero);

        if (needsSprite)
        {
            Sprite replacement = fallbackSlicedSprite != null
                ? fallbackSlicedSprite
                : (UITheme.Instance != null && UITheme.Instance.roundedButtonSprite != null
                    ? UITheme.Instance.roundedButtonSprite
                    : GetOrCreateRuntimeRoundedSprite());
            if (replacement != null)
            {
                backgroundImage.sprite = replacement;
            }
        }

        if (backgroundImage.sprite != null && backgroundImage.sprite.border != Vector4.zero)
        {
            backgroundImage.type = Image.Type.Sliced;
        }
    }

    private void ApplyThemeFontIfRequested()
    {
        if (!forceFontFromTheme || label == null || UITheme.Instance == null) return;
        TMP_FontAsset font = UITheme.Instance.GetTitleFont();
        if (font != null && label.font != font)
        {
            label.font = font;
        }
    }

    private void ApplyLabelBoxMargins()
    {
        if (!manageLabelMargins || label == null) return;
        var labelRt = label.rectTransform;
        if (labelRt == null || labelRt.parent != transform) return;

        float vPadHalf = Mathf.Max(0f, verticalPadding * 0.5f);
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.pivot = new Vector2(0.5f, 0.5f);
        labelRt.offsetMin = new Vector2(horizontalPaddingLeft, vPadHalf);
        labelRt.offsetMax = new Vector2(-horizontalPaddingRight, -vPadHalf);
    }

    private float ResolveBaseButtonWidth(RectTransform rt)
    {
        if (fitWidthToText) return minWidth;

        float width = rt.rect.width;
        if (width <= 1f) width = rt.sizeDelta.x;
        if (width <= 1f && layoutElement != null && layoutElement.preferredWidth > 1f)
            width = layoutElement.preferredWidth;
        if (width <= 1f) width = minWidth;
        return width;
    }

    private Vector2 MeasureTmpSize(string text, float fontSize, float wrapWidth)
    {
        if (label == null) return Vector2.zero;
        float prevSize = label.fontSize;
        bool prevAuto = label.enableAutoSizing;
        TextWrappingModes prevWrap = label.textWrappingMode;
        TextOverflowModes prevOverflow = label.overflowMode;

        label.enableAutoSizing = false;
        label.fontSize = fontSize;
        bool wrapping = !float.IsInfinity(wrapWidth);
        label.textWrappingMode = wrapping ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;

        Vector2 measured = wrapping
            ? label.GetPreferredValues(text, Mathf.Max(1f, wrapWidth), 0f)
            : label.GetPreferredValues(text);

        label.fontSize = prevSize;
        label.enableAutoSizing = prevAuto;
        label.textWrappingMode = prevWrap;
        label.overflowMode = prevOverflow;

        return measured;
    }

    private void ApplySizing(RectTransform rt, float width, float height, Vector2 measuredTextSize)
    {
        if (layoutElement != null)
        {
            if (fitWidthToText)
            {
                layoutElement.minWidth = width;
                layoutElement.preferredWidth = width;
            }
            layoutElement.minHeight = height;
            layoutElement.preferredHeight = height;
            layoutElement.flexibleHeight = 0f;
        }

        // Support nested labels driven by HorizontalLayoutGroup / KeyCapPrompt via their LayoutElement
        if (!manageLabelMargins && label != null && label.transform.parent != null && label.transform.parent.GetComponent<LayoutGroup>() != null)
        {
            var labelLe = label.GetComponent<LayoutElement>();
            if (labelLe != null)
            {
                float textBudgetW = Mathf.Max(24f, width - (horizontalPaddingLeft + horizontalPaddingRight));
                float clampedW = Mathf.Clamp(Mathf.Ceil(measuredTextSize.x), 24f, textBudgetW);
                labelLe.minWidth = clampedW;
                labelLe.preferredWidth = clampedW;
                labelLe.preferredHeight = Mathf.Max(20f, Mathf.Ceil(measuredTextSize.y));
            }
        }

        if (!applyRectSizeWhenUncontrolled) return;

        var parentGroup = transform.parent != null ? transform.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
        bool parentControlsWidth = parentGroup != null && parentGroup.childControlWidth;
        bool parentControlsHeight = parentGroup != null && parentGroup.childControlHeight;

        Vector2 size = rt.sizeDelta;
        if (fitWidthToText && !parentControlsWidth) size.x = width;
        if (!parentControlsHeight) size.y = height;
        rt.sizeDelta = size;
    }

    private static void RebuildHierarchy(RectTransform leaf)
    {
        if (leaf == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(leaf);

        Transform cur = leaf.parent;
        for (int depth = 0; depth < 3 && cur != null; depth++, cur = cur.parent)
        {
            if (cur is RectTransform curRt && cur.GetComponent<LayoutGroup>() != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(curRt);
            }
        }
    }

    private static bool LongestWordFits(string longestWord, float fontSize, float availWidth, Func<string, float, float> measureWord)
    {
        if (string.IsNullOrEmpty(longestWord) || measureWord == null) return true;
        return measureWord(longestWord, fontSize) <= availWidth + 0.5f;
    }

    private static string ExtractLongestWord(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string plain = RichTextTagRegex.Replace(text, string.Empty);
        string[] parts = plain.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string longest = string.Empty;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > longest.Length) longest = parts[i];
        }
        return longest;
    }

    public static Sprite GetOrCreateRuntimeRoundedSprite()
    {
        if (_runtimeRoundedSprite != null) return _runtimeRoundedSprite;

        const int size = 64;
        const int radius = 14;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "ResponsiveButtonRoundedFallback",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        float ext = half - 1.5f - radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs((x + 0.5f) - half) - ext;
                float qy = Mathf.Abs((y + 0.5f) - half) - ext;
                float dist = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                    + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - dist) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        _runtimeRoundedSprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
        _runtimeRoundedSprite.name = "ResponsiveButtonRoundedFallback";
        return _runtimeRoundedSprite;
    }
}
