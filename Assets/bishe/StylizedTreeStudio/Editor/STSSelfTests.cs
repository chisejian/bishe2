#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// No NUnit/Test Framework dependency. These checks also run in an ordinary Unity project.
internal static class STSSelfTests
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    [MenuItem("Tools/风格化植物工坊/运行九物种生成检查")]
    public static void Run()
    {
        int speciesCount=0;
        for(int species=1;species<=9;species++)
        {
            var go=new GameObject("STS_Check"){hideFlags=HideFlags.HideAndDontSave};
            try
            {
                var t=go.AddComponent<StylizedTreeAuthoring>();STSPresets.Apply(t,(STSPlantSpecies)species);
                var ids=new HashSet<int>();foreach(var b in t.branches){Require(ids.Add(b.id),"枝条 ID 重复");Require(b.points.Count>=2,"枝条点不足");}
                int previous=int.MaxValue;
                for(int lod=0;lod<3;lod++)
                {
                    var c=new STSOrganContext(t,lod);c.Build();int total=0;
                    foreach(var data in new[]{c.bark,c.leaves,c.flowers,c.centers,c.fruit}){CheckMesh(data);total+=data.TriangleCount;}
                    Require(total>0,"生成空模型："+species);Require(total<=previous,"LOD 未降低面数："+species);previous=total;
                    if(species==7)Require(c.flowers.v.Count==0&&c.fruit.v.Count==0,"海金沙出现了花果");
                }
                var first=new List<Vector3>();foreach(var b in t.branches)first.AddRange(b.points);
                t.leafSeed+=17;t.flowerSeed+=23;t.fruitSeed+=31;STSSkeleton.Generate(t);int index=0;
                foreach(var b in t.branches)foreach(Vector3 p in b.points)Require((p-first[index++]).sqrMagnitude<1e-10f,"花叶种子改变了枝干");
                Require(index==first.Count,"独立种子改变了枝条数量");
                speciesCount++;
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        CheckTube();CheckAttachmentsAndOverlap();
        Debug.Log("[植物工坊] 检查通过："+speciesCount+" 个物种 × 3 个 LOD；网格索引、法线、独立种子、枝端分布、附着跟随和花瓣旋叠。");
    }
    internal static void CheckMesh(STSMeshData data)
    {
        Require(data.v.Count==data.n.Count&&data.v.Count==data.uv.Count&&data.v.Count==data.colors.Count,"顶点通道数量不一致");
        Require(data.triangles.Count%3==0,"三角形索引数量错误");
        foreach(int index in data.triangles)Require(index>=0&&index<data.v.Count,"三角形索引越界");
        for(int i=0;i<data.v.Count;i++){Require(STSPath.Finite(data.v[i]),"非有限顶点");Require(STSPath.Finite(data.n[i])&&data.n[i].sqrMagnitude>.9f,"法线无效或被抵消");}
    }
    static void CheckTube()
    {
        var data=new STSMeshData();
        data.Tube(new List<Vector3>{Vector3.zero,new Vector3(.01f,1,0),new Vector3(.5f,2,.1f),new Vector3(-.2f,3,.4f)},.12f,7,Color.white);
        CheckMesh(data);
        for(int k=0;k<data.triangles.Count;k+=3)
        {
            int a=data.triangles[k],b=data.triangles[k+1],c=data.triangles[k+2];Vector3 face=Vector3.Cross(data.v[b]-data.v[a],data.v[c]-data.v[a]);
            Require(Vector3.Dot(face,data.n[a]+data.n[b]+data.n[c])>=-1e-7f,"圆管三角形朝向与法线相反");
        }
    }
    static void CheckAttachmentsAndOverlap()
    {
        var go=new GameObject("STS_AnchorCheck"){hideFlags=HideFlags.HideAndDontSave};
        try
        {
            var t=go.AddComponent<StylizedTreeAuthoring>();STSPresets.Apply(t,STSPlantSpecies.Plumeria);
            var branch=t.branches[t.branches.Count-1];var anchor=new STSPlantAttachment{branchId=branch.id,t=.7f,offset=new Vector3(.03f,.02f,0)};
            var before=new STSOrganContext(t,0);Vector3 p=before.Frame(anchor).p,delta=new Vector3(.3f,.4f,-.2f);
            for(int i=0;i<branch.points.Count;i++)branch.points[i]+=delta;
            var after=new STSOrganContext(t,0);Require((after.Frame(anchor).p-p-delta).sqrMagnitude<1e-7f,"手工花叶果没有跟随枝条");
            float low=0,high=0;for(int i=0;i<100;i++){low+=after.RandomAnchor(STSOrganKind.Flower,i,123,0).t;high+=after.RandomAnchor(STSOrganKind.Flower,i,123,1).t;}
            Require(high>low+15,"枝端集中参数未正确影响整根枝条");
            t.randomFlowers=1;t.flowerGrouping=STSFlowerGrouping.Single;t.flowerCenterSize=0;t.petalOverlap=0;
            var a=new STSOrganContext(t,0);a.Build();t.petalOverlap=.65f;var b=new STSOrganContext(t,0);b.Build();
            Require(a.flowers.v.Count==b.flowers.v.Count&&a.flowers.v.Count>3,"旋叠测试生成失败");
            float distanceA=(a.flowers.v[0]-a.flowers.v[a.flowers.v.Count/2]).sqrMagnitude,distanceB=(b.flowers.v[0]-b.flowers.v[b.flowers.v.Count/2]).sqrMagnitude;
            Require(Mathf.Abs(distanceA-distanceB)>1e-9f,"旋叠参数只是整体旋转");
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
    }
}
#endif
