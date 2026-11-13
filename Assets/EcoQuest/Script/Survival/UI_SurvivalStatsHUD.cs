using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the UI presentation of the Hunger and Stamina bars.
/// Smoothly animates fill changes, displays percentages, and provides visual alerts when stats are critical.
/// </summary>
public class UI_SurvivalStatsHUD : MonoBehaviour
{
    [Header("Hunger UI Elements")]
    [SerializeField] private Image _hungerFillImage;
    [SerializeField] private TextMeshProUGUI _hungerText;
    [SerializeField] private RectTransform _hungerBadge;

    [Header("Stamina UI Elements")]
    [SerializeField] private Image _staminaFillImage;
    [SerializeField] private TextMeshProUGUI _staminaText;
    [SerializeField] private RectTransform _staminaBadge;

    [Header("Color Schemes")]
    [SerializeField] private Color _hungerNormalColor = new Color(1.0f, 0.58f, 0.12f, 1f);
    [SerializeField] private Color _hungerWarningColor = new Color(1.0f, 0.84f, 0.18f, 1f);
    [SerializeField] private Color _hungerCriticalColor = new Color(0.92f, 0.22f, 0.20f, 1f);
    [SerializeField] private Color _staminaNormalColor = Color.white;
    [SerializeField] private Color _staminaExhaustedColor = new Color(1.0f, 0.4f, 0.35f, 1f);

    [Header("Text Format")]
    [SerializeField] private string _hungerTextPrefix = "HUNGER ";
    [SerializeField] private string _staminaTextPrefix = "STAMINA ";

    [Header("Active Buffs & Timers HUD (Optional / Auto-Generated)")]
    [SerializeField] private GameObject _buffsContainerGo;
    [SerializeField] private GameObject _speedBuffPillGo;
    [SerializeField] private TextMeshProUGUI _speedBuffTimerText;
    [SerializeField] private Image _speedBuffIcon;
    [SerializeField] private GameObject _staminaBuffPillGo;
    [SerializeField] private TextMeshProUGUI _staminaBuffTimerText;
    [SerializeField] private Image _staminaBuffIcon;

    [Header("Animation Settings")]
    [SerializeField] private float _fillLerpSpeed = 10f;
    [SerializeField] private float _criticalThreshold = 0.20f;

    private float _displayHunger = 1f;
    private float _displayStamina = 1f;
    private Vector3 _hungerBadgeBaseScale = Vector3.one;
    private Vector3 _staminaBadgeBaseScale = Vector3.one;
    private GameObject _needsToastGo;
    private CanvasGroup _needsToastGroup;
    private TextMeshProUGUI _needsToastText;
    private Coroutine _needsToastRoutine;

    private void OnEnable()
    {
        PlayerSurvivalStats.OnHungerRumbleWarning -= HandleHungerRumbleWarning;
        PlayerSurvivalStats.OnHungerRumbleWarning += HandleHungerRumbleWarning;
    }

    private void OnDisable()
    {
        PlayerSurvivalStats.OnHungerRumbleWarning -= HandleHungerRumbleWarning;
        StopAllCoroutines();
        _needsToastRoutine = null;
        if (_needsToastGroup != null) _needsToastGroup.alpha = 0f;
        if (_needsToastGo != null) _needsToastGo.SetActive(false);
    }

    private void HandleHungerRumbleWarning(int thresholdPercent)
    {
        ShowNeedsToast("Your tummy is rumbling! Eat something from your bag.", 4.0f);
    }

    private void Awake()
    {
        if (_hungerBadge != null) _hungerBadgeBaseScale = _hungerBadge.localScale;
        if (_staminaBadge != null) _staminaBadgeBaseScale = _staminaBadge.localScale;

        // Clean up visual notches so text stays crisp and unbroken
        var hungerNotches = transform.Find("Container_PlayerStats/Row_Hunger/BarTrack/Notches");
        if (hungerNotches != null) hungerNotches.gameObject.SetActive(false);
        var staminaNotches = transform.Find("Container_PlayerStats/Row_Stamina/BarTrack/Notches");
        if (staminaNotches != null) staminaNotches.gameObject.SetActive(false);

        // Delete any leftover misplaced LowerBuffPill
        var lower = transform.Find("LowerBuffPill");
        if (lower != null) Destroy(lower.gameObject);
    }

    private void Start()
    {
        // Initialize fill instantly on start
        if (PlayerSurvivalStats.Instance != null)
        {
            _displayHunger = PlayerSurvivalStats.Instance.HungerNormalized;
            _displayStamina = PlayerSurvivalStats.Instance.StaminaNormalized;
            if (_hungerFillImage != null) _hungerFillImage.fillAmount = _displayHunger;
            if (_staminaFillImage != null) _staminaFillImage.fillAmount = _displayStamina;
        }
    }

