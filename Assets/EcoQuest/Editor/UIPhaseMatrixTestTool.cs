using UnityEngine;
using UnityEditor;

namespace EcoQuest.EditorTools
{
    public static class UIPhaseMatrixTestTool
    {
        [MenuItem("Tools/EcoQuest/UI Phase Matrix Test")]
        public static void RunTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[UI Phase Matrix Test] Entering Play Mode to run automated tests...");
                EditorApplication.isPlaying = true;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                return;
            }

            UIPhaseMatrixTestRunner.StartRunner();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
                EditorApplication.delayCall += () => UIPhaseMatrixTestRunner.StartRunner();
            }
        }
    }
}
