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
        // A stable string avoids culture-dependent float formatting and excludes organ seeds.
        return string.Join("|",t.height.ToString("R",System.Globalization.CultureInfo.InvariantCulture),t.crownWidth.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
            t.trunkRadius.ToString("R",System.Globalization.CultureInfo.InvariantCulture),t.branchCount,t.branchLevels,t.secondaryBranches,t.baseStemCount,t.seed,
            t.crownStart,t.branchRise,t.branchDroop,t.irregularity,t.trunkBend,t.branchTipRatio,(int)t.growthForm,(int)t.species,(int)t.vineLayout,
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
        t.branches.Clear();t.nextBranchId=1;
        if(t.growthForm==STSGrowthForm.Vine||t.growthForm==STSGrowthForm.ClimbingFern) Vine(t);
        else if(t.growthForm==STSGrowthForm.Palm) Palm(t);
        else Broadleaf(t);
        t.generatedBranches=true;t.dataVersion=3;t.growthSignature=Signature(t);
        // Regeneration reuses stable IDs. Remove only anchors whose branch truly disappeared.
        var ids=new HashSet<int>();foreach(var b in t.branches)ids.Add(b.id);
        t.attachments.RemoveAll(a=>a==null||!ids.Contains(a.branchId));
    }
    static void Broadleaf(StylizedTreeAuthoring t)
    {
        int stems=t.growthForm==STSGrowthForm.Shrub?Mathf.Clamp(t.baseStemCount,1,8):1;
        var roots=new List<StylizedTreeBranch>();
        for(int s=0;s<stems;s++)
        {
            var rng=STSRandom.For(t.seed,s);float angle=2*Mathf.PI*s/stems;
            Vector3 lean=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(stems>1?t.crownWidth*.22f:t.height*t.trunkBend);
            float h=t.height*(s==0?1:STSRandom.Range(rng,.72f,1));var points=new List<Vector3>();
            for(int k=0;k<=5;k++) {float f=k/5f;points.Add(Vector3.up*h*f+lean*f*f+new Vector3(Mathf.Sin(f*5+s),0,Mathf.Cos(f*4+s))*t.trunkBend*t.height*.08f*f);}
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
        for(int k=0;k<=16;k++) {float f=k/16f;trunk.Add(new Vector3(Mathf.Sin(f*2)*t.trunkBend*t.height, f*t.height,0));}
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
