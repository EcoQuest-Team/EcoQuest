using System.Collections.Generic;
using UnityEngine;

namespace EcoQuest.CityQuest.Editor
{
    /// <summary>
    /// Builds low-poly, flat-shaded, multi-part 3D meshes (80 to 400 triangles each)
    /// with base-center pivot (min.y == 0) and 4-quadrant UV palette mapping so every
    /// trash item has a distinct silhouette and multi-colored details in 1 draw call.
    /// </summary>
    public static class CityTrashProceduralMeshFactory
    {
        // UV quadrant centers for the 4-color palette texture:
        // Q0 (Bottom-Left): Primary Body Color
        // Q1 (Bottom-Right): Secondary / Label / Peel Inner Color
        // Q2 (Top-Left): Cap / Rim / Metallic Accent Color
        // Q3 (Top-Right): Dark / Stem / Detail Color
        public static readonly Vector2 UV_Q0 = new Vector2(0.25f, 0.25f);
        public static readonly Vector2 UV_Q1 = new Vector2(0.75f, 0.25f);
        public static readonly Vector2 UV_Q2 = new Vector2(0.25f, 0.75f);
        public static readonly Vector2 UV_Q3 = new Vector2(0.75f, 0.75f);

        private class MeshBuilder
        {
            public readonly List<Vector3> verts = new List<Vector3>(512);
            public readonly List<Vector2> uvs = new List<Vector2>(512);
            public readonly List<int> tris = new List<int>(1024);

            public void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector2 uv)
            {
                int idx = verts.Count;
                verts.Add(a);
                verts.Add(b);
                verts.Add(c);
                uvs.Add(uv);
                uvs.Add(uv);
                uvs.Add(uv);
                tris.Add(idx);
                tris.Add(idx + 1);
                tris.Add(idx + 2);
            }

            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uv)
            {
                AddTri(a, b, c, uv);
                AddTri(a, c, d, uv);
            }

            public void AddBox(Vector3 center, Vector3 size, Quaternion rot, Vector2 uvTop, Vector2 uvSide, Vector2 uvBottom)
            {
                Vector3 h = size * 0.5f;
                Vector3[] p = new Vector3[8]
                {
                    center + rot * new Vector3(-h.x, -h.y, -h.z),
                    center + rot * new Vector3( h.x, -h.y, -h.z),
                    center + rot * new Vector3( h.x, -h.y,  h.z),
                    center + rot * new Vector3(-h.x, -h.y,  h.z),
                    center + rot * new Vector3(-h.x,  h.y, -h.z),
                    center + rot * new Vector3( h.x,  h.y, -h.z),
                    center + rot * new Vector3( h.x,  h.y,  h.z),
                    center + rot * new Vector3(-h.x,  h.y,  h.z),
                };
                // Top
                AddQuad(p[4], p[7], p[6], p[5], uvTop);
                // Bottom
                AddQuad(p[0], p[1], p[2], p[3], uvBottom);
                // Front/Back/Left/Right
                AddQuad(p[0], p[4], p[5], p[1], uvSide);
                AddQuad(p[1], p[5], p[6], p[2], uvSide);
                AddQuad(p[2], p[6], p[7], p[3], uvSide);
                AddQuad(p[3], p[7], p[4], p[0], uvSide);
            }

            public void AddLathe(Vector3 origin, Quaternion rot, float[] heights, float[] radii, Vector2[] ringUvs, int segments, bool capBottom, bool capTop, float crushX = 1f, float bendX = 0f)
            {
                int rings = heights.Length;
                Vector3[,] grid = new Vector3[rings, segments];

                for (int r = 0; r < rings; r++)
                {
                    float y = heights[r];
                    float rad = radii[r];
                    float t = rings > 1 ? (float)r / (rings - 1) : 0f;
                    float xOff = Mathf.Sin(t * Mathf.PI) * bendX;

                    for (int s = 0; s < segments; s++)
                    {
                        float ang = (float)s / segments * Mathf.PI * 2f;
                        float lx = Mathf.Cos(ang) * rad * crushX + xOff;
                        float lz = Mathf.Sin(ang) * rad / Mathf.Sqrt(Mathf.Max(0.4f, crushX));
                        grid[r, s] = origin + rot * new Vector3(lx, y, lz);
                    }
                }

                for (int r = 0; r < rings - 1; r++)
                {
                    Vector2 uv = (ringUvs != null && r < ringUvs.Length) ? ringUvs[r] : UV_Q0;
                    for (int s = 0; s < segments; s++)
                    {
                        int next = (s + 1) % segments;
                        AddQuad(grid[r, s], grid[r + 1, s], grid[r + 1, next], grid[r, next], uv);
                    }
                }

                if (capBottom && radii[0] > 0.0005f)
                {
                    Vector3 botCenter = origin + rot * new Vector3(0f, heights[0], 0f);
                    Vector2 uv = (ringUvs != null && ringUvs.Length > 0) ? ringUvs[0] : UV_Q0;
                    for (int s = 0; s < segments; s++)
                    {
                        int next = (s + 1) % segments;
                        AddTri(botCenter, grid[0, s], grid[0, next], uv);
                    }
                }

                if (capTop && radii[rings - 1] > 0.0005f)
                {
                    Vector3 topCenter = origin + rot * new Vector3(0f, heights[rings - 1], 0f);
                    Vector2 uv = (ringUvs != null && ringUvs.Length > 0) ? ringUvs[rings - 1] : UV_Q2;
                    for (int s = 0; s < segments; s++)
                    {
                        int next = (s + 1) % segments;
                        AddTri(topCenter, grid[rings - 1, next], grid[rings - 1, s], uv);
                    }
                }
            }

