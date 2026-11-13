using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EcoQuest.Tests.EditMode
{
    public class OptimizationPresetAndSettingsTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void QualityPresets_HaveDedicatedURPAssetsAndMinimapRendererAtIndex1()
        {
            string[] expectedAssetNames = { "URP_Low", "URP_Medium", "URP_High", "URP_Ultra" };
            int count = QualitySettings.names.Length;
            Assert.GreaterOrEqual(count, 4, "Expected at least 4 quality presets (Low, Medium, High, Ultra).");

            ScriptableObject minimapRenderer = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Settings/Minimap_Renderer.asset");
            Assert.IsNotNull(minimapRenderer, "Minimap_Renderer.asset must exist.");

            for (int q = 0; q < 4; q++)
            {
                RenderPipelineAsset rp = QualitySettings.GetRenderPipelineAssetAt(q);
                Assert.IsNotNull(rp, $"Quality index {q} must have a URP RenderPipelineAsset.");
                Assert.AreEqual(expectedAssetNames[q], rp.name, $"Quality index {q} URP asset mismatch.");

                SerializedObject so = new SerializedObject(rp);
                SerializedProperty rendererList = so.FindProperty("m_RendererDataList");
                Assert.IsNotNull(rendererList);
                Assert.GreaterOrEqual(rendererList.arraySize, 2, $"{rp.name} must include Minimap_Renderer at index 1.");
                Assert.AreEqual(minimapRenderer, rendererList.GetArrayElementAtIndex(1).objectReferenceValue, $"{rp.name} renderer[1] must be Minimap_Renderer.");

                SerializedProperty defaultRendererObjProp = rendererList.GetArrayElementAtIndex(0);
                UnityEngine.Object defaultRenderer = defaultRendererObjProp.objectReferenceValue;
                Assert.IsNotNull(defaultRenderer, $"{rp.name} must have a valid default renderer at index 0.");

                SerializedObject renSo = new SerializedObject(defaultRenderer);
                if (q <= 1)
                {
                    SerializedProperty features = renSo.FindProperty("m_RendererFeatures");
                    Assert.IsNotNull(features);
                    Assert.AreEqual(0, features.arraySize, $"{rp.name} default renderer should not run expensive RendererFeatures (e.g., SSAO).");
                }
            }

            // Minimap_Renderer itself should stay Forward and feature-free
            SerializedObject mmSo = new SerializedObject(minimapRenderer);
            Assert.AreEqual(0, mmSo.FindProperty("m_RendererFeatures").arraySize, "Minimap_Renderer must have 0 renderer features.");
        }

        [Test]
        public void ForestGrassMaterial_UsesInstancedCutoutShader_AndNoBuildSceneHasMissingScripts()
        {
            Material grassMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Polytope Studio/Lowpoly_Demos/Environment_Free/Helpers/PT_Grass_Mat 1.mat");
            Assert.IsNotNull(grassMat, "PT_Grass_Mat 1.mat must exist.");
            Assert.AreEqual("EcoQuest/VegetationGrassCutout", grassMat.shader.name, "Grass material must use custom EcoQuest/VegetationGrassCutout opaque alpha-tested shader.");
            Assert.IsTrue(grassMat.enableInstancing, "Grass material must have GPU Instancing enabled.");

            RenderTexture minimapRt = AssetDatabase.LoadAssetAtPath<RenderTexture>("Assets/EcoQuest/MinimapTexture.renderTexture");
            Assert.IsNotNull(minimapRt);
            Assert.AreEqual(512, minimapRt.width, "MinimapTexture width should be 512.");
            Assert.AreEqual(512, minimapRt.height, "MinimapTexture height should be 512.");
            Assert.AreEqual(1, minimapRt.antiAliasing, "MinimapTexture must not use MSAA.");

            string originalScenePath = SceneManager.GetActiveScene().path;
            try
            {
                foreach (var s in EditorBuildSettings.scenes)
                {
                    if (!s.enabled) continue;
                    Scene opened = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
                    int missingCount = 0;
                    foreach (var root in opened.GetRootGameObjects())
                    {
                        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                        {
                            missingCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(tr.gameObject);
                        }
                    }
                    Assert.AreEqual(0, missingCount, $"Build scene {s.path} must have 0 missing scripts.");
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(originalScenePath))
                {
                    EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
                }
            }
        }

        [Test]
        public void HardwareAutoDetect_ClassifiesLowIntegratedAndDedicatedGPUsCorrectly()
        {
            Type smType = Type.GetType("SettingsManager, Assembly-CSharp", true);
            MethodInfo detect = smType.GetMethod(
                "DetectRecommendedQualityIndex",
                Flags,
                null,
                new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(GraphicsDeviceType) },
                null);
            Assert.IsNotNull(detect, "SettingsManager.DetectRecommendedQualityIndex overload must exist.");

            int lowUhd = (int)detect.Invoke(null, new object[] { "Intel(R) UHD Graphics 620", 128, 8192, 8, GraphicsDeviceType.Direct3D11 });
            int lowRam = (int)detect.Invoke(null, new object[] { "NVIDIA GeForce GTX 1050", 2048, 4096, 4, GraphicsDeviceType.Direct3D11 });
            int medIris = (int)detect.Invoke(null, new object[] { "Intel(R) Iris(R) Xe Graphics", 2048, 16384, 8, GraphicsDeviceType.Direct3D11 });
            int medRadeon = (int)detect.Invoke(null, new object[] { "AMD Radeon(TM) Graphics", 4096, 16384, 16, GraphicsDeviceType.Direct3D11 });
            int highRtx = (int)detect.Invoke(null, new object[] { "NVIDIA GeForce RTX 3060", 8192, 16384, 12, GraphicsDeviceType.Direct3D11 });

            Assert.AreEqual(0, lowUhd, "Intel UHD iGPU should default to Low (0).");
            Assert.AreEqual(0, lowRam, "<=6GB RAM should default to Low (0).");
            Assert.AreEqual(1, medIris, "Intel Iris Xe iGPU should default to Medium (1).");
            Assert.AreEqual(1, medRadeon, "AMD Radeon integrated graphics should default to Medium (1).");
            Assert.AreEqual(2, highRtx, "Midrange/dedicated discrete GPU should default to High (2) and never auto-pick Ultra (3).");
        }
    }
}
