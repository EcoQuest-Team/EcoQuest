using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

namespace EcoQuest.Inventory.Editor
{
    public static class InventoryPrefabBuilder
    {
        private const string PrefabFolder = "Assets/EcoQuest/Inventory/Prefabs";
        private const string SpritesFolder = "Assets/EcoQuest/Inventory/Sprites";
        private const string UiSpritesFolder = "Assets/EcoQuest/Inventory/Sprites/UI";

        public static void ConfigureTextureImporters()
        {
            if (!Directory.Exists(SpritesFolder)) Directory.CreateDirectory(SpritesFolder);
            string[] sprites = Directory.GetFiles(SpritesFolder, "*.png", SearchOption.AllDirectories);
            foreach (var path in sprites)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    bool changed = false;
                    if (importer.textureType != TextureImporterType.Sprite)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        changed = true;
                    }
                    if (importer.alphaIsTransparency != true)
                    {
                        importer.alphaIsTransparency = true;
                        changed = true;
                    }
                    if (importer.mipmapEnabled != false)
                    {
                        importer.mipmapEnabled = false;
                        changed = true;
                    }
                    if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                    {
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        changed = true;
                    }
                    if (importer.filterMode != FilterMode.Bilinear)
                    {
                        importer.filterMode = FilterMode.Bilinear;
                        changed = true;
                    }
                    if (importer.maxTextureSize != 2048)
                    {
                        importer.maxTextureSize = 2048;
                        changed = true;
                    }

                    var standaloneSettings = importer.GetPlatformTextureSettings("Standalone");
                    if (standaloneSettings.textureCompression != TextureImporterCompression.Uncompressed || standaloneSettings.format != TextureImporterFormat.RGBA32 || standaloneSettings.maxTextureSize != 2048)
                    {
                        standaloneSettings.overridden = true;
                        standaloneSettings.textureCompression = TextureImporterCompression.Uncompressed;
                        standaloneSettings.format = TextureImporterFormat.RGBA32;
                        standaloneSettings.maxTextureSize = 2048;
                        importer.SetPlatformTextureSettings(standaloneSettings);
                        changed = true;
                    }

                    string fname = Path.GetFileName(path);
                    if (fname == "panel_bg.png" && importer.spriteBorder != new Vector4(16, 16, 16, 16))
                    {
                        importer.spriteBorder = new Vector4(16, 16, 16, 16);
                        changed = true;
                    }
                    else if (fname == "preview_box_bg.png" && importer.spriteBorder != new Vector4(42, 42, 42, 42))
                    {
                        importer.spriteBorder = new Vector4(42, 42, 42, 42);
                        changed = true;
                    }
                    else if (fname == "details_box_bg.png" && importer.spriteBorder != new Vector4(12, 12, 12, 12))
                    {
                        importer.spriteBorder = new Vector4(12, 12, 12, 12);
                        changed = true;
                    }
                    else if (fname.StartsWith("btn_pill_") && importer.spriteBorder != new Vector4(30, 16, 30, 16))
                    {
                        importer.spriteBorder = new Vector4(30, 16, 30, 16);
                        changed = true;
                    }
                    else if (fname == "scrollbar_handle_glow.png" && importer.spriteBorder != new Vector4(12, 16, 12, 16))
                    {
                        importer.spriteBorder = new Vector4(12, 16, 12, 16);
                        changed = true;
                    }
                    else if (fname == "scrollbar_track_recessed.png" && importer.spriteBorder != new Vector4(10, 16, 10, 16))
                    {
                        importer.spriteBorder = new Vector4(10, 16, 10, 16);
                        changed = true;
                    }
                    else if (fname.StartsWith("scrollbar_") && importer.spriteBorder != new Vector4(6, 12, 6, 12))
                    {
                        importer.spriteBorder = new Vector4(6, 12, 6, 12);
                        changed = true;
                    }
                    else if (fname == "eco_shop_panel_bg.png" && importer.spriteBorder != new Vector4(50, 50, 50, 50))
                    {
                        importer.spriteBorder = new Vector4(50, 50, 50, 50);
                        changed = true;
                    }
                    else if (fname == "eco_shop_banner_clean.png" && importer.spriteBorder != new Vector4(56, 56, 56, 56))
                    {
                        importer.spriteBorder = new Vector4(56, 56, 56, 56);
                        changed = true;
                    }
                    else if (fname == "eco_shop_well_bg.png" && importer.spriteBorder != new Vector4(24, 24, 24, 24))
                    {
                        importer.spriteBorder = new Vector4(24, 24, 24, 24);
                        changed = true;
                    }
                    else if ((fname == "eco_shop_card_bg.png" || fname == "eco_shop_card_hover.png") && importer.spriteBorder != new Vector4(24, 24, 24, 24))
                    {
                        importer.spriteBorder = new Vector4(24, 24, 24, 24);
                        changed = true;
                    }
                    else if (fname.StartsWith("btn_buy_") && importer.spriteBorder != new Vector4(30, 20, 30, 20))
                    {
                        importer.spriteBorder = new Vector4(30, 20, 30, 20);
                        changed = true;
                    }
                    else if (fname == "coin_counter_pill_bg.png" && importer.spriteBorder != new Vector4(28, 24, 28, 24))
                    {
                        importer.spriteBorder = new Vector4(28, 24, 28, 24);
                        changed = true;
                    }

