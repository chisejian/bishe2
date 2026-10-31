#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class StylizedTreeStudioWindow : EditorWindow
{
    enum EditMode {None,Branches,VinePath,Paint,Erase,Organs}
    [SerializeField] StylizedTreeAuthoring tree;
    [SerializeField] STSPlantSpecies chosen=STSPlantSpecies.CrepeMyrtle;
    STSPlantPreset recipe;
    EditMode mode;
    STSOrganKind organ=STSOrganKind.Flower;
    int branchIndex,pointIndex,batchCount=3,attachmentIndex=-1;
    float paintRadius=22,paintScale=1,paintRotation,branchAttach=.75f;
    Vector2 scroll;
    bool growth=true,leaves=true,flowers=true,fruit,climbing,materials,advanced;
    double rebuildAt=-1;
    string status;

    [MenuItem("Tools/风格化植物工坊/Stylized Tree Studio")]
    public static void Open(){GetWindow<StylizedTreeStudioWindow>("风格化植物工坊 V0.3");}
    public static void OpenFor(StylizedTreeAuthoring t){var w=GetWindow<StylizedTreeStudioWindow>("风格化植物工坊 V0.3");w.SetTree(t);}
    void OnEnable(){SceneView.duringSceneGui+=SceneGUI;EditorApplication.update+=Tick;}
    void OnDisable(){SceneView.duringSceneGui-=SceneGUI;EditorApplication.update-=Tick;}
    void SetTree(StylizedTreeAuthoring t){tree=t;mode=EditMode.None;branchIndex=pointIndex=0;if(tree){chosen=tree.species;STSPlantBuilder.Migrate(tree);}Repaint();}
    void Tick(){if(rebuildAt<0||EditorApplication.timeSinceStartup<rebuildAt)return;rebuildAt=-1;if(tree)Rebuild();}
    void Schedule(){if(tree&&tree.livePreview)rebuildAt=EditorApplication.timeSinceStartup+.25;}
    void Rebuild(){rebuildAt=-1;status=STSPlantBuilder.Build(tree)?"已刷新。":"生成失败：请查看控制台（Console）；上次模型已保留。";Repaint();}
    static void Fields(SerializedObject so,params string[] fields)
    {
        foreach(string entry in fields)
        {
            string[] pair=entry.Split('|');var p=so.FindProperty(pair[0]);if(p==null)continue;
            string[] options=EnumLabels(pair[0]);GUIContent label=new GUIContent(pair.Length>1?pair[1]:pair[0]);
            if(options!=null)p.enumValueIndex=EditorGUILayout.Popup(label,p.enumValueIndex,options);
            else EditorGUILayout.PropertyField(p,label,true);
        }
    }
    static string[] EnumLabels(string name)
    {
        switch(name)
        {
            case "growthForm":return new[]{"乔木 / Tree","灌木 / Shrub","棕榈 / Palm","藤本 / Vine","攀援蕨类 / Fern"};
            case "leafShape":return new[]{"椭圆叶 / Oval","狭长叶 / Lanceolate","三裂叶 / Lobed","锯齿叶 / Serrated","羽状叶 / Feather","蕨类复叶 / Fern"};
            case "flowerMode":return new[]{"程序花瓣 / Procedural","透明花片 / Texture Cards","花朵预制体 / Prefab"};
            case "flowerShape":return new[]{"通用 / Generic","大花紫薇 / Myrtle","扶桑 / Hibiscus","白兰 / Champaca","百香果 / Passion Flower","柑橘 / Citrus","鸡蛋花 / Plumeria","三角梅 / Bougainvillea"};
            case "flowerGrouping":return new[]{"单花 / Single","圆锥花序 / Panicle","枝端花簇 / Tip Cluster"};
            case "flowerFacing":return new[]{"朝上 / Up","朝外 / Outward","沿枝 / Along Branch","下垂 / Down"};
            case "vineLayout":return new[]{"手工曲线 / Free Curve","向上攀援 / Climb","绕柱 / Wrap Column","攀架 / Trellis","垂挂 / Hanging"};
            default:return null;
        }
    }
    void Record(string label){Undo.RecordObject(tree,label);EditorUtility.SetDirty(tree);}
    void OnGUI()
    {
        scroll=EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox("选择物种生成整株植物。枝条、花叶果可在场景视图（Scene）中修改；参数可保存为自己的预设。",MessageType.Info);
        var next=(StylizedTreeAuthoring)EditorGUILayout.ObjectField("当前植物 / Plant",tree,typeof(StylizedTreeAuthoring),true);if(next!=tree)SetTree(next);
        chosen=(STSPlantSpecies)EditorGUILayout.Popup("物种预设 / Species",(int)chosen,STSPresets.Names);
        using(new EditorGUILayout.HorizontalScope())
        {
            if(GUILayout.Button("新建所选植物"))Create();
            if(GUILayout.Button("使用当前选中植物"))SetTree(Selection.activeGameObject?Selection.activeGameObject.GetComponentInParent<StylizedTreeAuthoring>():null);
        }
        if(!tree){EditorGUILayout.EndScrollView();return;}
        using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if(GUILayout.Button("应用所选预设并重建（替换当前造型，可撤销）")){Record("应用植物预设");STSPresets.Apply(tree,chosen);mode=EditMode.None;Rebuild();}
            var so=new SerializedObject(tree);so.Update();EditorGUI.BeginChangeCheck();
            Fields(so,"livePreview|自动刷新预览 / Live Preview","lockBranches|锁住现有枝条造型 / Lock Branches");
            if(tree.lockBranches)EditorGUILayout.HelpBox("枝条已锁定：高度、冠幅等参数不会自动改动现有枝条。解除锁定后刷新可应用生长参数。",MessageType.None);
            growth=EditorGUILayout.Foldout(growth,"树形与随机 / Shape & Seeds",true);
            if(growth)
            {
                Fields(so,"growthForm|生长形式 / Growth Form","height|主干高度（米） / Height","crownWidth|冠幅／藤展宽度（米） / Width","trunkRadius|主干半径（米） / Radius",
                    "baseStemCount|灌木主干数量 / Base Stems","branchCount|主枝／羽叶数量 / Primary Count","branchLevels|分枝层数 / Levels","secondaryBranches|每级侧枝数量 / Children",
                    "crownStart|起枝高度比例 / Crown Start","branchRise|枝条上扬 / Rise","branchDroop|枝条下垂 / Droop","trunkBend|主干弯曲 / Bend","irregularity|不规则程度 / Irregularity",
                    "crownProfile|树冠轮廓 / Crown Profile","radiusProfile|主干粗细曲线 / Radius Profile","branchTipRatio|枝端粗细比例 / Tip Ratio","radialSides|枝干截面边数 / Sides",
                    "seed|枝干种子 / Branch Seed","leafSeed|叶片种子 / Leaf Seed","flowerSeed|花朵种子 / Flower Seed","fruitSeed|果实种子 / Fruit Seed");
            }
            leaves=EditorGUILayout.Foldout(leaves,"叶片与复叶 / Leaves & Compound Leaves",true);
            if(leaves)Fields(so,"leafShape|叶形 / Leaf Shape","leafClusters|叶片／复叶着生点数量 / Nodes","leavesPerNode|每个点的叶片数量 / Leaves Per Node","leafSize|叶片／复叶长度（米） / Length",
                "leafWidth|叶片宽长比 / Width Ratio","leafBend|叶片弯曲 / Bend","leafEdge|叶缘锯齿幅度 / Serration","leafTipBias|枝端集中程度 / Tip Bias","leafTint|叶片颜色 / Color",
                "leafColorVariation|叶色变化 / Variation","palmLeafletPairs|王棕小叶对数 / Palm Pairs","fernPinnaPairs|蕨叶羽片对数 / Fern Pinnae","fernPinnulePairs|蕨叶小羽片对数 / Pinnules");
            flowers=EditorGUILayout.Foldout(flowers,"花朵、苞片与花序 / Flowers & Bracts",true);
            if(flowers)
            {
                Fields(so,"enableFlowers|生成花朵 / Enable Flowers","flowerMode|花朵模式 / Mode","flowerShape|花朵结构 / Structure","flowerGrouping|花序形式 / Grouping","flowerFacing|花朵朝向 / Facing",
                    "randomFlowers|单花／花序着生点数量 / Sites","flowersPerCluster|每花序内的花数 / Flowers Per Cluster","inflorescenceLength|花序长度（米） / Cluster Length","flowerPedicel|花梗长度（米） / Pedicel",
                    "flowerSize|单花半径（米） / Flower Radius","flowerSizeVariation|大小变化 / Size Variation","flowerTipBias|真实枝端集中程度 / Tip Bias","flowerTint|花瓣／苞片颜色 / Color","flowerCenterTint|花蕊／真花颜色 / Center Color");
                STSFlowerMode fm=(STSFlowerMode)so.FindProperty("flowerMode").enumValueIndex;
                if(fm==STSFlowerMode.ProceduralPetals)Fields(so,"petalCount|花瓣／花被片数量 / Petals","petalWidth|花瓣宽度 / Width","petalLength|花瓣长度 / Length","petalCup|花瓣开合弯曲 / Cup",
                    "petalTwist|花瓣扭转 / Twist","petalOverlap|花瓣旋叠 / Overlap","petalRuffle|花瓣褶皱 / Ruffle","petalThickness|花瓣厚度比例 / Thickness","flowerCenterSize|花心大小 / Center Size",
                    "petalOutline|花瓣轮廓曲线 / Outline","petalBendProfile|花瓣弯曲曲线 / Bend Profile","petalSegments|花瓣纵向分段 / Segments","petalColumns|花瓣横向分段 / Columns");
                else if(fm==STSFlowerMode.TextureCards)Fields(so,"flowerTexture|透明花片 PNG / Texture","textureCardCrossCount|交叉面片数量 / Cards");
                else Fields(so,"flowerPrefab|花朵预制体（朝 +Y） / Prefab");
            }
            fruit=EditorGUILayout.Foldout(fruit,"果实 / Fruit",true);
            if(fruit)Fields(so,"enableFruit|生成果实 / Enable Fruit","fruitCount|果实数量 / Count","fruitSize|果实直径（米） / Diameter","fruitSizeVariation|大小变化 / Variation",
                "fruitPearShape|梨形程度 / Pear Shape","fruitStalk|果柄长度（米） / Stalk","fruitRipeness|成熟程度 / Ripeness","fruitYoungTint|未成熟颜色 / Young Color","fruitRipeTint|成熟颜色 / Ripe Color");
            climbing=EditorGUILayout.Foldout(climbing,"攀援与贴附 / Climbing & Projection",true);
            if(climbing)Fields(so,"vineLayout|默认路径造型 / Layout","vineWrapRadius|绕柱半径（米） / Wrap Radius","vineTurns|绕柱圈数 / Turns","vineSag|垂坠程度 / Sag","enableTendrils|卷须（用于百香果） / Tendrils",
                "projectionTarget|贴附目标碰撞体 / Target Collider","projectionDirection|贴附方向（植物局部） / Direction","projectionDistance|投射距离（米） / Distance","surfaceOffset|离开表面距离（米） / Offset");
            materials=EditorGUILayout.Foldout(materials,"材质与游戏导出 / Materials & Export",true);
            if(materials)
            {
                Fields(so,"barkTint|枝干颜色 / Bark Color","barkMaterial|枝干材质 / Bark","leafMaterial|叶片材质 / Leaves","flowerMaterial|花瓣苞片材质 / Flowers","flowerCenterMaterial|花蕊真花材质 / Centers","fruitMaterial|果实材质 / Fruit",
                    "generateLOD|生成三级细节层级 / LOD","lod1Density|中距离叶花密度 / LOD1 Density","lod2Density|远距离叶花密度 / LOD2 Density","addTrunkCollider|添加简化主干碰撞体 / Collider");
                EditorGUILayout.HelpBox("默认材质支持顶点色和透明裁切，使用固定造型光。FlatKit 可手工赋材质；颜色和实时阴影由所用着色器决定。",MessageType.None);
            }
            bool changed=EditorGUI.EndChangeCheck();if(so.ApplyModifiedProperties()||changed)Schedule();
            if(materials&&GUILayout.Button("给当前植物恢复工坊默认材质")){Record("恢复默认材质");STSMaterials.ResetDefaults(tree);Rebuild();}
            if(climbing&&GUILayout.Button("按上方设置重建默认藤蔓路径（覆盖路径，可撤销）")){Record("重建藤蔓路径");STSSkeleton.DefaultVinePath(tree);STSSkeleton.Generate(tree);Rebuild();}
            EditorGUILayout.Space();
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("重新随机树形（覆盖枝条，可撤销）")){Record("重新生成树形");STSSkeleton.Generate(tree);Rebuild();}
                if(GUILayout.Button("生成／刷新模型",GUILayout.Height(30)))Rebuild();
            }
            EditingUI();
            EditorGUILayout.Space();
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("保存自定义预设"))STSPlantFiles.SavePreset(tree);
                recipe=(STSPlantPreset)EditorGUILayout.ObjectField(recipe,typeof(STSPlantPreset),false);
                using(new EditorGUI.DisabledScope(!recipe))if(GUILayout.Button("载入预设")){STSPlantFiles.LoadPreset(tree,recipe);chosen=tree.species;}
            }
            batchCount=EditorGUILayout.IntSlider("批量变体数量 / Variants",batchCount,1,20);
            if(GUILayout.Button("在旁边生成变体"))STSPlantFiles.Variants(tree,batchCount,Mathf.Max(tree.crownWidth,tree.height*.35f)+1);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("烘焙游戏用 Prefab",GUILayout.Height(32)))STSPlantFiles.Bake(tree,false);
                if(GUILayout.Button("保存可编辑 Prefab",GUILayout.Height(32)))STSPlantFiles.Bake(tree,true);
            }
            Transform root=tree.transform.Find(STSPlantBuilder.RootName);var output=root?root.GetComponent<STSGeneratedOutput>():null;
            if(output)EditorGUILayout.LabelField("程序网格 LOD0："+output.highDetailTriangles.ToString("N0")+" 三角形 / "+output.highDetailVertices.ToString("N0")+" 顶点（不含外接花朵 Prefab）");
            if(!string.IsNullOrEmpty(status))EditorGUILayout.HelpBox(status,MessageType.None);
            advanced=EditorGUILayout.Foldout(advanced,"手工数据（高级） / Authoring Data",true);
            if(advanced){so.Update();EditorGUI.BeginChangeCheck();Fields(so,"branches|枝条 / Branches","attachments|手工花叶果 / Attachments","vinePath|藤蔓路径控制点 / Path");bool dataChanged=EditorGUI.EndChangeCheck();if(so.ApplyModifiedProperties()||dataChanged){tree.lockBranches=true;Schedule();}}
        }
        EditorGUILayout.EndScrollView();
    }
    void Create()
    {
        var go=new GameObject(STSPresets.Names[(int)chosen].Split('/')[0].Trim());Undo.RegisterCreatedObjectUndo(go,"新建植物");
        tree=go.AddComponent<StylizedTreeAuthoring>();STSPresets.Apply(tree,chosen);Selection.activeGameObject=go;Rebuild();
        if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.FrameSelected();
    }
    void EditingUI()
    {
        EditorGUILayout.Space();EditorGUILayout.LabelField("场景编辑 / Scene Editing",EditorStyles.boldLabel);
        mode=(EditMode)GUILayout.Toolbar((int)mode,new[]{"结束","枝条","藤蔓路径","种花叶果","擦除","选花叶果"});
        EditorGUILayout.HelpBox("Esc 结束；Alt＋鼠标保留视角操作。枝条模式用 Shift＋点击线条选枝，点击控制点后拖动。编辑枝条会自动锁住树形。",MessageType.None);
        if(mode==EditMode.Branches)
        {
            var names=new List<string>();foreach(var b in tree.branches)names.Add((b.isTrunk?"主干 ":b.isFrond?"羽叶 ":"枝条 ")+b.id);
            branchIndex=Mathf.Clamp(branchIndex,0,Mathf.Max(0,names.Count-1));
            if(names.Count>0)
            {
                branchIndex=EditorGUILayout.Popup("当前枝条 / Branch",branchIndex,names.ToArray());var b=tree.branches[branchIndex];
                pointIndex=EditorGUILayout.IntSlider("当前控制点 / Point",pointIndex,0,Mathf.Max(0,b.points.Count-1));
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("在当前点后插入控制点")){Record("插入控制点");int k=Mathf.Clamp(pointIndex,0,b.points.Count-1);b.points.Insert(k+1,k<b.points.Count-1?(b.points[k]+b.points[k+1])*.5f:b.points[k]+Vector3.up*.2f);pointIndex=k+1;tree.lockBranches=true;STSSkeleton.ReattachChildren(tree,b.id);Rebuild();}
                    using(new EditorGUI.DisabledScope(b.points.Count<=2))if(GUILayout.Button("删除当前控制点")){Record("删除控制点");b.points.RemoveAt(Mathf.Clamp(pointIndex,0,b.points.Count-1));pointIndex=0;tree.lockBranches=true;STSSkeleton.ReattachChildren(tree,b.id);Rebuild();}
                }
                branchAttach=EditorGUILayout.Slider("新枝着生位置 / Attach",branchAttach,.05f,.98f);
                if(GUILayout.Button("从当前枝条增加子枝")){Record("添加子枝");STSFrame f=STSPath.OnBranch(b,branchAttach);float len=tree.height*.18f;tree.branches.Add(new StylizedTreeBranch{id=tree.nextBranchId++,parentId=b.id,parentT=branchAttach,radius=b.radius*.25f,points=new List<Vector3>{f.p,f.p+(f.right+Vector3.up)*len*.5f,f.p+f.right*len+Vector3.up*len*.65f}});branchIndex=tree.branches.Count-1;pointIndex=0;tree.lockBranches=true;Rebuild();}
                using(new EditorGUI.DisabledScope(b.isTrunk))if(GUILayout.Button("删除当前枝条及其子枝（可撤销）"))DeleteBranch(b.id);
            }
        }
        if(mode==EditMode.Paint||mode==EditMode.Erase||mode==EditMode.Organs)
        {
            organ=(STSOrganKind)EditorGUILayout.Popup("花叶果类型 / Organ",(int)organ,new[]{"花朵 / Flower","叶片 / Leaf","果实 / Fruit"});
            paintRadius=EditorGUILayout.Slider("拾取半径（像素） / Pick Radius",paintRadius,8,60);
            if(mode==EditMode.Organs)
            {
                if(attachmentIndex>=0&&attachmentIndex<tree.attachments.Count)
                {
                    var a=tree.attachments[attachmentIndex];EditorGUI.BeginChangeCheck();
                    float scale=EditorGUILayout.Slider("选中器官缩放 / Scale",a.scale,.2f,3),rotation=EditorGUILayout.Slider("选中器官旋转 / Rotation",a.rotation,0,360);
                    if(EditorGUI.EndChangeCheck()){Record("调整手工花叶果");a.scale=scale;a.rotation=rotation;Schedule();}
                }
                else EditorGUILayout.HelpBox("在场景中点击手工花叶果的紫色标记。选中后可拖动位置，并在这里调整旋转和缩放。",MessageType.None);
            }
            else {paintScale=EditorGUILayout.Slider("放置缩放 / Scale",paintScale,.2f,3);paintRotation=EditorGUILayout.Slider("放置旋转 / Rotation",paintRotation,0,360);}
            if((organ==STSOrganKind.Flower&&!tree.enableFlowers)||(organ==STSOrganKind.Fruit&&!tree.enableFruit))EditorGUILayout.HelpBox("请先启用对应的花朵或果实开关。海金沙预设不生成花果。",MessageType.Warning);
            if(GUILayout.Button("清空当前类型的手工放置（可撤销）")){Record("清空手工放置");tree.attachments.RemoveAll(a=>a.kind==organ);Rebuild();}
        }
        if(mode==EditMode.VinePath)
        {
            if(tree.vinePath.Count<2)EditorGUILayout.HelpBox("先使用“重建默认藤蔓路径”创建路径。",MessageType.Info);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("增加路径末端点")){Record("增加路径点");int n=tree.vinePath.Count;tree.vinePath.Add(n>0?tree.vinePath[n-1]+Vector3.up*.3f:Vector3.zero);tree.vineLayout=STSVineLayout.FreeCurve;STSSkeleton.Generate(tree);Rebuild();}
                using(new EditorGUI.DisabledScope(tree.vinePath.Count<=2))if(GUILayout.Button("删除路径末端点")){Record("删除路径点");tree.vinePath.RemoveAt(tree.vinePath.Count-1);tree.vineLayout=STSVineLayout.FreeCurve;STSSkeleton.Generate(tree);Rebuild();}
            }
        }
    }
    void DeleteBranch(int id)
    {
        Record("删除枝条及子枝");var ids=new HashSet<int>{id};bool changed=true;
        while(changed){changed=false;foreach(var b in tree.branches)if(ids.Contains(b.parentId)&&ids.Add(b.id))changed=true;}
        tree.branches.RemoveAll(b=>ids.Contains(b.id));tree.attachments.RemoveAll(a=>ids.Contains(a.branchId));tree.lockBranches=true;branchIndex=pointIndex=0;Rebuild();
    }
    void SceneGUI(SceneView view)
    {
        if(!tree||mode==EditMode.None||EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(Selection.activeGameObject&&Selection.activeGameObject.GetComponentInParent<StylizedTreeAuthoring>()!=tree)return;
        Event e=Event.current;
        if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){mode=EditMode.None;e.Use();Repaint();return;}
        if(mode==EditMode.Branches)
        {
            branchIndex=Mathf.Clamp(branchIndex,0,Mathf.Max(0,tree.branches.Count-1));
            for(int k=0;k<tree.branches.Count;k++)
            {
                var b=tree.branches[k];var path=STSPath.Shape(b);var world=new Vector3[path.Count];for(int j=0;j<path.Count;j++)world[j]=tree.transform.TransformPoint(path[j]);
                Handles.color=k==branchIndex?Color.cyan:new Color(.3f,.75f,.8f,.4f);Handles.DrawAAPolyLine(k==branchIndex?3:1,world);
                if(e.type==EventType.MouseDown&&e.button==0&&e.shift&&!e.alt&&HandleUtility.DistanceToPolyLine(world)<7){branchIndex=k;pointIndex=0;Repaint();e.Use();}
                if(k!=branchIndex)continue;
                for(int j=0;j<b.points.Count;j++)
                {
                    Vector3 p=tree.transform.TransformPoint(b.points[j]);float radius=HandleUtility.GetHandleSize(p)*.055f;
                    if(Handles.Button(p,Quaternion.identity,radius,radius,Handles.SphereHandleCap)){pointIndex=j;Repaint();}
                }
                pointIndex=Mathf.Clamp(pointIndex,0,b.points.Count-1);Vector3 point=tree.transform.TransformPoint(b.points[pointIndex]);
                EditorGUI.BeginChangeCheck();Vector3 moved=Handles.PositionHandle(point,Quaternion.identity);
                if(EditorGUI.EndChangeCheck()&&STSPath.Finite(moved))
                {
                    Record("移动枝条控制点");b.points[pointIndex]=tree.transform.InverseTransformPoint(moved);
                    if(pointIndex==0&&b.parentId>=0){var parent=tree.branches.Find(x=>x.id==b.parentId);if(parent!=null){float distance;b.parentT=STSPath.Closest(STSPath.Shape(parent),b.points[0],out distance);b.points[0]=STSPath.OnBranch(parent,b.parentT).p;}}
                    STSSkeleton.ReattachChildren(tree,b.id);tree.lockBranches=true;Schedule();
                }
            }
        }
        else if(mode==EditMode.VinePath)
        {
            for(int i=0;i<tree.vinePath.Count;i++)
            {
                Vector3 p=tree.transform.TransformPoint(tree.vinePath[i]);if(i>0)Handles.DrawLine(tree.transform.TransformPoint(tree.vinePath[i-1]),p);
                EditorGUI.BeginChangeCheck();Vector3 moved=Handles.PositionHandle(p,Quaternion.identity);
                if(EditorGUI.EndChangeCheck()&&STSPath.Finite(moved)){Record("移动藤蔓路径");tree.vinePath[i]=tree.transform.InverseTransformPoint(moved);tree.vineLayout=STSVineLayout.FreeCurve;STSSkeleton.Generate(tree);Schedule();}
            }
        }
        else if(mode==EditMode.Organs)EditOrgans(e);
        else Paint(e);
    }
    void EditOrgans(Event e)
    {
        if(e.alt)return;float best=paintRadius;int picked=-1;
        for(int i=0;i<tree.attachments.Count;i++)
        {
            var a=tree.attachments[i];if(a.kind!=organ)continue;var b=tree.branches.Find(x=>x.id==a.branchId);if(b==null)continue;
            STSFrame f=STSPath.OnBranch(b,a.t);Vector3 p=tree.transform.TransformPoint(f.p+f.Offset(a.offset));
            Handles.color=i==attachmentIndex?Color.cyan:Color.magenta;Handles.SphereHandleCap(0,p,Quaternion.identity,HandleUtility.GetHandleSize(p)*.065f,EventType.Repaint);
            float dist=Vector2.Distance(e.mousePosition,HandleUtility.WorldToGUIPoint(p));if(dist<best){best=dist;picked=i;}
        }
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        if(e.type==EventType.MouseDown&&e.button==0&&picked>=0&&picked!=attachmentIndex){attachmentIndex=picked;e.Use();Repaint();}
        if(attachmentIndex<0||attachmentIndex>=tree.attachments.Count)return;
        var anchor=tree.attachments[attachmentIndex];var branch=tree.branches.Find(x=>x.id==anchor.branchId);if(branch==null)return;
        STSFrame frame=STSPath.OnBranch(branch,anchor.t);Vector3 world=tree.transform.TransformPoint(frame.p+frame.Offset(anchor.offset));
        EditorGUI.BeginChangeCheck();Vector3 moved=Handles.PositionHandle(world,Quaternion.identity);
        if(EditorGUI.EndChangeCheck()&&STSPath.Finite(moved))
        {
            Record("移动手工花叶果");Vector3 d=tree.transform.InverseTransformPoint(moved)-frame.p;
            anchor.offset=new Vector3(Vector3.Dot(d,frame.right),Vector3.Dot(d,frame.up),Vector3.Dot(d,frame.tangent));Schedule();
        }
    }
    void Paint(Event e)
    {
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        if(e.alt)return;
        if(mode==EditMode.Erase)
        {
            float best=paintRadius;int picked=-1;
            for(int i=0;i<tree.attachments.Count;i++)
            {
                var a=tree.attachments[i];if(a.kind!=organ)continue;var b=tree.branches.Find(x=>x.id==a.branchId);if(b==null)continue;
                STSFrame f=STSPath.OnBranch(b,a.t);Vector3 p=tree.transform.TransformPoint(f.p+f.Offset(a.offset));float distance=Vector2.Distance(e.mousePosition,HandleUtility.WorldToGUIPoint(p));
                Handles.color=Color.magenta;Handles.SphereHandleCap(0,p,Quaternion.identity,HandleUtility.GetHandleSize(p)*.06f,EventType.Repaint);
                if(distance<best){best=distance;picked=i;}
            }
            if(picked>=0&&e.type==EventType.MouseDown&&e.button==0){Record("擦除手工花叶果");tree.attachments.RemoveAt(picked);Rebuild();e.Use();}return;
        }
        Vector3 closest=Vector3.zero;float pick=paintRadius;StylizedTreeBranch target=null;float at=0;
        foreach(var b in tree.branches)
        {
            var points=STSPath.Shape(b);float length=STSPath.Length(points),travel=0;
            for(int j=1;j<points.Count;j++)
            {
                Vector3 wa=tree.transform.TransformPoint(points[j-1]),wb=tree.transform.TransformPoint(points[j]);Vector2 a=HandleUtility.WorldToGUIPoint(wa),c=HandleUtility.WorldToGUIPoint(wb),d=c-a;
                float f=Mathf.Clamp01(Vector2.Dot(e.mousePosition-a,d)/Mathf.Max(.001f,d.sqrMagnitude)),dist=Vector2.Distance(e.mousePosition,a+d*f),len=Vector3.Distance(points[j-1],points[j]);
                if(dist<pick){pick=dist;closest=Vector3.Lerp(wa,wb,f);target=b;at=(travel+len*f)/Mathf.Max(.001f,length);}travel+=len;
            }
        }
        if(target==null)return;
        Handles.color=Color.magenta;Handles.SphereHandleCap(0,closest,Quaternion.identity,HandleUtility.GetHandleSize(closest)*.09f,EventType.Repaint);
        if(e.type==EventType.MouseDown&&e.button==0){Record("手工种花叶果");tree.attachments.Add(new STSPlantAttachment{kind=organ,branchId=target.id,t=at,scale=paintScale,rotation=paintRotation});Rebuild();e.Use();}
    }
}

[CustomEditor(typeof(StylizedTreeAuthoring))]
internal class STSAuthoringInspector : Editor
{
    public override void OnInspectorGUI()
    {
        var t=(StylizedTreeAuthoring)target;
        EditorGUILayout.LabelField("风格化植物工坊 V0.3",EditorStyles.boldLabel);
        EditorGUILayout.LabelField(STSPresets.Names[(int)t.species]);
        EditorGUILayout.HelpBox("在工坊窗口修改参数和场景控制点。游戏使用前请烘焙 Prefab。",MessageType.Info);
        if(GUILayout.Button("打开植物编辑窗口"))StylizedTreeStudioWindow.OpenFor(t);
        if(GUILayout.Button("刷新当前植物"))STSPlantBuilder.Build(t);
    }
}
#endif
