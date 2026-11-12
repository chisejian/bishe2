#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Read-only capture. Never touch an original importer, mesh, material or Prefab.
internal static class STSReferenceCapture
{
    public const string CopiesFolder = "Assets/StylizedTreeStudio/Generated/SourceCopies";
    public const string OakPrefabGuid = "7836fe73991d47f438b48060514f50a8";

    internal sealed class Surface
    {
        public Vector3[] vertices, normals;
        public Vector2[] uv;
        public Color[] colors;
        public int[] indices;
    }

    // Editor-only API also works when the original model has Read/Write OFF.
    internal static Surface Read(Mesh mesh, int slot, Matrix4x4 matrix)
    {
        if (!mesh || slot < 0 || slot >= mesh.subMeshCount)
            throw new InvalidOperationException("找不到指定的枝干网格／材质槽。");
        using (var snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh))
        {
            var data = snapshot[0];
            var sub = data.GetSubMesh(slot);
            if (sub.topology != MeshTopology.Triangles)
                throw new InvalidOperationException("参考枝干须为三角形网格，请使用模型的近景 LOD。");
            using (var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
            using (var normals = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
            using (var uv = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp))
            using (var colors = new NativeArray<Color>(data.vertexCount, Allocator.Temp))
            using (var indices = new NativeArray<int>(sub.indexCount, Allocator.Temp))
            {
                data.GetVertices(positions);
                bool hasNormals = data.HasVertexAttribute(VertexAttribute.Normal);
                bool hasUV = data.HasVertexAttribute(VertexAttribute.TexCoord0);
                bool hasColors = data.HasVertexAttribute(VertexAttribute.Color);
                if (hasNormals) data.GetNormals(normals);
                if (hasUV) data.GetUVs(0, uv);
                if (hasColors) data.GetColors(colors);
                data.GetIndices(indices, slot, true);
                var result = new Surface();
                var v = new List<Vector3>(); var n = new List<Vector3>();
                var u = new List<Vector2>(); var c = new List<Color>();
                var remap = new Dictionary<int, int>();
                result.indices = new int[indices.Length];
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                for (int i = 0; i < indices.Length; i++)
                {
                    int original = indices[i], mapped;
                    if (!remap.TryGetValue(original, out mapped))
                    {
                        mapped = v.Count; remap.Add(original, mapped);
                        Vector3 p = matrix.MultiplyPoint3x4(positions[original]);
                        if (!STSPath.Finite(p)) throw new InvalidOperationException("参考模型包含无效坐标。");
                        v.Add(p); n.Add(hasNormals ? normalMatrix.MultiplyVector(normals[original]).normalized : Vector3.zero);
                        u.Add(hasUV ? uv[original] : Vector2.zero);
                        c.Add(hasColors ? colors[original] : Color.white);
                    }
                    result.indices[i] = mapped;
                }
                if (matrix.determinant < 0)
                    for (int i = 0; i < result.indices.Length; i += 3)
                    { int k = result.indices[i + 1]; result.indices[i + 1] = result.indices[i + 2]; result.indices[i + 2] = k; }
                result.vertices = v.ToArray(); result.normals = n.ToArray();
                result.uv = u.ToArray(); result.colors = c.ToArray();
                if (!hasNormals)
                {
                    for (int i = 0; i < result.indices.Length; i += 3)
                    {
                        int a = result.indices[i], b = result.indices[i + 1], d = result.indices[i + 2];
                        Vector3 face = Vector3.Cross(v[b] - v[a], v[d] - v[a]);
                        result.normals[a] += face; result.normals[b] += face; result.normals[d] += face;
                    }
                    for (int i = 0; i < result.normals.Length; i++) result.normals[i].Normalize();
                }
                return result;
            }
        }
    }

    static Mesh MakeMesh(Surface surface, string name)
    {
        if (surface.vertices.Length == 0) throw new InvalidOperationException("枝干材质槽中没有几何体。");
        var mesh = new Mesh { name = name };
        if (surface.vertices.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = surface.vertices; mesh.normals = surface.normals;
        mesh.uv = surface.uv; mesh.colors = surface.colors;
        mesh.triangles = surface.indices; mesh.RecalculateBounds(); mesh.RecalculateTangents();
        return mesh;
    }

    static List<MeshFilter[]> Levels(GameObject source)
    {
        var result = new List<MeshFilter[]>();
        var group = source.GetComponentInChildren<LODGroup>(true);
        if (group)
        {
            foreach (var lod in group.GetLODs())
            {
                var filters = new List<MeshFilter>();
                foreach (var renderer in lod.renderers)
                {
                    if (!renderer) continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter && filter.sharedMesh) filters.Add(filter);
                }
                if (filters.Count > 0) result.Add(filters.ToArray());
            }
        }
        else result.Add(source.GetComponentsInChildren<MeshFilter>(true));
        if (result.Count == 0 || result[0].Length == 0)
            throw new InvalidOperationException("预制体中没有可读取的静态枝干网格。");
        return result;
    }

