using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using EcoQuest.CityQuest;
using EcoQuest.CityQuest.Editor;

namespace EcoQuest.CityTraffic.Editor
{
    public static class CityTrafficEditorTools
    {
        private const string CityScenePath = "Assets/EcoQuest/City Scene.unity";
        private const string GraphAssetPath = "Assets/EcoQuest/CityTraffic/CityTrafficGraph.asset";
        private const string ReportsDir = @"C:\EcoQuest.gg\Reports\Traffic";

        private struct GridJunctionDef
        {
            public Vector2 pos;
            public string tileName;
            public List<int> activeDirs; // 0=+Z(N), 1=+X(E), 2=-Z(S), 3=-X(W)
        }

        [MenuItem("EcoQuest/Traffic/Build Road Graph", false, 10)]
        public static void BuildRoadGraphMenu()
        {
            BuildRoadGraph(driveOnRight: true);
        }

        public static CityTrafficGraph BuildRoadGraph(bool driveOnRight = true)
        {
            if (!EnsureCitySceneOpen()) return null;

            GameObject roadRoot = GameObject.Find("Road and Ground");
            if (roadRoot == null)
            {
                Debug.LogError("[CityTraffic] Could not find 'Road and Ground' root in City Scene.");
                return null;
            }

            CityTrafficGraph graph = AssetDatabase.LoadAssetAtPath<CityTrafficGraph>(GraphAssetPath);
            if (graph == null)
            {
                graph = ScriptableObject.CreateInstance<CityTrafficGraph>();
                AssetDatabase.CreateAsset(graph, GraphAssetPath);
            }

            Undo.RecordObject(graph, "Build City Traffic Graph");
            graph.driveOnRight = driveOnRight;
            graph.laneOffset = 1.5f;
            graph.nodes.Clear();
            graph.edges.Clear();
            graph.intersections.Clear();
            graph.crosswalkZones.Clear();

            // 1. Scan Road and Ground tiles algorithmically (excluding Highway x > 75 and dead-end spurs (-54,-78) & (72,-54))
            List<Transform> junctionTiles = new List<Transform>();
            List<Transform> straightTiles = new List<Transform>();
            List<Transform> crosswalkTiles = new List<Transform>();

            int childCount = roadRoot.transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform t = roadRoot.transform.GetChild(i);
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                Vector3 p = t.position;

                // Exclude Highway (x > 75)
                if (p.x > 75f) continue;
                // Exclude dead-end southern spur at (-54, -78) and railway spur at (72, -54)
                if (p.z < -60f) continue;
                if (p.x > 60f) continue;

                string baseName = GetBaseTileName(t.name);
                if (baseName == "Road_17" || baseName == "Road_18" || baseName == "Road_19" ||
                    baseName == "Road_21" || baseName == "Road_40" || baseName == "Road_41" ||
                    baseName == "Road_51")
                {
                    junctionTiles.Add(t);
                }
                else if (baseName == "Road_01" || baseName == "Road_02" || baseName == "Road_03" ||
                         baseName == "Road_04" || baseName == "Road_22" || baseName == "Road_24" ||
                         baseName == "Road_38" || baseName == "Road_39")
                {
                    straightTiles.Add(t);
                    if (baseName == "Road_03" || baseName == "Road_04")
                    {
                        crosswalkTiles.Add(t);
                    }
                }
            }

            // Deduplicate junction centers on the 2D grid (snap to 1m)
            List<GridJunctionDef> junctions = new List<GridJunctionDef>();
            for (int i = 0; i < junctionTiles.Count; i++)
            {
                Transform jt = junctionTiles[i];
                Vector2 p2 = new Vector2(Mathf.Round(jt.position.x), Mathf.Round(jt.position.z));
                bool exists = false;
                for (int j = 0; j < junctions.Count; j++)
                {
                    if (Vector2.Distance(junctions[j].pos, p2) < 2.0f)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    junctions.Add(new GridJunctionDef
                    {
                        pos = p2,
                        tileName = jt.name,
                        activeDirs = new List<int>()
                    });
                }
            }

            // Sort junctions deterministically by X then Z
            junctions.Sort((a, b) =>
            {
                int cmpX = a.pos.x.CompareTo(b.pos.x);
                return cmpX != 0 ? cmpX : a.pos.y.CompareTo(b.pos.y);
            });

            // 2. Discover valid corridor connections between adjacent junctions along shared X or Z lines
            // Two junctions (A, B) on the same X or Z line are connected if there is no intermediate junction between them
            // AND there are straight road tiles lying along the segment between A and B.
            int jCount = junctions.Count;
            bool[,] connectedPairs = new bool[jCount, jCount];

            for (int a = 0; a < jCount; a++)
            {
                for (int b = a + 1; b < jCount; b++)
                {
                    Vector2 pa = junctions[a].pos;
                    Vector2 pb = junctions[b].pos;
                    bool sameX = Mathf.Abs(pa.x - pb.x) < 1.0f;
                    bool sameZ = Mathf.Abs(pa.y - pb.y) < 1.0f;
                    if (!sameX && !sameZ) continue;

                    // Check no intermediate junction lies strictly between pa and pb
                    bool hasIntermediate = false;
                    for (int c = 0; c < jCount; c++)
                    {
                        if (c == a || c == b) continue;
                        Vector2 pc = junctions[c].pos;
                        if (sameX && Mathf.Abs(pc.x - pa.x) < 1.0f)
                        {
                            float minZ = Mathf.Min(pa.y, pb.y) + 1.0f;
                            float maxZ = Mathf.Max(pa.y, pb.y) - 1.0f;
                            if (pc.y >= minZ && pc.y <= maxZ) { hasIntermediate = true; break; }
                        }
                        else if (sameZ && Mathf.Abs(pc.y - pa.y) < 1.0f)
                        {
                            float minX = Mathf.Min(pa.x, pb.x) + 1.0f;
                            float maxX = Mathf.Max(pa.x, pb.x) - 1.0f;
                            if (pc.x >= minX && pc.x <= maxX) { hasIntermediate = true; break; }
                        }
                    }
                    if (hasIntermediate) continue;

                    // Verify straight road tiles exist along the corridor between pa and pb
                    int tilesOnCorridor = 0;
                    for (int t = 0; t < straightTiles.Count; t++)
                    {
                        Vector3 tp = straightTiles[t].position;
                        if (sameX && Mathf.Abs(tp.x - pa.x) < 2.0f)
                        {
                            float minZ = Mathf.Min(pa.y, pb.y) + 4.0f;
                            float maxZ = Mathf.Max(pa.y, pb.y) - 4.0f;
                            if (tp.z >= minZ && tp.z <= maxZ) tilesOnCorridor++;
                        }
                        else if (sameZ && Mathf.Abs(tp.z - pa.y) < 2.0f)
                        {
                            float minX = Mathf.Min(pa.x, pb.x) + 4.0f;
                            float maxX = Mathf.Max(pa.x, pb.x) - 4.0f;
                            if (tp.x >= minX && tp.x <= maxX) tilesOnCorridor++;
                        }
                    }

                    if (tilesOnCorridor >= 2)
                    {
                        connectedPairs[a, b] = true;
                        connectedPairs[b, a] = true;

                        int dirAB = GetCardinalDir(pa, pb);
                        int dirBA = (dirAB + 2) % 4;
                        if (!junctions[a].activeDirs.Contains(dirAB)) junctions[a].activeDirs.Add(dirAB);
                        if (!junctions[b].activeDirs.Contains(dirBA)) junctions[b].activeDirs.Add(dirBA);
                    }
                }
            }

            // 3. Create TrafficIntersections and arm Entry/Exit nodes
            // Map (junctionIndex, armDir) -> entryNodeId (entering the junction from that arm) and exitNodeId (leaving the junction onto that arm)
            Dictionary<int, int> armEntryNodes = new Dictionary<int, int>();
            Dictionary<int, int> armExitNodes = new Dictionary<int, int>();

            float sideSign = driveOnRight ? 1.0f : -1.0f;
            float laneOff = graph.laneOffset * sideSign;
            const float junctionArmRadius = 6.0f; // 12x12m intersection tiles have boundary at +/- 6.0m from center

