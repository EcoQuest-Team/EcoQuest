using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls the scattered low-poly visual trash pile inside a trash bin.
/// Pre-spawns a pool of individual litter pieces tailored to the bin's category:
/// - Biodegradable: greens, browns, yellows (banana peels, fish bones, sandwiches, cupcakes, fruits)
/// - Recyclable: blues, whites, cardboard (plastic cups, papers, toilet rolls, clean tin cans)
/// - Non-Biodegradable: grays, reds, darks (tin cans, plastic packaging, soiled wrappers)
/// Fills the entire circular opening (~30% at 1st deposit, growing to nearly full just below the rim by quota).
/// </summary>
public class BinVisualTrashLayer : MonoBehaviour
{
    public enum BinCategory
    {
        Biodegradable,
        Recyclable,
        NonBiodegradable
    }

    [Header("Bin Configuration")]
    [SerializeField] private BinCategory category = BinCategory.Biodegradable;
    [SerializeField] private int quota = 5;
    [SerializeField] private int poolSize = 60;

    [Header("Pool References")]
    [SerializeField] private Transform pileRoot;
    [SerializeField] private List<GameObject> pooledPieces = new List<GameObject>();
    [SerializeField] private List<Vector3> baseScales = new List<Vector3>();

    private int currentDepositCount = 0;
    private float currentFillPercent = 0f;
    private Coroutine popCoroutine;

    public int CurrentDepositCount => currentDepositCount;
    public float CurrentFillPercent => currentFillPercent;
    public int Quota => quota;
    public BinCategory Category => category;
    public Transform PileRoot => pileRoot;
    public IReadOnlyList<GameObject> PooledPieces => pooledPieces;

    public int ActivePieceCount
    {
        get
        {
            int c = 0;
            for (int i = 0; i < pooledPieces.Count; i++)
            {
                if (pooledPieces[i] != null && pooledPieces[i].activeSelf) c++;
            }
            return c;
        }
    }

    private void Awake()
    {
        DetectCategoryAndQuota();
        RestoreSavedState();
    }

    private void OnEnable()
    {
        DetectCategoryAndQuota();
        RestoreSavedState();
    }

    public void DetectCategoryAndQuota()
    {
        string n = gameObject.name.ToLower();
        var qm = GetComponent<QuestManager>();
        string tag = qm != null ? qm.AcceptedTag : "";
        if (string.IsNullOrEmpty(tag)) tag = n;
        else tag = tag.ToLower();

        if (tag.Contains("bio") && !tag.Contains("non")) category = BinCategory.Biodegradable;
        else if (tag.Contains("recyc")) category = BinCategory.Recyclable;
        else if (tag.Contains("non")) category = BinCategory.NonBiodegradable;

        if (qm != null)
        {
            int qmQuota = QuestManager.GetCategoryTarget(qm.AcceptedTag);
            if (qmQuota <= 0)
            {
                if (category == BinCategory.Biodegradable) qmQuota = 5;
                else if (category == BinCategory.Recyclable) qmQuota = 3;
                else qmQuota = 1;
            }
            quota = qmQuota;
        }
    }

    public void RestoreSavedState()
    {
        if (!Application.isPlaying) return;

        if (QuestManager.IsQuestComplete)
        {
            SetDepositCount(quota, false);
            return;
        }

        string saveKey = $"EcoQuest_Bin_{gameObject.name}_Deposits";
        int saved = PlayerPrefs.GetInt(saveKey, -1);
        if (saved >= 0)
        {
            currentDepositCount = saved;
            SetDepositCount(saved, false);
        }
        else
        {
            ResetVisuals();
        }
    }

    /// <summary>
    /// Sets deposit count for this bin.
    /// About 30% full on 1st deposit, growing to nearly full (just below the rim) by quota or 6 deposits.
    /// </summary>
    public void SetDepositCount(int count, bool animatePop = true)
    {
        currentDepositCount = Mathf.Max(0, count);
        if (Application.isPlaying)
        {
            PlayerPrefs.SetInt($"EcoQuest_Bin_{gameObject.name}_Deposits", currentDepositCount);
        }

        float fill = 0f;
        if (currentDepositCount > 0)
        {
            int targetSteps = Mathf.Clamp(quota, 1, 6);
            if (targetSteps <= 1)
            {
                fill = 1.0f;
            }
            else
            {
                float t = Mathf.Clamp01((float)(currentDepositCount - 1) / (targetSteps - 1));
                fill = Mathf.Lerp(0.30f, 1.0f, t);
            }
            if (currentDepositCount >= targetSteps)
            {
                fill = 1.0f;
            }
        }

        SetFillPercent(fill, animatePop);
    }