    static Surface Merge(List<Surface> surfaces)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
        var colors = new List<Color>(); var triangles = new List<int>();
        foreach (var s in surfaces)
        {
            int offset = v.Count; v.AddRange(s.vertices); n.AddRange(s.normals);
            uv.AddRange(s.uv); colors.AddRange(s.colors);
            foreach (int index in s.indices) triangles.Add(offset + index);
        }
        return new Surface { vertices = v.ToArray(), normals = n.ToArray(), uv = uv.ToArray(), colors = colors.ToArray(), indices = triangles.ToArray() };
    }

    static Vector3 Nearest(Vector3 p, Vector3[] vertices)
    {
        Vector3 best = vertices[0]; float distance = float.PositiveInfinity;
        foreach (Vector3 v in vertices)
        { float d = (p - v).sqrMagnitude; if (d < distance) { best = v; distance = d; } }
        return best;
    }

    internal static STSReferenceGuide[] Guides(List<Vector3> leafPoints, Vector3[] bark, Vector3 root, float height)
    {
        // Sample positions only; the source leaves are NEVER copied or rendered.
        // Keep guides at real crown positions, then use the unchanged card engine.
        var candidates = new List<Vector3>();
        var cells = new HashSet<Vector3Int>(); float cell = Mathf.Max(.001f, height * .025f);
        var input = leafPoints.Count > 0 ? leafPoints : new List<Vector3>(bark);
        foreach (Vector3 p in input)
        {
            if (p.y < root.y + height * .32f) continue;
            var key = new Vector3Int(Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.y / cell), Mathf.RoundToInt(p.z / cell));
            if (cells.Add(key)) candidates.Add(p);
        }
        if (candidates.Count == 0) throw new InvalidOperationException("参考模型上部没有可用的发叶位置。");
        var selected = new List<Vector3>(); var distance = new float[candidates.Count];
        int next = 0;
        for (int i = 0; i < candidates.Count; i++)
        { distance[i] = float.PositiveInfinity; if (candidates[i].y > candidates[next].y) next = i; }
        for (int count = 0; count < Mathf.Min(64, candidates.Count); count++)
        {
            Vector3 point = candidates[next]; selected.Add(point); float farthest = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                distance[i] = Mathf.Min(distance[i], (point - candidates[i]).sqrMagnitude);
                if (distance[i] > farthest) { farthest = distance[i]; next = i; }
            }
            if (farthest < cell * cell * .25f) break;
        }
        var result = new STSReferenceGuide[selected.Count];
        for (int i = 0; i < result.Length; i++)
        {
            Vector3 end = selected[i], start = Nearest(end, bark);
            if ((end - start).sqrMagnitude < height * height * .000025f)
                end = start + ((start - root).normalized + Vector3.up).normalized * height * .025f;
            result[i] = new STSReferenceGuide { start = start, end = end };
        }
        return result;
    }

    public static STSReferenceTrunk Capture(GameObject source, int barkSlot)
    {
        if (!source || !EditorUtility.IsPersistent(source))
            throw new InvalidOperationException("请从 Project 窗口拖入预制体／模型资源。");
        var levels = Levels(source); var first = levels[0][0];
        var renderer0 = first.GetComponent<Renderer>(); var materials = renderer0 ? renderer0.sharedMaterials : new Material[0];
        if (barkSlot < 0 || barkSlot >= first.sharedMesh.subMeshCount || barkSlot >= materials.Length)
            throw new InvalidOperationException("枝干材质槽超出范围；你提供的 OakTree04 使用第 0 槽。");
        Material bark = materials[barkSlot];
        if (bark && (!bark.shader || bark.shader.name == "Hidden/InternalErrorShader"))
            throw new InvalidOperationException("原树皮材质的着色器缺失，请先恢复原材质的显示。");
        var meshes = new List<Mesh>(); var crown = new List<Vector3>();
        var profile = ScriptableObject.CreateInstance<STSReferenceTrunk>();
        string assetPath = null; bool success = false;
        try
        {
            Bounds bounds = new Bounds(); bool hasBounds = false;
            foreach (var filters in levels)
            {
                var surfaces = new List<Surface>();
                foreach (var filter in filters)
                {
                    var renderer = filter.GetComponent<Renderer>(); var slots = renderer ? renderer.sharedMaterials : new Material[0];
                    Matrix4x4 matrix = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    for (int slot = 0; slot < filter.sharedMesh.subMeshCount; slot++)
                    {
                        bool use = bark ? slot < slots.Length && slots[slot] == bark : filter == first && slot == barkSlot;
                        if (use)
                        {
                            // Ignore the original billboard even if it happens to share a material.
                            using (var data = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
                                if (data[0].GetSubMesh(slot).indexCount < 12) continue;
                            surfaces.Add(Read(filter.sharedMesh, slot, matrix));
                        }
                        else if (meshes.Count == 0)
                        {
                            var s = Read(filter.sharedMesh, slot, matrix);
                            crown.AddRange(s.vertices);
                        }
                    }
                }
                if (surfaces.Count == 0) continue;
                Mesh mesh = MakeMesh(Merge(surfaces), "STS_ReferenceBark_LOD" + meshes.Count);
                meshes.Add(mesh);
                if (meshes.Count == 1)
                {
                    bounds = mesh.bounds; hasBounds = true;
                    foreach (Vector3 p in crown) bounds.Encapsulate(p);
                }
                if (meshes.Count == 2) break;
            }
            if (!hasBounds) throw new InvalidOperationException("没有从指定材质槽提取到枝干，请检查槽位。");
            Vector3[] vertices = meshes[0].vertices; float bottom = meshes[0].bounds.min.y;
            Vector3 root = Vector3.zero; int rootCount = 0; float rootBand = Mathf.Max(.001f, meshes[0].bounds.size.y * .015f);
            foreach (Vector3 p in vertices) if (p.y <= bottom + rootBand) { root += p; rootCount++; }
            root /= Mathf.Max(1, rootCount); root.y = bottom;
            profile.root = root; profile.sourceBounds = bounds;
            profile.sourceHeight = bounds.max.y - root.y;
            profile.sourceWidth = Mathf.Max(bounds.size.x, bounds.size.z);
            if (profile.sourceHeight < .01f || profile.sourceWidth < .01f)
                throw new InvalidOperationException("参考模型尺寸过小或退化。");
            profile.guides = Guides(crown, vertices, root, profile.sourceHeight);
            profile.sourceName = source.name; profile.sourcePrefabPath = AssetDatabase.GetAssetPath(source);
            profile.sourcePrefabGuid = AssetDatabase.AssetPathToGUID(profile.sourcePrefabPath);
            profile.protectedSourcePaths = AssetDatabase.GetDependencies(profile.sourcePrefabPath, true);
            profile.revision = Guid.NewGuid().ToString("N"); profile.barkLODs = meshes.ToArray();
            STSMaterials.EnsureFolders(CopiesFolder + "/" + profile.revision);
            assetPath = CopiesFolder + "/" + profile.revision + "/ReferenceTrunk.asset";
            AssetDatabase.CreateAsset(profile, assetPath);
            foreach (Mesh mesh in meshes) AssetDatabase.AddObjectToAsset(mesh, profile);
            if (bark) profile.barkMaterial = new Material(bark) { name = bark.name + "_独立副本" };
            else
            {
                Shader shader = Shader.Find("StylizedTreeStudio/VertexColor");
                if (!shader) throw new InvalidOperationException("插件树皮着色器缺失。");
                profile.barkMaterial = new Material(shader) { name = "STS_ReferenceBark_独立副本" };
            }
            AssetDatabase.AddObjectToAsset(profile.barkMaterial, profile);
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets(); success = true;
            return profile;
        }
        finally
        {
            if (!success)
            {
                if (!string.IsNullOrEmpty(assetPath)) AssetDatabase.DeleteAsset(assetPath);
                foreach (Mesh mesh in meshes) if (mesh && !EditorUtility.IsPersistent(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
                if (profile && !EditorUtility.IsPersistent(profile)) UnityEngine.Object.DestroyImmediate(profile);
            }
        }
    }
    // The card engine changes texture import options. Configure a COPY if a
    // user chooses a texture that belongs to the protected original Prefab.
    public static void DetachTextureInputs(StylizedTreeAuthoring t)
    {
        if (!STSReferenceGrowth.Active(t)) return;
        if (string.IsNullOrEmpty(t.materialSetId)) t.materialSetId = Guid.NewGuid().ToString("N");
        var protectedPaths = new HashSet<string>(t.referenceTrunk.protectedSourcePaths ?? new string[0], StringComparer.OrdinalIgnoreCase);
        foreach (var map in new[] { t.leafCards, t.twigCards, t.bloomCards, t.fruitCards })
        {
            if (map == null) continue;
            map.color = Detached(t, map.color, protectedPaths);
            map.normal = Detached(t, map.normal, protectedPaths);
        }
        t.barkColor = Detached(t, t.barkColor, protectedPaths);
        t.barkNormal = Detached(t, t.barkNormal, protectedPaths);
    }

    static Texture2D Detached(StylizedTreeAuthoring t, Texture2D texture, HashSet<string> protectedPaths)
    {
        if (!texture) return texture;
        string source = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(source) || !protectedPaths.Contains(source)) return texture;
        if (AssetDatabase.IsSubAsset(texture))
            throw new InvalidOperationException("原预制体内嵌的贴图请先另存为独立 PNG，再拖到叶片槽。");
        string folder = CopiesFolder + "/TextureCopies_" + t.materialSetId;
        STSMaterials.EnsureFolders(folder);
        string path = folder + "/" + AssetDatabase.AssetPathToGUID(source) + "_" + Path.GetFileName(source);
        var copy = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (!copy)
        {
            if (!AssetDatabase.CopyAsset(source, path)) throw new InvalidOperationException("无法创建原预制体贴图的独立副本。");
            copy = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!copy) throw new InvalidOperationException("贴图副本无法读取。");
        }
        return copy;
    }
}
#endif
