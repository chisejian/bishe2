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
        t.dataVersion=3;
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
            if(b==null||b.points==null||b.points.Count<2)throw new InvalidOperationException("枝条至少需要两个有效控制点。");
            if(!STSPath.Finite(b.radius)||b.radius<=0)throw new InvalidOperationException("枝条半径必须大于零。");
            foreach(Vector3 p in b.points)if(!STSPath.Finite(p))throw new InvalidOperationException("枝条包含 NaN 或 Infinity，已保留上次模型。");
        }
        foreach(float f in new[]{t.height,t.trunkRadius,t.leafSize,t.flowerSize,t.fruitSize,t.petalWidth,t.petalLength,t.inflorescenceLength})
            if(!STSPath.Finite(f)||f<=0)throw new InvalidOperationException("植物尺寸必须是有效正数。");
        if(t.leafClusters<0||t.leafClusters>600||t.randomFlowers<0||t.randomFlowers>300||t.flowersPerCluster<1||t.flowersPerCluster>20||t.fruitCount<0||t.fruitCount>100)
            throw new InvalidOperationException("数量超出编辑器支持范围，请检查高级参数。");
        if(t.branches.Count>1000)throw new InvalidOperationException("枝条超过 1000 根，请减少分枝层数。");
        foreach(var a in t.attachments)if(a!=null&&(!STSPath.Finite(a.offset)||!STSPath.Finite(a.scale)||a.scale<=0))throw new InvalidOperationException("手工花叶果存在无效缩放或坐标。");
        if(t.enableFlowers&&t.flowerMode==STSFlowerMode.Prefab&&!t.flowerPrefab)throw new InvalidOperationException("预制体花朵模式需要先指定 Flower Prefab。");
        if(t.enableFlowers&&t.flowerMode==STSFlowerMode.TextureCards&&!t.flowerTexture)throw new InvalidOperationException("贴图花片模式需要先指定透明 PNG。");
    }
    public static bool Build(StylizedTreeAuthoring t)
    {
        if(!t||EditorUtility.IsPersistent(t)||EditorApplication.isPlayingOrWillChangePlaymode)return false;
        GameObject staged=null;
        try
        {
            Migrate(t);
            if(!t.lockBranches&&t.growthSignature!=STSSkeleton.Signature(t))STSSkeleton.Generate(t);
            Validate(t);STSMaterials.Ensure(t);
            Transform old=t.transform.Find(RootName);
            if(old&&!old.GetComponent<STSGeneratedOutput>()&&!IsLegacy(old))throw new InvalidOperationException("存在同名的非插件对象 STS_Generated，请先重命名该对象。");
            if(old&&old.GetComponent<STSGeneratedOutput>()&&old.GetComponent<STSGeneratedOutput>().owner!=t)throw new InvalidOperationException("生成节点归属不匹配，已停止操作。");
            staged=new GameObject("STS_Building"){hideFlags=HideFlags.HideInHierarchy};staged.transform.SetParent(t.transform,false);
            var marker=staged.AddComponent<STSGeneratedOutput>();marker.owner=t;
            var lods=new List<LOD>();int levels=t.generateLOD?3:1;
            for(int lod=0;lod<levels;lod++)
            {
                var context=new STSOrganContext(t,lod);context.Build();
                var level=new GameObject("LOD"+lod);level.transform.SetParent(staged.transform,false);
                AddMesh(level.transform,"枝干 / Bark",context.bark,t.barkMaterial,marker,lod);
                AddMesh(level.transform,"叶片 / Leaves",context.leaves,t.leafMaterial,marker,lod);
                AddMesh(level.transform,"花瓣苞片 / Flowers",context.flowers,t.flowerMaterial,marker,lod);
                AddMesh(level.transform,"花蕊真花 / Centers",context.centers,t.flowerCenterMaterial,marker,lod);
                AddMesh(level.transform,"果实 / Fruit",context.fruit,t.fruitMaterial,marker,lod);
                foreach(var a in context.prefabFlowers)
                {
                    GameObject instance=PrefabUtility.IsPartOfPrefabAsset(t.flowerPrefab)?(GameObject)PrefabUtility.InstantiatePrefab(t.flowerPrefab):UnityEngine.Object.Instantiate(t.flowerPrefab);
                    if(!instance)continue;instance.transform.SetParent(level.transform,false);STSFrame f=context.Frame(a);
                    instance.transform.localPosition=f.p;instance.transform.localRotation=Quaternion.FromToRotation(Vector3.up,context.FlowerNormal(f))*Quaternion.AngleAxis(a.rotation,Vector3.up);
                    instance.transform.localScale=t.flowerPrefab.transform.localScale*a.scale;
                }
                lods.Add(new LOD(lod==0?.55f:lod==1?.22f:.025f,level.GetComponentsInChildren<Renderer>(true)));
            }
            if(t.generateLOD){var group=staged.AddComponent<LODGroup>();group.SetLODs(lods.ToArray());group.RecalculateBounds();}
            if(t.addTrunkCollider)
            {
                var collider=staged.AddComponent<CapsuleCollider>();collider.center=Vector3.up*t.height*.5f;collider.height=t.height;collider.radius=t.trunkRadius;collider.direction=1;
            }
            foreach(Transform node in staged.GetComponentsInChildren<Transform>(true))marker.ownedObjects.Add(node.gameObject);
            marker.complete=true;
            // Commit only after every level succeeds. Failure leaves the previous output intact.
            if(old)Release(old.gameObject);
            staged.name=RootName;staged.hideFlags=HideFlags.None;staged=null;
            EditorUtility.SetDirty(t);if(t.gameObject.scene.IsValid())EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            BuiltState[t.GetInstanceID()]=JsonUtility.ToJson(t);SceneView.RepaintAll();return true;
        }
        catch(Exception ex){if(staged)Release(staged);Debug.LogError("[植物工坊] "+ex.Message,t);return false;}
    }
    static void AddMesh(Transform parent,string name,STSMeshData data,Material mat,STSGeneratedOutput marker,int lod)
    {
        if(data.v.Count>1000000)throw new InvalidOperationException("单部位超过一百万顶点，请降低数量或花瓣分段。");
        Mesh mesh=data.ToMesh("STS_"+parent.name+"_"+name.Split(' ')[0]);if(!mesh)return;
        marker.ownedMeshes.Add(mesh);var go=new GameObject(name);go.transform.SetParent(parent,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;
        // Default shader uses an art-directed light. Custom Lit/FlatKit materials may cast/receive real shadows.
        renderer.shadowCastingMode=ShadowCastingMode.On;
        if(lod==0){marker.highDetailTriangles+=data.TriangleCount;marker.highDetailVertices+=data.v.Count;}
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
