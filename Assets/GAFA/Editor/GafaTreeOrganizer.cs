using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// GAFA/4. 整理树木：把 glb 里合并在一起的树干网格和按 128 片一批导出的树叶卡片，
/// 重新拆成“一棵树一个物体（树干），树叶为子物体”。
/// 前提：已执行 GAFA/3（树干使用 GAFA_bark 材质）。
/// </summary>
public static class GafaTreeOrganizer
{
    const string MeshAssetPath = "Assets/GAFA/Meshes/GafaTrees.asset";
    const float JoinRadius = 0.6f;      // 枝条起点到树干的最大距离
    const float LeafEdgeMin = 1.5f;     // 树叶卡片的最小边长（区分草 / 藤蔓 / 落叶）

    class Comp
    {
        public List<int> verts = new();
        public Vector3 lowest = new(0, float.MaxValue, 0);
        public int tree = -1;
    }

    class Tree
    {
        public int id;
        public List<Comp> comps = new();
        public Vector3 pivot;
        public Dictionary<Material, LeafBuffer> leaves = new();
    }

    class LeafBuffer
    {
        public List<(Mesh mesh, Transform src, int quad)> quads = new();
    }

    [MenuItem("GAFA/4. Organize Trees In Selection")]
    public static void Organize()
    {
        var root = Selection.activeGameObject;
        if (root == null)
        {
            EditorUtility.DisplayDialog("GAFA", "请先选中导入的 GAFA 模型根节点。", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Organize GAFA Trees");

        if (PrefabUtility.IsPartOfPrefabInstance(root))
            PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(root), PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction);

        var children = root.transform.Cast<Transform>().ToList();

        // ---- 树干：GAFA_bark 中包围盒最大的是合并的背景树，其余为榕树
        var barks = children.Where(t => MatName(t).StartsWith("GAFA_bark")).OrderByDescending(t => t.GetComponent<Renderer>().bounds.size.sqrMagnitude).ToList();
        if (barks.Count == 0)
        {
            EditorUtility.DisplayDialog("GAFA", "没有找到使用 GAFA_bark 的树干，请先执行 GAFA/3。", "OK");
            return;
        }
        var mergedBark = barks[0];
        var banyanTrunk = barks.Count > 1 ? barks[1] : null;

        // ---- 树叶：榕树叶 + 与榕树叶同贴图或边长足够大的卡片批次
        var banyanLeaves = children.Where(t => t.name.StartsWith("banyanLeaves")).ToList();
        var banyanTex = banyanLeaves.Count > 0 ? MainTex(banyanLeaves[0]) : null;
        var leafChunks = children.Where(t => t.name.StartsWith("Vegetation") && IsQuadMesh(t) &&
                                             ((banyanTex != null && MainTex(t) == banyanTex) || QuadEdge(t) >= LeafEdgeMin)).ToList();

        // ---- 1. 树干网格拆连通块并归树
        var barkMesh = mergedBark.GetComponent<MeshFilter>().sharedMesh;
        var barkWorld = barkMesh.vertices.Select(v => mergedBark.TransformPoint(v)).ToArray();
        var comps = ConnectedComponents(barkMesh, barkWorld);
        var trees = new List<Tree>();
        var hash = new Dictionary<Vector3Int, List<(Vector3 p, int tree)>>();
        foreach (var c in comps.OrderBy(c => c.lowest.y))
        {
            int best = -1;
            float bestD = JoinRadius;
            foreach (var (p, tr) in Query(hash, c.lowest, JoinRadius))
            {
                // 枝条接在树干上部；树干底部互相挨着的是两棵不同的树
                if (p.y < trees[tr].pivot.y + 1f) continue;
                float d = Vector3.Distance(p, c.lowest);
                if (d < bestD) { bestD = d; best = tr; }
            }
            if (best < 0)
            {
                best = trees.Count;
                trees.Add(new Tree { id = best, pivot = c.lowest });
            }
            c.tree = best;
            trees[best].comps.Add(c);
            foreach (int vi in c.verts) Insert(hash, barkWorld[vi], best, JoinRadius);
        }

        // ---- 2. 树叶卡片分给最近的树（锚点：树干 / 枝条顶点）
        const float anchorCell = 4f;
        var anchors = new Dictionary<Vector3Int, List<(Vector3 p, int tree)>>();
        foreach (var t in trees)
            foreach (var c in t.comps)
                for (int i = 0; i < c.verts.Count; i += 3) Insert(anchors, barkWorld[c.verts[i]], t.id, anchorCell);

        // 第一遍：最近的树；据此确定每棵树的主叶型（A / B 卡片）
        var quadsAll = new List<(Mesh mesh, Transform src, int quad, Material mat, Vector3 c)>();
        foreach (var chunk in leafChunks)
        {
            var mesh = chunk.GetComponent<MeshFilter>().sharedMesh;
            var mat = chunk.GetComponent<Renderer>().sharedMaterial;
            var v = mesh.vertices;
            for (int q = 0; q < v.Length / 4; q++)
                quadsAll.Add((mesh, chunk, q, mat, chunk.TransformPoint((v[q * 4] + v[q * 4 + 1] + v[q * 4 + 2] + v[q * 4 + 3]) * 0.25f)));
        }
        var votes = new Dictionary<(int, Material), int>();
        foreach (var e in quadsAll)
        {
            int tr = Nearest(anchors, e.c, anchorCell, 6);
            if (tr < 0) continue;
            votes[(tr, e.mat)] = votes.TryGetValue((tr, e.mat), out int n) ? n + 1 : 1;
        }
        var dominant = new Dictionary<int, Material>();
        foreach (var g in votes.GroupBy(kv => kv.Key.Item1))
            dominant[g.Key] = g.OrderByDescending(kv => kv.Value).First().Key.Item2;

        // 第二遍：每片叶子只分给主叶型与之相同的最近的树
        var anchorsByMat = new Dictionary<Material, Dictionary<Vector3Int, List<(Vector3 p, int tree)>>>();
        foreach (var t in trees)
        {
            if (!dominant.TryGetValue(t.id, out var dm)) continue;
            if (!anchorsByMat.TryGetValue(dm, out var h)) anchorsByMat[dm] = h = new Dictionary<Vector3Int, List<(Vector3, int)>>();
            foreach (var c in t.comps)
                for (int i = 0; i < c.verts.Count; i += 3) Insert(h, barkWorld[c.verts[i]], t.id, anchorCell);
        }
        int assigned = 0, orphan = 0;
        foreach (var e in quadsAll)
        {
            int tr = anchorsByMat.TryGetValue(e.mat, out var h) ? Nearest(h, e.c, anchorCell, 8) : -1;
            if (tr < 0) tr = Nearest(anchors, e.c, anchorCell, 6);
            if (tr < 0) { orphan++; continue; }
            var t = trees[tr];
            if (!t.leaves.TryGetValue(e.mat, out var buf)) t.leaves[e.mat] = buf = new LeafBuffer();
            buf.quads.Add((e.mesh, e.src, e.quad));
            assigned++;
        }

        // ---- 3. 生成物体和网格资源
        Directory.CreateDirectory(Path.GetDirectoryName(MeshAssetPath));
        if (AssetDatabase.LoadAssetAtPath<Object>(MeshAssetPath) != null) AssetDatabase.DeleteAsset(MeshAssetPath);
        var container = ScriptableObject.CreateInstance<GafaMeshContainer>();
        AssetDatabase.CreateAsset(container, MeshAssetPath);

        var treesRoot = NewChild("Trees", root.transform);
        var nearRoot = NewChild("Near", treesRoot);
        var farRoot = NewChild("Far", treesRoot);
        var barkRenderer = mergedBark.GetComponent<MeshRenderer>();
        var barkMat = barkRenderer.sharedMaterial;
        int nearCount = 0, farCount = 0;

        foreach (var t in trees)
        {
            bool far = t.comps.Count == 1;   // 远景树墙只有一根树干、没有枝条
            string name = far ? $"FarTree_{farCount++:000}" : $"Tree_{nearCount++:000}";
            var go = NewChild(name, far ? farRoot : nearRoot).gameObject;
            go.transform.position = t.pivot;
            CopyRendererSettings(mergedBark.gameObject, go);

            var trunk = BuildSubMesh(barkMesh, t.comps.SelectMany(c => c.verts).ToList(), mergedBark, go.transform, name + "_Bark");
            AssetDatabase.AddObjectToAsset(trunk, container);
            go.AddComponent<MeshFilter>().sharedMesh = trunk;
            var mr = go.AddComponent<MeshRenderer>();
            CopyRenderer(barkRenderer, mr, barkMat);

            int li = 0;
            foreach (var (mat, buf) in t.leaves)
            {
                var leafGo = NewChild(t.leaves.Count > 1 ? $"Leaves_{(char)('A' + li)}" : "Leaves", go.transform).gameObject;
                var srcR = buf.quads[0].src.GetComponent<MeshRenderer>();
                CopyRendererSettings(srcR.gameObject, leafGo);
                var lm = BuildLeafMesh(buf, leafGo.transform, $"{name}_Leaves_{li}");
                AssetDatabase.AddObjectToAsset(lm, container);
                leafGo.AddComponent<MeshFilter>().sharedMesh = lm;
                CopyRenderer(srcR, leafGo.AddComponent<MeshRenderer>(), mat);
                li++;
            }
        }

        // ---- 榕树：树干即物体，所有榕树叶合并为一个子物体
        if (banyanTrunk != null)
        {
            Undo.SetTransformParent(banyanTrunk, treesRoot, "Organize GAFA Trees");
            Undo.RecordObject(banyanTrunk.gameObject, "Organize GAFA Trees");
            banyanTrunk.name = "Banyan";
            banyanTrunk.SetAsFirstSibling();
            if (banyanLeaves.Count > 0)
            {
                var buf = new LeafBuffer();
                foreach (var bl in banyanLeaves)
                {
                    var m = bl.GetComponent<MeshFilter>().sharedMesh;
                    for (int q = 0; q < m.vertexCount / 4; q++) buf.quads.Add((m, bl, q));
                }
                var leafGo = NewChild("Leaves", banyanTrunk).gameObject;
                leafGo.transform.localPosition = Vector3.zero;
                var srcR = banyanLeaves[0].GetComponent<MeshRenderer>();
                CopyRendererSettings(srcR.gameObject, leafGo);
                var lm = BuildLeafMesh(buf, leafGo.transform, "Banyan_Leaves");
                AssetDatabase.AddObjectToAsset(lm, container);
                leafGo.AddComponent<MeshFilter>().sharedMesh = lm;
                CopyRenderer(srcR, leafGo.AddComponent<MeshRenderer>(), srcR.sharedMaterial);
            }
        }
        AssetDatabase.SaveAssets();

        // ---- 删除被拆分的原物体
        foreach (var t in leafChunks.Concat(banyanLeaves).Append(mergedBark)) Undo.DestroyObjectImmediate(t.gameObject);

        // ---- 其余植被按类别收进文件夹
        var vegRoot = NewChild("OtherVegetation", root.transform);
        foreach (var t in root.transform.Cast<Transform>().ToList())
        {
            if (!(t.name.StartsWith("Vegetation") || t.name.StartsWith("grass"))) continue;
            string group = t.name.StartsWith("grass") ? "Grass" : "Vegetation_" + (MainTex(t) != null ? MainTex(t).name : "Untextured");
            var g = vegRoot.Find(group) ?? NewChild(group, vegRoot);
            Undo.SetTransformParent(t, g, "Organize GAFA Trees");
        }

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"[GAFA] 树木整理完成：近景树 {nearCount} 棵，远景树 {farCount} 棵，榕树 {(banyanTrunk != null ? 1 : 0)} 棵；" +
                  $"分配树叶卡片 {assigned} 片，未分配 {orphan} 片。网格资源：{MeshAssetPath}");
    }