    private void Update()
    {
        PlayerSurvivalStats stats = PlayerSurvivalStats.Instance;
        if (stats == null) return;

        float dt = Time.unscaledDeltaTime;

        // Smooth Lerp Hunger Fill
        float targetHunger = stats.HungerNormalized;
        _displayHunger = Mathf.Lerp(_displayHunger, targetHunger, dt * _fillLerpSpeed);
        if (_hungerFillImage != null)
        {
            _hungerFillImage.fillAmount = _displayHunger;
            if (targetHunger < 0.15f)
            {
                Color deepRed = new Color(0.78f, 0.14f, 0.14f, 1f);
                _hungerFillImage.color = Color.Lerp(deepRed, _hungerCriticalColor, Mathf.PingPong(Time.unscaledTime * 3f, 1f));
            }
            else if (targetHunger < 0.30f)
            {
                _hungerFillImage.color = _hungerWarningColor;
            }
            else
            {
                _hungerFillImage.color = _hungerNormalColor;
            }
        }

        if (_hungerText != null)
        {
            _hungerText.text = $"{_hungerTextPrefix}{Mathf.CeilToInt(stats.CurrentHunger)}%";
        }

        // Pulse hunger badge if low
        if (_hungerBadge != null)
        {
            if (targetHunger < 0.15f)
            {
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.08f;
                _hungerBadge.localScale = _hungerBadgeBaseScale * pulse;
            }
            else
            {
                _hungerBadge.localScale = _hungerBadgeBaseScale;
            }
        }

        // Smooth Lerp Stamina Fill
        float targetStamina = stats.StaminaNormalized;
        _displayStamina = Mathf.Lerp(_displayStamina, targetStamina, dt * _fillLerpSpeed);
        if (_staminaFillImage != null)
        {
            _staminaFillImage.fillAmount = _displayStamina;
            bool isExhausted = stats.IsExhausted || targetStamina <= _criticalThreshold;
            _staminaFillImage.color = isExhausted
                ? Color.Lerp(_staminaNormalColor, _staminaExhaustedColor, Mathf.PingPong(Time.unscaledTime * 4f, 1f))
                : _staminaNormalColor;
        }

        if (_staminaText != null)
        {
            _staminaText.text = $"{_staminaTextPrefix}{Mathf.CeilToInt(stats.CurrentStamina)}%";
        }

        // Pulse stamina badge if exhausted
        if (_staminaBadge != null)
        {
            if (stats.IsExhausted)
            {
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 8f) * 0.10f;
                _staminaBadge.localScale = _staminaBadgeBaseScale * pulse;
            }
            else
            {
                _staminaBadge.localScale = _staminaBadgeBaseScale;
            }
        }

