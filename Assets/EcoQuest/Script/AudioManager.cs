using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent AudioManager singleton (DontDestroyOnLoad) managing MainMixer
/// (Master, Music, SFX, UI, Ambience), decibel volume conversion, pooled 2D SFX/UI playback,
/// scene music & ambience crossfading, and build-safe Resource template fallback.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    public const string ParamMasterVol = "MasterVol";
    public const string ParamMusicVol = "MusicVol";
    public const string ParamSFXVol = "SFXVol";
    public const string ResourcesPrefabName = "AudioManager";

    private const int PoolSize = 4;
    private const float AmbienceBaseVolume = 0.45f;

    [Header("Audio Mixer, Groups & Shared Theme")]
    public AudioMixer mainMixer;
    public AudioMixerGroup masterGroup;
    public AudioMixerGroup musicGroup;
    public AudioMixerGroup sfxGroup;
    public AudioMixerGroup uiGroup;
    public AudioMixerGroup ambienceGroup;
    public UITheme uiThemeAsset;

    [Header("Audio Sources")]
    public AudioSource musicSource;
    public AudioSource sfxSource;
    public AudioSource ambienceSource;

    [Header("Scene Music & Ambience Loops")]
    public AudioClip defaultMusicClip;
    public AudioClip bgmMainMenu;
    public AudioClip bgmForest;
    public AudioClip bgmCity;
    public AudioClip ambForest;
    public AudioClip ambCity;

    [Header("UI Audio Clips")]
    public AudioClip buttonClickClip;
    public AudioClip buttonHoverClip;
    public AudioClip sliderChangeClip;
    public AudioClip uiModalOpenClip;
    public AudioClip uiModalCloseClip;
    public AudioClip uiPanelWhooshClip;
    public AudioClip uiDialogueBlipClip;
    public AudioClip uiNotificationClip;
    public AudioClip uiTutorialStepClip;
    public AudioClip uiHungerWarningClip;
    public AudioClip uiShopBuyClip;
    public AudioClip uiShopErrorClip;
    public AudioClip uiItemUseClip;

    [Header("Gameplay Audio Clips")]
    public AudioClip sfxTrashPickup;
    public AudioClip sfxTrashDrop;
    public AudioClip sfxTrashDepositCorrect;
    public AudioClip sfxTrashDepositWrong;
    public AudioClip sfxQuizCorrect;
    public AudioClip sfxQuizWrong;
    public AudioClip sfxCoinEarn;
    public AudioClip sfxQuestComplete;
    public AudioClip sfxJump;
    public AudioClip sfxLand;
    public AudioClip sfxLoadingReady;
    public AudioClip sfxLitterThrow;
    public AudioClip sfxLitterLand;

    [Header("Footstep Clips")]
    public AudioClip[] footstepGrassClips;
    public AudioClip[] footstepCityClips;

    private readonly Dictionary<string, float> cooldownTimestamps = new Dictionary<string, float>();
    private AudioSource[] sfxPool;
    private AudioSource[] uiPool;
    private int sfxPoolIndex;
    private int uiPoolIndex;
    private bool templateLoaded;
    private float lastRouteScanTime = -10f;
    private Coroutine musicFadeCoroutine;

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
        SyncVolumesFromSettingsOrPrefs();
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
        SyncVolumesFromSettingsOrPrefs();
        PlayAudioForScene(SceneManager.GetActiveScene().name);
        StartCoroutine(ReapplyVolumesNextFrame());
    }

    private IEnumerator ReapplyVolumesNextFrame()
    {
        yield return null;
        SyncVolumesFromSettingsOrPrefs();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureMixerAndSources();
        RouteSceneAudioSources();
        SyncVolumesFromSettingsOrPrefs();
        PlayAudioForScene(scene.name);
        StartCoroutine(ReapplyVolumesNextFrame());
    }

    private void SyncVolumesFromSettingsOrPrefs()
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.ApplyAudioSettings();
            return;
        }

        float m = Mathf.Clamp01(PlayerPrefs.GetFloat(SettingsManager.KeyMasterVol, SettingsManager.DefaultMasterVol));
        float mu = Mathf.Clamp01(PlayerPrefs.GetFloat(SettingsManager.KeyMusicVol, SettingsManager.DefaultMusicVol));
        float sf = Mathf.Clamp01(PlayerPrefs.GetFloat(SettingsManager.KeySFXVol, SettingsManager.DefaultSFXVol));
        bool mute = PlayerPrefs.GetInt(SettingsManager.KeyMuteAll, SettingsManager.DefaultMuteAll ? 1 : 0) == 1;
        ApplyVolumes(m, mu, sf, mute);
    }

    private static AudioMixer s_registeredMixerAsset;

    public static void RegisterMixerAsset(AudioMixer mixer)
    {
        if (mixer == null) return;
        s_registeredMixerAsset = mixer;
        if (Instance != null && Instance.mainMixer == null)
        {
            Instance.mainMixer = mixer;
            Instance.EnsureMixerAndSources();
            Instance.SyncVolumesFromSettingsOrPrefs();
        }
    }

    public AudioMixerGroup GetSFXGroup()
    {
        EnsureMixerAndSources();
        return sfxGroup;
    }

    public AudioMixerGroup GetUIGroup()
    {
        EnsureMixerAndSources();
        return uiGroup != null ? uiGroup : sfxGroup;
    }

    public AudioMixerGroup GetAmbienceGroup()
    {
        EnsureMixerAndSources();
        return ambienceGroup != null ? ambienceGroup : sfxGroup;
    }

    public AudioMixerGroup GetMusicGroup()
    {
        EnsureMixerAndSources();
        return musicGroup;
    }

    public void EnsureMixerAndSources()
    {
        if (!templateLoaded)
        {
            templateLoaded = true;
            GameObject prefab = Resources.Load<GameObject>(ResourcesPrefabName);
            AudioManager template = prefab != null ? prefab.GetComponent<AudioManager>() : null;
            if (template != null && template != this)
            {
                FillNullRefsFrom(template);
            }
        }

        if (mainMixer == null && s_registeredMixerAsset != null)
        {
            mainMixer = s_registeredMixerAsset;
        }
        if (mainMixer == null)
        {
            mainMixer = Resources.Load<AudioMixer>("MainMixer");
        }
#if UNITY_EDITOR
        if (mainMixer == null)
        {
            mainMixer = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/EcoQuest/Audio/MainMixer.mixer");
        }
        if (uiThemeAsset == null)
        {
            uiThemeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UITheme>("Assets/EcoQuest/UI/Shared/UITheme.asset");
        }
#endif
        if (mainMixer != null)
        {
            if (masterGroup == null) masterGroup = FindGroup("Master");
            if (musicGroup == null) musicGroup = FindGroup("Music");
            if (sfxGroup == null) sfxGroup = FindGroup("SFX");
            if (uiGroup == null)
            {
                uiGroup = FindGroup("UI");
                if (uiGroup == null) uiGroup = sfxGroup;
            }
            if (ambienceGroup == null)
            {
                ambienceGroup = FindGroup("Ambience");
                if (ambienceGroup == null) ambienceGroup = sfxGroup;
            }
        }

        musicSource = EnsureChildSource("MusicSource", true, musicGroup, false);
        ambienceSource = EnsureChildSource("AmbienceSource", true, ambienceGroup != null ? ambienceGroup : sfxGroup, false);
        if (ambienceSource != null) ambienceSource.volume = AmbienceBaseVolume;

        if (sfxSource == null)
        {
            sfxSource = EnsureChildSource("SFXSource", false, sfxGroup, true);
        }
        else if (sfxGroup != null && sfxSource.outputAudioMixerGroup != sfxGroup)
        {
            sfxSource.outputAudioMixerGroup = sfxGroup;
        }

        if (sfxPool == null || sfxPool.Length != PoolSize)
        {
            sfxPool = new AudioSource[PoolSize];
            sfxPool[0] = sfxSource;
            for (int i = 1; i < PoolSize; i++)
            {
                sfxPool[i] = EnsureChildSource($"SFXPool_{i}", false, sfxGroup, true);
            }
        }

        if (uiPool == null || uiPool.Length != PoolSize)
        {
            uiPool = new AudioSource[PoolSize];
            AudioMixerGroup targetUiGroup = uiGroup != null ? uiGroup : sfxGroup;
            for (int i = 0; i < PoolSize; i++)
            {
                uiPool[i] = EnsureChildSource($"UIPool_{i}", false, targetUiGroup, true);
            }
        }
    }

    private AudioMixerGroup FindGroup(string groupName)
    {
        if (mainMixer == null) return null;
        var g = mainMixer.FindMatchingGroups(groupName);
        return (g != null && g.Length > 0) ? g[0] : null;
    }

    private AudioSource EnsureChildSource(string childName, bool loop, AudioMixerGroup group, bool ignorePause)
    {
        Transform t = transform.Find(childName);
        GameObject go = t != null ? t.gameObject : new GameObject(childName);
        go.transform.SetParent(transform, false);
        AudioSource src = go.GetComponent<AudioSource>();
        if (src == null) src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 0f;
        src.ignoreListenerPause = ignorePause;
        if (group != null && src.outputAudioMixerGroup != group)
        {
            src.outputAudioMixerGroup = group;
        }
        return src;
    }

    private static void Pick<T>(ref T target, T fallback) where T : UnityEngine.Object
    {
        if (target == null && fallback != null) target = fallback;
    }

    private void FillNullRefsFrom(AudioManager src)
    {
        Pick(ref mainMixer, src.mainMixer);
        Pick(ref masterGroup, src.masterGroup);
        Pick(ref musicGroup, src.musicGroup);
        Pick(ref sfxGroup, src.sfxGroup);
        Pick(ref uiGroup, src.uiGroup);
        Pick(ref ambienceGroup, src.ambienceGroup);
        Pick(ref uiThemeAsset, src.uiThemeAsset);

        Pick(ref defaultMusicClip, src.defaultMusicClip);
        Pick(ref bgmMainMenu, src.bgmMainMenu);
        Pick(ref bgmForest, src.bgmForest);
        Pick(ref bgmCity, src.bgmCity);
        Pick(ref ambForest, src.ambForest);
        Pick(ref ambCity, src.ambCity);

        Pick(ref buttonClickClip, src.buttonClickClip);
        Pick(ref buttonHoverClip, src.buttonHoverClip);
        Pick(ref sliderChangeClip, src.sliderChangeClip);
        Pick(ref uiModalOpenClip, src.uiModalOpenClip);
        Pick(ref uiModalCloseClip, src.uiModalCloseClip);
        Pick(ref uiPanelWhooshClip, src.uiPanelWhooshClip);
        Pick(ref uiDialogueBlipClip, src.uiDialogueBlipClip);
        Pick(ref uiNotificationClip, src.uiNotificationClip);
        Pick(ref uiTutorialStepClip, src.uiTutorialStepClip);
        Pick(ref uiHungerWarningClip, src.uiHungerWarningClip);
        Pick(ref uiShopBuyClip, src.uiShopBuyClip);
        Pick(ref uiShopErrorClip, src.uiShopErrorClip);
        Pick(ref uiItemUseClip, src.uiItemUseClip);

        Pick(ref sfxTrashPickup, src.sfxTrashPickup);
        Pick(ref sfxTrashDrop, src.sfxTrashDrop);
        Pick(ref sfxTrashDepositCorrect, src.sfxTrashDepositCorrect);
        Pick(ref sfxTrashDepositWrong, src.sfxTrashDepositWrong);
        Pick(ref sfxQuizCorrect, src.sfxQuizCorrect);
        Pick(ref sfxQuizWrong, src.sfxQuizWrong);
        Pick(ref sfxCoinEarn, src.sfxCoinEarn);
        Pick(ref sfxQuestComplete, src.sfxQuestComplete);
        Pick(ref sfxJump, src.sfxJump);
        Pick(ref sfxLand, src.sfxLand);
        Pick(ref sfxLoadingReady, src.sfxLoadingReady);
        Pick(ref sfxLitterThrow, src.sfxLitterThrow);
        Pick(ref sfxLitterLand, src.sfxLitterLand);

        if ((footstepGrassClips == null || footstepGrassClips.Length == 0) && src.footstepGrassClips != null && src.footstepGrassClips.Length > 0)
        {
            footstepGrassClips = (AudioClip[])src.footstepGrassClips.Clone();
        }
        if ((footstepCityClips == null || footstepCityClips.Length == 0) && src.footstepCityClips != null && src.footstepCityClips.Length > 0)
        {
            footstepCityClips = (AudioClip[])src.footstepCityClips.Clone();
        }
    }

    /// <summary>
    /// Routes all unrouted AudioSources in loaded scenes to the SFX mixer group.
    /// </summary>
    public void RouteSceneAudioSources()
    {
        if (sfxGroup == null) return;

        var allSources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var src in allSources)
        {
            if (src == null || src == musicSource || src == ambienceSource) continue;
            if (src.outputAudioMixerGroup == null)
            {
                src.outputAudioMixerGroup = sfxGroup;
            }
        }
    }

    /// <summary>
    /// Converts normalized linear slider value (0..1) to decibels using Mathf.Log10(Mathf.Clamp(v, 0.0001f, 1f)) * 20.
    /// </summary>
    public static float LinearToDecibel(float v)
    {
        return Mathf.Log10(Mathf.Clamp(v, 0.0001f, 1f)) * 20f;
    }

    public void ApplyVolumes(float masterLinear, float musicLinear, float sfxLinear, bool muteAll)
    {
        EnsureMixerAndSources();
        if (Time.unscaledTime - lastRouteScanTime >= 1.0f)
        {
            lastRouteScanTime = Time.unscaledTime;
            RouteSceneAudioSources();
        }

        float effectiveMaster = muteAll ? 0f : Mathf.Clamp01(masterLinear);
        float effectiveMusic = Mathf.Clamp01(musicLinear);
        float effectiveSfx = Mathf.Clamp01(sfxLinear);

        CurrentMasterDb = LinearToDecibel(effectiveMaster);
        CurrentMusicDb = LinearToDecibel(effectiveMusic);
        CurrentSFXDb = LinearToDecibel(effectiveSfx);

        bool hasMixer = mainMixer != null;
        if (hasMixer)
        {
            mainMixer.SetFloat(ParamMasterVol, CurrentMasterDb);
            mainMixer.SetFloat(ParamMusicVol, CurrentMusicDb);
            mainMixer.SetFloat(ParamSFXVol, CurrentSFXDb);
            // Mixer drives attenuation cleanly (avoid double attenuation); listener enforces MuteAll globally.
            AudioListener.volume = muteAll ? 0f : 1f;
        }
        else
        {
            AudioListener.volume = effectiveMaster;
        }

        if (musicSource != null)
        {
            musicSource.mute = muteAll;
            if (!hasMixer) musicSource.volume = effectiveMusic;
        }
        if (ambienceSource != null)
        {
            ambienceSource.mute = muteAll;
            if (!hasMixer) ambienceSource.volume = AmbienceBaseVolume * effectiveSfx;
        }

        ApplyPoolVolumes(sfxPool, muteAll, hasMixer ? 1f : effectiveSfx);
        ApplyPoolVolumes(uiPool, muteAll, hasMixer ? 1f : effectiveSfx);
    }

    private static void ApplyPoolVolumes(AudioSource[] pool, bool muteAll, float linearVol)
    {
        if (pool == null) return;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] != null)
            {
                pool[i].mute = muteAll;
                pool[i].volume = linearVol;
            }
        }
    }

    public void PlayAudioForScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;

        // Beach scene intentionally excluded per scope; do not force Forest/City tracks onto it.
        if (sceneName.IndexOf("Beach", System.StringComparison.OrdinalIgnoreCase) >= 0) return;

        AudioClip targetMusic = null;
        AudioClip targetAmbience = null;

        if (sceneName.IndexOf("Main Menu", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            targetMusic = bgmMainMenu != null ? bgmMainMenu : defaultMusicClip;
        }
        else if (sceneName.IndexOf("Forest", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            targetMusic = bgmForest != null ? bgmForest : defaultMusicClip;
            targetAmbience = ambForest;
        }
        else if (sceneName.IndexOf("City", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            targetMusic = bgmCity != null ? bgmCity : defaultMusicClip;
            targetAmbience = ambCity;
        }
        else if (defaultMusicClip != null)
        {
            targetMusic = defaultMusicClip;
        }

        if (targetMusic != null) PlayMusic(targetMusic);
        PlayAmbience(targetAmbience, AmbienceBaseVolume);
    }

    public void PlayMusic(AudioClip clip)
    {
        if (clip == null) return;
        EnsureMixerAndSources();
        if (musicSource == null) return;

        float targetVol = (mainMixer != null || SettingsManager.Instance == null)
            ? 1f
            : Mathf.Clamp01(SettingsManager.Instance.MusicVolume);

        if (musicSource.clip == clip && musicSource.isPlaying)
        {
            if (musicSource.volume <= 0.01f) musicSource.volume = targetVol;
            return;
        }

        if (musicFadeCoroutine != null)
        {
            StopCoroutine(musicFadeCoroutine);
            musicFadeCoroutine = null;
        }

        if (!Application.isPlaying || !musicSource.isPlaying || musicSource.clip == null)
        {
            musicSource.clip = clip;
            musicSource.volume = targetVol;
            musicSource.Play();
            return;
        }

        musicFadeCoroutine = StartCoroutine(CrossfadeMusicRoutine(clip, targetVol, 0.35f));
    }

    private IEnumerator CrossfadeMusicRoutine(AudioClip nextClip, float targetVol, float halfDuration)
    {
        float startVol = musicSource.volume;
        float t = 0f;
        while (t < halfDuration)
        {
            t += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(startVol, 0f, t / halfDuration);
            yield return null;
        }

        musicSource.clip = nextClip;
        musicSource.Play();

        t = 0f;
        while (t < halfDuration)
        {
            t += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(0f, targetVol, t / halfDuration);
            yield return null;
        }
        musicSource.volume = targetVol;
        musicFadeCoroutine = null;
    }

    public void PlayAmbience(AudioClip clip, float volume = AmbienceBaseVolume)
    {
        EnsureMixerAndSources();
        if (ambienceSource == null) return;

        if (clip == null)
        {
            ambienceSource.Stop();
            ambienceSource.clip = null;
            return;
        }

        float targetVol = mainMixer != null || SettingsManager.Instance == null
            ? volume
            : volume * Mathf.Clamp01(SettingsManager.Instance.SFXVolume);

        if (ambienceSource.clip == clip && ambienceSource.isPlaying) return;

        ambienceSource.clip = clip;
        ambienceSource.volume = targetVol;
        ambienceSource.Play();
    }

    public void PlaySFX(AudioClip clip, float volumeScale = 1f)
    {
        PlayPooled(sfxPool, ref sfxPoolIndex, clip, volumeScale, 1f, 1f);
    }

    public void PlaySFX(AudioClip clip, float volumeScale, float pitchMin, float pitchMax)
    {
        PlayPooled(sfxPool, ref sfxPoolIndex, clip, volumeScale, pitchMin, pitchMax);
    }

    public void PlayUI(AudioClip clip, float volumeScale = 1f, float pitchMin = 1f, float pitchMax = 1f)
    {
        PlayPooled(uiPool, ref uiPoolIndex, clip, volumeScale, pitchMin, pitchMax);
    }

    public void PlaySFXAtPoint(AudioClip clip, Vector3 position, float volume = 1f, float spatialBlend = 1f, float maxDistance = 28f)
    {
        if (clip == null) return;
        EnsureMixerAndSources();

        GameObject go = new GameObject($"OneShot_{clip.name}");
        go.transform.position = position;
        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.outputAudioMixerGroup = sfxGroup;
        src.spatialBlend = Mathf.Clamp01(spatialBlend);
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = 2f;
        src.maxDistance = Mathf.Max(5f, maxDistance);
        src.playOnAwake = false;
        src.volume = Mathf.Clamp01(volume);
        src.Play();
        Destroy(go, clip.length + 0.1f);
    }

    public bool PlayWithCooldown(string key, AudioClip clip, float cooldownSec, float volumeScale = 1f, float pitchMin = 1f, float pitchMax = 1f, bool isUI = true)
    {
        if (clip == null) return false;
        float now = Time.unscaledTime;
        if (!string.IsNullOrEmpty(key) && cooldownTimestamps.TryGetValue(key, out float last) && (now - last) < cooldownSec)
        {
            return false;
        }
        if (!string.IsNullOrEmpty(key)) cooldownTimestamps[key] = now;

        if (isUI) PlayUI(clip, volumeScale, pitchMin, pitchMax);
        else PlaySFX(clip, volumeScale, pitchMin, pitchMax);
        return true;
    }

    private void PlayPooled(AudioSource[] pool, ref int index, AudioClip clip, float volumeScale, float pitchMin, float pitchMax)
    {
        if (clip == null) return;
        EnsureMixerAndSources();
        if (pool == null || pool.Length == 0) return;

        AudioSource src = pool[index % pool.Length];
        index = (index + 1) % pool.Length;
        if (src == null) return;

        src.pitch = (Mathf.Approximately(pitchMin, pitchMax)) ? pitchMin : Random.Range(pitchMin, pitchMax);
        src.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    // --- Semantic UI helpers ---
    public void PlayButtonClick() => PlayUI(buttonClickClip);
    public void PlayButtonHover() => PlayWithCooldown("hover", buttonHoverClip, 0.045f, 0.7f, 0.98f, 1.03f, true);
    public void PlaySliderChange() => PlayWithCooldown("slider", sliderChangeClip, 0.06f, 0.75f, 1f, 1f, true);
    public void PlayModalOpen() => PlayWithCooldown("modal_open", uiModalOpenClip, 0.08f, 0.85f, 1f, 1f, true);
    public void PlayModalClose() => PlayWithCooldown("modal_close", uiModalCloseClip, 0.08f, 0.8f, 1f, 1f, true);
    public void PlayPanelWhoosh() => PlayWithCooldown("panel_whoosh", uiPanelWhooshClip, 0.08f, 0.85f, 1f, 1f, true);
    public void PlayDialogueBlip() => PlayWithCooldown("dialogue_blip", uiDialogueBlipClip, 0.045f, 0.55f, 0.94f, 1.08f, true);
    public void PlayNotification() => PlayWithCooldown("notification", uiNotificationClip, 0.15f, 0.9f, 1f, 1f, true);
    public void PlayTutorialStep() => PlayWithCooldown("tutorial_step", uiTutorialStepClip, 0.25f, 0.9f, 1f, 1f, true);
    public void PlayHungerWarning() => PlayWithCooldown("hunger_warn", uiHungerWarningClip, 1.5f, 0.9f, 1f, 1f, true);
    public void PlayShopBuy() => PlayUI(uiShopBuyClip, 0.95f);
    public void PlayShopError() => PlayWithCooldown("shop_err", uiShopErrorClip, 0.18f, 0.85f, 1f, 1f, true);
    public void PlayItemUse() => PlayUI(uiItemUseClip, 0.9f);

    // --- Semantic Gameplay helpers ---
    public void PlayTrashPickup() => PlaySFX(sfxTrashPickup, 0.95f, 0.96f, 1.04f);
    public void PlayTrashDrop() => PlaySFX(sfxTrashDrop, 0.85f, 0.95f, 1.05f);
    public void PlayTrashDepositCorrect() => PlaySFX(sfxTrashDepositCorrect, 0.95f);
    public void PlayTrashDepositWrong() => PlayWithCooldown("trash_wrong", sfxTrashDepositWrong, 0.25f, 0.95f, 1f, 1f, false);
    public void PlayQuizCorrect() => PlaySFX(sfxQuizCorrect, 0.95f);
    public void PlayQuizWrong() => PlayWithCooldown("quiz_wrong", sfxQuizWrong, 0.2f, 0.9f, 1f, 1f, false);
    public void PlayCoinEarn() => PlayWithCooldown("coin", sfxCoinEarn, 0.07f, 0.95f, 0.98f, 1.05f, false);
    public void PlayQuestComplete() => PlaySFX(sfxQuestComplete, 1f);
    public void PlayJump() => PlayWithCooldown("jump", sfxJump, 0.12f, 0.7f, 0.96f, 1.04f, false);
    public void PlayLand() => PlayWithCooldown("land", sfxLand, 0.12f, 0.75f, 0.94f, 1.04f, false);
    public void PlayLoadingReady() => PlayUI(sfxLoadingReady, 0.85f);

    public void PlayFootstep(bool isCityScene, float volume = 0.55f)
    {
        AudioClip[] bank = isCityScene ? footstepCityClips : footstepGrassClips;
        if (bank == null || bank.Length == 0) bank = footstepGrassClips;
        if (bank == null || bank.Length == 0) return;

        AudioClip clip = bank[Random.Range(0, bank.Length)];
        PlayWithCooldown("footstep", clip, 0.14f, volume, 0.93f, 1.07f, false);
    }
}
