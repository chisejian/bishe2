#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Algorithm checks, without a dependency on NUnit or Unity Test Framework.
internal static class STSSelfTests
{
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    internal static STSAlphaMask Pattern(bool flower)
    {
        const int side=96;var a=new float[side*side];
        for(int y=0;y<side;y++)for(int x=0;x<side;x++)
        {
            float u=x/(float)(side-1),v=y/(float)(side-1);
            if(flower)a[y*side+x]=(u>.52f&&u<.87f&&v>.27f&&v<.72f)?1:0;
            else {float d=(u-.5f)*(u-.5f)+(v-.5f)*(v-.5f);a[y*side+x]=d<.22f&&d>.012f?1:0;}
        }
        return new STSAlphaMask(side,side,a);
    }
    [MenuItem("Tools/风格化植物工坊/运行面片定位检查")]
    public static void Run()
    {
        var leaf=Pattern(false);var bloom=Pattern(true);int total=0;
        CheckTransparentHole(leaf);CheckTube();CheckLeafDensity(leaf,bloom);
        for(int species=1;species<=9;species++)
        {
            var go=new GameObject("STS_CardCheck"){hideFlags=HideFlags.HideAndDontSave};
            try
            {
                var t=go.AddComponent<StylizedTreeAuthoring>();STSPresets.Apply(t,(STSPlantSpecies)species);STSMaterials.Defaults(t);
                if(t.growthForm==STSGrowthForm.Tree||t.growthForm==STSGrowthForm.Palm)Require(t.height>=20,"乔木／王棕低于 20 米");
                if(species==1)Require(t.branches.Count>=350,"大花紫薇枝条层次不足");
                var layout=new STSCardLayout(t,leaf,bloom);layout.Build();Require(layout.leaves.Count>0,"没有叶簇");
                CheckLeafLighting(layout);
                int previous=int.MaxValue;
                for(int lod=0;lod<3;lod++)
                {
                    var mesh=new STSMeshData();var keep=layout.LeafSet(lod);
                    if(t.growthForm==STSGrowthForm.Tree)CheckCrown(t,layout,keep);
                    foreach(int index in keep)STSCardLayout.Append(mesh,layout.leaves[index]);
                    for(int i=0;i<layout.flowers.Count;i++)if(STSCardLayout.KeepFlower(i,lod))
                    {Require(keep.Contains(layout.flowers[i].support),"LOD 丢失花簇承托叶片");STSCardLayout.Append(mesh,layout.flowers[i]);}
                    CheckMesh(mesh);Require(mesh.TriangleCount<=previous,"LOD 没有减少面片");previous=mesh.TriangleCount;
                }
                if(t.enableFlowers)
                {
                    Require(layout.flowers.Count>Mathf.RoundToInt(t.flowerAmount*40),"花簇生成过少");
                    Vector2 centroid=bloom.Center(t.bloomCards.uv,t.bloomCards.cutoff);
                    foreach(var flower in layout.flowers)
                    {
                        var support=layout.leaves[flower.support];Vector3 delta=flower.contact-support.center;
                        float u=Vector3.Dot(delta,support.right)/support.size.x+.5f,v=Vector3.Dot(delta,support.up)/support.size.y+.5f;
                        Require(leaf.Sample(support.uv.x+u*support.uv.width,support.uv.y+v*support.uv.height)>=t.leafCards.cutoff,"花簇落在透明区域");
                        Require(Mathf.Abs(Vector3.Dot(flower.attachment-flower.contact,flower.normal)-t.bloomSurfaceOffset)<.0001f,"花簇离叶面距离错误");
                        Require((flower.Point(centroid.x,centroid.y)-flower.attachment).sqrMagnitude<1e-7f,"透明留白导致花图偏移");
                        Require((flower.pivot-support.pivot).sqrMagnitude<1e-8f&&flower.wind==support.wind,"花叶风摆不同步");
                        int first;Vector3 hit;Require(layout.Surface(flower.rayOrigin,flower.rayDirection,out first,out hit)&&first==flower.support&&(hit-flower.contact).sqrMagnitude<1e-7f,"花没有使用外层首个不透明表面");
                        total++;
                    }
                }
                else Require(layout.flowers.Count==0,"王棕／海金沙出现花簇");
                Debug.Log("[面片检查] "+STSPresets.Names[species]+"：枝条 "+t.branches.Count+"，叶簇 "+layout.leaves.Count+"，外层花簇 "+layout.flowers.Count);
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        Debug.Log("[面片检查] 通过：九物种 × 三级 LOD；"+total+" 簇花的 Alpha 表面、图案重心、离面间距、风摆、顶部覆盖和承托检查。仍需在 Unity 中查看实际材质效果。");
    }
    static void CheckLeafDensity(STSAlphaMask leaf,STSAlphaMask bloom)
    {
        var go=new GameObject("STS_DensityCheck"){hideFlags=HideFlags.HideAndDontSave};
        try
        {
            var t=go.AddComponent<StylizedTreeAuthoring>();STSPresets.Apply(t,STSPlantSpecies.CrepeMyrtle);t.enableFlowers=false;
            float[] densities={0,.1f,.25f,.5f,.9f,1};int[] expected={0,15,38,77,139,154};
            for(int k=0;k<densities.Length;k++)
            {
                t.canopyFullness=densities[k];var layout=new STSCardLayout(t,leaf,bloom);layout.Build();int top=0;
                foreach(var card in layout.leaves)if(card.crownCap)top++;
                Require(top==expected[k],"叶簇密度未正确控制顶部数量");
                if(k==0)Require(layout.leaves.Count==0,"密度归零仍有残留叶片");
                for(int lod=0;lod<3;lod++)
                {
                    int kept=0;foreach(int index in layout.LeafSet(lod))if(layout.leaves[index].crownCap)kept++;
                    Require(kept==expected[k],"LOD 绕过叶簇密度恢复了顶部叶片");
                }
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
    }
    static void CheckTransparentHole(STSAlphaMask mask)
    {
        var front=new STSPlacedCard(Vector3.forward,Vector3.forward,Vector3.up,Vector2.one,new Rect(0,0,1,1));
        float distance;Vector3 point;
        Require(!front.Hit(Vector3.forward*3,Vector3.forward*-1,mask,.35f,out distance,out point),"射线未穿过叶簇透明孔洞");
        var opaque=new STSAlphaMask(1,1,new[]{1f});
        var back=new STSPlacedCard(Vector3.zero,Vector3.forward,Vector3.up,Vector2.one,new Rect(0,0,1,1));
        Require(back.Hit(Vector3.forward*3,Vector3.forward*-1,opaque,.35f,out distance,out point)&&Mathf.Abs(distance-3)<1e-6f,"透明孔后的表面未命中");
    }
    static void CheckLeafLighting(STSCardLayout layout)
    {
        var first=new Dictionary<int,STSPlacedCard>();int shared=0;
        foreach(var card in layout.leaves)
        {
            Require(card.volumeShading&&card.occlusion>=.5f&&card.occlusion<=1,"叶簇光照数据无效");
            foreach(var p in new[]{card.Point(0,0),card.Point(1,0),card.Point(0,1),card.Point(1,1)})
            {
                Vector3 n=STSCardLayout.LightingNormal(card,p);Require(STSPath.Finite(n)&&n.sqrMagnitude>.99f,"团簇法线无效");
            }
            STSPlacedCard other;if(first.TryGetValue(card.clusterKey,out other))
            {
                Vector3 p=(card.center+other.center)*.5f;
                Require((STSCardLayout.LightingNormal(card,p)-STSCardLayout.LightingNormal(other,p)).sqrMagnitude<1e-8f,"同簇面片未共享连续光照");shared++;
            }
            else first.Add(card.clusterKey,card);
        }
        Require(shared>0,"没有形成共享光照的叶簇");
    }
    static void CheckCrown(StylizedTreeAuthoring t,STSCardLayout layout,HashSet<int> keep)
    {
        Vector3 top=t.branches[0].points[t.branches[0].points.Count-1];float radius=t.crownWidth*.15f,minY=top.y-t.crownWidth*.135f*.6f;
        int tested=0,covered=0;
        for(int z=-4;z<=4;z++)for(int x=-4;x<=4;x++)
        {
            float px=x/4f*radius,pz=z/4f*radius;if(px*px+pz*pz>radius*radius)continue;tested++;
            Vector3 origin=top+new Vector3(px,t.height,pz);bool hit=false;
            foreach(int index in keep)
            {
                float distance;Vector3 p;if(layout.leaves[index].Hit(origin,Vector3.down,layout.leafMask,t.leafCards.cutoff,out distance,out p)&&p.y>=minY){hit=true;break;}
            }
            if(hit)covered++;
        }
        Require(covered>=tested*.94f,"树冠顶部存在大面积透空");
        for(int i=0;i<layout.leaves.Count;i++)if(layout.leaves[i].crownCap)Require(keep.Contains(i),"LOD 丢失顶部封冠叶簇");
    }
    internal static void CheckMesh(STSMeshData data)
    {
        Require(data.v.Count==data.n.Count&&data.v.Count==data.uv.Count&&data.v.Count==data.colors.Count,"顶点通道长度不一致");
        Require(data.triangles.Count%3==0,"索引数量错误");
        if(data.volumeNormals.Count>0)Require(data.volumeNormals.Count==data.v.Count&&data.foliageLighting.Count==data.v.Count,"团簇光照通道丢失");
        foreach(int index in data.triangles)Require(index>=0&&index<data.v.Count,"索引越界");
        foreach(Vector3 p in data.v)Require(STSPath.Finite(p),"无效顶点");
        for(int i=0;i<data.triangles.Count;i+=3)
        {
            int a=data.triangles[i],b=data.triangles[i+1],c=data.triangles[i+2];
            Vector3 cross=Vector3.Cross(data.v[b]-data.v[a],data.v[c]-data.v[a]);
            Require(cross.sqrMagnitude>1e-14f,"退化三角形");Require(Vector3.Dot(cross,data.n[a]+data.n[b]+data.n[c])>=-1e-7f,"三角形朝向错误");
        }
    }
    static void CheckTube()
    {
        var mesh=new STSMeshData();mesh.Tube(new List<Vector3>{Vector3.zero,Vector3.up,new Vector3(.4f,2,.1f)},.12f,6,Color.white);CheckMesh(mesh);
    }
}
#endif
