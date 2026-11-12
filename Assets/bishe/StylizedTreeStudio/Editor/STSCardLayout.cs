#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

internal class STSPlacedCard
{
    public Vector3 center,right,up,normal,pivot;
    public Vector2 size;
    public Rect uv;
    public Color color=Color.white;
    public float wind;
    public bool crownCap;
    public bool topBloom;
    public int clusterKey;
    public bool volumeShading;
    public Vector3 shadeCenter,shadeRadii,canopyCenter,canopyRadii;
    public float occlusion=1;
    public int support=-1;
    public bool referenceLeaf,referenceStructural,referenceScatter;
    public int referenceParent=-1;
    public Vector2 referenceOwnContact,referenceParentContact;
    public Vector3 attachment,contact,rayOrigin,rayDirection;
    public STSPlacedCard(Vector3 center,Vector3 normal,Vector3 up,Vector2 size,Rect uv)
    {
        this.center=center;this.normal=normal.normalized;
        this.up=Vector3.ProjectOnPlane(up,this.normal).normalized;
        if(this.up.sqrMagnitude<.01f)this.up=Vector3.ProjectOnPlane(Vector3.forward,this.normal).normalized;
        right=Vector3.Cross(this.up,this.normal).normalized;this.up=Vector3.Cross(this.normal,right).normalized;
        this.size=size;this.uv=uv;pivot=center;
    }
    public Vector3 Point(float u,float v){return center+right*((u-.5f)*size.x)+up*((v-.5f)*size.y);}
    public bool Hit(Vector3 origin,Vector3 direction,STSAlphaMask mask,float cutoff,out float distance,out Vector3 point)
    {
        point=Vector3.zero;distance=0;float denominator=Vector3.Dot(normal,direction);if(Mathf.Abs(denominator)<.0001f)return false;
        float t=Vector3.Dot(center-origin,normal)/denominator;if(t<0)return false;
        Vector3 hit=origin+direction*t,d=hit-center;
        float u=Vector3.Dot(d,right)/size.x+.5f,v=Vector3.Dot(d,up)/size.y+.5f;
        if(u<0||u>1||v<0||v>1||mask.Sample(uv.x+u*uv.width,uv.y+v*uv.height)<cutoff)return false;
        distance=t;point=hit;return true;
    }
}

