#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

// Explicit, opt-in regression checks. Fixtures live only in a new test folder;
// the user's source tree is never used as a writable test fixture.
internal static class STSReferenceChecks
{
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("[主干副本检查] " + message); }

    static string Digest(string path)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
    }

    static Mesh Fixture()
    {
        var data = new STSMeshData();
        data.Tube(new List<Vector3> { Vector3.zero, Vector3.up * 1.5f, Vector3.up * 3 }, .2f, 8, Color.white);
        var mesh = new Mesh { name = "STS_ReadOnlySourceFixture" };
        int start = data.v.Count;
        data.v.AddRange(new[] { new Vector3(-1, 2.5f, 0), new Vector3(1, 2.5f, 0), new Vector3(-1, 3.5f, 0), new Vector3(1, 3.5f, 0) });
        for (int i = 0; i < 4; i++) { data.n.Add(Vector3.forward); data.colors.Add(Color.white); }
        data.uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one });
        mesh.SetVertices(data.v); mesh.SetNormals(data.n); mesh.SetColors(data.colors); mesh.SetUVs(0, data.uv);
        mesh.subMeshCount = 2; mesh.SetTriangles(data.triangles, 0);
        mesh.SetTriangles(new[] { start, start + 1, start + 2, start + 1, start + 3, start + 2 }, 1);
        mesh.RecalculateBounds(); return mesh;
    }

    [MenuItem("Tools/风格化植物工坊/运行主干副本保护检查")]
    public static void Run()
    {
        string folder = "Assets/StylizedTreeStudio/Generated/ReferenceCheck_" + Guid.NewGuid().ToString("N");
        STSMaterials.EnsureFolders(folder);
        STSReferenceTrunk profile = null; GameObject source = null, plant = null, template = null;
        Texture2D pixels = null;
        try
        {
            Shader shader = Shader.Find("StylizedTreeStudio/VertexColor"); Require(shader, "插件 Shader 未安装。");
            pixels = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var colors = new Color[16]; for (int i = 0; i < colors.Length; i++) colors[i] = Color.green;
            pixels.SetPixels(colors); pixels.Apply(); File.WriteAllBytes(folder + "/Source.png", pixels.EncodeToPNG());
            AssetDatabase.ImportAsset(folder + "/Source.png");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/Source.png");
            var material = new Material(shader); material.SetTexture("_MainTex", texture);
            AssetDatabase.CreateAsset(material, folder + "/Source.mat");
            Mesh mesh = Fixture(); AssetDatabase.CreateAsset(mesh, folder + "/SourceMesh.asset");
            source = new GameObject("STS_SourceFixture");
            source.AddComponent<MeshFilter>().sharedMesh = mesh;
            source.AddComponent<MeshRenderer>().sharedMaterials = new[] { material, material };
            // Separate leaf material: capture must not include the leaf submesh.
            var leafMaterial = new Material(shader); AssetDatabase.CreateAsset(leafMaterial, folder + "/Leaves.mat");
            source.GetComponent<MeshRenderer>().sharedMaterials = new[] { material, leafMaterial };
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, folder + "/Source.prefab");
            mesh.UploadMeshData(true); Require(!mesh.isReadable, "测试源网格未设为只读。");
            AssetDatabase.SaveAssets();
            var before = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) before[file] = Digest(file);
            // This is deliberately non-readable. Capture must not change that.
            profile = STSReferenceCapture.Capture(prefab, 0);
            Require(!mesh.isReadable, "捕获修改了源网格 Read/Write。");
            Require(profile.barkLODs[0] != mesh && profile.barkMaterial != material, "源网格或材质被直接用于修改。");
            Require(profile.barkLODs[0].triangles.Length < mesh.GetIndexCount(0) + mesh.GetIndexCount(1), "源叶片混入了枝干副本。");
            plant = new GameObject("STS_ReferenceCheck"); var t = plant.AddComponent<StylizedTreeAuthoring>();
            t.growthForm = STSGrowthForm.Tree; t.useReferenceTrunk = true; t.referenceTrunk = profile;
            t.height = profile.sourceHeight; t.crownWidth = profile.sourceWidth;
            Require(STSReferenceGrowth.Point(t, profile.root).sqrMagnitude < 1e-10f, "根部未固定。");
            var barkVertices = profile.barkLODs[0].vertices;
            Vector3[] immutable = (Vector3[])barkVertices.Clone();
            for (int i = 0; i < barkVertices.Length; i++)
                Require((STSReferenceGrowth.Point(t, barkVertices[i]) - (barkVertices[i] - profile.root)).sqrMagnitude < 1e-8f, "弯曲 0 改变了源形状。");
            for (int amount = 0; amount <= 4; amount++)
            {
                t.referenceBend = amount * .25f; t.referenceAddedBranches = amount;t.trunkTwist=amount*.75f;
                STSSkeleton.Generate(t); int count = 0;
                foreach (var branch in t.branches) if (branch.referenceAddition) count++;
                Require(count == 0 && t.referenceAddedBranches==0 && t.referenceBend==0, "旧加枝／弯曲数据没有清除。");
                for (int lod = 0; lod < 3; lod++)
                {
                    var output = new STSMeshData(); STSReferenceGrowth.AppendBark(t, output, lod);
                    Require(output.v.Count==profile.barkLODs[Mathf.Min(lod,profile.barkLODs.Length-1)].vertexCount,"扭曲增减了原枝干顶点。");
                    foreach (var p in output.v) Require(STSPath.Finite(p), "变形产生无效坐标。");
                    foreach (int index in output.triangles) Require(index >= 0 && index < output.v.Count, "网格索引越界。");
                }
                Require(STSReferenceGrowth.Point(t, profile.root).sqrMagnitude < 1e-10f, "弯曲后根部移动。");
            }
            var after = profile.barkLODs[0].vertices;
            for (int i = 0; i < immutable.Length; i++) Require(after[i] == immutable[i], "刷新修改了主干快照。");
            t.leafCards.color = texture; t.materialSetId = Guid.NewGuid().ToString("N");
            STSReferenceCapture.DetachTextureInputs(t);
            Require(t.leafCards.color && t.leafCards.color != texture, "没有隔离原预制体贴图。");
            Require(AssetDatabase.GetAssetPath(t.leafCards.color).StartsWith(STSReferenceCapture.CopiesFolder + "/", StringComparison.Ordinal), "贴图副本路径无效。");
            Require(STSPlantFiles.DestinationPath(t, folder + "/Source.prefab") != folder + "/Source.prefab", "导出可能覆盖原预制体。");
            template = new GameObject("STS_TemplateFixture"); var copy = template.AddComponent<StylizedTreeAuthoring>();
            STSPlantBatch.CopySource(t, copy); copy.branches[0].points[0] += Vector3.right;
            Require(copy.branches[0].points[0] != t.branches[0].points[0], "树群共享可修改的枝条列表。");
            Require(copy.leafCards != t.leafCards, "树群共享可修改的贴图槽对象。");
            AssetDatabase.SaveAssets();
            foreach (var pair in before) Require(File.Exists(pair.Key) && Digest(pair.Key) == pair.Value, "源资源被写入：" + pair.Key);
            Debug.Log("[主干副本检查] 通过：只读源捕获、源叶片排除、零扭曲保形、根部固定、旧加枝清零、枝干拓扑保留、三级 LOD 有限坐标与有效索引、快照不变、贴图隔离、另存防覆盖、树群数据隔离、源文件字节不变。");
        }
        finally
        {
            string profileFolder = profile ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(profile)).Replace('\\', '/') : null;
            if (plant)
            {
                var t = plant.GetComponent<StylizedTreeAuthoring>();
                if (t && !string.IsNullOrEmpty(t.materialSetId)) AssetDatabase.DeleteAsset(STSReferenceCapture.CopiesFolder + "/TextureCopies_" + t.materialSetId);
                UnityEngine.Object.DestroyImmediate(plant);
            }
            if (source) UnityEngine.Object.DestroyImmediate(source);
            if (template) UnityEngine.Object.DestroyImmediate(template);
            if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
            if (!string.IsNullOrEmpty(profileFolder) && profileFolder.StartsWith(STSReferenceCapture.CopiesFolder + "/", StringComparison.Ordinal)) AssetDatabase.DeleteAsset(profileFolder);
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
#endif
