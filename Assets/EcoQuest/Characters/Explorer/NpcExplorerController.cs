using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Runtime controller for NPC_Explorer and its palette variants (Explorer_Var1..Var5).
/// Drives locomotion Speed from NavMeshAgent, grounded/jump parameters, random IdleLookAround,
/// periodic "Blink" blendshape every 3-6s, head look-at IK, backpack visibility toggle,
/// and null-safe OnFootstep / OnThrowRelease animation events.
/// </summary>
[ExecuteAlways]
public class NpcExplorerController : MonoBehaviour
{
    [Header("Appearance & Variant Options")]
    [Tooltip("Toggle to show or hide the Explorer_Backpack mesh on this instance or variant.")]
    [SerializeField] private bool showBackpack = true;
    [SerializeField] private GameObject backpackObject;
    [SerializeField] private SkinnedMeshRenderer bodyRenderer;

    [Header("Navigation & Locomotion")]
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent navAgent;
    [SerializeField] private bool driveSpeedFromAgent = true;
    [SerializeField] private float walkSpeed = 1.4f;
    [SerializeField] private float runSpeed = 3.5f;
    [SerializeField] private float speedDampTime = 0.10f;

    [Header("Grounded & Idle Variation")]
    [SerializeField] private bool autoGroundCheck = true;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private Vector2 idleLookIntervalRange = new Vector2(8f, 15f);

    [Header("Blink Shape Key")]
    [SerializeField] private string blinkBlendShapeName = "Blink";
    [SerializeField] private Vector2 blinkIntervalRange = new Vector2(3f, 6f);
    [SerializeField] private float blinkDuration = 0.16f;

    [Header("Head Look-At IK")]
    [SerializeField] private bool enableLookAtIK = true;
    [SerializeField] private Transform lookAtTarget;
    [SerializeField] private bool autoLookAtNearbyPlayer = true;
    [SerializeField] private float lookAtRadius = 4.5f;
    [Range(0f, 1f)]
    [SerializeField] private float maxLookAtWeight = 0.75f;
    [SerializeField] private float targetEyeHeightOffset = 1.52f;

    [Header("Audio & Events (Null-Safe)")]
    [SerializeField] private AudioSource footstepAudioSource;
    [SerializeField] private AudioClip[] footstepClips;
    [Range(0f, 1f)]
    [SerializeField] private float footstepVolume = 0.55f;
    public UnityEvent onThrowReleased;
    public event Action ThrowReleased;

    private int blinkShapeIndex = -1;
    private float nextBlinkTimer;
    private float blinkElapsed = -1f;
    private float nextIdleLookTimer;
    private bool isGrounded = true;
    private float jumpAirGraceTimer;
    private float currentLookWeight;
    private Vector3 smoothedLookPos;
    private Transform cachedPlayer;

    private static readonly int HashSpeed = Animator.StringToHash("Speed");
    private static readonly int HashIsGrounded = Animator.StringToHash("IsGrounded");
    private static readonly int HashJump = Animator.StringToHash("Jump");
    private static readonly int HashTalk = Animator.StringToHash("Talk");
    private static readonly int HashIsTalking = Animator.StringToHash("IsTalking");
    private static readonly int HashTalkIndex = Animator.StringToHash("TalkIndex");
    private static readonly int HashListen = Animator.StringToHash("Listen");
    private static readonly int HashIdleLookAround = Animator.StringToHash("IdleLookAround");

    public float WalkSpeed => walkSpeed;
    public float RunSpeed => runSpeed;
    public bool ShowBackpack => showBackpack;

    private void Awake()
    {
        CacheReferences();
        ApplyBackpackVisibility();
        if (Application.isPlaying)
        {
            ScheduleNextBlink();
            ScheduleNextIdleLook();
        }
    }

    private void OnValidate()
    {
        CacheReferences();
        ApplyBackpackVisibility();
    }

