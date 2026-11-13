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
    public class ObjectiveMarkerPlayModeTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private Type markerServiceType;
        private Type pauseMenuType;
        private Type cursorStateType;

        private readonly List<GameObject> createdObjects = new List<GameObject>();
        private GameObject serviceGo;
        private Component markerService;
        private Camera testCamera;
        private bool initialPaused;
        private float initialTimeScale;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            markerServiceType = Type.GetType("ObjectiveMarkerService, Assembly-CSharp", true);
            pauseMenuType = Type.GetType("PauseMenuController, Assembly-CSharp", true);
            cursorStateType = Type.GetType("CursorState, Assembly-CSharp", true);
        }

        [SetUp]
        public void SetUp()
        {
            createdObjects.Clear();
            initialPaused = (bool)GetStaticProperty(pauseMenuType, "IsPaused");
            initialTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            SetStaticProperty(pauseMenuType, "IsPaused", false);
            InvokeStatic(cursorStateType, "ResetForSceneOrTest");

            var camGo = new GameObject("PlayModeTestMainCamera") { tag = "MainCamera" };
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.identity);
            testCamera = camGo.AddComponent<Camera>();
            testCamera.fieldOfView = 60f;
            createdObjects.Add(camGo);

            serviceGo = new GameObject("PlayModeObjectiveMarkerService");
            markerService = serviceGo.AddComponent(markerServiceType);
            createdObjects.Add(serviceGo);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = initialTimeScale > 0f ? initialTimeScale : 1f;
            SetStaticProperty(pauseMenuType, "IsPaused", initialPaused);
            InvokeStatic(cursorStateType, "ResetForSceneOrTest");

            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
                }
            }
            createdObjects.Clear();
            serviceGo = null;
            markerService = null;
        }

        [UnityTest]
        public IEnumerator ServicePersistsInDontDestroyOnLoadAndClearsWhenTargetSceneUnloads()
        {
            yield return null;

            Assert.AreEqual("DontDestroyOnLoad", serviceGo.scene.name,
                "ObjectiveMarkerService should persist in DontDestroyOnLoad across scene loads.");

            Scene tempTargetScene = SceneManager.CreateScene("TempObjectiveTargetScene");
            var targetGo = new GameObject("TempSceneTarget");
            targetGo.transform.position = new Vector3(0f, 1.6f, 18f);
            SceneManager.MoveGameObjectToScene(targetGo, tempTargetScene);

            InvokeInstance(markerService, "SetTarget", targetGo.transform, "Temp NPC");
            yield return WaitFrames(15);

            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"));
            Assert.AreEqual("18m", GetProp<string>("CurrentDistanceText"));

            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(tempTargetScene);
            while (unloadOp != null && !unloadOp.isDone)
            {
                yield return null;
            }
            yield return WaitFrames(5);

            Assert.IsTrue(serviceGo != null && markerService != null, "Service should survive unloading of target scene.");
            Assert.IsNull(GetProp<Transform>("CurrentTarget"), "Destroyed target from unloaded scene must clear.");
            Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"));
            Assert.IsFalse(GetProp<bool>("IsScreenIndicatorActive"));
            Assert.IsFalse(GetProp<bool>("IsDistanceTextActive"));
        }

        [UnityTest]
        public IEnumerator DestroyingTargetMidPlay_ClearsTargetAndHidesIndicatorsCleanly()
        {
            yield return null;

            var targetGo = new GameObject("DestroyableNpc");
            targetGo.transform.position = new Vector3(0f, 1.6f, 15f);
            createdObjects.Add(targetGo);

            InvokeInstance(markerService, "SetTarget", targetGo.transform, "Robin");
            yield return WaitFrames(20);

            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"));
            Assert.AreEqual("15m", GetProp<string>("CurrentDistanceText"));

            UnityEngine.Object.Destroy(targetGo);
            yield return WaitFrames(5);

            Assert.IsNull(GetProp<Transform>("CurrentTarget"));
            Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"));
            Assert.IsFalse(GetProp<bool>("IsScreenIndicatorActive"));
            Assert.IsFalse(GetProp<bool>("IsDistanceTextActive"));
        }

        [UnityTest]
        public IEnumerator CameraRotationAndObstacleOcclusion_UpdateArrowAndDistanceMeterLiveInLateUpdate()
        {
            yield return null;

            var targetGo = new GameObject("OrbitTarget");
            targetGo.transform.position = new Vector3(0f, 1.6f, 20f);
            createdObjects.Add(targetGo);

            GameObject wallGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallGo.transform.position = new Vector3(0f, 1.6f, 10f);
            wallGo.transform.localScale = new Vector3(6f, 6f, 1f);
            wallGo.SetActive(false);
            createdObjects.Add(wallGo);

            InvokeInstance(markerService, "SetTarget", targetGo.transform, "Guardian");
            yield return WaitFrames(20);

            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"));
            Assert.IsFalse(GetProp<bool>("IsScreenArrowActive"));
            Assert.AreEqual("20m", GetProp<string>("CurrentDistanceText"));

            // Turn camera 90 degrees right -> target is off-screen left.
            testCamera.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            yield return WaitFrames(5);

            Assert.IsFalse(GetProp<bool>("LastIsOnScreen"));
            Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"));
            Assert.IsTrue(GetProp<bool>("IsScreenArrowActive"));
            Assert.IsTrue(GetProp<bool>("IsDistanceTextActive"));
            Assert.AreEqual("20m", GetProp<string>("CurrentDistanceText"));

            // Turn camera 180 degrees -> target directly behind camera.
            testCamera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            yield return WaitFrames(5);

            Assert.IsFalse(GetProp<bool>("LastIsOnScreen"));
            Assert.IsTrue(GetProp<bool>("IsScreenArrowActive"));
            Assert.IsTrue(GetProp<bool>("IsDistanceTextActive"));
            Vector2 behindPos = GetProp<Vector2>("CurrentIndicatorCanvasPosition");
            Assert.IsFalse(float.IsNaN(behindPos.x) || float.IsNaN(behindPos.y));

            // Face target again, but enable blocking geometry between camera and target.
            testCamera.transform.rotation = Quaternion.identity;
            wallGo.SetActive(true);
            Physics.SyncTransforms();
            yield return WaitFrames(5);

            Assert.IsTrue(GetProp<bool>("LastIsOnScreen"));
            Assert.IsTrue(GetProp<bool>("LastIsArrowOccluded"));
            Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"));
            Assert.IsTrue(GetProp<bool>("IsScreenArrowActive"));
            Assert.IsTrue(GetProp<bool>("IsDistanceTextActive"));
            Assert.AreEqual("20m", GetProp<string>("CurrentDistanceText"));

            // Remove geometry -> world arrow resumes.
            wallGo.SetActive(false);
            Physics.SyncTransforms();
            yield return WaitFrames(5);

            Assert.IsFalse(GetProp<bool>("LastIsArrowOccluded"));
            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"));
            Assert.IsFalse(GetProp<bool>("IsScreenArrowActive"));
        }

        [UnityTest]
        public IEnumerator ZeroTimeScalePauseAndUnpause_UsesUnscaledDeltaTime()
        {
            yield return null;

            var targetGo = new GameObject("UnscaledTarget");
            targetGo.transform.position = new Vector3(0f, 1.6f, 14f);
            createdObjects.Add(targetGo);

            InvokeInstance(markerService, "SetTarget", targetGo.transform, "Guardian");
            yield return WaitFrames(20);
            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"));

            Time.timeScale = 0f;
            SetStaticProperty(pauseMenuType, "IsPaused", true);
            yield return new WaitForSecondsRealtime(0.25f);
            yield return WaitFrames(5);

            Assert.IsTrue(GetProp<bool>("IsSuppressed"));
            Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"));
            Assert.IsFalse(GetProp<bool>("IsScreenIndicatorActive"));

            // Unpause while Time.timeScale stays 0 to prove UI fade uses unscaledDeltaTime.
            SetStaticProperty(pauseMenuType, "IsPaused", false);
            yield return new WaitForSecondsRealtime(0.25f);
            yield return WaitFrames(10);

            Assert.IsFalse(GetProp<bool>("IsSuppressed"));
            Assert.IsTrue(GetProp<bool>("IsWorldArrowActive"));
            Assert.IsTrue(GetProp<bool>("IsDistanceTextActive"));
        }

        private static IEnumerator WaitFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        private T GetProp<T>(string propName)
        {
            PropertyInfo prop = markerServiceType.GetProperty(propName, InstanceFlags);
            Assert.IsNotNull(prop, $"Property '{propName}' not found on ObjectiveMarkerService.");
            return (T)prop.GetValue(markerService);
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
