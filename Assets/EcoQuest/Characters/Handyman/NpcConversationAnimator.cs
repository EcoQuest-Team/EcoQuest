using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EcoQuest.Characters.Handyman
{
    /// <summary>
    /// Expressive dialogue and conversation behavior for the Handyman NPC.
    /// Provides the full conversation API: BeginConversation, SetSpeaking, OnPageStarted,
    /// OnPageEnded, PlayReaction, EndConversation, along with procedural jaw-speech sync,
    /// natural blinking, look-at IK, and null-safe footstep events.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class NpcConversationAnimator : MonoBehaviour
    {
        [Header("Appearance & Components")]
        [SerializeField] private Animator animator;
        [SerializeField] private NavMeshAgent navAgent;
        [SerializeField] private SkinnedMeshRenderer bodyRenderer;
        [SerializeField] private GameObject satchelObject;
        [SerializeField] private AudioSource footstepAudioSource;
        [SerializeField] private AudioClip[] footstepClips;
        [SerializeField] private bool showSatchel = true;

        [Header("Locomotion Tuning")]
        [SerializeField] private float walkSpeed = 1.4f;
        [SerializeField] private float runSpeed = 3.6f;
        [SerializeField] private float speedDampTime = 0.1f;

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;

        [Header("Look-At IK")]
        [SerializeField] private bool enableLookAtIK = true;
        [SerializeField] private Transform currentConversationPartner;
        [Range(0f, 1f)]
        [SerializeField] private float maxLookAtWeight = 0.8f;
        [SerializeField] private float lookAtEyeHeight = 1.55f;
        [SerializeField] private float yawTurnDuration = 0.4f;

        [Header("Facial Expression & Shape Keys")]
        [SerializeField] private string blinkShapeName = "Blink";
        [SerializeField] private string jawShapeName = "JawOpen";
        [SerializeField] private string browShapeName = "BrowUp";
        [SerializeField] private string smileShapeName = "Smile";

        [Header("Timing Configuration")]
        [SerializeField] private Vector2 blinkIntervalRange = new Vector2(3f, 6f);
        [SerializeField] private float blinkDuration = 0.15f;
        [SerializeField] private Vector2 idleVariationIntervalRange = new Vector2(8f, 15f);

        // Shape key indices
        private int blinkIdx = -1;
        private int jawIdx = -1;
        private int browIdx = -1;
        private int smileIdx = -1;

        // Conversation state
        private bool inConversation = false;
        private bool isSpeaking = false;
        private int talkIndexCounter = 0;
        private float currentLookWeight = 0f;
        private float targetLookWeight = 0f;
        private Vector3 smoothedLookTarget;

        // Smooth rotation easing
        private Quaternion initialRotation;
        private Quaternion targetRotation;
        private float turnTimer = 0f;
        private bool isTurning = false;

        // Procedural speech modulation
        private float jawTimer = 0f;
        private float targetJawWeight = 0f;
        private float currentJawWeight = 0f;
        private float jawFrequency = 5.2f;

        // Timers
        private float nextBlinkTimer = 3f;
        private float blinkTimer = -1f;
        private float nextIdleVariationTimer = 10f;

        // Animator Hashes
        private static readonly int HashSpeed = Animator.StringToHash("Speed");
        private static readonly int HashTalk = Animator.StringToHash("Talk");
        private static readonly int HashTalkIndex = Animator.StringToHash("TalkIndex");
        private static readonly int HashListen = Animator.StringToHash("Listen");
        private static readonly int HashGreet = Animator.StringToHash("Greet");
        private static readonly int HashNod = Animator.StringToHash("Nod");
        private static readonly int HashShakeHead = Animator.StringToHash("ShakeHead");
        private static readonly int HashShrug = Animator.StringToHash("Shrug");
        private static readonly int HashPoint = Animator.StringToHash("Point");
        private static readonly int HashThanks = Animator.StringToHash("Thanks");
        private static readonly int HashLaugh = Animator.StringToHash("Laugh");
        private static readonly int HashSurprised = Animator.StringToHash("Surprised");
        private static readonly int HashPlayIdleVariation = Animator.StringToHash("PlayIdleVariation");
        private static readonly int HashIdleVariation = Animator.StringToHash("IdleVariation");

        public bool InConversation => inConversation;
        public bool IsSpeaking => isSpeaking;
        public bool ShowSatchel
        {
            get => showSatchel;
            set
            {
                showSatchel = value;
                ApplySatchelVisibility();
            }
        }

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
            if (footstepAudioSource == null) footstepAudioSource = GetComponent<AudioSource>();

            LocateMeshesAndShapeKeys();
            ApplySatchelVisibility();
            ScheduleNextBlink();
            ScheduleNextIdleVariation();
        }

        private void OnValidate()
        {
            ApplySatchelVisibility();
        }

        public void LocateMeshesAndShapeKeys()
        {
            if (bodyRenderer == null)
            {
                var rends = GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var r in rends)
                {
                    if (r.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bodyRenderer = r;
                    }
                    else if (r.name.IndexOf("Satchel", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        satchelObject = r.gameObject;
                    }
                }
            }

            if (bodyRenderer != null && bodyRenderer.sharedMesh != null)
            {
                var mesh = bodyRenderer.sharedMesh;
                blinkIdx = mesh.GetBlendShapeIndex(blinkShapeName);
                jawIdx = mesh.GetBlendShapeIndex(jawShapeName);
                browIdx = mesh.GetBlendShapeIndex(browShapeName);
                smileIdx = mesh.GetBlendShapeIndex(smileShapeName);
            }
        }

        public void ApplySatchelVisibility()
        {
            if (satchelObject != null)
            {
                satchelObject.SetActive(showSatchel);
            }
        }

        private void Update()
        {
            UpdateLocomotion();
            UpdateTurning();
            UpdateLookWeight();
            UpdateBlinking();
            UpdateSpeechLipSync();
            UpdateIdleVariations();
        }

        private void UpdateLocomotion()
        {
            if (animator == null || animator.runtimeAnimatorController == null || !animator.isActiveAndEnabled) return;

            float speed = 0f;
            if (navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh)
            {
                speed = navAgent.velocity.magnitude;
            }

            animator.SetFloat(HashSpeed, speed, speedDampTime, Time.deltaTime);
        }

        private void UpdateTurning()
        {
            if (!isTurning) return;

            turnTimer += Time.deltaTime;
            float t = Mathf.Clamp01(turnTimer / Mathf.Max(0.01f, yawTurnDuration));
            // Smooth step easing
            float smoothT = t * t * (3f - 2f * t);
            transform.rotation = Quaternion.Slerp(initialRotation, targetRotation, smoothT);

            if (t >= 1f)
            {
                isTurning = false;
            }
        }

        private void UpdateLookWeight()
        {
            currentLookWeight = Mathf.MoveTowards(currentLookWeight, targetLookWeight, Time.deltaTime * 2.5f);

            if (currentConversationPartner != null)
            {
                Vector3 targetPos = currentConversationPartner.position + Vector3.up * lookAtEyeHeight;
                smoothedLookTarget = Vector3.Lerp(smoothedLookTarget, targetPos, Time.deltaTime * 8f);
            }
        }

        private void UpdateBlinking()
        {
            if (bodyRenderer == null || blinkIdx < 0) return;

            if (blinkTimer >= 0f)
            {
                blinkTimer += Time.deltaTime;
                float progress = blinkTimer / Mathf.Max(0.01f, blinkDuration);
                if (progress <= 1f)
                {
                    float weight = Mathf.Sin(progress * Mathf.PI) * 100f;
                    bodyRenderer.SetBlendShapeWeight(blinkIdx, weight);
                }
                else
                {
                    bodyRenderer.SetBlendShapeWeight(blinkIdx, 0f);
                    blinkTimer = -1f;
                    ScheduleNextBlink();
                }
            }
            else
            {
                nextBlinkTimer -= Time.deltaTime;
                if (nextBlinkTimer <= 0f)
                {
                    TriggerBlink();
                }
            }
        }

        private void TriggerBlink()
        {
            blinkTimer = 0f;
        }

        private void ScheduleNextBlink()
        {
            nextBlinkTimer = UnityEngine.Random.Range(blinkIntervalRange.x, blinkIntervalRange.y);
        }

        private void UpdateSpeechLipSync()
        {
            if (bodyRenderer == null || jawIdx < 0) return;

            if (isSpeaking)
            {
                jawTimer += Time.deltaTime * jawFrequency;
                // Pulsed jaw motion at 4.5 - 6 Hz with natural secondary micro-wiggles
                float pulse = (Mathf.Sin(jawTimer * Mathf.PI * 2f) * 0.5f + 0.5f);
                float accent = Mathf.PerlinNoise(jawTimer * 0.8f, 0f) * 0.3f;
                targetJawWeight = Mathf.Clamp((pulse + accent) * 75f, 0f, 95f);

                // Subtle BrowUp on accents
                if (browIdx >= 0)
                {
                    bodyRenderer.SetBlendShapeWeight(browIdx, accent * 50f);
                }
            }
            else
            {
                targetJawWeight = 0f;
                if (browIdx >= 0)
                {
                    float curBrow = bodyRenderer.GetBlendShapeWeight(browIdx);
                    bodyRenderer.SetBlendShapeWeight(browIdx, Mathf.MoveTowards(curBrow, 0f, Time.deltaTime * 150f));
                }
            }

            currentJawWeight = Mathf.MoveTowards(currentJawWeight, targetJawWeight, Time.deltaTime * 350f);
            bodyRenderer.SetBlendShapeWeight(jawIdx, currentJawWeight);
        }

        private void UpdateIdleVariations()
        {
            if (inConversation || animator == null) return;

            // Only trigger idle variations when stationary
            if (navAgent != null && navAgent.isActiveAndEnabled && navAgent.velocity.sqrMagnitude > 0.05f)
            {
                return;
            }

            nextIdleVariationTimer -= Time.deltaTime;
            if (nextIdleVariationTimer <= 0f)
            {
                int varIndex = UnityEngine.Random.Range(0, 3); // 0=ScratchBeard, 1=CheckPhone, 2=AdjustCap
                animator.SetInteger(HashIdleVariation, varIndex);
                animator.SetTrigger(HashPlayIdleVariation);
                ScheduleNextIdleVariation();
            }
        }

        private void ScheduleNextIdleVariation()
        {
            nextIdleVariationTimer = UnityEngine.Random.Range(idleVariationIntervalRange.x, idleVariationIntervalRange.y);
        }

        // =========================================================================
        // PUBLIC CONVERSATION API (Specification Compliant)
        // =========================================================================

        /// <summary>
        /// Begins conversation with the player: eases yaw turn toward player (<=0.4s),
        /// blends to Listen state, enables look-at IK smoothly, and triggers Greet.
        /// </summary>
        public void BeginConversation(Transform player)
        {
            inConversation = true;
            currentConversationPartner = player;

            if (player != null)
            {
                Vector3 dir = (player.position - transform.position);
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                {
                    initialRotation = transform.rotation;
                    targetRotation = Quaternion.LookRotation(dir.normalized);
                    turnTimer = 0f;
                    isTurning = true;
                }
                smoothedLookTarget = player.position + Vector3.up * lookAtEyeHeight;
            }

            targetLookWeight = maxLookAtWeight;

            if (animator != null)
            {
                animator.SetBool(HashListen, true);
                animator.SetBool(HashTalk, false);
                animator.SetTrigger(HashGreet);
            }

            TriggerBlink();
        }

        /// <summary>
        /// Sets speaking mode: alternates TalkA/TalkB and pulses jawOpen shape key.
        /// When false, smoothly returns to listen loop, closes jaw, and maintains eye contact.
        /// </summary>
        public void SetSpeaking(bool speaking)
        {
            isSpeaking = speaking;

            if (animator != null)
            {
                if (speaking)
                {
                    animator.SetBool(HashTalk, true);
                    animator.SetBool(HashListen, false);
                    animator.SetInteger(HashTalkIndex, talkIndexCounter % 2);
                    talkIndexCounter++;
                }
                else
                {
                    animator.SetBool(HashTalk, false);
                    animator.SetBool(HashListen, true);
                }
            }
        }

        /// <summary>
        /// Called when a new dialogue text page begins. Scales cadence to sentence structure.
        /// </summary>
        public void OnPageStarted(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                // Tune frequency slightly based on text pacing
                jawFrequency = text.Length > 60 ? 5.8f : 4.8f;
                // Alternate animation style: short texts use TalkA, longer use TalkB
                if (animator != null)
                {
                    animator.SetInteger(HashTalkIndex, (text.Length % 2));
                }
            }
            SetSpeaking(true);
        }

        /// <summary>
        /// Called when dialogue text playback ends on current page.
        /// </summary>
        public void OnPageEnded()
        {
            SetSpeaking(false);
        }

        /// <summary>
        /// Triggers a specific reaction gesture (Nod, ShakeHead, Shrug, Point, Thanks, Laugh, Surprised)
        /// with a natural blink, cleanly returning to current talk/listen state.
        /// </summary>
        public void PlayReaction(string reactionName)
        {
            if (animator == null || string.IsNullOrEmpty(reactionName)) return;

            TriggerBlink();

            switch (reactionName.Trim().ToLowerInvariant())
            {
                case "nod":
                    animator.SetTrigger(HashNod);
                    break;
                case "shakehead":
                case "shake":
                    animator.SetTrigger(HashShakeHead);
                    break;
                case "shrug":
                    animator.SetTrigger(HashShrug);
                    break;
                case "point":
                    animator.SetTrigger(HashPoint);
                    break;
                case "thanks":
                case "thank":
                    animator.SetTrigger(HashThanks);
                    break;
                case "laugh":
                    animator.SetTrigger(HashLaugh);
                    break;
                case "surprised":
                case "surprise":
                    animator.SetTrigger(HashSurprised);
                    break;
                case "greet":
                case "wave":
                    animator.SetTrigger(HashGreet);
                    break;
                default:
                    animator.SetTrigger(HashNod);
                    break;
            }
        }

        /// <summary>
        /// Ends conversation: plays Thanks gesture, eases look-at back to forward over 0.5s,
        /// and resets to idle/locomotion mode.
        /// </summary>
        public void EndConversation()
        {
            inConversation = false;
            isSpeaking = false;
            targetLookWeight = 0f;

            if (animator != null)
            {
                animator.SetBool(HashTalk, false);
                animator.SetBool(HashListen, false);
                animator.SetTrigger(HashThanks);
            }

            ScheduleNextIdleVariation();
        }

        // =========================================================================
        // Compatibility Aliases for EcoQuest Dialogue System
        // =========================================================================
        public void BeginDialogue(Transform player, bool isGuideNpc = false) => BeginConversation(player);
        public void EndDialogue() => EndConversation();

        // =========================================================================
        // Head Look-At IK
        // =========================================================================
        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || !enableLookAtIK) return;

            if (currentLookWeight > 0.001f && currentConversationPartner != null)
            {
                animator.SetLookAtWeight(currentLookWeight, 0.25f, 0.65f, 0.85f, 0.5f);
                animator.SetLookAtPosition(smoothedLookTarget);
            }
            else
            {
                animator.SetLookAtWeight(0f);
            }
        }

        // =========================================================================
        // Animation Event Handlers (Null-Safe)
        // =========================================================================
        public void OnFootstep(string foot)
        {
            if (footstepAudioSource == null || footstepClips == null || footstepClips.Length == 0) return;

            AudioClip clip = footstepClips[UnityEngine.Random.Range(0, footstepClips.Length)];
            if (clip != null)
            {
                footstepAudioSource.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
                footstepAudioSource.PlayOneShot(clip, 0.45f);
            }
        }
    }
}
