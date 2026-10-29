#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class GraphCleanupTool
{
    private const string GraphPath = "Assets/EcoQuest/City Scene/CityWalkGraph.asset";

    [MenuItem("NPC/Execute Graph Cleanup")]
    public static void ExecuteCleanup()
    {
        var graph = AssetDatabase.LoadAssetAtPath<CityWalkGraph>(GraphPath);
        if (graph == null)
        {
            Debug.LogError($"Could not find graph at {GraphPath}");
            return;
        }

        Undo.RecordObject(graph, "Execute Graph Cleanup");

        int mask = ~LayerMask.GetMask("Ignore Raycast", "UI");
        int initialCount = graph.nodes.Count;
        int crosswalkRemoved = 0;
        int roadRemoved = 0;

        HashSet<int> toRemove = new HashSet<int>();

        for (int i = 0; i < graph.nodes.Count; i++)
        {
            var node = graph.nodes[i];

            // 1. Check if Crosswalk
            if (node.isCrosswalk)
            {
                toRemove.Add(i);
                crosswalkRemoved++;
                continue;
            }

            // 2. Check if on Road or Excluded surface
            if (Physics.Raycast(node.position + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 2.5f, mask, QueryTriggerInteraction.Ignore))
            {
                string n = hit.collider.gameObject.name;
                bool isCross = (n.StartsWith("Road_03") || n.StartsWith("Road_04")) && hit.point.y <= 0.08f;
                bool isSide = hit.point.y >= 0.12f;

                if (isCross)
                {
                    toRemove.Add(i);
                    crosswalkRemoved++;
                    continue;
                }

                if (!isSide)
                {
                    toRemove.Add(i);
                    roadRemoved++;
                    continue;
                }
            }
            else
            {
                toRemove.Add(i);
                roadRemoved++;
            }
        }

        Debug.Log($"Identified for removal: Crosswalk={crosswalkRemoved}, Road/Excluded={roadRemoved}");

        // Build pruned graph
        int oldCount = graph.nodes.Count;
        int[] remap = new int[oldCount];
        for (int i = 0; i < oldCount; i++) remap[i] = -1;

        List<CityWalkGraph.Node> keptNodes = new List<CityWalkGraph.Node>();
        for (int i = 0; i < oldCount; i++)
        {
            if (!toRemove.Contains(i))
            {
                remap[i] = keptNodes.Count;
                keptNodes.Add(graph.nodes[i]);
            }
        }

        // Remap neighbors
        foreach (var node in keptNodes)
        {
            List<int> newNeighbors = new List<int>();
            List<float> newCosts = new List<float>();
            for (int k = 0; k < node.neighbors.Count; k++)
            {
                int oldN = node.neighbors[k];
                if (oldN >= 0 && oldN < oldCount && remap[oldN] >= 0)
                {
                    newNeighbors.Add(remap[oldN]);
                    newCosts.Add(node.costs[k]);
                }
            }
            node.neighbors = newNeighbors;
            node.costs = newCosts;
        }

        // Iteratively prune isolated nodes (neighbors.Count == 0)
        bool prunedAny = true;
        while (prunedAny)
        {
            prunedAny = false;
            HashSet<int> isolated = new HashSet<int>();
            for (int i = 0; i < keptNodes.Count; i++)
            {
                if (keptNodes[i].neighbors.Count == 0)
                {
                    isolated.Add(i);
                }
            }

            if (isolated.Count > 0)
            {
                prunedAny = true;
                int currentTotal = keptNodes.Count;
                int[] isolatedRemap = new int[currentTotal];
                for (int i = 0; i < currentTotal; i++) isolatedRemap[i] = -1;

                List<CityWalkGraph.Node> nextKept = new List<CityWalkGraph.Node>();
                for (int i = 0; i < currentTotal; i++)
                {
                    if (!isolated.Contains(i))
                    {
                        isolatedRemap[i] = nextKept.Count;
                        nextKept.Add(keptNodes[i]);
                    }
                }

                foreach (var node in nextKept)
                {
                    List<int> newN = new List<int>();
                    List<float> newC = new List<float>();
                    for (int k = 0; k < node.neighbors.Count; k++)
                    {
                        int oldN = node.neighbors[k];
                        if (oldN >= 0 && oldN < currentTotal && isolatedRemap[oldN] >= 0)
                        {
                            newN.Add(isolatedRemap[oldN]);
                            newC.Add(node.costs[k]);
                        }
                    }
                    node.neighbors = newN;
                    node.costs = newC;
                }
                keptNodes = nextKept;
            }
        }

        // Compute connected components (islands)
        var visited = new HashSet<int>();
        List<List<int>> components = new List<List<int>>();

        for (int i = 0; i < keptNodes.Count; i++)
        {
            if (visited.Contains(i)) continue;

            List<int> comp = new List<int>();
            Queue<int> q = new Queue<int>();
            q.Enqueue(i);
            visited.Add(i);

            while (q.Count > 0)
            {
                int curr = q.Dequeue();
                comp.Add(curr);

                foreach (int n in keptNodes[curr].neighbors)
                {
                    if (!visited.Contains(n))
                    {
                        visited.Add(n);
                        q.Enqueue(n);
                    }
                }
            }

            // Prune tiny components (< 10 nodes, e.g. dead stubs)
            if (comp.Count >= 10)
            {
                components.Add(comp);
            }
        }

        // Sort islands by size descending
        components.Sort((a, b) => b.Count.CompareTo(a.Count));

        // Rebuild final list ordered by island
        List<CityWalkGraph.Node> finalNodes = new List<CityWalkGraph.Node>();
        int[] islandRemap = new int[keptNodes.Count];
        for (int i = 0; i < keptNodes.Count; i++) islandRemap[i] = -1;

        List<int> finalIslandSizes = new List<int>();

        for (int islandId = 0; islandId < components.Count; islandId++)
        {
            var comp = components[islandId];
            finalIslandSizes.Add(comp.Count);
            foreach (int oldIdx in comp)
            {
                islandRemap[oldIdx] = finalNodes.Count;
                var n = keptNodes[oldIdx];
                n.islandId = islandId;
                n.isCrosswalk = false;
                finalNodes.Add(n);
            }
        }

        // Remap neighbors for final nodes
        foreach (var node in finalNodes)
        {
            List<int> newN = new List<int>();
            List<float> newC = new List<float>();
            for (int k = 0; k < node.neighbors.Count; k++)
            {
                int oldN = node.neighbors[k];
                if (oldN >= 0 && oldN < keptNodes.Count && islandRemap[oldN] >= 0)
                {
                    newN.Add(islandRemap[oldN]);
                    newC.Add(node.costs[k]);
                }
            }
            node.neighbors = newN;
            node.costs = newC;
        }

        graph.nodes = finalNodes;
        graph.islandSizes = finalIslandSizes;
        graph.allowCrosswalks = false;
        graph.RebuildCategoryLists();

        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssets();

        Debug.Log($"=== GRAPH CLEANUP FINISHED ===");
        Debug.Log($"Initial nodes: {initialCount} ➔ Kept nodes: {finalNodes.Count}");
        Debug.Log($"Total Islands: {finalIslandSizes.Count}");
        for (int id = 0; id < finalIslandSizes.Count; id++)
        {
            Debug.Log($"  Island #{id}: {finalIslandSizes[id]} nodes");
        }
    }
}
#endif
