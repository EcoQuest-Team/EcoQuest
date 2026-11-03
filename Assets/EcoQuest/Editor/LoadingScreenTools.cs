using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Editor tools for EcoQuest Loading Screen:
/// 1. Menu: EcoQuest > Build Loading Screen Prefab
/// 2. Menu: EcoQuest > Preview Loading Screen (Interactive EditorWindow with 0-100% slider)
/// 3. Menu: EcoQuest > Validate Loading Setup (Checks image import, tips >= 40, scenes, build settings)
/// </summary>
public class LoadingScreenTools : EditorWindow
{
    private float previewProgress = 0.5f;
    private LoadingScreenController activePreviewCtrl;
    private bool autoAnimate = false;
    private double lastUpdateTime = 0;

    [MenuItem("EcoQuest/Preview Loading Screen", false, 120)]
    public static void OpenPreviewWindow()
    {
        var window = GetWindow<LoadingScreenTools>("Loading Screen Preview");
        window.minSize = new Vector2(360f, 220f);
        window.Show();
    }

    [MenuItem("EcoQuest/Build Loading Screen Prefab", false, 121)]
    public static void BuildPrefab()
    {
        string prefabPath = "Assets/EcoQuest/UI/LoadingScreen/LoadingScreen.prefab";
        string resourcesPath = "Assets/Resources/LoadingScreen.prefab";

        GameObject rootGo = new GameObject("LoadingScreen", typeof(LoadingScreenController));
        LoadingScreenController ctrl = rootGo.GetComponent<LoadingScreenController>();
        ctrl.EnsureReferences();

        // Save main prefab
        PrefabUtility.SaveAsPrefabAsset(rootGo, prefabPath);
        Debug.Log($"[LoadingScreenTools] Successfully saved prefab to {prefabPath}");

        // Save to Resources/LoadingScreen.prefab for runtime on-demand instantiation
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }
        PrefabUtility.SaveAsPrefabAsset(rootGo, resourcesPath);
        Debug.Log($"[LoadingScreenTools] Successfully saved runtime prefab to {resourcesPath}");

