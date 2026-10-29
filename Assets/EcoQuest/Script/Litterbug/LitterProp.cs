using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(FlightRoutine(startPos, groundPos, arcHeight, Mathf.Max(0.15f, flightDuration), onLanded));
    }

    private IEnumerator FlightRoutine(Vector3 startPos, Vector3 endPos, float arcHeight, float duration, Action<LitterProp> onLanded)
    {
        Vector3 spinAxis = new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-0.6f, 0.6f), UnityEngine.Random.Range(-1f, 1f)).normalized;
        if (spinAxis.sqrMagnitude < 0.01f) spinAxis = Vector3.right;
        float spinSpeed = 380f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Vector3 pos = Vector3.Lerp(startPos, endPos, t);
            pos.y += 4f * arcHeight * t * (1f - t);
            transform.position = pos;
            transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
            yield return null;
        }

        transform.position = endPos;
        ApplyLandingState();

        if (shattersOnLanding && shatteredShardsVisual != null)
        {
            yield return StartCoroutine(ScatterShardsBrief(shatteredShardsVisual.transform));
        }

        activeRoutine = null;
        onLanded?.Invoke(this);
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
        float duration = 0.42f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curve = t < 0.2f
                ? Mathf.Lerp(1f, 1.18f, t / 0.2f)
                : Mathf.Lerp(1.18f, 0f, (t - 0.2f) / 0.8f);
            transform.localScale = startScale * curve;
            yield return null;
        }

        Destroy(gameObject);
    }

    public static Material GetSharedParticleMaterial()
    {
        if (cachedParticleMat != null) return cachedParticleMat;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh != null)
        {
            cachedParticleMat = new Material(sh);
            if (cachedParticleMat.HasProperty("_BaseColor"))
                cachedParticleMat.SetColor("_BaseColor", Color.white);
        }
        return cachedParticleMat;
    }

    private static void SpawnSparkleBurst(Vector3 worldPos)
    {
        GameObject fxGo = new GameObject("LitterCleanSparkleFX");
        fxGo.transform.position = worldPos;

        ParticleSystem ps = fxGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.7f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
        main.gravityModifier = -0.15f;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.35f, 1f, 0.55f, 1f),
            new Color(1f, 0.95f, 0.4f, 1f));

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.16f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

        var psr = fxGo.GetComponent<ParticleSystemRenderer>();
        Material mat = GetSharedParticleMaterial();
        if (mat != null) psr.sharedMaterial = mat;

        ps.Play();
        Destroy(fxGo, 1.1f);
    }
}
