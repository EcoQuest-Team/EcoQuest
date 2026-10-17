using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DragAndDrop : MonoBehaviour
{
    [Header("Distance Settings")]
    [SerializeField] private float pickupRange = 5f; 
    [SerializeField] private float holdDistance = 2f; 

    private Rigidbody rb;
    private Camera mainCamera;
    private bool isGrabbed = false;
    private Collider[] myColliders;
    private static Collider[] playerColliders;

    public bool IsGrabbed => isGrabbed;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        mainCamera = Camera.main;
        myColliders = GetComponentsInChildren<Collider>();
        CachePlayerColliders();
    }

    private void CachePlayerColliders()
    {
        if (playerColliders == null || playerColliders.Length == 0)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerColliders = player.GetComponentsInChildren<Collider>();
            }
        }
    }

    private void OnDisable()
    {
        if (isGrabbed) ForceRelease();
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0)) TryGrabObject();
        
        if (Input.GetMouseButtonUp(0) && isGrabbed) ReleaseObject();
    }

    private void FixedUpdate()
    {
        if (isGrabbed && mainCamera != null)
        {
            float targetDist = holdDistance;
            Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);

            int ignoreMask = (1 << 2);
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer != -1) ignoreMask |= (1 << playerLayer);
            int trashLayer = LayerMask.NameToLayer("TrashItem");
            if (trashLayer != -1) ignoreMask |= (1 << trashLayer);
            int binSolidLayer = LayerMask.NameToLayer("BinSolid");
            if (binSolidLayer != -1) ignoreMask |= (1 << binSolidLayer);

            if (Physics.Raycast(ray, out RaycastHit hit, holdDistance, ~ignoreMask, QueryTriggerInteraction.Ignore))
            {
                targetDist = Mathf.Clamp(hit.distance - 0.25f, 0.15f, holdDistance);
            }

            Vector3 holdPosition = mainCamera.transform.position + (mainCamera.transform.forward * targetDist);
            rb.MovePosition(holdPosition);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void TryGrabObject()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;

        Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            if (hit.collider.gameObject == gameObject)
            {
                float distance = Vector3.Distance(mainCamera.transform.position, transform.position);

                if (distance <= pickupRange)
                {
                    isGrabbed = true;
                    rb.useGravity = false;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    SetPlayerCollisionIgnored(true);
                }
            }
        }
    }

    public void ReleaseObject()
    {
        if (!isGrabbed) return;
        isGrabbed = false;
        if (rb != null)
        {
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        SetPlayerCollisionIgnored(false);
    }

    public void ForceRelease()
    {
        ReleaseObject();
    }

    private void SetPlayerCollisionIgnored(bool ignore)
    {
        if (myColliders == null) myColliders = GetComponentsInChildren<Collider>();
        CachePlayerColliders();

        if (myColliders == null || playerColliders == null) return;

        for (int i = 0; i < myColliders.Length; i++)
        {
            Collider myCol = myColliders[i];
            if (myCol == null) continue;
            for (int j = 0; j < playerColliders.Length; j++)
            {
                Collider pCol = playerColliders[j];
                if (pCol != null)
                {
                    Physics.IgnoreCollision(myCol, pCol, ignore);
                }
            }
        }
    }
}