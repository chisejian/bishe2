using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GAFA/5. 替换选中模型的植被材质：把 glb 自带的植被材质换成 GAFA/Foliage，
/// 每类植被一个材质（Assets/GAFA/Materials/Foliage），之后调色只需改这些材质。
/// 需先执行 GAFA/4（树木整理）。
/// </summary>
public static class GafaFoliageTools
{
    const string MatDir = "Assets/GAFA/Materials/Foliage";
    const string ShaderName = "GAFA/Foliage";

    enum NormalMode { None, Sphere, Up }

    class Preset
    {
        public string name;
        public float wind, flutter, normalBlend, transl, roughness = 0.78f;
        public NormalMode normalMode = NormalMode.None;
        public bool grass, alphaClip = true;
    }

    // 与 TestGAFA/src/main.js 的 foliage 预设对应。球面法线依赖“一棵树一个物体”，
    // 只用于树叶；其余植被的原始法线信息在导出时丢失，故不做法线混合。
    static readonly Preset[] Presets =
    {
        new Preset { name = "LeavesA", wind = 0.05f, flutter = 0.022f, normalBlend = 0.78f, transl = 0.42f, normalMode = NormalMode.Sphere },
        new Preset { name = "LeavesB", wind = 0.06f, flutter = 0.025f, normalBlend = 0.75f, transl = 0.5f, normalMode = NormalMode.Sphere },
        new Preset { name = "Banyan", wind = 0.05f, flutter = 0.022f, normalBlend = 0.78f, transl = 0.42f, normalMode = NormalMode.Sphere },
        new Preset { name = "Grass", wind = 0.08f, flutter = 0.012f, normalBlend = 0.6f, transl = 0.45f, roughness = 0.85f, normalMode = NormalMode.Up, grass = true, alphaClip = false },
        new Preset { name = "Shrub", wind = 0.035f, flutter = 0.02f, normalBlend = 0f, transl = 0.45f },
        new Preset { name = "Ivy", wind = 0.018f, flutter = 0.015f, normalBlend = 0f, transl = 0.55f },
        new Preset { name = "Fern", wind = 0.03f, flutter = 0.01f, normalBlend = 0f, transl = 0.5f },
        new Preset { name = "Fallen", wind = 0f, flutter = 0f, normalBlend = 0f, transl = 0.15f, roughness = 0.9f },
    };

    [MenuItem("GAFA/5. Apply Foliage Materials To Selection")]
    public static void ApplyToSelection()
    {
        var root = Selection.activeGameObject;
        if (root == null)
        {
            EditorUtility.DisplayDialog("GAFA", "请先选中 GAFA 模型根节点。", "OK");
            return;
        }
        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[GAFA] 找不到着色器 {ShaderName}");
            return;
        }

        var mats = CreateOrLoadMaterials(shader);