            public Mesh ToMesh(string meshName)
            {
                // Snap min.y to 0 and center X/Z at 0
                if (verts.Count > 0)
                {
                    Vector3 min = verts[0];
                    Vector3 max = verts[0];
                    for (int i = 1; i < verts.Count; i++)
                    {
                        min = Vector3.Min(min, verts[i]);
                        max = Vector3.Max(max, verts[i]);
                    }
                    Vector3 offset = new Vector3(-(min.x + max.x) * 0.5f, -min.y, -(min.z + max.z) * 0.5f);
                    for (int i = 0; i < verts.Count; i++)
                    {
                        verts[i] += offset;
                    }
                }

                Mesh m = new Mesh();
                m.name = meshName;
                m.SetVertices(verts);
                m.SetUVs(0, uvs);
                m.SetTriangles(tris, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        public static Mesh BuildItemMesh(string itemId, int variantIndex)
        {
            MeshBuilder mb = new MeshBuilder();
            float vShift = (variantIndex - 1) * 0.12f;
            float crush = variantIndex == 1 ? 0.78f : (variantIndex == 2 ? 0.88f : 1.0f);

            switch (itemId)
            {
                case "BananaPeel":
                    BuildBananaPeel(mb, variantIndex);
                    break;
                case "AppleCore":
                    BuildAppleCore(mb, variantIndex);
                    break;
                case "DryLeavesBundle":
                    BuildDryLeavesBundle(mb, variantIndex);
                    break;
                case "PaperNapkin":
                    BuildPaperNapkin(mb, variantIndex);
                    break;
                case "CardboardSleeve":
                    BuildCardboardSleeve(mb, crush);
                    break;
                case "WoodenFork":
                    BuildWoodenFork(mb, variantIndex);
                    break;
                case "PlasticWaterBottle":
                    BuildPlasticWaterBottle(mb, crush, vShift * 0.08f);
                    break;
                case "CrushedSodaCan":
                    BuildCrushedSodaCan(mb, crush, vShift * 0.06f);
                    break;
                case "CardboardBox":
                    BuildCardboardBox(mb, variantIndex);
                    break;
                case "NewspaperStack":
                    BuildNewspaperStack(mb, variantIndex);
                    break;
                case "GlassJar":
                    BuildGlassJar(mb, variantIndex);
                    break;
                case "MilkCarton":
                    BuildMilkCarton(mb, crush);
                    break;
                case "TinCan":
                    BuildTinCan(mb, crush);
                    break;
                case "FlyerStack":
                    BuildFlyerStack(mb, variantIndex);
                    break;
                case "ChipBag":
                    BuildChipBag(mb, variantIndex);
                    break;
                case "StyrofoamCup":
                    BuildStyrofoamCup(mb, crush);
                    break;
                case "PlasticStrawWrapper":
                    BuildPlasticStrawWrapper(mb, variantIndex);
                    break;
                case "CandyWrapper":
                    BuildCandyWrapper(mb, variantIndex);
                    break;
                case "TornPlasticBag":
                    BuildTornPlasticBag(mb, variantIndex);
                    break;
                case "AABattery":
                    BuildAABattery(mb, variantIndex);
                    break;
                case "SprayPaintCan":
                    BuildSprayPaintCan(mb, crush);
                    break;
                case "BrokenLightBulb":
                    BuildBrokenLightBulb(mb, variantIndex);
                    break;
                case "MedicineBlisterPack":
                    BuildMedicineBlisterPack(mb, variantIndex);
                    break;
                case "SmallCircuitBoard":
                default:
                    BuildSmallCircuitBoard(mb, variantIndex);
                    break;
            }

            return mb.ToMesh($"Mesh_{itemId}_v{variantIndex}");
        }

        private static void BuildBananaPeel(MeshBuilder mb, int variant)
        {
            // Center stem + 4 curved peel petals (approx 160 tris)
            mb.AddLathe(new Vector3(0f, 0.06f, 0f), Quaternion.identity,
                new float[] { 0f, 0.05f, 0.09f },
                new float[] { 0.025f, 0.018f, 0.012f },
                new Vector2[] { UV_Q0, UV_Q3, UV_Q3 }, 6, true, true);

            int petals = 4;
            for (int p = 0; p < petals; p++)
            {
                float baseAng = (p * 90f + variant * 18f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(baseAng), 0f, Mathf.Sin(baseAng));
                Vector3 side = new Vector3(-dir.z, 0f, dir.x);

                int segs = 5;
                float len = 0.18f + (p % 2) * 0.02f;
                for (int s = 0; s < segs; s++)
                {
                    float t0 = (float)s / segs;
                    float t1 = (float)(s + 1) / segs;
                    float r0 = Mathf.Lerp(0.02f, len, t0);
                    float r1 = Mathf.Lerp(0.02f, len, t1);
                    float y0 = Mathf.Sin(t0 * Mathf.PI) * 0.065f + (1f - t0) * 0.05f;
                    float y1 = Mathf.Sin(t1 * Mathf.PI) * 0.065f + (1f - t1) * 0.05f;
                    float w0 = Mathf.Sin((t0 * 0.8f + 0.15f) * Mathf.PI) * 0.036f;
                    float w1 = Mathf.Sin((t1 * 0.8f + 0.15f) * Mathf.PI) * 0.036f;

                    Vector3 c0 = dir * r0 + Vector3.up * y0;
                    Vector3 c1 = dir * r1 + Vector3.up * y1;

                    // Outer yellow skin (Q0) & Inner cream peel (Q1) double-sided
                    mb.AddQuad(c0 - side * w0, c0 + side * w0, c1 + side * w1, c1 - side * w1, s == segs - 1 ? UV_Q3 : UV_Q0);
                    mb.AddQuad(c0 - side * w0 + Vector3.up * 0.008f, c1 - side * w1 + Vector3.up * 0.008f, c1 + side * w1 + Vector3.up * 0.008f, c0 + side * w0 + Vector3.up * 0.008f, UV_Q1);
                }
            }
        }

        private static void BuildAppleCore(MeshBuilder mb, int variant)
        {
            Quaternion tilt = Quaternion.Euler(variant * 12f, 0f, 15f);
            float[] h = { 0f, 0.02f, 0.05f, 0.08f, 0.14f, 0.17f, 0.20f, 0.22f };
            float[] r = { 0.035f, 0.075f, 0.082f, 0.032f, 0.032f, 0.085f, 0.075f, 0.030f };
            Vector2[] uvs = { UV_Q0, UV_Q0, UV_Q1, UV_Q1, UV_Q1, UV_Q0, UV_Q0, UV_Q0 };
            mb.AddLathe(Vector3.zero, tilt, h, r, uvs, 12, true, true);

            // Stem + small leaf on top
            mb.AddBox(tilt * new Vector3(0f, 0.24f, 0f), new Vector3(0.012f, 0.045f, 0.012f), tilt * Quaternion.Euler(0f, 0f, -12f), UV_Q3, UV_Q3, UV_Q3);
            mb.AddBox(tilt * new Vector3(0.025f, 0.245f, 0f), new Vector3(0.042f, 0.008f, 0.022f), tilt * Quaternion.Euler(10f, 15f, 25f), UV_Q2, UV_Q2, UV_Q2);
        }

        private static void BuildDryLeavesBundle(MeshBuilder mb, int variant)
        {
            // 12 layered low-poly autumn leaves + 2 crossed twigs (approx 120 tris)
            int leafCount = 12;
            for (int i = 0; i < leafCount; i++)
            {
                float ang = (i * 30f + variant * 23f) * Mathf.Deg2Rad;
                float dist = (i == 0) ? 0f : 0.045f + (i % 4) * 0.022f;
                Vector3 center = new Vector3(Mathf.Cos(ang) * dist, 0.010f + i * 0.005f, Mathf.Sin(ang) * dist);
                Quaternion rot = Quaternion.Euler(12f + (i % 3) * 6f, i * 33f, (i % 2 == 0 ? 10f : -10f));
                Vector2 uv = (i % 3 == 0) ? UV_Q0 : ((i % 3 == 1) ? UV_Q1 : UV_Q2);

                // Diamond/hex leaf blade
                float L = 0.11f;
                float W = 0.055f;
                Vector3 p0 = center + rot * new Vector3(0f, 0f, -L);
                Vector3 p1 = center + rot * new Vector3(-W, 0.01f, -L * 0.2f);
                Vector3 p2 = center + rot * new Vector3(-W * 0.8f, 0.015f, L * 0.4f);
                Vector3 p3 = center + rot * new Vector3(0f, 0.005f, L);
                Vector3 p4 = center + rot * new Vector3(W * 0.8f, 0.015f, L * 0.4f);
                Vector3 p5 = center + rot * new Vector3(W, 0.01f, -L * 0.2f);

                mb.AddQuad(p0, p1, p2, p3, uv);
                mb.AddQuad(p0, p3, p4, p5, uv);
                mb.AddQuad(p3, p2, p1, p0, uv);
                mb.AddQuad(p5, p4, p3, p0, uv);
            }
            // 2 twigs across top
            mb.AddBox(new Vector3(0f, 0.045f, 0f), new Vector3(0.018f, 0.018f, 0.26f), Quaternion.Euler(6f, 25f + variant * 15f, 0f), UV_Q3, UV_Q3, UV_Q3);
            mb.AddBox(new Vector3(0.01f, 0.052f, -0.01f), new Vector3(0.014f, 0.014f, 0.22f), Quaternion.Euler(-5f, -40f + variant * 15f, 0f), UV_Q3, UV_Q3, UV_Q3);
        }

        private static void BuildPaperNapkin(MeshBuilder mb, int variant)
        {
            // Crumpled multi-peak paper napkin (6x6 faceted grid, top & bottom = 144 tris)
            int N = 6;
            float size = 0.26f;
            Vector3[,] top = new Vector3[N, N];
            for (int z = 0; z < N; z++)
            {
                for (int x = 0; x < N; x++)
                {
                    float fx = ((float)x / (N - 1) - 0.5f) * size;
                    float fz = ((float)z / (N - 1) - 0.5f) * size;
                    float wave = Mathf.Sin((x + variant) * 1.7f) * Mathf.Cos((z + variant) * 1.5f) * 0.032f;
                    float arch = (1f - (fx * fx + fz * fz) / (size * size * 0.5f)) * 0.045f;
                    top[z, x] = new Vector3(fx, Mathf.Max(0.005f, arch + wave + 0.025f), fz);
                }
            }
            for (int z = 0; z < N - 1; z++)
            {
                for (int x = 0; x < N - 1; x++)
                {
                    Vector2 uv = ((x + z) == 4 || (x == 2 && z == 3)) ? UV_Q1 : UV_Q0;
                    mb.AddQuad(top[z, x], top[z + 1, x], top[z + 1, x + 1], top[z, x + 1], uv);
                    mb.AddQuad(top[z, x + 1] - Vector3.up * 0.006f, top[z + 1, x + 1] - Vector3.up * 0.006f, top[z + 1, x] - Vector3.up * 0.006f, top[z, x] - Vector3.up * 0.006f, UV_Q2);
                }
            }
        }

        private static void BuildCardboardSleeve(MeshBuilder mb, float crush)
        {
            // Tapered corrugated coffee cup sleeve with inner & outer walls (approx 160 tris)
            Quaternion rot = Quaternion.Euler(78f, 15f, 0f);
            float[] h = { 0f, 0.03f, 0.06f, 0.09f, 0.12f };
            float[] rOut = { 0.062f, 0.065f, 0.068f, 0.071f, 0.074f };
            Vector2[] uvs = { UV_Q0, UV_Q1, UV_Q2, UV_Q0, UV_Q0 };
            mb.AddLathe(Vector3.zero, rot, h, rOut, uvs, 14, true, true, crush * 0.72f);
        }

        private static void BuildWoodenFork(MeshBuilder mb, int variant)
        {
            // Handle + neck + head + 4 tines (approx 132 tris)
            Quaternion tilt = Quaternion.Euler(4f, variant * 20f, 0f);
            mb.AddBox(tilt * new Vector3(0f, 0.015f, -0.08f), new Vector3(0.026f, 0.010f, 0.16f), tilt, UV_Q0, UV_Q1, UV_Q0);
            mb.AddBox(tilt * new Vector3(0f, 0.016f, 0.01f), new Vector3(0.034f, 0.010f, 0.035f), tilt, UV_Q0, UV_Q1, UV_Q0);
            mb.AddBox(tilt * new Vector3(0f, 0.018f, 0.035f), new Vector3(0.056f, 0.010f, 0.028f), tilt, UV_Q0, UV_Q1, UV_Q0);
            for (int t = 0; t < 4; t++)
            {
                float x = -0.021f + t * 0.014f;
                mb.AddBox(tilt * new Vector3(x, 0.020f, 0.085f), new Vector3(0.008f, 0.009f, 0.075f), tilt * Quaternion.Euler(-4f, 0f, 0f), UV_Q1, UV_Q0, UV_Q0);
            }
            // Decorative eco-band on handle
            for (int b = 0; b < 3; b++)
            {
                mb.AddBox(tilt * new Vector3(0f, 0.016f, -0.11f + b * 0.022f), new Vector3(0.028f, 0.012f, 0.010f), tilt, UV_Q2, UV_Q2, UV_Q2);
            }
        }

        private static void BuildPlasticWaterBottle(MeshBuilder mb, float crush, float bend)
        {
            Quaternion rot = Quaternion.Euler(84f, 20f, 0f);
            float[] h = { 0f, 0.02f, 0.09f, 0.11f, 0.19f, 0.24f, 0.29f, 0.31f, 0.34f };
            float[] r = { 0.048f, 0.058f, 0.058f, 0.055f, 0.058f, 0.056f, 0.024f, 0.026f, 0.025f };
            Vector2[] uvs = { UV_Q0, UV_Q0, UV_Q1, UV_Q1, UV_Q0, UV_Q0, UV_Q0, UV_Q2, UV_Q2 };
            mb.AddLathe(Vector3.zero, rot, h, r, uvs, 14, true, true, crush, bend);
        }

        private static void BuildCrushedSodaCan(MeshBuilder mb, float crush, float bend)
        {
            Quaternion rot = Quaternion.Euler(82f, -25f, 0f);
            float[] h = { 0f, 0.015f, 0.06f, 0.11f, 0.16f, 0.20f, 0.215f };
            float[] r = { 0.048f, 0.058f, 0.056f * crush, 0.050f * crush, 0.057f, 0.052f, 0.046f };
            Vector2[] uvs = { UV_Q2, UV_Q0, UV_Q1, UV_Q0, UV_Q0, UV_Q2, UV_Q2 };
            mb.AddLathe(Vector3.zero, rot, h, r, uvs, 14, true, true, crush, bend);
            // Pull tab on top
            mb.AddBox(rot * new Vector3(0.01f, 0.22f, 0f), new Vector3(0.022f, 0.006f, 0.032f), rot, UV_Q3, UV_Q3, UV_Q3);
        }

        private static void BuildCardboardBox(MeshBuilder mb, int variant)
        {
            // Main carton body + 4 open angled top flaps + shipping tape stripe (approx 132 tris)
            mb.AddBox(new Vector3(0f, 0.08f, 0f), new Vector3(0.28f, 0.16f, 0.22f), Quaternion.identity, UV_Q1, UV_Q0, UV_Q0);
            // Tape strips on sides
            mb.AddBox(new Vector3(0f, 0.08f, 0.112f), new Vector3(0.06f, 0.15f, 0.006f), Quaternion.identity, UV_Q2, UV_Q2, UV_Q2);
            mb.AddBox(new Vector3(0f, 0.08f, -0.112f), new Vector3(0.06f, 0.15f, 0.006f), Quaternion.identity, UV_Q2, UV_Q2, UV_Q2);

            float openAng = 35f + variant * 12f;
            // 4 open flaps
            mb.AddBox(new Vector3(0f, 0.18f, 0.13f), new Vector3(0.27f, 0.010f, 0.09f), Quaternion.Euler(openAng, 0f, 0f), UV_Q0, UV_Q1, UV_Q1);
            mb.AddBox(new Vector3(0f, 0.18f, -0.13f), new Vector3(0.27f, 0.010f, 0.09f), Quaternion.Euler(-openAng, 0f, 0f), UV_Q0, UV_Q1, UV_Q1);
            mb.AddBox(new Vector3(0.16f, 0.175f, 0f), new Vector3(0.08f, 0.010f, 0.21f), Quaternion.Euler(0f, 0f, -openAng), UV_Q1, UV_Q0, UV_Q0);
            mb.AddBox(new Vector3(-0.16f, 0.175f, 0f), new Vector3(0.08f, 0.010f, 0.21f), Quaternion.Euler(0f, 0f, openAng), UV_Q1, UV_Q0, UV_Q0);

            // Shipping label on front
            mb.AddBox(new Vector3(0.06f, 0.09f, 0.113f), new Vector3(0.08f, 0.055f, 0.005f), Quaternion.identity, UV_Q3, UV_Q3, UV_Q3);
            mb.AddBox(new Vector3(-0.06f, 0.06f, -0.113f), new Vector3(0.07f, 0.045f, 0.005f), Quaternion.identity, UV_Q3, UV_Q3, UV_Q3);
        }

        private static void BuildNewspaperStack(MeshBuilder mb, int variant)
        {
            // Rolled newspaper tube + 3 folded inner layers + tying string band (approx 168 tris)
            Quaternion rot = Quaternion.Euler(90f, variant * 18f, 8f);
            float[] h = { -0.16f, -0.02f, 0.02f, 0.16f };
            float[] r = { 0.052f, 0.048f, 0.048f, 0.054f };
            Vector2[] uvs = { UV_Q0, UV_Q2, UV_Q0, UV_Q0 };
            mb.AddLathe(Vector3.zero, rot, h, r, uvs, 14, true, true, 0.62f, 0.01f);

            // Headline column blocks on top surface
            mb.AddBox(new Vector3(-0.04f, 0.065f, 0.02f), new Vector3(0.04f, 0.006f, 0.18f), Quaternion.Euler(0f, variant * 18f, 0f), UV_Q3, UV_Q3, UV_Q3);
            mb.AddBox(new Vector3(0.03f, 0.065f, -0.02f), new Vector3(0.035f, 0.006f, 0.14f), Quaternion.Euler(0f, variant * 18f, 0f), UV_Q1, UV_Q1, UV_Q1);
        }

        private static void BuildGlassJar(MeshBuilder mb, int variant)
        {
            Quaternion rot = (variant == 1) ? Quaternion.Euler(82f, 15f, 0f) : Quaternion.identity;
            float[] h = { 0f, 0.015f, 0.06f, 0.15f, 0.19f, 0.21f, 0.235f };
            float[] r = { 0.062f, 0.074f, 0.074f, 0.074f, 0.054f, 0.058f, 0.058f };
            Vector2[] uvs = { UV_Q0, UV_Q0, UV_Q1, UV_Q0, UV_Q0, UV_Q2, UV_Q2 };
            mb.AddLathe(Vector3.zero, rot, h, r, uvs, 14, true, true);
        }

        private static void BuildMilkCarton(MeshBuilder mb, float crush)
        {
            // Square base body + gable roof top + spout cap (approx 110 tris)
            float w = 0.065f * crush;
            float d = 0.065f;
            mb.AddBox(new Vector3(0f, 0.09f, 0f), new Vector3(w * 2f, 0.18f, d * 2f), Quaternion.identity, UV_Q0, UV_Q0, UV_Q0);
            // Blue/cream stripe bands
            mb.AddBox(new Vector3(0f, 0.08f, 0f), new Vector3(w * 2.04f, 0.065f, d * 2.04f), Quaternion.identity, UV_Q1, UV_Q1, UV_Q1);
            mb.AddBox(new Vector3(0f, 0.14f, 0f), new Vector3(w * 2.03f, 0.025f, d * 2.03f), Quaternion.identity, UV_Q2, UV_Q2, UV_Q2);

            // Gable roof prism
            Vector3 b0 = new Vector3(-w, 0.18f, -d);
            Vector3 b1 = new Vector3( w, 0.18f, -d);
            Vector3 b2 = new Vector3( w, 0.18f,  d);
            Vector3 b3 = new Vector3(-w, 0.18f,  d);
            Vector3 r0 = new Vector3(-w, 0.24f, 0f);
            Vector3 r1 = new Vector3( w, 0.24f, 0f);
            mb.AddQuad(b0, r0, r1, b1, UV_Q1);
            mb.AddQuad(b2, r1, r0, b3, UV_Q1);
            mb.AddTri(b0, b3, r0, UV_Q0);
            mb.AddTri(b1, r1, b2, UV_Q0);
            // Top sealing fin + round screw cap + front label panels (approx 134 tris)
            mb.AddBox(new Vector3(0f, 0.252f, 0f), new Vector3(w * 1.95f, 0.024f, 0.008f), Quaternion.identity, UV_Q2, UV_Q2, UV_Q2);
            mb.AddBox(new Vector3(0f, 0.09f, d * 1.03f), new Vector3(w * 1.3f, 0.085f, 0.006f), Quaternion.identity, UV_Q2, UV_Q2, UV_Q2);
            mb.AddBox(new Vector3(0f, 0.09f, -d * 1.03f), new Vector3(w * 1.3f, 0.085f, 0.006f), Quaternion.identity, UV_Q2, UV_Q2, UV_Q2);
            mb.AddLathe(new Vector3(0f, 0.205f, d * 0.45f), Quaternion.Euler(35f, 0f, 0f),
                new float[] { 0f, 0.012f, 0.024f }, new float[] { 0.019f, 0.020f, 0.018f }, new Vector2[] { UV_Q3, UV_Q3, UV_Q3 }, 14, true, true);
        }

        private static void BuildTinCan(MeshBuilder mb, float crush)
        {
            Quaternion rot = Quaternion.Euler(85f, 10f, 0f);
            float[] h = { 0f, 0.012f, 0.04f, 0.14f, 0.17f, 0.185f };
            float[] r = { 0.066f, 0.062f, 0.063f, 0.063f, 0.062f, 0.066f };
            Vector2[] uvs = { UV_Q2, UV_Q0, UV_Q1, UV_Q0, UV_Q2, UV_Q2 };
            mb.AddLathe(Vector3.zero, rot, h, r, uvs, 14, true, true, crush);
            // Open bent lid tilted at top
            mb.AddLathe(rot * new Vector3(0f, 0.21f, -0.045f), rot * Quaternion.Euler(-55f, 0f, 0f),
                new float[] { 0f, 0.006f }, new float[] { 0.058f, 0.058f }, new Vector2[] { UV_Q2, UV_Q3 }, 10, true, true);
        }

        private static void BuildFlyerStack(MeshBuilder mb, int variant)
        {
            // 8 fanned colorful paper flyer sheets (approx 112 tris)
            int sheets = 8;
            for (int i = 0; i < sheets; i++)
            {
                float yaw = (i - 3.5f) * (9f + variant * 2f);
                Vector3 pos = new Vector3((i - 3.5f) * 0.012f, 0.005f + i * 0.006f, (i % 2 - 0.5f) * 0.015f);
                Vector2 topUv = (i % 3 == 0) ? UV_Q0 : ((i % 3 == 1) ? UV_Q1 : UV_Q2);
                mb.AddBox(pos, new Vector3(0.20f, 0.005f, 0.26f), Quaternion.Euler(0f, yaw, 0f), topUv, UV_Q0, UV_Q0);
            }
            // Top flyer header banner
            mb.AddBox(new Vector3(0.02f, 0.056f, 0.04f), new Vector3(0.14f, 0.004f, 0.06f), Quaternion.Euler(0f, 25f, 0f), UV_Q3, UV_Q3, UV_Q3);
        }

        private static void BuildChipBag(MeshBuilder mb, int variant)
        {
            // Puffed foil pillow bag with crimped top/bottom ridges (approx 160 tris)
            int rings = 7;
            int segs = 12;
            float[] zPos = { -0.15f, -0.13f, -0.08f, 0f, 0.08f, 0.13f, 0.15f };
            float[] wX =   {  0.11f,  0.105f, 0.115f, 0.12f, 0.115f, 0.105f, 0.11f };
            float[] hY =   {  0.006f, 0.018f, 0.045f, 0.058f, 0.045f, 0.018f, 0.006f };

            Vector3[,] pts = new Vector3[rings, segs];
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segs; s++)
                {
                    float a = (float)s / segs * Mathf.PI * 2f;
                    float x = Mathf.Cos(a) * wX[r];
                    float y = Mathf.Sin(a) * hY[r] + 0.06f;
                    float crinkle = Mathf.Sin(r * 2f + s + variant) * 0.004f;
                    pts[r, s] = new Vector3(x + crinkle, y, zPos[r]);
                }
            }
            for (int r = 0; r < rings - 1; r++)
            {
                Vector2 uv = (r == 0 || r == rings - 2) ? UV_Q2 : ((r == 2 || r == 3) ? UV_Q1 : UV_Q0);
                for (int s = 0; s < segs; s++)
                {
                    int next = (s + 1) % segs;
                    mb.AddQuad(pts[r, s], pts[r + 1, s], pts[r + 1, next], pts[r, next], uv);
                }
            }
        }

