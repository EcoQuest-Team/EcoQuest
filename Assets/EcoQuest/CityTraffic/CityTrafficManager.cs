using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EcoQuest.CityTraffic
{
    [Serializable]
    public class TrafficSignalPoleBinding
    {
        public int intersectionIndex;
        public int travelDir; // 0=+Z(N), 1=+X(E), 2=-Z(S), 3=-X(W)
        public MeshRenderer poleRenderer;
        [NonSerialized] public int lastAppliedState = -1;
    }

    [DisallowMultipleComponent]
    public class CityTrafficManager : MonoBehaviour
    {
        private const string AllowedScenePath = "Assets/EcoQuest/City Scene.unity";

        public static CityTrafficManager Instance { get; private set; }

        [Header("Graph & Pool Settings")]
        public CityTrafficGraph graph;
        [Range(6, 12)] public int activeCarTarget = 10;
        public int randomSeed = 20261108;
        public List<GameObject> fleetPrefabs = new List<GameObject>();

        [Header("Speed & Sensor Tuning")]
        public float minSpeed = 4.0f;
        public float maxSpeed = 6.5f;
        public float parkSpeed = 3.5f;
        public float curveSpeed = 3.0f;
        public float sensorLength = 5.0f;

        [Header("Spawn & Despawn Radii")]
        public float minSpawnRadius = 30.0f;
        public float maxSpawnRadius = 75.0f;
        public float despawnRadius = 80.0f;
        public float offscreenFarRadius = 48.0f;
        public float offscreenDespawnSeconds = 10.0f;

        [Header("Runtime Options & Diagnostics")]
        public bool showDebugOverlay;
        public bool hideUnconvertedLaneBlockersInPlayMode = true;

        [Header("Stage D Polish (Traffic Lights & Hazard Flashers)")]
        public List<TrafficSignalPoleBinding> signalPoles = new List<TrafficSignalPoleBinding>();
        public List<GameObject> parkedHazardFlashers = new List<GameObject>();
        public float hazardBlinkInterval = 0.45f;

        [SerializeField] private float _averageStepTimeMs;
        [SerializeField] private int _activeCount;
        [SerializeField] private int _totalSpawnCount;
        [SerializeField] private int _totalDespawnCount;

        private readonly List<TrafficVehicleController> _pool = new List<TrafficVehicleController>(16);
        private readonly List<TrafficVehicleController> _activeVehicles = new List<TrafficVehicleController>(16);
        private readonly List<int> _candidateEdges = new List<int>(128);
        private readonly Plane[] _frustumPlanes = new Plane[6];

        private System.Random _rng;
        private Transform _playerTransform;
        private Camera _mainCamera;
        private float _spawnMaintainTimer;
        private float _simulationTime;
        private bool _lastHazardFlashState;
        private System.Diagnostics.Stopwatch _perfWatch;

        private static MaterialPropertyBlock _mpbGreenLit;
        private static MaterialPropertyBlock _mpbYellowLit;
        private static MaterialPropertyBlock _mpbRedLit;
        private static MaterialPropertyBlock _mpbBulbOff;
        private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");

        public List<TrafficVehicleController> ActiveVehicles => _activeVehicles;
        public List<TrafficVehicleController> AllPooledVehicles => _pool;
        public float AverageStepTimeMs => _averageStepTimeMs;
        public int TotalSpawnCount => _totalSpawnCount;
        public int TotalDespawnCount => _totalDespawnCount;
        public float SimulationTime => _simulationTime;

        private void Awake()
        {
            if (!IsInAllowedScene())
            {
                enabled = false;
                gameObject.SetActive(false);
                return;
            }

            Instance = this;
            _rng = new System.Random(randomSeed);
            _perfWatch = new System.Diagnostics.Stopwatch();
            _simulationTime = 0f;

            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            ConfigureTrafficLayerAndMinimap();
            EnableVehicleMaterialInstancing();

            if (graph != null)
            {
                graph.ResetRuntimeState();
            }

            if (Application.isPlaying && hideUnconvertedLaneBlockersInPlayMode)
            {
                SuppressStaticLaneAndCrosswalkBlockersAtRuntime();
            }

            BuildInitialPool();
            UpdateStageDVisuals(0f, forceRefresh: true);
        }

        private void OnEnable()
        {
            if (!IsInAllowedScene())
            {
                enabled = false;
                gameObject.SetActive(false);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            if (!IsInAllowedScene()) return;
            ResolvePlayerAndCamera();
            PopulateInitialTraffic(ignoreFrustum: false);
        }

        private void OnDisable()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            StopAllCoroutines();
            if (graph != null) graph.ResetRuntimeState();
            if (Instance == this) Instance = null;
        }

        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (graph != null) graph.ResetRuntimeState();
            if (Instance == this) Instance = null;
        }

        private void OnSceneUnloaded(Scene unloaded)
        {
            if (graph != null) graph.ResetRuntimeState();
            if (Instance == this) Instance = null;
        }

        private static bool IsInAllowedScene()
        {
            Scene active = SceneManager.GetActiveScene();
            return active.IsValid() && active.path == AllowedScenePath;
        }

        public float NextRandom01()
        {
            if (_rng == null) _rng = new System.Random(randomSeed);
            return (float)_rng.NextDouble();
        }

        public Transform GetPlayerTransform()
        {
            if (_playerTransform == null) ResolvePlayerAndCamera();
            return _playerTransform;
        }

        private void ResolvePlayerAndCamera()
        {
            if (_playerTransform == null)
            {
                GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
                if (playerGo != null)
                {
                    _playerTransform = playerGo.transform;
                }
                else
                {
                    CharacterController cc = FindFirstObjectByType<CharacterController>();
                    if (cc != null) _playerTransform = cc.transform;
                }
            }
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }
        }

        public void ConfigureTrafficLayerAndMinimap()
        {
            int trafficLayer = LayerMask.NameToLayer("Traffic");
            if (trafficLayer >= 0)
            {
                if (Application.isPlaying)
                {
                    // Disable Traffic vs Traffic physics collisions at runtime; keep Traffic vs Default (0) and Player enabled
                    Physics.IgnoreLayerCollision(trafficLayer, trafficLayer, true);
                    Physics.IgnoreLayerCollision(trafficLayer, 0, false);
                    int playerLayer = LayerMask.NameToLayer("Player");
                    if (playerLayer >= 0)
                    {
                        Physics.IgnoreLayerCollision(trafficLayer, playerLayer, false);
                    }
                }

                // Add Traffic layer to MinimapCamera culling mask if present in City Scene
                GameObject minimapCamObj = GameObject.Find("MinimapCamera");
                if (minimapCamObj != null)
                {
                    Camera miniCam = minimapCamObj.GetComponent<Camera>();
                    if (miniCam != null)
                    {
                        miniCam.cullingMask |= (1 << trafficLayer);
                    }
                }
            }
        }

        public void EnableVehicleMaterialInstancing()
        {
            if (!Application.isPlaying) return;
            for (int i = 0; i < fleetPrefabs.Count; i++)
            {
                if (fleetPrefabs[i] == null) continue;
                MeshRenderer[] mrs = fleetPrefabs[i].GetComponentsInChildren<MeshRenderer>(true);
                for (int j = 0; j < mrs.Length; j++)
                {
                    Material[] mats = mrs[j].sharedMaterials;
                    for (int k = 0; k < mats.Length; k++)
                    {
                        if (mats[k] != null && !mats[k].enableInstancing)
                        {
                            mats[k].enableInstancing = true;
                        }
                    }
                }
            }
        }

        private void SuppressStaticLaneAndCrosswalkBlockersAtRuntime()
        {
            if (graph == null || graph.edges.Count == 0) return;
            GameObject vehiclesRoot = GameObject.Find("Vehicles");
            if (vehiclesRoot == null) return;

            int childCount = vehiclesRoot.transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform v = vehiclesRoot.transform.GetChild(i);
                if (v == null || !v.gameObject.activeSelf) continue;
                Vector3 p = v.position;
                // Ignore Highway (x > 75) and high-altitude planes/balloons
                if (p.x > 75f || p.y > 1.5f || p.y < -1.0f) continue;
                string n = v.name;
                if (n.StartsWith("Plane", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("Train", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("AirBalloon", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Check if this static vehicle sits within 2.1m of any active graph lane segment
                if (IsPositionBlockingAnyGraphLane(p, 2.15f))
                {
                    v.gameObject.SetActive(false);
                }
            }
        }

        public bool IsPositionBlockingAnyGraphLane(Vector3 pos, float threshold)
        {
            if (graph == null) return false;
            float threshSq = threshold * threshold;
            Vector2 p2 = new Vector2(pos.x, pos.z);

            for (int i = 0; i < graph.edges.Count; i++)
            {
                TrafficEdge e = graph.edges[i];
                if (e.waypoints == null || e.waypoints.Length < 2) continue;
                for (int j = 0; j < e.waypoints.Length - 1; j++)
                {
                    Vector2 a = new Vector2(e.waypoints[j].x, e.waypoints[j].z);
                    Vector2 b = new Vector2(e.waypoints[j + 1].x, e.waypoints[j + 1].z);
                    float dSq = DistancePointToSegmentSqr(p2, a, b);
                    if (dSq <= threshSq) return true;
                }
            }
            return false;
        }

        private static float DistancePointToSegmentSqr(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 proj = a + ab * t;
            return (p - proj).sqrMagnitude;
        }

        public void BuildInitialPool()
        {
            // Clean up any pre-existing child vehicles
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform ch = transform.GetChild(i);
                var tv = ch.GetComponent<TrafficVehicleController>();
                if (tv != null)
                {
                    if (Application.isPlaying) Destroy(ch.gameObject);
                    else DestroyImmediate(ch.gameObject);
                }
            }

            _pool.Clear();
            _activeVehicles.Clear();

            if (fleetPrefabs == null || fleetPrefabs.Count == 0) return;

            int poolCapacity = Mathf.Max(12, activeCarTarget + 2);
            for (int i = 0; i < poolCapacity; i++)
            {
                GameObject prefab = fleetPrefabs[i % fleetPrefabs.Count];
                if (prefab == null) continue;

                GameObject inst = Instantiate(prefab, transform);
                inst.name = $"TrafficCar_{i:D2}_{prefab.name}";
                inst.SetActive(false);

                TrafficVehicleController ctrl = inst.GetComponent<TrafficVehicleController>();
                if (ctrl == null) ctrl = inst.AddComponent<TrafficVehicleController>();
                ctrl.vehicleId = i + 1;
                ctrl.sourceModelName = prefab.name;
                ctrl.sensorLength = sensorLength;
                ctrl.EnsurePhysicsAndWheels();

                _pool.Add(ctrl);
            }
        }

        public void PopulateInitialTraffic(bool ignoreFrustum)
        {
            if (graph == null || graph.edges.Count == 0) return;
            if (_pool.Count == 0) BuildInitialPool();

            int target = Mathf.Clamp(activeCarTarget, 6, 12);
            int attempts = 0;
            while (_activeVehicles.Count < target && attempts < 60)
            {
                attempts++;
                if (!TrySpawnOneVehicle(ignoreFrustum))
                {
                    // Fallback if camera frustum is wide: allow spawning slightly outside strict frustum check on initial fill
                    if (!ignoreFrustum) TrySpawnOneVehicle(ignoreFrustum: true);
                }
            }
            _activeCount = _activeVehicles.Count;
        }

        private void Update()
        {
            if (Time.timeScale <= 0.0001f) return;

            if (_perfWatch == null) _perfWatch = new System.Diagnostics.Stopwatch();
            _perfWatch.Restart();
            float dt = Time.deltaTime;
            _simulationTime += dt;

            _spawnMaintainTimer -= dt;
            if (_spawnMaintainTimer <= 0f)
            {
                _spawnMaintainTimer = 0.25f;
                MaintainPoolAndCulling(0.25f, useCameraFrustum: true);
            }

            UpdateStageDVisuals(_simulationTime, forceRefresh: false);

            _perfWatch.Stop();
            float elapsedMs = (float)_perfWatch.Elapsed.TotalMilliseconds;
            _averageStepTimeMs = Mathf.Lerp(_averageStepTimeMs, elapsedMs, 0.1f);
            _activeCount = _activeVehicles.Count;
        }

        public void StepSimulationManual(float dt, bool useCameraFrustum = false)
        {
            if (_perfWatch == null) _perfWatch = new System.Diagnostics.Stopwatch();
            _perfWatch.Restart();
            _simulationTime += dt;

            for (int i = 0; i < _activeVehicles.Count; i++)
            {
                var v = _activeVehicles[i];
                if (v != null && v.gameObject.activeSelf)
                {
                    v.StepSimulation(dt);
                }
            }

            _spawnMaintainTimer -= dt;
            if (_spawnMaintainTimer <= 0f)
            {
                _spawnMaintainTimer = 0.25f;
                MaintainPoolAndCulling(0.25f, useCameraFrustum);
            }

            UpdateStageDVisuals(_simulationTime, forceRefresh: false);

            _perfWatch.Stop();
            float elapsedMs = (float)_perfWatch.Elapsed.TotalMilliseconds;
            _averageStepTimeMs = _averageStepTimeMs <= 0.0001f ? elapsedMs : Mathf.Lerp(_averageStepTimeMs, elapsedMs, 0.05f);
            _activeCount = _activeVehicles.Count;
        }

        private void MaintainPoolAndCulling(float checkInterval, bool useCameraFrustum)
        {
            ResolvePlayerAndCamera();
            Vector3 playerPos = _playerTransform != null ? _playerTransform.position : Vector3.zero;
            bool hasCam = useCameraFrustum && _mainCamera != null;
            if (hasCam)
            {
                GeometryUtility.CalculateFrustumPlanes(_mainCamera, _frustumPlanes);
            }

            float despawnSq = despawnRadius * despawnRadius;
            float farSq = offscreenFarRadius * offscreenFarRadius;

            for (int i = _activeVehicles.Count - 1; i >= 0; i--)
            {
                TrafficVehicleController v = _activeVehicles[i];
                if (v == null || !v.gameObject.activeSelf)
                {
                    _activeVehicles.RemoveAt(i);
                    continue;
                }

                Vector3 diff = v.transform.position - playerPos;
                diff.y = 0f;
                float distSq = diff.sqrMagnitude;

                if (distSq > despawnSq)
                {
                    DespawnVehicleAt(i);
                    continue;
                }

                if (hasCam && distSq > farSq)
                {
                    Bounds b = new Bounds(v.transform.position + Vector3.up * 0.8f, new Vector3(2.5f, 2.0f, 4.5f));
                    if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, b))
                    {
                        v.OffscreenFarTimer += checkInterval;
                        if (v.OffscreenFarTimer >= offscreenDespawnSeconds)
                        {
                            DespawnVehicleAt(i);
                            continue;
                        }
                    }
                    else
                    {
                        v.OffscreenFarTimer = 0f;
                    }
                }
                else
                {
                    v.OffscreenFarTimer = 0f;
                }
            }

            int target = Mathf.Clamp(activeCarTarget, 6, 12);
            int spawnTries = 0;
            while (_activeVehicles.Count < target && spawnTries < 8)
            {
                spawnTries++;
                if (!TrySpawnOneVehicle(ignoreFrustum: !hasCam))
                {
                    TrySpawnOneVehicle(ignoreFrustum: true);
                }
            }
        }

        private void DespawnVehicleAt(int activeListIndex)
        {
            TrafficVehicleController v = _activeVehicles[activeListIndex];
            _activeVehicles.RemoveAt(activeListIndex);
            if (v != null)
            {
                if (graph != null) graph.ReleaseAllForVehicle(v.vehicleId);
                v.gameObject.SetActive(false);
                _totalDespawnCount++;
            }
        }

        public bool TrySpawnOneVehicle(bool ignoreFrustum)
        {
            if (graph == null || graph.edges.Count == 0) return false;

            TrafficVehicleController freeCar = null;
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null && !_pool[i].gameObject.activeSelf)
                {
                    freeCar = _pool[i];
                    break;
                }
            }
            if (freeCar == null) return false;

            ResolvePlayerAndCamera();
            Vector3 playerPos = _playerTransform != null ? _playerTransform.position : Vector3.zero;
            bool checkFrustum = !ignoreFrustum && _mainCamera != null;
            if (checkFrustum)
            {
                GeometryUtility.CalculateFrustumPlanes(_mainCamera, _frustumPlanes);
            }

            _candidateEdges.Clear();
            for (int i = 0; i < graph.edges.Count; i++)
            {
                TrafficEdge e = graph.edges[i];
                // Spawn only on straight corridor edges (never inside an intersection arc or turn curve)
                if (e.isIntersectionArc || e.isCurve || e.length < 10.0f) continue;

                // Sample midpoint of edge
                e.SampleAtDistance(e.length * 0.45f, out Vector3 midPos, out _);
                Vector3 toPlayer = midPos - playerPos;
                toPlayer.y = 0f;
                float dist = toPlayer.magnitude;
                if (dist < minSpawnRadius || dist > maxSpawnRadius) continue;

                _candidateEdges.Add(i);
            }

            if (_candidateEdges.Count == 0) return false;

            int count = _candidateEdges.Count;
            int startIdx = _rng != null ? _rng.Next(count) : UnityEngine.Random.Range(0, count);

            for (int step = 0; step < count; step++)
            {
                int edgeId = _candidateEdges[(startIdx + step) % count];
                TrafficEdge edge = graph.edges[edgeId];

                // Pick a safe distance along the corridor away from intersection entry/exit & crosswalks
                float minD = 3.5f;
                float maxD = Mathf.Max(minD + 0.5f, edge.length - 7.5f);
                if (edge.stopLineDistanceOnEdge > 0f)
                {
                    maxD = Mathf.Min(maxD, edge.stopLineDistanceOnEdge - 3.0f);
                }
                if (maxD <= minD) continue;

                float spawnDist = Mathf.Lerp(minD, maxD, NextRandom01());
                edge.SampleAtDistance(spawnDist, out Vector3 spawnPos, out _);
                spawnPos.y = 0f;

                float playerDist = Vector2.Distance(new Vector2(spawnPos.x, spawnPos.z), new Vector2(playerPos.x, playerPos.z));
                if (playerDist < minSpawnRadius || playerDist > maxSpawnRadius) continue;

                if (checkFrustum)
                {
                    Bounds b = new Bounds(spawnPos + Vector3.up * 0.8f, new Vector3(3.0f, 2.2f, 5.0f));
                    if (GeometryUtility.TestPlanesAABB(_frustumPlanes, b)) continue;
                }

                // Ensure not inside any crosswalk zone or intersection box
                if (IsInsideAnyCrosswalkOrIntersection(spawnPos)) continue;

                // Ensure >= 9m clear of any active vehicle
                bool tooCloseToOtherCar = false;
                for (int j = 0; j < _activeVehicles.Count; j++)
                {
                    var other = _activeVehicles[j];
                    if (other == null || !other.gameObject.activeSelf) continue;
                    if ((other.transform.position - spawnPos).sqrMagnitude < 81f) // 9m^2
                    {
                        tooCloseToOtherCar = true;
                        break;
                    }
                }
                if (tooCloseToOtherCar) continue;

                float speed = Mathf.Lerp(minSpeed, maxSpeed, NextRandom01());
                freeCar.sensorLength = sensorLength;
                freeCar.gameObject.SetActive(true);
                freeCar.InitializeOnEdge(this, graph, edgeId, spawnDist, speed);
                _activeVehicles.Add(freeCar);
                _totalSpawnCount++;
                return true;
            }

            return false;
        }

        public void UpdateStageDVisuals(float now, bool forceRefresh)
        {
            // 1. Update Polyperfect traffic light bulbs on central 4-way intersections via MaterialPropertyBlock
            if (graph != null && signalPoles != null && signalPoles.Count > 0)
            {
                EnsureSignalPropertyBlocks();
                for (int i = 0; i < signalPoles.Count; i++)
                {
                    TrafficSignalPoleBinding binding = signalPoles[i];
                    if (binding == null || binding.poleRenderer == null) continue;

                    int stateInt = (int)graph.GetTrafficLightState(binding.intersectionIndex, binding.travelDir, now);
                    if (!forceRefresh && binding.lastAppliedState == stateInt) continue;
                    binding.lastAppliedState = stateInt;

                    // Submesh 2 = Green, Submesh 3 = Yellow/Orange, Submesh 4 = Red
                    binding.poleRenderer.SetPropertyBlock(stateInt == 0 ? _mpbGreenLit : _mpbBulbOff, 2);
                    binding.poleRenderer.SetPropertyBlock(stateInt == 1 ? _mpbYellowLit : _mpbBulbOff, 3);
                    binding.poleRenderer.SetPropertyBlock(stateInt == 2 ? _mpbRedLit : _mpbBulbOff, 4);
                }
            }

            // 2. Update hazard flashers on parked roadside cars (~1.1 Hz)
            if (parkedHazardFlashers != null && parkedHazardFlashers.Count > 0)
            {
                bool flashOn = Mathf.FloorToInt(now / Mathf.Max(0.15f, hazardBlinkInterval)) % 2 == 0;
                if (forceRefresh || flashOn != _lastHazardFlashState)
                {
                    _lastHazardFlashState = flashOn;
                    for (int i = 0; i < parkedHazardFlashers.Count; i++)
                    {
                        if (parkedHazardFlashers[i] != null && parkedHazardFlashers[i].activeSelf != flashOn)
                        {
                            parkedHazardFlashers[i].SetActive(flashOn);
                        }
                    }
                }
            }
        }

        private static void EnsureSignalPropertyBlocks()
        {
            if (_mpbGreenLit != null) return;

            _mpbGreenLit = new MaterialPropertyBlock();
            Color greenCol = new Color(0.15f, 1.85f, 0.25f, 1f);
            _mpbGreenLit.SetColor(BaseColorPropId, greenCol);
            _mpbGreenLit.SetColor(ColorPropId, greenCol);

            _mpbYellowLit = new MaterialPropertyBlock();
            Color yellowCol = new Color(2.1f, 1.45f, 0.10f, 1f);
            _mpbYellowLit.SetColor(BaseColorPropId, yellowCol);
            _mpbYellowLit.SetColor(ColorPropId, yellowCol);

            _mpbRedLit = new MaterialPropertyBlock();
            Color redCol = new Color(2.1f, 0.15f, 0.15f, 1f);
            _mpbRedLit.SetColor(BaseColorPropId, redCol);
            _mpbRedLit.SetColor(ColorPropId, redCol);

            _mpbBulbOff = new MaterialPropertyBlock();
            Color offCol = new Color(0.08f, 0.08f, 0.09f, 1f);
            _mpbBulbOff.SetColor(BaseColorPropId, offCol);
            _mpbBulbOff.SetColor(ColorPropId, offCol);
        }

        private bool IsInsideAnyCrosswalkOrIntersection(Vector3 pos)
        {
            if (graph == null) return false;
            for (int i = 0; i < graph.intersections.Count; i++)
            {
                var inter = graph.intersections[i];
                if (Mathf.Abs(pos.x - inter.center.x) < 7.0f && Mathf.Abs(pos.z - inter.center.z) < 7.0f)
                    return true;
            }
            for (int i = 0; i < graph.crosswalkZones.Count; i++)
            {
                var cw = graph.crosswalkZones[i];
                if ((pos - cw.center).sqrMagnitude < 16.0f)
                    return true;
            }
            return false;
        }

        private void OnGUI()
        {
            if (!showDebugOverlay || !Application.isPlaying) return;

            GUIStyle boxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft
            };
            Rect rect = new Rect(16f, 16f, 360f, 115f);
            string info = $"<b>[City Traffic Debug Overlay]</b>\n" +
                          $"Active Cars: {_activeVehicles.Count} / {activeCarTarget} (Pool: {_pool.Count})\n" +
                          $"Spawns: {_totalSpawnCount} | Despawns: {_totalDespawnCount}\n" +
                          $"Manager CPU: {_averageStepTimeMs:F3} ms (Budget < 0.300 ms)\n" +
                          $"Nodes: {(graph != null ? graph.nodes.Count : 0)} | Edges: {(graph != null ? graph.edges.Count : 0)}";
            GUI.Box(rect, info, boxStyle);
        }

        private void OnDrawGizmos()
        {
            if (!showDebugOverlay || graph == null) return;

            // 1. Draw Directed Lane Edges and Arrows
            for (int i = 0; i < graph.edges.Count; i++)
            {
                TrafficEdge e = graph.edges[i];
                if (e.waypoints == null || e.waypoints.Length < 2) continue;

                if (e.isIntersectionArc)
                {
                    Gizmos.color = e.turnType == TrafficTurnType.Straight
                        ? new Color(0.2f, 0.85f, 1.0f, 0.85f)
                        : (e.turnType == TrafficTurnType.Right ? new Color(0.3f, 1.0f, 0.4f, 0.85f) : new Color(1.0f, 0.75f, 0.2f, 0.85f));
                }
                else
                {
                    Gizmos.color = e.isParkDistrict ? new Color(0.4f, 1.0f, 0.5f, 0.9f) : new Color(0.15f, 0.95f, 0.35f, 0.9f);
                }

                for (int j = 0; j < e.waypoints.Length - 1; j++)
                {
                    Vector3 a = e.waypoints[j] + Vector3.up * 0.25f;
                    Vector3 b = e.waypoints[j + 1] + Vector3.up * 0.25f;
                    Gizmos.DrawLine(a, b);
                }

                // Direction chevron at midpoint
                e.SampleAtDistance(e.length * 0.5f, out Vector3 mid, out Vector3 tan);
                mid += Vector3.up * 0.28f;
                Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
                Gizmos.DrawLine(mid, mid - tan * 1.1f + right * 0.45f);
                Gizmos.DrawLine(mid, mid - tan * 1.1f - right * 0.45f);
            }

            // 2. Draw Intersection Conflict Zones & Traffic Light Phase
            for (int i = 0; i < graph.intersections.Count; i++)
            {
                var inter = graph.intersections[i];
                int occ = graph.GetActiveOccupantCount(i);
                Gizmos.color = occ > 0 ? new Color(1f, 0.25f, 0.2f, 0.45f) : new Color(1f, 0.85f, 0.1f, 0.25f);
                Gizmos.DrawWireCube(inter.center + Vector3.up * 0.5f, new Vector3(12f, 1.0f, 12f));

                if (inter.hasTrafficLight)
                {
                    var nsState = graph.GetTrafficLightState(i, 0, _simulationTime);
                    var ewState = graph.GetTrafficLightState(i, 1, _simulationTime);
                    Gizmos.color = nsState == TrafficLightPhaseState.Green ? Color.green : (nsState == TrafficLightPhaseState.Yellow ? Color.yellow : Color.red);
                    Gizmos.DrawSphere(inter.center + new Vector3(0f, 3.8f, 4.5f), 0.55f);
                    Gizmos.DrawSphere(inter.center + new Vector3(0f, 3.8f, -4.5f), 0.55f);
                    Gizmos.color = ewState == TrafficLightPhaseState.Green ? Color.green : (ewState == TrafficLightPhaseState.Yellow ? Color.yellow : Color.red);
                    Gizmos.DrawSphere(inter.center + new Vector3(4.5f, 3.8f, 0f), 0.55f);
                    Gizmos.DrawSphere(inter.center + new Vector3(-4.5f, 3.8f, 0f), 0.55f);
                }
            }

            // 3. Draw Merged Crosswalk Zones & Stop Lines
            for (int i = 0; i < graph.crosswalkZones.Count; i++)
            {
                var cw = graph.crosswalkZones[i];
                Matrix4x4 oldMat = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(cw.center + Vector3.up * 0.2f, cw.rotation, Vector3.one);
                Gizmos.color = new Color(0.2f, 0.6f, 1.0f, 0.4f);
                Gizmos.DrawWireCube(Vector3.zero, cw.halfExtents * 2f);
                Gizmos.matrix = oldMat;

                Gizmos.color = Color.red;
                Gizmos.DrawSphere(cw.stopLinePosition + Vector3.up * 0.3f, 0.4f);
            }

            // 4. Draw Active Vehicle Sensors
            for (int i = 0; i < _activeVehicles.Count; i++)
            {
                var v = _activeVehicles[i];
                if (v == null || !v.gameObject.activeSelf) continue;
                Gizmos.color = v.IsBlockedByPlayer ? Color.red : (v.CurrentSpeed < 0.5f ? Color.yellow : Color.cyan);
                Vector3 start = v.transform.position + Vector3.up * 0.7f + v.transform.forward * (v.bodyLength * 0.5f);
                Gizmos.DrawLine(start, start + v.transform.forward * v.sensorLength);
                Gizmos.DrawWireSphere(start + v.transform.forward * v.sensorLength, v.sensorRadius);
            }
        }
    }
}
