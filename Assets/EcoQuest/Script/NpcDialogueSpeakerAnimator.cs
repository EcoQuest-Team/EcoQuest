using System.Collections;
using UnityEngine;

/// <summary>
/// Controls NPC dialogue animation: yaw-only rotation toward player (0.4s ease),
/// eye-level look-at with clamped yaw/pitch (Animator IK or safe procedural fallback),
/// non-accumulating procedural talking nods (clamped +-8 deg), and safety watchdog.
/// </summary>
public class NpcDialogueSpeakerAnimator : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private Animator animator;

    [Header("Tuning")]
    [SerializeField] private float maxLookWeight = 0.8f;
    [SerializeField] private float yawEaseDuration = 0.4f;

    private Transform playerTarget;
    private Transform headBone;
    private Transform neckBone;
    private Transform chestBone;
    private Transform leftUpperArm;
    private Transform rightUpperArm;

    private Quaternion baseHeadLocalRot;
    private Quaternion baseChestLocalRot;
    private Quaternion baseLeftArmLocalRot;
    private Quaternion baseRightArmLocalRot;
    private bool hasCachedBasePose = false;

    private bool isSpeaking = false;
    private bool isTyping = false;
    private bool isGuide = false;

    private float currentLookWeight = 0f;
    private float targetLookWeight = 0f;
    private Vector3 smoothedLookTarget;

    private Quaternion initialYaw;
    private Quaternion targetYaw;
    private float yawTimer = 0f;
    private bool isYawEasing = false;

    private int activeReaction = 0; // 0=none, 1=defensive, 2=surprised, 3=grateful
    private float reactionTimer = 0f;
    private float punctuationNodTimer = 0f;
    private float pitchWatchdogTimer = 0f;

    public bool UsesProceduralFallback => animator == null || !animator.isHuman;
    public float CurrentLookWeight => currentLookWeight;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        CacheBones();
    }

    private void CacheBones()
    {
        if (animator != null && animator.isHuman)
        {
            headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
            chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);
            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);

            if (headBone != null && !hasCachedBasePose)
            {
                baseHeadLocalRot = headBone.localRotation;
                if (chestBone != null) baseChestLocalRot = chestBone.localRotation;
                if (leftUpperArm != null) baseLeftArmLocalRot = leftUpperArm.localRotation;
                if (rightUpperArm != null) baseRightArmLocalRot = rightUpperArm.localRotation;
                hasCachedBasePose = true;
            }
        }
    }

    /// <summary>
    /// Starts dialogue engagement: yaw-only ease to player, fades in eye-level look-at.
    /// targetPlayer should be player root or character (not dialogue camera).
    /// </summary>
    public void BeginDialogue(Transform targetPlayer, bool guideNpc)
    {
        CacheBones();
        playerTarget = targetPlayer;
        isGuide = guideNpc;
        isSpeaking = true;
        isTyping = false;
        activeReaction = 0;
        reactionTimer = 0f;
        pitchWatchdogTimer = 0f;

        if (animator != null)
        {
            animator.SetFloat("Speed", 0f);
        }

        if (playerTarget != null)
        {
            // Body turn: Yaw ONLY (pitch is strictly 0)
            Vector3 toPlayer = (playerTarget.position - transform.position);
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f)
            {
                initialYaw = transform.rotation;
                targetYaw = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                yawTimer = 0f;
                isYawEasing = true;
            }

            smoothedLookTarget = playerTarget.position + Vector3.up * 1.55f;
        }

        targetLookWeight = maxLookWeight;

        if (isGuide && animator != null)
        {
            // Guide welcoming wave on line 1
            animator.SetInteger("ActionType", 1);
            animator.SetTrigger("ActionTrigger");
        }
    }

    public void SetTyping(bool typing)
    {
        isTyping = typing;
        if (animator != null)
        {
            animator.SetBool("IsTalking", typing);
        }
    }

    public void TriggerPunctuationNod()
    {
        punctuationNodTimer = 0.16f;
    }

    public void TriggerReactionBeat(int lineIndex)
    {
        if (isGuide)
        {
            if (lineIndex == 0 && animator != null)
            {
                animator.SetInteger("ActionType", 1);
                animator.SetTrigger("ActionTrigger");
            }
            return;
        }

        if (lineIndex == 0 || lineIndex == 1)
        {
            activeReaction = 1; // Defensive
            reactionTimer = 1.6f;
        }
        else if (lineIndex == 2 || lineIndex == 3)
        {
            activeReaction = 2; // Surprised
            reactionTimer = 1.6f;
        }
        else if (lineIndex >= 4)
        {
            activeReaction = 3; // Grateful
            reactionTimer = 1.8f;
            if (animator != null)
            {
                animator.SetInteger("ActionType", 2);
                animator.SetTrigger("ActionTrigger");
            }
        }
    }

    public void EndDialogue(bool accepted = false)
    {
        ResetSafetyState();

        if (animator != null)
        {
            animator.SetBool("IsTalking", false);
            animator.SetInteger("ActionType", 2);
            animator.SetTrigger("ActionTrigger");
        }
    }

    private void OnDisable()
    {
        ResetSafetyState();
    }

    private void ResetSafetyState()
    {
        isSpeaking = false;
        isTyping = false;
        activeReaction = 0;
        reactionTimer = 0f;
        punctuationNodTimer = 0f;
        pitchWatchdogTimer = 0f;
        isYawEasing = false;
        targetLookWeight = 0f;
        currentLookWeight = 0f;

        if (hasCachedBasePose)
        {
            if (headBone != null) headBone.localRotation = baseHeadLocalRot;
            if (chestBone != null) chestBone.localRotation = baseChestLocalRot;
            if (leftUpperArm != null) leftUpperArm.localRotation = baseLeftArmLocalRot;
            if (rightUpperArm != null) rightUpperArm.localRotation = baseRightArmLocalRot;
        }

        if (animator != null)
        {
            animator.SetBool("IsTalking", false);
            animator.ResetTrigger("ActionTrigger");
        }
    }

    private void Update()
    {
        // 1. Smooth Yaw Ease (0.4s) - Yaw Only
        if (isYawEasing)
        {
            yawTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(yawTimer / Mathf.Max(0.01f, yawEaseDuration));
            transform.rotation = Quaternion.Slerp(initialYaw, targetYaw, Mathf.SmoothStep(0f, 1f, t));
            if (t >= 1f) isYawEasing = false;
        }

        // 2. Smooth Look Target to Player Eye (never dialogue cam)
        if (playerTarget != null && isSpeaking)
        {
            Vector3 rawEyePos = playerTarget.position + Vector3.up * 1.55f;
            smoothedLookTarget = Vector3.Lerp(smoothedLookTarget, rawEyePos, Time.unscaledDeltaTime * 8f);

            // Angle limit check: Yaw +-70 deg, Pitch -10..+20 deg
            Vector3 dir = (smoothedLookTarget - transform.position);
            Vector3 localDir = transform.InverseTransformDirection(dir.normalized);
            float yawDeg = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
            float pitchDeg = Mathf.Atan2(localDir.y, Mathf.Sqrt(localDir.x * localDir.x + localDir.z * localDir.z)) * Mathf.Rad2Deg;

            if (yawDeg < -70f || yawDeg > 70f || pitchDeg < -10f || pitchDeg > 20f || localDir.z <= 0.1f)
            {
                targetLookWeight = 0f;
            }
            else
            {
                targetLookWeight = maxLookWeight;
            }
        }
        else
        {
            targetLookWeight = 0f;
        }

        currentLookWeight = Mathf.MoveTowards(currentLookWeight, targetLookWeight, Time.unscaledDeltaTime * 2.5f);

        // 3. Timers
        if (reactionTimer > 0f) reactionTimer -= Time.unscaledDeltaTime;
        else activeReaction = 0;

        if (punctuationNodTimer > 0f) punctuationNodTimer -= Time.unscaledDeltaTime;

        // 4. Watchdog: if head pitch > 35 deg for >0.5s, force reset
        if (isSpeaking && headBone != null && hasCachedBasePose)
        {
            float pitchDiff = Quaternion.Angle(headBone.localRotation, baseHeadLocalRot);
            if (pitchDiff > 35f && punctuationNodTimer <= 0f)
            {
                pitchWatchdogTimer += Time.unscaledDeltaTime;
                if (pitchWatchdogTimer > 0.5f)
                {
                    Debug.LogWarning($"[NpcDialogueSpeakerAnimator] Watchdog triggered on {gameObject.name}: pitch exceeded 35 deg. Resetting head pose.");
                    headBone.localRotation = baseHeadLocalRot;
                    pitchWatchdogTimer = 0f;
                }
            }
            else
            {
                pitchWatchdogTimer = 0f;
            }
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || !animator.isHuman) return;

        if (currentLookWeight > 0.005f && playerTarget != null)
        {
            // Humanoid Animator IK: weight 0.8, body 0.15, head 0.85, eyes 0
            animator.SetLookAtWeight(currentLookWeight * 0.8f, 0.15f, 0.85f, 0f, 0.5f);
            animator.SetLookAtPosition(smoothedLookTarget);
        }
        else
        {
            animator.SetLookAtWeight(0f);
        }
    }

    private void LateUpdate()
    {
        // Safe procedural fallback gestures:
        // Always computed relative to cached base pose so they NEVER accumulate across frames.
        if (!isSpeaking || headBone == null || !hasCachedBasePose) return;

        float nodDeg = 0f;
        if (isTyping)
        {
            nodDeg += Mathf.Sin(Time.unscaledTime * 10f) * 2.0f;
        }
        if (punctuationNodTimer > 0f)
        {
            nodDeg += Mathf.Sin((punctuationNodTimer / 0.16f) * Mathf.PI) * 4.5f;
        }

        // Clamp total procedural nod to +-8 degrees
        nodDeg = Mathf.Clamp(nodDeg, -8f, 8f);

        // Apply only relative to cached base pose: no accumulation!
        if (Mathf.Abs(nodDeg) > 0.01f)
        {
            headBone.localRotation = baseHeadLocalRot * Quaternion.Euler(nodDeg, 0f, 0f);
        }

        // Reaction Beats (Procedural)
        if (activeReaction == 1)
        {
            // Defensive
            if (headBone != null) headBone.localRotation = baseHeadLocalRot * Quaternion.Euler(-2f, 0f, 6.0f);
            if (leftUpperArm != null) leftUpperArm.localRotation = baseLeftArmLocalRot * Quaternion.Euler(0f, 0f, 7f);
            if (rightUpperArm != null) rightUpperArm.localRotation = baseRightArmLocalRot * Quaternion.Euler(0f, 0f, -7f);
        }
        else if (activeReaction == 2)
        {
            // Surprised
            if (headBone != null) headBone.localRotation = baseHeadLocalRot * Quaternion.Euler(-6f, 4f, 0f);
            if (chestBone != null) chestBone.localRotation = baseChestLocalRot * Quaternion.Euler(-3f, 0f, 0f);
        }
        else if (activeReaction == 3)
        {
            // Grateful nod
            float bow = Mathf.Sin(Mathf.Clamp01(reactionTimer / 1.8f) * Mathf.PI) * 5f;
            if (chestBone != null) chestBone.localRotation = baseChestLocalRot * Quaternion.Euler(bow, 0f, 0f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (playerTarget != null)
        {
            Gizmos.color = currentLookWeight > 0.01f ? Color.green : Color.red;
            Gizmos.DrawWireSphere(smoothedLookTarget, 0.15f);
            Vector3 origin = headBone != null ? headBone.position : (transform.position + Vector3.up * 1.5f);
            Gizmos.DrawLine(origin, smoothedLookTarget);
        }
    }
}