        // 树叶 A 与榕树叶共用一张贴图，用来区分 A / B
        Texture leafATex = null;
        var banyanLeaves = root.transform.Find("Trees/Banyan/Leaves");
        if (banyanLeaves != null) leafATex = SourceTexture(banyanLeaves.GetComponent<Renderer>().sharedMaterial);

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        // 先逐个分类，再按原贴图多数投票：同一张贴图的植被归为同一类（边长阈值只是初判）
        var items = new List<(Renderer r, Material src, Texture tex, string kind)>();
        int skipped = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var src = r.sharedMaterial;
            if (src == null || src.shader.name.StartsWith("GAFA/")) continue;
            string kind = Classify(r, leafATex);
            if (kind == null) { skipped++; continue; }
            items.Add((r, src, SourceTexture(src), kind));
        }
        var majority = items.Where(i => i.tex != null && i.kind != "Banyan")
                            .GroupBy(i => i.tex)
                            .ToDictionary(g => g.Key, g => g.GroupBy(i => i.kind).OrderByDescending(k => k.Count()).First().Key);

        var counts = new Dictionary<string, int>();
        foreach (var (r, src, tex, k0) in items)
        {
            string kind = k0 == "Banyan" || tex == null || !majority.ContainsKey(tex) ? k0 : majority[tex];
            // 贴图已是某个预设材质的贴图（例如散落的阔叶卡片），直接归入该预设
            if (k0 != "Banyan" && tex != null)
                foreach (var (name, m) in mats)
                    if (name != "Banyan" && m.GetTexture("_BaseMap") == tex) { kind = name; break; }
            var dst = mats[kind];
            if (tex != null && dst.GetTexture("_BaseMap") == null)
            {
                Undo.RecordObject(dst, "Apply GAFA Foliage");
                dst.SetTexture("_BaseMap", tex);
                EditorUtility.SetDirty(dst);
            }
            Undo.RecordObject(r, "Apply GAFA Foliage");
            r.sharedMaterials = Enumerable.Repeat(dst, r.sharedMaterials.Length).ToArray();
            counts[kind] = counts.TryGetValue(kind, out int n) ? n + 1 : 1;
        }
        Undo.CollapseUndoOperations(group);
        AssetDatabase.SaveAssets();
        Debug.Log($"[GAFA] 植被材质替换完成：{string.Join("，", counts.Select(kv => $"{kv.Key} {kv.Value}"))}；跳过 {skipped} 个（非植被）。材质目录：{MatDir}");
    }

    static string Classify(Renderer r, Texture leafATex)
    {
        var t = r.transform;
        string path = GetPath(t);
        var tex = SourceTexture(r.sharedMaterial);

        if (path.Contains("/Trees/Banyan/")) return "Banyan";
        if (path.Contains("/Trees/")) return tex != null && tex == leafATex ? "LeavesA" : "LeavesB";
        if (t.name.StartsWith("grass")) return "Grass";
        if (!t.name.StartsWith("Vegetation") || tex == null) return null;

        var mf = t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return null;
        var m = mf.sharedMesh;
        bool quads = m.vertexCount % 4 == 0 && m.GetIndexCount(0) == (uint)(m.vertexCount / 4 * 6);
        if (!quads) return "Fern";
        var v = m.vertices;
        float edge = (v[0] - v[1]).magnitude * t.lossyScale.x;
        if (edge >= 1.5f) return tex == leafATex ? "LeavesA" : "LeavesB";
        if (edge >= 0.6f) return "Shrub";
        if (edge >= 0.25f) return "Ivy";
        return "Fallen";
    }

    static Dictionary<string, Material> CreateOrLoadMaterials(Shader shader)
    {
        Directory.CreateDirectory(MatDir);
        var dict = new Dictionary<string, Material>();
        foreach (var p in Presets)
        {
            string path = $"{MatDir}/GAFA_Foliage_{p.name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                ApplyPreset(mat, p);
                AssetDatabase.CreateAsset(mat, path);
            }
            dict[p.name] = mat;   // 已存在的材质保留你的调色，不覆盖
        }
        return dict;
    }

    static void ApplyPreset(Material m, Preset p)
    {
        m.SetFloat("_Wind", p.wind);
        m.SetFloat("_Flutter", p.flutter);
        m.SetFloat("_NormalBlend", p.normalBlend);
        m.SetFloat("_Translucency", p.transl);
        m.SetFloat("_Roughness", p.roughness);

        foreach (var k in new[] { "_NORMALMODE_NONE", "_NORMALMODE_SPHERE", "_NORMALMODE_UP" }) m.DisableKeyword(k);
        m.SetFloat("_NormalMode", (int)p.normalMode);
        m.EnableKeyword("_NORMALMODE_" + p.normalMode.ToString().ToUpperInvariant());

        m.SetFloat("_Grass", p.grass ? 1 : 0);
        if (p.grass) m.EnableKeyword("_GAFA_GRASS"); else m.DisableKeyword("_GAFA_GRASS");
        m.SetFloat("_AlphaClip", p.alphaClip ? 1 : 0);
        if (p.alphaClip) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
        m.renderQueue = p.alphaClip ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : (int)UnityEngine.Rendering.RenderQueue.Geometry;
    }

    static Texture SourceTexture(Material m)
    {
        if (m == null) return null;
        if (m.HasProperty("baseColorTexture")) return m.GetTexture("baseColorTexture");
        if (m.HasProperty("_BaseMap")) return m.GetTexture("_BaseMap");
        return null;
    }

    static string GetPath(Transform t)
    {
        var s = "/" + t.name;
        while (t.parent != null) { t = t.parent; s = "/" + t.name + s; }
        return s;
    }
}
