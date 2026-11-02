using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor tools to fit and validate bin visual trash piles across all bins in the active scene.
/// Menus:
/// - EcoQuest > Fit Bin Piles
/// - EcoQuest > Validate Bin Piles
/// </summary>
public static class BinPileFittingTool
{
    public static readonly Vector3 DefaultFittedPosition = new Vector3(0.005f, 0.670f, 0.029f);
    public static readonly Vector3 DefaultFittedScale = new Vector3(0.280f, 0.950f, 0.380f);

    public const float MaxSafeAbsX = 0.300f; // Bin body wall boundary (0.334m inner cavity, 0.300m body)
    public const float MaxSafeAbsZ = 0.300f;
    public const float MaxSafeTopY = 1.110f; // Below 1.114m rim

    [MenuItem("EcoQuest/Fit Bin Piles", priority = 30)]
    public static void FitAllBinPiles()
    {
        var bins = Object.FindObjectsByType<BinVisualTrashLayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (bins == null || bins.Length == 0)
        {
            Debug.LogWarning("[Fit Bin Piles] No BinVisualTrashLayer components found in active scene.");
            return;
        }

        int fittedCount = 0;
        foreach (var bvl in bins)
        {
            if (bvl == null) continue;
            var pile = bvl.PileObject;
            if (pile == null)
            {
                var tr = bvl.transform.Find("VisualTrashPile");
                if (tr != null) pile = tr.gameObject;
            }

            if (pile != null)
            {
                Undo.RecordObject(pile.transform, "Fit Bin Pile");
                Undo.RecordObject(bvl, "Fit Bin Pile");

                pile.transform.localPosition = DefaultFittedPosition;
                pile.transform.localRotation = Quaternion.identity;
                pile.transform.localScale = DefaultFittedScale;

                bvl.Setup(pile, DefaultFittedPosition, DefaultFittedScale);
                EditorUtility.SetDirty(bvl);
                EditorUtility.SetDirty(pile.transform);
                fittedCount++;
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Fit Bin Piles] Successfully fitted visual trash piles for {fittedCount} bins. (Pos={DefaultFittedPosition}, Scale={DefaultFittedScale})");
        ValidateAllBinPiles();
    }

    [MenuItem("EcoQuest/Validate Bin Piles", priority = 31)]
    public static bool ValidateAllBinPiles()
    {
        var bins = Object.FindObjectsByType<BinVisualTrashLayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (bins == null || bins.Length == 0)
        {
            Debug.LogWarning("[Validate Bin Piles] No BinVisualTrashLayer components found in active scene.");
            return true;
        }

        int failureCount = 0;
        int checkedCount = 0;

        foreach (var bvl in bins)
        {
            if (bvl == null) continue;
            var pile = bvl.PileObject;
            if (pile == null)
            {
                var tr = bvl.transform.Find("VisualTrashPile");
                if (tr != null) pile = tr.gameObject;
            }
            if (pile == null) continue;

            var mf = pile.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            checkedCount++;
            var verts = mf.sharedMesh.vertices;
            float maxAbsX = 0f;
            float maxAbsZ = 0f;
            float maxTopY = float.MinValue;

            foreach (var v in verts)
            {
                Vector3 vLocal = pile.transform.localPosition + Vector3.Scale(v, pile.transform.localScale);
                if (Mathf.Abs(vLocal.x) > maxAbsX) maxAbsX = Mathf.Abs(vLocal.x);
                if (Mathf.Abs(vLocal.z) > maxAbsZ) maxAbsZ = Mathf.Abs(vLocal.z);
                if (vLocal.y > maxTopY) maxTopY = vLocal.y;
            }

            float overflowX = Mathf.Max(0f, maxAbsX - MaxSafeAbsX);
            float overflowZ = Mathf.Max(0f, maxAbsZ - MaxSafeAbsZ);
            float overflowY = Mathf.Max(0f, maxTopY - MaxSafeTopY);
            float totalOverflow = Mathf.Max(overflowX, Mathf.Max(overflowZ, overflowY));

            if (totalOverflow > 0.001f)
            {
                failureCount++;
                Debug.LogWarning($"[Validate Bin Piles] OVERFLOW in bin '{bvl.gameObject.name}': overflow={totalOverflow:F3}m (overX={overflowX:F3}m, overZ={overflowZ:F3}m, overY={overflowY:F3}m). Footprint: absX={maxAbsX:F3}m, absZ={maxAbsZ:F3}m, topY={maxTopY:F3}m");
            }
        }

        bool passed = (failureCount == 0);
        if (passed)
        {
            Debug.Log($"[Validate Bin Piles] PASS: All {checkedCount} bins have visual piles fully inside inner bounds with 0.00m overflow.");
        }
        else
        {
            Debug.LogError($"[Validate Bin Piles] FAIL: {failureCount} / {checkedCount} bins overflow outside inner bounds.");
        }
        return passed;
    }

    [MenuItem("EcoQuest/Run Post-Travel Health Check", priority = 32)]
    public static bool RunHealthCheckMenu()
    {
        bool passed = TravelGate.RunPostTravelHealthCheck(out string report);
        if (passed)
            Debug.Log($"[Post-Travel Health Check] {report}");
        else
            Debug.LogError($"[Post-Travel Health Check] {report}");
        return passed;
    }
}
