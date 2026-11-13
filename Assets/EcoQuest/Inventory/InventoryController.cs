using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using StarterAssets;

namespace EcoQuest.Inventory
{
    /// <summary>
    /// Central manager for the Inventory system.
    /// Handles player input (Key B, Escape), 0.2s close lockout, tutorial gate,
    /// cursor & gameplay input locking/restoration across all close/interrupt paths,
    /// full-scene blur integration, ModalManager & UIOrchestrator profile, and test item provisioning.
    /// </summary>
    public class InventoryController : MonoBehaviour
    {
        private static InventoryController instance;
        public static InventoryController Instance => instance;

        private static bool s_provisionedThisPlaySession = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticSessionState()
        {
            s_provisionedThisPlaySession = false;
        }

        [Header("Database & Service")]
        [SerializeField] private InventoryItemDatabase itemDatabase;
        [SerializeField] private int slotCapacity = 24;

        [Header("View References")]
        [SerializeField] private UI_Inventory inventoryUI;
        [SerializeField] private UI_BagButton bagButton;

        [Header("Behavior & Rules")]
        [Tooltip("Keep Coins and Hunger/Stamina bars visible while the inventory modal is open.")]
        [SerializeField] private bool keepVitalsVisible = true;

        [Tooltip("Block using a food/stamina item when that stat is already at 100%.")]
        [SerializeField] private bool blockUseWhenFull = true;

        [Tooltip("Give 5 Apples and 3 Energy Drinks once at Play start when inventory is empty (Editor/Dev builds only).")]
        [SerializeField] private bool giveTestItemsOnStart = true;

        private InventoryService service;
        private bool isOpen = false;
        private float closeLockoutUntil = -1f;
        private Coroutine blurRoutine;

        // Saved pre-open state for clean restoration on any close/interrupt path
        private bool hasSavedPlayerState = false;
        private CursorLockMode savedCursorLock = CursorLockMode.Locked;
        private bool savedCursorVisible = false;
        private FirstPersonController cachedFpc;
        private bool savedFpcEnabled = true;
        private StarterAssetsInputs cachedInputs;
        private bool savedCursorLockedInput = true;
        private bool savedCursorInputForLook = true;

        // Vitals Canvas sorting order restoration
        private Canvas cachedStatsCanvas;
        private int savedStatsSortOrder = 20;
        private Canvas cachedCoinsCanvas;
        private int savedCoinsSortOrder = 10;
        private bool hasElevatedVitalsSorting = false;

        public InventoryService Service => service;
        public InventoryItemDatabase Database => itemDatabase;
        public UI_Inventory InventoryUI => inventoryUI;
        public bool IsOpen => isOpen;
        public bool IsInCloseLockout => Time.unscaledTime < closeLockoutUntil;
        public bool KeepVitalsVisible { get => keepVitalsVisible; set => keepVitalsVisible = value; }
        public bool BlockUseWhenFull { get => blockUseWhenFull; set => blockUseWhenFull = value; }
        public bool GiveTestItemsOnStart { get => giveTestItemsOnStart; set => giveTestItemsOnStart = value; }

        public static bool IsTutorialGateOverrideForTest { get; set; } = false;
        public static bool TutorialGateOverrideCompletedValue { get; set; } = true;

        public static bool IsTutorialCompleted
        {
            get
            {
                if (IsTutorialGateOverrideForTest) return TutorialGateOverrideCompletedValue;
                if (UIOrchestrator.IsCityScene()) return true;

                // If SequentialWASDTutorial is actively running in the scene and not completed
                if (SequentialWASDTutorial.Instance != null && !SequentialWASDTutorial.IsTutorialCompleted)
                {
                    return false;
                }

                // If TutorialManager overlay is actively displayed
                var tutMgr = FindFirstObjectByType<TutorialManager>();
                if (tutMgr != null &&
                    tutMgr.tutorialOverlay != null &&
                    tutMgr.tutorialOverlay.activeInHierarchy)
                {
                    return false;
                }

                // If UIOrchestrator explicitly reports we are in the Tutorial phase
                if (UIOrchestrator.Instance != null &&
                    (UIOrchestrator.Instance.CurrentPhase == UIOrchestrator.GamePhase.Tutorial ||
                     UIOrchestrator.Instance.CurrentFlowStep == UIOrchestrator.FlowStep.Tutorial))
                {
                    return false;
                }

                bool wasd = PlayerPrefs.GetInt("SequentialWASD_TutorialCompleted", 0) == 1;
                bool tabTip = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0) == 1 || PlayerPrefs.GetInt(EcoQuestSaveKeys.TabTipDone, 0) == 1;
                return wasd || tabTip;
            }
        }

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            else if (instance != this)
            {
                Destroy(gameObject);
                return;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName == "Main Menu" || sceneName.Contains("MainMenu"))
            {
                enabled = false;
                if (inventoryUI != null) inventoryUI.gameObject.SetActive(false);
                if (bagButton != null) bagButton.gameObject.SetActive(false);
                return;
            }

