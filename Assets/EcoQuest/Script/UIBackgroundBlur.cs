using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Manages runtime background blur (Gaussian Depth of Field) for pause and settings UI.
/// Smoothly lerps volume weight with unscaled time, ensuring 0 leftover weight on close.
/// </summary>
public static class UIBackgroundBlur
{
    private static Volume blurVolume;
    private static UniversalAdditionalCameraData cameraData;

    public static Volume EnsureVolume()
    {
        if (blurVolume != null && blurVolume.gameObject != null)
        {
            return blurVolume;
        }

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            cameraData = mainCam.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData != null && !cameraData.renderPostProcessing)
            {
                cameraData.renderPostProcessing = true;
            }
        }

        GameObject go = GameObject.Find("UI_Blur_Volume");
        if (go == null)
        {
            go = new GameObject("UI_Blur_Volume");
        }

        blurVolume = go.GetComponent<Volume>();
        if (blurVolume == null)
        {
            blurVolume = go.AddComponent<Volume>();
        }

        blurVolume.isGlobal = true;
        blurVolume.priority = 100f;

        if (blurVolume.sharedProfile == null)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "UI_Blur_Profile";
            var dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(0.05f);
            dof.gaussianEnd.Override(1.0f);
            dof.gaussianMaxRadius.Override(2.0f);
            dof.highQualitySampling.Override(true);
            blurVolume.sharedProfile = profile;
        }

        return blurVolume;
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
        if (blurVolume != null && blurVolume.gameObject != null)
        {
            blurVolume.weight = 0f;
            blurVolume.gameObject.SetActive(false);
        }
    }
}
