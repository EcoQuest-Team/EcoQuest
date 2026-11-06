using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Reusable prompt element: [Keycap] + Action Text.
/// Displays key name (dynamic from InputAction or fallback string),
/// simulates 3D press down ~3px and darkens when pressed, with subtle idle breathing.
/// Supports 0.5s input lock dimming (40% alpha) during advance delays.
/// Implements pointer click and hover for interactive UI buttons.
/// </summary>
public class KeyCapPrompt : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("UI References")]
    [SerializeField] private RectTransform keycapRect;
    [SerializeField] private Image keycapImage;
    [SerializeField] private TextMeshProUGUI keycapText;
    [SerializeField] private TextMeshProUGUI actionLabel;
    [SerializeField] private CanvasGroup promptCanvasGroup;

#if ENABLE_INPUT_SYSTEM
    [Header("Input System (Optional)")]
    [SerializeField] private InputActionReference inputActionReference;
#endif

    [Header("Settings")]
    [SerializeField] private string fallbackKey = "E";
    [SerializeField] private string defaultActionText = "Interact";
    [SerializeField] private float pressDepth = 3f;
    [SerializeField] private float idlePulseScale = 1.04f;

    [Header("Colors")]
    [SerializeField] private Color normalColor = new Color(0.91f, 0.91f, 0.91f, 1f); // #E8E8E8
    [SerializeField] private Color heldColor = new Color(0.72f, 0.72f, 0.72f, 1f);   // Darkened pressed
    [SerializeField] private Color textColor = new Color(0.106f, 0.106f, 0.106f, 1f); // #1B1B1B

    public event Action OnPromptClicked;

    private Vector2 baseKeycapPos;
    private bool hasBasePos = false;
    private bool isPressed = false;
    private bool isHovered = false;
    private bool isLocked = false;
    private KeyCode cachedKeyCode = KeyCode.E;
    private int lastClickFrame = -1;

    public string CurrentKey => keycapText != null ? keycapText.text : fallbackKey;
    public string CurrentAction => actionLabel != null ? actionLabel.text : defaultActionText;
    public TextMeshProUGUI ActionLabelText => actionLabel;
    public bool IsLocked => isLocked;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isLocked) return;
        isHovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (isLocked) return;
        isPressed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (isLocked) return;
        if (Time.frameCount == lastClickFrame) return;
        lastClickFrame = Time.frameCount;

        UITheme.PlaySound(UITheme.Instance != null ? UITheme.Instance.buttonClickSound : null);
        OnPromptClicked?.Invoke();
    }

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();
        UpdateKeyDisplay();
    }

    private void Initialize()
    {
        if (promptCanvasGroup == null)
            promptCanvasGroup = GetComponent<CanvasGroup>();

        if (keycapImage != null) keycapImage.raycastTarget = true;
        if (actionLabel != null) actionLabel.raycastTarget = true;

        if (keycapRect != null)
        {
            if (!hasBasePos)
            {
                baseKeycapPos = new Vector2(keycapRect.anchoredPosition.x, GetBaselineY());
                hasBasePos = true;
            }
        }

        var hlg = GetComponent<HorizontalLayoutGroup>();
        if (hlg != null)
        {
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
        }

        if (actionLabel != null)
        {
            actionLabel.alignment = TextAlignmentOptions.MidlineLeft;
            var ale = actionLabel.GetComponent<LayoutElement>();
            if (ale == null && hlg != null) ale = actionLabel.gameObject.AddComponent<LayoutElement>();
            if (ale != null)
            {
                ale.minHeight = 48f;
                ale.preferredHeight = 48f;
            }
        }

        if (keycapText != null)
        {
            keycapText.color = textColor;
            keycapText.fontStyle = FontStyles.Bold;
            keycapText.enableAutoSizing = false;
            keycapText.alignment = TextAlignmentOptions.Center;
        }

        if (string.IsNullOrEmpty(fallbackKey)) fallbackKey = "E";
        UpdateCachedKeyCode();
        AdjustKeycapWidth();
    }

    private float GetBaselineY()
    {
        if (keycapRect == null) return 0f;
        if (keycapRect.anchorMin.y > 0.9f && keycapRect.parent is RectTransform prt)
        {
            float h = prt.rect.height > 1f ? prt.rect.height : prt.sizeDelta.y;
            if (h <= 1f) h = 48f;
            return -h * 0.5f;
        }
        return 0f;
    }

    private void AdjustKeycapWidth()
    {
        if (keycapRect != null && keycapText != null)
        {
            float textPreferred = keycapText.preferredWidth;
            float targetWidth = Mathf.Max(48f, textPreferred + 18f);
            Vector2 size = keycapRect.sizeDelta;
            size.x = targetWidth;
            size.y = 48f;
            keycapRect.sizeDelta = size;

            var kle = keycapRect.GetComponent<LayoutElement>();
            if (kle == null && GetComponent<HorizontalLayoutGroup>() != null)
                kle = keycapRect.gameObject.AddComponent<LayoutElement>();
            if (kle != null)
            {
                kle.minWidth = targetWidth;
                kle.preferredWidth = targetWidth;
                kle.minHeight = 48f;
                kle.preferredHeight = 48f;
            }
        }
    }

    public void SetPrompt(string key, string action)
    {
        fallbackKey = key;
        UpdateCachedKeyCode();
        if (actionLabel != null) actionLabel.text = action;
        if (keycapText != null) keycapText.text = key;
        AdjustKeycapWidth();
    }

    public void SetActionText(string action)
    {
        if (actionLabel != null) actionLabel.text = action;
    }

    public void SetKey(string key)
    {
        fallbackKey = key;
        UpdateCachedKeyCode();
        if (keycapText != null) keycapText.text = key;
        AdjustKeycapWidth();
    }

    public void SetLocked(bool locked)
    {
        isLocked = locked;
        if (promptCanvasGroup != null)
        {
            promptCanvasGroup.alpha = locked ? 0.40f : 1.0f;
        }
        else
        {
            if (keycapImage != null)
            {
                Color c = isPressed ? heldColor : normalColor;
                c.a = locked ? 0.40f : 1.0f;
                keycapImage.color = c;
            }
            if (keycapText != null)
            {
                Color tc = textColor;
                tc.a = locked ? 0.40f : 1.0f;
                keycapText.color = tc;
            }
            if (actionLabel != null)
            {
                Color ac = actionLabel.color;
                ac.a = locked ? 0.40f : 1.0f;
                actionLabel.color = ac;
            }
        }

        if (locked)
        {
            isPressed = false;
            isHovered = false;
            if (keycapRect != null)
            {
                keycapRect.anchoredPosition = new Vector2(keycapRect.anchoredPosition.x, GetBaselineY());
                keycapRect.localScale = Vector3.one;
            }
        }
    }

    private void UpdateCachedKeyCode()
    {
        if (string.IsNullOrEmpty(fallbackKey))
        {
            cachedKeyCode = KeyCode.E;
            return;
        }

        string upper = fallbackKey.Trim().ToUpperInvariant();
        if (upper == "E") cachedKeyCode = KeyCode.E;
        else if (upper == "Q") cachedKeyCode = KeyCode.Q;
        else if (upper == "W") cachedKeyCode = KeyCode.W;
        else if (upper == "A") cachedKeyCode = KeyCode.A;
        else if (upper == "S") cachedKeyCode = KeyCode.S;
        else if (upper == "D") cachedKeyCode = KeyCode.D;
        else if (upper == "TAB") cachedKeyCode = KeyCode.Tab;
        else if (upper == "SPACE") cachedKeyCode = KeyCode.Space;
        else if (upper == "CTRL" || upper == "LEFT CTRL" || upper == "CONTROL") cachedKeyCode = KeyCode.LeftControl;
        else if (upper == "SHIFT" || upper == "LEFT SHIFT") cachedKeyCode = KeyCode.LeftShift;
        else if (upper == "ESC" || upper == "ESCAPE") cachedKeyCode = KeyCode.Escape;
        else if (upper == "RETURN" || upper == "ENTER") cachedKeyCode = KeyCode.Return;
        else if (System.Enum.TryParse<KeyCode>(fallbackKey, true, out var parsed))
        {
            cachedKeyCode = parsed;
        }
        else
        {
            cachedKeyCode = KeyCode.None;
        }
    }

    private void Update()
    {
        if (isLocked) return;

        CheckInputState();
        AnimateVisuals();
    }

    private void CheckInputState()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        if (inputActionReference != null && inputActionReference.action != null)
        {
            pressed = inputActionReference.action.IsPressed();
        }
        else
