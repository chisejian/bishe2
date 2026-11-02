#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class StylizedTreeStudioWindow : EditorWindow
{
    [SerializeField] StylizedTreeAuthoring tree;
    [SerializeField] STSPlantSpecies chosen=STSPlantSpecies.CrepeMyrtle;
    Vector2 scroll;
    bool extra,files;
    STSPlantPreset recipe;
    double rebuildAt=-1;
    string status;
    [MenuItem("Tools/风格化植物工坊/Stylized Tree Studio")]
    public static void Open(){GetWindow<StylizedTreeStudioWindow>("面片植物工坊 V0.5.5");}
    public static void OpenFor(StylizedTreeAuthoring t){var w=GetWindow<StylizedTreeStudioWindow>("面片植物工坊 V0.5.5");w.tree=t;w.chosen=t.species;}
    void OnEnable(){EditorApplication.update+=Tick;}
    void OnDisable(){EditorApplication.update-=Tick;}
    void Tick(){if(rebuildAt<0||EditorApplication.timeSinceStartup<rebuildAt)return;rebuildAt=-1;if(tree)Rebuild();}
    void Schedule(){if(tree.livePreview)rebuildAt=EditorApplication.timeSinceStartup+.4;}
    void Record(string s){Undo.RecordObject(tree,s);EditorUtility.SetDirty(tree);}
    void Rebuild(){rebuildAt=-1;status=STSPlantBuilder.Build(tree)?"已刷新：花簇按叶簇 Alpha 定位在树冠外层。":"生成失败，请查看 Console；上次生成模型保留。";Repaint();}
    static void Field(SerializedObject so,string field,string label){var p=so.FindProperty(field);if(p!=null)EditorGUILayout.PropertyField(p,new GUIContent(label),true);}
    static void Map(SerializedObject so,string field,string label)
    {
        EditorGUILayout.LabelField(label,EditorStyles.boldLabel);
        Field(so,field+".normal","法线（可选） / Normal");Field(so,field+".uv","UV 区域 / UV Rect");
        Field(so,field+".tint","叠加颜色 / Tint");Field(so,field+".cutoff","透明裁切 / Alpha Cutoff");
    }
    void Create()
    {
        var go=new GameObject(STSPresets.Names[(int)chosen].Split('/')[0].Trim());Undo.RegisterCreatedObjectUndo(go,"新建面片植物");
        tree=go.AddComponent<StylizedTreeAuthoring>();STSPresets.Apply(tree,chosen);Selection.activeGameObject=go;Rebuild();
        if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.FrameSelected();
    }
    void OnGUI()
    {
        scroll=EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("面片植物工坊 V0.5.5",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("叶簇拖入一张 RGBA PNG，即自动创建团簇受光材质。花簇继续贴在树冠外层。",MessageType.Info);
        tree=(StylizedTreeAuthoring)EditorGUILayout.ObjectField("当前植物 / Plant",tree,typeof(StylizedTreeAuthoring),true);
        chosen=(STSPlantSpecies)EditorGUILayout.Popup("物种 / Species",(int)chosen,STSPresets.Names);
        using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("新建植物",GUILayout.Height(28)))Create();
                if(GUILayout.Button("使用当前选中植物")){tree=Selection.activeGameObject?Selection.activeGameObject.GetComponentInParent<StylizedTreeAuthoring>():null;if(tree)chosen=tree.species;}
            }
            if(tree)PlantGUI();
        }
        EditorGUILayout.EndScrollView();
    }
    void PlantGUI()
    {
        if(tree.dataVersion<5)EditorGUILayout.HelpBox("旧植物请先应用 V0.5 默认造型。旧数据保留在组件中，原有手工花叶不参与本版生成。",MessageType.Info);
        if(GUILayout.Button(tree.dataVersion<5?"升级为 V0.5 面片造型（可撤销）":"应用所选物种默认造型（可撤销）"))
        {Record("应用面片植物预设");STSPresets.Apply(tree,chosen);Rebuild();}
        EditorGUILayout.Space();EditorGUILayout.LabelField("树形和密度",EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        float height=EditorGUILayout.FloatField("高度（米） / Height",tree.height),width=EditorGUILayout.FloatField("冠幅（米） / Width",tree.crownWidth);
        float branches=EditorGUILayout.Slider("枝干密度 / Branches",tree.branchFullness,0,1),leaves=EditorGUILayout.Slider("叶簇密度（含顶部） / Leaves",tree.canopyFullness,0,1);
        float blooms=tree.flowerAmount,scale=tree.bloomScale;
        if(tree.growthForm!=STSGrowthForm.Palm&&tree.growthForm!=STSGrowthForm.ClimbingFern)
        {blooms=EditorGUILayout.Slider("外层花量 / Flowers",tree.flowerAmount,0,1);scale=EditorGUILayout.Slider("花簇大小 / Bloom Scale",tree.bloomScale,.2f,3);}
        int seed=EditorGUILayout.IntField("随机种子 / Seed",tree.seed);
        if(EditorGUI.EndChangeCheck())
        {
            Record("调整植物外观");float before=tree.height;
            tree.height=Mathf.Max(tree.growthForm==STSGrowthForm.Tree||tree.growthForm==STSGrowthForm.Palm?20:.2f,height);
            if(Mathf.Abs(tree.height-before)>.001f&&Mathf.Abs(width-tree.crownWidth)<.001f)width*=tree.height/Mathf.Max(.1f,before);
            tree.crownWidth=Mathf.Max(.2f,width);tree.branchFullness=branches;tree.canopyFullness=leaves;tree.flowerAmount=blooms;tree.bloomScale=scale;
            if(seed!=tree.seed){tree.leafSeed=STSRandom.Hash(seed,153);tree.flowerSeed=STSRandom.Hash(seed,731);tree.fruitSeed=STSRandom.Hash(seed,991);}tree.seed=seed;
            STSQuickRecipe.Apply(tree);tree.lockBranches=false;Schedule();
        }
        EditorGUI.BeginChangeCheck();
        float twist=tree.trunkTwist;
        using(new EditorGUI.DisabledScope(tree.growthForm==STSGrowthForm.Vine||tree.growthForm==STSGrowthForm.ClimbingFern))
            twist=EditorGUILayout.Slider("树干扭曲度 / Trunk Twist",tree.trunkTwist,0,1);
        float leafScale=EditorGUILayout.Slider("叶片大小（倍） / Leaf Scale",tree.leafScale,.2f,3);
        if(EditorGUI.EndChangeCheck())
        {
            Record("调整树干扭曲度和叶片大小");
            if(twist!=tree.trunkTwist)tree.lockBranches=false;
            tree.trunkTwist=twist;tree.leafScale=leafScale;Schedule();
        }
        EditorGUILayout.HelpBox("扭曲度 0 保留原树形，调大增加主干弯扭。叶片大小 1 为原尺寸，整张叶簇 PNG（含顶部）一起缩放。藤本仍沿原攀援路径生成。",MessageType.None);
        STSMaterials.Defaults(tree);var so=new SerializedObject(tree);so.Update();EditorGUI.BeginChangeCheck();
        EditorGUILayout.Space();EditorGUILayout.LabelField("把自己画的 PNG 拖到这里",EditorStyles.boldLabel);
        Field(so,"leafCards.color",tree.growthForm==STSGrowthForm.Palm?"整张羽叶 PNG（必需）":"叶簇 PNG（自动创建材质）");
        if(tree.growthForm!=STSGrowthForm.Palm&&tree.growthForm!=STSGrowthForm.ClimbingFern)Field(so,"bloomCards.color","花簇 PNG");
        Field(so,"twigCards.color","细枝 PNG（可选）");Field(so,"barkColor","树皮 RGB（可选）");
        extra=EditorGUILayout.Foldout(extra,"更多贴图设置与风摆",true);
        if(extra)
        {
            EditorGUILayout.HelpBox("UV 默认使用整图。图集可填写局部 UV；原点在左下。花的位置按叶簇和花簇 PNG 的 Alpha 计算。",MessageType.None);
            Map(so,"leafCards","叶簇 / Leaves");Map(so,"bloomCards","花簇 / Blooms");Map(so,"twigCards","细枝 / Twigs");
            Field(so,"leafClusterSoftness","团簇明暗 / Cluster Shading");Field(so,"leafSmoothness","柔和反光 / Smoothness");
            if(tree.growthForm==STSGrowthForm.Tree)Field(so,"topBloomDensity","顶部花量 / Top Flowers");
            Field(so,"barkNormal","树皮法线（可选）");Field(so,"barkTint","无贴图时的树皮颜色");
            Field(so,"enableFruit","显示果实面片");Field(so,"fruitCards.color","果实 PNG（可选）");Map(so,"fruitCards","果实 / Fruit");
            Field(so,"fruitCount","果实数量");Field(so,"fruitSize","果实直径（米）");Field(so,"fruitStalk","果实下垂（米）");
            Field(so,"bloomSurfaceOffset","花簇离叶面（米）");Field(so,"windStrength","风摆幅度");
            Field(so,"showTestTextures","空槽使用测试贴图");Field(so,"livePreview","自动刷新");Field(so,"generateLOD","生成三级 LOD");Field(so,"addTrunkCollider","主干碰撞体");
        }
        bool changed=EditorGUI.EndChangeCheck();if(so.ApplyModifiedProperties()||changed)Schedule();
        if(extra)
        {
            if(GUILayout.Button("定位自动创建的叶片材质球")){STSMaterials.Ensure(tree);EditorGUIUtility.PingObject(tree.leafMaterial);}
            if(GUILayout.Button("自绘贴图：使用整图 UV 和原图颜色"))
            {Record("恢复整图 UV");foreach(var map in new[]{tree.leafCards,tree.twigCards,tree.bloomCards,tree.fruitCards}){map.uv=new Rect(0,0,1,1);map.tint=Color.white;}Rebuild();}
            if(GUILayout.Button("设置所用贴图的透明、Mip 与法线导入参数")){STSMaterials.ConfigureTextures(tree);Rebuild();}
            if(GUILayout.Button("使用参考包中的叶簇图集（须已导入原包）"))
            {Record("使用参考叶簇图集");if(STSMaterials.ReferenceAtlas(tree))Rebuild();else status="未找到 KapokierLeaves_a.png，请先导入参考包，或直接拖入自己的 PNG。";}
        }
        using(new EditorGUILayout.HorizontalScope())
        {
            if(GUILayout.Button("生成／刷新",GUILayout.Height(34)))Rebuild();
            if(GUILayout.Button("换一棵",GUILayout.Height(34)))
            {Record("随机植物");tree.seed=STSRandom.Hash(tree.seed,17);tree.leafSeed=STSRandom.Hash(tree.leafSeed,23);tree.flowerSeed=STSRandom.Hash(tree.flowerSeed,31);tree.lockBranches=false;STSSkeleton.Generate(tree);Rebuild();}
        }
        Transform root=tree.transform.Find(STSPlantBuilder.RootName);var output=root?root.GetComponent<STSGeneratedOutput>():null;
        if(output)
        {
            EditorGUILayout.LabelField("LOD0：叶簇 "+output.leafCardCount+"；花簇 "+output.flowerCardCount+"；三角形 "+output.highDetailTriangles.ToString("N0"));
            EditorGUILayout.LabelField("模型高度 "+(output.localModelHeight*Mathf.Abs(tree.transform.lossyScale.y)).ToString("F1")+" 米");
            if(tree.enableFlowers&&output.flowerCardCount==0)EditorGUILayout.HelpBox("没有可显示的花簇。检查花量和 PNG 的 Alpha；背景须透明，图案不能全透明。",MessageType.Warning);
        }
        if(!string.IsNullOrEmpty(status))EditorGUILayout.HelpBox(status,MessageType.None);
        if(GUILayout.Button("烘焙游戏用 Prefab",GUILayout.Height(28)))STSPlantFiles.Bake(tree,false);
        files=EditorGUILayout.Foldout(files,"保存和载入外观",true);
        if(files)
        {
            if(GUILayout.Button("保存当前参数和贴图为预设"))STSPlantFiles.SavePreset(tree);
            recipe=(STSPlantPreset)EditorGUILayout.ObjectField("预设 / Preset",recipe,typeof(STSPlantPreset),false);
            using(new EditorGUI.DisabledScope(!recipe))if(GUILayout.Button("载入预设")){STSPlantFiles.LoadPreset(tree,recipe);chosen=tree.species;}
            if(GUILayout.Button("保存可编辑 Prefab"))STSPlantFiles.Bake(tree,true);
        }
    }
}

[CustomEditor(typeof(StylizedTreeAuthoring))]
internal class STSAuthoringInspector : Editor
{
    public override void OnInspectorGUI()
    {
        var t=(StylizedTreeAuthoring)target;EditorGUILayout.LabelField("面片植物工坊 V0.5.5",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("在工坊窗口拖入叶簇和花簇 PNG，再调整密度。",MessageType.Info);
        if(GUILayout.Button("打开植物工坊"))StylizedTreeStudioWindow.OpenFor(t);
        if(GUILayout.Button("刷新当前植物"))STSPlantBuilder.Build(t);
    }
}
#endif
