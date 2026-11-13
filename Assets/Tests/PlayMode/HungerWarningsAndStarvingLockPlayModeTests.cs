using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EcoQuest.Tests.PlayMode
{
    public class HungerWarningsAndStarvingLockPlayModeTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private Type saveKeysType;
        private Type survivalStatsType;
        private Type hudType;
        private Type sprintCtrlType;
        private Type fpcType;
        private Type inputsType;
        private Type pauseMenuType;
        private Type controlTipType;
        private Type wrongTrashType;
        private Type loadingScreenType;
        private Type cursorStateType;
        private Type uiImageType;

        private string[] progressKeys;
        private string hungerRemainingKey;
        private string warn10Key;
        private string warn5Key;
        private string warn0Key;

        private readonly Dictionary<string, string> savedStringPrefs = new Dictionary<string, string>();
        private readonly Dictionary<string, int> savedIntPrefs = new Dictionary<string, int>();
        private readonly Dictionary<string, float> savedFloatPrefs = new Dictionary<string, float>();

        private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();
        private readonly List<Scene> createdTempScenes = new List<Scene>();
        private Scene originalActiveScene;

        private EventInfo warningEvent;
        private readonly List<(Delegate Handler, Action<int> Action)> subscribedHandlers = new List<(Delegate, Action<int>)>();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            saveKeysType = Type.GetType("EcoQuestSaveKeys, Assembly-CSharp", true);
            survivalStatsType = Type.GetType("PlayerSurvivalStats, Assembly-CSharp", true);
            hudType = Type.GetType("UI_SurvivalStatsHUD, Assembly-CSharp", true);
            sprintCtrlType = Type.GetType("PlayerSprintController, Assembly-CSharp", true);
            fpcType = Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp", true);
            inputsType = Type.GetType("StarterAssets.StarterAssetsInputs, Assembly-CSharp", true);
            pauseMenuType = Type.GetType("PauseMenuController, Assembly-CSharp", true);
            controlTipType = Type.GetType("ControlTipCardUI, Assembly-CSharp", true);
            wrongTrashType = Type.GetType("WrongTrashPopup, Assembly-CSharp", true);
            loadingScreenType = Type.GetType("LoadingScreenController, Assembly-CSharp", true);
            cursorStateType = Type.GetType("CursorState, Assembly-CSharp", true);
            uiImageType = Type.GetType("UnityEngine.UI.Image, UnityEngine.UI", true);

            progressKeys = (string[])saveKeysType.GetField("AllTutorialProgressKeys", StaticFlags).GetValue(null);
            hungerRemainingKey = (string)saveKeysType.GetField("PlayerHungerRemaining", StaticFlags).GetValue(null);
            warn10Key = (string)saveKeysType.GetField("PlayerHungerWarn10Shown", StaticFlags).GetValue(null);
            warn5Key = (string)saveKeysType.GetField("PlayerHungerWarn5Shown", StaticFlags).GetValue(null);
            warn0Key = (string)saveKeysType.GetField("PlayerHungerWarn0Shown", StaticFlags).GetValue(null);

            warningEvent = survivalStatsType.GetEvent("OnHungerRumbleWarning", StaticFlags);
        }

        [SetUp]
        public void SetUp()
        {
            createdObjects.Clear();
            createdTempScenes.Clear();
            subscribedHandlers.Clear();
            originalActiveScene = SceneManager.GetActiveScene();
            BackupPrefs();
            saveKeysType.GetMethod("ResetAllPlayerProgress", StaticFlags).Invoke(null, null);

            Time.timeScale = 1f;
            SetPaused(false);
            SetLoading(false);
            cursorStateType.GetMethod("ResetForSceneOrTest", StaticFlags)?.Invoke(null, null);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = subscribedHandlers.Count - 1; i >= 0; i--)
            {
                warningEvent.RemoveEventHandler(null, subscribedHandlers[i].Handler);
            }
            subscribedHandlers.Clear();

            Time.timeScale = 1f;
            SetPaused(false);
            SetLoading(false);
            cursorStateType.GetMethod("ResetForSceneOrTest", StaticFlags)?.Invoke(null, null);

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
        public IEnumerator Scenario1And2_Crossing10And5Thresholds_ShowsEachNotificationOnceWithoutSpam()
        {
            List<int> warnings = RecordWarnings();
            (Component stats, Component hud) = CreateMinimalStatsAndHud(50f);
            yield return null;

            string expected10 = (string)hudType.GetField("_hungerWarn10Message", InstanceFlags).GetValue(hud);
            string expected5 = (string)hudType.GetField("_hungerWarn5Message", InstanceFlags).GetValue(hud);

            // Drop 50 -> 9: triggers 10% warning once
            SetHungerImmediate(stats, 9f, true);
            yield return null;

            Assert.AreEqual(new[] { 10 }, warnings.ToArray(), "Crossing <= 10% should emit exactly one 10% warning.");
            Assert.IsTrue(IsToastVisible(hud), "10% warning banner should become visible.");
            Assert.AreEqual(expected10, GetToastMessage(hud));

            // Subsequent drops within (5%, 10%] must not re-trigger
            SetHungerImmediate(stats, 8f, true);
            SetHungerImmediate(stats, 6f, true);
            yield return null;

            Assert.AreEqual(new[] { 10 }, warnings.ToArray(), "Further drops between 10% and 5% must not re-trigger the 10% warning.");

            // Drop 6 -> 4: triggers 5% warning once
            SetHungerImmediate(stats, 4f, true);
            yield return null;

            Assert.AreEqual(new[] { 10, 5 }, warnings.ToArray(), "Crossing <= 5% should emit one 5% warning.");
            Assert.IsTrue(IsToastVisible(hud), "5% warning banner should become visible.");
            Assert.AreEqual(expected5, GetToastMessage(hud));

            // Further drop 4 -> 2 must not re-trigger 5% warning
            SetHungerImmediate(stats, 2f, true);
            yield return null;
            Assert.AreEqual(new[] { 10, 5 }, warnings.ToArray());
        }

        [UnityTest]
        public IEnumerator Scenario3_AtZeroHunger_ShowsStarvingNotification_LocksSprintRegensStamina_AllowsWalkAndJump()
        {
            List<int> warnings = RecordWarnings();
            PlayerRig rig = CreateFullPlayerRig(50f);
            yield return null;

            // Verify normal sprinting works at 50% hunger
            inputsType.GetField("move", InstanceFlags).SetValue(rig.Inputs, Vector2.up);
            inputsType.GetField("sprint", InstanceFlags).SetValue(rig.Inputs, true);
            for (int i = 0; i < 6; i++) yield return null;

            Assert.IsTrue(GetBoolProp(survivalStatsType, rig.Stats, "CanSprint"), "Player should be allowed to sprint above 0% hunger.");
            Assert.IsTrue(GetBoolProp(sprintCtrlType, rig.SprintCtrl, "IsSprinting"), "PlayerSprintController should enter sprinting when holding Sprint.");
            float staminaBeforeStarve = GetFloatProp(survivalStatsType, rig.Stats, "CurrentStamina");
            Assert.Less(staminaBeforeStarve, 100f, "Sprinting above 0% hunger should drain stamina.");

            // Hit 0% while holding Move + Sprint
            SetHungerImmediate(rig.Stats, 0f, true);
            yield return null;

            string expected0 = (string)hudType.GetField("_hungerStarvingMessage", InstanceFlags).GetValue(rig.Hud);
            Assert.AreEqual(new[] { 0 }, warnings.ToArray(), "Jumping directly to 0% should emit a single starving warning.");
            Assert.IsTrue(IsToastVisible(rig.Hud));
            Assert.AreEqual(expected0, GetToastMessage(rig.Hud));
            Assert.IsTrue(GetBoolProp(survivalStatsType, rig.Stats, "IsStarving"));
            Assert.IsFalse(GetBoolProp(survivalStatsType, rig.Stats, "CanSprint"), "CanSprint must be false at 0% hunger.");
            Assert.IsFalse(GetBoolProp(sprintCtrlType, rig.SprintCtrl, "IsSprinting"), "IsSprinting must clear immediately when starving.");

            Vector3 startPos = rig.PlayerGo.transform.position;
            float staminaAtLock = GetFloatProp(survivalStatsType, rig.Stats, "CurrentStamina");
            float regenDelay = (float)survivalStatsType.GetField("staminaRegenDelay", InstanceFlags).GetValue(rig.Stats);

            float waited = 0f;
            while (waited < regenDelay + 0.35f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            float moveSpeed = (float)fpcType.GetField("MoveSpeed", InstanceFlags).GetValue(rig.Fpc);
            float sprintSpeed = (float)fpcType.GetField("SprintSpeed", InstanceFlags).GetValue(rig.Fpc);
            float horizSpeed = GetFloatProp(fpcType, rig.Fpc, "CurrentHorizontalSpeed");
            float staminaAfterLock = GetFloatProp(survivalStatsType, rig.Stats, "CurrentStamina");

            Assert.IsFalse(GetBoolProp(sprintCtrlType, rig.SprintCtrl, "IsSprinting"), "Holding Sprint at 0% hunger must remain locked to walk.");
            Assert.Greater(rig.PlayerGo.transform.position.z - startPos.z, 0.2f, "Player must still be able to walk while starving.");
            Assert.LessOrEqual(horizSpeed, moveSpeed + 0.35f, "Horizontal speed while starving must stay at walk speed.");
            Assert.Less(horizSpeed, sprintSpeed - 0.5f, "Starving walk speed must remain below sprint speed.");
            Assert.Greater(staminaAfterLock, staminaAtLock, "Stamina must stop draining and regenerate when starving locks sprint.");

            // Jump must still work while starving
            inputsType.GetField("jump", InstanceFlags).SetValue(rig.Inputs, true);
            yield return null;
            float vertVel = GetFloatProp(fpcType, rig.Fpc, "VerticalVelocity");
            Assert.Greater(vertVel, 0f, "Player must still be able to jump while starving.");
        }

        [UnityTest]
        public IEnumerator Scenario4_EatingAboveZeroRestoresSprintImmediately_AndHysteresisPreventsFlickerThenRearms()
        {
            List<int> warnings = RecordWarnings();
            PlayerRig rig = CreateFullPlayerRig(0f);
            yield return null;

            Assert.IsFalse(GetBoolProp(survivalStatsType, rig.Stats, "CanSprint"));

            // Eat small food restoring 2% hunger -> sprinting unlocks immediately
            MethodInfo addHunger = survivalStatsType.GetMethod("AddHunger", InstanceFlags);
            addHunger.Invoke(rig.Stats, new object[] { 2f });

            inputsType.GetField("move", InstanceFlags).SetValue(rig.Inputs, Vector2.up);
            inputsType.GetField("sprint", InstanceFlags).SetValue(rig.Inputs, true);
            yield return null;

            Assert.IsFalse(GetBoolProp(survivalStatsType, rig.Stats, "IsStarving"));
            Assert.IsTrue(GetBoolProp(survivalStatsType, rig.Stats, "CanSprint"), "CanSprint should unlock immediately once hunger > 0%.");
            Assert.IsTrue(GetBoolProp(sprintCtrlType, rig.SprintCtrl, "IsSprinting"), "Holding Sprint should resume sprinting once hunger > 0%.");

            // Trigger 10% and 5% cleanly, then test hysteresis buffer (+1%)
            warnings.Clear();
            SetHungerImmediate(rig.Stats, 50f, true);
            addHunger.Invoke(rig.Stats, new object[] { 0f });
            SetHungerImmediate(rig.Stats, 9f, true);
            Assert.AreEqual(new[] { 10 }, warnings.ToArray());

            // Recover to 10.5% (within hysteresis 10 + 1 = 11%): dropping back to 9.5% must NOT re-trigger
            addHunger.Invoke(rig.Stats, new object[] { 1.5f });
            SetHungerImmediate(rig.Stats, 9.5f, true);
            Assert.AreEqual(new[] { 10 }, warnings.ToArray(), "Hysteresis band (10%..11%) must prevent warning flicker.");

            // Eat to 12.5% (above hysteresis 11%): re-arms 10% warning so dropping back to 9.5% triggers again
            addHunger.Invoke(rig.Stats, new object[] { 3.0f });
            SetHungerImmediate(rig.Stats, 9.5f, true);
            Assert.AreEqual(new[] { 10, 10 }, warnings.ToArray(), "Eating cleanly past hysteresis must re-arm the 10% warning.");
        }

        [UnityTest]
        public IEnumerator Scenario5_LargeDropAndSaveLoadBehavior_EmitsSingleMostSevereWarningWithoutSceneReloadSpam()
        {
            List<int> warnings = RecordWarnings();

            // A) Big drain step 50 -> 0 triggers only starving (0%)
            (Component stats, Component hud) = CreateMinimalStatsAndHud(50f);
            yield return null;

            survivalStatsType.GetField("hungerDrainPerSec", InstanceFlags).SetValue(stats, 100f);
            survivalStatsType.GetMethod("StepHungerDrain", InstanceFlags).Invoke(stats, new object[] { 0.6f, false });
            yield return null;

            Assert.AreEqual(new[] { 0 }, warnings.ToArray(), "Dropping across 10/5/0 in one step must emit only the most severe (0%) warning.");
            Assert.AreEqual((string)hudType.GetField("_hungerStarvingMessage", InstanceFlags).GetValue(hud), GetToastMessage(hud));

            UnityEngine.Object.DestroyImmediate(stats.gameObject);
            UnityEngine.Object.DestroyImmediate(hud.gameObject);
            yield return null;

            // B) Loading an already-hungry save at 4% (warning flags unset) shows ONLY 5% warning once
            warnings.Clear();
            saveKeysType.GetMethod("ResetAllPlayerProgress", StaticFlags).Invoke(null, null);
            PlayerPrefs.SetFloat(hungerRemainingKey, 4f);
            PlayerPrefs.Save();

            GameObject statsGo = CreateStatsGo();
            GameObject hudGo = CreateHudGo();
            Component loadedStats = statsGo.GetComponent(survivalStatsType);
            Component loadedHud = hudGo.GetComponent(hudType);
            yield return null;
            yield return null;

            Assert.AreEqual(new[] { 5 }, warnings.ToArray(), "Loading a 4% hunger save with unset flags should emit only the 5% warning once.");
            Assert.IsTrue(IsToastVisible(loadedHud));
            Assert.AreEqual((string)hudType.GetField("_hungerWarn5Message", InstanceFlags).GetValue(loadedHud), GetToastMessage(loadedHud));
            Assert.AreEqual(1, PlayerPrefs.GetInt(warn5Key, 0), "Warn5 shown flag should be persisted after triggering.");
            Assert.AreEqual(1, PlayerPrefs.GetInt(warn10Key, 0), "Lower severity Warn10 flag should also be marked shown so it won't fire retroactively.");

            UnityEngine.Object.DestroyImmediate(statsGo);
            UnityEngine.Object.DestroyImmediate(hudGo);
            yield return null;

            // C) Scene reload at 4% hunger with Warn5 flag already persisted must not re-fire
            warnings.Clear();
            GameObject reloadedStatsGo = CreateStatsGo();
            GameObject reloadedHudGo = CreateHudGo();
            Component reloadedHud = reloadedHudGo.GetComponent(hudType);
            yield return null;
            yield return null;

            Assert.AreEqual(0, warnings.Count, "Scene reload with Warn5Shown == 1 must not re-fire the warning.");
            Assert.IsFalse(IsToastVisible(reloadedHud));
        }

        [UnityTest]
        public IEnumerator Scenario6_QueueingAndSuppression_WaitsForBlockers_DiscardsIfHealed_AndRespectsMenuAndLoading()
        {
            (Component stats, Component hud) = CreateMinimalStatsAndHud(50f);

            // Build WrongTrashPopup and ControlTipCardUI blockers on a Canvas
            var canvasGo = TrackObject(new GameObject("TestBlockerCanvas", typeof(RectTransform), typeof(Canvas)));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var wrongGo = TrackObject(new GameObject("TestWrongTrash", typeof(RectTransform)));
            wrongGo.transform.SetParent(canvasGo.transform, false);
            var wrongPopup = wrongGo.AddComponent(wrongTrashType);

            var tipGo = TrackObject(new GameObject("TestControlTipCard", typeof(RectTransform), typeof(CanvasGroup)));
            tipGo.transform.SetParent(canvasGo.transform, false);
            var tipCard = tipGo.AddComponent(controlTipType);

            yield return null;
            hudType.GetMethod("RefreshCachedBlockers", InstanceFlags).Invoke(hud, null);

            // A) Paused blocks immediate display, then displays once unpaused
            SetPaused(true);
            SetHungerImmediate(stats, 9f, true);
            yield return null;
            Assert.IsFalse(IsToastVisible(hud), "Hunger warning must queue instead of showing while paused.");
            Assert.IsTrue(HasPendingWarning(hud));

            SetPaused(false);
            yield return null;
            Assert.IsTrue(IsToastVisible(hud), "Queued hunger warning must display once unpaused.");
            Assert.IsFalse(HasPendingWarning(hud));

            // B) WrongTrashPopup blocks and then allows queued 5% warning after Hide
            ResetHudAndStatsForSubcase(stats, hud, 50f);
            wrongTrashType.GetMethod("Show", InstanceFlags).Invoke(wrongPopup, new object[] { "Wrong bin" });
            SetHungerImmediate(stats, 4f, true);
            yield return null;
            Assert.IsFalse(IsToastVisible(hud), "Hunger warning must not display while WrongTrashPopup is visible.");
            Assert.IsTrue(HasPendingWarning(hud), "Hunger warning must queue while WrongTrashPopup is visible.");

            wrongTrashType.GetMethod("Hide", InstanceFlags).Invoke(wrongPopup, null);
            yield return null;
            Assert.IsTrue(IsToastVisible(hud));
            Assert.AreEqual((string)hudType.GetField("_hungerWarn5Message", InstanceFlags).GetValue(hud), GetToastMessage(hud));

            // C) ControlTipCardUI (tutorial step banner) is neither hidden nor overlapped while active
            ResetHudAndStatsForSubcase(stats, hud, 50f);
            controlTipType.GetMethod("RestoreVisible", InstanceFlags).Invoke(tipCard, null);
            SetHungerImmediate(stats, 9f, true);
            yield return null;

            Assert.AreNotEqual("Hidden", controlTipType.GetProperty("CurrentState", InstanceFlags).GetValue(tipCard).ToString(),
                "Hunger warning must never hide or dismiss an active tutorial tip banner.");
            Assert.IsFalse(IsToastVisible(hud), "Hunger warning must not overlap an active tutorial banner.");
            Assert.IsTrue(HasPendingWarning(hud), "Hunger warning must wait until tutorial banner hides.");

            controlTipType.GetMethod("SetImmediateHidden", InstanceFlags).Invoke(tipCard, null);
            yield return null;
            Assert.IsTrue(IsToastVisible(hud), "Queued warning should show once ControlTipCardUI hides.");

            // D) Healing above threshold while queued discards stale warning cleanly
            ResetHudAndStatsForSubcase(stats, hud, 50f);
            SetPaused(true);
            SetHungerImmediate(stats, 9f, true);
            yield return null;
            Assert.IsTrue(HasPendingWarning(hud));

            survivalStatsType.GetMethod("AddHunger", InstanceFlags).Invoke(stats, new object[] { 10f }); // 19% > 10%
            SetPaused(false);
            yield return null;
            Assert.IsFalse(HasPendingWarning(hud), "Stale queued warning must be discarded when player eats above threshold.");
            Assert.IsFalse(IsToastVisible(hud));

            // E) Loading screen & Main Menu suppress hunger warnings
            ResetHudAndStatsForSubcase(stats, hud, 50f);
            SetLoading(true);
            SetHungerImmediate(stats, 9f, true);
            yield return null;
            Assert.IsFalse(IsToastVisible(hud), "Must not display warning during LoadingScreen.");
            Assert.IsTrue(HasPendingWarning(hud), "Loading screen blocks immediate display cleanly.");
            SetLoading(false);

            // Clear pending queue, switch to a temp Main Menu scene -> completely suppressed
            ResetHudAndStatsForSubcase(stats, hud, 50f);
            yield return null;

            Scene menuScene = CreateTempScene("Main Menu");
            SceneManager.SetActiveScene(menuScene);
            SetHungerImmediate(stats, 9f, true);
            yield return null;
            Assert.IsFalse(HasPendingWarning(hud), "Main Menu scene must suppress hunger warnings completely.");
            Assert.IsFalse(IsToastVisible(hud));
        }

        [UnityTest]
        public IEnumerator Scenario7_HudBarColorTurnsCriticalRedOnlyAtOrBelow10Percent()
        {
            var imgGo = TrackObject(new GameObject("HungerFillTest", typeof(RectTransform)));
            var fillImage = imgGo.AddComponent(uiImageType);

            (Component stats, Component hud) = CreateMinimalStatsAndHud(15f);
            hudType.GetField("_hungerFillImage", InstanceFlags).SetValue(hud, fillImage);
            hudType.GetMethod("RefreshImmediateFromStats", InstanceFlags).Invoke(hud, null);
            yield return null;

            Color expectedWarning = (Color)hudType.GetField("_hungerWarningColor", InstanceFlags).GetValue(hud);
            Color actualAt15 = (Color)uiImageType.GetProperty("color", InstanceFlags).GetValue(fillImage);
            Assert.AreEqual(expectedWarning.g, actualAt15.g, 0.05f, "At 15% (> 10%), hunger bar should use warning yellow rather than critical red.");

            SetHungerImmediate(stats, 10f, true);
            hudType.GetMethod("RefreshImmediateFromStats", InstanceFlags).Invoke(hud, null);
            yield return null;

            Color actualAt10 = (Color)uiImageType.GetProperty("color", InstanceFlags).GetValue(fillImage);
            Assert.Less(actualAt10.g, 0.35f, "At <= 10%, hunger bar should switch into red critical pulse.");
            Assert.Greater(actualAt10.r, 0.60f);
        }

        private struct PlayerRig
        {
            public GameObject PlayerGo;
            public Component Stats;
            public Component Hud;
            public Component SprintCtrl;
            public Component Fpc;
            public Component Inputs;
        }

        private PlayerRig CreateFullPlayerRig(float startHunger)
        {
            var floor = TrackObject(GameObject.CreatePrimitive(PrimitiveType.Plane));
            floor.name = "TestFloor";
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(10f, 1f, 10f);

            var camGo = TrackObject(new GameObject("MainCamera", typeof(Camera)));
            camGo.tag = "MainCamera";

            var playerGo = TrackObject(new GameObject("TestPlayer"));
            playerGo.SetActive(false);
            playerGo.transform.position = new Vector3(0f, 0.05f, 0f);

            var camTarget = new GameObject("CinemachineCameraTarget");
            camTarget.transform.SetParent(playerGo.transform, false);

            var cc = playerGo.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var playerInput = playerGo.AddComponent<PlayerInput>();
            var inputAsset = TrackObject(ScriptableObject.CreateInstance<InputActionAsset>());
            var map = new InputActionMap("Player");
            map.AddAction("Look", InputActionType.Value, binding: "<Pointer>/delta");
            map.AddAction("Move", InputActionType.Value, binding: "<Keyboard>/w");
            map.AddAction("Jump", InputActionType.Button, binding: "<Keyboard>/space");
            map.AddAction("Sprint", InputActionType.Button, binding: "<Keyboard>/leftShift");
            inputAsset.AddActionMap(map);
            playerInput.actions = inputAsset;
            playerInput.defaultActionMap = "Player";
            playerInput.notificationBehavior = PlayerNotifications.SendMessages;

            Component inputs = playerGo.AddComponent(inputsType);
            Component stats = playerGo.AddComponent(survivalStatsType);
            Component fpc = playerGo.AddComponent(fpcType);
            Component sprintCtrl = playerGo.AddComponent(sprintCtrlType);

            fpcType.GetField("CinemachineCameraTarget", InstanceFlags).SetValue(fpc, camTarget);
            fpcType.GetField("GroundLayers", InstanceFlags).SetValue(fpc, (LayerMask)(~0));

            survivalStatsType.GetField("hungerDrainPerSec", InstanceFlags).SetValue(stats, 0f);
            survivalStatsType.GetField("staminaRegenDelay", InstanceFlags).SetValue(stats, 0.05f);

            PlayerPrefs.SetFloat(hungerRemainingKey, startHunger);
            PlayerPrefs.Save();

            playerGo.SetActive(true);
            SetHungerImmediate(stats, startHunger, false);

            GameObject hudGo = CreateHudGo();
            return new PlayerRig
            {
                PlayerGo = playerGo,
                Stats = stats,
                Hud = hudGo.GetComponent(hudType),
                SprintCtrl = sprintCtrl,
                Fpc = fpc,
                Inputs = inputs
            };
        }

        private (Component Stats, Component Hud) CreateMinimalStatsAndHud(float startHunger)
        {
            PlayerPrefs.SetFloat(hungerRemainingKey, startHunger);
            PlayerPrefs.Save();

            GameObject statsGo = CreateStatsGo();
            GameObject hudGo = CreateHudGo();
            Component stats = statsGo.GetComponent(survivalStatsType);
            survivalStatsType.GetField("hungerDrainPerSec", InstanceFlags).SetValue(stats, 0f);

            // Ensure tests start with clean warning state matching startHunger > 10%
            if (startHunger > 11f)
            {
                SetHungerImmediate(stats, startHunger, false);
                survivalStatsType.GetMethod("ResetNeedsToFull", InstanceFlags).Invoke(stats, null);
                SetHungerImmediate(stats, startHunger, false);
            }

            return (stats, hudGo.GetComponent(hudType));
        }

        private GameObject CreateStatsGo()
        {
            var go = TrackObject(new GameObject("TestPlayerStats"));
            Component stats = go.AddComponent(survivalStatsType);
            survivalStatsType.GetField("hungerDrainPerSec", InstanceFlags).SetValue(stats, 0f);
            return go;
        }

        private GameObject CreateHudGo()
        {
            var go = TrackObject(new GameObject("TestStatsHUD", typeof(RectTransform), typeof(Canvas)));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            Component hud = go.AddComponent(hudType);
            hudType.GetField("_hungerWarningDuration", InstanceFlags).SetValue(hud, 2.0f);
            return go;
        }

        private List<int> RecordWarnings()
        {
            var list = new List<int>();
            Action<int> cb = threshold => list.Add(threshold);
            Delegate handler = Delegate.CreateDelegate(warningEvent.EventHandlerType, cb.Target, cb.Method);
            warningEvent.AddEventHandler(null, handler);
            subscribedHandlers.Add((handler, cb));
            return list;
        }

        private void SetHungerImmediate(Component stats, float hunger, bool evaluateWarnings)
        {
            survivalStatsType.GetMethod("SetHungerImmediate", InstanceFlags).Invoke(stats, new object[] { hunger, evaluateWarnings });
        }

        private void ResetHudAndStatsForSubcase(Component stats, Component hud, float hunger)
        {
            hud.gameObject.SetActive(false);
            hud.gameObject.SetActive(true);
            survivalStatsType.GetMethod("ResetNeedsToFull", InstanceFlags).Invoke(stats, null);
            SetHungerImmediate(stats, hunger, false);
        }

        private bool IsToastVisible(Component hud)
        {
            return (bool)hudType.GetProperty("IsNeedsToastVisible", InstanceFlags).GetValue(hud);
        }

        private string GetToastMessage(Component hud)
        {
            return (string)hudType.GetProperty("CurrentNeedsToastMessage", InstanceFlags).GetValue(hud);
        }

        private bool HasPendingWarning(Component hud)
        {
            return (bool)hudType.GetProperty("HasPendingHungerWarning", InstanceFlags).GetValue(hud);
        }

        private bool GetBoolProp(Type t, object obj, string propName)
        {
            return (bool)t.GetProperty(propName, InstanceFlags).GetValue(obj);
        }

        private float GetFloatProp(Type t, object obj, string propName)
        {
            return (float)t.GetProperty(propName, InstanceFlags).GetValue(obj);
        }

        private void SetPaused(bool paused)
        {
            pauseMenuType.GetProperty("IsPaused", StaticFlags)?.SetValue(null, paused);
        }

        private void SetLoading(bool loading)
        {
            loadingScreenType.GetField("_isLoading", StaticFlags)?.SetValue(null, loading);
        }

        private Scene CreateTempScene(string sceneName)
        {
            Scene s = SceneManager.CreateScene(sceneName);
            createdTempScenes.Add(s);
            return s;
        }

        private T TrackObject<T>(T obj) where T : UnityEngine.Object
        {
            if (obj != null) createdObjects.Add(obj);
            return obj;
        }

        private void BackupPrefs()
        {
            savedStringPrefs.Clear();
            savedIntPrefs.Clear();
            savedFloatPrefs.Clear();

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
            PlayerPrefs.Save();
        }
    }
}
