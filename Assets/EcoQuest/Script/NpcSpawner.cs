using System;
using System.Collections.Generic;
using UnityEngine;

public class NpcSpawner : MonoBehaviour
{
    [Header("Graph & Population")]
    [SerializeField] private CityWalkGraph walkGraph;
    [SerializeField] private OutfitProfile outfitProfile;
    [SerializeField] private int outfitBaseSeed = 202610;
    [SerializeField] private int targetNpcCount = 20;
    [SerializeField] private float minPlayerDistance = 15f;
    [SerializeField] private float minNpcDistance = 12f;
    [SerializeField] private float neighborArchetypeRadius = 35f;
    [SerializeField] private int talkingPairCount = 2; // 2 pairs = 4 NPCs
    [SerializeField] private int runnerCount = 3;

    [Header("Unique Character Prefabs")]
    [SerializeField] private List<GameObject> npcPrefabs = new List<GameObject>();

    [SerializeField, HideInInspector] private List<NpcBrain> spawnedNpcs = new List<NpcBrain>();
    private Transform playerTransform;

    private struct SpawnCandidate
    {
        public GameObject prefab;
        public OutfitArchetype archetype;
        public Gender gender;
        public bool openHead;
    }

    private struct ArchetypePlacement
    {
        public Vector3 position;
        public int islandId;
        public OutfitArchetype archetype;
    }

    public List<NpcBrain> SpawnedNpcs => spawnedNpcs;

    public static Gender ResolveGender(string prefabName)
    {
        string n = prefabName.ToLowerInvariant();
        if (n.Contains("female") || n.Contains("woman") || n.Contains("girl"))
            return Gender.Female;
        return Gender.Male;
    }

    public static OutfitArchetype GetPreferredArchetype(string prefabName)
    {
        string n = prefabName.ToLowerInvariant();
        if (n.Contains("everyday_female") || n.Contains("womanpirate")) return OutfitArchetype.Artist;
        if (n.Contains("everyday_male") || n.Contains("female02")) return OutfitArchetype.Jogger;
        if (n.Contains("female01") || n.Contains("mancoat")) return OutfitArchetype.OfficeWorker;
        if (n.Contains("female03") || n.Contains("manpolice")) return OutfitArchetype.Tourist;
        if (n.Contains("girlwinter") || n.Contains("_male01")) return OutfitArchetype.Student;
        if (n.Contains("mandoctor") || n.Contains("_male02")) return OutfitArchetype.Shopkeeper;
        if (n.Contains("manswat") || n.Contains("_male03")) return OutfitArchetype.Delivery;
        if (n.Contains("manfire") || n.Contains("_male04")) return OutfitArchetype.Construction;
        if (n.Contains("mansir") || n.Contains("_male05")) return OutfitArchetype.Elder;
        return OutfitArchetype.Casual;
    }

    private void Awake()
    {
        // Remove editor-only outfit preview line if left in scene
        var previewRoot = GameObject.Find("--- NPC Outfit Preview (Editor Only) ---");
        if (previewRoot != null) Destroy(previewRoot);

        if (walkGraph == null)
        {
#if UNITY_EDITOR
            walkGraph = UnityEditor.AssetDatabase.LoadAssetAtPath<CityWalkGraph>("Assets/EcoQuest/City Scene/CityWalkGraph.asset");
#endif
        }

        if (outfitProfile == null)
        {
#if UNITY_EDITOR
            outfitProfile = UnityEditor.AssetDatabase.LoadAssetAtPath<OutfitProfile>("Assets/EcoQuest/City Scene/CityOutfitProfile.asset");
#endif
        }

        if (npcPrefabs == null || npcPrefabs.Count == 0)
        {
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/EcoQuest/Prefabs/NPC" });
            foreach (var g in guids)
            {
                string p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (prefab != null && prefab.GetComponent<NpcBrain>() != null)
                {
                    npcPrefabs.Add(prefab);
                }
            }
#endif
        }
    }

