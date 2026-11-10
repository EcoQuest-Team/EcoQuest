using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace EcoQuest.Inventory
{
    /// <summary>
    /// Individual item card in the Eco Shop grid.
    /// Matched to the user's reference mockup:
    /// - 3 States: Available (green BUY), Locked (padlock, "--", disabled BUY), CannotAfford (soft red price).
    /// - Hover: Card lifts slightly (scale 1.04x) with soft light border.
    /// - Pressed BUY: Quick squash animation.
    /// - Locked click: Slight shake animation and toast ("Locked for now!").
    /// </summary>
    public class UI_EcoShopSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("UI Elements")]
        [SerializeField] private RectTransform cardRect;
        [SerializeField] private Image cardFrame;
        [SerializeField] private Image itemIcon;
        [SerializeField] private Image coinIcon;
        [SerializeField] private TextMeshProUGUI priceText;
        [SerializeField] private Button buyButton;
        [SerializeField] private Image buyButtonBg;
        [SerializeField] private TextMeshProUGUI buyButtonText;
        [SerializeField] private Outline buyButtonOutline;
        [SerializeField] private Outline cardOutline;

        [Header("Visual Sprites")]
        [SerializeField] private Sprite cardNormalSprite;
        [SerializeField] private Sprite cardHoverSprite;
        [SerializeField] private Sprite btnAvailableSprite;
        [SerializeField] private Sprite btnDisabledSprite;

        private InventoryItemDef itemDef;
        private bool isLocked = false;
        private bool cannotAfford = false;
        private Action<InventoryItemDef> onBuyCallback;
        private Action onLockedClickCallback;
        private Coroutine animRoutine;
        private Vector3 initialScale = Vector3.one;
        private Vector2 initialPos = Vector2.zero;

        public InventoryItemDef ItemDef => itemDef;
        public bool IsLocked => isLocked;
        public Button BuyButton => buyButton;

        private void Awake()
        {
            if (cardRect == null) cardRect = GetComponent<RectTransform>();
            if (cardRect != null) initialScale = cardRect.localScale;
        }

        public void Setup(
            InventoryItemDef def,
            bool locked,
            Sprite lockSprite,
            Sprite coinSprite,
            Action<InventoryItemDef> buyCallback,
            Action lockedCallback = null,
            int playerCoins = int.MaxValue)
        {
            itemDef = def;
            isLocked = locked;
            onBuyCallback = buyCallback;
            onLockedClickCallback = lockedCallback;

            if (cardRect == null) cardRect = GetComponent<RectTransform>();
            if (cardRect != null)
            {
                cardRect.localScale = Vector3.one;
                cardRect.anchoredPosition = initialPos;
            }

            if (buyButton != null)
            {
                buyButton.onClick.RemoveAllListeners();
                if (!isLocked && def != null)
                {
                    buyButton.onClick.AddListener(OnBuyPressed);
                }
            }

            cannotAfford = (!isLocked && def != null && playerCoins < def.price);

            if (isLocked || def == null)
            {
                // Locked State
                if (itemIcon != null)
                {
                    itemIcon.sprite = lockSprite;
                    itemIcon.enabled = lockSprite != null;
                    itemIcon.color = Color.white;
                }
                if (priceText != null)
                {
                    priceText.text = "--";
                    priceText.color = new Color(0.70f, 0.70f, 0.70f, 0.75f);
                }
                if (buyButton != null)
                {
                    buyButton.interactable = false;
                }
                if (buyButtonBg != null)
                {
                    if (btnDisabledSprite != null) buyButtonBg.sprite = btnDisabledSprite;
                    buyButtonBg.color = new Color(0.24f, 0.29f, 0.27f, 0.90f);
                }
                if (buyButtonText != null)
                {
                    buyButtonText.text = "BUY";
                    buyButtonText.color = new Color(0.83f, 0.87f, 0.85f, 0.65f);
                }
                if (buyButtonOutline != null)
                {
                    buyButtonOutline.effectColor = new Color(0.43f, 0.49f, 0.46f, 0.60f);
                }
            }
            else
            {
                // Available State
                if (itemIcon != null)
                {
                    itemIcon.sprite = def.icon;
                    itemIcon.enabled = def.icon != null;
                    itemIcon.color = Color.white;
                }
                if (priceText != null)
                {
                    priceText.text = def.price.ToString();
                    priceText.color = cannotAfford ? new Color(0.92f, 0.42f, 0.42f, 1f) : new Color(1.0f, 0.94f, 0.88f, 1f);
                }
                if (buyButton != null)
                {
                    buyButton.interactable = true;
                }
                if (buyButtonBg != null)
                {
                    if (btnAvailableSprite != null) buyButtonBg.sprite = btnAvailableSprite;
                    buyButtonBg.color = new Color(0.12f, 0.42f, 0.20f, 1f);
                }
                if (buyButtonText != null)
                {
                    buyButtonText.text = "BUY";
                    buyButtonText.color = Color.white;
                }
                if (buyButtonOutline != null)
                {
                    buyButtonOutline.effectColor = new Color(0.30f, 0.82f, 0.42f, 1f);
                }
            }

            if (coinIcon != null && coinSprite != null)
            {
                coinIcon.sprite = coinSprite;
            }

            SetHoverVisual(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            SetHoverVisual(true);
            if (animRoutine != null) StopCoroutine(animRoutine);
            animRoutine = StartCoroutine(ScaleRoutine(1.04f, 0.10f));
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetHoverVisual(false);
            if (animRoutine != null) StopCoroutine(animRoutine);
            animRoutine = StartCoroutine(ScaleRoutine(1.0f, 0.10f));
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (isLocked)
            {
                if (animRoutine != null) StopCoroutine(animRoutine);
                animRoutine = StartCoroutine(ShakeRoutine(0.20f, 5f));
                onLockedClickCallback?.Invoke();
            }
        }

        private void OnBuyPressed()
        {
            if (isLocked || itemDef == null) return;
            if (animRoutine != null) StopCoroutine(animRoutine);
            animRoutine = StartCoroutine(SquashRoutine(0.94f, 0.08f));
            onBuyCallback?.Invoke(itemDef);
        }

        private void SetHoverVisual(bool isHover)
        {
            if (cardFrame != null)
            {
                if (isHover && cardHoverSprite != null) cardFrame.sprite = cardHoverSprite;
                else if (cardNormalSprite != null) cardFrame.sprite = cardNormalSprite;
            }
            if (cardOutline != null)
            {
                cardOutline.effectColor = isHover
                    ? new Color(0.62f, 0.72f, 0.67f, 0.95f)
                    : new Color(0.42f, 0.45f, 0.44f, 0.80f);
            }
        }

        private IEnumerator ScaleRoutine(float targetScale, float duration)
        {
            if (cardRect == null) yield break;
            Vector3 start = cardRect.localScale;
            Vector3 target = Vector3.one * targetScale;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                cardRect.localScale = Vector3.Lerp(start, target, t);
                yield return null;
            }
            cardRect.localScale = target;
        }

        private IEnumerator SquashRoutine(float squashScale, float duration)
        {
            if (cardRect == null) yield break;
            yield return ScaleRoutine(squashScale, duration * 0.5f);
            yield return ScaleRoutine(1.04f, duration * 0.5f);
        }

        private IEnumerator ShakeRoutine(float duration, float magnitude)
        {
            if (cardRect == null) yield break;
            Vector2 origin = initialPos;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float offset = Mathf.Sin(elapsed * 40f) * magnitude * (1f - (elapsed / duration));
                cardRect.anchoredPosition = new Vector2(origin.x + offset, origin.y);
                yield return null;
            }
            cardRect.anchoredPosition = origin;
        }

        public void SetupReferences(
            Image frame, Image icon, Image coin, TextMeshProUGUI price,
            Button btn, Image btnBg, TextMeshProUGUI btnText, Outline btnOutline,
            Outline crdOutline = null, Sprite normalSp = null, Sprite hoverSp = null,
            Sprite btnAvail = null, Sprite btnDis = null)
        {
            cardFrame = frame;
            itemIcon = icon;
            coinIcon = coin;
            priceText = price;
            buyButton = btn;
            buyButtonBg = btnBg;
            buyButtonText = btnText;
            buyButtonOutline = btnOutline;
            cardOutline = crdOutline;
            cardNormalSprite = normalSp;
            cardHoverSprite = hoverSp;
            btnAvailableSprite = btnAvail;
            btnDisabledSprite = btnDis;
            cardRect = GetComponent<RectTransform>();
            if (cardRect != null) initialPos = cardRect.anchoredPosition;
        }
    }
}