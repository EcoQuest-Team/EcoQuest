using System;
using System.Collections.Generic;
using UnityEngine;

namespace EcoQuest.Inventory
{
    /// <summary>
    /// Master database of all registered EcoQuest inventory items.
    /// Acts as single source of truth for runtime item lookups by string ID.
    /// </summary>
    [CreateAssetMenu(fileName = "InventoryItemDatabase", menuName = "EcoQuest/Inventory/Item Database")]
    public class InventoryItemDatabase : ScriptableObject
    {
        [SerializeField] private List<InventoryItemDef> registeredItems = new List<InventoryItemDef>();

        private readonly Dictionary<string, InventoryItemDef> lookup = new Dictionary<string, InventoryItemDef>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<InventoryItemDef> RegisteredItems => registeredItems;

        private void OnEnable()
        {
            RebuildLookup();
        }

        public void RebuildLookup()
        {
            lookup.Clear();
            if (registeredItems == null) return;
            for (int i = 0; i < registeredItems.Count; i++)
            {
                var item = registeredItems[i];
                if (item != null && !string.IsNullOrEmpty(item.id))
                {
                    lookup[item.id] = item;
                    if (!string.IsNullOrEmpty(item.displayName))
                    {
                        lookup[item.displayName] = item;
                        lookup[item.displayName.Replace(" ", "_").ToLowerInvariant()] = item;
                    }
                    if (item.id.Equals("food_apple", StringComparison.OrdinalIgnoreCase))
                        lookup["apple"] = item;
                    else if (item.id.Equals("food_sandwich", StringComparison.OrdinalIgnoreCase))
                        lookup["sandwich"] = item;
                    else if (item.id.Equals("food_lunchbox", StringComparison.OrdinalIgnoreCase))
                        lookup["lunchbox"] = item;
                    else if (item.id.Equals("energy_drink", StringComparison.OrdinalIgnoreCase) || item.id.Equals("drink_energy", StringComparison.OrdinalIgnoreCase))
                    {
                        lookup["energy_drink"] = item;
                        lookup["energydrink"] = item;
                    }
                    else if (item.id.Equals("boost_speed_short", StringComparison.OrdinalIgnoreCase))
                    {
                        lookup["speed_boost_short"] = item;
                        lookup["speed_potion"] = item;
                    }
                    else if (item.id.Equals("boost_speed_long", StringComparison.OrdinalIgnoreCase))
                    {
                        lookup["speed_boost_long"] = item;
                        lookup["speed_elixir"] = item;
                    }
                }
            }
        }

        public InventoryItemDef GetItem(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (lookup.Count == 0 && registeredItems != null && registeredItems.Count > 0) RebuildLookup();
            lookup.TryGetValue(id, out var def);
            return def;
        }

        public bool Contains(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (lookup.Count == 0 && registeredItems != null && registeredItems.Count > 0) RebuildLookup();
            return lookup.ContainsKey(id);
        }

        public void RegisterItem(InventoryItemDef item)
        {
            if (item == null || string.IsNullOrEmpty(item.id)) return;
            if (!registeredItems.Contains(item))
            {
                registeredItems.Add(item);
            }
            RebuildLookup();
        }
    }
}
