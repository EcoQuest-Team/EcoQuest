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
    [Tooltip("Passive hunger drain rate per second.")]
    public float hungerDepletionPerSec = 0.05f;
    [Tooltip("Extra hunger drain rate per second while actively sprinting.")]
    public float sprintHungerDepletionPerSec = 0.08f;

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

        currentHunger = Mathf.Clamp(currentHunger, 0f, maxHunger);
        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
    }

    private void OnDestroy()
    {
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

    private void UpdateHunger(float dt)
    {
        if (currentHunger > 0f)
        {
            float rate = hungerDepletionPerSec;
            if (_isSprinting)
            {
                rate += sprintHungerDepletionPerSec;
            }

            float prev = currentHunger;
            currentHunger = Mathf.Max(0f, currentHunger - rate * dt);
            if (!Mathf.Approximately(prev, currentHunger))
            {
                OnHungerChanged?.Invoke(currentHunger, maxHunger);
            }
        }
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
