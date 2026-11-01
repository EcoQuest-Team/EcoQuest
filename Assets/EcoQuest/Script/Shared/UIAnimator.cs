using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unified UI tweening component providing token-cancelled, edge-case-safe transitions:
/// - Fade: alpha fade in/out
/// - PopIn: scale pop (0.92 -> 1.03 -> 1.0) with optional slide
/// - Out: clean alpha/scale fade out with deactivation ONLY upon completion
/// All coroutines run on an always-active runner so deactivating target never cuts coroutines mid-flight.
/// </summary>
[DisallowMultipleComponent]
public class UIAnimator : MonoBehaviour
{
    public enum LayerPriority
    {
        HUD = 0,
        TIP = 1,
        Tutorial = 2,
        Modal = 3
    }

    [Header("Configuration")]
    [SerializeField] private LayerPriority priority = LayerPriority.HUD;
    public LayerPriority Priority { get => priority; set => priority = value; }

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform rectTransform;

    [Header("Transition Settings (Unscaled Time)")]
    [SerializeField] private float openDuration = 0.28f;
    [SerializeField] private float closeDuration = 0.18f;
    [SerializeField] private float startScale = 0.95f;
    [SerializeField] private AnimationCurve openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve closeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public float OpenDuration => openDuration;
    public float CloseDuration => closeDuration;

    private int activeToken = 0;
    private bool isAnimating = false;
    public bool IsAnimating => isAnimating;

    private Vector2 baseAnchoredPosition;
    private bool hasBasePosition = false;

    // Singleton runner host for coroutines
    private class Runner : MonoBehaviour { }
    private static Runner runnerHost;
    private static void EnsureRunner()
    {
        if (runnerHost == null)
        {
            var go = new GameObject("[UIAnimatorRunner]");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            runnerHost = go.AddComponent<Runner>();
        }
    }

    public static Coroutine RunStaticCoroutine(IEnumerator routine)
    {
        EnsureRunner();
        return runnerHost.StartCoroutine(routine);
    }

    public static void StopStaticCoroutine(Coroutine routine)
    {
        if (runnerHost != null && routine != null)
        {
            runnerHost.StopCoroutine(routine);
        }
    }

    private void Awake()
    {
        ResolveComponents();
    }

    private void OnDisable()
    {
        activeToken++;
        isAnimating = false;
    }

