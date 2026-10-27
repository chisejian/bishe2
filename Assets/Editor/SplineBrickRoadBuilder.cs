// 曲线自动铺砖工具（Spline Brick Road Builder）
// 放入 Assets/Editor 文件夹，需安装 Unity Splines 2.x。
using UnityEngine;
using UnityEditor;
using UnityEngine.Splines;
using System.Collections.Generic;

public class SplineBrickRoadBuilder : EditorWindow
{
    private SplineContainer path;
    private Terrain terrain;
    private GameObject brick;
    private float roadWidth = 3f;
    private float brickWidth = 0.5f;
    private float brickLength = 0.9f;
    private float gap = 0.04f;
    private float heightOffset = 0.04f;
    private bool stagger = true;
    private bool alignToSlope = false;
    private int maximumBricks = 10000;
    private const string GeneratedName = "Generated_Brick_Road";

    [MenuItem("Tools/铺砖工具/曲线自动铺砖")]
    public static void Open() { GetWindow<SplineBrickRoadBuilder>("曲线自动铺砖"); }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("曲线自动铺砖工具（Spline Brick Road Builder）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("沿样条曲线（Spline）自动铺出多排砖。砖模型 Z+ 方向为道路前进方向，X+ 为道路横向。生成的是普通游戏对象（GameObjects）。", MessageType.Info);
        path = (SplineContainer)EditorGUILayout.ObjectField("道路曲线（Spline Path）", path, typeof(SplineContainer), true);
        terrain = (Terrain)EditorGUILayout.ObjectField("地形（Terrain，可选）", terrain, typeof(Terrain), true);
        brick = (GameObject)EditorGUILayout.ObjectField("砖块预制体（Brick Prefab）", brick, typeof(GameObject), false);
        EditorGUILayout.Space();
        roadWidth = EditorGUILayout.FloatField("道路宽度（Road Width，米）", roadWidth);
        brickWidth = EditorGUILayout.FloatField("砖块宽度 X（Brick Width，米）", brickWidth);
        brickLength = EditorGUILayout.FloatField("砖块长度 Z（Brick Length，米）", brickLength);
        gap = EditorGUILayout.FloatField("砖块间距（Gap，米）", gap);
        heightOffset = EditorGUILayout.FloatField("离地高度（Height Offset）", heightOffset);
        stagger = EditorGUILayout.Toggle("交错排列（Stagger Rows）", stagger);
        alignToSlope = EditorGUILayout.Toggle("贴合地形坡度（Align To Slope）", alignToSlope);
        maximumBricks = EditorGUILayout.IntField("最大砖块数量（Max Bricks）", maximumBricks);
        EditorGUILayout.HelpBox("建议将砖块预制体的枢轴点（Pivot）放在底面中心，缩放（Scale）设为 (1,1,1)。砖块宽度和长度用于计算铺设间距，不会自动缩放模型。", MessageType.None);
        using (new EditorGUI.DisabledScope(path == null || brick == null))
        {
            if (GUILayout.Button("生成／重新生成道路（Generate）", GUILayout.Height(32))) Generate();
        }
        if (GUILayout.Button("使用当前选中的曲线（Use Selected Spline）"))
        {
            if (Selection.activeGameObject) path = Selection.activeGameObject.GetComponent<SplineContainer>();
        }
    }

    private bool TrySurface(Vector3 point, out Vector3 position, out Vector3 normal)
    {
        position = point;
        normal = Vector3.up;
        if (terrain == null) return true;
        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        if (point.x < origin.x || point.x > origin.x + size.x || point.z < origin.z || point.z > origin.z + size.z)
            return false;
        position.y = terrain.SampleHeight(point) + origin.y;
        normal = terrain.terrainData.GetInterpolatedNormal(
            Mathf.Clamp01((point.x - origin.x) / size.x),
            Mathf.Clamp01((point.z - origin.z) / size.z));
        return true;
    }

