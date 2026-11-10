using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using EcoQuest.Inventory;

namespace EcoQuest.Inventory.Editor
{
    public static class ShopPreviewAndValidationTools
    {
        [MenuItem("EcoQuest/Shop/Preview Reference State", false, 100)]
        public static void PreviewReferenceState()
        {
            var shop = UnityEngine.Object.FindFirstObjectByType<UI_EcoShop>(FindObjectsInactive.Include);
            if (shop == null)
            {
                Debug.LogError("[ShopPreview] UI_EcoShop instance not found in scene!");
                return;
            }

            // Force enable shop GameObject and Panel
            shop.gameObject.SetActive(true);
            var panelTransform = shop.transform.Find("ShopPanel");
            if (panelTransform != null)
            {
                panelTransform.gameObject.SetActive(true);
                var cg = panelTransform.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
            }

            var backdropTransform = shop.transform.Find("Backdrop");
            if (backdropTransform != null)
            {
                backdropTransform.gameObject.SetActive(true);
                var bcg = backdropTransform.GetComponent<CanvasGroup>();
                if (bcg != null) bcg.alpha = 1f;
            }

            // Configure all 16 slots to Locked state (matching reference mockup Image 1)
            var padlockSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/EcoQuest/Inventory/Sprites/UI/icon_padlock_mossy.png");
            if (padlockSprite == null) padlockSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/EcoQuest/Inventory/Sprites/icon_padlock.png");
            var coinSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/EcoQuest/Inventory/Sprites/UI/icon_shop_coin.png");

            foreach (var slot in shop.SlotViews)
            {
                if (slot != null)
                {
                    slot.Setup(null, true, padlockSprite, coinSprite, null, null, 945);
                }
            }

            // Configure coin pill
            var pillText = shop.transform.Find("CoinCounterPill/AmountText")?.GetComponent<TextMeshProUGUI>();
            if (pillText != null) pillText.text = "945";

            // Configure HUD Reference State
            var hud = UnityEngine.Object.FindFirstObjectByType<UI_SurvivalStatsHUD>(FindObjectsInactive.Include);
            if (hud != null)
            {
                hud.SetPreviewReferenceState(true);
            }

            var scrollContent = shop.transform.Find("ShopPanel/ContentWell/ScrollRect/Viewport/Content")?.GetComponent<RectTransform>();
            if (scrollContent != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
            }

            Canvas.ForceUpdateCanvases();
            SceneView.RepaintAll();
            Debug.Log("[ShopPreview] Reference State Preview enabled! (All 16 slots locked, 945 coins, HUD buffs active)");
        }

        [MenuItem("EcoQuest/Shop/Validate Shop UI", false, 101)]
        public static void ValidateShopUI()
        {
            var shop = UnityEngine.Object.FindFirstObjectByType<UI_EcoShop>(FindObjectsInactive.Include);
            if (shop == null)
            {
                Debug.LogError("[ShopValidator] FAIL: UI_EcoShop not found in scene!");
                return;
            }

            bool allPass = true;
            Debug.Log("=== ECO SHOP UI VALIDATION ===");

            // 1. Canvas
            var canvas = shop.GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.sortingOrder == 92)
            {
                Debug.Log("[PASS] Canvas: ScreenSpaceOverlay, SortingOrder 92");
            }
            else
            {
                Debug.LogError($"[FAIL] Canvas settings incorrect: mode={canvas?.renderMode}, order={canvas?.sortingOrder}");
                allPass = false;
            }

            // 2. Main Panel
            var panel = shop.transform.Find("ShopPanel")?.GetComponent<RectTransform>();
            if (panel != null && Mathf.Approximately(panel.sizeDelta.x, 1226f) && Mathf.Approximately(panel.sizeDelta.y, 834f))
            {
                Debug.Log($"[PASS] Panel RectTransform: size {panel.sizeDelta.x}x{panel.sizeDelta.y}");
            }
            else
            {
                Debug.LogWarning($"[WARN] Panel size: {panel?.sizeDelta} (expected 1226x834)");
            }

            // 3. ScrollRect & Viewport Mask
            var sr = shop.GetComponentInChildren<ScrollRect>(true);
            var mask = sr != null && sr.viewport != null ? sr.viewport.GetComponent<RectMask2D>() : null;
            if (sr != null && mask != null && sr.movementType == ScrollRect.MovementType.Clamped)
            {
                Debug.Log("[PASS] ScrollRect: Clamped movement, RectMask2D attached to Viewport");
            }
            else
            {
                Debug.LogError("[FAIL] ScrollRect or RectMask2D missing or unclamped!");
                allPass = false;
            }

            // 4. Scrollbar
            var sb = shop.GetComponentInChildren<Scrollbar>(true);
            if (sb != null && sb.handleRect != null && sb.targetGraphic != null)
            {
                Debug.Log("[PASS] Scrollbar: Functional with track and glowing handle");
            }
            else
            {
                Debug.LogError("[FAIL] Scrollbar missing or incomplete!");
                allPass = false;
            }

            // 5. Card Count
            if (shop.SlotViews != null && shop.SlotViews.Count >= 16)
            {
                Debug.Log($"[PASS] Card Slots: {shop.SlotViews.Count} pooled slots configured");
            }
            else
            {
                Debug.LogError($"[FAIL] Card slots count: {shop.SlotViews?.Count ?? 0} (expected >= 16)");
                allPass = false;
            }

            // 6. Coin Pill
            var pill = shop.transform.Find("CoinCounterPill");
            if (pill != null)
            {
                Debug.Log("[PASS] Coin Counter Pill: Present");
            }
            else
            {
                Debug.LogError("[FAIL] Coin Counter Pill missing!");
                allPass = false;
            }

            if (allPass)
            {
                Debug.Log("=== VALIDATION RESULT: ALL TESTS PASSED ===");
            }
            else
            {
                Debug.LogError("=== VALIDATION RESULT: SOME CHECKS FAILED ===");
            }
        }
    }
}