    // ================================================================ 网格工具
    static List<Comp> ConnectedComponents(Mesh mesh, Vector3[] world)
    {
        int n = mesh.vertexCount;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        var tris = mesh.triangles;
        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = Find(tris[i]), b = Find(tris[i + 1]), c = Find(tris[i + 2]);
            parent[b] = a;
            parent[Find(c)] = a;
        }
        var map = new Dictionary<int, Comp>();
        for (int i = 0; i < n; i++)
        {
            int r = Find(i);
            if (!map.TryGetValue(r, out var c)) map[r] = c = new Comp();
            c.verts.Add(i);
            if (world[i].y < c.lowest.y) c.lowest = world[i];
        }
        return map.Values.ToList();
    }

    static Mesh BuildSubMesh(Mesh src, List<int> verts, Transform srcT, Transform dstT, string name)
    {
        var remap = new Dictionary<int, int>(verts.Count);
        foreach (int v in verts) remap[v] = remap.Count;
        var sv = src.vertices; var sn = src.normals; var su = src.uv; var sc = src.colors;
        var pos = new Vector3[remap.Count]; var nrm = new Vector3[remap.Count];
        var uv = su.Length > 0 ? new Vector2[remap.Count] : null;
        var col = sc.Length > 0 ? new Color[remap.Count] : null;
        foreach (var (o, i) in remap)
        {
            pos[i] = dstT.InverseTransformPoint(srcT.TransformPoint(sv[o]));
            nrm[i] = dstT.InverseTransformDirection(srcT.TransformDirection(sn[o])).normalized;
            if (uv != null) uv[i] = su[o];
            if (col != null) col[i] = sc[o];
        }
        var tris = new List<int>();
        var st = src.triangles;
        for (int i = 0; i < st.Length; i += 3)
            if (remap.TryGetValue(st[i], out int a) && remap.TryGetValue(st[i + 1], out int b) && remap.TryGetValue(st[i + 2], out int c))
            { tris.Add(a); tris.Add(b); tris.Add(c); }
        return MakeMesh(name, pos, nrm, uv, col, tris);
    }

    static Mesh BuildLeafMesh(LeafBuffer buf, Transform dstT, string name)
    {
        int n = buf.quads.Count * 4;
        var pos = new Vector3[n]; var nrm = new Vector3[n]; var uv = new Vector2[n]; var col = new Color[n];
        var tris = new List<int>(buf.quads.Count * 6);
        var cache = new Dictionary<Mesh, (Vector3[] v, Vector3[] n, Vector2[] u, Color[] c, int[] t)>();
        int k = 0;
        foreach (var (mesh, src, q) in buf.quads)
        {
            if (!cache.TryGetValue(mesh, out var d)) cache[mesh] = d = (mesh.vertices, mesh.normals, mesh.uv, mesh.colors, mesh.triangles);
            int baseV = k;
            for (int j = 0; j < 4; j++, k++)
            {
                int o = q * 4 + j;
                pos[k] = dstT.InverseTransformPoint(src.TransformPoint(d.v[o]));
                nrm[k] = d.n.Length > 0 ? dstT.InverseTransformDirection(src.TransformDirection(d.n[o])).normalized : Vector3.up;
                uv[k] = d.u.Length > 0 ? d.u[o] : Vector2.zero;
                col[k] = d.c.Length > 0 ? d.c[o] : Color.white;
            }
            for (int j = 0; j < 6; j++) tris.Add(d.t[q * 6 + j] - q * 4 + baseV);
        }
        return MakeMesh(name, pos, nrm, uv, col, tris);
    }

    static Mesh MakeMesh(string name, Vector3[] pos, Vector3[] nrm, Vector2[] uv, Color[] col, List<int> tris)
    {
        var m = new Mesh { name = name };
        if (pos.Length > 65535) m.indexFormat = IndexFormat.UInt32;
        m.vertices = pos;
        m.normals = nrm;
        if (uv != null) m.uv = uv;
        if (col != null) m.colors = col;
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    // ================================================================ 空间哈希
    static Vector3Int Cell(Vector3 p, float s) => new(Mathf.FloorToInt(p.x / s), Mathf.FloorToInt(p.y / s), Mathf.FloorToInt(p.z / s));

    static void Insert(Dictionary<Vector3Int, List<(Vector3, int)>> h, Vector3 p, int id, float s)
    {
        var c = Cell(p, s);
        if (!h.TryGetValue(c, out var l)) h[c] = l = new List<(Vector3, int)>();
        l.Add((p, id));
    }

    static IEnumerable<(Vector3 p, int tree)> Query(Dictionary<Vector3Int, List<(Vector3 p, int tree)>> h, Vector3 p, float s)
    {
        var c = Cell(p, s);
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                    if (h.TryGetValue(c + new Vector3Int(x, y, z), out var l))
                        foreach (var e in l) yield return e;
    }

    static int Nearest(Dictionary<Vector3Int, List<(Vector3 p, int tree)>> h, Vector3 p, float s, int maxRing)
    {
        var c = Cell(p, s);
        int best = -1;
        float bestD = float.MaxValue;
        for (int ring = 0; ring <= maxRing; ring++)
        {
            for (int x = -ring; x <= ring; x++)
                for (int y = -ring; y <= ring; y++)
                    for (int z = -ring; z <= ring; z++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y), Mathf.Abs(z)) != ring) continue;
                        if (!h.TryGetValue(c + new Vector3Int(x, y, z), out var l)) continue;
                        foreach (var (q, tr) in l)
                        {
                            float d = (q - p).sqrMagnitude;
                            if (d < bestD) { bestD = d; best = tr; }
                        }
                    }
            // 已找到且更外圈不可能更近时停止
            if (best >= 0 && Mathf.Sqrt(bestD) <= ring * s) break;
        }
        return best;
    }

    // ================================================================ 杂项
    static Transform NewChild(string name, Transform parent)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Organize GAFA Trees");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void CopyRendererSettings(GameObject src, GameObject dst)
    {
        dst.layer = src.layer;
        GameObjectUtility.SetStaticEditorFlags(dst, GameObjectUtility.GetStaticEditorFlags(src));
    }

    static void CopyRenderer(Renderer src, MeshRenderer dst, Material mat)
    {
        dst.sharedMaterial = mat;
        dst.shadowCastingMode = src.shadowCastingMode;
        dst.receiveShadows = src.receiveShadows;
        dst.lightProbeUsage = src.lightProbeUsage;
        dst.reflectionProbeUsage = src.reflectionProbeUsage;
    }

    static string MatName(Transform t)
    {
        var r = t.GetComponent<Renderer>();
        return r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "";
    }

    static Texture MainTex(Transform t)
    {
        var r = t.GetComponent<Renderer>();
        if (r == null || r.sharedMaterial == null || !r.sharedMaterial.HasProperty("baseColorTexture")) return null;
        return r.sharedMaterial.GetTexture("baseColorTexture");
    }

    static bool IsQuadMesh(Transform t)
    {
        var mf = t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return false;
        var m = mf.sharedMesh;
        return m.vertexCount % 4 == 0 && m.GetIndexCount(0) == (uint)(m.vertexCount / 4 * 6);
    }

    static float QuadEdge(Transform t)
    {
        var v = t.GetComponent<MeshFilter>().sharedMesh.vertices;
        return v.Length >= 2 ? (v[0] - v[1]).magnitude * t.lossyScale.x : 0;
    }
}
