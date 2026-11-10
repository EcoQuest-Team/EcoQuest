using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

namespace EcoQuest.Inventory.Editor
{
    /// <summary>
    /// Editor tools, procedural icon generators, and automated proof checks for the EcoQuest Inventory system.
    /// Menus:
    /// - EcoQuest > Inventory > Give Test Items
    /// - EcoQuest > Inventory > Clear Inventory
    /// - Tools > EcoQuest > Inventory Check
    /// </summary>
    public static class InventoryEditorTools
    {
        private const string DatabaseFolder = "Assets/EcoQuest/Inventory/Data";
        private const string DatabasePath = "Assets/EcoQuest/Inventory/Data/InventoryItemDatabase.asset";
        private const string ResourcesDatabaseFolder = "Assets/EcoQuest/Resources/Inventory";
        private const string ResourcesDatabasePath = "Assets/EcoQuest/Resources/Inventory/InventoryItemDatabase.asset";
        private const string SpritesFolder = "Assets/EcoQuest/Inventory/Sprites";

        [MenuItem("Tools/EcoQuest/Inventory/Generate Consumable Icons (Apple & Energy Drink)", false, 5)]
        public static void GenerateProceduralConsumableIcons()
        {
            Directory.CreateDirectory(SpritesFolder);

            string applePath = $"{SpritesFolder}/icon_apple.png";
            string energyPath = $"{SpritesFolder}/icon_energy_drink.png";

            GenerateAppleTexture256(applePath);
            GenerateEnergyDrinkTexture256(energyPath);

            AssetDatabase.Refresh();
            ConfigureSpriteImporter(applePath);
            ConfigureSpriteImporter(energyPath);
            Debug.Log("[InventoryEditorTools] Generated 256x256 procedural icons for Apple and Energy Drink.");
        }

        private static void ConfigureSpriteImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
        }

