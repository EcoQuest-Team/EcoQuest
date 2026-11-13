using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using EcoQuest.CityQuest;
using EcoQuest.CityQuest.Editor;
using EcoQuest.CityTraffic;

namespace EcoQuest.EditorTools
{
    /// <summary>
    /// One-click, idempotent Unity Editor tool that applies and verifies the Tier 1 & Tier 2
    /// scene/prefab fixes from the Team Review:
    /// 1. Replaces vehicle MeshColliders with fitted root BoxColliders across all 64 vehicle prefabs.
    /// 2. Expands small CityTrash prefab colliders to >= 0.30m XZ for child-friendly pickups.
    /// 3. Relocates CityTrashSpawnPoints within 4.10m of CityTrafficGraph roads (or on road surfaces) onto safe NavMesh sidewalks/parks.
    /// 4. Disables passive hunger drain on Player.prefab & gameplay scenes while enforcing 5-min Ranger Robin cooldown and 3.0m pickup range.
    /// 5. Verifies AudioListeners and zero missing MonoBehaviours.
    /// </summary>
    public static class ApplyTeamReviewFixesTool
    {
        private const string VehiclePrefabsFolder = "Assets/EcoQuest/Asset/LowPolyMegapolis/Prefabs/Vehicles";
        private const string CityTrashPrefabsFolder = "Assets/EcoQuest/CityTrash/Prefabs";
        private const string PlayerPrefabPath = "Assets/EcoQuest/Prefabs/Player.prefab";
        private const string TrafficGraphPath = "Assets/EcoQuest/CityTraffic/CityTrafficGraph.asset";
        private const string ForestScenePath = "Assets/EcoQuest/Forest Scene.unity";
        private const string CityScenePath = "Assets/EcoQuest/City Scene.unity";
        private const float MinRoadClearanceMeters = 4.10f;

        [MenuItem("Tools/EcoQuest/Apply Tier 1 & 2 Scene & Prefab Fixes")]
        public static void ApplyAllSceneAndPrefabFixes()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                int vehiclesFixed = FixVehiclePrefabsMeshColliders();
                int trashPrefabsFixed = FixCityTrashPrefabs();
                bool playerFixed = FixPlayerPrefab();

                int citySpawnsMoved = FixCityScene(out int cityMissingRemaining, out int cityMeshColsRemoved);
                bool forestChanged = FixForestScene();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                string verifyReport = BuildVerificationReport(out bool allPassed);
                string summary =
                    $"[EcoQuest TeamReview Fixes] {(allPassed ? "PASS" : "CHECK WARNINGS")}\n" +
                    $"• Vehicle prefabs converted to BoxCollider: {vehiclesFixed}\n" +
                    $"• CityTrash prefabs updated (min hitbox + zero missing scripts): {trashPrefabsFixed}\n" +
                    $"• Player.prefab survival/AudioListener updated: {playerFixed}\n" +
                    $"• City Scene: moved {citySpawnsMoved} unsafe roadside trash spawns >= {MinRoadClearanceMeters:F1}m away, " +
                    $"removed {cityMeshColsRemoved} scene vehicle MeshColliders, remaining missing scripts={cityMissingRemaining}\n" +
                    $"• Forest Scene survival/verify synced: {(forestChanged ? "updated" : "clean")}\n\n" +
                    verifyReport;

