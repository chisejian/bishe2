#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

internal static class STSReferenceGrowth
{
    public static bool Active(StylizedTreeAuthoring t) { return t.useReferenceTrunk && t.referenceTrunk; }

    public static void Migrate(StylizedTreeAuthoring t)
    {
        if(!t.useReferenceTrunk)return;
        // Retire the older add-branch/bend experiment, including saved values.
        t.referenceBend=0;t.referenceBendDirection=0;t.referenceAddedBranches=0;
        if(t.referenceCanopyVersion<1)
        {t.referenceCrownStart=.48f;t.referenceCrownBaseRatio=.16f;}
        if(t.species==STSPlantSpecies.Plumeria&&t.referenceCanopyVersion<3)
        {
            t.referenceCrownBaseRatio=.62f;t.referenceCrownOffset=-.12f;
            t.referenceCanopyVersion=3;
        }
        else t.referenceCanopyVersion=Mathf.Max(2,t.referenceCanopyVersion);
    }

    public static void Validate(StylizedTreeAuthoring t)
    {
        if (!t.useReferenceTrunk) return;
        var source = t.referenceTrunk;
        if (!source || source.barkLODs == null || source.barkLODs.Length == 0 || !source.barkLODs[0] || !source.barkMaterial || source.guides == null || source.guides.Length == 0)
            throw new InvalidOperationException("现成主干副本不完整，请重新从参考预制体创建新植物。");
        if (t.growthForm != STSGrowthForm.Tree) throw new InvalidOperationException("现成主干模式用于乔木，王棕、灌木和藤本请使用原生成模式。");
        if (!STSPath.Finite(source.sourceHeight) || !STSPath.Finite(source.sourceWidth) || source.sourceHeight < .01f || source.sourceWidth < .01f)
            throw new InvalidOperationException("主干副本的基准尺寸无效。");
        float maxBase=t.species==STSPlantSpecies.Plumeria?.9f:.45f;
        if(!STSPath.Finite(t.referenceCrownStart)||t.referenceCrownStart<.3f||t.referenceCrownStart>.8f||!STSPath.Finite(t.referenceCrownBaseRatio)||t.referenceCrownBaseRatio<.08f||t.referenceCrownBaseRatio>maxBase)
            throw new InvalidOperationException("树冠底部比例无效。");
        if(!STSPath.Finite(t.referenceCrownOffset)||t.referenceCrownOffset<-.3f||t.referenceCrownOffset>.1f)
            throw new InvalidOperationException("树冠上下偏移须在树高的 -30%～10% 之间。");
        if(!STSPath.Finite(t.referenceBranchStretch)||t.referenceBranchStretch<.5f||t.referenceBranchStretch>2)
            throw new InvalidOperationException("枝条拉伸须在 0.5～2 倍之间。");
        foreach(var guide in source.guides)
            if(!STSPath.Finite(guide.start)||!STSPath.Finite(guide.end))throw new InvalidOperationException("主干副本包含无效的发叶位置。");
    }

    // Source geometry first scales to the requested dimensions, then receives
    // one shared deformation. No source Mesh or snapshot is modified.
    public static Vector3 Point(StylizedTreeAuthoring t, Vector3 sourcePoint)
    { return LocalPoint(t,CanonicalPoint(t,sourcePoint)); }

    public static Vector3 CanonicalPoint(StylizedTreeAuthoring t,Vector3 sourcePoint)
    {
        var source = t.referenceTrunk;
        Vector3 p = sourcePoint - source.root;
        float horizontal = t.crownWidth / source.sourceWidth;
        return new Vector3(p.x * horizontal, p.y * t.height / source.sourceHeight, p.z * horizontal);
    }

    static void Terms(StylizedTreeAuthoring t,float y,out Quaternion rotation,out Vector3 offset,out float angularDerivative,out Vector3 offsetDerivative)
    {
        float h=Mathf.Max(.01f,t.height),u=Mathf.Clamp01(y/h),s=Mathf.Sin(Mathf.PI*u),c=Mathf.Cos(Mathf.PI*u);
        float wave=Mathf.Sin(2*Mathf.PI*u),waveCos=Mathf.Cos(2*Mathf.PI*u);
        float twist=Mathf.Clamp(t.trunkTwist,0,StylizedTreeAuthoring.MaxTrunkTwist);
        rotation=Quaternion.AngleAxis(45*twist*s*s,Vector3.up);
        offset=Vector3.forward*(h*.055f*twist*wave*s*s);
        angularDerivative=45*Mathf.Deg2Rad*twist*2*Mathf.PI*s*c/h;
        offsetDerivative=Vector3.forward*(.055f*twist*(2*Mathf.PI*waveCos*s*s+wave*2*Mathf.PI*s*c));
        if(y<0||y>h){angularDerivative=0;offsetDerivative=Vector3.zero;}
    }

