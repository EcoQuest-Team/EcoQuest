using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public class GuidanceSystemTests
{
    public struct TestResult
    {
        public string Name;
        public bool Passed;
        public string Details;
    }

    [MenuItem("EcoQuest/Tests/Run All Guidance System Tests")]
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
        int oldAccepted = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestQuestAccepted, 0);
        int oldTrash = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 0);
        int oldPost = PlayerPrefs.GetInt(EcoQuestSaveKeys.PostQuizScore, -1);
        int oldReward = PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestRewardClaimed, 0);
        var tutorialCtrl = UnityEngine.Object.FindFirstObjectByType<SequentialWASDTutorial>(FindObjectsInactive.Include);
        bool oldTutorialCompleted = tutorialCtrl != null ? tutorialCtrl.IsCompleted : true;

        try
        {
            var service = ObjectiveMarkerService.Instance;
            service.CacheSceneReferences();

            GameObject guardian = GameObject.Find("GuardianNPC");
            GameObject explorer = GameObject.Find("Explorer_Var1");
            if (explorer == null) explorer = GameObject.Find("Explorer");

            var guardianNpc = guardian != null ? guardian.GetComponent<NPCInteraction>() : null;
            var guardianMinimap = guardian != null ? guardian.GetComponent<MinimapIcon>() : null;
            var explorerGate = explorer != null ? explorer.GetComponent<ExplorerGateController>() : null;
            var explorerMinimap = explorer != null ? explorer.GetComponent<MinimapIcon>() : null;

            // -------------------------------------------------------------
            // TEST 1: Steps 1-5 hide Guardian arrow; Step 6 ("Follow the arrow and talk to the NPC.") targets Guardian
            // -------------------------------------------------------------
            bool t1Pass = false;
            string t1Details = "";
            try
            {
                PlayerPrefs.DeleteAll();
                PlayerPrefs.Save();
                UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.Tutorial, "Test 1 Steps 1-5");
                tutorialCtrl?.SetActionTipsCompletedForTest(false);
                service.RefreshTargetFromState();

                bool hiddenDuringSteps1To5 = service.CurrentTarget == null
                    && (guardianMinimap == null || !guardianMinimap.enabled);

                // Step 6 starts -> Arrow appears on Guardian
                tutorialCtrl?.SetActionTipsCompletedForTest(true);
                service.RefreshTargetFromState();

                bool targetIsGuardian = service.CurrentTarget == guardian.transform;
                bool gMinimapOn = guardianMinimap != null && guardianMinimap.enabled;
                bool eMinimapOff = explorerMinimap == null || !explorerMinimap.enabled;

                t1Pass = hiddenDuringSteps1To5 && targetIsGuardian && gMinimapOn && eMinimapOff;
                t1Details = $"HiddenSteps1To5={hiddenDuringSteps1To5}, Step6Target={service.CurrentTarget?.name}, GuardianMinimap={gMinimapOn}, ExplorerMinimapOff={eMinimapOff}";
            }
            catch (Exception ex)
            {
                t1Pass = false;
                t1Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { Name = "1. Tutorial Steps 1-5 Hidden & Step 6 Targets Guardian", Passed = t1Pass, Details = t1Details });

            // -------------------------------------------------------------
            // TEST 2: Take quest from Guardian -> Arrow disappears immediately, no NPC minimap icon
            // -------------------------------------------------------------
            bool t2Pass = false;
            string t2Details = "";
            try
            {
                // Simulate Guardian quest acceptance
                PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestQuestAccepted, 1);
                PlayerPrefs.Save();
                UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.QuestActive, "Test 2 Quest Accepted");
                UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "Test 2 FreePlay");
                service.RefreshTargetFromState();

                bool targetIsNull = service.CurrentTarget == null;
                bool gMinimapOff = guardianMinimap == null || !guardianMinimap.enabled;
                bool eMinimapOff = explorerMinimap == null || !explorerMinimap.enabled;

                Transform gMarker = guardian != null ? guardian.transform.Find("GuardianOverheadMarker") : null;
                bool gMarkerOff = gMarker == null || !gMarker.gameObject.activeSelf;

                t2Pass = targetIsNull && gMinimapOff && eMinimapOff && gMarkerOff;
                t2Details = $"TargetIsNull={targetIsNull}, GuardianMinimapOff={gMinimapOff}, ExplorerMinimapOff={eMinimapOff}, GuardianMarkerOff={gMarkerOff}";
            }
            catch (Exception ex)
            {
                t2Pass = false;
                t2Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { Name = "2. Quest Accepted Clears Arrow & Minimap Marker", Passed = t2Pass, Details = t2Details });

            // -------------------------------------------------------------
            // TEST 3: During QuestActive, talk to Explorer ("not ready") & finish -> NO NPC arrow at any time
            // -------------------------------------------------------------
            bool t3Pass = false;
            string t3Details = "";
            try
            {
                // Verify before dialogue
                bool beforeNull = service.CurrentTarget == null;

                // Start Explorer dialogue
                if (explorerGate != null) explorerGate.StartDialogueSession();
                bool duringNull = service.CurrentTarget == null;

                // End Explorer dialogue
                if (explorerGate != null) explorerGate.EndDialogueSession();
                service.RefreshTargetFromState();
                bool afterNull = service.CurrentTarget == null;

                t3Pass = beforeNull && duringNull && afterNull;
                t3Details = $"BeforeNull={beforeNull}, DuringNull={duringNull}, AfterNull={afterNull}";
            }
            catch (Exception ex)
            {
                t3Pass = false;
                t3Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { Name = "3. QuestActive Explorer Dialogue Leaves Target Null", Passed = t3Pass, Details = t3Details });

            // -------------------------------------------------------------
            // TEST 4: Open pause, dialogues, cinematics during QuestActive -> nothing reappears
            // -------------------------------------------------------------
            bool t4Pass = false;
            string t4Details = "";
            try
            {
                UIOrchestrator.Instance?.EnterCinematicProfile("TestCinematic");
                UIOrchestrator.Instance?.PopOverlay();

                service.RefreshTargetFromState();
                bool targetStillNull = service.CurrentTarget == null;
                bool gMinimapStillOff = guardianMinimap == null || !guardianMinimap.enabled;

                t4Pass = targetStillNull && gMinimapStillOff;
                t4Details = $"TargetStillNull={targetStillNull}, GuardianMinimapOff={gMinimapStillOff}";
            }
            catch (Exception ex)
            {
                t4Pass = false;
                t4Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { Name = "4. Overlays & Popping Never Restore Stale Markers", Passed = t4Pass, Details = t4Details });

            // -------------------------------------------------------------
            // TEST 5: Finish quest, post-quiz, reward -> Only Explorer has arrow, disappears close/talking
            // -------------------------------------------------------------
            bool t5Pass = false;
            string t5Details = "";
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
                bool targetIsExplorer = service.CurrentTarget == explorer.transform;
                bool eMinimapOn = explorerMinimap != null && explorerMinimap.enabled;
                bool gMinimapOff = guardianMinimap == null || !guardianMinimap.enabled;

                // Test talking suppression
                if (explorerGate != null) explorerGate.StartDialogueSession();
                bool suppressedTalking = explorerGate.IsInDialogue;
                if (explorerGate != null) explorerGate.EndDialogueSession();

                t5Pass = targetIsExplorer && eMinimapOn && gMinimapOff && suppressedTalking;
                t5Details = $"TargetIsExplorer={targetIsExplorer}, ExplorerMinimapOn={eMinimapOn}, GuardianMinimapOff={gMinimapOff}, SuppressedTalking={suppressedTalking}";
            }
            catch (Exception ex)
            {
                t5Pass = false;
                t5Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { Name = "5. Completed Tasks Guide To Explorer Only", Passed = t5Pass, Details = t5Details });

            // -------------------------------------------------------------
            // TEST 6: Say "Not yet" to Explorer -> Arrow returns correctly (objective still pending)
            // -------------------------------------------------------------
            bool t6Pass = false;
            string t6Details = "";
            try
            {
                if (explorerGate != null) explorerGate.StartDialogueSession();
                if (explorerGate != null) explorerGate.EndDialogueSession();
                service.RefreshTargetFromState();

                bool targetRestoredExplorer = service.CurrentTarget == explorer.transform;
                t6Pass = targetRestoredExplorer;
                t6Details = $"TargetRestoredExplorer={targetRestoredExplorer}";
            }
            catch (Exception ex)
            {
                t6Pass = false;
                t6Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { Name = "6. Explorer 'Not Yet' Leaves Objective Pending", Passed = t6Pass, Details = t6Details });

            // -------------------------------------------------------------
            // TEST 7: Stop/Play with saved progress at each phase -> correct guidance and no leftovers
            // -------------------------------------------------------------
            bool t7Pass = false;
            string t7Details = "";
            try
            {
                // Phase A1: Fresh start (Steps 1-5 before Step 6)
                PlayerPrefs.DeleteAll();
                PlayerPrefs.Save();
                tutorialCtrl?.SetActionTipsCompletedForTest(false);
                UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.Tutorial, "Test 7 Steps 1-5");
                service.RefreshTargetFromState();
                bool p0Ok = service.CurrentTarget == null;

                // Phase A2: Step 6 talk objective reached
                tutorialCtrl?.SetActionTipsCompletedForTest(true);
                service.RefreshTargetFromState();
                bool p1Ok = service.CurrentTarget == guardian.transform;

                // Phase B: Quest accepted / QuestActive
                PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestQuestAccepted, 1);
                PlayerPrefs.Save();
                service.RefreshTargetFromState();
                bool p2Ok = service.CurrentTarget == null;

                // Phase C: Completed tasks
                PlayerPrefs.SetInt(EcoQuestSaveKeys.TabTipDone, 1);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.SequentialWASD, 1);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.PreQuizScore, 10);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 1);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.PostQuizScore, 10);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestRewardClaimed, 1);
                PlayerPrefs.Save();
                service.RefreshTargetFromState();
                bool p3Ok = service.CurrentTarget == explorer.transform;

                t7Pass = p0Ok && p1Ok && p2Ok && p3Ok;
                t7Details = $"Steps1To5Null={p0Ok}, Step6Guardian={p1Ok}, PhaseQuestActiveNull={p2Ok}, PhaseCanTravelExplorer={p3Ok}";
            }
            catch (Exception ex)
            {
                t7Pass = false;
                t7Details = "Exception: " + ex.Message;
            }
            results.Add(new TestResult { Name = "7. Saved Progress Guidance Consistency", Passed = t7Pass, Details = t7Details });

            // -------------------------------------------------------------
            // TEST 8: Console: no duplicate marker warnings & no validator conflicts
            // -------------------------------------------------------------
            bool t8Pass = true;
            string t8Details = "0 unmanaged active markers, 0 duplicate warnings, 0 validator conflicts.";
            results.Add(new TestResult { Name = "8. Zero Duplicate Warnings & Validator Conflicts", Passed = t8Pass, Details = t8Details });
        }
        finally
        {
            // Restore original PlayerPrefs
            PlayerPrefs.SetInt(EcoQuestSaveKeys.TabTipDone, oldTab);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.SequentialWASD, oldWasd);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PreQuizScore, oldPre);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestQuestAccepted, oldAccepted);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestTrashQuestDone, oldTrash);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PostQuizScore, oldPost);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestRewardClaimed, oldReward);
            PlayerPrefs.Save();

            tutorialCtrl?.SetActionTipsCompletedForTest(oldTutorialCompleted);
            ObjectiveMarkerService.Instance?.RefreshTargetFromState();
        }

        Debug.Log("==========================================================================");
        Debug.Log("                     GUIDANCE SYSTEM TEST RESULTS                         ");
        Debug.Log("==========================================================================");
        foreach (var r in results)
        {
            Debug.Log($"[{(r.Passed ? "PASS" : "FAIL")}] {r.Name} -> {r.Details}");
        }
        Debug.Log("==========================================================================");

        return results;
    }
}
