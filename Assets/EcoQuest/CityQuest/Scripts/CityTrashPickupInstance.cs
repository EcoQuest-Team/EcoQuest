using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EcoQuest.CityQuest
{
    [DisallowMultipleComponent]
    public class CityTrashVisualVariant : MonoBehaviour
    {
        public MeshFilter targetMeshFilter;
        public MeshRenderer targetRenderer;
        public Mesh[] variantMeshes;
        public Material[] variantMaterials;

        public void ApplyVariant(int seed)
        {
            if (targetMeshFilter == null) targetMeshFilter = GetComponentInChildren<MeshFilter>();
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<MeshRenderer>();

            int abs = Mathf.Abs(seed);
            if (targetMeshFilter != null && variantMeshes != null && variantMeshes.Length > 0)
            {
                Mesh m = variantMeshes[abs % variantMeshes.Length];
                if (m != null) targetMeshFilter.sharedMesh = m;
            }
            if (targetRenderer != null && variantMaterials != null && variantMaterials.Length > 0)
            {
                Material mat = variantMaterials[(abs / 3) % variantMaterials.Length];
                if (mat != null) targetRenderer.sharedMaterial = mat;
            }
        }
    }

    [DisallowMultipleComponent]
    public class CityTrashPickupInstance : MonoBehaviour
    {
        [Header("Runtime State")]
        public int checklistSlotIndex = -1;
        public CityTrashItemDef itemDef;
        public CityDistrict spawnedDistrict;
        public bool isCollected;
        public bool isCollectingAnim;

        [Header("References")]
        public Transform visualContainer;
        public Transform shimmerAnchor;
        public SphereCollider pickupCollider;

        private readonly Dictionary<string, GameObject> cachedItemVisuals = new Dictionary<string, GameObject>(32);
        private GameObject activeItemVisual;
        private Vector3 baseLocalPos;
        private Vector3 baseScale = Vector3.one;
        private float bobPhase;

        private void Awake()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene())
            {
                enabled = false;
                gameObject.SetActive(false);
                return;
            }
        }

        private void OnEnable()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene())
            {
                enabled = false;
                return;
            }
        }

        public void PrewarmVisuals(CityTrashDatabase db)
        {
            if (visualContainer == null)
            {
                GameObject vc = new GameObject("VisualContainer");
                vc.transform.SetParent(transform, false);
                visualContainer = vc.transform;
            }

            if (shimmerAnchor == null)
            {
                Transform existingShimmer = transform.Find("ShimmerAnchor");
                if (existingShimmer != null)
                {
                    shimmerAnchor = existingShimmer;
                }
                else
                {
                    GameObject sa = BuildShimmerRing();
                    sa.transform.SetParent(transform, false);
                    sa.transform.localPosition = new Vector3(0f, 0.22f, 0f);
                    shimmerAnchor = sa.transform;
                }
            }

            if (pickupCollider == null)
            {
                pickupCollider = GetComponent<SphereCollider>();
                if (pickupCollider == null)
                {
                    pickupCollider = gameObject.AddComponent<SphereCollider>();
                    pickupCollider.radius = 0.42f;
                    pickupCollider.center = new Vector3(0f, 0.20f, 0f);
                    pickupCollider.isTrigger = false;
                }
            }

            if (db == null || db.items == null) return;

            for (int i = 0; i < db.items.Count; i++)
            {
                var def = db.items[i];
                if (def == null || string.IsNullOrEmpty(def.id) || def.worldPrefab == null || visualContainer == null) continue;
                if (cachedItemVisuals.ContainsKey(def.id)) continue;

                GameObject inst = Instantiate(def.worldPrefab, visualContainer);
                inst.name = def.id;
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.SetActive(false);
                cachedItemVisuals[def.id] = inst;
            }
        }

        private GameObject BuildShimmerRing()
        {
            GameObject root = new GameObject("ShimmerAnchor");
            // Low-poly diamond sparkles around the item so it reads clearly from 15m without garish beams
            Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (urpUnlit == null) urpUnlit = Shader.Find("Unlit/Color");
            Material mat = new Material(urpUnlit != null ? urpUnlit : Shader.Find("Standard"));
            Color gold = new Color(1f, 0.90f, 0.35f, 0.95f);
            mat.color = gold;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", gold);
            mat.enableInstancing = true;

            for (int i = 0; i < 4; i++)
            {
                GameObject spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spark.name = "Spark_" + i;
                Collider col = spark.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);
                spark.transform.SetParent(root.transform, false);
                float angle = i * 90f * Mathf.Deg2Rad;
                spark.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.28f, (i % 2) * 0.08f, Mathf.Sin(angle) * 0.28f);
                spark.transform.localRotation = Quaternion.Euler(45f, 45f + i * 30f, 45f);
                spark.transform.localScale = new Vector3(0.08f, 0.08f, 0.08f);
                Renderer r = spark.GetComponent<Renderer>();
                if (r != null)
                {
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            }
            return root;
        }

        private void EnsureComponentsAndVisuals(CityTrashItemDef def)
        {
            if (visualContainer == null)
            {
                Transform vc = transform.Find("VisualContainer");
                if (vc != null) visualContainer = vc;
                else
                {
                    GameObject go = new GameObject("VisualContainer");
                    go.transform.SetParent(transform, false);
                    visualContainer = go.transform;
                }
            }

            if (shimmerAnchor == null)
            {
                Transform sa = transform.Find("ShimmerAnchor");
                if (sa != null) shimmerAnchor = sa;
                else
                {
                    GameObject sGo = BuildShimmerRing();
                    sGo.transform.SetParent(transform, false);
                    sGo.transform.localPosition = new Vector3(0f, 0.24f, 0f);
                    shimmerAnchor = sGo.transform;
                }
            }

            if (pickupCollider == null)
            {
                pickupCollider = GetComponent<SphereCollider>();
                if (pickupCollider == null)
                {
                    pickupCollider = gameObject.AddComponent<SphereCollider>();
                }
                pickupCollider.radius = 0.50f;
                pickupCollider.center = new Vector3(0f, 0.20f, 0f);
                pickupCollider.isTrigger = false;
            }

            if (def != null && def.worldPrefab != null)
            {
                if (!cachedItemVisuals.TryGetValue(def.id, out GameObject vis) || vis == null)
                {
                    Transform existing = visualContainer.Find(def.id);
                    if (existing != null)
                    {
                        vis = existing.gameObject;
                    }
                    else
                    {
                        vis = Instantiate(def.worldPrefab, visualContainer);
                        vis.name = def.id;
                        vis.transform.localPosition = Vector3.zero;
                        vis.transform.localRotation = Quaternion.identity;
                    }
                    vis.SetActive(false);
                    cachedItemVisuals[def.id] = vis;
                }
            }
        }

        public void ActivateAtSpawn(int slotIndex, CityTrashItemDef def, CityTrashSpawnPoint spawnPoint, int variantSeed)
        {
            checklistSlotIndex = slotIndex;
            itemDef = def;
            spawnedDistrict = spawnPoint != null ? spawnPoint.district : def.GetPrimaryDistrict();
            isCollected = false;
            isCollectingAnim = false;

            EnsureComponentsAndVisuals(def);

            Vector3 pos = spawnPoint != null ? spawnPoint.transform.position : Vector3.zero;
            Vector3 normal = spawnPoint != null && spawnPoint.groundNormal.sqrMagnitude > 0.01f ? spawnPoint.groundNormal.normalized : Vector3.up;

            // Ground-snap verification via Raycast (ignore self)
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out hit, 4.0f, ~0, QueryTriggerInteraction.Ignore))
            {
                pos = hit.point + Vector3.up * 0.035f;
                normal = hit.normal;
            }
            else
            {
                pos += Vector3.up * 0.035f;
            }

            transform.position = pos;

            // Random yaw + subtle surface normal alignment
            float yaw = ((variantSeed * 73) % 360);
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, normal, 0.35f));
            transform.rotation = tilt * Quaternion.Euler(0f, yaw, 0f);

            // Scale variation: base 1.05x with +-8%
            float scaleMult = 1.05f * (0.92f + ((variantSeed & 15) / 15f) * 0.16f);
            baseScale = Vector3.one * scaleMult;
            transform.localScale = baseScale;

            if (activeItemVisual != null)
            {
                activeItemVisual.SetActive(false);
                activeItemVisual = null;
            }

            if (def != null && cachedItemVisuals.TryGetValue(def.id, out GameObject vis) && vis != null)
            {
                activeItemVisual = vis;
                activeItemVisual.SetActive(true);

                var varComp = activeItemVisual.GetComponent<CityTrashVisualVariant>();
                if (varComp != null)
                {
                    varComp.ApplyVariant(variantSeed);
                }

                // Select 1 of the 2-3 visual variants inside the prefab if present
                int childCount = activeItemVisual.transform.childCount;
                int variantCount = 0;
                for (int i = 0; i < childCount; i++)
                {
                    Transform c = activeItemVisual.transform.GetChild(i);
                    if (c.name.StartsWith("Variant_")) variantCount++;
                }

                if (variantCount > 0)
                {
                    int chosen = Mathf.Abs(variantSeed) % variantCount;
                    int vIdx = 0;
                    for (int i = 0; i < childCount; i++)
                    {
                        Transform c = activeItemVisual.transform.GetChild(i);
                        if (c.name.StartsWith("Variant_"))
                        {
                            c.gameObject.SetActive(vIdx == chosen);
                            vIdx++;
                        }
                    }
                }
            }

            if (visualContainer != null)
            {
                baseLocalPos = Vector3.zero;
                visualContainer.localPosition = baseLocalPos;
            }

            if (pickupCollider != null) pickupCollider.enabled = true;
            if (shimmerAnchor != null) shimmerAnchor.gameObject.SetActive(true);

            bobPhase = (slotIndex * 0.7f) % (Mathf.PI * 2f);
            gameObject.SetActive(true);
        }

        public void TickVisuals(float time, Vector3 playerPos)
        {
            if (isCollected || isCollectingAnim || !gameObject.activeSelf) return;

            float dx = transform.position.x - playerPos.x;
            float dz = transform.position.z - playerPos.z;
            float distSq = dx * dx + dz * dz;

            // Only animate shimmer & gentle bob within 22m for zero wasted CPU
            if (distSq <= 484f)
            {
                float bob = Mathf.Sin(time * 2.8f + bobPhase) * 0.025f + 0.025f;
                if (visualContainer != null)
                {
                    visualContainer.localPosition = new Vector3(0f, bob, 0f);
                }
                if (shimmerAnchor != null)
                {
                    shimmerAnchor.localRotation = Quaternion.Euler(0f, (time * 65f + bobPhase * 30f) % 360f, 0f);
                    shimmerAnchor.localPosition = new Vector3(0f, 0.20f + bob * 1.4f, 0f);
                }
            }
        }

        public void PlayPickupAndPool()
        {
            if (isCollected || isCollectingAnim) return;
            isCollected = true;
            isCollectingAnim = true;
            if (pickupCollider != null) pickupCollider.enabled = false;
            if (shimmerAnchor != null) shimmerAnchor.gameObject.SetActive(false);

            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(PickupPopRoutine());
            }
            else
            {
                isCollectingAnim = false;
                gameObject.SetActive(false);
            }
        }

        private IEnumerator PickupPopRoutine()
        {
            float duration = 0.18f;
            float elapsed = 0f;
            Vector3 startScale = baseScale;
            Vector3 peakScale = baseScale * 1.18f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                if (t < 0.35f)
                {
                    float st = t / 0.35f;
                    transform.localScale = Vector3.Lerp(startScale, peakScale, st);
                }
                else
                {
                    float st = (t - 0.35f) / 0.65f;
                    transform.localScale = Vector3.Lerp(peakScale, Vector3.zero, st);
                }
                yield return null;
            }

            transform.localScale = baseScale;
            isCollectingAnim = false;
            gameObject.SetActive(false);
        }

        public void ReturnToPoolImmediate()
        {
            StopAllCoroutines();
            isCollected = false;
            isCollectingAnim = false;
            if (shimmerAnchor != null) shimmerAnchor.gameObject.SetActive(false);
            if (activeItemVisual != null)
            {
                activeItemVisual.SetActive(false);
                activeItemVisual = null;
            }
            transform.localScale = Vector3.one;
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            if (shimmerAnchor != null) shimmerAnchor.gameObject.SetActive(false);
        }
    }
}
