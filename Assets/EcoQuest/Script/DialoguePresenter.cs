using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Unified singleton dialogue presenter used across all EcoQuest scenes (Forest Scene & City Scene).
/// Manages standard dark-green dialogue box, speaker tab, paginated typewriter body text with Text-Fit Guard,
/// 0.5s advance delay, bottom-right hint row, and final page choices.
/// Supports clicking on hint row, buttons, or anywhere on panel to advance.
/// </summary>
public class DialoguePresenter : MonoBehaviour, IPointerClickHandler
{
    private static DialoguePresenter instance;
    public static DialoguePresenter Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<DialoguePresenter>(FindObjectsInactive.Include);
            }
            return instance;
        }
        private set => instance = value;
    }

    [Header("UI Containers & Background")]
    [SerializeField] private Canvas dialogueCanvas;
    [SerializeField] private CanvasGroup dialogueCanvasGroup;
    [SerializeField] private RectTransform dialoguePanelRect;
    [SerializeField] private UnityEngine.UI.Image panelBackgroundImage;

    [Header("Speaker Tab")]
    [SerializeField] private RectTransform speakerTabRect;
    [SerializeField] private UnityEngine.UI.Image speakerPillImage;
    [SerializeField] private TextMeshProUGUI speakerNameText;

    [Header("Body Text")]
    [SerializeField] private RectTransform bodyTextContainer;
    [SerializeField] private TextMeshProUGUI dialogueBodyText;

    [Header("Bottom Right: Footer & Hint")]
    [SerializeField] private RectTransform footerRowRect;
    [SerializeField] private RectTransform continuePromptRoot;
    [SerializeField] private KeyCapPrompt continueKeyCap;

    [Header("Bottom Right: Choice Buttons")]
    [SerializeField] private RectTransform choiceButtonsRoot;
    [SerializeField] private Button acceptQuestButton;
    [SerializeField] private Button notNowButton;
    [SerializeField] private KeyCapPrompt acceptKeyCap;
    [SerializeField] private KeyCapPrompt notNowKeyCap;

    [Header("Next Page Indicator")]
    [SerializeField] private GameObject dialogueNextTriangle;
    private Vector3 triangleBaseLocalPos;

    [Header("Sprites")]
    [SerializeField] private Sprite panelBackgroundSprite;
    [SerializeField] private Sprite speakerPillGreenSprite;
    [SerializeField] private Sprite speakerPillTealSprite;

    [Header("Paging & Timing Settings")]
    [SerializeField] private float advanceLockDuration = 0.50f;
    [SerializeField] private float typewriterCharDelay = 0.015f;
    [SerializeField] private float baseFontSize = 34f;
    [SerializeField] private float minFontSize = 30f;
    [SerializeField] private int maxLinesPerPage = 3;

    [Header("Transition Settings (Unscaled Time)")]
    [SerializeField] private float panelOpenDuration = 0.28f;
    [SerializeField] private float panelCloseDuration = 0.18f;
    [SerializeField] private float transitionStartScale = 0.95f;
    [SerializeField] private AnimationCurve openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve closeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Input Settings")]
    [Tooltip("If true, clicking anywhere on the panel advances normal dialogue pages.")]
    [SerializeField] public bool clickAnywhereToContinue = true;

    // Events
    public event Action OnDialogueStarted;
    public event Action OnDialogueEnded;
    public event Action OnLastDialogueLineReached;
    public event Action<bool> OnTypingStateChanged;

    // State
    public bool IsInDialogue { get; private set; } = false;
    public float CurrentAlpha => dialogueCanvasGroup != null ? dialogueCanvasGroup.alpha : 0f;
    private bool isSuspended = false;
    public bool IsSuspended => isSuspended;
    private bool isTypewriterComplete = false;
    private bool isAdvanceLocked = false;
    private float advanceLockTimer = 0f;
    private float inputCooldown = 0f;
    private int lastAdvanceFrame = -1;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!IsInDialogue || isSuspended) return;
        bool isLastPage = (currentPageIndex == activePages.Count - 1);
        if (isLastPage && hasChoicesAtEnd) return; // Never on choice pages!
        if (clickAnywhereToContinue)
        {
            HandleAdvanceOrConfirm();
        }
    }

    public class DialogueCoroutineRunner : MonoBehaviour
    {
        public static DialogueCoroutineRunner Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                if (Application.isPlaying && transform.parent == null)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
            }
        }
    }
    private static DialogueCoroutineRunner persistentRunner;

    public static DialogueCoroutineRunner GetRunner()
    {
        if (persistentRunner == null || !persistentRunner.gameObject.activeInHierarchy)
        {
            if (persistentRunner != null)
            {
                persistentRunner.gameObject.SetActive(true);
            }

            if (persistentRunner == null || !persistentRunner.gameObject.activeInHierarchy)
            {
                var runners = UnityEngine.Object.FindObjectsByType<DialogueCoroutineRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (runners != null && runners.Length > 0)
                {
                    persistentRunner = runners[0];
                    persistentRunner.gameObject.SetActive(true);
                    for (int i = 1; i < runners.Length; i++)
                    {
                        if (runners[i] != null)
                        {
                            if (Application.isPlaying) Destroy(runners[i].gameObject);
                            else DestroyImmediate(runners[i].gameObject);
                        }
                    }
                }
                else
                {
                    var existingGo = GameObject.Find("[DialogueCoroutineRunner]");
                    if (existingGo != null)
                    {
                        existingGo.SetActive(true);
                        persistentRunner = existingGo.GetComponent<DialogueCoroutineRunner>() ?? existingGo.AddComponent<DialogueCoroutineRunner>();
                    }
                    else
                    {
                        var runnerGo = new GameObject("[DialogueCoroutineRunner]");
                        if (Application.isPlaying)
                        {
                            UnityEngine.Object.DontDestroyOnLoad(runnerGo);
                        }
                        persistentRunner = runnerGo.AddComponent<DialogueCoroutineRunner>();
                    }
                }
            }
        }

        if (persistentRunner != null && !persistentRunner.gameObject.activeSelf)
        {
            persistentRunner.gameObject.SetActive(true);
        }
        return persistentRunner;
    }

    public static Coroutine RunCoroutine(IEnumerator routine)
    {
        return GetRunner().StartCoroutine(routine);
    }

    public static void StopRunningCoroutine(Coroutine coroutine)
    {
        if (coroutine != null && persistentRunner != null)
        {
            persistentRunner.StopCoroutine(coroutine);
        }
    }

    public void EnsureActiveHierarchy()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (dialoguePanelRect != null && !dialoguePanelRect.gameObject.activeSelf)
            dialoguePanelRect.gameObject.SetActive(true);
        Transform p = transform.parent;
        while (p != null)
        {
            if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
            p = p.parent;
        }
    }

    private readonly List<string> activePages = new List<string>();
    private int currentPageIndex = 0;
    private bool hasChoicesAtEnd = false;

    private Action onAcceptCallback;
    private Action onDeclineCallback;
    private Action onCompleteCallback;

    private Coroutine typewriterRoutine;
    private Coroutine fadeRoutine;
    private Coroutine punchRoutine;
    private readonly List<CanvasGroup> fadedHudGroups = new List<CanvasGroup>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            enabled = false;
            return;
        }

        InitializeUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnDisable()
    {
        if (typewriterRoutine != null) { StopRunningCoroutine(typewriterRoutine); typewriterRoutine = null; }
        if (punchRoutine != null) { StopRunningCoroutine(punchRoutine); punchRoutine = null; }
    }

    private void InitializeUI()
    {
        if (dialogueCanvas == null) dialogueCanvas = GetComponentInParent<Canvas>();
        if (dialoguePanelRect == null) dialoguePanelRect = transform as RectTransform;
        if (dialogueCanvasGroup == null)
        {
            dialogueCanvasGroup = GetComponent<CanvasGroup>();
            if (dialogueCanvasGroup == null && dialoguePanelRect != null) dialogueCanvasGroup = dialoguePanelRect.gameObject.AddComponent<CanvasGroup>();
        }

        // Ensure all child parts (tab, body, footer, buttons) are strictly parented under the single dialogue root CanvasGroup
        EnsurePartsReparented();
        EnsureBodyAreaSettings();
        EnsureFooterLayout();

        if (dialogueNextTriangle == null && dialoguePanelRect != null)
        {
            Transform dnt = dialoguePanelRect.Find("DialogueNextTriangle");
            if (dnt == null && dialoguePanelRect.Find("Box") != null) dnt = dialoguePanelRect.Find("Box/DialogueNextTriangle");
            if (dnt != null) dialogueNextTriangle = dnt.gameObject;
        }
        if (dialogueNextTriangle != null)
        {
            triangleBaseLocalPos = dialogueNextTriangle.transform.localPosition;
            dialogueNextTriangle.SetActive(false);
        }

        SetImmediateHidden();
    }

    private void EnsurePartsReparented()
    {
        if (dialoguePanelRect == null) return;
        Transform[] parts = new Transform[] { speakerTabRect, bodyTextContainer, footerRowRect, continuePromptRoot, choiceButtonsRoot };
        foreach (var p in parts)
        {
            if (p != null && !p.IsChildOf(dialoguePanelRect))
            {
                p.SetParent(dialoguePanelRect, false);
            }
        }
    }

    public void EnsureFooterLayout()
    {
        if (footerRowRect != null)
        {
            var fhlg = footerRowRect.GetComponent<HorizontalLayoutGroup>();
            if (fhlg != null)
            {
                fhlg.childAlignment = TextAnchor.MiddleRight;
                fhlg.childControlWidth = true;
                fhlg.childControlHeight = true;
                fhlg.childForceExpandWidth = false;
                fhlg.childForceExpandHeight = false;
                fhlg.spacing = 14f;
                var pad = fhlg.padding;
                pad.right = 32;
                fhlg.padding = pad;
            }
        }

        if (continuePromptRoot != null)
        {
            var chlg = continuePromptRoot.GetComponent<HorizontalLayoutGroup>();
            if (chlg != null)
            {
                chlg.childAlignment = TextAnchor.MiddleCenter;
                chlg.childControlWidth = true;
                chlg.childControlHeight = true;
                chlg.childForceExpandWidth = false;
                chlg.childForceExpandHeight = false;
                chlg.spacing = 14f;
            }
            continuePromptRoot.sizeDelta = new Vector2(continuePromptRoot.sizeDelta.x, 48f);

            var csf = continuePromptRoot.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = continuePromptRoot.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var kcp = continuePromptRoot.GetComponentInChildren<KeyCapPrompt>(true);
            if (kcp != null)
            {
                var khlg = kcp.GetComponent<HorizontalLayoutGroup>();
                if (khlg != null)
                {
                    khlg.childAlignment = TextAnchor.MiddleCenter;
                    khlg.childControlWidth = true;
                    khlg.childControlHeight = true;
                    khlg.childForceExpandWidth = false;
                    khlg.childForceExpandHeight = false;
                    khlg.spacing = 14f;
                }
            }
        }
    }

    private float EffectiveFontSize => Mathf.Round(baseFontSize * SettingsManager.CurrentTextScale * 10f) / 10f;

    public void ApplyTextSizeScale(HashSet<int> managedTmpIds = null)
    {
        if (dialogueBodyText != null)
        {
            if (managedTmpIds != null) managedTmpIds.Add(dialogueBodyText.GetInstanceID());
            dialogueBodyText.fontSize = EffectiveFontSize;
        }
    }

    private void EnsureBodyAreaSettings()
    {
        if (dialogueBodyText != null)
        {
            dialogueBodyText.fontSize = EffectiveFontSize;
            dialogueBodyText.enableAutoSizing = false;
            dialogueBodyText.enableWordWrapping = true;
            dialogueBodyText.overflowMode = TextOverflowModes.Truncate;
            dialogueBodyText.alignment = TextAlignmentOptions.TopLeft;
        }
    }

    public void SetImmediateHidden()
    {
        IsInDialogue = false;
        if (dialogueCanvasGroup != null)
        {
            dialogueCanvasGroup.alpha = 0f;
            dialogueCanvasGroup.interactable = false;
            dialogueCanvasGroup.blocksRaycasts = false;
        }
        if (choiceButtonsRoot != null) choiceButtonsRoot.gameObject.SetActive(false);
        if (continuePromptRoot != null) continuePromptRoot.gameObject.SetActive(false);
    }

    /// <summary>
    /// Shows a dialogue session with speaker name, lines, and optional choice buttons on the last page.
    /// Uses Text-Fit Guard to split long lines into pages where each page has at most 3 lines at 34px.
    /// </summary>
    public void ShowDialogue(
        string speakerName,
        IEnumerable<string> lines,
        bool showChoicesAtEnd = false,
        string acceptLabel = "Accept",
        string declineLabel = "Close",
        Action onAccept = null,
        Action onDecline = null,
        Action onComplete = null)
    {
        if (lines == null) return;

        EnsureActiveHierarchy();
        EnsureBodyAreaSettings();
        EnsureFooterLayout();

        // Calculate body width for text fitting
        float bodyWidth = 1154f;
        if (bodyTextContainer != null && bodyTextContainer.rect.width > 100f)
            bodyWidth = bodyTextContainer.rect.width;
        else if (dialogueBodyText != null && dialogueBodyText.rectTransform.rect.width > 100f)
            bodyWidth = dialogueBodyText.rectTransform.rect.width;

        float scaledFont = EffectiveFontSize;
        // Build paginated lines via Text-Fit Guard
        activePages.Clear();
        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            activePages.AddRange(SplitIntoPagesWithFitGuard(rawLine.Trim(), dialogueBodyText, bodyWidth, maxLinesPerPage, scaledFont));
        }

        if (activePages.Count == 0) return;

        IsInDialogue = true;
        isSuspended = false;
        currentPageIndex = 0;
        hasChoicesAtEnd = showChoicesAtEnd;
        onAcceptCallback = onAccept;
        onDeclineCallback = onDecline;
        onCompleteCallback = onComplete;

        // Configure choices
        if (acceptKeyCap != null)
        {
            acceptKeyCap.SetPrompt("E", acceptLabel);
            acceptKeyCap.OnPromptClicked -= HandleAccept;
            acceptKeyCap.OnPromptClicked += HandleAccept;
        }
        if (notNowKeyCap != null)
        {
            notNowKeyCap.SetPrompt("Q", declineLabel);
            notNowKeyCap.OnPromptClicked -= HandleDecline;
            notNowKeyCap.OnPromptClicked += HandleDecline;
        }

        if (continueKeyCap != null)
        {
            continueKeyCap.OnPromptClicked -= HandleAdvanceOrConfirm;
            continueKeyCap.OnPromptClicked += HandleAdvanceOrConfirm;
        }

        float containerW = choiceButtonsRoot != null ? choiceButtonsRoot.rect.width : 0f;
        float maxChoiceW = containerW > 200f ? Mathf.Clamp((containerW - 18f) * 0.5f, 180f, 430f) : 400f;
        ConfigureChoiceButton(acceptQuestButton, acceptKeyCap, acceptLabel, HandleAccept, maxChoiceW);
        ConfigureChoiceButton(notNowButton, notNowKeyCap, declineLabel, HandleDecline, maxChoiceW);
        if (choiceButtonsRoot != null) LayoutRebuilder.ForceRebuildLayoutImmediate(choiceButtonsRoot);

        // Setup speaker
        SetSpeakerDisplay(speakerName);

        // Fade HUD out
        FadeHud(false);

        if (ModalManager.Instance != null && ModalManager.Instance.ActiveModal?.modalObject != gameObject)
        {
            ModalManager.Instance.RequestModal(ModalManager.ModalType.Dialogue, gameObject, null, () => CloseDialogue());
        }

        // Fade dialogue panel in
        if (fadeRoutine != null) StopRunningCoroutine(fadeRoutine);
        fadeRoutine = RunCoroutine(FadeDialoguePanelRoutine(1f, panelOpenDuration));

        OnDialogueStarted?.Invoke();

        ShowPage(currentPageIndex);
    }

    private static void ConfigureChoiceButton(Button btn, KeyCapPrompt keyCap, string labelText, UnityEngine.Events.UnityAction onClick, float maxBtnWidth)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(onClick);

        TMP_Text tmp = keyCap != null && keyCap.ActionLabelText != null
            ? keyCap.ActionLabelText
            : btn.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null) return;

        bool hasVisibleBadge = btn.transform.Find("KeycapBadge")?.gameObject.activeSelf ?? (keyCap != null);
        var rtb = ResponsiveTextButton.AttachOrGet(btn);
        rtb.Configure(
            labelRef: tmp,
            backgroundRef: btn.GetComponent<Image>(),
            minFont: 15f,
            maxFont: 22f,
            minBtnHeight: 52f,
            maxBtnHeight: 120f,
            minBtnWidth: 140f,
            maxBtnWidth: maxBtnWidth,
            fitWidth: true,
            allowHeightGrow: true,
            padLeft: hasVisibleBadge ? 64f : 22f,
            padRight: 22f,
            padVertical: 14f,
            manageMargins: false);
        rtb.SetText(labelText);
    }

    private void SetSpeakerDisplay(string speaker)
    {
        if (speakerNameText != null)
        {
            string clean = string.IsNullOrEmpty(speaker) ? "GUARDIAN" : speaker.Trim().ToUpperInvariant();
            speakerNameText.text = clean;

            bool isPlayer = clean.Equals("YOU", StringComparison.OrdinalIgnoreCase);
            speakerNameText.color = isPlayer ? new Color(0.85f, 1.0f, 0.95f, 1f) : Color.white;

            if (speakerPillImage != null)
            {
                if (isPlayer && speakerPillTealSprite != null)
                    speakerPillImage.sprite = speakerPillTealSprite;
                else if (!isPlayer && speakerPillGreenSprite != null)
                    speakerPillImage.sprite = speakerPillGreenSprite;
                else
                    speakerPillImage.color = isPlayer ? new Color(0.18f, 0.55f, 0.46f, 1f) : new Color(0.36f, 0.67f, 0.35f, 1f);
            }

            // Adjust speaker tab width with 28px padding, max 360
            if (speakerTabRect != null)
            {
                float prefWidth = speakerNameText.preferredWidth + 56f;
                float clampedWidth = Mathf.Clamp(prefWidth, 140f, 360f);
                speakerTabRect.sizeDelta = new Vector2(clampedWidth, 44f);
            }
        }
    }

    private void ShowPage(int pageIdx)
    {
        if (pageIdx < 0 || pageIdx >= activePages.Count)
        {
            CloseDialogue();
            return;
        }

        string pageText = activePages[pageIdx];
        bool isLastPage = (pageIdx == activePages.Count - 1);

        if (isLastPage)
        {
            OnLastDialogueLineReached?.Invoke();
        }

        // Start advance delay lock (0.5s per page)
        isAdvanceLocked = true;
        advanceLockTimer = advanceLockDuration;
        if (continueKeyCap != null) continueKeyCap.SetLocked(true);
        if (acceptKeyCap != null) acceptKeyCap.SetLocked(true);
        if (notNowKeyCap != null) notNowKeyCap.SetLocked(true);

        // Footer: every dialogue page must show a footer (normal -> [E] Continue, last -> [E] Finish, choice -> Accept/Not now)
        bool isChoicePage = isLastPage && hasChoicesAtEnd;
        if (isChoicePage)
        {
            if (choiceButtonsRoot != null) choiceButtonsRoot.gameObject.SetActive(true);
            if (continuePromptRoot != null) continuePromptRoot.gameObject.SetActive(false);
        }
        else
        {
            if (choiceButtonsRoot != null) choiceButtonsRoot.gameObject.SetActive(false);
            if (continuePromptRoot != null)
            {
                continuePromptRoot.gameObject.SetActive(true);
                if (continueKeyCap != null)
                {
                    continueKeyCap.SetPrompt("E", isLastPage ? "Finish" : "Continue");
                }
            }
        }
        if (footerRowRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(footerRowRect);

        // Punch speaker tab
        if (punchRoutine != null) StopRunningCoroutine(punchRoutine);
        punchRoutine = RunCoroutine(PunchSpeakerTabRoutine());

        // Typewriter
        if (dialogueNextTriangle != null) dialogueNextTriangle.SetActive(false);
        if (typewriterRoutine != null) StopRunningCoroutine(typewriterRoutine);
        typewriterRoutine = RunCoroutine(TypewriterRoutine(pageText, isLastPage));
    }

    private IEnumerator TypewriterRoutine(string fullText, bool isLastPage)
    {
        isTypewriterComplete = false;
        OnTypingStateChanged?.Invoke(true);
        if (dialogueNextTriangle != null) dialogueNextTriangle.SetActive(false);

        if (dialogueBodyText != null)
        {
            dialogueBodyText.text = fullText;
            if (!Application.isPlaying)
            {
                dialogueBodyText.maxVisibleCharacters = 99999;
                isTypewriterComplete = true;
                isAdvanceLocked = false;
                if (continueKeyCap != null) continueKeyCap.SetLocked(false);
                if (acceptKeyCap != null) acceptKeyCap.SetLocked(false);
                if (notNowKeyCap != null) notNowKeyCap.SetLocked(false);
                if (dialogueCanvasGroup != null) dialogueCanvasGroup.alpha = 1f;
                yield break;
            }
            dialogueBodyText.maxVisibleCharacters = 0;
            int totalChars = fullText.Length;

            for (int i = 1; i <= totalChars; i++)
            {
                dialogueBodyText.maxVisibleCharacters = i;
                yield return new WaitForSecondsRealtime(typewriterCharDelay);
            }

            dialogueBodyText.maxVisibleCharacters = 99999;
        }

        isTypewriterComplete = true;
        OnTypingStateChanged?.Invoke(false);

        if (dialogueNextTriangle != null && (!isLastPage || !hasChoicesAtEnd))
        {
            dialogueNextTriangle.SetActive(true);
        }

        if (isLastPage && hasChoicesAtEnd)
        {
            ShowChoiceButtons();
        }
    }

    /// <summary>
    /// Updates the currently shown dialogue body line in place (e.g., for live timers) without restarting typing.
    /// </summary>
    public void UpdateLiveCurrentLineText(string newText)
    {
        if (!IsInDialogue || string.IsNullOrEmpty(newText)) return;
        if (currentPageIndex >= 0 && currentPageIndex < activePages.Count)
        {
            activePages[currentPageIndex] = newText;
        }
        if (dialogueBodyText != null)
        {
            dialogueBodyText.text = newText;
            if (isTypewriterComplete)
            {
                dialogueBodyText.maxVisibleCharacters = 99999;
            }
        }
    }

    private void CompleteTypewriterInstantly()
    {
        if (typewriterRoutine != null) StopRunningCoroutine(typewriterRoutine);
        if (dialogueBodyText != null) dialogueBodyText.maxVisibleCharacters = 99999;
        isTypewriterComplete = true;
        OnTypingStateChanged?.Invoke(false);

        bool isLastPage = (currentPageIndex == activePages.Count - 1);
        if (dialogueNextTriangle != null && (!isLastPage || !hasChoicesAtEnd))
        {
            dialogueNextTriangle.SetActive(true);
        }

        if (isLastPage && hasChoicesAtEnd)
        {
            ShowChoiceButtons();
        }
    }

    private void ShowChoiceButtons()
    {
        if (choiceButtonsRoot != null)
        {
            choiceButtonsRoot.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(choiceButtonsRoot);
            if (footerRowRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(footerRowRect);
        }
        if (continuePromptRoot != null) continuePromptRoot.gameObject.SetActive(false);
    }

    /// <summary>
    /// Suspends dialogue: pauses typewriter, halts fading, sets CanvasGroup alpha = 0,
    /// blocksRaycasts = false, interactable = false, and deactivates visuals completely.
    /// Retains page index, pages, and callbacks so it can resume cleanly.
    /// </summary>
    public void Suspend()
    {
        if (!IsInDialogue || isSuspended) return;
        isSuspended = true;

        if (typewriterRoutine != null)
        {
            StopRunningCoroutine(typewriterRoutine);
            typewriterRoutine = null;
        }
        if (fadeRoutine != null)
        {
            StopRunningCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        if (dialogueCanvasGroup != null)
        {
            dialogueCanvasGroup.alpha = 0f;
            dialogueCanvasGroup.interactable = false;
            dialogueCanvasGroup.blocksRaycasts = false;
        }

        if (dialoguePanelRect != null)
        {
            dialoguePanelRect.gameObject.SetActive(false);
        }
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Resumes suspended dialogue: reactivates visuals, sets alpha to 1,
    /// displays current page text, and restores inputs.
    /// </summary>
    public void Resume()
    {
        if (!isSuspended) return;
        isSuspended = false;

        gameObject.SetActive(true);
        if (dialoguePanelRect != null)
        {
            dialoguePanelRect.gameObject.SetActive(true);
        }

        if (dialogueCanvasGroup != null)
        {
            dialogueCanvasGroup.alpha = 1f;
            dialogueCanvasGroup.interactable = true;
            dialogueCanvasGroup.blocksRaycasts = true;
        }

        if (activePages.Count > 0 && currentPageIndex >= 0 && currentPageIndex < activePages.Count)
        {
            ShowPage(currentPageIndex);
        }
    }

    private void Update()
    {
        if (!IsInDialogue || isSuspended) return;

        if (inputCooldown > 0f) inputCooldown -= Time.unscaledDeltaTime;

        if (isAdvanceLocked)
        {
            advanceLockTimer -= Time.unscaledDeltaTime;
            if (advanceLockTimer <= 0f)
            {
                isAdvanceLocked = false;
                if (continueKeyCap != null) continueKeyCap.SetLocked(false);
                if (acceptKeyCap != null) acceptKeyCap.SetLocked(false);
                if (notNowKeyCap != null) notNowKeyCap.SetLocked(false);
            }
        }

        if (dialogueNextTriangle != null && dialogueNextTriangle.activeSelf)
        {
            float bob = Mathf.Sin(Time.unscaledTime * 6f) * 3f;
            dialogueNextTriangle.transform.localPosition = new Vector3(triangleBaseLocalPos.x, triangleBaseLocalPos.y + bob, 0f);
        }

        HandleInput();
    }

    private void HandleInput()
    {
        if (inputCooldown > 0f) return;

        bool ePressed = Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
        bool clickPressed = Input.GetMouseButtonDown(0);
        bool qPressed = Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Escape);

        bool isLastPage = (currentPageIndex == activePages.Count - 1);

        if (ePressed || (clickPressed && clickAnywhereToContinue && (!isLastPage || !hasChoicesAtEnd)))
        {
            HandleAdvanceOrConfirm();
        }
        else if (qPressed && isLastPage && hasChoicesAtEnd && isTypewriterComplete && !isAdvanceLocked)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) PauseMenuController.NotifyEscapeConsumed();
            HandleDecline();
            inputCooldown = 0.25f;
        }
    }

    public void HandleAdvanceOrConfirm()
    {
        if (Time.frameCount == lastAdvanceFrame) return; // Debounce same-frame click + key
        if (isAdvanceLocked) return; // Strict lock: clicks during lock ignored, not buffered
        lastAdvanceFrame = Time.frameCount;

        bool isLastPage = (currentPageIndex == activePages.Count - 1);

        if (!isTypewriterComplete)
        {
            CompleteTypewriterInstantly();
            inputCooldown = 0.20f;
            return;
        }

        if (isLastPage && hasChoicesAtEnd)
        {
            HandleAccept();
        }
        else
        {
            AdvancePage();
        }
        inputCooldown = 0.25f;
    }

    public void AdvancePage()
    {
        if (currentPageIndex < activePages.Count - 1)
        {
            currentPageIndex++;
            ShowPage(currentPageIndex);
        }
        else
        {
            CloseDialogue();
            onCompleteCallback?.Invoke();
        }
    }

    public void HandleAccept()
    {
        CloseDialogue();
        onAcceptCallback?.Invoke();
    }

    public void HandleDecline()
    {
        CloseDialogue();
        onDeclineCallback?.Invoke();
    }

    public void CloseDialogue()
    {
        if (!IsInDialogue) return;
        IsInDialogue = false;
        isSuspended = false;

        if (dialogueNextTriangle != null) dialogueNextTriangle.SetActive(false);
        OnTypingStateChanged?.Invoke(false);

        CompleteTypewriterInstantly();
        if (punchRoutine != null) StopRunningCoroutine(punchRoutine);

        if (acceptKeyCap != null)
        {
            acceptKeyCap.SetPrompt("E", "Accept");
            acceptKeyCap.SetLocked(false);
        }
        if (notNowKeyCap != null)
        {
            notNowKeyCap.SetPrompt("Q", "Close");
            notNowKeyCap.SetLocked(false);
        }

        if (fadeRoutine != null) StopRunningCoroutine(fadeRoutine);
        fadeRoutine = RunCoroutine(FadeDialoguePanelRoutine(0f, panelCloseDuration));

        FadeHud(true);
        if (ModalManager.Instance != null && ModalManager.Instance.ActiveModal?.modalObject == gameObject)
        {
            ModalManager.Instance.CloseModal(gameObject);
        }
        OnDialogueEnded?.Invoke();
    }

    private IEnumerator FadeDialoguePanelRoutine(float targetAlpha, float duration)
    {
        if (dialogueCanvasGroup == null) yield break;

        bool isOpening = targetAlpha > 0.5f;
        float startAlpha = dialogueCanvasGroup.alpha;
        float startScale = isOpening
            ? ((dialoguePanelRect != null && dialoguePanelRect.localScale.x > 0.5f && dialoguePanelRect.localScale.x < 0.99f && startAlpha > 0.05f) ? dialoguePanelRect.localScale.x : transitionStartScale)
            : (dialoguePanelRect != null ? dialoguePanelRect.localScale.x : 1f);
        float endScale = isOpening ? 1f : transitionStartScale;
        float elapsed = 0f;

        if (isOpening && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        // Disable raycasts/interactivity during transition to prevent rapid clicks/skips while animating
        dialogueCanvasGroup.interactable = false;
        dialogueCanvasGroup.blocksRaycasts = false;
        if (dialoguePanelRect != null)
        {
            dialoguePanelRect.localScale = new Vector3(startScale, startScale, 1f);
        }

        while (elapsed < duration)
        {
            if (isSuspended)
            {
                dialogueCanvasGroup.alpha = 0f;
                dialogueCanvasGroup.interactable = false;
                dialogueCanvasGroup.blocksRaycasts = false;
                if (dialoguePanelRect != null)
                {
                    dialoguePanelRect.localScale = Vector3.one;
                    dialoguePanelRect.gameObject.SetActive(false);
                }
                gameObject.SetActive(false);
                yield break;
            }
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = isOpening ? (1f - Mathf.Pow(1f - t, 3f)) : (t * t);

            dialogueCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, ease);
            if (dialoguePanelRect != null)
            {
                float s = Mathf.Lerp(startScale, endScale, ease);
                dialoguePanelRect.localScale = new Vector3(s, s, 1f);
            }
            yield return null;
        }

        if (isSuspended)
        {
            dialogueCanvasGroup.alpha = 0f;
            dialogueCanvasGroup.interactable = false;
            dialogueCanvasGroup.blocksRaycasts = false;
            if (dialoguePanelRect != null)
            {
                dialoguePanelRect.localScale = Vector3.one;
                dialoguePanelRect.gameObject.SetActive(false);
            }
            gameObject.SetActive(false);
            yield break;
        }

        dialogueCanvasGroup.alpha = targetAlpha;
        dialogueCanvasGroup.interactable = isOpening;
        dialogueCanvasGroup.blocksRaycasts = isOpening;
        if (dialoguePanelRect != null)
        {
            dialoguePanelRect.localScale = Vector3.one;
        }

        if (!isOpening)
        {
            if (choiceButtonsRoot != null) choiceButtonsRoot.gameObject.SetActive(false);
            if (continuePromptRoot != null) continuePromptRoot.gameObject.SetActive(false);
            if (dialoguePanelRect != null) dialoguePanelRect.gameObject.SetActive(false);
            gameObject.SetActive(false);
            UIOrchestrator.Instance?.ApplyCurrentProfile(0.20f);
        }
    }

    private IEnumerator PunchSpeakerTabRoutine()
    {
        if (speakerTabRect == null) yield break;
        float elapsed = 0f;
        float duration = 0.18f;
        Vector3 baseScale = Vector3.one;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            float s = 1f + 0.10f * Mathf.Sin(t * Mathf.PI);
            speakerTabRect.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        speakerTabRect.localScale = baseScale;
    }

    private void FadeHud(bool show)
    {
        if (!show)
        {
            HudVisibilityService.Instance?.HideHUD("Dialogue");
        }
        else
        {
            HudVisibilityService.Instance?.ShowHUD("Dialogue");
        }
    }

    /// <summary>
    /// Text-Fit Guard: Splits input into pages by sentence, then word, using GetPreferredValues.
    /// Guarantees that each page has at most maxLines (3 lines) at targetFontSize (34px) within bodyWidth.
    /// </summary>
    public static List<string> SplitIntoPagesWithFitGuard(
        string rawText,
        TMP_Text tmpMeasurer,
        float bodyWidth,
        int maxLines = 3,
        float targetFontSize = 34f)
    {
        List<string> pages = new List<string>();
        if (string.IsNullOrWhiteSpace(rawText)) return pages;

        rawText = rawText.Trim();

        // If no TMP measurer available or bodyWidth <= 0, fallback to safe char split (approx 120 chars)
        if (tmpMeasurer == null || bodyWidth <= 50f)
        {
            return FallbackCharSplit(rawText, 120);
        }

        // Measure single line height with the measurer
        float oldSize = tmpMeasurer.fontSize;
        tmpMeasurer.fontSize = targetFontSize;
        Vector2 singleLine = tmpMeasurer.GetPreferredValues("Mg", bodyWidth, 0);
        float lineHeight = singleLine.y > 0 ? singleLine.y : 42f;
        float maxHeight = lineHeight * (maxLines + 0.35f);

        // Check if entire text fits in one page
        Vector2 totalSize = tmpMeasurer.GetPreferredValues(rawText, bodyWidth, 0);
        if (totalSize.y <= maxHeight)
        {
            tmpMeasurer.fontSize = oldSize;
            pages.Add(rawText);
            return pages;
        }

        // Split rawText into sentences
        List<string> sentences = SplitSentences(rawText);
        System.Text.StringBuilder currentPage = new System.Text.StringBuilder();

        foreach (var sentence in sentences)
        {
            string testCandidate = currentPage.Length == 0 ? sentence : currentPage.ToString() + " " + sentence;
            Vector2 size = tmpMeasurer.GetPreferredValues(testCandidate, bodyWidth, 0);

            if (size.y <= maxHeight)
            {
                if (currentPage.Length > 0) currentPage.Append(" ");
                currentPage.Append(sentence);
            }
            else
            {
                // The candidate exceeds 3 lines.
                // If currentPage already has content, flush it and test sentence alone.
                if (currentPage.Length > 0)
                {
                    pages.Add(currentPage.ToString().Trim());
                    currentPage.Clear();

                    Vector2 singleSentenceSize = tmpMeasurer.GetPreferredValues(sentence, bodyWidth, 0);
                    if (singleSentenceSize.y <= maxHeight)
                    {
                        currentPage.Append(sentence);
                        continue;
                    }
                }

                // Sentence alone is longer than 3 lines: split by word
                string[] words = sentence.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var w in words)
                {
                    string wordCandidate = currentPage.Length == 0 ? w : currentPage.ToString() + " " + w;
                    Vector2 wSize = tmpMeasurer.GetPreferredValues(wordCandidate, bodyWidth, 0);

                    if (wSize.y <= maxHeight)
                    {
                        if (currentPage.Length > 0) currentPage.Append(" ");
                        currentPage.Append(w);
                    }
                    else
                    {
                        if (currentPage.Length > 0)
                        {
                            pages.Add(currentPage.ToString().Trim());
                            currentPage.Clear();
                        }
                        currentPage.Append(w);
                    }
                }
            }
        }

        if (currentPage.Length > 0)
        {
            pages.Add(currentPage.ToString().Trim());
        }

        tmpMeasurer.fontSize = oldSize;
        return pages;
    }

    private static List<string> SplitSentences(string text)
    {
        List<string> list = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if ((c == '.' || c == '!' || c == '?') && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1])))
            {
                list.Add(text.Substring(start, i - start + 1).Trim());
                start = i + 1;
            }
        }
        if (start < text.Length)
        {
            string rem = text.Substring(start).Trim();
            if (!string.IsNullOrEmpty(rem)) list.Add(rem);
        }
        return list;
    }

    public static List<string> SplitIntoPages(string text, int maxChars = 120)
    {
        return FallbackCharSplit(text, maxChars);
    }

    private static List<string> FallbackCharSplit(string text, int maxChars = 120)
    {
        List<string> pages = new List<string>();
        string[] words = text.Split(' ');
        System.Text.StringBuilder current = new System.Text.StringBuilder();

        foreach (var w in words)
        {
            if (current.Length + w.Length + 1 > maxChars && current.Length > 0)
            {
                pages.Add(current.ToString().Trim());
                current.Clear();
            }
            if (current.Length > 0) current.Append(" ");
            current.Append(w);
        }

        if (current.Length > 0) pages.Add(current.ToString().Trim());
        return pages;
    }
}
