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

    [Header("Colors")]
    [SerializeField] private Color normalButtonColor = new Color(0.96f, 0.93f, 0.88f, 1f);
    [SerializeField] private Color correctHighlightColor = Color.white;
    [SerializeField] private Color dimmedButtonColor = new Color(0.80f, 0.80f, 0.80f, 1f);
    [SerializeField] private Color correctColor = new Color(0.24f, 0.68f, 0.32f, 1f);
    [SerializeField] private Color wrongColor = new Color(0.86f, 0.28f, 0.24f, 1f);
    [SerializeField] private Color normalTextColor = new Color(0.23f, 0.16f, 0.10f, 1f);
    [SerializeField] private Color highlightedTextColor = Color.white;

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

    private void Awake()
    {
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
        if (quizPanel != null) quizPanel.SetActive(false);
        if (questionContainerRect != null) questionContainerRect.gameObject.SetActive(false);
        if (answersContainer != null) answersContainer.gameObject.SetActive(false);
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(true);
        if (celebratoryParticlesGroup != null) celebratoryParticlesGroup.SetActive(false);
        if (resultsTitleText != null) resultsTitleText.text = "EcoQuest Learning Results";
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

        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (options != null && i < options.Length)
            {
                answerButtons[i].gameObject.SetActive(true);
                answerButtons[i].interactable = false;
                if (answerButtonTexts[i] != null)
                {
                    string prefix = $"{(char)('A' + i)}) ";
                    answerButtonTexts[i].text = options[i].StartsWith(prefix) ? options[i] : prefix + options[i];
                    answerButtonTexts[i].color = normalTextColor;
                }
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
    }

    /// <summary>
    /// Opens the Quiz modal with bouncy scale pop-in and dark overlay fade.
    /// </summary>
    public IEnumerator AnimateOpenModal(string title)
    {
        isAnimating = true;
        IsAnyQuizOpen = true;

        if (CenterScreenCursor.Instance != null)
        {
            CenterScreenCursor.Instance.SetVisible(false);
        }

        // 1. Explicitly configure child sub-panels BEFORE enabling the modal
        if (quizPanel != null) quizPanel.SetActive(true);
        if (feedbackPanel != null) feedbackPanel.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (celebratoryParticlesGroup != null) celebratoryParticlesGroup.SetActive(false);

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
            modalPanelRect.localScale = Vector3.one * 0.8f;
            if (modalPanelCanvasGroup != null) modalPanelCanvasGroup.alpha = 0f;
            modalPanelRect.gameObject.SetActive(true);
        }

        // Overlay fade in (0.3s)
        Coroutine overlayRoutine = StartCoroutine(QuizUIAnimator.FadeCanvasGroup(overlayCanvasGroup, 0f, 1f, 0.3f));
        // Modal bounce pop-in (0.8 -> 1.05 -> 1.0)
        yield return QuizUIAnimator.PopInPanel(modalPanelRect, modalPanelCanvasGroup, 0.8f, 0.35f);
        yield return overlayRoutine;

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
        if (questionText != null) questionText.text = question;

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
                if (answerButtonTexts[i] != null)
                {
                    string prefix = $"{(char)('A' + i)}) ";
                    answerButtonTexts[i].text = options[i].StartsWith(prefix) ? options[i] : prefix + options[i];
                    answerButtonTexts[i].color = normalTextColor;
                }
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
            if (feedbackTitleText != null)
            {
                feedbackTitleText.text = isCorrect ? "CORRECT!" : "NOT QUITE!";
                feedbackTitleText.color = isCorrect ? correctColor : wrongColor;
            }
            if (explanationText != null)
            {
                explanationText.text = explanation;
            }

            if (feedbackCanvasGroup != null)
            {
                yield return QuizUIAnimator.FadeCanvasGroup(feedbackCanvasGroup, 0f, 1f, 0.25f);
            }
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
        if (resultsTitleText != null)
            resultsTitleText.text = "EcoQuest Learning Results";
        else if (titleText != null)
            titleText.text = "EcoQuest Learning Results";

        if (resultsPanel != null) resultsPanel.SetActive(true);
        if (preScoreText != null) preScoreText.gameObject.SetActive(true);
        if (postScoreText != null) postScoreText.gameObject.SetActive(true);
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
                improvementText.color = correctColor;
            }
            else if (diff == 0)
            {
                improvementText.text = "Consistent Performance!";
                improvementText.color = normalTextColor;
            }
            else
            {
                improvementText.text = "Review & Keep Growing!";
                improvementText.color = normalTextColor;
            }
        }

        if (improvementBadgeRect != null)
        {
            yield return QuizUIAnimator.PulseScale(improvementBadgeRect, 1.25f, 0.35f);
        }

        // Encouraging message
        if (encouragingMessageText != null)
        {
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

        Coroutine overlayRoutine = StartCoroutine(QuizUIAnimator.FadeCanvasGroup(overlayCanvasGroup, 1f, 0f, 0.25f));
        yield return QuizUIAnimator.PopOutPanel(modalPanelRect, modalPanelCanvasGroup, 0.85f, 0.25f);
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
