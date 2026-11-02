using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public class CityQuestTabAndBinFittingTests
{
    public const string ForestScenePath = "Assets/EcoQuest/Forest Scene.unity";
    public const string CityScenePath = "Assets/EcoQuest/City Scene.unity";

    public struct TestResult
    {
        public string Name;
        public bool Passed;
        public string Details;
    }

    [MenuItem("EcoQuest/Tests/Run City Quest Tab & Bin Fitting Tests")]
    public static List<TestResult> RunAllTests()
    {
        List<TestResult> results = new List<TestResult>();
        string originalScene = EditorSceneManager.GetActiveScene().path;

        try
        {
            // -------------------------------------------------------------
            // SUITE 1: FOREST BIN PILE FITTING (PROBLEM 2)
            // -------------------------------------------------------------
            results.Add(RunTest("Forest_BinPiles_Fitting_InsideBounds", () =>
            {
                EditorSceneManager.OpenScene(ForestScenePath, OpenSceneMode.Single);
                var bins = UnityEngine.Object.FindObjectsByType<BinVisualTrashLayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (bins == null || bins.Length != 9)
                {
                    return (false, $"Expected 9 bins, found {(bins != null ? bins.Length : 0)}");
                }

                bool valid = BinPileFittingTool.ValidateAllBinPiles();
                if (!valid)
                {
                    return (false, "BinPileFittingTool.ValidateAllBinPiles failed (piles overflow bounds)");
                }

                foreach (var bvl in bins)
                {
                    if (bvl.PileObject == null)
                        return (false, $"Bin {bvl.gameObject.name} missing PileObject");

                    if (Vector3.Distance(bvl.PileObject.transform.localPosition, BinPileFittingTool.DefaultFittedPosition) > 0.005f)
                        return (false, $"Bin {bvl.gameObject.name} position mismatch: {bvl.PileObject.transform.localPosition}");

                    if (Vector3.Distance(bvl.PileObject.transform.localScale, BinPileFittingTool.DefaultFittedScale) > 0.005f)
                        return (false, $"Bin {bvl.gameObject.name} scale mismatch: {bvl.PileObject.transform.localScale}");
                }

                return (true, "All 9 bins fitted within inner cavity bounds with 0.00m overflow.");
            }));

            results.Add(RunTest("Forest_BinPiles_0Empty_1Full_Pop_Reset", () =>
            {
                EditorSceneManager.OpenScene(ForestScenePath, OpenSceneMode.Single);
                var bin = UnityEngine.Object.FindFirstObjectByType<BinVisualTrashLayer>();
                if (bin == null || bin.PileObject == null)
                    return (false, "No BinVisualTrashLayer found in scene");

                bin.ResetVisuals();
                if (bin.PileObject.activeSelf)
                    return (false, "PileObject was active after ResetVisuals");

                bin.SetDepositCount(1, animatePop: false);
                if (!bin.PileObject.activeSelf)
                    return (false, "PileObject not active after SetDepositCount(1)");

                if (Vector3.Distance(bin.PileObject.transform.localScale, bin.FittedLocalScale) > 0.005f)
                    return (false, $"PileObject scale {bin.PileObject.transform.localScale} does not match fitted {bin.FittedLocalScale}");

                bin.SetDepositCount(4, animatePop: false);
                if (Vector3.Distance(bin.PileObject.transform.localScale, bin.FittedLocalScale) > 0.005f)
                    return (false, $"PileObject scale changed on count 4: {bin.PileObject.transform.localScale}");

                bin.ResetVisuals();
                if (bin.PileObject.activeSelf)
                    return (false, "PileObject not inactive after second ResetVisuals");

                return (true, "0 empty, >=1 full dense pile, stable fitted scale confirmed.");
            }));

            // -------------------------------------------------------------
            // SUITE 2: CITY QUEST TAB VISIBILITY & HEALTH CHECK (PROBLEM 1)
            // -------------------------------------------------------------
            results.Add(RunTest("City_Arrival_CleanSlate_NoQuest", () =>
            {
                EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
                UIOrchestrator.Instance?.ResolveReferences();
                UIOrchestrator.Instance?.ClearAllOverlays();
                UIOrchestrator.Instance?.SetQuestPanelUserHidden(false, "TestSetup");
                UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "Arrival Test");
                UIOrchestrator.Instance?.SetPhase(UIOrchestrator.Instance.EvaluateLivePhase("Arrival Test"), "Arrival Test");
                UIOrchestrator.Instance?.ApplyCurrentProfile(0f);

                if (UIOrchestrator.Instance.CurrentPhase != UIOrchestrator.GamePhase.FreePlay)
                    return (false, $"Expected Phase FreePlay (NoQuest), got {UIOrchestrator.Instance.CurrentPhase}");

                bool questExpected = UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.QuestPanel);
                if (questExpected)
                    return (false, "QuestPanel expected visibility should be FALSE on arrival (NoQuest)");

                bool minimapExpected = UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.Minimap);
                bool gearExpected = UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.Gear);
                bool coinsExpected = UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.Coins);

                if (!minimapExpected || !gearExpected || !coinsExpected)
                    return (false, $"HUD elements missing on arrival: map={minimapExpected}, gear={gearExpected}, coins={coinsExpected}");

                return (true, "City clean slate verified: Phase=FreePlay, QuestPanel=hidden, HUD=visible.");
            }));

            results.Add(RunTest("City_GuardianDialogue_OverlayCinematic", () =>
            {
                EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
                UIOrchestrator.Instance?.ResolveReferences();
                UIOrchestrator.Instance?.EnterCinematicProfile("CityDialogueTest");

                if (!UIOrchestrator.Instance.IsOverlayActive)
                    return (false, "Overlay stack should be active during dialogue");

                bool gearExpected = UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.Gear);
                bool coinsExpected = UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.Coins);
                bool questExpected = UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.QuestPanel);

                if (gearExpected || coinsExpected || questExpected)
                    return (false, $"Cinematic profile leaked: gear={gearExpected}, coins={coinsExpected}, quest={questExpected}");

                UIOrchestrator.Instance?.ExitCinematicProfile("CityDialogueTest");
                if (UIOrchestrator.Instance.IsOverlayActive)
                    return (false, "Overlay stack should be empty after ExitCinematicProfile");

                return (true, "Dialogue cinematic profile correctly hides HUD and QuestPanel without leaking.");
            }));

            results.Add(RunTest("City_AcceptQuest_LivePhaseActive_QuestTabVisible", () =>
            {
                EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
                UIOrchestrator.Instance?.ResolveReferences();
                var lqm = LitterbugQuestManager.Instance;
                if (lqm == null)
                    return (false, "LitterbugQuestManager not found in City Scene");

                // Simulate dialogue offer
                UIOrchestrator.Instance.EnterCinematicProfile("CityDialogue");
                
                // Simulate player accepting quest -> StartNewRound
                UIOrchestrator.Instance.ExitCinematicProfile("CityDialogue");
                lqm.StartNewRound();

                if (lqm.QuestState != LitterbugQuestState.Active)
                    return (false, $"Expected QuestState.Active, got {lqm.QuestState}");

                if (UIOrchestrator.Instance.CurrentPhase != UIOrchestrator.GamePhase.QuestActive)
                    return (false, $"Expected UIOrchestrator Phase QuestActive, got {UIOrchestrator.Instance.CurrentPhase}");

                bool questExpected = UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.QuestPanel);
                if (!questExpected)
                    return (false, "UIOrchestrator expected visibility for QuestPanel is FALSE when Quest is Active!");

                bool healthCheckPass = TravelGate.RunPostTravelHealthCheck(out string healthReport);
                if (!healthCheckPass)
                    return (false, $"Health check failed after quest start: {healthReport}");

                return (true, "Quest accepted: Phase=QuestActive, QuestPanel expected=true, Health Check PASS.");
            }));

            results.Add(RunTest("City_QuestTab_TAB_Toggle", () =>
            {
                EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
                UIOrchestrator.Instance?.ResolveReferences();
                var lqm = LitterbugQuestManager.Instance;
                lqm.StartNewRound();

                // 1. Initial: visible
                if (UIOrchestrator.Instance.IsQuestPanelUserHidden)
                    return (false, "QuestPanel should not be user-hidden initially");

                // 2. Press TAB -> toggles userHidden = true
                UIOrchestrator.Instance.ToggleQuestPanel("TAB_Test");
                if (!UIOrchestrator.Instance.IsQuestPanelUserHidden)
                    return (false, "QuestPanel should be userHidden=true after TAB toggle");

                if (UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.QuestPanel))
                    return (false, "QuestPanel expected visibility should be FALSE when userHidden=true");

                // Other HUD elements should stay visible
                if (!UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.Minimap))
                    return (false, "Minimap was hidden by TAB toggle!");

                // 3. Press TAB again -> toggles userHidden = false
                UIOrchestrator.Instance.ToggleQuestPanel("TAB_Test");
                if (UIOrchestrator.Instance.IsQuestPanelUserHidden)
                    return (false, "QuestPanel should be userHidden=false after second TAB toggle");

                if (!UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.QuestPanel))
                    return (false, "QuestPanel expected visibility should be TRUE after restoring with TAB");

                return (true, "TAB toggle smoothly hides and shows QuestPanel without affecting other HUD elements.");
            }));

            results.Add(RunTest("City_PostTravel_HealthCheck_ThreePoints", () =>
            {
                EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
                UIOrchestrator.Instance?.ResolveReferences();
                UIOrchestrator.Instance?.ClearAllOverlays();
                UIOrchestrator.Instance?.SetQuestPanelUserHidden(false, "TestSetup");
                UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "HealthCheckTest");
                UIOrchestrator.Instance?.SetPhase(UIOrchestrator.Instance.EvaluateLivePhase("HealthCheckTest"), "HealthCheckTest");
                UIOrchestrator.Instance?.ApplyCurrentProfile(0f);

                // Point 1: Right after travel (before quest)
                bool pass1 = TravelGate.RunPostTravelHealthCheck(out string report1);
                if (!pass1)
                    return (false, $"Point 1 (Post-Travel) failed: {report1}");

                // Point 2: Right after Guardian dialogue ends (before accept)
                UIOrchestrator.Instance.EnterCinematicProfile("CityDialogue");
                UIOrchestrator.Instance.ExitCinematicProfile("CityDialogue");
                bool pass2 = TravelGate.RunPostTravelHealthCheck(out string report2);
                if (!pass2)
                    return (false, $"Point 2 (DialogueEnd) failed: {report2}");

                // Point 3: Right after quest start (round started)
                LitterbugQuestManager.Instance?.StartNewRound();
                bool pass3 = TravelGate.RunPostTravelHealthCheck(out string report3);
                if (!pass3)
                    return (false, $"Point 3 (QuestStart) failed: {report3}");

                return (true, $"All 3 health check points passed cleanly.\nP1: {report1}\nP2: {report2}\nP3: {report3}");
            }));
        }
        finally
        {
            if (!string.IsNullOrEmpty(originalScene))
            {
                EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
            }
        }

        PrintSummary(results);
        return results;
    }

    private static TestResult RunTest(string name, Func<(bool passed, string details)> testFunc)
    {
        try
        {
            var res = testFunc();
            return new TestResult { Name = name, Passed = res.passed, Details = res.details };
        }
        catch (Exception ex)
        {
            return new TestResult { Name = name, Passed = false, Details = $"Exception: {ex.Message}\n{ex.StackTrace}" };
        }
    }

    private static void PrintSummary(List<TestResult> results)
    {
        int passed = 0;
        int failed = 0;
        foreach (var r in results)
        {
            if (r.Passed)
            {
                passed++;
                Debug.Log($"<color=green>[PASS]</color> {r.Name}: {r.Details}");
            }
            else
            {
                failed++;
                Debug.LogError($"<color=red>[FAIL]</color> {r.Name}: {r.Details}");
            }
        }
        Debug.Log($"[Test Summary] Total: {results.Count}, Passed: {passed}, Failed: {failed}");
    }
}
