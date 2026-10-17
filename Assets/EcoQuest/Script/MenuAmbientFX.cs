using UnityEngine;
using UnityEngine.UI;

public class MenuAmbientFX : MonoBehaviour
{
    [Header("Fireflies (15 to 25 recommended)")]
    [Range(5, 40)] public int fireflyCount = 20;
    public float fireflySpeed = 22f;
    public float fireflyPulseSpeed = 1.8f;
    public Vector2 fireflySizeRange = new Vector2(12f, 24f);
    public Color fireflyColor = new Color(0.95f, 1f, 0.45f, 0.85f);
    public Sprite fireflySprite;

    [Header("Falling Leaves (8 to 12 recommended)")]
    [Range(2, 25)] public int leafCount = 10;
    public float leafFallSpeed = 38f;
    public float leafWindSwayStrength = 48f;
    public float leafWindSwaySpeed = 1.1f;
    public float leafRotationSpeed = 32f;
    public Vector2 leafSizeRange = new Vector2(20f, 34f);
    public Color leafColor = new Color(0.45f, 0.75f, 0.30f, 0.88f);
    public Sprite leafSprite;

    private struct FireflyData
    {
        public RectTransform rect;
        public Image image;
        public Vector2 pos;
        public float seedX;
        public float seedY;
        public float pulsePhase;
        public float baseSize;
    }

    private struct LeafData
    {
        public RectTransform rect;
        public Vector2 pos;
        public float speedMult;
        public float swayPhase;
        public float rotAngle;
        public float rotSpeed;
    }

    private FireflyData[] fireflies;
    private LeafData[] leaves;
    private RectTransform container;
    private static Sprite fallbackGlowSprite;
    private static Sprite fallbackLeafSprite;

    private void Start()
    {
        RebuildParticles();
    }