    private void Start()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) playerTransform = playerObj.transform;

        var oldWalkers = GameObject.Find("--- NPC Walkers ---");
        if (oldWalkers != null) Destroy(oldWalkers);

        SpawnAllNpcs();
    }

    public void SpawnAllNpcs()
    {
        spawnedNpcs.Clear();
        if (walkGraph == null || walkGraph.nodes.Count == 0 || npcPrefabs.Count == 0)
        {
            Debug.LogError("NpcSpawner: WalkGraph or Prefabs missing!");
            return;
        }

        if (outfitProfile == null)
        {
            outfitProfile = ScriptableObject.CreateInstance<OutfitProfile>();
            outfitProfile.EnsureDefaultRules();
        }

        // Deterministic sorted candidate pool so seeded spawns are repeatable
        var sortedPrefabs = new List<GameObject>(npcPrefabs);
        sortedPrefabs.RemoveAll(p => p == null);
        sortedPrefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        var pool = new List<SpawnCandidate>(sortedPrefabs.Count);
        foreach (var p in sortedPrefabs)
        {
            pool.Add(new SpawnCandidate
            {
                prefab = p,
                archetype = GetPreferredArchetype(p.name),
                gender = ResolveGender(p.name),
                openHead = !OutfitBuilder.HasBuiltInHeadwear(p.name)
            });
        }

        var rng = new System.Random(outfitBaseSeed);
        var usedSignatures = new HashSet<string>();
        var archetypeRegistry = new List<ArchetypePlacement>();
        var islandNpcCount = new Dictionary<int, int>();
        int islandTotal = Mathf.Max(1, walkGraph.IslandCount);

        GameObject npcContainer = new GameObject("--- City NPCs ---");
        int spawnSeq = 0;

        // 1. Spawn Talking Pairs (allowPhone = true ONLY on talking-pair NPCs; partners never share archetype)
        for (int pairIdx = 0; pairIdx < talkingPairCount; pairIdx++)
        {
            if (pool.Count < 2 || spawnedNpcs.Count + 2 > targetNpcCount) break;

            int targetIsland = pairIdx % islandTotal;
            int nodeIdx = FindSpawnNodeForIsland(targetIsland, rng);
            if (nodeIdx < 0) continue;

            Vector3 nodePos = walkGraph.nodes[nodeIdx].position;
            Vector3 forwardDir = Vector3.forward;
            if (walkGraph.nodes[nodeIdx].neighbors.Count > 0)
            {
                int nIdx = walkGraph.nodes[nodeIdx].neighbors[0];
                forwardDir = (walkGraph.nodes[nIdx].position - nodePos).normalized;
            }

            int idxA = PickCandidateIndex(pool, nodePos, targetIsland, archetypeRegistry, talkSuitableOnly: true, runnerSuitableOnly: false);
            var candA = pool[idxA];
            pool.RemoveAt(idxA);

            var brainA = InstantiateAndDress(candA, nodePos, Quaternion.identity, npcContainer.transform,
                $"{candA.prefab.name}_Pair{pairIdx + 1}_A", targetIsland, isRunner: false, canUsePhone: true, canHoldItem: true,
                spawnSeq++, usedSignatures, archetypeRegistry);

            int idxB = PickCandidateIndex(pool, nodePos, targetIsland, archetypeRegistry, talkSuitableOnly: true, runnerSuitableOnly: false);
            var candB = pool[idxB];
            pool.RemoveAt(idxB);

            var brainB = InstantiateAndDress(candB, nodePos, Quaternion.identity, npcContainer.transform,
                $"{candB.prefab.name}_Pair{pairIdx + 1}_B", targetIsland, isRunner: false, canUsePhone: true, canHoldItem: true,
                spawnSeq++, usedSignatures, archetypeRegistry);

            islandNpcCount[targetIsland] = islandNpcCount.TryGetValue(targetIsland, out int c) ? c + 2 : 2;

            GameObject convObj = new GameObject($"Conversation_Pair_{pairIdx + 1}");
            convObj.transform.parent = npcContainer.transform;
            convObj.transform.position = nodePos;
            var conv = convObj.AddComponent<NpcConversation>();
            conv.SetupPair(brainA, brainB, nodePos, forwardDir);
        }

        // 2. Spawn Walkers & Runners across islands (pass 1: max 2 per island; pass 2: max 3 on largest islands so all 20 spawn)
        int runnerAssigned = 0;
        for (int maxPerIsland = 2; maxPerIsland <= 4 && spawnedNpcs.Count < targetNpcCount && pool.Count > 0; maxPerIsland++)
        {
            for (int currentIsland = 0; currentIsland < islandTotal && spawnedNpcs.Count < targetNpcCount && pool.Count > 0; currentIsland++)
            {
                while (spawnedNpcs.Count < targetNpcCount && pool.Count > 0)
                {
                    int currentCount = islandNpcCount.TryGetValue(currentIsland, out int count) ? count : 0;
                    if (currentCount >= maxPerIsland) break;

                    int nodeIdx = FindSpawnNodeForIsland(currentIsland, rng);
                    if (nodeIdx < 0) break;

                    Vector3 spawnPos = walkGraph.nodes[nodeIdx].position;
                    bool wantRunner = runnerAssigned < runnerCount && HasJoggerOrRunnerCandidate(pool);

                    int candIdx = PickCandidateIndex(pool, spawnPos, currentIsland, archetypeRegistry, talkSuitableOnly: false, runnerSuitableOnly: wantRunner);
                    var cand = pool[candIdx];
                    pool.RemoveAt(candIdx);

                    bool isRunner = cand.archetype == OutfitArchetype.Jogger || (wantRunner && runnerAssigned < runnerCount);
                    if (isRunner) runnerAssigned++;

                    Quaternion rot = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                    string objName = $"{cand.prefab.name}_{(isRunner ? "Runner" : "Walker")}_Island{currentIsland}";

                    InstantiateAndDress(cand, spawnPos, rot, npcContainer.transform,
                        objName, currentIsland, isRunner: isRunner, canUsePhone: false, canHoldItem: !isRunner,
                        spawnSeq++, usedSignatures, archetypeRegistry);

                    islandNpcCount[currentIsland] = currentCount + 1;
                }
            }
        }

        Debug.Log($"<color=green>SUCCESS: Spawned {spawnedNpcs.Count} unique NPCs across {islandNpcCount.Count} islands ({talkingPairCount} talking pairs, {runnerAssigned} runners, {spawnedNpcs.Count - talkingPairCount * 2 - runnerAssigned} walkers).</color>");
    }

    private NpcBrain InstantiateAndDress(
        SpawnCandidate cand,
        Vector3 pos,
        Quaternion rot,
        Transform parent,
        string objName,
        int islandId,
        bool isRunner,
        bool canUsePhone,
        bool canHoldItem,
        int spawnSeq,
        HashSet<string> usedSignatures,
        List<ArchetypePlacement> archetypeRegistry)
    {
        var go = Instantiate(cand.prefab, pos, rot, parent);
        go.name = objName;
        var brain = go.GetComponent<NpcBrain>();
        brain.SetGender(cand.gender);
        brain.ApplyGenderController();
        brain.SetWalkGraph(walkGraph);
        brain.SetIslandId(islandId);
        brain.SetRole(isRunner);

        int seed = outfitBaseSeed + spawnSeq * 137;
        var outfit = brain.ApplyOutfit(outfitProfile, seed, cand.archetype, canUsePhone, canHoldItem);

        // Enforce 100% unique (archetype + palette + accessories) across all spawned NPCs
        int attempt = 1;
        while (outfit != null && !usedSignatures.Add(outfit.signature) && attempt < 64)
        {
            seed = outfitBaseSeed + spawnSeq * 137 + attempt * 53;
            outfit = brain.RerollOutfit(seed);
            attempt++;
        }

        archetypeRegistry.Add(new ArchetypePlacement
        {
            position = pos,
            islandId = islandId,
            archetype = cand.archetype
        });

        var anim = go.GetComponentInChildren<Animator>();
        string ctrlName = anim != null && anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "null";
        Debug.Log($"[NpcSpawner] Spawned {go.name} | Gender: {cand.gender} | Controller: {ctrlName}");

        spawnedNpcs.Add(brain);
        return brain;
    }

    public void DespawnAll()
    {
        foreach (var npc in spawnedNpcs)
        {
            if (npc != null) npc.Despawn();
        }
    }

    public void RespawnAll()
    {
        foreach (var npc in spawnedNpcs)
        {
            if (npc != null) npc.Respawn();
        }
    }

    private static bool HasJoggerOrRunnerCandidate(List<SpawnCandidate> pool)
    {
        foreach (var c in pool)
        {
            if (c.archetype == OutfitArchetype.Jogger || c.archetype == OutfitArchetype.Student || c.archetype == OutfitArchetype.Delivery)
                return true;
        }
        return false;
    }

    private int PickCandidateIndex(
        List<SpawnCandidate> pool,
        Vector3 pos,
        int islandId,
        List<ArchetypePlacement> registry,
        bool talkSuitableOnly,
        bool runnerSuitableOnly)
    {
        float[] radii = { neighborArchetypeRadius, 22f, 12f, 0f };
        foreach (float r in radii)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                var c = pool[i];
                if (talkSuitableOnly && (c.archetype == OutfitArchetype.Jogger || c.archetype == OutfitArchetype.Construction || !c.openHead))
                    continue;
                if (runnerSuitableOnly && c.archetype != OutfitArchetype.Jogger && c.archetype != OutfitArchetype.Student && c.archetype != OutfitArchetype.Delivery)
                    continue;

                if (!HasNeighborConflict(c.archetype, pos, islandId, r, registry))
                    return i;
            }
            // Relax role preference before relaxing neighbor check
            if (talkSuitableOnly || runnerSuitableOnly)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    if (!HasNeighborConflict(pool[i].archetype, pos, islandId, r, registry))
                        return i;
                }
            }
        }
        return 0;
    }

    private static bool HasNeighborConflict(OutfitArchetype arch, Vector3 pos, int islandId, float radius, List<ArchetypePlacement> registry)
    {
        for (int i = 0; i < registry.Count; i++)
        {
            if (registry[i].archetype != arch) continue;
            if (registry[i].islandId == islandId) return true;
            if (radius > 0f && Vector3.Distance(registry[i].position, pos) < radius) return true;
        }
        return false;
    }

    private int FindSpawnNodeForIsland(int islandId, System.Random rng)
    {
        List<int> candidates = new List<int>();
        for (int i = 0; i < walkGraph.nodes.Count; i++)
        {
            if (walkGraph.nodes[i].islandId == islandId)
                candidates.Add(i);
        }
        if (candidates.Count == 0) return -1;

        for (int i = 0; i < candidates.Count; i++)
        {
            int r = rng.Next(i, candidates.Count);
            int tmp = candidates[i];
            candidates[i] = candidates[r];
            candidates[r] = tmp;
        }

        foreach (int idx in candidates)
        {
            Vector3 pos = walkGraph.nodes[idx].position;

            if (playerTransform != null && Vector3.Distance(pos, playerTransform.position) < minPlayerDistance)
                continue;

            bool tooClose = false;
            foreach (var npc in spawnedNpcs)
            {
                if (npc != null && Vector3.Distance(pos, npc.transform.position) < minNpcDistance)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            return idx;
        }

        return candidates[0];
    }
}
