using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class QuestManager : MonoBehaviour
{
    [Header("Trash Bin Settings")]
    [Tooltip("What kind of trash does THIS specific bin accept?")]
    [SerializeField] private string acceptedTag; 

    [Header("Quest UI Settings")]
    [Tooltip("Assign the progress bar for THIS specific category.")]
    [SerializeField] private Image fillImage; 
    [Tooltip("How many items does this category need total?")]
    [SerializeField] private int totalItemsNeeded = 3; 
    
    [Header("Global Settings")]
    [SerializeField] private GameObject questCanvas;

    private static Dictionary<string, int> currentCounts = new Dictionary<string, int>();
    private static Dictionary<string, int> targets = new Dictionary<string, int>();
    private static Dictionary<string, Image> progressBars = new Dictionary<string, Image>();
    private static List<GameObject> collectedTrash = new List<GameObject>();
    
    private static int mistakes = 0;
    private static float lastMistakeTime = 0f;
    private static bool isQuestComplete = false;

    private void Awake()
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
            fillImage.fillAmount = 0f;
        }

        isQuestComplete = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isQuestComplete) return;

        TrashItem trash = other.GetComponent<TrashItem>();
        if (trash == null) return; // Ignore if it is not trash

        if (other.CompareTag(acceptedTag))
        {
            HandleCorrectTrash(other.gameObject);
        }
        else
        {
            if (Time.time > lastMistakeTime + 0.5f)
            {
                lastMistakeTime = Time.time;
                HandleMistake();
            }
        }
    }

    private void HandleCorrectTrash(GameObject trashObject)
    {
        if (!targets.ContainsKey(acceptedTag)) return;

        if (currentCounts[acceptedTag] >= targets[acceptedTag]) return;

        currentCounts[acceptedTag]++;
        
        trashObject.SetActive(false);
        collectedTrash.Add(trashObject);

        if (progressBars.ContainsKey(acceptedTag) && progressBars[acceptedTag] != null)
        {
            progressBars[acceptedTag].fillAmount = (float)currentCounts[acceptedTag] / targets[acceptedTag];
        }

        CheckForWin();
    }

    private void HandleMistake()
    {
        mistakes++;
        Debug.Log($"Mistake! {mistakes}/3");

        if (mistakes >= 3)
        {
            PlayerCurrency bank = FindFirstObjectByType<PlayerCurrency>();
            if (bank != null)
            {
                bank.DeductGold(10);
                Debug.Log("Deducted 10 gold for 3 mistakes.");
            }
            mistakes = 0;
        }
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

        if (questCanvas != null) questCanvas.SetActive(false);

        PlayerCurrency bank = FindFirstObjectByType<PlayerCurrency>();
        if (bank != null) bank.AddGold(Random.Range(100, 150));
    }

    public void ResetQuest()
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
        }

        if (questCanvas != null) questCanvas.SetActive(true);

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
    }
}