using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// GAFA 插画版材质工具：
///   GAFA/Illustrated/1. 创建插画材质        —— 按 TestGAFA/illustrated/src/main.js 与 vegetation.js 的预设生成 .mat（已存在的保留你的调色）
///   GAFA/Illustrated/重置插画材质为预设      —— 把所有插画材质的参数恢复成预设值
/// 贴图 Assets/GAFA/Textures/Illustrated 由 illustrated/src/textures.js 原样生成（与 glb 贴图同一朝向）。
/// </summary>
public static class GafaIllustratedTools
{
    const string Root = "Assets/GAFA";
    const string NoisePath = Root + "/Textures/GafaNoise.png";
    const string TexDir = Root + "/Textures/Illustrated";
    const string MatDir = Root + "/Materials/Illustrated";
    const string FoliageDir = MatDir + "/Foliage";
    const string PaintedShader = "GAFA/Illustrated/Painted";
    const string FoliageShader = "GAFA/Illustrated/Foliage";

    // 插画版的半球环境光（线性值，materials.js 的 uSkyAmb / uGroundAmb）
    static readonly Color SkyAmbientLinear = new Color(0.4f, 0.42f, 0.47f);
    static readonly Color GroundAmbientLinear = new Color(0.36f, 0.33f, 0.28f);

    // ================================================================ 预设
    enum Kind { Concrete, Rust, Bark, Pavers, Soil, Wood, Plain }

    class Painted
    {
        public string name;
        public Kind kind = Kind.Concrete;
        public string color = "#c4bdad", paint, mossColor = "#8f9c5a";
        public float paintAmount = 0.8f, moss = 1, streaks = 1, dirt = 1, scale = 1, seed = 0, decal = 1;
        public float edge = 0f, soft = 0.04f, brush = 0.2f, rim = 0.12f, spec = 0f;
        public bool decalMap, transparent;
        public float opacity = 1;
        public Vector4 rect = new Vector4(-1e4f, 1e4f, -1e4f, 1e4f);
    }

    // 与 illustrated/src/main.js 的 mats 一一对应
    static readonly Painted[] PaintedPresets =
    {
        new Painted { name = "concrete", color = "#c6bfae", seed = 0f, moss = 0.7f, streaks = 0.6f },
        new Painted { name = "paint", color = "#c6bfae", paint = "#c4825e", paintAmount = 0.9f, moss = 0.6f, streaks = 0.5f, seed = 3.1f },
        new Painted { name = "wall", color = "#d2cab8", paint = "#ddd5c4", paintAmount = 0.82f, moss = 0.4f, streaks = 0.5f, seed = 7.7f },
        new Painted { name = "interior", color = "#56605f", moss = 0.1f, streaks = 0.2f, dirt = 0.2f, seed = 1.7f, rim = 0f },
        new Painted { name = "stone", color = "#d3c9b4", moss = 0.6f, streaks = 0.3f, seed = 5.5f },
        new Painted { name = "plaster", color = "#ece6da", moss = 0.6f, streaks = 0.3f, seed = 9.1f, scale = 2f },
        new Painted { name = "soil", kind = Kind.Soil, color = "#a4a66e", brush = 0.2f },
        new Painted { name = "frame", kind = Kind.Rust, color = "#6e5444", paint = "#3e4a48", paintAmount = 0.85f, seed = 2.2f },
        new Painted { name = "rust", kind = Kind.Rust, color = "#8a5e44", seed = 4.4f },
        new Painted { name = "lamp", kind = Kind.Rust, color = "#8a5e44", paint = "#4c5650", paintAmount = 0.9f, seed = 8.2f },
        new Painted { name = "bike", kind = Kind.Rust, color = "#8a5e44", paint = "#d4ad52", paintAmount = 1f, seed = 6.6f, scale = 3f },
        new Painted { name = "rubber", kind = Kind.Plain, color = "#36332f" },
        new Painted { name = "wood", kind = Kind.Wood, color = "#a98563", moss = 0.4f, streaks = 0.2f, seed = 3.3f },
        new Painted { name = "painting", color = "#e6dccb", decalMap = true, moss = 0.1f, streaks = 0.2f, decal = 1f },
        new Painted { name = "mural", color = "#cfc6b4", decalMap = true, moss = 0.3f, streaks = 0.5f, decal = 1f, seed = 2.9f },
        // glassMaterial()：暗青灰，半透明，一块硬高光，不投影
        new Painted { name = "glass", kind = Kind.Plain, color = "#6f8a8a", transparent = true, opacity = 0.6f, rim = 0.6f, spec = 1f, brush = 0.05f },
        new Painted { name = "bark", kind = Kind.Bark, color = "#9b7658", moss = 1.1f, mossColor = "#8c9a50" },
        // SITE.plaza = { x0: -32, x1: 32, z1: 40 }
        new Painted { name = "plaza", kind = Kind.Pavers, color = "#e8dbbb", mossColor = "#a3a865", rect = new Vector4(-32, 32, -10, 40) },
        new Painted { name = "podium", kind = Kind.Pavers, color = "#e2d5b6", mossColor = "#a3a865", rect = new Vector4(-38, 38, -60, 7) },
    };

