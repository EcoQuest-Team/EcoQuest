using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Controls the cinematic presentation for interactions with Explorer_Var1:
/// - Smooth Cinemachine blend (priority 35) with SphereCast geometry obstacle avoidance
/// - Letterbox bars slide in/out (TopLetterboxBar & BottomLetterboxBar)
/// - Explorer smooth facing toward player
/// - Coordinated animation triggers via NpcExplorerController
/// </summary>
public class ExplorerCinematicController : MonoBehaviour
{
    [Header("Cinematic Camera Settings")]
    [SerializeField] private Transform dialogueCameraPoint;
    [SerializeField] private CinemachineCamera cinemachineCam;
    [SerializeField] private float dialogueFOV = 50f;
    [SerializeField] private float transitionTime = 0.8f;

    [Header("Letterbox UI")]
    [SerializeField] private RectTransform topLetterboxBar;
    [SerializeField] private RectTransform bottomLetterboxBar;

    [Header("Controller Reference")]
    [SerializeField] private NpcExplorerController explorerController;

    private Coroutine letterboxRoutine;
    private Coroutine facePlayerRoutine;
    private bool isInCinematic = false;
    public bool IsInCinematic => isInCinematic;

    private GameObject viewmodelCameraObj;
    private int savedCameraCullingMask;
    private bool cameraCullingMaskModified = false;

    private void Awake()
    {
        if (explorerController == null)
        {
            explorerController = GetComponent<NpcExplorerController>();
        }
        EnsureDialogueCamera();
        FindLetterboxBars();
    }

    public void EnsureDialogueCamera()
    {
        if (dialogueCameraPoint == null)
        {
            Transform existing = transform.Find("ExplorerDialogueCamPoint");
            if (existing != null)
            {
                dialogueCameraPoint = existing;
            }
            else
            {
                GameObject camObj = new GameObject("ExplorerDialogueCamPoint");
                camObj.transform.SetParent(transform, false);
                camObj.transform.localPosition = new Vector3(-0.6f, 1.55f, 1.8f);
                camObj.transform.localRotation = Quaternion.Euler(5f, 160f, 0f);
                dialogueCameraPoint = camObj.transform;
            }
        }

        if (dialogueCameraPoint != null && cinemachineCam == null)
        {
            cinemachineCam = dialogueCameraPoint.GetComponent<CinemachineCamera>();
            if (cinemachineCam == null)
            {
                cinemachineCam = dialogueCameraPoint.gameObject.AddComponent<CinemachineCamera>();
            }
            cinemachineCam.Lens.FieldOfView = dialogueFOV;
            cinemachineCam.Lens.NearClipPlane = 0.03f;
            cinemachineCam.Priority.Value = 0;
        }
    }

    private void FindLetterboxBars()
    {
        if (topLetterboxBar == null)
        {
            GameObject tlb = GameObject.Find("TopLetterboxBar");
            if (tlb != null) topLetterboxBar = tlb.GetComponent<RectTransform>();
        }
        if (bottomLetterboxBar == null)
        {
            GameObject blb = GameObject.Find("BottomLetterboxBar");
            if (blb != null) bottomLetterboxBar = blb.GetComponent<RectTransform>();
        }
    }

    public void ResetLetterboxImmediate()
    {
        FindLetterboxBars();
        if (topLetterboxBar != null) topLetterboxBar.anchoredPosition = new Vector2(0f, 86f);
        if (bottomLetterboxBar != null) bottomLetterboxBar.anchoredPosition = new Vector2(0f, -86f);
    }

    public void BeginCinematic(Transform playerTransform)
    {
        isInCinematic = true;
        EnsureDialogueCamera();
        FindLetterboxBars();

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            if (!cameraCullingMaskModified)
            {
                savedCameraCullingMask = mainCam.cullingMask;
                int hfLayer = LayerMask.NameToLayer("HiddenFromFirstPerson");
                if (hfLayer != -1) mainCam.cullingMask |= (1 << hfLayer);
                else mainCam.cullingMask |= (1 << 7);
                cameraCullingMaskModified = true;
            }

            // Configure CinemachineBrain blend
            CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
            if (brain != null)
            {
                brain.IgnoreTimeScale = true;
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, transitionTime);
            }

            // Hide first-person viewmodel arms if present
            Transform vm = mainCam.transform.Find("ViewmodelCamera");
            if (vm != null)
            {
                viewmodelCameraObj = vm.gameObject;
                viewmodelCameraObj.SetActive(false);
            }

            // Calculate obstacle-free framing shot
            Vector3 playerPos = playerTransform != null ? playerTransform.position : (mainCam.transform.position - Vector3.up * 1.5f);
            Vector3 speakerPos = transform.position;
            Vector3 toSpeaker = speakerPos - playerPos;
            toSpeaker.y = 0f;
            if (toSpeaker.sqrMagnitude < 0.01f) toSpeaker = transform.forward * -1f;
            toSpeaker.Normalize();

            Vector3 rightOffset = Vector3.Cross(Vector3.up, toSpeaker) * 0.45f;
            Vector3 playerEye = playerPos + Vector3.up * 1.55f;
            Vector3 desiredShotPos = playerPos - toSpeaker * 0.85f + rightOffset + Vector3.up * 1.58f;

