#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class StylizedTreeStudioWindow : EditorWindow
{
    [SerializeField] StylizedTreeAuthoring tree;
    [SerializeField] STSPlantSpecies chosen=STSPlantSpecies.CrepeMyrtle;
    [SerializeField] int batchCount=10;
    [SerializeField] float batchSpacing;
    [SerializeField] float batchVariation=.35f;
    [SerializeField] GameObject referencePrefab;
    [SerializeField] int referenceBarkSlot;
    Vector2 scroll;
    bool extra,files;
    STSPlantPreset recipe;
    double rebuildAt=-1;
    string status;
    [MenuItem("Tools/风格化植物工坊/Stylized Tree Studio")]
    public static void Open(){GetWindow<StylizedTreeStudioWindow>("面片植物工坊 V0.5.15");}
    public static void OpenFor(StylizedTreeAuthoring t){var w=GetWindow<StylizedTreeStudioWindow>("面片植物工坊 V0.5.15");w.tree=t;w.chosen=STSPresets.IsSupported(t.species)?t.species:STSPlantSpecies.CrepeMyrtle;}
    void OnEnable(){EditorApplication.update+=Tick;if(!referencePrefab)referencePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(STSReferenceCapture.OakPrefabGuid));}
    void OnDisable(){EditorApplication.update-=Tick;}
    void Tick(){if(rebuildAt<0||EditorApplication.timeSinceStartup<rebuildAt)return;rebuildAt=-1;if(tree)Rebuild();}
    void Schedule(){if(tree.livePreview)rebuildAt=EditorApplication.timeSinceStartup+.4;else{status="参数已保存。自动刷新关闭时，请点击“生成／刷新”应用到模型。";Repaint();}}
    void OnSelectionChange()
    {
        var selected=Selection.activeGameObject?Selection.activeGameObject.GetComponentInParent<StylizedTreeAuthoring>():null;
        if(selected&&!EditorUtility.IsPersistent(selected))
        {tree=selected;if(STSPresets.IsSupported(selected.species))chosen=selected.species;Repaint();}
        else if(Selection.activeGameObject&&!EditorUtility.IsPersistent(Selection.activeGameObject)&&!Selection.activeGameObject.GetComponentInChildren<StylizedTreeAuthoring>(true)&&Selection.activeGameObject.GetComponentInChildren<Renderer>(true))
        {tree=null;status="当前选中的是静态模型，没有工坊可编辑参数。请选择带 StylizedTreeAuthoring 组件的植物根节点，再调叶片；游戏用 Prefab 需从源植物重新烘焙。";Repaint();}
    }
    void Record(string s){Undo.RecordObject(tree,s);EditorUtility.SetDirty(tree);}
    void Rebuild(){rebuildAt=-1;status=STSPlantBuilder.Build(tree)?"已刷新当前植物。":"生成失败，请查看 Console；上次生成模型保留。";Repaint();}
    static void Field(SerializedObject so,string field,string label){var p=so.FindProperty(field);if(p!=null)EditorGUILayout.PropertyField(p,new GUIContent(label),true);}
    static void Map(SerializedObject so,string field,string label)
    {
        EditorGUILayout.LabelField(label,EditorStyles.boldLabel);
        Field(so,field+".normal","法线（可选） / Normal");Field(so,field+".uv","UV 区域 / UV Rect");
        Field(so,field+".tint","叠加颜色 / Tint");Field(so,field+".cutoff","透明裁切 / Alpha Cutoff");
    }
    void Create()
    {
        var go=new GameObject(STSPresets.DisplayName(chosen).Split('/')[0].Trim());Undo.RegisterCreatedObjectUndo(go,"新建面片植物");
        tree=go.AddComponent<StylizedTreeAuthoring>();STSPresets.Apply(tree,chosen);STSDefaultTextures.Apply(tree);batchSpacing=0;Selection.activeGameObject=go;Rebuild();
        if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.FrameSelected();
    }
    void ReferenceSourceGUI()
    {
        EditorGUILayout.Space();EditorGUILayout.LabelField("现成主干 → 新植物副本",EditorStyles.boldLabel);
        referencePrefab=(GameObject)EditorGUILayout.ObjectField("参考预制体 / Source Prefab",referencePrefab,typeof(GameObject),false);
        referenceBarkSlot=Mathf.Max(0,EditorGUILayout.IntField("枝干材质槽（从 0 开始）",referenceBarkSlot));
        EditorGUILayout.HelpBox("你提供的 OakTree04 使用第 0 槽。创建时提取枝干副本，原树叶不带入；叶片、花和贴图沿用当前植物设置。原预制体及模型保持原样。",MessageType.None);
        using(new EditorGUI.DisabledScope(!referencePrefab))
            if(GUILayout.Button("从参考主干创建独立新植物",GUILayout.Height(30)))CreateReferenced();
    }
    void CreateReferenced()
    {
        StylizedTreeAuthoring template=tree;GameObject created=null;STSReferenceTrunk reference=null;
        try
        {
            if(UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new System.InvalidOperationException("请先退出预制体编辑模式，回到主场景，再创建独立新植物。");
            bool copyTemplate=template&&template.growthForm==STSGrowthForm.Tree;
            if(!copyTemplate&&chosen!=STSPlantSpecies.Generic&&chosen!=STSPlantSpecies.CrepeMyrtle&&chosen!=STSPlantSpecies.WhiteChampaca&&chosen!=STSPlantSpecies.Pomelo&&chosen!=STSPlantSpecies.Plumeria)
                throw new System.InvalidOperationException("请先选择通用乔木、大花紫薇、白兰、柚子或鸡蛋花；王棕、灌木和藤本继续使用原生成模式。");
            reference=STSReferenceCapture.Capture(referencePrefab,referenceBarkSlot);
            created=new GameObject((copyTemplate?template.gameObject.name:STSPresets.DisplayName(chosen).Split('/')[0].Trim())+"_主干副本");
            var plant=created.AddComponent<StylizedTreeAuthoring>();
            if(copyTemplate)
            {
                STSPlantBatch.CopySource(template,plant);
                created.transform.position=template.transform.position+template.transform.right*Mathf.Max(1,template.crownWidth*1.2f);
                created.transform.rotation=template.transform.rotation;created.transform.localScale=template.transform.lossyScale;
            }
            else{STSPresets.Apply(plant,chosen);STSDefaultTextures.Apply(plant);}
            plant.useReferenceTrunk=true;plant.referenceTrunk=reference;plant.referenceBend=0;plant.referenceBendDirection=0;plant.referenceAddedBranches=0;plant.trunkTwist=0;
            STSReferenceGrowth.Migrate(plant);
            plant.materialSetId=System.Guid.NewGuid().ToString("N");plant.lockBranches=false;
            plant.branches.Clear();plant.attachments.Clear();plant.paintedFlowers.Clear();plant.projectionTarget=null;plant.growthSignature=null;
            plant.crownWidth=reference.sourceWidth*plant.height/reference.sourceHeight;
            if(!STSPlantBuilder.Build(plant))throw new System.InvalidOperationException("新植物生成失败，请查看 Console 中的第一条错误。");
            Undo.RegisterCreatedObjectUndo(created,"创建现成主干植物副本");
            tree=plant;chosen=plant.species;rebuildAt=-1;Selection.activeGameObject=created;
            status="已创建独立新植物：原预制体未改动。扭曲为 0 保留参考枝干原形；叶片沿用工坊设置；鸡蛋花使用下移的自然圆台树冠。";
            created=null;reference=null;
            if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.FrameSelected();
        }
        catch(System.Exception ex)
        {
            if(created)STSPlantBuilder.Release(created);
            if(reference)
            {
                string path=AssetDatabase.GetAssetPath(reference);
                if(path.StartsWith(STSReferenceCapture.CopiesFolder+"/",System.StringComparison.Ordinal))AssetDatabase.DeleteAsset(System.IO.Path.GetDirectoryName(path).Replace('\\','/'));
            }
            status="创建主干副本失败："+ex.Message;Debug.LogError(status);
        }
    }
    void ReferenceControlsGUI()
    {
        if(!tree.referenceTrunk&&!tree.useReferenceTrunk)return;
        EditorGUI.BeginChangeCheck();
        bool enabled=EditorGUILayout.Toggle("使用现成主干副本",tree.useReferenceTrunk);
        float twist=tree.trunkTwist,stretch=tree.referenceBranchStretch,offset=tree.referenceCrownOffset,baseRatio=tree.referenceCrownBaseRatio;
        if(enabled)
        {
            twist=EditorGUILayout.Slider("枝干扭曲度 / Trunk Twist",twist,0,StylizedTreeAuthoring.MaxTrunkTwist);
            stretch=EditorGUILayout.Slider("枝条拉伸（倍） / Branch Stretch",stretch,.5f,2);
            if(tree.species==STSPlantSpecies.Plumeria)
            {
                float h=Mathf.Max(.1f,tree.height);
                offset=EditorGUILayout.Slider("树冠上下（米） / Crown Offset",offset*h,-.3f*h,.1f*h)/h;
                baseRatio=EditorGUILayout.Slider("圆台底宽比例 / Crown Base",baseRatio,.35f,.9f);
                EditorGUILayout.HelpBox("鸡蛋花使用下宽上更宽的自然圆台树冠，默认下移。上下偏移的负数向下；底宽比例越大，底部越宽。裸露枝条上只补零星叶花。",MessageType.None);
            }
            EditorGUILayout.HelpBox("枝条拉伸：1 为原长度，小于 1 收短，大于 1 向外伸长已有上部枝条；根部与下段主干固定，叶花随附着点移动。",MessageType.None);
        }
        if(EditorGUI.EndChangeCheck())
        {
            Record("调整枝条和树冠");tree.useReferenceTrunk=enabled;
            tree.trunkTwist=twist;tree.referenceBranchStretch=stretch;tree.referenceCrownOffset=offset;tree.referenceCrownBaseRatio=baseRatio;tree.lockBranches=false;Schedule();
        }
    }
    void OnGUI()
    {
        scroll=EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("面片植物工坊 V0.5.15",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("叶簇拖入一张 RGBA PNG，即自动创建团簇受光材质。花簇继续贴在树冠外层。",MessageType.Info);
        tree=(StylizedTreeAuthoring)EditorGUILayout.ObjectField("当前植物 / Plant",tree,typeof(StylizedTreeAuthoring),true);
        if(!STSPresets.IsSupported(chosen))chosen=STSPlantSpecies.CrepeMyrtle;
        chosen=(STSPlantSpecies)EditorGUILayout.Popup("物种 / Species",(int)chosen,STSPresets.Names);
        using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("新建植物",GUILayout.Height(28)))Create();
                if(GUILayout.Button("使用当前选中植物")){tree=Selection.activeGameObject?Selection.activeGameObject.GetComponentInParent<StylizedTreeAuthoring>():null;if(tree&&STSPresets.IsSupported(tree.species))chosen=tree.species;}
            }
            ReferenceSourceGUI();
            if(tree)PlantGUI();
            else if(!string.IsNullOrEmpty(status))EditorGUILayout.HelpBox(status,MessageType.None);
        }
        EditorGUILayout.EndScrollView();
    }
    void PlantGUI()
    {
        if(EditorUtility.IsPersistent(tree))
        {EditorGUILayout.HelpBox("当前选中的是预制体资源。可从上方创建独立新植物；编辑已保存的工坊植物时，请先把它拖入场景。",MessageType.Info);return;}
        STSReferenceGrowth.Migrate(tree);
        EditorGUI.BeginChangeCheck();bool preview=EditorGUILayout.Toggle("调整后自动刷新 / Auto Refresh",tree.livePreview);
        if(EditorGUI.EndChangeCheck()){Record("调整自动刷新");tree.livePreview=preview;if(preview)Schedule();else rebuildAt=-1;}
        if(!STSPresets.IsSupported(tree.species))
        {EditorGUILayout.HelpBox("当前是已移除物种的旧对象，已有模型保留。请新建列表中的其他植物。",MessageType.Info);return;}
        if(tree.dataVersion<5)EditorGUILayout.HelpBox("旧植物请先应用 V0.5 默认造型。旧数据保留在组件中，原有手工花叶不参与本版生成。",MessageType.Info);
        if(GUILayout.Button(tree.dataVersion<5?"升级为 V0.5 面片造型（可撤销）":"应用所选物种默认造型（可撤销）"))
        {Record("应用面片植物预设");STSPresets.Apply(tree,chosen);Rebuild();}
        ReferenceControlsGUI();
        EditorGUILayout.Space();EditorGUILayout.LabelField("树形和密度",EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        float height=EditorGUILayout.FloatField("高度（米） / Height",tree.height),width=EditorGUILayout.FloatField("冠幅（米） / Width",tree.crownWidth);
        float branches=tree.branchFullness;
        using(new EditorGUI.DisabledScope(tree.useReferenceTrunk))branches=EditorGUILayout.Slider("枝干密度 / Branches",tree.branchFullness,0,1);
        float leaves=EditorGUILayout.Slider("叶簇密度（含顶部） / Leaves",tree.canopyFullness,0,1);
        float leafScale=EditorGUILayout.Slider("叶片大小（倍） / Leaf Scale",tree.leafScale,.2f,3);
        float blooms=tree.flowerAmount,scale=tree.bloomScale;
        if(tree.growthForm!=STSGrowthForm.Palm&&tree.growthForm!=STSGrowthForm.ClimbingFern)
        {blooms=EditorGUILayout.Slider("外层花量 / Flowers",tree.flowerAmount,0,1);scale=EditorGUILayout.Slider("花簇大小 / Bloom Scale",tree.bloomScale,.2f,3);}
        int seed=EditorGUILayout.IntField("随机种子 / Seed",tree.seed);
        if(EditorGUI.EndChangeCheck())
        {
            Record("调整植物外观");float before=tree.height;
            tree.height=Mathf.Max(tree.growthForm==STSGrowthForm.Tree||tree.growthForm==STSGrowthForm.Palm?20:.2f,height);
            if(Mathf.Abs(tree.height-before)>.001f&&Mathf.Abs(width-tree.crownWidth)<.001f)width*=tree.height/Mathf.Max(.1f,before);
            tree.crownWidth=Mathf.Max(.2f,width);tree.branchFullness=branches;tree.canopyFullness=leaves;tree.leafScale=leafScale;tree.flowerAmount=blooms;tree.bloomScale=scale;
            if(seed!=tree.seed){tree.leafSeed=STSRandom.Hash(seed,153);tree.flowerSeed=STSRandom.Hash(seed,731);tree.fruitSeed=STSRandom.Hash(seed,991);}tree.seed=seed;
            STSQuickRecipe.Apply(tree);tree.lockBranches=false;Schedule();
        }
        EditorGUI.BeginChangeCheck();
        float twist=tree.trunkTwist;
        if(!tree.useReferenceTrunk)using(new EditorGUI.DisabledScope(tree.growthForm==STSGrowthForm.Vine||tree.growthForm==STSGrowthForm.ClimbingFern))
            twist=EditorGUILayout.Slider("树干扭曲度 / Trunk Twist",tree.trunkTwist,0,StylizedTreeAuthoring.MaxTrunkTwist);
        if(EditorGUI.EndChangeCheck())
        {
            Record("调整树干扭曲度和叶片大小");
            if(twist!=tree.trunkTwist)tree.lockBranches=false;
            tree.trunkTwist=twist;Schedule();
        }
        EditorGUILayout.HelpBox("扭曲度现可调到 3，超过 1 可得到更明显的弯扭。叶片大小 1 为原尺寸，顶部一起缩放。藤本仍沿原攀援路径生成。",MessageType.None);
        STSMaterials.Defaults(tree);var so=new SerializedObject(tree);so.Update();EditorGUI.BeginChangeCheck();
        EditorGUILayout.Space();EditorGUILayout.LabelField("把自己画的 PNG 拖到这里",EditorStyles.boldLabel);
        Field(so,"leafCards.color",tree.growthForm==STSGrowthForm.Palm?"整张羽叶 PNG（必需）":"叶簇 PNG（自动创建材质）");
        if(tree.growthForm!=STSGrowthForm.Palm&&tree.growthForm!=STSGrowthForm.ClimbingFern)Field(so,"bloomCards.color","花簇 PNG");
        Field(so,"twigCards.color","细枝 PNG（可选）");
        if(!tree.useReferenceTrunk)Field(so,"barkColor","树皮 RGB（可选）");
        extra=EditorGUILayout.Foldout(extra,"更多贴图设置与风摆",true);
        if(extra)
        {
            EditorGUILayout.HelpBox("UV 默认使用整图。图集可填写局部 UV；原点在左下。花的位置按叶簇和花簇 PNG 的 Alpha 计算。",MessageType.None);
            Map(so,"leafCards","叶簇 / Leaves");Map(so,"bloomCards","花簇 / Blooms");Map(so,"twigCards","细枝 / Twigs");
            Field(so,"leafClusterSoftness","团簇明暗 / Cluster Shading");Field(so,"leafSmoothness","柔和反光 / Smoothness");
            if(tree.growthForm==STSGrowthForm.Tree)Field(so,"topBloomDensity","顶部花量 / Top Flowers");
            if(!tree.useReferenceTrunk){Field(so,"barkNormal","树皮法线（可选）");Field(so,"barkTint","无贴图时的树皮颜色");}
            Field(so,"enableFruit","显示果实面片");Field(so,"fruitCards.color","果实 PNG（可选）");Map(so,"fruitCards","果实 / Fruit");
            Field(so,"fruitCount","果实数量");Field(so,"fruitSize","果实直径（米）");Field(so,"fruitStalk","果实下垂（米）");
            Field(so,"bloomSurfaceOffset","花簇离叶面（米）");Field(so,"windStrength","风摆幅度");
            Field(so,"showTestTextures","空槽使用测试贴图");Field(so,"livePreview","自动刷新");Field(so,"generateLOD","生成三级 LOD");Field(so,"addTrunkCollider","主干碰撞体");
        }
        bool changed=EditorGUI.EndChangeCheck();if(so.ApplyModifiedProperties()||changed)Schedule();
        if(GUILayout.Button("保存当前纹理贴图为默认贴图（当前物种）",GUILayout.Height(26)))
        {
            try{STSReferenceCapture.DetachTextureInputs(tree);STSDefaultTextures.Save(tree);status="已保存 "+STSPresets.Names[(int)tree.species].Split('/')[0].Trim()+" 的默认贴图；今后新建该物种时自动载入。";}
            catch(System.Exception ex){status="默认贴图保存失败："+ex.Message;Debug.LogError(status);}
        }
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
            if(GUILayout.Button("转存当前植物网格（缩小场景文件）",GUILayout.Height(28)))
                status=STSSceneMeshStorage.TransferCurrent(tree);
            EditorGUILayout.LabelField(STSSceneMeshStorage.InlineCount(output)>0?"网格仍保存在场景中，可点击上方转存。":"网格已保存为独立资源。保存场景后生效。");
            EditorGUILayout.LabelField("LOD0：叶簇 "+output.leafCardCount+"；花簇 "+output.flowerCardCount+"；三角形 "+output.highDetailTriangles.ToString("N0"));
            if(tree.canopyFullness>0&&output.leafCardCount==0)EditorGUILayout.HelpBox("叶片未生成，请检查叶簇 PNG、UV 区域和透明裁切值。",MessageType.Warning);
            EditorGUILayout.LabelField("模型高度 "+(output.localModelHeight*Mathf.Abs(tree.transform.lossyScale.y)).ToString("F1")+" 米");
            if(tree.enableFlowers&&output.flowerCardCount==0)EditorGUILayout.HelpBox("没有可显示的花簇。检查花量和 PNG 的 Alpha；背景须透明，图案不能全透明。",MessageType.Warning);
        }
        EditorGUILayout.Space();EditorGUILayout.LabelField("批量生成同种植物 / Batch Variants",EditorStyles.boldLabel);
        batchCount=EditorGUILayout.IntSlider("生成数量 / Count",batchCount,1,100);
        if(batchSpacing<=0)batchSpacing=Mathf.Max(1,tree.crownWidth*Mathf.Max(Mathf.Abs(tree.transform.lossyScale.x),Mathf.Abs(tree.transform.lossyScale.z))*1.2f);
        batchSpacing=Mathf.Max(.1f,EditorGUILayout.FloatField("间距（米） / Spacing",batchSpacing));
        batchVariation=EditorGUILayout.Slider("造型差异 / Variation",batchVariation,0,1);
        EditorGUILayout.HelpBox("以当前植物为模板，变化冠幅、分枝、扭曲和朝向，生成到同一树群下。差异 0 为相同造型；建议先试 5–10 棵。",MessageType.None);
        if(GUILayout.Button("生成一群同种植物（不同造型）",GUILayout.Height(32)))
        {
            var group=STSPlantBatch.Generate(tree,batchCount,batchSpacing,batchVariation);
            status=group?"已生成 "+batchCount+" 棵同种植物；选中树群父物体可整体移动，Ctrl+Z 可撤销本次生成。":"未生成树群：已取消或生成失败，请查看 Console。";
        }
        if(!string.IsNullOrEmpty(status))EditorGUILayout.HelpBox(status,MessageType.None);
        if(GUILayout.Button("烘焙游戏用 Prefab",GUILayout.Height(28)))STSPlantFiles.Bake(tree,false);
        files=EditorGUILayout.Foldout(files,"保存和载入外观",true);
        if(files)
        {
            if(GUILayout.Button("保存当前参数和贴图为预设"))STSPlantFiles.SavePreset(tree);
            recipe=(STSPlantPreset)EditorGUILayout.ObjectField("预设 / Preset",recipe,typeof(STSPlantPreset),false);
            using(new EditorGUI.DisabledScope(!recipe))if(GUILayout.Button("载入预设")){STSPlantFiles.LoadPreset(tree,recipe);if(STSPresets.IsSupported(tree.species))chosen=tree.species;}
            if(GUILayout.Button("保存可编辑 Prefab"))STSPlantFiles.Bake(tree,true);
        }
    }
}

[CustomEditor(typeof(StylizedTreeAuthoring))]
internal class STSAuthoringInspector : Editor
{
    public override void OnInspectorGUI()
    {
        var t=(StylizedTreeAuthoring)target;EditorGUILayout.LabelField("面片植物工坊 V0.5.15",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("在工坊窗口拖入叶簇和花簇 PNG，再调整密度。",MessageType.Info);
        if(GUILayout.Button("打开植物工坊"))StylizedTreeStudioWindow.OpenFor(t);
        if(!STSPresets.IsSupported(t.species)){EditorGUILayout.HelpBox("该旧物种已移除，已有模型保留。",MessageType.Info);return;}
        if(GUILayout.Button("刷新当前植物"))STSPlantBuilder.Build(t);
    }
}
#endif
