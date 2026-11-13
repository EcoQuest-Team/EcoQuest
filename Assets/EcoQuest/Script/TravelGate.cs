using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single source of truth for World 1 -> World 2 gating and travel control.
/// Enforces that the City scene can ONLY be loaded when:
/// 1. Tutorial is complete (TabTipDone, SequentialWASD, PreQuizScore).
/// 2. Trash quest is complete (all waste sorted).
/// 3. Post-quiz is finished and results/reward claimed.
/// All entry points to City scene must route through TravelGate.TryTravelToCity().
/// </summary>
public class TravelGate : MonoBehaviour
{
    private static TravelGate instance;
    public static TravelGate Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<TravelGate>();
                if (instance == null && Application.isPlaying)
                {
                    GameObject go = new GameObject("[TravelGate]");
                    if (go.transform.parent == null) DontDestroyOnLoad(go);
                    instance = go.AddComponent<TravelGate>();
                }
            }
            return instance;
        }
    }

    public const string CitySceneName = "City Scene";
    public const string CitySpawnPointName = "CityPlayerSpawnPoint";

    [Header("Scene Configuration")]
    [Tooltip("Full asset path to the World 2 City scene.")]
    [SerializeField]
    private string citySceneAssetPath = "Assets/EcoQuest/City Scene.unity";
    public string CitySceneAssetPath => citySceneAssetPath;

    [Header("Loading Screen Settings")]
    [Tooltip("Whether to use the unified EcoQuest Loading Screen during travel to City.")]
    [SerializeField] private bool useLoadingScreen = true;
    public bool UseLoadingScreen { get => useLoadingScreen; set => useLoadingScreen = value; }

    [Tooltip("Optional City loading artwork override. If unassigned, defaults to Forest art.")]
    [SerializeField] private Sprite cityLoadingArt = null;
    public Sprite CityLoadingArt { get => cityLoadingArt; set => cityLoadingArt = value; }

    public static readonly string[] RequiredCityRootObjects = new string[]
    {
        "--- GAMEPLAY ---",
        "--- MANAGERS ---",
        "--- UI ---",
        "NavMesh Surface"
    };

    public static event Action OnGateUnlocked;
    public static event Action OnTravelStarted;

    private static bool isTraveling = false;
    public static bool IsTraveling => isTraveling;

    // -------------------------------------------------------------------------
    // Single Source of Truth Gating Properties
    // -------------------------------------------------------------------------

    public static bool IsTutorialComplete
    {
        get
        {
            bool tabDone = PlayerPrefs.GetInt(EcoQuestSaveKeys.TabTipDone, 0) == 1;
            bool wasdDone = PlayerPrefs.GetInt(EcoQuestSaveKeys.SequentialWASD, 0) == 1
                            || SequentialWASDTutorial.IsTutorialCompleted;
            bool preQuizDone = PlayerPrefs.GetInt(EcoQuestSaveKeys.PreQuizScore, -1) >= 0;
            return tabDone && wasdDone && preQuizDone;
        }
    }

    public static bool IsTrashQuestComplete
    {
        get
        {
            return QuestManager.IsQuestComplete
                   || PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 0) == 1;
        }
    }

    public static bool IsPostQuizAndRewardClaimed
    {
        get
        {
            bool postQuizDone = PlayerPrefs.GetInt(EcoQuestSaveKeys.PostQuizScore, -1) >= 0;
            bool rewardClaimed = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestRewardClaimed, 0) == 1;
            return postQuizDone && rewardClaimed;
        }
    }

    /// <summary>
    /// Strict single source of truth property for whether the gate to World 2 is open.
    /// </summary>
    public static bool CanTravelToCity
    {
        get
        {
            return IsTutorialComplete && IsTrashQuestComplete && IsPostQuizAndRewardClaimed;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        if (transform.parent == null)
        {
            DontDestroyOnLoad(gameObject);
        }
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            instance = null;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8))
        {
            RunPostTravelHealthCheck(out string report);
            Debug.Log($"[Post-Travel Health Check (F8)] {report}");
        }
    }

    public static void ResetStaticState()
    {
        isTraveling = false;
        Debug.Log("[TravelGate] Static state reset.");
    }

    // -------------------------------------------------------------------------
    // Progress Notification Hooks
    // -------------------------------------------------------------------------

    public static void NotifyTrashQuestCompleted()
    {
        PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 1);
        PlayerPrefs.Save();
        Debug.Log("[TravelGate] Trash quest completion saved. CanTravelToCity=" + CanTravelToCity);
        CheckGateUnlocked();
    }

    public static void NotifyRewardClaimed()
    {
        PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestRewardClaimed, 1);
        PlayerPrefs.Save();
        Debug.Log("[TravelGate] Reward claim saved. CanTravelToCity=" + CanTravelToCity);
        CheckGateUnlocked();
    }

    private static void CheckGateUnlocked()
    {
        if (CanTravelToCity)
        {
            Debug.Log("[TravelGate] Gate to World 2 is now UNLOCKED!");
            OnGateUnlocked?.Invoke();
        }
    }

    // -------------------------------------------------------------------------
    // Safe Scene Transition Guard
    // -------------------------------------------------------------------------

    /// <summary>
    /// Guarded method for traveling to City. Checks gating conditions and initiates async transition.
    /// Returns true if travel sequence initiated, false if blocked.
    /// </summary>
    public static bool TryTravelToCity(string caller = "Direct")
    {
        if (isTraveling)
        {
            Debug.LogWarning($"[TravelGate] Travel request from '{caller}' ignored: transition already in progress.");
            return false;
        }

        if (!CanTravelToCity)
        {
            Debug.LogWarning($"[TravelGate] Travel blocked for caller '{caller}': Forest tasks incomplete! " +
                             $"(Tutorial: {IsTutorialComplete}, Quest: {IsTrashQuestComplete}, PostQuizReward: {IsPostQuizAndRewardClaimed})");
            return false;
        }

        if (!ValidateBuildSettings(out string buildError))
        {
            Debug.LogError($"[TravelGate] Cannot travel to City: {buildError}");
            return false;
        }

        Debug.Log($"[TravelGate] Travel approved for caller '{caller}'. Starting transition to '{CitySceneName}'...");
        if (Application.isPlaying && Instance != null)
        {
            Instance.StartCoroutine(Instance.TravelTransitionRoutine());
        }
        return true;
    }

    private IEnumerator TravelTransitionRoutine()
    {
        isTraveling = true;
        OnTravelStarted?.Invoke();

        // 1. Hide HUD via UIOrchestrator profile and freeze player controls
        UIOrchestrator.Instance?.EnterCinematicProfile("TravelGate");
        SetPlayerMovementFrozen(true);

        // 2. Fade out forest audio
        if (AudioManager.Instance != null && AudioManager.Instance.musicSource != null)
        {
            StartCoroutine(FadeOutAudioSource(AudioManager.Instance.musicSource, 1.2f));
        }

        // 3. Trigger SceneTransition leaf wipe / loading screen
        if (useLoadingScreen)
        {
            LoadingScreen.Load(CitySceneName, cityLoadingArt);
        }
        else if (SceneTransition.Instance != null)
        {
            SceneTransition.Instance.LoadScene(CitySceneName);
        }
        else
        {
            // Fallback direct async load if SceneTransition prefab is absent
            AsyncOperation asyncOp = null;
            try
            {
                asyncOp = SceneManager.LoadSceneAsync(CitySceneName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TravelGate] Exception loading scene '{CitySceneName}': {ex.Message}");
            }

            if (asyncOp == null)
            {
                Debug.LogError($"[TravelGate] Failed to start async load for '{CitySceneName}'! Restoring player.");
                SetPlayerMovementFrozen(false);
                UIOrchestrator.Instance?.ExitCinematicProfile("TravelGateFail");
                isTraveling = false;
                yield break;
            }

            while (!asyncOp.isDone)
            {
                yield return null;
            }
        }
    }

    public static bool ValidateBuildSettings(out string errorMessage)
    {
        errorMessage = "";
#if UNITY_EDITOR
        var scenes = UnityEditor.EditorBuildSettings.scenes;
        if (scenes == null || scenes.Length == 0)
        {
            errorMessage = "No scenes configured in EditorBuildSettings!";
            return false;
        }

        // Expected order: Forest Scene at Index 0, City Scene at Index 1
        if (scenes[0].path != "Assets/EcoQuest/Forest Scene.unity" || !scenes[0].enabled)
        {
            errorMessage = $"Expected 'Assets/EcoQuest/Forest Scene.unity' as enabled scene at index 0, but found '{scenes[0].path}' (enabled={scenes[0].enabled})!";
            return false;
        }

        bool cityFound = false;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (scenes[i].path == "Assets/EcoQuest/City Scene.unity" && scenes[i].enabled)
            {
                cityFound = true;
                break;
            }
        }
        if (!cityFound)
        {
            errorMessage = "Required scene 'Assets/EcoQuest/City Scene.unity' is missing or disabled in EditorBuildSettings!";
            return false;
        }
