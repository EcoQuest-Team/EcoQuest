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

    [Header("Fonts & Materials")]
    [SerializeField] private TMP_FontAsset titleFont;
    [SerializeField] private TMP_FontAsset tipFont;
    [SerializeField] private Material titleMaterial;
    [SerializeField] private Material tipMaterial;

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
    [SerializeField] private RectTransform artFrame;
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

    public void UpdateArtFrameLayout()
    {
        if (artFrame == null || rootCanvas == null) return;
        RectTransform canvasRect = rootCanvas.transform as RectTransform;
        if (canvasRect == null) return;

        float cw = canvasRect.rect.width;
        float ch = canvasRect.rect.height;
        if (cw <= 0 || ch <= 0)
        {
            if (canvasScaler != null)
            {
                cw = canvasScaler.referenceResolution.x;
                ch = canvasScaler.referenceResolution.y;
            }
            else
            {
                cw = 1920f;
                ch = 1080f;
            }
        }

        float targetAspect = 2752f / 1536f;
        float parentAspect = cw / ch;
        float w, h;
        if (parentAspect >= targetAspect)
        {
            w = cw;
            h = cw / targetAspect;
        }
        else
        {
            h = ch;
            w = ch * targetAspect;
        }

        artFrame.pivot = new Vector2(0.5f, 0f);
        artFrame.anchorMin = new Vector2(0.5f, 0f);
        artFrame.anchorMax = new Vector2(0.5f, 0f);
        artFrame.sizeDelta = new Vector2(w, h);
        artFrame.anchoredPosition = Vector2.zero;
    }

    private void Update()
    {
        if (!_isLoading && (rootCanvasGroup == null || rootCanvasGroup.alpha <= 0.001f))
            return;

        UpdateArtFrameLayout();

        float dt = Time.unscaledDeltaTime;
        float time = Time.unscaledTime;

        // 1. Subtle camera drift on background (< 3%)
        if (enableCameraDrift && backgroundImage != null)
        {
            float zoom = 1.01f + Mathf.Sin(time * 0.25f) * 0.008f;
            float panX = Mathf.Sin(time * 0.2f) * 8f;
            float panY = Mathf.Cos(time * 0.22f) * 4f;
            backgroundImage.rectTransform.localScale = new Vector3(zoom, zoom, 1f);
            backgroundImage.rectTransform.anchoredPosition = new Vector2(panX, panY);
        }

        // 2. Title animation (fixed reserved dot width: word NEVER shifts)
        if (titleText != null)
        {
            int dots = ((int)(time * 3.0f) % 3) + 1;
            string dotsVisible = new string('.', dots);
            string dotsHidden = new string('.', 3 - dots);
            titleText.text = $"LOADING{dotsVisible}<color=#00000000>{dotsHidden}</color>";
            float bounceY = Mathf.Sin(time * 3.5f) * 2f;
            titleText.rectTransform.anchoredPosition = baseTitlePos + new Vector2(0f, bounceY);
        }

        // 3. Leaf handle animation
        if (leafHandleRect != null && fillTrackRect != null)
        {
            float trackW = fillTrackRect.rect.width;
            float targetX = currentDisplayProgress * trackW;
            leafHandleRect.anchoredPosition = new Vector2(targetX, Mathf.Sin(time * 3.5f) * 1.5f);

            float sway = Mathf.Sin(time * 3.0f) * 2.5f;
            leafHandleRect.localRotation = Quaternion.Euler(0f, 0f, sway);
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
            // Keep titleText "LOADING..." - no "READY!" change
            StartCoroutine(PunchTransform(titleText.rectTransform, 1.25f, 0.25f));
        }

        if (leafHandleRect != null)
        {
            StartCoroutine(PunchTransform(leafHandleRect, 1.4f, 0.3f));
        }

        if (AudioManager.Instance != null)
        {
            if (readyChimeClip != null) AudioManager.Instance.PlayUI(readyChimeClip, 0.8f);
            else AudioManager.Instance.PlayLoadingReady();
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

        if (tipChangeClip != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayUI(tipChangeClip, 0.4f);
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

        if (leafHandleRect != null && fillTrackRect != null)
        {
            float trackW = fillTrackRect.rect.width;
            leafHandleRect.anchoredPosition = new Vector2(clamped * trackW, 0f);
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

#if UNITY_EDITOR
        string dir = "Assets/EcoQuest/UI/LoadingScreen";
        if (titleFont == null) titleFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Fredoka SDF.asset");
        if (tipFont == null) tipFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Nunito SDF.asset");
        if (titleMaterial == null) titleMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>($"{dir}/LoadingTitle_Fredoka.mat");
        if (tipMaterial == null) tipMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>($"{dir}/LoadingTip_Nunito.mat");
        if (tipsDatabase == null) tipsDatabase = UnityEditor.AssetDatabase.LoadAssetAtPath<LoadingTips>($"{dir}/EcoQuestLoadingTips.asset");

        if (forestBackgroundSprite == null) forestBackgroundSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/LoadingBG_Clean.png");
        if (forestBackgroundSprite == null) forestBackgroundSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/ForestLoadingBg.jpg");
        if (barFrameSprite == null) barFrameSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/LoadingBarFrame.png");
        if (barFillSprite == null) barFillSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/LoadingBarFill.png");
        if (leafHandleSprite == null) leafHandleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/LeafHandle.png");
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

        // Clean up deprecated / unwanted objects
        Transform oldBottomGrad = transform.Find("BottomDarkGradient");
        if (oldBottomGrad != null) DestroyImmediate(oldBottomGrad.gameObject);

        Transform oldPanel = transform.Find("BottomUIPanel");
        if (oldPanel != null) DestroyImmediate(oldPanel.gameObject);

        Transform oldBg = transform.Find("BgContainer");
        if (oldBg != null && oldBg != (artFrame != null ? artFrame.transform : null)) DestroyImmediate(oldBg.gameObject);

        // 1. Art Frame: Parent container that maintains 2752:1536 aspect ratio bottom-aligned
        if (artFrame == null)
        {
            Transform t = transform.Find("ArtFrame");
            if (t != null) artFrame = t as RectTransform;
            else
            {
                GameObject go = new GameObject("ArtFrame", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                artFrame = go.GetComponent<RectTransform>();
            }
        }
        artFrame.pivot = new Vector2(0.5f, 0f);
        artFrame.anchorMin = new Vector2(0.5f, 0f);
        artFrame.anchorMax = new Vector2(0.5f, 0f);
        UpdateArtFrameLayout();

        // 2. Background Image inside ArtFrame
        if (backgroundImage == null && artFrame != null)
        {
            Transform t = artFrame.Find("BackgroundImage");
            if (t != null) backgroundImage = t.GetComponent<Image>();
            else
            {
                GameObject go = new GameObject("BackgroundImage", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(artFrame, false);
                backgroundImage = go.GetComponent<Image>();
            }
        }
        if (backgroundImage != null)
        {
            RectTransform rt = backgroundImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            backgroundImage.sprite = forestBackgroundSprite;
            backgroundImage.type = Image.Type.Simple;
            backgroundImage.color = Color.white;
            backgroundImage.raycastTarget = false;
        }

        // 3. Particle Container inside ArtFrame
        if (particleContainer == null && artFrame != null)
        {
            Transform t = artFrame.Find("ParticleContainer");
            if (t != null) particleContainer = t as RectTransform;
            else
            {
                GameObject go = new GameObject("ParticleContainer", typeof(RectTransform));
                go.transform.SetParent(artFrame, false);
                particleContainer = go.GetComponent<RectTransform>();
            }
        }
        if (particleContainer != null)
        {
            particleContainer.anchorMin = Vector2.zero;
            particleContainer.anchorMax = Vector2.one;
            particleContainer.offsetMin = Vector2.zero;
            particleContainer.offsetMax = Vector2.zero;
        }

        // 4. Title Text (LOADING...) inside ArtFrame
        // Full res box: x 440 to 880 (w=440), y 1195 to 1292 (Unity Y 244 to 341)
        if (titleText == null && artFrame != null)
        {
            Transform t = artFrame.Find("TitleText");
            if (t != null) titleText = t.GetComponent<TMP_Text>();
            else
            {
                GameObject go = new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(artFrame, false);
                titleText = go.GetComponent<TextMeshProUGUI>();
            }
        }
        if (titleText != null)
        {
            RectTransform rt = titleText.rectTransform;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchorMin = new Vector2(440f / 2752f, 244f / 1536f);
            rt.anchorMax = new Vector2(880f / 2752f, 341f / 1536f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            if (titleFont != null) titleText.font = titleFont;
            if (titleMaterial != null) titleText.fontSharedMaterial = titleMaterial;
            titleText.text = "LOADING...";
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = 24f;
            titleText.fontSizeMax = 72f;
            titleText.alignment = TextAlignmentOptions.MidlineLeft;
            titleText.enableWordWrapping = false;
            titleText.enableVertexGradient = true;
            titleText.colorGradient = new VertexGradient(
                new Color32(255, 238, 70, 255),
                new Color32(255, 238, 70, 255),
                new Color32(238, 142, 22, 255),
                new Color32(238, 142, 22, 255)
            );
            titleText.raycastTarget = false;
            baseTitlePos = rt.anchoredPosition;
        }

        // 5. Wooden Bar Frame Container inside ArtFrame
        // Full res box: x 418 to 2333, y 1314 to 1414 (Unity Y 122 to 222)
        if (barContainer == null && artFrame != null)
        {
            Transform t = artFrame.Find("BarContainer");
            if (t != null) barContainer = t as RectTransform;
            else
            {
                GameObject go = new GameObject("BarContainer", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(artFrame, false);
                barContainer = go.GetComponent<RectTransform>();
                barFrameImage = go.GetComponent<Image>();
            }
        }
        if (barContainer != null)
        {
            barContainer.pivot = new Vector2(0.5f, 0.5f);
            barContainer.anchorMin = new Vector2(418f / 2752f, 122f / 1536f);
            barContainer.anchorMax = new Vector2(2333f / 2752f, 222f / 1536f);
            barContainer.offsetMin = Vector2.zero;
            barContainer.offsetMax = Vector2.zero;

            if (barFrameImage == null) barFrameImage = barContainer.GetComponent<Image>();
            if (barFrameImage != null)
            {
                barFrameImage.sprite = barFrameSprite;
                barFrameImage.type = Image.Type.Sliced;
                barFrameImage.color = Color.white;
                barFrameImage.raycastTarget = false;
            }
        }

        // 6. Fill Track inside BarContainer
        // Inset: left 27px, right 21px, top 24px, bottom 24px
        if (fillTrackRect == null && barContainer != null)
        {
            Transform t = barContainer.Find("FillTrack");
            if (t != null) fillTrackRect = t as RectTransform;
            else
            {
                GameObject go = new GameObject("FillTrack", typeof(RectTransform));
                go.transform.SetParent(barContainer, false);
                fillTrackRect = go.GetComponent<RectTransform>();
            }
        }
        if (fillTrackRect != null)
        {
            fillTrackRect.anchorMin = new Vector2(27f / 1915f, 24f / 100f);
            fillTrackRect.anchorMax = new Vector2(1f - (21f / 1915f), 1f - (24f / 100f));
            fillTrackRect.offsetMin = Vector2.zero;
            fillTrackRect.offsetMax = Vector2.zero;
        }

        // 7. Fill Bar inside FillTrack
        if (fillBarImage == null && fillTrackRect != null)
        {
            Transform t = fillTrackRect.Find("FillBar");
            if (t != null) fillBarImage = t.GetComponent<Image>();
            else
            {
                GameObject go = new GameObject("FillBar", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(fillTrackRect, false);
                fillBarImage = go.GetComponent<Image>();
            }
        }
        if (fillBarImage != null)
        {
            RectTransform rt = fillBarImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            fillBarImage.sprite = barFillSprite;
            fillBarImage.type = Image.Type.Filled;
            fillBarImage.fillMethod = Image.FillMethod.Horizontal;
            fillBarImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillBarImage.fillAmount = currentDisplayProgress;
            fillBarImage.color = Color.white;
            fillBarImage.raycastTarget = false;
        }

        // 8. Leaf Handle inside FillTrack
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
                leafHandleImage = go.GetComponent<Image>();
            }
        }
        if (leafHandleRect != null)
        {
            leafHandleRect.pivot = new Vector2(0.5f, 0.5f);
            leafHandleRect.anchorMin = new Vector2(0f, 0.5f);
            leafHandleRect.anchorMax = new Vector2(0f, 0.5f);
            leafHandleRect.sizeDelta = new Vector2(126f, 159f);
            float trackW = fillTrackRect.rect.width > 0 ? fillTrackRect.rect.width : 1867f;
            leafHandleRect.anchoredPosition = new Vector2(currentDisplayProgress * trackW, 0f);
            leafHandleRect.localRotation = Quaternion.identity;

            if (leafHandleImage != null)
            {
                leafHandleImage.sprite = leafHandleSprite;
                leafHandleImage.color = Color.white;
                leafHandleImage.raycastTarget = false;
            }
        }

        // 9. Tip Container & Tip Text inside ArtFrame
        // Full res box: x 440 to 2240, y 1442 to 1495 (Unity Y 41 to 94)
        if (tipText == null && artFrame != null)
        {
            Transform t = artFrame.Find("TipContainer");
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
                tipContainer.transform.SetParent(artFrame, false);
                tipCanvasGroup = tipContainer.GetComponent<CanvasGroup>();

                GameObject txtGo = new GameObject("TipText", typeof(RectTransform), typeof(TextMeshProUGUI));
                txtGo.transform.SetParent(tipContainer.transform, false);
                tipText = txtGo.GetComponent<TextMeshProUGUI>();
            }
        }
        if (tipText != null)
        {
            Transform tipContainer = tipText.transform.parent;
            if (tipContainer != null && tipContainer != artFrame)
            {
                RectTransform crt = tipContainer as RectTransform;
                crt.pivot = new Vector2(0.5f, 0.5f);
                crt.anchorMin = new Vector2(440f / 2752f, 41f / 1536f);
                crt.anchorMax = new Vector2(2240f / 2752f, 94f / 1536f);
                crt.offsetMin = Vector2.zero;
                crt.offsetMax = Vector2.zero;
            }

            RectTransform trt = tipText.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            if (tipFont != null) tipText.font = tipFont;
            if (tipMaterial != null) tipText.fontSharedMaterial = tipMaterial;
            tipText.enableAutoSizing = true;
            tipText.fontSizeMin = 16f;
            tipText.fontSizeMax = 38f;
            tipText.alignment = TextAlignmentOptions.Center;
            tipText.color = new Color(0.871f, 0.808f, 0.647f, 1f); // Soft cream #DECEA5
            tipText.enableWordWrapping = false;
            tipText.raycastTarget = false;
            baseTipPos = trt.anchoredPosition;
        }

        // 10. Exit Flash (Canvas overlay)
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

    public void SetStaticTestState(float progress, string tip, bool showThreeDots)
    {
        EnsureReferences();
        UpdateArtFrameLayout();
        if (rootCanvas != null) rootCanvas.enabled = true;
        if (rootCanvasGroup != null) rootCanvasGroup.alpha = 1f;

        currentDisplayProgress = Mathf.Clamp01(progress);
        UpdateProgressUI(currentDisplayProgress);

        if (backgroundImage != null)
        {
            backgroundImage.rectTransform.localScale = Vector3.one;
            backgroundImage.rectTransform.anchoredPosition = Vector2.zero;
        }

        if (titleText != null)
        {
            titleText.text = showThreeDots ? "LOADING..." : "LOADING";
            titleText.rectTransform.anchoredPosition = baseTitlePos;
        }

        if (leafHandleRect != null)
        {
            leafHandleRect.localRotation = Quaternion.identity;
        }

        if (tipText != null && !string.IsNullOrEmpty(tip))
        {
            tipText.text = tip.StartsWith("Pro Tips:") ? tip : $"Pro Tips: {tip}";
            tipText.rectTransform.anchoredPosition = baseTipPos;
        }
        if (tipCanvasGroup != null) tipCanvasGroup.alpha = 1f;
    }

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
            titleText.text = "LOADING...";
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
