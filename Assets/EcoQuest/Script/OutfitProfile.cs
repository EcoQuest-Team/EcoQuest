using System;
using System.Collections.Generic;
using UnityEngine;

public enum Gender
{
    Male = 0,
    Female = 1
}

public enum OutfitArchetype
{
    OfficeWorker = 0,
    Student = 1,
    Jogger = 2,
    Casual = 3,
    Tourist = 4,
    Shopkeeper = 5,
    Elder = 6,
    Construction = 7,
    Delivery = 8,
    Artist = 9
}

public enum AccessoryId
{
    // Head
    Cap,
    Beanie,
    HardHat,
    Glasses,
    Sunglasses,
    Headphones,
    // Body
    Backpack,
    ShoulderBag,
    Tote,
    Scarf,
    Tie,
    Vest,
    Lanyard,
    // Hands
    Phone,
    CoffeeCup,
    ShoppingBag,
    BookFolder,
    Umbrella
}

public enum ModelColoringMethod
{
    SeparateSlots,
    PaletteTexture,
    SingleShadedTexture
}

[CreateAssetMenu(fileName = "CityOutfitProfile", menuName = "EcoQuest/City Outfit Profile")]
public class OutfitProfile : ScriptableObject
{
    [Serializable]
    public class ArchetypeRule
    {
        public OutfitArchetype archetype;
        public int[] allowedNeutralTopIndices;
        public int[] allowedNeutralBottomIndices;
        public int[] allowedAccentIndices;
        [Range(0f, 1f)] public float accentOnTopChance = 0.35f;
        [Range(0f, 1f)] public float stripeOrPatternChance = 0.25f;
        [Range(0f, 1f)] public float openJacketChance = 0.35f;
        public int minAccessories = 1;
        public int maxAccessories = 2;
        public AccessoryId[] headPool;
        public AccessoryId[] bodyPool;
        public AccessoryId[] handPool;
    }

    [Header("Curated Natural Skin & Hair Ranges")]
    public Color[] skinTones = new Color[]
    {
        new Color(0.95f, 0.82f, 0.74f), // Fair ivory
        new Color(0.91f, 0.75f, 0.64f), // Light warm peach
        new Color(0.85f, 0.67f, 0.54f), // Medium warm beige
        new Color(0.76f, 0.57f, 0.42f), // Olive tan
        new Color(0.64f, 0.45f, 0.31f), // Golden brown
        new Color(0.49f, 0.33f, 0.23f), // Rich brown
        new Color(0.35f, 0.23f, 0.17f)  // Deep espresso
    };

    public Color[] naturalHairColors = new Color[]
    {
        new Color(0.11f, 0.10f, 0.10f), // Jet black
        new Color(0.20f, 0.14f, 0.10f), // Dark espresso
        new Color(0.35f, 0.23f, 0.15f), // Chestnut brown
        new Color(0.46f, 0.22f, 0.14f), // Warm auburn
        new Color(0.72f, 0.59f, 0.38f), // Honey blond
        new Color(0.66f, 0.58f, 0.46f), // Ash blond
        new Color(0.46f, 0.47f, 0.49f), // Salt and pepper
        new Color(0.70f, 0.71f, 0.73f)  // Silver grey
    };

    public Color[] artistDyedHairColors = new Color[]
    {
        new Color(0.19f, 0.45f, 0.48f), // Muted teal
        new Color(0.44f, 0.23f, 0.42f), // Dusty plum
        new Color(0.64f, 0.34f, 0.36f)  // Copper rose
    };

    [Header("Curated Outfit Neutrals (Navy, Charcoal, Grey, Beige, White, Black, Olive, Slate)")]
    public Color[] neutralPalette = new Color[]
    {
        new Color(0.14f, 0.19f, 0.29f), // 0: Navy
        new Color(0.22f, 0.23f, 0.25f), // 1: Charcoal
        new Color(0.56f, 0.58f, 0.60f), // 2: Cool Grey
        new Color(0.77f, 0.70f, 0.58f), // 3: Beige / Khaki
        new Color(0.89f, 0.88f, 0.85f), // 4: Off-White / Stone
        new Color(0.14f, 0.14f, 0.15f), // 5: Near-Black
        new Color(0.29f, 0.34f, 0.24f), // 6: Olive
        new Color(0.25f, 0.34f, 0.44f)  // 7: Denim Slate
    };

    public string[] neutralNames = new string[]
    {
        "Navy", "Charcoal", "Grey", "Beige", "White", "Black", "Olive", "Denim"
    };

