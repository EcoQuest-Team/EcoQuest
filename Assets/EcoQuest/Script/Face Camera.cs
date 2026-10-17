using UnityEngine;

public class FaceCamera : MonoBehaviour
{
    [Header("Target Tracking")]
    [Tooltip("Drag your NPC object into this slot")]
    public Transform targetNPC;

    [Header("Position Offset")]
    [Tooltip("Adjust Y to make it float higher or lower over the head")]
    public Vector3 offset = new Vector3(0f, 2.3f, 0f);

    void LateUpdate()
    {
        // 1. If we haven't assigned the NPC yet, do nothing to prevent errors
        if (targetNPC == null) return;

        // 2. Lock the canvas directly to the NPC's position, plus our height offset
        transform.position = targetNPC.position + offset;

        // 3. Force the canvas to perfectly match the camera's rotation (billboarding)
        if (Camera.main != null)
        {
            transform.rotation = Camera.main.transform.rotation;
        }
    }
}