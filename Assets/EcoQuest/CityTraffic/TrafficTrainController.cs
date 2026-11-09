using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace EcoQuest.CityTraffic
{
    [DisallowMultipleComponent]
    public class TrafficTrainController : MonoBehaviour
    {
        private const string AllowedScenePath = "Assets/EcoQuest/City Scene.unity";

        [Header("Rail Path & Speed")]
        public float railX = 72.0f;
        public float railY = 0.29f;
        public float southTunnelZ = -135.0f;
        public float northTunnelZ = 170.0f;
        public float cruiseSpeed = 9.0f;
        public float acceleration = 2.5f;
        public float brakingDeceleration = 6.0f;
        public float waitAtTunnelSeconds = 60.0f;

        [Header("Train Geometry & Sway")]
        public float frontNoseOffsetZ = 10.65f;
        public float rearTailOffsetZ = -49.72f;
        public float swayAmplitudeDeg = 0.45f;
        public float swayFrequency = 1.6f;

        [Header("Safety & Audio (Null-Safe)")]
        public float trackClearanceHalfWidth = 2.4f;
        public float obstacleStopLookahead = 18.0f;
        public AudioSource hornAudioSource;
        public AudioSource rumbleAudioSource;
        public AudioClip hornClip;

        [Header("Runtime State (Read-Only)")]
        [SerializeField] private float _currentSpeed;
        [SerializeField] private float _waitTimer;
        [SerializeField] private bool _waitingInTunnel;
        [SerializeField] private bool _blockedByTrackObstacle;
        [SerializeField] private float _distanceTraveled;

        private Transform _carriage1;
        private Transform _carriage2;
        private float _swayPhase;
        private bool _hasBlownCrossingHorn;
        private bool _hasBlownObstacleHorn;

        private static readonly Collider[] SharedTrackOverlap = new Collider[16];
        private static AudioClip _sharedTrainHornClip;
        private static AudioClip _sharedTrainRumbleClip;

        public float CurrentSpeed => _currentSpeed;
        public bool IsWaitingInTunnel => _waitingInTunnel;
        public bool IsBlockedByObstacle => _blockedByTrackObstacle;
        public float DistanceTraveled => _distanceTraveled;
        public float FrontWorldZ => transform.position.z + frontNoseOffsetZ;
        public float RearWorldZ => transform.position.z + rearTailOffsetZ;

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

        private void OnDisable()
        {
            StopAllCoroutines();
            if (hornAudioSource != null && hornAudioSource.isPlaying) hornAudioSource.Stop();
            if (rumbleAudioSource != null && rumbleAudioSource.isPlaying) rumbleAudioSource.Stop();
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

            // Replace non-convex MeshColliders with clean kinematic BoxColliders on locomotive and carriages
            MeshCollider[] mcs = GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < mcs.Length; i++)
            {
                GameObject target = mcs[i].gameObject;
                Bounds localB = mcs[i].sharedMesh != null ? mcs[i].sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(3.4f, 4.5f, 18.5f));
                DestroyImmediate(mcs[i]);
                BoxCollider bc = target.GetComponent<BoxCollider>();
                if (bc == null) bc = target.AddComponent<BoxCollider>();
                bc.center = localB.center;
                bc.size = localB.size;
            }

            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            _carriage1 = transform.Find("Train_02");
            _carriage2 = transform.Find("Train_02 (1)");

            // Setup 3D horn + track rumble audio
            if (hornAudioSource == null)
            {
                hornAudioSource = GetComponent<AudioSource>();
                if (hornAudioSource == null) hornAudioSource = gameObject.AddComponent<AudioSource>();
                hornAudioSource.playOnAwake = false;
                hornAudioSource.spatialBlend = 1.0f;
                hornAudioSource.minDistance = 8.0f;
                hornAudioSource.maxDistance = 85.0f;
                hornAudioSource.volume = 0.45f;
            }

            Transform rumbleTr = transform.Find("TrainRumbleAudio");
            if (rumbleTr == null)
            {
                GameObject rGo = new GameObject("TrainRumbleAudio");
                rGo.layer = gameObject.layer;
                rumbleTr = rGo.transform;
                rumbleTr.SetParent(transform, false);
                rumbleTr.localPosition = new Vector3(0f, 1.2f, -15f);
            }
            rumbleAudioSource = rumbleTr.GetComponent<AudioSource>();
            if (rumbleAudioSource == null) rumbleAudioSource = rumbleTr.gameObject.AddComponent<AudioSource>();
            rumbleAudioSource.playOnAwake = false;
            rumbleAudioSource.loop = true;
            rumbleAudioSource.spatialBlend = 1.0f;
            rumbleAudioSource.minDistance = 6.0f;
            rumbleAudioSource.maxDistance = 55.0f;
            rumbleAudioSource.volume = 0.22f;
            rumbleAudioSource.clip = GetOrCreateTrainRumbleClip();

            _waitingInTunnel = false;
            _waitTimer = 0f;
            _currentSpeed = 0f;
            _distanceTraveled = 0f;
            _hasBlownCrossingHorn = false;
            transform.position = new Vector3(railX, railY, -53.20f);
            transform.rotation = Quaternion.identity;
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
            if (_waitingInTunnel)
            {
                _currentSpeed = 0f;
                _waitTimer -= dt;
                if (_waitTimer <= 0f)
                {
                    _waitingInTunnel = false;
                    _hasBlownCrossingHorn = false;
                    transform.position = new Vector3(railX, railY, southTunnelZ);
                    transform.rotation = Quaternion.identity;
                }
                UpdateAudio();
                return;
            }

            _blockedByTrackObstacle = CheckPlayerOrNpcOnTracksAhead(out float obstacleDist);

            float targetSpeed = cruiseSpeed;
            if (_blockedByTrackObstacle)
            {
                targetSpeed = obstacleDist <= 4.0f ? 0f : Mathf.Min(cruiseSpeed, Mathf.Sqrt(2f * brakingDeceleration * Mathf.Max(0f, obstacleDist - 3.5f)));
                if (!_hasBlownObstacleHorn)
                {
                    _hasBlownObstacleHorn = true;
                    PlayHorn();
                }
            }
            else
            {
                _hasBlownObstacleHorn = false;
            }

            if (_currentSpeed < targetSpeed)
            {
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, acceleration * dt);
            }
            else
            {
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, brakingDeceleration * dt);
            }

            // Friendly whistle when approaching the z = -54 road crossing
            float frontZ = FrontWorldZ;
            if (!_hasBlownCrossingHorn && frontZ >= -95f && frontZ <= -56f && _currentSpeed > 1.0f)
            {
                _hasBlownCrossingHorn = true;
                PlayHorn();
            }

            float dz = _currentSpeed * dt;
            _distanceTraveled += dz;
            Vector3 pos = transform.position;
            pos.x = railX;
            pos.y = railY;
            pos.z += dz;

            // Gentle locomotive & carriage sway
            if (_currentSpeed > 0.1f)
            {
                _swayPhase += dt * swayFrequency * Mathf.Clamp01(_currentSpeed / cruiseSpeed);
            }
            float roll = Mathf.Sin(_swayPhase * Mathf.PI * 2f) * swayAmplitudeDeg * Mathf.Clamp01(_currentSpeed / cruiseSpeed);
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 0f, roll));

            if (_carriage1 != null)
            {
                float r1 = Mathf.Sin((_swayPhase - 0.35f) * Mathf.PI * 2f) * swayAmplitudeDeg * Mathf.Clamp01(_currentSpeed / cruiseSpeed);
                _carriage1.localRotation = Quaternion.Euler(0f, 0f, r1 - roll);
            }
            if (_carriage2 != null)
            {
                float r2 = Mathf.Sin((_swayPhase - 0.70f) * Mathf.PI * 2f) * swayAmplitudeDeg * Mathf.Clamp01(_currentSpeed / cruiseSpeed);
                _carriage2.localRotation = Quaternion.Euler(0f, 0f, r2 - roll);
            }

            // When the entire train has entered the North mountain tunnel (z >= northTunnelZ), wait before next run
            if (pos.z >= northTunnelZ)
            {
                _waitingInTunnel = true;
                _waitTimer = Mathf.Clamp(waitAtTunnelSeconds, 60f, 120f);
                _currentSpeed = 0f;
            }

            UpdateAudio();
        }

        private bool CheckPlayerOrNpcOnTracksAhead(out float minDistanceAhead)
        {
            minDistanceAhead = 999f;
            float noseZ = FrontWorldZ;
            bool blocked = false;

            // Check Player transform directly
            if (CityTrafficManager.Instance != null)
            {
                Transform playerTr = CityTrafficManager.Instance.GetPlayerTransform();
                if (playerTr != null)
                {
                    Vector3 pp = playerTr.position;
                    if (Mathf.Abs(pp.x - railX) <= trackClearanceHalfWidth && pp.z >= noseZ - 1.0f && pp.z <= noseZ + obstacleStopLookahead)
                    {
                        minDistanceAhead = Mathf.Min(minDistanceAhead, Mathf.Max(0f, pp.z - noseZ));
                        blocked = true;
                    }
                }
            }

            // Non-alloc overlap box ahead of train nose
            Vector3 boxCenter = new Vector3(railX, railY + 1.0f, noseZ + obstacleStopLookahead * 0.5f);
            Vector3 boxHalf = new Vector3(trackClearanceHalfWidth, 1.5f, obstacleStopLookahead * 0.5f);
            int count = Physics.OverlapBoxNonAlloc(boxCenter, boxHalf, SharedTrackOverlap, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider col = SharedTrackOverlap[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;
                if (col.GetComponentInParent<CharacterController>() != null || col.GetComponentInParent<NavMeshAgent>() != null)
                {
                    float d = Mathf.Max(0f, col.transform.position.z - noseZ);
                    minDistanceAhead = Mathf.Min(minDistanceAhead, d);
                    blocked = true;
                }
            }

            return blocked;
        }

        private void UpdateAudio()
        {
            if (!Application.isPlaying || rumbleAudioSource == null) return;
            if (_currentSpeed > 0.25f && !_waitingInTunnel && Time.timeScale > 0.001f)
            {
                if (!rumbleAudioSource.isPlaying && rumbleAudioSource.isActiveAndEnabled)
                {
                    rumbleAudioSource.Play();
                }
                float ratio = Mathf.Clamp01(_currentSpeed / cruiseSpeed);
                rumbleAudioSource.pitch = Mathf.Lerp(0.75f, 1.15f, ratio);
                rumbleAudioSource.volume = Mathf.Lerp(0.08f, 0.24f, ratio);
            }
            else if (rumbleAudioSource.isPlaying)
            {
                rumbleAudioSource.Pause();
            }
        }

        public void PlayHorn()
        {
            if (!Application.isPlaying || hornAudioSource == null || !hornAudioSource.isActiveAndEnabled) return;
            AudioClip clip = hornClip != null ? hornClip : GetOrCreateTrainHornClip();
            if (clip != null)
            {
                hornAudioSource.PlayOneShot(clip, 0.45f);
            }
        }

        private static AudioClip GetOrCreateTrainHornClip()
        {
            if (_sharedTrainHornClip != null) return _sharedTrainHornClip;
            int sampleRate = 22050;
            int samplesCount = (int)(sampleRate * 0.65f);
            float[] data = new float[samplesCount];
            for (int i = 0; i < samplesCount; i++)
            {
                float t = (float)i / sampleRate;
                float env = t < 0.05f ? (t / 0.05f) : (t > 0.50f ? Mathf.Clamp01((0.65f - t) / 0.15f) : 1.0f);
                // Warm train chord (F4 + A4 + C5)
                float s = 0.45f * Mathf.Sin(2f * Mathf.PI * 349.23f * t) +
                          0.35f * Mathf.Sin(2f * Mathf.PI * 440.00f * t) +
                          0.20f * Mathf.Sin(2f * Mathf.PI * 523.25f * t);
                data[i] = env * s * 0.30f;
            }
            _sharedTrainHornClip = AudioClip.Create("FriendlyTrainHorn", samplesCount, 1, sampleRate, false);
            _sharedTrainHornClip.SetData(data, 0);
            return _sharedTrainHornClip;
        }

        private static AudioClip GetOrCreateTrainRumbleClip()
        {
            if (_sharedTrainRumbleClip != null) return _sharedTrainRumbleClip;
            int sampleRate = 22050;
            int samplesCount = sampleRate;
            float[] data = new float[samplesCount];
            for (int i = 0; i < samplesCount; i++)
            {
                float t = (float)i / sampleRate;
                float cadence = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 4.0f * t);
                float wave = 0.6f * Mathf.Sin(2f * Mathf.PI * 48f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 96f * t);
                data[i] = cadence * wave * 0.25f;
            }
            _sharedTrainRumbleClip = AudioClip.Create("TrainTrackRumble", samplesCount, 1, sampleRate, false);
            _sharedTrainRumbleClip.SetData(data, 0);
            return _sharedTrainRumbleClip;
        }
    }
}
