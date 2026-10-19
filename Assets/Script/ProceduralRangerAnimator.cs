using System.Collections.Generic;
using UnityEngine;

public class ProceduralRangerAnimator : MonoBehaviour
{
    [Header("Safe Mode Toggle")]
    [Tooltip("When false, clean Mecanim humanoid animations play without procedural bone overrides.")]
    public bool useProcedural = false;

    [Header("Live Inputs (Driven by RangerAnimationDriver)")]
    public float HorizontalSpeed;
    public bool IsGrounded = true;
    public bool IsCrouching;
    public bool IsSprinting;

    [Header("Reference Speeds")]
    public float WalkSpeed = 4.0f;
    public float RunSpeed = 6.4f;

    private Animator _animator;
    private Transform _spine;
    private Transform _uaL, _laL;
    private Transform _uaR, _laR;
    private Transform _ulL, _llL;
    private Transform _ulR, _llR;

    private Quaternion _spineRestRot;
    private Quaternion _uaLRestRot, _laLRestRot;
    private Quaternion _uaRRestRot, _laRRestRot;
    private Quaternion _ulLRestRot, _llLRestRot;
    private Quaternion _ulRRestRot, _llRRestRot;

    private bool _initialized;
    private float _cyclePhase;

    private void Start()
    {
        InitializeBones();
    }

    private void InitializeBones()
    {
        if (_animator == null)
            _animator = GetComponent<Animator>();

        if (_animator == null)
        {
            enabled = false;
            return;
        }

        // Cache bone transforms and their rest localRotations once in Start
        _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
        _uaL   = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        _laL   = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        _uaR   = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        _laR   = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        _ulL   = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        _llL   = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        _ulR   = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        _llR   = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);

        if (_spine != null) _spineRestRot = _spine.localRotation;
        if (_uaL != null)   _uaLRestRot   = _uaL.localRotation;
        if (_laL != null)   _laLRestRot   = _laL.localRotation;
        if (_uaR != null)   _uaRRestRot   = _uaR.localRotation;
        if (_laR != null)   _laRRestRot   = _laR.localRotation;
        if (_ulL != null)   _ulLRestRot   = _ulL.localRotation;
        if (_llL != null)   _llLRestRot   = _llL.localRotation;
        if (_ulR != null)   _ulRRestRot   = _ulR.localRotation;
        if (_llR != null)   _llRRestRot   = _llR.localRotation;

