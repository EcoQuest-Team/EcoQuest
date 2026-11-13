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
        private Scene? tempCityScene;

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

        [UnityTest]
        public IEnumerator CityScene_OptionalFountainGuardianHasNoArrowWhenAvailable_OnlyWhenReadyToTurnIn()
        {
            yield return null;

            Scene originalScene = SceneManager.GetActiveScene();
            Scene cityScene = SceneManager.GetSceneByName("City Scene");
            bool createdCityScene = false;
            if (!cityScene.IsValid() || !cityScene.isLoaded)
            {
                cityScene = SceneManager.CreateScene("City Scene");
                createdCityScene = true;
            }

            try
            {
                if (createdCityScene)
            {
                tempCityScene = cityScene;
            }
            SceneManager.SetActiveScene(cityScene);

                Type litterbugType = Type.GetType("LitterbugQuestManager, Assembly-CSharp", true);
                Type litterbugStateType = Type.GetType("LitterbugQuestState, Assembly-CSharp", true);

                var fountainGo = new GameObject("FountainQuestGiver");
                fountainGo.transform.position = new Vector3(0f, 1.6f, 21f);
                SceneManager.MoveGameObjectToScene(fountainGo, cityScene);
                createdObjects.Add(fountainGo);

                var lqmGo = new GameObject("LitterbugQuestManager");
                Component lqm = lqmGo.AddComponent(litterbugType);
                litterbugType.GetField("questGiverTransform", InstanceFlags).SetValue(lqm, fountainGo.transform);
                SceneManager.MoveGameObjectToScene(lqmGo, cityScene);
                createdObjects.Add(lqmGo);

                yield return null;

                InvokeInstance(markerService, "CacheSceneReferences");
                InvokeInstance(markerService, "RefreshTargetFromState");
                yield return WaitFrames(3);

                bool readyForArrow = (bool)litterbugType.GetProperty("IsQuestGiverReadyForArrow", InstanceFlags).GetValue(lqm);
                Assert.IsFalse(readyForArrow, "Optional Fountain Guardian quest should not expose an offer arrow when Available.");
                Assert.AreNotSame(fountainGo.transform, GetProp<Transform>("CurrentTarget"),
                    "ObjectiveMarkerService must not target optional Fountain Guardian when quest is Available.");
                Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"));

                object readyState = Enum.Parse(litterbugStateType, "ReadyToTurnIn");
                litterbugType.GetField("questState", InstanceFlags).SetValue(lqm, readyState);
                InvokeInstance(markerService, "RefreshTargetFromState");
                yield return WaitFrames(3);

                Assert.AreSame(fountainGo.transform, GetProp<Transform>("CurrentTarget"),
                    "Once player completes an accepted Fountain quest round (ReadyToTurnIn), arrow may guide turn-in.");
            }
            finally
            {
                if (originalScene.IsValid() && originalScene.isLoaded)
                {
                    SceneManager.SetActiveScene(originalScene);
                }
            }
        }

        [UnityTest]
        public IEnumerator CityScene_OptionalRangerRobinHasNoArrowOrDistanceWhenNotStartedOrCompleted_OnlyWhenReadyToTurnIn()
        {
            yield return null;

            Scene originalScene = SceneManager.GetActiveScene();
            Scene cityScene = SceneManager.GetSceneByName("City Scene");
            bool createdCityScene = false;
            if (!cityScene.IsValid() || !cityScene.isLoaded)
            {
                cityScene = SceneManager.CreateScene("City Scene");
                createdCityScene = true;
            }

            try
            {
                if (createdCityScene)
                {
                    tempCityScene = cityScene;
                }
                SceneManager.SetActiveScene(cityScene);

                Type detectiveType = Type.GetType("EcoQuest.CityQuest.CityTrashDetectiveManager, Assembly-CSharp", true);
                Type detectiveStateType = Type.GetType("EcoQuest.CityQuest.CityDetectiveState, Assembly-CSharp", true);

                var robinGo = new GameObject("RangerRobin");
                robinGo.transform.position = new Vector3(0f, 1.6f, 21f);
                SceneManager.MoveGameObjectToScene(robinGo, cityScene);
                createdObjects.Add(robinGo);

                var ctdGo = new GameObject("CityTrashDetectiveManager");
                Component ctd = ctdGo.AddComponent(detectiveType);
                detectiveType.GetField("rangerRobinTransform", InstanceFlags).SetValue(ctd, robinGo.transform);
                SceneManager.MoveGameObjectToScene(ctdGo, cityScene);
                createdObjects.Add(ctdGo);

                yield return null;

                InvokeInstance(markerService, "CacheSceneReferences");
                InvokeInstance(markerService, "RefreshTargetFromState");
                yield return WaitFrames(3);

                bool readyForArrow = (bool)detectiveType.GetProperty("IsRobinReadyForArrow", InstanceFlags).GetValue(ctd);
                Assert.IsFalse(readyForArrow, "Optional Ranger Robin quest should not expose an offer arrow when NotStarted.");
                Assert.IsNull(GetProp<Transform>("CurrentTarget"),
                    "ObjectiveMarkerService must not target optional Ranger Robin when quest is NotStarted.");
                Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"));
                Assert.IsFalse(GetProp<bool>("IsScreenIndicatorActive"));
                Assert.IsFalse(GetProp<bool>("IsDistanceTextActive"));

                object completedState = Enum.Parse(detectiveStateType, "Completed");
                detectiveType.GetField("questState", InstanceFlags).SetValue(ctd, completedState);
                InvokeInstance(markerService, "RefreshTargetFromState");
                yield return WaitFrames(3);

                Assert.IsFalse((bool)detectiveType.GetProperty("IsRobinReadyForArrow", InstanceFlags).GetValue(ctd));
                Assert.IsNull(GetProp<Transform>("CurrentTarget"),
                    "ObjectiveMarkerService must not target optional Ranger Robin when quest is Completed.");
                Assert.IsFalse(GetProp<bool>("IsWorldArrowActive"));
                Assert.IsFalse(GetProp<bool>("IsDistanceTextActive"));

                object readyState = Enum.Parse(detectiveStateType, "ReadyToTurnIn");
                detectiveType.GetField("questState", InstanceFlags).SetValue(ctd, readyState);
                InvokeInstance(markerService, "RefreshTargetFromState");
                yield return WaitFrames(3);

                Assert.AreSame(robinGo.transform, GetProp<Transform>("CurrentTarget"),
                    "Once player finishes an accepted Ranger Robin investigation (ReadyToTurnIn), arrow may guide turn-in.");
            }
            finally
            {
                if (originalScene.IsValid() && originalScene.isLoaded)
                {
                    SceneManager.SetActiveScene(originalScene);
                }
            }
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            if (tempCityScene.HasValue)
            {
                Scene s = tempCityScene.Value;
                tempCityScene = null;
                if (s.IsValid() && s.isLoaded)
                {
                    AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(s);
                    while (unloadOp != null && !unloadOp.isDone)
                    {
                        yield return null;
                    }
                }
            }
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
