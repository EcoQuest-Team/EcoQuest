using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton manager orchestrating the Pre-Quiz (baseline evaluation)
/// and Post-Quiz (learning gain measurement & rewards).
/// </summary>
public class QuizManager : MonoBehaviour
{
    public static QuizManager Instance { get; private set; }

    [Header("Data & UI References")]
    [Tooltip("The ScriptableObject containing quiz questions")]
    [SerializeField] private QuizData quizData;

    [Tooltip("The UI controller rendering the quiz modal")]
    [SerializeField] private QuizUIController quizUI;

    [Header("Reward Settings")]
    [Tooltip("Optional bonus gold awarded for each correct answer during post-quiz")]
    [SerializeField] private int goldBonusPerCorrect = 5;

    [Tooltip("If true, awards gold through PlayerCurrency on each correct post-quiz answer")]
    [SerializeField] private bool awardBonusGold = true;

    [Header("Repeat Quest Settings")]
    [Tooltip("If true, repeat quests also run the Post-Test at the end. If false (default), shows a simple completion message instead.")]
    public bool repeatQuestRunsPostTest = false;

    [Header("Runtime State")]
    [SerializeField] private int preQuizScore = 0;
    [SerializeField] private int postQuizScore = 0;
    [SerializeField] private bool isPreQuizCompleted = false;
    [SerializeField] private bool isPostQuizCompleted = false;

    public int PreQuizScore => preQuizScore;
    public int PostQuizScore => postQuizScore;
    public bool IsPreQuizCompleted => isPreQuizCompleted;
    public bool IsPostQuizCompleted => isPostQuizCompleted;

    public event Action<int, int> OnPreQuizFinished;
    public event Action<int, int, int> OnPostQuizFinished; // pre, post, improvement

