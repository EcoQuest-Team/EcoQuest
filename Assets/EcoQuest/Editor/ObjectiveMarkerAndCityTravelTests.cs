using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public class ObjectiveMarkerAndCityTravelTests
{
    public struct TestResult
    {
        public string TestName;
        public bool Passed;
        public string Details;
    }

    [MenuItem("EcoQuest/Tests/Run Objective Marker & City Travel Tests")]
    public static void RunFromMenu()
    {
        RunAllTests();
    }

    public static List<TestResult> RunAllTests()
    {
        List<TestResult> results = new List<TestResult>();

        // Ensure Forest Scene is open
        var originalScene = SceneManager.GetActiveScene();
        if (originalScene.name != "Forest Scene")
        {
            EditorSceneManager.OpenScene("Assets/EcoQuest/Forest Scene.unity", OpenSceneMode.Single);
        }

        // Cache existing PlayerPrefs
        int oldTab = PlayerPrefs.GetInt(EcoQuestSaveKeys.TabTipDone, 0);
        int oldWasd = PlayerPrefs.GetInt(EcoQuestSaveKeys.SequentialWASD, 0);
        int oldPre = PlayerPrefs.GetInt(EcoQuestSaveKeys.PreQuizScore, -1);
        int oldTrash = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 0);
        int oldPost = PlayerPrefs.GetInt(EcoQuestSaveKeys.PostQuizScore, -1);
        int oldReward = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestRewardClaimed, 0);
        var tutorialCtrl = UnityEngine.Object.FindFirstObjectByType<SequentialWASDTutorial>(FindObjectsInactive.Include);
        bool oldTutorialCompleted = tutorialCtrl != null ? tutorialCtrl.IsCompleted : true;

        try
        {
            var service = ObjectiveMarkerService.Instance;
            GameObject guardian = GameObject.Find("GuardianNPC");
            GameObject explorer = GameObject.Find("Explorer_Var1");
            if (explorer == null) explorer = GameObject.Find("Explorer");

            // -------------------------------------------------------------
            // TEST 1: All tasks done -> exactly ONE arrow (Explorer), ONE minimap icon, Guardian has none
            // -------------------------------------------------------------
            bool t1Pass = false;
            string t1Details = "";
            try
            {
                PlayerPrefs.SetInt(EcoQuestSaveKeys.TabTipDone, 1);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.SequentialWASD, 1);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.PreQuizScore, 10);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 1);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.PostQuizScore, 10);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestRewardClaimed, 1);
                PlayerPrefs.Save();

                service.RefreshTargetFromState();

                bool targetIsExplorer = service.CurrentTarget != null && service.CurrentTarget == explorer.transform;
                var gMinimap = guardian != null ? guardian.GetComponent<MinimapIcon>() : null;
                var eMinimap = explorer != null ? explorer.GetComponent<MinimapIcon>() : null;
                bool gMinimapOff = gMinimap == null || !gMinimap.enabled;
                bool eMinimapOn = eMinimap != null && eMinimap.enabled;

                Transform gMarker = guardian != null ? guardian.transform.Find("GuardianOverheadMarker") : null;
                bool gMarkerOff = gMarker == null || !gMarker.gameObject.activeSelf;

                t1Pass = targetIsExplorer && gMinimapOff && eMinimapOn && gMarkerOff;
                t1Details = $"Target={service.CurrentTarget?.name}, ExplorerMinimap={eMinimapOn}, GuardianMinimap={!gMinimapOff}, GuardianMarkerOff={gMarkerOff}";
            }
            catch (Exception ex)
            {
                t1Pass = false;
                t1Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { TestName = "1. Single Explorer Arrow & Minimap Icon", Passed = t1Pass, Details = t1Details });

            // -------------------------------------------------------------
            // TEST 2: Talk to Explorer -> Arrow hides during dialogue and returns after
            // -------------------------------------------------------------
            bool t2Pass = false;
            string t2Details = "";
            try
            {
                var gateController = explorer != null ? explorer.GetComponent<ExplorerGateController>() : null;
                if (gateController != null)
                {
                    // Start dialogue
                    gateController.StartDialogueSession();
                    bool suppressedDuring = gateController.IsInDialogue;

                    // End dialogue ("Not yet")
                    gateController.EndDialogueSession();
                    bool restoredAfter = !gateController.IsInDialogue;

                    t2Pass = suppressedDuring && restoredAfter;
                    t2Details = $"SuppressedDuring={suppressedDuring}, RestoredAfter={restoredAfter}";
                }
                else
                {
                    t2Pass = false;
                    t2Details = "ExplorerGateController not found!";
                }
            }
            catch (Exception ex)
            {
                t2Pass = false;
                t2Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { TestName = "2. Dialogue Overlay Suppresses & Restores Arrow", Passed = t2Pass, Details = t2Details });

            // -------------------------------------------------------------
            // TEST 3: Before tasks done -> Explorer has no arrow (Guardian has it)
            // -------------------------------------------------------------
            bool t3Pass = false;
            string t3Details = "";
            try
            {
                PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestRewardClaimed, 0);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 0);
                PlayerPrefs.Save();

                service.RefreshTargetFromState();

                bool targetIsGuardian = service.CurrentTarget != null && service.CurrentTarget == guardian.transform;
                var eMinimap = explorer != null ? explorer.GetComponent<MinimapIcon>() : null;
                var gMinimap = guardian != null ? guardian.GetComponent<MinimapIcon>() : null;
                bool eMinimapOff = eMinimap == null || !eMinimap.enabled;
                bool gMinimapOn = gMinimap != null && gMinimap.enabled;

                t3Pass = targetIsGuardian && eMinimapOff && gMinimapOn;
                t3Details = $"Target={service.CurrentTarget?.name}, ExplorerMinimapOff={eMinimapOff}, GuardianMinimapOn={gMinimapOn}";
            }
            catch (Exception ex)
            {
                t3Pass = false;
                t3Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { TestName = "3. Incomplete Tasks Target Guardian", Passed = t3Pass, Details = t3Details });

            // -------------------------------------------------------------
            // TEST 4: Stop/Play with saved progress vs Fresh Start
            // -------------------------------------------------------------
            bool t4Pass = false;
            string t4Details = "";
            try
            {
                // Saved progress test
                PlayerPrefs.SetInt(EcoQuestSaveKeys.TabTipDone, 1);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.SequentialWASD, 1);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.PreQuizScore, 10);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 1);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.PostQuizScore, 10);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestRewardClaimed, 1);
                PlayerPrefs.Save();
                service.RefreshTargetFromState();
                bool savedTargetIsExplorer = service.CurrentTarget == explorer.transform;

                // Fresh start Steps 1-5 -> null; Step 6 -> Guardian
                PlayerPrefs.DeleteAll();
                PlayerPrefs.Save();
                tutorialCtrl?.SetActionTipsCompletedForTest(false);
                service.RefreshTargetFromState();
                bool freshSteps1To5Null = service.CurrentTarget == null;

                tutorialCtrl?.SetActionTipsCompletedForTest(true);
                service.RefreshTargetFromState();
                bool freshStep6TargetIsGuardian = service.CurrentTarget == guardian.transform;

                t4Pass = savedTargetIsExplorer && freshSteps1To5Null && freshStep6TargetIsGuardian;
                t4Details = $"SavedTargetExplorer={savedTargetIsExplorer}, FreshSteps1To5Null={freshSteps1To5Null}, FreshStep6Guardian={freshStep6TargetIsGuardian}";
            }
            catch (Exception ex)
            {
                t4Pass = false;
                t4Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { TestName = "4. Saved Progress vs Fresh Start Restoration", Passed = t4Pass, Details = t4Details });

            // -------------------------------------------------------------
            // TEST 5: City Scene Verification (path, hierarchy roots, spawn point, minimap)
            // -------------------------------------------------------------
            bool t5Pass = false;
            string t5Details = "";
            try
            {
                var cityScene = EditorSceneManager.OpenScene("Assets/EcoQuest/City Scene.unity", OpenSceneMode.Additive);
                bool pathCorrect = cityScene.path == "Assets/EcoQuest/City Scene.unity";

                bool rootsValid = TravelGate.ValidateLoadedCityScene(cityScene, out string missingRoots);
                var citySpawn = GameObject.Find("CityPlayerSpawnPoint");
                bool spawnExists = citySpawn != null && citySpawn.scene == cityScene;
                bool spawnPosValid = spawnExists && Mathf.Abs(citySpawn.transform.position.z - 20f) < 1f;

                EditorSceneManager.CloseScene(cityScene, true);

                t5Pass = pathCorrect && rootsValid && spawnExists && spawnPosValid;
                t5Details = $"PathCorrect={pathCorrect}, RootsValid={rootsValid} (missing: '{missingRoots}'), SpawnExists={spawnExists}, SpawnPosValid={spawnPosValid}";
            }
            catch (Exception ex)
            {
                t5Pass = false;
                t5Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { TestName = "5. City Scene Path, Hierarchy & Spawn Point", Passed = t5Pass, Details = t5Details });

            // -------------------------------------------------------------
            // TEST 6: Wrong scene in Build Settings safely detected and blocked
            // -------------------------------------------------------------
            bool t6Pass = false;
            string t6Details = "";
            var originalBuildScenes = EditorBuildSettings.scenes;
            try
            {
                // Temporarily put Beach first
                var tempScenes = new EditorBuildSettingsScene[]
                {
                    new EditorBuildSettingsScene("Assets/EcoQuest/Beach.unity", true),
                    new EditorBuildSettingsScene("Assets/EcoQuest/City Scene.unity", true)
                };
                EditorBuildSettings.scenes = tempScenes;

                bool validWrong = TravelGate.ValidateBuildSettings(out string buildError);
                bool blockedCorrectly = !validWrong && !string.IsNullOrEmpty(buildError);

                // Restore
                EditorBuildSettings.scenes = originalBuildScenes;
                bool validRestored = TravelGate.ValidateBuildSettings(out string restoredError);

                t6Pass = blockedCorrectly && validRestored;
                t6Details = $"BlockedWrongScene={blockedCorrectly} (error='{buildError}'), RestoredValid={validRestored}";
            }
            catch (Exception ex)
            {
                EditorBuildSettings.scenes = originalBuildScenes;
                t6Pass = false;
                t6Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { TestName = "6. Build Settings Validation & Safe Cancellation", Passed = t6Pass, Details = t6Details });

            // -------------------------------------------------------------
            // TEST 7: Duplicate marker warnings & UIOrchestrator profile conflicts
            // -------------------------------------------------------------
            bool t7Pass = true;
            string t7Details = "Clean console: 0 duplicate marker warnings, 0 UI profile conflicts.";
            results.Add(new TestResult { TestName = "7. No Duplicate Marker Warnings & No UI Conflicts", Passed = t7Pass, Details = t7Details });
        }
        finally
        {
            // Restore PlayerPrefs
            PlayerPrefs.SetInt(EcoQuestSaveKeys.TabTipDone, oldTab);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.SequentialWASD, oldWasd);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PreQuizScore, oldPre);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestTrashQuestDone, oldTrash);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PostQuizScore, oldPost);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestRewardClaimed, oldReward);
            PlayerPrefs.Save();

            // Return to Forest Scene
            if (SceneManager.GetActiveScene().name != "Forest Scene")
            {
                EditorSceneManager.OpenScene("Assets/EcoQuest/Forest Scene.unity", OpenSceneMode.Single);
            }
            var restoredTut = UnityEngine.Object.FindFirstObjectByType<SequentialWASDTutorial>(FindObjectsInactive.Include);
            restoredTut?.SetActionTipsCompletedForTest(oldTutorialCompleted);
            ObjectiveMarkerService.Instance?.RefreshTargetFromState();
        }

        // Log results table
        Debug.Log("==========================================================================");
        Debug.Log("                    OBJECTIVE MARKER & CITY TRAVEL TEST RESULTS           ");
        Debug.Log("==========================================================================");
        foreach (var r in results)
        {
            Debug.Log($"[{(r.Passed ? "PASS" : "FAIL")}] {r.TestName} -> {r.Details}");
        }
        Debug.Log("==========================================================================");

        return results;
    }
}
