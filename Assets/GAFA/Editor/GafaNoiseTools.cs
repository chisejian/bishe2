using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GAFA 噪声贴图（Assets/GAFA/Textures/GafaNoise.png）：逐位移植 TestGAFA/src/textures.js 的 createNoise(512)。
/// R 低频 fbm，G 高频 fbm，B Worley F1，A 中频 fbm；插画材质、天空和后期都用它。
/// </summary>
public static class GafaNoiseTools
{
    const string NoisePath = "Assets/GAFA/Textures/GafaNoise.png";

    // ================================================================ 1. 噪声贴图
    [MenuItem("GAFA/Illustrated/Generate Noise Texture")]
    public static void GenerateNoise()
    {
        const int size = 512;
        Directory.CreateDirectory(Path.GetDirectoryName(NoisePath));
        try
        {
            EditorUtility.DisplayProgressBar("GAFA", "生成噪声…", 0.1f);
            byte[] rgba = CreateNoise(size);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.SetPixelData(rgba, 0);
            tex.Apply(false);
            File.WriteAllBytes(NoisePath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.ImportAsset(NoisePath, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(NoisePath);
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = false;             // 数据贴图，线性
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = false;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.filterMode = FilterMode.Trilinear;
        ti.anisoLevel = 8;
        ti.mipmapEnabled = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.SaveAndReimport();
        Debug.Log($"[GAFA] 噪声贴图已生成：{NoisePath}");
    }

    // ---- 以下为 textures.js 的逐位移植（Math.imul / >>> 语义）
    static double LHash(int ix, int iy, int seed)
    {
        unchecked
        {
            int h = (ix * 0x27d4eb2d) ^ (iy * 0x165667b1) ^ ((seed + 1) * 0x1b873593);
            h = (h ^ (int)((uint)h >> 15)) * (int)0x85ebca6b;
            h = (h ^ (int)((uint)h >> 13)) * (int)0xc2b2ae35;
            h ^= (int)((uint)h >> 16);
            return (uint)h / 4294967296.0;
        }
    }

    static int Wrap(int v, int p) => ((v % p) + p) % p;

    static double PNoise(double x, double y, int period, int seed)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double xf = x - xi, yf = y - yi;
        double u = xf * xf * xf * (xf * (xf * 6 - 15) + 10);
        double v = yf * yf * yf * (yf * (yf * 6 - 15) + 10);
        int x0 = Wrap(xi, period), x1 = (x0 + 1) % period;
        int y0 = Wrap(yi, period), y1 = (y0 + 1) % period;
        double a = LHash(x0, y0, seed), b = LHash(x1, y0, seed), c = LHash(x0, y1, seed), d = LHash(x1, y1, seed);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    static double PFbm(double x, double y, int period, int oct, int seed)
    {
        double s = 0, a = 0.5, n = 0, f = 1;
        int p = period;
        for (int o = 0; o < oct; o++)
        {
            s += a * PNoise(x * f, y * f, p, seed + o * 31);
            n += a;
            a *= 0.5;
            f *= 2;
            p *= 2;
        }
        return s / n;
    }

    static void Stretch(float[] arr)
    {
        var sorted = (float[])arr.Clone();
        Array.Sort(sorted);
        float lo = sorted[(int)Math.Floor(sorted.Length * 0.01)];
        float hi = sorted[(int)Math.Floor(sorted.Length * 0.99)];
        double k = 1.0 / Math.Max(1e-6, hi - lo);
        for (int i = 0; i < arr.Length; i++) arr[i] = (float)Math.Min(1, Math.Max(0, (arr[i] - lo) * k));
    }

    /// R: 低频 fbm   G: 高频 fbm   B: Worley F1   A: 中频 fbm
    static byte[] CreateNoise(int size)
    {
        int n = size * size;
        var R = new float[n];
        var G = new float[n];
        var B = new float[n];
        var A = new float[n];
        const int cells = 14;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                double u = (double)x / size, v = (double)y / size;
                R[i] = (float)PFbm(u * 4, v * 4, 4, 5, 1);
                G[i] = (float)PFbm(u * 16, v * 16, 16, 4, 2);
                A[i] = (float)PFbm(u * 8, v * 8, 8, 4, 3);
                double px = u * cells, py = v * cells;
                int cx = (int)Math.Floor(px), cy = (int)Math.Floor(py);
                double dmin = 9;
                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int gx = cx + ox, gy = cy + oy;
                        int wx = Wrap(gx, cells), wy = Wrap(gy, cells);
                        double fx = gx + LHash(wx, wy, 91), fy = gy + LHash(wx, wy, 92);
                        double dd = Math.Sqrt((px - fx) * (px - fx) + (py - fy) * (py - fy));
                        if (dd < dmin) dmin = dd;
                    }
                }
                B[i] = (float)dmin;
            }
        }
        Stretch(R);
        Stretch(G);
        Stretch(A);
        Stretch(B);
        // three.js DataTexture 不翻转 Y：第 0 行对应 v = 0，与 Unity 的像素行顺序一致
        var data = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            data[i * 4] = (byte)(R[i] * 255.0);
            data[i * 4 + 1] = (byte)(G[i] * 255.0);
            data[i * 4 + 2] = (byte)(B[i] * 255.0);
            data[i * 4 + 3] = (byte)(A[i] * 255.0);
        }
        return data;
    }
}
