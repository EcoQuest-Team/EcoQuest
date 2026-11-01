using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;

public static class DiagnosticHUDTool
{
    [MenuItem("Tools/EcoQuest/Diagnose HUD State")]
    public static void DiagnoseHUD()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== DIAGNOSTIC HUD REPORT ===");
        sb.AppendLine($"Active Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        sb.AppendLine($"Play Mode: {Application.isPlaying}");

        string[] targets = new string[]
        {
            "UI_HUD_SettingsButton",
            "SettingsIconButton",
            "UI_Quest",
            "QuestPanel",
            "UI_Minimap",
            "UI_Coins",
            "Control Tip Banner",
            "TutorialCards",
            "UI_TutorialOverlay",
            "WASDTutorial",
            "UI_InteractPrompt",
            "ModalBackdrop"
        };

        foreach (var name in targets)
        {
            var gos = Resources.FindObjectsOfTypeAll<GameObject>();
            GameObject match = null;
            foreach (var go in gos)
            {
                if (go.name == name && (go.scene.name == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name || go.scene.name == "DontDestroyOnLoad"))
                {
                    match = go;
                    break;
                }
            }

            if (match == null)
            {
                sb.AppendLine($"\n[TARGET: {name}] NOT FOUND in active scene!");
                continue;
            }

            sb.AppendLine($"\n[TARGET: {name}] (Path: {GetPath(match.transform)})");
            sb.AppendLine($"  activeSelf: {match.activeSelf}, activeInHierarchy: {match.activeInHierarchy}");
            var rt = match.GetComponent<RectTransform>();
            if (rt != null)
            {
                sb.AppendLine($"  localScale: {rt.localScale}");
                sb.AppendLine($"  anchoredPosition: {rt.anchoredPosition}, sizeDelta: {rt.sizeDelta}, rect: {rt.rect}");
                sb.AppendLine($"  anchorMin: {rt.anchorMin}, anchorMax: {rt.anchorMax}, pivot: {rt.pivot}");
            }
            var canvas = match.GetComponent<Canvas>() ?? match.GetComponentInParent<Canvas>(true);
            if (canvas != null)
            {
                sb.AppendLine($"  Canvas: '{canvas.gameObject.name}', enabled: {canvas.enabled}, renderMode: {canvas.renderMode}, sortingOrder: {canvas.sortingOrder}");
            }
            else
            {
                sb.AppendLine("  Canvas: NONE found on target or parent");
            }

            var cg = match.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                sb.AppendLine($"  Own CanvasGroup: alpha={cg.alpha}, interactable={cg.interactable}, blocksRaycasts={cg.blocksRaycasts}, ignoreParentGroups={cg.ignoreParentGroups}");
            }
            else
            {
                sb.AppendLine("  Own CanvasGroup: NONE");
            }

            var parentCgs = match.GetComponentsInParent<CanvasGroup>(true);
            foreach (var pcg in parentCgs)
            {
                if (pcg.gameObject != match)
                {
                    sb.AppendLine($"  Parent CanvasGroup ('{pcg.gameObject.name}'): alpha={pcg.alpha}, interactable={pcg.interactable}, blocksRaycasts={pcg.blocksRaycasts}");
                }
            }
        }

        // ModalManager state
        sb.AppendLine("\n=== MODAL MANAGER STATE ===");
        if (ModalManager.Instance != null)
        {
            sb.AppendLine($"  ModalManager.Instance found. IsAnyModalOpen: {ModalManager.Instance.IsAnyModalOpen}");
            sb.AppendLine($"  ActiveModal: {(ModalManager.Instance.ActiveModal != null ? ModalManager.Instance.ActiveModal.type.ToString() : "null")}");
        }
        else
        {
            sb.AppendLine("  ModalManager.Instance is null");
        }

        // DialoguePresenter state
        sb.AppendLine("\n=== DIALOGUE PRESENTER STATE ===");
        var dp = UnityEngine.Object.FindAnyObjectByType<DialoguePresenter>(FindObjectsInactive.Include);
        if (dp != null)
        {
            sb.AppendLine($"  DialoguePresenter found: IsInDialogue={dp.IsInDialogue}, alpha={dp.CurrentAlpha}");
        }

        // SequentialWASDTutorial state
        sb.AppendLine("\n=== TUTORIAL STATE ===");
        if (SequentialWASDTutorial.Instance != null)
        {
            sb.AppendLine($"  SequentialWASDTutorial.Instance found: IsCompleted={SequentialWASDTutorial.Instance.IsCompleted}, AllowedState={SequentialWASDTutorial.AllowedState}");
        }

        // Look for garbled markers (small world-space objects, prompts, sprites)
        sb.AppendLine("\n=== SUSPECT GARBLED MARKERS ===");
        var allRenderers = Resources.FindObjectsOfTypeAll<Renderer>();
        foreach (var r in allRenderers)
        {
            if (r.gameObject.scene.name == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name && r.enabled && r.gameObject.activeInHierarchy)
            {
                if (r.gameObject.name.ToLower().Contains("marker") || r.gameObject.name.ToLower().Contains("prompt") || r.gameObject.name.ToLower().Contains("arrow") || r.gameObject.name.ToLower().Contains("exclamation"))
                {
                    sb.AppendLine($"  Renderer: '{r.gameObject.name}' (type {r.GetType().Name}), bounds: {r.bounds}, transform.position: {r.transform.position}");
                }
            }
        }

        var allGraphics = Resources.FindObjectsOfTypeAll<Graphic>();
        foreach (var g in allGraphics)
        {
            if (g.gameObject.scene.name == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name && g.enabled && g.gameObject.activeInHierarchy)
            {
                if (g.canvas != null && g.canvas.renderMode == RenderMode.WorldSpace)
                {
                    sb.AppendLine($"  WorldSpace Graphic: '{g.gameObject.name}' on '{g.canvas.gameObject.name}', color: {g.color}, rect: {g.rectTransform.rect}");
                }
                else if (g.gameObject.name.ToLower().Contains("marker") || g.gameObject.name.ToLower().Contains("exclamation") || g.gameObject.name.ToLower().Contains("pointer"))
                {
                    sb.AppendLine($"  UI Graphic: '{g.gameObject.name}' on Canvas '{g.canvas?.gameObject.name}', active: {g.gameObject.activeInHierarchy}, color: {g.color}");
                }
            }
        }

        string result = sb.ToString();
        Debug.Log(result);
        File.WriteAllText(@"C:\EcoQuest.gg\Reports\HUD_Diagnostic.txt", result);
    }

    private static string GetPath(Transform t)
    {
        if (t.parent == null) return t.name;
        return GetPath(t.parent) + "/" + t.name;
    }
}