    public void RebuildParticles()
    {
        if (container != null)
        {
            Destroy(container.gameObject);
        }

        GameObject go = new GameObject("AmbientParticlesContainer", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        container = go.GetComponent<RectTransform>();
        container.anchorMin = new Vector2(0.5f, 0.5f);
        container.anchorMax = new Vector2(0.5f, 0.5f);
        container.sizeDelta = Vector2.zero;

        float halfW = Mathf.Max(960f, Screen.width * 0.5f) + 30f;
        float halfH = Mathf.Max(540f, Screen.height * 0.5f) + 30f;

        Sprite glow = fireflySprite != null ? fireflySprite : GetOrCreateGlowSprite();
        Sprite leaf = leafSprite != null ? leafSprite : GetOrCreateLeafSprite();

        fireflies = new FireflyData[fireflyCount];
        for (int i = 0; i < fireflyCount; i++)
        {
            GameObject fGo = new GameObject($"Firefly_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fGo.transform.SetParent(container, false);
            Image img = fGo.GetComponent<Image>();
            img.sprite = glow;
            img.color = fireflyColor;
            img.raycastTarget = false;

            RectTransform rt = fGo.GetComponent<RectTransform>();
            float size = Random.Range(fireflySizeRange.x, fireflySizeRange.y);
            rt.sizeDelta = new Vector2(size, size);

            Vector2 startPos = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));
            rt.anchoredPosition = startPos;

            fireflies[i] = new FireflyData
            {
                rect = rt,
                image = img,
                pos = startPos,
                seedX = Random.Range(0f, 100f),
                seedY = Random.Range(100f, 200f),
                pulsePhase = Random.Range(0f, Mathf.PI * 2f),
                baseSize = size
            };
        }

        leaves = new LeafData[leafCount];
        for (int i = 0; i < leafCount; i++)
        {
            GameObject lGo = new GameObject($"FallingLeaf_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lGo.transform.SetParent(container, false);
            Image img = lGo.GetComponent<Image>();
            img.sprite = leaf;
            // Slight natural variation in leaf tint
            float tintVar = Random.Range(-0.08f, 0.08f);
            img.color = new Color(
                Mathf.Clamp01(leafColor.r + tintVar),
                Mathf.Clamp01(leafColor.g + tintVar),
                Mathf.Clamp01(leafColor.b),
                leafColor.a
            );
            img.raycastTarget = false;

            RectTransform rt = lGo.GetComponent<RectTransform>();
            float size = Random.Range(leafSizeRange.x, leafSizeRange.y);
            rt.sizeDelta = new Vector2(size, size);

            Vector2 startPos = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));
            rt.anchoredPosition = startPos;

            leaves[i] = new LeafData
            {
                rect = rt,
                pos = startPos,
                speedMult = Random.Range(0.8f, 1.25f),
                swayPhase = Random.Range(0f, Mathf.PI * 2f),
                rotAngle = Random.Range(0f, 360f),
                rotSpeed = Random.Range(-1f, 1f) * leafRotationSpeed
            };
        }
    }

    private void Update()
    {
        if (fireflies == null || leaves == null) return;

        float dt = Time.unscaledDeltaTime;
        float t = Time.unscaledTime;
        float halfW = Mathf.Max(960f, Screen.width * 0.5f) + 40f;
        float halfH = Mathf.Max(540f, Screen.height * 0.5f) + 40f;

        for (int i = 0; i < fireflies.Length; i++)
        {
            var f = fireflies[i];
            if (f.rect == null) continue;

            float vx = (Mathf.PerlinNoise(f.seedX, t * 0.35f) - 0.5f) * 2f * fireflySpeed;
            float vy = (Mathf.PerlinNoise(f.seedY, t * 0.35f) - 0.5f) * 2f * fireflySpeed;
            f.pos += new Vector2(vx, vy) * dt;

            if (f.pos.x > halfW) f.pos.x = -halfW;
            else if (f.pos.x < -halfW) f.pos.x = halfW;
            if (f.pos.y > halfH) f.pos.y = -halfH;
            else if (f.pos.y < -halfH) f.pos.y = halfH;

            f.rect.anchoredPosition = f.pos;

            float pulse = 0.5f + 0.5f * Mathf.Sin(t * fireflyPulseSpeed + f.pulsePhase);
            Color c = fireflyColor;
            c.a = Mathf.Lerp(0.15f, fireflyColor.a, pulse);
            f.image.color = c;

            float s = Mathf.Lerp(0.75f, 1.2f, pulse);
            f.rect.localScale = new Vector3(s, s, 1f);
            fireflies[i] = f;
        }

        for (int i = 0; i < leaves.Length; i++)
        {
            var l = leaves[i];
            if (l.rect == null) continue;

            float swayVx = Mathf.Sin(t * leafWindSwaySpeed + l.swayPhase) * leafWindSwayStrength;
            l.pos.x += swayVx * dt;
            l.pos.y -= leafFallSpeed * l.speedMult * dt;

            if (l.pos.y < -halfH)
            {
                l.pos.y = halfH;
                l.pos.x = Random.Range(-halfW, halfW);
            }
            if (l.pos.x > halfW) l.pos.x = -halfW;
            else if (l.pos.x < -halfW) l.pos.x = halfW;

            l.rotAngle += l.rotSpeed * dt;
            float tilt = Mathf.Sin(t * leafWindSwaySpeed + l.swayPhase) * 22f;
            l.rect.anchoredPosition = l.pos;
            l.rect.localRotation = Quaternion.Euler(0f, 0f, l.rotAngle + tilt);
            leaves[i] = l;
        }
    }

    public static Sprite GetOrCreateGlowSprite()
    {
        if (fallbackGlowSprite != null) return fallbackGlowSprite;
        int res = 64;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(res * 0.5f, res * 0.5f);
        float maxR = res * 0.5f;
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center) / maxR;
                float a = Mathf.Clamp01(1f - d);
                a = a * a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        fallbackGlowSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
        return fallbackGlowSprite;
    }

    public static Sprite GetOrCreateLeafSprite()
    {
        if (fallbackLeafSprite != null) return fallbackLeafSprite;
        int res = 48;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        for (int y = 0; y < res; y++)
        {
            float ny = (y / (float)(res - 1)) * 2f - 1f;
            for (int x = 0; x < res; x++)
            {
                float nx = (x / (float)(res - 1)) * 2f - 1f;
                // Pointed oval leaf shape
                float halfWidth = Mathf.Max(0f, (1f - ny * ny) * 0.55f);
                float dist = Mathf.Abs(nx);
                if (dist <= halfWidth)
                {
                    float edge = Mathf.Clamp01((halfWidth - dist) * 8f);
                    float vein = Mathf.Abs(nx) < 0.04f ? 0.85f : 1f;
                    tex.SetPixel(x, y, new Color(vein, 1f, vein, edge));
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }
        tex.Apply();
        fallbackLeafSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
        return fallbackLeafSprite;
    }
}
