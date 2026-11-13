using UnityEngine;

/// <summary>
/// Allows the player to toggle the Quest panel on/off with TAB.
/// Fades + slides 40px right over 0.25s via HudVisibility without glitches.
/// Coins counter stays visible. Ignored during dialogue and info cards.
/// </summary>
public class QuestPanelToggleController : MonoBehaviour
{
    private static QuestPanelToggleController _instance;
    public static QuestPanelToggleController Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = UnityEngine.Object.FindAnyObjectByType<QuestPanelToggleController>(FindObjectsInactive.Include);
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [SerializeField] private HudVisibility questHud;
    [SerializeField] private float toggleDuration = 0.25f;

    [Header("Quest Panel Layout Settings")]
    [SerializeField] private float panelWidth = 380f;
    [SerializeField] private float rowSpacing = 12f;
    [SerializeField] private float barGap = 5f;
    [SerializeField] private float barHeight = 10f;
    [SerializeField] private Color bioColor = new Color(0.184f, 0.706f, 0.388f, 1f);      // #2FB463 Green
    [SerializeField] private Color nonBioColor = new Color(0.149f, 0.149f, 0.149f, 1f);   // #262626 Dark Charcoal
    [SerializeField] private Color nonBioBorderColor = new Color(0.427f, 0.745f, 0.427f, 1f); // #6DBE6D Border so dark square is crisp & visible
    [SerializeField] private Color recycleColor = new Color(0.059f, 0.624f, 0.980f, 1f);  // #0F9FFA Blue

    private bool isVisible = true;
    private bool hasHiddenOnce = false;
    private bool hasShownAgain = false;

    private readonly UnityEngine.UI.Image[] categoryFillImages = new UnityEngine.UI.Image[3];
    private readonly float[] targetFillAmounts = new float[3];
    private static readonly string[] CategoryTags = new string[] { "Biodegradable", "NonBiodegradable", "Recyclable" };

