#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

// Reference-trunk foliage has its own placement constraint, but uses the same
// PNGs, density, scale, shading, flowers and baked card format as the workshop.
internal static class STSReferenceCanopy
{
    public static bool Frustum(StylizedTreeAuthoring t) { return t.species==STSPlantSpecies.Plumeria; }
    static float Offset(StylizedTreeAuthoring t) { return Frustum(t)?t.height*t.referenceCrownOffset:0; }
    public static float Bottom(StylizedTreeAuthoring t) { return t.height*t.referenceCrownStart+Offset(t); }
    public static float Top(StylizedTreeAuthoring t) { return t.height+Offset(t); }
    public static float Radius(StylizedTreeAuthoring t,float y)
    {
        float u=Mathf.Clamp01((y-Bottom(t))/Mathf.Max(.01f,Top(t)-Bottom(t)));
        return t.crownWidth*.48f*Mathf.Lerp(t.referenceCrownBaseRatio,1,u);
    }
    public static Vector3 Center(StylizedTreeAuthoring t)
    { return STSReferenceGrowth.LocalPoint(t,Vector3.up*Mathf.Lerp(Bottom(t),Top(t),.6f)); }
    public static bool Contains(StylizedTreeAuthoring t,Vector3 p,float tolerance=0)
    {
        if(!STSPath.Finite(p)||p.y<Bottom(t)-tolerance||p.y>Top(t)+tolerance)return false;
        Vector3 q=STSReferenceGrowth.Unwarp(t,p);
        float r=Radius(t,q.y)+tolerance;
        return q.x*q.x+q.z*q.z<=r*r;
    }
    public static bool Fits(StylizedTreeAuthoring t,STSPlacedCard card)
    {
        Vector3 a=card.Point(0,0),b=card.Point(1,0),c=card.Point(0,1),d=card.Point(1,1);
        float min=Mathf.Min(Mathf.Min(a.y,b.y),Mathf.Min(c.y,d.y)),max=Mathf.Max(Mathf.Max(a.y,b.y),Mathf.Max(c.y,d.y));
        // The cone/frustum radius is linear in height. Shrinking the four corner
        // radii by M*heightSpan^2/8 bounds the interpolation error of that axis,
        // so the ENTIRE plane fits, including points between sampled vertices.
        float curvature=.33f*Mathf.PI*Mathf.PI*Mathf.Min(Mathf.Abs(t.trunkTwist),StylizedTreeAuthoring.MaxTrunkTwist)/Mathf.Max(.01f,t.height);
        float span=Mathf.Max(.01f,t.height*.48f),radius=t.crownWidth*.48f;
        float slope=radius*Mathf.Abs(1-t.referenceCrownBaseRatio)/Mathf.Max(.01f,Top(t)-Bottom(t));
        curvature+=Mathf.Abs(t.referenceBranchStretch-1)*(6*radius/(span*span)+3*slope/span);
        float margin=curvature*(max-min)*(max-min)*.125f+t.crownWidth*.00002f;
        return WithinMargin(t,a,margin)&&WithinMargin(t,b,margin)&&WithinMargin(t,c,margin)&&WithinMargin(t,d,margin);
    }
    static bool WithinMargin(StylizedTreeAuthoring t,Vector3 p,float margin)
    {
        if(!STSPath.Finite(p)||p.y<Bottom(t)||p.y>Top(t))return false;
        float derivative,stretch=STSReferenceGrowth.Stretch(t,p.y,out derivative);
        Vector3 q=STSReferenceGrowth.Unwarp(t,p);float radius=Radius(t,p.y)-margin/stretch;
        return radius>0&&q.x*q.x+q.z*q.z<=radius*radius;
    }
    static List<Vector2> OpaqueSamples(STSAlphaMask mask,Rect rect,float cutoff)
    {
        var result=new List<Vector2>();var interior=new List<Vector2>();
        for(int y=0;y<40;y++)for(int x=0;x<40;x++)
        {
            float u=(x+.5f)/40,v=(y+.5f)/40;
            if(mask.Sample(rect.x+u*rect.width,rect.y+v*rect.height)<cutoff)continue;
            result.Add(new Vector2(u,v));
            // Prefer a small opaque patch rather than a single edge pixel, so
            // contact is less fragile when the texture switches mip levels.
            bool solid=true;
            for(int k=0;k<4;k++)
            {
                float du=k==0?-.01f:k==1?.01f:0,dv=k==2?-.01f:k==3?.01f:0;
                if(mask.Sample(rect.x+(u+du)*rect.width,rect.y+(v+dv)*rect.height)<cutoff){solid=false;break;}
            }
            if(solid)interior.Add(new Vector2(u,v));
        }
        return interior.Count>=8?interior:result;
    }
    static Vector2 ParentContact(STSPlacedCard parent,Vector3 target,List<Vector2> opaque,int salt)
    {
        Vector2 best=opaque[0];float distance=float.PositiveInfinity;
        for(int k=0;k<24;k++)
        {
            Vector2 uv=opaque[(salt*37+k*59)%opaque.Count];
            float d=(parent.Point(uv.x,uv.y)-target).sqrMagnitude;
            if(d<distance){distance=d;best=uv;}
        }
        return best;
    }
    static Vector2 OwnContact(STSPlacedCard card,Vector3 toward,List<Vector2> opaque,int salt)
    {
        Vector2 best=opaque[0];float projection=float.PositiveInfinity;
        for(int k=0;k<24;k++)
        {
            Vector2 uv=opaque[(salt*43+k*61)%opaque.Count];
            float d=Vector3.Dot(card.right*(uv.x-.5f)+card.up*(uv.y-.5f),toward);
            if(d<projection){projection=d;best=uv;}
        }
        return best;
    }
    static void Anchor(STSPlacedCard card,Vector3 contact,Vector2 uv)
    { card.center=contact-card.right*((uv.x-.5f)*card.size.x)-card.up*((uv.y-.5f)*card.size.y); }

