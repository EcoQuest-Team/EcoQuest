#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class CityWalkGraphEditorWindow : EditorWindow
{
    private const string DefaultGraphPath = "Assets/EcoQuest/City Scene/CityWalkGraph.asset";
    private const float GridCellSize = 8f;

    [SerializeField] private CityWalkGraph walkGraph;
    [SerializeField] private bool showGraphInSceneView = false;
    [SerializeField] private bool editMode = false;
    [SerializeField] private bool addNodeMode = false;
    [SerializeField] private bool boxSelectMode = false;
    [SerializeField] private bool autoSnapY = true;
    [SerializeField] private float viewDistance = 8f;
    [SerializeField] private float nodeHandleSize = 0.22f;
    [SerializeField] private bool defaultsAppliedV2 = false;

    private readonly HashSet<int> selectedIndices = new HashSet<int>();
    private readonly HashSet<int> movedIndices = new HashSet<int>();
    private int activeNodeIndex = -1;
    private int hoveredNodeIndex = -1;

    // Spatial hash & cached classifications (avoids looping 5,900 nodes + Physics.Raycast per frame)
    private readonly Dictionary<long, List<int>> spatialGrid = new Dictionary<long, List<int>>();
    private GroundClass[] cachedClasses = Array.Empty<GroundClass>();
    private CityWalkGraph indexedGraphRef;
    private int indexedNodeCount = -1;
    private bool spatialDirty = true;

    // Reusable visible query & batched line buffers
    private readonly List<int> visibleNodes = new List<int>(256);
    private readonly List<Vector3> batchSidewalkEdges = new List<Vector3>(512);
    private readonly List<Vector3> batchCrosswalkEdges = new List<Vector3>(128);
    private readonly List<Vector3> batchSidewalkNodes = new List<Vector3>(512);
    private readonly List<Vector3> batchCrosswalkNodes = new List<Vector3>(128);
    private readonly List<Vector3> batchRoadNodes = new List<Vector3>(64);
    private readonly List<Vector3> batchSelectedNodes = new List<Vector3>(64);
    private Vector3[] arrSidewalkEdges = Array.Empty<Vector3>();
    private Vector3[] arrCrosswalkEdges = Array.Empty<Vector3>();
    private Vector3[] arrSidewalkNodes = Array.Empty<Vector3>();
    private Vector3[] arrCrosswalkNodes = Array.Empty<Vector3>();
    private Vector3[] arrRoadNodes = Array.Empty<Vector3>();
    private Vector3[] arrSelectedNodes = Array.Empty<Vector3>();
    private Vector3 lastBatchPivot = new Vector3(float.NaN, float.NaN, float.NaN);
    private float lastBatchDist = -1f;
    private float lastBatchHandleSize = -1f;
    private bool batchDirty = true;

    // Box select state
    private bool isDraggingBox = false;
    private Vector2 boxStartPos;
    private Vector2 boxCurrentPos;

    // Validation cache
    private int cachedRoadNodeCount = -1;
    private int cachedIsolatedNodeCount = -1;
    private int cachedComponentCount = -1;
    private string validationReport = "";

    [MenuItem("NPC/Walk Graph Editor")]
    public static void ShowWindow()
    {
        var window = GetWindow<CityWalkGraphEditorWindow>("Walk Graph Editor");
        window.minSize = new Vector2(360, 540);
        window.Show();
    }

    private void OnEnable()
    {
        if (!defaultsAppliedV2)
        {
            showGraphInSceneView = false;
            editMode = false;
            viewDistance = 8f;
            defaultsAppliedV2 = true;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            editMode = false;
        }
        if (walkGraph == null)
        {
            walkGraph = AssetDatabase.LoadAssetAtPath<CityWalkGraph>(DefaultGraphPath);
        }
        spatialDirty = true;
        batchDirty = true;
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        Undo.undoRedoPerformed -= OnUndoRedo;
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
        {
            if (editMode)
            {
                editMode = false;
                addNodeMode = false;
                boxSelectMode = false;
                hoveredNodeIndex = -1;
                Repaint();
                SceneView.RepaintAll();
            }
        }
    }

    private void OnUndoRedo()
    {
        spatialDirty = true;
        batchDirty = true;
        Repaint();
        if (showGraphInSceneView) SceneView.RepaintAll();
    }

    private static long CellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

    private static void PosToCell(Vector3 p, out int cx, out int cz)
    {
        cx = Mathf.FloorToInt(p.x / GridCellSize);
        cz = Mathf.FloorToInt(p.z / GridCellSize);
    }

    private void EnsureSpatialIndex()
    {
        if (walkGraph == null || walkGraph.nodes == null) return;
        if (!spatialDirty && indexedGraphRef == walkGraph && indexedNodeCount == walkGraph.nodes.Count) return;

        spatialGrid.Clear();
        int count = walkGraph.nodes.Count;
        if (cachedClasses.Length != count) cachedClasses = new GroundClass[count];

        int mask = ~LayerMask.GetMask("Ignore Raycast", "UI");
        for (int i = 0; i < count; i++)
        {
            var node = walkGraph.nodes[i];
            PosToCell(node.position, out int cx, out int cz);
            long key = CellKey(cx, cz);
            if (!spatialGrid.TryGetValue(key, out var bucket))
            {
                bucket = new List<int>(16);
                spatialGrid[key] = bucket;
            }
            bucket.Add(i);

            if (node.isCrosswalk)
                cachedClasses[i] = GroundClass.Crosswalk;
            else
                cachedClasses[i] = (node.position.y >= 0.10f) ? GroundClass.Sidewalk : ClassifyPosition(node.position, mask);
        }

        indexedGraphRef = walkGraph;
        indexedNodeCount = count;
        spatialDirty = false;
        batchDirty = true;
    }

    private void RebuildVisibleBatch(Vector3 pivot)
    {
        EnsureSpatialIndex();
        visibleNodes.Clear();
        batchSidewalkEdges.Clear();
        batchCrosswalkEdges.Clear();
        batchSidewalkNodes.Clear();
        batchCrosswalkNodes.Clear();
        batchRoadNodes.Clear();
        batchSelectedNodes.Clear();

        float viewDistSq = viewDistance * viewDistance;
        int minCx = Mathf.FloorToInt((pivot.x - viewDistance) / GridCellSize);
        int maxCx = Mathf.FloorToInt((pivot.x + viewDistance) / GridCellSize);
        int minCz = Mathf.FloorToInt((pivot.z - viewDistance) / GridCellSize);
        int maxCz = Mathf.FloorToInt((pivot.z + viewDistance) / GridCellSize);

        for (int cx = minCx; cx <= maxCx; cx++)
        {
            for (int cz = minCz; cz <= maxCz; cz++)
            {
                if (!spatialGrid.TryGetValue(CellKey(cx, cz), out var bucket)) continue;
                for (int b = 0; b < bucket.Count; b++)
                {
                    int idx = bucket[b];
                    if (idx < 0 || idx >= walkGraph.nodes.Count) continue;
                    Vector3 pos = walkGraph.nodes[idx].position;
                    if ((pos - pivot).sqrMagnitude <= viewDistSq)
                    {
                        visibleNodes.Add(idx);
                    }
                }
            }
        }

        float r = nodeHandleSize;
        Vector3 upLift = new Vector3(0f, 0.03f, 0f);
        Vector3 dx = new Vector3(r, 0f, 0f);
        Vector3 dz = new Vector3(0f, 0f, r);

        for (int v = 0; v < visibleNodes.Count; v++)
        {
            int i = visibleNodes[v];
            var node = walkGraph.nodes[i];
            Vector3 p = node.position + upLift;

            GroundClass gClass = i < cachedClasses.Length ? cachedClasses[i] : GroundClass.Sidewalk;
            List<Vector3> targetNodeList = (gClass == GroundClass.Road)
                ? batchRoadNodes
                : (node.isCrosswalk ? batchCrosswalkNodes : batchSidewalkNodes);

            // Cross marker (2 segments = 4 verts) in batched Handles.DrawLines
            targetNodeList.Add(p - dx); targetNodeList.Add(p + dx);
            targetNodeList.Add(p - dz); targetNodeList.Add(p + dz);

            if (selectedIndices.Contains(i))
            {
                float sr = r * 1.45f;
                Vector3 sdx = new Vector3(sr, 0f, 0f);
                Vector3 sdz = new Vector3(0f, 0f, sr);
                batchSelectedNodes.Add(p - sdx); batchSelectedNodes.Add(p + sdz);
                batchSelectedNodes.Add(p + sdz); batchSelectedNodes.Add(p + sdx);
                batchSelectedNodes.Add(p + sdx); batchSelectedNodes.Add(p - sdz);
                batchSelectedNodes.Add(p - sdz); batchSelectedNodes.Add(p - sdx);
            }

            if (node.neighbors != null)
            {
                List<Vector3> edgeList = node.isCrosswalk ? batchCrosswalkEdges : batchSidewalkEdges;
                for (int n = 0; n < node.neighbors.Count; n++)
                {
                    int neighborIdx = node.neighbors[n];
                    if (neighborIdx > i && neighborIdx < walkGraph.nodes.Count)
                    {
                        edgeList.Add(p);
                        edgeList.Add(walkGraph.nodes[neighborIdx].position + upLift);
                    }
                }
            }
        }

        arrSidewalkEdges = batchSidewalkEdges.ToArray();
        arrCrosswalkEdges = batchCrosswalkEdges.ToArray();
        arrSidewalkNodes = batchSidewalkNodes.ToArray();
        arrCrosswalkNodes = batchCrosswalkNodes.ToArray();
        arrRoadNodes = batchRoadNodes.ToArray();
        arrSelectedNodes = batchSelectedNodes.ToArray();

        lastBatchPivot = pivot;
        lastBatchDist = viewDistance;
        lastBatchHandleSize = nodeHandleSize;
        batchDirty = false;
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("NPC Walk Graph Scene Editor", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        walkGraph = (CityWalkGraph)EditorGUILayout.ObjectField("Target Graph", walkGraph, typeof(CityWalkGraph), false);
        if (EditorGUI.EndChangeCheck())
        {
            selectedIndices.Clear();
            movedIndices.Clear();
            activeNodeIndex = -1;
            hoveredNodeIndex = -1;
            cachedRoadNodeCount = -1;
            spatialDirty = true;
            batchDirty = true;
            if (showGraphInSceneView) SceneView.RepaintAll();
        }

        if (walkGraph == null)
        {
            EditorGUILayout.HelpBox("Assign a CityWalkGraph asset to begin editing.", MessageType.Warning);
            if (GUILayout.Button("Load Default Graph"))
            {
                walkGraph = AssetDatabase.LoadAssetAtPath<CityWalkGraph>(DefaultGraphPath);
                spatialDirty = true;
                batchDirty = true;
            }
            return;
        }

        EditorGUILayout.Space(4);

        EditorGUI.BeginChangeCheck();
        showGraphInSceneView = EditorGUILayout.Toggle("Show graph in Scene view", showGraphInSceneView);
        if (EditorGUI.EndChangeCheck())
        {
            if (!showGraphInSceneView) editMode = false;
            batchDirty = true;
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = editMode ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button(editMode ? "EDIT MODE: ACTIVE (Scene GUI On)" : "EDIT MODE: OFF", GUILayout.Height(30)))
        {
            editMode = !editMode;
            if (editMode) showGraphInSceneView = true;
            hoveredNodeIndex = -1;
            batchDirty = true;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);

        EditorGUILayout.LabelField("View Settings", EditorStyles.boldLabel);
        autoSnapY = EditorGUILayout.Toggle("Auto-Snap Y to Ground", autoSnapY);

        EditorGUI.BeginChangeCheck();
        viewDistance = EditorGUILayout.Slider("Node View Distance", viewDistance, 2f, 60f);
        nodeHandleSize = EditorGUILayout.Slider("Node Handle Size", nodeHandleSize, 0.1f, 0.5f);
        if (EditorGUI.EndChangeCheck())
        {
            batchDirty = true;
            if (showGraphInSceneView) SceneView.RepaintAll();
        }

        EditorGUILayout.Space(4);

        EditorGUILayout.LabelField("Graph & Selection Info", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Total Nodes: {walkGraph.nodes.Count} (Sidewalk: {walkGraph.sidewalkNodeIndices.Count}, Crosswalk: {walkGraph.crosswalkNodeIndices.Count})");
        EditorGUILayout.LabelField($"Selected: {selectedIndices.Count} nodes (Active: {(activeNodeIndex >= 0 ? activeNodeIndex.ToString() : "None")})");
        EditorGUILayout.LabelField($"Pending Moved Nodes: {movedIndices.Count}");

        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("Tool Modes & Hotkeys", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = addNodeMode ? Color.yellow : Color.white;
        if (GUILayout.Button(addNodeMode ? "Add Mode [ON] (Click Ground)" : "Add Mode [A]"))
        {
            addNodeMode = !addNodeMode;
            if (addNodeMode) boxSelectMode = false;
        }

        GUI.backgroundColor = boxSelectMode ? Color.cyan : Color.white;
        if (GUILayout.Button(boxSelectMode ? "Box Select [ON]" : "Box Select [B]"))
        {
            boxSelectMode = !boxSelectMode;
            if (boxSelectMode) addNodeMode = false;
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Delete Selected [Del]")) DeleteSelectedNodes();
        if (GUILayout.Button("Connect Edge [C]")) ConnectSelectedNodes();
        if (GUILayout.Button("Disconnect [X]")) DisconnectSelectedNodes();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        EditorGUILayout.LabelField("Class-Based Selection", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
        if (GUILayout.Button("Select Road Nodes (Errors)")) SelectNodesByClass(GroundClass.Road);
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("Select Sidewalk")) SelectNodesByClass(GroundClass.Sidewalk);
        GUI.backgroundColor = new Color(1f, 1f, 0.5f);
        if (GUILayout.Button("Select Crosswalk")) SelectNodesByClass(GroundClass.Crosswalk);
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Deselect All"))
        {
            selectedIndices.Clear();
            activeNodeIndex = -1;
            batchDirty = true;
            if (showGraphInSceneView) SceneView.RepaintAll();
        }

        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("Edge Rebuilding", EditorStyles.boldLabel);
        if (GUILayout.Button($"Rebuild edges for nearby nodes ({movedIndices.Count} moved / {selectedIndices.Count} selected)"))
        {
            RebuildEdgesForMovedOrSelected();
        }

        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField("Validation & Saving", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Validate Graph")) ValidateGraph();

        GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
        if (GUILayout.Button("Save Graph Asset")) SaveGraph();
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(validationReport))
        {
            EditorGUILayout.HelpBox(validationReport, cachedRoadNodeCount > 0 ? MessageType.Error : MessageType.Info);
        }
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!showGraphInSceneView || walkGraph == null || walkGraph.nodes == null) return;

        Event e = Event.current;
        Camera cam = sceneView.camera;
        if (cam == null) return;

        Vector3 pivot = sceneView.pivot;
        if (batchDirty || spatialDirty
            || (pivot - lastBatchPivot).sqrMagnitude > 0.01f
            || !Mathf.Approximately(viewDistance, lastBatchDist)
            || !Mathf.Approximately(nodeHandleSize, lastBatchHandleSize))
        {
            RebuildVisibleBatch(pivot);
        }

        // Batched drawing ONLY during Repaint (no per-node Handles.Button)
        if (e.type == EventType.Repaint)
        {
            var prevZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            if (arrSidewalkEdges.Length > 0)
            {
                Handles.color = new Color(0.2f, 1f, 0.2f, 0.35f);
                Handles.DrawLines(arrSidewalkEdges);
            }
            if (arrCrosswalkEdges.Length > 0)
            {
                Handles.color = new Color(1f, 0.92f, 0.016f, 0.45f);
                Handles.DrawLines(arrCrosswalkEdges);
            }
            if (arrSidewalkNodes.Length > 0)
            {
                Handles.color = Color.green;
                Handles.DrawLines(arrSidewalkNodes);
            }
            if (arrCrosswalkNodes.Length > 0)
            {
                Handles.color = Color.yellow;
                Handles.DrawLines(arrCrosswalkNodes);
            }
            if (arrRoadNodes.Length > 0)
            {
                Handles.color = Color.red;
                Handles.DrawLines(arrRoadNodes);
            }
            if (arrSelectedNodes.Length > 0)
            {
                Handles.color = new Color(1f, 0.6f, 0.1f);
                Handles.DrawLines(arrSelectedNodes);
            }

            if (editMode && hoveredNodeIndex >= 0 && hoveredNodeIndex < walkGraph.nodes.Count)
            {
                Handles.color = Color.white;
                Handles.DrawWireDisc(walkGraph.nodes[hoveredNodeIndex].position, Vector3.up, nodeHandleSize * 1.4f);
            }
            if (activeNodeIndex >= 0 && activeNodeIndex < walkGraph.nodes.Count)
            {
                Handles.color = Color.cyan;
                Handles.DrawWireDisc(walkGraph.nodes[activeNodeIndex].position, Vector3.up, nodeHandleSize * 1.6f);
            }

            Handles.zTest = prevZTest;
        }

        if (!editMode) return;

        sceneView.wantsMouseMove = true;
        int mask = ~LayerMask.GetMask("Ignore Raycast", "UI");

        // Handle Hotkeys
        if (e.type == EventType.KeyDown && GUIUtility.keyboardControl == 0)
        {
            if (e.keyCode == KeyCode.A)
            {
                addNodeMode = !addNodeMode;
                if (addNodeMode) boxSelectMode = false;
                Repaint();
                e.Use();
            }
            else if (e.keyCode == KeyCode.B)
            {
                boxSelectMode = !boxSelectMode;
                if (boxSelectMode) addNodeMode = false;
                Repaint();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                DeleteSelectedNodes();
                e.Use();
            }
            else if (e.keyCode == KeyCode.C)
            {
                ConnectSelectedNodes();
                e.Use();
            }
            else if (e.keyCode == KeyCode.X)
            {
                DisconnectSelectedNodes();
                e.Use();
            }
            else if (e.keyCode == KeyCode.F && activeNodeIndex >= 0 && activeNodeIndex < walkGraph.nodes.Count)
            {
                sceneView.Frame(new Bounds(walkGraph.nodes[activeNodeIndex].position, Vector3.one * 2f), false);
                e.Use();
            }
        }

        // Update hover target near mouse ONLY from visible spatial-culled nodes; repaint only on hover change
        if (!addNodeMode && !boxSelectMode && e.type == EventType.MouseMove)
        {
            int newHover = FindVisibleNodeNearMouse(e.mousePosition, 15f);
            if (newHover != hoveredNodeIndex)
            {
                hoveredNodeIndex = newHover;
                sceneView.Repaint();
            }
        }

        // Add Node on Ground Click
        if (addNodeMode && !e.alt)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 500f, mask, QueryTriggerInteraction.Ignore))
                {
                    AddNode(hit.point);
                    e.Use();
                }
            }
        }

        // Box Select Mode (operates on spatially visible nodes)
        if (boxSelectMode && !e.alt)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                isDraggingBox = true;
                boxStartPos = e.mousePosition;
                boxCurrentPos = e.mousePosition;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && isDraggingBox)
            {
                boxCurrentPos = e.mousePosition;
                sceneView.Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && isDraggingBox)
            {
                isDraggingBox = false;
                Rect selectRect = GetScreenRect(boxStartPos, boxCurrentPos);
                if (!e.shift) selectedIndices.Clear();

                for (int v = 0; v < visibleNodes.Count; v++)
                {
                    int i = visibleNodes[v];
                    Vector2 guiPt = HandleUtility.WorldToGUIPoint(walkGraph.nodes[i].position);
                    if (selectRect.Contains(guiPt))
                    {
                        selectedIndices.Add(i);
                        activeNodeIndex = i;
                    }
                }
                batchDirty = true;
                Repaint();
                sceneView.Repaint();
                e.Use();
            }

            if (isDraggingBox && e.type == EventType.Repaint)
            {
                Handles.BeginGUI();
                Rect r = GetScreenRect(boxStartPos, boxCurrentPos);
                EditorGUI.DrawRect(r, new Color(0.2f, 0.6f, 1f, 0.2f));
                Handles.DrawSolidRectangleWithOutline(r, new Color(0.2f, 0.6f, 1f, 0.15f), Color.cyan);
                Handles.EndGUI();
            }
        }

        // Click Selection (only checks spatially culled visible nodes near mouse)
        if (!addNodeMode && !boxSelectMode && !e.alt && e.type == EventType.MouseDown && e.button == 0)
        {
            int clickedNode = FindVisibleNodeNearMouse(e.mousePosition, 15f);
            if (clickedNode >= 0)
            {
                if (e.shift)
                {
                    if (selectedIndices.Contains(clickedNode))
                    {
                        selectedIndices.Remove(clickedNode);
                        if (activeNodeIndex == clickedNode) activeNodeIndex = -1;
                    }
                    else
                    {
                        selectedIndices.Add(clickedNode);
                        activeNodeIndex = clickedNode;
                    }
                }
                else
                {
                    selectedIndices.Clear();
                    selectedIndices.Add(clickedNode);
                    activeNodeIndex = clickedNode;
                }
                batchDirty = true;
                Repaint();
                sceneView.Repaint();
                e.Use();
            }
        }

        // Interactive PositionHandle created ONLY for the active selected node
        if (activeNodeIndex >= 0 && activeNodeIndex < walkGraph.nodes.Count)
        {
            var activeNode = walkGraph.nodes[activeNodeIndex];
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(activeNode.position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(walkGraph, "Move Walk Graph Node");

                Vector3 delta = newPos - activeNode.position;
                if (autoSnapY && Physics.Raycast(newPos + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 6f, mask, QueryTriggerInteraction.Ignore))
                {
                    newPos.y = hit.point.y;
                }

                activeNode.position = newPos;
                movedIndices.Add(activeNodeIndex);

                if (selectedIndices.Count > 1)
                {
                    foreach (int idx in selectedIndices)
                    {
                        if (idx == activeNodeIndex || idx < 0 || idx >= walkGraph.nodes.Count) continue;
                        Vector3 otherPos = walkGraph.nodes[idx].position + delta;
                        if (autoSnapY && Physics.Raycast(otherPos + Vector3.up * 2.5f, Vector3.down, out RaycastHit hitOther, 6f, mask, QueryTriggerInteraction.Ignore))
                        {
                            otherPos.y = hitOther.point.y;
                        }
                        walkGraph.nodes[idx].position = otherPos;
                        movedIndices.Add(idx);
                    }
                }

                UpdateNeighborCosts(walkGraph, activeNodeIndex);
                spatialDirty = true;
                batchDirty = true;
                EditorUtility.SetDirty(walkGraph);
                Repaint();
            }
        }
    }

    private int FindVisibleNodeNearMouse(Vector2 mousePos, float pixelThreshold)
    {
        int bestIdx = -1;
        float bestDist = pixelThreshold;

        for (int v = 0; v < visibleNodes.Count; v++)
        {
            int i = visibleNodes[v];
            if (i < 0 || i >= walkGraph.nodes.Count) continue;
            Vector2 screenPos = HandleUtility.WorldToGUIPoint(walkGraph.nodes[i].position);
            float dist = Vector2.Distance(screenPos, mousePos);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    private Rect GetScreenRect(Vector2 p1, Vector2 p2)
    {
        return new Rect(
            Mathf.Min(p1.x, p2.x),
            Mathf.Min(p1.y, p2.y),
            Mathf.Abs(p1.x - p2.x),
            Mathf.Abs(p1.y - p2.y)
        );
    }

    private enum GroundClass { Sidewalk, Crosswalk, Road, Excluded }

    private GroundClass ClassifyPosition(Vector3 pos, int mask)
    {
        if (Physics.Raycast(pos + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 2.5f, mask, QueryTriggerInteraction.Ignore))
        {
            string n = hit.collider.gameObject.name;
            if ((n.StartsWith("Road_03") || n.StartsWith("Road_04")) && hit.point.y <= 0.08f)
                return GroundClass.Crosswalk;
            if (hit.point.y >= 0.12f)
                return GroundClass.Sidewalk;
            if (n.StartsWith("Road_") && hit.point.y <= 0.08f)
                return GroundClass.Road;
        }
        return GroundClass.Excluded;
    }

    private void AddNode(Vector3 pos)
    {
        Undo.RecordObject(walkGraph, "Add Walk Graph Node");

        int mask = ~LayerMask.GetMask("Ignore Raycast", "UI");
        if (autoSnapY && Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, mask, QueryTriggerInteraction.Ignore))
        {
            pos.y = hit.point.y;
        }

        GroundClass gClass = ClassifyPosition(pos, mask);
        var newNode = new CityWalkGraph.Node
        {
            position = pos,
            isCrosswalk = (gClass == GroundClass.Crosswalk),
            neighbors = new List<int>(),
            costs = new List<float>()
        };

        walkGraph.nodes.Add(newNode);
        int newIdx = walkGraph.nodes.Count - 1;
        walkGraph.RebuildCategoryLists();

        selectedIndices.Clear();
        selectedIndices.Add(newIdx);
        activeNodeIndex = newIdx;
        movedIndices.Add(newIdx);
        spatialDirty = true;
        batchDirty = true;

        EditorUtility.SetDirty(walkGraph);
        Repaint();
        SceneView.RepaintAll();
        Debug.Log($"Added Walk Node #{newIdx} at {pos} (Class: {gClass})");
    }

    private void DeleteSelectedNodes()
    {
        if (selectedIndices.Count == 0) return;

        Undo.RecordObject(walkGraph, "Delete Walk Graph Nodes");

        int oldCount = walkGraph.nodes.Count;
        int[] remap = new int[oldCount];
        for (int i = 0; i < oldCount; i++) remap[i] = -1;

        List<CityWalkGraph.Node> remaining = new List<CityWalkGraph.Node>();
        for (int i = 0; i < oldCount; i++)
        {
            if (!selectedIndices.Contains(i))
            {
                remap[i] = remaining.Count;
                remaining.Add(walkGraph.nodes[i]);
            }
        }

        foreach (var node in remaining)
        {
            List<int> newNeighbors = new List<int>();
            List<float> newCosts = new List<float>();
            for (int k = 0; k < node.neighbors.Count; k++)
            {
                int oldN = node.neighbors[k];
                if (oldN >= 0 && oldN < oldCount)
                {
                    int mapped = remap[oldN];
                    if (mapped >= 0)
                    {
                        newNeighbors.Add(mapped);
                        newCosts.Add(node.costs[k]);
                    }
                }
            }
            node.neighbors = newNeighbors;
            node.costs = newCosts;
        }

        walkGraph.nodes = remaining;
        walkGraph.RebuildCategoryLists();

        selectedIndices.Clear();
        movedIndices.Clear();
        activeNodeIndex = -1;
        hoveredNodeIndex = -1;
        spatialDirty = true;
        batchDirty = true;

        EditorUtility.SetDirty(walkGraph);
        Repaint();
        SceneView.RepaintAll();
        Debug.Log($"Deleted {oldCount - remaining.Count} nodes. Remaining nodes: {remaining.Count}");
    }

    private void ConnectSelectedNodes()
    {
        if (selectedIndices.Count < 2)
        {
            Debug.LogWarning("Select at least 2 nodes to connect an edge.");
            return;
        }

        Undo.RecordObject(walkGraph, "Connect Walk Graph Edge");
        int connectedPairs = 0;

        List<int> list = new List<int>(selectedIndices);
        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                int a = list[i];
                int b = list[j];
                if (a < 0 || a >= walkGraph.nodes.Count || b < 0 || b >= walkGraph.nodes.Count) continue;

                var nodeA = walkGraph.nodes[a];
                var nodeB = walkGraph.nodes[b];
                float dist = Vector3.Distance(nodeA.position, nodeB.position);

                if (!nodeA.neighbors.Contains(b))
                {
                    nodeA.neighbors.Add(b);
                    nodeA.costs.Add(dist);
                }
                if (!nodeB.neighbors.Contains(a))
                {
                    nodeB.neighbors.Add(a);
                    nodeB.costs.Add(dist);
                }
                connectedPairs++;
            }
        }

        batchDirty = true;
        EditorUtility.SetDirty(walkGraph);
        SceneView.RepaintAll();
        Debug.Log($"Connected {connectedPairs} edge pair(s) between selected nodes.");
    }

    private void DisconnectSelectedNodes()
    {
        if (selectedIndices.Count == 0) return;

        Undo.RecordObject(walkGraph, "Disconnect Walk Graph Edge");

        if (selectedIndices.Count == 1)
        {
            int target = activeNodeIndex;
            if (target >= 0 && target < walkGraph.nodes.Count)
            {
                var node = walkGraph.nodes[target];
                foreach (int n in node.neighbors)
                {
                    if (n >= 0 && n < walkGraph.nodes.Count)
                    {
                        var neighbor = walkGraph.nodes[n];
                        int idx = neighbor.neighbors.IndexOf(target);
                        if (idx >= 0)
                        {
                            neighbor.neighbors.RemoveAt(idx);
                            neighbor.costs.RemoveAt(idx);
                        }
                    }
                }
                node.neighbors.Clear();
                node.costs.Clear();
                Debug.Log($"Disconnected node #{target} from all neighbors.");
            }
        }
        else
        {
            List<int> list = new List<int>(selectedIndices);
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    int a = list[i];
                    int b = list[j];
                    if (a < 0 || a >= walkGraph.nodes.Count || b < 0 || b >= walkGraph.nodes.Count) continue;

                    var nodeA = walkGraph.nodes[a];
                    var nodeB = walkGraph.nodes[b];

                    int idxB = nodeA.neighbors.IndexOf(b);
                    if (idxB >= 0)
                    {
                        nodeA.neighbors.RemoveAt(idxB);
                        nodeA.costs.RemoveAt(idxB);
                    }

                    int idxA = nodeB.neighbors.IndexOf(a);
                    if (idxA >= 0)
                    {
                        nodeB.neighbors.RemoveAt(idxA);
                        nodeB.costs.RemoveAt(idxA);
                    }
                }
            }
            Debug.Log("Disconnected edges between selected nodes.");
        }

        batchDirty = true;
        EditorUtility.SetDirty(walkGraph);
        SceneView.RepaintAll();
    }

    private void SelectNodesByClass(GroundClass targetClass)
    {
        EnsureSpatialIndex();
        selectedIndices.Clear();

        for (int i = 0; i < walkGraph.nodes.Count; i++)
        {
            GroundClass gClass = i < cachedClasses.Length ? cachedClasses[i] : GroundClass.Sidewalk;
            if (gClass == targetClass)
            {
                selectedIndices.Add(i);
                activeNodeIndex = i;
            }
        }

        batchDirty = true;
        if (showGraphInSceneView) SceneView.RepaintAll();
        Repaint();
        Debug.Log($"Selected {selectedIndices.Count} nodes of class '{targetClass}'.");
    }

    private void RebuildEdgesForMovedOrSelected()
    {
        HashSet<int> targets = new HashSet<int>(movedIndices);
        foreach (int idx in selectedIndices) targets.Add(idx);

        if (targets.Count == 0)
        {
            Debug.LogWarning("No moved or selected nodes to rebuild edges for.");
            return;
        }

        Undo.RecordObject(walkGraph, "Rebuild Edges for Nearby Nodes");
        spatialDirty = true;
        EnsureSpatialIndex();

        int mask = ~LayerMask.GetMask("Ignore Raycast", "UI");
        float maxConnectDist = 1.8f;
        int edgesCreated = 0;

        foreach (int targetIdx in targets)
        {
            if (targetIdx < 0 || targetIdx >= walkGraph.nodes.Count) continue;
            var nodeA = walkGraph.nodes[targetIdx];

            foreach (int n in nodeA.neighbors)
            {
                if (n >= 0 && n < walkGraph.nodes.Count)
                {
                    var neighbor = walkGraph.nodes[n];
                    int idx = neighbor.neighbors.IndexOf(targetIdx);
                    if (idx >= 0)
                    {
                        neighbor.neighbors.RemoveAt(idx);
                        neighbor.costs.RemoveAt(idx);
                    }
                }
            }
            nodeA.neighbors.Clear();
            nodeA.costs.Clear();

            PosToCell(nodeA.position, out int cxA, out int czA);
            for (int cx = cxA - 1; cx <= cxA + 1; cx++)
            {
                for (int cz = czA - 1; cz <= czA + 1; cz++)
                {
                    if (!spatialGrid.TryGetValue(CellKey(cx, cz), out var bucket)) continue;
                    for (int b = 0; b < bucket.Count; b++)
                    {
                        int otherIdx = bucket[b];
                        if (otherIdx == targetIdx) continue;
                        var nodeB = walkGraph.nodes[otherIdx];

                        float dist = Vector3.Distance(nodeA.position, nodeB.position);
                        if (dist > maxConnectDist) continue;

                        if (IsEdgeWalkable(nodeA.position, nodeB.position, mask))
                        {
                            nodeA.neighbors.Add(otherIdx);
                            nodeA.costs.Add(dist);

                            if (!nodeB.neighbors.Contains(targetIdx))
                            {
                                nodeB.neighbors.Add(targetIdx);
                                nodeB.costs.Add(dist);
                            }
                            edgesCreated++;
                        }
                    }
                }
            }
        }

        movedIndices.Clear();
        batchDirty = true;
        EditorUtility.SetDirty(walkGraph);
        if (showGraphInSceneView) SceneView.RepaintAll();
        Debug.Log($"Rebuilt edges for {targets.Count} node(s): created {edgesCreated} bidirectional edge connections.");
    }

    private bool IsEdgeWalkable(Vector3 pA, Vector3 pB, int mask)
    {
        float dist = Vector3.Distance(pA, pB);
        int steps = Mathf.Max(2, Mathf.CeilToInt(dist / 0.35f));

        for (int s = 0; s <= steps; s++)
        {
            Vector3 pt = Vector3.Lerp(pA, pB, (float)s / steps);

            if (Physics.OverlapCapsule(pt + Vector3.up * 0.35f, pt + Vector3.up * 1.5f, 0.35f, mask, QueryTriggerInteraction.Ignore).Length > 0)
            {
                return false;
            }

            if (Physics.Raycast(pt + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3.5f, mask, QueryTriggerInteraction.Ignore))
            {
                string n = hit.collider.gameObject.name;
                bool isCross = (n.StartsWith("Road_03") || n.StartsWith("Road_04")) && hit.point.y <= 0.08f;
                bool isSide = hit.point.y >= 0.12f;
                if (!isCross && !isSide)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    private void UpdateNeighborCosts(CityWalkGraph graph, int nodeIdx)
    {
        if (nodeIdx < 0 || nodeIdx >= graph.nodes.Count) return;
        var node = graph.nodes[nodeIdx];
        for (int i = 0; i < node.neighbors.Count; i++)
        {
            int n = node.neighbors[i];
            if (n >= 0 && n < graph.nodes.Count)
            {
                float d = Vector3.Distance(node.position, graph.nodes[n].position);
                node.costs[i] = d;

                int reverseIdx = graph.nodes[n].neighbors.IndexOf(nodeIdx);
                if (reverseIdx >= 0)
                {
                    graph.nodes[n].costs[reverseIdx] = d;
                }
            }
        }
    }

    public void ValidateGraph()
    {
        if (walkGraph == null || walkGraph.nodes.Count == 0) return;

        int mask = ~LayerMask.GetMask("Ignore Raycast", "UI");
        int roadCount = 0;
        int excludedCount = 0;
        int isolatedCount = 0;

        for (int i = 0; i < walkGraph.nodes.Count; i++)
        {
            var node = walkGraph.nodes[i];
            GroundClass gClass = ClassifyPosition(node.position, mask);
            if (gClass == GroundClass.Road) roadCount++;
            else if (gClass == GroundClass.Excluded) excludedCount++;

            if (node.neighbors == null || node.neighbors.Count == 0) isolatedCount++;
        }

        var visited = new HashSet<int>();
        int components = 0;
        int largestComponentSize = 0;

        for (int i = 0; i < walkGraph.nodes.Count; i++)
        {
            if (visited.Contains(i)) continue;
            components++;
            int count = 0;
            Queue<int> q = new Queue<int>();
            q.Enqueue(i);
            visited.Add(i);

            while (q.Count > 0)
            {
                int curr = q.Dequeue();
                count++;
                foreach (int n in walkGraph.nodes[curr].neighbors)
                {
                    if (n >= 0 && n < walkGraph.nodes.Count && !visited.Contains(n))
                    {
                        visited.Add(n);
                        q.Enqueue(n);
                    }
                }
            }

            if (count > largestComponentSize) largestComponentSize = count;
        }

        cachedRoadNodeCount = roadCount;
        cachedIsolatedNodeCount = isolatedCount;
        cachedComponentCount = components;

        validationReport = $"Validation Summary:\n" +
            $"• Total Nodes: {walkGraph.nodes.Count}\n" +
            $"• Road Nodes (Errors): {roadCount}\n" +
            $"• Excluded Nodes: {excludedCount}\n" +
            $"• Isolated Nodes: {isolatedCount}\n" +
            $"• Connected Components: {components} (Largest: {largestComponentSize} nodes)";

        Debug.Log($"=== Walk Graph Validation ===\n{validationReport}");
        Repaint();
    }

    public void SaveGraph()
    {
        if (walkGraph == null) return;
        walkGraph.RebuildCategoryLists();
        EditorUtility.SetDirty(walkGraph);
        AssetDatabase.SaveAssets();
        Debug.Log($"<color=green>SUCCESS: CityWalkGraph saved ({walkGraph.nodes.Count} nodes, {walkGraph.sidewalkNodeIndices.Count} sidewalk, {walkGraph.crosswalkNodeIndices.Count} crosswalk).</color>");
    }
}
#endif
