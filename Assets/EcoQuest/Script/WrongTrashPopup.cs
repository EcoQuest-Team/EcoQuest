using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class WrongTrashPopup : MonoBehaviour
{
    public static WrongTrashPopup Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private Button closeButton;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Timing")]
    [Tooltip("How long the popup stays visible before automatically popping out.")]
    [SerializeField] private float showDuration = 3.0f;
    [Tooltip("Pop-in animation duration in seconds.")]
    [SerializeField] private float popInDuration = 0.35f;
    [Tooltip("Pop-out animation duration in seconds.")]
    [SerializeField] private float popOutDuration = 0.25f;

    [Header("Transform Animation Settings")]
    [Tooltip("Distance above resting position to start the pop-in slide.")]
    [SerializeField] private float slideDistance = 150f;
    [Tooltip("Distance above resting position to slide during pop-out.")]
    [SerializeField] private float popOutSlideDistance = 60f;
    [Tooltip("Starting scale when popping in.")]
    [SerializeField] private float startScale = 0.85f;
    [Tooltip("Scale target when popping out.")]
    [SerializeField] private float popOutScale = 0.95f;

    [Header("Idle Floating Animation")]
    [Tooltip("Sine wave amplitude in pixels for subtle floating while visible.")]
    [SerializeField] private float floatAmplitude = 3.0f;
    [Tooltip("Speed multiplier for the floating sine wave.")]
    [SerializeField] private float floatSpeed = 3.0f;

    [Header("Animation Curves")]
    [Tooltip("Ease-out-back curve for bouncy pop-in.")]
    [SerializeField] private AnimationCurve popInCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 2.5f, 2.5f),
        new Keyframe(0.72f, 1.06f, 0.2f, 0.2f),
        new Keyframe(1f, 1f, 0f, 0f)
    );

    [Tooltip("Ease-in curve for pop-out.")]
    [SerializeField] private AnimationCurve popOutCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(1f, 1f, 2f, 2f)
    );

    private RectTransform rectTransform;
    private Vector2 restingPosition;
    private Coroutine activeAnimationCoroutine;
    private bool isVisible = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        rectTransform = GetComponent<RectTransform>();
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        // Cache the resting position relative to the Canvas
        restingPosition = rectTransform.anchoredPosition;

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Hide);
        }

        // Initialize in hidden state
        ResetToHidden();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Displays the wrong trash notification popup.
    /// If already visible, executes a gentle pulse and resets the visible countdown.
    /// </summary>
    public void Show()
    {
        if (isVisible)
        {
            // Already visible: smoothly punch/pulse without restarting offscreen
            if (activeAnimationCoroutine != null)
            {
                StopCoroutine(activeAnimationCoroutine);
            }
            activeAnimationCoroutine = StartCoroutine(PulseAndHoldRoutine());
        }
        else
        {
            // Start pop-in animation from offscreen
            if (activeAnimationCoroutine != null)
            {
                StopCoroutine(activeAnimationCoroutine);
            }
            activeAnimationCoroutine = StartCoroutine(PopInSequenceRoutine());
        }
    }

    /// <summary>
    /// Closes the popup playing the pop-out animation.
    /// </summary>
    public void Hide()
    {
        if (!isVisible) return;

        if (activeAnimationCoroutine != null)
        {
            StopCoroutine(activeAnimationCoroutine);
        }
        activeAnimationCoroutine = StartCoroutine(PopOutRoutine());
    }

    private IEnumerator PopInSequenceRoutine()
    {
        isVisible = true;

        if (canvasGroup != null)
        {
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
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
            float t = Mathf.Clamp01(elapsed / popInDuration);
            float curveT = popInCurve.Evaluate(t);

            rectTransform.anchoredPosition = Vector2.LerpUnclamped(startPos, restingPosition, curveT);
            rectTransform.localScale = Vector3.LerpUnclamped(initialScale, Vector3.one, curveT);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Clamp01(t);
            }

            yield return null;
        }

        rectTransform.anchoredPosition = restingPosition;
        rectTransform.localScale = Vector3.one;
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        // Idle floating loop while visible
        yield return IdleFloatRoutine(showDuration);

        // Auto pop-out
        yield return PopOutRoutine();
    }

    private IEnumerator PulseAndHoldRoutine()
    {
        isVisible = true;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        // Pulse scale: 1.0 -> 1.05 -> 1.0 over 0.15s unscaled time
        float pulseDuration = 0.15f;
        float elapsed = 0f;
        Vector3 baseScale = Vector3.one;
        Vector3 punchScale = Vector3.one * 1.05f;
        Vector2 startPulsePos = rectTransform.anchoredPosition;

        while (elapsed < pulseDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / pulseDuration);
            // Parabola: 0 at t=0, 1 at t=0.5, 0 at t=1
            float punch = 4f * t * (1f - t);
            rectTransform.localScale = Vector3.Lerp(baseScale, punchScale, punch);
            rectTransform.anchoredPosition = Vector2.Lerp(startPulsePos, restingPosition, t);
            yield return null;
        }

        rectTransform.localScale = Vector3.one;

        // Reset and wait full show duration with idle float
        yield return IdleFloatRoutine(showDuration);

        yield return PopOutRoutine();
    }

    private IEnumerator IdleFloatRoutine(float duration)
    {
        float timer = 0f;
        float floatPhase = 0f;

        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            floatPhase += Time.unscaledDeltaTime * floatSpeed;

            float yOffset = Mathf.Sin(floatPhase) * floatAmplitude;
            rectTransform.anchoredPosition = restingPosition + new Vector2(0f, yOffset);

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
            float t = Mathf.Clamp01(elapsed / popOutDuration);
            float curveT = popOutCurve.Evaluate(t);

            rectTransform.anchoredPosition = Vector2.LerpUnclamped(startPos, endPos, curveT);
            rectTransform.localScale = Vector3.LerpUnclamped(startScaleVec, endScaleVec, curveT);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, curveT);
            }

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
}
