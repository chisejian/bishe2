#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

internal class STSOrganContext
{
    public readonly StylizedTreeAuthoring t;
    public readonly int lod;
    public readonly STSMeshData bark=new STSMeshData(), leaves=new STSMeshData(), flowers=new STSMeshData(), centers=new STSMeshData(), fruit=new STSMeshData();
    public readonly List<STSPlantAttachment> prefabFlowers=new List<STSPlantAttachment>();
    public readonly Dictionary<int,List<Vector3>> paths=new Dictionary<int,List<Vector3>>();
    readonly List<StylizedTreeBranch> eligible=new List<StylizedTreeBranch>(), terminal=new List<StylizedTreeBranch>();
    public float Density {get{return lod==0?1:lod==1?t.lod1Density:t.lod2Density;}}
    public STSOrganContext(StylizedTreeAuthoring t,int lod)
    {
        this.t=t;this.lod=lod;var parents=new HashSet<int>();
        foreach(var b in t.branches)if(b.parentId>=0)parents.Add(b.parentId);
        foreach(var b in t.branches)
        {
            if(b.points.Count<2)continue;paths[b.id]=STSPath.Shape(b);
            if(!b.isFrond)eligible.Add(b);
            if(!b.isFrond&&!parents.Contains(b.id))terminal.Add(b);
        }
        if(terminal.Count==0)terminal.AddRange(eligible);
    }
    public STSFrame Frame(STSPlantAttachment a)
    {
        List<Vector3> pts;if(!paths.TryGetValue(a.branchId,out pts))return new STSFrame(Vector3.zero,Vector3.up,Vector3.right);
        STSFrame f=STSPath.Evaluate(pts,a.t);f.p+=f.Offset(a.offset);return f;
    }
    public STSPlantAttachment RandomAnchor(STSOrganKind kind,int index,int seed,float bias)
    {
        var rng=STSRandom.For(seed,index,17);
        var source=rng.NextDouble()<bias?terminal:eligible;
        if(source.Count==0)return null;
        var b=source[rng.Next(source.Count)];
        if(kind==STSOrganKind.Leaf&&(t.growthForm==STSGrowthForm.Vine||t.growthForm==STSGrowthForm.ClimbingFern)&&index%3==0)
        {
            var rachis=eligible.Find(x=>x.isTrunk);if(rachis!=null)b=rachis;
        }
        float u=(float)rng.NextDouble();float at=Mathf.Lerp(.08f,.99f,Mathf.Pow(u,Mathf.Lerp(1,.13f,bias)));
        return new STSPlantAttachment{kind=kind,branchId=b.id,t=at,rotation=STSRandom.Range(rng,0,360)};
    }
    public void Build()
    {
        foreach(var b in t.branches)
        {
            List<Vector3> pts;if(!paths.TryGetValue(b.id,out pts))continue;
            int sides=Mathf.Max(3,t.radialSides-lod*2);
            if(b.isFrond) {leaves.Tube(pts,b.radius,sides,t.leafTint);PalmFrond(b,pts);continue;}
            Func<float,float> radius=b.isTrunk?(Func<float,float>)(u=>Mathf.Max(.015f,t.radiusProfile.Evaluate(u))):(u=>Mathf.Lerp(1,t.branchTipRatio,u));
            if(t.growthForm==STSGrowthForm.Palm&&b.isTrunk)
                radius=u=>Mathf.Max(.015f,t.radiusProfile.Evaluate(u))*(1+.025f*Mathf.Cos(u*t.height*18));
            bark.Tube(pts,b.radius,sides,t.barkTint,radius);
            if(t.growthForm==STSGrowthForm.Palm&&b.isTrunk)
            {
                Vector3 a=STSPath.Evaluate(pts,.86f).p,c=STSPath.Evaluate(pts,1).p;
                leaves.Tube(new List<Vector3>{a,c},b.radius*.79f,Mathf.Max(5,sides),t.leafTint,u=>1);
            }
        }
        if(t.growthForm!=STSGrowthForm.Palm)
        {
            int count=Mathf.CeilToInt(t.leafClusters*Density);
            for(int i=0;i<count;i++){var a=RandomAnchor(STSOrganKind.Leaf,i,t.leafSeed,t.leafTipBias);if(a!=null)LeafAt(a,i);}
        }
        if(t.enableFlowers&&t.growthForm!=STSGrowthForm.ClimbingFern)
        {
            int count=Mathf.CeilToInt(t.randomFlowers*Density);
            for(int i=0;i<count;i++){var a=RandomAnchor(STSOrganKind.Flower,i,t.flowerSeed,t.flowerTipBias);if(a!=null)FlowerAt(a,i);}
        }
        if(t.enableFruit&&t.growthForm!=STSGrowthForm.ClimbingFern)
        {
            int count=Mathf.CeilToInt(t.fruitCount*Density);
            for(int i=0;i<count;i++){var a=RandomAnchor(STSOrganKind.Fruit,i,t.fruitSeed,.35f);if(a!=null)FruitAt(a,i);}
        }
        // Painted organs survive density reduction; artists can rely on their focal details.
        for(int i=0;i<t.attachments.Count;i++)
        {
            var a=t.attachments[i];if(a==null||!paths.ContainsKey(a.branchId))continue;
            if(a.kind==STSOrganKind.Leaf)LeafAt(a,10000+i);
            else if(a.kind==STSOrganKind.Flower&&t.enableFlowers&&t.growthForm!=STSGrowthForm.ClimbingFern)FlowerAt(a,10000+i);
            else if(a.kind==STSOrganKind.Fruit&&t.enableFruit&&t.growthForm!=STSGrowthForm.ClimbingFern)FruitAt(a,10000+i);
        }
    }
    Color LeafColor(System.Random rng)
    {Color c=t.leafTint*STSRandom.Range(rng,1-t.leafColorVariation,1+t.leafColorVariation);c.a=1;return c;}
    void LeafAt(STSPlantAttachment a,int index)
    {
        STSFrame frame=Frame(a);var rng=STSRandom.For(t.leafSeed,index,51);
        if(t.leafShape==STSLeafShape.Fern) {Fern(frame,a,LeafColor(rng));return;}
        int count=Mathf.Clamp(t.leavesPerNode,1,8);
        for(int j=0;j<count;j++)
        {
            float angle=(a.rotation+j*360f/count+STSRandom.Range(rng,-20,20))*Mathf.Deg2Rad;
            Vector3 radial=frame.right*Mathf.Cos(angle)+frame.up*Mathf.Sin(angle);
            Vector3 dir=(radial*.8f+frame.tangent*.35f+Vector3.up*.25f).normalized;
            float size=t.leafSize*a.scale*STSRandom.Range(rng,.75f,1.2f);
            Vector3 basePos=frame.p+radial*.015f,stemEnd=basePos+dir*size*.16f;
            if(lod<2)leaves.Rod(basePos,stemEnd,size*.012f,LeafColor(rng),3);
            if(t.leafShape==STSLeafShape.ThreeLobed)
            {
                Vector3 side=Vector3.Cross(dir,Vector3.up).normalized;if(side.sqrMagnitude<.01f)side=frame.right;
                Blade(stemEnd,dir,size,t.leafWidth*.5f,LeafColor(rng),STSLeafShape.Lanceolate);
                Blade(stemEnd+dir*size*.12f,(dir*.55f+side*.75f).normalized,size*.7f,t.leafWidth*.5f,LeafColor(rng),STSLeafShape.Lanceolate);
                Blade(stemEnd+dir*size*.12f,(dir*.55f-side*.75f).normalized,size*.7f,t.leafWidth*.5f,LeafColor(rng),STSLeafShape.Lanceolate);
            }
            else Blade(stemEnd,dir,size,t.leafWidth,LeafColor(rng),t.leafShape);
        }
        if(t.enableTendrils&&t.growthForm==STSGrowthForm.Vine&&index%3==0&&lod==0)
        {
            var points=new List<Vector3>();
            for(int k=0;k<=14;k++){float f=k/14f,angle=f*2*Mathf.PI*1.4f;points.Add(frame.p+frame.right*t.leafSize*(f*.7f+Mathf.Sin(angle)*.14f*f)+frame.up*Mathf.Cos(angle)*t.leafSize*.14f*f+frame.tangent*t.leafSize*f*.5f);}
            leaves.Tube(points,t.trunkRadius*.12f,3,t.leafTint,u=>Mathf.Lerp(1,.25f,u));
        }
    }
    void Blade(Vector3 p,Vector3 dir,float length,float width,Color color,STSLeafShape shape,int segments=0)
    {
        dir.Normalize();Vector3 side=Vector3.Cross(dir,Vector3.up).normalized;
        if(side.sqrMagnitude<.01f)side=Vector3.right;
        Vector3 normal=Vector3.Cross(side,dir).normalized;
        int steps=segments>0?segments:lod==0?6:lod==1?4:2;
        Func<float,float> outline=q=>{
            float w=Mathf.Pow(Mathf.Max(0,Mathf.Sin(q*Mathf.PI)),shape==STSLeafShape.Lanceolate?.9f:.65f);
            if(shape==STSLeafShape.Serrated)w*=1+t.leafEdge*Mathf.Cos(q*Mathf.PI*steps);
            return Mathf.Max(.008f,w)*width*length*.5f;
        };
        for(int k=0;k<steps;k++)
        {
            float u=k/(float)steps,w=(k+1f)/steps;
            Vector3 a=p+dir*length*u+normal*(Mathf.Sin(u*Mathf.PI)*length*t.leafBend),b=p+dir*length*w+normal*(Mathf.Sin(w*Mathf.PI)*length*t.leafBend);
            float wa=outline(u),wb=outline(w);
            // Center ridge gives a folded leaf rather than a flat diamond.
            Vector3 ridgeA=a+normal*wa*.16f,ridgeB=b+normal*wb*.16f;
            leaves.Quad(a-side*wa,ridgeA,b-side*wb,ridgeB,color);
            leaves.Quad(ridgeA,a+side*wa,ridgeB,b+side*wb,color);
        }
    }
    void PalmFrond(StylizedTreeBranch branch,List<Vector3> pts)
    {
        int pairs=Mathf.Max(4,Mathf.RoundToInt(t.palmLeafletPairs*(lod==0?1:lod==1?.55f:.25f)));
        for(int i=0;i<pairs;i++)
        {
            float at=Mathf.Lerp(.08f,.98f,(i+.5f)/pairs);STSFrame f=STSPath.Evaluate(pts,at);
            Vector3 side=Vector3.Cross(f.tangent,Vector3.up).normalized;if(side.sqrMagnitude<.01f)side=f.right;
            float len=t.leafSize*Mathf.Pow(Mathf.Sin(at*Mathf.PI),.6f);
            foreach(int sign in new[]{-1,1})
            {
                Vector3 dir=(side*sign+f.tangent*.25f+Vector3.down*(.25f+at*.5f)).normalized;
                Blade(f.p,dir,len,t.leafWidth,t.leafTint,STSLeafShape.Lanceolate);
            }
        }
    }
    void Fern(STSFrame frame,STSPlantAttachment anchor,Color color)
    {
        Vector3 dir=(frame.up*.5f+frame.tangent*.25f+Vector3.up*.45f).normalized;
        Vector3 side=Vector3.Cross(dir,Vector3.up).normalized;if(side.sqrMagnitude<.01f)side=frame.right;
        float len=t.leafSize*anchor.scale;
        leaves.Rod(frame.p,frame.p+dir*len,len*.009f,color,3);
        int pairs=Mathf.Max(2,t.fernPinnaPairs-lod),sub=Mathf.Max(2,t.fernPinnulePairs-lod);
        for(int i=0;i<pairs;i++)
        {
            float u=(i+.5f)/(pairs+.3f);Vector3 basePos=frame.p+dir*len*u;
            float span=len*.45f*(1-u);
            foreach(int sign in new[]{-1,1})
            {
                Vector3 axis=(side*sign+dir*.3f).normalized;
                Vector3 across=Vector3.Cross(axis,Vector3.up).normalized;
                leaves.Rod(basePos,basePos+axis*span,len*.004f,color,3);
                for(int j=0;j<sub;j++)
                {
                    float v=(j+.4f)/sub;Vector3 p=basePos+axis*span*v;float leafLen=span*.42f*(1-v*.65f);
                    foreach(int hand in new[]{-1,1})
                    {
                        Vector3 d=(across*hand+axis*.45f).normalized;
                        Blade(p,d,leafLen,.42f,color,STSLeafShape.Lanceolate,2);
                        if(lod==0)foreach(int lobe in new[]{-1,1})Blade(p+d*leafLen*.28f,(d+axis*lobe*.7f).normalized,leafLen*.42f,.45f,color,STSLeafShape.Lanceolate,2);
                    }
                }
            }
        }
        Blade(frame.p+dir*len*.88f,dir,len*.2f,.4f,color,STSLeafShape.Lanceolate);
    }
    public Vector3 FlowerNormal(STSFrame f)
    {
        if(t.flowerFacing==STSFacing.Up)return Vector3.up;
        if(t.flowerFacing==STSFacing.Down)return Vector3.down;
        if(t.flowerFacing==STSFacing.AlongBranch)return f.tangent;
        Vector3 outwards=new Vector3(f.p.x,0,f.p.z);
        if(outwards.sqrMagnitude<.01f)outwards=f.right;
        return (outwards.normalized+Vector3.up*.2f).normalized;
    }
    void FlowerAt(STSPlantAttachment anchor,int index)
    {
        STSFrame frame=Frame(anchor);var rng=STSRandom.For(t.flowerSeed,index,77);
        int count=t.flowerGrouping==STSFlowerGrouping.Single?1:Mathf.Max(1,Mathf.RoundToInt(t.flowersPerCluster*(lod==2?.5f:1)));
        Vector3 axis=(frame.tangent*.35f+Vector3.up*.65f).normalized;
        Vector3 side=Vector3.Cross(axis,Vector3.forward).normalized;if(side.sqrMagnitude<.01f)side=Vector3.right;
        Vector3 other=Vector3.Cross(axis,side).normalized;
        if(count>1&&lod<2)leaves.Rod(frame.p,frame.p+axis*t.inflorescenceLength,t.trunkRadius*.06f,t.leafTint,3);
        for(int k=0;k<count;k++)
        {
            float f=count==1?0:(k+.5f)/count,angle=STSRandom.Range(rng,0,Mathf.PI*2);
            float spread=t.inflorescenceLength*(t.flowerGrouping==STSFlowerGrouping.Panicle?.42f*(1-f):.4f);
            Vector3 p=frame.p+(count==1?Vector3.zero:axis*t.inflorescenceLength*f+(side*Mathf.Cos(angle)+other*Mathf.Sin(angle))*spread);
            Vector3 normal=FlowerNormal(new STSFrame(p,frame.tangent,frame.right));
            Vector3 center=p+normal*t.flowerPedicel;
            float size=t.flowerSize*anchor.scale*STSRandom.Range(rng,1-t.flowerSizeVariation,1+t.flowerSizeVariation);
            if(lod<2&&t.flowerPedicel>.001f)leaves.Rod(p,center,Mathf.Max(.0005f,size*.035f),t.leafTint,3);
            if(t.flowerMode==STSFlowerMode.Prefab)
            {
                // Frame-relative offsets are evaluated again when actual Prefabs are placed.
                Vector3 delta=center-frame.p;
                prefabFlowers.Add(new STSPlantAttachment {kind=STSOrganKind.Flower,branchId=anchor.branchId,t=anchor.t,
                    offset=anchor.offset+new Vector3(Vector3.Dot(delta,frame.right),Vector3.Dot(delta,frame.up),Vector3.Dot(delta,frame.tangent)),rotation=anchor.rotation+k*31,scale=size});
            }
            else if(t.flowerMode==STSFlowerMode.TextureCards)Card(center,normal,size,anchor.rotation+k*31);
            else Flower(center,normal,size,anchor.rotation+k*31);
        }
    }
    void Card(Vector3 p,Vector3 normal,float size,float rotation)
    {
        Vector3 right=Vector3.Cross(normal,Vector3.up).normalized;if(right.sqrMagnitude<.01f)right=Vector3.right;
        right=Quaternion.AngleAxis(rotation,normal)*right;Vector3 up=Vector3.Cross(normal,right).normalized;
        int count=Mathf.Clamp(t.textureCardCrossCount,1,4);
        for(int i=0;i<count;i++)
        {
            Vector3 r=Quaternion.AngleAxis(i*180f/count,up)*right*size;Vector3 u=up*size;
            flowers.Quad(p-r-u,p+r-u,p-r+u,p+r+u,t.flowerTint);
        }
    }
    void Flower(Vector3 p,Vector3 normal,float size,float rotation)
    {
        Vector3 right=Vector3.Cross(normal,Vector3.up).normalized;if(right.sqrMagnitude<.01f)right=Vector3.right;
        right=Quaternion.AngleAxis(rotation,normal)*right;Vector3 up=Vector3.Cross(normal,right).normalized;
        int petals=Mathf.Clamp(t.petalCount,3,16);
        if(t.flowerShape==STSFlowerShape.Champaca)
        {
            for(int k=0;k<petals;k++)Petal(p+normal*(k%2)*size*.1f,normal,right,up,size*(k%2==0?1:.78f),2*Mathf.PI*k/petals,t.flowerTint,k,.25f);
            centers.Ellipsoid(p+normal*size*.18f,Vector3.one*size*.17f,t.flowerCenterTint,6,4);return;
        }
        if(t.flowerShape==STSFlowerShape.Bougainvillea)
        {
            // Three colored bracts plus three separate tubular true flowers.
            for(int k=0;k<3;k++)
            {
                float a=2*Mathf.PI*k/3;Vector3 d=right*Mathf.Cos(a)+up*Mathf.Sin(a);
                Petal(p+d*size*.12f,normal,right,up,size,a,t.flowerTint,k,1);
                Vector3 basePos=p+d*size*.13f,tip=basePos+normal*size*.5f;
                centers.Rod(basePos,tip,size*.045f,t.flowerCenterTint,4);
                centers.Ellipsoid(tip,Vector3.one*size*.085f,t.flowerCenterTint,5,3);
            }
            return;
        }
        for(int k=0;k<petals;k++)Petal(p,normal,right,up,size,2*Mathf.PI*k/petals,t.flowerTint,k,1);
        if(t.flowerShape==STSFlowerShape.PassionFlower)
        {
            for(int k=0;k<5;k++)Petal(p-normal*size*.03f,normal,right,up,size*.86f,2*Mathf.PI*(k+.5f)/5,t.flowerTint,k,.65f);
            int filaments=lod==0?28:lod==1?16:8;
            for(int k=0;k<filaments;k++)
            {
                float a=2*Mathf.PI*k/filaments;Vector3 d=right*Mathf.Cos(a)+up*Mathf.Sin(a),cross=Vector3.Cross(normal,d)*size*.012f;
                Vector3 inner=p+d*size*.18f+normal*size*.12f,mid=p+d*size*.43f+normal*size*.14f,tip=p+d*size*.8f+normal*size*.22f;
                flowers.Quad(inner-cross,inner+cross,mid-cross,mid+cross,t.flowerCenterTint);
                flowers.Quad(mid-cross,mid+cross,tip-cross,tip+cross,t.flowerTint);
            }
            Vector3 head=p+normal*size*.42f;centers.Rod(p,head,size*.055f,new Color(.58f,.68f,.27f),5);
            for(int k=0;k<3;k++){float a=2*Mathf.PI*k/3;Vector3 tip=head+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*size*.2f+normal*size*.1f;centers.Rod(head,tip,size*.025f,t.flowerCenterTint,3);centers.Ellipsoid(tip,Vector3.one*size*.065f,t.flowerCenterTint,5,3);}
            for(int k=0;k<5;k++){float a=2*Mathf.PI*k/5;Vector3 tip=head+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*size*.33f;centers.Rod(head,tip,size*.018f,t.flowerCenterTint,3);centers.Ellipsoid(tip,Vector3.one*size*.06f,new Color(.86f,.8f,.4f),5,3);}
        }
        else if(t.flowerShape==STSFlowerShape.Hibiscus)
        {
            Vector3 end=p+normal*size*1.25f;centers.Rod(p,end,size*.045f,t.flowerTint,5);
            for(int k=0;k<(lod==0?15:6);k++){float a=k*2.39996f;Vector3 d=right*Mathf.Cos(a)+up*Mathf.Sin(a);Vector3 dot=p+normal*size*(.7f+(k%5)*.09f)+d*size*.12f;centers.Rod(dot-d*size*.1f,dot,size*.012f,t.flowerTint,3);centers.Ellipsoid(dot,Vector3.one*size*.035f,t.flowerCenterTint,5,3);}
            for(int k=0;k<5;k++){float a=2*Mathf.PI*k/5;centers.Ellipsoid(end+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*size*.07f,Vector3.one*size*.045f,t.flowerTint,5,3);}
        }
        else if(t.flowerCenterSize>.001f)
            centers.Ellipsoid(p+normal*size*.1f,Vector3.one*size*t.flowerCenterSize,t.flowerCenterTint,lod==0?8:5,3);
    }
    void Petal(Vector3 center,Vector3 normal,Vector3 right,Vector3 up,float size,float angle,Color color,int index,float widthFactor)
    {
        Vector3 forward=right*Mathf.Cos(angle)+up*Mathf.Sin(angle),side=-right*Mathf.Sin(angle)+up*Mathf.Cos(angle);
        int rows=Mathf.Max(2,t.petalSegments-lod),columns=Mathf.Max(1,t.petalColumns-lod);
        Func<float,float,Vector3> point=(q,x)=>{
            float contour=Mathf.Max(.005f,t.petalOutline.Evaluate(q))*t.petalWidth*widthFactor;
            float twist=Mathf.Sin(q*Mathf.PI)*t.petalTwist*Mathf.Deg2Rad;
            Vector3 localSide=side*Mathf.Cos(twist)+normal*Mathf.Sin(twist);
            // Tangential sweep and edge lift create real overlapping geometry.
            float sweep=t.petalOverlap*q*q*size*.7f;
            float ruffle=t.petalRuffle*size*Mathf.Pow(Mathf.Abs(x),3)*Mathf.Sin(q*Mathf.PI*(7+index%3)+index)*q;
            return center+forward*q*size*t.petalLength+localSide*x*contour*size*.48f+side*sweep
                +normal*(t.petalCup*size*t.petalBendProfile.Evaluate(q)+(1-x*x)*.045f*size+t.petalOverlap*x*q*size*.08f+ruffle);
        };
        Func<float,Color> tint=q=>t.flowerShape==STSFlowerShape.Plumeria?Color.Lerp(t.flowerCenterTint,color,Mathf.SmoothStep(0,1,q*2.5f)):color;
        float thickness=t.petalThickness*size;
        for(int r=0;r<rows;r++)for(int c=0;c<columns;c++)
        {
            float q=r/(float)rows,w=(r+1f)/rows,x=c*2f/columns-1,z=(c+1f)*2/columns-1;
            Vector3 a=point(q,x),b=point(q,z),d=point(w,x),e=point(w,z);
            if(thickness<=.00001f)flowers.Quad(a,b,d,e,tint(q),tint(q),tint(w),tint(w));
            else
            {
                Vector3 h=normal*thickness*.5f;
                flowers.Quad(b+h,a+h,e+h,d+h,tint(q),tint(q),tint(w),tint(w),false);
                flowers.Quad(a-h,b-h,d-h,e-h,tint(q),tint(q),tint(w),tint(w),false);
                if(c==0)flowers.Quad(a-h,a+h,d-h,d+h,tint(q));
                if(c==columns-1)flowers.Quad(b+h,b-h,e+h,e-h,tint(q));
                if(r==rows-1)flowers.Quad(d+h,e+h,d-h,e-h,tint(w));
                if(r==0)flowers.Quad(b+h,a+h,b-h,a-h,tint(q));
            }
        }
    }
    void FruitAt(STSPlantAttachment a,int index)
    {
        STSFrame frame=Frame(a);var rng=STSRandom.For(t.fruitSeed,index,123);
        Vector3 down=t.transform.InverseTransformDirection(Vector3.down).normalized;
        float diameter=t.fruitSize*a.scale*STSRandom.Range(rng,1-t.fruitSizeVariation,1+t.fruitSizeVariation),radius=diameter*.5f;
        Vector3 end=frame.p+down*t.fruitStalk,center=end+down*radius;
        leaves.Rod(frame.p,end,Mathf.Max(.0006f,diameter*.017f),t.leafTint,4);
        Color color=Color.Lerp(t.fruitYoungTint,t.fruitRipeTint,Mathf.Clamp01(t.fruitRipeness+STSRandom.Range(rng,-.18f,.18f)));
        fruit.Ellipsoid(center,new Vector3(radius,radius*(1+t.fruitPearShape*.22f),radius),color,lod==0?12:lod==1?8:6,lod==0?8:4,t.fruitPearShape);
    }
}
#endif
