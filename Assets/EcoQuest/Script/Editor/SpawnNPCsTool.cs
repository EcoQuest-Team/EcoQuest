using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class SpawnNPCsTool
{
    [MenuItem("Tools/Spawn All NPCs Around Player")]
    public static void SpawnNPCs()
    {
        // Check if we are in the City scene. If not, open it.
        Scene currentScene = SceneManager.GetActiveScene();
        if (!currentScene.name.Contains("LowPolyMegapolis_Demo"))
        {
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            EditorSceneManager.OpenScene("Assets/EcoQuest/Asset/LowPolyMegapolis/Scenes/LowPolyMegapolis_Demo.unity");
        }

        // Find the player so we can spawn NPCs near them
        GameObject player = GameObject.Find("Player");
        Vector3 spawnCenter = Vector3.zero;
        if (player != null)
        {
            spawnCenter = player.transform.position + new Vector3(0, 0, 5); // 5 units in front of player
        }

        // Find all prefabs in LowPolyCivilians
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/LowPolyCivilians/Prefabs" });
        if (prefabGuids.Length == 0)
        {
            Debug.LogError("No NPC prefabs found in Assets/LowPolyCivilians/Prefabs");
            return;
        }

        // Create a parent object for organization
        GameObject npcGroup = GameObject.Find("NPCs");
        if (npcGroup == null)
        {
            npcGroup = new GameObject("NPCs");
        }

        int count = 0;
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                // Instantiate in a line, slightly spread out
                Vector3 spawnPos = spawnCenter + new Vector3((count * 1.5f) - (prefabGuids.Length * 0.75f), 0, Random.Range(-1f, 1f));
                
                GameObject npc = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                npc.transform.position = spawnPos;
                npc.transform.SetParent(npcGroup.transform);
                
                // Set up the specific animations
                Animation anim = npc.GetComponent<Animation>();
                if (anim != null)
                {
                    List<AnimationClip> goodClips = new List<AnimationClip>();
                    
                    // The civilians have many animations. Let's pick appropriate ones like idle, walk, run.
                    foreach (AnimationState state in anim)
                    {
                        string clipName = state.clip.name.ToLower();
                        // Filter out combat and death for normal civilian usage
                        if (clipName.Contains("idle") || clipName.Contains("walk") || clipName.Contains("run"))
                        {
                            goodClips.Add(state.clip);
                        }
                    }

                    if (goodClips.Count > 0)
                    {
                        // Assign a random realistic civilian animation
                        AnimationClip chosenClip = goodClips[Random.Range(0, goodClips.Count)];
                        anim.clip = chosenClip;
                        anim.playAutomatically = true;
                    }
                }

                count++;
            }
        }

        // Mark scene dirty
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"Successfully spawned {count} NPCs and assigned them specific civilian animations (idle, walk, run)!");
    }
}
