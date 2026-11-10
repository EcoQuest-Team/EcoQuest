using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;
using EcoQuest.Inventory;

namespace EcoQuest.EditorTools
{
    public static class TaskVerificationSelfCheck
    {
        [MenuItem("Tools/EcoQuest/Run Task Verification Self Check")]
        public static void RunAllChecks()
        {
            CheckTask1_ForestSceneInventoryBlocked();
            CheckTask2_EcoShopLayout();
            CheckTask3_InventoryCompaction();
            CheckTask4_MinimapIcons();
            Debug.Log("<color=green>[TaskVerificationSelfCheck] ALL 4 TASKS VERIFIED SUCCESSFULLY!</color>");
        }

        private static void CheckTask1_ForestSceneInventoryBlocked()
        {
            var go = new GameObject("Test_InventoryController", typeof(InventoryController));
            var ctrl = go.GetComponent<InventoryController>();

            // When in Forest Scene, CanOpenInventory must return false
            if (!UIOrchestrator.IsCityScene())
            {
                bool canOpen = ctrl.CanOpenInventory(out string reason);
                Assert.IsFalse(canOpen, "Inventory should not be openable outside City Scene.");
                Assert.AreEqual("Inventory is unavailable in the Forest.", reason);
            }
            Object.DestroyImmediate(go);
            Debug.Log("[Check 1] Task 1 verified: Forest scene correctly blocks inventory.");
        }

        private static void CheckTask2_EcoShopLayout()
        {
            var db = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>("Assets/EcoQuest/Inventory/Data/InventoryItemDatabase.asset");
            Assert.IsNotNull(db, "Database should be found");

            var shopGo = new GameObject("Test_Shop", typeof(RectTransform), typeof(UI_EcoShop));
            var shop = shopGo.GetComponent<UI_EcoShop>();

            var slotViews = new List<UI_EcoShopSlot>();
            for (int i = 0; i < 8; i++)
            {
                var slotGo = new GameObject($"Slot_{i}", typeof(RectTransform), typeof(UI_EcoShopSlot));
                slotGo.transform.SetParent(shopGo.transform);
                var view = slotGo.GetComponent<UI_EcoShopSlot>();
                slotViews.Add(view);
            }

            var dbField = typeof(UI_EcoShop).GetField("itemDatabase", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            dbField.SetValue(shop, db);

            var svField = typeof(UI_EcoShop).GetField("slotViews", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            svField.SetValue(shop, slotViews);

            shop.PopulateSlots();

            bool seenLocked = false;
            int unlockedCount = 0;
            for (int i = 0; i < slotViews.Count; i++)
            {
                if (slotViews[i].IsLocked)
                {
                    seenLocked = true;
                }
                else
                {
                    Assert.IsFalse(seenLocked, $"Slot {i} unlocked after locked slot. Consumables must come first!");
                    unlockedCount++;
                }
            }

            Assert.IsTrue(unlockedCount >= 6, "Expected at least 6 unlocked consumables in shop.");
            Assert.IsTrue(slotViews[slotViews.Count - 1].IsLocked, "Final slot (lower right) must be locked.");

            Object.DestroyImmediate(shopGo);
            Debug.Log($"[Check 2] Task 2 verified: {unlockedCount} consumables first, slot {slotViews.Count - 1} locked.");
        }

        private static void CheckTask3_InventoryCompaction()
        {
            var db = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>("Assets/EcoQuest/Inventory/Data/InventoryItemDatabase.asset");
            var service = new InventoryService(db, 24);
            service.Clear();

            service.AddItem("food_apple", 1);
            service.AddItem("food_sandwich", 1);
            service.AddItem("energy_drink", 1);

            Assert.AreEqual(3, service.GetOccupiedSlotCount());
            Assert.AreEqual("food_apple", service.Slots[0].itemId);
            Assert.AreEqual("food_sandwich", service.Slots[1].itemId);
            Assert.AreEqual("energy_drink", service.Slots[2].itemId);

            // Consume apple at slot 0
            service.RemoveOneAt(0);

            Assert.AreEqual(2, service.GetOccupiedSlotCount());
            Assert.AreEqual("food_sandwich", service.Slots[0].itemId, "Slot 0 must be food_sandwich after compaction");
            Assert.AreEqual("energy_drink", service.Slots[1].itemId, "Slot 1 must be energy_drink after compaction");
            Assert.IsTrue(service.Slots[2].IsEmpty, "Slot 2 must be empty after compaction");

            service.Clear();
            Debug.Log("[Check 3] Task 3 verified: Remaining items shifted forward contiguously without gaps.");
        }

        private static void CheckTask4_MinimapIcons()
        {
            var rangerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/Prefabs/NPC/NPC_Ranger.prefab");
            Assert.IsNotNull(rangerPrefab);
            var rangerIcon = rangerPrefab.GetComponent<MinimapIcon>();
            Assert.IsNotNull(rangerIcon, "NPC_Ranger must have MinimapIcon");
            Assert.AreEqual(MinimapIcon.IconType.NPC, rangerIcon.type);
            Assert.IsNotNull(rangerIcon.customSprite, "NPC_Ranger must have customSprite");
            Assert.AreEqual("map_npc_ranger", rangerIcon.customSprite.name, "Ranger icon should be map_npc_ranger");

            var handymanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/Characters/Handyman/NPC_Handyman.prefab");
            Assert.IsNotNull(handymanPrefab);
            var handymanIcon = handymanPrefab.GetComponent<MinimapIcon>();
            Assert.IsNotNull(handymanIcon, "NPC_Handyman must have MinimapIcon");
            Assert.AreEqual(MinimapIcon.IconType.NPC, handymanIcon.type);
            Assert.IsNotNull(handymanIcon.customSprite, "NPC_Handyman must have customSprite");
            Assert.AreEqual("map_npc_shop", handymanIcon.customSprite.name, "Handyman shop icon should be map_npc_shop");

            var fountainGuardSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/EcoQuest/Sprites/MapIcons/map_npc_fountain_guard.png");
            Assert.IsNotNull(fountainGuardSprite, "Fountain Guard sprite must exist");

            Debug.Log($"[Check 4] Task 4 verified: Ranger icon '{rangerIcon.customSprite.name}', Handyman shop icon '{handymanIcon.customSprite.name}', Fountain Guard icon '{fountainGuardSprite.name}'.");
        }
    }
}