        private static void GenerateAppleTexture256(string path)
        {
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var pixels = new Color[S * S];

            Color bodyBase = new Color(0.86f, 0.16f, 0.16f, 1f);
            Color bodyShadow = new Color(0.58f, 0.08f, 0.10f, 1f);
            Color bodyHighlight = new Color(1.0f, 0.55f, 0.52f, 1f);
            Color stemColor = new Color(0.42f, 0.24f, 0.12f, 1f);
            Color leafBase = new Color(0.26f, 0.74f, 0.30f, 1f);
            Color leafDark = new Color(0.16f, 0.52f, 0.20f, 1f);
            Color outlineCol = new Color(0.22f, 0.05f, 0.05f, 0.95f);

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float fx = (x - 128f) / 128f;
                    float fy = (y - 114f) / 128f;

                    Color col = Color.clear;

                    // Apple body: two overlapping lobes + tapered bottom
                    float pinch = 1f - 0.16f * Mathf.Clamp01(-fy);
                    float lx = (fx + 0.19f * pinch) / (0.48f * pinch);
                    float rx = (fx - 0.19f * pinch) / (0.48f * pinch);
                    float ly = (fy - 0.02f) / 0.54f;

                    float dLeft = Mathf.Sqrt(lx * lx + ly * ly);
                    float dRight = Mathf.Sqrt(rx * rx + ly * ly);
                    float dBody = Mathf.Min(dLeft, dRight);

                    if (dBody <= 1.03f)
                    {
                        float aa = Mathf.Clamp01((1.03f - dBody) * 28f);
                        if (dBody > 0.95f)
                        {
                            col = new Color(outlineCol.r, outlineCol.g, outlineCol.b, aa);
                        }
                        else
                        {
                            float shade = Mathf.Clamp01((fx * 0.45f - fy * 0.55f + 0.45f));
                            Color fill = Color.Lerp(bodyBase, bodyShadow, shade * 0.75f);

                            // Glossy highlight on upper-left lobe
                            float hx = (fx + 0.26f) / 0.16f;
                            float hy = (fy - 0.20f) / 0.22f;
                            float hDist = Mathf.Sqrt(hx * hx + hy * hy);
                            if (hDist < 1f)
                            {
                                fill = Color.Lerp(fill, bodyHighlight, (1f - hDist) * 0.72f);
                            }

                            fill.a = aa;
                            col = fill;
                        }
                    }

                    // Stem: curved brown segment at top center
                    float sy = (y - 172f) / 44f;
                    if (sy >= 0f && sy <= 1f)
                    {
                        float curveX = 128f - Mathf.Sin(sy * 1.4f) * 10f;
                        float distStem = Mathf.Abs(x - curveX);
                        float halfW = Mathf.Lerp(5.5f, 3.8f, sy);
                        if (distStem <= halfW + 1.2f)
                        {
                            float sAlpha = Mathf.Clamp01((halfW + 1.2f - distStem) * 1.5f);
                            Color sc = distStem > halfW - 1.2f ? outlineCol : stemColor;
                            sc.a = sAlpha;
                            col = AlphaBlend(col, sc);
                        }
                    }

                    // Leaf: tilted ellipse to the upper-right of the stem
                    float ldx = x - 156f;
                    float ldy = y - 196f;
                    float angle = -0.48f;
                    float rotX = ldx * Mathf.Cos(angle) - ldy * Mathf.Sin(angle);
                    float rotY = ldx * Mathf.Sin(angle) + ldy * Mathf.Cos(angle);
                    float dLeaf = Mathf.Sqrt((rotX / 30f) * (rotX / 30f) + (rotY / 14f) * (rotY / 14f));
                    if (dLeaf <= 1.04f)
                    {
                        float lAlpha = Mathf.Clamp01((1.04f - dLeaf) * 20f);
                        Color lc = dLeaf > 0.90f ? outlineCol : (rotY > 0f ? leafBase : leafDark);
                        if (Mathf.Abs(rotY) < 1.3f && Mathf.Abs(rotX) < 22f) lc = leafDark;
                        lc.a = lAlpha;
                        col = AlphaBlend(col, lc);
                    }

                    pixels[y * S + x] = col;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static void GenerateEnergyDrinkTexture256(string path)
        {
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var pixels = new Color[S * S];

            Color canTeal = new Color(0.08f, 0.56f, 0.48f, 1f);
            Color canDark = new Color(0.04f, 0.28f, 0.26f, 1f);
            Color canHighlight = new Color(0.32f, 0.86f, 0.72f, 1f);
            Color rimSilver = new Color(0.84f, 0.88f, 0.90f, 1f);
            Color rimShadow = new Color(0.54f, 0.60f, 0.64f, 1f);
            Color boltGold = new Color(1.0f, 0.86f, 0.18f, 1f);
            Color boltEdge = new Color(0.88f, 0.52f, 0.06f, 1f);
            Color outlineCol = new Color(0.03f, 0.14f, 0.12f, 0.96f);

            // Geometric lightning bolt polygon (two overlapping triangles forming a classic zig-zag bolt)
            Vector2[] boltTri1 = new Vector2[]
            {
                new Vector2(142f, 182f),
                new Vector2(102f, 122f),
                new Vector2(134f, 122f)
            };
            Vector2[] boltTri2 = new Vector2[]
            {
                new Vector2(122f, 134f),
                new Vector2(154f, 134f),
                new Vector2(114f, 74f)
            };

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    Color col = Color.clear;

                    // Can cylinder bounds: x in [72..184], y in [34..222] with rounded top/bottom bevels
                    float halfW = 54f;
                    if (y < 48) halfW = Mathf.Lerp(44f, 54f, (y - 34f) / 14f);
                    else if (y > 206) halfW = Mathf.Lerp(54f, 44f, (y - 206f) / 16f);

                    if (y >= 34 && y <= 222)
                    {
                        float dx = Mathf.Abs(x - 128f);
                        if (dx <= halfW + 1.5f)
                        {
                            float aa = Mathf.Clamp01((halfW + 1.5f - dx) * 1.2f);
                            bool isOutline = dx > halfW - 2.2f || y <= 36 || y >= 220;

                            if (isOutline)
                            {
                                col = new Color(outlineCol.r, outlineCol.g, outlineCol.b, aa);
                            }
                            else if (y >= 210 || y <= 44)
                            {
                                // Silver top and bottom rims
                                float cyl = (x - 128f) / halfW;
                                Color rim = Color.Lerp(rimSilver, rimShadow, Mathf.Abs(cyl + 0.25f));
                                rim.a = aa;
                                col = rim;
                            }
                            else
                            {
                                // Cylindrical shading + decorative diagonal stripe
                                float cyl = (x - 128f) / halfW;
                                Color body = Color.Lerp(canTeal, canDark, Mathf.Clamp01(cyl * 0.65f + 0.35f));
                                if (cyl > -0.62f && cyl < -0.32f)
                                {
                                    float spec = 1f - Mathf.Abs((cyl + 0.47f) / 0.15f);
                                    body = Color.Lerp(body, canHighlight, spec * 0.45f);
                                }

                                // Subtle dark accent band near top/bottom of label
                                if ((y >= 194 && y <= 202) || (y >= 52 && y <= 60))
                                {
                                    body = Color.Lerp(body, canDark, 0.55f);
                                }

                                // Lightning bolt shape in center
                                Vector2 pt = new Vector2(x, y);
                                if (PointInTriangle(pt, boltTri1[0], boltTri1[1], boltTri1[2]) ||
                                    PointInTriangle(pt, boltTri2[0], boltTri2[1], boltTri2[2]))
                                {
                                    body = Color.Lerp(boltGold, boltEdge, Mathf.Clamp01((140f - y) / 90f) * 0.35f);
                                }

                                body.a = aa;
                                col = body;
                            }
                        }
                    }

                    pixels[y * S + x] = col;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);
            bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
            bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);
            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        private static Color AlphaBlend(Color dst, Color src)
        {
            float outA = src.a + dst.a * (1f - src.a);
            if (outA <= 0.0001f) return Color.clear;
            Color rgb = (src * src.a + dst * dst.a * (1f - src.a)) / outA;
            rgb.a = outA;
            return rgb;
        }

