#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
internal static class STSPlantBuilder
{
    public const string RootName="STS_Generated";
    static readonly Dictionary<int,string> BuiltState=new Dictionary<int,string>();
    static bool restoring;
    static STSPlantBuilder(){Undo.undoRedoPerformed+=OnUndo;}
    static void OnUndo()
    {
        EditorApplication.delayCall+=()=>{
            if(restoring||EditorApplication.isPlayingOrWillChangePlaymode)return;
            restoring=true;
            try
            {
                foreach(var t in Resources.FindObjectsOfTypeAll<StylizedTreeAuthoring>())
                {
                    if(!t||EditorUtility.IsPersistent(t)||!t.gameObject.scene.IsValid()||!t.gameObject.scene.isLoaded)continue;
                    string old;string now=JsonUtility.ToJson(t);
                    if(BuiltState.TryGetValue(t.GetInstanceID(),out old)&&old!=now)Build(t);
                }
            }
            finally{restoring=false;}
        };
    }
    public static void Migrate(StylizedTreeAuthoring t)
    {
        if(t.branches==null)t.branches=new List<StylizedTreeBranch>();
        if(t.attachments==null)t.attachments=new List<STSPlantAttachment>();
        if(t.paintedFlowers==null)t.paintedFlowers=new List<Vector3>();
        if(t.vinePath==null)t.vinePath=new List<Vector3>();
        if(t.dataVersion<3&&t.branches.Count>0)
        {
            int id=1;var trunk=new StylizedTreeBranch{isTrunk=true,id=id++,radius=t.trunkRadius,
                points=new List<Vector3>{Vector3.zero,Vector3.up*t.height*.52f,Vector3.up*t.height}};
            foreach(var b in t.branches){b.id=id++;b.parentId=trunk.id;b.isTrunk=false;if(b.points.Count>0){float distance;b.parentT=STSPath.Closest(trunk.points,b.points[0],out distance);}}
            t.branches.Insert(0,trunk);t.nextBranchId=id;t.lockBranches=true;
        }
        var used=new HashSet<int>();t.nextBranchId=Mathf.Max(1,t.nextBranchId);
        foreach(var b in t.branches)if(b!=null&&b.id>0)t.nextBranchId=Mathf.Max(t.nextBranchId,b.id+1);
        foreach(var b in t.branches)if(b!=null){if(b.id<=0||!used.Add(b.id)){b.id=t.nextBranchId++;used.Add(b.id);}}
        if(t.petalOutline==null)t.petalOutline=AnimationCurve.EaseInOut(0,.05f,1,.05f);
        if(t.petalBendProfile==null)t.petalBendProfile=AnimationCurve.Linear(0,0,1,1);
        if(t.radiusProfile==null)t.radiusProfile=AnimationCurve.Linear(0,1,1,.08f);
        if(t.crownProfile==null)t.crownProfile=AnimationCurve.Linear(0,1,1,.3f);
        if(t.dataVersion<3)t.growthSignature=STSSkeleton.Signature(t);
        t.dataVersion=Mathf.Max(3,t.dataVersion);
        if(t.branches.Count==0)STSSkeleton.Generate(t);
        foreach(Vector3 p in t.paintedFlowers)
        {
            if(!STSPath.Finite(p))continue;
            STSPlantAttachment a=Nearest(t,p,STSOrganKind.Flower);if(a!=null)t.attachments.Add(a);
        }
        t.paintedFlowers.Clear();
    }
    public static STSPlantAttachment Nearest(StylizedTreeAuthoring t,Vector3 point,STSOrganKind kind)
    {
        float best=float.PositiveInfinity;STSPlantAttachment anchor=null;
        foreach(var b in t.branches)
        {
            if(b==null||b.points.Count<2)continue;var path=STSPath.Shape(b);float distance;
            float at=STSPath.Closest(path,point,out distance);if(distance>=best)continue;
            best=distance;STSFrame f=STSPath.Evaluate(path,at);Vector3 delta=point-f.p;
            anchor=new STSPlantAttachment{kind=kind,branchId=b.id,t=at,offset=new Vector3(Vector3.Dot(delta,f.right),Vector3.Dot(delta,f.up),Vector3.Dot(delta,f.tangent))};
        }
        return anchor;
    }
    static void Validate(StylizedTreeAuthoring t)
    {
        foreach(var b in t.branches)
        {
            if(b==null||b.points==null||b.points.Count<2||!STSPath.Finite(b.radius)||b.radius<=0)throw new InvalidOperationException("枝条数据无效。");
            foreach(Vector3 p in b.points)if(!STSPath.Finite(p))throw new InvalidOperationException("枝条含无效坐标，已保留上次模型。");
        }
        foreach(float value in new[]{t.height,t.crownWidth,t.bloomScale,t.bloomSurfaceOffset})if(!STSPath.Finite(value)||value<=0)throw new InvalidOperationException("高度、冠幅、花簇大小和偏移须为有效正数。");
        if(!STSPath.Finite(t.trunkTwist)||t.trunkTwist<0||t.trunkTwist>1)throw new InvalidOperationException("树干扭曲度须在 0–1 之间。");
        if(!STSPath.Finite(t.leafScale)||t.leafScale<.2f||t.leafScale>3)throw new InvalidOperationException("叶片大小须在 0.2–3 倍之间。");
        if(t.branches.Count>1000)throw new InvalidOperationException("枝条超过支持范围。");
        foreach(var map in new[]{t.leafCards,t.twigCards,t.bloomCards,t.fruitCards})
        {
            Rect uv=map.uv;
            if(!STSPath.Finite(uv.x)||!STSPath.Finite(uv.y)||!STSPath.Finite(uv.width)||!STSPath.Finite(uv.height)||uv.width<=0||uv.height<=0||uv.x<0||uv.y<0||uv.x+uv.width>1.0001f||uv.y+uv.height>1.0001f)
                throw new InvalidOperationException("UV 区域必须位于 0–1 内，宽高须为有效正数。");
        }
        if(t.canopyFullness>0&&!t.leafCards.color)throw new InvalidOperationException("请指定叶簇 PNG，或启用随包测试贴图。");
    }
    public static bool Build(StylizedTreeAuthoring t,STSAlphaMask testLeaves=null,STSAlphaMask testBlooms=null)
    {
        if(!t||EditorUtility.IsPersistent(t)||EditorApplication.isPlayingOrWillChangePlaymode)return false;
        GameObject staged=null;
        try
        {
            Migrate(t);STSMaterials.Defaults(t);
            if(!t.lockBranches&&t.growthSignature!=STSSkeleton.Signature(t))STSSkeleton.Generate(t);
            Validate(t);STSMaterials.Ensure(t);
            var layout=new STSCardLayout(t,testLeaves,testBlooms);
            if(t.enableFlowers&&t.bloomCards.color&&testLeaves==null&&(!layout.leafMask.verified||!layout.bloomMask.verified))
                throw new InvalidOperationException("花簇表面定位需要读取叶簇和花簇 PNG 的 Alpha。请使用项目内的 PNG 源文件。");
            layout.Build();
            Transform old=t.transform.Find(RootName);
            if(old&&!old.GetComponent<STSGeneratedOutput>()&&!IsLegacy(old))throw new InvalidOperationException("同名节点不属于本插件，已停止。");
            if(old&&old.GetComponent<STSGeneratedOutput>()&&old.GetComponent<STSGeneratedOutput>().owner!=t)throw new InvalidOperationException("生成节点归属不匹配。");
            staged=new GameObject("STS_Building"){hideFlags=HideFlags.HideInHierarchy};staged.transform.SetParent(t.transform,false);
            var marker=staged.AddComponent<STSGeneratedOutput>();marker.owner=t;marker.leafCardCount=layout.leaves.Count;marker.flowerCardCount=layout.flowers.Count;
            marker.alphaSurfaceVerified=layout.leafMask.verified&&layout.bloomMask.verified;
            var lods=new List<LOD>();int levels=t.generateLOD?3:1;
            for(int lod=0;lod<levels;lod++)
            {
                var bark=new STSMeshData();
                foreach(var b in t.branches)
                {
                    int depth=layout.Depth(b);if(b.isFrond||depth>2||(lod==2&&depth>1))continue;
                    var pts=layout.paths[b.id];var sample=new List<Vector3>();int segments=b.isTrunk?Mathf.Max(4,7-lod):Mathf.Max(2,4-depth-lod);
                    if(b.isTrunk)segments+=Mathf.CeilToInt(Mathf.Clamp01(t.trunkTwist)*(17-lod*4));
                    for(int k=0;k<=segments;k++)sample.Add(STSPath.Evaluate(pts,k/(float)segments).p);
                    int sides=b.isTrunk?Mathf.Max(5,t.radialSides-lod):Mathf.Max(3,5-depth-lod);
                    bark.Tube(sample,b.radius,sides,t.barkColor?Color.white:t.barkTint,u=>b.isTrunk?Mathf.Max(.02f,t.radiusProfile.Evaluate(u)):Mathf.Lerp(1,t.branchTipRatio,u));
                }
                var leaves=new STSMeshData();var keep=layout.LeafSet(lod);foreach(int index in keep)STSCardLayout.Append(leaves,layout.leaves[index]);
                var twigs=new STSMeshData();if(t.twigCards.color)for(int k=0;k<layout.twigs.Count;k++)if(lod==0||k%(lod+1)==0)STSCardLayout.Append(twigs,layout.twigs[k]);
                var blooms=new STSMeshData();if(t.bloomCards.color)for(int k=0;k<layout.flowers.Count;k++)if(STSCardLayout.KeepFlower(k,lod))STSCardLayout.Append(blooms,layout.flowers[k]);
                var fruit=new STSMeshData();for(int k=0;k<layout.fruit.Count;k++)STSCardLayout.Append(fruit,layout.fruit[k]);
                var level=new GameObject("LOD"+lod);level.transform.SetParent(staged.transform,false);
                AddMesh(level.transform,"枝干 / Bark",bark,t.barkMaterial,marker,lod);
                AddMesh(level.transform,"叶簇面片 / Leaf Cards",leaves,t.leafMaterial,marker,lod);
                AddMesh(level.transform,"细枝面片 / Twig Cards",twigs,t.flowerCenterMaterial,marker,lod);
                AddMesh(level.transform,"表面花簇 / Bloom Cards",blooms,t.flowerMaterial,marker,lod);
                AddMesh(level.transform,"果实面片 / Fruit Cards",fruit,t.fruitMaterial,marker,lod);
                lods.Add(new LOD(lod==0?.55f:lod==1?.22f:.025f,level.GetComponentsInChildren<Renderer>(true)));
            }
            if(t.generateLOD){var group=staged.AddComponent<LODGroup>();group.SetLODs(lods.ToArray());group.RecalculateBounds();}
            if(t.addTrunkCollider){var c=staged.AddComponent<CapsuleCollider>();c.center=Vector3.up*t.height*.5f;c.height=t.height;c.radius=t.trunkRadius;c.direction=1;}
            foreach(Transform node in staged.GetComponentsInChildren<Transform>(true))marker.ownedObjects.Add(node.gameObject);marker.complete=true;
            if(old)Release(old.gameObject);staged.name=RootName;staged.hideFlags=HideFlags.None;staged=null;
            EditorUtility.SetDirty(t);if(t.gameObject.scene.IsValid())EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            BuiltState[t.GetInstanceID()]=JsonUtility.ToJson(t);SceneView.RepaintAll();return true;
        }
        catch(Exception ex){if(staged)Release(staged);Debug.LogError("[面片植物工坊] "+ex.Message,t);return false;}
    }
    static void AddMesh(Transform parent,string name,STSMeshData data,Material mat,STSGeneratedOutput marker,int lod)
    {
        if(data.v.Count>1000000)throw new InvalidOperationException("单部位超过一百万顶点，请降低面片密度。");
        Mesh mesh=data.ToMesh("STS_"+parent.name+"_"+name.Split(' ')[0]);if(!mesh)return;
        marker.ownedMeshes.Add(mesh);var go=new GameObject(name);go.transform.SetParent(parent,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;
        // URP card shader uses alpha-tested, double-sided shadows.
        renderer.shadowCastingMode=ShadowCastingMode.On;
        if(lod==0)
        {
            marker.highDetailTriangles+=data.TriangleCount;marker.highDetailVertices+=data.v.Count;
            foreach(Vector3 p in data.v)
            {
                if(!marker.hasBounds){marker.localMinimum=marker.localMaximum=p;marker.hasBounds=true;}
                marker.localMinimum=new Vector3(Mathf.Min(marker.localMinimum.x,p.x),Mathf.Min(marker.localMinimum.y,p.y),Mathf.Min(marker.localMinimum.z,p.z));
                marker.localMaximum=new Vector3(Mathf.Max(marker.localMaximum.x,p.x),Mathf.Max(marker.localMaximum.y,p.y),Mathf.Max(marker.localMaximum.z,p.z));
            }
            marker.localModelHeight=marker.localMaximum.y-marker.localMinimum.y;marker.localModelWidth=marker.localMaximum.x-marker.localMinimum.x;
        }
    }
    static bool IsLegacy(Transform root)
    {
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))if(filter.sharedMesh&&filter.sharedMesh.name=="STS_Branch")return true;
        return false;
    }
    public static void Release(GameObject root)
    {
        if(!root)return;var marker=root.GetComponent<STSGeneratedOutput>();var meshes=new HashSet<Mesh>();
        if(marker)
        {
            foreach(var mesh in marker.ownedMeshes)if(mesh&&!EditorUtility.IsPersistent(mesh))meshes.Add(mesh);
            if(marker.complete&&marker.owner)
            {
                var owned=new HashSet<GameObject>(marker.ownedObjects);var extras=new List<Transform>();
                foreach(Transform node in root.GetComponentsInChildren<Transform>(true))
                    if(node!=root.transform&&!owned.Contains(node.gameObject)&&node.parent&&owned.Contains(node.parent.gameObject))extras.Add(node);
                foreach(var node in extras)node.SetParent(marker.owner.transform,true);
            }
        }
        else
        {
            foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))if(f.sharedMesh&&!EditorUtility.IsPersistent(f.sharedMesh)&&f.sharedMesh.name.StartsWith("STS_",StringComparison.Ordinal))meshes.Add(f.sharedMesh);
        }
        UnityEngine.Object.DestroyImmediate(root);
        foreach(var mesh in meshes)if(mesh)UnityEngine.Object.DestroyImmediate(mesh);
    }
}
#endif
