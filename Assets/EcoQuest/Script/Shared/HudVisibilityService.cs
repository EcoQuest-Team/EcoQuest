using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Centralized single source of truth for HUD visibility across all scenes.
/// Reference-counted per reason (modal, tutorial, cutscene, dialogue).
/// HUD is visible if and only if reason count is zero.
/// Includes 1-second watchdog safety to force-restore any stuck hidden HUD elements.
/// Dev-only F5 key forces complete UI restore with on-screen toast.
/// </summary>
public class HudVisibilityService : MonoBehaviour
{
    private static HudVisibilityService instance;
    public static HudVisibilityService Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<HudVisibilityService>(FindObjectsInactive.Include);
                if (instance == null)
                {
                    var go = GameObject.Find("[HudVisibilityService]");
                    if (go == null) go = new GameObject("[HudVisibilityService]");
                    if (Application.isPlaying && go.transform.parent == null) DontDestroyOnLoad(go);
                    instance = go.GetComponent<HudVisibilityService>() ?? go.AddComponent<HudVisibilityService>();
                }
            }
            return instance;
        }
    }

    [Header("Tracked HUD Elements")]
    [SerializeField] private List<CanvasGroup> hudCanvasGroups = new List<CanvasGroup>();

    [Header("Watchdog Settings")]
    [SerializeField] private bool watchdogEnabled = true;
    public bool WatchdogEnabled { get => watchdogEnabled; set => watchdogEnabled = value; }

    private readonly HashSet<string> activeHideReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<CanvasGroup> ownedElements = new HashSet<CanvasGroup>();
    private readonly Dictionary<CanvasGroup, float> lastRestoreTimes = new Dictionary<CanvasGroup, float>();
    private readonly Dictionary<CanvasGroup, float> lastLogTimes = new Dictionary<CanvasGroup, float>();

    private Coroutine activeFadeRoutine;
    private Coroutine watchdogRoutine;
    private float currentHudAlpha = 1f;

    public void ShowToast(string message, float duration = 2.5f)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        toastMessage = message;
        toastEndTime = Time.unscaledTime + duration;
        Debug.Log($"[Toast] {message}");
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private string toastMessage = null;
    private float toastEndTime = 0f;
