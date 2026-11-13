using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// Idempotent Editor setup utility for EcoQuest audio import settings, non-destructive
/// MainMixer UI/Ambience sub-groups, UITheme, Resources/AudioManager prefab, prefabs,
/// and non-Beach scenes (Main Menu, Forest Scene, City Scene).
/// </summary>
public static class EcoQuestAudioSetupTool
{
    private const string AudioRoot = "Assets/EcoQuest/Audio";
    private const string MixerPath = "Assets/EcoQuest/Audio/MainMixer.mixer";
    private const string ThemePath = "Assets/EcoQuest/UI/Shared/UITheme.asset";
    private const string AudioManagerPrefabPath = "Assets/Resources/AudioManager.prefab";

    private static readonly string[] TargetScenes =
    {
        "Assets/EcoQuest/Main Menu.unity",
        "Assets/EcoQuest/Forest Scene.unity",
        "Assets/EcoQuest/City Scene.unity"
    };

    [MenuItem("EcoQuest/Audio/Setup Complete Audio System")]
    public static void SetupMenuCommand()
    {
        string summary = RunCompleteSetup();
        Debug.Log($"[EcoQuestAudioSetup] {summary}");
    }

    public static string RunCompleteSetup()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        // 1. Apply optimal per-clip AudioImporter settings across Assets/EcoQuest/Audio
        int configuredClips = ConfigureAllAudioImportSettings();

        // 2. Extend MainMixer non-destructively with UI & Ambience child groups under SFX
        AudioMixer mixer = EnsureMixerSubGroups();
        AudioMixerGroup masterGrp = FindGroup(mixer, "Master");
        AudioMixerGroup musicGrp = FindGroup(mixer, "Music");
        AudioMixerGroup sfxGrp = FindGroup(mixer, "SFX");
        AudioMixerGroup uiGrp = FindGroup(mixer, "UI");
        if (uiGrp == null) uiGrp = sfxGrp;
        AudioMixerGroup ambGrp = FindGroup(mixer, "Ambience");
        if (ambGrp == null) ambGrp = sfxGrp;

        // 3. Populate UITheme.asset
        UITheme theme = PopulateUITheme();

        // 4. Create / update Assets/Resources/AudioManager.prefab (source of truth in builds)
        GameObject audioMgrPrefab = BuildAudioManagerResourcesPrefab(mixer, masterGrp, musicGrp, sfxGrp, uiGrp, ambGrp, theme);

        // 5. Update prefabs cleanly (Player, SceneTransition, SettingsPanel, LoadingScreen, UI_EcoShop, UI_Inventory)
        int updatedPrefabs = WirePrefabs(sfxGrp, uiGrp);

        // 6. Wire Main Menu, Forest Scene, City Scene (explicitly excluding Beach)
        string initialScenePath = SceneManager.GetActiveScene().path;
        int wiredScenes = 0;
        int fixedCars = 0;
        int routedSources = 0;

        for (int i = 0; i < TargetScenes.Length; i++)
        {
            string scenePath = TargetScenes[i];
            if (!File.Exists(scenePath)) continue;

            Scene sc = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            WireScene(sc, mixer, masterGrp, musicGrp, sfxGrp, uiGrp, ambGrp, theme, audioMgrPrefab, ref fixedCars, ref routedSources);
            EditorSceneManager.MarkSceneDirty(sc);
            EditorSceneManager.SaveScene(sc);
            wiredScenes++;
        }

        if (!string.IsNullOrEmpty(initialScenePath) && File.Exists(initialScenePath))
        {
            EditorSceneManager.OpenScene(initialScenePath, OpenSceneMode.Single);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return $"Configured {configuredClips} clips, updated {updatedPrefabs} prefabs, wired {wiredScenes} scenes, fixed {fixedCars} vehicle engine clips, routed {routedSources} AudioSources.";
    }