    [Header("Curated Muted Accents (Zero Neon)")]
    public Color[] accentPalette = new Color[]
    {
        new Color(0.48f, 0.18f, 0.22f), // 0: Burgundy
        new Color(0.68f, 0.35f, 0.26f), // 1: Terracotta
        new Color(0.84f, 0.66f, 0.18f), // 2: Hi-Vis Amber / Mustard
        new Color(0.24f, 0.44f, 0.32f), // 3: Forest Sage
        new Color(0.20f, 0.46f, 0.50f), // 4: Teal
        new Color(0.27f, 0.44f, 0.64f), // 5: Cobalt Blue
        new Color(0.46f, 0.30f, 0.46f), // 6: Dusty Plum
        new Color(0.72f, 0.44f, 0.38f)  // 7: Clay Rose
    };

    public string[] accentNames = new string[]
    {
        "Burgundy", "Terracotta", "Amber", "Forest", "Teal", "Cobalt", "Plum", "Clay"
    };

    [Header("Existing Project Accessory Meshes")]
    public Mesh capMesh;
    public Mesh hardHatMesh;
    public Mesh headphonesMesh;
    public Mesh backpackMesh;
    public Mesh survivalBagMesh;
    public Mesh toteBagMesh;
    public Mesh phoneMesh;
    public Mesh coffeeCupMesh;
    public Mesh bookMesh;
    public Mesh notebookMesh;

    [Header("Canonical Untinted Base Textures for UV-Region Recoloring")]
    public Texture2D maleCivilianTex;
    public Texture2D femaleCivilianTex;
    public Texture2D everydayMaleTex;
    public Texture2D everydayFemaleTex;

    [Header("Archetype Definitions")]
    public List<ArchetypeRule> rules = new List<ArchetypeRule>();

    public ArchetypeRule GetRule(OutfitArchetype archetype)
    {
        if (rules == null || rules.Count == 0) EnsureDefaultRules();
        foreach (var r in rules)
        {
            if (r.archetype == archetype) return r;
        }
        return rules[0];
    }