    /// <summary>
    /// Sets exact fill percentage (0.0f to 1.0f) across the pool.
    /// Used by preview slider and deposit scaling.
    /// </summary>
    public void SetFillPercent(float fill, bool animatePop = false)
    {
        currentFillPercent = Mathf.Clamp01(fill);
        if (pooledPieces == null || pooledPieces.Count == 0) return;

        int targetActiveCount = Mathf.RoundToInt(currentFillPercent * pooledPieces.Count);
        int prevActiveCount = ActivePieceCount;

        List<Transform> newlyActivated = new List<Transform>();

        for (int i = 0; i < pooledPieces.Count; i++)
        {
            var piece = pooledPieces[i];
            if (piece == null) continue;

            bool shouldBeActive = i < targetActiveCount;
            if (shouldBeActive && !piece.activeSelf)
            {
                piece.SetActive(true);
                if (animatePop && i >= prevActiveCount)
                {
                    newlyActivated.Add(piece.transform);
                }
                else if (i < baseScales.Count)
                {
                    piece.transform.localScale = baseScales[i];
                }
            }
            else if (!shouldBeActive && piece.activeSelf)
            {
                piece.SetActive(false);
            }
        }

        if (animatePop && newlyActivated.Count > 0 && gameObject.activeInHierarchy)
        {
            if (popCoroutine != null) StopCoroutine(popCoroutine);
            popCoroutine = StartCoroutine(AnimatePopRoutine(newlyActivated));
        }
    }

    public void ResetVisuals()
    {
        currentDepositCount = 0;
        currentFillPercent = 0f;
        if (Application.isPlaying)
        {
            PlayerPrefs.DeleteKey($"EcoQuest_Bin_{gameObject.name}_Deposits");
        }
        if (pooledPieces != null)
        {
            for (int i = 0; i < pooledPieces.Count; i++)
            {
                if (pooledPieces[i] != null)
                {
                    pooledPieces[i].SetActive(false);
                    if (i < baseScales.Count)
                    {
                        pooledPieces[i].transform.localScale = baseScales[i];
                    }
                }
            }
        }
    }

    private IEnumerator AnimatePopRoutine(List<Transform> pieces)
    {
        float duration = 0.20f;
        float elapsed = 0f;

        // Initialize newly active pieces at scale 0
        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i] != null) pieces[i].localScale = Vector3.zero;
        }

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Elastic pop: overshoot to 1.15 then settle to 1.0
            float overshoot = Mathf.Sin(t * Mathf.PI * 0.95f) * 1.15f;
            float s = (t >= 0.85f) ? Mathf.Lerp(overshoot, 1.0f, (t - 0.85f) / 0.15f) : overshoot;

            for (int i = 0; i < pieces.Count; i++)
            {
                var tr = pieces[i];
                if (tr == null) continue;
                int idx = pooledPieces.IndexOf(tr.gameObject);
                Vector3 baseScale = (idx >= 0 && idx < baseScales.Count) ? baseScales[idx] : Vector3.one * 0.18f;
                tr.localScale = baseScale * Mathf.Max(0f, s);
            }

            yield return null;
        }

        for (int i = 0; i < pieces.Count; i++)
        {
            var tr = pieces[i];
            if (tr == null) continue;
            int idx = pooledPieces.IndexOf(tr.gameObject);
            Vector3 baseScale = (idx >= 0 && idx < baseScales.Count) ? baseScales[idx] : Vector3.one * 0.18f;
            tr.localScale = baseScale;
        }

        popCoroutine = null;
    }

    public void SetupPool(Transform root, List<GameObject> pieces, List<Vector3> scales, BinCategory cat, int binQuota)
    {
        pileRoot = root;
        pooledPieces = pieces;
        baseScales = scales;
        category = cat;
        quota = binQuota;
        poolSize = pieces != null ? pieces.Count : 60;
    }
}
