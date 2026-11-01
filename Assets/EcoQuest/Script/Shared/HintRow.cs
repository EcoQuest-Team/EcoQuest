using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Reusable HintRow element combining KeyCapView and a label in one level row (height 48px),
/// with 32px boundary padding and unified styling.
/// </summary>
[RequireComponent(typeof(HorizontalLayoutGroup))]
public class HintRow : MonoBehaviour
{
    [SerializeField] private KeyCapView keyCapView;
    [SerializeField] private TextMeshProUGUI actionLabel;
    [SerializeField] private HorizontalLayoutGroup layoutGroup;
    [SerializeField] private ContentSizeFitter sizeFitter;

    public KeyCapView KeyCap => keyCapView;
    public string CurrentAction => actionLabel != null ? actionLabel.text : "";

    private void Awake()
    {
        EnsureLayout();
    }

    public void EnsureLayout()
    {
        if (layoutGroup == null) layoutGroup = GetComponent<HorizontalLayoutGroup>();
        if (layoutGroup != null)
        {
            layoutGroup.childAlignment = TextAnchor.MiddleCenter;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.childForceExpandWidth = false;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.spacing = 14f;
            layoutGroup.padding = new RectOffset(16, 32, 0, 0); // 32px right padding
        }

        if (sizeFitter == null) sizeFitter = GetComponent<ContentSizeFitter>();
        if (sizeFitter == null) sizeFitter = gameObject.AddComponent<ContentSizeFitter>();
        sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (actionLabel != null)
        {
            actionLabel.alignment = TextAlignmentOptions.MidlineLeft;
            actionLabel.enableAutoSizing = false;
            actionLabel.fontSize = 24f;
            actionLabel.color = Color.white;
            actionLabel.textWrappingMode = TextWrappingModes.NoWrap;
            actionLabel.overflowMode = TextOverflowModes.Overflow;
        }

        var rt = transform as RectTransform;
        if (rt != null)
        {
            Vector2 sz = rt.sizeDelta;
            sz.y = 48f;
            rt.sizeDelta = sz;
        }
    }

    public void SetHint(string key, string action)
    {
        EnsureLayout();
        if (keyCapView != null) keyCapView.SetKey(key);
        if (actionLabel != null) actionLabel.text = action;
        LayoutRebuilder.ForceRebuildLayoutImmediate(transform as RectTransform);
    }

    public void SetLocked(bool locked, float duration = 0.5f)
    {
        if (keyCapView != null) keyCapView.SetLocked(locked, duration);
    }
}
