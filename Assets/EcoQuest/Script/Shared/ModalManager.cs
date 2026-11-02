using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Centralized ModalManager singleton (under --- MANAGERS ---) guaranteeing:
/// 1. Exactly ONE modal popup/dialogue visible at any time.
/// 2. Non-reentrant modal stack & priority queue (prevents StackOverflowException).
/// 3. Seamless sequential transitions (closing suspends/resumes correctly).
/// 4. HUD fade-out (quest panel, minimap, coin, tips) + black backdrop & blur.
/// 5. Safe watchdogs and clean state restoration.
/// </summary>
public class ModalManager : MonoBehaviour
{
    private static ModalManager instance;
    public static ModalManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<ModalManager>(FindObjectsInactive.Include);
            }
            return instance;
        }
    }

    public enum ModalType
    {
        Dialogue = 1,
        Quiz = 2,
        InfoCard = 3,
        Warning = 4,
        Reward = 5,
        Results = 6
    }

    public class ModalRequest
    {
        public ModalType type;
        public int priority;
        public GameObject modalObject;
        public Action onShow;
        public Action onHide;
        public float requestedTime;
        public string cardId;
        public string flowToken;
    }

    private readonly HashSet<string> seenCardsThisFlow = new HashSet<string>();
    private string currentFlowToken = null;
    private static HashSet<string> persistedSeenCards;

    public static bool HasSeenInfoCard(string cardId)
    {
        EnsurePersistedCardsLoaded();
        return !string.IsNullOrEmpty(cardId) && persistedSeenCards.Contains(cardId);
    }

    public static void MarkInfoCardSeen(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return;
        EnsurePersistedCardsLoaded();
        if (persistedSeenCards.Add(cardId))
        {
            PlayerPrefs.SetString("EcoQuest_SeenInfoCards", string.Join(";", persistedSeenCards));
            PlayerPrefs.Save();
        }
    }

    public static void ClearPersistedSeenCards()
    {
        persistedSeenCards?.Clear();
        if (instance != null) instance.seenCardsThisFlow.Clear();
    }

    private static void EnsurePersistedCardsLoaded()
    {
        if (persistedSeenCards == null)
        {
            persistedSeenCards = new HashSet<string>();
            string raw = PlayerPrefs.GetString("EcoQuest_SeenInfoCards", "");
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (var item in raw.Split(';'))
                {
                    if (!string.IsNullOrEmpty(item)) persistedSeenCards.Add(item);
                }
            }
        }
    }

    [Header("Backdrop & Blur")]
    [SerializeField] private CanvasGroup backdropCanvasGroup;
    [SerializeField] private Volume blurVolume;

    [Header("HUD Canvas Groups to Fade")]
    [SerializeField] private List<CanvasGroup> hudCanvasGroups = new List<CanvasGroup>();

    [Header("Timing & Curves (Unscaled Time)")]
    [SerializeField] private float hudFadeDuration = 0.25f;
    [SerializeField] private float backdropFadeDuration = 0.25f;
    [SerializeField] private float modalCloseDuration = 0.18f;
    [SerializeField] private float modalTransitionGap = 0.05f;
    [SerializeField] private AnimationCurve blurEaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private readonly Stack<ModalRequest> modalStack = new Stack<ModalRequest>();
    private readonly List<ModalRequest> queue = new List<ModalRequest>();
    private ModalRequest activeModal = null;
    private Coroutine transitionRoutine;
    private Coroutine watchdogRoutine;
    private Coroutine blurFailsafeRoutine;
    private bool isHudFaded = false;
    private bool isClosingModal = false;
    private bool isBlurTweening = false;

    public bool IsAnyModalOpen => activeModal != null || modalStack.Count > 0 || isClosingModal;
    public ModalRequest ActiveModal => activeModal;
    public int ModalStackCount => modalStack.Count;
    public Volume BlurVolume => blurVolume;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        CleanupDuplicateRunners();
        ResolveSceneReferences();
        EnsureBackdrop();
    }

    private Coroutine ghostWatchdogRoutine;
    private float ghostInvisibleDuration = 0f;
    private bool hasLoggedGhostWarning = false;

    private void OnEnable()
    {
        EnsureWatchdogRoutines();
    }

    private void Start()
    {
        EnsureWatchdogRoutines();
    }

    public void EnsureWatchdogRoutines()
    {
        if (!isActiveAndEnabled) return;
        if (blurFailsafeRoutine == null) blurFailsafeRoutine = StartCoroutine(BlurFailsafeLoop());
        if (ghostWatchdogRoutine == null) ghostWatchdogRoutine = StartCoroutine(GhostModalWatchdogLoop());
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void OnDisable()
    {
        modalStack.Clear();
        queue.Clear();
        activeModal = null;
        isClosingModal = false;
        isBlurTweening = false;
        BlurFailsafeReset();
        if (blurFailsafeRoutine != null)
        {
            StopCoroutine(blurFailsafeRoutine);
            blurFailsafeRoutine = null;
        }
        if (ghostWatchdogRoutine != null)
        {
            StopCoroutine(ghostWatchdogRoutine);
            ghostWatchdogRoutine = null;
        }
        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
            transitionRoutine = null;
        }
    }

    /// <summary>
    /// Checks whether any modal root is genuinely visible (alpha > 0.01 and active).
    /// </summary>
    public bool IsAnyModalActuallyVisible()
    {
        if (activeModal != null && activeModal.modalObject != null)
        {
            if (activeModal.modalObject.GetComponent<QuizUIController>() != null)
            {
                if (QuizUIController.IsAnyQuizOpen) return true;
            }
            else if (activeModal.modalObject.activeInHierarchy)
            {
                var cg = activeModal.modalObject.GetComponent<CanvasGroup>();
                if (cg == null || cg.alpha > 0.01f) return true;
            }
        }

        if (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue && !DialoguePresenter.Instance.IsSuspended)
        {
            if (DialoguePresenter.Instance.gameObject.activeInHierarchy && DialoguePresenter.Instance.CurrentAlpha > 0.01f)
            {
                return true;
            }
        }

        if (QuizUIController.IsAnyQuizOpen)
        {
            return true;
        }

        return false;
    }

    private string GetStackContentsString()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Active=[{(activeModal != null ? activeModal.type.ToString() : "none")}], Stack=[");
        foreach (var m in modalStack)
        {
            sb.Append(m.type.ToString()).Append(", ");
        }
        sb.Append("]");
        return sb.ToString();
    }

    /// <summary>
    /// GhostModalWatchdog: Runs every 0.25s. If modal count > 0 but no modal root is visible (alpha > 0.01 and active)
    /// for > 0.5s, then:
    /// (a) if suspended dialogue exists, Resumes it;
    /// (b) otherwise force-closes the stack: modal count 0, blur off, HUD profile applied, input map restored, camera restored.
    /// </summary>
    private IEnumerator GhostModalWatchdogLoop()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(0.25f);

            int modalCount = (activeModal != null ? 1 : 0) + modalStack.Count;
            bool flowActive = NPCInteraction.IsAnyGuardianFlowRunning;

            if (modalCount > 0 || flowActive)
            {
                bool anyVisible = IsAnyModalActuallyVisible();
                if (!anyVisible)
                {
                    ghostInvisibleDuration += 0.25f;
                    float threshold = flowActive ? 2.0f : 0.5f;

                    if (ghostInvisibleDuration >= threshold)
                    {
                        string contents = GetStackContentsString();
                        if (!hasLoggedGhostWarning)
                        {
                            Debug.LogWarning($"[ModalManager GhostModalWatchdog] Detected ghost modal/flow state: modalCount={modalCount}, flowActive={flowActive} but no modal/popup visible for {ghostInvisibleDuration:F2}s. Stack: {contents}");
                            hasLoggedGhostWarning = true;
                        }

                        if (flowActive)
                        {
                            ghostInvisibleDuration = 0f;
                            hasLoggedGhostWarning = false;
                            var npc = FindFirstObjectByType<NPCInteraction>();
                            npc?.EndGuardianFlow(accepted: true, flowToken: "watchdog_recovered");
                            continue;
                        }

                        // (a) If suspended dialogue exists, Resume it
                        bool hasSuspendedDialogue = (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsSuspended);
                        if (!hasSuspendedDialogue)
                        {
                            foreach (var req in modalStack)
                            {
                                if (req.type == ModalType.Dialogue)
                                {
                                    hasSuspendedDialogue = true;
                                    break;
                                }
                            }
                        }

                        if (hasSuspendedDialogue)
                        {
                            ghostInvisibleDuration = 0f;
                            hasLoggedGhostWarning = false;

                            // Cleanly deactivate ghost modal
                            if (activeModal != null)
                            {
                                var ghost = activeModal;
                                activeModal = null;
                                if (ghost.modalObject != null) ghost.modalObject.SetActive(false);
                            }

                            while (modalStack.Count > 0 && modalStack.Peek().type != ModalType.Dialogue)
                            {
                                modalStack.Pop();
                            }
                            if (modalStack.Count > 0)
                            {
                                var dReq = modalStack.Pop();
                                activeModal = dReq;
                                ResumeModal(dReq);
                            }
                            else if (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsSuspended)
                            {
                                DialoguePresenter.Instance.Resume();
                            }
                        }
                        else
                        {
                            // (b) Force-close stack, modal count 0, blur off, HUD profile applied, input map restored, camera restored
                            ghostInvisibleDuration = 0f;
                            hasLoggedGhostWarning = false;
                            ForceCloseAllAndRestore();
                            UIOrchestrator.Instance?.ApplyCurrentProfile(0f);

                            var npc = FindFirstObjectByType<NPCInteraction>();
                            if (npc != null)
                            {
                                npc.DeactivateDialogueCamera();
                                npc.RestorePlayerControls();
                            }
                        }
                    }
                }
                else
                {
                    ghostInvisibleDuration = 0f;
                    hasLoggedGhostWarning = false;
                }
            }
            else
            {
                ghostInvisibleDuration = 0f;
                hasLoggedGhostWarning = false;
            }
        }
    }

    private IEnumerator BlurFailsafeLoop()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (!IsAnyModalOpen && !isBlurTweening)
            {
                if (blurVolume != null && (blurVolume.weight > 0.01f || blurVolume.gameObject.activeSelf))
                {
                    Debug.LogWarning("[ModalManager BlurService] Failsafe: Resetting stuck blur volume (modal count is 0)");
                    BlurFailsafeReset();
                }
            }
        }
    }

    public void BlurFailsafeReset()
    {
        isBlurTweening = false;
        if (blurVolume != null)
        {
            blurVolume.weight = 0f;
            blurVolume.gameObject.SetActive(false);
        }
        if (backdropCanvasGroup != null)
        {
            backdropCanvasGroup.alpha = 0f;
            backdropCanvasGroup.blocksRaycasts = false;
            backdropCanvasGroup.interactable = false;
        }
    }

    private void CleanupDuplicateRunners()
    {
        var runners = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int found = 0;
        foreach (var go in runners)
        {
            if (go.name == "[DialogueCoroutineRunner]")
            {
                found++;
                if (found > 1)
                {
                    Debug.Log($"[ModalManager] Disabling duplicate [DialogueCoroutineRunner] #{found} in scene root.");
                    go.SetActive(false);
                }
            }
        }
    }

    public void ResolveSceneReferences()
    {
        if (blurVolume == null)
        {
            var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Volume chosen = null;
            foreach (var v in volumes)
            {
                if (v.gameObject.name.Contains("Blur"))
                {
                    if (chosen == null)
                    {
                        chosen = v;
                    }
                    else
                    {
                        v.gameObject.SetActive(false);
                    }
                }
            }
            blurVolume = chosen;
        }

        hudCanvasGroups.Clear();
        var uiRoot = GameObject.Find("--- UI ---");
        string[] hudRelativePaths = new string[] {
            "UI_Quest/QuestPanel",
            "UI_Coins",
            "UI_HUD_SettingsButton",
            "UI_Notification/Control Tip Banner"
        };

        if (uiRoot != null)
        {
            foreach (var rel in hudRelativePaths)
            {
                var t = uiRoot.transform.Find(rel);
                if (t != null)
                {
                    var cg = t.GetComponent<CanvasGroup>();
                    if (cg == null) cg = t.gameObject.AddComponent<CanvasGroup>();
                    if (!hudCanvasGroups.Contains(cg)) hudCanvasGroups.Add(cg);
                }
            }
        }
    }

    private void EnsureBackdrop()
    {
        if (backdropCanvasGroup == null)
        {
            var existing = GameObject.Find("--- UI ---/ModalBackdrop");
            if (existing != null)
            {
                backdropCanvasGroup = existing.GetComponent<CanvasGroup>();
            }
            else
            {
                var uiRoot = GameObject.Find("--- UI ---");
                if (uiRoot != null)
                {
                    var bdGo = new GameObject("ModalBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(CanvasGroup));
                    bdGo.transform.SetParent(uiRoot.transform, false);
                    bdGo.transform.SetSiblingIndex(0);
                    var rt = bdGo.GetComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    var img = bdGo.GetComponent<UnityEngine.UI.Image>();
                    img.color = new Color(0f, 0f, 0f, 0.55f); // High contrast 55%
                    backdropCanvasGroup = bdGo.GetComponent<CanvasGroup>();
                    backdropCanvasGroup.alpha = 0f;
                    backdropCanvasGroup.blocksRaycasts = false;
                    backdropCanvasGroup.interactable = false;
                }
            }
        }
    }

    /// <summary>
    /// Queues or immediately shows a modal with non-reentrant state-machine.
    /// If another modal is open, suspends lower-priority modals or queues sequentially.
    /// </summary>
    public void RequestModal(ModalType type, GameObject modalObj, Action onShow, Action onHide, int priority = 5, string cardId = null, string flowToken = null)
    {
        if (modalObj == null) return;
        EnsureWatchdogRoutines();

        if (!string.IsNullOrEmpty(flowToken) && !string.IsNullOrEmpty(cardId))
        {
            if (currentFlowToken != flowToken)
            {
                currentFlowToken = flowToken;
                seenCardsThisFlow.Clear();
            }

            if (seenCardsThisFlow.Contains(cardId))
            {
                Debug.Log($"[ModalManager] Ignoring duplicate info card '{cardId}' in flow token '{flowToken}'");
                return;
            }

            seenCardsThisFlow.Add(cardId);
            MarkInfoCardSeen(cardId);
        }

        var request = new ModalRequest
        {
            type = type,
            priority = priority,
            modalObject = modalObj,
            onShow = onShow,
            onHide = onHide,
            requestedTime = Time.unscaledTime,
            cardId = cardId,
            flowToken = flowToken
        };

        if (activeModal == null)
        {
            activeModal = request;
            StartTransitionTo(request);
        }
        else
        {
            if (activeModal.modalObject == modalObj)
            {
                // Already active
                return;
            }

            // If a different modal type arrives (e.g. Dialogue active, Pre-Quiz/InfoCard requested)
            // Suspend the active modal to the stack so it resumes cleanly later
            if (priority >= activeModal.priority || activeModal.type == ModalType.Dialogue)
            {
                SuspendCurrentModal();
                activeModal = request;
                StartTransitionTo(request);
            }
            else
            {
                queue.Add(request);
                queue.Sort((a, b) => b.priority.CompareTo(a.priority));
            }
        }
    }

    private void SuspendCurrentModal()
    {
        if (activeModal == null) return;

        var suspended = activeModal;
        modalStack.Push(suspended);

        // Hide suspended modal visually without closing its logic
        if (suspended.type == ModalType.Dialogue || (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue))
        {
            DialoguePresenter.Instance?.Suspend();
        }

        if (suspended.modalObject != null)
        {
            var cg = suspended.modalObject.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 0f;
                cg.blocksRaycasts = false;
                cg.interactable = false;
            }
            if (suspended.modalObject.activeSelf)
            {
                suspended.modalObject.SetActive(false);
            }
        }
        activeModal = null;
    }

    /// <summary>
    /// Closes a modal with re-entrancy protection.
    /// Clears activeModal BEFORE invoking onHide so nested calls cannot cause StackOverflowException.
    /// </summary>
    public void CloseModal(GameObject modalObj)
    {
        if (isClosingModal) return;

        if (activeModal != null && (activeModal.modalObject == modalObj || modalObj == null))
        {
            CloseCurrentModal();
            return;
        }

        // Check if modalObj is in modalStack or queue
        if (modalObj != null)
        {
            queue.RemoveAll(r => r.modalObject == modalObj);

            if (modalStack.Count > 0)
            {
                var remaining = new Stack<ModalRequest>();
                var items = modalStack.ToArray();
                System.Array.Reverse(items);
                foreach (var req in items)
                {
                    if (req.modalObject != modalObj)
                    {
                        remaining.Push(req);
                    }
                }
                modalStack.Clear();
                var rev = remaining.ToArray();
                System.Array.Reverse(rev);
                foreach (var req in rev) modalStack.Push(req);
            }
        }
    }

    private void CloseCurrentModal()
    {
        if (activeModal == null || isClosingModal) return;

        isClosingModal = true;
        var closing = activeModal;
        activeModal = null; // Cleared immediately to break any recursion loops

        try
        {
            closing.onHide?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            if (!Application.isPlaying)
            {
                HudVisibilityService.Instance?.ShowHUD("Modal", 0f);
                isClosingModal = false;
                UIOrchestrator.Instance?.SetModalsOpen(false);
            }
            else
            {
                // Keep isClosingModal = true until CloseAndProcessNextRoutine completes so modal open/close is always balanced
                if (transitionRoutine != null) StopCoroutine(transitionRoutine);
                transitionRoutine = StartCoroutine(CloseAndProcessNextRoutine(closing));
            }
        }
    }

    private IEnumerator CloseAndProcessNextRoutine(ModalRequest closing)
    {
        try
        {
            float exitDur = UITheme.Instance != null ? UITheme.Instance.modalExitDuration : modalCloseDuration;
            yield return new WaitForSecondsRealtime(exitDur);

            if (closing != null && closing.modalObject != null)
            {
                var cg = closing.modalObject.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 0f;
                    cg.blocksRaycasts = false;
                    cg.interactable = false;
                }
                if (closing.modalObject.activeSelf)
                {
                    closing.modalObject.SetActive(false);
                }
            }

            // 1. Resume suspended modal from stack if any
            if (modalStack.Count > 0)
            {
                var resumed = modalStack.Pop();
                activeModal = resumed;
                yield return new WaitForSecondsRealtime(modalTransitionGap);
                ResumeModal(resumed);
            }
            // 2. Otherwise process next from queue
            else if (queue.Count > 0)
            {
                var next = queue[0];
                queue.RemoveAt(0);
                activeModal = next;
                yield return new WaitForSecondsRealtime(modalTransitionGap);
                StartTransitionTo(next);
            }
            // 3. Otherwise restore system state and HUD
            else
            {
                yield return StartCoroutine(FadeBackdropAndHudRoutine(false));
                RestoreSystemState();
            }
        }
        finally
        {
            isClosingModal = false;
            if (!IsAnyModalOpen)
            {
                BlurFailsafeReset();
                UIOrchestrator.Instance?.SetModalsOpen(false);
                RestoreSystemState();
            }
        }
    }

    private void ResumeModal(ModalRequest request)
    {
        if (request == null || request.modalObject == null) return;

        if (request.type == ModalType.Dialogue || (DialoguePresenter.Instance != null && DialoguePresenter.Instance.IsInDialogue))
        {
            DialoguePresenter.Instance?.Resume();
        }

        if (!request.modalObject.activeSelf)
        {
            request.modalObject.SetActive(true);
        }

        var cg = request.modalObject.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            cg.blocksRaycasts = true;
            cg.interactable = true;
        }

        try
        {
            request.onShow?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void StartTransitionTo(ModalRequest request)
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(OpenModalRoutine(request));
    }

    private IEnumerator OpenModalRoutine(ModalRequest request)
    {
        // 1. Fade HUD out & backdrop in
        yield return StartCoroutine(FadeBackdropAndHudRoutine(true));

        // Unlock and show cursor during any open modal
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 2. Activate modal object hierarchy
        if (request.modalObject != null)
        {
            if (!request.modalObject.activeSelf) request.modalObject.SetActive(true);
            Transform p = request.modalObject.transform.parent;
            while (p != null)
            {
                if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
                p = p.parent;
            }

            var cg = request.modalObject.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
                cg.interactable = true;
            }
        }

        // 3. Fire onShow safely
        try
        {
            request.onShow?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private IEnumerator FadeBackdropAndHudRoutine(bool showModal)
    {
        float duration = hudFadeDuration;
        float elapsed = 0f;
        float startBackdropAlpha = backdropCanvasGroup != null ? backdropCanvasGroup.alpha : 0f;
        float targetBackdropAlpha = showModal ? 1f : 0f;

        float startBlurWeight = blurVolume != null ? blurVolume.weight : 0f;
        float targetBlurWeight = showModal ? 0.60f : 0f;

        if (showModal && blurVolume != null) blurVolume.gameObject.SetActive(true);
        if (backdropCanvasGroup != null)
        {
            backdropCanvasGroup.blocksRaycasts = showModal;
            backdropCanvasGroup.interactable = showModal;
        }

        if (showModal)
        {
            HudVisibilityService.Instance?.HideHUD("Modal", duration);
            UIOrchestrator.Instance?.SetModalsOpen(true);
        }
        else
        {
            HudVisibilityService.Instance?.ShowHUD("Modal", duration);
        }

        isBlurTweening = true;
        try
        {
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Smooth ease-out when opening (1-(1-t)^3), smooth ease-in when closing (t^2)
                float ease = showModal ? (1f - Mathf.Pow(1f - t, 3f)) : (t * t);

                if (backdropCanvasGroup != null)
                {
                    backdropCanvasGroup.alpha = Mathf.Lerp(startBackdropAlpha, targetBackdropAlpha, ease);
                }
                if (blurVolume != null)
                {
                    blurVolume.weight = Mathf.Lerp(startBlurWeight, targetBlurWeight, ease);
                }

                yield return null;
            }
        }
        finally
        {
            isBlurTweening = false;
            // Always snap to exact target on completion or interrupt
            if (backdropCanvasGroup != null) backdropCanvasGroup.alpha = targetBackdropAlpha;
            if (blurVolume != null)
            {
                blurVolume.weight = targetBlurWeight;
                if (!showModal) blurVolume.gameObject.SetActive(false);
            }
            isHudFaded = showModal;
            UIOrchestrator.Instance?.SetModalsOpen(showModal);
        }
    }

    public void ForceCloseAllAndRestore()
    {
        modalStack.Clear();
        queue.Clear();
        isClosingModal = false;

        if (activeModal != null)
        {
            var m = activeModal;
            activeModal = null;
            try
            {
                m.onHide?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            if (m.modalObject != null) m.modalObject.SetActive(false);
        }

        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
            transitionRoutine = null;
        }

        BlurFailsafeReset();
        isHudFaded = false;
        UIOrchestrator.Instance?.SetModalsOpen(false);
        HudVisibilityService.Instance?.ShowHUD("Modal", 0f);
        RestoreSystemState();
    }

    private void RestoreSystemState()
    {
        Time.timeScale = 1f;

        // Failsafe: if a modal closes during the sequential Guardian flow (Steps 1-4),
        // restore the current step's HUD profile and keep player controls frozen / cursor unlocked for the next step.
        if (NPCInteraction.IsAnyGuardianFlowRunning)
        {
            UIOrchestrator.Instance?.ApplyCurrentProfile(0.20f);
            return;
        }

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        var inputs = UnityEngine.Object.FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
        if (inputs != null)
        {
            inputs.cursorLocked = true;
            inputs.cursorInputForLook = true;
        }
    }
}
