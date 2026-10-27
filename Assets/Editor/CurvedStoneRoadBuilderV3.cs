#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

/// <summary>
/// 连续曲线石砖道路 V3（Unity Splines 2.x，编辑器使用）。
/// 生成：连续路面、两侧石板路沿、厚度侧壁与底面、随机轮廓崩边、侧壁凹损、路面浅坑与破损表层。
/// 破损是真实几何轮廓/凹坑，但不使用布尔切割，也不逐砖切割。
/// </summary>
public class CurvedStoneRoadBuilderV3 : EditorWindow
{
    [SerializeField] private SplineContainer spline;
    [SerializeField] private Terrain terrain;
    [SerializeField] private Material topMaterial, sideMaterial, bottomMaterial, damageMaterial, curbMaterial;
    [SerializeField] private float width = 3f, thickness = 0.25f, curbWidth = 0.3f, curbRise = 0.025f;
    [SerializeField] private float sampleStep = 0.12f, heightOffset = 0.05f;
    [SerializeField] private float topRepeatLength = 2.4f, sideRepeatLength = 2f;
    [SerializeField] private bool conformToTerrain = true, createCollider = true;
    [SerializeField] private bool edgeDamage = true, sideDamage = true, surfaceDamage = true;
    [SerializeField, Range(0,1)] private float edgeChance = 0.28f, sideChance = 0.22f, pitChance = 0.22f;
    [SerializeField] private float edgeDepth = 0.075f, sideDepth = 0.05f, pitDepth = 0.025f;
    [SerializeField] private float damageScale = 0.65f;
    [SerializeField] private int seed = 2026;
    [SerializeField] private bool saveMeshAsset = true;
    [SerializeField] private string outputName = "连续石砖道路_GeneratedRoad_V3";
    private Vector2 scroll;
    private const int Across = 15; // 15 横向顶点：独立两侧路沿和中央路面，并有破损形变所需细分

