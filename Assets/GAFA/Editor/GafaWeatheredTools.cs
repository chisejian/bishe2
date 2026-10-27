using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GAFA 风化材质工具：
///   GAFA/1. 生成噪声贴图      —— 逐位移植 TestGAFA/src/textures.js 的 createNoise(512)
///   GAFA/2. 创建 / 更新风化材质 —— 按 TestGAFA/src/main.js 的材质预设生成 .mat
///   GAFA/3. 替换选中模型的材质  —— 按节点名（concrete、paint…）或原材质颜色把 glb 材质换成风化材质
/// </summary>
public static class GafaWeatheredTools
{
    const string Root = "Assets/GAFA";
    const string NoisePath = Root + "/Textures/GafaNoise.png";
    const string MatDir = Root + "/Materials";
    const string ShaderName = "GAFA/Weathered";

    enum Kind { Concrete, Rust, Bark, Pavers, Soil, Wood }

    class Preset
    {
        public string name;
        public Kind kind = Kind.Concrete;
        public string color = "#a29d92";
        public string paint;
        public float paintAmount = 0.7f, moss = 1, streaks = 1, dirt = 1, roughness = 0.92f, metalness = 0, scale = 1, seed = 0, decal = 1;
        public float? bump;
        public bool decalMap;
        public Vector4 rect = new Vector4(-1e4f, 1e4f, -1e4f, 1e4f);
    }

    // 与 TestGAFA/src/main.js 中的 mats 一一对应
    static readonly Preset[] Presets =
    {
        new Preset { name = "concrete", color = "#a39e93", seed = 0f },
        new Preset { name = "paint", color = "#a39e93", paint = "#bb6640", paintAmount = 0.72f, moss = 0.85f, streaks = 1.1f, seed = 3.1f },
        new Preset { name = "wall", color = "#a8a296", paint = "#c6bead", paintAmount = 0.6f, moss = 0.5f, streaks = 0.85f, seed = 7.7f },
        new Preset { name = "interior", color = "#6a655d", moss = 0.2f, streaks = 0.4f, dirt = 0.5f, seed = 1.7f },
        new Preset { name = "stone", color = "#b2ada2", moss = 0.75f, streaks = 0.5f, roughness = 0.86f, seed = 5.5f },
        new Preset { name = "plaster", color = "#efeadf", moss = 1.3f, streaks = 0.7f, roughness = 0.82f, seed = 9.1f, bump = 0.006f, scale = 2f },
        new Preset { name = "soil", kind = Kind.Soil, roughness = 1f },
        new Preset { name = "frame", kind = Kind.Rust, paint = "#38423e", paintAmount = 0.6f, metalness = 0.45f, roughness = 0.72f, seed = 2.2f },
        new Preset { name = "rust", kind = Kind.Rust, metalness = 0.4f, roughness = 0.8f, seed = 4.4f },
        new Preset { name = "lamp", kind = Kind.Rust, paint = "#2a302d", paintAmount = 0.85f, metalness = 0.5f, roughness = 0.65f, seed = 8.2f },
        new Preset { name = "bike", kind = Kind.Rust, paint = "#e0a91c", paintAmount = 1f, metalness = 0.35f, roughness = 0.6f, seed = 6.6f, scale = 3f },
        new Preset { name = "wood", kind = Kind.Wood, color = "#8c6d4a", moss = 0.5f, streaks = 0.3f, roughness = 0.9f, seed = 3.3f },
        new Preset { name = "painting", color = "#d4cdb9", decalMap = true, moss = 0.15f, streaks = 0.5f, roughness = 0.92f, decal = 1f },
        new Preset { name = "mural", color = "#aaa498", decalMap = true, moss = 0.35f, streaks = 1f, roughness = 0.9f, decal = 0.95f, seed = 2.9f },
        new Preset { name = "bark", kind = Kind.Bark, color = "#8d8576", moss = 0.8f, roughness = 0.92f },
        // SITE.plaza = { x0: -32, x1: 32, z1: 40 }
        new Preset { name = "plaza", kind = Kind.Pavers, color = "#9d988d", rect = new Vector4(-32, 32, -10, 40), roughness = 0.82f },
        new Preset { name = "podium", kind = Kind.Pavers, color = "#99938a", rect = new Vector4(-38, 38, -60, 7), roughness = 0.85f },
    };

