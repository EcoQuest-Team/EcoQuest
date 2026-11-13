using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Single central authority for cursor lock/visibility and camera look gating.
/// Guarantees camera rotation stops immediately on CTRL unlock or focus loss,
/// suppresses OS cursor re-center spikes on re-lock, and prevents UI panels
/// and gameplay scripts from fighting over Cursor.lockState.
/// </summary>
public static class CursorState
{
    public const int RelockSuppressionFrames = 2;

    private static bool isCtrlUnlockActive;
    private static bool hasApplicationFocus = true;
    private static int relockAllowFrame = -1;
    private static CursorLockMode lastObservedLockMode = CursorLockMode.None;
    private static bool sceneHookRegistered;

    private static StarterAssetsInputs cachedInputs;
    private static PlayerInput cachedPlayerInput;
    private static InputAction cachedLookAction;

    public static bool IsCtrlUnlockActive => isCtrlUnlockActive;
    public static bool HasApplicationFocus => hasApplicationFocus;
    public static bool IsRelockSuppressed => Time.frameCount < relockAllowFrame;

    /// <summary>
    /// True only when cursor is locked, application is focused, CTRL quick-unlock
    /// is inactive, no blocking UI is open, and any re-lock suppression window has elapsed.
    /// </summary>
    public static bool LookEnabled =>
        Cursor.lockState == CursorLockMode.Locked
        && hasApplicationFocus
        && !isCtrlUnlockActive
        && !IsRelockSuppressed
        && !IsAnyBlockingUIOpen();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnSubsystemRegistration()
    {
        ResetForSceneOrTest();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneLifecycleHook()
    {
        if (sceneHookRegistered) return;
        sceneHookRegistered = true;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private static void OnSceneUnloaded(Scene _)
    {
        ResetForSceneOrTest();
    }

    public static void ResetForSceneOrTest()
    {
        isCtrlUnlockActive = false;
        hasApplicationFocus = true;
        relockAllowFrame = -1;
        lastObservedLockMode = Cursor.lockState;
        ClearCachedInputRefs();
    }

    public static bool IsAnyBlockingUIOpen()
    {
        if (PauseMenuController.IsPaused) return true;
        if (SettingsPanelUI.IsAnySettingsOpen) return true;
        if (QuizUIController.IsAnyQuizOpen) return true;
        if (MinimapController.IsMapOpenOrTutorialCursorUnlocked) return true;
        if (SequentialWASDTutorial.IsTutorialCursorUnlocked) return true;
        if (TutorialManager.IsAnyTutorialActive) return true;
        if (LitterbugQuestManager.IsDialogueOpen) return true;
        if (NPCInteraction.IsAnyGuardianFlowRunning) return true;

        var intro = IntroSequenceController.Instance;
        if (intro != null && intro.CurrentState != IntroSequenceController.IntroState.Gameplay) return true;

        var dialogue = DialoguePresenter.Instance;
        if (dialogue != null && dialogue.IsInDialogue) return true;

        var explorer = ExplorerGateController.Instance;
        if (explorer != null && explorer.IsInDialogue) return true;

        var inv = EcoQuest.Inventory.InventoryController.Instance;
        if (inv != null && inv.IsOpen) return true;

        var shop = EcoQuest.Inventory.UI_EcoShop.Instance;
        if (shop != null && shop.IsOpen) return true;

        var modal = ModalManager.Instance;
        if (modal != null && modal.IsAnyModalActuallyVisible()) return true;

        var orch = UIOrchestrator.Instance;
        if (orch != null && (orch.AreModalsOpen || orch.IsOverlayActive)) return true;

        if (EcoQuest.CityQuest.CityTrashDetectiveManager.IsRobinCooldownDialogueOpen) return true;
        var detective = EcoQuest.CityQuest.CityTrashDetectiveManager.Instance;
        if (detective != null && (detective.DialogueState != EcoQuest.CityQuest.RobinDialogueState.Idle || detective.IsOverlayOrModalOpen))
            return true;

        return false;
    }

    /// <summary>
    /// Central entry point for locking/unlocking the gameplay cursor.
    /// Requests to lock are automatically overridden to unlocked whenever
    /// focus is lost, CTRL quick-unlock is active, or any blocking UI is open.
    /// </summary>
    public static void SetLocked(bool locked, StarterAssetsInputs inputs = null, bool updateCenterCursor = true)
    {
        inputs = ResolveInputs(inputs);

        CursorLockMode previousLockMode = Cursor.lockState;
        bool shouldLock = locked && hasApplicationFocus && !isCtrlUnlockActive && !IsAnyBlockingUIOpen();

        Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !shouldLock;

        if (shouldLock && (previousLockMode != CursorLockMode.Locked || lastObservedLockMode != CursorLockMode.Locked))
        {
            ArmRelockSuppression();
        }

        lastObservedLockMode = Cursor.lockState;

        if (inputs != null)
        {
            inputs.cursorLocked = shouldLock;
            inputs.cursorInputForLook = shouldLock;
            inputs.look = Vector2.zero;
        }

        SyncLookAction(inputs);

        if (updateCenterCursor && CenterScreenCursor.Instance != null)
        {
            CenterScreenCursor.Instance.SetVisible(shouldLock);
        }
    }

    public static void SetCtrlUnlock(bool active, StarterAssetsInputs inputs = null)
    {
        inputs = ResolveInputs(inputs);

        if (active)
        {
            if (IsAnyBlockingUIOpen())
            {
                isCtrlUnlockActive = false;
                SetLocked(false, inputs, updateCenterCursor: false);
                return;
            }

            isCtrlUnlockActive = true;
            SetLocked(false, inputs);
            return;
        }

        bool wasActive = isCtrlUnlockActive;
        isCtrlUnlockActive = false;

        if (IsAnyBlockingUIOpen() || !hasApplicationFocus)
        {
            SetLocked(false, inputs, updateCenterCursor: false);
            return;
        }

        if (wasActive)
        {
            ArmRelockSuppression();
        }

        SetLocked(true, inputs);
    }

    public static void NotifyApplicationFocus(bool hasFocus, StarterAssetsInputs inputs = null)
    {
        inputs = ResolveInputs(inputs);
        bool wasFocused = hasApplicationFocus;
        hasApplicationFocus = hasFocus;

        if (!hasFocus)
        {
            isCtrlUnlockActive = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            lastObservedLockMode = Cursor.lockState;
            if (inputs != null)
            {
                inputs.look = Vector2.zero;
                inputs.cursorLocked = false;
                inputs.cursorInputForLook = false;
            }
            SyncLookAction(inputs);
            return;
        }

        if (!IsAnyBlockingUIOpen() && !isCtrlUnlockActive)
        {
            if (!wasFocused)
            {
                ArmRelockSuppression();
            }
            SetLocked(true, inputs);
        }
        else
        {
            SetLocked(false, inputs, updateCenterCursor: false);
        }
    }

    /// <summary>
    /// Observes any external lock transitions, syncs the Input System Look action,
    /// clears stale look input & rotation velocity when look is disabled, and returns LookEnabled.
    /// </summary>
    public static bool SyncAndCheckLook(StarterAssetsInputs inputs, ref float rotationVelocity)
    {
        inputs = ResolveInputs(inputs);
        ObserveExternalLockTransition();

        if (!LookEnabled)
        {
            ResetLookInputAndVelocity(inputs, ref rotationVelocity);
            SyncLookAction(inputs);
            return false;
        }

        SyncLookAction(inputs);
        return true;
    }

    public static void ResetLookInputAndVelocity(StarterAssetsInputs inputs, ref float rotationVelocity)
    {
        rotationVelocity = 0f;
        if (inputs != null)
        {
            inputs.look = Vector2.zero;
        }
    }

    public static void SyncLookAction(StarterAssetsInputs inputs = null)
    {
        inputs = ResolveInputs(inputs);
        InputAction lookAction = ResolveLookAction(inputs);
        if (lookAction == null) return;

        bool allowLook = LookEnabled;
        if (!allowLook)
        {
            if (inputs != null)
            {
                inputs.look = Vector2.zero;
            }

            if (lookAction.enabled)
            {
                lookAction.Disable();
            }
        }
        else if (!lookAction.enabled)
        {
            if (inputs != null)
            {
                inputs.look = Vector2.zero;
            }

            lookAction.Enable();
        }
    }

    private static void ObserveExternalLockTransition()
    {
        CursorLockMode current = Cursor.lockState;
        if (current == CursorLockMode.Locked && lastObservedLockMode != CursorLockMode.Locked)
        {
            ArmRelockSuppression();
        }
        lastObservedLockMode = current;
    }

    private static void ArmRelockSuppression()
    {
        relockAllowFrame = Mathf.Max(relockAllowFrame, Time.frameCount + RelockSuppressionFrames);
    }

    private static StarterAssetsInputs ResolveInputs(StarterAssetsInputs inputs)
    {
        if (inputs != null)
        {
            CacheInputRefs(inputs);
            return inputs;
        }

        if (cachedInputs == null)
        {
            cachedInputs = Object.FindFirstObjectByType<StarterAssetsInputs>();
            CacheInputRefs(cachedInputs);
        }

        return cachedInputs;
    }

    private static void CacheInputRefs(StarterAssetsInputs inputs)
    {
        if (inputs == null)
        {
            ClearCachedInputRefs();
            return;
        }

        if (cachedInputs != inputs)
        {
            cachedInputs = inputs;
            cachedPlayerInput = inputs.GetComponent<PlayerInput>();
            cachedLookAction = null;
        }
        else if (cachedPlayerInput == null)
        {
            cachedPlayerInput = inputs.GetComponent<PlayerInput>();
        }
    }

    private static InputAction ResolveLookAction(StarterAssetsInputs inputs)
    {
        if (inputs == null) return null;
        CacheInputRefs(inputs);

        if (cachedPlayerInput == null || cachedPlayerInput.actions == null)
        {
            cachedLookAction = null;
            return null;
        }

        if (cachedLookAction == null || !IsActionInAsset(cachedLookAction, cachedPlayerInput.actions))
        {
            cachedLookAction = cachedPlayerInput.actions.FindAction("Player/Look", false)
                ?? cachedPlayerInput.actions.FindAction("Look", false);
        }

        return cachedLookAction;
    }

    private static bool IsActionInAsset(InputAction action, InputActionAsset asset)
    {
        return action != null && asset != null && action.actionMap != null && action.actionMap.asset == asset;
    }

    private static void ClearCachedInputRefs()
    {
        cachedInputs = null;
        cachedPlayerInput = null;
        cachedLookAction = null;
    }
}
