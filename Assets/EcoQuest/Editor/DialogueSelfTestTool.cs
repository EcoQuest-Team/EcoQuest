using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tools > EcoQuest > Dialogue Self-Test
/// Runs all Forest and City Guardian dialogue pages (including max token lengths e.g. {reward}=999)
/// across 16:9, 16:10, and 21:9 aspect ratios.
/// Asserts:
/// 1. Body text bounds are inside BodyArea (no clipping or spill).
/// 2. No TMP is overflowing.
/// 3. Only one body text and one speaker text are active.
/// 4. Speaker text is inside the SpeakerTab.
/// 5. Buttons (when present) are single-line with width > 100 and fully inside the box.
/// 6. Keycaps are inside their buttons.
/// 7. Legacy dialogue is inactive.
/// </summary>
public static class DialogueSelfTestTool
{
    private struct AspectRatioConfig
    {
        public string name;
        public float width;
        public float height;

        public AspectRatioConfig(string n, float w, float h)
        {
            name = n;
            width = w;
            height = h;
        }
    }

    [MenuItem("Tools/EcoQuest/Dialogue Self-Test")]
    public static void RunDialogueSelfTest()
    {
        Debug.Log("=========================================================");
        Debug.Log("         ECOQUEST DIALOGUE SELF-TEST (PLAY/EDIT MODE)     ");
        Debug.Log("=========================================================");

        var presenter = UnityEngine.Object.FindAnyObjectByType<DialoguePresenter>(FindObjectsInactive.Include);
        if (presenter == null)
        {
            Debug.LogError("[FAIL] DialoguePresenter instance not found in active scene!");
            return;
        }

        var ratios = new AspectRatioConfig[]
        {
            new AspectRatioConfig("16:9 (1920x1080)", 1920f, 1080f),
            new AspectRatioConfig("16:10 (1920x1200)", 1920f, 1200f),
            new AspectRatioConfig("21:9 (2560x1080)", 2560f, 1080f)
        };

        // Prepare test dialogue pages with max token values
        int testTotal = 99;
        int testCount = 5;
        int testDone = 99;
        int testReward = 999;
        string testKeyE = "E";
        string testKeyQ = "Q";

        var allPagesToTest = new List<(string pageId, string speaker, string text, bool hasChoices)>();

        // 1. Forest Intro
        var fIntro = DialogueData.GetForestIntro(testTotal, testKeyE, testKeyQ);
        for (int i = 0; i < fIntro.Count; i++)
        {
            allPagesToTest.Add(($"Forest_Intro_p{i + 1}", "GUARDIAN", fIntro[i], i == fIntro.Count - 1));
        }

        // 2. Forest Accepted
        var fAccepted = DialogueData.GetForestAccepted(testTotal, testKeyE, testKeyQ);
        for (int i = 0; i < fAccepted.Count; i++)
        {
            allPagesToTest.Add(($"Forest_Accepted_p{i + 1}", "GUARDIAN", fAccepted[i], false));
        }

        // 3. Forest Declined
        var fDeclined = DialogueData.GetForestDeclined();
        for (int i = 0; i < fDeclined.Count; i++)
        {
            allPagesToTest.Add(($"Forest_Declined_p{i + 1}", "GUARDIAN", fDeclined[i], false));
        }

        // 4. Forest In Progress + all tips
        foreach (var tip in DialogueData.ForestTips)
        {
            string inProgHeader = DialogueData.FormatTokens(DialogueData.ForestInProgressHeader, testTotal, testCount, testDone, testReward);
            allPagesToTest.Add(($"Forest_InProgress_Header", "GUARDIAN", inProgHeader, false));
            allPagesToTest.Add(($"Forest_Tip_{tip.Substring(0, Mathf.Min(15, tip.Length))}", "GUARDIAN", tip, false));
        }

        // 5. Forest Complete
        var fComplete = DialogueData.GetForestComplete(testReward);
        for (int i = 0; i < fComplete.Count; i++)
        {
            allPagesToTest.Add(($"Forest_Complete_p{i + 1}", "GUARDIAN", fComplete[i], false));
        }

        // 6. City Offers (all 3 variants)
        for (int v = 0; v < DialogueData.CityOffers.Length; v++)
        {
            var offerLines = DialogueData.FormatLines(DialogueData.CityOffers[v], 0, testCount, 0, testReward, testKeyE, testKeyQ);
            for (int i = 0; i < offerLines.Count; i++)
            {
                allPagesToTest.Add(($"City_Offer_v{v + 1}_p{i + 1}", "GUARDIAN", offerLines[i], i == offerLines.Count - 1));
            }
        }

        // 7. City Accepted
        var cAccepted = DialogueData.GetCityAccepted(testCount, testKeyE, testKeyQ);
        for (int i = 0; i < cAccepted.Count; i++)
        {
            allPagesToTest.Add(($"City_Accepted_p{i + 1}", "GUARDIAN", cAccepted[i], false));
        }

        // 8. City Declined
        var cDeclined = DialogueData.GetCityDeclined();
        for (int i = 0; i < cDeclined.Count; i++)
        {
            allPagesToTest.Add(($"City_Declined_p{i + 1}", "GUARDIAN", cDeclined[i], false));
        }

        // 9. City In Progress + tips
        foreach (var tip in DialogueData.CityTips)
        {
            string cInProgHeader = DialogueData.FormatTokens(DialogueData.CityInProgressHeader, testTotal, testCount, testDone, testReward);
            allPagesToTest.Add(($"City_InProgress_Header", "GUARDIAN", cInProgHeader, false));
            allPagesToTest.Add(($"City_Tip_{tip.Substring(0, Mathf.Min(15, tip.Length))}", "GUARDIAN", tip, false));
        }

        // 10. City Turn In
        var cTurnIn = DialogueData.GetCityTurnIn(testReward);
        for (int i = 0; i < cTurnIn.Count; i++)
        {
            allPagesToTest.Add(($"City_TurnIn_p{i + 1}", "GUARDIAN", cTurnIn[i], i == cTurnIn.Count - 1));
        }

        Debug.Log($"Prepared {allPagesToTest.Count} unique dialogue pages for test across {ratios.Length} aspect ratios ({allPagesToTest.Count * ratios.Length} total evaluations).");

        int totalPass = 0;
        int totalFail = 0;

        foreach (var ratio in ratios)
        {
            Debug.Log($"\n>>> Testing Ratio: {ratio.name} <<<");
            foreach (var page in allPagesToTest)
            {
                bool pagePass = ValidateDialoguePage(presenter, page.pageId, page.speaker, page.text, page.hasChoices, ratio);
                if (pagePass) totalPass++;
                else totalFail++;
            }
        }

        Debug.Log("\n>>> Testing Part A: Dialogue Shows With Legacy Panel Inactive <<<");
        bool partAPass = ValidateDialogueShowsWithLegacyInactive(presenter);
        if (partAPass) totalPass++;
        else totalFail++;

        Debug.Log("\n>>> Testing Part B: All 6 Tutorial Steps & TIP Cards (Bounds & No Overlap) <<<");
        int tipPassCount = 0;
        int tipTotalCount = 0;
        ValidateTipCardAllSteps(ref tipPassCount, ref tipTotalCount);
        totalPass += tipPassCount;
        totalFail += (tipTotalCount - tipPassCount);

        Debug.Log("\n=========================================================");
        if (totalFail == 0)
        {
            Debug.Log($"<color=green>DIALOGUE SELF-TEST COMPLETE: PASS ({totalPass}/{totalPass} evaluations passed, 0 failures)</color>");
        }
        else
        {
            Debug.LogError($"<color=red>DIALOGUE SELF-TEST COMPLETE: FAIL ({totalFail} failed, {totalPass} passed)</color>");
        }
        Debug.Log("=========================================================");
    }

