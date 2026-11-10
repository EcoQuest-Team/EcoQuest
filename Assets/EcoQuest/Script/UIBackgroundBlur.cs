using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Manages runtime Depth of Field blur for NPC dialogue (Bokeh focused on NPC head)
/// and full-screen modals (Pause/Settings/Quiz).
/// Ref-counted with per-frame focus tracking and 0.5s failsafe reset.
/// </summary>
public static class UIBackgroundBlur
{
    public enum BlurMode
    {
        None = 0,
        DialogueFocus = 1,
        FullScene = 2
    }

    private static Volume blurVolume;
    private static DepthOfField dofComponent;
    private static UniversalAdditionalCameraData cameraData;
    private static readonly HashSet<string> activeDialogueOwners = new HashSet<string>();
    private static readonly HashSet<string> activeFullSceneOwners = new HashSet<string>();
    private static Transform activeFocusTarget;
    private static Vector3 activeFocusOffset = new Vector3(0f, 1.58f, 0f);
    private static float currentFocusDistance = 2.5f;

    public static BlurMode CurrentMode { get; private set; } = BlurMode.None;
    public static int ActiveDialogueOwnerCount => activeDialogueOwners.Count;
    public static int ActiveFullSceneOwnerCount => activeFullSceneOwners.Count;
    public static bool IsDialogueDofActive => activeDialogueOwners.Count > 0;
    public static bool IsFullSceneBlurActive => activeFullSceneOwners.Count > 0;
    public static float CurrentFocusDistance => dofComponent != null ? dofComponent.focusDistance.value : currentFocusDistance;
    public static DepthOfField DofComponent => dofComponent;