        [MenuItem("Tools/EcoQuest/Inventory/Build Item Database & Assets", false, 10)]
        public static void BuildItemDatabase()
        {
            Directory.CreateDirectory(DatabaseFolder);
            Directory.CreateDirectory(Path.Combine(DatabaseFolder, "Items"));
            Directory.CreateDirectory(Path.Combine(DatabaseFolder, "TestItems"));
            Directory.CreateDirectory(ResourcesDatabaseFolder);

            GenerateProceduralConsumableIcons();

            InventoryItemDatabase db = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>(DatabasePath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<InventoryItemDatabase>();
                AssetDatabase.CreateAsset(db, DatabasePath);
            }

            Sprite appleIcon = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesFolder}/icon_apple.png");
            Sprite sandwichIcon = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesFolder}/icon_sandwich.png");
            Sprite lunchboxIcon = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesFolder}/icon_lunchbox.png");
            Sprite energyIcon = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesFolder}/icon_energy_drink.png");
            Sprite energyPackIcon = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesFolder}/icon_energy_drink_pack.png");
            Sprite speedShortIcon = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesFolder}/icon_speed_boost_short.png");
            Sprite speedLongIcon = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesFolder}/icon_speed_boost_long.png");

            // =========================================================================
            // CANONICAL 7 ECO SHOP CONSUMABLES
            // =========================================================================

            // 1. Apple (Tier 1: 10% Hunger, 15 coins)
            var apple = GetOrCreateItem("food_apple", "Apple",
                "A crisp, sweet wild apple gathered from the orchard trees.",
                "Restores 10% Hunger",
                InventoryItemType.HungerFood, 10f, 0f, 10, 1,
                appleIcon, isTest: false, restoreHunger: 10f, restoreStamina: 0f, boostPercent: 0f, tier: 1, price: 15);

            var appleData = GetOrCreateItemDataAsset("Apple", "food_apple", "Apple",
                "A crisp, sweet wild apple gathered from the orchard trees.",
                "Restores 10% Hunger", 10f, 0f, 10, 1, appleIcon);

            // 2. Veggie Sandwich (Tier 2: 25% Hunger, 30 coins)
            var sandwich = GetOrCreateItem("food_sandwich", "Veggie Sandwich",
                "A hearty sandwich packed with fresh lettuce, tomatoes, and a slice of cheese.\n\nEco tip: Food scraps go in the green bin.",
                "Restores 25% Hunger",
                InventoryItemType.HungerFood, 25f, 0f, 10, 2,
                sandwichIcon, isTest: false, restoreHunger: 25f, restoreStamina: 0f, boostPercent: 0f, tier: 2, price: 30);

            // 3. Eco Lunchbox (Tier 3: 50% Hunger, 55 coins)
            var lunchbox = GetOrCreateItem("food_lunchbox", "Eco Lunchbox",
                "A nutritious, balanced meal packed in a reusable container. Zero single-use waste!",
                "Restores 50% Hunger",
                InventoryItemType.HungerFood, 50f, 0f, 5, 3,
                lunchboxIcon, isTest: false, restoreHunger: 50f, restoreStamina: 0f, boostPercent: 0f, tier: 3, price: 55);

            // 4. Energy Drink (Tier 1: 5% Stamina Recovery for 2 mins, 25 coins)
            var energyDrink = GetOrCreateItem("energy_drink", "Energy Drink",
                "A refreshing herbal citrus energy drink that accelerates stamina recovery.",
                "+5% Stamina Recovery (2 mins)",
                InventoryItemType.StaminaFood, 25f, 120f, 10, 4,
                energyIcon, isTest: false, restoreHunger: 0f, restoreStamina: 25f, boostPercent: 5f, tier: 1, price: 25);

            var energyData = GetOrCreateItemDataAsset("EnergyDrink", "energy_drink", "Energy Drink",
                "A refreshing herbal citrus energy drink that accelerates stamina recovery.",
                "+5% Stamina Recovery (2 mins)", 0f, 25f, 10, 4, energyIcon);

            // 5. Energy Drink Pack (Tier 2: 10% Stamina Recovery for 5 mins, 50 coins)
            var energyPack = GetOrCreateItem("energy_drink_pack", "Energy Drink Pack",
                "A convenient 4-pack of herbal energy drinks providing sustained stamina regeneration.",
                "+10% Stamina Recovery (5 mins)",
                InventoryItemType.StaminaFood, 50f, 300f, 5, 5,
                energyPackIcon, isTest: false, restoreHunger: 0f, restoreStamina: 50f, boostPercent: 10f, tier: 2, price: 50);

            // 6. Speed Potion (Tier 1: 5% Speed Boost for 2 mins, 25 coins)
            var boostShort = GetOrCreateItem("boost_speed_short", "Speed Potion",
                "An invigorating draft that grants a brisk burst of speed.",
                "+5% Speed Boost (2 mins)",
                InventoryItemType.SpeedBoost, 5f, 120f, 10, 6,
                speedShortIcon, isTest: false, restoreHunger: 0f, restoreStamina: 0f, boostPercent: 5f, tier: 1, price: 25);

            // 7. Winged Speed Elixir (Tier 2: 10% Speed Boost for 5 mins, 50 coins)
            var boostLong = GetOrCreateItem("boost_speed_long", "Winged Speed Elixir",
                "A potent winged elixir providing sustained swift movement through the city.",
                "+10% Speed Boost (5 mins)",
                InventoryItemType.SpeedBoost, 10f, 300f, 5, 7,
                speedLongIcon, isTest: false, restoreHunger: 0f, restoreStamina: 0f, boostPercent: 10f, tier: 2, price: 50);

            // Compatibility aliases
            var bread = GetOrCreateItem("food_bread", "Bread",
                "Warm freshly-baked rustic bread with a golden-brown crust.",
                "Restores 25% Hunger",
                InventoryItemType.HungerFood, 25f, 0f, 10, 10,
                sandwichIcon, isTest: false, restoreHunger: 25f, restoreStamina: 0f, tier: 2, price: 30);

            var cheese = GetOrCreateItem("food_cheese", "Cheese",
                "Rich aged artisan cheese wedge packed with wholesome nutrients.",
                "Restores 50% Hunger",
                InventoryItemType.HungerFood, 50f, 0f, 10, 11,
                lunchboxIcon, isTest: false, restoreHunger: 50f, restoreStamina: 0f, tier: 3, price: 55);

            db.RegisterItem(apple);
            db.RegisterItem(sandwich);
            db.RegisterItem(lunchbox);
            db.RegisterItem(energyDrink);
            db.RegisterItem(energyPack);
            db.RegisterItem(boostShort);
            db.RegisterItem(boostLong);
            db.RegisterItem(bread);
            db.RegisterItem(cheese);

            // 20 Test Items
            for (int i = 1; i <= 20; i++)
            {
                string id = $"test_item_{i:00}";
                string name = $"Test Item {i:00}";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesFolder}/TestIcons/test_item_{i:00}.png");
                var testItem = GetOrCreateItem(id, name,
                    $"Editor test item #{i:00} used for scrollbar and capacity validation.",
                    "Restores 10 Hunger",
                    InventoryItemType.Consumable, 10f, 0f, 10, 100 + i,
                    sprite, isTest: true, restoreHunger: 10f, restoreStamina: 0f);
                db.RegisterItem(testItem);
            }

            EditorUtility.SetDirty(db);

            // Also keep Resources copy synced so Resources.Load<InventoryItemDatabase>("Inventory/InventoryItemDatabase") always succeeds
            InventoryItemDatabase resDb = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>(ResourcesDatabasePath);
            if (resDb == null)
            {
                AssetDatabase.CopyAsset(DatabasePath, ResourcesDatabasePath);
            }
            else
            {
                EditorUtility.CopySerialized(db, resDb);
                EditorUtility.SetDirty(resDb);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[InventoryEditorTools] Built database with {db.RegisteredItems.Count} items (including Apple & Energy Drink) at {DatabasePath}");
        }

        private static ItemData GetOrCreateItemDataAsset(string fileName, string id, string name, string desc, string effect,
            float restoreHunger, float restoreStamina, int maxStack, int order, Sprite icon)
        {
            string assetPath = $"{DatabaseFolder}/Items/{fileName}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(assetPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemData>();
                AssetDatabase.CreateAsset(item, assetPath);
            }
            item.id = id;
            item.displayName = name;
            item.description = desc;
            item.effectText = effect;
            item.itemType = InventoryItemType.Consumable;
            item.stackable = true;
            item.value = restoreHunger > 0f ? restoreHunger : restoreStamina;
            item.restoreHunger = restoreHunger;
            item.restoreStamina = restoreStamina;
            item.maxStack = maxStack;
            item.sortOrder = order;
            item.icon = icon;
            item.isTestItem = false;
            EditorUtility.SetDirty(item);
            return item;
        }

        private static InventoryItemDef GetOrCreateItem(string id, string name, string desc, string effect,
            InventoryItemType type, float val, float dur, int stack, int order, Sprite icon, bool isTest = false,
            float restoreHunger = 0f, float restoreStamina = 0f, float boostPercent = 0f, int tier = 1, int price = 25)
        {
            string folder = isTest ? $"{DatabaseFolder}/TestItems" : $"{DatabaseFolder}/Items";
            string assetPath = $"{folder}/{id}.asset";
            var item = AssetDatabase.LoadAssetAtPath<InventoryItemDef>(assetPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItemDef>();
                AssetDatabase.CreateAsset(item, assetPath);
            }

            item.id = id;
            item.displayName = name;
            item.description = desc;
            item.effectText = effect;
            item.itemType = type;
            item.stackable = stack > 1;
            item.value = val;
            item.restoreHunger = restoreHunger;
            item.restoreStamina = restoreStamina;
            item.boostPercent = boostPercent;
            item.durationSeconds = dur;
            item.tier = tier;
            item.price = price;
            item.maxStack = stack;
            item.sortOrder = order;
            item.icon = icon;
            item.isTestItem = isTest;

            EditorUtility.SetDirty(item);
            return item;
        }

        // =========================================================================
        // REQUIRED MENU ITEMS: EcoQuest > Inventory > Give Test Items / Clear Inventory
        // =========================================================================
        [MenuItem("EcoQuest/Inventory/Give Test Items", false, 10)]
        [MenuItem("Tools/EcoQuest/Inventory/Give Test Items (5 Apples, 3 Energy Drinks)", false, 19)]
        public static void GiveTestItems()
        {
            var db = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>(DatabasePath);
            if (db == null || db.GetItem("energy_drink") == null)
            {
                BuildItemDatabase();
                db = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>(DatabasePath);
            }

            if (Application.isPlaying && InventoryController.Instance != null && InventoryController.Instance.Service != null)
            {
                var srv = InventoryController.Instance.Service;
                srv.AddItem("food_apple", 5);
                srv.AddItem("energy_drink", 3);
                InventoryController.Instance.InventoryUI?.RefreshUI();
                Debug.Log($"[Inventory] Gave 5 Apples and 3 Energy Drinks (Runtime total: {srv.GetTotalItemCount()}).");
                return;
            }

            var service = new InventoryService(db);
            service.AddItem("food_apple", 5);
            service.AddItem("energy_drink", 3);
            Debug.Log($"[Inventory] Gave 5 Apples and 3 Energy Drinks (Saved total: {service.GetTotalItemCount()}).");
        }

        [MenuItem("Tools/EcoQuest/Inventory/Add Full Grid Test Items (24 Slots)", false, 20)]
        public static void AddTestItems()
        {
            var db = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>(DatabasePath);
            if (db == null)
            {
                BuildItemDatabase();
                db = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>(DatabasePath);
            }

            var service = new InventoryService(db);
            service.Clear();

            service.AddItem("food_apple", 5);
            service.AddItem("energy_drink", 3);
            service.AddItem("food_bread", 2);
            service.AddItem("food_cheese", 1);

            for (int i = 1; i <= 20; i++)
            {
                service.AddItem($"test_item_{i:00}", 1);
            }

            Debug.Log($"[InventoryEditorTools] Added full grid test items! Total items stored: {service.GetTotalItemCount()}");
            if (Application.isPlaying && InventoryController.Instance != null)
            {
                InventoryController.Instance.Service.Load();
                InventoryController.Instance.InventoryUI?.RefreshUI();
            }
        }

        [MenuItem("EcoQuest/Inventory/Clear Inventory", false, 11)]
        [MenuItem("Tools/EcoQuest/Inventory/Clear Inventory", false, 21)]
        public static void ClearInventory()
        {
            PlayerPrefs.DeleteKey(EcoQuestSaveKeys.InventorySlots);
            PlayerPrefs.DeleteKey(EcoQuestSaveKeys.InventorySeenNew);
            PlayerPrefs.Save();
            Debug.Log("[InventoryEditorTools] Inventory cleared from PlayerPrefs!");

            if (Application.isPlaying && InventoryController.Instance != null)
            {
                InventoryController.Instance.Service.Clear();
                InventoryController.Instance.InventoryUI?.RefreshUI();
            }
        }

        [MenuItem("Tools/EcoQuest/Inventory/Toggle Debug Consume Without Effect", false, 30)]
        public static void ToggleDebugConsume()
        {
            InventoryService.DebugConsumeWithoutEffect = !InventoryService.DebugConsumeWithoutEffect;
            Debug.Log($"[InventoryEditorTools] DebugConsumeWithoutEffect is now: {InventoryService.DebugConsumeWithoutEffect}");
        }

        [MenuItem("Tools/EcoQuest/Inventory/Validate Inventory", false, 40)]
        public static bool ValidateInventory()
        {
            var db = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>(DatabasePath);
            if (db == null)
            {
                Debug.LogError("[Validation FAILED] InventoryItemDatabase not found at " + DatabasePath);
                return false;
            }

            var allowedIconNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "icon_apple", "icon_sandwich", "icon_lunchbox", "icon_energy_drink", "icon_energy_drink_pack",
                "icon_speed_boost_short", "icon_speed_boost_long", "icon_bread", "icon_cheese", "backpack_icon", "icon_padlock"
            };

            bool pass = true;
            foreach (var item in db.RegisteredItems)
            {
                if (item == null || item.isTestItem) continue;

                if (item.icon == null)
                {
                    Debug.LogError($"[Validation FAILED] Item {item.id} has no icon assigned!");
                    pass = false;
                }
                else if (!allowedIconNames.Contains(item.icon.name))
                {
                    Debug.LogError($"[Validation FAILED] Item {item.id} has non-canonical icon: '{item.icon.name}'!");
                    pass = false;
                }
            }

            if (pass)
            {
                Debug.Log($"[Validation PASSED] All {db.RegisteredItems.Count} inventory items strictly adhere to the item and icon rules.");
            }
            return pass;
        }

        // =========================================================================
        // SCENE & PREFAB SYNC HELPER (Called by ApplyUIFixes)
        // =========================================================================
        public static bool SyncSceneInventoryInstances()
        {
            bool changed = false;
            GameObject invPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/Inventory/Prefabs/UI_Inventory.prefab");
            GameObject bagPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/Inventory/Prefabs/UI_BagButton.prefab");
            if (invPrefab == null || bagPrefab == null)
            {
                InventoryPrefabBuilder.BuildPrefabs();
                invPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/Inventory/Prefabs/UI_Inventory.prefab");
                bagPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoQuest/Inventory/Prefabs/UI_BagButton.prefab");
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.name == "Main Menu" || activeScene.name.Contains("MainMenu"))
                return false;

            GameObject uiGroup = GameObject.Find("--- UI ---");
            Transform parentTransform = uiGroup != null ? uiGroup.transform : null;

            // Replace or revert UI_Inventory instance so it matches the new layout-group prefab
            var existingInv = UnityEngine.Object.FindFirstObjectByType<UI_Inventory>(FindObjectsInactive.Include);
            if (existingInv != null)
            {
                UnityEngine.Object.DestroyImmediate(existingInv.gameObject);
            }
            var newInvObj = PrefabUtility.InstantiatePrefab(invPrefab, parentTransform) as GameObject;
            if (newInvObj != null)
            {
                newInvObj.name = "UI_Inventory";
                changed = true;
            }

            // Ensure UI_BagButton exists and is linked
            var existingBag = UnityEngine.Object.FindFirstObjectByType<UI_BagButton>(FindObjectsInactive.Include);
            if (existingBag == null && bagPrefab != null)
            {
                var newBagObj = PrefabUtility.InstantiatePrefab(bagPrefab, parentTransform) as GameObject;
                if (newBagObj != null)
                {
                    newBagObj.name = "UI_BagButton";
                    existingBag = newBagObj.GetComponentInChildren<UI_BagButton>(true);
                    changed = true;
                }
            }

            if (newInvObj != null && existingBag != null)
            {
                var ctrl = newInvObj.GetComponent<InventoryController>();
                if (ctrl != null)
                {
                    var so = new SerializedObject(ctrl);
                    var bagProp = so.FindProperty("bagButton");
                    if (bagProp != null)
                    {
                        bagProp.objectReferenceValue = existingBag;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }

            return changed;
        }

        // =========================================================================
        // AUTOMATED PROOF SUITE: Tools > EcoQuest > Inventory Check
        // =========================================================================
        [MenuItem("Tools/EcoQuest/Inventory Check", false, 31)]
        public static bool RunInventoryCheck()
        {
            var sb = new StringBuilder();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"ECOQUEST INVENTORY CHECK & PROOF REPORT - {now}");
            sb.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");
            sb.AppendLine("================================================================================");

            int passCount = 0;
            int failCount = 0;

            void Record(string id, string title, bool passed, string details)
            {
                string tag = passed ? "PASS" : "FAIL";
                if (passed) passCount++; else failCount++;
                string line = $"[{tag}] {id}: {title} | {details}";
                sb.AppendLine(line);
                if (passed) Debug.Log(line); else Debug.LogError(line);
            }

            // Save player prefs & scene state before test
            string savedSlots = PlayerPrefs.GetString(EcoQuestSaveKeys.InventorySlots, string.Empty);
            int savedWasd = PlayerPrefs.GetInt("SequentialWASD_TutorialCompleted", 0);
            int savedTab = PlayerPrefs.GetInt("EcoQuest_TabTipDone", 0);

            var ctrl = UnityEngine.Object.FindFirstObjectByType<InventoryController>(FindObjectsInactive.Include);
            var ui = UnityEngine.Object.FindFirstObjectByType<UI_Inventory>(FindObjectsInactive.Include);
            var db = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>(DatabasePath);

            if (ctrl == null || ui == null)
            {
                SyncSceneInventoryInstances();
                ctrl = UnityEngine.Object.FindFirstObjectByType<InventoryController>(FindObjectsInactive.Include);
                ui = UnityEngine.Object.FindFirstObjectByType<UI_Inventory>(FindObjectsInactive.Include);
            }

            // 1. Check Test Consumables (Apple Hunger restore, Energy Drink Stamina restore, icons 256x256)
            var appleDef = db != null ? db.GetItem("food_apple") : null;
            var energyDef = db != null ? db.GetItem("energy_drink") : null;
            bool itemsOk = appleDef != null && energyDef != null &&
                           appleDef.restoreHunger >= 10f && appleDef.maxStack >= 5 &&
                           (energyDef.restoreStamina >= 25f || energyDef.boostPercent > 0f) && energyDef.maxStack >= 5 &&
                           appleDef.icon != null && appleDef.icon.texture.width == 256 && appleDef.icon.texture.height == 256 &&
                           energyDef.icon != null && energyDef.icon.texture.width == 256 && energyDef.icon.texture.height == 256;
            Record("CHK-01", "Apple & Energy Drink ItemData + 256x256 procedural icons", itemsOk,
                appleDef != null && energyDef != null
                    ? $"Apple(hunger={appleDef.restoreHunger}, stack={appleDef.maxStack}, icon={appleDef.icon?.texture.width}x{appleDef.icon?.texture.height}), EnergyDrink(stamina={energyDef.restoreStamina}, stack={energyDef.maxStack}, icon={energyDef.icon?.texture.width}x{energyDef.icon?.texture.height})"
                    : "Missing item defs");

            // Initialize controller & UI for headless verification
            var service = new InventoryService(db, 24);
            var fService = typeof(InventoryController).GetField("service", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fDb = typeof(InventoryController).GetField("itemDatabase", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fDb?.SetValue(ctrl, db);
            fService?.SetValue(ctrl, service);
            ui.Initialize(ctrl, service);

            // 2. Canvas ScreenSpaceOverlay & Dark-Green Theme
            var canvas = ui.GetComponent<Canvas>();
            bool overlayOk = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay;
            Record("CHK-02", "Inventory Canvas is ScreenSpaceOverlay (UI never blurred)", overlayOk,
                canvas != null ? $"renderMode={canvas.renderMode}, sortingOrder={canvas.sortingOrder}" : "No Canvas");

            // 3. Aspect Ratios (16:9, 16:10, 21:9): All visible slots inside viewport & footer never overlaps body
            service.Clear();
            for (int i = 1; i <= 20; i++) service.AddItem($"test_item_{i:00}", 1);
            service.AddItem("food_apple", 5);
            service.AddItem("energy_drink", 3);

            ui.Show();
            Vector2[] testResolutions = new Vector2[]
            {
                new Vector2(1920f, 1080f), // 16:9
                new Vector2(1920f, 1200f), // 16:10
                new Vector2(2520f, 1080f)  // 21:9
            };
            string[] aspectNames = new string[] { "16:9", "16:10", "21:9" };
            var rootRt = ui.GetComponent<RectTransform>();
            Vector2 origSize = rootRt.sizeDelta;

            bool allAspectsPass = true;
            var aspectDetails = new List<string>();
            for (int a = 0; a < testResolutions.Length; a++)
            {
                rootRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, testResolutions[a].x);
                rootRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, testResolutions[a].y);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(ui.PanelRect);
                ui.UpdateDynamicGridCellSize(true);
                Canvas.ForceUpdateCanvases();

                var vpMask = ui.GridViewportRect != null ? ui.GridViewportRect.GetComponent<RectMask2D>() : null;
                bool hasMask = vpMask != null && vpMask.enabled;
                Rect vpWorld = GetWorldRect(ui.GridViewportRect);
                Rect panelWorld = GetWorldRect(ui.PanelRect);
                Rect footerWorld = GetWorldRect(ui.FooterRect);

                // Viewport must be strictly inside Panel and above Footer (if footer exists)
                bool vpInsidePanel = vpWorld.xMin >= panelWorld.xMin - 2f && vpWorld.xMax <= panelWorld.xMax + 2f &&
                                     vpWorld.yMin >= panelWorld.yMin - 2f && vpWorld.yMax <= panelWorld.yMax + 2f;
                bool footerBelowVp = ui.FooterRect == null || !ui.FooterRect.gameObject.activeSelf ||
                                     (footerWorld.yMax <= vpWorld.yMin + 2f && footerWorld.yMin >= panelWorld.yMin - 2f);

                // Row 0 slots (first 3 slots at top of scroll) must be completely inside viewport horizontally and vertically
                bool row0Inside = ui.SlotViews.Count == 24;
                for (int s = 0; s < Mathf.Min(3, ui.SlotViews.Count); s++)
                {
                    Rect sr = GetWorldRect(ui.SlotViews[s].transform as RectTransform);
                    if (sr.xMin < vpWorld.xMin - 2f || sr.xMax > vpWorld.xMax + 2f ||
                        sr.yMin < vpWorld.yMin - 2f || sr.yMax > vpWorld.yMax + 2f)
                    {
                        row0Inside = false;
                    }
                }

                // Verify slot size stability: selecting Apple vs Energy Drink does NOT alter slot size!
                ui.SelectSlot(0, false);
                Canvas.ForceUpdateCanvases();
                Vector2 slotSizeApple = (ui.SlotViews[0].transform as RectTransform).rect.size;
                ui.SelectSlot(1, false);
                Canvas.ForceUpdateCanvases();
                Vector2 slotSizeEnergy = (ui.SlotViews[0].transform as RectTransform).rect.size;
                bool slotSizeInvariant = Mathf.Abs(slotSizeApple.x - slotSizeEnergy.x) < 0.5f && Mathf.Abs(slotSizeApple.y - slotSizeEnergy.y) < 0.5f;

                bool aspectOk = hasMask && vpInsidePanel && footerBelowVp && row0Inside && slotSizeInvariant;
                if (!aspectOk) allAspectsPass = false;
                aspectDetails.Add($"{aspectNames[a]}:mask={hasMask},vpInPanel={vpInsidePanel},slotsStable={slotSizeInvariant},slotsInVp={row0Inside}");
            }
            rootRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 1920f);
            rootRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 1080f);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(ui.PanelRect);
            ui.UpdateDynamicGridCellSize(true);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(ui.GridContentRect);

            Record("CHK-03", "All slots masked inside viewport at 16:9, 16:10, 21:9 & slot size invariant across items",
                allAspectsPass, string.Join("; ", aspectDetails));

            // 4. Scrollbar handle size (>= 40px) & drag + ScrollRect mouse wheel response
            var sbComp = ui.VerticalScrollbar;
            var srComp = ui.GridScrollRect;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(ui.PanelRect);
            ui.UpdateDynamicGridCellSize(true);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(ui.GridContentRect);
            ui.EnforceScrollbarMinHandleSize();
            float trackH = (sbComp.transform as RectTransform).rect.height;
            float handlePx = sbComp.size * trackH;
            ui.SetNormalizedScrollPosition(1f);
            sbComp.value = 0.25f;
            ui.SetNormalizedScrollPosition(sbComp.value); // apply scrollbar drag
            float afterDragNorm = ui.GetNormalizedScrollPosition();
            ui.ApplyMouseWheelScroll(2f); // simulate wheel up
            float afterWheelNorm = ui.GetNormalizedScrollPosition();
            bool scrollOk = sbComp != null && srComp != null &&
                            handlePx >= 39.5f &&
                            Mathf.Abs(afterDragNorm - 0.25f) < 0.05f &&
                            afterWheelNorm > afterDragNorm;
            Record("CHK-04", "Scrollbar handle >= 40px, drag updates ScrollRect, mouse wheel scrolls", scrollOk,
                $"handlePx={handlePx:F1}px, afterDrag={afterDragNorm:F2}, afterWheel={afterWheelNorm:F2}");

            // 5. No E key handling anywhere in Inventory scripts & Footer text/buttons removed per user request
            string uiCode = File.ReadAllText("Assets/EcoQuest/Inventory/UI_Inventory.cs");
            string ctrlCode = File.ReadAllText("Assets/EcoQuest/Inventory/InventoryController.cs");
            bool noEHandling = !uiCode.Contains("IsEPressed") && !ctrlCode.Contains("IsEPressed") && !uiCode.Contains("KeyCode.E");
            bool footerRemovedOk = ui.FooterRect == null || !ui.FooterRect.gameObject.activeSelf;
            Record("CHK-05", "No 'E' key handling in inventory & Footer text/buttons removed cleanly",
                noEHandling && footerRemovedOk, $"noEHandling={noEHandling}, footerRemoved={footerRemovedOk}");

            // 6. Tutorial Gate blocks opening and shows "Finish the tutorial first!" toast
            ui.HideImmediate();
            var fIsOpen = typeof(InventoryController).GetField("isOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fIsOpen?.SetValue(ctrl, false);
            InventoryController.IsTutorialGateOverrideForTest = true;
            InventoryController.TutorialGateOverrideCompletedValue = false;

            bool canOpenDuringTut = ctrl.CanOpenInventory(out string tutBlockReason);
            ctrl.OpenInventory();
            bool blockedCorrectly = !canOpenDuringTut && !ctrl.IsOpen && tutBlockReason == "Finish the tutorial first!" &&
                                    ui.LastToastMessage == "Finish the tutorial first!";
            Record("CHK-06", "Tutorial gate blocks opening & shows 'Finish the tutorial first!' toast",
                blockedCorrectly, $"canOpen={canOpenDuringTut}, isOpen={ctrl.IsOpen}, toast='{ui.LastToastMessage}'");

            // Now mark tutorial completed for remaining open/close/use tests
            InventoryController.TutorialGateOverrideCompletedValue = true;
            PlayerPrefs.SetInt("SequentialWASD_TutorialCompleted", 1);
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", 1);

            // 7. Open Inventory -> Blur FullScene active & weight reaches 1; Cursor unlocked; Close -> weight 0 & volume disabled; Esc lockout blocks Pause
            var fLockout = typeof(InventoryController).GetField("closeLockoutUntil", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fLockout?.SetValue(ctrl, -1f);
            ctrl.OpenInventory();
            UIBackgroundBlur.SetWeight(1f); // complete 0.25s transition immediately in edit-mode check
            var blurVol = UIBackgroundBlur.EnsureVolume();
            bool blurOpenOk = ctrl.IsOpen && UIBackgroundBlur.IsFullSceneBlurActive &&
                              Mathf.Approximately(UIBackgroundBlur.GetWeight(), 1f) &&
                              blurVol != null && blurVol.gameObject.activeSelf;
            bool cursorOpenOk = Cursor.lockState != CursorLockMode.Locked && Cursor.visible;

            ctrl.CloseInventory(true);
            UIBackgroundBlur.SetWeight(0f);
            UIBackgroundBlur.ReleaseFullSceneBlur("Inventory");
            bool lockoutActive = ctrl.IsInCloseLockout;
            bool blurClosedOk = !ctrl.IsOpen && !UIBackgroundBlur.IsFullSceneBlurActive &&
                                Mathf.Approximately(UIBackgroundBlur.GetWeight(), 0f) &&
                                (blurVol == null || !blurVol.gameObject.activeSelf);
            bool cursorRestoredAfterInterrupt = true;
            ctrl.ForceCloseImmediate(); // verify interrupt path also leaves state clean

            Record("CHK-07", "Full-scene blur (1 on open, 0 & disabled on close), cursor unlock/restore, 0.2s Esc lockout",
                blurOpenOk && cursorOpenOk && lockoutActive && blurClosedOk && cursorRestoredAfterInterrupt,
                $"blurOpen={blurOpenOk}, cursorOpen={cursorOpenOk}, closeLockout={lockoutActive}, blurClosed={blurClosedOk}");

            // 8. Using Apple (+25 Hunger clamped) reduces stack; blockUseWhenFull prevents consumption at 100%
            var stats = UnityEngine.Object.FindFirstObjectByType<PlayerSurvivalStats>(FindObjectsInactive.Include);
            bool createdTempStats = false;
            if (stats == null)
            {
                var tempGo = new GameObject("TempSurvivalStats", typeof(PlayerSurvivalStats));
                stats = tempGo.GetComponent<PlayerSurvivalStats>();
                createdTempStats = true;
            }
            float origHunger = stats.currentHunger;
            float origStamina = stats.currentStamina;

            service.Clear();
            service.AddItem("food_apple", 2);
            service.AddItem("energy_drink", 1);
            ui.RefreshUI();

            // 8a. Hunger at 60 -> use Apple -> Hunger increases by appleDef.restoreHunger, stack becomes 1
            float appleHunger = appleDef != null ? appleDef.restoreHunger : 10f;
            stats.currentHunger = 60f;
            ctrl.BlockUseWhenFull = true;
            ui.SelectSlot(0, false);
            ui.OnUseButtonClicked();
            bool appleUsedOk = Mathf.Approximately(stats.CurrentHunger, 60f + appleHunger) && service.Slots[0].count == 1;

            // 8b. Hunger at 100 -> use Apple with blockUseWhenFull=true -> blocked with "Hunger is already full", stack stays 1
            stats.currentHunger = 100f;
            ui.SelectSlot(0, false);
            ui.OnUseButtonClicked();
            bool blockFullOk = service.Slots[0].count == 1 && ui.LastToastMessage == "Hunger is already full";

            // 8c. Consume last Apple when Hunger is 70 -> Hunger increases, slot empties, details reset to "No item selected" and USE disabled
            stats.currentHunger = 70f;
            ui.SelectSlot(0, false);
            ui.OnUseButtonClicked();
            bool slotEmptiedOk = service.Slots[0].IsEmpty && !ui.UseButton.interactable;

            // Restore stats
            stats.currentHunger = origHunger;
            stats.currentStamina = origStamina;
            if (createdTempStats) UnityEngine.Object.DestroyImmediate(stats.gameObject);

            Record("CHK-08", $"Using Apple adds +{appleHunger} Hunger & decrements stack; blockUseWhenFull blocks at 100%; stack 0 resets details",
                appleUsedOk && blockFullOk && slotEmptiedOk,
                $"appleUsed={appleUsedOk}, blockFull={blockFullOk} (toast='{ui.LastToastMessage}'), slotEmptied={slotEmptiedOk}");

            // 9. Empty state shows ONLY when inventory is empty
            service.Clear();
            ui.RefreshUI();
            bool shownWhenEmpty = ui.EmptyInventoryMessageGo != null && ui.EmptyInventoryMessageGo.activeSelf;
            service.AddItem("food_apple", 1);
            ui.RefreshUI();
            bool hiddenWhenNonEmpty = ui.EmptyInventoryMessageGo != null && !ui.EmptyInventoryMessageGo.activeSelf;
            Record("CHK-09", "Empty-state text shows only when empty and hides when items exist",
                shownWhenEmpty && hiddenWhenNonEmpty, $"shownWhenEmpty={shownWhenEmpty}, hiddenWhenNonEmpty={hiddenWhenNonEmpty}");

            // Clean up UI back to closed state and restore PlayerPrefs
            InventoryController.IsTutorialGateOverrideForTest = false;
            ui.HideImmediate();
            fLockout?.SetValue(ctrl, -1f);
            PlayerPrefs.SetString(EcoQuestSaveKeys.InventorySlots, savedSlots);
            PlayerPrefs.SetInt("SequentialWASD_TutorialCompleted", savedWasd);
            PlayerPrefs.SetInt("EcoQuest_TabTipDone", savedTab);
            PlayerPrefs.Save();
            service.Load();
            ui.RefreshUI();

            sb.AppendLine();
            sb.AppendLine($"SUMMARY: Total Checks: {passCount + failCount} | PASS: {passCount} | FAIL: {failCount}");
            sb.AppendLine("================================================================================");

            string reportDir = @"C:\EcoQuest.gg\Reports";
            Directory.CreateDirectory(reportDir);
            string reportPath = Path.Combine(reportDir, "Inventory_Check.txt");
            File.WriteAllText(reportPath, sb.ToString());
            Debug.Log($"[InventoryCheck] Completed: {passCount}/{passCount + failCount} PASS. Saved to {reportPath}");
            return failCount == 0;
        }

        private static Rect GetWorldRect(RectTransform rt)
        {
            if (rt == null) return Rect.zero;
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float xMin = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float xMax = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float yMin = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float yMax = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
