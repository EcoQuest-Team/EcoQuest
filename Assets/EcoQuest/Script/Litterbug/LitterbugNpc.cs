using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum LitterbugState
{
    Idle,
    WalkingToNode,
    Throwing,
    Listening,
    Persuaded
}

/// <summary>
/// Controller for individual Litterbug NPCs.
/// Manages throwing behavior (in front of player within 8m, 4-5s intervals, scripted arc, max 4 items),
/// overhead distance-scaled "!" marker (hides inside InteractRadius+1m or during dialogue),
/// and humanoid arm throw animation.
/// </summary>
public class LitterbugNpc : MonoBehaviour
{
    public const int MaxLitterPerNpc = 4;

    [Header("Radii & Landing Tuning (Inspector)")]
    [SerializeField] private float triggerRadius = 20f;
    [SerializeField] private float triggerExitBuffer = 3f;
    [SerializeField] private float interactRadius = 2.4f;
    [SerializeField] private float throwAtPlayerRadius = 8f;
    [SerializeField] private Vector2 landingDistRange = new Vector2(2.5f, 3.5f);
    [SerializeField] private Vector2 throwIntervalRange = new Vector2(4.0f, 5.0f);

    private LitterbugQuestManager manager;
    private NpcBrain brain;
    private LitterTypeEntry assignedEntry;
    private Sprite exclamationSprite;
    private Sprite greenCheckSprite;
    private AnimationClip customThrowClip;
    private Transform mainCamTransform;

    private Animator anim;
    private Transform rightUpperArm;
    private Transform rightLowerArm;
    private Transform rightHand;

    private Transform markerRoot;
    private SpriteRenderer markerRenderer;
    private float bobPhase;
    private float markerBaseScale = 0.24f;
    private float markerPopTimer;
    private bool isDialogueActive = false;

    public void SetDialogueActive(bool active)
    {
        isDialogueActive = active;
        if (markerRoot != null)
            markerRoot.gameObject.SetActive(!active && State != LitterbugState.Persuaded);
    }

    private float nextThrowTimer;
    private bool isSwingingThrow;
    private float swingElapsed;
    private const float SwingDuration = 0.92f;
    private const float ReleaseNormalizedTime = 0.46f;
    private bool releasedTrashThisSwing;
    private float armBlendWeight;
    private Vector3 lastKnownPlayerPos;
    private Vector3 lastThrownDirectionXZ;
    private bool wasInTriggerLastFrame = false;

    private readonly Queue<LitterProp> spawnedLitter = new Queue<LitterProp>(MaxLitterPerNpc + 1);
    private int groundMask;

    public NpcBrain Brain => brain;
    public LitterTypeEntry AssignedEntry => assignedEntry;
    public LitterbugState State { get; private set; } = LitterbugState.WalkingToNode;
    public bool IsPersuaded => State == LitterbugState.Persuaded;
    public bool UsesProceduralFallback { get; private set; } = true;
    public int ActiveLitterCount => spawnedLitter.Count;
    public Vector3 LastThrownDirectionXZ => lastThrownDirectionXZ;

    public float TriggerRadius => triggerRadius;
    public float ThrowAtPlayerRadius => throwAtPlayerRadius;
    public float InteractRadius => interactRadius;
    public float TriggerRadiusSqr => triggerRadius * triggerRadius;
    public float TriggerExitRadiusSqr => (triggerRadius + triggerExitBuffer) * (triggerRadius + triggerExitBuffer);
    public float InteractRadiusSqr => interactRadius * interactRadius;
    public float InteractExitRadiusSqr => (interactRadius + 0.35f) * (interactRadius + 0.35f);

