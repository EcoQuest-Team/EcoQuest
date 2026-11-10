using System;
using UnityEngine;

namespace EcoQuest.Inventory
{
    public enum InventoryItemType
    {
        HungerFood = 0,
        SpeedBoost = 1,
        Generic = 2,
        Consumable = 3,
        StaminaFood = 4
    }

    /// <summary>
    /// ScriptableObject defining an immutable inventory item definition.
    /// Supports the 7 Eco Shop consumables (3 Hunger Food, 2 Stamina Recovery, 2 Speed Boost).
    /// </summary>
    [CreateAssetMenu(fileName = "Item_", menuName = "EcoQuest/Inventory/Item Definition")]
    public class InventoryItemDef : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique item identifier string (e.g. food_apple, boost_speed_short).")]
        public string id;

        [Tooltip("User-facing display name formatted in Fredoka.")]
        public string displayName;

        [TextArea(2, 4)]
        [Tooltip("Detailed lore and item description.")]
        public string description;

        [TextArea(1, 2)]
        [Tooltip("Gameplay stat effect summary (e.g. 'Restores 25% Hunger', '+15% Speed Boost for 2 mins').")]
        public string effectText;

        [Header("Visuals")]
        [Tooltip("256x256 transparent square icon.")]
        public Sprite icon;

        [Header("Item Properties")]
        public InventoryItemType itemType = InventoryItemType.Consumable;
        public bool stackable = true;
        public float value = 10f;
        public float restoreHunger = 0f;
        public float restoreStamina = 0f;
        [Tooltip("Percentage boost for timed Stamina Recovery or Speed Boost consumables.")]
        public float boostPercent = 0f;
        public float durationSeconds = 0f;
        [Range(1, 3)]
        public int tier = 1;
        public int maxStack = 99;
        public int sortOrder = 0;
        public int price = 25;

        [Tooltip("Editor-only test item flag.")]
        public bool isTestItem = false;

        public string GetEffectiveEffectText()
        {
            if (!string.IsNullOrEmpty(effectText))
                return effectText;
            if (restoreHunger > 0f && restoreStamina > 0f)
                return $"Restores {Mathf.RoundToInt(restoreHunger)}% Hunger & {Mathf.RoundToInt(restoreStamina)}% Stamina";
            if (restoreHunger > 0f)
                return $"Restores {Mathf.RoundToInt(restoreHunger)}% Hunger";
            if (restoreStamina > 0f)
                return $"Restores {Mathf.RoundToInt(restoreStamina)}% Stamina";
            return string.Empty;
        }
    }
}
