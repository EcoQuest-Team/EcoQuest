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

    // Gate & Completion Keys
    public const string ForestQuestAccepted = "EcoQuest_ForestQuestAccepted";
    public const string ForestTrashQuestDone = "EcoQuest_ForestTrashQuestDone";
    public const string ForestRewardClaimed = "EcoQuest_ForestRewardClaimed";
    public const string ForestExplorerGateNotified = "EcoQuest_ForestExplorerGateNotified";

    // City Trash Detective Keys
    public const string CityBestStars = "EcoQuest_City_BestStars";
    public const string CityBestTime = "EcoQuest_City_BestTime";
    public const string CityBonusPending = "EcoQuest_City_BonusPending";
    public const string CityBonusTaken = "EcoQuest_City_BonusTaken";
    public const string CityLastSeed = "EcoQuest_City_LastSeed";
    public const string CityRobinCooldownRemaining = "EcoQuest_City_RobinCooldownRemaining";
    public const string CityRobinCooldownLegacyUntil = "EcoQuest_RobinCooldownUntil";

    // Survival / Needs Keys
    public const string PlayerHungerRemaining = "EcoQuest_Player_HungerRemaining";
    public const string PlayerHungerRumble20Shown = "EcoQuest_Player_HungerRumble20Shown";
    public const string PlayerHungerRumble0Shown = "EcoQuest_Player_HungerRumble0Shown";
    public const string PlayerSpeedBuffRemaining = "EcoQuest_Player_SpeedBuffRemaining";
    public const string PlayerSpeedBuffDuration = "EcoQuest_Player_SpeedBuffDuration";
    public const string PlayerSpeedBuffPercent = "EcoQuest_Player_SpeedBuffPercent";
    public const string PlayerStaminaBuffRemaining = "EcoQuest_Player_StaminaBuffRemaining";
    public const string PlayerStaminaBuffDuration = "EcoQuest_Player_StaminaBuffDuration";
    public const string PlayerStaminaBuffPercent = "EcoQuest_Player_StaminaBuffPercent";

    // Inventory Keys
    public const string InventorySlots = "EcoQuest_Inventory_Slots";
    public const string InventorySeenNew = "EcoQuest_Inventory_SeenNew";

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
        SequentialWASD,
        ForestQuestAccepted,
        ForestTrashQuestDone,
        ForestRewardClaimed,
        ForestExplorerGateNotified,
        CityBestStars,
        CityBestTime,
        CityBonusPending,
        CityBonusTaken,
        CityLastSeed,
        CityRobinCooldownRemaining,
        CityRobinCooldownLegacyUntil,
        PlayerHungerRemaining,
        PlayerHungerRumble20Shown,
        PlayerHungerRumble0Shown,
        PlayerSpeedBuffRemaining,
        PlayerSpeedBuffDuration,
        PlayerSpeedBuffPercent,
        PlayerStaminaBuffRemaining,
        PlayerStaminaBuffDuration,
        PlayerStaminaBuffPercent,
        InventorySlots,
        InventorySeenNew
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

        string[] binPrefixes = new string[]
        {
            "Biodegradable", "Biodegradable 2", "Biodegradable 3",
            "Recyclable", "Recyclable 2", "Recyclable 3",
            "NonBiodegradable", "NonBiodegradable 2", "NonBiodegradable 3"
        };
        for (int i = 0; i < binPrefixes.Length; i++)
        {
            string key = $"EcoQuest_Bin_{binPrefixes[i]}_Deposits";
            if (PlayerPrefs.HasKey(key)) PlayerPrefs.DeleteKey(key);
        }

        PlayerPrefs.Save();
        ModalManager.ClearPersistedSeenCards();
    }
}
