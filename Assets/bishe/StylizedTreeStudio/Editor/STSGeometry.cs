#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal struct STSFrame
{
    public Vector3 p, tangent, right, up;
    public STSFrame(Vector3 p, Vector3 tangent, Vector3 right)
    {
        this.p = p;
        this.tangent = tangent.sqrMagnitude < 1e-10f ? Vector3.up : tangent.normalized;
        this.right = Vector3.ProjectOnPlane(right, this.tangent).normalized;
        if (this.right.sqrMagnitude < 1e-8f)
            this.right = Vector3.Cross(this.tangent, Mathf.Abs(this.tangent.y) > .9f ? Vector3.forward : Vector3.up).normalized;
        up = Vector3.Cross(this.tangent, this.right).normalized;
    }
    public Vector3 Offset(Vector3 v) { return right * v.x + up * v.y + tangent * v.z; }
}

internal static class STSPath
{
    public static List<Vector3> Shape(StylizedTreeBranch b) { return Smooth(b.points, 3); }
    public static STSFrame OnBranch(StylizedTreeBranch b, float t) { return Evaluate(Shape(b), t); }
    public static bool Finite(Vector3 p) { return Finite(p.x) && Finite(p.y) && Finite(p.z); }
    public static bool Finite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }
    public static float Length(List<Vector3> pts)
    {
        float length = 0;
        for (int i = 1; i < pts.Count; i++) length += Vector3.Distance(pts[i-1], pts[i]);
        return length;
    }
    public static STSFrame Evaluate(List<Vector3> pts, float t)
    {
        if (pts == null || pts.Count < 2) return new STSFrame(pts != null && pts.Count > 0 ? pts[0] : Vector3.zero, Vector3.up, Vector3.right);
        float wanted = Length(pts) * Mathf.Clamp01(t), travelled = 0;
        Vector3 previous = (pts[1] - pts[0]).normalized;
        if (previous.sqrMagnitude < 1e-8f) previous = Vector3.up;
        Vector3 right = new STSFrame(pts[0], previous, Vector3.right).right;
        for (int i = 1; i < pts.Count; i++)
        {
            Vector3 d = pts[i] - pts[i-1]; float len = d.magnitude;
            if (len < 1e-6f) continue;
            Vector3 tangent = d / len;
            right = Quaternion.FromToRotation(previous, tangent) * right;
            if (travelled + len >= wanted || i == pts.Count - 1)
                return new STSFrame(Vector3.Lerp(pts[i-1], pts[i], Mathf.Clamp01((wanted - travelled) / len)), tangent, right);
            previous = tangent; travelled += len;
        }
        return new STSFrame(pts[pts.Count-1], previous, right);
    }
    public static List<Vector3> Smooth(List<Vector3> pts, int steps)
    {
        var result = new List<Vector3>();
        if (pts == null || pts.Count < 2) return result;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 a = pts[Mathf.Max(0, i-1)], b = pts[i], c = pts[i+1], d = pts[Mathf.Min(pts.Count-1, i+2)];
            for (int k = 0; k < steps; k++)
            {
                float u = k / (float)steps, u2 = u*u, u3 = u2*u;
                Vector3 p = .5f * ((2*b) + (-a+c)*u + (2*a-5*b+4*c-d)*u2 + (-a+3*b-3*c+d)*u3);
                if (result.Count == 0 || Vector3.Distance(result[result.Count-1], p) > 1e-5f) result.Add(p);
            }
        }
        result.Add(pts[pts.Count-1]); return result;
    }
    public static float Closest(List<Vector3> points, Vector3 p, out float distance)
    {
        float best = float.PositiveInfinity, at = 0, travelled = 0, length = Length(points);
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 d = points[i] - points[i-1]; float len = d.magnitude;
            float f = Mathf.Clamp01(Vector3.Dot(p-points[i-1], d) / Mathf.Max(d.sqrMagnitude, 1e-12f));
            float dist = (p - (points[i-1]+d*f)).sqrMagnitude;
            if (dist < best) { best = dist; at = (travelled+len*f)/Mathf.Max(length, 1e-6f); }
            travelled += len;
        }
        distance = Mathf.Sqrt(best); return at;
    }
}

