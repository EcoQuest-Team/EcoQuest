using System.Collections.Generic;
using UnityEngine;

public enum LitterbugState
{
    WalkingToNode = 0,
    Idle = 1,
    Throwing = 2,
    Listening = 3,
    Persuaded = 4
}

public class LitterbugNpc : MonoBehaviour
{
    public const int MaxLitterPerNpc = 4;

    [Header("Detection Radii (Inspector Configurable)")]
    [SerializeField] private float triggerRadius = 20f;
    [SerializeField] private float triggerExitBuffer = 2f;
    [SerializeField] private float interactRadius = 2.5f;

    private NpcBrain brain;
    private Animator anim;
    private Transform rightUpperArm;
    private Transform rightLowerArm;
    private Transform rightHand;
    private Transform mainCamTransform;

    private LitterbugQuestManager manager;
    private LitterTypeEntry assignedEntry;
    private Sprite exclamationSprite;
    private Sprite greenCheckSprite;
    private AnimationClip customThrowClip;

    private Transform markerRoot;
    private SpriteRenderer markerRenderer;
    private float bobPhase;
    private float markerBaseScale = 0.42f;
    private float markerPopTimer;

    private float nextThrowTimer;
    private bool isSwingingThrow;
    private float swingElapsed;
    private const float SwingDuration = 0.92f;
    private const float ReleaseNormalizedTime = 0.46f;
    private bool releasedTrashThisSwing;
    private float armBlendWeight;
    private Vector3 lastKnownPlayerPos;
    private Vector3 lastThrownDirectionXZ;

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
        triggerRadius = trigRadius;
        triggerExitBuffer = trigExitBuf;
        interactRadius = interRadius;
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

