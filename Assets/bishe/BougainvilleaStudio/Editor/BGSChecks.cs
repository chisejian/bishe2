#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
namespace BougainvilleaStudio
{
    internal static class BGSChecks
    {
        sealed class Plane : IBGSSurfaceQuery
        {
            public bool TryProject(Vector3 p, Vector3 n, float range, out BGSSurface hit)
            { hit = new BGSSurface { position = new Vector3(p.x, p.y, 0), normal = Vector3.forward }; return Mathf.Abs(p.z) <= range; }
        }
        static void Require(bool b, string message) { if (!b) throw new InvalidOperationException(message); }
        static void Mesh(BGSData data, bool foliage)
        {
            Require(data.triangles.Count % 3 == 0, "三角形索引不完整");
            Require(data.vertices.Count == data.normals.Count && data.vertices.Count == data.uv.Count, "顶点属性数量不匹配");
            for (int i = 0; i < data.vertices.Count; i++)
            {
                Require(BGSCore.Finite(data.vertices[i]) && BGSCore.Finite(data.normals[i]), "出现无效顶点或法线");
                if (foliage) Require(data.uv[i].y > 0 && data.uv[i].y < .5f, "花叶未使用图集下半部的透明簇");
                else Require(data.wind[i].y > .5f, "出现不透明花叶包壳");
            }
            for (int i = 0; i < data.triangles.Count; i += 3)
            {
                int a = data.triangles[i], b = data.triangles[i + 1], c = data.triangles[i + 2];
                Require(a >= 0 && a < data.vertices.Count && b >= 0 && b < data.vertices.Count && c >= 0 && c < data.vertices.Count, "网格索引越界");
                Require(Vector3.Cross(data.vertices[b] - data.vertices[a], data.vertices[c] - data.vertices[a]).sqrMagnitude >= 1e-16f, "存在退化三角形");
            }
        }
        static float Depth(BGSData data)
        {
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            foreach (var p in data.vertices) { low = Mathf.Min(low, p.z); high = Mathf.Max(high, p.z); }
            return high - low;
        }
        [MenuItem("Tools/三角梅工坊/运行基础检查")]
        public static void Run()
        {
            var go = new GameObject("BGS_Check") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var t = go.AddComponent<BGSAuthoring>(); t.mode = BGSMode.FlowerBranches; t.showStems = false; int count = 0;
                foreach (float length in new[] { .15f, 1f, 6f, 20f }) foreach (float twist in new[] { 0f, 2.5f, 5f })
                {
                    t.branchLength = length; t.twist = twist; BGSCore.Randomize(t, false);
                    foreach (var branch in t.branches)
                        Require(Mathf.Abs(BGSCore.Length(t, BGSCore.Smooth(branch.points)) - length) < Mathf.Max(.001f, length * .0001f), "枝条长度不匹配");
                    int previous = int.MaxValue;
                    for (int lod = 0; lod < 3; lod++)
                    {
                        var data = BGSCore.Build(t, lod); Mesh(data.body, false); Mesh(data.edges, true);
                        Require(data.edges.vertices.Count > 0, "透明花叶簇缺失");
                        Require(data.Triangles < previous, "LOD 面数没有下降"); previous = data.Triangles;
                    }
                    count++;
                }
                t.branches[0].points[3].position = new Vector3(.3f, -1, .2f); t.branches[0].points[3].locked = true;
                BGSCore.Randomize(t, true); Require(t.branches[0].points[3].position.x == .3f, "随机覆盖锁定点");
                t.snapEnabled = true; t.snapStrength = .5f; t.snapRange = .5f; t.surfaceOffset = .1f;
                Vector3 raw = new Vector3(1, 2, .3f); var query = new Plane();
                Require(Mathf.Abs(BGSCore.Snap(t, raw, 1, query).z - .2f) < .00001f, "吸附强度错误");
                Require(BGSCore.Snap(t, raw, 0, query).z == .3f, "局部权重未关闭吸附");
                t.snapStrength = 0; Require(BGSCore.Snap(t, raw, 1, query).z == .3f, "关闭吸附未恢复原位置");
                t.snapEnabled = false; t.branchCount = 1; t.branchLength = 6; t.twist = 0; t.detailDensity = 0;
                t.mode = BGSMode.FlowerBranches; BGSCore.Randomize(t, false);
                var dense = BGSCore.Build(t, 0); t.fullness = .25f; var sparse = BGSCore.Build(t, 0);
                Require(dense.edges.TriangleCount > sparse.edges.TriangleCount, "密度没有控制整个花叶层");
                Require(dense.body.vertices.Count == 0, "关闭近景细节后仍有不透明主体");
                t.fullness = .95f;
                for (int lod = 0; lod < 3; lod++) Require(Depth(BGSCore.Build(t, lod).edges) > .1f, "远景侧面花叶体积丢失");
                foreach (var mode in new[] { BGSMode.FlowerBranches, BGSMode.FlowerWall })
                {
                    t.mode = mode; BGSCore.Randomize(t, false); t.fullness = 0;
                    for (int lod = 0; lod < 3; lod++) Require(BGSCore.Build(t, lod).Triangles == 0, "零密度仍残留粗管或花墙底板");
                    t.fullness = .95f; int last = int.MaxValue;
                    for (int lod = 0; lod < 3; lod++)
                    {
                        var data = BGSCore.Build(t, lod); Mesh(data.body, false); Mesh(data.edges, true);
                        Require(data.Triangles < last, "花叶 LOD 未简化"); last = data.Triangles;
                    }
                }
                Debug.Log("[三角梅工坊 1.2] " + count + " 组长度/盘曲 × 三级 LOD、锁定点、可逆吸附、透明簇 UV、密度及无包壳回归检查通过。材质、实际吸附和导出仍需在项目中检查。");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
#endif
