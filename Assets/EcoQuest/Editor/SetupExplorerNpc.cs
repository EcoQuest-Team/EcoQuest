using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace EcoQuest.Editor
{
    /// <summary>
    /// Automated setup tool for the stylized low-poly Explorer character.
    /// Menu: Tools > EcoQuest > Setup Explorer NPC
    /// Configures Humanoid Avatar, clip import settings & events, materials, 
    /// Animator Controller (locomotion blend tree, jump, dialogue, reactions),
    /// and generates base & 5 variant prefabs.
    /// </summary>
    public static class SetupExplorerNpc
    {
        private const string BaseFolder = "Assets/EcoQuest/Characters/Explorer";
        private const string TexturesFolder = "Assets/EcoQuest/Characters/Explorer/Textures";
        private const string VariantsFolder = "Assets/EcoQuest/Characters/Explorer/Variants";
        private const string FbxPath = "Assets/EcoQuest/Characters/Explorer/Explorer.fbx";
        private const string MainPalettePath = "Assets/EcoQuest/Characters/Explorer/Explorer_Palette.png";
        private const string MainMatPath = "Assets/EcoQuest/Characters/Explorer/Mat_Explorer.mat";
        private const string ControllerPath = "Assets/EcoQuest/Characters/Explorer/AC_Explorer.controller";
        private const string MainPrefabPath = "Assets/EcoQuest/Characters/Explorer/NPC_Explorer.prefab";

        private static readonly string[] VariantNames = new string[]
        {
            "Explorer_Var1", // Forest Ranger
            "Explorer_Var2", // Coastal Scout (No backpack)
            "Explorer_Var3", // Autumn Trailblazer
            "Explorer_Var4", // Urban Eco-Volunteer (No backpack)
            "Explorer_Var5"  // Alpine Expedition
        };

        private static readonly bool[] VariantBackpackVisible = new bool[]
        {
            true,
            false,
            true,
            false,
            true
        };

        [MenuItem("Tools/EcoQuest/Setup Explorer NPC", false, 100)]
        public static void RunSetup()
        {
            Debug.Log("[SetupExplorerNpc] === Starting Explorer 3D Character Setup ===");
            try
            {
                EnsureFolders();
                ConfigureTextures();
                ConfigureModelImporter();
                Material mainMat = SetupMaterials();
                AnimatorController controller = SetupAnimatorController();
                SetupPrefabs(mainMat, controller);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[SetupExplorerNpc] === Setup COMPLETE! NPC_Explorer and 5 variants ready in Assets/EcoQuest/Characters/Explorer/ ===");
            }
            catch (Exception ex)
            {
                Debug.LogError("[SetupExplorerNpc] Setup failed with error: " + ex);
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
            List<string> texPaths = new List<string> { MainPalettePath };
            for (int i = 1; i <= 5; i++)
            {
                texPaths.Add($"{TexturesFolder}/Explorer_Palette_Var{i}.png");
            }

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
                throw new FileNotFoundException("Explorer FBX not found at " + FbxPath);
            }

            bool needsReimport = false;

            if (importer.animationType != ModelImporterAnimationType.Human ||
                importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                needsReimport = true;
            }

            // Configure animation clips and events
            ModelImporterClipAnimation[] sourceClips = importer.clipAnimations;
            if (sourceClips == null || sourceClips.Length == 0)
            {
                sourceClips = importer.defaultClipAnimations;
            }

            if (sourceClips != null && sourceClips.Length > 0)
            {
                List<string> loopingClips = new List<string>()
                {
                    "Explorer_Idle", "Explorer_Walk", "Explorer_Run",
                    "Explorer_IdleLookAround", "Explorer_Talk_A", "Explorer_Talk_B",
                    "Explorer_Listen", "Explorer_JumpAir"
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

                    // Grounding fix: Bake Root Transform into Pose (Y, XZ, Rotation)
                    // With Based Upon = Original (keepOriginalPosition = true), clips stay strictly in-place at authored standing height
                    clip.lockRootHeightY = true;
                    clip.keepOriginalPositionY = true;
                    clip.heightOffset = 0f;
                    clip.lockRootPositionXZ = true;
                    clip.keepOriginalPositionXZ = true;
                    clip.lockRootRotation = true;
                    clip.keepOriginalOrientation = true;


                    List<AnimationEvent> events = new List<AnimationEvent>();
                    if (clip.name == "Explorer_Walk")
                    {
                        events.Add(new AnimationEvent { time = 2f / 30f, functionName = "OnFootstep", stringParameter = "FootL" });
                        events.Add(new AnimationEvent { time = 17f / 30f, functionName = "OnFootstep", stringParameter = "FootR" });
                    }
                    else if (clip.name == "Explorer_Run")
                    {
                        events.Add(new AnimationEvent { time = 2f / 20f, functionName = "OnFootstep", stringParameter = "FootL" });
                        events.Add(new AnimationEvent { time = 12f / 20f, functionName = "OnFootstep", stringParameter = "FootR" });
                    }
                    else if (clip.name == "Explorer_Throw")
                    {
                        events.Add(new AnimationEvent { time = 18f / 40f, functionName = "OnThrowRelease", stringParameter = "Release" });
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

        private static Material SetupMaterials()
        {
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            // Main Material
            Texture2D mainTex = AssetDatabase.LoadAssetAtPath<Texture2D>(MainPalettePath);
            Material mainMat = AssetDatabase.LoadAssetAtPath<Material>(MainMatPath);
            if (mainMat == null)
            {
                mainMat = new Material(urpShader);
                AssetDatabase.CreateAsset(mainMat, MainMatPath);
            }
            ApplyMaterialProperties(mainMat, mainTex);

            // Variant Materials
            for (int i = 0; i < VariantNames.Length; i++)
            {
                int varIdx = i + 1;
                string varMatPath = $"{VariantsFolder}/Mat_Explorer_Var{varIdx}.mat";
                string varTexPath = $"{TexturesFolder}/Explorer_Palette_Var{varIdx}.png";
                Texture2D varTex = AssetDatabase.LoadAssetAtPath<Texture2D>(varTexPath);

                Material varMat = AssetDatabase.LoadAssetAtPath<Material>(varMatPath);
                if (varMat == null)
                {
                    varMat = new Material(urpShader);
                    AssetDatabase.CreateAsset(varMat, varMatPath);
                }
                ApplyMaterialProperties(varMat, varTex);
            }

            return mainMat;
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
                    clips[clip.name] = clip;
                }
            }

            // Setup Parameters
            EnsureParameter(controller, "Speed", AnimatorControllerParameterType.Float);
            EnsureParameter(controller, "IsGrounded", AnimatorControllerParameterType.Bool, true);
            EnsureParameter(controller, "Jump", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "Talk", AnimatorControllerParameterType.Bool, false);
            EnsureParameter(controller, "IsTalking", AnimatorControllerParameterType.Bool, false);
            EnsureParameter(controller, "TalkIndex", AnimatorControllerParameterType.Int, 0);
            EnsureParameter(controller, "ActionTrigger", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "ActionType", AnimatorControllerParameterType.Int, 0);
            EnsureParameter(controller, "Wave", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "Nod", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "ShakeHead", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "Shrug", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "Surprised", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "Cheer", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "Throw", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "PickUp", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "Listen", AnimatorControllerParameterType.Bool, false);
            EnsureParameter(controller, "IdleLookAround", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            // Clear old states to ensure clean reproducible generation
            foreach (var state in sm.states)
            {
                sm.RemoveState(state.state);
            }
            foreach (var subSm in sm.stateMachines)
            {
                sm.RemoveStateMachine(subSm.stateMachine);
            }

            // 1. Locomotion Blend Tree
            AnimatorState locomotionState = sm.AddState("Locomotion", new Vector3(250, 0, 0));
            BlendTree blendTree;
            AnimatorState.Instantiate(locomotionState);
            controller.CreateBlendTreeInController("LocomotionBlend", out blendTree, 0);
            locomotionState.motion = blendTree;
            blendTree.blendType = BlendTreeType.Simple1D;
            blendTree.blendParameter = "Speed";

            if (clips.TryGetValue("Explorer_Idle", out var cIdle)) blendTree.AddChild(cIdle, 0.0f);
            if (clips.TryGetValue("Explorer_Walk", out var cWalk)) blendTree.AddChild(cWalk, 1.4f);
            if (clips.TryGetValue("Explorer_Run", out var cRun)) blendTree.AddChild(cRun, 3.5f);

            sm.defaultState = locomotionState;

            // 2. Jump States
            AnimatorState jumpStartState = sm.AddState("JumpStart", new Vector3(50, -120, 0));
            AnimatorState jumpAirState = sm.AddState("JumpAir", new Vector3(250, -120, 0));
            AnimatorState jumpLandState = sm.AddState("JumpLand", new Vector3(450, -120, 0));

            if (clips.TryGetValue("Explorer_JumpStart", out var cJumpStart)) jumpStartState.motion = cJumpStart;
            if (clips.TryGetValue("Explorer_JumpAir", out var cJumpAir)) jumpAirState.motion = cJumpAir;
            if (clips.TryGetValue("Explorer_JumpLand", out var cJumpLand)) jumpLandState.motion = cJumpLand;

            // Locomotion -> JumpStart
            var toJump = locomotionState.AddTransition(jumpStartState);
            toJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
            toJump.hasExitTime = false;
            toJump.duration = 0.08f;

            // JumpStart -> JumpAir
            var startToAir = jumpStartState.AddTransition(jumpAirState);
            startToAir.hasExitTime = true;
            startToAir.exitTime = 0.85f;
            startToAir.duration = 0.08f;

            // JumpAir -> JumpLand
            var airToLand = jumpAirState.AddTransition(jumpLandState);
            airToLand.AddCondition(AnimatorConditionMode.If, 0, "IsGrounded");
            airToLand.hasExitTime = false;
            airToLand.duration = 0.06f;

            // JumpLand -> Locomotion
            var landToLoco = jumpLandState.AddTransition(locomotionState);
            landToLoco.hasExitTime = true;
            landToLoco.exitTime = 0.80f;
            landToLoco.duration = 0.12f;

            // 3. Idle variation
            AnimatorState idleLookState = sm.AddState("IdleLookAround", new Vector3(250, 100, 0));
            if (clips.TryGetValue("Explorer_IdleLookAround", out var cLook)) idleLookState.motion = cLook;
            var toLook = locomotionState.AddTransition(idleLookState);
            toLook.AddCondition(AnimatorConditionMode.If, 0, "IdleLookAround");
            toLook.hasExitTime = false;
            toLook.duration = 0.20f;
            var lookToLoco = idleLookState.AddTransition(locomotionState);
            lookToLoco.hasExitTime = true;
            lookToLoco.exitTime = 0.90f;
            lookToLoco.duration = 0.20f;

            // 4. Dialogue states
            AnimatorState talkAState = sm.AddState("Talk_A", new Vector3(50, 180, 0));
            AnimatorState talkBState = sm.AddState("Talk_B", new Vector3(250, 180, 0));
            AnimatorState listenState = sm.AddState("Listen", new Vector3(450, 180, 0));

            if (clips.TryGetValue("Explorer_Talk_A", out var cTalkA)) talkAState.motion = cTalkA;
            if (clips.TryGetValue("Explorer_Talk_B", out var cTalkB)) talkBState.motion = cTalkB;
            if (clips.TryGetValue("Explorer_Listen", out var cListen)) listenState.motion = cListen;

            // Locomotion -> Talk_A
            var locoToTalkA1 = locomotionState.AddTransition(talkAState);
            locoToTalkA1.AddCondition(AnimatorConditionMode.If, 0, "Talk");
            locoToTalkA1.AddCondition(AnimatorConditionMode.Equals, 0, "TalkIndex");
            locoToTalkA1.hasExitTime = false;
            locoToTalkA1.duration = 0.15f;

            var locoToTalkA2 = locomotionState.AddTransition(talkAState);
            locoToTalkA2.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
            locoToTalkA2.AddCondition(AnimatorConditionMode.Equals, 0, "TalkIndex");
            locoToTalkA2.hasExitTime = false;
            locoToTalkA2.duration = 0.15f;

            // Locomotion -> Talk_B
            var locoToTalkB1 = locomotionState.AddTransition(talkBState);
            locoToTalkB1.AddCondition(AnimatorConditionMode.If, 0, "Talk");
            locoToTalkB1.AddCondition(AnimatorConditionMode.Equals, 1, "TalkIndex");
            locoToTalkB1.hasExitTime = false;
            locoToTalkB1.duration = 0.15f;

            var locoToTalkB2 = locomotionState.AddTransition(talkBState);
            locoToTalkB2.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
            locoToTalkB2.AddCondition(AnimatorConditionMode.Equals, 1, "TalkIndex");
            locoToTalkB2.hasExitTime = false;
            locoToTalkB2.duration = 0.15f;

            // Talk_A <-> Talk_B
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
            talkAToLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
            talkAToLoco.hasExitTime = false;
            talkAToLoco.duration = 0.15f;

            var talkBToLoco = talkBState.AddTransition(locomotionState);
            talkBToLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "Talk");
            talkBToLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
            talkBToLoco.hasExitTime = false;
            talkBToLoco.duration = 0.15f;

            // Listen
            var locoToListen = locomotionState.AddTransition(listenState);
            locoToListen.AddCondition(AnimatorConditionMode.If, 0, "Listen");
            locoToListen.hasExitTime = false;
            locoToListen.duration = 0.15f;

            var listenToLoco = listenState.AddTransition(locomotionState);
            listenToLoco.AddCondition(AnimatorConditionMode.IfNot, 0, "Listen");
            listenToLoco.hasExitTime = false;
            listenToLoco.duration = 0.15f;

            // 5. Reaction States
            CreateReactionState(sm, locomotionState, "Wave", "Explorer_Wave", clips, 1);
            CreateReactionState(sm, locomotionState, "Nod", "Explorer_Nod", clips, 2);
            CreateReactionState(sm, locomotionState, "Cheer", "Explorer_Cheer", clips, 3);
            CreateReactionState(sm, locomotionState, "ShakeHead", "Explorer_ShakeHead", clips, -1);
            CreateReactionState(sm, locomotionState, "Shrug", "Explorer_Shrug", clips, -1);
            CreateReactionState(sm, locomotionState, "Surprised", "Explorer_Surprised", clips, -1);
            CreateReactionState(sm, locomotionState, "Throw", "Explorer_Throw", clips, -1);
            CreateReactionState(sm, locomotionState, "PickUp", "Explorer_PickUp", clips, -1);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void CreateReactionState(
            AnimatorStateMachine sm,
            AnimatorState returnState,
            string triggerName,
            string clipName,
            Dictionary<string, AnimationClip> clips,
            int actionTypeValue)
        {
            AnimatorState state = sm.AddState(triggerName);
            if (clips.TryGetValue(clipName, out var clip))
            {
                state.motion = clip;
            }

            // Direct trigger transition
            var toState = sm.AddAnyStateTransition(state);
            toState.AddCondition(AnimatorConditionMode.If, 0, triggerName);
            toState.hasExitTime = false;
            toState.duration = 0.12f;

            // Compatibility ActionType transition if specified
            if (actionTypeValue > 0)
            {
                var toStateCompat = sm.AddAnyStateTransition(state);
                toStateCompat.AddCondition(AnimatorConditionMode.If, 0, "ActionTrigger");
                toStateCompat.AddCondition(AnimatorConditionMode.Equals, actionTypeValue, "ActionType");
                toStateCompat.hasExitTime = false;
                toStateCompat.duration = 0.12f;
            }

            // Return transition
            var backToLoco = state.AddTransition(returnState);
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

        private static void SetupPrefabs(Material mainMat, AnimatorController controller)
        {
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbxAsset == null)
            {
                throw new FileNotFoundException("FBX asset not found at " + FbxPath);
            }

            // --- Base Prefab: NPC_Explorer ---
            GameObject mainInstance = PrefabUtility.InstantiatePrefab(fbxAsset) as GameObject;
            PrefabUtility.UnpackPrefabInstance(mainInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            mainInstance.name = "NPC_Explorer";
            ConfigureExplorerComponents(mainInstance, mainMat, controller, true);
            PrefabUtility.SaveAsPrefabAsset(mainInstance, MainPrefabPath);
            GameObject.DestroyImmediate(mainInstance);
            Debug.Log("[SetupExplorerNpc] Created base prefab: " + MainPrefabPath);

            // --- 5 Variants: Explorer_Var1..5 ---
            for (int i = 0; i < VariantNames.Length; i++)
            {
                int varIdx = i + 1;
                string varMatPath = $"{VariantsFolder}/Mat_Explorer_Var{varIdx}.mat";
                Material varMat = AssetDatabase.LoadAssetAtPath<Material>(varMatPath);
                string variantPrefabPath = $"{VariantsFolder}/{VariantNames[i]}.prefab";

                GameObject varInstance = PrefabUtility.InstantiatePrefab(fbxAsset) as GameObject;
                PrefabUtility.UnpackPrefabInstance(varInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                varInstance.name = VariantNames[i];
                ConfigureExplorerComponents(varInstance, varMat, controller, VariantBackpackVisible[i]);
                PrefabUtility.SaveAsPrefabAsset(varInstance, variantPrefabPath);
                GameObject.DestroyImmediate(varInstance);
                Debug.Log("[SetupExplorerNpc] Created variant prefab: " + variantPrefabPath);
            }
        }

        private static void ConfigureExplorerComponents(
            GameObject go,
            Material mat,
            AnimatorController controller,
            bool backpackVisible)
        {
            // 1. Animator
            Animator anim = go.GetComponent<Animator>();
            if (anim == null) anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false; // Must be false for in-place animations driven by NavMeshAgent
            SerializedObject animSo = new SerializedObject(anim);
            var armProp = animSo.FindProperty("m_ApplyRootMotion");
            if (armProp != null)
            {
                armProp.boolValue = false;
                animSo.ApplyModifiedProperties();
            }

            // Load Avatar
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
            UnityEngine.AI.NavMeshAgent agent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent == null) agent = go.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.75f;
            agent.speed = 1.4f;
            agent.acceleration = 8.0f;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = 1.2f;

            // 3. CapsuleCollider
            CapsuleCollider col = go.GetComponent<CapsuleCollider>();
            if (col == null) col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.90f, 0f);
            col.radius = 0.35f;
            col.height = 1.80f;

            // 4. Interaction SphereCollider (Trigger)
            SphereCollider triggerCol = go.AddComponent<SphereCollider>();
            triggerCol.isTrigger = true;
            triggerCol.center = new Vector3(0f, 0.90f, 0f);
            triggerCol.radius = 2.5f;

            // 5. AudioSource
            AudioSource audio = go.GetComponent<AudioSource>();
            if (audio == null) audio = go.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1.0f;
            audio.minDistance = 2.0f;
            audio.maxDistance = 18.0f;

            // 6. Materials and Backpack visibility
            GameObject backpackObj = null;
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
                else if (r.name.IndexOf("Backpack", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    backpackObj = r.gameObject;
                    backpackObj.SetActive(backpackVisible);
                }
            }

            // 7. NpcExplorerController
            NpcExplorerController ctrl = go.GetComponent<NpcExplorerController>();
            if (ctrl == null) ctrl = go.AddComponent<NpcExplorerController>();

            // Use SerializedObject to cleanly set private fields
            SerializedObject so = new SerializedObject(ctrl);
            so.FindProperty("showBackpack").boolValue = backpackVisible;
            if (backpackObj != null) so.FindProperty("backpackObject").objectReferenceValue = backpackObj;
            if (bodyRend != null) so.FindProperty("bodyRenderer").objectReferenceValue = bodyRend;
            so.FindProperty("animator").objectReferenceValue = anim;
            so.FindProperty("navAgent").objectReferenceValue = agent;
            so.FindProperty("footstepAudioSource").objectReferenceValue = audio;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