    enum NormalMode { None, Sphere, Up }

    class Foliage
    {
        public string name, texture;
        public string[] palette;
        public string dry = "#c9be7e";
        public float paletteGroup = 0, itemVertices = 4, jitter = 0.04f, vertexColor = 0;
        public float wind, flutter, normalBlend, lift = 0.35f, transl = 0.25f;
        public float edge = -0.12f, soft = 0.04f, brush = 0.35f, rim = 0.06f;
        public NormalMode normalMode = NormalMode.None;
        public bool grass, alphaClip = true;
        public float inkMode = 2;      // 原项目 INK.clump；草、藤、落叶为 INK.none
        public bool inkPerItem;        // 蕨：每丛一个叶团编号（glb 植被用；网页植被自带叶团编号）
        public bool exact = true;      // 网页导出的植被：颜色 / 叶团法线 / 叶团编号直接取自逐实例数据
    }

    // 与 illustrated/src/main.js 的 foliage() 预设一一对应，用于从网页版导出的植被（Assets/GAFA/Meshes/Illustrated）：
    // 颜色是原项目的实例色（顶点色），法线按 normalBlend 混向逐实例的 aNormalW（TEXCOORD2），草混向正上方。
    static readonly Foliage[] FoliagePresets =
    {
        // banyanLeaf：背景树 A（cardsA，含远景树墙）
        new Foliage { name = "LeavesA", texture = "GafaIll_Leaf_Banyan", wind = 0.04f, flutter = 0.015f, normalBlend = 0.92f, normalMode = NormalMode.Sphere },
        // broadLeaf：背景树 B（cardsB）
        new Foliage { name = "LeavesB", texture = "GafaIll_Leaf_Broad", wind = 0.05f, flutter = 0.018f, normalBlend = 0.92f, normalMode = NormalMode.Sphere },
        // 旧版（glb 植被）的远景树墙配色，网页植被不用
        new Foliage { name = "FarTrees", texture = "GafaIll_Leaf_Banyan", palette = new[] { "#9aae7a", "#94aa80", "#a3b07a" }, jitter = 0.02f, wind = 0.04f, flutter = 0.015f, normalBlend = 0.92f, lift = 0.5f, normalMode = NormalMode.Sphere, exact = false },
        // banyanLeaf：榕树叶
        new Foliage { name = "Banyan", texture = "GafaIll_Leaf_Banyan", wind = 0.04f, flutter = 0.015f, normalBlend = 0.92f, normalMode = NormalMode.Sphere },
        new Foliage { name = "Grass", wind = 0.08f, flutter = 0.012f, normalBlend = 1f, transl = 0.2f, brush = 0.2f, normalMode = NormalMode.Up, grass = true, alphaClip = false, inkMode = 0 },
        new Foliage { name = "Shrub", texture = "GafaIll_Leaf_Shrub", wind = 0.03f, flutter = 0.015f, normalBlend = 0.88f, normalMode = NormalMode.Sphere },
        new Foliage { name = "Ivy", texture = "GafaIll_Ivy", wind = 0.018f, flutter = 0.015f, normalBlend = 0.7f, normalMode = NormalMode.Sphere, inkMode = 0 },
        new Foliage { name = "Fern", texture = "GafaIll_Fern", itemVertices = 126, wind = 0.03f, flutter = 0.01f, normalBlend = 0.5f, normalMode = NormalMode.Sphere, inkPerItem = true },
        // 落叶：颜色在图集里，顶点色是原项目的明暗抖动（0.75–1.1）
        new Foliage { name = "Fallen", texture = "GafaIll_FallenAtlas", wind = 0f, flutter = 0f, normalBlend = 0f, transl = 0f, inkMode = 0 },
    };

    // ================================================================ 1. 材质
    [MenuItem("GAFA/Illustrated/1. Create Illustrated Materials")]
    public static void CreateMaterials() => CreateOrUpdate(false);

    [MenuItem("GAFA/Illustrated/Reset Illustrated Materials To Presets")]
    public static void ResetMaterials()
    {
        if (!EditorUtility.DisplayDialog("GAFA", "把所有插画材质的参数恢复为预设值？你在材质上做的调色会被覆盖。", "恢复", "取消")) return;
        CreateOrUpdate(true);
    }