#endif

    public bool IsHUDHidden => activeHideReasons.Count > 0;
    public int HideReasonCount => activeHideReasons.Count;
    public IEnumerable<string> ActiveReasons => activeHideReasons;

    public void RegisterOwnedElement(CanvasGroup cg)
    {
        if (cg != null && !ownedElements.Contains(cg))
        {
            ownedElements.Add(cg);
        }
    }

    public void UnregisterOwnedElement(CanvasGroup cg)
    {
        if (cg != null)
        {
            ownedElements.Remove(cg);
        }
    }

    public bool IsElementOwned(CanvasGroup cg)
    {
        return cg != null && ownedElements.Contains(cg);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize()
    {
        if (instance == null)
        {
            var go = new GameObject("[HudVisibilityService]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<HudVisibilityService>();
        }
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
        ResolveHudElements();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
        activeHideReasons.Clear();
    }

    private void OnDisable()
    {
        activeHideReasons.Clear();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        activeHideReasons.Clear();
        ResolveHudElements();
        if (UIOrchestrator.Instance != null)
        {
            UIOrchestrator.Instance.ApplyCurrentProfile(0f);
        }
    }

    private void Start()
    {
        ResolveHudElements();
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.F5))
        {
            ForceRestoreUI();
        }
#endif
    }

    /// <summary>
    /// Resolves and caches all core HUD CanvasGroups across both Forest and City scenes.
    /// </summary>
    public void ResolveHudElements()
    {
        hudCanvasGroups.Clear();

        string[] hudPaths = new string[]
        {
            "--- UI ---/UI_HUD_SettingsButton",
            "--- UI ---/UI_Quest/QuestPanel",
            "--- UI ---/UI_Coins",
            "--- UI ---/UI_Minimap",
            "--- UI ---/UI_Crosshair",
            "--- UI ---/UI_Notification/Control Tip Banner"
        };

        var uiRoot = GameObject.Find("--- UI ---");
        if (uiRoot != null)
        {
            foreach (var p in hudPaths)
            {
                var sub = p.Substring("--- UI ---/".Length);
                var t = uiRoot.transform.Find(sub);
                if (t != null)
                {
                    var cg = t.GetComponent<CanvasGroup>();
                    if (cg == null) cg = t.gameObject.AddComponent<CanvasGroup>();
                    if (!hudCanvasGroups.Contains(cg)) hudCanvasGroups.Add(cg);

                    if (t.gameObject.name == "UI_HUD_SettingsButton" || 
                        t.gameObject.name == "Control Tip Banner" || 
                        t.gameObject.name == "QuestPanel")
                    {
                        RegisterOwnedElement(cg);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Requests HUD to be hidden for a specific reason (e.g. "Modal", "Dialogue", "Quiz", "Cutscene").
    /// </summary>
    public void HideHUD(string reason, float duration = 0.2f)
    {
        if (string.IsNullOrWhiteSpace(reason)) reason = "Default";
        activeHideReasons.Add(reason);
#if UNITY_EDITOR
        Debug.Log($"[FlowTrace] HudVisibilityService.HideHUD('{reason}') -> activeReasons=[{string.Join(",", activeHideReasons)}]");
#endif
        ApplyVisibility(duration);
    }

    /// <summary>
    /// Releases a hide reason for HUD.
    /// </summary>
    public void ShowHUD(string reason, float duration = 0.2f)
    {
        if (string.IsNullOrWhiteSpace(reason)) reason = "Default";
        activeHideReasons.Remove(reason);
#if UNITY_EDITOR
        Debug.Log($"[FlowTrace] HudVisibilityService.ShowHUD('{reason}') -> activeReasons=[{string.Join(",", activeHideReasons)}]");
#endif
        ApplyVisibility(duration);
    }

    /// <summary>
    /// Clears all hide reasons and brings HUD back to full opacity.
    /// </summary>
    public void ClearAllReasons(float duration = 0.2f)
    {
        activeHideReasons.Clear();
        ApplyVisibility(duration);
    }

    private void ApplyVisibility(float duration)
    {
        bool shouldBeVisible = (activeHideReasons.Count == 0);
        currentHudAlpha = shouldBeVisible ? 1f : 0f;

        // UIOrchestrator is the single HUD authority: event-driven profile update
        if (UIOrchestrator.Instance != null)
        {
            UIOrchestrator.Instance.ApplyCurrentProfile(duration);
        }
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration)
    {
        float startAlpha = currentHudAlpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            currentHudAlpha = Mathf.Lerp(startAlpha, targetAlpha, t);

            for (int i = 0; i < hudCanvasGroups.Count; i++)
            {
                var cg = hudCanvasGroups[i];
                if (cg != null)
                {
                    cg.alpha = currentHudAlpha;
                }
            }
            yield return null;
        }

        currentHudAlpha = targetAlpha;
        bool isInteractive = (targetAlpha > 0.5f);

        for (int i = 0; i < hudCanvasGroups.Count; i++)
        {
            var cg = hudCanvasGroups[i];
            if (cg != null)
            {
                cg.alpha = targetAlpha;
                cg.interactable = isInteractive;
                cg.blocksRaycasts = isInteractive;
            }
        }

        activeFadeRoutine = null;
    }

    public void SetHUDVisibleImmediate(bool visible)
    {
        if (activeFadeRoutine != null)
        {
            DialoguePresenter.StopRunningCoroutine(activeFadeRoutine);
            activeFadeRoutine = null;
        }

        currentHudAlpha = visible ? 1f : 0f;
        if (UIOrchestrator.Instance != null)
        {
            UIOrchestrator.Instance.ApplyCurrentProfile(0f);
        }
    }

    /// <summary>
    /// Watchdog disabled: UIOrchestrator is the single HUD authority.
    /// </summary>
    private IEnumerator WatchdogLoop()
    {
        yield break;
    }

    /// <summary>
    /// Dev-only F5 key: Force Restore UI (restores HUD, input map, cursor, camera, Time.timeScale).
    /// </summary>
    public void ForceRestoreUI()
    {
        activeHideReasons.Clear();
        ResolveHudElements();
        if (UIOrchestrator.Instance != null)
        {
            UIOrchestrator.Instance.ApplyCurrentProfile(0f);
        }

        // Restore modal manager if open
        if (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalOpen)
        {
            ModalManager.Instance.ForceCloseAllAndRestore();
        }

        // Restore dialogue presenter if open
        var dp = FindFirstObjectByType<DialoguePresenter>(FindObjectsInactive.Include);
        if (dp != null && dp.IsInDialogue)
        {
            dp.CloseDialogue();
        }

        // Restore quiz if open
        var quiz = FindFirstObjectByType<QuizUIController>(FindObjectsInactive.Include);
        if (quiz != null && QuizUIController.IsAnyQuizOpen)
        {
            quiz.HideImmediate();
        }

        // Restore time scale
        Time.timeScale = 1f;

        // Restore cursor and input map
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        var inputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
        if (inputs != null)
        {
            inputs.cursorLocked = true;
            inputs.cursorInputForLook = true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        toastMessage = "[F5] Force Restored UI, HUD, Inputs & TimeScale";
        toastEndTime = Time.unscaledTime + 2.5f;
        Debug.Log(toastMessage);
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnGUI()
    {
        if (Time.unscaledTime < toastEndTime && !string.IsNullOrEmpty(toastMessage))
        {
            var oldColor = GUI.color;
            GUI.color = new Color(0.1f, 0.9f, 0.3f, 1f);
            GUI.Box(new Rect((Screen.width - 450) * 0.5f, 24, 450, 36), toastMessage);
            GUI.color = oldColor;
        }
    }
#endif
}