    public void Initialize(
        LitterbugQuestManager questManager,
        NpcBrain npcBrain,
        LitterTypeEntry entry,
        float trigRadius,
        float trigExitBuf,
        float interRadius,
        Sprite exclSprite,
        Sprite checkSprite,
        AnimationClip optionalThrowClip,
        Transform cameraTransform)
    {
        manager = questManager;
        brain = npcBrain;
        assignedEntry = entry;
        triggerRadius = trigRadius > 0f ? trigRadius : 20f;
        triggerExitBuffer = trigExitBuf;
        interactRadius = interRadius > 0f ? interRadius : 2.4f;
        exclamationSprite = exclSprite;
        greenCheckSprite = checkSprite;
        customThrowClip = optionalThrowClip;
        mainCamTransform = cameraTransform;

        anim = GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            rightUpperArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightLowerArm = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rightHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        }

        UsesProceduralFallback = (customThrowClip == null);
        if (customThrowClip != null && anim != null && anim.runtimeAnimatorController != null)
        {
            var aoc = new AnimatorOverrideController(anim.runtimeAnimatorController);
            anim.runtimeAnimatorController = aoc;
        }

        groundMask = ~LayerMask.GetMask("Ignore Raycast", "UI", "Player");
        bobPhase = Random.Range(0f, Mathf.PI * 2f);
        nextThrowTimer = 0.3f;
        isSwingingThrow = false;
        swingElapsed = 0f;
        armBlendWeight = 0f;