#else
        int cityIndex = SceneUtility.GetBuildIndexByScenePath("Assets/EcoQuest/City Scene.unity");
        if (cityIndex < 0)
        {
            errorMessage = "Required scene 'Assets/EcoQuest/City Scene.unity' is not in build list!";
            return false;
        }
#endif
        return true;
    }

    public static bool ValidateLoadedCityScene(Scene scene, out string missingRoots)
    {
        missingRoots = "";
        GameObject[] roots = scene.GetRootGameObjects();
        System.Collections.Generic.List<string> missing = new System.Collections.Generic.List<string>();
        foreach (string req in RequiredCityRootObjects)
        {
            bool found = false;
            foreach (GameObject r in roots)
            {
                if (r != null && r.name == req)
                {
                    found = true;
                    break;
                }
            }
            if (!found) missing.Add(req);
        }

        if (missing.Count > 0)
        {
            missingRoots = string.Join(", ", missing);
            return false;
        }
        return true;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == CitySceneName || scene.path == "Assets/EcoQuest/City Scene.unity")
        {
            Debug.Log($"[TravelGate] City scene loaded ({scene.path}). Validating roots...");
            if (!ValidateLoadedCityScene(scene, out string missingRoots))
            {
                Debug.LogError($"[TravelGate] Scene root validation failed! Missing: {missingRoots}");
            }
            else
            {
                Debug.Log("[TravelGate] Scene root validation PASSED.");
            }
            ObjectiveMarkerService.Instance?.ClearTarget();
            StartCoroutine(PostCityLoadSetupRoutine());
        }
    }

    private IEnumerator PostCityLoadSetupRoutine()
    {
        // 1. Wait two frames for City scene objects (managers, UI, player) to complete Awake and Start
        yield return null;
        yield return null;

        // 2. Remove duplicate managers safely
        CleanDuplicateManagers();

        // 3. Position Player at CityPlayerSpawnPoint if present
        GameObject spawnPoint = GameObject.Find(CitySpawnPointName);
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) player = GameObject.Find("Player");

        if (spawnPoint != null && player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            player.transform.position = spawnPoint.transform.position;
            player.transform.rotation = spawnPoint.transform.rotation;

            if (cc != null) cc.enabled = true;
            Debug.Log($"[TravelGate] Player positioned at City spawn point {spawnPoint.transform.position}.");
        }

        // 4. Clear all overlays and stale HUD state
        UIOrchestrator.Instance?.ClearAllOverlays();
        UIOrchestrator.Instance?.SetQuestPanelUserHidden(false, "PostCityLoadSetupRoutine");
        HudVisibilityService.Instance?.ClearAllReasons();
        ModalManager.Instance?.CloseModal(null);
        ObjectiveMarkerService.Instance?.ClearTarget();
        FindFirstObjectByType<ExplorerCinematicController>()?.ResetLetterboxImmediate();
        NPCInteraction.ResetStaticFlowState();
        TrashItem.ResetStaticState();
        TrashItem.AreTrashItemsAllowed = false;
        DragAndDrop.ResetStaticState();
        QuestManager.ResetAllQuestCountsToZero();
        QuestManager.ResetStaticState();
        ModalManager.ClearPersistedSeenCards();
        LitterbugQuestManager.ResetStaticState();
        SequentialWASDTutorial.AllowedState = SequentialWASDTutorial.TutorialState.All;
        ResetStaticState();
        isTraveling = false;

        // 5. Rebind UIOrchestrator and HudVisibilityService to the City HUD canvases, minimap, and settings button
        UIOrchestrator.Instance?.ResolveReferences();
        HudVisibilityService.Instance?.ResolveHudElements();

        // 6. Apply the correct gameplay HUD profile once
        UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "City SceneReady");
        UIOrchestrator.Instance?.SetPhase(UIOrchestrator.Instance.EvaluateLivePhase("City SceneReady"), "City SceneReady");
        UIOrchestrator.Instance?.ApplyCurrentProfile(0f);

        // 7. Restore input, cursor, timeScale
        Time.timeScale = 1f;
        CursorState.SetLocked(true);
        SetPlayerMovementFrozen(false);

        // 8. Run Post-Travel Health Check
        RunPostTravelHealthCheck(out string healthReport);
        Debug.Log($"[Post-Travel Health Check] {healthReport}");

        // 9. Show welcome notification
        ExplorerGateNotification.ShowImmediateNotification("Welcome to Eco-City! Explore and talk to the citizens.");
    }

    public static void CleanDuplicateManagers()
    {
        var eventSystems = UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (eventSystems.Length > 1)
        {
            for (int i = 1; i < eventSystems.Length; i++)
            {
                if (eventSystems[i] != null)
                {
                    Debug.Log($"[TravelGate] Destroying duplicate EventSystem '{eventSystems[i].name}'");
                    UnityEngine.Object.Destroy(eventSystems[i].gameObject);
                }
            }
        }

        var audioListeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (audioListeners.Length > 1)
        {
            for (int i = 1; i < audioListeners.Length; i++)
            {
                if (audioListeners[i] != null)
                {
                    Debug.Log($"[TravelGate] Disabling duplicate AudioListener on '{audioListeners[i].name}'");
                    audioListeners[i].enabled = false;
                }
            }
        }
    }

    public static bool RunPostTravelHealthCheck(out string details)
    {
        var issues = new System.Collections.Generic.List<string>();

        // 1. Minimap
        var map = GameObject.Find("UI_Minimap");
        var mapCg = map != null ? map.GetComponent<CanvasGroup>() : null;
        if (map == null || !map.activeInHierarchy || (mapCg != null && mapCg.alpha < 0.5f))
        {
            issues.Add($"Minimap missing or hidden (map={map != null}, alpha={(mapCg != null ? mapCg.alpha : 0f):F2})");
        }

        // 2. Settings button
        var gear = GameObject.Find("UI_HUD_SettingsButton");
        var gearCg = gear != null ? gear.GetComponent<CanvasGroup>() : null;
        if (gear == null || !gear.activeInHierarchy || (gearCg != null && gearCg.alpha < 0.5f))
        {
            issues.Add($"Settings button missing or hidden (gear={gear != null}, alpha={(gearCg != null ? gearCg.alpha : 0f):F2})");
        }

        // 3. Coins
        var coins = GameObject.Find("UI_Coins");
        var coinsCg = coins != null ? coins.GetComponent<CanvasGroup>() : null;
        if (coins == null || !coins.activeInHierarchy || (coinsCg != null && coinsCg.alpha < 0.5f))
        {
            issues.Add($"Coins missing or hidden (coins={coins != null}, alpha={(coinsCg != null ? coinsCg.alpha : 0f):F2})");
        }

        // 4. Overlays & HUD hidden reasons
        if (UIOrchestrator.Instance != null && UIOrchestrator.Instance.IsOverlayActive)
        {
            issues.Add("UIOrchestrator overlay stack is not empty!");
        }

        if (HudVisibilityService.Instance != null && HudVisibilityService.Instance.IsHUDHidden)
        {
            issues.Add($"HudVisibilityService has active hide reasons: {string.Join(",", HudVisibilityService.Instance.ActiveReasons)}");
        }

        // 5. Time scale
        if (Time.timeScale != 1f)
        {
            issues.Add($"Time.timeScale is {Time.timeScale} (expected 1.0)");
        }

        // 6. Cursor & Player Input
        if (!LitterbugQuestManager.IsDialogueOpen)
        {
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                issues.Add($"Cursor lockState is {Cursor.lockState} (expected Locked outside dialogue)");
            }
            var fpc = UnityEngine.Object.FindFirstObjectByType<StarterAssets.FirstPersonController>();
            if (fpc != null && !fpc.enabled)
            {
                issues.Add("FirstPersonController is disabled outside dialogue");
            }
        }

        // 7. Duplicate managers
        var evCount = UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length;
        if (evCount > 1)
        {
            issues.Add($"Duplicate EventSystems ({evCount})");
        }

        var alCount = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
        if (alCount > 1)
        {
            issues.Add($"Duplicate active AudioListeners ({alCount})");
        }

        var qmCount = UnityEngine.Object.FindObjectsByType<LitterbugQuestManager>(FindObjectsSortMode.None).Length;
        if (qmCount > 1)
        {
            issues.Add($"Duplicate LitterbugQuestManagers ({qmCount})");
        }

        // 8. Quest Manager present in City
        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        bool isCity = activeScene.name == "City Scene" || activeScene.path.Contains("City Scene");
        if (isCity)
        {
            if (LitterbugQuestManager.Instance == null)
            {
                issues.Add("LitterbugQuestManager missing in City Scene!");
            }
            else
            {
                // 9. Quest Panel visibility matching phase
                var uiRoot = GameObject.Find("--- UI ---");
                var questPanelTr = uiRoot != null ? (uiRoot.transform.Find("UI_LitterbugQuest/QuestPanel") ?? uiRoot.transform.Find("UI_Quest/QuestPanel")) : null;
                var questPanel = questPanelTr != null ? questPanelTr.gameObject : GameObject.Find("QuestPanel");
                if (questPanel == null)
                {
                    issues.Add("Quest panel GameObject not found in City Scene!");
                }
                else
                {
                    var qpCg = questPanel.GetComponent<CanvasGroup>();
                    var qState = LitterbugQuestManager.Instance.QuestState;
                    bool isQuestActive = (qState == LitterbugQuestState.Active || qState == LitterbugQuestState.ReadyToTurnIn);
                    bool userHidden = UIOrchestrator.Instance != null && UIOrchestrator.Instance.IsQuestPanelUserHidden;
                    bool shouldBeVisible = isQuestActive && !userHidden && !LitterbugQuestManager.IsDialogueOpen;
                    bool isVisible = questPanel.activeInHierarchy && (qpCg == null || qpCg.alpha > 0.5f);

                    if (shouldBeVisible && !isVisible && !LitterbugQuestManager.Instance.IsPanelAnimating)
                    {
                        issues.Add($"Quest panel should be VISIBLE for phase {qState} but is hidden (alpha={(qpCg != null ? qpCg.alpha : 0f):F2})");
                    }
                    else if (!shouldBeVisible && isVisible && !isQuestActive && !LitterbugQuestManager.Instance.IsPanelAnimating)
                    {
                        issues.Add($"Quest panel should be HIDDEN before quest start (phase {qState}) but is visible (alpha={(qpCg != null ? qpCg.alpha : 0f):F2})");
                    }
                }
            }
        }

        bool passed = issues.Count == 0;
        details = passed ? "PASS: All post-travel checks passed cleanly." : ("FAIL: " + string.Join(" | ", issues));
        return passed;
    }

    private static void SetPlayerMovementFrozen(bool frozen)
    {
        var fpc = FindFirstObjectByType<StarterAssets.FirstPersonController>();
        if (fpc != null)
        {
            fpc.enabled = !frozen;
        }

        var inputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
        if (inputs != null && frozen)
        {
            inputs.MoveInput(Vector2.zero);
        }
    }

    private IEnumerator FadeOutAudioSource(AudioSource src, float duration)
    {
        if (src == null) yield break;
        float startVol = src.volume;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(startVol, 0f, elapsed / duration);
            yield return null;
        }
        src.volume = 0f;
        src.Stop();
    }
}
