using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace EcoQuest.Tests.EditMode
{
    public class DisplayModeSettingsEditModeTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private Type settingsManagerType;
        private Type displayModeType;

        private readonly string[] trackedIntKeys =
        {
            "EcoQuest_Settings_Quality",
            "EcoQuest_Settings_Quality_v2_Migrated",
            "EcoQuest_Settings_Fullscreen",
            "EcoQuest_Settings_ResolutionWidth",
            "EcoQuest_Settings_ResolutionHeight",
            "EcoQuest_Settings_ResolutionAuto",
            "EcoQuest_Settings_DisplayMode",
            "EcoQuest_Settings_DisplayMode_v2_Migrated",
            "EcoQuest_Settings_VSync",
            "EcoQuest_Settings_FPSLimit",
            "EcoQuest_Settings_TextSize",
            "EcoQuest_Settings_TextSize_v2_Migrated",
        };

        private bool[] hadIntKey;
        private int[] savedIntValues;
        private bool hadMasterVol, hadMusicVol, hadSfxVol, hadMuteAll, hadSens;
        private float savedMasterVol, savedMusicVol, savedSfxVol, savedSens;
        private int savedMuteAll;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            settingsManagerType = Type.GetType("SettingsManager, Assembly-CSharp", true);
            displayModeType = Type.GetType("ResolutionDisplayMode, Assembly-CSharp", true);
        }

        [SetUp]
        public void SetUp()
        {
            hadIntKey = new bool[trackedIntKeys.Length];
            savedIntValues = new int[trackedIntKeys.Length];
            for (int i = 0; i < trackedIntKeys.Length; i++)
            {
                hadIntKey[i] = PlayerPrefs.HasKey(trackedIntKeys[i]);
                savedIntValues[i] = PlayerPrefs.GetInt(trackedIntKeys[i], 0);
            }

            hadMasterVol = PlayerPrefs.HasKey("EcoQuest_Settings_MasterVol");
            savedMasterVol = PlayerPrefs.GetFloat("EcoQuest_Settings_MasterVol", 1f);
            hadMusicVol = PlayerPrefs.HasKey("EcoQuest_Settings_MusicVol");
            savedMusicVol = PlayerPrefs.GetFloat("EcoQuest_Settings_MusicVol", 0.8f);
            hadSfxVol = PlayerPrefs.HasKey("EcoQuest_Settings_SFXVol");
            savedSfxVol = PlayerPrefs.GetFloat("EcoQuest_Settings_SFXVol", 0.8f);
            hadMuteAll = PlayerPrefs.HasKey("EcoQuest_Settings_MuteAll");
            savedMuteAll = PlayerPrefs.GetInt("EcoQuest_Settings_MuteAll", 0);
            hadSens = PlayerPrefs.HasKey("EcoQuest_Settings_MouseSensitivity");
            savedSens = PlayerPrefs.GetFloat("EcoQuest_Settings_MouseSensitivity", 1f);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < trackedIntKeys.Length; i++)
            {
                if (hadIntKey[i]) PlayerPrefs.SetInt(trackedIntKeys[i], savedIntValues[i]);
                else PlayerPrefs.DeleteKey(trackedIntKeys[i]);
            }

            RestoreFloat("EcoQuest_Settings_MasterVol", hadMasterVol, savedMasterVol);
            RestoreFloat("EcoQuest_Settings_MusicVol", hadMusicVol, savedMusicVol);
            RestoreFloat("EcoQuest_Settings_SFXVol", hadSfxVol, savedSfxVol);
            if (hadMuteAll) PlayerPrefs.SetInt("EcoQuest_Settings_MuteAll", savedMuteAll);
            else PlayerPrefs.DeleteKey("EcoQuest_Settings_MuteAll");
            RestoreFloat("EcoQuest_Settings_MouseSensitivity", hadSens, savedSens);
            PlayerPrefs.Save();
        }

        private static void RestoreFloat(string key, bool hadKey, float val)
        {
            if (hadKey) PlayerPrefs.SetFloat(key, val);
            else PlayerPrefs.DeleteKey(key);
        }

        private void ClearDisplayPrefs()
        {
            PlayerPrefs.DeleteKey("EcoQuest_Settings_DisplayMode");
            PlayerPrefs.DeleteKey("EcoQuest_Settings_DisplayMode_v2_Migrated");
            PlayerPrefs.DeleteKey("EcoQuest_Settings_ResolutionAuto");
            PlayerPrefs.DeleteKey("EcoQuest_Settings_ResolutionWidth");
            PlayerPrefs.DeleteKey("EcoQuest_Settings_ResolutionHeight");
            PlayerPrefs.DeleteKey("EcoQuest_Settings_Fullscreen");
        }

        private object InvokeResolve(string modeName, bool fullscreen, int customW, int customH, Vector2Int native)
        {
            MethodInfo resolve = settingsManagerType.GetMethod("ResolveDisplayTarget", Flags);
            Assert.IsNotNull(resolve, "ResolveDisplayTarget must exist on SettingsManager.");
            object modeVal = Enum.Parse(displayModeType, modeName);
            object[] args = { modeVal, fullscreen, customW, customH, native, 0, 0, FullScreenMode.Windowed };
            resolve.Invoke(null, args);
            return args;
        }

        [Test]
        public void ResolveDisplayTarget_HandlesAutoBorderlessAndCustomModesAccurately()
        {
            Vector2Int native = new Vector2Int(1920, 1080);

            // Auto (Fit Screen): native display res in user's current fullscreen/windowed choice
            var autoFs = (object[])InvokeResolve("Auto", true, 1280, 720, native);
            Assert.AreEqual(1920, (int)autoFs[5]);
            Assert.AreEqual(1080, (int)autoFs[6]);
            Assert.AreEqual(FullScreenMode.FullScreenWindow, (FullScreenMode)autoFs[7]);

            var autoWin = (object[])InvokeResolve("Auto", false, 1280, 720, native);
            Assert.AreEqual(1920, (int)autoWin[5]);
            Assert.AreEqual(1080, (int)autoWin[6]);
            Assert.AreEqual(FullScreenMode.Windowed, (FullScreenMode)autoWin[7]);

            // Borderless: always native display res + FullScreenWindow regardless of fullscreen arg
            var borderlessFromFs = (object[])InvokeResolve("Borderless", true, 1280, 720, native);
            Assert.AreEqual(1920, (int)borderlessFromFs[5]);
            Assert.AreEqual(1080, (int)borderlessFromFs[6]);
            Assert.AreEqual(FullScreenMode.FullScreenWindow, (FullScreenMode)borderlessFromFs[7]);

            var borderlessFromWin = (object[])InvokeResolve("Borderless", false, 1280, 720, native);
            Assert.AreEqual(1920, (int)borderlessFromWin[5]);
            Assert.AreEqual(1080, (int)borderlessFromWin[6]);
            Assert.AreEqual(FullScreenMode.FullScreenWindow, (FullScreenMode)borderlessFromWin[7]);

            // Custom: exact resolution following Fullscreen checkbox (ExclusiveFullScreen vs Windowed)
            var customFs = (object[])InvokeResolve("Custom", true, 1280, 720, native);
            Assert.AreEqual(1280, (int)customFs[5]);
            Assert.AreEqual(720, (int)customFs[6]);
            Assert.AreEqual(FullScreenMode.ExclusiveFullScreen, (FullScreenMode)customFs[7]);

            var customWin = (object[])InvokeResolve("Custom", false, 1280, 720, native);
            Assert.AreEqual(1280, (int)customWin[5]);
            Assert.AreEqual(720, (int)customWin[6]);
            Assert.AreEqual(FullScreenMode.Windowed, (FullScreenMode)customWin[7]);
        }

        [Test]
        public void LoadSettings_MigratesLegacyDisplayPrefsWithoutSelectingBorderlessOrCrashing()
        {
            MethodInfo loadMethod = settingsManagerType.GetMethod("LoadSettings", Flags);
            PropertyInfo displayModeProp = settingsManagerType.GetProperty("DisplayMode", Flags);
            PropertyInfo isAutoProp = settingsManagerType.GetProperty("IsAutoResolution", Flags);
            PropertyInfo resWProp = settingsManagerType.GetProperty("ResolutionWidth", Flags);
            PropertyInfo resHProp = settingsManagerType.GetProperty("ResolutionHeight", Flags);

            object modeAuto = Enum.Parse(displayModeType, "Auto");
            object modeBorderless = Enum.Parse(displayModeType, "Borderless");
            object modeCustom = Enum.Parse(displayModeType, "Custom");

            GameObject go = new GameObject("TempSettingsManagerForEditTest");
            go.SetActive(false); // Prevent Awake/DontDestroyOnLoad in EditMode
            try
            {
                Component sm = go.AddComponent(settingsManagerType);

                // 1. Legacy Auto = 1 -> migrates to Auto
                ClearDisplayPrefs();
                PlayerPrefs.SetInt("EcoQuest_Settings_ResolutionAuto", 1);
                loadMethod.Invoke(sm, null);
                Assert.AreEqual(modeAuto, displayModeProp.GetValue(sm));
                Assert.IsTrue((bool)isAutoProp.GetValue(sm));

                // 2. Legacy Auto = 0 with valid 800x600 custom res -> migrates to Custom (never Borderless)
                ClearDisplayPrefs();
                PlayerPrefs.SetInt("EcoQuest_Settings_ResolutionAuto", 0);
                PlayerPrefs.SetInt("EcoQuest_Settings_ResolutionWidth", 800);
                PlayerPrefs.SetInt("EcoQuest_Settings_ResolutionHeight", 600);
                loadMethod.Invoke(sm, null);
                Assert.AreEqual(modeCustom, displayModeProp.GetValue(sm));
                Assert.AreEqual(800, (int)resWProp.GetValue(sm));
                Assert.AreEqual(600, (int)resHProp.GetValue(sm));

                // 3. Legacy Auto = 0 with invalid / oversized resolution -> falls back safely to Auto
                ClearDisplayPrefs();
                PlayerPrefs.SetInt("EcoQuest_Settings_ResolutionAuto", 0);
                PlayerPrefs.SetInt("EcoQuest_Settings_ResolutionWidth", 99999);
                PlayerPrefs.SetInt("EcoQuest_Settings_ResolutionHeight", 99999);
                loadMethod.Invoke(sm, null);
                Assert.AreEqual(modeAuto, displayModeProp.GetValue(sm));
                Assert.IsTrue((bool)isAutoProp.GetValue(sm));

                // 4. Corrupt out-of-range DisplayMode enum value -> falls back safely to Auto
                ClearDisplayPrefs();
                PlayerPrefs.SetInt("EcoQuest_Settings_DisplayMode_v2_Migrated", 1);
                PlayerPrefs.SetInt("EcoQuest_Settings_DisplayMode", 42);
                loadMethod.Invoke(sm, null);
                Assert.AreEqual(modeAuto, displayModeProp.GetValue(sm));

                // 5. Explicit Borderless saved value loads back cleanly as Borderless
                ClearDisplayPrefs();
                PlayerPrefs.SetInt("EcoQuest_Settings_DisplayMode_v2_Migrated", 1);
                PlayerPrefs.SetInt("EcoQuest_Settings_DisplayMode", (int)modeBorderless);
                loadMethod.Invoke(sm, null);
                Assert.AreEqual(modeBorderless, displayModeProp.GetValue(sm));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ResetToDefaults_RestoresAutoDisplayMode()
        {
            MethodInfo resetMethod = settingsManagerType.GetMethod("ResetToDefaults", Flags);
            PropertyInfo displayModeProp = settingsManagerType.GetProperty("DisplayMode", Flags);
            PropertyInfo isAutoProp = settingsManagerType.GetProperty("IsAutoResolution", Flags);
            PropertyInfo isBorderlessProp = settingsManagerType.GetProperty("IsBorderless", Flags);

            object modeAuto = Enum.Parse(displayModeType, "Auto");
            object modeBorderless = Enum.Parse(displayModeType, "Borderless");

            GameObject go = new GameObject("TempSettingsManagerForResetTest");
            go.SetActive(false);
            try
            {
                Component sm = go.AddComponent(settingsManagerType);
                PlayerPrefs.SetInt("EcoQuest_Settings_DisplayMode_v2_Migrated", 1);
                PlayerPrefs.SetInt("EcoQuest_Settings_DisplayMode", (int)modeBorderless);
                settingsManagerType.GetMethod("LoadSettings", Flags).Invoke(sm, null);
                Assert.AreEqual(modeBorderless, displayModeProp.GetValue(sm));

                resetMethod.Invoke(sm, null);
                Assert.AreEqual(modeAuto, displayModeProp.GetValue(sm));
                Assert.IsTrue((bool)isAutoProp.GetValue(sm));
                Assert.IsFalse((bool)isBorderlessProp.GetValue(sm));
                Assert.AreEqual((int)modeAuto, PlayerPrefs.GetInt("EcoQuest_Settings_DisplayMode", -1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
