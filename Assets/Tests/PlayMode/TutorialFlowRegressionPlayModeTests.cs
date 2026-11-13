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
            "EcoQuest_Forest_CollectedTrash",
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

        [UnityTest]
        public IEnumerator CompletingTabTutorial_DoesNotResetTrashSortedDuringTabTip()
        {
            ResetProgress();
            PlayerPrefs.SetInt("SequentialWASD_TutorialCompleted", 1);
            PlayerPrefs.SetInt("EcoQuest_SequentialStep", 6);
            PlayerPrefs.SetInt("EcoQuest_GuardianFlowStage", 3);
            PlayerPrefs.SetInt("EcoQuest_PreQuizScore", 4);
            PlayerPrefs.SetInt("EcoQuest_ForestQuestAccepted", 1);
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 0);
            PlayerPrefs.SetInt("EcoQuest_ForestTrashQuestDone", 0);
            PlayerPrefs.Save();

            yield return LoadSceneAndWait("Forest Scene", 5);

            object orchestrator = GetOrchestratorInstance();
            Assert.AreEqual("TabTutorial", GetFlowStepName(orchestrator), "Player in TAB tip should be at FlowStep.TabTutorial.");
            Assert.IsTrue((bool)trashType.GetProperty("AreTrashItemsAllowed", Flags).GetValue(null), "Trash is already active during TabTutorial.");

            Assert.IsTrue(TryDepositOneMatchingTrashItem(out string sortedCategory, out string sortedItemName), "Should find and deposit an active TrashItem into a matching bin.");
            int beforeTabCount = (int)questType.GetMethod("GetTotalSortedWaste", Flags).Invoke(null, null);
            Assert.AreEqual(1, beforeTabCount, $"Expected 1 sorted waste before TAB tip completion (deposited {sortedItemName} -> {sortedCategory}).");

            Type introType = Type.GetType("IntroSequenceController, Assembly-CSharp", true);
            object intro = introType.GetProperty("Instance", Flags).GetValue(null);
            Assert.IsNotNull(intro, "IntroSequenceController.Instance should exist.");
            introType.GetMethod("HandleTabTipCompleted", Flags).Invoke(intro, null);

            yield return null;
            yield return null;

            int afterTabCount = (int)questType.GetMethod("GetTotalSortedWaste", Flags).Invoke(null, null);
            Assert.AreEqual(1, afterTabCount, "Completing TAB tutorial must preserve trash sorted during TAB tutorial.");
            Assert.AreEqual("FreePlay", GetFlowStepName(orchestrator), "FlowStep should transition to FreePlay after TAB tutorial completes.");
        }

        [UnityTest]
        public IEnumerator MidTutorialAndMidQuestProgress_PersistsAcrossMainMenuReload_AndClearsOnReset()
        {
            // 1. Mid-WASD step checkpoint: completed through Map step (5) -> reload resumes unlocked Step 6 (Talk to Guardian)
            ResetProgress();
            PlayerPrefs.SetInt("EcoQuest_SequentialStep", 5);
            PlayerPrefs.Save();

            yield return LoadSceneAndWait("Main Menu", 3);
            yield return LoadSceneAndWait("Forest Scene", 5);
            yield return WaitUnscaledSeconds(0.35f);

            bool mapUnlocked = (bool)minimapType.GetProperty("IsMapTutorialUnlocked", Flags).GetValue(null);
            Assert.IsTrue(mapUnlocked, "Resuming after Map step (5) should keep minimap tutorial unlocked without repeating Steps 1-5.");

            // 2. Mid-quest deposit -> Return to Main Menu -> Play Forest Scene again preserves count and keeps deposited item inactive
            ResetProgress();
            PlayerPrefs.SetInt("SequentialWASD_TutorialCompleted", 1);
            PlayerPrefs.SetInt("EcoQuest_SequentialStep", 6);
            PlayerPrefs.SetInt("EcoQuest_GuardianFlowStage", 3);
            PlayerPrefs.SetInt("EcoQuest_PreQuizScore", 4);
            PlayerPrefs.SetInt("EcoQuest_ForestQuestAccepted", 1);
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);
            PlayerPrefs.SetInt("EcoQuest_ForestTrashQuestDone", 0);
            PlayerPrefs.Save();

            yield return LoadSceneAndWait("Forest Scene", 5);
            Assert.IsTrue(TryDepositOneMatchingTrashItem(out string sortedCategory, out string sortedItemName), "Should deposit 1 matching trash item.");
            Assert.AreEqual(1, (int)questType.GetMethod("GetTotalSortedWaste", Flags).Invoke(null, null));

            // Simulate quit / back to Main Menu and hitting Play again
            yield return LoadSceneAndWait("Main Menu", 3);
            yield return LoadSceneAndWait("Forest Scene", 5);

            int restoredTotal = (int)questType.GetMethod("GetTotalSortedWaste", Flags).Invoke(null, null);
            int restoredCategoryCount = (int)questType.GetMethod("GetCategoryCurrentCount", Flags).Invoke(null, new object[] { sortedCategory });
            Assert.AreEqual(1, restoredTotal, "Mid-quest total sorted waste must persist across Main Menu / restart.");
            Assert.AreEqual(1, restoredCategoryCount, $"Category count for '{sortedCategory}' must persist across Main Menu / restart.");

            // Deposited item must remain inactive and marked IsDeposited
            var allTrash = UnityEngine.Object.FindObjectsByType(trashType, FindObjectsInactive.Include, FindObjectsSortMode.None);
            int inactiveDeposited = 0;
            int activeRemaining = 0;
            PropertyInfo isDepositedProp = trashType.GetProperty("IsDeposited", Flags);
            foreach (UnityEngine.Object obj in allTrash)
            {
                var comp = (Component)obj;
                bool dep = (bool)isDepositedProp.GetValue(comp);
                if (dep && !comp.gameObject.activeSelf) inactiveDeposited++;
                else if (!dep && comp.gameObject.activeSelf) activeRemaining++;
            }
            Assert.AreEqual(1, inactiveDeposited, $"Deposited item '{sortedItemName}' must remain deposited and inactive on reload.");
            Assert.Greater(activeRemaining, 0, "Remaining uncollected trash items must remain active in scene.");

            // Explicit reset must clear everything back to 0
            ResetProgress();
            yield return LoadSceneAndWait("Forest Scene", 5);
            Assert.AreEqual(0, (int)questType.GetMethod("GetTotalSortedWaste", Flags).Invoke(null, null), "ResetAllPlayerProgress must clear mid-quest counts to 0.");
        }

        [UnityTest]
        public IEnumerator MainMenuToForestScene_LocksCursorAndEnablesCrosshairImmediately()
        {
            ResetProgress();
            yield return LoadSceneAndWait("Main Menu", 3);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            yield return LoadSceneAndWait("Forest Scene", 5);

            Type cursorStateType = Type.GetType("CursorState, Assembly-CSharp", true);
            AssertCursorLockedForGameplay(cursorStateType, "entering Forest Scene from Main Menu");
        }

        [UnityTest]
        public IEnumerator ExplorerNpcDialogue_CompletedForest_ShowsConfirmationAndTeleportsToCityScene()
        {
            Type travelGateType = Type.GetType("TravelGate, Assembly-CSharp", true);
            Type explorerType = Type.GetType("ExplorerGateController, Assembly-CSharp", true);
            Type presenterType = Type.GetType("DialoguePresenter, Assembly-CSharp", true);
            Type cursorStateType = Type.GetType("CursorState, Assembly-CSharp", true);

            ResetProgress();
            travelGateType.GetMethod("ResetStaticState", Flags)?.Invoke(null, null);

            PlayerPrefs.SetInt((string)saveKeysType.GetField("TabTipDone", Flags).GetValue(null), 1);
            PlayerPrefs.SetInt((string)saveKeysType.GetField("SequentialWASD", Flags).GetValue(null), 1);
            PlayerPrefs.SetInt((string)saveKeysType.GetField("SequentialStep", Flags).GetValue(null), 6);
            PlayerPrefs.SetInt((string)saveKeysType.GetField("PreQuizScore", Flags).GetValue(null), 5);
            PlayerPrefs.SetInt((string)saveKeysType.GetField("ForestQuestAccepted", Flags).GetValue(null), 1);
            PlayerPrefs.SetInt((string)saveKeysType.GetField("GuardianFlowStage", Flags).GetValue(null), 3);
            PlayerPrefs.SetInt((string)saveKeysType.GetField("ForestTrashQuestDone", Flags).GetValue(null), 1);
            PlayerPrefs.SetInt((string)saveKeysType.GetField("PostQuizScore", Flags).GetValue(null), 5);
            PlayerPrefs.SetInt((string)saveKeysType.GetField("ForestRewardClaimed", Flags).GetValue(null), 1);
            PlayerPrefs.Save();

            yield return LoadSceneAndWait("Forest Scene", 5);

            bool canTravel = (bool)travelGateType.GetProperty("CanTravelToCity", Flags).GetValue(null);
            Assert.IsTrue(canTravel, "TravelGate.CanTravelToCity should be true when Forest quest, post-quiz, and reward are completed.");

            object explorer = explorerType.GetProperty("Instance", Flags).GetValue(null);
            object presenter = presenterType.GetProperty("Instance", Flags).GetValue(null);
            Assert.IsNotNull(explorer, "ExplorerGateController.Instance should exist in Forest Scene.");
            Assert.IsNotNull(presenter, "DialoguePresenter.Instance should exist in Forest Scene.");

            explorerType.GetMethod("StartDialogueSession", Flags).Invoke(explorer, null);
            yield return null;
            yield return null;

            Assert.IsTrue((bool)presenterType.GetProperty("IsInDialogue", Flags).GetValue(presenter), "DialoguePresenter must be active after starting Explorer dialogue.");
            Assert.IsTrue((bool)explorerType.GetProperty("IsInDialogue", Flags).GetValue(explorer), "ExplorerGateController must report IsInDialogue.");

            var acceptBtn = presenterType.GetField("acceptQuestButton", Flags).GetValue(presenter) as Component;
            var notNowBtn = presenterType.GetField("notNowButton", Flags).GetValue(presenter) as Component;
            var acceptKeyCap = presenterType.GetField("acceptKeyCap", Flags).GetValue(presenter) as Component;
            var notNowKeyCap = presenterType.GetField("notNowKeyCap", Flags).GetValue(presenter) as Component;
            var continueRoot = presenterType.GetField("continuePromptRoot", Flags).GetValue(presenter) as Component;
            var continueKeyCap = presenterType.GetField("continueKeyCap", Flags).GetValue(presenter) as Component;

            Assert.IsNotNull(acceptBtn, "DialoguePresenter.acceptQuestButton should be assigned.");
            Assert.IsNotNull(notNowBtn, "DialoguePresenter.notNowButton should be assigned.");
            Assert.IsNotNull(continueRoot, "DialoguePresenter.continuePromptRoot should be assigned.");

            for (int page = 0; page < 8; page++)
            {
                yield return WaitUnscaledSeconds(0.65f);
                if (acceptBtn.gameObject.activeInHierarchy) break;

                Assert.IsTrue(continueRoot.gameObject.activeInHierarchy, $"Dialogue page {page} must show [E] Continue prompt while not yet at choice buttons.");
                if (continueKeyCap != null)
                {
                    Assert.IsTrue(continueKeyCap.gameObject.activeInHierarchy, "continueKeyCap must be active on non-choice dialogue pages.");
                    AssertKeyCapNotDimmed(continueKeyCap);
                }

                presenterType.GetMethod("HandleAdvanceOrConfirm", Flags).Invoke(presenter, null);
                yield return null;
            }

            Assert.IsTrue(acceptBtn.gameObject.activeInHierarchy, "Explorer travel confirmation button ('Yes, let's go!') must be active and visible on the final choice page.");
            Assert.IsTrue(notNowBtn.gameObject.activeInHierarchy, "Explorer decline button ('Maybe later') must be active and visible on the final choice page.");
            if (acceptKeyCap != null)
            {
                Assert.IsTrue(acceptKeyCap.gameObject.activeInHierarchy, "Accept KeyCap prompt must be active on the confirmation button.");
                AssertKeyCapNotDimmed(acceptKeyCap);
            }
            if (notNowKeyCap != null)
            {
                Assert.IsTrue(notNowKeyCap.gameObject.activeInHierarchy, "Decline KeyCap prompt must be active on the decline button.");
                AssertKeyCapNotDimmed(notNowKeyCap);
            }
            Assert.IsFalse(continueRoot.gameObject.activeInHierarchy, "Continue prompt should be hidden once choice confirmation buttons are shown.");
            Assert.AreNotEqual(CursorLockMode.Locked, Cursor.lockState, "Cursor must unlock during Explorer confirmation dialogue so player can click choice buttons.");
            Assert.IsTrue(Cursor.visible, "Cursor must be visible during Explorer confirmation dialogue.");

            var onClick = acceptBtn.GetType().GetProperty("onClick", Flags).GetValue(acceptBtn) as UnityEngine.Events.UnityEvent;
            Assert.IsNotNull(onClick, "acceptQuestButton.onClick should be a UnityEvent.");
            onClick.Invoke();

            float waitElapsed = 0f;
            while (SceneManager.GetActiveScene().name != "City Scene" && waitElapsed < 15f)
            {
                waitElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual("City Scene", SceneManager.GetActiveScene().name, "Confirming Explorer travel offer must teleport the player to 'City Scene'.");
            for (int i = 0; i < 5; i++) yield return null;

            Assert.IsNotNull(GameObject.FindGameObjectWithTag("Player"), "Player must be present in 'City Scene' after teleporting from Explorer NPC.");
            AssertCursorLockedForGameplay(cursorStateType, "teleporting into City Scene");
        }

        private void AssertCursorLockedForGameplay(Type cursorStateType, string context)
        {
            Type inputsType = Type.GetType("StarterAssets.StarterAssetsInputs, Assembly-CSharp", true);
            var inputs = UnityEngine.Object.FindFirstObjectByType(inputsType) as Component;
            Assert.IsNotNull(inputs, $"StarterAssetsInputs should exist when {context}.");

            bool inputsLocked = (bool)inputsType.GetField("cursorLocked", Flags).GetValue(inputs);
            bool inputsLook = (bool)inputsType.GetField("cursorInputForLook", Flags).GetValue(inputs);
            bool blockingUiOpen = (bool)cursorStateType.GetMethod("IsAnyBlockingUIOpen", Flags).Invoke(null, null);

            Assert.IsFalse(blockingUiOpen, $"No blocking UI should remain open when {context}.");
            Assert.IsTrue(inputsLocked, $"StarterAssetsInputs.cursorLocked must be true immediately when {context}.");
            Assert.IsTrue(inputsLook, $"StarterAssetsInputs.cursorInputForLook must be true immediately when {context}.");
            Assert.IsFalse(Cursor.visible, $"OS cursor should be hidden immediately when {context}.");

            Type centerCursorType = Type.GetType("CenterScreenCursor, Assembly-CSharp", true);
            object centerCursor = centerCursorType.GetProperty("Instance", Flags)?.GetValue(null);
            if (centerCursor != null)
            {
                bool requestedVisible = (bool)centerCursorType.GetField("requestedVisible", Flags).GetValue(centerCursor);
                Assert.IsTrue(requestedVisible, $"CenterScreenCursor.requestedVisible must be true when {context}.");
            }

            // Unity -batchmode keeps hardware Cursor.lockState == None without an OS window focus handle.
            if (!Application.isBatchMode)
            {
                bool lookEnabled = (bool)cursorStateType.GetProperty("LookEnabled", Flags).GetValue(null);
                Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState, $"Cursor must lock immediately when {context}.");
                Assert.IsTrue(lookEnabled, $"CursorState.LookEnabled must be true when {context}.");
            }
        }

        private void AssertKeyCapNotDimmed(Component keyCap)
        {
            Type keyCapType = keyCap.GetType();
            var canvasGroup = keyCapType.GetField("promptCanvasGroup", Flags)?.GetValue(keyCap) as CanvasGroup;
            if (canvasGroup != null)
            {
                Assert.Greater(canvasGroup.alpha, 0.9f, $"{keyCap.name} CanvasGroup alpha should be unlocked (~1.0), not dimmed.");
            }

            foreach (string textFieldName in new[] { "actionLabel", "keycapText" })
            {
                object tmp = keyCapType.GetField(textFieldName, Flags)?.GetValue(keyCap);
                if (tmp == null) continue;
                var color = (Color)tmp.GetType().GetProperty("color", Flags).GetValue(tmp);
                Assert.Greater(color.a, 0.9f, $"{keyCap.name}.{textFieldName} alpha should be 1.0 when unlocked (not stuck at baked 0.4).");
            }
        }

        private bool TryDepositOneMatchingTrashItem(out string categoryTag, out string itemName)
        {
            categoryTag = null;
            itemName = null;

            var bins = UnityEngine.Object.FindObjectsByType(questType, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var trashItems = UnityEngine.Object.FindObjectsByType(trashType, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            PropertyInfo acceptedTagProp = questType.GetProperty("AcceptedTag", Flags);
            MethodInfo tryProcessMethod = questType.GetMethod("TryProcessTrashItem", Flags);
            MethodInfo matchesCategoryMethod = questType.GetMethod("IsItemMatchingCategory", Flags);

            foreach (UnityEngine.Object binObj in bins)
            {
                var bin = (Component)binObj;
                string tag = acceptedTagProp.GetValue(bin) as string;
                if (string.IsNullOrEmpty(tag)) continue;

                foreach (UnityEngine.Object trashObj in trashItems)
                {
                    var trash = (Component)trashObj;
                    if (!trash.gameObject.activeInHierarchy) continue;

                    bool matches = (bool)matchesCategoryMethod.Invoke(bin, new object[] { trash.gameObject, tag });
                    if (!matches) continue;

                    bool processed = (bool)tryProcessMethod.Invoke(bin, new object[] { trash });
                    if (processed)
                    {
                        categoryTag = tag;
                        itemName = trash.gameObject.name;
                        return true;
                    }
                }
            }

            return false;
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