    private static int ConfigureAllAudioImportSettings()
    {
        int count = 0;
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot });
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) continue;

            AudioImporterSampleSettings sample = importer.defaultSampleSettings;
            bool dirty = false;

            if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            {
                bool targetMono = false;
                bool targetBg = true;
                AudioClipLoadType targetLoad = AudioClipLoadType.Streaming;
                AudioCompressionFormat targetFmt = AudioCompressionFormat.Vorbis;
                float targetQuality = path.Contains("/Music/") ? 0.72f : 0.68f;

                if (importer.forceToMono != targetMono || importer.loadInBackground != targetBg ||
                    sample.loadType != targetLoad || sample.compressionFormat != targetFmt ||
                    Mathf.Abs(sample.quality - targetQuality) > 0.02f)
                {
                    importer.forceToMono = targetMono;
                    importer.loadInBackground = targetBg;
                    sample.loadType = targetLoad;
                    sample.compressionFormat = targetFmt;
                    sample.quality = targetQuality;
                    sample.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                    dirty = true;
                }
            }
            else if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                bool isTrafficLoop = path.Contains("sfx_car_engine_hum") || path.Contains("sfx_train_rumble");
                bool isFanfare = path.Contains("sfx_quest_complete");
                AudioClipLoadType targetLoad = isFanfare ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                AudioCompressionFormat targetFmt = isFanfare ? AudioCompressionFormat.Vorbis : (isTrafficLoop ? AudioCompressionFormat.ADPCM : AudioCompressionFormat.PCM);
                float targetQuality = isFanfare ? 0.85f : sample.quality;

                if (!importer.forceToMono || importer.loadInBackground ||
                    sample.loadType != targetLoad || sample.compressionFormat != targetFmt ||
                    (isFanfare && Mathf.Abs(sample.quality - targetQuality) > 0.02f))
                {
                    importer.forceToMono = true;
                    importer.loadInBackground = false;
                    sample.loadType = targetLoad;
                    sample.compressionFormat = targetFmt;
                    if (isFanfare) sample.quality = targetQuality;
                    sample.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                    dirty = true;
                }
            }

            if (dirty)
            {
                importer.defaultSampleSettings = sample;
                importer.SaveAndReimport();
            }
            count++;
        }
        return count;
    }

    private static AudioMixer EnsureMixerSubGroups()
    {
        AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (mixer == null)
        {
            MixerBuilderHelper.BuildMainMixer();
            mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        }
        if (mixer == null) return null;

        Type mcType = Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor.CoreModule");
        if (mcType == null) return mixer;

        var createNewGroup = mcType.GetMethod("CreateNewGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var addChild = mcType.GetMethod("AddChildToParent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var sfxController = FindGroup(mixer, "SFX");

        if (sfxController != null && createNewGroup != null && addChild != null)
        {
            bool changed = false;
            if (FindGroup(mixer, "UI") == null)
            {
                object uiGrp = createNewGroup.Invoke(mixer, new object[] { "UI", true });
                addChild.Invoke(mixer, new object[] { uiGrp, sfxController });
                changed = true;
            }
            if (FindGroup(mixer, "Ambience") == null)
            {
                object ambGrp = createNewGroup.Invoke(mixer, new object[] { "Ambience", true });
                addChild.Invoke(mixer, new object[] { ambGrp, sfxController });
                changed = true;
            }

            if (changed)
            {
                mcType.GetMethod("OnSubAssetChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(mixer, null);
                EditorUtility.SetDirty(mixer);
                AssetDatabase.SaveAssets();
            }
        }

        return mixer;
    }

    private static AudioMixerGroup FindGroup(AudioMixer mixer, string groupName)
    {
        if (mixer == null) return null;
        var arr = mixer.FindMatchingGroups(groupName);
        if (arr == null) return null;
        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] != null && string.Equals(arr[i].name, groupName, StringComparison.Ordinal))
                return arr[i];
        }
        return arr.Length > 0 ? arr[0] : null;
    }

    private static AudioClip Clip(string relativeSubPath)
    {
        return AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioRoot}/{relativeSubPath}");
    }

    private static UITheme PopulateUITheme()
    {
        UITheme theme = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
        if (theme == null) return null;

        theme.buttonHoverSound = Clip("UI/ui_button_hover.wav");
        theme.buttonClickSound = Clip("UI/ui_button_click.wav");
        theme.modalOpenSound = Clip("UI/ui_modal_open.wav");
        theme.modalCloseSound = Clip("UI/ui_modal_close.wav");
        theme.quizCorrectSound = Clip("Gameplay/sfx_quiz_correct.wav");
        theme.quizWrongSound = Clip("Gameplay/sfx_quiz_wrong.wav");
        theme.coinPickupSound = Clip("Gameplay/sfx_coin_earn.wav");

        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        return theme;
    }

    private static void PopulateAudioManagerComponent(
        AudioManager am, AudioMixer mixer, AudioMixerGroup masterGrp, AudioMixerGroup musicGrp,
        AudioMixerGroup sfxGrp, AudioMixerGroup uiGrp, AudioMixerGroup ambGrp, UITheme theme)
    {
        if (am == null) return;
        am.mainMixer = mixer;
        am.masterGroup = masterGrp;
        am.musicGroup = musicGrp;
        am.sfxGroup = sfxGrp;
        am.uiGroup = uiGrp;
        am.ambienceGroup = ambGrp;
        am.uiThemeAsset = theme;

        am.defaultMusicClip = Clip("Music/bgm_forest.ogg");
        am.bgmMainMenu = Clip("Music/bgm_main_menu.ogg");
        am.bgmForest = Clip("Music/bgm_forest.ogg");
        am.bgmCity = Clip("Music/bgm_city.ogg");
        am.ambForest = Clip("Ambience/amb_forest.ogg");
        am.ambCity = Clip("Ambience/amb_city.ogg");

        am.buttonClickClip = Clip("UI/ui_button_click.wav");
        am.buttonHoverClip = Clip("UI/ui_button_hover.wav");
        am.sliderChangeClip = Clip("UI/ui_slider_tick.wav");
        am.uiModalOpenClip = Clip("UI/ui_modal_open.wav");
        am.uiModalCloseClip = Clip("UI/ui_modal_close.wav");
        am.uiPanelWhooshClip = Clip("UI/ui_panel_whoosh.wav");
        am.uiDialogueBlipClip = Clip("UI/ui_dialogue_blip.wav");
        am.uiNotificationClip = Clip("UI/ui_notification.wav");
        am.uiTutorialStepClip = Clip("UI/ui_tutorial_step.wav");
        am.uiHungerWarningClip = Clip("UI/ui_hunger_warning.wav");
        am.uiShopBuyClip = Clip("UI/ui_shop_buy.wav");
        am.uiShopErrorClip = Clip("UI/ui_shop_error.wav");
        am.uiItemUseClip = Clip("UI/ui_item_use.wav");

        am.sfxTrashPickup = Clip("Gameplay/sfx_trash_pickup.wav");
        am.sfxTrashDrop = Clip("Gameplay/sfx_trash_drop.wav");
        am.sfxTrashDepositCorrect = Clip("Gameplay/sfx_trash_correct.wav");
        am.sfxTrashDepositWrong = Clip("Gameplay/sfx_trash_wrong.wav");
        am.sfxQuizCorrect = Clip("Gameplay/sfx_quiz_correct.wav");
        am.sfxQuizWrong = Clip("Gameplay/sfx_quiz_wrong.wav");
        am.sfxCoinEarn = Clip("Gameplay/sfx_coin_earn.wav");
        am.sfxQuestComplete = Clip("Gameplay/sfx_quest_complete.wav");
        am.sfxJump = Clip("Gameplay/sfx_jump.wav");
        am.sfxLand = Clip("Gameplay/sfx_land.wav");
        am.sfxLoadingReady = Clip("Gameplay/sfx_loading_ready.wav");
        am.sfxLitterThrow = Clip("Gameplay/sfx_litter_throw.wav");
        am.sfxLitterLand = Clip("Gameplay/sfx_litter_land.wav");

        am.footstepGrassClips = new[]
        {
            Clip("Gameplay/sfx_footstep_grass_01.wav"),
            Clip("Gameplay/sfx_footstep_grass_02.wav"),
            Clip("Gameplay/sfx_footstep_grass_03.wav"),
            Clip("Gameplay/sfx_footstep_grass_04.wav")
        };
        am.footstepCityClips = new[]
        {
            Clip("Gameplay/sfx_footstep_city_01.wav"),
            Clip("Gameplay/sfx_footstep_city_02.wav"),
            Clip("Gameplay/sfx_footstep_city_03.wav"),
            Clip("Gameplay/sfx_footstep_city_04.wav")
        };

        am.musicSource = EnsureChildAudioSource(am.transform, "MusicSource", true, musicGrp, false);
        am.ambienceSource = EnsureChildAudioSource(am.transform, "AmbienceSource", true, ambGrp, false);
        am.sfxSource = EnsureChildAudioSource(am.transform, "SFXSource", false, sfxGrp, true);
    }

    private static AudioSource EnsureChildAudioSource(Transform root, string childName, bool loop, AudioMixerGroup group, bool ignorePause)
    {
        Transform t = root.Find(childName);
        GameObject go = t != null ? t.gameObject : new GameObject(childName);
        if (t == null) go.transform.SetParent(root, false);
        AudioSource src = go.GetComponent<AudioSource>();
        if (src == null) src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 0f;
        src.ignoreListenerPause = ignorePause;
        src.outputAudioMixerGroup = group;
        return src;
    }

    private static GameObject BuildAudioManagerResourcesPrefab(
        AudioMixer mixer, AudioMixerGroup masterGrp, AudioMixerGroup musicGrp,
        AudioMixerGroup sfxGrp, AudioMixerGroup uiGrp, AudioMixerGroup ambGrp, UITheme theme)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        GameObject root = new GameObject("AudioManager");
        AudioManager am = root.AddComponent<AudioManager>();
        PopulateAudioManagerComponent(am, mixer, masterGrp, musicGrp, sfxGrp, uiGrp, ambGrp, theme);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, AudioManagerPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    private static int WirePrefabs(AudioMixerGroup sfxGrp, AudioMixerGroup uiGrp)
    {
        string[] prefabPaths =
        {
            "Assets/EcoQuest/Prefabs/Player.prefab",
            "Assets/StarterAssets/FirstPersonController/Prefabs/PlayerCapsule.prefab",
            "Assets/EcoQuest/Prefabs/SceneTransition.prefab",
            "Assets/EcoQuest/Prefabs/SettingsPanel.prefab",
            "Assets/Resources/LoadingScreen.prefab",
            "Assets/EcoQuest/UI/LoadingScreen/LoadingScreen.prefab",
            "Assets/EcoQuest/Inventory/Prefabs/UI_EcoShop.prefab",
            "Assets/EcoQuest/Inventory/Prefabs/UI_Inventory.prefab"
        };

        int modifiedCount = 0;
        AudioClip landClip = Clip("Gameplay/sfx_land.wav");
        AudioClip[] grassSteps =
        {
            Clip("Gameplay/sfx_footstep_grass_01.wav"),
            Clip("Gameplay/sfx_footstep_grass_02.wav"),
            Clip("Gameplay/sfx_footstep_grass_03.wav"),
            Clip("Gameplay/sfx_footstep_grass_04.wav")
        };

        foreach (string path in prefabPaths)
        {
            if (!File.Exists(path)) continue;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            if (contents == null) continue;

            bool isUiPrefab = path.Contains("SettingsPanel") || path.Contains("LoadingScreen") || path.Contains("UI_");
            AudioMixerGroup fallbackGrp = isUiPrefab ? uiGrp : sfxGrp;

            foreach (var src in contents.GetComponentsInChildren<AudioSource>(true))
            {
                if (src != null && src.outputAudioMixerGroup == null)
                {
                    src.outputAudioMixerGroup = fallbackGrp;
                }
            }

            foreach (var fpc in contents.GetComponentsInChildren<StarterAssets.FirstPersonController>(true))
            {
                fpc.LandingAudioClip = landClip;
                fpc.FootstepAudioClips = grassSteps;
                if (fpc.FootstepAudioSource != null && fpc.FootstepAudioSource.outputAudioMixerGroup == null)
                    fpc.FootstepAudioSource.outputAudioMixerGroup = sfxGrp;
            }

            var st = contents.GetComponent<SceneTransition>();
            if (st != null)
            {
                if (st.startSoundSource != null)
                {
                    st.startSoundSource.clip = Clip("UI/ui_modal_open.wav");
                    st.startSoundSource.outputAudioMixerGroup = uiGrp;
                }
                if (st.whooshSoundSource != null)
                {
                    st.whooshSoundSource.clip = Clip("UI/ui_panel_whoosh.wav");
                    st.whooshSoundSource.outputAudioMixerGroup = uiGrp;
                }
            }

            var lsc = contents.GetComponent<LoadingScreenController>();
            if (lsc != null)
            {
                SetClipProp(lsc, "readyChimeClip", Clip("Gameplay/sfx_loading_ready.wav"));
                SetClipProp(lsc, "tipChangeClip", Clip("UI/ui_panel_whoosh.wav"));
            }

            WireEcoShopAndInventory(contents, uiGrp);

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            modifiedCount++;
        }

        return modifiedCount;
    }

    private static void WireEcoShopAndInventory(GameObject root, AudioMixerGroup uiGrp)
    {
        foreach (var shop in root.GetComponentsInChildren<EcoQuest.Inventory.UI_EcoShop>(true))
        {
            SetClipProp(shop, "openSound", Clip("UI/ui_modal_open.wav"));
            SetClipProp(shop, "closeSound", Clip("UI/ui_modal_close.wav"));
            SetClipProp(shop, "buySound", Clip("UI/ui_shop_buy.wav"));
            SetClipProp(shop, "errorSound", Clip("UI/ui_shop_error.wav"));
            var src = shop.GetComponent<AudioSource>();
            if (src != null && src.outputAudioMixerGroup == null) src.outputAudioMixerGroup = uiGrp;
        }

        foreach (var inv in root.GetComponentsInChildren<EcoQuest.Inventory.UI_Inventory>(true))
        {
            SetClipProp(inv, "openSound", Clip("UI/ui_modal_open.wav"));
            SetClipProp(inv, "closeSound", Clip("UI/ui_modal_close.wav"));
            SetClipProp(inv, "selectSound", Clip("UI/ui_button_click.wav"));
            SetClipProp(inv, "useItemSound", Clip("UI/ui_item_use.wav"));
            var src = inv.GetComponent<AudioSource>();
            if (src != null && src.outputAudioMixerGroup == null) src.outputAudioMixerGroup = uiGrp;
        }
    }

    private static void WireScene(
        Scene scene, AudioMixer mixer, AudioMixerGroup masterGrp, AudioMixerGroup musicGrp,
        AudioMixerGroup sfxGrp, AudioMixerGroup uiGrp, AudioMixerGroup ambGrp, UITheme theme,
        GameObject audioMgrPrefab, ref int fixedCars, ref int routedSources)
    {
        bool isMainMenu = scene.name.IndexOf("Main Menu", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isCity = scene.name.IndexOf("City", StringComparison.OrdinalIgnoreCase) >= 0;

        // 1. Sync scene AudioManager (or instantiate only in Main Menu so Forest/City instantiate from Resources cleanly)
        var sceneAm = UnityEngine.Object.FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include);
        if (sceneAm == null && isMainMenu && audioMgrPrefab != null)
        {
            var inst = PrefabUtility.InstantiatePrefab(audioMgrPrefab, scene) as GameObject;
            sceneAm = inst != null ? inst.GetComponent<AudioManager>() : null;
        }
        if (sceneAm != null)
        {
            PopulateAudioManagerComponent(sceneAm, mixer, masterGrp, musicGrp, sfxGrp, uiGrp, ambGrp, theme);
            sceneAm.defaultMusicClip = isMainMenu ? sceneAm.bgmMainMenu : (isCity ? sceneAm.bgmCity : sceneAm.bgmForest);
            EditorUtility.SetDirty(sceneAm);
        }

        // 2. SettingsPanelUI mixer assignment
        foreach (var sp in UnityEngine.Object.FindObjectsByType<SettingsPanelUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SetObjectProp(sp, "mainMixerAsset", mixer);
        }

        // 3. Player footsteps (grass in Forest, city pavement in City)
        AudioClip landClip = Clip("Gameplay/sfx_land.wav");
        AudioClip[] stepBank = isCity
            ? new[] { Clip("Gameplay/sfx_footstep_city_01.wav"), Clip("Gameplay/sfx_footstep_city_02.wav"), Clip("Gameplay/sfx_footstep_city_03.wav"), Clip("Gameplay/sfx_footstep_city_04.wav") }
            : new[] { Clip("Gameplay/sfx_footstep_grass_01.wav"), Clip("Gameplay/sfx_footstep_grass_02.wav"), Clip("Gameplay/sfx_footstep_grass_03.wav"), Clip("Gameplay/sfx_footstep_grass_04.wav") };

        foreach (var fpc in UnityEngine.Object.FindObjectsByType<StarterAssets.FirstPersonController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            fpc.LandingAudioClip = landClip;
            fpc.FootstepAudioClips = stepBank;
            if (fpc.FootstepAudioSource != null && fpc.FootstepAudioSource.outputAudioMixerGroup == null)
                fpc.FootstepAudioSource.outputAudioMixerGroup = sfxGrp;
            EditorUtility.SetDirty(fpc);
        }

        // 4. Survival HUD hunger warning, Explorer Gate, Inventory, Shop, Litterbug
        foreach (var hud in UnityEngine.Object.FindObjectsByType<UI_SurvivalStatsHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SetClipProp(hud, "_hungerWarningSound", Clip("UI/ui_hunger_warning.wav"));
        }

        foreach (var gateNote in UnityEngine.Object.FindObjectsByType<ExplorerGateNotification>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SetClipProp(gateNote, "notificationClip", Clip("UI/ui_notification.wav"));
        }

        foreach (var gateCtrl in UnityEngine.Object.FindObjectsByType<ExplorerGateController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SetClipProp(gateCtrl, "travelAcceptClip", Clip("Gameplay/sfx_quest_complete.wav"));
        }

        foreach (var rootGo in scene.GetRootGameObjects())
        {
            WireEcoShopAndInventory(rootGo, uiGrp);
        }

        foreach (var lb in UnityEngine.Object.FindObjectsByType<LitterbugQuestManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SetClipProp(lb, "throwClip", Clip("Gameplay/sfx_litter_throw.wav"));
            SetClipProp(lb, "landClip", Clip("Gameplay/sfx_litter_land.wav"));
            SetClipProp(lb, "dialogueBlipClip", Clip("UI/ui_dialogue_blip.wav"));
            SetClipProp(lb, "coinClip", Clip("Gameplay/sfx_coin_earn.wav"));
        }

        // 5. Fix all CityTraffic vehicle / train / crossing AudioSources & clips
        AudioClip carEngineClip = Clip("Traffic/sfx_car_engine_hum.wav");
        AudioClip carHornClip = Clip("Traffic/sfx_car_horn_friendly.wav");
        foreach (var car in UnityEngine.Object.FindObjectsByType<EcoQuest.CityTraffic.TrafficVehicleController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            car.engineHumClip = carEngineClip;
            car.hornClip = carHornClip;
            if (car.hornAudioSource != null) car.hornAudioSource.outputAudioMixerGroup = sfxGrp;
            if (car.engineAudioSource != null)
            {
                car.engineAudioSource.clip = carEngineClip;
                car.engineAudioSource.outputAudioMixerGroup = sfxGrp;
            }
            EditorUtility.SetDirty(car);
            fixedCars++;
        }

        AudioClip trainHornClip = Clip("Traffic/sfx_train_horn.wav");
        AudioClip trainRumbleClip = Clip("Traffic/sfx_train_rumble.wav");
        foreach (var train in UnityEngine.Object.FindObjectsByType<EcoQuest.CityTraffic.TrafficTrainController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            train.hornClip = trainHornClip;
            if (train.hornAudioSource != null) train.hornAudioSource.outputAudioMixerGroup = sfxGrp;
            if (train.rumbleAudioSource != null)
            {
                train.rumbleAudioSource.clip = trainRumbleClip;
                train.rumbleAudioSource.outputAudioMixerGroup = sfxGrp;
            }
            EditorUtility.SetDirty(train);
        }

        foreach (var rail in UnityEngine.Object.FindObjectsByType<EcoQuest.CityTraffic.RailCrossingController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (rail.bellAudioSource != null)
            {
                rail.bellAudioSource.outputAudioMixerGroup = sfxGrp;
                EditorUtility.SetDirty(rail.bellAudioSource);
            }
        }

        // 6. Route EVERY remaining unrouted AudioSource in the scene
        foreach (var src in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (src == null) continue;
            if (sceneAm != null && (src == sceneAm.musicSource || src == sceneAm.ambienceSource)) continue;
            if (src.outputAudioMixerGroup == null)
            {
                bool isUiObj = src.GetComponentInParent<Canvas>(true) != null
                    || src.gameObject.name.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0
                    || src.gameObject.name.IndexOf("StartSoundHook", StringComparison.OrdinalIgnoreCase) >= 0
                    || src.gameObject.name.IndexOf("WhooshSoundHook", StringComparison.OrdinalIgnoreCase) >= 0;
                src.outputAudioMixerGroup = isUiObj ? uiGrp : sfxGrp;
                EditorUtility.SetDirty(src);
                routedSources++;
            }
        }
    }

    private static void SetClipProp(UnityEngine.Object target, string propName, AudioClip clip)
    {
        if (target == null || clip == null) return;
        var so = new SerializedObject(target);
        var p = so.FindProperty(propName);
        if (p != null)
        {
            p.objectReferenceValue = clip;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }

    private static void SetObjectProp(UnityEngine.Object target, string propName, UnityEngine.Object val)
    {
        if (target == null || val == null) return;
        var so = new SerializedObject(target);
        var p = so.FindProperty(propName);
        if (p != null)
        {
            p.objectReferenceValue = val;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
