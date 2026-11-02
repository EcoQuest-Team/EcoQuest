using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interaction and dialogue controller on Explorer_Var1.
/// Manages player proximity detection, E-key interaction prompt, gating evaluation,
/// dynamic contextual dialogue delivery via DialoguePresenter, and transition triggering.
/// </summary>
public class ExplorerGateController : MonoBehaviour
{
    public static ExplorerGateController Instance { get; private set; }

    [Header("Interaction Settings")]
    [SerializeField] private float interactRadius = 2.5f;
    [SerializeField] private float exitRadius = 3.2f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Header("Components")]
    [SerializeField] private ExplorerCinematicController cinematicController;
    [SerializeField] private NpcExplorerController explorerAnimController;
    [SerializeField] private NPCWaypointPointer waypointPointer;
    [SerializeField] private MinimapIcon minimapIcon;

    [Header("Audio")]
    [SerializeField] private AudioClip travelAcceptClip;

    private Transform playerTransform;
    private bool isPlayerInRadius = false;
    private bool isInDialogue = false;
    public bool IsInDialogue => isInDialogue;
    private float interactionCooldown = 0f;

    private void Awake()
    {
        Instance = this;
        if (cinematicController == null) cinematicController = GetComponent<ExplorerCinematicController>();
        if (explorerAnimController == null) explorerAnimController = GetComponent<NpcExplorerController>();
        if (waypointPointer == null) waypointPointer = GetComponent<NPCWaypointPointer>();
        if (minimapIcon == null) minimapIcon = GetComponent<MinimapIcon>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        FindPlayer();
        UpdateWaypointAndMinimapState();
    }

    private void OnEnable()
    {
        TravelGate.OnGateUnlocked += HandleGateUnlocked;
        UpdateWaypointAndMinimapState();
    }

    private void OnDisable()
    {
        TravelGate.OnGateUnlocked -= HandleGateUnlocked;
        if (WorldInteractionPrompt.Instance != null && isPlayerInRadius)
        {
            WorldInteractionPrompt.Instance.HidePrompt();
        }
        if (isInDialogue)
        {
            EndDialogueSession();
        }
    }

    private void HandleGateUnlocked()
    {
        UpdateWaypointAndMinimapState();
    }

    public void UpdateWaypointAndMinimapState()
    {
        if (ObjectiveMarkerService.Instance != null)
        {
            ObjectiveMarkerService.Instance.RefreshTargetFromState();
        }
        else
        {
            bool canTravel = TravelGate.CanTravelToCity;
            if (waypointPointer != null)
            {
                waypointPointer.enabled = canTravel;
            }
            if (minimapIcon != null)
            {
                minimapIcon.enabled = canTravel;
            }
        }
    }

    private void FindPlayer()
    {
        if (playerTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
            else
            {
                Camera cam = Camera.main;
                if (cam != null) playerTransform = cam.transform;
            }
        }
    }

    private void Update()
    {
        if (interactionCooldown > 0f)
        {
            interactionCooldown -= Time.unscaledDeltaTime;
        }

        FindPlayer();
        if (playerTransform == null) return;

        float dist = Vector3.Distance(transform.position, playerTransform.position);

        if (!isPlayerInRadius)
        {
            if (dist <= interactRadius)
            {
                isPlayerInRadius = true;
                UpdatePromptVisibility();
            }
        }
        else
        {
            if (dist > exitRadius)
            {
                isPlayerInRadius = false;
                if (WorldInteractionPrompt.Instance != null)
                {
                    WorldInteractionPrompt.Instance.HidePrompt();
                }
                if (isInDialogue)
                {
                    EndDialogueSession();
                }
            }
            else
            {
                UpdatePromptVisibility();
            }
        }

        // Allow player to cleanly interrupt dialogue with Escape key
        if (isInDialogue && Input.GetKeyDown(KeyCode.Escape))
        {
            EndDialogueSession();
            return;
        }

        // Listen for E interaction key
        if (isPlayerInRadius && !isInDialogue && interactionCooldown <= 0f)
        {
            if (Input.GetKeyDown(interactKey))
            {
                bool canStart = (ModalManager.Instance == null || !ModalManager.Instance.IsAnyModalActuallyVisible())
                                && (DialoguePresenter.Instance == null || !DialoguePresenter.Instance.IsInDialogue);

                if (canStart)
                {
                    StartDialogueSession();
                }
            }
        }
    }

    private void UpdatePromptVisibility()
    {
        if (WorldInteractionPrompt.Instance == null) return;

        bool shouldShow = isPlayerInRadius && !isInDialogue
                          && (ModalManager.Instance == null || !ModalManager.Instance.IsAnyModalActuallyVisible())
                          && (DialoguePresenter.Instance == null || !DialoguePresenter.Instance.IsInDialogue);

        if (shouldShow)
        {
            WorldInteractionPrompt.Instance.ShowPrompt(transform, "Explorer", "Talk", "E");
        }
        else
        {
            WorldInteractionPrompt.Instance.HidePrompt();
        }
    }

    public void StartDialogueSession()
    {
        if (isInDialogue) return;
        isInDialogue = true;
        interactionCooldown = 0.5f;

        if (WorldInteractionPrompt.Instance != null)
        {
            WorldInteractionPrompt.Instance.HidePrompt();
        }

        // 1. Enter Cinematic profile in UIOrchestrator
        UIOrchestrator.Instance?.EnterCinematicProfile("ExplorerGate");

        // 2. Freeze player movement and start cinematic framing
        SetPlayerFrozen(true);
        if (cinematicController != null)
        {
            cinematicController.BeginCinematic(playerTransform);
        }

        // 3. Hook typing animation
        if (DialoguePresenter.Instance != null)
        {
            DialoguePresenter.Instance.OnTypingStateChanged -= HandleTypingStateChanged;
            DialoguePresenter.Instance.OnTypingStateChanged += HandleTypingStateChanged;
        }

        // 4. Formulate dialogue based on gate status
        if (!TravelGate.IsTutorialComplete)
        {
            PlayTutorialIncompleteDialogue();
        }
        else if (!TravelGate.IsTrashQuestComplete)
        {
            PlayQuestIncompleteDialogue();
        }
        else if (!TravelGate.IsPostQuizAndRewardClaimed)
        {
            PlayQuizIncompleteDialogue();
        }
        else
        {
            PlayTravelOfferedDialogue();
        }
    }

    private void PlayTutorialIncompleteDialogue()
    {
        if (cinematicController != null) cinematicController.PlayWave();

        List<string> lines = new List<string>()
        {
            "Hey there, young explorer! Welcome to our lush forest sanctuary.",
            "Before we can set off on journeys beyond the woods, you'll need to learn the basics from our Forest Guardian.",
            "Go complete your training and practice with the Guardian first. The secrets of the Eco-City will be waiting for you!"
        };

        DialoguePresenter.Instance.ShowDialogue(
            speakerName: "Explorer",
            lines: lines,
            showChoicesAtEnd: false,
            onComplete: () =>
            {
                EndDialogueSession();
            }
        );
    }

    private void PlayQuestIncompleteDialogue()
    {
        if (cinematicController != null) cinematicController.PlayReaction("Shrug");

        int remainingWaste = Mathf.Max(1, QuestManager.GetTotalRequiredWaste() - QuestManager.GetTotalSortedWaste());

        List<string> lines = new List<string>()
        {
            "Hello again! Our forest is making great progress, but there is still cleaning left to do.",
            $"There are still {remainingWaste} pieces of trash scattered across the grove that need to be sorted into the proper bins!",
            "Once every piece of litter is tidied up, come find me right here. Together we'll prepare for the journey ahead!"
        };

        DialoguePresenter.Instance.ShowDialogue(
            speakerName: "Explorer",
            lines: lines,
            showChoicesAtEnd: false,
            onComplete: () =>
            {
                EndDialogueSession();
            }
        );
    }

    private void PlayQuizIncompleteDialogue()
    {
        if (cinematicController != null) cinematicController.PlayReaction("Cheer");

        List<string> lines = new List<string>()
        {
            "Tremendous work out there! All the litter is sorted and the forest is sparkling clean!",
            "You have just one final milestone before we can embark: take your final quiz with the Guardian and claim your certificate!",
            "Once your certificate and reward are claimed, our route to the Eco-City will be wide open!"
        };

        DialoguePresenter.Instance.ShowDialogue(
            speakerName: "Explorer",
            lines: lines,
            showChoicesAtEnd: false,
            onComplete: () =>
            {
                EndDialogueSession();
            }
        );
    }

    private void PlayTravelOfferedDialogue()
    {
        if (cinematicController != null) cinematicController.PlayWave();

        List<string> lines = new List<string>()
        {
            "Incredible achievements, Ranger! You've cleaned the grove, aced the test, and earned your badge!",
            "Just across the ridge lies the bustling Eco-City, where green technology and sustainable living thrive.",
            "I have our expedition gear packed and ready to go. Are you ready to travel to the City?"
        };

        DialoguePresenter.Instance.ShowDialogue(
            speakerName: "Explorer",
            lines: lines,
            showChoicesAtEnd: true,
            acceptLabel: "Yes, let's go!",
            declineLabel: "Not now",
            onAccept: () =>
            {
                HandleTravelAccepted();
            },
            onDecline: () =>
            {
                HandleTravelDeclined();
            },
            onComplete: () =>
            {
                // Fallback completion if no choice picked
                EndDialogueSession();
            }
        );
    }

    private void HandleTravelDeclined()
    {
        if (cinematicController != null) cinematicController.PlayReaction("Nod");

        List<string> declineLines = new List<string>()
        {
            "No problem at all! Take your time enjoying the clean forest. Come speak with me whenever you are ready to travel!"
        };

        DialoguePresenter.Instance.ShowDialogue(
            speakerName: "Explorer",
            lines: declineLines,
            showChoicesAtEnd: false,
            onComplete: () =>
            {
                EndDialogueSession();
            }
        );
    }

    private void HandleTravelAccepted()
    {
        if (cinematicController != null)
        {
            cinematicController.PlayReaction("Cheer");
        }

        if (travelAcceptClip != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(travelAcceptClip);
        }

        // Brief delay for cheer reaction before screen transition begins
        StartCoroutine(ExecuteTravelSequence());
    }

    private IEnumerator ExecuteTravelSequence()
    {
        // Unhook typing listener
        if (DialoguePresenter.Instance != null)
        {
            DialoguePresenter.Instance.OnTypingStateChanged -= HandleTypingStateChanged;
        }

        yield return new WaitForSecondsRealtime(0.65f);

        // Transition through guarded gate
        bool approved = TravelGate.TryTravelToCity("ExplorerDialogue");
        if (!approved)
        {
            Debug.LogError("[ExplorerGateController] TravelGate refused travel! Ending dialogue.");
            EndDialogueSession();
        }
    }

    private void HandleTypingStateChanged(bool isTyping)
    {
        if (cinematicController != null)
        {
            cinematicController.SetTalking(isTyping);
        }
    }

    public void EndDialogueSession()
    {
        if (!isInDialogue) return;
        isInDialogue = false;
        interactionCooldown = 0.5f;

        if (DialoguePresenter.Instance != null)
        {
            DialoguePresenter.Instance.OnTypingStateChanged -= HandleTypingStateChanged;
            if (DialoguePresenter.Instance.IsInDialogue)
            {
                DialoguePresenter.Instance.CloseDialogue();
            }
        }

        if (cinematicController != null)
        {
            cinematicController.EndCinematic();
        }

        SetPlayerFrozen(false);
        UIOrchestrator.Instance?.ExitCinematicProfile("ExplorerGate");
        TrashVisibilityController.Refresh("Explorer dialogue ended");
    }

    private void SetPlayerFrozen(bool frozen)
    {
        var fpc = FindFirstObjectByType<StarterAssets.FirstPersonController>();
        if (fpc != null)
        {
            fpc.enabled = !frozen;
        }

        var inputs = FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
        if (inputs != null && frozen)
        {
            inputs.MoveInput(Vector2.zero);
        }
    }
}
