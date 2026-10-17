using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class TrashItem : MonoBehaviour
{
    private static readonly List<TrashItem> allTrashItems = new List<TrashItem>();

    private Rigidbody rb;
    private DragAndDrop dragAndDrop;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private bool hasRecordedStart = false;
    private static Transform playerTransform;

    private void Awake()
    {
        if (!allTrashItems.Contains(this)) allTrashItems.Add(this);
        rb = GetComponent<Rigidbody>();
        dragAndDrop = GetComponent<DragAndDrop>();
        RecordStartPositionIfNeeded();
        EnsureCameraRendersTrash();

        if (GetComponent<MinimapIcon>() == null)
        {
            MinimapIcon icon = gameObject.AddComponent<MinimapIcon>();
            icon.type = MinimapIcon.IconType.Trash;
        }
    }

    private void OnDestroy()
    {
        allTrashItems.Remove(this);
    }

    private void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        RecordStartPositionIfNeeded();
        EnsureCameraRendersTrash();

        if (GetComponent<MinimapIcon>() == null)
        {
            MinimapIcon icon = gameObject.AddComponent<MinimapIcon>();
            icon.type = MinimapIcon.IconType.Trash;
        }
    }

    private void RecordStartPositionIfNeeded()
    {
        if (!hasRecordedStart || startPosition == Vector3.zero)
        {
            if (transform.position != Vector3.zero)
            {
                startPosition = transform.position;
                startRotation = transform.rotation;
                hasRecordedStart = true;
            }
        }
    }

    private void EnsureCameraRendersTrash()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.cullingMask |= (1 << gameObject.layer);
        }
    }

    private void Update()
    {
        if (!hasRecordedStart || startPosition == Vector3.zero) return;

        if (transform.position.y < -2f)
        {
            ResetTrash();
            return;
        }

        if (dragAndDrop != null && dragAndDrop.IsGrabbed) return;

        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }

        if (playerTransform != null)
        {
            Vector3 diff = transform.position - playerTransform.position;
            float horizDistSq = diff.x * diff.x + diff.z * diff.z;
            if (horizDistSq < 0.35f * 0.35f && diff.y >= 0.15f && diff.y <= 1.85f)
            {
                ResetTrash();
            }
        }
    }

    public void ResetTrash()
    {
        RecordStartPositionIfNeeded();

        DragAndDrop drag = GetComponent<DragAndDrop>();
        if (drag != null) drag.ForceRelease();

        gameObject.SetActive(true);

        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            transform.SetPositionAndRotation(startPosition, startRotation);
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        else
        {
            transform.SetPositionAndRotation(startPosition, startRotation);
        }
    }

    public static void ResetAllTrashItems()
    {
        var found = FindObjectsByType<TrashItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var item in found)
        {
            if (item != null && !allTrashItems.Contains(item))
            {
                allTrashItems.Add(item);
            }
        }

        foreach (var item in allTrashItems)
        {
            if (item != null)
            {
                item.ResetTrash();
            }
        }
    }
}