        _initialized = true;
    }

    private void LateUpdate()
    {
        if (!useProcedural) return;

        if (!_initialized)
        {
            InitializeBones();
            if (!_initialized) return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Apply nothing when speed is 0, except for a tiny breathing sway
        float moveFactor = Mathf.Clamp01(HorizontalSpeed / Mathf.Max(0.1f, WalkSpeed));
        float breathSway = Mathf.Sin(Time.time * 2.0f) * 1.5f;

        if (moveFactor > 0.01f)
        {
            _cyclePhase += HorizontalSpeed * 3.0f * dt;
            if (_cyclePhase > Mathf.PI * 200f) _cyclePhase -= Mathf.PI * 200f;
        }

        float sinPhase = Mathf.Sin(_cyclePhase);

        // Clamp offsets: legs/arms max ±60°, spine max ±25°
        float spineOffset = Mathf.Clamp(breathSway + moveFactor * 3.0f, -25f, 25f);
        float armSwingL   = Mathf.Clamp( sinPhase * 30f * moveFactor, -60f, 60f);
        float armSwingR   = Mathf.Clamp(-sinPhase * 30f * moveFactor, -60f, 60f);
        float legSwingL   = Mathf.Clamp(-sinPhase * 35f * moveFactor, -60f, 60f);
        float legSwingR   = Mathf.Clamp( sinPhase * 35f * moveFactor, -60f, 60f);

        // Apply offsets strictly from cached restLocalRotation
        if (_spine != null) _spine.localRotation = _spineRestRot * Quaternion.Euler(spineOffset, 0f, 0f);
        if (_uaL != null)   _uaL.localRotation   = _uaLRestRot * Quaternion.Euler(armSwingL, 0f, 0f);
        if (_uaR != null)   _uaR.localRotation   = _uaRRestRot * Quaternion.Euler(armSwingR, 0f, 0f);
        if (_ulL != null)   _ulL.localRotation   = _ulLRestRot * Quaternion.Euler(legSwingL, 0f, 0f);
        if (_ulR != null)   _ulR.localRotation   = _ulRRestRot * Quaternion.Euler(legSwingR, 0f, 0f);
    }

    private Transform _handTargetL;
    private Transform _handTargetR;
    private Camera _mainCam;
    private float _carryBlend = 0f;
    private float _swayTimer = 0f;
    private Vector3 _currentHandR;
    private Vector3 _currentHandL;
    private Vector3 _handRVel;
    private Vector3 _handLVel;
    private bool _targetsInitialized;

    private void OnAnimatorIK(int layerIndex)
    {
        if (_animator == null) _animator = GetComponent<Animator>();
        if (_animator == null) return;

        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return;

        if (_handTargetL == null || _handTargetR == null)
        {
            _handTargetL = _mainCam.transform.Find("HandTarget_L");
            _handTargetR = _mainCam.transform.Find("HandTarget_R");
        }

        // Tilt spine & head with camera pitch
        _animator.SetLookAtWeight(1.0f, 0.45f, 0.70f, 0.50f, 0.50f);
        _animator.SetLookAtPosition(_mainCam.transform.position + _mainCam.transform.forward * 4.0f);

        if (_handTargetL == null || _handTargetR == null) return;

        // Dynamic walk and sprint sway
        float dt = Time.deltaTime;
        float moveFactor = Mathf.Clamp01(HorizontalSpeed / Mathf.Max(0.1f, WalkSpeed));
        float swayFreq = IsSprinting ? 12.0f : 8.0f;
        float swayAmp = IsSprinting ? 0.024f : (moveFactor > 0.05f ? 0.012f : 0.003f);
        _swayTimer += dt * swayFreq;

        Vector3 swayR = new Vector3(Mathf.Sin(_swayTimer) * swayAmp, Mathf.Cos(_swayTimer * 2f) * swayAmp * 0.6f, 0f);
        Vector3 swayL = new Vector3(-Mathf.Sin(_swayTimer) * swayAmp, Mathf.Cos(_swayTimer * 2f) * swayAmp * 0.6f, 0f);

        Vector3 idlePosR = _handTargetR.TransformPoint(swayR);
        Vector3 idlePosL = _handTargetL.TransformPoint(swayL);
        Quaternion idleRotR = _handTargetR.rotation;
        Quaternion idleRotL = _handTargetL.rotation;

        bool holding = DragAndDrop.CurrentlyGrabbedItem != null && DragAndDrop.CurrentlyGrabbedItem.IsGrabbed;
        _carryBlend = Mathf.MoveTowards(_carryBlend, holding ? 1.0f : 0.0f, dt / 0.18f);

        Vector3 targetPosR = idlePosR;
        Vector3 targetPosL = idlePosL;
        Quaternion targetRotR = idleRotR;
        Quaternion targetRotL = idleRotL;

        if (_carryBlend > 0.001f && DragAndDrop.CurrentlyGrabbedItem != null)
        {
            // Carrying pose: right hand grips the right/side, left hand supports left/bottom
            Vector3 carryCamR = new Vector3(0.10f, -0.035f, 0.24f);
            Vector3 carryCamL = new Vector3(0.01f, -0.045f, 0.24f);

            Vector3 carryWorldR = _mainCam.transform.TransformPoint(carryCamR);
            Vector3 carryWorldL = _mainCam.transform.TransformPoint(carryCamL);
            Quaternion carryRotR = _mainCam.transform.rotation * Quaternion.Euler(15f, -35f, 65f);
            Quaternion carryRotL = _mainCam.transform.rotation * Quaternion.Euler(20f, 35f, -65f);

            targetPosR = Vector3.Lerp(idlePosR, carryWorldR, _carryBlend);
            targetRotR = Quaternion.Slerp(idleRotR, carryRotR, _carryBlend);
            targetPosL = Vector3.Lerp(idlePosL, carryWorldL, _carryBlend);
            targetRotL = Quaternion.Slerp(idleRotL, carryRotL, _carryBlend);
        }

        Transform uaR = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform uaL = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);

        // Clamp targets within comfortable arm reach (max 0.39m from shoulder)
        if (uaR != null && Vector3.Distance(uaR.position, targetPosR) > 0.39f)
            targetPosR = uaR.position + (targetPosR - uaR.position).normalized * 0.39f;
        if (uaL != null && Vector3.Distance(uaL.position, targetPosL) > 0.39f)
            targetPosL = uaL.position + (targetPosL - uaL.position).normalized * 0.39f;

        if (!_targetsInitialized)
        {
            _currentHandR = targetPosR;
            _currentHandL = targetPosL;
            _targetsInitialized = true;
        }

        _currentHandR = Vector3.SmoothDamp(_currentHandR, targetPosR, ref _handRVel, 0.04f);
        _currentHandL = Vector3.SmoothDamp(_currentHandL, targetPosL, ref _handLVel, 0.04f);

        // Tuck elbows downward and inward so sleeves enter naturally from screen bottom
        Vector3 elbowHintR = (uaR != null ? uaR.position : transform.position) - _mainCam.transform.up * 0.30f - _mainCam.transform.right * 0.05f + _mainCam.transform.forward * 0.05f;
        Vector3 elbowHintL = (uaL != null ? uaL.position : transform.position) - _mainCam.transform.up * 0.30f + _mainCam.transform.right * 0.05f + _mainCam.transform.forward * 0.05f;

        // Apply IK smoothly blended with carry state
        _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, _carryBlend);
        _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, _carryBlend);
        _animator.SetIKPosition(AvatarIKGoal.RightHand, _currentHandR);
        _animator.SetIKRotation(AvatarIKGoal.RightHand, targetRotR);

        _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, _carryBlend);
        _animator.SetIKHintPosition(AvatarIKHint.RightElbow, elbowHintR);

        _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, _carryBlend);
        _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, _carryBlend);
        _animator.SetIKPosition(AvatarIKGoal.LeftHand, _currentHandL);
        _animator.SetIKRotation(AvatarIKGoal.LeftHand, targetRotL);

        _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, _carryBlend);
        _animator.SetIKHintPosition(AvatarIKHint.LeftElbow, elbowHintL);
    }

    public void TriggerJump() { }
    public void TriggerPickUp() { }
    public void TriggerDrop() { }
    public void TriggerInteract() { }
}