    public void ResolveComponents()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null && !hasBasePosition)
        {
            baseAnchoredPosition = rectTransform.anchoredPosition;
            hasBasePosition = true;
        }
    }

    public void SetBasePosition(Vector2 pos)
    {
        baseAnchoredPosition = pos;
        hasBasePosition = true;
    }

    public Vector2 BasePosition => hasBasePosition ? baseAnchoredPosition : (rectTransform != null ? rectTransform.anchoredPosition : Vector2.zero);

    /// <summary>
    /// Fades canvas group to targetAlpha.
    /// </summary>
    public void Fade(float targetAlpha, float duration = 0.20f, Action onComplete = null)
    {
        ResolveComponents();
        if (!gameObject.activeSelf && targetAlpha > 0f)
        {
            gameObject.SetActive(true);
        }

        activeToken++;
        int token = activeToken;
        isAnimating = true;

        EnsureRunner();
        runnerHost.StartCoroutine(FadeRoutine(token, targetAlpha, duration, onComplete));
    }

    private IEnumerator FadeRoutine(int token, float targetAlpha, float duration, Action onComplete)
    {
        float startAlpha = canvasGroup != null ? canvasGroup.alpha : 0f;
        float elapsed = 0f;

        if (canvasGroup != null)
        {
            // Prevent clicks/raycasts while transitioning
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (duration <= 0.001f)
        {
            if (canvasGroup != null) canvasGroup.alpha = targetAlpha;
        }
        else
        {
            while (elapsed < duration)
            {
                if (token != activeToken) yield break; // Cancelled by newer animation
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                }
                yield return null;
            }
        }

        if (token == activeToken)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = targetAlpha;
                canvasGroup.interactable = targetAlpha > 0.5f;
                canvasGroup.blocksRaycasts = targetAlpha > 0.5f;
            }
            isAnimating = false;
            onComplete?.Invoke();
        }
    }

    /// <summary>
    /// PopIn transition: scale from startScale (0.95) to 1.0 with smooth ease-out and optional slide.
    /// </summary>
    public void PopIn(float duration = -1f, float scaleStart = -1f, float scaleOvershoot = 1.0f, float slideOffset = 16f, Action onComplete = null)
    {
        ResolveComponents();
        if (duration < 0f) duration = openDuration;
        if (scaleStart < 0f) scaleStart = startScale;
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        activeToken++;
        int token = activeToken;
        isAnimating = true;

        EnsureRunner();
        runnerHost.StartCoroutine(PopInRoutine(token, duration, scaleStart, scaleOvershoot, slideOffset, onComplete));
    }

    private IEnumerator PopInRoutine(int token, float duration, float scaleStart, float scaleOvershoot, float slideOffset, Action onComplete)
    {
        Vector2 startPos = hasBasePosition ? baseAnchoredPosition - new Vector2(0f, slideOffset) : (rectTransform != null ? rectTransform.anchoredPosition - new Vector2(0f, slideOffset) : Vector2.zero);
        Vector2 targetPos = hasBasePosition ? baseAnchoredPosition : (rectTransform != null ? rectTransform.anchoredPosition : Vector2.zero);

        float currentScale = (rectTransform != null && isAnimating && rectTransform.localScale.x > 0.05f && rectTransform.localScale.x < 0.99f)
            ? rectTransform.localScale.x
            : scaleStart;

        float startAlpha = (canvasGroup != null && canvasGroup.alpha > 0.01f && canvasGroup.alpha < 0.99f)
            ? canvasGroup.alpha
            : 0f;
        float elapsed = 0f;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = startAlpha;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = startPos;
            rectTransform.localScale = new Vector3(currentScale, currentScale, 1f);
        }

        while (elapsed < duration)
        {
            if (token != activeToken) yield break;
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 3f); // Cubic ease-out

            float scale = Mathf.Lerp(currentScale, 1.0f, ease);
            if (scaleOvershoot > 1.001f)
            {
                if (t < 0.75f) scale = Mathf.Lerp(currentScale, scaleOvershoot, Mathf.Sin((t / 0.75f) * Mathf.PI * 0.5f));
                else scale = Mathf.Lerp(scaleOvershoot, 1.0f, (t - 0.75f) / 0.25f);
            }

            if (rectTransform != null)
            {
                rectTransform.localScale = new Vector3(scale, scale, 1f);
                rectTransform.anchoredPosition = Vector2.Lerp(startPos, targetPos, ease);
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 1.0f, ease);
            }

            yield return null;
        }

        if (token == activeToken)
        {
            if (rectTransform != null)
            {
                rectTransform.localScale = Vector3.one;
                rectTransform.anchoredPosition = targetPos;
            }
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1.0f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
            isAnimating = false;
            onComplete?.Invoke();
        }
    }

    /// <summary>
    /// Out transition: fades out alpha and shrinks to 0.95 (ease-in), only deactivates GameObject at completion.
    /// </summary>
    public void Out(float duration = -1f, Action onComplete = null)
    {
        ResolveComponents();
        if (duration < 0f) duration = closeDuration;
        if (!gameObject.activeSelf)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
            onComplete?.Invoke();
            return;
        }

        activeToken++;
        int token = activeToken;
        isAnimating = true;

        EnsureRunner();
        runnerHost.StartCoroutine(OutRoutine(token, duration, onComplete));
    }

    private IEnumerator OutRoutine(int token, float duration, Action onComplete)
    {
        float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
        float beginScale = rectTransform != null ? rectTransform.localScale.x : 1f;
        float elapsed = 0f;

        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        while (elapsed < duration)
        {
            if (token != activeToken) yield break;
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = t * t; // Quadratic ease-in

            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, ease);
            }
            if (rectTransform != null)
            {
                float s = Mathf.Lerp(beginScale, startScale, ease);
                rectTransform.localScale = new Vector3(s, s, 1f);
            }

            yield return null;
        }

        if (token == activeToken)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
            if (rectTransform != null)
            {
                rectTransform.localScale = Vector3.one;
                if (hasBasePosition) rectTransform.anchoredPosition = baseAnchoredPosition;
            }
            gameObject.SetActive(false);
            isAnimating = false;
            onComplete?.Invoke();
        }
    }

    /// <summary>
    /// Snaps immediately to exact target state, cancelling any active animations.
    /// </summary>
    public void SnapImmediate(bool visible)
    {
        activeToken++;
        isAnimating = false;
        ResolveComponents();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }
        if (rectTransform != null)
        {
            rectTransform.localScale = Vector3.one;
            if (hasBasePosition) rectTransform.anchoredPosition = baseAnchoredPosition;
        }
        if (gameObject.activeSelf != visible)
        {
            gameObject.SetActive(visible);
        }
    }

    /// <summary>
    /// Sequencing helper: animates this out, then calls inAction.
    /// </summary>
    public static void UISequence(UIAnimator outgoing, Action incomingAction, float gap = 0.05f)
    {
        if (outgoing != null && outgoing.gameObject.activeSelf)
        {
            outgoing.Out(0.20f, () => {
                if (gap > 0f)
                {
                    EnsureRunner();
                    runnerHost.StartCoroutine(DelayRoutine(gap, incomingAction));
                }
                else
                {
                    incomingAction?.Invoke();
                }
            });
        }
        else
        {
            incomingAction?.Invoke();
        }
    }

    private static IEnumerator DelayRoutine(float delay, Action action)
    {
        yield return new WaitForSecondsRealtime(delay);
        action?.Invoke();
    }
}
