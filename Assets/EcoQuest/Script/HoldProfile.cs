using UnityEngine;

public enum HoldSizeCategory
{
    Automatic,
    Small,
    Medium,
    Large
}

[DisallowMultipleComponent]
public class HoldProfile : MonoBehaviour
{
    [Header("Optional Grip Overrides")]
    [Tooltip("World/local offset applied to the computed lower-middle grip point")]
    public Vector3 gripOffset = Vector3.zero;

    [Tooltip("Euler rotation offset applied on top of the upright camera-facing orientation")]
    public Vector3 rotationOffset = Vector3.zero;

    [Header("Optional Size Category Override")]
    public bool overrideSizeCategory = false;
    public HoldSizeCategory sizeCategory = HoldSizeCategory.Automatic;
}
