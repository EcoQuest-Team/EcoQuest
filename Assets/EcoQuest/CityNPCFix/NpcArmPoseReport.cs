#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace EcoQuest.CityNPCFix
{
    public static class NpcArmPoseReport
    {
        private const string ReportDir = @"C:\EcoQuest.gg\Reports\NPC";
        private static readonly float[] SampleFractions = { 0f, 0.25f, 0.50f, 0.75f, 1.0f };

        public struct ArmFrameMetrics
        {
            public float fraction;
            public float leftStretchPct;
            public float rightStretchPct;
            public float leftElbowDeg;
            public float rightElbowDeg;
            public float leftWristTwistDeg;
            public float rightWristTwistDeg;
            public float leftWristTorsoDist;
            public float rightWristTorsoDist;
            public bool leftHandInTorso;
            public bool rightHandInTorso;
        }

        public struct ClipAuditResult
        {
            public string npcName;
            public string prefabPath;
            public string avatarName;
            public string clipName;
            public float bindLeftArmLen;
            public float bindRightArmLen;
            public float maxStretchPct;
            public float minElbowDeg;
            public float maxElbowDeg;
            public float maxWristTwistDeg;
            public int handInBodyFrames;
            public bool passed;
            public string failReason;
            public ArmFrameMetrics[] frames;
        }

        [MenuItem("EcoQuest/NPC/Arm Pose Report", false, 10)]
        public static void RunArmPoseReport()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[NpcArmPoseReport] Please exit Play Mode before running Arm Pose Report.");
                return;
            }

            Directory.CreateDirectory(ReportDir);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# EcoQuest NPC Arm Pose Report");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            // 1. Detailed Handyman Wave Evidence + Render
            var handymanResults = AuditSinglePrefab(
                "NPC_Handyman_Green",
                "Assets/EcoQuest/Characters/Handyman/Variants/NPC_Handyman_Green.prefab",
                sb,
                detailedLog: true);

            RenderHandymanWaveSheet(
                "Assets/EcoQuest/Characters/Handyman/Variants/NPC_Handyman_Green.prefab",
                Path.Combine(ReportDir, "Handyman_Wave.png"));

            // 2. Full Audit of ALL NPC families and arm-heavy clips
            List<ClipAuditResult> allResults = RunFullAuditAllNpcs(sb);

            // 3. Render Contact Sheet for each NPC family
            RenderNpcContactSheet(Path.Combine(ReportDir, "ContactSheet.png"));

            string mdPath = Path.Combine(ReportDir, "ArmPoseReport.md");
            File.WriteAllText(mdPath, sb.ToString());
            Debug.Log($"[NpcArmPoseReport] Report written to {mdPath}\n" + sb.ToString());
        }

        [MenuItem("EcoQuest/NPC/Fix All NPC Arm Rigs & Clips", false, 12)]
        public static void FixAllNpcArmRigsAndClips()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[NpcArmPoseReport] Please exit Play Mode first.");
                return;
            }

            // 1. Configure Handyman FBX + Prefabs + Controller via menu item
            EditorApplication.ExecuteMenuItem("Tools/EcoQuest/Setup Handyman NPC");

            // 2. Configure shared Humanoid Avatar settings (armStretch = 0.01f, natural twist distribution) on all NPC FBX rigs
            string[] npcRigFbxPaths = new[]
            {
                "Assets/EcoQuest/Characters/Handyman/Handyman.fbx",
                "Assets/EcoQuest/Characters/Explorer/Explorer.fbx",
                "Assets/EverydayMotionPack/Model/Chatacter/male.FBX",
                "Assets/EverydayMotionPack/Model/Chatacter/female.FBX",
                "Assets/Characters/Ranger/Ranger_final.fbx",
                "Assets/LowPolyCivilians/Models/Male_Civilian.FBX",
                "Assets/LowPolyCivilians/Models/Female_Civilian.FBX",
                "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/girl-winter_Rig.fbx",
                "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-coat_Rig.fbx",
                "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-doctor_Rig.fbx",
                "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-fire_Rig.fbx",
                "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-lord-rival_Rig.fbx",
                "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-police_Rig.fbx",
                "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-sir_Rig.fbx",
                "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-swat_Rig.fbx",
                "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/woman-pirate-scarf_Rig.fbx",
                "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/woman-winter-coat_Rig.fbx"
            };

            foreach (string fbxPath in npcRigFbxPaths)
            {
                ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer == null) continue;

                HumanDescription hd = importer.humanDescription;
                bool changed = false;
                if (Mathf.Abs(hd.armStretch - 0.01f) > 0.001f) { hd.armStretch = 0.01f; changed = true; }
                if (Mathf.Abs(hd.legStretch - 0.02f) > 0.001f) { hd.legStretch = 0.02f; changed = true; }
                if (Mathf.Abs(hd.upperArmTwist - 0.40f) > 0.001f) { hd.upperArmTwist = 0.40f; changed = true; }
                if (Mathf.Abs(hd.lowerArmTwist - 0.40f) > 0.001f) { hd.lowerArmTwist = 0.40f; changed = true; }

                // Ensure LowerArm muscle limits use valid Unity defaults
                HumanBone[] bones = hd.human;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (!bones[i].limit.useDefaultValues)
                    {
                        bones[i].limit.useDefaultValues = true;
                        changed = true;
                    }
                }
                hd.human = bones;

                // For Female_Civilian, calibrate T-pose forearm reference angle (+4 deg Z) so happy clip never hyperextends > 175 deg
                if (fbxPath.Contains("Female_Civilian"))
                {
                    SkeletonBone[] skel = hd.skeleton;
                    for (int i = 0; i < skel.Length; i++)
                    {
                        if (skel[i].name == "Bip01 L Forearm" || skel[i].name == "Bip01 R Forearm")
                        {
                            skel[i].rotation = Quaternion.Euler(0f, 0f, 4f);
                            changed = true;
                        }
                    }
                    hd.skeleton = skel;
                }

                if (changed)
                {
                    importer.humanDescription = hd;
                    importer.SaveAndReimport();
                }
            }

            // 3. Configure EverydayMotionPack shared interaction/emotion clip FBX import settings (Bake Into Pose + sourceAvatar)
            Avatar femaleSourceAvatar = null;
            var femImporter = AssetImporter.GetAtPath("Assets/EverydayMotionPack/Model/Chatacter/female.FBX") as ModelImporter;
            if (femImporter != null)
            {
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath("Assets/EverydayMotionPack/Model/Chatacter/female.FBX"))
                    if (a is Avatar av) { femaleSourceAvatar = av; break; }
            }

            Avatar maleSourceAvatar = null;
            var maleImporter = AssetImporter.GetAtPath("Assets/EverydayMotionPack/Model/Chatacter/male.FBX") as ModelImporter;
            if (maleImporter != null)
            {
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath("Assets/EverydayMotionPack/Model/Chatacter/male.FBX"))
                    if (a is Avatar av) { maleSourceAvatar = av; break; }
            }

            string[] everydayClipPaths = new[]
            {
                "Assets/EverydayMotionPack/Motion/01_Idle/@female_idle_breath.FBX",
                "Assets/EverydayMotionPack/Motion/04_Interaction/@female_talk_stand.FBX",
                "Assets/EverydayMotionPack/Motion/04_Interaction/@female_greet.FBX",
                "Assets/EverydayMotionPack/Motion/04_Interaction/@female_nod_stand.FBX",
                "Assets/EverydayMotionPack/Motion/03_Emotion/@female_emotion_happy.FBX",
                "Assets/EverydayMotionPack/Motion/01_Idle/@male_idle_breath.FBX",
                "Assets/EverydayMotionPack/Motion/04_Interaction/@male_talk_stand.FBX",
                "Assets/EverydayMotionPack/Motion/04_Interaction/@male_greet.FBX",
                "Assets/EverydayMotionPack/Motion/04_Interaction/@male_nod_stand.FBX",
                "Assets/EverydayMotionPack/Motion/03_Emotion/@male_emotion_happy.FBX"
            };

            foreach (string clipFbx in everydayClipPaths)
            {
                ModelImporter ci = AssetImporter.GetAtPath(clipFbx) as ModelImporter;
                if (ci == null) continue;

                bool isMaleClip = clipFbx.IndexOf("@male_", StringComparison.OrdinalIgnoreCase) >= 0;
                Avatar refAv = isMaleClip ? maleSourceAvatar : femaleSourceAvatar;
                if (refAv != null)
                {
                    ci.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    ci.sourceAvatar = refAv;
                }

                ModelImporterClipAnimation[] clips = ci.clipAnimations;
                if (clips == null || clips.Length == 0) clips = ci.defaultClipAnimations;
                if (clips != null && clips.Length > 0)
                {
                    for (int i = 0; i < clips.Length; i++)
                    {
                        clips[i].lockRootRotation = true;
                        clips[i].keepOriginalOrientation = true;
                        clips[i].lockRootHeightY = true;
                        clips[i].keepOriginalPositionY = true;
                        clips[i].lockRootPositionXZ = true;
                        clips[i].keepOriginalPositionXZ = true;
                    }
                    ci.clipAnimations = clips;
                }
                ci.SaveAndReimport();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[NpcArmPoseReport] Finished FixAllNpcArmRigsAndClips.");
        }

        [MenuItem("EcoQuest/NPC/Validate NPC Arm Poses", false, 11)]
        public static void ValidateNpcArmPoses()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[ValidateNpcArmPoses] Please exit Play Mode before running validation.");
                return;
            }

            StringBuilder sb = new StringBuilder();
            List<ClipAuditResult> results = RunFullAuditAllNpcs(sb);

            int passCount = 0;
            int failCount = 0;
            StringBuilder failLog = new StringBuilder();

            foreach (var r in results)
            {
                if (r.passed) passCount++;
                else
                {
                    failCount++;
                    failLog.AppendLine($"  FAIL: {r.npcName} | {r.clipName} -> {r.failReason} (stretch={r.maxStretchPct:F1}%, elbow={r.minElbowDeg:F1}..{r.maxElbowDeg:F1} deg, twist={r.maxWristTwistDeg:F1} deg, inBody={r.handInBodyFrames})");
                }
            }

            Directory.CreateDirectory(ReportDir);
            File.WriteAllText(Path.Combine(ReportDir, "ValidationLatest.md"), sb.ToString());

            if (failCount == 0)
            {
                Debug.Log($"<color=green>[ValidateNpcArmPoses] PASS: All {passCount}/{results.Count} NPC arm-heavy clip checks passed thresholds (stretch < 8%, 10 <= elbow <= 175 deg, twist < 90 deg, 0 hand-in-body frames).</color>");
            }
            else
            {
                Debug.LogWarning($"[ValidateNpcArmPoses] {failCount} FAILURES out of {results.Count} checks ({passCount} passed):\n{failLog}");
            }
        }

        public static List<ClipAuditResult> RunFullAuditAllNpcs(StringBuilder sb)
        {
            var results = new List<ClipAuditResult>();

            // Build canonical list of every NPC in the project:
            // - Handyman + variants
            // - Explorer + variants
            // - Guardian / Ranger
            // - All 20 City NPC models
            var npcList = new List<(string label, string path)>
            {
                ("NPC_Handyman_Green", "Assets/EcoQuest/Characters/Handyman/Variants/NPC_Handyman_Green.prefab"),
                ("NPC_Handyman", "Assets/EcoQuest/Characters/Handyman/NPC_Handyman.prefab"),
                ("NPC_Handyman_Red", "Assets/EcoQuest/Characters/Handyman/Variants/NPC_Handyman_Red.prefab"),
                ("NPC_Handyman_Blue", "Assets/EcoQuest/Characters/Handyman/Variants/NPC_Handyman_Blue.prefab"),
                ("NPC_Handyman_NoSatchel", "Assets/EcoQuest/Characters/Handyman/Variants/NPC_Handyman_NoSatchel.prefab"),

                ("NPC_Explorer", "Assets/EcoQuest/Characters/Explorer/NPC_Explorer.prefab"),
                ("Explorer_Var1 (Forest)", "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var1.prefab"),
                ("Explorer_Var2 (Robin)", "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var2.prefab"),
                ("Explorer_Var3", "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var3.prefab"),
                ("Explorer_Var4", "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var4.prefab"),
                ("Explorer_Var5", "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var5.prefab"),

                ("Guardian (EverydayMale)", "Assets/EcoQuest/Prefabs/NPC/NPC_Everyday_Male.prefab"),
                ("NPC_Ranger", "Assets/EcoQuest/Prefabs/NPC/NPC_Ranger.prefab"),
                ("NPC_Everyday_Female", "Assets/EcoQuest/Prefabs/NPC/NPC_Everyday_Female.prefab"),

                ("NPC_Male01", "Assets/EcoQuest/Prefabs/NPC/NPC_Male01.prefab"),
                ("NPC_Male02", "Assets/EcoQuest/Prefabs/NPC/NPC_Male02.prefab"),
                ("NPC_Male03", "Assets/EcoQuest/Prefabs/NPC/NPC_Male03.prefab"),
                ("NPC_Male04", "Assets/EcoQuest/Prefabs/NPC/NPC_Male04.prefab"),
                ("NPC_Male05", "Assets/EcoQuest/Prefabs/NPC/NPC_Male05.prefab"),
                ("NPC_Female01", "Assets/EcoQuest/Prefabs/NPC/NPC_Female01.prefab"),
                ("NPC_Female02", "Assets/EcoQuest/Prefabs/NPC/NPC_Female02.prefab"),
                ("NPC_Female03", "Assets/EcoQuest/Prefabs/NPC/NPC_Female03.prefab"),

                ("NPC_Poly_GirlWinter", "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_GirlWinter.prefab"),
                ("NPC_Poly_ManCoat", "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_ManCoat.prefab"),
                ("NPC_Poly_ManDoctor", "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_ManDoctor.prefab"),
                ("NPC_Poly_ManFire", "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_ManFire.prefab"),
                ("NPC_Poly_ManLord", "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_ManLord.prefab"),
                ("NPC_Poly_ManPolice", "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_ManPolice.prefab"),
                ("NPC_Poly_ManSir", "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_ManSir.prefab"),
                ("NPC_Poly_ManSwat", "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_ManSwat.prefab"),
                ("NPC_Poly_WomanPirate", "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_WomanPirate.prefab"),
                ("NPC_Poly_WomanWinter", "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_WomanWinter.prefab")
            };

            sb.AppendLine("## Summary Table (All NPCs & Arm-Heavy Clips)");
            sb.AppendLine("| NPC | Avatar | Clip | Max Stretch % | Elbow Min..Max (°) | Max Wrist Twist (°) | In-Body Frames | Status |");
            sb.AppendLine("| :--- | :--- | :--- | :---: | :---: | :---: | :---: | :---: |");

            foreach (var item in npcList)
            {
                var npcRes = AuditSinglePrefab(item.label, item.path, sb, detailedLog: false);
                foreach (var r in npcRes)
                {
                    results.Add(r);
                    string status = r.passed ? "PASS" : $"FAIL ({r.failReason})";
                    sb.AppendLine($"| {r.npcName} | {r.avatarName} | {r.clipName} | {r.maxStretchPct:F1}% | {r.minElbowDeg:F1}°..{r.maxElbowDeg:F1}° | {r.maxWristTwistDeg:F1}° | {r.handInBodyFrames} | {status} |");
                }
            }

            return results;
        }

        public static List<ClipAuditResult> AuditSinglePrefab(string npcLabel, string prefabPath, StringBuilder sb, bool detailedLog)
        {
            var list = new List<ClipAuditResult>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[NpcArmPoseReport] Prefab not found: {prefabPath}");
                return list;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = new Vector3(500f, 0f, 500f);
            instance.transform.rotation = Quaternion.identity;

            try
            {
                // Apply gender override controller if NpcBrain is present (matches runtime City NPC setup)
                var brain = instance.GetComponent<NpcBrain>();
                if (brain != null)
                {
                    Gender g = NpcSpawner.ResolveGender(prefab.name);
                    brain.SetGender(g);
                    brain.ApplyGenderController();
                }

                Animator anim = instance.GetComponentInChildren<Animator>();
                if (anim == null || anim.avatar == null || !anim.avatar.isHuman)
                {
                    return list;
                }

                Transform lUpper = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                Transform lLower = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                Transform lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform rUpper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
                Transform rLower = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
                Transform rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                Transform spine = anim.GetBoneTransform(HumanBodyBones.Spine);
                Transform chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? spine;

                if (lUpper == null || lLower == null || lHand == null || rUpper == null || rLower == null || rHand == null)
                {
                    return list;
                }

                // Measure bind-pose arm lengths (upperArm + lowerArm bone lengths AND straight-line distance)
                float bindLeftBoneSum = Vector3.Distance(lUpper.position, lLower.position) + Vector3.Distance(lLower.position, lHand.position);
                float bindRightBoneSum = Vector3.Distance(rUpper.position, rLower.position) + Vector3.Distance(rLower.position, rHand.position);
                float bindLeftDist = Vector3.Distance(lUpper.position, lHand.position);
                float bindRightDist = Vector3.Distance(rUpper.position, rHand.position);

                Quaternion bindLeftHandLocal = Quaternion.Inverse(lLower.rotation) * lHand.rotation;
                Quaternion bindRightHandLocal = Quaternion.Inverse(rLower.rotation) * rHand.rotation;

                var clips = CollectClipsForController(anim.runtimeAnimatorController);
                if (detailedLog)
                {
                    sb.AppendLine($"## Detailed Frame Breakdown: {npcLabel} ({prefabPath})");
                    sb.AppendLine($"- Avatar: `{anim.avatar.name}` | Controller: `{(anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "null")}`");
                    sb.AppendLine($"- Bind Arm Length (Upper+Lower): L = `{bindLeftBoneSum:F4} m` (chord `{bindLeftDist:F4} m`), R = `{bindRightBoneSum:F4} m` (chord `{bindRightDist:F4} m`)");
                    sb.AppendLine();
                }

                foreach (var clip in clips)
                {
                    if (clip == null) continue;
                    if (!IsArmCheckClip(clip.name)) continue;

                    ClipAuditResult res = new ClipAuditResult
                    {
                        npcName = npcLabel,
                        prefabPath = prefabPath,
                        avatarName = anim.avatar.name,
                        clipName = clip.name,
                        bindLeftArmLen = bindLeftBoneSum,
                        bindRightArmLen = bindRightBoneSum,
                        maxStretchPct = 0f,
                        minElbowDeg = 180f,
                        maxElbowDeg = 0f,
                        maxWristTwistDeg = 0f,
                        handInBodyFrames = 0,
                        frames = new ArmFrameMetrics[SampleFractions.Length]
                    };

                    if (detailedLog && (clip.name.Contains("Idle") || clip.name.Contains("Greet") || clip.name.Contains("Wave")))
                    {
                        sb.AppendLine($"### Clip: `{clip.name}` ({clip.length:F2}s)");
                        sb.AppendLine("| Frame % | L Stretch % | R Stretch % | L Elbow (°) | R Elbow (°) | L Wrist Twist (°) | R Wrist Twist (°) | L Torso Dist | R Torso Dist |");
                        sb.AppendLine("| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");
                    }

                    for (int i = 0; i < SampleFractions.Length; i++)
                    {
                        float frac = SampleFractions[i];
                        float t = frac * Mathf.Max(0.001f, clip.length);
                        clip.SampleAnimation(instance, t);

                        // Measure current bone-chain length AND shoulder-to-wrist reach vs bind arm length
                        float curLBoneSum = Vector3.Distance(lUpper.position, lLower.position) + Vector3.Distance(lLower.position, lHand.position);
                        float curRBoneSum = Vector3.Distance(rUpper.position, rLower.position) + Vector3.Distance(rLower.position, rHand.position);
                        float curLDist = Vector3.Distance(lUpper.position, lHand.position);
                        float curRDist = Vector3.Distance(rUpper.position, rHand.position);

                        // Stretch %: max of bone-chain stretch or over-extension beyond bind pose reach
                        float chainStretchL = ((curLBoneSum - bindLeftBoneSum) / Mathf.Max(0.001f, bindLeftBoneSum)) * 100f;
                        float chainStretchR = ((curRBoneSum - bindRightBoneSum) / Mathf.Max(0.001f, bindRightBoneSum)) * 100f;
                        float overReachL = Mathf.Max(0f, ((curLDist - bindLeftBoneSum) / Mathf.Max(0.001f, bindLeftBoneSum)) * 100f);
                        float overReachR = Mathf.Max(0f, ((curRDist - bindRightBoneSum) / Mathf.Max(0.001f, bindRightBoneSum)) * 100f);
                        float stretchL = Mathf.Max(Mathf.Abs(chainStretchL), overReachL);
                        float stretchR = Mathf.Max(Mathf.Abs(chainStretchR), overReachR);

                        // Interior elbow angle: 180 deg = straight arm, < 180 = bent
                        Vector3 lUpVec = (lUpper.position - lLower.position).normalized;
                        Vector3 lForeVec = (lHand.position - lLower.position).normalized;
                        float elbowL = Vector3.Angle(lUpVec, lForeVec);

                        Vector3 rUpVec = (rUpper.position - rLower.position).normalized;
                        Vector3 rForeVec = (rHand.position - rLower.position).normalized;
                        float elbowR = Vector3.Angle(rUpVec, rForeVec);

                        // Wrist orientation relative to forearm compared against bind pose
                        Quaternion curLHandRel = Quaternion.Inverse(lLower.rotation) * lHand.rotation;
                        Quaternion curRHandRel = Quaternion.Inverse(rLower.rotation) * rHand.rotation;
                        float twistL = ComputeWristTwistAndBendMaxDeg(bindLeftHandLocal, curLHandRel, lLower, lHand);
                        float twistR = ComputeWristTwistAndBendMaxDeg(bindRightHandLocal, curRHandRel, rLower, rHand);

                        // Wrist-to-body distance (horizontal distance from spine/chest centerline within torso height)
                        float torsoDistL = ComputeWristToTorsoDistance(lHand.position, hips, chest);
                        float torsoDistR = ComputeWristToTorsoDistance(rHand.position, hips, chest);
                        bool inTorsoL = torsoDistL < 0.085f;
                        bool inTorsoR = torsoDistR < 0.085f;

                        res.frames[i] = new ArmFrameMetrics
                        {
                            fraction = frac,
                            leftStretchPct = stretchL,
                            rightStretchPct = stretchR,
                            leftElbowDeg = elbowL,
                            rightElbowDeg = elbowR,
                            leftWristTwistDeg = twistL,
                            rightWristTwistDeg = twistR,
                            leftWristTorsoDist = torsoDistL,
                            rightWristTorsoDist = torsoDistR,
                            leftHandInTorso = inTorsoL,
                            rightHandInTorso = inTorsoR
                        };

                        res.maxStretchPct = Mathf.Max(res.maxStretchPct, Mathf.Max(Mathf.Abs(stretchL), Mathf.Abs(stretchR)));
                        res.minElbowDeg = Mathf.Min(res.minElbowDeg, Mathf.Min(elbowL, elbowR));
                        res.maxElbowDeg = Mathf.Max(res.maxElbowDeg, Mathf.Max(elbowL, elbowR));
                        res.maxWristTwistDeg = Mathf.Max(res.maxWristTwistDeg, Mathf.Max(twistL, twistR));
                        if (inTorsoL || inTorsoR) res.handInBodyFrames++;

                        if (detailedLog && (clip.name.Contains("Idle") || clip.name.Contains("Greet") || clip.name.Contains("Wave")))
                        {
                            sb.AppendLine($"| {frac * 100f:F0}% | {stretchL:F1}% | {stretchR:F1}% | {elbowL:F1}° | {elbowR:F1}° | {twistL:F1}° | {twistR:F1}° | {torsoDistL:F3}m | {torsoDistR:F3}m |");
                        }
                    }

                    if (detailedLog && (clip.name.Contains("Idle") || clip.name.Contains("Greet") || clip.name.Contains("Wave")))
                    {
                        sb.AppendLine();
                    }

                    List<string> reasons = new List<string>();
                    if (res.maxStretchPct >= 8.0f) reasons.Add($"Stretch {res.maxStretchPct:F1}%>=8%");
                    if (res.minElbowDeg < 10.0f || res.maxElbowDeg > 175.0f) reasons.Add($"Elbow {res.minElbowDeg:F1}..{res.maxElbowDeg:F1}");
                    if (res.maxWristTwistDeg >= 90.0f) reasons.Add($"Twist {res.maxWristTwistDeg:F1}>=90");
                    if (res.handInBodyFrames > 0) reasons.Add($"InBody={res.handInBodyFrames}");

                    res.passed = reasons.Count == 0;
                    res.failReason = string.Join(", ", reasons);
                    list.Add(res);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            return list;
        }

        private static float ComputeWristTwistAndBendMaxDeg(Quaternion bindRel, Quaternion curRel, Transform lowerArm, Transform hand)
        {
            Quaternion delta = Quaternion.Inverse(bindRel) * curRel;
            delta.ToAngleAxis(out float angle, out _);
            if (angle > 180f) angle = 360f - angle;

            // Also check anatomical angle between forearm bone vector and hand bone vector (if child exists)
            if (hand.childCount > 0)
            {
                Vector3 foreDir = (hand.position - lowerArm.position).normalized;
                Vector3 handDir = (hand.GetChild(0).position - hand.position).normalized;
                if (foreDir.sqrMagnitude > 0.001f && handDir.sqrMagnitude > 0.001f)
                {
                    float bendAngle = Vector3.Angle(foreDir, handDir);
                    angle = Mathf.Max(angle, bendAngle);
                }
            }
            return angle;
        }

        private static float ComputeWristToTorsoDistance(Vector3 wristPos, Transform hips, Transform chest)
        {
            if (hips == null || chest == null) return 0.5f;
            Vector3 p0 = hips.position;
            Vector3 p1 = chest.position + Vector3.up * 0.15f;
            float minY = Mathf.Min(p0.y, p1.y) - 0.05f;
            float maxY = Mathf.Max(p0.y, p1.y) + 0.05f;

            // If wrist is outside torso vertical band, return horizontal distance to capsule segment
            Vector3 ab = p1 - p0;
            float t = Mathf.Clamp01(Vector3.Dot(wristPos - p0, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            Vector3 closest = p0 + t * ab;
            if (wristPos.y < minY || wristPos.y > maxY)
            {
                return Vector3.Distance(wristPos, closest) + 0.10f;
            }
            Vector2 horizWrist = new Vector2(wristPos.x, wristPos.z);
            Vector2 horizSpine = new Vector2(closest.x, closest.z);
            return Vector2.Distance(horizWrist, horizSpine);
        }

        private static List<AnimationClip> CollectClipsForController(RuntimeAnimatorController rac)
        {
            var result = new List<AnimationClip>();
            if (rac == null) return result;
            var seen = new HashSet<string>();
            foreach (var c in rac.animationClips)
            {
                if (c != null && seen.Add(c.name))
                {
                    result.Add(c);
                }
            }
            return result;
        }

        private static bool IsArmCheckClip(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return false;
            string n = clipName.ToLowerInvariant();
            // Exclude pure locomotion/jump air if desired, or check all non-locomotion + idle/wave/talk/reactions
            if (n.Contains("walk") || n.Contains("run") || n.Contains("jump"))
                return false;
            return true;
        }

        public static void RenderHandymanWaveSheet(string prefabPath, string outPngPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Vector3 stageOrigin = new Vector3(600f, 0f, 600f);
            instance.transform.position = stageOrigin;
            instance.transform.rotation = Quaternion.identity;

            foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                smr.forceMatrixRecalculationPerRender = true;
            }

            AnimationClip waveClip = null;
            AnimationClip greetClip = null;
            var anim = instance.GetComponentInChildren<Animator>();
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                foreach (var c in anim.runtimeAnimatorController.animationClips)
                {
                    if (c == null) continue;
                    if (c.name == "Handyman_Idle") waveClip = c;
                    if (c.name == "Handyman_Greet") greetClip = c;
                }
            }
            // Prefer Handyman_Greet so all 5 sampled percentages (0%, 25%, 50%, 75%, 100%) fall inside the active wave arc
            AnimationClip targetClip = greetClip ?? waveClip;

            int cellW = 280;
            int cellH = 340;
            int cols = SampleFractions.Length;
            int rows = 2; // Row 0 = Front view, Row 1 = 3/4 view
            Texture2D sheet = new Texture2D(cellW * cols, cellH * rows, TextureFormat.RGBA32, false);

            GameObject camGo = new GameObject("TempReportCam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.18f, 0.21f, 0.25f, 1f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;

            RenderTexture rt = RenderTexture.GetTemporary(cellW, cellH, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            try
            {
                for (int cIdx = 0; cIdx < cols; cIdx++)
                {
                    float frac = SampleFractions[cIdx];
                    if (targetClip != null)
                    {
                        GameObject sampleTarget = anim != null ? anim.gameObject : instance;
                        targetClip.SampleAnimation(sampleTarget, frac * targetClip.length);
                        instance.transform.position = stageOrigin;
                        instance.transform.rotation = Quaternion.identity;
                    }

                    // Front View (top row in image -> y = cellH)
                    camGo.transform.position = stageOrigin + new Vector3(0f, 1.05f, 3.1f);
                    camGo.transform.LookAt(stageOrigin + new Vector3(0f, 0.92f, 0f));
                    cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, cellW, cellH), cIdx * cellW, cellH);

                    // 3/4 View (bottom row in image -> y = 0)
                    camGo.transform.position = stageOrigin + new Vector3(-2.1f, 1.10f, 2.3f);
                    camGo.transform.LookAt(stageOrigin + new Vector3(0f, 0.92f, 0f));
                    cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, cellW, cellH), cIdx * cellW, 0);
                }

                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(outPngPath));
                File.WriteAllBytes(outPngPath, sheet.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }

        public static void RenderNpcContactSheet(string outPngPath)
        {
            var families = new List<(string label, string prefabPath, string[] preferredClips)>
            {
                ("Handyman", "Assets/EcoQuest/Characters/Handyman/Variants/NPC_Handyman_Green.prefab", new[] { "Handyman_Idle", "Handyman_Greet", "Handyman_TalkA" }),
                ("Explorer", "Assets/EcoQuest/Characters/Explorer/Variants/Explorer_Var1.prefab", new[] { "Explorer_Wave", "Explorer_Talk_A", "Explorer_Cheer" }),
                ("Guardian/Everyday", "Assets/EcoQuest/Prefabs/NPC/NPC_Everyday_Male.prefab", new[] { "greet", "talk", "happy" }),
                ("CivilianMale", "Assets/EcoQuest/Prefabs/NPC/NPC_Male01.prefab", new[] { "greet", "talk", "happy" }),
                ("CivilianFemale", "Assets/EcoQuest/Prefabs/NPC/NPC_Female01.prefab", new[] { "greet", "talk", "happy" }),
                ("PolyPerfect", "Assets/EcoQuest/Prefabs/NPC/NPC_Poly_ManCoat.prefab", new[] { "greet", "talk", "happy" })
            };

            int cellW = 180;
            int cellH = 220;
            int cols = SampleFractions.Length; // 5 frames per clip
            int totalRows = families.Count * 3; // 3 clips per NPC family

            Texture2D sheet = new Texture2D(cellW * cols, cellH * totalRows, TextureFormat.RGBA32, false);
            GameObject camGo = new GameObject("TempContactCam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.19f, 0.23f, 1f);
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;

            RenderTexture rt = RenderTexture.GetTemporary(cellW, cellH, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            Vector3 stageOrigin = new Vector3(700f, 0f, 700f);

            try
            {
                int currentRow = 0;
                foreach (var fam in families)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fam.prefabPath);
                    if (prefab == null) continue;

                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    instance.transform.position = stageOrigin;
                    instance.transform.rotation = Quaternion.identity;

                    foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        smr.updateWhenOffscreen = true;
                        smr.forceMatrixRecalculationPerRender = true;
                    }

                    var brain = instance.GetComponent<NpcBrain>();
                    if (brain != null)
                    {
                        brain.SetGender(NpcSpawner.ResolveGender(prefab.name));
                        brain.ApplyGenderController();
                    }

                    // Ensure preview instance has URP Lit materials if its prefab uses runtime outfit assignment
                    Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
                    if (urpLit != null)
                    {
                        foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                        {
                            var mats = r.sharedMaterials;
                            bool changed = false;
                            for (int m = 0; m < mats.Length; m++)
                            {
                                bool hasTex = mats[m] != null && (mats[m].mainTexture != null || (mats[m].HasProperty("_BaseMap") && mats[m].GetTexture("_BaseMap") != null));
                                if (mats[m] == null || mats[m].shader == null || mats[m].shader.name == "Standard" || !hasTex || fam.label == "Guardian/Everyday")
                                {
                                    Material tempMat = new Material(urpLit);
                                    tempMat.SetColor("_BaseColor", new Color(0.38f, 0.54f, 0.44f, 1f));
                                    if (mats[m] != null && mats[m].mainTexture != null && fam.label != "Guardian/Everyday")
                                        tempMat.SetTexture("_BaseMap", mats[m].mainTexture);
                                    mats[m] = tempMat;
                                    changed = true;
                                }
                            }
                            if (changed) r.sharedMaterials = mats;
                        }
                    }

                    Animator anim = instance.GetComponentInChildren<Animator>();
                    var clips = anim != null ? CollectClipsForController(anim.runtimeAnimatorController) : new List<AnimationClip>();

                    for (int clipSlot = 0; clipSlot < 3; clipSlot++)
                    {
                        string pattern = fam.preferredClips[Mathf.Min(clipSlot, fam.preferredClips.Length - 1)];
                        AnimationClip chosen = FindClipBySubstring(clips, pattern) ?? (clips.Count > 0 ? clips[clipSlot % clips.Count] : null);

                        int yOffset = (totalRows - 1 - currentRow) * cellH;
                        for (int cIdx = 0; cIdx < cols; cIdx++)
                        {
                            float frac = SampleFractions[cIdx];
                            if (chosen != null)
                            {
                                GameObject sampleTarget = anim != null ? anim.gameObject : instance;
                                chosen.SampleAnimation(sampleTarget, frac * chosen.length);
                                instance.transform.position = stageOrigin;
                                instance.transform.rotation = Quaternion.identity;
                                if (anim != null && anim.gameObject != instance)
                                {
                                    anim.transform.localPosition = Vector3.zero;
                                    anim.transform.localRotation = Quaternion.identity;
                                }
                            }

                            camGo.transform.position = stageOrigin + new Vector3(-1.3f, 1.10f, 2.8f);
                            camGo.transform.LookAt(stageOrigin + new Vector3(0f, 0.95f, 0f));
                            cam.Render();
                            RenderTexture.active = rt;
                            sheet.ReadPixels(new Rect(0, 0, cellW, cellH), cIdx * cellW, yOffset);
                        }
                        currentRow++;
                    }

                    UnityEngine.Object.DestroyImmediate(instance);
                }

                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(outPngPath));
                File.WriteAllBytes(outPngPath, sheet.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }

        private static AnimationClip FindClipBySubstring(List<AnimationClip> clips, string sub)
        {
            foreach (var c in clips)
            {
                if (c != null && c.name.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0)
                    return c;
            }
            return null;
        }
    }
}
#endif
