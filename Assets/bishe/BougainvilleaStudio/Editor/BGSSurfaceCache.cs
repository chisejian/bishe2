#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace BougainvilleaStudio
{
    internal sealed class BGSSurfaceCache:IBGSSurfaceQuery,IDisposable
    {
        readonly List<Collider> colliders=new List<Collider>();
        readonly List<GameObject> objects=new List<GameObject>();
        readonly List<Mesh> meshes=new List<Mesh>();
        public readonly int fingerprint;
        public int queries,hits;
        public static int Fingerprint(Transform target)
        {
            unchecked{int hash=target.GetInstanceID();foreach(var f in target.GetComponentsInChildren<MeshFilter>(true))if(f.sharedMesh&&!f.GetComponentInParent<BGSAuthoring>())hash=hash*31+f.sharedMesh.GetInstanceID()*17+f.transform.localToWorldMatrix.GetHashCode()+(f.gameObject.activeInHierarchy?1:0);foreach(var c in target.GetComponentsInChildren<Collider>(true))hash=hash*31+c.transform.localToWorldMatrix.GetHashCode();return hash;}
        }
        public BGSSurfaceCache(Transform target)
        {
            fingerprint=Fingerprint(target);
            try
            {
                foreach(var f in target.GetComponentsInChildren<MeshFilter>(true))if(f.sharedMesh&&f.gameObject.activeInHierarchy&&!f.GetComponentInParent<BGSAuthoring>())
                {
                    var source=f.sharedMesh;var vertices=source.vertices;for(int i=0;i<vertices.Length;i++)vertices[i]=f.transform.TransformPoint(vertices[i]);
                    var mesh=new Mesh{name="BGS_EditorSurface",indexFormat=vertices.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16};meshes.Add(mesh);mesh.vertices=vertices;mesh.triangles=source.triangles;mesh.RecalculateBounds();
                    var go=new GameObject("BGS_SurfaceQuery"){hideFlags=HideFlags.HideAndDontSave,layer=2};objects.Add(go);
                    var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;colliders.Add(collider);
                }
                if(colliders.Count==0)foreach(var c in target.GetComponentsInChildren<Collider>(true))if(c.enabled&&!c.isTrigger&&!c.GetComponentInParent<BGSAuthoring>())colliders.Add(c);
                Physics.SyncTransforms();
                if(colliders.Count==0)throw new InvalidOperationException("吸附目标下没有可查询的网格或碰撞体。");
            }
            catch{Dispose();throw;}
        }
        public bool TryProject(Vector3 point,Vector3 preferred,float range,out BGSSurface result)
        {
            queries++;result=new BGSSurface();float score=float.PositiveInfinity;bool found=false;
            Vector3[] directions={preferred.normalized,-preferred.normalized,Vector3.right,-Vector3.right,Vector3.up,-Vector3.up,Vector3.forward,-Vector3.forward};
            foreach(var collider in colliders)
            {
                if(!collider||collider.bounds.SqrDistance(point)>range*range)continue;
                var candidates=new List<Vector3>(directions){(point-collider.bounds.center).normalized,(point-collider.bounds.ClosestPoint(point)).normalized};
                foreach(Vector3 direction in candidates)
                {
                    if(direction.sqrMagnitude<.1f)continue;RaycastHit hit;
                    if(!collider.Raycast(new Ray(point+direction*range,-direction),out hit,range*2+.001f))continue;
                    float distance=Vector3.Distance(hit.point,point);if(distance>range+.0001f)continue;
                    Vector3 normal=hit.normal;if(Vector3.Dot(normal,point-hit.point)<0)normal=-normal;
                    float candidate=distance+(1-Vector3.Dot(normal,preferred.normalized))*range*.025f;
                    if(candidate>=score)continue;score=candidate;result=new BGSSurface{position=hit.point,normal=normal};found=true;
                }
            }
            if(found)hits++;return found;
        }
        public void Dispose(){foreach(var go in objects)if(go)UnityEngine.Object.DestroyImmediate(go);foreach(var mesh in meshes)if(mesh)UnityEngine.Object.DestroyImmediate(mesh);objects.Clear();meshes.Clear();colliders.Clear();}
    }
}
#endif
