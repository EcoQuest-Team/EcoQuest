using UnityEngine;
using StarterAssets;

/// <summary>
/// Controls sprint speed (Inspector multiplier x1.6) and smooth FOV bump (+5 FOV)
/// for the first-person player. Tracks sprint time for the tutorial step.
/// </summary>
public class PlayerSprintController : MonoBehaviour
{
    public static PlayerSprintController Instance { get; private set; }

    [Header("Sprint Settings")]
    [Tooltip("Sprint speed multiplier over base MoveSpeed (default 1.6x)")]
    [SerializeField] private float sprintSpeedMultiplier = 1.6f;

    [Tooltip("Field of View bump added while sprinting (+5)")]
    [SerializeField] private float fovBump = 5.0f;

    [Tooltip("Speed of FOV transition")]
    [SerializeField] private float fovTransitionSpeed = 8.0f;

    [Header("Tutorial Tracking")]
    [Tooltip("Required sprint duration to complete tutorial step (approx 1.5s)")]
    [SerializeField] private float requiredSprintDuration = 1.5f;

    private FirstPersonController fpc;
    private StarterAssetsInputs inputs;
    private Camera mainCamera;
    private float baseFOV = 40f;
    private float currentSprintTime = 0f;
    private bool hasCompletedSprintStep = false;

    public bool IsSprinting { get; private set; }
    public float CurrentSprintTime => currentSprintTime;
    public bool HasCompletedSprintStep => hasCompletedSprintStep;
    public float SprintSpeedMultiplier => sprintSpeedMultiplier;
    public float FOVBump => fovBump;

    public event System.Action OnSprintStepCompleted;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        fpc = GetComponent<FirstPersonController>();
        inputs = GetComponent<StarterAssetsInputs>();
    }

    private void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera != null)
        {
            baseFOV = mainCamera.fieldOfView;
        }

        ApplySprintSpeed();
    }

    private void Update()
    {
        if (fpc == null) fpc = GetComponent<FirstPersonController>();
        if (inputs == null) inputs = GetComponent<StarterAssetsInputs>();
        if (mainCamera == null) mainCamera = Camera.main;

        ApplySprintSpeed();

        // Check if player is actively holding sprint while moving
        bool isHoldingSprint = (inputs != null && inputs.sprint) ||
                               Input.GetKey(KeyCode.LeftShift) ||
                               Input.GetKey(KeyCode.RightShift);

        bool isMoving = (inputs != null && inputs.move.sqrMagnitude > 0.01f);
        bool isGrounded = (fpc == null || fpc.Grounded);

        IsSprinting = isHoldingSprint && isMoving && isGrounded;

        // Smooth FOV Bump
        if (mainCamera != null)
        {
            float targetFOV = IsSprinting ? (baseFOV + fovBump) : baseFOV;
            mainCamera.fieldOfView = Mathf.Lerp(mainCamera.fieldOfView, targetFOV, Time.deltaTime * fovTransitionSpeed);
        }

        // Track sprint duration
        if (IsSprinting)
        {
            currentSprintTime += Time.deltaTime;
            if (currentSprintTime >= requiredSprintDuration && !hasCompletedSprintStep)
            {
                hasCompletedSprintStep = true;
                OnSprintStepCompleted?.Invoke();
            }
        }
    }

    public void ApplySprintSpeed()
    {
        if (fpc == null) fpc = GetComponent<FirstPersonController>();
        if (fpc != null)
        {
            fpc.SprintSpeed = fpc.MoveSpeed * sprintSpeedMultiplier;
        }
    }

    /// <summary>
    /// Resets sprint tracking timer for tutorial re-testing.
    /// </summary>
    public void ResetSprintTimer()
    {
        currentSprintTime = 0f;
        hasCompletedSprintStep = false;
    }
}
