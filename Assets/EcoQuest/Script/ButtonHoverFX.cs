using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonHoverFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Scale Feedback")]
    [Tooltip("Target scale multiplier on hover (e.g. 1.05)")]
    public float hoverScale = 1.05f;
    [Tooltip("Quick squish multiplier on click (X, Y)")]
    public Vector2 clickSquishScale = new Vector2(1.06f, 0.92f);
    [Tooltip("How long the click squish lasts in seconds")]
    public float squishDuration = 0.12f;
    [Tooltip("Lerp smoothing speed")]
    public float smoothSpeed = 14f;

    [Header("Sound Hook (Optional)")]
    public AudioSource audioSource;
    public AudioClip hoverSound;
    public AudioClip clickSound;

    [Header("Toggle Parent Reference")]
    [Tooltip("If assigned and inactive, button feedback pauses so MenuFX toggle controls all effects")]
    public GameObject menuFXRoot;

    private Vector3 initialScale;
    private bool captured;
    private bool isHovered;
    private float squishTimer;

    private void Awake()
    {
        CaptureScale();
    }

    private void OnEnable()
    {
        CaptureScale();
    }

    private void CaptureScale()
    {
        if (!captured)
        {
            initialScale = transform.localScale;
            captured = true;
        }
    }

    private bool IsEffectActive()
    {
        return menuFXRoot == null || menuFXRoot.activeInHierarchy;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!IsEffectActive()) return;
        isHovered = true;
        if (audioSource != null && hoverSound != null)
        {
            audioSource.PlayOneShot(hoverSound);
        }
        else if (AudioManager.Instance != null)
        {
            if (hoverSound != null) AudioManager.Instance.PlaySFX(hoverSound);
            else AudioManager.Instance.PlayButtonHover();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!IsEffectActive()) return;
        squishTimer = squishDuration;
        if (audioSource != null && (clickSound != null || audioSource.clip != null))
        {
            if (clickSound != null)
                audioSource.PlayOneShot(clickSound);
            else if (audioSource.clip != null)
                audioSource.Play();
        }
        else if (AudioManager.Instance != null)
        {
            if (clickSound != null) AudioManager.Instance.PlaySFX(clickSound);
            else AudioManager.Instance.PlayButtonClick();
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        // Squish timer naturally decays back to hover or idle scale
    }

    private void Update()
    {
        if (!captured) CaptureScale();

        if (!IsEffectActive())
        {
            isHovered = false;
            squishTimer = 0f;
            transform.localScale = initialScale;
            return;
        }

        float dt = Time.unscaledDeltaTime;
        Vector3 targetScale = initialScale;

        if (squishTimer > 0f)
        {
            squishTimer -= dt;
            targetScale = new Vector3(
                initialScale.x * clickSquishScale.x,
                initialScale.y * clickSquishScale.y,
                initialScale.z
            );
        }
        else if (isHovered)
        {
            targetScale = initialScale * hoverScale;
        }

        float speed = squishTimer > 0f ? smoothSpeed * 2.2f : smoothSpeed;
        float t = 1f - Mathf.Exp(-Mathf.Max(1f, speed) * dt);
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, t);
    }

    private void OnDisable()
    {
        isHovered = false;
        squishTimer = 0f;
        if (captured)
        {
            transform.localScale = initialScale;
        }
    }
}
