using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using StarterAssets;

public class RangerAnimationDriver : MonoBehaviour
{
    [Header("References")]
    public Animator visualAnimator;
    public ProceduralRangerAnimator proceduralAnimator;
    public Animator viewmodelAnimator;
    public ViewmodelBob viewmodelBob;

    [Header("Grounded Coyote Safety")]
    public float coyoteTime = 0.12f;

    [Header("Crouch Mechanic (C Key)")]
    public KeyCode crouchKey = KeyCode.C;
    public float crouchHeightMultiplier = 0.65f;
    public float crouchSpeedMultiplier = 0.5f;

    public bool IsCrouching { get; private set; }
    public bool IsGroundedSafe { get; private set; }
    public float CurrentHorizontalSpeed { get; private set; }

    private CharacterController _cc;
    private FirstPersonController _fpc;
    private PlayerSprintController _sprintCtrl;
    private StarterAssetsInputs _inputs;

    private float _baseColliderHeight = 1.75f;
    private Vector3 _baseColliderCenter = new Vector3(0f, 0.90f, 0f);
    private float _baseMoveSpeed = 4.0f;
    private Transform _cameraTarget;
    private Transform _headCameraRoot;
    private Transform _visualTransform;
    private float _visualY;

    private float _coyoteTimer;
    private bool _wasRawGrounded = true;
    private Vector3 _lastPosition;
    private float _smoothedSpeed;

    private static readonly int HashSpeed = Animator.StringToHash("Speed");
    private static readonly int HashIsGrounded = Animator.StringToHash("IsGrounded");
    private static readonly int HashCrouch = Animator.StringToHash("Crouch");
    private static readonly int HashJump = Animator.StringToHash("Jump");
    private static readonly int HashPickUp = Animator.StringToHash("PickUp");
    private static readonly int HashDrop = Animator.StringToHash("Drop");
    private static readonly int HashInteract = Animator.StringToHash("Interact");
    private static readonly int HashIsCarrying = Animator.StringToHash("IsCarrying");
    public bool IsCarrying { get; private set; }

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _fpc = GetComponent<FirstPersonController>();
        _sprintCtrl = GetComponent<PlayerSprintController>();
        _inputs = GetComponent<StarterAssetsInputs>();

        if (_cc != null)
        {
            if (_cc.height < 1.5f)
            {
                _cc.height = 1.80f;
                _cc.center = new Vector3(0f, 0.90f, 0f);
            }
            _baseColliderHeight = _cc.height;
            _baseColliderCenter = _cc.center;
        }
        if (_fpc != null)
        {
            _baseMoveSpeed = _fpc.MoveSpeed;
            if (_fpc.CinemachineCameraTarget != null)
            {
                _cameraTarget = _fpc.CinemachineCameraTarget.transform;
            }
        }

        if (visualAnimator == null)
        {
            Transform vis = transform.Find("Visual");
            if (vis != null) visualAnimator = vis.GetComponent<Animator>();
        }
        if (visualAnimator != null)
        {
            _visualTransform = visualAnimator.transform;
            Transform head = visualAnimator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null)
            {
                _headCameraRoot = head.Find("CameraRoot");
                if (_headCameraRoot == null)
                {
                    GameObject go = new GameObject("CameraRoot");
                    go.transform.SetParent(head, false);
                    go.transform.localPosition = new Vector3(0f, 0.08f, 0.14f);
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    _headCameraRoot = go.transform;
                }
            }
        }
        if (proceduralAnimator == null && visualAnimator != null)
        {
            proceduralAnimator = visualAnimator.GetComponent<ProceduralRangerAnimator>();
        }

        _lastPosition = transform.position;
        _coyoteTimer = coyoteTime;
        IsGroundedSafe = true;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // --- CROUCH MECHANIC BLOCK (C Key) ---
        bool crouchPressed = Input.GetKeyDown(crouchKey);
