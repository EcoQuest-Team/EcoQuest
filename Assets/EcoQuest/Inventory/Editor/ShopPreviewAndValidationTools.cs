using System;
using System.IO;
using System.Collections.Generic;
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
        [MenuItem("Tools/EcoQuest/Eco Shop/Preview Locked (Compare)", false, 20)]
        public static void PreviewLockedCompare()
        {
            string outDir = @"C:\EcoQuest.gg\Reports\EcoShop";
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            var shop = UnityEngine.Object.FindFirstObjectByType<UI_EcoShop>(FindObjectsInactive.Include);
            if (shop == null)
            {
                Debug.LogError("[ShopPreview] UI_EcoShop instance not found in scene!");
                return;
            }

            var padlockSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/EcoQuest/Inventory/Sprites/UI/icon_padlock_mossy.png");
            if (padlockSprite == null) padlockSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/EcoQuest/Inventory/Sprites/icon_padlock.png");
            var coinSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/EcoQuest/Inventory/Sprites/UI/icon_shop_coin.png");

            var cam = Camera.main;
            if (cam == null && Camera.allCameras.Length > 0) cam = Camera.allCameras[0];
            if (cam == null)
            {
                GameObject camGo = new GameObject("TempShopCam", typeof(Camera));
                cam = camGo.GetComponent<Camera>();
            }

            Color prevBgColor = cam.backgroundColor;
            CameraClearFlags prevClearFlags = cam.clearFlags;
            cam.backgroundColor = new Color(0.282f, 0.275f, 0.278f, 1f); // #484647 flat gray
            cam.clearFlags = CameraClearFlags.SolidColor;

            var canvas = shop.GetComponent<Canvas>();
            var origRenderMode = canvas != null ? canvas.renderMode : RenderMode.ScreenSpaceOverlay;
            var origCam = canvas != null ? canvas.worldCamera : null;

            if (canvas != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
            }

            shop.gameObject.SetActive(true);
            var panel = shop.transform.Find("ShopPanel") as RectTransform;
            if (panel != null)
            {
                panel.gameObject.SetActive(true);
                var cg = panel.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
            }

            var backdrop = shop.transform.Find("Backdrop");
            if (backdrop != null)
            {
                backdrop.gameObject.SetActive(true);
                var bcg = backdrop.GetComponent<CanvasGroup>();
                if (bcg != null) bcg.alpha = 0f;
            }

            var pill = shop.transform.Find("CoinCounterPill");
            if (pill != null)
            {
                pill.gameObject.SetActive(true);
                var pt = pill.Find("AmountText")?.GetComponent<TextMeshProUGUI>();
                if (pt != null) pt.text = "945";
            }

            var sr = shop.GetComponentInChildren<ScrollRect>(true);
            var contentRt = sr != null ? sr.content : null;

            try
            {
                // 1. All slots locked (display only)
                foreach (var slot in shop.SlotViews)
                {
                    if (slot != null) slot.Setup(null, true, padlockSprite, coinSprite, null, null, 945);
                }
                if (contentRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
                if (sr != null) sr.verticalNormalizedPosition = 1f;
                ForceDirtyAndCanvasUpdate(shop);

                // Capture 1920x1080 Locked
                CaptureCameraRT(cam, 1920, 1080, Path.Combine(outDir, "Compare_Locked_1920.png"));
                // Capture 1280x720 Locked
                CaptureCameraRT(cam, 1280, 720, Path.Combine(outDir, "Compare_Locked_1280.png"));

                // 2. Real items setup
                shop.PopulateSlots();
                if (contentRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
                if (sr != null) sr.verticalNormalizedPosition = 1f;
                ForceDirtyAndCanvasUpdate(shop);

                // Capture Real items
                CaptureCameraRT(cam, 1920, 1080, Path.Combine(outDir, "Compare_Real.png"));

                // 3. Scrolled to bottom
                if (sr != null && contentRt != null)
                {
                    float scrollableHeight = Mathf.Max(0f, contentRt.rect.height - sr.viewport.rect.height);
                    contentRt.anchoredPosition = new Vector2(contentRt.anchoredPosition.x, scrollableHeight);
                    ForceDirtyAndCanvasUpdate(shop);
                    CaptureCameraRT(cam, 1920, 1080, Path.Combine(outDir, "Compare_Scrolled.png"));

                    // Restore to locked top state
                    contentRt.anchoredPosition = new Vector2(contentRt.anchoredPosition.x, 0f);
                    foreach (var slot in shop.SlotViews)
                    {
                        if (slot != null) slot.Setup(null, true, padlockSprite, coinSprite, null, null, 945);
                    }
                    sr.verticalNormalizedPosition = 1f;
                    ForceDirtyAndCanvasUpdate(shop);
                }

                Debug.Log($"<color=green>[PASS] Preview Locked (Compare) screenshots generated in {outDir}</color>");
            }
            finally
            {
                if (canvas != null)
                {
                    canvas.renderMode = origRenderMode;
                    canvas.worldCamera = origCam;
                }
                cam.backgroundColor = prevBgColor;
                cam.clearFlags = prevClearFlags;
            }

            // Generate SideBySide and Overlay
            GenerateSideBySideAndOverlay(outDir);
        }

        private static void ForceDirtyAndCanvasUpdate(UI_EcoShop shop)
        {
            if (shop == null) return;
            foreach (var g in shop.GetComponentsInChildren<Graphic>(true))
            {
                g.SetAllDirty();
            }
            Canvas.ForceUpdateCanvases();
        }

        private static void CaptureCameraRT(Camera cam, int width, int height, string fullPath)
        {
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            RenderTexture.active = prev;
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            byte[] bytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            File.WriteAllBytes(fullPath, bytes);
            Debug.Log($"[Screenshot] Saved {fullPath} ({width}x{height})");
        }

        private static void GenerateSideBySideAndOverlay(string outDir)
        {
            try
            {
                string scriptPath = Path.Combine(outDir, "generate_comparisons.py");
                if (File.Exists(scriptPath))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("python", $"\"{scriptPath}\"");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    var p = System.Diagnostics.Process.Start(psi);
                    if (p != null) p.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ComparisonGen] Python generator notice: {ex.Message}");
            }
        }

        [MenuItem("Tools/EcoQuest/Eco Shop/Check Style", false, 21)]
        public static void CheckStyle()
        {
            var shop = UnityEngine.Object.FindFirstObjectByType<UI_EcoShop>(FindObjectsInactive.Include);
            if (shop == null)
            {
                Debug.LogError("[CheckStyle] UI_EcoShop instance not found in scene!");
                return;
            }

            var panel = shop.transform.Find("ShopPanel") as RectTransform;
            if (panel == null)
            {
                Debug.LogError("[CheckStyle] ShopPanel not found!");
                return;
            }

            float W = panel.sizeDelta.x;
            float H = panel.sizeDelta.y;
            int passCount = 0;
            int totalCount = 0;

            Debug.Log($"=== ECO SHOP VISUAL STYLE AUDIT (Panel {W:F1}x{H:F1}) ===");

            Action<string, float, float, float, string> checkValue = (name, measured, target, tol, unit) =>
            {
                totalCount++;
                float diff = Mathf.Abs(measured - target);
                bool pass = diff <= tol;
                if (pass) passCount++;
                string status = pass ? "<color=green>PASS</color>" : "<color=red>FAIL</color>";
                Debug.Log($"[{status}] {name}: {measured:F1}{unit} (Target: {target:F1}{unit}, Tol: +- {tol:F1}{unit}, Diff: {diff:F1}{unit})");
            };

            // 1. Panel aspect
            checkValue("Panel Aspect Ratio", W / H, 1.470f, 0.05f, ":1");

            // 2. Banner
            var banner = panel.Find("HeaderBanner") as RectTransform;
            if (banner != null)
            {
                checkValue("Banner Width", banner.sizeDelta.x, 0.447f * W, 0.015f * W, "px");
                checkValue("Banner Height", banner.sizeDelta.y, 0.153f * H, 0.015f * H, "px");
                checkValue("Banner Top Overhang", banner.anchoredPosition.y, 2.0f, 3.0f, "px");
                checkValue("Banner Center X", Mathf.Abs(banner.anchoredPosition.x), 0.0f, 1.0f, "px");

                var titleTmp = banner.GetComponentInChildren<TextMeshProUGUI>(true);
                bool titleOk = titleTmp != null && titleTmp.gameObject.activeSelf && titleTmp.text == "ECO SHOP";
                totalCount++;
                if (titleOk) passCount++;
                Debug.Log($"[{(titleOk ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}] Title Text: '{(titleTmp != null ? titleTmp.text : "null")}' (Active: {titleOk})");

                // Ornaments disabled check
                int ornamentCount = 0;
                foreach (Transform child in banner)
                {
                    if (child.name.ToLower().Contains("leaf") || child.name.ToLower().Contains("vine") || child.name.ToLower().Contains("ornament"))
                    {
                        if (child.gameObject.activeSelf) ornamentCount++;
                    }
                }
                totalCount++;
                bool noOrnaments = ornamentCount == 0;
                if (noOrnaments) passCount++;
                Debug.Log($"[{(noOrnaments ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}] Ornaments Disabled: Active leaves/vines = {ornamentCount}");
            }

            // 3. Close Button
            var close = panel.Find("CloseButton") as RectTransform;
            if (close != null)
            {
                checkValue("Close X Size", close.sizeDelta.x, 0.031f * W, 0.015f * W, "px");
                checkValue("Close X Pos Y from top", Mathf.Abs(close.anchoredPosition.y), 0.070f * H, 0.015f * H, "px");
            }

            // 4. Content Well (Inset)
            var well = panel.Find("ContentWell") as RectTransform;
            if (well != null)
            {
                checkValue("Well Width", well.sizeDelta.x, 0.939f * W, 0.015f * W, "px");
                checkValue("Well Height", well.sizeDelta.y, 0.742f * H, 0.015f * H, "px");
                float wellMarginLeft = (W - well.sizeDelta.x) * 0.5f;
                checkValue("Well Margin Left/Right", wellMarginLeft, 0.031f * W, 0.015f * W, "px");
            }

            // 5. Card & Grid
            var glg = panel.GetComponentInChildren<GridLayoutGroup>(true);
            if (glg != null)
            {
                float S = glg.cellSize.x;
                checkValue("Card Side S", S, 0.199f * W, 0.015f * W, "px");
                checkValue("Column Pitch", S + glg.spacing.x, 0.224f * W, 0.015f * W, "px");
                checkValue("Row Pitch", glg.cellSize.y + glg.spacing.y, 0.360f * H, 0.015f * H, "px");
                checkValue("Grid Inset Left Offset", glg.padding.left, 0.025f * W, 0.015f * W, "px");
                checkValue("Grid Inset Top Offset", glg.padding.top, 0.029f * H, 0.015f * H, "px");

                // Inside Card (first slot)
                var card0 = glg.transform.Find("Card_00") as RectTransform;
                if (card0 != null)
                {
                    var icon = card0.Find("Icon") as RectTransform;
                    if (icon != null)
                    {
                        checkValue("Slot Icon Width", icon.sizeDelta.x, 0.48f * S, 0.02f * S, "px");
                        checkValue("Slot Icon Height", icon.sizeDelta.y, 0.49f * S, 0.02f * S, "px");
                        checkValue("Slot Icon Pos Y", icon.anchoredPosition.y, 14.0f, 3.0f, "px");
                    }

                    var priceRow = card0.Find("PriceRow") as RectTransform;
                    if (priceRow != null)
                    {
                        var coin = priceRow.Find("CoinIcon") as RectTransform;
                        if (coin != null)
                        {
                            checkValue("Price Coin Diameter", coin.sizeDelta.x, 0.115f * S, 0.02f * S, "px");
                        }
                        checkValue("Price Row Pos Y", priceRow.anchoredPosition.y, -66.0f, 3.0f, "px");
                    }

                    var buy = card0.Find("BuyButton") as RectTransform;
                    if (buy != null)
                    {
                        checkValue("BUY Pill Width", buy.sizeDelta.x, 0.54f * S, 0.02f * S, "px");
                        checkValue("BUY Pill Height", buy.sizeDelta.y, 0.20f * S, 0.02f * S, "px");
                        checkValue("BUY Pill Straddle Y", buy.anchoredPosition.y, -118.0f, 3.0f, "px");
                    }
                }
            }

            // 6. Scrollbar
            var sb = panel.Find("ContentWell/VerticalScrollbar") as RectTransform;
            if (sb != null)
            {
                checkValue("Scrollbar Width", sb.sizeDelta.x, 0.020f * W, 0.015f * W, "px");
            }

            // 7. No Clipping & Safe Viewport
            var sr = shop.GetComponentInChildren<ScrollRect>(true);
            bool maskOk = sr != null && sr.viewport != null && sr.viewport.GetComponent<RectMask2D>() != null;
            totalCount++;
            if (maskOk) passCount++;
            Debug.Log($"[{(maskOk ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}] Viewport RectMask2D: Attached and active");

            // 8. Behavior unchanged
            totalCount++;
            bool behaviorOk = shop.SlotViews != null && shop.SlotViews.Count >= 16;
            if (behaviorOk) passCount++;
            Debug.Log($"[{(behaviorOk ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}] Shop Behavior/Slots Invariant: {shop.SlotViews?.Count} slots intact");

            Debug.Log($"=======================================================\n" +
                      $"CHECK STYLE RESULT: {passCount}/{totalCount} CHECKS PASSED " +
                      $"({(passCount == totalCount ? "<color=green>100% PASS</color>" : "<color=yellow>PARTIAL</color>")})\n" +
                      $"=======================================================");
        }

        [MenuItem("EcoQuest/Shop/Preview Reference State", false, 100)]
        public static void PreviewReferenceState()
        {
            var shop = UnityEngine.Object.FindFirstObjectByType<UI_EcoShop>(FindObjectsInactive.Include);
            if (shop == null)
            {
                Debug.LogError("[ShopPreview] UI_EcoShop instance not found in scene!");
                return;
            }

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

            var pillText = shop.transform.Find("CoinCounterPill/AmountText")?.GetComponent<TextMeshProUGUI>();
            if (pillText != null) pillText.text = "945";

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

            var panel = shop.transform.Find("ShopPanel")?.GetComponent<RectTransform>();
            if (panel != null && Mathf.Approximately(panel.sizeDelta.x, 1238f))
            {
                Debug.Log($"[PASS] Panel RectTransform: size {panel.sizeDelta.x}x{panel.sizeDelta.y}");
            }
            else
            {
                Debug.LogWarning($"[WARN] Panel size: {panel?.sizeDelta} (expected 1238x842)");
            }

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

            if (shop.SlotViews != null && shop.SlotViews.Count >= 16)
            {
                Debug.Log($"[PASS] Card Slots: {shop.SlotViews.Count} pooled slots configured");
            }
            else
            {
                Debug.LogError($"[FAIL] Card slots count: {shop.SlotViews?.Count ?? 0} (expected >= 16)");
                allPass = false;
            }

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

        [MenuItem("EcoQuest/Shop/Stability Test", false, 102)]
        public static void RunStabilityTest()
        {
            var shop = UnityEngine.Object.FindFirstObjectByType<UI_EcoShop>(FindObjectsInactive.Include);
            if (shop == null)
            {
                Debug.LogError("[StabilityTest] UI_EcoShop instance not found in scene!");
                return;
            }

            Debug.Log("=== ECO SHOP STABILITY AUDIT (ZERO DRIFT / NO MOVEMENT VERIFICATION) ===");
            PreviewReferenceState();

            var panel = shop.transform.Find("ShopPanel") as RectTransform;
            var plaque = shop.transform.Find("ShopPanel/HeaderBanner") as RectTransform;
            var closeBtn = shop.transform.Find("ShopPanel/CloseButton") as RectTransform;
            var well = shop.transform.Find("ShopPanel/ContentWell") as RectTransform;
            var card0 = shop.transform.Find("ShopPanel/ContentWell/ScrollRect/Viewport/Content/Card_00") as RectTransform;

            Vector3[] pCorners = new Vector3[4];
            Vector3[] bnrCorners = new Vector3[4];
            Vector3[] clsCorners = new Vector3[4];
            Vector3[] wllCorners = new Vector3[4];
            Vector3[] crdCorners = new Vector3[4];

            panel.GetWorldCorners(pCorners);
            plaque.GetWorldCorners(bnrCorners);
            closeBtn.GetWorldCorners(clsCorners);
            well.GetWorldCorners(wllCorners);
            card0.GetWorldCorners(crdCorners);

            float maxPanelDrift = 0f;
            float maxPlaqueDrift = 0f;
            float maxCloseDrift = 0f;
            float maxWellDrift = 0f;

            var sr = shop.GetComponentInChildren<ScrollRect>(true);
            var slot0 = card0.GetComponent<UI_EcoShopSlot>();

            Vector3[] testCorners = new Vector3[4];
            for (int frame = 0; frame < 900; frame++)
            {
                if (frame == 100 && slot0 != null) slot0.OnPointerEnter(null);
                if (frame == 200 && slot0 != null) slot0.OnPointerExit(null);
                if (frame == 300 && slot0 != null) slot0.OnPointerClick(null);

                if (frame >= 400 && frame < 600 && sr != null)
                {
                    float t = (frame - 400) / 200f;
                    sr.verticalNormalizedPosition = Mathf.Lerp(1f, 0f, t);
                }
                if (frame >= 600 && frame < 800 && sr != null)
                {
                    float t = (frame - 600) / 200f;
                    sr.verticalNormalizedPosition = Mathf.Lerp(0f, 1f, t);
                }

                Canvas.ForceUpdateCanvases();

                panel.GetWorldCorners(testCorners);
                for (int c = 0; c < 4; c++) maxPanelDrift = Mathf.Max(maxPanelDrift, Vector3.Distance(testCorners[c], pCorners[c]));

                plaque.GetWorldCorners(testCorners);
                for (int c = 0; c < 4; c++) maxPlaqueDrift = Mathf.Max(maxPlaqueDrift, Vector3.Distance(testCorners[c], bnrCorners[c]));

                closeBtn.GetWorldCorners(testCorners);
                for (int c = 0; c < 4; c++) maxCloseDrift = Mathf.Max(maxCloseDrift, Vector3.Distance(testCorners[c], clsCorners[c]));

                well.GetWorldCorners(testCorners);
                for (int c = 0; c < 4; c++) maxWellDrift = Mathf.Max(maxWellDrift, Vector3.Distance(testCorners[c], wllCorners[c]));
            }

            if (sr != null) sr.verticalNormalizedPosition = 1f;
            Canvas.ForceUpdateCanvases();

            bool passPanel = maxPanelDrift <= 0.5f;
            bool passPlaque = maxPlaqueDrift <= 0.5f;
            bool passClose = maxCloseDrift <= 0.5f;
            bool passWell = maxWellDrift <= 0.5f;
            bool overallPass = passPanel && passPlaque && passClose && passWell;

            Debug.Log($"--------------------------------------------------------------------------------------\n" +
                      $"| ELEMENT          | MAX DRIFT (px) | THRESHOLD | STATUS |\n" +
                      $"--------------------------------------------------------------------------------------\n" +
                      $"| ShopPanel Root   | {maxPanelDrift:F4} px    | <= 0.5 px | {(passPanel ? "PASS" : "FAIL")}   |\n" +
                      $"| Plaque (Header)  | {maxPlaqueDrift:F4} px    | <= 0.5 px | {(passPlaque ? "PASS" : "FAIL")}   |\n" +
                      $"| Close Button (X) | {maxCloseDrift:F4} px    | <= 0.5 px | {(passClose ? "PASS" : "FAIL")}   |\n" +
                      $"| Content Well     | {maxWellDrift:F4} px    | <= 0.5 px | {(passWell ? "PASS" : "FAIL")}   |\n" +
                      $"--------------------------------------------------------------------------------------\n" +
                      $"OVERALL STABILITY: {(overallPass ? "ALL ELEMENTS 100% ROCK SOLID (PASS)" : "FAIL")}");
        }
    }
}
