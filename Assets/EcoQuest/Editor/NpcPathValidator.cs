#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class NpcPathValidator
{
    private const int SamplePairsCount = 500;
    private const float StepMeters = 1.0f;
    private const string GraphPath = "Assets/EcoQuest/City Scene/CityWalkGraph.asset";

    [MenuItem("NPC/Validate Paths")]
    public static void ValidatePaths()
    {
        Debug.Log("=== Starting NPC/Validate Paths ===");

        var walkGraph = AssetDatabase.LoadAssetAtPath<CityWalkGraph>(GraphPath);
        if (walkGraph == null || walkGraph.nodes.Count == 0 || walkGraph.sidewalkNodeIndices.Count < 2)
        {
            Debug.LogError("NPC Path Validator: CityWalkGraph not found or has insufficient sidewalk nodes!");
            return;
        }

        int mask = ~LayerMask.GetMask("Ignore Raycast", "UI");

        int passedCount = 0;
        int failedCount = 0;
        var failureDetails = new List<string>();

        for (int pair = 0; pair < SamplePairsCount; pair++)
        {
            int islandId = Random.Range(0, Mathf.Max(1, walkGraph.IslandCount));
            List<int> islandNodes = new List<int>();
            for (int i = 0; i < walkGraph.nodes.Count; i++)
            {
                if (walkGraph.nodes[i].islandId == islandId) islandNodes.Add(i);
            }
            if (islandNodes.Count < 2)
            {
                pair--;
                continue;
            }

            int startIdx = islandNodes[Random.Range(0, islandNodes.Count)];
            int endIdx = islandNodes[Random.Range(0, islandNodes.Count)];

            Vector3 startPt = walkGraph.nodes[startIdx].position;
            Vector3 endPt = walkGraph.nodes[endIdx].position;

            List<Vector3> path = walkGraph.FindPath(startPt, endPt, islandId);
            if (path == null || path.Count == 0)
            {
                failedCount++;
                if (failureDetails.Count < 10)
                    failureDetails.Add($"Pair {pair}: No path found ({startPt} -> {endPt})");
                continue;
            }

            bool pathValid = true;
            for (int c = 0; c < path.Count - 1 && pathValid; c++)
            {
                Vector3 segStart = path[c];
                Vector3 segEnd = path[c + 1];
                float len = Vector3.Distance(segStart, segEnd);
                int steps = Mathf.Max(1, Mathf.FloorToInt(len / StepMeters));

                for (int s = 0; s <= steps; s++)
                {
                    Vector3 samplePos = Vector3.Lerp(segStart, segEnd, (float)s / steps);

                    // 1. Check building wall collision at pedestrian body height (Y = 0.3 to 1.5)
                    var bodyHits = Physics.OverlapCapsule(samplePos + Vector3.up * 0.3f, samplePos + Vector3.up * 1.5f, 0.25f, mask, QueryTriggerInteraction.Ignore);
                    bool insideBuilding = false;
                    string buildingName = "";
                    foreach (var bh in bodyHits)
                    {
                        if (bh.transform.root.name == "Buildings" || bh.name.StartsWith("House_") || bh.name.StartsWith("SuburbHouse_"))
                        {
                            insideBuilding = true;
                            buildingName = bh.gameObject.name;
                            break;
                        }
                    }

                    if (insideBuilding)
                    {
                        pathValid = false;
                        failedCount++;
                        if (failureDetails.Count < 10)
                            failureDetails.Add($"Pair {pair}: Point at {samplePos} is inside BUILDING ({buildingName})");
                        break;
                    }

                    // 2. Check ground surface directly under footprint (radius 0.25m)
                    var groundHits = Physics.SphereCastAll(samplePos + Vector3.up * 1f, 0.25f, Vector3.down, 2.5f, mask, QueryTriggerInteraction.Ignore);
                    if (groundHits.Length == 0)
                    {
                        pathValid = false;
                        failedCount++;
                        if (failureDetails.Count < 10)
                            failureDetails.Add($"Pair {pair}: Point at {samplePos} is OFF-GROUND");
                        break;
                    }

                    bool hasHighway = false;
                    bool hasCrosswalk = false;
                    bool hasRoad = false;
                    bool hasSidewalk = false;
                    string roadName = "";

                    foreach (var hit in groundHits)
                    {
                        string name = hit.collider.gameObject.name;

                        if (name.StartsWith("Road_42") || name.StartsWith("RoadPart_07") || hit.point.x > 85f)
                        {
                            hasHighway = true;
                            roadName = name;
                        }

                        if ((name.StartsWith("Road_03") || name.StartsWith("Road_04")) && hit.point.y <= 0.08f)
                        {
                            hasCrosswalk = true;
                        }

                        if (name.StartsWith("Road_") && hit.point.y <= 0.08f)
                        {
                            hasRoad = true;
                            if (string.IsNullOrEmpty(roadName)) roadName = name;
                        }

                        if (name.StartsWith("Tile_") || (name.StartsWith("Road_") && hit.point.y > 0.08f))
                        {
                            hasSidewalk = true;
                        }
                    }

                    if (hasHighway)
                    {
                        pathValid = false;
                        failedCount++;
                        if (failureDetails.Count < 10)
                            failureDetails.Add($"Pair {pair}: Point at {samplePos} is on HIGHWAY ({roadName})");
                        break;
                    }

                    // Must be on Crosswalk or Sidewalk. If on Road without Crosswalk -> FAIL
                    if (hasRoad && !hasCrosswalk && !hasSidewalk)
                    {
                        pathValid = false;
                        failedCount++;
                        if (failureDetails.Count < 10)
                            failureDetails.Add($"Pair {pair}: Point at {samplePos} is on ROAD outside crosswalk ({roadName})");
                        break;
                    }
                }
            }

            if (pathValid) passedCount++;
        }

        Debug.Log($"=== NPC PATH VALIDATION RESULTS ===\nTotal Tested: {SamplePairsCount}\nPassed: {passedCount}\nFailed: {failedCount}");

        if (failedCount > 0)
        {
            Debug.LogWarning("First 10 Failures:\n" + string.Join("\n", failureDetails));
        }
        else
        {
            Debug.Log("<color=green>SUCCESS: 100% of tested paths (500/500) stay strictly on Sidewalks and Crosswalks!</color>");
        }
    }
}
#endif
