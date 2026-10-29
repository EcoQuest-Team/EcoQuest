using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NpcWalker : MonoBehaviour
{
    [Header("Waypoints")]
    [Tooltip("Specific loop parent container for this NPC")]
    [SerializeField] private Transform waypointContainer;
    [SerializeField] private List<Transform> waypoints = new List<Transform>();
    [SerializeField] private string waypointParentName = "NpcPaths";
    [SerializeField] private bool loopSequentially = true;

    [Header("Movement Speeds")]
    [Tooltip("Walk speed matched to walk animation stride")]
    [SerializeField] private float walkSpeed = 1.4f;
    [Tooltip("Run speed matched to run animation stride")]
    [SerializeField] private float runSpeed = 3.5f;
    [Range(0f, 1f)]
    [SerializeField] private float runChance = 0.25f;
    [SerializeField] private float turnSpeed = 8f;

    [Header("Idle Pause")]
    [Range(0f, 1f)]
    [SerializeField] private float idleChance = 0.4f;
    [SerializeField] private float minIdleTime = 2f;
    [SerializeField] private float maxIdleTime = 4.5f;

    [Header("Components")]
    private NavMeshAgent agent;
    private Animator anim;
    private NPCInteraction interaction;
    private Transform playerTransform;

    private int currentWaypointIndex = -1;
    private bool isWaiting = false;
    private bool isTalking = false;
    private Coroutine waitCoroutine;

    public Transform WaypointContainer
    {
        get => waypointContainer;
        set
        {
            waypointContainer = value;
            LoadWaypointsFromContainer();
        }
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        interaction = GetComponent<NPCInteraction>();
    }

    private void Start()
    {
        if (anim == null) anim = GetComponentInChildren<Animator>();

        // Configure NavMeshAgent physics and avoidance
        if (agent != null)
        {
            agent.speed = walkSpeed;
            agent.acceleration = 10f;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = 0.5f;
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.autoBraking = true;
            agent.updateRotation = false; // Custom smooth rotation toward travel direction
        }

        // Auto-discover waypoints if list is empty
        if (waypoints == null || waypoints.Count == 0)
        {
            LoadWaypointsFromContainer();
        }

        // Player reference
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) playerTransform = p.transform;

        // Hook up dialogue events
        if (interaction != null)
        {
            interaction.OnDialogueStart += OnDialogueStarted;
            interaction.OnDialogueEnd += OnDialogueEnded;
        }

        // Start at closest waypoint or random waypoint on loop
        if (waypoints != null && waypoints.Count > 0)
        {
            currentWaypointIndex = FindClosestWaypointIndex();
            Vector3 startPos = waypoints[currentWaypointIndex].position;
            if (NavMesh.SamplePosition(startPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
            PickNextDestination();
        }
    }

    private void OnDestroy()
    {
        if (interaction != null)
        {
            interaction.OnDialogueStart -= OnDialogueStarted;
            interaction.OnDialogueEnd -= OnDialogueEnded;
        }
    }

    public void LoadWaypointsFromContainer()
    {
        waypoints = new List<Transform>();
        Transform root = waypointContainer;
        if (root == null && !string.IsNullOrEmpty(waypointParentName))
        {
            GameObject found = GameObject.Find(waypointParentName);
            if (found != null) root = found.transform;
        }

        if (root != null)
        {
            // If container has sub-loops and no waypoints directly, check children
            if (root.childCount > 0 && root.GetChild(0).childCount > 0)
            {
                // Root is NpcPaths, take first loop if not specified
                root = root.GetChild(0);
            }

            foreach (Transform child in root)
            {
                waypoints.Add(child);
            }
        }
    }

    public void SetWaypoints(List<Transform> points)
    {
        waypoints = points;
        if (agent != null && agent.isActiveAndEnabled && waypoints != null && waypoints.Count > 0)
        {
            currentWaypointIndex = 0;
            PickNextDestination();
        }
    }

    private int FindClosestWaypointIndex()
    {
        if (waypoints == null || waypoints.Count == 0) return 0;
        int closest = 0;
        float minDist = float.MaxValue;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) continue;
            float d = Vector3.Distance(transform.position, waypoints[i].position);
            if (d < minDist)
            {
                minDist = d;
                closest = i;
            }
        }
        return closest;
    }

    private void Update()
    {
        // Check if currently talking or player nearby
        bool talking = (interaction != null && (interaction.IsTalking || interaction.IsPlayerNearby));
        if (talking != isTalking)
        {
            if (talking) OnDialogueStarted();
            else OnDialogueEnded();
        }

        if (isTalking)
        {
            // Face the player smoothly while talking
            if (playerTransform != null)
            {
                Vector3 lookDir = playerTransform.position - transform.position;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 6f);
                }
            }

            if (anim != null)
            {
                anim.SetFloat("Speed", 0f);
                anim.SetBool("IsTalking", true);
            }
            return;
        }

        if (anim != null)
        {
            anim.SetBool("IsTalking", false);
        }

        // Smooth rotation toward movement direction
        if (agent != null && agent.isActiveAndEnabled && !agent.isStopped && agent.velocity.sqrMagnitude > 0.05f)
        {
            Vector3 moveDir = agent.velocity;
            moveDir.y = 0f;
            if (moveDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * turnSpeed);
            }
        }

        // Check waypoint arrival
        if (!isWaiting && agent.isActiveAndEnabled && agent.isOnNavMesh && !agent.pathPending)
        {
            if (agent.remainingDistance <= agent.stoppingDistance)
            {
                waitCoroutine = StartCoroutine(WaitAtWaypointRoutine());
            }
        }

        // Update Speed parameter on Animator
        if (anim != null && agent.isActiveAndEnabled)
        {
            float speed = agent.velocity.magnitude;
            if (isWaiting) speed = 0f;
            anim.SetFloat("Speed", speed);
        }
    }

    private IEnumerator WaitAtWaypointRoutine()
    {
        isWaiting = true;
        if (agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }

        bool shouldIdle = (Random.value < idleChance);
        if (shouldIdle)
        {
            float waitTime = Random.Range(minIdleTime, maxIdleTime);
            yield return new WaitForSeconds(waitTime);
        }
        else
        {
            yield return new WaitForSeconds(0.2f);
        }

        if (agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }
        isWaiting = false;

        PickNextDestination();
    }

    private void PickNextDestination()
    {
        if (waypoints == null || waypoints.Count == 0) return;

        if (loopSequentially)
        {
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Count;
        }
        else
        {
            int nextIndex = Random.Range(0, waypoints.Count);
            if (waypoints.Count > 1 && nextIndex == currentWaypointIndex)
            {
                nextIndex = (nextIndex + 1) % waypoints.Count;
            }
            currentWaypointIndex = nextIndex;
        }

        // Decide walk or run
        bool willRun = Random.value < runChance;
        agent.speed = willRun ? runSpeed : walkSpeed;

        Vector3 dest = waypoints[currentWaypointIndex].position;
        if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 4f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
        else
        {
            agent.SetDestination(dest);
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

        if (agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }

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

        if (agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }

        if (agent.isActiveAndEnabled && agent.isOnNavMesh && agent.remainingDistance <= agent.stoppingDistance)
        {
            PickNextDestination();
        }
    }
}
