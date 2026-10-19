using UnityEngine;

[CreateAssetMenu(fileName = "HoldSettings", menuName = "EcoQuest/Hold Settings")]
public class HoldSettings : ScriptableObject
{
    private static HoldSettings _instance;
    public static HoldSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Resources.Load<HoldSettings>("HoldSettings");
                if (_instance == null)
                {
                    HoldSettings[] all = Resources.FindObjectsOfTypeAll<HoldSettings>();
                    if (all != null && all.Length > 0) _instance = all[0];
                }
                if (_instance == null)
                {
                    _instance = CreateInstance<HoldSettings>();
                }
            }
            return _instance;
        }
    }

    [Header("Size Thresholds (Meters)")]
    [Tooltip("Items with longest dimension <= this are Small (one-handed hold)")]
    public float smallMaxDistance = 0.25f;

    [Tooltip("Items with longest dimension <= this are Medium (two-handed hold)")]
    public float mediumMaxDistance = 0.45f;

    [Tooltip("Items with longest dimension <= this are Large. Above this, items are scaled down.")]
    public float largeMaxDistance = 0.90f;

    [Header("Size Fitting & Screen Area Limits")]
    [Tooltip("Max screen height/width fraction the item should occupy")]
    public float maxScreenAreaFraction = 0.25f;

    [Tooltip("Minimum allowed uniform scale multiplier for oversized items")]
    [Range(0.1f, 1.0f)]
    public float minUniformScaleDown = 0.5f;

    [Header("Physics & Mass Dip")]
    public float minMass = 0.05f;
    public float maxMass = 5.0f;
    public float defaultMass = 0.20f;
    public float maxArmDipMeters = 0.05f;

    [Header("Grip Geometry")]
    [Tooltip("Vertical height fraction from bottom of bounds for grip point (30% to 40%)")]
    [Range(0.20f, 0.50f)]
    public float gripHeightFraction = 0.35f;

    [Tooltip("Narrowest horizontal width mapping for finger curl")]
    public float minGripWidth = 0.04f;
    public float maxGripWidth = 0.30f;
}