#endif
        {
            if (cachedKeyCode != KeyCode.None && Input.GetKey(cachedKeyCode))
            {
                pressed = true;
            }
#if ENABLE_INPUT_SYSTEM
            if (!pressed && Keyboard.current != null)
            {
                var k = Keyboard.current;
                if (cachedKeyCode == KeyCode.E && k.eKey.isPressed) pressed = true;
                else if (cachedKeyCode == KeyCode.Q && k.qKey.isPressed) pressed = true;
                else if (cachedKeyCode == KeyCode.Tab && k.tabKey.isPressed) pressed = true;
                else if (cachedKeyCode == KeyCode.Space && k.spaceKey.isPressed) pressed = true;
            }
#endif
        }

        isPressed = pressed;
    }

    private void AnimateVisuals()
    {
        if (keycapRect == null) return;

        float pressY = isPressed ? -pressDepth : (isHovered ? 2f : 0f);
        Vector2 curPos = keycapRect.anchoredPosition;
        float targetY = GetBaselineY() + pressY;
        keycapRect.anchoredPosition = new Vector2(curPos.x, Mathf.Lerp(curPos.y, targetY, Time.unscaledDeltaTime * 25f));

        if (keycapImage != null)
        {
            Color targetCol = isPressed ? heldColor : (isHovered ? new Color(Mathf.Min(1f, normalColor.r * 1.15f), Mathf.Min(1f, normalColor.g * 1.15f), Mathf.Min(1f, normalColor.b * 1.15f), normalColor.a) : normalColor);
            keycapImage.color = Color.Lerp(keycapImage.color, targetCol, Time.unscaledDeltaTime * 25f);
        }

        if (!isPressed)
        {
            float pulse = 1f + (idlePulseScale - 1f) * Mathf.Sin(Time.unscaledTime * 3.5f);
            keycapRect.localScale = new Vector3(pulse, pulse, 1f);
        }
        else
        {
            keycapRect.localScale = Vector3.one * 0.96f;
        }
    }

    public void UpdateKeyDisplay()
    {
        string displayStr = fallbackKey;

#if ENABLE_INPUT_SYSTEM
        if (inputActionReference != null && inputActionReference.action != null)
        {
            string binding = inputActionReference.action.GetBindingDisplayString();
            if (!string.IsNullOrEmpty(binding))
            {
                displayStr = binding;
            }
        }

        if (Gamepad.current != null)
        {
            if (displayStr == "E" || displayStr == "Interact")
            {
                displayStr = "X";
            }
        }
#endif

        if (keycapText != null && !string.IsNullOrEmpty(displayStr))
        {
            keycapText.text = displayStr.ToUpperInvariant();
            AdjustKeycapWidth();
        }
    }
}
