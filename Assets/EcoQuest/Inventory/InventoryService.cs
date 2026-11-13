using System;
using System.Collections.Generic;
using UnityEngine;

namespace EcoQuest.Inventory
{
    public enum BoostGroup
    {
        None = 0,
        SpeedBoost = 1,
        StaminaEnergy = 2
    }

    public interface IInventoryItemEffect
    {
        bool CanUse(InventoryItemDef item, out string message);
        bool Execute(InventoryItemDef item, out string message);
    }

    [Serializable]
    public class InventorySlotData
    {
        public string itemId;
        public int count;

        public bool IsEmpty => string.IsNullOrEmpty(itemId) || count <= 0;

        public void Clear()
        {
            itemId = string.Empty;
            count = 0;
        }
    }

    /// <summary>
    /// Headless runtime inventory data service.
    /// Manages 24 persistent item slots, stacking to 99, effect delegation, and PlayerPrefs synchronization.
    /// Does not contain UI code; communicates via events.
    /// </summary>
    public class InventoryService
    {
        public const int DefaultCapacity = 24;
        public const int MaxStackCount = 99;

        private readonly InventorySlotData[] slots;
        private readonly InventoryItemDatabase database;
        private readonly Dictionary<InventoryItemType, IInventoryItemEffect> effectHandlers =
            new Dictionary<InventoryItemType, IInventoryItemEffect>();
        private bool _isUseInProgress = false;

        public event Action OnInventoryChanged;
        public event Action<InventoryItemDef, int> OnItemAdded;

        public static bool DebugConsumeWithoutEffect
        {
            get => PlayerPrefs.GetInt("EcoQuest_Inventory_DebugConsume", 0) == 1;
            set
            {
                PlayerPrefs.SetInt("EcoQuest_Inventory_DebugConsume", value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public int Capacity => slots.Length;
        public IReadOnlyList<InventorySlotData> Slots => slots;
        public InventoryItemDatabase Database => database;

        public bool HasUnseenNewItems
        {
            get => PlayerPrefs.GetInt(EcoQuestSaveKeys.InventorySeenNew, 0) == 1;
            private set
            {
                PlayerPrefs.SetInt(EcoQuestSaveKeys.InventorySeenNew, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public InventoryService(InventoryItemDatabase db, int capacity = DefaultCapacity)
        {
            database = db;
            slots = new InventorySlotData[Mathf.Max(12, capacity)];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new InventorySlotData();
            }
            Load();
        }

        public void RegisterEffectHandler(InventoryItemType type, IInventoryItemEffect handler)
        {
            if (handler != null) effectHandlers[type] = handler;
        }

        public void UnregisterEffectHandler(InventoryItemType type)
        {
            effectHandlers.Remove(type);
        }

        public void MarkAllSeen()
        {
            HasUnseenNewItems = false;
        }

        public int GetTotalItemCount()
        {
            int total = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].IsEmpty) total += slots[i].count;
            }
            return total;
        }

        public int GetItemCount(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;
            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].itemId == itemId) count += slots[i].count;
            }
            return count;
        }

        public InventoryItemDef GetItemDefAt(int index)
        {
            if (index < 0 || index >= slots.Length || slots[index].IsEmpty) return null;
            return database != null ? database.GetItem(slots[index].itemId) : null;
        }

        /// <summary>
        /// Adds an item to the inventory, prioritizing existing incomplete stacks then empty slots.
        /// Returns the number of items successfully added.
        /// </summary>
        public int AddItem(string itemId, int amount = 1)
        {
            if (string.IsNullOrEmpty(itemId) || amount <= 0) return 0;
            var def = database != null ? database.GetItem(itemId) : null;
            if (def == null)
            {
                Debug.LogWarning($"[InventoryService] Cannot add unregistered item ID: '{itemId}'");
                return 0;
            }

            int remaining = amount;
            int maxStack = def.stackable ? Mathf.Clamp(def.maxStack, 1, MaxStackCount) : 1;

            // 1. Stack into existing slots
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                if ((slots[i].itemId == def.id || slots[i].itemId == itemId) && slots[i].count < maxStack)
                {
                    slots[i].itemId = def.id;
                    int space = maxStack - slots[i].count;
                    int toAdd = Mathf.Min(space, remaining);
                    slots[i].count += toAdd;
                    remaining -= toAdd;
                }
            }

