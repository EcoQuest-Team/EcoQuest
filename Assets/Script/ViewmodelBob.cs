using System.Collections.Generic;
using UnityEngine;

public class ViewmodelBob : MonoBehaviour
{
    public static bool IsHoldingTrash { get; set; } = false;
    public static void SetHoldingTrash(bool holding) => IsHoldingTrash = holding;

    public CharacterController playerController;

    [Header("Base Placement")]
    public Vector3 baseLocalPosition = new Vector3(0f, 0.09f, 0.08f);
    public Vector3 baseLocalEuler = Vector3.zero;

    [Header("Bob Parameters")]
    public float idleSwayDegrees = 0.5f;
    public float idleFrequencyHz = 0.8f;
    public float walkBobVertical = 0.01f;
    public float walkBobHorizontal = 0.008f;
    public float walkBobFrequency = 8.5f;

    private float _bobTimer;
    private Vector3 _currentBobPos;
    private Vector3 _bobPosVel;

    private float _pickUpTimer = -1f;
    private float _dropTimer = -1f;
    private float _interactTimer = -1f;

    private readonly List<Transform> _posedBones = new List<Transform>();
    private readonly List<Quaternion> _targetRotations = new List<Quaternion>();
    private readonly List<Quaternion> _carryRotations = new List<Quaternion>();
    private readonly HashSet<Transform> _armBones = new HashSet<Transform>();
    private float _carryBlend = 0f;
    private float _ikWeight = 0f;
    private Animator _animator;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        if (playerController == null)
        {
            playerController = GetComponentInParent<CharacterController>();
            if (playerController == null)
                playerController = Object.FindAnyObjectByType<CharacterController>();
        }

        transform.localPosition = baseLocalPosition;
        transform.localRotation = Quaternion.Euler(baseLocalEuler);

