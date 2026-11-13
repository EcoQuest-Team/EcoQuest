using System;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    [Header("Tutorial Drain Balance")]
    [Tooltip("Drain multiplier applied to hunger decay and sprint stamina drain during the Forest tutorial (0.2 = 5x slower).")]
    [Range(0.05f, 1f)]
    public float tutorialDrainMultiplier = 0.2f;

    [Header("Hunger Warnings & Starving Lock")]
    [Tooltip("First hunger warning threshold percentage (0-100).")]
    public float hungerWarningThreshold10 = 10f;
    [Tooltip("Urgent second hunger warning threshold percentage (0-100).")]
    public float hungerWarningThreshold5 = 5f;
    [Tooltip("Starving threshold percentage (0-100) at or below which sprinting is locked.")]
    public float hungerStarvingThreshold = 0f;
    [Tooltip("Hysteresis margin (%) above a threshold before its warning re-arms.")]
    [Range(0f, 5f)]
    public float hungerWarningHysteresis = 1.0f;

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
    public static event Action OnActiveBuffsChanged;

    private bool _warn10Shown;
    private bool _warn5Shown;
    private bool _warn0Shown;
    private int _pendingStartupWarning = -1;
    private float _lastHungerSaveUnscaledTime = -999f;
    private float _lastBuffSaveUnscaledTime = -999f;

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
        OnActiveBuffsChanged = null;
    }

    public static void ClearSavedNeedsState()
    {
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerHungerRemaining);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerHungerRumble20Shown);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerHungerRumble0Shown);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerHungerWarn10Shown);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerHungerWarn5Shown);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerHungerWarn0Shown);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerSpeedBuffRemaining);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerSpeedBuffDuration);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerSpeedBuffPercent);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerStaminaBuffRemaining);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerStaminaBuffDuration);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.PlayerStaminaBuffPercent);
        if (Instance != null)
        {
            Instance._warn10Shown = false;
            Instance._warn5Shown = false;
            Instance._warn0Shown = false;
            Instance._pendingStartupWarning = -1;
            Instance.currentHunger = Instance.maxHunger;
            Instance._speedBuffRemaining = 0f;
            Instance._speedBuffDuration = 0f;
            Instance._speedBuffPercent = 0f;
            Instance._staminaBuffRemaining = 0f;
            Instance._staminaBuffDuration = 0f;
            Instance._staminaBuffPercent = 0f;
            Instance.OnHungerChanged?.Invoke(Instance.currentHunger, Instance.maxHunger);
            OnActiveBuffsChanged?.Invoke();
        }
    }

    public float HungerPercent => maxHunger > 0f ? (currentHunger / maxHunger) * 100f : 0f;
    public bool IsStarving => HungerPercent <= Mathf.Max(0f, hungerStarvingThreshold);
    public bool CanSprint => !_isExhausted && currentStamina > 0.5f && !IsStarving;
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

        _warn10Shown = PlayerPrefs.GetInt(EcoQuestSaveKeys.PlayerHungerWarn10Shown, 0) == 1;
        _warn5Shown = PlayerPrefs.GetInt(EcoQuestSaveKeys.PlayerHungerWarn5Shown, 0) == 1;
        _warn0Shown = PlayerPrefs.GetInt(EcoQuestSaveKeys.PlayerHungerWarn0Shown, 0) == 1;

        InitializeStartupWarningState();
        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);

        float savedSpeedRem = PlayerPrefs.GetFloat(EcoQuestSaveKeys.PlayerSpeedBuffRemaining, 0f);
        if (savedSpeedRem > 0.01f)
        {
            _speedBuffRemaining = savedSpeedRem;
            _speedBuffDuration = Mathf.Max(savedSpeedRem, PlayerPrefs.GetFloat(EcoQuestSaveKeys.PlayerSpeedBuffDuration, savedSpeedRem));
            _speedBuffPercent = Mathf.Max(0f, PlayerPrefs.GetFloat(EcoQuestSaveKeys.PlayerSpeedBuffPercent, 5f));
        }

        float savedStaminaRem = PlayerPrefs.GetFloat(EcoQuestSaveKeys.PlayerStaminaBuffRemaining, 0f);
        if (savedStaminaRem > 0.01f)
        {
            _staminaBuffRemaining = savedStaminaRem;
            _staminaBuffDuration = Mathf.Max(savedStaminaRem, PlayerPrefs.GetFloat(EcoQuestSaveKeys.PlayerStaminaBuffDuration, savedStaminaRem));
            _staminaBuffPercent = Mathf.Max(0f, PlayerPrefs.GetFloat(EcoQuestSaveKeys.PlayerStaminaBuffPercent, 5f));
        }
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

        if (_pendingStartupWarning >= 0 && OnHungerRumbleWarning != null)
        {
            int? warn = ConsumePendingStartupWarning();
            if (warn.HasValue)
            {
                OnHungerRumbleWarning.Invoke(warn.Value);
            }
        }
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
        if (!Application.isPlaying || PauseMenuController.IsPaused || Time.timeScale <= 0.001f || LoadingScreenController.IsLoading)
        {
            return;
        }

        if (StepActiveBuffs(dt, out bool expiredNow))
        {
            if (expiredNow)
            {
                FlushNeedsSave();
                OnActiveBuffsChanged?.Invoke();
            }
            else if (Time.unscaledTime - _lastBuffSaveUnscaledTime >= 1.0f)
            {
                FlushNeedsSave();
            }
        }
    }

    public bool StepActiveBuffs(float dt, out bool anyExpired)
    {
        anyExpired = false;
        if (dt <= 0f) return false;
        bool ticked = false;

        if (_staminaBuffRemaining > 0f)
        {
            ticked = true;
            _staminaBuffRemaining = Mathf.Max(0f, _staminaBuffRemaining - dt);
            if (_staminaBuffRemaining <= 0f)
            {
                _staminaBuffPercent = 0f;
                _staminaBuffDuration = 0f;
                anyExpired = true;
            }
        }

        if (_speedBuffRemaining > 0f)
        {
            ticked = true;
            _speedBuffRemaining = Mathf.Max(0f, _speedBuffRemaining - dt);
            if (_speedBuffRemaining <= 0f)
            {
                _speedBuffPercent = 0f;
                _speedBuffDuration = 0f;
                anyExpired = true;
            }
        }

        return ticked;
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
    /// Returns true only while actively in the Forest Scene AND before the tutorial completes.
    /// Guarantees normal 1.0x drain in City, Beach, Main Menu, and post-tutorial free roam.
    /// </summary>
    public bool IsTutorialDrainSlowdownActive()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !string.Equals(activeScene.name, "Forest Scene", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (PlayerPrefs.GetInt(EcoQuestSaveKeys.TabTipDone, 0) == 1 ||
            PlayerPrefs.GetInt(EcoQuestSaveKeys.SequentialWASD, 0) == 1 ||
            PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 0) == 1)
        {
            return false;
        }

        if (SequentialWASDTutorial.Instance != null && SequentialWASDTutorial.Instance.IsFullTutorialCompleted)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Effective drain multiplier for hunger and sprint stamina drain (0.2x during Forest tutorial, 1.0x otherwise).
    /// </summary>
    public float GetEffectiveTutorialDrainMultiplier()
    {
        return IsTutorialDrainSlowdownActive() ? Mathf.Clamp(tutorialDrainMultiplier, 0f, 1f) : 1f;
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

        rate *= GetEffectiveTutorialDrainMultiplier();
        if (rate <= 0f) return false;

        float prev = currentHunger;
        currentHunger = Mathf.Max(0f, currentHunger - rate * dt);
        if (Mathf.Approximately(prev, currentHunger)) return false;

        EvaluateRumbleWarnings(prev, currentHunger);
        OnHungerChanged?.Invoke(currentHunger, maxHunger);
        return true;
    }

    public int? ConsumePendingStartupWarning()
    {
        if (_pendingStartupWarning < 0) return null;
        int val = _pendingStartupWarning;
        _pendingStartupWarning = -1;
        return val;
    }

    private float ToHungerPercent(float rawHunger) => maxHunger > 0f ? (rawHunger / maxHunger) * 100f : 0f;

    private void InitializeStartupWarningState()
    {
        float pct = HungerPercent;
        _pendingStartupWarning = -1;

        if (pct <= Mathf.Max(0f, hungerStarvingThreshold))
        {
            if (!_warn0Shown)
            {
                _pendingStartupWarning = 0;
            }
            _warn10Shown = true;
            _warn5Shown = true;
            _warn0Shown = true;
        }
        else if (pct <= hungerWarningThreshold5)
        {
            if (!_warn5Shown)
            {
                _pendingStartupWarning = 5;
            }
            _warn10Shown = true;
            _warn5Shown = true;
        }
        else if (pct <= hungerWarningThreshold10)
        {
            if (!_warn10Shown)
            {
                _pendingStartupWarning = 10;
            }
            _warn10Shown = true;
        }
    }

    private void RearmWarningsForPercent(float nextPct)
    {
        float hyst = Mathf.Max(0f, hungerWarningHysteresis);
        if (nextPct > hungerWarningThreshold10 + hyst) _warn10Shown = false;
        if (nextPct > hungerWarningThreshold5 + hyst) _warn5Shown = false;
        if (nextPct > hungerStarvingThreshold + hyst) _warn0Shown = false;
    }

    private void SaveWarningPrefs()
    {
        if (!Application.isPlaying) return;
        PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerWarn10Shown, _warn10Shown ? 1 : 0);
        PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerWarn5Shown, _warn5Shown ? 1 : 0);
        PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerWarn0Shown, _warn0Shown ? 1 : 0);
    }

    private void EvaluateRumbleWarnings(float prevHunger, float nextHunger)
    {
        float prevPct = ToHungerPercent(prevHunger);
        float nextPct = ToHungerPercent(nextHunger);

        if (nextPct > prevPct)
        {
            RearmWarningsForPercent(nextPct);
            return;
        }

        if (Mathf.Approximately(prevPct, nextPct)) return;

        int mostSevere = -1;
        float starvePct = Mathf.Max(0f, hungerStarvingThreshold);

        if (nextPct <= starvePct)
        {
            if (!_warn0Shown && prevPct > starvePct)
            {
                mostSevere = 0;
            }
            _warn10Shown = true;
            _warn5Shown = true;
            _warn0Shown = true;
        }
        else if (nextPct <= hungerWarningThreshold5)
        {
            if (!_warn5Shown && prevPct > hungerWarningThreshold5)
            {
                mostSevere = 5;
            }
            _warn10Shown = true;
            _warn5Shown = true;
        }
        else if (nextPct <= hungerWarningThreshold10)
        {
            if (!_warn10Shown && prevPct > hungerWarningThreshold10)
            {
                mostSevere = 10;
            }
            _warn10Shown = true;
        }

        if (mostSevere >= 0)
        {
            if (IsStarving && _isSprinting)
            {
                _isSprinting = false;
            }
            SaveWarningPrefs();
            OnHungerRumbleWarning?.Invoke(mostSevere);
        }
    }

    public void FlushNeedsSave()
    {
        if (!Application.isPlaying) return;
        _lastHungerSaveUnscaledTime = Time.unscaledTime;
        _lastBuffSaveUnscaledTime = Time.unscaledTime;
        PlayerPrefs.SetFloat(EcoQuestSaveKeys.PlayerHungerRemaining, Mathf.Clamp(currentHunger, 0f, maxHunger));
        PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerWarn10Shown, _warn10Shown ? 1 : 0);
        PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerWarn5Shown, _warn5Shown ? 1 : 0);
        PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerWarn0Shown, _warn0Shown ? 1 : 0);
        PlayerPrefs.SetFloat(EcoQuestSaveKeys.PlayerSpeedBuffRemaining, Mathf.Max(0f, _speedBuffRemaining));
        PlayerPrefs.SetFloat(EcoQuestSaveKeys.PlayerSpeedBuffDuration, Mathf.Max(0f, _speedBuffDuration));
        PlayerPrefs.SetFloat(EcoQuestSaveKeys.PlayerSpeedBuffPercent, Mathf.Max(0f, _speedBuffPercent));
        PlayerPrefs.SetFloat(EcoQuestSaveKeys.PlayerStaminaBuffRemaining, Mathf.Max(0f, _staminaBuffRemaining));
        PlayerPrefs.SetFloat(EcoQuestSaveKeys.PlayerStaminaBuffDuration, Mathf.Max(0f, _staminaBuffDuration));
        PlayerPrefs.SetFloat(EcoQuestSaveKeys.PlayerStaminaBuffPercent, Mathf.Max(0f, _staminaBuffPercent));
    }

    public void SetHungerImmediate(float targetHunger, bool persistNow = true)
    {
        float prev = currentHunger;
        currentHunger = Mathf.Clamp(targetHunger, 0f, maxHunger);
        _pendingStartupWarning = -1;
        RearmWarningsForPercent(HungerPercent);
        EvaluateRumbleWarnings(prev, currentHunger);
        if (persistNow)
        {
            PlayerPrefs.SetFloat(EcoQuestSaveKeys.PlayerHungerRemaining, currentHunger);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerWarn10Shown, _warn10Shown ? 1 : 0);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerWarn5Shown, _warn5Shown ? 1 : 0);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerWarn0Shown, _warn0Shown ? 1 : 0);
            PlayerPrefs.Save();
        }
        OnHungerChanged?.Invoke(currentHunger, maxHunger);
    }

    private void UpdateStamina(float dt)
    {
        StepStaminaUpdate(dt);
    }

    /// <summary>
    /// Advances stamina drain (while sprinting) or stamina regeneration (while resting) by <paramref name="dt"/> seconds.
    /// </summary>
    public void StepStaminaUpdate(float dt)
    {
        if (dt <= 0f) return;

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

        if (_isSprinting && !CanSprint)
        {
            _isSprinting = false;
        }

        if (_isSprinting)
        {
            _timeSinceLastSprint = 0f;
            if (currentStamina > 0f)
            {
                float tutorialDrainMult = GetEffectiveTutorialDrainMultiplier();
                currentStamina = Mathf.Max(0f, currentStamina - (staminaDrainPerSec * drainMultiplier * tutorialDrainMult) * dt);
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
        RearmWarningsForPercent(HungerPercent);
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
    /// Activates a timed Stamina Recovery buff only if the Stamina/Energy buff group is not already active.
    /// Returns true if applied, false if blocked because an active buff is still running.
    /// </summary>
    public bool ApplyStaminaBuff(float boostPercent, float durationSeconds, float instantRestore = 25f)
    {
        if (HasActiveStaminaBuff) return false;
        if (instantRestore > 0f)
        {
            AddStamina(instantRestore);
        }
        _staminaBuffPercent = Mathf.Max(0f, boostPercent);
        _staminaBuffDuration = Mathf.Max(1f, durationSeconds);
        _staminaBuffRemaining = Mathf.Max(1f, durationSeconds);
        FlushNeedsSave();
        OnActiveBuffsChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Activates a timed Speed Boost buff only if the Speed Boost group is not already active.
    /// Returns true if applied, false if blocked because an active buff is still running.
    /// </summary>
    public bool ApplySpeedBuff(float boostPercent, float durationSeconds)
    {
        if (HasActiveSpeedBuff) return false;
        _speedBuffPercent = Mathf.Max(0f, boostPercent);
        _speedBuffDuration = Mathf.Max(1f, durationSeconds);
        _speedBuffRemaining = Mathf.Max(1f, durationSeconds);
        FlushNeedsSave();
        OnActiveBuffsChanged?.Invoke();
        return true;
    }

    public void ResetNeedsToFull()
    {
        _isSprinting = false;
        _isExhausted = false;
        _timeSinceLastSprint = 0f;
        _warn10Shown = false;
        _warn5Shown = false;
        _warn0Shown = false;
        _pendingStartupWarning = -1;
        currentHunger = maxHunger;
        currentStamina = maxStamina;

        _speedBuffRemaining = 0f;
        _speedBuffDuration = 0f;
        _speedBuffPercent = 0f;
        _staminaBuffRemaining = 0f;
        _staminaBuffDuration = 0f;
        _staminaBuffPercent = 0f;

        OnHungerChanged?.Invoke(currentHunger, maxHunger);
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
        OnActiveBuffsChanged?.Invoke();
    }

    public void ClearAllBuffsForTest()
    {
        _speedBuffRemaining = 0f;
        _speedBuffDuration = 0f;
        _speedBuffPercent = 0f;
        _staminaBuffRemaining = 0f;
        _staminaBuffDuration = 0f;
        _staminaBuffPercent = 0f;
        FlushNeedsSave();
        OnActiveBuffsChanged?.Invoke();
    }
}