                    if (changed)
                    {
                        importer.SaveAndReimport();
                    }
                }
            }
        }

        [MenuItem("Tools/EcoQuest/Inventory/Build Prefabs (UI_Inventory & UI_BagButton)", false, 15)]
        public static void BuildPrefabs()
        {
            Directory.CreateDirectory(PrefabFolder);
            InventoryEditorTools.BuildItemDatabase();
            ConfigureTextureImporters();

            TMP_FontAsset fredoka = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Fredoka SDF.asset");
            TMP_FontAsset nunito = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Nunito SDF.asset");

            UITheme theme = UITheme.Instance;
            Sprite themePanelSprite = theme != null ? theme.panelBackgroundSprite : null;
            Sprite themeTabSprite = theme != null ? theme.speakerTabGreenSprite : null;
            Sprite themeKeyFace = theme != null ? theme.keycapFaceSprite : null;
            Sprite themeKeyBase = theme != null ? theme.keycapBaseSprite : null;

            Sprite panelBg = themePanelSprite != null ? themePanelSprite : AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/panel_bg.png");
            Sprite previewBg = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/preview_box_bg.png");
            Sprite detailsBg = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/details_box_bg.png");
            Sprite slotNorm = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/slot_normal.png");
            Sprite slotHov = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/slot_hover.png");
            Sprite slotSel = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/slot_selected.png");
            Sprite sbTrack = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/scrollbar_track.png");
            Sprite sbHandle = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/scrollbar_handle.png");
            Sprite btnNorm = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/btn_pill_normal.png");
            Sprite btnHov = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/btn_pill_hover.png");
            Sprite btnDis = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/btn_pill_disabled.png");
            Sprite closeX = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/close_x_icon.png");
            Sprite backpackSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesFolder}/backpack_icon.png");
            Sprite dotSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/badge_dot_accent.png");
            Sprite keycapPlate = themeKeyFace != null ? themeKeyFace : AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/keycap_plate.png");

            InventoryItemDatabase db = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>("Assets/EcoQuest/Inventory/Data/InventoryItemDatabase.asset");

            // =========================================================================
            // 1. BUILD UI_Inventory.prefab (Responsive Layout Groups, ScreenSpaceOverlay)
            // =========================================================================
            GameObject rootGo = new GameObject("UI_Inventory",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(UI_Inventory), typeof(InventoryController));
            RectTransform rootRt = rootGo.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            Canvas rootCanvas = rootGo.GetComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 90;

            CanvasScaler rootScaler = rootGo.GetComponent<CanvasScaler>();
            rootScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            rootScaler.referenceResolution = new Vector2(1920f, 1080f);
            rootScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            rootScaler.matchWidthOrHeight = 0.5f;

            UI_Inventory uiInv = rootGo.GetComponent<UI_Inventory>();
            InventoryController ctrl = rootGo.GetComponent<InventoryController>();

            // Lighter Dim Backdrop (~0.28 alpha so blurred world shines through)
            GameObject backdropGo = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            backdropGo.transform.SetParent(rootGo.transform, false);
            RectTransform bdRt = backdropGo.GetComponent<RectTransform>();
            bdRt.anchorMin = Vector2.zero;
            bdRt.anchorMax = Vector2.one;
            bdRt.offsetMin = Vector2.zero;
            bdRt.offsetMax = Vector2.zero;
            Image bdImg = backdropGo.GetComponent<Image>();
            bdImg.color = new Color(0.02f, 0.06f, 0.04f, 0.28f);
            CanvasGroup bdCg = backdropGo.GetComponent<CanvasGroup>();
            bdCg.alpha = 0f;
            bdCg.blocksRaycasts = false;
            bdCg.interactable = false;
            backdropGo.SetActive(false);

            // Panel (~60% of screen width and 70% of height, centered, VerticalLayoutGroup)
            GameObject panelGo = new GameObject("InventoryPanel",
                typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup));
            panelGo.transform.SetParent(rootGo.transform, false);
            RectTransform pRt = panelGo.GetComponent<RectTransform>();
            pRt.anchorMin = new Vector2(0.20f, 0.15f);
            pRt.anchorMax = new Vector2(0.80f, 0.85f);
            pRt.pivot = new Vector2(0.5f, 0.5f);
            pRt.offsetMin = Vector2.zero;
            pRt.offsetMax = Vector2.zero;

            Image pImg = panelGo.GetComponent<Image>();
            pImg.sprite = panelBg;
            pImg.type = Image.Type.Sliced;
            // Shared dark-green UITheme style (#0B2419 @ 97%)
            pImg.color = (themePanelSprite != null) ? Color.white : new Color(0.043f, 0.141f, 0.098f, 0.97f);

            Outline pOutline = panelGo.GetComponent<Outline>();
            pOutline.effectColor = new Color(0.427f, 0.745f, 0.427f, 1f); // #6DBE6D
            pOutline.effectDistance = new Vector2(3f, -3f);

            CanvasGroup pCg = panelGo.GetComponent<CanvasGroup>();
            pCg.alpha = 0f;
            pCg.interactable = false;
            pCg.blocksRaycasts = false;

            VerticalLayoutGroup panelVlg = panelGo.GetComponent<VerticalLayoutGroup>();
            panelVlg.padding = new RectOffset(28, 28, 22, 24);
            panelVlg.spacing = 14f;
            panelVlg.childAlignment = TextAnchor.UpperCenter;
            panelVlg.childControlWidth = true;
            panelVlg.childControlHeight = true;
            panelVlg.childForceExpandWidth = true;
            panelVlg.childForceExpandHeight = false;

            // -------------------------------------------------------------------------
            // ROW 1: HEADER ROW ("INVENTORY" tab pill + clickable X button)
            // -------------------------------------------------------------------------
            GameObject headerRowGo = new GameObject("HeaderRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            headerRowGo.transform.SetParent(panelGo.transform, false);
            LayoutElement headerLe = headerRowGo.GetComponent<LayoutElement>();
            headerLe.minHeight = 52f;
            headerLe.preferredHeight = 52f;
            headerLe.flexibleHeight = 0f;
            HorizontalLayoutGroup headerHlg = headerRowGo.GetComponent<HorizontalLayoutGroup>();
            headerHlg.childAlignment = TextAnchor.MiddleLeft;
            headerHlg.childControlWidth = true;
            headerHlg.childControlHeight = true;
            headerHlg.childForceExpandWidth = false;
            headerHlg.childForceExpandHeight = false;
            headerHlg.spacing = 12f;

            // Title Pill Container
            GameObject titleTabGo = new GameObject("TitleTab", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            titleTabGo.transform.SetParent(headerRowGo.transform, false);
            Image titleTabImg = titleTabGo.GetComponent<Image>();
            if (themeTabSprite != null)
            {
                titleTabImg.sprite = themeTabSprite;
                titleTabImg.type = Image.Type.Sliced;
                titleTabImg.color = Color.white;
            }
            else
            {
                titleTabImg.sprite = btnNorm;
                titleTabImg.type = Image.Type.Sliced;
                titleTabImg.color = new Color(0.180f, 0.490f, 0.310f, 1f); // #2E7D4F
            }
            LayoutElement titleTabLe = titleTabGo.GetComponent<LayoutElement>();
            titleTabLe.minWidth = 250f;
            titleTabLe.preferredWidth = 250f;
            titleTabLe.minHeight = 46f;
            titleTabLe.preferredHeight = 46f;

            GameObject titleGo = new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(titleTabGo.transform, false);
            RectTransform titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = Vector2.zero;
            titleRt.anchorMax = Vector2.one;
            titleRt.offsetMin = new Vector2(16f, 2f);
            titleRt.offsetMax = new Vector2(-16f, -2f);
            TextMeshProUGUI titleTmp = titleGo.GetComponent<TextMeshProUGUI>();
            titleTmp.font = fredoka;
            titleTmp.text = "INVENTORY";
            titleTmp.fontSize = 30f;
            titleTmp.characterSpacing = 6f;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.color = Color.white;

            // Header Flexible Spacer
            GameObject headerSpacerGo = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            headerSpacerGo.transform.SetParent(headerRowGo.transform, false);
            LayoutElement headerSpacerLe = headerSpacerGo.GetComponent<LayoutElement>();
            headerSpacerLe.flexibleWidth = 1f;

            // Close Button "X"
            GameObject closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(Button), typeof(Image), typeof(LayoutElement));
            closeGo.transform.SetParent(headerRowGo.transform, false);
            LayoutElement closeLe = closeGo.GetComponent<LayoutElement>();
            closeLe.minWidth = 42f;
            closeLe.preferredWidth = 42f;
            closeLe.minHeight = 42f;
            closeLe.preferredHeight = 42f;
            Image closeImg = closeGo.GetComponent<Image>();
            closeImg.sprite = closeX;
            closeImg.preserveAspect = true;
            closeImg.color = Color.white;
            Button closeBtn = closeGo.GetComponent<Button>();

            // -------------------------------------------------------------------------
            // ROW 2: BODY ROW (Left: Grid + Scrollbar | Right: Preview + Details + USE)
            // -------------------------------------------------------------------------
            GameObject bodyRowGo = new GameObject("BodyRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            bodyRowGo.transform.SetParent(panelGo.transform, false);
            LayoutElement bodyLe = bodyRowGo.GetComponent<LayoutElement>();
            bodyLe.flexibleHeight = 1f;
            bodyLe.minHeight = 360f;
            HorizontalLayoutGroup bodyHlg = bodyRowGo.GetComponent<HorizontalLayoutGroup>();
            bodyHlg.spacing = 20f;
            bodyHlg.childAlignment = TextAnchor.UpperLeft;
            bodyHlg.childControlWidth = true;
            bodyHlg.childControlHeight = true;
            bodyHlg.childForceExpandWidth = false;
            bodyHlg.childForceExpandHeight = true;

            // LEFT COLUMN: ScrollRect + 14px VerticalScrollbar
            GameObject leftColGo = new GameObject("LeftGridArea", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            leftColGo.transform.SetParent(bodyRowGo.transform, false);
            LayoutElement leftColLe = leftColGo.GetComponent<LayoutElement>();
            leftColLe.minWidth = 560f;
            leftColLe.preferredWidth = 560f;
            leftColLe.flexibleWidth = 0f; // Fixed width: slot size NEVER shifts when selecting different items!
            leftColLe.flexibleHeight = 1f;
            HorizontalLayoutGroup leftHlg = leftColGo.GetComponent<HorizontalLayoutGroup>();
            leftHlg.spacing = 8f;
            leftHlg.childAlignment = TextAnchor.UpperLeft;
            leftHlg.childControlWidth = true;
            leftHlg.childControlHeight = true;
            leftHlg.childForceExpandWidth = false;
            leftHlg.childForceExpandHeight = true;

            // GridScrollView
            GameObject scrollGo = new GameObject("GridScrollView", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(LayoutElement));
            scrollGo.transform.SetParent(leftColGo.transform, false);
            LayoutElement scrollLe = scrollGo.GetComponent<LayoutElement>();
            scrollLe.flexibleWidth = 1f;
            scrollLe.flexibleHeight = 1f;
            Image scrollBgImg = scrollGo.GetComponent<Image>();
            scrollBgImg.sprite = detailsBg;
            scrollBgImg.type = Image.Type.Sliced;
            scrollBgImg.color = new Color(0.06f, 0.18f, 0.12f, 0.65f);

            ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = true;
            sr.decelerationRate = 0.08f;
            sr.scrollSensitivity = 32f;

            // Viewport (with RectMask2D so slots NEVER draw outside the viewport)
            GameObject vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            vpGo.transform.SetParent(scrollGo.transform, false);
            RectTransform vpRt = vpGo.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(4f, 4f);
            vpRt.offsetMax = new Vector2(-4f, -4f);
            Image vpImg = vpGo.GetComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.01f); // Transparent raycast target for mouse wheel anywhere over list
            vpImg.raycastTarget = true;
            sr.viewport = vpRt;

            // Content (3-column GridLayoutGroup + ContentSizeFitter PreferredSize)
            GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(vpGo.transform, false);
            RectTransform contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);

            GridLayoutGroup glg = contentGo.GetComponent<GridLayoutGroup>();
            glg.padding = new RectOffset(8, 8, 8, 8);
            glg.cellSize = new Vector2(150f, 150f);
            glg.spacing = new Vector2(12f, 12f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 3;
            glg.childAlignment = TextAnchor.UpperLeft;

            ContentSizeFitter contentCsf = contentGo.GetComponent<ContentSizeFitter>();
            contentCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = contentRt;

            // Empty Inventory Message (centered inside the grid viewport area, never below panel)
            GameObject emptyMsgGo = new GameObject("EmptyInventoryMessage", typeof(RectTransform), typeof(Image));
            emptyMsgGo.transform.SetParent(vpGo.transform, false);
            RectTransform emRt = emptyMsgGo.GetComponent<RectTransform>();
            emRt.anchorMin = new Vector2(0.08f, 0.35f);
            emRt.anchorMax = new Vector2(0.92f, 0.65f);
            emRt.offsetMin = Vector2.zero;
            emRt.offsetMax = Vector2.zero;
            Image emBg = emptyMsgGo.GetComponent<Image>();
            emBg.color = new Color(0.043f, 0.141f, 0.098f, 0.88f);
            emBg.raycastTarget = false;

            GameObject emTextGo = new GameObject("EmptyInventoryText", typeof(RectTransform), typeof(TextMeshProUGUI));
            emTextGo.transform.SetParent(emptyMsgGo.transform, false);
            RectTransform emTxtRt = emTextGo.GetComponent<RectTransform>();
            emTxtRt.anchorMin = Vector2.zero;
            emTxtRt.anchorMax = Vector2.one;
            emTxtRt.offsetMin = new Vector2(16f, 8f);
            emTxtRt.offsetMax = new Vector2(-16f, -8f);
            TextMeshProUGUI emTmp = emTextGo.GetComponent<TextMeshProUGUI>();
            emTmp.font = nunito;
            emTmp.text = "You have no items yet.\nVisit the shop!";
            emTmp.fontSize = 23f;
            emTmp.fontStyle = FontStyles.Bold;
            emTmp.alignment = TextAlignmentOptions.Center;
            emTmp.color = new Color(0.89f, 0.96f, 0.89f, 0.95f);
            emTmp.raycastTarget = false;
            // Remove extra TMP if Unity added one to parent
            var extraTmp = emptyMsgGo.GetComponent<TextMeshProUGUI>();
            if (extraTmp != null) UnityEngine.Object.DestroyImmediate(extraTmp);
            emptyMsgGo.SetActive(false);

            // Real Vertical Scrollbar (14 px wide inside panel at right edge of viewport, visible track, handle min 40px)
            GameObject sbGo = new GameObject("VerticalScrollbar", typeof(RectTransform), typeof(Scrollbar), typeof(Image), typeof(LayoutElement));
            sbGo.transform.SetParent(leftColGo.transform, false);
            LayoutElement sbLe = sbGo.GetComponent<LayoutElement>();
            sbLe.minWidth = 14f;
            sbLe.preferredWidth = 14f;
            sbLe.flexibleWidth = 0f;
            sbLe.flexibleHeight = 1f;
            Image sbImg = sbGo.GetComponent<Image>();
            sbImg.sprite = sbTrack;
            sbImg.type = Image.Type.Sliced;
            sbImg.color = new Color(0.12f, 0.28f, 0.20f, 0.95f);

            Scrollbar sb = sbGo.GetComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            GameObject slidingAreaGo = new GameObject("Sliding Area", typeof(RectTransform));
            slidingAreaGo.transform.SetParent(sbGo.transform, false);
            RectTransform saRt = slidingAreaGo.GetComponent<RectTransform>();
            saRt.anchorMin = Vector2.zero;
            saRt.anchorMax = Vector2.one;
            saRt.offsetMin = new Vector2(2f, 2f);
            saRt.offsetMax = new Vector2(-2f, -2f);

            GameObject handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(slidingAreaGo.transform, false);
            RectTransform handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = Vector2.zero;
            handleRt.offsetMax = Vector2.zero;
            Image handleImg = handleGo.GetComponent<Image>();
            handleImg.sprite = sbHandle;
            handleImg.type = Image.Type.Sliced;
            handleImg.color = new Color(0.427f, 0.745f, 0.427f, 1f); // #6DBE6D
            sb.targetGraphic = handleImg;
            sb.handleRect = handleRt;

            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            // -------------------------------------------------------------------------
            // RIGHT COLUMN: Item Preview, Details (Name, Effect, Owned, Description), USE button
            // -------------------------------------------------------------------------
            GameObject rightColGo = new GameObject("RightDetailsColumn", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            rightColGo.transform.SetParent(bodyRowGo.transform, false);
            LayoutElement rightColLe = rightColGo.GetComponent<LayoutElement>();
            rightColLe.minWidth = 360f;
            rightColLe.preferredWidth = 440f;
            rightColLe.flexibleWidth = 1f; // Fills remaining panel space
            rightColLe.flexibleHeight = 1f;
            VerticalLayoutGroup rightVlg = rightColGo.GetComponent<VerticalLayoutGroup>();
            rightVlg.spacing = 12f;
            rightVlg.childAlignment = TextAnchor.UpperCenter;
            rightVlg.childControlWidth = true;
            rightVlg.childControlHeight = true;
            rightVlg.childForceExpandWidth = true;
            rightVlg.childForceExpandHeight = false;

            // Preview Box
            GameObject prevGo = new GameObject("PreviewBox", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
            prevGo.transform.SetParent(rightColGo.transform, false);
            RectTransform prevRt = prevGo.GetComponent<RectTransform>();
            LayoutElement prevLe = prevGo.GetComponent<LayoutElement>();
            prevLe.flexibleHeight = 0.95f;
            prevLe.minHeight = 160f;
            prevLe.preferredHeight = 220f;
            Image prevImg = prevGo.GetComponent<Image>();
            prevImg.sprite = detailsBg;
            prevImg.type = Image.Type.Sliced;
            prevImg.color = new Color(0.07f, 0.21f, 0.15f, 0.92f);
            Outline prevOutline = prevGo.GetComponent<Outline>();
            prevOutline.effectColor = new Color(0.427f, 0.745f, 0.427f, 0.65f);
            prevOutline.effectDistance = new Vector2(2f, -2f);

            // Preview Icon (item)
            GameObject pIconGo = new GameObject("PreviewIcon", typeof(RectTransform), typeof(Image));
            pIconGo.transform.SetParent(prevGo.transform, false);
            RectTransform pIconRt = pIconGo.GetComponent<RectTransform>();
            pIconRt.anchorMin = new Vector2(0.18f, 0.14f);
            pIconRt.anchorMax = new Vector2(0.82f, 0.86f);
            pIconRt.offsetMin = Vector2.zero;
            pIconRt.offsetMax = Vector2.zero;
            Image pIconImg = pIconGo.GetComponent<Image>();
            pIconImg.preserveAspect = true;
            pIconImg.raycastTarget = false;

            // Empty Preview (Backpack icon + Select an item)
            GameObject emptyPrevGo = new GameObject("EmptyPreview", typeof(RectTransform));
            emptyPrevGo.transform.SetParent(prevGo.transform, false);
            RectTransform epRt = emptyPrevGo.GetComponent<RectTransform>();
            epRt.anchorMin = Vector2.zero;
            epRt.anchorMax = Vector2.one;
            epRt.offsetMin = Vector2.zero;
            epRt.offsetMax = Vector2.zero;

            GameObject epIconGo = new GameObject("EmptyBackpackIcon", typeof(RectTransform), typeof(Image));
            epIconGo.transform.SetParent(emptyPrevGo.transform, false);
            RectTransform epIconRt = epIconGo.GetComponent<RectTransform>();
            epIconRt.anchorMin = new Vector2(0.5f, 0.58f);
            epIconRt.anchorMax = new Vector2(0.5f, 0.58f);
            epIconRt.sizeDelta = new Vector2(96f, 96f);
            Image epIconImg = epIconGo.GetComponent<Image>();
            epIconImg.sprite = backpackSprite;
            epIconImg.preserveAspect = true;
            epIconImg.color = new Color(1f, 1f, 1f, 0.45f);
            epIconImg.raycastTarget = false;

            GameObject epTextGo = new GameObject("EmptyText", typeof(RectTransform), typeof(TextMeshProUGUI));
            epTextGo.transform.SetParent(emptyPrevGo.transform, false);
            RectTransform epTextRt = epTextGo.GetComponent<RectTransform>();
            epTextRt.anchorMin = new Vector2(0.05f, 0.08f);
            epTextRt.anchorMax = new Vector2(0.95f, 0.28f);
            epTextRt.offsetMin = Vector2.zero;
            epTextRt.offsetMax = Vector2.zero;
            TextMeshProUGUI epTmp = epTextGo.GetComponent<TextMeshProUGUI>();
            epTmp.font = nunito;
            epTmp.text = "Select an item";
            epTmp.fontSize = 20f;
            epTmp.alignment = TextAlignmentOptions.Center;
            epTmp.color = new Color(0.85f, 0.92f, 0.85f, 0.70f);
            epTmp.raycastTarget = false;

            // Preview Flash (for use item feedback)
            GameObject flashGo = new GameObject("PreviewFlash", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            flashGo.transform.SetParent(prevGo.transform, false);
            RectTransform flashRt = flashGo.GetComponent<RectTransform>();
            flashRt.anchorMin = Vector2.zero;
            flashRt.anchorMax = Vector2.one;
            flashRt.offsetMin = Vector2.zero;
            flashRt.offsetMax = Vector2.zero;
            Image flashImg = flashGo.GetComponent<Image>();
            flashImg.sprite = detailsBg;
            flashImg.type = Image.Type.Sliced;
            flashImg.color = new Color(0.6f, 1f, 0.6f, 0.6f);
            flashImg.raycastTarget = false;
            CanvasGroup flashCg = flashGo.GetComponent<CanvasGroup>();
            flashCg.alpha = 0f;
            flashCg.blocksRaycasts = false;

            // Details Box (Name, Effect, Owned count, Description)
            GameObject detGo = new GameObject("DetailsBox", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            detGo.transform.SetParent(rightColGo.transform, false);
            RectTransform detRt = detGo.GetComponent<RectTransform>();
            LayoutElement detLe = detGo.GetComponent<LayoutElement>();
            detLe.flexibleHeight = 1.05f;
            detLe.minHeight = 165f;
            detLe.preferredHeight = 210f;
            Image detImg = detGo.GetComponent<Image>();
            detImg.sprite = detailsBg;
            detImg.type = Image.Type.Sliced;
            detImg.color = new Color(0.07f, 0.21f, 0.15f, 0.92f);
            Outline detOutline = detGo.GetComponent<Outline>();
            detOutline.effectColor = new Color(0.427f, 0.745f, 0.427f, 0.65f);
            detOutline.effectDistance = new Vector2(2f, -2f);

            VerticalLayoutGroup detVlg = detGo.GetComponent<VerticalLayoutGroup>();
            detVlg.padding = new RectOffset(18, 18, 14, 14);
            detVlg.spacing = 4f;
            detVlg.childAlignment = TextAnchor.UpperLeft;
            detVlg.childControlWidth = true;
            detVlg.childControlHeight = true;
            detVlg.childForceExpandWidth = true;
            detVlg.childForceExpandHeight = false;

            // Item Name
            GameObject dNameGo = new GameObject("ItemName", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            dNameGo.transform.SetParent(detGo.transform, false);
            LayoutElement dnLe = dNameGo.GetComponent<LayoutElement>();
            dnLe.minHeight = 32f;
            dnLe.preferredHeight = 32f;
            TextMeshProUGUI dnTmp = dNameGo.GetComponent<TextMeshProUGUI>();
            dnTmp.font = fredoka;
            dnTmp.text = "No item selected";
            dnTmp.fontSize = 26f;
            dnTmp.fontStyle = FontStyles.Bold;
            dnTmp.color = Color.white;

            // Item Effect ("Restores 25 Hunger")
            GameObject dEffectGo = new GameObject("ItemEffect", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            dEffectGo.transform.SetParent(detGo.transform, false);
            LayoutElement deLe = dEffectGo.GetComponent<LayoutElement>();
            deLe.minHeight = 24f;
            deLe.preferredHeight = 24f;
            TextMeshProUGUI deTmp = dEffectGo.GetComponent<TextMeshProUGUI>();
            deTmp.font = nunito;
            deTmp.fontSize = 19f;
            deTmp.fontStyle = FontStyles.Bold;
            deTmp.color = new Color(0.427f, 0.745f, 0.427f, 1f);

            // Owned Count ("Owned: 5")
            GameObject dOwnedGo = new GameObject("ItemOwned", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            dOwnedGo.transform.SetParent(detGo.transform, false);
            LayoutElement doLe = dOwnedGo.GetComponent<LayoutElement>();
            doLe.minHeight = 22f;
            doLe.preferredHeight = 22f;
            TextMeshProUGUI doTmp = dOwnedGo.GetComponent<TextMeshProUGUI>();
            doTmp.font = nunito;
            doTmp.fontSize = 17f;
            doTmp.fontStyle = FontStyles.Bold;
            doTmp.color = new Color(0.89f, 0.96f, 0.89f, 0.90f);

            // Item Description
            GameObject dDescGo = new GameObject("ItemDescription", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            dDescGo.transform.SetParent(detGo.transform, false);
            LayoutElement ddLe = dDescGo.GetComponent<LayoutElement>();
            ddLe.flexibleHeight = 1f;
            ddLe.minHeight = 56f;
            TextMeshProUGUI ddTmp = dDescGo.GetComponent<TextMeshProUGUI>();
            ddTmp.font = nunito;
            ddTmp.fontSize = 17f;
            ddTmp.color = new Color(0.89f, 0.96f, 0.89f, 0.95f);
            ddTmp.lineSpacing = 8f;

            // USE Button Row (centered below details)
            GameObject useRowGo = new GameObject("UseButtonRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            useRowGo.transform.SetParent(rightColGo.transform, false);
            LayoutElement useRowLe = useRowGo.GetComponent<LayoutElement>();
            useRowLe.minHeight = 54f;
            useRowLe.preferredHeight = 54f;
            useRowLe.flexibleHeight = 0f;
            HorizontalLayoutGroup useRowHlg = useRowGo.GetComponent<HorizontalLayoutGroup>();
            useRowHlg.childAlignment = TextAnchor.MiddleCenter;
            useRowHlg.childControlWidth = false;
            useRowHlg.childControlHeight = false;
            useRowHlg.childForceExpandWidth = false;
            useRowHlg.childForceExpandHeight = false;

            GameObject useGo = new GameObject("UseButton", typeof(RectTransform), typeof(Button), typeof(Image), typeof(Outline), typeof(LayoutElement));
            useGo.transform.SetParent(useRowGo.transform, false);
            RectTransform useRt = useGo.GetComponent<RectTransform>();
            useRt.sizeDelta = new Vector2(180f, 52f);
            LayoutElement useLe = useGo.GetComponent<LayoutElement>();
            useLe.minWidth = 180f;
            useLe.preferredWidth = 180f;
            useLe.minHeight = 52f;
            useLe.preferredHeight = 52f;
            Image useImg = useGo.GetComponent<Image>();
            useImg.sprite = btnNorm;
            useImg.type = Image.Type.Sliced;
            useImg.color = new Color(0.180f, 0.490f, 0.310f, 1f); // #2E7D4F
            Outline useOutline = useGo.GetComponent<Outline>();
            useOutline.effectColor = new Color(0.427f, 0.745f, 0.427f, 1f);
            useOutline.effectDistance = new Vector2(2f, -2f);
            Button useBtn = useGo.GetComponent<Button>();

            GameObject useTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            useTextGo.transform.SetParent(useGo.transform, false);
            RectTransform utRt = useTextGo.GetComponent<RectTransform>();
            utRt.anchorMin = Vector2.zero;
            utRt.anchorMax = Vector2.one;
            utRt.offsetMin = Vector2.zero;
            utRt.offsetMax = Vector2.zero;
            TextMeshProUGUI utTmp = useTextGo.GetComponent<TextMeshProUGUI>();
            utTmp.font = fredoka;
            utTmp.text = "USE";
            utTmp.fontSize = 24f;
            utTmp.characterSpacing = 5f;
            utTmp.fontStyle = FontStyles.Bold;
            utTmp.alignment = TextAlignmentOptions.Center;
            utTmp.color = Color.white;

            // Footer hint strip removed per user request for clean, uncluttered presentation

            // -------------------------------------------------------------------------
            // Themed Toast Banner (for "Finish the tutorial first!", "Hunger is already full", etc.)
            // -------------------------------------------------------------------------
            GameObject toastGo = new GameObject("InventoryToast", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline));
            toastGo.transform.SetParent(rootGo.transform, false);
            RectTransform toastRt = toastGo.GetComponent<RectTransform>();
            toastRt.anchorMin = new Vector2(0.5f, 0.90f);
            toastRt.anchorMax = new Vector2(0.5f, 0.90f);
            toastRt.pivot = new Vector2(0.5f, 0.5f);
            toastRt.anchoredPosition = Vector2.zero;
            toastRt.sizeDelta = new Vector2(460f, 54f);
            Image toastImg = toastGo.GetComponent<Image>();
            toastImg.sprite = panelBg;
            toastImg.type = Image.Type.Sliced;
            toastImg.color = new Color(0.043f, 0.141f, 0.098f, 0.97f);
            toastImg.raycastTarget = false;
            Outline toastOut = toastGo.GetComponent<Outline>();
            toastOut.effectColor = new Color(0.427f, 0.745f, 0.427f, 1f);
            toastOut.effectDistance = new Vector2(2f, -2f);
            CanvasGroup toastCg = toastGo.GetComponent<CanvasGroup>();
            toastCg.alpha = 0f;
            toastCg.blocksRaycasts = false;
            toastCg.interactable = false;

            GameObject toastTxtGo = new GameObject("ToastText", typeof(RectTransform), typeof(TextMeshProUGUI));
            toastTxtGo.transform.SetParent(toastGo.transform, false);
            RectTransform toastTxtRt = toastTxtGo.GetComponent<RectTransform>();
            toastTxtRt.anchorMin = Vector2.zero;
            toastTxtRt.anchorMax = Vector2.one;
            toastTxtRt.offsetMin = new Vector2(16f, 4f);
            toastTxtRt.offsetMax = new Vector2(-16f, -4f);
            TextMeshProUGUI toastTmp = toastTxtGo.GetComponent<TextMeshProUGUI>();
            toastTmp.font = nunito;
            toastTmp.fontSize = 22f;
            toastTmp.fontStyle = FontStyles.Bold;
            toastTmp.alignment = TextAlignmentOptions.Center;
            toastTmp.color = Color.white;
            toastTmp.raycastTarget = false;

            // Panel starts hidden in prefab so it never flashes open during tutorial
            panelGo.SetActive(false);

            // Wire up UI_Inventory serialized fields
            SetPrivateField(uiInv, "backdropGroup", bdCg);
            SetPrivateField(uiInv, "panelRect", pRt);
            SetPrivateField(uiInv, "panelGroup", pCg);
            SetPrivateField(uiInv, "closeButton", closeBtn);
            SetPrivateField(uiInv, "titleText", titleTmp);
            SetPrivateField(uiInv, "gridViewportRect", vpRt);
            SetPrivateField(uiInv, "gridContentRect", contentRt);
            SetPrivateField(uiInv, "scrollRect", sr);
            SetPrivateField(uiInv, "verticalScrollbar", sb);
            SetPrivateField(uiInv, "emptyInventoryMessageGo", emptyMsgGo);
            SetPrivateField(uiInv, "emptyInventoryText", emTmp);
            SetPrivateField(uiInv, "previewBoxRect", prevRt);
            SetPrivateField(uiInv, "previewIcon", pIconImg);
            SetPrivateField(uiInv, "emptyPreviewGo", emptyPrevGo);
            SetPrivateField(uiInv, "emptyPreviewText", epTmp);
            SetPrivateField(uiInv, "emptyPreviewBackpackIcon", epIconImg);
            SetPrivateField(uiInv, "previewFlashGroup", flashCg);
            SetPrivateField(uiInv, "detailsBoxRect", detRt);
            SetPrivateField(uiInv, "detailNameText", dnTmp);
            SetPrivateField(uiInv, "detailEffectText", deTmp);
            SetPrivateField(uiInv, "detailOwnedText", doTmp);
            SetPrivateField(uiInv, "detailDescText", ddTmp);
            SetPrivateField(uiInv, "useButton", useBtn);
            SetPrivateField(uiInv, "useButtonBg", useImg);
            SetPrivateField(uiInv, "useButtonText", utTmp);
            SetPrivateField(uiInv, "useButtonRect", useRt);
            SetPrivateField(uiInv, "hintStripRect", null);
            SetPrivateField(uiInv, "hintText", null);
            SetPrivateField(uiInv, "toastGroup", toastCg);
            SetPrivateField(uiInv, "toastText", toastTmp);
            SetPrivateField(uiInv, "slotNormalSprite", slotNorm);
            SetPrivateField(uiInv, "slotHoverSprite", slotHov);
            SetPrivateField(uiInv, "slotSelectedSprite", slotSel);
            SetPrivateField(uiInv, "btnNormalSprite", btnNorm);
            SetPrivateField(uiInv, "btnHoverSprite", btnHov);
            SetPrivateField(uiInv, "btnDisabledSprite", btnDis);

            // Wire up InventoryController serialized fields
            SetPrivateField(ctrl, "itemDatabase", db);
            SetPrivateField(ctrl, "slotCapacity", 24);
            SetPrivateField(ctrl, "inventoryUI", uiInv);
            SetPrivateField(ctrl, "keepVitalsVisible", true);
            SetPrivateField(ctrl, "blockUseWhenFull", true);
            SetPrivateField(ctrl, "giveTestItemsOnStart", false);

            string uiPrefabPath = $"{PrefabFolder}/UI_Inventory.prefab";
            PrefabUtility.SaveAsPrefabAsset(rootGo, uiPrefabPath);
            GameObject.DestroyImmediate(rootGo);
            Debug.Log($"[InventoryPrefabBuilder] Created {uiPrefabPath}");

            // =========================================================================
            // 2. BUILD UI_BagButton.prefab
            // =========================================================================
            GameObject rootBagGo = new GameObject("UI_BagButton", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            RectTransform rootBagRt = rootBagGo.GetComponent<RectTransform>();
            rootBagRt.anchorMin = Vector2.zero;
            rootBagRt.anchorMax = Vector2.one;
            rootBagRt.offsetMin = Vector2.zero;
            rootBagRt.offsetMax = Vector2.zero;

            Canvas bCanvas = rootBagGo.GetComponent<Canvas>();
            bCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            bCanvas.sortingOrder = 20;

            CanvasScaler bScaler = rootBagGo.GetComponent<CanvasScaler>();
            bScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            bScaler.referenceResolution = new Vector2(1920f, 1080f);
            bScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            bScaler.matchWidthOrHeight = 0.5f;

            // Interactive Bag Button (Anchored bottom-right: 96x96 at -32, 32)
            GameObject bagBtnGo = new GameObject("BagButton", typeof(RectTransform), typeof(CanvasGroup), typeof(UI_BagButton));
            bagBtnGo.transform.SetParent(rootBagGo.transform, false);
            RectTransform bRt = bagBtnGo.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(1f, 0f);
            bRt.anchorMax = new Vector2(1f, 0f);
            bRt.pivot = new Vector2(1f, 0f);
            bRt.anchoredPosition = new Vector2(-32f, 32f);
            bRt.sizeDelta = new Vector2(96f, 96f);

            CanvasGroup bCg = bagBtnGo.GetComponent<CanvasGroup>();
            UI_BagButton bagBtn = bagBtnGo.GetComponent<UI_BagButton>();

            // Plate Image
            GameObject bPlateGo = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            bPlateGo.transform.SetParent(bagBtnGo.transform, false);
            RectTransform bPlateRt = bPlateGo.GetComponent<RectTransform>();
            bPlateRt.anchorMin = Vector2.zero;
            bPlateRt.anchorMax = Vector2.one;
            bPlateRt.offsetMin = Vector2.zero;
            bPlateRt.offsetMax = Vector2.zero;
            Image bPlateImg = bPlateGo.GetComponent<Image>();
            bPlateImg.sprite = panelBg;
            bPlateImg.type = Image.Type.Sliced;
            bPlateImg.color = Color.white;

            // Backpack Artwork Icon
            GameObject bIconGo = new GameObject("BackpackIcon", typeof(RectTransform), typeof(Image));
            bIconGo.transform.SetParent(bagBtnGo.transform, false);
            RectTransform bIconRt = bIconGo.GetComponent<RectTransform>();
            bIconRt.anchorMin = new Vector2(0.5f, 0.5f);
            bIconRt.anchorMax = new Vector2(0.5f, 0.5f);
            bIconRt.pivot = new Vector2(0.5f, 0.5f);
            bIconRt.sizeDelta = new Vector2(72f, 72f);
            Image bIconImg = bIconGo.GetComponent<Image>();
            bIconImg.sprite = backpackSprite;
            bIconImg.preserveAspect = true;

            // Keycap pill "[B]"
            GameObject bKeycapGo = new GameObject("KeycapPill", typeof(RectTransform), typeof(Image));
            bKeycapGo.transform.SetParent(bagBtnGo.transform, false);
            RectTransform bKeycapRt = bKeycapGo.GetComponent<RectTransform>();
            bKeycapRt.anchorMin = new Vector2(0f, 0f);
            bKeycapRt.anchorMax = new Vector2(0f, 0f);
            bKeycapRt.pivot = new Vector2(0f, 0f);
            bKeycapRt.anchoredPosition = new Vector2(6f, 6f);
            bKeycapRt.sizeDelta = new Vector2(24f, 24f);
            Image bKeycapImg = bKeycapGo.GetComponent<Image>();
            bKeycapImg.sprite = keycapPlate;
            bKeycapImg.type = Image.Type.Sliced;

            GameObject bKeycapTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            bKeycapTextGo.transform.SetParent(bKeycapGo.transform, false);
            RectTransform bktRt = bKeycapTextGo.GetComponent<RectTransform>();
            bktRt.anchorMin = Vector2.zero;
            bktRt.anchorMax = Vector2.one;
            bktRt.offsetMin = Vector2.zero;
            bktRt.offsetMax = Vector2.zero;
            TextMeshProUGUI bktTmp = bKeycapTextGo.GetComponent<TextMeshProUGUI>();
            bktTmp.font = fredoka;
            bktTmp.text = "B";
            bktTmp.fontSize = 15f;
            bktTmp.fontStyle = FontStyles.Bold;
            bktTmp.alignment = TextAlignmentOptions.Center;
            bktTmp.color = new Color(0.11f, 0.11f, 0.11f, 1f);

            // Unread Notification Badge Dot
            GameObject bDotGo = new GameObject("UnreadBadgeDot", typeof(RectTransform), typeof(Image));
            bDotGo.transform.SetParent(bagBtnGo.transform, false);
            RectTransform bDotRt = bDotGo.GetComponent<RectTransform>();
            bDotRt.anchorMin = new Vector2(1f, 1f);
            bDotRt.anchorMax = new Vector2(1f, 1f);
            bDotRt.pivot = new Vector2(1f, 1f);
            bDotRt.anchoredPosition = new Vector2(-4f, -4f);
            bDotRt.sizeDelta = new Vector2(18f, 18f);
            Image bDotImg = bDotGo.GetComponent<Image>();
            bDotImg.sprite = dotSprite;
            bDotImg.color = Color.white;
            bDotImg.enabled = false;

            // Wire up UI_BagButton serialized fields
            SetPrivateField(bagBtn, "rectTransform", bRt);
            SetPrivateField(bagBtn, "canvasGroup", bCg);
            SetPrivateField(bagBtn, "plateImage", bPlateImg);
            SetPrivateField(bagBtn, "backpackIcon", bIconImg);
            SetPrivateField(bagBtn, "unreadDotImage", bDotImg);
            SetPrivateField(bagBtn, "keycapGo", bKeycapGo);
            SetPrivateField(bagBtn, "keycapText", bktTmp);

            string bagPrefabPath = $"{PrefabFolder}/UI_BagButton.prefab";
            PrefabUtility.SaveAsPrefabAsset(rootBagGo, bagPrefabPath);
            GameObject.DestroyImmediate(rootBagGo);
            Debug.Log($"[InventoryPrefabBuilder] Created {bagPrefabPath}");

            BuildEcoShopPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/EcoQuest/Inventory/Build UI_EcoShop Prefab", false, 16)]
        public static void BuildEcoShopPrefab()
        {
            Directory.CreateDirectory(PrefabFolder);
            ConfigureTextureImporters();
            TMP_FontAsset fredoka = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Fredoka SDF.asset");
            TMP_FontAsset nunito = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Nunito SDF.asset");

            Sprite panelBg = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/eco_shop_panel_bg.png");
            if (panelBg == null) panelBg = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/panel_bg.png");

            Sprite wellBg = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/eco_shop_well_bg.png");
            if (wellBg == null) wellBg = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/details_box_bg.png");

            Sprite cardBg = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/eco_shop_card_bg.png");
            Sprite cardHover = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/eco_shop_card_hover.png");
            Sprite btnBuyAvail = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/btn_buy_available.png");
            Sprite btnBuyDis = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/btn_buy_disabled.png");
            Sprite closeX = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/close_x_white.png");
            if (closeX == null) closeX = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/close_x_icon.png");

            Sprite padlockSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/icon_padlock_mossy.png");
            if (padlockSprite == null) padlockSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesFolder}/icon_padlock.png");

            Sprite coinSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/icon_shop_coin.png");
            Sprite bannerSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/eco_shop_banner_clean.png");
            if (bannerSprite == null) bannerSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/eco_shop_header_banner_v2.png");

            Sprite sbTrack = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/scrollbar_track_recessed.png");
            Sprite sbHandle = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/scrollbar_handle_glow.png");
            Sprite vignetteSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/ui_vignette_gradient.png");
            Sprite coinPillBg = AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpritesFolder}/coin_counter_pill_bg.png");

            InventoryItemDatabase db = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>("Assets/EcoQuest/Inventory/Data/InventoryItemDatabase.asset");

            BuildEcoShopPrefabInternal(
                fredoka, nunito,
                panelBg, wellBg, cardBg, cardHover,
                btnBuyAvail, btnBuyDis, closeX,
                padlockSprite, coinSprite, bannerSprite,
                sbTrack, sbHandle, vignetteSprite, coinPillBg,
                db);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void BuildEcoShopPrefabInternal(
            TMP_FontAsset fredoka, TMP_FontAsset nunito,
            Sprite panelBg, Sprite wellBg, Sprite cardBg, Sprite cardHover,
            Sprite btnBuyAvail, Sprite btnBuyDis, Sprite closeX,
            Sprite padlockSprite, Sprite coinSprite, Sprite bannerSprite,
            Sprite sbTrack, Sprite sbHandle, Sprite vignetteSprite, Sprite coinPillBg,
            InventoryItemDatabase db)
        {
            GameObject rootGo = new GameObject("UI_EcoShop",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(UI_EcoShop), typeof(AudioSource));
            RectTransform rootRt = rootGo.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            Canvas canvas = rootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 92;

            CanvasScaler scaler = rootGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            UI_EcoShop ecoShop = rootGo.GetComponent<UI_EcoShop>();

            // 1. Backdrop + Vignette
            GameObject backdropGo = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            backdropGo.transform.SetParent(rootGo.transform, false);
            RectTransform bdRt = backdropGo.GetComponent<RectTransform>();
            bdRt.anchorMin = Vector2.zero;
            bdRt.anchorMax = Vector2.one;
            bdRt.offsetMin = Vector2.zero;
            bdRt.offsetMax = Vector2.zero;
            Image bdImg = backdropGo.GetComponent<Image>();
            if (vignetteSprite != null)
            {
                bdImg.sprite = vignetteSprite;
                bdImg.type = Image.Type.Simple;
                bdImg.color = new Color(0.01f, 0.05f, 0.03f, 0.60f);
            }
            else
            {
                bdImg.color = new Color(0.02f, 0.06f, 0.04f, 0.28f);
            }
            bdImg.raycastTarget = false;
            CanvasGroup bdCg = backdropGo.GetComponent<CanvasGroup>();
            bdCg.alpha = 0f;
            bdCg.blocksRaycasts = false;
            bdCg.interactable = false;
            backdropGo.SetActive(false);

            // 2. Main Shop Panel (Double border rounded rect with soft outer glow)
            GameObject panelGo = new GameObject("ShopPanel",
                typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panelGo.transform.SetParent(rootGo.transform, false);
            RectTransform pRt = panelGo.GetComponent<RectTransform>();
            pRt.anchorMin = new Vector2(0.5f, 0.5f);
            pRt.anchorMax = new Vector2(0.5f, 0.5f);
            pRt.pivot = new Vector2(0.5f, 0.5f);
            pRt.anchoredPosition = new Vector2(0f, 0f);
            pRt.sizeDelta = new Vector2(1238f, 842f);

            Image pImg = panelGo.GetComponent<Image>();
            pImg.sprite = panelBg;
            pImg.type = Image.Type.Sliced;
            pImg.color = Color.white;
            pImg.raycastTarget = true;

            CanvasGroup pCg = panelGo.GetComponent<CanvasGroup>();
            pCg.alpha = 0f;
            pCg.interactable = false;
            pCg.blocksRaycasts = false;

            // 3. Header Banner (Clean rounded pill with crisp white text, zero leaves/vines)
            GameObject bannerGo = new GameObject("HeaderBanner", typeof(RectTransform), typeof(Image));
            bannerGo.transform.SetParent(panelGo.transform, false);
            RectTransform bnRt = bannerGo.GetComponent<RectTransform>();
            bnRt.anchorMin = new Vector2(0.5f, 1f);
            bnRt.anchorMax = new Vector2(0.5f, 1f);
            bnRt.pivot = new Vector2(0.5f, 1f);
            bnRt.anchoredPosition = new Vector2(0f, 2f);
            bnRt.sizeDelta = new Vector2(553f, 129f);
            Image bnImg = bannerGo.GetComponent<Image>();
            bnImg.sprite = bannerSprite;
            bnImg.type = Image.Type.Sliced;
            bnImg.preserveAspect = false;
            bnImg.raycastTarget = false;

            // Plaque Title Text
            GameObject titleGo = new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(bannerGo.transform, false);
            RectTransform tRt = titleGo.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = Vector2.zero;
            TextMeshProUGUI titleTmp = titleGo.GetComponent<TextMeshProUGUI>();
            titleTmp.font = fredoka;
            titleTmp.text = "ECO SHOP";
            titleTmp.fontSize = 52f;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.color = Color.white;
            titleTmp.characterSpacing = 2f;
            titleTmp.enabled = true;
            titleTmp.raycastTarget = false;
            titleGo.SetActive(true);

            // 4. Crisp White Close Button (X)
            GameObject closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(Button), typeof(Image));
            closeGo.transform.SetParent(panelGo.transform, false);
            RectTransform closeRt = closeGo.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(0.5f, 0.5f);
            closeRt.anchoredPosition = new Vector2(-67f, -59f);
            closeRt.sizeDelta = new Vector2(38f, 38f);
            Image closeImg = closeGo.GetComponent<Image>();
            closeImg.sprite = closeX;
            closeImg.preserveAspect = true;
            closeImg.color = Color.white;
            closeImg.raycastTarget = true;
            Button closeBtn = closeGo.GetComponent<Button>();

            // 5. Recessed Content Well
            GameObject wellGo = new GameObject("ContentWell", typeof(RectTransform), typeof(Image));
            wellGo.transform.SetParent(panelGo.transform, false);
            RectTransform wellRt = wellGo.GetComponent<RectTransform>();
            wellRt.anchorMin = new Vector2(0.5f, 0.5f);
            wellRt.anchorMax = new Vector2(0.5f, 0.5f);
            wellRt.pivot = new Vector2(0.5f, 0.5f);
            wellRt.anchoredPosition = new Vector2(0f, -81f);
            wellRt.sizeDelta = new Vector2(1162f, 625f);
            Image wellImg = wellGo.GetComponent<Image>();
            wellImg.sprite = wellBg;
            wellImg.type = Image.Type.Sliced;
            wellImg.color = Color.white;
            wellImg.raycastTarget = false;

            // 6. ScrollRect Container inside Content Well (Viewport height = 618px)
            GameObject scrollGo = new GameObject("ScrollRect", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(wellGo.transform, false);
            RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(16f, 8f);
            scrollRt.offsetMax = new Vector2(-48f, -6f);

            ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = true;
            sr.decelerationRate = 0.135f;
            sr.scrollSensitivity = 30f;

            // Viewport with RectMask2D
            GameObject vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            vpGo.transform.SetParent(scrollGo.transform, false);
            RectTransform vpRt = vpGo.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = Vector2.zero;
            Image vpImg = vpGo.GetComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.005f);
            vpImg.raycastTarget = true;
            sr.viewport = vpRt;

            // Content (4 columns, exactly 2 full rows visible at rest with 24px top and 45px bottom framing)
            GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(vpGo.transform, false);
            RectTransform contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            GridLayoutGroup glg = contentGo.GetComponent<GridLayoutGroup>();
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 4;
            glg.padding = new RectOffset(31, 35, 24, 45);
            glg.spacing = new Vector2(31f, 57f);
            glg.cellSize = new Vector2(246f, 246f);
            glg.childAlignment = TextAnchor.UpperLeft;

            ContentSizeFitter contentCsf = contentGo.GetComponent<ContentSizeFitter>();
            contentCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = contentRt;

            // 7. Draggable Glowing Scrollbar on the right edge of Content Well
            GameObject sbGo = new GameObject("VerticalScrollbar", typeof(RectTransform), typeof(Scrollbar), typeof(Image));
            sbGo.transform.SetParent(wellGo.transform, false);
            RectTransform sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(1f, 0f);
            sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot = new Vector2(1f, 0.5f);
            sbRt.anchoredPosition = new Vector2(-16f, 0f);
            sbRt.sizeDelta = new Vector2(25f, -32f);

            Image sbImg = sbGo.GetComponent<Image>();
            sbImg.sprite = sbTrack;
            sbImg.type = Image.Type.Sliced;
            sbImg.color = Color.white;

            Scrollbar sb = sbGo.GetComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            GameObject slidingAreaGo = new GameObject("Sliding Area", typeof(RectTransform));
            slidingAreaGo.transform.SetParent(sbGo.transform, false);
            RectTransform saRt = slidingAreaGo.GetComponent<RectTransform>();
            saRt.anchorMin = Vector2.zero;
            saRt.anchorMax = Vector2.one;
            saRt.offsetMin = new Vector2(0f, 14f);
            saRt.offsetMax = new Vector2(0f, -14f);

            GameObject handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(slidingAreaGo.transform, false);
            RectTransform handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = Vector2.zero;
            handleRt.offsetMax = Vector2.zero;
            Image handleImg = handleGo.GetComponent<Image>();
            handleImg.sprite = sbHandle;
            handleImg.type = Image.Type.Sliced;
            handleImg.color = Color.white;

            sb.targetGraphic = handleImg;
            sb.handleRect = handleRt;
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            // 8. 16 Pooled Card Slots
            List<UI_EcoShopSlot> slotList = new List<UI_EcoShopSlot>();
            for (int i = 0; i < 16; i++)
            {
                var cardGo = new GameObject($"Card_{i:00}", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(UI_EcoShopSlot));
                cardGo.transform.SetParent(contentGo.transform, false);
                var crdRt = cardGo.GetComponent<RectTransform>();
                crdRt.sizeDelta = new Vector2(246f, 246f);

                var crdImg = cardGo.GetComponent<Image>();
                crdImg.sprite = cardBg;
                crdImg.type = Image.Type.Sliced;
                crdImg.color = Color.white;

                var crdOutl = cardGo.GetComponent<Outline>();
                crdOutl.effectColor = new Color(0.34f, 0.41f, 0.38f, 0.60f);
                crdOutl.effectDistance = new Vector2(1.5f, -1.5f);

                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(cardGo.transform, false);
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.anchorMin = new Vector2(0.5f, 0.5f);
                iconRt.anchorMax = new Vector2(0.5f, 0.5f);
                iconRt.pivot = new Vector2(0.5f, 0.5f);
                iconRt.anchoredPosition = new Vector2(0f, 14f);
                iconRt.sizeDelta = new Vector2(118f, 120f);
                var iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = padlockSprite;
                iconImg.preserveAspect = true;

                var priceRowGo = new GameObject("PriceRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                priceRowGo.transform.SetParent(cardGo.transform, false);
                var prRt = priceRowGo.GetComponent<RectTransform>();
                prRt.anchorMin = new Vector2(0.5f, 0.5f);
                prRt.anchorMax = new Vector2(0.5f, 0.5f);
                prRt.pivot = new Vector2(0.5f, 0.5f);
                prRt.anchoredPosition = new Vector2(0f, -66f);
                prRt.sizeDelta = new Vector2(130f, 28f);

                var prHlg = priceRowGo.GetComponent<HorizontalLayoutGroup>();
                prHlg.spacing = 8f;
                prHlg.childAlignment = TextAnchor.MiddleCenter;
                prHlg.childControlWidth = false;
                prHlg.childControlHeight = false;

                var cGo = new GameObject("CoinIcon", typeof(RectTransform), typeof(Image));
                cGo.transform.SetParent(priceRowGo.transform, false);
                var cRt = cGo.GetComponent<RectTransform>();
                cRt.sizeDelta = new Vector2(28f, 28f);
                var cImg = cGo.GetComponent<Image>();
                cImg.sprite = coinSprite;
                cImg.preserveAspect = true;

                var pTxtGo = new GameObject("PriceText", typeof(RectTransform), typeof(TextMeshProUGUI));
                pTxtGo.transform.SetParent(priceRowGo.transform, false);
                var ptRt = pTxtGo.GetComponent<RectTransform>();
                ptRt.sizeDelta = new Vector2(80f, 28f);
                var ptTmp = pTxtGo.GetComponent<TextMeshProUGUI>();
                ptTmp.font = fredoka;
                ptTmp.fontSize = 22f;
                ptTmp.fontStyle = FontStyles.Bold;
                ptTmp.alignment = TextAlignmentOptions.MidlineLeft;
                ptTmp.color = Color.white;

                var buyGo = new GameObject("BuyButton", typeof(RectTransform), typeof(Button), typeof(Image), typeof(Outline));
                buyGo.transform.SetParent(cardGo.transform, false);
                var buyRt = buyGo.GetComponent<RectTransform>();
                buyRt.anchorMin = new Vector2(0.5f, 0.5f);
                buyRt.anchorMax = new Vector2(0.5f, 0.5f);
                buyRt.pivot = new Vector2(0.5f, 0.5f);
                buyRt.anchoredPosition = new Vector2(0f, -118f);
                buyRt.sizeDelta = new Vector2(133f, 49f);

                var buyImg = buyGo.GetComponent<Image>();
                buyImg.sprite = btnBuyDis;
                buyImg.type = Image.Type.Sliced;
                buyImg.color = Color.white;

                var buyOutl = buyGo.GetComponent<Outline>();
                buyOutl.enabled = false;
                buyOutl.effectColor = new Color(0.47f, 0.86f, 0.56f, 0.90f);
                buyOutl.effectDistance = new Vector2(1.5f, -1.5f);

                var buyBtn = buyGo.GetComponent<Button>();
                buyBtn.targetGraphic = buyImg;

                var bTxtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                bTxtGo.transform.SetParent(buyGo.transform, false);
                var btRt = bTxtGo.GetComponent<RectTransform>();
                btRt.anchorMin = Vector2.zero;
                btRt.anchorMax = Vector2.one;
                btRt.offsetMin = Vector2.zero;
                btRt.offsetMax = Vector2.zero;
                var btTmp = bTxtGo.GetComponent<TextMeshProUGUI>();
                btTmp.font = fredoka;
                btTmp.text = "BUY";
                btTmp.fontSize = 20f;
                btTmp.characterSpacing = 3f;
                btTmp.fontStyle = FontStyles.Bold;
                btTmp.alignment = TextAlignmentOptions.Center;
                btTmp.color = new Color(0.56f, 0.64f, 0.61f, 1f);

                var slotComp = cardGo.GetComponent<UI_EcoShopSlot>();
                slotComp.SetupReferences(
                    crdImg, iconImg, cImg, ptTmp,
                    buyBtn, buyImg, btTmp, buyOutl,
                    crdOutl, cardBg, cardHover, btnBuyAvail, btnBuyDis);
                slotList.Add(slotComp);
            }

            // 9. Bottom-Left Coin Counter Capsule Pill
            GameObject pillGo = new GameObject("CoinCounterPill", typeof(RectTransform), typeof(Image));
            pillGo.transform.SetParent(rootGo.transform, false);
            RectTransform pillRt = pillGo.GetComponent<RectTransform>();
            pillRt.anchorMin = new Vector2(0f, 0f);
            pillRt.anchorMax = new Vector2(0f, 0f);
            pillRt.pivot = new Vector2(0f, 0f);
            pillRt.anchoredPosition = new Vector2(40f, 36f);
            pillRt.sizeDelta = new Vector2(225f, 64f);

            Image pillImg = pillGo.GetComponent<Image>();
            pillImg.sprite = coinPillBg;
            pillImg.type = Image.Type.Sliced;
            pillImg.color = Color.white;
            pillImg.raycastTarget = false;

            GameObject pillIconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            pillIconGo.transform.SetParent(pillGo.transform, false);
            RectTransform pillIconRt = pillIconGo.GetComponent<RectTransform>();
            pillIconRt.anchorMin = new Vector2(0f, 0.5f);
            pillIconRt.anchorMax = new Vector2(0f, 0.5f);
            pillIconRt.pivot = new Vector2(0f, 0.5f);
            pillIconRt.anchoredPosition = new Vector2(14f, 0f);
            pillIconRt.sizeDelta = new Vector2(42f, 42f);
            Image pillIconImg = pillIconGo.GetComponent<Image>();
            pillIconImg.sprite = coinSprite;
            pillIconImg.preserveAspect = true;
            pillIconImg.raycastTarget = false;

            GameObject pillTxtGo = new GameObject("AmountText", typeof(RectTransform), typeof(TextMeshProUGUI));
            pillTxtGo.transform.SetParent(pillGo.transform, false);
            RectTransform pillTxtRt = pillTxtGo.GetComponent<RectTransform>();
            pillTxtRt.anchorMin = new Vector2(0f, 0f);
            pillTxtRt.anchorMax = new Vector2(1f, 1f);
            pillTxtRt.pivot = new Vector2(0f, 0.5f);
            pillTxtRt.offsetMin = new Vector2(64f, 0f);
            pillTxtRt.offsetMax = new Vector2(-14f, 0f);
            TextMeshProUGUI pillTxtTmp = pillTxtGo.GetComponent<TextMeshProUGUI>();
            pillTxtTmp.font = fredoka;
            pillTxtTmp.text = "945";
            pillTxtTmp.fontSize = 32f;
            pillTxtTmp.fontStyle = FontStyles.Bold;
            pillTxtTmp.alignment = TextAlignmentOptions.MidlineLeft;
            pillTxtTmp.color = Color.white;
            pillTxtTmp.raycastTarget = false;

            // 10. Themed Toast Banner (centered at bottom of panel so it never overlaps ECO SHOP plaque)
            var toastGo = new GameObject("ShopToast", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline));
            toastGo.transform.SetParent(panelGo.transform, false);
            var toastRt = toastGo.GetComponent<RectTransform>();
            toastRt.anchorMin = new Vector2(0.5f, 0.5f);
            toastRt.anchorMax = new Vector2(0.5f, 0.5f);
            toastRt.pivot = new Vector2(0.5f, 0.5f);
            toastRt.anchoredPosition = new Vector2(0f, -270f);
            toastRt.sizeDelta = new Vector2(440f, 50f);

            var toastImg = toastGo.GetComponent<Image>();
            toastImg.sprite = panelBg;
            toastImg.type = Image.Type.Sliced;
            toastImg.color = new Color(0.043f, 0.141f, 0.098f, 0.97f);

            var toastOut = toastGo.GetComponent<Outline>();
            toastOut.effectColor = new Color(0.427f, 0.745f, 0.427f, 1f);
            toastOut.effectDistance = new Vector2(2f, -2f);

            var toastCg = toastGo.GetComponent<CanvasGroup>();
            toastCg.alpha = 0f;
            toastCg.blocksRaycasts = false;
            toastCg.interactable = false;

            var toastTxtGo = new GameObject("ToastText", typeof(RectTransform), typeof(TextMeshProUGUI));
            toastTxtGo.transform.SetParent(toastGo.transform, false);
            var ttRt = toastTxtGo.GetComponent<RectTransform>();
            ttRt.anchorMin = Vector2.zero;
            ttRt.anchorMax = Vector2.one;
            ttRt.offsetMin = new Vector2(16f, 4f);
            ttRt.offsetMax = new Vector2(-16f, -4f);
            var ttTmp = toastTxtGo.GetComponent<TextMeshProUGUI>();
            ttTmp.font = nunito;
            ttTmp.fontSize = 21f;
            ttTmp.fontStyle = FontStyles.Bold;
            ttTmp.alignment = TextAlignmentOptions.Center;
            ttTmp.color = Color.white;

            panelGo.SetActive(false);

            ecoShop.SetupReferences(
                bdCg, bdImg, pRt, pCg, closeBtn,
                bnRt, bnImg, titleTmp,
                wellRt, sr, contentRt, sb, handleImg,
                slotList, pillRt, pillIconImg, pillTxtTmp,
                padlockSprite, coinSprite);

            SetPrivateField(ecoShop, "itemDatabase", db);
            SetPrivateField(ecoShop, "toastGroup", toastCg);
            SetPrivateField(ecoShop, "toastText", ttTmp);

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                SetLayerRecursively(rootGo, uiLayer);
            }

            string shopPrefabPath = $"{PrefabFolder}/UI_EcoShop.prefab";
            PrefabUtility.SaveAsPrefabAsset(rootGo, shopPrefabPath);
            GameObject.DestroyImmediate(rootGo);
            Debug.Log($"[InventoryPrefabBuilder] Created {shopPrefabPath}");
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static HintRow CreateSharedHintRow(Transform parent, string name, string keyLabel, string actionText,
            TMP_FontAsset titleFont, TMP_FontAsset bodyFont, Sprite keyFaceSprite, Sprite keyBaseSprite)
        {
            GameObject rowGo = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter), typeof(HintRow));
            rowGo.transform.SetParent(parent, false);

            GameObject keyViewGo = new GameObject("KeyCapView", typeof(RectTransform), typeof(KeyCapView), typeof(LayoutElement));
            keyViewGo.transform.SetParent(rowGo.transform, false);
            RectTransform kvRt = keyViewGo.GetComponent<RectTransform>();
            kvRt.sizeDelta = new Vector2(44f, 36f);
            LayoutElement kvLe = keyViewGo.GetComponent<LayoutElement>();
            float minW = keyLabel.Length > 2 ? 78f : 38f;
            kvLe.minWidth = minW;
            kvLe.preferredWidth = minW;
            kvLe.minHeight = 36f;
            kvLe.preferredHeight = 36f;

            // Base layer
            GameObject baseGo = new GameObject("BaseLayer", typeof(RectTransform), typeof(Image));
            baseGo.transform.SetParent(keyViewGo.transform, false);
            RectTransform baseRt = baseGo.GetComponent<RectTransform>();
            baseRt.anchorMin = Vector2.zero;
            baseRt.anchorMax = Vector2.one;
            baseRt.offsetMin = new Vector2(0f, -3f);
            baseRt.offsetMax = new Vector2(0f, -3f);
            Image baseImg = baseGo.GetComponent<Image>();
            baseImg.sprite = keyBaseSprite != null ? keyBaseSprite : keyFaceSprite;
            baseImg.type = Image.Type.Sliced;
            baseImg.color = new Color(0.70f, 0.72f, 0.70f, 1f);

            // Top face
            GameObject topGo = new GameObject("TopFace", typeof(RectTransform), typeof(Image));
            topGo.transform.SetParent(keyViewGo.transform, false);
            RectTransform topRt = topGo.GetComponent<RectTransform>();
            topRt.anchorMin = Vector2.zero;
            topRt.anchorMax = Vector2.one;
            topRt.offsetMin = Vector2.zero;
            topRt.offsetMax = Vector2.zero;
            Image topImg = topGo.GetComponent<Image>();
            topImg.sprite = keyFaceSprite;
            topImg.type = Image.Type.Sliced;
            topImg.color = new Color(0.95f, 0.96f, 0.95f, 1f);

            // Key Text
            GameObject kTxtGo = new GameObject("KeyText", typeof(RectTransform), typeof(TextMeshProUGUI));
            kTxtGo.transform.SetParent(topGo.transform, false);
            RectTransform kTxtRt = kTxtGo.GetComponent<RectTransform>();
            kTxtRt.anchorMin = Vector2.zero;
            kTxtRt.anchorMax = Vector2.one;
            kTxtRt.offsetMin = new Vector2(4f, 2f);
            kTxtRt.offsetMax = new Vector2(-4f, -2f);
            TextMeshProUGUI kTmp = kTxtGo.GetComponent<TextMeshProUGUI>();
            kTmp.font = titleFont;
            kTmp.text = keyLabel;
            kTmp.fontSize = 18f;
            kTmp.fontStyle = FontStyles.Bold;
            kTmp.alignment = TextAlignmentOptions.Center;
            kTmp.color = new Color(0.106f, 0.106f, 0.106f, 1f);

            KeyCapView kcv = keyViewGo.GetComponent<KeyCapView>();
            SetPrivateField(kcv, "containerRect", kvRt);
            SetPrivateField(kcv, "baseLayerRect", baseRt);
            SetPrivateField(kcv, "topFaceRect", topRt);
            SetPrivateField(kcv, "baseLayerImage", baseImg);
            SetPrivateField(kcv, "topFaceImage", topImg);
            SetPrivateField(kcv, "keycapText", kTmp);
            SetPrivateField(kcv, "baseHeight", 36f);
            SetPrivateField(kcv, "minWidth", minW);

            // Action Label
            GameObject actGo = new GameObject("ActionLabel", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            actGo.transform.SetParent(rowGo.transform, false);
            LayoutElement actLe = actGo.GetComponent<LayoutElement>();
            actLe.minHeight = 36f;
            actLe.preferredHeight = 36f;
            TextMeshProUGUI actTmp = actGo.GetComponent<TextMeshProUGUI>();
            actTmp.font = bodyFont;
            actTmp.text = actionText;
            actTmp.fontSize = 20f;
            actTmp.fontStyle = FontStyles.Bold;
            actTmp.alignment = TextAlignmentOptions.MidlineLeft;
            actTmp.color = new Color(0.89f, 0.96f, 0.89f, 1f);

            HintRow hr = rowGo.GetComponent<HintRow>();
            SetPrivateField(hr, "keyCapView", kcv);
            SetPrivateField(hr, "actionLabel", actTmp);
            SetPrivateField(hr, "layoutGroup", rowGo.GetComponent<HorizontalLayoutGroup>());
            SetPrivateField(hr, "sizeFitter", rowGo.GetComponent<ContentSizeFitter>());
            hr.EnsureLayout();
            return hr;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            if (target == null) return;
            var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            field?.SetValue(target, value);
        }
    }
}
