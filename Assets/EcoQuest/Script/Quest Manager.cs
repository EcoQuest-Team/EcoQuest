using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class QuestManager : MonoBehaviour
{
    [Header("Trash Bin Settings")]
    [Tooltip("What kind of trash does THIS specific bin accept? ('Biodegradable', 'Recyclable', 'NonBiodegradable')")]
    [SerializeField] private string acceptedTag; 
    [Tooltip("Target point inside the bin where deposited trash items animate to.")]
    [SerializeField] private Transform depositPoint;

    [Header("Deposited Trash Appearance")]
    [Tooltip("Keep deposited items visible inside the bin in a neat pile")]
    [SerializeField] private bool keepDepositedItemsVisible = false;
    [Tooltip("Vanish deposited items after animation finishes (legacy behavior)")]
    [SerializeField] private bool vanishAfterAnimation = true;
    [Tooltip("Maximum deposited items visible per bin before oldest is hidden")]
    [SerializeField] private int maxDepositedItemsVisible = 8;
    [Tooltip("Visual trash layer component on this bin that grows in stages as trash is deposited")]
    [SerializeField] private BinVisualTrashLayer binVisualLayer;

    public Transform DepositPointTransform => depositPoint;
    public bool KeepDepositedItemsVisible => keepDepositedItemsVisible && !vanishAfterAnimation;
    public bool VanishAfterAnimation => vanishAfterAnimation;
    public int MaxDepositedItemsVisible => maxDepositedItemsVisible;
    public BinVisualTrashLayer BinVisualLayer => binVisualLayer;

    private int depositedInThisBinCount = 0;
    public int DepositedInThisBinCount => depositedInThisBinCount;

    private readonly List<GameObject> depositedInBin = new List<GameObject>();
    public IReadOnlyList<GameObject> DepositedInBin => depositedInBin;

    private static readonly Vector3[] PileSlotOffsets = new Vector3[]
    {
        new Vector3( 0.00f, 0.00f,  0.00f),
        new Vector3( 0.08f, 0.03f,  0.06f),
        new Vector3(-0.07f, 0.05f, -0.06f),
        new Vector3( 0.06f, 0.08f, -0.07f),
        new Vector3(-0.07f, 0.10f,  0.05f),
        new Vector3( 0.07f, 0.12f, -0.03f),
        new Vector3(-0.04f, 0.14f,  0.07f),
        new Vector3( 0.01f, 0.16f, -0.02f)
    };

    public Vector3 GetDepositSlotPosition(int slotIndex)
    {
        Vector3 basePos = GetDepositTargetPosition();
        if (PileSlotOffsets == null || PileSlotOffsets.Length == 0) return basePos;
        Vector3 offset = PileSlotOffsets[slotIndex % PileSlotOffsets.Length];
        return basePos + offset;
    }

    public Quaternion GetDepositSlotRotation(int slotIndex)
    {
        float yRot = (slotIndex * 53f) % 360f;
        float tiltX = ((slotIndex * 17) % 25) - 12f;
        float tiltZ = ((slotIndex * 29) % 25) - 12f;
        return Quaternion.Euler(tiltX, yRot, tiltZ);
    }

    public void ClearDepositedBinList()
    {
        depositedInBin.Clear();
    }

    public Vector3 GetDepositTargetPosition()
    {
        if (depositPoint != null) return depositPoint.position;
        return transform.position + Vector3.up * 0.60f;
    } 

    [Header("Quest UI Settings")]
    [Tooltip("Assign the progress bar for THIS specific category.")]
    [SerializeField] private Image fillImage; 
    [Tooltip("Assign the count text for THIS specific category.")]
    [SerializeField] private TextMeshProUGUI countText;
    [Tooltip("How many items does this category need total?")]
    [SerializeField] private int totalItemsNeeded = 3; 
    
    [Header("Global Settings")]
    [SerializeField] private GameObject questCanvas;

    [Header("Repeat Quest Settings")]
    [Tooltip("If true, completing a repeat quest runs the Post-Test again; if false (default), shows a simple completion message.")]
    public bool repeatQuestRunsPostTest = false;

    private static Dictionary<string, int> currentCounts = new Dictionary<string, int>();
    private static Dictionary<string, int> targets = new Dictionary<string, int>();
    private static Dictionary<string, Image> progressBars = new Dictionary<string, Image>();
    private static Dictionary<string, TextMeshProUGUI> countLabels = new Dictionary<string, TextMeshProUGUI>();
    private static List<GameObject> collectedTrash = new List<GameObject>();
    
    private static int mistakes = 0;
    private static float lastMistakeTime = 0f;
    private static bool isQuestComplete = false;
    private static int lastInitializedSceneHandle = -1;
    public static bool IsQuestComplete => isQuestComplete;
    public static bool IsRepeatQuest { get; set; } = false;

    public static int GetTotalRequiredWaste()
    {
        int total = 0;
        foreach (var kvp in targets) total += kvp.Value;
        return total > 0 ? total : 9;
    }

    public static int GetTotalSortedWaste()
    {
        int done = 0;
        foreach (var kvp in currentCounts) done += kvp.Value;
        return done;
    }

    public static int GetCategoryTarget(string category)
    {
        return targets.TryGetValue(category, out int t) ? t : 0;
    }

    public static int GetCategoryCurrentCount(string category)
    {
        return currentCounts.TryGetValue(category, out int c) ? c : 0;
    }

    public static void ResetAllQuestCountsToZero()
    {
        mistakes = 0;
        isQuestComplete = false;
        List<string> keys = new List<string>(currentCounts.Keys);
        foreach (string key in keys)
        {
            currentCounts[key] = 0;
            if (progressBars.ContainsKey(key) && progressBars[key] != null)
            {
                progressBars[key].fillAmount = 0f;
            }
            if (countLabels.ContainsKey(key) && countLabels[key] != null && targets.ContainsKey(key))
            {
                countLabels[key].text = $"0/{targets[key]}";
            }
        }
        collectedTrash.Clear();
        var allBins = FindObjectsByType<QuestManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var bin in allBins)
        {
            if (bin != null)
            {
                bin.depositedInThisBinCount = 0;
                bin.ClearDepositedBinList();
                if (bin.binVisualLayer != null) bin.binVisualLayer.ResetVisuals();
            }
        }
        QuestPanelToggleController.Instance?.RefreshProgressBars(true);
    }

    public static void ResetStaticState()
    {
        currentCounts.Clear();
        targets.Clear();
        progressBars.Clear();
        countLabels.Clear();
        collectedTrash.Clear();
        mistakes = 0;
        lastMistakeTime = 0f;
        isQuestComplete = false;
        IsRepeatQuest = false;

        var allVisualLayers = FindObjectsByType<BinVisualTrashLayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var vl in allVisualLayers)
        {
            if (vl != null) vl.ResetVisuals();
        }
    }

    public string AcceptedTag => acceptedTag;

    private void Awake()
    {
        if (gameObject.scene.IsValid() && gameObject.scene.handle != lastInitializedSceneHandle)
        {
            lastInitializedSceneHandle = gameObject.scene.handle;
            currentCounts.Clear();
            targets.Clear();
            progressBars.Clear();
            countLabels.Clear();
            collectedTrash.Clear();
            mistakes = 0;
            lastMistakeTime = 0f;
            isQuestComplete = false;
            IsRepeatQuest = false;
        }

        if (string.IsNullOrEmpty(acceptedTag))
        {
            string gName = gameObject.name.ToLower();
            if (gName.Contains("bio") && !gName.Contains("non")) acceptedTag = "Biodegradable";
            else if (gName.Contains("recyc")) acceptedTag = "Recyclable";
            else if (gName.Contains("non")) acceptedTag = "NonBiodegradable";
        }

        if (!string.IsNullOrEmpty(acceptedTag))
        {
            if (!targets.ContainsKey(acceptedTag) || targets[acceptedTag] < totalItemsNeeded)
            {
                targets[acceptedTag] = totalItemsNeeded;
            }

            if (!currentCounts.ContainsKey(acceptedTag))
            {
                currentCounts[acceptedTag] = 0;
            }

            if (fillImage != null)
            {
                progressBars[acceptedTag] = fillImage;
                int target = targets.ContainsKey(acceptedTag) ? targets[acceptedTag] : 0;
                fillImage.fillAmount = target > 0 ? Mathf.Clamp01((float)currentCounts[acceptedTag] / target) : 0f;
            }

            if (countText == null && fillImage != null && fillImage.transform.parent != null)
            {
                countText = fillImage.transform.parent.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (countText != null)
            {
                countLabels[acceptedTag] = countText;
                countText.text = $"{currentCounts[acceptedTag]}/{targets[acceptedTag]}";
            }
        }

        isQuestComplete = false;
        IsRepeatQuest = false;
        if (binVisualLayer == null) binVisualLayer = GetComponent<BinVisualTrashLayer>();
        depositedInThisBinCount = 0;
    }

    public bool TryProcessTrashItem(TrashItem trash)
    {
        if (trash == null || isQuestComplete || !TrashItem.AreTrashItemsAllowed) return false;
        if (trash.IsDeposited || trash.IsOnDepositCooldown) return false;

        bool isCorrectCategory = IsItemMatchingCategory(trash.gameObject, acceptedTag);
        if (isCorrectCategory)
        {
            HandleCorrectTrash(trash.gameObject);
        }
        else
        {
            HandleWrongTrash(trash.gameObject);
        }
        return true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isQuestComplete || !TrashItem.AreTrashItemsAllowed) return;

        TrashItem trash = other.GetComponent<TrashItem>();
        if (trash == null) return; // Ignore if it is not trash
        if (trash.IsDeposited || trash.IsOnDepositCooldown) return;

        bool isCorrectCategory = IsItemMatchingCategory(other.gameObject, acceptedTag);

        if (isCorrectCategory)
        {
            HandleCorrectTrash(other.gameObject);
        }
        else
        {
            HandleWrongTrash(other.gameObject);
        }
    }

    private bool IsItemMatchingCategory(GameObject item, string categoryTag)
    {
        if (string.IsNullOrEmpty(categoryTag)) return true; // Accept if untagged fallback

        // 1. Tag comparison
        if (item.CompareTag(categoryTag)) return true;

        // 2. Flexible name-based fallback matching
        string itemName = item.name.ToLower();
        string catLower = categoryTag.ToLower();

        if (catLower.Contains("bio") && !catLower.Contains("non"))
        {
            if (itemName.Contains("bio") || itemName.Contains("apple") || itemName.Contains("banana") || itemName.Contains("food") || itemName.Contains("organic") || itemName.Contains("leaf") || itemName.Contains("wood") || itemName.Contains("applecore")) return true;
        }
        else if (catLower.Contains("recyc"))
        {
            if (itemName.Contains("recyc") || itemName.Contains("can") || itemName.Contains("bottle") || itemName.Contains("paper") || itemName.Contains("cardboard") || itemName.Contains("plastic") || itemName.Contains("glass") || itemName.Contains("trash_01")) return true;
        }
        else if (catLower.Contains("non"))
        {
            if (itemName.Contains("non") || itemName.Contains("bag") || itemName.Contains("wrapper") || itemName.Contains("styrofoam") || itemName.Contains("battery") || itemName.Contains("e-waste") || itemName.Contains("trash_02")) return true;
        }

        return false;
    }

    public static string GetCorrectCategoryHint(GameObject item)
    {
        if (item == null) return "the correct bin";
        string tag = item.tag;
        string name = item.name.ToLower();

        if (tag.Equals("Biodegradable", System.StringComparison.OrdinalIgnoreCase) ||
            (!name.Contains("non") && (name.Contains("bio") || name.Contains("apple") || name.Contains("banana") || name.Contains("food") || name.Contains("organic") || name.Contains("leaf") || name.Contains("wood") || name.Contains("applecore"))))
        {
            return "Green Bin (Biodegradable / Nabubulok)";
        }
        else if (tag.Equals("Recyclable", System.StringComparison.OrdinalIgnoreCase) ||
            (name.Contains("recyc") || name.Contains("can") || name.Contains("bottle") || name.Contains("paper") || name.Contains("cardboard") || name.Contains("plastic") || name.Contains("glass") || name.Contains("trash_01")))
        {
            return "Blue Bin (Recyclable / Pwedeng Gamitin Muli)";
        }
        else if (tag.Equals("Non-Biodegradable", System.StringComparison.OrdinalIgnoreCase) ||
            (name.Contains("non") || name.Contains("bag") || name.Contains("wrapper") || name.Contains("styrofoam") || name.Contains("battery") || name.Contains("e-waste") || name.Contains("trash_02")))
        {
            return "Yellow Bin (Non-Biodegradable / Hindi Nabubulok)";
        }
        return "the matching colored bin";
    }

    private void HandleWrongTrash(GameObject trashObject)
    {
        TrashItem trash = trashObject.GetComponent<TrashItem>();
        if (trash != null)
        {
            trash.SetDepositCooldown(1.0f);
        }

        string hint = GetCorrectCategoryHint(trashObject);

        if (Time.time > lastMistakeTime + 0.5f)
        {
            lastMistakeTime = Time.time;
            HandleMistake(hint);
        }
        else if (WrongTrashPopup.Instance != null)
        {
            WrongTrashPopup.Instance.Show($"💡 Hint: Put this in the {hint}!");
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayTrashDepositWrong();
        }

        DragAndDrop drag = trashObject.GetComponent<DragAndDrop>();
        if (drag != null)
        {
            drag.OnEjectedFromBin(transform);
        }
        else
        {
            Vector3 binFrontPos = transform.position + transform.forward * 1.5f + Vector3.up * 0.4f;
            Rigidbody rb = trashObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                trashObject.transform.position = binFrontPos;
                rb.AddForce((transform.forward * 2.5f + Vector3.up * 1.8f), ForceMode.Impulse);
                rb.WakeUp();
            }
            else
            {
                trashObject.transform.position = binFrontPos;
            }
        }
    }

    private void HandleCorrectTrash(GameObject trashObject)
    {
        TrashItem trash = trashObject.GetComponent<TrashItem>();
        if (trash != null)
        {
            trash.IsDeposited = true;
        }

        if (string.IsNullOrEmpty(acceptedTag)) acceptedTag = "Recyclable";

        if (!targets.ContainsKey(acceptedTag))
        {
            targets[acceptedTag] = totalItemsNeeded;
        }

        if (!currentCounts.ContainsKey(acceptedTag))
        {
            currentCounts[acceptedTag] = 0;
        }

        bool canIncrement = currentCounts[acceptedTag] < targets[acceptedTag];
        if (canIncrement)
        {
            currentCounts[acceptedTag]++;
            Debug.Log($"[QuestManager] '{gameObject.name}' HandleCorrectTrash: '{trashObject.name}' -> acceptedTag='{acceptedTag}', count={currentCounts[acceptedTag]}/{targets[acceptedTag]}");
        }
        else
        {
            Debug.Log($"[QuestManager] '{gameObject.name}' HandleCorrectTrash (Over-quota): '{trashObject.name}' -> category already at max {targets[acceptedTag]}");
        }

        depositedInThisBinCount++;
        if (binVisualLayer != null)
        {
            binVisualLayer.SetDepositCount(depositedInThisBinCount, animatePop: true);
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayTrashDepositCorrect();
        }

        int slotIndex = depositedInBin.Count;
        Vector3 slotPos = GetDepositSlotPosition(slotIndex);
        Quaternion slotRot = GetDepositSlotRotation(slotIndex);

        DragAndDrop drag = trashObject.GetComponent<DragAndDrop>();
        if (drag != null)
        {
            drag.OnDepositedCorrectly(slotPos, slotRot, this);
        }
        else
        {
            trashObject.SetActive(false);
        }

        depositedInBin.Add(trashObject);
        while (depositedInBin.Count > maxDepositedItemsVisible)
        {
            var oldest = depositedInBin[0];
            depositedInBin.RemoveAt(0);
            if (oldest != null) oldest.SetActive(false);
        }

        if (!collectedTrash.Contains(trashObject))
        {
            collectedTrash.Add(trashObject);
        }

        if (canIncrement)
        {
            if (progressBars.ContainsKey(acceptedTag) && progressBars[acceptedTag] != null)
            {
                int target = targets.ContainsKey(acceptedTag) ? targets[acceptedTag] : 0;
                float fill = target > 0 ? Mathf.Clamp01((float)currentCounts[acceptedTag] / target) : 0f;
                progressBars[acceptedTag].fillAmount = fill;
            }

            QuestPanelToggleController.Instance?.RefreshProgressBars();

            if (countLabels.ContainsKey(acceptedTag) && countLabels[acceptedTag] != null)
            {
                countLabels[acceptedTag].text = $"{currentCounts[acceptedTag]}/{targets[acceptedTag]}";
            }

            CheckForWin();
        }
    }

    private void HandleMistake(string hint = null)
    {
        mistakes++;
        Debug.Log($"Mistake! {mistakes}/3");

        string msg;
        if (mistakes >= 3)
        {
            PlayerCurrency bank = FindFirstObjectByType<PlayerCurrency>();
            if (bank != null)
            {
                bank.DeductGold(10);
                Debug.Log("Deducted 10 gold for 3 mistakes.");
            }
            msg = "⚠️ 3 Mistakes reached! -10 Eco-Coins deducted. Please check bin colors!";
            mistakes = 0;
        }
        else
        {
            if (!string.IsNullOrEmpty(hint))
            {
                msg = $"💡 Hint: Put this in the {hint}! • Mistake {mistakes}/3";
            }
            else
            {
                msg = $"Oops! Mistake {mistakes}/3 — try the matching colored bin!";
            }
        }

        if (WrongTrashPopup.Instance != null)
        {
            WrongTrashPopup.Instance.Show(msg);
        }
    }

    private bool ShouldRunPostTestOnRepeat()
    {
        if (repeatQuestRunsPostTest) return true;
        if (QuizManager.Instance != null && QuizManager.Instance.repeatQuestRunsPostTest) return true;
        if (IntroSequenceController.Instance != null && IntroSequenceController.Instance.repeatQuestRunsPostTest) return true;
        var npc = FindFirstObjectByType<NPCInteraction>();
        if (npc != null && npc.repeatQuestRunsPostTest) return true;
        var allBins = FindObjectsByType<QuestManager>(FindObjectsSortMode.None);
        foreach (var b in allBins)
        {
            if (b != null && b.repeatQuestRunsPostTest) return true;
        }
        return false;
    }

    private void CheckForWin()
    {
        foreach (var kvp in targets)
        {
            string tag = kvp.Key;
            int targetCount = kvp.Value;

            if (!currentCounts.ContainsKey(tag) || currentCounts[tag] < targetCount)
            {
                return;
            }
        }

        isQuestComplete = true;
        Debug.Log("Quest Complete!");

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayQuestComplete();
        }

        if (questCanvas != null) questCanvas.SetActive(false);
        if (IntroSequenceController.Instance != null)
        {
            IntroSequenceController.Instance.HideQuestTab();
        }
        MinimapIcon.SetTrashIconsActive(false);

        int goldReward = Random.Range(100, 150);
        PlayerCurrency bank = FindFirstObjectByType<PlayerCurrency>();
        if (bank != null) bank.AddGold(goldReward);

        NPCInteraction.NotifyAllQuestCompleted();
        TravelGate.NotifyTrashQuestCompleted();

        if (QuizManager.Instance != null)
        {
            if (!IsRepeatQuest || ShouldRunPostTestOnRepeat())
            {
                QuizManager.Instance.ResetPostQuizOnly();
                QuizManager.Instance.StartPostQuiz();
            }
            else
            {
                QuizManager.Instance.ShowRepeatQuestComplete(goldReward);
            }
        }
    }

    public void ResetQuest()
    {
        mistakes = 0;
        isQuestComplete = false;

        if (QuizManager.Instance != null)
        {
            QuizManager.Instance.ResetPostQuizOnly();
        }

        TrashBin bin = GetComponent<TrashBin>();
        if (bin != null)
        {
            bin.ResetBin();
        }

        List<string> keys = new List<string>(currentCounts.Keys);
        foreach (string key in keys)
        {
            currentCounts[key] = 0;
            if (progressBars.ContainsKey(key) && progressBars[key] != null)
            {
                progressBars[key].fillAmount = 0f;
            }
            if (countLabels.ContainsKey(key) && countLabels[key] != null && targets.ContainsKey(key))
            {
                countLabels[key].text = $"0/{targets[key]}";
            }
        }

        QuestPanelToggleController.Instance?.RefreshProgressBars();

        if (questCanvas != null && (IntroSequenceController.Instance == null || IsRepeatQuest))
        {
            questCanvas.SetActive(true);
        }

        foreach (GameObject t in collectedTrash)
        {
            if (t != null)
            {
                TrashItem script = t.GetComponent<TrashItem>();
                if (script != null) script.ResetTrash();
                else t.SetActive(true);
            }
        }
        collectedTrash.Clear();
        TrashItem.ResetAllTrashItems();
    }
}