internal class STSCardLayout
{
    public readonly StylizedTreeAuthoring tree;
    public readonly List<STSPlacedCard> leaves=new List<STSPlacedCard>(),twigs=new List<STSPlacedCard>(),flowers=new List<STSPlacedCard>(),fruit=new List<STSPlacedCard>();
    public readonly Dictionary<int,List<Vector3>> paths=new Dictionary<int,List<Vector3>>();
    readonly Dictionary<int,StylizedTreeBranch> branches=new Dictionary<int,StylizedTreeBranch>();
    readonly List<StylizedTreeBranch> tips=new List<StylizedTreeBranch>();
    readonly List<int> capLeaves=new List<int>();
    public readonly STSAlphaMask leafMask,bloomMask;
    public STSCardLayout(StylizedTreeAuthoring tree,STSAlphaMask leafMask=null,STSAlphaMask bloomMask=null)
    {
        this.tree=tree;this.leafMask=leafMask??STSAlphaMask.Read(tree.leafCards.color);this.bloomMask=bloomMask??STSAlphaMask.Read(tree.bloomCards.color);
        var parents=new HashSet<int>();foreach(var b in tree.branches)if(b.parentId>=0)parents.Add(b.parentId);
        foreach(var b in tree.branches)
        {branches[b.id]=b;paths[b.id]=STSPath.Shape(b);if(!b.isTrunk&&!b.isFrond&&!parents.Contains(b.id))tips.Add(b);}
        if(tips.Count==0)tips.AddRange(tree.branches);
    }
    public int Depth(StylizedTreeBranch b)
    {
        int depth=0;var seen=new HashSet<int>();
        while(b.parentId>=0&&seen.Add(b.id)){StylizedTreeBranch p;if(!branches.TryGetValue(b.parentId,out p))break;depth++;b=p;}return depth;
    }
    static Vector3 Unit(System.Random rng)
    {
        float y=STSRandom.Range(rng,-1,1),a=STSRandom.Range(rng,0,Mathf.PI*2),r=Mathf.Sqrt(Mathf.Max(0,1-y*y));
        return new Vector3(Mathf.Cos(a)*r,y,Mathf.Sin(a)*r);
    }
    public void Build()
    {
        if(tree.growthForm==STSGrowthForm.Palm)PalmCards();else LeafCards();
        CrownCap();LeafLighting();TwigCards();FlowerCards();FruitCards();
    }
    void LeafCards()
    {
        if(STSReferenceGrowth.Active(tree))
        {
            STSReferenceCanopy.Build(tree,leaves,leafMask);
            for(int i=0;i<leaves.Count;i++)if(leaves[i].crownCap)capLeaves.Add(i);
            return;
        }
        if(tree.canopyFullness<=0)return;
        bool large=tree.growthForm==STSGrowthForm.Tree,shrub=tree.growthForm==STSGrowthForm.Shrub;
        int count=Mathf.RoundToInt(Mathf.Lerp(large?650:shrub?250:70,large?1500:shrub?650:280,tree.canopyFullness));
        if(tree.growthForm==STSGrowthForm.ClimbingFern)count=Mathf.RoundToInt(Mathf.Lerp(35,100,tree.canopyFullness));
        float radius=tree.crownWidth*(large?.105f:shrub?.12f:.05f),cardSize=large?tree.crownWidth*.135f:shrub?tree.crownWidth*.2f:tree.height*.19f;
        for(int i=0;i<count;i++)
        {
            var rng=STSRandom.For(tree.leafSeed,i,510);var branch=tips[rng.Next(tips.Count)];
            float at=large||shrub?STSRandom.Range(rng,.38f,1):STSRandom.Range(rng,.1f,1);STSFrame frame=STSPath.Evaluate(paths[branch.id],at);
            Vector3 d=Unit(rng);float shell=STSRandom.Range(rng,.45f,1);
            Vector3 center=frame.p+new Vector3(d.x,d.y*.72f,d.z)*radius*shell;
            Vector3 outward=(center-new Vector3(0,tree.height*.65f,0)).normalized;
            if(!large&&!shrub)outward=(d+Vector3.up*.3f).normalized;
            Vector3 normal=(outward*.78f+Unit(rng)*.22f).normalized;
            Vector3 up=Quaternion.AngleAxis(STSRandom.Range(rng,-180,180),normal)*Vector3.up;
            float size=cardSize*tree.leafScale*STSRandom.Range(rng,.8f,1.22f);
            float aspect=tree.growthForm==STSGrowthForm.ClimbingFern&&tree.leafCards.color?tree.leafCards.color.width*tree.leafCards.uv.width/(tree.leafCards.color.height*tree.leafCards.uv.height):1;
            var card=new STSPlacedCard(center,normal,up,new Vector2(size*aspect,size),tree.leafCards.uv);
            card.color=tree.leafCards.tint*STSRandom.Range(rng,.97f,1.03f);card.color.a=1;card.wind=Mathf.Clamp01(center.y/Mathf.Max(.1f,tree.height));
            var group=branch;var visited=new HashSet<int>();
            while(visited.Add(group.id)){StylizedTreeBranch parent;if(!branches.TryGetValue(group.parentId,out parent)||parent.isTrunk)break;group=parent;}
            card.clusterKey=group.id;
            leaves.Add(card);
        }
    }
    void CrownCap()
    {
        if(STSReferenceGrowth.Active(tree))return;
        if(tree.growthForm!=STSGrowthForm.Tree||tree.canopyMode!=STSCanopyMode.LayeredClusters||tree.canopyFullness<=0||!leafMask.HasVisible(tree.leafCards.uv,tree.leafCards.cutoff))return;
        StylizedTreeBranch trunk=null;foreach(var b in tree.branches)if(b.isTrunk&&(trunk==null||b.points[b.points.Count-1].y>trunk.points[trunk.points.Count-1].y))trunk=b;
        if(trunk==null)return;Vector3 top=trunk.points[trunk.points.Count-1];
        // No fixed minimum: the same leaf-density slider controls the crown cap.
        // 154 keeps the default .9 density at 139 cards, matching V0.5.3.
        int count=Mathf.RoundToInt(154*Mathf.Clamp01(tree.canopyFullness));float radius=tree.crownWidth*.25f,baseSize=tree.crownWidth*.135f;
        for(int i=0;i<count;i++)
        {
            var rng=STSRandom.For(tree.leafSeed,i,571);float r=i==0?0:Mathf.Sqrt((i-.5f)/(count-1f));float angle=i*2.39996323f;
            Vector3 radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            Vector3 center=top+radial*(r*radius)+Vector3.up*(baseSize*(.16f-.55f*r*r)+STSRandom.Range(rng,-.12f,.12f)*baseSize);
            Vector3 normal=(Vector3.up+radial*(r*.65f)).normalized;
            Vector3 up=Quaternion.AngleAxis(STSRandom.Range(rng,-180,180),normal)*Vector3.forward;
            float size=baseSize*tree.leafScale*STSRandom.Range(rng,.95f,1.18f);var card=new STSPlacedCard(center,normal,up,new Vector2(size,size),tree.leafCards.uv);
            // The central trunk tip must lie under actual opaque leaf pixels,
            // even when the PNG has a transparent hole at its canvas center.
            if(i==0)
            {
                Vector2 opaque=leafMask.OpaquePoint(card.uv,tree.leafCards.cutoff,rng);
                card.center-=card.right*((opaque.x-.5f)*size)+card.up*((opaque.y-.5f)*size);
            }
            card.pivot=card.center;card.color=tree.leafCards.tint*STSRandom.Range(rng,.97f,1.03f);card.color.a=1;card.wind=1;card.crownCap=true;
            card.clusterKey=-100-Mathf.FloorToInt((angle%(Mathf.PI*2))/(Mathf.PI*2)*8);
            capLeaves.Add(leaves.Count);leaves.Add(card);
        }
    }
    void PalmCards()
    {
        // Three straight carrier segments follow each frond. The PNG contains
        // the complete feather leaf: no individual leaflet geometry is made.
        foreach(var b in tree.branches)if(b.isFrond)
        {
            var pts=paths[b.id];float length=STSPath.Length(pts),width=tree.crownWidth*.3f;
            for(int i=0;i<3;i++)
            {
                STSFrame f=STSPath.Evaluate(pts,(i+.5f)/3);Vector3 side=Vector3.Cross(f.tangent,Vector3.up).normalized;if(side.sqrMagnitude<.01f)side=f.right;
                Rect uv=tree.leafCards.uv;uv.y+=uv.height*i/3;uv.height/=3;
                // Scale the complete segmented frond about its stem attachment.
                // Scaling each segment in place would open gaps at small sizes.
                Vector3 center=pts[0]+(f.p-pts[0])*tree.leafScale;
                var card=new STSPlacedCard(center,Vector3.Cross(side,f.tangent),f.tangent,new Vector2(width,length/3*1.04f)*tree.leafScale,uv);
                card.color=tree.leafCards.tint;card.wind=1;leaves.Add(card);
            }
        }
    }
    void LeafLighting()
    {
        // The carrier planes remain unchanged for alpha attachment and shadows.
        // Shared ellipsoid normals form coherent broad shading across each crown lobe.
        var groups=new Dictionary<int,List<STSPlacedCard>>();
        for(int i=0;i<leaves.Count;i++)
        {
            var card=leaves[i];if(tree.growthForm==STSGrowthForm.Palm)card.clusterKey=i/3;
            List<STSPlacedCard> list;if(!groups.TryGetValue(card.clusterKey,out list)){list=new List<STSPlacedCard>();groups.Add(card.clusterKey,list);}list.Add(card);
        }
        bool reference=STSReferenceGrowth.Active(tree);
        Vector3 canopyCenter=reference?STSReferenceCanopy.Center(tree):new Vector3(0,tree.height*.66f,0);
        Vector3 canopyRadii=new Vector3(tree.crownWidth*.55f,reference?(STSReferenceCanopy.Top(tree)-STSReferenceCanopy.Bottom(tree))*.66f:tree.height*.36f,tree.crownWidth*.5f);
        if(reference){float derivative;float stretch=STSReferenceGrowth.Stretch(tree,canopyCenter.y,out derivative);canopyRadii.x*=stretch;canopyRadii.z*=stretch;}
        foreach(var list in groups.Values)
        {
            Vector3 center=Vector3.zero,min=list[0].center,max=min;float size=0;
            foreach(var card in list)
            {
                center+=card.center;size+=card.size.x;
                min=new Vector3(Mathf.Min(min.x,card.center.x),Mathf.Min(min.y,card.center.y),Mathf.Min(min.z,card.center.z));
                max=new Vector3(Mathf.Max(max.x,card.center.x),Mathf.Max(max.y,card.center.y),Mathf.Max(max.z,card.center.z));
            }
            center/=list.Count;size/=list.Count;Vector3 ext=(max-min)*.65f+Vector3.one*(size*.65f);
            center-=Vector3.up*size*.15f;
            foreach(var card in list)
            {
                card.volumeShading=true;card.shadeCenter=center;card.shadeRadii=ext;card.canopyCenter=canopyCenter;card.canopyRadii=canopyRadii;
                Vector3 d=card.center-canopyCenter;float depth=Mathf.Sqrt(d.x*d.x/(canopyRadii.x*canopyRadii.x)+d.y*d.y/(canopyRadii.y*canopyRadii.y)+d.z*d.z/(canopyRadii.z*canopyRadii.z));
                card.occlusion=Mathf.Lerp(.58f,1,Mathf.Clamp01(depth));
                if(tree.growthForm!=STSGrowthForm.Tree&&tree.growthForm!=STSGrowthForm.Shrub)card.occlusion=.95f;
            }
        }
    }
    static Vector3 EllipsoidNormal(Vector3 p,Vector3 center,Vector3 radii)
    {
        Vector3 d=p-center;return new Vector3(d.x/Mathf.Max(.001f,radii.x*radii.x),d.y/Mathf.Max(.001f,radii.y*radii.y),d.z/Mathf.Max(.001f,radii.z*radii.z)).normalized;
    }
    public static Vector3 LightingNormal(STSPlacedCard card,Vector3 position)
    {
        if(!card.volumeShading)return card.normal;
        Vector3 local=EllipsoidNormal(position,card.shadeCenter,card.shadeRadii),canopy=EllipsoidNormal(position,card.canopyCenter,card.canopyRadii);
        Vector3 n=local*.4f+canopy*.6f;return n.sqrMagnitude>.0001f?n.normalized:card.normal;
    }
    void TwigCards()
    {
        foreach(var b in tree.branches)
        {
            if(b.isTrunk||b.isFrond||Depth(b)<3)continue;
            float length=STSPath.Length(paths[b.id]);STSFrame frame=STSPath.Evaluate(paths[b.id],.5f);
            Vector3 normal=Vector3.Cross(frame.tangent,Vector3.right).normalized;if(normal.sqrMagnitude<.01f)normal=Vector3.forward;
            var card=new STSPlacedCard(frame.p,normal,frame.tangent,new Vector2(length*.45f,length*1.1f),tree.twigCards.uv);
            card.color=tree.twigCards.tint;card.wind=.4f;twigs.Add(card);
        }
    }
    public bool Surface(Vector3 origin,Vector3 inward,out int support,out Vector3 point)
    {
        support=-1;point=Vector3.zero;float best=float.PositiveInfinity;
        for(int i=0;i<leaves.Count;i++)
        {
            float distance;Vector3 hit;
            if(leaves[i].Hit(origin,inward,leafMask,tree.leafCards.cutoff,out distance,out hit)&&distance<best)
            {best=distance;point=hit;support=i;}
        }
        return support>=0;
    }
    void FlowerCards()
    {
        if(!tree.enableFlowers||leaves.Count==0||tree.growthForm==STSGrowthForm.ClimbingFern||tree.growthForm==STSGrowthForm.Palm)return;
        if(!leafMask.HasVisible(tree.leafCards.uv,tree.leafCards.cutoff)||!bloomMask.HasVisible(tree.bloomCards.uv,tree.bloomCards.cutoff))return;
        int budget=tree.species==STSPlantSpecies.CrepeMyrtle?850:tree.growthForm==STSGrowthForm.Tree?550:tree.growthForm==STSGrowthForm.Shrub?180:95;
        int wanted=Mathf.RoundToInt(tree.flowerAmount*budget);
        int topLimit=Mathf.RoundToInt(wanted*.22f*Mathf.Clamp01(tree.topBloomDensity)),onTop=0;
        float topY=0;foreach(var b in tree.branches)if(b.isTrunk)topY=Mathf.Max(topY,b.points[b.points.Count-1].y);
        Vector2 centroid=bloomMask.Center(tree.bloomCards.uv,tree.bloomCards.cutoff);
        float size=(tree.growthForm==STSGrowthForm.Tree?tree.crownWidth*.065f:tree.height*.16f)*tree.bloomScale;
        if(STSReferenceGrowth.Active(tree)&&STSReferenceCanopy.Frustum(tree))
            wanted-=BranchFlowers(centroid);
        for(int i=0;i<wanted;i++)
        {
            var rng=STSRandom.For(tree.flowerSeed,i,550);
            for(int attempt=0;attempt<20;attempt++)
            {
                int candidateIndex=capLeaves.Count>0&&i%12==0?capLeaves[rng.Next(capLeaves.Count)]:rng.Next(leaves.Count);
                var candidate=leaves[candidateIndex];Vector2 opaque=leafMask.OpaquePoint(candidate.uv,tree.leafCards.cutoff,rng);
                if(candidate.referenceScatter)continue;
                Vector3 anchor=candidate.Point(opaque.x,opaque.y);
                Vector3 outward=(anchor-(STSReferenceGrowth.Active(tree)?STSReferenceCanopy.Center(tree):new Vector3(0,tree.height*.62f,0))).normalized;
                if(outward.y<-.25f)continue;
                outward=(outward+Vector3.up*.2f).normalized;
                Vector3 origin=anchor+outward*(tree.crownWidth+tree.height);int support;Vector3 contact;
                if(!Surface(origin,-outward,out support,out contact))continue;
                var leaf=leaves[support];if(leaf.referenceScatter)continue;
                Vector3 normal=leaf.normal;if(Vector3.Dot(normal,outward)<0)normal=-normal;
                bool top=tree.growthForm==STSGrowthForm.Tree&&(leaf.crownCap||(contact.y>topY-tree.crownWidth*.065f&&normal.y>.45f));
                // A separate upper budget prevents the cap and random candidates
                // from repeatedly filling the same roof with overlapping blooms.
                if(top&&onTop>=topLimit)break;
                float cardSize=top?size*.78f:size;
                Vector3 target=contact+normal*tree.bloomSurfaceOffset;
                bool overlaps=false;foreach(var old in flowers)
                {
                    Vector3 d=old.attachment-target;
                    if(top&&old.topBloom)
                    {
                        float gap=Mathf.Max(cardSize,old.size.x)*.85f;
                        if(d.x*d.x+d.z*d.z<gap*gap){overlaps=true;break;}
                    }
                    else if(d.sqrMagnitude<cardSize*cardSize*.09f){overlaps=true;break;}
                }
                if(overlaps){if(top)break;continue;}
                Vector3 up=Quaternion.AngleAxis(STSRandom.Range(rng,-180,180),normal)*leaf.up;
                var card=new STSPlacedCard(target,normal,up,new Vector2(cardSize,cardSize),tree.bloomCards.uv);
                // Align the visible flower's alpha centroid, not the PNG canvas center.
                card.center-=card.right*((centroid.x-.5f)*cardSize)+card.up*((centroid.y-.5f)*cardSize);
                card.attachment=target;card.contact=contact;card.rayOrigin=origin;card.rayDirection=-outward;card.support=support;card.pivot=leaf.pivot;card.wind=leaf.wind;card.color=tree.bloomCards.tint;
                card.topBloom=top;if(top)onTop++;flowers.Add(card);break;
            }
        }
    }
    int BranchFlowers(Vector2 centroid)
    {
        int wanted=Mathf.RoundToInt(14*Mathf.Clamp01(tree.flowerAmount)),added=0;
        var candidates=new List<int>();for(int i=0;i<leaves.Count;i++)if(leaves[i].referenceScatter)candidates.Add(i);
        var rng=STSRandom.For(tree.flowerSeed,0,514);
        // Visit distinct small branch clusters, so a high flower setting still
        // leaves most of the original exposed branch silhouette visible.
        for(int i=candidates.Count-1;i>0;i--){int j=rng.Next(i+1),k=candidates[i];candidates[i]=candidates[j];candidates[j]=k;}
        foreach(int index in candidates)
        {
            if(added>=wanted)break;
            var leaf=leaves[index];Vector2 uv=leafMask.OpaquePoint(leaf.uv,tree.leafCards.cutoff,rng);
            Vector3 contact=leaf.Point(uv.x,uv.y),normal=leaf.normal;
            float size=tree.crownWidth*.025f*tree.bloomScale*STSRandom.Range(rng,.85f,1.1f);
            Vector3 target=contact+normal*tree.bloomSurfaceOffset;
            var card=new STSPlacedCard(target,normal,Quaternion.AngleAxis(STSRandom.Range(rng,-180,180),normal)*leaf.up,new Vector2(size,size),tree.bloomCards.uv);
            card.center-=card.right*((centroid.x-.5f)*size)+card.up*((centroid.y-.5f)*size);
            card.referenceScatter=true;card.attachment=target;card.contact=contact;
            card.rayOrigin=contact+normal*.001f;card.rayDirection=-normal;card.support=index;
            card.pivot=leaf.pivot;card.wind=leaf.wind;card.color=tree.bloomCards.tint;
            flowers.Add(card);added++;
        }
        return added;
    }
    void FruitCards()
    {
        if(!tree.enableFruit||!tree.fruitCards.color||leaves.Count==0)return;
        int count=Mathf.Clamp(tree.fruitCount,0,100);
        for(int i=0;i<count;i++)
        {
            var rng=STSRandom.For(tree.fruitSeed,i);var leaf=leaves[rng.Next(leaves.Count)];
            Vector3 p=leaf.center+Vector3.down*tree.fruitStalk;
            var card=new STSPlacedCard(p,leaf.normal,Vector3.up,new Vector2(tree.fruitSize,tree.fruitSize),tree.fruitCards.uv);
            card.color=tree.fruitCards.tint;card.wind=leaf.wind;card.pivot=leaf.pivot;fruit.Add(card);
        }
    }
    public HashSet<int> LeafSet(int lod)
    {
        if(STSReferenceGrowth.Active(tree))
        {
            var selected=STSReferenceCanopy.LeafSet(tree,leaves,lod);
            for(int i=0;i<flowers.Count;i++)if(KeepFlowerAt(i,lod))STSReferenceCanopy.KeepWithParents(leaves,selected,flowers[i].support);
            return selected;
        }
        var keep=new HashSet<int>();int step=lod==0?1:lod==1?4:2;
        for(int i=0;i<leaves.Count;i++)if(leaves[i].crownCap||lod==0||(lod==1?i%step!=0:i%step==0))keep.Add(i);
        for(int i=0;i<flowers.Count;i++)if(KeepFlower(i,lod))keep.Add(flowers[i].support);
        return keep;
    }
    public static bool KeepFlower(int index,int lod){return lod==0||lod==1?lod==0||index%3!=0:index%3==0;}
    public bool KeepFlowerAt(int index,int lod)
    {
        if(!STSReferenceGrowth.Active(tree))return KeepFlower(index,lod);
        return lod==0||(lod==1?index%2==0:index%6==0);
    }
    public static void Append(STSMeshData mesh,STSPlacedCard card)
    {
        int start=mesh.v.Count;
        foreach(Vector2 uv in new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)})
        {
            mesh.v.Add(card.Point(uv.x,uv.y));mesh.n.Add(card.normal);mesh.uv.Add(new Vector2(card.uv.x+uv.x*card.uv.width,card.uv.y+uv.y*card.uv.height));
            mesh.colors.Add(card.color);mesh.wind.Add(new Vector2(card.wind,0));mesh.pivots.Add(card.pivot);
            mesh.volumeNormals.Add(LightingNormal(card,card.Point(uv.x,uv.y)));mesh.foliageLighting.Add(new Vector2(card.occlusion,card.volumeShading?1:0));
        }
        // Exactly two triangles; the material renders the reverse face as well.
        mesh.triangles.Add(start);mesh.triangles.Add(start+1);mesh.triangles.Add(start+2);
        mesh.triangles.Add(start+1);mesh.triangles.Add(start+3);mesh.triangles.Add(start+2);
    }
}
#endif
