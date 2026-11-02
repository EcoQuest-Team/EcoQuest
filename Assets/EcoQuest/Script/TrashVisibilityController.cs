using UnityEngine;

/// <summary>
/// Single source of truth for trash visibility in World 1 (Forest Scene).
/// Evaluates pure persistent state:
/// - questStarted: tutorial completed (EcoQuest_TabTipDone == 1) OR Guardian quest accepted (NPCInteraction.IsAnyQuestActive).
/// - !questCompleted: trash quest not yet completed (!QuestManager.IsQuestComplete && EcoQuest_ForestTrashQuestDone == 0).
/// Transient dialogue, cinematic camera, HUD profiles (ExplorerTravel), pause, and Explorer states MUST NEVER affect trash visibility.
/// </summary>
public static class TrashVisibilityController
{
    /// <summary>
    /// Pure function of persistent gameplay state.
    /// </summary>
    public static bool ShouldTrashBeVisible()
    {
        bool tutorialDone = PlayerPrefs.GetInt(EcoQuestSaveKeys.TabTipDone, 0) == 1;
        bool questStarted = tutorialDone || NPCInteraction.IsAnyQuestActive;
        bool questCompleted = QuestManager.IsQuestComplete ||
                              PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 0) == 1;

        return questStarted && !questCompleted;
    }

    /// <summary>
    /// Idempotent refresh that brings scene trash container and individual items into alignment
    /// with the persistent state. Deposited items are preserved and never respawned.
    /// </summary>
    public static void Refresh(string reason = "")
    {
        bool targetVisible = ShouldTrashBeVisible();
        var intro = IntroSequenceController.Instance;
        if (intro == null)
        {
            intro = Object.FindFirstObjectByType<IntroSequenceController>(FindObjectsInactive.Include);
        }
        if (intro != null)
        {
            intro.ApplyTrashVisibilityState(targetVisible, reason);
        }
    }
}
