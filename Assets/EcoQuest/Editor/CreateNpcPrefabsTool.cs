#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class CreateNpcPrefabsTool
{
    private const string ControllerPath = "Assets/EcoQuest/Asset/GuardianAnimatorController.controller";
    private const string OutDir = "Assets/EcoQuest/Prefabs/NPC";

    private struct SourceDef
    {
        public string prefabName;
        public string assetPath;
        public string avatarFbxPath;
    }

    [MenuItem("NPC/Create All 20 Unique NPC Prefabs")]
    public static void CreateAllPrefabs()
    {
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"Controller not found at {ControllerPath}");
            return;
        }

        if (!AssetDatabase.IsValidFolder(OutDir))
        {
            AssetDatabase.CreateFolder("Assets/EcoQuest/Prefabs", "NPC");
        }

        var sources = new List<SourceDef>
        {
            new SourceDef { prefabName = "NPC_Male01", assetPath = "Assets/LowPolyCivilians/Prefabs/MaleCivilian01.prefab", avatarFbxPath = "Assets/LowPolyCivilians/Models/Male_Civilian.FBX" },
            new SourceDef { prefabName = "NPC_Male02", assetPath = "Assets/LowPolyCivilians/Prefabs/MaleCivilian02.prefab", avatarFbxPath = "Assets/LowPolyCivilians/Models/Male_Civilian.FBX" },
            new SourceDef { prefabName = "NPC_Male03", assetPath = "Assets/LowPolyCivilians/Prefabs/MaleCivilian03.prefab", avatarFbxPath = "Assets/LowPolyCivilians/Models/Male_Civilian.FBX" },
            new SourceDef { prefabName = "NPC_Male04", assetPath = "Assets/LowPolyCivilians/Prefabs/MaleCivilian04.prefab", avatarFbxPath = "Assets/LowPolyCivilians/Models/Male_Civilian.FBX" },
            new SourceDef { prefabName = "NPC_Male05", assetPath = "Assets/LowPolyCivilians/Prefabs/MaleCivilian05.prefab", avatarFbxPath = "Assets/LowPolyCivilians/Models/Male_Civilian.FBX" },
            new SourceDef { prefabName = "NPC_Female01", assetPath = "Assets/LowPolyCivilians/Prefabs/FemaleCivilian01.prefab", avatarFbxPath = "Assets/LowPolyCivilians/Models/Female_Civilian.FBX" },
            new SourceDef { prefabName = "NPC_Female02", assetPath = "Assets/LowPolyCivilians/Prefabs/FemaleCivilian02.prefab", avatarFbxPath = "Assets/LowPolyCivilians/Models/Female_Civilian.FBX" },
            new SourceDef { prefabName = "NPC_Female03", assetPath = "Assets/LowPolyCivilians/Prefabs/FemaleCivilian03.prefab", avatarFbxPath = "Assets/LowPolyCivilians/Models/Female_Civilian.FBX" },
            new SourceDef { prefabName = "NPC_Everyday_Male", assetPath = "Assets/EverydayMotionPack/Model/Chatacter/male.prefab", avatarFbxPath = "Assets/EverydayMotionPack/Model/Chatacter/male.FBX" },
            new SourceDef { prefabName = "NPC_Everyday_Female", assetPath = "Assets/EverydayMotionPack/Model/Chatacter/female.prefab", avatarFbxPath = "Assets/EverydayMotionPack/Model/Chatacter/female.FBX" },
            new SourceDef { prefabName = "NPC_Poly_GirlWinter", assetPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/girl-winter_Rig.fbx", avatarFbxPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/girl-winter_Rig.fbx" },
            new SourceDef { prefabName = "NPC_Poly_ManCoat", assetPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-coat_Rig.fbx", avatarFbxPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-coat_Rig.fbx" },
            new SourceDef { prefabName = "NPC_Poly_ManDoctor", assetPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-doctor_Rig.fbx", avatarFbxPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-doctor_Rig.fbx" },
            new SourceDef { prefabName = "NPC_Poly_ManFire", assetPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-fire_Rig.fbx", avatarFbxPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-fire_Rig.fbx" },
            new SourceDef { prefabName = "NPC_Poly_ManLord", assetPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-lord-rival_Rig.fbx", avatarFbxPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-lord-rival_Rig.fbx" },
            new SourceDef { prefabName = "NPC_Poly_ManPolice", assetPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-police_Rig.fbx", avatarFbxPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-police_Rig.fbx" },
            new SourceDef { prefabName = "NPC_Poly_ManSir", assetPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-sir_Rig.fbx", avatarFbxPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-sir_Rig.fbx" },
            new SourceDef { prefabName = "NPC_Poly_ManSwat", assetPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-swat_Rig.fbx", avatarFbxPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/man-swat_Rig.fbx" },
            new SourceDef { prefabName = "NPC_Poly_WomanPirate", assetPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/woman-pirate-scarf_Rig.fbx", avatarFbxPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/woman-pirate-scarf_Rig.fbx" },
            new SourceDef { prefabName = "NPC_Poly_WomanWinter", assetPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/woman-winter-coat_Rig.fbx", avatarFbxPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Meshes_M/People_M/Rigs_M/woman-winter-coat_Rig.fbx" }
        };

        var walkGraph = AssetDatabase.LoadAssetAtPath<CityWalkGraph>("Assets/EcoQuest/City Scene/CityWalkGraph.asset");
        int count = 0;

        foreach (var def in sources)
        {
            var srcObj = AssetDatabase.LoadAssetAtPath<GameObject>(def.assetPath);
            if (srcObj == null)
            {
                Debug.LogWarning($"Could not load source {def.assetPath}");
                continue;
            }

            // Find Avatar
            Avatar avatar = null;
            if (!string.IsNullOrEmpty(def.avatarFbxPath))
            {
                var importer = AssetImporter.GetAtPath(def.avatarFbxPath) as ModelImporter;
                if (importer != null) avatar = importer.sourceAvatar;
            }
            if (avatar == null)
            {
                var existingAnim = srcObj.GetComponentInChildren<Animator>();
                if (existingAnim != null && existingAnim.avatar != null) avatar = existingAnim.avatar;
            }

            // Create temporary instance to configure
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(srcObj);
            instance.name = def.prefabName;

            // Remove legacy Animation component
            var legacyAnim = instance.GetComponent<Animation>();
            if (legacyAnim != null) Object.DestroyImmediate(legacyAnim);
            foreach (var a in instance.GetComponentsInChildren<Animation>(true))
                Object.DestroyImmediate(a);

            // Configure Animator
            var anim = instance.GetComponent<Animator>();
            if (anim == null) anim = instance.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            if (avatar != null) anim.avatar = avatar;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Ensure URP material on polyperfect models
            if (def.prefabName.StartsWith("NPC_Poly_"))
            {
                var atlasMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/polyperfect/Low Poly Ultimate Pack/- Materials/atlas-LPUP.mat");
                if (atlasMat != null)
                {
                    var smr = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (smr != null)
                    {
                        var newMats = new Material[smr.sharedMaterials.Length];
                        for (int m = 0; m < newMats.Length; m++) newMats[m] = atlasMat;
                        smr.sharedMaterials = newMats;
                    }
                }
            }

            // CapsuleCollider
            var col = instance.GetComponent<CapsuleCollider>();
            if (col == null) col = instance.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.9f, 0f);
            col.radius = 0.35f;
            col.height = 1.8f;

            // NavMeshAgent
            var agent = instance.GetComponent<NavMeshAgent>();
            if (agent == null) agent = instance.AddComponent<NavMeshAgent>();
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.radius = 0.35f;
            agent.height = 1.8f;

            // NpcBrain
            var brain = instance.GetComponent<NpcBrain>();
            if (brain == null) brain = instance.AddComponent<NpcBrain>();
            brain.SetWalkGraph(walkGraph);

            string targetPrefabPath = $"{OutDir}/{def.prefabName}.prefab";
            PrefabUtility.SaveAsPrefabAsset(instance, targetPrefabPath);
            Object.DestroyImmediate(instance);
            count++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"<color=green>SUCCESS: Created/Configured {count} unique NPC prefabs in {OutDir}!</color>");
    }
}
#endif
