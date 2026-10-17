using UnityEngine;
using TMPro;
using System.Collections;

public class PlayerCurrency : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI coinText; 
    
    [SerializeField] private TextMeshProUGUI popupText; 

    private int currentGold = 0;
    private Coroutine popupCoroutine;

    private void Start()
    {
        UpdateGoldUI();
        
        if (popupText != null)
        {
            popupText.gameObject.SetActive(false);
        }
    }

    public void AddGold(int amount)
    {
        currentGold += amount;
        UpdateGoldUI();
        
        ShowPopup("+" + amount.ToString(), Color.green);
    }

    public void DeductGold(int amount)
    {
        currentGold -= amount;
        if (currentGold < 0) currentGold = 0; 
        UpdateGoldUI();
        
        ShowPopup("-" + amount.ToString(), Color.red);
    }

    private void UpdateGoldUI()
    {
        if (coinText != null)
        {
            coinText.text = currentGold.ToString();
        }
    }


    private void ShowPopup(string message, Color color)
    {
        if (popupText == null) return;

        if (popupCoroutine != null)
        {
            StopCoroutine(popupCoroutine);
        }

        popupCoroutine = StartCoroutine(AnimatePopup(message, color));
    }

    private IEnumerator AnimatePopup(string message, Color targetColor)
    {
        popupText.text = message;
        popupText.gameObject.SetActive(true);

        RectTransform rectTransform = popupText.GetComponent<RectTransform>();
        Vector2 startPos = rectTransform.anchoredPosition;

        float duration = 1.5f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float percent = elapsed / duration;

            float alpha = Mathf.Lerp(1f, 0f, percent);
            popupText.color = new Color(targetColor.r, targetColor.g, targetColor.b, alpha);

            rectTransform.anchoredPosition = startPos + new Vector2(0, percent * 50f);

            yield return null;
        }

        popupText.gameObject.SetActive(false);
        rectTransform.anchoredPosition = startPos; 
    }
}