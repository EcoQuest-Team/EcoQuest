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
    public class CityQuestAndForestCooldownPlayModeTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly HashSet<string> FloatKeys = new HashSet<string>
        {
            "EcoQuest_City_BestTime",
            "EcoQuest_City_RobinCooldownRemaining",
            "EcoQuest_ForestGuardianCooldownRemaining",
            "EcoQuest_CityRobin_RemainingSeconds",
            "EcoQuest_CityRobin_HintCooldown",
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
            "EcoQuest_Forest_CollectedTrash",
            "EcoQuest_GuardianCooldownUntil",
            "EcoQuest_RobinCooldownUntil",
            "EcoQuest_CityTip_Checklist",
            "EcoQuest_CityTip_Hint",
            "EcoQuest_CityLitterbug_SlotData",
            "EcoQuest_CityRobin_SlotData",
            "EcoQuest_Inventory_Slots",
            "EcoQuest_Inventory_SeenNew"
        };

        private Type saveKeysType;
        private Type npcSpawnerType;
        private Type litterbugManagerType;
        private Type detectiveManagerType;
        private Type npcInteractionType;
        private Type travelGateType;
        private Type pauseType;

        private string[] progressKeys;
        private readonly Dictionary<string, int> backedUpInts = new Dictionary<string, int>();
        private readonly Dictionary<string, float> backedUpFloats = new Dictionary<string, float>();
        private readonly Dictionary<string, string> backedUpStrings = new Dictionary<string, string>();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            saveKeysType = Type.GetType("EcoQuestSaveKeys, Assembly-CSharp", true);
            npcSpawnerType = Type.GetType("NpcSpawner, Assembly-CSharp", true);
            litterbugManagerType = Type.GetType("LitterbugQuestManager, Assembly-CSharp", true);
            detectiveManagerType = Type.GetType("EcoQuest.CityQuest.CityTrashDetectiveManager, Assembly-CSharp", true);
            npcInteractionType = Type.GetType("NPCInteraction, Assembly-CSharp", true);
            travelGateType = Type.GetType("TravelGate, Assembly-CSharp", true);
            pauseType = Type.GetType("PauseMenuController, Assembly-CSharp", true);

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
            pauseType.GetProperty("IsPaused", Flags)?.SetValue(null, false);
            RestoreProgressPrefs();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Type omsType = Type.GetType("ObjectiveMarkerService, Assembly-CSharp", false);
            if (omsType != null)
            {
                var inst = omsType.GetProperty("Instance", Flags)?.GetValue(null) as Component;
                if (inst != null)
                {
                    UnityEngine.Object.DestroyImmediate(inst.gameObject);
                }
            }
        }

        [UnityTest]
        public IEnumerator CityScene_NpcSpawnerAndAnimatorsAreSerialized_AndNpcsSpawnAtRuntime()
        {
            ResetProgress();
            yield return LoadSceneAndWait("City Scene", 5);

            var spawner = UnityEngine.Object.FindFirstObjectByType(npcSpawnerType, FindObjectsInactive.Include) as MonoBehaviour;
            Assert.IsNotNull(spawner, "NpcSpawner must exist in City Scene.");

            Assert.IsNotNull(npcSpawnerType.GetField("walkGraph", Flags).GetValue(spawner), "NpcSpawner.walkGraph must be serialized.");
            Assert.IsNotNull(npcSpawnerType.GetField("outfitProfile", Flags).GetValue(spawner), "NpcSpawner.outfitProfile must be serialized.");
            Assert.IsNotNull(npcSpawnerType.GetField("maleOverrideController", Flags).GetValue(spawner), "NpcSpawner.maleOverrideController must be serialized for standalone builds.");
            Assert.IsNotNull(npcSpawnerType.GetField("femaleOverrideController", Flags).GetValue(spawner), "NpcSpawner.femaleOverrideController must be serialized for standalone builds.");

            var spawnedObj = npcSpawnerType.GetProperty("SpawnedNpcs", Flags).GetValue(spawner) as System.Collections.ICollection;
            Assert.IsNotNull(spawnedObj, "NpcSpawner.SpawnedNpcs must not be null.");
            Assert.GreaterOrEqual(spawnedObj.Count, 10, $"Expected at least 10 spawned walking NPCs in City Scene, got {spawnedObj.Count}.");

            int animatedCount = 0;
            foreach (var item in spawnedObj)
            {
                var comp = item as Component;
                if (comp == null || !comp.gameObject.activeInHierarchy) continue;
                var anim = comp.GetComponentInChildren<Animator>(true);
                if (anim != null && anim.runtimeAnimatorController != null)
                {
                    animatedCount++;
                }
            }
            Assert.GreaterOrEqual(animatedCount, 10, "Spawned City NPCs must have runtimeAnimatorController assigned without Editor AssetDatabase.");
        }

        [UnityTest]
        public IEnumerator CityScene_FountainLitterbugRoundStarts_AndPersistsAcrossMainMenuReload()
        {
            ResetProgress();
            yield return LoadSceneAndWait("City Scene", 5);

            object lqm = litterbugManagerType.GetProperty("Instance", Flags).GetValue(null);
            Assert.IsNotNull(lqm, "LitterbugQuestManager.Instance must exist in City Scene.");

            litterbugManagerType.GetMethod("StartNewRound", Flags).Invoke(lqm, null);
            yield return null;

            string initialState = litterbugManagerType.GetProperty("QuestState", Flags).GetValue(lqm).ToString();
            Assert.AreEqual("Active", initialState, "LitterbugQuestManager must enter Active state when starting round with live NPCs.");

            MethodInfo getSlotBrain = litterbugManagerType.GetMethod("GetSlotBrain", Flags);
            MethodInfo getSlotLitterbug = litterbugManagerType.GetMethod("GetSlotLitterbug", Flags);
            for (int i = 0; i < 5; i++)
            {
                Assert.IsNotNull(getSlotBrain.Invoke(lqm, new object[] { i }), $"Litterbug slot {i} brain must be bound.");
                Assert.IsNotNull(getSlotLitterbug.Invoke(lqm, new object[] { i }), $"Litterbug slot {i} LitterbugNpc must be bound.");
            }

            MethodInfo persuadeMethod = litterbugManagerType.GetMethod("CompletePersuasionForSlot", Flags);
            persuadeMethod.Invoke(lqm, new object[] { 0 });
            persuadeMethod.Invoke(lqm, new object[] { 1 });
            litterbugManagerType.GetMethod("FlushMidQuestSaveNow", Flags).Invoke(lqm, null);

            Assert.AreEqual(2, (int)litterbugManagerType.GetProperty("PersuadedCount", Flags).GetValue(lqm));
            Assert.AreEqual(1, PlayerPrefs.GetInt("EcoQuest_CityLitterbug_HasSavedProgress", 0));

            yield return LoadSceneAndWait("Main Menu", 3);
            yield return LoadSceneAndWait("City Scene", 5);

            object restoredLqm = litterbugManagerType.GetProperty("Instance", Flags).GetValue(null);
            Assert.IsNotNull(restoredLqm);
            Assert.AreEqual("Active", litterbugManagerType.GetProperty("QuestState", Flags).GetValue(restoredLqm).ToString(),
                "Litterbug round must resume Active after returning from Main Menu.");
            Assert.AreEqual(2, (int)litterbugManagerType.GetProperty("PersuadedCount", Flags).GetValue(restoredLqm),
                "PersuadedCount (2/5) must persist across Main Menu reload.");

            MethodInfo restoredGetBrain = litterbugManagerType.GetMethod("GetSlotBrain", Flags);
            for (int i = 2; i < 5; i++)
            {
                Assert.IsNotNull(restoredGetBrain.Invoke(restoredLqm, new object[] { i }),
                    $"Unpersuaded slot {i} must rebind to a live NPC after reload.");
            }

            MethodInfo restoredPersuade = litterbugManagerType.GetMethod("CompletePersuasionForSlot", Flags);
            restoredPersuade.Invoke(restoredLqm, new object[] { 2 });
            restoredPersuade.Invoke(restoredLqm, new object[] { 3 });
            restoredPersuade.Invoke(restoredLqm, new object[] { 4 });
            Assert.AreEqual("ReadyToTurnIn", litterbugManagerType.GetProperty("QuestState", Flags).GetValue(restoredLqm).ToString());

            litterbugManagerType.GetMethod("ClaimRoundReward", Flags).Invoke(restoredLqm, null);
            Assert.AreEqual("Available", litterbugManagerType.GetProperty("QuestState", Flags).GetValue(restoredLqm).ToString());
            Assert.AreEqual(0, PlayerPrefs.GetInt("EcoQuest_CityLitterbug_HasSavedProgress", 0),
                "Saved mid-round key must clear after claiming round reward.");
        }

        [UnityTest]
        public IEnumerator CityScene_RangerRobinSpawnsTrash_AndPersistsMidAttemptAcrossMainMenuReload()
        {
            ResetProgress();
            yield return LoadSceneAndWait("City Scene", 5);

            object detective = detectiveManagerType.GetProperty("Instance", Flags).GetValue(null);
            Assert.IsNotNull(detective, "CityTrashDetectiveManager.Instance must exist in City Scene.");

            detectiveManagerType.GetMethod("StartNewAttempt", Flags).Invoke(detective, null);
            yield return null;

            Assert.AreEqual("Active", detectiveManagerType.GetProperty("QuestState", Flags).GetValue(detective).ToString());
            var checklist = detectiveManagerType.GetProperty("ActiveChecklist", Flags).GetValue(detective) as System.Collections.IList;
            Assert.IsNotNull(checklist);
            Assert.AreEqual(10, checklist.Count, "Ranger Robin attempt must spawn 10 checklist items.");

            Type slotType = checklist[0].GetType();
            FieldInfo pickupField = slotType.GetField("pickupInstance", Flags);
            FieldInfo collectedField = slotType.GetField("collected", Flags);

            int activePickups = 0;
            foreach (var slot in checklist)
            {
                var pickup = pickupField.GetValue(slot) as Component;
                if (pickup != null && pickup.gameObject.activeInHierarchy) activePickups++;
            }
            Assert.AreEqual(10, activePickups, "All 10 Ranger Robin trash pickups must be active in City Scene.");

            MethodInfo collectMethod = detectiveManagerType.GetMethod("CollectChecklistSlot", Flags);
            collectMethod.Invoke(detective, new object[] { 0 });
            collectMethod.Invoke(detective, new object[] { 1 });
            collectMethod.Invoke(detective, new object[] { 2 });
            detectiveManagerType.GetMethod("TryTriggerCompassHint", Flags).Invoke(detective, null);
            detectiveManagerType.GetMethod("FlushMidQuestSaveNow", Flags).Invoke(detective, null);

            Assert.AreEqual(3, (int)detectiveManagerType.GetProperty("CollectedCount", Flags).GetValue(detective));
            Assert.AreEqual(3, (int)detectiveManagerType.GetProperty("RemainingHintCharges", Flags).GetValue(detective));
            Assert.AreEqual(1, PlayerPrefs.GetInt("EcoQuest_CityRobin_HasSavedProgress", 0));

            yield return LoadSceneAndWait("Main Menu", 3);
            yield return LoadSceneAndWait("City Scene", 5);

            object restoredDetective = detectiveManagerType.GetProperty("Instance", Flags).GetValue(null);
            Assert.IsNotNull(restoredDetective);
            Assert.AreEqual("Active", detectiveManagerType.GetProperty("QuestState", Flags).GetValue(restoredDetective).ToString(),
                "Ranger Robin quest must resume Active after returning from Main Menu.");
            Assert.AreEqual(3, (int)detectiveManagerType.GetProperty("CollectedCount", Flags).GetValue(restoredDetective));
            Assert.AreEqual(3, (int)detectiveManagerType.GetProperty("RemainingHintCharges", Flags).GetValue(restoredDetective));

            var restoredList = detectiveManagerType.GetProperty("ActiveChecklist", Flags).GetValue(restoredDetective) as System.Collections.IList;
            Assert.IsNotNull(restoredList);
            Assert.AreEqual(10, restoredList.Count);
            int remainingActivePickups = 0;
            for (int i = 0; i < restoredList.Count; i++)
            {
                bool isCol = (bool)collectedField.GetValue(restoredList[i]);
                var pickup = pickupField.GetValue(restoredList[i]) as Component;
                if (i < 3)
                {
                    Assert.IsTrue(isCol, $"Restored slot {i} should remain collected.");
                    Assert.IsNull(pickup, $"Collected slot {i} must not respawn in the world.");
                }
                else
                {
                    Assert.IsFalse(isCol, $"Restored slot {i} should still be uncollected.");
                    if (pickup != null && pickup.gameObject.activeInHierarchy) remainingActivePickups++;
                }
            }
            Assert.AreEqual(7, remainingActivePickups, "All 7 remaining Ranger Robin trash items must respawn after reload.");

            detectiveManagerType.GetMethod("CancelActiveAttempt", Flags).Invoke(restoredDetective, null);
            Assert.AreEqual(0, PlayerPrefs.GetInt("EcoQuest_CityRobin_HasSavedProgress", 0),
                "Cancelling Ranger Robin quest must clear saved mid-attempt progress.");
        }

        [UnityTest]
        public IEnumerator CityScene_ZeroNpcsDoesNotSoftLockFountainOrBlockRangerRobin()
        {
            ResetProgress();
            yield return LoadSceneAndWait("City Scene", 5);

            var spawner = UnityEngine.Object.FindFirstObjectByType(npcSpawnerType, FindObjectsInactive.Include) as MonoBehaviour;
            var spawned = npcSpawnerType.GetProperty("SpawnedNpcs", Flags).GetValue(spawner) as System.Collections.IEnumerable;
            foreach (var item in spawned)
            {
                if (item is Component c) c.gameObject.SetActive(false);
            }

            object lqm = litterbugManagerType.GetProperty("Instance", Flags).GetValue(null);
            LogAssert.Expect(LogType.Error, "[LitterbugQuestManager] No eligible walking NPCs bound; reverting to Available instead of soft-locking 0/5.");
            litterbugManagerType.GetMethod("StartNewRound", Flags).Invoke(lqm, null);
            yield return null;

            Assert.AreEqual("Available", litterbugManagerType.GetProperty("QuestState", Flags).GetValue(lqm).ToString(),
                "0 available NPCs must abort back to Available instead of soft-locking Active 0/5.");

            object detective = detectiveManagerType.GetProperty("Instance", Flags).GetValue(null);
            detectiveManagerType.GetMethod("StartNewAttempt", Flags).Invoke(detective, null);
            yield return null;

            Assert.AreEqual("Active", detectiveManagerType.GetProperty("QuestState", Flags).GetValue(detective).ToString(),
                "Ranger Robin must not be blocked by a 0-NPC Litterbug failure.");
            detectiveManagerType.GetMethod("CancelActiveAttempt", Flags).Invoke(detective, null);
        }

        [UnityTest]
        public IEnumerator ForestScene_GuardianHas300sCooldownOnCompletion_AndPreservesTravelUnlock()
        {
            ResetProgress();
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);
            PlayerPrefs.SetInt("SequentialWASD_TutorialCompleted", 1);
            PlayerPrefs.SetInt("EcoQuest_PreQuizScore", 3);
            PlayerPrefs.SetInt("EcoQuest_PostQuizScore", 5);
            PlayerPrefs.Save();

            yield return LoadSceneAndWait("Forest Scene", 5);

            var guardian = UnityEngine.Object.FindFirstObjectByType(npcInteractionType, FindObjectsInactive.Include) as MonoBehaviour;
            Assert.IsNotNull(guardian, "Forest Guardian NPCInteraction must exist in Forest Scene.");

            Assert.IsNotNull(npcInteractionType.GetField("guardianAnimatorController", Flags).GetValue(guardian),
                "Guardian animator controller must be serialized on NPCInteraction.");
            float configuredCooldown = (float)npcInteractionType.GetField("repeatCooldownSeconds", Flags).GetValue(guardian);
            Assert.That(configuredCooldown, Is.EqualTo(300f).Within(0.01f),
                "Forest Guardian cooldown duration must be configured to 300s (5 minutes).");

            MethodInfo isOnCooldownMethod = npcInteractionType.GetMethod("IsOnCooldown", Flags);
            object[] outArgs = new object[] { 0f };
            bool initiallyOnCooldown = (bool)isOnCooldownMethod.Invoke(null, outArgs);
            Assert.IsFalse(initiallyOnCooldown);

            npcInteractionType.GetMethod("NotifyAllQuestCompleted", Flags).Invoke(null, null);
            travelGateType.GetMethod("NotifyTrashQuestCompleted", Flags).Invoke(null, null);
            travelGateType.GetMethod("NotifyRewardClaimed", Flags).Invoke(null, null);

            bool canTravel = (bool)travelGateType.GetProperty("CanTravelToCity", Flags).GetValue(null);
            Assert.IsTrue(canTravel, "Forest Guardian cooldown must keep TravelGate.CanTravelToCity unlocked.");

            outArgs = new object[] { 0f };
            bool startedCooldown = (bool)isOnCooldownMethod.Invoke(null, outArgs);
            Assert.IsTrue(startedCooldown, "Quest completion must activate Forest Guardian 5:00 cooldown.");
            Assert.That((float)outArgs[0], Is.InRange(298f, 300.1f));

            MethodInfo shouldTick = npcInteractionType.GetMethod("ShouldTickCooldown", Flags);
            pauseType.GetProperty("IsPaused", Flags).SetValue(null, true);
            Assert.IsFalse((bool)shouldTick.Invoke(null, null), "Guardian cooldown must pause while game is paused.");
            pauseType.GetProperty("IsPaused", Flags).SetValue(null, false);
            Assert.IsTrue((bool)shouldTick.Invoke(null, null), "Guardian cooldown must tick during active unpaused gameplay.");

            MethodInfo tickMethod = npcInteractionType.GetMethod("TickCooldownGlobal", Flags);
            tickMethod.Invoke(null, new object[] { 125f });
            npcInteractionType.GetMethod("FlushCooldownSave", Flags).Invoke(null, null);

            yield return LoadSceneAndWait("Main Menu", 3);
            yield return LoadSceneAndWait("Forest Scene", 5);

            outArgs = new object[] { 0f };
            bool persistedAfterReload = (bool)isOnCooldownMethod.Invoke(null, outArgs);
            Assert.IsTrue(persistedAfterReload, "Remaining cooldown must persist across Main Menu reload.");
            Assert.That((float)outArgs[0], Is.InRange(170f, 176f));

            tickMethod.Invoke(null, new object[] { 200f });
            outArgs = new object[] { 0f };
            bool stillOnCooldown = (bool)isOnCooldownMethod.Invoke(null, outArgs);
            Assert.IsFalse(stillOnCooldown, "Cooldown must expire cleanly when remaining seconds reach 0.");
            Assert.IsFalse(PlayerPrefs.HasKey("EcoQuest_ForestGuardianCooldownRemaining"),
                "Expired Guardian cooldown key must be removed from PlayerPrefs.");
        }

        [UnityTest]
        public IEnumerator ResetAllPlayerProgress_ClearsCityMidQuestAndForestCooldownKeys()
        {
            PlayerPrefs.SetInt("EcoQuest_CityLitterbug_HasSavedProgress", 1);
            PlayerPrefs.SetString("EcoQuest_CityLitterbug_SlotData", "{}");
            PlayerPrefs.SetInt("EcoQuest_CityRobin_HasSavedProgress", 1);
            PlayerPrefs.SetString("EcoQuest_CityRobin_SlotData", "{}");
            npcInteractionType.GetMethod("StartCooldown", Flags).Invoke(null, new object[] { 300f });

            ResetProgress();
            yield return null;

            Assert.AreEqual(0, PlayerPrefs.GetInt("EcoQuest_CityLitterbug_HasSavedProgress", 0));
            Assert.AreEqual(0, PlayerPrefs.GetInt("EcoQuest_CityRobin_HasSavedProgress", 0));
            object[] outArgs = new object[] { 0f };
            Assert.IsFalse((bool)npcInteractionType.GetMethod("IsOnCooldown", Flags).Invoke(null, outArgs));

            // Leave TestRunner on a clean empty scene so subsequent isolated PlayMode suites don't inherit Forest/City scene objects.
            Scene cleanScene = SceneManager.GetSceneByName("CleanPostCityQuestTestScene");
            if (!cleanScene.IsValid())
            {
                cleanScene = SceneManager.CreateScene("CleanPostCityQuestTestScene");
            }
            SceneManager.SetActiveScene(cleanScene);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.IsValid() && s != cleanScene && (s.name == "Forest Scene" || s.name == "City Scene" || s.name == "Main Menu"))
                {
                    var op = SceneManager.UnloadSceneAsync(s);
                    while (op != null && !op.isDone) yield return null;
                }
            }
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
