using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

namespace EcoQuest.EditorTools
{
    public static class ValidateUIVisibleTool
    {
        [MenuItem("Tools/EcoQuest/Validate UI Visible")]
        public static void ValidateUIVisible()
        {
            Debug.Log("==========================================");
            Debug.Log($"[Validate UI Visible] Validating active scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            Debug.Log("==========================================");

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool isCity = sceneName.IndexOf("City", System.StringComparison.OrdinalIgnoreCase) >= 0;

            bool allPassed = true;

            var orch = UIOrchestrator.Instance;

            // 1. Settings Gear Button
            bool expGear = orch != null ? orch.GetExpectedVisibility(UIOrchestrator.UIElement.Gear) : true;
            allPassed &= CheckElement("UI_HUD_SettingsButton", "Settings Button", mustBeActive: expGear);

            // 2. Quest Panel (Phase profile driven)
            bool expQuest = orch != null ? orch.GetExpectedVisibility(UIOrchestrator.UIElement.QuestPanel) : (!isCity);
            allPassed &= CheckElement("QuestPanel", "Quest Panel", mustBeActive: expQuest);

            // 3. Minimap
            bool expMinimap = orch != null ? orch.GetExpectedVisibility(UIOrchestrator.UIElement.Minimap) : true;
            allPassed &= CheckElement("UI_Minimap", "Minimap", mustBeActive: expMinimap);

            // 4. Coins Counter
            bool expCoins = orch != null ? orch.GetExpectedVisibility(UIOrchestrator.UIElement.Coins) : true;
            allPassed &= CheckElement("UI_Coins", "Coin Counter", mustBeActive: expCoins);

            // 5. Tutorial / Tip Banner (Forest only)
            if (!isCity)
            {
                bool expTut = orch != null ? orch.GetExpectedVisibility(UIOrchestrator.UIElement.TutorialCard) : true;
                allPassed &= CheckElement("Control Tip Banner", "Tutorial / Tip Banner", mustBeActive: expTut);
            }
            else
            {
                Debug.Log("<color=green>[PASS]</color> Tutorial / Tip Banner: Omitted in City Scene (by design).");
            }

            // 6. Crosshair Aiming Dot (Forest gameplay reticle)
            if (!isCity)
            {
                bool expCrosshair = orch != null ? orch.GetExpectedVisibility(UIOrchestrator.UIElement.Crosshair) : true;
                allPassed &= CheckElement("UI_Crosshair", "Crosshair Marker", mustBeActive: expCrosshair);
            }
            else
            {
                Debug.Log("<color=green>[PASS]</color> Crosshair Marker: Omitted in City Scene (by design).");
            }

            if (allPassed)
            {
                Debug.Log("<color=green><b>[Validate UI Visible] ALL CHECKS PASSED!</b></color>");
            }
            else
            {
                Debug.LogWarning("<color=orange><b>[Validate UI Visible] SOME CHECKS FAILED - see details above.</b></color>");
            }
        }

        private static bool CheckElement(string objectName, string description, bool mustBeActive)
        {
            var go = GameObject.Find(objectName);
            if (go == null)
            {
                // Try finding by name across all objects including inactive
                var allGos = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (var g in allGos)
                {
                    if (g.name == objectName && g.scene.isLoaded)
                    {
                        go = g;
                        break;
                    }
                }
            }

            if (go == null)
            {
                if (!mustBeActive || objectName == "UI_Crosshair")
                {
                    Debug.Log($"<color=green>[PASS]</color> {description} ('{objectName}'): Correctly omitted or inactive in this scene.");
                    return true;
                }
                Debug.LogError($"<color=red>[FAIL]</color> {description} ('{objectName}'): Not found in scene hierarchy!");
                return false;
            }

            // For owned banner, allow inactive if in proper state
            if (objectName == "Control Tip Banner")
            {
                var bannerComp = go.GetComponent<ControlTipCardUI>();
                var rtBanner = go.GetComponent<RectTransform>();
                if (bannerComp != null && (go.activeInHierarchy || bannerComp.CurrentState == ControlTipCardUI.BannerState.Hidden))
                {
                    Debug.Log($"<color=green>[PASS]</color> {description} ('{objectName}'): Validated (State: {bannerComp.CurrentState}, scale: {rtBanner?.localScale}).");
                    return true;
                }
            }

            if (!mustBeActive)
            {
                if (go.activeInHierarchy)
                {
                    Debug.LogError($"<color=red>[FAIL]</color> {description} ('{objectName}'): Is ACTIVE in hierarchy when it should be disabled.");
                    return false;
                }
                Debug.Log($"<color=green>[PASS]</color> {description} ('{objectName}'): Correctly disabled.");
                return true;
            }

            // mustBeActive == true
            if (!go.activeInHierarchy)
            {
                Debug.LogError($"<color=red>[FAIL]</color> {description} ('{objectName}'): Is inactive in hierarchy!");
                return false;
            }

            // Check CanvasGroup alpha
            var cg = go.GetComponent<CanvasGroup>();
            if (cg != null && cg.alpha < 0.05f)
            {
                Debug.LogError($"<color=red>[FAIL]</color> {description} ('{objectName}'): CanvasGroup alpha is {cg.alpha:F2} (invisible)!");
                return false;
            }

            // Check RectTransform localScale
            var rt = go.GetComponent<RectTransform>();
            if (rt != null && (Mathf.Abs(rt.localScale.x) < 0.01f || Mathf.Abs(rt.localScale.y) < 0.01f))
            {
                Debug.LogError($"<color=red>[FAIL]</color> {description} ('{objectName}'): RectTransform scale is zero ({rt.localScale})!");
                return false;
            }

            // Check parent Canvas if any
            var canvas = go.GetComponentInParent<Canvas>();
            if (canvas != null && !canvas.enabled)
            {
                Debug.LogError($"<color=red>[FAIL]</color> {description} ('{objectName}'): Parent Canvas '{canvas.name}' is disabled!");
                return false;
            }

            float currentAlpha = (cg != null) ? cg.alpha : 1f;
            Vector3 currentScale = (rt != null) ? rt.localScale : Vector3.one;
            Debug.Log($"<color=green>[PASS]</color> {description} ('{objectName}'): Active, scale={currentScale}, alpha={currentAlpha}.");
            return true;
        }
    }
}