            LoadDatabaseIfNeeded();
            service = new InventoryService(itemDatabase, slotCapacity);

            if (inventoryUI == null)
            {
                inventoryUI = GetComponent<UI_Inventory>();
                if (inventoryUI == null) inventoryUI = GetComponentInChildren<UI_Inventory>(true);
            }
            if (bagButton == null)
            {
                bagButton = FindFirstObjectByType<UI_BagButton>(FindObjectsInactive.Include);
            }

            if (inventoryUI != null)
            {
                inventoryUI.Initialize(this, service);
            }
            if (bagButton != null)
            {
                bagButton.Initialize(this, service);
            }
        }

        private void Start()
        {
            if (inventoryUI != null && !isOpen)
            {
                inventoryUI.HideImmediate();
            }

            TryProvisionInitialTestItems();
        }

        private void LoadDatabaseIfNeeded()
        {
            if (itemDatabase == null)
            {
                itemDatabase = Resources.Load<InventoryItemDatabase>("Inventory/InventoryItemDatabase");
            }
#if UNITY_EDITOR
            if (itemDatabase == null)
            {
                itemDatabase = UnityEditor.AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>("Assets/EcoQuest/Inventory/Data/InventoryItemDatabase.asset");
            }
#endif
        }

        public void TryProvisionInitialTestItems()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!giveTestItemsOnStart || s_provisionedThisPlaySession || service == null) return;
            s_provisionedThisPlaySession = true;

            if (service.GetTotalItemCount() == 0)
            {
                service.AddItem("food_apple", 5);
                service.AddItem("energy_drink", 3);
                service.MarkAllSeen();
            }
#endif
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (isOpen)
            {
                ForceCloseImmediate();
            }
            else if (hasSavedPlayerState)
            {
                RestorePlayerAndCursorState();
            }
        }

        private void OnDestroy()
        {
            if (isOpen || hasSavedPlayerState)
            {
                ForceCloseImmediate();
            }
            if (instance == this) instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (isOpen)
            {
                ForceCloseImmediate();
            }

            if (scene.name == "Main Menu" || scene.name.Contains("MainMenu"))
            {
                enabled = false;
                if (inventoryUI != null) inventoryUI.gameObject.SetActive(false);
                if (bagButton != null) bagButton.gameObject.SetActive(false);
                return;
            }

            if (!UIOrchestrator.IsCityScene())
            {
                if (inventoryUI != null) inventoryUI.HideImmediate();
                return;
            }

            service?.Load();
        }

        private void Update()
        {
            // Inventory & Hotkey B are disabled outside City Scene (Forest Scene / World 1)
            if (!UIOrchestrator.IsCityScene())
            {
                if (isOpen) CloseInventory(true);
                return;
            }

            if (isOpen)
            {
                // Maintain cursor unlocked and movement suppressed while open
                if (Cursor.lockState == CursorLockMode.Locked || !Cursor.visible)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                if (cachedInputs != null)
                {
                    cachedInputs.move = Vector2.zero;
                    cachedInputs.look = Vector2.zero;
                    cachedInputs.jump = false;
                    cachedInputs.sprint = false;
                }

                // Close on Esc or B
                bool escPressed = IsEscapePressed();
                if (escPressed || IsBPressed())
                {
                    if (escPressed) PauseMenuController.NotifyEscapeConsumed();
                    CloseInventory(true);
                }
                return;
            }

            // Respect 0.2s close lockout
            if (IsInCloseLockout) return;

            if (IsBPressed())
            {
                OpenInventory();
            }
        }

        public static bool IsBPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.bKey.wasPressedThisFrame)
                return true;
