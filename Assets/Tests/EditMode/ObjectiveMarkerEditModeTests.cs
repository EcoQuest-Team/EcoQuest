using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EcoQuest.Tests.EditMode
{
    public class ObjectiveMarkerEditModeTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private Type markerServiceType;
        private Type pauseMenuType;
        private Type quizUiType;
        private Type cursorStateType;

        private readonly List<GameObject> createdObjects = new List<GameObject>();
        private readonly List<Camera> disabledMainCameras = new List<Camera>();
        private readonly List<(MonoBehaviour comp, bool wasEnabled)> disabledServices = new List<(MonoBehaviour, bool)>();

        private Camera testCamera;
        private RenderTexture testRenderTexture;
        private Component markerService;
        private bool initialPaused;
        private bool initialQuizOpen;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            markerServiceType = Type.GetType("ObjectiveMarkerService, Assembly-CSharp", true);
            pauseMenuType = Type.GetType("PauseMenuController, Assembly-CSharp", true);
            quizUiType = Type.GetType("QuizUIController, Assembly-CSharp", true);
            cursorStateType = Type.GetType("CursorState, Assembly-CSharp", true);
        }

        [SetUp]
        public void SetUp()
        {
            createdObjects.Clear();
            disabledMainCameras.Clear();
            disabledServices.Clear();

            initialPaused = (bool)GetStaticProperty(pauseMenuType, "IsPaused");
            initialQuizOpen = (bool)GetStaticProperty(quizUiType, "IsAnyQuizOpen");
            SetStaticProperty(pauseMenuType, "IsPaused", false);
            SetStaticProperty(quizUiType, "IsAnyQuizOpen", false);
            InvokeStatic(cursorStateType, "ResetForSceneOrTest");

            // Temporarily isolate any scene cameras tagged MainCamera so Camera.main resolves deterministically.
            foreach (Camera cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (cam != null && cam.CompareTag("MainCamera") && cam.enabled)
                {
                    cam.enabled = false;
                    disabledMainCameras.Add(cam);
                }
            }

            foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsByType(markerServiceType, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (obj is MonoBehaviour mb && mb != null)
                {
                    disabledServices.Add((mb, mb.enabled));
                    mb.enabled = false;
                }
            }

            var camGo = new GameObject("TestMainCamera") { tag = "MainCamera" };
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.identity);
            testCamera = camGo.AddComponent<Camera>();
            testCamera.fieldOfView = 60f;
            testCamera.nearClipPlane = 0.1f;
            testCamera.farClipPlane = 500f;
            if (testCamera.pixelWidth < 2 || testCamera.pixelHeight < 2)
            {
                testRenderTexture = new RenderTexture(1920, 1080, 16);
                testRenderTexture.Create();
                testCamera.targetTexture = testRenderTexture;
            }
            createdObjects.Add(camGo);

            var serviceGo = new GameObject("TestObjectiveMarkerService");
            createdObjects.Add(serviceGo);
            markerService = serviceGo.AddComponent(markerServiceType);
            InvokeLifecycle(markerService, "Awake");
            InvokeLifecycle(markerService, "OnEnable");
            InvokeLifecycle(markerService, "Start");
            InvokeInstance(markerService, "CacheSceneReferences");
            // Isolate from any scene Player (e.g. Forest Scene left open by earlier EditMode tests).
            markerServiceType.GetField("playerTransform", InstanceFlags)?.SetValue(markerService, camGo.transform);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            SetStaticProperty(pauseMenuType, "IsPaused", initialPaused);
            SetStaticProperty(quizUiType, "IsAnyQuizOpen", initialQuizOpen);
            InvokeStatic(cursorStateType, "ResetForSceneOrTest");

            if (markerService != null)
            {
                InvokeLifecycle(markerService, "OnDisable");
                InvokeLifecycle(markerService, "OnDestroy");
                markerService = null;
            }

            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
                }
            }
            if (testCamera != null) testCamera.targetTexture = null;
            if (testRenderTexture != null)
            {
                testRenderTexture.Release();
                UnityEngine.Object.DestroyImmediate(testRenderTexture);
                testRenderTexture = null;
            }
            createdObjects.Clear();

            foreach (Camera cam in disabledMainCameras)
            {
                if (cam != null) cam.enabled = true;
            }
            disabledMainCameras.Clear();

            foreach ((MonoBehaviour comp, bool wasEnabled) in disabledServices)
            {
                if (comp != null) comp.enabled = wasEnabled;
            }
            disabledServices.Clear();
        }

        [Test]
        public void CreatedUiHierarchy_ConfiguresResponsiveScalerSpriteAndNonBlockingRaycasts()
        {
            Canvas canvas = markerService.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas, "ObjectiveMarkerService should create an overlay Canvas.");
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);

            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler);
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
            Assert.AreEqual(new Vector2(1920f, 1080f), scaler.referenceResolution);
            Assert.AreEqual(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight, scaler.screenMatchMode);
            Assert.AreEqual(0.5f, scaler.matchWidthOrHeight, 0.001f);

            Assert.IsNull(canvas.GetComponent<GraphicRaycaster>(), "Indicator canvas must not intercept UI pointer clicks.");

            Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(true);
            Assert.IsNotEmpty(graphics);
            foreach (Graphic g in graphics)
            {
                Assert.IsFalse(g.raycastTarget, $"Graphic '{g.name}' must have raycastTarget disabled.");
            }

            Image pointerImage = FindChildComponent<Image>(canvas.transform, "PointerIcon");
            Assert.IsNotNull(pointerImage, "PointerIcon Image should exist.");
            Assert.IsNotNull(pointerImage.sprite, "PointerIcon must use a crisp arrow sprite, not a solid square.");

            TMP_Text distanceText = FindChildComponent<TMP_Text>(canvas.transform, "DistanceText");
            Assert.IsNotNull(distanceText, "DistanceText TMP component should exist.");
        }

        [Test]
        public void ClearOnScreenTarget_ShowsWorldArrowAndDistanceMeterWithoutScreenEdgeArrow()
        {
            Transform target = CreateTarget("Guardian", new Vector3(0f, 1.6f, 20f));
            InvokeInstance(markerService, "SetTarget", target, "Guardian");
            Tick(20, 0.05f);

            Assert.IsFalse(GetProp<bool>("IsSuppressed"));
            Assert.IsTrue(GetProp<bool>("LastIsOnScreen"));
            Assert.IsFalse(GetProp<bool>("LastIsArrowOccluded"));
            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"), "World arrow should show for unobstructed on-screen target.");
            Assert.IsTrue(GetProp<bool>("IsScreenIndicatorActive"), "Distance indicator root should stay visible on screen.");
            Assert.IsFalse(GetProp<bool>("IsScreenArrowActive"), "Off-screen/occluded pointer arrow should hide when world arrow is clearly visible.");
            Assert.IsTrue(GetProp<bool>("IsDistanceTextActive"), "Distance meter should remain visible on screen.");
            Assert.AreEqual("20m", GetProp<string>("CurrentDistanceText"));
            AssertFinite(GetProp<Vector2>("CurrentIndicatorCanvasPosition"));
        }

        [Test]
        public void OccludedOnScreenTarget_HidesWorldArrowAndKeepsScreenArrowAndDistanceMeterVisible()
        {
            Transform target = CreateTarget("Robin", new Vector3(0f, 1.6f, 20f));
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "OcclusionWall";
            wall.transform.position = new Vector3(0f, 1.6f, 10f);
            wall.transform.localScale = new Vector3(6f, 6f, 1f);
            createdObjects.Add(wall);
            Physics.SyncTransforms();

            InvokeInstance(markerService, "SetTarget", target, "Ranger Robin");
            Tick(20, 0.05f);

            Assert.IsTrue(GetProp<bool>("LastIsOnScreen"));
            Assert.IsTrue(GetProp<bool>("LastIsArrowOccluded"), "Wall collider between camera and target should trigger occlusion.");
            Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"), "Occluded 3D world arrow should yield to screen indicator.");
            Assert.IsTrue(GetProp<bool>("IsScreenIndicatorActive"));
            Assert.IsTrue(GetProp<bool>("IsScreenArrowActive"), "Overlay pointer icon should remain visible over blocking geometry.");
            Assert.IsTrue(GetProp<bool>("IsDistanceTextActive"), "Distance meter must remain visible when geometry blocks line of sight.");
            Assert.AreEqual("20m", GetProp<string>("CurrentDistanceText"));

            // Removing the obstacle should seamlessly restore the 3D world arrow.
            wall.SetActive(false);
            Physics.SyncTransforms();
            Tick(20, 0.05f);

            Assert.IsFalse(GetProp<bool>("LastIsArrowOccluded"));
            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"));
            Assert.IsFalse(GetProp<bool>("IsScreenArrowActive"));
            Assert.IsTrue(GetProp<bool>("IsDistanceTextActive"));
        }

        [Test]
        public void OffScreenAndDirectlyBehindTarget_ClampsSafelyWithFiniteEdgePositionAndDistance()
        {
            Vector3[] offScreenPositions =
            {
                new Vector3(-30f, 1.6f, 5f),   // Far left
                new Vector3(30f, 1.6f, 5f),    // Far right
                new Vector3(0f, 30f, 5f),      // Above view
                new Vector3(0f, -30f, 5f),     // Below view
                new Vector3(0f, 1.6f, -20f),   // Directly behind camera (180 degrees)
                new Vector3(-25f, 25f, 5f),    // Top-left HUD corner direction
                new Vector3(25f, 25f, 5f),     // Top-right HUD corner direction
            };

            Transform target = CreateTarget("OffScreenNpc", offScreenPositions[0]);
            InvokeInstance(markerService, "SetTarget", target, "NPC");

            foreach (Vector3 pos in offScreenPositions)
            {
                target.position = pos;
                Tick(10, 0.05f);

                Assert.IsFalse(GetProp<bool>("LastIsOnScreen"), $"Position {pos} should evaluate as off-screen.");
                Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"), $"World arrow should not show when off-screen at {pos}.");
                Assert.IsTrue(GetProp<bool>("IsScreenIndicatorActive"), $"Screen indicator should stay visible off-screen at {pos}.");
                Assert.IsTrue(GetProp<bool>("IsScreenArrowActive"), $"Edge pointer arrow should show off-screen at {pos}.");
                Assert.IsTrue(GetProp<bool>("IsDistanceTextActive"), $"Distance text should stay visible off-screen at {pos}.");

                string distText = GetProp<string>("CurrentDistanceText");
                Assert.IsNotNull(distText);
                Assert.IsTrue(distText.EndsWith("m", StringComparison.Ordinal) && !distText.StartsWith("-", StringComparison.Ordinal),
                    $"Unexpected distance format '{distText}' at {pos}.");

                Vector2 canvasPos = GetProp<Vector2>("CurrentIndicatorCanvasPosition");
                AssertFinite(canvasPos);
                Assert.That(Mathf.Abs(canvasPos.x), Is.LessThan(960f), $"Edge X {canvasPos.x} exceeded canvas bounds at {pos}.");
                Assert.That(Mathf.Abs(canvasPos.y), Is.LessThan(540f), $"Edge Y {canvasPos.y} exceeded canvas bounds at {pos}.");
                Assert.That(canvasPos.magnitude, Is.GreaterThan(50f), $"Indicator should be pushed to perimeter edge at {pos}.");
            }
        }

        [Test]
        public void CloseTarget_HidesInsideThresholdAndFormatsZeroMetersWithoutNegative()
        {
            Transform target = CreateTarget("CloseNpc", new Vector3(0f, 1.6f, 0.5f));
            SetProp("HideDistanceThreshold", 2.0f);
            InvokeInstance(markerService, "SetTarget", target, "Guardian");
            Tick(20, 0.05f);

            Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"), "Marker should hide when standing inside HideDistanceThreshold.");
            Assert.IsFalse(GetProp<bool>("IsScreenIndicatorActive"));
            Assert.IsFalse(GetProp<bool>("IsDistanceTextActive"));

            // Lower threshold to inspect close "0m" rounding without negative numbers.
            SetProp("HideDistanceThreshold", 0.1f);
            target.position = new Vector3(0f, 1.6f, 0.35f);
            Tick(20, 0.05f);

            Assert.IsTrue(GetProp<bool>("IsDistanceTextActive"));
            Assert.AreEqual("0m", GetProp<string>("CurrentDistanceText"));
        }

        [Test]
        public void BlockingOverlaysSuppressMarkers_WhileCtrlCursorUnlockKeepsMarkersVisible()
        {
            Transform target = CreateTarget("Guardian", new Vector3(0f, 1.6f, 15f));
            InvokeInstance(markerService, "SetTarget", target, "Guardian");
            Tick(20, 0.05f);
            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"));

            SetStaticProperty(pauseMenuType, "IsPaused", true);
            Tick(20, 0.05f);
            Assert.IsTrue(GetProp<bool>("IsSuppressed"));
            Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"));
            Assert.IsFalse(GetProp<bool>("IsScreenIndicatorActive"));
            Assert.IsFalse(GetProp<bool>("IsDistanceTextActive"));

            SetStaticProperty(pauseMenuType, "IsPaused", false);
            Tick(20, 0.05f);
            Assert.IsFalse(GetProp<bool>("IsSuppressed"));
            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"));

            SetStaticProperty(quizUiType, "IsAnyQuizOpen", true);
            Tick(20, 0.05f);
            Assert.IsTrue(GetProp<bool>("IsSuppressed"));
            Assert.IsFalse(GetProp<bool>("IsScreenIndicatorActive"));

            SetStaticProperty(quizUiType, "IsAnyQuizOpen", false);
            Tick(20, 0.05f);
            Assert.IsFalse(GetProp<bool>("IsSuppressed"));
            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"));

            // Holding CTRL to unlock cursor is not a blocking modal menu and must preserve navigation cues.
            InvokeStatic(cursorStateType, "SetCtrlUnlock", true, null);
            Tick(10, 0.05f);
            Assert.IsFalse((bool)InvokeStatic(cursorStateType, "IsAnyBlockingUIOpen"));
            Assert.IsFalse(GetProp<bool>("IsSuppressed"));
            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"));
            Assert.IsTrue(GetProp<bool>("IsDistanceTextActive"));
        }

        [Test]
        public void DestroyedTarget_ClearsTargetAndHidesVisualsWithoutErrors()
        {
            Transform target = CreateTarget("TempNpc", new Vector3(0f, 1.6f, 12f));
            InvokeInstance(markerService, "SetTarget", target, "NPC");
            Tick(20, 0.05f);
            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"));

            UnityEngine.Object.DestroyImmediate(target.gameObject);
            Tick(10, 0.05f);

            Assert.IsNull(GetProp<Transform>("CurrentTarget"));
            Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"));
            Assert.IsFalse(GetProp<bool>("IsScreenIndicatorActive"));
            Assert.IsFalse(GetProp<bool>("IsDistanceTextActive"));
        }

        private Transform CreateTarget(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            createdObjects.Add(go);
            return go.transform;
        }

        private void Tick(int steps, float dt)
        {
            for (int i = 0; i < steps; i++)
            {
                InvokeInstance(markerService, "TickMarkerVisuals", dt);
            }
        }

        private T GetProp<T>(string propName)
        {
            PropertyInfo prop = markerServiceType.GetProperty(propName, InstanceFlags);
            Assert.IsNotNull(prop, $"Property '{propName}' not found on ObjectiveMarkerService.");
            return (T)prop.GetValue(markerService);
        }

        private void SetProp<T>(string propName, T value)
        {
            PropertyInfo prop = markerServiceType.GetProperty(propName, InstanceFlags);
            Assert.IsNotNull(prop, $"Property '{propName}' not found on ObjectiveMarkerService.");
            prop.SetValue(markerService, value);
        }

        private static T FindChildComponent<T>(Transform root, string exactName) where T : Component
        {
            foreach (T comp in root.GetComponentsInChildren<T>(true))
            {
                if (comp != null && comp.gameObject.name == exactName) return comp;
            }
            return null;
        }

        private static void AssertFinite(Vector2 v)
        {
            Assert.IsFalse(float.IsNaN(v.x) || float.IsInfinity(v.x), $"Vector2.x was invalid: {v.x}");
            Assert.IsFalse(float.IsNaN(v.y) || float.IsInfinity(v.y), $"Vector2.y was invalid: {v.y}");
        }

        private static void InvokeLifecycle(object target, string methodName)
        {
            MethodInfo m = target.GetType().GetMethod(methodName, InstanceFlags);
            m?.Invoke(target, null);
        }

        private static object InvokeInstance(object target, string methodName, params object[] args)
        {
            MethodInfo m = target.GetType().GetMethod(methodName, InstanceFlags);
            Assert.IsNotNull(m, $"Method '{methodName}' not found on {target.GetType().Name}.");
            return m.Invoke(target, args);
        }

        private static object InvokeStatic(Type type, string methodName, params object[] args)
        {
            MethodInfo m = type.GetMethod(methodName, StaticFlags);
            Assert.IsNotNull(m, $"Static method '{methodName}' not found on {type.Name}.");
            return m.Invoke(null, args);
        }

        private static object GetStaticProperty(Type type, string propName)
        {
            PropertyInfo p = type.GetProperty(propName, StaticFlags);
            Assert.IsNotNull(p, $"Static property '{propName}' not found on {type.Name}.");
            return p.GetValue(null);
        }

        private static void SetStaticProperty(Type type, string propName, object value)
        {
            PropertyInfo p = type.GetProperty(propName, StaticFlags);
            Assert.IsNotNull(p, $"Static property '{propName}' not found on {type.Name}.");
            p.SetValue(null, value);
        }
    }
}