    public void EnsureDefaultRules()
    {
        if (rules != null && rules.Count == 10) return;
        rules = new List<ArchetypeRule>
        {
            new ArchetypeRule {
                archetype = OutfitArchetype.OfficeWorker,
                allowedNeutralTopIndices = new[]{ 0, 1, 2, 4 },
                allowedNeutralBottomIndices = new[]{ 0, 1, 5, 3 },
                allowedAccentIndices = new[]{ 0, 4, 5 },
                accentOnTopChance = 0.15f, stripeOrPatternChance = 0.25f, openJacketChance = 0.55f,
                minAccessories = 2, maxAccessories = 3,
                headPool = new[]{ AccessoryId.Glasses },
                bodyPool = new[]{ AccessoryId.Tie, AccessoryId.Lanyard, AccessoryId.ShoulderBag },
                handPool = new[]{ AccessoryId.BookFolder, AccessoryId.CoffeeCup, AccessoryId.Phone }
            },
            new ArchetypeRule {
                archetype = OutfitArchetype.Student,
                allowedNeutralTopIndices = new[]{ 0, 2, 4, 6 },
                allowedNeutralBottomIndices = new[]{ 7, 1, 3, 5 },
                allowedAccentIndices = new[]{ 0, 1, 3, 5 },
                accentOnTopChance = 0.55f, stripeOrPatternChance = 0.40f, openJacketChance = 0.45f,
                minAccessories = 2, maxAccessories = 3,
                headPool = new[]{ AccessoryId.Cap, AccessoryId.Beanie, AccessoryId.Headphones, AccessoryId.Glasses },
                bodyPool = new[]{ AccessoryId.Backpack },
                handPool = new[]{ AccessoryId.BookFolder, AccessoryId.Phone }
            },
            new ArchetypeRule {
                archetype = OutfitArchetype.Jogger,
                allowedNeutralTopIndices = new[]{ 4, 2, 5, 0 },
                allowedNeutralBottomIndices = new[]{ 5, 1, 0 },
                allowedAccentIndices = new[]{ 3, 4, 5, 1 },
                accentOnTopChance = 0.70f, stripeOrPatternChance = 0.50f, openJacketChance = 0.20f,
                minAccessories = 2, maxAccessories = 2,
                headPool = new[]{ AccessoryId.Cap, AccessoryId.Headphones, AccessoryId.Sunglasses },
                bodyPool = new AccessoryId[0],
                handPool = new AccessoryId[0]
            },
            new ArchetypeRule {
                archetype = OutfitArchetype.Casual,
                allowedNeutralTopIndices = new[]{ 2, 3, 4, 6, 0 },
                allowedNeutralBottomIndices = new[]{ 7, 5, 1, 3 },
                allowedAccentIndices = new[]{ 1, 3, 4, 7 },
                accentOnTopChance = 0.45f, stripeOrPatternChance = 0.35f, openJacketChance = 0.45f,
                minAccessories = 1, maxAccessories = 2,
                headPool = new[]{ AccessoryId.Beanie, AccessoryId.Cap, AccessoryId.Sunglasses },
                bodyPool = new[]{ AccessoryId.ShoulderBag, AccessoryId.Scarf },
                handPool = new[]{ AccessoryId.CoffeeCup, AccessoryId.ShoppingBag, AccessoryId.Phone }
            },
            new ArchetypeRule {
                archetype = OutfitArchetype.Tourist,
                allowedNeutralTopIndices = new[]{ 4, 3, 2 },
                allowedNeutralBottomIndices = new[]{ 3, 6, 7 },
                allowedAccentIndices = new[]{ 1, 4, 5, 7 },
                accentOnTopChance = 0.65f, stripeOrPatternChance = 0.55f, openJacketChance = 0.30f,
                minAccessories = 2, maxAccessories = 3,
                headPool = new[]{ AccessoryId.Sunglasses, AccessoryId.Cap },
                bodyPool = new[]{ AccessoryId.Backpack, AccessoryId.ShoulderBag, AccessoryId.Lanyard },
                handPool = new[]{ AccessoryId.ShoppingBag, AccessoryId.Umbrella, AccessoryId.Phone }
            },
            new ArchetypeRule {
                archetype = OutfitArchetype.Shopkeeper,
                allowedNeutralTopIndices = new[]{ 4, 3, 6, 1 },
                allowedNeutralBottomIndices = new[]{ 5, 1, 0 },
                allowedAccentIndices = new[]{ 0, 1, 3 },
                accentOnTopChance = 0.40f, stripeOrPatternChance = 0.35f, openJacketChance = 0.40f,
                minAccessories = 1, maxAccessories = 2,
                headPool = new[]{ AccessoryId.Glasses },
                bodyPool = new[]{ AccessoryId.Lanyard, AccessoryId.Tote },
                handPool = new[]{ AccessoryId.CoffeeCup, AccessoryId.BookFolder }
            },
            new ArchetypeRule {
                archetype = OutfitArchetype.Elder,
                allowedNeutralTopIndices = new[]{ 3, 2, 6, 0 },
                allowedNeutralBottomIndices = new[]{ 1, 3, 5, 0 },
                allowedAccentIndices = new[]{ 0, 6, 3 },
                accentOnTopChance = 0.30f, stripeOrPatternChance = 0.25f, openJacketChance = 0.50f,
                minAccessories = 2, maxAccessories = 3,
                headPool = new[]{ AccessoryId.Glasses },
                bodyPool = new[]{ AccessoryId.Scarf },
                handPool = new[]{ AccessoryId.Umbrella, AccessoryId.ShoppingBag }
            },
            new ArchetypeRule {
                archetype = OutfitArchetype.Construction,
                allowedNeutralTopIndices = new[]{ 1, 0, 2, 6 },
                allowedNeutralBottomIndices = new[]{ 7, 1, 5 },
                allowedAccentIndices = new[]{ 2 }, // Hi-Vis Amber
                accentOnTopChance = 0.0f, stripeOrPatternChance = 0.0f, openJacketChance = 0.0f,
                minAccessories = 2, maxAccessories = 2,
                headPool = new[]{ AccessoryId.HardHat },
                bodyPool = new[]{ AccessoryId.Vest },
                handPool = new AccessoryId[0]
            },
            new ArchetypeRule {
                archetype = OutfitArchetype.Delivery,
                allowedNeutralTopIndices = new[]{ 0, 1, 6 },
                allowedNeutralBottomIndices = new[]{ 5, 1, 3 },
                allowedAccentIndices = new[]{ 5, 3, 1, 0 },
                accentOnTopChance = 0.75f, stripeOrPatternChance = 0.15f, openJacketChance = 0.25f,
                minAccessories = 2, maxAccessories = 3,
                headPool = new[]{ AccessoryId.Cap },
                bodyPool = new[]{ AccessoryId.ShoulderBag, AccessoryId.Backpack },
                handPool = new[]{ AccessoryId.Phone }
            },
            new ArchetypeRule {
                archetype = OutfitArchetype.Artist,
                allowedNeutralTopIndices = new[]{ 5, 1, 4, 6 },
                allowedNeutralBottomIndices = new[]{ 5, 1, 3, 7 },
                allowedAccentIndices = new[]{ 6, 4, 1, 0, 2 },
                accentOnTopChance = 0.55f, stripeOrPatternChance = 0.45f, openJacketChance = 0.50f,
                minAccessories = 2, maxAccessories = 3,
                headPool = new[]{ AccessoryId.Beanie, AccessoryId.Glasses, AccessoryId.Sunglasses },
                bodyPool = new[]{ AccessoryId.Scarf, AccessoryId.Tote },
                handPool = new[]{ AccessoryId.BookFolder, AccessoryId.CoffeeCup }
            }
        };
    }
}
