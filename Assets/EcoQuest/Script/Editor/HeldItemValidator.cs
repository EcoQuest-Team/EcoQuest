#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class HeldItemValidator
{
    public struct ValidationIssue
    {
        public string objectName;
        public string assetPath;
        public string issueDescription;
    }

    [MenuItem("Tools/EcoQuest/Validate Held Items")]
    public static void ValidateHeldItems()
    {
        List<ValidationIssue> issues = new List<ValidationIssue>();
        int sceneItemCount = 0;
        int prefabCount = 0;

        // 1. Scan Scene Items
        var sceneTrashItems = Object.FindObjectsByType<TrashItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sceneDndItems = Object.FindObjectsByType<DragAndDrop>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sceneSet = new HashSet<GameObject>();

        foreach (var t in sceneTrashItems) if (t != null) sceneSet.Add(t.gameObject);
        foreach (var d in sceneDndItems) if (d != null) sceneSet.Add(d.gameObject);

        if (sceneSet.Count == 0 && !EditorApplication.isPlaying)
        {
            string forestScene = "Assets/EcoQuest/Forest Scene.unity";
            if (System.IO.File.Exists(forestScene))
            {
                EditorSceneManager.OpenScene(forestScene, OpenSceneMode.Single);
                var forestTrash = Object.FindObjectsByType<TrashItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                var forestDnd = Object.FindObjectsByType<DragAndDrop>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var t in forestTrash) if (t != null) sceneSet.Add(t.gameObject);
                foreach (var d in forestDnd) if (d != null) sceneSet.Add(d.gameObject);
            }
        }

        sceneItemCount = sceneSet.Count;
        foreach (var go in sceneSet)
        {
            ValidateGameObject(go, $"(Scene: {go.scene.name})", issues);
        }

        // 2. Scan Project Prefabs
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/EcoQuest" });
        foreach (var guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            bool isTrashCandidate = prefab.GetComponent<TrashItem>() != null ||
                                    prefab.GetComponent<DragAndDrop>() != null ||
                                    prefab.CompareTag("Biodegradable") ||
                                    prefab.CompareTag("Recyclable") ||
                                    prefab.CompareTag("NonBiodegradable");

            if (isTrashCandidate)
            {
                prefabCount++;
                ValidateGameObject(prefab, path, issues);
            }
        }

        // 3. Report Results
        Debug.Log($"[HeldItemValidator] Scanned {sceneItemCount} scene items and {prefabCount} prefabs.");
        if (issues.Count == 0)
        {
            Debug.Log("<color=green>[HeldItemValidator] PASS: All held items are valid! Zero issues found.</color>");
            EditorUtility.DisplayDialog("Held Item Validation",
                $"Validation PASSED!\n\nScanned {sceneItemCount} scene items and {prefabCount} prefabs.\nZero issues found.",
                "OK");
        }
        else
        {
            Debug.LogWarning($"<color=yellow>[HeldItemValidator] Found {issues.Count} issues across held items.</color>");
            foreach (var issue in issues)
            {
                Debug.LogWarning($"[HeldItemValidator] Issue on '{issue.objectName}' ({issue.assetPath}): {issue.issueDescription}");
            }
            EditorUtility.DisplayDialog("Held Item Validation",
                $"Validation found {issues.Count} issue(s).\nCheck the Unity Console for detailed reports.",
                "OK");
        }
    }

    public static void ValidateGameObject(GameObject go, string sourcePath, List<ValidationIssue> issues)
    {
        if (go == null) return;

        // Check required components
        if (go.GetComponent<DragAndDrop>() == null)
        {
            issues.Add(new ValidationIssue
            {
                objectName = go.name,
                assetPath = sourcePath,
                issueDescription = "Missing DragAndDrop component."
            });
        }

        if (go.GetComponent<TrashItem>() == null)
        {
            issues.Add(new ValidationIssue
            {
                objectName = go.name,
                assetPath = sourcePath,
                issueDescription = "Missing TrashItem component."
            });
        }

        // Check Rigidbody
        var rb = go.GetComponent<Rigidbody>();
        if (rb == null)
        {
            issues.Add(new ValidationIssue
            {
                objectName = go.name,
                assetPath = sourcePath,
                issueDescription = "Missing Rigidbody component."
            });
        }

        // Check Colliders
        var colliders = go.GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0)
        {
            issues.Add(new ValidationIssue
            {
                objectName = go.name,
                assetPath = sourcePath,
                issueDescription = "Missing Collider component (no colliders in hierarchy)."
            });
        }

        // Check Scale
        Vector3 s = go.transform.localScale;
        if (s.x <= 0.0001f || s.y <= 0.0001f || s.z <= 0.0001f)
        {
            issues.Add(new ValidationIssue
            {
                objectName = go.name,
                assetPath = sourcePath,
                issueDescription = $"Invalid scale (zero or negative): {s}"
            });
        }
        else if (s.x > 5.0f || s.y > 5.0f || s.z > 5.0f)
        {
            issues.Add(new ValidationIssue
            {
                objectName = go.name,
                assetPath = sourcePath,
                issueDescription = $"Excessive scale (> 5.0): {s}"
            });
        }

        // Check Layer
        int trashLayer = LayerMask.NameToLayer("TrashItem");
        if (trashLayer != -1 && go.layer != trashLayer)
        {
            issues.Add(new ValidationIssue
            {
                objectName = go.name,
                assetPath = sourcePath,
                issueDescription = $"Layer is '{LayerMask.LayerToName(go.layer)}' instead of 'TrashItem'."
            });
        }
    }
}
#endif
