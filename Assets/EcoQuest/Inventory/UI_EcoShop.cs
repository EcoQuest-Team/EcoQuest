using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using StarterAssets;

namespace EcoQuest.Inventory
{
    /// <summary>
    /// Master UI controller for the ECO SHOP modal screen.
    /// Matched to the user's approved design (Image 1):
    /// - Dark forest green rounded panel with leaf garland banner and "ECO SHOP" title
    /// - Top-right "X" close button
    /// - 2x4 grid of shop cards: 7 consumables with icons, prices, and BUY buttons,
    ///   plus an 8th locked padlock card.
    /// - Elevates coin and survival stats HUD sorting so they stay visible while shopping.
    /// - Background blur, cursor management, and audio feedback.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UI_EcoShop : MonoBehaviour
    {
        private static UI_EcoShop instance;
        public static UI_EcoShop Instance
        {
            get
            {
                if (instance == null) instance = FindFirstObjectByType<UI_EcoShop>(FindObjectsInactive.Include);
                return instance;
            }
        }

        [Header("Backdrop & Panel")]
        [SerializeField] private CanvasGroup backdropGroup;
        [SerializeField] private RectTransform panelRect;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private Button closeButton;

        [Header("Header")]
        [SerializeField] private Image headerBannerImage;
        [SerializeField] private TextMeshProUGUI headerTitleText;

        [Header("Grid & Cards (8 Slots)")]
        [SerializeField] private RectTransform gridContainerRect;
        [SerializeField] private List<UI_EcoShopSlot> slotViews = new List<UI_EcoShopSlot>();

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

        private void Update()
        {
            if (!isOpen) return;

            // Maintain cursor unlocked while shop is open
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

            // Close with Escape or B
            if (IsEscapePressed() || IsBPressed())
            {
                CloseShop();
            }
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

            if (transitionRoutine != null) StopCoroutine(transitionRoutine);
            transitionRoutine = StartCoroutine(AnimateOpenRoutine());

            PlaySound(openSound != null ? openSound : (UITheme.Instance != null ? UITheme.Instance.modalOpenSound : null));
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
            PlaySound(closeSound != null ? closeSound : (UITheme.Instance != null ? UITheme.Instance.modalCloseSound : null));
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
            ApplyVitalsVisibilityAndSorting(false);
            RestorePlayerAndCursorState();
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

            for (int i = 0; i < slotViews.Count; i++)
            {
                if (i < shopItemIds.Length)
                {
                    var def = itemDatabase.GetItem(shopItemIds[i]);
                    slotViews[i].Setup(def, false, padlockSprite, coinSprite, OnSlotBuyClicked);
                }
                else
                {
                    // 8th slot: locked with padlock
                    slotViews[i].Setup(null, true, padlockSprite, coinSprite, null);
                }
            }
        }

        private void OnSlotBuyClicked(InventoryItemDef item)
        {
            if (item == null) return;

            var currency = PlayerCurrency.Instance ?? FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            int currentGold = currency != null ? currency.CurrentGold : PlayerPrefs.GetInt("PlayerGold", 0);

            if (currentGold < item.price)
            {
                ShowToast("Not enough coins!", isError: true);
                PlaySound(errorSound != null ? errorSound : (UITheme.Instance != null ? UITheme.Instance.buttonHoverSound : null));
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

            ShowToast($"Purchased {item.displayName}!", isError: false);
            PlaySound(buySound != null ? buySound : (UITheme.Instance != null ? UITheme.Instance.coinPickupSound : null));
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
                toastRoutine = StartCoroutine(ToastRoutine(2.0f));
            }
        }

        private IEnumerator ToastRoutine(float duration)
        {
            if (toastGroup == null) yield break;
            toastGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(duration);

            float fade = 0.35f;
            float el = 0f;
            while (el < fade)
            {
                el += Time.unscaledDeltaTime;
                toastGroup.alpha = Mathf.Lerp(1f, 0f, el / fade);
                yield return null;
            }
            toastGroup.alpha = 0f;
            toastRoutine = null;
        }

        private IEnumerator AnimateOpenRoutine()
        {
            if (backdropGroup != null)
            {
                backdropGroup.alpha = 0f;
                backdropGroup.blocksRaycasts = true;
            }
            if (panelGroup != null)
            {
                panelGroup.alpha = 0f;
                panelGroup.interactable = true;
                panelGroup.blocksRaycasts = true;
            }
            if (panelRect != null)
            {
                panelRect.localScale = new Vector3(0.95f, 0.95f, 1f);
            }

            float duration = 0.25f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = 1f - Mathf.Pow(1f - t, 3f);

                if (backdropGroup != null) backdropGroup.alpha = ease;
                if (panelGroup != null) panelGroup.alpha = ease;
                if (panelRect != null) panelRect.localScale = Vector3.LerpUnclamped(new Vector3(0.95f, 0.95f, 1f), Vector3.one, ease);
                yield return null;
            }

            if (backdropGroup != null) backdropGroup.alpha = 1f;
            if (panelGroup != null)
            {
                panelGroup.alpha = 1f;
                panelGroup.interactable = true;
                panelGroup.blocksRaycasts = true;
            }
            if (panelRect != null) panelRect.localScale = Vector3.one;
            transitionRoutine = null;
        }