            // 2. Add into empty slots
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                if (slots[i].IsEmpty)
                {
                    int toAdd = Mathf.Min(maxStack, remaining);
                    slots[i].itemId = def.id;
                    slots[i].count = toAdd;
                    remaining -= toAdd;
                }
            }

            int added = amount - remaining;
            if (added > 0)
            {
                HasUnseenNewItems = true;
                Save();
                OnItemAdded?.Invoke(def, added);
                OnInventoryChanged?.Invoke();
            }

            return added;
        }

        /// <summary>
        /// Removes a quantity of an item starting from the last slot.
        /// </summary>
        public bool RemoveItem(string itemId, int amount = 1)
        {
            if (string.IsNullOrEmpty(itemId) || amount <= 0) return false;
            if (GetItemCount(itemId) < amount) return false;

            int remaining = amount;
            for (int i = slots.Length - 1; i >= 0 && remaining > 0; i--)
            {
                if (slots[i].itemId == itemId)
                {
                    if (slots[i].count <= remaining)
                    {
                        remaining -= slots[i].count;
                        slots[i].Clear();
                    }
                    else
                    {
                        slots[i].count -= remaining;
                        remaining = 0;
                    }
                }
            }

            CompactSlots();
            Save();
            OnInventoryChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Removes one item directly from the specified slot index.
        /// When a slot empties, remaining items shift forward to keep slots contiguous.
        /// </summary>
        public bool RemoveOneAt(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slots.Length || slots[slotIndex].IsEmpty || slots[slotIndex].count <= 0)
                return false;
            slots[slotIndex].count = Mathf.Max(0, slots[slotIndex].count - 1);
            if (slots[slotIndex].count <= 0)
            {
                slots[slotIndex].Clear();
                CompactSlots();
            }
            Save();
            OnInventoryChanged?.Invoke();
            return true;
        }

        public static BoostGroup GetBoostGroup(InventoryItemDef def)
        {
            if (def == null) return BoostGroup.None;
            string lowerId = !string.IsNullOrEmpty(def.id) ? def.id.ToLowerInvariant() : string.Empty;
            if (def.itemType == InventoryItemType.SpeedBoost ||
                (def.durationSeconds > 0f && lowerId.Contains("speed")))
            {
                return BoostGroup.SpeedBoost;
            }

            if (def.itemType == InventoryItemType.StaminaFood ||
                (def.durationSeconds > 0f && (def.boostPercent > 0f || lowerId.Contains("energy") || lowerId.Contains("stamina"))))
            {
                return BoostGroup.StaminaEnergy;
            }

            return BoostGroup.None;
        }

        public static bool IsBlockedByActiveBoost(
            InventoryItemDef def,
            PlayerSurvivalStats stats,
            out float remainingSeconds,
            out string blockedMessage)
        {
            remainingSeconds = 0f;
            blockedMessage = string.Empty;
            if (def == null || stats == null) return false;

            BoostGroup group = GetBoostGroup(def);
            if (group == BoostGroup.SpeedBoost && stats.HasActiveSpeedBuff)
            {
                remainingSeconds = stats.SpeedBuffRemaining;
                blockedMessage = "Speed Boost is already active!";
                return true;
            }

            if (group == BoostGroup.StaminaEnergy && stats.HasActiveStaminaBuff)
            {
                remainingSeconds = stats.StaminaBuffRemaining;
                blockedMessage = "Stamina Boost is already active!";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Compacts the slots so that all non-empty items are contiguous starting from index 0.
        /// </summary>
        public void CompactSlots()
        {
            int writeIdx = 0;
            for (int readIdx = 0; readIdx < slots.Length; readIdx++)
            {
                if (!slots[readIdx].IsEmpty)
                {
                    if (writeIdx != readIdx)
                    {
                        slots[writeIdx].itemId = slots[readIdx].itemId;
                        slots[writeIdx].count = slots[readIdx].count;
                        slots[readIdx].Clear();
                    }
                    writeIdx++;
                }
            }
        }

        public int GetOccupiedSlotCount()
        {
            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].IsEmpty) count++;
            }
            return count;
        }

        /// <summary>
        /// Attempts to use the item at the specified slot index.
        /// Applies survival stats (Hunger/Stamina) directly or delegates to registered effect handlers.
        /// </summary>
        public bool TryUse(int slotIndex, out string feedbackMessage)
        {
            bool blockFull = InventoryController.Instance != null ? InventoryController.Instance.BlockUseWhenFull : true;
            return TryUse(slotIndex, blockFull, out feedbackMessage);
        }

        public bool TryUse(int slotIndex, bool blockUseWhenFull, out string feedbackMessage)
        {
            feedbackMessage = string.Empty;
            if (_isUseInProgress)
            {
                return false;
            }

            _isUseInProgress = true;
            try
            {
                if (slotIndex < 0 || slotIndex >= slots.Length || slots[slotIndex].IsEmpty || slots[slotIndex].count <= 0)
                {
                    feedbackMessage = "No item selected";
                    return false;
                }

                string itemId = slots[slotIndex].itemId;
                var def = database != null ? database.GetItem(itemId) : null;
                if (def == null)
                {
                    feedbackMessage = "Unknown item.";
                    return false;
                }

                float hungerGain = def.restoreHunger > 0f ? def.restoreHunger : (def.itemType == InventoryItemType.HungerFood ? def.value : 0f);
                float staminaGain = def.restoreStamina;
                BoostGroup boostGroup = GetBoostGroup(def);

                // 1. Timed Speed Boost Buff (atomic: check mutex -> apply effect first -> consume 1)
                if (boostGroup == BoostGroup.SpeedBoost)
                {
                    var stats = PlayerSurvivalStats.Instance ?? UnityEngine.Object.FindFirstObjectByType<PlayerSurvivalStats>(FindObjectsInactive.Include);
                    if (stats == null)
                    {
                        feedbackMessage = "Cannot use right now.";
                        return false;
                    }

                    if (IsBlockedByActiveBoost(def, stats, out _, out string activeMsg))
                    {
                        feedbackMessage = activeMsg;
                        return false;
                    }

                    float boostPct = def.boostPercent > 0f ? def.boostPercent : 10f;
                    float duration = def.durationSeconds > 0f ? def.durationSeconds : 120f;
                    if (!stats.ApplySpeedBuff(boostPct, duration))
                    {
                        feedbackMessage = "Speed Boost is already active!";
                        return false;
                    }

                    if (!RemoveOneAt(slotIndex))
                    {
                        return false;
                    }

                    var hud = UnityEngine.Object.FindFirstObjectByType<UI_SurvivalStatsHUD>(FindObjectsInactive.Include);
                    if (hud != null)
                    {
                        hud.ShowFloatingGain(false, boostPct);
                    }

                    int mins = Mathf.RoundToInt(duration / 60f);
                    feedbackMessage = $"+{Mathf.RoundToInt(boostPct)}% Speed Boost ({mins} min{(mins > 1 ? "s" : "")})";
                    return true;
                }

                // 2. Timed Stamina Recovery Buff (atomic: check mutex -> apply effect first -> consume 1)
                if (boostGroup == BoostGroup.StaminaEnergy)
                {
                    var stats = PlayerSurvivalStats.Instance ?? UnityEngine.Object.FindFirstObjectByType<PlayerSurvivalStats>(FindObjectsInactive.Include);
                    if (stats == null)
                    {
                        feedbackMessage = "Cannot use right now.";
                        return false;
                    }

                    if (IsBlockedByActiveBoost(def, stats, out _, out string activeMsg))
                    {
                        feedbackMessage = activeMsg;
                        return false;
                    }

                    float boostPct = def.boostPercent > 0f ? def.boostPercent : 5f;
                    float duration = def.durationSeconds > 0f ? def.durationSeconds : 120f;
                    if (!stats.ApplyStaminaBuff(boostPct, duration, staminaGain))
                    {
                        feedbackMessage = "Stamina Boost is already active!";
                        return false;
                    }

                    if (!RemoveOneAt(slotIndex))
                    {
                        return false;
                    }

                    var hud = UnityEngine.Object.FindFirstObjectByType<UI_SurvivalStatsHUD>(FindObjectsInactive.Include);
                    if (hud != null)
                    {
                        hud.ShowFloatingGain(false, boostPct);
                    }

                    int mins = Mathf.RoundToInt(duration / 60f);
                    feedbackMessage = $"+{Mathf.RoundToInt(boostPct)}% Stamina Recovery ({mins} min{(mins > 1 ? "s" : "")})";
                    return true;
                }

                // 3. Instant Consumable / Survival Stats handling (Hunger / Stamina)
                if (hungerGain > 0f || staminaGain > 0f)
                {
                    var stats = PlayerSurvivalStats.Instance ?? UnityEngine.Object.FindFirstObjectByType<PlayerSurvivalStats>(FindObjectsInactive.Include);
                    if (stats == null)
                    {
                        feedbackMessage = "Cannot use right now.";
                        return false;
                    }

                    bool isHungerFull = stats.CurrentHunger >= (stats.maxHunger - 1.0f);
                    bool isStaminaFull = stats.CurrentStamina >= (stats.maxStamina - 1.0f);

                    if (blockUseWhenFull)
                    {
                        if (hungerGain > 0f && staminaGain <= 0f && isHungerFull)
                        {
                            feedbackMessage = "Hunger is already full";
                            return false;
                        }
                        if (staminaGain > 0f && hungerGain <= 0f && isStaminaFull)
                        {
                            feedbackMessage = "Stamina is already full";
                            return false;
                        }
                        if (hungerGain > 0f && staminaGain > 0f && isHungerFull && isStaminaFull)
                        {
                            feedbackMessage = "Hunger is already full";
                            return false;
                        }
                    }

                    if (hungerGain > 0f) stats.AddHunger(hungerGain);
                    if (staminaGain > 0f) stats.AddStamina(staminaGain);

                    if (!RemoveOneAt(slotIndex))
                    {
                        return false;
                    }

                    var hud = UnityEngine.Object.FindFirstObjectByType<UI_SurvivalStatsHUD>(FindObjectsInactive.Include);
                    if (hud != null)
                    {
                        if (hungerGain > 0f) hud.ShowFloatingGain(true, hungerGain);
                        if (staminaGain > 0f) hud.ShowFloatingGain(false, staminaGain);
                    }

                    if (hungerGain > 0f && staminaGain > 0f)
                        feedbackMessage = $"+{Mathf.RoundToInt(hungerGain)} Hunger, +{Mathf.RoundToInt(staminaGain)} Stamina";
                    else if (hungerGain > 0f)
                        feedbackMessage = $"+{Mathf.RoundToInt(hungerGain)} Hunger";
                    else
                        feedbackMessage = $"+{Mathf.RoundToInt(staminaGain)} Stamina";
                    return true;
                }

                // Debug consume toggle (editor testing)
                if (DebugConsumeWithoutEffect)
                {
                    if (!RemoveOneAt(slotIndex)) return false;
                    feedbackMessage = $"Used {def.displayName} (Debug Mode)";
                    return true;
                }

                // Check effect handler by item type
                if (!effectHandlers.TryGetValue(def.itemType, out var handler) || handler == null)
                {
                    feedbackMessage = "This item cannot be used yet.";
                    return false;
                }

                if (!handler.CanUse(def, out string reason))
                {
                    feedbackMessage = !string.IsNullOrEmpty(reason) ? reason : "Cannot use this item right now.";
                    return false;
                }

                if (handler.Execute(def, out string resultMsg))
                {
                    if (!RemoveOneAt(slotIndex)) return false;
                    feedbackMessage = !string.IsNullOrEmpty(resultMsg) ? resultMsg : $"Used {def.displayName}!";
                    return true;
                }
                else
                {
                    feedbackMessage = !string.IsNullOrEmpty(resultMsg) ? resultMsg : "Failed to use item.";
                    return false;
                }
            }
            finally
            {
                _isUseInProgress = false;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].Clear();
            }
            Save();
            OnInventoryChanged?.Invoke();
        }

        public void Save()
        {
            var parts = new List<string>(slots.Length);
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].IsEmpty)
                {
                    parts.Add($"{slots[i].itemId}:{slots[i].count}");
                }
            }
            string raw = string.Join(";", parts);
            PlayerPrefs.SetString(EcoQuestSaveKeys.InventorySlots, raw);
            PlayerPrefs.Save();
        }

        public void Load()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].Clear();
            }

            string raw = PlayerPrefs.GetString(EcoQuestSaveKeys.InventorySlots, string.Empty);
            if (string.IsNullOrEmpty(raw)) return;

            string[] tokens = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
            int slotIdx = 0;
            for (int i = 0; i < tokens.Length && slotIdx < slots.Length; i++)
            {
                string[] kv = tokens[i].Split(':');
                if (kv.Length == 2 && int.TryParse(kv[1], out int cnt) && cnt > 0)
                {
                    string id = kv[0];
                    if (database == null || database.Contains(id))
                    {
                        slots[slotIdx].itemId = id;
                        slots[slotIdx].count = cnt;
                        slotIdx++;
                    }
                }
            }
        }
    }
}
