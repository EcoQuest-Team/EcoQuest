using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace EcoQuest.Inventory
{
    /// <summary>
    /// Individual item card in the Eco Shop 2x4 grid.
    /// Displays item icon, coin icon + price, and an interactive BUY button.
    /// Supports locked slot state with padlock and disabled BUY button.
    /// </summary>
    public class UI_EcoShopSlot : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private Image cardFrame;
        [SerializeField] private Image itemIcon;
        [SerializeField] private Image coinIcon;
        [SerializeField] private TextMeshProUGUI priceText;
        [SerializeField] private Button buyButton;
        [SerializeField] private Image buyButtonBg;
        [SerializeField] private TextMeshProUGUI buyButtonText;
        [SerializeField] private Outline buyButtonOutline;

        private InventoryItemDef itemDef;
        private bool isLocked = false;
        private Action<InventoryItemDef> onBuyCallback;

        public InventoryItemDef ItemDef => itemDef;
        public bool IsLocked => isLocked;
        public Button BuyButton => buyButton;

        public void Setup(InventoryItemDef def, bool locked, Sprite lockSprite, Sprite coinSprite, Action<InventoryItemDef> buyCallback)
        {
            itemDef = def;
            isLocked = locked;
            onBuyCallback = buyCallback;

            if (buyButton != null)
            {
                buyButton.onClick.RemoveAllListeners();
                if (!isLocked && def != null)
                {
                    buyButton.onClick.AddListener(OnBuyPressed);
                }
            }

            if (isLocked || def == null)
            {
                if (itemIcon != null)
                {
                    itemIcon.sprite = lockSprite;
                    itemIcon.enabled = lockSprite != null;
                    itemIcon.color = new Color(0.85f, 0.85f, 0.85f, 0.95f);
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
                    buyButtonBg.color = new Color(0.20f, 0.28f, 0.24f, 0.65f);
                }
                if (buyButtonText != null)
                {
                    buyButtonText.color = new Color(0.55f, 0.62f, 0.58f, 0.70f);
                }
                if (buyButtonOutline != null)
                {
                    buyButtonOutline.effectColor = new Color(0.35f, 0.45f, 0.40f, 0.45f);
                }
            }
            else
            {
                if (itemIcon != null)
                {
                    itemIcon.sprite = def.icon;
                    itemIcon.enabled = def.icon != null;
                    itemIcon.color = Color.white;
                }
                if (priceText != null)
                {
                    priceText.text = def.price.ToString();
                    priceText.color = new Color(1.0f, 0.92f, 0.55f, 1f);
                }
                if (buyButton != null)
                {
                    buyButton.interactable = true;
                }
                if (buyButtonBg != null)
                {
                    buyButtonBg.color = new Color(0.18f, 0.48f, 0.32f, 1f);
                }
                if (buyButtonText != null)
                {
                    buyButtonText.color = Color.white;
                }
                if (buyButtonOutline != null)
                {
                    buyButtonOutline.effectColor = new Color(0.42f, 0.78f, 0.52f, 1f);
                }
            }

            if (coinIcon != null && coinSprite != null)
            {
                coinIcon.sprite = coinSprite;
            }
        }

        private void OnBuyPressed()
        {
            if (isLocked || itemDef == null) return;
            onBuyCallback?.Invoke(itemDef);
        }

        public void SetupReferences(Image frame, Image icon, Image coin, TextMeshProUGUI price, Button btn, Image btnBg, TextMeshProUGUI btnTxt, Outline btnOutl)
        {
            cardFrame = frame;
            itemIcon = icon;
            coinIcon = coin;
            priceText = price;
            buyButton = btn;
            buyButtonBg = btnBg;
            buyButtonText = btnTxt;
            buyButtonOutline = btnOutl;
        }
    }
}