        DestroyImmediate(rootGo);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("EcoQuest/Validate Loading Setup", false, 122)]
    public static bool ValidateLoadingSetup()
    {
        Debug.Log("==================================================");
        Debug.Log("        ECOQUEST LOADING SETUP VALIDATION        ");
        Debug.Log("==================================================");

        int passCount = 0;
        int totalChecks = 6;

        // Check 1: Background Image Import Settings
        string bgPath = "Assets/EcoQuest/UI/LoadingScreen/ForestLoadingBg.jpg";
        TextureImporter bgImp = AssetImporter.GetAtPath(bgPath) as TextureImporter;
        if (bgImp != null && bgImp.textureType == TextureImporterType.Sprite && bgImp.wrapMode == TextureWrapMode.Clamp && !bgImp.mipmapEnabled)
        {
            Debug.Log($"[PASS] Check 1: Background image correctly imported as Sprite (2D/UI), Clamp, No Mipmaps, MaxSize={bgImp.maxTextureSize}");
            passCount++;
        }
        else
        {
            Debug.LogError($"[FAIL] Check 1: Background image at {bgPath} missing or has invalid import settings!");
        }

        // Check 2: UI Sprites
        string[] uiSprites = new string[] {
            "LoadingBarFrame.png", "LoadingBarFill.png", "LeafHandle.png",
            "BottomDarkGradient.png", "FireflyGlow.png", "LeafParticle.png",
            "FogWisp.png", "MushroomGlow.png", "LightRay.png"
        };
        bool allSpritesOk = true;
        foreach (var s in uiSprites)
        {
            var imp = AssetImporter.GetAtPath($"Assets/EcoQuest/UI/LoadingScreen/{s}") as TextureImporter;
            if (imp == null || imp.textureType != TextureImporterType.Sprite)
            {
                allSpritesOk = false;
                Debug.LogError($"[FAIL] UI Sprite missing or not Sprite: {s}");
            }
        }
        if (allSpritesOk)
        {
            Debug.Log($"[PASS] Check 2: All {uiSprites.Length} UI and particle sprites valid and imported as 2D/UI Sprites.");
            passCount++;
        }

        // Check 3: LoadingTips Database (>= 40 tips across all 3 categories)
        string tipsPath = "Assets/EcoQuest/UI/LoadingScreen/EcoQuestLoadingTips.asset";
        LoadingTips tipsAsset = AssetDatabase.LoadAssetAtPath<LoadingTips>(tipsPath);
        if (tipsAsset != null && tipsAsset.tips != null && tipsAsset.tips.Count >= 40)
        {
            int gp = 0, ws = 0, ef = 0;
            foreach (var t in tipsAsset.tips)
            {
                if (t.category == LoadingTips.TipCategory.Gameplay) gp++;
                else if (t.category == LoadingTips.TipCategory.WasteSorting) ws++;
                else if (t.category == LoadingTips.TipCategory.EcoFacts5Rs) ef++;
            }

            if (gp >= 10 && ws >= 10 && ef >= 10)
            {
                Debug.Log($"[PASS] Check 3: Tips database verified ({tipsAsset.tips.Count} total tips: {gp} Gameplay, {ws} Waste Sorting, {ef} 5Rs & Eco Facts).");
                passCount++;
            }
            else
            {
                Debug.LogError($"[FAIL] Check 3: Tips categories uneven: GP={gp}, WS={ws}, EF={ef}");
            }
        }
        else
        {
            Debug.LogError($"[FAIL] Check 3: Tips database missing or has fewer than 40 tips (found {(tipsAsset?.tips?.Count ?? 0)})");
        }

        // Check 4: Build Settings
        var scenes = EditorBuildSettings.scenes;
        bool hasMainMenu = false, hasForest = false, hasCity = false;
        if (scenes != null && scenes.Length >= 3)
        {
            foreach (var s in scenes)
            {
                if (s.path.Contains("Main Menu")) hasMainMenu = true;
                if (s.path.Contains("Forest Scene")) hasForest = true;
                if (s.path.Contains("City Scene")) hasCity = true;
            }
        }
        if (hasMainMenu && hasForest && hasCity)
        {
            Debug.Log($"[PASS] Check 4: Build settings contain Main Menu, Forest Scene, and City Scene.");
            passCount++;
        }
        else
        {
            Debug.LogError($"[FAIL] Check 4: Build settings missing required scenes! (Menu={hasMainMenu}, Forest={hasForest}, City={hasCity})");
        }

        // Check 5: TMP Fonts
        TMP_FontAsset fredoka = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Fredoka SDF.asset");
        TMP_FontAsset nunito = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Nunito SDF.asset");
        if (fredoka != null && nunito != null)
        {
            Debug.Log($"[PASS] Check 5: Project TMP fonts resolved (Fredoka SDF and Nunito SDF).");
            passCount++;
        }
        else
        {
            Debug.LogError($"[FAIL] Check 5: Missing TMP font assets! (Fredoka={fredoka != null}, Nunito={nunito != null})");
        }

        // Check 6: Prefab existence and wiring
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/UI/LoadingScreen/LoadingScreen.prefab");
        if (prefab != null && prefab.GetComponent<LoadingScreenController>() != null)
        {
            Debug.Log($"[PASS] Check 6: LoadingScreen.prefab verified and ready.");
            passCount++;
        }
        else
        {
            Debug.LogError($"[FAIL] Check 6: LoadingScreen.prefab missing or lacks LoadingScreenController!");
        }

        Debug.Log($"==================================================");
        Debug.Log($"VALIDATION RESULT: {passCount}/{totalChecks} PASSED");
        Debug.Log($"==================================================");

        return passCount == totalChecks;
    }

    private void OnGUI()
    {
        GUILayout.Label("EcoQuest Loading Screen Preview", EditorStyles.boldLabel);
        EditorGUILayout.Space(6f);

        EditorGUI.BeginChangeCheck();
        previewProgress = EditorGUILayout.Slider("Progress (0-100%)", previewProgress, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            ApplyProgress(previewProgress);
        }

        EditorGUILayout.Space(6f);
        autoAnimate = EditorGUILayout.Toggle("Auto Animate (Simulate Load)", autoAnimate);

        EditorGUILayout.Space(8f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Show / Refresh Preview", GUILayout.Height(28f)))
        {
            ShowPreview();
        }
        if (GUILayout.Button("Next Tip", GUILayout.Height(28f)))
        {
            CycleTip();
        }
        if (GUILayout.Button("Hide Preview", GUILayout.Height(28f)))
        {
            HidePreview();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6f);
        if (GUILayout.Button("Run Setup Validation", GUILayout.Height(26f)))
        {
            ValidateLoadingSetup();
        }
    }

    private void Update()
    {
        if (autoAnimate)
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - lastUpdateTime);
            lastUpdateTime = now;

            previewProgress += dt * 0.25f;
            if (previewProgress > 1f) previewProgress = 0f;
            ApplyProgress(previewProgress);
            Repaint();
        }
        else
        {
            lastUpdateTime = EditorApplication.timeSinceStartup;
        }
    }

    private void ShowPreview()
    {
        EnsurePreviewInstance();
        if (activePreviewCtrl != null)
        {
            activePreviewCtrl.EditorPreviewSetProgress(previewProgress);
        }
    }

    private void ApplyProgress(float val)
    {
        EnsurePreviewInstance();
        if (activePreviewCtrl != null)
        {
            activePreviewCtrl.EditorPreviewSetProgress(val);
        }
    }

    private void CycleTip()
    {
        EnsurePreviewInstance();
        if (activePreviewCtrl != null)
        {
            activePreviewCtrl.EditorPreviewNextTip();
        }
    }

    private void HidePreview()
    {
        if (activePreviewCtrl != null)
        {
            activePreviewCtrl.EditorPreviewHide();
        }
    }

    private void EnsurePreviewInstance()
    {
        if (activePreviewCtrl == null)
        {
            activePreviewCtrl = Object.FindFirstObjectByType<LoadingScreenController>();
            if (activePreviewCtrl == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/UI/LoadingScreen/LoadingScreen.prefab");
                if (prefab != null)
                {
                    GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    activePreviewCtrl = go.GetComponent<LoadingScreenController>();
                }
            }
        }
    }
}
