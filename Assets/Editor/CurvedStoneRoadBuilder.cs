#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

/// <summary>
/// 中文版连续道路生成器：保留现有 Spline，生成路面、底面、侧面和两端封口。
/// 放到 Assets/Editor/CurvedStoneRoadBuilder.cs。依赖 Unity Splines 2.x。
/// </summary>
public class CurvedStoneRoadBuilder : EditorWindow
{
    private SplineContainer spline;
    private Terrain terrain;
    private Material roadMaterial;
    private Material sideMaterial;
    private float roadWidth = 3f;
    private float sampleStep = 0.2f;
    private float heightOffset = 0.06f;
    private float thickness = 0.12f;
    private float tileWidth = 1f;
    private float tileLength = 1f;
    private float sideTileLength = 2f; // 侧壁沿道路方向每多少米重复一次纹理
    private bool conformToTerrain = true;
    private bool createCollider = true;
    private const string GeneratedName = "连续石砖道路_GeneratedRoad";

    [MenuItem("Tools/道路工具/连续曲线石砖路（免费）")]
    private static void Open() => GetWindow<CurvedStoneRoadBuilder>("连续石砖道路");

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("沿现有 Spline 生成带厚度的连续道路：上表面、两侧、底面与端面。修改曲线后重新点击生成即可。不会修改 Terrain Data。", MessageType.Info);
        spline = (SplineContainer)EditorGUILayout.ObjectField("道路曲线（Spline）", spline, typeof(SplineContainer), true);
        terrain = (Terrain)EditorGUILayout.ObjectField("贴合地形（Terrain）", terrain, typeof(Terrain), true);
        roadMaterial = (Material)EditorGUILayout.ObjectField("路面材质（Top Material）", roadMaterial, typeof(Material), false);
        sideMaterial = (Material)EditorGUILayout.ObjectField("侧面材质（Side Material）", sideMaterial, typeof(Material), false);
        roadWidth = EditorGUILayout.FloatField("道路宽度（Road Width，米）", roadWidth);
        sampleStep = EditorGUILayout.FloatField("采样间距（Sample Step，米）", sampleStep);
        heightOffset = EditorGUILayout.FloatField("高出地形（Height Offset，米）", heightOffset);
        thickness = EditorGUILayout.FloatField("道路厚度（Thickness，米）", thickness);
        tileWidth = EditorGUILayout.FloatField("纹理横向尺寸（Tile Width，米）", tileWidth);
        tileLength = EditorGUILayout.FloatField("纹理纵向尺寸（Tile Length，米）", tileLength);
        sideTileLength = EditorGUILayout.FloatField("侧壁纹理重复长度（Side Tile Length，米）", sideTileLength);
        conformToTerrain = EditorGUILayout.Toggle("沿地形起伏（Conform to Terrain）", conformToTerrain);
        createCollider = EditorGUILayout.Toggle("生成网格碰撞体（Mesh Collider）", createCollider);
        if (GUILayout.Button("使用选中的曲线（Use Selected Spline）"))
            if (Selection.activeGameObject) spline = Selection.activeGameObject.GetComponent<SplineContainer>();
        bool valid = spline && roadWidth > 0 && sampleStep > 0 && thickness > 0 && tileWidth > 0 && tileLength > 0 && sideTileLength > 0;
        using (new EditorGUI.DisabledScope(!valid))
            if (GUILayout.Button("生成 / 更新带厚度道路", GUILayout.Height(34))) Generate();
        EditorGUILayout.HelpBox("侧面材质留空时使用路面材质。厚度是向下延伸：路面高度保持不变。建议 Thickness = 0.10～0.20 米。非常急的弯道仍可能自交，需要拉大曲线转弯半径。", MessageType.None);
    }

    private void Generate()
    {
        if (!spline || spline.Splines.Count == 0 || spline.Splines[0].Count < 2)
        {
            EditorUtility.DisplayDialog("缺少曲线", "请选择至少有两个控制点（Knots）的 Spline。", "确定");
            return;
        }

        try
        {
            const int probeCount = 2000;
            var cumulative = new float[probeCount + 1];
            Vector3 last = (Vector3)spline.EvaluatePosition(0, 0f);
            for (int i = 1; i <= probeCount; i++)
            {
                Vector3 p = (Vector3)spline.EvaluatePosition(0, i / (float)probeCount);
                cumulative[i] = cumulative[i - 1] + Vector3.Distance(last, p);
                last = p;
            }
            float length = cumulative[probeCount];
            if (length < 0.02f) throw new InvalidOperationException("曲线长度过短。");
            int segments = Mathf.Clamp(Mathf.CeilToInt(length / Mathf.Max(0.02f, sampleStep)), 1, 20000);
            int rows = segments + 1;
            bool closed = spline.Splines[0].Closed;

            var worldVertices = new List<Vector3>(rows * 4);
            var uvs = new List<Vector2>(rows * 4);
            var topTris = new List<int>(segments * 6);
            var otherTris = new List<int>(segments * 18 + 12);
            int cursor = 1;

            for (int i = 0; i < rows; i++)
            {
                float distance = length * i / segments;
                while (cursor < probeCount && cumulative[cursor] < distance) cursor++;
                float frac = (distance - cumulative[cursor - 1]) /
                             Mathf.Max(0.000001f, cumulative[cursor] - cumulative[cursor - 1]);
                float t = i == rows - 1 ? 1f : (cursor - 1 + Mathf.Clamp01(frac)) / probeCount;
                Vector3 center = (Vector3)spline.EvaluatePosition(0, t);
                const float delta = 0.0005f;
                Vector3 before = (Vector3)spline.EvaluatePosition(0, Mathf.Max(0f, t - delta));
                Vector3 after = (Vector3)spline.EvaluatePosition(0, Mathf.Min(1f, t + delta));
                Vector3 forward = after - before;
                forward.y = 0;
                if (forward.sqrMagnitude < 0.00000001f)
                {
                    forward = (Vector3)spline.EvaluateTangent(0, t);
                    forward.y = 0;
                }
                if (forward.sqrMagnitude < 0.00000001f) forward = Vector3.forward;
                forward.Normalize();
                Vector3 right = new Vector3(forward.z, 0, -forward.x);
                Vector3 leftTop = ToSurface(center - right * roadWidth * 0.5f);
                Vector3 rightTop = ToSurface(center + right * roadWidth * 0.5f);
                Vector3 leftBottom = leftTop - Vector3.up * thickness;
                Vector3 rightBottom = rightTop - Vector3.up * thickness;
                int b = i * 4;
                worldVertices.Add(leftTop);     // b + 0
                worldVertices.Add(rightTop);    // b + 1
                worldVertices.Add(leftBottom);  // b + 2
                worldVertices.Add(rightBottom); // b + 3
                float v = distance / tileLength;
                float u = roadWidth / tileWidth;
                uvs.Add(new Vector2(0, v));
                uvs.Add(new Vector2(u, v));
                uvs.Add(new Vector2(0, v));
                uvs.Add(new Vector2(u, v));

                if (i == segments) continue;
                int n = b + 4;
                // 上表面：朝上
                AddQuad(topTris, b, n, b + 1, n + 1);
                // 左右侧壁：朝外
                AddQuad(otherTris, b, b + 2, n, n + 2);
                AddQuad(otherTris, b + 1, n + 1, b + 3, n + 3);
                // 底面：朝下
                AddQuad(otherTris, b + 2, b + 3, n + 2, n + 3);
            }
            if (!closed)
            {
                int e = segments * 4;
                // 两头封口
                AddQuad(otherTris, 0, 1, 2, 3);
                AddQuad(otherTris, e, e + 2, e + 1, e + 3);
            }

            // 每个表面都拥有独立顶点和 UV：原版侧壁上下两个顶点 V 相同，纹理被压成一条线。
            var faceVertices = new List<Vector3>(segments * 16 + 8);
            var faceUVs = new List<Vector2>(segments * 16 + 8);
            var faceTopTris = new List<int>(segments * 6);
            var faceOtherTris = new List<int>(segments * 18 + 12);
            float widthU = roadWidth / tileWidth;
            for (int i = 0; i < segments; i++)
            {
                int a = i * 4;
                int b = (i + 1) * 4;
                float v0 = (length * i / segments) / tileLength;
                float v1 = (length * (i + 1) / segments) / tileLength;
                float s0 = (length * i / segments) / sideTileLength;
                float s1 = (length * (i + 1) / segments) / sideTileLength;
                // 路面：U=道路宽度，V=沿曲线的距离
                AppendQuad(faceVertices, faceUVs, faceTopTris, worldVertices,
                    a, b, a + 1, b + 1,
                    new Vector2(0, v0), new Vector2(0, v1), new Vector2(widthU, v0), new Vector2(widthU, v1));
                // 左侧壁：V=0~1 从底到顶，整幅侧壁图能完整显示
                AppendQuad(faceVertices, faceUVs, faceOtherTris, worldVertices,
                    a, a + 2, b, b + 2,
                    new Vector2(s0, 1), new Vector2(s0, 0), new Vector2(s1, 1), new Vector2(s1, 0));
                // 右侧壁
                AppendQuad(faceVertices, faceUVs, faceOtherTris, worldVertices,
                    a + 1, b + 1, a + 3, b + 3,
                    new Vector2(s0, 1), new Vector2(s1, 1), new Vector2(s0, 0), new Vector2(s1, 0));
                // 底面
                AppendQuad(faceVertices, faceUVs, faceOtherTris, worldVertices,
                    a + 2, a + 3, b + 2, b + 3,
                    new Vector2(0, v0), new Vector2(widthU, v0), new Vector2(0, v1), new Vector2(widthU, v1));
            }
            if (!closed)
            {
                int e = segments * 4;
                AppendQuad(faceVertices, faceUVs, faceOtherTris, worldVertices,
                    0, 1, 2, 3,
                    new Vector2(0, 1), new Vector2(widthU, 1), new Vector2(0, 0), new Vector2(widthU, 0));
                AppendQuad(faceVertices, faceUVs, faceOtherTris, worldVertices,
                    e, e + 2, e + 1, e + 3,
                    new Vector2(0, 1), new Vector2(0, 0), new Vector2(widthU, 1), new Vector2(widthU, 0));
            }

            Transform existing = spline.transform.Find(GeneratedName);
            GameObject output = existing ? existing.gameObject : new GameObject(GeneratedName);
            if (!existing)
            {
                Undo.RegisterCreatedObjectUndo(output, "创建道路");
                output.transform.SetParent(spline.transform, true);
            }
            Undo.RecordObject(output.transform, "重设道路变换");
            output.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            output.transform.localScale = Vector3.one;
            Matrix4x4 toLocal = output.transform.worldToLocalMatrix;
            Vector3[] local = new Vector3[faceVertices.Count];
            for (int i = 0; i < local.Length; i++) local[i] = toLocal.MultiplyPoint3x4(faceVertices[i]);

            const string folder = "Assets/GeneratedRoadMeshes";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "GeneratedRoadMeshes");
            string path = folder + "/Road_" + spline.gameObject.GetInstanceID() + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh)
            {
                mesh = new Mesh { name = "带厚度的连续道路网格" };
                AssetDatabase.CreateAsset(mesh, path);
            }
            Undo.RecordObject(mesh, "更新道路网格");
            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(local);
            mesh.SetUVs(0, faceUVs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(faceTopTris, 0);
            mesh.SetTriangles(faceOtherTris, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            MeshFilter filter = output.GetComponent<MeshFilter>();
            if (!filter) filter = Undo.AddComponent<MeshFilter>(output);
            MeshRenderer renderer = output.GetComponent<MeshRenderer>();
            if (!renderer) renderer = Undo.AddComponent<MeshRenderer>(output);
            Undo.RecordObject(filter, "绑定道路网格");
            Undo.RecordObject(renderer, "绑定道路材质");
            filter.sharedMesh = mesh;
            Material top = roadMaterial ? roadMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            Material side = sideMaterial ? sideMaterial : top;
            renderer.sharedMaterials = new[] { top, side };
            renderer.enabled = true;

            MeshCollider collider = output.GetComponent<MeshCollider>();
            if (createCollider)
            {
                if (!collider) collider = Undo.AddComponent<MeshCollider>(output);
                Undo.RecordObject(collider, "刷新道路碰撞");
                collider.sharedMesh = null;
                collider.sharedMesh = mesh;
            }
            else if (collider) Undo.DestroyObjectImmediate(collider);

            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(output);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(output.scene);
            Selection.activeGameObject = output;
            EditorGUIUtility.PingObject(output);
            Debug.Log($"带厚度道路已生成：长度 {length:F1}m，厚度 {thickness:F2}m，顶点 {mesh.vertexCount}。网格资源：{path}", output);
            EditorUtility.DisplayDialog("生成成功", "连续道路已生成，包含路面、两侧、底面及端面。\n网格资源：" + path, "确定");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("生成失败", "请打开 Console（控制台）查看错误：\n" + ex.Message, "确定");
        }
    }

    private static void AppendQuad(List<Vector3> verts, List<Vector2> uv, List<int> tris,
        List<Vector3> source, int a, int b, int c, int d,
        Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        int start = verts.Count;
        verts.Add(source[a]); verts.Add(source[b]); verts.Add(source[c]); verts.Add(source[d]);
        uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
        AddQuad(tris, start, start + 1, start + 2, start + 3);
    }

    private static void AddQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(c); tris.Add(b); tris.Add(d);
    }

    private Vector3 ToSurface(Vector3 p)
    {
        if (conformToTerrain && terrain && terrain.terrainData)
        {
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            if (p.x >= origin.x && p.x <= origin.x + size.x && p.z >= origin.z && p.z <= origin.z + size.z)
                p.y = terrain.SampleHeight(p) + origin.y;
        }
        p.y += heightOffset;
        return p;
    }
}
#endif
