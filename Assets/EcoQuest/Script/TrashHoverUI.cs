using UnityEngine;
using TMPro;

public class TrashHoverUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The main camera in your scene")]
    public Camera mainCamera;
    
    [Tooltip("The UI Text element that will display the name")]
    public TextMeshProUGUI hoverText; 

    private string[] validTrashTags = { "Biodegradable", "Recyclable", "NonBiodegradable" };

    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        
        if (hoverText != null) hoverText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (hoverText == null) return;
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            if (IsValidTrash(hit.collider.tag))
            {
                hoverText.gameObject.SetActive(true);
                
                hoverText.text = hit.collider.gameObject.name;
            }
            else
            {
                hoverText.gameObject.SetActive(false);
            }
        }
        else
        {
            hoverText.gameObject.SetActive(false);
        }
    }

    private bool IsValidTrash(string objectTag)
    {
        foreach (string tag in validTrashTags)
        {
            if (objectTag == tag) return true;
        }
        return false;
    }
}