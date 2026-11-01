using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Polished 3D keycap view with multi-layered top face, 5px darker bevel base,
/// idle glow pulse, pressed sink animation, and 0.5s advance delay radial lock.
/// Automatically expands for wide keys (TAB, CTRL, ESC, SPACE, SHIFT).
/// </summary>
public class KeyCapView : MonoBehaviour
{
    public enum KeyState
    {
        Ready,
        Pressed,
        Locked,
        Disabled
    }

    [Header("Visual Components")]
    [SerializeField] private RectTransform containerRect;
    [SerializeField] private RectTransform baseLayerRect;
    [SerializeField] private RectTransform topFaceRect;
    [SerializeField] private UnityEngine.UI.Image baseLayerImage;
    [SerializeField] private UnityEngine.UI.Image topFaceImage;
    [SerializeField] private TextMeshProUGUI keycapText;
    [SerializeField] private UnityEngine.UI.Image lockRadialOverlay;
    [SerializeField] private UnityEngine.UI.Image glowOutlineImage;

    [Header("Colors")]
    [SerializeField] private Color faceNormalColor = new Color(0.95f, 0.95f, 0.95f, 1f); // #F2F2F2
    [SerializeField] private Color facePressedColor = new Color(0.80f, 0.80f, 0.80f, 1f);
    [SerializeField] private Color baseColor = new Color(0.72f, 0.72f, 0.72f, 1f);        // #B8B8B8
    [SerializeField] private Color textColor = new Color(0.106f, 0.106f, 0.106f, 1f);      // #1B1B1B
    [SerializeField] private Color readyGlowColor = new Color(0.43f, 0.75f, 0.43f, 0.75f); // #6DBE6D glow

    [Header("Dimensions & Sinking")]
    [SerializeField] private float baseHeight = 48f;
    [SerializeField] private float minWidth = 48f;
    [SerializeField] private float horizontalPadding = 16f;
    [SerializeField] private float sinkDistance = 4f;

    private KeyState currentState = KeyState.Ready;
    private Vector2 topFaceOriginalPos = Vector2.zero;
    private Coroutine lockRoutine;
    private Coroutine idleGlowRoutine;
    private string currentKeyString = "E";

