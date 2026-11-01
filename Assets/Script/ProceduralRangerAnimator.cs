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

        // Subtle head-only LookAt (low bodyWeight keeps spine stable)
        _animator.SetLookAtWeight(0.25f, 0.03f, 0.50f, 0.25f, 0.50f);
        _animator.SetLookAtPosition(_mainCam.transform.position + _mainCam.transform.forward * 4.0f);

        if (layerIndex != 0) return;

        float dt = Time.deltaTime;
        bool holding = (DragAndDrop.CurrentlyGrabbedItem != null && DragAndDrop.CurrentlyGrabbedItem.IsGrabbed) || ItemCarry.ActiveCarriedItem != null;
        _carryBlend = Mathf.MoveTowards(_carryBlend, holding ? 1.0f : 0.0f, dt / 0.16f);

        if (_carryBlend <= 0.001f)
        {
            _targetsInitialized = false;
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
            return;
        }

        Vector3 surfL, surfR;
        if (!DragAndDrop.TryGetHeldGrabPoints(_mainCam.transform, out surfL, out surfR))
        {
            Vector3 center = _mainCam.transform.position + _mainCam.transform.forward * 0.42f - _mainCam.transform.up * 0.10f;
            surfL = center - _mainCam.transform.right * 0.055f;
            surfR = center + _mainCam.transform.right * 0.055f;
        }

        Vector3 targetPosL = surfL - _mainCam.transform.forward * 0.045f - _mainCam.transform.right * 0.018f - _mainCam.transform.up * 0.008f;
        Vector3 targetPosR = surfR - _mainCam.transform.forward * 0.045f + _mainCam.transform.right * 0.018f - _mainCam.transform.up * 0.008f;

        float moveFactor = Mathf.Clamp01(HorizontalSpeed / Mathf.Max(0.1f, WalkSpeed));
        float swayFreq = IsSprinting ? 12.0f : 8.0f;
        float swayAmp = IsSprinting ? 0.014f : (moveFactor > 0.05f ? 0.008f : 0.002f);
        _swayTimer += dt * swayFreq;

        Vector3 swayR = new Vector3(Mathf.Sin(_swayTimer) * swayAmp, Mathf.Cos(_swayTimer * 2f) * swayAmp * 0.6f, 0f);
        Vector3 swayL = new Vector3(-Mathf.Sin(_swayTimer) * swayAmp, Mathf.Cos(_swayTimer * 2f) * swayAmp * 0.6f, 0f);
        targetPosR += _mainCam.transform.TransformVector(swayR);
        targetPosL += _mainCam.transform.TransformVector(swayL);

        Transform uaR = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform uaL = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);

        // Clamp targets within comfortable reach (max 0.60m from shoulder)
        if (uaR != null && Vector3.Distance(uaR.position, targetPosR) > 0.60f)
            targetPosR = uaR.position + (targetPosR - uaR.position).normalized * 0.60f;
        if (uaL != null && Vector3.Distance(uaL.position, targetPosL) > 0.60f)
            targetPosL = uaL.position + (targetPosL - uaL.position).normalized * 0.60f;

        if (!_targetsInitialized)
        {
            _currentHandR = targetPosR;
            _currentHandL = targetPosL;
            _targetsInitialized = true;
        }
        else
        {
            _currentHandR = Vector3.SmoothDamp(_currentHandR, targetPosR, ref _handRVel, 0.035f);
            _currentHandL = Vector3.SmoothDamp(_currentHandL, targetPosL, ref _handLVel, 0.035f);
        }

        Quaternion rotL = _mainCam.transform.rotation * Quaternion.Euler(0f, 12f, 100f);
        Quaternion rotR = _mainCam.transform.rotation * Quaternion.Euler(0f, -12f, -100f);

        Vector3 elbowHintR = (uaR != null ? uaR.position : transform.position) - _mainCam.transform.up * 0.22f + _mainCam.transform.right * 0.08f;
        Vector3 elbowHintL = (uaL != null ? uaL.position : transform.position) - _mainCam.transform.up * 0.22f - _mainCam.transform.right * 0.08f;

        _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, _carryBlend);
        _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, _carryBlend);
        _animator.SetIKPosition(AvatarIKGoal.RightHand, _currentHandR);
        _animator.SetIKRotation(AvatarIKGoal.RightHand, rotR);
        _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, _carryBlend);
        _animator.SetIKHintPosition(AvatarIKHint.RightElbow, elbowHintR);

        _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, _carryBlend);
        _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, _carryBlend);
        _animator.SetIKPosition(AvatarIKGoal.LeftHand, _currentHandL);
        _animator.SetIKRotation(AvatarIKGoal.LeftHand, rotL);
        _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, _carryBlend);
        _animator.SetIKHintPosition(AvatarIKHint.LeftElbow, elbowHintL);
    }

    public void TriggerJump() { }
    public void TriggerPickUp() { }
    public void TriggerDrop() { }
    public void TriggerInteract() { }
}
