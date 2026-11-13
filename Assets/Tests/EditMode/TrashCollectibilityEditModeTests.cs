using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EcoQuest.Tests.EditMode
{
    public class TrashCollectibilityEditModeTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void ForestScene_AllTrashItems_MatchQuestCountRenderAndHaveClearPickupPath()
        {
            string prevScenePath = SceneManager.GetActiveScene().path;
            bool needOpen = prevScenePath != "Assets/EcoQuest/Forest Scene.unity";
            Scene forest = needOpen
                ? EditorSceneManager.OpenScene("Assets/EcoQuest/Forest Scene.unity", OpenSceneMode.Additive)
                : SceneManager.GetActiveScene();

            try
            {
                Physics.SyncTransforms();

                Type questMgrType = Type.GetType("QuestManager, Assembly-CSharp", true);
                int requiredWaste = (int)questMgrType.GetMethod("GetTotalRequiredWaste", Flags).Invoke(null, null);

                Type trashItemType = Type.GetType("TrashItem, Assembly-CSharp", true);
                Type dragType = Type.GetType("DragAndDrop, Assembly-CSharp", true);

                var trashItems = new List<Component>();
                foreach (var obj in UnityEngine.Object.FindObjectsByType(trashItemType, FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (obj is Component c && c.gameObject.scene == forest)
                    {
                        trashItems.Add(c);
                    }
                }

                Assert.AreEqual(requiredWaste, trashItems.Count,
                    $"Forest trash items count ({trashItems.Count}) must match QuestManager.GetTotalRequiredWaste() ({requiredWaste}).");

                Camera mainCam = null;
                foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (cam.gameObject.scene == forest && cam.CompareTag("MainCamera"))
                    {
                        mainCam = cam;
                        break;
                    }
                }
                Assert.IsNotNull(mainCam, "Forest MainCamera must exist.");

                // Verify chest parent has no broken empty MeshCollider blocking pickup or physics
                GameObject chest = FindInScene(forest, "chest");
                if (chest != null)
                {
                    var stray = chest.GetComponent<MeshCollider>();
                    Assert.IsTrue(stray == null || stray.sharedMesh != null, "Chest parent must not carry an empty MeshCollider with sharedMesh == null.");
                }

                int pickupMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "BinSolid");
                bool foundSlicedCake = false;
                var categoryCounts = new Dictionary<string, int>();

                foreach (Component ti in trashItems)
                {
                    GameObject go = ti.gameObject;
                    if (go.name == "Sliced Cake") foundSlicedCake = true;

                    Assert.AreEqual(0, (int)GameObjectUtility.GetStaticEditorFlags(go),
                        $"Trash item '{go.name}' must remain dynamic (no StaticEditorFlags).");
                    Assert.IsTrue((mainCam.cullingMask & (1 << go.layer)) != 0,
                        $"Camera '{mainCam.name}' cullingMask excludes layer {go.layer} of '{go.name}'.");

                    AssertValidRenderersAndMaterials(go);

                    Collider col = go.GetComponent<Collider>();
                    Assert.IsNotNull(col, $"Trash item '{go.name}' needs a Collider.");
                    Assert.IsTrue(col.enabled && !col.isTrigger, $"Trash item '{go.name}' collider must be enabled and non-trigger.");

                    Rigidbody rb = go.GetComponent<Rigidbody>();
                    Assert.IsNotNull(rb, $"Trash item '{go.name}' needs a Rigidbody.");

                    Behaviour dnd = go.GetComponent(dragType) as Behaviour;
                    Assert.IsNotNull(dnd, $"Trash item '{go.name}' needs DragAndDrop.");
                    Assert.IsTrue(dnd.enabled, $"DragAndDrop on '{go.name}' must be enabled.");

                    categoryCounts[go.tag] = categoryCounts.TryGetValue(go.tag, out int cnt) ? cnt + 1 : 1;

                    // Verify item is not enclosed inside another prop collider (temporarily activate container to raycast)
                    Transform rootGroup = go.transform.parent;
                    bool prevGroupActive = rootGroup != null && rootGroup.gameObject.activeSelf;
                    bool prevItemActive = go.activeSelf;
                    try
                    {
                        if (rootGroup != null) rootGroup.gameObject.SetActive(true);
                        go.SetActive(true);
                        Physics.SyncTransforms();

                        Renderer mr = go.GetComponent<Renderer>();
                        Vector3 center = mr != null ? mr.bounds.center : go.transform.position;
                        Vector3[] dirs =
                        {
                            Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
                            new Vector3(1, 1, 0).normalized, new Vector3(-1, 1, 0).normalized,
                            new Vector3(0, 1, 1).normalized, new Vector3(0, 1, -1).normalized
                        };
                        int clearRays = 0;
                        foreach (Vector3 d in dirs)
                        {
                            Vector3 origin = center + d * 1.4f + Vector3.up * 0.2f;
                            if (Physics.Raycast(origin, (center - origin).normalized, out RaycastHit hit, 2.5f, pickupMask, QueryTriggerInteraction.Ignore)
                                && (hit.collider.transform == go.transform || hit.collider.transform.IsChildOf(go.transform)))
                            {
                                clearRays++;
                            }
                        }
                        Assert.GreaterOrEqual(clearRays, 5, $"Trash item '{go.name}' is too occluded/enclosed (only {clearRays}/8 pickup rays hit it directly).");
                    }
                    finally
                    {
                        if (!prevItemActive) go.SetActive(false);
                        if (rootGroup != null && !prevGroupActive) rootGroup.gameObject.SetActive(false);
                        Physics.SyncTransforms();
                    }
                }

                Assert.IsTrue(foundSlicedCake, "Sliced Cake must exist in Forest TrashItemsGroup.");
                Assert.AreEqual(5, categoryCounts.GetValueOrDefault("Biodegradable", 0));
                Assert.AreEqual(3, categoryCounts.GetValueOrDefault("Recyclable", 0));
                Assert.AreEqual(1, categoryCounts.GetValueOrDefault("NonBiodegradable", 0));
            }
            finally
            {
                if (needOpen && forest.IsValid())
                {
                    EditorSceneManager.CloseScene(forest, true);
                }
            }
        }

        [Test]
        public void CityScene_AllTrashPrefabsCatalogAndSpawnPoints_AreRenderableAndUnobstructed()
        {
            string prevScenePath = SceneManager.GetActiveScene().path;
            bool needOpen = prevScenePath != "Assets/EcoQuest/City Scene.unity";
            Scene city = needOpen
                ? EditorSceneManager.OpenScene("Assets/EcoQuest/City Scene.unity", OpenSceneMode.Additive)
                : SceneManager.GetActiveScene();

            try
            {
                Physics.SyncTransforms();

                Type detectiveType = Type.GetType("EcoQuest.CityQuest.CityTrashDetectiveManager, Assembly-CSharp", true);
                Type spawnType = Type.GetType("EcoQuest.CityQuest.CityTrashSpawnPoint, Assembly-CSharp", true);
                Type litterMgrType = Type.GetType("LitterbugQuestManager, Assembly-CSharp", true);

                Component detective = FindComponentInScene(city, detectiveType);
                Assert.IsNotNull(detective, "CityTrashDetectiveManager must exist in City Scene.");

                object dbObj = detectiveType.GetField("database", Flags).GetValue(detective);
                Assert.IsNotNull(dbObj, "CityTrashDetectiveManager.database must be assigned.");
                var dbItems = dbObj.GetType().GetField("items", Flags).GetValue(dbObj) as IList;
                Assert.IsNotNull(dbItems);
                Assert.Greater(dbItems.Count, 0);

                Camera cityCam = null;
                foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (cam.gameObject.scene == city && cam.CompareTag("MainCamera"))
                    {
                        cityCam = cam;
                        break;
                    }
                }
                Assert.IsNotNull(cityCam, "City MainCamera must exist.");

                foreach (object def in dbItems)
                {
                    string id = def.GetType().GetField("id", Flags)?.GetValue(def) as string ?? "unknown";
                    GameObject prefab = def.GetType().GetField("worldPrefab", Flags)?.GetValue(def) as GameObject;
                    Assert.IsNotNull(prefab, $"CityTrashDatabase item '{id}' has null worldPrefab.");
                    Assert.IsTrue((cityCam.cullingMask & (1 << prefab.layer)) != 0,
                        $"City MainCamera cullingMask excludes layer {prefab.layer} of '{id}'.");
                    AssertValidRenderersAndMaterials(prefab);
                }

                Component litterMgr = FindComponentInScene(city, litterMgrType);
                Assert.IsNotNull(litterMgr, "LitterbugQuestManager must exist in City Scene.");
                object catalog = litterMgrType.GetField("litterCatalog", Flags)?.GetValue(litterMgr);
                Assert.IsNotNull(catalog, "LitterbugQuestManager.litterCatalog must be assigned.");
                var entries = catalog.GetType().GetField("entries", Flags)?.GetValue(catalog) as IList;
                Assert.IsNotNull(entries);
                Assert.Greater(entries.Count, 0);
                foreach (object entry in entries)
                {
                    Component propPrefab = entry.GetType().GetField("propPrefab", Flags)?.GetValue(entry) as Component;
                    Assert.IsNotNull(propPrefab, "LitterCatalog entry has null propPrefab.");
                    AssertValidRenderersAndMaterials(propPrefab.gameObject);
                }

                // Every CityTrashSpawnPoint must hit valid ground (not high fence/building collider >= orig.y + 0.5m)
                int spawnCount = 0;
                foreach (var obj in UnityEngine.Object.FindObjectsByType(spawnType, FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (!(obj is Component sp) || sp.gameObject.scene != city) continue;
                    spawnCount++;
                    Vector3 p = sp.transform.position;
                    Assert.IsTrue(Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4.0f, ~0, QueryTriggerInteraction.Ignore),
                        $"City spawn point '{sp.name}' at {p} has no ground beneath it.");
                    Assert.LessOrEqual(hit.point.y, p.y + 0.35f,
                        $"City spawn point '{sp.name}' is blocked/under obstacle '{hit.collider.name}' at y={hit.point.y:F2} (spawn y={p.y:F2}).");
                }
                Assert.GreaterOrEqual(spawnCount, 20, "City Scene should have active CityTrashSpawnPoints.");
            }
            finally
            {
                if (needOpen && city.IsValid())
                {
                    EditorSceneManager.CloseScene(city, true);
                }
            }
        }

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == name) return t.gameObject;
                }
            }
            return null;
        }

        private static Component FindComponentInScene(Scene scene, Type type)
        {
            foreach (var obj in UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (obj is Component c && c.gameObject.scene == scene) return c;
            }
            return null;
        }

        private static void AssertValidRenderersAndMaterials(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.IsNotEmpty(renderers, $"'{root.name}' must have at least one Renderer.");
            foreach (Renderer r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                Assert.IsTrue(r.enabled, $"Renderer '{r.name}' on '{root.name}' must be enabled.");
                Material[] mats = r.sharedMaterials;
                Assert.IsNotEmpty(mats, $"Renderer '{r.name}' on '{root.name}' has no materials.");
                foreach (Material m in mats)
                {
                    Assert.IsNotNull(m, $"Null material on '{root.name}/{r.name}'.");
                    Assert.IsNotNull(m.shader, $"Null shader on material '{m.name}' ({root.name}).");
                    Assert.IsTrue(m.shader.isSupported, $"Unsupported shader '{m.shader.name}' on '{root.name}'.");
                    Assert.IsFalse(m.shader.name.Contains("Error"), $"Error shader on '{root.name}'.");
                }
            }
        }
    }
}
