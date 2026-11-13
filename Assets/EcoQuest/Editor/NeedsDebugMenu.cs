#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EcoQuest.EditorTools
{
    public static class NeedsDebugMenu
    {
        [MenuItem("EcoQuest/Needs/Set Hunger/0")]
        private static void SetHunger0() => SetHunger(0f);

        [MenuItem("EcoQuest/Needs/Set Hunger/5")]
        private static void SetHunger5() => SetHunger(5f);

        [MenuItem("EcoQuest/Needs/Set Hunger/10")]
        private static void SetHunger10() => SetHunger(10f);

        [MenuItem("EcoQuest/Needs/Set Hunger/15")]
        private static void SetHunger15() => SetHunger(15f);

        [MenuItem("EcoQuest/Needs/Set Hunger/25")]
        private static void SetHunger25() => SetHunger(25f);

        [MenuItem("EcoQuest/Needs/Set Hunger/50")]
        private static void SetHunger50() => SetHunger(50f);

        [MenuItem("EcoQuest/Needs/Set Hunger/100")]
        private static void SetHunger100() => SetHunger(100f);

        private static void SetHunger(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, 100f);
            var stats = PlayerSurvivalStats.Instance ?? Object.FindFirstObjectByType<PlayerSurvivalStats>(FindObjectsInactive.Include);
            if (stats != null)
            {
                stats.SetHungerImmediate(clamped, persistNow: true);
            }
            else
            {
                PlayerPrefs.SetFloat(EcoQuestSaveKeys.PlayerHungerRemaining, clamped);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerWarn10Shown, clamped <= 10f ? 1 : 0);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerWarn5Shown, clamped <= 5f ? 1 : 0);
                PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerWarn0Shown, clamped <= 0f ? 1 : 0);
                PlayerPrefs.Save();
            }

            var hud = Object.FindFirstObjectByType<UI_SurvivalStatsHUD>(FindObjectsInactive.Include);
            if (hud != null)
            {
                hud.RefreshImmediateFromStats();
            }

            Debug.Log($"[Needs] Set Hunger to {clamped:F0}%");
        }
    }
}
#endif