    public static Volume EnsureVolume()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            cameraData = mainCam.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData != null && !cameraData.renderPostProcessing)
            {
                cameraData.renderPostProcessing = true;
            }
        }

        if (blurVolume == null || blurVolume.gameObject == null)
        {
            GameObject go = GameObject.Find("UI_Blur_Volume");
            if (go == null)
            {
                var volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var v in volumes)
                {
                    if (v != null && v.gameObject.name.Contains("Blur"))
                    {
                        go = v.gameObject;
                        break;
                    }
                }
            }
            if (go == null)
            {
                go = new GameObject("UI_Blur_Volume");
            }

            blurVolume = go.GetComponent<Volume>();
            if (blurVolume == null)
            {
                blurVolume = go.AddComponent<Volume>();
            }
        }

        blurVolume.isGlobal = true;
        blurVolume.priority = 100f;

        VolumeProfile profile = blurVolume.sharedProfile;
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "UI_Blur_Profile";
            blurVolume.sharedProfile = profile;
        }

        if (!profile.TryGet(out dofComponent) || dofComponent == null)
        {
            dofComponent = profile.Add<DepthOfField>(true);
        }

        dofComponent.active = true;
        dofComponent.highQualitySampling.Override(true);

        return blurVolume;
    }

    /// <summary>
    /// Acquires ref-counted dialogue DOF focused on the NPC head, keeping NPC and player head sharp
    /// while blurring only the background beyond the NPC.
    /// </summary>
    public static void AcquireDialogueDof(string ownerId, Transform npcTarget, Vector3 headOffset)
    {
        if (string.IsNullOrEmpty(ownerId)) ownerId = "Dialogue";
        activeDialogueOwners.Add(ownerId);
        activeFocusTarget = npcTarget;
        activeFocusOffset = headOffset;

        EnsureVolume();
        UpdateDialogueFocusImmediate();
    }

    public static void ReleaseDialogueDof(string ownerId)
    {
        if (string.IsNullOrEmpty(ownerId)) ownerId = "Dialogue";
        activeDialogueOwners.Remove(ownerId);
        if (activeDialogueOwners.Count == 0)
        {
            activeFocusTarget = null;
        }
    }

    public static void ClearAllDialogueOwners()
    {
        activeDialogueOwners.Clear();
        activeFocusTarget = null;
    }

    /// <summary>
    /// Updates the DepthOfField focusDistance to Vector3.Distance(dialogueCam.position, npcHead.position).
    /// Configures Bokeh (and Gaussian fallback bounds) so both player head and NPC remain sharp,
    /// blurring only the background beyond the NPC.
    /// </summary>
    public static void UpdateDialogueFocusImmediate(Transform overrideTarget = null, Vector3? overrideWorldFocusPos = null)
    {
        EnsureVolume();
        if (dofComponent == null) return;

        Camera cam = Camera.main;
        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
        Transform target = overrideTarget != null ? overrideTarget : activeFocusTarget;

        Vector3 focusWorldPos = camPos + (cam != null ? cam.transform.forward * 2.5f : Vector3.forward * 2.5f);
        if (overrideWorldFocusPos.HasValue)
        {
            focusWorldPos = overrideWorldFocusPos.Value;
        }
        else if (target != null)
        {
            Animator anim = target.GetComponentInChildren<Animator>();
            Transform headBone = (anim != null && anim.isHuman) ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            focusWorldPos = headBone != null ? headBone.position : (target.position + activeFocusOffset);
        }

        float dist = cam != null ? Vector3.Distance(camPos, focusWorldPos) : 2.5f;
        dist = Mathf.Clamp(dist, 0.8f, 15f);
        currentFocusDistance = dist;

        // Bokeh mode: focused on NPC head; 40mm f/4.2 keeps player head (~1.4m) and NPC (~2.4-3.0m) in sharp range
        // while blurring city background beyond the NPC.
        dofComponent.mode.Override(DepthOfFieldMode.Bokeh);
        dofComponent.focusDistance.Override(dist);
        dofComponent.focalLength.Override(40f);
        dofComponent.aperture.Override(4.2f);
        dofComponent.bladeCount.Override(5);
        dofComponent.bladeCurvature.Override(1f);

        // Gaussian fallback parameters in case platform falls back to Gaussian: start beyond NPC so NPC & player are never blurred
        dofComponent.gaussianStart.Override(dist + 0.75f);
        dofComponent.gaussianEnd.Override(dist + 6.5f);
        dofComponent.gaussianMaxRadius.Override(1.5f);
        CurrentMode = BlurMode.DialogueFocus;
    }

    public static void AcquireFullSceneBlur(string ownerId)
    {
        if (string.IsNullOrEmpty(ownerId)) ownerId = "Modal";
        activeFullSceneOwners.Add(ownerId);
        ConfigureFullModalBlur();
    }

    public static void ReleaseFullSceneBlur(string ownerId)
    {
        if (string.IsNullOrEmpty(ownerId)) ownerId = "Modal";
        activeFullSceneOwners.Remove(ownerId);
        if (activeFullSceneOwners.Count == 0 && !IsDialogueDofActive)
        {
            CurrentMode = BlurMode.None;
        }
    }

    /// <summary>
    /// Configures full-screen blur for non-dialogue modals (Pause, Quiz, Results, Inventory).
    /// </summary>
    public static void ConfigureFullModalBlur()
    {
        if (IsDialogueDofActive)
        {
            UpdateDialogueFocusImmediate();
            return;
        }

        EnsureVolume();
        if (dofComponent == null) return;

        dofComponent.mode.Override(DepthOfFieldMode.Gaussian);
        dofComponent.gaussianStart.Override(0f);
        dofComponent.gaussianEnd.Override(0.5f);
        dofComponent.gaussianMaxRadius.Override(1.5f);
        CurrentMode = BlurMode.FullScene;
    }

    public static float GetWeight()
    {
        if (blurVolume != null && blurVolume.gameObject != null && blurVolume.gameObject.activeSelf)
        {
            return blurVolume.weight;
        }
        return 0f;
    }

    public static void SetWeight(float weight)
    {
        weight = Mathf.Clamp01(weight);
        if (weight <= 0.0001f && (blurVolume == null || blurVolume.gameObject == null)) return;
        var vol = EnsureVolume();
        if (vol == null) return;

        vol.weight = weight;
        if (weight <= 0.0001f)
        {
            vol.weight = 0f;
            if (vol.gameObject.activeSelf)
            {
                vol.gameObject.SetActive(false);
            }
            if (!IsDialogueDofActive && !IsFullSceneBlurActive)
            {
                CurrentMode = BlurMode.None;
            }
        }
        else
        {
            if (!vol.gameObject.activeSelf)
            {
                vol.gameObject.SetActive(true);
            }
            if (cameraData != null && !cameraData.renderPostProcessing)
            {
                cameraData.renderPostProcessing = true;
            }
        }
    }

    public static void Cleanup()
    {
        activeDialogueOwners.Clear();
        activeFullSceneOwners.Clear();
        activeFocusTarget = null;
        CurrentMode = BlurMode.None;
        if (blurVolume != null && blurVolume.gameObject != null)
        {
            blurVolume.weight = 0f;
            blurVolume.gameObject.SetActive(false);
        }
    }
}