    static void RootCards(StylizedTreeAuthoring t,List<STSPlacedCard> cards,List<Vector2> opaque,int count,float size)
    {
        // Use ONLY the private bark snapshot, never the old leaf-card edges.
        // Several independent branch-attached clusters leave room for exposed
        // branches. No condition requires the crown to hide the skeleton.
        var candidates=new List<Vector3>();var cells=new HashSet<Vector3Int>();
        float bottom=Bottom(t),depth=Top(t)-bottom,cell=Mathf.Max(.001f,t.crownWidth*.018f);
        foreach(Vector3 vertex in t.referenceTrunk.barkLODs[0].vertices)
        {
            Vector3 p=STSReferenceGrowth.CanonicalPoint(t,vertex);
            if(p.y<bottom+depth*.03f||p.y>Top(t)-depth*.04f)continue;
            float radius=Radius(t,p.y)*.9f;
            if(p.x*p.x+p.z*p.z>radius*radius)continue;
            var key=new Vector3Int(Mathf.RoundToInt(p.x/cell),Mathf.RoundToInt(p.y/cell),Mathf.RoundToInt(p.z/cell));
            if(cells.Add(key))candidates.Add(p);
        }
        if(candidates.Count==0)
            throw new InvalidOperationException("原枝干与当前树冠范围没有交点，请恢复原比例后重试。");
        var distance=new float[candidates.Count];int next=0;
        for(int i=0;i<candidates.Count;i++)
        {distance[i]=float.PositiveInfinity;if(candidates[i].y>candidates[next].y)next=i;}
        Vector2 rootUV=opaque[0];float uvDistance=float.PositiveInfinity;
        foreach(Vector2 uv in opaque)
        {float d=(uv-new Vector2(.5f,.5f)).sqrMagnitude;if(d<uvDistance){uvDistance=d;rootUV=uv;}}
        for(int i=0;i<Mathf.Min(16,count);i++)
        {
            Vector3 p=candidates[next],contact=STSReferenceGrowth.LocalPoint(t,p);
            var rng=STSRandom.For(t.leafSeed,i,513);
            Vector3 radial=new Vector3(p.x,0,p.z).normalized;
            Vector3 normal=STSReferenceGrowth.LocalNormal(t,p,(Vector3.up+radial*.35f).normalized);
            Vector3 up=Quaternion.AngleAxis(STSRandom.Range(rng,-180,180),normal)*Vector3.forward;
            var card=new STSPlacedCard(contact,normal,up,new Vector2(size,size),t.leafCards.uv);
            Anchor(card,contact,rootUV);
            for(int attempt=0;attempt<18&&!Fits(t,card);attempt++)
            {card.size*=.82f;Anchor(card,contact,rootUV);}
            if(Fits(t,card))
            {
                card.referenceLeaf=true;card.referenceParent=-1;card.referenceStructural=true;
                card.referenceOwnContact=rootUV;card.contact=contact;card.pivot=contact;
                card.wind=Mathf.Clamp01(p.y/t.height);card.color=t.leafCards.tint;card.color.a=1;
                card.clusterKey=i;cards.Add(card);
            }
            float farthest=0;
            for(int j=0;j<candidates.Count;j++)
            {distance[j]=Mathf.Min(distance[j],(candidates[j]-p).sqrMagnitude);if(distance[j]>farthest){farthest=distance[j];next=j;}}
            if(farthest<cell*cell)break;
        }
        if(cards.Count==0)throw new InvalidOperationException("叶片无法放入原枝条所在的树冠范围。");
    }

