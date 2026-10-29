using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class NpcOutfitPreviewWindow : EditorWindow
{
    private const string PreviewRootName = "--- NPC Outfit Preview (Editor Only) ---";
    private const string DefaultProfilePath = "Assets/EcoQuest/City Scene/CityOutfitProfile.asset";
    private const string NpcPrefabFolder = "Assets/EcoQuest/Prefabs/NPC";

    private OutfitProfile profile;
    private int baseSeed = 202610;
    private float spacing = 1.65f;
    private Vector2 scrollPos;

    [InitializeOnLoadMethod]
    private static void RegisterPlayModeCleanup()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            var preview = GameObject.Find(PreviewRootName);
            if (preview != null) DestroyImmediate(preview);
        }
    }

    [MenuItem("NPC/Outfit Preview")]
    public static void OpenWindow()
    {
        var win = GetWindow<NpcOutfitPreviewWindow>("Outfit Preview");
        win.minSize = new Vector2(520f, 400f);
        win.Show();
    }

    private void OnEnable()
    {
        if (profile == null)
            profile = AssetDatabase.LoadAssetAtPath<OutfitProfile>(DefaultProfilePath);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(4f);
        profile = (OutfitProfile)EditorGUILayout.ObjectField("Outfit Profile", profile, typeof(OutfitProfile), false);
        baseSeed = EditorGUILayout.IntField("Base Seed", baseSeed);
        spacing = EditorGUILayout.Slider("Line Spacing (m)", spacing, 1.0f, 3.0f);

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Spawn All 20 in Line (Editor Only)", GUILayout.Height(28f)))
                SpawnPreviewLine();

            if (GUILayout.Button("Reroll All 20", GUILayout.Height(28f)))
            {
                baseSeed = Random.Range(10000, 999999);
                SpawnPreviewLine();
            }

            if (GUILayout.Button("Clear Preview", GUILayout.Height(28f)))
                ClearPreviewLine();
        }

        EditorGUILayout.Space(4f);
        var selectedBrain = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<NpcBrain>()
            : null;

        using (new EditorGUI.DisabledScope(selectedBrain == null))
        {
            string label = selectedBrain != null
                ? $"Reroll Seed ({selectedBrain.name} | Seed {selectedBrain.OutfitSeed})"
                : "Reroll Seed (Select an NPC in Scene)";
            if (GUILayout.Button(label, GUILayout.Height(28f)))
            {
                Undo.RecordObject(selectedBrain, "Reroll NPC Outfit Seed");
                int nextSeed = Random.Range(10000, 999999);
                selectedBrain.RerollOutfit(nextSeed);
                EditorUtility.SetDirty(selectedBrain);
                SceneView.RepaintAll();
            }
        }

        EditorGUILayout.Space(6f);
        DrawNpcList();
    }

    public void SpawnPreviewLine()
    {
        if (profile == null)
            profile = AssetDatabase.LoadAssetAtPath<OutfitProfile>(DefaultProfilePath);
        if (profile == null)
        {
            Debug.LogError("NpcOutfitPreviewWindow: CityOutfitProfile.asset not found.");
            return;
        }

        ClearPreviewLine();

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { NpcPrefabFolder });
        var prefabs = new List<GameObject>();
        foreach (string g in guids)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
            if (p != null && p.GetComponent<NpcBrain>() != null)
                prefabs.Add(p);
        }
        prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        Vector3 origin = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
        {
            Vector3 pivot = SceneView.lastActiveSceneView.pivot;
            if (Physics.Raycast(pivot + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 80f))
                origin = hit.point;
            else
                origin = new Vector3(pivot.x, 0f, pivot.z);
        }

        var root = new GameObject(PreviewRootName);
        root.tag = "EditorOnly";
        Undo.RegisterCreatedObjectUndo(root, "Spawn NPC Outfit Preview");

        var usedSignatures = new HashSet<string>();
        float startX = -((prefabs.Count - 1) * spacing) * 0.5f;

        for (int i = 0; i < prefabs.Count; i++)
        {
            var prefab = prefabs[i];
            Vector3 pos = origin + new Vector3(startX + i * spacing, 0f, 0f);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            inst.transform.position = pos;
            inst.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            inst.tag = "EditorOnly";
            inst.name = $"[Preview_{i + 1:00}]_{prefab.name}";

            var brain = inst.GetComponent<NpcBrain>();
            var arch = NpcSpawner.GetPreferredArchetype(prefab.name);
            bool canPhone = i < 4;
            bool canHold = arch != OutfitArchetype.Jogger;

            int seed = baseSeed + i * 137;
            var outfit = brain.ApplyOutfit(profile, seed, arch, canPhone, canHold);
            int attempt = 1;
            while (outfit != null && !usedSignatures.Add(outfit.signature) && attempt < 64)
            {
                seed = baseSeed + i * 137 + attempt * 53;
                outfit = brain.RerollOutfit(seed);
                attempt++;
            }
        }

        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView != null)
            SceneView.FrameLastActiveSceneView();
        SceneView.RepaintAll();
    }

    public static void ClearPreviewLine()
    {
        var existing = GameObject.Find(PreviewRootName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);
    }

    private void DrawNpcList()
    {
        var brains = FindObjectsByType<NpcBrain>(FindObjectsSortMode.None);
        if (brains == null || brains.Length == 0) return;

        System.Array.Sort(brains, (a, b) => string.CompareOrdinal(a.name, b.name));
        EditorGUILayout.LabelField($"Scene NPCs ({brains.Length})", EditorStyles.boldLabel);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        foreach (var b in brains)
        {
            if (b == null) continue;
            var o = b.CurrentOutfit;
            string summary = o != null
                ? $"{b.Archetype} | {o.paletteSummary} | [{string.Join(", ", o.accessories)}]"
                : $"{b.Archetype} | Seed {b.OutfitSeed}";

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button("Select", GUILayout.Width(52f)))
                {
                    Selection.activeGameObject = b.gameObject;
                    SceneView.FrameLastActiveSceneView();
                }
                if (GUILayout.Button("Reroll", GUILayout.Width(52f)))
                {
                    b.RerollOutfit(Random.Range(10000, 999999));
                    SceneView.RepaintAll();
                }
                EditorGUILayout.LabelField($"{b.name}: {summary}");
            }
        }
        EditorGUILayout.EndScrollView();
    }
}
