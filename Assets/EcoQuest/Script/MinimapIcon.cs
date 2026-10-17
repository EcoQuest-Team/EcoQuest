using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MinimapIcon : MonoBehaviour
{
    public enum IconType { NPC, Trash, Player }

    [Header("Icon Settings")]
    public IconType type = IconType.NPC;
    public Sprite customSprite;

    private void Awake()
    {
        if (type == IconType.Trash && GetComponent<TrashItem>() == null)
        {
            if (Application.isPlaying) Destroy(this);
            else DestroyImmediate(this);
            return;
        }
        CleanLegacy3DQuads();
    }

    private void OnEnable()
    {
        if (type == IconType.Trash && GetComponent<TrashItem>() == null)
        {
            return;
        }
        CleanLegacy3DQuads();
        MinimapController.RegisterIcon(this);
    }

    private void OnDisable()
    {
        MinimapController.UnregisterIcon(this);
    }

    private void OnDestroy()
    {
        MinimapController.UnregisterIcon(this);
    }

    private void Start()
    {
        if (type == IconType.Trash && GetComponent<TrashItem>() == null)
        {
            if (Application.isPlaying) Destroy(this);
            else DestroyImmediate(this);
            return;
        }
        CleanLegacy3DQuads();
        MinimapController.RegisterIcon(this);
    }

    /// <summary>
    /// Removes any obsolete 3D quads previously created by legacy versions of MinimapIcon.
    /// </summary>
    public void CleanLegacy3DQuads()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child != null && child.name.StartsWith("MinimapIcon_"))
            {
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }
    }

    public Sprite GetResolvedSprite()
    {
        if (customSprite != null) return customSprite;
        return MinimapController.GetDefaultSprite(type);
    }

    public static void SetTrashIconsActive(bool active)
    {
        MinimapController.AreTrashIconsActive = active;
        if (MinimapController.Instance != null)
        {
            MinimapController.Instance.NotifyTrashIconsActiveChanged();
        }
    }

    /// <summary>
    /// Legacy fallback method preserved for backwards compatibility with any external callers.
    /// Uses the correct Assets/EcoQuest/Sprites/MapIcons path with no procedural textures.
    /// </summary>
    public Texture2D LoadIconTexture()
    {
        Sprite sprite = GetResolvedSprite();
        if (sprite != null && sprite.texture != null)
        {
            return sprite.texture;
        }

        string fileName = (type == IconType.Player) ? "map_person_pin_tan.png"
            : (type == IconType.NPC) ? "map_npc_guardian_elder.png"
            : "map_trash_pin_alert.png";

        string filePath = System.IO.Path.Combine(Application.dataPath, "EcoQuest", "Sprites", "MapIcons", fileName);
        if (System.IO.File.Exists(filePath))
        {
            byte[] fileData = System.IO.File.ReadAllBytes(filePath);
            Texture2D tex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            if (tex.LoadImage(fileData))
            {
                return tex;
            }
        }

        return Texture2D.whiteTexture;
    }
}
