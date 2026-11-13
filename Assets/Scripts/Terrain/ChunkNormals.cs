using System;
using System.Collections.Generic;
using System.Numerics;

namespace Colo.Terrain
{
    /// <summary>
    /// 按世界顶点坐标返回地形高度。游戏里由域扭曲 Perlin 噪声和群系插值实现
    /// （见 Reference/ChunkMeshGenerator.cs 的 SampleHeight / SurfaceTerrainSiteData.SampleVertexHeight）。
    /// 作品集把它抽象成接口，测试里用确定性的解析函数代替。
    /// </summary>
    public interface IVertexHeightSampler
    {
        /// <param name="x">世界顶点坐标 X（格）。</param>
        /// <param name="z">世界顶点坐标 Z（格）。</param>
        /// <returns>高度（米）。</returns>
        float SampleVertexHeight(int x, int z);
    }

    /// <summary>一个区块的顶点高度与法线。顶点按 z * (Width + 1) + x 排列。</summary>
    public sealed class ChunkSurface
    {
        public ChunkSurface(int originX, int originZ, int width, int height, float cellSize, float[] heights, Vector3[] normals)
        {
            OriginX = originX;
            OriginZ = originZ;
            Width = width;
            Height = height;
            CellSize = cellSize;
            Heights = heights;
            Normals = normals;
        }

        public int OriginX { get; }
        public int OriginZ { get; }
        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public float[] Heights { get; }
        public Vector3[] Normals { get; }
    }

    /// <summary>
    /// 区块法线的两种算法。
    /// <para>
    /// 每个区块单独建网格。如果各块只用自己的三角形求法线（Unity 的 Mesh.RecalculateNormals 就是这样），
    /// 边界顶点只能看到一侧的三角形，相邻两块在同一点算出的法线不同，光照会沿区块边界出现折线。
    /// </para>
    /// <para>
    /// 游戏的做法（ChunkMeshGenerator.BuildSeamlessNormals）：用中心差分从高度场直接求法线，
    /// 区块内部取已生成的高度，边界向区块外再采样一格。相邻两块在同一顶点读到的是同一组高度，法线完全一致。
    /// </para>
    /// </summary>
    public static class ChunkNormals
    {
        /// <summary>采样区块的 (width + 1) × (height + 1) 个顶点高度。</summary>
        public static float[] SampleHeights(IVertexHeightSampler sampler, int originX, int originZ, int width, int height)
        {
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "区块至少 1×1 格。");

            int vw = width + 1, vh = height + 1;
            var heights = new float[vw * vh];
            for (int z = 0; z < vh; z++)
            for (int x = 0; x < vw; x++)
                heights[z * vw + x] = sampler.SampleVertexHeight(originX + x, originZ + z);
            return heights;
        }

        /// <summary>
        /// 共享高度场法线，对应游戏 ChunkMeshGenerator.BuildSeamlessNormals 的地块分支，公式逐项一致：
        /// n = normalize(hL - hR, 2 * cellSize, hD - hU)。
        /// </summary>
        public static Vector3[] FromSharedHeightField(float[] heights, int width, int height, int originX, int originZ,
            float cellSize, IVertexHeightSampler sampler)
        {
            int vw = width + 1, vh = height + 1;
            if (heights == null || heights.Length != vw * vh) throw new ArgumentException("高度数组与区块尺寸不符。", nameof(heights));

            var norms = new Vector3[vw * vh];
            for (int z = 0; z < vh; z++)
            for (int x = 0; x < vw; x++)
            {
                int sx = originX + x;
                int sy = originZ + z;
                float hL = x > 0 ? heights[z * vw + (x - 1)] : sampler.SampleVertexHeight(sx - 1, sy);
                float hR = x < vw - 1 ? heights[z * vw + (x + 1)] : sampler.SampleVertexHeight(sx + 1, sy);
                float hD = z > 0 ? heights[(z - 1) * vw + x] : sampler.SampleVertexHeight(sx, sy - 1);
                float hU = z < vh - 1 ? heights[(z + 1) * vw + x] : sampler.SampleVertexHeight(sx, sy + 1);

                norms[z * vw + x] = Vector3.Normalize(new Vector3(hL - hR, 2f * cellSize, hD - hU));
            }

            return norms;
        }

