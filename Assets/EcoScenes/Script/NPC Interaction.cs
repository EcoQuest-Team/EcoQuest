using System.Collections;
using UnityEngine;
using TMPro;

public class NPCInteraction : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject interactPrompt; 
    [SerializeField] private GameObject dialogueBox;    
    [SerializeField] private TextMeshProUGUI dialogueText;
    [SerializeField] private GameObject questCanvas;
    
    [Tooltip("Drag your OverheadCanvas here")]
    [SerializeField] private GameObject overheadMarker; 

    [Header("Trash Management")]
    [SerializeField] private GameObject trashContainer; 

    [Header("Tutorial Settings")]
    [SerializeField] private GameObject tutorialNotification;
    [SerializeField] private TutorialManager tutorialManager; // <--- ADDED THIS

    [Header("Dialogue Content & Settings")]
    [SerializeField] private string[] dialogueLines;
    
    [SerializeField] private float typingSpeed = 0.04f;
    [SerializeField] private float nextLineDelay = 0.5f;

    private int currentLineIndex = 0;
    private bool isPlayerNearby = false;
    private bool isTalking = false;
    private QuestManager[] allQuests;

    private Coroutine typingCoroutine;
    private bool isTyping = false;
    private bool canContinue = false;

    private void Start()
    {
        interactPrompt.SetActive(false);
        dialogueBox.SetActive(false);
        
        if (questCanvas != null) questCanvas.SetActive(false);
        if (trashContainer != null) trashContainer.SetActive(false);

        if (tutorialNotification != null) tutorialNotification.SetActive(true);

        allQuests = FindObjectsByType<QuestManager>(FindObjectsSortMode.None);
    }

    private void Update()
    {
        bool isQuestActive = questCanvas != null && questCanvas.activeInHierarchy;

        HandleInteractionPrompt(isQuestActive);

        if (isPlayerNearby && !isTalking && !isQuestActive && Input.GetKeyDown(KeyCode.E))
        {
            StartDialogue();
        }

        if (isTalking && Input.GetMouseButtonDown(0))
        {
            if (isTyping)
            {
                CompleteLineInstantly();
            }
            else if (canContinue)
            {
                DisplayNextLine();
            }
        }
    }

    private void HandleInteractionPrompt(bool isQuestActive)
    {
        bool shouldShowPrompt = isPlayerNearby && !isTalking && !isQuestActive;
        
        if (interactPrompt.activeSelf != shouldShowPrompt)
        {
            interactPrompt.SetActive(shouldShowPrompt);
        }
    }

    private void StartDialogue()
    {
        isTalking = true;
        currentLineIndex = 0;
        interactPrompt.SetActive(false);
        dialogueBox.SetActive(true);
        
        if (tutorialNotification != null) tutorialNotification.SetActive(false);

        UpdateDialogueText();
    }

    private void DisplayNextLine()
    {
        currentLineIndex++;
        if (currentLineIndex < dialogueLines.Length)
        {
            UpdateDialogueText();
        }
        else
        {
            EndDialogue();
        }
    }

    private void UpdateDialogueText()
    {
        if (dialogueLines.Length > 0)
        {
            if (typingCoroutine != null) StopCoroutine(typingCoroutine);
            
            typingCoroutine = StartCoroutine(TypeLine());
        }
    }

    private IEnumerator TypeLine()
    {
        isTyping = true;
        canContinue = false;
        dialogueText.text = "";

        foreach (char c in dialogueLines[currentLineIndex].ToCharArray())
        {
            dialogueText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }

        isTyping = false;
        StartCoroutine(WaitBeforeContinue());
    }

    private void CompleteLineInstantly()
    {
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        
        dialogueText.text = dialogueLines[currentLineIndex];
        isTyping = false;
        
        StartCoroutine(WaitBeforeContinue());
    }

    private IEnumerator WaitBeforeContinue()
    {
        yield return new WaitForSeconds(nextLineDelay);
        canContinue = true;
    }

    private void EndDialogue()
    {
        isTalking = false;
        dialogueBox.SetActive(false);

        foreach (QuestManager qm in allQuests)
        {
            qm.ResetQuest(); 
        }
        
        if (trashContainer != null) trashContainer.SetActive(true);
        if (questCanvas != null) questCanvas.SetActive(true);

        if (overheadMarker != null)
        {
            overheadMarker.SetActive(false);
        }

        // <--- ADDED THIS: Triggers the tutorial popup right when the quest starts
        if (tutorialManager != null) 
        {
            tutorialManager.ShowTutorial(0);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) isPlayerNearby = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNearby = false;
            interactPrompt.SetActive(false);
            dialogueBox.SetActive(false);
            isTalking = false; 
            
            if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        }
    }
}