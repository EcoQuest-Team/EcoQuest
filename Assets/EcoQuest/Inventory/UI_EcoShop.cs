using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;
using StarterAssets;

namespace EcoQuest.Inventory
{
    /// <summary>
    /// Master UI controller for the ECO SHOP modal screen.
    /// Rebuilt to match the user's reference mockup (Image 1):
    /// - Double-border forest green panel with outer glow and soft vignette.
    /// - Decorative top banner with plaque, leaves, and tendrils (gentle idle sway).
    /// - Functional ScrollRect with RectMask2D, clamped bounds, glowing mint scrollbar handle,
    ///   and row-by-row mouse wheel scroll.
    /// - 16+ pooled cards: Available, Locked (padlock, shake on click), and CannotAfford.
    /// - Bottom-left coin counter capsule pill bound to PlayerCurrency.
    /// - Elevation of HUD vitals & active buffs so they remain visible while shopping.
    /// - Proper cursor unlocking and input freezing without camera look leakage.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UI_EcoShop : MonoBehaviour
    {
        private static UI_EcoShop instance;
        private static int instanceEpoch;
        public static UI_EcoShop Instance => SceneSingletonLookup.GetOrFind(ref instance, ref instanceEpoch);

        [Header("Backdrop & Panel")]
        [SerializeField] private CanvasGroup backdropGroup;
        [SerializeField] private Image vignetteImage;
        [SerializeField] private RectTransform panelRect;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private Button closeButton;

        [Header("Header Decoration & Plaque")]
        [SerializeField] private RectTransform bannerTransform;
        [SerializeField] private Image headerBannerImage;
        [SerializeField] private TextMeshProUGUI headerTitleText;

        [Header("Scrolling Content Well")]
        [SerializeField] private RectTransform contentWellRect;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform scrollContent;
        [SerializeField] private Scrollbar scrollbar;
        [SerializeField] private Image scrollbarHandleImage;

        [Header("Shop Cards (16+ Slots)")]
        [SerializeField] private List<UI_EcoShopSlot> slotViews = new List<UI_EcoShopSlot>();

        [Header("Bottom-Left Coin Counter Pill")]
        [SerializeField] private RectTransform coinPillRect;
        [SerializeField] private Image coinPillIcon;
        [SerializeField] private TextMeshProUGUI coinPillAmountText;

        [Header("Database & Sprites")]
        [SerializeField] private InventoryItemDatabase itemDatabase;
        [SerializeField] private Sprite padlockSprite;
        [SerializeField] private Sprite coinSprite;

        [Header("Themed Toast")]
        [SerializeField] private CanvasGroup toastGroup;
        [SerializeField] private TextMeshProUGUI toastText;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip openSound;
        [SerializeField] private AudioClip closeSound;
        [SerializeField] private AudioClip buySound;
        [SerializeField] private AudioClip errorSound;

        private bool isOpen = false;
        private Coroutine transitionRoutine;
        private Coroutine toastRoutine;
        private Coroutine blurRoutine;
        private Coroutine wheelScrollRoutine;

        // Wheel scrolling math
        private float targetScrollPos = 1f;
        private const int Columns = 4;
        private const int TotalSlots = 16;
        private const int VisibleRows = 2;

        // Saved player & HUD state
        private bool hasSavedPlayerState = false;
        private CursorLockMode savedCursorLock = CursorLockMode.Locked;
        private bool savedCursorVisible = false;
        private FirstPersonController cachedFpc;
        private bool savedFpcEnabled = true;
        private StarterAssetsInputs cachedInputs;
        private bool savedCursorLockedInput = true;
        private bool savedCursorInputForLook = true;

        private Canvas cachedStatsCanvas;
        private int savedStatsSortOrder = 20;
        private Canvas cachedCoinsCanvas;
        private int savedCoinsSortOrder = 10;
        private bool hasElevatedVitalsSorting = false;

        public bool IsOpen => isOpen;
        public IReadOnlyList<UI_EcoShopSlot> SlotViews => slotViews;

        public event Action OnShopOpened;
        public event Action OnShopClosed;

        private void Awake()
        {
            if (instance == null) instance = this;

            // Safety rule: disable in Main Menu
            if (SceneManager.GetActiveScene().name.Equals("Main Menu", StringComparison.OrdinalIgnoreCase))
            {
                gameObject.SetActive(false);
                return;
            }

            LoadDatabaseIfNeeded();

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(CloseShop);
            }

            EnsureToastSetup();
        }

