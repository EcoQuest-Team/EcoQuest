using UnityEngine;
using TMPro;

namespace EcoQuest.CityQuest
{
    /// <summary>
    /// Smoothly glides category text back and forth (left to right, right to left)
    /// inside a masked container so the full text (e.g. "NON-BIODEGRADABLE", "BIODEGRADABLE")
    /// is completely readable without being cut off or obscured.
    /// </summary>
    [ExecuteAlways]
    public class UIPingPongMarquee : MonoBehaviour
    {
        [Header("References")]
        public RectTransform maskRect;
        public RectTransform textRect;
        public TextMeshProUGUI tmpText;

        [Header("Scroll Settings")]
        [Tooltip("Horizontal scrolling speed in pixels per second")]
        public float scrollSpeed = 18f;

        [Tooltip("Pause duration at each end in seconds (for clear reading)")]
        public float pauseDuration = 1.3f;

        private float timer = 0f;
        private bool scrollingLeft = true; // when true, text shifts left to reveal right side
        private float currentOffset = 0f;
        private float maxScroll = 0f;
        private string lastText = null;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ResetMarquee();
        }

        public void ResolveReferences()
        {
            if (textRect == null) textRect = GetComponent<RectTransform>();
            if (tmpText == null) tmpText = GetComponent<TextMeshProUGUI>();
            if (maskRect == null && textRect != null && textRect.parent != null)
            {
                maskRect = textRect.parent.GetComponent<RectTransform>();
            }

            // CRITICAL: Text pivot must be (0, 0.5) so that x=0 aligns the start of the word to the left edge of the mask!
            if (textRect != null)
            {
                textRect.pivot = new Vector2(0f, 0.5f);
                textRect.anchorMin = new Vector2(0f, 0.5f);
                textRect.anchorMax = new Vector2(0f, 0.5f);
            }
            if (tmpText != null)
            {
                tmpText.alignment = TextAlignmentOptions.MidlineLeft;
                tmpText.enableWordWrapping = false;
                tmpText.overflowMode = TextOverflowModes.Overflow;
            }
        }

        public void ResetMarquee()
        {
            currentOffset = 0f;
            timer = pauseDuration;
            scrollingLeft = true;
            if (textRect != null)
            {
                textRect.pivot = new Vector2(0f, 0.5f);
                textRect.anchorMin = new Vector2(0f, 0.5f);
                textRect.anchorMax = new Vector2(0f, 0.5f);
                textRect.anchoredPosition = new Vector2(0f, 0f);
            }
        }

        private void Update()
        {
            if (maskRect == null || textRect == null || tmpText == null)
            {
                ResolveReferences();
                if (maskRect == null || textRect == null || tmpText == null) return;
            }

            // Ensure pivot is always left-aligned
            if (textRect.pivot.x != 0f)
            {
                textRect.pivot = new Vector2(0f, 0.5f);
                textRect.anchorMin = new Vector2(0f, 0.5f);
                textRect.anchorMax = new Vector2(0f, 0.5f);
            }

            // Detect text content change and reset
            if (tmpText.text != lastText)
            {
                lastText = tmpText.text;
                ResetMarquee();
            }

            float textWidth = tmpText.preferredWidth;
            float maskWidth = maskRect.rect.width;
            maxScroll = Mathf.Max(0f, textWidth - maskWidth + 4f);

            // If the text fits comfortably inside the mask, keep it at start
            if (maxScroll <= 1f)
            {
                if (currentOffset != 0f)
                {
                    currentOffset = 0f;
                    textRect.anchoredPosition = new Vector2(0f, 0f);
                }
                return;
            }

            // Pause at ends for comfortable reading
            float dt = Application.isPlaying ? Time.unscaledDeltaTime : 0.016f;
            if (timer > 0f)
            {
                timer -= dt;
                return;
            }

            float step = scrollSpeed * dt;
            if (scrollingLeft)
            {
                currentOffset += step;
                if (currentOffset >= maxScroll)
                {
                    currentOffset = maxScroll;
                    scrollingLeft = false;
                    timer = pauseDuration;
                }
            }
            else
            {
                currentOffset -= step;
                if (currentOffset <= 0f)
                {
                    currentOffset = 0f;
                    scrollingLeft = true;
                    timer = pauseDuration;
                }
            }

            textRect.anchoredPosition = new Vector2(-currentOffset, 0f);
        }
    }
}
