// 生成 Docs/chunk-seam.png：同一片地形、同一个光照，左边每个区块只用自己的三角形求法线，
// 右边用共享高度场求法线（游戏的做法）。法线全部由 Assets/Scripts/Terrain/ChunkNormals.cs 计算。
// 用法（仓库根目录）：dotnet run --project Tools/SeamFigure -- Docs/chunk-seam.png
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using Colo.Terrain;

const int Chunks = 4, ChunkCells = 8, Px = 12, Gap = 16, Margin = 8;
const float CellSize = 1f;
int cells = Chunks * ChunkCells;
int panel = cells * Px;
int width = Margin * 2 + panel * 2 + Gap, height = Margin * 2 + panel;
var rgb = new byte[width * height * 3];
for (int i = 0; i < rgb.Length; i++) rgb[i] = 24;

var sampler = new FbmSampler(seed: 20251225);
var light = Vector3.Normalize(new Vector3(-0.8f, 0.55f, 0.35f)); // 低角度侧光，让法线差异更明显

for (int pass = 0; pass < 2; pass++)
{
    bool shared = pass == 1;
    int left = Margin + pass * (panel + Gap);
    var surfaces = new List<ChunkSurface>();
    for (int cz = 0; cz < Chunks; cz++)
    for (int cx = 0; cx < Chunks; cx++)
    {
        int ox = cx * ChunkCells, oz = cz * ChunkCells;
        var h = ChunkNormals.SampleHeights(sampler, ox, oz, ChunkCells, ChunkCells);
        var n = shared
            ? ChunkNormals.FromSharedHeightField(h, ChunkCells, ChunkCells, ox, oz, CellSize, sampler)
            : ChunkNormals.FromChunkTrianglesOnly(h, ChunkCells, ChunkCells, CellSize);
        surfaces.Add(new ChunkSurface(ox, oz, ChunkCells, ChunkCells, CellSize, h, n));
    }

    var report = ChunkSeams.Measure(surfaces);
    Console.WriteLine($"{(shared ? "共享高度场" : "每块独立  ")}: 接缝顶点 {report.SharedVertices}，最大夹角 {report.MaxDegrees:F3}°，平均 {report.MeanDegrees:F3}°");

    foreach (var s in surfaces)
    {
        int vw = s.Width + 1;
        for (int py = 0; py < s.Height * Px; py++)
        for (int px = 0; px < s.Width * Px; px++)
        {
            float fx = (px + 0.5f) / Px, fz = (py + 0.5f) / Px;
            int x = (int)fx, z = (int)fz;
            float u = fx - x, v = fz - z;
            int v00 = z * vw + x, v10 = v00 + 1, v01 = v00 + vw, v11 = v01 + 1;
            // 与 BuildMesh 相同的三角形划分：(v00, v01, v11) 与 (v00, v11, v10)，在三角形内按重心坐标插值法线。
            Vector3 nrm; float hgt;
            if (v >= u) { nrm = s.Normals[v00] * (1 - v) + s.Normals[v01] * (v - u) + s.Normals[v11] * u; hgt = s.Heights[v00] * (1 - v) + s.Heights[v01] * (v - u) + s.Heights[v11] * u; }
            else { nrm = s.Normals[v00] * (1 - u) + s.Normals[v10] * (u - v) + s.Normals[v11] * v; hgt = s.Heights[v00] * (1 - u) + s.Heights[v10] * (u - v) + s.Heights[v11] * v; }
            nrm = Vector3.Normalize(nrm);
            float lambert = MathF.Max(0f, Vector3.Dot(nrm, light));
            float shade = 0.25f + 0.85f * lambert;
            float t = Math.Clamp((hgt + 4f) / 8f, 0f, 1f); // 低处偏绿、高处偏土黄
            var baseColor = Vector3.Lerp(new Vector3(86, 124, 74), new Vector3(168, 150, 104), t);
            var c = Vector3.Clamp(baseColor * shade, Vector3.Zero, new Vector3(255));
            int ix = left + (s.OriginX * Px) + px;
            int iy = Margin + panel - 1 - (s.OriginZ * Px + py); // z 朝上
            int o = (iy * width + ix) * 3;
            rgb[o] = (byte)c.X; rgb[o + 1] = (byte)c.Y; rgb[o + 2] = (byte)c.Z;
        }
    }

    // 在面板外沿画出区块边界的刻度，方便对照接缝位置。
    for (int k = 1; k < Chunks; k++)
    for (int d = 0; d < Margin - 2; d++)
    {
        Put(left + k * ChunkCells * Px, d, 200);
        Put(left + k * ChunkCells * Px, height - 1 - d, 200);
        if (pass == 0) Put(d, Margin + k * ChunkCells * Px, 200);
        else Put(width - 1 - d, Margin + k * ChunkCells * Px, 200);
    }
}

var output = args.Length > 0 ? args[0] : "chunk-seam.png";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllBytes(output, Png(width, height, rgb));
Console.WriteLine($"已写入 {output}（{width}×{height}）");

void Put(int x, int y, byte g)
{
    int o = (y * width + x) * 3;
    rgb[o] = rgb[o + 1] = rgb[o + 2] = g;
}

static byte[] Png(int w, int h, byte[] rgb)
{
    using var raw = new MemoryStream();
    for (int y = 0; y < h; y++) { raw.WriteByte(0); raw.Write(rgb, y * w * 3, w * 3); }
    using var z = new MemoryStream();
    using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) raw.WriteTo(zs);

    using var png = new MemoryStream();
    png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
    var ihdr = new byte[13];
    BE(ihdr, 0, w); BE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 2;
    Chunk(png, "IHDR", ihdr);
    Chunk(png, "IDAT", z.ToArray());
    Chunk(png, "IEND", Array.Empty<byte>());
    return png.ToArray();

    static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, td, 4, data.Length);
        s.Write(td);
        var crc = new byte[4]; BE(crc, 0, (int)Crc(td)); s.Write(crc);
    }
    static uint Crc(byte[] d)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in d) { c ^= b; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; }
        return c ^ 0xFFFFFFFF;
    }
}

/// <summary>确定性的值噪声 fBm，代替游戏里的 Mathf.PerlinNoise（那个只能在 Unity 里调用）。</summary>
sealed class FbmSampler : IVertexHeightSampler
{
    private readonly int _seed;
    public FbmSampler(int seed) => _seed = seed;

    public float SampleVertexHeight(int x, int z)
    {
        float sum = 0f, amp = 3.2f, freq = 1f / 11f;
        for (int o = 0; o < 4; o++) { sum += amp * (Value(x * freq, z * freq, o) * 2f - 1f); amp *= 0.5f; freq *= 2.1f; }
        return sum;
    }

    private float Value(float x, float z, int octave)
    {
        int x0 = (int)MathF.Floor(x), z0 = (int)MathF.Floor(z);
        float tx = Smooth(x - x0), tz = Smooth(z - z0);
        float a = Hash(x0, z0, octave), b = Hash(x0 + 1, z0, octave), c = Hash(x0, z0 + 1, octave), d = Hash(x0 + 1, z0 + 1, octave);
        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), tz);
    }

    private float Hash(int x, int z, int o)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + z * 668265263 + o * 1442695041 + _seed * 2246822519);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
