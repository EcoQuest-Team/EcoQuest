using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Thrown litter prop. Parabolic arc flight without Rigidbody, no player collision during flight,
/// ground-safe landing (trigger collider, non-blocking), shatter visual for glass, and smooth shrink disposal.
/// </summary>
public class LitterProp : MonoBehaviour
{
    [SerializeField] private int litterId = 1;
    [SerializeField] private LitterCategory category;
    [SerializeField] private bool shattersOnLanding;
    [SerializeField] private GameObject intactVisual;
    [SerializeField] private GameObject shatteredShardsVisual;

    private bool isShrinking;
    private Coroutine activeRoutine;
    private static Material cachedParticleMat;
    private static readonly HashSet<LitterProp> activeWorldProps = new HashSet<LitterProp>();

    public int LitterId => litterId;
    public LitterCategory Category => category;
    public bool ShattersOnLanding => shattersOnLanding;
    public bool IsShattered => shatteredShardsVisual != null && shatteredShardsVisual.activeSelf;

    private void OnEnable()
    {
        activeWorldProps.Add(this);
    }

    private void OnDestroy()
    {
        activeWorldProps.Remove(this);
    }

    public static void ShrinkAllActiveWorldLitter()
    {
        if (activeWorldProps.Count == 0) return;
        var snapshot = new List<LitterProp>(activeWorldProps);
        for (int i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i] != null)
                snapshot[i].ShrinkAndDestroy();
        }
        activeWorldProps.Clear();
    }

    public void ConfigurePrefabVisuals(int id, LitterCategory cat, bool shatters, GameObject intact, GameObject shards)
    {
        litterId = id;
        category = cat;
        shattersOnLanding = shatters;
        intactVisual = intact;
        shatteredShardsVisual = shards;
    }

    public void Launch(Vector3 startPos, Vector3 groundPos, float arcHeight, float flightDuration, Action<LitterProp> onLanded)
    {
        transform.position = startPos;
        if (intactVisual != null) intactVisual.SetActive(true);
        if (shatteredShardsVisual != null) shatteredShardsVisual.SetActive(false);

        // Disable colliders during flight: trash can never hit, push, or damage the player
        Collider[] colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(FlightRoutine(startPos, groundPos, arcHeight, Mathf.Max(0.2f, flightDuration), onLanded));
    }

    private IEnumerator FlightRoutine(Vector3 startPos, Vector3 endPos, float arcHeight, float duration, Action<LitterProp> onLanded)
    {
        Vector3 spinAxis = new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-0.6f, 0.6f), UnityEngine.Random.Range(-1f, 1f)).normalized;
        if (spinAxis.sqrMagnitude < 0.01f) spinAxis = Vector3.right;
        float spinSpeed = 380f;

        Camera mainCam = Camera.main;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Vector3 pos = Vector3.Lerp(startPos, endPos, t);
            pos.y += 4f * arcHeight * t * (1f - t);

            // Arc stays at least 1.2m from camera
            if (mainCam != null)
            {
                Vector3 camPos = mainCam.transform.position;
                Vector3 toPos = pos - camPos;
                float dist = toPos.magnitude;
                if (dist < 1.2f && dist > 0.01f)
                {
                    pos = camPos + (toPos / dist) * 1.2f;
                }
            }

            transform.position = pos;
            transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
            yield return null;
        }

        transform.position = endPos;
        ApplyLandingState();

        // Re-enable colliders as triggers so litter does not block player movement
        Collider[] colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].isTrigger = true;
            colliders[i].enabled = true;
        }

        // Small squash & bounce on landing
        yield return StartCoroutine(LandingBounceRoutine());

        if (shattersOnLanding && shatteredShardsVisual != null)
        {
            yield return StartCoroutine(ScatterShardsBrief(shatteredShardsVisual.transform));
        }

        activeRoutine = null;
        onLanded?.Invoke(this);
    }

    private IEnumerator LandingBounceRoutine()
    {
        Vector3 baseScale = transform.localScale;
        float dur = 0.18f;
        float elapsed = 0f;

        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            // Squash Y, stretch XZ, then recover
            float squash = 1f - 0.22f * Mathf.Sin(t * Mathf.PI);
            float stretch = 1f + 0.12f * Mathf.Sin(t * Mathf.PI);
            transform.localScale = new Vector3(baseScale.x * stretch, baseScale.y * squash, baseScale.z * stretch);
            yield return null;
        }

        transform.localScale = baseScale;
    }

    public void ApplyLandingState()
    {
        transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
        if (shattersOnLanding && shatteredShardsVisual != null)
        {
            if (intactVisual != null) intactVisual.SetActive(false);
            shatteredShardsVisual.SetActive(true);
        }
    }

    private IEnumerator ScatterShardsBrief(Transform shardsRoot)
    {
        int count = shardsRoot.childCount;
        Vector3[] targets = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            targets[i] = shardsRoot.GetChild(i).localPosition;
            shardsRoot.GetChild(i).localPosition = Vector3.zero;
        }

        float dur = 0.14f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 2f);
            for (int i = 0; i < count; i++)
            {
                shardsRoot.GetChild(i).localPosition = Vector3.Lerp(Vector3.zero, targets[i], k);
            }
            yield return null;
        }
    }

    public void ShrinkAndDestroy()
    {
        if (isShrinking) return;
        isShrinking = true;
        if (activeRoutine != null) StopCoroutine(activeRoutine);

        SpawnSparkleBurst(transform.position + Vector3.up * 0.12f);

        if (gameObject.activeInHierarchy)
            StartCoroutine(ShrinkRoutine());
        else
            Destroy(gameObject);
    }

    private IEnumerator ShrinkRoutine()
    {
        Vector3 startScale = transform.localScale;
        float duration = 0.35f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float s = 1f - Mathf.SmoothStep(0f, 1f, t);
            transform.localScale = startScale * s;
            yield return null;
        }

        Destroy(gameObject);
    }

    private static void SpawnSparkleBurst(Vector3 pos)
    {
        GameObject go = new GameObject("LitterShrinkPuff");
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var pr = go.GetComponent<ParticleSystemRenderer>();

        Material mat = GetSharedParticleMaterial();
        if (mat != null) pr.material = mat;

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.35f;
        main.startLifetime = 0.32f;
        main.startSpeed = 1.4f;
        main.startSize = 0.08f;
        main.startColor = new Color(0.9f, 0.95f, 0.85f, 0.8f);
        main.stopAction = ParticleSystemStopAction.Destroy;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });

        ps.Play();
        Destroy(go, 0.6f);
    }

    public static Material GetSharedParticleMaterial()
    {
        if (cachedParticleMat != null) return cachedParticleMat;
        Shader sh = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh != null) cachedParticleMat = new Material(sh);
        return cachedParticleMat;
    }
}