        private static void BuildStyrofoamCup(MeshBuilder mb, float crush)
        {
            Quaternion rot = Quaternion.Euler(76f, -15f, 0f);
            float[] h = { 0f, 0.015f, 0.15f, 0.18f, 0.195f, 0.21f };
            float[] r = { 0.046f, 0.048f, 0.066f, 0.068f, 0.072f, 0.069f };
            Vector2[] uvs = { UV_Q0, UV_Q0, UV_Q1, UV_Q0, UV_Q2, UV_Q3 };
            mb.AddLathe(Vector3.zero, rot, h, r, uvs, 14, true, true, crush);
        }

        private static void BuildPlasticStrawWrapper(MeshBuilder mb, int variant)
        {
            // Bent drinking straw (2 angled cylinders + accordion bend) + crumpled paper wrapper next to it (approx 180 tris)
            Quaternion rot1 = Quaternion.Euler(90f, 15f + variant * 10f, 0f);
            mb.AddLathe(new Vector3(-0.02f, 0.015f, -0.04f), rot1,
                new float[] { 0f, 0.08f, 0.16f }, new float[] { 0.010f, 0.010f, 0.011f },
                new Vector2[] { UV_Q0, UV_Q1, UV_Q0 }, 8, true, true);

            Quaternion rot2 = Quaternion.Euler(90f, -28f + variant * 10f, 0f);
            mb.AddLathe(new Vector3(-0.02f, 0.015f, 0.11f), rot2,
                new float[] { 0f, 0.07f }, new float[] { 0.011f, 0.010f },
                new Vector2[] { UV_Q1, UV_Q0 }, 8, true, true);

            // Torn paper wrapper beside straw
            Quaternion rotW = Quaternion.Euler(88f, 5f, 0f);
            mb.AddLathe(new Vector3(0.035f, 0.012f, -0.05f), rotW,
                new float[] { 0f, 0.05f, 0.10f, 0.15f }, new float[] { 0.014f, 0.016f, 0.013f, 0.015f },
                new Vector2[] { UV_Q2, UV_Q2, UV_Q3, UV_Q2 }, 8, true, true, 0.55f);
        }

