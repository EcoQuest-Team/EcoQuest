using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NpcBrain : MonoBehaviour
{
    [Header("Graph & Island Navigation")]
    [SerializeField] private CityWalkGraph walkGraph;
    [SerializeField] private int islandId = -1;
    [SerializeField] private float walkSpeed = 1.4f;
    [SerializeField] private float runSpeed = 3.5f;
    [SerializeField] private float runChance = 0.20f;
    [SerializeField] private float idleDurationMin = 2f;
    [SerializeField] private float idleDurationMax = 5f;
    [SerializeField] private float turnSpeed = 9f;
    [SerializeField] private float waypointReachDist = 0.45f;

    [Header("Outfit")]
    [SerializeField] private OutfitProfile outfitProfile;
    [SerializeField] private int outfitSeed;
    [SerializeField] private OutfitArchetype archetype;
    [SerializeField] private bool allowPhone;
    [SerializeField] private bool allowHandheld = true;

    [Header("Gender & Animation Overrides")]
    [SerializeField] private Gender gender = Gender.Male;
    [SerializeField] private RuntimeAnimatorController maleOverrideController;
    [SerializeField] private RuntimeAnimatorController femaleOverrideController;
    [SerializeField] private bool showGenderDebugLogs = true;

    [Header("Debug")]
    [SerializeField] private bool showDebugPath = true;

    [SerializeField, HideInInspector] private OutfitBuilder.GeneratedOutfit currentOutfit;
    [SerializeField, HideInInspector] private List<Material> ownedMaterials = new List<Material>();
    [SerializeField, HideInInspector] private Material[] defaultSharedMats;
    [SerializeField, HideInInspector] private bool defaultMatsCaptured;
    [SerializeField, HideInInspector] private bool hasOutfitConfigured;
    [SerializeField, HideInInspector] private bool needsOutfitRebuild;
    [SerializeField, HideInInspector] private bool hasStarted;

    private Animator anim;
    private CapsuleCollider col;
    private NPCInteraction interaction;
    private NavMeshAgent agent;

    private List<Vector3> currentPath = new List<Vector3>();
    private int currentWaypointIndex = 0;
    private float currentSpeed = 0f;
    private bool isRunning = false;
    private bool isWaiting = false;
    private bool isTalking = false;
    private bool inConversation = false;
    private Transform conversationPartner;
    private Coroutine waitCoroutine;
    private bool loiteringForQuest = false;

    private int raycastMask;

    public int IslandId => islandId;
    public void SetIslandId(int id) => islandId = id;
    public void SetWalkGraph(CityWalkGraph graph) => walkGraph = graph;
    public void SetOverrideControllers(RuntimeAnimatorController male, RuntimeAnimatorController female)
    {
        if (maleOverrideController == null && male != null) maleOverrideController = male;
        if (femaleOverrideController == null && female != null) femaleOverrideController = female;
    }

    public bool IsQuestTarget { get; private set; }
    public bool IsLoiteringForQuest => loiteringForQuest;
    public bool InConversation => inConversation;

    public bool IsOnCrosswalkOrRoad()
    {
        if (walkGraph == null || walkGraph.nodes.Count == 0) return false;
        int idx = walkGraph.FindClosestNode(transform.position, false);
        if (idx >= 0 && walkGraph.nodes[idx].isCrosswalk) return true;
        int swIdx = walkGraph.FindClosestNode(transform.position, true);
        if (swIdx >= 0)
        {
            Vector3 diff = walkGraph.nodes[swIdx].position - transform.position;
            diff.y = 0f;
            if (diff.sqrMagnitude > 6.25f) return true;
        }
        return false;
    }

    public void AssignAsQuestTarget()
    {
        IsQuestTarget = true;
        isRunning = false;
        if (waitCoroutine != null)
        {
            StopCoroutine(waitCoroutine);
            waitCoroutine = null;
        }
        if (isWaiting || currentPath == null || currentWaypointIndex >= currentPath.Count)
        {
            isWaiting = false;
            loiteringForQuest = true;
            currentSpeed = 0f;
            if (anim != null) anim.SetFloat("Speed", 0f);
        }
        else
        {
            loiteringForQuest = false;
        }
    }

    public void ReleaseFromQuestTarget()
    {
        IsQuestTarget = false;
        loiteringForQuest = false;
        isTalking = false;
        if (anim != null) anim.SetBool("IsTalking", false);
        PickNewDestination();
    }

    public Gender Gender => gender;
    public void SetGender(Gender g)
    {
        gender = g;
        ApplyGenderSpeeds();
        ApplyGenderController();
    }

    public void SetRole(bool runner)
    {
        runChance = runner ? 0.85f : 0.15f;
    }

    public void ApplyGenderSpeeds()
    {
        if (gender == Gender.Male)
        {
            walkSpeed = Random.Range(1.38f, 1.50f);
            runSpeed = Random.Range(3.45f, 3.70f);
        }
        else
        {
            walkSpeed = Random.Range(1.25f, 1.38f);
            runSpeed = Random.Range(3.20f, 3.45f);
        }
    }

    public void ApplyGenderController()
    {
        if (anim == null) anim = GetComponentInChildren<Animator>(true);
        if (anim == null) return;

        if (maleOverrideController == null)
        {
#if UNITY_EDITOR
            maleOverrideController = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/EcoQuest/Asset/NPC_Male_Override.overrideController");
#endif
        }
        if (femaleOverrideController == null)
        {
#if UNITY_EDITOR
            femaleOverrideController = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/EcoQuest/Asset/NPC_Female_Override.overrideController");
#endif
        }

        RuntimeAnimatorController target = (gender == Gender.Female) ? femaleOverrideController : maleOverrideController;
        if (target != null && anim.runtimeAnimatorController != target)
        {
            anim.runtimeAnimatorController = target;
        }

        if (showGenderDebugLogs)
        {
            Debug.Log($"[NPC Gender] {gameObject.name} | Gender: {gender} | Controller: {(anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "null")}");
        }
    }

    public int OutfitSeed => outfitSeed;
    public OutfitArchetype Archetype => archetype;
    public OutfitBuilder.GeneratedOutfit CurrentOutfit => currentOutfit;

    private void CaptureDefaultMaterials()
    {
        if (defaultMatsCaptured) return;
        var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null && smr.sharedMaterials != null && smr.sharedMaterials.Length > 0)
            defaultSharedMats = (Material[])smr.sharedMaterials.Clone();
        defaultMatsCaptured = true;
    }

    public OutfitBuilder.GeneratedOutfit ApplyOutfit(OutfitProfile profile, int seed, OutfitArchetype arch, bool canUsePhone, bool canHoldItem = true)
    {
        CaptureDefaultMaterials();
        outfitProfile = profile;
        outfitSeed = seed;
        archetype = arch;
        allowPhone = canUsePhone;
        allowHandheld = canHoldItem;
        hasOutfitConfigured = true;
        needsOutfitRebuild = !gameObject.activeInHierarchy;
        currentOutfit = OutfitBuilder.ApplyToNpc(gameObject, outfitProfile, outfitSeed, archetype, allowPhone, allowHandheld, ownedMaterials, defaultSharedMats);
        return currentOutfit;
    }

    public OutfitBuilder.GeneratedOutfit RerollOutfit(int newSeed)
    {
        CaptureDefaultMaterials();
        if (outfitProfile == null)
        {
#if UNITY_EDITOR
            outfitProfile = UnityEditor.AssetDatabase.LoadAssetAtPath<OutfitProfile>("Assets/EcoQuest/City Scene/CityOutfitProfile.asset");
#endif
        }
        outfitSeed = newSeed;
        hasOutfitConfigured = true;
        needsOutfitRebuild = !gameObject.activeInHierarchy;
        currentOutfit = OutfitBuilder.ApplyToNpc(gameObject, outfitProfile, outfitSeed, archetype, allowPhone, allowHandheld, ownedMaterials, defaultSharedMats);
        return currentOutfit;
    }

    public void Despawn()
    {
        if (IsQuestTarget) return;
        gameObject.SetActive(false);
    }

    public void Respawn(Vector3? newPosition = null)
    {
        if (IsQuestTarget) return;
        if (newPosition.HasValue) transform.position = newPosition.Value;
        ApplyGenderController();
        gameObject.SetActive(true);
    }

    private void Awake()
    {
        CaptureDefaultMaterials();
        anim = GetComponentInChildren<Animator>();
        col = GetComponent<CapsuleCollider>();
        interaction = GetComponent<NPCInteraction>();
        agent = GetComponent<NavMeshAgent>();

        string n = gameObject.name.ToLowerInvariant();
        if (n.Contains("female") || n.Contains("woman") || n.Contains("girl"))
            gender = Gender.Female;

        ApplyGenderSpeeds();
        ApplyGenderController();

        var rb = GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);

        if (col != null)
        {
            col.center = new Vector3(0f, 0.9f, 0f);
            col.radius = 0.35f;
            col.height = 1.8f;
        }

        if (agent != null)
        {
            agent.updatePosition = false;
            agent.updateRotation = false;
        }

        if (anim != null)
        {
            anim.enabled = true;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.updateWhenOffscreen = true;
        }

        raycastMask = ~LayerMask.GetMask("Ignore Raycast", "UI");

        if (walkGraph == null)
        {
            walkGraph = Resources.Load<CityWalkGraph>("CityWalkGraph");
            if (walkGraph == null)
            {
#if UNITY_EDITOR
                walkGraph = UnityEditor.AssetDatabase.LoadAssetAtPath<CityWalkGraph>("Assets/EcoQuest/City Scene/CityWalkGraph.asset");
#endif
            }
        }
    }

    private void OnEnable()
    {
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.enabled = true;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        ApplyGenderController();

        if (interaction == null) interaction = GetComponent<NPCInteraction>();
        if (interaction != null)
        {
            interaction.OnDialogueStart += OnDialogueStarted;
            interaction.OnDialogueEnd += OnDialogueEnded;
        }

        if (hasOutfitConfigured && needsOutfitRebuild && outfitProfile != null)
        {
            needsOutfitRebuild = false;
            currentOutfit = OutfitBuilder.ApplyToNpc(gameObject, outfitProfile, outfitSeed, archetype, allowPhone, allowHandheld, ownedMaterials, defaultSharedMats);
        }

        if (hasStarted)
        {
            if (anim != null)
            {
                anim.Play("Locomotion", 0, 0f);
                anim.SetFloat("Speed", 0f);
            }
            SnapToGround();
            if (inConversation)
            {
                if (anim != null) anim.SetBool("IsTalking", isTalking);
            }
            else
            {
                PickNewDestination();
            }
        }
    }

    private void OnDisable()
    {
        if (interaction != null)
        {
            interaction.OnDialogueStart -= OnDialogueStarted;
            interaction.OnDialogueEnd -= OnDialogueEnded;
        }

        waitCoroutine = null;
        isWaiting = false;
        currentSpeed = 0f;
        if (!inConversation) isTalking = false;

        if (hasOutfitConfigured)
        {
            OutfitBuilder.CleanupNpcOutfit(gameObject, ownedMaterials, defaultSharedMats);
            needsOutfitRebuild = true;
        }
    }

    private void OnDestroy()
    {
        OutfitBuilder.CleanupNpcOutfit(null, ownedMaterials, null);
    }

    private void Start()
    {
        hasStarted = true;
        SnapToGround();

        if (islandId < 0 && walkGraph != null && walkGraph.nodes.Count > 0)
        {
            int closest = walkGraph.FindClosestNode(transform.position, true);
            if (closest >= 0) islandId = walkGraph.nodes[closest].islandId;
        }

        if (!inConversation)
        {
            PickNewDestination();
        }
    }

    private void SnapToGround()
    {
        RaycastHit[] hits = Physics.RaycastAll(transform.position + Vector3.up * 2f, Vector3.down, 5f, raycastMask, QueryTriggerInteraction.Ignore);
        float bestY = -999f;
        foreach (var hit in hits)
        {
            if (hit.collider.transform.root == transform.root) continue;
            if (hit.collider.GetComponentInParent<NpcBrain>() != null) continue;
            if (hit.point.y > bestY)
            {
                bestY = hit.point.y;
            }
        }
        if (bestY > -100f)
        {
            transform.position = new Vector3(transform.position.x, bestY, transform.position.z);
        }
    }

    public void PickNewDestination()
    {
        if (walkGraph == null || walkGraph.nodes.Count == 0 || inConversation) return;

        Vector3 targetPos = walkGraph.GetRandomSidewalkNode(-1, islandId);
        currentPath = walkGraph.FindPath(transform.position, targetPos, islandId);
        currentWaypointIndex = 0;

        isRunning = (Random.value < runChance);
    }

    private void Update()
    {
        if (inConversation)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, 8f * Time.deltaTime);
            if (anim != null) anim.SetFloat("Speed", currentSpeed);

            if (conversationPartner != null)
            {
                Vector3 toPartner = conversationPartner.position - transform.position;
                toPartner.y = 0f;
                if (toPartner.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(toPartner.normalized);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 5f * Time.deltaTime);
                }
            }
            return;
        }

        if (isTalking)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, 8f * Time.deltaTime);
            if (anim != null) anim.SetFloat("Speed", currentSpeed);
            return;
        }

        if (loiteringForQuest)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, 8f * Time.deltaTime);
            if (anim != null) anim.SetFloat("Speed", currentSpeed);
            return;
        }

        if (isWaiting)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, 8f * Time.deltaTime);
            if (anim != null) anim.SetFloat("Speed", currentSpeed);
            return;
        }

        if (currentPath == null || currentWaypointIndex >= currentPath.Count)
        {
            if (IsQuestTarget)
            {
                loiteringForQuest = true;
                currentSpeed = 0f;
                if (anim != null) anim.SetFloat("Speed", 0f);
                return;
            }
            if (!isWaiting)
            {
                StartWaiting();
            }
            return;
        }

        Vector3 targetWaypoint = currentPath[currentWaypointIndex];
        Vector3 toTarget = targetWaypoint - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude <= waypointReachDist)
        {
            if (IsQuestTarget)
            {
                loiteringForQuest = true;
                currentSpeed = 0f;
                if (anim != null) anim.SetFloat("Speed", 0f);
                return;
            }
            currentWaypointIndex++;
            if (currentWaypointIndex >= currentPath.Count)
            {
                StartWaiting();
                return;
            }
            targetWaypoint = currentPath[currentWaypointIndex];
            toTarget = targetWaypoint - transform.position;
            toTarget.y = 0f;
        }

        float targetSpeed = isRunning ? runSpeed : walkSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, 6f * Time.deltaTime);

        if (toTarget.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
        }

        transform.position += transform.forward * currentSpeed * Time.deltaTime;
        SnapToGround();

        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.nextPosition = transform.position;
        }

        if (anim != null)
        {
            anim.SetFloat("Speed", currentSpeed);
        }
    }

    private void StartWaiting()
    {
        if (waitCoroutine != null) StopCoroutine(waitCoroutine);
        waitCoroutine = StartCoroutine(WaitRoutine());
    }

    private IEnumerator WaitRoutine()
    {
        isWaiting = true;
        currentSpeed = 0f;
        if (anim != null) anim.SetFloat("Speed", 0f);

        // Extra idle behaviors while waiting at nodes
        if (Random.value < 0.45f && anim != null)
        {
            int actionType = Random.Range(1, 4); // 1 = Greet/Wave, 2 = Nod, 3 = Happy
            anim.SetInteger("ActionType", actionType);
            anim.SetTrigger("ActionTrigger");
        }

        yield return new WaitForSeconds(Random.Range(idleDurationMin, idleDurationMax));

        isWaiting = false;
        waitCoroutine = null;
        PickNewDestination();
    }

    public void StartConversation(Transform partner)
    {
        inConversation = true;
        conversationPartner = partner;
        if (waitCoroutine != null)
        {
            StopCoroutine(waitCoroutine);
            waitCoroutine = null;
        }
        isWaiting = false;
        currentSpeed = 0f;
        if (anim != null) anim.SetFloat("Speed", 0f);
    }

    public void EndConversation()
    {
        inConversation = false;
        conversationPartner = null;
        SetTalking(false);
        PickNewDestination();
    }

    public void SetTalking(bool talking)
    {
        isTalking = talking;
        if (anim != null)
        {
            anim.SetBool("IsTalking", talking);
        }
    }

    public void TriggerIdleGesture(int type)
    {
        if (anim != null)
        {
            anim.SetInteger("ActionType", type);
            anim.SetTrigger("ActionTrigger");
        }
    }

    private void OnDialogueStarted()
    {
        isTalking = true;
        if (waitCoroutine != null)
        {
            StopCoroutine(waitCoroutine);
            waitCoroutine = null;
        }
        isWaiting = false;
        currentSpeed = 0f;

        if (anim != null)
        {
            anim.SetFloat("Speed", 0f);
            anim.SetBool("IsTalking", true);
        }
    }

    private void OnDialogueEnded()
    {
        isTalking = false;

        if (anim != null)
        {
            anim.SetBool("IsTalking", false);
        }

        PickNewDestination();
    }

    private void OnDrawGizmosSelected()
    {
        if (!showDebugPath || currentPath == null || currentPath.Count == 0) return;

        Gizmos.color = isRunning ? Color.cyan : Color.green;
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Gizmos.DrawLine(currentPath[i] + Vector3.up * 0.2f, currentPath[i + 1] + Vector3.up * 0.2f);
        }
        if (currentWaypointIndex < currentPath.Count)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(currentPath[currentWaypointIndex] + Vector3.up * 0.2f, 0.25f);
        }
    }
}
