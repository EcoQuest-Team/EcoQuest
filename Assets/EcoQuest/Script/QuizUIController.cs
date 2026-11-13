using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class QuizUIController : MonoBehaviour
{
    [Header("Root & Overlay References")]
    [SerializeField] private GameObject quizCanvasRoot;
    [SerializeField] private CanvasGroup overlayCanvasGroup;
    [SerializeField] private RectTransform modalPanelRect;
    [SerializeField] private CanvasGroup modalPanelCanvasGroup;
    [SerializeField] private GameObject quizPanel;

    [Header("Header Elements")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private Image progressBarFill;

    [Header("Question Content")]
    [SerializeField] private RectTransform questionContainerRect;
    [SerializeField] private CanvasGroup questionContainerCanvasGroup;
    [SerializeField] private TextMeshProUGUI questionText;
    public UnityEngine.UI.Image questionIllustrationImage;

    [Header("Answer Buttons")]
    [SerializeField] private RectTransform answersContainer;
    [SerializeField] private Button[] answerButtons;
    [SerializeField] private TextMeshProUGUI[] answerButtonTexts;
    [SerializeField] private Image[] answerButtonImages;
    [SerializeField] private CanvasGroup[] answerButtonCanvasGroups;
    [SerializeField] private GameObject[] answerCheckmarks;
    [SerializeField] private GameObject[] answerCrosses;

    [Header("Feedback Panel (Post-Quiz Only)")]
    [SerializeField] private GameObject feedbackPanel;
    [SerializeField] private CanvasGroup feedbackCanvasGroup;
    [SerializeField] private TextMeshProUGUI feedbackTitleText;
    [SerializeField] private TextMeshProUGUI explanationText;
    [SerializeField] private Button nextQuestionButton;

    [Header("Results Panel")]
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private CanvasGroup resultsCanvasGroup;
    [SerializeField] private TextMeshProUGUI resultsTitleText;
    [SerializeField] private TextMeshProUGUI preScoreText;
    [SerializeField] private TextMeshProUGUI postScoreText;
    [SerializeField] private RectTransform improvementBadgeRect;
    [SerializeField] private TextMeshProUGUI improvementText;
    [SerializeField] private TextMeshProUGUI encouragingMessageText;
    [SerializeField] private TextMeshProUGUI bonusGoldText;
    [SerializeField] private Button continueButton;
    [SerializeField] private GameObject celebratoryParticlesGroup;
    public RectTransform rewardGiftIcon;

    [Header("Quiz Mode")]
    public bool isBonusQuizMode = false;

    [Header("Colors")]
    [SerializeField] private Color normalButtonColor = new Color(0.96f, 0.93f, 0.88f, 1f);
    [SerializeField] private Color correctHighlightColor = Color.white;
    [SerializeField] private Color dimmedButtonColor = new Color(0.80f, 0.80f, 0.80f, 1f);
    [SerializeField] private Color correctColor = new Color(0.24f, 0.68f, 0.32f, 1f);
    [SerializeField] private Color wrongColor = new Color(0.86f, 0.28f, 0.24f, 1f);
    [SerializeField] private Color normalTextColor = new Color(0.23f, 0.16f, 0.10f, 1f);
    [SerializeField] private Color highlightedTextColor = Color.white;

    [Header("Transition Settings (Unscaled Time)")]
    [SerializeField] private float modalOpenDuration = 0.28f;
    [SerializeField] private float modalCloseDuration = 0.18f;
    [SerializeField] private float modalStartScale = 0.95f;
    [SerializeField] private AnimationCurve modalOpenCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve modalCloseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private bool isAnimating = false;
    public bool IsAnimating => isAnimating;
    public static bool IsAnyQuizOpen { get; private set; }
    private Action<int> onAnswerSelectedCallback;
    private Action onNextQuestionCallback;
    private Action onResultsClosedCallback;
    private Vector2 questionContainerBasePos;
    private bool hasRecordedQuestionBasePos = false;
    private Coroutine rewardGiftCoroutine;
    private Vector2 rewardGiftBasePos = new Vector2(-675f, 14f);
    private bool hasRecordedGiftBasePos = false;
    private bool hasRecordedAnswersBaseRect = false;
    private float answersBaseTopY;
    private float answersBaseHeight;
    private Vector2 answersBaseAnchoredPos;
    private Vector2 answersBaseOffsetMin;
    private Vector2 answersBaseOffsetMax;
    private StarterAssets.StarterAssetsInputs cachedStarterInputs;

    private void Awake()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("City"))
        {
            isBonusQuizMode = true;
        }

        if (questionContainerRect != null && !hasRecordedQuestionBasePos)
        {
            questionContainerBasePos = questionContainerRect.anchoredPosition;
            hasRecordedQuestionBasePos = true;
        }
        if (rewardGiftIcon != null && !hasRecordedGiftBasePos)
        {
            rewardGiftBasePos = rewardGiftIcon.anchoredPosition;
            hasRecordedGiftBasePos = true;
        }

        // Wire button clicks
        if (answerButtons != null)
        {
            for (int i = 0; i < answerButtons.Length; i++)
            {
                int index = i;
                if (answerButtons[i] != null)
                {
                    answerButtons[i].onClick.AddListener(() => HandleAnswerClicked(index));
                    if (answerButtons[i].GetComponent<QuizButtonHoverEffect>() == null)
                    {
                        answerButtons[i].gameObject.AddComponent<QuizButtonHoverEffect>();
                    }
                }
            }
        }

        ApplyThemeStyling();

        if (nextQuestionButton != null)
        {
            nextQuestionButton.onClick.AddListener(() => HandleNextClicked());
            if (nextQuestionButton.GetComponent<QuizButtonHoverEffect>() == null)
            {
                nextQuestionButton.gameObject.AddComponent<QuizButtonHoverEffect>();
            }
        }

        if (continueButton != null)
        {
            continueButton.onClick.AddListener(() => HandleContinueClicked());
            if (continueButton.GetComponent<QuizButtonHoverEffect>() == null)
            {
                continueButton.gameObject.AddComponent<QuizButtonHoverEffect>();
            }
        }

        // Initially hide when Play starts at runtime
        HideImmediate();
    }

    public struct FeedbackColumnLayout
    {
        public Vector2 CenterPosition;
        public Vector2 Size;
    }

    public static FeedbackColumnLayout ComputeFeedbackColumnLayout(
        float columnLeftX,
        float columnRightX,
        float lowestAnswerBottomY,
        float modalBottomY,
        float desiredHeight = 148f,
        float minBottomMargin = 16f,
        float gapBelowAnswers = 12f,
        float minPanelHeight = 112f,
        float maxPanelHeight = 172f)
    {
        float colWidth = Mathf.Max(320f, columnRightX - columnLeftX);
        float centerX = (columnLeftX + columnRightX) * 0.5f;

        float safeBottomY = modalBottomY + minBottomMargin;
        float topAllowedY = lowestAnswerBottomY - gapBelowAnswers;
        float availableSpan = Mathf.Max(0f, topAllowedY - safeBottomY);

        float panelHeight;
        float panelBottomY;
        if (availableSpan >= minPanelHeight)
        {
            panelHeight = Mathf.Clamp(desiredHeight, minPanelHeight, Mathf.Min(maxPanelHeight, availableSpan));
            // Place directly below the lowest active answer option while keeping bottom safely inside modal
            panelBottomY = Mathf.Max(safeBottomY, topAllowedY - panelHeight);
        }
        else
        {
            // Never overlap the lowest active answer button; fit within remaining span
            panelHeight = Mathf.Max(88f, availableSpan);
            panelBottomY = topAllowedY - panelHeight;
        }

        return new FeedbackColumnLayout
        {
            CenterPosition = new Vector2(centerX, panelBottomY),
            Size = new Vector2(colWidth, panelHeight)
        };
    }

    private static float ScaledFont(float baseSize)
    {
        return Mathf.Round(baseSize * SettingsManager.CurrentTextScale * 10f) / 10f;
    }

    public void ApplyTextSizeScale(HashSet<int> managedTmpIds = null)
    {
        RegisterScaledTmp(managedTmpIds, questionText, 34f);
        RegisterScaledTmp(managedTmpIds, feedbackTitleText, 22f);
        if (explanationText != null)
        {
            if (managedTmpIds != null) managedTmpIds.Add(explanationText.GetInstanceID());
            explanationText.fontSize = ScaledFont(18f);
            if (explanationText.enableAutoSizing)
            {
                explanationText.fontSizeMin = ScaledFont(15.5f);
                explanationText.fontSizeMax = ScaledFont(19.5f);
            }
        }
        RegisterScaledTmp(managedTmpIds, resultsTitleText, 35f);
        RegisterScaledTmp(managedTmpIds, preScoreText, 26f);
        RegisterScaledTmp(managedTmpIds, postScoreText, 26f);
        RegisterScaledTmp(managedTmpIds, improvementText, 26f);
        RegisterScaledTmp(managedTmpIds, encouragingMessageText, 24f);
        RegisterScaledTmp(managedTmpIds, bonusGoldText, 25f);
        if (continueButton != null)
        {
            var btnTxt = continueButton.GetComponentInChildren<TextMeshProUGUI>();
            RegisterScaledTmp(managedTmpIds, btnTxt, 24f);
        }
    }

    private static void RegisterScaledTmp(HashSet<int> managedTmpIds, TextMeshProUGUI tmp, float baseSize)
    {
        if (tmp == null) return;
        if (managedTmpIds != null) managedTmpIds.Add(tmp.GetInstanceID());
        tmp.fontSize = ScaledFont(baseSize);
    }

    private void StyleAndPositionFeedbackPanel(Color? accentColor = null)
    {
        if (feedbackPanel == null) return;

        var fbImg = feedbackPanel.GetComponent<Image>();
        if (fbImg != null && UITheme.Instance != null && UITheme.Instance.panelBackgroundSprite != null)
        {
            fbImg.sprite = UITheme.Instance.panelBackgroundSprite;
            fbImg.type = Image.Type.Sliced;
            fbImg.color = Color.white;
        }

        // Left colored AccentBar (ensured consistently across Forest & City)
        Transform accentTr = feedbackPanel.transform.Find("AccentBar");
        if (accentTr == null)
        {
            var barGo = new GameObject("AccentBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            barGo.transform.SetParent(feedbackPanel.transform, false);
            accentTr = barGo.transform;
            accentTr.SetAsFirstSibling();
        }
        var barRt = accentTr as RectTransform;
        if (barRt != null)
        {
            barRt.anchorMin = new Vector2(0f, 0f);
            barRt.anchorMax = new Vector2(0f, 1f);
            barRt.pivot = new Vector2(0.5f, 0.5f);
            barRt.anchoredPosition = new Vector2(14f, 0f);
            barRt.sizeDelta = new Vector2(6f, -24f);
        }
        if (accentColor.HasValue)
        {
            var barImg = accentTr.GetComponent<Image>();
            if (barImg != null) barImg.color = accentColor.Value;
        }

        if (feedbackTitleText != null)
        {
            var titleFont = UITheme.Instance != null ? UITheme.Instance.GetTitleFont() : null;
            if (titleFont != null) feedbackTitleText.font = titleFont;
            feedbackTitleText.fontSize = ScaledFont(22f);
            feedbackTitleText.fontStyle = FontStyles.Bold;
            feedbackTitleText.alignment = TextAlignmentOptions.MidlineLeft;
            var rt = feedbackTitleText.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(30f, -44f);
            rt.offsetMax = new Vector2(-216f, -12f);
        }

        if (explanationText != null)
        {
            var bodyFont = UITheme.Instance != null ? UITheme.Instance.GetBodyFont() : null;
            if (bodyFont != null) explanationText.font = bodyFont;
            explanationText.fontSize = ScaledFont(18f);
            explanationText.enableAutoSizing = true;
            explanationText.fontSizeMin = ScaledFont(15.5f);
            explanationText.fontSizeMax = ScaledFont(19.5f);
            explanationText.lineSpacing = 2f;
            explanationText.color = UITheme.Instance != null ? UITheme.Instance.textMuted : Color.white;
            explanationText.textWrappingMode = TextWrappingModes.Normal;
            explanationText.overflowMode = TextOverflowModes.Ellipsis;
            explanationText.alignment = TextAlignmentOptions.TopLeft;
            var rt = explanationText.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(30f, 14f);
            rt.offsetMax = new Vector2(-216f, -48f);
        }

        if (nextQuestionButton != null)
        {
            var nextRt = nextQuestionButton.GetComponent<RectTransform>();
            if (nextRt != null)
            {
                nextRt.anchorMin = new Vector2(1f, 0.5f);
                nextRt.anchorMax = new Vector2(1f, 0.5f);
                nextRt.pivot = new Vector2(1f, 0.5f);
                nextRt.anchoredPosition = new Vector2(-20f, 0f);
            }

            var btnImg = nextQuestionButton.GetComponent<Image>();
            if (btnImg != null && UITheme.Instance != null)
            {
                btnImg.color = UITheme.Instance.successColor;
            }
            var btnTxt = nextQuestionButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btnTxt != null)
            {
                var titleFont = UITheme.Instance != null ? UITheme.Instance.GetTitleFont() : null;
                if (titleFont != null) btnTxt.font = titleFont;
                btnTxt.color = Color.white;
                btnTxt.fontStyle = FontStyles.Bold;
            }
            var nextRtb = ResponsiveTextButton.AttachOrGet(nextQuestionButton);
            nextRtb.Configure(
                labelRef: btnTxt,
                backgroundRef: btnImg,
                minFont: 15f,
                maxFont: 22f,
                minBtnHeight: 48f,
                maxBtnHeight: 84f,
                minBtnWidth: 140f,
                maxBtnWidth: 184f,
                fitWidth: true,
                allowHeightGrow: true,
                padLeft: 18f,
                padRight: 18f,
                padVertical: 10f,
                manageMargins: true);
        }

        PositionFeedbackPanelInsideAnswersColumn();
    }

    private void PositionFeedbackPanelInsideAnswersColumn()
    {
        if (feedbackPanel == null) return;
        var fbRt = feedbackPanel.GetComponent<RectTransform>();
        if (fbRt == null || answersContainer == null) return;

        var fbParent = fbRt.parent as RectTransform;
        if (fbParent == null) return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(answersContainer);

        Vector3[] colCorners = new Vector3[4];
        answersContainer.GetWorldCorners(colCorners);
        float colLeftX = fbParent.InverseTransformPoint(colCorners[0]).x;
        float colRightX = fbParent.InverseTransformPoint(colCorners[2]).x;
        float lowestBottomY = ComputeLowestActiveButtonBottomY(fbParent, fbParent.InverseTransformPoint(colCorners[0]).y);
        float parentBottomY = fbParent.rect.yMin;
        // If space below answers is tight (e.g. City 620px modal with 4 rows), compact row height slightly before placing FeedbackPanel
        if (lowestBottomY - 12f - (parentBottomY + 16f) < 118f && answersContainer != null && answerButtons != null)
        {
            var vlg = answersContainer.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) vlg.spacing = 6f;
            int activeCount = 0;
            for (int i = 0; i < answerButtons.Length; i++)
            {
                if (answerButtons[i] == null || !answerButtons[i].gameObject.activeSelf) continue;
                activeCount++;
                var rtb = answerButtons[i].GetComponent<ResponsiveTextButton>();
                if (rtb != null) rtb.Configure(minBtnHeight: 46f, padVertical: 10f);
            }
            if (activeCount > 0) ResizeAnswersContainerToFit(activeCount);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(answersContainer);
            lowestBottomY = ComputeLowestActiveButtonBottomY(fbParent, lowestBottomY);
        }

        float desiredHeight = 148f;
        if (explanationText != null && colRightX > colLeftX)
        {
            float expAvailW = Mathf.Max(200f, (colRightX - colLeftX) - 246f);
            float prefH = explanationText.GetPreferredValues(explanationText.text ?? string.Empty, expAvailW, 0f).y;
            desiredHeight = Mathf.Clamp(Mathf.Ceil(prefH) + 64f, 128f, 172f);
        }

        FeedbackColumnLayout layout = ComputeFeedbackColumnLayout(
            colLeftX,
            colRightX,
            lowestBottomY,
            parentBottomY,
            desiredHeight);

        fbRt.anchorMin = new Vector2(0.5f, 0.5f);
        fbRt.anchorMax = new Vector2(0.5f, 0.5f);
        fbRt.pivot = new Vector2(0.5f, 0f);
        fbRt.sizeDelta = layout.Size;
        fbRt.anchoredPosition = layout.CenterPosition;
        LayoutRebuilder.ForceRebuildLayoutImmediate(fbRt);
    }

    private float ComputeLowestActiveButtonBottomY(RectTransform targetSpace, float fallbackY)
    {
        if (answerButtons == null || targetSpace == null) return fallbackY;
        float lowest = float.PositiveInfinity;
        Vector3[] corners = new Vector3[4];
        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (answerButtons[i] == null || !answerButtons[i].gameObject.activeSelf) continue;
            var brt = answerButtons[i].GetComponent<RectTransform>();
            if (brt == null) continue;
            brt.GetWorldCorners(corners);
            float y = targetSpace.InverseTransformPoint(corners[0]).y;
            if (y < lowest) lowest = y;
        }
        return float.IsInfinity(lowest) ? fallbackY : lowest;
    }

    public void ApplyThemeStyling()
    {
        bool useLegacy = UITheme.Instance != null && UITheme.Instance.useLegacyPopups;
        if (!useLegacy)
        {
            normalButtonColor = new Color(0.090f, 0.251f, 0.184f, 1f); // #17402F
            normalTextColor = Color.white;
            correctHighlightColor = new Color(0.180f, 0.545f, 0.341f, 1f); // #2E8B57
            dimmedButtonColor = new Color(0.090f, 0.251f, 0.184f, 0.85f);
            highlightedTextColor = Color.white;

            if (modalPanelRect != null)
            {
                var bg = modalPanelRect.GetComponent<Image>();
                if (bg != null && UITheme.Instance != null && UITheme.Instance.panelBackgroundSprite != null)
                {
                    bg.sprite = UITheme.Instance.panelBackgroundSprite;
                    bg.type = Image.Type.Sliced;
                    bg.color = Color.white;
                }

                EnsureQuizPillTab(modalPanelRect, "QUESTION 1 OF 5");
            }

            if (feedbackPanel != null)
            {
                StyleAndPositionFeedbackPanel();
                ConfigureAnswerButtonsLayout();
            }

            if (resultsPanel != null)
            {
                var rRect = resultsPanel.GetComponent<RectTransform>();
                if (rRect != null)
                {
                    var rBg = resultsPanel.GetComponent<Image>();
                    if (rBg != null && UITheme.Instance != null && UITheme.Instance.panelBackgroundSprite != null)
                    {
                        rBg.sprite = UITheme.Instance.panelBackgroundSprite;
                        rBg.type = Image.Type.Sliced;
                        rBg.color = Color.white;
                    }
                    EnsureQuizPillTab(rRect, "RESULTS");
                }

                if (resultsTitleText != null)
                {
                    var titleFont = UITheme.Instance != null ? UITheme.Instance.GetTitleFont() : null;
                    if (titleFont != null) resultsTitleText.font = titleFont;
                    resultsTitleText.color = Color.white;
                    resultsTitleText.fontSize = ScaledFont(35f);
                    resultsTitleText.fontStyle = FontStyles.Bold;
                }

                if (preScoreText != null)
                {
                    var bodyFont = UITheme.Instance != null ? UITheme.Instance.GetBodyFont() : null;
                    if (bodyFont != null) preScoreText.font = bodyFont;
                    preScoreText.color = UITheme.Instance != null ? UITheme.Instance.textMuted : Color.white;
                    preScoreText.fontSize = ScaledFont(26f);
                    preScoreText.fontStyle = FontStyles.Bold;
                }

                if (postScoreText != null)
                {
                    var bodyFont = UITheme.Instance != null ? UITheme.Instance.GetBodyFont() : null;
                    if (bodyFont != null) postScoreText.font = bodyFont;
                    postScoreText.color = new Color(0.29f, 0.88f, 0.46f, 1f); // Bright success green #4AE076
                    postScoreText.fontSize = ScaledFont(26f);
                    postScoreText.fontStyle = FontStyles.Bold;
                }

                if (improvementBadgeRect != null)
                {
                    var badgeImg = improvementBadgeRect.GetComponent<Image>();
                    if (badgeImg != null && UITheme.Instance != null && UITheme.Instance.speakerTabGreenSprite != null)
                    {
                        badgeImg.sprite = UITheme.Instance.speakerTabGreenSprite;
                        badgeImg.type = Image.Type.Sliced;
                        badgeImg.color = Color.white;
                    }
                }

                if (improvementText != null)
                {
                    var titleFont = UITheme.Instance != null ? UITheme.Instance.GetTitleFont() : null;
                    if (titleFont != null) improvementText.font = titleFont;
                    improvementText.color = Color.white;
                    improvementText.fontSize = ScaledFont(26f);
                    improvementText.fontStyle = FontStyles.Bold;
                }

                if (encouragingMessageText != null)
                {
                    var bodyFont = UITheme.Instance != null ? UITheme.Instance.GetBodyFont() : null;
                    if (bodyFont != null) encouragingMessageText.font = bodyFont;
                    encouragingMessageText.color = UITheme.Instance != null ? UITheme.Instance.textMuted : Color.white;
                    encouragingMessageText.fontSize = ScaledFont(24f);
                }

                if (bonusGoldText != null)
                {
                    var titleFont = UITheme.Instance != null ? UITheme.Instance.GetTitleFont() : null;
                    if (titleFont != null) bonusGoldText.font = titleFont;
                    bonusGoldText.color = UITheme.Instance != null ? UITheme.Instance.warnColor : new Color(0.95f, 0.76f, 0.31f, 1f);
                    bonusGoldText.fontSize = ScaledFont(25f);
                    bonusGoldText.fontStyle = FontStyles.Bold;
                }

                if (continueButton != null)
                {
                    var btnImg = continueButton.GetComponent<Image>();
                    if (btnImg != null && UITheme.Instance != null)
                    {
                        btnImg.color = UITheme.Instance.successColor;
                    }
                    var btnTxt = continueButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (btnTxt != null)
                    {
                        var titleFont = UITheme.Instance != null ? UITheme.Instance.GetTitleFont() : null;
                        if (titleFont != null) btnTxt.font = titleFont;
                        btnTxt.color = Color.white;
                        btnTxt.fontSize = ScaledFont(24f);
                        btnTxt.fontStyle = FontStyles.Bold;
                    }
                    var contRtb = ResponsiveTextButton.AttachOrGet(continueButton);
                    contRtb.Configure(
                        labelRef: btnTxt,
                        backgroundRef: btnImg,
                        minFont: ScaledFont(16f),
                        maxFont: ScaledFont(22f),
                        minBtnHeight: 50f,
                        maxBtnHeight: 96f,
                        minBtnWidth: 180f,
                        maxBtnWidth: 360f,
                        fitWidth: true,
                        allowHeightGrow: true,
                        padLeft: 24f,
                        padRight: 24f,
                        padVertical: 12f,
                        manageMargins: true);
                }
            }

            if (modalPanelCanvasGroup != null)
            {
                modalPanelCanvasGroup.alpha = 1f;
            }

            if (questionText != null)
            {
                questionText.color = Color.white;
                questionText.fontSize = ScaledFont(34f);
            }

            if (progressText != null)
            {
                progressText.color = new Color(0.890f, 0.960f, 0.890f, 1f); // #E3F5E3
            }

            if (questionIllustrationImage != null)
            {
                questionIllustrationImage.color = Color.white;
                questionIllustrationImage.preserveAspect = true;

                var frameOutline = questionIllustrationImage.GetComponent<UnityEngine.UI.Outline>();
                if (frameOutline != null) Destroy(frameOutline);

                if (questionIllustrationImage.sprite != null && questionIllustrationImage.sprite.texture != null)
                {
                    questionIllustrationImage.sprite.texture.filterMode = FilterMode.Bilinear;
                }
            }
        }
    }

    private void EnsureQuizPillTab(RectTransform parentRect, string defaultLabel)
    {
        if (parentRect == null) return;
        var tab = parentRect.Find("PillTab");
        if (tab == null)
        {
            var tabGo = new GameObject("PillTab", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            tabGo.transform.SetParent(parentRect, false);
            var tabRt = tabGo.GetComponent<RectTransform>();
            tabRt.anchorMin = new Vector2(0f, 1f);
            tabRt.anchorMax = new Vector2(0f, 1f);
            tabRt.pivot = new Vector2(0f, 0.5f);
            tabRt.anchoredPosition = new Vector2(32f, 0f);
            tabRt.sizeDelta = new Vector2(240f, 44f);

            var tabImg = tabGo.GetComponent<UnityEngine.UI.Image>();
            if (UITheme.Instance != null && UITheme.Instance.speakerTabGreenSprite != null)
            {
                tabImg.sprite = UITheme.Instance.speakerTabGreenSprite;
                tabImg.type = UnityEngine.UI.Image.Type.Sliced;
            }
            tabImg.color = Color.white;

            var txtGo = new GameObject("TabText", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(tabGo.transform, false);
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            var tText = txtGo.GetComponent<TextMeshProUGUI>();
            var titleFont = UITheme.Instance != null ? UITheme.Instance.GetTitleFont() : null;
            if (titleFont != null) tText.font = titleFont;
            tText.text = defaultLabel;
            tText.fontSize = ScaledFont(22f);
            tText.fontStyle = FontStyles.Bold;
            tText.alignment = TextAlignmentOptions.Center;
            tText.color = Color.white;
        }
        else
        {
            var tText = tab.Find("TabText")?.GetComponent<TextMeshProUGUI>();
            if (tText != null)
            {
                var titleFont = UITheme.Instance != null ? UITheme.Instance.GetTitleFont() : null;
                if (titleFont != null) tText.font = titleFont;
                tText.color = Color.white;
            }
        }
    }

    private void Update()
    {
        if (IsAnyQuizOpen)
        {
            if (cachedStarterInputs == null)
                cachedStarterInputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
            var inputs = cachedStarterInputs;
            if (Cursor.lockState != CursorLockMode.None || !Cursor.visible || (inputs != null && (inputs.cursorLocked || inputs.cursorInputForLook)))
            {
                CursorState.SetLocked(false, inputs);
            }
        }

        if (!IsAnyQuizOpen || isAnimating) return;

        // Number keys 1-4 for answers
        if (quizPanel != null && quizPanel.activeInHierarchy && (feedbackPanel == null || !feedbackPanel.activeInHierarchy))
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SelectButtonByIndex(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SelectButtonByIndex(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SelectButtonByIndex(2);
            else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) SelectButtonByIndex(3);
        }
        else if (feedbackPanel != null && feedbackPanel.activeInHierarchy)
        {
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                HandleNextClicked();
            }
        }
        else if (resultsPanel != null && resultsPanel.activeInHierarchy)
        {
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                HandleContinueClicked();
            }
        }
    }

    public void SimulateSelectAnswer(int index)
    {
        SelectButtonByIndex(index);
    }

    public void SimulateClickNext()
    {
        HandleNextClicked();
    }

    private void SelectButtonByIndex(int index)
    {
        if (answerButtons != null && index >= 0 && index < answerButtons.Length && answerButtons[index] != null && answerButtons[index].interactable)
        {
            HandleAnswerClicked(index);
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            IsAnyQuizOpen = false;
        }
        StopAndResetRewardGift();
    }

    public void StopAndResetRewardGift()
    {
        if (rewardGiftCoroutine != null)
        {
            StopCoroutine(rewardGiftCoroutine);
            rewardGiftCoroutine = null;
        }
        if (rewardGiftIcon != null)
        {
            if (!hasRecordedGiftBasePos)
            {
                rewardGiftBasePos = rewardGiftIcon.anchoredPosition;
                hasRecordedGiftBasePos = true;
            }
            QuizUIAnimator.StopTransitionsOn(rewardGiftIcon);
            rewardGiftIcon.localScale = Vector3.one;
            rewardGiftIcon.localEulerAngles = Vector3.zero;
            rewardGiftIcon.anchoredPosition = rewardGiftBasePos;
        }
    }

    /// <summary>
    /// Hides the Quiz modal at runtime when Play mode starts.
    /// Does not run in Edit mode.
    /// </summary>
    public void HideImmediate()
    {
        IsAnyQuizOpen = false;
        if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("City"))
        {
            isBonusQuizMode = false;
        }
        StopAndResetRewardGift();

        if (overlayCanvasGroup != null)
        {
            QuizUIAnimator.StopTransitionsOn( null, overlayCanvasGroup);
            overlayCanvasGroup.alpha = 0f;
            overlayCanvasGroup.gameObject.SetActive(false);
        }

        if (modalPanelCanvasGroup != null)
        {
            QuizUIAnimator.StopTransitionsOn(modalPanelRect, modalPanelCanvasGroup);
            modalPanelCanvasGroup.alpha = 0f;
        }
        if (modalPanelRect != null)
        {
            modalPanelRect.localScale = Vector3.one * 0.8f;
            modalPanelRect.gameObject.SetActive(false);
        }

        Transform quizModalTab = modalPanelRect != null ? modalPanelRect.Find("PillTab") : null;
        if (quizModalTab != null) quizModalTab.gameObject.SetActive(true);

        if (quizPanel != null) quizPanel.SetActive(true);
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (celebratoryParticlesGroup != null) celebratoryParticlesGroup.SetActive(false);
    }

    #region ContextMenu Preview Helpers (Edit Mode)
    [ContextMenu("Preview Quiz")]
    [ContextMenu("Preview Quiz Panel")]
    public void PreviewQuizPanel()
    {
        SetAllVisibleEditMode();
        Transform quizModalTab = modalPanelRect != null ? modalPanelRect.Find("PillTab") : null;
        if (quizModalTab != null) quizModalTab.gameObject.SetActive(true);
        if (quizPanel != null) quizPanel.SetActive(true);
        if (questionContainerRect != null) questionContainerRect.gameObject.SetActive(true);
        if (answersContainer != null) answersContainer.gameObject.SetActive(true);
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (celebratoryParticlesGroup != null) celebratoryParticlesGroup.SetActive(false);
        if (titleText != null) titleText.text = "Pre-Quiz: Initial Knowledge";

        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (answerButtonImages != null && i < answerButtonImages.Length && answerButtonImages[i] != null)
                answerButtonImages[i].color = normalButtonColor;
            if (answerButtonTexts != null && i < answerButtonTexts.Length && answerButtonTexts[i] != null)
                answerButtonTexts[i].color = normalTextColor;
            if (answerCheckmarks != null && i < answerCheckmarks.Length && answerCheckmarks[i] != null)
                answerCheckmarks[i].SetActive(false);
            if (answerCrosses != null && i < answerCrosses.Length && answerCrosses[i] != null)
                answerCrosses[i].SetActive(false);
        }
    }

    [ContextMenu("Preview Feedback")]
    [ContextMenu("Preview Feedback Panel")]
    public void PreviewFeedbackPanel()
    {
        SetAllVisibleEditMode();
        Transform quizModalTab = modalPanelRect != null ? modalPanelRect.Find("PillTab") : null;
        if (quizModalTab != null) quizModalTab.gameObject.SetActive(true);
        if (quizPanel != null) quizPanel.SetActive(true);
        if (questionContainerRect != null) questionContainerRect.gameObject.SetActive(true);
        if (answersContainer != null) answersContainer.gameObject.SetActive(true);
        if (feedbackPanel != null) feedbackPanel.SetActive(true);
        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (celebratoryParticlesGroup != null) celebratoryParticlesGroup.SetActive(false);
        if (titleText != null) titleText.text = "Post-Quiz: Master Verification";

        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (answerButtonImages != null && i < answerButtonImages.Length && answerButtonImages[i] != null)
                answerButtonImages[i].color = (i == 0) ? correctHighlightColor : dimmedButtonColor;
            if (answerButtonTexts != null && i < answerButtonTexts.Length && answerButtonTexts[i] != null)
                answerButtonTexts[i].color = (i == 0) ? highlightedTextColor : normalTextColor;
            if (answerCheckmarks != null && i < answerCheckmarks.Length && answerCheckmarks[i] != null)
                answerCheckmarks[i].SetActive(i == 0);
            if (answerCrosses != null && i < answerCrosses.Length && answerCrosses[i] != null)
                answerCrosses[i].SetActive(false);
        }
    }

    [ContextMenu("Preview Results")]
    [ContextMenu("Preview Results Panel")]
    public void PreviewResultsPanel()
    {
        SetAllVisibleEditMode();
        Transform quizModalTab = modalPanelRect != null ? modalPanelRect.Find("PillTab") : null;
        if (quizModalTab != null) quizModalTab.gameObject.SetActive(false);
        if (quizPanel != null) quizPanel.SetActive(false);
        if (questionContainerRect != null) questionContainerRect.gameObject.SetActive(false);
        if (answersContainer != null) answersContainer.gameObject.SetActive(false);
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(true);
        if (celebratoryParticlesGroup != null) celebratoryParticlesGroup.SetActive(false);
        if (resultsTitleText != null) resultsTitleText.text = isBonusQuizMode ? "Bonus Quiz Results" : "EcoQuest Learning Results";
        else if (titleText != null) titleText.text = isBonusQuizMode ? "Bonus Quiz Results" : "EcoQuest Learning Results";

        if (isBonusQuizMode)
        {
            if (preScoreText != null) preScoreText.gameObject.SetActive(false);
            if (postScoreText != null)
            {
                postScoreText.gameObject.SetActive(true);
                postScoreText.rectTransform.anchorMin = new Vector2(0f, 0.58f);
                postScoreText.rectTransform.anchorMax = new Vector2(1f, 0.74f);
                postScoreText.rectTransform.offsetMin = Vector2.zero;
                postScoreText.rectTransform.offsetMax = Vector2.zero;
                postScoreText.text = "Score: 5 / 5";
            }
            if (improvementBadgeRect != null)
            {
                improvementBadgeRect.anchorMin = new Vector2(0.5f, 0.48f);
                improvementBadgeRect.anchorMax = new Vector2(0.5f, 0.48f);
            }
            if (improvementText != null) improvementText.text = "Master Detective! (5/5)";
            if (encouragingMessageText != null)
            {
                encouragingMessageText.text = "Flawless! You correctly categorized every city trash item. Ranger Robin and the City are proud of you!";
                encouragingMessageText.rectTransform.anchorMin = new Vector2(0.08f, 0.26f);
                encouragingMessageText.rectTransform.anchorMax = new Vector2(0.92f, 0.42f);
            }
            if (bonusGoldText != null)
            {
                bonusGoldText.gameObject.SetActive(true);
                bonusGoldText.text = "+70 Bonus Gold Earned!";
            }
        }
        else if (titleText != null) titleText.text = "EcoQuest Learning Results";
    }

    [ContextMenu("Show All")]
    [ContextMenu("Show All (Every Object Visible)")]
    public void ShowAll()
    {
        SetAllVisibleEditMode();
        if (quizPanel != null) quizPanel.SetActive(true);
        if (questionContainerRect != null) questionContainerRect.gameObject.SetActive(true);
        if (answersContainer != null) answersContainer.gameObject.SetActive(true);
        if (feedbackPanel != null) feedbackPanel.SetActive(true);
        if (resultsPanel != null) resultsPanel.SetActive(true);
        if (celebratoryParticlesGroup != null) celebratoryParticlesGroup.SetActive(true);
    }

    public void SetAllVisibleEditMode()
    {
        if (quizCanvasRoot != null) quizCanvasRoot.SetActive(true);
        if (overlayCanvasGroup != null)
        {
            overlayCanvasGroup.gameObject.SetActive(true);
            overlayCanvasGroup.alpha = 1f;
        }
        if (modalPanelCanvasGroup != null)
        {
            modalPanelCanvasGroup.gameObject.SetActive(true);
            modalPanelCanvasGroup.alpha = 1f;
        }
        if (modalPanelRect != null)
        {
            modalPanelRect.gameObject.SetActive(true);
            modalPanelRect.localScale = Vector3.one;
        }

        if (questionContainerCanvasGroup != null) questionContainerCanvasGroup.alpha = 1f;
        if (feedbackCanvasGroup != null) feedbackCanvasGroup.alpha = 1f;
        if (resultsCanvasGroup != null) resultsCanvasGroup.alpha = 1f;

        if (answerButtonCanvasGroups != null)
        {
            foreach (var cg in answerButtonCanvasGroups)
            {
                if (cg != null) cg.alpha = 1f;
            }
        }
        if (answerButtons != null)
        {
            foreach (var btn in answerButtons)
            {
                if (btn != null)
                {
                    btn.gameObject.SetActive(true);
                    btn.transform.localScale = Vector3.one;
                }
            }
        }
    }
    #endregion

    /// <summary>
    /// Synchronously populates the first question's state on frame 0 before the modal becomes visible
    /// so there is never a 1-frame flash of stale text or buttons during the modal pop-in animation.
    /// </summary>
    public void PrepareInitialQuestion(string question, string[] options, int totalQuestions, Sprite questionImage = null)
    {
        if (questionContainerRect != null && !hasRecordedQuestionBasePos)
        {
            questionContainerBasePos = questionContainerRect.anchoredPosition;
            hasRecordedQuestionBasePos = true;
        }

        if (progressText != null)
        {
            progressText.text = $"Question 1 of {totalQuestions}";
        }
        if (progressBarFill != null)
        {
            progressBarFill.fillAmount = 0f;
        }
        if (questionText != null)
        {
            questionText.text = question;
        }
        if (questionIllustrationImage != null)
        {
            if (questionImage != null)
            {
                questionIllustrationImage.sprite = questionImage;
                questionIllustrationImage.gameObject.SetActive(true);
            }
            else
            {
                questionIllustrationImage.gameObject.SetActive(false);
            }
        }
        if (questionContainerRect != null)
        {
            questionContainerRect.anchoredPosition = questionContainerBasePos;
            if (questionContainerCanvasGroup != null) questionContainerCanvasGroup.alpha = 0f;
        }

        ConfigureAnswerButtonsLayout();

        int activeCount = 0;
        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (options != null && i < options.Length)
            {
                activeCount++;
                answerButtons[i].gameObject.SetActive(true);
                answerButtons[i].interactable = false;
                SetAnswerButtonText(i, options[i]);
                if (answerButtonImages[i] != null) answerButtonImages[i].color = normalButtonColor;
                if (answerCheckmarks != null && i < answerCheckmarks.Length && answerCheckmarks[i] != null)
                    answerCheckmarks[i].SetActive(false);
                if (answerCrosses != null && i < answerCrosses.Length && answerCrosses[i] != null)
                    answerCrosses[i].SetActive(false);
                if (answerButtonCanvasGroups != null && i < answerButtonCanvasGroups.Length && answerButtonCanvasGroups[i] != null)
                    answerButtonCanvasGroups[i].alpha = 0f;
            }
            else if (answerButtons[i] != null)
            {
                answerButtons[i].gameObject.SetActive(false);
            }
        }
        ResizeAnswersContainerToFit(activeCount);
    }

    /// <summary>
    /// Opens the Quiz modal with bouncy scale pop-in and dark overlay fade.
    /// </summary>
    public IEnumerator AnimateOpenModal(string title)
    {
        isAnimating = true;
        IsAnyQuizOpen = true;

        var openInputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
        CursorState.SetLocked(false, openInputs);

        // 1. Explicitly configure child sub-panels BEFORE enabling the modal
        if (quizPanel != null) quizPanel.SetActive(true);
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (celebratoryParticlesGroup != null) celebratoryParticlesGroup.SetActive(false);

        Transform quizModalTab = modalPanelRect != null ? modalPanelRect.Find("PillTab") : null;
        if (quizModalTab != null) quizModalTab.gameObject.SetActive(true);

        if (questionContainerRect != null)
        {
            questionContainerRect.gameObject.SetActive(true);
        }
        if (answersContainer != null) answersContainer.gameObject.SetActive(true);

        if (progressBarFill != null && progressBarFill.transform.parent != null)
            progressBarFill.transform.parent.gameObject.SetActive(true);
        if (progressText != null) progressText.gameObject.SetActive(true);
        if (titleText != null) titleText.text = title;
        if (progressBarFill != null) progressBarFill.fillAmount = 0f;

        // 2. Synchronously initialize alpha=0 and starting scale=0.8 on frame 0 BEFORE enabling GameObjects
        if (quizCanvasRoot != null) quizCanvasRoot.SetActive(true);

        if (overlayCanvasGroup != null)
        {
            QuizUIAnimator.StopTransitionsOn(null, overlayCanvasGroup);
            overlayCanvasGroup.alpha = 0f;
            overlayCanvasGroup.gameObject.SetActive(true);
        }

        if (modalPanelRect != null)
        {
            QuizUIAnimator.StopTransitionsOn(modalPanelRect, modalPanelCanvasGroup);
            modalPanelRect.localScale = Vector3.one * modalStartScale;
            if (modalPanelCanvasGroup != null)
            {
                modalPanelCanvasGroup.alpha = 0f;
                modalPanelCanvasGroup.interactable = false;
                modalPanelCanvasGroup.blocksRaycasts = false;
            }
            modalPanelRect.gameObject.SetActive(true);
        }

        ApplyThemeStyling();
        ModalManager.Instance?.RequestModal(ModalManager.ModalType.Quiz, gameObject, null, null, priority: 6);

        // Overlay fade in + Modal pop-in (0.95 -> 1.0 over 0.28s)
        Coroutine overlayRoutine = StartCoroutine(QuizUIAnimator.FadeCanvasGroup(overlayCanvasGroup, 0f, 1f, modalOpenDuration));
        yield return QuizUIAnimator.PopInPanel(modalPanelRect, modalPanelCanvasGroup, modalStartScale, modalOpenDuration);
        yield return overlayRoutine;

        if (modalPanelCanvasGroup != null)
        {
            modalPanelCanvasGroup.interactable = true;
            modalPanelCanvasGroup.blocksRaycasts = true;
        }

        isAnimating = false;
    }

    /// <summary>
    /// Displays a question with smooth slide-in and staggered answer buttons.
    /// </summary>
    public IEnumerator AnimateDisplayQuestion(
        string question,
        string[] options,
        int questionIndex,
        int totalQuestions,
        bool isFirstQuestion,
        Action<int> onAnswerSelected,
        Sprite questionImage = null)
    {
        isAnimating = true;
        IsAnyQuizOpen = true;
        onAnswerSelectedCallback = onAnswerSelected;

        if (CenterScreenCursor.Instance != null)
        {
            CenterScreenCursor.Instance.SetVisible(false);
        }

        if (quizPanel != null) quizPanel.SetActive(true);
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (celebratoryParticlesGroup != null) celebratoryParticlesGroup.SetActive(false);

        Transform quizModalTab = modalPanelRect != null ? modalPanelRect.Find("PillTab") : null;
        if (quizModalTab != null) quizModalTab.gameObject.SetActive(true);

        if (questionContainerRect != null && !hasRecordedQuestionBasePos)
        {
            questionContainerBasePos = questionContainerRect.anchoredPosition;
            hasRecordedQuestionBasePos = true;
        }

        // Update progress indicators
        if (progressText != null)
        {
            progressText.text = $"Question {questionIndex + 1} of {totalQuestions}";
        }
        float targetProgress = (float)(questionIndex + 1) / totalQuestions;
        StartCoroutine(QuizUIAnimator.SmoothFill(progressBarFill, targetProgress, 0.3f));

        Vector2 basePos = questionContainerRect != null ? questionContainerBasePos : Vector2.zero;

        // Slide out previous question if not first
        if (!isFirstQuestion && questionContainerRect != null)
        {
            yield return QuizUIAnimator.SlideAndFade(
                questionContainerRect,
                questionContainerCanvasGroup,
                basePos,
                basePos + new Vector2(-120f, 0f),
                1f,
                0f,
                0.2f
            );
        }

        // Update question text and illustration
        if (modalPanelRect != null)
        {
            var tabText = modalPanelRect.Find("PillTab/TabText")?.GetComponent<TextMeshProUGUI>();
            if (tabText != null) tabText.text = $"QUESTION {questionIndex + 1} OF {totalQuestions}";
        }
        if (questionText != null)
        {
            questionText.text = question;
            questionText.color = Color.white;
        }
        if (progressText != null)
        {
            progressText.color = new Color(0.890f, 0.960f, 0.890f, 1f);
        }

        if (questionIllustrationImage != null)
        {
            if (questionImage != null)
            {
                questionIllustrationImage.sprite = questionImage;
                questionIllustrationImage.gameObject.SetActive(true);
            }
            else
            {
                questionIllustrationImage.gameObject.SetActive(false);
            }
        }

        ConfigureAnswerButtonsLayout();

        // Reset and populate answer buttons
        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (i < options.Length)
            {
                QuizUIAnimator.StopTransitionsOn(
                    answerButtons[i] != null ? answerButtons[i].GetComponent<RectTransform>() : null,
                    (answerButtonCanvasGroups != null && i < answerButtonCanvasGroups.Length) ? answerButtonCanvasGroups[i] : null
                );

                answerButtons[i].gameObject.SetActive(true);
                answerButtons[i].interactable = true;
                SetAnswerButtonText(i, options[i]);
                if (answerButtonImages[i] != null)
                {
                    answerButtonImages[i].color = normalButtonColor;
                }
                if (answerCheckmarks != null && i < answerCheckmarks.Length && answerCheckmarks[i] != null)
                    answerCheckmarks[i].SetActive(false);
                if (answerCrosses != null && i < answerCrosses.Length && answerCrosses[i] != null)
                    answerCrosses[i].SetActive(false);

                var hover = answerButtons[i].GetComponent<QuizButtonHoverEffect>();
                if (hover != null) hover.ResetState();

                if (answerButtonCanvasGroups != null && i < answerButtonCanvasGroups.Length && answerButtonCanvasGroups[i] != null)
                {
                    answerButtonCanvasGroups[i].alpha = 0f;
                }
            }
            else
            {
                if (answerButtons[i] != null) answerButtons[i].gameObject.SetActive(false);
            }
        }

        ResizeAnswersContainerToFit(options.Length);

        // Slide in question container from right
        if (questionContainerRect != null)
        {
            yield return QuizUIAnimator.SlideAndFade(
                questionContainerRect,
                questionContainerCanvasGroup,
                basePos + new Vector2(120f, 0f),
                basePos,
                0f,
                1f,
                0.25f
            );
        }

        // Staggered entry for answer buttons (0.08s each)
        for (int i = 0; i < options.Length; i++)
        {
            if (i < answerButtonCanvasGroups.Length && answerButtonCanvasGroups[i] != null)
            {
                StartCoroutine(QuizUIAnimator.FadeCanvasGroup(answerButtonCanvasGroups[i], 0f, 1f, 0.15f));
                StartCoroutine(QuizUIAnimator.PulseScale(answerButtons[i].GetComponent<RectTransform>(), 1.03f, 0.15f));
            }
            yield return new WaitForSecondsRealtime(0.08f);
        }

        isAnimating = false;
    }

    private void HandleAnswerClicked(int optionIndex)
    {
        if (isAnimating) return;
        onAnswerSelectedCallback?.Invoke(optionIndex);
    }

    private void ConfigureAnswerButtonsLayout()
    {
        if (answersContainer != null)
        {
            var vlg = answersContainer.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = answersContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            if (!hasRecordedAnswersBaseRect)
            {
                answersBaseAnchoredPos = answersContainer.anchoredPosition;
                answersBaseHeight = answersContainer.rect.height > 10f ? answersContainer.rect.height : answersContainer.sizeDelta.y;
                answersBaseOffsetMin = answersContainer.offsetMin;
                answersBaseOffsetMax = answersContainer.offsetMax;
                answersBaseTopY = answersBaseAnchoredPos.y + (1f - answersContainer.pivot.y) * answersBaseHeight;
                hasRecordedAnswersBaseRect = true;
            }
        }

        if (answerButtons == null) return;
        Sprite slicedRounded = UITheme.Instance != null ? UITheme.Instance.roundedButtonSprite : null;
        for (int i = 0; i < answerButtons.Length; i++)
        {
            Button btn = answerButtons[i];
            if (btn == null) continue;

            // Clear scene transform tilt and stray Z offset artifacts
            btn.transform.localRotation = Quaternion.identity;
            TMP_Text txt = (answerButtonTexts != null && i < answerButtonTexts.Length) ? answerButtonTexts[i] : btn.GetComponentInChildren<TMP_Text>(true);
            if (txt != null)
            {
                Vector3 lp = txt.rectTransform.localPosition;
                if (Mathf.Abs(lp.z) > 0.01f) txt.rectTransform.localPosition = new Vector3(lp.x, lp.y, 0f);
                txt.alignment = TextAlignmentOptions.MidlineLeft;
            }

            Image bg = (answerButtonImages != null && i < answerButtonImages.Length && answerButtonImages[i] != null)
                ? answerButtonImages[i]
                : btn.GetComponent<Image>();

            var rtb = ResponsiveTextButton.AttachOrGet(btn);
            rtb.Configure(
                labelRef: txt,
                backgroundRef: bg,
                minFont: 16f,
                maxFont: 23f,
                minBtnHeight: 54f,
                maxBtnHeight: 136f,
                minBtnWidth: 260f,
                maxBtnWidth: 960f,
                fitWidth: false,
                allowHeightGrow: true,
                padLeft: 24f,
                padRight: 58f,
                padVertical: 14f,
                manageMargins: true,
                useSharedRounded: true,
                slicedSprite: slicedRounded);
        }
    }

    private void SetAnswerButtonText(int index, string rawOption)
    {
        if (answerButtons == null || index < 0 || index >= answerButtons.Length || answerButtons[index] == null) return;
        string prefix = $"{(char)('A' + index)}) ";
        string formatted = (!string.IsNullOrEmpty(rawOption) && rawOption.StartsWith(prefix)) ? rawOption : prefix + (rawOption ?? string.Empty);

        if (answerButtonTexts != null && index < answerButtonTexts.Length && answerButtonTexts[index] != null)
        {
            answerButtonTexts[index].color = normalTextColor;
        }

        var rtb = ResponsiveTextButton.AttachOrGet(answerButtons[index]);
        rtb.SetText(formatted);
    }

    private void ResizeAnswersContainerToFit(int activeCount)
    {
        if (answersContainer == null || answerButtons == null) return;

        var vlg = answersContainer.GetComponent<VerticalLayoutGroup>();
        float spacing = vlg != null ? vlg.spacing : 8f;
        float padV = vlg != null ? (vlg.padding.top + vlg.padding.bottom) : 0f;

        float totalH = padV;
        int visible = 0;
        for (int i = 0; i < answerButtons.Length && i < activeCount; i++)
        {
            if (answerButtons[i] == null || !answerButtons[i].gameObject.activeSelf) continue;
            var rtb = answerButtons[i].GetComponent<ResponsiveTextButton>();
            float h = rtb != null && rtb.LastMetrics.TargetHeight > 1f ? rtb.LastMetrics.TargetHeight : 58f;
            totalH += h;
            visible++;
        }
        if (visible > 1) totalH += (visible - 1) * spacing;

        float targetH = Mathf.Max(58f, totalH);
        if (Mathf.Approximately(answersContainer.anchorMin.y, answersContainer.anchorMax.y))
        {
            float pivY = answersContainer.pivot.y;
            float baseTop = hasRecordedAnswersBaseRect
                ? answersBaseTopY
                : answersContainer.anchoredPosition.y + (1f - pivY) * answersContainer.sizeDelta.y;
            answersContainer.sizeDelta = new Vector2(answersContainer.sizeDelta.x, targetH);
            answersContainer.anchoredPosition = new Vector2(answersContainer.anchoredPosition.x, baseTop - (1f - pivY) * targetH);
        }
        else if (hasRecordedAnswersBaseRect)
        {
            var parentRt = answersContainer.parent as RectTransform;
            float parentH = parentRt != null && parentRt.rect.height > 10f ? parentRt.rect.height : 572f;
            float topY = answersContainer.anchorMax.y * parentH + answersBaseOffsetMax.y;
            float minAnchorY = answersContainer.anchorMin.y * parentH;
            answersContainer.offsetMax = answersBaseOffsetMax;
            answersContainer.offsetMin = new Vector2(answersBaseOffsetMin.x, topY - targetH - minAnchorY);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(answersContainer);
    }

    /// <summary>
    /// Displays post-quiz feedback: green pulse or red shake, explanation fade, and next button.
    /// </summary>
    public IEnumerator AnimatePostQuizFeedback(
        int chosenIndex,
        int correctIndex,
        bool isCorrect,
        string explanation,
        Action onNextClicked)
    {
        isAnimating = true;
        onNextQuestionCallback = onNextClicked;

        // Lock all answer buttons
        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (answerButtons[i] != null)
            {
                answerButtons[i].interactable = false;
                var hover = answerButtons[i].GetComponent<QuizButtonHoverEffect>();
                if (hover != null) hover.isInteractable = false;
            }
        }

        // Dim all non-chosen, non-correct buttons by 15-20% so correct answer stands out
        for (int i = 0; i < answerButtonImages.Length; i++)
        {
            if (answerButtonImages[i] != null && i != correctIndex && i != chosenIndex)
            {
                answerButtonImages[i].color = dimmedButtonColor;
            }
        }

        // Animate chosen & correct buttons
        if (isCorrect)
        {
            if (chosenIndex >= 0 && chosenIndex < answerButtonImages.Length)
            {
                // Correct answer is highlighted 15-20% lighter than other buttons
                answerButtonImages[chosenIndex].color = correctHighlightColor;
                if (answerButtonTexts[chosenIndex] != null) answerButtonTexts[chosenIndex].color = highlightedTextColor;
                if (answerCheckmarks != null && chosenIndex < answerCheckmarks.Length && answerCheckmarks[chosenIndex] != null)
                    answerCheckmarks[chosenIndex].SetActive(true);

                yield return QuizUIAnimator.PulseScale(answerButtons[chosenIndex].GetComponent<RectTransform>(), 1.08f, 0.25f);
            }
        }
        else
        {
            // Wrong answer shake and red flash
            if (chosenIndex >= 0 && chosenIndex < answerButtonImages.Length)
            {
                answerButtonImages[chosenIndex].color = wrongColor;
                if (answerButtonTexts[chosenIndex] != null) answerButtonTexts[chosenIndex].color = highlightedTextColor;
                if (answerCrosses != null && chosenIndex < answerCrosses.Length && answerCrosses[chosenIndex] != null)
                    answerCrosses[chosenIndex].SetActive(true);

                StartCoroutine(QuizUIAnimator.ShakeHorizontal(answerButtons[chosenIndex].GetComponent<RectTransform>(), 16f, 0.3f));
            }

            // Highlight the correct answer 15-20% lighter than the other buttons
            if (correctIndex >= 0 && correctIndex < answerButtonImages.Length)
            {
                answerButtonImages[correctIndex].color = correctHighlightColor;
                if (answerButtonTexts[correctIndex] != null) answerButtonTexts[correctIndex].color = highlightedTextColor;
                if (answerCheckmarks != null && correctIndex < answerCheckmarks.Length && answerCheckmarks[correctIndex] != null)
                    answerCheckmarks[correctIndex].SetActive(true);
            }

            yield return new WaitForSecondsRealtime(0.35f);
        }

        // Show feedback panel with explanation
        if (feedbackPanel != null)
        {
            feedbackPanel.SetActive(true);
            Color stateColor = isCorrect
                ? (UITheme.Instance != null ? UITheme.Instance.successColor : correctColor)
                : (UITheme.Instance != null ? UITheme.Instance.errorColor : wrongColor);

            if (feedbackTitleText != null)
            {
                feedbackTitleText.text = isCorrect ? "CORRECT!" : "NOT QUITE!";
                feedbackTitleText.color = stateColor;
            }
            if (explanationText != null)
            {
                explanationText.text = explanation;
                explanationText.color = UITheme.Instance != null ? UITheme.Instance.textMuted : Color.white;
            }

            StyleAndPositionFeedbackPanel(stateColor);

            if (feedbackCanvasGroup != null)
            {
                yield return QuizUIAnimator.FadeCanvasGroup(feedbackCanvasGroup, 0f, 1f, 0.25f);
            }
        }

        if (isCorrect)
        {
            yield return new WaitForSecondsRealtime(0.9f);
            HandleNextClicked();
        }

        isAnimating = false;
    }

    private void HandleNextClicked()
    {
        if (isAnimating) return;
        onNextQuestionCallback?.Invoke();
    }

    /// <summary>
    /// Displays results screen with score count-ups, improvement punch pop, and celebration.
    /// </summary>
    public IEnumerator AnimateResultsScreen(
        int preScore,
        int postScore,
        int totalQuestions,
        int bonusGold,
        Action onContinueClicked)
    {
        isAnimating = true;
        onResultsClosedCallback = onContinueClicked;

        // Hide quiz elements and show results panel (mutually exclusive)
        if (quizPanel != null) quizPanel.SetActive(false);
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
        if (questionContainerRect != null) questionContainerRect.gameObject.SetActive(false);
        if (answersContainer != null) answersContainer.gameObject.SetActive(false);
        if (progressBarFill != null && progressBarFill.transform.parent != null)
            progressBarFill.transform.parent.gameObject.SetActive(false);
        if (progressText != null) progressText.gameObject.SetActive(false);

        // Hide quiz modal tab so only the RESULTS tab is visible
        Transform quizModalTab = modalPanelRect != null ? modalPanelRect.Find("PillTab") : null;
        if (quizModalTab != null) quizModalTab.gameObject.SetActive(false);

        bool isCityScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("City");
        if (isBonusQuizMode || isCityScene || preScore == 0)
        {
            if (resultsTitleText != null)
            {
                resultsTitleText.text = "Bonus Quiz Results";
                resultsTitleText.color = Color.white;
            }
            else if (titleText != null)
            {
                titleText.text = "Bonus Quiz Results";
                titleText.color = Color.white;
            }

            if (resultsPanel != null) resultsPanel.SetActive(true);
            if (preScoreText != null)
            {
                preScoreText.gameObject.SetActive(false);
            }
            if (postScoreText != null)
            {
                postScoreText.gameObject.SetActive(true);
                postScoreText.rectTransform.anchorMin = new Vector2(0f, 0.58f);
                postScoreText.rectTransform.anchorMax = new Vector2(1f, 0.74f);
                postScoreText.rectTransform.offsetMin = Vector2.zero;
                postScoreText.rectTransform.offsetMax = Vector2.zero;
                postScoreText.fontSize = ScaledFont(28f);
                postScoreText.color = new Color(0.29f, 0.88f, 0.46f, 1f); // #4AE076
            }
            if (rewardGiftIcon != null)
            {
                StopAndResetRewardGift();
                rewardGiftIcon.gameObject.SetActive(true);
                rewardGiftCoroutine = StartCoroutine(QuizUIAnimator.AnimateRewardGift(rewardGiftIcon));
            }
            if (resultsCanvasGroup != null)
            {
                yield return QuizUIAnimator.FadeCanvasGroup(resultsCanvasGroup, 0f, 1f, 0.3f);
            }

            // Single score count-up for Bonus Quiz
            Coroutine bonusCount = StartCoroutine(QuizUIAnimator.CountUpText(postScoreText, "Score: ", postScore, $" / {totalQuestions}", 0.7f));
            yield return bonusCount;

            yield return new WaitForSecondsRealtime(0.15f);

            // Badge text & pill
            if (improvementBadgeRect != null)
            {
                improvementBadgeRect.anchorMin = new Vector2(0.5f, 0.48f);
                improvementBadgeRect.anchorMax = new Vector2(0.5f, 0.48f);
            }
            if (improvementText != null)
            {
                if (postScore == totalQuestions)
                {
                    improvementText.text = "Master Detective! (5/5)";
                    improvementText.color = Color.white;
                }
                else if (postScore >= 3)
                {
                    improvementText.text = $"Great Job! ({postScore}/{totalQuestions})";
                    improvementText.color = Color.white;
                }
                else
                {
                    improvementText.text = $"Quiz Complete ({postScore}/{totalQuestions})";
                    improvementText.color = UITheme.Instance != null ? UITheme.Instance.textMuted : Color.white;
                }
            }

            if (improvementBadgeRect != null)
            {
                yield return QuizUIAnimator.PulseScale(improvementBadgeRect, 1.25f, 0.35f);
            }

            // City-appropriate message (no forest!)
            if (encouragingMessageText != null)
            {
                encouragingMessageText.color = UITheme.Instance != null ? UITheme.Instance.textMuted : Color.white;
                encouragingMessageText.enableAutoSizing = true;
                encouragingMessageText.fontSizeMin = ScaledFont(12f);
                encouragingMessageText.fontSizeMax = ScaledFont(15f);
                encouragingMessageText.rectTransform.anchorMin = new Vector2(0.08f, 0.26f);
                encouragingMessageText.rectTransform.anchorMax = new Vector2(0.92f, 0.42f);

                if (postScore == totalQuestions)
                {
                    encouragingMessageText.text = "Flawless! You correctly categorized every city trash item. Ranger Robin and the City are proud of you!";
                }
                else if (postScore >= 3)
                {
                    encouragingMessageText.text = "Well done! Proper sorting keeps city streets, markets, and parks clean and sustainable.";
                }
                else
                {
                    encouragingMessageText.text = "Good effort! Every piece of trash properly sorted makes our city cleaner. Keep practicing!";
                }
            }

            // Bonus gold notification
            if (bonusGoldText != null)
            {
                bonusGoldText.rectTransform.anchorMin = new Vector2(0f, 0.14f);
                bonusGoldText.rectTransform.anchorMax = new Vector2(1f, 0.24f);
                if (bonusGold > 0)
                {
                    bonusGoldText.gameObject.SetActive(true);
                    bonusGoldText.text = $"+{bonusGold} Bonus Gold Earned!";
                    bonusGoldText.color = new Color(1f, 0.85f, 0.2f, 1f);
                }
                else
                {
                    bonusGoldText.gameObject.SetActive(false);
                }
            }

            if (continueButton != null)
            {
                var contRt = continueButton.GetComponent<RectTransform>();
                if (contRt != null) contRt.anchoredPosition = new Vector2(0f, 32f);
            }

            if (postScore >= 3 && celebratoryParticlesGroup != null)
            {
                celebratoryParticlesGroup.SetActive(true);
                StartCoroutine(AnimateCelebratoryParticles());
            }

            isAnimating = false;
            yield break;
        }

        if (resultsTitleText != null)
        {
            resultsTitleText.text = "EcoQuest Learning Results";
            resultsTitleText.color = Color.white;
        }
        else if (titleText != null)
        {
            titleText.text = "EcoQuest Learning Results";
            titleText.color = Color.white;
        }

        if (resultsPanel != null) resultsPanel.SetActive(true);
        if (preScoreText != null)
        {
            preScoreText.gameObject.SetActive(true);
            preScoreText.color = UITheme.Instance != null ? UITheme.Instance.textMuted : Color.white;
        }
        if (postScoreText != null)
        {
            postScoreText.gameObject.SetActive(true);
            postScoreText.color = new Color(0.29f, 0.88f, 0.46f, 1f); // #4AE076
        }
        if (rewardGiftIcon != null)
        {
            StopAndResetRewardGift();
            rewardGiftIcon.gameObject.SetActive(true);
            rewardGiftCoroutine = StartCoroutine(QuizUIAnimator.AnimateRewardGift(rewardGiftIcon));
        }
        if (resultsCanvasGroup != null)
        {
            yield return QuizUIAnimator.FadeCanvasGroup(resultsCanvasGroup, 0f, 1f, 0.3f);
        }

        // Count up pre-quiz and post-quiz scores
        Coroutine preCount = StartCoroutine(QuizUIAnimator.CountUpText(preScoreText, "Pre-Quiz Score: ", preScore, $" / {totalQuestions}", 0.7f));
        Coroutine postCount = StartCoroutine(QuizUIAnimator.CountUpText(postScoreText, "Post-Quiz Score: ", postScore, $" / {totalQuestions}", 0.7f));
        yield return preCount;
        yield return postCount;

        yield return new WaitForSecondsRealtime(0.15f);

        // Improvement punch pop
        int diff = postScore - preScore;
        if (improvementText != null)
        {
            if (diff > 0)
            {
                improvementText.text = $"+{diff} Points Improvement!";
                improvementText.color = Color.white;
            }
            else if (diff == 0)
            {
                improvementText.text = "Consistent Performance!";
                improvementText.color = UITheme.Instance != null ? UITheme.Instance.textMuted : Color.white;
            }
            else
            {
                improvementText.text = "Review & Keep Growing!";
                improvementText.color = UITheme.Instance != null ? UITheme.Instance.textMuted : Color.white;
            }
        }

        if (improvementBadgeRect != null)
        {
            yield return QuizUIAnimator.PulseScale(improvementBadgeRect, 1.25f, 0.35f);
        }

        // Encouraging message
        if (encouragingMessageText != null)
        {
            encouragingMessageText.color = UITheme.Instance != null ? UITheme.Instance.textMuted : Color.white;
            if (diff > 0)
            {
                encouragingMessageText.text = "Fantastic job! Your knowledge of waste sorting and environmental protection grew significantly during this quest!";
            }
            else if (diff == 0 && postScore == totalQuestions)
            {
                encouragingMessageText.text = "Flawless score! You are a master eco-guardian and already knew every concept inside out!";
            }
            else
            {
                encouragingMessageText.text = "Every effort helps protect our forest! With a little more practice, you will become a certified sorting master!";
            }
        }

        // Bonus gold notification
        if (bonusGoldText != null)
        {
            if (bonusGold > 0)
            {
                bonusGoldText.gameObject.SetActive(true);
                bonusGoldText.text = $"+{bonusGold} Bonus Gold Earned!";
            }
            else
            {
                bonusGoldText.gameObject.SetActive(false);
            }
        }

        // Celebratory UI particles if positive improvement or top score
        if ((diff > 0 || postScore == totalQuestions) && celebratoryParticlesGroup != null)
        {
            celebratoryParticlesGroup.SetActive(true);
            StartCoroutine(AnimateCelebratoryParticles());
        }

        isAnimating = false;
    }

    /// <summary>
    /// Displays a simple completion screen at the end of a repeat quest instead of the Post-Test.
    /// </summary>
    public IEnumerator AnimateRepeatQuestCompleteScreen(int goldEarned, Action onContinueClicked)
    {
        isAnimating = true;
        IsAnyQuizOpen = true;
        onResultsClosedCallback = onContinueClicked;

        if (CenterScreenCursor.Instance != null)
        {
            CenterScreenCursor.Instance.SetVisible(false);
        }

        if (quizPanel != null) quizPanel.SetActive(false);
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
        if (questionContainerRect != null) questionContainerRect.gameObject.SetActive(false);
        if (answersContainer != null) answersContainer.gameObject.SetActive(false);
        if (progressBarFill != null && progressBarFill.transform.parent != null)
            progressBarFill.transform.parent.gameObject.SetActive(false);
        if (progressText != null) progressText.gameObject.SetActive(false);

        if (titleText != null) titleText.text = "Quest Complete!";
        if (resultsTitleText != null) resultsTitleText.text = "Quest Complete!";

        if (preScoreText != null) preScoreText.gameObject.SetActive(false);
        if (postScoreText != null) postScoreText.gameObject.SetActive(false);

        if (improvementText != null)
        {
            improvementText.text = "Forest Cleaned Again!";
            improvementText.color = correctColor;
        }

        if (encouragingMessageText != null)
        {
            encouragingMessageText.text = "Great work, Guardian of the forest! You sorted all 9 pieces of trash and kept the forest sparkling clean!";
        }

        if (bonusGoldText != null)
        {
            if (goldEarned > 0)
            {
                bonusGoldText.gameObject.SetActive(true);
                bonusGoldText.text = $"+{goldEarned} Gold Earned!";
            }
            else
            {
                bonusGoldText.gameObject.SetActive(false);
            }
        }

        if (resultsPanel != null) resultsPanel.SetActive(true);
        if (resultsCanvasGroup != null) resultsCanvasGroup.alpha = 1f;

        if (quizCanvasRoot != null) quizCanvasRoot.SetActive(true);

        if (overlayCanvasGroup != null)
        {
            QuizUIAnimator.StopTransitionsOn(null, overlayCanvasGroup);
            overlayCanvasGroup.alpha = 0f;
            overlayCanvasGroup.gameObject.SetActive(true);
        }

        if (modalPanelRect != null)
        {
            QuizUIAnimator.StopTransitionsOn(modalPanelRect, modalPanelCanvasGroup);
            modalPanelRect.localScale = Vector3.one * 0.8f;
            if (modalPanelCanvasGroup != null) modalPanelCanvasGroup.alpha = 0f;
            modalPanelRect.gameObject.SetActive(true);
        }

        if (rewardGiftIcon != null)
        {
            StopAndResetRewardGift();
            rewardGiftIcon.gameObject.SetActive(true);
            rewardGiftCoroutine = StartCoroutine(QuizUIAnimator.AnimateRewardGift(rewardGiftIcon));
        }

        Coroutine overlayRoutine = StartCoroutine(QuizUIAnimator.FadeCanvasGroup(overlayCanvasGroup, 0f, 1f, 0.25f));
        yield return QuizUIAnimator.PopInPanel(modalPanelRect, modalPanelCanvasGroup, 0.8f, 0.3f);
        yield return overlayRoutine;

        if (improvementBadgeRect != null)
        {
            yield return QuizUIAnimator.PulseScale(improvementBadgeRect, 1.18f, 0.28f);
        }

        if (celebratoryParticlesGroup != null)
        {
            celebratoryParticlesGroup.SetActive(true);
            StartCoroutine(AnimateCelebratoryParticles());
        }

        isAnimating = false;
    }

    private IEnumerator AnimateCelebratoryParticles()
    {
        if (celebratoryParticlesGroup == null) yield break;
        var images = celebratoryParticlesGroup.GetComponentsInChildren<Image>(true);
        var originalPos = new List<Vector2>();
        var rects = new List<RectTransform>();

        foreach (var img in images)
        {
            var rt = img.GetComponent<RectTransform>();
            rects.Add(rt);
            originalPos.Add(rt.anchoredPosition);
        }

        float duration = 1.2f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;

            for (int i = 0; i < rects.Count; i++)
            {
                if (rects[i] != null)
                {
                    float angle = (i * 360f / rects.Count) * Mathf.Deg2Rad;
                    float dist = Mathf.Lerp(0f, 180f, Mathf.Sin(t * Mathf.PI * 0.5f));
                    Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
                    rects[i].anchoredPosition = originalPos[i] + offset;
                    rects[i].localScale = Vector3.one * Mathf.Lerp(1.2f, 0.4f, t);

                    var img = images[i];
                    if (img != null)
                    {
                        Color c = img.color;
                        c.a = Mathf.Lerp(1f, 0f, t);
                        img.color = c;
                    }
                }
            }
            yield return null;
        }

        // Reset positions
        for (int i = 0; i < rects.Count; i++)
        {
            if (rects[i] != null) rects[i].anchoredPosition = originalPos[i];
        }
    }

    private void HandleContinueClicked()
    {
        if (isAnimating) return;
        StopAndResetRewardGift();
        onResultsClosedCallback?.Invoke();
    }

    /// <summary>
    /// Closes the entire quiz modal with scale/fade out.
    /// </summary>
    public IEnumerator AnimateCloseModal()
    {
        isAnimating = true;
        StopAndResetRewardGift();

        if (modalPanelCanvasGroup != null)
        {
            modalPanelCanvasGroup.interactable = false;
            modalPanelCanvasGroup.blocksRaycasts = false;
        }

        ModalManager.Instance?.CloseModal(gameObject);

        Coroutine overlayRoutine = StartCoroutine(QuizUIAnimator.FadeCanvasGroup(overlayCanvasGroup, 1f, 0f, modalCloseDuration));
        yield return QuizUIAnimator.PopOutPanel(modalPanelRect, modalPanelCanvasGroup, modalStartScale, modalCloseDuration);
        yield return overlayRoutine;

        HideImmediate();

        // Restore UI elements for next run
        if (quizPanel != null) quizPanel.SetActive(true);
        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (questionContainerRect != null) questionContainerRect.gameObject.SetActive(true);
        if (answersContainer != null) answersContainer.gameObject.SetActive(true);
        if (progressBarFill != null && progressBarFill.transform.parent != null)
            progressBarFill.transform.parent.gameObject.SetActive(true);
        if (progressText != null) progressText.gameObject.SetActive(true);

        isAnimating = false;
    }
}
