using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EcoQuest.CityTraffic
{
    [DisallowMultipleComponent]
    public class RailCrossingController : MonoBehaviour
    {
        private const string AllowedScenePath = "Assets/EcoQuest/City Scene.unity";

        [Header("Crossing Geometry & Trigger")]
        public TrafficTrainController train;
        public Vector3 crossingCenter = new Vector3(72.0f, 0.0f, -54.0f);
        public float activationDistance = 60.0f;
        public float westStopLineX = 64.5f;
        public float eastStopLineX = 79.5f;

        [Header("Barrier Gate Arms & Flashing Lights")]
        public Transform westBarrierArm;
        public Transform eastBarrierArm;
        public float openAngleZ = -80.0f;
        public float closedAngleZ = 0.0f;
        public float barrierRotateSpeed = 95.0f;
        public List<MeshRenderer> redLightGroupA = new List<MeshRenderer>();
        public List<MeshRenderer> redLightGroupB = new List<MeshRenderer>();
        public AudioSource bellAudioSource;

        [Header("Runtime State (Read-Only)")]
        [SerializeField] private bool _isCrossingActive;
        [SerializeField] private float _currentBarrierAngleZ = -80.0f;
        [SerializeField] private float _bellTimer;

        private float _simTime;
        private static MaterialPropertyBlock _mpbLitRed;
        private static MaterialPropertyBlock _mpbDarkRed;
        private static AudioClip _sharedBellDingClip;
        private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");

        public bool IsCrossingActive => _isCrossingActive;

        private void Awake()
        {
            if (!IsInAllowedScene())
            {
                enabled = false;
                return;
            }
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (!IsInAllowedScene())
            {
                enabled = false;
                return;
            }
            ResolveReferences();
        }

        private void OnDisable()
        {
            if (bellAudioSource != null && bellAudioSource.isPlaying)
            {
                bellAudioSource.Stop();
            }
        }

        private static bool IsInAllowedScene()
        {
            Scene active = SceneManager.GetActiveScene();
            return active.IsValid() && active.path == AllowedScenePath;
        }

        public void ResolveReferences()
        {
            if (train == null)
            {
                train = FindFirstObjectByType<TrafficTrainController>(FindObjectsInactive.Include);
            }

            if (westBarrierArm == null)
            {
                GameObject p3 = GameObject.Find("Parking_03 (3)");
                if (p3 != null) westBarrierArm = p3.transform.Find("Parking_03_2");
            }
            if (eastBarrierArm == null)
            {
                GameObject p4 = GameObject.Find("Parking_03 (4)");
                if (p4 != null) eastBarrierArm = p4.transform.Find("Parking_03_2");
            }

            if (bellAudioSource == null)
            {
                bellAudioSource = GetComponent<AudioSource>();
                if (bellAudioSource == null) bellAudioSource = gameObject.AddComponent<AudioSource>();
                bellAudioSource.playOnAwake = false;
                bellAudioSource.spatialBlend = 1.0f;
                bellAudioSource.minDistance = 5.0f;
                bellAudioSource.maxDistance = 42.0f;
                bellAudioSource.volume = 0.28f;
            }
        }

        private void Update()
        {
            if (Time.timeScale <= 0.0001f) return;
            StepSimulation(Time.deltaTime);
        }

        public void StepSimulation(float dt)
        {
            _simTime += dt;
            if (train == null) ResolveReferences();

            bool shouldActivate = false;
            if (train != null && train.gameObject.activeInHierarchy && !train.IsWaitingInTunnel)
            {
                float noseZ = train.FrontWorldZ;
                float tailZ = train.RearWorldZ;
                float crossZ = crossingCenter.z;

                // Train moves Northbound (+Z): active when nose is within 60m before crossing until tail is 12m past crossing
                float distToCrossing = crossZ - noseZ;
                float tailPastCrossing = tailZ - crossZ;
                if (distToCrossing <= activationDistance && tailPastCrossing <= 10.0f)
                {
                    shouldActivate = true;
                }
            }

            _isCrossingActive = shouldActivate;

            float targetAngle = _isCrossingActive ? closedAngleZ : openAngleZ;
            _currentBarrierAngleZ = Mathf.MoveTowards(_currentBarrierAngleZ, targetAngle, barrierRotateSpeed * dt);

            Quaternion armRot = Quaternion.Euler(0f, 0f, _currentBarrierAngleZ);
            if (westBarrierArm != null) westBarrierArm.localRotation = armRot;
            if (eastBarrierArm != null) eastBarrierArm.localRotation = armRot;

            UpdateCrossingLightsAndBell(dt);
        }

        private void UpdateCrossingLightsAndBell(float dt)
        {
            EnsurePropertyBlocks();

            bool phaseA = false;
            bool phaseB = false;
            if (_isCrossingActive)
            {
                int tick = Mathf.FloorToInt(_simTime * 3.0f);
                phaseA = (tick % 2) == 0;
                phaseB = !phaseA;

                _bellTimer -= dt;
                if (_bellTimer <= 0f)
                {
                    _bellTimer = 0.55f;
                    if (Application.isPlaying && bellAudioSource != null && bellAudioSource.isActiveAndEnabled)
                    {
                        bellAudioSource.PlayOneShot(GetOrCreateBellDingClip(), 0.28f);
                    }
                }
            }
            else
            {
                _bellTimer = 0f;
            }

            for (int i = 0; i < redLightGroupA.Count; i++)
            {
                if (redLightGroupA[i] != null)
                {
                    redLightGroupA[i].SetPropertyBlock(phaseA ? _mpbLitRed : _mpbDarkRed);
                }
            }
            for (int i = 0; i < redLightGroupB.Count; i++)
            {
                if (redLightGroupB[i] != null)
                {
                    redLightGroupB[i].SetPropertyBlock(phaseB ? _mpbLitRed : _mpbDarkRed);
                }
            }
        }

        private static void EnsurePropertyBlocks()
        {
            if (_mpbLitRed != null) return;
            _mpbLitRed = new MaterialPropertyBlock();
            Color lit = new Color(2.4f, 0.12f, 0.12f, 1f);
            _mpbLitRed.SetColor(BaseColorPropId, lit);
            _mpbLitRed.SetColor(ColorPropId, lit);

            _mpbDarkRed = new MaterialPropertyBlock();
            Color dark = new Color(0.16f, 0.04f, 0.04f, 1f);
            _mpbDarkRed.SetColor(BaseColorPropId, dark);
            _mpbDarkRed.SetColor(ColorPropId, dark);
        }

        private static AudioClip GetOrCreateBellDingClip()
        {
            if (_sharedBellDingClip != null) return _sharedBellDingClip;
            int sampleRate = 22050;
            int count = sampleRate / 3;
            float[] data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate;
                float env = Mathf.Exp(-9.5f * t);
                float s = 0.65f * Mathf.Sin(2f * Mathf.PI * 1174.66f * t) +
                          0.35f * Mathf.Sin(2f * Mathf.PI * 2349.32f * t);
                data[i] = env * s * 0.30f;
            }
            _sharedBellDingClip = AudioClip.Create("RailCrossingBellDing", count, 1, sampleRate, false);
            _sharedBellDingClip.SetData(data, 0);
            return _sharedBellDingClip;
        }
    }
}
