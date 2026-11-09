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
    [SerializeField] private Color _hungerNormalColor = Color.white;
    [SerializeField] private Color _hungerCriticalColor = new Color(1.0f, 0.4f, 0.35f, 1f);
    [SerializeField] private Color _staminaNormalColor = Color.white;
    [SerializeField] private Color _staminaExhaustedColor = new Color(1.0f, 0.4f, 0.35f, 1f);

    [Header("Text Format")]
    [SerializeField] private string _hungerTextPrefix = "HUNGER ";
    [SerializeField] private string _staminaTextPrefix = "STAMINA ";

    [Header("Animation Settings")]
    [SerializeField] private float _fillLerpSpeed = 10f;
    [SerializeField] private float _criticalThreshold = 0.20f;

    private float _displayHunger = 1f;
    private float _displayStamina = 1f;
    private Vector3 _hungerBadgeBaseScale = Vector3.one;
    private Vector3 _staminaBadgeBaseScale = Vector3.one;

    private void Awake()
    {
        if (_hungerBadge != null) _hungerBadgeBaseScale = _hungerBadge.localScale;
        if (_staminaBadge != null) _staminaBadgeBaseScale = _staminaBadge.localScale;
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
            bool isLowHunger = targetHunger <= _criticalThreshold;
            _hungerFillImage.color = isLowHunger
                ? Color.Lerp(_hungerNormalColor, _hungerCriticalColor, Mathf.PingPong(Time.time * 3f, 1f))
                : _hungerNormalColor;
        }

        if (_hungerText != null)
        {
            _hungerText.text = $"{_hungerTextPrefix}{Mathf.CeilToInt(stats.CurrentHunger)}%";
        }

        // Pulse hunger badge if low
        if (_hungerBadge != null)
        {
            if (targetHunger <= _criticalThreshold)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.08f;
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
                ? Color.Lerp(_staminaNormalColor, _staminaExhaustedColor, Mathf.PingPong(Time.time * 4f, 1f))
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
                float pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.10f;
                _staminaBadge.localScale = _staminaBadgeBaseScale * pulse;
            }
            else
            {
                _staminaBadge.localScale = _staminaBadgeBaseScale;
            }
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
}
