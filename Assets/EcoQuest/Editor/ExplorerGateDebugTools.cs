#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor debugging tools for the Explorer Gate in World 1 (Forest Scene).
/// Provides quick actions to set all tasks complete, reset gate flags, and inspect status.
/// </summary>
public static class ExplorerGateDebugTools
{
    private const string MenuCompleteTasks = "EcoQuest/Debug: Complete Forest Tasks";
    private const string MenuResetGate = "EcoQuest/Debug: Reset Gate";
    private const string MenuLogStatus = "EcoQuest/Debug: Log Travel Gate Status";

    [MenuItem(MenuCompleteTasks, false, 110)]
    public static void CompleteForestTasks()
    {
        // 1. Mark tutorial complete
        PlayerPrefs.SetInt(EcoQuestSaveKeys.TabTipDone, 1);
        PlayerPrefs.SetInt(EcoQuestSaveKeys.SequentialWASD, 1);
        PlayerPrefs.SetInt(EcoQuestSaveKeys.PreQuizScore, 5);

        // 2. Mark trash quest complete
        PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 1);

        // 3. Mark post-quiz & reward claim complete
        PlayerPrefs.SetInt(EcoQuestSaveKeys.PostQuizScore, 5);
        PlayerPrefs.SetInt(EcoQuestSaveKeys.ForestRewardClaimed, 1);

        PlayerPrefs.Save();

        // Update in-scene managers if playing
        if (Application.isPlaying)
        {
            var explorer = Object.FindFirstObjectByType<ExplorerGateController>();
            if (explorer != null)
            {
                explorer.UpdateWaypointAndMinimapState();
            }
        }

        Debug.Log($"[TravelGate Debug] All Forest tasks marked COMPLETE in PlayerPrefs! CanTravelToCity is now: {TravelGate.CanTravelToCity}");
    }

    [MenuItem(MenuResetGate, false, 111)]
    public static void ResetGate()
    {
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.ForestTrashQuestDone);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.ForestRewardClaimed);
        PlayerPrefs.DeleteKey(EcoQuestSaveKeys.ForestExplorerGateNotified);
        PlayerPrefs.Save();

        TravelGate.ResetStaticState();

        if (Application.isPlaying)
        {
            var explorer = Object.FindFirstObjectByType<ExplorerGateController>();
            if (explorer != null)
            {
                explorer.UpdateWaypointAndMinimapState();
            }
        }

        Debug.Log($"[TravelGate Debug] Forest Gate flags reset! CanTravelToCity is now: {TravelGate.CanTravelToCity}");
    }

    [MenuItem(MenuLogStatus, false, 112)]
    public static void LogTravelGateStatus()
    {
        Debug.Log($"=== TRAVEL GATE STATUS ===\n" +
                  $"CanTravelToCity: {TravelGate.CanTravelToCity}\n" +
                  $" - 1. Tutorial Complete: {TravelGate.IsTutorialComplete} " +
                  $"(TabTipDone={PlayerPrefs.GetInt(EcoQuestSaveKeys.TabTipDone, 0)}, " +
                  $"WASD={PlayerPrefs.GetInt(EcoQuestSaveKeys.SequentialWASD, 0)}, " +
                  $"PreQuiz={PlayerPrefs.GetInt(EcoQuestSaveKeys.PreQuizScore, -1)})\n" +
                  $" - 2. Trash Quest Complete: {TravelGate.IsTrashQuestComplete} " +
                  $"(IsQuestComplete={QuestManager.IsQuestComplete}, SavedKey={PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 0)})\n" +
                  $" - 3. PostQuiz & Reward: {TravelGate.IsPostQuizAndRewardClaimed} " +
                  $"(PostQuizScore={PlayerPrefs.GetInt(EcoQuestSaveKeys.PostQuizScore, -1)}, " +
                  $"RewardClaimed={PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestRewardClaimed, 0)})\n" +
                  $"Gate Notified: {PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestExplorerGateNotified, 0) == 1}");
    }
}
#endif