#endif
            return Input.GetKeyDown(KeyCode.B);
        }

        public static bool IsEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                return true;
#endif
            return Input.GetKeyDown(KeyCode.Escape);
        }

        public void ToggleInventory()
        {
            if (isOpen)
            {
                CloseInventory(true);
            }
            else if (!IsInCloseLockout)
            {
                OpenInventory();
            }
        }

        public bool CanOpenInventory(out string blockedReason)
        {
            blockedReason = string.Empty;

            if (IsInCloseLockout)
            {
                return false;
            }

            // 0. World check: Inventory is available in City Scene only (World 2)
            if (!UIOrchestrator.IsCityScene())
            {
                blockedReason = "Inventory is unavailable in the Forest.";
                return false;
            }

            // 1. Tutorial progression lock
            if (!IsTutorialCompleted)
            {
                blockedReason = "Finish the tutorial first!";
                return false;
            }

            // 2. Active Scene Transition or Loading Screen
            if (SceneTransition.Instance != null && SceneTransition.Instance.IsTransitioning)
            {
                blockedReason = "Cannot open while travelling.";
                return false;
            }

            // 3. Active Dialogue Presenter
            if (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue)
            {
                blockedReason = "Cannot open during dialogue.";
                return false;
            }

            // 4. Litterbug or City Dialogue
            if (LitterbugQuestManager.IsDialogueOpen)
            {
                blockedReason = "Cannot open during dialogue.";
                return false;
            }

            // 5. Active Quiz / Modal Window (other than Inventory itself)
            if (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalOpen &&
                (ModalManager.Instance.ActiveModal == null || ModalManager.Instance.ActiveModal.type != ModalManager.ModalType.Inventory))
            {
                blockedReason = "Cannot open while another window is open.";
                return false;
            }

            // 6. UIOrchestrator Overlay Active (Explorer travel, cinematic)
            if (UIOrchestrator.Instance != null)
            {
                if (UIOrchestrator.Instance.AreModalsOpen && !isOpen)
                {
                    blockedReason = "Cannot open while another window is open.";
                    return false;
                }
                if (UIOrchestrator.Instance.IsOverlayActive &&
                    !System.Linq.Enumerable.Contains(UIOrchestrator.Instance.ActiveOverlayIds, "Inventory", System.StringComparer.OrdinalIgnoreCase))
                {
                    blockedReason = "Cannot open during cutscene or travel.";
                    return false;
                }
            }

            // 7. Pause Menu
            if (PauseMenuController.IsPaused)
            {
                blockedReason = "Cannot open while paused.";
                return false;
            }

            return true;
        }

        public void OpenInventory()
        {
            if (isOpen || IsInCloseLockout) return;

            if (!CanOpenInventory(out string blockedReason))
            {
                ShowFeedbackToast(blockedReason);
                return;
            }

            isOpen = true;
            service?.MarkAllSeen();

            // 1. Snapshot and lock gameplay input + unlock cursor
            SaveAndDisableGameplayInput();

            // 2. Register with ModalManager (single modal at a time)
            if (ModalManager.Instance != null && inventoryUI != null)
            {
                ModalManager.Instance.RequestModal(
                    ModalManager.ModalType.Inventory,
                    inventoryUI.gameObject,
                    null,
                    OnModalExternallyInterrupted,
                    priority: 10);
            }

            // 3. Acquire FullScene blur (0 -> 1 over 0.25s)
            StartBlurTransition(true, 0.25f);

            // 4. Push Inventory overlay to UIOrchestrator & keep vitals visible if configured
            if (UIOrchestrator.Instance != null)
            {
                UIOrchestrator.Instance.PushOverlay("Inventory");
            }
            ApplyVitalsVisibilityAndSorting(true);

            // 5. Show Inventory Modal UI
            if (inventoryUI != null)
            {
                inventoryUI.Show();
            }
        }

        public void CloseInventory(bool restoreOverlay = true)
        {
            if (!isOpen) return;
            isOpen = false;
            closeLockoutUntil = Time.unscaledTime + 0.20f;

            // 1. Hide Inventory Modal UI
            if (inventoryUI != null)
            {
                inventoryUI.Hide();
            }

            // 2. Animate FullScene blur (1 -> 0 over 0.20s) and release
            StartBlurTransition(false, 0.20f);

            // 3. Notify ModalManager
            if (ModalManager.Instance != null && ModalManager.Instance.ActiveModal != null &&
                ModalManager.Instance.ActiveModal.type == ModalManager.ModalType.Inventory)
            {
                ModalManager.Instance.CloseModal(inventoryUI != null ? inventoryUI.gameObject : null);
            }

            // 4. Restore vitals sorting & pop UIOrchestrator overlay
            ApplyVitalsVisibilityAndSorting(false);
            if (restoreOverlay && UIOrchestrator.Instance != null)
            {
                UIOrchestrator.Instance.PopOverlay("Inventory");
                UIOrchestrator.Instance.ApplyCurrentProfile(0.20f);
            }

            // 5. Restore cursor and gameplay input
            RestorePlayerAndCursorState();

            // Always ensure world interaction prompt is unblocked unless an active dialogue is open
            if (WorldInteractionPrompt.Instance != null)
            {
                bool dialogueOpen = (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue) ||
                                    LitterbugQuestManager.IsDialogueOpen;
                if (!dialogueOpen)
                {
                    WorldInteractionPrompt.Instance.SetBlocked(false);
                }
            }
        }

        private void OnModalExternallyInterrupted()
        {
            if (isOpen)
            {
                CloseInventory(true);
            }
        }

        public void ForceCloseImmediate()
        {
            isOpen = false;
            closeLockoutUntil = Time.unscaledTime + 0.20f;

            if (blurRoutine != null)
            {
                StopCoroutine(blurRoutine);
                blurRoutine = null;
            }
            UIBackgroundBlur.ReleaseFullSceneBlur("Inventory");

            if (inventoryUI != null)
            {
                inventoryUI.HideImmediate();
            }

            ApplyVitalsVisibilityAndSorting(false);
            if (UIOrchestrator.Instance != null)
            {
                UIOrchestrator.Instance.PopOverlay("Inventory");
            }

            RestorePlayerAndCursorState();
        }

        private void SaveAndDisableGameplayInput()
        {
            if (!hasSavedPlayerState)
            {
                savedCursorLock = Cursor.lockState;
                savedCursorVisible = Cursor.visible;

                cachedFpc = FindFirstObjectByType<FirstPersonController>();
                if (cachedFpc != null)
                {
                    savedFpcEnabled = cachedFpc.enabled;
                }

                cachedInputs = FindFirstObjectByType<StarterAssetsInputs>();
                if (cachedInputs != null)
                {
                    savedCursorLockedInput = cachedInputs.cursorLocked;
                    savedCursorInputForLook = cachedInputs.cursorInputForLook;
                }

                hasSavedPlayerState = true;
            }

            if (cachedFpc != null) cachedFpc.enabled = false;
            if (cachedInputs != null)
            {
                cachedInputs.move = Vector2.zero;
                cachedInputs.look = Vector2.zero;
                cachedInputs.jump = false;
                cachedInputs.sprint = false;
                cachedInputs.cursorLocked = false;
                cachedInputs.cursorInputForLook = false;
            }

            if (WorldInteractionPrompt.Instance != null)
            {
                WorldInteractionPrompt.Instance.SetBlocked(true);
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void RestorePlayerAndCursorState()
        {
            if (!hasSavedPlayerState) return;
            hasSavedPlayerState = false;

            bool otherModalActive = (ModalManager.Instance != null && (
                (ModalManager.Instance.ActiveModal != null && ModalManager.Instance.ActiveModal.type != ModalManager.ModalType.Inventory) ||
                ModalManager.Instance.ModalStackCount > 0
            ));
            bool otherBlocker = otherModalActive ||
                                PauseMenuController.IsPaused ||
                                (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue) ||
                                LitterbugQuestManager.IsDialogueOpen;

            if (!otherBlocker)
            {
                if (cachedFpc != null) cachedFpc.enabled = savedFpcEnabled;
                if (cachedInputs != null)
                {
                    cachedInputs.move = Vector2.zero;
                    cachedInputs.look = Vector2.zero;
                    cachedInputs.cursorLocked = savedCursorLockedInput;
                    cachedInputs.cursorInputForLook = savedCursorInputForLook;
                }
                if (WorldInteractionPrompt.Instance != null)
                {
                    WorldInteractionPrompt.Instance.SetBlocked(false);
                }

                // Restore previous cursor state (or Locked in active gameplay)
                Cursor.lockState = savedCursorLock == CursorLockMode.None && Application.isPlaying ? CursorLockMode.Locked : savedCursorLock;
                Cursor.visible = savedCursorLock == CursorLockMode.None && Application.isPlaying ? false : savedCursorVisible;
            }
        }

        private void ApplyVitalsVisibilityAndSorting(bool opening)
        {
            var statsHud = FindFirstObjectByType<UI_SurvivalStatsHUD>(FindObjectsInactive.Include);
            if (statsHud != null)
            {
                if (cachedStatsCanvas == null) cachedStatsCanvas = statsHud.GetComponentInParent<Canvas>();
            }

            if (cachedCoinsCanvas == null)
            {
                var coinsGo = GameObject.Find("UI_Coins");
                if (coinsGo != null) cachedCoinsCanvas = coinsGo.GetComponentInParent<Canvas>();
            }

            if (opening && keepVitalsVisible)
            {
                if (cachedStatsCanvas != null)
                {
                    if (!hasElevatedVitalsSorting) savedStatsSortOrder = cachedStatsCanvas.sortingOrder;
                    cachedStatsCanvas.sortingOrder = 95;
                }
                if (cachedCoinsCanvas != null)
                {
                    if (!hasElevatedVitalsSorting) savedCoinsSortOrder = cachedCoinsCanvas.sortingOrder;
                    cachedCoinsCanvas.sortingOrder = 95;
                }
                hasElevatedVitalsSorting = true;
            }
            else if (!opening && hasElevatedVitalsSorting)
            {
                if (cachedStatsCanvas != null) cachedStatsCanvas.sortingOrder = savedStatsSortOrder;
                if (cachedCoinsCanvas != null) cachedCoinsCanvas.sortingOrder = savedCoinsSortOrder;
                hasElevatedVitalsSorting = false;
            }
        }

        private void StartBlurTransition(bool opening, float duration)
        {
            if (opening)
            {
                UIBackgroundBlur.AcquireFullSceneBlur("Inventory");
            }

            if (!gameObject.activeInHierarchy)
            {
                if (!opening) UIBackgroundBlur.ReleaseFullSceneBlur("Inventory");
                else UIBackgroundBlur.SetWeight(1f);
                return;
            }

            if (blurRoutine != null) StopCoroutine(blurRoutine);
            blurRoutine = StartCoroutine(BlurRoutine(opening, duration));
        }

        private IEnumerator BlurRoutine(bool opening, float duration)
        {
            float startW = UIBackgroundBlur.GetWeight();
            float endW = opening ? 1f : 0f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
                UIBackgroundBlur.SetWeight(Mathf.Lerp(startW, endW, t));
                yield return null;
            }

            UIBackgroundBlur.SetWeight(endW);
            if (!opening)
            {
                UIBackgroundBlur.ReleaseFullSceneBlur("Inventory");
            }
            blurRoutine = null;
        }

        public void ShowFeedbackToast(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            if (inventoryUI != null)
            {
                inventoryUI.ShowToast(message, 2.0f);
            }

            if (EcoQuest.CityQuest.CityTrashDetectiveManager.Instance != null &&
                EcoQuest.CityQuest.CityTrashDetectiveManager.Instance.ui != null &&
                !isOpen)
            {
                EcoQuest.CityQuest.CityTrashDetectiveManager.Instance.ui.ShowHintToast(message, 2.0f);
            }

            Debug.Log($"[Inventory] {message}");
        }
    }
}
