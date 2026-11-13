using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EcoQuest.Tests.PlayMode
{
    public class PerformanceAndSettingsPlayModeTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private Type settingsManagerType;
        private Type settingsPanelType;

        private int savedQuality;
        private int savedVsync;
        private int savedFps;
        private bool hadSavedQuality;
        private bool hadSavedAutoRes;
        private int savedAutoRes;
        private bool hadSavedFullscreen;
        private int savedFullscreen;
        private int savedResW;
        private int savedResH;
        private bool hadSavedDisplayMode;
        private int savedDisplayMode;
        private bool hadSavedDisplayModeMigrated;
        private int savedDisplayModeMigrated;
        private bool hadSavedRealAutoRes;
        private int savedRealAutoRes;
        private bool hadSavedRealResW;
        private int savedRealResW;
        private bool hadSavedRealResH;
        private int savedRealResH;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            settingsManagerType = Type.GetType("SettingsManager, Assembly-CSharp", true);
            settingsPanelType = Type.GetType("SettingsPanelUI, Assembly-CSharp", true);
        }

        [SetUp]
        public void SetUp()
        {
            hadSavedQuality = PlayerPrefs.HasKey("EcoQuest_Settings_Quality");
            savedQuality = PlayerPrefs.GetInt("EcoQuest_Settings_Quality", 2);
            hadSavedAutoRes = PlayerPrefs.HasKey("EcoQuest_Settings_AutoRes");
            savedAutoRes = PlayerPrefs.GetInt("EcoQuest_Settings_AutoRes", 1);
            hadSavedFullscreen = PlayerPrefs.HasKey("EcoQuest_Settings_Fullscreen");
            savedFullscreen = PlayerPrefs.GetInt("EcoQuest_Settings_Fullscreen", 1);
            savedResW = PlayerPrefs.GetInt("EcoQuest_Settings_ResW", Screen.width);
            savedResH = PlayerPrefs.GetInt("EcoQuest_Settings_ResH", Screen.height);
            savedVsync = PlayerPrefs.GetInt("EcoQuest_Settings_VSync", 1);
            savedFps = PlayerPrefs.GetInt("EcoQuest_Settings_FPSLimit", -1);

            hadSavedDisplayMode = PlayerPrefs.HasKey("EcoQuest_Settings_DisplayMode");
            savedDisplayMode = PlayerPrefs.GetInt("EcoQuest_Settings_DisplayMode", 0);
            hadSavedDisplayModeMigrated = PlayerPrefs.HasKey("EcoQuest_Settings_DisplayMode_v2_Migrated");
            savedDisplayModeMigrated = PlayerPrefs.GetInt("EcoQuest_Settings_DisplayMode_v2_Migrated", 0);
            hadSavedRealAutoRes = PlayerPrefs.HasKey("EcoQuest_Settings_ResolutionAuto");
            savedRealAutoRes = PlayerPrefs.GetInt("EcoQuest_Settings_ResolutionAuto", 1);
            hadSavedRealResW = PlayerPrefs.HasKey("EcoQuest_Settings_ResolutionWidth");
            savedRealResW = PlayerPrefs.GetInt("EcoQuest_Settings_ResolutionWidth", Screen.width);
            hadSavedRealResH = PlayerPrefs.HasKey("EcoQuest_Settings_ResolutionHeight");
            savedRealResH = PlayerPrefs.GetInt("EcoQuest_Settings_ResolutionHeight", Screen.height);
        }

        [TearDown]
        public void TearDown()
        {
            if (hadSavedQuality) PlayerPrefs.SetInt("EcoQuest_Settings_Quality", savedQuality);
            else PlayerPrefs.DeleteKey("EcoQuest_Settings_Quality");
            if (hadSavedAutoRes) PlayerPrefs.SetInt("EcoQuest_Settings_AutoRes", savedAutoRes);
            else PlayerPrefs.DeleteKey("EcoQuest_Settings_AutoRes");
            if (hadSavedFullscreen) PlayerPrefs.SetInt("EcoQuest_Settings_Fullscreen", savedFullscreen);
            else PlayerPrefs.DeleteKey("EcoQuest_Settings_Fullscreen");
            PlayerPrefs.SetInt("EcoQuest_Settings_ResW", savedResW);
            PlayerPrefs.SetInt("EcoQuest_Settings_ResH", savedResH);
            PlayerPrefs.SetInt("EcoQuest_Settings_VSync", savedVsync);
            PlayerPrefs.SetInt("EcoQuest_Settings_FPSLimit", savedFps);

            RestoreIntPref("EcoQuest_Settings_DisplayMode", hadSavedDisplayMode, savedDisplayMode);
            RestoreIntPref("EcoQuest_Settings_DisplayMode_v2_Migrated", hadSavedDisplayModeMigrated, savedDisplayModeMigrated);
            RestoreIntPref("EcoQuest_Settings_ResolutionAuto", hadSavedRealAutoRes, savedRealAutoRes);
            RestoreIntPref("EcoQuest_Settings_ResolutionWidth", hadSavedRealResW, savedRealResW);
            RestoreIntPref("EcoQuest_Settings_ResolutionHeight", hadSavedRealResH, savedRealResH);
            PlayerPrefs.Save();

            object sm = settingsManagerType.GetProperty("Instance", Flags)?.GetValue(null);
            if (sm != null)
            {
                settingsManagerType.GetMethod("LoadSettings", Flags)?.Invoke(sm, null);
                settingsManagerType.GetMethod("ApplyGraphicsSettings", Flags)?.Invoke(sm, new object[] { false });
            }
        }

        private static void RestoreIntPref(string key, bool hadKey, int savedVal)
        {
            if (hadKey) PlayerPrefs.SetInt(key, savedVal);
            else PlayerPrefs.DeleteKey(key);
        }

        [UnityTest]
        public IEnumerator FPSLimitDropdown_RemainsUnlockedWithVSyncOn_AndSyncsBidirectionally()
        {
            var load = SceneManager.LoadSceneAsync("Main Menu");
            while (load != null && !load.isDone) yield return null;
            yield return null;
            yield return null;

            object sm = settingsManagerType.GetProperty("Instance", Flags)?.GetValue(null);
            Assert.IsNotNull(sm, "SettingsManager.Instance should exist in Main Menu.");

            // Start with VSync ON and Unlimited FPS Limit (-1)
            settingsManagerType.GetMethod("SetAndApplyGraphics", Flags)?.Invoke(sm, new object[]
            {
                2, true, true, Screen.width, Screen.height, true, -1, false
            });
            yield return null;

            UnityEngine.Object panelObj = null;
            foreach (var obj in UnityEngine.Object.FindObjectsByType(settingsPanelType, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                panelObj = obj;
                break;
            }
            Assert.IsNotNull(panelObj, "SettingsPanelUI should exist in Main Menu scene.");

            Component panelComp = panelObj as Component;
            Assert.IsNotNull(panelComp);
            panelComp.gameObject.SetActive(true);
            yield return null;
            yield return null;

            object vsyncToggle = settingsPanelType.GetField("vsyncToggle", Flags)?.GetValue(panelComp);
            object fpsDropdown = settingsPanelType.GetField("fpsLimitDropdown", Flags)?.GetValue(panelComp);
            Assert.IsNotNull(vsyncToggle, "vsyncToggle must be wired on SettingsPanelUI.");
            Assert.IsNotNull(fpsDropdown, "fpsLimitDropdown must be wired on SettingsPanelUI.");

            PropertyInfo isOnProp = vsyncToggle.GetType().GetProperty("isOn");
            PropertyInfo ddInteractableProp = fpsDropdown.GetType().GetProperty("interactable");
            PropertyInfo ddValueProp = fpsDropdown.GetType().GetProperty("value");

            Assert.IsTrue((bool)isOnProp.GetValue(vsyncToggle), "VSync toggle should reflect active VSync=true.");
            Assert.IsTrue((bool)ddInteractableProp.GetValue(fpsDropdown), "FPS Limit dropdown must stay interactable even when VSync is ON.");

            Component ddComp = fpsDropdown as Component;
            Assert.IsNotNull(ddComp);
            CanvasGroup ddCg = ddComp.GetComponent<CanvasGroup>();
            if (ddCg != null)
            {
                Assert.GreaterOrEqual(ddCg.alpha, 0.99f, "FPS Limit dropdown must not be dimmed when VSync is ON.");
                Assert.IsTrue(ddCg.interactable, "CanvasGroup on FPS Limit dropdown must remain interactable.");
                Assert.IsTrue(ddCg.blocksRaycasts, "CanvasGroup on FPS Limit dropdown must allow raycasts.");
            }

            // Pick 60 FPS (index 1 in {30, 60, 120, -1}) while VSync is ON -> should auto-turn VSync OFF
            ddValueProp.SetValue(fpsDropdown, 1);
            yield return null;

            Assert.IsFalse((bool)isOnProp.GetValue(vsyncToggle), "Selecting a finite FPS limit (60 FPS) should turn VSync OFF.");
            Assert.IsTrue((bool)ddInteractableProp.GetValue(fpsDropdown));

            MethodInfo applyMethod = settingsPanelType.GetMethod("OnApplyGraphicsClicked", Flags);
            Assert.IsNotNull(applyMethod);
            applyMethod.Invoke(panelComp, null);
            yield return null;

            Assert.AreEqual(0, QualitySettings.vSyncCount, "Applying after choosing 60 FPS should set vSyncCount = 0.");
            Assert.AreEqual(60, Application.targetFrameRate, "Applying after choosing 60 FPS should set targetFrameRate = 60.");

            // Toggle VSync back ON -> dropdown must stay unlocked and keep 60 FPS visible; applying enables VSync
            isOnProp.SetValue(vsyncToggle, true);
            yield return null;
            Assert.IsTrue((bool)ddInteractableProp.GetValue(fpsDropdown), "FPS Limit dropdown must stay unlocked when VSync is re-enabled.");
            Assert.AreEqual(1, (int)ddValueProp.GetValue(fpsDropdown), "Chosen FPS cap (60 FPS) should remain preserved when VSync is toggled ON.");

            applyMethod.Invoke(panelComp, null);
            yield return null;
            Assert.AreEqual(1, QualitySettings.vSyncCount, "Applying with VSync ON should set vSyncCount = 1.");
            Assert.AreEqual(-1, Application.targetFrameRate, "When VSync is ON, targetFrameRate should defer to VSync (-1).");

            // Toggle VSync back OFF -> last selected 60 FPS cap restores seamlessly on Apply
            isOnProp.SetValue(vsyncToggle, false);
            yield return null;
            Assert.AreEqual(1, (int)ddValueProp.GetValue(fpsDropdown));
            applyMethod.Invoke(panelComp, null);
            yield return null;
            Assert.AreEqual(0, QualitySettings.vSyncCount);
            Assert.AreEqual(60, Application.targetFrameRate, "Unchecking VSync should restore the user's selected 60 FPS cap.");
        }

        [UnityTest]
        public IEnumerator QualityPresets_UpdateURPAssetTerrainLODAndCameraAA_AndPersistAcrossForestAndCityScenes()
        {
            MethodInfo setAndApply = settingsManagerType.GetMethod("SetAndApplyGraphics", Flags);
            Assert.IsNotNull(setAndApply);

            // 1. Main Menu: verify switching across Low..Ultra updates active URP asset & Main Camera AA cleanly
            var loadMenu = SceneManager.LoadSceneAsync("Main Menu");
            while (loadMenu != null && !loadMenu.isDone) yield return null;
            yield return null;
            yield return null;

            object sm = settingsManagerType.GetProperty("Instance", Flags)?.GetValue(null);
            Assert.IsNotNull(sm, "SettingsManager.Instance must exist in Main Menu.");

            string[] expectedRpNames = { "URP_Low", "URP_Medium", "URP_High", "URP_Ultra" };
            for (int q = 0; q < 4; q++)
            {
                setAndApply.Invoke(sm, new object[] { q, true, true, Screen.width, Screen.height, false, -1, false });
                yield return null;
                Assert.AreEqual(q, QualitySettings.GetQualityLevel());
                Assert.IsNotNull(QualitySettings.renderPipeline);
                Assert.AreEqual(expectedRpNames[q], QualitySettings.renderPipeline.name);
            }

            // Persist Medium (1) before transitioning scenes
            setAndApply.Invoke(sm, new object[] { 1, true, true, Screen.width, Screen.height, false, -1, true });
            yield return null;

            // 2. Forest Scene: verify persisted Medium + Terrain LOD scaling + Minimap configuration
            var loadForest = SceneManager.LoadSceneAsync("Forest Scene");
            while (loadForest != null && !loadForest.isDone) yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual(1, QualitySettings.GetQualityLevel(), "Medium quality should persist into Forest Scene.");
            Assert.AreEqual("URP_Medium", QualitySettings.renderPipeline.name);

            sm = settingsManagerType.GetProperty("Instance", Flags)?.GetValue(null);
            Terrain forestTerrain = Terrain.activeTerrain;
            Assert.IsNotNull(forestTerrain, "Forest Scene should have an active Terrain.");
            Assert.AreEqual(50f, forestTerrain.detailObjectDistance, 0.5f, "Medium Terrain detailObjectDistance mismatch.");
            Assert.AreEqual(0.60f, forestTerrain.detailObjectDensity, 0.02f, "Medium Terrain detailObjectDensity mismatch.");

            // Cycle to High (2) and Low (0) on Forest and verify Terrain LOD updates immediately
            setAndApply.Invoke(sm, new object[] { 2, true, true, Screen.width, Screen.height, false, -1, false });
            yield return null;
            Assert.AreEqual(70f, forestTerrain.detailObjectDistance, 0.5f, "High Terrain detailObjectDistance mismatch.");
            Assert.AreEqual(0.85f, forestTerrain.detailObjectDensity, 0.02f, "High Terrain detailObjectDensity mismatch.");

            // Check Camera AA updated cleanly on High (SMAA) vs Low (None)
            Camera mainCam = Camera.main;
            Assert.IsNotNull(mainCam);
            Component uac = mainCam.GetComponent("UniversalAdditionalCameraData");
            Assert.IsNotNull(uac);
            PropertyInfo aaProp = uac.GetType().GetProperty("antialiasing");
            Assert.IsNotNull(aaProp);
            Assert.AreEqual("SubpixelMorphologicalAntiAliasing", aaProp.GetValue(uac).ToString());

            setAndApply.Invoke(sm, new object[] { 0, true, true, Screen.width, Screen.height, false, -1, false });
            yield return null;
            Assert.AreEqual(35f, forestTerrain.detailObjectDistance, 0.5f, "Low Terrain detailObjectDistance mismatch.");
            Assert.AreEqual("None", aaProp.GetValue(uac).ToString());

            // Informational frame timing check over 10 frames (never fails on busy hardware)
            float sumDt = 0f;
            for (int f = 0; f < 10; f++) { sumDt += Time.unscaledDeltaTime; yield return null; }
            Debug.Log($"[PerfTest] Forest 10-frame avg dt={(sumDt / 10f) * 1000f:F2}ms");

            // 3. City Scene: verify clean unload from Forest (0 assertions), Medium persistence, MinimapCamera optimized setup & inactive UI blur
            setAndApply.Invoke(sm, new object[] { 1, true, true, Screen.width, Screen.height, false, -1, true });
            var loadCity = SceneManager.LoadSceneAsync("City Scene");
            while (loadCity != null && !loadCity.isDone) yield return null;
            for (int i = 0; i < 5; i++) yield return null;

            Assert.AreEqual(1, QualitySettings.GetQualityLevel(), "Medium quality should persist into City Scene.");
            Assert.AreEqual("URP_Medium", QualitySettings.renderPipeline.name);

            Type minimapType = Type.GetType("MinimapController, Assembly-CSharp", true);
            Component minimapCtrl = minimapType.GetProperty("Instance", Flags)?.GetValue(null) as Component;
            Assert.IsNotNull(minimapCtrl, "MinimapController.Instance must exist in City Scene.");
            Camera mmCam = minimapCtrl.GetComponent<Camera>();
            Assert.IsNotNull(mmCam);
            Assert.IsFalse(mmCam.enabled, "MinimapCam should stay disabled for manual cadence rendering.");
            Assert.IsFalse(mmCam.allowMSAA, "MinimapCam MSAA must be disabled.");
            Component mmUac = mmCam.GetComponent("UniversalAdditionalCameraData");
            Assert.IsNotNull(mmUac);
            Assert.IsFalse((bool)mmUac.GetType().GetProperty("renderShadows").GetValue(mmUac), "MinimapCam must not render shadows.");

            Type volType = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
            if (volType != null)
            {
                foreach (var obj in UnityEngine.Object.FindObjectsByType(volType, FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Component volComp = obj as Component;
                    if (volComp != null && volComp.gameObject.name == "UI_Blur_Volume")
                    {
                        FieldInfo weightField = volType.GetField("weight", Flags);
                        float weight = weightField != null ? (float)weightField.GetValue(volComp) : 0f;
                        Assert.IsTrue(!volComp.gameObject.activeSelf || weight <= 0.0001f, "UI_Blur_Volume should stay inactive/zero weight when no modal is open.");
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator ResolutionDropdown_IncludesBorderlessAfterAuto_LocksFullscreenAndPersistsAcrossReload()
        {
            var loadMenu = SceneManager.LoadSceneAsync("Main Menu");
            while (loadMenu != null && !loadMenu.isDone) yield return null;
            yield return null;
            yield return null;

            object sm = settingsManagerType.GetProperty("Instance", Flags)?.GetValue(null);
            Assert.IsNotNull(sm, "SettingsManager.Instance should exist in Main Menu.");

            Type displayModeType = Type.GetType("ResolutionDisplayMode, Assembly-CSharp", true);
            object modeAuto = Enum.Parse(displayModeType, "Auto");
            object modeBorderless = Enum.Parse(displayModeType, "Borderless");
            MethodInfo setDisplay = settingsManagerType.GetMethod("SetAndApplyDisplayGraphics", Flags);
            Assert.IsNotNull(setDisplay, "SetAndApplyDisplayGraphics must exist on SettingsManager.");

            // Start at Auto + Windowed (Fullscreen=false) so toggling to Borderless proves lock + auto-check + restore.
            setDisplay.Invoke(sm, new object[] { 2, false, modeAuto, Screen.width, Screen.height, true, -1, true });
            yield return null;

            Component panelComp = FindFirstSettingsPanel();
            Assert.IsNotNull(panelComp, "SettingsPanelUI should exist in Main Menu scene.");
            panelComp.gameObject.SetActive(true);
            yield return null;
            yield return null;

            object resDropdown = settingsPanelType.GetField("resolutionDropdown", Flags)?.GetValue(panelComp);
            object fsToggle = settingsPanelType.GetField("fullscreenToggle", Flags)?.GetValue(panelComp);
            Assert.IsNotNull(resDropdown, "resolutionDropdown must be wired on SettingsPanelUI.");
            Assert.IsNotNull(fsToggle, "fullscreenToggle must be wired on SettingsPanelUI.");

            var optionsList = resDropdown.GetType().GetProperty("options").GetValue(resDropdown) as System.Collections.IList;
            Assert.IsNotNull(optionsList);
            Assert.GreaterOrEqual(optionsList.Count, 2, "Resolution dropdown should include Auto and Borderless.");
            PropertyInfo optionTextProp = optionsList[0].GetType().GetProperty("text");
            Assert.AreEqual("Auto (Fit Screen)", optionTextProp.GetValue(optionsList[0]));
            Assert.AreEqual("Borderless", optionTextProp.GetValue(optionsList[1]));

            PropertyInfo ddValueProp = resDropdown.GetType().GetProperty("value");
            PropertyInfo toggleOnProp = fsToggle.GetType().GetProperty("isOn");
            PropertyInfo toggleInteractableProp = fsToggle.GetType().GetProperty("interactable");

            Assert.AreEqual(0, (int)ddValueProp.GetValue(resDropdown), "Should open on Auto (index 0).");
            Assert.IsFalse((bool)toggleOnProp.GetValue(fsToggle), "Auto mode started Windowed.");
            Assert.IsTrue((bool)toggleInteractableProp.GetValue(fsToggle), "Auto mode must keep Fullscreen toggle editable.");

            // Switch to Borderless (index 1) -> Fullscreen locks checked & dimmed
            ddValueProp.SetValue(resDropdown, 1);
            yield return null;

            Assert.IsTrue((bool)toggleOnProp.GetValue(fsToggle), "Borderless should force Fullscreen toggle checked.");
            Assert.IsFalse((bool)toggleInteractableProp.GetValue(fsToggle), "Borderless must lock Fullscreen toggle.");

            // Switch back to Auto (index 0) before saving -> restores Windowed state and unlocks toggle
            ddValueProp.SetValue(resDropdown, 0);
            yield return null;

            Assert.IsTrue((bool)toggleInteractableProp.GetValue(fsToggle), "Leaving Borderless must unlock Fullscreen toggle.");
            Assert.IsFalse((bool)toggleOnProp.GetValue(fsToggle), "Leaving Borderless should restore prior non-Borderless Fullscreen choice.");

            // Pick Borderless again and Apply
            ddValueProp.SetValue(resDropdown, 1);
            yield return null;
            MethodInfo applyMethod = settingsPanelType.GetMethod("OnApplyGraphicsClicked", Flags);
            Assert.IsNotNull(applyMethod);
            applyMethod.Invoke(panelComp, null);
            yield return null;

            Assert.AreEqual(modeBorderless, settingsManagerType.GetProperty("DisplayMode", Flags).GetValue(sm));
            Assert.IsTrue((bool)settingsManagerType.GetProperty("IsBorderless", Flags).GetValue(sm));
            Assert.AreEqual(1, PlayerPrefs.GetInt("EcoQuest_Settings_DisplayMode", -1));

            // Simulate relaunch / scene transition into Forest Scene & back to Main Menu
            var loadForest = SceneManager.LoadSceneAsync("Forest Scene");
            while (loadForest != null && !loadForest.isDone) yield return null;
            yield return null;
            sm = settingsManagerType.GetProperty("Instance", Flags)?.GetValue(null);
            settingsManagerType.GetMethod("LoadSettings", Flags)?.Invoke(sm, null);
            settingsManagerType.GetMethod("ApplyGraphicsSettings", Flags)?.Invoke(sm, new object[] { false });
            Assert.AreEqual(modeBorderless, settingsManagerType.GetProperty("DisplayMode", Flags).GetValue(sm), "Borderless should persist cleanly into Forest Scene & across LoadSettings.");

            var loadMenuAgain = SceneManager.LoadSceneAsync("Main Menu");
            while (loadMenuAgain != null && !loadMenuAgain.isDone) yield return null;
            yield return null;

            panelComp = FindFirstSettingsPanel();
            Assert.IsNotNull(panelComp);
            panelComp.gameObject.SetActive(true);
            yield return null;
            yield return null;

            resDropdown = settingsPanelType.GetField("resolutionDropdown", Flags)?.GetValue(panelComp);
            fsToggle = settingsPanelType.GetField("fullscreenToggle", Flags)?.GetValue(panelComp);
            Assert.AreEqual(1, (int)ddValueProp.GetValue(resDropdown), "UI should reopen with Borderless selected.");
            Assert.IsTrue((bool)toggleOnProp.GetValue(fsToggle));
            Assert.IsFalse((bool)toggleInteractableProp.GetValue(fsToggle), "Fullscreen toggle must stay locked when Borderless reloads.");

            // ResetToDefaults restores Auto (Fit Screen) and unlocks Fullscreen toggle
            settingsManagerType.GetMethod("ResetToDefaults", Flags)?.Invoke(sm, null);
            settingsPanelType.GetMethod("RefreshUIFromSettings", Flags)?.Invoke(panelComp, null);
            yield return null;

            Assert.AreEqual(modeAuto, settingsManagerType.GetProperty("DisplayMode", Flags).GetValue(sm));
            Assert.AreEqual(0, (int)ddValueProp.GetValue(resDropdown), "ResetToDefaults must restore Auto (Fit Screen).");
            Assert.IsTrue((bool)toggleInteractableProp.GetValue(fsToggle));
        }

        private Component FindFirstSettingsPanel()
        {
            foreach (var obj in UnityEngine.Object.FindObjectsByType(settingsPanelType, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (obj is Component comp) return comp;
            }
            return null;
        }
    }
}
