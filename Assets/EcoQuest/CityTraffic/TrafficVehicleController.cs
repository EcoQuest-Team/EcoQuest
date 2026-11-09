using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace EcoQuest.CityTraffic
{
    public enum TrafficSimulationBand
    {
        Near = 0,
        Far = 1
    }

    [DisallowMultipleComponent]
    public class TrafficVehicleController : MonoBehaviour
    {
        private const string AllowedScenePath = "Assets/EcoQuest/City Scene.unity";

        [Header("Identity & Tuning")]
        public int vehicleId;
        public string sourceModelName;
        public bool isHighwayVehicle;
        public int highwayLaneIndex = -1;
        public TrafficSimulationBand simulationBand = TrafficSimulationBand.Near;
        public float baseCruiseSpeed = 5.2f;
        public float acceleration = 2.5f;
        public float brakingDeceleration = 5.0f;
        public float maxSteerAngle = 30.0f;
        public float wheelRadius = 0.35f;
        public float bodyLength = 4.2f;
        public float bodyWidth = 1.9f;

        [Header("Safety Distances")]
        public float sensorLength = 5.0f;
        public float sensorRadius = 0.8f;
        public float minCarGap = 4.0f;
        public float highwayMinCarGap = 8.0f;
        public float minPedestrianGap = 3.5f;
        public float honkDelaySeconds = 3.0f;
        public float blockedRerouteSeconds = 20.0f;
        public float crosswalkClearWaitSeconds = 1.0f;

        [Header("Audio (Null-Safe)")]
        public AudioSource hornAudioSource;
        public AudioClip hornClip;
        public AudioSource engineAudioSource;
        public AudioClip engineHumClip;

        [Header("Stage D Polish (Old Car Exhaust)")]
        public bool isOldCar;
        public ParticleSystem exhaustParticles;

        [Header("Wheel Transforms")]
        public Transform wheelFL;
        public Transform wheelFR;
        public Transform wheelRL;
        public Transform wheelRR;
        public Transform wheelRL2;
        public Transform wheelRR2;

        [Header("Runtime State (Read-Only)")]
        [SerializeField] private int _currentEdgeId = -1;
        [SerializeField] private int _nextEdgeId = -1;
        [SerializeField] private float _distanceOnEdge;
        [SerializeField] private float _currentSpeed;
        [SerializeField] private float _targetSpeed;
        [SerializeField] private string _statusReason = "Cruising";
        [SerializeField] private float _distanceTraveled;

        private CityTrafficManager _manager;
        private CityTrafficGraph _graph;
        private Rigidbody _rb;
        private BoxCollider _boxCollider;

        private float _wheelRollAngle;
        private float _currentSteerAngle;
        private float _sensorTimer;
        private float _sensorInterval = 0.08f; // ~12.5 Hz

        private bool _sensorBlockedByCar;
        private bool _sensorBlockedByPlayer;
        private bool _sensorBlockedByNpc;
        private bool _overlappingPlayer;
        private bool _overlappingCar;
        private float _obstacleDistance = 999f;

        private float _playerBlockTimer;
        private bool _hasHonkedForCurrentBlock;
        private int _honkCount;
        private float _totalBlockedTimer;
        private float _rerouteClearanceTimer;
        private float _crosswalkOccupiedTimer;
        private bool _crosswalkBlocked;
        private int _reservedIntersectionIndex = -1;
        private float _offscreenFarTimer;

        // Pre-allocated non-alloc buffers (zero GC in Update/FixedUpdate)
        private static readonly RaycastHit[] SharedHitBuffer = new RaycastHit[16];
        private static readonly Collider[] SharedOverlapBuffer = new Collider[24];
        private static AudioClip _synthesizedBeepClip;
        private static AudioClip _sharedEngineHumClip;
        private static Material _sharedExhaustParticleMat;

        public int CurrentEdgeId => _currentEdgeId;
        public int NextEdgeId => _nextEdgeId;
        public float DistanceOnEdge => _distanceOnEdge;
        public float CurrentSpeed => _currentSpeed;
        public float TargetSpeed => _targetSpeed;
        public string StatusReason => _statusReason;
        public float DistanceTraveled => _distanceTraveled;
        public bool IsBlockedByPlayer => _sensorBlockedByPlayer || _overlappingPlayer;
        public bool IsBlockedByCar => _sensorBlockedByCar || _overlappingCar;
        public bool IsOverlappingCar => _overlappingCar;
        public bool IsBlockedByCrosswalk => _crosswalkBlocked;
        public int HonkCount => _honkCount;
        public float PlayerBlockTimer => _playerBlockTimer;
        public float OffscreenFarTimer { get => _offscreenFarTimer; set => _offscreenFarTimer = value; }

        private void Awake()
        {
            if (!IsInAllowedScene())
            {
                enabled = false;
                gameObject.SetActive(false);
                return;
            }
            EnsurePhysicsAndWheels();
        }

        private void OnEnable()
        {
            if (!IsInAllowedScene())
            {
                enabled = false;
                gameObject.SetActive(false);
            }
        }

        private void OnDisable()
        {
            ReleaseReservations();
            if (hornAudioSource != null && hornAudioSource.isPlaying)
            {
                hornAudioSource.Stop();
            }
            if (engineAudioSource != null && engineAudioSource.isPlaying)
            {
                engineAudioSource.Stop();
            }
            if (exhaustParticles != null && exhaustParticles.isPlaying)
            {
                exhaustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void OnDestroy()
        {
            ReleaseReservations();
        }

        private static bool IsInAllowedScene()
        {
            Scene active = SceneManager.GetActiveScene();
            return active.IsValid() && active.path == AllowedScenePath;
        }

        private void ReleaseReservations()
        {
            if (_graph != null)
            {
                _graph.ReleaseAllForVehicle(vehicleId);
            }
            _reservedIntersectionIndex = -1;
        }

        public void EnsurePhysicsAndWheels()
        {
            int trafficLayer = LayerMask.NameToLayer("Traffic");
            if (trafficLayer >= 0)
            {
                SetLayerRecursively(transform, trafficLayer);
            }

            // Strip any non-convex MeshColliders immediately before adding Rigidbody so they never coexist during Instantiate()
            MeshCollider[] meshCols = GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < meshCols.Length; i++)
            {
                if (meshCols[i] != null) DestroyImmediate(meshCols[i]);
            }

            // Remove any extra BoxColliders on child objects so there is strictly ONE BoxCollider on root
            BoxCollider[] childBoxes = GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < childBoxes.Length; i++)
            {
                if (childBoxes[i] != null && childBoxes[i].gameObject != gameObject)
                {
                    DestroyImmediate(childBoxes[i]);
                }
            }

            _boxCollider = GetComponent<BoxCollider>();
            if (_boxCollider == null) _boxCollider = gameObject.AddComponent<BoxCollider>();

            // Compute combined local bounds from MeshRenderers
            MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length > 0)
            {
                Quaternion savedRot = transform.rotation;
                Vector3 savedPos = transform.position;
                transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    b.Encapsulate(renderers[i].bounds);
                }

                transform.SetPositionAndRotation(savedPos, savedRot);

                float maxLen = isHighwayVehicle ? 10.0f : 5.5f;
                Vector3 localCenter = new Vector3(b.center.x, Mathf.Max(0.65f, b.center.y), b.center.z);
                Vector3 localSize = new Vector3(
                    Mathf.Clamp(b.size.x, 1.5f, 2.6f),
                    Mathf.Clamp(b.size.y, 1.1f, 3.0f),
                    Mathf.Clamp(b.size.z, 3.0f, maxLen));
                _boxCollider.center = localCenter;
                _boxCollider.size = localSize;
                _boxCollider.isTrigger = false;
                bodyLength = localSize.z;
                bodyWidth = localSize.x;
            }

            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // Find wheels (supports 2, 4, or 6 wheels)
            if (wheelFL == null || wheelFR == null)
            {
                Transform[] children = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < children.Length; i++)
                {
                    Transform ch = children[i];
                    if (ch == transform) continue;
                    string n = ch.name;
                    if (n.EndsWith("_FL", StringComparison.OrdinalIgnoreCase)) wheelFL = ch;
                    else if (n.EndsWith("_FR", StringComparison.OrdinalIgnoreCase)) wheelFR = ch;
                    else if (n.EndsWith("_RL", StringComparison.OrdinalIgnoreCase) || n.EndsWith("_RL01", StringComparison.OrdinalIgnoreCase)) wheelRL = ch;
                    else if (n.EndsWith("_RR", StringComparison.OrdinalIgnoreCase) || n.EndsWith("_RR01", StringComparison.OrdinalIgnoreCase)) wheelRR = ch;
                    else if (n.EndsWith("_RL02", StringComparison.OrdinalIgnoreCase)) wheelRL2 = ch;
                    else if (n.EndsWith("_RR02", StringComparison.OrdinalIgnoreCase)) wheelRR2 = ch;
                }
            }

            if (wheelFL != null)
            {
                MeshFilter mf = wheelFL.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    float r = mf.sharedMesh.bounds.extents.y * wheelFL.lossyScale.y;
                    if (r > 0.1f && r < 1.0f) wheelRadius = r;
                }
            }

            if (hornAudioSource == null)
            {
                hornAudioSource = GetComponent<AudioSource>();
                if (hornAudioSource == null)
                {
                    hornAudioSource = gameObject.AddComponent<AudioSource>();
                }
                hornAudioSource.playOnAwake = false;
                hornAudioSource.spatialBlend = 1.0f;
                hornAudioSource.minDistance = 4.0f;
                hornAudioSource.maxDistance = 30.0f;
                hornAudioSource.volume = 0.35f;
            }

            // Stage D.2: Shared 3D Engine Hum AudioSource
            Transform engChild = transform.Find("EngineHumAudio");
            if (engChild == null)
            {
                GameObject engGo = new GameObject("EngineHumAudio");
                engGo.layer = gameObject.layer;
                engChild = engGo.transform;
                engChild.SetParent(transform, false);
                engChild.localPosition = new Vector3(0f, 0.55f, bodyLength * 0.35f);
            }
            engineAudioSource = engChild.GetComponent<AudioSource>();
            if (engineAudioSource == null)
            {
                engineAudioSource = engChild.gameObject.AddComponent<AudioSource>();
            }
            engineAudioSource.playOnAwake = false;
            engineAudioSource.loop = true;
            engineAudioSource.spatialBlend = 1.0f;
            engineAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            engineAudioSource.minDistance = 2.0f;
            engineAudioSource.maxDistance = 18.0f;
            engineAudioSource.volume = 0.12f;
            engineAudioSource.dopplerLevel = 0.15f;
            if (engineHumClip == null)
            {
                engineHumClip = GetOrCreateSharedEngineHumClip();
            }
            engineAudioSource.clip = engineHumClip;

            // Stage D.3: Exhaust particles on OLD cars only
            string checkName = !string.IsNullOrEmpty(sourceModelName) ? sourceModelName : gameObject.name;
            isOldCar = IsOldCarModelName(checkName);
            Transform exhChild = transform.Find("OldCarExhaust");
            if (isOldCar)
            {
                if (exhChild == null)
                {
                    GameObject exhGo = new GameObject("OldCarExhaust");
                    exhGo.layer = gameObject.layer;
                    exhChild = exhGo.transform;
                    exhChild.SetParent(transform, false);
                    exhChild.localPosition = new Vector3(-0.42f, 0.28f, -bodyLength * 0.5f - 0.05f);
                    exhChild.localRotation = Quaternion.Euler(-15f, 180f, 0f);
                }
                exhaustParticles = ConfigureOldCarExhaustParticles(exhChild.gameObject);
            }
            else
            {
                if (exhChild != null)
                {
                    if (Application.isPlaying) Destroy(exhChild.gameObject);
                    else DestroyImmediate(exhChild.gameObject);
                }
                exhaustParticles = null;
            }
        }

        public static bool IsOldCarModelName(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return false;
            // OldCar_01..06 and classic boxy vintage sedans Car_06, Car_07, Car_11
            return modelName.IndexOf("OldCar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   modelName.StartsWith("Car_06", StringComparison.OrdinalIgnoreCase) ||
                   modelName.StartsWith("Car_07", StringComparison.OrdinalIgnoreCase) ||
                   modelName.StartsWith("Car_11", StringComparison.OrdinalIgnoreCase);
        }

        public static ParticleSystem ConfigureOldCarExhaustParticles(GameObject targetGo)
        {
            ParticleSystem ps = targetGo.GetComponent<ParticleSystem>();
            if (ps == null) ps = targetGo.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.34f);
            main.startColor = new Color(0.58f, 0.58f, 0.60f, 0.38f);
            main.gravityModifier = -0.08f; // rises gently
            main.maxParticles = 16;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 5.0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.05f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.65f));

            var psRenderer = targetGo.GetComponent<ParticleSystemRenderer>();
            if (psRenderer != null)
            {
                psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                psRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                psRenderer.receiveShadows = false;
                if (_sharedExhaustParticleMat == null)
                {
                    Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                    if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
                    if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                    if (sh != null)
                    {
                        _sharedExhaustParticleMat = new Material(sh)
                        {
                            name = "RuntimeOldCarExhaustMat",
                            hideFlags = HideFlags.HideAndDontSave,
                            color = new Color(0.62f, 0.62f, 0.64f, 0.4f)
                        };
                    }
                }
                if (_sharedExhaustParticleMat != null)
                {
                    psRenderer.sharedMaterial = _sharedExhaustParticleMat;
                }
            }

            return ps;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            int count = root.childCount;
            for (int i = 0; i < count; i++)
            {
                SetLayerRecursively(root.GetChild(i), layer);
            }
        }

        public void InitializeOnEdge(CityTrafficManager manager, CityTrafficGraph graph, int edgeId, float distanceAlongEdge, float cruiseSpeed, TrafficSimulationBand band = TrafficSimulationBand.Near)
        {
            _manager = manager;
            _graph = graph;
            isHighwayVehicle = false;
            highwayLaneIndex = -1;
            simulationBand = band;
            _currentEdgeId = edgeId;
            _distanceOnEdge = distanceAlongEdge;
            baseCruiseSpeed = cruiseSpeed;
            _currentSpeed = cruiseSpeed * 0.8f;
            _targetSpeed = cruiseSpeed;
            _nextEdgeId = -1;
            _playerBlockTimer = 0f;
            _hasHonkedForCurrentBlock = false;
            _totalBlockedTimer = 0f;
            _rerouteClearanceTimer = 0f;
            _crosswalkOccupiedTimer = 0f;
            _crosswalkBlocked = false;
            _offscreenFarTimer = 0f;
            _distanceTraveled = 0f;
            _sensorTimer = (vehicleId % 8) * 0.01f; // stagger sensor ticks across pool

            ReleaseReservations();
            ChooseNextEdge(forceAlternative: false);

            var edge = _graph.edges[_currentEdgeId];
            edge.SampleAtDistance(_distanceOnEdge, out Vector3 pos, out Vector3 tangent);
            pos.y = 0f;
            Quaternion rot = tangent.sqrMagnitude > 0.001f ? Quaternion.LookRotation(tangent, Vector3.up) : Quaternion.identity;

            transform.SetPositionAndRotation(pos, rot);
            if (_rb != null)
            {
                _rb.position = pos;
                _rb.rotation = rot;
            }

            if (simulationBand == TrafficSimulationBand.Near)
            {
                RunForwardSensorAndCrosswalkCheck();
            }
            else
            {
                RunLightweightFarSensorCheck();
            }
        }

        public void InitializeOnHighwayLane(CityTrafficManager manager, CityTrafficGraph graph, int laneIndex, float startZPos, float cruiseSpeed)
        {
            _manager = manager;
            _graph = graph;
            isHighwayVehicle = true;
            highwayLaneIndex = laneIndex;
            simulationBand = TrafficSimulationBand.Far;
            baseCruiseSpeed = cruiseSpeed;
            _currentSpeed = cruiseSpeed;
            _targetSpeed = cruiseSpeed;
            _distanceTraveled = 0f;
            _offscreenFarTimer = 0f;
            EnsurePhysicsAndWheels();

            if (_graph != null && laneIndex >= 0 && laneIndex < _graph.highwayLanes.Count)
            {
                HighwayLane lane = _graph.highwayLanes[laneIndex];
                Vector3 pos = new Vector3(lane.laneX, 0.01f, startZPos);
                Quaternion rot = Quaternion.Euler(0f, lane.directionZ > 0f ? 0f : 180f, 0f);
                transform.SetPositionAndRotation(pos, rot);
                if (_rb != null)
                {
                    _rb.position = pos;
                    _rb.rotation = rot;
                }
            }
        }

        private void FixedUpdate()
        {
            if (Time.timeScale <= 0.0001f)
            {
                if (engineAudioSource != null && engineAudioSource.isPlaying)
                {
                    engineAudioSource.Pause();
                }
                return;
            }
            StepSimulation(Time.fixedDeltaTime);
        }

        public void StepSimulation(float dt)
        {
            if (isHighwayVehicle)
            {
                StepHighwaySimulation(dt);
                return;
            }

            if (_graph == null || _currentEdgeId < 0 || _currentEdgeId >= _graph.edges.Count) return;

            if (simulationBand == TrafficSimulationBand.Near)
            {
                _sensorTimer -= dt;
                if (_sensorTimer <= 0f)
                {
                    _sensorTimer = _sensorInterval;
                    RunForwardSensorAndCrosswalkCheck();
                }
                else if (_crosswalkOccupiedTimer > 0f)
                {
                    _crosswalkOccupiedTimer = Mathf.Max(0f, _crosswalkOccupiedTimer - dt);
                    if (_crosswalkOccupiedTimer <= 0f) _crosswalkBlocked = false;
                }
            }
            else
            {
                // Far band lightweight spacing check (no Physics raycasts/overlaps)
                _sensorTimer -= dt;
                if (_sensorTimer <= 0f)
                {
                    _sensorTimer = 0.12f;
                    RunLightweightFarSensorCheck();
                }
            }

            // Immediate freeze if overlapping player or overlapping another vehicle
            if (_overlappingPlayer)
            {
                _currentSpeed = 0f;
                _targetSpeed = 0f;
                _statusReason = "Emergency Stop (Player Overlap)";
                UpdatePlayerBlockAndHonk(dt, true);
                return;
            }

            if (_overlappingCar)
            {
                _currentSpeed = 0f;
                _targetSpeed = 0f;
                _statusReason = "Emergency Stop (Car Overlap)";
                return;
            }

            TrafficEdge edge = _graph.edges[_currentEdgeId];
            if (_nextEdgeId < 0) ChooseNextEdge(forceAlternative: false);

            // Base speed limit for current segment
            float desiredSpeed = baseCruiseSpeed;
            if (edge.isParkDistrict || transform.position.x < -100f)
            {
                desiredSpeed = Mathf.Min(desiredSpeed, _manager != null ? _manager.parkSpeed : 3.5f);
            }
            if (edge.isCurve)
            {
                desiredSpeed = Mathf.Min(desiredSpeed, _manager != null ? _manager.curveSpeed : 3.0f);
            }

            float distRemainingOnEdge = edge.length - _distanceOnEdge;
            bool mustStopAtLine = false;
            float stopDistanceAhead = 999f;
            _statusReason = "Cruising";

            if (_rerouteClearanceTimer > 0f)
            {
                _rerouteClearanceTimer = Mathf.Max(0f, _rerouteClearanceTimer - dt);
            }

            // 1. Crosswalk stop line check (stop 2.5m before crossing zone)
            if (_crosswalkBlocked && edge.approachingCrosswalkIndex >= 0 && edge.stopLineDistanceOnEdge >= 0f)
            {
                float distToStopLine = edge.stopLineDistanceOnEdge - _distanceOnEdge;
                if (distToStopLine >= -1.0f && distToStopLine < 18.0f)
                {
                    mustStopAtLine = true;
                    stopDistanceAhead = Mathf.Min(stopDistanceAhead, Mathf.Max(0f, distToStopLine));
                    _statusReason = "Yielding at Crosswalk";
                }
            }

            // 1b. Railway Crossing stop line check (when train is within 60m of z = -54 crossing)
            if (edge.isRailCrossingApproach && edge.railCrossingStopLineDist >= 0f && _manager != null && _manager.IsRailCrossingActive())
            {
                float distToRailStop = edge.railCrossingStopLineDist - _distanceOnEdge;
                if (distToRailStop >= -1.5f && distToRailStop < 25.0f)
                {
                    mustStopAtLine = true;
                    stopDistanceAhead = Mathf.Min(stopDistanceAhead, Mathf.Max(0f, distToRailStop));
                    _statusReason = "Stopped at Rail Crossing";
                }
            }

            // 2. Intersection approach & right-of-way / 4-phase traffic signal reservation
            bool heldByTrafficSignal = false;
            if (!edge.isIntersectionArc && _nextEdgeId >= 0 && _nextEdgeId < _graph.edges.Count)
            {
                TrafficEdge nextEdge = _graph.edges[_nextEdgeId];
                if (nextEdge.isIntersectionArc && nextEdge.intersectionIndex >= 0)
                {
                    if (distRemainingOnEdge < 10.0f)
                    {
                        var inter = _graph.intersections[nextEdge.intersectionIndex];
                        float simNow = _manager != null ? _manager.SimulationTime : Time.time;

                        if (inter.armCount >= 3 && (!inter.hasTrafficLight || _graph.GetTrafficLightState(nextEdge.intersectionIndex, nextEdge.approachDir, simNow) != TrafficLightPhaseState.Green))
                        {
                            // Slow to ~2.0 m/s approaching unsignalized conflict box or non-green signal
                            desiredSpeed = Mathf.Min(desiredSpeed, 2.2f);
                        }

                        // Request right-of-way reservation when approaching the intersection stop line (<= 4.5m from entry)
                        if (distRemainingOnEdge <= 4.5f && !mustStopAtLine && !_sensorBlockedByPlayer && !_sensorBlockedByNpc && !_sensorBlockedByCar && !_overlappingCar)
                        {
                            bool allowYellow = distRemainingOnEdge <= 3.2f && _currentSpeed > 1.2f;
                            bool granted = _graph.RequestIntersectionEntry(nextEdge.intersectionIndex, vehicleId, _nextEdgeId, simNow, 6.0f, allowYellow);
                            if (granted)
                            {
                                _reservedIntersectionIndex = nextEdge.intersectionIndex;
                            }
                            else
                            {
                                float stopPointFromEnd = 0.6f;
                                float distToIntersectionStop = Mathf.Max(0f, distRemainingOnEdge - stopPointFromEnd);
                                mustStopAtLine = true;
                                stopDistanceAhead = Mathf.Min(stopDistanceAhead, distToIntersectionStop);
                                if (inter.hasTrafficLight && _graph.GetTrafficLightState(nextEdge.intersectionIndex, nextEdge.approachDir, simNow) != TrafficLightPhaseState.Green)
                                {
                                    heldByTrafficSignal = true;
                                    _statusReason = "Stopped at Red Light";
                                }
                                else
                                {
                                    _statusReason = "Yielding at Intersection";
                                }
                            }
                        }
                    }
                }
            }

            // 3. Forward obstacle (Player, NPC, Car in front) - NEVER bypassed by reroute timers
            if (_sensorBlockedByPlayer || _sensorBlockedByNpc || _sensorBlockedByCar)
            {
                float reqGap = _sensorBlockedByCar ? minCarGap : minPedestrianGap;
                float stopDist = Mathf.Max(0f, _obstacleDistance - reqGap);
                mustStopAtLine = true;
                stopDistanceAhead = Mathf.Min(stopDistanceAhead, stopDist);
                if (_sensorBlockedByPlayer) _statusReason = "Stopped for Player";
                else if (_sensorBlockedByNpc) _statusReason = "Stopped for Pedestrian";
                else _statusReason = "Following Vehicle";
            }

            if (mustStopAtLine)
            {
                if (stopDistanceAhead <= 0.25f)
                {
                    desiredSpeed = 0f;
                }
                else
                {
                    // Smooth kinematic braking curve v = sqrt(2 * a * d)
                    float safeApproachSpeed = Mathf.Sqrt(2f * brakingDeceleration * stopDistanceAhead);
                    desiredSpeed = Mathf.Min(desiredSpeed, safeApproachSpeed);
                    if (stopDistanceAhead < 1.0f) desiredSpeed = Mathf.Min(desiredSpeed, 0.5f);
                }
            }

            _targetSpeed = desiredSpeed;

            // Accelerate or brake smoothly
            if (_currentSpeed < _targetSpeed)
            {
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, _targetSpeed, acceleration * dt);
            }
            else
            {
                float decel = (_sensorBlockedByPlayer || _sensorBlockedByCar || stopDistanceAhead < 0.5f) ? brakingDeceleration * 1.6f : brakingDeceleration;
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, _targetSpeed, decel * dt);
            }

            if (mustStopAtLine && stopDistanceAhead <= 0.15f)
            {
                _currentSpeed = 0f;
            }

            // Friendly honk timer when blocked by player
            UpdatePlayerBlockAndHonk(dt, _sensorBlockedByPlayer && _currentSpeed < 0.25f);

            // Reroute timer if waiting > 20s behind a blocked path (scheduled Red lights are normal signal phases, not blockages)
            if (_currentSpeed < 0.2f && mustStopAtLine && !heldByTrafficSignal)
            {
                _totalBlockedTimer += dt;
                if (_totalBlockedTimer >= blockedRerouteSeconds)
                {
                    _totalBlockedTimer = 0f;
                    RerouteAtNextIntersection();
                }
            }
            else
            {
                _totalBlockedTimer = 0f;
            }

            // Advance along edge
            float moveDist = _currentSpeed * dt;
            if (mustStopAtLine && moveDist > stopDistanceAhead)
            {
                moveDist = Mathf.Max(0f, stopDistanceAhead);
            }

            // Hard invariant: never advance within safe gap of vehicle ahead on the same edge
            if (_manager != null)
            {
                float myHalf = bodyLength * 0.5f;
                List<TrafficVehicleController> activeCars = _manager.ActiveVehicles;
                for (int i = 0; i < activeCars.Count; i++)
                {
                    TrafficVehicleController other = activeCars[i];
                    if (other == null || other == this || !other.gameObject.activeInHierarchy) continue;
                    if (other._currentEdgeId == _currentEdgeId && other._distanceOnEdge > _distanceOnEdge)
                    {
                        float minSeparation = myHalf + other.bodyLength * 0.5f + 1.2f;
                        float maxAllowedDist = Mathf.Max(0f, other._distanceOnEdge - minSeparation);
                        if (_distanceOnEdge + moveDist > maxAllowedDist)
                        {
                            moveDist = Mathf.Max(0f, maxAllowedDist - _distanceOnEdge);
                            _currentSpeed = 0f;
                        }
                    }
                }
            }

            _distanceOnEdge += moveDist;
            _distanceTraveled += moveDist;

            // Transition across edge boundaries
            int safetyLoops = 0;
            while (_distanceOnEdge >= edge.length && safetyLoops < 4)
            {
                safetyLoops++;
                float overshoot = _distanceOnEdge - edge.length;

                // If we are about to enter an unreserved 3/4-way intersection arc, clamp at the end of current edge
                if (!edge.isIntersectionArc && _nextEdgeId >= 0 && _nextEdgeId < _graph.edges.Count)
                {
                    TrafficEdge candidateNext = _graph.edges[_nextEdgeId];
                    if (candidateNext.isIntersectionArc && candidateNext.intersectionIndex >= 0)
                    {
                        // Also check if any other vehicle currently occupies the target intersection arc
                        bool arcOccupied = false;
                        if (_manager != null)
                        {
                            List<TrafficVehicleController> activeCars = _manager.ActiveVehicles;
                            for (int a = 0; a < activeCars.Count; a++)
                            {
                                if (activeCars[a] != null && activeCars[a] != this && activeCars[a].gameObject.activeInHierarchy && activeCars[a]._currentEdgeId == _nextEdgeId)
                                {
                                    arcOccupied = true;
                                    break;
                                }
                            }
                        }

                        float simNow = _manager != null ? _manager.SimulationTime : Time.time;
                        if (arcOccupied || !_graph.RequestIntersectionEntry(candidateNext.intersectionIndex, vehicleId, _nextEdgeId, simNow, 6.0f, true))
                        {
                            _distanceOnEdge = Mathf.Max(0f, edge.length - 0.5f);
                            _currentSpeed = 0f;
                            break;
                        }
                        _reservedIntersectionIndex = candidateNext.intersectionIndex;
                    }
                }
                            _currentSpeed = 0f;
                            break;
                        }
                        _reservedIntersectionIndex = candidateNext.intersectionIndex;
                    }
                }

                // If leaving an intersection arc, release its reservation
                if (edge.isIntersectionArc && edge.intersectionIndex >= 0)
                {
                    _graph.ReleaseIntersection(edge.intersectionIndex, vehicleId);
                    if (_reservedIntersectionIndex == edge.intersectionIndex)
                        _reservedIntersectionIndex = -1;
                }

                if (_nextEdgeId < 0 || _nextEdgeId >= _graph.edges.Count)
                {
                    ChooseNextEdge(forceAlternative: false);
                }

                if (_nextEdgeId < 0 || _nextEdgeId >= _graph.edges.Count)
                {
                    _distanceOnEdge = edge.length;
                    _currentSpeed = 0f;
                    break;
                }

                _currentEdgeId = _nextEdgeId;
                _distanceOnEdge = overshoot;
                edge = _graph.edges[_currentEdgeId];
                ChooseNextEdge(forceAlternative: false);
            }

            // Sample world position and tangent
            edge.SampleAtDistance(_distanceOnEdge, out Vector3 targetPos, out Vector3 tangent);
            // Look ahead slightly for smoother cornering rotation
            float lookAheadDist = Mathf.Clamp(_currentSpeed * 0.35f, 0.8f, 2.2f);
            Vector3 lookPos;
            Vector3 lookTangent;
            if (_distanceOnEdge + lookAheadDist <= edge.length)
            {
                edge.SampleAtDistance(_distanceOnEdge + lookAheadDist, out lookPos, out lookTangent);
            }
            else if (_nextEdgeId >= 0 && _nextEdgeId < _graph.edges.Count)
            {
                float rem = (_distanceOnEdge + lookAheadDist) - edge.length;
                _graph.edges[_nextEdgeId].SampleAtDistance(rem, out lookPos, out lookTangent);
            }
            else
            {
                lookPos = targetPos + tangent * lookAheadDist;
                lookTangent = tangent;
            }

            Vector3 moveDir = (lookPos - targetPos);
            moveDir.y = 0f;
            if (moveDir.sqrMagnitude < 0.0001f) moveDir = tangent;
            else moveDir.Normalize();

            targetPos.y = 0f;
            Quaternion targetRot = moveDir.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(moveDir, Vector3.up)
                : transform.rotation;

            Quaternion currentRot = (Application.isPlaying && _rb != null) ? _rb.rotation : transform.rotation;
            float signedTurnAngle = Vector3.SignedAngle(currentRot * Vector3.forward, moveDir, Vector3.up);
            Quaternion smoothedRot = Quaternion.RotateTowards(currentRot, targetRot, 140f * dt);

            transform.SetPositionAndRotation(targetPos, smoothedRot);
            if (_rb != null && Application.isPlaying)
            {
                _rb.MovePosition(targetPos);
                _rb.MoveRotation(smoothedRot);
            }

            AnimateWheels(dt, simulationBand == TrafficSimulationBand.Near ? signedTurnAngle : 0f);
            UpdateEngineAudioAndExhaust();
        }

        private void StepHighwaySimulation(float dt)
        {
            if (_graph == null || highwayLaneIndex < 0 || highwayLaneIndex >= _graph.highwayLanes.Count) return;

            HighwayLane lane = _graph.highwayLanes[highwayLaneIndex];
            float dirZ = lane.directionZ >= 0f ? 1f : -1f;
            float myZ = transform.position.z;
            float halfLen = bodyLength * 0.5f;

            // Forward car-only sensor along the same highway lane (8m minimum following distance, no lane changes)
            float minGapAhead = 999f;
            bool highwayOverlap = false;
            if (_manager != null)
            {
                List<TrafficVehicleController> hwyCars = _manager.HighwayVehicles;
                for (int i = 0; i < hwyCars.Count; i++)
                {
                    TrafficVehicleController other = hwyCars[i];
                    if (other == null || other == this || !other.gameObject.activeInHierarchy) continue;
                    if (other.highwayLaneIndex != highwayLaneIndex) continue;

                    float dz = (other.transform.position.z - myZ) * dirZ;
                    float combinedHalf = halfLen + other.bodyLength * 0.5f;
                    if (Mathf.Abs(dz) < combinedHalf + 0.25f)
                    {
                        if (dz >= -0.2f)
                        {
                            highwayOverlap = true;
                            minGapAhead = 0f;
                        }
                    }
                    else if (dz > 0f)
                    {
                        float gap = dz - combinedHalf;
                        if (gap < minGapAhead) minGapAhead = gap;
                    }
                }
            }

            if (highwayOverlap)
            {
                _currentSpeed = 0f;
                _targetSpeed = 0f;
                _statusReason = "Emergency Stop (Highway Overlap)";
                return;
            }

            float desiredSpeed = baseCruiseSpeed;
            if (minGapAhead < highwayMinCarGap + 10.0f)
            {
                float stopDist = Mathf.Max(0f, minGapAhead - highwayMinCarGap);
                if (stopDist <= 0.25f)
                {
                    desiredSpeed = 0f;
                    _statusReason = "Highway Following Stop";
                }
                else
                {
                    float safeSpeed = Mathf.Sqrt(2f * brakingDeceleration * stopDist);
                    desiredSpeed = Mathf.Min(desiredSpeed, safeSpeed);
                    _statusReason = "Highway Following";
                }
            }
            else
            {
                _statusReason = "Highway Cruising";
            }

            _targetSpeed = desiredSpeed;
            if (_currentSpeed < _targetSpeed)
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, _targetSpeed, (acceleration * 1.4f) * dt);
            else
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, _targetSpeed, (brakingDeceleration * 1.4f) * dt);

            float moveDist = _currentSpeed * dt;
            _distanceTraveled += moveDist;
            float nextZ = myZ + dirZ * moveDist;

            // Hard clamp so we never advance into the car ahead on the highway
            if (_manager != null)
            {
                List<TrafficVehicleController> hwyCars = _manager.HighwayVehicles;
                for (int i = 0; i < hwyCars.Count; i++)
                {
                    TrafficVehicleController other = hwyCars[i];
                    if (other == null || other == this || !other.gameObject.activeInHierarchy) continue;
                    if (other.highwayLaneIndex != highwayLaneIndex) continue;
                    float dz = (other.transform.position.z - myZ) * dirZ;
                    if (dz > 0f)
                    {
                        float minZDist = halfLen + other.bodyLength * 0.5f + 1.2f;
                        float maxAllowedZ = other.transform.position.z - dirZ * minZDist;
                        if ((nextZ - maxAllowedZ) * dirZ > 0f)
                        {
                            nextZ = maxAllowedZ;
                            _currentSpeed = 0f;
                        }
                    }
                }
            }

            // End-to-end tunnel wrap when reaching far end inside Tunel_03 (z = +/-118m)
            bool reachedEnd = (dirZ > 0f && nextZ >= lane.endZ) || (dirZ < 0f && nextZ <= lane.endZ);
            if (reachedEnd)
            {
                float wrapZ = lane.startZ;
                // Ensure we don't wrap directly on top of another car at the tunnel entrance
                if (_manager != null)
                {
                    List<TrafficVehicleController> hwyCars = _manager.HighwayVehicles;
                    for (int i = 0; i < hwyCars.Count; i++)
                    {
                        TrafficVehicleController other = hwyCars[i];
                        if (other == null || other == this || other.highwayLaneIndex != highwayLaneIndex) continue;
                        float dz = (other.transform.position.z - wrapZ) * dirZ;
                        if (dz >= -6f && dz < (highwayMinCarGap + bodyLength))
                        {
                            wrapZ -= dirZ * (highwayMinCarGap + bodyLength - dz);
                        }
                    }
                }
                nextZ = wrapZ;
            }

            Vector3 nextPos = new Vector3(lane.laneX, 0.01f, nextZ);
            Quaternion rot = Quaternion.Euler(0f, dirZ > 0f ? 0f : 180f, 0f);
            transform.SetPositionAndRotation(nextPos, rot);
            if (_rb != null && Application.isPlaying)
            {
                _rb.MovePosition(nextPos);
                _rb.MoveRotation(rot);
            }

            AnimateWheels(dt, 0f);
            UpdateEngineAudioAndExhaust();
        }

        private void UpdateEngineAudioAndExhaust()
        {
            if (engineAudioSource != null && Application.isPlaying)
            {
                bool shouldPlayAudio = (simulationBand == TrafficSimulationBand.Near || isHighwayVehicle) && Time.timeScale > 0.001f;
                if (shouldPlayAudio)
                {
                    if (!engineAudioSource.isPlaying && engineAudioSource.isActiveAndEnabled)
                    {
                        engineAudioSource.Play();
                    }
                    float speedRatio = Mathf.Clamp01(_currentSpeed / 8.5f);
                    engineAudioSource.pitch = Mathf.Lerp(0.82f, 1.28f, speedRatio);
                    engineAudioSource.volume = Mathf.Lerp(0.08f, 0.14f, speedRatio);
                }
                else if (engineAudioSource.isPlaying)
                {
                    engineAudioSource.Pause();
                }
            }

            if (isOldCar && exhaustParticles != null)
            {
                var emission = exhaustParticles.emission;
                emission.rateOverTime = 3.5f + (_currentSpeed * 1.15f);
            }
        }

        private void AnimateWheels(float dt, float signedTurnAngle)
        {
            if (wheelRadius > 0.01f)
            {
                float angularSpeedDeg = (_currentSpeed / wheelRadius) * Mathf.Rad2Deg;
                _wheelRollAngle = Mathf.Repeat(_wheelRollAngle + angularSpeedDeg * dt, 360f);
            }

            float desiredSteer = Mathf.Clamp(signedTurnAngle * 1.6f, -maxSteerAngle, maxSteerAngle);
            _currentSteerAngle = Mathf.MoveTowards(_currentSteerAngle, desiredSteer, 120f * dt);

            Quaternion frontRot = Quaternion.Euler(_wheelRollAngle, _currentSteerAngle, 0f);
            Quaternion rearRot = Quaternion.Euler(_wheelRollAngle, 0f, 0f);

            if (wheelFL != null) wheelFL.localRotation = frontRot;
            if (wheelFR != null) wheelFR.localRotation = frontRot;
            if (wheelRL != null) wheelRL.localRotation = rearRot;
            if (wheelRR != null) wheelRR.localRotation = rearRot;
            if (wheelRL2 != null) wheelRL2.localRotation = rearRot;
            if (wheelRR2 != null) wheelRR2.localRotation = rearRot;
        }

        private void RunLightweightFarSensorCheck()
        {
            _sensorBlockedByCar = false;
            _sensorBlockedByPlayer = false;
            _sensorBlockedByNpc = false;
            _overlappingPlayer = false;
            _overlappingCar = false;
            _crosswalkBlocked = false;
            _obstacleDistance = 999f;

            if (_manager == null || _graph == null || _currentEdgeId < 0) return;

            float halfLen = bodyLength * 0.5f;
            List<TrafficVehicleController> activeCars = _manager.ActiveVehicles;
            for (int i = 0; i < activeCars.Count; i++)
            {
                TrafficVehicleController other = activeCars[i];
                if (other == null || other == this || !other.gameObject.activeInHierarchy) continue;

                float otherHalfLen = other.bodyLength * 0.5f;
                float combinedHalfLen = halfLen + otherHalfLen;
                if (other._currentEdgeId == _currentEdgeId)
                {
                    if (other._distanceOnEdge > _distanceOnEdge)
                    {
                        float gap = (other._distanceOnEdge - _distanceOnEdge) - combinedHalfLen;
                        if (gap <= sensorLength + minCarGap)
                        {
                            _sensorBlockedByCar = true;
                            _obstacleDistance = Mathf.Min(_obstacleDistance, Mathf.Max(0f, gap));
                        }
                    }
                    else if (Mathf.Abs(_distanceOnEdge - other._distanceOnEdge) < combinedHalfLen + 0.25f)
                    {
                        _overlappingCar = true;
                        _sensorBlockedByCar = true;
                        _obstacleDistance = 0f;
                    }
                }
                else if (_nextEdgeId >= 0 && other._currentEdgeId == _nextEdgeId)
                {
                    float myRem = _graph.edges[_currentEdgeId].length - _distanceOnEdge;
                    float gap = (myRem + other._distanceOnEdge) - combinedHalfLen;
                    if (gap <= sensorLength + minCarGap)
                    {
                        _sensorBlockedByCar = true;
                        _obstacleDistance = Mathf.Min(_obstacleDistance, Mathf.Max(0f, gap));
                    }
                }
                else if (_nextEdgeId >= 0 && _graph.edges[_nextEdgeId].isIntersectionArc && other._currentEdgeId == _nextEdgeId)
                {
                    float myRem = Mathf.Max(0f, _graph.edges[_currentEdgeId].length - _distanceOnEdge);
                    _sensorBlockedByCar = true;
                    _obstacleDistance = Mathf.Min(_obstacleDistance, myRem);
                }
            }
        }

        private void UpdatePlayerBlockAndHonk(float dt, bool blockedByPlayerNow)
        {
            if (blockedByPlayerNow)
            {
                _playerBlockTimer += dt;
                if (_playerBlockTimer >= honkDelaySeconds && !_hasHonkedForCurrentBlock)
                {
                    _hasHonkedForCurrentBlock = true;
                    TriggerFriendlyHonk();
                }
            }
            else
            {
                _playerBlockTimer = 0f;
                _hasHonkedForCurrentBlock = false;
            }
        }

        public void TriggerFriendlyHonk()
        {
            _honkCount++;
            if (hornAudioSource == null) return;

            AudioClip clipToPlay = hornClip;
            if (clipToPlay == null)
            {
                clipToPlay = GetOrCreateFriendlyBeepClip();
            }
            if (clipToPlay != null && hornAudioSource.isActiveAndEnabled)
            {
                hornAudioSource.PlayOneShot(clipToPlay, 0.35f);
            }
        }

        private static AudioClip GetOrCreateFriendlyBeepClip()
        {
            if (_synthesizedBeepClip != null) return _synthesizedBeepClip;
            int sampleRate = 22050;
            int sampleCount = sampleRate / 4; // 0.25s short friendly double-chirp
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float env = (t < 0.10f || (t > 0.13f && t < 0.23f)) ? 0.25f : 0f;
                samples[i] = env * (Mathf.Sin(2f * Mathf.PI * 523.25f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 659.25f * t));
            }
            _synthesizedBeepClip = AudioClip.Create("FriendlyTrafficBeep", sampleCount, 1, sampleRate, false);
            _synthesizedBeepClip.SetData(samples, 0);
            return _synthesizedBeepClip;
        }

        public static AudioClip GetOrCreateSharedEngineHumClip()
        {
            if (_sharedEngineHumClip != null) return _sharedEngineHumClip;
            int sampleRate = 22050;
            int sampleCount = sampleRate; // 1.0s seamless loopable soft low-frequency hum
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                // Seamless integer-harmonic warm purr (55 Hz fundamental + 110 Hz + 165 Hz)
                float s = 0.55f * Mathf.Sin(2f * Mathf.PI * 55f * t) +
                          0.30f * Mathf.Sin(2f * Mathf.PI * 110f * t) +
                          0.15f * Mathf.Sin(2f * Mathf.PI * 165f * t);
                samples[i] = s * 0.22f;
            }
            _sharedEngineHumClip = AudioClip.Create("SharedTrafficEngineHum", sampleCount, 1, sampleRate, false);
            _sharedEngineHumClip.SetData(samples, 0);
            return _sharedEngineHumClip;
        }

        private void RunForwardSensorAndCrosswalkCheck()
        {
            _sensorBlockedByCar = false;
            _sensorBlockedByPlayer = false;
            _sensorBlockedByNpc = false;
            _overlappingPlayer = false;
            _overlappingCar = false;
            _obstacleDistance = 999f;

            Vector3 myPos = (Application.isPlaying && _rb != null) ? _rb.position : transform.position;
            Quaternion myRot = (Application.isPlaying && _rb != null) ? _rb.rotation : transform.rotation;
            Vector3 fwd = myRot * Vector3.forward;
            float halfLen = bodyLength * 0.5f;
            Vector3 frontBumper = myPos + fwd * halfLen + Vector3.up * 0.65f;

            // 1. Direct player proximity & forward cone check (100% deterministic even if CharacterController isn't stepped)
            Transform playerTr = _manager != null ? _manager.GetPlayerTransform() : null;
            if (playerTr != null)
            {
                Vector3 toPlayer = playerTr.position - myPos;
                toPlayer.y = 0f;
                float localZ = Vector3.Dot(toPlayer, fwd);
                float localX = Mathf.Abs(Vector3.Dot(toPlayer, myRot * Vector3.right));

                // Check overlap with car body (+0.35m player radius margin)
                if (Mathf.Abs(localZ) < (halfLen + 0.35f) && localX < (bodyWidth * 0.5f + 0.35f))
                {
                    _overlappingPlayer = true;
                    _sensorBlockedByPlayer = true;
                    _obstacleDistance = 0f;
                    return;
                }

                // Check forward sensor corridor ahead of front bumper
                float aheadDist = localZ - halfLen;
                if (aheadDist >= -0.2f && aheadDist <= sensorLength + minPedestrianGap && localX <= (sensorRadius + 0.65f))
                {
                    _sensorBlockedByPlayer = true;
                    _obstacleDistance = Mathf.Min(_obstacleDistance, Mathf.Max(0f, aheadDist));
                }
            }

            // 2. Multi-car spatial & graph-aware sensor check across all active vehicles
            if (_manager != null)
            {
                List<TrafficVehicleController> activeCars = _manager.ActiveVehicles;
                for (int i = 0; i < activeCars.Count; i++)
                {
                    TrafficVehicleController other = activeCars[i];
                    if (other == null || other == this || !other.gameObject.activeInHierarchy) continue;

                    float otherHalfLen = other.bodyLength * 0.5f;
                    float combinedHalfLen = halfLen + otherHalfLen;
                    float combinedHalfWidth = Mathf.Max(bodyWidth * 0.5f, other.bodyWidth * 0.5f) + 0.25f;

                    Vector3 toOther = other.transform.position - myPos;
                    toOther.y = 0f;
                    float localZ = Vector3.Dot(toOther, fwd);
                    float localX = Mathf.Abs(Vector3.Dot(toOther, myRot * Vector3.right));

                    // Immediate bounding box overlap check (360-degree close-proximity collision guard)
                    if (Mathf.Abs(localZ) < (combinedHalfLen + 0.20f) && localX < combinedHalfWidth)
                    {
                        _overlappingCar = true;
                        _sensorBlockedByCar = true;
                        _obstacleDistance = 0f;
                    }

                    // Forward corridor check (detects any vehicle ahead across any angle: straight, turning, or crossing)
                    float corridorWidth = combinedHalfWidth + 0.45f;
                    float forwardDist = localZ - combinedHalfLen;
                    if (localZ > -0.2f && forwardDist <= (sensorLength + minCarGap) && localX <= corridorWidth)
                    {
                        _sensorBlockedByCar = true;
                        _obstacleDistance = Mathf.Min(_obstacleDistance, Mathf.Max(0f, forwardDist));
                    }

                    // Same edge check
                    if (other._currentEdgeId == _currentEdgeId)
                    {
                        if (other._distanceOnEdge > _distanceOnEdge)
                        {
                            float gap = (other._distanceOnEdge - _distanceOnEdge) - combinedHalfLen;
                            if (gap <= sensorLength + minCarGap)
                            {
                                _sensorBlockedByCar = true;
                                _obstacleDistance = Mathf.Min(_obstacleDistance, Mathf.Max(0f, gap));
                            }
                        }
                        else if (Mathf.Abs(_distanceOnEdge - other._distanceOnEdge) < combinedHalfLen + 0.25f)
                        {
                            _overlappingCar = true;
                            _sensorBlockedByCar = true;
                            _obstacleDistance = 0f;
                        }
                    }
                    // Next edge check
                    else if (_nextEdgeId >= 0 && other._currentEdgeId == _nextEdgeId && _currentEdgeId >= 0)
                    {
                        float myRem = _graph.edges[_currentEdgeId].length - _distanceOnEdge;
                        float gap = (myRem + other._distanceOnEdge) - combinedHalfLen;
                        if (gap <= sensorLength + minCarGap)
                        {
                            _sensorBlockedByCar = true;
                            _obstacleDistance = Mathf.Min(_obstacleDistance, Mathf.Max(0f, gap));
                        }
                    }
                    // Next edge is intersection arc: hold before entering if another vehicle is already inside the arc
                    if (_nextEdgeId >= 0 && _graph != null && _nextEdgeId < _graph.edges.Count && _graph.edges[_nextEdgeId].isIntersectionArc)
                    {
                        if (other._currentEdgeId == _nextEdgeId)
                        {
                            float myRem = Mathf.Max(0f, _graph.edges[_currentEdgeId].length - _distanceOnEdge);
                            _sensorBlockedByCar = true;
                            _obstacleDistance = Mathf.Min(_obstacleDistance, myRem);
                        }
                    }
                }
            }

            // 3. Non-alloc Physics SphereCast ahead for NavMeshAgents / Player / Traffic colliders
            int hitCount = Physics.SphereCastNonAlloc(frontBumper, sensorRadius, fwd, SharedHitBuffer, sensorLength + minPedestrianGap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider col = SharedHitBuffer[i].collider;
                if (col == null || col == _boxCollider || col.transform.IsChildOf(transform)) continue;

                float hitDist = SharedHitBuffer[i].distance;
                if (col.GetComponentInParent<CharacterController>() != null)
                {
                    _sensorBlockedByPlayer = true;
                    _obstacleDistance = Mathf.Min(_obstacleDistance, hitDist);
                }
                else if (col.GetComponentInParent<NavMeshAgent>() != null)
                {
                    // Only stop for pedestrians actually on the road asphalt (y < 0.08m), not on raised sidewalks across corners
                    if (col.transform.position.y < 0.08f)
                    {
                        _sensorBlockedByNpc = true;
                        _obstacleDistance = Mathf.Min(_obstacleDistance, hitDist);
                    }
                }
                else
                {
                    var otherCar = col.GetComponentInParent<TrafficVehicleController>();
                    if (otherCar != null && otherCar != this)
                    {
                        _sensorBlockedByCar = true;
                        _obstacleDistance = Mathf.Min(_obstacleDistance, hitDist);
                    }
                }
            }

            // 4. Merged Crosswalk Zone Check (Stage B.5)
            if (_graph != null && _currentEdgeId >= 0 && _currentEdgeId < _graph.edges.Count)
            {
                TrafficEdge edge = _graph.edges[_currentEdgeId];
                if (edge.approachingCrosswalkIndex >= 0 && edge.approachingCrosswalkIndex < _graph.crosswalkZones.Count)
                {
                    TrafficCrosswalkZone zone = _graph.crosswalkZones[edge.approachingCrosswalkIndex];
                    bool pedInZone = IsPedestrianInCrosswalkZone(zone, playerTr);
                    if (pedInZone)
                    {
                        _crosswalkOccupiedTimer = crosswalkClearWaitSeconds;
                        _crosswalkBlocked = true;
                    }
                    else if (_crosswalkOccupiedTimer > 0f)
                    {
                        _crosswalkOccupiedTimer = Mathf.Max(0f, _crosswalkOccupiedTimer - _sensorInterval);
                        _crosswalkBlocked = _crosswalkOccupiedTimer > 0f;
                    }
                    else
                    {
                        _crosswalkBlocked = false;
                    }
                }
                else
                {
                    _crosswalkBlocked = false;
                    _crosswalkOccupiedTimer = 0f;
                }
            }
        }

        private static bool IsPedestrianInCrosswalkZone(TrafficCrosswalkZone zone, Transform playerTr)
        {
            // Check road width (+/- 3.05m from centerline) and crossing length so stationary sidewalk NPCs don't trigger permanent stops
            Vector3 roadCheckHalf = new Vector3(Mathf.Min(zone.halfExtents.x, 3.05f), 0.5f, zone.halfExtents.z + 0.35f);

            if (playerTr != null && playerTr.position.y < 0.15f)
            {
                Vector3 localP = Quaternion.Inverse(zone.rotation) * (playerTr.position - zone.center);
                if (Mathf.Abs(localP.x) <= roadCheckHalf.x && Mathf.Abs(localP.z) <= roadCheckHalf.z)
                {
                    return true;
                }
            }

            int count = Physics.OverlapBoxNonAlloc(zone.center, roadCheckHalf, SharedOverlapBuffer, zone.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = SharedOverlapBuffer[i];
                if (c == null) continue;
                if (c.GetComponentInParent<CharacterController>() != null || c.GetComponentInParent<NavMeshAgent>() != null)
                {
                    if (c.transform.position.y < 0.15f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void ChooseNextEdge(bool forceAlternative)
        {
            if (_graph == null || _currentEdgeId < 0 || _currentEdgeId >= _graph.edges.Count)
            {
                _nextEdgeId = -1;
                return;
            }

            TrafficEdge curEdge = _graph.edges[_currentEdgeId];
            if (curEdge.toNode < 0 || curEdge.toNode >= _graph.nodes.Count)
            {
                _nextEdgeId = -1;
                return;
            }

            TrafficNode toNode = _graph.nodes[curEdge.toNode];
            List<int> outgoing = toNode.outgoingEdges;
            if (outgoing == null || outgoing.Count == 0)
            {
                _nextEdgeId = -1;
                return;
            }

            if (outgoing.Count == 1)
            {
                _nextEdgeId = outgoing[0];
                return;
            }

            // Weighted choice at intersections: 60% Straight, 20% Right, 20% Left (no U-turns)
            float totalWeight = 0f;
            int chosen = outgoing[0];
            for (int i = 0; i < outgoing.Count; i++)
            {
                int eid = outgoing[i];
                if (forceAlternative && eid == _nextEdgeId && outgoing.Count > 1) continue;
                TrafficEdge cand = _graph.edges[eid];
                float w = cand.turnType == TrafficTurnType.Straight ? 0.60f : 0.20f;
                totalWeight += w;
            }

            float roll = (_manager != null ? _manager.NextRandom01() : UnityEngine.Random.value) * totalWeight;
            float accum = 0f;
            for (int i = 0; i < outgoing.Count; i++)
            {
                int eid = outgoing[i];
                if (forceAlternative && eid == _nextEdgeId && outgoing.Count > 1) continue;
                TrafficEdge cand = _graph.edges[eid];
                float w = cand.turnType == TrafficTurnType.Straight ? 0.60f : 0.20f;
                accum += w;
                if (roll <= accum)
                {
                    chosen = eid;
                    break;
                }
            }

            _nextEdgeId = chosen;
        }

        public void RerouteAtNextIntersection()
        {
            if (_reservedIntersectionIndex >= 0 && _graph != null)
            {
                _graph.ReleaseIntersection(_reservedIntersectionIndex, vehicleId);
                _reservedIntersectionIndex = -1;
            }
            ChooseNextEdge(forceAlternative: true);
            if (!_sensorBlockedByPlayer && !_overlappingPlayer && !_sensorBlockedByCar && !_overlappingCar)
            {
                _rerouteClearanceTimer = 2.5f;
            }
        }
    }
}
