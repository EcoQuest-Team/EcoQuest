using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class MenuCritter : MonoBehaviour
{
    [Header("Spawn & Counts (3 to 5 recommended)")]
    [Range(1, 10)] public int critterCount = 4;
    public Sprite[] customCritterSprites;
    public Color[] critterColors = new Color[]
    {
        new Color(1f, 0.78f, 0.28f, 0.92f),
        new Color(0.55f, 0.88f, 1f, 0.92f),
        new Color(1f, 0.58f, 0.75f, 0.92f),
        new Color(0.75f, 0.98f, 0.52f, 0.92f)
    };
    public Vector2 sizeRange = new Vector2(24f, 32f);

    [Header("Movement & Flee Settings")]
    public float wanderSpeed = 55f;
    public float fleeSpeed = 250f;
    public float fleeRadius = 155f;
    public float steeringSmoothing = 5f;
    public float wingFlapSpeed = 10f;

    private struct CritterAgent
    {
        public RectTransform rect;
        public Vector2 pos;
        public Vector2 velocity;
        public Vector2 wanderTarget;
        public float nextTargetTime;
        public float flapPhase;
        public float baseScale;
    }

    private CritterAgent[] critters;
    private RectTransform container;
    private static Sprite placeholderButterflySprite;

    private void Start()
    {
        SpawnCritters();
    }

    public void SpawnCritters()
    {
        if (container != null)
        {
            Destroy(container.gameObject);
        }

        GameObject go = new GameObject("CrittersContainer", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        container = go.GetComponent<RectTransform>();
        container.anchorMin = new Vector2(0.5f, 0.5f);
        container.anchorMax = new Vector2(0.5f, 0.5f);
        container.sizeDelta = Vector2.zero;

        float halfW = Mathf.Max(960f, Screen.width * 0.5f) * 0.85f;
        float halfH = Mathf.Max(540f, Screen.height * 0.5f) * 0.85f;

        Sprite defaultSprite = GetOrCreateButterflySprite();
        critters = new CritterAgent[critterCount];

        for (int i = 0; i < critterCount; i++)
        {
            GameObject cGo = new GameObject($"Butterfly_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            cGo.transform.SetParent(container, false);
            Image img = cGo.GetComponent<Image>();

            bool hasCustom = customCritterSprites != null && customCritterSprites.Length > 0 && customCritterSprites[i % customCritterSprites.Length] != null;
            img.sprite = hasCustom ? customCritterSprites[i % customCritterSprites.Length] : defaultSprite;
            img.color = (critterColors != null && critterColors.Length > 0)
                ? critterColors[i % critterColors.Length]
                : new Color(1f, 0.82f, 0.35f, 0.92f);
            img.raycastTarget = false;

            RectTransform rt = cGo.GetComponent<RectTransform>();
            float size = Random.Range(sizeRange.x, sizeRange.y);
            rt.sizeDelta = new Vector2(size, size);

            Vector2 startPos = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));
            rt.anchoredPosition = startPos;

            critters[i] = new CritterAgent
            {
                rect = rt,
                pos = startPos,
                velocity = Random.insideUnitCircle.normalized * wanderSpeed,
                wanderTarget = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH)),
                nextTargetTime = Time.unscaledTime + Random.Range(2f, 5f),
                flapPhase = Random.Range(0f, Mathf.PI * 2f),
                baseScale = 1f
            };
        }
    }

    private void Update()
    {
        if (critters == null) return;

        float dt = Time.unscaledDeltaTime;
        float t = Time.unscaledTime;

        float halfW = Mathf.Max(960f, Screen.width * 0.5f) * 0.88f;
        float halfH = Mathf.Max(540f, Screen.height * 0.5f) * 0.88f;

        Vector2 mouseScreen = Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : (Vector2)Input.mousePosition;
        Vector2 mouseLocal = mouseScreen - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        float steerLerp = 1f - Mathf.Exp(-Mathf.Max(0.5f, steeringSmoothing) * dt);

        for (int i = 0; i < critters.Length; i++)
        {
            var c = critters[i];
            if (c.rect == null) continue;

            Vector2 toCritter = c.pos - mouseLocal;
            float distToMouse = toCritter.magnitude;

            Vector2 desiredVel;
            bool isFleeing = distToMouse < fleeRadius;

            if (isFleeing)
            {
                Vector2 fleeDir = distToMouse > 0.01f ? toCritter / distToMouse : Random.insideUnitCircle.normalized;
                float urgency = Mathf.Clamp01(1f - (distToMouse / Mathf.Max(1f, fleeRadius)));
                desiredVel = fleeDir * Mathf.Lerp(wanderSpeed * 1.5f, fleeSpeed, urgency);
                // Pick a new wander target ahead in the flee direction so it returns to wandering smoothly
                c.wanderTarget = new Vector2(
                    Mathf.Clamp(c.pos.x + fleeDir.x * 220f, -halfW, halfW),
                    Mathf.Clamp(c.pos.y + fleeDir.y * 220f, -halfH, halfH)
                );
                c.nextTargetTime = t + Random.Range(2f, 4f);
            }
            else
            {
                if (t >= c.nextTargetTime || Vector2.Distance(c.pos, c.wanderTarget) < 35f)
                {
                    c.wanderTarget = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));
                    c.nextTargetTime = t + Random.Range(2.5f, 5.5f);
                }

                Vector2 toTarget = (c.wanderTarget - c.pos).normalized;
                // Add gentle sinusoidal flutter to wandering direction
                Vector2 flutter = new Vector2(
                    Mathf.Sin(t * 2.1f + c.flapPhase),
                    Mathf.Cos(t * 1.7f + c.flapPhase)
                ) * 0.35f;
                desiredVel = (toTarget + flutter).normalized * wanderSpeed;
            }

            c.velocity = Vector2.Lerp(c.velocity, desiredVel, isFleeing ? steerLerp * 2.2f : steerLerp);
            c.pos += c.velocity * dt;

            // Keep inside padded screen bounds
            float boundW = halfW + 60f;
            float boundH = halfH + 60f;
            if (c.pos.x > boundW) { c.pos.x = boundW; c.velocity.x = -Mathf.Abs(c.velocity.x); }
            else if (c.pos.x < -boundW) { c.pos.x = -boundW; c.velocity.x = Mathf.Abs(c.velocity.x); }
            if (c.pos.y > boundH) { c.pos.y = boundH; c.velocity.y = -Mathf.Abs(c.velocity.y); }
            else if (c.pos.y < -boundH) { c.pos.y = -boundH; c.velocity.y = Mathf.Abs(c.velocity.y); }

            c.rect.anchoredPosition = c.pos;

            if (c.velocity.sqrMagnitude > 1f)
            {
                float angle = Mathf.Atan2(c.velocity.y, c.velocity.x) * Mathf.Rad2Deg - 90f;
                c.rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            // Wing flap animation (faster when fleeing)
            float flapFreq = isFleeing ? wingFlapSpeed * 1.8f : wingFlapSpeed;
            float wingX = Mathf.Lerp(0.35f, 1.05f, 0.5f + 0.5f * Mathf.Sin(t * flapFreq + c.flapPhase));
            c.rect.localScale = new Vector3(wingX * c.baseScale, c.baseScale, 1f);

            critters[i] = c;
        }
    }

    private static Sprite GetOrCreateButterflySprite()
    {
        if (placeholderButterflySprite != null) return placeholderButterflySprite;
        int res = 64;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        for (int y = 0; y < res; y++)
        {
            float ny = (y / (float)(res - 1)) * 2f - 1f; // -1 to 1
            for (int x = 0; x < res; x++)
            {
                float nx = (x / (float)(res - 1)) * 2f - 1f; // -1 to 1
                float ax = Mathf.Abs(nx);

                // Body in center
                bool body = ax < 0.07f && Mathf.Abs(ny) < 0.55f;

                // Upper wing lobe and lower wing lobe
                float upperWing = Vector2.Distance(new Vector2(ax, ny), new Vector2(0.42f, 0.24f));
                float lowerWing = Vector2.Distance(new Vector2(ax, ny), new Vector2(0.32f, -0.25f));
                float d = Mathf.Min(upperWing / 0.44f, lowerWing / 0.34f);

                if (body)
                {
                    tex.SetPixel(x, y, new Color(0.25f, 0.2f, 0.15f, 1f));
                }
                else if (d <= 1f && ax >= 0.04f)
                {
                    float alpha = Mathf.Clamp01((1f - d) * 6f);
                    float shade = Mathf.Lerp(1f, 0.8f, d);
                    tex.SetPixel(x, y, new Color(shade, shade, shade, alpha));
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }
        tex.Apply();
        placeholderButterflySprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
        return placeholderButterflySprite;
    }
}