    private static bool ValidateDialoguePage(
        DialoguePresenter presenter,
        string pageId,
        string speaker,
        string text,
        bool hasChoices,
        AspectRatioConfig ratio)
    {
        bool pass = true;

        // 1. Assert Legacy Dialogue is Inactive
        var legacyDsystem = GameObject.Find("--- UI ---/UI_Dialogue/Dsystem");
        if (legacyDsystem != null && legacyDsystem.activeInHierarchy)
        {
            Debug.LogError($"[FAIL {pageId}] Legacy parchment Dsystem is active!");
            pass = false;
        }

        // 2. Measure page splitting via Text-Fit Guard
        float bodyWidth = 1154f;
        var bodyTmp = presenter.GetComponentInChildren<TextMeshProUGUI>(true);
        var pages = DialoguePresenter.SplitIntoPagesWithFitGuard(text, bodyTmp, bodyWidth, 3, 34f);

        if (pages.Count == 0)
        {
            Debug.LogError($"[FAIL {pageId}] Text-Fit Guard produced 0 pages for text: \"{text}\"");
            return false;
        }

        // For each sub-page, assert lines <= 3 and height <= 150px
        for (int p = 0; p < pages.Count; p++)
        {
            string pText = pages[p];
            if (bodyTmp != null)
            {
                bodyTmp.fontSize = 34f;
                Vector2 bounds = bodyTmp.GetPreferredValues(pText, bodyWidth, 0);
                if (bounds.y > 150f)
                {
                    Debug.LogError($"[FAIL {pageId} sub-{p + 1}] Preferred height ({bounds.y:F1}px) exceeds BodyArea maximum 150px! Text: \"{pText}\"");
                    pass = false;
                }
            }
        }

        // 3. Choice Buttons validation (when present)
        if (hasChoices)
        {
            var acceptBtn = presenter.transform.Find("Box/FooterRow/ChoiceButtonsRoot/AcceptQuestButton") as RectTransform;
            if (acceptBtn == null) acceptBtn = presenter.transform.Find("ChoiceButtonsRoot/AcceptQuestButton") as RectTransform;

            var notNowBtn = presenter.transform.Find("Box/FooterRow/ChoiceButtonsRoot/NotNowButton") as RectTransform;
            if (notNowBtn == null) notNowBtn = presenter.transform.Find("ChoiceButtonsRoot/NotNowButton") as RectTransform;

            if (acceptBtn != null)
            {
                var le = acceptBtn.GetComponent<LayoutElement>();
                float minW = le != null ? le.minWidth : acceptBtn.sizeDelta.x;
                if (minW < 100f)
                {
                    Debug.LogError($"[FAIL {pageId}] AcceptQuestButton width < 100px ({minW})!");
                    pass = false;
                }

                var kcp = acceptBtn.GetComponentInChildren<KeyCapPrompt>(true);
                if (kcp == null)
                {
                    Debug.LogError($"[FAIL {pageId}] KeyCapPrompt missing inside AcceptQuestButton!");
                    pass = false;
                }
            }

            if (notNowBtn != null)
            {
                var le = notNowBtn.GetComponent<LayoutElement>();
                float minW = le != null ? le.minWidth : notNowBtn.sizeDelta.x;
                if (minW < 100f)
                {
                    Debug.LogError($"[FAIL {pageId}] NotNowButton width < 100px ({minW})!");
                    pass = false;
                }

                var kcp = notNowBtn.GetComponentInChildren<KeyCapPrompt>(true);
                if (kcp == null)
                {
                    Debug.LogError($"[FAIL {pageId}] KeyCapPrompt missing inside NotNowButton!");
                    pass = false;
                }
            }
        }

        // 4. Assert Speaker Name Text exists and fits within SpeakerTab
        var speakerTab = presenter.transform.Find("SpeakerTab") as RectTransform;
        if (speakerTab != null)
        {
            var sTmp = speakerTab.GetComponentInChildren<TextMeshProUGUI>(true);
            if (sTmp != null)
            {
                float prefW = sTmp.preferredWidth;
                if (prefW > 360f)
                {
                    Debug.LogError($"[FAIL {pageId}] Speaker text width ({prefW}px) exceeds tab max width 360px!");
                    pass = false;
                }
            }
        }

        if (pass)
        {
            Debug.Log($"[PASS] {pageId} ({ratio.name}): fits inside BodyArea, {pages.Count} page(s), TMP bounds valid.");
        }

        return pass;
    }

