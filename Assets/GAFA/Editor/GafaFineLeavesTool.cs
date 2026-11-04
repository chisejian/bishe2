using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 细碎树叶：把树上每张大叶片卡拆成若干张小卡（随机旋转 / 倾斜 / 散开），并换上小叶簇贴图。
/// 小卡沿用原卡的实例色、叶团数据（TEXCOORD2，球面法线 / 墨线）与风动中心（TEXCOORD3），所以明暗分区和风动与原树一致。
/// 原网格不改动：新网格存到 Meshes/Illustrated/Vegetation/FineLeaves/&lt;树名&gt;/，新材质为 *_Fine.mat（树冠描边着色器）。
/// 用法：选中树（或其父节点），菜单 GAFA/Illustrated/Fine Leaves/Build For Selected Trees；可撤销。
/// </summary>
public static class GafaFineLeavesTool
{
    const string MeshDir = "Assets/GAFA/Meshes/Illustrated/Vegetation/FineLeaves";
    const string MatDir = "Assets/GAFA/Materials/Illustrated/Foliage";
    const string TexPath = "Assets/GAFA/Textures/Illustrated/GafaIll_Leaf_Fine.png";
    const string CanopyShader = "GAFA/Illustrated/Foliage Canopy";

    // 每张原卡拆成几张小卡、小卡相对原卡的大小、在原卡平面内 / 法线方向上散开的范围（相对原卡边长）
    public static int SubCards = 8;
    public static float SubScale = 0.42f;
    public static float Spread = 0.5f;
    public static float Depth = 0.3f;
    public static float Tilt = 35f;

    [MenuItem("GAFA/Illustrated/Fine Leaves/Build For Selected Trees")]
    static void BuildSelected()
    {
        int n = Build(Selection.gameObjects);
        Debug.Log($"[GAFA] Fine leaves: rebuilt {n} leaf renderer(s).");
    }

    [MenuItem("GAFA/Illustrated/Fine Leaves/Build For Selected Trees", true)]
    static bool BuildSelectedValid() => Selection.gameObjects.Length > 0;

    [MenuItem("GAFA/Illustrated/Fine Leaves/Rebuild Leaf Texture")]
    static void RebuildTexture() => BuildTexture();

    public static int Build(GameObject[] roots)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        if (tex == null) tex = BuildTexture();
        int count = 0;
        var done = new HashSet<MeshRenderer>();
        foreach (var root in roots)
        {
            if (root == null) continue;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!done.Add(r) || !r.name.StartsWith("Leaves")) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var src = mf.sharedMesh;
                if (src.name.EndsWith("_Fine")) continue;

                string tree = r.transform.parent != null ? r.transform.parent.name : root.name;
                string dir = MeshDir + "/" + tree;
                EnsureFolder(dir);
                string path = dir + "/" + src.name + "_Fine.asset";
                var fine = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool create = fine == null;
                if (create) fine = new Mesh();
                BuildMesh(src, fine, Hash(tree + "/" + src.name));
                if (create) AssetDatabase.CreateAsset(fine, path);
                else EditorUtility.SetDirty(fine);

