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
                    Texture mainTex = null;
                    if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) mainTex = m.GetTexture("_BaseMap");
                    else if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) mainTex = m.GetTexture("_MainTex");

                    // Fallback for Polyperfect meshes if texture was missing
                    if (mainTex == null && (r.name.Contains("_Rig") || r.name.StartsWith("man-") || r.name.StartsWith("woman-") || r.name.StartsWith("girl-")))
                    {
                        var atlasMat = Resources.Load<Material>("atlas-LPUP");
#if UNITY_EDITOR
                        if (atlasMat == null) atlasMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/polyperfect/Low Poly Ultimate Pack/- Materials/atlas-LPUP.mat");
#endif
                        if (atlasMat != null && atlasMat.GetTexture("_BaseMap") != null) mainTex = atlasMat.GetTexture("_BaseMap");
                    }

                    Color color = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : (m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white);

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