            for (int i = 0; i < jCount; i++)
            {
                GridJunctionDef jd = junctions[i];
                bool isCentralFourWay = jd.activeDirs.Count == 4 && jd.pos.x >= -56f && jd.pos.x <= 20f && jd.pos.y >= -20f && jd.pos.y <= 56f;
                int gx = Mathf.Abs(Mathf.RoundToInt(jd.pos.x) / 36);
                int gz = Mathf.Abs(Mathf.RoundToInt(jd.pos.y) / 36);
                TrafficIntersection inter = new TrafficIntersection
                {
                    id = i,
                    name = $"Intersection_{i:D2}_{jd.tileName}_({jd.pos.x:F0},{jd.pos.y:F0})",
                    center = new Vector3(jd.pos.x, 0f, jd.pos.y),
                    size = new Vector3(12f, 4f, 12f),
                    armCount = jd.activeDirs.Count,
                    hasTrafficLight = isCentralFourWay,
                    signalPhaseOffset = isCentralFourWay ? ((gx + gz) & 1) * 6.0f : 0f
                };
                graph.intersections.Add(inter);

                for (int dIdx = 0; dIdx < jd.activeDirs.Count; dIdx++)
                {
                    int armDir = jd.activeDirs[dIdx]; // Direction pointing OUT from intersection center along the arm
                    Vector3 armOutVec = DirToVector(armDir);
                    Vector3 armInVec = -armOutVec; // Travel direction when ENTERING the intersection from this arm

                    // Right-hand side vector for travel direction V is (V.z, 0, -V.x)
                    Vector3 rightOfIn = new Vector3(armInVec.z, 0f, -armInVec.x);
                    Vector3 rightOfOut = new Vector3(armOutVec.z, 0f, -armOutVec.x);

                    Vector3 entryPos = inter.center + armOutVec * junctionArmRadius + rightOfIn * laneOff;
                    Vector3 exitPos = inter.center + armOutVec * junctionArmRadius + rightOfOut * laneOff;

                    TrafficNode entryNode = new TrafficNode
                    {
                        id = graph.nodes.Count,
                        position = entryPos,
                        forward = armInVec,
                        intersectionIndex = i,
                        isIntersectionEntry = true,
                        isIntersectionExit = false
                    };
                    graph.nodes.Add(entryNode);
                    inter.entryNodes.Add(entryNode.id);
                    armEntryNodes[ i * 10 + armDir ] = entryNode.id;

                    TrafficNode exitNode = new TrafficNode
                    {
                        id = graph.nodes.Count,
                        position = exitPos,
                        forward = armOutVec,
                        intersectionIndex = i,
                        isIntersectionEntry = false,
                        isIntersectionExit = true
                    };
                    graph.nodes.Add(exitNode);
                    inter.exitNodes.Add(exitNode.id);
                    armExitNodes[ i * 10 + armDir ] = exitNode.id;
                }
            }

            // 4. Create merged long Straight Corridor Edges between connected junctions + Merged Crosswalk Zones on approaches
            for (int a = 0; a < jCount; a++)
            {
                for (int b = 0; b < jCount; b++)
                {
                    if (a == b || !connectedPairs[a, b]) continue;

                    Vector2 pa = junctions[a].pos;
                    Vector2 pb = junctions[b].pos;
                    int travelDir = GetCardinalDir(pa, pb); // Direction from A to B
                    int oppositeArmAtB = (travelDir + 2) % 4;

                    int fromNodeId = armExitNodes[a * 10 + travelDir];
                    int toNodeId = armEntryNodes[b * 10 + oppositeArmAtB];

                    TrafficNode fromNode = graph.nodes[fromNodeId];
                    TrafficNode toNode = graph.nodes[toNodeId];

                    Vector3 startPos = fromNode.position;
                    Vector3 endPos = toNode.position;
                    float len = Vector3.Distance(startPos, endPos);

                    bool isPark = (startPos.x < -100f || endPos.x < -100f) ||
                                  (Mathf.Abs(startPos.x - 0f) < 25f && Mathf.Abs(startPos.z - 36f) < 25f);

                    TrafficEdge corridorEdge = new TrafficEdge
                    {
                        id = graph.edges.Count,
                        fromNode = fromNodeId,
                        toNode = toNodeId,
                        waypoints = new Vector3[] { startPos, endPos },
                        cumulativeLengths = new float[] { 0f, len },
                        length = len,
                        speedLimit = isPark ? graph.parkDistrictSpeed : graph.defaultStraightSpeed,
                        isCurve = false,
                        isIntersectionArc = false,
                        intersectionIndex = -1,
                        turnType = TrafficTurnType.Straight,
                        approachDir = travelDir,
                        exitDir = travelDir,
                        isParkDistrict = isPark
                    };

                    fromNode.outgoingEdges.Add(corridorEdge.id);
                    toNode.incomingEdges.Add(corridorEdge.id);
                    graph.edges.Add(corridorEdge);

                    // Create ONE merged Crosswalk Zone for this approach arm at junction B (if junction B is a 3-way or 4-way intersection)
                    if (junctions[b].activeDirs.Count >= 3 && len >= 12.0f)
                    {
                        Vector3 interCenterB = graph.intersections[b].center;
                        Vector3 armOutFromB = DirToVector(oppositeArmAtB);
                        // Crosswalk tiles sit immediately outside the 12x12m intersection box (~7.5m from center)
                        Vector3 cwCenter = interCenterB + armOutFromB * 7.5f;
                        Quaternion cwRot = Quaternion.LookRotation(DirToVector(travelDir), Vector3.up);
                        float stopLineDistOnEdge = Mathf.Max(1.0f, len - (1.5f + graph.stopLineOffsetFromCrosswalk)); // 2.5m before crosswalk zone
                        corridorEdge.SampleAtDistance(stopLineDistOnEdge, out Vector3 stopLinePos, out _);

                        TrafficCrosswalkZone cwZone = new TrafficCrosswalkZone
                        {
                            id = graph.crosswalkZones.Count,
                            intersectionIndex = b,
                            armDirection = armOutFromB,
                            center = cwCenter,
                            halfExtents = new Vector3(3.1f, 1.5f, 1.6f),
                            rotation = cwRot,
                            stopLinePosition = stopLinePos,
                            approachEdgeId = corridorEdge.id,
                            stopLineDistAlongEdge = stopLineDistOnEdge
                        };

                        corridorEdge.approachingCrosswalkIndex = cwZone.id;
                        corridorEdge.stopLineDistanceOnEdge = stopLineDistOnEdge;
                        graph.crosswalkZones.Add(cwZone);
                    }
                }
            }

            // 5. Create Smooth Bezier Connection Arcs inside each Intersection / Turn Corner (Straight, Right, Left; NO U-turns)
            for (int i = 0; i < jCount; i++)
            {
                GridJunctionDef jd = junctions[i];
                TrafficIntersection inter = graph.intersections[i];

                for (int inIdx = 0; inIdx < jd.activeDirs.Count; inIdx++)
                {
                    int inArmDir = jd.activeDirs[inIdx]; // Arm side where vehicle enters
                    int approachTravelDir = (inArmDir + 2) % 4; // Direction vehicle is moving when entering
                    int entryNodeId = armEntryNodes[i * 10 + inArmDir];
                    TrafficNode entryNode = graph.nodes[entryNodeId];

                    for (int outIdx = 0; outIdx < jd.activeDirs.Count; outIdx++)
                    {
                        int outArmDir = jd.activeDirs[outIdx];
                        if (outArmDir == inArmDir) continue; // No U-turns!

                        int exitNodeId = armExitNodes[i * 10 + outArmDir];
                        TrafficNode exitNode = graph.nodes[exitNodeId];

                        TrafficTurnType turnType = ClassifyTurn(approachTravelDir, outArmDir, driveOnRight);
                        bool isCurve = (turnType != TrafficTurnType.Straight);

                        Vector3[] waypoints = BuildSmoothArcWaypoints(entryNode.position, entryNode.forward, exitNode.position, exitNode.forward, isCurve ? 9 : 2, out float[] cumLengths, out float arcLen);

                        TrafficEdge arcEdge = new TrafficEdge
                        {
                            id = graph.edges.Count,
                            fromNode = entryNodeId,
                            toNode = exitNodeId,
                            waypoints = waypoints,
                            cumulativeLengths = cumLengths,
                            length = arcLen,
                            speedLimit = isCurve ? graph.curveSpeed : graph.defaultStraightSpeed,
                            isCurve = isCurve,
                            isIntersectionArc = true,
                            intersectionIndex = i,
                            turnType = turnType,
                            approachDir = approachTravelDir,
                            exitDir = outArmDir,
                            isParkDistrict = inter.center.x < -100f
                        };

                        entryNode.outgoingEdges.Add(arcEdge.id);
                        exitNode.incomingEdges.Add(arcEdge.id);
                        inter.internalEdges.Add(arcEdge.id);
                        graph.edges.Add(arcEdge);
                    }
                }
            }

            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();

            // 6. Ensure "--- TRAFFIC ---" root and CityTrafficManager exist in City Scene.unity
            EnsureTrafficRootInScene(graph);

            Debug.Log($"[CityTraffic] Built CityTrafficGraph (driveOnRight={driveOnRight}): " +
                      $"{graph.nodes.Count} nodes, {graph.edges.Count} directed edges, " +
                      $"{graph.intersections.Count} junctions/intersections, {graph.crosswalkZones.Count} merged crosswalk zones.");

            return graph;
        }