        private static void BuildCandyWrapper(MeshBuilder mb, int variant)
        {
            // Classic twisted-end candy wrapper: plump center + 2 flared crimped twist tails (approx 168 tris)
            Quaternion rot = Quaternion.Euler(90f, variant * 22f, 0f);
            float[] h = { -0.14f, -0.10f, -0.065f, -0.05f, 0f, 0.05f, 0.065f, 0.10f, 0.14f };
            float[] r = {  0.045f, 0.032f, 0.012f,  0.038f, 0.046f, 0.038f, 0.012f, 0.032f, 0.045f };
            Vector2[] uvs = { UV_Q2, UV_Q1, UV_Q3, UV_Q0, UV_Q1, UV_Q0, UV_Q3, UV_Q1, UV_Q2 };
            mb.AddLathe(Vector3.zero, rot, h, r, uvs, 12, true, true, 0.78f);
        }

        private static void BuildTornPlasticBag(MeshBuilder mb, int variant)
        {
            // Puffed crumpled grocery bag body + 2 loop handles on top (approx 196 tris)
            float[] h = { 0f, 0.03f, 0.09f, 0.15f, 0.19f };
            float[] r = { 0.08f, 0.115f, 0.125f, 0.095f, 0.065f };
            Vector2[] uvs = { UV_Q0, UV_Q0, UV_Q1, UV_Q0, UV_Q2 };
            mb.AddLathe(Vector3.zero, Quaternion.Euler(10f, variant * 30f, 8f), h, r, uvs, 14, true, true, 0.82f, 0.015f);

            // 2 loop handles
            for (int side = -1; side <= 1; side += 2)
            {
                mb.AddBox(new Vector3(side * 0.045f, 0.22f, 0.01f), new Vector3(0.022f, 0.07f, 0.014f), Quaternion.Euler(15f * side, 0f, -side * 18f), UV_Q0, UV_Q2, UV_Q0);
                mb.AddBox(new Vector3(side * 0.055f, 0.25f, 0.01f), new Vector3(0.035f, 0.018f, 0.016f), Quaternion.Euler(0f, 0f, side * 25f), UV_Q2, UV_Q0, UV_Q0);
            }
        }

