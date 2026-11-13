using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Manages the in-game Pause Menu in Forest Scene opened with Esc or the HUD settings gear button.
/// Pauses with Time.timeScale = 0, unlocks cursor, locks player controller, smoothly blurs
/// the 3D scene via UIBackgroundBlur, and restores exact previous cursor/control states on resume.
/// Blocks opening during dialogue, Pre-Test, Post-Test, lesson cards, or intro WASD tutorial.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    public static PauseMenuController Instance { get; private set; }
    public static bool IsPaused { get; private set; }

    [Header("Pause UI References")]
    public GameObject pauseOverlayRoot;
    public CanvasGroup pauseCanvasGroup;
    public RectTransform pauseCardRect;
    public Button resumeButton;
    public Button settingsButton;
    public Button returnToMainMenuButton;

    [Header("Settings Panel Reference (Shared Prefab Instance)")]
    public GameObject settingsPanelObject;
    public SettingsPanelUI settingsPanelUI;

    [Header("HUD Settings Gear Button")]
    public Button hudSettingsButton;
    public CanvasGroup hudSettingsButtonCanvasGroup;

    [Header("Return to Main Menu Confirmation Popup")]
    public GameObject confirmMainMenuOverlay;
    public Button confirmMainMenuYesButton;
    public Button confirmMainMenuNoButton;
    public string mainMenuSceneName = "Main Menu";

    // Snapshot of pre-pause state
    private float savedTimeScale = 1f;
    private CursorLockMode savedCursorLockMode = CursorLockMode.Locked;
    private bool savedCursorVisible = false;
    private bool savedFpcEnabled = true;
    private bool savedInputsCursorLocked = true;
    private bool savedInputsCursorLook = true;

    private StarterAssets.FirstPersonController fpc;
    private StarterAssets.StarterAssetsInputs playerInputs;
    private Coroutine animRoutine;
    private bool isResuming = false;

    private static int _escapeConsumedFrame = -1;
    private static float _pauseLockoutUntil = 0f;

    /// <summary>
    /// Marks Escape as handled by an active modal/dialogue/shop so PauseMenuController
    /// never opens on the same frame or behind an active popup.
    /// </summary>
    public static void NotifyEscapeConsumed()
    {
        _escapeConsumedFrame = Time.frameCount;
        _pauseLockoutUntil = Time.unscaledTime + 0.15f;
    }

    private void Awake()
    {
        Instance = this;
        IsPaused = false;
        isResuming = false;

        if (resumeButton != null)
            resumeButton.onClick.AddListener(ResumeGame);
        if (settingsButton != null)
            settingsButton.onClick.AddListener(OpenSettingsFromPause);
        if (returnToMainMenuButton != null)
            returnToMainMenuButton.onClick.AddListener(OnReturnToMainMenuClicked);

        if (confirmMainMenuYesButton != null)
            confirmMainMenuYesButton.onClick.AddListener(OnConfirmMainMenuYes);
        if (confirmMainMenuNoButton != null)
            confirmMainMenuNoButton.onClick.AddListener(OnConfirmMainMenuNo);

        if (pauseOverlayRoot != null) pauseOverlayRoot.SetActive(false);
        if (confirmMainMenuOverlay != null) confirmMainMenuOverlay.SetActive(false);
        if (settingsPanelObject != null) settingsPanelObject.SetActive(false);

        ResolveSettingsPanelUI();
        if (settingsPanelUI != null)
        {
            settingsPanelUI.OnPanelClosed += OnSettingsPanelClosed;
        }

        ResolveHudButton();
        if (hudSettingsButton != null)
        {
            hudSettingsButton.onClick.AddListener(OnSettingsIconButtonClicked);
        }
    }

    private void Start()
    {
        ResolveSettingsPanelUI();
        ResolveHudButton();
    }

    private void OnDestroy()
    {
        if (settingsPanelUI != null)
        {
            settingsPanelUI.OnPanelClosed -= OnSettingsPanelClosed;
        }

        if (Instance == this)
        {
            if (IsPaused)
            {
                Time.timeScale = 1f;
                IsPaused = false;
                UIBackgroundBlur.Cleanup();
            }
            Instance = null;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            HandleEscapePressed();
        }

        UpdateHudSettingsButtonVisibility();
    }

    private void ResolveSettingsPanelUI()
    {
        if (settingsPanelUI == null && settingsPanelObject != null)
        {
            settingsPanelUI = settingsPanelObject.GetComponent<SettingsPanelUI>();
        }
        if (settingsPanelUI == null)
        {
            settingsPanelUI = FindFirstObjectByType<SettingsPanelUI>(FindObjectsInactive.Include);
        }
    }

    private void ResolveHudButton()
    {
        if (hudSettingsButton == null)
        {
            var btnGo = GameObject.Find("SettingsIconButton");
            if (btnGo != null) hudSettingsButton = btnGo.GetComponent<Button>();
        }
        if (hudSettingsButtonCanvasGroup == null && hudSettingsButton != null)
        {
            hudSettingsButtonCanvasGroup = hudSettingsButton.GetComponentInParent<CanvasGroup>();
        }
    }

    private bool wasGearHiddenByMenu;

    public void UpdateHudSettingsButtonVisibility()
    {
        if (hudSettingsButtonCanvasGroup == null) return;

        if (HudVisibilityService.Instance != null && !HudVisibilityService.Instance.IsElementOwned(hudSettingsButtonCanvasGroup))
        {
            HudVisibilityService.Instance.RegisterOwnedElement(hudSettingsButtonCanvasGroup);
        }

        // Hide HUD gear button cleanly while Settings or Pause is open (UIOrchestrator agrees via IsAnySettingsOpen / IsPaused)
        bool settingsOpen = (settingsPanelObject != null && settingsPanelObject.activeInHierarchy) || SettingsPanelUI.IsAnySettingsOpen;
        if (settingsOpen || IsPaused)
        {
            wasGearHiddenByMenu = true;
            hudSettingsButtonCanvasGroup.alpha = 0f;
            hudSettingsButtonCanvasGroup.interactable = false;
            hudSettingsButtonCanvasGroup.blocksRaycasts = false;
            return;
        }

        if (wasGearHiddenByMenu)
        {
            wasGearHiddenByMenu = false;
            if (UIOrchestrator.Instance != null)
            {
                UIOrchestrator.Instance.ApplyCurrentProfile(0f);
                return;
            }
        }

        // If HUD is hidden by HudVisibilityService, UIOrchestrator, or active dialogue/cinematic, let authority control fade
        bool isCinematicOrDialogue = (HudVisibilityService.Instance != null && HudVisibilityService.Instance.IsHUDHidden) ||
                                     (UIOrchestrator.Instance != null && (UIOrchestrator.Instance.AreModalsOpen || UIOrchestrator.Instance.IsOverlayActive)) ||
                                     (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue) ||
                                     LitterbugQuestManager.IsDialogueOpen;

        if (isCinematicOrDialogue)
        {
            return;
        }

        // UIOrchestrator is the single HUD authority: do not stomp over profile transitions every frame
        if (UIOrchestrator.Instance != null)
        {
            return;
        }

        // In gameplay & paused (fallback if UIOrchestrator absent): gear button is visible and active
        hudSettingsButtonCanvasGroup.alpha = 1f;
        hudSettingsButtonCanvasGroup.interactable = true;
        hudSettingsButtonCanvasGroup.blocksRaycasts = true;
    }

    public void HandleEscapePressed()
    {
        if (Time.frameCount == _escapeConsumedFrame || Time.unscaledTime < _pauseLockoutUntil)
            return;

        // 0. If Full Map is open, Esc closes the map first
        if (MinimapController.Instance != null && MinimapController.Instance.IsFullMapOpen)
        {
            MinimapController.Instance.CloseFullMap();
            NotifyEscapeConsumed();
            return;
        }

        // 1. If Settings Panel is open, close its confirm popup or the Settings Panel
        if (settingsPanelObject != null && settingsPanelObject.activeSelf)
        {
            if (settingsPanelUI != null && settingsPanelUI.IsConfirmPopupOpen)
            {
                settingsPanelUI.CloseConfirmPopup();
                NotifyEscapeConsumed();
                return;
            }
            if (settingsPanelUI != null)
            {
                settingsPanelUI.ClosePanel();
            }
            else
            {
                settingsPanelObject.SetActive(false);
            }
            NotifyEscapeConsumed();
            return;
        }

        // 2. If Return to Main Menu confirm popup is open, close it
        if (confirmMainMenuOverlay != null && confirmMainMenuOverlay.activeSelf)
        {
            confirmMainMenuOverlay.SetActive(false);
            NotifyEscapeConsumed();
            return;
        }

        // 3. Enforce LIFO dismissal for Eco Shop, Inventory, and non-blocking Info/Warning/Reward modals
        if (EcoQuest.Inventory.UI_EcoShop.Instance != null && EcoQuest.Inventory.UI_EcoShop.Instance.IsOpen)
        {
            EcoQuest.Inventory.UI_EcoShop.Instance.CloseShop();
            NotifyEscapeConsumed();
            return;
        }

        if (EcoQuest.Inventory.InventoryController.Instance != null && EcoQuest.Inventory.InventoryController.Instance.IsOpen)
        {
            EcoQuest.Inventory.InventoryController.Instance.CloseInventory(true);
            NotifyEscapeConsumed();
            return;
        }

        if (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalOpen)
        {
            var active = ModalManager.Instance.ActiveModal;
            if (active != null &&
                (active.type == ModalManager.ModalType.InfoCard ||
                 active.type == ModalManager.ModalType.Warning ||
                 active.type == ModalManager.ModalType.Reward))
            {
                ModalManager.Instance.CloseModal(active.modalObject);
            }
            NotifyEscapeConsumed();
            return;
        }

        // 4. Toggle Pause / Resume
        if (IsPaused)
        {
            ResumeGame();
        }
        else if (CanOpenPauseMenu())
        {
            PauseGame();
        }
    }

    /// <summary>
    /// Clicking the HUD gear button toggles the pause menu (opens if in gameplay, closes if already open).
    /// </summary>
    public void OnSettingsIconButtonClicked()
    {
        if (SceneTransition.Instance != null && SceneTransition.Instance.IsTransitioning)
            return;

        if (MinimapController.Instance != null && MinimapController.Instance.IsFullMapOpen)
        {
            MinimapController.Instance.CloseFullMap();
        }

        if (IsPaused)
        {
            if (settingsPanelObject != null && settingsPanelObject.activeSelf)
            {
                if (settingsPanelUI != null) settingsPanelUI.ClosePanel();
                else settingsPanelObject.SetActive(false);
                return;
            }
            ResumeGame();
        }
        else if (EcoQuest.Inventory.InventoryController.Instance != null && EcoQuest.Inventory.InventoryController.Instance.IsOpen) { EcoQuest.Inventory.InventoryController.Instance.CloseInventory(true); }
        else if (CanOpenPauseMenu())
        {
            PauseGame();
        }
    }

    /// <summary>
    /// Returns true only when in free-roam gameplay (not in dialogue, Pre-Test, Post-Test,
    /// lesson cards, or initial WASD onboarding tutorial).
    /// </summary>
    public bool CanOpenPauseMenu()
    {
        if (SceneTransition.Instance != null && SceneTransition.Instance.IsTransitioning)
            return false;

        if (MinimapController.Instance != null && MinimapController.Instance.IsFullMapOpen)
            return false;

        if (EcoQuest.Inventory.InventoryController.Instance != null && (EcoQuest.Inventory.InventoryController.Instance.IsOpen || EcoQuest.Inventory.InventoryController.Instance.IsInCloseLockout))
            return false;

        // Block during Pre-Test, Post-Test, or Results Screen
        if (QuizUIController.IsAnyQuizOpen)
            return false;

        // Block during Dialogue or BinIntro lesson cards via IntroSequenceController
        if (IntroSequenceController.Instance != null)
        {
            var state = IntroSequenceController.Instance.CurrentState;
            if (state == IntroSequenceController.IntroState.Dialogue ||
                state == IntroSequenceController.IntroState.BinIntro)
            {
                return false;
            }
        }

        // Block if any NPC is actively in dialogue
        var npcs = FindObjectsByType<NPCInteraction>(FindObjectsSortMode.None);
        for (int i = 0; i < npcs.Length; i++)
        {
            if (npcs[i] != null && npcs[i].IsTalking)
                return false;
        }

        // Block if EcoShop, active Modals, Dialogues, or City overlays are open
        if (EcoQuest.Inventory.UI_EcoShop.Instance != null && EcoQuest.Inventory.UI_EcoShop.Instance.IsOpen)
            return false;

        if (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalOpen)
            return false;

        if (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue)
            return false;

        if (LitterbugQuestManager.IsDialogueOpen)
            return false;

        if (ExplorerGateController.Instance != null && ExplorerGateController.Instance.IsInDialogue)
            return false;

        if (EcoQuest.CityQuest.CityTrashDetectiveManager.Instance != null &&
            (EcoQuest.CityQuest.CityTrashDetectiveManager.Instance.IsOverlayOrModalOpen ||
             EcoQuest.CityQuest.CityTrashDetectiveManager.Instance.DialogueState != EcoQuest.CityQuest.RobinDialogueState.Idle))
            return false;

        if (UIOrchestrator.Instance != null && (UIOrchestrator.Instance.AreModalsOpen || UIOrchestrator.Instance.IsOverlayActive))
            return false;

        // Block if Lesson / Tutorial Infographic overlay is open
        var tm = FindFirstObjectByType<TutorialManager>();
        if (tm != null && tm.tutorialOverlay != null && tm.tutorialOverlay.activeInHierarchy)
            return false;

        return true;
    }

    public void PauseGame()
    {
        if (IsPaused || isResuming) return;
        ResolvePlayerReferences();

        // Capture exact pre-pause state
        savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        savedCursorLockMode = Cursor.lockState;
        savedCursorVisible = Cursor.visible;
        savedFpcEnabled = fpc != null ? fpc.enabled : true;
        savedInputsCursorLocked = playerInputs != null ? playerInputs.cursorLocked : true;
        savedInputsCursorLook = playerInputs != null ? playerInputs.cursorInputForLook : true;

        IsPaused = true;
        Time.timeScale = 0f;
        AudioManager.Instance?.PlayModalOpen();

        if (fpc != null) fpc.enabled = false;
        if (playerInputs != null)
        {
            playerInputs.move = Vector2.zero;
            playerInputs.look = Vector2.zero;
            playerInputs.jump = false;
            playerInputs.sprint = false;
        }

        CursorState.SetLocked(false, playerInputs);

        if (confirmMainMenuOverlay != null) confirmMainMenuOverlay.SetActive(false);
        if (pauseOverlayRoot != null) pauseOverlayRoot.SetActive(true);

        if (animRoutine != null) StopCoroutine(animRoutine);
        animRoutine = StartCoroutine(AnimatePauseOpenRoutine());
    }

    public void ResumeGame()
    {
        if (!IsPaused || isResuming) return;
        AudioManager.Instance?.PlayModalClose();

        if (settingsPanelObject != null && settingsPanelObject.activeSelf)
        {
            if (settingsPanelUI != null) settingsPanelUI.ClosePanel();
            else settingsPanelObject.SetActive(false);
        }
        if (confirmMainMenuOverlay != null && confirmMainMenuOverlay.activeSelf)
        {
            confirmMainMenuOverlay.SetActive(false);
        }

        if (animRoutine != null) StopCoroutine(animRoutine);
        animRoutine = StartCoroutine(AnimatePauseCloseRoutine());
    }

    private IEnumerator AnimatePauseOpenRoutine()
    {
        if (pauseCanvasGroup != null)
        {
            pauseCanvasGroup.alpha = 0f;
            pauseCanvasGroup.interactable = false; // Block clicks during opening transition
            pauseCanvasGroup.blocksRaycasts = true;
        }

        if (pauseCardRect != null)
        {
            pauseCardRect.gameObject.SetActive(true);
            var cardCg = pauseCardRect.GetComponent<CanvasGroup>();
            if (cardCg != null)
            {
                cardCg.alpha = 1f;
                cardCg.interactable = true;
                cardCg.blocksRaycasts = true;
            }
            pauseCardRect.localScale = new Vector3(0.95f, 0.95f, 1f);
        }

        float dur = 0.25f;
        float elapsed = 0f;
        float startBlur = UIBackgroundBlur.GetWeight();

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float ease = 1f - Mathf.Pow(1f - t, 3f);

            if (pauseCanvasGroup != null) pauseCanvasGroup.alpha = ease;
            if (pauseCardRect != null) pauseCardRect.localScale = Vector3.Lerp(new Vector3(0.95f, 0.95f, 1f), Vector3.one, ease);
            UIBackgroundBlur.SetWeight(Mathf.Lerp(startBlur, 1f, ease));

            yield return null;
        }

        if (pauseCanvasGroup != null)
        {
            pauseCanvasGroup.alpha = 1f;
            pauseCanvasGroup.interactable = true; // Re-enable clicks after opening finishes
            pauseCanvasGroup.blocksRaycasts = true;
        }
        if (pauseCardRect != null) pauseCardRect.localScale = Vector3.one;
        UIBackgroundBlur.SetWeight(1f);

        animRoutine = null;
    }

    private IEnumerator AnimatePauseCloseRoutine()
    {
        isResuming = true;

        if (pauseCanvasGroup != null)
        {
            pauseCanvasGroup.interactable = false; // Block clicks during closing transition
        }

        float dur = 0.18f;
        float elapsed = 0f;
        Vector3 startScale = pauseCardRect != null ? pauseCardRect.localScale : Vector3.one;
        float startAlpha = pauseCanvasGroup != null ? pauseCanvasGroup.alpha : 1f;
        float startBlur = UIBackgroundBlur.GetWeight();

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float ease = t * t;

            if (pauseCanvasGroup != null) pauseCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, ease);
            if (pauseCardRect != null) pauseCardRect.localScale = Vector3.Lerp(startScale, new Vector3(0.95f, 0.95f, 1f), ease);
            UIBackgroundBlur.SetWeight(Mathf.Lerp(startBlur, 0f, ease));
            SettingsPanelUI.SetAllHudAlpha(ease);

            yield return null;
        }

        if (pauseCanvasGroup != null)
        {
            pauseCanvasGroup.alpha = 0f;
            pauseCanvasGroup.blocksRaycasts = false;
        }

        UIBackgroundBlur.Cleanup();
        SettingsPanelUI.SetAllHudAlpha(1f);

        if (pauseOverlayRoot != null)
            pauseOverlayRoot.SetActive(false);

        FinalizeResume();
        isResuming = false;
        animRoutine = null;
    }

    private void FinalizeResume()
    {
        ResolvePlayerReferences();
        IsPaused = false;
        Time.timeScale = savedTimeScale;
        SettingsPanelUI.SetAllHudAlpha(1f);
        if (UIOrchestrator.Instance != null)
        {
            UIOrchestrator.Instance.ApplyCurrentProfile(0f);
        }
        UpdateHudSettingsButtonVisibility();

        if (fpc != null) fpc.enabled = savedFpcEnabled;

        bool holdingCtrl = (UnityEngine.InputSystem.Keyboard.current != null &&
            (UnityEngine.InputSystem.Keyboard.current.leftCtrlKey.isPressed || UnityEngine.InputSystem.Keyboard.current.rightCtrlKey.isPressed))
            || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        CursorState.SetLocked(!holdingCtrl && (savedCursorLockMode == CursorLockMode.Locked || savedFpcEnabled), playerInputs);
    }

    public void OpenSettingsFromPause()
    {
        if (animRoutine != null) StopCoroutine(animRoutine);
        animRoutine = StartCoroutine(TransitionToSettingsRoutine());
    }

    private IEnumerator TransitionToSettingsRoutine()
    {
        if (pauseCanvasGroup != null)
        {
            pauseCanvasGroup.interactable = false;
        }

        CanvasGroup cardCg = null;
        if (pauseCardRect != null)
        {
            cardCg = pauseCardRect.GetComponent<CanvasGroup>();
            if (cardCg == null) cardCg = pauseCardRect.gameObject.AddComponent<CanvasGroup>();
            cardCg.interactable = false;
        }

        float dur = 0.15f;
        float elapsed = 0f;
        Vector3 startScale = pauseCardRect != null ? pauseCardRect.localScale : Vector3.one;
        float startAlpha = cardCg != null ? cardCg.alpha : 1f;

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float ease = t * t;

            if (cardCg != null) cardCg.alpha = Mathf.Lerp(startAlpha, 0f, ease);
            if (pauseCardRect != null) pauseCardRect.localScale = Vector3.Lerp(startScale, new Vector3(0.95f, 0.95f, 1f), ease);
            yield return null;
        }

        if (cardCg != null)
        {
            cardCg.alpha = 0f;
            cardCg.blocksRaycasts = false;
        }
        if (pauseCardRect != null)
        {
            pauseCardRect.gameObject.SetActive(false);
        }

        if (settingsPanelUI != null)
        {
            settingsPanelUI.OpenPanel();
        }
        else if (settingsPanelObject != null)
        {
            settingsPanelObject.SetActive(true);
        }

        animRoutine = null;
    }

    private void OnSettingsPanelClosed()
    {
        if (IsPaused)
        {
            if (animRoutine != null) StopCoroutine(animRoutine);
            animRoutine = StartCoroutine(TransitionFromSettingsRoutine());
        }
    }

    private IEnumerator TransitionFromSettingsRoutine()
    {
        if (pauseCardRect != null)
        {
            pauseCardRect.gameObject.SetActive(true);
            var cardCg = pauseCardRect.GetComponent<CanvasGroup>();
            if (cardCg == null) cardCg = pauseCardRect.gameObject.AddComponent<CanvasGroup>();

            cardCg.alpha = 0f;
            cardCg.interactable = false;
            cardCg.blocksRaycasts = true;
            pauseCardRect.localScale = new Vector3(0.95f, 0.95f, 1f);

            float dur = 0.25f;
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / dur);
                float ease = 1f - Mathf.Pow(1f - t, 3f);

                cardCg.alpha = ease;
                pauseCardRect.localScale = Vector3.Lerp(new Vector3(0.95f, 0.95f, 1f), Vector3.one, ease);
                yield return null;
            }

            cardCg.alpha = 1f;
            cardCg.interactable = true;
            pauseCardRect.localScale = Vector3.one;
        }

        if (pauseCanvasGroup != null)
        {
            pauseCanvasGroup.alpha = 1f;
            pauseCanvasGroup.interactable = true;
            pauseCanvasGroup.blocksRaycasts = true;
        }

        UIBackgroundBlur.SetWeight(1f);
        animRoutine = null;
    }

    public void OnReturnToMainMenuClicked()
    {
        if (confirmMainMenuOverlay != null)
        {
            confirmMainMenuOverlay.SetActive(true);
        }
    }

    public void OnConfirmMainMenuYes()
    {
        if (confirmMainMenuOverlay != null) confirmMainMenuOverlay.SetActive(false);
        if (pauseOverlayRoot != null) pauseOverlayRoot.SetActive(false);

        IsPaused = false;
        Time.timeScale = 1f;
        UIBackgroundBlur.Cleanup();

        CursorState.SetLocked(false, playerInputs, updateCenterCursor: false);

        LoadingScreen.Load(mainMenuSceneName);
    }

    public void OnConfirmMainMenuNo()
    {
        if (confirmMainMenuOverlay != null)
        {
            confirmMainMenuOverlay.SetActive(false);
        }
    }

    private void ResolvePlayerReferences()
    {
        if (fpc == null) fpc = FindFirstObjectByType<StarterAssets.FirstPersonController>();
        if (playerInputs == null) playerInputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
    }
}
