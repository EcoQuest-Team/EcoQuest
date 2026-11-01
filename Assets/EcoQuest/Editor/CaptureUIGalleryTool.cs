using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tools > EcoQuest > Capture UI Gallery
/// Renders and saves PNG screenshots of all popups (Dialogue, Quiz Q1-Q6, Quiz Results,
/// 3 Bin Cards, Mistake Popup, Tutorial Step Tip) across 16:9, 16:10, and 21:9 into C:\EcoQuest.gg\Reports\UI_Gallery\.
/// </summary>
public static class CaptureUIGalleryTool
{
    private const string GalleryDir = @"C:\EcoQuest.gg\Reports\UI_Gallery";

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

    [MenuItem("Tools/EcoQuest/Capture UI Gallery")]
    public static void CaptureAll()
    {
        Debug.Log(">>> Starting EcoQuest UI Gallery Capture...");
        if (!Directory.Exists(GalleryDir))
        {
            Directory.CreateDirectory(GalleryDir);
        }

        var cam = Camera.main;
        if (cam == null && Camera.allCameras.Length > 0) cam = Camera.allCameras[0];
        if (cam == null)
        {
            Debug.LogError("[FAIL] No camera found for UI Gallery capture!");
            return;
        }

        var ratios = new AspectRatio[]
        {
            new AspectRatio("16_9", 1920, 1080),
            new AspectRatio("16_10", 1920, 1200),
            new AspectRatio("21_9", 2560, 1080)
        };

        // Cache all canvases to ensure they render to camera
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var origRenderModes = new Dictionary<Canvas, RenderMode>();
        var origCameras = new Dictionary<Canvas, Camera>();

        foreach (var c in canvases)
        {
            origRenderModes[c] = c.renderMode;
            origCameras[c] = c.worldCamera;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
        }

        try
        {
            foreach (var r in ratios)
            {
                CaptureAspect(r, cam);
            }

            Debug.Log($"<color=green>[PASS] UI Gallery Capture complete! Saved to {GalleryDir}</color>");
        }
        finally
        {
            // Restore canvases
            foreach (var kvp in origRenderModes)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.renderMode = kvp.Value;
                    kvp.Key.worldCamera = origCameras[kvp.Key];
                }
            }

