#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NpcAnimationValidator
{
    private const string ControllerPath = "Assets/EcoQuest/Asset/GuardianAnimatorController.controller";

    [MenuItem("NPC/Validate Animation")]
    public static void ValidateAndFixAnimations()
    {
        Debug.Log("=== Starting NPC/Validate Animation ===");

        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Cannot run while in Play Mode. Please exit Play Mode first.");
            return;
        }

        var sharedController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (sharedController == null)
        {
            Debug.LogError($"Could not load Animator Controller at {ControllerPath}!");
            return;
        }

        // Verify Speed parameter exists
        var animController = sharedController as AnimatorController;
        bool hasSpeed = false;
        if (animController != null)
        {
            foreach (var p in animController.parameters)
            {
                if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float)
                {
                    hasSpeed = true;
                    break;
                }
            }
        }
        Debug.Log($"GuardianAnimatorController has 'Speed' parameter: {hasSpeed}");

        int prefabsChecked = 0;
        int prefabsValid = 0;

        string[] searchFolders = { "Assets/EcoQuest/Prefabs/NPC", "Assets/LowPolyCivilians/Prefabs" };
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", searchFolders);

        foreach (var guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
            if (prefabRoot == null) continue;

            prefabsChecked++;
            bool changed = false;

            // Remove legacy Animation
            var legacyAnim = prefabRoot.GetComponent<Animation>();
            if (legacyAnim != null)
            {
                Object.DestroyImmediate(legacyAnim);
                changed = true;
            }

            var anim = prefabRoot.GetComponent<Animator>();
            if (anim == null)
            {
                anim = prefabRoot.AddComponent<Animator>();
                changed = true;
            }

            if (anim.runtimeAnimatorController != sharedController)
            {
                anim.runtimeAnimatorController = sharedController;
                changed = true;
            }

            if (anim.applyRootMotion)
            {
                anim.applyRootMotion = false;
                changed = true;
            }

            if (anim.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            {
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                changed = true;
            }

            bool isValid = (anim.avatar != null && anim.avatar.isHuman && anim.runtimeAnimatorController != null);
            if (isValid) prefabsValid++;
            else
            {
                Debug.LogWarning($"Prefab {path} has invalid avatar or humanoid setup!");
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
            }
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        Debug.Log($"=== ANIMATION VALIDATION RESULTS ===\nPrefabs Checked: {prefabsChecked}\nPrefabs Valid: {prefabsValid}\nFailed: {prefabsChecked - prefabsValid}");
        if (prefabsValid == prefabsChecked && prefabsChecked > 0)
        {
            Debug.Log("<color=green>SUCCESS: 100% of NPC prefabs have valid Humanoid Rig, Controller, and AlwaysAnimate!</color>");
        }
    }
}
#endif
