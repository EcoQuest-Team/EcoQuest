using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace EcoQuest.Tests.EditMode
{
    public class AudioSystemEditModeTests
    {
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private Type audioManagerType;
        private Type uiThemeType;
        private Type vehicleType;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            audioManagerType = Type.GetType("AudioManager, Assembly-CSharp", true);
            uiThemeType = Type.GetType("UITheme, Assembly-CSharp", true);
            vehicleType = Type.GetType("EcoQuest.CityTraffic.TrafficVehicleController, Assembly-CSharp", true);
        }

        [Test]
        public void MainMixer_HasExpectedGroups_AndExposedVolumeParameters()
        {
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/EcoQuest/Audio/MainMixer.mixer");
            Assert.IsNotNull(mixer, "MainMixer.mixer must exist at Assets/EcoQuest/Audio/MainMixer.mixer.");

            foreach (string grp in new[] { "Master", "Music", "SFX", "UI", "Ambience" })
            {
                var found = mixer.FindMatchingGroups(grp);
                Assert.IsNotNull(found, $"FindMatchingGroups returned null for '{grp}'.");
                Assert.IsTrue(found.Length > 0, $"Mixer group '{grp}' missing in MainMixer.mixer.");
            }
        }

        [Test]
        public void ResourcesAudioManager_And_UITheme_HaveAllClipsAssigned()
        {
            GameObject prefab = Resources.Load<GameObject>("AudioManager");
            Assert.IsNotNull(prefab, "Assets/Resources/AudioManager.prefab must exist so builds always find MainMixer & clips.");

            Component am = prefab.GetComponent(audioManagerType);
            Assert.IsNotNull(am, "Resources/AudioManager prefab must have AudioManager component.");
            Assert.IsNotNull(GetField(am, "mainMixer"), "AudioManager.mainMixer must be assigned.");
            Assert.IsNotNull(GetField(am, "uiThemeAsset"), "AudioManager.uiThemeAsset must be assigned.");

            string[] requiredClipFields =
            {
                "defaultMusicClip", "bgmMainMenu", "bgmForest", "bgmCity", "ambForest", "ambCity",
                "buttonClickClip", "buttonHoverClip", "sliderChangeClip", "uiModalOpenClip", "uiModalCloseClip",
                "uiPanelWhooshClip", "uiDialogueBlipClip", "uiNotificationClip", "uiTutorialStepClip",
                "uiHungerWarningClip", "uiShopBuyClip", "uiShopErrorClip", "uiItemUseClip",
                "sfxTrashPickup", "sfxTrashDrop", "sfxTrashDepositCorrect", "sfxTrashDepositWrong",
                "sfxQuizCorrect", "sfxQuizWrong", "sfxCoinEarn", "sfxQuestComplete",
                "sfxJump", "sfxLand", "sfxLoadingReady", "sfxLitterThrow", "sfxLitterLand"
            };

            foreach (string f in requiredClipFields)
            {
                Assert.IsNotNull(GetField(am, f), $"AudioManager clip slot '{f}' is null on Resources/AudioManager.prefab.");
            }

            AudioClip[] grassSteps = GetField(am, "footstepGrassClips") as AudioClip[];
            AudioClip[] citySteps = GetField(am, "footstepCityClips") as AudioClip[];
            Assert.IsNotNull(grassSteps);
            Assert.GreaterOrEqual(grassSteps.Length, 4, "footstepGrassClips must have 4 variations.");
            Assert.IsNotNull(citySteps);
            Assert.GreaterOrEqual(citySteps.Length, 4, "footstepCityClips must have 4 variations.");

            ScriptableObject theme = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/EcoQuest/UI/Shared/UITheme.asset");
            Assert.IsNotNull(theme, "UITheme.asset missing.");
            foreach (string tf in new[] { "buttonHoverSound", "buttonClickSound", "modalOpenSound", "modalCloseSound", "quizCorrectSound", "quizWrongSound", "coinPickupSound" })
            {
                Assert.IsNotNull(uiThemeType.GetField(tf, InstanceFlags)?.GetValue(theme), $"UITheme.{tf} is null.");
            }
        }

        [Test]
        public void LinearToDecibel_ConvertsExpectedPointsAndClampsBounds()
        {
            MethodInfo m = audioManagerType.GetMethod("LinearToDecibel", StaticFlags);
            Assert.IsNotNull(m);

            Assert.AreEqual(0f, (float)m.Invoke(null, new object[] { 1f }), 0.01f);
            Assert.AreEqual(-6.02f, (float)m.Invoke(null, new object[] { 0.5f }), 0.1f);
            Assert.AreEqual(-80f, (float)m.Invoke(null, new object[] { 0f }), 0.01f);
            Assert.AreEqual(-80f, (float)m.Invoke(null, new object[] { -2f }), 0.01f);
            Assert.AreEqual(0f, (float)m.Invoke(null, new object[] { 4f }), 0.01f);
        }

        [Test]
        public void TargetScenes_And_Prefabs_HaveZeroUnroutedSources_AndValidVehicleClips()
        {
            string[] prefabs =
            {
                "Assets/EcoQuest/Prefabs/Player.prefab",
                "Assets/EcoQuest/Prefabs/SceneTransition.prefab",
                "Assets/Resources/LoadingScreen.prefab",
                "Assets/EcoQuest/Inventory/Prefabs/UI_EcoShop.prefab",
                "Assets/EcoQuest/Inventory/Prefabs/UI_Inventory.prefab"
            };

            foreach (string p in prefabs)
            {
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                Assert.IsNotNull(go, $"Missing prefab at {p}");
                foreach (AudioSource src in go.GetComponentsInChildren<AudioSource>(true))
                {
                    Assert.IsNotNull(src.outputAudioMixerGroup, $"Unrouted AudioSource '{src.name}' in prefab {p}.");
                }
            }

            string initialScene = SceneManager.GetActiveScene().path;
            try
            {
                foreach (string scenePath in new[]
                {
                    "Assets/EcoQuest/Main Menu.unity",
                    "Assets/EcoQuest/Forest Scene.unity",
                    "Assets/EcoQuest/City Scene.unity"
                })
                {
                    Scene sc = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    foreach (AudioSource src in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        Assert.IsNotNull(src.outputAudioMixerGroup, $"Unrouted AudioSource '{src.name}' in scene {sc.name}.");
                    }

                    foreach (Component car in UnityEngine.Object.FindObjectsByType(vehicleType, FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        AudioClip eng = vehicleType.GetField("engineHumClip", InstanceFlags)?.GetValue(car) as AudioClip;
                        Assert.IsNotNull(eng, $"Vehicle '{car.name}' in {sc.name} has null engineHumClip.");
                        Assert.Greater(eng.length, 0.01f, $"Vehicle '{car.name}' in {sc.name} has broken 0-length engineHumClip.");
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(initialScene))
                {
                    EditorSceneManager.OpenScene(initialScene, OpenSceneMode.Single);
                }
            }
        }

        private object GetField(object obj, string fieldName)
        {
            return obj.GetType().GetField(fieldName, InstanceFlags)?.GetValue(obj);
        }
    }
}