#if ENABLE_INPUT_SYSTEM
        if (!crouchPressed && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            crouchPressed = true;
#endif
        if (crouchPressed)
        {
            SetCrouch(!IsCrouching);
        }

        if (_visualTransform != null)
        {
            float heightDelta = _baseColliderHeight * (1.0f - crouchHeightMultiplier);
            float targetVisualY = IsCrouching ? (-heightDelta * 0.5f) : 0f;
            _visualY = Mathf.Lerp(_visualY, targetVisualY, dt * 12.0f);
            Vector3 vp = _visualTransform.localPosition;
            vp.y = _visualY;
            _visualTransform.localPosition = vp;
        }
        // --- END CROUCH MECHANIC BLOCK ---

        // 1. Measure real horizontal velocity
        Vector3 ccVel = _cc != null ? _cc.velocity : Vector3.zero;
        Vector3 posDeltaVel = (transform.position - _lastPosition) / dt;
        _lastPosition = transform.position;

        float ccHoriz = new Vector2(ccVel.x, ccVel.z).magnitude;
        float deltaHoriz = new Vector2(posDeltaVel.x, posDeltaVel.z).magnitude;
        CurrentHorizontalSpeed = Mathf.Max(ccHoriz, deltaHoriz);
        _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, CurrentHorizontalSpeed, dt * 14.0f);

        // 2. Grounded check with short coyote-time safety (prevents slope flicker)
        bool rawGrounded = (_fpc != null && _fpc.Grounded) || (_cc != null && _cc.isGrounded);
        float vertVel = Mathf.Max(ccVel.y, posDeltaVel.y);

        if ( _wasRawGrounded && !rawGrounded && vertVel > 0.8f )
        {
            _coyoteTimer = 0f;
            PlayJump();
        }
        else if (rawGrounded)
        {
            _coyoteTimer = coyoteTime;
        }
        else
        {
            _coyoteTimer -= dt;
        }
        _wasRawGrounded = rawGrounded;
        IsGroundedSafe = _coyoteTimer > 0f;

        // 3. Sprint detection
        bool sprinting = (_sprintCtrl != null && _sprintCtrl.IsSprinting) ||
                         (_inputs != null && _inputs.sprint) ||
                         Input.GetKey(KeyCode.LeftShift);

        // 4. Drive Visual Animator, ViewmodelArms Animator, and ProceduralRangerAnimator
        if (visualAnimator != null && visualAnimator.runtimeAnimatorController != null)
        {
            bool carrying = (DragAndDrop.CurrentlyGrabbedItem != null && DragAndDrop.CurrentlyGrabbedItem.IsGrabbed) || ItemCarry.ActiveCarriedItem != null;
            IsCarrying = carrying;
            visualAnimator.SetFloat(HashSpeed, _smoothedSpeed);
            visualAnimator.SetBool(HashIsGrounded, IsGroundedSafe);
            visualAnimator.SetBool(HashCrouch, IsCrouching);
            visualAnimator.SetBool(HashIsCarrying, carrying);
            if (visualAnimator.layerCount > 1)
            {
                float targetW = carrying ? 1.0f : 0.0f;
                float currentW = visualAnimator.GetLayerWeight(1);
                float newW = Mathf.MoveTowards(currentW, targetW, dt * 6.0f);
                if (!carrying && newW < 0.005f) newW = 0f;
                visualAnimator.SetLayerWeight(1, newW);
            }
            if (!carrying)
            {
                visualAnimator.ResetTrigger(HashPickUp);
            }
        }

        if (viewmodelAnimator != null && viewmodelAnimator.runtimeAnimatorController != null && viewmodelAnimator.enabled)
        {
            viewmodelAnimator.SetFloat(HashSpeed, _smoothedSpeed);
            viewmodelAnimator.SetBool(HashIsGrounded, IsGroundedSafe);
            viewmodelAnimator.SetBool(HashCrouch, IsCrouching);
        }

        if (proceduralAnimator != null)
        {
            proceduralAnimator.HorizontalSpeed = CurrentHorizontalSpeed;
            proceduralAnimator.IsGrounded = IsGroundedSafe;
            proceduralAnimator.IsCrouching = IsCrouching;
            proceduralAnimator.IsSprinting = sprinting && !IsCrouching;
        }
    }

    public void SetCrouch(bool crouch)
    {
        if (IsCrouching == crouch) return;
        IsCrouching = crouch;

        if (_cc != null)
        {
            float newHeight = IsCrouching ? (_baseColliderHeight * crouchHeightMultiplier) : _baseColliderHeight;
            float heightDiff = _baseColliderHeight - newHeight;
            _cc.height = newHeight;
            _cc.center = new Vector3(_baseColliderCenter.x, _baseColliderCenter.y - heightDiff * 0.5f, _baseColliderCenter.z);
        }

        if (_fpc != null)
        {
            _fpc.MoveSpeed = IsCrouching ? (_baseMoveSpeed * crouchSpeedMultiplier) : _baseMoveSpeed;
        }
    }

    public void PlayJump()
    {
        if (visualAnimator != null && visualAnimator.runtimeAnimatorController != null)
            visualAnimator.SetTrigger(HashJump);
        if (viewmodelAnimator != null && viewmodelAnimator.runtimeAnimatorController != null && viewmodelAnimator.enabled)
            viewmodelAnimator.SetTrigger(HashJump);
        if (proceduralAnimator != null)
            proceduralAnimator.TriggerJump();
    }

    public void PlayPickUp()
    {
        if (visualAnimator != null && visualAnimator.runtimeAnimatorController != null)
            visualAnimator.SetTrigger(HashPickUp);
        if (viewmodelAnimator != null && viewmodelAnimator.runtimeAnimatorController != null && viewmodelAnimator.enabled)
            viewmodelAnimator.SetTrigger(HashPickUp);
        if (proceduralAnimator != null)
            proceduralAnimator.TriggerPickUp();
        if (viewmodelBob != null)
            viewmodelBob.PlayPickUp();
    }

    public void PlayDrop()
    {
        if (visualAnimator != null && visualAnimator.runtimeAnimatorController != null)
            visualAnimator.SetTrigger(HashDrop);
        if (viewmodelAnimator != null && viewmodelAnimator.runtimeAnimatorController != null && viewmodelAnimator.enabled)
            viewmodelAnimator.SetTrigger(HashDrop);
        if (proceduralAnimator != null)
            proceduralAnimator.TriggerDrop();
        if (viewmodelBob != null)
            viewmodelBob.PlayDrop();
    }

    public void PlayInteract()
    {
        if (visualAnimator != null && visualAnimator.runtimeAnimatorController != null)
            visualAnimator.SetTrigger(HashInteract);
        if (viewmodelAnimator != null && viewmodelAnimator.runtimeAnimatorController != null && viewmodelAnimator.enabled)
            viewmodelAnimator.SetTrigger(HashInteract);
        if (proceduralAnimator != null)
            proceduralAnimator.TriggerInteract();
    }

    public void SetCarrying(bool carrying)
    {
        IsCarrying = carrying;
        if (visualAnimator != null && visualAnimator.runtimeAnimatorController != null)
        {
            visualAnimator.SetBool(HashIsCarrying, carrying);
            if (!carrying)
            {
                visualAnimator.ResetTrigger(HashPickUp);
                if (visualAnimator.layerCount > 1) visualAnimator.SetLayerWeight(1, 0f);
            }
        }
    }

    private void LateUpdate()
    {
        if (_cameraTarget != null && _headCameraRoot != null)
        {
            _cameraTarget.position = _headCameraRoot.position;
        }
    }
}
