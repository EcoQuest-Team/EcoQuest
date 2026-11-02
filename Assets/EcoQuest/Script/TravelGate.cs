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
        if (SceneTransition.Instance != null)
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
        // Wait one frame for City scene objects to run Awake/Start
        yield return null;

        // Position Player at CityPlayerSpawnPoint if present
        GameObject spawnPoint = GameObject.Find(CitySpawnPointName);
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) player = GameObject.Find("Player");

        if (spawnPoint != null && player != null)
        {
            // Disable CharacterController temporarily if present to avoid snapping collision overrides
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            player.transform.position = spawnPoint.transform.position;
            player.transform.rotation = spawnPoint.transform.rotation;

            if (cc != null) cc.enabled = true;
            Debug.Log($"[TravelGate] Player positioned at City spawn point {spawnPoint.transform.position}.");
        }

        // Reset static state across scene change
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

        // Restore gameplay HUD profile
        UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.QuestActive, "Arrived in City");
        SetPlayerMovementFrozen(false);

        // Show welcome notification
        ExplorerGateNotification.ShowImmediateNotification("Welcome to Eco-City! Explore and talk to the citizens.");
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
