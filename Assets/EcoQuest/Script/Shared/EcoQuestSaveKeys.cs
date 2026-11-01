using UnityEngine;

/// <summary>
/// Single source of truth for all EcoQuest persistent save keys and tutorial progress management.
/// Used by both game runtime systems and Editor reset tools to ensure consistency.
/// </summary>
public static class EcoQuestSaveKeys
{
    // Tutorial & Flow Progress Keys
    public const string TabTipDone = "EcoQuest_TabTipDone";
    public const string SeenInfoCards = "EcoQuest_SeenInfoCards";
    public const string PreQuizScore = "EcoQuest_PreQuizScore";
    public const string PostQuizScore = "EcoQuest_PostQuizScore";
    public const string SequentialWASD = "SequentialWASD_TutorialCompleted";

    /// <summary>
    /// Complete array of all persistent keys related to tutorial and quest progress.
    /// Settings (volume, resolution, graphics) are excluded so player preferences remain intact.
    /// </summary>
    public static readonly string[] AllTutorialProgressKeys = new string[]
    {
        TabTipDone,
        SeenInfoCards,
        PreQuizScore,
        PostQuizScore,
        SequentialWASD
    };

    /// <summary>
    /// Deletes all tutorial and flow progress keys from PlayerPrefs and clears cached modal seen cards.
    /// </summary>
    public static void ClearAllTutorialProgress()
    {
        for (int i = 0; i < AllTutorialProgressKeys.Length; i++)
        {
            if (PlayerPrefs.HasKey(AllTutorialProgressKeys[i]))
            {
                PlayerPrefs.DeleteKey(AllTutorialProgressKeys[i]);
            }
        }
        PlayerPrefs.Save();
        ModalManager.ClearPersistedSeenCards();
    }
}