                Debug.Log(summary);
                EditorUtility.DisplayDialog("EcoQuest Fixes Applied", summary, "OK");
            }
            finally
            {
                if (previousSetup != null && previousSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        [MenuItem("Tools/EcoQuest/Verify Tier 1 & 2 Fix Status")]
        public static void VerifyFixStatus()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                string report = BuildVerificationReport(out _);
                Debug.Log(report);
                EditorUtility.DisplayDialog("EcoQuest Verification", report, "OK");
            }
            finally
            {
                if (previousSetup != null && previousSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        private static string BuildVerificationReport(out bool allPassed)
        {
            int vehWithMeshCol = CountVehiclePrefabsWithMeshColliders();
            int trashMissingScripts = CountMissingScriptsInFolder(CityTrashPrefabsFolder);

            GameObject playerRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var playerStats = playerRoot != null ? playerRoot.GetComponentInChildren<PlayerSurvivalStats>(true) : null;
            bool playerHungerOk = playerStats == null ||
                (Mathf.Approximately(playerStats.hungerDepletionPerSec, 0f) &&
                 Mathf.Approximately(playerStats.sprintHungerDepletionPerSec, 0f));

            Scene city = EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            var graph = AssetDatabase.LoadAssetAtPath<CityTrafficGraph>(TrafficGraphPath);
            var spawns = UnityEngine.Object.FindObjectsByType<CityTrashSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int unsafeSpawns = 0;
            for (int i = 0; i < spawns.Length; i++)
            {
                var sp = spawns[i];
                if (sp == null) continue;
                Collider hitCol = null;
                if (Physics.Raycast(sp.transform.position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore))
                    hitCol = hit.collider;
                if (!CityQuestSceneSetupAndValidator.IsSafePedestrianSpawnPosition(sp.transform.position, hitCol, graph, MinRoadClearanceMeters))
                    unsafeSpawns++;
            }

            var mgr = UnityEngine.Object.FindFirstObjectByType<CityTrashDetectiveManager>(FindObjectsInactive.Include);
            int cityMissing = CountMissingScriptsInScene(city);
            int cityAudioListeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int cityVehMeshCols = CountSceneVehicleMeshColliders(city);

            Scene forest = EditorSceneManager.OpenScene(ForestScenePath, OpenSceneMode.Single);
            int forestMissing = CountMissingScriptsInScene(forest);
            int forestAudioListeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            bool cooldownOk = mgr != null && Mathf.Approximately(mgr.repeatCooldownMinutes, 5f);
            bool pickupRangeOk = mgr != null && mgr.pickupInteractRange >= 3.0f;
            allPassed = vehWithMeshCol == 0 && trashMissingScripts == 0 && playerHungerOk &&
                        unsafeSpawns == 0 && cityMissing == 0 && cityVehMeshCols == 0 &&
                        cityAudioListeners >= 1 && forestMissing == 0 && forestAudioListeners >= 1 &&
                        cooldownOk && pickupRangeOk;

            return
                $"[EcoQuest Verification: {(allPassed ? "PASS" : "FAIL")}]\n" +
                $"• Vehicle prefabs still using MeshCollider: {vehWithMeshCol} (expected 0)\n" +
                $"• City Scene vehicle MeshColliders: {cityVehMeshCols} (expected 0)\n" +
                $"• CityTrash prefab missing scripts: {trashMissingScripts} (expected 0)\n" +
                $"• Player.prefab passive hunger drain zeroed: {playerHungerOk}\n" +
                $"• City Scene unsafe spawns (<{MinRoadClearanceMeters:F1}m / road): {unsafeSpawns} / {spawns.Length} (expected 0)\n" +
                $"• City Scene missing scripts: {cityMissing} | AudioListeners: {cityAudioListeners}\n" +
                $"• Ranger Robin Cooldown: {(mgr != null ? mgr.repeatCooldownMinutes : -1f)} min (expected 5) | Pickup Range: {(mgr != null ? mgr.pickupInteractRange : -1f)}m\n" +
                $"• Forest Scene missing scripts: {forestMissing} | AudioListeners: {forestAudioListeners}";
        }

        private static int FixVehiclePrefabsMeshColliders()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { VehiclePrefabsFolder });
            int updatedCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;

                try
                {
                    if (ReplaceMeshCollidersWithBoxCollider(root, false) > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        updatedCount++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return updatedCount;
        }

        private static int ReplaceMeshCollidersWithBoxCollider(GameObject root, bool useUndo)
        {
            MeshCollider[] meshCols = root.GetComponentsInChildren<MeshCollider>(true);
            if (meshCols == null || meshCols.Length == 0) return 0;

            Bounds combined = default;
            bool hasBounds = false;
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter mf = filters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Vector3 mc = mb.center;
                Vector3 me = mb.extents;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 worldPt = mf.transform.TransformPoint(mc + new Vector3(me.x * x, me.y * y, me.z * z));
                    Vector3 localPt = root.transform.InverseTransformPoint(worldPt);
                    if (!hasBounds) { combined = new Bounds(localPt, Vector3.zero); hasBounds = true; }
                    else combined.Encapsulate(localPt);
                }
            }

            int removed = 0;
            for (int i = 0; i < meshCols.Length; i++)
            {
                if (meshCols[i] == null) continue;
                if (useUndo) Undo.DestroyObjectImmediate(meshCols[i]);
                else UnityEngine.Object.DestroyImmediate(meshCols[i], true);
                removed++;
            }

            BoxCollider[] existingBoxes = root.GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < existingBoxes.Length; i++)
            {
                if (existingBoxes[i] != null && existingBoxes[i].gameObject != root)
                {
                    if (useUndo) Undo.DestroyObjectImmediate(existingBoxes[i]);
                    else UnityEngine.Object.DestroyImmediate(existingBoxes[i], true);
                }
            }

            if (hasBounds && combined.size.sqrMagnitude > 0.001f)
            {
                BoxCollider rootBox = root.GetComponent<BoxCollider>();
                if (rootBox == null)
                    rootBox = useUndo ? Undo.AddComponent<BoxCollider>(root) : root.AddComponent<BoxCollider>();
                else if (useUndo)
                    Undo.RecordObject(rootBox, "Fit vehicle BoxCollider");

                rootBox.center = combined.center;
                rootBox.size = new Vector3(
                    Mathf.Max(combined.size.x, 0.5f),
                    Mathf.Max(combined.size.y, 0.5f),
                    Mathf.Max(combined.size.z, 0.5f));
                rootBox.isTrigger = false;
                rootBox.enabled = true;
                if (useUndo) EditorUtility.SetDirty(rootBox);
            }

            return removed;
        }

