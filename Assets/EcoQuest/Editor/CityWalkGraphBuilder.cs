#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CityWalkGraphBuilder
{
    private const float GridStep = 1.0f;
    private const float MinX = -168f;
    private const float MaxX = 85f; // Exclude highway beyond X = 85
    private const float MinZ = -84f;
    private const float MaxZ = 96f;

    // Ground Classification Types
    public const int CellExcluded = 0;
    public const int CellSidewalk = 1;  // Green
    public const int CellCrosswalk = 2; // Yellow
    public const int CellRoad = 3;      // Red

    [MenuItem("NPC/Build Walk Graph")]
    public static void BuildWalkGraph()
    {
        Debug.Log("=== Starting NPC/Build Walk Graph ===");

        // 1. Delete old area volumes and waypoints
        string[] toDelete = { "RoadNavMeshModifiers", "IsolatedIslandBlocks", "NpcPaths" };
        foreach (var name in toDelete)
        {
            var go = GameObject.Find(name);
            if (go != null)
            {
                Object.DestroyImmediate(go);
                Debug.Log($"Removed old object '{name}'");
            }
        }

        int mask = ~LayerMask.GetMask("Ignore Raycast", "UI");

        // 2. Sample ground on 1.0m grid
        Dictionary<Vector2Int, (Vector3 pos, int type)> cellMap = new Dictionary<Vector2Int, (Vector3, int)>();
        List<CityWalkGraphVisualizer.SampledCell> sampledList = new List<CityWalkGraphVisualizer.SampledCell>();

        int countSidewalk = 0;
        int countCrosswalk = 0;
        int countRoad = 0;
        int countExcluded = 0;

        for (float x = MinX; x <= MaxX; x += GridStep)
        {
            for (float z = MinZ; z <= MaxZ; z += GridStep)
            {
                Vector3 origin = new Vector3(x, 10f, z);
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f, mask, QueryTriggerInteraction.Ignore))
                {
                    int type = ClassifyHit(hit);
                    Vector2Int coord = new Vector2Int(Mathf.RoundToInt(x / GridStep), Mathf.RoundToInt(z / GridStep));
                    cellMap[coord] = (hit.point, type);

                    if (type != CellExcluded)
                    {
                        sampledList.Add(new CityWalkGraphVisualizer.SampledCell { position = hit.point, cellType = type });
                    }

                    if (type == CellSidewalk) countSidewalk++;
                    else if (type == CellCrosswalk) countCrosswalk++;
                    else if (type == CellRoad) countRoad++;
                    else countExcluded++;
                }
            }
        }

        Debug.Log($"Ground Sampled: Sidewalk={countSidewalk}, Crosswalk={countCrosswalk}, Road={countRoad}, Excluded={countExcluded}");

        // 3. Erode sidewalk cells away from road curbs (~0.5m) and building walls / props
        HashSet<Vector2Int> validNodes = new HashSet<Vector2Int>();
        Vector2Int[] neighbors4 = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        foreach (var kvp in cellMap)
        {
            Vector2Int coord = kvp.Key;
            var (pos, type) = kvp.Value;

            if (type == CellCrosswalk)
            {
                // Verify crosswalk node does not clip into vehicles or props
                var cols = Physics.OverlapCapsule(pos + Vector3.up * 0.3f, pos + Vector3.up * 1.5f, 0.35f, mask, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                foreach (var c in cols)
                {
                    string rootName = c.transform.root.name;
                    if (rootName == "Buildings" || rootName == "Props and Nature")
                    {
                        blocked = true;
                        break;
                    }
                }
                if (!blocked) validNodes.Add(coord);
            }
            else if (type == CellSidewalk)
            {
                // Check physical clearance from building walls and props (radius 0.45m)
                var cols = Physics.OverlapCapsule(pos + Vector3.up * 0.3f, pos + Vector3.up * 1.5f, 0.45f, mask, QueryTriggerInteraction.Ignore);
                bool blockedByObstacle = false;
                foreach (var c in cols)
                {
                    string rootName = c.transform.root.name;
                    if (rootName == "Buildings" || rootName == "Props and Nature" || c.name.StartsWith("House_") || c.name.StartsWith("SuburbHouse_"))
                    {
                        blockedByObstacle = true;
                        break;
                    }
                }
                if (blockedByObstacle) continue;

                // Check 4 directional offsets (0.5m) to ensure kept away from road curb
                bool nearRoadOrWall = false;
                Vector3[] offsets = { new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 0, 0), new Vector3(0, 0, 0.5f), new Vector3(0, 0, -0.5f) };
                foreach (var off in offsets)
                {
                    Vector3 testPt = pos + off;
                    if (Physics.Raycast(testPt + Vector3.up * 5f, Vector3.down, out RaycastHit offHit, 10f, mask, QueryTriggerInteraction.Ignore))
                    {
                        int offType = ClassifyHit(offHit);
                        if (offType == CellRoad || offType == CellExcluded)
                        {
                            nearRoadOrWall = true;
                            break;
                        }
                    }
                }

                if (!nearRoadOrWall)
                {
                    validNodes.Add(coord);
                }
            }
        }

        Debug.Log($"Nodes after erosion: {validNodes.Count} (from {countSidewalk + countCrosswalk} candidates)");

        // 4. Create Graph Nodes
        var graph = ScriptableObject.CreateInstance<CityWalkGraph>();
        graph.name = "CityWalkGraph";
        Dictionary<Vector2Int, int> coordToNodeIdx = new Dictionary<Vector2Int, int>();

        foreach (var coord in validNodes)
        {
            var cell = cellMap[coord];
            var node = new CityWalkGraph.Node
            {
                position = cell.pos,
                isCrosswalk = (cell.type == CellCrosswalk)
            };
            coordToNodeIdx[coord] = graph.nodes.Count;
            graph.nodes.Add(node);
        }

        // 5. Connect 8-way Neighbors with STRICT validation that path stays on sidewalk/crosswalk and hits no obstacle
        Vector2Int[] neighbors8 = {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
        };

        for (int i = 0; i < graph.nodes.Count; i++)
        {
            var node = graph.nodes[i];
            Vector2Int coord = new Vector2Int(Mathf.RoundToInt(node.position.x / GridStep), Mathf.RoundToInt(node.position.z / GridStep));

            foreach (var offset in neighbors8)
            {
                Vector2Int nCoord = coord + offset;
                if (coordToNodeIdx.TryGetValue(nCoord, out int nIdx) && nIdx > i)
                {
                    var nNode = graph.nodes[nIdx];
                    float dist = Vector3.Distance(node.position, nNode.position);

                    // Capsule check between nodes at pedestrian height (radius 0.3m)
                    Vector3 p1 = node.position + Vector3.up * 0.5f;
                    Vector3 p2 = nNode.position + Vector3.up * 0.5f;
                    if (Physics.CapsuleCast(p1, p1 + Vector3.up * 0.8f, 0.3f, (p2 - p1).normalized, dist, mask, QueryTriggerInteraction.Ignore))
                        continue;

                    // Strictly verify ground along the edge
                    bool edgeValid = true;
                    int numChecks = Mathf.Max(3, Mathf.CeilToInt(dist / 0.3f));
                    for (int s = 1; s < numChecks; s++)
                    {
                        float t = (float)s / numChecks;
                        Vector3 testPt = Vector3.Lerp(node.position, nNode.position, t);

                        // Check obstacle clearance along edge
                        var obs = Physics.OverlapSphere(testPt + Vector3.up * 0.9f, 0.35f, mask, QueryTriggerInteraction.Ignore);
                        foreach (var o in obs)
                        {
                            string rName = o.transform.root.name;
                            if (rName == "Buildings" || rName == "Props and Nature")
                            {
                                edgeValid = false;
                                break;
                            }
                        }
                        if (!edgeValid) break;

                        // Check ground beneath edge
                        if (Physics.Raycast(testPt + Vector3.up * 2f, Vector3.down, out RaycastHit edgeHit, 4f, mask, QueryTriggerInteraction.Ignore))
                        {
                            int edgeType = ClassifyHit(edgeHit);
                            if (edgeType == CellRoad || edgeType == CellExcluded)
                            {
                                edgeValid = false;
                                break;
                            }
                        }
                        else
                        {
                            edgeValid = false;
                            break;
                        }
                    }

                    if (edgeValid)
                    {
                        node.neighbors.Add(nIdx);
                        node.costs.Add(dist);
                        nNode.neighbors.Add(i);
                        nNode.costs.Add(dist);
                    }
                }
            }
        }

        // Bridge Crosswalks to adjacent Sidewalk nodes within 1.6m with edge validation
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            var node = graph.nodes[i];
            if (!node.isCrosswalk) continue;

            for (int j = 0; j < graph.nodes.Count; j++)
            {
                if (i == j) continue;
                var other = graph.nodes[j];
                if (other.isCrosswalk) continue; // Must connect to sidewalk

                float d = Vector3.Distance(node.position, other.position);
                if (d <= 1.6f && !node.neighbors.Contains(j))
                {
                    Vector3 startRay = node.position + Vector3.up * 0.5f;
                    Vector3 endRay = other.position + Vector3.up * 0.5f;
                    if (Physics.Linecast(startRay, endRay, mask, QueryTriggerInteraction.Ignore))
                        continue;

                    bool bridgeValid = true;
                    int bSteps = 4;
                    for (int s = 1; s < bSteps; s++)
                    {
                        Vector3 testPt = Vector3.Lerp(node.position, other.position, (float)s / bSteps);
                        if (Physics.Raycast(testPt + Vector3.up * 2f, Vector3.down, out RaycastHit bHit, 4f, mask, QueryTriggerInteraction.Ignore))
                        {
                            int bType = ClassifyHit(bHit);
                            if (bType == CellRoad || bType == CellExcluded)
                            {
                                bridgeValid = false;
                                break;
                            }
                        }
                        else
                        {
                            bridgeValid = false;
                            break;
                        }
                    }

                    if (bridgeValid)
                    {
                        node.neighbors.Add(j);
                        node.costs.Add(d);
                        other.neighbors.Add(i);
                        other.costs.Add(d);
                    }
                }
            }
        }

        // 6. Prune isolated components: keep largest connected group
        var largestGroup = FindLargestConnectedGroup(graph);
        Debug.Log($"Connected component pruning: keeping {largestGroup.Count} of {graph.nodes.Count} nodes");

        var newGraph = ScriptableObject.CreateInstance<CityWalkGraph>();
        newGraph.name = "CityWalkGraph";
        Dictionary<int, int> oldToNewIdx = new Dictionary<int, int>();

        foreach (int oldIdx in largestGroup)
        {
            oldToNewIdx[oldIdx] = newGraph.nodes.Count;
            newGraph.nodes.Add(new CityWalkGraph.Node
            {
                position = graph.nodes[oldIdx].position,
                isCrosswalk = graph.nodes[oldIdx].isCrosswalk
            });
        }

        // Remap neighbors
        int edgeCount = 0;
        foreach (int oldIdx in largestGroup)
        {
            int newIdx = oldToNewIdx[oldIdx];
            var oldNode = graph.nodes[oldIdx];
            var newNode = newGraph.nodes[newIdx];

            for (int k = 0; k < oldNode.neighbors.Count; k++)
            {
                int oldNeighbor = oldNode.neighbors[k];
                if (oldToNewIdx.TryGetValue(oldNeighbor, out int newNeighbor))
                {
                    newNode.neighbors.Add(newNeighbor);
                    newNode.costs.Add(oldNode.costs[k]);
                    edgeCount++;
                }
            }
        }

        newGraph.RebuildCategoryLists();
        Debug.Log($"Final Walk Graph: {newGraph.nodes.Count} nodes ({newGraph.sidewalkNodeIndices.Count} sidewalk, {newGraph.crosswalkNodeIndices.Count} crosswalk), {edgeCount / 2} bidirectional edges.");

        // 7. Save Asset
        string assetPath = "Assets/EcoQuest/City Scene/CityWalkGraph.asset";
        var existing = AssetDatabase.LoadAssetAtPath<CityWalkGraph>(assetPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(newGraph, existing);
            EditorUtility.SetDirty(existing);
        }
        else
        {
            AssetDatabase.CreateAsset(newGraph, assetPath);
            existing = newGraph;
        }
        AssetDatabase.SaveAssets();

        // 8. Update Visualizer in scene
        var viz = Object.FindAnyObjectByType<CityWalkGraphVisualizer>();
        if (viz == null)
        {
            var go = new GameObject("WalkGraphVisualizer");
            viz = go.AddComponent<CityWalkGraphVisualizer>();
        }
        viz.graph = existing;
        viz.sampledCells = sampledList;
        EditorUtility.SetDirty(viz);

        // 9. Assign graph to NpcSpawner and active NpcBrains
        var spawner = Object.FindAnyObjectByType<NpcSpawner>();
        if (spawner != null)
        {
            var so = new SerializedObject(spawner);
            var prop = so.FindProperty("walkGraph");
            if (prop != null)
            {
                prop.objectReferenceValue = existing;
                so.ApplyModifiedProperties();
            }
            EditorUtility.SetDirty(spawner);
        }

        var npcs = Object.FindObjectsByType<NpcBrain>(FindObjectsSortMode.None);
        foreach (var npc in npcs)
        {
            var so = new SerializedObject(npc);
            var prop = so.FindProperty("walkGraph");
            if (prop != null)
            {
                prop.objectReferenceValue = existing;
                so.ApplyModifiedProperties();
            }
            EditorUtility.SetDirty(npc);
        }

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("<color=green>SUCCESS: Walk Graph built and saved to " + assetPath + "!</color>");
    }

    public static int ClassifyHit(RaycastHit hit)
    {
        string name = hit.collider.gameObject.name;
        Transform root = hit.collider.transform.root;
        string rootName = root.name;

        // Highway exclusion
        if (name.StartsWith("Road_42") || name.StartsWith("RoadPart_07") || hit.point.x > 85f)
            return CellExcluded;

        // Building / Props / Nature exclusion
        if (rootName == "Buildings" || rootName == "Props and Nature" || rootName == "Vehicles")
            return CellExcluded;

        if (name.StartsWith("Terrain_") || name.StartsWith("Mountain_") || name.StartsWith("Hill_") ||
            name.StartsWith("BG_") || name.StartsWith("ParkingBG_") || name.StartsWith("Railway_") ||
            name.StartsWith("Tunel_") || name.StartsWith("Stairs_") || name.StartsWith("Floor_Safety"))
            return CellExcluded;

        // Normal check: must be mostly horizontal (slope <= ~30 deg)
        if (hit.normal.y < 0.85f)
            return CellExcluded;

        // Crosswalk (Road_03 or Road_04 at road height)
        if ((name.StartsWith("Road_03") || name.StartsWith("Road_04")) && hit.point.y <= 0.08f)
            return CellCrosswalk;

        // City Road surface (asphalt at road height Y <= 0.08m)
        if (name.StartsWith("Road_") && hit.point.y <= 0.08f)
            return CellRoad;

        // Sidewalk: Tile_ pieces, or Road_ pieces with curb height (Y > 0.08m)
        if (name.StartsWith("Tile_09") || name.StartsWith("Tile_10") || name.StartsWith("Tile_11") ||
            name.StartsWith("Tile_12") || name.StartsWith("Tile_14"))
            return CellSidewalk;

        if (name.StartsWith("Road_") && hit.point.y > 0.08f)
            return CellSidewalk;

        return CellExcluded;
    }

    private static List<int> FindLargestConnectedGroup(CityWalkGraph graph)
    {
        var visited = new HashSet<int>();
        List<int> largestGroup = new List<int>();

        for (int i = 0; i < graph.nodes.Count; i++)
        {
            if (visited.Contains(i)) continue;

            List<int> currentGroup = new List<int>();
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(i);
            visited.Add(i);

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                currentGroup.Add(cur);

                foreach (int n in graph.nodes[cur].neighbors)
                {
                    if (!visited.Contains(n))
                    {
                        visited.Add(n);
                        queue.Enqueue(n);
                    }
                }
            }

            if (currentGroup.Count > largestGroup.Count)
            {
                largestGroup = currentGroup;
            }
        }

        return largestGroup;
    }
}
#endif
