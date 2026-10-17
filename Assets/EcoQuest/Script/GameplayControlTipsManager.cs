using System.Collections;
using UnityEngine;

/// <summary>
/// Coordinates the TAB and CTRL control tips after the bin intro card closes
/// (when the Quest panel is back), before practice drag-and-drop.
/// Completes each step when the player performs the action, or skips instantly if already done.
/// </summary>
public class GameplayControlTipsManager : MonoBehaviour
{
    public static GameplayControlTipsManager Instance { get; private set; }

    [Header("Data & UI References")]
    [SerializeField] private ControlTipsData tipsData;
    [SerializeField] private ControlTipCardUI tipCardUI;

    [Header("Controllers")]
    [SerializeField] private QuestPanelToggleController tabController;
    [SerializeField] private CursorQuickAccessController ctrlController;
    [SerializeField] private IntroSequenceController introController;

    private bool hasRunGameplayTips = false;
    private Coroutine tipsSequenceRoutine;

    public bool HasCompletedAllTips => hasRunGameplayTips;
    public event System.Action OnTabTipCompleted;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        ResolveReferences();

        if (introController != null)
        {
            introController.OnIntroCompleted += HandleIntroCompleted;
            introController.SubscribeEvents();
            if (introController.HasRunIntro)
            {
                HandleIntroCompleted();
            }
        }
    }

    private void OnDestroy()
    {
        if (introController != null)
        {
            introController.OnIntroCompleted -= HandleIntroCompleted;
        }
    }

    public void ResolveReferences()
    {
        if (tipCardUI == null) tipCardUI = FindFirstObjectByType<ControlTipCardUI>(FindObjectsInactive.Include);
        if (tabController == null) tabController = FindFirstObjectByType<QuestPanelToggleController>();
        if (ctrlController == null) ctrlController = FindFirstObjectByType<CursorQuickAccessController>();
        if (introController == null) introController = FindFirstObjectByType<IntroSequenceController>();
    }

    private void HandleIntroCompleted()
    {
        if (hasRunGameplayTips) return;
        if (introController != null && !introController.HasRunIntro) return;
        StartGameplayTipsSequence();
    }

    /// <summary>
    /// Starts the TAB and CTRL tips sequence after bin intro.
    /// </summary>
    public void StartGameplayTipsSequence()
    {
        ResolveReferences();
        if (SettingsManager.Instance != null && !SettingsManager.Instance.TutorialHints)
        {
            ApplyTutorialHintsSetting(false);
            return;
        }
        if (tipsSequenceRoutine != null) StopCoroutine(tipsSequenceRoutine);
        tipsSequenceRoutine = StartCoroutine(GameplayTipsRoutine());
    }

    private bool wasSkippedBySettings = false;

    public void ApplyTutorialHintsSetting(bool enabled)
    {
        if (!enabled)
        {
            if (!hasRunGameplayTips) wasSkippedBySettings = true;
            if (tipsSequenceRoutine != null)
            {
                StopCoroutine(tipsSequenceRoutine);
                tipsSequenceRoutine = null;
            }
            hasRunGameplayTips = true;
            if (tipCardUI != null) tipCardUI.SetImmediateHidden();
            OnTabTipCompleted?.Invoke();
        }
        else if (wasSkippedBySettings)
        {
            wasSkippedBySettings = false;
            hasRunGameplayTips = false;
            if (introController != null && introController.HasRunIntro)
            {
                StartGameplayTipsSequence();
            }
        }
    }

    private IEnumerator GameplayTipsRoutine()
    {
        // Short pause after bin intro closes so the player sees the Quest panel return
        yield return new WaitForSecondsRealtime(0.5f);

        if (tipCardUI == null || tipsData == null)
        {
            hasRunGameplayTips = true;
            OnTabTipCompleted?.Invoke();
            yield break;
        }

        // ==============================================================
        // 1. TAB STEP: Hide/Show Quest List
        // ==============================================================
        bool tabCompleted = false;

        if (tabController != null && tabController.HasCompletedTabCycle)
        {
            // Already did it -> skip instantly!
            tabCompleted = true;
            hasRunGameplayTips = true;
            OnTabTipCompleted?.Invoke();
            yield break;
        }
        else
        {
            tipCardUI.ShowTip(tipsData.tabTip);

            // Wait for player to hide and then show the Quest panel
            while (!tabCompleted)
            {
                bool tabHeld = Input.GetKey(KeyCode.Tab);
                tipCardUI.SetKeyHeld(tabHeld);

                if (tabController != null && tabController.HasCompletedTabCycle)
                {
                    tabCompleted = true;
                }
                yield return null;
            }

            bool stepDone = false;
            tipCardUI.CompleteTip(tipsData.tabTip.doneMessage, () => stepDone = true);
            while (!stepDone) yield return null;
        }

        hasRunGameplayTips = true;
        tipsSequenceRoutine = null;
        OnTabTipCompleted?.Invoke();
    }
}
