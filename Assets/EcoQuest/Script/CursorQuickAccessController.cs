using UnityEngine;
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

    private void Update()
    {
        if (playerInputs == null) playerInputs = FindFirstObjectByType<StarterAssetsInputs>();

        // Do not interfere if a modal, pause menu, quiz, full map, dialogue, or info card is actively managing cursor
        if (PauseMenuController.IsPaused) return;
        if (QuizUIController.IsAnyQuizOpen) return;
        if (MinimapController.IsMapOpenOrTutorialCursorUnlocked)
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
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
            isHoldingCtrl = false;
            return;
        }
        if (IntroSequenceController.Instance != null)
        {
            var state = IntroSequenceController.Instance.CurrentState;
            if (state == IntroSequenceController.IntroState.Dialogue || state == IntroSequenceController.IntroState.BinIntro)
            {
                if (isHoldingCtrl)
                {
                    isHoldingCtrl = false;
                    currentHoldTime = 0f;
                }
                return;
            }
        }

        bool ctrlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

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
        if (IntroSequenceController.Instance != null)
        {
            IntroSequenceController.Instance.SetCursorState(unlocked);
        }
        else
        {
            Cursor.lockState = unlocked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = unlocked;

            if (playerInputs != null)
            {
                playerInputs.cursorLocked = !unlocked;
                playerInputs.cursorInputForLook = !unlocked;
            }

            if (CenterScreenCursor.Instance != null)
            {
                CenterScreenCursor.Instance.SetVisible(!unlocked);
            }
        }
    }

    private void OnDisable()
    {
        if (isHoldingCtrl)
        {
            isHoldingCtrl = false;
            SetCursorUnlocked(false);
        }
    }

    public void ResetHoldStep()
    {
        currentHoldTime = 0f;
        hasCompletedHoldStep = false;
    }
}