    [MenuItem("Tools/道路工具/连续曲线石砖路 V3（破损与路沿）")]
    private static void Open() => GetWindow<CurvedStoneRoadBuilderV3>("石砖道路 V3");

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox("沿已有 Spline 曲线生成道路。包含真实边缘崩缺、侧壁凹损、浅凹坑、独立石板路沿及破损表面材质。不会修改 Terrain Data。建议先保存场景副本。", MessageType.Info);
        spline = (SplineContainer)EditorGUILayout.ObjectField("道路曲线（Spline）", spline, typeof(SplineContainer), true);
        terrain = (Terrain)EditorGUILayout.ObjectField("地形（Terrain）", terrain, typeof(Terrain), true);
        if (GUILayout.Button("使用选中的曲线（Selected Spline）") && Selection.activeGameObject)
            spline = Selection.activeGameObject.GetComponent<SplineContainer>();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("材质（Materials）", EditorStyles.boldLabel);
        topMaterial = Field("路面材质（Top）", topMaterial);
        curbMaterial = Field("路沿材质（Curb，留空用路面）", curbMaterial);
        sideMaterial = Field("侧壁材质（Side）", sideMaterial);
        bottomMaterial = Field("底面材质（Bottom，留空用侧壁）", bottomMaterial);
        damageMaterial = Field("破损材质（Fracture，留空用侧壁）", damageMaterial);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("道路结构（Geometry）", EditorStyles.boldLabel);
        width = EditorGUILayout.FloatField("道路宽度（Width，m）", width);
        thickness = EditorGUILayout.FloatField("道路厚度（Thickness，m）", thickness);
        curbWidth = EditorGUILayout.FloatField("单侧路沿宽度（Curb Width，m）", curbWidth);
        curbRise = EditorGUILayout.FloatField("路沿抬升（Curb Rise，m）", curbRise);
        sampleStep = EditorGUILayout.FloatField("采样间距（Sample Step，m）", sampleStep);
        heightOffset = EditorGUILayout.FloatField("离地高度（Height Offset，m）", heightOffset);
        topRepeatLength = EditorGUILayout.FloatField("路面纹理重复长度（m）", topRepeatLength);
        sideRepeatLength = EditorGUILayout.FloatField("侧壁纹理重复长度（m）", sideRepeatLength);
        conformToTerrain = EditorGUILayout.Toggle("贴合地形（Conform）", conformToTerrain);
        createCollider = EditorGUILayout.Toggle("网格碰撞（Collider）", createCollider);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("真实几何破损（Geometry Damage）", EditorStyles.boldLabel);
        edgeDamage = EditorGUILayout.Toggle("边缘随机缺损（Edge）", edgeDamage);
        if (edgeDamage) { edgeChance = EditorGUILayout.Slider("边缘破损概率", edgeChance, 0, 1); edgeDepth = EditorGUILayout.FloatField("边缘最大缺口（m）", edgeDepth); }
        sideDamage = EditorGUILayout.Toggle("侧壁随机凹损（Side）", sideDamage);
        if (sideDamage) { sideChance = EditorGUILayout.Slider("侧壁破损概率", sideChance, 0, 1); sideDepth = EditorGUILayout.FloatField("侧壁最大凹损（m）", sideDepth); }
        surfaceDamage = EditorGUILayout.Toggle("路面随机浅坑（Surface）", surfaceDamage);
        if (surfaceDamage) { pitChance = EditorGUILayout.Slider("路面破损概率", pitChance, 0, 1); pitDepth = EditorGUILayout.FloatField("浅坑深度（m）", pitDepth); }
        damageScale = EditorGUILayout.Slider("破损尺寸（Damage Scale）", damageScale, 0.2f, 3f);
        seed = EditorGUILayout.IntField("随机种子（Seed）", seed);
        EditorGUILayout.Space();
        saveMeshAsset = EditorGUILayout.Toggle("保存网格 .asset（Save Mesh）", saveMeshAsset);
        outputName = EditorGUILayout.TextField("生成对象名称", outputName);
        bool ok = spline != null && width > 0.2f && thickness > 0.001f && sampleStep > 0.01f && topRepeatLength > 0.001f && sideRepeatLength > 0.001f && curbWidth >= 0 && curbWidth < width * .45f;
        using (new EditorGUI.DisabledScope(!ok))
            if (GUILayout.Button("生成 / 重新生成道路 V3", GUILayout.Height(38))) Generate();
        EditorGUILayout.HelpBox("限制：此版不会逐块切砖、不会生成真正的贯通破洞；路面浅坑为局部顶点下陷。非常急的弯道仍可能因宽度过大产生自交。路面原图已包含左右边带，建议在 V3 中使用无边带的中央砖纹理，否则可能出现重复路沿。", MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    private static Material Field(string label, Material value) => (Material)EditorGUILayout.ObjectField(label, value, typeof(Material), false);

    private struct Sample
    {
        public Vector3 center, right;
        public float dist, edgeL, edgeR, wallL, wallR, pit;
    }

    private void Generate()
    {
        if (spline.Splines.Count == 0 || spline.Splines[0].Count < 2)
        { EditorUtility.DisplayDialog("曲线无效", "请选择有两个以上控制点的 Spline。", "确定"); return; }
        if (!spline.gameObject.scene.IsValid())
        { EditorUtility.DisplayDialog("场景无效", "请选择场景中的曲线对象，不能直接生成到 Project 资源。", "确定"); return; }
        try
        {
            const int probes = 2500;
            float[] distances = new float[probes + 1];
            Vector3 prev = Position(0);
            for (int i = 1; i <= probes; ++i)
            {
                Vector3 p = Position(i / (float)probes);
                distances[i] = distances[i - 1] + Vector3.Distance(p, prev);
                prev = p;
            }
            float length = distances[probes];
            if (length < .05f) throw new Exception("Spline 长度太短。");
            int segments = Mathf.Clamp(Mathf.CeilToInt(length / sampleStep), 2, 6000);
            if ((long)(segments + 1) * Across > 90000)
                throw new Exception("道路太长或采样过密，请增大采样间距（Sample Step）。");
            bool closed = spline.Splines[0].Closed;
            int count = segments + 1;
            Sample[] samples = new Sample[count];
            for (int i = 0; i < count; ++i)
            {
                float d = length * i / segments;
                int k = Array.BinarySearch(distances, d);
                if (k < 0) k = ~k;
                k = Mathf.Clamp(k, 1, probes);
                float a = (d - distances[k - 1]) / Mathf.Max(0.000001f, distances[k] - distances[k - 1]);
                float t = i == segments ? 1f : ((k - 1) + Mathf.Clamp01(a)) / probes;
                Vector3 c = Position(t);
                Vector3 f = Position(Mathf.Min(1, t + .0003f)) - Position(Mathf.Max(0, t - .0003f));
                f.y = 0;
                if (f.sqrMagnitude < 1e-8f) { f = spline.transform.TransformDirection((Vector3)spline.EvaluateTangent(0, t)); f.y = 0; }
                if (f.sqrMagnitude < 1e-8f) f = Vector3.forward;
                f.Normalize();
                Vector3 r = new Vector3(f.z, 0, -f.x);
                float interval = d / Mathf.Max(.05f, damageScale);
                float eL = edgeDamage ? SmoothDamage(interval, 11, edgeChance, 1.3f) * Mathf.Max(0, edgeDepth) : 0;
                float eR = edgeDamage ? SmoothDamage(interval, 29, edgeChance, 1.3f) * Mathf.Max(0, edgeDepth) : 0;
                float wallL = sideDamage ? SmoothDamage(interval, 61, sideChance, 1.8f) * Mathf.Max(0, sideDepth) : 0;
                float wallR = sideDamage ? SmoothDamage(interval, 83, sideChance, 1.8f) * Mathf.Max(0, sideDepth) : 0;
                float pit = surfaceDamage ? SmoothDamage(interval, 101, pitChance, .8f) : 0;
                samples[i] = new Sample { center = c, right = r, dist = d, edgeL = eL, edgeR = eR, wallL = wallL, wallR = wallR, pit = pit };
            }
            if (closed) samples[segments] = samples[0];
            var vertices = new List<Vector3>(count * (Across + 8));
            var uv = new List<Vector2>(count * (Across + 8));
            var tris = new List<int>[5]; // top, curb, side, bottom, fracture
            for (int i = 0; i < 5; i++) tris[i] = new List<int>();
            Vector3[,] topPoints = new Vector3[count, Across];
            Vector3[,] bottomPoints = new Vector3[count, 2];
            Vector3[,] midPoints = new Vector3[count, 2];
            for (int i = 0; i < count; ++i)
            {
                Sample s = samples[i];
                float L = -width * .5f + s.edgeL, R = width * .5f - s.edgeR;
                float innerL = -width * .5f + curbWidth;
                float innerR = width * .5f - curbWidth;
                innerL = Mathf.Clamp(innerL, L + .02f, R - .05f);
                innerR = Mathf.Clamp(innerR, innerL + .05f, R - .02f);
                for (int j = 0; j < Across; ++j)
                {
                    float x;
                    if (j == 0) x = L;
                    else if (j == 1) x = Mathf.Lerp(L, innerL, .25f);
                    else if (j == 2) x = innerL;
                    else if (j == 12) x = innerR;
                    else if (j == 13) x = Mathf.Lerp(innerR, R, .75f);
                    else if (j == 14) x = R;
                    else x = Mathf.Lerp(innerL, innerR, (j - 2) / 10f);
                    Vector3 p = OnTerrain(s.center + s.right * x);
                    if (j <= 2 || j >= 12) p.y += curbRise;
                    if (j > 2 && j < 12 && surfaceDamage)
                    {
                        float cross = Mathf.Sin((j - 2) / 10f * Mathf.PI);
                        float n = SmoothDamage(s.dist / Mathf.Max(.05f, damageScale), 151 + j * 3, pitChance, .8f);
                        p.y -= pitDepth * s.pit * n * cross;
                    }
                    topPoints[i,j] = p;
                }
                for (int side = 0; side < 2; ++side)
                {
                    Vector3 p = topPoints[i, side == 0 ? 0 : 14];
                    Vector3 inside = side == 0 ? s.right : -s.right;
                    float indent = side == 0 ? s.wallL : s.wallR;
                    midPoints[i,side] = p - Vector3.up * (thickness * .5f) + inside * indent;
                    bottomPoints[i,side] = p - Vector3.up * thickness;
                }
            }
            // 路面沿曲线方向的 UV (V)，横向 U = 0..1；中间带与两侧石板使用不同 submesh。
            for (int i = 0; i < segments; ++i)
            {
                float v0 = samples[i].dist / topRepeatLength, v1 = samples[i+1].dist / topRepeatLength;
                for (int j = 0; j < Across - 1; ++j)
                {
                    int group = j < 2 || j >= 12 ? 1 : 0;
                    // 2 与 11 之间才是中央区域，其余是路沿。
                    if (j == 2 || j == 11) group = 0;
                    float u0 = j / (float)(Across - 1), u1 = (j + 1) / (float)(Across - 1);
                    Quad(vertices, uv, tris[group], topPoints[i,j], topPoints[i+1,j], topPoints[i,j+1], topPoints[i+1,j+1],
                        new Vector2(u0,v0), new Vector2(u0,v1), new Vector2(u1,v0), new Vector2(u1,v1));
                }
                for (int side = 0; side < 2; ++side)
                {
                    Vector3 t0 = topPoints[i,side==0?0:14], t1=topPoints[i+1,side==0?0:14];
                    Vector3 m0 = midPoints[i,side], m1=midPoints[i+1,side];
                    Vector3 b0 = bottomPoints[i,side], b1=bottomPoints[i+1,side];
                    float su0 = samples[i].dist / sideRepeatLength, su1 = samples[i+1].dist / sideRepeatLength;
                    // 外壁中间受损的区域使用断面材质；几何通过三层轮廓成形。
                    bool damaged = (side==0 ? samples[i].wallL+samples[i+1].wallL : samples[i].wallR+samples[i+1].wallR) > .002f;
                    int wallGroup = damaged ? 4 : 2;
                    if (side == 0)
                    {
                        Quad(vertices,uv,tris[wallGroup],t0,m0,t1,m1,new Vector2(su0,1),new Vector2(su0,.5f),new Vector2(su1,1),new Vector2(su1,.5f));
                        Quad(vertices,uv,tris[wallGroup],m0,b0,m1,b1,new Vector2(su0,.5f),new Vector2(su0,0),new Vector2(su1,.5f),new Vector2(su1,0));
                    }
                    else
                    {
                        Quad(vertices,uv,tris[wallGroup],t0,t1,m0,m1,new Vector2(su0,1),new Vector2(su1,1),new Vector2(su0,.5f),new Vector2(su1,.5f));
                        Quad(vertices,uv,tris[wallGroup],m0,m1,b0,b1,new Vector2(su0,.5f),new Vector2(su1,.5f),new Vector2(su0,0),new Vector2(su1,0));
                    }
                }
                Quad(vertices, uv, tris[3], bottomPoints[i,0],bottomPoints[i,1],bottomPoints[i+1,0],bottomPoints[i+1,1],
                    new Vector2(0,v0),new Vector2(1,v0),new Vector2(0,v1),new Vector2(1,v1));
            }
            if (!closed)
            {
                for (int end = 0; end < 2; end++)
                {
                    int i = end==0?0:segments;
                    Vector3 l = topPoints[i,0], r = topPoints[i,14], lb=bottomPoints[i,0], rb=bottomPoints[i,1];
                    if (end==0) Quad(vertices,uv,tris[2],l,r,lb,rb,Vector2.zero,Vector2.right,Vector2.up,Vector2.one);
                    else Quad(vertices,uv,tris[2],r,l,rb,lb,Vector2.zero,Vector2.right,Vector2.up,Vector2.one);
                }
            }
            if (vertices.Count < 3) throw new Exception("没有生成有效顶点。");
            Transform old = spline.transform.Find(outputName);
            GameObject go = old ? old.gameObject : new GameObject(outputName);
            if (!old) { Undo.RegisterCreatedObjectUndo(go,"创建 V3 道路"); go.transform.SetParent(spline.transform, true); }
            go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            go.transform.localScale=Vector3.one;
            Matrix4x4 inverse=go.transform.worldToLocalMatrix;
            for(int i=0;i<vertices.Count;i++) vertices[i]=inverse.MultiplyPoint3x4(vertices[i]);
            Mesh mesh;
            if (saveMeshAsset)
            {
                string folder="Assets/GeneratedRoadMeshes";
                if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets","GeneratedRoadMeshes");
                // GUID based path remains stable across Unity restarts (instance ID does not).
                string guid=GlobalObjectId.GetGlobalObjectIdSlow(spline.gameObject).ToString().Replace(':','_').Replace('-','_');
                string path=folder+"/V3_"+guid+".asset";
                mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(!mesh){mesh=new Mesh {name="V3 破损石砖道路网格"}; AssetDatabase.CreateAsset(mesh,path);}
                Undo.RecordObject(mesh,"更新 V3 道路网格");
            }
            else mesh=new Mesh {name="V3 临时道路网格（未保存）"};
            mesh.Clear(); mesh.indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.subMeshCount=5;
            for(int i=0;i<5;i++)mesh.SetTriangles(tris[i],i);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            if(saveMeshAsset)EditorUtility.SetDirty(mesh);
            var mf=go.GetComponent<MeshFilter>();if(!mf)mf=Undo.AddComponent<MeshFilter>(go);
            var mr=go.GetComponent<MeshRenderer>();if(!mr)mr=Undo.AddComponent<MeshRenderer>(go);
            Undo.RecordObject(mf,"绑定网格");Undo.RecordObject(mr,"绑定材质");mf.sharedMesh=mesh;
            Material fallback=AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            Material top=topMaterial?topMaterial:fallback;
            Material sideMat = sideMaterial ? sideMaterial : top;
            mr.sharedMaterials = new[] { top, curbMaterial ? curbMaterial : top, sideMat, bottomMaterial ? bottomMaterial : sideMat, damageMaterial ? damageMaterial : sideMat };
            MeshCollider mc=go.GetComponent<MeshCollider>();
            if(createCollider){if(!mc)mc=Undo.AddComponent<MeshCollider>(go);Undo.RecordObject(mc,"设置道路碰撞");mc.sharedMesh=null;mc.sharedMesh=mesh;}
            else if(mc)Undo.DestroyObjectImmediate(mc);
            EditorUtility.SetDirty(mf);EditorUtility.SetDirty(mr); EditorSceneManager.MarkSceneDirty(go.scene);
            if(saveMeshAsset)AssetDatabase.SaveAssets();
            Selection.activeGameObject=go;SceneView.FrameLastActiveSceneView();
            Debug.Log($"[石砖道路 V3] 已生成 {segments} 段，{vertices.Count} 顶点，约 {length:F1} 米。",go);
        }
        catch(Exception e){Debug.LogException(e); EditorUtility.DisplayDialog("V3 生成失败",e.Message+"\n请检查 Console 控制台。","确定");}
    }

    private Vector3 Position(float t) => (Vector3)spline.EvaluatePosition(0,Mathf.Clamp01(t));
    private Vector3 OnTerrain(Vector3 p)
    {
        if(conformToTerrain && terrain)
        {
            Vector3 o=terrain.transform.position;
            Vector3 q=p-o;
            if(q.x>=0 && q.z>=0 && q.x<=terrain.terrainData.size.x && q.z<=terrain.terrainData.size.z)
                p.y=terrain.SampleHeight(p)+o.y;
        }
        p.y+=heightOffset;
        return p;
    }
    private float Random01(int cell,int salt)
    {
        unchecked
        {
            uint n=(uint)(cell*374761393 + salt*668265263 + seed*1442695041);
            n=(n^(n>>13))*1274126177u;
            return (n^(n>>16))/4294967295f;
        }
    }
    private float SmoothDamage(float coord,int salt,float chance,float span)
    {
        float x=coord/Mathf.Max(.15f,span);
        int cell=Mathf.FloorToInt(x);
        float f=x-cell;
        float a=Random01(cell,salt)<chance?Mathf.Lerp(.3f,1,Random01(cell,salt+7)):0;
        float b=Random01(cell+1,salt)<chance?Mathf.Lerp(.3f,1,Random01(cell+1,salt+7)):0;
        float sm=f*f*(3-2*f);
        return Mathf.Lerp(a,b,sm);
    }
    private static void Quad(List<Vector3> vertices,List<Vector2> uv,List<int> tris,Vector3 a,Vector3 b,Vector3 c,Vector3 d,
        Vector2 ta,Vector2 tb,Vector2 tc,Vector2 td)
    {
        int idx=vertices.Count;
        vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);
        uv.Add(ta);uv.Add(tb);uv.Add(tc);uv.Add(td);
        tris.Add(idx);tris.Add(idx+1);tris.Add(idx+2);
        tris.Add(idx+2);tris.Add(idx+1);tris.Add(idx+3);
    }
}
#endif
