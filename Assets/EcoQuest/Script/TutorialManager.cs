using System.Collections;
using UnityEngine;

public class TutorialManager : MonoBehaviour
{
    [Header("Main Background")]
    public GameObject tutorialOverlay;

    [Header("Your Tutorial Cards")]
    public GameObject[] tutorialCards; 

    [Header("Single Card & Close Events")]
    public bool singleCardMode = false;
    public UnityEngine.Events.UnityEvent OnLastCardReachedEvent;
    public event System.Action OnLastCardReached;
    public UnityEngine.Events.UnityEvent OnTutorialClosedEvent;
    public event System.Action OnTutorialClosed;

    private int currentCardIndex = 0;
    private bool isTutorialActive = false; 
    private bool isTransitioning = false;
    private float cardOpenedTime = 0f;
    private const float INPUT_IGNORE_DURATION = 0.2f;

    void Awake()
    {
        CleanseCardsList();
    }

    void Start()
    {
        CleanseCardsList();
        HideTutorial();
    }

    private void CleanseCardsList()
    {
        if (tutorialCards == null) return;
        var validCards = new System.Collections.Generic.List<GameObject>();
        for (int i = 0; i < tutorialCards.Length; i++)
        {
            if (tutorialCards[i] != null)
            {
                validCards.Add(tutorialCards[i]);
            }
        }
        if (validCards.Count != tutorialCards.Length)
        {
            tutorialCards = validCards.ToArray();
        }
    }

    void Update()
    {
        if (isTutorialActive && !isTransitioning)
        {
            // Ignore input for the first 0.2s after opening so dialogue clicks do not close the card
            if (Time.unscaledTime - cardOpenedTime < INPUT_IGNORE_DURATION)
                return;

            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return))
            {
                if (singleCardMode)
                {
                    HideTutorial();
                }
                else
                {
                    StartCoroutine(TransitionToNextCard());
                }
            }
        }
    }

    public void ShowTutorial(int startingIndex = 0, bool singleCard = false)
    {
        CleanseCardsList();

        if (tutorialCards == null || tutorialCards.Length == 0)
        {
            Debug.LogWarning("[TutorialManager] No valid tutorial cards to display!");
            HideTutorial();
            return;
        }

        singleCardMode = singleCard;
        currentCardIndex = Mathf.Clamp(startingIndex, 0, tutorialCards.Length - 1);
        cardOpenedTime = Time.unscaledTime;
        UpdateCardDisplay();

        if (tutorialOverlay != null) tutorialOverlay.SetActive(true);
        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (CenterScreenCursor.Instance != null)
        {
            CenterScreenCursor.Instance.SetVisible(false);
        }
        
        isTutorialActive = true; 
        isTransitioning = false;
    }

    public void ShowSingleCard(int index)
    {
        ShowTutorial(index, true);
    }

    private IEnumerator TransitionToNextCard()
    {
        isTransitioning = true;

        yield return new WaitForSecondsRealtime(0.2f);

        currentCardIndex++; 

        if (currentCardIndex < tutorialCards.Length)
        {
            UpdateCardDisplay();
            isTransitioning = false;
        }
        else
        {
            HideTutorial();
        }
    }

    private void UpdateCardDisplay()
    {
        for (int i = 0; i < tutorialCards.Length; i++)
        {
            if (tutorialCards[i] != null)
                tutorialCards[i].SetActive(i == currentCardIndex);
        }

        cardOpenedTime = Time.unscaledTime;

        bool isLast = (singleCardMode || (tutorialCards != null && currentCardIndex >= tutorialCards.Length - 1));
        if (isLast)
        {
            OnLastCardReached?.Invoke();
            OnLastCardReachedEvent?.Invoke();
        }
    }

    public void HideTutorial()
    {
        if (tutorialOverlay != null) tutorialOverlay.SetActive(false);
        
        if (tutorialCards != null)
        {
            foreach (GameObject card in tutorialCards)
            {
                if (card != null) card.SetActive(false);
            }
        }

        Time.timeScale = 1f;
        isTutorialActive = false;
        isTransitioning = false;

        OnTutorialClosed?.Invoke();
        OnTutorialClosedEvent?.Invoke();
    }
}
