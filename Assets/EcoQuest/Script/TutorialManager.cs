using System.Collections;
using UnityEngine;
using TMPro;

public class TutorialManager : MonoBehaviour
{
    [Header("Main Background")]
    public GameObject tutorialOverlay;

    [Header("Your Tutorial Cards")]
    public GameObject[] tutorialCards; 

    [Header("Single Card & Close Events")]
    public bool singleCardMode = false;
    public UnityEngine.Events.UnityEvent OnLastCardReachedEvent;
    public event System.Action OnLastCardReached;
    public UnityEngine.Events.UnityEvent OnTutorialClosedEvent;
    public event System.Action OnTutorialClosed;

#if UNITY_EDITOR
    public static bool EnableCardTrace = false;
#endif
    private string activeFlowToken = null;

    private int currentCardIndex = 0;
    private bool isTutorialActive = false; 
    private bool isTransitioning = false;
    private float cardOpenedTime = 0f;
    private const float INPUT_IGNORE_DURATION = 0.50f;
    private int lastCardAdvanceFrame = -1;

    [Header("Input Options")]
    [Tooltip("If true, clicking anywhere on the info card advances to next card.")]
    public bool clickAnywhereToContinue = true;

    [Header("Transition Settings (Unscaled Time)")]
    [SerializeField] private float cardOpenDuration = 0.28f;
    [SerializeField] private float cardCloseDuration = 0.18f;
    [SerializeField] private float cardSwitchDelay = 0.12f;
    [SerializeField] private float cardStartScale = 0.95f;
    [SerializeField] private AnimationCurve cardOpenCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve cardCloseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    private Coroutine cardAnimRoutine;

    private void OnDisable()
    {
        if (cardAnimRoutine != null)
        {
            StopCoroutine(cardAnimRoutine);
            cardAnimRoutine = null;
        }
        isTransitioning = false;
    }

    private void OnDestroy()
    {
        if (cardAnimRoutine != null)
        {
            StopCoroutine(cardAnimRoutine);
            cardAnimRoutine = null;
        }
        if (tutorialCards != null)
        {
            foreach (var card in tutorialCards)
            {
                if (card == null) continue;
                var kcp = card.GetComponentInChildren<KeyCapPrompt>(true);
                if (kcp != null) kcp.OnPromptClicked -= HandleCardAdvance;
            }
        }
    }

    void Awake()
    {
        CleanseCardsList();
    }

    void Start()
    {
        CleanseCardsList();
        HideTutorial();
    }

    private void CleanseCardsList()
    {
        if (tutorialCards == null) return;
        var validCards = new System.Collections.Generic.List<GameObject>();
        for (int i = 0; i < tutorialCards.Length; i++)
        {
            if (tutorialCards[i] != null)
            {
                validCards.Add(tutorialCards[i]);
            }
        }
        if (validCards.Count != tutorialCards.Length)
        {
            tutorialCards = validCards.ToArray();
        }
    }

