#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EcoQuest.Editor
{
    public static class ResetPlayerProgressMenu
    {
        [MenuItem("EcoQuest/Reset Player Progress", false, 50)]
        public static void ResetPlayerProgress()
        {
            int removedCount = EcoQuestSaveKeys.ResetAllPlayerProgress();
            Debug.Log($"[EcoQuest] Reset Player Progress complete. Cleared {removedCount} PlayerPrefs progress key(s) and preserved settings.");
        }
    }
}
#endif
