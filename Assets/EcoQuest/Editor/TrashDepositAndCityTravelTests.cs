using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public class TrashDepositAndCityTravelTests
{
    public const string ForestScenePath = "Assets/EcoQuest/Forest Scene.unity";
    public const string CityScenePath = "Assets/EcoQuest/City Scene.unity";

    public struct TestReportItem
    {
        public string TestName;
        public bool Passed;
        public string Details;
    }

    [MenuItem("EcoQuest/Tests/Run Trash Deposit & City Travel Tests")]
    public static List<TestReportItem> RunAllTests()
    {
        List<TestReportItem> report = new List<TestReportItem>();
        Scene originalScene = SceneManager.GetActiveScene();

        try
        {
            // =========================================================================
            // TEST SUITE 1: FOREST TRASH DEPOSIT IN BIN (TASK 1)
            // =========================================================================
            EditorSceneManager.OpenScene(ForestScenePath, OpenSceneMode.Single);
            NPCInteraction.ResetStaticFlowState();
            TrashItem.ResetStaticState();
            TrashItem.AreTrashItemsAllowed = true;
            TrashItem.ResetAllTrashItems();
            QuestManager.ResetAllQuestCountsToZero();
            QuestManager.ResetStaticState();
            UIOrchestrator.Instance?.ResolveReferences();
            UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "Test Setup");
            UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.QuestActive, "Test Setup");

            var bioBin = GameObject.Find("Biodegradable");
            var bioQm = bioBin != null ? bioBin.GetComponent<QuestManager>() : null;

            // 1.1 Deposit 1 item: stays visible, kinematic, colliders disabled, counted once
            bool t1_1Pass = false;
            string t1_1Details = "";
            try
            {
                var banana = GameObject.Find("Banana Peel");
                if (bioQm == null || banana == null)
                {
                    t1_1Details = $"Missing bioQm={bioQm != null}, banana={banana != null}";
                }
                else
                {
                    int initialCount = QuestManager.GetCategoryCurrentCount("Biodegradable");
                    var drag = banana.GetComponent<DragAndDrop>();
                    var trash = banana.GetComponent<TrashItem>();

                    // Simulate correct deposit
                    int slotIndex = bioQm.DepositedInBin.Count;
                    Vector3 slotPos = bioQm.GetDepositSlotPosition(slotIndex);
                    Quaternion slotRot = bioQm.GetDepositSlotRotation(slotIndex);
                    drag.OnDepositedCorrectly(slotPos, slotRot, bioQm);

                    bool isDeposited = trash.IsDeposited;
                    bool isActive = banana.activeSelf;
                    bool isKinematic = drag.GetComponent<Rigidbody>().isKinematic;
                    bool collidersDisabled = true;
                    foreach (var c in banana.GetComponentsInChildren<Collider>())
                    {
                        if (c.enabled) collidersDisabled = false;
                    }
                    bool parentedToBin = banana.transform.IsChildOf(bioBin.transform);
                    float distToOpening = Vector3.Distance(banana.transform.position, slotPos);

                    t1_1Pass = isDeposited && isActive && isKinematic && collidersDisabled && parentedToBin && distToOpening < 0.05f;
                    t1_1Details = $"Active={isActive}, Deposited={isDeposited}, Kinematic={isKinematic}, ColsDisabled={collidersDisabled}, InBin={parentedToBin}, SlotDist={distToOpening:F3}m";
                }
            }
            catch (Exception ex)
            {
                t1_1Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "1.1 Single Trash Deposit: Visible in Bin & Kinematic / Colliders Off", Passed = t1_1Pass, Details = t1_1Details });

            // 1.2 Deposit multiple items: Pile slot offsets & Cap of 8
            bool t1_2Pass = false;
            string t1_2Details = "";
            try
            {
                if (bioQm != null)
                {
                    List<Vector3> slotPositions = new List<Vector3>();
                    for (int i = 0; i < 8; i++)
                    {
                        slotPositions.Add(bioQm.GetDepositSlotPosition(i));
                    }

                    // Check that slots are spread out (no duplicate slot positions)
                    bool slotsSpread = true;
                    for (int i = 0; i < slotPositions.Count; i++)
                    {
                        for (int j = i + 1; j < slotPositions.Count; j++)
                        {
                            if (Vector3.Distance(slotPositions[i], slotPositions[j]) < 0.02f)
                            {
                                slotsSpread = false;
                            }
                        }
                    }

                    // Verify serialized options exist with expected defaults
                    bool keepVisibleDefault = bioQm.KeepDepositedItemsVisible;
                    bool vanishDefault = bioQm.VanishAfterAnimation;
                    int maxCap = bioQm.MaxDepositedItemsVisible;

                    t1_2Pass = slotsSpread && keepVisibleDefault && !vanishDefault && (maxCap == 8);
                    t1_2Details = $"SlotsSpread={slotsSpread}, KeepVisibleDefault={keepVisibleDefault}, VanishDefault={vanishDefault}, MaxCap={maxCap}";
                }
            }
            catch (Exception ex)
            {
                t1_2Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "1.2 Bin Pile Geometry & Serialized Options (Cap=8, KeepVisible=True)", Passed = t1_2Pass, Details = t1_2Details });

            // 1.3 Deposited item visibility preserved during flow / dialogue / IntroSequenceController refresh
            bool t1_3Pass = false;
            string t1_3Details = "";
            try
            {
                var banana = GameObject.Find("Banana Peel");
                if (banana != null)
                {
                    // Call SetTrashVisible(false) as during cinematic/dialogue
                    IntroSequenceController.Instance?.SetTrashVisible(false, false, "Dialogue Overlay");
                    bool stillActiveInDialogue = banana.activeSelf;

                    // Restore
                    IntroSequenceController.Instance?.SetTrashVisible(true, false, "Dialogue Restored");
                    bool stillActiveAfterDialogue = banana.activeSelf;

                    t1_3Pass = stillActiveInDialogue && stillActiveAfterDialogue;
                    t1_3Details = $"StillActiveInDialogue={stillActiveInDialogue}, StillActiveAfterDialogue={stillActiveAfterDialogue}";
                }
            }
            catch (Exception ex)
            {
                t1_3Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "1.3 Deposited Items Immune to Dialogue / Cinematic Trash Hiding", Passed = t1_3Pass, Details = t1_3Details });

            // 1.4 Quest Reset Restores Deposited Items Cleanly
            bool t1_4Pass = false;
            string t1_4Details = "";
            try
            {
                var banana = GameObject.Find("Banana Peel");
                if (banana != null)
                {
                    QuestManager.ResetAllQuestCountsToZero();
                    TrashItem.ResetAllTrashItems();

                    var trash = banana.GetComponent<TrashItem>();
                    var drag = banana.GetComponent<DragAndDrop>();

                    bool notDeposited = !trash.IsDeposited;
                    bool collidersRestored = false;
                    foreach (var c in banana.GetComponentsInChildren<Collider>())
                    {
                        if (c.enabled) collidersRestored = true;
                    }
                    bool parentRestored = !banana.transform.IsChildOf(bioBin.transform);

                    t1_4Pass = notDeposited && collidersRestored && parentRestored;
                    t1_4Details = $"IsDeposited={trash.IsDeposited}, CollidersRestored={collidersRestored}, ParentRestored={parentRestored}";
                }
            }
            catch (Exception ex)
            {
                t1_4Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "1.4 Reset: Restores Original Parent, Colliders, and Pickable State", Passed = t1_4Pass, Details = t1_4Details });

            // =========================================================================
            // TEST SUITE 2: CITY SCENE TRAVEL & HUD STABILITY (TASK 2)
            // =========================================================================
            EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);

            // 2.1 Direct Play in City Scene: Minimap & Settings button visible, zero duplicate managers
            bool t2_1Pass = false;
            string t2_1Details = "";
            try
            {
                TravelGate.CleanDuplicateManagers();
                UIOrchestrator.Instance?.ResolveReferences();
                UIOrchestrator.Instance?.ClearAllOverlays();
                UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "Direct City Play");
                UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.FreePlay, "Direct City Play");
                UIOrchestrator.Instance?.ApplyCurrentProfile(0f);

                bool healthCheck = TravelGate.RunPostTravelHealthCheck(out string reportMsg);
                var mapGo = GameObject.Find("UI_Minimap");
                var mapCg = mapGo != null ? mapGo.GetComponent<CanvasGroup>() : null;
                var gearGo = GameObject.Find("UI_HUD_SettingsButton");
                var gearCg = gearGo != null ? gearGo.GetComponent<CanvasGroup>() : null;

                bool mapOk = mapGo != null && mapGo.activeInHierarchy && (mapCg == null || mapCg.alpha >= 0.95f);
                bool gearOk = gearGo != null && gearGo.activeInHierarchy && (gearCg == null || gearCg.alpha >= 0.95f);

                t2_1Pass = healthCheck && mapOk && gearOk;
                t2_1Details = $"HealthCheck={healthCheck}, MapAlpha={(mapCg != null ? mapCg.alpha : 1f):F2}, GearAlpha={(gearCg != null ? gearCg.alpha : 1f):F2} ({reportMsg})";
            }
            catch (Exception ex)
            {
                t2_1Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "2.1 Direct City Scene: Minimap & Settings Button Fully Visible", Passed = t2_1Pass, Details = t2_1Details });

            // 2.2 Explorer Travel Simulation: Overlay Cleared, Rebound CanvasGroups, Health Check PASS
            bool t2_2Pass = false;
            string t2_2Details = "";
            try
            {
                // Simulate Forest Travel push
                UIOrchestrator.Instance?.PushOverlay("ExplorerTravelGateSim");
                HudVisibilityService.Instance?.HideHUD("TravelSim", 0.1f);

                // Simulate Post-Load SceneReady execution
                TravelGate.CleanDuplicateManagers();
                UIOrchestrator.Instance?.ClearAllOverlays();
                HudVisibilityService.Instance?.ClearAllReasons();
                UIOrchestrator.Instance?.ResolveReferences();
                HudVisibilityService.Instance?.ResolveHudElements();
                UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "PostTravel Sim");
                UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.FreePlay, "PostTravel Sim");
                UIOrchestrator.Instance?.ApplyCurrentProfile(0f);

                bool healthPass = TravelGate.RunPostTravelHealthCheck(out string healthDetails);
                bool overlayEmpty = UIOrchestrator.Instance != null && !UIOrchestrator.Instance.IsOverlayActive;
                bool hudUnblocked = HudVisibilityService.Instance == null || !HudVisibilityService.Instance.IsHUDHidden;

                t2_2Pass = healthPass && overlayEmpty && hudUnblocked;
                t2_2Details = $"PostTravelHealth={healthPass}, OverlayEmpty={overlayEmpty}, HudUnblocked={hudUnblocked} ({healthDetails})";
            }
            catch (Exception ex)
            {
                t2_2Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "2.2 Post-Travel SceneReady: Zero Overlay Leak & Health Check PASS", Passed = t2_2Pass, Details = t2_2Details });

            // 2.3 City Dialogue / Cinematic Integration: Stable HUD Restore
            bool t2_3Pass = false;
            string t2_3Details = "";
            try
            {
                var lqm = UnityEngine.Object.FindFirstObjectByType<LitterbugQuestManager>();
                var gearGo = GameObject.Find("UI_HUD_SettingsButton");
                var gearCg = gearGo != null ? gearGo.GetComponent<CanvasGroup>() : null;

                if (lqm != null && gearCg != null)
                {
                    lqm.OpenQuestGiverDialogue();
                    float duringDialogueAlpha = gearCg.alpha;

                    var closeMethod = typeof(LitterbugQuestManager).GetMethod("CloseDialogueImmediateOrFade", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    closeMethod?.Invoke(lqm, null);

                    UIOrchestrator.Instance?.ApplyCurrentProfile(0f);
                    float exitDialogueAlpha = gearCg.alpha;

                    bool stable = duringDialogueAlpha <= 0.05f && exitDialogueAlpha >= 0.95f;
                    t2_3Pass = stable;
                    t2_3Details = $"DuringDialogueAlpha={duringDialogueAlpha:F2}, ExitDialogueAlpha={exitDialogueAlpha:F2}";
                }
                else
                {
                    t2_3Details = "LQM or Gear CanvasGroup not found";
                }
            }
            catch (Exception ex)
            {
                t2_3Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "2.3 City Dialogue HUD Restore: Gear Hidden During, Visible on Exit", Passed = t2_3Pass, Details = t2_3Details });

            // =========================================================================
            // TEST SUITE 3: SCENE CLEANUP VERIFICATION (TASK 3)
            // =========================================================================
            bool t3Pass = false;
            string t3Details = "";
            try
            {
                string b1 = "Assets/EcoQuest/City Scene_backup.unity";
                string b2 = "Assets/EcoQuest/City Scene_final_backup.unity";
                string b3 = "Assets/EcoQuest/City Scene_hands_backup.unity";

                bool b1Exists = File.Exists(Application.dataPath + "/EcoQuest/City Scene_backup.unity");
                bool b2Exists = File.Exists(Application.dataPath + "/EcoQuest/City Scene_final_backup.unity");
                bool b3Exists = File.Exists(Application.dataPath + "/EcoQuest/City Scene_hands_backup.unity");

                bool mainSceneExists = File.Exists(Application.dataPath + "/EcoQuest/City Scene.unity");
                bool dataFolderExists = Directory.Exists(Application.dataPath + "/EcoQuest/City Scene");

                int buildCount = EditorBuildSettings.scenes.Length;
                bool buildOrderOk = buildCount == 3 && 
                                    EditorBuildSettings.scenes[0].path == "Assets/EcoQuest/Forest Scene.unity" &&
                                    EditorBuildSettings.scenes[1].path == "Assets/EcoQuest/City Scene.unity";

                bool backupsDeleted = !b1Exists && !b2Exists && !b3Exists;
                bool workingPreserved = mainSceneExists && dataFolderExists;

                t3Pass = backupsDeleted && workingPreserved && buildOrderOk;
                t3Details = $"BackupsDeleted={backupsDeleted}, WorkingScenePreserved={workingPreserved}, DataFolderPreserved={dataFolderExists}, BuildSettingsScenes={buildCount} (OrderOk={buildOrderOk})";
            }
            catch (Exception ex)
            {
                t3Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "3.1 City Scene Cleanup: 3 Backups Deleted, Working Scene & Data Kept", Passed = t3Pass, Details = t3Details });
        }
        finally
        {
            if (originalScene.IsValid() && SceneManager.GetActiveScene().path != originalScene.path)
            {
                EditorSceneManager.OpenScene(originalScene.path, OpenSceneMode.Single);
            }
        }

        // Print Summary to Console
        Debug.Log("=================================================");
        Debug.Log("   TRASH DEPOSIT & CITY TRAVEL TESTS RESULTS     ");
        Debug.Log("=================================================");
        int passCount = 0;
        foreach (var item in report)
        {
            if (item.Passed) passCount++;
            string status = item.Passed ? "<color=green>PASS</color>" : "<color=red>FAIL</color>";
            Debug.Log($"[{status}] {item.TestName}\n   {item.Details}");
        }
        Debug.Log($"Total: {passCount}/{report.Count} tests PASSED.");
        Debug.Log("=================================================");

        return report;
    }
}
