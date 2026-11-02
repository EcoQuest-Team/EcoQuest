using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window to preview bin fill levels across all trash bins in the active scene.
/// Provides a 0-100% slider, Fill All, Empty All, and Reset All buttons.
/// Menu: EcoQuest > Preview Bin Fill
/// </summary>
public class BinFillPreviewWindow : EditorWindow
{
    private float fillPercentage = 0f;

    [MenuItem("EcoQuest/Preview Bin Fill", priority = 20)]
    public static void OpenWindow()
    {
        var window = GetWindow<BinFillPreviewWindow>("Bin Fill Preview");
        window.minSize = new Vector2(320, 200);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Bin Visual Fill Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Adjust fill percentage across all bins in the active scene to preview visual litter distribution.", MessageType.Info);
        EditorGUILayout.Space(10);

        EditorGUI.BeginChangeCheck();
        fillPercentage = EditorGUILayout.Slider("Fill Level", fillPercentage, 0f, 1f);
        EditorGUILayout.LabelField($"Percentage: {Mathf.RoundToInt(fillPercentage * 100f)}%", EditorStyles.miniLabel);

        if (EditorGUI.EndChangeCheck())
        {
            ApplyFillToAllBins(fillPercentage);
        }

        EditorGUILayout.Space(15);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Empty All (0%)", GUILayout.Height(30)))
        {
            fillPercentage = 0f;
            ApplyFillToAllBins(0f);
        }

        if (GUILayout.Button("Fill All (100%)", GUILayout.Height(30)))
        {
            fillPercentage = 1f;
            ApplyFillToAllBins(1f);
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(5);

        if (GUILayout.Button("Reset to Game State", GUILayout.Height(28)))
        {
            ResetAllBins();
        }
    }

    private static void ApplyFillToAllBins(float fill)
    {
        var bins = Object.FindObjectsByType<BinVisualTrashLayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (bins == null || bins.Length == 0)
        {
            Debug.LogWarning("[BinFillPreview] No BinVisualTrashLayer components found in active scene.");
            return;
        }

        Undo.RecordObjects(bins, "Preview Bin Fill");
        foreach (var b in bins)
        {
            if (b == null) continue;
            b.SetFillPercent(fill, animatePop: false);
            EditorUtility.SetDirty(b);
        }
        SceneView.RepaintAll();
    }

    private void ResetAllBins()
    {
        var bins = Object.FindObjectsByType<BinVisualTrashLayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (bins == null || bins.Length == 0) return;

        Undo.RecordObjects(bins, "Reset Bin Fill");
        foreach (var b in bins)
        {
            if (b == null) continue;
            if (Application.isPlaying)
            {
                b.RestoreSavedState();
            }
            else
            {
                b.ResetVisuals();
            }
            EditorUtility.SetDirty(b);
        }
        fillPercentage = 0f;
        SceneView.RepaintAll();
    }
}