    public bool IsPanelVisible
    {
        get
        {
            if (UIOrchestrator.Instance != null)
            {
                return UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.QuestPanel);
            }
            return isVisible;
        }
    }
    public bool HasCompletedTabCycle => hasHiddenOnce && hasShownAgain;

    public event System.Action OnTabCycleCompleted;
    public event System.Action<bool> OnPanelToggled;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        if (questHud == null) questHud = GetComponent<HudVisibility>();
        EnsureCleanRowLayout();
    }

    [ContextMenu("Rebuild Quest Panel Layout")]
    public void EnsureCleanRowLayout()
    {
        var panelRt = transform as RectTransform;
        if (panelRt == null) return;

        // 1. Style QuestPillTab with Fredoka SDF & UITheme white
        var pillTab = panelRt.Find("QuestPillTab") as RectTransform;
        if (pillTab != null)
        {
            var pillLe = pillTab.GetComponent<UnityEngine.UI.LayoutElement>() ?? pillTab.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            pillLe.ignoreLayout = true;
            pillTab.anchorMin = new Vector2(0f, 1f);
            pillTab.anchorMax = new Vector2(0f, 1f);
            pillTab.pivot = new Vector2(0f, 0.5f);
            pillTab.anchoredPosition = new Vector2(24f, 0f);
            pillTab.sizeDelta = new Vector2(110f, 32f);

            var pillLabel = pillTab.Find("Label")?.GetComponent<TMPro.TextMeshProUGUI>();
            if (pillLabel != null)
            {
                var titleFont = UITheme.Instance != null ? UITheme.Instance.GetTitleFont() : null;
                if (titleFont != null) pillLabel.font = titleFont;
                pillLabel.fontSize = 18f;
                pillLabel.fontStyle = TMPro.FontStyles.Bold;
                pillLabel.color = UITheme.Instance != null ? UITheme.Instance.textWhite : Color.white;
                pillLabel.alignment = TMPro.TextAlignmentOptions.Center;
            }
        }

        // 2. Root VerticalLayoutGroup + ContentSizeFitter on QuestPanel
        panelRt.anchorMin = new Vector2(0f, 1f);
        panelRt.anchorMax = new Vector2(0f, 1f);
        panelRt.pivot = new Vector2(0f, 1f);
        panelRt.anchoredPosition = UIOrchestrator.SharedQuestPanelHomePos;
        panelRt.localScale = Vector3.one;
        panelRt.sizeDelta = new Vector2(395f, panelRt.sizeDelta.y);

        var rootVlg = panelRt.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() ?? panelRt.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        rootVlg.padding = new RectOffset(20, 20, 26, 18);
        rootVlg.spacing = rowSpacing;
        rootVlg.childAlignment = TextAnchor.UpperLeft;
        rootVlg.childControlWidth = true;
        rootVlg.childControlHeight = true;
        rootVlg.childForceExpandWidth = true;
        rootVlg.childForceExpandHeight = false;

        var rootCsf = panelRt.GetComponent<UnityEngine.UI.ContentSizeFitter>() ?? panelRt.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        rootCsf.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        rootCsf.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        Color[] catColors = new Color[] { bioColor, nonBioColor, recycleColor };
        bool[] needsVisibleOutline = new bool[] { false, true, false };
        var bodyFont = UITheme.Instance != null ? UITheme.Instance.GetBodyFont() : null;
        Color labelColor = UITheme.Instance != null ? UITheme.Instance.textMuted : new Color(0.890f, 0.960f, 0.890f, 1f);
        Color countColor = UITheme.Instance != null ? UITheme.Instance.textWhite : Color.white;

        // 3. Build/align each row container (Row_1, Row_2, Row_3) preserving Bar{i}_Header and Bar{i} references
        for (int i = 1; i <= 3; i++)
        {
            string rowName = $"Row_{i}";
            var rowTr = panelRt.Find(rowName) as RectTransform;
            if (rowTr == null)
            {
                var rowGo = new GameObject(rowName, typeof(RectTransform));
                rowGo.transform.SetParent(panelRt, false);
                rowTr = rowGo.GetComponent<RectTransform>();
            }
            rowTr.localScale = Vector3.one;

            var rowVlg = rowTr.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() ?? rowTr.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            rowVlg.padding = new RectOffset(0, 0, 0, 0);
            rowVlg.spacing = barGap;
            rowVlg.childAlignment = TextAnchor.UpperLeft;
            rowVlg.childControlWidth = true;
            rowVlg.childControlHeight = true;
            rowVlg.childForceExpandWidth = true;
            rowVlg.childForceExpandHeight = false;

            // Find Bar{i}_Header and Bar{i} whether directly under QuestPanel or already inside Row_{i}
            var headerTr = (panelRt.Find($"Bar{i}_Header") ?? rowTr.Find($"Bar{i}_Header")) as RectTransform;
            var barTr = (panelRt.Find($"Bar{i}") ?? rowTr.Find($"Bar{i}")) as RectTransform;

            if (headerTr != null)
            {
                if (headerTr.parent != rowTr) headerTr.SetParent(rowTr, false);
                headerTr.SetSiblingIndex(0);
                headerTr.localScale = Vector3.one;

                var hHlg = headerTr.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() ?? headerTr.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                hHlg.padding = new RectOffset(0, 0, 0, 0);
                hHlg.spacing = 10f;
                hHlg.childAlignment = TextAnchor.MiddleLeft;
                hHlg.childControlWidth = true;
                hHlg.childControlHeight = true;
                hHlg.childForceExpandWidth = false;
                hHlg.childForceExpandHeight = false;

                var hLe = headerTr.GetComponent<UnityEngine.UI.LayoutElement>() ?? headerTr.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                hLe.minHeight = 24f;
                hLe.preferredHeight = 24f;
                hLe.flexibleWidth = 1f;

                // Category Color Square ("Dot")
                var dotTr = headerTr.Find("Dot") as RectTransform;
                if (dotTr != null)
                {
                    dotTr.gameObject.SetActive(true);
                    dotTr.localScale = Vector3.one;
                    dotTr.sizeDelta = new Vector2(14f, 14f);
                    var dotLe = dotTr.GetComponent<UnityEngine.UI.LayoutElement>() ?? dotTr.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    dotLe.minWidth = 14f;
                    dotLe.preferredWidth = 14f;
                    dotLe.flexibleWidth = 0f;
                    dotLe.minHeight = 14f;
                    dotLe.preferredHeight = 14f;
                    dotLe.flexibleHeight = 0f;

                    var dotImg = dotTr.GetComponent<UnityEngine.UI.Image>();
                    if (dotImg != null)
                    {
                        dotImg.enabled = true;
                        dotImg.sprite = null;
                        dotImg.type = UnityEngine.UI.Image.Type.Simple;
                        dotImg.color = catColors[i - 1];
                    }

                    // Ensure Non-Biodegradable dark square has a crisp border so it never blends into the dark-green panel
                    var dotOutline = dotTr.GetComponent<UnityEngine.UI.Outline>();
                    if (needsVisibleOutline[i - 1])
                    {
                        if (dotOutline == null) dotOutline = dotTr.gameObject.AddComponent<UnityEngine.UI.Outline>();
                        dotOutline.enabled = true;
                        dotOutline.effectColor = nonBioBorderColor;
                        dotOutline.effectDistance = new Vector2(1.5f, -1.5f);
                    }
                    else if (dotOutline != null)
                    {
                        dotOutline.enabled = false;
                    }
                }

                // Category Label
                var lblTr = headerTr.Find("Label") as RectTransform;
                if (lblTr != null)
                {
                    lblTr.gameObject.SetActive(true);
                    lblTr.localScale = Vector3.one;
                    var lblLe = lblTr.GetComponent<UnityEngine.UI.LayoutElement>() ?? lblTr.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    lblLe.minWidth = 140f;
                    lblLe.flexibleWidth = 1f;
                    lblLe.minHeight = 24f;
                    lblLe.preferredHeight = 24f;

                    var lblTmp = lblTr.GetComponent<TMPro.TextMeshProUGUI>();
                    if (lblTmp != null)
                    {
                        if (bodyFont != null) lblTmp.font = bodyFont;
                        lblTmp.fontSize = 20f;
                        lblTmp.fontStyle = TMPro.FontStyles.Bold;
                        lblTmp.color = labelColor;
                        lblTmp.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
                        lblTmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                        lblTmp.overflowMode = TMPro.TextOverflowModes.Overflow;
                    }
                }

                // Count Text (e.g., "0/5")
                var cntTr = headerTr.Find("CountText") as RectTransform;
                if (cntTr != null)
                {
                    cntTr.gameObject.SetActive(true);
                    cntTr.localScale = Vector3.one;
                    var cntLe = cntTr.GetComponent<UnityEngine.UI.LayoutElement>() ?? cntTr.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    cntLe.minWidth = 48f;
                    cntLe.preferredWidth = 54f;
                    cntLe.flexibleWidth = 0f;
                    cntLe.minHeight = 24f;
                    cntLe.preferredHeight = 24f;

                    var cntTmp = cntTr.GetComponent<TMPro.TextMeshProUGUI>();
                    if (cntTmp != null)
                    {
                        if (bodyFont != null) cntTmp.font = bodyFont;
                        cntTmp.fontSize = 20f;
                        cntTmp.fontStyle = TMPro.FontStyles.Bold;
                        cntTmp.color = countColor;
                        cntTmp.alignment = TMPro.TextAlignmentOptions.MidlineRight;
                        cntTmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                    }
                }
            }

            if (barTr != null)
            {
                if (barTr.parent != rowTr) barTr.SetParent(rowTr, false);
                barTr.SetSiblingIndex(1);
                // Reset legacy 0.56 scale so left and right edges align 100% with the row header (square -> count)
                barTr.localScale = Vector3.one;
                barTr.sizeDelta = new Vector2(0f, barHeight);

                var barLe = barTr.GetComponent<UnityEngine.UI.LayoutElement>() ?? barTr.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                barLe.minHeight = barHeight;
                barLe.preferredHeight = barHeight;
                barLe.flexibleWidth = 1f;

                var barOutline = barTr.GetComponent<UnityEngine.UI.Outline>();
                if (needsVisibleOutline[i - 1])
                {
                    if (barOutline == null) barOutline = barTr.gameObject.AddComponent<UnityEngine.UI.Outline>();
                    barOutline.enabled = true;
                    barOutline.effectColor = nonBioBorderColor;
                    barOutline.effectDistance = new Vector2(1f, -1f);
                }
                else if (barOutline != null)
                {
                    barOutline.enabled = false;
                }

                // Ensure child fill image stretches cleanly 0..1 with scale (1,1,1)
                if (barTr.childCount > 0)
                {
                    var fillTr = barTr.GetChild(0) as RectTransform;
                    if (fillTr != null)
                    {
                        fillTr.localScale = Vector3.one;
                        fillTr.anchorMin = Vector2.zero;
                        fillTr.anchorMax = Vector2.one;
                        fillTr.offsetMin = Vector2.zero;
                        fillTr.offsetMax = Vector2.zero;
                        var fillImg = fillTr.GetComponent<UnityEngine.UI.Image>();
                        if (fillImg != null)
                        {
                            fillImg.color = catColors[i - 1];
                            fillImg.preserveAspect = false;
                            fillImg.raycastTarget = false;
                            categoryFillImages[i - 1] = fillImg;
                        }
                    }
                }
            }
        }

        if (pillTab != null) pillTab.SetAsLastSibling();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panelRt);
        RefreshProgressBars(true);
    }

    private void OnEnable()
    {
        RefreshProgressBars(true);
    }

    public void RefreshProgressBars(bool immediate = false)
    {
        for (int i = 0; i < 3; i++)
        {
            string cat = CategoryTags[i];
            int req = QuestManager.GetCategoryTarget(cat);
            int cur = QuestManager.GetCategoryCurrentCount(cat);
            float target = req > 0 ? Mathf.Clamp01((float)cur / req) : 0f;
            targetFillAmounts[i] = target;

            if (categoryFillImages[i] != null && immediate)
            {
                categoryFillImages[i].fillAmount = target;
            }
        }
    }

    private void Start()
    {
        if (questHud == null) questHud = GetComponent<HudVisibility>();

        // Enforce UIOrchestrator profile: QuestPanel is hidden during Tutorial and FreePlay
        if (UIOrchestrator.Instance != null && UIOrchestrator.Instance.CurrentPhase != UIOrchestrator.GamePhase.QuestActive)
        {
            isVisible = false;
            if (questHud != null) questHud.SetVisibleImmediate(false);
            gameObject.SetActive(false);
            return;
        }

        if (questHud != null)
        {
            isVisible = questHud.IsVisible;
        }

        RefreshProgressBars(true);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            TryToggle();
        }

        // Smoothly animate progress bars toward exact target using unscaled time
        for (int i = 0; i < 3; i++)
        {
            var fill = categoryFillImages[i];
            if (fill != null)
            {
                float target = targetFillAmounts[i];
                if (Mathf.Abs(fill.fillAmount - target) > 0.001f)
                {
                    fill.fillAmount = Mathf.MoveTowards(fill.fillAmount, target, Time.unscaledDeltaTime * 3.5f);
                }
                else
                {
                    fill.fillAmount = target;
                }
            }
        }
    }

    /// <summary>
    /// Attempts to toggle the quest panel, respecting dialogue and modal restrictions.
    /// Allowed in QuestActive phase, and during TabTip overlay.
    /// </summary>
    public void TryToggle()
    {
        TogglePanel();
    }

    /// <summary>
    /// Slides the quest panel into view from the left over 0.4s with smooth fade.
    /// First thing the player sees after transitioning to QuestActive gameplay.
    /// </summary>
    public void SlideInFromLeft(float duration = 0.4f)
    {
        gameObject.SetActive(true);
        isVisible = true;
        UIOrchestrator.Instance?.SetQuestPanelUserHidden(false, "SlideInFromLeft");
        RefreshProgressBars(false);
        OnPanelToggled?.Invoke(true);
    }

    public void TogglePanel()
    {
        if (UIOrchestrator.Instance != null)
        {
            UIOrchestrator.Instance.ToggleQuestPanel("TAB");
            isVisible = UIOrchestrator.Instance.GetExpectedVisibility(UIOrchestrator.UIElement.QuestPanel);
            if (isVisible) RefreshProgressBars(false);
            OnPanelToggled?.Invoke(isVisible);

            if (!isVisible) hasHiddenOnce = true;
            else if (hasHiddenOnce)
            {
                hasShownAgain = true;
                OnTabCycleCompleted?.Invoke();
            }
            return;
        }

        if (questHud == null) questHud = GetComponent<HudVisibility>();
        if (questHud == null) return;

        isVisible = !questHud.IsVisible;
        questHud.SetVisible(isVisible, toggleDuration);
        OnPanelToggled?.Invoke(isVisible);

        if (!isVisible) hasHiddenOnce = true;
        else if (hasHiddenOnce)
        {
            hasShownAgain = true;
            OnTabCycleCompleted?.Invoke();
        }
    }

    public void SetPanelVisible(bool visible, bool immediate = false)
    {
        isVisible = visible;
        if (questHud != null)
        {
            if (immediate) questHud.SetVisibleImmediate(visible);
            else questHud.SetVisible(visible, toggleDuration);
        }
    }

    public void ResetTabCycle()
    {
        hasHiddenOnce = false;
        hasShownAgain = false;
    }
}
