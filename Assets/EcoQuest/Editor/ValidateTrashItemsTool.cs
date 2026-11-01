using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class ValidateTrashItemsTool
{
    [MenuItem("EcoQuest/Validate Trash Items", false, 50)]
    [MenuItem("Tools/EcoQuest/Validate Trash Items", false, 50)]
    public static void ValidateFromMenu()
    {
        RunValidation(showDialog: true);
    }

    public static bool RunValidation(bool showDialog = false)
    {
        int issuesCount = 0;
        int passedCount = 0;
        var logLines = new List<string>();

        logLines.Add("=== ECOQUEST TRASH ITEMS & BINS VALIDATION ===");

        // 1. Audit Trash Items
        var trashItems = Object.FindObjectsByType<TrashItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        logLines.Add($"Found {trashItems.Length} TrashItem object(s) in scene (Expected: 9).");
        if (trashItems.Length != 9)
        {
            logLines.Add($"[WARNING] Expected 9 trash items, found {trashItems.Length}.");
            issuesCount++;
        }

        int trashLayer = LayerMask.NameToLayer("TrashItem");

        foreach (var item in trashItems)
        {
            string name = item.gameObject.name;
            bool itemOk = true;

            // Check Layer
            if (item.gameObject.layer != trashLayer)
            {
                logLines.Add($"[FAIL] '{name}': Layer is {LayerMask.LayerToName(item.gameObject.layer)} ({item.gameObject.layer}), expected TrashItem ({trashLayer}).");
                issuesCount++;
                itemOk = false;
            }

            // Check Tag
            string tag = item.tag;
            if (tag != "Biodegradable" && tag != "Recyclable" && tag != "NonBiodegradable")
            {
                logLines.Add($"[WARNING] '{name}': Tag '{tag}' is not standard category tag.");
            }

            // Check DragAndDrop
            var dad = item.GetComponent<DragAndDrop>();
            if (dad == null)
            {
                logLines.Add($"[FAIL] '{name}': Missing DragAndDrop component.");
                issuesCount++;
                itemOk = false;
            }

            // Check Rigidbody
            var rb = item.GetComponent<Rigidbody>();
            if (rb == null)
            {
                logLines.Add($"[FAIL] '{name}': Missing Rigidbody component.");
                issuesCount++;
                itemOk = false;
            }
            else
            {
                if (rb.mass <= 0f)
                {
                    logLines.Add($"[FAIL] '{name}': Rigidbody mass is <= 0.");
                    issuesCount++;
                    itemOk = false;
                }
            }

            // Check Collider
            var cols = item.GetComponentsInChildren<Collider>(true);
            bool hasSolidCollider = false;
            foreach (var c in cols)
            {
                if (c != null && !c.isTrigger && c.enabled)
                {
                    hasSolidCollider = true;
                }
                if (c != null && c.gameObject.layer != trashLayer)
                {
                    logLines.Add($"[FAIL] '{name}' child collider '{c.gameObject.name}': Layer is {LayerMask.LayerToName(c.gameObject.layer)}, expected TrashItem ({trashLayer}).");
                    issuesCount++;
                    itemOk = false;
                }
            }
            if (!hasSolidCollider)
            {
                logLines.Add($"[FAIL] '{name}': Has no enabled non-trigger collider.");
                issuesCount++;
                itemOk = false;
            }

            // Check Collider vs Renderer Bounds alignment
            var meshRend = item.GetComponentInChildren<MeshRenderer>(true);
            var boxCol = item.GetComponent<BoxCollider>();
            if (meshRend != null && boxCol != null)
            {
                Bounds rBounds = meshRend.localBounds;
                Vector3 bSize = boxCol.size;
                float volR = Mathf.Max(0.0001f, rBounds.size.x * rBounds.size.y * rBounds.size.z);
                float volB = Mathf.Max(0.0001f, bSize.x * bSize.y * bSize.z);
                float ratio = volB / volR;
                if (ratio > 3.0f || ratio < 0.3f)
                {
                    logLines.Add($"[NOTICE] '{name}': BoxCollider size {bSize} differs from MeshRenderer bounds {rBounds.size} (vol ratio={ratio:F2}). Can fit via context menu 'EcoQuest: Fit Collider To Renderer'.");
                }
            }

            // Check Scale
            Vector3 ls = item.transform.localScale;
            if (ls.x <= 0f || ls.y <= 0f || ls.z <= 0f)
            {
                logLines.Add($"[FAIL] '{name}': Invalid localScale {ls}.");
                issuesCount++;
                itemOk = false;
            }

            if (itemOk) passedCount++;
        }

        // 2. Audit Bins
        var bins = Object.FindObjectsByType<QuestManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        logLines.Add($"\nFound {bins.Length} QuestManager Bin(s) in scene (Expected: 9).");
        if (bins.Length != 9)
        {
            logLines.Add($"[WARNING] Expected 9 bins, found {bins.Length}.");
            issuesCount++;
        }

        foreach (var bin in bins)
        {
            string binName = bin.gameObject.name;
            var dp = bin.transform.Find("DepositPoint");
            if (dp == null)
            {
                logLines.Add($"[FAIL] Bin '{binName}': Missing child 'DepositPoint'.");
                issuesCount++;
            }
            else
            {
                // Verify serialized field
                var so = new SerializedObject(bin);
                var prop = so.FindProperty("depositPoint");
                if (prop != null && prop.objectReferenceValue == null)
                {
                    logLines.Add($"[FAIL] Bin '{binName}': 'depositPoint' field is not assigned in Inspector.");
                    issuesCount++;
                }
            }

            // Check trigger collider
            var boxCol = bin.GetComponent<BoxCollider>();
            if (boxCol == null || !boxCol.isTrigger)
            {
                logLines.Add($"[FAIL] Bin '{binName}': Missing BoxCollider with isTrigger=true.");
                issuesCount++;
            }

            // Check Bin_SolidCollider
            var solidChild = bin.transform.Find("Bin_SolidCollider");
            if (solidChild == null)
            {
                logLines.Add($"[FAIL] Bin '{binName}': Missing child 'Bin_SolidCollider'.");
                issuesCount++;
            }
            else if (solidChild.gameObject.layer != LayerMask.NameToLayer("BinSolid"))
            {
                logLines.Add($"[FAIL] Bin '{binName}': 'Bin_SolidCollider' is on layer {LayerMask.LayerToName(solidChild.gameObject.layer)}, expected BinSolid (10).");
                issuesCount++;
            }
        }

        logLines.Add($"\n=== RESULT: {passedCount} items validated ok, {issuesCount} issue(s) detected. ===");

        string fullReport = string.Join("\n", logLines);
        if (issuesCount == 0)
        {
            Debug.Log($"[ValidateTrashItemsTool] PASSED! Zero issues found.\n{fullReport}");
            if (showDialog)
            {
                EditorUtility.DisplayDialog("EcoQuest Validation", "All 9 trash items and 9 bins validated successfully with 0 issues!", "OK");
            }
            return true;
        }
        else
        {
            Debug.LogWarning($"[ValidateTrashItemsTool] FAILED with {issuesCount} issue(s)!\n{fullReport}");
            if (showDialog)
            {
                EditorUtility.DisplayDialog("EcoQuest Validation", $"Validation detected {issuesCount} issue(s). Check Console for details.", "OK");
            }
            return false;
        }
    }
}
