using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public static class MixerBuilderHelper
{
    public static string BuildAllSettingsSystem()
    {
        // 1. Build MainMixer.mixer
        string mixerStatus = BuildMainMixer();

        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/EcoQuest/Audio/MainMixer.mixer");
        AudioMixerGroup masterGrp = null, musicGrp = null, sfxGrp = null;
        if (mixer != null)
        {
            var mg = mixer.FindMatchingGroups("Master");
            if (mg != null && mg.Length > 0) masterGrp = mg[0];
            var mug = mixer.FindMatchingGroups("Music");
            if (mug != null && mug.Length > 0) musicGrp = mug[0];
            var sg = mixer.FindMatchingGroups("SFX");
            if (sg != null && sg.Length > 0) sfxGrp = sg[0];
        }

        // 2. Build reusable SettingsPanel.prefab
        string prefabPath = "Assets/EcoQuest/Prefabs/SettingsPanel.prefab";
        if (!AssetDatabase.IsValidFolder("Assets/EcoQuest/Prefabs"))
            AssetDatabase.CreateFolder("Assets/EcoQuest", "Prefabs");

        GameObject tempSettingsRoot = CreateSettingsPanelHierarchy();
        GameObject settingsPrefab = PrefabUtility.SaveAsPrefabAsset(tempSettingsRoot, prefabPath);
        UnityEngine.Object.DestroyImmediate(tempSettingsRoot);

        // 3. Update SceneTransition.prefab hook AudioSources to use SFX group
        string stPrefabPath = "Assets/EcoQuest/Prefabs/SceneTransition.prefab";
        GameObject stPrefabContents = PrefabUtility.LoadPrefabContents(stPrefabPath);
        if (stPrefabContents != null)
        {
            foreach (var src in stPrefabContents.GetComponentsInChildren<AudioSource>(true))
            {
                if (sfxGrp != null) src.outputAudioMixerGroup = sfxGrp;
            }
            PrefabUtility.SaveAsPrefabAsset(stPrefabContents, stPrefabPath);
            PrefabUtility.UnloadPrefabContents(stPrefabContents);
        }

        // 4. Setup Main Menu.unity
        SetupMainMenuScene(mixer, masterGrp, musicGrp, sfxGrp, settingsPrefab);

        // 5. Setup Forest Scene.unity
        SetupForestScene(mixer, masterGrp, musicGrp, sfxGrp, settingsPrefab);

        // Return to Main Menu.unity
        EditorSceneManager.OpenScene("Assets/EcoQuest/Main Menu.unity", OpenSceneMode.Single);
        AssetDatabase.SaveAssets();

        return $"{mixerStatus} | Built SettingsPanel.prefab and wired Main Menu.unity & Forest Scene.unity.";
    }

    public static string BuildMainMixer()
    {
        string dir = "Assets/EcoQuest/Audio";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets/EcoQuest", "Audio");

        string mixerPath = "Assets/EcoQuest/Audio/MainMixer.mixer";
        AssetDatabase.DeleteAsset(mixerPath);

        Type mcType = Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor.CoreModule");
        Type gcType = Type.GetType("UnityEditor.Audio.AudioMixerGroupController, UnityEditor.CoreModule");
        Type scType = Type.GetType("UnityEditor.Audio.AudioMixerSnapshotController, UnityEditor.CoreModule");
        Type epType = Type.GetType("UnityEditor.Audio.ExposedAudioParameter, UnityEditor.CoreModule");

        var createMethod = mcType.GetMethod("CreateMixerControllerAtPath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var mixer = createMethod.Invoke(null, new object[] { mixerPath }) as AudioMixer;

        var masterGroup = mcType.GetProperty("masterGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(mixer);
        var createNewGroup = mcType.GetMethod("CreateNewGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var addChild = mcType.GetMethod("AddChildToParent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        var musicGroup = createNewGroup.Invoke(mixer, new object[] { "Music", true });
        var sfxGroup = createNewGroup.Invoke(mixer, new object[] { "SFX", true });

        addChild.Invoke(mixer, new object[] { musicGroup, masterGroup });
        addChild.Invoke(mixer, new object[] { sfxGroup, masterGroup });

        var getGuidForVol = gcType.GetMethod("GetGUIDForVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object masterGuid = getGuidForVol.Invoke(masterGroup, null);
        object musicGuid = getGuidForVol.Invoke(musicGroup, null);
        object sfxGuid = getGuidForVol.Invoke(sfxGroup, null);

        var targetSnapshot = mcType.GetProperty("TargetSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(mixer);
        foreach (var m in scType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.Name == "SetValue" && m.GetParameters().Length == 2)
            {
                m.Invoke(targetSnapshot, new object[] { masterGuid, 0f });
                m.Invoke(targetSnapshot, new object[] { musicGuid, 0f });
                m.Invoke(targetSnapshot, new object[] { sfxGuid, 0f });
                break;
            }
        }

        Array exposedArr = Array.CreateInstance(epType, 3);
        string[] names = new[] { "MasterVol", "MusicVol", "SFXVol" };
        object[] guids = new[] { masterGuid, musicGuid, sfxGuid };

        var guidField = epType.GetField("guid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var nameField = epType.GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        for (int i = 0; i < 3; i++)
        {
            object elem = Activator.CreateInstance(epType);
            guidField.SetValue(elem, guids[i]);
            nameField.SetValue(elem, names[i]);
            exposedArr.SetValue(elem, i);
        }

        mcType.GetProperty("exposedParameters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(mixer, exposedArr);
        mcType.GetMethod("OnChangedExposedParameter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(mixer, null);
        mcType.GetMethod("OnSubAssetChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(mixer, null);

        EditorUtility.SetDirty(mixer);
        if (targetSnapshot is UnityEngine.Object snapObj) EditorUtility.SetDirty(snapObj);
        AssetDatabase.SaveAssets();

        return "MainMixer.mixer built";
    }

    private static void SetupMainMenuScene(AudioMixer mixer, AudioMixerGroup masterGrp, AudioMixerGroup musicGrp, AudioMixerGroup sfxGrp, GameObject settingsPrefab)
    {
        var scene = EditorSceneManager.OpenScene("Assets/EcoQuest/Main Menu.unity", OpenSceneMode.Single);

        // 1. Ensure AudioManager and SettingsManager exist in Main Menu
        var audioMgr = UnityEngine.Object.FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include);
        if (audioMgr == null)
        {
            GameObject go = new GameObject("AudioManager");
            audioMgr = go.AddComponent<AudioManager>();
        }
        audioMgr.mainMixer = mixer;
        audioMgr.masterGroup = masterGrp;
        audioMgr.musicGroup = musicGrp;
        audioMgr.sfxGroup = sfxGrp;
        audioMgr.EnsureMixerAndSources();
        EditorUtility.SetDirty(audioMgr);

        var settingsMgr = UnityEngine.Object.FindFirstObjectByType<SettingsManager>(FindObjectsInactive.Include);
        if (settingsMgr == null)
        {
            GameObject go = new GameObject("SettingsManager");
            settingsMgr = go.AddComponent<SettingsManager>();
        }
        EditorUtility.SetDirty(settingsMgr);

        // 2. Route existing hook AudioSources (StartSoundHook, WhooshSoundHook, ButtonHoverFX) to SFX group
        var mainMenuCanvas = GameObject.Find("Main Menu");
        if (mainMenuCanvas != null)
        {
            foreach (var hoverFx in mainMenuCanvas.GetComponentsInChildren<ButtonHoverFX>(true))
            {
                if (hoverFx.audioSource == null)
                {
                    AudioSource btnSrc = hoverFx.GetComponent<AudioSource>();
                    if (btnSrc == null) btnSrc = hoverFx.gameObject.AddComponent<AudioSource>();
                    btnSrc.playOnAwake = false;
                    btnSrc.loop = false;
                    btnSrc.spatialBlend = 0f;
                    hoverFx.audioSource = btnSrc;
                }
                if (sfxGrp != null) hoverFx.audioSource.outputAudioMixerGroup = sfxGrp;
                EditorUtility.SetDirty(hoverFx);
                EditorUtility.SetDirty(hoverFx.audioSource);
            }
        }

        foreach (var src in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (src != null && src != audioMgr.musicSource && sfxGrp != null)
            {
                src.outputAudioMixerGroup = sfxGrp;
                EditorUtility.SetDirty(src);
            }
        }

        // 3. Instantiate SettingsPanel under Main Menu Canvas (or root) and wire MainMenuManager.settingsPanel
        var existingSettings = GameObject.Find("SettingsPanel");
        if (existingSettings != null) UnityEngine.Object.DestroyImmediate(existingSettings);
        if (mainMenuCanvas != null)
        {
            var oldChild = mainMenuCanvas.transform.Find("SettingsPanel");
            if (oldChild != null) UnityEngine.Object.DestroyImmediate(oldChild.gameObject);
        }

        GameObject settingsInstance = (GameObject)PrefabUtility.InstantiatePrefab(settingsPrefab);
        settingsInstance.name = "SettingsPanel";
        if (mainMenuCanvas != null)
        {
            settingsInstance.transform.SetParent(mainMenuCanvas.transform, false);
        }
        RectTransform rt = settingsInstance.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }
        settingsInstance.SetActive(false);

        var menuMgr = UnityEngine.Object.FindFirstObjectByType<MainMenuManager>(FindObjectsInactive.Include);
        if (menuMgr != null)
        {
            var so = new SerializedObject(menuMgr);
            var sp = so.FindProperty("settingsPanel");
            if (sp != null)
            {
                sp.objectReferenceValue = settingsInstance;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(menuMgr);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetupForestScene(AudioMixer mixer, AudioMixerGroup masterGrp, AudioMixerGroup musicGrp, AudioMixerGroup sfxGrp, GameObject settingsPrefab)
    {
        var scene = EditorSceneManager.OpenScene("Assets/EcoQuest/Forest Scene.unity", OpenSceneMode.Single);

        GameObject managersRoot = GameObject.Find("--- MANAGERS ---");
        GameObject uiRoot = GameObject.Find("--- UI ---");

        // 1. Route FirstPersonController footstep & landing sounds through SFX group
        var fpc = UnityEngine.Object.FindFirstObjectByType<StarterAssets.FirstPersonController>(FindObjectsInactive.Include);
        if (fpc != null)
        {
            AudioSource footstepSrc = fpc.GetComponent<AudioSource>();
            if (footstepSrc == null) footstepSrc = fpc.gameObject.AddComponent<AudioSource>();
            footstepSrc.playOnAwake = false;
            footstepSrc.loop = false;
            footstepSrc.spatialBlend = 0f;
            if (sfxGrp != null) footstepSrc.outputAudioMixerGroup = sfxGrp;
            fpc.FootstepAudioSource = footstepSrc;
            EditorUtility.SetDirty(footstepSrc);
            EditorUtility.SetDirty(fpc);
        }

        // 2. Build UI_PauseMenu under --- UI ---
        if (uiRoot != null)
        {
            var oldPause = uiRoot.transform.Find("UI_PauseMenu");
            if (oldPause != null) UnityEngine.Object.DestroyImmediate(oldPause.gameObject);
        }
        var strayPause = GameObject.Find("UI_PauseMenu");
        if (strayPause != null) UnityEngine.Object.DestroyImmediate(strayPause);

        GameObject pauseCanvasGo = CreatePauseMenuHierarchy(uiRoot != null ? uiRoot.transform : null, settingsPrefab);
        EditorUtility.SetDirty(pauseCanvasGo);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static GameObject CreateSettingsPanelHierarchy()
    {
        Sprite panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/info_card_clean.png");
        Sprite popupSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/dialogue_panel_no_button.png");
        Sprite btnSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/accept_button.png");
        Sprite barBgSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/progress_bar_bg.png");
        Sprite barFillSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/progress_bar_fill_white.png");
        Sprite checkSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/checkmark_icon.png");
        Sprite leafSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/icon_info_leaf.png");

        TMP_FontAsset fredoka = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Fredoka SDF.asset");
        TMP_FontAsset nunito = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Nunito SDF.asset");
        if (nunito == null) nunito = fredoka;

        // Root: SettingsPanel (has Canvas with overrideSorting = 100 so it renders above HUD/Quiz 45 and below SceneTransition 2000)
        GameObject root = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(SettingsPanelUI));
        RectTransform rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        CanvasGroup rootCg = root.GetComponent<CanvasGroup>();
        rootCg.alpha = 1f;
        rootCg.interactable = true;
        rootCg.blocksRaycasts = true;

        SettingsPanelUI ui = root.GetComponent<SettingsPanelUI>();
        ui.rootCanvasGroup = rootCg;

        // 1. DarkOverlay (blocks clicks on underlying menu/HUD buttons)
        GameObject darkOverlay = CreateUIObject("DarkOverlay", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image darkImg = darkOverlay.AddComponent<Image>();
        darkImg.color = new Color(0f, 0f, 0f, 0.76f);
        darkImg.raycastTarget = true;

        // 2. ModalFrame (1500 x 680 wooden frame)
        GameObject modalFrame = CreateUIObject("ModalFrame", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 680f));
        CanvasGroup modalCg = modalFrame.AddComponent<CanvasGroup>();
        ui.modalFrameRect = modalFrame.GetComponent<RectTransform>();
        ui.modalFrameCanvasGroup = modalCg;

        // 2a. Parchment backing behind the left photo-window cutout of info_card_clean.png
        GameObject leftBacking = CreateUIObject("LeftCutoutParchmentFill", modalFrame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-407f, 7f), new Vector2(474f, 440f));
        Image leftBackingImg = leftBacking.AddComponent<Image>();
        leftBackingImg.color = new Color(0.929f, 0.890f, 0.816f, 1f); // #EDE3D0 warm parchment
        leftBackingImg.raycastTarget = false;

        // 2b. Wooden Frame Sprite (info_card_clean.png)
        GameObject woodenBg = CreateUIObject("WoodenFrameBG", modalFrame.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image woodenImg = woodenBg.AddComponent<Image>();
        woodenImg.sprite = panelSprite;
        woodenImg.type = panelSprite != null && panelSprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        woodenImg.color = Color.white;
        woodenImg.raycastTarget = true;

        // 2c. Left Box: Read-Only Controls Guide inside the wooden picture frame (-407, 7)
        GameObject controlsContainer = CreateUIObject("ControlsGuideBox", modalFrame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-407f, 7f), new Vector2(430f, 396f));
        CreateTMP("ControlsTitle", controlsContainer.transform, fredoka, "CONTROLS GUIDE", 28f, new Color(0.18f, 0.42f, 0.16f, 1f), TextAlignmentOptions.Center, new Vector2(0f, 162f), new Vector2(400f, 40f), true);
        CreateTMP("ControlsSubtitle", controlsContainer.transform, nunito, "Keyboard & Mouse (Read-Only)", 18f, new Color(0.38f, 0.27f, 0.16f, 1f), TextAlignmentOptions.Center, new Vector2(0f, 130f), new Vector2(400f, 26f), false);

        string[,] controlsList = new string[,]
        {
            { "WASD", "Move Around" },
            { "SPACE", "Jump" },
            { "SHIFT", "Sprint Faster" },
            { "E", "Talk to Guardian" },
            { "CTRL", "Unlock Mouse Cursor" },
            { "TAB", "Toggle Quest Panel" },
            { "ESC", "Pause / Settings" }
        };

        float startY = 88f;
        float rowStep = 41f;
        for (int i = 0; i < controlsList.GetLength(0); i++)
        {
            float y = startY - i * rowStep;
            GameObject row = CreateUIObject($"ControlRow_{controlsList[i, 0]}", controlsContainer.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(400f, 35f));
            Image rowBg = row.AddComponent<Image>();
            rowBg.color = (i % 2 == 0) ? new Color(0.85f, 0.79f, 0.69f, 0.55f) : new Color(0.89f, 0.84f, 0.75f, 0.35f);
            rowBg.raycastTarget = false;

            GameObject keyBadge = CreateUIObject("KeyBadge", row.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(68f, 0f), new Vector2(112f, 29f));
            Image badgeImg = keyBadge.AddComponent<Image>();
            badgeImg.sprite = btnSprite;
            badgeImg.type = btnSprite != null && btnSprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            badgeImg.color = new Color(0.38f, 0.26f, 0.15f, 1f);
            badgeImg.raycastTarget = false;

            CreateTMP("KeyText", keyBadge.transform, fredoka, controlsList[i, 0], 19f, Color.white, TextAlignmentOptions.Center, Vector2.zero, new Vector2(104f, 27f), true);
            CreateTMP("ActionText", row.transform, nunito, controlsList[i, 1], 21f, new Color(0.20f, 0.14f, 0.08f, 1f), TextAlignmentOptions.MidlineLeft, new Vector2(65f, 0f), new Vector2(245f, 32f), true);
        }

        // 2d. Right Area Header: Leaf + "GAME SETTINGS" + Leaf
        GameObject header = CreateUIObject("Header", modalFrame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(255f, 255f), new Vector2(880f, 54f));
        CreateTMP("HeaderTitle", header.transform, fredoka, "GAME SETTINGS", 36f, new Color(0.20f, 0.14f, 0.08f, 1f), TextAlignmentOptions.Center, Vector2.zero, new Vector2(420f, 50f), true);

        if (leafSprite != null)
        {
            GameObject leafL = CreateUIObject("LeafIconLeft", header.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-195f, 2f), new Vector2(38f, 38f));
            Image leafLImg = leafL.AddComponent<Image>();
            leafLImg.sprite = leafSprite;
            leafLImg.raycastTarget = false;

            GameObject leafR = CreateUIObject("LeafIconRight", header.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(195f, 2f), new Vector2(38f, 38f));
            Image leafRImg = leafR.AddComponent<Image>();
            leafRImg.sprite = leafSprite;
            leafRImg.raycastTarget = false;
        }

        // 2e. Left Settings Column (x = 45): AUDIO & GAMEPLAY
        GameObject colLeft = CreateUIObject("AudioAndGameplayColumn", modalFrame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(45f, 10f), new Vector2(430f, 420f));

        CreateSectionHeader("AudioHeader", colLeft.transform, fredoka, "AUDIO", new Vector2(0f, 192f), 420f);
        ui.masterSlider = CreateLabeledSlider("MasterVolumeRow", colLeft.transform, fredoka, nunito, barBgSprite, barFillSprite, btnSprite, "Master Volume", new Vector2(0f, 148f), 0f, 1f, 1f, out ui.masterPercentText);
        ui.musicSlider = CreateLabeledSlider("MusicVolumeRow", colLeft.transform, fredoka, nunito, barBgSprite, barFillSprite, btnSprite, "Music Volume", new Vector2(0f, 104f), 0f, 1f, 0.8f, out ui.musicPercentText);
        ui.sfxSlider = CreateLabeledSlider("SFXVolumeRow", colLeft.transform, fredoka, nunito, barBgSprite, barFillSprite, btnSprite, "SFX Volume", new Vector2(0f, 60f), 0f, 1f, 0.8f, out ui.sfxPercentText);
        ui.muteAllToggle = CreateLabeledToggle("MuteAllRow", colLeft.transform, nunito, btnSprite, checkSprite, "Mute All Audio", new Vector2(0f, 16f), false);

        CreateSectionHeader("GameplayHeader", colLeft.transform, fredoka, "GAMEPLAY", new Vector2(0f, -34f), 420f);
        ui.sensitivitySlider = CreateLabeledSlider("SensitivityRow", colLeft.transform, fredoka, nunito, barBgSprite, barFillSprite, btnSprite, "Mouse Speed", new Vector2(0f, -78f), 0.2f, 3.0f, 1.0f, out ui.sensitivityValueText);
        ui.sensitivityValueText.text = "1.0x";
        ui.textSizeDropdown = CreateLabeledDropdown("TextSizeRow", colLeft.transform, fredoka, nunito, btnSprite, checkSprite, "Quiz/Chat Text", new Vector2(0f, -132f), new List<string> { "Small", "Medium", "Large" });

        // 2f. Right Settings Column (x = 485): GRAPHICS
        GameObject colRight = CreateUIObject("GraphicsColumn", modalFrame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(485f, 10f), new Vector2(420f, 420f));

        CreateSectionHeader("GraphicsHeader", colRight.transform, fredoka, "GRAPHICS", new Vector2(0f, 192f), 410f);
        ui.qualityDropdown = CreateLabeledDropdown("QualityRow", colRight.transform, fredoka, nunito, btnSprite, checkSprite, "Quality Preset", new Vector2(0f, 142f), new List<string>(QualitySettings.names));
        ui.resolutionDropdown = CreateLabeledDropdown("ResolutionRow", colRight.transform, fredoka, nunito, btnSprite, checkSprite, "Resolution", new Vector2(0f, 88f), new List<string> { "Auto (Fit Screen)" });
        ui.fpsLimitDropdown = CreateLabeledDropdown("FPSLimitRow", colRight.transform, fredoka, nunito, btnSprite, checkSprite, "FPS Limit", new Vector2(0f, 34f), new List<string> { "30 FPS", "60 FPS", "120 FPS", "Unlimited" });
        ui.fullscreenToggle = CreateLabeledToggle("FullscreenRow", colRight.transform, nunito, btnSprite, checkSprite, "Fullscreen Mode", new Vector2(0f, -20f), true);
        ui.vsyncToggle = CreateLabeledToggle("VSyncRow", colRight.transform, nunito, btnSprite, checkSprite, "VSync", new Vector2(0f, -66f), true);

        CreateTMP("GraphicsNote", colRight.transform, nunito, "Press 'Apply Graphics' below to apply display changes.", 18f, new Color(0.38f, 0.27f, 0.16f, 1f), TextAlignmentOptions.Center, new Vector2(0f, -125f), new Vector2(390f, 48f), false);

        // 2g. Bottom Action Buttons Bar (Apply Graphics, Reset to Defaults, Back)
        GameObject bottomBar = CreateUIObject("BottomButtonsBar", modalFrame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(265f, -238f), new Vector2(860f, 58f));
        ui.applyGraphicsButton = CreateStyledButton("ApplyButton", bottomBar.transform, fredoka, btnSprite, "Apply Graphics", new Vector2(-280f, 0f), new Vector2(250f, 52f), new Color(0.24f, 0.68f, 0.32f, 1f));
        ui.resetDefaultsButton = CreateStyledButton("ResetDefaultsButton", bottomBar.transform, fredoka, btnSprite, "Reset to Defaults", new Vector2(10f, 0f), new Vector2(260f, 52f), new Color(0.78f, 0.49f, 0.18f, 1f));
        ui.backButton = CreateStyledButton("BackButton", bottomBar.transform, fredoka, btnSprite, "Back", new Vector2(285f, 0f), new Vector2(210f, 52f), new Color(0.45f, 0.31f, 0.19f, 1f));

        // 3. Reset to Defaults Confirmation Popup
        GameObject confirmOverlay = CreateUIObject("ConfirmResetOverlay", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image confirmBg = confirmOverlay.AddComponent<Image>();
        confirmBg.color = new Color(0f, 0f, 0f, 0.68f);
        confirmBg.raycastTarget = true;

        GameObject confirmCard = CreateUIObject("ConfirmResetCard", confirmOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680f, 310f));
        Image cardImg = confirmCard.AddComponent<Image>();
        cardImg.sprite = popupSprite != null ? popupSprite : panelSprite;
        cardImg.type = Image.Type.Simple;
        cardImg.color = Color.white;

        CreateTMP("ConfirmTitle", confirmCard.transform, fredoka, "Reset to Defaults?", 32f, new Color(0.20f, 0.14f, 0.08f, 1f), TextAlignmentOptions.Center, new Vector2(0f, 65f), new Vector2(580f, 44f), true);
        CreateTMP("ConfirmBody", confirmCard.transform, nunito, "Restore all Audio, Graphics, and Gameplay settings back to their default values?", 22f, new Color(0.28f, 0.20f, 0.12f, 1f), TextAlignmentOptions.Center, new Vector2(0f, 4f), new Vector2(560f, 68f), false);

        ui.confirmResetYesButton = CreateStyledButton("ConfirmYesButton", confirmCard.transform, fredoka, btnSprite, "Yes, Reset", new Vector2(-135f, -78f), new Vector2(220f, 50f), new Color(0.24f, 0.68f, 0.32f, 1f));
        ui.confirmResetNoButton = CreateStyledButton("ConfirmCancelButton", confirmCard.transform, fredoka, btnSprite, "Cancel", new Vector2(135f, -78f), new Vector2(220f, 50f), new Color(0.45f, 0.31f, 0.19f, 1f));

        confirmOverlay.SetActive(false);
        ui.confirmResetOverlay = confirmOverlay;

        return root;
    }

    private static GameObject CreatePauseMenuHierarchy(Transform parent, GameObject settingsPrefab)
    {
        Sprite panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/dialogue_panel_no_button.png");
        Sprite fallbackSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/info_card_clean.png");
        Sprite btnSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/accept_button.png");
        Sprite leafSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/icon_info_leaf.png");

        TMP_FontAsset fredoka = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Fredoka SDF.asset");
        TMP_FontAsset nunito = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Nunito SDF.asset");
        if (nunito == null) nunito = fredoka;

        GameObject pauseRoot = new GameObject("UI_PauseMenu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PauseMenuController));
        if (parent != null) pauseRoot.transform.SetParent(parent, false);

        Canvas c = pauseRoot.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 95; // Above HUD & Quiz (45), below SettingsPanel (100) and SceneTransition (2000)

        CanvasScaler cs = pauseRoot.GetComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        cs.matchWidthOrHeight = 0.5f;

        PauseMenuController ctrl = pauseRoot.GetComponent<PauseMenuController>();

        // 1. PauseOverlayRoot
        GameObject overlayRoot = CreateUIObject("PauseOverlayRoot", pauseRoot.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        CanvasGroup overlayCg = overlayRoot.AddComponent<CanvasGroup>();
        Image darkImg = overlayRoot.AddComponent<Image>();
        darkImg.color = new Color(0f, 0f, 0f, 0.72f);
        darkImg.raycastTarget = true;

        ctrl.pauseOverlayRoot = overlayRoot;
        ctrl.pauseCanvasGroup = overlayCg;

        // 2. PauseCard (Wooden Frame)
        GameObject pauseCard = CreateUIObject("PauseCard", overlayRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680f, 460f));
        Image cardImg = pauseCard.AddComponent<Image>();
        cardImg.sprite = panelSprite != null ? panelSprite : fallbackSprite;
        cardImg.type = Image.Type.Simple;
        cardImg.color = Color.white;
        ctrl.pauseCardRect = pauseCard.GetComponent<RectTransform>();

        CreateTMP("PauseTitle", pauseCard.transform, fredoka, "GAME PAUSED", 40f, new Color(0.20f, 0.14f, 0.08f, 1f), TextAlignmentOptions.Center, new Vector2(0f, 130f), new Vector2(460f, 54f), true);
        if (leafSprite != null)
        {
            GameObject leafL = CreateUIObject("LeafL", pauseCard.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-190f, 132f), new Vector2(38f, 38f));
            leafL.AddComponent<Image>().sprite = leafSprite;
            GameObject leafR = CreateUIObject("LeafR", pauseCard.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(190f, 132f), new Vector2(38f, 38f));
            leafR.AddComponent<Image>().sprite = leafSprite;
        }

        ctrl.resumeButton = CreateStyledButton("ResumeButton", pauseCard.transform, fredoka, btnSprite, "Resume Game", new Vector2(0f, 42f), new Vector2(340f, 58f), new Color(0.24f, 0.68f, 0.32f, 1f));
        ctrl.settingsButton = CreateStyledButton("SettingsButton", pauseCard.transform, fredoka, btnSprite, "Settings", new Vector2(0f, -32f), new Vector2(340f, 58f), new Color(0.78f, 0.49f, 0.18f, 1f));
        ctrl.returnToMainMenuButton = CreateStyledButton("ReturnToMainMenuButton", pauseCard.transform, fredoka, btnSprite, "Return to Main Menu", new Vector2(0f, -106f), new Vector2(340f, 58f), new Color(0.45f, 0.31f, 0.19f, 1f));

        // 3. Return to Main Menu Confirmation Popup
        GameObject confirmMenuOverlay = CreateUIObject("ConfirmMainMenuOverlay", pauseRoot.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image confirmBg = confirmMenuOverlay.AddComponent<Image>();
        confirmBg.color = new Color(0f, 0f, 0f, 0.72f);
        confirmBg.raycastTarget = true;

        GameObject confirmCard = CreateUIObject("ConfirmMainMenuCard", confirmMenuOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680f, 310f));
        Image confirmCardImg = confirmCard.AddComponent<Image>();
        confirmCardImg.sprite = panelSprite != null ? panelSprite : fallbackSprite;
        confirmCardImg.type = Image.Type.Simple;
        confirmCardImg.color = Color.white;

        CreateTMP("ConfirmTitle", confirmCard.transform, fredoka, "Return to Main Menu?", 32f, new Color(0.20f, 0.14f, 0.08f, 1f), TextAlignmentOptions.Center, new Vector2(0f, 65f), new Vector2(560f, 44f), true);
        CreateTMP("ConfirmBody", confirmCard.transform, nunito, "Are you sure you want to leave the forest and return to the Main Menu?", 22f, new Color(0.28f, 0.20f, 0.12f, 1f), TextAlignmentOptions.Center, new Vector2(0f, 4f), new Vector2(540f, 68f), false);

        ctrl.confirmMainMenuYesButton = CreateStyledButton("ConfirmMainMenuYesButton", confirmCard.transform, fredoka, btnSprite, "Yes, Main Menu", new Vector2(-140f, -78f), new Vector2(235f, 52f), new Color(0.24f, 0.68f, 0.32f, 1f));
        ctrl.confirmMainMenuNoButton = CreateStyledButton("ConfirmMainMenuNoButton", confirmCard.transform, fredoka, btnSprite, "Cancel", new Vector2(140f, -78f), new Vector2(215f, 52f), new Color(0.45f, 0.31f, 0.19f, 1f));

        confirmMenuOverlay.SetActive(false);
        ctrl.confirmMainMenuOverlay = confirmMenuOverlay;

        // 4. Instantiate reusable SettingsPanel prefab inside UI_PauseMenu
        if (settingsPrefab != null)
        {
            GameObject settingsInst = (GameObject)PrefabUtility.InstantiatePrefab(settingsPrefab);
            settingsInst.name = "SettingsPanel";
            settingsInst.transform.SetParent(pauseRoot.transform, false);
            RectTransform srt = settingsInst.GetComponent<RectTransform>();
            if (srt != null)
            {
                srt.anchorMin = Vector2.zero;
                srt.anchorMax = Vector2.one;
                srt.offsetMin = Vector2.zero;
                srt.offsetMax = Vector2.zero;
                srt.localScale = Vector3.one;
            }
            settingsInst.SetActive(false);
            ctrl.settingsPanelObject = settingsInst;
            ctrl.settingsPanelUI = settingsInst.GetComponent<SettingsPanelUI>();
        }

        overlayRoot.SetActive(false);
        return pauseRoot;
    }

    // =====================================================================
    // UI BUILDER HELPERS
    // =====================================================================

    private static GameObject CreateUIObject(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        rt.localScale = Vector3.one;
        return go;
    }

    private static TextMeshProUGUI CreateTMP(string name, Transform parent, TMP_FontAsset font, string text, float fontSize, Color color, TextAlignmentOptions align, Vector2 pos, Vector2 size, bool bold)
    {
        GameObject go = CreateUIObject(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void CreateSectionHeader(string name, Transform parent, TMP_FontAsset font, string title, Vector2 pos, float width)
    {
        GameObject headerGo = CreateUIObject(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(width, 32f));
        CreateTMP("Label", headerGo.transform, font, title, 23f, new Color(0.18f, 0.42f, 0.16f, 1f), TextAlignmentOptions.MidlineLeft, Vector2.zero, new Vector2(width, 30f), true);
        GameObject line = CreateUIObject("Underline", headerGo.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 1f), new Vector2(width, 2f));
        Image lineImg = line.AddComponent<Image>();
        lineImg.color = new Color(0.38f, 0.27f, 0.16f, 0.35f);
        lineImg.raycastTarget = false;
    }

    private static Slider CreateLabeledSlider(string name, Transform parent, TMP_FontAsset fredoka, TMP_FontAsset nunito, Sprite bgSprite, Sprite fillSprite, Sprite knobSprite, string label, Vector2 pos, float min, float max, float val, out TextMeshProUGUI valueText)
    {
        GameObject row = CreateUIObject(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(420f, 38f));
        CreateTMP("Label", row.transform, nunito, label, 21f, new Color(0.20f, 0.14f, 0.08f, 1f), TextAlignmentOptions.MidlineLeft, new Vector2(-130f, 0f), new Vector2(155f, 34f), true);

        GameObject sliderGo = CreateUIObject("Slider", row.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(32f, 0f), new Vector2(175f, 24f));
        Slider slider = sliderGo.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = false;

        GameObject bg = CreateUIObject("Background", sliderGo.transform, new Vector2(0f, 0.2f), new Vector2(1f, 0.8f), Vector2.zero, Vector2.zero);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.sprite = bgSprite;
        bgImg.type = bgSprite != null && bgSprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        bgImg.color = new Color(0.32f, 0.23f, 0.15f, 1f);

        GameObject fillArea = CreateUIObject("Fill Area", sliderGo.transform, new Vector2(0f, 0.2f), new Vector2(1f, 0.8f), Vector2.zero, new Vector2(-10f, 0f));
        GameObject fill = CreateUIObject("Fill", fillArea.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.sprite = fillSprite;
        fillImg.type = fillSprite != null && fillSprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        fillImg.color = new Color(0.24f, 0.68f, 0.32f, 1f);

        GameObject handleArea = CreateUIObject("Handle Slide Area", sliderGo.transform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-16f, 0f));
        GameObject handle = CreateUIObject("Handle", handleArea.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 28f));
        Image handleImg = handle.AddComponent<Image>();
        handleImg.sprite = knobSprite;
        handleImg.color = new Color(0.96f, 0.82f, 0.32f, 1f);

        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.value = val;

        valueText = CreateTMP("ValueText", row.transform, fredoka, $"{Mathf.RoundToInt(val * 100f)}%", 20f, new Color(0.24f, 0.16f, 0.09f, 1f), TextAlignmentOptions.MidlineRight, new Vector2(168f, 0f), new Vector2(78f, 34f), true);
        return slider;
    }

    private static Toggle CreateLabeledToggle(string name, Transform parent, TMP_FontAsset font, Sprite boxSprite, Sprite checkSprite, string label, Vector2 pos, bool defaultOn)
    {
        GameObject row = CreateUIObject(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(420f, 38f));
        CreateTMP("Label", row.transform, font, label, 21f, new Color(0.20f, 0.14f, 0.08f, 1f), TextAlignmentOptions.MidlineLeft, new Vector2(-80f, 0f), new Vector2(250f, 34f), true);

        GameObject toggleGo = CreateUIObject("Toggle", row.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(120f, 0f), new Vector2(42f, 36f));
        Toggle toggle = toggleGo.AddComponent<Toggle>();

        GameObject bg = CreateUIObject("Background", toggleGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.sprite = boxSprite;
        bgImg.type = boxSprite != null && boxSprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        bgImg.color = new Color(0.38f, 0.27f, 0.16f, 1f);

        GameObject check = CreateUIObject("Checkmark", bg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f));
        Image checkImg = check.AddComponent<Image>();
        checkImg.sprite = checkSprite;
        checkImg.color = new Color(0.30f, 0.92f, 0.40f, 1f);

        toggle.targetGraphic = bgImg;
        toggle.graphic = checkImg;
        toggle.isOn = defaultOn;

        toggleGo.AddComponent<ButtonHoverFX>();
        return toggle;
    }

    private static TMP_Dropdown CreateLabeledDropdown(string name, Transform parent, TMP_FontAsset fredoka, TMP_FontAsset nunito, Sprite btnSprite, Sprite checkSprite, string label, Vector2 pos, List<string> options)
    {
        GameObject row = CreateUIObject(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(415f, 44f));
        CreateTMP("Label", row.transform, nunito, label, 21f, new Color(0.20f, 0.14f, 0.08f, 1f), TextAlignmentOptions.MidlineLeft, new Vector2(-120f, 0f), new Vector2(165f, 38f), true);

        GameObject ddGo = CreateUIObject("Dropdown", row.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(88f, 0f), new Vector2(230f, 38f));
        Image ddImg = ddGo.AddComponent<Image>();
        ddImg.sprite = btnSprite;
        ddImg.type = btnSprite != null && btnSprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        ddImg.color = new Color(0.42f, 0.30f, 0.18f, 1f);

        TMP_Dropdown dd = ddGo.AddComponent<TMP_Dropdown>();
        dd.targetGraphic = ddImg;

        TextMeshProUGUI caption = CreateTMP("Label", ddGo.transform, fredoka, options.Count > 0 ? options[0] : "", 19f, Color.white, TextAlignmentOptions.MidlineLeft, new Vector2(-10f, 0f), new Vector2(180f, 32f), false);
        CreateTMP("Arrow", ddGo.transform, fredoka, "v", 18f, new Color(0.96f, 0.85f, 0.45f, 1f), TextAlignmentOptions.Center, new Vector2(92f, 0f), new Vector2(26f, 26f), true);

        // Template for TMP_Dropdown
        GameObject template = CreateUIObject("Template", ddGo.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, -2f), new Vector2(0f, 180f));
        template.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
        Image tmplImg = template.AddComponent<Image>();
        tmplImg.color = new Color(0.28f, 0.20f, 0.12f, 0.98f);
        ScrollRect sr = template.AddComponent<ScrollRect>();
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;

        GameObject viewport = CreateUIObject("Viewport", template.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image vpImg = viewport.AddComponent<Image>();
        vpImg.color = Color.white;
        Mask vpMask = viewport.AddComponent<Mask>();
        vpMask.showMaskGraphic = false;

        GameObject content = CreateUIObject("Content", viewport.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 36f));
        content.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);

        GameObject item = CreateUIObject("Item", content.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(0f, 34f));
        Toggle itemToggle = item.AddComponent<Toggle>();

        GameObject itemBg = CreateUIObject("Item Background", item.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image itemBgImg = itemBg.AddComponent<Image>();
        itemBgImg.color = new Color(0.36f, 0.25f, 0.15f, 1f);

        GameObject itemCheck = CreateUIObject("Item Checkmark", item.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(20f, 20f));
        Image itemCheckImg = itemCheck.AddComponent<Image>();
        itemCheckImg.sprite = checkSprite;
        itemCheckImg.color = new Color(0.30f, 0.92f, 0.40f, 1f);

        TextMeshProUGUI itemLabel = CreateTMP("Item Label", item.transform, fredoka, "Option", 18f, Color.white, TextAlignmentOptions.MidlineLeft, new Vector2(12f, 0f), new Vector2(180f, 30f), false);
        RectTransform itemLabelRt = itemLabel.GetComponent<RectTransform>();
        itemLabelRt.anchorMin = Vector2.zero;
        itemLabelRt.anchorMax = Vector2.one;
        itemLabelRt.offsetMin = new Vector2(34f, 2f);
        itemLabelRt.offsetMax = new Vector2(-8f, -2f);

        itemToggle.targetGraphic = itemBgImg;
        itemToggle.graphic = itemCheckImg;
        itemToggle.isOn = true;

        sr.content = content.GetComponent<RectTransform>();
        sr.viewport = viewport.GetComponent<RectTransform>();

        dd.template = template.GetComponent<RectTransform>();
        dd.captionText = caption;
        dd.itemText = itemLabel;
        dd.ClearOptions();
        dd.AddOptions(options);
        template.SetActive(false);

        ddGo.AddComponent<ButtonHoverFX>();
        return dd;
    }

    private static Button CreateStyledButton(string name, Transform parent, TMP_FontAsset font, Sprite btnSprite, string text, Vector2 pos, Vector2 size, Color bgColor)
    {
        GameObject go = CreateUIObject(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        Image img = go.AddComponent<Image>();
        img.sprite = btnSprite;
        img.type = btnSprite != null && btnSprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        img.color = bgColor;

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        CreateTMP("Text", go.transform, font, text, 23f, Color.white, TextAlignmentOptions.Center, Vector2.zero, size - new Vector2(16f, 8f), true);
        go.AddComponent<ButtonHoverFX>();
        return btn;
    }
}
