using System;
using System.IO;
using System.Diagnostics;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Comparison tool for EcoQuest Loading Screen against Reference Artwork.
/// Menu: EcoQuest > Compare Loading Screen To Reference
/// Captures high-res render at 2752x1536 and executes SSIM/PSNR comparison on the UI region (bottom 30%).
/// Outputs to C:\EcoQuest.gg\Reports\Loading\
/// </summary>
public static class LoadingScreenComparison
{
    private const string REPORT_DIR = @"C:\EcoQuest.gg\Reports\Loading";
    private static readonly string PIPELINE_SCRIPT = @"C:\Users\ssopb\.gemini\antigravity\brain\dddfe162-d84e-4f03-8823-1a1772e16d60\scratch\run_comparison_pipeline.py";

    [MenuItem("EcoQuest/Compare Loading Screen To Reference", false, 126)]
    public static void CompareToReference()
    {
        UnityEngine.Debug.Log("===============================================================");
        UnityEngine.Debug.Log("    STARTING LOADING SCREEN COMPARISON TO REFERENCE ARTWORK    ");
        UnityEngine.Debug.Log("===============================================================");

        if (!Directory.Exists(REPORT_DIR))
        {
            Directory.CreateDirectory(REPORT_DIR);
        }

        // Rebuild prefab to guarantee latest references
        LoadingScreenTools.BuildPrefab();

        // Run python comparison pipeline
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{PIPELINE_SCRIPT}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(error))
                {
                    UnityEngine.Debug.LogWarning($"[Compare Python Error] {error}");
                }

                UnityEngine.Debug.Log($"[Compare Python Metrics]\n{output}");

                string jsonPath = Path.Combine(REPORT_DIR, "loading_metrics.json");
                if (File.Exists(jsonPath))
                {
                    string jsonContent = File.ReadAllText(jsonPath);
                    UnityEngine.Debug.Log($"===============================================================\nPIXEL COMPARISON REPORT (Target SSIM >= 0.93):\n{jsonContent}\n===============================================================");
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[Compare] Failed to run comparison pipeline: {ex.Message}");
        }
    }
}