        InitializeBonePoses();
    }

    private void InitializeBonePoses()
    {
        _posedBones.Clear();
        _targetRotations.Clear();
        _carryRotations.Clear();
        _armBones.Clear();

        Transform armRoot = transform.Find("FP_Armature/Root") ?? transform.Find("Armature/Hips/Spine/Chest") ?? transform.Find("Armature/Root");
        if (armRoot == null) return;

        bool isFPArms = transform.Find("FP_Armature") != null;

        // 1. UpperArms & Elbows
        Transform uaL = armRoot.Find("Shoulder.L/UpperArm.L");
        Transform uaR = armRoot.Find("Shoulder.R/UpperArm.R");
        if (uaL != null)
        {
            _armBones.Add(uaL);
            Quaternion rest = uaL.localRotation;
            Quaternion carry = isFPArms ? rest * Quaternion.Euler(6f, 10f, -4f) : rest * Quaternion.Euler(18f, 0f, -25f);
            RegisterBone(uaL, rest, carry);
        }
        if (uaR != null)
        {
            _armBones.Add(uaR);
            Quaternion rest = uaR.localRotation;
            Quaternion carry = isFPArms ? rest * Quaternion.Euler(6f, -10f, 4f) : rest * Quaternion.Euler(18f, 0f, 25f);
            RegisterBone(uaR, rest, carry);
        }

        Transform laL = uaL != null ? uaL.Find("LowerArm.L") : null;
        Transform laR = uaR != null ? uaR.Find("LowerArm.R") : null;
        if (laL != null)
        {
            _armBones.Add(laL);
            Quaternion rest = laL.localRotation;
            Quaternion carry = isFPArms ? rest * Quaternion.Euler(-14f, 4f, 0f) : rest * Quaternion.Euler(-28f, 0f, 0f);
            RegisterBone(laL, rest, carry);
        }
        if (laR != null)
        {
            _armBones.Add(laR);
            Quaternion rest = laR.localRotation;
            Quaternion carry = isFPArms ? rest * Quaternion.Euler(-14f, -4f, 0f) : rest * Quaternion.Euler(-28f, 0f, 0f);
            RegisterBone(laR, rest, carry);
        }

        // 2. Wrists
        Transform hL = laL != null ? laL.Find("Hand.L") : null;
        Transform hR = laR != null ? laR.Find("Hand.R") : null;
        if (hL != null)
        {
            _armBones.Add(hL);
            Quaternion rest = hL.localRotation;
            Quaternion carry = isFPArms ? rest * Quaternion.Euler(8f, 16f, -4f) : rest * Quaternion.Euler(0f, 15f, -12f);
            RegisterBone(hL, rest, carry);
        }
        if (hR != null)
        {
            _armBones.Add(hR);
            Quaternion rest = hR.localRotation;
            Quaternion carry = isFPArms ? rest * Quaternion.Euler(8f, -16f, 4f) : rest * Quaternion.Euler(0f, -15f, 12f);
            RegisterBone(hR, rest, carry);
        }

        // 3. Fingers (Supports both individual fingers and 2-block finger rigs)
        string[] fingerPrefixes = new string[] { "Index", "Middle", "Ring", "Pinky" };
        foreach (var hand in new Transform[] { hL, hR })
        {
            if (hand == null) continue;
            string side = hand == hL ? "L" : "R";

            // Two-block finger rig support
            Transform fIM = hand.Find("Finger_IndexMid." + side);
            if (fIM != null)
            {
                Quaternion rIM = fIM.localRotation;
                RegisterBone(fIM, rIM, rIM * Quaternion.Euler(18f, 0f, 0f));
            }
            Transform fRP = hand.Find("Finger_RingPinky." + side);
            if (fRP != null)
            {
                Quaternion rRP = fRP.localRotation;
                RegisterBone(fRP, rRP, rRP * Quaternion.Euler(20f, 0f, 0f));
            }
            Transform th = hand.Find("Thumb." + side);
            if (th != null)
            {
                Quaternion rTh = th.localRotation;
                RegisterBone(th, rTh, rTh * Quaternion.Euler(14f, 0f, 0f));
            }

            foreach (var f in fingerPrefixes)
            {
                Transform j1 = hand.Find(f + "1." + side);
                if (j1 != null)
                {
                    Quaternion r1 = j1.localRotation;
                    RegisterBone(j1, r1, r1 * Quaternion.Euler(14f, 0f, 0f));
                    Transform j2 = j1.Find(f + "2." + side);
                    if (j2 != null)
                    {
                        Quaternion r2 = j2.localRotation;
                        RegisterBone(j2, r2, r2 * Quaternion.Euler(16f, 0f, 0f));
                        Transform j3 = j2.Find(f + "3." + side);
                        if (j3 != null)
                        {
                            Quaternion r3 = j3.localRotation;
                            RegisterBone(j3, r3, r3 * Quaternion.Euler(10f, 0f, 0f));
                        }
                    }
                }
            }

            Transform t1 = hand.Find("Thumb1." + side);
            if (t1 != null)
            {
                Quaternion rt1 = t1.localRotation;
                RegisterBone(t1, rt1, rt1 * Quaternion.Euler(10f, 0f, 0f));
                Transform t2 = t1.Find("Thumb2." + side);
                if (t2 != null)
                {
                    Quaternion rt2 = t2.localRotation;
                    RegisterBone(t2, rt2, rt2 * Quaternion.Euler(8f, 0f, 0f));
                    Transform t3 = t2.Find("Thumb3." + side);
                    if (t3 != null)
                    {
                        Quaternion rt3 = t3.localRotation;
                        RegisterBone(t3, rt3, rt3 * Quaternion.Euler(6f, 0f, 0f));
                    }
                }
            }
        }

    }

    private void RegisterBone(Transform bone, Quaternion targetRot, Quaternion carryRot)
    {
        if (bone != null)
        {
            _posedBones.Add(bone);
            _targetRotations.Add(targetRot);
            _carryRotations.Add(carryRot);
        }
    }

    public void PlayPickUp() => _pickUpTimer = 0f;
    public void PlayDrop() => _dropTimer = 0f;
    public void PlayInteract() => _interactTimer = 0f;

    private void OnAnimatorIK(int layerIndex)
    {
        if (_animator == null) _animator = GetComponent<Animator>();
        if (_animator == null) return;

        bool holding = IsHoldingTrash && DragAndDrop.CurrentlyGrabbedItem != null && DragAndDrop.CurrentlyGrabbedItem.IsGrabbed;
        float targetWeight = holding ? 1.0f : 0.0f;
        _ikWeight = Mathf.MoveTowards(_ikWeight, targetWeight, Time.deltaTime / 0.18f);

        if (_ikWeight <= 0.001f)
        {
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
            _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
            return;
        }

        var item = DragAndDrop.CurrentlyGrabbedItem;
        if (item == null) return;

        Bounds itemBounds = item.CurrentBounds;
        Vector3 center = itemBounds.size.sqrMagnitude > 0.0001f ? itemBounds.center : item.transform.position;
        Vector3 extents = itemBounds.size.sqrMagnitude > 0.0001f ? itemBounds.extents : new Vector3(0.12f, 0.12f, 0.12f);
        bool isSmall = item.CurrentSizeCategory == HoldSizeCategory.Small;

        // Grip points locked to item collider bounds
        Vector3 rightGrip = center + item.transform.right * (extents.x * 0.85f) - item.transform.up * (extents.y * 0.20f);
        Quaternion rightRot = item.transform.rotation * Quaternion.Euler(0f, -80f, 0f);

        Transform uaR = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform uaL = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Vector3 elbowHintR = (uaR != null ? uaR.position : transform.position) - transform.up * 0.35f + transform.right * 0.20f - transform.forward * 0.10f;
        Vector3 elbowHintL = (uaL != null ? uaL.position : transform.position) - transform.up * 0.35f - transform.right * 0.20f - transform.forward * 0.10f;

        _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, _ikWeight);
        _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, _ikWeight);
        _animator.SetIKPosition(AvatarIKGoal.RightHand, rightGrip);
        _animator.SetIKRotation(AvatarIKGoal.RightHand, rightRot);

        _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, _ikWeight);
        _animator.SetIKHintPosition(AvatarIKHint.RightElbow, elbowHintR);

        if (!isSmall)
        {
            Vector3 leftGrip = center - item.transform.right * (extents.x * 0.85f) - item.transform.up * (extents.y * 0.20f);
            Quaternion leftRot = item.transform.rotation * Quaternion.Euler(0f, 80f, 0f);

            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, _ikWeight);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, _ikWeight);
            _animator.SetIKPosition(AvatarIKGoal.LeftHand, leftGrip);
            _animator.SetIKRotation(AvatarIKGoal.LeftHand, leftRot);

            _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, _ikWeight);
            _animator.SetIKHintPosition(AvatarIKHint.LeftElbow, elbowHintL);
        }
        else
        {
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
        }
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Smoothly blend carry pose
        float targetBlend = IsHoldingTrash ? 1.0f : 0.0f;
        _carryBlend = Mathf.MoveTowards(_carryBlend, targetBlend, dt * 5.0f);

        for (int i = 0; i < _posedBones.Count; i++)
        {
            Transform bone = _posedBones[i];
            if (bone != null)
            {
                // When IK is active on arm bones, let Animator IK handle UpperArm/LowerArm/Hand
                if (_ikWeight > 0.01f && _armBones.Contains(bone))
                {
                    continue;
                }
                bone.localRotation = Quaternion.Slerp(_targetRotations[i], _carryRotations[i], _carryBlend);
            }
        }

        // Measure real player movement speed
        float planarSpeed = 0f;
        if (playerController != null)
        {
            Vector3 v = playerController.velocity;
            planarSpeed = new Vector2(v.x, v.z).magnitude;
        }

        // 1. Idle breathing sway
        float idleAngle = Mathf.Sin(Time.time * (2f * Mathf.PI * idleFrequencyHz)) * idleSwayDegrees;

        // 2. Walking & running bob
        float speedFactor = Mathf.Clamp01(planarSpeed / 2.0f);
        float runT = Mathf.Clamp01((planarSpeed - 2.0f) / 3.8f);
        float runMultiplier = Mathf.Lerp(1.0f, 1.8f, runT);
        float freqMultiplier = Mathf.Lerp(1.0f, 1.4f, runT);

        Vector3 targetBob = Vector3.zero;
        if (speedFactor > 0.02f)
        {
            _bobTimer += dt * walkBobFrequency * freqMultiplier;
            float bobX = Mathf.Cos(_bobTimer * 0.5f) * walkBobHorizontal * speedFactor * runMultiplier;
            float bobY = Mathf.Sin(_bobTimer) * walkBobVertical * speedFactor * runMultiplier;
            targetBob = new Vector3(bobX, bobY, 0f);
        }

        _currentBobPos = Vector3.SmoothDamp(_currentBobPos, targetBob, ref _bobPosVel, 0.08f);

        // Action offsets
        Vector3 actionPos = Vector3.zero;
        Vector3 actionEuler = Vector3.zero;

        if (_pickUpTimer >= 0f)
        {
            _pickUpTimer += dt;
            if (_pickUpTimer >= 0.8f) _pickUpTimer = -1f;
            else
            {
                float w = Mathf.Sin((_pickUpTimer / 0.8f) * Mathf.PI);
                actionPos += new Vector3(0f, -0.04f * w, 0.06f * w);
                actionEuler += new Vector3(14f * w, 0f, 0f);
            }
        }

        if (_dropTimer >= 0f)
        {
            _dropTimer += dt;
            if (_dropTimer >= 0.5f) _dropTimer = -1f;
            else
            {
                float w = Mathf.Sin((_dropTimer / 0.5f) * Mathf.PI);
                actionPos += new Vector3(0f, 0.02f * w, 0.08f * w);
                actionEuler += new Vector3(-12f * w, 0f, 0f);
            }
        }

        if (_interactTimer >= 0f)
        {
            _interactTimer += dt;
            if (_interactTimer >= 0.6f) _interactTimer = -1f;
            else
            {
                float w = Mathf.Sin((_interactTimer / 0.6f) * Mathf.PI);
                actionPos += new Vector3(0f, 0.03f * w, 0.05f * w);
                actionEuler += new Vector3(-15f * w, 0f, 0f);
            }
        }

        // Reach shift toward held item when carrying
        Vector3 carryPosOffset = Vector3.Lerp(Vector3.zero, new Vector3(0.01f, 0.04f, 0.05f), _carryBlend);
        Vector3 carryEulerOffset = Vector3.Lerp(Vector3.zero, new Vector3(-2f, 2f, -1f), _carryBlend);

        transform.localPosition = baseLocalPosition + _currentBobPos + actionPos + carryPosOffset;
        transform.localRotation = Quaternion.Euler(baseLocalEuler + carryEulerOffset) * Quaternion.Euler(idleAngle, 0f, 0f) * Quaternion.Euler(actionEuler);
    }
}
