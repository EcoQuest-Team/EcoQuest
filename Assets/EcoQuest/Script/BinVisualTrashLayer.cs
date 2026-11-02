using System.Collections;
using UnityEngine;

/// <summary>
/// Manages the visual trash pile layer inside a trash bin.
/// Shows staged piles (low, medium, full) based on items deposited into THIS specific bin.
/// </summary>
public class BinVisualTrashLayer : MonoBehaviour
{
    [Header("Visual Stages")]
    [Tooltip("Staged visual piles inside the bin: Stage 1 (small), Stage 2 (medium), Stage 3 (full).")]
    [SerializeField] private GameObject[] stageObjects;

    private int currentDepositCount = 0;
    public int CurrentDepositCount => currentDepositCount;
    public GameObject[] StageObjects => stageObjects;

    public void SetStageObjects(GameObject[] stages)
    {
        stageObjects = stages;
    }

    private void Awake()
    {
        UpdateVisuals(false);
    }

    /// <summary>
    /// Updates the visual layer based on the number of items deposited in THIS specific bin.
    /// </summary>
    public void SetDepositCount(int count, bool animatePop = true)
    {
        currentDepositCount = Mathf.Max(0, count);
        UpdateVisuals(animatePop);
    }

    /// <summary>
    /// Resets all visual stages to hidden (stage 0).
    /// </summary>
    public void ResetVisuals()
    {
        currentDepositCount = 0;
        UpdateVisuals(false);
    }

    private void UpdateVisuals(bool animatePop)
    {
        if (stageObjects == null || stageObjects.Length == 0) return;

        // Map deposit count to stage index:
        // 0: none
        // 1: Stage 0 (small)
        // 2: Stage 1 (medium)
        // >= 3: Stage 2 (full)
        int activeStageIndex = -1;
        if (currentDepositCount == 1) activeStageIndex = 0;
        else if (currentDepositCount == 2) activeStageIndex = Mathf.Min(1, stageObjects.Length - 1);
        else if (currentDepositCount >= 3) activeStageIndex = Mathf.Min(2, stageObjects.Length - 1);

        for (int i = 0; i < stageObjects.Length; i++)
        {
            if (stageObjects[i] == null) continue;

            bool shouldBeActive = (i == activeStageIndex);
            if (stageObjects[i].activeSelf != shouldBeActive)
            {
                stageObjects[i].SetActive(shouldBeActive);
                if (shouldBeActive && animatePop && gameObject.activeInHierarchy)
                {
                    StartCoroutine(PopAnimation(stageObjects[i].transform));
                }
            }
        }
    }

    private IEnumerator PopAnimation(Transform target)
    {
        if (target == null) yield break;
        Vector3 baseScale = target.localScale;
        float elapsed = 0f;
        float duration = 0.25f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float scaleMultiplier = Mathf.Sin(t * Mathf.PI * 0.5f);
            target.localScale = baseScale * (0.85f + 0.15f * scaleMultiplier);
            yield return null;
        }

        target.localScale = baseScale;
    }
}
