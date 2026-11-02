using System.Collections;
using UnityEngine;

/// <summary>
/// Controls the visual trash pile inside a trash bin.
/// Shows the full dense pile as soon as at least 1 item is deposited into THIS bin.
/// Empty when count is 0, full when count >= 1.
/// Purely visual: no colliders, no pickup, no effect on quest counting.
/// </summary>
public class BinVisualTrashLayer : MonoBehaviour
{
    [Header("Visual Pile")]
    [Tooltip("The dense visual trash pile GameObject inside this bin.")]
    [SerializeField] private GameObject pileObject;

    private int currentDepositCount = 0;
    private Coroutine popCoroutine;
    private Vector3 baseScale = Vector3.one;

    public int CurrentDepositCount => currentDepositCount;
    public GameObject PileObject => pileObject;

    private void Awake()
    {
        if (pileObject != null)
        {
            baseScale = pileObject.transform.localScale;
        }
        RestoreSavedState();
    }

    private void OnEnable()
    {
        RestoreSavedState();
    }

    /// <summary>
    /// Restores pile visibility from PlayerPrefs or quest completion.
    /// </summary>
    public void RestoreSavedState()
    {
        if (QuestManager.IsQuestComplete)
        {
            SetDepositCount(1, animatePop: false);
            return;
        }

        string saveKey = $"EcoQuest_Bin_{gameObject.name}_Deposits";
        int saved = PlayerPrefs.GetInt(saveKey, 0);
        SetDepositCount(saved, animatePop: false);
    }

    /// <summary>
    /// Updates the visual layer based on items deposited into THIS specific bin.
    /// Count 0 = hidden. Count >= 1 = dense pile shown.
    /// </summary>
    public void SetDepositCount(int count, bool animatePop = true)
    {
        currentDepositCount = Mathf.Max(0, count);

        if (Application.isPlaying)
        {
            PlayerPrefs.SetInt($"EcoQuest_Bin_{gameObject.name}_Deposits", currentDepositCount);
        }

        bool shouldShow = currentDepositCount > 0;

        if (pileObject != null)
        {
            bool wasActive = pileObject.activeSelf;

            if (shouldShow)
            {
                if (!wasActive)
                {
                    if (animatePop && gameObject.activeInHierarchy)
                    {
                        if (popCoroutine != null) StopCoroutine(popCoroutine);
                        popCoroutine = StartCoroutine(AnimatePopRoutine(0.35f));
                    }
                    else
                    {
                        pileObject.SetActive(true);
                        pileObject.transform.localScale = baseScale;
                    }
                }
                // If already active, it stays full with no redundant animation
            }
            else
            {
                if (popCoroutine != null)
                {
                    StopCoroutine(popCoroutine);
                    popCoroutine = null;
                }
                pileObject.SetActive(false);
                pileObject.transform.localScale = baseScale;
            }
        }
    }

    /// <summary>
    /// Resets the visual pile to hidden.
    /// </summary>
    public void ResetVisuals()
    {
        currentDepositCount = 0;
        PlayerPrefs.DeleteKey($"EcoQuest_Bin_{gameObject.name}_Deposits");
        if (popCoroutine != null)
        {
            StopCoroutine(popCoroutine);
            popCoroutine = null;
        }
        if (pileObject != null)
        {
            pileObject.SetActive(false);
            pileObject.transform.localScale = baseScale;
        }
    }

    private IEnumerator AnimatePopRoutine(float delayBeforePop = 0.35f)
    {
        if (pileObject == null) yield break;

        // Keep pile inactive while the thrown item is in flight
        pileObject.SetActive(false);
        pileObject.transform.localScale = Vector3.zero;

        if (delayBeforePop > 0f)
        {
            float delayElapsed = 0f;
            while (delayElapsed < delayBeforePop)
            {
                delayElapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        pileObject.SetActive(true);

        float duration = 0.20f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Elastic pop: overshoot to 1.12 then settle to 1.0
            float overshoot = Mathf.Sin(t * Mathf.PI * 0.95f) * 1.12f;
            float s = (t >= 0.85f) ? Mathf.Lerp(overshoot, 1.0f, (t - 0.85f) / 0.15f) : overshoot;
            pileObject.transform.localScale = baseScale * Mathf.Max(0f, s);
            yield return null;
        }

        pileObject.transform.localScale = baseScale;
        popCoroutine = null;
    }

    public void Setup(GameObject pile, Vector3 scale)
    {
        pileObject = pile;
        baseScale = scale;
    }
}
