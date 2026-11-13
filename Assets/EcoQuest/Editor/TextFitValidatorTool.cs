#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Editor utility verifying that key UI buttons and banners handle short strings,
/// "Active 4:59" timers, and ~120-character stress text without overflow or clipping.
/// </summary>
public static class TextFitValidatorTool
{
    private const string StressText120 =
        "Sort biodegradable food scraps, compostable leaves, and yard waste carefully to keep park bins clean!";

    private static readonly string[] CheckSamples =
    {
        "BUY",
        "USE",
        "Next >",
        "Retry",
        "Active 4:59",
        "Biodegradable",
        "Take Bonus Quiz (+70 Coins Max)",
        StressText120
    };

    [MenuItem("EcoQuest/UI/Validate Text Fit")]
    public static void ValidateAllLoadedTextFit()
    {
        int checkedCount = 0;
        int failedCount = 0;
        var failures = new List<string>();

        // 1. Validate the shared ResponsiveButton prefab across all sample strings
        var sharedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/UI/Shared/ResponsiveButton.prefab");
        if (sharedPrefab != null)
        {
            var inst = UnityEngine.Object.Instantiate(sharedPrefab);
            inst.name = "__TextFitValidator_Temp";
            try
            {
                var rtb = inst.GetComponent<ResponsiveTextButton>();
                foreach (string sample in CheckSamples)
                {
                    checkedCount++;
                    rtb.SetText(sample);
                    var res = rtb.ValidateFit();
                    if (!res.Passed)
                    {
                        failedCount++;
                        failures.Add($"[ResponsiveButton.prefab | '{Short(sample)}'] {res.Summary}");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }
        else
        {
            failedCount++;
            failures.Add("Missing Assets/EcoQuest/UI/Shared/ResponsiveButton.prefab");
        }

        // 2. Validate every ResponsiveTextButton currently in open scenes / prefabs (preserving original text)
        var sceneButtons = Resources.FindObjectsOfTypeAll<ResponsiveTextButton>();
        foreach (var rtb in sceneButtons)
        {
            if (rtb == null || EditorUtility.IsPersistent(rtb.gameObject)) continue;
            if (!rtb.gameObject.scene.IsValid()) continue;

            var tmp = rtb.Label;
            if (tmp == null) continue;
            string originalText = tmp.text;
            try
            {
                foreach (string sample in new[] { "OK", "Active 4:59", StressText120 })
                {
                    checkedCount++;
                    rtb.SetText(sample);
                    var res = rtb.ValidateFit();
                    if (!res.Passed)
                    {
                        failedCount++;
                        failures.Add($"[{rtb.gameObject.name} ({rtb.gameObject.scene.name}) | '{Short(sample)}'] {res.Summary}");
                    }
                }
            }
            finally
            {
                rtb.SetText(originalText);
            }
        }

        // 3. Validate WrongTrashPopup if present
        foreach (var wrongPopup in Resources.FindObjectsOfTypeAll<WrongTrashPopup>())
        {
            if (wrongPopup == null || !wrongPopup.gameObject.scene.IsValid()) continue;
            checkedCount++;
            var hint = wrongPopup.GetComponentInChildren<TextMeshProUGUI>(true);
            if (hint != null && hint.enableWordWrapping == false && hint.textWrappingMode == TextWrappingModes.NoWrap)
            {
                failedCount++;
                failures.Add($"[WrongTrashPopup ({wrongPopup.gameObject.scene.name})] wrapping disabled");
            }
        }

        string report = $"[EcoQuest] Text Fit Validation complete: checked={checkedCount}, failed={failedCount}";
        if (failedCount == 0)
        {
            Debug.Log($"{report} (ALL PASSED)");
        }
        else
        {
            Debug.LogWarning($"{report}\n - " + string.Join("\n - ", failures));
        }
    }

    private static string Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= 24 ? s : s.Substring(0, 21) + "...";
    }
}
#endif
