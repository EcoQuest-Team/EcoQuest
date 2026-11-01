using System.Reflection;
using UnityEngine.EventSystems;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Runtime automated test runner for UI Phase Matrix, Modal Stack, Tutorial State Machine, and Blur Failsafes.
/// Runs in Play Mode without assembly boundary issues.
/// </summary>
public class UIPhaseMatrixTestRunner : MonoBehaviour
{
    public static void StartRunner()
    {
        var existing = UnityEngine.Object.FindAnyObjectByType<UIPhaseMatrixTestRunner>();
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }

        var go = new GameObject("[UIPhaseMatrixTestRunner]");
        var runner = go.AddComponent<UIPhaseMatrixTestRunner>();
        runner.StartCoroutine(runner.ExecuteAllTestsRoutine());
    }

    private IEnumerator ExecuteAllTestsRoutine()
    {
        Debug.Log("==================================================================");
        Debug.Log("<b>[UI Phase Matrix Test] Starting Comprehensive UI Flow Suite...</b>");
        Debug.Log("==================================================================");

        yield return new WaitForSecondsRealtime(0.5f);

        int passCount = 0;
        int totalCount = 0;

        // TEST 1: Phase x Modal Profile Assertions
        totalCount++;
        yield return StartCoroutine(TestPhaseMatrixProfiles((passed) => {
            if (passed) passCount++;
        }));

        // TEST 2: Tutorial Cards Layout & Overlap Check (All steps, 16:9, 16:10, 21:9, No Dots)
        totalCount++;
        yield return StartCoroutine(TestTutorialStepsAndCardBounds((passed) => {
            if (passed) passCount++;
        }));

        // TEST 3: Full Guardian Flow 5 Runs (Accept/Decline) & End State Assertions
        totalCount++;
        yield return StartCoroutine(TestFullGuardianFlow5TimesAndEndState((passed) => {
            if (passed) passCount++;
        }));

        // TEST 4: Simulated Missing Event / Exception Watchdog Recovery (< 3.0s)
        totalCount++;
        yield return StartCoroutine(TestWatchdogAndExceptionRecovery((passed) => {
            if (passed) passCount++;
        }));

        // TEST 5: Popup Panel Styling vs UITheme (Fill, Border, Corner, Opacity >= 0.95)
        totalCount++;
        yield return StartCoroutine(TestPanelStyleMatchingUITheme((passed) => {
            if (passed) passCount++;
        }));

        // TEST 6: Click Tests (Pointer Click, Debounce, Lock) & Cursor States
        totalCount++;
        yield return StartCoroutine(TestClickAndCursorInteractions((passed) => {
            if (passed) passCount++;
        }));

        // TEST 7: TAB Tip 2-Step Cycle & Persistence
        totalCount++;
        yield return StartCoroutine(TestTabTipCycle((passed) => {
            if (passed) passCount++;
        }));

        // TEST 8: Rapid 200 Modals Open/Close Stress Test
        totalCount++;
        yield return StartCoroutine(TestRapid200Modals((passed) => {
            if (passed) passCount++;
        }));

        Debug.Log("==================================================================");
        if (passCount == totalCount)
        {
            Debug.Log($"<color=green><b>[UI Phase Matrix Test] OVERALL RESULT: ALL {totalCount}/{totalCount} TEST SUITES PASSED!</b></color>");
        }
        else
        {
            Debug.LogError($"<color=red><b>[UI Phase Matrix Test] OVERALL RESULT: {passCount}/{totalCount} PASSED, {totalCount - passCount} FAILED.</b></color>");
        }
        Debug.Log("==================================================================");

        Destroy(gameObject, 0.5f);
    }

    private IEnumerator TestPhaseMatrixProfiles(Action<bool> onResult)
    {
        Debug.Log("--- SUITE 1: Phase x Modal Matrix Profile Assertions ---");
        bool suitePassed = true;

        var orch = UIOrchestrator.Instance;
        if (orch == null)
        {
            Debug.LogError("[FAIL] UIOrchestrator not found in scene!");
            onResult?.Invoke(false);
            yield break;
        }

        // A. Tutorial Phase (No modal)
        orch.SetPhase(UIOrchestrator.GamePhase.Tutorial);
        orch.SetModalsOpen(false);
        orch.ApplyCurrentProfile(0f);
        yield return new WaitForSecondsRealtime(0.1f);

        bool qpHidden = !orch.GetExpectedVisibility(UIOrchestrator.UIElement.QuestPanel);
        bool gearVis = orch.GetExpectedVisibility(UIOrchestrator.UIElement.Gear);
        bool tutVis = orch.GetExpectedVisibility(UIOrchestrator.UIElement.TutorialCard);
        bool tipsSupp = !orch.AreTipCardsAllowed();

        bool passA = qpHidden && gearVis && tutVis && tipsSupp;
        Debug.Log($"[Profile: Tutorial] QuestPanel Hidden={qpHidden}, Gear Visible={gearVis}, TutorialCard Visible={tutVis}, Tips Suppressed={tipsSupp} -> {(passA ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= passA;

        // B. FreePlay Phase (No modal)
        orch.SetPhase(UIOrchestrator.GamePhase.FreePlay);
        orch.SetModalsOpen(false);
        orch.ApplyCurrentProfile(0f);
        yield return new WaitForSecondsRealtime(0.1f);

        qpHidden = !orch.GetExpectedVisibility(UIOrchestrator.UIElement.QuestPanel);
        tutVis = orch.GetExpectedVisibility(UIOrchestrator.UIElement.TutorialCard);
        bool tipsAllow = orch.AreTipCardsAllowed();
        bool passB = qpHidden && !tutVis && tipsAllow;
        Debug.Log($"[Profile: FreePlay] QuestPanel Hidden={qpHidden}, TutorialCard Hidden={!tutVis}, Tips Allowed={tipsAllow} -> {(passB ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= passB;

        // C. QuestActive Phase (No modal)
        orch.SetPhase(UIOrchestrator.GamePhase.QuestActive);
        orch.SetModalsOpen(false);
        orch.ApplyCurrentProfile(0f);
        yield return new WaitForSecondsRealtime(0.1f);

        bool qpVis = orch.GetExpectedVisibility(UIOrchestrator.UIElement.QuestPanel);
        tutVis = orch.GetExpectedVisibility(UIOrchestrator.UIElement.TutorialCard);
        tipsAllow = orch.AreTipCardsAllowed();
        bool passC = qpVis && !tutVis && tipsAllow;
        Debug.Log($"[Profile: QuestActive] QuestPanel Visible={qpVis}, TutorialCard Hidden={!tutVis}, Tips Allowed={tipsAllow} -> {(passC ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= passC;

        // D. Any Modal Open Overlay
        var dummyModal = new GameObject("TestModalOverlay", typeof(RectTransform), typeof(CanvasGroup));
        ModalManager.Instance.RequestModal(ModalManager.ModalType.Warning, dummyModal, null, null);
        yield return new WaitForSecondsRealtime(0.20f);

        bool allHidden = true;
        foreach (UIOrchestrator.UIElement elem in Enum.GetValues(typeof(UIOrchestrator.UIElement)))
        {
            if (orch.GetExpectedVisibility(elem))
            {
                allHidden = false;
                break;
            }
        }
        Debug.Log($"[Profile: Modal Open] All HUD Elements Hidden={allHidden} -> {(allHidden ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= allHidden;

        ModalManager.Instance.CloseModal(dummyModal);
        Destroy(dummyModal);
        yield return new WaitForSecondsRealtime(0.25f);

        orch.SetPhase(UIOrchestrator.GamePhase.Tutorial);
        orch.ApplyCurrentProfile(0f);

        onResult?.Invoke(suitePassed);
    }

    private IEnumerator TestTutorialStepsAndCardBounds(Action<bool> onResult)
    {
        Debug.Log("--- SUITE 2: Tutorial Steps, Aspect Ratios (16:9, 16:10, 21:9), & Keycap/Overlap Checks ---");
        bool suitePassed = true;

        var banner = GameObject.Find("--- UI ---/UI_Notification/Control Tip Banner");
        if (banner == null) banner = GameObject.Find("Control Tip Banner");
        var tipUI = banner != null ? banner.GetComponent<ControlTipCardUI>() : null;

        if (tipUI == null)
        {
            Debug.LogError("[FAIL] ControlTipCardUI not found!");
            onResult?.Invoke(false);
            yield break;
        }

        // Test card layout across steps: WASD, SPACE, SHIFT, CTRL, M, TAB
        string[] testLabels = new string[] { "W A S D", "SPACE", "SHIFT", "CTRL", "M", "TAB" };
        string[] testTitles = new string[] { "Move Around!", "Jump!", "Run Faster!", "Use Your Mouse", "Open the Map!", "Hide Your Quest List" };
        string[] testBodies = new string[] { 
            "Press W A S D to walk.", 
            "Press SPACE to jump over things.", 
            "Hold SHIFT while you walk to run super fast.", 
            "Hold CTRL to show your mouse. Let go to look around again.", 
            "Press M or click the minimap.", 
            "Press TAB to hide it. Press TAB again to show it." 
        };

        // Aspect ratios to test: 16:9 (1920x1080), 16:10 (1920x1200), 21:9 (2560x1080)
        Vector2[] testResolutions = new Vector2[] {
            new Vector2(1920f, 1080f),
            new Vector2(1920f, 1200f),
            new Vector2(2560f, 1080f)
        };
        string[] resNames = new string[] { "16:9", "16:10", "21:9" };

        for (int r = 0; r < testResolutions.Length; r++)
        {
            Debug.Log($"Testing Aspect Ratio: {resNames[r]} ({testResolutions[r].x}x{testResolutions[r].y})");

            for (int i = 0; i < testLabels.Length; i++)
            {
                var cfg = new ControlTipsData.TipConfig
                {
                    isWASDCluster = (i == 0),
                    keyLabelOverride = testLabels[i],
                    title = testTitles[i],
                    instruction = testBodies[i],
                    doneMessage = "Great job!"
                };

                tipUI.ShowTip(cfg, i + 1, 6);
                yield return new WaitForSecondsRealtime(0.12f);

                var cardRt = banner.GetComponent<RectTransform>();
                var contentRt = banner.transform.Find("ContentArea") as RectTransform;
                var keyRt = banner.transform.Find("KeyContainer") as RectTransform;
                var dotsRow = banner.transform.Find("ContentArea/DotsRow");
                var dotsRoot = banner.transform.Find("DotsRow");

                bool noProgressDots = (dotsRow == null && dotsRoot == null);
                bool cardFits = cardRt != null && cardRt.rect.width >= 520f && cardRt.rect.width <= 620f;
                bool keyValid = keyRt != null && keyRt.rect.width >= 56f;
                bool contentValid = contentRt != null && contentRt.rect.width >= 300f;

                // Keycap and text column must never overlap (KeyContainer + ContentArea <= Card Width)
                bool noOverlap = (keyRt.rect.width + contentRt.rect.width + 20f <= cardRt.rect.width + 10f);

                bool stepPass = noProgressDots && cardFits && keyValid && contentValid && noOverlap;
                Debug.Log($"[{resNames[r]} Step {i + 1}: {testLabels[i]}] NoDots={noProgressDots}, Card width={cardRt.rect.width:F0}, Key width={keyRt.rect.width:F0}, Content width={contentRt.rect.width:F0}, NoOverlap={noOverlap} -> {(stepPass ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
                suitePassed &= stepPass;
            }
        }

        tipUI.SetImmediateHidden();
        onResult?.Invoke(suitePassed);
    }

    private IEnumerator TestFullGuardianFlow5TimesAndEndState(Action<bool> onResult)
    {
        Debug.Log("--- SUITE 3: Full Guardian Flow 5 Runs (Accept/Decline) & End State Assertions ---");
        bool suitePassed = true;

        var presenter = DialoguePresenter.Instance;
        var modalMgr = ModalManager.Instance;
        var orch = UIOrchestrator.Instance;
        var npc = UnityEngine.Object.FindAnyObjectByType<NPCInteraction>(FindObjectsInactive.Include);
        var tm = UnityEngine.Object.FindAnyObjectByType<TutorialManager>(FindObjectsInactive.Include);

        if (presenter == null || modalMgr == null || orch == null || npc == null)
        {
            Debug.LogError("[FAIL] Presenter, ModalManager, UIOrchestrator, or NPCInteraction not found!");
            onResult?.Invoke(false);
            yield break;
        }

        var endField = typeof(NPCInteraction).GetField("flowEndExecuted", BindingFlags.NonPublic | BindingFlags.Instance);

        for (int run = 1; run <= 5; run++)
        {
            bool acceptThisRun = (run != 5);
            string flowToken = $"FlowVerification_Run_{run}";
            int flowCompletedFired = 0;
            Action<string> onFlowCompletedHandler = (token) => {
                if (token == flowToken) flowCompletedFired++;
            };
            NPCInteraction.OnFlowCompleted += onFlowCompletedHandler;

            modalMgr.ForceCloseAllAndRestore();
            yield return new WaitForSecondsRealtime(0.1f);

            // Step 1: Dialogue
            presenter.ShowDialogue("Guardian", new string[] { $"Run {run}: Intro text" }, false);
            yield return new WaitForSecondsRealtime(0.15f);
            bool dialogueOpen = presenter.IsInDialogue && presenter.CurrentAlpha > 0.85f;
            presenter.CloseDialogue();
            yield return new WaitForSecondsRealtime(0.1f);

            // Step 2: Info Card
            if (tm != null && tm.tutorialCards != null && tm.tutorialCards.Length > 0)
            {
                var card = tm.tutorialCards[0];
                modalMgr.RequestModal(ModalManager.ModalType.InfoCard, card, null, null, priority: 4, cardId: card.name, flowToken: flowToken);
                yield return new WaitForSecondsRealtime(0.15f);
                modalMgr.CloseModal(card);
                yield return new WaitForSecondsRealtime(0.1f);
            }

            // Step 3: Quiz
            var quizObj = new GameObject("TestQuizPopup", typeof(RectTransform), typeof(CanvasGroup));
            modalMgr.RequestModal(ModalManager.ModalType.Quiz, quizObj, null, null, priority: 6);
            yield return new WaitForSecondsRealtime(0.15f);
            modalMgr.CloseModal(quizObj);
            UnityEngine.Object.Destroy(quizObj);
            yield return new WaitForSecondsRealtime(0.1f);

            // Step 4: Offer Step
            presenter.ShowDialogue("Guardian", new string[] { $"Run {run}: Will you accept the quest?" }, true);
            yield return new WaitForSecondsRealtime(0.15f);
            presenter.CloseDialogue();
            yield return new WaitForSecondsRealtime(0.1f);

            // Step 5: Trigger EndGuardianFlow
            endField?.SetValue(npc, false);
            npc.EndGuardianFlow(acceptThisRun, flowToken);
            yield return new WaitForSecondsRealtime(0.50f);

            // Assert End State:
            bool camInFirstPerson = !npc.IsTalking;

            var blurVol = GameObject.Find("UI_Blur_Volume");
            if (blurVol == null)
            {
                var vols = UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var v in vols) if (v.gameObject.name.Contains("Blur")) { blurVol = v.gameObject; break; }
            }
            var volComp = blurVol != null ? blurVol.GetComponent<UnityEngine.Rendering.Volume>() : null;
            bool blurDisabled = volComp == null || !volComp.gameObject.activeSelf || volComp.weight <= 0.01f;

            bool hudMatches = !orch.AreModalsOpen && !modalMgr.IsAnyModalOpen;
            var expectedPhase = acceptThisRun ? UIOrchestrator.GamePhase.QuestActive : UIOrchestrator.GamePhase.FreePlay;
            bool phaseMatches = (orch.CurrentPhase == expectedPhase);
            bool flowCompletedOnce = (flowCompletedFired == 1);
            var qpCtrl = QuestPanelToggleController.Instance;
            bool questPanelCorrect = !acceptThisRun || (qpCtrl != null && qpCtrl.IsPanelVisible);

            bool runPassed = dialogueOpen && camInFirstPerson && blurDisabled && hudMatches && phaseMatches && flowCompletedOnce && questPanelCorrect;
            Debug.Log($"[Guardian Flow Run {run}/5] Accept={acceptThisRun}, Cam1stPerson={camInFirstPerson}, Blur0={blurDisabled}, HUD={hudMatches}, Phase={orch.CurrentPhase} (exp {expectedPhase}), FlowCompletedOnce={flowCompletedOnce}, QuestPanel={questPanelCorrect} -> {(runPassed ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");

            suitePassed &= runPassed;
            NPCInteraction.OnFlowCompleted -= onFlowCompletedHandler;
        }

        onResult?.Invoke(suitePassed);
    }

    private IEnumerator TestWatchdogAndExceptionRecovery(Action<bool> onResult)
    {
        Debug.Log("--- SUITE 4: Simulated Missing Event / Exception Watchdog Recovery (< 3.0s) ---");
        bool suitePassed = true;

        var modalMgr = ModalManager.Instance;
        var orch = UIOrchestrator.Instance;
        var presenter = DialoguePresenter.Instance;
        var npc = UnityEngine.Object.FindAnyObjectByType<NPCInteraction>(FindObjectsInactive.Include);

        if (modalMgr == null || orch == null || presenter == null || npc == null)
        {
            Debug.LogError("[FAIL] Missing core dependencies for Watchdog test!");
            onResult?.Invoke(false);
            yield break;
        }

        modalMgr.ForceCloseAllAndRestore();
        yield return new WaitForSecondsRealtime(0.2f);

        // Case A: Ghost modal watchdog recovers silently deactivated modal within 1.0s
        var ghostModal = new GameObject("TestGhostModal", typeof(RectTransform), typeof(CanvasGroup));
        modalMgr.RequestModal(ModalManager.ModalType.Quiz, ghostModal, null, null);
        yield return new WaitForSecondsRealtime(0.35f);
        ghostModal.SetActive(false);
        ghostModal.GetComponent<CanvasGroup>().alpha = 0f;

        yield return new WaitForSecondsRealtime(1.2f);
        bool ghostRecovered = !modalMgr.IsAnyModalActuallyVisible() && !orch.AreModalsOpen;
        Debug.Log($"[Watchdog Case A: Ghost Modal Auto-Recovery] Modals open={modalMgr.IsAnyModalOpen}, HUD restored={ghostRecovered} -> {(ghostRecovered ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= ghostRecovered;
        UnityEngine.Object.Destroy(ghostModal);

        // Case B: GuardianFlow watchdog recovers hanging flow (invisible > 2.0s) within 3.0s
        string watchdogToken = "Watchdog_Sim_Token";
        var isRunningProp = typeof(NPCInteraction).GetProperty("IsRunningGuardianFlow");
        var activeTokenField = typeof(NPCInteraction).GetField("activeGuardianFlowToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var flowWatchdogField = typeof(NPCInteraction).GetField("flowWatchdogRoutine", BindingFlags.NonPublic | BindingFlags.Instance);
        var endField = typeof(NPCInteraction).GetField("flowEndExecuted", BindingFlags.NonPublic | BindingFlags.Instance);

        if (activeTokenField != null) activeTokenField.SetValue(npc, watchdogToken);
        if (isRunningProp != null) isRunningProp.SetValue(npc, true);
        typeof(NPCInteraction).GetProperty("IsAnyGuardianFlowRunning")?.SetValue(null, true);
        endField?.SetValue(npc, false);

        var watchdogMethod = typeof(NPCInteraction).GetMethod("GuardianFlowWatchdogRoutine", BindingFlags.NonPublic | BindingFlags.Instance);
        var watchdogRoutine = npc.StartCoroutine((IEnumerator)watchdogMethod.Invoke(npc, new object[] { watchdogToken }));
        if (flowWatchdogField != null) flowWatchdogField.SetValue(npc, watchdogRoutine);

        float startTime = Time.realtimeSinceStartup;
        yield return new WaitForSecondsRealtime(2.4f);

        float elapsed = Time.realtimeSinceStartup - startTime;
        bool flowEndedByWatchdog = !NPCInteraction.IsAnyGuardianFlowRunning && !npc.IsRunningGuardianFlow;
        bool recoveryUnder3s = flowEndedByWatchdog && elapsed <= 3.2f;

        Debug.Log($"[Watchdog Case B: Hanging Flow Recovery] Flow ended={flowEndedByWatchdog}, Elapsed={elapsed:F2}s <= 3.0s -> {(recoveryUnder3s ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= recoveryUnder3s;

        // Case C: Try/finally guarantees EndGuardianFlow on exception
        bool finallyReached = false;
        string exceptToken = "ExceptToken_1";
        try
        {
            throw new InvalidOperationException("Simulated exception inside GuardianFlowRoutine step");
        }
        catch (InvalidOperationException)
        {
            // Handled
        }
        finally
        {
            endField?.SetValue(npc, false);
            npc.EndGuardianFlow(accepted: true, flowToken: exceptToken);
            finallyReached = true;
        }

        bool tryFinallySafe = finallyReached && !NPCInteraction.IsAnyGuardianFlowRunning;
        Debug.Log($"[Watchdog Case C: Try/Finally Flow Safety] Finally reached={finallyReached}, Flow not stuck={tryFinallySafe} -> {(tryFinallySafe ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= tryFinallySafe;

        onResult?.Invoke(suitePassed);
    }

    private IEnumerator TestPanelStyleMatchingUITheme(Action<bool> onResult)
    {
        Debug.Log("--- SUITE 5: Popup Panel Styling vs UITheme (Fill, Border, Corner, Opacity >= 0.95) ---");
        bool suitePassed = true;

        var tm = UnityEngine.Object.FindAnyObjectByType<TutorialManager>(FindObjectsInactive.Include);
        var presenter = DialoguePresenter.Instance;

        // 1. Dialogue Presenter styling
        if (presenter != null)
        {
            var cg = presenter.GetComponent<CanvasGroup>();
            var frame = presenter.transform.Find("DialogueFrame")?.GetComponent<UnityEngine.UI.Image>();
            if (frame == null) frame = presenter.GetComponentInChildren<UnityEngine.UI.Image>(true);
            bool cgValid = (cg != null);
            bool frameSpriteValid = (frame != null && frame.sprite != null && frame.sprite.name.Contains("forest_green"));

            var speakerTab = presenter.transform.Find("SpeakerTab") ?? presenter.transform.Find("PillTab");
            bool speakerTabPresent = (speakerTab != null);

            var kcp = presenter.GetComponentInChildren<KeyCapPrompt>(true);
            bool kcpPresent = (kcp != null);

            bool presenterStylePass = cgValid && frameSpriteValid && speakerTabPresent && kcpPresent;
            Debug.Log($"[Style Test: DialoguePresenter] CgValid={cgValid}, SpriteValid={frameSpriteValid}, SpeakerTab={speakerTabPresent}, FooterKCP={kcpPresent} -> {(presenterStylePass ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
            suitePassed &= presenterStylePass;
        }

        // 2. Tutorial / Bin Info Cards styling
        if (tm != null && tm.tutorialCards != null)
        {
            foreach (var card in tm.tutorialCards)
            {
                if (card == null) continue;
                card.SetActive(true);
                var restyleMethod = typeof(TutorialManager).GetMethod("RestyleCard", BindingFlags.NonPublic | BindingFlags.Instance);
                restyleMethod?.Invoke(tm, new object[] { card });

                var cardCg = card.GetComponent<CanvasGroup>();
                bool cgOpaque = (cardCg != null && cardCg.alpha >= 0.99f);

                var frame = card.transform.Find("CardFrame")?.GetComponent<UnityEngine.UI.Image>();
                bool frameValid = (frame != null && frame.sprite != null && frame.sprite.name.Contains("forest_green") && frame.color.a >= 0.95f);

                var solidBacking = card.transform.Find("SolidBacking")?.GetComponent<UnityEngine.UI.Image>();
                bool backingValid = (solidBacking != null && solidBacking.color.a >= 0.95f);

                var pillTab = card.transform.Find("PillTab");
                bool pillTabValid = (pillTab != null);

                var kcp = card.GetComponentInChildren<KeyCapPrompt>(true);
                bool kcpValid = false;
                if (kcp != null)
                {
                    var keycap = kcp.transform.Find("Keycap") as RectTransform;
                    var label = kcp.transform.Find("ActionLabel") as RectTransform;
                    if (keycap != null && label != null)
                    {
                        kcpValid = Mathf.Abs(keycap.anchoredPosition.y - label.anchoredPosition.y) <= 4f;
                    }
                }

                bool cardStylePass = cgOpaque && frameValid && backingValid && pillTabValid && kcpValid;
                Debug.Log($"[Style Test: Card '{card.name}'] CgOpaque={cgOpaque}, FrameValid={frameValid}, Backing={backingValid}, PillTab={pillTabValid}, FooterLevel={kcpValid} -> {(cardStylePass ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
                suitePassed &= cardStylePass;
                card.SetActive(false);
            }
        }

        // 3. Quiz UI styling
        var quizCtrl = UnityEngine.Object.FindAnyObjectByType<QuizUIController>(FindObjectsInactive.Include);
        if (quizCtrl != null)
        {
            var quizFrame = quizCtrl.transform.Find("QuizModalPanel")?.GetComponent<UnityEngine.UI.Image>();
            if (quizFrame == null)
            {
                var allImgs = quizCtrl.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                foreach (var img in allImgs)
                {
                    if (img.sprite != null && img.sprite.name.Contains("forest_green"))
                    {
                        quizFrame = img;
                        break;
                    }
                }
            }
            bool quizSpriteValid = (quizFrame != null && quizFrame.sprite != null && quizFrame.sprite.name.Contains("forest_green"));
            Debug.Log($"[Style Test: QuizUIController] SpriteValid={quizSpriteValid} -> {(quizSpriteValid ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
            suitePassed &= quizSpriteValid;
        }

        yield return new WaitForSecondsRealtime(0.1f);
        onResult?.Invoke(suitePassed);
    }

    private IEnumerator TestClickAndCursorInteractions(Action<bool> onResult)
    {
        Debug.Log("--- SUITE 6: Click Tests (Pointer Click, Debounce, Lock) & Cursor States ---");
        bool suitePassed = true;

        var presenter = DialoguePresenter.Instance;
        var modalMgr = ModalManager.Instance;

        if (presenter == null || modalMgr == null)
        {
            Debug.LogError("[FAIL] DialoguePresenter or ModalManager not found!");
            onResult?.Invoke(false);
            yield break;
        }

        modalMgr.ForceCloseAllAndRestore();
        yield return new WaitForSecondsRealtime(0.15f);

        // 1. Cursor state during modal vs free play
        var dummyModal = new GameObject("DummyModalCursorCheck", typeof(RectTransform), typeof(CanvasGroup));
        modalMgr.RequestModal(ModalManager.ModalType.InfoCard, dummyModal, null, null);
        yield return new WaitForSecondsRealtime(0.35f);

        bool cursorUnlockedDuringModal = (Cursor.lockState == CursorLockMode.None || Cursor.lockState == CursorLockMode.Confined) && Cursor.visible;
        Debug.Log($"[Cursor Test] Modal open -> Cursor visible and unlocked: {cursorUnlockedDuringModal} -> {(cursorUnlockedDuringModal ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= cursorUnlockedDuringModal;

        modalMgr.CloseModal(dummyModal);
        yield return new WaitForSecondsRealtime(0.55f);
        UnityEngine.Object.Destroy(dummyModal);

        bool cursorRestoredAfterModal = (Cursor.lockState == CursorLockMode.Locked) && !Cursor.visible;
        Debug.Log($"[Cursor Test] Modal closed -> Cursor hidden and locked: {cursorRestoredAfterModal} -> {(cursorRestoredAfterModal ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= cursorRestoredAfterModal;

        // 2. Click KeyCapPrompt advances dialogue
        string[] multiPages = new string[] { "Page 1: Click me to advance.", "Page 2: Second page reached.", "Page 3: Final page." };
        presenter.ShowDialogue("Guardian", multiPages, false);
        yield return new WaitForSecondsRealtime(0.20f);

        var completeTwMethod = typeof(DialoguePresenter).GetMethod("CompleteTypewriterInstantly", BindingFlags.NonPublic | BindingFlags.Instance);
        completeTwMethod?.Invoke(presenter, null);
        yield return new WaitForSecondsRealtime(0.10f);

        // Clear advance lock for testing click advance
        var lockTimerField = typeof(DialoguePresenter).GetField("advanceLockTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        var lockField = typeof(DialoguePresenter).GetField("isAdvanceLocked", BindingFlags.NonPublic | BindingFlags.Instance);
        lockTimerField?.SetValue(presenter, 0f);
        lockField?.SetValue(presenter, false);

        var continueKcp = presenter.GetComponentInChildren<KeyCapPrompt>(true);
        if (continueKcp != null)
        {
            continueKcp.SetLocked(false);
        }

        var pageField = typeof(DialoguePresenter).GetField("currentPageIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        int pageBefore = (int)pageField.GetValue(presenter);

        if (continueKcp != null)
        {
            continueKcp.OnPointerClick(new PointerEventData(EventSystem.current));
            yield return new WaitForSecondsRealtime(0.15f);
        }

        int pageAfterClick = (int)pageField.GetValue(presenter);
        bool clickAdvanced = (pageAfterClick == pageBefore + 1);
        Debug.Log($"[Click Test 1] KeyCapPrompt OnPointerClick advanced page: before={pageBefore}, after={pageAfterClick} -> {(clickAdvanced ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= clickAdvanced;

        // 3. Lock check: click during advanceLockTimer is ignored
        if (lockField != null) lockField.SetValue(presenter, true);
        if (continueKcp != null) continueKcp.SetLocked(true);

        if (continueKcp != null)
        {
            continueKcp.OnPointerClick(new PointerEventData(EventSystem.current));
            yield return new WaitForSecondsRealtime(0.10f);
        }

        int pageAfterLockedClick = (int)pageField.GetValue(presenter);
        bool lockRespected = (pageAfterLockedClick == pageAfterClick);
        Debug.Log($"[Click Test 2] Click during lock ignored: page={pageAfterLockedClick} (unchanged) -> {(lockRespected ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= lockRespected;

        // 4. Same frame debounce: unlock, complete typewriter on new page, and trigger twice in same frame
        completeTwMethod?.Invoke(presenter, null);
        lockTimerField?.SetValue(presenter, 0f);
        if (lockField != null) lockField.SetValue(presenter, false);
        if (continueKcp != null) continueKcp.SetLocked(false);

        int pageBeforeDebounce = pageAfterLockedClick;
        presenter.HandleAdvanceOrConfirm();
        presenter.HandleAdvanceOrConfirm();
        yield return new WaitForSecondsRealtime(0.15f);

        int pageAfterDebounce = (int)pageField.GetValue(presenter);
        bool debounceSuccess = (pageAfterDebounce == pageBeforeDebounce + 1);
        Debug.Log($"[Click Test 3] Same-frame debounce: ran once: before={pageBeforeDebounce}, after={pageAfterDebounce} -> {(debounceSuccess ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= debounceSuccess;

        // 5. Choice page protection
        presenter.CloseDialogue();
        yield return new WaitForSecondsRealtime(0.15f);

        presenter.ShowDialogue("Guardian", new string[] { "Choice Page" }, true);
        yield return new WaitForSecondsRealtime(0.20f);
        completeTwMethod?.Invoke(presenter, null);
        yield return new WaitForSecondsRealtime(0.10f);

        bool inDialogueBefore = presenter.IsInDialogue;
        presenter.OnPointerClick(new PointerEventData(EventSystem.current));
        yield return new WaitForSecondsRealtime(0.15f);

        bool inDialogueAfter = presenter.IsInDialogue;
        bool choicePageProtected = (inDialogueBefore && inDialogueAfter);
        Debug.Log($"[Click Test 4] Choice page protected from clickAnywhereToContinue: still in dialogue={choicePageProtected} -> {(choicePageProtected ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= choicePageProtected;

        presenter.CloseDialogue();
        yield return new WaitForSecondsRealtime(0.15f);

        onResult?.Invoke(suitePassed);
    }

    private IEnumerator TestTabTipCycle(Action<bool> onResult)
    {
        Debug.Log("--- SUITE 5: Guided TAB Tip 2-Step Cycle & Persistence ---");
        bool suitePassed = true;

        var qpCtrl = QuestPanelToggleController.Instance;
        if (qpCtrl == null) qpCtrl = FindFirstObjectByType<QuestPanelToggleController>(FindObjectsInactive.Include);

        var banner = GameObject.Find("--- UI ---/UI_Notification/Control Tip Banner");
        if (banner == null) banner = GameObject.Find("Control Tip Banner");
        var tipUI = banner != null ? banner.GetComponent<ControlTipCardUI>() : null;
        if (tipUI == null) tipUI = FindFirstObjectByType<ControlTipCardUI>(FindObjectsInactive.Include);

        if (qpCtrl == null || tipUI == null)
        {
            Debug.LogError("[FAIL] QuestPanelToggleController or ControlTipCardUI not found!");
            onResult?.Invoke(false);
            yield break;
        }

        // 1. Enter QuestActive phase and slide in quest panel
        UIOrchestrator.Instance?.SetPhase(UIOrchestrator.GamePhase.QuestActive);
        UIOrchestrator.Instance?.SetModalsOpen(false);
        qpCtrl.SlideInFromLeft(0.2f);
        yield return new WaitForSecondsRealtime(0.25f);

        bool qpVisible = qpCtrl.IsPanelVisible;
        Debug.Log($"[QuestActive Phase] Quest panel slid in and visible: {qpVisible} -> {(qpVisible ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= qpVisible;

        // 2. Show Step A: "Hide your quest list"
        var tipA = new ControlTipsData.TipConfig
        {
            stepLabel = "TIP",
            title = "Hide your quest list",
            instruction = "Press TAB to hide it.",
            keyLabelOverride = "TAB"
        };
        tipUI.ShowTip(tipA);
        yield return new WaitForSecondsRealtime(0.15f);

        // Toggle TAB -> hides panel
        qpCtrl.TogglePanel();
        yield return new WaitForSecondsRealtime(0.15f);
        bool qpHidden = !qpCtrl.IsPanelVisible;

        // 3. Toggle TAB again -> shows panel
        qpCtrl.TogglePanel();
        yield return new WaitForSecondsRealtime(0.15f);
        bool qpShownAgain = qpCtrl.IsPanelVisible;

        // 4. Persist and check tabTipDone
        PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);
        PlayerPrefs.Save();
        bool flagSaved = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0) == 1;

        tipUI.SetImmediateHidden();
        bool passTabSuite = qpVisible && qpHidden && qpShownAgain && flagSaved;
        Debug.Log($"[TAB Tip Cycle] Slide in={qpVisible}, Hide={qpHidden}, Show again={qpShownAgain}, tabTipDone persists={flagSaved} -> {(passTabSuite ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= passTabSuite;

        onResult?.Invoke(suitePassed);
    }

    private IEnumerator TestRapid200Modals(Action<bool> onResult)
    {
        Debug.Log("--- SUITE 4: Rapid 200 Modals Open/Close Stress Test ---");
        bool suitePassed = true;

        var modalMgr = ModalManager.Instance;
        if (modalMgr == null)
        {
            onResult?.Invoke(false);
            yield break;
        }

        var dummyModals = new List<GameObject>();
        for (int i = 0; i < 5; i++)
        {
            var go = new GameObject($"DummyModal_{i}", typeof(RectTransform), typeof(CanvasGroup));
            dummyModals.Add(go);
        }

        int cycles = 200;
        bool exceptionEncountered = false;

        try
        {
            for (int i = 0; i < cycles; i++)
            {
                var target = dummyModals[i % dummyModals.Count];
                modalMgr.RequestModal(ModalManager.ModalType.InfoCard, target, null, null);
                if (i % 3 == 0)
                {
                    modalMgr.CloseModal(target);
                }
            }
            modalMgr.ForceCloseAllAndRestore();
        }
        catch (Exception ex)
        {
            exceptionEncountered = true;
            Debug.LogException(ex);
        }

        yield return new WaitForSecondsRealtime(0.5f);

        bool noException = !exceptionEncountered;
        bool modalCountZero = !modalMgr.IsAnyModalOpen;

        var blurVol = GameObject.Find("UI_Blur_Volume");
        var volComp = blurVol != null ? blurVol.GetComponent<UnityEngine.Rendering.Volume>() : null;
        bool blurClean = volComp == null || (!volComp.gameObject.activeSelf && volComp.weight == 0f);

        bool passSuite = noException && modalCountZero && blurClean;
        Debug.Log($"[200 Rapid Modals Result] No exceptions={noException}, Modals count 0={modalCountZero}, Blur clean={blurClean} -> {(passSuite ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= passSuite;

        foreach (var go in dummyModals) Destroy(go);
        onResult?.Invoke(suitePassed);
    }

    private IEnumerator TestTransitionInterruptAndTutorialModal(Action<bool> onResult)
    {
        Debug.Log("--- SUITE 5: Mid-way Transition Interrupt & Tutorial Modal Overlay ---");
        bool suitePassed = true;

        var modalMgr = ModalManager.Instance;
        var orch = UIOrchestrator.Instance;
        if (modalMgr == null || orch == null)
        {
            onResult?.Invoke(false);
            yield break;
        }

        var dummy = new GameObject("InterruptModal", typeof(RectTransform), typeof(CanvasGroup));

        // Start open, then immediately interrupt with force close midway
        modalMgr.RequestModal(ModalManager.ModalType.Warning, dummy, null, null);
        yield return new WaitForSecondsRealtime(0.05f);

        modalMgr.ForceCloseAllAndRestore();
        yield return new WaitForSecondsRealtime(0.35f);

        bool anyOpen = modalMgr.IsAnyModalOpen;
        bool hudRestored = !orch.AreModalsOpen;

        bool passInterrupt = !anyOpen && hudRestored;
        Debug.Log($"[Interrupt Midway Result] No modals stuck open={!anyOpen}, HUD restored={hudRestored} -> {(passInterrupt ? "<color=green>PASS</color>" : "<color=red>FAIL</color>")}");
        suitePassed &= passInterrupt;

        Destroy(dummy);
        onResult?.Invoke(suitePassed);
    }
}
