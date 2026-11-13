#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EcoQuest.EditorTools
{
    public static class NeedsDebugMenu
    {
        [MenuItem("EcoQuest/Needs/Set Hunger/25")]
        private static void SetHunger25() => SetHunger(25f);

        [MenuItem("EcoQuest/Needs/Set Hunger/50")]
        private static void SetHunger50() => SetHunger(50f);

        [MenuItem("EcoQuest/Needs/Set Hunger/100")]
        private static void SetHunger100() => SetHunger(100f);

        private static void SetHunger(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, 100f);
            PlayerPrefs.SetFloat(EcoQuestSaveKeys.PlayerHungerRemaining, clamped);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerRumble20Shown, clamped <= 20f ? 1 : 0);
            PlayerPrefs.SetInt(EcoQuestSaveKeys.PlayerHungerRumble0Shown, clamped <= 0f ? 1 : 0);
            PlayerPrefs.Save();

            var stats = PlayerSurvivalStats.Instance ?? Object.FindFirstObjectByType<PlayerSurvivalStats>(FindObjectsInactive.Include);
            if (stats != null)
            {
                stats.SetHungerImmediate(clamped, persistNow: true);
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
