using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EcoQuest.CityQuest.Editor
{
    public static class CityTrashAssetGeneratorAndValidator
    {
        public const string TrashRoot = "Assets/EcoQuest/CityTrash";
        public const string MeshesFolder = "Assets/EcoQuest/CityTrash/Meshes";
        public const string MaterialsFolder = "Assets/EcoQuest/CityTrash/Materials";
        public const string PrefabsFolder = "Assets/EcoQuest/CityTrash/Prefabs";
        public const string IconsFolder = "Assets/EcoQuest/CityTrash/Icons";
        public const string DataFolder = "Assets/EcoQuest/CityTrash/Data";
        public const string DatabasePath = "Assets/EcoQuest/CityTrash/Data/CityTrashDatabase.asset";
        public const string ContactSheetOutputPath = @"C:\EcoQuest.gg\Reports\CityTrash\ContactSheet.png";

        private struct RawItemSpec
        {
            public string id;
            public string displayName;
            public LitterCategory category;
            public bool isRare;
            public Color c0; // Primary body
            public Color c1; // Secondary / label
            public Color c2; // Cap / rim / accent
            public Color c3; // Dark / detail
            public DistrictWeight[] weights;
            public string funFact;
            public string quizExplanation;
        }

        private static RawItemSpec[] GetAll24Specs()
        {
            return new RawItemSpec[]
            {
                // ==================== BIODEGRADABLE (6) ====================
                new RawItemSpec
                {
                    id = "BananaPeel",
                    displayName = "Banana Peel",
                    category = LitterCategory.Biodegradable,
                    isRare = false,
                    c0 = new Color(0.96f, 0.82f, 0.18f),
                    c1 = new Color(0.98f, 0.94f, 0.72f),
                    c2 = new Color(0.75f, 0.58f, 0.14f),
                    c3 = new Color(0.36f, 0.24f, 0.10f),
                    weights = new[] { new DistrictWeight(CityDistrict.Park, 5f), new DistrictWeight(CityDistrict.Market, 4f), new DistrictWeight(CityDistrict.Residential, 3f), new DistrictWeight(CityDistrict.Riverside, 2f) },
                    funFact = "Banana peels rot into rich soil food in a few weeks when composted!",
                    quizExplanation = "Banana peels are organic fruit scraps that decompose naturally into nutrient-rich compost."
                },
                new RawItemSpec
                {
                    id = "AppleCore",
                    displayName = "Apple Core",
                    category = LitterCategory.Biodegradable,
                    isRare = false,
                    c0 = new Color(0.88f, 0.22f, 0.20f),
                    c1 = new Color(0.96f, 0.90f, 0.70f),
                    c2 = new Color(0.32f, 0.74f, 0.28f),
                    c3 = new Color(0.38f, 0.24f, 0.12f),
                    weights = new[] { new DistrictWeight(CityDistrict.Park, 5f), new DistrictWeight(CityDistrict.Residential, 4f), new DistrictWeight(CityDistrict.Market, 3f), new DistrictWeight(CityDistrict.Riverside, 2f) },
                    funFact = "Apple cores break down in compost in about a month and feed earthworms!",
                    quizExplanation = "Apple cores are natural plant matter that breaks down quickly in a biodegradable compost bin."
                },
                new RawItemSpec
                {
                    id = "DryLeavesBundle",
                    displayName = "Dry Leaves Bundle",
                    category = LitterCategory.Biodegradable,
                    isRare = false,
                    c0 = new Color(0.84f, 0.48f, 0.16f),
                    c1 = new Color(0.92f, 0.68f, 0.20f),
                    c2 = new Color(0.56f, 0.68f, 0.24f),
                    c3 = new Color(0.42f, 0.26f, 0.14f),
                    weights = new[] { new DistrictWeight(CityDistrict.Park, 6f), new DistrictWeight(CityDistrict.Riverside, 5f), new DistrictWeight(CityDistrict.Residential, 3f) },
                    funFact = "Fallen leaves turn into natural forest mulch that keeps soil moist!",
                    quizExplanation = "Dry leaves and twigs are 100% organic yard waste that enriches soil as biodegradable mulch."
                },
                new RawItemSpec
                {
                    id = "PaperNapkin",
                    displayName = "Paper Napkin",
                    category = LitterCategory.Biodegradable,
                    isRare = false,
                    c0 = new Color(0.95f, 0.93f, 0.88f),
                    c1 = new Color(0.84f, 0.68f, 0.48f),
                    c2 = new Color(0.86f, 0.84f, 0.78f),
                    c3 = new Color(0.65f, 0.50f, 0.35f),
                    weights = new[] { new DistrictWeight(CityDistrict.Market, 5f), new DistrictWeight(CityDistrict.MainStreet, 4f), new DistrictWeight(CityDistrict.Park, 3f) },
                    funFact = "Clean or food-stained paper napkins can decompose in a compost bin!",
                    quizExplanation = "Unbleached or food-soiled paper napkins have short plant fibers that compost cleanly."
                },
                new RawItemSpec
                {
                    id = "CardboardSleeve",
                    displayName = "Cardboard Cup Sleeve",
                    category = LitterCategory.Biodegradable,
                    isRare = false,
                    c0 = new Color(0.76f, 0.56f, 0.36f),
                    c1 = new Color(0.64f, 0.45f, 0.28f),
                    c2 = new Color(0.28f, 0.66f, 0.36f),
                    c3 = new Color(0.36f, 0.24f, 0.14f),
                    weights = new[] { new DistrictWeight(CityDistrict.MainStreet, 5f), new DistrictWeight(CityDistrict.Market, 4f), new DistrictWeight(CityDistrict.Park, 2f) },
                    funFact = "Unwaxed cardboard coffee sleeves come from trees and break down naturally!",
                    quizExplanation = "Plain corrugated cup sleeves are uncoated wood pulp, making them biodegradable."
                },
                new RawItemSpec
                {
                    id = "WoodenFork",
                    displayName = "Wooden Fork",
                    category = LitterCategory.Biodegradable,
                    isRare = false,
                    c0 = new Color(0.86f, 0.72f, 0.50f),
                    c1 = new Color(0.76f, 0.60f, 0.38f),
                    c2 = new Color(0.30f, 0.68f, 0.34f),
                    c3 = new Color(0.48f, 0.34f, 0.18f),
                    weights = new[] { new DistrictWeight(CityDistrict.Market, 5f), new DistrictWeight(CityDistrict.Park, 4f), new DistrictWeight(CityDistrict.Riverside, 3f) },
                    funFact = "Untreated birch wood forks rot back into nature just like small twigs!",
                    quizExplanation = "Natural wooden cutlery has no plastic coating and decomposes safely with organic waste."
                },

                // ==================== RECYCLABLE (8) ====================
                new RawItemSpec
                {
                    id = "PlasticWaterBottle",
                    displayName = "Plastic Water Bottle",
                    category = LitterCategory.Recyclable,
                    isRare = false,
                    c0 = new Color(0.52f, 0.82f, 0.96f),
                    c1 = new Color(0.18f, 0.52f, 0.88f),
                    c2 = new Color(0.12f, 0.36f, 0.76f),
                    c3 = new Color(0.88f, 0.95f, 1.00f),
                    weights = new[] { new DistrictWeight(CityDistrict.Park, 4f), new DistrictWeight(CityDistrict.MainStreet, 4f), new DistrictWeight(CityDistrict.Riverside, 5f), new DistrictWeight(CityDistrict.Market, 3f) },
                    funFact = "Recycling one plastic bottle saves enough energy to power a lamp for hours!",
                    quizExplanation = "Clean PET plastic water bottles are highly recyclable and can be spun into new bottles or fleece."
                },
                new RawItemSpec
                {
                    id = "CrushedSodaCan",
                    displayName = "Crushed Soda Can",
                    category = LitterCategory.Recyclable,
                    isRare = false,
                    c0 = new Color(0.90f, 0.22f, 0.24f),
                    c1 = new Color(0.96f, 0.96f, 0.98f),
                    c2 = new Color(0.76f, 0.80f, 0.84f),
                    c3 = new Color(0.35f, 0.38f, 0.42f),
                    weights = new[] { new DistrictWeight(CityDistrict.MainStreet, 5f), new DistrictWeight(CityDistrict.Market, 4f), new DistrictWeight(CityDistrict.Park, 3f), new DistrictWeight(CityDistrict.Residential, 3f) },
                    funFact = "Aluminum soda cans can be recycled again and again without losing quality!",
                    quizExplanation = "Aluminum cans are 100% recyclable and save 95% of the energy needed to make new metal."
                },
                new RawItemSpec
                {
                    id = "CardboardBox",
                    displayName = "Folded Cardboard Box",
                    category = LitterCategory.Recyclable,
                    isRare = false,
                    c0 = new Color(0.78f, 0.58f, 0.36f),
                    c1 = new Color(0.68f, 0.48f, 0.28f),
                    c2 = new Color(0.88f, 0.80f, 0.62f),
                    c3 = new Color(0.95f, 0.95f, 0.92f),
                    weights = new[] { new DistrictWeight(CityDistrict.Market, 5f), new DistrictWeight(CityDistrict.MainStreet, 4f), new DistrictWeight(CityDistrict.Residential, 4f) },
                    funFact = "Flattening cardboard boxes saves space and turns old boxes into new paper!",
                    quizExplanation = "Clean corrugated cardboard boxes have strong paper fibers that paper mills recycle into new boxes."
                },
                new RawItemSpec
                {
                    id = "NewspaperStack",
                    displayName = "Rolled Newspaper",
                    category = LitterCategory.Recyclable,
                    isRare = false,
                    c0 = new Color(0.90f, 0.89f, 0.84f),
                    c1 = new Color(0.28f, 0.52f, 0.78f),
                    c2 = new Color(0.82f, 0.32f, 0.26f),
                    c3 = new Color(0.24f, 0.26f, 0.28f),
                    weights = new[] { new DistrictWeight(CityDistrict.Residential, 5f), new DistrictWeight(CityDistrict.MainStreet, 4f), new DistrictWeight(CityDistrict.Park, 3f) },
                    funFact = "Old newspapers can be pulped and reborn as fresh paper in just seven days!",
                    quizExplanation = "Clean newspapers are standard paper recyclables that save trees when re-pulped."
                },
                new RawItemSpec
                {
                    id = "GlassJar",
                    displayName = "Empty Glass Jar",
                    category = LitterCategory.Recyclable,
                    isRare = false,
                    c0 = new Color(0.62f, 0.88f, 0.82f),
                    c1 = new Color(0.96f, 0.86f, 0.42f),
                    c2 = new Color(0.82f, 0.66f, 0.24f),
                    c3 = new Color(0.24f, 0.54f, 0.48f),
                    weights = new[] { new DistrictWeight(CityDistrict.Residential, 5f), new DistrictWeight(CityDistrict.Market, 4f), new DistrictWeight(CityDistrict.Riverside, 2f) },
                    funFact = "Glass jars can be melted and remade endlessly without ever wearing out!",
                    quizExplanation = "Empty, unbroken glass jars are 100% recyclable and can be melted into new jars forever."
                },
                new RawItemSpec
                {
                    id = "MilkCarton",
                    displayName = "Milk Carton",
                    category = LitterCategory.Recyclable,
                    isRare = false,
                    c0 = new Color(0.96f, 0.97f, 0.98f),
                    c1 = new Color(0.22f, 0.62f, 0.92f),
                    c2 = new Color(0.14f, 0.42f, 0.78f),
                    c3 = new Color(0.88f, 0.28f, 0.28f),
                    weights = new[] { new DistrictWeight(CityDistrict.Residential, 5f), new DistrictWeight(CityDistrict.Market, 4f), new DistrictWeight(CityDistrict.MainStreet, 2f) },
                    funFact = "Rinsed milk cartons contain high-grade paper fibers that mills love to reuse!",
                    quizExplanation = "Rinsed beverage and milk cartons are recyclable paperboard containers."
                },
                new RawItemSpec
                {
                    id = "TinCan",
                    displayName = "Empty Tin Can",
                    category = LitterCategory.Recyclable,
                    isRare = false,
                    c0 = new Color(0.74f, 0.78f, 0.82f),
                    c1 = new Color(0.26f, 0.66f, 0.42f),
                    c2 = new Color(0.86f, 0.88f, 0.90f),
                    c3 = new Color(0.42f, 0.45f, 0.48f),
                    weights = new[] { new DistrictWeight(CityDistrict.Residential, 5f), new DistrictWeight(CityDistrict.Market, 4f), new DistrictWeight(CityDistrict.Riverside, 2f) },
                    funFact = "Steel tin cans are magnetic, making them easy to sort and recycle at plants!",
                    quizExplanation = "Steel and tin food cans are recyclable metals that magnets pull straight off the sorting belt."
                },
                new RawItemSpec
                {
                    id = "FlyerStack",
                    displayName = "Stack of Flyers",
                    category = LitterCategory.Recyclable,
                    isRare = false,
                    c0 = new Color(0.98f, 0.88f, 0.36f),
                    c1 = new Color(0.38f, 0.78f, 0.96f),
                    c2 = new Color(0.96f, 0.56f, 0.72f),
                    c3 = new Color(0.22f, 0.32f, 0.48f),
                    weights = new[] { new DistrictWeight(CityDistrict.MainStreet, 5f), new DistrictWeight(CityDistrict.Market, 4f), new DistrictWeight(CityDistrict.Residential, 3f) },
                    funFact = "Paper flyers recycle into egg cartons, notebooks, and new cardboard boxes!",
                    quizExplanation = "Paper flyers and leaflets are clean mixed paper that goes in the Recyclable bin."
                },

                // ==================== NON-BIODEGRADABLE (5) ====================
                new RawItemSpec
                {
                    id = "ChipBag",
                    displayName = "Foil Chip Bag",
                    category = LitterCategory.NonBiodegradable,
                    isRare = false,
                    c0 = new Color(0.94f, 0.36f, 0.14f),
                    c1 = new Color(0.98f, 0.82f, 0.18f),
                    c2 = new Color(0.78f, 0.82f, 0.86f),
                    c3 = new Color(0.22f, 0.18f, 0.16f),
                    weights = new[] { new DistrictWeight(CityDistrict.Park, 4f), new DistrictWeight(CityDistrict.Market, 4f), new DistrictWeight(CityDistrict.MainStreet, 4f), new DistrictWeight(CityDistrict.Residential, 3f) },
                    funFact = "Shiny chip bags mix plastic and foil layers, so regular mills cannot recycle them.",
                    quizExplanation = "Foil-lined chip bags fuse plastic and aluminum layers together, making them non-biodegradable residual waste."
                },
                new RawItemSpec
                {
                    id = "StyrofoamCup",
                    displayName = "Styrofoam Cup",
                    category = LitterCategory.NonBiodegradable,
                    isRare = false,
                    c0 = new Color(0.94f, 0.94f, 0.96f),
                    c1 = new Color(0.88f, 0.46f, 0.22f),
                    c2 = new Color(0.84f, 0.86f, 0.88f),
                    c3 = new Color(0.45f, 0.32f, 0.24f),
                    weights = new[] { new DistrictWeight(CityDistrict.MainStreet, 4f), new DistrictWeight(CityDistrict.Market, 4f), new DistrictWeight(CityDistrict.Riverside, 5f) },
                    funFact = "Styrofoam crumbles into tiny beads that never rot and can harm water birds.",
                    quizExplanation = "Expanded polystyrene (Styrofoam) does not biodegrade and is not accepted in standard recycling."
                },
                new RawItemSpec
                {
                    id = "PlasticStrawWrapper",
                    displayName = "Plastic Straw & Wrapper",
                    category = LitterCategory.NonBiodegradable,
                    isRare = false,
                    c0 = new Color(0.96f, 0.34f, 0.52f),
                    c1 = new Color(0.98f, 0.96f, 0.96f),
                    c2 = new Color(0.92f, 0.90f, 0.84f),
                    c3 = new Color(0.28f, 0.62f, 0.88f),
                    weights = new[] { new DistrictWeight(CityDistrict.Market, 5f), new DistrictWeight(CityDistrict.Riverside, 4f), new DistrictWeight(CityDistrict.Park, 4f) },
                    funFact = "Lightweight straws blow into storm drains easily and cannot be recycled curbside.",
                    quizExplanation = "Single-use plastic straws are too small and flexible for sorting screens, so they go in Non-Biodegradable trash."
                },
                new RawItemSpec
                {
                    id = "CandyWrapper",
                    displayName = "Candy Wrapper",
                    category = LitterCategory.NonBiodegradable,
                    isRare = false,
                    c0 = new Color(0.68f, 0.28f, 0.88f),
                    c1 = new Color(0.98f, 0.84f, 0.24f),
                    c2 = new Color(0.92f, 0.46f, 0.78f),
                    c3 = new Color(0.34f, 0.14f, 0.48f),
                    weights = new[] { new DistrictWeight(CityDistrict.Park, 5f), new DistrictWeight(CityDistrict.Residential, 4f), new DistrictWeight(CityDistrict.MainStreet, 3f) },
                    funFact = "Crinkly candy wrappers are too small and mixed-plastic for recycling machines.",
                    quizExplanation = "Plastic candy wrappers neither decompose nor recycle in curbside bins, so they belong in Non-Biodegradable waste."
                },
                new RawItemSpec
                {
                    id = "TornPlasticBag",
                    displayName = "Torn Plastic Bag",
                    category = LitterCategory.NonBiodegradable,
                    isRare = false,
                    c0 = new Color(0.78f, 0.88f, 0.94f),
                    c1 = new Color(0.92f, 0.38f, 0.32f),
                    c2 = new Color(0.64f, 0.76f, 0.84f),
                    c3 = new Color(0.32f, 0.44f, 0.52f),
                    weights = new[] { new DistrictWeight(CityDistrict.Riverside, 5f), new DistrictWeight(CityDistrict.Market, 4f), new DistrictWeight(CityDistrict.Park, 3f), new DistrictWeight(CityDistrict.Residential, 3f) },
                    funFact = "Thin plastic bags tangle sorting machines and must stay out of regular bins.",
                    quizExplanation = "Torn plastic film bags jam recycling machinery and do not rot in nature, making them Non-Biodegradable."
                },

                // ==================== HAZARDOUS / RARE (5) ====================
                new RawItemSpec
                {
                    id = "AABattery",
                    displayName = "Used AA Battery",
                    category = LitterCategory.Hazardous,
                    isRare = true,
                    c0 = new Color(0.18f, 0.20f, 0.24f),
                    c1 = new Color(0.94f, 0.68f, 0.14f),
                    c2 = new Color(0.82f, 0.84f, 0.88f),
                    c3 = new Color(0.88f, 0.24f, 0.20f),
                    weights = new[] { new DistrictWeight(CityDistrict.Residential, 5f), new DistrictWeight(CityDistrict.MainStreet, 4f), new DistrictWeight(CityDistrict.Market, 2f) },
                    funFact = "Batteries hold corrosive metals that can leak into soil if tossed in regular trash!",
                    quizExplanation = "Used batteries contain heavy metals and corrosive electrolytes that require Hazardous waste drop-off."
                },
                new RawItemSpec
                {
                    id = "SprayPaintCan",
                    displayName = "Empty Spray Paint Can",
                    category = LitterCategory.Hazardous,
                    isRare = true,
                    c0 = new Color(0.88f, 0.20f, 0.22f),
                    c1 = new Color(0.98f, 0.86f, 0.22f),
                    c2 = new Color(0.78f, 0.82f, 0.86f),
                    c3 = new Color(0.18f, 0.20f, 0.22f),
                    weights = new[] { new DistrictWeight(CityDistrict.MainStreet, 5f), new DistrictWeight(CityDistrict.Riverside, 4f), new DistrictWeight(CityDistrict.Residential, 2f) },
                    funFact = "Pressurized aerosol cans can burst or leak toxic chemicals if not handled safely!",
                    quizExplanation = "Aerosol spray paint cans contain pressurized propellants and chemical solvents classified as Hazardous waste."
                },
                new RawItemSpec
                {
                    id = "BrokenLightBulb",
                    displayName = "Compact Light Bulb",
                    category = LitterCategory.Hazardous,
                    isRare = true,
                    c0 = new Color(0.92f, 0.96f, 0.98f),
                    c1 = new Color(0.76f, 0.88f, 0.96f),
                    c2 = new Color(0.72f, 0.76f, 0.80f),
                    c3 = new Color(0.28f, 0.30f, 0.34f),
                    weights = new[] { new DistrictWeight(CityDistrict.Residential, 5f), new DistrictWeight(CityDistrict.MainStreet, 4f), new DistrictWeight(CityDistrict.Riverside, 2f) },
                    funFact = "Some compact bulbs contain trace mercury and need special hazardous drop-off!",
                    quizExplanation = "Compact bulbs and fragile glass lamps require Hazardous collection to prevent mercury release and injuries."
                },
                new RawItemSpec
                {
                    id = "MedicineBlisterPack",
                    displayName = "Empty Medicine Blister",
                    category = LitterCategory.Hazardous,
                    isRare = true,
                    c0 = new Color(0.82f, 0.86f, 0.90f),
                    c1 = new Color(0.92f, 0.28f, 0.32f),
                    c2 = new Color(0.70f, 0.74f, 0.78f),
                    c3 = new Color(0.24f, 0.58f, 0.88f),
                    weights = new[] { new DistrictWeight(CityDistrict.Residential, 5f), new DistrictWeight(CityDistrict.Market, 4f), new DistrictWeight(CityDistrict.Riverside, 2f) },
                    funFact = "Leftover medicine packs can pollute waterways if flushed or thrown in open trash!",
                    quizExplanation = "Pharmaceutical blister packs can carry chemical residues that belong in Hazardous/medical take-back bins."
                },
                new RawItemSpec
                {
                    id = "SmallCircuitBoard",
                    displayName = "E-Waste Circuit Board",
                    category = LitterCategory.Hazardous,
                    isRare = true,
                    c0 = new Color(0.14f, 0.56f, 0.32f),
                    c1 = new Color(0.92f, 0.76f, 0.22f),
                    c2 = new Color(0.76f, 0.80f, 0.84f),
                    c3 = new Color(0.18f, 0.20f, 0.22f),
                    weights = new[] { new DistrictWeight(CityDistrict.MainStreet, 5f), new DistrictWeight(CityDistrict.Residential, 4f), new DistrictWeight(CityDistrict.Market, 3f) },
                    funFact = "E-waste circuit boards hold lead and gold that need special electronics recovery!",
                    quizExplanation = "Electronic circuit boards contain lead solder and heavy metals, so they must go to Hazardous E-Waste recovery."
                }
            };
        }

        private static void EnsureFolders()
        {
            string[] folders = { TrashRoot, MeshesFolder, MaterialsFolder, PrefabsFolder, IconsFolder, DataFolder };
            foreach (string f in folders)
            {
                if (!AssetDatabase.IsValidFolder(f))
                {
                    string parent = Path.GetDirectoryName(f).Replace("\\", "/");
                    string name = Path.GetFileName(f);
                    if (!AssetDatabase.IsValidFolder(parent))
                    {
                        Directory.CreateDirectory(f);
                    }
                    else
                    {
                        AssetDatabase.CreateFolder(parent, name);
                    }
                }
            }
        }

        public static CityTrashDatabase GenerateAllTrashAssets()
        {
            EnsureFolders();
            RawItemSpec[] specs = GetAll24Specs();

            CityTrashDatabase db = AssetDatabase.LoadAssetAtPath<CityTrashDatabase>(DatabasePath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<CityTrashDatabase>();
                AssetDatabase.CreateAsset(db, DatabasePath);
            }
            db.items.Clear();

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) urpLit = Shader.Find("Standard");

            for (int i = 0; i < specs.Length; i++)
            {
                RawItemSpec spec = specs[i];

                // 1. Create 3 variant meshes (80 <= tris <= 400 each, base-center pivot)
                Mesh[] vMeshes = new Mesh[3];
                for (int v = 0; v < 3; v++)
                {
                    string meshPath = $"{MeshesFolder}/Mesh_{spec.id}_v{v}.asset";
                    Mesh m = CityTrashProceduralMeshFactory.BuildItemMesh(spec.id, v);
                    Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (existingMesh != null)
                    {
                        existingMesh.Clear();
                        existingMesh.SetVertices(m.vertices);
                        existingMesh.SetUVs(0, m.uv);
                        existingMesh.SetTriangles(m.triangles, 0);
                        existingMesh.RecalculateNormals();
                        existingMesh.RecalculateBounds();
                        EditorUtility.SetDirty(existingMesh);
                        UnityEngine.Object.DestroyImmediate(m);
                        vMeshes[v] = existingMesh;
                    }
                    else
                    {
                        AssetDatabase.CreateAsset(m, meshPath);
                        vMeshes[v] = m;
                    }
                }

                // 2. Create 2 palette textures & GPU-instanced URP Lit materials per item
                Material[] vMats = new Material[2];
                for (int mIdx = 0; mIdx < 2; mIdx++)
                {
                    Color c0 = mIdx == 0 ? spec.c0 : Color.Lerp(spec.c0, spec.c1, 0.28f);
                    Color c1 = mIdx == 0 ? spec.c1 : Color.Lerp(spec.c1, Color.white, 0.20f);
                    Texture2D palTex = CreatePaletteTexture(spec.id, mIdx, c0, c1, spec.c2, spec.c3);

                    string matPath = $"{MaterialsFolder}/Mat_{spec.id}_v{mIdx}.mat";
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat == null)
                    {
                        mat = new Material(urpLit);
                        AssetDatabase.CreateAsset(mat, matPath);
                    }
                    mat.shader = urpLit;
                    mat.color = Color.white;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", palTex);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", palTex);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
                    mat.enableInstancing = true;
                    EditorUtility.SetDirty(mat);
                    vMats[mIdx] = mat;
                }

                // 3. Create Prefab under Assets/EcoQuest/CityTrash/Prefabs/
                string prefabPath = $"{PrefabsFolder}/CityTrash_{spec.id}.prefab";
                GameObject tempRoot = new GameObject($"CityTrash_{spec.id}");
                tempRoot.transform.position = Vector3.zero;
                tempRoot.transform.rotation = Quaternion.identity;
                tempRoot.transform.localScale = Vector3.one;

                MeshFilter mf = tempRoot.AddComponent<MeshFilter>();
                mf.sharedMesh = vMeshes[0];

                MeshRenderer mr = tempRoot.AddComponent<MeshRenderer>();
                mr.sharedMaterial = vMats[0];

                BoxCollider bc = tempRoot.AddComponent<BoxCollider>();
                Bounds b = vMeshes[0].bounds;
                bc.center = b.center;
                bc.size = Vector3.Max(b.size, new Vector3(0.18f, 0.12f, 0.18f));
                bc.isTrigger = false;

                var varSelector = tempRoot.AddComponent<CityTrashVisualVariant>();
                varSelector.targetMeshFilter = mf;
                varSelector.targetRenderer = mr;
                varSelector.variantMeshes = vMeshes;
                varSelector.variantMaterials = vMats;

                GameObject shimmerAnchor = new GameObject("ShimmerAnchor");
                shimmerAnchor.transform.SetParent(tempRoot.transform, false);
                shimmerAnchor.transform.localPosition = new Vector3(0f, b.max.y + 0.12f, 0f);

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(tempRoot, prefabPath);
                UnityEngine.Object.DestroyImmediate(tempRoot);

                // 4. Render 256x256 transparent orthographic icon
                Sprite iconSprite = RenderAndImportIcon(spec.id, vMeshes[0], vMats[0]);

                // 5. Add entry to CityTrashDatabase
                CityTrashItemDef def = new CityTrashItemDef
                {
                    id = spec.id,
                    displayName = spec.displayName,
                    category = spec.category,
                    icon = iconSprite,
                    worldPrefab = savedPrefab,
                    districtWeights = new List<DistrictWeight>(spec.weights),
                    funFact = spec.funFact,
                    quizExplanation = spec.quizExplanation,
                    isRare = spec.isRare
                };
                db.items.Add(def);
            }

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CityTrashGenerator] Generated 24 trash items, 72 variant meshes, 48 GPU-instanced materials, 24 prefabs, and 24 icons in '{TrashRoot}'.");
            return db;
        }

        private static Texture2D CreatePaletteTexture(string id, int variantIdx, Color q0, Color q1, Color q2, Color q3)
        {
            string texPath = $"{MaterialsFolder}/Pal_{id}_v{variantIdx}.png";
            int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool right = x >= size / 2;
                    bool top = y >= size / 2;
                    Color c = !top ? (!right ? q0 : q1) : (!right ? q2 : q3);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (ti != null)
            {
                ti.textureType = TextureImporterType.Default;
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        }

        private static Sprite RenderAndImportIcon(string id, Mesh mesh, Material mat)
        {
            string iconAssetPath = $"{IconsFolder}/{id}.png";
            Texture2D rendered = RenderMeshPreviewToTexture(mesh, mat, 256, 256, new Color(0f, 0f, 0f, 0f), true);
            File.WriteAllBytes(iconAssetPath, rendered.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(rendered);

            AssetDatabase.ImportAsset(iconAssetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter ti = AssetImporter.GetAtPath(iconAssetPath) as TextureImporter;
            if (ti != null)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.filterMode = FilterMode.Bilinear;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(iconAssetPath);
        }

        public static Texture2D RenderMeshPreviewToTexture(Mesh mesh, Material mat, int width, int height, Color bgColor, bool addSoftShadow)
        {
            int previewLayer = 31;
            GameObject stageRoot = new GameObject("__TempIconStage__");
            stageRoot.hideFlags = HideFlags.HideAndDontSave;
            Vector3 stageOrigin = new Vector3(5000f, 5000f, 5000f);
            stageRoot.transform.position = stageOrigin;

            GameObject itemGo = new GameObject("PreviewMesh");
            itemGo.layer = previewLayer;
            itemGo.transform.SetParent(stageRoot.transform, false);
            MeshFilter mf = itemGo.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = itemGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            Bounds b = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.25f);
            itemGo.transform.localPosition = -b.center;

            GameObject camGo = new GameObject("PreviewCam");
            camGo.transform.SetParent(stageRoot.transform, false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = bgColor;
            cam.cullingMask = 1 << previewLayer;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 15f;

            // Consistent 30 deg pitch, 35 deg yaw angle
            Quaternion camRot = Quaternion.Euler(30f, 35f, 0f);
            camGo.transform.localRotation = camRot;
            camGo.transform.localPosition = camRot * new Vector3(0f, 0f, -3.5f);

            float maxExtent = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
            cam.orthographicSize = Mathf.Max(0.08f, maxExtent * 1.42f);

            GameObject keyLightGo = new GameObject("KeyLight");
            keyLightGo.transform.SetParent(stageRoot.transform, false);
            keyLightGo.transform.localRotation = Quaternion.Euler(48f, -30f, 0f);
            Light keyLight = keyLightGo.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.35f;
            keyLight.color = new Color(1f, 0.98f, 0.94f);

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D outTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            outTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            outTex.Apply();

            cam.targetTexture = null;
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(stageRoot);

            if (addSoftShadow)
            {
                AddSoftDropShadowAndOutline(outTex);
            }

            return outTex;
        }

        private static void AddSoftDropShadowAndOutline(Texture2D tex)
        {
            int w = tex.width;
            int h = tex.height;
            Color[] src = tex.GetPixels();
            Color[] dst = new Color[src.Length];

            // Soft ground shadow ellipse at bottom + crisp dark silhouette rim
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    Color c = src[idx];
                    if (c.a > 0.1f)
                    {
                        dst[idx] = c;
                        continue;
                    }

                    // Check 2px neighborhood for subtle dark outline so icon pops on any background
                    float maxNeighborA = 0f;
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= h) continue;
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= w) continue;
                            float a = src[ny * w + nx].a;
                            if (a > maxNeighborA) maxNeighborA = a;
                        }
                    }

                    if (maxNeighborA > 0.25f)
                    {
                        dst[idx] = new Color(0.08f, 0.14f, 0.10f, maxNeighborA * 0.72f);
                    }
                    else
                    {
                        // Soft ground shadow disc at bottom center
                        float nx = (x - w * 0.5f) / (w * 0.34f);
                        float ny = (y - h * 0.16f) / (h * 0.08f);
                        float rSq = nx * nx + ny * ny;
                        if (rSq < 1f)
                        {
                            float sa = (1f - rSq) * 0.28f;
                            dst[idx] = new Color(0f, 0f, 0f, sa);
                        }
                        else
                        {
                            dst[idx] = new Color(0f, 0f, 0f, 0f);
                        }
                    }
                }
            }

            tex.SetPixels(dst);
            tex.Apply();
        }

        // =====================================================================
        // MENU ITEM 1: VALIDATE CITY TRASH ASSETS
        // =====================================================================

        [MenuItem("EcoQuest/Validate City Trash Assets")]
        public static bool ValidateCityTrashAssets()
        {
            CityTrashDatabase db = AssetDatabase.LoadAssetAtPath<CityTrashDatabase>(DatabasePath);
            if (db == null || db.items == null || db.items.Count == 0)
            {
                Debug.LogError($"[ValidateCityTrashAssets] FAIL: Database not found or empty at '{DatabasePath}'.");
                return false;
            }

            int passCount = 0;
            int failCount = 0;

            for (int i = 0; i < db.items.Count; i++)
            {
                var item = db.items[i];
                if (item == null)
                {
                    Debug.LogError($"[ValidateCityTrashAssets] Item #{i}: FAIL (null entry)");
                    failCount++;
                    continue;
                }

                List<string> reasons = new List<string>();

                if (string.IsNullOrEmpty(item.id)) reasons.Add("missing id");
                if (string.IsNullOrEmpty(item.displayName)) reasons.Add("missing displayName");
                if (!Enum.IsDefined(typeof(LitterCategory), item.category)) reasons.Add("invalid category");
                if (item.icon == null) reasons.Add("missing icon Sprite");
                if (string.IsNullOrEmpty(item.funFact)) reasons.Add("empty funFact");
                else if (item.funFact.Length > 90) reasons.Add($"funFact length {item.funFact.Length} > 90");

                int tris = 0;
                bool hasMat = false;
                bool hasCol = false;
                if (item.worldPrefab == null)
                {
                    reasons.Add("missing worldPrefab");
                }
                else
                {
                    MeshFilter mf = item.worldPrefab.GetComponentInChildren<MeshFilter>(true);
                    if (mf == null || mf.sharedMesh == null)
                    {
                        reasons.Add("missing MeshFilter/sharedMesh");
                    }
                    else
                    {
                        tris = mf.sharedMesh.triangles.Length / 3;
                        if (tris < 80 || tris > 400)
                        {
                            reasons.Add($"triangle count {tris} outside [80..400]");
                        }
                    }

                    MeshRenderer mr = item.worldPrefab.GetComponentInChildren<MeshRenderer>(true);
                    hasMat = (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.enableInstancing);
                    if (!hasMat) reasons.Add("missing Material or GPU instancing disabled");

                    Collider col = item.worldPrefab.GetComponentInChildren<Collider>(true);
                    hasCol = (col != null && !col.isTrigger);
                    if (!hasCol) reasons.Add("missing non-trigger Collider");
                }

                if (reasons.Count == 0)
                {
                    passCount++;
                    Debug.Log($"[ValidateCityTrashAssets] PASS [{i + 1:00}/24] {item.id} ('{item.displayName}') | Cat={item.category} | Tris={tris} | FactLen={item.funFact.Length} | Icon={item.icon.name}");
                }
                else
                {
                    failCount++;
                    Debug.LogError($"[ValidateCityTrashAssets] FAIL [{i + 1:00}/24] {item.id}: {string.Join(", ", reasons)}");
                }
            }

            Debug.Log($"[ValidateCityTrashAssets] SUMMARY: {passCount} PASS, {failCount} FAIL (Total={db.items.Count}).");
            return failCount == 0 && passCount >= 24;
        }

        // =====================================================================
        // MENU ITEM 2: RENDER TRASH CONTACT SHEET (6x4 GRID)
        // =====================================================================

        [MenuItem("EcoQuest/Render Trash Contact Sheet")]
        public static void RenderTrashContactSheet()
        {
            CityTrashDatabase db = AssetDatabase.LoadAssetAtPath<CityTrashDatabase>(DatabasePath);
            if (db == null || db.items == null || db.items.Count == 0)
            {
                Debug.LogError("[RenderTrashContactSheet] Database missing; generating first.");
                db = GenerateAllTrashAssets();
            }

            int cols = 6;
            int rows = 4;
            int cellW = 280;
            int cellH = 300;
            int sheetW = cols * cellW;
            int sheetH = rows * cellH;

            Texture2D sheet = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false);
            Color bgDark = new Color(0.08f, 0.15f, 0.11f, 1f);
            Color cardBg = new Color(0.12f, 0.22f, 0.16f, 1f);
            Color borderCol = new Color(0.24f, 0.44f, 0.30f, 1f);

            // Fill sheet background
            Color[] allPixels = new Color[sheetW * sheetH];
            for (int i = 0; i < allPixels.Length; i++) allPixels[i] = bgDark;
            sheet.SetPixels(allPixels);

            for (int idx = 0; idx < Mathf.Min(24, db.items.Count); idx++)
            {
                var item = db.items[idx];
                int col = idx % cols;
                int row = (rows - 1) - (idx / cols); // Top-to-bottom

                int x0 = col * cellW;
                int y0 = row * cellH;

                // Draw card background & border
                FillRect(sheet, x0 + 6, y0 + 6, cellW - 12, cellH - 12, borderCol);
                FillRect(sheet, x0 + 8, y0 + 8, cellW - 16, cellH - 16, cardBg);

                // Category top header stripe
                Color catColor = CityTrashDatabase.GetCategoryColor(item.category);
                FillRect(sheet, x0 + 8, y0 + cellH - 36, cellW - 16, 28, catColor);

                // Render 220x220 3D preview into center of cell
                MeshFilter mf = item.worldPrefab != null ? item.worldPrefab.GetComponentInChildren<MeshFilter>(true) : null;
                MeshRenderer mr = item.worldPrefab != null ? item.worldPrefab.GetComponentInChildren<MeshRenderer>(true) : null;
                int tris = (mf != null && mf.sharedMesh != null) ? (mf.sharedMesh.triangles.Length / 3) : 0;

                if (mf != null && mf.sharedMesh != null && mr != null && mr.sharedMaterial != null)
                {
                    Texture2D preview = RenderMeshPreviewToTexture(mf.sharedMesh, mr.sharedMaterial, 220, 220, cardBg, true);
                    BlitAlpha(sheet, preview, x0 + (cellW - 220) / 2, y0 + 44);
                    UnityEngine.Object.DestroyImmediate(preview);
                }

                // Draw crisp bitmap text labels on tile: Category on top bar, Name & Tris at bottom
                string catLabel = CityTrashDatabase.GetCategoryDisplayName(item.category).ToUpperInvariant();
                DrawSimpleBitmapText(sheet, x0 + 16, y0 + cellH - 27, catLabel, Color.white, 2);

                string titleLabel = item.displayName.ToUpperInvariant();
                if (titleLabel.Length > 20) titleLabel = titleLabel.Substring(0, 20);
                DrawSimpleBitmapText(sheet, x0 + 14, y0 + 30, titleLabel, new Color(0.96f, 0.95f, 0.90f, 1f), 2);

                string triLabel = $"TRIS: {tris}" + (item.isRare ? " [RARE]" : "");
                DrawSimpleBitmapText(sheet, x0 + 14, y0 + 14, triLabel, new Color(0.65f, 0.92f, 0.72f, 1f), 2);
            }

            sheet.Apply();

            string dir = Path.GetDirectoryName(ContactSheetOutputPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(ContactSheetOutputPath, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);

            Debug.Log($"[RenderTrashContactSheet] Saved 6x4 contact sheet ({sheetW}x{sheetH}) to '{ContactSheetOutputPath}'.");
        }

        private static void FillRect(Texture2D tex, int x, int y, int w, int h, Color c)
        {
            int xMax = Mathf.Min(tex.width, x + w);
            int yMax = Mathf.Min(tex.height, y + h);
            for (int py = Mathf.Max(0, y); py < yMax; py++)
            {
                for (int px = Mathf.Max(0, x); px < xMax; px++)
                {
                    tex.SetPixel(px, py, c);
                }
            }
        }

        private static void BlitAlpha(Texture2D dst, Texture2D src, int dstX, int dstY)
        {
            for (int y = 0; y < src.height; y++)
            {
                int py = dstY + y;
                if (py < 0 || py >= dst.height) continue;
                for (int x = 0; x < src.width; x++)
                {
                    int px = dstX + x;
                    if (px < 0 || px >= dst.width) continue;
                    Color sc = src.GetPixel(x, y);
                    if (sc.a <= 0.01f) continue;
                    Color dc = dst.GetPixel(px, py);
                    dst.SetPixel(px, py, Color.Lerp(dc, new Color(sc.r, sc.g, sc.b, 1f), sc.a));
                }
            }
        }

        // Compact 3x5 pixel font for contact sheet labels
        private static readonly Dictionary<char, ushort> MiniFont = new Dictionary<char, ushort>
        {
            {'A', 0b010_101_111_101_101}, {'B', 0b110_101_110_101_110}, {'C', 0b011_100_100_100_011},
            {'D', 0b110_101_101_101_110}, {'E', 0b111_100_110_100_111}, {'F', 0b111_100_110_100_100},
            {'G', 0b011_100_101_101_011}, {'H', 0b101_101_111_101_101}, {'I', 0b111_010_010_010_111},
            {'J', 0b001_001_001_101_010}, {'K', 0b101_101_110_101_101}, {'L', 0b100_100_100_100_111},
            {'M', 0b101_111_101_101_101}, {'N', 0b111_101_101_101_101}, {'O', 0b010_101_101_101_010},
            {'P', 0b110_101_110_100_100}, {'Q', 0b010_101_101_011_001}, {'R', 0b110_101_110_101_101},
            {'S', 0b011_100_010_001_110}, {'T', 0b111_010_010_010_010}, {'U', 0b101_101_101_101_111},
            {'V', 0b101_101_101_101_010}, {'W', 0b101_101_101_111_101}, {'X', 0b101_101_010_101_101},
            {'Y', 0b101_101_010_010_010}, {'Z', 0b111_001_010_100_111},
            {'0', 0b111_101_101_101_111}, {'1', 0b010_110_010_010_111}, {'2', 0b111_001_111_100_111},
            {'3', 0b111_001_111_001_111}, {'4', 0b101_101_111_001_001}, {'5', 0b111_100_111_001_111},
            {'6', 0b111_100_111_101_111}, {'7', 0b111_001_001_010_010}, {'8', 0b111_101_111_101_111},
            {'9', 0b111_101_111_001_111}, {':', 0b000_010_000_010_000}, {'-', 0b000_000_111_000_000},
            {'[', 0b110_100_100_100_110}, {']', 0b011_001_001_001_011}, {'&', 0b010_101_010_101_011}
        };

        private static void DrawSimpleBitmapText(Texture2D tex, int startX, int startY, string text, Color col, int scale)
        {
            int curX = startX;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = char.ToUpperInvariant(text[i]);
                if (ch == ' ')
                {
                    curX += 4 * scale;
                    continue;
                }
                if (MiniFont.TryGetValue(ch, out ushort mask))
                {
                    for (int row = 0; row < 5; row++)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            int bit = 14 - (row * 3 + c);
                            if (((mask >> bit) & 1) != 0)
                            {
                                FillRect(tex, curX + c * scale, startY + (4 - row) * scale, scale, scale, col);
                            }
                        }
                    }
                }
                curX += 4 * scale;
            }
        }
    }
}
