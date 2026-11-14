using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EcoQuest.CityQuest
{
    [DisallowMultipleComponent]
    public class CityTrashDetectiveUI : MonoBehaviour
    {
        [Header("Checklist Panel (Top-Left 340x480)")]
        public CanvasGroup checklistCanvasGroup;
        public RectTransform checklistRect;
        public TextMeshProUGUI headerProgressText;
        public Image headerProgressBarFill;
        public Image timerIcon;
        public TextMeshProUGUI timerText;
        public TextMeshProUGUI timerPausedText;
        public TextMeshProUGUI footerHintText;
        public Image fKeyCooldownFill;
        public ChecklistRowWidgets[] rowWidgets = new ChecklistRowWidgets[10];

        [Header("Trash ID Card Toast (Bottom-Right 360x110)")]
        public CanvasGroup idCardCanvasGroup;
        public RectTransform idCardRect;
        public Image idCardIcon;
        public TextMeshProUGUI idCardTitleText;
        public Image idCardCategoryBadge;
        public Image idCardCategoryIcon;
        public TextMeshProUGUI idCardCategoryText;
        public TextMeshProUGUI idCardFactText;

        [Header("Hint Compass Overlay (Top-Center)")]
        public CanvasGroup compassHudGroup;
        public RectTransform compassArrowRect;
        public Image compassArrowImage;
        public TextMeshProUGUI compassLabelText;

        [Header("Small Themed Toast (Non-blocking hints & tips)")]
        public CanvasGroup toastCanvasGroup;
        public RectTransform toastRect;
        public TextMeshProUGUI toastText;

        [Header("Results & Bonus Quiz Popup (Rebuilt on UITheme/PopupFrame)")]
        public CanvasGroup resultsCanvasGroup;
        public RectTransform resultsCardRect;
        public TextMeshProUGUI resultsTitleText;
        public Image[] resultsStarImages = new Image[3];
        public TextMeshProUGUI resultsSummaryText;
        public TextMeshProUGUI resultsBestBadgeText;
        public Image resultsBestBadgeStar;
        public TextMeshProUGUI resultsEcoTipText;
        public Button resultsTakeQuizButton;
        public TextMeshProUGUI resultsTakeQuizButtonText;
        public KeyCapPrompt resultsTakeQuizKeyCap;
        public Button resultsPlayAgainButton;
        public TextMeshProUGUI resultsPlayAgainButtonText;
        public KeyCapPrompt resultsPlayAgainKeyCap;
        public Button resultsSkipCloseButton;
        public TextMeshProUGUI resultsSkipCloseButtonText;

        [Header("Shared Sprites & Icons")]
        public Sprite starFilledSprite;
        public Sprite starOutlineSprite;
        public Sprite checkSprite;
        public Sprite clockSprite;
        public Sprite coinSprite;
        public Sprite compassArrowSprite;
        public Sprite categoryBioSprite;
        public Sprite categoryRecycleSprite;
        public Sprite categoryNonBioSprite;
        public Sprite categoryHazardSprite;

        [Serializable]
        public class ChecklistRowWidgets
        {
            public CanvasGroup rowGroup;
            public Image rowBackground;
            public Image itemIcon;
            public TextMeshProUGUI itemNameText;
            public Image categoryPill;
            public Image categoryIcon;
            public TextMeshProUGUI categoryPillText;
            public TextMeshProUGUI districtTagText;
            public Image checkBoxBg;
            public Image checkBoxIcon;
            public Coroutine checkPopRoutine;
        }

        private struct QueuedIdCard
        {
            public CityTrashItemDef item;
            public int coinsAwarded;
        }

        private readonly Queue<QueuedIdCard> idCardQueue = new Queue<QueuedIdCard>(4);
        private Coroutine idCardRoutine;
        private Coroutine toastRoutine;
        private Coroutine resultsStarRoutine;
        private Coroutine coinCountRoutine;

        private int lastDisplayedSecond = -1;
        private int lastHintCooldownSecond = -1;
        private int lastHintCharges = -1;
        private int highlightedHintSlot = -1;
        private float highlightExpireTime = 0f;
        private bool isChecklistVisible = true;
        private Coroutine checklistSlideRoutine;

        private Action onResultsQuizClicked;
        private Action onResultsRetryClicked;
        private Action onResultsCloseClicked;
        private bool resultsCanTakeBonusQuiz;
        private int lastResultsCooldownSec = -2;
        private bool lastResultsCooldownActive = false;
        private KeyCapPrompt resultsSkipCloseKeyCap;

        // Legacy / compatibility properties
        public CanvasGroup dialogueCanvasGroup;
        public RectTransform dialogueCardRect;
        public TextMeshProUGUI dialogueSpeakerText;
        public TextMeshProUGUI dialogueBodyText;
        public Button dialoguePrimaryButton;
        public TextMeshProUGUI dialoguePrimaryButtonText;
        public Button dialogueSecondaryButton;
        public TextMeshProUGUI dialogueSecondaryButtonText;
        public Button dialogueTertiaryButton;
        public TextMeshProUGUI dialogueTertiaryButtonText;
        public TextMeshProUGUI resultsStarsText;

        public bool IsAnyModalOpen =>
            (resultsCanvasGroup != null && resultsCanvasGroup.gameObject.activeSelf && resultsCanvasGroup.alpha > 0.05f) ||
            (dialogueCanvasGroup != null && dialogueCanvasGroup.gameObject.activeSelf && dialogueCanvasGroup.alpha > 0.05f);

        private void Awake()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene(gameObject.scene))
            {
                enabled = false;
                gameObject.SetActive(false);
                return;
            }
            BindResultsButtons();
            HideAllImmediate();
        }

        private void OnEnable()
        {
            if (!CityDistrictVolume.IsInAllowedCityScene(gameObject.scene))
            {
                enabled = false;
                return;
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            idCardRoutine = null;
            toastRoutine = null;
            resultsStarRoutine = null;
            coinCountRoutine = null;
            checklistSlideRoutine = null;
            idCardQueue.Clear();

            if (resultsTakeQuizKeyCap != null) resultsTakeQuizKeyCap.OnPromptClicked -= HandleQuizPromptClicked;
            if (resultsPlayAgainKeyCap != null) resultsPlayAgainKeyCap.OnPromptClicked -= HandleRetryPromptClicked;
            if (resultsSkipCloseKeyCap != null) resultsSkipCloseKeyCap.OnPromptClicked -= HandleClosePromptClicked;
        }

        private void Update()
        {
            if (resultsCanvasGroup != null && resultsCanvasGroup.gameObject.activeSelf && resultsCanvasGroup.alpha > 0.5f)
            {
                RefreshResultsRetryCooldownUI();

                if (Input.GetKeyDown(KeyCode.E))
                {
                    if (resultsTakeQuizButton != null && resultsTakeQuizButton.gameObject.activeSelf && resultsTakeQuizButton.interactable)
                    {
                        onResultsQuizClicked?.Invoke();
                    }
                    else if (resultsPlayAgainButton != null && resultsPlayAgainButton.gameObject.activeSelf && resultsPlayAgainButton.interactable)
                    {
                        onResultsRetryClicked?.Invoke();
                    }
                    else if (resultsSkipCloseButton != null && resultsSkipCloseButton.gameObject.activeSelf && resultsSkipCloseButton.interactable)
                    {
                        onResultsCloseClicked?.Invoke();
                    }
                }
                else if (Input.GetKeyDown(KeyCode.Q))
                {
                    if (resultsSkipCloseButton != null && resultsSkipCloseButton.gameObject.activeSelf && resultsSkipCloseButton.interactable)
                    {
                        onResultsCloseClicked?.Invoke();
                    }
                    else if (resultsPlayAgainButton != null && resultsPlayAgainButton.gameObject.activeSelf && resultsPlayAgainButton.interactable)
                    {
                        onResultsRetryClicked?.Invoke();
                    }
                }
            }
        }

        private void BindResultsButtons()
        {
            if (resultsTakeQuizButton != null)
            {
                resultsTakeQuizButton.onClick.RemoveAllListeners();
                resultsTakeQuizButton.onClick.AddListener(() => onResultsQuizClicked?.Invoke());
            }
            if (resultsTakeQuizKeyCap == null && resultsTakeQuizButton != null)
                resultsTakeQuizKeyCap = resultsTakeQuizButton.GetComponent<KeyCapPrompt>();
            if (resultsTakeQuizKeyCap != null)
            {
                resultsTakeQuizKeyCap.OnPromptClicked -= HandleQuizPromptClicked;
                resultsTakeQuizKeyCap.OnPromptClicked += HandleQuizPromptClicked;
            }

            if (resultsPlayAgainButton != null)
            {
                resultsPlayAgainButton.onClick.RemoveAllListeners();
                resultsPlayAgainButton.onClick.AddListener(HandleRetryPromptClicked);
            }
            if (resultsPlayAgainKeyCap == null && resultsPlayAgainButton != null)
                resultsPlayAgainKeyCap = resultsPlayAgainButton.GetComponent<KeyCapPrompt>();
            if (resultsPlayAgainKeyCap != null)
            {
                resultsPlayAgainKeyCap.OnPromptClicked -= HandleRetryPromptClicked;
                resultsPlayAgainKeyCap.OnPromptClicked += HandleRetryPromptClicked;
            }

            if (resultsSkipCloseButton != null)
            {
                resultsSkipCloseButton.onClick.RemoveAllListeners();
                resultsSkipCloseButton.onClick.AddListener(HandleClosePromptClicked);
            }
            if (resultsSkipCloseKeyCap == null && resultsSkipCloseButton != null)
                resultsSkipCloseKeyCap = resultsSkipCloseButton.GetComponent<KeyCapPrompt>();
            if (resultsSkipCloseKeyCap != null)
            {
                resultsSkipCloseKeyCap.OnPromptClicked -= HandleClosePromptClicked;
                resultsSkipCloseKeyCap.OnPromptClicked += HandleClosePromptClicked;
            }
        }

        private void HandleQuizPromptClicked()
        {
            if (resultsTakeQuizButton != null && !resultsTakeQuizButton.interactable) return;
            onResultsQuizClicked?.Invoke();
        }

        private void HandleRetryPromptClicked()
        {
            if (resultsPlayAgainButton != null && !resultsPlayAgainButton.interactable) return;
            if (CityTrashDetectiveManager.IsRobinCooldownActive(out _)) return;
            onResultsRetryClicked?.Invoke();
        }

        private void HandleClosePromptClicked()
        {
            onResultsCloseClicked?.Invoke();
        }

        public void HideAllImmediate()
        {
            SetGroupState(checklistCanvasGroup, false);
            SetGroupState(idCardCanvasGroup, false);
            SetGroupState(compassHudGroup, false);
            SetGroupState(toastCanvasGroup, false);
            SetGroupState(resultsCanvasGroup, false);
            if (dialogueCanvasGroup != null) SetGroupState(dialogueCanvasGroup, false);
        }

        private static void SetGroupState(CanvasGroup cg, bool visible)
        {
            if (cg == null) return;
            cg.alpha = visible ? 1f : 0f;
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
            cg.gameObject.SetActive(visible);
        }

        // =====================================================================
        // FIELD CHECKLIST (Top-Left 340x480, TAB slide to left)
        // =====================================================================

        // Single source of truth for the checklist placement (anchor/pivot top-left). Hidden = slid off-screen left.
        public static Vector2 ChecklistHomePos => UIOrchestrator.SharedQuestPanelHomePos;
        public static Vector2 ChecklistHiddenPos => UIOrchestrator.SharedQuestPanelHiddenPos;

        public void ShowChecklist(List<CityTrashDetectiveManager.ChecklistSlot> slots, int collectedCount, float remainingSeconds, int hintCharges, float hintCooldown)
        {
            if (checklistCanvasGroup == null) return;
            checklistCanvasGroup.gameObject.SetActive(true);
            checklistCanvasGroup.alpha = 1f;
            checklistCanvasGroup.interactable = true;
            checklistCanvasGroup.blocksRaycasts = false;
            isChecklistVisible = true;

            if (checklistRect != null)
            {
                checklistRect.anchoredPosition = ChecklistHomePos;
            }

            lastDisplayedSecond = -1;
            lastHintCooldownSecond = -1;
            lastHintCharges = -1;
            highlightedHintSlot = -1;

            RefreshChecklistRows(slots, collectedCount);
            TickTimerAndHintFooter(remainingSeconds, hintCharges, hintCooldown, Time.unscaledTime);
        }

        public void HideChecklist()
        {
            SetGroupState(checklistCanvasGroup, false);
            isChecklistVisible = false;
        }

        public void ToggleChecklist()
        {
            if (checklistCanvasGroup == null || checklistRect == null) return;

            isChecklistVisible = !isChecklistVisible;
            if (checklistSlideRoutine != null) StopCoroutine(checklistSlideRoutine);
            checklistSlideRoutine = StartCoroutine(AnimateChecklistSlide(isChecklistVisible, 0.30f));
        }

        private IEnumerator AnimateChecklistSlide(bool visible, float duration)
        {
            float startAlpha = checklistCanvasGroup.alpha;
            float endAlpha = visible ? 1f : 0f;
            Vector2 startPos = checklistRect.anchoredPosition;
            Vector2 endPos = visible ? ChecklistHomePos : ChecklistHiddenPos;

            if (!checklistCanvasGroup.gameObject.activeSelf)
                checklistCanvasGroup.gameObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                checklistCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, ease);
                checklistRect.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);
                yield return null;
            }

            checklistCanvasGroup.alpha = endAlpha;
            checklistRect.anchoredPosition = endPos;
            checklistCanvasGroup.interactable = visible;
            checklistCanvasGroup.blocksRaycasts = false;
            if (!visible) checklistCanvasGroup.gameObject.SetActive(false);
            checklistSlideRoutine = null;
        }

        public void RefreshChecklistRows(List<CityTrashDetectiveManager.ChecklistSlot> slots, int collectedCount)
        {
            if (headerProgressText != null)
            {
                headerProgressText.text = $"FIELD CHECKLIST ({collectedCount}/10)";
            }
            if (headerProgressBarFill != null)
            {
                headerProgressBarFill.fillAmount = Mathf.Clamp01(collectedCount / 10f);
            }

            if (rowWidgets == null || slots == null) return;

            int count = Mathf.Min(rowWidgets.Length, slots.Count);
            for (int i = 0; i < count; i++)
            {
                var w = rowWidgets[i];
                var s = slots[i];
                if (w == null || s.item == null) continue;

                if (w.itemIcon != null)
                {
                    w.itemIcon.sprite = s.item.icon;
                    w.itemIcon.enabled = s.item.icon != null;
                }

                if (w.itemNameText != null)
                {
                    w.itemNameText.text = s.collected ? $"<s>{s.item.displayName}</s>" : s.item.displayName;
                    w.itemNameText.color = s.collected
                        ? new Color(0.68f, 0.85f, 0.72f, 0.70f)
                        : new Color(0.96f, 0.95f, 0.92f, 1f);
                }

                Color catColor = CityTrashDatabase.GetCategoryColor(s.item.category);
                if (w.categoryPill != null)
                {
                    w.categoryPill.color = catColor;
                }

                if (w.categoryIcon != null)
                {
                    w.categoryIcon.sprite = GetCategoryIcon(s.item.category);
                    w.categoryIcon.enabled = w.categoryIcon.sprite != null;
                }

                if (w.categoryPillText != null)
                {
                    w.categoryPillText.text = GetShortCategoryLabel(s.item.category);
                }

                if (w.districtTagText != null)
                {
                    w.districtTagText.text = CityTrashDatabase.GetDistrictDisplayName(s.district);
                }

                if (w.checkBoxBg != null)
                {
                    w.checkBoxBg.color = s.collected
                        ? new Color(0.22f, 0.78f, 0.35f, 1f)
                        : new Color(0.08f, 0.18f, 0.12f, 0.9f);
                }

                if (w.checkBoxIcon != null)
                {
                    if (checkSprite != null && w.checkBoxIcon.sprite != checkSprite)
                        w.checkBoxIcon.sprite = checkSprite;

                    bool wasActive = w.checkBoxIcon.enabled;
                    w.checkBoxIcon.enabled = s.collected;

                    if (s.collected && !wasActive && gameObject.activeInHierarchy)
                    {
                        if (w.checkPopRoutine != null) StopCoroutine(w.checkPopRoutine);
                        w.checkPopRoutine = StartCoroutine(AnimateCheckPop(w.checkBoxIcon.transform));
                    }
                }

                if (w.rowGroup != null)
                {
                    w.rowGroup.alpha = s.collected ? 0.50f : 1.0f;
                }

                if (w.rowBackground != null)
                {
                    bool isHinted = (i == highlightedHintSlot && Time.unscaledTime < highlightExpireTime && !s.collected);
                    w.rowBackground.color = isHinted
                        ? new Color(0.24f, 0.50f, 0.28f, 0.95f)
                        : (s.collected ? new Color(0.06f, 0.14f, 0.10f, 0.55f) : new Color(0.08f, 0.19f, 0.13f, 0.90f));
                }
            }
        }

        private IEnumerator AnimateCheckPop(Transform checkTransform)
        {
            if (checkTransform == null) yield break;
            float dur = 0.25f;
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / dur);
                float scale = 0f;
                if (t < 0.6f) scale = Mathf.Lerp(0f, 1.25f, t / 0.6f);
                else scale = Mathf.Lerp(1.25f, 1.0f, (t - 0.6f) / 0.4f);

                checkTransform.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
            checkTransform.localScale = Vector3.one;
        }

        public void HighlightHintRow(int slotIndex, float duration, List<CityTrashDetectiveManager.ChecklistSlot> slots, int collectedCount)
        {
            highlightedHintSlot = slotIndex;
            highlightExpireTime = Time.unscaledTime + duration;
            RefreshChecklistRows(slots, collectedCount);
        }

        public void TickTimerAndHintFooter(float remainingSeconds, int hintCharges, float hintCooldown, float unscaledTime, bool isPaused = false)
        {
            int sec = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            if (sec != lastDisplayedSecond && timerText != null)
            {
                lastDisplayedSecond = sec;
                int mins = sec / 60;
                int rem = sec % 60;
                timerText.text = $"{mins:00}:{rem:00}";
                timerText.color = (sec <= 30 && sec > 0)
                    ? new Color(1f, 0.34f, 0.34f, 1f)
                    : new Color(1f, 0.90f, 0.42f, 1f);
            }

            if (timerPausedText != null)
            {
                timerPausedText.gameObject.SetActive(isPaused);
            }

            if (timerText != null)
            {
                if (sec <= 30 && sec > 0 && !isPaused)
                {
                    float pulse = 1f + Mathf.Sin(unscaledTime * 8f) * 0.08f;
                    timerText.transform.localScale = new Vector3(pulse, pulse, 1f);
                }
                else
                {
                    timerText.transform.localScale = Vector3.one;
                }
            }

            int cdSec = Mathf.Max(0, Mathf.CeilToInt(hintCooldown));
            if (cdSec != lastHintCooldownSecond || hintCharges != lastHintCharges)
            {
                lastHintCooldownSecond = cdSec;
                lastHintCharges = hintCharges;
                if (footerHintText != null)
                {
                    if (hintCharges <= 0)
                    {
                        footerHintText.text = "<b>[TAB]</b> Hide/Show   <b>[F]</b> Hints Used (0/4)";
                    }
                    else if (cdSec > 0)
                    {
                        footerHintText.text = $"<b>[TAB]</b> Hide/Show   <b>[F]</b> Cooldown ({cdSec}s) [{hintCharges}/4]";
                    }
                    else
                    {
                        footerHintText.text = $"<b>[TAB]</b> Hide/Show   <b>[F]</b> Hint ({hintCharges}/4)";
                    }
                }

                if (fKeyCooldownFill != null)
                {
                    fKeyCooldownFill.fillAmount = cdSec > 0 ? (hintCooldown / 25f) : 0f;
                }
            }
        }

        public Sprite GetCategoryIcon(LitterCategory cat)
        {
            switch (cat)
            {
                case LitterCategory.Biodegradable: return categoryBioSprite;
                case LitterCategory.Recyclable: return categoryRecycleSprite;
                case LitterCategory.NonBiodegradable: return categoryNonBioSprite;
                case LitterCategory.Hazardous: return categoryHazardSprite;
                default: return null;
            }
        }

        public static string GetShortCategoryLabel(LitterCategory cat)
        {
            switch (cat)
            {
                case LitterCategory.Biodegradable: return "BIODEGRADABLE";
                case LitterCategory.Recyclable: return "RECYCLABLE";
                case LitterCategory.NonBiodegradable: return "NON-BIODEGRADABLE";
                case LitterCategory.Hazardous: return "HAZARDOUS";
                default: return "TRASH";
            }
        }

        // =====================================================================
        // TRASH ID CARD TOAST (360x110 bottom-right, 2-deep queue)
        // =====================================================================

        public void EnqueueTrashIdCard(CityTrashItemDef item, int coinsAwarded)
        {
            if (item == null || idCardCanvasGroup == null) return;

            while (idCardQueue.Count >= 2)
            {
                idCardQueue.Dequeue();
            }

            idCardQueue.Enqueue(new QueuedIdCard { item = item, coinsAwarded = coinsAwarded });

            if (idCardRoutine == null && gameObject.activeInHierarchy)
            {
                idCardRoutine = StartCoroutine(ProcessIdCardQueueRoutine());
            }
        }

        private IEnumerator ProcessIdCardQueueRoutine()
        {
            while (idCardQueue.Count > 0)
            {
                QueuedIdCard card = idCardQueue.Dequeue();
                PopulateIdCard(card.item, card.coinsAwarded);

                idCardCanvasGroup.gameObject.SetActive(true);
                idCardCanvasGroup.interactable = false;
                idCardCanvasGroup.blocksRaycasts = false;

                Vector2 shownPos = new Vector2(-24f, 140f);
                Vector2 hiddenPos = new Vector2(390f, 140f);

                // Slide in (0.22s)
                float t = 0f;
                while (t < 0.22f)
                {
                    t += Time.unscaledDeltaTime;
                    float u = Mathf.Clamp01(t / 0.22f);
                    float ease = 1f - Mathf.Pow(1f - u, 3f);
                    idCardCanvasGroup.alpha = ease;
                    if (idCardRect != null) idCardRect.anchoredPosition = Vector2.Lerp(hiddenPos, shownPos, ease);
                    yield return null;
                }
                idCardCanvasGroup.alpha = 1f;
                if (idCardRect != null) idCardRect.anchoredPosition = shownPos;

                float holdDuration = 3.5f;
                float elapsed = 0f;
                while (elapsed < holdDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    if (idCardQueue.Count > 0 && elapsed >= 1.6f) break;
                    yield return null;
                }

                // Slide out (0.20s)
                t = 0f;
                while (t < 0.20f)
                {
                    t += Time.unscaledDeltaTime;
                    float u = Mathf.Clamp01(t / 0.20f);
                    idCardCanvasGroup.alpha = 1f - u;
                    if (idCardRect != null) idCardRect.anchoredPosition = Vector2.Lerp(shownPos, hiddenPos, u);
                    yield return null;
                }

                idCardCanvasGroup.alpha = 0f;
                idCardCanvasGroup.gameObject.SetActive(false);
            }

            idCardRoutine = null;
        }

        private void PopulateIdCard(CityTrashItemDef item, int coins)
        {
            if (idCardIcon != null)
            {
                idCardIcon.sprite = item.icon;
                idCardIcon.enabled = item.icon != null;
            }
            if (idCardTitleText != null)
            {
                idCardTitleText.text = $"{item.displayName}  <color=#FFD54F>+{coins} Coins</color>";
            }
            if (idCardCategoryBadge != null)
            {
                idCardCategoryBadge.color = CityTrashDatabase.GetCategoryColor(item.category);
            }
            if (idCardCategoryIcon != null)
            {
                idCardCategoryIcon.sprite = GetCategoryIcon(item.category);
                idCardCategoryIcon.enabled = idCardCategoryIcon.sprite != null;
            }
            if (idCardCategoryText != null)
            {
                idCardCategoryText.text = CityTrashDatabase.GetCategoryDisplayName(item.category).ToUpperInvariant();
            }
            if (idCardFactText != null)
            {
                idCardFactText.text = item.funFact;
            }
        }

        // =====================================================================
        // HINT COMPASS HUD OVERLAY
        // =====================================================================

        public void UpdateCompassHud(bool active, Vector3 playerPos, Vector3 cameraForward, Vector3 targetPos, string itemName, CityDistrict district, float remainingSec)
        {
            if (compassHudGroup == null) return;
            if (!active)
            {
                if (compassHudGroup.gameObject.activeSelf)
                {
                    SetGroupState(compassHudGroup, false);
                }
                return;
            }

            if (!compassHudGroup.gameObject.activeSelf)
            {
                compassHudGroup.gameObject.SetActive(true);
                compassHudGroup.alpha = 1f;
                compassHudGroup.interactable = false;
                compassHudGroup.blocksRaycasts = false;
            }

            Vector3 toTarget = targetPos - playerPos;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            Vector3 camFlat = cameraForward;
            camFlat.y = 0f;
            if (camFlat.sqrMagnitude < 0.001f) camFlat = Vector3.forward;
            camFlat.Normalize();

            Vector3 dir = dist > 0.01f ? (toTarget / dist) : camFlat;
            float signedAngle = Vector3.SignedAngle(camFlat, dir, Vector3.up);

            if (compassArrowRect != null)
            {
                compassArrowRect.localRotation = Quaternion.Euler(0f, 0f, -signedAngle);
            }

            if (compassArrowImage != null && compassArrowSprite != null && compassArrowImage.sprite != compassArrowSprite)
            {
                compassArrowImage.sprite = compassArrowSprite;
            }

            if (compassLabelText != null)
            {
                int distMeters = Mathf.RoundToInt(dist);
                compassLabelText.text = $"HINT: {itemName} ({CityTrashDatabase.GetDistrictDisplayName(district)}) - {distMeters}m";
            }
        }

        // =====================================================================
        // SMALL THEMED TOAST (Cooldowns & Non-Blocking Tips)
        // =====================================================================

        public void ShowHintToast(string message, float duration = 1.8f)
        {
            if (toastCanvasGroup == null || toastText == null) return;
            toastText.text = message;

            if (toastRoutine != null) StopCoroutine(toastRoutine);
            toastRoutine = StartCoroutine(ToastRoutine(duration));
        }

        private IEnumerator ToastRoutine(float duration)
        {
            toastCanvasGroup.gameObject.SetActive(true);
            toastCanvasGroup.alpha = 0f;

            float fadeTime = 0.20f;
            float elapsed = 0f;
            while (elapsed < fadeTime)
            {
                elapsed += Time.unscaledDeltaTime;
                toastCanvasGroup.alpha = Mathf.Clamp01(elapsed / fadeTime);
                yield return null;
            }
            toastCanvasGroup.alpha = 1f;

            yield return new WaitForSecondsRealtime(duration);

            elapsed = 0f;
            while (elapsed < fadeTime)
            {
                elapsed += Time.unscaledDeltaTime;
                toastCanvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeTime);
                yield return null;
            }
            toastCanvasGroup.alpha = 0f;
            toastCanvasGroup.gameObject.SetActive(false);
            toastRoutine = null;
        }

        // =====================================================================
        // RESULTS & BONUS QUIZ POPUP (Rebuilt with UITheme & 3 Star Icons)
        // =====================================================================

        public void ShowResultsModal(
            int collectedCount,
            float remainingSeconds,
            int starsEarned,
            int pickupCoins,
            int starBonusCoins,
            bool isNewBest,
            bool canTakeBonusQuiz,
            Action onTakeQuiz,
            Action onPlayAgain,
            Action onSkipOrClose)
        {
            if (resultsCanvasGroup == null) return;

            onResultsQuizClicked = onTakeQuiz;
            onResultsRetryClicked = onPlayAgain;
            onResultsCloseClicked = onSkipOrClose;
            resultsCanTakeBonusQuiz = canTakeBonusQuiz;
            lastResultsCooldownSec = -2;
            BindResultsButtons();

            int sec = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            int mins = sec / 60;
            int rem = sec % 60;

            if (resultsTitleText != null)
            {
                resultsTitleText.text = (collectedCount >= 10)
                    ? "CASE SOLVED! TRASH DETECTIVE"
                    : (starsEarned >= 1 ? "GREAT JOB!" : "TIME'S UP!");
            }

            if (resultsSummaryText != null)
            {
                resultsSummaryText.text =
                    $"Items Found: <b>{collectedCount} / 10</b>\n" +
                    $"Time Left: <b>{mins:00}:{rem:00}</b>\n" +
                    $"Pickup Coins: <b>+{pickupCoins}</b>   |   Star Bonus: <b>+{starBonusCoins}</b>";
            }

            if (resultsBestBadgeText != null)
            {
                int bestStars = PlayerPrefs.GetInt(EcoQuestSaveKeys.CityBestStars, 0);
                float bestTime = PlayerPrefs.GetFloat(EcoQuestSaveKeys.CityBestTime, 0f);
                int bSec = Mathf.RoundToInt(bestTime);
                resultsBestBadgeText.text = isNewBest
                    ? $"NEW PERSONAL BEST! ({bestStars} Stars • {bSec / 60:00}:{bSec % 60:00} left)"
                    : $"Personal Best: {bestStars} Stars ({bSec / 60:00}:{bSec % 60:00} left)";
            }

            if (resultsBestBadgeStar != null)
            {
                resultsBestBadgeStar.sprite = starFilledSprite;
                resultsBestBadgeStar.color = new Color(1f, 0.84f, 0.25f, 1f);
            }

            if (resultsEcoTipText != null)
            {
                resultsEcoTipText.text = "Eco Tip: Recycling 1 ton of paper saves 17 mature trees and 7,000 gallons of water!";
            }

            // Buttons
            if (resultsTakeQuizButton != null)
            {
                resultsTakeQuizButton.gameObject.SetActive(canTakeBonusQuiz);
                resultsTakeQuizButton.interactable = true;
                var quizRt = resultsTakeQuizButton.GetComponent<RectTransform>();
                if (quizRt != null)
                {
                    quizRt.anchorMin = new Vector2(0.5f, 0f);
                    quizRt.anchorMax = new Vector2(0.5f, 0f);
                    quizRt.pivot = new Vector2(0.5f, 0.5f);
                    quizRt.anchoredPosition = new Vector2(0f, 114f);
                }

                if (resultsTakeQuizKeyCap == null) resultsTakeQuizKeyCap = resultsTakeQuizButton.GetComponent<KeyCapPrompt>();
                if (resultsTakeQuizKeyCap != null)
                {
                    resultsTakeQuizKeyCap.SetLocked(false);
                    resultsTakeQuizKeyCap.SetCustomKeycapHeight(26f);
                    resultsTakeQuizKeyCap.SetPrompt("E", "Take Bonus Quiz (+70 Coins Max)");
                }
                else if (resultsTakeQuizButtonText != null)
                {
                    resultsTakeQuizButtonText.text = "<b>[E]</b> Take Bonus Quiz (+70 Coins Max)";
                }

                ConfigureResponsiveResultsButton(
                    resultsTakeQuizButton,
                    resultsTakeQuizKeyCap,
                    resultsTakeQuizButtonText,
                    resultsTakeQuizKeyCap != null,
                    minW: 320f,
                    maxW: 540f,
                    minH: 46f,
                    maxH: 96f);
            }

            RefreshResultsRetryCooldownUI(forceUpdate: true);

            SetGroupState(resultsCanvasGroup, true);

            // Pop in stars one by one (Golden Yellow)
            if (resultsStarRoutine != null) StopCoroutine(resultsStarRoutine);
            resultsStarRoutine = StartCoroutine(AnimateStarsPopIn(starsEarned));

            // Count up total coins
            if (coinCountRoutine != null) StopCoroutine(coinCountRoutine);
            coinCountRoutine = StartCoroutine(AnimateCoinsCountUp(pickupCoins + starBonusCoins));
        }

        private void RefreshResultsRetryCooldownUI(bool forceUpdate = false)
        {
            bool cooldownActive = CityTrashDetectiveManager.IsRobinCooldownActive(out float cooldownRemaining);
            int displaySec = cooldownActive ? Mathf.CeilToInt(cooldownRemaining) : 0;
            if (!forceUpdate && cooldownActive == lastResultsCooldownActive && (!cooldownActive || displaySec == lastResultsCooldownSec))
            {
                return;
            }

            lastResultsCooldownActive = cooldownActive;
            lastResultsCooldownSec = displaySec;

            if (resultsPlayAgainButton != null)
            {
                resultsPlayAgainButton.gameObject.SetActive(true);
                resultsPlayAgainButton.interactable = !cooldownActive;

                if (cooldownActive)
                {
                    ApplyResultsButtonLabel(resultsPlayAgainButton, resultsPlayAgainKeyCap, resultsPlayAgainButtonText,
                        string.Empty, $"Ready in {displaySec / 60}:{displaySec % 60:00}", showBadge: false);
                }
                else
                {
                    string retryKey = resultsCanTakeBonusQuiz ? "Q" : "E";
                    string retryLabel = resultsCanTakeBonusQuiz ? "Play Again" : "Retry";
                    ApplyResultsButtonLabel(resultsPlayAgainButton, resultsPlayAgainKeyCap, resultsPlayAgainButtonText,
                        retryKey, retryLabel, showBadge: true);
                }

                var rt = resultsPlayAgainButton.GetComponent<RectTransform>();
                bool twoUpRow = cooldownActive || resultsCanTakeBonusQuiz;
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 0f);
                    rt.anchorMax = new Vector2(0.5f, 0f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = twoUpRow ? new Vector2(-145f, 48f) : new Vector2(0f, 60f);
                }

                ConfigureResponsiveResultsButton(
                    resultsPlayAgainButton,
                    resultsPlayAgainKeyCap,
                    resultsPlayAgainButtonText,
                    !cooldownActive,
                    minW: twoUpRow ? 220f : 280f,
                    maxW: twoUpRow ? 276f : 440f,
                    minH: twoUpRow ? 44f : 46f,
                    maxH: 96f);
            }

            if (resultsSkipCloseButton != null)
            {
                bool showClose = cooldownActive || resultsCanTakeBonusQuiz;
                resultsSkipCloseButton.gameObject.SetActive(showClose);
                resultsSkipCloseButton.interactable = showClose;
                if (showClose)
                {
                    string closeKey = resultsCanTakeBonusQuiz ? "Q" : "E";
                    string closeLabel = resultsCanTakeBonusQuiz ? "Take Quiz Later" : "Close";
                    if (resultsSkipCloseKeyCap == null) resultsSkipCloseKeyCap = resultsSkipCloseButton.GetComponent<KeyCapPrompt>();
                    ApplyResultsButtonLabel(resultsSkipCloseButton, resultsSkipCloseKeyCap, resultsSkipCloseButtonText,
                        closeKey, closeLabel, showBadge: true);

                    var srt = resultsSkipCloseButton.GetComponent<RectTransform>();
                    if (srt != null)
                    {
                        srt.anchorMin = new Vector2(0.5f, 0f);
                        srt.anchorMax = new Vector2(0.5f, 0f);
                        srt.pivot = new Vector2(0.5f, 0.5f);
                        srt.anchoredPosition = new Vector2(145f, 48f);
                    }

                    ConfigureResponsiveResultsButton(
                        resultsSkipCloseButton,
                        resultsSkipCloseKeyCap,
                        resultsSkipCloseButtonText,
                        true,
                        minW: 220f,
                        maxW: 276f,
                        minH: 44f,
                        maxH: 96f);
                }
            }
        }

        private static void ConfigureResponsiveResultsButton(
            Button btn, KeyCapPrompt kcp, TextMeshProUGUI fallbackTmp, bool badgeVisible,
            float minW, float maxW, float minH, float maxH)
        {
            if (btn == null) return;
            TMP_Text tmp = kcp != null && kcp.ActionLabelText != null ? kcp.ActionLabelText : fallbackTmp;
            if (tmp == null) tmp = btn.GetComponentInChildren<TMP_Text>(true);
            if (tmp == null) return;

            var rtb = ResponsiveTextButton.AttachOrGet(btn);
            rtb.Configure(
                labelRef: tmp,
                backgroundRef: btn.GetComponent<Image>(),
                minFont: 13f,
                maxFont: 19f,
                minBtnHeight: minH,
                maxBtnHeight: maxH,
                minBtnWidth: minW,
                maxBtnWidth: maxW,
                fitWidth: true,
                allowHeightGrow: true,
                padLeft: badgeVisible ? 48f : 18f,
                padRight: 18f,
                padVertical: 10f,
                manageMargins: kcp == null);
            rtb.RefreshFit();
        }

        private static void ApplyResultsButtonLabel(Button btn, KeyCapPrompt kcp, TextMeshProUGUI fallbackTmp, string keyStr, string labelText, bool showBadge)
        {
            if (btn == null) return;
            Transform badge = btn.transform.Find("KeycapBadge");
            if (badge != null) badge.gameObject.SetActive(showBadge);

            if (kcp != null)
            {
                kcp.SetCustomKeycapHeight(26f);
                kcp.SetLocked(!btn.interactable);
                if (showBadge && !string.IsNullOrEmpty(keyStr))
                {
                    kcp.SetPrompt(keyStr, labelText);
                }
                else if (kcp.ActionLabelText != null)
                {
                    kcp.ActionLabelText.text = labelText;
                }
                if (kcp.ActionLabelText != null)
                {
                    var le = kcp.ActionLabelText.GetComponent<LayoutElement>();
                    if (le != null)
                    {
                        float pw = kcp.ActionLabelText.GetPreferredValues(labelText).x;
                        le.minWidth = pw;
                        le.preferredWidth = pw;
                    }
                    if (!btn.interactable)
                    {
                        Color lc = kcp.ActionLabelText.color;
                        lc.a = 0.82f;
                        kcp.ActionLabelText.color = lc;
                    }
                }
            }
            else if (fallbackTmp != null)
            {
                fallbackTmp.text = showBadge && !string.IsNullOrEmpty(keyStr) ? $"<b>[{keyStr}]</b> {labelText}" : labelText;
            }

            var rt = btn.transform as RectTransform;
            if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        private IEnumerator AnimateStarsPopIn(int starsEarned)
        {
            if (resultsStarImages == null || resultsStarImages.Length < 3) yield break;

            for (int i = 0; i < 3; i++)
            {
                if (resultsStarImages[i] != null)
                {
                    resultsStarImages[i].transform.localScale = Vector3.zero;
                    bool isEarned = (i < starsEarned);
                    resultsStarImages[i].sprite = (isEarned || starOutlineSprite == null) ? starFilledSprite : starOutlineSprite;
                    resultsStarImages[i].color = isEarned ? new Color(1f, 0.85f, 0.20f, 1f) : new Color(1f, 0.85f, 0.20f, 0.35f);
                }
            }

            yield return new WaitForSecondsRealtime(0.15f);

            for (int i = 0; i < 3; i++)
            {
                var img = resultsStarImages[i];
                if (img == null) continue;

                bool isEarned = (i < starsEarned);
                img.sprite = (isEarned || starOutlineSprite == null) ? starFilledSprite : starOutlineSprite;
                img.color = isEarned ? new Color(1f, 0.85f, 0.20f, 1f) : new Color(1f, 0.85f, 0.20f, 0.35f);

                float dur = 0.28f;
                float elapsed = 0f;
                while (elapsed < dur)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / dur);
                    float scale = (t < 0.6f)
                        ? Mathf.Lerp(0f, 1.30f, t / 0.6f)
                        : Mathf.Lerp(1.30f, 1.0f, (t - 0.6f) / 0.4f);

                    img.transform.localScale = new Vector3(scale, scale, 1f);
                    yield return null;
                }
                img.transform.localScale = Vector3.one;

                if (isEarned)
                {
                    if (UITheme.Instance != null && UITheme.Instance.quizCorrectSound != null)
                    {
                        UITheme.PlaySound(UITheme.Instance.quizCorrectSound, 0.85f);
                    }
                    else
                    {
                        AudioManager.Instance?.PlayQuizCorrect();
                    }
                }

                yield return new WaitForSecondsRealtime(0.20f);
            }

            resultsStarRoutine = null;
        }

        private IEnumerator AnimateCoinsCountUp(int targetTotal)
        {
            float dur = 0.60f;
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                int current = Mathf.RoundToInt(Mathf.Lerp(0, targetTotal, elapsed / dur));
                yield return null;
            }
            coinCountRoutine = null;
        }

        public void HideResultsModal()
        {
            SetGroupState(resultsCanvasGroup, false);
            if (resultsStarRoutine != null) { StopCoroutine(resultsStarRoutine); resultsStarRoutine = null; }
            if (coinCountRoutine != null) { StopCoroutine(coinCountRoutine); coinCountRoutine = null; }
        }

        // Forwarding legacy ShowDialogueModal / HideDialogueModal
        public void ShowDialogueModal(
            string speakerName,
            string body,
            string primaryLabel, Action primaryAction,
            string secondaryLabel = null, Action secondaryAction = null,
            string tertiaryLabel = null, Action tertiaryAction = null)
        {
            // Fallback for any legacy caller
            if (dialogueCanvasGroup != null)
            {
                SetGroupState(dialogueCanvasGroup, true);
            }
        }

        public void HideDialogueModal()
        {
            if (dialogueCanvasGroup != null)
            {
                SetGroupState(dialogueCanvasGroup, false);
            }
        }
    }
}
