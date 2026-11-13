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

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            settingsManagerType = Type.GetType("SettingsManager, Assembly-CSharp", true);
            settingsPanelType = Type.GetType("SettingsPanelUI, Assembly-CSharp", true);
        }

        [SetUp]
        public void SetUp()
        {
            savedQuality = PlayerPrefs.GetInt("EcoQuest_Settings_Quality", 2);
            savedVsync = PlayerPrefs.GetInt("EcoQuest_Settings_VSync", 1);
            savedFps = PlayerPrefs.GetInt("EcoQuest_Settings_FPSLimit", -1);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.SetInt("EcoQuest_Settings_Quality", savedQuality);
            PlayerPrefs.SetInt("EcoQuest_Settings_VSync", savedVsync);
            PlayerPrefs.SetInt("EcoQuest_Settings_FPSLimit", savedFps);
            PlayerPrefs.Save();

            object sm = settingsManagerType.GetProperty("Instance", Flags)?.GetValue(null);
            if (sm != null)
            {
                settingsManagerType.GetMethod("LoadSettings", Flags)?.Invoke(sm, null);
                settingsManagerType.GetMethod("ApplyGraphicsSettings", Flags)?.Invoke(sm, new object[] { false });
            }
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
    }
}
