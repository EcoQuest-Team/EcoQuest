using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable Settings Panel UI controller matching the EcoQuest wooden-panel and leaf style.
/// Handles Audio, Graphics, Gameplay, and read-only Controls display, plus unscaled fade/scale
/// open/close animations and a Reset to Defaults confirmation popup.
/// </summary>
public class SettingsPanelUI : MonoBehaviour
{
    [Header("Panel & Animation References")]
    public CanvasGroup rootCanvasGroup;
    public RectTransform modalFrameRect;
    public CanvasGroup modalFrameCanvasGroup;
    public float openDuration = 0.25f;
    public float closeDuration = 0.18f;

    private List<CanvasGroup> hudCanvasGroups = new List<CanvasGroup>();
    private bool isClosing = false;

    [Header("Audio Controls")]
    public Slider masterSlider;
    public TextMeshProUGUI masterPercentText;
    public Slider musicSlider;
    public TextMeshProUGUI musicPercentText;
    public Slider sfxSlider;
    public TextMeshProUGUI sfxPercentText;
    public Toggle muteAllToggle;
    public RectTransform muteSwitchKnob;

    [Header("Graphics Controls")]
    public TMP_Dropdown qualityDropdown;
    public Toggle fullscreenToggle;
    public TMP_Dropdown resolutionDropdown;
    public Toggle vsyncToggle;
    public TMP_Dropdown fpsLimitDropdown;

    [Header("Gameplay Controls")]
    public Slider sensitivitySlider;
    public TextMeshProUGUI sensitivityValueText;
    public Toggle tutorialHintsToggle;
    public TMP_Dropdown textSizeDropdown;

    [Header("Action Buttons")]
    public Button applyGraphicsButton;
    public Button resetDefaultsButton;
    public Button backButton;

    [Header("Reset Confirmation Popup")]
    public GameObject confirmResetOverlay;
    public Button confirmResetYesButton;
    public Button confirmResetNoButton;

    private List<Vector2Int> availableResolutions = new List<Vector2Int>();
    private readonly int[] fpsOptions = new[] { 30, 60, 120, -1 };

    private bool suppressCallbacks = false;
    private bool isInitialized = false;
    private Coroutine animCoroutine;

    public event Action OnPanelClosed;

    private void Awake()
    {
        EnsureValidLayoutAndScale();
        InitializeListeners();
    }

    private void OnEnable()
    {
        EnsureValidLayoutAndScale();
        InitializeListeners();
        if (confirmResetOverlay != null) confirmResetOverlay.SetActive(false);

        RefreshUIFromSettings();

        if (animCoroutine != null) StopCoroutine(animCoroutine);
        animCoroutine = StartCoroutine(AnimateOpenRoutine());
    }

    public void EnsureValidLayoutAndScale()
    {
        if (transform.localScale != Vector3.one)
        {
            transform.localScale = Vector3.one;
        }

        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            if (rt.anchorMin != Vector2.zero || rt.anchorMax != Vector2.one)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
        }

