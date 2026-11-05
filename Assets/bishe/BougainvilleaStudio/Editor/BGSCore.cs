#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BougainvilleaStudio
{
    public struct BGSSurface {public Vector3 position,normal;}
    public interface IBGSSurfaceQuery {bool TryProject(Vector3 point,Vector3 preferred,float range,out BGSSurface hit);}
    public sealed class BGSData
    {
        public readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();
        public readonly List<Vector2> uv=new List<Vector2>(),wind=new List<Vector2>();
        public readonly List<Color> colors=new List<Color>();
        public readonly List<int> triangles=new List<int>();
        public int TriangleCount{get{return triangles.Count/3;}}
        public void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector2 ua,Vector2 ub,Vector2 uc,Color tint,float sway,bool solid=false)
        {
            Vector3 cross=Vector3.Cross(b-a,c-a);if(cross.sqrMagnitude<1e-16f)return;
            int start=vertices.Count;if(start>750000)throw new InvalidOperationException("网格过大，请缩小单次生成范围或降低细节。");
            Vector3 normal=cross/Mathf.Sqrt(cross.sqrMagnitude);
            foreach(var p in new[]{a,b,c}){if(!BGSCore.Finite(p))throw new InvalidOperationException("曲线包含无效坐标。");vertices.Add(p);normals.Add(normal);colors.Add(tint);wind.Add(new Vector2(sway,solid?1:0));}
            uv.Add(ua);uv.Add(ub);uv.Add(uc);triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
        }
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector2 ua,Vector2 ub,Vector2 uc,Vector2 ud,Color tint,float sway)
        {
            Vector3 n=Vector3.Cross(b-a,c-a)+Vector3.Cross(d-b,c-b);
            if(n.sqrMagnitude<1e-16f)return;n=n/Mathf.Sqrt(n.sqrMagnitude);int start=vertices.Count;
            if(start>750000)throw new InvalidOperationException("网格过大，请缩小单次生成范围或降低细节。");
            foreach(var p in new[]{a,b,c,d}){if(!BGSCore.Finite(p))throw new InvalidOperationException("曲线包含无效坐标。");vertices.Add(p);normals.Add(n);colors.Add(tint);wind.Add(new Vector2(sway,0));}
            uv.Add(ua);uv.Add(ub);uv.Add(uc);uv.Add(ud);
            triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);triangles.Add(start+1);triangles.Add(start+3);triangles.Add(start+2);
        }
    }
    public sealed class BGSBuild {public BGSData body=new BGSData(),edges=new BGSData();public int Triangles{get{return body.TriangleCount+edges.TriangleCount;}}}
    public static class BGSCore
    {
        public static bool Finite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        public static bool Finite(Vector3 v){return Finite(v.x)&&Finite(v.y)&&Finite(v.z);}
        public static float Range(System.Random r,float a,float b){return a+(float)r.NextDouble()*(b-a);}
        static int Seed(int seed,int id){unchecked{return (seed*73856093)^(id*19349663);}}
        public static float Length(BGSAuthoring t,List<Vector3> p)
        {float result=0;for(int i=1;i<p.Count;i++)result+=Vector3.Distance(t.transform.TransformPoint(p[i-1]),t.transform.TransformPoint(p[i]));return result;}
        public static List<Vector3> Smooth(List<BGSPoint> points,int steps=8)
        {
            var result=new List<Vector3>();if(points==null||points.Count<2)return result;
            for(int i=0;i<points.Count-1;i++)
            {
                Vector3 a=points[Mathf.Max(0,i-1)].position,b=points[i].position,c=points[i+1].position,d=points[Mathf.Min(points.Count-1,i+2)].position;
                for(int k=0;k<steps;k++){float u=k/(float)steps,u2=u*u,u3=u2*u;result.Add(.5f*((2*b)+(-a+c)*u+(2*a-5*b+4*c-d)*u2+(-a+3*b-3*c+d)*u3));}
            }
            result.Add(points[points.Count-1].position);return result;
        }
        public static void ScaleLength(BGSAuthoring t,BGSBranch branch,float metres)
        {
            if(!Finite(metres)||metres<=0)throw new ArgumentException("长度须为有效正数，没有 15 米下限。");
            float old=Length(t,Smooth(branch.points));if(old<1e-7f)throw new InvalidOperationException("曲线没有长度，请先移动控制点。");
            Vector3 start=branch.points[0].position;float scale=metres/old;
            foreach(var p in branch.points)p.position=start+(p.position-start)*scale;
        }
        public static void Randomize(BGSAuthoring t,bool preserveLocks)
        {
            if(t.mode==BGSMode.FlowerWall)
            {
                var old=t.wallPoints;var points=new List<BGSPoint>();var r=new System.Random(t.seed);
                for(int y=0;y<3;y++)for(int x=0;x<3;x++)
                {
                    int index=y*3+x;Vector3 p=new Vector3((x/2f-.5f)*t.wallWidth,y/2f*t.wallHeight,Range(r,-.12f,.16f)*t.fullness);
                    points.Add(preserveLocks&&old!=null&&old.Count==9&&old[index].locked?Copy(old[index]):new BGSPoint(p));
                }
                t.wallPoints=points;t.referenceWidth=t.wallWidth;t.referenceHeight=t.wallHeight;return;
            }
            if(!Finite(t.branchLength)||t.branchLength<=0)throw new ArgumentException("枝条长度须为正数。");
            var previous=t.branches;var next=new List<BGSBranch>();
            for(int branch=0;branch<Mathf.Clamp(t.branchCount,1,30);branch++)
            {
                BGSBranch old=previous!=null&&branch<previous.Count?previous[branch]:null;
                int count=preserveLocks&&old!=null?Mathf.Max(2,old.points.Count):13;
                var r=new System.Random(Seed(t.seed,branch+1));float phase=Range(r,0,Mathf.PI*2),cycles=.6f+t.twist*.32f;
                var b=new BGSBranch{name="花叶枝条 "+(branch+1)};
                Vector3 root=new Vector3((branch-(t.branchCount-1)*.5f)*t.branchSpacing,0,0);
                for(int k=0;k<count;k++)
                {
                    float u=k/(float)(count-1),fade=Mathf.Sin(u*Mathf.PI);float amplitude=t.branchLength*.14f*t.twist*fade;
                    b.points.Add(new BGSPoint(root+new Vector3(Mathf.Sin(phase+u*Mathf.PI*2*cycles)*amplitude,-t.branchLength*u,Mathf.Cos(phase+u*Mathf.PI*2*cycles)*amplitude*.65f)));
                }
                ScaleLength(t,b,t.branchLength);
                if(preserveLocks&&old!=null)for(int k=0;k<Mathf.Min(count,old.points.Count);k++)if(old.points[k].locked)b.points[k]=Copy(old.points[k]);
                next.Add(b);
            }
            t.branches=next;
        }
        public static BGSPoint Copy(BGSPoint p){return new BGSPoint(p.position){snapWeight=p.snapWeight,locked=p.locked};}
        public static void ResizeWall(BGSAuthoring t,float w,float h)
        {
            if(!Finite(w)||!Finite(h)||w<=0||h<=0)throw new ArgumentException("墙宽和墙高须为正数。");
            if(t.wallPoints.Count==9)foreach(var p in t.wallPoints)p.position=new Vector3(p.position.x*w/Mathf.Max(.001f,t.referenceWidth),p.position.y*h/Mathf.Max(.001f,t.referenceHeight),p.position.z);
            t.wallWidth=w;t.wallHeight=h;t.referenceWidth=w;t.referenceHeight=h;
        }
        public static Vector3 Snap(BGSAuthoring t,Vector3 local,float weight,IBGSSurfaceQuery query,float clearance=0)
        {
            if(!t.snapEnabled||query==null||t.snapStrength<=0||weight<=0)return local;
            BGSSurface hit;Vector3 world=t.transform.TransformPoint(local);
            if(!query.TryProject(world,t.transform.TransformDirection(Vector3.forward),t.snapRange,out hit))return local;
            Vector3 desired=hit.position+hit.normal*(t.surfaceOffset+clearance);
            return t.transform.InverseTransformPoint(Vector3.Lerp(world,desired,Mathf.Clamp01(t.snapStrength*weight)));
        }
        static float Noise(float x,float y,int seed){return Mathf.Sin(x*1.9f+y*.7f+seed)*.48f+Mathf.Sin(y*2.7f-x*.6f+seed*.13f)*.27f+Mathf.Sin(x*4.3f+y*3.1f)*.15f;}
        static Vector3 Row(Vector3 a,Vector3 b,Vector3 c,float t)
        {
            float value=Mathf.Clamp01(t)*2;int i=Mathf.Min(1,Mathf.FloorToInt(value));float u=value-i,u2=u*u,u3=u2*u;
            Vector3 p=i==0?a:b,q=i==0?b:c,previous=a,next=c;
            return .5f*((2*p)+(-previous+q)*u+(2*previous-5*p+4*q-next)*u2+(-previous+3*p-3*q+next)*u3);
        }
        public static Vector3 WallPoint(BGSAuthoring t,float u,float v,IBGSSurfaceQuery query)
        {
            var rows=new Vector3[3];for(int row=0;row<3;row++)rows[row]=Row(t.wallPoints[row*3].position,t.wallPoints[row*3+1].position,t.wallPoints[row*3+2].position,u);
            Vector3 p=Row(rows[0],rows[1],rows[2],v);
            float x=Mathf.Clamp01(u)*2,y=Mathf.Clamp01(v)*2;int ix=Mathf.Min(1,Mathf.FloorToInt(x)),iy=Mathf.Min(1,Mathf.FloorToInt(y));x-=ix;y-=iy;
            float weight=Mathf.Lerp(Mathf.Lerp(t.wallPoints[iy*3+ix].snapWeight,t.wallPoints[iy*3+ix+1].snapWeight,x),Mathf.Lerp(t.wallPoints[(iy+1)*3+ix].snapWeight,t.wallPoints[(iy+1)*3+ix+1].snapWeight,x),y);
            return Snap(t,p,weight,query);
        }
        public static BGSBuild Build(BGSAuthoring t,int lod,IBGSSurfaceQuery query=null)
        {
            if(!Finite(t.wallWidth)||!Finite(t.wallHeight)||!Finite(t.branchWidth)||!Finite(t.volumeDepth)||!Finite(t.textureMetres)||!Finite(t.sideBranchLength)||!Finite(t.fullness)||!Finite(t.gapAmount)||!Finite(t.detailDensity)||!Finite(t.lowerDetailFraction)||t.wallWidth<=0||t.wallHeight<=0||t.branchWidth<=0||t.volumeDepth<=0||t.textureMetres<=0||t.sideBranchLength<=0)throw new ArgumentException("尺寸须为有效正数，密度与疏密设置不能为 NaN 或无穷大。");
            return BGSVolume.Build(t,Mathf.Clamp(lod,0,2),query);
        }
        public static List<Vector3> SnappedCurve(BGSAuthoring t,BGSBranch b,IBGSSurfaceQuery query,int segments=96)
        {
            var curve=Smooth(b.points);var output=new List<Vector3>();if(curve.Count<2)return output;
            for(int i=0;i<=segments;i++)
            {
                float station;Vector3 p=AlongWithStation(curve,i/(float)segments,out station);
                float where=station*(b.points.Count-1);int j=Mathf.Min(b.points.Count-2,Mathf.FloorToInt(where));
                float weight=Mathf.Lerp(b.points[j].snapWeight,b.points[j+1].snapWeight,where-j);float clearance=Mathf.Max(Vector3.Distance(t.transform.TransformPoint(Vector3.right*t.branchWidth*.5f),t.transform.TransformPoint(Vector3.zero)),Vector3.Distance(t.transform.TransformPoint(Vector3.forward*t.volumeDepth*.5f),t.transform.TransformPoint(Vector3.zero)));output.Add(Snap(t,p,weight,query,clearance));
            }
            return output;
        }
        public static Vector3 Along(List<Vector3> points,float at){float station;return AlongWithStation(points,at,out station);}
        static Vector3 AlongWithStation(List<Vector3> points,float at,out float station)
        {
            float total=0;for(int i=1;i<points.Count;i++)total+=Vector3.Distance(points[i-1],points[i]);float wanted=total*Mathf.Clamp01(at),travelled=0;
            for(int i=1;i<points.Count;i++)
            {
                float length=Vector3.Distance(points[i-1],points[i]);
                if(travelled+length>=wanted){float u=(wanted-travelled)/Mathf.Max(.000001f,length);station=(i-1+u)/(points.Count-1);return Vector3.Lerp(points[i-1],points[i],u);}travelled+=length;
            }
            station=1;return points[points.Count-1];
        }
    }
}
#endif
