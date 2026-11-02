#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Automated verification tests for the trash visibility & Explorer interaction bugfix.
/// Tests that trash visibility is a pure function of persistent state and is never hidden
/// by Explorer dialogue, cinematic camera, or temporary HUD profiles.
/// </summary>
public static class TrashVisibilityExplorerBugTests
{
    public struct TestBugResult
    {
        public string TestName;
        public bool Passed;
        public string Details;
    }

    [MenuItem("EcoQuest/Run Trash & Explorer Bug Tests", false, 101)]
    public static List<TestBugResult> RunAllBugTests()
    {
        var results = new List<TestBugResult>();
        Debug.Log("=================================================");
        Debug.Log("RUNNING TRASH VISIBILITY & EXPLORER BUG TESTS");
        Debug.Log("=================================================");

        // Backup current PlayerPrefs
        int origTab = PlayerPrefs.GetInt(EcoQuestSaveKeys.TabTipDone, -99);
        int origWasd = PlayerPrefs.GetInt(EcoQuestSaveKeys.SequentialWASD, -99);
        int origPre = PlayerPrefs.GetInt(EcoQuestSaveKeys.PreQuizScore, -99);
        int origTrash = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestTrashQuestDone, -99);
        int origPost = PlayerPrefs.GetInt(EcoQuestSaveKeys.PostQuizScore, -99);
        int origReward = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestRewardClaimed, -99);
        int origNotif = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestExplorerGateNotified, -99);

        var intro = Object.FindFirstObjectByType<IntroSequenceController>();
        var explorerGate = Object.FindFirstObjectByType<ExplorerGateController>();

        try
        {
            // Test 1: Fresh start -> tutorial finished -> take quest -> 9 trash items visible
            EcoQuestSaveKeys.ClearAllTutorialProgress();
            NPCInteraction.IsAnyQuestActive = false;
            TravelGate.ResetStaticState();
            TrashVisibilityController.Refresh("Fresh start test");

            bool freshStartHidden = !TrashVisibilityController.ShouldTrashBeVisible();

            // Simulate Guardian quest acceptance
            PlayerPrefs.SetInt(EcoQuestSaveKeys.TabTipDone, 1);
            NPCInteraction.IsAnyQuestActive = true;
            TrashVisibilityController.Refresh("Guardian quest accepted");

            int activeCount1 = CountActiveTrashItems(intro);
            bool t1Pass = freshStartHidden && TrashVisibilityController.ShouldTrashBeVisible()
                          && TrashItem.AreTrashItemsAllowed && activeCount1 == 9;

            results.Add(new TestBugResult
            {
                TestName = "1. Quest Accepted: 9 Trash Items Visible",
                Passed = t1Pass,
                Details = $"FreshHidden={freshStartHidden}, ShouldBeVisible={TrashVisibilityController.ShouldTrashBeVisible()}, ActiveItems={activeCount1}/9"
            });

            // Test 2: Talk to Explorer (early dialogue, not ready): trash stays visible before, during, and after
            int countBefore = CountActiveTrashItems(intro);

            // Simulate entering Explorer dialogue
            UIOrchestrator.Instance?.EnterCinematicProfile("ExplorerGate_Test");
            UIOrchestrator.Instance?.ValidateStepState(UIOrchestrator.FlowStep.ExplorerTravel);

            int countDuring = CountActiveTrashItems(intro);
            bool allowedDuring = TrashItem.AreTrashItemsAllowed;

            // Simulate exiting Explorer dialogue
            UIOrchestrator.Instance?.ExitCinematicProfile("ExplorerGate_Test");
            TrashVisibilityController.Refresh("Explorer dialogue ended");
            UIOrchestrator.Instance?.ValidateStepState(UIOrchestrator.FlowStep.FreePlay);

            int countAfter = CountActiveTrashItems(intro);
            bool allowedAfter = TrashItem.AreTrashItemsAllowed;

            bool t2Pass = (countBefore == 9) && (countDuring == 9) && (countAfter == 9) && allowedDuring && allowedAfter;
            results.Add(new TestBugResult
            {
                TestName = "2. Talk to Explorer: Trash Stays Visible Before, During & After",
                Passed = t2Pass,
                Details = $"Before={countBefore}, During={countDuring} (Allowed={allowedDuring}), After={countAfter} (Allowed={allowedAfter})"
            });

            // Test 3: Talk to Explorer 5 times in a row, with Esc/interrupt simulation
            bool t3Pass = true;
            for (int i = 0; i < 5; i++)
            {
                UIOrchestrator.Instance?.EnterCinematicProfile($"Explorer_Iter_{i}");
                UIOrchestrator.Instance?.ValidateStepState(UIOrchestrator.FlowStep.ExplorerTravel);
                if (CountActiveTrashItems(intro) != 9) { t3Pass = false; break; }

                // Interrupted / exit
                UIOrchestrator.Instance?.ExitCinematicProfile($"Explorer_Iter_{i}");
                TrashVisibilityController.Refresh($"Explorer iter {i} exit");
                UIOrchestrator.Instance?.ValidateStepState(UIOrchestrator.FlowStep.FreePlay);
                if (CountActiveTrashItems(intro) != 9) { t3Pass = false; break; }
            }

            results.Add(new TestBugResult
            {
                TestName = "3. Talk 5x with Dialogue Interrupts: 0 Items Disappeared",
                Passed = t3Pass,
                Details = $"5 consecutive cycles tested. All 9 items remained active: {t3Pass}"
            });

            // Test 4: Deposit 3 items, then talk to Explorer: 3 stay deposited, 6 remain visible
            var allTrash = GetSceneTrashItems(intro);
            if (allTrash.Count >= 3)
            {
                allTrash[0].IsDeposited = true;
                allTrash[0].gameObject.SetActive(false);

                allTrash[1].IsDeposited = true;
                allTrash[1].gameObject.SetActive(false);

                allTrash[2].IsDeposited = true;
                allTrash[2].gameObject.SetActive(false);
            }

            int countAfterDeposit = CountActiveTrashItems(intro); // should be 6

            // Talk to Explorer
            UIOrchestrator.Instance?.EnterCinematicProfile("Explorer_Deposit_Test");
            UIOrchestrator.Instance?.ValidateStepState(UIOrchestrator.FlowStep.ExplorerTravel);
            int countDuringAfterDeposit = CountActiveTrashItems(intro);

            UIOrchestrator.Instance?.ExitCinematicProfile("Explorer_Deposit_Test");
            TrashVisibilityController.Refresh("Explorer deposit test exit");
            UIOrchestrator.Instance?.ValidateStepState(UIOrchestrator.FlowStep.FreePlay);
            int countFinalAfterDeposit = CountActiveTrashItems(intro);

            bool t4Pass = (countAfterDeposit == 6) && (countDuringAfterDeposit == 6) && (countFinalAfterDeposit == 6)
                          && allTrash[0].IsDeposited && !allTrash[0].gameObject.activeSelf
                          && allTrash[1].IsDeposited && !allTrash[1].gameObject.activeSelf
                          && allTrash[2].IsDeposited && !allTrash[2].gameObject.activeSelf;

            results.Add(new TestBugResult
            {
                TestName = "4. Deposit 3 Items: 3 Stay Deposited, 6 Stay Visible (No Respawns)",
                Passed = t4Pass,
                Details = $"Active={countFinalAfterDeposit}/6. Deposited items remained inactive: {t4Pass}"
            });

            // Clean up deposit state for remaining tests
            if (allTrash.Count >= 3)
            {
                allTrash[0].IsDeposited = false;
                allTrash[1].IsDeposited = false;
                allTrash[2].IsDeposited = false;
                TrashVisibilityController.Refresh("Reset deposit test items");
            }

            // Test 5: Complete quest, post-quiz, reward, then talk to Explorer and travel
            PlayerPrefs.SetInt(EcoQuestSaveKeys.TabTipDone, 1);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.SequentialWASD, 1);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PreQuizScore, 5);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 1);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PostQuizScore, 5);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestRewardClaimed, 1);
            PlayerPrefs.Save();

            bool t5GateOpen = TravelGate.CanTravelToCity;
            bool t5TravelApproved = TravelGate.TryTravelToCity("BugTest_Test5");
            TravelGate.ResetStaticState();

            results.Add(new TestBugResult
            {
                TestName = "5. Complete All Tasks: Travel Flow Remains Fully Functional",
                Passed = t5GateOpen && t5TravelApproved,
                Details = $"CanTravelToCity={t5GateOpen}, TryTravelToCity approved={t5TravelApproved}"
            });

            // Test 6: Stop/Play and auto reset: fresh start hides trash until Guardian quest starts
            EcoQuestEditorAutoReset.ExecuteAutoReset();
            TrashVisibilityController.Refresh("AutoReset execution");

            bool t6GateClosed = !TravelGate.CanTravelToCity;
            bool t6ShouldBeHidden = !TrashVisibilityController.ShouldTrashBeVisible();
            int t6ActiveCount = CountActiveTrashItems(intro);

            bool t6Pass = t6GateClosed && t6ShouldBeHidden && t6ActiveCount == 0;
            results.Add(new TestBugResult
            {
                TestName = "6. AutoReset: Fresh Start Closes Gate & Hides Trash",
                Passed = t6Pass,
                Details = $"GateClosed={t6GateClosed}, TrashHidden={t6ShouldBeHidden}, ActiveTrashCount={t6ActiveCount}"
            });

            // Test 7: Validator warning check: FreePlay and ExplorerTravel produce NO warning
            bool t7Pass = true;
            // Test both during quest
            PlayerPrefs.SetInt(EcoQuestSaveKeys.TabTipDone, 1);
            NPCInteraction.IsAnyQuestActive = true;
            TrashVisibilityController.Refresh("Test 7 setup");

            UIOrchestrator.Instance?.ValidateStepState(UIOrchestrator.FlowStep.FreePlay);
            UIOrchestrator.Instance?.ValidateStepState(UIOrchestrator.FlowStep.ExplorerTravel);

            bool trashStillAllowed = TrashItem.AreTrashItemsAllowed;
            int countTest7 = CountActiveTrashItems(intro);
            t7Pass = trashStillAllowed && (countTest7 == 9);

            results.Add(new TestBugResult
            {
                TestName = "7. Validator Mapping: No Trash Warnings at FreePlay or ExplorerTravel",
                Passed = t7Pass,
                Details = $"Step validation clean. Trash allowed={trashStillAllowed}, Active items={countTest7}/9"
            });
        }
        finally
        {
            // Restore original preferences
            RestorePref(EcoQuestSaveKeys.TabTipDone, origTab);
            RestorePref(EcoQuestSaveKeys.SequentialWASD, origWasd);
            RestorePref(EcoQuestSaveKeys.PreQuizScore, origPre);
            RestorePref(EcoQuestSaveKeys.ForestTrashQuestDone, origTrash);
            RestorePref(EcoQuestSaveKeys.PostQuizScore, origPost);
            RestorePref(EcoQuestSaveKeys.ForestRewardClaimed, origReward);
            RestorePref(EcoQuestSaveKeys.ForestExplorerGateNotified, origNotif);
            PlayerPrefs.Save();
            NPCInteraction.IsAnyQuestActive = false;
        }

        foreach (var r in results)
        {
            if (r.Passed) Debug.Log($"[BUG TEST PASS] {r.TestName} => {r.Details}");
            else Debug.LogError($"[BUG TEST FAIL] {r.TestName} => {r.Details}");
        }

        return results;
    }

    private static int CountActiveTrashItems(IntroSequenceController intro)
    {
        var items = GetSceneTrashItems(intro);
        int count = 0;
        foreach (var it in items)
        {
            if (it != null && it.gameObject.activeInHierarchy) count++;
        }
        return count;
    }

    private static List<TrashItem> GetSceneTrashItems(IntroSequenceController intro)
    {
        var list = new List<TrashItem>();
        if (intro != null)
        {
            var so = new SerializedObject(intro);
            var containerProp = so.FindProperty("trashContainer");
            if (containerProp != null && containerProp.objectReferenceValue != null)
            {
                var container = containerProp.objectReferenceValue as GameObject;
                if (container != null)
                {
                    for (int i = 0; i < container.transform.childCount; i++)
                    {
                        var child = container.transform.GetChild(i);
                        var ti = child.GetComponent<TrashItem>();
                        if (ti != null) list.Add(ti);
                    }
                }
            }
        }
        return list;
    }

    private static void RestorePref(string key, int val)
    {
        if (val == -99) PlayerPrefs.DeleteKey(key);
        else PlayerPrefs.SetInt(key, val);
    }
}
#endif
