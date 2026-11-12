#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class STSReferenceCanopyChecks
{
    static void Require(bool condition,string message)
    {if(!condition)throw new InvalidOperationException("[树冠与枝条检查] "+message);}
    static float Check(StylizedTreeAuthoring t,STSAlphaMask mask,out int count)
    {
        var cards=new List<STSPlacedCard>();STSReferenceCanopy.Build(t,cards,mask);count=cards.Count;
        if(t.canopyFullness<=0){Require(count==0,"叶簇密度为 0 时仍有叶片。");return 0;}
        Require(count>0,"没有生成叶片。");float sum=0,upperRadius=0,lowerRadius=0;int upper=0,lower=0;
        foreach(var card in cards)sum+=card.size.x;
        for(int i=0;i<cards.Count;i++)
        {
            var card=cards[i];
            if(!card.referenceScatter)for(int y=0;y<=10;y++)for(int x=0;x<=10;x++)
                Require(STSReferenceCanopy.Contains(t,card.Point(x*.1f,y*.1f),.0003f),"主树冠叶片超出轮廓边界。");
            if(card.referenceParent>=0)
            {
                Require(card.referenceParent>=0&&card.referenceParent<i,"叶簇衔接关系无效。");
                var parent=cards[card.referenceParent];Vector2 a=card.referenceOwnContact,b=card.referenceParentContact;
                Require((card.Point(a.x,a.y)-parent.Point(b.x,b.y)).sqrMagnitude<1e-7f,"叶簇连接位置分离。");
                Require(mask.Sample(card.uv.x+a.x*card.uv.width,card.uv.y+a.y*card.uv.height)>=t.leafCards.cutoff,"连接点落在透明像素。");
                Require(card.wind==parent.wind&&(card.pivot-parent.pivot).sqrMagnitude<1e-8f,"风摆会拆开叶簇。");
            }
            else
            {
                Vector2 uv=card.referenceOwnContact;Vector3 contact=card.Point(uv.x,uv.y);float distance=float.PositiveInfinity;
                foreach(Vector3 vertex in t.referenceTrunk.barkLODs[0].vertices)
                    distance=Mathf.Min(distance,(STSReferenceGrowth.Point(t,vertex)-contact).sqrMagnitude);
                Require(distance<1e-7f,"叶簇根部没有附着在原枝干表面。");
                Require(mask.Sample(card.uv.x+uv.x*card.uv.width,card.uv.y+uv.y*card.uv.height)>=t.leafCards.cutoff,"根部附着点落在透明区域。");
            }
            if(card.referenceScatter)continue;
            Vector3 p=STSReferenceGrowth.Unwarp(t,card.center);float u=(p.y-STSReferenceCanopy.Bottom(t))/(STSReferenceCanopy.Top(t)-STSReferenceCanopy.Bottom(t));
            float radius=Mathf.Sqrt(p.x*p.x+p.z*p.z);
            if(u<.45f){lower++;lowerRadius=Mathf.Max(lowerRadius,radius);}
            if(u>.75f){upper++;upperRadius=Mathf.Max(upperRadius,radius);}
        }
        HashSet<int> previous=null;
        for(int lod=0;lod<3;lod++)
        {
            var keep=STSReferenceCanopy.LeafSet(t,cards,lod);
            foreach(int index in keep)
            {
                if(previous!=null)Require(previous.Contains(index),"远景叶片集不是中景子集。");
                int parent=cards[index].referenceParent;if(parent>=0)Require(keep.Contains(parent),"LOD 丢失了叶簇的连接支撑。");
            }
            previous=keep;
        }
        if(t.canopyFullness>.8f&&Mathf.Abs(t.leafScale-1)<.01f)
            Require(upper>0&&lower>0&&upperRadius>lowerRadius*(STSReferenceCanopy.Frustum(t)?.95f:1.3f),"树冠没有形成上宽下窄的轮廓。");
        return sum/count;
    }
    [MenuItem("Tools/风格化植物工坊/运行树冠与枝条检查")]
    public static void Run()
    {
        var go=new GameObject("STS_ConeCheck"){hideFlags=HideFlags.HideAndDontSave};
        STSReferenceTrunk profile=null;Mesh mesh=null;
        try
        {
            var t=go.AddComponent<StylizedTreeAuthoring>();t.useReferenceTrunk=true;
            profile=ScriptableObject.CreateInstance<STSReferenceTrunk>();var data=new STSMeshData();
            for(int i=0;i<12;i++)
            {
                float a=i*Mathf.PI*2/12;Vector3 d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                data.Tube(new List<Vector3>{Vector3.up*9,Vector3.up*12+d*2,Vector3.up*16+d*5},.12f,5,Color.white);
            }
            mesh=data.ToMesh("STS_BranchFixture");profile.barkLODs=new[]{mesh};profile.sourceHeight=20;profile.sourceWidth=15;
            t.referenceTrunk=profile;STSReferenceGrowth.Migrate(t);
            t.height=20;t.crownWidth=15;t.trunkTwist=0;
            var mask=STSSelfTests.Pattern(false);int cases=0,count;
            foreach(float density in new[]{.1f,.5f,.9f})
            {
                t.canopyFullness=density;float previousSize=0;
                foreach(float scale in new[]{.35f,1f,2f})
                {
                    t.leafScale=scale;float size=Check(t,mask,out count);
                    Require(size>previousSize,"叶片大小参数没有生效。");previousSize=size;cases++;
                }
            }
            t.leafScale=1;int previousCount=0;
            foreach(float density in new[]{.1f,.5f,.9f})
            {t.canopyFullness=density;Check(t,mask,out count);Require(count>previousCount,"密度参数没有改变叶片数量。");previousCount=count;}
            foreach(float twist in new[]{0f,1.5f,3f})
            {
                t.trunkTwist=twist;t.leafScale=1;t.canopyFullness=.9f;Check(t,mask,out count);cases++;
                Require(STSReferenceGrowth.LocalPoint(t,Vector3.zero).sqrMagnitude<1e-10f,"扭曲改变了根部位置。");
                Vector3 p=new Vector3(2,11,1),n=new Vector3(.3f,.4f,.7f).normalized;
                Vector3 tangent=Vector3.Cross(n,new Vector3(.2f,.7f,.6f)).normalized;
                Vector3 mapped=(STSReferenceGrowth.LocalPoint(t,p+tangent*.001f)-STSReferenceGrowth.LocalPoint(t,p-tangent*.001f)).normalized;
                Require(Mathf.Abs(Vector3.Dot(STSReferenceGrowth.LocalNormal(t,p,n),mapped))<.005f,"弯曲后法线方向不正确。");
                Require((STSReferenceGrowth.Unwarp(t,STSReferenceGrowth.LocalPoint(t,p))-p).sqrMagnitude<1e-7f,"弯曲坐标无法正确还原。");
            }
            t.species=STSPlantSpecies.Plumeria;STSReferenceGrowth.Migrate(t);
            Require(Mathf.Abs(STSReferenceCanopy.Top(t)-17.6f)<.0001f,"鸡蛋花树冠未默认下移。");
            Require(Mathf.Abs(STSReferenceCanopy.Radius(t,STSReferenceCanopy.Bottom(t))/STSReferenceCanopy.Radius(t,STSReferenceCanopy.Top(t))-.62f)<.0001f,"鸡蛋花底部未改成圆台。");
            foreach(float stretch in new[]{.5f,1f,1.5f,2f})
            {
                t.referenceBranchStretch=stretch;Check(t,mask,out count);cases++;
                Vector3 p=new Vector3(2,11,1),n=new Vector3(.3f,.4f,.7f).normalized;
                Vector3 tangent=Vector3.Cross(n,new Vector3(.2f,.7f,.6f)).normalized;
                Vector3 mapped=(STSReferenceGrowth.LocalPoint(t,p+tangent*.001f)-STSReferenceGrowth.LocalPoint(t,p-tangent*.001f)).normalized;
                Require(Mathf.Abs(Vector3.Dot(STSReferenceGrowth.LocalNormal(t,p,n),mapped))<.005f,"拉伸后法线方向不正确。");
                Require((STSReferenceGrowth.Unwarp(t,STSReferenceGrowth.LocalPoint(t,p))-p).sqrMagnitude<1e-7f,"拉伸坐标无法正确还原。");
            }
            float saved=t.referenceCrownOffset=-.16f;STSReferenceGrowth.Migrate(t);
            Require(t.referenceCrownOffset==saved,"重复刷新覆盖了用户树冠偏移。");
            t.canopyFullness=0;Check(t,mask,out count);
            Debug.Log("[树冠与枝条检查] 通过 "+cases+" 组：圆台下移、叶片大小／密度、轮廓边界、枝条表面附着、LOD 支撑、风摆一致、扭曲／拉伸、根部固定、法线与迁移。仍需检查项目中实际 PNG 与材质显示。");
        }
        finally{UnityEngine.Object.DestroyImmediate(go);if(mesh)UnityEngine.Object.DestroyImmediate(mesh);if(profile)UnityEngine.Object.DestroyImmediate(profile);}
    }
}
#endif
