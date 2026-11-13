using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using TMPro;

namespace EcoQuest.Inventory
{
    /// <summary>
    /// Master UI view for the EcoQuest Inventory modal screen.
    /// Built with responsive layout groups (~60% screen width, 70% height),
    /// 3-column dynamic square GridLayoutGroup inside a RectMask2D ScrollRect viewport,
    /// 14px draggable vertical scrollbar with >=40px handle, right-column item preview & details,
    /// click-only USE button, and shared KeyCapView/HintRow footer inside the panel.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UI_Inventory : MonoBehaviour
    {
        [Header("Theme & Accent")]
        [Tooltip("Match shared dark-green UITheme / PopupFrame style (#0B2419 @ 97%, #6DBE6D border).")]
        [SerializeField] private bool useSharedDarkGreenTheme = true;
        [Tooltip("Single serialized accent color field used for selection glow and enabled USE button.")]
        public Color accent = new Color(0.427f, 0.745f, 0.427f, 1f); // #6DBE6D
        [SerializeField] private Color panelBorderColor = new Color(0.427f, 0.745f, 0.427f, 1f); // #6DBE6D
        [SerializeField] private Color panelFillColor = new Color(0.043f, 0.141f, 0.098f, 0.97f); // #0B2419 @ 97%
        [SerializeField] private Color disabledColor = new Color(0.541f, 0.541f, 0.541f, 0.70f);

        [Header("Backdrop & Panel")]
        [SerializeField] private CanvasGroup backdropGroup;
        [SerializeField] private RectTransform panelRect;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private Button closeButton;

        [Header("Header")]
        [SerializeField] private TextMeshProUGUI titleText;

        [Header("Grid & Scrolling")]
        [SerializeField] private RectTransform gridViewportRect;
        [SerializeField] private RectTransform gridContentRect;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private Scrollbar verticalScrollbar;
        [SerializeField] private GameObject emptyInventoryMessageGo;
        [SerializeField] private TextMeshProUGUI emptyInventoryText;

        [Header("Right Column - Preview & Details")]
        [SerializeField] private RectTransform previewBoxRect;
        [SerializeField] private Image previewIcon;
        [SerializeField] private GameObject emptyPreviewGo;
        [SerializeField] private TextMeshProUGUI emptyPreviewText;
        [SerializeField] private Image emptyPreviewBackpackIcon;
        [SerializeField] private CanvasGroup previewFlashGroup;

        [SerializeField] private RectTransform detailsBoxRect;
        [SerializeField] private TextMeshProUGUI detailNameText;
        [SerializeField] private TextMeshProUGUI detailEffectText;
        [SerializeField] private TextMeshProUGUI detailOwnedText;
        [SerializeField] private TextMeshProUGUI detailDescText;

        [Header("USE Button")]
        [SerializeField] private Button useButton;
        [SerializeField] private Image useButtonBg;
        [SerializeField] private TextMeshProUGUI useButtonText;
        [SerializeField] private RectTransform useButtonRect;

        [Header("Keycap Prompt Strip (Footer)")]
        [SerializeField] private RectTransform hintStripRect;
        [SerializeField] private TextMeshProUGUI hintText;

        [Header("Themed Toast Banner")]
        [SerializeField] private CanvasGroup toastGroup;
        [SerializeField] private TextMeshProUGUI toastText;

        [Header("Sprites")]
        [SerializeField] private Sprite slotNormalSprite;
        [SerializeField] private Sprite slotHoverSprite;
        [SerializeField] private Sprite slotSelectedSprite;
        [SerializeField] private Sprite btnNormalSprite;
        [SerializeField] private Sprite btnHoverSprite;
        [SerializeField] private Sprite btnDisabledSprite;

        [Header("Audio (Optional / Null-Safe)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip openSound;
        [SerializeField] private AudioClip closeSound;
        [SerializeField] private AudioClip selectSound;
        [SerializeField] private AudioClip useItemSound;

        [Header("Consumable Usage & Cooldown")]
        [SerializeField] private float useCooldownDuration = 0.35f;
        private float useLockoutUntil = -1f;
        private bool isUsePending = false;
        private int lastBoostButtonSec = -1;
        private bool lastBoostBlockedState = false;
        private bool wasUseCooldownActive = false;
        private Coroutine useCooldownRoutine;

        public bool IsInUseCooldown => Application.isPlaying && Time.unscaledTime < useLockoutUntil;
        public float UseCooldownDuration { get => useCooldownDuration; set => useCooldownDuration = value; }
        public void ResetUseCooldown() { useLockoutUntil = -1f; isUsePending = false; }

        private InventoryController controller;
        private InventoryService service;
        private readonly List<UI_InventorySlot> slotViews = new List<UI_InventorySlot>();
        private int selectedIndex = -1;
        private Coroutine transitionRoutine;
        private Coroutine useFlashRoutine;
        private Coroutine toastRoutine;
        private float lastViewportWidth = -1f;

        public bool UseSharedDarkGreenTheme => useSharedDarkGreenTheme;
        public int SelectedIndex => selectedIndex;
        public IReadOnlyList<UI_InventorySlot> SlotViews => slotViews;
        public ScrollRect GridScrollRect => scrollRect;
        public Scrollbar VerticalScrollbar => verticalScrollbar;
        public RectTransform GridViewportRect => gridViewportRect;
        public RectTransform GridContentRect => gridContentRect;
        public RectTransform PanelRect => panelRect;
        public RectTransform FooterRect => hintStripRect;
        public Button UseButton => useButton;
        public Button CloseButton => closeButton;
        public GameObject EmptyInventoryMessageGo => emptyInventoryMessageGo;
        public string LastToastMessage { get; private set; } = string.Empty;

        public void Initialize(InventoryController ctrl, InventoryService srv)
        {
            controller = ctrl;
            service = srv;

            if (service != null)
            {
                service.OnInventoryChanged -= RefreshUI;
                service.OnInventoryChanged += RefreshUI;
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(() => controller?.CloseInventory(true));
            }

            if (useButton != null)
            {
                useButton.onClick.RemoveAllListeners();
                useButton.onClick.AddListener(OnUseButtonClicked);
            }

            if (hintStripRect != null)
            {
                hintStripRect.gameObject.SetActive(false);
            }

            EnsureScrollAndGridSetup();
            BuildSlotPool();
            UpdateDynamicGridCellSize(true);
            RefreshUI();
            HideImmediate();
        }

        private void OnEnable()
        {
            PlayerSurvivalStats.OnActiveBuffsChanged -= HandleActiveBuffsChanged;
            PlayerSurvivalStats.OnActiveBuffsChanged += HandleActiveBuffsChanged;
            lastBoostButtonSec = -1;
            lastBoostBlockedState = false;
        }

        private void OnDisable()
        {
            PlayerSurvivalStats.OnActiveBuffsChanged -= HandleActiveBuffsChanged;
            StopTransientRoutines();
            isUsePending = false;
        }

        private void OnDestroy()
        {
            PlayerSurvivalStats.OnActiveBuffsChanged -= HandleActiveBuffsChanged;
            StopTransientRoutines();
            if (service != null)
            {
                service.OnInventoryChanged -= RefreshUI;
            }
        }

        private void HandleActiveBuffsChanged()
        {
            lastBoostButtonSec = -1;
            if (gameObject.activeInHierarchy && service != null)
            {
                UpdateDetailsAndPreview();
            }
        }

        private void StopTransientRoutines()
        {
            if (useCooldownRoutine != null)
            {
                StopCoroutine(useCooldownRoutine);
                useCooldownRoutine = null;
            }
            if (toastRoutine != null)
            {
                StopCoroutine(toastRoutine);
                toastRoutine = null;
                if (toastGroup != null) toastGroup.alpha = 0f;
            }
            if (useFlashRoutine != null)
            {
                StopCoroutine(useFlashRoutine);
                useFlashRoutine = null;
                if (previewFlashGroup != null) previewFlashGroup.alpha = 0f;
            }
        }

        private void EnsureScrollAndGridSetup()
        {
            if (scrollRect != null)
            {
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
                scrollRect.inertia = true;
                scrollRect.decelerationRate = 0.08f;
                scrollRect.scrollSensitivity = 32f;
                if (verticalScrollbar != null)
                {
                    verticalScrollbar.direction = Scrollbar.Direction.BottomToTop;
                    scrollRect.verticalScrollbar = verticalScrollbar;
                    scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
                    verticalScrollbar.onValueChanged.RemoveListener(OnScrollbarValueChanged);
                    verticalScrollbar.onValueChanged.AddListener(OnScrollbarValueChanged);
                }
            }

            if (gridContentRect != null)
            {
                var csf = gridContentRect.GetComponent<ContentSizeFitter>();
                if (csf == null) csf = gridContentRect.gameObject.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            EnsureToastSetup();
        }

        private void EnsureToastSetup()
        {
            if (toastGroup != null && toastText != null) return;

            Transform existing = transform.Find("InventoryToast");
            if (existing != null)
            {
                toastGroup = existing.GetComponent<CanvasGroup>();
                toastText = existing.GetComponentInChildren<TextMeshProUGUI>(true);
                if (toastGroup != null)
                {
                    toastGroup.alpha = 0f;
                    toastGroup.blocksRaycasts = false;
                    toastGroup.interactable = false;
                }
                return;
            }

            var toastGo = new GameObject("InventoryToast", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline));
            toastGo.transform.SetParent(transform, false);
            var rt = toastGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.90f);
            rt.anchorMax = new Vector2(0.5f, 0.90f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(460f, 54f);

            var img = toastGo.GetComponent<Image>();
            img.color = new Color(0.043f, 0.141f, 0.098f, 0.97f);
            img.raycastTarget = false;

            var outline = toastGo.GetComponent<Outline>();
            outline.effectColor = new Color(0.427f, 0.745f, 0.427f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);

            toastGroup = toastGo.GetComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            toastGroup.blocksRaycasts = false;
            toastGroup.interactable = false;

            var txtGo = new GameObject("ToastText", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(toastGo.transform, false);
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(16f, 4f);
            txtRt.offsetMax = new Vector2(-16f, -4f);

            toastText = txtGo.GetComponent<TextMeshProUGUI>();
            if (UITheme.Instance != null && UITheme.Instance.GetBodyFont() != null)
                toastText.font = UITheme.Instance.GetBodyFont();
            toastText.fontSize = 22f;
            toastText.fontStyle = FontStyles.Bold;
            toastText.alignment = TextAlignmentOptions.Center;
            toastText.color = Color.white;
            toastText.raycastTarget = false;
        }

        private void Update()
        {
            if (panelGroup == null || panelGroup.alpha < 0.1f || (panelRect != null && !panelRect.gameObject.activeSelf))
                return;

            UpdateUseButtonLiveState();
            UpdateDynamicGridCellSize(false);
            EnforceScrollbarMinHandleSize();

            // Slot selection is purely mouse-driven (no WASD navigation)

            // Mouse wheel scrolling works anywhere while inventory is open
            float wheel = GetMouseWheelDelta();
            if (Mathf.Abs(wheel) > 0.001f)
            {
                ApplyMouseWheelScroll(wheel);
            }
        }

        private float GetMouseWheelDelta()
        {
            float wheel = Input.mouseScrollDelta.y;
#if ENABLE_INPUT_SYSTEM
            if (Mathf.Abs(wheel) < 0.001f && UnityEngine.InputSystem.Mouse.current != null)
            {
                wheel = UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y / 120f;
            }
#endif
            return wheel;
        }

        private void OnScrollbarValueChanged(float val)
        {
            SetNormalizedScrollPosition(val);
        }

        public float GetNormalizedScrollPosition()
        {
            if (gridContentRect == null || gridViewportRect == null) return 1f;
            float scrollable = gridContentRect.rect.height - gridViewportRect.rect.height;
            if (scrollable <= 1f) return 1f;
            return 1f - Mathf.Clamp01(gridContentRect.anchoredPosition.y / scrollable);
        }

        public void SetNormalizedScrollPosition(float normPos)
        {
            normPos = Mathf.Clamp01(normPos);
            if (gridContentRect != null && gridViewportRect != null)
            {
                float scrollable = Mathf.Max(0f, gridContentRect.rect.height - gridViewportRect.rect.height);
                gridContentRect.anchoredPosition = new Vector2(gridContentRect.anchoredPosition.x, (1f - normPos) * scrollable);
            }
            if (scrollRect != null && Application.isPlaying && Mathf.Abs(scrollRect.verticalNormalizedPosition - normPos) > 0.001f)
            {
                scrollRect.verticalNormalizedPosition = normPos;
            }
            if (verticalScrollbar != null && Mathf.Abs(verticalScrollbar.value - normPos) > 0.001f)
            {
                verticalScrollbar.SetValueWithoutNotify(normPos);
            }
            EnforceScrollbarMinHandleSize();
        }

        public void ApplyMouseWheelScroll(float wheelNotches)
        {
            if (scrollRect == null || gridContentRect == null || gridViewportRect == null) return;
            float currentNorm = GetNormalizedScrollPosition();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(gridContentRect);
            float scrollableHeight = Mathf.Max(0f, gridContentRect.rect.height - gridViewportRect.rect.height);
            if (scrollableHeight <= 1f) return;

            float pixelStep = wheelNotches * 95f;
            float normDelta = pixelStep / scrollableHeight;
            float next = Mathf.Clamp01(currentNorm + normDelta);
            SetNormalizedScrollPosition(next);
        }

        public void UpdateDynamicGridCellSize(bool force)
        {
            if (gridViewportRect == null || gridContentRect == null) return;
            float vpWidth = gridViewportRect.rect.width;
            if (vpWidth <= 10f) return;
            if (!force && Mathf.Abs(vpWidth - lastViewportWidth) < 0.5f) return;

            lastViewportWidth = vpWidth;
            var glg = gridContentRect.GetComponent<GridLayoutGroup>();
            if (glg != null)
            {
                glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                glg.constraintCount = 3;
                glg.spacing = new Vector2(12f, 12f);
                glg.padding = new RectOffset(8, 8, 8, 8);
                float usable = vpWidth - glg.padding.left - glg.padding.right - glg.spacing.x * 2f;
                float cell = Mathf.Floor(Mathf.Max(64f, usable / 3f));
                glg.cellSize = new Vector2(cell, cell);
                LayoutRebuilder.ForceRebuildLayoutImmediate(gridContentRect);
            }
            EnforceScrollbarMinHandleSize();
        }

        public void EnforceScrollbarMinHandleSize()
        {
            if (verticalScrollbar == null) return;
            var sbRt = verticalScrollbar.transform as RectTransform;
            float trackH = sbRt != null ? sbRt.rect.height : 0f;
            if (trackH <= 1f) trackH = 400f;

            if (gridViewportRect != null && gridContentRect != null && gridContentRect.rect.height > gridViewportRect.rect.height + 1f)
            {
                float naturalRatio = Mathf.Clamp01(gridViewportRect.rect.height / gridContentRect.rect.height);
                float minRatio = Mathf.Clamp(40f / trackH, 0.08f, 0.50f);
                verticalScrollbar.size = Mathf.Max(minRatio, naturalRatio);
            }
            else
            {
                float minRatio = Mathf.Clamp(40f / trackH, 0.08f, 0.50f);
                if (verticalScrollbar.size < minRatio)
                {
                    verticalScrollbar.size = minRatio;
                }
            }
        }

        public void Show()
        {
            gameObject.SetActive(true);
            if (backdropGroup != null) backdropGroup.gameObject.SetActive(true);
            if (panelRect != null) panelRect.gameObject.SetActive(true);

            Canvas.ForceUpdateCanvases();
            UpdateDynamicGridCellSize(true);

            if (transitionRoutine != null) StopCoroutine(transitionRoutine);
            transitionRoutine = StartCoroutine(AnimateOpenRoutine());

            if (openSound != null) PlaySound(openSound);
            else if (UITheme.Instance != null && UITheme.Instance.modalOpenSound != null) PlaySound(UITheme.Instance.modalOpenSound);
            else AudioManager.Instance?.PlayModalOpen();
            RefreshUI();

            // Auto-select first occupied slot if current selection is invalid or empty
            int targetIdx = selectedIndex;
            if (service != null)
            {
                if (targetIdx < 0 || targetIdx >= service.Capacity || service.Slots[targetIdx].IsEmpty)
                {
                    targetIdx = GetFirstOccupiedSlotIndex();
                }
            }
            if (targetIdx >= 0)
            {
                SelectSlot(targetIdx, false);
            }
            else
            {
                ClearSelection();
            }

            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
            if (verticalScrollbar != null) verticalScrollbar.value = 1f;
            EnforceScrollbarMinHandleSize();
        }

        public void Hide()
        {
            if (!gameObject.activeInHierarchy)
            {
                HideImmediate();
                return;
            }
            if (transitionRoutine != null) StopCoroutine(transitionRoutine);
            transitionRoutine = StartCoroutine(AnimateCloseRoutine());
            if (closeSound != null) PlaySound(closeSound);
            else if (UITheme.Instance != null && UITheme.Instance.modalCloseSound != null) PlaySound(UITheme.Instance.modalCloseSound);
            else AudioManager.Instance?.PlayModalClose();
        }

        public void HideImmediate()
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
                transitionRoutine = null;
            }
            StopTransientRoutines();
            isUsePending = false;
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
        }

        private int GetFirstOccupiedSlotIndex()
        {
            if (service == null) return -1;
            for (int i = 0; i < service.Slots.Count; i++)
            {
                if (!service.Slots[i].IsEmpty) return i;
            }
            return -1;
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

            UpdateDynamicGridCellSize(true);
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

        private void BuildSlotPool()
        {
            if (gridContentRect == null) return;

            for (int i = gridContentRect.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(gridContentRect.GetChild(i).gameObject);
                else
                    DestroyImmediate(gridContentRect.GetChild(i).gameObject);
            }
            slotViews.Clear();

            int capacity = service != null ? service.Capacity : 24;
            for (int i = 0; i < capacity; i++)
            {
                var slotGo = new GameObject($"Slot_{i:00}", typeof(RectTransform), typeof(UI_InventorySlot));
                slotGo.transform.SetParent(gridContentRect, false);

                var slotView = slotGo.GetComponent<UI_InventorySlot>();
                BuildSlotVisualHierarchy(slotGo, slotView);

                slotView.Setup(i, slotNormalSprite, slotHoverSprite, slotSelectedSprite, OnSlotClicked, OnSlotDoubleClicked);
                slotViews.Add(slotView);
            }
        }

        private void BuildSlotVisualHierarchy(GameObject slotGo, UI_InventorySlot slotView)
        {
            var rt = slotGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(128f, 128f);

            // Frame Image
            var frameImg = slotGo.AddComponent<Image>();
            frameImg.sprite = slotNormalSprite;
            frameImg.type = Image.Type.Sliced;
            frameImg.color = Color.white;

            // Icon Image
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(slotGo.transform, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.14f, 0.14f);
            iconRt.anchorMax = new Vector2(0.86f, 0.86f);
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            iconImg.enabled = false;

            // Count Text
            var countGo = new GameObject("CountText", typeof(RectTransform), typeof(TextMeshProUGUI));
            countGo.transform.SetParent(slotGo.transform, false);
            var countRt = countGo.GetComponent<RectTransform>();
            countRt.anchorMin = new Vector2(0.35f, 0.04f);
            countRt.anchorMax = new Vector2(0.92f, 0.36f);
            countRt.offsetMin = Vector2.zero;
            countRt.offsetMax = Vector2.zero;
            var countTmp = countGo.GetComponent<TextMeshProUGUI>();
            if (UITheme.Instance != null && UITheme.Instance.GetTitleFont() != null)
                countTmp.font = UITheme.Instance.GetTitleFont();
            countTmp.alignment = TextAlignmentOptions.BottomRight;
            countTmp.fontSize = 20f;
            countTmp.fontStyle = FontStyles.Bold;
            countTmp.color = Color.white;
            countTmp.raycastTarget = false;
            countTmp.enabled = false;

            SetSlotReferences(slotView, frameImg, iconImg, countTmp, rt);
        }

        private void SetSlotReferences(UI_InventorySlot slot, Image frame, Image icon, TextMeshProUGUI count, RectTransform rt)
        {
            var type = typeof(UI_InventorySlot);
            var fFrame = type.GetField("slotFrameImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fIcon = type.GetField("itemIconImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fCount = type.GetField("countText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fRt = type.GetField("rectTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            fFrame?.SetValue(slot, frame);
            fIcon?.SetValue(slot, icon);
            fCount?.SetValue(slot, count);
            fRt?.SetValue(slot, rt);
        }

        public void RefreshUI()
        {
            if (service == null) return;

            int totalItems = service.GetTotalItemCount();
            bool isEmpty = totalItems == 0;

            if (emptyInventoryMessageGo != null)
            {
                emptyInventoryMessageGo.SetActive(isEmpty);
            }

            int occupiedCount = service.GetOccupiedSlotCount();
            if (isEmpty || occupiedCount == 0)
            {
                selectedIndex = -1;
            }
            else if (selectedIndex >= occupiedCount)
            {
                selectedIndex = occupiedCount - 1;
            }

            for (int i = 0; i < slotViews.Count && i < service.Slots.Count; i++)
            {
                var slotData = service.Slots[i];
                var def = service.GetItemDefAt(i);
                slotViews[i].Bind(slotData, def, i == selectedIndex);
            }

            UpdateDetailsAndPreview();
            EnforceScrollbarMinHandleSize();
        }

        public void ClearSelection()
        {
            selectedIndex = -1;
            for (int i = 0; i < slotViews.Count; i++)
            {
                slotViews[i].SetSelected(false);
            }
            UpdateDetailsAndPreview();
        }

        public void SelectSlot(int index, bool playAudio = true)
        {
            if (service == null || index < 0 || index >= service.Capacity)
            {
                ClearSelection();
                return;
            }

            selectedIndex = index;
            for (int i = 0; i < slotViews.Count; i++)
            {
                slotViews[i].SetSelected(i == selectedIndex);
            }

            if (playAudio) PlaySound(selectSound != null ? selectSound : (UITheme.Instance != null ? UITheme.Instance.buttonHoverSound : null));

            ScrollSlotIntoView(selectedIndex);
            UpdateDetailsAndPreview();
        }

        public void ScrollSlotIntoView(int index)
        {
            if (scrollRect == null || gridViewportRect == null || gridContentRect == null) return;
            if (index < 0 || index >= slotViews.Count) return;

            Canvas.ForceUpdateCanvases();
            float vpHeight = gridViewportRect.rect.height;
            float contentHeight = gridContentRect.rect.height;
            float scrollable = contentHeight - vpHeight;
            if (scrollable <= 1f) return;

            var slotRt = slotViews[index].transform as RectTransform;
            if (slotRt == null) return;

            // In top-anchored content, slot anchoredPosition.y is negative from top
            float slotHalfH = slotRt.rect.height * 0.5f;
            float slotTopFromTop = -slotRt.anchoredPosition.y - slotHalfH;
            float slotBottomFromTop = -slotRt.anchoredPosition.y + slotHalfH;

            float currentNorm = scrollRect.verticalNormalizedPosition;
            float viewTop = (1f - currentNorm) * scrollable;
            float viewBottom = viewTop + vpHeight;

            float newViewTop = viewTop;
            if (slotTopFromTop < viewTop + 8f)
            {
                newViewTop = Mathf.Max(0f, slotTopFromTop - 8f);
            }
            else if (slotBottomFromTop > viewBottom - 8f)
            {
                newViewTop = Mathf.Min(scrollable, slotBottomFromTop - vpHeight + 8f);
            }
            else
            {
                return;
            }

            float targetNorm = 1f - Mathf.Clamp01(newViewTop / scrollable);
            SetNormalizedScrollPosition(targetNorm);
        }

        private void OnSlotClicked(int index)
        {
            SelectSlot(index, true);
        }

        private void OnSlotDoubleClicked(int index)
        {
            if (isUsePending || IsInUseCooldown) return;
            SelectSlot(index, false);
            OnUseButtonClicked();
        }

        private void UpdateDetailsAndPreview()
        {
            if (service == null) return;

            var def = (selectedIndex >= 0 && selectedIndex < service.Capacity) ? service.GetItemDefAt(selectedIndex) : null;
            var slotData = (selectedIndex >= 0 && selectedIndex < service.Slots.Count) ? service.Slots[selectedIndex] : null;

            if (def != null && slotData != null && !slotData.IsEmpty)
            {
                // Item preview
                if (previewIcon != null)
                {
                    previewIcon.sprite = def.icon;
                    previewIcon.enabled = def.icon != null;
                }
                if (emptyPreviewGo != null) emptyPreviewGo.SetActive(false);

                // Details box
                if (detailNameText != null) detailNameText.text = def.displayName;
                if (detailEffectText != null)
                {
                    detailEffectText.text = def.GetEffectiveEffectText();
                    detailEffectText.color = accent;
                }
                if (detailOwnedText != null)
                {
                    int totalOwned = service.GetItemCount(def.id);
                    detailOwnedText.text = $"Owned: {totalOwned}";
                    detailOwnedText.enabled = true;
                }
                if (detailDescText != null) detailDescText.text = def.description;

                // USE button state: block active boost group reuse with live countdown
                bool blockedByBoost = InventoryService.IsBlockedByActiveBoost(
                    def,
                    PlayerSurvivalStats.Instance,
                    out float activeRemaining,
                    out _);
                lastBoostBlockedState = blockedByBoost;
                if (blockedByBoost)
                {
                    int sec = Mathf.Max(1, Mathf.CeilToInt(activeRemaining));
                    lastBoostButtonSec = sec;
                    SetUseButtonLabel($"Active {sec / 60}:{sec % 60:00}");
                    SetUseButtonState(false);
                }
                else
                {
                    lastBoostButtonSec = -1;
                    SetUseButtonLabel("USE");
                    SetUseButtonState(!isUsePending && !IsInUseCooldown);
                }
            }
            else
            {
                // Empty preview
                if (previewIcon != null) previewIcon.enabled = false;
                if (emptyPreviewGo != null) emptyPreviewGo.SetActive(true);

                // Details box clear
                if (detailNameText != null) detailNameText.text = "No item selected";
                if (detailEffectText != null) detailEffectText.text = string.Empty;
                if (detailOwnedText != null)
                {
                    detailOwnedText.text = string.Empty;
                    detailOwnedText.enabled = false;
                }
                if (detailDescText != null) detailDescText.text = "Select an item from your inventory to view its details and effects.";

                // USE button state: Disabled
                lastBoostBlockedState = false;
                lastBoostButtonSec = -1;
                SetUseButtonLabel("USE");
                SetUseButtonState(false);
            }
        }

        private void UpdateUseButtonLiveState()
        {
            if (service == null) return;

            bool cooldownNow = IsInUseCooldown;
            if (wasUseCooldownActive && !cooldownNow)
            {
                wasUseCooldownActive = false;
                UpdateDetailsAndPreview();
                return;
            }
            wasUseCooldownActive = cooldownNow;

            var def = (selectedIndex >= 0 && selectedIndex < service.Capacity)
                ? service.GetItemDefAt(selectedIndex)
                : null;
            if (def == null) return;

            bool blocked = InventoryService.IsBlockedByActiveBoost(
                def,
                PlayerSurvivalStats.Instance,
                out float remaining,
                out _);
            int sec = blocked ? Mathf.Max(1, Mathf.CeilToInt(remaining)) : -1;

            if (blocked != lastBoostBlockedState || (blocked && sec != lastBoostButtonSec))
            {
                UpdateDetailsAndPreview();
            }
        }

        private void SetUseButtonLabel(string label)
        {
            if (useButtonText != null && useButtonText.text != label)
            {
                useButtonText.text = label;
            }
            RefreshUseButtonFit();
        }

        private void SetUseButtonState(bool isEnabled)
        {
            if (useButton == null) return;
            useButton.interactable = isEnabled;

            if (useButtonBg != null)
            {
                useButtonBg.sprite = isEnabled ? btnNormalSprite : btnDisabledSprite;
                useButtonBg.color = isEnabled ? new Color(0.180f, 0.490f, 0.310f, 1f) : disabledColor;
            }

            if (useButtonText != null)
            {
                useButtonText.color = isEnabled ? Color.white : new Color(0.92f, 0.94f, 0.90f, 0.88f);
            }

            RefreshUseButtonFit();
        }

        private void RefreshUseButtonFit()
        {
            if (useButton == null || useButtonText == null) return;
            var rtb = ResponsiveTextButton.AttachOrGet(useButton);
            rtb.Configure(
                labelRef: useButtonText,
                backgroundRef: useButtonBg != null ? useButtonBg : useButton.GetComponent<Image>(),
                minFont: 14f,
                maxFont: 22f,
                minBtnHeight: 52f,
                maxBtnHeight: 90f,
                minBtnWidth: 180f,
                maxBtnWidth: 260f,
                fitWidth: true,
                allowHeightGrow: true,
                padLeft: 16f,
                padRight: 16f,
                padVertical: 10f,
                manageMargins: true);
        }

        public void OnUseButtonClicked()
        {
            if (isUsePending || IsInUseCooldown) return;
            if (service == null || selectedIndex < 0 || selectedIndex >= service.Capacity) return;

            int usedSlot = selectedIndex;
            var slotData = service.Slots[usedSlot];
            if (slotData.IsEmpty) return;
            bool wasLastInStack = slotData.count == 1;

            bool blockFull = controller != null ? controller.BlockUseWhenFull : true;

            isUsePending = true;
            bool success = false;
            string message = string.Empty;
            try
            {
                success = service.TryUse(usedSlot, blockFull, out message);
            }
            finally
            {
                isUsePending = false;
            }

            if (success)
            {
                useLockoutUntil = Time.unscaledTime + useCooldownDuration;
                wasUseCooldownActive = true;
                if (useCooldownRoutine != null) StopCoroutine(useCooldownRoutine);
                useCooldownRoutine = isActiveAndEnabled ? StartCoroutine(UseCooldownRoutine(useCooldownDuration)) : null;

                if (useItemSound != null) PlaySound(useItemSound);
                else if (AudioManager.Instance != null) AudioManager.Instance.PlayItemUse();
                else if (UITheme.Instance != null && UITheme.Instance.buttonClickSound != null) PlaySound(UITheme.Instance.buttonClickSound);
                TriggerPreviewFlash();

                if (usedSlot >= 0 && usedSlot < slotViews.Count)
                {
                    slotViews[usedSlot].PlayUsePopAnimation();
                }

                // If stack was depleted, items after it shifted forward (compaction)
                if (wasLastInStack)
                {
                    int occupiedCount = service.GetOccupiedSlotCount();
                    if (occupiedCount == 0)
                    {
                        selectedIndex = -1;
                    }
                    else if (usedSlot < occupiedCount)
                    {
                        // Select the item that slid into that index
                        selectedIndex = usedSlot;
                    }
                    else
                    {
                        // Last item was removed; select the previous index
                        selectedIndex = occupiedCount - 1;
                    }
                }

                RefreshUI();
            }
            else
            {
                UpdateDetailsAndPreview();
                if (!string.IsNullOrEmpty(message))
                {
                    controller?.ShowFeedbackToast(message);
                }
            }
        }

        private IEnumerator UseCooldownRoutine(float duration)
        {
            SetUseButtonState(false);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            useCooldownRoutine = null;
            UpdateDetailsAndPreview();
        }

        public void ShowToast(string message, float duration = 2.0f)
        {
            if (string.IsNullOrEmpty(message)) return;
            LastToastMessage = message;
            EnsureToastSetup();
            if (toastGroup == null || toastText == null) return;

            gameObject.SetActive(true);
            toastGroup.gameObject.SetActive(true);
            toastGroup.transform.SetAsLastSibling();
            toastText.text = message;

            if (toastRoutine != null) StopCoroutine(toastRoutine);
            toastRoutine = StartCoroutine(ToastRoutine(duration));
        }

        private IEnumerator ToastRoutine(float duration)
        {
            toastGroup.alpha = 1f;
            float hold = Mathf.Max(0.2f, duration - 0.25f);
            float t = 0f;
            while (t < hold)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            float fade = 0.25f;
            t = 0f;
            while (t < fade)
            {
                t += Time.unscaledDeltaTime;
                toastGroup.alpha = Mathf.Lerp(1f, 0f, t / fade);
                yield return null;
            }
            toastGroup.alpha = 0f;
            toastRoutine = null;
        }

        private void TriggerPreviewFlash()
        {
            if (previewFlashGroup == null || !isActiveAndEnabled) return;
            if (useFlashRoutine != null) StopCoroutine(useFlashRoutine);
            useFlashRoutine = StartCoroutine(PreviewFlashRoutine());
        }

        private IEnumerator PreviewFlashRoutine()
        {
            previewFlashGroup.alpha = 0.8f;
            float duration = 0.20f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                previewFlashGroup.alpha = Mathf.Lerp(0.8f, 0f, elapsed / duration);
                yield return null;
            }
            previewFlashGroup.alpha = 0f;
            useFlashRoutine = null;
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip == null) return;
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayUI(clip);
            }
            else if (audioSource != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }
    }
}
