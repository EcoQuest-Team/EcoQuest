#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EcoQuest.QA
{
    /// <summary>
    /// One-click Verification and 1920x1080 Screenshot Runner for the 9-Item EcoQuest Polish Pass.
    /// Outputs PASS/FAIL logs and 1080p PNG captures to C:\EcoQuest.gg\Reports\Polish\.
    /// Lives exclusively under Assets/EcoQuest/Editor/QA/ for clean deletion anytime.
    /// </summary>
    public static class PolishPassQARunner
    {
        public const string ReportDir = @"C:\EcoQuest.gg\Reports\Polish";

        [MenuItem("EcoQuest/QA/Run Polish Pass Checks")]
        public static void RunAllChecks()
        {
            Directory.CreateDirectory(ReportDir);
            string sceneName = SceneManager.GetActiveScene().name;
            var rows = new List<string> { "Item,Test,Status,MeasuredDetails" };
            int passCount = 0;
            int failCount = 0;

            void Record(string item, string test, bool passed, string details)
            {
                if (passed) passCount++; else failCount++;
                string clean = (details ?? "").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
                rows.Add($"{item},\"{test}\",{(passed ? "PASS" : "FAIL")},\"{clean}\"");
                if (passed) Debug.Log($"[PolishQA PASS] [{item}] {test}: {details}");
                else Debug.LogError($"[PolishQA FAIL] [{item}] {test}: {details}");
            }

            // 1. Item 1 & Item 9: SharedQuestPanelHomePos & HUD Buff Chip clearance
            try
            {
                Type orchType = FindType("UIOrchestrator");
                var homeField = orchType?.GetField("SharedQuestPanelHomePos", BindingFlags.Public | BindingFlags.Static);
                Vector2 homePos = homeField != null ? (Vector2)homeField.GetValue(null) : Vector2.zero;
                // HUD PlayerStats (40,-32) + Stamina bottom (-122); Buff row at -128 sizeH=36 -> bottom = -164.
                // Panel top = homePos.y (-192), QUEST pill extends +16 up -> -176 -> clearance = -164 - (-176) = 12px (28px to panel).
                bool posOk = Mathf.Abs(homePos.x - 40f) < 0.1f && homePos.y <= -192f;
                Record("Item 1/9", "Shared Quest Panel Anchor Below Buff Row", posOk,
                    $"SharedQuestPanelHomePos={homePos} (BuffChipBottom=-164, ClearanceToPanel={-164f - homePos.y:F1}px)");
            }
            catch (Exception ex) { Record("Item 1/9", "Shared Quest Panel Anchor", false, ex.Message); }

            // 2. Item 2: Ranger Robin 5-minute cooldown helpers
            try
            {
                Type robinType = FindType("CityTrashDetectiveManager");
                var fmt = robinType?.GetMethod("FormatCooldownMS", BindingFlags.Public | BindingFlags.Static);
                string formatted = fmt != null ? (string)fmt.Invoke(null, new object[] { 272f }) : "";
                bool ok = fmt != null && formatted == "4:32";
                Record("Item 2", "Robin Cooldown M:SS Formatting & API", ok,
                    $"FormatCooldownMS(272)='{formatted}'");
            }
            catch (Exception ex) { Record("Item 2", "Robin Cooldown Formatting", false, ex.Message); }

            // 3. Item 3: Hunger drain math (~1% / 15s = 8.0% / 120s)
            try
            {
                Type statsType = FindType("PlayerSurvivalStats");
                var stats = UnityEngine.Object.FindFirstObjectByType(statsType, FindObjectsInactive.Include);
                float rate = 0f;
                bool ok = false;
                if (stats != null)
                {
                    rate = (float)statsType.GetField("hungerDepletionPerSec").GetValue(stats);
                    ok = Mathf.Abs(rate - (1f / 15f)) < 0.001f;
                }
                Record("Item 3", "Hunger Steady Drain Rate & 120s Projection", ok,
                    stats == null ? "PlayerSurvivalStats not in active scene" : $"rate={rate:F5}/s -> 120s drain={rate * 120f:F2}%");
            }
            catch (Exception ex) { Record("Item 3", "Hunger Drain Rate", false, ex.Message); }

            // 4. Item 4: Boost Mutex Groups & Anti-Spam Signature Check
            try
            {
                Type invType = FindType("EcoQuest.Inventory.InventoryService");
                var getGroup = invType?.GetMethod("GetBoostGroup", BindingFlags.Public | BindingFlags.Static);
                var isBlocked = invType?.GetMethod("IsBlockedByActiveBoost", BindingFlags.Public | BindingFlags.Static);
                bool ok = getGroup != null && isBlocked != null;
                Record("Item 4", "Boost Mutex & Anti-Spam Methods Present", ok,
                    $"GetBoostGroup={(getGroup != null)}, IsBlockedByActiveBoost={(isBlocked != null)}");
            }
            catch (Exception ex) { Record("Item 4", "Boost Mutex Check", false, ex.Message); }

            // 5. Item 5: Crosshair Unified Prefab (Small & Pure White)
            try
            {
                Type curType = FindType("CenterScreenCursor");
                var cursor = UnityEngine.Object.FindFirstObjectByType(curType, FindObjectsInactive.Include);
                if (cursor != null)
                {
                    Color nCol = (Color)curType.GetProperty("NormalColor").GetValue(cursor);
                    Color hCol = (Color)curType.GetProperty("HoverColor").GetValue(cursor);
                    float nSize = (float)curType.GetProperty("NormalSize").GetValue(cursor);
                    bool white = nCol.r > 0.98f && nCol.g > 0.98f && nCol.b > 0.98f
                              && hCol.r > 0.98f && hCol.g > 0.98f && hCol.b > 0.98f;
                    bool small = nSize <= 14f;
                    Record("Item 5", $"Crosshair Unified Pure-White Small ({sceneName})", white && small,
                        $"normalSize={nSize:F1}, normalColor={nCol}, hoverColor={hCol}");
                }
                else
                {
                    Record("Item 5", $"Crosshair Present ({sceneName})", false, "CenterScreenCursor not found in open scene");
                }
            }
            catch (Exception ex) { Record("Item 5", "Crosshair Check", false, ex.Message); }

            // 6. Item 6: Run Validate Text Fit Tool
            try
            {
                Type fitTool = FindType("TextFitValidatorTool");
                var valMethod = fitTool?.GetMethod("ValidateAllLoadedTextFit", BindingFlags.Public | BindingFlags.Static);
                valMethod?.Invoke(null, null);
                Record("Item 6", "TextFitValidatorTool Execution", valMethod != null,
                    "Executed ValidateAllLoadedTextFit() (inspect Unity Console for per-control bounds details)");
            }
            catch (Exception ex) { Record("Item 6", "TextFitValidatorTool", false, ex.Message); }

            // 7. Item 7: FeedbackPanel Column Alignment Layout Helper
            try
            {
                Type quizType = FindType("QuizUIController");
                var layoutFn = quizType?.GetMethod("ComputeFeedbackColumnLayout", BindingFlags.Public | BindingFlags.Static);
                bool ok = layoutFn != null;
                if (ok)
                {
                    // Simulate 4-option right column in Forest & City bounds
                    object res = layoutFn.Invoke(null, new object[] { -146.5f, 735.7f, -110f, -325f, 148f });
                    Type resType = res.GetType();
                    Vector2 pos = (Vector2)resType.GetField("CenterPosition").GetValue(res);
                    Vector2 sz = (Vector2)resType.GetField("Size").GetValue(res);
                    float topY = pos.y + sz.y;
                    ok = Math.Abs(sz.x - 882.2f) < 0.5f && topY <= -122f + 0.1f;
                    Record("Item 7", "FeedbackPanel Answer-Column Alignment Layout", ok,
                        $"pos={pos}, size={sz}, topY={topY:F1} (gap below -110f = {-110f - topY:F1}px)");
                }
                else
                {
                    Record("Item 7", "FeedbackPanel Column Layout", false, "ComputeFeedbackColumnLayout missing");
                }
            }
            catch (Exception ex) { Record("Item 7", "FeedbackPanel Column Layout", false, ex.Message); }

            // 8. Item 8: WrongTrashPopup Single Toast & Parse Verification
            try
            {
                Type popupType = FindType("WrongTrashPopup");
                var parseFn = popupType?.GetMethod("ParseNotification", BindingFlags.Public | BindingFlags.Static);
                if (parseFn != null)
                {
                    object r1 = parseFn.Invoke(null, new object[] { "💡 Hint: Put this in the Blue Bin (Recyclable / Pwedeng Gamitin Muli)! • Mistake 1/3", 0 });
                    object r3 = parseFn.Invoke(null, new object[] { "⚠️ 3 Mistakes reached! -10 Eco-Coins deducted. Please check bin colors!", 2 });
                    Type pType = r1.GetType();
                    string lbl1 = (string)pType.GetField("MistakeLabel").GetValue(r1);
                    string lbl3 = (string)pType.GetField("MistakeLabel").GetValue(r3);
                    bool pen3 = (bool)pType.GetField("IsPenalty").GetValue(r3);
                    string body1 = (string)pType.GetField("Body").GetValue(r1);
                    bool ok = lbl1 == "Mistake 1 of 3" && lbl3 == "Mistake 3 of 3" && pen3 && !body1.Contains("\u25A1");
                    Record("Item 8", "WrongTrashPopup Single Card Parse & Mistake Dots", ok,
                        $"r1='{lbl1}' body='{body1}', penalty='{lbl3}'");
                }
                else
                {
                    Record("Item 8", "WrongTrashPopup ParseNotification", false, "Method missing");
                }
            }
            catch (Exception ex) { Record("Item 8", "WrongTrashPopup Check", false, ex.Message); }

            string csvPath = Path.Combine(ReportDir, $"QA_Verification_{sceneName}.csv");
            File.WriteAllLines(csvPath, rows);
            Debug.Log($"[PolishQA] Complete ({sceneName}): PASS={passCount}, FAIL={failCount}. Saved {csvPath}");
        }

        [MenuItem("EcoQuest/QA/Capture 1920x1080 Polish Screenshots")]
        public static void Capture1080pShowcase()
        {
            Directory.CreateDirectory(ReportDir);
            string sceneName = SceneManager.GetActiveScene().name.Replace(" ", "");
            Camera cam = Camera.main != null ? Camera.main : (Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null);
            if (cam == null)
            {
                Debug.LogError("[PolishQA] No active camera found for 1080p capture!");
                return;
            }

            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var origModes = new Dictionary<Canvas, RenderMode>();
            var origCams = new Dictionary<Canvas, Camera>();
            foreach (var c in canvases)
            {
                origModes[c] = c.renderMode;
                origCams[c] = c.worldCamera;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
            }

            RenderTexture rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            RenderTexture prevRt = cam.targetTexture;
            cam.targetTexture = rt;

            try
            {
                // Clean HUD with 2 Buff Chips preview
                CaptureHUDWithBuffChips(sceneName, cam, rt);

                // WrongTrashPopup Toast (Forest scene)
                CaptureWrongTrashToast(sceneName, cam, rt);

                // Quiz long option + aligned FeedbackPanel
                CaptureQuizPolish(sceneName, cam, rt);

                Debug.Log($"<color=green>[PolishQA] 1920x1080 Polish Screenshots saved to {ReportDir} for {sceneName}</color>");
            }
            finally
            {
                cam.targetTexture = prevRt;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);

                foreach (var kvp in origModes)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.renderMode = kvp.Value;
                        kvp.Key.worldCamera = origCams[kvp.Key];
                    }
                }
            }
        }

        private static void CaptureHUDWithBuffChips(string sceneTag, Camera cam, RenderTexture rt)
        {
            Type hudType = FindType("UI_SurvivalStatsHUD");
            var hud = UnityEngine.Object.FindFirstObjectByType(hudType, FindObjectsInactive.Include) as MonoBehaviour;
            if (hud == null) return;

            var previewMethod = hudType.GetMethod("SetPreviewReferenceState", BindingFlags.Public | BindingFlags.Instance);
            previewMethod?.Invoke(hud, new object[] { true });
            Canvas.ForceUpdateCanvases();
            cam.Render();
            SaveRenderTexture(rt, $"{sceneTag}_Item1_HUD_2BuffChips_1080p.png");
            previewMethod?.Invoke(hud, new object[] { false });
        }

        private static void CaptureWrongTrashToast(string sceneTag, Camera cam, RenderTexture rt)
        {
            Type popupType = FindType("WrongTrashPopup");
            var popup = UnityEngine.Object.FindFirstObjectByType(popupType, FindObjectsInactive.Include) as MonoBehaviour;
            if (popup == null) return;

            bool prevActive = popup.gameObject.activeSelf;
            popup.gameObject.SetActive(true);
            var cg = popup.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;

            var parseFn = popupType.GetMethod("ParseNotification", BindingFlags.Public | BindingFlags.Static);
            var applyFn = popupType.GetMethod("ApplyParsedContent", BindingFlags.NonPublic | BindingFlags.Instance);
            if (parseFn != null && applyFn != null)
            {
                object parsed = parseFn.Invoke(null, new object[] { "💡 Hint: Put this in the Blue Bin (Recyclable / Pwedeng Gamitin Muli)! • Mistake 1/3", 0 });
                applyFn.Invoke(popup, new object[] { parsed });
                Canvas.ForceUpdateCanvases();
                cam.Render();
                SaveRenderTexture(rt, $"{sceneTag}_Item8_WrongTrash_Toast_1080p.png");
            }

            if (cg != null) cg.alpha = 0f;
            popup.gameObject.SetActive(prevActive);
        }

        private static void CaptureQuizPolish(string sceneTag, Camera cam, RenderTexture rt)
        {
            Type quizType = FindType("QuizUIController");
            var quiz = UnityEngine.Object.FindFirstObjectByType(quizType, FindObjectsInactive.Include) as MonoBehaviour;
            if (quiz == null) return;

            bool prevActive = quiz.gameObject.activeSelf;
            quiz.gameObject.SetActive(true);
            quizType.GetMethod("SetAllVisibleEditMode", BindingFlags.Public | BindingFlags.Instance)?.Invoke(quiz, null);
            var prepFn = quizType.GetMethod("PrepareInitialQuestion", BindingFlags.Public | BindingFlags.Instance);
            if (prepFn != null)
            {
                prepFn.Invoke(quiz, new object[]
                {
                    "What are non-biodegradable materials according to the info cards?",
                    new string[]
                    {
                        "A) Reusable materials repurposed for crafting",
                        "B) Synthetic materials that cannot break down naturally in the environment",
                        "C) Naturally decaying leaves and food scraps",
                        "D) Organic matter that can rot back into soil"
                    },
                    6
                });
                Canvas.ForceUpdateCanvases();
                cam.Render();
                SaveRenderTexture(rt, $"{sceneTag}_Item6_7_Quiz_Aligned_1080p.png");
            }

            quizType.GetMethod("HideImmediate", BindingFlags.Public | BindingFlags.Instance)?.Invoke(quiz, null);
            quiz.gameObject.SetActive(prevActive);
        }

        private static void SaveRenderTexture(RenderTexture rt, string filename)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            File.WriteAllBytes(Path.Combine(ReportDir, filename), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static Type FindType(string fullOrShortName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullOrShortName, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
#endif
