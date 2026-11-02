#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Unity 6.x / URP compatible asset processing utility.
/// Does not alter original FBX import settings, meshes, materials, prefab, or terrain data by default.
/// Non-readable meshes are accessed with UnityEditor.MeshUtility.AcquireReadOnlyMeshData.
/// </summary>
public sealed class GaiaVegetationAutoFixV2 : EditorWindow
{
    private const string Key = "OAI_GaiaVegetationAutoFixV2_";
    private const int MaxDetailVertices = 180000;
    private readonly List<GameObject> sources = new List<GameObject>();
    private Vector2 scroll;
    private string outputFolder = "Assets/MyVegetation/Repaired";
    private int detailLod = 1;
    private float treeThreshold = 4.5f;
    private bool includeModelFBX = false;
    private bool autoClassification = true;
    private bool groundPivot = true;
    private bool addToTerrain = false;
    private Terrain targetTerrain;
    private string report = "";
    private readonly Dictionary<GameObject, GameObject> convertedDetails = new Dictionary<GameObject, GameObject>();

    [MenuItem("Tools/植被工具/Gaia 植物一键修复 V2")]
    public static void ShowWindow() { GetWindow<GaiaVegetationAutoFixV2>("Gaia 植物修复 V2"); }

    private void OnEnable()
    {
        outputFolder = EditorPrefs.GetString(Key + "Output", outputFolder);
        detailLod = EditorPrefs.GetInt(Key + "LOD", detailLod);
        treeThreshold = EditorPrefs.GetFloat(Key + "TreeHeight", treeThreshold);
        includeModelFBX = EditorPrefs.GetBool(Key + "IncludeFBX", includeModelFBX);
        autoClassification = EditorPrefs.GetBool(Key + "Auto", autoClassification);
        groundPivot = EditorPrefs.GetBool(Key + "GroundPivot", groundPivot);
        if (sources.Count == 0)
        {
            foreach (string guid in EditorPrefs.GetString(Key + "Items", "").Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries))
            {
                GameObject obj = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (obj && !sources.Contains(obj)) sources.Add(obj);
            }
        }
    }

    private void OnDisable()
    {
        EditorPrefs.SetString(Key + "Output", outputFolder);
        EditorPrefs.SetInt(Key + "LOD", detailLod);
        EditorPrefs.SetFloat(Key + "TreeHeight", treeThreshold);
        EditorPrefs.SetBool(Key + "IncludeFBX", includeModelFBX);
        EditorPrefs.SetBool(Key + "Auto", autoClassification);
        EditorPrefs.SetBool(Key + "GroundPivot", groundPivot);
        EditorPrefs.SetString(Key + "Items", string.Join(";", sources.Where(x => x)
            .Select(x => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(x)))
            .Where(x => !string.IsNullOrEmpty(x)).ToArray()));
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox("Gaia / POLYGON 资源批量检查与生成兼容副本。\n树木：保留 LOD 的新 Prefab。草、花、蕨类：合并成没有 LOD Group 的单网格 Prefab。\n自动读取不可读网格，不改原始 FBX、不乱填材质。", MessageType.Info);

        EditorGUILayout.LabelField("① 选择资产（建议先选 Gaia 的 Trees、Plants 文件夹）", EditorStyles.boldLabel);
        if (GUILayout.Button("导入 Project 当前选中的文件夹 / 预制体", GUILayout.Height(30))) AddProjectSelection();
        Rect drop = GUILayoutUtility.GetRect(0, 47, GUILayout.ExpandWidth(true));
        GUI.Box(drop, "把整个 Trees / Plants 文件夹拖到这里");
        DragDrop(drop);
        includeModelFBX = EditorGUILayout.Toggle("同时扫描 FBX 模型（可能与 Prefab 重复）", includeModelFBX);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("待处理资源：" + sources.Count, EditorStyles.boldLabel);
        if (GUILayout.Button("清空", GUILayout.Width(70))) sources.Clear();
        EditorGUILayout.EndHorizontal();
        if (sources.Count > 0)
        {
            int preview = Math.Min(8, sources.Count);
            for (int i = 0; i < preview; i++) EditorGUILayout.ObjectField(sources[i], typeof(GameObject), false);
            if (sources.Count > preview) EditorGUILayout.LabelField("另有 " + (sources.Count - preview) + " 个资源（已隐藏预览）", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("② 自动转换设置", EditorStyles.boldLabel);
        autoClassification = EditorGUILayout.Toggle("自动区分大树 / 地被", autoClassification);
        if (autoClassification)
            treeThreshold = EditorGUILayout.FloatField("树木高度判断阈值（米）", treeThreshold);
        detailLod = EditorGUILayout.Popup("草花采用的 LOD（不足时回退）", detailLod,
            new[] {"LOD 0：细节最多", "LOD 1：推荐", "LOD 2：性能优先", "LOD 3：极简"});
        groundPivot = EditorGUILayout.Toggle("草花生成模型底部放到 Y=0", groundPivot);
        outputFolder = EditorGUILayout.TextField("输出文件夹", outputFolder);
        EditorGUILayout.HelpBox("不修改任何已填入材质。发现 Missing、空槽、子网格与材质槽不一致时会报出位置；无法证明材质填错的情况不会自动猜测。", MessageType.None);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("③ 可选：加入 Terrain 绘制细节列表", EditorStyles.boldLabel);
        addToTerrain = EditorGUILayout.Toggle("转换完成后追加草花类型（默认关闭）", addToTerrain);
        if (addToTerrain)
        {
            targetTerrain = (Terrain)EditorGUILayout.ObjectField("目标地形", targetTerrain, typeof(Terrain), true);
            EditorGUILayout.HelpBox("会修改当前 Terrain Data，且同一资产可能被其他场景共用。建议先备份 Terrain Data。不会自动生成草的数量，也不会替换旧草。", MessageType.Warning);
        }

        EditorGUILayout.Space(9);
        using (new EditorGUI.DisabledScope(sources.Count == 0))
        {
            if (GUILayout.Button("扫描材质 / LOD（不修改任何资源）", GUILayout.Height(32))) InspectAll();
            if (GUILayout.Button("一键批量生成修复副本", GUILayout.Height(42))) ConvertBatch();
        }
        if (!string.IsNullOrWhiteSpace(report))
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("检查 / 转换报告", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(report, GUILayout.MinHeight(150), GUILayout.MaxHeight(330));
            if (GUILayout.Button("复制报告")) EditorGUIUtility.systemCopyBuffer = report;
        }
        EditorGUILayout.EndScrollView();
    }

    private void DragDrop(Rect rect)
    {
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;
        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (UnityEngine.Object obj in DragAndDrop.objectReferences) AddObject(obj);
        }
        e.Use();
    }

    private void AddProjectSelection()
    {
        foreach (UnityEngine.Object obj in Selection.objects) AddObject(obj);
        sources.Sort((a, b) => string.Compare(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b), StringComparison.OrdinalIgnoreCase));
        Repaint();
    }

    private void AddObject(UnityEngine.Object obj)
    {
        if (!obj) return;
        string path = AssetDatabase.GetAssetPath(obj);
        if (obj is DefaultAsset && AssetDatabase.IsValidFolder(path))
        {
            var guids = new HashSet<string>(AssetDatabase.FindAssets("t:Prefab", new[] { path }));
            if (includeModelFBX) foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { path })) guids.Add(g);
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                    !(includeModelFBX && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))) continue;
                GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                AddCandidate(candidate);
            }
        }
        else if (obj is GameObject)
        {
            GameObject candidate = (GameObject)obj;
            if (string.IsNullOrEmpty(path)) candidate = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
            AddCandidate(candidate);
        }
    }

    private void AddCandidate(GameObject go)
    {
        if (!go) return;
        string path = AssetDatabase.GetAssetPath(go);
        if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return;
        if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;
        if (!sources.Contains(go)) sources.Add(go);
    }

    private static List<Renderer> SelectLOD(GameObject root, int preferred, out int used)
    {
        LODGroup group = root.GetComponent<LODGroup>();
        if (!group) group = root.GetComponentInChildren<LODGroup>(true);
        if (group)
        {
            LOD[] lods = group.GetLODs();
            if (lods.Length == 0) throw new Exception("LODGroup 未配置 LOD");
            for (int step = 0; step < lods.Length; ++step)
            {
                int idx = Mathf.Clamp(preferred - step, 0, lods.Length - 1);
                var list = lods[idx].renderers.Where(r => r is MeshRenderer && r.GetComponent<MeshFilter>() &&
                    r.GetComponent<MeshFilter>().sharedMesh).ToList();
                if (list.Count > 0) { used = idx; return list; }
            }
            throw new Exception("所有 LOD 均无可用 MeshRenderer（可能只有 Billboard）");
        }
        used = -1;
        var plain = root.GetComponentsInChildren<MeshRenderer>(true)
            .Where(r => r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh).Cast<Renderer>().ToList();
        if (plain.Count == 0) throw new Exception("没有 MeshFilter / MeshRenderer 网格");
        return plain;
    }

    private static List<string> CheckMaterials(GameObject root, List<Renderer> list)
    {
        var issues = new List<string>();
        foreach (Renderer r in list)
        {
            MeshFilter f = r.GetComponent<MeshFilter>();
            Mesh mesh = f ? f.sharedMesh : null;
            if (!mesh) { issues.Add(r.name + "：没有 Mesh"); continue; }
            Material[] materials = r.sharedMaterials;
            if (materials.Length != mesh.subMeshCount)
                issues.Add(r.name + "：子网格=" + mesh.subMeshCount + "，材质槽=" + materials.Length + "（需要检查分配）");
            for (int i = 0; i < mesh.subMeshCount; ++i)
            {
                Material m = i < materials.Length ? materials[i] : null;
                if (!m) issues.Add(r.name + " / Element " + i + "：Missing / None 材质");
                else if (!m.shader || m.shader.name == "Hidden/InternalErrorShader")
                    issues.Add(r.name + " / Element " + i + "：Shader 不可用，材质=" + m.name);
            }
        }
        return issues;
    }

    private bool IsTree(GameObject source, GameObject instance)
    {
        if (!autoClassification) return false;
        string n = source.name.ToLowerInvariant();
        string[] treeNames = {"tree", "pine", "birch", "oak", "palm", "spruce", "maple", "willow", "cedar", "cypress", "sequoia"};
        if (treeNames.Any(n.Contains)) return true;
        string[] groundNames = {"fern", "flower", "grass", "moss", "mushroom", "shrub", "bush", "plant", "reed", "weed", "lily"};
        if (groundNames.Any(n.Contains)) return false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool found = false;
        foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer)) continue;
            if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds);
        }
        return found && bounds.size.y >= Mathf.Max(1.0f, treeThreshold);
    }

    private static string SafeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string parent = "Assets";
        for (int i = 1; i < parts.Length; ++i)
        {
            string candidate = parent + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(candidate)) AssetDatabase.CreateFolder(parent, parts[i]);
            parent = candidate;
        }
    }

    private static void ValidateFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
            path.Contains("..") || path.EndsWith("/") || path.Contains("//"))
            throw new Exception("输出目录必须为 Assets/ 下的安全路径，例如 Assets/MyVegetation/Repaired");
    }

    private void InspectAll()
    {
        var lines = new List<string>();
        int checkedCount = 0;
        try
        {
            for (int i = 0; i < sources.Count; ++i)
            {
                if (EditorUtility.DisplayCancelableProgressBar("植物检查", sources[i] ? sources[i].name : "null", (float)i / sources.Count)) break;
                GameObject instance = null;
                try
                {
                    if (!sources[i]) continue;
                    instance = PrefabUtility.InstantiatePrefab(sources[i]) as GameObject;
                    if (!instance) throw new Exception("Prefab 实例化失败");
                    int used;
                    List<Renderer> selected = SelectLOD(instance, detailLod, out used);
                    var issues = CheckMaterials(instance, selected);
                    bool tree = IsTree(sources[i], instance);
                    int unreadable = selected.Count(r => !r.GetComponent<MeshFilter>().sharedMesh.isReadable);
                    lines.Add(sources[i].name + " | " + (tree ? "树木（保留 LOD）" : "草花地被（单网格）") +
                        " | LOD=" + used + " | 不可读网格=" + unreadable + " | " +
                        (issues.Count == 0 ? "材质槽无明显缺失" : string.Join("；", issues.ToArray())));
                    checkedCount++;
                }
                catch (Exception e) { lines.Add("失败 " + (sources[i] ? sources[i].name : "null") + " : " + e.Message); }
                finally { if (instance) DestroyImmediate(instance); }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        report = "扫描完成 " + checkedCount + "/" + sources.Count + "\n" + string.Join("\n", lines.ToArray());
        Repaint();
    }

    private void ConvertBatch()
    {
        if (sources.Count == 0) return;
        try { ValidateFolder(outputFolder); }
        catch (Exception e) { EditorUtility.DisplayDialog("路径不正确", e.Message, "知道了"); return; }
        if (addToTerrain && (!targetTerrain || !targetTerrain.terrainData))
        {
            EditorUtility.DisplayDialog("请指定 Terrain", "如果勾选追加草花类型，请先在界面指定有效的 Terrain。", "确定");
            return;
        }
        if (addToTerrain && !EditorUtility.DisplayDialog("修改 Terrain Data 前确认",
            "追加细节类型会修改 Terrain Data，并可能影响引用该资源的其他场景。请先备份 Terrain Data；不会自动刷植物。继续吗？", "继续", "取消")) return;
        string dest = outputFolder.Trim();
        EnsureFolder(dest);
        EnsureFolder(dest + "/Trees");
        EnsureFolder(dest + "/Details");
        EnsureFolder(dest + "/Meshes");
        convertedDetails.Clear();
        int ok = 0, failed = 0, skipped = 0;
        var lines = new List<string>();
        GameObject firstOutput = null;
        try
        {
            for (int i = 0; i < sources.Count; ++i)
            {
                GameObject source = sources[i];
                if (EditorUtility.DisplayCancelableProgressBar("Gaia / POLYGON 植物批量修复",
                    (i + 1) + "/" + sources.Count + "：" + (source ? source.name : "null"), (float)i / sources.Count))
                { lines.Add("用户取消：已生成的资源保留，原始资源未修改。"); break; }
                if (!source) { skipped++; continue; }
                GameObject instance = null;
                try
                {
                    instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
                    if (!instance) throw new Exception("不能实例化源预制体");
                    bool tree = IsTree(source, instance);
                    int used;
                    var parts = SelectLOD(instance, detailLod, out used);
                    var issues = CheckMaterials(instance, parts);
                    // Missing materials are never silently guessed. Keep trees intact, skip detail conversion.
                    if (!tree && parts.Any(r =>
                    {
                        Mesh m = r.GetComponent<MeshFilter>().sharedMesh;
                        Material[] mats = r.sharedMaterials;
                        for (int k = 0; k < m.subMeshCount; ++k)
                            if (k >= mats.Length || !mats[k] || !mats[k].shader) return true;
                        return false;
                    }))
                    {
                        failed++;
                        lines.Add("跳过 " + source.name + "：存在缺失材质；" + string.Join("；", issues.ToArray()));
                        continue;
                    }
                    GameObject output = tree ? CreateTreeCopy(source, instance, dest) : CreateDetailCopy(source, instance, parts, used, dest);
                    if (!output) throw new Exception("预制体保存失败");
                    if (!firstOutput) firstOutput = output;
                    if (!tree) convertedDetails[source] = output;
                    ok++;
                    string hint = issues.Count > 0 ? " [材质需复核：" + string.Join("；", issues.ToArray()) + "]" : "";
                    lines.Add("成功 " + source.name + " → " + (tree ? "Trees/" : "Details/") + output.name + hint);
                }
                catch (Exception e)
                {
                    failed++;
                    lines.Add("失败 " + source.name + "：" + e.Message);
                    Debug.LogWarning("[植物批量修复] " + source.name + "：" + e);
                }
                finally { if (instance) DestroyImmediate(instance); }
            }
            AssetDatabase.SaveAssets();
            if (addToTerrain && convertedDetails.Count > 0) AddDetailPrototypes();
        }
        finally { EditorUtility.ClearProgressBar(); }
        report = "已生成 " + ok + "，失败/跳过问题 " + failed + "，空项 " + skipped +
            "\n原始 FBX、材质、Gaia 目录未修改。\n" + string.Join("\n", lines.ToArray());
        Debug.Log("[植物批量修复 V2]\n" + report);
        SaveReport(dest, report);
        if (firstOutput) EditorGUIUtility.PingObject(firstOutput);
        Repaint();
    }

    private GameObject CreateTreeCopy(GameObject source, GameObject instance, string dest)
    {
        // Unpack prefab to avoid creating a variant referencing the original prefab's child structure.
        if (PrefabUtility.IsPartOfPrefabInstance(instance))
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = SafeName(source.name) + "_Tree";
        string path = AssetDatabase.GenerateUniqueAssetPath(dest + "/Trees/" + instance.name + ".prefab");
        GameObject result = PrefabUtility.SaveAsPrefabAsset(instance, path);
        if (!result) throw new Exception("保存树木 Prefab 失败");
        return result;
    }

    private GameObject CreateDetailCopy(GameObject source, GameObject instance, List<Renderer> parts, int lod, string dest)
    {
        // One material -> one resulting submesh.  No LOD Group on new prefab.
        var order = new List<Material>();
        var groups = new Dictionary<Material, List<CombineInstance>>();
        var tempReadMeshes = new Dictionary<Mesh, Mesh>();
        var intermediate = new List<Mesh>();
        Mesh final = null;
        bool finalSaved = false;
        try
        {
            Matrix4x4 rootInverse = instance.transform.worldToLocalMatrix;
            int roughVertices = 0;
            foreach (Renderer rend in parts)
            {
                Mesh srcMesh = rend.GetComponent<MeshFilter>().sharedMesh;
                roughVertices += srcMesh.vertexCount;
                if (roughVertices > MaxDetailVertices)
                    throw new Exception("细节网格过密（超过 " + MaxDetailVertices + " 顶点）。建议选 LOD 2 或 3，避免草地卡顿。");
                Mesh readable = srcMesh;
                if (!srcMesh.isReadable)
                {
                    if (!tempReadMeshes.TryGetValue(srcMesh, out readable))
                    {
                        readable = CloneFromReadOnlyMesh(srcMesh);
                        tempReadMeshes[srcMesh] = readable;
                    }
                }
                Material[] mats = rend.sharedMaterials;
                for (int sub = 0; sub < srcMesh.subMeshCount; ++sub)
                {
                    if (srcMesh.GetTopology(sub) != MeshTopology.Triangles)
                        throw new Exception(rend.name + " 子网格 " + sub + " 不是三角面拓扑");
                    Material m = mats[sub]; // already validated
                    if (!groups.ContainsKey(m)) { groups.Add(m, new List<CombineInstance>()); order.Add(m); }
                    groups[m].Add(new CombineInstance
                    {
                        mesh = readable, subMeshIndex = sub,
                        transform = rootInverse * rend.transform.localToWorldMatrix
                    });
                }
            }
            if (order.Count == 0) throw new Exception("没有可合并的材质子网格");
            var finalCombines = new List<CombineInstance>();
            foreach (Material mat in order)
            {
                Mesh groupMesh = new Mesh {name = "Temp_" + mat.name, indexFormat = IndexFormat.UInt32};
                groupMesh.CombineMeshes(groups[mat].ToArray(), true, true, false);
                intermediate.Add(groupMesh);
                finalCombines.Add(new CombineInstance {mesh = groupMesh, subMeshIndex = 0, transform = Matrix4x4.identity});
            }
            final = new Mesh {name = SafeName(source.name) + "_LOD" + (lod < 0 ? "X" : lod.ToString()) + "_Detail", indexFormat = IndexFormat.UInt32};
            final.CombineMeshes(finalCombines.ToArray(), false, true, false);
            if (groundPivot)
            {
                Vector3[] v = final.vertices;
                float minY = final.bounds.min.y;
                for (int i = 0; i < v.Length; ++i) v[i].y -= minY;
                final.vertices = v;
                final.RecalculateBounds();
            }
            string name = SafeName(source.name) + "_LOD" + (lod < 0 ? "X" : lod.ToString()) + "_Detail";
            string pathMesh = AssetDatabase.GenerateUniqueAssetPath(dest + "/Meshes/" + name + ".asset");
            AssetDatabase.CreateAsset(final, pathMesh);
            finalSaved = true;
            GameObject prefabRoot = new GameObject(name);
            try
            {
                // FBX / nested prefab root scaling may differ; geometry already baked to root-local space.
                prefabRoot.AddComponent<MeshFilter>().sharedMesh = final;
                prefabRoot.AddComponent<MeshRenderer>().sharedMaterials = order.ToArray();
                string path = AssetDatabase.GenerateUniqueAssetPath(dest + "/Details/" + name + ".prefab");
                GameObject output = PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                if (!output) throw new Exception("Prefab 保存失败");
                return output;
            }
            finally { DestroyImmediate(prefabRoot); }
        }
        finally
        {
            foreach (Mesh m in tempReadMeshes.Values) if (m) DestroyImmediate(m);
            foreach (Mesh m in intermediate) if (m) DestroyImmediate(m);
            if (!finalSaved && final) DestroyImmediate(final);
        }
    }

    /// <summary>
    /// Make a readable *temporary copy* without changing ModelImporter.Read/Write.
    /// Editor MeshUtility can read imported non-readable meshes in Unity 6.
    /// This preserves vertices, normals, tangents, vertex colors, UV0..UV7, and triangle submesh indices.
    /// </summary>
    private static Mesh CloneFromReadOnlyMesh(Mesh source)
    {
        Mesh result = new Mesh {name = "TempReadable_" + source.name, indexFormat = IndexFormat.UInt32};
        using (Mesh.MeshDataArray array = MeshUtility.AcquireReadOnlyMeshData(source))
        {
            Mesh.MeshData data = array[0];
            int count = data.vertexCount;
            if (count <= 0) throw new Exception(source.name + " 顶点数量为 0");
            using (var pos = new NativeArray<Vector3>(count, Allocator.Temp))
            {
                data.GetVertices(pos);
                result.SetVertices(pos);
            }
            if (data.HasVertexAttribute(VertexAttribute.Normal))
            {
                using (var normals = new NativeArray<Vector3>(count, Allocator.Temp))
                { data.GetNormals(normals); result.SetNormals(normals); }
            }
            if (data.HasVertexAttribute(VertexAttribute.Tangent))
            {
                using (var tangent = new NativeArray<Vector4>(count, Allocator.Temp))
                { data.GetTangents(tangent); result.SetTangents(tangent); }
            }
            if (data.HasVertexAttribute(VertexAttribute.Color))
            {
                using (var colors = new NativeArray<Color32>(count, Allocator.Temp))
                { data.GetColors(colors); result.SetColors(colors); }
            }
            for (int channel = 0; channel < 8; ++channel)
            {
                VertexAttribute attr = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!data.HasVertexAttribute(attr)) continue;
                using (var tex = new NativeArray<Vector4>(count, Allocator.Temp))
                {
                    data.GetUVs(channel, tex);
                    result.SetUVs(channel, tex);
                }
            }
            result.subMeshCount = data.subMeshCount;
            for (int sub = 0; sub < data.subMeshCount; ++sub)
            {
                SubMeshDescriptor descr = data.GetSubMesh(sub);
                if (descr.topology != MeshTopology.Triangles)
                    throw new Exception(source.name + " 存在非三角形拓扑，无法转成 Detail Mesh");
                using (var indices = new NativeArray<int>(descr.indexCount, Allocator.Temp))
                {
                    data.GetIndices(indices, sub, true);
                    result.SetIndices(indices.ToArray(), MeshTopology.Triangles, sub, false);
                }
            }
        }
        result.bounds = source.bounds;
        if (!result.HasVertexAttribute(VertexAttribute.Normal)) result.RecalculateNormals();
        return result;
    }

    private void AddDetailPrototypes()
    {
        TerrainData data = targetTerrain.terrainData;
        if (!data) return;
        List<DetailPrototype> list = data.detailPrototypes.ToList();
        int added = 0;
        foreach (GameObject prefab in convertedDetails.Values)
        {
            if (list.Any(p => p.prototype == prefab)) continue;
            list.Add(new DetailPrototype
            {
                prototype = prefab,
                usePrototypeMesh = true,
                renderMode = DetailRenderMode.VertexLit,
                useInstancing = true,
                minWidth = 0.8f, maxWidth = 1.2f,
                minHeight = 0.8f, maxHeight = 1.2f,
                healthyColor = Color.white, dryColor = Color.white,
                noiseSeed = UnityEngine.Random.Range(1, 2000000000)
            });
            added++;
        }
        if (added == 0) return;
        Undo.RecordObject(data, "Gaia 植物修复器：添加 Terrain 细节类型");
        data.detailPrototypes = list.ToArray();
        EditorUtility.SetDirty(data);
        report += "\n已追加 Terrain Detail 类型：" + added + "（没有自动刷到地图）";
    }

    private static void SaveReport(string dest, string content)
    {
        string path = AssetDatabase.GenerateUniqueAssetPath(dest + "/VegetationFixReport.txt");
        File.WriteAllText(Path.GetFullPath(path), content, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(path);
    }
}
#endif