        private static void BuildAABattery(MeshBuilder mb, int variant)
        {
            // Distinct cylindrical battery cell + positive terminal nipple + hazard band (approx 168 tris)
            Quaternion rot = Quaternion.Euler(86f, variant * 25f, 0f);
            float[] h = { 0f, 0.01f, 0.06f, 0.16f, 0.21f, 0.22f, 0.225f, 0.248f };
            float[] r = { 0.048f, 0.052f, 0.052f, 0.052f, 0.052f, 0.046f, 0.022f, 0.020f };
            Vector2[] uvs = { UV_Q2, UV_Q3, UV_Q0, UV_Q1, UV_Q1, UV_Q2, UV_Q2, UV_Q2 };
            mb.AddLathe(Vector3.zero, rot, h, r, uvs, 14, true, true);
        }

        private static void BuildSprayPaintCan(MeshBuilder mb, float crush)
        {
            // Tall aerosol cylinder + domed shoulder + spray nozzle button (approx 196 tris)
            Quaternion rot = Quaternion.Euler(82f, -20f, 0f);
            float[] h = { 0f, 0.015f, 0.08f, 0.20f, 0.25f, 0.285f, 0.295f, 0.325f };
            float[] r = { 0.056f, 0.058f, 0.058f, 0.058f, 0.056f, 0.038f, 0.018f, 0.016f };
            Vector2[] uvs = { UV_Q2, UV_Q0, UV_Q1, UV_Q0, UV_Q2, UV_Q2, UV_Q3, UV_Q1 };
            mb.AddLathe(Vector3.zero, rot, h, r, uvs, 14, true, true, crush);
        }

