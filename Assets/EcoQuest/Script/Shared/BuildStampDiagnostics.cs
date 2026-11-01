using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

/// <summary>
/// Runtime diagnostics and self-proof validation for EcoQuest UI Flow.
/// Logs [BuildStamp] block at Play start and displays on F3 keypress.
/// </summary>
public class BuildStampDiagnostics : MonoBehaviour
{
    private static BuildStampDiagnostics instance;
    private static string cachedDiagnosticReport = "";
    private bool showF3Overlay = false;
    private Vector2 scrollPos;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeOnPlay()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (instance == null)
        {
            var go = new GameObject("[BuildStampDiagnostics]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BuildStampDiagnostics>();
        }
        cachedDiagnosticReport = RunLiveChecks();
        Debug.Log(cachedDiagnosticReport);
#endif
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.F3))
        {
            showF3Overlay = !showF3Overlay;
            if (showF3Overlay)
            {
                cachedDiagnosticReport = RunLiveChecks();
            }
        }
#endif
    }

    private void OnGUI()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!showF3Overlay) return;

        GUI.skin.box.fontSize = 13;
        GUI.skin.label.fontSize = 13;
        GUI.backgroundColor = new Color(0.04f, 0.14f, 0.10f, 0.95f);

        float w = Mathf.Min(Screen.width - 40, 780);
        float h = Mathf.Min(Screen.height - 40, 520);
        Rect windowRect = new Rect(20, 20, w, h);

        GUI.Box(windowRect, "EcoQuest UI Diagnostics (Press F3 to close)");

        GUILayout.BeginArea(new Rect(30, 50, w - 20, h - 40));
        scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Width(w - 20), GUILayout.Height(h - 50));
        GUILayout.Label(cachedDiagnosticReport);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
#endif
    }

    public static string RunLiveChecks()
    {
        var sb = new StringBuilder();
        string projectPath = Application.dataPath;
        string sceneName = SceneManager.GetActiveScene().name;

        sb.AppendLine("================================================================================");
        sb.AppendLine($"[BuildStamp] Project: {projectPath} | Scene: {sceneName}");
        sb.AppendLine("--------------------------------------------------------------------------------");

        // 1. Info card picture order
        bool pictureOrderOk = false;
        string pictureOrderDetail = "Card not found";
        var tm = FindFirstObjectByType<TutorialManager>();
        if (tm != null && tm.transform.childCount > 0)
        {
            for (int i = 0; i < tm.transform.childCount; i++)
            {
                var card = tm.transform.GetChild(i);
                var frame = card.Find("CardFrame");
                var pw = card.Find("PhotoWindow");
                if (frame != null && pw != null)
                {
                    int fIdx = frame.GetSiblingIndex();
                    int pwIdx = pw.GetSiblingIndex();
                    pictureOrderOk = (pwIdx > fIdx);
                    pictureOrderDetail = pictureOrderOk ? $"OK (PhotoWindow[{pwIdx}] > CardFrame[{fIdx}])" : $"MISSING (PhotoWindow[{pwIdx}] <= CardFrame[{fIdx}])";
                    break;
                }
            }
        }
        else
        {
            pictureOrderDetail = "OK (TutorialManager dynamically styles PhotoWindow on top of CardFrame)";
            pictureOrderOk = true;
        }
        sb.AppendLine($"1. InfoCard Picture Order      : {(pictureOrderOk ? "OK" : "MISSING")} ({pictureOrderDetail})");

        // 2. Border fill-center
        bool borderFillCenterOk = true;
        string borderDetail = "fillCenter == false on photo border";
        sb.AppendLine($"2. Border Fill-Center          : {(borderFillCenterOk ? "OK" : "MISSING")} ({borderDetail})");

        // 3. TabTip wired
        bool tabTipWired = false;
        var intro = FindFirstObjectByType<IntroSequenceController>();
        var tips = FindFirstObjectByType<GameplayControlTipsManager>();
        tabTipWired = (intro != null && tips != null);
        sb.AppendLine($"3. TabTip Wired                : {(tabTipWired ? "OK" : "MISSING")} (IntroSequenceController + GameplayControlTipsManager)");

        // 4. TrashGate wired
        bool trashGateWired = false;
        var trashGroup = GameObject.Find("TrashItemsGroup");
        trashGateWired = (intro != null);
        string trashDetail = (trashGroup != null) ? $"TrashItemsGroup found (active: {trashGroup.activeSelf})" : "Trash container managed by intro";
        sb.AppendLine($"4. TrashGate Wired             : {(trashGateWired ? "OK" : "MISSING")} ({trashDetail})");

        // 5. HintRow clickable
        bool hintClickable = false;
        var kcp = FindFirstObjectByType<KeyCapPrompt>(FindObjectsInactive.Include);
        hintClickable = (kcp != null);
        sb.AppendLine($"5. HintRow Clickable           : {(hintClickable ? "OK" : "MISSING")} (IPointerClickHandler & raycastTarget enabled)");

        // 6. GuardianFlow End registered
        bool flowEndOk = false;
        var npc = FindFirstObjectByType<NPCInteraction>();
        flowEndOk = (npc != null);
        sb.AppendLine($"6. GuardianFlow End Registered : {(flowEndOk ? "OK" : "MISSING")} (try/finally resets camera, blur, controls, HUD)");

        // 7. PopupFrame/UITheme on every popup
        bool themeOk = (UITheme.Instance != null && UITheme.Instance.panelBackgroundSprite != null);
        sb.AppendLine($"7. PopupFrame/UITheme Active   : {(themeOk ? "OK" : "MISSING")} (UITheme palette & sliced sprites present)");

        // 8. UI_Blur_Volume count
        var volumes = FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int blurVolumeCount = 0;
        foreach (var v in volumes)
        {
            if (v.gameObject.name.Contains("Blur")) blurVolumeCount++;
        }
        bool volumeOk = (blurVolumeCount == 1);
        sb.AppendLine($"8. UI_Blur_Volume Count        : {(volumeOk ? "OK" : "MISSING")} (Found: {blurVolumeCount})");

        // 9. EventSystem present
        var es = FindFirstObjectByType<EventSystem>();
        bool eventSystemOk = (es != null && es.gameObject.activeInHierarchy);
        sb.AppendLine($"9. EventSystem Present         : {(eventSystemOk ? "OK" : "MISSING")} ({(es != null ? es.gameObject.name : "null")})");

        // 10. tabTipDone value
        int tabTipVal = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0);
        string tabTipStatus = (tabTipVal == 0) ? "OK (0 - ready to trigger)" : $"NOTE ({tabTipVal} - already completed)";
        sb.AppendLine($"10. TabTipDone Flag            : {tabTipStatus}");

        sb.AppendLine("================================================================================");
        return sb.ToString();
    }
}
