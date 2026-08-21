using System.Collections;
using UnityEngine;

public class TutorialManager : MonoBehaviour
{
    [Header("Main Background")]
    public GameObject tutorialOverlay;

    [Header("Your Tutorial Cards")]
    public GameObject[] tutorialCards; 

    private int currentCardIndex = 0;
    private bool isTutorialActive = false; 
    private bool isTransitioning = false;

    void Start()
    {
        HideTutorial();
    }

    void Update()
    {
        if (isTutorialActive && !isTransitioning)
        {
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
            {
                StartCoroutine(TransitionToNextCard());
            }
        }
    }

    public void ShowTutorial(int startingIndex = 0)
    {
        currentCardIndex = startingIndex;
        UpdateCardDisplay();

        tutorialOverlay.SetActive(true);
        Time.timeScale = 0f;
        
        isTutorialActive = true; 
        isTransitioning = false;
    }

    private IEnumerator TransitionToNextCard()
    {
        isTransitioning = true;

        yield return new WaitForSecondsRealtime(0.5f);

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
            tutorialCards[i].SetActive(i == currentCardIndex);
        }
    }

    public void HideTutorial()
    {
        tutorialOverlay.SetActive(false);
        
        foreach (GameObject card in tutorialCards)
        {
            card.SetActive(false);
        }

        Time.timeScale = 1f;
        isTutorialActive = false;
        isTransitioning = false;
    }
}