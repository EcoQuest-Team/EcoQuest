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
        UpdateHunger(Time.deltaTime);
        UpdateStamina(Time.deltaTime);
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
        if (_isSprinting)
        {
            _timeSinceLastSprint = 0f;
            if (currentStamina > 0f)
            {
                currentStamina = Mathf.Max(0f, currentStamina - staminaDrainPerSec * dt);
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
            if (_timeSinceLastSprint >= regenDelay && currentStamina < maxStamina)
            {
                currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenPerSec * dt);
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
}