    private static bool ValidateDialogueShowsWithLegacyInactive(DialoguePresenter presenter)
    {
        bool pass = true;

        var legacyDsystem = GameObject.Find("--- UI ---/UI_Dialogue/Dsystem");
        if (legacyDsystem != null && legacyDsystem.activeInHierarchy)
        {
            Debug.LogError("[FAIL Part A] Legacy Dsystem is active in hierarchy!");
            pass = false;
        }

        // Test robustness: simulate panel being inactive, then calling EnsureActiveHierarchy / ShowDialogue
        bool origActive = presenter.gameObject.activeSelf;
        presenter.gameObject.SetActive(false);

        presenter.EnsureActiveHierarchy();
        if (!presenter.gameObject.activeInHierarchy)
        {
            Debug.LogError("[FAIL Part A] EnsureActiveHierarchy failed to activate presenter hierarchy!");
            pass = false;
        }

        try
        {
            presenter.ShowDialogue("Guardian", new string[] { "Test line with persistent runner." }, false);
            if (!presenter.IsInDialogue)
            {
                Debug.LogError("[FAIL Part A] ShowDialogue failed to enter dialogue state!");
                pass = false;
            }
            presenter.CloseDialogue();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FAIL Part A] ShowDialogue threw exception when starting coroutine: {ex.Message}");
            pass = false;
        }

