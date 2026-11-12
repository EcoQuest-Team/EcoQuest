using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class WrongTrashPopup : MonoBehaviour
{
    private static WrongTrashPopup _instance;
    public static WrongTrashPopup Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Object.FindFirstObjectByType<WrongTrashPopup>(FindObjectsInactive.Include);
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
            _instance = Object.FindFirstObjectByType<WrongTrashPopup>(FindObjectsInactive.Include);
        }
        if (_instance != null && !_instance.gameObject.activeSelf)
        {
            _instance.gameObject.SetActive(true);
        }
    }

    [Header("UI References")]
    [SerializeField] private Button closeButton;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMPro.TextMeshProUGUI hintText;
    [SerializeField] private GameObject hintRibbon;

    [Header("Timing")]
    [Tooltip("How long the popup stays visible before automatically popping out.")]
    [SerializeField] private float showDuration = 3.5f;
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
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

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
        if (rectTransform != null)
        {
            restingPosition = rectTransform.anchoredPosition;
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Hide);
        }

        // Initialize in hidden state
        ResetToHidden();
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void EnsureHintText()
    {
        if (hintText != null && hintRibbon != null) return;

        // Check if HintRibbon already exists as a child
        Transform existingRibbon = transform.Find("HintRibbon");
        if (existingRibbon != null)
        {
            hintRibbon = existingRibbon.gameObject;
            hintText = hintRibbon.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            return;
        }

        // Dynamically build HintRibbon matching UITheme dark-green styling
        GameObject ribbon = new GameObject("HintRibbon", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        ribbon.transform.SetParent(transform, false);

        RectTransform rt = ribbon.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -6f);
        rt.sizeDelta = new Vector2(700f, 44f);

        UnityEngine.UI.Image img = ribbon.GetComponent<UnityEngine.UI.Image>();
        if (UITheme.Instance != null && UITheme.Instance.panelBackgroundSprite != null)
        {
            img.sprite = UITheme.Instance.panelBackgroundSprite;
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.color = UITheme.Instance.panelColor;
        }
        else
        {
            img.color = new Color(0.043f, 0.141f, 0.098f, 0.95f);
        }

        var outline = ribbon.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = (UITheme.Instance != null) ? UITheme.Instance.borderColor : new Color(0.427f, 0.745f, 0.427f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        // Child Text
        GameObject textObj = new GameObject("HintText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
        textObj.transform.SetParent(ribbon.transform, false);

        RectTransform textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(16f, 4f);
        textRt.offsetMax = new Vector2(-16f, -4f);

        hintText = textObj.GetComponent<TMPro.TextMeshProUGUI>();
        hintText.alignment = TMPro.TextAlignmentOptions.Center;
        hintText.enableWordWrapping = true;
        hintText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        hintText.fontSize = 20f;
        hintText.fontSizeMin = 14f;
        hintText.fontSizeMax = 22f;
        hintText.enableAutoSizing = true;
        hintText.color = (UITheme.Instance != null) ? UITheme.Instance.textMuted : new Color(0.890f, 0.960f, 0.890f, 1f);

        if (UITheme.Instance != null)
        {
            var font = UITheme.Instance.GetBodyFont();
            if (font != null) hintText.font = font;
        }
#if UNITY_EDITOR
        if (hintText.font == null)
        {
            hintText.font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Fonts/Nunito SDF.asset");
        }
#endif
        hintRibbon = ribbon;
    }

    /// <summary>
    /// Displays the wrong trash notification popup with an optional educational hint message.
    /// If already visible, executes a gentle pulse and resets the visible countdown.
    /// </summary>
    public void Show(string message = null)
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
            if (rectTransform != null) restingPosition = rectTransform.anchoredPosition;
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        EnsureHintText();
        if (hintRibbon != null)
        {
            if (!string.IsNullOrEmpty(message))
            {
                if (hintText != null) hintText.text = message;
                hintRibbon.SetActive(true);
            }
            else
            {
                hintRibbon.SetActive(false);
            }
        }

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
        isVisible = false;

        if (activeAnimationCoroutine != null)
        {
            StopCoroutine(activeAnimationCoroutine);
        }

        if (gameObject.activeInHierarchy)
        {
            activeAnimationCoroutine = StartCoroutine(PopOutRoutine());
        }
        else
        {
            ResetToHidden();
            activeAnimationCoroutine = null;
        }
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
