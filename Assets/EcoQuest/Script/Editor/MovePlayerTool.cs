using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MovePlayerTool
{
    [MenuItem("Tools/Move Player To City Scene")]
    public static void MovePlayer()
    {
        // Save current scenes to avoid losing data
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        // 1. Load Forest Scene to grab the Player
        string forestScenePath = "Assets/EcoQuest/Forest Scene.unity";
        Scene forestScene = EditorSceneManager.OpenScene(forestScenePath, OpenSceneMode.Single);
        
        GameObject player = GameObject.Find("Player");
        if (player == null)
        {
            Debug.LogError("Could not find the 'Player' GameObject in the Forest Scene!");
            return;
        }

        // 2. Create a temporary prefab out of the player
        string tempPrefabPath = "Assets/EcoQuest/PlayerTempPrefab.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, tempPrefabPath);

        // 3. Load City Scene
        string cityScenePath = "Assets/EcoQuest/Asset/LowPolyMegapolis/Scenes/LowPolyMegapolis_Demo.unity";
        Scene cityScene = EditorSceneManager.OpenScene(cityScenePath, OpenSceneMode.Single);

        // 4. Instantiate the player in the City scene
        GameObject cityPlayer = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        
        // Unpack it completely so it's independent
        PrefabUtility.UnpackPrefabInstance(cityPlayer, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // 5. Delete the temporary prefab
        AssetDatabase.DeleteAsset(tempPrefabPath);

        // 6. Disable existing cameras in the City scene so they don't conflict with the Player's camera
        GameObject camerasGroup = GameObject.Find("Cameras");
        if (camerasGroup != null)
        {
            camerasGroup.SetActive(false);
        }

        // Mark the City scene as dirty so the changes are saved
        EditorSceneManager.MarkSceneDirty(cityScene);

        Debug.Log("Successfully moved Player to City Scene! Existing cameras were disabled so your Player camera works.");
    }
}
