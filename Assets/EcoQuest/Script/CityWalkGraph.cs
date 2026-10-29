using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CityWalkGraph", menuName = "EcoQuest/Walk Graph")]
public class CityWalkGraph : ScriptableObject
{
    [Serializable]
    public class Node
    {
        public Vector3 position;
        public bool isCrosswalk;
        public int islandId;
        public List<int> neighbors = new List<int>();
        public List<float> costs = new List<float>();
    }

    public List<Node> nodes = new List<Node>();
    public List<int> sidewalkNodeIndices = new List<int>();
    public List<int> crosswalkNodeIndices = new List<int>();
    public List<int> islandSizes = new List<int>();
    public bool allowCrosswalks = false;

    public int IslandCount => islandSizes != null ? islandSizes.Count : 0;

    private static readonly Rect[] DistrictZones = new Rect[]
    {
        new Rect(-165f, -105f, 85f, 75f), // SW
        new Rect(-80f, -105f, 80f, 75f),  // S-Center
        new Rect(0f, -105f, 85f, 75f),    // SE
        new Rect(-165f, -30f, 85f, 75f),  // W
        new Rect(-80f, -30f, 80f, 75f),   // Center
        new Rect(0f, -30f, 85f, 75f),     // E
        new Rect(-165f, 45f, 85f, 70f),   // NW
        new Rect(-80f, 45f, 80f, 70f),    // N-Center
        new Rect(0f, 45f, 85f, 70f)       // NE
    };

    public void RebuildCategoryLists()
    {
        sidewalkNodeIndices.Clear();
        crosswalkNodeIndices.Clear();
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].isCrosswalk) crosswalkNodeIndices.Add(i);
            else sidewalkNodeIndices.Add(i);
        }
    }

    public int FindClosestNode(Vector3 worldPos, bool sidewalkOnly = false, int matchIslandId = -1)
    {
        if (nodes.Count == 0) return -1;

        int bestIdx = -1;
        float bestSqDist = float.MaxValue;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (matchIslandId >= 0 && nodes[i].islandId != matchIslandId) continue;
            if (sidewalkOnly && nodes[i].isCrosswalk && !allowCrosswalks) continue;

            float d = (nodes[i].position - worldPos).sqrMagnitude;
            if (d < bestSqDist)
            {
                bestSqDist = d;
                bestIdx = i;
            }
        }

        if (bestIdx == -1 && matchIslandId >= 0)
        {
            return FindClosestNode(worldPos, sidewalkOnly, -1);
        }

        return bestIdx;
    }

    public Vector3 GetRandomNodeInIsland(int islandId)
    {
        List<int> candidates = new List<int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].islandId == islandId)
            {
                if (allowCrosswalks || !nodes[i].isCrosswalk)
                    candidates.Add(i);
            }
        }
        if (candidates.Count == 0) return Vector3.zero;
        return nodes[candidates[UnityEngine.Random.Range(0, candidates.Count)]].position;
    }

    public Vector3 GetRandomSidewalkNode(int zoneIndex = -1, int matchIslandId = -1)
    {
        if (matchIslandId >= 0)
        {
            return GetRandomNodeInIsland(matchIslandId);
        }

        if (sidewalkNodeIndices.Count == 0) return Vector3.zero;

        if (zoneIndex >= 0 && zoneIndex < DistrictZones.Length)
        {
            Rect zone = DistrictZones[zoneIndex];
            List<int> zoneCandidates = new List<int>();
            foreach (int i in sidewalkNodeIndices)
            {
                Vector3 p = nodes[i].position;
                if (zone.Contains(new Vector2(p.x, p.z)))
                {
                    zoneCandidates.Add(i);
                }
            }
            if (zoneCandidates.Count > 0)
            {
                int pick = zoneCandidates[UnityEngine.Random.Range(0, zoneCandidates.Count)];
                return nodes[pick].position;
            }
        }

        int randIdx = sidewalkNodeIndices[UnityEngine.Random.Range(0, sidewalkNodeIndices.Count)];
        return nodes[randIdx].position;
    }

    public List<Vector3> FindPath(Vector3 startPos, Vector3 targetPos, int forceIslandId = -1)
    {
        List<Vector3> result = new List<Vector3>();
        if (nodes.Count == 0) return result;

        int startNode = FindClosestNode(startPos, true, forceIslandId);
        int targetIsland = (forceIslandId >= 0) ? forceIslandId : (startNode >= 0 ? nodes[startNode].islandId : -1);
        int targetNode = FindClosestNode(targetPos, true, targetIsland);

        if (startNode < 0 || targetNode < 0) return result;
        if (startNode == targetNode)
        {
            result.Add(nodes[startNode].position);
            return result;
        }

        // A* Pathfinding
        var openSet = new HashSet<int> { startNode };
        var cameFrom = new Dictionary<int, int>();
        var gScore = new Dictionary<int, float> { [startNode] = 0f };
        var fScore = new Dictionary<int, float> { [startNode] = Vector3.Distance(nodes[startNode].position, nodes[targetNode].position) };

        int closestFallback = startNode;
        float closestDistToTarget = Vector3.Distance(nodes[startNode].position, nodes[targetNode].position);

        while (openSet.Count > 0)
        {
            // Find lowest fScore in openSet
            int current = -1;
            float lowestF = float.MaxValue;
            foreach (int n in openSet)
            {
                float f = fScore.TryGetValue(n, out float val) ? val : float.MaxValue;
                if (f < lowestF)
                {
                    lowestF = f;
                    current = n;
                }
            }

            if (current == targetNode)
            {
                // Reconstruct path
                return ReconstructPath(cameFrom, current);
            }

            openSet.Remove(current);

            float distToTarget = Vector3.Distance(nodes[current].position, nodes[targetNode].position);
            if (distToTarget < closestDistToTarget)
            {
                closestDistToTarget = distToTarget;
                closestFallback = current;
            }

            Node curNode = nodes[current];
            for (int i = 0; i < curNode.neighbors.Count; i++)
            {
                int neighbor = curNode.neighbors[i];
                if (!allowCrosswalks && nodes[neighbor].isCrosswalk) continue;

                float cost = curNode.costs[i];
                float tentativeG = (gScore.TryGetValue(current, out float g) ? g : float.MaxValue) + cost;

                float neighborG = gScore.TryGetValue(neighbor, out float nG) ? nG : float.MaxValue;
                if (tentativeG < neighborG)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    float h = Vector3.Distance(nodes[neighbor].position, nodes[targetNode].position);
                    fScore[neighbor] = tentativeG + h;
                    openSet.Add(neighbor);
                }
            }
        }

        // If target was unreachable, return path to closest reachable node
        if (cameFrom.ContainsKey(closestFallback))
        {
            return ReconstructPath(cameFrom, closestFallback);
        }

        result.Add(nodes[startNode].position);
        return result;
    }

    private List<Vector3> ReconstructPath(Dictionary<int, int> cameFrom, int current)
    {
        List<Vector3> path = new List<Vector3>();
        path.Add(nodes[current].position);

        while (cameFrom.TryGetValue(current, out int prev))
        {
            current = prev;
            path.Add(nodes[current].position);
        }

        path.Reverse();
        return path;
    }
}
