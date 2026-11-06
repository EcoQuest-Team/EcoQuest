using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EcoQuest.CityQuest.Editor
{
    public static class CityQuestSceneSetupAndValidator
    {
        private const string IconsFolder = "Assets/EcoQuest/UI/Icons";
        private const string SharedUiFolder = "Assets/UI/EcoQuest";
        private const string FontsFolder = "Assets/EcoQuest/UI/Fonts";
        private const string ReportsFolder = "C:/EcoQuest.gg/Reports/CityQuestUI";

        // =====================================================================
        // MENU ITEM 1: APPLY CITY QUEST UI POLISH
        // =====================================================================

        [MenuItem("EcoQuest/Apply City Quest UI Polish")]
        public static void ApplyCityQuestUIPolish()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path != CityDistrictVolume.AllowedScenePath && activeScene.name != "City Scene")
            {
                Debug.LogError($"[CityQuestPolish] Must run in 'Assets/EcoQuest/City Scene.unity' (current='{activeScene.path}'). Aborting.");
                return;
            }

            // 1. Ensure Icons are imported as Sprites
            EnsureIconImportSettings();

            // 2. Load Sprite assets
            Sprite panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{SharedUiFolder}/panel_rounded_forest_green.png");
            Sprite tabSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{SharedUiFolder}/pill_tab_green.png");
            Sprite checkSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconsFolder}/Icon_Check.png");
            Sprite starFilledSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconsFolder}/Icon_StarFilled.png");
            Sprite starOutlineSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconsFolder}/Icon_StarOutline.png");
            Sprite clockSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconsFolder}/Icon_Clock.png");
            Sprite coinSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconsFolder}/Icon_Coin.png");
            Sprite compassArrowSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconsFolder}/Icon_CompassArrow.png");
            Sprite catBioSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconsFolder}/Icon_Category_Bio.png");
            Sprite catRecycleSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconsFolder}/Icon_Category_Recycle.png");
            Sprite catNonBioSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconsFolder}/Icon_Category_NonBio.png");
            Sprite catHazardSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconsFolder}/Icon_Category_Hazard.png");

            // 3. Configure Ranger Robin (Explorer_Var2) scene instance overrides
            GameObject robinGo = GameObject.Find("Explorer_Var2");
            Transform robinTransform = null;
            CinemachineCamera robinDialogueCam = null;
            Transform dialogueCamPoint = null;

            if (robinGo != null)
            {
                robinTransform = robinGo.transform;

                // Ensure dialogue camera point child
                dialogueCamPoint = robinTransform.Find("DialogueCameraPoint");
                if (dialogueCamPoint == null)
                {
                    GameObject camPointGo = new GameObject("DialogueCameraPoint");
                    camPointGo.transform.SetParent(robinTransform, false);
                    dialogueCamPoint = camPointGo.transform;
                }
                dialogueCamPoint.localPosition = new Vector3(-0.4f, 1.35f, 2.75f);
                dialogueCamPoint.LookAt(robinTransform.TransformPoint(new Vector3(0f, 1.25f, 0f)));

                robinDialogueCam = dialogueCamPoint.GetComponent<CinemachineCamera>();
                if (robinDialogueCam == null)
                {
                    robinDialogueCam = dialogueCamPoint.gameObject.AddComponent<CinemachineCamera>();
                }
                robinDialogueCam.Lens.FieldOfView = 50f;
                robinDialogueCam.Lens.NearClipPlane = 0.05f;
                robinDialogueCam.Priority.Value = 0;

                // Configure Robin billboard head marker
                Transform markerRoot = robinTransform.Find("RobinQuestMarker");
                if (markerRoot == null)
                {
                    GameObject mGo = new GameObject("RobinQuestMarker");
                    mGo.transform.SetParent(robinTransform, false);
                    mGo.transform.localPosition = new Vector3(0f, 2.25f, 0f);
                    markerRoot = mGo.transform;

                    GameObject textGo = new GameObject("MarkerText");
                    textGo.transform.SetParent(markerRoot, false);
                    var tmp = textGo.AddComponent<TextMeshPro>();
                    tmp.text = "!";
                    tmp.fontSize = 7.5f;
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.color = new Color(1f, 0.85f, 0.20f, 1f);
                }

                // Remove legacy cube diamond if present
                Transform oldDiamond = markerRoot.Find("MarkerDiamond");
                if (oldDiamond != null) UnityEngine.Object.DestroyImmediate(oldDiamond.gameObject);

                // Ensure ground snapped & agent disabled while stationary
                var nav = robinGo.GetComponent<NavMeshAgent>();
                if (nav != null) nav.enabled = false;

                var ctrl = robinGo.GetComponent<NpcExplorerController>();
                if (ctrl != null) ctrl.enabled = true;

                EditorUtility.SetDirty(robinGo);
            }

            // 4. Find UI root & rebuild UI_CityTrashDetective
            GameObject uiRoot = GameObject.Find("--- UI ---");
            if (uiRoot == null)
            {
                Debug.LogError("[CityQuestPolish] '--- UI ---' root not found in City Scene!");
                return;
            }

            Transform oldCityQuestUi = uiRoot.transform.Find("UI_CityTrashDetective");
            if (oldCityQuestUi != null)
            {
                UnityEngine.Object.DestroyImmediate(oldCityQuestUi.gameObject);
            }

            CityTrashDetectiveUI detectiveUI = BuildPolishedCityTrashDetectiveUI(
                uiRoot.transform,
                panelSprite, tabSprite, checkSprite, starFilledSprite, starOutlineSprite,
                clockSprite, coinSprite, compassArrowSprite, catBioSprite, catRecycleSprite,
                catNonBioSprite, catHazardSprite,
                out QuizUIController bonusQuizUI);

            // 5. Wire Manager & Numeric Settings
            var mgr = UnityEngine.Object.FindFirstObjectByType<CityTrashDetectiveManager>(FindObjectsInactive.Include);
            if (mgr != null)
            {
                mgr.ui = detectiveUI;
                mgr.bonusQuizAdapter = mgr.GetComponent<CityBonusQuizAdapter>();
                if (mgr.bonusQuizAdapter != null)
                {
                    mgr.bonusQuizAdapter.quizUI = bonusQuizUI;
                }

                mgr.rangerRobinTransform = robinTransform;
                mgr.rangerRobinController = robinTransform != null ? robinTransform.GetComponent<NpcExplorerController>() : null;
                mgr.dialogueCameraPoint = dialogueCamPoint;
                mgr.robinDialogueCam = robinDialogueCam;

                // Wire Letterbox bars
                var topBar = GameObject.Find("TopLetterboxBar");
                if (topBar != null) mgr.topLetterboxBar = topBar.GetComponent<RectTransform>();
                var botBar = GameObject.Find("BottomLetterboxBar");
                if (botBar != null) mgr.bottomLetterboxBar = botBar.GetComponent<RectTransform>();

                // Numeric settings: 240 s timer, 4 hint charges
                mgr.questDurationSeconds = 240f;
                mgr.maxHintCharges = 4;
                mgr.hintCooldownSeconds = 25f;

                if (mgr.introDialogueLines != null)
                {
                    for (int i = 0; i < mgr.introDialogueLines.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(mgr.introDialogueLines[i]) && mgr.introDialogueLines[i].Contains("3:00"))
                        {
                            mgr.introDialogueLines[i] = mgr.introDialogueLines[i].Replace("3:00", "4:00");
                        }
                    }
                }

                EditorUtility.SetDirty(mgr);
            }

            // 6. Fix UI_InteractPrompt/Panel tint so panel_rounded_forest_green renders true #0F2A1E @ 90% and 2px #6DBE6D border
            Transform interactPanel = uiRoot.transform.Find("UI_InteractPrompt/Panel");
            if (interactPanel != null)
            {
                var panelImg = interactPanel.GetComponent<Image>();
                if (panelImg != null)
                {
                    if (panelSprite != null) panelImg.sprite = panelSprite;
                    panelImg.type = Image.Type.Sliced;
                    panelImg.color = Color.white;
                    EditorUtility.SetDirty(panelImg);
                }
                var wip = interactPanel.GetComponentInParent<WorldInteractionPrompt>(true);
                if (wip != null)
                {
                    wip.EnsureCenteredLayout();
                    EditorUtility.SetDirty(wip);
                }
            }

            // 7. Reset UI_Blur_Volume to weight=0, inactive at rest, with Bokeh DOF profile ready
            var blurVol = UIBackgroundBlur.EnsureVolume();
            if (blurVol != null)
            {
                blurVol.weight = 0f;
                blurVol.gameObject.SetActive(false);
                EditorUtility.SetDirty(blurVol);
                EditorUtility.SetDirty(blurVol.gameObject);
            }

            // 8. Ensure shared DialogueCameraRig exists under --- MANAGERS ---
            var rig = EcoQuest.Shared.DialogueCameraRig.Instance;
            if (rig != null)
            {
                EditorUtility.SetDirty(rig.gameObject);
            }

            // 9. Ensure DialoguePresenter footer [E] Continue keycap is vertically centered (48px height, MiddleCenter)
            var dp = UnityEngine.Object.FindFirstObjectByType<DialoguePresenter>(FindObjectsInactive.Include);
            if (dp != null)
            {
                var kcp = dp.GetComponentInChildren<KeyCapPrompt>(true);
                if (kcp != null)
                {
                    var hlg = kcp.GetComponent<HorizontalLayoutGroup>();
                    if (hlg != null)
                    {
                        hlg.childAlignment = TextAnchor.MiddleCenter;
                        hlg.childControlHeight = true;
                        hlg.childControlWidth = true;
                        EditorUtility.SetDirty(hlg);
                    }
                }
                EditorUtility.SetDirty(dp);
            }

            // 10. Ensure all 10 pooled trash pickups are prewarmed with visual models from database
            if (mgr != null && mgr.poolRoot != null && mgr.database != null)
            {
                var pickups = mgr.poolRoot.GetComponentsInChildren<CityTrashPickupInstance>(true);
                for (int p = 0; p < pickups.Length; p++)
                {
                    pickups[p].PrewarmVisuals(mgr.database);
                    pickups[p].ReturnToPoolImmediate();
                    EditorUtility.SetDirty(pickups[p]);
                }
            }

            var lbPanel = GameObject.Find("--- UI ---/UI_LitterbugQuest/QuestPanel");
            if (lbPanel != null)
            {
                var rt = lbPanel.GetComponent<RectTransform>();
                if (rt != null)
                {
                    Undo.RecordObject(rt, "Fix Litterbug QuestPanel Pos");
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(40f, -145f);
                    rt.sizeDelta = new Vector2(395f, rt.sizeDelta.y);
                    EditorUtility.SetDirty(rt);
                }
            }

            var clPanel = GameObject.Find("--- UI ---/UI_CityTrashDetective/ChecklistPanel");
            if (clPanel != null)
            {
                var rt = clPanel.GetComponent<RectTransform>();
                if (rt != null)
                {
                    Undo.RecordObject(rt, "Fix ChecklistPanel Pos");
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(40f, -145f);
                    rt.sizeDelta = new Vector2(395f, 480f);
                    EditorUtility.SetDirty(rt);
                }
            }

            // 11. Synchronize UI_Coins text styling to match World 1 (Forest Scene)
            var coinTxtGo = GameObject.Find("--- UI ---/UI_Coins/Coin Txt") ?? GameObject.Find("UI_Coins/Coin Txt");
            if (coinTxtGo != null)
            {
                var tmp = coinTxtGo.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    Undo.RecordObject(tmp, "Sync UI_Coins Text");
                    TMP_FontAsset libFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
                    if (libFont != null) tmp.font = libFont;
                    tmp.color = new Color(1f, 0.92549026f, 0.65882355f, 1f); // #FFECAB
                    tmp.fontSize = 55f;
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.alignment = TextAlignmentOptions.MidlineLeft;
                    EditorUtility.SetDirty(tmp);
                }
            }

            // 12. Polish ResultsCard button placement & centered compact keycaps
            var resCardGo = GameObject.Find("--- UI ---/UI_CityTrashDetective/ResultsAndBonusPopup/ResultsCard") ?? GameObject.Find("ResultsCard");
            if (resCardGo != null)
            {
                var ecoTip = resCardGo.transform.Find("EcoTipText");
                if (ecoTip != null)
                {
                    var rt = ecoTip.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        Undo.RecordObject(rt, "Polish EcoTip Pos");
                        rt.anchorMin = new Vector2(0.08f, 0.28f);
                        rt.anchorMax = new Vector2(0.92f, 0.36f);
                        rt.anchoredPosition = Vector2.zero;
                        rt.sizeDelta = Vector2.zero;
                        EditorUtility.SetDirty(rt);
                    }
                }

                PolishResultButton(resCardGo.transform.Find("TakeQuizButton"), new Vector2(0f, 104f), new Vector2(440f, 46f), "E", "Take Bonus Quiz (+70 Coins Max)");
                PolishResultButton(resCardGo.transform.Find("PlayAgainButton"), new Vector2(0f, 50f), new Vector2(320f, 46f), "Q", "Continue (Back to Robin)");
            }

            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(activeScene);
                EditorSceneManager.SaveScene(activeScene);
            }
            Debug.Log("[CityQuestPolish] SUCCESS: City Quest UI Polish applied and scene saved cleanly.");
        }

        private static void PolishResultButton(Transform btnTrans, Vector2 pos, Vector2 size, string fallbackKey, string actionText)
        {
            if (btnTrans == null) return;
            var rt = btnTrans.GetComponent<RectTransform>();
            if (rt != null)
            {
                Undo.RecordObject(rt, "Polish Button Transform");
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
                EditorUtility.SetDirty(rt);
            }

            var hlg = btnTrans.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null) hlg = btnTrans.gameObject.AddComponent<HorizontalLayoutGroup>();
            Undo.RecordObject(hlg, "Configure Button HLG");
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 10f;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            EditorUtility.SetDirty(hlg);

            var keyBadge = btnTrans.Find("KeycapBadge");
            if (keyBadge != null)
            {
                var krt = keyBadge.GetComponent<RectTransform>();
                if (krt != null)
                {
                    Undo.RecordObject(krt, "KeyBadge Size");
                    krt.anchorMin = new Vector2(0.5f, 0.5f);
                    krt.anchorMax = new Vector2(0.5f, 0.5f);
                    krt.pivot = new Vector2(0.5f, 0.5f);
                    krt.anchoredPosition = Vector2.zero;
                    krt.sizeDelta = new Vector2(26f, 26f);
                    EditorUtility.SetDirty(krt);
                }
                var kle = keyBadge.GetComponent<LayoutElement>();
                if (kle == null) kle = keyBadge.gameObject.AddComponent<LayoutElement>();
                Undo.RecordObject(kle, "KeyBadge LayoutElement");
                kle.minWidth = 26f;
                kle.preferredWidth = 26f;
                kle.minHeight = 26f;
                kle.preferredHeight = 26f;
                EditorUtility.SetDirty(kle);

                var ktmp = keyBadge.GetComponentInChildren<TextMeshProUGUI>(true);
                if (ktmp != null)
                {
                    Undo.RecordObject(ktmp, "KeyBadge TMP");
                    ktmp.fontSize = 13f;
                    ktmp.fontStyle = FontStyles.Bold;
                    ktmp.alignment = TextAlignmentOptions.Center;
                    EditorUtility.SetDirty(ktmp);
                }
            }

            var actionLabel = btnTrans.Find("ActionLabel");
            if (actionLabel != null)
            {
                var ltmp = actionLabel.GetComponent<TextMeshProUGUI>();
                if (ltmp != null)
                {
                    Undo.RecordObject(ltmp, "ActionLabel TMP");
                    ltmp.fontSize = 14f;
                    ltmp.fontStyle = FontStyles.Bold;
                    ltmp.textWrappingMode = TextWrappingModes.NoWrap;
                    EditorUtility.SetDirty(ltmp);
                }
                var csf = actionLabel.GetComponent<ContentSizeFitter>();
                if (csf == null) csf = actionLabel.gameObject.AddComponent<ContentSizeFitter>();
                Undo.RecordObject(csf, "ActionLabel CSF");
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
                EditorUtility.SetDirty(csf);
            }

            var kcp = btnTrans.GetComponent<KeyCapPrompt>();
            if (kcp != null)
            {
                Undo.RecordObject(kcp, "KeyCapPrompt Config");
                kcp.SetCustomKeycapHeight(26f);
                kcp.SetPrompt(fallbackKey, actionText);
                EditorUtility.SetDirty(kcp);
            }
        }

        // =====================================================================
        // MENU ITEM 2: BUILD & POPULATE (COMPLETE GENERATION)
        // =====================================================================

        [MenuItem("EcoQuest/Build & Populate City Trash Detective")]
        public static void BuildAllCityTrashDetective()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path != CityDistrictVolume.AllowedScenePath && activeScene.name != "City Scene")
            {
                Debug.LogError($"[CityQuestBuilder] Must run in 'Assets/EcoQuest/City Scene.unity' (current='{activeScene.path}'). Aborting.");
                return;
            }

            CityTrashDatabase db = CityTrashAssetGeneratorAndValidator.GenerateAllTrashAssets();

            GameObject questRoot = GameObject.Find("--- QUEST ---");
            if (questRoot != null) UnityEngine.Object.DestroyImmediate(questRoot);
            questRoot = new GameObject("--- QUEST ---");

            Transform districtsRoot = new GameObject("Districts").transform;
            districtsRoot.SetParent(questRoot.transform, false);

            Transform spawnPointsRoot = new GameObject("SpawnPoints").transform;
            spawnPointsRoot.SetParent(questRoot.transform, false);

            Transform poolRoot = new GameObject("PickupPool").transform;
            poolRoot.SetParent(questRoot.transform, false);

            List<CityDistrictVolume> districtVolumes = CreateDistrictVolumes(districtsRoot);
            GenerateVerifiedSpawnPoints(districtVolumes, spawnPointsRoot, 18);

            // Pre-create 10 pooled pickup instances
            for (int i = 0; i < 10; i++)
            {
                GameObject pGo = new GameObject($"PooledCityTrash_{i:00}");
                pGo.transform.SetParent(poolRoot, false);
                pGo.AddComponent<CityTrashPickupInstance>();
                pGo.SetActive(false);
            }

            // Manager & Quiz Adapter
            GameObject managerGo = new GameObject("CityTrashDetectiveManager");
            managerGo.transform.SetParent(questRoot.transform, false);
            CityTrashDetectiveManager mgr = managerGo.AddComponent<CityTrashDetectiveManager>();
            CityBonusQuizAdapter quizAdapter = managerGo.AddComponent<CityBonusQuizAdapter>();

            mgr.database = db;
            mgr.questRoot = questRoot.transform;
            mgr.districtsRoot = districtsRoot;
            mgr.spawnPointsRoot = spawnPointsRoot;
            mgr.poolRoot = poolRoot;
            mgr.questDurationSeconds = 240f;
            mgr.maxHintCharges = 4;

            ApplyCityQuestUIPolish();
        }

        // =====================================================================
        // UI BUILDER (LEFT-ANCHORED CHECKLIST, 9-SLICED THEME PANELS, IMAGE ICONS)
        // =====================================================================

        private static CityTrashDetectiveUI BuildPolishedCityTrashDetectiveUI(
            Transform uiRoot,
            Sprite panelSprite, Sprite tabSprite, Sprite checkSprite,
            Sprite starFilledSprite, Sprite starOutlineSprite, Sprite clockSprite,
            Sprite coinSprite, Sprite compassArrowSprite, Sprite catBioSprite,
            Sprite catRecycleSprite, Sprite catNonBioSprite, Sprite catHazardSprite,
            out QuizUIController bonusQuizUI)
        {
            TMP_FontAsset headingFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Fredoka SDF.asset");
            TMP_FontAsset bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Nunito SDF.asset");
            if (headingFont == null) headingFont = TMP_Settings.defaultFontAsset;
            if (bodyFont == null) bodyFont = headingFont;

            const string printableAscii = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
            if (headingFont != null && headingFont.atlasPopulationMode == AtlasPopulationMode.Dynamic)
            {
                headingFont.TryAddCharacters(printableAscii);
            }
            if (bodyFont != null && bodyFont.atlasPopulationMode == AtlasPopulationMode.Dynamic)
            {
                bodyFont.TryAddCharacters(printableAscii);
            }

            GameObject canvasGo = new GameObject("UI_CityTrashDetective");
            canvasGo.transform.SetParent(uiRoot, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            CityTrashDetectiveUI ui = canvasGo.AddComponent<CityTrashDetectiveUI>();

            // UITheme Colors (WCAG AAA/AA)
            Color panelFill = new Color(0.043f, 0.141f, 0.098f, 0.97f);     // #0B2419 @ 97%
            Color borderColor = new Color(0.427f, 0.745f, 0.427f, 1f);      // #6DBE6D
            Color accentGreen = new Color(0.180f, 0.490f, 0.310f, 1f);      // #2E7D4F
            Color goldAccent = new Color(1.00f, 0.84f, 0.25f, 1f);          // #FFD54F
            Color creamText = new Color(0.96f, 0.95f, 0.92f, 1f);           // #F5F2EB
            Color rowInnerFill = new Color(0.08f, 0.19f, 0.13f, 0.90f);

            // Assign shared sprites to UI component
            ui.starFilledSprite = starFilledSprite;
            ui.starOutlineSprite = starOutlineSprite;
            ui.checkSprite = checkSprite;
            ui.clockSprite = clockSprite;
            ui.coinSprite = coinSprite;
            ui.compassArrowSprite = compassArrowSprite;
            ui.categoryBioSprite = catBioSprite;
            ui.categoryRecycleSprite = catRecycleSprite;
            ui.categoryNonBioSprite = catNonBioSprite;
            ui.categoryHazardSprite = catHazardSprite;

            // -----------------------------------------------------------------
            // 1. FIELD CHECKLIST PANEL (Top-Left 395x480, margin left 40, top -145)
            // -----------------------------------------------------------------
            GameObject checklistGo = CreateSlicedPanel(canvasGo.transform, "ChecklistPanel",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                CityTrashDetectiveUI.ChecklistHomePos, new Vector2(395f, 480f),
                panelSprite, panelFill, borderColor);
            CanvasGroup checklistCg = checklistGo.AddComponent<CanvasGroup>();

            // Header tab pill "Quest"
            GameObject tabPillGo = CreateSlicedPanel(checklistGo.transform, "QuestTabPill",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(18f, 0f), new Vector2(90f, 28f),
                tabSprite, accentGreen, borderColor);
            CreateTMP(tabPillGo.transform, "TabText", "QUEST", headingFont, 13f, creamText,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // Header Title + Timer
            TextMeshProUGUI headerTitle = CreateTMP(checklistGo.transform, "HeaderProgressText", "FIELD CHECKLIST (0/10)",
                headingFont, 16f, creamText, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -38f), new Vector2(-130f, -12f));

            // Clock icon + Timer Text
            GameObject clockIconGo = CreateImage(checklistGo.transform, "ClockIcon",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-96f, -25f), new Vector2(18f, 18f), clockSprite, goldAccent);

            TextMeshProUGUI timerTmp = CreateTMP(checklistGo.transform, "TimerText", "04:00",
                headingFont, 17f, goldAccent, TextAlignmentOptions.MidlineRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-92f, -38f), new Vector2(-16f, -12f));

            TextMeshProUGUI timerPausedTmp = CreateTMP(checklistGo.transform, "TimerPausedText", "PAUSED",
                headingFont, 10f, new Color(1f, 0.5f, 0.5f, 1f), TextAlignmentOptions.Center,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-92f, -50f), new Vector2(-16f, -38f));
            timerPausedTmp.gameObject.SetActive(false);

            // Thin progress bar
            GameObject progBarBg = CreateSlicedPanel(checklistGo.transform, "ProgressBarBg",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -44f), new Vector2(-32f, 6f),
                null, new Color(0.06f, 0.12f, 0.08f, 0.8f), Color.clear);

            GameObject progBarFillGo = CreateImage(progBarBg.transform, "ProgressBarFill",
                Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero,
                null, accentGreen);
            Image progBarFill = progBarFillGo.GetComponent<Image>();
            progBarFill.type = Image.Type.Filled;
            progBarFill.fillMethod = Image.FillMethod.Horizontal;
            progBarFill.fillAmount = 0f;

            // 10 Rows
            ui.rowWidgets = new CityTrashDetectiveUI.ChecklistRowWidgets[10];
            for (int r = 0; r < 10; r++)
            {
                float yTop = -58f - r * 37f;
                GameObject rowGo = CreateSlicedPanel(checklistGo.transform, $"Row_{r:00}",
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, yTop), new Vector2(-28f, 33f),
                    panelSprite, rowInnerFill, Color.clear);
                CanvasGroup rowCg = rowGo.AddComponent<CanvasGroup>();
                Image rowBg = rowGo.GetComponent<Image>();

                // Item Icon (26x26)
                GameObject iconGo = CreateImage(rowGo.transform, "Icon",
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(6f, 0f), new Vector2(26f, 26f), null, Color.white);
                Image iconImg = iconGo.GetComponent<Image>();

                // Name (single line, ellipsis, no wrap, width ~146px)
                TextMeshProUGUI nameTmp = CreateTMP(rowGo.transform, "ItemName", $"Item #{r + 1}",
                    bodyFont, 12f, creamText, TextAlignmentOptions.MidlineLeft,
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(36f, 0f), new Vector2(182f, 0f));
                nameTmp.overflowMode = TextOverflowModes.Ellipsis;
                nameTmp.textWrappingMode = TextWrappingModes.NoWrap;
                nameTmp.enableWordWrapping = false;

                // Category Badge (106x20) with category icon + masked left-right ping-pong marquee text
                GameObject catPillGo = CreateSlicedPanel(rowGo.transform, "CategoryBadge",
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-84f, 0f), new Vector2(106f, 20f),
                    tabSprite, accentGreen, Color.clear);
                Image catPillImg = catPillGo.GetComponent<Image>();

                GameObject catIconGo = CreateImage(catPillGo.transform, "CatIcon",
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(3.5f, 0f), new Vector2(12f, 12f), catBioSprite, Color.white);
                Image catIconImg = catIconGo.GetComponent<Image>();

                // Masked container for smooth left-right ping-pong marquee (85px wide)
                GameObject catMaskGo = new GameObject("CatTextMask");
                catMaskGo.transform.SetParent(catPillGo.transform, false);
                RectTransform catMaskRt = catMaskGo.AddComponent<RectTransform>();
                catMaskRt.anchorMin = new Vector2(0f, 0f);
                catMaskRt.anchorMax = new Vector2(1f, 1f);
                catMaskRt.offsetMin = new Vector2(18f, 0f);
                catMaskRt.offsetMax = new Vector2(-3f, 0f);
                catMaskGo.AddComponent<RectMask2D>();

                TextMeshProUGUI catTmp = CreateTMP(catMaskGo.transform, "CatText", "BIODEGRADABLE",
                    headingFont, 10f, Color.white, TextAlignmentOptions.MidlineLeft,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
                catTmp.rectTransform.pivot = new Vector2(0f, 0.5f);
                catTmp.rectTransform.sizeDelta = new Vector2(200f, 20f);
                catTmp.rectTransform.anchoredPosition = Vector2.zero;
                catTmp.overflowMode = TextOverflowModes.Overflow;
                catTmp.textWrappingMode = TextWrappingModes.NoWrap;
                catTmp.enableWordWrapping = false;

                UIPingPongMarquee marquee = catTmp.gameObject.AddComponent<UIPingPongMarquee>();
                marquee.maskRect = catMaskRt;
                marquee.textRect = catTmp.rectTransform;
                marquee.tmpText = catTmp;
                marquee.scrollSpeed = 16f;
                marquee.pauseDuration = 1.4f;

                // District Tag (single line, no wrapping on "Residential")
                TextMeshProUGUI distTmp = CreateTMP(rowGo.transform, "DistrictTag", "Park",
                    bodyFont, 9.5f, new Color(0.75f, 0.90f, 0.80f, 1f), TextAlignmentOptions.MidlineRight,
                    new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-82f, 0f), new Vector2(-28f, 0f));
                distTmp.textWrappingMode = TextWrappingModes.NoWrap;
                distTmp.overflowMode = TextOverflowModes.Ellipsis;
                distTmp.enableWordWrapping = false;

                // Checkbox (20x20 rounded) + Checkmark Image
                GameObject chkGo = CreateSlicedPanel(rowGo.transform, "CheckBox",
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-6f, 0f), new Vector2(20f, 20f),
                    tabSprite, new Color(0.08f, 0.18f, 0.12f, 0.9f), borderColor);
                Image chkBg = chkGo.GetComponent<Image>();

                GameObject chkIconGo = CreateImage(chkGo.transform, "CheckIcon",
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(15f, 15f), checkSprite, Color.white);
                Image chkIconImg = chkIconGo.GetComponent<Image>();
                chkIconImg.enabled = false;

                ui.rowWidgets[r] = new CityTrashDetectiveUI.ChecklistRowWidgets
                {
                    rowGroup = rowCg,
                    rowBackground = rowBg,
                    itemIcon = iconImg,
                    itemNameText = nameTmp,
                    categoryPill = catPillImg,
                    categoryIcon = catIconImg,
                    categoryPillText = catTmp,
                    districtTagText = distTmp,
                    checkBoxBg = chkBg,
                    checkBoxIcon = chkIconImg
                };
            }

            // Footer Hint Bar with Keycaps
            GameObject footerBar = CreateSlicedPanel(checklistGo.transform, "FooterBar",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 6f), new Vector2(-24f, 32f),
                tabSprite, new Color(0.05f, 0.11f, 0.08f, 0.9f), Color.clear);

            TextMeshProUGUI footerTmp = CreateTMP(footerBar.transform, "FooterHintText",
                "<b>[TAB]</b> Hide/Show   <b>[F]</b> Hint (4/4)",
                bodyFont, 12f, goldAccent, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            ui.checklistCanvasGroup = checklistCg;
            ui.checklistRect = checklistGo.GetComponent<RectTransform>();
            ui.headerProgressText = headerTitle;
            ui.headerProgressBarFill = progBarFill;
            ui.timerIcon = clockIconGo.GetComponent<Image>();
            ui.timerText = timerTmp;
            ui.timerPausedText = timerPausedTmp;
            ui.footerHintText = footerTmp;

            // -----------------------------------------------------------------
            // 2. TRASH ID CARD TOAST (Bottom-Right 360x110)
            // -----------------------------------------------------------------
            GameObject idCardGo = CreateSlicedPanel(canvasGo.transform, "TrashIdCardToast",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-24f, 24f), new Vector2(360f, 110f),
                panelSprite, panelFill, borderColor);
            CanvasGroup idCardCg = idCardGo.AddComponent<CanvasGroup>();

            GameObject idIconBg = CreateSlicedPanel(idCardGo.transform, "IconFrame",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(76f, 76f),
                tabSprite, rowInnerFill, borderColor);
            GameObject idIconGo = CreateImage(idIconBg.transform, "Icon",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(66f, 66f), null, Color.white);
            Image idIconImg = idIconGo.GetComponent<Image>();

            TextMeshProUGUI idTitleTmp = CreateTMP(idCardGo.transform, "IdTitle", "Banana Peel  +5 Coins",
                headingFont, 15f, creamText, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(98f, -32f), new Vector2(-10f, -6f));

            GameObject idCatPill = CreateSlicedPanel(idCardGo.transform, "IdCategoryBadge",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(98f, -34f), new Vector2(136f, 20f),
                tabSprite, accentGreen, Color.clear);
            GameObject idCatIconGo = CreateImage(idCatPill.transform, "CatIcon",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(5f, 0f), new Vector2(14f, 14f), catBioSprite, Color.white);
            TextMeshProUGUI idCatTmp = CreateTMP(idCatPill.transform, "IdCategoryText", "BIODEGRADABLE",
                headingFont, 10f, Color.white, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(18f, 0f), Vector2.zero);

            TextMeshProUGUI idFactTmp = CreateTMP(idCardGo.transform, "IdFactText",
                "Banana peels rot into rich soil food in a few weeks when composted!",
                bodyFont, 12f, new Color(0.88f, 0.94f, 0.90f, 1f), TextAlignmentOptions.TopLeft,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(98f, 8f), new Vector2(-10f, 52f));

            ui.idCardCanvasGroup = idCardCg;
            ui.idCardRect = idCardGo.GetComponent<RectTransform>();
            ui.idCardIcon = idIconImg;
            ui.idCardTitleText = idTitleTmp;
            ui.idCardCategoryBadge = idCatPill.GetComponent<Image>();
            ui.idCardCategoryIcon = idCatIconGo.GetComponent<Image>();
            ui.idCardCategoryText = idCatTmp;
            ui.idCardFactText = idFactTmp;

            // -----------------------------------------------------------------
            // 3. HINT COMPASS OVERLAY (Top-Center 380x64)
            // -----------------------------------------------------------------
            GameObject compassGo = CreateSlicedPanel(canvasGo.transform, "CompassHintHud",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(380f, 64f),
                panelSprite, panelFill, borderColor);
            CanvasGroup compassCg = compassGo.AddComponent<CanvasGroup>();

            GameObject arrowCircle = CreateSlicedPanel(compassGo.transform, "ArrowDisc",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(14f, 0f), new Vector2(42f, 42f),
                tabSprite, accentGreen, borderColor);

            GameObject arrowImgGo = CreateImage(arrowCircle.transform, "ArrowImage",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(26f, 26f), compassArrowSprite, goldAccent);

            TextMeshProUGUI compassLabel = CreateTMP(compassGo.transform, "CompassLabel", "HINT: Trash (Park) - 20m",
                headingFont, 14.5f, creamText, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(68f, 0f), new Vector2(-12f, 0f));

            ui.compassHudGroup = compassCg;
            ui.compassArrowRect = arrowImgGo.GetComponent<RectTransform>();
            ui.compassArrowImage = arrowImgGo.GetComponent<Image>();
            ui.compassLabelText = compassLabel;

            // -----------------------------------------------------------------
            // 4. SMALL THEMED TOAST (Top-Center for cooldowns & tips)
            // -----------------------------------------------------------------
            GameObject toastGo = CreateSlicedPanel(canvasGo.transform, "ThemedToast",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -100f), new Vector2(360f, 44f),
                panelSprite, panelFill, borderColor);
            CanvasGroup toastCg = toastGo.AddComponent<CanvasGroup>();
            TextMeshProUGUI toastTmp = CreateTMP(toastGo.transform, "ToastText", "Hint ready in 15s",
                headingFont, 13.5f, goldAccent, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            ui.toastCanvasGroup = toastCg;
            ui.toastRect = toastGo.GetComponent<RectTransform>();
            ui.toastText = toastTmp;

            // -----------------------------------------------------------------
            // 5. RESULTS POPUP (Rebuilt with UITheme/PopupFrame, 3 Star Icons)
            // -----------------------------------------------------------------
            GameObject resOverlay = CreateUIPanel(canvasGo.transform, "ResultsAndBonusPopup",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                new Color(0f, 0f, 0f, 0.65f));
            CanvasGroup resCg = resOverlay.AddComponent<CanvasGroup>();

            GameObject resCard = CreateSlicedPanel(resOverlay.transform, "ResultsCard",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(680f, 490f),
                panelSprite, panelFill, borderColor);

            // Tab Pill Header
            GameObject resHeaderPill = CreateSlicedPanel(resCard.transform, "HeaderPill",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(360f, 44f),
                tabSprite, accentGreen, borderColor);
            TextMeshProUGUI resTitleTmp = CreateTMP(resHeaderPill.transform, "ResultsTitle", "GREAT JOB!",
                headingFont, 19f, creamText, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // 3 Star Images
            GameObject starsRow = CreateUIPanel(resCard.transform, "StarsRow",
                new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(220f, 60f), Color.clear);
            Image[] starImgs = new Image[3];
            for (int s = 0; s < 3; s++)
            {
                float x = -64f + s * 64f;
                GameObject sGo = CreateImage(starsRow.transform, $"Star_{s}",
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(x, 0f), new Vector2(52f, 52f),
                    starFilledSprite, goldAccent);
                starImgs[s] = sGo.GetComponent<Image>();
            }

            // Stat summary
            TextMeshProUGUI resSummaryTmp = CreateTMP(resCard.transform, "SummaryText", "",
                bodyFont, 16.5f, creamText, TextAlignmentOptions.Center,
                new Vector2(0f, 0.44f), new Vector2(1f, 0.72f), new Vector2(32f, 0f), new Vector2(-32f, 0f));

            // Personal best badge + star icon
            GameObject bestBadgeGo = CreateSlicedPanel(resCard.transform, "BestBadge",
                new Vector2(0.5f, 0.38f), new Vector2(0.5f, 0.38f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(400f, 32f),
                tabSprite, rowInnerFill, borderColor);
            GameObject bestStarGo = CreateImage(bestBadgeGo.transform, "BestStar",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(18f, 18f), starFilledSprite, goldAccent);
            TextMeshProUGUI resBestTmp = CreateTMP(bestBadgeGo.transform, "BestBadgeText", "Personal Best: 3 Stars",
                headingFont, 13f, creamText, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(34f, 0f), new Vector2(-10f, 0f));

            // Eco tip
            TextMeshProUGUI resEcoTmp = CreateTMP(resCard.transform, "EcoTipText",
                "Eco Tip: Recycling 1 ton of paper saves 17 mature trees and 7,000 gallons of water!",
                bodyFont, 12.5f, new Color(0.78f, 0.92f, 0.82f, 0.9f), TextAlignmentOptions.Center,
                new Vector2(0.08f, 0.28f), new Vector2(0.92f, 0.36f), Vector2.zero, Vector2.zero);

            // Buttons
            Button btnQuiz = CreateKeycapButton(resCard.transform, "TakeQuizButton",
                "[E] Take Bonus Quiz (+70 Coins Max)", headingFont,
                new Vector2(0.5f, 0f), new Vector2(0f, 104f), new Vector2(440f, 46f),
                tabSprite, accentGreen, borderColor, goldAccent, out TextMeshProUGUI btnQuizTmp);

            Button btnRetry = CreateKeycapButton(resCard.transform, "PlayAgainButton",
                "[E] Continue", headingFont,
                new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(320f, 46f),
                tabSprite, accentGreen, borderColor, goldAccent, out TextMeshProUGUI btnRetryTmp);

            Button btnSkip = CreateKeycapButton(resCard.transform, "SkipCloseButton",
                "[Q] Close", headingFont,
                new Vector2(0.72f, 0f), new Vector2(0f, 30f), new Vector2(240f, 40f),
                tabSprite, rowInnerFill, borderColor, creamText, out TextMeshProUGUI btnSkipTmp);
            btnSkip.gameObject.SetActive(false);

            ui.resultsCanvasGroup = resCg;
            ui.resultsCardRect = resCard.GetComponent<RectTransform>();
            ui.resultsTitleText = resTitleTmp;
            ui.resultsStarImages = starImgs;
            ui.resultsSummaryText = resSummaryTmp;
            ui.resultsBestBadgeText = resBestTmp;
            ui.resultsBestBadgeStar = bestStarGo.GetComponent<Image>();
            ui.resultsEcoTipText = resEcoTmp;
            ui.resultsTakeQuizButton = btnQuiz;
            ui.resultsTakeQuizButtonText = btnQuizTmp;
            ui.resultsTakeQuizKeyCap = btnQuiz.GetComponent<KeyCapPrompt>();
            ui.resultsPlayAgainButton = btnRetry;
            ui.resultsPlayAgainButtonText = btnRetryTmp;
            ui.resultsPlayAgainKeyCap = btnRetry.GetComponent<KeyCapPrompt>();
            ui.resultsSkipCloseButton = btnSkip;
            ui.resultsSkipCloseButtonText = btnSkipTmp;

            // -----------------------------------------------------------------
            // 6. BONUS QUIZ PANEL (Cleaned of missing unicode glyphs)
            // -----------------------------------------------------------------
            bonusQuizUI = BuildPolishedQuizUIHierarchy(canvasGo.transform, headingFont, bodyFont,
                panelSprite, tabSprite, checkSprite, panelFill, rowInnerFill, accentGreen, borderColor, goldAccent, creamText);

            ui.HideAllImmediate();
            return ui;
        }

        private static QuizUIController BuildPolishedQuizUIHierarchy(
            Transform parent,
            TMP_FontAsset headingFont,
            TMP_FontAsset bodyFont,
            Sprite panelSprite,
            Sprite tabSprite,
            Sprite checkSprite,
            Color panelFill,
            Color rowInnerFill,
            Color accentGreen,
            Color borderColor,
            Color goldAccent,
            Color creamText)
        {
            GameObject quizRoot = new GameObject("UI_CityBonusQuiz");
            quizRoot.transform.SetParent(parent, false);
            RectTransform rootRt = quizRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            QuizUIController qCtrl = quizRoot.AddComponent<QuizUIController>();

            GameObject overlayGo = CreateUIPanel(quizRoot.transform, "Overlay", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.68f));
            CanvasGroup overlayCg = overlayGo.AddComponent<CanvasGroup>();

            GameObject modalGo = CreateSlicedPanel(quizRoot.transform, "ModalPanel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(820f, 620f),
                panelSprite, panelFill, borderColor);
            RectTransform modalRt = modalGo.GetComponent<RectTransform>();
            CanvasGroup modalCg = modalGo.AddComponent<CanvasGroup>();

            GameObject pillTab = CreateSlicedPanel(modalGo.transform, "PillTab",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(320f, 44f),
                tabSprite, accentGreen, borderColor);
            TextMeshProUGUI titleTmp = CreateTMP(pillTab.transform, "TabText", "BONUS QUIZ", headingFont, 19f, creamText, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            GameObject quizPanel = CreateUIPanel(modalGo.transform, "QuizPanel", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-32f, -48f), Color.clear);

            TextMeshProUGUI progTmp = CreateTMP(quizPanel.transform, "ProgressText", "Question 1 of 5", headingFont, 15f, creamText, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -34f), new Vector2(-16f, -6f));
            GameObject progBarBg = CreateSlicedPanel(quizPanel.transform, "ProgressBarBg", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(-32f, 10f), tabSprite, rowInnerFill, Color.clear);
            GameObject progBarFillGo = CreateImage(progBarBg.transform, "ProgressBarFill", Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, null, accentGreen);
            Image progBarFill = progBarFillGo.GetComponent<Image>();
            progBarFill.type = Image.Type.Filled;
            progBarFill.fillMethod = Image.FillMethod.Horizontal;

            GameObject qContainer = CreateSlicedPanel(quizPanel.transform, "QuestionContainer", new Vector2(0f, 0.62f), new Vector2(1f, 0.90f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-32f, 0f), tabSprite, rowInnerFill, borderColor);
            CanvasGroup qContainerCg = qContainer.AddComponent<CanvasGroup>();
            GameObject qImgGo = CreateImage(qContainer.transform, "Illustration", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(96f, 96f), null, Color.white);
            Image qIllust = qImgGo.GetComponent<Image>();
            TextMeshProUGUI qTextTmp = CreateTMP(qContainer.transform, "QuestionText", "Which waste category does it belong to?", headingFont, 18f, creamText, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(130f, 10f), new Vector2(-18f, -10f));

            GameObject ansContainer = CreateUIPanel(quizPanel.transform, "AnswersContainer", new Vector2(0f, 0.22f), new Vector2(1f, 0.60f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-32f, 0f), Color.clear);
            Button[] ansBtns = new Button[4];
            TextMeshProUGUI[] ansTmps = new TextMeshProUGUI[4];
            Image[] ansImgs = new Image[4];
            CanvasGroup[] ansCgs = new CanvasGroup[4];
            GameObject[] ansChecks = new GameObject[4];
            GameObject[] ansCrosses = new GameObject[4];

            for (int i = 0; i < 4; i++)
            {
                float yAnchorMax = 1f - i * 0.25f;
                float yAnchorMin = yAnchorMax - 0.22f;
                GameObject bGo = CreateSlicedPanel(ansContainer.transform, $"AnswerBtn_{i}", new Vector2(0f, yAnchorMin), new Vector2(1f, yAnchorMax), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, tabSprite, rowInnerFill, borderColor);
                ansImgs[i] = bGo.GetComponent<Image>();
                ansCgs[i] = bGo.AddComponent<CanvasGroup>();
                ansBtns[i] = bGo.AddComponent<Button>();
                ansTmps[i] = CreateTMP(bGo.transform, "Label", $"Option {i + 1}", bodyFont, 16f, creamText, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.one, new Vector2(20f, 0f), new Vector2(-48f, 0f));

                // Checkmark Image instead of unicode
                GameObject chk = CreateImage(bGo.transform, "Checkmark", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(24f, 24f), checkSprite, accentGreen);
                chk.SetActive(false);
                ansChecks[i] = chk;

                GameObject crs = CreateTMP(bGo.transform, "Cross", "X", headingFont, 20f, Color.red, TextAlignmentOptions.Center, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-42f, 0f), new Vector2(-8f, 0f)).gameObject;
                crs.SetActive(false);
                ansCrosses[i] = crs;
            }

            // Feedback Panel (generous height & padding with auto-sizing so explanation text never cuts off)
            GameObject fbPanel = CreateSlicedPanel(modalGo.transform, "FeedbackPanel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(760f, 156f), tabSprite, rowInnerFill, borderColor);
            CanvasGroup fbCg = fbPanel.AddComponent<CanvasGroup>();
            TextMeshProUGUI fbTitle = CreateTMP(fbPanel.transform, "FeedbackTitle", "CORRECT!", headingFont, 20f, goldAccent, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(0.72f, 1f), new Vector2(24f, -54f), new Vector2(-10f, -24f));
            TextMeshProUGUI fbExp = CreateTMP(fbPanel.transform, "ExplanationText", "Explanation", bodyFont, 14f, creamText, TextAlignmentOptions.TopLeft, new Vector2(0f, 0f), new Vector2(0.72f, 1f), new Vector2(24f, 12f), new Vector2(-10f, -56f));
            fbExp.enableAutoSizing = true;
            fbExp.fontSizeMin = 11.5f;
            fbExp.fontSizeMax = 14.5f;
            fbExp.lineSpacing = -4f;
            Button nextBtn = CreateKeycapButton(fbPanel.transform, "NextButton", "Next >", headingFont, new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(140f, 44f), tabSprite, accentGreen, borderColor, creamText, out _);

            // Results Panel (Single Score Bonus Quiz layout)
            GameObject resPanel = CreateSlicedPanel(modalGo.transform, "ResultsPanel", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-48f, -64f), tabSprite, rowInnerFill, borderColor);
            CanvasGroup resPanelCg = resPanel.AddComponent<CanvasGroup>();
            TextMeshProUGUI resTitle = CreateTMP(resPanel.transform, "ResultsTitle", "Bonus Quiz Results", headingFont, 26f, goldAccent, TextAlignmentOptions.Center, new Vector2(0f, 0.78f), new Vector2(1f, 0.94f), Vector2.zero, Vector2.zero);
            TextMeshProUGUI preScore = CreateTMP(resPanel.transform, "PreScoreText", "", bodyFont, 18f, creamText, TextAlignmentOptions.Center, new Vector2(0f, 0.64f), new Vector2(1f, 0.76f), Vector2.zero, Vector2.zero);
            preScore.gameObject.SetActive(false);
            TextMeshProUGUI postScore = CreateTMP(resPanel.transform, "PostScoreText", "Score: 5 / 5", headingFont, 28f, accentGreen, TextAlignmentOptions.Center, new Vector2(0f, 0.58f), new Vector2(1f, 0.74f), Vector2.zero, Vector2.zero);
            GameObject impBadge = CreateSlicedPanel(resPanel.transform, "ImprovementBadge", new Vector2(0.5f, 0.48f), new Vector2(0.5f, 0.48f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360f, 36f), tabSprite, accentGreen, Color.clear);
            TextMeshProUGUI impText = CreateTMP(impBadge.transform, "ImprovementText", "Great Job!", headingFont, 16f, creamText, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            TextMeshProUGUI encText = CreateTMP(resPanel.transform, "EncouragingText", "", bodyFont, 14f, creamText, TextAlignmentOptions.Center, new Vector2(0.08f, 0.26f), new Vector2(0.92f, 0.42f), Vector2.zero, Vector2.zero);
            encText.enableAutoSizing = true;
            encText.fontSizeMin = 12f;
            encText.fontSizeMax = 15f;
            TextMeshProUGUI goldText = CreateTMP(resPanel.transform, "BonusGoldText", "+70 Bonus Gold Earned!", headingFont, 20f, goldAccent, TextAlignmentOptions.Center, new Vector2(0f, 0.14f), new Vector2(1f, 0.24f), Vector2.zero, Vector2.zero);
            Button contBtn = CreateKeycapButton(resPanel.transform, "ContinueButton", "Continue", headingFont, new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(220f, 44f), tabSprite, accentGreen, borderColor, creamText, out _);

            // Wire private serialized fields on QuizUIController
            SerializedObject so = new SerializedObject(qCtrl);
            so.FindProperty("quizCanvasRoot").objectReferenceValue = quizRoot;
            so.FindProperty("overlayCanvasGroup").objectReferenceValue = overlayCg;
            so.FindProperty("modalPanelRect").objectReferenceValue = modalRt;
            so.FindProperty("modalPanelCanvasGroup").objectReferenceValue = modalCg;
            so.FindProperty("quizPanel").objectReferenceValue = quizPanel;
            so.FindProperty("titleText").objectReferenceValue = titleTmp;
            so.FindProperty("progressText").objectReferenceValue = progTmp;
            so.FindProperty("progressBarFill").objectReferenceValue = progBarFill;
            so.FindProperty("questionContainerRect").objectReferenceValue = qContainer.GetComponent<RectTransform>();
            so.FindProperty("questionContainerCanvasGroup").objectReferenceValue = qContainerCg;
            so.FindProperty("questionText").objectReferenceValue = qTextTmp;
            so.FindProperty("questionIllustrationImage").objectReferenceValue = qIllust;
            so.FindProperty("answersContainer").objectReferenceValue = ansContainer.GetComponent<RectTransform>();

            SetObjectArrayProp(so.FindProperty("answerButtons"), ansBtns);
            SetObjectArrayProp(so.FindProperty("answerButtonTexts"), ansTmps);
            SetObjectArrayProp(so.FindProperty("answerButtonImages"), ansImgs);
            SetObjectArrayProp(so.FindProperty("answerButtonCanvasGroups"), ansCgs);
            SetObjectArrayProp(so.FindProperty("answerCheckmarks"), ansChecks);
            SetObjectArrayProp(so.FindProperty("answerCrosses"), ansCrosses);

            so.FindProperty("feedbackPanel").objectReferenceValue = fbPanel;
            so.FindProperty("feedbackCanvasGroup").objectReferenceValue = fbCg;
            so.FindProperty("feedbackTitleText").objectReferenceValue = fbTitle;
            so.FindProperty("explanationText").objectReferenceValue = fbExp;
            so.FindProperty("nextQuestionButton").objectReferenceValue = nextBtn;

            so.FindProperty("resultsPanel").objectReferenceValue = resPanel;
            so.FindProperty("resultsCanvasGroup").objectReferenceValue = resPanelCg;
            so.FindProperty("resultsTitleText").objectReferenceValue = resTitle;
            so.FindProperty("preScoreText").objectReferenceValue = preScore;
            so.FindProperty("postScoreText").objectReferenceValue = postScore;
            so.FindProperty("improvementBadgeRect").objectReferenceValue = impBadge.GetComponent<RectTransform>();
            so.FindProperty("improvementText").objectReferenceValue = impText;
            so.FindProperty("encouragingMessageText").objectReferenceValue = encText;
            so.FindProperty("bonusGoldText").objectReferenceValue = goldText;
            so.FindProperty("continueButton").objectReferenceValue = contBtn;
            so.ApplyModifiedPropertiesWithoutUndo();

            quizRoot.SetActive(false);
            return qCtrl;
        }

        // =====================================================================
        // UI PRIMITIVE HELPERS
        // =====================================================================

        private static GameObject CreateSlicedPanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta,
            Sprite sprite, Color bgColor, Color outlineColor)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            Image img = go.AddComponent<Image>();
            if (sprite != null && !name.StartsWith("Row_"))
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
                // panel_rounded_forest_green already has #0F2A1E fill and 2px #6DBE6D border baked into its texture;
                // use Color.white so Unity does not multiply dark green by dark green into gray/black.
                if (sprite.name.Contains("panel_rounded_forest_green"))
                {
                    img.color = Color.white;
                }
                else
                {
                    img.color = bgColor;
                }
            }
            else
            {
                img.color = bgColor;
            }

            if (outlineColor.a > 0.05f && (sprite == null || !sprite.name.Contains("panel_rounded_forest_green")))
            {
                Outline outline = go.AddComponent<Outline>();
                outline.effectColor = outlineColor;
                outline.effectDistance = new Vector2(2f, -2f);
            }

            return go;
        }

        private static GameObject CreateUIPanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta, Color bgCol)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            Image img = go.AddComponent<Image>();
            img.color = bgCol;
            return go;
        }

        private static GameObject CreateImage(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta,
            Sprite sprite, Color tint)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            Image img = go.AddComponent<Image>();
            if (sprite != null) img.sprite = sprite;
            img.color = tint;
            img.preserveAspect = true;
            return go;
        }

        private static TextMeshProUGUI CreateTMP(Transform parent, string name, string text,
            TMP_FontAsset font, float size, Color color, TextAlignmentOptions align,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button CreateKeycapButton(Transform parent, string name, string label,
            TMP_FontAsset font, Vector2 anchor, Vector2 anchoredPos, Vector2 size,
            Sprite bgSprite, Color bgCol, Color outlineColor, Color textCol, out TextMeshProUGUI labelTmp)
        {
            GameObject go = CreateSlicedPanel(parent, name, anchor, anchor, new Vector2(0.5f, 0.5f),
                anchoredPos, size, bgSprite, bgCol, outlineColor);
            Button btn = go.AddComponent<Button>();

            string key = null;
            string cleanLabel = label;
            if (label.StartsWith("[") && label.Contains("]"))
            {
                int closeIdx = label.IndexOf(']');
                key = label.Substring(1, closeIdx - 1);
                cleanLabel = label.Substring(closeIdx + 1).Trim();
            }

            if (!string.IsNullOrEmpty(key))
            {
                // Centered HorizontalLayoutGroup so the keycap badge and text label are dead-centered together
                HorizontalLayoutGroup hlg = go.AddComponent<HorizontalLayoutGroup>();
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.spacing = 10f;
                hlg.childControlWidth = false;
                hlg.childControlHeight = false;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = false;

                // Compact 26x26 keycap badge
                GameObject keyBox = CreateSlicedPanel(go.transform, "KeycapBadge",
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(26f, 26f), bgSprite, new Color(0.91f, 0.91f, 0.91f, 1f), new Color(0.20f, 0.20f, 0.20f, 0.9f));
                RectTransform keyBoxRt = keyBox.GetComponent<RectTransform>();
                Image keyBoxImg = keyBox.GetComponent<Image>();

                LayoutElement keyBoxLe = keyBox.AddComponent<LayoutElement>();
                keyBoxLe.minWidth = 26f;
                keyBoxLe.preferredWidth = 26f;
                keyBoxLe.minHeight = 26f;
                keyBoxLe.preferredHeight = 26f;

                TextMeshProUGUI keyTmp = CreateTMP(keyBox.transform, "KeyText", key, font, 13f,
                    new Color(0.11f, 0.11f, 0.11f, 1f), TextAlignmentOptions.Center,
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                keyTmp.fontStyle = FontStyles.Bold;

                // Action label
                GameObject labelGo = new GameObject("ActionLabel", typeof(RectTransform));
                labelGo.transform.SetParent(go.transform, false);
                RectTransform labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.sizeDelta = new Vector2(0f, 26f);

                labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
                labelTmp.text = cleanLabel;
                labelTmp.font = font;
                labelTmp.fontSize = 14f;
                labelTmp.color = textCol;
                labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
                labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
                labelTmp.raycastTarget = false;
                labelTmp.fontStyle = FontStyles.Bold;

                ContentSizeFitter csf = labelGo.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

                KeyCapPrompt kcp = go.AddComponent<KeyCapPrompt>();
                SerializedObject so = new SerializedObject(kcp);
                so.FindProperty("keycapRect").objectReferenceValue = keyBoxRt;
                so.FindProperty("keycapImage").objectReferenceValue = keyBoxImg;
                so.FindProperty("keycapText").objectReferenceValue = keyTmp;
                so.FindProperty("actionLabel").objectReferenceValue = labelTmp;
                so.FindProperty("fallbackKey").stringValue = key;
                so.FindProperty("defaultActionText").stringValue = cleanLabel;
                so.FindProperty("customKeycapHeight").floatValue = 26f;
                so.ApplyModifiedPropertiesWithoutUndo();

                kcp.SetCustomKeycapHeight(26f);
                kcp.SetPrompt(key, cleanLabel);
            }
            else
            {
                labelTmp = CreateTMP(go.transform, "Text", label, font, 15f, textCol, TextAlignmentOptions.Center,
                    Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
            }

            return btn;
        }

        private static void SetObjectArrayProp(SerializedProperty prop, UnityEngine.Object[] arr)
        {
            if (prop == null || arr == null) return;
            prop.arraySize = arr.Length;
            for (int i = 0; i < arr.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = arr[i];
            }
        }

        private static void EnsureIconImportSettings()
        {
            if (!Directory.Exists(IconsFolder)) Directory.CreateDirectory(IconsFolder);
            string[] files = Directory.GetFiles(IconsFolder, "*.png");
            foreach (string file in files)
            {
                string unityPath = file.Replace('\\', '/');
                TextureImporter importer = AssetImporter.GetAtPath(unityPath) as TextureImporter;
                if (importer != null)
                {
                    bool dirty = false;
                    if (importer.textureType != TextureImporterType.Sprite)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        dirty = true;
                    }
                    if (!importer.alphaIsTransparency)
                    {
                        importer.alphaIsTransparency = true;
                        dirty = true;
                    }
                    if (dirty)
                    {
                        importer.SaveAndReimport();
                    }
                }
            }
        }

        // =====================================================================
        // MENU ITEM 3: EXTENDED VALIDATION & SIMULATION
        // =====================================================================

        [MenuItem("EcoQuest/Validate City Quest Setup & Simulate 20 Attempts")]
        public static bool ValidateCityQuestAndSimulate20Attempts()
        {
            var mgr = UnityEngine.Object.FindFirstObjectByType<CityTrashDetectiveManager>(FindObjectsInactive.Include);
            if (mgr == null || mgr.database == null)
            {
                Debug.LogError("[ValidateCityQuest] FAIL: CityTrashDetectiveManager or database not found in scene.");
                return false;
            }

            bool allValid = true;

            // 1. Validate The Two Numbers
            if (Mathf.Abs(mgr.questDurationSeconds - 240f) > 0.1f)
            {
                Debug.LogError($"[ValidateCityQuest] FAIL: questDurationSeconds is {mgr.questDurationSeconds}, expected 240.0!");
                allValid = false;
            }
            if (mgr.maxHintCharges != 4)
            {
                Debug.LogError($"[ValidateCityQuest] FAIL: maxHintCharges is {mgr.maxHintCharges}, expected 4!");
                allValid = false;
            }

            // 2. Validate TMP font character availability (assert all characters exist)
            var ui = UnityEngine.Object.FindFirstObjectByType<CityTrashDetectiveUI>(FindObjectsInactive.Include);
            if (ui != null)
            {
                var tmps = ui.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var tmp in tmps)
                {
                    if (string.IsNullOrEmpty(tmp.text)) continue;
                    TMP_FontAsset font = tmp.font;
                    if (font == null) font = TMP_Settings.defaultFontAsset;

                    string content = tmp.text;
                    if (font != null && font.atlasPopulationMode == AtlasPopulationMode.Dynamic)
                    {
                        font.TryAddCharacters(content);
                    }
                    for (int cIdx = 0; cIdx < content.Length; cIdx++)
                    {
                        char c = content[cIdx];
                        if (c == '<')
                        {
                            // Skip TMP rich text tags
                            int close = content.IndexOf('>', cIdx);
                            if (close > cIdx)
                            {
                                cIdx = close;
                                continue;
                            }
                        }
                        if (char.IsWhiteSpace(c) || c == '\n' || c == '\r') continue;

                        bool hasChar = font != null && (font.HasCharacter(c, true, true) || HasFallbackCharacter(font, c));
                        if (!hasChar)
                        {
                            Debug.LogError($"[ValidateCityQuest] FAIL: Missing font glyph '{c}' (U+{(int)c:X4}) on '{GetHierarchyPath(tmp.transform)}' in font '{font?.name}'!");
                            allValid = false;
                        }
                    }
                }

                // 3. Validate Checklist Anchor & Margins
                if (ui.checklistRect != null)
                {
                    if (ui.checklistRect.anchorMin.x != 0f || ui.checklistRect.anchorMax.x != 0f)
                    {
                        Debug.LogError($"[ValidateCityQuest] FAIL: Checklist anchor is not left! (min={ui.checklistRect.anchorMin}, max={ui.checklistRect.anchorMax})");
                        allValid = false;
                    }
                    if (Mathf.Abs(ui.checklistRect.sizeDelta.x - 395f) > 10f)
                    {
                        Debug.LogError($"[ValidateCityQuest] FAIL: Checklist width is {ui.checklistRect.sizeDelta.x}, expected 395!");
                        allValid = false;
                    }
                }
            }

            // 4. Validate Spawn Points
            List<CityTrashSpawnPoint> spawns = new List<CityTrashSpawnPoint>();
            if (mgr.spawnPointsRoot != null)
            {
                mgr.spawnPointsRoot.GetComponentsInChildren(true, spawns);
            }

            Dictionary<CityDistrict, int> countsByDist = new Dictionary<CityDistrict, int>();
            foreach (CityDistrict d in Enum.GetValues(typeof(CityDistrict))) countsByDist[d] = 0;

            for (int i = 0; i < spawns.Count; i++)
            {
                countsByDist[spawns[i].district]++;
            }
            foreach (var kv in countsByDist)
            {
                if (kv.Value < 15)
                {
                    Debug.LogError($"[ValidateCityQuest] FAIL: District '{kv.Key}' has {kv.Value} spawn points (< 15)!");
                    allValid = false;
                }
            }

            // 5. 20-Attempt Simulation
            int simPass = 0;
            List<CityTrashDetectiveManager.ChecklistSlot> simSlots = new List<CityTrashDetectiveManager.ChecklistSlot>(10);
            for (int attempt = 1; attempt <= 20; attempt++)
            {
                int seed = 50000 + attempt * 7919;
                bool ok = CityTrashDetectiveManager.GenerateChecklistAndSpawns(seed, mgr.database, spawns, simSlots);
                if (ok && simSlots.Count == 10) simPass++;
            }

            if (simPass != 20)
            {
                Debug.LogError($"[ValidateCityQuest] FAIL: 20-attempt simulation passed {simPass}/20!");
                allValid = false;
            }

            // 6. Run 10-Cycle Ranger Robin Dialogue Stress Test (framing, player head, DOF focus, HUD restore, overlay set idempotency)
            bool stressPass = RunRobinDialogue10xStressTest();
            if (!stressPass)
            {
                allValid = false;
            }

            Debug.Log($"[ValidateCityQuest] Validation Result: {(allValid ? "ALL PASS" : "FAILED")} | Timer=240, Hints=4, Sim={simPass}/20, Robin10x={stressPass}.");
            return allValid;
        }

        [MenuItem("EcoQuest/Run Robin 10x Dialogue Stress Test")]
        public static bool RunRobinDialogue10xStressTest()
        {
            var mgr = UnityEngine.Object.FindFirstObjectByType<CityTrashDetectiveManager>(FindObjectsInactive.Include);
            var orch = UIOrchestrator.Instance;
            var hud = HudVisibilityService.Instance;
            var rig = EcoQuest.Shared.DialogueCameraRig.Instance;
            Camera mainCam = Camera.main ?? WorldInteractionPrompt.GetActiveWorldCamera();
            GameObject player = GameObject.FindGameObjectWithTag("Player");

            if (mgr == null || orch == null || rig == null || mainCam == null || player == null || mgr.rangerRobinTransform == null)
            {
                Debug.LogError("[Robin10xTest] FAIL: Missing required scene objects (Manager, UIOrchestrator, DialogueCameraRig, MainCamera, Player, or Robin).");
                return false;
            }

            // Ensure hooks wired for edit-mode / play-mode simulation
            UIOrchestrator.IsCityScavengerActiveHook = () => mgr.QuestState == CityDetectiveState.Active;
            UIOrchestrator.IsCityScavengerOverlayOpenHook = () => mgr.IsOverlayOrModalOpen;
            orch.ClearAllOverlays();
            hud?.ClearAllReasons(0f);

            // Position player 2.1m in front of Ranger Robin
            Transform robin = mgr.rangerRobinTransform;
            Vector3 robinFwd = robin.forward;
            robinFwd.y = 0f;
            if (robinFwd.sqrMagnitude < 0.01f) robinFwd = Vector3.forward;
            robinFwd.Normalize();

            Vector3 savedPlayerPos = player.transform.position;
            Quaternion savedPlayerRot = player.transform.rotation;
            int savedCullingMask = mainCam.cullingMask;

            player.transform.position = robin.position + robinFwd * 2.1f;
            player.transform.rotation = Quaternion.LookRotation(-robinFwd, Vector3.up);

            Renderer headMeshRenderer = null;
            foreach (var r in player.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Head_Mesh")
                {
                    headMeshRenderer = r;
                    break;
                }
            }

            bool allCyclesPassed = true;
            string[] cycleModes = new string[]
            {
                "Accept_1",
                "KeepSearching_2",
                "CancelQuest_3",
                "NotNow_4",
                "CloseWithQ_5",
                "ClickSkipAndAccept_6",
                "RapidEPresses_7",
                "ImmediateRetalkLockout_8",
                "ExitAnimationRetalkLockout_9",
                "AcceptAndTabToggle_10"
            };

            try
            {
                for (int c = 0; c < 10; c++)
                {
                    string mode = cycleModes[c];

                    // --- OPEN DIALOGUE ---
                    orch.EnterCinematicProfile("RangerRobin");
                    // Test idempotent duplicate push (must be a no-op)
                    orch.PushOverlay("RangerRobin");
                    if (orch.ActiveOverlayCount != 1)
                    {
                        Debug.LogError($"[Robin10xTest] Cycle {c + 1} ({mode}) FAIL: Duplicate PushOverlay('RangerRobin') resulted in count={orch.ActiveOverlayCount} (expected 1).");
                        allCyclesPassed = false;
                    }

                    rig.EnterDialogue(robin, player.transform, 0f, "RangerRobin");
                    if (rig.VirtualCamera != null)
                    {
                        mainCam.transform.SetPositionAndRotation(rig.VirtualCamera.transform.position, rig.VirtualCamera.transform.rotation);
                        mainCam.fieldOfView = rig.VirtualCamera.Lens.FieldOfView;
                    }
                    UIBackgroundBlur.SetWeight(1f);
                    UIBackgroundBlur.UpdateDialogueFocusImmediate(robin, EcoQuest.Shared.DialogueCameraRig.GetNpcHeadPosition(robin));

                    // --- ASSERT INVARIANTS DURING DIALOGUE ---
                    // 1. Player head renderer enabled & layer 7 visible in MainCamera cullingMask
                    bool headLayerVisible = (mainCam.cullingMask & (1 << 7)) != 0;
                    bool headRendererOk = headMeshRenderer != null && headMeshRenderer.enabled && headMeshRenderer.gameObject.activeInHierarchy;
                    if (!headLayerVisible || !headRendererOk)
                    {
                        Debug.LogError($"[Robin10xTest] Cycle {c + 1} ({mode}) FAIL during dialogue: headLayerVisible={headLayerVisible}, headRendererOk={headRendererOk}");
                        allCyclesPassed = false;
                    }

                    // 2. DOF focus distance within 0.5m of camera-to-NPC-head distance
                    Vector3 npcHeadPos = EcoQuest.Shared.DialogueCameraRig.GetNpcHeadPosition(robin);
                    float actualCamToNpcDist = Vector3.Distance(mainCam.transform.position, npcHeadPos);
                    float dofFocusDist = UIBackgroundBlur.CurrentFocusDistance;
                    if (Mathf.Abs(dofFocusDist - actualCamToNpcDist) > 0.5f)
                    {
                        Debug.LogError($"[Robin10xTest] Cycle {c + 1} ({mode}) FAIL during dialogue: DOF focusDistance={dofFocusDist:F2}m vs camToNpc={actualCamToNpcDist:F2}m (> 0.5m diff)");
                        allCyclesPassed = false;
                    }

                    // 3. Raycast / bounds from dialogue camera to Robin's head does NOT hit player backpack/jacket
                    Ray camToNpcRay = new Ray(mainCam.transform.position, (npcHeadPos - mainCam.transform.position).normalized);
                    bool hitPlayerBackpack = false;
                    foreach (var r in player.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null || !r.enabled || r.name == "Capsule") continue;
                        if (r.name.Contains("Backpack") || r.name.Contains("Jacket"))
                        {
                            if (r.bounds.IntersectRay(camToNpcRay, out float hitD) && hitD < actualCamToNpcDist)
                            {
                                hitPlayerBackpack = true;
                                break;
                            }
                        }
                    }
                    if (hitPlayerBackpack)
                    {
                        Debug.LogError($"[Robin10xTest] Cycle {c + 1} ({mode}) FAIL during dialogue: Camera-to-Robin-head ray occluded by player backpack/jacket!");
                        allCyclesPassed = false;
                    }

                    // --- EXECUTE CYCLE ACTION ---
                    if (mode == "Accept_1" || mode == "ClickSkipAndAccept_6" || mode == "AcceptAndTabToggle_10")
                    {
                        mgr.StartNewAttempt();
                    }
                    else if (mode == "CancelQuest_3")
                    {
                        mgr.CancelActiveAttempt();
                    }

                    // --- CLOSE DIALOGUE ---
                    rig.ExitDialogue("RangerRobin", 0f);
                    UIBackgroundBlur.SetWeight(0f);
                    orch.ExitCinematicProfile("RangerRobin");
                    // Test idempotent duplicate pop (must be a no-op)
                    orch.PopOverlay("RangerRobin");

                    if (mgr.QuestState == CityDetectiveState.Active && mgr.ui != null && !orch.IsQuestPanelUserHidden)
                    {
                        mgr.ui.ShowChecklist(new List<CityTrashDetectiveManager.ChecklistSlot>(mgr.ActiveChecklist), mgr.CollectedCount, mgr.RemainingSeconds, mgr.RemainingHintCharges, mgr.CurrentHintCooldown);
                    }
                    orch.ApplyCurrentProfile(0f);

                    if (mode == "AcceptAndTabToggle_10")
                    {
                        orch.ToggleQuestPanel("Test_TAB_Hide");
                        orch.ApplyCurrentProfile(0f);
                        orch.ToggleQuestPanel("Test_TAB_Show");
                        orch.ApplyCurrentProfile(0f);
                    }

                    // --- ASSERT INVARIANTS AFTER CLOSE ---
                    // 1. Overlay set is empty & FlowStep is FreePlay
                    if (orch.IsOverlayActive || orch.ActiveOverlayCount != 0 || orch.CurrentFlowStep != UIOrchestrator.FlowStep.FreePlay)
                    {
                        Debug.LogError($"[Robin10xTest] Cycle {c + 1} ({mode}) FAIL after close: overlayCount={orch.ActiveOverlayCount}, flowStep={orch.CurrentFlowStep}");
                        allCyclesPassed = false;
                    }

                    // 2. Every HUD element in active profile has alpha == 1 and activeInHierarchy == true
                    UIOrchestrator.UIElement[] coreElements = new UIOrchestrator.UIElement[]
                    {
                        UIOrchestrator.UIElement.Gear,
                        UIOrchestrator.UIElement.Minimap,
                        UIOrchestrator.UIElement.Coins,
                        UIOrchestrator.UIElement.Crosshair
                    };
                    foreach (var el in coreElements)
                    {
                        if (!orch.GetExpectedVisibility(el))
                        {
                            Debug.LogError($"[Robin10xTest] Cycle {c + 1} ({mode}) FAIL after close: GetExpectedVisibility({el}) is false!");
                            allCyclesPassed = false;
                        }
                    }

                    if (mgr.QuestState == CityDetectiveState.Active)
                    {
                        bool clExpected = orch.GetExpectedVisibility(UIOrchestrator.UIElement.QuestPanel);
                        bool clActual = mgr.ui != null && mgr.ui.checklistCanvasGroup != null && mgr.ui.checklistCanvasGroup.gameObject.activeInHierarchy &&
                                        mgr.ui.checklistCanvasGroup.alpha > 0.99f;
                        if (!clExpected || !clActual)
                        {
                            Debug.LogError($"[Robin10xTest] Cycle {c + 1} ({mode}) FAIL after close: Checklist visible expected={clExpected}, actual={clActual}");
                            allCyclesPassed = false;
                        }
                    }

                    // 3. Blur weight is 0 and blur volume is disabled
                    var blurVol = GameObject.Find("UI_Blur_Volume");
                    if (UIBackgroundBlur.GetWeight() > 0.001f || (blurVol != null && blurVol.activeSelf))
                    {
                        Debug.LogError($"[Robin10xTest] Cycle {c + 1} ({mode}) FAIL after close: blurWeight={UIBackgroundBlur.GetWeight():F3}, volumeActive={(blurVol != null && blurVol.activeSelf)}");
                        allCyclesPassed = false;
                    }

                    // 4. Player camera cullingMask restored to first-person (layer 7 hidden)
                    if ((mainCam.cullingMask & (1 << 7)) != 0)
                    {
                        Debug.LogError($"[Robin10xTest] Cycle {c + 1} ({mode}) FAIL after close: MainCamera cullingMask still includes layer 7 (HiddenFromFirstPerson)!");
                        allCyclesPassed = false;
                    }
                }
            }
            finally
            {
                mgr.CancelActiveAttempt();
                rig.ForceRestoreAll();
                orch.ClearAllOverlays();
                hud?.ClearAllReasons(0f);
                UIBackgroundBlur.Cleanup();
                mainCam.cullingMask = savedCullingMask;
                player.transform.SetPositionAndRotation(savedPlayerPos, savedPlayerRot);
            }

            Debug.Log($"[Robin10xTest] 10-Cycle Ranger Robin Dialogue Stress Test: {(allCyclesPassed ? "10/10 PASS" : "FAILED")}");
            return allCyclesPassed;
        }

        private static bool HasFallbackCharacter(TMP_FontAsset font, char c)
        {
            if (font == null || font.fallbackFontAssetTable == null) return false;
            foreach (var fb in font.fallbackFontAssetTable)
            {
                if (fb != null && fb.HasCharacter(c)) return true;
            }
            return false;
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "";
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        // =====================================================================
        // DISTRICT VOLUMES & SPAWN POINTS BUILDERS
        // =====================================================================

        private static List<CityDistrictVolume> CreateDistrictVolumes(Transform parent)
        {
            var list = new List<CityDistrictVolume>(5);
            var specs = new (CityDistrict dist, Vector3 center, Vector3 size)[]
            {
                (CityDistrict.Park, new Vector3(-135f, 5f, 25f), new Vector3(85f, 20f, 75f)),
                (CityDistrict.MainStreet, new Vector3(-80f, 5f, 0f), new Vector3(60f, 20f, 130f)),
                (CityDistrict.Market, new Vector3(-25f, 5f, -25f), new Vector3(70f, 20f, 70f)),
                (CityDistrict.Riverside, new Vector3(30f, 5f, 40f), new Vector3(60f, 20f, 120f)),
                (CityDistrict.Residential, new Vector3(-40f, 5f, 65f), new Vector3(75f, 20f, 70f))
            };

            foreach (var sp in specs)
            {
                GameObject go = new GameObject($"District_{sp.dist}");
                go.transform.SetParent(parent, false);
                go.transform.position = sp.center;
                BoxCollider col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = sp.size;

                CityDistrictVolume vol = go.AddComponent<CityDistrictVolume>();
                vol.district = sp.dist;
                list.Add(vol);
            }
            return list;
        }

        private static int GenerateVerifiedSpawnPoints(List<CityDistrictVolume> districts, Transform parent, int countPerDistrict)
        {
            int total = 0;
            System.Random rng = new System.Random(42069);

            foreach (var vol in districts)
            {
                BoxCollider col = vol.GetComponent<BoxCollider>();
                Vector3 min = col.bounds.min;
                Vector3 max = col.bounds.max;

                int created = 0;
                int attempts = 0;
                while (created < countPerDistrict && attempts < 500)
                {
                    attempts++;
                    float rx = (float)rng.NextDouble() * (max.x - min.x) + min.x;
                    float rz = (float)rng.NextDouble() * (max.z - min.z) + min.z;
                    Vector3 samplePos = new Vector3(rx, 15f, rz);

                    if (Physics.Raycast(samplePos, Vector3.down, out RaycastHit hit, 25f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 1.8f, NavMesh.AllAreas))
                        {
                            GameObject spGo = new GameObject($"Spawn_{vol.district}_{created:00}");
                            spGo.transform.SetParent(parent, false);
                            spGo.transform.position = navHit.position;

                            CityTrashSpawnPoint pt = spGo.AddComponent<CityTrashSpawnPoint>();
                            pt.district = vol.district;
                            pt.isVerifiedValid = true;
                            pt.navMeshDistance = Vector3.Distance(hit.point, navHit.position);

                            created++;
                            total++;
                        }
                    }
                }
            }
            return total;
        }
    }
}