        if (rootCanvasGroup == null)
        {
            rootCanvasGroup = GetComponent<CanvasGroup>();
        }

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null && !canvas.overrideSorting)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
        }

        EnsureControlsGuideMapRow();
    }

    public void EnsureControlsGuideMapRow()
    {
        Transform box = transform.Find("ModalFrame/ControlsGuideBox");
        if (box == null) return;

        Transform rowM = box.Find("ControlRow_M");
        if (rowM == null)
        {
            Transform rowE = box.Find("ControlRow_E");
            if (rowE != null)
            {
                GameObject clone = Instantiate(rowE.gameObject, box);
                clone.name = "ControlRow_M";
                rowM = clone.transform;
            }
        }

        if (rowM != null)
        {
            Transform keyTextTr = rowM.Find("KeyBadge/KeyText");
            if (keyTextTr != null)
            {
                var tmp = keyTextTr.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = "M";
            }
            Transform actionTextTr = rowM.Find("ActionText");
            if (actionTextTr != null)
            {
                var tmp = actionTextTr.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = "Toggle Map";
            }
        }

        Transform keyW = box.Find("Key_W");
        if (keyW != null)
        {
            RectTransform kwRt = keyW.GetComponent<RectTransform>();
            if (kwRt != null) kwRt.anchoredPosition = new Vector2(kwRt.anchoredPosition.x, 116f);
        }

        string[] orderedRows = new string[]
        {
            "ControlRow_WASD",
            "ControlRow_SPACE",
            "ControlRow_SHIFT",
            "ControlRow_E",
            "ControlRow_CTRL",
            "ControlRow_TAB",
            "ControlRow_M",
            "ControlRow_ESC"
        };

        float startY = 82f;
        float stepY = 35f;
        for (int i = 0; i < orderedRows.Length; i++)
        {
            Transform r = box.Find(orderedRows[i]);
            if (r != null)
            {
                RectTransform rrt = r.GetComponent<RectTransform>();
                if (rrt != null)
                {
                    rrt.anchoredPosition = new Vector2(0f, startY - i * stepY);
                    rrt.sizeDelta = new Vector2(0f, 31f);
                }
            }
        }
    }

    public void OpenPanel()
    {
        EnsureValidLayoutAndScale();
        if (confirmResetOverlay != null) confirmResetOverlay.SetActive(false);
        gameObject.SetActive(true);

        InitializeListeners();
        RefreshUIFromSettings();

        if (animCoroutine != null) StopCoroutine(animCoroutine);
        animCoroutine = StartCoroutine(AnimateOpenRoutine());
    }

    private void InitializeListeners()
    {
        if (isInitialized) return;
        isInitialized = true;

        PopulateDropdownOptions();

        // Audio live callbacks
        if (masterSlider != null)
            masterSlider.onValueChanged.AddListener(_ => OnAudioControlChanged(true));
        if (musicSlider != null)
            musicSlider.onValueChanged.AddListener(_ => OnAudioControlChanged(true));
        if (sfxSlider != null)
            sfxSlider.onValueChanged.AddListener(_ => OnAudioControlChanged(true));
        if (muteAllToggle != null)
            muteAllToggle.onValueChanged.AddListener(_ => OnAudioControlChanged(false));

        // Gameplay live callbacks
        if (sensitivitySlider != null)
            sensitivitySlider.onValueChanged.AddListener(_ => OnGameplayControlChanged(true));
        if (tutorialHintsToggle != null)
            tutorialHintsToggle.onValueChanged.AddListener(_ => OnGameplayControlChanged(false));
        if (textSizeDropdown != null)
            textSizeDropdown.onValueChanged.AddListener(_ => OnGameplayControlChanged(false));

        // Action buttons
        if (applyGraphicsButton != null)
            applyGraphicsButton.onClick.AddListener(OnApplyGraphicsClicked);
        if (resetDefaultsButton != null)
            resetDefaultsButton.onClick.AddListener(OnResetDefaultsClicked);
        if (backButton != null)
            backButton.onClick.AddListener(ClosePanel);

        // Confirm Reset Popup buttons
        if (confirmResetYesButton != null)
            confirmResetYesButton.onClick.AddListener(OnConfirmResetYes);
        if (confirmResetNoButton != null)
            confirmResetNoButton.onClick.AddListener(OnConfirmResetNo);
    }

    public void PopulateDropdownOptions()
    {
        // 1. Quality presets from QualitySettings.names
        if (qualityDropdown != null)
        {
            qualityDropdown.ClearOptions();
            List<string> qNames = new List<string>(QualitySettings.names);
            if (qNames.Count == 0) qNames.Add("Default");
            qualityDropdown.AddOptions(qNames);
        }

        // 2. Valid unique resolutions
        availableResolutions = SettingsManager.GetUniqueValidResolutions();
        if (resolutionDropdown != null)
        {
            resolutionDropdown.ClearOptions();
            List<string> resLabels = new List<string>();
            for (int i = 0; i < availableResolutions.Count; i++)
            {
                resLabels.Add($"{availableResolutions[i].x} x {availableResolutions[i].y}");
            }
            resolutionDropdown.AddOptions(resLabels);
        }

        // 3. FPS Limit (30, 60, 120, Unlimited)
        if (fpsLimitDropdown != null)
        {
            fpsLimitDropdown.ClearOptions();
            fpsLimitDropdown.AddOptions(new List<string> { "30 FPS", "60 FPS", "120 FPS", "Unlimited" });
        }

        // 4. Text Size (Normal / Large)
        if (textSizeDropdown != null)
        {
            textSizeDropdown.ClearOptions();
            textSizeDropdown.AddOptions(new List<string> { "Normal", "Large" });
        }
    }

    public void RefreshUIFromSettings()
    {
        EnsureSettingsManager();
        var sm = SettingsManager.Instance;
        if (sm == null) return;

        suppressCallbacks = true;

        if (availableResolutions == null || availableResolutions.Count == 0)
        {
            PopulateDropdownOptions();
        }

        // Audio
        if (masterSlider != null) masterSlider.value = sm.MasterVolume;
        if (musicSlider != null) musicSlider.value = sm.MusicVolume;
        if (sfxSlider != null) sfxSlider.value = sm.SFXVolume;
        if (muteAllToggle != null) muteAllToggle.isOn = sm.MuteAll;
        UpdateAudioLabels();

        // Graphics
        if (qualityDropdown != null)
        {
            qualityDropdown.value = Mathf.Clamp(sm.QualityIndex, 0, Mathf.Max(0, qualityDropdown.options.Count - 1));
            qualityDropdown.RefreshShownValue();
        }
        if (fullscreenToggle != null) fullscreenToggle.isOn = sm.Fullscreen;

        if (resolutionDropdown != null && availableResolutions.Count > 0)
        {
            int idx = availableResolutions.FindIndex(r => r.x == sm.ResolutionWidth && r.y == sm.ResolutionHeight);
            if (idx < 0) idx = availableResolutions.Count - 1;
            resolutionDropdown.value = idx;
            resolutionDropdown.RefreshShownValue();
        }

        if (vsyncToggle != null) vsyncToggle.isOn = sm.VSync;

        if (fpsLimitDropdown != null)
        {
            int fpsIdx = Array.IndexOf(fpsOptions, sm.FPSLimit);
            if (fpsIdx < 0) fpsIdx = 3; // Unlimited
            fpsLimitDropdown.value = fpsIdx;
            fpsLimitDropdown.RefreshShownValue();
        }

        // Gameplay
        if (sensitivitySlider != null) sensitivitySlider.value = sm.MouseSensitivity;
        UpdateSensitivityLabel();
        if (tutorialHintsToggle != null) tutorialHintsToggle.isOn = sm.TutorialHints;
        if (textSizeDropdown != null)
        {
            textSizeDropdown.value = Mathf.Clamp(sm.TextSize, 0, 1);
            textSizeDropdown.RefreshShownValue();
        }

        suppressCallbacks = false;
    }

    private void UpdateAudioLabels()
    {
        if (masterPercentText != null && masterSlider != null)
            masterPercentText.text = $"{Mathf.RoundToInt(masterSlider.value * 100f)}%";
        if (musicPercentText != null && musicSlider != null)
            musicPercentText.text = $"{Mathf.RoundToInt(musicSlider.value * 100f)}%";
        if (sfxPercentText != null && sfxSlider != null)
            sfxPercentText.text = $"{Mathf.RoundToInt(sfxSlider.value * 100f)}%";

        if (muteSwitchKnob == null && muteAllToggle != null)
        {
            Transform k = muteAllToggle.transform.Find("Background/Knob");
            if (k != null) muteSwitchKnob = k as RectTransform;
        }
        if (muteSwitchKnob != null && muteAllToggle != null)
        {
            muteSwitchKnob.anchoredPosition = new Vector2(muteAllToggle.isOn ? 15f : -15f, 0f);
        }
    }

    private void UpdateSensitivityLabel()
    {
        if (sensitivityValueText != null && sensitivitySlider != null)
            sensitivityValueText.text = $"{sensitivitySlider.value:0.0}x";
    }

    private void OnAudioControlChanged(bool isSlider)
    {
        UpdateAudioLabels();
        if (suppressCallbacks) return;

        EnsureSettingsManager();
        if (SettingsManager.Instance != null)
        {
            float master = masterSlider != null ? masterSlider.value : SettingsManager.DefaultMasterVol;
            float music = musicSlider != null ? musicSlider.value : SettingsManager.DefaultMusicVol;
            float sfx = sfxSlider != null ? sfxSlider.value : SettingsManager.DefaultSFXVol;
            bool mute = muteAllToggle != null && muteAllToggle.isOn;
            SettingsManager.Instance.SetAudioSettings(master, music, sfx, mute, true);
        }

        if (AudioManager.Instance != null)
        {
            if (isSlider) AudioManager.Instance.PlaySliderChange();
            else AudioManager.Instance.PlayButtonClick();
        }
    }

    private void OnGameplayControlChanged(bool isSlider)
    {
        UpdateSensitivityLabel();
        if (suppressCallbacks) return;

        EnsureSettingsManager();
        if (SettingsManager.Instance != null)
        {
            float sens = sensitivitySlider != null ? sensitivitySlider.value : SettingsManager.DefaultMouseSensitivity;
            bool hints = tutorialHintsToggle == null || tutorialHintsToggle.isOn;
            int textSize = textSizeDropdown != null ? textSizeDropdown.value : SettingsManager.DefaultTextSize;
            SettingsManager.Instance.SetGameplaySettings(sens, hints, textSize, true);
        }

        if (AudioManager.Instance != null)
        {
            if (isSlider) AudioManager.Instance.PlaySliderChange();
            else AudioManager.Instance.PlayButtonClick();
        }
    }

    public void OnApplyGraphicsClicked()
    {
        EnsureSettingsManager();
        if (SettingsManager.Instance == null) return;

        int qIdx = qualityDropdown != null ? qualityDropdown.value : SettingsManager.DefaultQuality;
        bool fs = fullscreenToggle == null || fullscreenToggle.isOn;

        int resW = SettingsManager.DefaultResolutionWidth;
        int resH = SettingsManager.DefaultResolutionHeight;
        if (resolutionDropdown != null && availableResolutions.Count > 0)
        {
            int rIdx = Mathf.Clamp(resolutionDropdown.value, 0, availableResolutions.Count - 1);
            resW = availableResolutions[rIdx].x;
            resH = availableResolutions[rIdx].y;
        }

        bool vsync = vsyncToggle != null && vsyncToggle.isOn;
        int fpsIdx = fpsLimitDropdown != null ? Mathf.Clamp(fpsLimitDropdown.value, 0, fpsOptions.Length - 1) : 3;
        int fpsLimit = fpsOptions[fpsIdx];

        SettingsManager.Instance.SetAndApplyGraphics(qIdx, fs, resW, resH, vsync, fpsLimit, true);

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }
    }

    public void OnResetDefaultsClicked()
    {
        if (confirmResetOverlay != null)
        {
            confirmResetOverlay.SetActive(true);
        }
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }
    }

    public void OnConfirmResetYes()
    {
        EnsureSettingsManager();
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.ResetToDefaults();
        }
        if (confirmResetOverlay != null)
        {
            confirmResetOverlay.SetActive(false);
        }
        RefreshUIFromSettings();
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }
    }

    public void OnConfirmResetNo()
    {
        if (confirmResetOverlay != null)
        {
            confirmResetOverlay.SetActive(false);
        }
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }
    }

    public bool IsConfirmPopupOpen => confirmResetOverlay != null && confirmResetOverlay.activeSelf;

    public void CloseConfirmPopup()
    {
        if (confirmResetOverlay != null)
            confirmResetOverlay.SetActive(false);
    }

    private void OnDisable()
    {
        isClosing = false;
        if (animCoroutine != null)
        {
            StopCoroutine(animCoroutine);
            animCoroutine = null;
        }

        if (!PauseMenuController.IsPaused)
        {
            UIBackgroundBlur.Cleanup();
            SetHudAlpha(1f);
        }
    }

    public void ClosePanel()
    {
        if (isClosing) return;
        if (!gameObject.activeInHierarchy)
        {
            gameObject.SetActive(false);
            return;
        }

        isClosing = true;

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }

        if (animCoroutine != null) StopCoroutine(animCoroutine);
        animCoroutine = StartCoroutine(AnimateCloseRoutine());
    }

    public static void SetAllHudAlpha(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        string[] hudNames = new string[] {
            "UI_Quest",
            "UI_Coins",
            "UI_Minimap",
            "UI_Notification",
            "WASDTutorial",
            "UI_HUD_SettingsButton"
        };

        foreach (var name in hudNames)
        {
            var go = GameObject.Find(name);
            if (go != null)
            {
                var cg = go.GetComponent<CanvasGroup>();
                if (cg == null)
                {
                    cg = go.AddComponent<CanvasGroup>();
                }
                cg.alpha = alpha;
                cg.blocksRaycasts = (alpha > 0.01f);
                cg.interactable = (alpha > 0.01f);
            }
        }
    }

    private void SetHudAlpha(float alpha)
    {
        SetAllHudAlpha(alpha);
    }

    private IEnumerator AnimateOpenRoutine()
    {
        isClosing = false;

        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.alpha = 0f;
            rootCanvasGroup.interactable = false; // Block clicks during opening transition
            rootCanvasGroup.blocksRaycasts = true;
        }
        if (modalFrameRect != null)
        {
            modalFrameRect.localScale = new Vector3(0.95f, 0.95f, 1f);
        }

        float elapsed = 0f;
        float dur = Mathf.Max(0.01f, openDuration);
        float startBlur = UIBackgroundBlur.GetWeight();

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float ease = 1f - Mathf.Pow(1f - t, 3f);

            if (rootCanvasGroup != null) rootCanvasGroup.alpha = ease;
            if (modalFrameRect != null) modalFrameRect.localScale = Vector3.Lerp(new Vector3(0.95f, 0.95f, 1f), Vector3.one, ease);

            SetHudAlpha(1f - ease);
            UIBackgroundBlur.SetWeight(Mathf.Lerp(startBlur, 1f, ease));

            yield return null;
        }

        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.alpha = 1f;
            rootCanvasGroup.interactable = true; // Re-enable clicks after opening finishes
            rootCanvasGroup.blocksRaycasts = true;
        }
        if (modalFrameRect != null) modalFrameRect.localScale = Vector3.one;

        SetHudAlpha(0f);
        UIBackgroundBlur.SetWeight(1f);
        animCoroutine = null;
    }

    private IEnumerator AnimateCloseRoutine()
    {
        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.interactable = false; // Block clicks during closing transition
        }

        float elapsed = 0f;
        float dur = Mathf.Max(0.01f, closeDuration);
        Vector3 startScale = modalFrameRect != null ? modalFrameRect.localScale : Vector3.one;
        float startAlpha = rootCanvasGroup != null ? rootCanvasGroup.alpha : 1f;
        float startBlur = UIBackgroundBlur.GetWeight();
        bool isPaused = PauseMenuController.IsPaused;

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float ease = t * t;

            if (rootCanvasGroup != null) rootCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, ease);
            if (modalFrameRect != null) modalFrameRect.localScale = Vector3.Lerp(startScale, new Vector3(0.95f, 0.95f, 1f), ease);

            // If game is NOT paused, restore blur and HUD as Settings closes
            if (!isPaused)
            {
                SetHudAlpha(ease);
                UIBackgroundBlur.SetWeight(Mathf.Lerp(startBlur, 0f, ease));
            }

            yield return null;
        }

        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.alpha = 0f;
            rootCanvasGroup.blocksRaycasts = false;
        }

        if (!isPaused)
        {
            SetHudAlpha(1f);
            UIBackgroundBlur.Cleanup();
        }

        isClosing = false;
        animCoroutine = null;
        gameObject.SetActive(false);
        OnPanelClosed?.Invoke();
    }

    private void EnsureSettingsManager()
    {
        if (SettingsManager.Instance == null)
        {
            var existing = FindFirstObjectByType<SettingsManager>();
            if (existing == null)
            {
                GameObject go = new GameObject("SettingsManager");
                go.AddComponent<SettingsManager>();
            }
        }
    }
}
