using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AnimateEnvironmentTool
{
    [MenuItem("Tools/Animate City Environment (Balloons, Vehicles, Sky)")]
    public static void AnimateEnvironment()
    {
        // Check if we are in the City scene. If not, open it.
        Scene currentScene = SceneManager.GetActiveScene();
        if (!currentScene.name.Contains("LowPolyMegapolis_Demo"))
        {
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            EditorSceneManager.OpenScene("Assets/EcoQuest/Asset/LowPolyMegapolis/Scenes/LowPolyMegapolis_Demo.unity");
        }

        int animatedCount = 0;

        // 1. Animate Air Balloons
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.ToLower().Contains("balloon"))
            {
                if (obj.GetComponent<SimpleFloat>() == null)
                {
                    SimpleFloat sf = obj.AddComponent<SimpleFloat>();
                    sf.floatSpeed = Random.Range(0.3f, 0.7f);
                    sf.floatHeight = Random.Range(1f, 3f);
                    animatedCount++;
                }
            }

            // 2. Animate Wind Turbines or Clouds (if applicable)
            if (obj.name.ToLower().Contains("cloud") || obj.name.ToLower().Contains("turbine") || obj.name.ToLower().Contains("rotor"))
            {
                if (obj.GetComponent<SimpleRotate>() == null)
                {
                    SimpleRotate sr = obj.AddComponent<SimpleRotate>();
                    sr.rotationSpeed = new Vector3(0, Random.Range(2f, 5f), 0);
                    if (obj.name.ToLower().Contains("turbine") || obj.name.ToLower().Contains("rotor"))
                    {
                        sr.rotationSpeed = new Vector3(0, 0, Random.Range(40f, 80f)); // Turbines usually rotate on Z
                    }
                    animatedCount++;
                }
            }

            // 3. Animate Vehicles
            // In LowPolyMegapolis, vehicles are often under a "Vehicles" parent, but we can just check names or parents.
            if (obj.name.ToLower().Contains("car") || obj.name.ToLower().Contains("bus") || obj.name.ToLower().Contains("truck") || obj.name.ToLower().Contains("van"))
            {
                // Only animate root vehicle objects, not wheels
                if (!obj.name.ToLower().Contains("wheel") && !obj.name.ToLower().Contains("glass"))
                {
                    if (obj.GetComponent<SimpleDrive>() == null && obj.GetComponent<MeshRenderer>() != null)
                    {
                        SimpleDrive sd = obj.AddComponent<SimpleDrive>();
                        sd.speed = Random.Range(10f, 25f);
                        animatedCount++;
                    }
                }
            }
        }

        // Mark scene dirty
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"Successfully animated {animatedCount} environment objects (Balloons, Vehicles, Clouds/Turbines)!");
    }
}
