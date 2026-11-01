using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized dialogue repository and token replacement system for EcoQuest.
/// Manages Forest Guardian, City Guardian, and litterbug dialogues with live tokens:
/// {total}, {count}, {done}, {reward}, {E}, {Q}.
/// Guarantees random tips and offers never repeat twice in a row.
/// </summary>
public static class DialogueData
{
    private static int lastForestTipIndex = -1;
    private static int lastCityTipIndex = -1;
    private static int lastCityOfferIndex = -1;

    #region Forest Guardian Dialogues

    public static readonly string[] ForestIntro = new string[]
    {
        "Welcome, traveler! I'm the Guardian of this forest, and I'm so glad you're here.",
        "Look around. This place used to be beautiful, but now it's covered in junk.",
        "Trash poisons the soil, dirties the water, and hurts every animal that lives here.",
        "Will you help me clean it up? There are {total} pieces to find."
    };

    public static readonly string[] ForestAccepted = new string[]
    {
        "Thank you! Your first Eco Quest begins now.",
        "Find all {total} pieces of waste and sort each one into the right bin.",
        "Green is for Biodegradable, blue for Recyclable, and black for Non-Biodegradable.",
        "Careful! A wrong bin costs you Gold, so think before you drop.",
        "Good luck, traveler. The forest is counting on you!"
    };

    public static readonly string[] ForestDeclined = new string[]
    {
        "No problem. Take your time, and come back when you're ready to help the forest."
    };

    public static readonly string ForestInProgressHeader = "How's it going? You've sorted {done} of {total} so far. Keep going!";

    public static readonly string[] ForestTips = new string[]
    {
        "Biodegradable waste, like fruit peels and leaves, rots naturally and can become compost.",
        "Recyclable items, like clean bottles and cans, can be turned into brand-new products.",
        "Non-biodegradable waste, like plastic wrappers, can stay in nature for hundreds of years.",
        "Not sure which bin? Think about what the item is made of.",
        "One dropped wrapper looks small, but millions of them become a huge problem.",
        "Made a mistake? Don't worry. Learn from it and try again."
    };

    public static readonly string[] ForestComplete = new string[]
    {
        "You did it! Every piece is sorted, and the forest can breathe again.",
        "Sorting waste correctly means less pollution, safer animals, and cleaner water.",
        "Here's {reward} Gold for your hard work.",
        "Remember: reduce, reuse, recycle, and always use the right bin.",
        "There's more to protect out there. Keep up the great work, traveler!"
    };

    #endregion

    #region City Guardian Dialogues

    public static readonly string[][] CityOffers = new string[][]
    {
        // v1
        new string[]
        {
            "Welcome to the city, traveler! It's busy here, but not everyone knows where their trash belongs.",
            "Some citizens toss litter right onto the street. Most of them simply don't know the harm it causes.",
            "Find them, explain what their trash does, and help them change their ways. Will you help?"
        },
        // v2
        new string[]
        {
            "Traveler! Perfect timing. Litter keeps piling up in our streets.",
            "Clogged drains, bad smells, and sick animals can all start with one thrown wrapper.",
            "Talk to {count} citizens and show them why it matters. Are you in?"
        },
        // v3
        new string[]
        {
            "Good to see you! The city is growing, and so is the litter.",
            "People often don't realize their habits hurt the neighborhood. A kind word can change a lot.",
            "Can you track down the litterbugs and explain where each item belongs?"
        }
    };

    public static readonly string[] CityAccepted = new string[]
    {
        "Thank you! Check your minimap. Every \"!\" marks a litterbug.",
        "Walk up to each one and press {E} to talk. Stay kind and explain the effect of their trash.",
        "Come back to me once you've reached all {count}."
    };

    public static readonly string[] CityDeclined = new string[]
    {
        "No worries. The litterbugs aren't going anywhere. Come back when you're ready."
    };

    public static readonly string CityInProgressHeader = "{done} of {count} litterbugs enlightened. Keep an eye on your minimap!";

    public static readonly string[] CityTips = new string[]
    {
        "Clogged drains cause flooding when it rains, so never toss litter near them.",
        "Hazardous waste, like batteries and spray cans, needs a special drop-off point.",
        "A kind explanation works better than a scolding. Stay friendly!",
        "E-waste, like old phones and chargers, can be recycled for valuable metals."
    };

    public static readonly string[] CityReadyToTurnIn = new string[]
    {
        "You found everyone! The city already feels cleaner.",
        "Every citizen you talked to will think twice before littering again.",
        "Here's {reward} Gold. You've earned it.",
        "Want another round? There are always more litterbugs out there."
    };

    #endregion

    #region Token Formatting & Random Selection

