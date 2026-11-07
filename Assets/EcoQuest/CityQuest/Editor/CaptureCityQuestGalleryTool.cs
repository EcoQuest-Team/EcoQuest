using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using EcoQuest.CityQuest;

namespace EcoQuest.CityQuest.Editor
{
    public static class CaptureCityQuestGalleryTool
    {
        private const string GalleryDir = @"C:\EcoQuest.gg\Reports\CityQuestUI";

        private struct AspectRatio
        {
            public string name;
            public int width;
            public int height;

            public AspectRatio(string n, int w, int h)
            {
                name = n;
                width = w;
                height = h;
            }
        }

        [MenuItem("EcoQuest/Capture City Quest UI Gallery")]
        public static void CaptureAll()
        {
            Debug.Log("[CityQuestGallery] Starting City Quest UI Gallery Capture...");
            if (!Directory.Exists(GalleryDir))
            {
                Directory.CreateDirectory(GalleryDir);
            }

            Camera cam = Camera.main;
            if (cam == null && Camera.allCameras.Length > 0) cam = Camera.allCameras[0];
            if (cam == null)
            {
                Debug.LogError("[CityQuestGallery] FAIL: No camera found!");
                return;
            }

            var mgr = UnityEngine.Object.FindFirstObjectByType<CityTrashDetectiveManager>(FindObjectsInactive.Include);
            var ui = UnityEngine.Object.FindFirstObjectByType<CityTrashDetectiveUI>(FindObjectsInactive.Include);
            var dp = UnityEngine.Object.FindFirstObjectByType<DialoguePresenter>(FindObjectsInactive.Include);

            if (ui == null || mgr == null || mgr.database == null)
            {
                Debug.LogError("[CityQuestGallery] FAIL: CityTrashDetectiveUI or Manager or Database missing!");
                return;
            }

            var ratios = new AspectRatio[]
            {
                new AspectRatio("16_9", 1920, 1080),
                new AspectRatio("16_10", 1920, 1200),
                new AspectRatio("21_9", 2560, 1080)
            };

            // Switch all active/inactive canvases to ScreenSpaceCamera with planeDistance=1f
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var origRenderModes = new Dictionary<Canvas, RenderMode>();
            var origCameras = new Dictionary<Canvas, Camera>();
            var origDistances = new Dictionary<Canvas, float>();

            foreach (var c in canvases)
            {
                origRenderModes[c] = c.renderMode;
                origCameras[c] = c.worldCamera;
                origDistances[c] = c.planeDistance;

                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 1f;
            }

            try
            {
                foreach (var r in ratios)
                {
                    CaptureAspect(r, cam, mgr, ui, dp);
                }
                Debug.Log($"<color=green>[PASS] City Quest UI Gallery successfully saved to {GalleryDir}</color>");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CityQuestGallery] Exception: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                foreach (var kvp in origRenderModes)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.renderMode = kvp.Value;
                        kvp.Key.worldCamera = origCameras[kvp.Key];
                        kvp.Key.planeDistance = origDistances[kvp.Key];
                    }
                }

