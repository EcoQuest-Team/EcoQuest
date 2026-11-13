using UnityEngine;

/// <summary>
/// Central theme configuration for EcoQuest dark-green UI styling, fonts, sounds, and animations.
/// High-contrast palette meeting WCAG AAA/AA accessibility standards.
/// Allows live tweaking in editor and safe rollback via useLegacyPopups bool.
/// </summary>
[CreateAssetMenu(fileName = "UITheme", menuName = "EcoQuest/UI Theme")]
public class UITheme : ScriptableObject
{
    private static UITheme instance;
    public static UITheme Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<UITheme>("UITheme");
                if (instance == null)
                {
#if UNITY_EDITOR
                    instance = UnityEditor.AssetDatabase.LoadAssetAtPath<UITheme>("Assets/EcoQuest/UI/Shared/UITheme.asset");
#endif
                }
            }
            return instance;
        }
    }

    [Header("Rollback Settings")]
    [Tooltip("If true, re-enables legacy parchment popups for safe rollback.")]
    public bool useLegacyPopups = false;

    [Header("High-Contrast Color Palette (WCAG AAA/AA)")]
    [Tooltip("Opaque panel fill: #0B2419 at 97%")]
    public Color panelColor = new Color(0.043f, 0.141f, 0.098f, 0.97f); // #0B2419 @ 97%

    [Tooltip("Border color: #6DBE6D")]
    public Color borderColor = new Color(0.427f, 0.745f, 0.427f, 1f);   // #6DBE6D

    [Tooltip("Accent green / Title & tab pill: #2E7D4F")]
    public Color accentColor = new Color(0.180f, 0.490f, 0.310f, 1f);   // #2E7D4F

    [Tooltip("Primary text: Crisp White")]
    public Color textWhite = Color.white;

    [Tooltip("Secondary / muted text: #E3F5E3")]
    public Color textMuted = new Color(0.890f, 0.960f, 0.890f, 1f);     // #E3F5E3

    [Tooltip("Hint text: #D5ECD5")]
    public Color textHint = new Color(0.835f, 0.925f, 0.835f, 1f);      // #D5ECD5

    [Tooltip("Success green: #2E8B57")]
    public Color successColor = new Color(0.180f, 0.545f, 0.341f, 1f);  // #2E8B57

    [Tooltip("Error red: #B3413C")]
    public Color errorColor = new Color(0.702f, 0.255f, 0.235f, 1f);    // #B3413C

    [Tooltip("Warning amber: #F2C14E")]
    public Color warnColor = new Color(0.949f, 0.757f, 0.306f, 1f);     // #F2C14E

    [Tooltip("Backdrop overlay: Black 55%")]
    public Color backdropColor = new Color(0f, 0f, 0f, 0.55f);

    [Header("Quiz Answer Row Colors")]
    public Color quizAnswerIdle = new Color(0.090f, 0.251f, 0.184f, 1f);     // #17402F
    public Color quizAnswerHover = new Color(0.133f, 0.388f, 0.278f, 1f);    // #226347
    public Color quizAnswerSelected = new Color(0.180f, 0.490f, 0.310f, 1f); // #2E7D4F
    public Color quizAnswerCorrect = new Color(0.180f, 0.545f, 0.341f, 1f);  // #2E8B57
    public Color quizAnswerWrong = new Color(0.702f, 0.255f, 0.235f, 1f);    // #B3413C

    [Header("Font Assets (Shared)")]
    public TMPro.TMP_FontAsset titleFont;
    public TMPro.TMP_FontAsset bodyFont;

    public TMPro.TMP_FontAsset GetTitleFont()
    {
        if (titleFont == null)
        {
#if UNITY_EDITOR
            titleFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Fonts/Fredoka SDF.asset");
#endif
        }
        return titleFont;
    }

    public TMPro.TMP_FontAsset GetBodyFont()
    {
        if (bodyFont == null)
        {
#if UNITY_EDITOR
            bodyFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Fonts/Nunito SDF.asset");
#endif
        }
        return bodyFont;
    }

    [Header("Font Sizes")]
    public float fontSizeBody = 34f;
    public float fontSizeTitle = 40f;
    public float fontSizeLabel = 24f;
    public float fontSizeSmall = 20f;

    [Header("Animation Durations & Curves (Unscaled Time)")]
    public float modalEnterDuration = 0.28f;
    public float modalExitDuration = 0.18f;
    public float modalCrossfadeDuration = 0.25f;
    public float hudFadeDuration = 0.25f;
    public float advanceDelayDuration = 0.50f;
    public float tutorialStepGap = 0.15f;
    public float transitionStartScale = 0.95f;
    public AnimationCurve openEaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public AnimationCurve closeEaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Sprites")]
    public Sprite panelBackgroundSprite;
    public Sprite speakerTabGreenSprite;
    public Sprite speakerTabTealSprite;
    public Sprite roundedButtonSprite;
    public Sprite imageFrameBorderSprite;
    public Sprite categoryBioIcon;
    public Sprite categoryNonBioIcon;
    public Sprite categoryRecycleIcon;
    public Sprite categoryHazardIcon;
    public Sprite keycapFaceSprite;
    public Sprite keycapBaseSprite;

    [Header("Audio (Null-Safe)")]
    public AudioClip buttonHoverSound;
    public AudioClip buttonClickSound;
    public AudioClip modalOpenSound;
    public AudioClip modalCloseSound;
    public AudioClip quizCorrectSound;
    public AudioClip quizWrongSound;
    public AudioClip coinPickupSound;

    public static void PlaySound(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(clip, volume);
        }
    }
}
