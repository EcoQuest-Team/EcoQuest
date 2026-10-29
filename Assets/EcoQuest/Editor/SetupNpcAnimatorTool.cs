#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class SetupNpcAnimatorTool
{
    private const string ControllerPath = "Assets/EcoQuest/Asset/GuardianAnimatorController.controller";

    [MenuItem("NPC/Setup NPC Animator Controller")]
    public static void SetupController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }

        Undo.RecordObject(controller, "Setup NPC Animator Controller");

        // Helper to load clip from FBX
        AnimationClip LoadClip(string fbxPath)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (var a in assets)
            {
                if (a is AnimationClip c && !c.name.StartsWith("__preview__"))
                    return c;
            }
            return null;
        }

        var idleClip = LoadClip("Assets/EverydayMotionPack/Motion/01_Idle/@female_idle_breath.FBX");
        var walkClip = LoadClip("Assets/EverydayMotionPack/Motion/02_Move/@female_move_walk_normal.FBX");
        var runClip = LoadClip("Assets/EverydayMotionPack/Motion/02_Move/@female_move_run_jogging.FBX");
        var talkClip = LoadClip("Assets/EverydayMotionPack/Motion/04_Interaction/@female_talk_stand.FBX");
        var greetClip = LoadClip("Assets/EverydayMotionPack/Motion/04_Interaction/@female_greet.FBX");
        var nodClip = LoadClip("Assets/EverydayMotionPack/Motion/04_Interaction/@female_nod_stand.FBX");
        var happyClip = LoadClip("Assets/EverydayMotionPack/Motion/03_Emotion/@female_emotion_happy.FBX");

        // Clear existing parameters
        controller.parameters = new AnimatorControllerParameter[0];

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsTalking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("ActionTrigger", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("ActionType", AnimatorControllerParameterType.Int);

        var rootStateMachine = controller.layers[0].stateMachine;

        // Clear existing states and transitions
        for (int i = rootStateMachine.states.Length - 1; i >= 0; i--)
        {
            rootStateMachine.RemoveState(rootStateMachine.states[i].state);
        }
        for (int i = rootStateMachine.anyStateTransitions.Length - 1; i >= 0; i--)
        {
            rootStateMachine.RemoveAnyStateTransition(rootStateMachine.anyStateTransitions[i]);
        }

        // 1. Locomotion Blend Tree State
        var locomotionState = rootStateMachine.AddState("Locomotion");
        BlendTree blendTree;
        var tree = controller.CreateBlendTreeInController("LocomotionTree", out blendTree);
        blendTree.blendType = BlendTreeType.Simple1D;
        blendTree.blendParameter = "Speed";

        if (idleClip != null) blendTree.AddChild(idleClip, 0.0f);
        if (walkClip != null) blendTree.AddChild(walkClip, 1.4f);
        if (runClip != null) blendTree.AddChild(runClip, 3.5f);

        locomotionState.motion = blendTree;
        rootStateMachine.defaultState = locomotionState;

        // 2. Talk State
        var talkState = rootStateMachine.AddState("Talk");
        talkState.motion = talkClip;

        var toTalk = locomotionState.AddTransition(talkState);
        toTalk.hasExitTime = false;
        toTalk.duration = 0.25f;
        toTalk.AddCondition(AnimatorConditionMode.If, 0f, "IsTalking");

        var fromTalk = talkState.AddTransition(locomotionState);
        fromTalk.hasExitTime = false;
        fromTalk.duration = 0.25f;
        fromTalk.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsTalking");

        // 3. Greet State (Wave)
        var greetState = rootStateMachine.AddState("Greet");
        greetState.motion = greetClip;

        var toGreet = locomotionState.AddTransition(greetState);
        toGreet.hasExitTime = false;
        toGreet.duration = 0.2f;
        toGreet.AddCondition(AnimatorConditionMode.If, 0f, "ActionTrigger");
        toGreet.AddCondition(AnimatorConditionMode.Equals, 1f, "ActionType");

        var fromGreet = greetState.AddTransition(locomotionState);
        fromGreet.hasExitTime = true;
        fromGreet.exitTime = 0.85f;
        fromGreet.duration = 0.2f;

        // 4. Nod State
        var nodState = rootStateMachine.AddState("Nod");
        nodState.motion = nodClip;

        var toNod = locomotionState.AddTransition(nodState);
        toNod.hasExitTime = false;
        toNod.duration = 0.2f;
        toNod.AddCondition(AnimatorConditionMode.If, 0f, "ActionTrigger");
        toNod.AddCondition(AnimatorConditionMode.Equals, 2f, "ActionType");

        var fromNod = nodState.AddTransition(locomotionState);
        fromNod.hasExitTime = true;
        fromNod.exitTime = 0.85f;
        fromNod.duration = 0.2f;

        // 5. Happy State
        var happyState = rootStateMachine.AddState("Happy");
        happyState.motion = happyClip;

        var toHappy = locomotionState.AddTransition(happyState);
        toHappy.hasExitTime = false;
        toHappy.duration = 0.2f;
        toHappy.AddCondition(AnimatorConditionMode.If, 0f, "ActionTrigger");
        toHappy.AddCondition(AnimatorConditionMode.Equals, 3f, "ActionType");

        var fromHappy = happyState.AddTransition(locomotionState);
        fromHappy.hasExitTime = true;
        fromHappy.exitTime = 0.85f;
        fromHappy.duration = 0.2f;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log("<color=green>SUCCESS: GuardianAnimatorController configured with Locomotion (Idle/Walk/Run), Talk, Greet, Nod, Happy.</color>");
    }
}
#endif
