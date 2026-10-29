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
    public float coyoteTime = 0.10f;

    [Header("Hotkeys")]
    public KeyCode crouchKey = KeyCode.C;
    public KeyCode waveKey = KeyCode.H;
    public float crouchHeightMultiplier = 0.68f;
    public float crouchSpeedMultiplier = 0.4f;

    public bool IsCrouching => _fpc != null && _fpc.IsCrouching;
    public bool IsGroundedSafe => _fpc == null || _fpc.Grounded;
    public float CurrentHorizontalSpeed => _fpc != null ? _fpc.CurrentHorizontalSpeed : 0f;
    public bool IsCarrying => _fpc != null && _fpc.IsCarrying;

    private FirstPersonController _fpc;
    private PlayerSprintController _sprintCtrl;
    private StarterAssetsInputs _inputs;
    private Transform _cameraTarget;
    private Transform _headCameraRoot;

    private void Awake()
    {
        _fpc = GetComponent<FirstPersonController>();
        _sprintCtrl = GetComponent<PlayerSprintController>();
        _inputs = GetComponent<StarterAssetsInputs>();

        if (visualAnimator == null)
        {
            Transform vis = transform.Find("Visual");
            if (vis != null) visualAnimator = vis.GetComponent<Animator>();
        }

        if (_fpc != null)
        {
            _fpc.CoyoteTime = coyoteTime;
            _fpc.CrouchHeightMultiplier = crouchHeightMultiplier;
            _fpc.CrouchSpeedMultiplier = crouchSpeedMultiplier;
            if (visualAnimator != null) _fpc.CharacterAnimator = visualAnimator;
            if (_fpc.CinemachineCameraTarget != null)
            {
                _cameraTarget = _fpc.CinemachineCameraTarget.transform;
            }
        }

        if (visualAnimator != null)
        {
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
    }

    private void Update()
    {
        if (Time.deltaTime <= 0f || _fpc == null) return;

        bool crouchPressed = Input.GetKeyDown(crouchKey);
        bool wavePressed = Input.GetKeyDown(waveKey);
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (!crouchPressed && Keyboard.current.cKey.wasPressedThisFrame) crouchPressed = true;
            if (!wavePressed && Keyboard.current.hKey.wasPressedThisFrame) wavePressed = true;
        }
#endif
        if (crouchPressed)
        {
            SetCrouch(!_fpc.IsCrouching);
        }
        if (wavePressed)
        {
            PlayWave();
        }

        if (proceduralAnimator != null)
        {
            bool sprinting = (_sprintCtrl != null && _sprintCtrl.IsSprinting) ||
                             (_inputs != null && _inputs.sprint) ||
                             Input.GetKey(KeyCode.LeftShift);
            proceduralAnimator.HorizontalSpeed = _fpc.CurrentHorizontalSpeed;
            proceduralAnimator.IsGrounded = _fpc.Grounded;
            proceduralAnimator.IsCrouching = _fpc.IsCrouching;
            proceduralAnimator.IsSprinting = sprinting && !_fpc.IsCrouching;
        }
    }

    public void SetCrouch(bool crouch)
    {
        if (_fpc != null) _fpc.SetCrouch(crouch);
    }

    public void PlayJump()
    {
        if (_fpc != null) _fpc.TriggerJumpAnimation();
        if (proceduralAnimator != null) proceduralAnimator.TriggerJump();
    }

    public void PlayPickUp()
    {
        if (_fpc != null) _fpc.TriggerPickUpAnimation();
        if (proceduralAnimator != null) proceduralAnimator.TriggerPickUp();
        if (viewmodelBob != null) viewmodelBob.PlayPickUp();
    }

    public void PlayDrop()
    {
        if (_fpc != null) _fpc.TriggerDropAnimation();
        if (proceduralAnimator != null) proceduralAnimator.TriggerDrop();
        if (viewmodelBob != null) viewmodelBob.PlayDrop();
    }

    public void PlayInteract()
    {
        if (_fpc != null) _fpc.TriggerInteractAnimation();
        if (proceduralAnimator != null) proceduralAnimator.TriggerInteract();
    }

    public void PlayWave()
    {
        if (_fpc != null) _fpc.TriggerWaveAnimation();
    }

    public void SetCarrying(bool carrying)
    {
        if (_fpc != null) _fpc.SetCarryingState(carrying);
    }

    private void LateUpdate()
    {
        if (_cameraTarget != null && _headCameraRoot != null)
        {
            _cameraTarget.position = _headCameraRoot.position;
        }
    }
}