        private void Start()
        {
            if (!isOpen)
            {
                HideImmediate();
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (isOpen)
            {
                isOpen = false;
                RestorePlayerAndCursorState();
                ApplyVitalsVisibilityAndSorting(false);
            }
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            if (instance == this) instance = null;
        }

        private void LoadDatabaseIfNeeded()
        {
            if (itemDatabase == null)
            {
                itemDatabase = Resources.Load<InventoryItemDatabase>("Inventory/InventoryItemDatabase");
            }
            if (itemDatabase == null)
            {
                var invCtrl = InventoryController.Instance ?? FindFirstObjectByType<InventoryController>(FindObjectsInactive.Include);
                if (invCtrl != null && invCtrl.Database != null)
                {
                    itemDatabase = invCtrl.Database;
                }
            }
#if UNITY_EDITOR
            if (itemDatabase == null)
            {
                itemDatabase = UnityEditor.AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>("Assets/EcoQuest/Inventory/Data/InventoryItemDatabase.asset");
            }
#endif
        }

        private void Update()
        {
            if (!isOpen) return;

            // Maintain cursor unlocked while shop is open
            if (Cursor.lockState == CursorLockMode.Locked || !Cursor.visible)
            {
                CursorState.SetLocked(false, cachedInputs);
            }

            // Freeze player locomotion & camera look
            if (cachedInputs != null)
            {
                cachedInputs.move = Vector2.zero;
                cachedInputs.look = Vector2.zero;
                cachedInputs.jump = false;
                cachedInputs.sprint = false;
            }

            // Close with Escape or B
            bool escPressed = IsEscapePressed();
            if (escPressed || IsBPressed())
            {
                if (escPressed) PauseMenuController.NotifyEscapeConsumed();
                CloseShop();
                return;
            }

            // Decorative leaves stay completely stationary (zero sway/drift to match target reference)
            if (bannerTransform != null && bannerTransform.localRotation != Quaternion.identity)
            {
                bannerTransform.localRotation = Quaternion.identity;
            }

            // Update live coin display
            UpdateCoinPill(false);

            // Handle mouse wheel scrolling over content well or scrollbar
            HandleMouseWheelScroll();
        }

        private void HandleMouseWheelScroll()
        {
            if (scrollRect == null) return;

            float scrollDelta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scrollDelta) < 0.01f) return;

            // Check if cursor is over shop content well or scrollbar
            Vector2 mousePos = Input.mousePosition;
            if (contentWellRect != null && !RectTransformUtility.RectangleContainsScreenPoint(contentWellRect, mousePos))
            {
                if (scrollbar != null && !RectTransformUtility.RectangleContainsScreenPoint(scrollbar.GetComponent<RectTransform>(), mousePos))
                {
                    return;
                }
            }

            int totalRows = Mathf.CeilToInt((float)slotViews.Count / Columns);
            int scrollableRows = Mathf.Max(1, totalRows - VisibleRows);
            float step = 1.0f / scrollableRows;

            // Invert delta: scroll down (negative delta) moves content up (decreases normalizedPosition)
            targetScrollPos = Mathf.Clamp01(targetScrollPos + (scrollDelta > 0 ? step : -step));

            if (wheelScrollRoutine != null) StopCoroutine(wheelScrollRoutine);
            wheelScrollRoutine = StartCoroutine(SmoothWheelScrollRoutine(targetScrollPos, 0.12f));
        }

        private IEnumerator SmoothWheelScrollRoutine(float target, float duration)
        {
            if (scrollRect == null) yield break;
            float start = scrollRect.verticalNormalizedPosition;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                scrollRect.verticalNormalizedPosition = Mathf.Lerp(start, target, t);
                yield return null;
            }
            scrollRect.verticalNormalizedPosition = target;
        }

        private static bool IsBPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.bKey.wasPressedThisFrame) return true;