        private IEnumerator AnimateCloseRoutine()
        {
            if (panelGroup != null)
            {
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
            }

            float duration = 0.20f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = t * t;

                if (backdropGroup != null) backdropGroup.alpha = 1f - ease;
                if (panelGroup != null) panelGroup.alpha = 1f - ease;
                if (panelRect != null) panelRect.localScale = Vector3.LerpUnclamped(Vector3.one, new Vector3(0.95f, 0.95f, 1f), ease);
                yield return null;
            }

            HideImmediate();
            transitionRoutine = null;
        }

        private void SaveAndDisableGameplayInput()
        {
            if (!hasSavedPlayerState)
            {
                savedCursorLock = Cursor.lockState;
                savedCursorVisible = Cursor.visible;

                cachedFpc = FindFirstObjectByType<FirstPersonController>();
                if (cachedFpc != null) savedFpcEnabled = cachedFpc.enabled;

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

            bool otherModalActive = (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalActuallyVisible());
            if (!otherModalActive)
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

                Cursor.lockState = savedCursorLock == CursorLockMode.None && Application.isPlaying ? CursorLockMode.Locked : savedCursorLock;
                Cursor.visible = savedCursorLock == CursorLockMode.None && Application.isPlaying ? false : savedCursorVisible;
            }
        }

        private void ApplyVitalsVisibilityAndSorting(bool opening)
        {
            var statsHud = FindFirstObjectByType<UI_SurvivalStatsHUD>(FindObjectsInactive.Include);
            if (statsHud != null && cachedStatsCanvas == null)
            {
                cachedStatsCanvas = statsHud.GetComponentInParent<Canvas>();
            }

            if (cachedCoinsCanvas == null)
            {
                var coinsGo = GameObject.Find("UI_Coins");
                if (coinsGo != null) cachedCoinsCanvas = coinsGo.GetComponentInParent<Canvas>();
            }

            if (opening)
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
            else if (hasElevatedVitalsSorting)
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
                UIBackgroundBlur.AcquireFullSceneBlur("EcoShop");
            }

            if (!gameObject.activeInHierarchy)
            {
                if (!opening) UIBackgroundBlur.ReleaseFullSceneBlur("EcoShop");
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
                UIBackgroundBlur.ReleaseFullSceneBlur("EcoShop");
            }
            blurRoutine = null;
        }

        private void EnsureToastSetup()
        {
            if (toastGroup != null && toastText != null) return;
            Transform existing = transform.Find("ShopToast");
            if (existing != null)
            {
                toastGroup = existing.GetComponent<CanvasGroup>();
                toastText = existing.GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip == null) return;
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource != null && audioSource.isActiveAndEnabled)
            {
                audioSource.PlayOneShot(clip, 0.85f);
            }
        }

        public void SetupReferences(
            CanvasGroup bdGroup, RectTransform pnlRt, CanvasGroup pnlGroup, Button closeBtn,
            Image bannerImg, TextMeshProUGUI titleTmp, RectTransform gridRt,
            List<UI_EcoShopSlot> slots, Sprite padlock, Sprite coin)
        {
            backdropGroup = bdGroup;
            panelRect = pnlRt;
            panelGroup = pnlGroup;
            closeButton = closeBtn;
            headerBannerImage = bannerImg;
            headerTitleText = titleTmp;
            gridContainerRect = gridRt;
            slotViews = slots;
            padlockSprite = padlock;
            coinSprite = coin;
        }
    }
}