    /// <summary>
    /// Replaces standard tokens {total}, {count}, {done}, {reward}, {E}, {Q} in the string.
    /// </summary>
    public static string FormatTokens(string template, int total, int count, int done, int reward, string keyE = "E", string keyQ = "Q")
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        return template
            .Replace("{total}", total.ToString())
            .Replace("{count}", count.ToString())
            .Replace("{done}", done.ToString())
            .Replace("{reward}", reward.ToString())
            .Replace("{E}", keyE)
            .Replace("{Q}", keyQ);
    }

    public static List<string> FormatLines(IEnumerable<string> rawLines, int total, int count, int done, int reward, string keyE = "E", string keyQ = "Q")
    {
        List<string> result = new List<string>();
        foreach (var line in rawLines)
        {
            result.Add(FormatTokens(line, total, count, done, reward, keyE, keyQ));
        }
        return result;
    }

    /// <summary>
    /// Returns a random Forest tip that is never the same as the previous tip.
    /// </summary>
    public static string GetRandomForestTip()
    {
        if (ForestTips == null || ForestTips.Length == 0) return string.Empty;
        if (ForestTips.Length == 1) return ForestTips[0];

        int newIdx;
        do
        {
            newIdx = UnityEngine.Random.Range(0, ForestTips.Length);
        } while (newIdx == lastForestTipIndex);

        lastForestTipIndex = newIdx;
        return ForestTips[newIdx];
    }

    /// <summary>
    /// Returns a random City tip that is never the same as the previous tip.
    /// </summary>
    public static string GetRandomCityTip()
    {
        if (CityTips == null || CityTips.Length == 0) return string.Empty;
        if (CityTips.Length == 1) return CityTips[0];

        int newIdx;
        do
        {
            newIdx = UnityEngine.Random.Range(0, CityTips.Length);
        } while (newIdx == lastCityTipIndex);

        lastCityTipIndex = newIdx;
        return CityTips[newIdx];
    }

    /// <summary>
    /// Returns 1 of the 3 City Guardian offers at random, never the same twice in a row.
    /// </summary>
    public static string[] GetRandomCityOffer()
    {
        if (CityOffers == null || CityOffers.Length == 0) return Array.Empty<string>();
        if (CityOffers.Length == 1) return CityOffers[0];

        int newIdx;
        do
        {
            newIdx = UnityEngine.Random.Range(0, CityOffers.Length);
        } while (newIdx == lastCityOfferIndex);

        lastCityOfferIndex = newIdx;
        return CityOffers[newIdx];
    }

    #endregion

    #region Forest Dialogue Builders

    public static List<string> GetForestIntro(int total, string keyE = "E", string keyQ = "Q")
    {
        return FormatLines(ForestIntro, total: total, count: 0, done: 0, reward: 0, keyE: keyE, keyQ: keyQ);
    }

    public static List<string> GetForestAccepted(int total, string keyE = "E", string keyQ = "Q")
    {
        return FormatLines(ForestAccepted, total: total, count: 0, done: 0, reward: 0, keyE: keyE, keyQ: keyQ);
    }

    public static List<string> GetForestDeclined()
    {
        return new List<string>(ForestDeclined);
    }

    public static List<string> GetForestInProgress(int done, int total)
    {
        string header = FormatTokens(ForestInProgressHeader, total: total, count: 0, done: done, reward: 0);
        string tip = GetRandomForestTip();
        return new List<string> { header, tip };
    }

    public static List<string> GetForestComplete(int reward)
    {
        return FormatLines(ForestComplete, total: 0, count: 0, done: 0, reward: reward);
    }

    #endregion

    #region City Dialogue Builders

    public static List<string> GetCityOffer(int count, string keyE = "E", string keyQ = "Q")
    {
        string[] selectedOffer = GetRandomCityOffer();
        return FormatLines(selectedOffer, total: 0, count: count, done: 0, reward: 0, keyE: keyE, keyQ: keyQ);
    }

    public static List<string> GetCityAccepted(int count, string keyE = "E", string keyQ = "Q")
    {
        return FormatLines(CityAccepted, total: 0, count: count, done: 0, reward: 0, keyE: keyE, keyQ: keyQ);
    }

    public static List<string> GetCityDeclined()
    {
        return new List<string>(CityDeclined);
    }

    public static List<string> GetCityInProgress(int done, int count)
    {
        string header = FormatTokens(CityInProgressHeader, total: 0, count: count, done: done, reward: 0);
        string tip = GetRandomCityTip();
        return new List<string> { header, tip };
    }

    public static List<string> GetCityTurnIn(int reward)
    {
        return FormatLines(CityReadyToTurnIn, total: 0, count: 0, done: 0, reward: reward);
    }

    #endregion
}
