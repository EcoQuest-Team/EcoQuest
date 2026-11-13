using System;
using UnityEngine;

/// <summary>
/// Manages player survival stats: Hunger and Stamina.
/// - Hunger slowly depletes over time and with physical exertion (sprinting). Food restores it.
/// - Stamina depletes rapidly while sprinting and regenerates when idle/walking.
/// - Exhaustion prevents sprinting until stamina recovers to a safe threshold.
/// </summary>
public class PlayerSurvivalStats : MonoBehaviour
{
    public static PlayerSurvivalStats Instance { get; private set; }

    [Header("Hunger Settings")]
    [Tooltip("Maximum hunger value.")]
    public float maxHunger = 100f;
    [Tooltip("Current hunger value.")]
    public float currentHunger = 100f;
    [Tooltip("Passive hunger drain rate per second (default 1/15 = 1% every 15s, ~25 mins full-to-empty).")]
    public float hungerDepletionPerSec = 1f / 15f;
    [Tooltip("Extra hunger drain rate per second while actively sprinting (capped at +50% of passive rate).")]
    public float sprintHungerDepletionPerSec = 1f / 30f;

    [Header("Stamina Settings")]
    [Tooltip("Maximum stamina value.")]
    public float maxStamina = 100f;
    [Tooltip("Current stamina value.")]
    public float currentStamina = 100f;
    [Tooltip("Stamina drain rate per second while sprinting.")]
    public float staminaDrainPerSec = 22f;
    [Tooltip("Stamina recovery rate per second while resting/walking.")]
    public float staminaRegenPerSec = 18f;
    [Tooltip("Delay in seconds after sprinting stops before stamina starts regenerating.")]
    public float regenDelay = 0.5f;
    [Tooltip("Minimum stamina percentage needed to resume sprinting after total exhaustion.")]
    public float exhaustionThreshold = 15f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool _isSprinting;
    [SerializeField] private bool _isExhausted;
    [SerializeField] private float _timeSinceLastSprint;

    [Header("Active Timed Buffs")]
    [SerializeField] private float _staminaBuffRemaining = 0f;
    [SerializeField] private float _staminaBuffDuration = 0f;
    [SerializeField] private float _staminaBuffPercent = 0f;
    [SerializeField] private float _speedBuffRemaining = 0f;
    [SerializeField] private float _speedBuffDuration = 0f;
    [SerializeField] private float _speedBuffPercent = 0f;

    // Events for UI and gameplay audio/effects
    public event Action<float, float> OnHungerChanged;
    public event Action<float, float> OnStaminaChanged;
    public event Action OnExhausted;
    public event Action OnExhaustionRecovered;
    public static event Action<int> OnHungerRumbleWarning;

    private bool _rumble20Shown;
    private bool _rumble0Shown;
    private float _lastHungerSaveUnscaledTime = -999f;

    // Cached scene references so Update() never calls FindFirstObjectByType.
    private UIOrchestrator _cachedOrchestrator;
    private ModalManager _cachedModalManager;
    private DialoguePresenter _cachedDialoguePresenter;
    private HudVisibilityService _cachedHudVisibility;
    private EcoQuest.Inventory.UI_EcoShop _cachedEcoShop;
    private IntroSequenceController _cachedIntroController;
    private ExplorerCinematicController _cachedExplorerCinematic;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticNeedsState()
    {
        Instance = null;
        OnHungerRumbleWarning = null;
    }

