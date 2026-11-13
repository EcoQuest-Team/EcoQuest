using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent SettingsManager singleton (DontDestroyOnLoad) that saves and loads all settings
/// in PlayerPrefs using the EcoQuest_Settings_ prefix and applies them before the first frame
/// and across all scenes. Never touches EcoQuest_PreQuizScore or EcoQuest_PostQuizScore.
/// </summary>
public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    // PlayerPrefs Keys
    public const string KeyMasterVol = "EcoQuest_Settings_MasterVol";
    public const string KeyMusicVol = "EcoQuest_Settings_MusicVol";
    public const string KeySFXVol = "EcoQuest_Settings_SFXVol";
    public const string KeyMuteAll = "EcoQuest_Settings_MuteAll";
    public const string KeyQuality = "EcoQuest_Settings_Quality";
    public const string KeyFullscreen = "EcoQuest_Settings_Fullscreen";
    public const string KeyResolutionWidth = "EcoQuest_Settings_ResolutionWidth";
    public const string KeyResolutionHeight = "EcoQuest_Settings_ResolutionHeight";
    public const string KeyResolutionAuto = "EcoQuest_Settings_ResolutionAuto";
    public const string KeyVSync = "EcoQuest_Settings_VSync";
    public const string KeyFPSLimit = "EcoQuest_Settings_FPSLimit";
    public const string KeyMouseSensitivity = "EcoQuest_Settings_MouseSensitivity";
    public const string KeyTextSize = "EcoQuest_Settings_TextSize";
    private const string KeyTextSizeV2Migrated = "EcoQuest_Settings_TextSize_v2_Migrated";

    // Default Values
    public const float DefaultMasterVol = 1.0f;
    public const float DefaultMusicVol = 0.8f;
    public const float DefaultSFXVol = 0.8f;
    public const bool DefaultMuteAll = false;
    public const int DefaultQuality = 2; // High (0 = Low, 1 = Medium, 2 = High, 3 = Ultra)
    public const bool DefaultFullscreen = true;
    public const bool DefaultResolutionAuto = true;
    public const bool DefaultVSync = true;
    public const int DefaultFPSLimit = -1; // Unlimited
    public const float DefaultMouseSensitivity = 1.0f;
    public const int DefaultTextSize = 1; // 0 = Small, 1 = Medium, 2 = Large

    // Current Settings State
    public float MasterVolume { get; private set; } = DefaultMasterVol;
    public float MusicVolume { get; private set; } = DefaultMusicVol;
    public float SFXVolume { get; private set; } = DefaultSFXVol;
    public bool MuteAll { get; private set; } = DefaultMuteAll;

    public int QualityIndex { get; private set; } = DefaultQuality;
    public bool Fullscreen { get; private set; } = DefaultFullscreen;
    public bool IsAutoResolution { get; private set; } = DefaultResolutionAuto;
    public int ResolutionWidth { get; private set; }
    public int ResolutionHeight { get; private set; }
    public bool VSync { get; private set; } = DefaultVSync;
    public int FPSLimit { get; private set; } = DefaultFPSLimit;

    public float MouseSensitivity { get; private set; } = DefaultMouseSensitivity;
    public int TextSize { get; private set; } = DefaultTextSize; // 0 = Small, 1 = Medium, 2 = Large

    public static float CurrentTextScale => Instance != null ? GetTextScaleFactor(Instance.TextSize) : 1.0f;

    public static float GetTextScaleFactor(int textSize)
    {
        switch (textSize)
        {
            case 0: return 0.88f; // Small
            case 2: return 1.18f; // Large
            default: return 1.0f; // Medium
        }
    }

    public event Action OnSettingsChanged;

    private struct OriginalFontMetrics
    {
        public float fontSize;
        public float fontSizeMin;
        public float fontSizeMax;
    }

    private readonly Dictionary<int, OriginalFontMetrics> baseFontMetrics = new Dictionary<int, OriginalFontMetrics>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BootstrapBeforeSceneLoad()
    {
        if (Instance == null)
        {
            var existing = FindFirstObjectByType<SettingsManager>();
            if (existing == null)
            {
                GameObject go = new GameObject("SettingsManager");
                Instance = go.AddComponent<SettingsManager>();
            }
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        EnsureAudioManagerExists();
        LoadSettings();
        ApplyAllSettings(applyResolution: true);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        EnsureAudioManagerExists();
        ApplyAllSettings(applyResolution: false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureAudioManagerExists();
        ApplyAllSettings(applyResolution: false);
    }

    private void EnsureAudioManagerExists()
    {
        if (AudioManager.Instance == null)
        {
            var existing = FindFirstObjectByType<AudioManager>();
            if (existing == null)
            {
                GameObject audioGo = new GameObject("AudioManager");
                audioGo.AddComponent<AudioManager>();
            }
        }
    }

    public void LoadSettings()
    {
        MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMasterVol, DefaultMasterVol));
        MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMusicVol, DefaultMusicVol));
        SFXVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeySFXVol, DefaultSFXVol));
        MuteAll = PlayerPrefs.GetInt(KeyMuteAll, DefaultMuteAll ? 1 : 0) == 1;

        int maxQuality = Mathf.Max(0, QualitySettings.names.Length - 1);
        int defaultQual = Mathf.Clamp(DefaultQuality, 0, maxQuality);

        // Migrate old saved Mobile/PC indices if not yet migrated
        const string KeyQualityMigrated = "EcoQuest_Settings_Quality_v2_Migrated";
        if (PlayerPrefs.GetInt(KeyQualityMigrated, 0) == 0)
        {
            if (PlayerPrefs.HasKey(KeyQuality))
            {
                int oldQuality = PlayerPrefs.GetInt(KeyQuality, 1);
                // Old 0 was Mobile -> map to 1 (Medium); Old 1 was PC -> map to 2 (High)
                int migratedQuality = (oldQuality == 0) ? 1 : 2;
                PlayerPrefs.SetInt(KeyQuality, migratedQuality);
            }
            PlayerPrefs.SetInt(KeyQualityMigrated, 1);
            PlayerPrefs.Save();
        }

        QualityIndex = Mathf.Clamp(PlayerPrefs.GetInt(KeyQuality, defaultQual), 0, maxQuality);
        IsAutoResolution = PlayerPrefs.GetInt(KeyResolutionAuto, DefaultResolutionAuto ? 1 : 0) == 1;
        Fullscreen = PlayerPrefs.GetInt(KeyFullscreen, DefaultFullscreen ? 1 : 0) == 1;

        Vector2Int nativeRes = GetCurrentDisplayNativeResolution();
        int savedW = PlayerPrefs.GetInt(KeyResolutionWidth, nativeRes.x);
        int savedH = PlayerPrefs.GetInt(KeyResolutionHeight, nativeRes.y);
        if (savedW < 800 || savedH < 600)
        {
            savedW = nativeRes.x;
            savedH = nativeRes.y;
        }
        ResolutionWidth = savedW;
        ResolutionHeight = savedH;

        VSync = PlayerPrefs.GetInt(KeyVSync, DefaultVSync ? 1 : 0) == 1;
        FPSLimit = PlayerPrefs.GetInt(KeyFPSLimit, DefaultFPSLimit);

        MouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(KeyMouseSensitivity, DefaultMouseSensitivity), 0.2f, 3.0f);

        // Migrate legacy 2-option TextSize (0 = Normal, 1 = Large) -> 3-option (0 = Small, 1 = Medium, 2 = Large)
        if (PlayerPrefs.GetInt(KeyTextSizeV2Migrated, 0) == 0)
        {
            if (PlayerPrefs.HasKey(KeyTextSize))
            {
                int legacy = PlayerPrefs.GetInt(KeyTextSize, 0);
                PlayerPrefs.SetInt(KeyTextSize, legacy >= 1 ? 2 : 1);
            }
            PlayerPrefs.SetInt(KeyTextSizeV2Migrated, 1);
            PlayerPrefs.Save();
        }
        TextSize = Mathf.Clamp(PlayerPrefs.GetInt(KeyTextSize, DefaultTextSize), 0, 2);
    }

    public void SaveAllSettings()
    {
        PlayerPrefs.SetFloat(KeyMasterVol, MasterVolume);
        PlayerPrefs.SetFloat(KeyMusicVol, MusicVolume);
        PlayerPrefs.SetFloat(KeySFXVol, SFXVolume);
        PlayerPrefs.SetInt(KeyMuteAll, MuteAll ? 1 : 0);

        PlayerPrefs.SetInt(KeyQuality, QualityIndex);
        PlayerPrefs.SetInt(KeyFullscreen, Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(KeyResolutionAuto, IsAutoResolution ? 1 : 0);
        PlayerPrefs.SetInt(KeyResolutionWidth, ResolutionWidth);
        PlayerPrefs.SetInt(KeyResolutionHeight, ResolutionHeight);
        PlayerPrefs.SetInt(KeyVSync, VSync ? 1 : 0);
        PlayerPrefs.SetInt(KeyFPSLimit, FPSLimit);

        PlayerPrefs.SetFloat(KeyMouseSensitivity, MouseSensitivity);
        PlayerPrefs.SetInt(KeyTextSize, TextSize);
        PlayerPrefs.SetInt(KeyTextSizeV2Migrated, 1);

        PlayerPrefs.Save();
    }

    public void ApplyAllSettings(bool applyResolution = true)
    {
        ApplyAudioSettings();
        ApplyGraphicsSettings(applyResolution);
        ApplyGameplaySettings();
        OnSettingsChanged?.Invoke();
    }

    // =====================================================================
    // AUDIO
    // =====================================================================

    public void SetAudioSettings(float master, float music, float sfx, bool muteAll, bool save = true)
    {
        MasterVolume = Mathf.Clamp01(master);
        MusicVolume = Mathf.Clamp01(music);
        SFXVolume = Mathf.Clamp01(sfx);
        MuteAll = muteAll;

        ApplyAudioSettings();

        if (save)
        {
            PlayerPrefs.SetFloat(KeyMasterVol, MasterVolume);
            PlayerPrefs.SetFloat(KeyMusicVol, MusicVolume);
            PlayerPrefs.SetFloat(KeySFXVol, SFXVolume);
            PlayerPrefs.SetInt(KeyMuteAll, MuteAll ? 1 : 0);
            PlayerPrefs.Save();
        }

        OnSettingsChanged?.Invoke();
    }

    public void ApplyAudioSettings()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.ApplyVolumes(MasterVolume, MusicVolume, SFXVolume, MuteAll);
        }
    }

    // =====================================================================
    // GRAPHICS
    // =====================================================================

    public void SetAndApplyGraphics(int qualityIndex, bool fullscreen, bool isAutoResolution, int resWidth, int resHeight, bool vsync, int fpsLimit, bool save = true)
    {
        int maxQuality = Mathf.Max(0, QualitySettings.names.Length - 1);
        QualityIndex = Mathf.Clamp(qualityIndex, 0, maxQuality);
        IsAutoResolution = isAutoResolution;
        Fullscreen = isAutoResolution ? true : fullscreen;
        ResolutionWidth = Mathf.Max(800, resWidth);
        ResolutionHeight = Mathf.Max(600, resHeight);
        VSync = vsync;
        FPSLimit = fpsLimit;

        ApplyGraphicsSettings(applyResolution: true);

        if (save)
        {
            PlayerPrefs.SetInt(KeyQuality, QualityIndex);
            PlayerPrefs.SetInt(KeyFullscreen, Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(KeyResolutionAuto, IsAutoResolution ? 1 : 0);
            PlayerPrefs.SetInt(KeyResolutionWidth, ResolutionWidth);
            PlayerPrefs.SetInt(KeyResolutionHeight, ResolutionHeight);
            PlayerPrefs.SetInt(KeyVSync, VSync ? 1 : 0);
            PlayerPrefs.SetInt(KeyFPSLimit, FPSLimit);
            PlayerPrefs.Save();
        }

        OnSettingsChanged?.Invoke();
    }

    public void ApplyGraphicsSettings(bool applyResolution = true)
    {
        int clampedQ = 0;
        if (QualitySettings.names != null && QualitySettings.names.Length > 0)
        {
            clampedQ = Mathf.Clamp(QualityIndex, 0, QualitySettings.names.Length - 1);
            if (QualitySettings.GetQualityLevel() != clampedQ)
            {
                QualitySettings.SetQualityLevel(clampedQ, true);
            }
        }

        ApplyPostProcessingForQuality(clampedQ);

        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Application.targetFrameRate = VSync ? -1 : FPSLimit;

        Vector2Int nativeRes = GetCurrentDisplayNativeResolution();
        int targetW = IsAutoResolution ? nativeRes.x : ResolutionWidth;
        int targetH = IsAutoResolution ? nativeRes.y : ResolutionHeight;
        if (targetW < 800 || targetH < 600)
        {
            targetW = nativeRes.x;
            targetH = nativeRes.y;
        }
        FullScreenMode mode = IsAutoResolution
            ? FullScreenMode.FullScreenWindow
            : (Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);

        if (applyResolution)
        {
            Screen.SetResolution(targetW, targetH, mode);
            string qName = (QualitySettings.names != null && clampedQ < QualitySettings.names.Length)
                ? QualitySettings.names[clampedQ]
                : clampedQ.ToString();
            Debug.Log($"[SettingsManager] Applied Graphics -> Quality: {qName} ({clampedQ}), Auto: {IsAutoResolution}, Resolution: {targetW}x{targetH}, Mode: {mode}, VSync: {VSync}, FPSLimit: {Application.targetFrameRate}");
        }
    }

    public static void ApplyPostProcessingForQuality(int qualityIndex)
    {
        // Keep Low (0) & Medium (1) free of heavy post-processing (SSAO/Bloom/CA/Tonemapping),
        // while leaving UI_Blur_Volume enabled so modal blur still works cleanly.
        bool enableScenePost = qualityIndex >= 2;
        var volumes = FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < volumes.Length; i++)
        {
            Volume vol = volumes[i];
            if (vol == null || !vol.isGlobal) continue;
            if (vol.gameObject.name.IndexOf("Blur", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            vol.enabled = enableScenePost;
        }
    }

    // =====================================================================
    // GAMEPLAY
    // =====================================================================

    public void SetGameplaySettings(float mouseSens, int textSize, bool save = true)
    {
        MouseSensitivity = Mathf.Clamp(mouseSens, 0.2f, 3.0f);
        TextSize = Mathf.Clamp(textSize, 0, 2);

        ApplyGameplaySettings();

        if (save)
        {
            PlayerPrefs.SetFloat(KeyMouseSensitivity, MouseSensitivity);
            PlayerPrefs.SetInt(KeyTextSize, TextSize);
            PlayerPrefs.SetInt(KeyTextSizeV2Migrated, 1);
            PlayerPrefs.Save();
        }

        OnSettingsChanged?.Invoke();
    }

    public void ApplyGameplaySettings()
    {
        // 1. Mouse Sensitivity -> FirstPersonController.RotationSpeed
        var fpc = FindFirstObjectByType<StarterAssets.FirstPersonController>(FindObjectsInactive.Include);
        if (fpc != null)
        {
            fpc.RotationSpeed = MouseSensitivity;
        }

        // 2. Text Size (Small / Medium / Large) -> scales Quiz and Dialogue text ONLY
        ApplyQuizAndDialogueTextSize();
    }

    public void ApplyQuizAndDialogueTextSize()
    {
        float scale = GetTextScaleFactor(TextSize);

        // Controllers that compute explicit font sizes from constants
        HashSet<int> controllerManagedIds = new HashSet<int>();
        var dp = FindFirstObjectByType<DialoguePresenter>(FindObjectsInactive.Include);
        if (dp != null)
        {
            dp.ApplyTextSizeScale(controllerManagedIds);
        }

        var quiz = FindFirstObjectByType<QuizUIController>(FindObjectsInactive.Include);
        if (quiz != null)
        {
            quiz.ApplyTextSizeScale(controllerManagedIds);
        }

        // Scale all other TMPs under Quiz / Dialogue canvases using cached unscaled baselines
        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in canvases)
        {
            if (c == null) continue;
            if (c.name == "UI_Quiz" || c.name == "UI_Dialogue" || c.name == "UI_LitterbugQuest")
            {
                var texts = c.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var t in texts)
                {
                    if (t == null) continue;
                    int id = t.GetInstanceID();
                    if (controllerManagedIds.Contains(id)) continue;

                    if (!baseFontMetrics.TryGetValue(id, out OriginalFontMetrics orig))
                    {
                        orig = new OriginalFontMetrics
                        {
                            fontSize = t.fontSize,
                            fontSizeMin = t.fontSizeMin,
                            fontSizeMax = t.fontSizeMax
                        };
                        baseFontMetrics[id] = orig;
                    }

                    t.fontSize = Mathf.Round(orig.fontSize * scale * 10f) / 10f;
                    t.fontSizeMin = Mathf.Round(orig.fontSizeMin * scale * 10f) / 10f;
                    t.fontSizeMax = Mathf.Round(orig.fontSizeMax * scale * 10f) / 10f;
                }
            }
        }
    }

    public void ResetToDefaults()
    {
        Vector2Int nativeRes = GetCurrentDisplayNativeResolution();

        MasterVolume = DefaultMasterVol;
        MusicVolume = DefaultMusicVol;
        SFXVolume = DefaultSFXVol;
        MuteAll = DefaultMuteAll;

        int maxQuality = Mathf.Max(0, QualitySettings.names.Length - 1);
        QualityIndex = Mathf.Clamp(DefaultQuality, 0, maxQuality);
        IsAutoResolution = DefaultResolutionAuto;
        Fullscreen = DefaultFullscreen;
        ResolutionWidth = nativeRes.x;
        ResolutionHeight = nativeRes.y;
        VSync = DefaultVSync;
        FPSLimit = DefaultFPSLimit;

        MouseSensitivity = DefaultMouseSensitivity;
        TextSize = DefaultTextSize;

        SaveAllSettings();
        ApplyAllSettings(applyResolution: true);
    }

    /// <summary>
    /// Returns the native resolution (width x height) of the display the window is currently on.
    /// </summary>
    public static Vector2Int GetCurrentDisplayNativeResolution()
    {
        DisplayInfo di = Screen.mainWindowDisplayInfo;
        if (di.width >= 800 && di.height >= 600)
        {
            return new Vector2Int(di.width, di.height);
        }

        if (Display.main != null && Display.main.systemWidth >= 800 && Display.main.systemHeight >= 600)
        {
            return new Vector2Int(Display.main.systemWidth, Display.main.systemHeight);
        }

        return new Vector2Int(
            Mathf.Max(800, Screen.currentResolution.width),
            Mathf.Max(600, Screen.currentResolution.height));
    }

    /// <summary>
    /// Pure helper: filters resolutions to those >= 800x600 and within the current monitor's native bounds,
    /// deduplicates (width x height), and sorts ascending. Never injects hardcoded fallback resolutions.
    /// </summary>
    public static List<Vector2Int> BuildFilteredUniqueResolutions(Resolution[] rawResolutions, int nativeWidth, int nativeHeight)
    {
        List<Vector2Int> list = new List<Vector2Int>();
        HashSet<Vector2Int> seen = new HashSet<Vector2Int>();

        if (rawResolutions != null)
        {
            for (int i = 0; i < rawResolutions.Length; i++)
            {
                int w = rawResolutions[i].width;
                int h = rawResolutions[i].height;
                if (w < 800 || h < 600) continue;
                if (nativeWidth > 0 && nativeHeight > 0 && (w > nativeWidth || h > nativeHeight)) continue;

                Vector2Int candidate = new Vector2Int(w, h);
                if (seen.Add(candidate))
                {
                    list.Add(candidate);
                }
            }
        }

        if (list.Count == 0 && nativeWidth >= 800 && nativeHeight >= 600)
        {
            list.Add(new Vector2Int(nativeWidth, nativeHeight));
        }

        list.Sort((a, b) =>
        {
            int cmp = (a.x * a.y).CompareTo(b.x * b.y);
            return cmp != 0 ? cmp : a.x.CompareTo(b.x);
        });

        return list;
    }

    /// <summary>
    /// Returns deduplicated, valid screen resolutions (width x height) supported by the current monitor, sorted ascending.
    /// </summary>
    public static List<Vector2Int> GetUniqueValidResolutions()
    {
        Vector2Int nativeRes = GetCurrentDisplayNativeResolution();
        return BuildFilteredUniqueResolutions(Screen.resolutions, nativeRes.x, nativeRes.y);
    }
}
