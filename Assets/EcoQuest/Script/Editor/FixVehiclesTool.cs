using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class FixVehiclesTool
{
    [MenuItem("Tools/Fix Flying Vehicles (Remove Drive Script)")]
    public static void FixVehicles()
    {
        int count = 0;
        
        // Find all SimpleDrive scripts in the scene and remove them
        SimpleDrive[] drives = GameObject.FindObjectsOfType<SimpleDrive>();
        foreach (SimpleDrive drive in drives)
        {
            GameObject.DestroyImmediate(drive);
            count++;
        }

        // Mark scene dirty
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"Removed the SimpleDrive script from {count} vehicles. They will now stay parked correctly!");
    }
}