internal class STSMeshData
{
    public readonly List<Vector3> v = new List<Vector3>();
    public readonly List<Vector3> n = new List<Vector3>();
    public readonly List<Vector2> uv = new List<Vector2>();
    public readonly List<Color> colors = new List<Color>();
    public readonly List<int> triangles = new List<int>();
    public int TriangleCount { get { return triangles.Count / 3; } }

    int Vertex(Vector3 p, Vector3 normal, Vector2 tex, Color color)
    {
        if(v.Count>=1000000)throw new InvalidOperationException("单部位超过一百万顶点，请减少数量或分段；上次模型保留。");
        int index = v.Count; v.Add(p); n.Add(normal.normalized); uv.Add(tex); colors.Add(color); return index;
    }
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color, bool doubleSided = true)
    {
        Vector3 normal = Vector3.Cross(b-a, c-a).normalized;
        if (normal.sqrMagnitude < 1e-8f) return;
        int i = Vertex(a, normal, Vector2.zero, color);
        Vertex(b, normal, Vector2.right, color); Vertex(c, normal, Vector2.up, color);
        triangles.Add(i); triangles.Add(i+1); triangles.Add(i+2);
        if (!doubleSided) return;
        i = Vertex(a, -normal, Vector2.zero, color);
        Vertex(c, -normal, Vector2.up, color); Vertex(b, -normal, Vector2.right, color);
        triangles.Add(i); triangles.Add(i+1); triangles.Add(i+2);
    }
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd, bool doubleSided = true)
    {
        // a,b bottom edge; c,d top edge. Back faces own their vertices and normals.
        Vector3 normal = (Vector3.Cross(b-a, c-a)+Vector3.Cross(d-b, c-b)).normalized;
        if (normal.sqrMagnitude < 1e-8f) return;
        int i = Vertex(a, normal, new Vector2(0,0), ca);
        Vertex(b, normal, new Vector2(1,0), cb); Vertex(c, normal, new Vector2(0,1), cc); Vertex(d, normal, new Vector2(1,1), cd);
        triangles.Add(i); triangles.Add(i+1); triangles.Add(i+2); triangles.Add(i+1); triangles.Add(i+3); triangles.Add(i+2);
        if (!doubleSided) return;
        i = Vertex(a, -normal, new Vector2(0,0), ca);
        Vertex(b, -normal, new Vector2(1,0), cb); Vertex(c, -normal, new Vector2(0,1), cc); Vertex(d, -normal, new Vector2(1,1), cd);
        triangles.Add(i); triangles.Add(i+2); triangles.Add(i+1); triangles.Add(i+1); triangles.Add(i+2); triangles.Add(i+3);
    }
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    { Quad(a, b, c, d, color, color, color, color); }

    public void Tube(List<Vector3> points, float radius, int sides, Color color, Func<float,float> profile = null)
    {
        if (points == null || points.Count < 2 || radius <= 0) return;
        sides = Mathf.Clamp(sides, 3, 16);
        var pts = new List<Vector3>();
        foreach (Vector3 p in points)
            if (pts.Count == 0 || Vector3.Distance(pts[pts.Count-1], p) > 1e-6f) pts.Add(p);
        if (pts.Count < 2) return;
        int start = v.Count; float total = STSPath.Length(pts), travelled = 0;
        Vector3 previous = (pts[1]-pts[0]).normalized;
        Vector3 right = new STSFrame(pts[0], previous, Vector3.right).right;
        var tangents = new List<Vector3>();
        for (int k = 0; k < pts.Count; k++)
        {
            if (k > 0) travelled += Vector3.Distance(pts[k], pts[k-1]);
            Vector3 tangent = (pts[Mathf.Min(k+1,pts.Count-1)]-pts[Mathf.Max(0,k-1)]).normalized;
            if (tangent.sqrMagnitude < 1e-8f) tangent = previous;
            right = Quaternion.FromToRotation(previous, tangent)*right;
            STSFrame frame = new STSFrame(pts[k], tangent, right); right = frame.right;
            float t = travelled/Mathf.Max(total,1e-6f);
            float r = Mathf.Max(.0003f, radius * (profile == null ? Mathf.Lerp(1,.06f,t) : Mathf.Max(.001f, profile(t))));
            for (int j = 0; j <= sides; j++)
            {
                float a = j*2*Mathf.PI/sides;
                Vector3 normal = frame.right*Mathf.Cos(a)+frame.up*Mathf.Sin(a);
                Vertex(pts[k]+normal*r, normal, new Vector2(j/(float)sides, travelled), color);
                if (k == 0 || j == sides) continue;
                int x = start + (k-1)*(sides+1)+j, y = x+sides+1;
                triangles.Add(x); triangles.Add(x+1); triangles.Add(y);
                triangles.Add(x+1); triangles.Add(y+1); triangles.Add(y);
            }
            tangents.Add(tangent); previous = tangent;
        }
        // Close both ends. Cap vertices are separate, so caps cannot flatten side normals.
        for (int end = 0; end < 2; end++)
        {
            int k = end == 0 ? 0 : pts.Count-1;
            Vector3 normal = tangents[k]*(end == 0 ? -1 : 1);
            for (int j = 0; j < sides; j++)
            {
                Vector3 a = v[start+k*(sides+1)+j], b = v[start+k*(sides+1)+j+1];
                if (Vector3.Dot(Vector3.Cross(a-pts[k], b-pts[k]), normal) < 0) { Vector3 tmp=a; a=b; b=tmp; }
                int i = Vertex(pts[k], normal, new Vector2(.5f,.5f), color);
                Vertex(a, normal, Vector2.zero, color); Vertex(b, normal, Vector2.one, color);
                triangles.Add(i); triangles.Add(i+1); triangles.Add(i+2);
            }
        }
    }
    public void Rod(Vector3 a, Vector3 b, float radius, Color color, int sides = 4)
    { Tube(new List<Vector3>{a,b}, radius, sides, color, t => 1); }

    public void Ellipsoid(Vector3 center, Vector3 radii, Color color, int segments = 10, int rings = 6, float pear = 0)
    {
        // Triangle patches avoid degenerate pole quads and provide outward normals.
        Func<int,int,Vector3> point = (r,s) => {
            float lat = Mathf.PI*r/rings, a=2*Mathf.PI*s/segments;
            float f=1-pear*.4f*Mathf.Cos(lat);
            return center+new Vector3(Mathf.Sin(lat)*Mathf.Cos(a)*radii.x*f, Mathf.Cos(lat)*radii.y, Mathf.Sin(lat)*Mathf.Sin(a)*radii.z*f);
        };
        for (int r=0;r<rings;r++) for (int s=0;s<segments;s++)
        {
            Vector3 a=point(r,s), b=point(r,s+1), c=point(r+1,s), d=point(r+1,s+1);
            if(r>0) OutwardTriangle(a,b,c,center,radii,color);
            if(r<rings-1) OutwardTriangle(b,d,c,center,radii,color);
        }
    }
    void OutwardTriangle(Vector3 a,Vector3 b,Vector3 c,Vector3 center,Vector3 radii,Color color)
    {
        if(Vector3.Dot(Vector3.Cross(b-a,c-a),(a+b+c)/3-center)<0) {Vector3 tmp=b;b=c;c=tmp;}
        int start=v.Count;
        foreach(Vector3 p in new[]{a,b,c})
        {
            Vector3 d=p-center;
            Vector3 normal=new Vector3(d.x/Mathf.Max(radii.x*radii.x,1e-8f),d.y/Mathf.Max(radii.y*radii.y,1e-8f),d.z/Mathf.Max(radii.z*radii.z,1e-8f)).normalized;
            Vertex(p,normal,new Vector2(Mathf.Atan2(d.z,d.x)/(2*Mathf.PI)+.5f,Mathf.Acos(Mathf.Clamp(normal.y,-1,1))/Mathf.PI),color);
        }
        triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
    }
    public Mesh ToMesh(string name)
    {
        if (v.Count == 0) return null;
        for(int i=0;i<v.Count;i++) if(!STSPath.Finite(v[i]) || !STSPath.Finite(n[i])) throw new InvalidOperationException("生成了无效顶点或法线："+name);
        Mesh mesh = new Mesh {name=name, indexFormat=v.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
        mesh.SetVertices(v);mesh.SetNormals(n);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);
        mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
    }
}
#endif
