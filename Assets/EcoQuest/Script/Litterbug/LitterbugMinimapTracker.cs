using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(120)]
public class LitterbugMinimapTracker : MonoBehaviour
{
    public const int MaxLitterbugSlots = 5;

    [Header("Inspector Sprites (Do NOT load via Application.dataPath)")]
    [SerializeField] private Sprite exclamationSprite;
    [SerializeField] private Sprite greenCheckSprite;
    [SerializeField] private Sprite edgeArrowSprite;

    [Header("Sizing & Clamp Padding")]
    [SerializeField] private float iconScaleMultiplier = 0.92f;
    [SerializeField] private float edgePaddingPixels = 16f;

    private struct MarkerData
    {
        public Transform target;
        public bool visible;
        public bool isCompletedCheck;
    }

    private readonly MarkerData[] slots = new MarkerData[MaxLitterbugSlots + 1]; // 0..4 = NPCs, 5 = Fountain Quest Giver
    private readonly RectTransform[] iconRects = new RectTransform[MaxLitterbugSlots + 1];
    private readonly Image[] iconImages = new Image[MaxLitterbugSlots + 1];
    private RectTransform cachedContainer;

    public void ConfigureSprites(Sprite exclamation, Sprite greenCheck, Sprite edgeArrow)
    {
        exclamationSprite = exclamation;
        greenCheckSprite = greenCheck;
        edgeArrowSprite = edgeArrow;
    }

    public void SetSlot(int index, Transform target, bool visible, bool completedCheck)
    {
        if (index < 0 || index >= slots.Length) return;
        slots[index].target = target;
        slots[index].visible = visible;
        slots[index].isCompletedCheck = completedCheck;
    }

    public void SetFountainMarker(Transform fountainTarget, bool visible, bool completedCheck = false)
    {
        SetSlot(MaxLitterbugSlots, fountainTarget, visible, completedCheck);
    }

    public void ClearAll()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].target = null;
            slots[i].visible = false;
            slots[i].isCompletedCheck = false;
            if (iconRects[i] != null && iconRects[i].gameObject.activeSelf)
                iconRects[i].gameObject.SetActive(false);
        }
    }

    private bool EnsurePoolCreated(RectTransform container)
    {
        if (container == null) return false;
        if (cachedContainer == container && iconRects[0] != null) return true;

        cachedContainer = container;
        for (int i = 0; i < iconRects.Length; i++)
        {
            if (iconRects[i] != null) Destroy(iconRects[i].gameObject);

            string label = (i == MaxLitterbugSlots) ? "MinimapQuestGiverIcon" : $"MinimapLitterbugIcon_{i}";
            GameObject go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(container, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(30f, 30f);

            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.sprite = exclamationSprite;

            go.SetActive(false);
            iconRects[i] = rt;
            iconImages[i] = img;
        }
        return true;
    }

    private void LateUpdate()
    {
        MinimapController mc = MinimapController.Instance;
        if (mc == null) return;

        Camera cam = mc.ActiveMapCamera;
        RectTransform container = mc.MapIconsContainer;
        if (cam == null || !EnsurePoolCreated(container)) return;

        Vector2 rectSize = container.rect.size;
        if (rectSize.x <= 2f || rectSize.y <= 2f) return;

        float baseSize = Mathf.Lerp(mc.MinimapIconSize, mc.FullMapIconSize, mc.TransitionProgress) * iconScaleMultiplier;
        float pulse = 1f + 0.15f * Mathf.Sin(Time.unscaledTime * 5.6f);

        float halfW = Mathf.Max(8f, rectSize.x * 0.5f - edgePaddingPixels);
        float halfH = Mathf.Max(8f, rectSize.y * 0.5f - edgePaddingPixels);

        for (int i = 0; i < slots.Length; i++)
        {
            RectTransform rt = iconRects[i];
            Image img = iconImages[i];
            MarkerData data = slots[i];

            if (!data.visible || data.target == null || !data.target.gameObject.activeInHierarchy)
            {
                if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false);
                continue;
            }

            Vector3 vp = cam.WorldToViewportPoint(data.target.position);
            if (vp.z < 0f)
            {
                vp.x = 1f - vp.x;
                vp.y = 1f - vp.y;
            }

            Vector2 localPos = new Vector2((vp.x - 0.5f) * rectSize.x, (vp.y - 0.5f) * rectSize.y);
            bool insideBounds = Mathf.Abs(localPos.x) <= halfW && Mathf.Abs(localPos.y) <= halfH && vp.z > 0f;

            if (data.isCompletedCheck)
            {
                // Persuaded NPC: green check on both minimap (when in view) and FullMapCamera; no edge arrow clamping
                bool visibleInFrustum = vp.z > 0f && vp.x >= -0.02f && vp.x <= 1.02f && vp.y >= -0.02f && vp.y <= 1.02f;
                if (!visibleInFrustum)
                {
                    if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false);
                    continue;
                }

                if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
                if (img.sprite != greenCheckSprite) img.sprite = greenCheckSprite;
                rt.anchoredPosition = localPos;
                rt.localRotation = Quaternion.identity;
                rt.sizeDelta = new Vector2(baseSize * 0.92f, baseSize * 0.92f);
                rt.localScale = Vector3.one;
            }
            else if (insideBounds)
            {
                // Active "!" target inside map bounds: pulsing exclamation icon
                if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
                if (img.sprite != exclamationSprite) img.sprite = exclamationSprite;
                rt.anchoredPosition = localPos;
                rt.localRotation = Quaternion.identity;
                rt.sizeDelta = new Vector2(baseSize, baseSize);
                rt.localScale = Vector3.one * pulse;
            }
            else
            {
                // Out of range: clamp to minimap edge as directional arrow pointing toward target
                if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
                Sprite arrow = edgeArrowSprite != null ? edgeArrowSprite : exclamationSprite;
                if (img.sprite != arrow) img.sprite = arrow;

                Vector2 dir = localPos;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;

                float tx = Mathf.Abs(dir.x) > 0.0001f ? halfW / Mathf.Abs(dir.x) : float.MaxValue;
                float ty = Mathf.Abs(dir.y) > 0.0001f ? halfH / Mathf.Abs(dir.y) : float.MaxValue;
                float t = Mathf.Min(tx, ty);

                rt.anchoredPosition = dir * t;
                float angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f; // Upward arrow at 0 deg
                rt.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
                rt.sizeDelta = new Vector2(baseSize * 0.88f, baseSize * 0.88f);
                rt.localScale = Vector3.one * pulse;
            }
        }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < iconRects.Length; i++)
        {
            if (iconRects[i] != null) Destroy(iconRects[i].gameObject);
        }
    }
}
