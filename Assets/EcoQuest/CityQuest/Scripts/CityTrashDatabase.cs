using System;
using System.Collections.Generic;
using UnityEngine;

namespace EcoQuest.CityQuest
{
    public enum CityDistrict
    {
        Park = 0,
        MainStreet = 1,
        Market = 2,
        Riverside = 3,
        Residential = 4
    }

    [Serializable]
    public struct DistrictWeight
    {
        public CityDistrict district;
        [Range(0f, 10f)] public float weight;

        public DistrictWeight(CityDistrict d, float w)
        {
            district = d;
            weight = w;
        }
    }

    [Serializable]
    public class CityTrashItemDef
    {
        [Tooltip("Unique identifier (e.g., BananaPeel)")]
        public string id;

        [Tooltip("Human-readable display name")]
        public string displayName;

        [Tooltip("EcoQuest 4-way waste category")]
        public LitterCategory category;

        [Tooltip("256x256 crisp icon rendered from the 3D prefab")]
        public Sprite icon;

        [Tooltip("World pickup prefab with 2-3 visual variants")]
        public GameObject worldPrefab;

        [Tooltip("Spawn weight per City district")]
        public List<DistrictWeight> districtWeights = new List<DistrictWeight>();

        [Tooltip("Kid-friendly educational fact (max 90 chars)")]
        [TextArea(1, 2)]
        public string funFact;

        [Tooltip("Short 1-sentence explanation shown in the Bonus Quiz")]
        public string quizExplanation;

        [Tooltip("If true, eligible for the 1-2 rare/hazardous slots on the checklist")]
        public bool isRare;

        public float GetWeightForDistrict(CityDistrict d)
        {
            if (districtWeights == null) return 1f;
            for (int i = 0; i < districtWeights.Count; i++)
            {
                if (districtWeights[i].district == d)
                    return districtWeights[i].weight;
            }
            return 0.1f;
        }

        public CityDistrict GetPrimaryDistrict()
        {
            if (districtWeights == null || districtWeights.Count == 0)
                return CityDistrict.Park;

            CityDistrict best = districtWeights[0].district;
            float bestW = districtWeights[0].weight;
            for (int i = 1; i < districtWeights.Count; i++)
            {
                if (districtWeights[i].weight > bestW)
                {
                    bestW = districtWeights[i].weight;
                    best = districtWeights[i].district;
                }
            }
            return best;
        }
    }

    [CreateAssetMenu(fileName = "CityTrashDatabase", menuName = "EcoQuest/City Trash Database")]
    public class CityTrashDatabase : ScriptableObject
    {
        public List<CityTrashItemDef> items = new List<CityTrashItemDef>();

        public CityTrashItemDef GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && string.Equals(items[i].id, id, StringComparison.OrdinalIgnoreCase))
                    return items[i];
            }
            return null;
        }

        public static string GetCategoryDisplayName(LitterCategory category)
        {
            switch (category)
            {
                case LitterCategory.Biodegradable: return "Biodegradable";
                case LitterCategory.Recyclable: return "Recyclable";
                case LitterCategory.NonBiodegradable: return "Non-Biodegradable";
                case LitterCategory.Hazardous: return "Hazardous";
                default: return category.ToString();
            }
        }

        public static Color GetCategoryColor(LitterCategory category)
        {
            switch (category)
            {
                case LitterCategory.Biodegradable: return new Color(0.24f, 0.72f, 0.32f, 1f);     // Green
                case LitterCategory.Recyclable: return new Color(0.20f, 0.58f, 0.90f, 1f);        // Blue
                case LitterCategory.NonBiodegradable: return new Color(0.706f, 0.325f, 0.035f, 1f); // Deep Amber #B45309 (WCAG AA >= 4.5:1 with white text)
                case LitterCategory.Hazardous: return new Color(0.88f, 0.24f, 0.24f, 1f);         // Red
                default: return Color.white;
            }
        }

        public static string GetDistrictDisplayName(CityDistrict district)
        {
            switch (district)
            {
                case CityDistrict.Park: return "Park";
                case CityDistrict.MainStreet: return "Main Street";
                case CityDistrict.Market: return "Market";
                case CityDistrict.Riverside: return "Riverside";
                case CityDistrict.Residential: return "Residential";
                default: return district.ToString();
            }
        }
    }
}
