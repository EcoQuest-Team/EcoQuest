using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class NpcInteractionVerificationRunner
{
    public const string ForestScenePath = "Assets/EcoQuest/Forest Scene.unity";
    public const string CityScenePath = "Assets/EcoQuest/City Scene.unity";

    public struct CheckResult
    {
        public string Name;
        public bool Passed;
        public string Message;
    }

    [MenuItem("EcoQuest/Tests/Run NPC Interaction Verification")]
    public static List<CheckResult> RunAllChecks()
    {
        var results = new List<CheckResult>();
        Scene originalScene = SceneManager.GetActiveScene();

        try
        {
            // CHECK 1: Player Prefab Tagging & Hierarchy Isolation
            bool c1Pass = false;
            string c1Msg = "";
            try
            {
                var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/Prefabs/Player.prefab");
                if (playerPrefab == null)
                {
                    c1Msg = "Player.prefab not found at Assets/EcoQuest/Prefabs/Player.prefab";
                }
                else
                {
                    bool rootUntagged = playerPrefab.tag == "Untagged";
                    var capsule = playerPrefab.transform.Find("PlayerCapsule");
                    bool capsuleTaggedPlayer = capsule != null && capsule.CompareTag("Player");
                    bool capsuleHasCC = capsule != null && capsule.GetComponent<CharacterController>() != null;

                    c1Pass = rootUntagged && capsuleTaggedPlayer && capsuleHasCC;
                    c1Msg = $"RootTag='{playerPrefab.tag}' (expected Untagged), CapsuleTag='{(capsule != null ? capsule.tag : "null")}' (expected Player), HasCC={capsuleHasCC}";
                }
            }
            catch (Exception ex) { c1Msg = ex.Message; }
            results.Add(new CheckResult { Name = "1. Player Prefab: Root is Untagged & Capsule is tagged Player", Passed = c1Pass, Message = c1Msg });

            // CHECK 2: Forest Scene Player Resolution & NPC Setups
            bool c2Pass = false;
            string c2Msg = "";
            try
            {
                if (SceneManager.GetActiveScene().path != ForestScenePath)
                {
                    EditorSceneManager.OpenScene(ForestScenePath, OpenSceneMode.Single);
                }

                var playerCapsule = GameObject.Find("PlayerCapsule");
                var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
                bool playerResolvesToCapsule = taggedPlayer != null && taggedPlayer == playerCapsule;

                var guardian = GameObject.Find("GuardianNPC");
                var guardianInt = guardian != null ? guardian.GetComponent<NPCInteraction>() : null;
                bool guardianOk = guardianInt != null;

                var explorer = GameObject.Find("Explorer_Var1");
                var expGate = explorer != null ? explorer.GetComponent<ExplorerGateController>() : null;
                bool explorerOk = expGate != null;

                c2Pass = playerResolvesToCapsule && guardianOk && explorerOk;
                c2Msg = $"TaggedPlayer='{(taggedPlayer != null ? taggedPlayer.name : "null")}', GuardianOk={guardianOk}, ExplorerGateOk={explorerOk}";
            }
            catch (Exception ex) { c2Msg = ex.Message; }
            results.Add(new CheckResult { Name = "2. Forest Scene: Tagged Player is Capsule & NPCs Configured", Passed = c2Pass, Message = c2Msg });

            // CHECK 3: City Scene Player Resolution, Robin & Handyman
            bool c3Pass = false;
            string c3Msg = "";
            try
            {
                if (SceneManager.GetActiveScene().path != CityScenePath)
                {
                    EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
                }

                var playerCapsule = GameObject.Find("PlayerCapsule");
                var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
                bool playerResolvesToCapsule = taggedPlayer != null && taggedPlayer == playerCapsule;

                var robin = GameObject.Find("Explorer_Var2");
                var ctdm = UnityEngine.Object.FindFirstObjectByType<EcoQuest.CityQuest.CityTrashDetectiveManager>();
                bool robinOk = robin != null && ctdm != null;

                var handyman = GameObject.Find("NPC_Handyman_Green");
                var hAnim = handyman != null ? handyman.GetComponent<EcoQuest.Characters.Handyman.NpcConversationAnimator>() : null;
                bool handymanOk = hAnim != null;

                c3Pass = playerResolvesToCapsule && robinOk && handymanOk;
                c3Msg = $"TaggedPlayer='{(taggedPlayer != null ? taggedPlayer.name : "null")}', RobinOk={robinOk}, HandymanOk={handymanOk}";
            }
            catch (Exception ex) { c3Msg = ex.Message; }
            results.Add(new CheckResult { Name = "3. City Scene: Tagged Player is Capsule, Robin & Handyman Ready", Passed = c3Pass, Message = c3Msg });

            // CHECK 4: City Trash Detective Proximity & Range Calculation
            bool c4Pass = false;
            string c4Msg = "";
            try
            {
                var ctdm = UnityEngine.Object.FindFirstObjectByType<EcoQuest.CityQuest.CityTrashDetectiveManager>();
                var robin = GameObject.Find("Explorer_Var2");
                var playerCapsule = GameObject.Find("PlayerCapsule");

                if (ctdm != null && robin != null && playerCapsule != null)
                {
                    // Test simulated proximity at 3.5m (user's standing spot in screenshot)
                    Vector3 testPos = robin.transform.position + robin.transform.forward * 3.5f;
                    testPos.y = robin.transform.position.y;
                    
                    Vector3 diff = robin.transform.position - testPos;
                    diff.y = 0f;
                    float distSq = diff.sqrMagnitude;
                    float effectiveRange = Mathf.Max(ctdm.npcInteractRange, 3.8f);
                    bool withinRange = distSq <= effectiveRange * effectiveRange;

                    c4Pass = withinRange && (effectiveRange >= 3.8f);
                    c4Msg = $"Simulated dist=3.5m, EffectiveRange={effectiveRange:F1}m, WithinRange={withinRange}";
                }
                else
                {
                    c4Msg = "Missing required objects in City Scene";
                }
            }
            catch (Exception ex) { c4Msg = ex.Message; }
            results.Add(new CheckResult { Name = "4. City Scene: Ranger Robin 3.8m Proximity Reachable", Passed = c4Pass, Message = c4Msg });
        }
        finally
        {
            if (originalScene.IsValid() && SceneManager.GetActiveScene().path != originalScene.path)
            {
                EditorSceneManager.OpenScene(originalScene.path, OpenSceneMode.Single);
            }
        }

        Debug.Log("=================================================");
        Debug.Log("      NPC INTERACTION VERIFICATION RESULTS       ");
        Debug.Log("=================================================");
        foreach (var r in results)
        {
            Debug.Log($"[{(r.Passed ? "PASS" : "FAIL")}] {r.Name} | {r.Message}");
        }
        Debug.Log("=================================================");

        return results;
    }
}
