using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EcoQuest.CityTraffic
{
    [DisallowMultipleComponent]
    public class TrafficBalloonController : MonoBehaviour
    {
        private const string AllowedScenePath = "Assets/EcoQuest/City Scene.unity";

        [Header("Drift & Bob Settings")]
        public Vector3 loopCenter = new Vector3(-27.68f, 46.5f, -8.40f);
        public float loopRadius = 48.0f;
        public float driftSpeed = 1.35f;
        public float verticalBobAmplitude = 1.5f;
        public float verticalBobPeriod = 9.0f;
        public float swayAmplitudeDeg = 2.0f;
        public float yawSpinDegPerSec = 5.5f;

        [Header("Burner Flame Flicker")]
        public Transform burnerFlameVisual;
        public Light burnerPointLight;

        [Header("Runtime State (Read-Only)")]
        [SerializeField] private float _angleRad = -Mathf.PI * 0.5f;
        [SerializeField] private float _simTime;
        [SerializeField] private float _distanceTraveled;

        public float DistanceTraveled => _distanceTraveled;

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

            // Disable collider on balloon
            Collider[] cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) cols[i].enabled = false;
            }

            // Ensure subtle burner flame glow child exists
            Transform flameTr = transform.Find("BurnerFlameGlow");
            if (flameTr == null)
            {
                GameObject flameGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                flameGo.name = "BurnerFlameGlow";
                flameGo.layer = gameObject.layer;
                Collider c = flameGo.GetComponent<Collider>();
                if (c != null) DestroyImmediate(c);
                flameTr = flameGo.transform;
                flameTr.SetParent(transform, false);
                flameTr.localPosition = new Vector3(0f, -2.65f, 0f);
                flameTr.localScale = new Vector3(0.42f, 0.65f, 0.42f);

                MeshRenderer mr = flameGo.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }
            burnerFlameVisual = flameTr;
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
            _simTime += dt;
            float angularVel = driftSpeed / Mathf.Max(10f, loopRadius);
            _angleRad = Mathf.Repeat(_angleRad + angularVel * dt, Mathf.PI * 2f);

            float bobY = Mathf.Sin((_simTime / Mathf.Max(1f, verticalBobPeriod)) * Mathf.PI * 2f) * verticalBobAmplitude;
            Vector3 nextPos = new Vector3(
                loopCenter.x + Mathf.Cos(_angleRad) * loopRadius,
                loopCenter.y + bobY,
                loopCenter.z + Mathf.Sin(_angleRad) * loopRadius);

            _distanceTraveled += Vector3.Distance(transform.position, nextPos);

            float swayX = Mathf.Sin(_simTime * 1.1f) * swayAmplitudeDeg;
            float swayZ = Mathf.Cos(_simTime * 0.9f) * swayAmplitudeDeg;
            float yaw = Mathf.Repeat(_simTime * yawSpinDegPerSec, 360f);
            transform.SetPositionAndRotation(nextPos, Quaternion.Euler(swayX, yaw, swayZ));

            if (burnerFlameVisual != null)
            {
                float pulse = 0.35f + 0.14f * Mathf.Sin(_simTime * 11.0f) + 0.08f * Mathf.Cos(_simTime * 17.0f);
                burnerFlameVisual.localScale = new Vector3(pulse, pulse * 1.55f, pulse);
            }
        }
    }
}
