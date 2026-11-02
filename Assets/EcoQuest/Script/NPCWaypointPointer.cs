using UnityEngine;

[DisallowMultipleComponent]
public class NPCWaypointPointer : MonoBehaviour
{
    [Header("Target & Positioning")]
    [Tooltip("The NPC object to point at. Defaults to this GameObject if unassigned.")]
    public Transform targetNPC;
    public Vector3 overheadOffset = new Vector3(0f, 2.5f, 0f);

    [Header("Arrow Styling & Animation")]
    public Color arrowColor = new Color(1f, 0.85f, 0.1f); // Gold
    public float arrowScale = 0.6f;
    public float bounceSpeed = 3.5f;
    public float bounceHeight = 0.25f;
    public float rotationSpeed = 60f;

    [Header("Visibility")]
    [Tooltip("Hide arrow when player is within this distance (in meters)")]
    public float hideDistanceThreshold = 2.5f;

    private GameObject overheadArrowObj;
    private Camera mainCam;
    private Transform playerTransform;

    private void Start()
    {
        if (ObjectiveMarkerService.Instance != null) return;
        if (targetNPC == null) targetNPC = transform;
        mainCam = Camera.main;

        Create3DOverheadArrow();
    }

    private void Update()
    {
        // Defer to unified ObjectiveMarkerService if present
        if (ObjectiveMarkerService.Instance != null)
        {
            if (overheadArrowObj != null && overheadArrowObj.activeSelf)
            {
                overheadArrowObj.SetActive(false);
            }
            return;
        }

        if (targetNPC == null) return;
        if (mainCam == null) mainCam = Camera.main;

        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }

        // Calculate distance from player (or camera fallback)
        float distance = playerTransform != null 
            ? Vector3.Distance(playerTransform.position, targetNPC.position) 
            : (mainCam != null ? Vector3.Distance(mainCam.transform.position, targetNPC.position) : 100f);

        // Check if WASD tutorial is finished before showing NPC arrow
        bool isTutorialDone = SequentialWASDTutorial.IsTutorialCompleted
                              || FindFirstObjectByType<SequentialWASDTutorial>() == null;

        // Hide arrow if player is actively talking to NPC or currently doing an active quest
        NPCInteraction npcInteraction = targetNPC != null ? targetNPC.GetComponent<NPCInteraction>() : null;
        bool isTalking = npcInteraction != null && npcInteraction.IsTalking;
        bool isQuestActive = npcInteraction != null && npcInteraction.IsQuestActive;

        bool shouldShow = isTutorialDone && !isTalking && !isQuestActive && (distance > hideDistanceThreshold);

        if (overheadArrowObj != null)
        {
            if (shouldShow)
            {
                // Smooth scale-in transition
                currentScaleFactor = Mathf.MoveTowards(currentScaleFactor, 1f, Time.deltaTime * 2.5f);
                overheadArrowObj.SetActive(true);
                overheadArrowObj.transform.localScale = Vector3.one * (arrowScale * currentScaleFactor);

                // Smooth bouncing & spinning animation
                float bounce = Mathf.Sin(Time.time * bounceSpeed) * bounceHeight;
                overheadArrowObj.transform.position = targetNPC.position + overheadOffset + new Vector3(0f, bounce, 0f);
                overheadArrowObj.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
            }
            else
            {
                if (currentScaleFactor > 0f)
                {
                    currentScaleFactor = Mathf.MoveTowards(currentScaleFactor, 0f, Time.deltaTime * 3.5f);
                    overheadArrowObj.transform.localScale = Vector3.one * (arrowScale * currentScaleFactor);
                    if (currentScaleFactor <= 0.01f)
                    {
                        currentScaleFactor = 0f;
                        overheadArrowObj.SetActive(false);
                    }
                }
                else
                {
                    overheadArrowObj.SetActive(false);
                }
            }
        }
    }

    private float currentScaleFactor = 0f;

    private void Create3DOverheadArrow()
    {
        overheadArrowObj = new GameObject("NPC_3D_Pointer_Arrow");
        overheadArrowObj.transform.SetParent(transform);

        MeshFilter filter = overheadArrowObj.AddComponent<MeshFilter>();
        MeshRenderer renderer = overheadArrowObj.AddComponent<MeshRenderer>();

        filter.mesh = GenerateArrowMesh();

        // Material with URP Lit or Simple Lit support
        Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpShader == null) urpShader = Shader.Find("Universal Render Pipeline/Simple Lit");
        Material mat = new Material(urpShader);

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", arrowColor);
        else mat.color = arrowColor;
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", arrowColor * 0.5f);
        }
        renderer.material = mat;
        overheadArrowObj.transform.localScale = Vector3.one * arrowScale;
    }

    private Mesh GenerateArrowMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "3DPointingArrowMesh";

        // 3D Arrow pointing DOWN at tip (0,0,0)
        Vector3[] vertices = new Vector3[]
        {
            // Cone Tip (Pointed down at NPC)
            new Vector3(0f, 0f, 0f),               // 0: Tip bottom

            // Cone Base Outer Ring
            new Vector3(-0.4f, 0.6f, -0.4f),     // 1
            new Vector3( 0.4f, 0.6f, -0.4f),     // 2
            new Vector3( 0.4f, 0.6f,  0.4f),     // 3
            new Vector3(-0.4f, 0.6f,  0.4f),     // 4

            // Shaft Bottom (Connects to Cone top)
            new Vector3(-0.15f, 0.6f, -0.15f),  // 5
            new Vector3( 0.15f, 0.6f, -0.15f),  // 6
            new Vector3( 0.15f, 0.6f,  0.15f),  // 7
            new Vector3(-0.15f, 0.6f,  0.15f),  // 8

            // Shaft Top
            new Vector3(-0.15f, 1.2f, -0.15f),  // 9
            new Vector3( 0.15f, 1.2f, -0.15f),  // 10
            new Vector3( 0.15f, 1.2f,  0.15f),  // 11
            new Vector3(-0.15f, 1.2f,  0.15f)   // 12
        };

        int[] triangles = new int[]
        {
            // Cone Sides (Pointing down to vertex 0)
            0, 2, 1,
            0, 3, 2,
            0, 4, 3,
            0, 1, 4,

            // Cone Top Ring
            1, 2, 3,
            1, 3, 4,

            // Shaft Sides
            5, 9, 6,   6, 9, 10,
            6, 10, 7,  7, 10, 11,
            7, 11, 8,  8, 11, 12,
            8, 12, 5,  5, 12, 9,

            // Shaft Top Cap
            9, 11, 10,
            9, 12, 11
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}
