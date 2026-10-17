using System.Collections;
using UnityEngine;

/// <summary>
/// Controls smooth fading and visibility of HUD elements using CanvasGroup.
/// Prevents deactivation of GameObjects so progress tracking and scripts remain active.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class HudVisibility : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float defaultFadeDuration = 0.2f;

    [Header("Slide Animation Settings")]
    [Tooltip("If enabled, slides by slideOffset while fading out (e.g. 40px right for Quest panel)")]
    [SerializeField] private bool slideOnHide = false;
    [SerializeField] private Vector2 slideOffset = new Vector2(40f, 0f);

    private RectTransform rectTransform;
    private Vector2 baseAnchoredPosition;
    private bool hasBasePosition = false;
    private Coroutine fadeCoroutine;

    public bool IsVisible => canvasGroup != null && canvasGroup.alpha > 0.5f;
    public bool SlideOnHide => slideOnHide;
    public Vector2 SlideOffset => slideOffset;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null && !hasBasePosition)
        {
            baseAnchoredPosition = rectTransform.anchoredPosition;
            hasBasePosition = true;
        }
    }

    /// <summary>
    /// Fades the HUD element in or out smoothly.
    /// </summary>
    public void SetVisible(bool visible, float duration = -1f)
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (duration < 0f) duration = defaultFadeDuration;

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }

        if (!gameObject.activeInHierarchy || duration <= 0f)
        {
            SetAlphaImmediate(visible ? 1f : 0f, visible);
            return;
        }

        fadeCoroutine = StartCoroutine(FadeRoutine(visible ? 1f : 0f, visible, duration));
    }

    /// <summary>
    /// Sets visibility instantly without fading.
    /// </summary>
    public void SetVisibleImmediate(bool visible)
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }

        SetAlphaImmediate(visible ? 1f : 0f, visible);
    }

    private void SetAlphaImmediate(float targetAlpha, bool interactive)
    {
        canvasGroup.alpha = targetAlpha;
        canvasGroup.interactable = interactive;
        canvasGroup.blocksRaycasts = interactive;

        if (slideOnHide && rectTransform != null)
        {
            if (!hasBasePosition)
            {
                baseAnchoredPosition = rectTransform.anchoredPosition;
                hasBasePosition = true;
            }
            Vector2 targetPos = (targetAlpha > 0.5f) ? baseAnchoredPosition : (baseAnchoredPosition + slideOffset);
            rectTransform.anchoredPosition = targetPos;
        }
    }

    private IEnumerator FadeRoutine(float targetAlpha, bool interactive, float duration)
    {
        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        Vector2 startPos = rectTransform != null ? rectTransform.anchoredPosition : Vector2.zero;
        Vector2 targetPos = startPos;
        if (slideOnHide && rectTransform != null)
        {
            if (!hasBasePosition)
            {
                baseAnchoredPosition = rectTransform.anchoredPosition;
                hasBasePosition = true;
            }
            targetPos = (targetAlpha > 0.5f) ? baseAnchoredPosition : (baseAnchoredPosition + slideOffset);
        }

        if (!interactive)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Ease-out curve for snappy, smooth responsiveness
            float ease = 1f - Mathf.Pow(1f - t, 2f);

            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, ease);
            if (slideOnHide && rectTransform != null)
            {
                rectTransform.anchoredPosition = Vector2.Lerp(startPos, targetPos, ease);
            }
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
        canvasGroup.interactable = interactive;
        canvasGroup.blocksRaycasts = interactive;
        if (slideOnHide && rectTransform != null)
        {
            rectTransform.anchoredPosition = targetPos;
        }
        fadeCoroutine = null;
    }
}