    static float DefaultBump(Kind k) => k switch
    {
        Kind.Concrete => 0.012f,
        Kind.Rust => 0.01f,
        Kind.Bark => 0.025f,
        Kind.Wood => 0.01f,
        _ => 1f, // pavers / soil
    };

    // 节点名为 "Static" 的网格没有材质名，只能按 glb 导出的 baseColor（线性）+ roughness 识别
    static readonly (Color color, float roughness, string preset)[] StaticSignatures =
    {
        (new Color(0.3368f, 0.3140f, 0.2664f), 0.82f, "plaza"),
        (new Color(0.3185f, 0.2918f, 0.2542f), 0.85f, "podium"),
        (new Color(0.2664f, 0.2346f, 0.1812f), 0.92f, "bark"),
        (new Color(0.1683f, 0.1676f, 0.1439f), 1.00f, "soil"),
    };

    // ================================================================ 1. 噪声贴图
    [MenuItem("GAFA/1. Generate Noise Texture")]
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

    // ================================================================ 2. 材质
    [MenuItem("GAFA/2. Create or Update Weathered Materials")]
    public static void CreateMaterials()
    {
        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[GAFA] 找不到着色器 {ShaderName}");
            return;
        }
        var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
        if (noise == null)
        {
            GenerateNoise();
            noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
        }
        Directory.CreateDirectory(MatDir);

        foreach (var p in Presets)
        {
            string path = $"{MatDir}/GAFA_{p.name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(shader);
            else mat.shader = shader;
            ApplyPreset(mat, p, noise);
            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[GAFA] 已创建 / 更新 {Presets.Length} 个风化材质：{MatDir}");
    }

    static void ApplyPreset(Material m, Preset p, Texture noise)
    {
        foreach (Kind k in Enum.GetValues(typeof(Kind))) m.DisableKeyword("_KIND_" + k.ToString().ToUpperInvariant());
        m.SetFloat("_Kind", (int)p.kind);
        m.EnableKeyword("_KIND_" + p.kind.ToString().ToUpperInvariant());

        m.SetColor("_BaseColor", Html(p.color));
        bool usePaint = !string.IsNullOrEmpty(p.paint);
        m.SetFloat("_UsePaint", usePaint ? 1 : 0);
        SetKeyword(m, "_USE_PAINT", usePaint);
        m.SetColor("_PaintColor", usePaint ? Html(p.paint) : Color.black);
        m.SetFloat("_PaintAmount", p.paintAmount);
        m.SetFloat("_Moss", p.moss);
        m.SetFloat("_Streaks", p.streaks);
        m.SetFloat("_Dirt", p.dirt);
        m.SetFloat("_Roughness", p.roughness);
        m.SetFloat("_Metallic", p.metalness);
        m.SetFloat("_Scale", p.scale);
        m.SetFloat("_Bump", p.bump ?? DefaultBump(p.kind));
        m.SetFloat("_Seed", p.seed);
        m.SetVector("_Rect", p.rect);
        m.SetFloat("_Decal", p.decal);
        m.SetTexture("_NoiseTex", noise);
        // 贴花贴图在“替换材质”时从 glb 原材质拷过来；没有贴图时不开启
        bool hasDecal = p.decalMap && m.GetTexture("_DecalMap") != null;
        m.SetFloat("_UseDecal", hasDecal ? 1 : 0);
        SetKeyword(m, "_GAFA_DECAL", hasDecal);
    }

    static void SetKeyword(Material m, string kw, bool on)
    {
        if (on) m.EnableKeyword(kw);
        else m.DisableKeyword(kw);
    }