        private static void BuildBrokenLightBulb(MeshBuilder mb, int variant)
        {
            // Threaded metal screw base + neck + pear bulb globe (approx 196 tris)
            Quaternion rot = Quaternion.Euler(78f, variant * 20f, 0f);
            float[] h = { 0f, 0.015f, 0.065f, 0.09f, 0.13f, 0.18f, 0.23f, 0.26f };
            float[] r = { 0.018f, 0.034f, 0.036f, 0.032f, 0.064f, 0.078f, 0.062f, 0.025f };
            Vector2[] uvs = { UV_Q3, UV_Q2, UV_Q2, UV_Q1, UV_Q0, UV_Q0, UV_Q1, UV_Q0 };
            mb.AddLathe(Vector3.zero, rot, h, r, uvs, 14, true, true);
        }

        private static void BuildMedicineBlisterPack(MeshBuilder mb, int variant)
        {
            // Foil backing tray + 6 raised capsule/pill domes (approx 156 tris)
            Quaternion rot = Quaternion.Euler(6f, variant * 18f, 4f);
            mb.AddBox(new Vector3(0f, 0.008f, 0f), new Vector3(0.18f, 0.010f, 0.26f), rot, UV_Q2, UV_Q0, UV_Q2);

            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 2; c++)
                {
                    float x = (c == 0 ? -0.042f : 0.042f);
                    float z = -0.075f + r * 0.075f;
                    bool popped = (r == 1 && c == (variant % 2));
                    float h = popped ? 0.010f : 0.028f;
                    Vector2 uv = popped ? UV_Q3 : ((r + c) % 2 == 0 ? UV_Q1 : UV_Q0);
                    mb.AddBox(rot * new Vector3(x, 0.014f + h * 0.5f, z), new Vector3(0.042f, h, 0.052f), rot, uv, uv, UV_Q2);
                }
            }
        }

        private static void BuildSmallCircuitBoard(MeshBuilder mb, int variant)
        {
            // Green PCB board + gold edge connector + CPU chip + capacitors & heatsinks (approx 144 tris)
            Quaternion rot = Quaternion.Euler(4f, variant * 15f, 0f);
            // Main PCB
            mb.AddBox(new Vector3(0f, 0.010f, 0f), new Vector3(0.24f, 0.014f, 0.20f), rot, UV_Q0, UV_Q0, UV_Q0);
            // Gold edge fingers
            mb.AddBox(rot * new Vector3(0f, 0.011f, -0.098f), new Vector3(0.18f, 0.016f, 0.022f), rot, UV_Q1, UV_Q1, UV_Q1);
            // Central black IC chip
            mb.AddBox(rot * new Vector3(-0.02f, 0.024f, 0.01f), new Vector3(0.085f, 0.018f, 0.085f), rot, UV_Q3, UV_Q2, UV_Q3);
            // Silver heatsink
            mb.AddBox(rot * new Vector3(0.065f, 0.032f, 0.03f), new Vector3(0.050f, 0.034f, 0.065f), rot, UV_Q2, UV_Q2, UV_Q2);
            // 4 small capacitors/chips
            mb.AddBox(rot * new Vector3(-0.075f, 0.028f, -0.03f), new Vector3(0.028f, 0.028f, 0.028f), rot, UV_Q2, UV_Q3, UV_Q3);
            mb.AddBox(rot * new Vector3(-0.075f, 0.028f,  0.04f), new Vector3(0.028f, 0.028f, 0.028f), rot, UV_Q2, UV_Q3, UV_Q3);
            mb.AddBox(rot * new Vector3( 0.055f, 0.022f, -0.045f), new Vector3(0.045f, 0.016f, 0.032f), rot, UV_Q3, UV_Q1, UV_Q3);
            mb.AddBox(rot * new Vector3(-0.01f,  0.020f,  0.072f), new Vector3(0.065f, 0.014f, 0.022f), rot, UV_Q1, UV_Q3, UV_Q3);
            // 3 heatsink fins
            for (int f = 0; f < 3; f++)
            {
                mb.AddBox(rot * new Vector3(0.050f + f * 0.015f, 0.048f, 0.03f), new Vector3(0.006f, 0.016f, 0.062f), rot, UV_Q2, UV_Q2, UV_Q2);
            }
        }
    }
}