    public static void ClearSavedNeedsState()
    {
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerHungerRemaining);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerHungerRumble20Shown);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerHungerRumble0Shown);
        if (Instance != null)
        {
            Instance._rumble20Shown = false;
            Instance._rumble0Shown = false;
            Instance.currentHunger = Instance.maxHunger;
            Instance.OnHungerChanged?.Invoke(Instance.currentHunger, Instance.maxHunger);
        }
    }

    public bool CanSprint => !_isExhausted && currentStamina > 0.5f;
    public bool IsSprinting => _isSprinting;
    public bool IsExhausted => _isExhausted;
    public float CurrentHunger => currentHunger;
    public float CurrentStamina => currentStamina;
    public float HungerNormalized => maxHunger > 0f ? Mathf.Clamp01(currentHunger / maxHunger) : 0f;
    public float StaminaNormalized => maxStamina > 0f ? Mathf.Clamp01(currentStamina / maxStamina) : 0f;

    public bool HasActiveStaminaBuff => _staminaBuffRemaining > 0.01f;
    public float StaminaBuffRemaining => _staminaBuffRemaining;
    public float StaminaBuffDuration => _staminaBuffDuration;
    public float StaminaBuffPercent => _staminaBuffPercent;
    public float StaminaBuffNormalized => _staminaBuffDuration > 0.01f ? Mathf.Clamp01(_staminaBuffRemaining / _staminaBuffDuration) : 0f;

    public bool HasActiveSpeedBuff => _speedBuffRemaining > 0.01f;
    public float SpeedBuffRemaining => _speedBuffRemaining;
    public float SpeedBuffDuration => _speedBuffDuration;
    public float SpeedBuffPercent => _speedBuffPercent;
    public float SpeedBuffNormalized => _speedBuffDuration > 0.01f ? Mathf.Clamp01(_speedBuffRemaining / _speedBuffDuration) : 0f;

    /// <summary>
    /// Balanced kid-friendly movement speed multiplier while Speed Boost is active.
    /// A 5%/15% tier-1 boost grants 1.18x speed, and a 10%/25% tier-2 boost grants 1.30x speed so players clearly feel faster!
    /// </summary>
    public float SpeedMultiplier
    {
        get
        {
            if (!HasActiveSpeedBuff) return 1f;
            float effectiveBonus = _speedBuffPercent >= 10f ? Mathf.Max(0.25f, _speedBuffPercent * 0.01f) : Mathf.Max(0.16f, _speedBuffPercent * 0.01f);
            return 1f + Mathf.Clamp(effectiveBonus, 0.15f, 0.40f);
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        maxHunger = Mathf.Max(1f, maxHunger);
        maxStamina = Mathf.Max(1f, maxStamina);
        currentHunger = PlayerPrefs.HasKey(EcoQuestSaveKeys.PlayerHungerRemaining)
            ? Mathf.Clamp(PlayerPrefs.GetFloat(EcoQuestSaveKeys.PlayerHungerRemaining, maxHunger), 0f, maxHunger)
            : Mathf.Clamp(currentHunger, 0f, maxHunger);
        _rumble20Shown = PlayerPrefs.GetInt(EcoQuestSaveKeys.PlayerHungerRumble20Shown, 0) == 1 || currentHunger <= 20f;
        _rumble0Shown = PlayerPrefs.GetInt(EcoQuestSaveKeys.PlayerHungerRumble0Shown, 0) == 1 || currentHunger <= 0f;
        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
    }

    private void Start()
    {
        _cachedOrchestrator = UnityEngine.Object.FindFirstObjectByType<UIOrchestrator>(FindObjectsInactive.Include);
        _cachedModalManager = UnityEngine.Object.FindFirstObjectByType<ModalManager>(FindObjectsInactive.Include);
        _cachedDialoguePresenter = UnityEngine.Object.FindFirstObjectByType<DialoguePresenter>(FindObjectsInactive.Include);
        _cachedHudVisibility = UnityEngine.Object.FindFirstObjectByType<HudVisibilityService>(FindObjectsInactive.Include);
        _cachedEcoShop = UnityEngine.Object.FindFirstObjectByType<EcoQuest.Inventory.UI_EcoShop>(FindObjectsInactive.Include);
        _cachedIntroController = UnityEngine.Object.FindFirstObjectByType<IntroSequenceController>(FindObjectsInactive.Include);
        _cachedExplorerCinematic = UnityEngine.Object.FindFirstObjectByType<ExplorerCinematicController>(FindObjectsInactive.Include);
        OnHungerChanged?.Invoke(currentHunger, maxHunger);
    }

    private void OnDisable()
    {
        FlushNeedsSave();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus) FlushNeedsSave();
    }

    private void OnApplicationQuit()
    {
        FlushNeedsSave();
    }

    private void OnDestroy()
    {
        FlushNeedsSave();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        UpdateActiveBuffs(dt);
        UpdateHunger(dt);
        UpdateStamina(dt);
    }

    private void UpdateActiveBuffs(float dt)
    {
        if (_staminaBuffRemaining > 0f)
        {
            _staminaBuffRemaining = Mathf.Max(0f, _staminaBuffRemaining - dt);
            if (_staminaBuffRemaining <= 0f)
            {
                _staminaBuffPercent = 0f;
                _staminaBuffDuration = 0f;
            }
        }

        if (_speedBuffRemaining > 0f)
        {
            _speedBuffRemaining = Mathf.Max(0f, _speedBuffRemaining - dt);
            if (_speedBuffRemaining <= 0f)
            {
                _speedBuffPercent = 0f;
                _speedBuffDuration = 0f;
            }
        }
    }

    public bool IsHungerDrainPaused()
    {
        if (!Application.isPlaying) return true;
        if (PauseMenuController.IsPaused || Time.timeScale <= 0.001f) return true;
        if (LoadingScreenController.IsLoading) return true;
        if (EcoQuest.Inventory.InventoryController.Instance != null && EcoQuest.Inventory.InventoryController.Instance.IsOpen) return true;
        if (_cachedEcoShop != null && _cachedEcoShop.IsOpen) return true;
        if ((_cachedDialoguePresenter != null && _cachedDialoguePresenter.IsInDialogue) ||
            LitterbugQuestManager.IsDialogueOpen ||
            NPCInteraction.IsAnyGuardianFlowRunning) return true;
        if (QuizUIController.IsAnyQuizOpen) return true;
        if (_cachedHudVisibility != null && _cachedHudVisibility.IsHUDHidden) return true;
        if (_cachedModalManager != null && _cachedModalManager.IsAnyModalActuallyVisible()) return true;
        if (_cachedIntroController != null && _cachedIntroController.CurrentState != IntroSequenceController.IntroState.Gameplay) return true;
        if (_cachedExplorerCinematic != null && _cachedExplorerCinematic.IsInCinematic) return true;
        if (_cachedOrchestrator != null && (_cachedOrchestrator.AreModalsOpen || _cachedOrchestrator.IsOverlayActive)) return true;
        if (UIOrchestrator.IsCityScavengerOverlayOpenHook != null && UIOrchestrator.IsCityScavengerOverlayOpenHook()) return true;
        return false;
    }

    private void UpdateHunger(float dt)
    {
        if (IsHungerDrainPaused()) return;
        if (StepHungerDrain(dt, _isSprinting))
        {
            if (Time.unscaledTime - _lastHungerSaveUnscaledTime >= 1.0f || currentHunger <= 0f)
            {
                FlushNeedsSave();
            }
        }
    }

    /// <summary>
    /// Advances hunger depletion by <paramref name="dt"/> seconds using serialized rates and threshold notifications.
    /// Returns true if currentHunger changed.
    /// </summary>
    public bool StepHungerDrain(float dt, bool isSprinting)
    {
        if (dt <= 0f || currentHunger <= 0f) return false;

        float baseRate = Mathf.Max(0f, hungerDepletionPerSec);
        if (baseRate <= 0f) return false;

        float rate = baseRate;
        if (isSprinting && sprintHungerDepletionPerSec > 0f)
        {
            // Sprinting adds up to +50% faster drain over the passive rate.
            rate += Mathf.Min(sprintHungerDepletionPerSec, baseRate * 0.5f);
        }

        float prev = currentHunger;
        currentHunger = Mathf.Max(0f, currentHunger - rate * dt);
        if (Mathf.Approximately(prev, currentHunger)) return false;

        EvaluateRumbleWarnings(prev, currentHunger);
        OnHungerChanged?.Invoke(currentHunger, maxHunger);
        return true;
    }

    private void EvaluateRumbleWarnings(float prevHunger, float nextHunger)
    {
        if (prevHunger > 20f && nextHunger <= 20f && nextHunger > 0f && !_rumble20Shown)
        {
            _rumble20Shown = true;
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerRumble20Shown, 1);
            OnHungerRumbleWarning?.Invoke(20);
        }
        if (prevHunger > 0f && nextHunger <= 0f && !_rumble0Shown)
        {
            _rumble0Shown = true;
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerRumble0Shown, 1);
            OnHungerRumbleWarning?.Invoke(0);
        }
    }

    public void FlushNeedsSave()
    {
        if (!Application.isPlaying) return;
        _lastHungerSaveUnscaledTime = Time.unscaledTime;
        PlayerPrefs.SetFloat(EcoQuestSaveKeys.PlayerHungerRemaining, Mathf.Clamp(currentHunger, 0f, maxHunger));
        PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerRumble20Shown, _rumble20Shown ? 1 : 0);
        PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerRumble0Shown, _rumble0Shown ? 1 : 0);
    }

    public void SetHungerImmediate(float targetHunger, bool persistNow = true)
    {
        float prev = currentHunger;
        currentHunger = Mathf.Clamp(targetHunger, 0f, maxHunger);
        if (currentHunger > 20f) _rumble20Shown = false;
        if (currentHunger > 0f) _rumble0Shown = false;
        EvaluateRumbleWarnings(prev, currentHunger);
        if (persistNow)
        {
            PlayerPrefs.SetFloat(EcoQuestSaveKeys.PlayerHungerRemaining, currentHunger);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerRumble20Shown, _rumble20Shown ? 1 : 0);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerRumble0Shown, _rumble0Shown ? 1 : 0);
            PlayerPrefs.Save();
        }
        OnHungerChanged?.Invoke(currentHunger, maxHunger);
    }

    private void UpdateStamina(float dt)
    {
        // When Stamina Recovery buff is active:
        // - Stamina drain while sprinting is reduced by 30% (Tier 1) to 50% (Tier 2)
        // - Stamina recovery rate is boosted by +40% (Tier 1) to +75% (Tier 2) with near-instant regen delay
        float drainMultiplier = 1f;
        float regenMultiplier = 1f;
        float effectiveRegenDelay = regenDelay;

        if (HasActiveStaminaBuff)
        {
            bool isTier2 = _staminaBuffPercent >= 10f;
            drainMultiplier = isTier2 ? 0.50f : 0.68f;
            regenMultiplier = isTier2 ? 1.75f : 1.45f;
            effectiveRegenDelay = 0.12f;
        }

        if (_isSprinting)
        {
            _timeSinceLastSprint = 0f;
            if (currentStamina > 0f)
            {
                currentStamina = Mathf.Max(0f, currentStamina - (staminaDrainPerSec * drainMultiplier) * dt);
                OnStaminaChanged?.Invoke(currentStamina, maxStamina);

                if (currentStamina <= 0f && !_isExhausted)
                {
                    _isExhausted = true;
                    _isSprinting = false;
                    OnExhausted?.Invoke();
                }
            }
        }
        else
        {
            _timeSinceLastSprint += dt;
            if (_timeSinceLastSprint >= effectiveRegenDelay && currentStamina < maxStamina)
            {
                currentStamina = Mathf.Min(maxStamina, currentStamina + (staminaRegenPerSec * regenMultiplier) * dt);
                OnStaminaChanged?.Invoke(currentStamina, maxStamina);

                if (_isExhausted && currentStamina >= exhaustionThreshold)
                {
                    _isExhausted = false;
                    OnExhaustionRecovered?.Invoke();
                }
            }
        }
    }

    /// <summary>
    /// Notified by PlayerSprintController whether the player is currently actively sprinting.
    /// </summary>
    public void SetSprinting(bool sprinting)
    {
        if (sprinting && !CanSprint)
        {
            _isSprinting = false;
            return;
        }
        _isSprinting = sprinting;
    }

    /// <summary>
    /// Adds or restores hunger (e.g., when eating food items).
    /// </summary>
    public void AddHunger(float amount)
    {
        currentHunger = Mathf.Clamp(currentHunger + amount, 0f, maxHunger);
        if (currentHunger > 20f) _rumble20Shown = false;
        if (currentHunger > 0f) _rumble0Shown = false;
        FlushNeedsSave();
        OnHungerChanged?.Invoke(currentHunger, maxHunger);
    }

    /// <summary>
    /// Adds or restores stamina (e.g., energy drink or resting).
    /// </summary>
    public void AddStamina(float amount)
    {
        currentStamina = Mathf.Clamp(currentStamina + amount, 0f, maxStamina);
        if (_isExhausted && currentStamina >= exhaustionThreshold)
        {
            _isExhausted = false;
            OnExhaustionRecovered?.Invoke();
        }
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    /// <summary>
    /// Activates or refreshes a timed Stamina Recovery buff (e.g., 5% for 2 mins or 10% for 5 mins)
    /// and optionally restores an immediate stamina boost so players feel instant relief.
    /// </summary>
    public void ApplyStaminaBuff(float boostPercent, float durationSeconds, float instantRestore = 25f)
    {
        if (instantRestore > 0f)
        {
            AddStamina(instantRestore);
        }
        _staminaBuffPercent = Mathf.Max(_staminaBuffPercent, boostPercent);
        _staminaBuffDuration = Mathf.Max(1f, durationSeconds);
        _staminaBuffRemaining = Mathf.Max(_staminaBuffRemaining, durationSeconds);
    }

    /// <summary>
    /// Activates or refreshes a timed Speed Boost buff (e.g., 5% for 2 mins or 10% for 5 mins).
    /// </summary>
    public void ApplySpeedBuff(float boostPercent, float durationSeconds)
    {
        _speedBuffPercent = Mathf.Max(_speedBuffPercent, boostPercent);
        _speedBuffDuration = Mathf.Max(1f, durationSeconds);
        _speedBuffRemaining = Mathf.Max(_speedBuffRemaining, durationSeconds);
    }
}