            // Restore HUD visibility
            HudVisibilityService.Instance?.ClearAllReasons(0f);
            HudVisibilityService.Instance?.SetHUDVisibleImmediate(true);
        }
    }

    private static void CaptureAspect(AspectRatio aspect, Camera cam)
    {
        RenderTexture rt = new RenderTexture(aspect.width, aspect.height, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        cam.targetTexture = rt;

        // 1. Dialogue
        CaptureDialogue(aspect, cam, rt);

        // 2. Quiz Questions & Results
        CaptureQuiz(aspect, cam, rt);

        // 3. Bin Cards
        CaptureBinCards(aspect, cam, rt);

        // 4. Mistake Popup
        CaptureMistakePopup(aspect, cam, rt);

        // 5. Control Tip
        CaptureControlTip(aspect, cam, rt);

        // 6. Quest Panel After Flow
        CaptureQuestPanelPostFlow(aspect, cam, rt);

        cam.targetTexture = null;
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
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
        Debug.Log($"Captured: {filename}");
    }

    private static void CaptureDialogue(AspectRatio aspect, Camera cam, RenderTexture rt)
    {
        var dp = UnityEngine.Object.FindAnyObjectByType<DialoguePresenter>(FindObjectsInactive.Include);
        if (dp == null) return;

        dp.gameObject.SetActive(true);
        dp.EnsureActiveHierarchy();
        dp.EnsureFooterLayout();

        var lines = new string[] {
            "Welcome back, Guardian! The ancient woods need your careful attention once again. Sort the waste properly so nature may thrive in balance."
        };

        dp.ShowDialogue("GUARDIAN", lines, true, "Accept Quest", "Not now");
        cam.Render();
        SaveRT(rt, $"Dialogue_{aspect.name}.png");
        dp.CloseDialogue();
    }

    private static void CaptureQuiz(AspectRatio aspect, Camera cam, RenderTexture rt)
    {
        var quiz = UnityEngine.Object.FindAnyObjectByType<QuizUIController>(FindObjectsInactive.Include);
        if (quiz == null) return;

        quiz.gameObject.SetActive(true);
        quiz.SetAllVisibleEditMode();

        string[] questions = new string[] {
            "Which item belongs in the Biodegradable Bin?",
            "What should you do with broken glass bottles?",
            "Can clean cardboard boxes be recycled?",
            "Where do leftover food scraps go?",
            "Which bin is designed for synthetic plastic wrappers?",
            "Why is proper waste sorting crucial for the forest?"
        };

        string[][] options = new string[][] {
            new string[] { "Banana peel & fruit scraps", "Plastic beverage bottle", "Aluminum soda can", "Foam takeout container" },
            new string[] { "Deposit into Non-Biodegradable Bin", "Throw into compost", "Leave on the grass", "Bury in river" },
            new string[] { "Yes, place in Recyclable Bin", "No, burn them", "Throw into lake", "Only if dirty" },
            new string[] { "Biodegradable Organic Unit", "Recyclable Glass Bin", "Non-Bio Bin", "City river" },
            new string[] { "Non-Biodegradable Trash Bin", "Biodegradable Bin", "Compost Heap", "Recyclable Bin" },
            new string[] { "Preserves healthy soil and clean waterways", "It looks nice", "Makes trash disappear", "No reason" }
        };

        for (int q = 0; q < questions.Length; q++)
        {
            quiz.PrepareInitialQuestion(questions[q], options[q], questions.Length);
            cam.Render();
            SaveRT(rt, $"Quiz_Q{q + 1}_{aspect.name}.png");
        }

        // Quiz Results
        quiz.PreviewResultsPanel();
        cam.Render();
        SaveRT(rt, $"Quiz_Results_{aspect.name}.png");

        quiz.HideImmediate();
    }

    private static void CaptureBinCards(AspectRatio aspect, Camera cam, RenderTexture rt)
    {
        var tm = UnityEngine.Object.FindAnyObjectByType<TutorialManager>(FindObjectsInactive.Include);
        if (tm == null || tm.tutorialCards == null) return;

        string[] cardNames = new string[] { "BinCard_Recycle", "BinCard_Bio", "BinCard_NonBio" };

        for (int i = 0; i < tm.tutorialCards.Length; i++)
        {
            var card = tm.tutorialCards[i];
            if (card == null) continue;

            // Hide all cards, show this one
            foreach (var c in tm.tutorialCards) if (c != null) c.SetActive(false);
            card.SetActive(true);

            // Apply card styling
            var method = typeof(TutorialManager).GetMethod("RestyleCard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(tm, new object[] { card });

            cam.Render();
            string name = (i < cardNames.Length) ? cardNames[i] : $"BinCard_{i}";
            SaveRT(rt, $"{name}_{aspect.name}.png");
            card.SetActive(false);
        }
    }

    private static void CaptureMistakePopup(AspectRatio aspect, Camera cam, RenderTexture rt)
    {
        var popup = UnityEngine.Object.FindAnyObjectByType<WrongTrashPopup>(FindObjectsInactive.Include);
        if (popup == null) return;

        popup.gameObject.SetActive(true);
        var cg = popup.GetComponent<CanvasGroup>();
        if (cg != null) cg.alpha = 1f;

        cam.Render();
        SaveRT(rt, $"MistakePopup_{aspect.name}.png");
        popup.gameObject.SetActive(false);
    }

    private static void CaptureControlTip(AspectRatio aspect, Camera cam, RenderTexture rt)
    {
        var notif = GameObject.Find("--- UI ---/UI_Notification");
        GameObject banner = null;
        if (notif != null) banner = notif.transform.Find("Control Tip Banner")?.gameObject;
        if (banner == null)
        {
            var all = Resources.FindObjectsOfTypeAll<ControlTipCardUI>();
            if (all != null && all.Length > 0) banner = all[0].gameObject;
        }

        if (banner != null)
        {
            bool origActive = banner.activeSelf;
            var cg = banner.GetComponent<CanvasGroup>();
            float origAlpha = cg != null ? cg.alpha : 1f;

            banner.SetActive(true);
            if (cg != null) cg.alpha = 1f;

            cam.Render();
            SaveRT(rt, $"TutorialTip_{aspect.name}.png");

            banner.SetActive(origActive);
            if (cg != null) cg.alpha = origAlpha;
        }
    }

    private static void CaptureQuestPanelPostFlow(AspectRatio aspect, Camera cam, RenderTexture rt)
    {
        var qpCtrl = UnityEngine.Object.FindAnyObjectByType<QuestPanelToggleController>(FindObjectsInactive.Include);
        if (qpCtrl == null) return;

        bool origActive = qpCtrl.gameObject.activeSelf;
        UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.QuestActive);
        UIOrchestrator.Instance?.SetModalsOpen(false);
        qpCtrl.gameObject.SetActive(true);
        qpCtrl.SetPanelVisible(true, immediate: true);

        cam.Render();
        SaveRT(rt, $"QuestPanel_PostFlow_{aspect.name}.png");

        qpCtrl.gameObject.SetActive(origActive);
    }
}
