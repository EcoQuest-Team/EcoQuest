using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// TextFit layout helper for popups:
/// - Titles: auto-sizes 28-44px, wraps only on whole words (never breaks mid-word), and dynamically widens container or reduces font size so the longest word fits.
/// - Body: pages by measuring text bounds with GetPreferredValues without dropping below 30px.
/// </summary>
public static class TextFit
{
    /// <summary>
    /// Fits an existing title with automatic word-boundary preservation.
    /// </summary>
    public static void FitTitle(TextMeshProUGUI tmp, float minSize = 28f, float maxSize = 44f)
    {
        if (tmp == null) return;
        FitTitle(tmp, tmp.text, tmp.rectTransform.rect.width > 50f ? tmp.rectTransform.rect.width : 800f, minSize, maxSize);
    }

    /// <summary>
    /// Fits a title into a TextMeshProUGUI with automatic word-boundary preservation (no mid-word breaking).
    /// </summary>
    public static void FitTitle(TextMeshProUGUI tmp, string text, float maxWidth = 800f, float minSize = 28f, float maxSize = 44f)
    {
        if (tmp == null) return;

        tmp.text = text ?? string.Empty;
        tmp.textWrappingMode = TextWrappingModes.Normal; // Word wrap only
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.enableAutoSizing = false;

        RectTransform rt = tmp.rectTransform;
        float currentSize = maxSize;
        tmp.fontSize = currentSize;

        // Find longest word in text
        string[] words = (text ?? "").Split(' ', '\n', '\r', '\t');
        string longestWord = "";
        foreach (var w in words)
        {
            if (w.Length > longestWord.Length) longestWord = w;
        }

        // Measure longest word width at max size
        float longestWordWidth = tmp.GetPreferredValues(longestWord, 0, 0).x;
        float availableWidth = rt.rect.width > 50f ? rt.rect.width : maxWidth;

        // If container is smaller than longest word, expand container up to maxWidth
        if (longestWordWidth > availableWidth && availableWidth < maxWidth)
        {
            availableWidth = Mathf.Min(maxWidth, longestWordWidth + 24f);
            rt.sizeDelta = new Vector2(availableWidth, rt.sizeDelta.y);
        }

        // If longest word still exceeds availableWidth, shrink font size until longest word fits
        while (currentSize > minSize)
        {
            tmp.fontSize = currentSize;
            float wWidth = tmp.GetPreferredValues(longestWord, 0, 0).x;
            if (wWidth <= availableWidth) break;
            currentSize -= 1f;
        }

        tmp.fontSize = Mathf.Clamp(currentSize, minSize, maxSize);
    }

    /// <summary>
    /// Measures and paginates body text into pages that strictly fit within maxLines at minSize (30px) to baseSize (34px).
    /// </summary>
    public static List<string> SplitBodyIntoPages(string text, TextMeshProUGUI measureTmp, float bodyWidth = 1154f, int maxLines = 3, float fontSize = 34f)
    {
        return DialoguePresenter.SplitIntoPagesWithFitGuard(text, measureTmp, bodyWidth, maxLines, fontSize);
    }
}
