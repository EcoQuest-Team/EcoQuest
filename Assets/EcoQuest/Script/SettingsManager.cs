using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
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
    public const string KeyVSync = "EcoQuest_Settings_VSync";
    public const string KeyFPSLimit = "EcoQuest_Settings_FPSLimit";
    public const string KeyMouseSensitivity = "EcoQuest_Settings_MouseSensitivity";
    public const string KeyTutorialHints = "EcoQuest_Settings_TutorialHints";
    public const string KeyTextSize = "EcoQuest_Settings_TextSize";

    // Default Values
    public const float DefaultMasterVol = 1.0f;
    public const float DefaultMusicVol = 0.8f;
    public const float DefaultSFXVol = 0.8f;
    public const bool DefaultMuteAll = false;
    public const int DefaultQuality = 2; // High (0 = Low, 1 = Medium, 2 = High, 3 = Ultra)
    public const bool DefaultFullscreen = true;
    public const int DefaultResolutionWidth = 1920;
    public const int DefaultResolutionHeight = 1080;
    public const bool DefaultVSync = true;
    public const int DefaultFPSLimit = -1; // Unlimited
    public const float DefaultMouseSensitivity = 1.0f;
    public const bool DefaultTutorialHints = true;
    public const int DefaultTextSize = 0; // 0 = Normal, 1 = Large

    // Current Settings State
    public float MasterVolume { get; private set; } = DefaultMasterVol;
    public float MusicVolume { get; private set; } = DefaultMusicVol;
    public float SFXVolume { get; private set; } = DefaultSFXVol;
    public bool MuteAll { get; private set; } = DefaultMuteAll;

    public int QualityIndex { get; private set; } = DefaultQuality;
    public bool Fullscreen { get; private set; } = DefaultFullscreen;
    public int ResolutionWidth { get; private set; } = DefaultResolutionWidth;
    public int ResolutionHeight { get; private set; } = DefaultResolutionHeight;
    public bool VSync { get; private set; } = DefaultVSync;
    public int FPSLimit { get; private set; } = DefaultFPSLimit;

    public float MouseSensitivity { get; private set; } = DefaultMouseSensitivity;
    public bool TutorialHints { get; private set; } = DefaultTutorialHints;
    public int TextSize { get; private set; } = DefaultTextSize; // 0 = Normal, 1 = Large

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
        ApplyAllSettings();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        EnsureAudioManagerExists();
        ApplyAllSettings();
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
        ApplyAllSettings();
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
        Fullscreen = PlayerPrefs.GetInt(KeyFullscreen, DefaultFullscreen ? 1 : 0) == 1;

        int fallbackW = Screen.currentResolution.width >= 800 ? Screen.currentResolution.width : DefaultResolutionWidth;
        int fallbackH = Screen.currentResolution.height >= 600 ? Screen.currentResolution.height : DefaultResolutionHeight;
        ResolutionWidth = PlayerPrefs.GetInt(KeyResolutionWidth, fallbackW);
        ResolutionHeight = PlayerPrefs.GetInt(KeyResolutionHeight, fallbackH);

        VSync = PlayerPrefs.GetInt(KeyVSync, DefaultVSync ? 1 : 0) == 1;
        FPSLimit = PlayerPrefs.GetInt(KeyFPSLimit, DefaultFPSLimit);

        MouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(KeyMouseSensitivity, DefaultMouseSensitivity), 0.2f, 3.0f);
        TutorialHints = PlayerPrefs.GetInt(KeyTutorialHints, DefaultTutorialHints ? 1 : 0) == 1;
        TextSize = Mathf.Clamp(PlayerPrefs.GetInt(KeyTextSize, DefaultTextSize), 0, 1);
    }

    public void SaveAllSettings()
    {
        PlayerPrefs.SetFloat(KeyMasterVol, MasterVolume);
        PlayerPrefs.SetFloat(KeyMusicVol, MusicVolume);
        PlayerPrefs.SetFloat(KeySFXVol, SFXVolume);
        PlayerPrefs.SetInt(KeyMuteAll, MuteAll ? 1 : 0);

        PlayerPrefs.SetInt(KeyQuality, QualityIndex);
        PlayerPrefs.SetInt(KeyFullscreen, Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(KeyResolutionWidth, ResolutionWidth);
        PlayerPrefs.SetInt(KeyResolutionHeight, ResolutionHeight);
        PlayerPrefs.SetInt(KeyVSync, VSync ? 1 : 0);
        PlayerPrefs.SetInt(KeyFPSLimit, FPSLimit);

        PlayerPrefs.SetFloat(KeyMouseSensitivity, MouseSensitivity);
        PlayerPrefs.SetInt(KeyTutorialHints, TutorialHints ? 1 : 0);
        PlayerPrefs.SetInt(KeyTextSize, TextSize);

        PlayerPrefs.Save();
    }

    public void ApplyAllSettings()
    {
        ApplyAudioSettings();
        ApplyGraphicsSettings();
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

    public void SetAndApplyGraphics(int qualityIndex, bool fullscreen, int resWidth, int resHeight, bool vsync, int fpsLimit, bool save = true)
    {
        int maxQuality = Mathf.Max(0, QualitySettings.names.Length - 1);
        QualityIndex = Mathf.Clamp(qualityIndex, 0, maxQuality);
        Fullscreen = fullscreen;
        ResolutionWidth = Mathf.Max(640, resWidth);
        ResolutionHeight = Mathf.Max(480, resHeight);
        VSync = vsync;
        FPSLimit = fpsLimit;

        ApplyGraphicsSettings();

        if (save)
        {
            PlayerPrefs.SetInt(KeyQuality, QualityIndex);
            PlayerPrefs.SetInt(KeyFullscreen, Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(KeyResolutionWidth, ResolutionWidth);
            PlayerPrefs.SetInt(KeyResolutionHeight, ResolutionHeight);
            PlayerPrefs.SetInt(KeyVSync, VSync ? 1 : 0);
            PlayerPrefs.SetInt(KeyFPSLimit, FPSLimit);
            PlayerPrefs.Save();
        }

        OnSettingsChanged?.Invoke();
    }

    public void ApplyGraphicsSettings()
    {
        if (QualitySettings.names != null && QualitySettings.names.Length > 0)
        {
            int clampedQ = Mathf.Clamp(QualityIndex, 0, QualitySettings.names.Length - 1);
            if (QualitySettings.GetQualityLevel() != clampedQ)
            {
                QualitySettings.SetQualityLevel(clampedQ, true);
            }
        }

        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Application.targetFrameRate = FPSLimit;

        FullScreenMode mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        Screen.SetResolution(ResolutionWidth, ResolutionHeight, mode);
    }

    // =====================================================================
    // GAMEPLAY
    // =====================================================================

    public void SetGameplaySettings(float mouseSens, bool tutorialHints, int textSize, bool save = true)
    {
        MouseSensitivity = Mathf.Clamp(mouseSens, 0.2f, 3.0f);
        TutorialHints = tutorialHints;
        TextSize = Mathf.Clamp(textSize, 0, 1);

        ApplyGameplaySettings();

        if (save)
        {
            PlayerPrefs.SetFloat(KeyMouseSensitivity, MouseSensitivity);
            PlayerPrefs.SetInt(KeyTutorialHints, TutorialHints ? 1 : 0);
            PlayerPrefs.SetInt(KeyTextSize, TextSize);
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

        // 2. Tutorial Hints -> SequentialWASDTutorial & GameplayControlTipsManager
        if (SequentialWASDTutorial.Instance != null)
        {
            SequentialWASDTutorial.Instance.ApplyTutorialHintsSetting(TutorialHints);
        }
        if (GameplayControlTipsManager.Instance != null)
        {
            GameplayControlTipsManager.Instance.ApplyTutorialHintsSetting(TutorialHints);
        }

        // 3. Text Size (Normal = 1.0x, Large = 1.15x) -> scales Quiz and Dialogue text ONLY
        ApplyQuizAndDialogueTextSize();
    }

    public void ApplyQuizAndDialogueTextSize()
    {
        float scale = (TextSize == 1) ? 1.15f : 1.0f;

        // Find UI_Quiz and UI_Dialogue roots (including inactive)
        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in canvases)
        {
            if (c == null) continue;
            if (c.name == "UI_Quiz" || c.name == "UI_Dialogue")
            {
                var texts = c.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var t in texts)
                {
                    if (t == null) continue;
                    int id = t.GetInstanceID();
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
        int fallbackW = DefaultResolutionWidth;
        int fallbackH = DefaultResolutionHeight;
        var validRes = GetUniqueValidResolutions();
        if (validRes.Count > 0)
        {
            var last = validRes[validRes.Count - 1];
            fallbackW = last.x;
            fallbackH = last.y;
        }

        MasterVolume = DefaultMasterVol;
        MusicVolume = DefaultMusicVol;
        SFXVolume = DefaultSFXVol;
        MuteAll = DefaultMuteAll;

        int maxQuality = Mathf.Max(0, QualitySettings.names.Length - 1);
        QualityIndex = Mathf.Clamp(DefaultQuality, 0, maxQuality);
        Fullscreen = DefaultFullscreen;
        ResolutionWidth = fallbackW;
        ResolutionHeight = fallbackH;
        VSync = DefaultVSync;
        FPSLimit = DefaultFPSLimit;

        MouseSensitivity = DefaultMouseSensitivity;
        TutorialHints = DefaultTutorialHints;
        TextSize = DefaultTextSize;

        SaveAllSettings();
        ApplyAllSettings();
    }

    /// <summary>
    /// Returns deduplicated, valid screen resolutions (width x height) sorted ascending.
    /// </summary>
    public static List<Vector2Int> GetUniqueValidResolutions()
    {
        List<Vector2Int> list = new List<Vector2Int>();
        HashSet<string> seen = new HashSet<string>();

        Resolution[] raw = Screen.resolutions;
        if (raw != null)
        {
            for (int i = 0; i < raw.Length; i++)
            {
                int w = raw[i].width;
                int h = raw[i].height;
                if (w < 800 || h < 600) continue;
                string key = $"{w}x{h}";
                if (seen.Add(key))
                {
                    list.Add(new Vector2Int(w, h));
                }
            }
        }

        // Ensure standard 16:9 resolutions exist if running in headless/editor window with only 1 resolution
        Vector2Int[] standards = new[]
        {
            new Vector2Int(1280, 720),
            new Vector2Int(1600, 900),
            new Vector2Int(1920, 1080)
        };
        foreach (var st in standards)
        {
            string key = $"{st.x}x{st.y}";
            if (seen.Add(key))
            {
                list.Add(st);
            }
        }

        list.Sort((a, b) =>
        {
            int cmp = (a.x * a.y).CompareTo(b.x * b.y);
            return cmp != 0 ? cmp : a.x.CompareTo(b.x);
        });

        return list;
    }
}
