using System;
using System.IO;
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
    public const string LastLoadingTipText = "EcoQuest_LastLoadingTipText";

    // Gate & Completion Keys
    public const string ForestQuestAccepted = "EcoQuest_ForestQuestAccepted";
    public const string ForestTrashQuestDone = "EcoQuest_ForestTrashQuestDone";
    public const string ForestRewardClaimed = "EcoQuest_ForestRewardClaimed";
    public const string ForestExplorerGateNotified = "EcoQuest_ForestExplorerGateNotified";
    public const string GuardianCooldownUntil = "EcoQuest_GuardianCooldownUntil";

    // Currency Keys
    public const string PlayerGold = "PlayerGold";
    public const string PlayerCoins = "PlayerCoins";

    // City Trash Detective Keys
    public const string CityBestStars = "EcoQuest_City_BestStars";
    public const string CityBestTime = "EcoQuest_City_BestTime";
    public const string CityBonusPending = "EcoQuest_City_BonusPending";
    public const string CityBonusTaken = "EcoQuest_City_BonusTaken";
    public const string CityLastSeed = "EcoQuest_City_LastSeed";
    public const string CityRobinCooldownRemaining = "EcoQuest_City_RobinCooldownRemaining";
    public const string CityRobinCooldownLegacyUntil = "EcoQuest_RobinCooldownUntil";
    public const string CityTipChecklist = "EcoQuest_CityTip_Checklist";
    public const string CityTipHint = "EcoQuest_CityTip_Hint";

    // Survival / Needs Keys
    public const string PlayerHungerRemaining = "EcoQuest_Player_HungerRemaining";
    public const string PlayerHungerRumble20Shown = "EcoQuest_Player_HungerRumble20Shown";
    public const string PlayerHungerRumble0Shown = "EcoQuest_Player_HungerRumble0Shown";
    public const string PlayerHungerWarn10Shown = "EcoQuest_Player_HungerWarn10Shown";
    public const string PlayerHungerWarn5Shown = "EcoQuest_Player_HungerWarn5Shown";
    public const string PlayerHungerWarn0Shown = "EcoQuest_Player_HungerWarn0Shown";
    public const string PlayerSpeedBuffRemaining = "EcoQuest_Player_SpeedBuffRemaining";
    public const string PlayerSpeedBuffDuration = "EcoQuest_Player_SpeedBuffDuration";
    public const string PlayerSpeedBuffPercent = "EcoQuest_Player_SpeedBuffPercent";
    public const string PlayerStaminaBuffRemaining = "EcoQuest_Player_StaminaBuffRemaining";
    public const string PlayerStaminaBuffDuration = "EcoQuest_Player_StaminaBuffDuration";
    public const string PlayerStaminaBuffPercent = "EcoQuest_Player_StaminaBuffPercent";

    // Inventory Keys
    public const string InventorySlots = "EcoQuest_Inventory_Slots";
    public const string InventorySeenNew = "EcoQuest_Inventory_SeenNew";
    public const string InventoryDebugConsume = "EcoQuest_Inventory_DebugConsume";

    /// <summary>
    /// Complete array of all persistent keys related to player progression, currency, inventory,
    /// survival needs, and tutorial/quest state.
    /// Settings (EcoQuest_Settings_*, graphics, volume, sensitivity) are intentionally excluded.
    /// </summary>
    public static readonly string[] AllTutorialProgressKeys = new string[]
    {
        TabTipDone,
        SeenInfoCards,
        PreQuizScore,
        PostQuizScore,
        SequentialWASD,
        LastLoadingTipText,
        ForestQuestAccepted,
        ForestTrashQuestDone,
        ForestRewardClaimed,
        ForestExplorerGateNotified,
        GuardianCooldownUntil,
        PlayerGold,
        PlayerCoins,
        CityBestStars,
        CityBestTime,
        CityBonusPending,
        CityBonusTaken,
        CityLastSeed,
        CityRobinCooldownRemaining,
        CityRobinCooldownLegacyUntil,
        CityTipChecklist,
        CityTipHint,
        PlayerHungerRemaining,
        PlayerHungerRumble20Shown,
        PlayerHungerRumble0Shown,
        PlayerHungerWarn10Shown,
        PlayerHungerWarn5Shown,
        PlayerHungerWarn0Shown,
        PlayerSpeedBuffRemaining,
        PlayerSpeedBuffDuration,
        PlayerSpeedBuffPercent,
        PlayerStaminaBuffRemaining,
        PlayerStaminaBuffDuration,
        PlayerStaminaBuffPercent,
        InventorySlots,
        InventorySeenNew,
        InventoryDebugConsume
    };

    /// <summary>
    /// Deletes all tutorial and flow progress keys from PlayerPrefs and clears cached modal seen cards.
    /// </summary>
    public static void ClearAllTutorialProgress()
    {
        DeleteProgressKeysInternal();
    }

    /// <summary>
    /// Single runtime entry point for New Game / Editor reset: clears all persistent player
    /// progress, resets loaded runtime managers back to brand-new defaults, and preserves all
    /// EcoQuest_Settings_* player preferences. Returns the number of deleted PlayerPrefs keys.
    /// </summary>
    public static int ResetAllPlayerProgress()
    {
        if (PlayerCurrency.Instance != null)
        {
            PlayerCurrency.Instance.ResetGoldToZero();
        }

        if (EcoQuest.Inventory.InventoryController.Instance != null && EcoQuest.Inventory.InventoryController.Instance.Service != null)
        {
            EcoQuest.Inventory.InventoryController.Instance.Service.Clear();
        }

        if (PlayerSurvivalStats.Instance != null)
        {
            PlayerSurvivalStats.Instance.ResetNeedsToFull();
        }

        NPCInteraction.ResetStaticFlowState();

        if (Application.isPlaying)
        {
            QuizManager.Instance?.ResetQuizzes();
            GameplayControlTipsManager.Instance?.ResetTipsState();
            QuestManager.ResetAllQuestCountsToZero();
            IntroSequenceController.Instance?.SetTrashVisible(false, false, "ResetAllPlayerProgress");
        }

        DeleteCustomSaveFilesIfPresent();
        return DeleteProgressKeysInternal();
    }

    private static int DeleteProgressKeysInternal()
    {
        int removedCount = 0;

        for (int i = 0; i < AllTutorialProgressKeys.Length; i++)
        {
            if (PlayerPrefs.HasKey(AllTutorialProgressKeys[i]))
            {
                PlayerPrefs.DeleteKey(AllTutorialProgressKeys[i]);
                removedCount++;
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
            if (PlayerPrefs.HasKey(key))
            {
                PlayerPrefs.DeleteKey(key);
                removedCount++;
            }
        }

        PlayerPrefs.Save();
        ModalManager.ClearPersistedSeenCards();
        return removedCount;
    }

    private static void DeleteCustomSaveFilesIfPresent()
    {
        string saveDir = Application.persistentDataPath;
        if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir))
        {
            return;
        }

        string[] patterns = { "*.json", "*.sav", "*.dat", "*.bin" };
        for (int p = 0; p < patterns.Length; p++)
        {
            string[] files = Directory.GetFiles(saveDir, patterns[p], SearchOption.TopDirectoryOnly);
            for (int f = 0; f < files.Length; f++)
            {
                try
                {
                    File.Delete(files[f]);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[EcoQuestSaveKeys] Could not delete save file '{files[f]}': {ex.Message}");
                }
            }
        }
    }
}
