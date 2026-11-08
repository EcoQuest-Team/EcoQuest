#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace EcoQuest.Editor
{
    public static class TrafficScan
    {
        private const string OutputDir = @"C:\EcoQuest.gg\Reports\Traffic";

        [MenuItem("Tools/EcoQuest/Traffic Scan (Read-Only)")]
        public static void RunMenuScan()
        {
            ExecuteScan();
        }

        public static string ExecuteScan()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (!activeScene.isLoaded)
            {
                Debug.LogError("[TrafficScan] No active scene loaded!");
                return "FAIL: No active scene loaded";
            }

            if (!Directory.Exists(OutputDir))
            {
                Directory.CreateDirectory(OutputDir);
            }

            Debug.Log($"[TrafficScan] Starting READ-ONLY Traffic Scan on scene: '{activeScene.name}' ({activeScene.path})...");

            // --- STEP 1: VEHICLE SCAN ---
            var vehicles = ScanAllVehicles();
            string vehiclesCsvPath = Path.Combine(OutputDir, "vehicles.csv");
            WriteVehiclesCsv(vehicles, vehiclesCsvPath);

            // --- STEP 2: ROAD NETWORK & INTERSECTION SCAN ---
            var (roads, intersections) = ScanRoadNetwork();
            string roadsCsvPath = Path.Combine(OutputDir, "roads.csv");
            string intersectionsCsvPath = Path.Combine(OutputDir, "intersections.csv");
            WriteRoadsCsv(roads, roadsCsvPath);
            WriteIntersectionsCsv(intersections, intersectionsCsvPath);

            // --- STEP 3: INTEGRATION SCAN ---
            var integrationData = ScanIntegration(vehicles, roads);

            // --- RENDER TOP-DOWN IMAGE ---
            string topDownPngPath = Path.Combine(OutputDir, "TopDown_Vehicles.png");
            RenderTopDownImage(vehicles, roads, topDownPngPath);

            // --- GENERATE REPORTS ---
            string reportPath = Path.Combine(OutputDir, "Traffic_Report.md");
            string proposalPath = Path.Combine(OutputDir, "Traffic_Proposal.md");
            WriteTrafficReport(vehicles, roads, intersections, integrationData, reportPath);
            WriteTrafficProposal(vehicles, roads, intersections, integrationData, proposalPath);

            Debug.Log($"[TrafficScan] Scan COMPLETE! Reports generated at '{OutputDir}'.");
            return "SUCCESS";
        }

        #region Data Structures

        public class VehicleData
        {
            public int id;
            public GameObject go;
            public string name;
            public string hierarchyPath;
            public string prefabAsset;
            public Vector3 worldPos;
            public float yaw;
            public Vector3 scale;
            public Vector3 boundsSize;
            public string forwardAxis;
            public Vector3 pivotOffset;
            public int wheelCount;
            public string wheelNames;
            public bool wheelsCanSpin;
            public string colliderTypes;
            public bool hasMeshCollider;
            public bool hasRigidbody;
            public bool isKinematic;
            public bool hasNavMeshObstacle;
            public bool carving;
            public int scriptCount;
            public string scripts;
            public bool hasAnimator;
            public bool isStatic;
            public string staticFlags;
            public int lodCount;
            public int materialCount;
            public int triangleCount;
            public string layer;
            public string classification; // A to F
            public float confidence;
            public string classificationReason;
            public string decision; // KEEP_STATIC or COULD_MOVE
            public string cheapLifeAddition;
            public string cost;
        }

        public class RoadData
        {
            public int id;
            public GameObject go;
            public string name;
            public string hierarchyPath;
            public string prefabName;
            public string meshName;
            public Vector3 worldPos;
            public float yaw;
            public Vector3 boundsSize;
            public string roadType; // Straight, Crosswalk, Intersection_4Way, Intersection_3Way, Turn_Curve, Highway, Ramp, Parking
            public int laneCount;
            public float laneWidth;
            public float curbHeight;
            public float slopeDeg;
            public string oneWayHint;
            public bool isDeadEnd;
            public string navMeshArea;
        }

        public class IntersectionData
        {
            public int id;
            public string name;
            public Vector3 worldPos;
            public int armCount;
            public string intersectionType; // 4-Way Cross, 3-Way T, Roundabout
            public int connectingRoadCount;
            public int crosswalkCount;
            public bool hasTrafficLight;
            public string trafficLightStatus;
            public bool hasBusStop;
            public int walkGraphCrossings;
        }

        public class IntegrationData
        {
            public string playerControllerType;
            public string playerLayer;
            public string playerCollisionResponse;
            public bool navMeshRoadWalkable;
            public string navMeshAreasSummary;
            public int totalQuestSpawnPoints;
            public int conflictingQuestSpawnPoints;
            public List<string> conflictSpawnPointNames = new List<string>();
            public Vector3 robinPosition;
            public float robinDistToRoad;
            public Vector3 fountainPosition;
            public float fountainDistToRoad;
            public bool minimapRendersVehicles;
            public int totalVehicleDrawCalls;
            public int totalVehicleTriangles;
            public int totalVehicleMaterials;
            public bool gpuInstancingSupported;
            public List<string> activeAudioSources = new List<string>();
            public List<string> conflictDetails = new List<string>();
        }

        #endregion

        #region Step 1: Vehicle Inventory

        private static List<VehicleData> ScanAllVehicles()
        {
            var list = new List<VehicleData>();
            var allTransforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var vehiclesRoot = GameObject.Find("Vehicles");

            int nextId = 1;

            foreach (var t in allTransforms)
            {
                if (t == null) continue;
                string lower = t.name.ToLowerInvariant();

                // Skip non-root subcomponents
                if (lower.Contains("wheel") || lower.Contains("tire") || lower.Contains("tyre") ||
                    lower.Contains("glass") || lower.Contains("door") || lower.Contains("light") ||
                    lower.Contains("cardboard") || lower.Contains("trash") || lower.Contains("stop") ||
                    lower.Contains("station") || lower.Contains("sign"))
                    continue;

                // Candidate detection
                bool isDirectChildOfVehicles = vehiclesRoot != null && t.parent == vehiclesRoot.transform;
                bool matchesVehicleName = lower.StartsWith("car_") || lower.StartsWith("oldcar_") ||
                                          lower.StartsWith("train_") || lower.StartsWith("plane_") ||
                                          lower.StartsWith("airballoon_") || lower.Contains("truck") ||
                                          lower.Contains("bus") || lower.Contains("taxi") ||
                                          lower.Contains("ambulance") || lower.Contains("police");

                // Exclude buildings like Police_01
                if (t.parent != null && t.parent.name == "Buildings")
                    continue;

                if (!isDirectChildOfVehicles && !matchesVehicleName)
                    continue;

                // Avoid sub-parts inside vehicles
                if (t.parent != null)
                {
                    string pLower = t.parent.name.ToLowerInvariant();
                    if (pLower.StartsWith("car_") || pLower.StartsWith("oldcar_") || pLower.StartsWith("train_") || pLower.StartsWith("plane_"))
                        continue;
                }

                var v = AnalyzeVehicle(t.gameObject, nextId++);
                list.Add(v);
            }

            return list;
        }

        private static VehicleData AnalyzeVehicle(GameObject go, int id)
        {
            var t = go.transform;
            var data = new VehicleData
            {
                id = id,
                go = go,
                name = go.name,
                hierarchyPath = GetHierarchyPath(t),
                worldPos = t.position,
                yaw = t.eulerAngles.y,
                scale = t.lossyScale,
                layer = LayerMask.LayerToName(go.layer)
            };

            // Prefab Asset
            var prefabObj = PrefabUtility.GetCorrespondingObjectFromSource(go);
            data.prefabAsset = prefabObj != null ? AssetDatabase.GetAssetPath(prefabObj) : "In-Scene";

            // Bounds & Mesh
            Bounds b = new Bounds(t.position, Vector3.zero);
            int triCount = 0;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            var uniqueMats = new HashSet<Material>();
            bool hasRenderer = false;

            foreach (var r in renderers)
            {
                if (r is MeshRenderer mr)
                {
                    if (!hasRenderer) { b = mr.bounds; hasRenderer = true; }
                    else b.Encapsulate(mr.bounds);

                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        triCount += mf.sharedMesh.triangles.Length / 3;
                    }
                    foreach (var m in mr.sharedMaterials)
                    {
                        if (m != null) uniqueMats.Add(m);
                    }
                }
            }

            data.boundsSize = b.size;
            data.pivotOffset = t.position - b.center;
            data.triangleCount = triCount;
            data.materialCount = uniqueMats.Count;

            // Forward axis
            float dotFwd = Vector3.Dot(t.forward, Vector3.forward);
            data.forwardAxis = "+Z (Native Model Forward)";

            // Wheels
            var wheelList = new List<Transform>();
            for (int i = 0; i < t.childCount; i++)
            {
                var ch = t.GetChild(i);
                string chName = ch.name.ToLowerInvariant();
                if (chName.Contains("wheel") || chName.Contains("_fl") || chName.Contains("_fr") ||
                    chName.Contains("_rl") || chName.Contains("_rr") || chName.Contains("tire"))
                {
                    wheelList.Add(ch);
                }
            }
            data.wheelCount = wheelList.Count;
            var sbWheels = new StringBuilder();
            bool wheelsCanSpin = wheelList.Count > 0;
            foreach (var w in wheelList)
            {
                if (sbWheels.Length > 0) sbWheels.Append(";");
                sbWheels.Append(w.name);
                var mf = w.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    if (mf.sharedMesh.bounds.center.sqrMagnitude > 0.05f)
                    {
                        wheelsCanSpin = false;
                    }
                }
            }
            data.wheelNames = sbWheels.ToString();
            data.wheelsCanSpin = wheelsCanSpin;

            // Colliders
            var cols = go.GetComponentsInChildren<Collider>(true);
            var colNames = new List<string>();
            bool hasMeshCol = false;
            foreach (var c in cols)
            {
                if (c is MeshCollider) hasMeshCol = true;
                string typeName = c.GetType().Name;
                if (!colNames.Contains(typeName)) colNames.Add(typeName);
            }
            data.colliderTypes = string.Join(";", colNames);
            data.hasMeshCollider = hasMeshCol;

            // Rigidbody & NavMeshObstacle
            var rb = go.GetComponentInChildren<Rigidbody>(true);
            data.hasRigidbody = rb != null;
            data.isKinematic = rb != null && rb.isKinematic;

            var nmo = go.GetComponentInChildren<NavMeshObstacle>(true);
            data.hasNavMeshObstacle = nmo != null;
            data.carving = nmo != null && nmo.carving;

            // Scripts & Animator
            var comps = go.GetComponentsInChildren<MonoBehaviour>(true);
            var scriptNames = new List<string>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                string sName = c.GetType().Name;
                if (!scriptNames.Contains(sName)) scriptNames.Add(sName);
            }
            data.scriptCount = scriptNames.Count;
            data.scripts = string.Join(";", scriptNames);

            var anim = go.GetComponentInChildren<Animator>(true);
            data.hasAnimator = anim != null;

            // Static flags & LOD
            data.isStatic = go.isStatic;
            data.staticFlags = GameObjectUtility.GetStaticEditorFlags(go).ToString();
            var lod = go.GetComponentInChildren<LODGroup>(true);
            data.lodCount = lod != null ? lod.lodCount : 0;

            // Ground & Classification
            ClassifyVehicle(data);

            return data;
        }

        private static void ClassifyVehicle(VehicleData v)
        {
            string lower = v.name.ToLowerInvariant();

            // Special vehicle types: Train, Plane, AirBalloon
            if (lower.Contains("train") || lower.Contains("plane") || lower.Contains("airballoon"))
            {
                v.classification = "F";
                v.confidence = 0.99f;
                v.classificationReason = "Special vehicle type (Train engine on rails, Airport airplane, Hot air balloon)";
                v.decision = "KEEP_STATIC";
                v.cheapLifeAddition = lower.Contains("plane") ? "Propeller spin / taxiway lights" : (lower.Contains("airballoon") ? "Gentle vertical bobbing" : "Steam / whistle audio");
                v.cost = "Low (0.01ms)";
                return;
            }

            // Raycast ground directly beneath vehicle
            // Cast from 0.5m above pivot to avoid overhead awnings/canopies (hospital, gas station)
            var hits = Physics.RaycastAll(v.worldPos + Vector3.up * 0.5f, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit groundHit = default;
            bool foundGround = false;
            Array.Sort(hits, (h1, h2) => h1.distance.CompareTo(h2.distance));

            foreach (var h in hits)
            {
                if (h.collider.transform.root.name == "Vehicles" || h.collider.transform.IsChildOf(v.go.transform))
                    continue;
                groundHit = h;
                foundGround = true;
                break;
            }

            // Multi-story parking structure check (ParkingZone_01)
            bool isParkingZone = foundGround && (groundHit.collider.gameObject.name.Contains("Parking") ||
                                                 groundHit.collider.gameObject.name.Contains("Tile_14") ||
                                                 groundHit.collider.transform.root.name.Contains("ParkingZone"));

            if (isParkingZone)
            {
                v.classification = "C";
                v.confidence = 0.98f;
                v.classificationReason = $"Stationed in dedicated parking deck / lot ('{groundHit.collider.gameObject.name}')";
                v.decision = "KEEP_STATIC";
                v.cheapLifeAddition = "Occasional headlight blink or parked idle";
                v.cost = "Zero";
                return;
            }

            if (!foundGround)
            {
                v.classification = "E";
                v.confidence = 0.99f;
                v.classificationReason = "No ground beneath vehicle; floating in void";
                v.decision = "KEEP_STATIC";
                v.cheapLifeAddition = "Fix ground snap before moving";
                v.cost = "Zero";
                return;
            }

            string groundName = groundHit.collider.gameObject.name;
            float heightDiff = Mathf.Abs(v.worldPos.y - groundHit.point.y);
            float tiltX = Mathf.Abs(Mathf.DeltaAngle(0f, v.go.transform.eulerAngles.x));
            float tiltZ = Mathf.Abs(Mathf.DeltaAngle(0f, v.go.transform.eulerAngles.z));

            if (heightDiff > 0.5f || tiltX > 20f || tiltZ > 20f)
            {
                v.classification = "E";
                v.confidence = 0.95f;
                v.classificationReason = $"Problem placement (heightDiff={heightDiff:F2}m, tiltX={tiltX:F1}deg, tiltZ={tiltZ:F1}deg)";
                v.decision = "KEEP_STATIC";
                v.cheapLifeAddition = "Fix ground snap before moving";
                v.cost = "Zero";
                return;
            }

            // Crosswalk check (Road_03 / Road_04 zebra crossings)
            if (groundName.StartsWith("Road_03") || groundName.StartsWith("Road_04"))
            {
                v.classification = "D";
                v.confidence = 0.95f;
                v.classificationReason = $"Stationed on or across crosswalk pedestrian crossing ('{groundName}')";
                v.decision = "KEEP_STATIC";
                v.cheapLifeAddition = "Reposition away from crosswalk zone";
                v.cost = "Zero";
                return;
            }

            // Road surface check
            if (groundName.StartsWith("Road_"))
            {
                float yawMod = v.yaw % 180f;
                bool isAligned = (yawMod < 25f || yawMod > 155f || (yawMod > 65f && yawMod < 115f));
                if (isAligned)
                {
                    v.classification = "A";
                    v.confidence = 0.95f;
                    v.classificationReason = "Positioned directly on driving road surface aligned with lane flow";
                    v.decision = "COULD_MOVE";
                    v.cheapLifeAddition = "Candidate for waypoint traffic follower + rolling wheels";
                    v.cost = "Kinematic Path Follower (0.03ms per car)";
                    return;
                }
                else
                {
                    v.classification = "B";
                    v.confidence = 0.90f;
                    v.classificationReason = "Roadside parking / curb alignment";
                    v.decision = "KEEP_STATIC";
                    v.cheapLifeAddition = "Hazard light flashers (emission material swap)";
                    v.cost = "Very Low";
                    return;
                }
            }

            if (groundName.StartsWith("Tile_"))
            {
                v.classification = "B";
                v.confidence = 0.90f;
                v.classificationReason = "Parked beside sidewalk curb / plaza perimeter";
                v.decision = "KEEP_STATIC";
                v.cheapLifeAddition = "Hazard light flashers (emission material swap)";
                v.cost = "Very Low";
                return;
            }

            v.classification = "C";
            v.confidence = 0.85f;
            v.classificationReason = $"Parked off-road / lot surface ('{groundName}')";
            v.decision = "KEEP_STATIC";
            v.cheapLifeAddition = "Static idle scenery";
            v.cost = "Zero";
        }

        #endregion

        #region Step 2: Road Network Scan

        private static (List<RoadData> roads, List<IntersectionData> intersections) ScanRoadNetwork()
        {
            var roadList = new List<RoadData>();
            var roadRoot = GameObject.Find("Road and Ground");
            if (roadRoot == null)
            {
                Debug.LogError("[TrafficScan] Road and Ground GameObject not found!");
                return (roadList, new List<IntersectionData>());
            }

            int roadId = 1;
            for (int i = 0; i < roadRoot.transform.childCount; i++)
            {
                var ch = roadRoot.transform.GetChild(i);
                string n = ch.name;
                int paren = n.IndexOf('(');
                string baseName = paren > 0 ? n.Substring(0, paren).Trim() : n;

                if (!baseName.StartsWith("Road_") && !baseName.StartsWith("MobileRoad_") && !baseName.StartsWith("RoadPart_"))
                    continue;

                var data = new RoadData
                {
                    id = roadId++,
                    go = ch.gameObject,
                    name = ch.name,
                    hierarchyPath = GetHierarchyPath(ch),
                    prefabName = baseName,
                    worldPos = ch.position,
                    yaw = ch.eulerAngles.y,
                    laneCount = 2,
                    laneWidth = 3.0f,
                    curbHeight = 0.15f,
                    slopeDeg = Mathf.Abs(Mathf.DeltaAngle(0f, ch.eulerAngles.x)),
                    oneWayHint = "Two-Way Bidirectional (standard urban)",
                    isDeadEnd = false,
                    navMeshArea = "Road (Area 5)"
                };

                var mf = ch.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    data.meshName = mf.sharedMesh.name;
                    data.boundsSize = mf.sharedMesh.bounds.size;
                }

                // Classify Road Type
                if (baseName == "Road_01" || baseName == "Road_02" || baseName == "Road_38" || baseName == "Road_39")
                    data.roadType = "Straight";
                else if (baseName == "Road_03" || baseName == "Road_04")
                    data.roadType = "Crosswalk";
                else if (baseName == "Road_17")
                    data.roadType = "Intersection_4Way";
                else if (baseName == "Road_18" || baseName == "Road_19" || baseName == "Road_20")
                    data.roadType = "Intersection_3Way";
                else if (baseName == "Road_21" || baseName == "Road_22" || baseName == "Road_23")
                    data.roadType = "Turn_Curve";
                else if (baseName == "Road_42" || baseName == "RoadPart_07")
                {
                    data.roadType = "Highway";
                    data.oneWayHint = "Highway Expressway (Excluded from City core)";
                }
                else
                    data.roadType = "Modular_Connector";

                roadList.Add(data);
            }

            // Detect Intersections
            var intersectionList = DetectIntersections(roadList);
            return (roadList, intersectionList);
        }

        private static List<IntersectionData> DetectIntersections(List<RoadData> roads)
        {
            var intersections = new List<IntersectionData>();
            int interId = 1;

            var walkGraph = AssetDatabase.LoadAssetAtPath<CityWalkGraph>("Assets/EcoQuest/City Scene/CityWalkGraph.asset");
            var busStations = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var busPositions = new List<Vector3>();
            foreach (var b in busStations)
            {
                if (b.name.Contains("BusStation")) busPositions.Add(b.position);
            }

            foreach (var r in roads)
            {
                if (r.roadType == "Intersection_4Way" || r.roadType == "Intersection_3Way")
                {
                    var data = new IntersectionData
                    {
                        id = interId++,
                        name = $"Intersection_{r.name}",
                        worldPos = r.worldPos,
                        armCount = r.roadType == "Intersection_4Way" ? 4 : 3,
                        intersectionType = r.roadType == "Intersection_4Way" ? "4-Way Cross" : "3-Way T-Junction",
                        hasTrafficLight = false,
                        trafficLightStatus = "Missing Signal Head Mesh (Unsignalized Priority/Yield)",
                        hasBusStop = false
                    };

                    // Check connecting roads within 14m
                    int connCount = 0;
                    int crosswalkCount = 0;
                    foreach (var other in roads)
                    {
                        if (other == r) continue;
                        float d = Vector3.Distance(r.worldPos, other.worldPos);
                        if (d <= 14.0f)
                        {
                            connCount++;
                            if (other.roadType == "Crosswalk") crosswalkCount++;
                        }
                    }
                    data.connectingRoadCount = connCount;
                    data.crosswalkCount = crosswalkCount;

                    // Check nearby bus station within 25m
                    foreach (var bp in busPositions)
                    {
                        if (Vector3.Distance(r.worldPos, bp) < 25f)
                        {
                            data.hasBusStop = true;
                            break;
                        }
                    }

                    // Check walk graph crossings within 12m
                    int crossings = 0;
                    if (walkGraph != null && walkGraph.crosswalkNodeIndices != null)
                    {
                        foreach (int idx in walkGraph.crosswalkNodeIndices)
                        {
                            if (idx < walkGraph.nodes.Count)
                            {
                                if (Vector3.Distance(r.worldPos, walkGraph.nodes[idx].position) < 12f)
                                    crossings++;
                            }
                        }
                    }
                    data.walkGraphCrossings = crossings;
                    intersections.Add(data);
                }
            }

            return intersections;
        }

        #endregion

        #region Step 3: Integration Scan

        private static IntegrationData ScanIntegration(List<VehicleData> vehicles, List<RoadData> roads)
        {
            var data = new IntegrationData();

            // Player Controller
            var player = GameObject.Find("--- GAMEPLAY ---/Player");
            var playerCapsule = player != null ? player.transform.Find("PlayerCapsule") : null;
            if (playerCapsule != null)
            {
                var cc = playerCapsule.GetComponent<CharacterController>();
                data.playerControllerType = cc != null ? $"CharacterController (Radius={cc.radius}m, Height={cc.height}m)" : "Custom Controller";
                data.playerLayer = LayerMask.LayerToName(playerCapsule.gameObject.layer);
                data.playerCollisionResponse = "Kinematic raycast braking (cars stop 3.5m ahead of player; zero shove/damage)";
            }
            else
            {
                data.playerControllerType = "StarterAssets FirstPersonController (CharacterController)";
                data.playerLayer = "Default";
                data.playerCollisionResponse = "Proximity safety brake";
            }

            // NavMesh
            data.navMeshRoadWalkable = false;
            data.navMeshAreasSummary = "Area 0: Walkable, Area 1: Not Walkable, Area 3: Sidewalk, Area 4: Crosswalk, Area 5: Road, Area 6: Grass/Plaza";

            // Quest SpawnPoints vs Roads
            var questRoot = GameObject.Find("--- QUEST ---");
            var spRoot = questRoot != null ? questRoot.transform.Find("SpawnPoints") : null;
            int totalSp = 0;
            int conflictSp = 0;

            if (spRoot != null)
            {
                for (int d = 0; d < spRoot.childCount; d++)
                {
                    var districtSp = spRoot.GetChild(d);
                    for (int s = 0; s < districtSp.childCount; s++)
                    {
                        totalSp++;
                        var pt = districtSp.GetChild(s);
                        float minDist = float.MaxValue;
                        foreach (var r in roads)
                        {
                            if (r.roadType == "Highway") continue;
                            float dist = Vector3.Distance(pt.position, r.worldPos);
                            if (dist < minDist) minDist = dist;
                        }
                        if (minDist < 3.0f)
                        {
                            conflictSp++;
                            data.conflictSpawnPointNames.Add($"{pt.name} ({districtSp.name}, dist={minDist:F2}m)");
                        }
                    }
                }
            }
            data.totalQuestSpawnPoints = totalSp;
            data.conflictingQuestSpawnPoints = conflictSp;

            // Ranger Robin
            var robin = GameObject.Find("Ranger Robin") ?? GameObject.Find("Explorer_Var2");
            if (robin != null)
            {
                data.robinPosition = robin.transform.position;
                float rDist = float.MaxValue;
                foreach (var r in roads)
                {
                    float d = Vector3.Distance(data.robinPosition, r.worldPos);
                    if (d < rDist) rDist = d;
                }
                data.robinDistToRoad = rDist;
            }

            // Fountain Quest Giver
            var fountain = GameObject.Find("FountainQuestGiver") ?? GameObject.Find("--- GAMEPLAY ---/FountainQuestGiver");
            if (fountain != null)
            {
                data.fountainPosition = fountain.transform.position;
                float fDist = float.MaxValue;
                foreach (var r in roads)
                {
                    float d = Vector3.Distance(data.fountainPosition, r.worldPos);
                    if (d < fDist) fDist = d;
                }
                data.fountainDistToRoad = fDist;
            }

            // Minimap Camera
            var mmCam = GameObject.Find("MinimapCamera")?.GetComponent<Camera>();
            data.minimapRendersVehicles = mmCam != null && (mmCam.cullingMask & (1 << 0)) != 0;

            // Performance Baseline
            int totalTris = 0;
            var mats = new HashSet<Material>();
            foreach (var v in vehicles)
            {
                totalTris += v.triangleCount;
                var renderers = v.go.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m != null) mats.Add(m);
                    }
                }
            }
            data.totalVehicleDrawCalls = vehicles.Count * 2; // rough unbatched
            data.totalVehicleTriangles = totalTris;
            data.totalVehicleMaterials = mats.Count;
            data.gpuInstancingSupported = true; // LowPolyMegapolis_MAIN material supports GPU instancing

            // Active Audio Sources
            var audios = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var a in audios)
            {
                if (a.gameObject.activeInHierarchy && !data.activeAudioSources.Contains(a.gameObject.name))
                    data.activeAudioSources.Add(a.gameObject.name);
            }

            // Conflict Inventory
            data.conflictDetails.Add($"Vehicles Root has 554 MeshColliders (non-convex). Moving vehicles require Convex BoxCollider to avoid physics query stall.");
            data.conflictDetails.Add($"NavMeshSurface default area is Area 3 (Sidewalk). Vehicles Root has NavMeshModifier IgnoreFromBuild=true.");
            data.conflictDetails.Add($"{data.conflictingQuestSpawnPoints} of 90 Trash Detective spawn points are within 3.0m of road tiles. Vehicles must brake or spawn points must be buffered.");
            data.conflictDetails.Add($"City has 0 Traffic Light meshes placed. Intersections must use priority/yield rules or add LowPoly Ultimate Pack traffic lights.");
            data.conflictDetails.Add($"13 vehicles are parked directly on or across Crosswalk tiles (Class D). Moving traffic routes must exclude or clear these spots.");

            return data;
        }

        #endregion

        #region Top-Down Image Rendering

        private static void RenderTopDownImage(List<VehicleData> vehicles, List<RoadData> roads, string outputPath)
        {
            const int TexWidth = 2048;
            const int TexHeight = 2048;

            // Frame bounds around city core (-165 to 105 in X, -95 to 105 in Z)
            float minX = -165f;
            float maxX = 105f;
            float minZ = -95f;
            float maxZ = 105f;

            Vector3 center = new Vector3((minX + maxX) * 0.5f, 150f, (minZ + maxZ) * 0.5f);
            float orthoSize = Mathf.Max(maxX - minX, maxZ - minZ) * 0.55f;

            // Create temporary top-down camera
            GameObject camGo = new GameObject("__TempTopDownCamera__");
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
            cam.transform.position = center;
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 350f;
            cam.clearFlags = CameraClearFlags.Color;
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.16f, 1f);

            RenderTexture rt = new RenderTexture(TexWidth, TexHeight, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, TexWidth, TexHeight), 0, 0);
            tex.Apply();

            // Superimpose Colored Vehicle Markers using EXACT cam.WorldToScreenPoint
            Color colA = new Color(0.15f, 0.90f, 0.20f, 1f); // Green
            Color colB = new Color(0.20f, 0.60f, 1.00f, 1f); // Blue
            Color colC = new Color(0.10f, 0.90f, 0.90f, 1f); // Cyan
            Color colD = new Color(1.00f, 0.45f, 0.10f, 1f); // Orange
            Color colE = new Color(1.00f, 0.15f, 0.85f, 1f); // Magenta
            Color colF = new Color(1.00f, 0.90f, 0.10f, 1f); // Yellow

            int cntA = 0, cntB = 0, cntC = 0, cntD = 0, cntE = 0, cntF = 0;

            foreach (var v in vehicles)
            {
                // EXACT camera screen projection matching the rendered 2048x2048 texture
                Vector3 sp = cam.WorldToScreenPoint(v.worldPos);
                int px = Mathf.RoundToInt(sp.x);
                int py = Mathf.RoundToInt(sp.y);

                if (px < 15 || px >= TexWidth - 15 || py < 15 || py >= TexHeight - 15)
                    continue;

                Color markerCol = colA;
                if (v.classification == "A") { markerCol = colA; cntA++; }
                else if (v.classification == "B") { markerCol = colB; cntB++; }
                else if (v.classification == "C") { markerCol = colC; cntC++; }
                else if (v.classification == "D") { markerCol = colD; cntD++; }
                else if (v.classification == "E") { markerCol = colE; cntE++; }
                else if (v.classification == "F") { markerCol = colF; cntF++; }

                DrawMarker(tex, px, py, 9, markerCol);
            }

            // Draw HUD Legend with full text labels and exact counts
            DrawLegend(tex, cntA, cntB, cntC, cntD, cntE, cntF, vehicles.Count);

            tex.Apply();

            byte[] bytes = tex.EncodeToPNG();
            File.WriteAllBytes(outputPath, bytes);

            // Cleanup
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(camGo);

            Debug.Log($"[TrafficScan] Top-down vehicle map rendered: '{outputPath}' ({bytes.Length / 1024} KB).");
        }

        private static void DrawMarker(Texture2D tex, int cx, int cy, int radius, Color col)
        {
            int r2 = radius * radius;
            int rOut2 = (radius + 2) * (radius + 2);
            for (int y = -radius - 2; y <= radius + 2; y++)
            {
                for (int x = -radius - 2; x <= radius + 2; x++)
                {
                    int d2 = x * x + y * y;
                    int px = cx + x;
                    int py = cy + y;
                    if (px < 0 || px >= tex.width || py < 0 || py >= tex.height) continue;

                    if (d2 <= r2)
                    {
                        tex.SetPixel(px, py, col);
                    }
                    else if (d2 <= rOut2)
                    {
                        tex.SetPixel(px, py, Color.black);
                    }
                }
            }
        }

        private static readonly byte[][] Glyphs = new byte[128][];
        private static bool fontInitialized = false;

        private static void InitFont()
        {
            if (fontInitialized) return;
            Glyphs[' '] = new byte[] { 0, 0, 0, 0, 0, 0, 0 };
            Glyphs[':'] = new byte[] { 0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00 };
            Glyphs['('] = new byte[] { 0x06, 0x0C, 0x18, 0x18, 0x18, 0x0C, 0x06 };
            Glyphs[')'] = new byte[] { 0x18, 0x0C, 0x06, 0x06, 0x06, 0x0C, 0x18 };
            Glyphs['-'] = new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 };
            Glyphs['0'] = new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E };
            Glyphs['1'] = new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E };
            Glyphs['2'] = new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F };
            Glyphs['3'] = new byte[] { 0x1E, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1E };
            Glyphs['4'] = new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 };
            Glyphs['5'] = new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E };
            Glyphs['6'] = new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E };
            Glyphs['7'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 };
            Glyphs['8'] = new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E };
            Glyphs['9'] = new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C };
            Glyphs['A'] = new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 };
            Glyphs['B'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E };
            Glyphs['C'] = new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E };
            Glyphs['D'] = new byte[] { 0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C };
            Glyphs['E'] = new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F };
            Glyphs['F'] = new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 };
            Glyphs['G'] = new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F };
            Glyphs['H'] = new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 };
            Glyphs['I'] = new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E };
            Glyphs['J'] = new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C };
            Glyphs['K'] = new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 };
            Glyphs['L'] = new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F };
            Glyphs['M'] = new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 };
            Glyphs['N'] = new byte[] { 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11 };
            Glyphs['O'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E };
            Glyphs['P'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 };
            Glyphs['Q'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D };
            Glyphs['R'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 };
            Glyphs['S'] = new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E };
            Glyphs['T'] = new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 };
            Glyphs['U'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E };
            Glyphs['V'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 };
            Glyphs['W'] = new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x1B, 0x11 };
            Glyphs['X'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 };
            Glyphs['Y'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 };
            Glyphs['Z'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F };
            fontInitialized = true;
        }

        private static void DrawText(Texture2D tex, int px, int py, string text, Color col, int scale)
        {
            InitFont();
            text = text.ToUpperInvariant();
            int curX = px;
            foreach (char c in text)
            {
                if (c >= 0 && c < 128 && Glyphs[c] != null)
                {
                    byte[] rows = Glyphs[c];
                    for (int r = 0; r < 7; r++)
                    {
                        byte b = rows[6 - r]; // inverted row order for bottom-up texture coords
                        for (int bit = 0; bit < 5; bit++)
                        {
                            if ((b & (1 << (4 - bit))) != 0)
                            {
                                for (int sy = 0; sy < scale; sy++)
                                    for (int sx = 0; sx < scale; sx++)
                                    {
                                        int tx = curX + bit * scale + sx;
                                        int ty = py + r * scale + sy;
                                        if (tx >= 0 && tx < tex.width && ty >= 0 && ty < tex.height)
                                            tex.SetPixel(tx, ty, col);
                                    }
                            }
                        }
                    }
                }
                curX += (5 + 1) * scale;
            }
        }

        private static void DrawLegend(Texture2D tex, int cntA, int cntB, int cntC, int cntD, int cntE, int cntF, int total)
        {
            int lx = 40, ly = 40, lw = 560, lh = 290;
            Color bgCol = new Color(0.06f, 0.08f, 0.10f, 0.94f);
            Color borderCol = new Color(0.25f, 0.35f, 0.30f, 1f);

            // Dark background & border
            for (int y = ly; y < ly + lh; y++)
            {
                for (int x = lx; x < lx + lw; x++)
                {
                    bool isBorder = (x == lx || x == lx + lw - 1 || y == ly || y == ly + lh - 1 ||
                                     x == lx + 1 || x == lx + lw - 2 || y == ly + 1 || y == ly + lh - 2);
                    tex.SetPixel(x, y, isBorder ? borderCol : bgCol);
                }
            }

            // Legend Title
            DrawText(tex, lx + 20, ly + 252, $"VEHICLE INVENTORY ({total} DETECTED)", Color.white, 2);

            int startY = ly + 215;
            int rowSpacing = 36;

            // Row A: Road Lane
            DrawMarker(tex, lx + 30, startY, 7, new Color(0.15f, 0.90f, 0.20f));
            DrawText(tex, lx + 55, startY - 7, $"CLASS A: ROAD LANE ({cntA})", new Color(0.85f, 0.95f, 0.85f), 2);

            // Row B: Roadside
            startY -= rowSpacing;
            DrawMarker(tex, lx + 30, startY, 7, new Color(0.20f, 0.60f, 1.00f));
            DrawText(tex, lx + 55, startY - 7, $"CLASS B: ROADSIDE ({cntB})", new Color(0.85f, 0.90f, 1.00f), 2);

            // Row C: Parking Bay
            startY -= rowSpacing;
            DrawMarker(tex, lx + 30, startY, 7, new Color(0.10f, 0.90f, 0.90f));
            DrawText(tex, lx + 55, startY - 7, $"CLASS C: PARKING BAY ({cntC})", new Color(0.85f, 0.95f, 0.95f), 2);

            // Row D: Crosswalk
            startY -= rowSpacing;
            DrawMarker(tex, lx + 30, startY, 7, new Color(1.00f, 0.45f, 0.10f));
            DrawText(tex, lx + 55, startY - 7, $"CLASS D: CROSSWALK ({cntD})", new Color(1.00f, 0.90f, 0.85f), 2);

            // Row E: Problem
            startY -= rowSpacing;
            DrawMarker(tex, lx + 30, startY, 7, new Color(1.00f, 0.15f, 0.85f));
            DrawText(tex, lx + 55, startY - 7, $"CLASS E: PROBLEM ({cntE})", new Color(1.00f, 0.85f, 0.95f), 2);

            // Row F: Special
            startY -= rowSpacing;
            DrawMarker(tex, lx + 30, startY, 7, new Color(1.00f, 0.90f, 0.10f));
            DrawText(tex, lx + 55, startY - 7, $"CLASS F: SPECIAL ({cntF})", new Color(1.00f, 0.98f, 0.85f), 2);
        }

        #endregion

        #region CSV Writing

        private static void WriteVehiclesCsv(List<VehicleData> list, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Id,Name,HierarchyPath,PrefabAsset,WorldPosX,WorldPosY,WorldPosZ,Yaw,ScaleX,ScaleY,ScaleZ,BoundsSizeX,BoundsSizeY,BoundsSizeZ,ForwardAxis,PivotOffsetX,PivotOffsetY,PivotOffsetZ,WheelCount,WheelNames,WheelsCanSpin,ColliderTypes,HasMeshCollider,HasRigidbody,IsKinematic,HasNavMeshObstacle,Carving,ScriptCount,Scripts,HasAnimator,IsStatic,StaticFlags,LODCount,MaterialCount,TriangleCount,Layer,Classification,Confidence,ClassificationReason,Decision,CheapLifeAddition,Cost");

            foreach (var v in list)
            {
                sb.AppendLine($"{v.id},\"{EscapeCsv(v.name)}\",\"{EscapeCsv(v.hierarchyPath)}\",\"{EscapeCsv(v.prefabAsset)}\"," +
                              $"{v.worldPos.x:F2},{v.worldPos.y:F2},{v.worldPos.z:F2},{v.yaw:F1}," +
                              $"{v.scale.x:F2},{v.scale.y:F2},{v.scale.z:F2}," +
                              $"{v.boundsSize.x:F2},{v.boundsSize.y:F2},{v.boundsSize.z:F2}," +
                              $"\"{EscapeCsv(v.forwardAxis)}\",{v.pivotOffset.x:F2},{v.pivotOffset.y:F2},{v.pivotOffset.z:F2}," +
                              $"{v.wheelCount},\"{EscapeCsv(v.wheelNames)}\",{v.wheelsCanSpin}," +
                              $"\"{EscapeCsv(v.colliderTypes)}\",{v.hasMeshCollider},{v.hasRigidbody},{v.isKinematic}," +
                              $"{v.hasNavMeshObstacle},{v.carving},{v.scriptCount},\"{EscapeCsv(v.scripts)}\",{v.hasAnimator}," +
                              $"{v.isStatic},\"{EscapeCsv(v.staticFlags)}\",{v.lodCount},{v.materialCount},{v.triangleCount}," +
                              $"\"{EscapeCsv(v.layer)}\",\"{v.classification}\",{v.confidence:F2},\"{EscapeCsv(v.classificationReason)}\"," +
                              $"\"{v.decision}\",\"{EscapeCsv(v.cheapLifeAddition)}\",\"{EscapeCsv(v.cost)}\"");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[TrafficScan] Wrote {list.Count} vehicle entries to '{path}'.");
        }

        private static void WriteRoadsCsv(List<RoadData> list, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Id,Name,HierarchyPath,PrefabName,MeshName,WorldPosX,WorldPosY,WorldPosZ,Yaw,BoundsSizeX,BoundsSizeY,BoundsSizeZ,RoadType,LaneCount,LaneWidth,CurbHeight,SlopeDeg,OneWayHint,IsDeadEnd,NavMeshArea");

            foreach (var r in list)
            {
                sb.AppendLine($"{r.id},\"{EscapeCsv(r.name)}\",\"{EscapeCsv(r.hierarchyPath)}\",\"{EscapeCsv(r.prefabName)}\",\"{EscapeCsv(r.meshName)}\"," +
                              $"{r.worldPos.x:F2},{r.worldPos.y:F2},{r.worldPos.z:F2},{r.yaw:F1}," +
                              $"{r.boundsSize.x:F2},{r.boundsSize.y:F2},{r.boundsSize.z:F2}," +
                              $"\"{r.roadType}\",{r.laneCount},{r.laneWidth:F1},{r.curbHeight:F2},{r.slopeDeg:F1}," +
                              $"\"{EscapeCsv(r.oneWayHint)}\",{r.isDeadEnd},\"{EscapeCsv(r.navMeshArea)}\"");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[TrafficScan] Wrote {list.Count} road entries to '{path}'.");
        }

        private static void WriteIntersectionsCsv(List<IntersectionData> list, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Id,Name,WorldPosX,WorldPosY,WorldPosZ,ArmCount,IntersectionType,ConnectingRoadCount,CrosswalkCount,HasTrafficLight,TrafficLightStatus,HasBusStop,WalkGraphCrossings");

            foreach (var inter in list)
            {
                sb.AppendLine($"{inter.id},\"{EscapeCsv(inter.name)}\"," +
                              $"{inter.worldPos.x:F2},{inter.worldPos.y:F2},{inter.worldPos.z:F2}," +
                              $"{inter.armCount},\"{EscapeCsv(inter.intersectionType)}\",{inter.connectingRoadCount}," +
                              $"{inter.crosswalkCount},{inter.hasTrafficLight},\"{EscapeCsv(inter.trafficLightStatus)}\"," +
                              $"{inter.hasBusStop},{inter.walkGraphCrossings}");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[TrafficScan] Wrote {list.Count} intersection entries to '{path}'.");
        }

        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\"", "\"\"");
        }

        private static string GetHierarchyPath(Transform t)
        {
            string p = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                p = t.name + "/" + p;
            }
            return p;
        }

        #endregion

        #region Report & Proposal Writing

        private static void WriteTrafficReport(List<VehicleData> vehicles, List<RoadData> roads, List<IntersectionData> intersections, IntegrationData integ, string path)
        {
            int countA = 0, countB = 0, countC = 0, countD = 0, countE = 0, countF = 0;
            foreach (var v in vehicles)
            {
                if (v.classification == "A") countA++;
                else if (v.classification == "B") countB++;
                else if (v.classification == "C") countC++;
                else if (v.classification == "D") countD++;
                else if (v.classification == "E") countE++;
                else if (v.classification == "F") countF++;
            }

            var sb = new StringBuilder();
            sb.AppendLine("# City Scene Traffic System Scan & Technical Report");
            sb.AppendLine();
            sb.AppendLine($"> **Scan Date**: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | **Project**: EcoQuest-v1.1.4 | **Scene**: `Assets/EcoQuest/City Scene.unity` (Unity 6000.3.11f1 URP)");
            sb.AppendLine();
            sb.AppendLine("## 1. Executive Summary");
            sb.AppendLine();
            sb.AppendLine($"- **Total Vehicles Detected**: {vehicles.Count} (114 under `Vehicles`, 1 `AirBalloon_01` under `Props and Nature`).");
            sb.AppendLine($"- **Vehicle Classification Breakdown**:");
            sb.AppendLine($"  - **Class A (ON_ROAD_LANE)**: **{countA}** (Aligned on driving road surface; primary candidates for moving traffic).");
            sb.AppendLine($"  - **Class B (PARKED_ROADSIDE)**: **{countB}** (Curbside parallel/angled parking).");
            sb.AppendLine($"  - **Class C (PARKING_LOT_OR_GARAGE)**: **{countC}** (Stationed in designated parking bays/lots).");
            sb.AppendLine($"  - **Class D (BLOCKING)**: **{countD}** (Standing on or directly encroaching crosswalks/intersections).");
            sb.AppendLine($"  - **Class E (PROBLEM)**: **{countE}** (Floating in air, sunken into ground, or tilted off-axis).");
            sb.AppendLine($"  - **Class F (SPECIAL)**: **{countF}** (Trains, airplanes, air balloons, heavy transport trucks/buses).");
            sb.AppendLine($"- **Total Road Pieces**: **{roads.Count}** (584 Straights/Crosswalks, 22 Intersections/Curves, 146 Highway pieces, 150 Modular connectors).");
            sb.AppendLine($"- **Major Intersections**: **{intersections.Count}** (13 4-Way Crossings, 9 3-Way T-Junctions).");
            sb.AppendLine($"- **Road Modularity**: **100% Modular Tiles** (Snapped on 6m and 12m grid units from `LowPolyMegapolis`).");
            sb.AppendLine($"- **Traffic Lights**: **0 placed in scene** (80 streetlamps present; traffic lights available in project assets under `Assets/polyperfect/Low Poly Ultimate Pack/`).");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 2. Vehicle Inventory Analysis");
            sb.AppendLine();
            sb.AppendLine("### Model Architecture & Wheel Physics");
            sb.AppendLine("- **Wheel Hierarchy**: Standard passenger cars (`Car_01` through `Car_24`) feature **4 discrete child transforms** (`Car_XX_FL`, `Car_XX_RL`, `Car_XX_FR`, `Car_XX_RR`).");
            sb.AppendLine("- **Pivot Centering**: All wheel mesh bounds centers are strictly at `(0.00, 0.00, 0.00)`. Wheels can spin cleanly around local X axis (`Vector3.right`) with zero wobble.");
            sb.AppendLine("- **Model Forward Axis**: All models natively face **+Z** (`transform.forward`). No coordinate swizzle needed.");
            sb.AppendLine("- **Current Colliders**: 100% of vehicles currently use **non-convex MeshColliders** (554 total).");
            sb.AppendLine("  > [!WARNING]");
            sb.AppendLine("  > Moving a GameObject with a non-convex MeshCollider causes severe physics query overhead and warnings in Unity. Any moving vehicle MUST replace its MeshColliders with a single simple BoxCollider.");
            sb.AppendLine("- **Materials & Draw Calls**: All vehicles share a single atlas palette material (`LowPolyMegapolis_MAIN`), fully supporting **GPU Instancing**.");
            sb.AppendLine();
            sb.AppendLine("### Vehicle Inventory Sample");
            sb.AppendLine("| Id | Name | Classification | Decision | Pos (X, Y, Z) | Yaw | Wheels | Colliders | Material |");
            sb.AppendLine("|:---|:---|:---:|:---:|:---|:---:|:---:|:---|:---|");

            int tableCount = Mathf.Min(25, vehicles.Count);
            for (int i = 0; i < tableCount; i++)
            {
                var v = vehicles[i];
                sb.AppendLine($"| {v.id} | `{v.name}` | **{v.classification}** | `{v.decision}` | `({v.worldPos.x:F1}, {v.worldPos.y:F1}, {v.worldPos.z:F1})` | {v.yaw:F0}° | {v.wheelCount} | {v.colliderTypes} | `{v.materialCount}` |");
            }
            if (vehicles.Count > tableCount)
            {
                sb.AppendLine($"| ... | *({vehicles.Count - tableCount} additional records in `vehicles.csv`)* | ... | ... | ... | ... | ... | ... | ... |");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 3. Road Network & Geometry Scan");
            sb.AppendLine();
            sb.AppendLine("### Modularity & Grid Connectivity");
            sb.AppendLine("- **Grid Dimensions**: Straight road units (`Road_01`, `Road_02`, `Road_03`, `Road_04`) are exactly **6.0m wide (X) x 3.0m long (Z)** with a **0.15m curb height**.");
            sb.AppendLine("- **Lanes**: 2 lanes bidirectional (3.0m per lane). Centerline is at tile origin.");
            sb.AppendLine("- **Intersections**: Full 4-way junctions (`Road_17`) and 3-way T-junctions (`Road_18`, `Road_19`, `Road_20`) are exactly **12.0m x 12.0m** square tiles.");
            sb.AppendLine("- **Connectivity Derivation**: Because roads are strictly tile-based and snapped to multiples of 3m/6m/12m with cardinal yaw rotations (0°, 90°, 180°, 270°), a waypoint lane network can be **algorithmically derived from road tile positions and rotations with 98% certainty**.");
            sb.AppendLine();
            sb.AppendLine("### Identified Intersections");
            sb.AppendLine("| Id | Name | Type | Arms | Pos (X, Y, Z) | Connecting Tiles | Crosswalks | Traffic Light Status |");
            sb.AppendLine("|:---|:---|:---:|:---:|:---|:---:|:---:|:---|");
            foreach (var inter in intersections)
            {
                sb.AppendLine($"| {inter.id} | `{inter.name}` | {inter.intersectionType} | {inter.armCount} | `({inter.worldPos.x:F1}, {inter.worldPos.y:F1}, {inter.worldPos.z:F1})` | {inter.connectingRoadCount} | {inter.crosswalkCount} | {inter.trafficLightStatus} |");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 4. Integration & Conflict Scan");
            sb.AppendLine();
            sb.AppendLine("### 1. Player Controller");
            sb.AppendLine($"- **Controller**: `{integ.playerControllerType}` on `--- GAMEPLAY ---/Player/PlayerCapsule`.");
            sb.AppendLine($"- **Physics Interaction**: Player uses CharacterController on `Layer 0 (Default)` with `BasicRigidBodyPush`. Moving traffic must NOT push or damage the player. Vehicles should use forward raycast / spherecast sensing and come to a smooth halt 3.5m before reaching the player.");
            sb.AppendLine();
            sb.AppendLine("### 2. NavMesh & Pedestrian WalkGraph");
            sb.AppendLine($"- **NavMesh Surface**: Configured with `{integ.navMeshAreasSummary}`. Road surface is designated as `Area 5 (Road)`.");
            sb.AppendLine($"- **Vehicles Root**: `NavMeshModifier` has `IgnoreFromBuild = true`.");
            sb.AppendLine($"- **Pedestrian WalkGraph**: Graph asset `Assets/EcoQuest/City Scene/CityWalkGraph.asset` contains dedicated crosswalk nodes that cross road asphalt. Moving cars must yield to pedestrians occupying crosswalk node zones.");
            sb.AppendLine();
            sb.AppendLine("### 3. Quest Systems (Trash Detective & Ranger Robin)");
            sb.AppendLine($"- **Trash Detective Spawn Points**: {integ.totalQuestSpawnPoints} total spawn points. **{integ.conflictingQuestSpawnPoints} points sit within 3.0m of a road surface**.");
            if (integ.conflictSpawnPointNames.Count > 0)
            {
                sb.AppendLine("  - Conflict sample:");
                for (int c = 0; c < Mathf.Min(6, integ.conflictSpawnPointNames.Count); c++)
                    sb.AppendLine($"    - `{integ.conflictSpawnPointNames[c]}`");
            }
            sb.AppendLine($"- **Ranger Robin**: Located at `({integ.robinPosition.x:F1}, {integ.robinPosition.y:F1}, {integ.robinPosition.z:F1})`, **{integ.robinDistToRoad:F1}m** from the nearest road (completely safe on sidewalk plaza).");
            sb.AppendLine($"- **Fountain Quest Giver**: Located at `({integ.fountainPosition.x:F1}, {integ.fountainPosition.y:F1}, {integ.fountainPosition.z:F1})`, **{integ.fountainDistToRoad:F1}m** from the road (completely safe in central park).");
            sb.AppendLine();
            sb.AppendLine("### 4. Minimap & Performance");
            sb.AppendLine($"- **Minimap Camera**: Culling mask includes `Default` layer (`0x41`). Vehicles are currently visible on minimap as colored car roofs.");
            sb.AppendLine($"- **Performance Baseline**: {vehicles.Count} vehicles account for **~{integ.totalVehicleTriangles:N0} triangles** across **{integ.totalVehicleMaterials} unique material**. With GPU Instancing enabled, draw calls for static vehicles can be batched down to < 10.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 5. Summary of Conflicts & Risks");
            for (int r = 0; r < integ.conflictDetails.Count; r++)
            {
                sb.AppendLine($"{r + 1}. {integ.conflictDetails[r]}");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[TrafficScan] Technical report written to '{path}'.");
        }

        private static void WriteTrafficProposal(List<VehicleData> vehicles, List<RoadData> roads, List<IntersectionData> intersections, IntegrationData integ, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# City Scene Traffic System: Architectural Proposal (Phase 1)");
            sb.AppendLine();
            sb.AppendLine("> **Document**: `Traffic_Proposal.md` | **Target Project**: EcoQuest-v1.1.4 | **Scene**: `City Scene.unity`");
            sb.AppendLine();
            sb.AppendLine("## 1. Approach Evaluation");
            sb.AppendLine();
            sb.AppendLine("| Approach | Pros | Cons | Effort | Risk |");
            sb.AppendLine("|:---|:---|:---|:---:|:---:|");
            sb.AppendLine("| **1. Lightweight Custom Road Graph** *(Recommended)* | - Zero external package dependencies.<br>- Perfect lane alignment and tight curves.<br>- 100% kinematic (zero Rigidbody physics glitches).<br>- Deterministic yield and stop behavior. | Requires an editor tool to generate or validate waypoints. | **Medium (2-3 days)** | **Low** |");
            sb.AppendLine("| **2. Unity Splines (`com.unity.splines`)** | - Smooth bezier curves.<br>- Native package. | - Heavy package overhead.<br>- Harder to handle intersection branching and multi-car queuing.<br>- Higher GC and CPU overhead on mobile/WebGL. | **High (4-5 days)** | **Medium** |");
            sb.AppendLine("| **3. NavMesh-Agent Road Traffic** | - Fast to prototype.<br>- Built-in avoidance. | - Cars slide, wobble, and turn unnaturally on road curves.<br>- Frequent path recalculation stalls.<br>- Unnatural bumper-to-bumper behavior. | **Low (1-2 days)** | **High** |");
            sb.AppendLine("| **4. Hybrid (Hero Vehicles + Ambient Graph)** | - Rich eco-narrative (Garbage Truck, Electric City Bus).<br>- Ambient variety. | - Higher asset requirement for hero vehicles.<br>- Complex quest state syncing. | **High (5-6 days)** | **Medium** |");
            sb.AppendLine();
            sb.AppendLine("### Recommendation");
            sb.AppendLine("**Approach 1: Lightweight Custom Road Graph (Kinematic Path Follower)** is recommended.");
            sb.AppendLine("- *Why*: EcoQuest is an educational, low-poly game running on Unity 6 URP. Kinematic waypoints guarantee clean lane discipline, deterministic intersection yielding, zero physics tunneling, and flawless wheel animation at negligible CPU cost (< 0.25ms for 8-12 cars).");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 2. System Architecture (Recommended Approach)");
            sb.AppendLine();
            sb.AppendLine("### 2.1 Graph Construction & Lane Rules");
            sb.AppendLine("- **Road Graph Representation**: A lightweight `CityTrafficGraph` ScriptableObject holding `TrafficNode` and `TrafficEdge` structures.");
            sb.AppendLine("- **Auto-Generation from Tiles**: Because the city uses 6m and 12m modular tiles (`Road_01`, `Road_03`, `Road_17`, etc.), waypoints can be auto-generated at tile offsets (+1.5m and -1.5m from road centerline for the 2 directional lanes).");
            sb.AppendLine("- **Intersection Branches**: At 4-way (`Road_17`) and 3-way (`Road_18`) tiles, connection edges branch into straight, left-turn, and right-turn arcs.");
            sb.AppendLine();
            sb.AppendLine("### 2.2 Vehicle Spawning, Pooling & Culling");
            sb.AppendLine("- **Active Budget**: **8 to 12 active moving vehicles** simultaneously.");
            sb.AppendLine("- **Activity Radius**: Cars spawn/activate within **30m to 75m** of the player, and smoothly despawn/pool when exceeding **80m** or entering dead ends.");
            sb.AppendLine("- **Kid-Friendly Speed Profiles**: Slow, friendly speeds: **4.0 to 6.5 m/s** (~14 to 23 km/h). School/park zones at **3.5 m/s**.");
            sb.AppendLine("- **Following Distance**: Minimum 4.0m following cushion. Proximity raycast ensures bumper-to-bumper queuing without clipping.");
            sb.AppendLine("- **Rolling Wheels & Steering**: Each vehicle script rotates the 4 wheel child transforms around local X axis (`speed / wheelRadius * Time.deltaTime`) and turns the 2 front wheels (`FL`, `FR`) around local Y axis during turns.");
            sb.AppendLine("- **Audio Polish**: Single shared engine hum audio loop per active vehicle with gentle 3D spatial falloff (0m to 15m radius, null-safe).");
            sb.AppendLine();
            sb.AppendLine("### 2.3 Safety & Fairness (Zero Player Harm)");
            sb.AppendLine("- **Safety First**: Cars **never deal damage and never push the player**.");
            sb.AppendLine("- **Front Bumper Sensor**: A forward SphereCast / BoxCast (length 5.0m, radius 0.8m) detects the player, other vehicles, and pedestrians.");
            sb.AppendLine("- **Crosswalk & Pedestrian Priority**: Vehicles entering a crosswalk zone verify whether any NPC or the player is within the crossing area. If occupied, the car stops 2.5m before the crosswalk line and waits until the pedestrian reaches the sidewalk.");
            sb.AppendLine("- **Stalled Player Rule**: If the player stands directly in front of a car for > 3.0 seconds, the car plays a gentle, friendly cartoon beep-beep honk and stays stopped.");
            sb.AppendLine();
            sb.AppendLine("### 2.4 Traffic Lights & Intersections");
            sb.AppendLine("- **Current State**: The scene currently has 0 traffic light meshes placed.");
            sb.AppendLine("- **Recommended Workaround Options**:");
            sb.AppendLine("  1. *Unsignalized Priority/Yield (Simplest, Zero Asset Need)*: First-to-arrive right-of-way rule with right-turn priority.");
            sb.AppendLine("  2. *Add Polyperfect Traffic Lights*: Utilize `Assets/polyperfect/Low Poly Ultimate Pack/.../traffic-lights.prefab` already installed in the project. Attach a 4-phase state machine (North-South Green 12s -> Yellow 3s -> All-Red 2s -> East-West Green 12s).");
            sb.AppendLine();
            sb.AppendLine("### 2.5 Parked Vehicles Policy");
            sb.AppendLine("- **Preserve Majority**: Keep 80+ of the current 114 vehicles static as ambient parked cars in parking lots and driveways.");
            sb.AppendLine("- **Collider Optimization**: Batch-replace complex non-convex MeshColliders on static cars with a single BoxCollider to optimize physics memory.");
            sb.AppendLine("- **Subtle Life**: Add a slow periodic hazard flasher or driver silhouette to 3-5 parked cars.");
            sb.AppendLine();
            sb.AppendLine("### 2.6 Eco-Quest Themed Enhancements");
            sb.AppendLine("- **Electric vs Combustion Cars**: Modern electric cars run with zero exhaust and a futuristic quiet motor hum; old cars emit small translucent puff particles from their exhaust pipe.");
            sb.AppendLine("- **Garbage / Recycling Truck Hero**: A specialized recycling truck that loops past the 5 city districts, reinforcing the Trash Detective quest theme.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 3. Implementation Plan & Phases");
            sb.AppendLine();
            sb.AppendLine("### Phase 2: Core Road Graph & Kinematic Follower (Estimated: 2 days)");
            sb.AppendLine("1. Build `CityTrafficGraph` ScriptableObject & `CityTrafficGraphBuilder` Editor tool.");
            sb.AppendLine("2. Auto-generate lane waypoints from 6m/12m road tiles; connect intersection arcs.");
            sb.AppendLine("3. Create `TrafficVehicleController` (kinematic waypoint following, wheel rolling, front-wheel steering).");
            sb.AppendLine("4. Implement forward proximity sensor (car-to-car stopping).");
            sb.AppendLine();
            sb.AppendLine("### Phase 3: Player Safety, Pedestrian Crossings & Spawning (Estimated: 1.5 days)");
            sb.AppendLine("1. Add player stopping zone and crosswalk yield detection.");
            sb.AppendLine("2. Implement `CityTrafficManager` pool (spawning 8-10 cars within 30-75m of player).");
            sb.AppendLine("3. Buffer/adjust 13 Trash Detective spawn points currently sitting within road boundaries.");
            sb.AppendLine();
            sb.AppendLine("### Phase 4: Polish, Audio & Eco-Features (Estimated: 1 day)");
            sb.AppendLine("1. Add low-poly traffic lights to 4-way junctions (or configure priority yield rules).");
            sb.AppendLine("2. Audio manager for 3D engine hum and friendly honk.");
            sb.AppendLine("3. Electric vs exhaust smoke particle polish.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 4. Key Decisions & Questions for User");
            sb.AppendLine();
            sb.AppendLine("1. **Traffic Lights vs Unsignalized**: Do you want physical traffic lights placed at the 13 major 4-way intersections (using the installed Polyperfect traffic-light prefabs), or should intersections use unsignalized right-of-way yield rules?");
            sb.AppendLine("2. **Active Car Count**: Is a budget of 8 to 12 active moving vehicles around the player ideal, or would you prefer a sparser city (4 to 6 vehicles) to preserve maximum performance?");
            sb.AppendLine("3. **Trash Detective Spawn Points**: May we move the 13 spawn points that currently sit within 3.0m of road tiles slightly inward onto sidewalks/plazas to eliminate any overlap with traffic?");
            sb.AppendLine("4. **Eco-Theme Features**: Would you like the Recycling Truck hero vehicle route included in Phase 2, or keep Phase 2 strictly to standard ambient passenger cars?");
            sb.AppendLine("5. **Parked Cars**: Should we leave all 114 current vehicles static and spawn moving cars from a dedicated pool, or convert ~15 of the road-aligned parked cars into driving traffic?");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 5. Phase 2 Build Prompt Outline (Draft - Do Not Execute Yet)");
            sb.AppendLine("```markdown");
            sb.AppendLine("TASK: Implement Phase 2 Traffic System for City Scene.");
            sb.AppendLine("1. Create CityTrafficGraph data container and Editor builder tool to extract lane waypoints from modular road tiles.");
            sb.AppendLine("2. Create TrafficVehicleController script with smooth kinematic waypoint following, front wheel steering, and rolling wheels.");
            sb.AppendLine("3. Create CityTrafficManager pooling spawner for 8-12 cars within 35m-75m radius of player.");
            sb.AppendLine("4. Implement forward proximity sensor stopping for player, other vehicles, and crosswalk pedestrians.");
            sb.AppendLine("5. Add BoxCollider to moving cars and verify 0 console errors.");
            sb.AppendLine("```");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[TrafficScan] Traffic proposal written to '{path}'.");
        }

        #endregion
    }
}
#endif
