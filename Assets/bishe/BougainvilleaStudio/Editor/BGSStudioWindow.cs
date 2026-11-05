#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
namespace BougainvilleaStudio
{
    internal static class BGSSelection {public static int branch,point;}
    public class BGSStudioWindow:EditorWindow
    {
        [SerializeField] BGSAuthoring current;
        [SerializeField] int newMode;
        Vector2 scroll;
        [MenuItem("Tools/三角梅工坊/Bougainvillea Studio")]
        public static void Open(){GetWindow<BGSStudioWindow>("三角梅工坊 1.2");}
        public static void OpenFor(BGSAuthoring t){var w=GetWindow<BGSStudioWindow>("三角梅工坊 1.2");w.current=t;Selection.activeGameObject=t.gameObject;}
        void OnGUI()
        {
            scroll=EditorGUILayout.BeginScrollView(scroll);EditorGUILayout.LabelField("三角梅工坊 1.2 · 自然花藤",EditorStyles.boldLabel);
            newMode=EditorGUILayout.Popup("新建模式 / Mode",newMode,new[]{"繁茂花墙 / Flower Wall","花叶枝条 / Flower Branches"});
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if(GUILayout.Button("新建三角梅",GUILayout.Height(30)))
                {
                    var go=new GameObject(newMode==0?"三角梅花墙":"三角梅枝条");Undo.RegisterCreatedObjectUndo(go,"新建三角梅");current=go.AddComponent<BGSAuthoring>();current.mode=(BGSMode)newMode;BGSCore.Randomize(current,false);BGSBuilder.Build(current);Selection.activeGameObject=go;BGSSelection.branch=BGSSelection.point=0;if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.FrameSelected();
                }
                current=(BGSAuthoring)EditorGUILayout.ObjectField("当前对象 / Current",current,typeof(BGSAuthoring),true);
                if(GUILayout.Button("使用当前选中对象"))current=Selection.activeGameObject?Selection.activeGameObject.GetComponentInParent<BGSAuthoring>():null;
                if(current)BGSControls.Draw(current);
            }
            EditorGUILayout.EndScrollView();
        }
    }
    internal static class BGSControls
    {
        static bool more,snapMore,pointMore,files;
        static BGSPreset preset;
        static void Record(BGSAuthoring t,string text){Undo.RecordObject(t,text);EditorUtility.SetDirty(t);}
        public static void Draw(BGSAuthoring t)
        {
            EditorGUI.BeginChangeCheck();int mode=EditorGUILayout.Popup("模式 / Mode",(int)t.mode,new[]{"花墙 / Wall","枝条 / Branches"});
            float w=t.wallWidth,h=t.wallHeight,len=t.branchLength,width=t.branchWidth,spacing=t.branchSpacing,twist=t.twist;int count=t.branchCount;
            if(mode==0){w=EditorGUILayout.DelayedFloatField("墙宽（米） / Width",w);h=EditorGUILayout.DelayedFloatField("墙高（米） / Height",h);}
            else{len=EditorGUILayout.DelayedFloatField("统一长度（米） / Length",len);width=EditorGUILayout.DelayedFloatField("花叶宽度（米） / Width",width);count=EditorGUILayout.IntSlider("枝条数量 / Count",count,1,30);twist=EditorGUILayout.Slider("各自盘曲 / Curl",twist,0,5);}
            float depth=EditorGUILayout.DelayedFloatField("花叶厚度（米） / Depth",t.volumeDepth);
            float side=t.sideBranchLength;if(mode==1)side=EditorGUILayout.DelayedFloatField("侧枝垂长（米） / Side Drop",side);
            float fullness=EditorGUILayout.Slider("花叶密度 / Density",t.fullness,0,1);
            float gaps=EditorGUILayout.Slider("疏密断续 / Gaps",t.gapAmount,0,.8f);
            int seed=EditorGUILayout.IntField("随机种子 / Seed",t.seed);
            if(EditorGUI.EndChangeCheck())
            {
                Record(t,"修改三角梅造型");bool random=mode!=(int)t.mode||count!=t.branchCount||seed!=t.seed||twist!=t.twist;bool changeLength=Mathf.Abs(len-t.branchLength)>.00001f;
                t.mode=(BGSMode)mode;t.seed=seed;t.branchCount=count;t.twist=twist;t.fullness=fullness;t.branchWidth=BGSCore.Finite(width)?Mathf.Max(.01f,width):.65f;
                t.volumeDepth=BGSCore.Finite(depth)?Mathf.Max(.01f,depth):.55f;
                t.sideBranchLength=BGSCore.Finite(side)?Mathf.Max(.01f,side):.75f;t.gapAmount=gaps;
                if(BGSCore.Finite(w)&&BGSCore.Finite(h)&&w>0&&h>0)BGSCore.ResizeWall(t,w,h);
                if(BGSCore.Finite(len)&&len>0)t.branchLength=len;
                if(t.mode==BGSMode.FlowerBranches&&changeLength&&!random)foreach(var branch in t.branches)BGSCore.ScaleLength(t,branch,t.branchLength);
                if(random||t.mode==BGSMode.FlowerWall&&t.wallPoints.Count!=9||t.mode==BGSMode.FlowerBranches&&t.branches.Count==0)BGSCore.Randomize(t,true);
                BGSBuilder.Schedule(t);SceneView.RepaintAll();
            }
            EditorGUILayout.HelpBox(t.mode==BGSMode.FlowerWall?"九个控制点调整花墙范围与起伏。花叶在空间中交错分布，簇间可透空；正面为本地 +Z。":"沿可编辑路线生成错落侧枝和花叶簇。侧枝垂长调整沿路线方向的延伸，宽度和厚度调整横向展开。手调点自动锁定。",MessageType.None);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("换一组随机造型")){Record(t,"重新随机三角梅");t.seed=unchecked(t.seed*1664525+1013904223);BGSCore.Randomize(t,true);BGSBuilder.Build(t);}
                if(GUILayout.Button("生成／刷新"))BGSBuilder.Build(t);
            }
            EditorGUILayout.Space();EditorGUILayout.LabelField("可选表面吸附 / Surface Snap",EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();bool enabled=EditorGUILayout.Toggle("启用吸附 / Enable",t.snapEnabled);var target=(Transform)EditorGUILayout.ObjectField("目标建筑 / Target",t.snapTarget,typeof(Transform),true);float strength=EditorGUILayout.Slider("吸附强度 / Strength",t.snapStrength,0,1);if(enabled&&!target)EditorGUILayout.HelpBox("先拖入墙体或建筑对象，再进行表面吸附。",MessageType.Info);float range=t.snapRange,offset=t.surfaceOffset;
            snapMore=EditorGUILayout.Foldout(snapMore,"吸附范围、间距",true);if(snapMore){range=EditorGUILayout.DelayedFloatField("范围（米） / Range",range);offset=EditorGUILayout.DelayedFloatField("离面间距（米） / Offset",offset);if(GUILayout.Button("重建目标缓存")){BGSBuilder.ClearCaches();BGSBuilder.Schedule(t,true);}}
            if(EditorGUI.EndChangeCheck()){Record(t,"调整表面吸附");t.snapEnabled=enabled;t.snapTarget=target;t.snapStrength=strength;t.snapRange=BGSCore.Finite(range)?Mathf.Max(.001f,range):.5f;t.surfaceOffset=BGSCore.Finite(offset)?Mathf.Max(0,offset):.1f;BGSBuilder.Schedule(t);SceneView.RepaintAll();}
            PointControls(t);
            more=EditorGUILayout.Foldout(more,"贴图、细节与自动刷新",true);
            if(more)
            {
                EditorGUI.BeginChangeCheck();var atlas=(Texture2D)EditorGUILayout.ObjectField("花叶图集 / Atlas",t.atlas,typeof(Texture2D),false);var tint=EditorGUILayout.ColorField("整体颜色 / Tint",t.tint);float pattern=EditorGUILayout.DelayedFloatField("花叶图案尺寸（米） / Pattern",t.textureMetres);bool lod=EditorGUILayout.Toggle("远近简化 / LOD",t.generateLOD),auto=EditorGUILayout.Toggle("自动刷新 / Auto",t.autoRefresh);
                if(t.mode==BGSMode.FlowerBranches)spacing=EditorGUILayout.DelayedFloatField("起点间距（米） / Spacing",t.branchSpacing);
                bool stems=t.showStems;if(t.mode==BGSMode.FlowerBranches)stems=EditorGUILayout.Toggle("显示细枝 / Show Stems",stems);
                float detail=EditorGUILayout.Slider("近景小花细节 / Detail",t.detailDensity,0,2);bool lower=EditorGUILayout.Toggle("小花细节集中下段",t.lowerDetailOnly);float fraction=t.lowerDetailFraction;if(lower)fraction=EditorGUILayout.Slider("下段比例 / Lower Part",fraction,.1f,1);
                EditorGUILayout.HelpBox("花叶密度控制整个花藤，设为 0 会移除花叶。疏密断续越大，稀疏段越明显。近景小花细节只添加折叠花叶；基础花叶簇始终覆盖全长。自带图集下半部须保留透明花簇。",MessageType.None);
                if(EditorGUI.EndChangeCheck()){Record(t,"三角梅显示设置");t.atlas=atlas;t.tint=tint;t.textureMetres=BGSCore.Finite(pattern)?Mathf.Max(.05f,pattern):.65f;t.generateLOD=lod;t.autoRefresh=auto;t.showStems=stems;t.detailDensity=detail;t.lowerDetailOnly=lower;t.lowerDetailFraction=fraction;if(spacing!=t.branchSpacing&&BGSCore.Finite(spacing)){t.branchSpacing=Mathf.Max(0,spacing);BGSCore.Randomize(t,true);}BGSBuilder.Schedule(t);}
            }
            if(!string.IsNullOrEmpty(t.status))EditorGUILayout.HelpBox(t.status,MessageType.None);
            if(GUILayout.Button("烘焙游戏用 Prefab",GUILayout.Height(28)))BGSBuilder.Bake(t);
            files=EditorGUILayout.Foldout(files,"保存、载入参数预设",true);if(files){if(GUILayout.Button("保存当前参数与控制点"))BGSBuilder.SavePreset(t);preset=(BGSPreset)EditorGUILayout.ObjectField("预设 / Preset",preset,typeof(BGSPreset),false);using(new EditorGUI.DisabledScope(!preset))if(GUILayout.Button("载入预设"))BGSBuilder.LoadPreset(t,preset);}
        }
        public static BGSPoint Selected(BGSAuthoring t)
        {
            if(t.mode==BGSMode.FlowerWall){if(t.wallPoints.Count!=9)return null;BGSSelection.point=Mathf.Clamp(BGSSelection.point,0,8);return t.wallPoints[BGSSelection.point];}
            if(t.branches.Count==0)return null;BGSSelection.branch=Mathf.Clamp(BGSSelection.branch,0,t.branches.Count-1);var points=t.branches[BGSSelection.branch].points;if(points.Count==0)return null;BGSSelection.point=Mathf.Clamp(BGSSelection.point,0,points.Count-1);return points[BGSSelection.point];
        }
        static void PointControls(BGSAuthoring t)
        {
            EditorGUILayout.Space();EditorGUILayout.LabelField("手动控制点 / Control Points",EditorStyles.boldLabel);
            if(t.mode==BGSMode.FlowerBranches&&t.branches.Count>0)
            {
                string[] names=new string[t.branches.Count];for(int i=0;i<names.Length;i++)names[i]=t.branches[i].name;int selected=EditorGUILayout.Popup("编辑枝条 / Branch",Mathf.Clamp(BGSSelection.branch,0,names.Length-1),names);if(selected!=BGSSelection.branch){BGSSelection.branch=selected;BGSSelection.point=0;SceneView.RepaintAll();}
                if(t.snapEnabled&&BGSSelection.branch<t.snappedLengths.Count)EditorGUILayout.LabelField("贴合后长度（米）",t.snappedLengths[BGSSelection.branch].ToString("F2"));
                var branch=t.branches[BGSSelection.branch];float actual=BGSCore.Length(t,BGSCore.Smooth(branch.points));EditorGUI.BeginChangeCheck();float length=EditorGUILayout.DelayedFloatField("此枝条长度（米）",actual);if(EditorGUI.EndChangeCheck()&&BGSCore.Finite(length)&&length>0){Record(t,"缩放单根枝条长度");BGSCore.ScaleLength(t,branch,length);BGSBuilder.Schedule(t);SceneView.RepaintAll();}
            }
            var p=Selected(t);if(p==null)return;
            int max=t.mode==BGSMode.FlowerWall?8:t.branches[BGSSelection.branch].points.Count-1;BGSSelection.point=EditorGUILayout.IntSlider("选中控制点 / Point",BGSSelection.point,0,max);p=Selected(t);
            EditorGUI.BeginChangeCheck();Vector3 position=EditorGUILayout.Vector3Field("位置 / Position",p.position);bool locked=EditorGUILayout.Toggle("随机时锁定 / Lock",p.locked);float weight=EditorGUILayout.Slider("此点吸附权重 / Weight",p.snapWeight,0,1);
            if(EditorGUI.EndChangeCheck()&&BGSCore.Finite(position)){Record(t,"修改三角梅控制点");bool moved=(p.position-position).sqrMagnitude>1e-12f;p.position=position;p.locked=moved||locked;p.snapWeight=weight;BGSBuilder.Schedule(t);SceneView.RepaintAll();}
            if(t.mode==BGSMode.FlowerBranches)
            {
                var points=t.branches[BGSSelection.branch].points;
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("在线段后加点")){Record(t,"添加曲线控制点");int i=BGSSelection.point;Vector3 q=i<points.Count-1?(points[i].position+points[i+1].position)*.5f:points[i].position+(points[i].position-points[i-1].position)*.5f;points.Insert(i+1,new BGSPoint(q){locked=true});BGSSelection.point=i+1;BGSBuilder.Schedule(t);SceneView.RepaintAll();}
                    using(new EditorGUI.DisabledScope(points.Count<=2))if(GUILayout.Button("删除选中点")){Record(t,"删除曲线控制点");points.RemoveAt(BGSSelection.point);BGSSelection.point=Mathf.Max(0,BGSSelection.point-1);BGSBuilder.Schedule(t);SceneView.RepaintAll();}
                }
            }
            pointMore=EditorGUILayout.Foldout(pointMore,"控制点说明与解锁",true);if(pointMore){if(GUILayout.Button("解除全部点的随机锁定")){Record(t,"解除控制点锁定");foreach(var item in t.wallPoints)item.locked=false;foreach(var branch in t.branches)foreach(var item in branch.points)item.locked=false;SceneView.RepaintAll();}EditorGUILayout.HelpBox("在 Scene 中点击圆点选中，再拖动坐标轴。此点权重设为 0 可保留悬垂段；吸附不会改写原始点。锁定仅防止重新随机覆盖，明确修改长度仍会整体缩放。Ctrl+Z 撤销。",MessageType.None);}
        }
    }
}
#endif