#endif
            return Input.GetKeyDown(KeyCode.B);
        }

        private static bool IsEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
#endif
            return Input.GetKeyDown(KeyCode.Escape);
        }

        public void OpenShop()
        {
            if (isOpen) return;
            isOpen = true;

            gameObject.SetActive(true);
            if (backdropGroup != null) backdropGroup.gameObject.SetActive(true);
            if (panelRect != null) panelRect.gameObject.SetActive(true);
            if (coinPillRect != null) coinPillRect.gameObject.SetActive(true);

            SaveAndDisableGameplayInput();

            if (ModalManager.Instance != null)
            {
                ModalManager.Instance.RequestModal(
                    ModalManager.ModalType.Dialogue,
                    gameObject,
                    null,
                    CloseShop,
                    priority: 9);
            }

            StartBlurTransition(true, 0.25f);
            ApplyVitalsVisibilityAndSorting(true);

            if (UIOrchestrator.Instance != null)
            {
                UIOrchestrator.Instance.PushOverlay("EcoShop");
            }

            PopulateSlots();
            UpdateCoinPill(true);

            if (scrollContent != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
            }
            Canvas.ForceUpdateCanvases();

            // Reset scroll to top on open
            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = 1f;
                targetScrollPos = 1f;
            }

            if (transitionRoutine != null) StopCoroutine(transitionRoutine);
            transitionRoutine = StartCoroutine(AnimateOpenRoutine());

            if (openSound != null) PlaySound(openSound);
            else if (UITheme.Instance != null && UITheme.Instance.modalOpenSound != null) PlaySound(UITheme.Instance.modalOpenSound);
            else AudioManager.Instance?.PlayModalOpen();
            OnShopOpened?.Invoke();
        }

        public void CloseShop()
        {
            if (!isOpen) return;
            isOpen = false;
            OnShopClosed?.Invoke();

            if (transitionRoutine != null) StopCoroutine(transitionRoutine);
            transitionRoutine = StartCoroutine(AnimateCloseRoutine());

            StartBlurTransition(false, 0.20f);
            ApplyVitalsVisibilityAndSorting(false);

            if (ModalManager.Instance != null && ModalManager.Instance.ActiveModal != null &&
                ModalManager.Instance.ActiveModal.modalObject == gameObject)
            {
                ModalManager.Instance.CloseModal(gameObject);
            }

            if (UIOrchestrator.Instance != null)
            {
                UIOrchestrator.Instance.PopOverlay("EcoShop");
                UIOrchestrator.Instance.ApplyCurrentProfile(0.20f);
            }

            RestorePlayerAndCursorState();
            if (closeSound != null) PlaySound(closeSound);
            else if (UITheme.Instance != null && UITheme.Instance.modalCloseSound != null) PlaySound(UITheme.Instance.modalCloseSound);
            else AudioManager.Instance?.PlayModalClose();
        }

        public void HideImmediate()
        {
            isOpen = false;
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
                transitionRoutine = null;
            }
            if (backdropGroup != null)
            {
                backdropGroup.alpha = 0f;
                backdropGroup.blocksRaycasts = false;
                backdropGroup.interactable = false;
                backdropGroup.gameObject.SetActive(false);
            }
            if (panelGroup != null)
            {
                panelGroup.alpha = 0f;
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
            }
            if (panelRect != null)
            {
                panelRect.localScale = Vector3.one;
                panelRect.gameObject.SetActive(false);
            }
            if (coinPillRect != null)
            {
                coinPillRect.gameObject.SetActive(false);
            }
            ApplyVitalsVisibilityAndSorting(false);
            RestorePlayerAndCursorState();
        }

        private IEnumerator AnimateOpenRoutine()
        {
            float duration = 0.28f;
            float elapsed = 0f;

            if (panelGroup != null) panelGroup.alpha = 0f;
            if (backdropGroup != null) backdropGroup.alpha = 0f;

            Vector3 startScale = new Vector3(0.92f, 0.92f, 1f);
            Vector3 targetScale = Vector3.one;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // Slight overshoot curve for juicy pop
                float pop = 1f + Mathf.Sin(t * Mathf.PI) * 0.05f;
                if (panelRect != null) panelRect.localScale = Vector3.Lerp(startScale, targetScale, t) * pop;

                float alpha = Mathf.SmoothStep(0f, 1f, t);
                if (panelGroup != null) panelGroup.alpha = alpha;
                if (backdropGroup != null) backdropGroup.alpha = alpha;

                yield return null;
            }

            if (panelRect != null) panelRect.localScale = targetScale;
            if (panelGroup != null)
            {
                panelGroup.alpha = 1f;
                panelGroup.interactable = true;
                panelGroup.blocksRaycasts = true;
            }
            if (backdropGroup != null)
            {
                backdropGroup.alpha = 1f;
                backdropGroup.blocksRaycasts = true;
                backdropGroup.interactable = true;
            }
        }

        private IEnumerator AnimateCloseRoutine()
        {
            float duration = 0.18f;
            float elapsed = 0f;

            if (panelGroup != null)
            {
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
            }

            Vector3 startScale = panelRect != null ? panelRect.localScale : Vector3.one;
            Vector3 targetScale = new Vector3(0.94f, 0.94f, 1f);

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                if (panelRect != null) panelRect.localScale = Vector3.Lerp(startScale, targetScale, t);

                float alpha = 1f - t;
                if (panelGroup != null) panelGroup.alpha = alpha;
                if (backdropGroup != null) backdropGroup.alpha = alpha;

                yield return null;
            }

            HideImmediate();
        }

        public void PopulateSlots()
        {
            LoadDatabaseIfNeeded();
            if (itemDatabase == null || slotViews == null || slotViews.Count == 0) return;

            string[] shopItemIds = new string[]
            {
                "food_apple",
                "food_sandwich",
                "food_lunchbox",
                "energy_drink",
                "energy_drink_pack",
                "boost_speed_short",
                "boost_speed_long"
            };

            int currentCoins = GetCurrentPlayerCoins();

            // 1. Gather all valid unlocked consumable definitions in original relative order
            var unlockedItems = new List<InventoryItemDef>();
            for (int i = 0; i < shopItemIds.Length; i++)
            {
                var def = itemDatabase.GetItem(shopItemIds[i]);
                if (def != null)
                {
                    unlockedItems.Add(def);
                }
            }

            // 2. Unlocked consumables fill slots from slot 0 upward
            int slotIdx = 0;
            for (; slotIdx < unlockedItems.Count && slotIdx < slotViews.Count; slotIdx++)
            {
                slotViews[slotIdx].Setup(
                    unlockedItems[slotIdx],
                    false,
                    padlockSprite,
                    coinSprite,
                    OnSlotBuyClicked,
                    OnLockedSlotClicked,
                    currentCoins);
            }

            // 3. Locked / expansion slots follow at the end (lower-right)
            for (; slotIdx < slotViews.Count; slotIdx++)
            {
                slotViews[slotIdx].Setup(
                    null,
                    true,
                    padlockSprite,
                    coinSprite,
                    null,
                    OnLockedSlotClicked,
                    currentCoins);
            }
        }

        private int GetCurrentPlayerCoins()
        {
            var currency = PlayerCurrency.Instance ?? FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            return currency != null ? currency.CurrentGold : PlayerPrefs.GetInt("PlayerGold", 0);
        }

        private void OnLockedSlotClicked()
        {
            ShowToast("Locked for now!", isError: false);
            if (errorSound != null) PlaySound(errorSound);
            else if (AudioManager.Instance != null) AudioManager.Instance.PlayShopError();
            else if (UITheme.Instance != null) PlaySound(UITheme.Instance.buttonHoverSound);
        }

        private void OnSlotBuyClicked(InventoryItemDef item)
        {
            if (item == null) return;

            var currency = PlayerCurrency.Instance ?? FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            int currentGold = GetCurrentPlayerCoins();

            if (currentGold < item.price)
            {
                ShowToast("Not enough coins yet - complete more quests!", isError: true);
                if (errorSound != null) PlaySound(errorSound);
                else if (AudioManager.Instance != null) AudioManager.Instance.PlayShopError();
                else if (UITheme.Instance != null) PlaySound(UITheme.Instance.buttonHoverSound);
                return;
            }

            // Deduct coins
            if (currency != null)
            {
                currency.DeductGold(item.price);
            }
            else
            {
                int next = Mathf.Max(0, currentGold - item.price);
                PlayerPrefs.SetInt("PlayerGold", next);
                PlayerPrefs.Save();
            }

            // Add item to inventory
            var invCtrl = InventoryController.Instance ?? FindFirstObjectByType<InventoryController>(FindObjectsInactive.Include);
            if (invCtrl != null && invCtrl.Service != null)
            {
                invCtrl.Service.AddItem(item.id, 1);
            }

            // Preserve normalized scroll position so purchasing never shifts or resets the grid
            float savedNormPos = scrollRect != null ? scrollRect.verticalNormalizedPosition : 1f;

            UpdateCoinPill(true);
            PopulateSlots();

            if (scrollContent != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
            }
            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = savedNormPos;
                targetScrollPos = savedNormPos;
            }

            ShowToast($"Purchased {item.displayName}!", isError: false);
            if (buySound != null) PlaySound(buySound);
            else if (AudioManager.Instance != null) AudioManager.Instance.PlayShopBuy();
            else if (UITheme.Instance != null) PlaySound(UITheme.Instance.coinPickupSound);
        }

        public void UpdateCoinPill(bool animate = false)
        {
            if (coinPillAmountText == null) return;
            int gold = GetCurrentPlayerCoins();
            coinPillAmountText.text = gold.ToString();
        }

        public void ShowToast(string message, bool isError = false)
        {
            if (string.IsNullOrEmpty(message)) return;
            EnsureToastSetup();

            if (toastText != null)
            {
                toastText.text = message;
                toastText.color = isError ? new Color(1.0f, 0.45f, 0.45f, 1f) : Color.white;
            }

            if (toastRoutine != null) StopCoroutine(toastRoutine);
            if (isActiveAndEnabled)
            {
                toastRoutine = StartCoroutine(ToastRoutine(2.2f));
            }
        }

        private IEnumerator ToastRoutine(float duration)
        {
            if (toastGroup == null) yield break;
            toastGroup.gameObject.SetActive(true);
            toastGroup.alpha = 0f;

            float fadeTime = 0.15f;
            float el = 0f;
            while (el < fadeTime)
            {
                el += Time.unscaledDeltaTime;
                toastGroup.alpha = Mathf.Clamp01(el / fadeTime);
                yield return null;
            }
            toastGroup.alpha = 1f;

            yield return new WaitForSecondsRealtime(duration);

            el = 0f;
            while (el < fadeTime)
            {
                el += Time.unscaledDeltaTime;
                toastGroup.alpha = 1f - Mathf.Clamp01(el / fadeTime);
                yield return null;
            }
            toastGroup.alpha = 0f;
            toastGroup.gameObject.SetActive(false);
        }

        private void EnsureToastSetup()
        {
            if (toastGroup != null)
            {
                toastGroup.alpha = 0f;
                toastGroup.blocksRaycasts = false;
                toastGroup.interactable = false;
            }
        }

        private void StartBlurTransition(bool blurOn, float duration)
        {
            if (blurRoutine != null) StopCoroutine(blurRoutine);
            blurRoutine = StartCoroutine(BlurRoutine(blurOn, duration));
        }

        private IEnumerator BlurRoutine(bool blurOn, float duration)
        {
            float startWeight = UIBackgroundBlur.GetWeight();
            float targetWeight = blurOn ? 1f : 0f;

            if (blurOn) UIBackgroundBlur.AcquireFullSceneBlur("EcoShop");

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                UIBackgroundBlur.SetWeight(Mathf.Lerp(startWeight, targetWeight, t));
                yield return null;
            }
            UIBackgroundBlur.SetWeight(targetWeight);

            if (!blurOn) UIBackgroundBlur.ReleaseFullSceneBlur("EcoShop");
        }

        private void ApplyVitalsVisibilityAndSorting(bool elevate)
        {
            if (elevate)
            {
                var statsHud = FindFirstObjectByType<UI_SurvivalStatsHUD>(FindObjectsInactive.Include);
                if (statsHud != null)
                {
                    cachedStatsCanvas = statsHud.GetComponentInParent<Canvas>();
                    if (cachedStatsCanvas != null && !hasElevatedVitalsSorting)
                    {
                        savedStatsSortOrder = cachedStatsCanvas.sortingOrder;
                        cachedStatsCanvas.sortingOrder = 95;
                    }
                }

                var currency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
                if (currency != null)
                {
                    cachedCoinsCanvas = currency.GetComponentInParent<Canvas>();
                    if (cachedCoinsCanvas != null && !hasElevatedVitalsSorting)
                    {
                        savedCoinsSortOrder = cachedCoinsCanvas.sortingOrder;
                        cachedCoinsCanvas.sortingOrder = 95;
                    }
                }
                hasElevatedVitalsSorting = true;
            }
            else
            {
                if (hasElevatedVitalsSorting)
                {
                    if (cachedStatsCanvas != null) cachedStatsCanvas.sortingOrder = savedStatsSortOrder;
                    if (cachedCoinsCanvas != null) cachedCoinsCanvas.sortingOrder = savedCoinsSortOrder;
                    hasElevatedVitalsSorting = false;
                }
            }
        }

        private void SaveAndDisableGameplayInput()
        {
            if (hasSavedPlayerState) return;
            hasSavedPlayerState = true;

            savedCursorLock = Cursor.lockState;
            savedCursorVisible = Cursor.visible;

            cachedFpc = FindFirstObjectByType<FirstPersonController>();
            if (cachedFpc != null)
            {
                savedFpcEnabled = cachedFpc.enabled;
                cachedFpc.enabled = false;
            }

            cachedInputs = FindFirstObjectByType<StarterAssetsInputs>();
            if (cachedInputs != null)
            {
                savedCursorLockedInput = cachedInputs.cursorLocked;
                savedCursorInputForLook = cachedInputs.cursorInputForLook;

                cachedInputs.move = Vector2.zero;
                cachedInputs.look = Vector2.zero;
                cachedInputs.jump = false;
                cachedInputs.sprint = false;
            }

            CursorState.SetLocked(false, cachedInputs);
        }

        private void RestorePlayerAndCursorState()
        {
            if (!hasSavedPlayerState) return;
            hasSavedPlayerState = false;

            if (cachedFpc != null)
            {
                cachedFpc.enabled = savedFpcEnabled;
                cachedFpc = null;
            }

            CursorState.SetLocked(savedCursorLock == CursorLockMode.Locked || Application.isPlaying, cachedInputs);
            cachedInputs = null;
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip == null) return;
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayUI(clip);
                return;
            }
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        public void SetupReferences(
            CanvasGroup bgGrp, Image vigImg, RectTransform pRt, CanvasGroup pCg, Button closeBtn,
            RectTransform bnrRt, Image bnrImg, TextMeshProUGUI titleTmp,
            RectTransform wellRt, ScrollRect sr, RectTransform scRt, Scrollbar sb, Image sbImg,
            List<UI_EcoShopSlot> slots, RectTransform pillRt, Image pillIcn, TextMeshProUGUI pillTxt,
            Sprite padSp, Sprite cnSp)
        {
            backdropGroup = bgGrp;
            vignetteImage = vigImg;
            panelRect = pRt;
            panelGroup = pCg;
            closeButton = closeBtn;
            bannerTransform = bnrRt;
            headerBannerImage = bnrImg;
            headerTitleText = titleTmp;
            contentWellRect = wellRt;
            scrollRect = sr;
            scrollContent = scRt;
            scrollbar = sb;
            scrollbarHandleImage = sbImg;
            slotViews = slots;
            coinPillRect = pillRt;
            coinPillIcon = pillIcn;
            coinPillAmountText = pillTxt;
            padlockSprite = padSp;
            coinSprite = cnSp;
        }
    }
}