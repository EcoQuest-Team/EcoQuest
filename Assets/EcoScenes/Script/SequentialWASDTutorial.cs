using System.Collections;
using UnityEngine;
using TMPro;

public class SequentialWASDTutorial : MonoBehaviour
{
    public enum TutorialState { None, OnlyW, OnlyA, OnlyS, OnlyD, OnlySpace, All }
    public static TutorialState AllowedState { get; private set; } = TutorialState.None;

    [Header("Title Intro Setup")]
    [Tooltip("The Canvas Group for your Level/Game Title (e.g., 'The Forest')")]
    public CanvasGroup titleGroup;
    public float titleHoldDuration = 2.0f;

    [Header("UI References")]
    public GameObject mainTutorialParent;
    public CanvasGroup wKeyGroup;
    public CanvasGroup aKeyGroup;
    public CanvasGroup sKeyGroup;
    public CanvasGroup dKeyGroup;
    public CanvasGroup spaceKeyGroup;

    [Header("Text Notification Setup")]
    public TMP_Text tutorialText;
    public CanvasGroup textCanvasGroup;
    public Color defaultTextColor = Color.white;
    public Color successTextColor = Color.green;

    [Header("NPC Integration")]
    public GameObject npcExclamation;

    [Header("Fade Settings")]
    public float fadeDuration = 0.5f;
    public float initialHoldTime = 1.5f;

    [Header("Hold Settings")]
    public float requiredHoldDuration = 1.5f; 

    void Start()
    {

        SetAllAlphas(0f);
        if (textCanvasGroup != null) textCanvasGroup.alpha = 0f;
        if (titleGroup != null) titleGroup.alpha = 0f;
        if (npcExclamation != null) npcExclamation.SetActive(false);

        AllowedState = TutorialState.None; 
        
        StartCoroutine(RunTutorialSequence());
    }

    IEnumerator RunTutorialSequence()
    {
        if (titleGroup != null)
        {
            yield return StartCoroutine(FadeSingleTo(titleGroup, 1f));
            yield return new WaitForSeconds(titleHoldDuration);
            yield return StartCoroutine(FadeSingleTo(titleGroup, 0f));
            yield return new WaitForSeconds(0.5f); // Short breath before tutorial starts
        }

        if (tutorialText != null) tutorialText.text = ""; 
        yield return StartCoroutine(FadeAllTo(1f));
        yield return new WaitForSeconds(initialHoldTime);
        yield return StartCoroutine(FadeAllTo(0f));
        yield return new WaitForSeconds(0.5f); 

        AllowedState = TutorialState.OnlyW;
        yield return StartCoroutine(RequireKeyHoldPrompt(KeyCode.W, wKeyGroup, "Press W to move forward"));

        AllowedState = TutorialState.OnlyA;
        yield return StartCoroutine(RequireKeyHoldPrompt(KeyCode.A, aKeyGroup, "Press A to move to the left"));

        AllowedState = TutorialState.OnlyS;
        yield return StartCoroutine(RequireKeyHoldPrompt(KeyCode.S, sKeyGroup, "Press S to move backward"));

        AllowedState = TutorialState.OnlyD;
        yield return StartCoroutine(RequireKeyHoldPrompt(KeyCode.D, dKeyGroup, "Press D to move to the right"));

        AllowedState = TutorialState.OnlySpace;
        yield return StartCoroutine(RequireKeyHoldPrompt(KeyCode.Space, spaceKeyGroup, "Press SPACE to jump"));

        AllowedState = TutorialState.All;

        // Turn off WASD keys
        if (wKeyGroup != null) wKeyGroup.gameObject.SetActive(false);
        if (aKeyGroup != null) aKeyGroup.gameObject.SetActive(false);
        if (sKeyGroup != null) sKeyGroup.gameObject.SetActive(false);
        if (dKeyGroup != null) dKeyGroup.gameObject.SetActive(false);
        if (spaceKeyGroup != null) spaceKeyGroup.gameObject.SetActive(false);

        if (npcExclamation != null) npcExclamation.SetActive(true);

        if (tutorialText != null)
        {
            tutorialText.text = "Seek out the (!) and talk to the NPC";
            tutorialText.color = defaultTextColor;
            if (textCanvasGroup != null)
            {
                yield return StartCoroutine(FadeSingleTo(textCanvasGroup, 1f));
            }
        }
    }

    IEnumerator RequireKeyHoldPrompt(KeyCode requiredKey, CanvasGroup keyCg, string instruction)
    {
        if (keyCg == null) yield break;

        if (tutorialText != null)
        {
            tutorialText.text = instruction;
            tutorialText.color = defaultTextColor;
        }

        StartCoroutine(FadeSingleTo(keyCg, 1f));
        if (textCanvasGroup != null) StartCoroutine(FadeSingleTo(textCanvasGroup, 1f));
        yield return new WaitForSeconds(fadeDuration);

        float holdTimer = 0f;
        while (holdTimer < requiredHoldDuration)
        {
            if (Input.GetKey(requiredKey))
            {
                holdTimer += Time.deltaTime;
            }
            else
            {
                holdTimer = 0f;
            }
            yield return null;
        }

        if (tutorialText != null) tutorialText.color = successTextColor;
        yield return new WaitForSeconds(0.5f);

        StartCoroutine(FadeSingleTo(keyCg, 0f));
        if (textCanvasGroup != null) StartCoroutine(FadeSingleTo(textCanvasGroup, 0f));
        yield return new WaitForSeconds(fadeDuration);
    }

    IEnumerator FadeAllTo(float targetAlpha)
    {
        float startW = wKeyGroup != null ? wKeyGroup.alpha : 0f;
        float startA = aKeyGroup != null ? aKeyGroup.alpha : 0f;
        float startS = sKeyGroup != null ? sKeyGroup.alpha : 0f;
        float startD = dKeyGroup != null ? dKeyGroup.alpha : 0f;
        float startSpace = spaceKeyGroup != null ? spaceKeyGroup.alpha : 0f;
        float timer = 0f;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / fadeDuration;
            if (wKeyGroup != null) wKeyGroup.alpha = Mathf.Lerp(startW, targetAlpha, progress);
            if (aKeyGroup != null) aKeyGroup.alpha = Mathf.Lerp(startA, targetAlpha, progress);
            if (sKeyGroup != null) sKeyGroup.alpha = Mathf.Lerp(startS, targetAlpha, progress);
            if (dKeyGroup != null) dKeyGroup.alpha = Mathf.Lerp(startD, targetAlpha, progress);
            if (spaceKeyGroup != null) spaceKeyGroup.alpha = Mathf.Lerp(startSpace, targetAlpha, progress);
            yield return null;
        }
        SetAllAlphas(targetAlpha);
    }

    IEnumerator FadeSingleTo(CanvasGroup cg, float targetAlpha)
    {
        float startAlpha = cg.alpha;
        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            cg.alpha = Mathf.Lerp(startAlpha, targetAlpha, timer / fadeDuration);
            yield return null;
        }
        cg.alpha = targetAlpha;
    }

    void SetAllAlphas(float alpha)
    {
        if (wKeyGroup != null) wKeyGroup.alpha = alpha;
        if (aKeyGroup != null) aKeyGroup.alpha = alpha;
        if (sKeyGroup != null) sKeyGroup.alpha = alpha;
        if (dKeyGroup != null) dKeyGroup.alpha = alpha;
        if (spaceKeyGroup != null) spaceKeyGroup.alpha = alpha;
    }
}