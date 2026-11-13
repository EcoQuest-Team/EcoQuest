using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EcoQuest.Tests.PlayMode
{
    public class TutorialFlowRegressionPlayModeTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly HashSet<string> FloatKeys = new HashSet<string>
        {
            "EcoQuest_City_BestTime",
            "EcoQuest_City_RobinCooldownRemaining",
            "EcoQuest_Player_HungerRemaining",
            "EcoQuest_Player_SpeedBuffRemaining",
            "EcoQuest_Player_SpeedBuffDuration",
            "EcoQuest_Player_SpeedBuffPercent",
            "EcoQuest_Player_StaminaBuffRemaining",
            "EcoQuest_Player_StaminaBuffDuration",
            "EcoQuest_Player_StaminaBuffPercent"
        };

        private static readonly HashSet<string> StringKeys = new HashSet<string>
        {
            "EcoQuest_SeenInfoCards",
            "EcoQuest_LastLoadingTipText",
            "EcoQuest_GuardianCooldownUntil",
            "EcoQuest_RobinCooldownUntil",
            "EcoQuest_CityTip_Checklist",
            "EcoQuest_CityTip_Hint",
            "EcoQuest_Inventory_Slots",
            "EcoQuest_Inventory_SeenNew"
        };

        private Type saveKeysType;
        private Type orchestratorType;
        private Type uiElementType;
        private Type sequentialTutorialType;
        private Type npcType;
        private Type minimapType;
        private Type trashType;
        private Type questType;
        private Type pauseType;
        private Type blurType;

        private string[] progressKeys;
        private readonly Dictionary<string, int> backedUpInts = new Dictionary<string, int>();
        private readonly Dictionary<string, float> backedUpFloats = new Dictionary<string, float>();
        private readonly Dictionary<string, string> backedUpStrings = new Dictionary<string, string>();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            saveKeysType = Type.GetType("EcoQuestSaveKeys, Assembly-CSharp", true);
            orchestratorType = Type.GetType("UIOrchestrator, Assembly-CSharp", true);
            uiElementType = orchestratorType.GetNestedType("UIElement", Flags);
            Assert.IsNotNull(uiElementType, "UIOrchestrator.UIElement nested enum should exist.");
            sequentialTutorialType = Type.GetType("SequentialWASDTutorial, Assembly-CSharp", true);
            npcType = Type.GetType("NPCInteraction, Assembly-CSharp", true);
            minimapType = Type.GetType("MinimapController, Assembly-CSharp", true);
            trashType = Type.GetType("TrashItem, Assembly-CSharp", true);
            questType = Type.GetType("QuestManager, Assembly-CSharp", true);
            pauseType = Type.GetType("PauseMenuController, Assembly-CSharp", true);
            blurType = Type.GetType("UIBackgroundBlur, Assembly-CSharp", true);

            progressKeys = (string[])saveKeysType.GetField("AllTutorialProgressKeys", Flags).GetValue(null);
        }

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            BackupProgressPrefs();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            RestoreProgressPrefs();
        }

        [UnityTest]
        public IEnumerator FreshStart_MainMenuThenForestScene_StartsInTutorialWithQuestHiddenAndTrashLocked()
        {
            ResetProgress();

            yield return LoadSceneAndWait("Main Menu", 3);
            yield return LoadSceneAndWait("Forest Scene", 5);

            object orchestrator = GetOrchestratorInstance();
            Assert.AreEqual("Tutorial", GetFlowStepName(orchestrator), "Fresh start must enter FlowStep.Tutorial in Forest Scene.");
            Assert.AreEqual("Tutorial", GetPhaseName(orchestrator), "Fresh start must enter GamePhase.Tutorial in Forest Scene.");
            Assert.IsFalse(GetExpectedVisibility(orchestrator, "QuestPanel"), "QuestPanel must stay hidden at start of Forest Scene tutorial.");

            bool trashAllowed = (bool)trashType.GetProperty("AreTrashItemsAllowed", Flags).GetValue(null);
            Assert.IsFalse(trashAllowed, "Trash items must remain locked during initial Forest tutorial.");

            object tut = sequentialTutorialType.GetProperty("Instance", Flags).GetValue(null);
            Assert.IsNotNull(tut, "SequentialWASDTutorial.Instance should exist in Forest Scene.");
            bool isCompleted = (bool)sequentialTutorialType.GetProperty("IsCompleted", Flags).GetValue(tut);
            Assert.IsFalse(isCompleted, "SequentialWASDTutorial.IsCompleted must be false on fresh start.");

            int sortedWaste = (int)questType.GetMethod("GetTotalSortedWaste", Flags).Invoke(null, null);
            Assert.AreEqual(0, sortedWaste, "QuestManager.GetTotalSortedWaste should reset to 0 on fresh Forest load.");
        }

        [UnityTest]
        public IEnumerator ReturningMidQuest_ForestScene_UnlocksTutorialAndEnablesQuestTrash()
        {
            ResetProgress();
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);
            PlayerPrefs.SetInt("EcoQuest_ForestTrashQuestDone", 0);
            PlayerPrefs.SetInt("EcoQuest_ForestQuestAccepted", 1);
            PlayerPrefs.SetInt("EcoQuest_PreQuizScore", 3);
            PlayerPrefs.Save();

            yield return LoadSceneAndWait("Forest Scene", 5);

            object tut = sequentialTutorialType.GetProperty("Instance", Flags).GetValue(null);
            Assert.IsNotNull(tut, "SequentialWASDTutorial.Instance should exist in Forest Scene.");
            bool isCompleted = (bool)sequentialTutorialType.GetProperty("IsCompleted", Flags).GetValue(tut);
            Assert.IsTrue(isCompleted, "Returning mid-quest player must have SequentialWASDTutorial.IsCompleted == true.");

            var npcs = UnityEngine.Object.FindObjectsByType(npcType, FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.IsNotEmpty(npcs, "Forest Scene must have at least one NPCInteraction.");
            MethodInfo isLockedMethod = npcType.GetMethod("IsTutorialLocked", Flags);
            foreach (var npc in npcs)
            {
                bool locked = (bool)isLockedMethod.Invoke(npc, null);
                Assert.IsFalse(locked, "Returning mid-quest player must not have NPCInteraction locked.");
            }

            bool mapUnlocked = (bool)minimapType.GetProperty("IsMapTutorialUnlocked", Flags).GetValue(null);
            Assert.IsTrue(mapUnlocked, "Returning mid-quest player must have MinimapController.IsMapTutorialUnlocked == true.");

            object orchestrator = GetOrchestratorInstance();
            Assert.AreEqual("FreePlay", GetFlowStepName(orchestrator), "Returning mid-quest must enter FlowStep.FreePlay.");
            Assert.AreEqual("QuestActive", GetPhaseName(orchestrator), "Returning mid-quest must enter GamePhase.QuestActive.");

            bool trashAllowed = (bool)trashType.GetProperty("AreTrashItemsAllowed", Flags).GetValue(null);
            Assert.IsTrue(trashAllowed, "Returning mid-quest must allow trash collection.");
        }

        [UnityTest]
        public IEnumerator ReturningPostQuest_ForestScene_StartsInFreePlayWithQuestHiddenAndTrashLocked()
        {
            ResetProgress();
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);
            PlayerPrefs.SetInt("EcoQuest_ForestTrashQuestDone", 1);
            PlayerPrefs.SetInt("EcoQuest_ForestQuestAccepted", 0);
            PlayerPrefs.SetInt("EcoQuest_PreQuizScore", 3);
            PlayerPrefs.Save();

            yield return LoadSceneAndWait("Forest Scene", 5);

            object orchestrator = GetOrchestratorInstance();
            Assert.AreEqual("FreePlay", GetFlowStepName(orchestrator), "Returning post-quest must enter FlowStep.FreePlay.");
            Assert.AreEqual("FreePlay", GetPhaseName(orchestrator), "Returning post-quest with completed trash quest must stay in GamePhase.FreePlay.");
            Assert.IsFalse(GetExpectedVisibility(orchestrator, "QuestPanel"), "QuestPanel must be hidden after Forest trash quest is done.");

            bool trashAllowed = (bool)trashType.GetProperty("AreTrashItemsAllowed", Flags).GetValue(null);
            Assert.IsFalse(trashAllowed, "Trash collection must be locked once Forest trash quest is complete.");
        }

        [UnityTest]
        public IEnumerator PauseAndResume_ManagesFullSceneBlurOwnershipAndMinimapVisibility()
        {
            ResetProgress();
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);
            PlayerPrefs.SetInt("EcoQuest_ForestTrashQuestDone", 1);
            PlayerPrefs.SetInt("EcoQuest_ForestQuestAccepted", 0);
            PlayerPrefs.Save();

            yield return LoadSceneAndWait("Forest Scene", 5);

            var pauseCtrl = UnityEngine.Object.FindFirstObjectByType(pauseType, FindObjectsInactive.Include);
            Assert.IsNotNull(pauseCtrl, "PauseMenuController should exist in Forest Scene.");

            object orchestrator = GetOrchestratorInstance();
            PropertyInfo isFullBlurProp = blurType.GetProperty("IsFullSceneBlurActive", Flags);
            PropertyInfo isPausedProp = pauseType.GetProperty("IsPaused", Flags);

            Assert.IsFalse((bool)isFullBlurProp.GetValue(null), "Full-scene blur should be inactive before pausing.");
            Assert.IsTrue(GetExpectedVisibility(orchestrator, "Minimap"), "Minimap should be expected visible before pausing.");

            pauseType.GetMethod("PauseGame", Flags).Invoke(pauseCtrl, null);
            yield return WaitUnscaledSeconds(0.35f);

            Assert.IsTrue((bool)isPausedProp.GetValue(null), "PauseMenuController.IsPaused should be true after PauseGame().");
            Assert.IsTrue((bool)isFullBlurProp.GetValue(null), "UIBackgroundBlur.IsFullSceneBlurActive should be true while paused.");
            Assert.IsFalse(GetExpectedVisibility(orchestrator, "Minimap"), "Minimap should be expected hidden while paused.");

            pauseType.GetMethod("ResumeGame", Flags).Invoke(pauseCtrl, null);
            float waitElapsed = 0f;
            while ((bool)isPausedProp.GetValue(null) && waitElapsed < 2.0f)
            {
                waitElapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return WaitUnscaledSeconds(0.05f);

            Assert.IsFalse((bool)isPausedProp.GetValue(null), "PauseMenuController.IsPaused should be false after ResumeGame().");
            Assert.IsFalse((bool)isFullBlurProp.GetValue(null), "UIBackgroundBlur.IsFullSceneBlurActive should clear after ResumeGame().");
            Assert.IsTrue(GetExpectedVisibility(orchestrator, "Minimap"), "Minimap should be expected visible again after ResumeGame().");
        }

        private void ResetProgress()
        {
            saveKeysType.GetMethod("ResetAllPlayerProgress", Flags).Invoke(null, null);
            PlayerPrefs.Save();
        }

        private IEnumerator LoadSceneAndWait(string sceneName, int extraFrames)
        {
            var load = SceneManager.LoadSceneAsync(sceneName);
            Assert.IsNotNull(load, $"Failed to start async load of scene '{sceneName}'.");
            while (!load.isDone)
            {
                yield return null;
            }
            for (int i = 0; i < extraFrames; i++)
            {
                yield return null;
            }
        }

        private IEnumerator WaitUnscaledSeconds(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return null;
        }

        private object GetOrchestratorInstance()
        {
            object inst = orchestratorType.GetProperty("Instance", Flags).GetValue(null);
            Assert.IsNotNull(inst, "UIOrchestrator.Instance should exist.");
            return inst;
        }

        private string GetFlowStepName(object orchestrator)
        {
            return orchestratorType.GetProperty("CurrentFlowStep", Flags).GetValue(orchestrator)?.ToString();
        }

        private string GetPhaseName(object orchestrator)
        {
            return orchestratorType.GetProperty("CurrentPhase", Flags).GetValue(orchestrator)?.ToString();
        }

        private bool GetExpectedVisibility(object orchestrator, string elementName)
        {
            object enumVal = Enum.Parse(uiElementType, elementName);
            return (bool)orchestratorType.GetMethod("GetExpectedVisibility", Flags).Invoke(orchestrator, new[] { enumVal });
        }

        private void BackupProgressPrefs()
        {
            backedUpInts.Clear();
            backedUpFloats.Clear();
            backedUpStrings.Clear();

            foreach (string key in progressKeys)
            {
                if (!PlayerPrefs.HasKey(key)) continue;
                if (FloatKeys.Contains(key)) backedUpFloats[key] = PlayerPrefs.GetFloat(key);
                else if (StringKeys.Contains(key)) backedUpStrings[key] = PlayerPrefs.GetString(key);
                else backedUpInts[key] = PlayerPrefs.GetInt(key);
            }
        }

        private void RestoreProgressPrefs()
        {
            saveKeysType.GetMethod("ResetAllPlayerProgress", Flags).Invoke(null, null);

            foreach (var kv in backedUpInts) PlayerPrefs.SetInt(kv.Key, kv.Value);
            foreach (var kv in backedUpFloats) PlayerPrefs.SetFloat(kv.Key, kv.Value);
            foreach (var kv in backedUpStrings) PlayerPrefs.SetString(kv.Key, kv.Value);
            PlayerPrefs.Save();
        }
    }
}
