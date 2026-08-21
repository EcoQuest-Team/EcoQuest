using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class TitleFade : MonoBehaviour
{
    [Header("UI References")]
    public Image titleImage;

    [Header("Fade Settings")]
    public float fadeInDuration = 2.0f;
    public float displayDuration = 2.0f;
    public float fadeOutDuration = 2.0f;

    void Start()
    {
        if (titleImage != null)
        {
            Color startColor = titleImage.color;
            startColor.a = 0f;
            titleImage.color = startColor;

            StartCoroutine(FadeSequence());
        }
        else
        {
            Debug.LogWarning("Title Image is not assigned in the Inspector!");
        }
    }

    IEnumerator FadeSequence()
    {
        yield return StartCoroutine(FadeToAlpha(1f, fadeInDuration));

        yield return new WaitForSeconds(displayDuration);

        yield return StartCoroutine(FadeToAlpha(0f, fadeOutDuration));
    }

    IEnumerator FadeToAlpha(float targetAlpha, float duration)
    {
        Color color = titleImage.color;
        float startAlpha = color.a;
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            color.a = Mathf.Lerp(startAlpha, targetAlpha, timer / duration);
            titleImage.color = color;
            yield return null;
        }

        color.a = targetAlpha;
        titleImage.color = color;
    }
}