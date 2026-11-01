using System;
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

public static class BuildSharedUIFoundationTool
{
    [MenuItem("Tools/EcoQuest/Build Shared UI Foundation")]
    public static void BuildFoundation()
    {
        Debug.Log(">>> Building Shared UI Foundation (Theme, KeyCapView, HintRow, PopupFrame)...");

        // 1. Create or Load UITheme.asset
        string themePath = "Assets/EcoQuest/UI/Shared/UITheme.asset";
        var theme = AssetDatabase.LoadAssetAtPath<UITheme>(themePath);
        if (theme == null)
        {
            theme = ScriptableObject.CreateInstance<UITheme>();
            AssetDatabase.CreateAsset(theme, themePath);
        }

        theme.panelBackgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/panel_rounded_forest_green.png");
        theme.speakerTabGreenSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/pill_tab_green.png");
        theme.speakerTabTealSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/pill_tab_teal.png");
        theme.keycapFaceSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/keycap_3d.png");
        theme.keycapBaseSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/keycap_base.png");
        theme.useLegacyPopups = false;

        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        Debug.Log("[PASS] UITheme asset configured.");

        // 2. Build KeyCapView Prefab
        BuildKeyCapViewPrefab(theme);

        // 3. Build HintRow Prefab
        BuildHintRowPrefab();

        // 4. Build PopupFrame Prefab
        BuildPopupFramePrefab(theme);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("<color=green>[PASS] Shared UI Foundation successfully built!</color>");
    }

