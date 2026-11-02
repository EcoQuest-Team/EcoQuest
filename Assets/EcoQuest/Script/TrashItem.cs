using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class TrashItem : MonoBehaviour
{
    private static readonly List<TrashItem> allTrashItems = new List<TrashItem>();
    public static IReadOnlyList<TrashItem> AllTrashItems
    {
        get
        {
            allTrashItems.RemoveAll(item => item == null);
            if (allTrashItems.Count == 0)
            {
                var found = Object.FindObjectsByType<TrashItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < found.Length; i++)
                {
                    if (found[i] != null && !allTrashItems.Contains(found[i]))
                    {
                        allTrashItems.Add(found[i]);
                    }
                }
            }
            return allTrashItems;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticOnSubsystemRegistration()
    {
        ResetStaticState();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStaticOnBeforeSceneLoad()
    {
        ResetStaticState();
    }

    public static void ResetStaticState()
    {
        allTrashItems.Clear();
        areTrashItemsAllowed = false;
        playerTransform = null;
    }

    private static bool areTrashItemsAllowed = false;
    public static bool AreTrashItemsAllowed
    {
        get => areTrashItemsAllowed;
        set
        {
            areTrashItemsAllowed = value;
            if (areTrashItemsAllowed)
            {
                DragAndDrop.SelfHealAllUnheldItems("tutorial unlocked trash");
            }
        }
    }

    private Rigidbody rb;
    private DragAndDrop dragAndDrop;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private Transform startParent;
    private Vector3 startScale = Vector3.one;
    private bool hasRecordedStart = false;
    private static Transform playerTransform;

    public bool IsDeposited { get; set; } = false;
    private float depositCooldownUntil = 0f;
    public bool IsOnDepositCooldown => Time.time < depositCooldownUntil;

    public void SetDepositCooldown(float duration = 1.0f)
    {
        depositCooldownUntil = Time.time + duration;
    }

    public void ClearDepositCooldown()
    {
        depositCooldownUntil = 0f;
    }

    private void Awake()
    {
        if (!allTrashItems.Contains(this)) allTrashItems.Add(this);
        rb = GetComponent<Rigidbody>();
        dragAndDrop = GetComponent<DragAndDrop>();
        RecordStartPositionIfNeeded();
        EnsureCameraRendersTrash();

        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }

        if (GetComponent<MinimapIcon>() == null)
        {
            MinimapIcon icon = gameObject.AddComponent<MinimapIcon>();
            icon.type = MinimapIcon.IconType.Trash;
        }
    }

    private void OnEnable()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (dragAndDrop == null) dragAndDrop = GetComponent<DragAndDrop>();
        if (AreTrashItemsAllowed)
        {
            if (dragAndDrop != null && !dragAndDrop.IsGrabbed)
            {
                dragAndDrop.EnsurePickable();
            }
            else if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.WakeUp();
            }
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
                startParent = transform.parent;
                startScale = transform.localScale;
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

        // Kill plane: respawn safely if item falls out of world
        if (transform.position.y < -10f)
        {
            ResetTrash();
            return;
        }
    }

    public void ResetTrash()
    {
        RecordStartPositionIfNeeded();
        IsDeposited = false;
        ClearDepositCooldown();

        if (startParent != null && transform.parent != startParent)
        {
            transform.SetParent(startParent, true);
        }
        if (startScale != Vector3.zero)
        {
            transform.localScale = startScale;
        }

        DragAndDrop drag = GetComponent<DragAndDrop>();
        if (drag != null)
        {
            drag.ForceRelease();
            drag.RestoreOriginalState();
        }

        var allCols = GetComponentsInChildren<Collider>(true);
        foreach (var c in allCols) c.enabled = true;

        transform.SetPositionAndRotation(startPosition, startRotation);

        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (AreTrashItemsAllowed)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.WakeUp();
            }
            else
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        gameObject.SetActive(AreTrashItemsAllowed);

        if (AreTrashItemsAllowed && drag != null)
        {
            drag.EnsurePickable(preserveReleasedState: false, logSelfHeal: true);
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