        UpdateBuffsHud(stats);
    }

    private void UpdateBuffsHud(PlayerSurvivalStats stats)
    {
        EnsureBuffsHudSetup();
        if (_buffsContainerGo == null) return;

        bool hasSpeed = stats.HasActiveSpeedBuff;
        bool hasStamina = stats.HasActiveStaminaBuff;

        if (!hasSpeed && !hasStamina)
        {
            if (_buffsContainerGo.activeSelf) _buffsContainerGo.SetActive(false);
            return;
        }

        if (!_buffsContainerGo.activeSelf) _buffsContainerGo.SetActive(true);

        // Update Speed Buff Pill
        if (_speedBuffPillGo != null)
        {
            if (hasSpeed)
            {
                if (!_speedBuffPillGo.activeSelf) _speedBuffPillGo.SetActive(true);
                int sec = Mathf.CeilToInt(stats.SpeedBuffRemaining);
                if (_speedBuffTimerText != null)
                    _speedBuffTimerText.text = $"{sec / 60}:{sec % 60:00}";
            }
            else if (_speedBuffPillGo.activeSelf)
            {
                _speedBuffPillGo.SetActive(false);
            }
        }

        // Update Stamina Buff Pill
        if (_staminaBuffPillGo != null)
        {
            if (hasStamina)
            {
                if (!_staminaBuffPillGo.activeSelf) _staminaBuffPillGo.SetActive(true);
                int sec = Mathf.CeilToInt(stats.StaminaBuffRemaining);
                if (_staminaBuffTimerText != null)
                    _staminaBuffTimerText.text = $"{sec / 60}:{sec % 60:00}";
            }
            else if (_staminaBuffPillGo.activeSelf)
            {
                _staminaBuffPillGo.SetActive(false);
            }
        }
    }

    private void EnsureBuffsHudSetup()
    {
        // Delete any leftover LowerBuffPill
        var lower = transform.Find("LowerBuffPill");
        if (lower != null) Destroy(lower.gameObject);

        if (_buffsContainerGo != null)
        {
            var crt = _buffsContainerGo.GetComponent<RectTransform>();
            if (crt != null)
            {
                crt.anchorMin = new Vector2(0f, 1f);
                crt.anchorMax = new Vector2(0f, 1f);
                crt.pivot = new Vector2(0f, 1f);
                crt.anchoredPosition = new Vector2(40f, -128f);
                crt.sizeDelta = new Vector2(260f, 36f);
            }
            var title = _buffsContainerGo.transform.Find("TitleText");
            if (title != null) Destroy(title.gameObject);
            return;
        }

        Transform existing = transform.Find("ActiveBuffsContainer");
        if (existing != null)
        {
            _buffsContainerGo = existing.gameObject;
            var crt = existing.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.anchoredPosition = new Vector2(40f, -128f);
            crt.sizeDelta = new Vector2(260f, 36f);

            var speedPill = existing.Find("SpeedBuffPill");
            if (speedPill != null)
            {
                _speedBuffPillGo = speedPill.gameObject;
                _speedBuffTimerText = speedPill.GetComponentInChildren<TextMeshProUGUI>(true);
                var img = speedPill.Find("Icon");
                if (img != null) _speedBuffIcon = img.GetComponent<Image>();
            }
            var stamPill = existing.Find("StaminaBuffPill");
            if (stamPill != null)
            {
                _staminaBuffPillGo = stamPill.gameObject;
                _staminaBuffTimerText = stamPill.GetComponentInChildren<TextMeshProUGUI>(true);
                var img = stamPill.Find("Icon");
                if (img != null) _staminaBuffIcon = img.GetComponent<Image>();
            }
            var title = existing.Find("TitleText");
            if (title != null) Destroy(title.gameObject);
            return;
        }

        var containerGo = new GameObject("ActiveBuffsContainer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        containerGo.transform.SetParent(transform, false);
        var contRt = containerGo.GetComponent<RectTransform>();
        contRt.anchorMin = new Vector2(0f, 1f);
        contRt.anchorMax = new Vector2(0f, 1f);
        contRt.pivot = new Vector2(0f, 1f);
        contRt.anchoredPosition = new Vector2(40f, -128f);
        contRt.sizeDelta = new Vector2(260f, 36f);

        var hlg = containerGo.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        _buffsContainerGo = containerGo;

        _speedBuffPillGo = CreateBuffPill(containerGo.transform, "SpeedBuffPill", "hud_badge_boot_cyan", new Color(0.18f, 0.70f, 0.90f, 0.95f), out _speedBuffTimerText, out _speedBuffIcon);
        _staminaBuffPillGo = CreateBuffPill(containerGo.transform, "StaminaBuffPill", "hud_badge_can_green", new Color(0.22f, 0.80f, 0.42f, 0.95f), out _staminaBuffTimerText, out _staminaBuffIcon);

        _buffsContainerGo.SetActive(false);
    }

    private GameObject CreateBuffPill(Transform parent, string name, string iconSpriteName, Color accent, out TextMeshProUGUI timerTxt, out Image iconImg)
    {
        var pillGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(HorizontalLayoutGroup));
        pillGo.transform.SetParent(parent, false);
        var prt = pillGo.GetComponent<RectTransform>();
        prt.sizeDelta = new Vector2(96f, 36f);

        var bg = pillGo.GetComponent<Image>();
        bg.color = new Color(0.04f, 0.12f, 0.08f, 0.92f);

        var outl = pillGo.GetComponent<Outline>();
        outl.effectColor = accent;
        outl.effectDistance = new Vector2(1.5f, -1.5f);

        var hlg = pillGo.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(6, 8, 4, 4);
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;

        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(pillGo.transform, false);
        var irt = iconGo.GetComponent<RectTransform>();
        irt.sizeDelta = new Vector2(26f, 26f);
        iconImg = iconGo.GetComponent<Image>();
        iconImg.preserveAspect = true;

#if UNITY_EDITOR
        string path = $"Assets/EcoQuest/Inventory/Sprites/UI/{iconSpriteName}.png";
        var sp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sp == null) sp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/EcoQuest/Inventory/Sprites/{iconSpriteName}.png");
        if (sp != null) iconImg.sprite = sp;