    private static void BuildKeyCapViewPrefab(UITheme theme)
    {
        string prefabPath = "Assets/EcoQuest/UI/Shared/KeyCapView.prefab";
        GameObject root = new GameObject("KeyCapView", typeof(RectTransform), typeof(KeyCapView));
        var rootRT = root.GetComponent<RectTransform>();
        rootRT.sizeDelta = new Vector2(48f, 48f);

        // Glow Outline
        GameObject glow = new GameObject("GlowOutline", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        glow.transform.SetParent(root.transform, false);
        var glowRT = glow.GetComponent<RectTransform>();
        glowRT.anchorMin = new Vector2(0.5f, 0.5f);
        glowRT.anchorMax = new Vector2(0.5f, 0.5f);
        glowRT.pivot = new Vector2(0.5f, 0.5f);
        glowRT.sizeDelta = new Vector2(56f, 56f);
        var glowImg = glow.GetComponent<UnityEngine.UI.Image>();
        glowImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/EcoQuest/keycap_3d_outline.png") ?? theme.keycapFaceSprite;
        glowImg.type = UnityEngine.UI.Image.Type.Sliced;
        glowImg.color = new Color(0.43f, 0.75f, 0.43f, 0f);

        // Base Layer (darker bevel 5px below)
        GameObject baseLayer = new GameObject("BaseLayer", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        baseLayer.transform.SetParent(root.transform, false);
        var baseRT = baseLayer.GetComponent<RectTransform>();
        baseRT.anchorMin = new Vector2(0.5f, 0.5f);
        baseRT.anchorMax = new Vector2(0.5f, 0.5f);
        baseRT.pivot = new Vector2(0.5f, 0.5f);
        baseRT.anchoredPosition = new Vector2(0f, -5f);
        baseRT.sizeDelta = new Vector2(48f, 48f);
        var baseImg = baseLayer.GetComponent<UnityEngine.UI.Image>();
        baseImg.sprite = theme.keycapBaseSprite ?? theme.keycapFaceSprite;
        baseImg.type = UnityEngine.UI.Image.Type.Sliced;
        baseImg.color = new Color(0.72f, 0.72f, 0.72f, 1f); // #B8B8B8

        // Top Face
        GameObject topFace = new GameObject("TopFace", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        topFace.transform.SetParent(root.transform, false);
        var topRT = topFace.GetComponent<RectTransform>();
        topRT.anchorMin = new Vector2(0.5f, 0.5f);
        topRT.anchorMax = new Vector2(0.5f, 0.5f);
        topRT.pivot = new Vector2(0.5f, 0.5f);
        topRT.sizeDelta = new Vector2(48f, 48f);
        var topImg = topFace.GetComponent<UnityEngine.UI.Image>();
        topImg.sprite = theme.keycapFaceSprite;
        topImg.type = UnityEngine.UI.Image.Type.Sliced;
        topImg.color = new Color(0.95f, 0.95f, 0.95f, 1f); // #F2F2F2

        // Letter (TextMeshProUGUI)
        GameObject textObj = new GameObject("KeyText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(topFace.transform, false);
        var textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        var tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = "E";
        tmp.fontSize = 26f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = new Color(0.106f, 0.106f, 0.106f, 1f); // #1B1B1B
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;

        // Lock Radial Overlay
        GameObject lockOverlay = new GameObject("LockRadialOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        lockOverlay.transform.SetParent(topFace.transform, false);
        var lockRT = lockOverlay.GetComponent<RectTransform>();
        lockRT.anchorMin = Vector2.zero;
        lockRT.anchorMax = Vector2.one;
        lockRT.offsetMin = Vector2.zero;
        lockRT.offsetMax = Vector2.zero;
        var lockImg = lockOverlay.GetComponent<UnityEngine.UI.Image>();
        lockImg.sprite = theme.keycapFaceSprite;
        lockImg.type = UnityEngine.UI.Image.Type.Filled;
        lockImg.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
        lockImg.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
        lockImg.color = new Color(0f, 0f, 0f, 0.40f);
        lockOverlay.SetActive(false);

        // Serialize fields into KeyCapView
        var view = root.GetComponent<KeyCapView>();
        var so = new SerializedObject(view);
        so.FindProperty("containerRect").objectReferenceValue = rootRT;
        so.FindProperty("baseLayerRect").objectReferenceValue = baseRT;
        so.FindProperty("topFaceRect").objectReferenceValue = topRT;
        so.FindProperty("baseLayerImage").objectReferenceValue = baseImg;
        so.FindProperty("topFaceImage").objectReferenceValue = topImg;
        so.FindProperty("keycapText").objectReferenceValue = tmp;
        so.FindProperty("lockRadialOverlay").objectReferenceValue = lockImg;
        so.FindProperty("glowOutlineImage").objectReferenceValue = glowImg;
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        Debug.Log("[PASS] KeyCapView prefab built.");
    }

    private static void BuildHintRowPrefab()
    {
        string prefabPath = "Assets/EcoQuest/UI/Shared/HintRow.prefab";
        GameObject root = new GameObject("HintRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter), typeof(HintRow));
        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160f, 48f);

        var hlg = root.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.spacing = 14f;
        hlg.padding = new RectOffset(16, 32, 0, 0);

        var csf = root.GetComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Instantiate KeyCapView inside
        var keyCapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/UI/Shared/KeyCapView.prefab");
        GameObject keyCapInstance = (GameObject)PrefabUtility.InstantiatePrefab(keyCapPrefab, root.transform);

        // Action Label
        GameObject labelObj = new GameObject("ActionLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObj.transform.SetParent(root.transform, false);
        var labelRT = labelObj.GetComponent<RectTransform>();
        labelRT.sizeDelta = new Vector2(110f, 48f);
        var labelTmp = labelObj.GetComponent<TextMeshProUGUI>();
        labelTmp.text = "Continue";
        labelTmp.fontSize = 24f;
        labelTmp.color = Color.white;
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        labelTmp.enableAutoSizing = false;

        var hint = root.GetComponent<HintRow>();
        var so = new SerializedObject(hint);
        so.FindProperty("keyCapView").objectReferenceValue = keyCapInstance.GetComponent<KeyCapView>();
        so.FindProperty("actionLabel").objectReferenceValue = labelTmp;
        so.FindProperty("layoutGroup").objectReferenceValue = hlg;
        so.FindProperty("sizeFitter").objectReferenceValue = csf;
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        Debug.Log("[PASS] HintRow prefab built.");
    }

    private static void BuildPopupFramePrefab(UITheme theme)
    {
        string prefabPath = "Assets/EcoQuest/UI/Shared/PopupFrame.prefab";
        GameObject root = new GameObject("PopupFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(CanvasGroup), typeof(PopupFrame));
        var rootRT = root.GetComponent<RectTransform>();
        rootRT.sizeDelta = new Vector2(1000f, 600f);

        var bgImg = root.GetComponent<UnityEngine.UI.Image>();
        bgImg.sprite = theme.panelBackgroundSprite;
        bgImg.type = UnityEngine.UI.Image.Type.Sliced;
        bgImg.color = Color.white;

        // Title Tab
        GameObject tab = new GameObject("TitleTab", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        tab.transform.SetParent(root.transform, false);
        var tabRT = tab.GetComponent<RectTransform>();
        tabRT.anchorMin = new Vector2(0f, 1f);
        tabRT.anchorMax = new Vector2(0f, 1f);
        tabRT.pivot = new Vector2(0f, 0.5f);
        tabRT.anchoredPosition = new Vector2(40f, 0f);
        tabRT.sizeDelta = new Vector2(300f, 44f);
        var tabImg = tab.GetComponent<UnityEngine.UI.Image>();
        tabImg.sprite = theme.speakerTabGreenSprite;
        tabImg.type = UnityEngine.UI.Image.Type.Sliced;
        tabImg.color = Color.white;

        GameObject titleTxt = new GameObject("TitleText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        titleTxt.transform.SetParent(tab.transform, false);
        var tTmp = titleTxt.GetComponent<TextMeshProUGUI>();
        tTmp.text = "TITLE";
        tTmp.fontSize = 24f;
        tTmp.fontStyle = FontStyles.Bold;
        tTmp.color = Color.white;
        tTmp.alignment = TextAlignmentOptions.Center;

        // Photo Frame (left side)
        GameObject photoRoot = new GameObject("PhotoFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        photoRoot.transform.SetParent(root.transform, false);
        var photoRT = photoRoot.GetComponent<RectTransform>();
        photoRT.anchorMin = new Vector2(0f, 0.5f);
        photoRT.anchorMax = new Vector2(0f, 0.5f);
        photoRT.pivot = new Vector2(0.5f, 0.5f);
        photoRT.anchoredPosition = new Vector2(200f, 20f);
        photoRT.sizeDelta = new Vector2(320f, 320f);
        var pBorderImg = photoRoot.GetComponent<UnityEngine.UI.Image>();
        pBorderImg.color = theme.borderColor;

        GameObject photoInner = new GameObject("PhotoImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(RectMask2D));
        photoInner.transform.SetParent(photoRoot.transform, false);
        var pInnerRT = photoInner.GetComponent<RectTransform>();
        pInnerRT.anchorMin = Vector2.zero;
        pInnerRT.anchorMax = Vector2.one;
        pInnerRT.offsetMin = new Vector2(4f, 4f);
        pInnerRT.offsetMax = new Vector2(-4f, -4f);
        var pImg = photoInner.GetComponent<UnityEngine.UI.Image>();
        pImg.color = Color.white; // Guaranteed untinted

        // Body Area (with RectMask2D safety clip)
        GameObject bodyArea = new GameObject("BodyArea", typeof(RectTransform), typeof(RectMask2D));
        bodyArea.transform.SetParent(root.transform, false);
        var bodyRT = bodyArea.GetComponent<RectTransform>();
        bodyRT.anchorMin = new Vector2(0f, 0f);
        bodyRT.anchorMax = new Vector2(1f, 1f);
        bodyRT.offsetMin = new Vector2(380f, 80f);
        bodyRT.offsetMax = new Vector2(-40f, -60f);

        GameObject bodyTmpObj = new GameObject("BodyText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        bodyTmpObj.transform.SetParent(bodyArea.transform, false);
        var bTmpRT = bodyTmpObj.GetComponent<RectTransform>();
        bTmpRT.anchorMin = Vector2.zero;
        bTmpRT.anchorMax = Vector2.one;
        bTmpRT.offsetMin = Vector2.zero;
        bTmpRT.offsetMax = Vector2.zero;
        var bTmp = bodyTmpObj.GetComponent<TextMeshProUGUI>();
        bTmp.text = "Body text goes here.";
        bTmp.fontSize = 32f;
        bTmp.color = Color.white;
        bTmp.alignment = TextAlignmentOptions.TopLeft;

        // Footer Row
        GameObject footer = new GameObject("FooterRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        footer.transform.SetParent(root.transform, false);
        var fRT = footer.GetComponent<RectTransform>();
        fRT.anchorMin = new Vector2(0f, 0f);
        fRT.anchorMax = new Vector2(1f, 0f);
        fRT.pivot = new Vector2(0.5f, 0f);
        fRT.anchoredPosition = new Vector2(0f, 16f);
        fRT.sizeDelta = new Vector2(-64f, 60f);
        var fHlg = footer.GetComponent<HorizontalLayoutGroup>();
        fHlg.childAlignment = TextAnchor.MiddleRight;
        fHlg.childControlWidth = false;
        fHlg.childControlHeight = false;
        fHlg.padding = new RectOffset(0, 32, 0, 0);

        // Instantiate HintRow inside FooterRow
        var hintPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/UI/Shared/HintRow.prefab");
        GameObject hintInstance = (GameObject)PrefabUtility.InstantiatePrefab(hintPrefab, footer.transform);

        // Choice Buttons Root (hidden by default)
        GameObject choiceRoot = new GameObject("ChoiceButtonsRoot", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        choiceRoot.transform.SetParent(footer.transform, false);
        choiceRoot.SetActive(false);

        // Wire into PopupFrame
        var frame = root.GetComponent<PopupFrame>();
        var so = new SerializedObject(frame);
        so.FindProperty("frameRect").objectReferenceValue = rootRT;
        so.FindProperty("frameCanvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
        so.FindProperty("frameBackgroundImage").objectReferenceValue = bgImg;
        so.FindProperty("titleTabRect").objectReferenceValue = tabRT;
        so.FindProperty("titleTabImage").objectReferenceValue = tabImg;
        so.FindProperty("titleTabText").objectReferenceValue = tTmp;
        so.FindProperty("photoFrameRoot").objectReferenceValue = photoRoot;
        so.FindProperty("photoImage").objectReferenceValue = pImg;
        so.FindProperty("photoBorderImage").objectReferenceValue = pBorderImg;
        so.FindProperty("bodyAreaRect").objectReferenceValue = bodyRT;
        so.FindProperty("bodyText").objectReferenceValue = bTmp;
        so.FindProperty("footerRowRect").objectReferenceValue = fRT;
        so.FindProperty("hintRow").objectReferenceValue = hintInstance.GetComponent<HintRow>();
        so.FindProperty("choiceButtonsRoot").objectReferenceValue = choiceRoot.GetComponent<RectTransform>();
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        Debug.Log("[PASS] PopupFrame prefab built.");
    }
}