    private void CacheReferences()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator != null)
        {
            if (animator.applyRootMotion) animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
        if (navAgent != null && Application.isPlaying && !navAgent.isOnNavMesh)
        {
            // Explorer is placed in a non-navmesh dialogue area; start agent disabled to prevent NavMesh warning
            navAgent.enabled = false;
        }
        if (footstepAudioSource == null) footstepAudioSource = GetComponent<AudioSource>();

        if (bodyRenderer == null || backpackObject == null)
        {
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (bodyRenderer == null && r.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0)
                    bodyRenderer = r;
                if (backpackObject == null && r.name.IndexOf("Backpack", StringComparison.OrdinalIgnoreCase) >= 0)
                    backpackObject = r.gameObject;
            }
        }

        blinkShapeIndex = -1;
        if (bodyRenderer != null && bodyRenderer.sharedMesh != null)
        {
            blinkShapeIndex = bodyRenderer.sharedMesh.GetBlendShapeIndex(blinkBlendShapeName);
        }
    }

    public void SetBackpackVisible(bool visible)
    {
        showBackpack = visible;
        ApplyBackpackVisibility();
    }

    private void ApplyBackpackVisibility()
    {
        if (backpackObject != null && backpackObject.activeSelf != showBackpack)
        {
            backpackObject.SetActive(showBackpack);
        }
    }

    private void Update()
    {
        if (!Application.isPlaying) return;

        float dt = Time.deltaTime;
        UpdateLocomotionAndGrounding(dt);
        UpdateIdleVariation(dt);
        UpdateBlink(dt);
        UpdateLookAtTarget(dt);
    }

    private void UpdateLocomotionAndGrounding(float dt)
    {
        if (animator == null) return;

        if (jumpAirGraceTimer > 0f)
        {
            jumpAirGraceTimer -= dt;
            isGrounded = false;
        }
        else if (autoGroundCheck)
        {
            Vector3 origin = transform.position + Vector3.up * 0.25f;
            isGrounded = Physics.Raycast(origin, Vector3.down, 0.40f, groundLayers, QueryTriggerInteraction.Ignore);
        }

        animator.SetBool(HashIsGrounded, isGrounded);

        // Only drive Speed automatically if NavMeshAgent is actively moving the transform
        // (allows NpcBrain / NpcWalker to take over seamlessly if attached).
        if (driveSpeedFromAgent && navAgent != null && navAgent.isActiveAndEnabled && navAgent.updatePosition && navAgent.isOnNavMesh)
        {
            float spd = navAgent.isStopped ? 0f : navAgent.velocity.magnitude;
            animator.SetFloat(HashSpeed, spd, speedDampTime, dt);
        }
    }

    private void UpdateIdleVariation(float dt)
    {
        if (animator == null) return;

        bool standingCalm = isGrounded
            && animator.GetFloat(HashSpeed) < 0.08f
            && !animator.GetBool(HashTalk)
            && !animator.GetBool(HashIsTalking)
            && !animator.GetBool(HashListen);

        if (!standingCalm)
        {
            return;
        }

        nextIdleLookTimer -= dt;
        if (nextIdleLookTimer <= 0f)
        {
            animator.SetTrigger(HashIdleLookAround);
            ScheduleNextIdleLook();
        }
    }

    private void UpdateBlink(float dt)
    {
        if (bodyRenderer == null || blinkShapeIndex < 0) return;

        if (blinkElapsed >= 0f)
        {
            blinkElapsed += dt;
            float half = Mathf.Max(0.04f, blinkDuration * 0.5f);
            float weight;
            if (blinkElapsed < half)
                weight = (blinkElapsed / half) * 100f;
            else if (blinkElapsed < blinkDuration)
                weight = (1f - (blinkElapsed - half) / half) * 100f;
            else
            {
                weight = 0f;
                blinkElapsed = -1f;
                ScheduleNextBlink();
            }
            bodyRenderer.SetBlendShapeWeight(blinkShapeIndex, Mathf.Clamp(weight, 0f, 100f));
        }
        else
        {
            nextBlinkTimer -= dt;
            if (nextBlinkTimer <= 0f)
            {
                blinkElapsed = 0f;
            }
        }
    }

    private void UpdateLookAtTarget(float dt)
    {
        if (!enableLookAtIK)
        {
            currentLookWeight = Mathf.MoveTowards(currentLookWeight, 0f, dt * 3f);
            return;
        }

        Transform target = lookAtTarget;
        if (target == null && autoLookAtNearbyPlayer)
        {
            if (cachedPlayer == null)
            {
                var pObj = GameObject.FindGameObjectWithTag("Player");
                if (pObj != null) cachedPlayer = pObj.transform;
            }
            target = cachedPlayer;
        }

        float desiredWeight = 0f;
        if (target != null)
        {
            Vector3 desiredPos = (lookAtTarget != null) ? target.position : (target.position + Vector3.up * targetEyeHeightOffset);
            Vector3 toTarget = desiredPos - (transform.position + Vector3.up * 1.50f);
            float sqrDist = toTarget.sqrMagnitude;
            if (sqrDist <= lookAtRadius * lookAtRadius && sqrDist > 0.04f)
            {
                float fwdDot = Vector3.Dot(transform.forward, toTarget.normalized);
                if (fwdDot > 0.20f)
                {
                    desiredWeight = maxLookAtWeight;
                    smoothedLookPos = (currentLookWeight < 0.02f)
                        ? desiredPos
                        : Vector3.Lerp(smoothedLookPos, desiredPos, dt * 8f);
                }
            }
        }

        currentLookWeight = Mathf.MoveTowards(currentLookWeight, desiredWeight, dt * 3.5f);
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || animator == null) return;
        if (currentLookWeight > 0.001f)
        {
            animator.SetLookAtWeight(currentLookWeight, 0.15f, 0.75f, 0.90f, 0.55f);
            animator.SetLookAtPosition(smoothedLookPos);
        }
        else
        {
            animator.SetLookAtWeight(0f);
        }
    }

    private void ScheduleNextBlink()
    {
        float lo = Mathf.Min(blinkIntervalRange.x, blinkIntervalRange.y);
        float hi = Mathf.Max(blinkIntervalRange.x, blinkIntervalRange.y);
        nextBlinkTimer = UnityEngine.Random.Range(Mathf.Max(0.5f, lo), Mathf.Max(1f, hi));
    }

    private void ScheduleNextIdleLook()
    {
        float lo = Mathf.Min(idleLookIntervalRange.x, idleLookIntervalRange.y);
        float hi = Mathf.Max(idleLookIntervalRange.x, idleLookIntervalRange.y);
        nextIdleLookTimer = UnityEngine.Random.Range(Mathf.Max(2f, lo), Mathf.Max(3f, hi));
    }

    // -----------------------------------------------------------------------
    // Public Control API
    // -----------------------------------------------------------------------

    public void SetMoveMode(bool running)
    {
        if (navAgent != null)
        {
            if (!navAgent.enabled && navAgent.isOnNavMesh) navAgent.enabled = true;
            if (navAgent.enabled) navAgent.speed = running ? runSpeed : walkSpeed;
        }
    }

    public void PlayJump(float airborneDuration = 0.55f)
    {
        if (animator == null) return;
        jumpAirGraceTimer = Mathf.Max(0.15f, airborneDuration);
        isGrounded = false;
        animator.SetBool(HashIsGrounded, false);
        animator.SetTrigger(HashJump);
    }

    public void SetGrounded(bool grounded)
    {
        autoGroundCheck = false;
        isGrounded = grounded;
        if (animator != null) animator.SetBool(HashIsGrounded, grounded);
    }

    public void PlayTalk(int talkIndex = 0)
    {
        if (animator == null) return;
        animator.SetInteger(HashTalkIndex, Mathf.Clamp(talkIndex, 0, 1));
        animator.SetBool(HashTalk, true);
        animator.SetBool(HashIsTalking, true);
    }

    public void StopTalk()
    {
        if (animator == null) return;
        animator.SetBool(HashTalk, false);
        animator.SetBool(HashIsTalking, false);
    }

    public void SetListen(bool listening)
    {
        if (animator != null) animator.SetBool(HashListen, listening);
    }

    public void PlayWave()
    {
        PlayReaction("Wave");
    }

    public void PlayThrow()
    {
        PlayReaction("Throw");
    }

    public void PlayReaction(string reactionName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(reactionName)) return;
        string trimmed = reactionName.Trim();

        if (string.Equals(trimmed, "StopTalk", StringComparison.OrdinalIgnoreCase))
        {
            StopTalk();
            return;
        }
        if (string.Equals(trimmed, "Listen", StringComparison.OrdinalIgnoreCase))
        {
            SetListen(true);
            return;
        }

        // Match trigger case-insensitively against valid Explorer reaction triggers
        string[] validTriggers = { "Wave", "Nod", "ShakeHead", "Shrug", "Surprised", "Cheer", "Throw", "PickUp", "Jump" };
        foreach (string trig in validTriggers)
        {
            if (string.Equals(trimmed, trig, StringComparison.OrdinalIgnoreCase))
            {
                animator.SetTrigger(trig);
                return;
            }
        }
    }

    public void SetLookAtTarget(Transform target)
    {
        lookAtTarget = target;
    }

    // -----------------------------------------------------------------------
    // Null-Safe Animation Event Receivers (wired to FootL / FootR / Release)
    // -----------------------------------------------------------------------

    public void OnFootstep(string footName)
    {
        if (footstepAudioSource == null || footstepClips == null || footstepClips.Length == 0)
            return;

        AudioClip clip = footstepClips[UnityEngine.Random.Range(0, footstepClips.Length)];
        if (clip != null)
        {
            footstepAudioSource.PlayOneShot(clip, footstepVolume);
        }
    }

    public void OnThrowRelease(string markerName)
    {
        ThrowReleased?.Invoke();
        onThrowReleased?.Invoke();
    }
}
