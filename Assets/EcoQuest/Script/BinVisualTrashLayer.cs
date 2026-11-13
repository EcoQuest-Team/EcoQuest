using System.Collections;
using UnityEngine;

/// <summary>
/// Controls the visual trash pile inside a trash bin.
/// Shows the full dense pile as soon as at least 1 item is deposited into THIS bin.
/// Empty when count is 0, full when count >= 1.
/// Purely visual: no colliders, no pickup, no effect on quest counting.
/// Uses stable fitted transform parameters to ensure the pile never pokes outside bin walls.
/// </summary>
public class BinVisualTrashLayer : MonoBehaviour
{
    [Header("Visual Pile")]
    [Tooltip("The dense visual trash pile GameObject inside this bin.")]
    [SerializeField] private GameObject pileObject;

    [Header("Fitted Transform")]
    [Tooltip("Local position ensuring pile stays centered within inner cavity.")]
    [SerializeField] private Vector3 fittedLocalPosition = new Vector3(0.005f, 0.670f, 0.029f);
    [Tooltip("Local scale ensuring pile covers opening without poking through walls or rim.")]
    [SerializeField] private Vector3 fittedLocalScale = new Vector3(0.280f, 0.950f, 0.380f);

    private int currentDepositCount = 0;
    private Coroutine popCoroutine;
    private Vector3 baseScale = new Vector3(0.280f, 0.950f, 0.380f);

    public int CurrentDepositCount => currentDepositCount;
    public GameObject PileObject => pileObject;
    public Vector3 FittedLocalPosition => fittedLocalPosition;
    public Vector3 FittedLocalScale => fittedLocalScale;

    private void Awake()
    {
        ApplyFittedTransform();
        RestoreSavedState();
    }

    private void OnEnable()
    {
        ApplyFittedTransform();
        RestoreSavedState();
    }

    public void ApplyFittedTransform()
    {
        baseScale = fittedLocalScale;
        if (pileObject != null)
        {
            pileObject.transform.localPosition = fittedLocalPosition;
            if (pileObject.activeSelf)
            {
                pileObject.transform.localScale = fittedLocalScale;
            }
        }
    }

    /// <summary>
    /// Restores pile visibility from PlayerPrefs or quest completion.
    /// </summary>
    public void RestoreSavedState()
    {
        if (QuestManager.IsQuestComplete || PlayerPrefs.GetInt(EcoQuestSaveKeys.ForestTrashQuestDone, 0) == 1)
        {
            SetDepositCount(1, animatePop: false);
            return;
        }

        // Only restore mid-session deposit visuals when QuestManager also has matching progress loaded;
        // otherwise fresh scene loads start all 9 items at 0/9 so bins should start empty too.
        if (QuestManager.GetTotalSortedWaste() <= 0)
        {
            SetDepositCount(0, animatePop: false);
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
        ApplyFittedTransform();
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
                        pileObject.transform.localPosition = fittedLocalPosition;
                        pileObject.transform.localScale = baseScale;
                    }
                }
            }
            else
            {
                if (popCoroutine != null)
                {
                    StopCoroutine(popCoroutine);
                    popCoroutine = null;
                }
                pileObject.SetActive(false);
                pileObject.transform.localPosition = fittedLocalPosition;
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
            pileObject.transform.localPosition = fittedLocalPosition;
            pileObject.transform.localScale = baseScale;
        }
    }

    private IEnumerator AnimatePopRoutine(float delayBeforePop = 0.35f)
    {
        if (pileObject == null) yield break;

        pileObject.SetActive(false);
        pileObject.transform.localPosition = fittedLocalPosition;
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
            float overshoot = Mathf.Sin(t * Mathf.PI * 0.95f) * 1.12f;
            float s = (t >= 0.85f) ? Mathf.Lerp(overshoot, 1.0f, (t - 0.85f) / 0.15f) : overshoot;
            pileObject.transform.localScale = baseScale * Mathf.Max(0f, s);
            yield return null;
        }

        pileObject.transform.localScale = baseScale;
        popCoroutine = null;
    }

    public void Setup(GameObject pile, Vector3 pos, Vector3 scale)
    {
        pileObject = pile;
        fittedLocalPosition = pos;
        fittedLocalScale = scale;
        baseScale = scale;
        if (pileObject != null)
        {
            pileObject.transform.localPosition = fittedLocalPosition;
            if (pileObject.activeSelf) pileObject.transform.localScale = baseScale;
        }
    }
}