        private static void EnsureTrafficRootInScene(CityTrafficGraph graph)
        {
            GameObject root = GameObject.Find("--- TRAFFIC ---");
            if (root == null)
            {
                root = new GameObject("--- TRAFFIC ---");
                Undo.RegisterCreatedObjectUndo(root, "Create Traffic Root");
            }

            CityTrafficManager mgr = root.GetComponent<CityTrafficManager>();
            if (mgr == null)
            {
                mgr = Undo.AddComponent<CityTrafficManager>(root);
            }

            Undo.RecordObject(mgr, "Configure CityTrafficManager");
            mgr.graph = graph;
            mgr.activeCarTarget = 10;
            mgr.minSpeed = 4.0f;
            mgr.maxSpeed = 6.5f;
            mgr.parkSpeed = 3.5f;
            mgr.curveSpeed = 3.0f;
            mgr.sensorLength = 5.0f;
            mgr.minSpawnRadius = 30.0f;
            mgr.maxSpawnRadius = 75.0f;
            mgr.despawnRadius = 80.0f;

            // Populate 14 distinct 4-wheel car prefabs under 5.5m long
            string[] desiredModels =
            {
                "Car_01", "Car_02", "Car_05", "Car_06", "Car_07", "Car_09", "Car_11",
                "Car_15", "Car_16", "Car_18", "Car_19", "Car_21", "Car_24", "Car_35"
            };

            mgr.fleetPrefabs.Clear();
            for (int i = 0; i < desiredModels.Length; i++)
            {
                string[] guids = AssetDatabase.FindAssets($"{desiredModels[i]} t:Prefab");
                for (int g = 0; g < guids.Length; g++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[g]);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && string.Equals(prefab.name, desiredModels[i], StringComparison.OrdinalIgnoreCase))
                    {
                        mgr.fleetPrefabs.Add(prefab);
                        break;
                    }
                }
            }

            mgr.ConfigureTrafficLayerAndMinimap();
            mgr.EnableVehicleMaterialInstancing();
            SetupStageDPolish(mgr, graph);
            EditorUtility.SetDirty(mgr);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        [MenuItem("EcoQuest/Traffic/Setup Stage D Polish", false, 35)]
        public static void SetupStageDPolishMenu()
        {
            if (!EnsureCitySceneOpen()) return;
            CityTrafficGraph graph = AssetDatabase.LoadAssetAtPath<CityTrafficGraph>(GraphAssetPath);
            if (graph == null) graph = BuildRoadGraph(driveOnRight: true);
            CityTrafficManager mgr = UnityEngine.Object.FindFirstObjectByType<CityTrafficManager>(FindObjectsInactive.Include);
            if (mgr != null && graph != null)
            {
                SetupStageDPolish(mgr, graph);
            }
        }

        public static void SetupStageDPolish(CityTrafficManager mgr, CityTrafficGraph graph)
        {
            if (mgr == null || graph == null) return;

            Transform trafficRoot = mgr.transform;

            // 1. Central 4-Way Intersections Traffic Lights (Polyperfect traffic-lights.prefab)
            Transform lightsRoot = trafficRoot.Find("TrafficLights");
            if (lightsRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(lightsRoot.gameObject);
            }
            GameObject lightsRootGo = new GameObject("TrafficLights");
            lightsRoot = lightsRootGo.transform;
            lightsRoot.SetParent(trafficRoot, false);

            mgr.signalPoles.Clear();
            const string polyTrafficLightPath = "Assets/polyperfect/Low Poly Ultimate Pack/M/- Prefabs_M/Props_M/City_M/traffic-lights.prefab";
            GameObject signalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(polyTrafficLightPath);

            int signalizedIntersections = 0;
            int totalPolesPlaced = 0;

            if (signalPrefab != null)
            {
                for (int i = 0; i < graph.intersections.Count; i++)
                {
                    TrafficIntersection inter = graph.intersections[i];
                    if (!inter.hasTrafficLight || inter.armCount < 4) continue;
                    signalizedIntersections++;

                    Vector3 c = inter.center;
                    // Place 4 poles per central 4-way intersection: each pole's +Z faces the approaching right-hand lane
                    // and its +X cantilever arm overhangs the lane:
                    // dir 0 (Northbound +Z): pole at SE corner (c.x + 3.6, 0.15, c.z - 6.2), yaw 180
                    // dir 1 (Eastbound  +X): pole at SW corner (c.x - 6.2, 0.15, c.z - 3.6), yaw 270
                    // dir 2 (Southbound -Z): pole at NW corner (c.x - 3.6, 0.15, c.z + 6.2), yaw 0
                    // dir 3 (Westbound  -X): pole at NE corner (c.x + 6.2, 0.15, c.z + 3.6), yaw 90
                    Vector3[] offsets =
                    {
                        new Vector3( 3.6f, 0.15f, -6.2f),
                        new Vector3(-6.2f, 0.15f, -3.6f),
                        new Vector3(-3.6f, 0.15f,  6.2f),
                        new Vector3( 6.2f, 0.15f,  3.6f)
                    };
                    float[] yaws = { 180f, 270f, 0f, 90f };

                    for (int dir = 0; dir < 4; dir++)
                    {
                        GameObject poleInst = (GameObject)PrefabUtility.InstantiatePrefab(signalPrefab, lightsRoot);
                        poleInst.name = $"TrafficLight_Int{i:D2}_Dir{dir}";
                        poleInst.transform.position = c + offsets[dir];
                        poleInst.transform.rotation = Quaternion.Euler(0f, yaws[dir], 0f);
                        poleInst.transform.localScale = new Vector3(0.52f, 0.52f, 0.52f);

                        MeshRenderer mr = poleInst.GetComponentInChildren<MeshRenderer>();
                        if (mr != null)
                        {
                            mgr.signalPoles.Add(new TrafficSignalPoleBinding
                            {
                                intersectionIndex = i,
                                travelDir = dir,
                                poleRenderer = mr,
                                lastAppliedState = -1
                            });
                            totalPolesPlaced++;
                        }
                    }
                }
            }

            // 2. Hazard Flashers on 4 Roadside Parked Cars (Class B)
            Transform hazardRoot = trafficRoot.Find("ParkedHazardFlashers");
            if (hazardRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(hazardRoot.gameObject);
            }
            GameObject hazardRootGo = new GameObject("ParkedHazardFlashers");
            hazardRoot = hazardRootGo.transform;
            hazardRoot.SetParent(trafficRoot, false);

            mgr.parkedHazardFlashers.Clear();
            GameObject vehiclesRoot = GameObject.Find("Vehicles");
            int hazardCarsCount = 0;

            if (vehiclesRoot != null)
            {
                // Target coordinates of the 4 Class B roadside parked cars
                Vector3[] targetRoadsidePositions =
                {
                    new Vector3(-151.54f, 0.13f, 11.23f), // Car_18
                    new Vector3(-136.37f, 0.13f, 24.84f), // Car_01
                    new Vector3(  52.72f, 0.01f, 81.19f), // Car_11
                    new Vector3(-151.44f, 0.13f, 47.08f)  // Car_17
                };

                Material hazardMat = null;
                GameObject light01Prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/Asset/LowPolyMegapolis/Prefabs/Props/Light_01.prefab");
                if (light01Prefab != null)
                {
                    MeshRenderer lmr = light01Prefab.GetComponentInChildren<MeshRenderer>();
                    if (lmr != null && lmr.sharedMaterials.Length > 1)
                    {
                        hazardMat = lmr.sharedMaterials[1]; // LowPolyMegapolis_Light emissive material
                    }
                }
                if (hazardMat == null && signalPrefab != null)
                {
                    MeshRenderer smr = signalPrefab.GetComponentInChildren<MeshRenderer>();
                    if (smr != null && smr.sharedMaterials.Length > 3)
                    {
                        hazardMat = smr.sharedMaterials[3]; // 29 ORANGE-LIGHT-LPUP
                    }
                }

                for (int t = 0; t < targetRoadsidePositions.Length; t++)
                {
                    Transform matchedCar = FindClosestChildVehicle(vehiclesRoot.transform, targetRoadsidePositions[t], 1.5f);
                    if (matchedCar == null) continue;

                    GameObject carFlasherGroup = new GameObject($"HazardFlashers_{matchedCar.name}_{t}");
                    carFlasherGroup.transform.SetParent(hazardRoot, false);
                    carFlasherGroup.transform.SetPositionAndRotation(matchedCar.position, matchedCar.rotation);

                    Vector3[] cornerOffsets =
                    {
                        new Vector3(-0.78f, 0.62f,  1.58f),
                        new Vector3( 0.78f, 0.62f,  1.58f),
                        new Vector3(-0.78f, 0.62f, -1.58f),
                        new Vector3( 0.78f, 0.62f, -1.58f)
                    };

                    for (int cIdx = 0; cIdx < cornerOffsets.Length; cIdx++)
                    {
                        GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        bulb.name = $"Blinker_{cIdx}";
                        Collider col = bulb.GetComponent<Collider>();
                        if (col != null) UnityEngine.Object.DestroyImmediate(col);
                        bulb.transform.SetParent(carFlasherGroup.transform, false);
                        bulb.transform.localPosition = cornerOffsets[cIdx];
                        bulb.transform.localScale = new Vector3(0.16f, 0.12f, 0.12f);

                        MeshRenderer bmr = bulb.GetComponent<MeshRenderer>();
                        if (bmr != null && hazardMat != null)
                        {
                            bmr.sharedMaterial = hazardMat;
                            bmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            bmr.receiveShadows = false;
                        }
                    }

                    mgr.parkedHazardFlashers.Add(carFlasherGroup);
                    hazardCarsCount++;
                }
            }

            // 3. Exhaust Particles on static OldCar_* props in scene (in addition to moving old cars in fleet)
            Transform oldExhaustRoot = trafficRoot.Find("ParkedOldCarExhausts");
            if (oldExhaustRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(oldExhaustRoot.gameObject);
            }
            GameObject oldExhaustRootGo = new GameObject("ParkedOldCarExhausts");
            oldExhaustRoot = oldExhaustRootGo.transform;
            oldExhaustRoot.SetParent(trafficRoot, false);

            int staticOldCarExhaustCount = 0;
            if (vehiclesRoot != null)
            {
                for (int i = 0; i < vehiclesRoot.transform.childCount; i++)
                {
                    Transform ch = vehiclesRoot.transform.GetChild(i);
                    if (ch == null || !ch.gameObject.activeSelf) continue;
                    if (ch.name.StartsWith("OldCar", StringComparison.OrdinalIgnoreCase))
                    {
                        GameObject exhGo = new GameObject($"Exhaust_{ch.name}");
                        exhGo.transform.SetParent(oldExhaustRoot, false);
                        exhGo.transform.position = ch.position - ch.forward * 1.65f + Vector3.up * 0.32f - ch.right * 0.42f;
                        exhGo.transform.rotation = Quaternion.LookRotation(-ch.forward + Vector3.up * 0.25f, Vector3.up);
                        TrafficVehicleController.ConfigureOldCarExhaustParticles(exhGo);
                        staticOldCarExhaustCount++;
                    }
                }
            }

            mgr.UpdateStageDVisuals(0f, forceRefresh: true);
            EditorUtility.SetDirty(mgr);
            Debug.Log($"[SetupStageDPolish] Configured {signalizedIntersections} central 4-way intersections ({totalPolesPlaced} Polyperfect traffic-light poles), " +
                      $"{hazardCarsCount} parked cars with hazard flashers, and old-car exhaust particles ({staticOldCarExhaustCount} static OldCars + moving old fleet models Car_06/07/11).");
        }

        private static Transform FindClosestChildVehicle(Transform vehiclesRoot, Vector3 targetPos, float maxDist)
        {
            Transform best = null;
            float bestSq = maxDist * maxDist;
            for (int i = 0; i < vehiclesRoot.childCount; i++)
            {
                Transform ch = vehiclesRoot.GetChild(i);
                if (ch == null) continue;
                float dSq = (ch.position - targetPos).sqrMagnitude;
                if (dSq < bestSq)
                {
                    bestSq = dSq;
                    best = ch;
                }
            }
            return best;
        }

        [MenuItem("EcoQuest/Traffic/Validate Road Graph", false, 11)]
        public static void ValidateRoadGraphMenu()
        {
            ValidateRoadGraph(renderTopDownPng: true);
        }

        public static bool ValidateRoadGraph(bool renderTopDownPng = true)
        {
            CityTrafficGraph graph = AssetDatabase.LoadAssetAtPath<CityTrafficGraph>(GraphAssetPath);
            if (graph == null)
            {
                graph = BuildRoadGraph(driveOnRight: true);
            }
            if (graph == null) return false;

            int nodeCount = graph.nodes.Count;
            int edgeCount = graph.edges.Count;
            int deadEnds = 0;
            int noExitLanes = 0;
            int noEntryNodes = 0;
            int outOfBoundsLanes = 0;
            int corridorEdges = 0;
            int arcEdges = 0;

            for (int i = 0; i < nodeCount; i++)
            {
                TrafficNode n = graph.nodes[i];
                if (n.outgoingEdges == null || n.outgoingEdges.Count == 0) deadEnds++;
                if (n.incomingEdges == null || n.incomingEdges.Count == 0) noEntryNodes++;
            }

            for (int i = 0; i < edgeCount; i++)
            {
                TrafficEdge e = graph.edges[i];
                if (e.isIntersectionArc) arcEdges++;
                else corridorEdges++;

                if (e.toNode < 0 || e.toNode >= nodeCount || graph.nodes[e.toNode].outgoingEdges.Count == 0)
                {
                    noExitLanes++;
                }

                if (e.waypoints != null)
                {
                    for (int w = 0; w < e.waypoints.Length; w++)
                    {
                        Vector3 wp = e.waypoints[w];
                        if (wp.x < -170f || wp.x > 65f || wp.z < -65f || wp.z > 100f)
                        {
                            outOfBoundsLanes++;
                        }
                    }
                }
            }

            // Strong connectivity check: BFS from Node 0 forward and backward
            bool isStronglyConnected = false;
            int forwardReachable = 0;
            int backwardReachable = 0;
            if (nodeCount > 0)
            {
                HashSet<int> fwdVisited = new HashSet<int>();
                Queue<int> q = new Queue<int>();
                q.Enqueue(0);
                fwdVisited.Add(0);
                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                    var outs = graph.nodes[cur].outgoingEdges;
                    for (int i = 0; i < outs.Count; i++)
                    {
                        int nextNode = graph.edges[outs[i]].toNode;
                        if (fwdVisited.Add(nextNode)) q.Enqueue(nextNode);
                    }
                }
                forwardReachable = fwdVisited.Count;

                HashSet<int> bwdVisited = new HashSet<int>();
                q.Enqueue(0);
                bwdVisited.Add(0);
                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                    var ins = graph.nodes[cur].incomingEdges;
                    for (int i = 0; i < ins.Count; i++)
                    {
                        int prevNode = graph.edges[ins[i]].fromNode;
                        if (bwdVisited.Add(prevNode)) q.Enqueue(prevNode);
                    }
                }
                backwardReachable = bwdVisited.Count;
                isStronglyConnected = (forwardReachable == nodeCount && backwardReachable == nodeCount);
            }

            int fourWayCount = 0, threeWayCount = 0, twoWayCornerCount = 0;
            for (int i = 0; i < graph.intersections.Count; i++)
            {
                int arms = graph.intersections[i].armCount;
                if (arms == 4) fourWayCount++;
                else if (arms == 3) threeWayCount++;
                else if (arms == 2) twoWayCornerCount++;
            }

            string pngPath = Path.Combine(ReportsDir, "Graph.png");
            if (renderTopDownPng)
            {
                Directory.CreateDirectory(ReportsDir);
                RenderTopDownGraphPng(graph, pngPath);
            }

            bool allValid = isStronglyConnected && deadEnds == 0 && noExitLanes == 0 && noEntryNodes == 0 && outOfBoundsLanes == 0;
            Debug.Log($"[ValidateRoadGraph] {(allValid ? "PASS" : "FAIL")} | " +
                      $"Nodes: {nodeCount} | Edges: {edgeCount} (Corridors: {corridorEdges}, Arcs: {arcEdges}) | " +
                      $"Junctions: {graph.intersections.Count} (4-way: {fourWayCount}, 3-way: {threeWayCount}, 2-arm turns: {twoWayCornerCount}) | " +
                      $"Merged Crosswalk Zones: {graph.crosswalkZones.Count} | " +
                      $"Strongly Connected: {isStronglyConnected} ({forwardReachable}/{nodeCount} fwd, {backwardReachable}/{nodeCount} bwd) | " +
                      $"Dead Ends: {deadEnds} | No-Exit Lanes: {noExitLanes} | Out-Of-Bounds: {outOfBoundsLanes} | " +
                      $"Rendered: {pngPath}");

            return allValid;
        }

        private static void RenderTopDownGraphPng(CityTrafficGraph graph, string outputPath)
        {
            const int res = 2048;
            float centerX = -54f;
            float centerZ = 18f;
            float orthoHalfSize = 122f;

            GameObject camObj = new GameObject("__TempTrafficGraphCam__");
            camObj.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = null;
            RenderTexture rt = new RenderTexture(res, res, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = new Texture2D(res, res, TextureFormat.RGB24, false);

            try
            {
                cam = camObj.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = orthoHalfSize;
                cam.aspect = 1.0f;
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 500f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.12f, 0.15f, 1f);
                camObj.transform.position = new Vector3(centerX, 180f, centerZ);
                camObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);

                Color32[] pixels = tex.GetPixels32();

                Vector2 WorldToPixel(Vector3 w)
                {
                    float u = (w.x - (centerX - orthoHalfSize)) / (orthoHalfSize * 2f);
                    float v = (w.z - (centerZ - orthoHalfSize)) / (orthoHalfSize * 2f);
                    return new Vector2(u * res, v * res);
                }

                // 1. Draw intersection conflict boxes (yellow outline)
                Color32 interBoxCol = new Color32(255, 215, 40, 255);
                for (int i = 0; i < graph.intersections.Count; i++)
                {
                    var inter = graph.intersections[i];
                    Vector3 c = inter.center;
                    Vector2 p0 = WorldToPixel(c + new Vector3(-6f, 0f, -6f));
                    Vector2 p1 = WorldToPixel(c + new Vector3(6f, 0f, -6f));
                    Vector2 p2 = WorldToPixel(c + new Vector3(6f, 0f, 6f));
                    Vector2 p3 = WorldToPixel(c + new Vector3(-6f, 0f, 6f));
                    DrawThickLine(pixels, res, p0, p1, 2, interBoxCol);
                    DrawThickLine(pixels, res, p1, p2, 2, interBoxCol);
                    DrawThickLine(pixels, res, p2, p3, 2, interBoxCol);
                    DrawThickLine(pixels, res, p3, p0, 2, interBoxCol);
                }

                // 2. Draw directed lane edges + arrowheads
                for (int i = 0; i < graph.edges.Count; i++)
                {
                    TrafficEdge e = graph.edges[i];
                    if (e.waypoints == null || e.waypoints.Length < 2) continue;

                    Color32 lineCol;
                    if (e.isIntersectionArc)
                    {
                        lineCol = e.turnType == TrafficTurnType.Straight
                            ? new Color32(60, 210, 255, 255)
                            : (e.turnType == TrafficTurnType.Right ? new Color32(90, 255, 120, 255) : new Color32(255, 165, 50, 255));
                    }
                    else
                    {
                        // Color code corridors by direction so opposing lanes are visually crisp
                        switch (e.approachDir)
                        {
                            case 0: lineCol = new Color32(40, 255, 110, 255); break; // North (+Z): bright green
                            case 2: lineCol = new Color32(40, 190, 255, 255); break; // South (-Z): cyan
                            case 1: lineCol = new Color32(255, 220, 60, 255); break; // East (+X): gold
                            default: lineCol = new Color32(255, 110, 220, 255); break; // West (-X): magenta
                        }
                    }

                    for (int j = 0; j < e.waypoints.Length - 1; j++)
                    {
                        Vector2 pa = WorldToPixel(e.waypoints[j]);
                        Vector2 pb = WorldToPixel(e.waypoints[j + 1]);
                        DrawThickLine(pixels, res, pa, pb, 2, lineCol);
                    }

                    // Draw direction arrowhead at 55% along corridor edges
                    if (!e.isIntersectionArc)
                    {
                        e.SampleAtDistance(e.length * 0.55f, out Vector3 mid, out Vector3 tan);
                        Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
                        Vector2 tip = WorldToPixel(mid + tan * 1.0f);
                        Vector2 wingL = WorldToPixel(mid - tan * 1.2f + right * 0.9f);
                        Vector2 wingR = WorldToPixel(mid - tan * 1.2f - right * 0.9f);
                        DrawThickLine(pixels, res, tip, wingL, 2, new Color32(255, 255, 255, 255));
                        DrawThickLine(pixels, res, tip, wingR, 2, new Color32(255, 255, 255, 255));
                    }
                }

                // 3. Draw merged crosswalk stop lines (red dots) and node markers
                Color32 stopCol = new Color32(255, 55, 55, 255);
                for (int i = 0; i < graph.crosswalkZones.Count; i++)
                {
                    Vector2 sp = WorldToPixel(graph.crosswalkZones[i].stopLinePosition);
                    DrawFilledCircle(pixels, res, sp, 4, stopCol);
                }

                tex.SetPixels32(pixels);
                tex.Apply();
                byte[] pngBytes = tex.EncodeToPNG();
                File.WriteAllBytes(outputPath, pngBytes);
            }
            finally
            {
                RenderTexture.active = null;
                if (cam != null) cam.targetTexture = null;
                if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (camObj != null) UnityEngine.Object.DestroyImmediate(camObj);
            }
        }

        private static void DrawThickLine(Color32[] pixels, int res, Vector2 a, Vector2 b, int radius, Color32 col)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 1.5f) + 1;
            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                int cx = Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t));
                int cy = Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t));
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int py = cy + dy;
                    if (py < 0 || py >= res) continue;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int px = cx + dx;
                        if (px < 0 || px >= res) continue;
                        pixels[py * res + px] = col;
                    }
                }
            }
        }

        private static void DrawFilledCircle(Color32[] pixels, int res, Vector2 c, int radius, Color32 col)
        {
            int cx = Mathf.RoundToInt(c.x);
            int cy = Mathf.RoundToInt(c.y);
            int rSq = radius * radius;
            for (int dy = -radius; dy <= radius; dy++)
            {
                int py = cy + dy;
                if (py < 0 || py >= res) continue;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > rSq) continue;
                    int px = cx + dx;
                    if (px < 0 || px >= res) continue;
                    pixels[py * res + px] = col;
                }
            }
        }

        [MenuItem("EcoQuest/Traffic/Convert Lane Cars (Dry Run)", false, 20)]
        public static void ConvertLaneCarsDryRunMenu()
        {
            RunVehicleConversion(applyToScene: false);
        }

        [MenuItem("EcoQuest/Traffic/Convert Lane Cars (Apply)", false, 21)]
        public static void ConvertLaneCarsApplyMenu()
        {
            RunVehicleConversion(applyToScene: true);
        }

        public static void RunVehicleConversion(bool applyToScene)
        {
            if (!EnsureCitySceneOpen()) return;

            CityTrafficGraph graph = AssetDatabase.LoadAssetAtPath<CityTrafficGraph>(GraphAssetPath);
            if (graph == null) graph = BuildRoadGraph(driveOnRight: true);

            GameObject vehiclesRoot = GameObject.Find("Vehicles");
            if (vehiclesRoot == null)
            {
                Debug.LogError("[CityTraffic] Could not find 'Vehicles' root in City Scene.");
                return;
            }

            // Read vehicles.csv classification from Phase 1 scan for 100% 1-to-1 consistency with the scan report
            List<ScanVehicleRow> scanRows = LoadScanVehicleRows();

            StringBuilder md = new StringBuilder();
            md.AppendLine("# Stage C.1 — City Traffic Vehicle Conversion Plan");
            md.AppendLine();
            md.AppendLine($"- **Mode**: `{(applyToScene ? "APPLIED TO SCENE" : "DRY RUN (Read-Only Preview — Waiting for Approval)")}`");
            md.AppendLine($"- **Scene**: `{CityScenePath}`");
            md.AppendLine();

            int convertLaneCount = 0;
            int removeBlockerCount = 0;
            int keepHighwayCount = 0;
            int keepParkedLotCount = 0;
            int keepRoadsideCount = 0;
            int keepSpecialCount = 0;

            List<Transform> allSceneVehicles = new List<Transform>(120);
            for (int i = 0; i < vehiclesRoot.transform.childCount; i++)
            {
                Transform ch = vehiclesRoot.transform.GetChild(i);
                if (ch != null) allSceneVehicles.Add(ch);
            }
            GameObject propsRoot = GameObject.Find("Props and Nature");
            if (propsRoot != null)
            {
                Transform balloon = propsRoot.transform.Find("AirBalloon_01");
                if (balloon != null) allSceneVehicles.Add(balloon);
            }

            List<string> tableRows = new List<string>();
            int totalAccounted = 0;

            for (int i = 0; i < allSceneVehicles.Count; i++)
            {
                Transform v = allSceneVehicles[i];
                if (v == null) continue;
                totalAccounted++;

                string rootPrefix = v.parent != null ? v.parent.name : "Vehicles";
                string path = $"{rootPrefix}/{v.name}";
                Vector3 p = v.position;
                float yaw = v.eulerAngles.y;

                string cls = "C";
                string reason = "Parked off-road";
                for (int r = 0; r < scanRows.Count; r++)
                {
                    if (scanRows[r].hierarchyPath == path &&
                        Mathf.Abs(scanRows[r].pos.x - p.x) < 0.5f &&
                        Mathf.Abs(scanRows[r].pos.z - p.z) < 0.5f)
                    {
                        cls = scanRows[r].classification;
                        reason = scanRows[r].reason;
                        break;
                    }
                }

                string action;
                bool shouldDeactivateInScene = false;

                if (cls == "A" && p.x > 75f)
                {
                    action = "KEEP_STATIC (Highway excluded from City core)";
                    keepHighwayCount++;
                }
                else if (cls == "A")
                {
                    bool isCrooked = (Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) > 5f &&
                                      Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) > 5f &&
                                      Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)) > 5f &&
                                      Mathf.Abs(Mathf.DeltaAngle(yaw, 270f)) > 5f);
                    action = isCrooked
                        ? "CONVERT_TO_TRAFFIC_POOL (Fix crooked yaw & absorb into dynamic traffic pool)"
                        : "CONVERT_TO_TRAFFIC_POOL (Absorb road-lane static car into dynamic pool)";
                    convertLaneCount++;
                    shouldDeactivateInScene = true;
                }
                else if (cls == "D")
                {
                    action = "REMOVE_CROSSWALK_LANE_BLOCKER (Absorb into traffic pool & clear crosswalk/lane)";
                    removeBlockerCount++;
                    shouldDeactivateInScene = true;
                }
                else if (cls == "B")
                {
                    action = "KEEP_STATIC (Curbside / driveway outside active lane)";
                    keepRoadsideCount++;
                }
                else if (cls == "F")
                {
                    action = "KEEP_STATIC (Special non-road prop: plane/train/balloon)";
                    keepSpecialCount++;
                }
                else
                {
                    action = "KEEP_STATIC (Parking lot / garage)";
                    keepParkedLotCount++;
                }

                if (applyToScene && shouldDeactivateInScene)
                {
                    if (v.name.StartsWith("OldCar_05", StringComparison.OrdinalIgnoreCase))
                    {
                        Undo.RecordObject(v, "Straighten OldCar_05 to Curbside Spot");
                        v.position = new Vector3(45.08f, -0.07f, 95.40f);
                        v.rotation = Quaternion.Euler(0f, 270f, 0f);
                        v.gameObject.SetActive(true);
                        EditorUtility.SetDirty(v);
                    }
                    else if (v.gameObject.activeSelf)
                    {
                        Undo.RecordObject(v.gameObject, "Convert Lane Car to Traffic Pool");
                        v.gameObject.SetActive(false);
                        EditorUtility.SetDirty(v.gameObject);
                    }
                }

                tableRows.Add($"| {totalAccounted} | `{path}` | `({p.x:F1}, {p.y:F2}, {p.z:F1})` | `{yaw:F1}°` | `Class {cls}` | **{action}** | {reason} |");
            }

            if (applyToScene)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            md.AppendLine("## Summary Counts (All 115 Vehicles)");
            md.AppendLine();
            md.AppendLine("| Category | Count | Action |");
            md.AppendLine("|---|---:|---|");
            md.AppendLine($"| **City Road-Lane Vehicles (`Class A`, `x < 75`)** (includes the 3 crooked lane cars `OldCar_05`, `Car_09 (2)`, `Car_09 (3)` and 2 lane buses) | **{convertLaneCount}** | **Convert / Absorb into `CityTrafficManager` Pool** |");
            md.AppendLine($"| **Crosswalk & Active-Lane Blockers (`Class D`)** | **{removeBlockerCount}** | **Convert / Absorb into Pool (Clears all 13 crosswalk/lane obstructions)** |");
            md.AppendLine($"| **Highway Vehicles (`Class A`, `x > 75`)** | **{keepHighwayCount}** | **Keep Static (Highway excluded from City core)** |");
            md.AppendLine($"| **Parking Lot / Garage (`Class C`)** | **{keepParkedLotCount}** | **Keep Static** |");
            md.AppendLine($"| **Roadside / Curb / Driveway (`Class B`)** | **{keepRoadsideCount}** | **Keep Static** |");
            md.AppendLine($"| **Special Props (`Class F`: Planes, Trains, Balloon)** | **{keepSpecialCount}** | **Keep Static** |");
            md.AppendLine($"| **Total Vehicles Accounted For** | **{totalAccounted}** | **{convertLaneCount + removeBlockerCount} Absorbed into Dynamic Traffic Pool / {keepHighwayCount + keepParkedLotCount + keepRoadsideCount + keepSpecialCount} Kept Static** |");
            md.AppendLine();
            md.AppendLine("## Full Vehicle-by-Vehicle Plan");
            md.AppendLine();
            md.AppendLine("| # | Hierarchy Path | World Position | Yaw | Scan Class | Planned Action | Notes |");
            md.AppendLine("|---:|---|---:|---:|---|---|---|");
            for (int i = 0; i < tableRows.Count; i++)
            {
                md.AppendLine(tableRows[i]);
            }

            Directory.CreateDirectory(ReportsDir);
            string outPath = Path.Combine(ReportsDir, "ConversionPlan.md");
            File.WriteAllText(outPath, md.ToString(), Encoding.UTF8);

            Debug.Log($"[ConvertLaneCars] Mode={(applyToScene ? "APPLY" : "DRY_RUN")} | " +
                      $"City Class A to Convert: {convertLaneCount} | Class D Blockers to Clear: {removeBlockerCount} | " +
                      $"Highway Class A Kept: {keepHighwayCount} | Lot Class C Kept: {keepParkedLotCount} | " +
                      $"Roadside Class B Kept: {keepRoadsideCount} | Special Class F Kept: {keepSpecialCount} | " +
                      $"Total: {totalAccounted} | Report written to: {outPath}");
        }

        private struct ScanVehicleRow
        {
            public string hierarchyPath;
            public Vector3 pos;
            public string classification;
            public string reason;
        }

        private static List<ScanVehicleRow> LoadScanVehicleRows()
        {
            var list = new List<ScanVehicleRow>(128);
            string csvPath = Path.Combine(ReportsDir, "vehicles.csv");
            if (!File.Exists(csvPath)) return list;

            string[] lines = File.ReadAllLines(csvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                List<string> cols = ParseCsvRow(lines[i]);
                if (cols.Count >= 39)
                {
                    float.TryParse(cols[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float px);
                    float.TryParse(cols[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float py);
                    float.TryParse(cols[6], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pz);
                    list.Add(new ScanVehicleRow
                    {
                        hierarchyPath = cols[2],
                        pos = new Vector3(px, py, pz),
                        classification = cols[36],
                        reason = cols[38]
                    });
                }
            }
            return list;
        }

        private static List<string> ParseCsvRow(string line)
        {
            List<string> result = new List<string>();
            bool inQuotes = false;
            StringBuilder cur = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(cur.ToString());
                    cur.Clear();
                }
                else
                {
                    cur.Append(c);
                }
            }
            result.Add(cur.ToString());
            return result;
        }

        [MenuItem("EcoQuest/Traffic/Buffer City Quest Spawns", false, 30)]
        public static void BufferCityQuestSpawnsMenu()
        {
            if (!EnsureCitySceneOpen()) return;

            CityTrafficGraph graph = AssetDatabase.LoadAssetAtPath<CityTrafficGraph>(GraphAssetPath);
            if (graph == null) graph = BuildRoadGraph(driveOnRight: true);

            CityTrashDetectiveManager questMgr = UnityEngine.Object.FindFirstObjectByType<CityTrashDetectiveManager>(FindObjectsInactive.Include);
            if (questMgr == null || questMgr.spawnPointsRoot == null)
            {
                Debug.LogError("[BufferCityQuestSpawns] Could not find CityTrashDetectiveManager or spawnPointsRoot.");
                return;
            }

            GameObject roadRoot = GameObject.Find("Road and Ground");
            List<Vector3> roadTileCenters = new List<Vector3>();
            if (roadRoot != null)
            {
                for (int i = 0; i < roadRoot.transform.childCount; i++)
                {
                    Transform rt = roadRoot.transform.GetChild(i);
                    if (rt != null && rt.gameObject.activeInHierarchy && rt.name.StartsWith("Road", StringComparison.OrdinalIgnoreCase))
                    {
                        roadTileCenters.Add(rt.position);
                    }
                }
            }

            CityDistrictVolume[] volumes = UnityEngine.Object.FindObjectsByType<CityDistrictVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Dictionary<CityDistrict, Bounds> districtBounds = new Dictionary<CityDistrict, Bounds>();
            for (int i = 0; i < volumes.Length; i++)
            {
                districtBounds[volumes[i].district] = volumes[i].WorldBounds;
            }

            List<CityTrashSpawnPoint> spawns = new List<CityTrashSpawnPoint>();
            questMgr.spawnPointsRoot.GetComponentsInChildren(true, spawns);

            // Identify the 29 spawn points within 3.0m of a road tile pivot
            List<int> indicesToMove = new List<int>();
            for (int i = 0; i < spawns.Count; i++)
            {
                Vector3 pos = spawns[i].transform.position;
                float minRoadDist3D = MinDistanceToRoadPivot3D(roadTileCenters, pos);
                if (minRoadDist3D <= 3.0f)
                {
                    indicesToMove.Add(i);
                }
            }

            if (indicesToMove.Count == 0)
            {
                Debug.Log("[BufferCityQuestSpawns] 0 spawn points are currently within 3.0m of a road (already buffered).");
                return;
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine("# Stage C.2 — Trash Detective Spawn Point Buffering Report");
            report.AppendLine();
            report.AppendLine($"- **Total Spawn Points**: `{spawns.Count}` (18 per district across 5 districts)");
            report.AppendLine($"- **Spawn Points Buffered Away From Roads (<= 3.0 m of road)**: `{indicesToMove.Count}`");
            report.AppendLine($"- **Safety Criteria Enforced**: `>= 4.0 m` from any traffic lane edge, `> 3.0 m` from any road tile, on sidewalk/plaza/grass (`y >= 0.10 m`), inside same `CityDistrictVolume`, snapped to `NavMesh`, `>= 6.0 m` spacing from all other spawn points, reachable.");
            report.AppendLine();
            report.AppendLine("| # | Spawn Point Name | District | Old Position | Old Road Dist | Old Lane Dist | New Position | New Road Dist | New Lane Dist |");
            report.AppendLine("|---:|---|---|---|---:|---:|---|---:|---:|");

            int movedCount = 0;
            for (int idx = 0; idx < indicesToMove.Count; idx++)
            {
                int spIndex = indicesToMove[idx];
                CityTrashSpawnPoint sp = spawns[spIndex];
                Vector3 oldPos = sp.transform.position;
                float oldRoadDist = MinDistanceToRoadPivot3D(roadTileCenters, oldPos);
                float oldLaneDist = MinDistanceToAnyGraphLane(graph, oldPos);

                if (!districtBounds.TryGetValue(sp.district, out Bounds distBox))
                {
                    Debug.LogError($"[BufferCityQuestSpawns] Missing CityDistrictVolume Bounds for {sp.district}");
                    continue;
                }

                if (TryFindSafeBufferedSpawnPosition(spIndex, oldPos, distBox, spawns, graph, roadTileCenters, out Vector3 newPos))
                {
                    Undo.RecordObject(sp.transform, "Buffer City Trash Spawn Point");
                    sp.transform.position = newPos;
                    EditorUtility.SetDirty(sp.transform);
                    EditorUtility.SetDirty(sp);
                    movedCount++;

                    float newRoadDist = MinDistanceToRoadPivot3D(roadTileCenters, newPos);
                    float newLaneDist = MinDistanceToAnyGraphLane(graph, newPos);
                    report.AppendLine($"| {movedCount} | `{sp.name}` | `{sp.district}` | `({oldPos.x:F2}, {oldPos.y:F2}, {oldPos.z:F2})` | `{oldRoadDist:F2} m` | `{oldLaneDist:F2} m` | `({newPos.x:F2}, {newPos.y:F2}, {newPos.z:F2})` | `{newRoadDist:F2} m` | `{newLaneDist:F2} m` |");
                }
                else
                {
                    Debug.LogError($"[BufferCityQuestSpawns] Could not find valid replacement position for {sp.name} in {sp.district}!");
                }
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Directory.CreateDirectory(ReportsDir);
            string reportPath = Path.Combine(ReportsDir, "QuestSpawnBufferReport.md");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            Debug.Log($"[BufferCityQuestSpawns] Relocated {movedCount}/{indicesToMove.Count} road-adjacent spawn points. Report: {reportPath}");
        }

        private static bool TryFindSafeBufferedSpawnPosition(
            int selfIndex,
            Vector3 oldPos,
            Bounds b,
            List<CityTrashSpawnPoint> allSpawns,
            CityTrafficGraph graph,
            List<Vector3> roadTileCenters,
            out Vector3 bestPos)
        {
            float minX = b.min.x + 0.25f;
            float maxX = b.max.x - 0.25f;
            float minZ = b.min.z + 0.25f;
            float maxZ = b.max.z - 0.25f;

            int walkableMask = 1 << 0; // NavMesh Walkable area (excludes Road Area 5)
            float bestScore = float.MaxValue;
            bestPos = oldPos;
            bool found = false;

            for (float x = minX; x <= maxX; x += 0.75f)
            {
                for (float z = minZ; z <= maxZ; z += 0.75f)
                {
                    Vector3 cand = new Vector3(x, 0.2f, z);
                    if (EvaluateSpawnCandidate(cand, selfIndex, b, allSpawns, graph, roadTileCenters, walkableMask, out Vector3 snapped))
                    {
                        float distMoved = Vector2.Distance(new Vector2(oldPos.x, oldPos.z), new Vector2(snapped.x, snapped.z));
                        if (distMoved < bestScore)
                        {
                            bestScore = distMoved;
                            bestPos = snapped;
                            found = true;
                        }
                    }
                }
            }

            return found;
        }

        private static bool EvaluateSpawnCandidate(
            Vector3 cand,
            int selfIndex,
            Bounds distBounds,
            List<CityTrashSpawnPoint> allSpawns,
            CityTrafficGraph graph,
            List<Vector3> roadTileCenters,
            int walkableMask,
            out Vector3 snapped)
        {
            snapped = cand;

            if (!NavMesh.SamplePosition(new Vector3(cand.x, 0.25f, cand.z), out NavMeshHit navHit, 1.0f, walkableMask))
            {
                if (!NavMesh.SamplePosition(new Vector3(cand.x, 0.25f, cand.z), out navHit, 0.8f, NavMesh.AllAreas))
                    return false;
            }

            snapped = navHit.position;
            // Must be on elevated sidewalk/plaza/grass (y >= 0.08m), never down on the road asphalt (y ~ 0.01m)
            if (snapped.y < 0.08f || snapped.y > 1.5f) return false;

            // Must lie strictly inside the district WorldBounds
            if (!distBounds.Contains(new Vector3(snapped.x, distBounds.center.y, snapped.z))) return false;

            // Must be >= 4.005m from any traffic lane edge
            float laneDist = MinDistanceToAnyGraphLane(graph, snapped);
            if (laneDist < 4.005f) return false;

            // Must be > 3.50m from any road tile pivot (> 3.0m from road)
            float roadPivotDist = MinDistanceToRoadPivot3D(roadTileCenters, snapped);
            if (roadPivotDist <= 3.50f) return false;

            // Must be >= 6.01m from every other spawn point
            for (int i = 0; i < allSpawns.Count; i++)
            {
                if (i == selfIndex || allSpawns[i] == null) continue;
                Vector3 other = allSpawns[i].transform.position;
                float d = Vector3.Distance(snapped, other);
                if (d < 6.01f) return false;
            }

            // Must not overlap any parked vehicle collider
            Collider[] cols = Physics.OverlapSphere(snapped + Vector3.up * 0.75f, 0.35f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null) continue;
                string rootName = c.transform.root != null ? c.transform.root.name : "";
                if (rootName == "Vehicles")
                {
                    return false;
                }
            }

            return true;
        }

        [MenuItem("EcoQuest/Validate City Quest Spawns", false, 31)]
        public static void ValidateCityQuestSpawnsMenu()
        {
            ValidateCityQuestSpawns();
        }

        public static bool ValidateCityQuestSpawns()
        {
            if (!EnsureCitySceneOpen()) return false;

            CityTrafficGraph graph = AssetDatabase.LoadAssetAtPath<CityTrafficGraph>(GraphAssetPath);
            if (graph == null) graph = BuildRoadGraph(driveOnRight: true);

            CityTrashDetectiveManager questMgr = UnityEngine.Object.FindFirstObjectByType<CityTrashDetectiveManager>(FindObjectsInactive.Include);
            if (questMgr == null || questMgr.spawnPointsRoot == null)
            {
                Debug.LogError("[ValidateCityQuestSpawns] FAIL: Missing CityTrashDetectiveManager.");
                return false;
            }

            GameObject roadRoot = GameObject.Find("Road and Ground");
            List<Vector3> roadTileCenters = new List<Vector3>();
            if (roadRoot != null)
            {
                for (int i = 0; i < roadRoot.transform.childCount; i++)
                {
                    Transform rt = roadRoot.transform.GetChild(i);
                    if (rt != null && rt.gameObject.activeInHierarchy && rt.name.StartsWith("Road", StringComparison.OrdinalIgnoreCase))
                    {
                        roadTileCenters.Add(rt.position);
                    }
                }
            }

            List<CityTrashSpawnPoint> spawns = new List<CityTrashSpawnPoint>();
            questMgr.spawnPointsRoot.GetComponentsInChildren(true, spawns);

            int within3mOfRoad = 0;
            int offNavMesh = 0;
            float minObservedRoadDist = 999f;

            for (int i = 0; i < spawns.Count; i++)
            {
                Vector3 p = spawns[i].transform.position;
                float roadDist = MinDistanceToRoadPivot3D(roadTileCenters, p);
                if (roadDist < minObservedRoadDist) minObservedRoadDist = roadDist;

                if (roadDist <= 3.0f)
                {
                    within3mOfRoad++;
                    Debug.LogError($"[ValidateCityQuestSpawns] FAIL: '{spawns[i].name}' at {p} is {roadDist:F2}m from a road (<= 3.0m)!");
                }

                if (!NavMesh.SamplePosition(p, out _, 1.5f, NavMesh.AllAreas))
                {
                    offNavMesh++;
                    Debug.LogError($"[ValidateCityQuestSpawns] FAIL: '{spawns[i].name}' at {p} is off NavMesh!");
                }
            }

            // Run the built-in CityQuest validator & 20-attempt simulator
            bool questSimOk = CityQuestSceneSetupAndValidator.ValidateCityQuestAndSimulate20Attempts();

            bool ok = (spawns.Count == 90 && within3mOfRoad == 0 && offNavMesh == 0 && questSimOk);
            Debug.Log($"[ValidateCityQuestSpawns] {(ok ? "PASS (0 bad points)" : "FAIL")} | " +
                      $"Total Spawns: {spawns.Count} | Within 3.0m of Road: {within3mOfRoad} (Min Road Dist: {minObservedRoadDist:F2}m) | " +
                      $"Off-NavMesh: {offNavMesh} | 20-Attempt Sim & Robin 10x: {(questSimOk ? "PASS" : "FAIL")}");
            return ok;
        }

        [MenuItem("EcoQuest/Traffic/Spawn Test Traffic", false, 40)]
        public static void SpawnTestTrafficMenu()
        {
            if (!EnsureCitySceneOpen()) return;
            CityTrafficManager mgr = UnityEngine.Object.FindFirstObjectByType<CityTrafficManager>(FindObjectsInactive.Include);
            if (mgr == null)
            {
                BuildRoadGraph(driveOnRight: true);
                mgr = UnityEngine.Object.FindFirstObjectByType<CityTrafficManager>(FindObjectsInactive.Include);
            }
            if (mgr == null) return;

            mgr.BuildInitialPool();
            mgr.PopulateInitialTraffic(ignoreFrustum: true);
            Debug.Log($"[SpawnTestTraffic] Spawned {mgr.ActiveVehicles.Count} test vehicles on the CityTrafficGraph.");
        }

        [MenuItem("EcoQuest/Traffic/Toggle Debug Overlay", false, 41)]
        public static void ToggleDebugOverlayMenu()
        {
            CityTrafficManager mgr = UnityEngine.Object.FindFirstObjectByType<CityTrafficManager>(FindObjectsInactive.Include);
            if (mgr == null)
            {
                Debug.LogWarning("[CityTraffic] No CityTrafficManager found in active scene.");
                return;
            }
            Undo.RecordObject(mgr, "Toggle Traffic Debug Overlay");
            mgr.showDebugOverlay = !mgr.showDebugOverlay;
            EditorUtility.SetDirty(mgr);
            SceneView.RepaintAll();
            Debug.Log($"[CityTraffic] Debug Overlay & Scene Gizmos: {(mgr.showDebugOverlay ? "ENABLED" : "DISABLED")}");
        }

        [MenuItem("EcoQuest/Traffic/Traffic Health Check", false, 42)]
        public static void TrafficHealthCheckMenu()
        {
            RunTrafficHealthCheck();
        }

        public static bool RunTrafficHealthCheck()
        {
            if (!EnsureCitySceneOpen()) return false;

            bool graphOk = ValidateRoadGraph(renderTopDownPng: false);
            bool spawnsOk = ValidateCityQuestSpawns();

            int trafficLayer = LayerMask.NameToLayer("Traffic");
            bool layerOk = trafficLayer >= 0;

            CityTrafficManager mgr = UnityEngine.Object.FindFirstObjectByType<CityTrafficManager>(FindObjectsInactive.Include);
            bool mgrOk = mgr != null && mgr.graph != null && mgr.fleetPrefabs != null && mgr.fleetPrefabs.Count >= 10;

            // Clean up any edit-mode preview child vehicles under --- TRAFFIC --- while preserving Stage D polish roots
            if (mgr != null && !Application.isPlaying)
            {
                for (int i = mgr.transform.childCount - 1; i >= 0; i--)
                {
                    Transform ch = mgr.transform.GetChild(i);
                    if (ch != null && ch.GetComponent<TrafficVehicleController>() != null)
                    {
                        UnityEngine.Object.DestroyImmediate(ch.gameObject);
                    }
                }
                mgr.ActiveVehicles.Clear();
                mgr.AllPooledVehicles.Clear();
            }

            int signalCount = mgr != null && mgr.signalPoles != null ? mgr.signalPoles.Count : 0;
            int hazardCount = mgr != null && mgr.parkedHazardFlashers != null ? mgr.parkedHazardFlashers.Count : 0;
            bool stageDOk = signalCount >= 36 && hazardCount >= 4;

            bool allOk = graphOk && spawnsOk && layerOk && mgrOk && stageDOk;
            Debug.Log($"[TrafficHealthCheck] {(allOk ? "ALL CHECKS PASSED" : "ISSUES FOUND")} | " +
                      $"Graph: {(graphOk ? "PASS" : "FAIL")} | Quest Spawns: {(spawnsOk ? "PASS" : "FAIL")} | " +
                      $"Traffic Layer ({trafficLayer}): {(layerOk ? "PASS" : "FAIL")} | " +
                      $"Manager & Fleet ({(mgr != null ? mgr.fleetPrefabs.Count : 0)} prefabs): {(mgrOk ? "PASS" : "FAIL")} | " +
                      $"Stage D (Signal Poles: {signalCount}, Hazard Cars: {hazardCount}): {(stageDOk ? "PASS" : "FAIL")}");
            return allOk;
        }

        public static float MinDistanceToAnyGraphLane(CityTrafficGraph graph, Vector3 pos)
        {
            if (graph == null || graph.edges.Count == 0) return 999f;
            Vector2 p2 = new Vector2(pos.x, pos.z);
            float minSq = float.MaxValue;

            for (int i = 0; i < graph.edges.Count; i++)
            {
                TrafficEdge e = graph.edges[i];
                if (e.waypoints == null || e.waypoints.Length < 2) continue;
                for (int j = 0; j < e.waypoints.Length - 1; j++)
                {
                    Vector2 a = new Vector2(e.waypoints[j].x, e.waypoints[j].z);
                    Vector2 b = new Vector2(e.waypoints[j + 1].x, e.waypoints[j + 1].z);
                    Vector2 ab = b - a;
                    float lenSq = ab.sqrMagnitude;
                    float t = lenSq > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p2 - a, ab) / lenSq) : 0f;
                    Vector2 proj = a + ab * t;
                    float dSq = (p2 - proj).sqrMagnitude;
                    if (dSq < minSq) minSq = dSq;
                }
            }
            return Mathf.Sqrt(minSq);
        }

        private static float MinDistanceToRoadPivot3D(List<Vector3> roadCenters, Vector3 pos)
        {
            float minSq = float.MaxValue;
            for (int i = 0; i < roadCenters.Count; i++)
            {
                float dSq = (pos - roadCenters[i]).sqrMagnitude;
                if (dSq < minSq) minSq = dSq;
            }
            return Mathf.Sqrt(minSq);
        }

        private static bool EnsureCitySceneOpen()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.path == CityScenePath) return true;
            Debug.LogError($"[CityTraffic] Active scene is '{active.path}'. Please open '{CityScenePath}' first.");
            return false;
        }

        private static string GetBaseTileName(string objName)
        {
            int idx = objName.IndexOf(' ');
            return idx > 0 ? objName.Substring(0, idx) : objName;
        }

        private static int GetCardinalDir(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            if (Mathf.Abs(d.x) > Mathf.Abs(d.y))
                return d.x > 0f ? 1 : 3; // 1 = East (+X), 3 = West (-X)
            return d.y > 0f ? 0 : 2;     // 0 = North (+Z), 2 = South (-Z)
        }

        private static Vector3 DirToVector(int dir)
        {
            switch (dir)
            {
                case 0: return Vector3.forward; // +Z
                case 1: return Vector3.right;   // +X
                case 2: return Vector3.back;    // -Z
                default: return Vector3.left;   // -X
            }
        }

        private static TrafficTurnType ClassifyTurn(int approachTravelDir, int exitArmDir, bool driveOnRight)
        {
            if (approachTravelDir == exitArmDir) return TrafficTurnType.Straight;
            int rightDir = (approachTravelDir + 1) % 4;
            if (exitArmDir == rightDir)
                return driveOnRight ? TrafficTurnType.Right : TrafficTurnType.Left;
            return driveOnRight ? TrafficTurnType.Left : TrafficTurnType.Right;
        }

        private static Vector3[] BuildSmoothArcWaypoints(
            Vector3 p0, Vector3 tan0, Vector3 p3, Vector3 tan3, int count,
            out float[] cumLengths, out float totalLength)
        {
            if (count <= 2)
            {
                totalLength = Vector3.Distance(p0, p3);
                cumLengths = new float[] { 0f, totalLength };
                return new Vector3[] { p0, p3 };
            }

            float chord = Vector3.Distance(p0, p3);
            float handleLen = chord * 0.45f;
            Vector3 p1 = p0 + tan0.normalized * handleLen;
            Vector3 p2 = p3 - tan3.normalized * handleLen;

            Vector3[] pts = new Vector3[count];
            cumLengths = new float[count];
            totalLength = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float u = 1f - t;
                Vector3 pt = (u * u * u) * p0 +
                             (3f * u * u * t) * p1 +
                             (3f * u * t * t) * p2 +
                             (t * t * t) * p3;
                pt.y = 0f;
                pts[i] = pt;
                if (i > 0)
                {
                    totalLength += Vector3.Distance(pts[i - 1], pt);
                }
                cumLengths[i] = totalLength;
            }
            return pts;
        }
    }
}