        CreateOverheadMarker();
        brain.AssignAsQuestTarget();
        State = brain.IsLoiteringForQuest ? LitterbugState.Idle : LitterbugState.WalkingToNode;
    }

    private void CreateOverheadMarker()
    {
        if (markerRoot != null) Destroy(markerRoot.gameObject);

        GameObject go = new GameObject("LitterbugOverheadMarker");
        markerRoot = go.transform;
        markerRoot.SetParent(transform, false);
        markerRoot.localPosition = new Vector3(0f, 2.22f, 0f);
        markerRoot.localScale = Vector3.one * markerBaseScale;

        markerRenderer = go.AddComponent<SpriteRenderer>();
        markerRenderer.sprite = exclamationSprite;
        markerRenderer.sortingOrder = 20;
    }

    public void TickFromManager(float sqrDistToPlayer, Vector3 playerPos, bool inDialogueWithThisNpc, float dt)
    {
        lastKnownPlayerPos = playerPos;
        if (State == LitterbugState.Persuaded) return;

        if (!brain.IsLoiteringForQuest)
        {
            State = LitterbugState.WalkingToNode;
            isSwingingThrow = false;
            wasInTriggerLastFrame = false;
            return;
        }

        if (inDialogueWithThisNpc || isDialogueActive)
        {
            State = LitterbugState.Listening;
            isSwingingThrow = false;
            FacePositionSmooth(playerPos, dt * 8f);
            return;
        }

        float trigSqr = TriggerRadiusSqr;
        float trigExitSqr = TriggerExitRadiusSqr;
        float interSqr = InteractRadiusSqr;
        float interExitSqr = InteractExitRadiusSqr;

        if (State == LitterbugState.Listening)
        {
            if (sqrDistToPlayer > interExitSqr)
            {
                State = sqrDistToPlayer <= trigSqr ? LitterbugState.Throwing : LitterbugState.Idle;
            }
        }
        else if (sqrDistToPlayer <= interSqr)
        {
            State = LitterbugState.Listening;
            isSwingingThrow = false;
        }
        else if (State == LitterbugState.Throwing)
        {
            if (sqrDistToPlayer > trigExitSqr)
            {
                State = LitterbugState.Idle;
                isSwingingThrow = false;
                wasInTriggerLastFrame = false;
            }
        }
        else
        {
            bool inTrigger = sqrDistToPlayer <= trigSqr;
            State = inTrigger ? LitterbugState.Throwing : LitterbugState.Idle;
            if (inTrigger && !wasInTriggerLastFrame)
            {
                // Entering TriggerRadius (20m): first throw after ~0.3s
                nextThrowTimer = 0.3f;
            }
            wasInTriggerLastFrame = inTrigger;
        }

        if (State == LitterbugState.Listening)
        {
            FacePositionSmooth(playerPos, dt * 7f);
        }
        else if (State == LitterbugState.Throwing)
        {
            // Turn toward player (yaw only)
            FacePositionSmooth(playerPos, dt * 7f);
            UpdateThrowingCycle(dt);
        }
    }

    private void UpdateThrowingCycle(float dt)
    {
        if (isSwingingThrow)
        {
            swingElapsed += dt;
            float norm = swingElapsed / SwingDuration;

            if (!releasedTrashThisSwing && norm >= ReleaseNormalizedTime)
            {
                releasedTrashThisSwing = true;
                SpawnThrownTrash(lastKnownPlayerPos);
            }

            if (norm >= 1f)
            {
                isSwingingThrow = false;
                swingElapsed = 0f;
                nextThrowTimer = Random.Range(throwIntervalRange.x, throwIntervalRange.y);
            }
        }
        else
        {
            nextThrowTimer -= dt;
            if (nextThrowTimer <= 0f)
            {
                BeginThrowSwing();
            }
        }
    }

    public LitterProp ForceSpawnThrowForTest(Vector3 playerPos)
    {
        lastKnownPlayerPos = playerPos;
        return SpawnThrownTrash(playerPos);
    }

    private void BeginThrowSwing()
    {
        isSwingingThrow = true;
        swingElapsed = 0f;
        releasedTrashThisSwing = false;
    }

    /// <summary>
    /// Computes landing position in front of player (if <=8m) or in front of NPC toward player (if >8m),
    /// snaps to ground, validates NavMesh/overlap, and launches scripted arc.
    /// </summary>
    private LitterProp SpawnThrownTrash(Vector3 playerPos)
    {
        if (assignedEntry == null || assignedEntry.propPrefab == null) return null;

        Vector3 handPos = rightHand != null
            ? rightHand.position
            : (transform.position + Vector3.up * 1.25f + transform.forward * 0.28f + transform.right * 0.2f);

        Vector3 candidateXZ;
        float distToPlayer = Vector3.Distance(transform.position, playerPos);

        if (distToPlayer <= throwAtPlayerRadius)
        {
            // Within 8m: lands 2.5-3.5m in front of player along camera yaw, lateral offset +-0.8m
            Vector3 camForwardXZ = (mainCamTransform != null)
                ? Vector3.ProjectOnPlane(mainCamTransform.forward, Vector3.up).normalized
                : (playerPos - transform.position).normalized;

            if (camForwardXZ.sqrMagnitude < 0.01f) camForwardXZ = transform.forward;
            Vector3 camRightXZ = Vector3.Cross(Vector3.up, camForwardXZ).normalized;

            float forwardDist = Random.Range(landingDistRange.x, landingDistRange.y);
            float lateralOffset = Random.Range(-0.8f, 0.8f);

            candidateXZ = playerPos + (camForwardXZ * forwardDist) + (camRightXZ * lateralOffset);
        }
        else
        {
            // Beyond 8m: lands 2-3m in front of NPC toward player
            Vector3 toPlayerXZ = Vector3.ProjectOnPlane(playerPos - transform.position, Vector3.up).normalized;
            if (toPlayerXZ.sqrMagnitude < 0.01f) toPlayerXZ = transform.forward;

            float forwardDist = Random.Range(2.0f, 3.0f);
            candidateXZ = transform.position + toPlayerXZ * forwardDist;
        }

        // Snap to ground by raycast
        float groundY = transform.position.y + 0.02f;
        RaycastHit[] hits = Physics.RaycastAll(candidateXZ + Vector3.up * 3.5f, Vector3.down, 7f, groundMask, QueryTriggerInteraction.Ignore);
        float bestY = -999f;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c.GetComponentInParent<NpcBrain>() != null) continue;
            if (c.CompareTag("Player")) continue;
            if (c.GetComponentInParent<LitterProp>() != null) continue;
            if (hits[i].point.y > bestY) bestY = hits[i].point.y;
        }
        if (bestY > -100f) groundY = bestY + 0.02f;

        Vector3 landingPos = new Vector3(candidateXZ.x, groundY, candidateXZ.z);

        // Validate: NavMesh walkable within 1.2m and no heavy obstacle overlap
        bool isValid = true;
        if (NavMesh.SamplePosition(landingPos, out NavMeshHit nmHit, 1.2f, NavMesh.AllAreas))
        {
            landingPos = nmHit.position + Vector3.up * 0.02f;
        }
        else
        {
            isValid = false;
        }

        Collider[] overlaps = Physics.OverlapSphere(landingPos + Vector3.up * 0.3f, 0.3f, groundMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            if (overlaps[i].CompareTag("Player") || overlaps[i].GetComponentInParent<NpcBrain>() != null)
            {
                isValid = false;
                break;
            }
        }

        if (!isValid)
        {
            // Safe fallback between NPC and player 1.5m from player
            Vector3 toNpc = (transform.position - playerPos);
            toNpc.y = 0f;
            Vector3 fbPos = playerPos + toNpc.normalized * 1.5f;
            if (Physics.Raycast(fbPos + Vector3.up * 3.5f, Vector3.down, out RaycastHit fbHit, 7f, groundMask, QueryTriggerInteraction.Ignore))
            {
                landingPos = fbHit.point + Vector3.up * 0.02f;
            }
            else
            {
                landingPos = fbPos;
            }
        }

        lastThrownDirectionXZ = (landingPos - handPos);
        lastThrownDirectionXZ.y = 0f;
        if (lastThrownDirectionXZ.sqrMagnitude > 0.001f) lastThrownDirectionXZ.Normalize();

        // Enforce max 4 litter per NPC (oldest shrinks away)
        while (spawnedLitter.Count > 0 && spawnedLitter.Peek() == null)
            spawnedLitter.Dequeue();

        while (spawnedLitter.Count >= MaxLitterPerNpc)
        {
            LitterProp oldest = spawnedLitter.Dequeue();
            if (oldest != null) oldest.ShrinkAndDestroy();
        }

        LitterProp instance = Instantiate(assignedEntry.propPrefab, handPos, Quaternion.identity);
        spawnedLitter.Enqueue(instance);

        if (manager != null) manager.PlayThrowAudio();

        float flightDist = Vector3.Distance(handPos, landingPos);
        float apex = Mathf.Clamp(1.5f + (flightDist / 10f) * 1.5f, 1.5f, 3.0f);
        float flightDuration = Mathf.Clamp(0.6f + (flightDist / 15f) * 0.4f, 0.6f, 1.0f);

        instance.Launch(handPos, landingPos, apex, flightDuration, OnLitterLanded);
        return instance;
    }

    private void OnLitterLanded(LitterProp prop)
    {
        if (manager != null) manager.PlayLandAudio();
    }

    public bool MarkPersuaded()
    {
        if (State == LitterbugState.Persuaded) return false;
        State = LitterbugState.Persuaded;
        isSwingingThrow = false;

        ShrinkRemainingLitter();

        if (markerRenderer != null && greenCheckSprite != null)
        {
            markerRenderer.sprite = greenCheckSprite;
            markerPopTimer = 0.32f;
        }

        if (brain != null)
        {
            brain.ReleaseFromQuestTarget();
        }

        return true;
    }

    public void ShrinkRemainingLitter()
    {
        while (spawnedLitter.Count > 0)
        {
            LitterProp prop = spawnedLitter.Dequeue();
            if (prop != null) prop.ShrinkAndDestroy();
        }
    }

    public void ReleaseForNewRound()
    {
        isSwingingThrow = false;
        ShrinkRemainingLitter();
        if (markerRoot != null) Destroy(markerRoot.gameObject);
        if (brain != null && brain.IsQuestTarget)
            brain.ReleaseFromQuestTarget();
        Destroy(this);
    }

    public void CleanupIfReassigned()
    {
        ShrinkRemainingLitter();
        if (markerRoot != null) Destroy(markerRoot.gameObject);
        if (brain != null) brain.ReleaseFromQuestTarget();
        Destroy(this);
    }

    public void FacePositionSmooth(Vector3 worldPos, float t)
    {
        Vector3 dir = worldPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Mathf.Clamp01(t));
        }
    }

    private void LateUpdate()
    {
        UpdateOverheadVisual();
        ApplyProceduralThrowArm();
    }

    private void UpdateOverheadVisual()
    {
        if (markerRoot == null) return;

        // Hide marker during dialogue, when persuaded, or when within InteractRadius + 1.0m
        float distToPlayer = Vector3.Distance(transform.position, lastKnownPlayerPos);
        bool shouldHide = isDialogueActive || (State == LitterbugState.Persuaded) || (distToPlayer <= (interactRadius + 1.0f));

        if (shouldHide)
        {
            if (markerRoot.gameObject.activeSelf) markerRoot.gameObject.SetActive(false);
            return;
        }

        if (!markerRoot.gameObject.activeSelf) markerRoot.gameObject.SetActive(true);

        float bob = (State != LitterbugState.Persuaded)
            ? Mathf.Sin(Time.time * 3.6f + bobPhase) * 0.085f
            : 0f;
        markerRoot.localPosition = new Vector3(0f, 2.22f + bob, 0f);

        // Scale with distance: 0.5 to 1.0, capped at ~60% base scale at close range
        float distFactor = Mathf.Clamp01((distToPlayer - 4f) / 16f);
        float distScale = Mathf.Lerp(0.60f, 1.0f, distFactor);

        if (markerPopTimer > 0f)
        {
            markerPopTimer -= Time.deltaTime;
            float p = 1f - Mathf.Clamp01(markerPopTimer / 0.32f);
            float punch = 1f + 0.38f * Mathf.Sin(p * Mathf.PI);
            markerRoot.localScale = Vector3.one * (markerBaseScale * punch * distScale);
        }
        else
        {
            markerRoot.localScale = Vector3.one * (markerBaseScale * distScale);
        }

        if (mainCamTransform != null)
        {
            Vector3 toCam = markerRoot.position - mainCamTransform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.001f)
                markerRoot.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        }
    }

    private void ApplyProceduralThrowArm()
    {
        if (!UsesProceduralFallback || rightUpperArm == null) return;

        float targetWeight = isSwingingThrow ? 1f : 0f;
        armBlendWeight = Mathf.MoveTowards(armBlendWeight, targetWeight, Time.deltaTime * 8f);
        if (armBlendWeight <= 0.001f) return;

        float norm = isSwingingThrow ? Mathf.Clamp01(swingElapsed / SwingDuration) : 1f;
        float upperPitch;
        float upperYaw;
        float lowerBend;

        if (norm < 0.36f)
        {
            float k = Mathf.SmoothStep(0f, 1f, norm / 0.36f);
            upperPitch = Mathf.Lerp(0f, 34f, k);
            upperYaw = Mathf.Lerp(0f, 22f, k);
            lowerBend = Mathf.Lerp(0f, -62f, k);
        }
        else if (norm < 0.62f)
        {
            float k = Mathf.SmoothStep(0f, 1f, (norm - 0.36f) / 0.26f);
            upperPitch = Mathf.Lerp(34f, -48f, k);
            upperYaw = Mathf.Lerp(22f, -14f, k);
            lowerBend = Mathf.Lerp(-62f, -12f, k);
        }
        else
        {
            float k = Mathf.SmoothStep(0f, 1f, (norm - 0.62f) / 0.38f);
            upperPitch = Mathf.Lerp(-48f, 0f, k);
            upperYaw = Mathf.Lerp(-14f, 0f, k);
            lowerBend = Mathf.Lerp(-12f, 0f, k);
        }

        Quaternion targetUpper = Quaternion.Euler(upperPitch, upperYaw, 0f);
        Quaternion targetLower = Quaternion.Euler(lowerBend, 0f, 0f);

        rightUpperArm.localRotation = Quaternion.Slerp(rightUpperArm.localRotation, rightUpperArm.localRotation * targetUpper, armBlendWeight);
        if (rightLowerArm != null)
        {
            rightLowerArm.localRotation = Quaternion.Slerp(rightLowerArm.localRotation, rightLowerArm.localRotation * targetLower, armBlendWeight);
        }
    }
}