        /// <summary>
        /// 只用本区块三角形求顶点法线（相邻面法线取平均），即每块独立 RecalculateNormals 的效果。
        /// 三角形划分与游戏 BuildMesh 相同：(v00, v01, v11) 与 (v00, v11, v10)。用作对照组。
        /// </summary>
        public static Vector3[] FromChunkTrianglesOnly(float[] heights, int width, int height, float cellSize)
        {
            int vw = width + 1, vh = height + 1;
            if (heights == null || heights.Length != vw * vh) throw new ArgumentException("高度数组与区块尺寸不符。", nameof(heights));

            var sum = new Vector3[vw * vh];
            Vector3 P(int i) => new Vector3(i % vw * cellSize, heights[i], i / vw * cellSize);

            void AddFace(int a, int b, int c)
            {
                var pa = P(a);
                var face = Vector3.Normalize(Vector3.Cross(P(b) - pa, P(c) - pa));
                sum[a] += face;
                sum[b] += face;
                sum[c] += face;
            }

            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                int v00 = z * vw + x, v10 = v00 + 1, v01 = v00 + vw, v11 = v01 + 1;
                AddFace(v00, v01, v11);
                AddFace(v00, v11, v10);
            }

            for (int i = 0; i < sum.Length; i++) sum[i] = Vector3.Normalize(sum[i]);
            return sum;
        }
    }

    /// <summary>接缝度量结果。</summary>
    public sealed class SeamReport
    {
        /// <summary>被两个及以上区块共有的边界顶点数。</summary>
        public int SharedVertices;

        /// <summary>共有顶点上各区块法线的最大夹角（度）。</summary>
        public float MaxDegrees;

        /// <summary>共有顶点上的平均夹角（度）。</summary>
        public float MeanDegrees;
    }

    /// <summary>
    /// 比较相邻区块在同一世界顶点上的法线夹角。对应游戏里的 ChunkSeamMetrics.Measure，
    /// 游戏版本按 0.01 米量化的世界坐标匹配顶点，这里区块对齐整格，直接用整数顶点坐标匹配。
    /// </summary>
    public static class ChunkSeams
    {
        public static SeamReport Measure(IReadOnlyList<ChunkSurface> chunks)
        {
            var normalsAt = new Dictionary<(int, int), List<Vector3>>();
            foreach (var c in chunks)
            {
                int vw = c.Width + 1, vh = c.Height + 1;
                for (int z = 0; z < vh; z++)
                for (int x = 0; x < vw; x++)
                {
                    if (x != 0 && x != vw - 1 && z != 0 && z != vh - 1) continue;
                    var key = (c.OriginX + x, c.OriginZ + z);
                    if (!normalsAt.TryGetValue(key, out var list)) normalsAt[key] = list = new List<Vector3>(2);
                    list.Add(c.Normals[z * vw + x]);
                }
            }

            var report = new SeamReport();
            double sum = 0;
            foreach (var list in normalsAt.Values)
            {
                if (list.Count < 2) continue;
                float worst = 0f;
                for (int a = 0; a < list.Count; a++)
                for (int b = a + 1; b < list.Count; b++)
                    worst = Math.Max(worst, AngleDegrees(list[a], list[b]));
                report.SharedVertices++;
                report.MaxDegrees = Math.Max(report.MaxDegrees, worst);
                sum += worst;
            }

            report.MeanDegrees = report.SharedVertices > 0 ? (float)(sum / report.SharedVertices) : 0f;
            return report;
        }

        /// <summary>两向量夹角（度）。用 atan2(|a×b|, a·b) 并在 double 下计算：小角度时 acos 在 float 精度下误差约 0.02°。</summary>
        public static float AngleDegrees(Vector3 a, Vector3 b)
        {
            double cx = (double)a.Y * b.Z - (double)a.Z * b.Y;
            double cy = (double)a.Z * b.X - (double)a.X * b.Z;
            double cz = (double)a.X * b.Y - (double)a.Y * b.X;
            double dot = (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;
            return (float)(Math.Atan2(Math.Sqrt(cx * cx + cy * cy + cz * cz), dot) * 180.0 / Math.PI);
        }
    }
}
