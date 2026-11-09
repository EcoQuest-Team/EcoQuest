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

        [Header("Graph & Pool Budgets (Near + Far Activity Bands)")]
        public CityTrafficGraph graph;
        [Range(6, 12)] public int activeCarTarget = 10; // Near band (0-80m) budget
        [Range(8, 24)] public int farCarBudget = 22;    // Far band (80-260m / Always-Visible) budget
        public int randomSeed = 20261108;
        public List<GameObject> fleetPrefabs = new List<GameObject>();
        public List<TrafficVehicleController> sceneCityVehicles = new List<TrafficVehicleController>();

        [Header("Speed & Sensor Tuning")]
        public float minSpeed = 4.0f;
        public float maxSpeed = 6.5f;
        public float parkSpeed = 3.5f;
        public float curveSpeed = 3.0f;
        public float sensorLength = 5.0f;
        public float highwayMinSpeed = 8.0f;
        public float highwayMaxSpeed = 11.0f;

        [Header("Near & Far Activity Band Radii")]
        public float minSpawnRadius = 18.0f;
        public float maxSpawnRadius = 75.0f;
        public float despawnRadius = 80.0f;   // Near band outer radius (80m)
        public float maxFarRadius = 260.0f;   // Far band outer radius (260m)
        public float offscreenFarRadius = 48.0f;
        public float offscreenDespawnSeconds = 12.0f;

        [Header("Extended Traffic Controllers (Highway, Rail, Air)")]
        public List<TrafficVehicleController> highwayVehicles = new List<TrafficVehicleController>();
        public TrafficTrainController trainController;
        public RailCrossingController railCrossingController;
        public List<TrafficPlaneController> planeControllers = new List<TrafficPlaneController>();
        public TrafficBalloonController balloonController;

        [Header("Runtime Options & Diagnostics")]
        public bool showDebugOverlay;
        public bool hideUnconvertedLaneBlockersInPlayMode = true;

        [Header("Stage D Polish (Traffic Lights & Hazard Flashers)")]
        public List<TrafficSignalPoleBinding> signalPoles = new List<TrafficSignalPoleBinding>();
        public List<GameObject> parkedHazardFlashers = new List<GameObject>();
        public float hazardBlinkInterval = 0.45f;

        [SerializeField] private float _averageStepTimeMs;
        [SerializeField] private int _activeCount;
        [SerializeField] private int _nearBandCount;
        [SerializeField] private int _farBandCount;
        [SerializeField] private int _totalSpawnCount;
        [SerializeField] private int _totalDespawnCount;

        private readonly List<TrafficVehicleController> _pool = new List<TrafficVehicleController>(32);
        private readonly List<TrafficVehicleController> _activeVehicles = new List<TrafficVehicleController>(32);
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
        public List<TrafficVehicleController> HighwayVehicles => highwayVehicles;
        public float AverageStepTimeMs => _averageStepTimeMs;
        public int NearBandCount => _nearBandCount;
        public int FarBandCount => _farBandCount;
        public int TotalSpawnCount => _totalSpawnCount;
        public int TotalDespawnCount => _totalDespawnCount;
        public float SimulationTime => _simulationTime;

        public bool IsRailCrossingActive()
        {
            return railCrossingController != null && railCrossingController.IsCrossingActive;
        }

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
            InitializeHighwayAndSpecialVehicles();
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
            ConfigureTrafficLayerAndMinimap();
        }

        private void Start()
        {
            if (!IsInAllowedScene()) return;
            ResolvePlayerAndCamera();
            ConfigureTrafficLayerAndMinimap();
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
                int layerBit = 1 << trafficLayer;
                if (Application.isPlaying)
                {
                    // Enable Traffic vs Traffic collisions; keep Traffic vs Default (0) and Player enabled
                    Physics.IgnoreLayerCollision(trafficLayer, trafficLayer, false);
                    Physics.IgnoreLayerCollision(trafficLayer, 0, false);
                    int playerLayer = LayerMask.NameToLayer("Player");
                    if (playerLayer >= 0)
                    {
                        Physics.IgnoreLayerCollision(trafficLayer, playerLayer, false);
                    }
                }

                // Ensure MainCamera and all scene Cameras include the Traffic layer in their cullingMask
                // (Root cause fix for Step 1: MainCamera had custom mask 0x00000237 which excluded Layer 12!)
                Camera[] allCams = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < allCams.Length; i++)
                {
                    if (allCams[i] != null && allCams[i].gameObject.scene.IsValid())
                    {
                        allCams[i].cullingMask |= layerBit;
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
                // Do not suppress any vehicle that already has an active controller
                if (v.GetComponent<TrafficVehicleController>() != null ||
                    v.GetComponent<TrafficTrainController>() != null ||
                    v.GetComponent<TrafficPlaneController>() != null)
                {
                    continue;
                }

                Vector3 p = v.position;
                if (p.x > 75f || p.y > 1.5f || p.y < -1.0f) continue;

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
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform ch = transform.GetChild(i);
                var tv = ch.GetComponent<TrafficVehicleController>();
                if (tv != null && !tv.isHighwayVehicle)
                {
                    if (Application.isPlaying) Destroy(ch.gameObject);
                    else DestroyImmediate(ch.gameObject);
                }
            }

            _pool.Clear();
            _activeVehicles.Clear();

            // Prefer using the 32 converted scene city vehicles directly so every converted vehicle in Vehicles/ actively drives
            if (sceneCityVehicles != null && sceneCityVehicles.Count > 0)
            {
                for (int i = 0; i < sceneCityVehicles.Count; i++)
                {
                    TrafficVehicleController ctrl = sceneCityVehicles[i];
                    if (ctrl == null) continue;
                    ctrl.vehicleId = i + 1;
                    ctrl.isHighwayVehicle = false;
                    ctrl.sensorLength = sensorLength;
                    ctrl.EnsurePhysicsAndWheels();
                    ctrl.gameObject.SetActive(false);
                    _pool.Add(ctrl);
                }
                if (_pool.Count > 0) return;
            }

            if (fleetPrefabs == null || fleetPrefabs.Count == 0) return;

            int nearTarget = Mathf.Clamp(activeCarTarget, 6, 12);
            int farTarget = Mathf.Clamp(farCarBudget, 8, 24);
            int poolCapacity = nearTarget + farTarget + 2;

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

        public void InitializeHighwayAndSpecialVehicles()
        {
            if (graph == null) return;

            // Initialize all highway vehicles on their matched highway lanes
            for (int i = 0; i < highwayVehicles.Count; i++)
            {
                TrafficVehicleController hv = highwayVehicles[i];
                if (hv == null) continue;
                hv.gameObject.SetActive(true);
                int laneIdx = hv.highwayLaneIndex;
                if (laneIdx < 0 || laneIdx >= graph.highwayLanes.Count)
                {
                    laneIdx = FindClosestHighwayLaneIndex(hv.transform.position.x);
                }
                float speed = Mathf.Lerp(highwayMinSpeed, highwayMaxSpeed, (i * 0.37f) % 1.0f);
                hv.InitializeOnHighwayLane(this, graph, laneIdx, hv.transform.position.z, speed);
            }

            if (trainController == null)
            {
                trainController = FindFirstObjectByType<TrafficTrainController>(FindObjectsInactive.Include);
            }
            if (trainController != null)
            {
                trainController.EnsureSetup();
            }

            if (railCrossingController == null)
            {
                railCrossingController = FindFirstObjectByType<RailCrossingController>(FindObjectsInactive.Include);
            }
            if (railCrossingController != null)
            {
                railCrossingController.ResolveReferences();
            }

            if (planeControllers == null || planeControllers.Count == 0)
            {
                planeControllers = new List<TrafficPlaneController>(FindObjectsByType<TrafficPlaneController>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            }
            for (int i = 0; i < planeControllers.Count; i++)
            {
                if (planeControllers[i] != null) planeControllers[i].EnsureSetup();
            }

            if (balloonController == null)
            {
                balloonController = FindFirstObjectByType<TrafficBalloonController>(FindObjectsInactive.Include);
            }
            if (balloonController != null)
            {
                balloonController.EnsureSetup();
            }
        }

        public int FindClosestHighwayLaneIndex(float worldX)
        {
            if (graph == null || graph.highwayLanes.Count == 0) return -1;
            int best = 0;
            float bestDiff = float.MaxValue;
            for (int i = 0; i < graph.highwayLanes.Count; i++)
            {
                float d = Mathf.Abs(graph.highwayLanes[i].laneX - worldX);
                if (d < bestDiff)
                {
                    bestDiff = d;
                    best = i;
                }
            }
            return best;
        }

        public void PopulateInitialTraffic(bool ignoreFrustum)
        {
            if (graph == null || graph.edges.Count == 0) return;
            if (_pool.Count == 0) BuildInitialPool();

            int nearTarget = Mathf.Clamp(activeCarTarget, 6, 12);
            int farTarget = Mathf.Clamp(farCarBudget, 8, 22);

            // 1. Populate Near Band (15m - 80m)
            int attempts = 0;
            while (_nearBandCount < nearTarget && attempts < 80)
            {
                attempts++;
                if (TrySpawnVehicleInBand(TrafficSimulationBand.Near, ignoreFrustum: true))
                {
                    _nearBandCount++;
                }
            }

            // 2. Populate Far Band (80m - 260m)
            attempts = 0;
            while (_farBandCount < farTarget && attempts < 140)
            {
                attempts++;
                if (TrySpawnVehicleInBand(TrafficSimulationBand.Far, ignoreFrustum: true))
                {
                    _farBandCount++;
                }
            }

            // Distribute remaining pooled vehicles only onto unoccupied straight city corridors
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null && !_pool[i].gameObject.activeSelf)
                {
                    TrySpawnVehicleAnywhereOnCityCorridors(_pool[i]);
                }
            }

            RecountBands();
        }

        private bool TrySpawnVehicleAnywhereOnCityCorridors(TrafficVehicleController freeCar)
        {
            if (freeCar == null || graph == null || graph.edges.Count == 0) return false;
            ResolvePlayerAndCamera();
            Vector3 playerPos = _playerTransform != null ? _playerTransform.position : Vector3.zero;

            int edgeCount = graph.edges.Count;
            int offset = (freeCar.vehicleId * 11) % edgeCount;
            for (int step = 0; step < edgeCount; step++)
            {
                int i = (offset + step) % edgeCount;
                TrafficEdge edge = graph.edges[i];
                if (edge.isIntersectionArc || edge.isCurve || edge.length < 18.0f) continue;

                // Check if edge is already occupied by any active vehicle
                bool edgeOccupied = false;
                for (int j = 0; j < _activeVehicles.Count; j++)
                {
                    var other = _activeVehicles[j];
                    if (other != null && other.gameObject.activeSelf && other.CurrentEdgeId == i)
                    {
                        edgeOccupied = true;
                        break;
                    }
                }
                if (edgeOccupied) continue;

                float spawnDist = edge.length * 0.45f;
                edge.SampleAtDistance(spawnDist, out Vector3 spawnPos, out _);
                spawnPos.y = 0f;

                bool tooClose = false;
                for (int j = 0; j < _activeVehicles.Count; j++)
                {
                    var other = _activeVehicles[j];
                    if (other != null && other.gameObject.activeSelf &&
                        (other.transform.position - spawnPos).sqrMagnitude < 324f) // 18m
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                float playerDist = Vector2.Distance(new Vector2(spawnPos.x, spawnPos.z), new Vector2(playerPos.x, playerPos.z));
                TrafficSimulationBand band = playerDist <= despawnRadius ? TrafficSimulationBand.Near : TrafficSimulationBand.Far;
                float speed = Mathf.Lerp(minSpeed, maxSpeed, NextRandom01());
                freeCar.gameObject.SetActive(true);
                freeCar.simulationBand = band;
                freeCar.InitializeOnEdge(this, graph, i, spawnDist, speed, band);
                _activeVehicles.Add(freeCar);
                _totalSpawnCount++;
                return true;
            }
            return false;
        }

        private void RecountBands()
        {
            _nearBandCount = 0;
            _farBandCount = 0;
            for (int i = 0; i < _activeVehicles.Count; i++)
            {
                var v = _activeVehicles[i];
                if (v == null || !v.gameObject.activeSelf) continue;
                if (v.simulationBand == TrafficSimulationBand.Near) _nearBandCount++;
                else _farBandCount++;
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

            // Step City Grid Vehicles (Near + Far)
            for (int i = 0; i < _activeVehicles.Count; i++)
            {
                var v = _activeVehicles[i];
                if (v != null && v.gameObject.activeSelf)
                {
                    v.StepSimulation(dt);
                }
            }

            // Step Highway Vehicles
            for (int i = 0; i < highwayVehicles.Count; i++)
            {
                var hv = highwayVehicles[i];
                if (hv != null && hv.gameObject.activeSelf)
                {
                    hv.StepSimulation(dt);
                }
            }

            // Step Train & Rail Crossing
            if (trainController != null && trainController.gameObject.activeSelf)
            {
                trainController.StepSimulation(dt);
            }
            if (railCrossingController != null && railCrossingController.gameObject.activeSelf)
            {
                railCrossingController.StepSimulation(dt);
            }

            // Step Planes
            for (int i = 0; i < planeControllers.Count; i++)
            {
                if (planeControllers[i] != null && planeControllers[i].gameObject.activeSelf)
                {
                    planeControllers[i].StepSimulation(dt);
                }
            }

            // Step Hot Air Balloon
            if (balloonController != null && balloonController.gameObject.activeSelf)
            {
                balloonController.StepSimulation(dt);
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

            float nearSq = despawnRadius * despawnRadius;
            float maxFarSq = maxFarRadius * maxFarRadius;

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

                bool inFrustum = false;
                if (hasCam)
                {
                    Bounds b = new Bounds(v.transform.position + Vector3.up * 0.8f, new Vector3(3.0f, 2.2f, 5.0f));
                    inFrustum = GeometryUtility.TestPlanesAABB(_frustumPlanes, b);
                }

                // Despawn only if beyond maxFarRadius (260m) AND not visible in camera
                if (distSq > maxFarSq && !inFrustum)
                {
                    DespawnVehicleAt(i);
                    continue;
                }

                // Seamless Near <-> Far band switching without teleporting or popping
                if (distSq <= nearSq)
                {
                    v.simulationBand = TrafficSimulationBand.Near;
                    v.OffscreenFarTimer = 0f;
                }
                else
                {
                    v.simulationBand = TrafficSimulationBand.Far;
                    if (hasCam && !inFrustum)
                    {
                        v.OffscreenFarTimer += checkInterval;
                        if (v.OffscreenFarTimer >= offscreenDespawnSeconds && _farBandCount > farCarBudget)
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
            }

            RecountBands();

            int nearTarget = Mathf.Clamp(activeCarTarget, 6, 12);
            int farTarget = Mathf.Clamp(farCarBudget, 8, 20);

            int spawnTries = 0;
            while (_nearBandCount < nearTarget && spawnTries < 6)
            {
                spawnTries++;
                if (TrySpawnVehicleInBand(TrafficSimulationBand.Near, ignoreFrustum: !hasCam))
                {
                    _nearBandCount++;
                }
                else if (TrySpawnVehicleInBand(TrafficSimulationBand.Near, ignoreFrustum: true))
                {
                    _nearBandCount++;
                }
            }

            spawnTries = 0;
            while (_farBandCount < farTarget && spawnTries < 6)
            {
                spawnTries++;
                if (TrySpawnVehicleInBand(TrafficSimulationBand.Far, ignoreFrustum: true))
                {
                    _farBandCount++;
                }
            }

            RecountBands();
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
            return TrySpawnVehicleInBand(TrafficSimulationBand.Near, ignoreFrustum);
        }

        public bool TrySpawnVehicleInBand(TrafficSimulationBand band, bool ignoreFrustum)
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

            float minR = band == TrafficSimulationBand.Near ? minSpawnRadius : despawnRadius;
            float maxR = band == TrafficSimulationBand.Near ? despawnRadius : maxFarRadius;

            _candidateEdges.Clear();
            for (int i = 0; i < graph.edges.Count; i++)
            {
                TrafficEdge e = graph.edges[i];
                if (e.isIntersectionArc || e.isCurve || e.length < 10.0f) continue;

                e.SampleAtDistance(e.length * 0.45f, out Vector3 midPos, out _);
                Vector3 toPlayer = midPos - playerPos;
                toPlayer.y = 0f;
                float dist = toPlayer.magnitude;
                if (dist < minR || dist > maxR) continue;

                _candidateEdges.Add(i);
            }

            if (_candidateEdges.Count == 0) return false;

            int count = _candidateEdges.Count;
            int startIdx = _rng != null ? _rng.Next(count) : UnityEngine.Random.Range(0, count);

            for (int step = 0; step < count; step++)
            {
                int edgeId = _candidateEdges[(startIdx + step) % count];
                TrafficEdge edge = graph.edges[edgeId];

                float minD = 3.5f;
                float maxD = Mathf.Max(minD + 0.5f, edge.length - 7.5f);
                if (edge.stopLineDistanceOnEdge > 0f)
                {
                    maxD = Mathf.Min(maxD, edge.stopLineDistanceOnEdge - 3.0f);
                }
                if (edge.railCrossingStopLineDist > 0f)
                {
                    maxD = Mathf.Min(maxD, edge.railCrossingStopLineDist - 4.0f);
                }
                if (maxD <= minD) continue;

                float spawnDist = Mathf.Lerp(minD, maxD, NextRandom01());
                edge.SampleAtDistance(spawnDist, out Vector3 spawnPos, out _);
                spawnPos.y = 0f;

                float playerDist = Vector2.Distance(new Vector2(spawnPos.x, spawnPos.z), new Vector2(playerPos.x, playerPos.z));
                if (playerDist < minR || playerDist > maxR) continue;

                if (checkFrustum)
                {
                    Bounds b = new Bounds(spawnPos + Vector3.up * 0.8f, new Vector3(3.0f, 2.2f, 5.0f));
                    if (GeometryUtility.TestPlanesAABB(_frustumPlanes, b)) continue;
                }

                if (IsInsideAnyCrosswalkOrIntersection(spawnPos)) continue;

                // Enforce single occupancy per corridor edge on spawn
                bool edgeAlreadyOccupied = false;
                for (int j = 0; j < _activeVehicles.Count; j++)
                {
                    var other = _activeVehicles[j];
                    if (other != null && other.gameObject.activeSelf && other.CurrentEdgeId == edgeId)
                    {
                        edgeAlreadyOccupied = true;
                        break;
                    }
                }
                if (edgeAlreadyOccupied) continue;

                bool tooCloseToOtherCar = false;
                for (int j = 0; j < _activeVehicles.Count; j++)
                {
                    var other = _activeVehicles[j];
                    if (other == null || !other.gameObject.activeSelf) continue;
                    if ((other.transform.position - spawnPos).sqrMagnitude < 324f) // 18m
                    {
                        tooCloseToOtherCar = true;
                        break;
                    }
                }
                if (tooCloseToOtherCar) continue;

                float speed = Mathf.Lerp(minSpeed, maxSpeed, NextRandom01());
                freeCar.sensorLength = sensorLength;
                freeCar.gameObject.SetActive(true);
                freeCar.InitializeOnEdge(this, graph, edgeId, spawnDist, speed, band);
                _activeVehicles.Add(freeCar);
                _totalSpawnCount++;
                return true;
            }

            return false;
        }

        public void UpdateStageDVisuals(float now, bool forceRefresh)
        {
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

                    binding.poleRenderer.SetPropertyBlock(stateInt == 0 ? _mpbGreenLit : _mpbBulbOff, 2);
                    binding.poleRenderer.SetPropertyBlock(stateInt == 1 ? _mpbYellowLit : _mpbBulbOff, 3);
                    binding.poleRenderer.SetPropertyBlock(stateInt == 2 ? _mpbRedLit : _mpbBulbOff, 4);
                }
            }

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
            // Avoid spawning directly on the railway tracks around x = 72
            if (Mathf.Abs(pos.x - 72.0f) < 6.5f) return true;
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
            Rect rect = new Rect(16f, 16f, 390f, 130f);
            string info = $"<b>[City Traffic Debug Overlay]</b>\n" +
                          $"City Cars: {_activeVehicles.Count} (Near: {_nearBandCount}/{activeCarTarget}, Far: {_farBandCount}/{farCarBudget})\n" +
                          $"Highway Cars: {highwayVehicles.Count} | Planes: {planeControllers.Count} | Train & Balloon: Active\n" +
                          $"Spawns: {_totalSpawnCount} | Despawns: {_totalDespawnCount}\n" +
                          $"Manager CPU: {_averageStepTimeMs:F3} ms (Budget < 0.500 ms)";
            GUI.Box(rect, info, boxStyle);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showDebugOverlay || graph == null) return;

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

                e.SampleAtDistance(e.length * 0.5f, out Vector3 mid, out Vector3 tan);
                mid += Vector3.up * 0.28f;
                Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
                Gizmos.DrawLine(mid, mid - tan * 1.1f + right * 0.45f);
                Gizmos.DrawLine(mid, mid - tan * 1.1f - right * 0.45f);
            }

            // Draw Highway Lanes
            Gizmos.color = new Color(0.2f, 0.9f, 1.0f, 0.85f);
            for (int i = 0; i < graph.highwayLanes.Count; i++)
            {
                var hl = graph.highwayLanes[i];
                Vector3 a = new Vector3(hl.laneX, 0.3f, hl.startZ);
                Vector3 b = new Vector3(hl.laneX, 0.3f, hl.endZ);
                Gizmos.DrawLine(a, b);
            }
        }
#endif
    }
}
