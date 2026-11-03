using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Static helper class providing a clean, simple loading API matching project specifications.
/// Usage: LoadingScreen.Load("Forest Scene"); or LoadingScreen.Load(1);
/// </summary>
public static class LoadingScreen
{
    public static void Load(string scenePathOrName, Sprite backgroundOverride = null)
    {
        LoadingScreenController.Load(scenePathOrName, backgroundOverride);
    }

    public static void Load(int buildIndex, Sprite backgroundOverride = null)
    {
        LoadingScreenController.Load(buildIndex, backgroundOverride);
    }

    public static bool IsLoading => LoadingScreenController.IsLoading;
}

/// <summary>
/// Unified, high-fidelity loading screen controller for EcoQuest.
/// Matches reference forest artwork with dynamic real UI:
/// - Chunky golden-yellow "LOADING..." title with animated dots and bounce
/// - 9-sliced wooden progress bar with smooth green fill and animated leaf handle
/// - Dynamic "Pro Tips" line with non-repeating shuffle bag and PlayerPrefs memory
/// - Unscaled-time background camera drift and pooled atmospheric UI particle effects
/// - Safe integration with UIOrchestrator, TravelGate, and Main Menu
/// </summary>
[DisallowMultipleComponent]
public class LoadingScreenController : MonoBehaviour
{
    private static LoadingScreenController _instance;
    public static LoadingScreenController Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<LoadingScreenController>();
                if (_instance == null)
                {
                    // Check Resources or AssetDatabase
                    GameObject prefab = Resources.Load<GameObject>("LoadingScreen");
#if UNITY_EDITOR
                    if (prefab == null)
                    {
                        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/UI/LoadingScreen/LoadingScreen.prefab");
                    }
#endif
                    if (prefab != null)
                    {
                        GameObject go = Instantiate(prefab);
                        _instance = go.GetComponent<LoadingScreenController>();
                    }
                    else
                    {
                        // Dynamic runtime fallback builder
                        GameObject runtimeRoot = new GameObject("[LoadingScreenController]");
                        _instance = runtimeRoot.AddComponent<LoadingScreenController>();
                        _instance.BuildRuntimeUI();
                    }
                }
            }
            return _instance;
        }
        private set => _instance = value;
    }

    private static bool _isLoading = false;
    public static bool IsLoading => _isLoading;

    [Header("Timing (Unscaled Time)")]
    [Tooltip("Minimum duration to display loading screen so it never flashes by on fast hardware.")]
    [SerializeField] private float minDisplayTime = 3.5f;
    [Tooltip("Duration for loading screen to fade in.")]
    [SerializeField] private float fadeInDuration = 0.5f;
    [Tooltip("Duration for loading screen to fade out into the new scene.")]
    [SerializeField] private float fadeOutDuration = 0.65f;
    [Tooltip("Time between tip changes in seconds.")]
    [SerializeField] private float tipInterval = 4.5f;
    [Tooltip("Pause duration after reaching 100% before activating the scene (if key press not required).")]
    [SerializeField] private float autoContinueDelay = 0.4f;

    [Header("Toggles")]
    [Tooltip("Display numeric percentage text alongside the bar (default false for clean art look).")]
    [SerializeField] private bool showPercentage = false;
    [Tooltip("Require user to press any key at 100% before proceeding.")]
    [SerializeField] private bool requireKeyPress = false;
    [Tooltip("Enable pooled atmospheric UI particles (fireflies, leaves, fog, mushroom glow).")]
    [SerializeField] private bool enableParticles = true;
    [Tooltip("Enable slow ping-pong cinematic zoom/pan drift on background.")]
    [SerializeField] private bool enableCameraDrift = true;

    [Header("Particle Counts (Cap <= 60 elements)")]
    [Range(0, 24)] [SerializeField] private int fireflyCount = 16;
    [Range(0, 16)] [SerializeField] private int leafCount = 10;
    [Range(0, 6)]  [SerializeField] private int fogWispCount = 4;
    [Range(0, 6)]  [SerializeField] private int mushroomTwinkleCount = 3;
    [Range(0, 4)]  [SerializeField] private int lightRayCount = 2;

    [Header("Art & Sprites")]
    [SerializeField] private Sprite forestBackgroundSprite;
    [SerializeField] private Sprite cityBackgroundSprite;
    [SerializeField] private Sprite barFrameSprite;
    [SerializeField] private Sprite barFillSprite;
    [SerializeField] private Sprite leafHandleSprite;
    [SerializeField] private Sprite bottomGradientSprite;
    [SerializeField] private Sprite fireflySprite;
    [SerializeField] private Sprite leafParticleSprite;
    [SerializeField] private Sprite fogWispSprite;
    [SerializeField] private Sprite mushroomGlowSprite;
    [SerializeField] private Sprite lightRaySprite;

    [Header("Fonts")]
    [SerializeField] private TMP_FontAsset titleFont;
    [SerializeField] private TMP_FontAsset tipFont;

    [Header("Tips Database")]
    [SerializeField] private LoadingTips tipsDatabase;

    [Header("Audio Hooks (Null-Safe)")]
    [SerializeField] private AudioSource ambientAudioSource;
    [SerializeField] private AudioClip ambientLoopClip;
    [SerializeField] private AudioClip readyChimeClip;
    [SerializeField] private AudioClip tipChangeClip;

    [Header("UI Object References")]
    [SerializeField] private Canvas rootCanvas;
    [SerializeField] private CanvasGroup rootCanvasGroup;
    [SerializeField] private CanvasScaler canvasScaler;
    [SerializeField] private RectTransform bgContainer;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image bottomGradientImage;
    [SerializeField] private Image exitFlashImage;
    [SerializeField] private RectTransform bottomUIPanel;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private RectTransform barContainer;
    [SerializeField] private Image barFrameImage;
    [SerializeField] private RectTransform fillTrackRect;
    [SerializeField] private Image fillBarImage;
    [SerializeField] private RectTransform leafHandleRect;
    [SerializeField] private Image leafHandleImage;
    [SerializeField] private TMP_Text percentText;
    [SerializeField] private TMP_Text tipText;
    [SerializeField] private CanvasGroup tipCanvasGroup;
    [SerializeField] private RectTransform particleContainer;

    // Runtime state
    private float currentDisplayProgress = 0f;
    private float progressSmoothVelocity = 0f;
    private bool isReady = false;
    private bool isSceneActivationAllowed = false;
    private Vector2 baseTitlePos;
    private Vector2 baseTipPos;
    private Coroutine activeLoadRoutine;
    private Coroutine activeTipRoutine;

    // Pooled particle structs
    private class FireflyParticle
    {
        public RectTransform rect;
        public Image img;
        public Vector2 velocity;
        public float baseAlpha;
        public float pulseSpeed;
        public float pulseOffset;
    }
    private class LeafParticle
    {
        public RectTransform rect;
        public Image img;
        public float fallSpeed;
        public float swaySpeed;
        public float swayAmp;
        public float rotSpeed;
        public float startX;
    }
    private class FogParticle
    {
        public RectTransform rect;
        public Image img;
        public float driftSpeed;
        public float width;
    }
    private class GlowParticle
    {
        public RectTransform rect;
        public Image img;
        public float minAlpha;
        public float maxAlpha;
        public float speed;
        public float offset;
    }

    private List<FireflyParticle> fireflies = new List<FireflyParticle>();
    private List<LeafParticle> leaves = new List<LeafParticle>();
    private List<FogParticle> fogWisps = new List<FogParticle>();
    private List<GlowParticle> mushroomGlows = new List<GlowParticle>();
    private List<GlowParticle> lightRays = new List<GlowParticle>();

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureReferences();
        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.alpha = 0f;
            rootCanvasGroup.blocksRaycasts = false;
            rootCanvasGroup.interactable = false;
        }
        if (rootCanvas != null)
        {
            rootCanvas.enabled = false;
        }

        if (titleText != null) baseTitlePos = titleText.rectTransform.anchoredPosition;
        if (tipText != null) baseTipPos = tipText.rectTransform.anchoredPosition;

        InitParticlePool();
    }

    private void Update()
    {
        if (!_isLoading && (rootCanvasGroup == null || rootCanvasGroup.alpha <= 0.001f))
            return;

        float dt = Time.unscaledDeltaTime;
        float time = Time.unscaledTime;

        // 1. Camera drift on background
        if (enableCameraDrift && bgContainer != null)
        {
            float zoom = 1.018f + Mathf.Sin(time * 0.35f) * 0.016f; // ~1.002 to 1.034
            float panX = Mathf.Sin(time * 0.28f) * 14f;
            float panY = Mathf.Cos(time * 0.32f) * 8f;
            bgContainer.localScale = new Vector3(zoom, zoom, 1f);
            bgContainer.anchoredPosition = new Vector2(panX, panY);
        }

        // 2. Title animation (dots and float)
        if (titleText != null)
        {
            if (!isReady)
            {
                int dots = ((int)(time * 3.2f) % 3) + 1;
                titleText.text = "LOADING" + new string('.', dots);
            }
            float bounceY = Mathf.Sin(time * 3.5f) * 3f;
            titleText.rectTransform.anchoredPosition = baseTitlePos + new Vector2(0f, bounceY);
        }

        // 3. Leaf handle animation
        if (leafHandleRect != null && fillTrackRect != null)
        {
            float trackW = fillTrackRect.rect.width;
            float targetX = (trackW * currentDisplayProgress) - (trackW * 0.5f);
            leafHandleRect.anchoredPosition = new Vector2(targetX, Mathf.Sin(time * 5f) * 2.5f);

            float sway = Mathf.Sin(time * 4f) * 7f;
            float speedAngle = Mathf.Clamp(progressSmoothVelocity * 45f, 0f, 25f);
            leafHandleRect.localRotation = Quaternion.Euler(0f, 0f, sway - speedAngle);
        }

        // 4. Update particles (unscaled time, zero allocations)
        if (enableParticles)
        {
            UpdateParticles(dt, time);
        }
    }

    private void UpdateParticles(float dt, float time)
    {
        float canvasW = 1920f;
        float canvasH = 1080f;

        // Fireflies
        for (int i = 0; i < fireflies.Count; i++)
        {
            var f = fireflies[i];
            if (f.rect == null) continue;
            Vector2 pos = f.rect.anchoredPosition + f.velocity * dt;
            if (pos.x < -40f) pos.x = canvasW + 40f;
            else if (pos.x > canvasW + 40f) pos.x = -40f;
            if (pos.y < -40f) pos.y = canvasH + 40f;
            else if (pos.y > canvasH + 40f) pos.y = -40f;
            f.rect.anchoredPosition = pos;

            float pulse = Mathf.Sin(time * f.pulseSpeed + f.pulseOffset) * 0.5f + 0.5f;
            float a = Mathf.Lerp(f.baseAlpha * 0.35f, f.baseAlpha, pulse);
            Color c = f.img.color;
            c.a = a;
            f.img.color = c;
        }

        // Leaves
        for (int i = 0; i < leaves.Count; i++)
        {
            var l = leaves[i];
            if (l.rect == null) continue;
            Vector2 pos = l.rect.anchoredPosition;
            pos.y -= l.fallSpeed * dt;
            pos.x = l.startX + Mathf.Sin(time * l.swaySpeed) * l.swayAmp;
            if (pos.y < -50f)
            {
                pos.y = canvasH + 50f;
                l.startX = UnityEngine.Random.Range(50f, canvasW - 50f);
                pos.x = l.startX;
            }
            l.rect.anchoredPosition = pos;
            l.rect.Rotate(0f, 0f, l.rotSpeed * dt);
        }

        // Fog
        for (int i = 0; i < fogWisps.Count; i++)
        {
            var fog = fogWisps[i];
            if (fog.rect == null) continue;
            Vector2 pos = fog.rect.anchoredPosition;
            pos.x += fog.driftSpeed * dt;
            if (pos.x > canvasW + fog.width * 0.5f)
            {
                pos.x = -fog.width * 0.5f;
            }
            fog.rect.anchoredPosition = pos;
        }

        // Mushroom glows
        for (int i = 0; i < mushroomGlows.Count; i++)
        {
            var m = mushroomGlows[i];
            if (m.img == null) continue;
            float tNorm = Mathf.Sin(time * m.speed + m.offset) * 0.5f + 0.5f;
            Color c = m.img.color;
            c.a = Mathf.Lerp(m.minAlpha, m.maxAlpha, tNorm);
            m.img.color = c;
        }

        // Light rays
        for (int i = 0; i < lightRays.Count; i++)
        {
            var lr = lightRays[i];
            if (lr.img == null) continue;
            float tNorm = Mathf.Sin(time * lr.speed + lr.offset) * 0.5f + 0.5f;
            Color c = lr.img.color;
            c.a = Mathf.Lerp(lr.minAlpha, lr.maxAlpha, tNorm);
            lr.img.color = c;
        }
    }

    // -------------------------------------------------------------------------
    // Public Static Entry Points
    // -------------------------------------------------------------------------

    public static void Load(string scenePathOrName, Sprite backgroundOverride = null)
    {
        if (_isLoading)
        {
            Debug.LogWarning($"[LoadingScreen] Scene load already in progress. Request to load '{scenePathOrName}' ignored.");
            return;
        }

        Instance.StartLoadRoutine(scenePathOrName, -1, backgroundOverride);
    }

    public static void Load(int buildIndex, Sprite backgroundOverride = null)
    {
        if (_isLoading)
        {
            Debug.LogWarning($"[LoadingScreen] Scene load already in progress. Request to load index '{buildIndex}' ignored.");
            return;
        }

        Instance.StartLoadRoutine(string.Empty, buildIndex, backgroundOverride);
    }

    private void StartLoadRoutine(string sceneName, int buildIndex, Sprite bgOverride)
    {
        if (activeLoadRoutine != null)
        {
            StopCoroutine(activeLoadRoutine);
        }
        activeLoadRoutine = StartCoroutine(LoadSceneSequence(sceneName, buildIndex, bgOverride));
    }

    private IEnumerator LoadSceneSequence(string sceneName, int buildIndex, Sprite bgOverride)
    {
        _isLoading = true;
        isReady = false;
        isSceneActivationAllowed = false;
        currentDisplayProgress = 0f;
        progressSmoothVelocity = 0f;

        EnsureReferences();

        // 1. Resolve Background Artwork
        Sprite targetArt = bgOverride;
        if (targetArt == null)
        {
            bool isCity = (!string.IsNullOrEmpty(sceneName) && sceneName.IndexOf("City", StringComparison.OrdinalIgnoreCase) >= 0);
            targetArt = (isCity && cityBackgroundSprite != null) ? cityBackgroundSprite : forestBackgroundSprite;
        }
        if (backgroundImage != null && targetArt != null)
        {
            backgroundImage.sprite = targetArt;
        }

        // 2. Initialize UI State & Tips
        if (rootCanvas != null) rootCanvas.enabled = true;
        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.alpha = 0f;
            rootCanvasGroup.blocksRaycasts = true;
            rootCanvasGroup.interactable = true;
        }
        if (exitFlashImage != null) exitFlashImage.color = new Color(1f, 1f, 1f, 0f);

        UpdateProgressUI(0f);
        if (titleText != null) titleText.text = "LOADING...";

        // Start cycling tips
        LoadingTips.SceneFilter sceneFilter = LoadingTips.SceneFilter.Any;
        if (!string.IsNullOrEmpty(sceneName))
        {
            if (sceneName.IndexOf("Forest", StringComparison.OrdinalIgnoreCase) >= 0) sceneFilter = LoadingTips.SceneFilter.Forest;
            else if (sceneName.IndexOf("City", StringComparison.OrdinalIgnoreCase) >= 0) sceneFilter = LoadingTips.SceneFilter.City;
        }
        if (activeTipRoutine != null) StopCoroutine(activeTipRoutine);
        activeTipRoutine = StartCoroutine(TipCycleRoutine(sceneFilter));

        // Start ambient audio hook if available
        if (ambientAudioSource != null && ambientLoopClip != null)
        {
            ambientAudioSource.clip = ambientLoopClip;
            ambientAudioSource.loop = true;
            ambientAudioSource.volume = 0.6f;
            ambientAudioSource.Play();
        }

        // 3. Fade In Loading Screen
        float fadeTimer = 0f;
        while (fadeTimer < fadeInDuration)
        {
            fadeTimer += Time.unscaledDeltaTime;
            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = Mathf.Clamp01(fadeTimer / fadeInDuration);
            }
            yield return null;
        }
        if (rootCanvasGroup != null) rootCanvasGroup.alpha = 1f;

        // 4. Start Real Async Scene Loading
        AsyncOperation asyncOp = null;
        try
        {
            if (buildIndex >= 0)
            {
                asyncOp = SceneManager.LoadSceneAsync(buildIndex);
            }
            else
            {
                asyncOp = SceneManager.LoadSceneAsync(sceneName);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LoadingScreen] Error initiating async load for '{sceneName}' (index {buildIndex}): {ex.Message}");
        }

        if (asyncOp == null)
        {
            Debug.LogError($"[LoadingScreen] Failed to load scene '{sceneName}'! Target not found in build settings. Returning to Main Menu in 2.5s...");
            if (tipText != null) tipText.text = "Scene could not be loaded. Returning to Main Menu...";
            yield return new WaitForSecondsRealtime(2.5f);
            _isLoading = false;
            SceneManager.LoadScene("Main Menu");
            yield break;
        }

        asyncOp.allowSceneActivation = false;

        // 5. Progress Tracking with SmoothDamp and Minimum Display Time
        float loadElapsed = 0f;
        float safeMinTime = Mathf.Max(0.5f, minDisplayTime);

        while (currentDisplayProgress < 0.999f)
        {
            loadElapsed += Time.unscaledDeltaTime;

            // asyncOp.progress stops at 0.9 until allowSceneActivation is set to true
            float realNormalized = Mathf.Clamp01(asyncOp.progress / 0.9f);
            float timeNormalized = Mathf.Clamp01(loadElapsed / safeMinTime);
            float targetProgress = (realNormalized >= 0.99f && timeNormalized >= 0.99f) ? 1.0f : Mathf.Min(realNormalized, timeNormalized);

            // Smooth eased progress: never jumps, never goes backwards
            float newProg = Mathf.SmoothDamp(currentDisplayProgress, targetProgress, ref progressSmoothVelocity, 0.22f, Mathf.Infinity, Time.unscaledDeltaTime);
            if (newProg > currentDisplayProgress)
            {
                currentDisplayProgress = newProg;
            }

            UpdateProgressUI(currentDisplayProgress);
            yield return null;
        }

        currentDisplayProgress = 1f;
        UpdateProgressUI(1f);

        // 6. 100% Celebration (Sparkle, Leaf Pop, "READY!" Title)
        isReady = true;
        if (titleText != null)
        {
            titleText.text = "READY!";
            StartCoroutine(PunchTransform(titleText.rectTransform, 1.25f, 0.25f));
        }

        if (leafHandleRect != null)
        {
            StartCoroutine(PunchTransform(leafHandleRect, 1.4f, 0.3f));
        }

        if (readyChimeClip != null)
        {
            AudioSource.PlayClipAtPoint(readyChimeClip, Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.8f);
        }

        // Wait for key press or auto-continue beat
        if (requireKeyPress)
        {
            if (tipText != null) tipText.text = "Press any key to continue...";
            while (!Input.anyKeyDown)
            {
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSecondsRealtime(autoContinueDelay);
        }

        // 7. Activate New Scene
        asyncOp.allowSceneActivation = true;
        while (!asyncOp.isDone)
        {
            yield return null;
        }

        // CRITICAL FIRST-FRAME GUARANTEE:
        // Wait 2 frames for the new scene's managers (Awake & Start) to execute,
        // ensuring UIOrchestrator and HudVisibilityService apply the correct HUD profile
        // so the user NEVER experiences an unstyled or flickering HUD flash!
        yield return null;
        yield return null;

        if (activeTipRoutine != null)
        {
            StopCoroutine(activeTipRoutine);
            activeTipRoutine = null;
        }

        // 8. Fade Out Loading Screen into Scene with Exit Bloom Flash
        fadeTimer = 0f;
        while (fadeTimer < fadeOutDuration)
        {
            fadeTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeTimer / fadeOutDuration);
            float ease = Mathf.SmoothStep(0f, 1f, t);

            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = 1f - ease;
            }

            // Quick soft light bloom flash peaking around 30% of transition
            if (exitFlashImage != null)
            {
                float flash = Mathf.Sin(t * Mathf.PI) * 0.35f;
                exitFlashImage.color = new Color(1f, 1f, 1f, flash);
            }

            if (ambientAudioSource != null && ambientAudioSource.isPlaying)
            {
                ambientAudioSource.volume = Mathf.Lerp(0.6f, 0f, t);
            }

            yield return null;
        }

        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.alpha = 0f;
            rootCanvasGroup.blocksRaycasts = false;
            rootCanvasGroup.interactable = false;
        }
        if (rootCanvas != null) rootCanvas.enabled = false;
        if (exitFlashImage != null) exitFlashImage.color = new Color(1f, 1f, 1f, 0f);
        if (ambientAudioSource != null) ambientAudioSource.Stop();

        _isLoading = false;
        activeLoadRoutine = null;
    }

    private IEnumerator TipCycleRoutine(LoadingTips.SceneFilter sceneFilter)
    {
        // Set first tip immediately
        DisplayNextTip(sceneFilter, immediate: true);

        while (true)
        {
            yield return new WaitForSecondsRealtime(tipInterval);
            yield return AnimateTipTransition(sceneFilter);
        }
    }

    private void DisplayNextTip(LoadingTips.SceneFilter sceneFilter, bool immediate)
    {
        string tip = (tipsDatabase != null) 
            ? tipsDatabase.GetNextTip(sceneFilter) 
            : "Always put correct trash in the recycle bins to restore the forest and earn gold!";

        if (tipText != null)
        {
            tipText.text = "Pro Tips: " + tip;
        }
        if (tipCanvasGroup != null && immediate)
        {
            tipCanvasGroup.alpha = 1f;
        }
    }

    private IEnumerator AnimateTipTransition(LoadingTips.SceneFilter sceneFilter)
    {
        if (tipCanvasGroup == null || tipText == null) yield break;

        // Slide up slightly while fading out
        float dur = 0.22f;
        float elapsed = 0f;
        Vector2 startPos = baseTipPos;
        Vector2 endPos = baseTipPos + new Vector2(0f, 12f);

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / dur;
            tipCanvasGroup.alpha = 1f - t;
            tipText.rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            yield return null;
        }

        // Change text
        DisplayNextTip(sceneFilter, immediate: false);

        if (tipChangeClip != null)
        {
            AudioSource.PlayClipAtPoint(tipChangeClip, Vector3.zero, 0.4f);
        }

        // Slide up from below while fading in
        elapsed = 0f;
        Vector2 belowPos = baseTipPos - new Vector2(0f, 12f);
        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / dur;
            tipCanvasGroup.alpha = t;
            tipText.rectTransform.anchoredPosition = Vector2.Lerp(belowPos, baseTipPos, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        tipCanvasGroup.alpha = 1f;
        tipText.rectTransform.anchoredPosition = baseTipPos;
    }

    private void UpdateProgressUI(float progress)
    {
        float clamped = Mathf.Clamp01(progress);

        if (fillBarImage != null)
        {
            fillBarImage.fillAmount = clamped;
        }

        if (percentText != null)
        {
            percentText.gameObject.SetActive(showPercentage);
            if (showPercentage)
            {
                percentText.text = $"{Mathf.RoundToInt(clamped * 100f)}%";
            }
        }
    }

    private IEnumerator PunchTransform(RectTransform target, float maxScale, float duration)
    {
        if (target == null) yield break;
        float elapsed = 0f;
        Vector3 baseScale = Vector3.one;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            float s = 1f + (maxScale - 1f) * Mathf.Sin(t * Mathf.PI);
            target.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        target.localScale = baseScale;
    }

    // -------------------------------------------------------------------------
    // Particle Pool Initialization
    // -------------------------------------------------------------------------

    private void InitParticlePool()
    {
        if (particleContainer == null) return;

        // Clear existing children
        for (int i = particleContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(particleContainer.GetChild(i).gameObject);
        }

        fireflies.Clear();
        leaves.Clear();
        fogWisps.Clear();
        mushroomGlows.Clear();
        lightRays.Clear();

        if (!enableParticles) return;

        // 1. Fireflies (16)
        if (fireflySprite != null)
        {
            for (int i = 0; i < fireflyCount; i++)
            {
                GameObject go = new GameObject($"Firefly_{i}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(particleContainer, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(UnityEngine.Random.Range(20f, 36f), UnityEngine.Random.Range(20f, 36f));
                rt.anchoredPosition = new Vector2(UnityEngine.Random.Range(50f, 1870f), UnityEngine.Random.Range(100f, 980f));

                Image img = go.GetComponent<Image>();
                img.sprite = fireflySprite;
                img.raycastTarget = false;

                fireflies.Add(new FireflyParticle()
                {
                    rect = rt,
                    img = img,
                    velocity = new Vector2(UnityEngine.Random.Range(-25f, 25f), UnityEngine.Random.Range(-15f, 25f)),
                    baseAlpha = UnityEngine.Random.Range(0.65f, 1.0f),
                    pulseSpeed = UnityEngine.Random.Range(2f, 5f),
                    pulseOffset = UnityEngine.Random.Range(0f, Mathf.PI * 2f)
                });
            }
        }

        // 2. Falling Leaves (10)
        if (leafParticleSprite != null)
        {
            for (int i = 0; i < leafCount; i++)
            {
                GameObject go = new GameObject($"Leaf_{i}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(particleContainer, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                float sz = UnityEngine.Random.Range(24f, 40f);
                rt.sizeDelta = new Vector2(sz, sz);
                float startX = UnityEngine.Random.Range(50f, 1870f);
                rt.anchoredPosition = new Vector2(startX, UnityEngine.Random.Range(100f, 1100f));

                Image img = go.GetComponent<Image>();
                img.sprite = leafParticleSprite;
                img.color = new Color(0.9f, 0.95f, 0.85f, UnityEngine.Random.Range(0.6f, 0.9f));
                img.raycastTarget = false;

                leaves.Add(new LeafParticle()
                {
                    rect = rt,
                    img = img,
                    fallSpeed = UnityEngine.Random.Range(45f, 75f),
                    swaySpeed = UnityEngine.Random.Range(1.8f, 3.2f),
                    swayAmp = UnityEngine.Random.Range(20f, 45f),
                    rotSpeed = UnityEngine.Random.Range(-40f, 40f),
                    startX = startX
                });
            }
        }

        // 3. Fog Wisps (4)
        if (fogWispSprite != null)
        {
            for (int i = 0; i < fogWispCount; i++)
            {
                GameObject go = new GameObject($"Fog_{i}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(particleContainer, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                float w = UnityEngine.Random.Range(650f, 950f);
                float h = UnityEngine.Random.Range(130f, 210f);
                rt.sizeDelta = new Vector2(w, h);
                rt.anchoredPosition = new Vector2(i * 500f, UnityEngine.Random.Range(40f, 180f));

                Image img = go.GetComponent<Image>();
                img.sprite = fogWispSprite;
                img.color = new Color(0.85f, 0.95f, 0.9f, UnityEngine.Random.Range(0.25f, 0.45f));
                img.raycastTarget = false;

                fogWisps.Add(new FogParticle()
                {
                    rect = rt,
                    img = img,
                    driftSpeed = UnityEngine.Random.Range(12f, 25f),
                    width = w
                });
            }
        }

        // 4. Mushroom Twinkles (3)
        if (mushroomGlowSprite != null)
        {
            Vector2[] mushPos = new Vector2[] {
                new Vector2(670f, 140f),
                new Vector2(1250f, 155f),
                new Vector2(1440f, 135f)
            };
            for (int i = 0; i < Mathf.Min(mushroomTwinkleCount, mushPos.Length); i++)
            {
                GameObject go = new GameObject($"MushroomGlow_{i}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(particleContainer, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(70f, 70f);
                rt.anchoredPosition = mushPos[i];

                Image img = go.GetComponent<Image>();
                img.sprite = mushroomGlowSprite;
                img.raycastTarget = false;

                mushroomGlows.Add(new GlowParticle()
                {
                    rect = rt,
                    img = img,
                    minAlpha = 0.25f,
                    maxAlpha = 0.90f,
                    speed = UnityEngine.Random.Range(2.5f, 4.5f),
                    offset = i * 1.5f
                });
            }
        }

        // 5. Light Rays (2)
        if (lightRaySprite != null)
        {
            Vector2[] rayPos = new Vector2[] {
                new Vector2(620f, 750f),
                new Vector2(1380f, 800f)
            };
            float[] rayAngles = new float[] { -15f, 20f };
            for (int i = 0; i < Mathf.Min(lightRayCount, rayPos.Length); i++)
            {
                GameObject go = new GameObject($"LightRay_{i}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(particleContainer, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(180f, 650f);
                rt.anchoredPosition = rayPos[i];
                rt.localRotation = Quaternion.Euler(0f, 0f, rayAngles[i]);

                Image img = go.GetComponent<Image>();
                img.sprite = lightRaySprite;
                img.raycastTarget = false;

                lightRays.Add(new GlowParticle()
                {
                    rect = rt,
                    img = img,
                    minAlpha = 0.10f,
                    maxAlpha = 0.35f,
                    speed = UnityEngine.Random.Range(1.2f, 2.0f),
                    offset = i * 2.0f
                });
            }
        }
    }

    // -------------------------------------------------------------------------
    // Fallback UI Builder (Creates full hierarchy dynamically if prefab absent)
    // -------------------------------------------------------------------------

    public void BuildRuntimeUI()
    {
        EnsureReferences();
    }

    public void EnsureReferences()
    {
        if (rootCanvas == null) rootCanvas = GetComponent<Canvas>();
        if (rootCanvas == null) rootCanvas = gameObject.AddComponent<Canvas>();
        rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        rootCanvas.sortingOrder = 2500; // Above all game HUD & modals

        if (canvasScaler == null) canvasScaler = GetComponent<CanvasScaler>();
        if (canvasScaler == null) canvasScaler = gameObject.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasScaler.matchWidthOrHeight = 0.5f;

        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

        if (rootCanvasGroup == null) rootCanvasGroup = GetComponent<CanvasGroup>();
        if (rootCanvasGroup == null) rootCanvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Load fonts if not assigned
#if UNITY_EDITOR
        if (titleFont == null) titleFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Fredoka SDF.asset");
        if (tipFont == null) tipFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Nunito SDF.asset");
        if (tipsDatabase == null) tipsDatabase = UnityEditor.AssetDatabase.LoadAssetAtPath<LoadingTips>("Assets/EcoQuest/UI/LoadingScreen/EcoQuestLoadingTips.asset");

        string dir = "Assets/EcoQuest/UI/LoadingScreen";
        if (forestBackgroundSprite == null) forestBackgroundSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/ForestLoadingBg.jpg");
        if (barFrameSprite == null) barFrameSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/LoadingBarFrame.png");
        if (barFillSprite == null) barFillSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/LoadingBarFill.png");
        if (leafHandleSprite == null) leafHandleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/LeafHandle.png");
        if (bottomGradientSprite == null) bottomGradientSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/BottomDarkGradient.png");
        if (fireflySprite == null) fireflySprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/FireflyGlow.png");
        if (leafParticleSprite == null) leafParticleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/LeafParticle.png");
        if (fogWispSprite == null) fogWispSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/FogWisp.png");
        if (mushroomGlowSprite == null) mushroomGlowSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/MushroomGlow.png");
        if (lightRaySprite == null) lightRaySprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/LightRay.png");
#endif

        if (ambientAudioSource == null)
        {
            ambientAudioSource = GetComponent<AudioSource>();
            if (ambientAudioSource == null) ambientAudioSource = gameObject.AddComponent<AudioSource>();
            ambientAudioSource.playOnAwake = false;
        }

        // 1. Background Container
        if (bgContainer == null)
        {
            Transform t = transform.Find("BgContainer");
            if (t != null) bgContainer = t as RectTransform;
            else
            {
                GameObject go = new GameObject("BgContainer", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                bgContainer = go.GetComponent<RectTransform>();
                bgContainer.anchorMin = Vector2.zero;
                bgContainer.anchorMax = Vector2.one;
                bgContainer.sizeDelta = Vector2.zero;
            }
        }

        // 2. Background Image with Aspect-Fill
        if (backgroundImage == null && bgContainer != null)
        {
            Transform t = bgContainer.Find("BackgroundImage");
            if (t != null) backgroundImage = t.GetComponent<Image>();
            else
            {
                GameObject go = new GameObject("BackgroundImage", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
                go.transform.SetParent(bgContainer, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.sizeDelta = Vector2.zero;

                AspectRatioFitter arf = go.GetComponent<AspectRatioFitter>();
                arf.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                arf.aspectRatio = 2752f / 1536f; // Native reference image aspect ratio

                backgroundImage = go.GetComponent<Image>();
                backgroundImage.sprite = forestBackgroundSprite;
                backgroundImage.color = Color.white;
                backgroundImage.raycastTarget = false;
            }
        }

        // 3. Particles Container
        if (particleContainer == null)
        {
            Transform t = transform.Find("ParticleContainer");
            if (t != null) particleContainer = t as RectTransform;
            else
            {
                GameObject go = new GameObject("ParticleContainer", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                particleContainer = go.GetComponent<RectTransform>();
                particleContainer.anchorMin = Vector2.zero;
                particleContainer.anchorMax = Vector2.one;
                particleContainer.sizeDelta = Vector2.zero;
            }
        }

        // 4. Bottom Dark Gradient Overlay (masks baked text & bar)
        if (bottomGradientImage == null)
        {
            Transform t = transform.Find("BottomDarkGradient");
            if (t != null) bottomGradientImage = t.GetComponent<Image>();
            else
            {
                GameObject go = new GameObject("BottomDarkGradient", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                // Covers bottom 34% of canvas completely
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 0.34f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = Vector2.zero;

                bottomGradientImage = go.GetComponent<Image>();
                bottomGradientImage.sprite = bottomGradientSprite;
                bottomGradientImage.type = Image.Type.Simple;
                bottomGradientImage.color = Color.white;
                bottomGradientImage.raycastTarget = false;
            }
        }

        // 5. Exit Bloom Flash Overlay
        if (exitFlashImage == null)
        {
            Transform t = transform.Find("ExitFlash");
            if (t != null) exitFlashImage = t.GetComponent<Image>();
            else
            {
                GameObject go = new GameObject("ExitFlash", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.sizeDelta = Vector2.zero;

                exitFlashImage = go.GetComponent<Image>();
                exitFlashImage.color = new Color(1f, 1f, 1f, 0f);
                exitFlashImage.raycastTarget = false;
            }
        }

        // 6. Bottom UI Panel (Fixed safe layout: Title, Wooden Bar, Pro Tips)
        if (bottomUIPanel == null)
        {
            Transform t = transform.Find("BottomUIPanel");
            if (t != null) bottomUIPanel = t as RectTransform;
            else
            {
                GameObject go = new GameObject("BottomUIPanel", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                bottomUIPanel = go.GetComponent<RectTransform>();
                bottomUIPanel.anchorMin = new Vector2(0.5f, 0f);
                bottomUIPanel.anchorMax = new Vector2(0.5f, 0f);
                bottomUIPanel.pivot = new Vector2(0.5f, 0f);
                bottomUIPanel.sizeDelta = new Vector2(1700f, 260f);
                bottomUIPanel.anchoredPosition = Vector2.zero;
            }
        }

        // 7. Golden "LOADING..." Title (Fredoka SDF)
        if (titleText == null && bottomUIPanel != null)
        {
            Transform t = bottomUIPanel.Find("TitleText");
            if (t != null) titleText = t.GetComponent<TMP_Text>();
            else
            {
                GameObject go = new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(bottomUIPanel, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(-670f, 150f);
                rt.sizeDelta = new Vector2(400f, 60f);

                titleText = go.GetComponent<TextMeshProUGUI>();
                if (titleFont != null) titleText.font = titleFont;
                titleText.text = "LOADING...";
                titleText.fontSize = 44f;
                titleText.fontStyle = FontStyles.Bold;
                titleText.alignment = TextAlignmentOptions.BottomLeft;
                titleText.color = new Color(1.0f, 0.84f, 0.32f, 1f); // Rich golden yellow
                titleText.outlineColor = new Color32(40, 20, 10, 255);
                titleText.outlineWidth = 0.28f;
                titleText.raycastTarget = false;
            }
        }

        // 8. Wooden Bar Frame Container
        if (barContainer == null && bottomUIPanel != null)
        {
            Transform t = bottomUIPanel.Find("BarContainer");
            if (t != null) barContainer = t as RectTransform;
            else
            {
                GameObject go = new GameObject("BarContainer", typeof(RectTransform));
                go.transform.SetParent(bottomUIPanel, false);
                barContainer = go.GetComponent<RectTransform>();
                barContainer.anchorMin = new Vector2(0.5f, 0f);
                barContainer.anchorMax = new Vector2(0.5f, 0f);
                barContainer.pivot = new Vector2(0.5f, 0.5f);
                barContainer.anchoredPosition = new Vector2(0f, 118f);
                barContainer.sizeDelta = new Vector2(1360f, 52f);
            }
        }

        // 9. Bar Frame Image (9-sliced wood)
        if (barFrameImage == null && barContainer != null)
        {
            Transform t = barContainer.Find("BarFrame");
            if (t != null) barFrameImage = t.GetComponent<Image>();
            else
            {
                GameObject go = new GameObject("BarFrame", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(barContainer, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.sizeDelta = Vector2.zero;

                barFrameImage = go.GetComponent<Image>();
                barFrameImage.sprite = barFrameSprite;
                barFrameImage.type = Image.Type.Sliced;
                barFrameImage.color = Color.white;
                barFrameImage.raycastTarget = false;
            }
        }

        // 10. Fill Track Container & Image
        if (fillTrackRect == null && barContainer != null)
        {
            Transform t = barContainer.Find("FillTrack");
            if (t != null) fillTrackRect = t as RectTransform;
            else
            {
                GameObject go = new GameObject("FillTrack", typeof(RectTransform));
                go.transform.SetParent(barContainer, false);
                fillTrackRect = go.GetComponent<RectTransform>();
                // Inset inside wooden frame
                fillTrackRect.anchorMin = new Vector2(0f, 0f);
                fillTrackRect.anchorMax = new Vector2(1f, 1f);
                fillTrackRect.offsetMin = new Vector2(12f, 10f);
                fillTrackRect.offsetMax = new Vector2(-12f, -10f);
            }
        }

        if (fillBarImage == null && fillTrackRect != null)
        {
            Transform t = fillTrackRect.Find("FillBar");
            if (t != null) fillBarImage = t.GetComponent<Image>();
            else
            {
                GameObject go = new GameObject("FillBar", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(fillTrackRect, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.sizeDelta = Vector2.zero;

                fillBarImage = go.GetComponent<Image>();
                fillBarImage.sprite = barFillSprite;
                fillBarImage.type = Image.Type.Filled;
                fillBarImage.fillMethod = Image.FillMethod.Horizontal;
                fillBarImage.fillOrigin = (int)Image.OriginHorizontal.Left;
                fillBarImage.fillAmount = 0f;
                fillBarImage.color = Color.white;
                fillBarImage.raycastTarget = false;
            }
        }

        // 11. Leaf Handle Image
        if (leafHandleRect == null && fillTrackRect != null)
        {
            Transform t = fillTrackRect.Find("LeafHandle");
            if (t != null)
            {
                leafHandleRect = t as RectTransform;
                leafHandleImage = t.GetComponent<Image>();
            }
            else
            {
                GameObject go = new GameObject("LeafHandle", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(fillTrackRect, false);
                leafHandleRect = go.GetComponent<RectTransform>();
                leafHandleRect.pivot = new Vector2(0.5f, 0.5f);
                leafHandleRect.sizeDelta = new Vector2(62f, 62f);
                leafHandleRect.anchoredPosition = new Vector2(-fillTrackRect.rect.width * 0.5f, 0f);

                leafHandleImage = go.GetComponent<Image>();
                leafHandleImage.sprite = leafHandleSprite;
                leafHandleImage.color = Color.white;
                leafHandleImage.raycastTarget = false;
            }
        }

        // 12. Percent Text (Optional toggle)
        if (percentText == null && barContainer != null)
        {
            Transform t = barContainer.Find("PercentText");
            if (t != null) percentText = t.GetComponent<TMP_Text>();
            else
            {
                GameObject go = new GameObject("PercentText", typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(barContainer, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(16f, 0f);
                rt.sizeDelta = new Vector2(100f, 40f);

                percentText = go.GetComponent<TextMeshProUGUI>();
                if (titleFont != null) percentText.font = titleFont;
                percentText.fontSize = 24f;
                percentText.fontStyle = FontStyles.Bold;
                percentText.color = new Color(0.95f, 0.95f, 0.85f, 1f);
                percentText.alignment = TextAlignmentOptions.MidlineLeft;
                percentText.raycastTarget = false;
                percentText.gameObject.SetActive(showPercentage);
            }
        }

        // 13. Pro Tips Container & Text (Nunito SDF)
        if (tipText == null && bottomUIPanel != null)
        {
            Transform t = bottomUIPanel.Find("TipContainer");
            GameObject tipContainer;
            if (t != null)
            {
                tipContainer = t.gameObject;
                tipCanvasGroup = tipContainer.GetComponent<CanvasGroup>();
                tipText = tipContainer.GetComponentInChildren<TMP_Text>();
            }
            else
            {
                tipContainer = new GameObject("TipContainer", typeof(RectTransform), typeof(CanvasGroup));
                tipContainer.transform.SetParent(bottomUIPanel, false);
                RectTransform crt = tipContainer.GetComponent<RectTransform>();
                crt.anchorMin = new Vector2(0.5f, 0f);
                crt.anchorMax = new Vector2(0.5f, 0f);
                crt.pivot = new Vector2(0.5f, 0.5f);
                crt.anchoredPosition = new Vector2(0f, 48f);
                crt.sizeDelta = new Vector2(1450f, 54f);

                tipCanvasGroup = tipContainer.GetComponent<CanvasGroup>();

                GameObject txtGo = new GameObject("TipText", typeof(RectTransform), typeof(TextMeshProUGUI));
                txtGo.transform.SetParent(tipContainer.transform, false);
                RectTransform trt = txtGo.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.sizeDelta = Vector2.zero;

                tipText = txtGo.GetComponent<TextMeshProUGUI>();
                if (tipFont != null) tipText.font = tipFont;
                tipText.fontSize = 22f;
                tipText.enableAutoSizing = true;
                tipText.fontSizeMin = 16f;
                tipText.fontSizeMax = 23f;
                tipText.alignment = TextAlignmentOptions.Center;
                tipText.color = new Color(0.96f, 0.91f, 0.82f, 1f); // Soft cream
                tipText.text = "Pro Tips: Always put correct trash in the recycle bins to restore the forest and earn gold!";
                tipText.raycastTarget = false;
            }
        }
    }

    private void OnDisable()
    {
        if (activeLoadRoutine != null)
        {
            StopCoroutine(activeLoadRoutine);
            activeLoadRoutine = null;
        }
        if (activeTipRoutine != null)
        {
            StopCoroutine(activeTipRoutine);
            activeTipRoutine = null;
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
            _isLoading = false;
        }
    }

    // -------------------------------------------------------------------------
    // Editor Preview & Debug Tools
    // -------------------------------------------------------------------------

    public void EditorPreviewSetProgress(float progress)
    {
        EnsureReferences();
        if (rootCanvas != null) rootCanvas.enabled = true;
        if (rootCanvasGroup != null) rootCanvasGroup.alpha = 1f;

        currentDisplayProgress = Mathf.Clamp01(progress);
        isReady = (currentDisplayProgress >= 0.999f);

        UpdateProgressUI(currentDisplayProgress);

        if (titleText != null)
        {
            titleText.text = isReady ? "READY!" : "LOADING...";
        }

        if (leafHandleRect != null && fillTrackRect != null)
        {
            float trackW = fillTrackRect.rect.width;
            float targetX = (trackW * currentDisplayProgress) - (trackW * 0.5f);
            leafHandleRect.anchoredPosition = new Vector2(targetX, 0f);
        }
    }

    public void EditorPreviewNextTip()
    {
        EnsureReferences();
        DisplayNextTip(LoadingTips.SceneFilter.Any, immediate: true);
    }

    public void EditorPreviewHide()
    {
        if (rootCanvasGroup != null) rootCanvasGroup.alpha = 0f;
        if (rootCanvas != null) rootCanvas.enabled = false;
    }
}
