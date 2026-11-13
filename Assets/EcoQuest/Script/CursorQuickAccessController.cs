using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;

/// <summary>
/// Allows the player to hold CTRL to temporarily unlock the mouse cursor
/// and pause camera rotation to click on UI elements.
/// Releasing CTRL re-locks the mouse and restores camera look.
/// Integrates seamlessly with IntroSequenceController cursor state ownership.
/// </summary>
public class CursorQuickAccessController : MonoBehaviour
{
    public static CursorQuickAccessController Instance { get; private set; }

    [Header("Hold Settings")]
    [Tooltip("Required hold duration in seconds to complete the tutorial step (approx 1.0s)")]
    [SerializeField] private float requiredHoldDuration = 1.0f;

    private StarterAssetsInputs playerInputs;
    private bool isHoldingCtrl = false;
    private float currentHoldTime = 0f;
    private bool hasCompletedHoldStep = false;

    public bool IsHoldingCtrl => isHoldingCtrl;
    public float CurrentHoldTime => currentHoldTime;
    public bool HasCompletedHoldStep => hasCompletedHoldStep;

    public event System.Action OnCtrlHoldCompleted;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        playerInputs = GetComponent<StarterAssetsInputs>();
        if (playerInputs == null) playerInputs = FindFirstObjectByType<StarterAssetsInputs>();
    }

    private static bool IsCtrlKeyPressed()
    {
        bool newInputCtrl = Keyboard.current != null
            && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
        return newInputCtrl || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    }

    private void Update()
    {
        if (playerInputs == null) playerInputs = FindFirstObjectByType<StarterAssetsInputs>();

        // Map tutorial still measures CTRL hold completion while full map / map step keeps cursor unlocked
        if (MinimapController.IsMapOpenOrTutorialCursorUnlocked)
        {
            bool ctrl = IsCtrlKeyPressed();
            if (ctrl)
            {
                currentHoldTime += Time.unscaledDeltaTime;
                if (currentHoldTime >= requiredHoldDuration && !hasCompletedHoldStep)
                {
                    hasCompletedHoldStep = true;
                    OnCtrlHoldCompleted?.Invoke();
                }
            }
            else
            {
                currentHoldTime = 0f;
            }

            if (isHoldingCtrl)
            {
                isHoldingCtrl = false;
                SetCursorUnlocked(false);
            }
            return;
        }

        // Do not interfere when pause, settings, quiz, dialogue, inventory, shop, tutorial, or modal owns cursor
        if (CursorState.IsAnyBlockingUIOpen())
        {
            if (isHoldingCtrl)
            {
                isHoldingCtrl = false;
                SetCursorUnlocked(false);
            }
            currentHoldTime = 0f;
            return;
        }

        bool ctrlPressed = IsCtrlKeyPressed();

        if (ctrlPressed)
        {
            if (!isHoldingCtrl)
            {
                isHoldingCtrl = true;
                SetCursorUnlocked(true);
            }

            currentHoldTime += Time.unscaledDeltaTime;
            if (currentHoldTime >= requiredHoldDuration && !hasCompletedHoldStep)
            {
                hasCompletedHoldStep = true;
                OnCtrlHoldCompleted?.Invoke();
            }
        }
        else
        {
            if (isHoldingCtrl)
            {
                isHoldingCtrl = false;
                SetCursorUnlocked(false);
            }
            currentHoldTime = 0f;
        }
    }

    private void SetCursorUnlocked(bool unlocked)
    {
        CursorState.SetCtrlUnlock(unlocked, playerInputs);
    }

    private void OnDisable()
    {
        if (isHoldingCtrl)
        {
            isHoldingCtrl = false;
            SetCursorUnlocked(false);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ResetHoldStep()
    {
        currentHoldTime = 0f;
        hasCompletedHoldStep = false;
    }
}
