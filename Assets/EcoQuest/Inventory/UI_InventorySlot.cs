using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace EcoQuest.Inventory
{
    /// <summary>
    /// Individual UI slot within the 3-column inventory grid.
    /// Handles normal, hover, and selected visual states, click & double-click selection,
    /// faint empty outline rendering, and use pop animation.
    /// </summary>
    public class UI_InventorySlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("UI Elements")]
        [SerializeField] private Image slotFrameImage;
        [SerializeField] private Image itemIconImage;
        [SerializeField] private TextMeshProUGUI countText;
        [SerializeField] private TextMeshProUGUI tierDotsText;
        [SerializeField] private RectTransform rectTransform;

        [Header("Sprites")]
        [SerializeField] private Sprite normalSprite;
        [SerializeField] private Sprite hoverSprite;
        [SerializeField] private Sprite selectedSprite;

        private int slotIndex = -1;
        private bool isSelected = false;
        private bool isHovered = false;
        private bool hasItem = false;
        private int itemTier = 1;
        private Action<int> onSlotClicked;
        private Action<int> onSlotDoubleClicked;
        private float lastClickTime = -10f;
        private Coroutine popRoutine;

        public int SlotIndex => slotIndex;
        public bool IsSelected => isSelected;
        public bool HasItem => hasItem;
        public int ItemTier => itemTier;

        public void Setup(int index, Sprite norm, Sprite hov, Sprite sel, Action<int> clickCallback, Action<int> doubleClickCallback = null)
        {
            slotIndex = index;
            normalSprite = norm;
            hoverSprite = hov;
            selectedSprite = sel;
            onSlotClicked = clickCallback;
            onSlotDoubleClicked = doubleClickCallback;
            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
            UpdateVisualState();
        }

        public void Bind(InventorySlotData data, InventoryItemDef def, bool selected)
        {
            isSelected = selected;
            hasItem = (data != null && !data.IsEmpty && def != null);
            itemTier = (hasItem && def != null) ? (def.tier > 0 ? def.tier : 1) : 1;

            if (hasItem)
            {
                if (itemIconImage != null)
                {
                    itemIconImage.sprite = def.icon;
                    itemIconImage.enabled = def.icon != null;
                }
                if (countText != null)
                {
                    countText.text = data.count > 1 ? data.count.ToString() : string.Empty;
                    countText.enabled = data.count > 1;
                }
                EnsureTierDotsText();
                if (tierDotsText != null)
                {
                    tierDotsText.text = new string('•', Mathf.Clamp(itemTier, 1, 3));
                    tierDotsText.color = itemTier == 3
                        ? new Color(1.0f, 0.88f, 0.40f, 0.95f)
                        : (itemTier == 2 ? new Color(0.55f, 0.95f, 0.70f, 0.95f) : new Color(0.92f, 0.95f, 0.92f, 0.85f));
                    tierDotsText.enabled = true;
                }
            }
            else
            {
                if (itemIconImage != null) itemIconImage.enabled = false;
                if (countText != null) countText.enabled = false;
                if (tierDotsText != null) tierDotsText.enabled = false;
            }

            UpdateVisualState();
        }

        private void EnsureTierDotsText()
        {
            if (tierDotsText != null) return;
            var dotsGo = new GameObject("TierDots", typeof(RectTransform), typeof(TextMeshProUGUI));
            dotsGo.transform.SetParent(transform, false);
            var drt = dotsGo.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0.08f, 0.68f);
            drt.anchorMax = new Vector2(0.55f, 0.95f);
            drt.offsetMin = Vector2.zero;
            drt.offsetMax = Vector2.zero;
            tierDotsText = dotsGo.GetComponent<TextMeshProUGUI>();
            tierDotsText.fontSize = 20f;
            tierDotsText.fontStyle = FontStyles.Bold;
            tierDotsText.alignment = TextAlignmentOptions.TopLeft;
            tierDotsText.color = new Color(0.92f, 0.95f, 0.92f, 0.90f);
            tierDotsText.raycastTarget = false;
        }

        public void SetSelected(bool selected)
        {
            if (isSelected != selected)
            {
                isSelected = selected;
                UpdateVisualState();
            }
        }

        public void PlayUsePopAnimation()
        {
            if (!isActiveAndEnabled) return;
            if (popRoutine != null) StopCoroutine(popRoutine);
            popRoutine = StartCoroutine(UsePopRoutine());
        }

        private IEnumerator UsePopRoutine()
        {
            Transform target = (itemIconImage != null && itemIconImage.enabled) ? itemIconImage.transform : transform;
            float duration = 0.16f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float s = 1f + 0.18f * Mathf.Sin(t * Mathf.PI);
                target.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            target.localScale = Vector3.one;
            popRoutine = null;
            UpdateVisualState();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovered = true;
            UpdateVisualState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
            UpdateVisualState();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            float now = Time.unscaledTime;
            bool isDouble = (eventData != null && eventData.clickCount >= 2) || (now - lastClickTime <= 0.30f);
            lastClickTime = now;

            onSlotClicked?.Invoke(slotIndex);

            if (isDouble && hasItem)
            {
                lastClickTime = -10f;
                onSlotDoubleClicked?.Invoke(slotIndex);
            }
        }

        private void UpdateVisualState()
        {
            if (slotFrameImage == null) return;

            if (isSelected)
            {
                if (selectedSprite != null) slotFrameImage.sprite = selectedSprite;
                slotFrameImage.color = Color.white;
                if (rectTransform != null && popRoutine == null) rectTransform.localScale = Vector3.one;
            }
            else if (isHovered)
            {
                if (hoverSprite != null) slotFrameImage.sprite = hoverSprite;
                slotFrameImage.color = hasItem ? Color.white : new Color(1f, 1f, 1f, 0.75f);
                if (rectTransform != null && popRoutine == null) rectTransform.localScale = Vector3.one;
            }
            else
            {
                if (normalSprite != null) slotFrameImage.sprite = normalSprite;
                // Empty slots drawn as faint outlines; Tier 2/3 draw subtle green/gold border
                if (hasItem)
                {
                    if (itemTier == 3)
                        slotFrameImage.color = new Color(1.0f, 0.88f, 0.40f, 0.95f);
                    else if (itemTier == 2)
                        slotFrameImage.color = new Color(0.45f, 0.95f, 0.65f, 0.95f);
                    else
                        slotFrameImage.color = new Color(1f, 1f, 1f, 0.92f);
                }
                else
                {
                    slotFrameImage.color = new Color(1f, 1f, 1f, 0.45f);
                }
                if (rectTransform != null && popRoutine == null) rectTransform.localScale = Vector3.one;
            }
        }
    }
}