                ui.HideAllImmediate();
                dp?.CloseDialogue();
                WorldInteractionPrompt.Instance?.HidePrompt();
            }
        }

        private static void CaptureAspect(AspectRatio aspect, Camera cam, CityTrashDetectiveManager mgr, CityTrashDetectiveUI ui, DialoguePresenter dp)
        {
            RenderTexture rt = new RenderTexture(aspect.width, aspect.height, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            cam.targetTexture = rt;

            // Generate sample slots for checklist
            var slots = new List<CityTrashDetectiveManager.ChecklistSlot>(10);
            for (int i = 0; i < 10 && i < mgr.database.items.Count; i++)
            {
                slots.Add(new CityTrashDetectiveManager.ChecklistSlot
                {
                    item = mgr.database.items[i],
                    collected = false,
                    district = (CityDistrict)(i % 5)
                });
            }

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Transform robin = mgr.rangerRobinTransform;
            Vector3 savedPlayerPos = player != null ? player.transform.position : Vector3.zero;
            Quaternion savedPlayerRot = player != null ? player.transform.rotation : Quaternion.identity;
            Vector3 savedCamPos = cam.transform.position;
            Quaternion savedCamRot = cam.transform.rotation;
            float savedCamFov = cam.fieldOfView;
            int savedMask = cam.cullingMask;

            try
            {
                if (robin != null && player != null)
                {
                    Vector3 rFwd = robin.forward;
                    rFwd.y = 0f;
                    if (rFwd.sqrMagnitude < 0.01f) rFwd = Vector3.forward;
                    rFwd.Normalize();

                    player.transform.position = robin.position + rFwd * 2.1f;
                    player.transform.rotation = Quaternion.LookRotation(-rFwd, Vector3.up);
                }

                // 0. Robin Interact Prompt in First-Person
                var wpRobin = UnityEngine.Object.FindFirstObjectByType<WorldInteractionPrompt>(FindObjectsInactive.Include);
                if (wpRobin != null && robin != null && player != null)
                {
                    cam.cullingMask = savedMask & ~(1 << 7);
                    cam.fieldOfView = 60f;
                    Vector3 eyePos = player.transform.position + Vector3.up * 1.52f;
                    Vector3 lookTarget = robin.position + Vector3.up * 1.42f;
                    cam.transform.SetPositionAndRotation(eyePos, Quaternion.LookRotation((lookTarget - eyePos).normalized, Vector3.up));

                    wpRobin.gameObject.SetActive(true);
                    wpRobin.SetBlocked(false);
                    wpRobin.ShowPrompt(robin, "Ranger Robin", "Talk", "E");
                    if (wpRobin.PromptPanelRect != null)
                    {
                        wpRobin.PromptPanelRect.anchoredPosition = new Vector2(180f, 60f);
                    }
                    Canvas.ForceUpdateCanvases();
                    cam.Render();
                    SaveRT(rt, $"00_Robin_InteractPrompt_{aspect.name}.png");
                    wpRobin.SetBlocked(true);
                    wpRobin.HidePrompt();
                    if (wpRobin.PromptPanelRect != null) wpRobin.PromptPanelRect.gameObject.SetActive(false);
                }

                // 1-3. Robin Dialogues using shared DialogueCameraRig + Bokeh DOF on Robin's head + full player head visible
                if (dp != null && robin != null)
                {
                    var rig = EcoQuest.Shared.DialogueCameraRig.Instance;
                    rig.EnterDialogue(robin, player != null ? player.transform : null, 0f, "GalleryCapture");
                    if (rig.VirtualCamera != null)
                    {
                        cam.transform.SetPositionAndRotation(rig.VirtualCamera.transform.position, rig.VirtualCamera.transform.rotation);
                        cam.fieldOfView = rig.VirtualCamera.Lens.FieldOfView;
                    }
                    UIBackgroundBlur.SetWeight(1f);
                    UIBackgroundBlur.UpdateDialogueFocusImmediate(robin, EcoQuest.Shared.DialogueCameraRig.GetNpcHeadPosition(robin));

                    // 1a. Robin Mid-Conversation ([E] Continue)
                    dp.gameObject.SetActive(true);
                    dp.EnsureActiveHierarchy();
                    dp.EnsureFooterLayout();
                    dp.ShowDialogue("RANGER ROBIN", new string[] {
                        "Hey there, Detective! Welcome to the City Eco-Watch station.",
                        "Take my Field Checklist, explore the 5 districts before the 4:00 timer runs out, and press E to collect each item and learn its story!"
                    }, true, "Accept", "Close");
                    // Show page 0 ([E] Continue state)
                    var showPageMethod = typeof(DialoguePresenter).GetMethod("ShowPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    showPageMethod?.Invoke(dp, new object[] { 0 });
                    ForceFullDialoguePage(dp);
                    Canvas.ForceUpdateCanvases();
                    cam.Render();
                    SaveRT(rt, $"01a_Robin_MidDialogue_Continue_{aspect.name}.png");

                    // 1b. Robin Offer Dialogue (Final page with choices)
                    showPageMethod?.Invoke(dp, new object[] { 1 });
                    ForceFullDialoguePage(dp);
                    Canvas.ForceUpdateCanvases();
                    cam.Render();
                    SaveRT(rt, $"01_Robin_OfferDialogue_{aspect.name}.png");
                    dp.CloseDialogue();

                    // 2. Robin Progress Dialogue
                    dp.gameObject.SetActive(true);
                    dp.EnsureActiveHierarchy();
                    dp.EnsureFooterLayout();
                    dp.ShowDialogue("RANGER ROBIN", new string[] {
                        "Current Progress: 4 / 10 Items Found. Time Remaining: 02:45. Keep searching the districts!"
                    }, true, "Keep Searching", "Cancel Quest");
                    ForceFullDialoguePage(dp);
                    Canvas.ForceUpdateCanvases();
                    cam.Render();
                    SaveRT(rt, $"02_Robin_ProgressDialogue_{aspect.name}.png");
                    dp.CloseDialogue();

                    // 3. Robin Turn-In Dialogue
                    dp.gameObject.SetActive(true);
                    dp.EnsureActiveHierarchy();
                    dp.EnsureFooterLayout();
                    dp.ShowDialogue("RANGER ROBIN", new string[] {
                        "Incredible work! You recovered every item on the Field Checklist! Let's examine your investigation report."
                    }, true, "Claim Star Bonus & View Results!", "Close");
                    ForceFullDialoguePage(dp);
                    Canvas.ForceUpdateCanvases();
                    cam.Render();
                    SaveRT(rt, $"03_Robin_TurnInDialogue_{aspect.name}.png");
                    dp.CloseDialogue();

                    rig.ExitDialogue("GalleryCapture", 0f);
                    UIBackgroundBlur.SetWeight(0f);
                }
                if (wpRobin != null) wpRobin.SetBlocked(false);
            }
            finally
            {
                if (player != null) player.transform.SetPositionAndRotation(savedPlayerPos, savedPlayerRot);
                cam.transform.SetPositionAndRotation(savedCamPos, savedCamRot);
                cam.fieldOfView = savedCamFov;
                cam.cullingMask = savedMask;
            }

            // 4. Checklist Unchecked (0/10)
            for (int i = 0; i < slots.Count; i++) slots[i].collected = false;
            ui.ShowChecklist(slots, 0, 240f, 4, 0f);
            Canvas.ForceUpdateCanvases();
            cam.Render();
            SaveRT(rt, $"04_Checklist_Unchecked_{aspect.name}.png");
            ui.HideChecklist();

            // 5. Checklist Checked (5/10)
            for (int i = 0; i < 5; i++) slots[i].collected = true;
            ui.ShowChecklist(slots, 5, 142f, 3, 0f);
            Canvas.ForceUpdateCanvases();
            cam.Render();
            SaveRT(rt, $"05_Checklist_Checked_{aspect.name}.png");
            ui.HideChecklist();

            // 6. Checklist Highlighted Hint
            ui.ShowChecklist(slots, 5, 118f, 2, 21f);
            ui.HighlightHintRow(5, 10f, slots, 5);
            Canvas.ForceUpdateCanvases();
            cam.Render();
            SaveRT(rt, $"06_Checklist_HighlightedHint_{aspect.name}.png");
            ui.HideChecklist();

            // 7. Checklist Hidden via TAB
            ui.ShowChecklist(slots, 5, 118f, 2, 21f);
            if (ui.checklistRect != null) ui.checklistRect.anchoredPosition = new Vector2(-260f, -40f);
            if (ui.checklistCanvasGroup != null) ui.checklistCanvasGroup.alpha = 0f;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            SaveRT(rt, $"07_Checklist_Hidden_TAB_{aspect.name}.png");
            ui.HideChecklist();

            // 8. Results 0 Stars (Time's Up)
            ui.ShowResultsModal(6, 0f, 0, 30, 0, false, false, null, null, null);
            SetResultsStarsManual(ui, 0);
            Canvas.ForceUpdateCanvases();
            cam.Render();
            SaveRT(rt, $"08_Results_0Stars_TimesUp_{aspect.name}.png");
            ui.HideResultsModal();

            // 9. Results 1 Star
            ui.ShowResultsModal(10, 35f, 1, 50, 25, false, true, null, null, null);
            SetResultsStarsManual(ui, 1);
            Canvas.ForceUpdateCanvases();
            cam.Render();
            SaveRT(rt, $"09_Results_1Star_{aspect.name}.png");
            ui.HideResultsModal();

            // 10. Results 2 Stars
            ui.ShowResultsModal(10, 58f, 2, 50, 50, false, true, null, null, null);
            SetResultsStarsManual(ui, 2);
            Canvas.ForceUpdateCanvases();
            cam.Render();
            SaveRT(rt, $"10_Results_2Stars_{aspect.name}.png");
            ui.HideResultsModal();

            // 11. Results 3 Stars (Case Solved)
            ui.ShowResultsModal(10, 94f, 3, 50, 100, true, true, null, null, null);
            SetResultsStarsManual(ui, 3);
            Canvas.ForceUpdateCanvases();
            cam.Render();
            SaveRT(rt, $"11_Results_3Stars_CaseSolved_{aspect.name}.png");
            ui.HideResultsModal();

            // 12. Bonus Quiz
            var quiz = UnityEngine.Object.FindAnyObjectByType<QuizUIController>(FindObjectsInactive.Include);
            if (quiz != null)
            {
                quiz.gameObject.SetActive(true);
                quiz.SetAllVisibleEditMode();
                quiz.PrepareInitialQuestion(
                    "Which waste category does a broken fluorescent tube belong to?",
                    new string[] { "Hazardous Waste", "Biodegradable Organic", "Recyclable Glass", "Non-Bio General" },
                    5);
                Canvas.ForceUpdateCanvases();
                cam.Render();
                SaveRT(rt, $"12_BonusQuiz_{aspect.name}.png");
                quiz.HideImmediate();
            }

            // 13. Trash ID Card Toast
            if (ui.idCardCanvasGroup != null)
            {
                var popMethod = typeof(CityTrashDetectiveUI).GetMethod("PopulateIdCard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (popMethod != null && mgr.database.items.Count > 0)
                {
                    popMethod.Invoke(ui, new object[] { mgr.database.items[0], 5 });
                }
                ui.idCardCanvasGroup.gameObject.SetActive(true);
                ui.idCardCanvasGroup.alpha = 1f;
                if (ui.idCardRect != null) ui.idCardRect.anchoredPosition = new Vector2(-24f, 24f);
                Canvas.ForceUpdateCanvases();
                cam.Render();
                SaveRT(rt, $"13_TrashIdCardToast_{aspect.name}.png");
                ui.idCardCanvasGroup.alpha = 0f;
                ui.idCardCanvasGroup.gameObject.SetActive(false);
            }

            // 14. Compass Hint HUD
            if (ui.compassHudGroup != null)
            {
                ui.compassHudGroup.gameObject.SetActive(true);
                ui.compassHudGroup.alpha = 1f;
                if (ui.compassLabelText != null) ui.compassLabelText.text = "HINT: Banana Peel (Park) - 24m";
                if (ui.compassArrowRect != null) ui.compassArrowRect.localRotation = Quaternion.Euler(0f, 0f, -38f);
                Canvas.ForceUpdateCanvases();
                cam.Render();
                SaveRT(rt, $"14_CompassHint_{aspect.name}.png");
                ui.compassHudGroup.alpha = 0f;
                ui.compassHudGroup.gameObject.SetActive(false);
            }

            // 15. World Pickup Prompt
            Transform promptTarget = (mgr.rangerRobinTransform != null) ? mgr.rangerRobinTransform : cam.transform;
            var wp = UnityEngine.Object.FindFirstObjectByType<WorldInteractionPrompt>(FindObjectsInactive.Include);
            if (wp != null)
            {
                wp.gameObject.SetActive(true);
                wp.ShowPrompt(promptTarget, "Banana Peel", "Collect", "E");
                Canvas.ForceUpdateCanvases();
                cam.Render();
                SaveRT(rt, $"15_WorldPickupPrompt_{aspect.name}.png");
                wp.HidePrompt();
            }

            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }

        private static void ForceFullDialoguePage(DialoguePresenter dp)
        {
            if (dp == null) return;
            foreach (var tmp in dp.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                tmp.maxVisibleCharacters = 99999;
            }
            foreach (var kcp in dp.GetComponentsInChildren<KeyCapPrompt>(true))
            {
                kcp.SetLocked(false);
                if (kcp.ActionLabelText != null && kcp.transform.parent != null)
                {
                    var btn = kcp.GetComponentInParent<Button>();
                    if (btn != null)
                    {
                        var rt = btn.GetComponent<RectTransform>();
                        float w = Mathf.Max(165f, kcp.ActionLabelText.preferredWidth + 96f);
                        if (rt != null) rt.sizeDelta = new Vector2(w, rt.sizeDelta.y > 10f ? rt.sizeDelta.y : 52f);
                        var le = btn.GetComponent<LayoutElement>();
                        if (le != null) { le.minWidth = w; le.preferredWidth = w; }
                    }
                }
            }
            var cg = dp.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;
        }

        private static void SetResultsStarsManual(CityTrashDetectiveUI ui, int earned)
        {
            if (ui == null || ui.resultsStarImages == null) return;
            for (int i = 0; i < 3; i++)
            {
                if (i < ui.resultsStarImages.Length && ui.resultsStarImages[i] != null)
                {
                    bool isEarned = (i < earned);
                    ui.resultsStarImages[i].sprite = isEarned ? ui.starFilledSprite : ui.starOutlineSprite;
                    ui.resultsStarImages[i].color = isEarned ? new Color(1f, 0.84f, 0.25f, 1f) : new Color(0.55f, 0.68f, 0.60f, 0.60f);
                    ui.resultsStarImages[i].transform.localScale = Vector3.one;
                    ui.resultsStarImages[i].gameObject.SetActive(true);
                }
            }
            if (ui.resultsStarsText != null)
            {
                ui.resultsStarsText.text = $"{earned} / 3 Stars Earned";
            }
        }

        private static void SaveRT(RenderTexture rt, string filename)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            RenderTexture.active = prev;

            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string fullPath = Path.Combine(GalleryDir, filename);
            File.WriteAllBytes(fullPath, png);
            Debug.Log($"[CityQuestGallery] Captured: {filename} ({png.Length} bytes)");
        }
    }
}
