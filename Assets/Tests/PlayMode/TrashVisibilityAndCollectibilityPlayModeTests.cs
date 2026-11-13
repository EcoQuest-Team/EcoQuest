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
    public class TrashVisibilityAndCollectibilityPlayModeTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private bool hadTabTip;
        private int savedTabTip;
        private bool hadTrashDone;
        private int savedTrashDone;
        private bool hadQuestAccepted;
        private int savedQuestAccepted;

        [SetUp]
        public void SetUp()
        {
            hadTabTip = PlayerPrefs.HasKey("EcoQuest_TabTipDone");
            savedTabTip = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0);
            hadTrashDone = PlayerPrefs.HasKey("EcoQuest_ForestTrashQuestDone");
            savedTrashDone = PlayerPrefs.GetInt("EcoQuest_ForestTrashQuestDone", 0);
            hadQuestAccepted = PlayerPrefs.HasKey("EcoQuest_ForestQuestAccepted");
            savedQuestAccepted = PlayerPrefs.GetInt("EcoQuest_ForestQuestAccepted", 0);
        }

        [TearDown]
        public void TearDown()
        {
            RestoreKey("EcoQuest_TabTipDone", hadTabTip, savedTabTip);
            RestoreKey("EcoQuest_ForestTrashQuestDone", hadTrashDone, savedTrashDone);
            RestoreKey("EcoQuest_ForestQuestAccepted", hadQuestAccepted, savedQuestAccepted);
            PlayerPrefs.Save();
        }

        [UnityTest]
        public IEnumerator ForestScene_AllTrashItems_ActivateRenderAndAreCollectibleMatchingQuestCount()
        {
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);
            PlayerPrefs.SetInt("EcoQuest_ForestTrashQuestDone", 0);
            PlayerPrefs.SetInt("EcoQuest_ForestQuestAccepted", 1);
            PlayerPrefs.Save();

            var load = SceneManager.LoadSceneAsync("Forest Scene");
            while (load != null && !load.isDone) yield return null;

            Type trashType = Type.GetType("TrashItem, Assembly-CSharp", true);
            Type dragType = Type.GetType("DragAndDrop, Assembly-CSharp", true);
            Type questType = Type.GetType("QuestManager, Assembly-CSharp", true);

            // Wait up to 1s unscaled for intro/visibility setup
            float deadline = Time.realtimeSinceStartup + 1.0f;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool allowed = (bool)trashType.GetProperty("AreTrashItemsAllowed", Flags).GetValue(null);
                if (allowed) break;
                yield return null;
            }
            yield return null;
            Physics.SyncTransforms();

            Assert.IsTrue((bool)trashType.GetProperty("AreTrashItemsAllowed", Flags).GetValue(null),
                "TrashItem.AreTrashItemsAllowed should become true in Forest Scene when quest/tutorial is active.");

            int expectedTotal = (int)questType.GetMethod("GetTotalRequiredWaste", Flags).Invoke(null, null);
            var items = new List<Component>();
            foreach (var obj in UnityEngine.Object.FindObjectsByType(trashType, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (obj is Component c) items.Add(c);
            }
            Assert.AreEqual(expectedTotal, items.Count,
                $"Reachable Forest trash items ({items.Count}) must equal QuestManager required waste ({expectedTotal}).");

            Camera mainCam = Camera.main;
            Assert.IsNotNull(mainCam, "Camera.main must resolve in Forest Scene.");

            int pickupMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "BinSolid");
            bool foundSlicedCake = false;

            foreach (Component ti in items)
            {
                GameObject go = ti.gameObject;
                if (go.name == "Sliced Cake") foundSlicedCake = true;

                Assert.IsTrue(go.activeInHierarchy, $"Trash item '{go.name}' should be active in hierarchy.");
                Assert.IsTrue((mainCam.cullingMask & (1 << go.layer)) != 0,
                    $"Camera cullingMask must include layer {go.layer} of '{go.name}'.");

                AssertValidRenderers(go);

                Collider col = go.GetComponent<Collider>();
                Assert.IsNotNull(col, $"Collider missing on '{go.name}'.");
                Assert.IsTrue(col.enabled && !col.isTrigger, $"Collider on '{go.name}' must be enabled & non-trigger.");

                Behaviour dnd = go.GetComponent(dragType) as Behaviour;
                Assert.IsNotNull(dnd);
                Assert.IsTrue(dnd.enabled, $"DragAndDrop on '{go.name}' must be enabled.");

                Renderer mr = go.GetComponent<Renderer>();
                Vector3 center = mr != null ? mr.bounds.center : go.transform.position;
                Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up };
                int directHits = 0;
                foreach (Vector3 d in dirs)
                {
                    Vector3 origin = center + d * 1.3f + Vector3.up * 0.15f;
                    if (Physics.Raycast(origin, (center - origin).normalized, out RaycastHit hit, 2.5f, pickupMask, QueryTriggerInteraction.Ignore)
                        && (hit.collider.transform == go.transform || hit.collider.transform.IsChildOf(go.transform)))
                    {
                        directHits++;
                    }
                }
                Assert.GreaterOrEqual(directHits, 3, $"Trash item '{go.name}' cannot be reliably targeted by pickup raycasts ({directHits}/5).");
            }

            Assert.IsTrue(foundSlicedCake, "Sliced Cake must be active and verified in Forest Scene.");
        }

        [UnityTest]
        public IEnumerator CityScene_AllDatabasePrefabsPoolAndSpawnPoints_RenderAndAreReachable()
        {
            var load = SceneManager.LoadSceneAsync("City Scene");
            while (load != null && !load.isDone) yield return null;
            for (int i = 0; i < 5; i++) yield return null;
            Physics.SyncTransforms();

            Camera mainCam = Camera.main;
            Assert.IsNotNull(mainCam, "Camera.main must resolve in City Scene.");

            Type detectiveType = Type.GetType("EcoQuest.CityQuest.CityTrashDetectiveManager, Assembly-CSharp", true);
            Type spawnType = Type.GetType("EcoQuest.CityQuest.CityTrashSpawnPoint, Assembly-CSharp", true);
            Type poolType = Type.GetType("EcoQuest.CityQuest.CityTrashPickupInstance, Assembly-CSharp", true);

            object detective = detectiveType.GetProperty("Instance", Flags)?.GetValue(null);
            Assert.IsNotNull(detective, "CityTrashDetectiveManager.Instance should exist in City Scene.");

            object dbObj = detectiveType.GetField("database", Flags).GetValue(detective);
            Assert.IsNotNull(dbObj);
            var dbItems = dbObj.GetType().GetField("items", Flags).GetValue(dbObj) as IList;
            Assert.IsNotNull(dbItems);
            Assert.Greater(dbItems.Count, 0);

            foreach (object def in dbItems)
            {
                string id = def.GetType().GetField("id", Flags)?.GetValue(def) as string ?? "unknown";
                GameObject prefab = def.GetType().GetField("worldPrefab", Flags)?.GetValue(def) as GameObject;
                Assert.IsNotNull(prefab, $"Null worldPrefab on '{id}'.");
                Assert.IsTrue((mainCam.cullingMask & (1 << prefab.layer)) != 0,
                    $"Camera cullingMask excludes City prefab '{id}'.");
                AssertValidRenderers(prefab);
            }

            var poolItems = UnityEngine.Object.FindObjectsByType(poolType, FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.IsNotEmpty(poolItems, "City pooled pickup instances should exist.");
            foreach (var obj in poolItems)
            {
                if (obj is Component c)
                {
                    Assert.IsTrue((mainCam.cullingMask & (1 << c.gameObject.layer)) != 0,
                        $"Pooled City pickup '{c.name}' layer excluded from MainCamera.");
                }
            }

            var spawnPoints = UnityEngine.Object.FindObjectsByType(spawnType, FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.IsNotEmpty(spawnPoints, "CityTrashSpawnPoints should exist in City Scene.");
            foreach (var obj in spawnPoints)
            {
                if (!(obj is Component sp)) continue;
                Vector3 p = sp.transform.position;
                Assert.IsTrue(Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4.0f, ~0, QueryTriggerInteraction.Ignore),
                    $"City spawn '{sp.name}' missing ground surface.");
                Assert.LessOrEqual(hit.point.y, p.y + 0.35f,
                    $"City spawn '{sp.name}' obstructed by '{hit.collider.name}' at y={hit.point.y:F2}.");
            }
        }

        private static void AssertValidRenderers(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.IsNotEmpty(renderers, $"No Renderer found on '{root.name}'.");
            foreach (Renderer r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                Assert.IsTrue(r.enabled, $"Renderer '{r.name}' disabled on '{root.name}'.");
                Material[] mats = r.sharedMaterials;
                Assert.IsNotEmpty(mats, $"No materials on '{root.name}/{r.name}'.");
                foreach (Material m in mats)
                {
                    Assert.IsNotNull(m, $"Null material on '{root.name}/{r.name}'.");
                    Assert.IsNotNull(m.shader, $"Null shader on material '{m.name}' ({root.name}).");
                    Assert.IsTrue(m.shader.isSupported, $"Unsupported shader '{m.shader.name}' on '{root.name}'.");
                }
            }
        }

        private static void RestoreKey(string key, bool existed, int value)
        {
            if (existed) PlayerPrefs.SetInt(key, value);
            else PlayerPrefs.DeleteKey(key);
        }
    }
}