    private struct ShuffledQuestion
    {
        public string questionText;
        public string[] shuffledOptions;
        public int correctIndex;
        public string explanation;
        public Sprite questionImage;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (quizUI == null)
        {
            quizUI = FindFirstObjectByType<QuizUIController>();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Resets quiz progression. Preserves the first Pre-Quiz baseline score once completed.
    /// </summary>
    public void ResetQuizzes()
    {
        if (!isPreQuizCompleted)
        {
            preQuizScore = 0;
        }
        postQuizScore = 0;
        isPostQuizCompleted = false;
    }

    /// <summary>
    /// Resets only the Post-Quiz state for repeat quests while keeping the first Pre-Quiz score unchanged.
    /// </summary>
    public void ResetPostQuizOnly()
    {
        postQuizScore = 0;
        isPostQuizCompleted = false;
    }

    /// <summary>
    /// Shows a simple completion modal at the end of a repeat quest instead of the Post-Test.
    /// </summary>
    public void ShowRepeatQuestComplete(int goldEarned, Action onCompleted = null)
    {
        StartCoroutine(RunRepeatQuestCompleteRoutine(goldEarned, onCompleted));
    }

    private IEnumerator RunRepeatQuestCompleteRoutine(int goldEarned, Action onCompleted)
    {
        if (Time.timeScale == 0f) Time.timeScale = 1f;
        SetControlsActive(false);

        if (quizUI != null)
        {
            bool continueClicked = false;
            yield return quizUI.AnimateRepeatQuestCompleteScreen(
                goldEarned,
                onContinueClicked: () => continueClicked = true
            );

            while (!continueClicked)
            {
                yield return null;
            }

            yield return quizUI.AnimateCloseModal();
        }

        SetControlsActive(true);
        onCompleted?.Invoke();
        TravelGate.NotifyRewardClaimed();
    }

    /// <summary>
    /// Initiates the Pre-Quiz modal before infographic cards appear.
    /// Does not show feedback/answers to establish baseline knowledge.
    /// </summary>
    public void StartPreQuiz(Action onCompleted = null)
    {
        if (isPreQuizCompleted)
        {
            onCompleted?.Invoke();
            return;
        }

        if (quizData == null || quizData.questions == null || quizData.questions.Count == 0)
        {
            Debug.LogWarning("[QuizManager] No quiz questions available in QuizData!");
            onCompleted?.Invoke();
            return;
        }

        StartCoroutine(RunPreQuizRoutine(onCompleted));
    }

    private IEnumerator RunPreQuizRoutine(Action onCompleted)
    {
        // 1. Freeze player and unlock cursor (keep Time.timeScale = 1f so Cinemachine camera blend finishes smoothly)
        if (Time.timeScale == 0f) Time.timeScale = 1f;
        SetControlsActive(false);

        // 2. Prepare shuffled questions
        List<ShuffledQuestion> questions = PrepareShuffledQuestions();
        preQuizScore = 0;

        // 3. Pre-populate first question on frame 0 and open modal animation
        if (quizUI != null)
        {
            if (questions.Count > 0)
            {
                quizUI.PrepareInitialQuestion(
                    questions[0].questionText,
                    questions[0].shuffledOptions,
                    questions.Count,
                    questions[0].questionImage
                );
            }
            AudioManager.Instance?.PlayModalOpen();
            yield return quizUI.AnimateOpenModal("Pre-Quiz: Initial Knowledge");
        }

        // 4. Iterate through questions
        for (int i = 0; i < questions.Count; i++)
        {
            ShuffledQuestion q = questions[i];
            bool answerChosen = false;
            int chosenOption = -1;

            if (quizUI != null)
            {
                yield return quizUI.AnimateDisplayQuestion(
                    q.questionText,
                    q.shuffledOptions,
                    i,
                    questions.Count,
                    isFirstQuestion: (i == 0),
                    onAnswerSelected: (selected) =>
                    {
                        chosenOption = selected;
                        answerChosen = true;
                    },
                    questionImage: q.questionImage
                );
            }

            // Wait for player selection
            while (!answerChosen)
            {
                yield return null;
            }
            AudioManager.Instance?.PlayButtonClick();

            // Record score without revealing feedback
            if (chosenOption == q.correctIndex)
            {
                preQuizScore++;
            }

            yield return new WaitForSecondsRealtime(0.12f);
        }

        isPreQuizCompleted = true;
        PlayerPrefs.SetInt("EcoQuest_PreQuizScore", preQuizScore);
        PlayerPrefs.Save();
        OnPreQuizFinished?.Invoke(preQuizScore, questions.Count);

        // 5. Close modal completely before triggering 3 Bin Educational Cards
        if (quizUI != null)
        {
            AudioManager.Instance?.PlayModalClose();
            yield return quizUI.AnimateCloseModal();
        }

        // Keep player frozen / cursor unlocked for the following tutorial cards
        SetControlsActive(false);
        onCompleted?.Invoke();
    }

    /// <summary>
    /// Initiates the Post-Quiz modal right after quest completion.
    /// Shows immediate feedback per question and the comparative Results screen.
    /// </summary>
    public void StartPostQuiz(Action onCompleted = null)
    {
        if (isPostQuizCompleted)
        {
            onCompleted?.Invoke();
            return;
        }

        if (quizData == null || quizData.questions == null || quizData.questions.Count == 0)
        {
            Debug.LogWarning("[QuizManager] No quiz questions available in QuizData!");
            onCompleted?.Invoke();
            return;
        }

        StartCoroutine(RunPostQuizRoutine(onCompleted));
    }

    private IEnumerator RunPostQuizRoutine(Action onCompleted)
    {
        // 1. Freeze player and unlock cursor
        SetControlsActive(false);

        // 2. Prepare fresh shuffled questions
        List<ShuffledQuestion> questions = PrepareShuffledQuestions();
        postQuizScore = 0;
        int totalBonusGoldEarned = 0;

        // 3. Pre-populate first question on frame 0 and open modal animation
        if (quizUI != null)
        {
            if (questions.Count > 0)
            {
                quizUI.PrepareInitialQuestion(
                    questions[0].questionText,
                    questions[0].shuffledOptions,
                    questions.Count,
                    questions[0].questionImage
                );
            }
            AudioManager.Instance?.PlayModalOpen();
            yield return quizUI.AnimateOpenModal("Post-Quiz: Master Verification");
        }

        // 4. Iterate through questions with feedback
        for (int i = 0; i < questions.Count; i++)
        {
            ShuffledQuestion q = questions[i];
            bool answerChosen = false;
            int chosenOption = -1;

            if (quizUI != null)
            {
                yield return quizUI.AnimateDisplayQuestion(
                    q.questionText,
                    q.shuffledOptions,
                    i,
                    questions.Count,
                    isFirstQuestion: (i == 0),
                    onAnswerSelected: (selected) =>
                    {
                        chosenOption = selected;
                        answerChosen = true;
                    },
                    questionImage: q.questionImage
                );
            }

            while (!answerChosen)
            {
                yield return null;
            }

            bool isCorrect = (chosenOption == q.correctIndex);
            if (isCorrect)
            {
                postQuizScore++;
                AudioManager.Instance?.PlayQuizCorrect();

                // Optional bonus gold award
                if (awardBonusGold && goldBonusPerCorrect > 0)
                {
                    PlayerCurrency bank = FindFirstObjectByType<PlayerCurrency>();
                    if (bank != null)
                    {
                        bank.AddGold(goldBonusPerCorrect);
                        totalBonusGoldEarned += goldBonusPerCorrect;
                    }
                }
            }
            else
            {
                AudioManager.Instance?.PlayQuizWrong();
            }

            // Post-quiz feedback: highlight, pulse/shake, explanation, Next button
            bool nextClicked = false;
            if (quizUI != null)
            {
                yield return quizUI.AnimatePostQuizFeedback(
                    chosenOption,
                    q.correctIndex,
                    isCorrect,
                    q.explanation,
                    onNextClicked: () => nextClicked = true
                );
            }

            while (!nextClicked)
            {
                yield return null;
            }
        }

        isPostQuizCompleted = true;
        PlayerPrefs.SetInt("EcoQuest_PostQuizScore", postQuizScore);
        PlayerPrefs.Save();
        int improvement = postQuizScore - preQuizScore;
        OnPostQuizFinished?.Invoke(preQuizScore, postQuizScore, improvement);

        // 5. Show Results Screen
        bool continueClicked = false;
        if (quizUI != null)
        {
            AudioManager.Instance?.PlayQuestComplete();
            yield return quizUI.AnimateResultsScreen(
                preQuizScore,
                postQuizScore,
                questions.Count,
                totalBonusGoldEarned,
                onContinueClicked: () => continueClicked = true
            );
        }

        while (!continueClicked)
        {
            yield return null;
        }

        // 6. Close modal and restore player controls
        if (quizUI != null)
        {
            AudioManager.Instance?.PlayModalClose();
            yield return quizUI.AnimateCloseModal();
        }

        SetControlsActive(true);
        onCompleted?.Invoke();
        TravelGate.NotifyRewardClaimed();
    }

    private List<ShuffledQuestion> PrepareShuffledQuestions()
    {
        var list = new List<ShuffledQuestion>();
        var rng = new System.Random();

        foreach (var q in quizData.questions)
        {
            if (q == null || q.options == null || q.options.Length == 0) continue;

            // Create indexed list of options
            var indexedOptions = new List<KeyValuePair<int, string>>();
            for (int i = 0; i < q.options.Length; i++)
            {
                indexedOptions.Add(new KeyValuePair<int, string>(i, q.options[i]));
            }

            // Fisher-Yates shuffle
            for (int i = indexedOptions.Count - 1; i > 0; i--)
            {
                int swapIdx = rng.Next(i + 1);
                var temp = indexedOptions[i];
                indexedOptions[i] = indexedOptions[swapIdx];
                indexedOptions[swapIdx] = temp;
            }

            string[] shuffledOpts = new string[indexedOptions.Count];
            int newCorrectIndex = 0;

            for (int i = 0; i < indexedOptions.Count; i++)
            {
                shuffledOpts[i] = indexedOptions[i].Value;
                if (indexedOptions[i].Key == q.correctOptionIndex)
                {
                    newCorrectIndex = i;
                }
            }

            list.Add(new ShuffledQuestion
            {
                questionText = q.questionText,
                shuffledOptions = shuffledOpts,
                correctIndex = newCorrectIndex,
                explanation = q.explanation,
                questionImage = q.questionImage
            });
        }

        return list;
    }

    private void SetControlsActive(bool active)
    {
        if (IntroSequenceController.Instance != null)
        {
            IntroSequenceController.Instance.SetPlayerControlsFrozen(!active);
            IntroSequenceController.Instance.SetCursorState(!active);
        }
        else
        {
            CursorState.SetLocked(active);
        }
    }
}
