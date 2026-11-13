using System;
using System.Collections.Generic;
using System.Numerics;
using Colo.Terrain;
using NUnit.Framework;

namespace Colo.Portfolio.Tests
{
    /// <summary>
    /// 3×3 个 8×8 格的区块拼成 24×24 的地形，比较两种法线算法在区块交界处的表现。
    /// </summary>
    public sealed class ChunkSeamNormalTests
    {
        private const int ChunkCells = 8;
        private const int ChunksPerSide = 3;
        private const float CellSize = 1f;

        /// <summary>几组不同方向和频率的正弦叠加，起伏明显且结果确定。</summary>
        private sealed class RollingHills : IVertexHeightSampler
        {
            public float SampleVertexHeight(int x, int z) =>
                2.0f * MathF.Sin(x * 0.37f)
                + 1.3f * MathF.Cos(z * 0.23f + x * 0.11f)
                + 0.5f * MathF.Sin((x + z) * 0.9f);
        }

        private static List<ChunkSurface> BuildChunks(IVertexHeightSampler sampler, bool sharedHeightField)
        {
            var chunks = new List<ChunkSurface>();
            for (int cz = 0; cz < ChunksPerSide; cz++)
            for (int cx = 0; cx < ChunksPerSide; cx++)
            {
                int ox = cx * ChunkCells, oz = cz * ChunkCells;
                var heights = ChunkNormals.SampleHeights(sampler, ox, oz, ChunkCells, ChunkCells);
                var normals = sharedHeightField
                    ? ChunkNormals.FromSharedHeightField(heights, ChunkCells, ChunkCells, ox, oz, CellSize, sampler)
                    : ChunkNormals.FromChunkTrianglesOnly(heights, ChunkCells, ChunkCells, CellSize);
                chunks.Add(new ChunkSurface(ox, oz, ChunkCells, ChunkCells, CellSize, heights, normals));
            }

            return chunks;
        }

        [Test]
        public void ChunkTrianglesOnly_NormalsDisagreeAtChunkBorders()
        {
            var report = ChunkSeams.Measure(BuildChunks(new RollingHills(), sharedHeightField: false));

            // 3×3 区块之间共有 4 条长 24 格的接缝线，每条 25 个顶点，交叉点重复计 4 次 → 4×25 - 4 = 96。
            Assert.That(report.SharedVertices, Is.EqualTo(96));
            Assert.That(report.MaxDegrees, Is.GreaterThan(5f), "每块独立求法线时，边界两侧只看到各自的三角形，应出现明显夹角。");
        }

        [Test]
        public void SharedHeightField_NormalsMatchAcrossChunkBorders()
        {
            var report = ChunkSeams.Measure(BuildChunks(new RollingHills(), sharedHeightField: true));

            Assert.That(report.SharedVertices, Is.EqualTo(96));
            Assert.That(report.MaxDegrees, Is.LessThan(1e-3f), "相邻区块在同一顶点读到同一组高度，法线应完全一致。");
        }

        [Test]
        public void SharedHeightField_SplitIntoChunks_EqualsOneLargeChunk()
        {
            var sampler = new RollingHills();
            int size = ChunkCells * ChunksPerSide;
            var whole = ChunkNormals.FromSharedHeightField(
                ChunkNormals.SampleHeights(sampler, 0, 0, size, size), size, size, 0, 0, CellSize, sampler);

            foreach (var chunk in BuildChunks(sampler, sharedHeightField: true))
            {
                int vw = chunk.Width + 1;
                for (int i = 0; i < chunk.Normals.Length; i++)
                {
                    int wx = chunk.OriginX + i % vw, wz = chunk.OriginZ + i / vw;
                    var expected = whole[wz * (size + 1) + wx];
                    Assert.That(ChunkSeams.AngleDegrees(chunk.Normals[i], expected), Is.LessThan(1e-3f),
                        $"顶点 ({wx}, {wz}) 的法线应与整块地形一致：分块不改变光照。");
                }
            }
        }

        [Test]
        public void FlatGround_BothMethodsPointStraightUp()
        {
            var flat = new FlatSampler();
            var heights = ChunkNormals.SampleHeights(flat, 0, 0, 4, 4);
            foreach (var n in ChunkNormals.FromSharedHeightField(heights, 4, 4, 0, 0, CellSize, flat))
                Assert.That(ChunkSeams.AngleDegrees(n, Vector3.UnitY), Is.LessThan(1e-4f));
            foreach (var n in ChunkNormals.FromChunkTrianglesOnly(heights, 4, 4, CellSize))
                Assert.That(ChunkSeams.AngleDegrees(n, Vector3.UnitY), Is.LessThan(1e-4f), "三角形绕序应让法线朝上。");
        }

        private sealed class FlatSampler : IVertexHeightSampler
        {
            public float SampleVertexHeight(int x, int z) => 3f;
        }
    }
}
