using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent AudioManager singleton (DontDestroyOnLoad) managing MainMixer (Master, Music, SFX),
/// decibel volume conversion, music playback, and SFX / UI sound hooks. Handles null clips safely.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    public const string ParamMasterVol = "MasterVol";
    public const string ParamMusicVol = "MusicVol";
    public const string ParamSFXVol = "SFXVol";

    [Header("Audio Mixer & Groups")]
    public AudioMixer mainMixer;
    public AudioMixerGroup masterGroup;
    public AudioMixerGroup musicGroup;
    public AudioMixerGroup sfxGroup;

    [Header("Audio Sources")]
    public AudioSource musicSource;
    public AudioSource sfxSource;

    [Header("Optional Audio Clips (Silent Until Assigned)")]
    public AudioClip defaultMusicClip;
    public AudioClip buttonClickClip;
    public AudioClip buttonHoverClip;
    public AudioClip sliderChangeClip;

    [Header("Gameplay Audio Clips (CC0 Slots - Silent Until Assigned)")]
    public AudioClip sfxTrashPickup;
    public AudioClip sfxTrashDepositCorrect;
    public AudioClip sfxTrashDepositWrong;
    public AudioClip sfxCoinEarn;
    public AudioClip sfxQuestComplete;

    private float lastSliderSoundTime = -10f;

    public float CurrentMasterDb { get; private set; }
    public float CurrentMusicDb { get; private set; }
    public float CurrentSFXDb { get; private set; }

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

        EnsureMixerAndSources();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
    }

    private void Start()
    {
        EnsureMixerAndSources();
        RouteSceneAudioSources();

        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.ApplyAudioSettings();
        }

        if (defaultMusicClip != null)
        {
            PlayMusic(defaultMusicClip);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureMixerAndSources();
        RouteSceneAudioSources();
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.ApplyAudioSettings();
        }
    }

    public void EnsureMixerAndSources()
    {
#if UNITY_EDITOR
        if (mainMixer == null)
        {
            mainMixer = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/EcoQuest/Audio/MainMixer.mixer");
        }
#endif
        if (mainMixer != null)
        {
            if (masterGroup == null)
            {
                var g = mainMixer.FindMatchingGroups("Master");
                if (g != null && g.Length > 0) masterGroup = g[0];
            }
            if (musicGroup == null)
            {
                var g = mainMixer.FindMatchingGroups("Music");
                if (g != null && g.Length > 0) musicGroup = g[0];
            }
            if (sfxGroup == null)
            {
                var g = mainMixer.FindMatchingGroups("SFX");
                if (g != null && g.Length > 0) sfxGroup = g[0];
            }
        }

        if (musicSource == null)
        {
            Transform t = transform.Find("MusicSource");
            GameObject go = t != null ? t.gameObject : new GameObject("MusicSource");
            go.transform.SetParent(transform, false);
            musicSource = go.GetComponent<AudioSource>();
            if (musicSource == null) musicSource = go.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
        }
        if (musicGroup != null && musicSource.outputAudioMixerGroup != musicGroup)
        {
            musicSource.outputAudioMixerGroup = musicGroup;
        }

        if (sfxSource == null)
        {
            Transform t = transform.Find("SFXSource");
            GameObject go = t != null ? t.gameObject : new GameObject("SFXSource");
            go.transform.SetParent(transform, false);
            sfxSource = go.GetComponent<AudioSource>();
            if (sfxSource == null) sfxSource = go.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
        }
        if (sfxGroup != null && sfxSource.outputAudioMixerGroup != sfxGroup)
        {
            sfxSource.outputAudioMixerGroup = sfxGroup;
        }
    }

    /// <summary>
    /// Routes all existing hook AudioSources (StartSoundHook, WhooshSoundHook, ButtonHoverFX, FirstPersonController)
    /// to the SFX mixer group if not already assigned.
    /// </summary>
    public void RouteSceneAudioSources()
    {
        if (sfxGroup == null) return;

        var allSources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var src in allSources)
        {
            if (src == null || src == musicSource) continue;
            if (src.outputAudioMixerGroup == null)
            {
                src.outputAudioMixerGroup = sfxGroup;
            }
        }
    }

    /// <summary>
    /// Converts normalized linear slider value (0..1) to decibels using Mathf.Log10(Mathf.Max(v, 0.0001f)) * 20.
    /// </summary>
    public static float LinearToDecibel(float v)
    {
        return Mathf.Log10(Mathf.Max(v, 0.0001f)) * 20f;
    }

    public void ApplyVolumes(float masterLinear, float musicLinear, float sfxLinear, bool muteAll)
    {
        EnsureMixerAndSources();

        float effectiveMaster = muteAll ? 0f : Mathf.Clamp01(masterLinear);
        float effectiveMusic = Mathf.Clamp01(musicLinear);
        float effectiveSfx = Mathf.Clamp01(sfxLinear);

        CurrentMasterDb = LinearToDecibel(effectiveMaster);
        CurrentMusicDb = LinearToDecibel(effectiveMusic);
        CurrentSFXDb = LinearToDecibel(effectiveSfx);

        if (mainMixer != null)
        {
            mainMixer.SetFloat(ParamMasterVol, CurrentMasterDb);
            mainMixer.SetFloat(ParamMusicVol, CurrentMusicDb);
            mainMixer.SetFloat(ParamSFXVol, CurrentSFXDb);
        }

        AudioListener.volume = effectiveMaster;
        if (musicSource != null)
        {
            musicSource.mute = muteAll;
            musicSource.volume = effectiveMusic;
        }
        if (sfxSource != null)
        {
            sfxSource.mute = muteAll;
            sfxSource.volume = effectiveSfx;
        }
    }

    public void PlayMusic(AudioClip clip)
    {
        if (clip == null) return;
        EnsureMixerAndSources();
        if (musicSource == null) return;

        if (musicSource.clip == clip && musicSource.isPlaying) return;
        musicSource.clip = clip;
        musicSource.Play();
    }

    public void PlaySFX(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null) return;
        EnsureMixerAndSources();
        if (sfxSource == null) return;

        sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    public void PlayButtonClick()
    {
        if (buttonClickClip != null)
        {
            PlaySFX(buttonClickClip);
        }
    }

    public void PlayButtonHover()
    {
        if (buttonHoverClip != null)
        {
            PlaySFX(buttonHoverClip);
        }
    }

    public void PlaySliderChange()
    {
        if (sliderChangeClip == null) return;
        float now = Time.unscaledTime;
        if (now - lastSliderSoundTime >= 0.06f)
        {
            lastSliderSoundTime = now;
            PlaySFX(sliderChangeClip, 0.75f);
        }
    }

    public void PlayTrashPickup() => PlaySFX(sfxTrashPickup);
    public void PlayTrashDepositCorrect() => PlaySFX(sfxTrashDepositCorrect);
    public void PlayTrashDepositWrong() => PlaySFX(sfxTrashDepositWrong);
    public void PlayCoinEarn() => PlaySFX(sfxCoinEarn);
    public void PlayQuestComplete() => PlaySFX(sfxQuestComplete);
}
