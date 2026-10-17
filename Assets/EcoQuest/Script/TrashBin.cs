using UnityEngine;

public class TrashBin : MonoBehaviour
{
    [Header("Bin Visuals")]
    [Tooltip("Drag the specific trash layer for THIS bin here.")]
    public GameObject trashLayer;

    [Header("Bin Settings")]
    [Tooltip("How many trash objects until the bin is 100% full?")]
    public int maxCapacity = 5; 
    
    [Tooltip("Type the tag this bin should accept (e.g., Biodegradable)")]
    public string acceptedTag;
    
    private int currentTrashCount = 0;
    private Vector3 fullScale;

    private void Awake()
    {
        MinimapIcon icon = GetComponent<MinimapIcon>();
        if (icon != null)
        {
            if (Application.isPlaying) Destroy(icon);
            else DestroyImmediate(icon);
        }
    }

    void Start()
    {
        if (trashLayer != null)
        {
            fullScale = trashLayer.transform.localScale;
            trashLayer.transform.localScale = new Vector3(fullScale.x, 0, fullScale.z);
            trashLayer.SetActive(false);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // Now it checks for the tag you type in the Inspector
        if (other.CompareTag(acceptedTag))
        {
            ProcessTrash(other.gameObject);
        }
    }

    private void ProcessTrash(GameObject trashObject)
    {
        if (currentTrashCount < maxCapacity)
        {
            currentTrashCount++;
            if (GetComponent<QuestManager>() == null)
            {
                Destroy(trashObject);
            }
            UpdateLayerVisual();
        }
        else
        {
            Debug.Log(gameObject.name + " is completely full!");
        }
    }

    public void ResetBin()
    {
        currentTrashCount = 0;
        if (trashLayer != null)
        {
            trashLayer.transform.localScale = new Vector3(fullScale.x, 0f, fullScale.z);
            trashLayer.SetActive(false);
        }
    }

    private void UpdateLayerVisual()
    {
        if (trashLayer != null)
        {
            trashLayer.SetActive(true);
            float fillPercentage = (float)currentTrashCount / maxCapacity;
            trashLayer.transform.localScale = new Vector3(fullScale.x, fullScale.y * fillPercentage, fullScale.z);
        }
    }
}