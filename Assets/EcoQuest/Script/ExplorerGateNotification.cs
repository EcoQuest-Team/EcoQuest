using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages kid-friendly banner notifications for the Explorer Gate and City arrival.
/// Styled according to the shared theme: dark green semi-transparent background (#0A2A1A, alpha 0.92)
/// with bright eco-green border (#52D172), 4.5s display, and unscaled fade transitions.
/// Guaranteed never to overlap active modals or dialogues.
/// </summary>
public class ExplorerGateNotification : MonoBehaviour
{
    private static ExplorerGateNotification instance;
    public static ExplorerGateNotification Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<ExplorerGateNotification>();
                if (instance == null)
                {
                    var uiRoot = GameObject.Find("--- UI ---/UI_Notification");
                    if (uiRoot == null) uiRoot = GameObject.Find("UI_Notification");
                    if (uiRoot != null)
                    {
                        instance = uiRoot.AddComponent<ExplorerGateNotification>();
                    }
                    else if (Application.isPlaying)
                    {
                        var go = new GameObject("[ExplorerGateNotification]");
                        instance = go.AddComponent<ExplorerGateNotification>();
                    }
                }
            }
            return instance;
        }
    }

    [Header("Notification UI References")]
    [SerializeField] private CanvasGroup bannerCanvasGroup;
    [SerializeField] private RectTransform bannerRect;
    [SerializeField] private TextMeshProUGUI bannerText;
    [SerializeField] private Image bannerBackground;

    [Header("Audio")]
    [SerializeField] private AudioClip notificationClip;

    private Coroutine activeNotificationRoutine;
    private bool isCheckingGate = false;

    private void Awake()
    {
        if (instance == null) instance = this;
        EnsureUIElements();
    }

    private void OnEnable()
    {
        TravelGate.OnGateUnlocked += HandleGateUnlocked;
    }

    private void OnDisable()
    {
        TravelGate.OnGateUnlocked -= HandleGateUnlocked;
    }

    private void Start()
    {
        EnsureUIElements();
        // Check if gate is already unlocked on scene start (e.g. returning to Forest)
        if (TravelGate.CanTravelToCity && PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestExplorerGateNotified, 0) == 0)
        {
            QueueGateNotification();
        }
    }

    private void Update()
    {
        if (!isCheckingGate && TravelGate.CanTravelToCity && PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestExplorerGateNotified, 0) == 0)
        {
            QueueGateNotification();
        }
    }

    private void HandleGateUnlocked()
    {
        if (PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestExplorerGateNotified, 0) == 0)
        {
            QueueGateNotification();
        }
    }

    public void QueueGateNotification()
    {
        if (isCheckingGate) return;
        StartCoroutine(WaitForModalAndNotifyRoutine());
    }

    private IEnumerator WaitForModalAndNotifyRoutine()
    {
        isCheckingGate = true;

        // Wait until no modal or dialogue is active
        while (IsModalOrDialogueActive())
        {
            yield return new WaitForSecondsRealtime(0.25f);
        }

        // Brief delay for modal exit animation to finish
        yield return new WaitForSecondsRealtime(0.4f);

        // Double-check gating conditions
        if (TravelGate.CanTravelToCity && PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestExplorerGateNotified, 0) == 0)
        {
            PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestExplorerGateNotified, 1);
            PlayerPrefs.Save();
            ShowNotification("Quest complete! Talk to the Explorer to travel to the City.", 4.5f);
        }

        isCheckingGate = false;
    }

    private bool IsModalOrDialogueActive()
    {
        if (ModalManager.Instance != null && ModalManager.Instance.IsAnyModalActuallyVisible())
            return true;
        if (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue)
            return true;
        return false;
    }

    public static void ShowImmediateNotification(string message, float duration = 4.5f)
    {
        if (Instance != null)
        {
            Instance.ShowNotification(message, duration);
        }
    }

    public void ShowNotification(string message, float duration = 4.5f)
    {
        EnsureUIElements();
        if (activeNotificationRoutine != null)
        {
            StopCoroutine(activeNotificationRoutine);
        }
        activeNotificationRoutine = StartCoroutine(AnimateNotificationRoutine(message, duration));
    }

    private IEnumerator AnimateNotificationRoutine(string message, float duration)
    {
        if (bannerText != null)
        {
            bannerText.text = message;
        }

        // Play sound if assigned or buttonClick / ready audio
        if (notificationClip != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(notificationClip);
        }
        else if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }

        // Fade In (0.35s)
        if (bannerRect != null) bannerRect.gameObject.SetActive(true);
        float t = 0f;
        while (t < 0.35f)
        {
            t += Time.unscaledDeltaTime;
            float norm = Mathf.Clamp01(t / 0.35f);
            if (bannerCanvasGroup != null)
            {
                bannerCanvasGroup.alpha = Mathf.SmoothStep(0f, 1f, norm);
            }
            if (bannerRect != null)
            {
                bannerRect.localScale = Vector3.Lerp(new Vector3(0.92f, 0.92f, 1f), Vector3.one, Mathf.SmoothStep(0f, 1f, norm));
            }
            yield return null;
        }

        if (bannerCanvasGroup != null) bannerCanvasGroup.alpha = 1f;
        if (bannerRect != null) bannerRect.localScale = Vector3.one;

        // Display hold
        yield return new WaitForSecondsRealtime(duration);

        // Fade Out (0.35s)
        t = 0f;
        while (t < 0.35f)
        {
            t += Time.unscaledDeltaTime;
            float norm = Mathf.Clamp01(t / 0.35f);
            if (bannerCanvasGroup != null)
            {
                bannerCanvasGroup.alpha = Mathf.SmoothStep(1f, 0f, norm);
            }
            if (bannerRect != null)
            {
                bannerRect.localScale = Vector3.Lerp(Vector3.one, new Vector3(0.95f, 0.95f, 1f), norm);
            }
            yield return null;
        }

        if (bannerCanvasGroup != null) bannerCanvasGroup.alpha = 0f;
        if (bannerRect != null) bannerRect.gameObject.SetActive(false);
        activeNotificationRoutine = null;
    }

    public void EnsureUIElements()
    {
        if (bannerRect != null && bannerCanvasGroup != null && bannerText != null) return;

        // Find or create notification panel
        Transform existing = transform.Find("ExplorerGateNotificationBanner");
        GameObject bannerObj;
        if (existing != null)
        {
            bannerObj = existing.gameObject;
        }
        else
        {
            bannerObj = new GameObject("ExplorerGateNotificationBanner");
            bannerObj.transform.SetParent(transform, false);
        }

        bannerRect = bannerObj.GetComponent<RectTransform>();
        if (bannerRect == null) bannerRect = bannerObj.AddComponent<RectTransform>();

        // Center-top banner positioning
        bannerRect.anchorMin = new Vector2(0.5f, 1f);
        bannerRect.anchorMax = new Vector2(0.5f, 1f);
        bannerRect.pivot = new Vector2(0.5f, 1f);
        bannerRect.anchoredPosition = new Vector2(0f, -80f);
        bannerRect.sizeDelta = new Vector2(740f, 72f);

        bannerCanvasGroup = bannerObj.GetComponent<CanvasGroup>();
        if (bannerCanvasGroup == null) bannerCanvasGroup = bannerObj.AddComponent<CanvasGroup>();
        bannerCanvasGroup.alpha = 0f;
        bannerCanvasGroup.blocksRaycasts = false;
        bannerCanvasGroup.interactable = false;

        bannerBackground = bannerObj.GetComponent<Image>();
        if (bannerBackground == null) bannerBackground = bannerObj.AddComponent<Image>();
        bannerBackground.color = new Color(0.04f, 0.16f, 0.10f, 0.94f); // #0A2A1A dark eco green

        // Text
        Transform textTrans = bannerObj.transform.Find("Text");
        GameObject textObj = textTrans != null ? textTrans.gameObject : new GameObject("Text");
        textObj.transform.SetParent(bannerObj.transform, false);

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        if (textRect == null) textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = new Vector2(-40f, -12f);
        textRect.anchoredPosition = Vector2.zero;

        bannerText = textObj.GetComponent<TextMeshProUGUI>();
        if (bannerText == null) bannerText = textObj.AddComponent<TextMeshProUGUI>();
        bannerText.fontSize = 24f;
        bannerText.alignment = TextAlignmentOptions.Center;
        bannerText.color = new Color(0.92f, 1.0f, 0.92f, 1f);
        bannerText.text = "";

        bannerObj.SetActive(false);
    }
}
