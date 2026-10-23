using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ItemCarry : MonoBehaviour
{
    [Header("Carry Settings")]
    public float pickupRadius = 2.0f;
    public float maxPickupAngle = 85.0f;
    public float tossVelocity = 3.2f;
    public KeyCode pickUpKey = KeyCode.E;
    public KeyCode dropKey = KeyCode.G;

    public Transform handSocketR;
    public GameObject CarriedItem { get; private set; }
    public bool IsBusy { get; private set; }

    private static ItemCarry _instance;
    public static GameObject ActiveCarriedItem => (_instance != null && _instance.CarriedItem != null) ? _instance.CarriedItem : null;

    private RangerAnimationDriver _driver;
    private Collider[] _playerColliders;
    private Vector3 _savedWorldScale;
    private Transform _savedOriginalParent;

    private void Awake()
    {
        _instance = this;
        _driver = GetComponent<RangerAnimationDriver>();
        _playerColliders = GetComponentsInChildren<Collider>(true);
        EnsureHandSocket();
    }

    public void EnsureHandSocket()
    {
        if (handSocketR != null) return;

        Transform vis = transform.Find("Visual");
        Animator anim = vis != null ? vis.GetComponent<Animator>() : GetComponentInChildren<Animator>();
        Transform rightHand = null;

        if (anim != null && anim.avatar != null && anim.avatar.isValid)
        {
            rightHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        }
        if (rightHand == null && vis != null)
        {
            foreach (Transform t in vis.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Hand.R" || t.name == "RightHand")
                {
                    rightHand = t;
                    break;
                }
            }
        }

        if (rightHand != null)
        {
            Transform existing = rightHand.Find("HandSocket_R");
            if (existing != null)
            {
                handSocketR = existing;
            }
            else
            {
                GameObject sock = new GameObject("HandSocket_R");
                sock.transform.SetParent(rightHand, false);
                sock.transform.localPosition = new Vector3(0f, 0.08f, 0.04f);
                sock.transform.localRotation = Quaternion.identity;
                sock.transform.localScale = Vector3.one;
                handSocketR = sock.transform;
            }
        }
    }

    private void Update()
    {
        if (CarriedItem != null && !DragAndDrop.IsAnyItemGrabbed && CarriedItem.GetComponentInParent<DragAndDrop>() != null)
            CarriedItem = null;

        if (IsBusy) return;

        bool pickPressed = Input.GetKeyDown(pickUpKey);
        bool dropPressed = Input.GetKeyDown(dropKey);
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (!pickPressed && Keyboard.current.eKey.wasPressedThisFrame) pickPressed = true;
            if (!dropPressed && Keyboard.current.gKey.wasPressedThisFrame) dropPressed = true;
        }
#endif

        if (pickPressed && CarriedItem == null && !DragAndDrop.IsAnyItemGrabbed)
        {
            TryPickUpNearest();
        }
        else if (dropPressed && CarriedItem != null)
        {
            TryDropCarried();
        }
    }

    private static bool IsPickableCandidate(GameObject go)
    {
        if (go == null) return false;
        string t = go.tag;
        if (t == "Item" || t == "Trash") return true;
        if (go.GetComponent<TrashItem>() != null || go.GetComponent<DragAndDrop>() != null) return true;
        if (go.GetComponentInParent<TrashItem>() != null || go.GetComponentInParent<DragAndDrop>() != null) return true;
        return false;
    }

    public bool TryPickUpNearest()
    {
        if (IsBusy || CarriedItem != null) return false;
        EnsureHandSocket();
        if (handSocketR == null) return false;

        Collider[] hits = Physics.OverlapSphere(transform.position + Vector3.up * 0.8f, pickupRadius);
        GameObject best = null;
        float bestDist = float.MaxValue;

        foreach (Collider col in hits)
        {
            if (col == null || col.transform.IsChildOf(transform)) continue;
            GameObject candidate = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.gameObject;
            if (!IsPickableCandidate(candidate)) continue;

            Vector3 toItem = candidate.transform.position - transform.position;
            Vector3 flatToItem = new Vector3(toItem.x, 0f, toItem.z);
            float dist = toItem.magnitude;
            if (dist > pickupRadius) continue;

            if (flatToItem.sqrMagnitude > 0.01f)
            {
                float angle = Vector3.Angle(transform.forward, flatToItem.normalized);
                if (angle > maxPickupAngle) continue;
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                best = candidate;
            }
        }

        if (best == null) return false;
        StartCoroutine(PickUpRoutine(best));
        return true;
    }

    private IEnumerator PickUpRoutine(GameObject item)
    {
        IsBusy = true;
        if (_driver == null) _driver = GetComponent<RangerAnimationDriver>();

        var dnd = item != null ? (item.GetComponent<DragAndDrop>() ?? item.GetComponentInParent<DragAndDrop>()) : null;
        if (dnd != null)
        {
            dnd.GrabObject();
            CarriedItem = item;
            yield return new WaitForSeconds(0.4f);
            IsBusy = false;
            yield break;
        }

        if (_driver != null) _driver.PlayPickUp();

        // Wait until middle of PickUp animation (~0.4s of 0.8s)
        yield return new WaitForSeconds(0.4f);

        if (item != null)
        {
            CarriedItem = item;
            if (_driver != null) _driver.SetCarrying(true);
            _savedOriginalParent = item.transform.parent;
            _savedWorldScale = item.transform.localScale;
            Bounds b = DragAndDrop.CalculateCombinedBounds(item);
            float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float fit = longest > 0.16f ? Mathf.Clamp(0.16f / longest, 0.08f, 1.0f) : 1.0f;
            Vector3 heldWorldScale = item.transform.lossyScale * fit;

            SetIgnorePlayerCollision(item, true);

            foreach (Collider c in item.GetComponentsInChildren<Collider>(true))
            {
                c.enabled = false;
            }

            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            Transform targetParent = null;
            if (Camera.main != null) targetParent = Camera.main.transform.Find("HoldSocket");
            if (targetParent == null && _driver != null && _driver.visualAnimator != null)
            {
                Transform chest = _driver.visualAnimator.transform.Find("Armature/Hips/Spine/Chest") ?? _driver.visualAnimator.GetBoneTransform(HumanBodyBones.Chest) ?? _driver.visualAnimator.GetBoneTransform(HumanBodyBones.Spine);
                if (chest != null) targetParent = chest.Find("HoldSocket");
            }
            if (targetParent == null) targetParent = handSocketR;

            if (targetParent != null)
            {
                item.transform.SetParent(targetParent, false);
                item.transform.localPosition = Vector3.zero;
                item.transform.localRotation = Quaternion.identity;

                Vector3 parentScale = targetParent.lossyScale;
                item.transform.localScale = new Vector3(
                    parentScale.x != 0f ? heldWorldScale.x / parentScale.x : heldWorldScale.x,
                    parentScale.y != 0f ? heldWorldScale.y / parentScale.y : heldWorldScale.y,
                    parentScale.z != 0f ? heldWorldScale.z / parentScale.z : heldWorldScale.z
                );
            }
        }

        yield return new WaitForSeconds(0.4f);
        IsBusy = false;
    }

    public bool TryDropCarried()
    {
        if (IsBusy || CarriedItem == null) return false;
        StartCoroutine(DropRoutine());
        return true;
    }

    private IEnumerator DropRoutine()
    {
        IsBusy = true;
        if (CarriedItem != null)
        {
            var dnd = CarriedItem.GetComponent<DragAndDrop>() ?? CarriedItem.GetComponentInParent<DragAndDrop>();
            if (dnd != null)
            {
                dnd.ReleaseObject();
                CarriedItem = null;
                yield return new WaitForSeconds(0.25f);
                IsBusy = false;
                yield break;
            }
        }

        if (_driver == null) _driver = GetComponent<RangerAnimationDriver>();
        if (_driver != null) _driver.PlayDrop();

        // Release at middle of Drop animation (~0.25s of 0.5s)
        yield return new WaitForSeconds(0.25f);

        if (CarriedItem != null)
        {
            GameObject item = CarriedItem;
            CarriedItem = null;
            if (_driver != null) _driver.SetCarrying(false);

            item.transform.SetParent(_savedOriginalParent, true);
            item.transform.localScale = _savedWorldScale;
            item.transform.position = transform.position + transform.forward * 0.85f + Vector3.up * 1.05f;

            foreach (Collider c in item.GetComponentsInChildren<Collider>(true))
            {
                c.enabled = true;
            }

            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                Vector3 tossDir = (transform.forward + Vector3.up * 0.35f).normalized;
                rb.AddForce(tossDir * tossVelocity, ForceMode.VelocityChange);
            }

            StartCoroutine(ReenablePlayerCollisionDelayed(item, 0.35f));
        }

        yield return new WaitForSeconds(0.25f);
        IsBusy = false;
    }

    private IEnumerator ReenablePlayerCollisionDelayed(GameObject item, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (item != null && CarriedItem != item)
        {
            SetIgnorePlayerCollision(item, false);
        }
    }

    private void SetIgnorePlayerCollision(GameObject item, bool ignore)
    {
        if (item == null || _playerColliders == null) return;
        Collider[] itemCols = item.GetComponentsInChildren<Collider>(true);
        foreach (Collider ic in itemCols)
        {
            if (ic == null) continue;
            foreach (Collider pc in _playerColliders)
            {
                if (pc != null)
                    Physics.IgnoreCollision(ic, pc, ignore);
            }
        }
    }
}