    public string CurrentKey => currentKeyString;
    public KeyState State => currentState;

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();
        if (currentState == KeyState.Ready)
        {
            StartIdleGlow();
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    private void Initialize()
    {
        if (containerRect == null) containerRect = GetComponent<RectTransform>();
        if (topFaceRect != null)
        {
            topFaceOriginalPos = new Vector2(0f, 0f);
        }

        if (keycapText != null)
        {
            keycapText.color = textColor;
            keycapText.fontStyle = FontStyles.Bold;
            keycapText.alignment = TextAlignmentOptions.Center;
            keycapText.enableAutoSizing = false;
            keycapText.fontSize = 26f;
        }

        UpdateVisualWidth();
    }

    public void SetKey(string key)
    {
        currentKeyString = string.IsNullOrEmpty(key) ? "E" : key.ToUpperInvariant();
        if (keycapText != null)
        {
            keycapText.text = currentKeyString;
        }
        UpdateVisualWidth();
    }

    public void UpdateVisualWidth()
    {
        if (keycapText == null || containerRect == null) return;

        float textWidth = keycapText.preferredWidth;
        float targetWidth = Mathf.Max(minWidth, textWidth + horizontalPadding * 2f);

        containerRect.sizeDelta = new Vector2(targetWidth, baseHeight);
        if (baseLayerRect != null)
        {
            baseLayerRect.sizeDelta = new Vector2(targetWidth, baseHeight);
            baseLayerRect.anchoredPosition = new Vector2(0f, -5f);
        }
        if (topFaceRect != null)
        {
            topFaceRect.sizeDelta = new Vector2(targetWidth, baseHeight);
        }
        if (lockRadialOverlay != null)
        {
            lockRadialOverlay.rectTransform.sizeDelta = new Vector2(targetWidth, baseHeight);
        }
        if (glowOutlineImage != null)
        {
            glowOutlineImage.rectTransform.sizeDelta = new Vector2(targetWidth + 8f, baseHeight + 8f);
        }
    }

    public void SetPressed(bool pressed)
    {
        if (currentState == KeyState.Locked || currentState == KeyState.Disabled) return;

        if (pressed)
        {
            currentState = KeyState.Pressed;
            if (topFaceRect != null)
            {
                topFaceRect.anchoredPosition = new Vector2(0f, -sinkDistance);
                topFaceRect.localScale = new Vector3(0.95f, 0.95f, 1f);
            }
            if (topFaceImage != null) topFaceImage.color = facePressedColor;
        }
        else
        {
            currentState = KeyState.Ready;
            if (topFaceRect != null)
            {
                topFaceRect.anchoredPosition = topFaceOriginalPos;
                topFaceRect.localScale = Vector3.one;
            }
            if (topFaceImage != null) topFaceImage.color = faceNormalColor;
        }
    }

    public void SetLocked(bool locked, float lockDuration = 0.5f)
    {
        if (locked)
        {
            currentState = KeyState.Locked;
            if (topFaceImage != null)
            {
                Color c = faceNormalColor;
                c.a = 0.45f;
                topFaceImage.color = c;
            }
            if (keycapText != null)
            {
                Color tc = textColor;
                tc.a = 0.45f;
                keycapText.color = tc;
            }

            if (lockRoutine != null) StopCoroutine(lockRoutine);
            lockRoutine = StartCoroutine(LockRadialRoutine(lockDuration));
        }
        else
        {
            if (lockRoutine != null) StopCoroutine(lockRoutine);
            if (lockRadialOverlay != null) lockRadialOverlay.fillAmount = 0f;

            currentState = KeyState.Ready;
            if (topFaceImage != null) topFaceImage.color = faceNormalColor;
            if (keycapText != null)
            {
                Color tc = textColor;
                tc.a = 1.0f;
                keycapText.color = tc;
            }
            StartIdleGlow();
        }
    }

    private IEnumerator LockRadialRoutine(float duration)
    {
        float elapsed = 0f;
        if (lockRadialOverlay != null)
        {
            lockRadialOverlay.gameObject.SetActive(true);
            lockRadialOverlay.fillAmount = 1f;
        }

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (lockRadialOverlay != null)
            {
                lockRadialOverlay.fillAmount = 1f - t;
            }
            yield return null;
        }

        if (lockRadialOverlay != null)
        {
            lockRadialOverlay.fillAmount = 0f;
            lockRadialOverlay.gameObject.SetActive(false);
        }

        // Ready pop and glow
        SetLocked(false);
        StartCoroutine(ReadyPopRoutine());
    }

    private IEnumerator ReadyPopRoutine()
    {
        float duration = 0.15f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            float scale = 1f + 0.08f * Mathf.Sin(t * Mathf.PI);
            containerRect.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }
        containerRect.localScale = Vector3.one;
    }

    private void StartIdleGlow()
    {
        if (idleGlowRoutine != null) StopCoroutine(idleGlowRoutine);
        idleGlowRoutine = StartCoroutine(IdleGlowRoutine());
    }

    private IEnumerator IdleGlowRoutine()
    {
        while (currentState == KeyState.Ready)
        {
            if (glowOutlineImage != null)
            {
                float pulse = (Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / 2.5f)) + 1f) * 0.5f;
                Color gc = readyGlowColor;
                gc.a = Mathf.Lerp(0.1f, 0.75f, pulse);
                glowOutlineImage.color = gc;
            }
            yield return null;
        }
        if (glowOutlineImage != null)
        {
            Color gc = readyGlowColor;
            gc.a = 0f;
            glowOutlineImage.color = gc;
        }
    }
}