    static void CreateOrUpdate(bool reset)
    {
        var painted = Shader.Find(PaintedShader);
        var foliage = Shader.Find(FoliageShader);
        if (painted == null || foliage == null)
        {
            Debug.LogError($"[GAFA] 找不到着色器 {PaintedShader} / {FoliageShader}");
            return;
        }
        var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
        if (noise == null)
        {
            GafaNoiseTools.GenerateNoise();
            noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
        }
        ConfigureTextures();
        Directory.CreateDirectory(MatDir);
        Directory.CreateDirectory(FoliageDir);

        int created = 0, index = 0;
        foreach (var p in PaintedPresets)
        {
            var (mat, isNew) = LoadOrCreate($"{MatDir}/GAFA_Ill_{p.name}.mat", painted);
            if (isNew || reset) ApplyPainted(mat, p, noise);
            else mat.SetTexture("_NoiseTex", noise);
            // 墨线：每个材质一个编号（原项目 nextInkId），实体描线；玻璃不描
            mat.SetFloat("_InkId", InkId(index++));
            mat.SetFloat("_InkMode", p.transparent ? 0 : 1);
            Save(mat, isNew, $"{MatDir}/GAFA_Ill_{p.name}.mat");
            if (isNew) created++;
        }
        foreach (var p in FoliagePresets)
        {
            var (mat, isNew) = LoadOrCreate($"{FoliageDir}/GAFA_Ill_Foliage_{p.name}.mat", foliage);
            if (isNew || reset) ApplyFoliage(mat, p, noise);
            else mat.SetTexture("_NoiseTex", noise);
            mat.SetFloat("_InkId", InkId(index++));
            mat.SetFloat("_InkMode", p.inkMode);
            mat.SetFloat("_InkPerItem", p.inkPerItem ? 1 : 0);
            Save(mat, isNew, $"{FoliageDir}/GAFA_Ill_Foliage_{p.name}.mat");
            if (isNew) created++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[GAFA] 插画材质：新建 {created} 个，{(reset ? "全部恢复为预设" : "已存在的保留原参数")}。目录：{MatDir}");
    }

    static float InkId(int i) => 1 + (i * 37) % 1500;

    static (Material, bool) LoadOrCreate(string path, Shader shader)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) return (new Material(shader), true);
        if (mat.shader != shader) mat.shader = shader;
        return (mat, false);
    }

    static void Save(Material mat, bool isNew, string path)
    {
        if (isNew) AssetDatabase.CreateAsset(mat, path);
        else EditorUtility.SetDirty(mat);
    }

    static void ApplyPainted(Material m, Painted p, Texture noise)
    {
        foreach (Kind k in System.Enum.GetValues(typeof(Kind))) m.DisableKeyword("_KIND_" + k.ToString().ToUpperInvariant());
        m.SetFloat("_Kind", (int)p.kind);
        m.EnableKeyword("_KIND_" + p.kind.ToString().ToUpperInvariant());

        m.SetColor("_BaseColor", Html(p.color));
        bool usePaint = !string.IsNullOrEmpty(p.paint);
        m.SetFloat("_UsePaint", usePaint ? 1 : 0);
        SetKeyword(m, "_USE_PAINT", usePaint);
        m.SetColor("_PaintColor", usePaint ? Html(p.paint) : Color.black);
        m.SetFloat("_PaintAmount", p.paintAmount);
        m.SetColor("_MossColor", Html(p.mossColor));
        m.SetFloat("_Moss", p.moss);
        m.SetFloat("_Streaks", p.streaks);
        m.SetFloat("_Dirt", p.dirt);
        m.SetFloat("_Scale", p.scale);
        m.SetFloat("_Seed", p.seed);
        m.SetVector("_Rect", p.rect);
        // 插画版石板 1.5 × 0.75 m（网页导出的草按这个砖缝 / 缺砖摆放；用 glb 写实植被时改成 1.2 × 0.6）
        m.SetVector("_TileSize", new Vector4(1.5f, 0.75f, 0, 0));
        m.SetFloat("_Decal", p.decal);
        m.SetTexture("_NoiseTex", noise);
        ApplyToon(m, p.edge, p.soft, p.brush, p.rim, p.spec);

        // 壁画 / 挂画沿用写实材质里从 glb 拷来的贴图（两版的贴图内容相同）
        if (p.decalMap)
        {
            var src = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/GAFA_{p.name}.mat");
            var tex = src != null && src.HasProperty("_DecalMap") ? src.GetTexture("_DecalMap") : null;
            if (tex != null)
            {
                m.SetTexture("_DecalMap", tex);
                m.SetTextureScale("_DecalMap", src.GetTextureScale("_DecalMap"));
                m.SetTextureOffset("_DecalMap", src.GetTextureOffset("_DecalMap"));
            }
        }
        bool hasDecal = p.decalMap && m.GetTexture("_DecalMap") != null;
        m.SetFloat("_UseDecal", hasDecal ? 1 : 0);
        SetKeyword(m, "_GAFA_DECAL", hasDecal);

        m.SetFloat("_Transparent", p.transparent ? 1 : 0);
        SetKeyword(m, "_SURFACE_TYPE_TRANSPARENT", p.transparent);
        m.SetFloat("_Opacity", p.opacity);
        m.SetFloat("_SrcBlend", (float)(p.transparent ? BlendMode.SrcAlpha : BlendMode.One));
        m.SetFloat("_DstBlend", (float)(p.transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
        m.SetFloat("_ZWrite", p.transparent ? 0 : 1);
        m.SetFloat("_Cull", (float)(p.transparent ? CullMode.Off : CullMode.Back));
        m.SetOverrideTag("RenderType", p.transparent ? "Transparent" : "");
        m.renderQueue = p.transparent ? (int)RenderQueue.Transparent : -1;
        // 透明物体不投影、不写深度法线（原项目 glass 是 shadowless，也不写墨线缓冲）
        m.SetShaderPassEnabled("ShadowCaster", !p.transparent);
        m.SetShaderPassEnabled("DepthOnly", !p.transparent);
        m.SetShaderPassEnabled("DepthNormals", !p.transparent);
    }

    static void ApplyFoliage(Material m, Foliage p, Texture noise)
    {
        var pal = (p.palette ?? new[] { "#ffffff", "#ffffff", "#ffffff" }).Select(Html).ToArray();
        m.SetColor("_Color", pal[0]);
        m.SetColor("_Color2", pal[1]);
        m.SetColor("_Color3", pal[2]);
        m.SetFloat("_PaletteGroup", p.paletteGroup);
        m.SetFloat("_ItemVertices", p.itemVertices);
        m.SetFloat("_Jitter", p.jitter);
        m.SetFloat("_VertexColorStrength", p.exact ? 1f : p.vertexColor);
        m.SetFloat("_ExactColor", p.exact ? 1 : 0);
        SetKeyword(m, "_GAFA_EXACT_COLOR", p.exact);
        m.SetColor("_DryColor", Html(p.dry));

        var tex = string.IsNullOrEmpty(p.texture) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{p.texture}.png");
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_AlphaClip", p.alphaClip ? 1 : 0);
        SetKeyword(m, "_ALPHATEST_ON", p.alphaClip);
        m.SetFloat("_Cutoff", 0.5f);
        m.renderQueue = p.alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;

        foreach (var k in new[] { "_NORMALMODE_NONE", "_NORMALMODE_SPHERE", "_NORMALMODE_UP" }) m.DisableKeyword(k);
        m.SetFloat("_NormalMode", (int)p.normalMode);
        m.EnableKeyword("_NORMALMODE_" + p.normalMode.ToString().ToUpperInvariant());
        m.SetFloat("_NormalBlend", p.normalBlend);
        // 网页植被的 aNormalW 已含向上的偏移
        m.SetFloat("_NormalLift", p.exact ? 0f : p.lift);

        ApplyToon(m, p.edge, p.soft, p.brush, p.rim, 0f);
        m.SetFloat("_Translucency", p.transl);
        m.SetTexture("_NoiseTex", noise);

        m.SetFloat("_Wind", p.wind);
        m.SetFloat("_Flutter", p.flutter);
        m.SetFloat("_Grass", p.grass ? 1 : 0);
        SetKeyword(m, "_GAFA_GRASS", p.grass);
    }

    static void ApplyToon(Material m, float edge, float soft, float brush, float rim, float spec)
    {
        m.SetFloat("_ShadeEdge", edge);
        m.SetFloat("_ShadeSoftness", soft);
        m.SetFloat("_BrushNoise", brush);
        m.SetFloat("_Rim", rim);
        m.SetFloat("_Specular", spec);
        // 材质颜色属性存的是 gamma 值，着色器里会转成线性
        m.SetColor("_SkyAmbient", SkyAmbientLinear.gamma);
        m.SetColor("_GroundAmbient", GroundAmbientLinear.gamma);
        m.SetFloat("_AmbientFromScene", 1f);
    }

    // 叶片贴图：sRGB、透明边、保持 alpha 覆盖率的 mipmap（对应 textures.js 的 coverageMips）
    static void ConfigureTextures()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
            bool dirty = !ti.sRGBTexture || !ti.alphaIsTransparency || !ti.mipMapsPreserveCoverage
                         || ti.wrapMode != TextureWrapMode.Clamp || ti.anisoLevel != 8 || ti.npotScale != TextureImporterNPOTScale.None;
            if (!dirty) continue;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = 0.5f;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 8;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.SaveAndReimport();
        }
    }

    static void SetKeyword(Material m, string kw, bool on)
    {
        if (on) m.EnableKeyword(kw);
        else m.DisableKeyword(kw);
    }

    static Color Html(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
}