    private void Generate()
    {
        if (path == null || brick == null || path.Spline == null || path.Spline.Count < 2)
        { EditorUtility.DisplayDialog("设置不完整", "请选择至少有两个控制点（Knots）的样条曲线，并指定砖块预制体。", "确定"); return; }
        if (roadWidth <= 0 || brickWidth <= 0 || brickLength <= 0 || gap < 0 || maximumBricks < 1)
        { EditorUtility.DisplayDialog("尺寸设置有误", "宽度、长度和最大数量必须大于 0；砖块间距不能为负数。", "确定"); return; }
        float length = path.CalculateLength();
        if (length <= 0.001f) return;
        int across = Mathf.Max(1, Mathf.FloorToInt((roadWidth + gap) / (brickWidth + gap)));
        float actualWidth = across * brickWidth + (across - 1) * gap;
        // 使用高精度折线采样，让弯道上的砖块间距更均匀。
        int resolution = Mathf.Clamp(Mathf.CeilToInt(length / 0.08f), 128, 16000);
        List<float> ts = new List<float>(resolution + 1);
        List<float> distances = new List<float>(resolution + 1);
        Vector3 previous = (Vector3)path.EvaluatePosition(0);
        float distance = 0;
        ts.Add(0); distances.Add(0);
        for (int i = 1; i <= resolution; i++)
        {
            float t = (float)i / resolution;
            Vector3 p = (Vector3)path.EvaluatePosition(t);
            distance += Vector3.Distance(previous, p);
            ts.Add(t); distances.Add(distance); previous = p;
        }
        float step = brickLength + gap;
        int rows = Mathf.FloorToInt(distance / step);
        long estimate = (long)(rows + 1) * across;
        if (estimate > maximumBricks)
        { EditorUtility.DisplayDialog("砖块数量超出限制", "预计生成 " + estimate + " 块砖，超过最大数量。请增大砖块尺寸或间距、缩小道路宽度，或调高最大砖块数量。", "确定"); return; }
        Transform existing = path.transform.Find(GeneratedName);
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("生成砖块道路");
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        GameObject parent = new GameObject(GeneratedName);
        Undo.RegisterCreatedObjectUndo(parent, "生成砖块道路");
        parent.transform.SetParent(path.transform, true);
        int count = 0, cursor = 1;
        for (int row = 0; row <= rows; row++)
        {
            float s = row * step;
            while (cursor < distances.Count - 1 && distances[cursor] < s) cursor++;
            float d0 = distances[cursor - 1], d1 = distances[cursor];
            float ratio = d1 > d0 ? (s - d0) / (d1 - d0) : 0;
            float t = Mathf.Lerp(ts[cursor - 1], ts[cursor], ratio);
            Vector3 center = (Vector3)path.EvaluatePosition(t);
            Vector3 tangent = (Vector3)path.EvaluateTangent(t);
            tangent.y = 0;
            if (tangent.sqrMagnitude < 0.00001f) tangent = Vector3.forward;
            tangent.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            for (int col = 0; col < across; col++)
            {
                float offset = -actualWidth * 0.5f + brickWidth * 0.5f + col * (brickWidth + gap);
                Vector3 p = center + right * offset;
                // 每隔一列沿道路方向错开半块砖，形成交错砌筑效果。
                if (stagger && col % 2 == 1) p += tangent * (step * 0.5f);
                if (!TrySurface(p, out Vector3 surface, out Vector3 normal)) continue;
                Quaternion rotation = alignToSlope
                    ? Quaternion.LookRotation(Vector3.ProjectOnPlane(tangent, normal).normalized, normal)
                    : Quaternion.LookRotation(tangent, Vector3.up);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(brick, path.gameObject.scene);
                if (instance == null) continue;
                Undo.RegisterCreatedObjectUndo(instance, "添加道路砖块");
                instance.transform.SetParent(parent.transform, true);
                instance.transform.position = surface + Vector3.up * heightOffset;
                instance.transform.rotation = rotation;
                instance.name = brick.name + "_" + row + "_" + col;
                count++;
            }
        }
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = parent;
        EditorUtility.DisplayDialog("道路生成完成", "已生成 " + count + " 块砖。请按 Ctrl+S 保存场景。", "确定");
    }
}
