using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.AI;
using EcoQuest.Characters.Handyman;

namespace EcoQuest.Editor
{
    /// <summary>
    /// Automated setup tool for the stylized low-poly Handyman NPC character.
    /// Menu: Tools > EcoQuest > Setup Handyman NPC
    /// Configures Humanoid Avatar, clip import settings & footstep events, materials & palettes,
    /// Animator Controller (Locomotion blend tree, conversational talk/listen, reactions, idle variations),
    /// and generates base & 4 variant prefabs.
    /// </summary>
    public static class SetupHandymanNpc
    {
        private const string BaseFolder = "Assets/EcoQuest/Characters/Handyman";
        private const string VariantsFolder = "Assets/EcoQuest/Characters/Handyman/Variants";
        private const string FbxPath = "Assets/EcoQuest/Characters/Handyman/Handyman.fbx";
        
        private const string MainPalettePath = "Assets/EcoQuest/Characters/Handyman/Handyman_Palette.png";
        private const string GreenPalettePath = "Assets/EcoQuest/Characters/Handyman/Handyman_Palette_Green.png";
        private const string RedPalettePath = "Assets/EcoQuest/Characters/Handyman/Handyman_Palette_Red.png";
        private const string BluePalettePath = "Assets/EcoQuest/Characters/Handyman/Handyman_Palette_Blue.png";

        private const string MainMatPath = "Assets/EcoQuest/Characters/Handyman/Mat_Handyman.mat";
        private const string GreenMatPath = "Assets/EcoQuest/Characters/Handyman/Mat_Handyman_Green.mat";
        private const string RedMatPath = "Assets/EcoQuest/Characters/Handyman/Mat_Handyman_Red.mat";
        private const string BlueMatPath = "Assets/EcoQuest/Characters/Handyman/Mat_Handyman_Blue.mat";

        private const string ControllerPath = "Assets/EcoQuest/Characters/Handyman/AC_Handyman.controller";
        private const string MainPrefabPath = "Assets/EcoQuest/Characters/Handyman/NPC_Handyman.prefab";

        [MenuItem("Tools/EcoQuest/Setup Handyman NPC", false, 101)]
        public static void RunSetup()
        {
            Debug.Log("[SetupHandymanNpc] === Starting Handyman 3D Character Setup ===");
            try
            {
                EnsureFolders();
                ConfigureTextures();
                ConfigureModelImporter();
                Dictionary<string, Material> materials = SetupMaterials();
                AnimatorController controller = SetupAnimatorController();
                SetupPrefabs(materials, controller);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[SetupHandymanNpc] === Setup COMPLETE! NPC_Handyman and variants ready in Assets/EcoQuest/Characters/Handyman/ ===");
            }
            catch (Exception ex)
            {
                Debug.LogError("[SetupHandymanNpc] Setup failed with error: " + ex);
            }
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(VariantsFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Variants");
            }
        }

