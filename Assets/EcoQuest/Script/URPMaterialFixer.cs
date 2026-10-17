using UnityEngine;

[ExecuteAlways]
public class URPMaterialFixer : MonoBehaviour
{
    private void Start()
    {
        FixPinkMaterialsInScene();
    }

    public static void FixPinkMaterialsInScene()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) urpLit = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (urpLit == null) return;

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        int fixedCount = 0;

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;

            Material[] mats = r.sharedMaterials;
            bool modified = false;

            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null) continue;

                Shader s = m.shader;
                // Check if shader is missing/invalid/pink or legacy surface shader incompatible with URP
                if (s == null || !s.isSupported || s.name.Contains("Error") || s.name == "Standard" || s.name.Contains("BodyColor") || s.name.StartsWith("Legacy"))
                {
                    Texture mainTex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : (m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null);
                    Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white);

                    Material newMat = new Material(urpLit);
                    newMat.name = m.name + "_URP_Fixed";
                    if (mainTex != null) newMat.SetTexture("_BaseMap", mainTex);
                    newMat.SetColor("_BaseColor", color);

                    mats[i] = newMat;
                    modified = true;
                    fixedCount++;
                }
            }

            if (modified)
            {
                r.sharedMaterials = mats;
            }
        }

        if (fixedCount > 0)
        {
            Debug.Log($"[URPMaterialFixer] Fixed {fixedCount} pink/legacy materials to URP Lit!");
        }
    }
}