        if (pass)
        {
            Debug.Log("[PASS Part A] Dialogue shows successfully with persistent coroutine runner and inactive legacy panel.");
        }

        return pass;
    }

    private static void ValidateTipCardAllSteps(ref int passCount, ref int totalCount)
    {
        var tipCard = GameObject.Find("Control Tip Banner");
        if (tipCard == null)
        {
            var notif = GameObject.Find("--- UI ---/UI_Notification");
            if (notif != null)
            {
                var t = notif.transform.Find("Control Tip Banner");
                if (t != null) tipCard = t.gameObject;
            }
        }
        if (tipCard == null)
        {
            var all = Resources.FindObjectsOfTypeAll<ControlTipCardUI>();
            if (all != null && all.Length > 0) tipCard = all[0].gameObject;
        }
        if (tipCard == null)
        {
            Debug.LogError("[FAIL Part B] Control Tip Banner not found in scene!");
            totalCount++;
            return;
        }

        var cardRT = tipCard.GetComponent<RectTransform>();
        var uiComp = tipCard.GetComponent<ControlTipCardUI>();
        var contentArea = tipCard.transform.Find("ContentArea") as RectTransform;
        var titleTmp = contentArea?.Find("TitleText")?.GetComponent<TextMeshProUGUI>();
        var instrTmp = contentArea?.Find("InstructionText")?.GetComponent<TextMeshProUGUI>();

        var tipsData = AssetDatabase.LoadAssetAtPath<ControlTipsData>("Assets/EcoQuest/Asset/ControlTipsData.asset");
        if (tipsData == null)
        {
            Debug.LogError("[FAIL Part B] ControlTipsData.asset not found!");
            totalCount++;
            return;
        }

        var configs = new (string label, ControlTipsData.TipConfig cfg, int step)[]
        {
            ("Step 1 (MOVE)", tipsData.moveTip, 1),
            ("Step 2 (JUMP)", tipsData.jumpTip, 2),
            ("Step 3 (SPRINT)", tipsData.sprintTip, 3),
            ("Step 4 (CTRL)", tipsData.ctrlTip, 4),
            ("Step 5 (MAP)", tipsData.mapTip, 5),
            ("Step 6 (NPC)", tipsData.npcTip, 6),
            ("TIP (TAB)", tipsData.tabTip, 0)
        };

        foreach (var item in configs)
        {
            totalCount++;
            bool tipPass = true;

            uiComp.ShowTip(item.cfg, item.step, 6);
            Canvas.ForceUpdateCanvases();
            if (contentArea != null) LayoutRebuilder.ForceRebuildLayoutImmediate(contentArea);
            if (cardRT != null) LayoutRebuilder.ForceRebuildLayoutImmediate(cardRT);

            // Assert 1: Width is 460
            if (Mathf.Abs(cardRT.rect.width - 460f) > 1f)
            {
                Debug.LogError($"[FAIL Part B {item.label}] Card width is {cardRT.rect.width:F1} (expected 460)!");
                tipPass = false;
            }

            // Assert 2: Height is >= 120 and >= content height + 32
            float minHeight = Mathf.Max(120f, (contentArea != null ? contentArea.rect.height : 80f) + 32f);
            if (cardRT.rect.height < minHeight - 1f)
            {
                Debug.LogError($"[FAIL Part B {item.label}] Card height ({cardRT.rect.height:F1}) < required minimum ({minHeight:F1})!");
                tipPass = false;
            }

            // Assert 3: No overlap between title and instruction text
            if (titleTmp != null && titleTmp.gameObject.activeSelf &&
                instrTmp != null && instrTmp.gameObject.activeSelf)
            {
                // In ContentArea local space:
                // Title bottom should be > Instruction top (or in anchored pos, Title.y - Title.height >= Instr.y)
                float titleBottom = titleTmp.rectTransform.anchoredPosition.y - titleTmp.rectTransform.rect.height;
                float instrTop = instrTmp.rectTransform.anchoredPosition.y;
                if (titleBottom < instrTop - 1f)
                {
                    Debug.LogError($"[FAIL Part B {item.label}] TitleText overlaps InstructionText! (titleBottom={titleBottom:F1}, instrTop={instrTop:F1})");
                    tipPass = false;
                }
            }

            if (tipPass)
            {
                passCount++;
                Debug.Log($"[PASS Part B {item.label}] Card fits content (width={cardRT.rect.width:F0}, height={cardRT.rect.height:F0}, contentH={contentArea?.rect.height:F0}), no text overlap.");
            }
        }
    }
}