        private static void ConfigureTextures()
        {
            string[] texPaths = new string[]
            {
                MainPalettePath,
                GreenPalettePath,
                RedPalettePath,
                BluePalettePath
            };

            foreach (var path in texPaths)
            {
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null)
                {
                    bool changed = false;
                    if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
                    if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; changed = true; }
                    if (ti.sRGBTexture != true) { ti.sRGBTexture = true; changed = true; }
                    if (ti.textureCompression != TextureImporterCompression.Uncompressed)
                    {
                        ti.textureCompression = TextureImporterCompression.Uncompressed;
                        changed = true;
                    }
                    if (changed)
                    {
                        ti.SaveAndReimport();
                    }
                }
            }
        }

        private static void ConfigureModelImporter()
        {
            ModelImporter importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null)
            {
                throw new FileNotFoundException("Handyman FBX not found at " + FbxPath);
            }

            bool needsReimport = false;

            if (importer.animationType != ModelImporterAnimationType.Human ||
                importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }

            // Reset HumanDescription so Unity regenerates skeleton & T-pose cleanly from the current FBX bone rolls
            HumanDescription hd = new HumanDescription();
            hd.armStretch = 0.01f;
            hd.legStretch = 0.02f;
            hd.upperArmTwist = 0.40f;
            hd.lowerArmTwist = 0.40f;
            importer.humanDescription = hd;
            importer.SaveAndReimport();

            // Ensure Chest is mapped in the regenerated HumanDescription
            hd = importer.humanDescription;
            hd.armStretch = 0.01f;
            hd.legStretch = 0.02f;
            hd.upperArmTwist = 0.40f;
            hd.lowerArmTwist = 0.40f;
            List<HumanBone> hBones = new List<HumanBone>(hd.human ?? Array.Empty<HumanBone>());
            bool hasChest = false;
            for (int i = 0; i < hBones.Count; i++)
            {
                if (hBones[i].humanName == "Chest")
                {
                    hasChest = true;
                    break;
                }
            }
            if (!hasChest)
            {
                HumanBone chestBone = new HumanBone
                {
                    boneName = "Chest",
                    humanName = "Chest"
                };
                chestBone.limit.useDefaultValues = true;
                hBones.Add(chestBone);
            }
            hd.human = hBones.ToArray();
            importer.humanDescription = hd;
            importer.SaveAndReimport();

            ModelImporterClipAnimation[] sourceClips = importer.defaultClipAnimations;
            if (sourceClips == null || sourceClips.Length == 0)
            {
                sourceClips = importer.clipAnimations;
            }

            if (sourceClips != null && sourceClips.Length > 0)
            {
                List<string> loopingClips = new List<string>()
                {
                    "Handyman_Idle",
                    "Handyman_Walk",
                    "Handyman_Run",
                    "Handyman_TalkA",
                    "Handyman_TalkB",
                    "Handyman_Listen"
                };

                List<ModelImporterClipAnimation> newClips = new List<ModelImporterClipAnimation>();
                foreach (var def in sourceClips)
                {
                    ModelImporterClipAnimation clip = new ModelImporterClipAnimation();
                    clip.name = def.name;
                    int pipeIdx = clip.name.IndexOf('|');
                    if (pipeIdx >= 0)
                    {
                        clip.name = clip.name.Substring(pipeIdx + 1);
                    }
                    clip.takeName = def.takeName;
                    clip.firstFrame = def.firstFrame;
                    clip.lastFrame = def.lastFrame;
                    clip.wrapMode = WrapMode.Default;

                    bool isLoop = loopingClips.Contains(clip.name);
                    clip.loopTime = isLoop;
                    if (isLoop) clip.loopPose = true;

                    // Grounding & in-place root transform
                    clip.lockRootHeightY = true;
                    clip.keepOriginalPositionY = true;
                    clip.heightOffset = 0f;
                    clip.lockRootPositionXZ = true;
                    clip.keepOriginalPositionXZ = true;
                    clip.lockRootRotation = true;
                    clip.keepOriginalOrientation = true;

                    // Footstep events
                    List<AnimationEvent> events = new List<AnimationEvent>();
                    if (clip.name == "Handyman_Walk")
                    {
                        events.Add(new AnimationEvent { time = 8f / 30f, functionName = "OnFootstep", stringParameter = "FootR" });
                        events.Add(new AnimationEvent { time = 23f / 30f, functionName = "OnFootstep", stringParameter = "FootL" });
                    }
                    else if (clip.name == "Handyman_Run")
                    {
                        events.Add(new AnimationEvent { time = 5f / 20f, functionName = "OnFootstep", stringParameter = "FootR" });
                        events.Add(new AnimationEvent { time = 15f / 20f, functionName = "OnFootstep", stringParameter = "FootL" });
                    }
                    clip.events = events.ToArray();

                    newClips.Add(clip);
                }

                importer.clipAnimations = newClips.ToArray();
                needsReimport = true;
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, Material> SetupMaterials()
        {
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Dictionary<string, Material> materials = new Dictionary<string, Material>();

            string[] paths = new string[] { MainMatPath, GreenMatPath, RedMatPath, BlueMatPath };
            string[] texPaths = new string[] { MainPalettePath, GreenPalettePath, RedPalettePath, BluePalettePath };
            string[] keys = new string[] { "Main", "Green", "Red", "Blue" };

            for (int i = 0; i < keys.Length; i++)
            {
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPaths[i]);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(paths[i]);
                if (mat == null)
                {
                    mat = new Material(urpShader);
                    AssetDatabase.CreateAsset(mat, paths[i]);
                }
                ApplyMaterialProperties(mat, tex);
                materials[keys[i]] = mat;
            }

            return materials;
        }

        private static void ApplyMaterialProperties(Material mat, Texture2D tex)
        {
            if (mat == null) return;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
        }

        private static AnimatorController SetupAnimatorController()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            // Load clips from FBX
            Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
            foreach (var a in assets)
            {
                if (a is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    string cleanName = clip.name;
                    int pipeIdx = cleanName.IndexOf('|');
                    if (pipeIdx >= 0) cleanName = cleanName.Substring(pipeIdx + 1);
                    clips[cleanName] = clip;
                }
            }

            // Ensure Parameters
            EnsureParameter(controller, "Speed", AnimatorControllerParameterType.Float, 0f);
            EnsureParameter(controller, "Talk", AnimatorControllerParameterType.Bool, false);
            EnsureParameter(controller, "TalkIndex", AnimatorControllerParameterType.Int, 0);
            EnsureParameter(controller, "Listen", AnimatorControllerParameterType.Bool, false);
            EnsureParameter(controller, "IdleVariation", AnimatorControllerParameterType.Int, 0);
            EnsureParameter(controller, "PlayIdleVariation", AnimatorControllerParameterType.Trigger);

            // Reaction triggers
            string[] triggers = new string[]
            {
                "Greet", "Nod", "ShakeHead", "Shrug", "Point", "Thanks", "Laugh", "Surprised"
            };
            foreach (var trig in triggers)
            {
                EnsureParameter(controller, trig, AnimatorControllerParameterType.Trigger);
            }

            // Configure Base Layer State Machine
            var layers = controller.layers;
            if (layers.Length == 0) controller.AddLayer("Base Layer");
            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            // Clear existing transitions and states to ensure fresh build
            sm.anyStateTransitions = new AnimatorStateTransition[0];
            sm.entryTransitions = new AnimatorTransition[0];
            sm.states = new ChildAnimatorState[0];

            // 1. Locomotion BlendTree
            AnimatorState locomotionState = sm.AddState("Locomotion");
            sm.defaultState = locomotionState;

            BlendTree blendTree;
            controller.CreateBlendTreeInController("LocomotionBlend", out blendTree, 0);
            locomotionState.motion = blendTree;
            blendTree.blendType = BlendTreeType.Simple1D;
            blendTree.blendParameter = "Speed";
            blendTree.useAutomaticThresholds = false;

            if (clips.TryGetValue("Handyman_Idle", out var idleClip))
                blendTree.AddChild(idleClip, 0.0f);
            if (clips.TryGetValue("Handyman_Walk", out var walkClip))
                blendTree.AddChild(walkClip, 1.4f);
            if (clips.TryGetValue("Handyman_Run", out var runClip))
                blendTree.AddChild(runClip, 3.6f);

            // 2. Idle Variations
            CreateIdleVarState(sm, locomotionState, "Idle_ScratchBeard", "Handyman_IdleScratchBeard", clips, 0);
            CreateIdleVarState(sm, locomotionState, "Idle_CheckPhone", "Handyman_IdleCheckPhone", clips, 1);
            CreateIdleVarState(sm, locomotionState, "Idle_AdjustCap", "Handyman_IdleAdjustCap", clips, 2);

            // 3. Conversational States
            AnimatorState listenState = sm.AddState("Listen");
            if (clips.TryGetValue("Handyman_Listen", out var listenClip)) listenState.motion = listenClip;

            AnimatorState talkAState = sm.AddState("TalkA");
            if (clips.TryGetValue("Handyman_TalkA", out var talkAClip)) talkAState.motion = talkAClip;

            AnimatorState talkBState = sm.AddState("TalkB");
            if (clips.TryGetValue("Handyman_TalkB", out var talkBClip)) talkBState.motion = talkBClip;

            // Transitions: Locomotion <-> Listen
            var locoToListen = locomotionState.AddTransition(listenState);
            locoToListen.AddCondition(AnimatorConditionMode.If, 0, "Listen");
            locoToListen.hasExitTime = false;
            locoToListen.duration = 0.2f;

            var listenToLoco = listenState.AddTransition(locomotionState);
            listenToLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "Listen");
            listenToLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "Talk");
            listenToLoco.hasExitTime = false;
            listenToLoco.duration = 0.2f;

            // Locomotion -> TalkA / TalkB
            var locoToTalkA = locomotionState.AddTransition(talkAState);
            locoToTalkA.AddCondition(AnimatorConditionMode.If, 0, "Talk");
            locoToTalkA.AddCondition(AnimatorConditionMode.Equals, 0, "TalkIndex");
            locoToTalkA.hasExitTime = false;
            locoToTalkA.duration = 0.2f;

            var locoToTalkB = locomotionState.AddTransition(talkBState);
            locoToTalkB.AddCondition(AnimatorConditionMode.If, 0, "Talk");
            locoToTalkB.AddCondition(AnimatorConditionMode.Equals, 1, "TalkIndex");
            locoToTalkB.hasExitTime = false;
            locoToTalkB.duration = 0.2f;

            // Listen <-> TalkA / TalkB
            var listenToTalkA = listenState.AddTransition(talkAState);
            listenToTalkA.AddCondition(AnimatorConditionMode.If, 0, "Talk");
            listenToTalkA.AddCondition(AnimatorConditionMode.Equals, 0, "TalkIndex");
            listenToTalkA.hasExitTime = false;
            listenToTalkA.duration = 0.15f;

            var listenToTalkB = listenState.AddTransition(talkBState);
            listenToTalkB.AddCondition(AnimatorConditionMode.If, 0, "Talk");
            listenToTalkB.AddCondition(AnimatorConditionMode.Equals, 1, "TalkIndex");
            listenToTalkB.hasExitTime = false;
            listenToTalkB.duration = 0.15f;

            var talkAToListen = talkAState.AddTransition(listenState);
            talkAToListen.AddCondition(AnimatorConditionMode.IfNot, 0, "Talk");
            talkAToListen.AddCondition(AnimatorConditionMode.If, 0, "Listen");
            talkAToListen.hasExitTime = false;
            talkAToListen.duration = 0.15f;

            var talkBToListen = talkBState.AddTransition(listenState);
            talkBToListen.AddCondition(AnimatorConditionMode.IfNot, 0, "Talk");
            talkBToListen.AddCondition(AnimatorConditionMode.If, 0, "Listen");
            talkBToListen.hasExitTime = false;
            talkBToListen.duration = 0.15f;

            // TalkA <-> TalkB alternating
            var aToB = talkAState.AddTransition(talkBState);
            aToB.AddCondition(AnimatorConditionMode.Equals, 1, "TalkIndex");
            aToB.hasExitTime = false;
            aToB.duration = 0.15f;

            var bToA = talkBState.AddTransition(talkAState);
            bToA.AddCondition(AnimatorConditionMode.Equals, 0, "TalkIndex");
            bToA.hasExitTime = false;
            bToA.duration = 0.15f;

            // Talk -> Locomotion
            var talkAToLoco = talkAState.AddTransition(locomotionState);
            talkAToLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "Talk");
            talkAToLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "Listen");
            talkAToLoco.hasExitTime = false;
            talkAToLoco.duration = 0.2f;

            var talkBToLoco = talkBState.AddTransition(locomotionState);
            talkBToLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "Talk");
            talkBToLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "Listen");
            talkBToLoco.hasExitTime = false;
            talkBToLoco.duration = 0.2f;

            // 4. Reactions
            CreateReactionState(sm, locomotionState, listenState, "Greet", "Handyman_Greet", clips);
            CreateReactionState(sm, locomotionState, listenState, "Nod", "Handyman_Nod", clips);
            CreateReactionState(sm, locomotionState, listenState, "ShakeHead", "Handyman_ShakeHead", clips);
            CreateReactionState(sm, locomotionState, listenState, "Shrug", "Handyman_Shrug", clips);
            CreateReactionState(sm, locomotionState, listenState, "Point", "Handyman_Point", clips);
            CreateReactionState(sm, locomotionState, listenState, "Thanks", "Handyman_Thanks", clips);
            CreateReactionState(sm, locomotionState, listenState, "Laugh", "Handyman_Laugh", clips);
            CreateReactionState(sm, locomotionState, listenState, "Surprised", "Handyman_Surprised", clips);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void CreateIdleVarState(
            AnimatorStateMachine sm,
            AnimatorState returnState,
            string stateName,
            string clipName,
            Dictionary<string, AnimationClip> clips,
            int varIndex)
        {
            AnimatorState state = sm.AddState(stateName);
            if (clips.TryGetValue(clipName, out var clip)) state.motion = clip;

            var toVar = returnState.AddTransition(state);
            toVar.AddCondition(AnimatorConditionMode.If, 0, "PlayIdleVariation");
            toVar.AddCondition(AnimatorConditionMode.Equals, varIndex, "IdleVariation");
            toVar.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            toVar.hasExitTime = false;
            toVar.duration = 0.2f;

            var back = state.AddTransition(returnState);
            back.hasExitTime = true;
            back.exitTime = 0.95f;
            back.duration = 0.2f;
        }

        private static void CreateReactionState(
            AnimatorStateMachine sm,
            AnimatorState locoState,
            AnimatorState listenState,
            string triggerName,
            string clipName,
            Dictionary<string, AnimationClip> clips)
        {
            AnimatorState state = sm.AddState(triggerName);
            if (clips.TryGetValue(clipName, out var clip)) state.motion = clip;

            var toState = sm.AddAnyStateTransition(state);
            toState.AddCondition(AnimatorConditionMode.If, 0, triggerName);
            toState.hasExitTime = false;
            toState.duration = 0.12f;

            // Return to Listen if in conversation
            var backToListen = state.AddTransition(listenState);
            backToListen.AddCondition(AnimatorConditionMode.If, 0, "Listen");
            backToListen.hasExitTime = true;
            backToListen.exitTime = 0.90f;
            backToListen.duration = 0.15f;

            // Return to Locomotion if not listening
            var backToLoco = state.AddTransition(locoState);
            backToLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "Listen");
            backToLoco.hasExitTime = true;
            backToLoco.exitTime = 0.90f;
            backToLoco.duration = 0.15f;
        }

        private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type, object defaultValue = null)
        {
            foreach (var p in controller.parameters)
            {
                if (p.name == name) return;
            }
            controller.AddParameter(name, type);
            if (defaultValue != null)
            {
                for (int i = 0; i < controller.parameters.Length; i++)
                {
                    if (controller.parameters[i].name == name)
                    {
                        var p = controller.parameters[i];
                        if (defaultValue is bool b) p.defaultBool = b;
                        else if (defaultValue is float f) p.defaultFloat = f;
                        else if (defaultValue is int integer) p.defaultInt = integer;
                        break;
                    }
                }
            }
        }

        private static void SetupPrefabs(Dictionary<string, Material> materials, AnimatorController controller)
        {
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbxAsset == null)
            {
                throw new FileNotFoundException("FBX asset not found at " + FbxPath);
            }

            // 1. Base Prefab: NPC_Handyman
            GameObject mainInstance = PrefabUtility.InstantiatePrefab(fbxAsset) as GameObject;
            PrefabUtility.UnpackPrefabInstance(mainInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            mainInstance.name = "NPC_Handyman";
            ConfigureHandymanComponents(mainInstance, materials["Main"], controller, true);
            PrefabUtility.SaveAsPrefabAsset(mainInstance, MainPrefabPath);
            GameObject.DestroyImmediate(mainInstance);
            Debug.Log("[SetupHandymanNpc] Created base prefab: " + MainPrefabPath);

            // 2. Variants: Green, Red, Blue, NoSatchel
            CreateVariantPrefab(fbxAsset, "NPC_Handyman_Green", materials["Green"], controller, true);
            CreateVariantPrefab(fbxAsset, "NPC_Handyman_Red", materials["Red"], controller, true);
            CreateVariantPrefab(fbxAsset, "NPC_Handyman_Blue", materials["Blue"], controller, true);
            CreateVariantPrefab(fbxAsset, "NPC_Handyman_NoSatchel", materials["Main"], controller, false);
        }

        private static void CreateVariantPrefab(
            GameObject fbxAsset,
            string variantName,
            Material mat,
            AnimatorController controller,
            bool satchelVisible)
        {
            string prefabPath = $"{VariantsFolder}/{variantName}.prefab";
            GameObject varInstance = PrefabUtility.InstantiatePrefab(fbxAsset) as GameObject;
            PrefabUtility.UnpackPrefabInstance(varInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            varInstance.name = variantName;
            ConfigureHandymanComponents(varInstance, mat, controller, satchelVisible);
            PrefabUtility.SaveAsPrefabAsset(varInstance, prefabPath);
            GameObject.DestroyImmediate(varInstance);
            Debug.Log("[SetupHandymanNpc] Created variant prefab: " + prefabPath);
        }

        private static void ConfigureHandymanComponents(
            GameObject go,
            Material mat,
            AnimatorController controller,
            bool satchelVisible)
        {
            // 1. Animator
            Animator anim = go.GetComponent<Animator>();
            if (anim == null) anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;

            // Load Humanoid Avatar
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
            foreach (var a in assets)
            {
                if (a is Avatar av && av.isValid)
                {
                    anim.avatar = av;
                    break;
                }
            }

            // 2. NavMeshAgent
            NavMeshAgent agent = go.GetComponent<NavMeshAgent>();
            if (agent == null) agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.70f;
            agent.speed = 1.4f;
            agent.acceleration = 8.0f;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = 1.2f;

            // 3. CapsuleCollider
            CapsuleCollider col = go.GetComponent<CapsuleCollider>();
            if (col == null) col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.85f, 0f);
            col.radius = 0.35f;
            col.height = 1.70f;

            // 4. Interaction Trigger Sphere
            SphereCollider triggerCol = go.AddComponent<SphereCollider>();
            triggerCol.isTrigger = true;
            triggerCol.center = new Vector3(0f, 0.85f, 0f);
            triggerCol.radius = 2.5f;

            // 5. AudioSource
            AudioSource audio = go.GetComponent<AudioSource>();
            if (audio == null) audio = go.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1.0f;
            audio.minDistance = 2.0f;
            audio.maxDistance = 18.0f;

            // 6. Materials & Satchel
            GameObject satchelObj = null;
            SkinnedMeshRenderer bodyRend = null;

            var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (mat != null)
                {
                    r.sharedMaterial = mat;
                }

                if (r.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bodyRend = r;
                }
                else if (r.name.IndexOf("Satchel", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    satchelObj = r.gameObject;
                    satchelObj.SetActive(satchelVisible);
                }
            }

            // 7. NpcConversationAnimator
            NpcConversationAnimator conv = go.GetComponent<NpcConversationAnimator>();
            if (conv == null) conv = go.AddComponent<NpcConversationAnimator>();

            SerializedObject so = new SerializedObject(conv);
            so.FindProperty("showSatchel").boolValue = satchelVisible;
            if (satchelObj != null) so.FindProperty("satchelObject").objectReferenceValue = satchelObj;
            if (bodyRend != null) so.FindProperty("bodyRenderer").objectReferenceValue = bodyRend;
            so.FindProperty("animator").objectReferenceValue = anim;
            so.FindProperty("navAgent").objectReferenceValue = agent;
            so.FindProperty("footstepAudioSource").objectReferenceValue = audio;
            so.ApplyModifiedPropertiesWithoutUndo();

            conv.LocateMeshesAndShapeKeys();
            conv.ApplySatchelVisibility();
        }
    }
}