            // SphereCast to avoid geometry/terrain clipping
            Vector3 rayDir = desiredShotPos - playerEye;
            float rayDist = rayDir.magnitude;
            if (rayDist > 0.05f)
            {
                rayDir /= rayDist;
                int mask = ~((1 << 8) | (1 << 2)); // Ignore Player (8) and IgnoreRaycast (2)
                if (Physics.SphereCast(playerEye, 0.20f, rayDir, out RaycastHit hit, rayDist, mask, QueryTriggerInteraction.Ignore))
                {
                    desiredShotPos = playerEye + rayDir * Mathf.Max(0.35f, hit.distance - 0.1f);
                }
            }

            Vector3 speakerFocus = speakerPos + Vector3.up * 1.35f;
            Quaternion shotRot = Quaternion.LookRotation((speakerFocus - desiredShotPos).normalized, Vector3.up);

            if (dialogueCameraPoint != null)
            {
                dialogueCameraPoint.SetPositionAndRotation(desiredShotPos, shotRot);
            }

            if (cinemachineCam != null)
            {
                cinemachineCam.Lens.FieldOfView = dialogueFOV;
                cinemachineCam.Lens.NearClipPlane = 0.03f;
                cinemachineCam.Priority.Value = 35;
            }
        }

        // Slide in letterbox bars
        if (letterboxRoutine != null) StopCoroutine(letterboxRoutine);
        letterboxRoutine = StartCoroutine(AnimateLetterbox(true, 0.55f));

        // Smoothly face player
        if (facePlayerRoutine != null) StopCoroutine(facePlayerRoutine);
        if (playerTransform != null)
        {
            facePlayerRoutine = StartCoroutine(SmoothFaceTarget(playerTransform.position));
        }
    }

    public void EndCinematic()
    {
        isInCinematic = false;

        if (cameraCullingMaskModified && Camera.main != null)
        {
            Camera.main.cullingMask = savedCameraCullingMask;
            cameraCullingMaskModified = false;
        }

        if (cinemachineCam != null)
        {
            cinemachineCam.Priority.Value = 0;
        }

        if (viewmodelCameraObj != null)
        {
            viewmodelCameraObj.SetActive(true);
            viewmodelCameraObj = null;
        }

        if (letterboxRoutine != null) StopCoroutine(letterboxRoutine);
        if (isActiveAndEnabled && gameObject.scene.isLoaded)
        {
            letterboxRoutine = StartCoroutine(AnimateLetterbox(false, 0.45f));
        }
        else
        {
            ResetLetterboxImmediate();
        }

        if (facePlayerRoutine != null)
        {
            StopCoroutine(facePlayerRoutine);
            facePlayerRoutine = null;
        }

        if (explorerController != null)
        {
            explorerController.StopTalk();
        }
    }

    private void OnDisable()
    {
        if (cameraCullingMaskModified && Camera.main != null)
        {
            Camera.main.cullingMask = savedCameraCullingMask;
            cameraCullingMaskModified = false;
        }
        if (letterboxRoutine != null)
        {
            StopCoroutine(letterboxRoutine);
            letterboxRoutine = null;
        }
        ResetLetterboxImmediate();
    }

    public void PlayWave()
    {
        if (explorerController != null) explorerController.PlayWave();
    }

    public void PlayReaction(string reaction)
    {
        if (explorerController != null) explorerController.PlayReaction(reaction);
    }

    public void SetTalking(bool talking)
    {
        if (explorerController == null) return;
        if (talking)
        {
            explorerController.PlayTalk(Random.Range(0, 2));
        }
        else
        {
            explorerController.StopTalk();
        }
    }

    private IEnumerator SmoothFaceTarget(Vector3 targetPos)
    {
        Vector3 dir = targetPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) yield break;

        Quaternion startRot = transform.rotation;
        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);

        float elapsed = 0f;
        float dur = 0.6f;
        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            transform.rotation = Quaternion.Slerp(startRot, targetRot, Mathf.SmoothStep(0f, 1f, elapsed / dur));
            yield return null;
        }
        transform.rotation = targetRot;
        facePlayerRoutine = null;
    }

    private IEnumerator AnimateLetterbox(bool enter, float duration)
    {
        FindLetterboxBars();
        if (topLetterboxBar == null && bottomLetterboxBar == null) yield break;

        float elapsed = 0f;
        Vector2 topClosed = new Vector2(0f, 86f);
        Vector2 topOpen = Vector2.zero;
        Vector2 botClosed = new Vector2(0f, -86f);
        Vector2 botOpen = Vector2.zero;

        Vector2 topStart = enter ? topClosed : (topLetterboxBar != null ? topLetterboxBar.anchoredPosition : topOpen);
        Vector2 topEnd = enter ? topOpen : topClosed;
        Vector2 botStart = enter ? botClosed : (bottomLetterboxBar != null ? bottomLetterboxBar.anchoredPosition : botOpen);
        Vector2 botEnd = enter ? botOpen : botClosed;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float norm = Mathf.Clamp01(elapsed / duration);
            float smooth = Mathf.SmoothStep(0f, 1f, norm);

            if (topLetterboxBar != null) topLetterboxBar.anchoredPosition = Vector2.Lerp(topStart, topEnd, smooth);
            if (bottomLetterboxBar != null) bottomLetterboxBar.anchoredPosition = Vector2.Lerp(botStart, botEnd, smooth);
            yield return null;
        }

        if (topLetterboxBar != null) topLetterboxBar.anchoredPosition = topEnd;
        if (bottomLetterboxBar != null) bottomLetterboxBar.anchoredPosition = botEnd;
        letterboxRoutine = null;
    }
}
