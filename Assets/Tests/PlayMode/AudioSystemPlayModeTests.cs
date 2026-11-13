using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EcoQuest.Tests.PlayMode
{
    public class AudioSystemPlayModeTests
    {
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private Type audioManagerType;
        private Type settingsManagerType;
        private Type uiThemeType;

        private float savedMaster;
        private float savedMusic;
        private float savedSfx;
        private int savedMute;
        private float savedTimeScale;
        private GameObject testListenerGo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            audioManagerType = Type.GetType("AudioManager, Assembly-CSharp", true);
            settingsManagerType = Type.GetType("SettingsManager, Assembly-CSharp", true);
            uiThemeType = Type.GetType("UITheme, Assembly-CSharp", true);
        }

        [SetUp]
        public void SetUp()
        {
            savedTimeScale = Time.timeScale;
            Time.timeScale = 1f;

            savedMaster = PlayerPrefs.GetFloat("EcoQuest_Settings_MasterVol", 1f);
            savedMusic = PlayerPrefs.GetFloat("EcoQuest_Settings_MusicVol", 0.8f);
            savedSfx = PlayerPrefs.GetFloat("EcoQuest_Settings_SFXVol", 0.8f);
            savedMute = PlayerPrefs.GetInt("EcoQuest_Settings_MuteAll", 0);

            if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null)
            {
                testListenerGo = new GameObject("TestAudioListener");
                testListenerGo.AddComponent<AudioListener>();
            }
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;

            PlayerPrefs.SetFloat("EcoQuest_Settings_MasterVol", savedMaster);
            PlayerPrefs.SetFloat("EcoQuest_Settings_MusicVol", savedMusic);
            PlayerPrefs.SetFloat("EcoQuest_Settings_SFXVol", savedSfx);
            PlayerPrefs.SetInt("EcoQuest_Settings_MuteAll", savedMute);
            PlayerPrefs.Save();

            object sm = settingsManagerType.GetProperty("Instance", StaticFlags)?.GetValue(null);
            if (sm != null)
            {
                settingsManagerType.GetMethod("SetAudioSettings", InstanceFlags)?
                    .Invoke(sm, new object[] { savedMaster, savedMusic, savedSfx, savedMute == 1, false });
            }

            if (testListenerGo != null) UnityEngine.Object.Destroy(testListenerGo);
        }

        [UnityTest]
        public IEnumerator SettingsBootstrap_CreatesBuildSafeAudioManager_WithMainMixerAndUITheme()
        {
            EnsureManagersInitialized();
            yield return null;

            object am = audioManagerType.GetProperty("Instance", StaticFlags)?.GetValue(null);
            Assert.IsNotNull(am, "AudioManager.Instance should exist.");
            Assert.IsNotNull(GetField(am, "mainMixer"), "AudioManager.mainMixer should auto-load from Resources prefab.");

            object theme = uiThemeType.GetProperty("Instance", StaticFlags)?.GetValue(null);
            Assert.IsNotNull(theme, "UITheme.Instance should resolve cleanly via AudioManager template in builds.");
            Assert.IsNotNull(uiThemeType.GetField("buttonClickSound", InstanceFlags)?.GetValue(theme));
        }

        [UnityTest]
        public IEnumerator ApplyVolumes_MuteAllAndSliders_WorkWithoutDoubleAttenuation()
        {
            EnsureManagersInitialized();
            yield return null;

            object sm = settingsManagerType.GetProperty("Instance", StaticFlags)?.GetValue(null);
            object am = audioManagerType.GetProperty("Instance", StaticFlags)?.GetValue(null);
            MethodInfo setAudio = settingsManagerType.GetMethod("SetAudioSettings", InstanceFlags);

            setAudio.Invoke(sm, new object[] { 0.5f, 0.25f, 0.75f, true, false });
            yield return null;

            Assert.AreEqual(-80f, (float)GetProp(am, "CurrentMasterDb"), 0.05f);
            Assert.AreEqual(0f, AudioListener.volume, 0.001f);
            AudioSource musicSrc = GetField(am, "musicSource") as AudioSource;
            AudioSource sfxSrc = GetField(am, "sfxSource") as AudioSource;
            Assert.IsTrue(musicSrc.mute);
            Assert.IsTrue(sfxSrc.mute);

            setAudio.Invoke(sm, new object[] { 0.5f, 0.25f, 0.75f, false, false });
            yield return null;

            Assert.AreEqual(-6.02f, (float)GetProp(am, "CurrentMasterDb"), 0.1f);
            Assert.AreEqual(-12.04f, (float)GetProp(am, "CurrentMusicDb"), 0.1f);
            Assert.AreEqual(-2.50f, (float)GetProp(am, "CurrentSFXDb"), 0.1f);
            Assert.IsFalse(musicSrc.mute);
            Assert.AreEqual(1f, AudioListener.volume, 0.001f);
            Assert.AreEqual(1f, musicSrc.volume, 0.001f);
        }

        [UnityTest]
        public IEnumerator PlayWithCooldown_WorksWhilePaused_AndRateLimitsRapidCalls()
        {
            EnsureManagersInitialized();
            yield return null;

            object am = audioManagerType.GetProperty("Instance", StaticFlags)?.GetValue(null);
            AudioClip clip = GetField(am, "buttonClickClip") as AudioClip;
            Assert.IsNotNull(clip);

            Time.timeScale = 0f;
            MethodInfo playCd = audioManagerType.GetMethod("PlayWithCooldown", InstanceFlags);
            bool first = (bool)playCd.Invoke(am, new object[] { "pm_test_cd", clip, 0.25f, 0.8f, 1f, 1f, true });
            bool second = (bool)playCd.Invoke(am, new object[] { "pm_test_cd", clip, 0.25f, 0.8f, 1f, 1f, true });

            Assert.IsTrue(first, "First UI sound call should play even when Time.timeScale == 0.");
            Assert.IsFalse(second, "Rapid repeat call within cooldown window must be suppressed.");

            AudioSource[] uiPool = GetField(am, "uiPool") as AudioSource[];
            Assert.IsNotNull(uiPool);
            Assert.IsTrue(uiPool[0].ignoreListenerPause, "UI pool sources must ignore listener pause.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayAudioForScene_SelectsCorrectTracks_AndRestoresVolumeAfterFadeOut()
        {
            EnsureManagersInitialized();
            yield return null;

            object am = audioManagerType.GetProperty("Instance", StaticFlags)?.GetValue(null);
            MethodInfo playScene = audioManagerType.GetMethod("PlayAudioForScene", InstanceFlags);
            AudioSource musicSrc = GetField(am, "musicSource") as AudioSource;
            AudioSource ambSrc = GetField(am, "ambienceSource") as AudioSource;

            // Reset any background track started during InitTestScene Start() so cold-start + crossfade both get exercised.
            musicSrc.Stop();
            musicSrc.clip = null;
            ambSrc.Stop();
            ambSrc.clip = null;

            playScene.Invoke(am, new object[] { "Main Menu" });
            Assert.AreEqual(GetField(am, "bgmMainMenu"), musicSrc.clip);
            Assert.IsNull(ambSrc.clip);

            playScene.Invoke(am, new object[] { "Forest Scene" });
            yield return new WaitForSecondsRealtime(1.0f);
            Assert.AreEqual(GetField(am, "bgmForest"), musicSrc.clip);
            Assert.AreEqual(GetField(am, "ambForest"), ambSrc.clip);

            // Simulate TravelGate lowering musicSource.volume to 0 before City load
            musicSrc.Stop();
            musicSrc.volume = 0f;
            playScene.Invoke(am, new object[] { "City Scene" });
            yield return null;
            Assert.AreEqual(GetField(am, "bgmCity"), musicSrc.clip);
            Assert.AreEqual(GetField(am, "ambCity"), ambSrc.clip);
            Assert.Greater(musicSrc.volume, 0.9f, "PlayMusic must restore musicSource.volume after scene transition fade.");
        }

        private void EnsureManagersInitialized()
        {
            if (settingsManagerType.GetProperty("Instance", StaticFlags)?.GetValue(null) == null)
            {
                new GameObject("SettingsManager").AddComponent(settingsManagerType);
            }
            object sm = settingsManagerType.GetProperty("Instance", StaticFlags)?.GetValue(null);
            settingsManagerType.GetMethod("EnsureAudioManagerExists", InstanceFlags)?.Invoke(sm, null);
        }

        private object GetField(object obj, string name) => obj.GetType().GetField(name, InstanceFlags)?.GetValue(obj);
        private object GetProp(object obj, string name) => obj.GetType().GetProperty(name, InstanceFlags)?.GetValue(obj);
    }
}
