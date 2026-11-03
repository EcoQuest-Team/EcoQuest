using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject database of kid-friendly loading screen tips.
/// Implements a persistent shuffle-bag randomization algorithm that:
/// - Guarantees every tip is seen before any tip repeats.
/// - Guarantees consecutive loads never show the same tip twice in a row.
/// - Filters tips optionally by scene (Forest, City, Any) or category.
/// </summary>
[CreateAssetMenu(fileName = "EcoQuestLoadingTips", menuName = "EcoQuest/Loading Tips Asset")]
public class LoadingTips : ScriptableObject
{
    public enum TipCategory
    {
        Gameplay,
        WasteSorting,
        EcoFacts5Rs
    }

    public enum SceneFilter
    {
        Any,
        Forest,
        City
    }

    [Serializable]
    public class TipEntry
    {
        [TextArea(2, 4)]
        public string text;
        public TipCategory category = TipCategory.Gameplay;
        public SceneFilter scene = SceneFilter.Any;

        public TipEntry() { }
        public TipEntry(string text, TipCategory category, SceneFilter scene = SceneFilter.Any)
        {
            this.text = text;
            this.category = category;
            this.scene = scene;
        }
    }

    [Tooltip("List of all loading tips.")]
    public List<TipEntry> tips = new List<TipEntry>();

    [NonSerialized] private List<int> currentBag = new List<int>();
    private const string LastTipTextPref = "EcoQuest_LastLoadingTipText";

    /// <summary>
    /// Gets the next random tip using a non-repeating shuffle bag.
    /// </summary>
    public string GetNextTip(SceneFilter currentScene = SceneFilter.Any)
    {
        if (tips == null || tips.Count == 0)
        {
            return "Put correct trash in the recycle bins to restore the forest and earn gold!";
        }

        List<int> matchingIndices = GetMatchingIndices(currentScene);
        if (matchingIndices.Count == 0)
        {
            // Fallback to all tips if no scene-specific tips found
            for (int i = 0; i < tips.Count; i++) matchingIndices.Add(i);
        }

        // Refill bag if empty or invalid
        if (currentBag == null || currentBag.Count == 0)
        {
            currentBag = new List<int>(matchingIndices);
            ShuffleList(currentBag);

            // Avoid repeating the last tip shown across runs/scene loads
            string lastShown = PlayerPrefs.GetString(LastTipTextPref, string.Empty);
            if (currentBag.Count > 1 && !string.IsNullOrEmpty(lastShown))
            {
                if (tips[currentBag[0]].text == lastShown)
                {
                    // Swap first element with a random subsequent element
                    int swapIdx = UnityEngine.Random.Range(1, currentBag.Count);
                    int temp = currentBag[0];
                    currentBag[0] = currentBag[swapIdx];
                    currentBag[swapIdx] = temp;
                }
            }
        }

        int selectedIndex = currentBag[0];
        currentBag.RemoveAt(0);

        string tipText = tips[selectedIndex].text;
        PlayerPrefs.SetString(LastTipTextPref, tipText);
        PlayerPrefs.Save();

        return tipText;
    }

    /// <summary>
    /// Resets the runtime shuffle bag (useful for testing or editor resets).
    /// </summary>
    public void ResetShuffleBag()
    {
        if (currentBag != null) currentBag.Clear();
    }

    private List<int> GetMatchingIndices(SceneFilter currentScene)
    {
        var list = new List<int>();
        for (int i = 0; i < tips.Count; i++)
        {
            if (tips[i] == null || string.IsNullOrWhiteSpace(tips[i].text)) continue;

            if (currentScene == SceneFilter.Any || tips[i].scene == SceneFilter.Any || tips[i].scene == currentScene)
            {
                list.Add(i);
            }
        }
        return list;
    }

    private static void ShuffleList(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int r = UnityEngine.Random.Range(0, i + 1);
            int tmp = list[i];
            list[i] = list[r];
            list[r] = tmp;
        }
    }
}
