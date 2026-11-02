#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Automated acceptance test suite for the Explorer Gate feature.
/// Verifies Gating truth table, forced call blockage, AutoReset integration,
/// scene spawn point existence, UIOrchestrator profile, and component configurations.
/// </summary>
public static class ExplorerGateAutomatedTests
{
    public struct TestResult
    {
        public string TestName;
        public bool Passed;
        public string Details;
    }

    [MenuItem("EcoQuest/Run Explorer Gate Acceptance Tests", false, 100)]
    public static List<TestResult> RunAllTests()
    {
        var results = new List<TestResult>();
        Debug.Log("=================================================");
        Debug.Log("RUNNING EXPLORER GATE ACCEPTANCE TEST SUITE");
        Debug.Log("=================================================");

        // Backup current PlayerPrefs state
        int origTab = PlayerPrefs.GetInt(EcoQuestSaveKeys.TabTipDone, -99);
        int origWasd = PlayerPrefs.GetInt(EcoQuestSaveKeys.SequentialWASD, -99);
        int origPre = PlayerPrefs.GetInt(EcoQuestSaveKeys.PreQuizScore, -99);
        int origTrash = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestTrashQuestDone, -99);
        int origPost = PlayerPrefs.GetInt(EcoQuestSaveKeys.PostQuizScore, -99);
        int origReward = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestRewardClaimed, -99);
        int origNotif = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestExplorerGateNotified, -99);

        try
        {
            // Test 1: Fresh start gating & forced travel block
            EcoQuestSaveKeys.ClearAllTutorialProgress();
            TravelGate.ResetStaticState();
            bool t1Pass = !TravelGate.CanTravelToCity && !TravelGate.IsTutorialComplete &&
                          !TravelGate.IsTrashQuestComplete && !TravelGate.IsPostQuizAndRewardClaimed;
            bool t1Blocked = !TravelGate.TryTravelToCity("AutomatedTest_FreshStart");
            results.Add(new TestResult
            {
                TestName = "1. Fresh Start: Gate Closed & Travel Blocked",
                Passed = t1Pass && t1Blocked,
                Details = $"CanTravelToCity={TravelGate.CanTravelToCity}, TryTravelToCity blocked={t1Blocked}"
            });

            // Test 2: Tutorial complete, quest incomplete
            PlayerPrefs.SetInt(EcoQuestSaveKeys.TabTipDone, 1);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.SequentialWASD, 1);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PreQuizScore, 5);
            PlayerPrefs.DeleteKey(EcoQuestSaveKeys.ForestTrashQuestDone);
            PlayerPrefs.DeleteKey(EcoQuestSaveKeys.ForestRewardClaimed);
            PlayerPrefs.Save();
            bool t2Pass = TravelGate.IsTutorialComplete && !TravelGate.IsTrashQuestComplete &&
                          !TravelGate.CanTravelToCity && !TravelGate.TryTravelToCity("AutomatedTest_TutorialOnly");
            results.Add(new TestResult
            {
                TestName = "2. Tutorial Done, Quest Incomplete: Gate Closed",
                Passed = t2Pass,
                Details = $"IsTutorialComplete={TravelGate.IsTutorialComplete}, IsTrashQuestComplete={TravelGate.IsTrashQuestComplete}, CanTravel={TravelGate.CanTravelToCity}"
            });

            // Test 3: Quest complete, post-quiz incomplete
            TravelGate.NotifyTrashQuestCompleted();
            bool t3Pass = TravelGate.IsTrashQuestComplete && !TravelGate.IsPostQuizAndRewardClaimed &&
                          !TravelGate.CanTravelToCity && !TravelGate.TryTravelToCity("AutomatedTest_QuestOnly");
            results.Add(new TestResult
            {
                TestName = "3. Quest Done, Post-Quiz Incomplete: Gate Closed",
                Passed = t3Pass,
                Details = $"IsTrashQuestComplete={TravelGate.IsTrashQuestComplete}, IsPostQuizAndRewardClaimed={TravelGate.IsPostQuizAndRewardClaimed}, CanTravel={TravelGate.CanTravelToCity}"
            });

            // Test 4: All complete -> Gate unlocked
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PostQuizScore, 5);
            TravelGate.NotifyRewardClaimed();
            bool t4Pass = TravelGate.CanTravelToCity && TravelGate.IsTutorialComplete &&
                          TravelGate.IsTrashQuestComplete && TravelGate.IsPostQuizAndRewardClaimed;
            results.Add(new TestResult
            {
                TestName = "4. All Complete: Gate Unlocked",
                Passed = t4Pass,
                Details = $"CanTravelToCity={TravelGate.CanTravelToCity} (Tut={TravelGate.IsTutorialComplete}, Quest={TravelGate.IsTrashQuestComplete}, Quiz={TravelGate.IsPostQuizAndRewardClaimed})"
            });

            // Test 5: Forced travel refusal when gate closed
            ExplorerGateDebugTools.ResetGate();
            bool t5Pass = !TravelGate.CanTravelToCity && !TravelGate.TryTravelToCity("AutomatedTest_ForcedClosed");
            results.Add(new TestResult
            {
                TestName = "5. Forced Call Refusal When Gate Closed",
                Passed = t5Pass,
                Details = $"Refused call successfully when CanTravelToCity={TravelGate.CanTravelToCity}"
            });

            // Test 6: AutoReset integration clears all gate keys
            ExplorerGateDebugTools.CompleteForestTasks();
            bool wasOpenBefore = TravelGate.CanTravelToCity;
            EcoQuestEditorAutoReset.ExecuteAutoReset();
            bool t6Pass = wasOpenBefore && !TravelGate.CanTravelToCity &&
                          PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 0) == 0 &&
                          PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestRewardClaimed, 0) == 0 &&
                          PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestExplorerGateNotified, 0) == 0;
            results.Add(new TestResult
            {
                TestName = "6. AutoReset Clears Gate Keys and Closes Gate",
                Passed = t6Pass,
                Details = $"WasOpen={wasOpenBefore}, ClearedSuccessfully={t6Pass}"
            });

            // Test 7: City scene spawn point verification
            bool t7Pass = false;
            string t7Details = "";
            var cityScene = EditorSceneManager.OpenScene("Assets/EcoQuest/City Scene.unity", OpenSceneMode.Additive);
            var spawnObj = GameObject.Find("CityPlayerSpawnPoint");
            if (spawnObj != null)
            {
                Vector3 expected = new Vector3(0.00f, 0.15f, 20.00f);
                float dist = Vector3.Distance(spawnObj.transform.position, expected);
                t7Pass = (dist < 0.05f);
                t7Details = $"Spawn point found at {spawnObj.transform.position} (dist to expected: {dist:F3}m)";
            }
            else
            {
                t7Details = "CityPlayerSpawnPoint NOT found in City Scene!";
            }
            EditorSceneManager.CloseScene(cityScene, true);
            var forest = SceneManager.GetSceneByName("Forest Scene");
            if (forest.isLoaded) EditorSceneManager.SetActiveScene(forest);
            results.Add(new TestResult
            {
                TestName = "7. City Scene Spawn Point Verification",
                Passed = t7Pass,
                Details = t7Details
            });

            // Test 8: Forest Scene Explorer & UIOrchestrator components verification
            bool t8Pass = false;
            string t8Details = "";
            var explorerObj = GameObject.Find("Explorer_Var1");
            if (explorerObj != null)
            {
                bool hasAnim = explorerObj.GetComponent<NpcExplorerController>() != null;
                bool hasCin = explorerObj.GetComponent<ExplorerCinematicController>() != null;
                bool hasGate = explorerObj.GetComponent<ExplorerGateController>() != null;
                bool hasPointer = explorerObj.GetComponent<NPCWaypointPointer>() != null;
                bool hasMinimap = explorerObj.GetComponent<MinimapIcon>() != null;
                bool hasCamPoint = explorerObj.transform.Find("ExplorerDialogueCamPoint") != null;

                var uiNotif = GameObject.Find("--- UI ---/UI_Notification");
                bool hasNotifComp = uiNotif != null && uiNotif.GetComponent<ExplorerGateNotification>() != null;

                t8Pass = hasAnim && hasCin && hasGate && hasPointer && hasMinimap && hasCamPoint && hasNotifComp;
                t8Details = $"Anim={hasAnim}, Cin={hasCin}, Gate={hasGate}, Pointer={hasPointer}, Minimap={hasMinimap}, CamPoint={hasCamPoint}, Notif={hasNotifComp}";
            }
            else
            {
                t8Details = "Explorer_Var1 not found in active scene!";
            }
            results.Add(new TestResult
            {
                TestName = "8. Explorer & Notification Components Configured",
                Passed = t8Pass,
                Details = t8Details
            });
        }
        finally
        {
            // Restore original PlayerPrefs
            RestorePref(EcoQuestSaveKeys.TabTipDone, origTab);
            RestorePref(EcoQuestSaveKeys.SequentialWASD, origWasd);
            RestorePref(EcoQuestSaveKeys.PreQuizScore, origPre);
            RestorePref(EcoQuestSaveKeys.ForestTrashQuestDone, origTrash);
            RestorePref(EcoQuestSaveKeys.PostQuizScore, origPost);
            RestorePref(EcoQuestSaveKeys.ForestRewardClaimed, origReward);
            RestorePref(EcoQuestSaveKeys.ForestExplorerGateNotified, origNotif);
            PlayerPrefs.Save();
        }

        foreach (var r in results)
        {
            if (r.Passed)
            {
                Debug.Log($"[TEST PASS] {r.TestName} => {r.Details}");
            }
            else
            {
                Debug.LogError($"[TEST FAIL] {r.TestName} => {r.Details}");
            }
        }

        return results;
    }

    private static void RestorePref(string key, int val)
    {
        if (val == -99) PlayerPrefs.DeleteKey(key);
        else PlayerPrefs.SetInt(key, val);
    }
}
#endif
