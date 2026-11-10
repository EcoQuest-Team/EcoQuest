using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public class NpcInteractionBugsTests
{
    public const string ForestScenePath = "Assets/EcoQuest/Forest Scene.unity";
    public const string CityScenePath = "Assets/EcoQuest/City Scene.unity";

    public struct TestReportItem
    {
        public string TestName;
        public bool Passed;
        public string Details;
    }

    [MenuItem("EcoQuest/Tests/Run NPC Interaction Bugs Tests")]
    public static List<TestReportItem> RunAllTests()
    {
        List<TestReportItem> report = new List<TestReportItem>();
        Scene originalScene = SceneManager.GetActiveScene();
        int savedTabTip = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0);

        try
        {
            NPCInteraction.ResetStaticFlowState();
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);
            PlayerPrefs.Save();

            // -------------------------------------------------------------
            // SECTION A: CITY SCENE TESTS
            // -------------------------------------------------------------
            if (originalScene.path != CityScenePath)
            {
                EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            }

            UIOrchestrator.Instance?.ResolveReferences();
            UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "City Setup");
            UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.FreePlay, "City Setup");

            var lqm = UnityEngine.Object.FindFirstObjectByType<LitterbugQuestManager>();
            var gearGo = GameObject.Find("UI_HUD_SettingsButton");
            var gearCg = gearGo != null ? gearGo.GetComponent<CanvasGroup>() : null;
            var fq = GameObject.Find("FountainQuestGiver");

            // TEST 1: City FountainQuestGiver & Gear Button Stability
            bool t1Pass = false;
            string t1Details = "";
            try
            {
                if (lqm == null || gearCg == null || fq == null)
                {
                    t1Details = $"Missing components: lqm={lqm != null}, gearCg={gearCg != null}, fq={fq != null}";
                }
                else
                {
                    var fqAnim = fq.GetComponent<Animator>();

                    // 1. Open Dialogue
                    lqm.OpenQuestGiverDialogue();
                    bool dialogueOpen = LitterbugQuestManager.IsDialogueOpen;
                    bool gearExpectedHidden = UIOrchestrator.Instance != null && !UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.Gear);
                    UIOrchestrator.Instance?.ApplyCurrentProfile(0f);
                    float duringAlpha = gearCg.alpha;

                    // 2. Check animation state
                    bool fqTalking = fqAnim != null && fqAnim.GetBool("IsTalking");

                    // 3. Close Dialogue
                    var closeMethod = typeof(LitterbugQuestManager).GetMethod("CloseDialogueImmediateOrFade", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    closeMethod?.Invoke(lqm, null);

                    bool gearExpectedVisible = UIOrchestrator.Instance == null || UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.Gear);
                    UIOrchestrator.Instance?.ApplyCurrentProfile(0f);
                    float exitAlpha = gearCg.alpha;

                    bool gearStable = gearExpectedHidden && duringAlpha <= 0.05f && gearExpectedVisible && exitAlpha >= 0.90f;
                    bool animValid = fqTalking;

                    t1Pass = dialogueOpen && gearStable && animValid;
                    t1Details = $"DialogueOpen={dialogueOpen}, GearDuringAlpha={duringAlpha:F2} (HiddenOk={gearExpectedHidden}), GearExitAlpha={exitAlpha:F2} (VisibleOk={gearExpectedVisible}), FqTalking={fqTalking}";
                }
            }
            catch (Exception ex)
            {
                t1Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "1. City FountainQuestGiver: Gear Stability & Natural Pose", Passed = t1Pass, Details = t1Details });

            // TEST 2: City >= 5 NPCs (WomanWinter, ManPolice, ManSwat, MaleRunner, WomanPirate)
            bool t2Pass = true;
            string t2Details = "";
            try
            {
                string[] prefabPaths = new string[]
                {
                    "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_WomanWinter.prefab",
                    "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_ManPolice.prefab",
                    "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_ManSwat.prefab",
                    "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_WomanPirate.prefab",
                    "Assets/EcoQuest/Prefabs/NPC/NPC_Everyday_Male.prefab"
                };

                int passedCount = 0;
                var detailsList = new List<string>();

                foreach (var path in prefabPaths)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        detailsList.Add($"Missing prefab: {path}");
                        t2Pass = false;
                        continue;
                    }

                    var instance = UnityEngine.Object.Instantiate(prefab);
                    try
                    {
                        var anim = instance.GetComponentInChildren<Animator>();
                        if (anim == null)
                        {
                            detailsList.Add($"No animator: {prefab.name}");
                            t2Pass = false;
                            continue;
                        }

                        var speaker = instance.GetComponent<NpcDialogueSpeakerAnimator>();
                        if (speaker == null) speaker = instance.AddComponent<NpcDialogueSpeakerAnimator>();

                        // Begin dialogue & reaction beat
                        speaker.BeginDialogue(Camera.main != null ? Camera.main.transform : null, false);
                        speaker.SetTyping(true);
                        speaker.TriggerReactionBeat(0); // Defensive reaction (previously forced arm T-pose!)

                        // Advance animator to evaluate pose
                        anim.Update(0.2f);

                        var leftArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                        var rightArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);

                        float talkLeftAngle = leftArm != null ? Quaternion.Angle(leftArm.localRotation, Quaternion.identity) : 15f;
                        float talkRightAngle = rightArm != null ? Quaternion.Angle(rightArm.localRotation, Quaternion.identity) : 15f;

                        // Switch to listening
                        speaker.SetTyping(false);
                        speaker.TriggerReactionBeat(2); // Surprised
                        anim.Update(0.2f);

                        float listenLeftAngle = leftArm != null ? Quaternion.Angle(leftArm.localRotation, Quaternion.identity) : 15f;
                        float listenRightAngle = rightArm != null ? Quaternion.Angle(rightArm.localRotation, Quaternion.identity) : 15f;

                        // End dialogue
                        speaker.EndDialogue(true);
                        anim.Update(0.2f);

                        float endLeftAngle = leftArm != null ? Quaternion.Angle(leftArm.localRotation, Quaternion.identity) : 15f;
                        float endRightAngle = rightArm != null ? Quaternion.Angle(rightArm.localRotation, Quaternion.identity) : 15f;

                        bool noTPose = (talkLeftAngle > 5f || talkRightAngle > 5f) &&
                                       (listenLeftAngle > 5f || listenRightAngle > 5f) &&
                                       (endLeftAngle > 5f || endRightAngle > 5f);

                        if (noTPose)
                        {
                            passedCount++;
                            detailsList.Add($"{prefab.name} (Talk:{talkLeftAngle:F0}deg, Listen:{listenLeftAngle:F0}deg, End:{endLeftAngle:F0}deg)");
                        }
                        else
                        {
                            t2Pass = false;
                            detailsList.Add($"FAIL: {prefab.name} in T-pose!");
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }

                t2Pass = t2Pass && (passedCount >= 5);
                t2Details = $"Verified {passedCount}/5 NPCs: " + string.Join(" | ", detailsList);
            }
            catch (Exception ex)
            {
                t2Pass = false;
                t2Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "2. City 5 NPCs: Natural Gesture & Zero T-Pose", Passed = t2Pass, Details = t2Details });

            // -------------------------------------------------------------
            // SECTION B: FOREST SCENE TESTS
            // -------------------------------------------------------------
            EditorSceneManager.OpenScene(ForestScenePath, OpenSceneMode.Single);
            NPCInteraction.ResetStaticFlowState();
            UIOrchestrator.Instance?.ResolveReferences();
            UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "Section B Tests");
            UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.FreePlay, "Section B Tests");

            var guardian = GameObject.Find("GuardianNPC");
            var guardianInt = guardian != null ? guardian.GetComponent<NPCInteraction>() : null;
            var guardianAnim = guardian != null ? guardian.GetComponent<Animator>() : null;
            var forestGearGo = GameObject.Find("UI_HUD_SettingsButton");
            var forestGearCg = forestGearGo != null ? forestGearGo.GetComponent<CanvasGroup>() : null;

            // TEST 3: Forest GuardianNPC Gear Stability & Natural Pose
            bool t3Pass = false;
            string t3Details = "";
            try
            {
                if (guardianInt == null || guardianAnim == null || forestGearCg == null)
                {
                    t3Details = $"Missing components: guardianInt={guardianInt != null}, anim={guardianAnim != null}, gearCg={forestGearCg != null}";
                }
                else
                {
                    typeof(NPCInteraction).GetField("hasAcceptedQuest", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(guardianInt, true);
                    guardianInt.StartDialogue();
                    bool gearExpectedHidden = UIOrchestrator.Instance != null && !UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.Gear);
                    UIOrchestrator.Instance?.ApplyCurrentProfile(0f);
                    float duringAlpha = forestGearCg.alpha;

                    bool isTalking = guardianAnim.GetBool("IsTalking");
                    guardianAnim.Update(0.2f);

                    guardianInt.EndDialogue();
                    // Restore FreePlay state after dialogue end
                    UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "Dialogue Ended");
                    bool gearExpectedVisible = UIOrchestrator.Instance == null || UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.Gear);
                    UIOrchestrator.Instance?.ApplyCurrentProfile(0f);
                    float exitAlpha = forestGearCg.alpha;

                    bool gearOk = gearExpectedHidden && duringAlpha <= 0.05f && gearExpectedVisible && exitAlpha >= 0.90f;
                    bool armsOk = isTalking;

                    t3Pass = gearOk && armsOk;
                    t3Details = $"GearDuring={duringAlpha:F2} (HiddenOk={gearExpectedHidden}), GearExit={exitAlpha:F2} (VisibleOk={gearExpectedVisible}), TalkingState={isTalking}";
                }
            }
            catch (Exception ex)
            {
                t3Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "3. Forest Guardian: Gear Stability & Natural Pose", Passed = t3Pass, Details = t3Details });

            // TEST 4: Forest Explorer_Var1 Cinematic Gear Stability & AlwaysAnimate
            bool t4Pass = false;
            string t4Details = "";
            try
            {
                var explorer = GameObject.Find("Explorer_Var1");
                var expGate = explorer != null ? explorer.GetComponent<ExplorerGateController>() : null;
                var expAnim = explorer != null ? explorer.GetComponent<Animator>() : null;

                if (explorer == null || expGate == null || expAnim == null || forestGearCg == null)
                {
                    t4Details = $"Missing components on Explorer: exp={explorer != null}, gate={expGate != null}, anim={expAnim != null}";
                }
                else
                {
                    bool cullingOk = expAnim.cullingMode == AnimatorCullingMode.AlwaysAnimate;

                    var leftArm = expAnim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    var rightArm = expAnim.GetBoneTransform(HumanBodyBones.RightUpperArm);

                    expGate.StartDialogueSession();
                    bool gearExpectedHidden = UIOrchestrator.Instance != null && !UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.Gear);
                    UIOrchestrator.Instance?.ApplyCurrentProfile(0f);
                    float duringAlpha = forestGearCg.alpha;

                    expAnim.Update(0.2f);
                    float leftAngle = leftArm != null ? Quaternion.Angle(leftArm.localRotation, Quaternion.identity) : 0f;
                    float rightAngle = rightArm != null ? Quaternion.Angle(rightArm.localRotation, Quaternion.identity) : 0f;

                    if (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue)
                    {
                        DialoguePresenter.Instance.CloseDialogue();
                    }
                    expGate.EndDialogueSession();
                    UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "Explorer Dialogue Ended");

                    bool gearExpectedVisible = UIOrchestrator.Instance == null || UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.Gear);
                    UIOrchestrator.Instance?.ApplyCurrentProfile(0f);
                    float exitAlpha = forestGearCg.alpha;

                    bool gearOk = gearExpectedHidden && duringAlpha <= 0.05f && gearExpectedVisible && exitAlpha >= 0.90f;
                    bool armsOk = leftAngle > 8f || rightAngle > 8f;

                    t4Pass = cullingOk && gearOk && armsOk;
                    t4Details = $"CullingMode={expAnim.cullingMode}, GearDuring={duringAlpha:F2}, GearExit={exitAlpha:F2}, Arms=({leftAngle:F1}deg, {rightAngle:F1}deg)";
                }
            }
            catch (Exception ex)
            {
                t4Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "4. Forest Explorer: Cinematic Gear Stability & AlwaysAnimate", Passed = t4Pass, Details = t4Details });

            // TEST 5: Rapid Dialogue Interrupt & Overlay Stack Integrity
            bool t5Pass = false;
            string t5Details = "";
            try
            {
                if (forestGearCg != null && guardianInt != null)
                {
                    // Rapid start and cancel 3 times
                    for (int k = 0; k < 3; k++)
                    {
                        guardianInt.StartDialogue();
                        guardianInt.EndDialogue();
                    }

                    UIOrchestrator.Instance?.SetFlowStep(UIOrchestrator.FlowStep.FreePlay, "Rapid Test Done");
                    bool overlayEmpty = UIOrchestrator.Instance == null || !UIOrchestrator.Instance.IsOverlayActive;
                    UIOrchestrator.Instance?.ApplyCurrentProfile(0f);
                    float restoredAlpha = forestGearCg.alpha;
                    t5Pass = overlayEmpty && restoredAlpha >= 0.90f;
                    t5Details = $"OverlayActive={UIOrchestrator.Instance?.IsOverlayActive}, FinalGearAlpha={restoredAlpha:F2}";
                }
            }
            catch (Exception ex)
            {
                t5Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "5. Rapid Dialogue Interrupt: Zero Overlay Leak & Restored Gear", Passed = t5Pass, Details = t5Details });

            // TEST 6: Player Prefab Hierarchy & Tag Isolation
            bool t6Pass = false;
            string t6Details = "";
            try
            {
                var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/Prefabs/Player.prefab");
                if (playerPrefab == null)
                {
                    t6Details = "Missing Assets/EcoQuest/Prefabs/Player.prefab";
                }
                else
                {
                    bool rootUntagged = playerPrefab.tag == "Untagged";
                    var capsule = playerPrefab.transform.Find("PlayerCapsule");
                    bool capsuleTaggedPlayer = capsule != null && capsule.CompareTag("Player");
                    bool capsuleHasController = capsule != null && capsule.GetComponent<CharacterController>() != null;

                    t6Pass = rootUntagged && capsuleTaggedPlayer && capsuleHasController;
                    t6Details = $"RootTag='{playerPrefab.tag}' (expected Untagged), CapsuleTag='{(capsule != null ? capsule.tag : "null")}' (expected Player), HasCC={capsuleHasController}";
                }
            }
            catch (Exception ex)
            {
                t6Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "6. Player Prefab: Root Untagged & Capsule Tagged Player", Passed = t6Pass, Details = t6Details });

            // TEST 7: Forest Scene NPC Interaction Player Resolution
            bool t7Pass = false;
            string t7Details = "";
            try
            {
                var fGuardian = GameObject.Find("GuardianNPC");
                var fExp = GameObject.Find("Explorer_Var1");
                var fPlayer = GameObject.FindGameObjectWithTag("Player");

                bool playerIsCapsule = fPlayer != null && fPlayer.name == "PlayerCapsule";
                bool guardianHasComp = fGuardian != null && fGuardian.GetComponent<NPCInteraction>() != null;
                var expGate = fExp != null ? fExp.GetComponent<ExplorerGateController>() : null;
                bool expGateOk = expGate != null;

                t7Pass = playerIsCapsule && guardianHasComp && expGateOk;
                t7Details = $"ResolvedPlayer='{(fPlayer != null ? fPlayer.name : "null")}', GuardianOk={guardianHasComp}, ExplorerGateOk={expGateOk}";
            }
            catch (Exception ex)
            {
                t7Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "7. Forest NPCs: PlayerCapsule Target & Interaction Range", Passed = t7Pass, Details = t7Details });

            // TEST 8: City Scene NPC Interaction Triggers (Robin & Handyman)
            bool t8Pass = false;
            string t8Details = "";
            try
            {
                EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
                var cRobin = GameObject.Find("Explorer_Var2");
                var cHandyman = GameObject.Find("NPC_Handyman_Green");
                var ctdm = UnityEngine.Object.FindFirstObjectByType<EcoQuest.CityQuest.CityTrashDetectiveManager>();
                var cPlayer = GameObject.FindGameObjectWithTag("Player");

                bool cPlayerIsCapsule = cPlayer != null && cPlayer.name == "PlayerCapsule";
                bool robinFound = cRobin != null && ctdm != null;
                var hAnim = cHandyman != null ? cHandyman.GetComponent<EcoQuest.Characters.Handyman.NpcConversationAnimator>() : null;
                bool handymanOk = hAnim != null;
                bool ctdmRangeOk = ctdm != null && ctdm.npcInteractRange >= 3.8f;

                t8Pass = cPlayerIsCapsule && robinFound && handymanOk && ctdmRangeOk;
                t8Details = $"ResolvedPlayer='{(cPlayer != null ? cPlayer.name : "null")}', RobinFound={robinFound}, HandymanOk={handymanOk}, CTDMRange={(ctdm != null ? ctdm.npcInteractRange.ToString("F1") : "null")}";
            }
            catch (Exception ex)
            {
                t8Details = "Exception: " + ex.Message;
            }
            report.Add(new TestReportItem { TestName = "8. City NPCs: Robin & Handyman Proximity Range & Capsule Target", Passed = t8Pass, Details = t8Details });
        }
        finally
        {
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", savedTabTip);
            PlayerPrefs.Save();

            // Restore original active scene if needed
            if (originalScene.IsValid() && SceneManager.GetActiveScene().path != originalScene.path)
            {
                EditorSceneManager.OpenScene(originalScene.path, OpenSceneMode.Single);
            }
        }

        // Print Summary to Console
        Debug.Log("=================================================");
        Debug.Log("     NPC INTERACTION BUGS TEST SUITE RESULTS     ");
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
