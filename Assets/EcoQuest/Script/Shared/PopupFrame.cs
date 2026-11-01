using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Universal dark-green popup frame component managing animations, layout groups,
/// optional title tab, optional photo frame, body text, and footer row (HintRow or choice buttons).
/// </summary>
public class PopupFrame : MonoBehaviour
{
    [Header("Frame References")]
    [SerializeField] private RectTransform frameRect;
    [SerializeField] private CanvasGroup frameCanvasGroup;
    [SerializeField] private UnityEngine.UI.Image frameBackgroundImage;

    [Header("Header / Tab")]
    [SerializeField] private RectTransform titleTabRect;
    [SerializeField] private UnityEngine.UI.Image titleTabImage;
    [SerializeField] private TextMeshProUGUI titleTabText;

    [Header("Image Frame (Optional)")]
    [SerializeField] private GameObject photoFrameRoot;
    [SerializeField] private UnityEngine.UI.Image photoImage;
    [SerializeField] private UnityEngine.UI.Image photoBorderImage;

    [Header("Body Area")]
    [SerializeField] private RectTransform bodyAreaRect;
    [SerializeField] private TextMeshProUGUI bodyText;

    [Header("Footer Row")]
    [SerializeField] private RectTransform footerRowRect;
    [SerializeField] private HintRow hintRow;
    [SerializeField] private RectTransform choiceButtonsRoot;

    private Coroutine transitionRoutine;
    private Vector2 baseAnchoredPosition;
    private bool hasBasePosition = false;

    public CanvasGroup CanvasGroup => frameCanvasGroup;
    public RectTransform FrameRect => frameRect;
    public HintRow Hint => hintRow;

    private void Awake()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (frameRect == null) frameRect = GetComponent<RectTransform>();
        if (frameCanvasGroup == null) frameCanvasGroup = GetComponent<CanvasGroup>();
        if (frameCanvasGroup == null) frameCanvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (frameRect != null && !hasBasePosition)
        {
            baseAnchoredPosition = frameRect.anchoredPosition;
            hasBasePosition = true;
        }

        if (photoImage != null)
        {
            // Requirement: Image color forced to WHITE so picture is never tinted
            photoImage.color = Color.white;
        }
    }

    public void SetTitle(string title)
    {
        if (titleTabText != null)
        {
            TextFit.FitTitle(titleTabText, title, 800f, 26f, 36f);
            if (titleTabRect != null)
            {
                float prefW = titleTabText.preferredWidth + 48f;
                titleTabRect.sizeDelta = new Vector2(Mathf.Clamp(prefW, 160f, 750f), 44f);
            }
        }
    }

    public void SetPhoto(Sprite sprite)
    {
        if (photoFrameRoot != null)
        {
            bool hasPhoto = sprite != null;
            photoFrameRoot.SetActive(hasPhoto);
            if (hasPhoto && photoImage != null)
            {
                photoImage.sprite = sprite;
                photoImage.color = Color.white; // Guaranteed untinted
            }
        }
    }

    public void SetBody(string text)
    {
        if (bodyText != null)
        {
            bodyText.text = text ?? string.Empty;
            bodyText.fontSize = 32f;
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            bodyText.overflowMode = TextOverflowModes.Overflow;
        }
    }

    public void SetFooterHint(string key, string action)
    {
        if (hintRow != null)
        {
            hintRow.gameObject.SetActive(true);
            hintRow.SetHint(key, action);
        }
        if (choiceButtonsRoot != null)
        {
            choiceButtonsRoot.gameObject.SetActive(false);
        }
    }

    public void AnimateIn(float duration = 0.30f, Action onComplete = null)
    {
        Initialize();
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(AnimateInRoutine(duration, onComplete));
    }

    public void AnimateOut(float duration = 0.20f, Action onComplete = null)
    {
        Initialize();
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(AnimateOutRoutine(duration, onComplete));
    }

    private IEnumerator AnimateInRoutine(float duration, Action onComplete)
    {
        float elapsed = 0f;
        Vector2 startPos = baseAnchoredPosition - new Vector2(0f, 24f);
        Vector3 startScale = new Vector3(0.94f, 0.94f, 1f);

        frameRect.anchoredPosition = startPos;
        frameRect.localScale = startScale;
        frameCanvasGroup.alpha = 0f;
        frameCanvasGroup.blocksRaycasts = true;
        frameCanvasGroup.interactable = true;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Ease out with slight overshoot
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            float overshoot = Mathf.Sin(t * Mathf.PI * 0.5f) * 1.02f;
            if (t > 0.85f) overshoot = Mathf.Lerp(1.02f, 1.0f, (t - 0.85f) / 0.15f);

            frameCanvasGroup.alpha = Mathf.Lerp(0f, 1f, ease);
            frameRect.anchoredPosition = Vector2.Lerp(startPos, baseAnchoredPosition, ease);
            frameRect.localScale = Vector3.Lerp(startScale, Vector3.one * overshoot, ease);

            yield return null;
        }

        frameCanvasGroup.alpha = 1f;
        frameRect.anchoredPosition = baseAnchoredPosition;
        frameRect.localScale = Vector3.one;

        onComplete?.Invoke();
    }

    private IEnumerator AnimateOutRoutine(float duration, Action onComplete)
    {
        float elapsed = 0f;
        Vector2 targetPos = baseAnchoredPosition - new Vector2(0f, 16f);
        Vector3 targetScale = new Vector3(0.96f, 0.96f, 1f);

        float startAlpha = frameCanvasGroup.alpha;
        Vector2 startPos = frameRect.anchoredPosition;
        Vector3 startScale = frameRect.localScale;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = t * t; // Ease in

            frameCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, ease);
            frameRect.anchoredPosition = Vector2.Lerp(startPos, targetPos, ease);
            frameRect.localScale = Vector3.Lerp(startScale, targetScale, ease);

            yield return null;
        }

        frameCanvasGroup.alpha = 0f;
        frameCanvasGroup.blocksRaycasts = false;
        frameCanvasGroup.interactable = false;

        onComplete?.Invoke();
    }
}
