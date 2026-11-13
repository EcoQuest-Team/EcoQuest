using UnityEngine;
using TMPro;
using System.Collections;

public class PlayerCurrency : MonoBehaviour
{
    private const string PrefsKey = "PlayerGold";
    private const string LegacyPrefsKey = "PlayerCoins";

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI coinText; 
    
    [SerializeField] private TextMeshProUGUI popupText; 

    public static PlayerCurrency Instance { get; private set; }

    private int currentGold = 0;
    private Coroutine popupCoroutine;
    private Coroutine countUpCoroutine;

    public int CurrentGold => currentGold;

    private void Awake()
    {
        Instance = this;
        currentGold = PlayerPrefs.GetInt(PrefsKey, PlayerPrefs.GetInt(LegacyPrefsKey, 0));
    }

    private void OnEnable()
    {
        UpdateGoldUI();
    }

    private void Start()
    {
        UpdateGoldUI();
        
        if (popupText != null)
        {
            popupText.gameObject.SetActive(false);
        }
    }

    private void SaveGold()
    {
        PlayerPrefs.SetInt(PrefsKey, currentGold);
        PlayerPrefs.SetInt(LegacyPrefsKey, currentGold);
        PlayerPrefs.Save();
    }

    public void AddGold(int amount)
    {
        currentGold += amount;
        SaveGold();
        UpdateGoldUI();
        
        ShowPopup("+" + amount.ToString(), Color.green);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayCoinEarn();
    }

    public void AddGoldAnimated(int amount, float duration = 0.9f)
    {
        int startGold = currentGold;
        currentGold += amount;
        SaveGold();
        ShowPopup("+" + amount.ToString(), Color.green);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayCoinEarn();

        if (coinText == null || !isActiveAndEnabled || duration <= 0f)
        {
            UpdateGoldUI();
            return;
        }

        if (countUpCoroutine != null) StopCoroutine(countUpCoroutine);
        countUpCoroutine = StartCoroutine(AnimateCountUp(startGold, currentGold, duration));
    }

    private IEnumerator AnimateCountUp(int fromVal, int toVal, float duration)
    {
        RectTransform rt = coinText.rectTransform;
        Vector3 baseScale = Vector3.one;
        float elapsed = 0f;
        int lastShown = -1;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            int val = Mathf.RoundToInt(Mathf.Lerp(fromVal, toVal, ease));
            if (val != lastShown)
            {
                lastShown = val;
                coinText.text = val.ToString();
            }
            float punch = 1f + 0.25f * Mathf.Sin(t * Mathf.PI);
            if (rt != null) rt.localScale = baseScale * punch;
            yield return null;
        }

        coinText.text = toVal.ToString();
        if (rt != null) rt.localScale = baseScale;
        countUpCoroutine = null;
    }

    public void DeductGold(int amount)
    {
        currentGold -= amount;
        if (currentGold < 0) currentGold = 0; 
        SaveGold();
        UpdateGoldUI();
        
        ShowPopup("-" + amount.ToString(), Color.red);
    }

    public void ResetGoldToZero()
    {
        currentGold = 0;
        if (PlayerPrefs.HasKey(PrefsKey)) PlayerPrefs.DeleteKey(PrefsKey);
        if (PlayerPrefs.HasKey(LegacyPrefsKey)) PlayerPrefs.DeleteKey(LegacyPrefsKey);
        PlayerPrefs.Save();
        UpdateGoldUI();
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
