using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace EcoQuest.Tests.PlayMode
{
    public class CtrlCameraSpinPlayModeTests : InputTestFixture
    {
        private static readonly BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private Type cursorStateType;
        private Type inputsType;
        private Type fpcType;
        private Type quickAccessType;
        private Type pauseMenuType;
        private Type minimapType;

        private PropertyInfo lookEnabledProp;
        private PropertyInfo isCtrlUnlockProp;
        private PropertyInfo isRelockSuppressedProp;
        private MethodInfo resetCursorStateMethod;
        private MethodInfo setLockedMethod;

        private FieldInfo inputsLookField;
        private FieldInfo inputsCursorLockedField;
        private FieldInfo inputsCursorInputForLookField;
        private FieldInfo fpcCameraTargetField;
        private FieldInfo fpcRotationSpeedField;

        private Mouse mouse;
        private Keyboard keyboard;
        private GameObject playerGo;
        private GameObject cameraTargetGo;
        private GameObject mainCamGo;
        private InputActionAsset inputAsset;
        private InputAction lookAction;
        private Component inputsComponent;
        private Component fpcComponent;

        [OneTimeSetUp]
        public void CacheGameplayTypes()
        {
            cursorStateType = Type.GetType("CursorState, Assembly-CSharp", true);
            inputsType = Type.GetType("StarterAssets.StarterAssetsInputs, Assembly-CSharp", true);
            fpcType = Type.GetType("StarterAssets.FirstPersonController, Assembly-CSharp", true);
            quickAccessType = Type.GetType("CursorQuickAccessController, Assembly-CSharp", true);
            pauseMenuType = Type.GetType("PauseMenuController, Assembly-CSharp", true);
            minimapType = Type.GetType("MinimapController, Assembly-CSharp", true);

            lookEnabledProp = cursorStateType.GetProperty("LookEnabled", StaticFlags);
            isCtrlUnlockProp = cursorStateType.GetProperty("IsCtrlUnlockActive", StaticFlags);
            isRelockSuppressedProp = cursorStateType.GetProperty("IsRelockSuppressed", StaticFlags);
            resetCursorStateMethod = cursorStateType.GetMethod("ResetForSceneOrTest", StaticFlags);
            setLockedMethod = cursorStateType.GetMethod("SetLocked", StaticFlags);

            inputsLookField = inputsType.GetField("look", InstanceFlags);
            inputsCursorLockedField = inputsType.GetField("cursorLocked", InstanceFlags);
            inputsCursorInputForLookField = inputsType.GetField("cursorInputForLook", InstanceFlags);
            fpcCameraTargetField = fpcType.GetField("CinemachineCameraTarget", InstanceFlags);
            fpcRotationSpeedField = fpcType.GetField("RotationSpeed", InstanceFlags);
        }

        [UnitySetUp]
        public IEnumerator SetUpRig()
        {
            Time.timeScale = 1f;
            SetMinimapStepActive(false);
            resetCursorStateMethod.Invoke(null, null);

            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();

            mainCamGo = new GameObject("MainCamera");
            mainCamGo.tag = "MainCamera";
            mainCamGo.AddComponent<Camera>();

            playerGo = new GameObject("TestPlayer");
            playerGo.SetActive(false);

            cameraTargetGo = new GameObject("CinemachineCameraTarget");
            cameraTargetGo.transform.SetParent(playerGo.transform, false);

            playerGo.AddComponent<CharacterController>();
            var playerInput = playerGo.AddComponent<PlayerInput>();
            inputsComponent = playerGo.AddComponent(inputsType);
            fpcComponent = playerGo.AddComponent(fpcType);
            playerGo.AddComponent(quickAccessType);

            inputAsset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Player");
            lookAction = map.AddAction("Look", InputActionType.Value, binding: "<Pointer>/delta");
            map.AddAction("Move", InputActionType.Value, binding: "<Keyboard>/w");
            map.AddAction("Jump", InputActionType.Button, binding: "<Keyboard>/space");
            map.AddAction("Sprint", InputActionType.Button, binding: "<Keyboard>/leftShift");
            inputAsset.AddActionMap(map);

            playerInput.actions = inputAsset;
            playerInput.defaultActionMap = "Player";
            playerInput.notificationBehavior = PlayerNotifications.SendMessages;

            fpcCameraTargetField.SetValue(fpcComponent, cameraTargetGo);
            fpcRotationSpeedField.SetValue(fpcComponent, 1.0f);

            playerGo.SetActive(true);
            yield return null;

            setLockedMethod.Invoke(null, new object[] { true, inputsComponent, true });

            // Wait through initial 2-frame lock settle window.
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDownRig()
        {
            Time.timeScale = 1f;
            SetMinimapStepActive(false);
            resetCursorStateMethod?.Invoke(null, null);

            if (playerGo != null) UnityEngine.Object.DestroyImmediate(playerGo);
            if (mainCamGo != null) UnityEngine.Object.DestroyImmediate(mainCamGo);
            if (inputAsset != null) UnityEngine.Object.DestroyImmediate(inputAsset);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            yield return null;
        }

        private bool LookEnabled => (bool)lookEnabledProp.GetValue(null);
        private bool IsCtrlUnlockActive => (bool)isCtrlUnlockProp.GetValue(null);
        private bool IsRelockSuppressed => (bool)isRelockSuppressedProp.GetValue(null);
        private Vector2 CurrentLookInput => (Vector2)inputsLookField.GetValue(inputsComponent);

        private void SetMinimapStepActive(bool value)
        {
            minimapType?.GetProperty("IsMapStepActive", StaticFlags)?.SetValue(null, value);
        }

        private IEnumerator StepLookDelta(Vector2 delta, int frames)
        {
            Vector2 pos = mouse.position.ReadValue();
            for (int i = 0; i < frames; i++)
            {
                pos += delta;
                Move(mouse.position, pos, delta: delta, queueEventOnly: true);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator CameraLook_StopsImmediatelyOnCtrlUnlock_ZeroDriftDuringHold()
        {
            Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState, "PlayMode test requires GameView cursor lock capability.");
            Assert.IsTrue(LookEnabled, "LookEnabled should be true after settle frames.");

            Quaternion startYaw = playerGo.transform.rotation;
            yield return StepLookDelta(new Vector2(20f, 5f), 2);
            Assert.Greater(Quaternion.Angle(startYaw, playerGo.transform.rotation), 1.0f, "Baseline mouse look must rotate camera.");

            // Press Left CTRL while non-zero look velocity/delta was active.
            Press(keyboard.leftCtrlKey, queueEventOnly: true);
            yield return null;

            Assert.AreEqual(CursorLockMode.None, Cursor.lockState, "Cursor must unlock on CTRL press.");
            Assert.IsTrue(Cursor.visible, "Cursor must become visible on CTRL unlock.");
            Assert.IsTrue(IsCtrlUnlockActive, "CursorState.IsCtrlUnlockActive must be true while holding CTRL.");
            Assert.IsFalse(LookEnabled, "CursorState.LookEnabled must be false when CTRL unlocks cursor.");
            Assert.IsFalse(lookAction.enabled, "Look InputAction must be disabled while CTRL unlocks cursor.");
            Assert.AreEqual(Vector2.zero, CurrentLookInput, "Stored look input must be zeroed immediately on unlock.");

            Quaternion lockedYawSnapshot = playerGo.transform.rotation;
            Quaternion lockedPitchSnapshot = cameraTargetGo.transform.localRotation;

            // Even with ongoing mouse movement over 10 frames while holding CTRL, camera must not spin or drift.
            yield return StepLookDelta(new Vector2(45f, -15f), 10);

            Assert.Less(Quaternion.Angle(lockedYawSnapshot, playerGo.transform.rotation), 0.001f, "Camera yaw must stay frozen while CTRL is held.");
            Assert.Less(Quaternion.Angle(lockedPitchSnapshot, cameraTargetGo.transform.localRotation), 0.001f, "Camera pitch must stay frozen while CTRL is held.");
            Assert.AreEqual(Vector2.zero, CurrentLookInput);
            Assert.IsFalse(lookAction.enabled);
        }

        [UnityTest]
        public IEnumerator CameraLook_SuppressesRelockDeltaSpike_AndResumesAfterSuppression()
        {
            Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState);

            Press(keyboard.leftCtrlKey, queueEventOnly: true);
            yield return null;
            Assert.IsFalse(LookEnabled);

            // Release CTRL to re-lock cursor.
            Release(keyboard.leftCtrlKey, queueEventOnly: true);
            yield return null;

            Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState, "Cursor must re-lock on CTRL release.");
            Assert.IsFalse(Cursor.visible);
            Assert.IsFalse(IsCtrlUnlockActive);
            Assert.IsTrue(IsRelockSuppressed, "Re-lock spike suppression should be active immediately on transition.");

            Quaternion relockYawSnapshot = playerGo.transform.rotation;
            Quaternion relockPitchSnapshot = cameraTargetGo.transform.localRotation;

            // Inject a huge simulated OS warp delta spike across the 2 suppression frames.
            yield return StepLookDelta(new Vector2(600f, -400f), 2);

            Assert.Less(Quaternion.Angle(relockYawSnapshot, playerGo.transform.rotation), 0.001f, "Re-lock warp spike must not jump camera yaw.");
            Assert.Less(Quaternion.Angle(relockPitchSnapshot, cameraTargetGo.transform.localRotation), 0.001f, "Re-lock warp spike must not jump camera pitch.");

            // Wait one extra frame so Look action re-enables post-suppression, then verify normal look resumes.
            yield return null;
            Assert.IsFalse(IsRelockSuppressed);
            Assert.IsTrue(LookEnabled);
            Assert.IsTrue(lookAction.enabled, "Look action must re-enable after suppression window.");

            Quaternion resumeYawBefore = playerGo.transform.rotation;
            yield return StepLookDelta(new Vector2(15f, 0f), 2);
            Assert.Greater(Quaternion.Angle(resumeYawBefore, playerGo.transform.rotation), 0.5f, "Normal mouse look must resume after suppression window.");
        }

        [UnityTest]
        public IEnumerator CtrlPressAndRelease_WhileBlockingUIOpen_KeepsCursorUnlocked()
        {
            var pauseGo = new GameObject("PauseMenuController");
            var pauseCtrl = pauseGo.AddComponent(pauseMenuType);
            try
            {
                pauseMenuType.GetMethod("PauseGame", InstanceFlags).Invoke(pauseCtrl, null);
                yield return null;

                Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
                Assert.IsFalse(LookEnabled);

                Press(keyboard.leftCtrlKey, queueEventOnly: true);
                yield return null;
                Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
                Assert.IsFalse(IsCtrlUnlockActive, "CTRL quick-access must not hijack cursor while blocking UI is open.");

                Release(keyboard.leftCtrlKey, queueEventOnly: true);
                yield return null;

                Assert.AreEqual(CursorLockMode.None, Cursor.lockState, "Releasing CTRL while Pause is open must keep cursor unlocked.");
                Assert.IsTrue(Cursor.visible, "Cursor must stay visible while Pause is open after releasing CTRL.");
                Assert.IsFalse(LookEnabled);

                // Also verify active map tutorial step keeps cursor unlocked on CTRL release.
                SetMinimapStepActive(true);
                Press(keyboard.leftCtrlKey, queueEventOnly: true);
                yield return null;
                Release(keyboard.leftCtrlKey, queueEventOnly: true);
                yield return null;
                Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pauseGo);
                Time.timeScale = 1f;
            }
        }

        [UnityTest]
        public IEnumerator ApplicationFocusLoss_StopsCameraRotation_AndRelocksCleanlyOnRefocus()
        {
            Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState);

            playerGo.SendMessage("OnApplicationFocus", false);
            yield return null;

            Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
            Assert.IsFalse(LookEnabled);
            Assert.IsFalse(lookAction.enabled);

            Quaternion focusLostYaw = playerGo.transform.rotation;
            yield return StepLookDelta(new Vector2(50f, 20f), 3);
            Assert.Less(Quaternion.Angle(focusLostYaw, playerGo.transform.rotation), 0.001f);

            playerGo.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState);

            // Spike right on refocus frame must be swallowed by re-lock suppression.
            Quaternion refocusYaw = playerGo.transform.rotation;
            yield return StepLookDelta(new Vector2(400f, 0f), 2);
            Assert.Less(Quaternion.Angle(refocusYaw, playerGo.transform.rotation), 0.001f);

            yield return null;
            Assert.IsTrue(LookEnabled);
            yield return StepLookDelta(new Vector2(15f, 0f), 2);
            Assert.Greater(Quaternion.Angle(refocusYaw, playerGo.transform.rotation), 0.5f);
        }
    }
}