        groundMask = ~LayerMask.GetMask("Ignore Raycast", "UI");
        bobPhase = Random.Range(0f, Mathf.PI * 2f);
        nextThrowTimer = Random.Range(0.9f, 1.5f);
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
            return;
        }

        if (inDialogueWithThisNpc)
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
                State = sqrDistToPlayer <= trigSqr ? LitterbugState.Throwing : LitterbugState.Idle;
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
            }
        }
        else
        {
            State = sqrDistToPlayer <= trigSqr ? LitterbugState.Throwing : LitterbugState.Idle;
            if (State == LitterbugState.Throwing && nextThrowTimer > 1.6f)
                nextThrowTimer = Random.Range(0.8f, 1.4f);
        }

        if (State == LitterbugState.Listening)
        {
            FacePositionSmooth(playerPos, dt * 7f);
        }
        else if (State == LitterbugState.Throwing)
        {
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
                nextThrowTimer = Random.Range(4f, 5f);
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

    private Vector3 ChooseNonPlayerThrowDirection(Vector3 playerPos)
    {
        Vector3 toPlayer = playerPos - transform.position;
        toPlayer.y = 0f;
        bool hasPlayerDir = toPlayer.sqrMagnitude > 0.001f;
        if (hasPlayerDir) toPlayer.Normalize();

        // Candidate front/side offsets relative to NPC forward; reject any direction pointing toward player
        float[] baseAngles = { -85f, 85f, -55f, 55f, 0f, -95f, 95f };
        int start = Random.Range(0, baseAngles.Length);

        for (int i = 0; i < baseAngles.Length; i++)
        {
            float yaw = baseAngles[(start + i) % baseAngles.Length] + Random.Range(-8f, 8f);
            Vector3 cand = Quaternion.Euler(0f, yaw, 0f) * transform.forward;
            cand.y = 0f;
            if (cand.sqrMagnitude < 0.001f) continue;
            cand.Normalize();

            if (!hasPlayerDir || Vector3.Dot(cand, toPlayer) <= 0.0f)
                return cand;
        }

        // Guaranteed perpendicular/away fallback if NPC is facing directly toward the player
        Vector3 side = hasPlayerDir ? Vector3.Cross(Vector3.up, toPlayer).normalized : transform.right;
        if (Random.value < 0.5f) side = -side;
        return (side - (hasPlayerDir ? toPlayer * 0.22f : Vector3.zero)).normalized;
    }

    private LitterProp SpawnThrownTrash(Vector3 playerPos)
    {
        if (assignedEntry == null || assignedEntry.propPrefab == null) return null;

        Vector3 handPos = rightHand != null
            ? rightHand.position
            : (transform.position + Vector3.up * 1.25f + transform.forward * 0.28f + transform.right * 0.2f);

        Vector3 flatDir = ChooseNonPlayerThrowDirection(playerPos);
        lastThrownDirectionXZ = flatDir;
        float throwDist = Random.Range(1.15f, 1.95f);
        Vector3 candidateXZ = transform.position + flatDir * throwDist;

        float groundY = transform.position.y + 0.02f;
        RaycastHit[] hits = Physics.RaycastAll(candidateXZ + Vector3.up * 2.5f, Vector3.down, 6f, groundMask, QueryTriggerInteraction.Ignore);
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

        while (spawnedLitter.Count > 0 && spawnedLitter.Peek() == null)
            spawnedLitter.Dequeue();

        while (spawnedLitter.Count >= MaxLitterPerNpc)
        {
            LitterProp oldest = spawnedLitter.Dequeue();
            if (oldest != null) Destroy(oldest.gameObject);
        }

        LitterProp instance = Instantiate(assignedEntry.propPrefab, handPos, Quaternion.identity);
        spawnedLitter.Enqueue(instance);

        if (manager != null) manager.PlayThrowAudio();
        instance.Launch(handPos, landingPos, Random.Range(0.45f, 0.72f), Random.Range(0.46f, 0.58f), OnLitterLanded);
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
        while (spawnedLitter.Count > 0)
        {
            LitterProp prop = spawnedLitter.Dequeue();
            if (prop != null) Destroy(prop.gameObject);
        }
        if (markerRoot != null) Destroy(markerRoot.gameObject);
        if (brain != null && brain.IsQuestTarget) brain.ReleaseFromQuestTarget();
        Destroy(this);
    }

    private void FacePositionSmooth(Vector3 targetWorldPos, float t)
    {
        Vector3 dir = targetWorldPos - transform.position;
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

        float bob = (State != LitterbugState.Persuaded)
            ? Mathf.Sin(Time.time * 3.6f + bobPhase) * 0.085f
            : 0f;
        markerRoot.localPosition = new Vector3(0f, 2.22f + bob, 0f);

        if (markerPopTimer > 0f)
        {
            markerPopTimer -= Time.deltaTime;
            float p = 1f - Mathf.Clamp01(markerPopTimer / 0.32f);
            float punch = 1f + 0.38f * Mathf.Sin(p * Mathf.PI);
            markerRoot.localScale = Vector3.one * (markerBaseScale * punch);
        }
        else
        {
            markerRoot.localScale = Vector3.one * markerBaseScale;
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
            upperPitch = Mathf.Lerp(34f, -58f, k);
            upperYaw = Mathf.Lerp(22f, -14f, k);
            lowerBend = Mathf.Lerp(-62f, -12f, k);
        }
        else
        {
            float k = Mathf.SmoothStep(0f, 1f, (norm - 0.62f) / 0.38f);
            upperPitch = Mathf.Lerp(-58f, 0f, k);
            upperYaw = Mathf.Lerp(-14f, 0f, k);
            lowerBend = Mathf.Lerp(-12f, 0f, k);
        }

        Quaternion worldUpperDelta = Quaternion.AngleAxis(upperPitch * armBlendWeight, transform.right)
                                   * Quaternion.AngleAxis(upperYaw * armBlendWeight, transform.up);
        rightUpperArm.rotation = worldUpperDelta * rightUpperArm.rotation;

        if (rightLowerArm != null)
        {
            Quaternion worldLowerDelta = Quaternion.AngleAxis(lowerBend * armBlendWeight, transform.right);
            rightLowerArm.rotation = worldLowerDelta * rightLowerArm.rotation;
        }
    }

    private void OnDestroy()
    {
        while (spawnedLitter.Count > 0)
        {
            LitterProp prop = spawnedLitter.Dequeue();
            if (prop != null) Destroy(prop.gameObject);
        }
    }
}
