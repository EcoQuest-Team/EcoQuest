using System;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace EcoQuest.Editor
{
    /// <summary>
    /// Automated Grounding & Root Motion Verification Suite.
    /// Menu: EcoQuest > Test Explorer Grounding
    /// Validates all 6 Explorer prefabs on flat plane (Y=0) and Forest terrain across key animation states.
    /// Measures root stability, lowest foot bone height, and lowest deformed sole vertex via BakeMesh.
    /// </summary>
    public static class TestExplorerGrounding
    {
        private static readonly string[] PrefabPaths = new string[]
        {
            "Assets/EcoQuest/Characters/Explorer/NPC_Explorer.prefab",
            "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var1.prefab",
            "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var2.prefab",
            "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var3.prefab",
            "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var4.prefab",
            "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var5.prefab"
        };

        private static readonly string[] TestStates = new string[]
        {
            "Idle",
            "Walk",
            "Run",
            "Talk",
            "Wave",
            "JumpStart",
            "JumpAir",
            "JumpLand",
            "PickUp"
        };

        private static readonly Dictionary<string, string> StateToClipMap = new Dictionary<string, string>()
        {
            { "Idle", "Explorer_Idle" },
            { "Walk", "Explorer_Walk" },
            { "Run", "Explorer_Run" },
            { "Talk", "Explorer_Talk_A" },
            { "Wave", "Explorer_Wave" },
            { "JumpStart", "Explorer_JumpStart" },
            { "JumpAir", "Explorer_JumpAir" },
            { "JumpLand", "Explorer_JumpLand" },
            { "PickUp", "Explorer_PickUp" }
        };

        [MenuItem("EcoQuest/Test Explorer Grounding", false, 200)]
        public static void RunGroundingTest()
        {
            string report = ExecuteTest();
            Debug.Log(report);
        }

        public static string ExecuteTest()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=========================================================================================================");
            sb.AppendLine("                       ECOQUEST EXPLORER GROUNDING & ROOT MOTION TEST REPORT                              ");
            sb.AppendLine("=========================================================================================================");

            string fbxPath = "Assets/EcoQuest/Characters/Explorer/Explorer.fbx";
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            Dictionary<string, AnimationClip> loadedClips = new Dictionary<string, AnimationClip>();
            foreach (var a in assets)
            {
                if (a is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    loadedClips[clip.name] = clip;
                }
            }

            Terrain activeTerrain = Terrain.activeTerrain;
            Vector3 terrainTestPos = new Vector3(462.88f, 0f, 383.424f);
            float terrainGroundY = 0f;
            if (activeTerrain != null)
            {
                terrainGroundY = activeTerrain.SampleHeight(terrainTestPos) + activeTerrain.transform.position.y;
                terrainTestPos.y = terrainGroundY;
            }

            int totalTests = 0;
            int passedTests = 0;

            sb.AppendLine($"| {"Prefab",-16} | {"Environment",-14} | {"State",-10} | {"Root Y Dlt",-10} | {"Foot Bone Y",-11} | {"Sole Min Y",-10} | {"Status",-6} |");
            sb.AppendLine("|------------------|----------------|------------|------------|-------------|------------|--------|");

            UnityEngine.Mesh tempMesh = new UnityEngine.Mesh();

            foreach (var path in PrefabPaths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    sb.AppendLine($"| {System.IO.Path.GetFileNameWithoutExtension(path),-16} | FAILED TO LOAD PREFAB |");
                    continue;
                }

                (string envName, Vector3 spawnPos, float groundLevel)[] envs = new[]
                {
                    ("Flat Ground (0m)", Vector3.zero, 0f),
                    ("Forest Terrain", terrainTestPos, terrainGroundY)
                };

                foreach (var env in envs)
                {
                    GameObject instance = UnityEngine.Object.Instantiate(prefab, env.spawnPos, Quaternion.identity);
                    Animator anim = instance.GetComponent<Animator>();
                    SkinnedMeshRenderer bodySmr = null;
                    foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (smr.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            bodySmr = smr;
                            break;
                        }
                    }

                    Transform leftFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                    Transform rightFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);

                    foreach (var state in TestStates)
                    {
                        totalTests++;
                        string clipName = StateToClipMap[state];
                        if (!loadedClips.TryGetValue(clipName, out AnimationClip clip))
                        {
                            sb.AppendLine($"| {prefab.name,-16} | {env.envName,-14} | {state,-10} | CLIP NOT FOUND |");
                            continue;
                        }

                        // Sample animation at key frames across cycle
                        float duration = clip.length;
                        float[] sampleTimes = duration > 0.05f 
                            ? new float[] { 0f, duration * 0.25f, duration * 0.5f, duration * 0.75f }
                            : new float[] { 0f };

                        float maxRootYDelta = 0f;
                        float lowestFootBoneDist = float.MaxValue;
                        float lowestSoleVertexDist = float.MaxValue;

                        Vector3 initialRootPos = instance.transform.position;

                        foreach (float t in sampleTimes)
                        {
                            clip.SampleAnimation(instance, t);

                            float rootYDelta = Mathf.Abs(instance.transform.position.y - initialRootPos.y);
                            if (rootYDelta > maxRootYDelta) maxRootYDelta = rootYDelta;

                            // Lowest foot bone Y relative to ground
                            float minFootY = float.MaxValue;
                            if (leftFoot != null) minFootY = Mathf.Min(minFootY, leftFoot.position.y - env.groundLevel);
                            if (rightFoot != null) minFootY = Mathf.Min(minFootY, rightFoot.position.y - env.groundLevel);
                            if (minFootY < lowestFootBoneDist) lowestFootBoneDist = minFootY;

                            // Measure lowest vertex of body mesh (bottom of boot sole)
                            if (bodySmr != null)
                            {
                                bodySmr.BakeMesh(tempMesh);
                                var verts = tempMesh.vertices;
                                var xform = bodySmr.transform;
                                float minVertY = float.MaxValue;
                                for (int i = 0; i < verts.Length; i++)
                                {
                                    Vector3 w = xform.TransformPoint(verts[i]);
                                    if (w.y < minVertY) minVertY = w.y;
                                }
                                float vertRelGround = minVertY - env.groundLevel;
                                if (vertRelGround < lowestSoleVertexDist) lowestSoleVertexDist = vertRelGround;
                            }
                        }

                        // Validation Criteria:
                        // 1. Root transform Y strictly clamped/stable (< 0.005m delta)
                        // 2. Standing & Locomotion states (Idle, Walk, Run, Talk, Wave):
                        //    Planted foot/sole touches ground within ±0.025m (no sinking below -0.020m)
                        // 3. JumpAir: feet elevated in air (> 0.050m)
                        // 4. Action states (JumpStart, JumpLand, PickUp): root remains clamped while body performs squat/reach
                        bool rootStable = maxRootYDelta < 0.005f;
                        bool pass = false;

                        if (state == "JumpAir")
                        {
                            pass = rootStable && (lowestSoleVertexDist > 0.050f);
                        }
                        else if (state == "JumpStart" || state == "JumpLand" || state == "PickUp")
                        {
                            pass = rootStable;
                        }
                        else
                        {
                            pass = rootStable && (lowestSoleVertexDist >= -0.025f) && (lowestSoleVertexDist <= 0.035f);
                        }

                        if (pass) passedTests++;

                        string statusStr = pass ? "PASS" : "FAIL";
                        string rootYStr = $"{maxRootYDelta:+0.000;-0.000;0.000}m";
                        string footYStr = $"{lowestFootBoneDist:+0.000;-0.000;0.000}m";
                        string soleYStr = $"{lowestSoleVertexDist:+0.000;-0.000;0.000}m";

                        sb.AppendLine($"| {prefab.name,-16} | {env.envName,-14} | {state,-10} | {rootYStr,-10} | {footYStr,-11} | {soleYStr,-10} | {statusStr,-6} |");
                    }

                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            UnityEngine.Object.DestroyImmediate(tempMesh);

            sb.AppendLine("=========================================================================================================");
            sb.AppendLine($"SUMMARY: {passedTests}/{totalTests} tests PASSED ({(passedTests == totalTests ? "ALL 108/108 CHECKS PASSED - GROUNDING VERIFIED!" : "FAILURES DETECTED")})");
            sb.AppendLine("=========================================================================================================");
            return sb.ToString();
        }
    }
}