                Undo.RecordObject(mf, "Fine Leaves");
                mf.sharedMesh = fine;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = FineMaterial(mats[i], tex);
                Undo.RecordObject(r, "Fine Leaves");
                r.sharedMaterials = mats;
                count++;
            }
        }
        AssetDatabase.SaveAssets();
        return count;
    }

    // ---------------------------------------------------------------- 网格
    static void BuildMesh(Mesh src, Mesh dst, int seed)
    {
        var v = src.vertices;
        var nrm = src.normals;
        var col = src.colors;
        var uv0 = new List<Vector2>(); src.GetUVs(0, uv0);
        var uv2 = new List<Vector4>(); src.GetUVs(2, uv2);
        var uv3 = new List<Vector3>(); src.GetUVs(3, uv3);
        var tris = src.triangles;
        int items = v.Length / 4;

        // 每张卡（4 个顶点）的三角形索引
        var itemTris = new List<int>[items];
        for (int i = 0; i < items; i++) itemTris[i] = new List<int>(6);
        for (int k = 0; k + 2 < tris.Length; k += 3)
        {
            int item = tris[k] / 4;
            if (item >= items) continue;
            itemTris[item].Add(tris[k]); itemTris[item].Add(tris[k + 1]); itemTris[item].Add(tris[k + 2]);
        }

        int cap = items * SubCards * 4;
        var oV = new List<Vector3>(cap);
        var oN = new List<Vector3>(nrm.Length > 0 ? cap : 0);
        var oC = new List<Color>(col.Length > 0 ? cap : 0);
        var oUV0 = new List<Vector2>(uv0.Count > 0 ? cap : 0);
        var oUV2 = new List<Vector4>(uv2.Count > 0 ? cap : 0);
        var oUV3 = new List<Vector3>(uv3.Count > 0 ? cap : 0);
        var oT = new List<int>(items * SubCards * 6);
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();
        float S() => (float)rng.NextDouble() * 2f - 1f;

        for (int item = 0; item < items; item++)
        {
            int b = item * 4;
            Vector3 c = (v[b] + v[b + 1] + v[b + 2] + v[b + 3]) * 0.25f;
            Vector3 e1 = v[b + 1] - v[b], e2 = v[b + 2] - v[b];
            Vector3 n = Vector3.Cross(e1, e2);
            n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            Vector3 t1 = e1.sqrMagnitude > 1e-12f ? e1.normalized : Vector3.Cross(n, Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(n, t1);
            float size = 0f;
            for (int k = 0; k < 4; k++) size = Mathf.Max(size, (v[b + k] - c).magnitude);
            size *= 1.4142f;

            for (int s = 0; s < SubCards; s++)
            {
                Quaternion rot = Quaternion.AngleAxis(R() * 360f, n);
                rot = Quaternion.AngleAxis(S() * Tilt, rot * t1) * rot;
                Vector3 off = (t1 * S() + t2 * S()) * (size * Spread) + n * (S() * size * Depth);
                float sc = SubScale * Mathf.Lerp(0.8f, 1.2f, R());
                float bright = Mathf.Lerp(0.92f, 1.06f, R());
                int nb = oV.Count;
                for (int k = 0; k < 4; k++)
                {
                    int i = b + k;
                    oV.Add(c + off + rot * ((v[i] - c) * sc));
                    if (nrm.Length > 0) oN.Add(rot * nrm[i]);
                    if (col.Length > 0) { var cc = col[i] * bright; cc.a = col[i].a; oC.Add(cc); }
                    if (uv0.Count > 0) oUV0.Add(uv0[i]);
                    if (uv2.Count > 0) oUV2.Add(uv2[i]);
                    if (uv3.Count > 0) oUV3.Add(uv3[i]);
                }
                foreach (int idx in itemTris[item]) oT.Add(idx - b + nb);
            }
        }

        dst.Clear();
        dst.name = src.name + "_Fine";
        dst.indexFormat = oV.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        dst.SetVertices(oV);
        if (oN.Count > 0) dst.SetNormals(oN);
        if (oC.Count > 0) dst.SetColors(oC);
        if (oUV0.Count > 0) dst.SetUVs(0, oUV0);
        if (oUV2.Count > 0) dst.SetUVs(2, oUV2);
        if (oUV3.Count > 0) dst.SetUVs(3, oUV3);
        dst.SetTriangles(oT, 0);
        dst.RecalculateBounds();
    }

    // ---------------------------------------------------------------- 材质
    static Material FineMaterial(Material src, Texture2D tex)
    {
        if (src == null || (src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap") == tex)) return src;
        string baseName = src.name.Replace("_Canopy", "").Replace("_Fine", "");
        string path = MatDir + "/" + baseName + "_Fine.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(src);
        var sh = Shader.Find(CanopyShader);
        if (sh != null && m.shader != sh)
        {
            var kw = m.shaderKeywords;
            m.shader = sh;
            m.shaderKeywords = kw;
        }
        m.SetTexture("_BaseMap", tex);
        m.SetTextureScale("_BaseMap", Vector2.one);
        m.SetTextureOffset("_BaseMap", Vector2.zero);
        m.renderQueue = src.renderQueue;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ---------------------------------------------------------------- 贴图：从叶柄处散开的 5 片尖叶（近白，颜色交给实例色）
    // 没有叶缘描边；每片叶分受光 / 背光两半，重叠处只靠明度区分。
    public static Texture2D BuildTexture()
    {
        const int N = 256;
        var px = new Color32[N * N];
        var basePt = new Vector2(0.5f, 0.06f);
        // 先画两侧，最后画中间那片（盖在上面）
        int[] order = { 0, 4, 1, 3, 2 };
        float[] tone = { 0.84f, 0.9f, 0.87f, 0.93f, 0.95f };
        foreach (int k in order)
        {
            float ang = Mathf.Lerp(-50f, 50f, k / 4f) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
            var perp = new Vector2(dir.y, -dir.x);
            float len = 0.8f - 0.14f * Mathf.Abs(k - 2);
            float wid = 0.13f * len;
            Vector2 p0 = basePt + dir * 0.04f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var p = new Vector2((x + 0.5f) / N, (y + 0.5f) / N) - p0;
                float t = Vector2.Dot(p, dir) / len;
                if (t <= 0f || t >= 1f) continue;
                float across = Vector2.Dot(p, perp);
                float hw = wid * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.8f)), 0.75f);
                float a = Mathf.Clamp01((hw - Mathf.Abs(across)) * N + 0.5f);
                if (a <= 0f) continue;
                float g = tone[k] * (across > 0f ? 1f : 0.93f) * Mathf.Lerp(0.96f, 1.02f, t);
                int i = y * N + x;
                var dstC = (Color)px[i];
                var c = new Color(g, g, g, 1f);
                // 空白处直接取叶色（不和黑色混，否则叶缘会有一圈暗边）
                var o = dstC.a <= 0f ? c : Color.Lerp(dstC, c, a);
                o.a = Mathf.Max(dstC.a, a);
                px[i] = o;
            }
        }
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(TexPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(TexPath, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(TexPath);
        imp.textureType = TextureImporterType.Default;
        imp.sRGBTexture = true;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = true;
        imp.mipMapsPreserveCoverage = true;   // 远处小叶不会因为 mip 透明度变低被裁掉
        imp.alphaTestReferenceValue = 0.5f;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
    }

    // ---------------------------------------------------------------- 工具
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static int Hash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char ch in s) h = (h ^ ch) * 16777619;
            return (int)(h & 0x7fffffff);
        }
    }
}
