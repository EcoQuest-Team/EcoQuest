using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EcoQuest.CityQuest
{
    [DisallowMultipleComponent]
    public class CityBonusQuizAdapter : MonoBehaviour
    {
        public QuizUIController quizUI;
        public string quizTitle = "TRASH DETECTIVE BONUS QUIZ";
        private QuizData runtimeQuizData;
        private int currentQuestionIndex;
        private int correctCount;
        private int totalQuizCoinsEarned;
        private bool isQuizRunning;
        private Action onQuizFinishedCallback;

        public bool IsQuizRunning => isQuizRunning;

        private void Awake()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene(gameObject.scene))
            {
                enabled = false;
                gameObject.SetActive(false);
                return;
            }
        }

        private void OnEnable()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene(gameObject.scene))
            {
                enabled = false;
                return;
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            bool wasRunning = isQuizRunning;
            isQuizRunning = false;
            if (wasRunning)
            {
                var fps = FindFirstObjectByType<StarterAssets.FirstPersonController>();
                if (fps != null) fps.enabled = true;
                var inputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
                CursorState.SetLocked(true, inputs);
            }
        }

        private void Update()
        {
            if (isQuizRunning)
            {
                if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
                {
                    CursorState.SetLocked(false);
                }
            }
        }

        public void StartBonusQuiz(List<CityTrashItemDef> collectedItems, CityTrashDatabase fallbackDb, Action onFinished)
        {
            if (isQuizRunning) return;
            if (quizUI == null)
            {
                quizUI = GetComponentInChildren<QuizUIController>(true);
                if (quizUI == null) quizUI = FindFirstObjectByType<QuizUIController>(FindObjectsInactive.Include);
            }
            if (quizUI == null)
            {
                Debug.LogWarning("[CityBonusQuizAdapter] No QuizUIController found in City Scene!");
                onFinished?.Invoke();
                return;
            }

            onQuizFinishedCallback = onFinished;
            BuildFiveQuestionsFromCollected(collectedItems, fallbackDb);

            if (runtimeQuizData == null || runtimeQuizData.questions == null || runtimeQuizData.questions.Count == 0)
            {
                onFinished?.Invoke();
                return;
            }

            isQuizRunning = true;
            currentQuestionIndex = 0;
            correctCount = 0;
            totalQuizCoinsEarned = 0;

            var inputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.move = Vector2.zero;
                inputs.look = Vector2.zero;
            }
            CursorState.SetLocked(false, inputs);
            var fps = FindFirstObjectByType<StarterAssets.FirstPersonController>();
            if (fps != null) fps.enabled = false;

            StartCoroutine(RunBonusQuizSequence());
        }

        private void BuildFiveQuestionsFromCollected(List<CityTrashItemDef> collectedItems, CityTrashDatabase fallbackDb)
        {
            if (runtimeQuizData == null)
            {
                runtimeQuizData = ScriptableObject.CreateInstance<QuizData>();
            }
            runtimeQuizData.questions.Clear();

            List<CityTrashItemDef> pool = new List<CityTrashItemDef>();
            if (collectedItems != null)
            {
                for (int i = 0; i < collectedItems.Count; i++)
                {
                    if (collectedItems[i] != null && !pool.Contains(collectedItems[i]))
                    {
                        pool.Add(collectedItems[i]);
                    }
                }
            }

            if (pool.Count < 5 && fallbackDb != null && fallbackDb.items != null)
            {
                for (int i = 0; i < fallbackDb.items.Count && pool.Count < 5; i++)
                {
                    if (fallbackDb.items[i] != null && !pool.Contains(fallbackDb.items[i]))
                    {
                        pool.Add(fallbackDb.items[i]);
                    }
                }
            }

            // Shuffle pool using current time seed
            System.Random rng = new System.Random(Environment.TickCount ^ pool.Count);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }

            int qCount = Mathf.Min(5, pool.Count);
            string[] catOptions = new string[]
            {
                "Biodegradable (Compost & Organic)",
                "Recyclable (Paper, Metal, Glass & Clean Plastic)",
                "Non-Biodegradable (Mixed Residual Trash)",
                "Hazardous (Special Safe Drop-Off)"
            };

            for (int i = 0; i < qCount; i++)
            {
                var item = pool[i];
                int correctIdx = (int)item.category;

                QuizQuestion q = new QuizQuestion
                {
                    questionText = $"You found a <b>{item.displayName}</b> in the city!\nWhich waste category does it belong to?",
                    questionImage = item.icon,
                    options = (string[])catOptions.Clone(),
                    correctOptionIndex = Mathf.Clamp(correctIdx, 0, 3),
                    explanation = !string.IsNullOrEmpty(item.quizExplanation)
                        ? $"{item.quizExplanation}\nFact: {item.funFact}"
                        : item.funFact
                };
                runtimeQuizData.questions.Add(q);
            }
        }

        private IEnumerator RunBonusQuizSequence()
        {
            quizUI.isBonusQuizMode = true;
            QuizQuestion firstQ = runtimeQuizData.questions[0];
            quizUI.PrepareInitialQuestion(
                firstQ.questionText,
                firstQ.options,
                runtimeQuizData.questions.Count,
                firstQ.questionImage);

            yield return StartCoroutine(quizUI.AnimateOpenModal(quizTitle));
            yield return StartCoroutine(DisplayCurrentQuestion(true));
        }

        private IEnumerator DisplayCurrentQuestion(bool isFirst)
        {
            QuizQuestion q = runtimeQuizData.questions[currentQuestionIndex];
            yield return StartCoroutine(quizUI.AnimateDisplayQuestion(
                q.questionText,
                q.options,
                currentQuestionIndex,
                runtimeQuizData.questions.Count,
                isFirst,
                OnAnswerSelected,
                q.questionImage));
        }

        private void OnAnswerSelected(int selectedIdx)
        {
            if (!isQuizRunning) return;
            QuizQuestion q = runtimeQuizData.questions[currentQuestionIndex];
            bool isCorrect = (selectedIdx == q.correctOptionIndex);

            if (isCorrect)
            {
                correctCount++;
                totalQuizCoinsEarned += 10;
                AwardCoins(10);
                AudioManager.Instance?.PlayQuizCorrect();
            }
            else
            {
                AudioManager.Instance?.PlayQuizWrong();
            }

            string feedbackBody = isCorrect
                ? $"+10 Coins! {q.explanation}"
                : $"Correct Category: {CityTrashDatabase.GetCategoryDisplayName((LitterCategory)q.correctOptionIndex)}.\n{q.explanation}";

            StartCoroutine(quizUI.AnimatePostQuizFeedback(
                selectedIdx,
                q.correctOptionIndex,
                isCorrect,
                feedbackBody,
                OnNextQuestionClicked));
        }

        private void OnNextQuestionClicked()
        {
            currentQuestionIndex++;
            if (currentQuestionIndex < runtimeQuizData.questions.Count)
            {
                StartCoroutine(DisplayCurrentQuestion(false));
            }
            else
            {
                StartCoroutine(FinishQuizSequence());
            }
        }

        private IEnumerator FinishQuizSequence()
        {
            if (quizUI != null) quizUI.isBonusQuizMode = true;
            int totalQs = runtimeQuizData.questions.Count;
            // Always award +20 completion bonus when the player finishes the bonus quiz
            int completionBonus = 20;
            totalQuizCoinsEarned += completionBonus;
            AwardCoins(completionBonus);

            PlayerPrefs.SetInt(EcoQuestSaveKeys.CityBonusPending, 0);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.CityBonusTaken, 1);
            PlayerPrefs.Save();

            bool closed = false;
            yield return StartCoroutine(quizUI.AnimateResultsScreen(
                0,
                correctCount,
                totalQs,
                totalQuizCoinsEarned,
                () => closed = true));

            while (!closed)
            {
                yield return null;
            }

            yield return StartCoroutine(quizUI.AnimateCloseModal());
            isQuizRunning = false;

            var fps = FindFirstObjectByType<StarterAssets.FirstPersonController>();
            if (fps != null) fps.enabled = true;
            var inputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
            CursorState.SetLocked(true, inputs);

            onQuizFinishedCallback?.Invoke();
        }

        private static void AwardCoins(int amount)
        {
            if (amount <= 0) return;
            var currency = PlayerCurrency.Instance ?? UnityEngine.Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            if (currency != null)
            {
                currency.AddGoldAnimated(amount, 0.75f);
            }
            else
            {
                int cur = PlayerPrefs.GetInt("PlayerGold", PlayerPrefs.GetInt("PlayerCoins", 0));
                cur += amount;
                PlayerPrefs.SetInt("PlayerGold", cur);
                PlayerPrefs.SetInt("PlayerCoins", cur);
                PlayerPrefs.Save();
            }
        }
    }
}
