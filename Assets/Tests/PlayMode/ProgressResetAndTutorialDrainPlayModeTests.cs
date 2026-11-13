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
    public class ProgressResetAndTutorialDrainPlayModeTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private Type saveKeysType;
        private Type currencyType;
        private Type survivalStatsType;
        private Type inventoryCtrlType;
        private Type inventoryServiceType;
        private Type itemDatabaseType;
        private Type itemDefType;
        private Type mainMenuType;
        private Type sequentialTutorialType;

        private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();
        private readonly List<Scene> createdTempScenes = new List<Scene>();
        private Scene originalActiveScene;

        private string[] progressKeys;
        private readonly Dictionary<string, string> savedStringPrefs = new Dictionary<string, string>();
        private readonly Dictionary<string, int> savedIntPrefs = new Dictionary<string, int>();
        private readonly Dictionary<string, float> savedFloatPrefs = new Dictionary<string, float>();
        private bool hadMasterVolSetting;
        private float savedMasterVolSetting;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            saveKeysType = Type.GetType("EcoQuestSaveKeys, Assembly-CSharp", true);
            currencyType = Type.GetType("PlayerCurrency, Assembly-CSharp", true);
            survivalStatsType = Type.GetType("PlayerSurvivalStats, Assembly-CSharp", true);
            inventoryCtrlType = Type.GetType("EcoQuest.Inventory.InventoryController, Assembly-CSharp", true);
            inventoryServiceType = Type.GetType("EcoQuest.Inventory.InventoryService, Assembly-CSharp", true);
            itemDatabaseType = Type.GetType("EcoQuest.Inventory.InventoryItemDatabase, Assembly-CSharp", true);
            itemDefType = Type.GetType("EcoQuest.Inventory.InventoryItemDef, Assembly-CSharp", true);
            mainMenuType = Type.GetType("MainMenuManager, Assembly-CSharp", true);
            sequentialTutorialType = Type.GetType("SequentialWASDTutorial, Assembly-CSharp", true);

            progressKeys = (string[])saveKeysType.GetField("AllTutorialProgressKeys", StaticFlags).GetValue(null);
        }

        [SetUp]
        public void SetUp()
        {
            createdObjects.Clear();
            createdTempScenes.Clear();
            originalActiveScene = SceneManager.GetActiveScene();
            BackupPrefs();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
                }
            }
            createdObjects.Clear();

            if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(originalActiveScene);
            }

            RestorePrefs();
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            for (int i = createdTempScenes.Count - 1; i >= 0; i--)
            {
                Scene s = createdTempScenes[i];
                if (s.IsValid() && s.isLoaded && s != originalActiveScene)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(s);
                    while (unload != null && !unload.isDone)
                    {
                        yield return null;
                    }
                }
            }
            createdTempScenes.Clear();
        }

        [UnityTest]
        public IEnumerator FreshInstallAndClearedProgress_StartsAtZeroWithCleanDefaults()
        {
            // Seed dirty playtest values and a player setting
            PlayerPrefs.SetInt("PlayerGold", 308);
            PlayerPrefs.SetInt("PlayerCoins", 308);
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);
            PlayerPrefs.SetInt("SequentialWASD_TutorialCompleted", 1);
            PlayerPrefs.SetInt("EcoQuest_ForestQuestAccepted", 1);
            PlayerPrefs.SetInt("EcoQuest_ForestTrashQuestDone", 1);
            PlayerPrefs.SetString("EcoQuest_Inventory_Slots", "food_apple:5");
            PlayerPrefs.SetFloat("EcoQuest_Player_HungerRemaining", 25f);
            PlayerPrefs.SetFloat("EcoQuest_Settings_MasterVol", 0.77f);
            PlayerPrefs.Save();

            // Run the single centralized reset path
            int removed = (int)saveKeysType.GetMethod("ResetAllPlayerProgress", StaticFlags).Invoke(null, null);
            Assert.Greater(removed, 0, "ResetAllPlayerProgress should delete dirty progress keys.");

            // Initialize fresh runtime components
            var currencyGo = new GameObject("TestPlayerCurrency");
            createdObjects.Add(currencyGo);
            var currency = currencyGo.AddComponent(currencyType);

            var statsGo = new GameObject("TestPlayerSurvivalStats");
            createdObjects.Add(statsGo);
            var stats = statsGo.AddComponent(survivalStatsType);

            var ctrlGo = new GameObject("TestInventoryController");
            createdObjects.Add(ctrlGo);
            var ctrl = ctrlGo.AddComponent(inventoryCtrlType);
            bool giveTestItemsOnStart = (bool)inventoryCtrlType.GetField("giveTestItemsOnStart", InstanceFlags).GetValue(ctrl);
            Assert.IsFalse(giveTestItemsOnStart, "InventoryController.giveTestItemsOnStart must default to false for clean new players.");

            object db = CreateRuntimeDatabaseWithApple();
            object invService = Activator.CreateInstance(inventoryServiceType, db, 24);

            var menuGo = new GameObject("TestMainMenuManager");
            createdObjects.Add(menuGo);
            var menu = menuGo.AddComponent(mainMenuType);
            string startScene = (string)mainMenuType.GetField("gameSceneName", InstanceFlags).GetValue(menu);

            yield return null;

            Assert.AreEqual(0, (int)currencyType.GetProperty("CurrentGold", InstanceFlags).GetValue(currency), "Coins must start at 0.");
            Assert.AreEqual(0, PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0), "Tutorial Step 1 requires TabTipDone == 0.");
            Assert.AreEqual(0, PlayerPrefs.GetInt("SequentialWASD_TutorialCompleted", 0), "Sequential WASD tutorial must not be pre-completed.");
            Assert.AreEqual(0, PlayerPrefs.GetInt("EcoQuest_ForestQuestAccepted", 0), "Forest quest must not be accepted.");
            Assert.AreEqual(0, PlayerPrefs.GetInt("EcoQuest_ForestTrashQuestDone", 0), "Forest quest must not be completed.");
            Assert.AreEqual(0, (int)inventoryServiceType.GetMethod("GetTotalItemCount", InstanceFlags).Invoke(invService, null), "Inventory/Eco Shop purchases must start empty.");
            Assert.AreEqual(100f, (float)survivalStatsType.GetField("currentHunger", InstanceFlags).GetValue(stats), 0.01f, "Hunger must start at 100.");
            Assert.AreEqual(100f, (float)survivalStatsType.GetField("currentStamina", InstanceFlags).GetValue(stats), 0.01f, "Stamina must start at 100.");
            Assert.AreEqual("Forest Scene", startScene, "Main menu Play scene must point to Forest Scene.");
            Assert.AreEqual(0.77f, PlayerPrefs.GetFloat("EcoQuest_Settings_MasterVol", -1f), 0.001f, "Player settings must be preserved when progress resets.");
        }

        [UnityTest]
        public IEnumerator SecondLaunchAfterPlaying_LoadsSavedProgress_UntilExplicitReset()
        {
            saveKeysType.GetMethod("ResetAllPlayerProgress", StaticFlags).Invoke(null, null);

            // Session 1: earn coins, acquire an apple, mark progress, drain some hunger
            var currencyGo1 = new GameObject("TestCurrencySession1");
            createdObjects.Add(currencyGo1);
            var currency1 = currencyGo1.AddComponent(currencyType);
            currencyType.GetMethod("AddGold", InstanceFlags).Invoke(currency1, new object[] { 75 });

            object db = CreateRuntimeDatabaseWithApple();
            object inv1 = Activator.CreateInstance(inventoryServiceType, db, 24);
            inventoryServiceType.GetMethod("AddItem", InstanceFlags).Invoke(inv1, new object[] { "food_apple", 2 });

            var statsGo1 = new GameObject("TestStatsSession1");
            createdObjects.Add(statsGo1);
            var stats1 = statsGo1.AddComponent(survivalStatsType);
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);
            survivalStatsType.GetMethod("StepHungerDrain", InstanceFlags).Invoke(stats1, new object[] { 30f, false });
            float savedHungerAfterSession1 = (float)survivalStatsType.GetField("currentHunger", InstanceFlags).GetValue(stats1);
            Assert.Less(savedHungerAfterSession1, 100f);

            UnityEngine.Object.DestroyImmediate(currencyGo1);
            UnityEngine.Object.DestroyImmediate(statsGo1);
            yield return null;

            // Session 2: fresh components should load persisted progress
            var currencyGo2 = new GameObject("TestCurrencySession2");
            createdObjects.Add(currencyGo2);
            var currency2 = currencyGo2.AddComponent(currencyType);
            object inv2 = Activator.CreateInstance(inventoryServiceType, db, 24);
            var statsGo2 = new GameObject("TestStatsSession2");
            createdObjects.Add(statsGo2);
            var stats2 = statsGo2.AddComponent(survivalStatsType);

            yield return null;

            Assert.AreEqual(75, (int)currencyType.GetProperty("CurrentGold", InstanceFlags).GetValue(currency2), "Saved coins should persist on second launch.");
            Assert.AreEqual(2, (int)inventoryServiceType.GetMethod("GetTotalItemCount", InstanceFlags).Invoke(inv2, null), "Saved inventory items should persist on second launch.");
            Assert.AreEqual(1, PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0), "Saved tutorial progress should persist on second launch.");
            Assert.AreEqual(savedHungerAfterSession1, (float)survivalStatsType.GetField("currentHunger", InstanceFlags).GetValue(stats2), 0.05f, "Saved hunger should persist on second launch.");

            // Explicit reset clears runtime + saved progress back to zero
            saveKeysType.GetMethod("ResetAllPlayerProgress", StaticFlags).Invoke(null, null);
            Assert.AreEqual(0, (int)currencyType.GetProperty("CurrentGold", InstanceFlags).GetValue(currency2));
            Assert.AreEqual(0, (int)inventoryServiceType.GetMethod("GetTotalItemCount", InstanceFlags).Invoke(inv2, null));
            Assert.AreEqual(100f, (float)survivalStatsType.GetField("currentHunger", InstanceFlags).GetValue(stats2), 0.01f);
            Assert.AreEqual(0, PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0));
        }

        [UnityTest]
        public IEnumerator ForestTutorialDrain_IsPointTwoTimesInTutorial_AndOneTimesAfterOrInCity()
        {
            saveKeysType.GetMethod("ResetAllPlayerProgress", StaticFlags).Invoke(null, null);

            Scene forestScene = GetOrCreateAndActivateScene("Forest Scene");
            var statsGo = new GameObject("TestTutorialStats");
            SceneManager.MoveGameObjectToScene(statsGo, forestScene);
            createdObjects.Add(statsGo);
            var stats = statsGo.AddComponent(survivalStatsType);

            // Clear singleton reference to SequentialWASDTutorial if another test left one completed
            PropertyInfo tutInstProp = sequentialTutorialType.GetProperty("Instance", StaticFlags);
            object activeTut = tutInstProp != null ? tutInstProp.GetValue(null) : null;
            if (activeTut != null)
            {
                sequentialTutorialType.GetField("isCompleted", InstanceFlags)?.SetValue(activeTut, false);
            }

            yield return null;

            float multDefault = (float)survivalStatsType.GetField("tutorialDrainMultiplier", InstanceFlags).GetValue(stats);
            Assert.AreEqual(0.2f, multDefault, 0.0001f, "Default tutorialDrainMultiplier must be 0.2f (5x slower).");
            Assert.IsTrue((bool)survivalStatsType.GetMethod("IsTutorialDrainSlowdownActive", InstanceFlags).Invoke(stats, null),
                "Tutorial drain slowdown must be active in Forest Scene before tutorial completion.");
            Assert.AreEqual(0.2f, (float)survivalStatsType.GetMethod("GetEffectiveTutorialDrainMultiplier", InstanceFlags).Invoke(stats, null), 0.0001f);

            // Passive hunger drain over 15s: normal = 1.0, tutorial (0.2x) = 0.2
            survivalStatsType.GetMethod("StepHungerDrain", InstanceFlags).Invoke(stats, new object[] { 15f, false });
            float hungerAfterTutorialDrain = (float)survivalStatsType.GetField("currentHunger", InstanceFlags).GetValue(stats);
            Assert.AreEqual(99.8f, hungerAfterTutorialDrain, 0.01f, "Hunger should drain 5x slower (0.2 instead of 1.0 over 15s) during Forest tutorial.");

            // Stamina sprint drain over 1s: normal = 22.0, tutorial (0.2x) = 4.4
            survivalStatsType.GetMethod("SetSprinting", InstanceFlags).Invoke(stats, new object[] { true });
            survivalStatsType.GetMethod("StepStaminaUpdate", InstanceFlags).Invoke(stats, new object[] { 1f });
            float staminaAfterTutorialSprint = (float)survivalStatsType.GetField("currentStamina", InstanceFlags).GetValue(stats);
            Assert.AreEqual(95.6f, staminaAfterTutorialSprint, 0.01f, "Sprint stamina should drain 5x slower (4.4 instead of 22 over 1s) during Forest tutorial.");

            // Stamina regen must remain unchanged at 1.0x (18/s after 0.5s delay)
            survivalStatsType.GetMethod("SetSprinting", InstanceFlags).Invoke(stats, new object[] { false });
            survivalStatsType.GetMethod("StepStaminaUpdate", InstanceFlags).Invoke(stats, new object[] { 0.6f });
            float beforeRegen = (float)survivalStatsType.GetField("currentStamina", InstanceFlags).GetValue(stats);
            survivalStatsType.GetMethod("StepStaminaUpdate", InstanceFlags).Invoke(stats, new object[] { 0.2f });
            float afterRegen = (float)survivalStatsType.GetField("currentStamina", InstanceFlags).GetValue(stats);
            Assert.AreEqual(3.6f, afterRegen - beforeRegen, 0.05f, "Stamina regen rate must stay unchanged during tutorial.");

            // Complete tutorial inside Forest Scene -> drain immediately returns to 1.0x
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);
            Assert.IsFalse((bool)survivalStatsType.GetMethod("IsTutorialDrainSlowdownActive", InstanceFlags).Invoke(stats, null),
                "Slowdown must stop once tutorial is complete even in Forest Scene.");
            Assert.AreEqual(1.0f, (float)survivalStatsType.GetMethod("GetEffectiveTutorialDrainMultiplier", InstanceFlags).Invoke(stats, null), 0.0001f);

            float hungerBeforeNormal = (float)survivalStatsType.GetField("currentHunger", InstanceFlags).GetValue(stats);
            survivalStatsType.GetMethod("StepHungerDrain", InstanceFlags).Invoke(stats, new object[] { 15f, false });
            float hungerAfterNormal = (float)survivalStatsType.GetField("currentHunger", InstanceFlags).GetValue(stats);
            Assert.AreEqual(1.0f, hungerBeforeNormal - hungerAfterNormal, 0.01f, "Post-tutorial hunger drain must return to 1.0x.");

            // Reset tutorial flags back to 0 and move to City Scene -> drain must still be 1.0x
            PlayerPrefs.DeleteKey("EcoQuest_TabTipDone");
            Scene cityScene = GetOrCreateAndActivateScene("City Scene");
            Assert.IsFalse((bool)survivalStatsType.GetMethod("IsTutorialDrainSlowdownActive", InstanceFlags).Invoke(stats, null),
                "Slowdown must NEVER apply in City Scene.");
            Assert.AreEqual(1.0f, (float)survivalStatsType.GetMethod("GetEffectiveTutorialDrainMultiplier", InstanceFlags).Invoke(stats, null), 0.0001f);

            yield return null;
        }

        private Scene GetOrCreateAndActivateScene(string sceneName)
        {
            Scene existing = SceneManager.GetSceneByName(sceneName);
            if (existing.IsValid() && existing.isLoaded)
            {
                SceneManager.SetActiveScene(existing);
                return existing;
            }

            Scene created = SceneManager.CreateScene(sceneName);
            createdTempScenes.Add(created);
            SceneManager.SetActiveScene(created);
            return created;
        }

        private object CreateRuntimeDatabaseWithApple()
        {
            var db = ScriptableObject.CreateInstance(itemDatabaseType);
            createdObjects.Add(db);

            var apple = ScriptableObject.CreateInstance(itemDefType);
            createdObjects.Add(apple);
            itemDefType.GetField("id", InstanceFlags).SetValue(apple, "food_apple");
            itemDefType.GetField("displayName", InstanceFlags).SetValue(apple, "Apple");

            var list = (IList)itemDatabaseType.GetField("registeredItems", InstanceFlags).GetValue(db);
            list.Add(apple);
            itemDatabaseType.GetMethod("RebuildLookup", InstanceFlags).Invoke(db, null);
            return db;
        }

        private void BackupPrefs()
        {
            savedStringPrefs.Clear();
            savedIntPrefs.Clear();
            savedFloatPrefs.Clear();

            hadMasterVolSetting = PlayerPrefs.HasKey("EcoQuest_Settings_MasterVol");
            if (hadMasterVolSetting)
            {
                savedMasterVolSetting = PlayerPrefs.GetFloat("EcoQuest_Settings_MasterVol", 1f);
            }

            for (int i = 0; i < progressKeys.Length; i++)
            {
                string key = progressKeys[i];
                if (!PlayerPrefs.HasKey(key)) continue;

                if (key == "EcoQuest_SeenInfoCards" || key == "EcoQuest_Inventory_Slots" || key == "EcoQuest_LastLoadingTipText")
                {
                    savedStringPrefs[key] = PlayerPrefs.GetString(key, string.Empty);
                }
                else if (key == "EcoQuest_City_BestTime" || key == "EcoQuest_GuardianCooldownUntil" ||
                         key == "EcoQuest_City_RobinCooldownRemaining" || key == "EcoQuest_RobinCooldownUntil" ||
                         key.StartsWith("EcoQuest_Player_HungerRemaining", StringComparison.Ordinal) ||
                         key.StartsWith("EcoQuest_Player_SpeedBuff", StringComparison.Ordinal) ||
                         key.StartsWith("EcoQuest_Player_StaminaBuff", StringComparison.Ordinal))
                {
                    savedFloatPrefs[key] = PlayerPrefs.GetFloat(key, 0f);
                }
                else
                {
                    savedIntPrefs[key] = PlayerPrefs.GetInt(key, 0);
                }
            }
        }

        private void RestorePrefs()
        {
            saveKeysType.GetMethod("ResetAllPlayerProgress", StaticFlags).Invoke(null, null);

            foreach (var kv in savedStringPrefs) PlayerPrefs.SetString(kv.Key, kv.Value);
            foreach (var kv in savedIntPrefs) PlayerPrefs.SetInt(kv.Key, kv.Value);
            foreach (var kv in savedFloatPrefs) PlayerPrefs.SetFloat(kv.Key, kv.Value);

            if (hadMasterVolSetting)
            {
                PlayerPrefs.SetFloat("EcoQuest_Settings_MasterVol", savedMasterVolSetting);
            }
            else if (PlayerPrefs.HasKey("EcoQuest_Settings_MasterVol"))
            {
                PlayerPrefs.DeleteKey("EcoQuest_Settings_MasterVol");
            }

            PlayerPrefs.Save();
        }
    }
}