#endif

        var txtGo = new GameObject("TimerText", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtGo.transform.SetParent(pillGo.transform, false);
        var trt = txtGo.GetComponent<RectTransform>();
        trt.sizeDelta = new Vector2(50f, 26f);
        timerTxt = txtGo.GetComponent<TextMeshProUGUI>();
        if (_hungerText != null && _hungerText.font != null) timerTxt.font = _hungerText.font;
        timerTxt.text = "0:00";
        timerTxt.fontSize = 17f;
        timerTxt.fontStyle = FontStyles.Bold;
        timerTxt.alignment = TextAlignmentOptions.Center;
        timerTxt.color = Color.white;

        pillGo.SetActive(false);
        return pillGo;
    }

    public void SetPreviewReferenceState(bool enabled)
    {
        EnsureBuffsHudSetup();

        if (enabled)
        {
            if (_hungerFillImage != null) _hungerFillImage.fillAmount = 1f;
            if (_staminaFillImage != null) _staminaFillImage.fillAmount = 1f;
            if (_hungerText != null) _hungerText.text = "HUNGER 100%";
            if (_staminaText != null) _staminaText.text = "STAMINA 100%";

            if (_buffsContainerGo != null) _buffsContainerGo.SetActive(true);
            if (_speedBuffPillGo != null)
            {
                _speedBuffPillGo.SetActive(true);
                if (_speedBuffTimerText != null) _speedBuffTimerText.text = "0:58";
            }
            if (_staminaBuffPillGo != null)
            {
                _staminaBuffPillGo.SetActive(true);
                if (_staminaBuffTimerText != null) _staminaBuffTimerText.text = "1:45";
            }
        }
        else
        {
            if (_buffsContainerGo != null) _buffsContainerGo.SetActive(false);
        }
    }

    public void SetupReferences(
        Image hungerFill, TextMeshProUGUI hungerTxt, RectTransform hungerBdg,
        Image staminaFill, TextMeshProUGUI staminaTxt, RectTransform staminaBdg)
    {
        _hungerFillImage = hungerFill;
        _hungerText = hungerTxt;
        _hungerBadge = hungerBdg;

        _staminaFillImage = staminaFill;
        _staminaText = staminaTxt;
        _staminaBadge = staminaBdg;

        if (_hungerBadge != null) _hungerBadgeBaseScale = _hungerBadge.localScale;
        if (_staminaBadge != null) _staminaBadgeBaseScale = _staminaBadge.localScale;
    }

    public void ShowFloatingGain(bool isHunger, float amount)
    {
        RectTransform targetBar = isHunger
            ? (_hungerFillImage != null ? _hungerFillImage.rectTransform : _hungerBadge)
            : (_staminaFillImage != null ? _staminaFillImage.rectTransform : _staminaBadge);
        Transform parent = targetBar != null && targetBar.parent != null ? targetBar.parent : transform;

        string statName = isHunger ? "Hunger" : "Stamina";
        string text = $"+{Mathf.RoundToInt(amount)} {statName}";
        Color color = isHunger ? new Color(1f, 0.72f, 0.22f, 1f) : new Color(0.43f, 0.95f, 0.50f, 1f);

        if (isActiveAndEnabled && Application.isPlaying)
        {
            StartCoroutine(FloatingGainRoutine(parent, text, color));
        }
    }

    private System.Collections.IEnumerator FloatingGainRoutine(Transform parent, string text, Color color)
    {
        var go = new GameObject("FloatingStatGain", typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        Vector2 startPos = new Vector2(12f, 0f);
        rt.anchoredPosition = startPos;
        rt.sizeDelta = new Vector2(200f, 36f);

        var cg = go.GetComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (_hungerText != null && _hungerText.font != null) tmp.font = _hungerText.font;
        tmp.text = text;
        tmp.fontSize = 22f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        float duration = 0.9f;
        float elapsed = 0f;
        while (elapsed < duration && go != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rt.anchoredPosition = startPos + new Vector2(0f, t * 24f);
            cg.alpha = t < 0.2f ? (t / 0.2f) : (1f - (t - 0.2f) / 0.8f);
            yield return null;
        }

        if (go != null) Destroy(go);
    }

    public void RefreshImmediateFromStats()
    {
        PlayerSurvivalStats stats = PlayerSurvivalStats.Instance;
        if (stats == null) return;
        _displayHunger = stats.HungerNormalized;
        _displayStamina = stats.StaminaNormalized;
        if (_hungerFillImage != null) _hungerFillImage.fillAmount = _displayHunger;
        if (_staminaFillImage != null) _staminaFillImage.fillAmount = _displayStamina;
        if (_hungerText != null) _hungerText.text = $"{_hungerTextPrefix}{Mathf.CeilToInt(stats.CurrentHunger)}%";
        if (_staminaText != null) _staminaText.text = $"{_staminaTextPrefix}{Mathf.CeilToInt(stats.CurrentStamina)}%";
    }

    public void ShowNeedsToast(string message, float duration = 4.0f)
    {
        if (string.IsNullOrEmpty(message)) return;
        EnsureNeedsToastSetup();
        if (_needsToastGo == null || _needsToastText == null) return;

        _needsToastText.text = message;
        Debug.Log($"[Needs] {message}");

        if (!isActiveAndEnabled || !Application.isPlaying) return;
        if (_needsToastRoutine != null) StopCoroutine(_needsToastRoutine);
        _needsToastGo.SetActive(true);
        _needsToastRoutine = StartCoroutine(NeedsToastRoutine(duration));
    }

    private void EnsureNeedsToastSetup()
    {
        if (_needsToastGo != null) return;

        Transform existing = transform.Find("SurvivalToastBanner");
        if (existing != null)
        {
            _needsToastGo = existing.gameObject;
            _needsToastGroup = _needsToastGo.GetComponent<CanvasGroup>();
            _needsToastText = _needsToastGo.GetComponentInChildren<TextMeshProUGUI>(true);
            return;
        }

        UITheme theme = UITheme.Instance;
        _needsToastGo = new GameObject("SurvivalToastBanner", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline));
        _needsToastGo.transform.SetParent(transform, false);

        var rt = _needsToastGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -24f);
        rt.sizeDelta = new Vector2(620f, 48f);

        _needsToastGroup = _needsToastGo.GetComponent<CanvasGroup>();
        _needsToastGroup.blocksRaycasts = false;
        _needsToastGroup.interactable = false;
        _needsToastGroup.alpha = 0f;

        var bg = _needsToastGo.GetComponent<Image>();
        bg.color = theme != null ? theme.panelColor : new Color(0.043f, 0.141f, 0.098f, 0.97f);
        if (theme != null && theme.panelBackgroundSprite != null)
        {
            bg.sprite = theme.panelBackgroundSprite;
            bg.type = Image.Type.Sliced;
        }
        bg.raycastTarget = false;

        var outline = _needsToastGo.GetComponent<Outline>();
        outline.effectColor = theme != null ? theme.borderColor : new Color(0.427f, 0.745f, 0.427f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        var txtGo = new GameObject("ToastText", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtGo.transform.SetParent(_needsToastGo.transform, false);
        var trt = txtGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(18f, 6f);
        trt.offsetMax = new Vector2(-18f, -6f);

        _needsToastText = txtGo.GetComponent<TextMeshProUGUI>();
        if (_hungerText != null && _hungerText.font != null) _needsToastText.font = _hungerText.font;
        else if (theme != null && theme.GetBodyFont() != null) _needsToastText.font = theme.GetBodyFont();
        _needsToastText.fontSize = 20f;
        _needsToastText.fontStyle = FontStyles.Bold;
        _needsToastText.alignment = TextAlignmentOptions.Center;
        _needsToastText.color = new Color(0.96f, 0.95f, 0.92f, 1f);
        _needsToastText.textWrappingMode = TextWrappingModes.Normal;
        _needsToastText.enableAutoSizing = true;
        _needsToastText.fontSizeMin = 14f;
        _needsToastText.fontSizeMax = 20f;
        _needsToastText.raycastTarget = false;

        _needsToastGo.SetActive(false);
    }

    private System.Collections.IEnumerator NeedsToastRoutine(float duration)
    {
        float elapsed = 0f;
        while (elapsed < 0.2f && _needsToastGroup != null)
        {
            elapsed += Time.unscaledDeltaTime;
            _needsToastGroup.alpha = Mathf.Clamp01(elapsed / 0.2f);
            yield return null;
        }
        if (_needsToastGroup != null) _needsToastGroup.alpha = 1f;

        float hold = 0f;
        while (hold < duration)
        {
            hold += Time.unscaledDeltaTime;
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < 0.3f && _needsToastGroup != null)
        {
            elapsed += Time.unscaledDeltaTime;
            _needsToastGroup.alpha = 1f - Mathf.Clamp01(elapsed / 0.3f);
            yield return null;
        }

        if (_needsToastGroup != null) _needsToastGroup.alpha = 0f;
        if (_needsToastGo != null) _needsToastGo.SetActive(false);
        _needsToastRoutine = null;
    }
}
