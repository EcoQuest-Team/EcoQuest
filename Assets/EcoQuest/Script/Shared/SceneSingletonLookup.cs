using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Caches per-scene singleton lookups across a monotonic scene epoch so absent singletons
/// only scan once per loaded scene instead of every frame.
/// </summary>
public static class SceneSingletonLookup
{
    private static int sceneEpoch = 1;
    private static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEpoch()
    {
        unchecked { sceneEpoch++; }
        if (sceneEpoch == 0) sceneEpoch = 1;
        EnsureHooked();
    }

    private static void EnsureHooked()
    {
        if (hooked) return;
        hooked = true;
        SceneManager.sceneLoaded += OnSceneChanged;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private static void OnSceneChanged(Scene scene, LoadSceneMode mode) { unchecked { sceneEpoch++; } }
    private static void OnSceneUnloaded(Scene scene) { unchecked { sceneEpoch++; } }

    public static T GetOrFind<T>(ref T cached, ref int cachedEpoch, bool includeInactive = true) where T : Component
    {
        if (cached != null) return cached;
        EnsureHooked();
        if (cachedEpoch == sceneEpoch) return null;

        cached = Object.FindFirstObjectByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
        if (SceneManager.GetActiveScene().isLoaded)
        {
            cachedEpoch = sceneEpoch;
        }
        return cached;
    }
}
