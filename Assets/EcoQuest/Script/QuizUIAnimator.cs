using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Handles all unscaled-time coroutines and easing animations for the EcoQuest Quiz UI.
/// </summary>
public class QuizUIAnimator : MonoBehaviour
{
    private static readonly AnimationCurve PopInCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 2.5f, 2.5f),
        new Keyframe(0.72f, 1.06f, 0.2f, 0.2f),
        new Keyframe(1f, 1f, 0f, 0f)
    );

    private static readonly AnimationCurve PopOutCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(1f, 1f, 2f, 2f)
    );

    private static readonly System.Collections.Generic.Dictionary<int, int> alphaTokens = new System.Collections.Generic.Dictionary<int, int>();
    private static readonly System.Collections.Generic.Dictionary<int, int> scaleTokens = new System.Collections.Generic.Dictionary<int, int>();
    private static readonly System.Collections.Generic.Dictionary<int, int> posTokens = new System.Collections.Generic.Dictionary<int, int>();
    private static readonly System.Collections.Generic.Dictionary<int, int> fillTokens = new System.Collections.Generic.Dictionary<int, int>();
    private static readonly System.Collections.Generic.Dictionary<int, int> textTokens = new System.Collections.Generic.Dictionary<int, int>();
    private static readonly System.Collections.Generic.Dictionary<int, Vector2> shakeBasePositions = new System.Collections.Generic.Dictionary<int, Vector2>();

    private static int NextToken(System.Collections.Generic.Dictionary<int, int> dict, UnityEngine.Object obj)
    {
        if (obj == null) return 0;
        int id = obj.GetInstanceID();
        int next = dict.TryGetValue(id, out int cur) ? cur + 1 : 1;
        dict[id] = next;
        return next;
    }

    private static bool IsTokenValid(System.Collections.Generic.Dictionary<int, int> dict, UnityEngine.Object obj, int token)
    {
        if (obj == null) return false;
        return dict.TryGetValue(obj.GetInstanceID(), out int cur) && cur == token;
    }

    public static void StopTransitionsOn(RectTransform rect, CanvasGroup cg = null)
    {
        if (rect != null)
        {
            NextToken(scaleTokens, rect);
            NextToken(posTokens, rect);
        }
        if (cg != null)
        {
            NextToken(alphaTokens, cg);
        }
    }

    public static IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        if (cg == null) yield break;
        int token = NextToken(alphaTokens, cg);
        cg.alpha = from;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (!IsTokenValid(alphaTokens, cg, token)) yield break;
            float t = Mathf.Clamp01(elapsed / duration);
            cg.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }
        if (IsTokenValid(alphaTokens, cg, token))
        {
            cg.alpha = to;
        }
    }

    public static IEnumerator PopInPanel(RectTransform panel, CanvasGroup cg, float startScale, float duration)
    {
        if (panel == null) yield break;
        int scaleToken = NextToken(scaleTokens, panel);
        int alphaToken = cg != null ? NextToken(alphaTokens, cg) : 0;

        Vector3 baseScale = Vector3.one * startScale;
        panel.localScale = baseScale;
        if (cg != null) cg.alpha = 0f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (!IsTokenValid(scaleTokens, panel, scaleToken)) yield break;
            float t = Mathf.Clamp01(elapsed / duration);
            float curveT = PopInCurve.Evaluate(t);

            panel.localScale = Vector3.LerpUnclamped(baseScale, Vector3.one, curveT);
            if (cg != null && IsTokenValid(alphaTokens, cg, alphaToken))
            {
                cg.alpha = Mathf.Clamp01(t * 1.5f);
            }
            yield return null;
        }

        if (IsTokenValid(scaleTokens, panel, scaleToken))
        {
            panel.localScale = Vector3.one;
        }
        if (cg != null && IsTokenValid(alphaTokens, cg, alphaToken))
        {
            cg.alpha = 1f;
        }
    }

    public static IEnumerator PopOutPanel(RectTransform panel, CanvasGroup cg, float endScale, float duration)
    {
        if (panel == null) yield break;
        int scaleToken = NextToken(scaleTokens, panel);
        int alphaToken = cg != null ? NextToken(alphaTokens, cg) : 0;

        Vector3 startScale = panel.localScale;
        Vector3 targetScale = Vector3.one * endScale;
        float startAlpha = cg != null ? cg.alpha : 1f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (!IsTokenValid(scaleTokens, panel, scaleToken)) yield break;
            float t = Mathf.Clamp01(elapsed / duration);
            float curveT = PopOutCurve.Evaluate(t);

            panel.localScale = Vector3.LerpUnclamped(startScale, targetScale, curveT);
            if (cg != null && IsTokenValid(alphaTokens, cg, alphaToken))
            {
                cg.alpha = Mathf.Lerp(startAlpha, 0f, curveT);
            }
            yield return null;
        }

        if (IsTokenValid(scaleTokens, panel, scaleToken))
        {
            panel.localScale = targetScale;
        }
        if (cg != null && IsTokenValid(alphaTokens, cg, alphaToken))
        {
            cg.alpha = 0f;
        }
    }

    public static IEnumerator SlideAndFade(RectTransform target, CanvasGroup cg, Vector2 startOffset, Vector2 endOffset, float startAlpha, float endAlpha, float duration)
    {
        if (target == null) yield break;
        int posToken = NextToken(posTokens, target);
        int alphaToken = cg != null ? NextToken(alphaTokens, cg) : 0;

        target.anchoredPosition = startOffset;
        if (cg != null) cg.alpha = startAlpha;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (!IsTokenValid(posTokens, target, posToken)) yield break;
            float t = Mathf.Clamp01(elapsed / duration);
            // Smooth ease out
            float ease = Mathf.Sin(t * Mathf.PI * 0.5f);

            target.anchoredPosition = Vector2.Lerp(startOffset, endOffset, ease);
            if (cg != null && IsTokenValid(alphaTokens, cg, alphaToken))
            {
                cg.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
            }
            yield return null;
        }

        if (IsTokenValid(posTokens, target, posToken))
        {
            target.anchoredPosition = endOffset;
        }
        if (cg != null && IsTokenValid(alphaTokens, cg, alphaToken))
        {
            cg.alpha = endAlpha;
        }
    }

    public static IEnumerator SmoothFill(Image fillImage, float targetFill, float duration)
    {
        if (fillImage == null) yield break;
        int token = NextToken(fillTokens, fillImage);
        float startFill = fillImage.fillAmount;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (!IsTokenValid(fillTokens, fillImage, token)) yield break;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = Mathf.SmoothStep(0f, 1f, t);
            fillImage.fillAmount = Mathf.Lerp(startFill, targetFill, ease);
            yield return null;
        }
        if (IsTokenValid(fillTokens, fillImage, token))
        {
            fillImage.fillAmount = targetFill;
        }
    }

    public static IEnumerator ShakeHorizontal(RectTransform target, float amplitude, float duration)
    {
        if (target == null) yield break;
        int id = target.GetInstanceID();
        if (!shakeBasePositions.TryGetValue(id, out Vector2 originalPos))
        {
            originalPos = target.anchoredPosition;
            shakeBasePositions[id] = originalPos;
        }
        else
        {
            target.anchoredPosition = originalPos;
        }

        int token = NextToken(posTokens, target);
        float elapsed = 0f;
        float frequency = 30f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (!IsTokenValid(posTokens, target, token)) yield break;
            float decay = 1f - Mathf.Clamp01(elapsed / duration);
            float offset = Mathf.Sin(elapsed * frequency) * amplitude * decay;
            target.anchoredPosition = originalPos + new Vector2(offset, 0f);
            yield return null;
        }
        if (IsTokenValid(posTokens, target, token))
        {
            target.anchoredPosition = originalPos;
            shakeBasePositions.Remove(id);
        }
    }

    public static IEnumerator PulseScale(RectTransform target, float punchMultiplier, float duration)
    {
        if (target == null) yield break;
        int token = NextToken(scaleTokens, target);
        Vector3 baseScale = Vector3.one;
        target.localScale = baseScale;
        Vector3 punchScale = baseScale * punchMultiplier;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (!IsTokenValid(scaleTokens, target, token)) yield break;
            float t = Mathf.Clamp01(elapsed / duration);
            float punch = 4f * t * (1f - t); // Parabola 0 -> 1 -> 0
            target.localScale = Vector3.Lerp(baseScale, punchScale, punch);
            yield return null;
        }
        if (IsTokenValid(scaleTokens, target, token))
        {
            target.localScale = baseScale;
        }
    }

    public static IEnumerator CountUpText(TextMeshProUGUI label, string prefix, int targetValue, string suffix, float duration)
    {
        if (label == null) yield break;
        int token = NextToken(textTokens, label);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (!IsTokenValid(textTokens, label, token)) yield break;
            float t = Mathf.Clamp01(elapsed / duration);
            int current = Mathf.RoundToInt(Mathf.Lerp(0, targetValue, t));
            label.text = $"{prefix}{current}{suffix}";
            yield return null;
        }
        if (IsTokenValid(textTokens, label, token))
        {
            label.text = $"{prefix}{targetValue}{suffix}";
        }
    }

    public static IEnumerator AnimateRewardGift(RectTransform giftRect)
    {
        if (giftRect == null) yield break;
        int id = giftRect.GetInstanceID();
        if (!shakeBasePositions.TryGetValue(id, out Vector2 basePos))
        {
            basePos = giftRect.anchoredPosition;
            shakeBasePositions[id] = basePos;
        }
        else
        {
            giftRect.anchoredPosition = basePos;
        }

        int scaleToken = NextToken(scaleTokens, giftRect);
        int posToken = NextToken(posTokens, giftRect);

        giftRect.localScale = Vector3.zero;
        giftRect.localEulerAngles = Vector3.zero;

        // 1. Pop-In Bounce (0 -> 1.18 -> 0.95 -> 1.0 over 0.45s) with tilt wiggle (-8 -> +8 -> 0 deg Z)
        float popDuration = 0.45f;
        float elapsed = 0f;
        while (elapsed < popDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (giftRect == null || !IsTokenValid(scaleTokens, giftRect, scaleToken) || !IsTokenValid(posTokens, giftRect, posToken))
                yield break;

            float t = Mathf.Clamp01(elapsed / popDuration);

            float scale;
            if (t < 0.50f)
                scale = Mathf.SmoothStep(0f, 1.18f, t / 0.50f);
            else if (t < 0.78f)
                scale = Mathf.SmoothStep(1.18f, 0.95f, (t - 0.50f) / 0.28f);
            else
                scale = Mathf.SmoothStep(0.95f, 1.0f, (t - 0.78f) / 0.22f);

            float rotZ;
            if (t < 0.35f)
                rotZ = Mathf.SmoothStep(0f, -8f, t / 0.35f);
            else if (t < 0.70f)
                rotZ = Mathf.SmoothStep(-8f, 8f, (t - 0.35f) / 0.35f);
            else
                rotZ = Mathf.SmoothStep(8f, 0f, (t - 0.70f) / 0.30f);

            giftRect.localScale = Vector3.one * scale;
            giftRect.localEulerAngles = new Vector3(0f, 0f, rotZ);
            yield return null;
        }

        if (giftRect == null || !IsTokenValid(scaleTokens, giftRect, scaleToken) || !IsTokenValid(posTokens, giftRect, posToken))
            yield break;

        giftRect.localScale = Vector3.one;
        giftRect.localEulerAngles = Vector3.zero;
        giftRect.anchoredPosition = basePos;

        // 2. Continuous Idle Float & Pulse while ResultsPanel / giftRect remains active
        float idleTime = 0f;
        while (giftRect != null && giftRect.gameObject.activeInHierarchy)
        {
            idleTime += Time.unscaledDeltaTime;
            if (!IsTokenValid(scaleTokens, giftRect, scaleToken) || !IsTokenValid(posTokens, giftRect, posToken))
                yield break;

            float bobY = Mathf.Sin(idleTime * 2.5f) * 6f;
            float pulse = Mathf.Lerp(1.0f, 1.06f, (1f - Mathf.Cos(idleTime * 3.0f)) * 0.5f);
            float rockZ = Mathf.Sin(idleTime * 2.0f) * 3.5f;

            giftRect.anchoredPosition = new Vector2(basePos.x, basePos.y + bobY);
            giftRect.localScale = Vector3.one * pulse;
            giftRect.localEulerAngles = new Vector3(0f, 0f, rockZ);
            yield return null;
        }
    }
}