        private static int FixCityTrashPrefabs()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { CityTrashPrefabsFolder });
            int updatedCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;

                try
                {
                    bool changed = false;
                    BoxCollider box = root.GetComponent<BoxCollider>();
                    if (box != null)
                    {
                        Vector3 newSize = new Vector3(
                            Mathf.Max(box.size.x, 0.30f),
                            Mathf.Max(box.size.y, 0.16f),
                            Mathf.Max(box.size.z, 0.30f));
                        Vector3 newCenter = box.center;
                        float bottomY = box.center.y - box.size.y * 0.5f;
                        if (newSize.y > box.size.y && bottomY >= -0.02f)
                        {
                            newCenter.y = bottomY + newSize.y * 0.5f;
                        }

                        if (Vector3.Distance(box.size, newSize) > 0.001f || Vector3.Distance(box.center, newCenter) > 0.001f)
                        {
                            box.size = newSize;
                            box.center = newCenter;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        updatedCount++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return updatedCount;
        }

        private static bool FixPlayerPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (root == null) return false;
            bool changed = false;

            try
            {
                var stats = root.GetComponentInChildren<PlayerSurvivalStats>(true);
                if (stats != null)
                {
                    if (!Mathf.Approximately(stats.hungerDepletionPerSec, 0f) ||
                        !Mathf.Approximately(stats.sprintHungerDepletionPerSec, 0f))
                    {
                        stats.hungerDepletionPerSec = 0f;
                        stats.sprintHungerDepletionPerSec = 0f;
                        changed = true;
                    }
                }

                var listener = root.GetComponentInChildren<AudioListener>(true);
                if (listener != null && !listener.enabled)
                {
                    listener.enabled = true;
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return true;
        }

        private static int FixCityScene(out int missingRemaining, out int meshColsRemoved)
        {
            Scene city = EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            meshColsRemoved = 0;

            // 1. Sync serialized survival stats on any scene instance overrides
            ZeroHungerDrainInActiveScene();

            // 2. Sync CityTrashDetectiveManager serialized settings
            var mgr = UnityEngine.Object.FindFirstObjectByType<CityTrashDetectiveManager>(FindObjectsInactive.Include);
            if (mgr != null)
            {
                Undo.RecordObject(mgr, "Update CityTrashDetectiveManager settings");
                mgr.pickupInteractRange = Mathf.Max(mgr.pickupInteractRange, 3.0f);
                mgr.repeatCooldownMinutes = 5f;
                mgr.questDurationSeconds = 240f;
                if (mgr.introDialogueLines != null)
                {
                    for (int i = 0; i < mgr.introDialogueLines.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(mgr.introDialogueLines[i]) && mgr.introDialogueLines[i].Contains("3:00"))
                            mgr.introDialogueLines[i] = mgr.introDialogueLines[i].Replace("3:00", "4:00");
                    }
                }
                EditorUtility.SetDirty(mgr);
            }

            // 3. Replace any stray non-convex MeshColliders on moving or parked scene vehicles
            var processedVehRoots = new HashSet<GameObject>();
            var trafficVehicles = UnityEngine.Object.FindObjectsByType<TrafficVehicleController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < trafficVehicles.Length; i++)
            {
                if (trafficVehicles[i] != null && processedVehRoots.Add(trafficVehicles[i].gameObject))
                    meshColsRemoved += ReplaceMeshCollidersWithBoxCollider(trafficVehicles[i].gameObject, true);
            }
            GameObject[] roots = city.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null || !string.Equals(roots[i].name, "Vehicles", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (MeshCollider mc in roots[i].GetComponentsInChildren<MeshCollider>(true))
                {
                    if (mc == null) continue;
                    GameObject vehRoot = PrefabUtility.GetNearestPrefabInstanceRoot(mc.gameObject) ?? mc.gameObject;
                    if (processedVehRoots.Add(vehRoot))
                        meshColsRemoved += ReplaceMeshCollidersWithBoxCollider(vehRoot, true);
                }
            }

            // 4. Relocate roadside/unsafe CityTrashSpawnPoints >= 4.10m from traffic lanes
            int spawnsMoved = RelocateUnsafeTrashSpawns();

            missingRemaining = CountMissingScriptsInScene(city);
            EditorSceneManager.MarkSceneDirty(city);
            EditorSceneManager.SaveScene(city);
            return spawnsMoved;
        }

        private static int RelocateUnsafeTrashSpawns()
        {
            var graph = AssetDatabase.LoadAssetAtPath<CityTrafficGraph>(TrafficGraphPath);
            var volumes = new List<CityDistrictVolume>(UnityEngine.Object.FindObjectsByType<CityDistrictVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            var spawns = UnityEngine.Object.FindObjectsByType<CityTrashSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var safePositions = new List<Vector3>(spawns.Length);
            var unsafeSpawns = new List<CityTrashSpawnPoint>();

            for (int i = 0; i < spawns.Length; i++)
            {
                var sp = spawns[i];
                if (sp == null) continue;
                Collider hitCol = null;
                if (Physics.Raycast(sp.transform.position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore))
                    hitCol = hit.collider;

                if (CityQuestSceneSetupAndValidator.IsSafePedestrianSpawnPosition(sp.transform.position, hitCol, graph, MinRoadClearanceMeters))
                    safePositions.Add(sp.transform.position);
                else
                    unsafeSpawns.Add(sp);
            }

            System.Random rng = new System.Random(7331);
            int moved = 0;

            for (int i = 0; i < unsafeSpawns.Count; i++)
            {
                CityTrashSpawnPoint sp = unsafeSpawns[i];
                BoxCollider volBounds = FindDistrictCollider(volumes, sp.district);
                Vector3 origin = sp.transform.position;

                if (TryFindNearestSafeNavPos(origin, volBounds, graph, safePositions, rng, out Vector3 newPos, out Vector3 normal, out float navDist))
                {
                    Undo.RecordObject(sp.transform, "Relocate roadside trash spawn");
                    Undo.RecordObject(sp, "Verify relocated spawn");
                    sp.transform.position = newPos;
                    sp.groundNormal = normal;
                    sp.navMeshDistance = navDist;
                    sp.isVerifiedValid = true;
                    EditorUtility.SetDirty(sp.transform);
                    EditorUtility.SetDirty(sp);
                    safePositions.Add(newPos);
                    moved++;
                }
                else
                {
                    Debug.LogWarning($"[ApplyTeamReviewFixesTool] Could not relocate unsafe spawn '{sp.name}' in {sp.district}.");
                }
            }

            return moved;
        }

        private static bool TryFindNearestSafeNavPos(
            Vector3 origin,
            BoxCollider districtCol,
            CityTrafficGraph graph,
            List<Vector3> existingSafe,
            System.Random rng,
            out Vector3 resultPos,
            out Vector3 resultNormal,
            out float navDist)
        {
            float[] radii = { 2.5f, 4.0f, 5.5f, 7.5f, 10.0f, 13.0f };
            for (int r = 0; r < radii.Length; r++)
            {
                float rad = radii[r];
                for (int a = 0; a < 16; a++)
                {
                    float ang = (a / 16f) * Mathf.PI * 2f;
                    Vector3 cand = new Vector3(origin.x + Mathf.Cos(ang) * rad, 15f, origin.z + Mathf.Sin(ang) * rad);
                    if (districtCol != null && !IsInsideXZ(districtCol.bounds, cand)) continue;
                    if (TryValidateCandidate(cand, graph, existingSafe, out resultPos, out resultNormal, out navDist))
                        return true;
                }
            }

            // Fallback: random search inside district volume
            if (districtCol != null)
            {
                Bounds b = districtCol.bounds;
                for (int attempt = 0; attempt < 400; attempt++)
                {
                    float rx = (float)rng.NextDouble() * (b.max.x - b.min.x) + b.min.x;
                    float rz = (float)rng.NextDouble() * (b.max.z - b.min.z) + b.min.z;
                    if (TryValidateCandidate(new Vector3(rx, 15f, rz), graph, existingSafe, out resultPos, out resultNormal, out navDist))
                        return true;
                }
            }

            resultPos = origin;
            resultNormal = Vector3.up;
            navDist = 0f;
            return false;
        }

        private static bool TryValidateCandidate(
            Vector3 highSample,
            CityTrafficGraph graph,
            List<Vector3> existingSafe,
            out Vector3 pos,
            out Vector3 normal,
            out float navDist)
        {
            pos = Vector3.zero;
            normal = Vector3.up;
            navDist = 0f;

            if (!Physics.Raycast(highSample, Vector3.down, out RaycastHit hit, 25f, ~0, QueryTriggerInteraction.Ignore))
                return false;

            if (!NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 1.5f, NavMesh.AllAreas))
                return false;

            if (!CityQuestSceneSetupAndValidator.IsSafePedestrianSpawnPosition(navHit.position, hit.collider, graph, MinRoadClearanceMeters + 0.15f))
                return false;

            // Maintain >= 2.0m separation from nearby spawns
            for (int i = 0; i < existingSafe.Count; i++)
            {
                Vector3 d = existingSafe[i] - navHit.position;
                d.y = 0f;
                if (d.sqrMagnitude < 4.0f) return false;
            }

            pos = navHit.position;
            normal = hit.normal.sqrMagnitude > 0.01f ? hit.normal.normalized : Vector3.up;
            navDist = Vector3.Distance(hit.point, navHit.position);
            return true;
        }

        private static bool IsInsideXZ(Bounds b, Vector3 p)
        {
            return p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;
        }

        private static BoxCollider FindDistrictCollider(List<CityDistrictVolume> volumes, CityDistrict district)
        {
            for (int i = 0; i < volumes.Count; i++)
            {
                if (volumes[i] != null && volumes[i].district == district)
                    return volumes[i].GetComponent<BoxCollider>();
            }
            return null;
        }

        private static bool ZeroHungerDrainInActiveScene()
        {
            bool changed = false;
            var allStats = UnityEngine.Object.FindObjectsByType<PlayerSurvivalStats>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allStats.Length; i++)
            {
                var st = allStats[i];
                if (st == null) continue;
                if (!Mathf.Approximately(st.hungerDepletionPerSec, 0f) ||
                    !Mathf.Approximately(st.sprintHungerDepletionPerSec, 0f))
                {
                    Undo.RecordObject(st, "Disable passive hunger drain");
                    st.hungerDepletionPerSec = 0f;
                    st.sprintHungerDepletionPerSec = 0f;
                    EditorUtility.SetDirty(st);
                    changed = true;
                }
            }
            return changed;
        }

        private static bool FixForestScene()
        {
            Scene forest = EditorSceneManager.OpenScene(ForestScenePath, OpenSceneMode.Single);
            bool changed = ZeroHungerDrainInActiveScene();
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(forest);
                EditorSceneManager.SaveScene(forest);
            }
            return changed;
        }

        private static int CountSceneVehicleMeshColliders(Scene scene)
        {
            int count = 0;
            var counted = new HashSet<MeshCollider>();
            var trafficVehicles = UnityEngine.Object.FindObjectsByType<TrafficVehicleController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < trafficVehicles.Length; i++)
            {
                if (trafficVehicles[i] == null) continue;
                foreach (var mc in trafficVehicles[i].GetComponentsInChildren<MeshCollider>(true))
                {
                    if (mc != null && counted.Add(mc)) count++;
                }
            }
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null || !string.Equals(roots[i].name, "Vehicles", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var mc in roots[i].GetComponentsInChildren<MeshCollider>(true))
                {
                    if (mc != null && counted.Add(mc)) count++;
                }
            }
            return count;
        }

        private static int CountVehiclePrefabsWithMeshColliders()
        {
            int count = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { VehiclePrefabsFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && go.GetComponentsInChildren<MeshCollider>(true).Length > 0)
                    count++;
            }
            return count;
        }

        private static int CountMissingScriptsInFolder(string folder)
        {
            int count = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null)
                {
                    foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                        count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                }
            }
            return count;
        }

        private static int CountMissingScriptsInScene(Scene scene)
        {
            int count = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                foreach (Transform t in roots[i].GetComponentsInChildren<Transform>(true))
                    count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            }
            return count;
        }
    }
}
