using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class MenuParallax : MonoBehaviour
{
    [System.Serializable]
    public class ParallaxLayer
    {
        public string name;
        public RectTransform target;
        [Tooltip("How many pixels this layer shifts opposite to the mouse")]
        public float mouseStrength = 20f;
        [Tooltip("Amplitude in pixels of the slow idle sway")]
        public float swayAmplitude = 8f;
        [Tooltip("Minimum scale factor so edges never show")]
        public float baseScale = 1.08f;

        [HideInInspector] public Vector2 initialPos;
        [HideInInspector] public Vector3 initialScale;
        [HideInInspector] public bool captured;
    }

    [Header("Parallax Settings")]
    public ParallaxLayer[] layers;
    [Tooltip("Lerp smoothing speed")]
    public float smoothing = 5f;
    [Tooltip("Seconds per full idle sway cycle (10-15s recommended)")]
    public float swayCycleDuration = 12.5f;

    [Header("Logo Float & Shimmer")]
    public RectTransform logoTarget;
    public float logoFloatAmplitude = 4f;
    public float logoFloatSpeed = 0.45f;
    public RectTransform logoShimmerRect;
    public Graphic logoShimmerGraphic;
    public float shimmerCycleDuration = 5.5f;
    public float shimmerMaxAlpha = 0.18f;

    private Vector2 logoInitialPos;
    private bool logoCaptured;

    private void Awake()
    {
        CaptureInitialState();
    }

    private void OnEnable()
    {
        CaptureInitialState();
    }

    private void CaptureInitialState()
    {
        if (layers != null)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                if (l != null && l.target != null && !l.captured)
                {
                    l.initialPos = l.target.anchoredPosition;
                    l.initialScale = l.target.localScale;
                    l.captured = true;
                }
            }
        }

        if (logoTarget != null && !logoCaptured)
        {
            logoInitialPos = logoTarget.anchoredPosition;
            logoCaptured = true;
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float t = Time.unscaledTime;

        Vector2 mousePos = Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : (Vector2)Input.mousePosition;

        float sw = Mathf.Max(1f, Screen.width);
        float sh = Mathf.Max(1f, Screen.height);
        Vector2 normMouse = new Vector2(
            Mathf.Clamp(mousePos.x / sw - 0.5f, -0.5f, 0.5f),
            Mathf.Clamp(mousePos.y / sh - 0.5f, -0.5f, 0.5f)
        );

        float swayFreq = Mathf.PI * 2f / Mathf.Max(1f, swayCycleDuration);
        float lerpFactor = 1f - Mathf.Exp(- Mathf.Max(0.1f, smoothing) * dt);

        if (layers != null)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                if (l == null || l.target == null) continue;
                if (!l.captured)
                {
                    l.initialPos = l.target.anchoredPosition;
                    l.initialScale = l.target.localScale;
                    l.captured = true;
                }

                float phase = i * 1.3f;
                Vector2 sway = new Vector2(
                    Mathf.Sin(t * swayFreq + phase),
                    Mathf.Cos(t * swayFreq * 0.85f + phase)
                ) * l.swayAmplitude;

                Vector2 targetPos = l.initialPos - normMouse * (l.mouseStrength * 2f) + sway;
                l.target.anchoredPosition = Vector2.Lerp(l.target.anchoredPosition, targetPos, lerpFactor);

                // Ensure layer scale covers the full screen plus max displacement at any resolution
                float maxDisp = l.mouseStrength + l.swayAmplitude + 12f;
                float rw = Mathf.Max(1f, l.target.rect.width);
                float rh = Mathf.Max(1f, l.target.rect.height);
                float reqScaleX = (sw + maxDisp * 2f) / rw;
                float reqScaleY = (sh + maxDisp * 2f) / rh;
                float s = Mathf.Max(l.baseScale, Mathf.Max(reqScaleX, reqScaleY));
                l.target.localScale = new Vector3(s, s, 1f);
            }
        }

        if (logoTarget != null)
        {
            if (!logoCaptured)
            {
                logoInitialPos = logoTarget.anchoredPosition;
                logoCaptured = true;
            }

            float floatOffset = Mathf.Sin(t * logoFloatSpeed * Mathf.PI * 2f) * logoFloatAmplitude;
            Vector2 currentLogoPos = logoInitialPos + new Vector2(0f, floatOffset);
            logoTarget.anchoredPosition = currentLogoPos;

            if (logoShimmerRect != null && logoShimmerGraphic != null)
            {
                float cycle = Mathf.Repeat(t / Mathf.Max(1f, shimmerCycleDuration), 1f);
                // Sweep across during first 60% of cycle, rest during remaining 40%
                if (cycle < 0.6f)
                {
                    float p = cycle / 0.6f;
                    float xOffset = Mathf.Lerp(-460f, 460f, p);
                    logoShimmerRect.anchoredPosition = currentLogoPos + new Vector2(xOffset, 0f);
                    float alpha = Mathf.Sin(p * Mathf.PI) * shimmerMaxAlpha;
                    Color c = logoShimmerGraphic.color;
                    c.a = alpha;
                    logoShimmerGraphic.color = c;
                }
                else
                {
                    Color c = logoShimmerGraphic.color;
                    c.a = 0f;
                    logoShimmerGraphic.color = c;
                }
            }
        }
    }

    private void OnDisable()
    {
        if (layers != null)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                if (l != null && l.target != null && l.captured)
                {
                    l.target.anchoredPosition = l.initialPos;
                    l.target.localScale = l.initialScale;
                }
            }
        }

        if (logoTarget != null && logoCaptured)
        {
            logoTarget.anchoredPosition = logoInitialPos;
        }
    }
}