/// <summary>
/// Smooth button hover and press scale animator using unscaled time.
/// </summary>
public class QuizButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private RectTransform rectTransform;
    private Coroutine currentScaleRoutine;
    private bool isHovered = false;
    private bool isPressed = false;
    public bool isInteractable = true;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isInteractable) return;
        isHovered = true;
        AnimateTo(1.04f, 0.12f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        if (!isPressed)
        {
            AnimateTo(1.0f, 0.12f);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isInteractable) return;
        isPressed = true;
        AnimateTo(0.96f, 0.08f);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
        if (isHovered && isInteractable)
        {
            AnimateTo(1.04f, 0.12f);
        }
        else
        {
            AnimateTo(1.0f, 0.12f);
        }
    }

    public void ResetState()
    {
        isHovered = false;
        isPressed = false;
        isInteractable = true;
        if (currentScaleRoutine != null) StopCoroutine(currentScaleRoutine);
        if (rectTransform != null) rectTransform.localScale = Vector3.one;
    }

    private void AnimateTo(float targetScale, float duration)
    {
        if (rectTransform == null) return;
        if (currentScaleRoutine != null) StopCoroutine(currentScaleRoutine);
        currentScaleRoutine = StartCoroutine(ScaleRoutine(targetScale, duration));
    }

    private IEnumerator ScaleRoutine(float targetScale, float duration)
    {
        Vector3 start = rectTransform.localScale;
        Vector3 end = Vector3.one * targetScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rectTransform.localScale = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        rectTransform.localScale = end;
    }
}
