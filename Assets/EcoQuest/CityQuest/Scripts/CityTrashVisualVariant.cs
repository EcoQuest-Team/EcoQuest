using UnityEngine;

namespace EcoQuest.CityQuest
{
    /// <summary>
    /// Selects deterministic mesh/material variants for City trash pickup visuals.
    /// Extracted into its own file so Unity serializes its MonoScript GUID cleanly across prefabs and scenes.
    /// </summary>
    [DisallowMultipleComponent]
    public class CityTrashVisualVariant : MonoBehaviour
    {
        public MeshFilter targetMeshFilter;
        public MeshRenderer targetRenderer;
        public Mesh[] variantMeshes;
        public Material[] variantMaterials;

        public void ApplyVariant(int seed)
        {
            if (targetMeshFilter == null) targetMeshFilter = GetComponentInChildren<MeshFilter>();
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<MeshRenderer>();

            int abs = Mathf.Abs(seed);
            if (targetMeshFilter != null && variantMeshes != null && variantMeshes.Length > 0)
            {
                Mesh m = variantMeshes[abs % variantMeshes.Length];
                if (m != null) targetMeshFilter.sharedMesh = m;
            }
            if (targetRenderer != null && variantMaterials != null && variantMaterials.Length > 0)
            {
                Material mat = variantMaterials[(abs / 3) % variantMaterials.Length];
                if (mat != null) targetRenderer.sharedMaterial = mat;
            }
        }
    }
}
