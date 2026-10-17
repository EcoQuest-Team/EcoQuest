using UnityEngine;

/// <summary>
/// Allows the player to toggle the Quest panel on/off with TAB.
/// Fades + slides 40px right over 0.25s via HudVisibility without glitches.
/// Coins counter stays visible. Ignored during dialogue and info cards.
/// </summary>
public class QuestPanelToggleController : MonoBehaviour
{
    public static QuestPanelToggleController Instance { get; private set; }

    [SerializeField] private HudVisibility questHud;
    [SerializeField] private float toggleDuration = 0.25f;

    private bool isVisible = true;
    private bool hasHiddenOnce = false;
    private bool hasShownAgain = false;

    public bool IsPanelVisible => isVisible;
    public bool HasCompletedTabCycle => hasHiddenOnce && hasShownAgain;

    public event System.Action OnTabCycleCompleted;
    public event System.Action<bool> OnPanelToggled;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        if (questHud == null) questHud = GetComponent<HudVisibility>();
    }

    private void Start()
    {
        if (questHud == null) questHud = GetComponent<HudVisibility>();
        if (questHud != null)
        {
            isVisible = questHud.IsVisible;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            TryToggle();
        }
    }

    /// <summary>
    /// Attempts to toggle the quest panel, respecting dialogue and modal restrictions.
    /// </summary>
    public void TryToggle()
    {
        // 1. Only allow TAB toggle after the bin intro card has closed and the quest panel is active
        var intro = IntroSequenceController.Instance != null 
            ? IntroSequenceController.Instance 
            : FindFirstObjectByType<IntroSequenceController>();

        if (intro != null && !intro.HasRunIntro)
        {
            return;
        }

        // 2. Ignore during dialogue or info card
        if (intro != null)
        {
            var state = intro.CurrentState;
            if (state == IntroSequenceController.IntroState.Dialogue || state == IntroSequenceController.IntroState.BinIntro)
            {
                return;
            }
        }

        var tm = FindFirstObjectByType<TutorialManager>();
        if (tm != null && tm.tutorialOverlay != null && tm.tutorialOverlay.activeSelf)
        {
            return;
        }

        TogglePanel();
    }

    public void TogglePanel()
    {
        if (questHud == null) questHud = GetComponent<HudVisibility>();
        if (questHud == null) return;

        isVisible = !questHud.IsVisible;
        questHud.SetVisible(isVisible, toggleDuration);
        OnPanelToggled?.Invoke(isVisible);

        // Track hide then show cycle for tutorial completion
        if (!isVisible)
        {
            hasHiddenOnce = true;
        }
        else if (hasHiddenOnce)
        {
            hasShownAgain = true;
            OnTabCycleCompleted?.Invoke();
        }
    }

    public void SetPanelVisible(bool visible, bool immediate = false)
    {
        isVisible = visible;
        if (questHud != null)
        {
            if (immediate) questHud.SetVisibleImmediate(visible);
            else questHud.SetVisible(visible, toggleDuration);
        }
    }

    public void ResetTabCycle()
    {
        hasHiddenOnce = false;
        hasShownAgain = false;
    }
}
