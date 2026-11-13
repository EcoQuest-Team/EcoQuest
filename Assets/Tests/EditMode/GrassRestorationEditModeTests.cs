using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EcoQuest.Tests.EditMode
{
    public class GrassRestorationEditModeTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void GrassShader_AndMaterial_CompilesCleanly_WithoutBackfaceFlipOrSSAO()
        {
            Shader grassShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/EcoQuest/Shaders/EcoQuest_GrassCutout.shader");
            Assert.IsNotNull(grassShader, "EcoQuest_GrassCutout.shader must exist.");
            Assert.IsFalse(ShaderUtil.ShaderHasError(grassShader), "EcoQuest_GrassCutout.shader must compile with zero errors.");

            string shaderSrc = File.ReadAllText("Assets/EcoQuest/Shaders/EcoQuest_GrassCutout.shader");
            Assert.IsFalse(shaderSrc.Contains("VFACE"), "Grass shader must not flip backface normals with VFACE.");
            Assert.IsFalse(shaderSrc.Contains("_SCREEN_SPACE_OCCLUSION"), "Grass shader must not apply SSAO self-darkening.");
            Assert.IsTrue(shaderSrc.Contains("SampleAndClipGrass"), "Grass shader must use shared mip-preserving SampleAndClipGrass cutout helper.");

            Material grassMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Polytope Studio/Lowpoly_Demos/Environment_Free/Helpers/PT_Grass_Mat 1.mat");
            Assert.IsNotNull(grassMat, "PT_Grass_Mat 1.mat must exist.");
            Assert.AreEqual("EcoQuest/VegetationGrassCutout", grassMat.shader.name);
            Assert.IsTrue(grassMat.enableInstancing, "Grass material must keep GPU instancing enabled.");
        }

        [Test]
        public void ForestTerrain_RestoredClusteredGrassDensityAndLushScale_MatchesTargetSpec()
        {
            TerrainData td = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/New Terrain 2.asset");
            Assert.IsNotNull(td, "New Terrain 2.asset must exist.");
            Assert.AreEqual(DetailScatterMode.InstanceCountMode, td.detailScatterMode);

            DetailPrototype[] protos = td.detailPrototypes;
            Assert.Greater(protos.Length, 2, "DetailPrototypes must contain grass layer at index 2.");
            DetailPrototype grassProto = protos[2];
            Assert.IsTrue(grassProto.useInstancing, "DetailProto[2] must use GPU instancing.");
            Assert.GreaterOrEqual((grassProto.minHeight + grassProto.maxHeight) * 0.5f, 1.80f, "Grass mean height should match pre-optimization lush height (~1.85).");
            Assert.GreaterOrEqual((grassProto.minWidth + grassProto.maxWidth) * 0.5f, 1.75f, "Grass mean width should match pre-optimization lush width (~1.80).");

            int[,] layer = td.GetDetailLayer(0, 0, td.detailWidth, td.detailHeight, 2);
            long totalInstances = 0;
            int nonZeroCells = 0;
            int maxPerCell = 0;
            for (int y = 0; y < td.detailHeight; y++)
            {
                for (int x = 0; x < td.detailWidth; x++)
                {
                    int v = layer[y, x];
                    if (v > 0)
                    {
                        nonZeroCells++;
                        totalInstances += v;
                        if (v > maxPerCell) maxPerCell = v;
                    }
                }
            }

            Assert.AreEqual(33498, nonZeroCells, "Restored DetailLayer[2] non-zero cell count should match pre-opt layout.");
            Assert.AreEqual(46469L, totalInstances, "Restored DetailLayer[2] total grass clumps should match all 46,469 pre-opt clumps.");
            Assert.Greater(maxPerCell, 1, "Restored grass layout must preserve multi-clump clusters per cell.");
        }

        [Test]
        public void TerrainQualitySettings_ScaleUltraOutPastHigh_WhilePreservingPresetDistances()
        {
            string prevScenePath = SceneManager.GetActiveScene().path;
            bool openedForest = false;
            int prevQuality = QualitySettings.GetQualityLevel();
            try
            {
                if (SceneManager.GetActiveScene().name != "Forest Scene")
                {
                    EditorSceneManager.OpenScene("Assets/EcoQuest/Forest Scene.unity", OpenSceneMode.Single);
                    openedForest = true;
                }

                Terrain t = Terrain.activeTerrain;
                Assert.IsNotNull(t, "Forest Scene must have an active Terrain.");

                Type smType = Type.GetType("SettingsManager, Assembly-CSharp", true);
                MethodInfo applyTerrain = smType.GetMethod("ApplyTerrainSettingsForQuality", Flags);
                Assert.IsNotNull(applyTerrain);

                applyTerrain.Invoke(null, new object[] { 0 });
                Assert.AreEqual(35f, t.detailObjectDistance, 0.1f);
                Assert.AreEqual(0.35f, t.detailObjectDensity, 0.01f);

                applyTerrain.Invoke(null, new object[] { 1 });
                Assert.AreEqual(50f, t.detailObjectDistance, 0.1f);
                Assert.AreEqual(0.60f, t.detailObjectDensity, 0.01f);

                applyTerrain.Invoke(null, new object[] { 2 });
                Assert.AreEqual(70f, t.detailObjectDistance, 0.1f);
                Assert.AreEqual(0.85f, t.detailObjectDensity, 0.01f);

                applyTerrain.Invoke(null, new object[] { 3 });
                Assert.AreEqual(115f, t.detailObjectDistance, 0.1f);
                Assert.AreEqual(1.00f, t.detailObjectDensity, 0.01f);
            }
            finally
            {
                QualitySettings.SetQualityLevel(prevQuality, true);
                if (openedForest && !string.IsNullOrEmpty(prevScenePath) && prevScenePath != "Assets/EcoQuest/Forest Scene.unity")
                {
                    EditorSceneManager.OpenScene(prevScenePath, OpenSceneMode.Single);
                }
            }
        }
    }
}
