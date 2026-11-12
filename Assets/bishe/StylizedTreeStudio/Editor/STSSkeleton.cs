#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

internal static class STSRandom
{
    public static int Hash(int seed,int index,int salt=0)
    { unchecked {uint x=(uint)seed ^ ((uint)index*0x9e3779b9u) ^ ((uint)salt*0x85ebca6bu);x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;x^=x>>16;return (int)(x&0x7fffffff);} }
    public static System.Random For(int seed,int index,int salt=0) {return new System.Random(Hash(seed,index,salt));}
    public static float Range(System.Random r,float min,float max) {return min+(float)r.NextDouble()*(max-min);}
}

internal static class STSSkeleton
{
    public static string Signature(StylizedTreeAuthoring t)
    {
        if(t.useReferenceTrunk)
            return string.Join("|", "reference-trunk-v0514", t.referenceTrunk?t.referenceTrunk.revision:"missing",
                t.height.ToString("R",System.Globalization.CultureInfo.InvariantCulture),t.crownWidth.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                t.trunkTwist.ToString("R",System.Globalization.CultureInfo.InvariantCulture),t.referenceBranchStretch.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
        // A stable string avoids culture-dependent float formatting and excludes organ seeds.
        return string.Join("|","trunk-twist-v055",t.height.ToString("R",System.Globalization.CultureInfo.InvariantCulture),t.crownWidth.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
            t.trunkRadius.ToString("R",System.Globalization.CultureInfo.InvariantCulture),t.branchCount,t.branchLevels,t.secondaryBranches,t.baseStemCount,t.seed,
            t.crownStart,t.branchRise,t.branchDroop,t.irregularity,t.trunkBend,t.trunkTwist.ToString("R",System.Globalization.CultureInfo.InvariantCulture),t.branchTipRatio,(int)t.growthForm,(int)t.species,(int)t.vineLayout,(int)t.canopyMode,
            t.vineTurns,t.vineWrapRadius,t.vineSag,CurveKey(t.crownProfile),CurveKey(t.radiusProfile),PathKey(t.vinePath),
            t.projectionTarget?t.projectionTarget.GetInstanceID():0,t.projectionDirection.x,t.projectionDirection.y,t.projectionDirection.z,t.projectionDistance,t.surfaceOffset);
    }
    static string PathKey(List<Vector3> points)
    {
        string text="";if(points!=null)foreach(Vector3 p in points)text+=p.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+";";
        return text;
    }
    static string CurveKey(AnimationCurve curve)
    {
        string text="";if(curve!=null)foreach(Keyframe k in curve.keys)text+=k.time.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+k.value.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+k.inTangent+","+k.outTangent+";";
        return text;
    }
    static StylizedTreeBranch Add(StylizedTreeAuthoring t,List<Vector3> points,float radius,int parent=-1,float at=0,bool trunk=false,bool frond=false)
    {
        var b=new StylizedTreeBranch {id=t.nextBranchId++,points=points,radius=radius,parentId=parent,parentT=at,isTrunk=trunk,isFrond=frond};
        t.branches.Add(b);return b;
    }
    public static void Generate(StylizedTreeAuthoring t)
    {
        if(!STSPresets.IsSupported(t.species))throw new InvalidOperationException("旧物种已移除，保留原有枝条数据。");
        t.branches.Clear();t.nextBranchId=1;
        if(t.useReferenceTrunk){STSReferenceGrowth.Migrate(t);STSReferenceGrowth.Generate(t);}
        else if(t.growthForm==STSGrowthForm.Vine||t.growthForm==STSGrowthForm.ClimbingFern) Vine(t);
        else if(t.growthForm==STSGrowthForm.Palm) Palm(t);
        else Broadleaf(t);
        t.generatedBranches=true;t.dataVersion=Mathf.Max(3,t.dataVersion);t.growthSignature=Signature(t);
        // Regeneration reuses stable IDs. Remove only anchors whose branch truly disappeared.
        var ids=new HashSet<int>();foreach(var b in t.branches)ids.Add(b.id);
        t.attachments.RemoveAll(a=>a==null||!ids.Contains(a.branchId));
    }
    static int TrunkSegments(StylizedTreeAuthoring t,int original)
    {
        if(t.trunkTwist<=0)return original;
        return 24+Mathf.CeilToInt(Mathf.Max(0,t.trunkTwist-1)*12);
    }
    static void TwistTrunk(StylizedTreeAuthoring t,List<Vector3> points,int stem=0)
    {
        if(t.trunkTwist<=0||points.Count<2)return;
        var rng=STSRandom.For(t.seed,stem,255);
        float phase=STSRandom.Range(rng,0,Mathf.PI*2);
        float amplitude=(points[points.Count-1]-points[0]).magnitude*.085f*t.trunkTwist;
        // Pin both ends and fade the deformation tangent to zero there, so the
        // root stays planted and the crown does not drift away from the trunk.
        for(int k=1;k<points.Count-1;k++)
        {
            float u=k/(float)(points.Count-1),fade=Mathf.Sin(u*Mathf.PI);
            float angle=phase+u*Mathf.PI*3;
            points[k]+=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(amplitude*fade*fade);
        }
    }
    static void Broadleaf(StylizedTreeAuthoring t)
    {
        if(t.canopyMode==STSCanopyMode.LayeredClusters&&t.growthForm==STSGrowthForm.Tree){DenseTree(t);return;}
        int stems=t.growthForm==STSGrowthForm.Shrub?Mathf.Clamp(t.baseStemCount,1,8):1;
        var roots=new List<StylizedTreeBranch>();
        for(int s=0;s<stems;s++)
        {
            var rng=STSRandom.For(t.seed,s);float angle=2*Mathf.PI*s/stems;
            Vector3 lean=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(stems>1?t.crownWidth*.22f:t.height*t.trunkBend);
            float h=t.height*(s==0?1:STSRandom.Range(rng,.72f,1));var points=new List<Vector3>();
            int segments=TrunkSegments(t,5);
            for(int k=0;k<=segments;k++) {float f=k/(float)segments;points.Add(Vector3.up*h*f+lean*f*f+new Vector3(Mathf.Sin(f*5+s),0,Mathf.Cos(f*4+s))*t.trunkBend*t.height*.08f*f);}
            TwistTrunk(t,points,s);
            roots.Add(Add(t,points,t.trunkRadius*(stems>1?.7f:1),trunk:true));
        }
        var current=new List<StylizedTreeBranch>();
        for(int i=0;i<t.branchCount;i++)
        {
            var rng=STSRandom.For(t.seed,i,17);var parent=roots[i%roots.Count];
            float at=Mathf.Lerp(t.crownStart,.91f,(i+.5f)/Mathf.Max(1,t.branchCount));
            float profile=t.crownProfile==null?1:Mathf.Max(.05f,t.crownProfile.Evaluate((at-t.crownStart)/Mathf.Max(.05f,1-t.crownStart)));
            float length=t.crownWidth*.5f*profile*STSRandom.Range(rng,.8f,1.12f);
            current.Add(Shoot(t,parent,at,i*2.39996323f+STSRandom.Range(rng,-.3f,.3f),length,t.branchRise,t.trunkRadius*.36f,rng));
        }
        for(int level=1;level<t.branchLevels;level++)
        {
            var next=new List<StylizedTreeBranch>();
            foreach(var parent in current)for(int i=0;i<t.secondaryBranches;i++)
            {
                var rng=STSRandom.For(t.seed,parent.id*13+i,level);
                bool plumeria=t.species==STSPlantSpecies.Plumeria;
                float at=plumeria?STSRandom.Range(rng,.8f,.96f):Mathf.Lerp(.42f,.9f,(i+.5f)/t.secondaryBranches);
                float length=STSPath.Length(parent.points)*STSRandom.Range(rng,.42f,.65f);
                Vector3 outward=STSPath.OnBranch(parent,at).tangent;
                float angle=Mathf.Atan2(outward.z,outward.x)+(i%2==0?-1:1)*STSRandom.Range(rng,.5f,1.2f);
                next.Add(Shoot(t,parent,at,angle,length,t.branchRise+.15f,parent.radius*.43f,rng));
            }
            current=next;
        }

    }
    static void DenseTree(StylizedTreeAuthoring t)
    {
        var points=new List<Vector3>();
        int segments=TrunkSegments(t,8);
        for(int k=0;k<=segments;k++)
        {
            float u=k/(float)segments;
            points.Add(new Vector3(Mathf.Sin(u*3)*t.trunkBend*t.height*.3f,t.height*u,Mathf.Sin(u*4+1)*t.trunkBend*t.height*.18f*u));
        }
        TwistTrunk(t,points);
        var trunk=Add(t,points,t.trunkRadius,trunk:true);var current=new List<StylizedTreeBranch>();
        for(int i=0;i<t.branchCount;i++)
        {
            var rng=STSRandom.For(t.seed,i,217);float tier=(i+.5f)/t.branchCount;
            float at=Mathf.Lerp(.25f,.83f,tier),angle=i*2.39996323f+STSRandom.Range(rng,-.18f,.18f);
            Vector3 start=STSPath.OnBranch(trunk,at).p;
            float spread=t.crownWidth*.5f*Mathf.Sqrt(Mathf.Max(.2f,1-Mathf.Pow((tier-.42f)*1.1f,2)))*STSRandom.Range(rng,.78f,1.02f);
            if(t.crownProfile!=null)spread*=Mathf.Lerp(.8f,1.12f,Mathf.Clamp01(t.crownProfile.Evaluate(tier)));
            Vector3 end=new Vector3(Mathf.Cos(angle)*spread,t.height*(Mathf.Lerp(.46f,.9f,tier)+STSRandom.Range(rng,-.025f,.025f)),Mathf.Sin(angle)*spread*.92f);
            current.Add(DenseShoot(t,trunk,at,start,end,t.trunkRadius*.42f,rng));
        }
        for(int level=1;level<t.branchLevels;level++)
        {
            var next=new List<StylizedTreeBranch>();int children=level==1?t.secondaryBranches:Mathf.Min(2,t.secondaryBranches);
            foreach(var parent in current)for(int i=0;i<children;i++)
            {
                if(t.branches.Count>=900)break;
                var rng=STSRandom.For(t.seed,parent.id*11+i,level+233);
                float at=Mathf.Lerp(.48f,.96f,(i+.5f)/children);STSFrame frame=STSPath.OnBranch(parent,at);
                float angle=Mathf.Atan2(frame.tangent.z,frame.tangent.x)+(i%2==0?-1:1)*STSRandom.Range(rng,.7f,1.65f);
                float length=STSPath.Length(parent.points)*STSRandom.Range(rng,.38f,.62f);
                Vector3 d=new Vector3(Mathf.Cos(angle),STSRandom.Range(rng,-.18f,.5f),Mathf.Sin(angle)).normalized;
                Vector3 end=frame.p+d*length;
                Vector3 center=Vector3.up*t.height*.7f,delta=end-center;
                float envelope=Mathf.Sqrt(delta.x*delta.x/(t.crownWidth*t.crownWidth*.3f)+delta.z*delta.z/(t.crownWidth*t.crownWidth*.27f)+delta.y*delta.y/(t.height*t.height*.105f));
                if(envelope>1)end=center+delta/envelope;
                next.Add(DenseShoot(t,parent,at,frame.p,end,parent.radius*.5f,rng));
            }
            current=next;
        }
        // Short upper shoots connect the formerly bare central crown to the trunk.
        // Append after existing tiers so their stable branch IDs are preserved.
        Vector3 top=trunk.points[trunk.points.Count-1];int upperCount=Mathf.Clamp(t.branchCount/2,5,10);
        for(int i=0;i<upperCount;i++)
        {
            var rng=STSRandom.For(t.seed,i,251);float u=(i+.5f)/upperCount;
            float at=Mathf.Lerp(.8f,.96f,u),angle=i*2.39996323f+.7f;
            float reach=t.crownWidth*Mathf.Lerp(.07f,.25f,Mathf.Sqrt(u));
            Vector3 end=top+new Vector3(Mathf.Cos(angle)*reach,-t.height*(.025f+.025f*u),Mathf.Sin(angle)*reach);
            DenseShoot(t,trunk,at,STSPath.OnBranch(trunk,at).p,end,t.trunkRadius*.14f,rng);
        }
    }
    static StylizedTreeBranch DenseShoot(StylizedTreeAuthoring t,StylizedTreeBranch parent,float at,Vector3 start,Vector3 end,float radius,System.Random rng)
    {
        var points=new List<Vector3>();Vector3 side=Vector3.Cross(end-start,Vector3.up).normalized;
        float wobble=STSRandom.Range(rng,-1,1)*t.irregularity;
        for(int k=0;k<=3;k++)
        {
            float f=k/3f;
            points.Add(Vector3.Lerp(start,end,f)+Vector3.up*Mathf.Sin(f*Mathf.PI)*(end-start).magnitude*.08f+side*Mathf.Sin(f*Mathf.PI)*wobble*.45f);
        }
        float profile=parent.isTrunk?Mathf.Max(.015f,t.radiusProfile.Evaluate(at)):Mathf.Lerp(1,t.branchTipRatio,at);
        return Add(t,points,Mathf.Max(.002f,Mathf.Min(radius,parent.radius*profile*.85f)),parent.id,at);
    }
    static StylizedTreeBranch Shoot(StylizedTreeAuthoring t,StylizedTreeBranch parent,float at,float angle,float length,float rise,float radius,System.Random rng)
    {
        Vector3 start=STSPath.OnBranch(parent,at).p;
        Vector3 direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));var points=new List<Vector3>();
        float wobble=STSRandom.Range(rng,-1,1)*t.irregularity;
        for(int k=0;k<=4;k++)
        {
            float f=k/4f;
            points.Add(start+direction*length*f+Vector3.up*length*(rise*f-t.branchDroop*f*f)+Vector3.Cross(direction,Vector3.up)*Mathf.Sin(f*Mathf.PI)*length*wobble*.2f);
        }
        float parentScale=parent.isTrunk?Mathf.Max(.015f,t.radiusProfile.Evaluate(at)):Mathf.Lerp(1,t.branchTipRatio,at);
        return Add(t,points,Mathf.Max(.0005f,Mathf.Min(radius,parent.radius*parentScale*.8f)),parent.id,at);
    }
    static void Palm(StylizedTreeAuthoring t)
    {
        var trunk=new List<Vector3>();
        int segments=TrunkSegments(t,16);
        for(int k=0;k<=segments;k++) {float f=k/(float)segments;trunk.Add(new Vector3(Mathf.Sin(f*2)*t.trunkBend*t.height, f*t.height,0));}
        TwistTrunk(t,trunk);
        var root=Add(t,trunk,t.trunkRadius,trunk:true);Vector3 top=trunk[trunk.Count-1];
        for(int i=0;i<t.branchCount;i++)
        {
            var rng=STSRandom.For(t.seed,i,81);float angle=i*2.39996323f;Vector3 dir=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            float spread=(i+.5f)/t.branchCount;
            float len=t.crownWidth*.5f*STSRandom.Range(rng,.85f,1.05f),rise=Mathf.Lerp(1.35f,.25f,spread),reach=Mathf.Lerp(.35f,1f,spread);
            var points=new List<Vector3>();for(int k=0;k<=7;k++){float f=k/7f;points.Add(top+dir*len*f*reach+Vector3.up*len*(rise*f-t.branchDroop*spread*f*f*1.5f));}
            Add(t,points,t.trunkRadius*.1f,root.id,1,frond:true);
        }
    }
    public static void DefaultVinePath(StylizedTreeAuthoring t)
    {
        t.vinePath.Clear();int count=t.vineLayout==STSVineLayout.WrapColumn?Mathf.Max(12,Mathf.CeilToInt(t.vineTurns*12)):8;
        for(int k=0;k<=count;k++)
        {
            float f=k/(float)count;Vector3 p;
            switch(t.vineLayout)
            {
                case STSVineLayout.WrapColumn:
                    float a=f*t.vineTurns*2*Mathf.PI;p=new Vector3(t.vineWrapRadius*(Mathf.Cos(a)-1),t.height*f,t.vineWrapRadius*Mathf.Sin(a));break;
                case STSVineLayout.Trellis:
                    p=f<.4f?new Vector3(0,t.height*f/.4f,0):new Vector3((f-.4f)/.6f*t.crownWidth,t.height-Mathf.Sin((f-.4f)/.6f*Mathf.PI)*t.vineSag,0);break;
                case STSVineLayout.Hanging:
                    p=new Vector3(t.crownWidth*f,t.height*(1-f*f),Mathf.Sin(f*3)*t.irregularity*.15f);break;
                default:p=new Vector3(Mathf.Sin(f*8)*t.crownWidth*.15f,t.height*f,Mathf.Sin(f*5)*t.irregularity*.15f);break;
            }
            t.vinePath.Add(p);
        }
    }
    static void Vine(StylizedTreeAuthoring t)
    {
        if(t.vinePath.Count<2||t.vineLayout!=STSVineLayout.FreeCurve)DefaultVinePath(t);
        var points=STSPath.Smooth(t.vinePath,t.vinePath.Count>20?2:5);
        Project(t,points);var root=Add(t,points,t.trunkRadius,trunk:true);
        for(int i=0;i<t.branchCount;i++)
        {
            var rng=STSRandom.For(t.seed,i,97);float at=STSRandom.Range(rng,.18f,.97f);STSFrame frame=STSPath.OnBranch(root,at);
            float angle=i*2.39996323f;Vector3 side=frame.right*Mathf.Cos(angle)+frame.up*Mathf.Sin(angle);
            float len=t.crownWidth*STSRandom.Range(rng,.16f,.38f);var sidePoints=new List<Vector3>();
            for(int k=0;k<=5;k++) {float f=k/5f;sidePoints.Add(frame.p+side*len*f+frame.tangent*len*f*.35f+Vector3.down*t.vineSag*len*f*f);}
            Project(t,sidePoints);Add(t,sidePoints,t.trunkRadius*.4f,root.id,at);
        }
    }
    public static void Project(StylizedTreeAuthoring t,List<Vector3> points)
    {
        if(!t.projectionTarget||t.projectionDirection.sqrMagnitude<1e-8f)return;
        Vector3 dir=t.transform.TransformDirection(t.projectionDirection).normalized;
        for(int i=0;i<points.Count;i++)
        {
            Vector3 world=t.transform.TransformPoint(points[i]);RaycastHit hit;
            if(t.projectionTarget.Raycast(new Ray(world-dir*t.projectionDistance,dir),out hit,t.projectionDistance*2))
                points[i]=t.transform.InverseTransformPoint(hit.point+hit.normal*t.surfaceOffset);
        }
    }
    public static void ReattachChildren(StylizedTreeAuthoring t,int parentId,HashSet<int> visited=null)
    {
        if(visited==null)visited=new HashSet<int>();if(!visited.Add(parentId))return;
        var parent=t.branches.Find(b=>b.id==parentId);if(parent==null)return;
        foreach(var b in t.branches)if(b.parentId==parentId&&b.points.Count>0)
        {
            Vector3 delta=STSPath.OnBranch(parent,b.parentT).p-b.points[0];
            for(int i=0;i<b.points.Count;i++)b.points[i]+=delta;
            ReattachChildren(t,b.id,visited);
        }
    }
}
#endif
