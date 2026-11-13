using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Editor build helpers for producing verified Windows x64 EcoQuest builds.
/// </summary>
public static class BuildTools
{
    private static readonly string[] ExpectedScenePaths =
    {
        "Assets/EcoQuest/Main Menu.unity",
        "Assets/EcoQuest/Forest Scene.unity",
        "Assets/EcoQuest/City Scene.unity"
    };

    [MenuItem("EcoQuest/Build Windows (Release)", false, 200)]
    public static void BuildWindowsRelease()
    {
        BuildWindows("Builds/EcoQuest_v1.1.5", BuildOptions.None);
    }

    [MenuItem("EcoQuest/Build Windows (Development)", false, 201)]
    public static void BuildWindowsDevelopment()
    {
        BuildWindows("Builds/EcoQuest_v1.1.5_Dev", BuildOptions.Development);
    }

    public static void BuildWindowsAll()
    {
        BuildWindowsRelease();
        BuildWindowsDevelopment();
    }

    private static void BuildWindows(string outputFolder, BuildOptions options)
    {
        string[] enabledScenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (!enabledScenes.SequenceEqual(ExpectedScenePaths))
        {
            string msg = $"[BuildTools] Build aborted: expected 3 enabled scenes ({string.Join(", ", ExpectedScenePaths)}), found ({string.Join(", ", enabledScenes)}).";
            Debug.LogError(msg);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        string fullOutputFolder = Path.GetFullPath(outputFolder);
        if (Directory.Exists(fullOutputFolder))
        {
            Directory.Delete(fullOutputFolder, true);
        }
        Directory.CreateDirectory(fullOutputFolder);

        string exePath = Path.Combine(outputFolder, "EcoQuest.exe");
        var buildOptions = new BuildPlayerOptions
        {
            scenes = enabledScenes,
            locationPathName = exePath,
            target = BuildTarget.StandaloneWindows64,
            options = options
        };

        BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
        BuildSummary summary = report.summary;
        float sizeMb = summary.totalSize / (1024f * 1024f);

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildTools] {summary.result} -> {exePath} | Errors={summary.totalErrors} | Warnings={summary.totalWarnings} | Size={sizeMb:F2} MB | Time={summary.totalTime.TotalSeconds:F1}s | Version={PlayerSettings.bundleVersion}");
        }
        else
        {
            Debug.LogError($"[BuildTools] Build failed ({summary.result}) -> {exePath} | Errors={summary.totalErrors} | Warnings={summary.totalWarnings}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