    public static void Build(StylizedTreeAuthoring t,List<STSPlacedCard> cards,STSAlphaMask mask)
    {
        if(t.canopyFullness<=0||!mask.HasVisible(t.leafCards.uv,t.leafCards.cutoff))return;
        var opaque=OpaqueSamples(mask,t.leafCards.uv,t.leafCards.cutoff);
        if(opaque.Count==0)return;
        int count=Mathf.Max(1,Mathf.RoundToInt(1400*Mathf.Clamp01(t.canopyFullness)));
        float baseSize=t.crownWidth*.14f*t.leafScale;
        bool frustum=Frustum(t);
        float bottom=Bottom(t),depth=Top(t)-bottom;
        RootCards(t,cards,opaque,count,baseSize);
        int scaffold=0,stalled=0;
        for(int i=cards.Count;i<count;i++)
        {
            var rng=STSRandom.For(t.leafSeed,i,512);bool placed=false;
            for(int attempt=0;attempt<8&&!placed;attempt++)
            {
                // First reach the wide upper rim, then the narrow lower crown.
                // Keep a target until reached instead of sprinkling independent
                // samples and hoping a cone emerges from a round cloud.
                bool guided=scaffold<16&&attempt<5;
                float u,angle;
                if(frustum)
                {
                    angle=guided?(scaffold%12)*Mathf.PI*2/12+STSRandom.Range(rng,-.025f,.025f):i*2.39996323f+STSRandom.Range(rng,-.3f,.3f);
                    float upper=.81f+.055f*Mathf.Sin(angle*3+.7f)+.025f*Mathf.Sin(angle*5-1.1f);
                    u=guided?(scaffold<12?upper:.22f)+STSRandom.Range(rng,-.018f,.018f):.08f+.77f*Mathf.Sqrt(STSRandom.Range(rng,0,1));
                }
                else
                {
                    // Preserve the previous random sequence for other species.
                    u=guided?(scaffold<12?.91f:.2f)+STSRandom.Range(rng,-.018f,.018f):.08f+.85f*Mathf.Sqrt(STSRandom.Range(rng,0,1));
                    angle=guided?(scaffold%12)*Mathf.PI*2/12+STSRandom.Range(rng,-.025f,.025f):i*2.39996323f+STSRandom.Range(rng,-.3f,.3f);
                }
                float y=bottom+depth*u;
                float r=Radius(t,y)*(guided?(scaffold<12?.85f:.68f):Mathf.Sqrt(STSRandom.Range(rng,.05f,.81f)));
                // Unequal, rounded lobes soften the round frustum without a
                // perfect level roof or twelve identical radial sectors.
                if(frustum)r*=.92f+.065f*Mathf.Sin(3*angle+.35f)+.035f*Mathf.Sin(5*angle-1.1f);
                Vector3 radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)),canonical=Vector3.up*y+radial*r;
                Vector3 target=STSReferenceGrowth.LocalPoint(t,canonical);
                int parentIndex=0;float best=float.PositiveInfinity;
                for(int j=0;j<cards.Count;j++)
                {float d=(cards[j].center-target).sqrMagnitude;if(d<best){best=d;parentIndex=j;}}
                var parent=cards[parentIndex];Vector2 parentUV=ParentContact(parent,target,opaque,i+attempt);
                Vector3 contact=parent.Point(parentUV.x,parentUV.y);
                Vector3 normal=STSReferenceGrowth.LocalNormal(t,canonical,(Vector3.up*.82f+radial*.52f).normalized);
                Vector3 toward=(target-contact).normalized;
                Vector3 growthNormal=Vector3.ProjectOnPlane(normal,toward).normalized;
                if(growthNormal.sqrMagnitude>.01f)normal=growthNormal;
                if(normal.y<0)normal=-normal;
                Vector3 up=STSReferenceGrowth.LocalPoint(t,canonical+radial)-target;
                up=Quaternion.AngleAxis(STSRandom.Range(rng,-28,28),normal)*up;
                float size=baseSize*STSRandom.Range(rng,.86f,1.1f);
                var card=new STSPlacedCard(contact,normal,up,new Vector2(size,size),t.leafCards.uv);
                Vector2 ownUV=OwnContact(card,target-contact,opaque,i+attempt);
                Anchor(card,contact,ownUV);
                while(!Fits(t,card)&&card.size.x>size*.34f)
                {card.size*=.82f;Anchor(card,contact,ownUV);}
                if(!Fits(t,card)||card.size.x<size*.28f)continue;
                card.referenceLeaf=true;card.referenceParent=parentIndex;card.referenceOwnContact=ownUV;card.referenceParentContact=parentUV;
                card.pivot=parent.pivot;card.wind=parent.wind;card.color=t.leafCards.tint*STSRandom.Range(rng,.97f,1.03f);card.color.a=1;
                Vector3 q=STSReferenceGrowth.Unwarp(t,card.center);
                card.clusterKey=parent.clusterKey;card.crownCap=q.y>bottom+depth*.85f;
                cards.Add(card);placed=true;
                if(guided&&Mathf.Min((card.center-target).sqrMagnitude,(contact-target).sqrMagnitude)<baseSize*baseSize*.55f)
                {scaffold++;stalled=0;}
            }
            // A blocked sample is skipped. Never add an unconnected fallback
            // or duplicate a coincident plane just to inflate the card count.
            if(++stalled>64){scaffold++;stalled=0;}
        }
        // Preserve crown silhouette at all LODs. Every retained leaf also retains
        // its ancestral support chain, so thinning cannot strand outside cards.
        var boundary=new Dictionary<int,int>();var radii=new Dictionary<int,float>();
        for(int i=0;i<cards.Count;i++)
        {
            Vector3 q=STSReferenceGrowth.Unwarp(t,cards[i].center);float radius=q.x*q.x+q.z*q.z;
            int heightBand=Mathf.Clamp(Mathf.FloorToInt((q.y-bottom)/depth*3),0,2);
            int directionBand=Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(q.z,q.x)+Mathf.PI)/(2*Mathf.PI)*8),0,7);
            int key=heightBand*8+directionBand;
            float previous;if(!radii.TryGetValue(key,out previous)||radius>previous){radii[key]=radius;boundary[key]=i;}
        }
        foreach(int index in boundary.Values)cards[index].referenceStructural=true;
        if(frustum)BranchLeaves(t,cards,opaque,baseSize);
    }

    static void BranchLeaves(StylizedTreeAuthoring t,List<STSPlacedCard> cards,List<Vector2> opaque,float baseSize)
    {
        var candidates=new List<Vector3>();var cells=new HashSet<Vector3Int>();
        float cell=Mathf.Max(.001f,t.crownWidth*.045f),bottom=Bottom(t),depth=Top(t)-bottom;
        foreach(Vector3 vertex in t.referenceTrunk.barkLODs[0].vertices)
        {
            Vector3 p=STSReferenceGrowth.CanonicalPoint(t,vertex);
            if(p.y<t.height*.25f||p.y>bottom+depth*.17f||p.x*p.x+p.z*p.z<t.crownWidth*t.crownWidth*.008f)continue;
            var key=new Vector3Int(Mathf.RoundToInt(p.x/cell),Mathf.RoundToInt(p.y/cell),Mathf.RoundToInt(p.z/cell));
            if(cells.Add(key))candidates.Add(p);
        }
        if(candidates.Count==0)return;
        var distance=new float[candidates.Count];for(int i=0;i<distance.Length;i++)distance[i]=float.PositiveInfinity;
        int next=STSRandom.For(t.leafSeed,0,514).Next(candidates.Count);
        int count=Mathf.Min(candidates.Count,Mathf.RoundToInt(22*Mathf.Clamp01(t.canopyFullness)));
        for(int i=0;i<count;i++)
        {
            Vector3 p=candidates[next],contact=STSReferenceGrowth.LocalPoint(t,p);
            var rng=STSRandom.For(t.leafSeed,i,514);Vector3 radial=new Vector3(p.x,0,p.z).normalized;
            Vector3 normal=STSReferenceGrowth.LocalNormal(t,p,(Vector3.up+radial*.6f).normalized);
            Vector3 up=Quaternion.AngleAxis(STSRandom.Range(rng,-180,180),normal)*Vector3.forward;
            float size=baseSize*STSRandom.Range(rng,.32f,.46f);Vector2 uv=opaque[rng.Next(opaque.Count)];
            var card=new STSPlacedCard(contact,normal,up,new Vector2(size,size),t.leafCards.uv);
            Anchor(card,contact,uv);card.referenceLeaf=true;card.referenceScatter=true;card.referenceStructural=true;
            card.referenceOwnContact=uv;card.contact=contact;card.pivot=contact;card.wind=Mathf.Clamp01(p.y/t.height);
            card.clusterKey=-5140-i;card.color=t.leafCards.tint;card.color.a=1;cards.Add(card);
            float farthest=0;
            for(int j=0;j<candidates.Count;j++)
            {distance[j]=Mathf.Min(distance[j],(candidates[j]-p).sqrMagnitude);if(distance[j]>farthest){farthest=distance[j];next=j;}}
            if(farthest<cell*cell)break;
        }
    }

    public static void KeepWithParents(List<STSPlacedCard> cards,HashSet<int> keep,int index)
    {
        while(index>=0&&index<cards.Count&&keep.Add(index))index=cards[index].referenceParent;
    }
    public static HashSet<int> LeafSet(StylizedTreeAuthoring t,List<STSPlacedCard> cards,int lod)
    {
        var keep=new HashSet<int>();float density=lod==0?1:lod==1?t.lod1Density:t.lod2Density;
        for(int i=0;i<cards.Count;i++)
        {
            float sample=(STSRandom.Hash(512,i,17)&0x7FFFFFFF)/(float)int.MaxValue;
            if(lod==0||i==0||cards[i].referenceStructural||sample<density)KeepWithParents(cards,keep,i);
        }
        return keep;
    }
}
#endif