    void Update()
    {
        if (isTutorialActive && !isTransitioning)
        {
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return))
            {
                HandleCardAdvance();
            }
        }
    }

    public void HandleCardAdvance()
    {
        if (Time.frameCount == lastCardAdvanceFrame) return; // Prevent double trigger in same frame
        if (!isTutorialActive || isTransitioning) return;
        if (Time.unscaledTime - cardOpenedTime < INPUT_IGNORE_DURATION) return; // Respect 0.5s lock
        lastCardAdvanceFrame = Time.frameCount;

        if (singleCardMode || (tutorialCards != null && currentCardIndex >= tutorialCards.Length - 1))
        {
            HideTutorial();
        }
        else
        {
            StartCoroutine(TransitionToNextCard());
        }
    }

    public void ShowTutorial(int startingIndex = 0, bool singleCard = false, string flowToken = null)
    {
        CleanseCardsList();

        if (tutorialCards == null || tutorialCards.Length == 0)
        {
            Debug.LogWarning("[TutorialManager] No valid tutorial cards to display!");
            HideTutorial();
            return;
        }

        activeFlowToken = flowToken;
        singleCardMode = singleCard;
        currentCardIndex = Mathf.Clamp(startingIndex, 0, tutorialCards.Length - 1);
        cardOpenedTime = Time.unscaledTime;
        UpdateCardDisplay();

        if (tutorialOverlay != null)
        {
            tutorialOverlay.SetActive(true);
            var overlayImg = tutorialOverlay.GetComponent<UnityEngine.UI.Image>();
            if (overlayImg != null && !(UITheme.Instance != null && UITheme.Instance.useLegacyPopups))
            {
                // Avoid double-darkening over ModalManager's ModalBackdrop (#0000008C)
                overlayImg.color = new Color(0f, 0f, 0f, 0f);
                overlayImg.raycastTarget = false;
            }
        }
        // Keep the world alive (NPC animations, idle movement, ambient effects) while blocking ONLY player input
        Time.timeScale = 1f;
        IntroSequenceController.Instance?.SetPlayerControlsFrozen(true);
        IntroSequenceController.Instance?.SetCursorState(true);
        WorldInteractionPrompt.Instance?.SetBlocked(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (CenterScreenCursor.Instance != null)
        {
            CenterScreenCursor.Instance.SetVisible(false);
        }
        
        isTutorialActive = true; 
        isTransitioning = false;

        if (currentCardIndex >= 0 && currentCardIndex < tutorialCards.Length && tutorialCards[currentCardIndex] != null)
        {
            if (cardAnimRoutine != null) StopCoroutine(cardAnimRoutine);
            cardAnimRoutine = StartCoroutine(AnimateCardInRoutine(tutorialCards[currentCardIndex], cardOpenDuration));
        }

        string currentCardId = (tutorialCards != null && currentCardIndex < tutorialCards.Length && tutorialCards[currentCardIndex] != null)
            ? tutorialCards[currentCardIndex].name
            : "BinCard";

#if UNITY_EDITOR
        if (EnableCardTrace)
        {
            var caller = new System.Diagnostics.StackFrame(1).GetMethod();
            Debug.Log($"[CardTrace] ShowCard: cardId='{currentCardId}', flowToken='{flowToken}', caller='{caller?.DeclaringType?.Name}.{caller?.Name}', frame={Time.frameCount}");
        }
#endif

        GameObject modalTarget = tutorialOverlay != null ? tutorialOverlay : gameObject;
        if (ModalManager.Instance != null && ModalManager.Instance.ActiveModal?.modalObject != modalTarget)
        {
            ModalManager.Instance.RequestModal(ModalManager.ModalType.InfoCard, modalTarget, null, () => HideTutorial(), priority: 4, cardId: currentCardId, flowToken: flowToken);
        }
    }

    public void RestyleCard(GameObject card)
    {
        if (card == null) return;
        bool useLegacy = UITheme.Instance != null && UITheme.Instance.useLegacyPopups;
        if (useLegacy) return;

        // Ensure root CanvasGroup is exactly 1.0 at rest
        var cardCg = card.GetComponent<CanvasGroup>();
        if (cardCg == null) cardCg = card.AddComponent<CanvasGroup>();
        cardCg.alpha = 1.0f;

        // 1. Remove any duplicate SolidBacking Layer so CardFrame uses the exact single panel_rounded_forest_green fill (#102B1EEB) and 3px #6EBE6E border as DialoguePanel, QuestPanel, and ControlTipCardUI
        var solidBacking = card.transform.Find("SolidBacking");
        if (solidBacking != null)
        {
            solidBacking.gameObject.SetActive(false);
        }

        var frame = card.transform.Find("CardFrame")?.GetComponent<UnityEngine.UI.Image>();
        if (frame != null)
        {
            frame.transform.SetAsFirstSibling();
            frame.color = Color.white; // Exact colors of panel_rounded_forest_green (#102B1EEB fill, 3px #6EBE6E border)
            if (UITheme.Instance != null && UITheme.Instance.panelBackgroundSprite != null)
            {
                frame.sprite = UITheme.Instance.panelBackgroundSprite;
                frame.type = UnityEngine.UI.Image.Type.Sliced;
            }
        }

        // Top-left Pill Tab: "BIN INFO" in speaker tab style
        var pillTab = card.transform.Find("PillTab");
        if (pillTab == null)
        {
            var tabGo = new GameObject("PillTab", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            tabGo.transform.SetParent(card.transform, false);
            var tabRt = tabGo.GetComponent<RectTransform>();
            tabRt.anchorMin = new Vector2(0f, 1f);
            tabRt.anchorMax = new Vector2(0f, 1f);
            tabRt.pivot = new Vector2(0f, 0.5f);
            tabRt.anchoredPosition = new Vector2(32f, 0f);
            tabRt.sizeDelta = new Vector2(160f, 44f);

            var tabImg = tabGo.GetComponent<UnityEngine.UI.Image>();
            if (UITheme.Instance != null && UITheme.Instance.speakerTabGreenSprite != null)
            {
                tabImg.sprite = UITheme.Instance.speakerTabGreenSprite;
                tabImg.type = UnityEngine.UI.Image.Type.Sliced;
            }
            tabImg.color = Color.white;

            var txtGo = new GameObject("TabText", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(tabGo.transform, false);
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            var tabText = txtGo.GetComponent<TextMeshProUGUI>();
            tabText.text = "BIN INFO";
            tabText.fontSize = 22f;
            tabText.fontStyle = FontStyles.Bold;
            tabText.alignment = TextAlignmentOptions.Center;
            tabText.color = UITheme.Instance != null ? UITheme.Instance.textWhite : Color.white;
            pillTab = tabGo.transform;
        }
        if (pillTab != null)
        {
            pillTab.SetAsLastSibling();
            var pillText = pillTab.Find("TabText")?.GetComponent<TextMeshProUGUI>();
            if (pillText != null)
            {
                var titleFont = UITheme.Instance != null ? UITheme.Instance.GetTitleFont() : null;
                if (titleFont != null) pillText.font = titleFont;
                pillText.color = UITheme.Instance != null ? UITheme.Instance.textWhite : Color.white;
            }
        }

        // 2. Picture: Left column, displayed cleanly without a picture frame
        var pw = card.transform.Find("PhotoWindow");
        if (pw != null)
        {
            pw.SetSiblingIndex(2);
            var pwRt = pw.GetComponent<RectTransform>();
            if (pwRt != null)
            {
                pwRt.anchorMin = new Vector2(0f, 0.5f);
                pwRt.anchorMax = new Vector2(0f, 0.5f);
                pwRt.pivot = new Vector2(0f, 0.5f);
                pwRt.anchoredPosition = new Vector2(36f, 0f);
                pwRt.sizeDelta = new Vector2(280f, 280f);
            }

            var pwImg = pw.GetComponent<UnityEngine.UI.Image>();
            if (pwImg != null) pwImg.enabled = false;

            var outline = pw.GetComponent<UnityEngine.UI.Outline>();
            if (outline != null) Destroy(outline);

            var photoBorder = pw.Find("PhotoBorder");
            if (photoBorder != null) photoBorder.gameObject.SetActive(false);

            var photoBg = pw.Find("PhotoBackground");
            if (photoBg != null) photoBg.gameObject.SetActive(false);

            var pwMask = pw.GetComponent<UnityEngine.UI.RectMask2D>();
            if (pwMask == null) pwMask = pw.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();

            var photo = pw.Find("Photo")?.GetComponent<UnityEngine.UI.Image>();
            if (photo != null)
            {
                photo.transform.SetAsLastSibling();
                photo.color = Color.white;
                photo.material = null;
                photo.preserveAspect = true;
                var photoRt = photo.GetComponent<RectTransform>();
                if (photoRt != null)
                {
                    photoRt.anchorMin = Vector2.zero;
                    photoRt.anchorMax = Vector2.one;
                    photoRt.offsetMin = Vector2.zero;
                    photoRt.offsetMax = Vector2.zero;
                }
                if (photo.sprite != null && photo.sprite.texture != null)
                {
                    photo.sprite.texture.filterMode = FilterMode.Bilinear;
                }
            }
        }

        // Hide distracting legacy side ornaments that crowd title
        var leaf = card.transform.Find("LeafIcon");
        if (leaf != null) leaf.gameObject.SetActive(false);
        var rec = card.transform.Find("RecycleIcon");
        if (rec != null) rec.gameObject.SetActive(false);

        // 3. Title Text: one line wrapping only at spaces, shared Fredoka SDF font & white color
        var title = card.transform.Find("TitleText")?.GetComponent<TextMeshProUGUI>();
        if (title != null)
        {
            var titleFont = UITheme.Instance != null ? UITheme.Instance.GetTitleFont() : null;
            if (titleFont != null) title.font = titleFont;
            var titleOutline = title.GetComponent<UnityEngine.UI.Outline>();
            if (titleOutline != null) Destroy(titleOutline);

            title.text = (title.text ?? "").Replace("-", "\u2011");
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0.40f, 0.64f);
            titleRt.anchorMax = new Vector2(0.96f, 0.86f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;
            title.color = UITheme.Instance != null ? UITheme.Instance.textWhite : Color.white;
            title.textWrappingMode = TextWrappingModes.Normal;
            title.enableAutoSizing = true;
            title.fontSizeMin = 22f;
            title.fontSizeMax = 32f;
        }

        // 4. Body Text: never clipped, shared Nunito SDF font & UITheme.textMuted (#E3F5E3)
        var body = card.transform.Find("BodyText")?.GetComponent<TextMeshProUGUI>();
        if (body != null)
        {
            var bodyFont = UITheme.Instance != null ? UITheme.Instance.GetBodyFont() : null;
            if (bodyFont != null) body.font = bodyFont;
            var bodyOutline = body.GetComponent<UnityEngine.UI.Outline>();
            if (bodyOutline != null) Destroy(bodyOutline);

            var bodyRt = body.rectTransform;
            bodyRt.anchorMin = new Vector2(0.38f, 0.22f);
            bodyRt.anchorMax = new Vector2(0.96f, 0.64f);
            bodyRt.offsetMin = Vector2.zero;
            bodyRt.offsetMax = Vector2.zero;
            body.color = UITheme.Instance != null ? UITheme.Instance.textMuted : new Color(0.890f, 0.960f, 0.890f, 1f);
            body.enableAutoSizing = true;
            body.fontSizeMin = 22f;
            body.fontSizeMax = 28f;
            body.maxVisibleLines = int.MaxValue;
            body.overflowMode = TextOverflowModes.Overflow;

            // Expand card height up to 70% screen height so full text is visible
            var cardRt = card.GetComponent<RectTransform>();
            if (cardRt != null)
            {
                float maxHeight = Mathf.Min(Screen.height * 0.70f, 540f);
                if (cardRt.sizeDelta.y < 520f)
                {
                    cardRt.sizeDelta = new Vector2(cardRt.sizeDelta.x, 520f);
                }
            }
        }

        // 5. Shared HintRow footer bottom-right: [E] keycap and "Continue" on one level row
        var kcp = card.GetComponentInChildren<KeyCapPrompt>(true);
        if (kcp == null)
        {
            GameObject kcpPrefab = null;
#if UNITY_EDITOR
            kcpPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/Prefabs/UI/KeyCapPrompt.prefab");
#endif
            if (kcpPrefab != null)
            {
                var kcpGo = Instantiate(kcpPrefab, card.transform);
                kcpGo.name = "KeyCapPrompt";
                kcp = kcpGo.GetComponent<KeyCapPrompt>();
            }
        }

        if (kcp != null)
        {
            kcp.gameObject.SetActive(true);
            kcp.transform.SetAsLastSibling();
            kcp.SetPrompt("E", "Continue");

            var kcpRt = kcp.GetComponent<RectTransform>();
            if (kcpRt != null)
            {
                kcpRt.anchorMin = new Vector2(1f, 0f);
                kcpRt.anchorMax = new Vector2(1f, 0f);
                kcpRt.pivot = new Vector2(1f, 0f);
                kcpRt.anchoredPosition = new Vector2(-32f, 20f);
                kcpRt.sizeDelta = new Vector2(210f, 48f);
            }

            var hlg = kcp.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            if (hlg == null) hlg = kcp.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleRight;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.spacing = 12f;

            var keycap = kcp.transform.Find("Keycap") as RectTransform;
            var label = kcp.transform.Find("ActionLabel") as RectTransform;
            if (keycap != null)
            {
                var leK = keycap.GetComponent<UnityEngine.UI.LayoutElement>() ?? keycap.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                leK.preferredWidth = 48f;
                leK.preferredHeight = 48f;
                leK.minWidth = 48f;
                leK.minHeight = 48f;
            }
            if (label != null)
            {
                var leL = label.GetComponent<UnityEngine.UI.LayoutElement>() ?? label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                leL.preferredWidth = 130f;
                leL.preferredHeight = 48f;
                var tmp = label.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.alignment = TextAlignmentOptions.MidlineLeft;
                    tmp.fontSize = 24f;
                    tmp.color = Color.white;
                }
            }

            if (keycap != null && label != null)
            {
                keycap.anchoredPosition = new Vector2(keycap.anchoredPosition.x, 0f);
                label.anchoredPosition = new Vector2(label.anchoredPosition.x, 0f);
            }
            if (kcpRt != null)
            {
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(kcpRt);
            }

            kcp.OnPromptClicked -= HandleCardAdvance;
            kcp.OnPromptClicked += HandleCardAdvance;

            var rawHint = card.transform.Find("HintText");
            if (rawHint != null) rawHint.gameObject.SetActive(false);
        }
    }

    public void ShowSingleCard(int index)
    {
        ShowTutorial(index, true, activeFlowToken);
    }

    private IEnumerator TransitionToNextCard()
    {
        isTransitioning = true;

        GameObject outgoing = (tutorialCards != null && currentCardIndex >= 0 && currentCardIndex < tutorialCards.Length)
            ? tutorialCards[currentCardIndex]
            : null;

        // 1. Fade out & scale down current card sequentially (no overlap)
        if (outgoing != null && outgoing.activeSelf)
        {
            yield return StartCoroutine(AnimateCardOutRoutine(outgoing, cardCloseDuration));
            outgoing.SetActive(false);
        }

        if (cardSwitchDelay > 0.001f)
        {
            yield return new WaitForSecondsRealtime(cardSwitchDelay);
        }

        currentCardIndex++; 

        if (currentCardIndex < tutorialCards.Length)
        {
            UpdateCardDisplay();
            GameObject incoming = tutorialCards[currentCardIndex];
            if (incoming != null)
            {
                yield return StartCoroutine(AnimateCardInRoutine(incoming, cardOpenDuration));
            }
            isTransitioning = false;
        }
        else
        {
            HideTutorial();
        }
    }

    private IEnumerator AnimateCardInRoutine(GameObject card, float duration)
    {
        if (card == null) yield break;
        var cg = card.GetComponent<CanvasGroup>();
        if (cg == null) cg = card.AddComponent<CanvasGroup>();
        var rt = card.GetComponent<RectTransform>();

        cg.alpha = 0f;
        cg.interactable = false;
        cg.blocksRaycasts = false;
        if (rt != null) rt.localScale = new Vector3(cardStartScale, cardStartScale, 1f);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 3f); // Ease-out

            cg.alpha = Mathf.Lerp(0f, 1f, ease);
            if (rt != null)
            {
                float s = Mathf.Lerp(cardStartScale, 1f, ease);
                rt.localScale = new Vector3(s, s, 1f);
            }
            yield return null;
        }

        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
        if (rt != null) rt.localScale = Vector3.one;
    }

    private IEnumerator AnimateCardOutRoutine(GameObject card, float duration)
    {
        if (card == null) yield break;
        var cg = card.GetComponent<CanvasGroup>();
        if (cg == null) cg = card.AddComponent<CanvasGroup>();
        var rt = card.GetComponent<RectTransform>();

        float startAlpha = cg.alpha;
        float startScale = rt != null ? rt.localScale.x : 1f;
        cg.interactable = false;
        cg.blocksRaycasts = false;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = t * t; // Ease-in

            cg.alpha = Mathf.Lerp(startAlpha, 0f, ease);
            if (rt != null)
            {
                float s = Mathf.Lerp(startScale, cardStartScale, ease);
                rt.localScale = new Vector3(s, s, 1f);
            }
            yield return null;
        }

        cg.alpha = 0f;
        if (rt != null) rt.localScale = Vector3.one;
    }

    private void UpdateCardDisplay()
    {
        int activeCount = 0;
        for (int i = 0; i < tutorialCards.Length; i++)
        {
            if (tutorialCards[i] != null)
            {
                bool active = (i == currentCardIndex);
                tutorialCards[i].SetActive(active);
                if (active)
                {
                    activeCount++;
                    RestyleCard(tutorialCards[i]);
                }
            }
        }
        Debug.Assert(activeCount <= 1, "[TutorialManager] Assert failed: More than one tutorial card active at once!");

        cardOpenedTime = Time.unscaledTime;

        bool isLast = (singleCardMode || (tutorialCards != null && currentCardIndex >= tutorialCards.Length - 1));
        if (isLast)
        {
            OnLastCardReached?.Invoke();
            OnLastCardReachedEvent?.Invoke();
        }
    }

    public void HideTutorial()
    {
        if (!isTutorialActive && (tutorialOverlay == null || !tutorialOverlay.activeSelf)) return;
        isTutorialActive = false;
        isTransitioning = false;
        if (cardAnimRoutine != null)
        {
            StopCoroutine(cardAnimRoutine);
            cardAnimRoutine = null;
        }

        GameObject modalTarget = tutorialOverlay != null ? tutorialOverlay : gameObject;
        if (ModalManager.Instance != null)
        {
            ModalManager.Instance.CloseModal(modalTarget);
        }

        if (tutorialOverlay != null) tutorialOverlay.SetActive(false);
        
        if (tutorialCards != null)
        {
            foreach (GameObject card in tutorialCards)
            {
                if (card != null) card.SetActive(false);
            }
        }

        Time.timeScale = 1f;

        OnTutorialClosed?.Invoke();
        OnTutorialClosedEvent?.Invoke();
    }
}