    // Smooth outward extension of the existing mesh, not extra generated limbs.
    // The same map is used by every bark LOD, foliage contact and guide.
    internal static float Stretch(StylizedTreeAuthoring t,float y,out float derivative)
    {
        float span=Mathf.Max(.01f,t.height*.48f);
        float u=Mathf.Clamp01((y-t.height*.24f)/span);
        float amount=t.referenceBranchStretch-1;
        derivative=amount*6*u*(1-u)/span;
        return 1+amount*u*u*(3-2*u);
    }

    public static Vector3 LocalPoint(StylizedTreeAuthoring t,Vector3 p)
    {
        Quaternion rotation;Vector3 offset,derivative;float angleDerivative;
        Terms(t,p.y,out rotation,out offset,out angleDerivative,out derivative);
        float stretchDerivative;float stretch=Stretch(t,p.y,out stretchDerivative);
        p.x*=stretch;p.z*=stretch;
        return rotation*p+offset;
    }

    public static Vector3 Unwarp(StylizedTreeAuthoring t,Vector3 p)
    {
        Quaternion rotation;Vector3 offset,derivative;float angleDerivative;
        Terms(t,p.y,out rotation,out offset,out angleDerivative,out derivative);
        Vector3 q=Quaternion.Inverse(rotation)*(p-offset);
        float stretchDerivative;float stretch=Stretch(t,p.y,out stretchDerivative);
        q.x/=stretch;q.z/=stretch;return q;
    }

    public static Vector3 LocalNormal(StylizedTreeAuthoring t,Vector3 p,Vector3 normal)
    {
        Quaternion rotation;Vector3 offset,derivative;float angleDerivative;
        Terms(t,p.y,out rotation,out offset,out angleDerivative,out derivative);
        float stretchDerivative;float stretch=Stretch(t,p.y,out stretchDerivative);
        Vector3 n=rotation*new Vector3(normal.x/stretch,normal.y,normal.z/stretch);
        Vector3 radial=rotation*new Vector3(p.x,0,p.z);
        Vector3 shear=derivative+radial*stretchDerivative+Vector3.Cross(Vector3.up,radial)*(stretch*angleDerivative);
        n.y-=n.x*shear.x+n.z*shear.z;
        return n.normalized;
    }

    public static Vector3 Normal(StylizedTreeAuthoring t, Vector3 sourcePoint, Vector3 sourceNormal)
    {
        var source = t.referenceTrunk;
        float horizontal = t.crownWidth / source.sourceWidth, vertical = t.height / source.sourceHeight;
        Vector3 n = new Vector3(sourceNormal.x / horizontal, sourceNormal.y / vertical, sourceNormal.z / horizontal);
        Vector3 p=sourcePoint-source.root;p=new Vector3(p.x*horizontal,p.y*vertical,p.z*horizontal);
        return LocalNormal(t,p,n);
    }

    static StylizedTreeBranch Add(StylizedTreeAuthoring t, List<Vector3> points, float radius, int parent, bool trunk = false)
    {
        var b = new StylizedTreeBranch { id = t.nextBranchId++, parentId = parent, parentT = 1, isTrunk = trunk,
            radius = radius, points = points };
        t.branches.Add(b); return b;
    }

    public static void Generate(StylizedTreeAuthoring t)
    {
        Validate(t); var source = t.referenceTrunk;
        Vector3 top = source.root + Vector3.up * source.sourceHeight;
        var trunk = Add(t, new List<Vector3> { Point(t, source.root), Point(t, Vector3.Lerp(source.root, top, .5f)), Point(t, top) }, Mathf.Max(.01f, t.trunkRadius), -1, true);
        foreach (var guide in source.guides)
            Add(t, new List<Vector3> { Point(t, guide.start), Point(t, Vector3.Lerp(guide.start, guide.end, .5f)), Point(t, guide.end) }, Mathf.Max(.002f, t.height * .0025f), trunk.id);
    }

    public static void AppendBark(StylizedTreeAuthoring t, STSMeshData target, int lod)
    {
        Validate(t); var meshes = t.referenceTrunk.barkLODs;
        Mesh source = meshes[Mathf.Min(lod, meshes.Length - 1)];
        if (!source) source = meshes[0];
        // These arrays belong to the private snapshot. Nothing writes to that Mesh.
        Vector3[] vertices = source.vertices, normals = source.normals;
        Vector2[] uv = source.uv; Color[] colors = source.colors;
        int offset = target.v.Count;
        for (int i = 0; i < vertices.Length; i++)
        {
            target.v.Add(Point(t, vertices[i]));
            target.n.Add(Normal(t, vertices[i], normals.Length == vertices.Length ? normals[i] : Vector3.up));
            target.uv.Add(uv.Length == vertices.Length ? uv[i] : Vector2.zero);
            target.colors.Add(colors.Length == vertices.Length ? colors[i] : Color.white);
        }
        foreach (int index in source.triangles) target.triangles.Add(offset + index);
    }
}
#endif
