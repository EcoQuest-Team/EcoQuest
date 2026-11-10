using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace EcoQuest.Inventory
{
    /// <summary>
    /// Persistent HUD bag button positioned at bottom-right (96x96, anchored bottom-right at -32, 32).
    /// Features clean transparent backpack artwork, [B] keycap prompt, unread notification dot,
    /// pop-on-add animation, and seamless HUD profile fading.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UI_BagButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        [Header("Plate & Artwork")]
        [SerializeField] private RectTransform rectTransform;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image plateImage;
        [SerializeField] private Image backpackIcon;
        [SerializeField] private Image unreadDotImage;
        [SerializeField] private GameObject keycapGo;
        [SerializeField] private TextMeshProUGUI keycapText;

        [Header("Animation Curves")]
        [SerializeField] private float hoverScale = 1.08f;
        [SerializeField] private float pressedScale = 0.94f;
        [SerializeField] private float normalScale = 1.0f;

        private InventoryController controller;
        private InventoryService service;
        private Coroutine popRoutine;
        private bool isHovered = false;
        private bool isPressed = false;

        public void Initialize(InventoryController ctrl, InventoryService srv)
        {
            controller = ctrl;
            service = srv;

            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

            if (service != null)
            {
                service.OnItemAdded += OnItemAdded;
                service.OnInventoryChanged += UpdateUnreadState;
            }

            UpdateUnreadState();
        }

        private void OnDestroy()
        {
            if (service != null)
            {
                service.OnItemAdded -= OnItemAdded;
                service.OnInventoryChanged -= UpdateUnreadState;
            }
        }

        private void Update()
        {
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (canvasGroup == null) return;

            // Hide in Main Menu
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("MainMenu"))
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
                return;
            }

            // Hide if tutorial not complete
            if (!InventoryController.IsTutorialCompleted)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
                return;
            }

            // Hide during modals, dialogues, travel, or overlays
            bool hide = false;
            if (controller != null && controller.IsOpen) hide = true;
            if (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalOpen) hide = true;
            if (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue) hide = true;
            if (LitterbugQuestManager.IsDialogueOpen) hide = true;
            if (SceneTransition.Instance != null && SceneTransition.Instance.IsTransitioning) hide = true;
            if (UIOrchestrator.Instance != null && (UIOrchestrator.Instance.AreModalsOpen || UIOrchestrator.Instance.IsOverlayActive)) hide = true;
            if (HudVisibilityService.Instance != null && HudVisibilityService.Instance.IsHUDHidden) hide = true;
            if (EcoQuest.Shared.DialogueCameraRig.IsDialogueCameraActive) hide = true;
            if (NPCInteraction.IsAnyGuardianFlowRunning) hide = true;

            float targetAlpha = hide ? 0f : 1f;
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, Time.unscaledDeltaTime * 6f);
            canvasGroup.interactable = !hide;
            canvasGroup.blocksRaycasts = !hide;
        }

        public void UpdateUnreadState()
        {
            if (unreadDotImage != null && service != null)
            {
                unreadDotImage.enabled = service.HasUnseenNewItems;
            }
        }

        private void OnItemAdded(InventoryItemDef item, int amount)
        {
            UpdateUnreadState();
            if (popRoutine != null) StopCoroutine(popRoutine);
            popRoutine = StartCoroutine(PopAnimationRoutine());
        }

        private IEnumerator PopAnimationRoutine()
        {
            if (rectTransform == null) yield break;

            float duration = 0.28f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / duration;
                float pop = 1f + 0.25f * Mathf.Sin(t * Mathf.PI);
                rectTransform.localScale = new Vector3(pop, pop, 1f);
                yield return null;
            }

            rectTransform.localScale = Vector3.one;
            popRoutine = null;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovered = true;
            if (!isPressed && rectTransform != null)
            {
                rectTransform.localScale = new Vector3(hoverScale, hoverScale, 1f);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
            if (!isPressed && rectTransform != null)
            {
                rectTransform.localScale = new Vector3(normalScale, normalScale, 1f);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            isPressed = true;
            if (rectTransform != null)
            {
                rectTransform.localScale = new Vector3(pressedScale, pressedScale, 1f);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            isPressed = false;
            if (rectTransform != null)
            {
                float target = isHovered ? hoverScale : normalScale;
                rectTransform.localScale = new Vector3(target, target, 1f);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            controller?.ToggleInventory();
        }
    }
}
