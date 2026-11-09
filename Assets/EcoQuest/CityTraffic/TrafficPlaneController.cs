using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EcoQuest.CityTraffic
{
    public enum PlaneFlightMode
    {
        RunwayCycle = 0,
        AerialOrbit = 1
    }

    public enum PlaneRunwayPhase
    {
        InitialStaggerTaxi = 0,
        TakeoffRoll = 1,
        ClimbOut = 2,
        PerimeterCircuit = 3,
        FinalApproach = 4,
        LandingRollout = 5
    }

    [DisallowMultipleComponent]
    public class TrafficPlaneController : MonoBehaviour
    {
        private const string AllowedScenePath = "Assets/EcoQuest/City Scene.unity";

        [Header("Flight Configuration")]
        public PlaneFlightMode flightMode = PlaneFlightMode.RunwayCycle;
        public float cruiseSpeed = 17.5f;
        public float cruiseAltitude = 58.0f;
        public Vector3 orbitCenter = new Vector3(-18.0f, 58.0f, -6.0f);
        public float orbitRadiusX = 128.0f;
        public float orbitRadiusZ = 104.0f;
        public float initialOrbitAngleRad;
        public float staggerDelaySeconds;
        public float propellerSpinSpeed = 1600.0f;

        [Header("Runway Coordinates (South Airport Strip)")]
        public float runwayZ = -89.98f;
        public float runwayGroundY = 1.25f;
        public float runwayStartX = -10.0f;
        public float runwayLiftoffX = 24.0f;
        public float runwayClimbEndX = 72.0f;

        [Header("References")]
        public Transform propellerTransform;
        public Vector3 homePosition;

        [Header("Runtime State (Read-Only)")]
        [SerializeField] private PlaneRunwayPhase _phase = PlaneRunwayPhase.TakeoffRoll;
        [SerializeField] private float _currentSpeed;
        [SerializeField] private float _orbitAngleRad;
        [SerializeField] private float _phaseTimer;
        [SerializeField] private float _distanceTraveled;

        private Vector3 _initialPos;
        private float _propellerAngle;

        public float CurrentSpeed => _currentSpeed;
        public float DistanceTraveled => _distanceTraveled;
        public PlaneRunwayPhase CurrentPhase => _phase;

        private void Awake()
        {
            if (!IsInAllowedScene())
            {
                enabled = false;
                return;
            }
            EnsureSetup();
        }

        private void OnEnable()
        {
            if (!IsInAllowedScene())
            {
                enabled = false;
                return;
            }
            EnsureSetup();
        }

        private static bool IsInAllowedScene()
        {
            Scene active = SceneManager.GetActiveScene();
            return active.IsValid() && active.path == AllowedScenePath;
        }

        public void EnsureSetup()
        {
            int trafficLayer = LayerMask.NameToLayer("Traffic");
            if (trafficLayer >= 0)
            {
                SetLayerRecursively(transform, trafficLayer);
            }

            // Disable all colliders so flying/taxiing planes never block or collide with props/player
            Collider[] cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) cols[i].enabled = false;
            }

            // Find propeller child transform (Plane_01_2, Plane_04_2, Plane_05_2)
            if (propellerTransform == null)
            {
                Transform[] children = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i] != transform && children[i].name.EndsWith("_2", StringComparison.OrdinalIgnoreCase))
                    {
                        propellerTransform = children[i];
                        break;
                    }
                }
            }

            if (homePosition.sqrMagnitude > 0.01f)
            {
                transform.position = homePosition;
                if (flightMode == PlaneFlightMode.RunwayCycle)
                {
                    transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                }
            }
            _initialPos = transform.position;
            _orbitAngleRad = initialOrbitAngleRad;
            _distanceTraveled = 0f;

            if (flightMode == PlaneFlightMode.AerialOrbit)
            {
                _phase = PlaneRunwayPhase.PerimeterCircuit;
                _currentSpeed = cruiseSpeed;
            }
            else
            {
                if (staggerDelaySeconds > 0.05f)
                {
                    _phase = PlaneRunwayPhase.InitialStaggerTaxi;
                    _phaseTimer = staggerDelaySeconds;
                    _currentSpeed = 3.5f;
                }
                else
                {
                    _phase = PlaneRunwayPhase.TakeoffRoll;
                    _currentSpeed = 4.0f;
                }
            }
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
            {
                SetLayerRecursively(root.GetChild(i), layer);
            }
        }

        private void Update()
        {
            if (Time.timeScale <= 0.0001f) return;
            StepSimulation(Time.deltaTime);
        }

        public void StepSimulation(float dt)
        {
            // Spin propeller continuously
            if (propellerTransform != null)
            {
                _propellerAngle = Mathf.Repeat(_propellerAngle + propellerSpinSpeed * dt, 360f);
                propellerTransform.localRotation = Quaternion.Euler(0f, 0f, _propellerAngle);
            }

            if (flightMode == PlaneFlightMode.AerialOrbit)
            {
                StepAerialOrbit(dt);
                return;
            }

            switch (_phase)
            {
                case PlaneRunwayPhase.InitialStaggerTaxi:
                {
                    // Smoothly taxi from apron parking spot toward runway lineup while waiting stagger timer
                    _phaseTimer -= dt;
                    Vector3 targetLineup = new Vector3(Mathf.Min(_initialPos.x, runwayLiftoffX - 6f), runwayGroundY, runwayZ);
                    Vector3 pos = Vector3.MoveTowards(transform.position, targetLineup, 3.8f * dt);
                    _distanceTraveled += Vector3.Distance(transform.position, pos);
                    Quaternion targetRot = Quaternion.Euler(0f, 90f, 0f);
                    transform.SetPositionAndRotation(pos, Quaternion.RotateTowards(transform.rotation, targetRot, 75f * dt));
                    if (_phaseTimer <= 0f)
                    {
                        _phase = PlaneRunwayPhase.TakeoffRoll;
                        _currentSpeed = 5.0f;
                    }
                    break;
                }

                case PlaneRunwayPhase.TakeoffRoll:
                {
                    _currentSpeed = Mathf.MoveTowards(_currentSpeed, cruiseSpeed, 6.5f * dt);
                    Vector3 pos = transform.position;
                    pos.z = Mathf.MoveTowards(pos.z, runwayZ, 5.0f * dt);
                    pos.y = runwayGroundY;
                    float dx = _currentSpeed * dt;
                    pos.x += dx;
                    _distanceTraveled += dx;

                    float nosePitch = pos.x >= (runwayLiftoffX - 6f) ? -10.0f : 0f;
                    Quaternion rot = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(nosePitch, 90f, 0f), 60f * dt);
                    transform.SetPositionAndRotation(pos, rot);

                    if (pos.x >= runwayLiftoffX)
                    {
                        _phase = PlaneRunwayPhase.ClimbOut;
                    }
                    break;
                }

                case PlaneRunwayPhase.ClimbOut:
                {
                    _currentSpeed = Mathf.MoveTowards(_currentSpeed, cruiseSpeed, 4.0f * dt);
                    // Climb smoothly toward the southern entry of the perimeter orbit at altitude >= 52m
                    Vector3 entryPoint = GetOrbitPoint(initialOrbitAngleRad, out Vector3 entryTangent);
                    Vector3 toEntry = entryPoint - transform.position;
                    float dist = toEntry.magnitude;
                    Vector3 step = Vector3.MoveTowards(transform.position, entryPoint, _currentSpeed * dt);
                    _distanceTraveled += Vector3.Distance(transform.position, step);

                    Vector3 moveDir = dist > 0.5f ? toEntry.normalized : entryTangent;
                    Quaternion look = Quaternion.LookRotation(moveDir, Vector3.up) * Quaternion.Euler(0f, 0f, 14f);
                    transform.SetPositionAndRotation(step, Quaternion.Slerp(transform.rotation, look, 3.5f * dt));

                    if (dist <= 4.0f)
                    {
                        _orbitAngleRad = initialOrbitAngleRad;
                        _phase = PlaneRunwayPhase.PerimeterCircuit;
                    }
                    break;
                }

                case PlaneRunwayPhase.PerimeterCircuit:
                {
                    StepAerialOrbit(dt);
                    break;
                }
            }
        }

        private void StepAerialOrbit(float dt)
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, cruiseSpeed, 3.5f * dt);
            float avgRadius = 0.5f * (orbitRadiusX + orbitRadiusZ);
            float angularVel = _currentSpeed / Mathf.Max(40f, avgRadius);
            _orbitAngleRad = Mathf.Repeat(_orbitAngleRad + angularVel * dt, Mathf.PI * 2f);

            Vector3 targetPos = GetOrbitPoint(_orbitAngleRad, out Vector3 tangent);
            // Smooth transition from initial spawn height (e.g. y=16m or y=27m) up to safe cruiseAltitude (>= 52m)
            float newY = Mathf.MoveTowards(transform.position.y, targetPos.y, 6.5f * dt);
            targetPos.y = Mathf.Max(newY, targetPos.y);

            _distanceTraveled += Vector3.Distance(transform.position, targetPos);

            float pitchDeg = (targetPos.y - transform.position.y) > 0.02f ? -8.0f : 0f;
            float bankDeg = 20.0f; // Counterclockwise left turn bank
            Quaternion desiredRot = Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(pitchDeg, 0f, bankDeg);
            transform.SetPositionAndRotation(targetPos, Quaternion.Slerp(transform.rotation, desiredRot, 4.5f * dt));
        }

        private Vector3 GetOrbitPoint(float angleRad, out Vector3 tangent)
        {
            float cos = Mathf.Cos(angleRad);
            float sin = Mathf.Sin(angleRad);
            float altWave = Mathf.Sin(angleRad * 2f) * 2.5f;
            Vector3 pos = new Vector3(
                orbitCenter.x + cos * orbitRadiusX,
                Mathf.Max(52.0f, cruiseAltitude + altWave),
                orbitCenter.z + sin * orbitRadiusZ);

            Vector3 deriv = new Vector3(-sin * orbitRadiusX, 0f, cos * orbitRadiusZ);
            tangent = deriv.sqrMagnitude > 0.001f ? deriv.normalized : Vector3.forward;
            return pos;
        }
    }
}