    static Color Html(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

    // ================================================================ 3. 替换
    [MenuItem("GAFA/3. Apply Weathered Materials To Selection")]
    public static void ApplyToSelection()
    {
        var roots = Selection.gameObjects;
        if (roots.Length == 0)
        {
            EditorUtility.DisplayDialog("GAFA", "请先在 Hierarchy 中选中导入的 GAFA 模型根节点（或其预制体）。", "OK");
            return;
        }
        var mats = LoadMaterials();
        if (mats == null) return;

        int replaced = 0, skipped = 0;
        var unknown = new HashSet<string>();
        foreach (var root in roots)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                string key = NodeKey(r.gameObject.name);
                var shared = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    var src = shared[i];
                    if (src != null && src.shader != null && src.shader.name == ShaderName) continue;
                    string presetName = mats.ContainsKey(key) ? key : MatchStaticSignature(src);
                    if (presetName == null)
                    {
                        skipped++;
                        unknown.Add(key);
                        continue;
                    }
                    var dst = mats[presetName];
                    CopyDecalTexture(src, dst);
                    shared[i] = dst;
                    changed = true;
                    replaced++;
                }
                if (changed)
                {
                    Undo.RecordObject(r, "Apply GAFA Weathered Materials");
                    r.sharedMaterials = shared;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                }
            }

            if (root.GetComponent<GafaSiteSpace>() == null) Undo.AddComponent<GafaSiteSpace>(root);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[GAFA] 已替换 {replaced} 个材质槽，跳过 {skipped} 个（植被等非风化材质）。跳过的节点类别：{string.Join(", ", unknown)}");
    }

    static Dictionary<string, Material> LoadMaterials()
    {
        var dict = new Dictionary<string, Material>();
        foreach (var p in Presets)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/GAFA_{p.name}.mat");
            if (m == null)
            {
                CreateMaterials();
                m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/GAFA_{p.name}.mat");
            }
            if (m == null)
            {
                Debug.LogError("[GAFA] 无法创建风化材质");
                return null;
            }
            dict[p.name] = m;
        }
        return dict;
    }

    // "concrete" / "concrete_2" / "concrete (1)" → "concrete"
    static string NodeKey(string name) => Regex.Replace(name, @"(_\d+|\s*\(\d+\))+$", "");

    static string MatchStaticSignature(Material src)
    {
        if (src == null || !src.HasProperty("baseColorFactor")) return null;
        Color c = src.GetColor("baseColorFactor");
        float rough = src.HasProperty("roughnessFactor") ? src.GetFloat("roughnessFactor") : -1f;
        // glb 里是线性色；材质属性可能存 gamma 色，也可能原样存，两种都比较
        foreach (var cand in new[] { c.linear, c })
        {
            foreach (var (color, roughness, preset) in StaticSignatures)
            {
                float d = Mathf.Abs(cand.r - color.r) + Mathf.Abs(cand.g - color.g) + Mathf.Abs(cand.b - color.b);
                if (d < 0.01f && Mathf.Abs(rough - roughness) < 0.01f) return preset;
            }
        }
        return null;
    }

    static void CopyDecalTexture(Material src, Material dst)
    {
        if (src == null || !dst.HasProperty("_DecalMap") || !src.HasProperty("baseColorTexture")) return;
        var tex = src.GetTexture("baseColorTexture");
        if (tex == null || dst.GetTexture("_DecalMap") == tex) return;
        Undo.RecordObject(dst, "Copy GAFA Decal");
        dst.SetTexture("_DecalMap", tex);
        if (src.HasProperty("baseColorTexture_ST"))
        {
            Vector4 st = src.GetVector("baseColorTexture_ST");
            dst.SetTextureScale("_DecalMap", new Vector2(st.x, st.y));
            dst.SetTextureOffset("_DecalMap", new Vector2(st.z, st.w));
        }
        dst.SetFloat("_UseDecal", 1);
        dst.EnableKeyword("_GAFA_DECAL");
        EditorUtility.SetDirty(dst);
    }
}
