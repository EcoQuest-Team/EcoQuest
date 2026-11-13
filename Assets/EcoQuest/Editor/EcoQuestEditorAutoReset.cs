#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility that automatically resets tutorial and progress flags whenever entering Play Mode,
/// guaranteeing a fresh player experience for testing without requiring manual menu clicks.
/// Controllable via the menu toggle: EcoQuest > Auto Reset Tutorial On Play (Default = ON).
/// </summary>
[InitializeOnLoad]
public static class EcoQuestEditorAutoReset
{
    private const string AutoResetPrefKey = "EcoQuest_AutoResetTutorialOnPlay";
    private const string MenuPath = "EcoQuest/Auto Reset Tutorial On Play";

    public static bool AutoResetEnabled
    {
        get => EditorPrefs.GetBool(AutoResetPrefKey, true); // DEFAULT = ON
        set => EditorPrefs.SetBool(AutoResetPrefKey, value);
    }

    static EcoQuestEditorAutoReset()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem(MenuPath, false, 50)]
    private static void ToggleAutoReset()
    {
        AutoResetEnabled = !AutoResetEnabled;
        Debug.Log($"[FlowTrace] Auto Reset Tutorial On Play is now {(AutoResetEnabled ? "ENABLED (Fresh Start on Play)" : "DISABLED (Persistent Progress)")}.");
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleAutoResetValidate()
    {
        Menu.SetChecked(MenuPath, AutoResetEnabled);
        return true;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // ExitingEditMode fires right before the transition to Play Mode starts and before any scene loads
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            if (AutoResetEnabled)
            {
                ExecuteAutoReset();
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnSubsystemRegistration()
    {
        // Handles "Reload Domain = Disabled" in Enter Play Mode Options
        ResetAllStaticState();
        if (AutoResetEnabled)
        {
            EcoQuestSaveKeys.ClearAllTutorialProgress();
        }
    }

    public static void ExecuteAutoReset()
    {
        EcoQuestSaveKeys.ClearAllTutorialProgress();
        ResetAllStaticState();
        Debug.Log("[FlowTrace] Editor auto-reset: tutorial flags cleared");
    }

    public static void ResetAllStaticState()
    {
        NPCInteraction.ResetStaticFlowState();
        TrashItem.ResetStaticState();
        TrashItem.AreTrashItemsAllowed = false;
        TrashItem.ResetAllTrashItems();
        DragAndDrop.ResetStaticState();
        QuestManager.ResetAllQuestCountsToZero();
        QuestManager.ResetStaticState();
        ModalManager.ClearPersistedSeenCards();
        LitterbugQuestManager.ResetStaticState();
        SequentialWASDTutorial.AllowedState = SequentialWASDTutorial.TutorialState.All;
        TravelGate.ResetStaticState();
        EcoQuest.CityQuest.CityTrashDetectiveManager.ClearCooldownCache();
        PlayerSurvivalStats.ClearSavedNeedsState();
    }
}
#endif
