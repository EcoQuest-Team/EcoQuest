using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class SceneTransition : MonoBehaviour
{
    private static SceneTransition _instance;
    public static SceneTransition Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SceneTransition>();
#if UNITY_EDITOR
                if (_instance == null)
                {
                    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/Prefabs/SceneTransition.prefab");
                    if (prefab != null)
                    {
                        var go = Instantiate(prefab);
                        _instance = go.GetComponent<SceneTransition>();
                    }
                }
#endif
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("Timing & Easing (Unscaled Time)")]
    [Tooltip("Short delay to let the clicked button squish before fading menu UI")]
    public float buttonSquishDelay = 0.12f;
    [Tooltip("Duration for menu logo and buttons to fade out")]
    public float menuFadeOutDuration = 0.4f;
    [Tooltip("Duration of the leaf-themed screen wipe (in and out)")]
    public float wipeDuration = 0.8f;
    [Tooltip("Minimum time to display the loading screen and eco tip")]
    public float minLoadingTime = 1.5f;
    [Tooltip("Smooth easing curve for the leaf wipe")]
    public AnimationCurve wipeEasing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Eco Tips for Kids")]
    [TextArea(2, 3)]
    public string[] ecoTips = new string[]
    {
        "Banana peels go in the Biodegradable bin!",
        "Rinse plastic bottles and tin cans before recycling them!",
        "Old paper and cardboard can be recycled into brand new paper!",
        "Turning off lights when you leave a room helps save energy!",
        "Using a reusable water bottle keeps plastic out of our oceans!",
        "Planting trees gives birds, bees, and butterflies a cozy home!"
    };

    [Header("Audio Hooks (No Audio Files Assigned)")]
    public AudioSource startSoundSource;
    public AudioSource whooshSoundSource;

    [Header("UI References")]
    public Canvas transitionCanvas;
    public CanvasGroup overlayCanvasGroup;
    public RectTransform wipeContainer;
    public CanvasGroup loadingContentGroup;
    public Image progressBarFill;
    public TMP_Text tipText;
    public TMP_Text progressPercentText;

    private bool isTransitioning;
    public bool IsTransitioning => isTransitioning || LoadingScreen.IsLoading;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (overlayCanvasGroup != null)
        {
            overlayCanvasGroup.alpha = 0f;
            overlayCanvasGroup.blocksRaycasts = false;
            overlayCanvasGroup.interactable = false;
        }

        if (wipeContainer != null)
        {
            SetWipeProgress(0f, entering: true);
        }
    }

    public void LoadScene(string sceneName)
    {
        if (transitionCanvas != null) transitionCanvas.enabled = false;
        LoadingScreen.Load(sceneName);
    }

    private IEnumerator TransitionRoutine(string sceneName)
    {
        isTransitioning = true;

        // Immediately block clicks via overlay CanvasGroup and disable any active buttons in the current scene
        if (overlayCanvasGroup != null)
        {
            overlayCanvasGroup.alpha = 1f;
            overlayCanvasGroup.blocksRaycasts = true;
            overlayCanvasGroup.interactable = true;
        }

        DisableCurrentSceneButtons(out CanvasGroup[] fadedMenuGroups);

        // Play start sound hook if assigned
        if (startSoundSource != null && startSoundSource.clip != null)
        {
            startSoundSource.Play();
        }

        // 1. Trigger squish on Start Game button if present and wait briefly
        GameObject playBtn = GameObject.Find("Play Button");
        if (playBtn != null)
        {
            var hoverFx = playBtn.GetComponent<ButtonHoverFX>();
            if (hoverFx != null)
            {
                hoverFx.OnPointerDown(null);
            }
        }

        if (buttonSquishDelay > 0f)
        {
            yield return WaitUnscaled(buttonSquishDelay);
        }

        // 2. Fade out menu logo and buttons (~0.4s)
        if (fadedMenuGroups != null && fadedMenuGroups.Length > 0 && menuFadeOutDuration > 0f)
        {
            float t = 0f;
            while (t < menuFadeOutDuration)
            {
                t += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(t / menuFadeOutDuration);
                for (int i = 0; i < fadedMenuGroups.Length; i++)
                {
                    if (fadedMenuGroups[i] != null)
                        fadedMenuGroups[i].alpha = a;
                }
                yield return null;
            }
            for (int i = 0; i < fadedMenuGroups.Length; i++)
            {
                if (fadedMenuGroups[i] != null)
                    fadedMenuGroups[i].alpha = 0f;
            }
        }

        // Pick a random eco tip and reset progress bar before wipe covers screen
        if (tipText != null && ecoTips != null && ecoTips.Length > 0)
        {
            string tip = ecoTips[Random.Range(0, ecoTips.Length)];
            tipText.text = "Eco Tip: " + tip;
        }
        UpdateProgressUI(0f);
        if (loadingContentGroup != null)
        {
            loadingContentGroup.alpha = 0f;
        }

        // 3. Leaf-themed wipe covers the screen (~0.8s)
        if (whooshSoundSource != null && whooshSoundSource.clip != null)
        {
            whooshSoundSource.Play();
        }

        float wipeTimer = 0f;
        float safeWipeDur = Mathf.Max(0.05f, wipeDuration);
        while (wipeTimer < safeWipeDur)
        {
            wipeTimer += Time.unscaledDeltaTime;
            float norm = Mathf.Clamp01(wipeTimer / safeWipeDur);
            float eval = wipeEasing != null ? wipeEasing.Evaluate(norm) : Mathf.SmoothStep(0f, 1f, norm);
            SetWipeProgress(eval, entering: true);

            if (loadingContentGroup != null)
            {
                loadingContentGroup.alpha = Mathf.Clamp01((norm - 0.5f) * 2f);
            }
            yield return null;
        }
        SetWipeProgress(1f, entering: true);
        if (loadingContentGroup != null)
        {
            loadingContentGroup.alpha = 1f;
        }

        // 4. Load target scene asynchronously with allowSceneActivation = false
        AsyncOperation asyncOp = SceneManager.LoadSceneAsync(sceneName);
        if (asyncOp != null)
        {
            asyncOp.allowSceneActivation = false;
            float loadElapsed = 0f;
            float safeMinLoad = Mathf.Max(0.1f, minLoadingTime);

            while (asyncOp.progress < 0.9f || loadElapsed < safeMinLoad)
            {
                loadElapsed += Time.unscaledDeltaTime;
                float asyncNorm = Mathf.Clamp01(asyncOp.progress / 0.9f);
                float timeNorm = Mathf.Clamp01(loadElapsed / safeMinLoad);
                float displayProgress = Mathf.Min(asyncNorm, timeNorm);
                UpdateProgressUI(displayProgress);
                yield return null;
            }

            UpdateProgressUI(1f);
            asyncOp.allowSceneActivation = true;

            while (!asyncOp.isDone)
            {
                yield return null;
            }
        }

        // Wait one frame for the new scene's Awake/Start to initialize
        yield return null;

        // Hold player control frozen while the reverse wipe reveals the new scene
        SetGameplayPlayerFrozen(true);

        // 5. Reverse the leaf wipe in the new scene (~0.8s) to reveal gameplay
        if (whooshSoundSource != null && whooshSoundSource.clip != null)
        {
            whooshSoundSource.Play();
        }

        wipeTimer = 0f;
        while (wipeTimer < safeWipeDur)
        {
            wipeTimer += Time.unscaledDeltaTime;
            float norm = Mathf.Clamp01(wipeTimer / safeWipeDur);
            float eval = wipeEasing != null ? wipeEasing.Evaluate(norm) : Mathf.SmoothStep(0f, 1f, norm);

            if (loadingContentGroup != null)
            {
                loadingContentGroup.alpha = 1f - Mathf.Clamp01(norm * 2.5f);
            }

            SetWipeProgress(eval, entering: false);
            yield return null;
        }

        SetWipeProgress(0f, entering: true);
        if (loadingContentGroup != null)
        {
            loadingContentGroup.alpha = 0f;
        }

        // 6. Unblock raycasts and hand control to the player
        if (overlayCanvasGroup != null)
        {
            overlayCanvasGroup.alpha = 0f;
            overlayCanvasGroup.blocksRaycasts = false;
            overlayCanvasGroup.interactable = false;
        }

        SetGameplayPlayerFrozen(false);
        isTransitioning = false;
    }

    private void DisableCurrentSceneButtons(out CanvasGroup[] groupsToFade)
    {
        var buttons = FindObjectsByType<Button>(FindObjectsSortMode.None);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && (transitionCanvas == null || !buttons[i].transform.IsChildOf(transitionCanvas.transform)))
            {
                buttons[i].interactable = false;
            }
        }

        // Collect main menu logo & buttons for smooth 0.4s fade out without modifying their saved scene layout
        string[] targetNames = new[] { "EcoQuest Text", "Play Button", "settings", "Quit" };
        var list = new System.Collections.Generic.List<CanvasGroup>();
        for (int i = 0; i < targetNames.Length; i++)
        {
            GameObject go = GameObject.Find(targetNames[i]);
            if (go != null)
            {
                CanvasGroup cg = go.GetComponent<CanvasGroup>();
                if (cg == null) cg = go.AddComponent<CanvasGroup>();
                list.Add(cg);
            }
        }
        groupsToFade = list.ToArray();
    }

    private void SetWipeProgress(float t, bool entering)
    {
        if (wipeContainer == null) return;
        float canvasW = transitionCanvas != null ? ((RectTransform)transitionCanvas.transform).rect.width : 1920f;
        if (canvasW <= 10f) canvasW = 1920f;
        float widthSpan = canvasW + 650f;
        float x = entering
            ? Mathf.Lerp(-widthSpan, 0f, t)
            : Mathf.Lerp(0f, widthSpan, t);
        wipeContainer.anchoredPosition = new Vector2(x, 0f);
    }

    private void UpdateProgressUI(float progress)
    {
        float clamped = Mathf.Clamp01(progress);
        if (progressBarFill != null)
        {
            progressBarFill.fillAmount = clamped;
        }
        if (progressPercentText != null)
        {
            progressPercentText.text = $"Loading... {Mathf.RoundToInt(clamped * 100f)}%";
        }
    }

    private void SetGameplayPlayerFrozen(bool frozen)
    {
        if (IntroSequenceController.Instance != null)
        {
            // Only unfreeze if IntroSequenceController is in normal Gameplay state
            if (!frozen && IntroSequenceController.Instance.CurrentState != IntroSequenceController.IntroState.Gameplay)
                return;
            IntroSequenceController.Instance.SetPlayerControlsFrozen(frozen);
            return;
        }

        var fpc = FindFirstObjectByType<StarterAssets.FirstPersonController>();
        if (fpc != null)
        {
            fpc.enabled = !frozen;
        }
    }

    private IEnumerator WaitUnscaled(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}
