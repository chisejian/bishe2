#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace BougainvilleaStudio
{
    [InitializeOnLoad] internal static class BGSBuilder
    {
        public const string Root="BGS_Generated";
        public const string Folder="Assets/BougainvilleaStudio";
        static readonly Dictionary<int,BGSSurfaceCache> caches=new Dictionary<int,BGSSurfaceCache>();
        static readonly Dictionary<BGSAuthoring,double> pending=new Dictionary<BGSAuthoring,double>();
        static BGSBuilder(){EditorApplication.delayCall+=()=>{if(!EditorApplication.isPlayingOrWillChangePlaymode)foreach(var t in Resources.FindObjectsOfTypeAll<BGSAuthoring>())if(t&&!EditorUtility.IsPersistent(t)&&t.gameObject.scene.IsValid()&&t.gameObject.scene.isLoaded)Schedule(t);};EditorApplication.update+=Tick;EditorApplication.quitting+=ClearCaches;AssemblyReloadEvents.beforeAssemblyReload+=ClearCaches;EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.ExitingEditMode)ClearCaches();};Undo.undoRedoPerformed+=()=>{foreach(var t in Resources.FindObjectsOfTypeAll<BGSAuthoring>())if(t&&!EditorUtility.IsPersistent(t)&&t.gameObject.scene.IsValid())Schedule(t,true);};}
        static void Tick(){var ready=new List<BGSAuthoring>();foreach(var p in pending)if(!p.Key||EditorApplication.timeSinceStartup>=p.Value)ready.Add(p.Key);foreach(var t in ready){pending.Remove(t);if(t)Build(t);}}
        public static void Schedule(BGSAuthoring t,bool force=false){if(t&&(t.autoRefresh||force))pending[t]=EditorApplication.timeSinceStartup+.18;}
        public static void ClearCaches(){foreach(var c in caches.Values)c.Dispose();caches.Clear();}
        public static IBGSSurfaceQuery Query(BGSAuthoring t)
        {
            if(!t.snapEnabled||!t.snapTarget||t.snapStrength<=0)return null;
            if(t.snapTarget==t.transform||t.snapTarget.IsChildOf(t.transform))throw new InvalidOperationException("吸附目标不能是三角梅自身或生成模型。");
            int id=t.snapTarget.GetInstanceID(),fingerprint=BGSSurfaceCache.Fingerprint(t.snapTarget);BGSSurfaceCache cache;
            if(caches.TryGetValue(id,out cache)&&cache.fingerprint==fingerprint)return cache;
            if(cache!=null)cache.Dispose();cache=new BGSSurfaceCache(t.snapTarget);caches[id]=cache;return cache;
        }
        public static void EnsureFolder(string folder)
        {string[] parts=folder.Split('/');string at=parts[0];for(int i=1;i<parts.Length;i++){string next=at+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(at,parts[i]);at=next;}}
        static Material Material(BGSAuthoring t,bool edge)
        {
            if(!string.IsNullOrEmpty(t.materialId))foreach(var other in Resources.FindObjectsOfTypeAll<BGSAuthoring>())if(other&&other!=t&&!EditorUtility.IsPersistent(other)&&other.materialId==t.materialId){t.materialId=null;break;}
            if(string.IsNullOrEmpty(t.materialId))t.materialId=Guid.NewGuid().ToString("N");EnsureFolder(Folder+"/GeneratedMaterials");
            string path=Folder+"/GeneratedMaterials/"+t.materialId+(edge?"_Edges.mat":"_Body.mat");var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader=Shader.Find(edge?"BougainvilleaStudio/Edges":"BougainvilleaStudio/Body");if(!shader)throw new InvalidOperationException("未找到三角梅着色器，请完整复制 Shaders 文件夹。");
            if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}else material.shader=shader;
            material.SetTexture("_MainTex",t.atlas);material.SetColor("_Color",Color.white);material.SetFloat("_Seed",t.seed);material.SetFloat("_PatternSize",t.textureMetres);material.SetFloat("_Cutoff",.35f);material.SetFloat("_WindStrength",.015f);material.enableInstancing=true;EditorUtility.SetDirty(material);return material;
        }
        static Mesh Mesh(BGSData data,string name)
        {
            if(data.vertices.Count==0)return null;
            var mesh=new Mesh{name=name,indexFormat=data.vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.SetVertices(data.vertices);mesh.SetNormals(data.normals);mesh.SetUVs(0,data.uv);mesh.SetUVs(1,data.wind);mesh.SetColors(data.colors);mesh.SetTriangles(data.triangles,0);mesh.RecalculateBounds();return mesh;
        }
        static void Add(Transform parent,BGSData data,Material material,string name,BGSGenerated marker)
        {var mesh=Mesh(data,name);if(!mesh)return;marker.meshes.Add(mesh);var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.On;}
        public static void Release(GameObject go)
        {if(!go)return;var marker=go.GetComponent<BGSGenerated>();if(!marker)throw new InvalidOperationException("同名节点不是本插件生成，未删除。");foreach(var mesh in marker.meshes)if(mesh&&!EditorUtility.IsPersistent(mesh))
            {
                bool shared=false;foreach(var other in Resources.FindObjectsOfTypeAll<BGSGenerated>())if(other&&other!=marker&&other.meshes.Contains(mesh)){shared=true;break;}
                if(!shared)UnityEngine.Object.DestroyImmediate(mesh);
            }UnityEngine.Object.DestroyImmediate(go);}
        public static bool Build(BGSAuthoring t)
        {
            if(!t||EditorUtility.IsPersistent(t)||EditorApplication.isPlayingOrWillChangePlaymode)return false;
            pending.Remove(t);GameObject staged=null;
            try
            {
                if(!t.atlas)t.atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/BougainvilleaAtlas.png");
                if(!t.atlas)throw new InvalidOperationException("请指定花叶图集。");
                if(t.mode==BGSMode.FlowerWall?t.wallPoints.Count!=9:t.branches.Count==0)BGSCore.Randomize(t,false);
                var old=t.transform.Find(Root);if(old&&(!old.GetComponent<BGSGenerated>()||old.GetComponent<BGSGenerated>().owner!=t))throw new InvalidOperationException("同名节点归属不匹配，旧内容保留。");
                var query=Query(t);var surface=query as BGSSurfaceCache;if(surface!=null){surface.hits=0;surface.queries=0;}var body=Material(t,false);var foliage=Material(t,true);
                staged=new GameObject("BGS_Building"){hideFlags=HideFlags.HideInHierarchy};staged.transform.SetParent(t.transform,false);var marker=staged.AddComponent<BGSGenerated>();marker.owner=t;
                int levels=t.generateLOD?3:1;var lods=new List<LOD>();int triangles=0;var lengths=new List<float>();
                if(t.mode==BGSMode.FlowerBranches)foreach(var branch in t.branches)lengths.Add(BGSCore.Length(t,BGSCore.SnappedCurve(t,branch,query)));
                for(int lod=0;lod<levels;lod++)
                {
                    var data=BGSCore.Build(t,lod,query);if(lod==0&&data.Triangles==0&&t.fullness>.15f)throw new InvalidOperationException("控制点没有有效长度或面积，请调整控制点，或解除锁定后重新随机。");var go=new GameObject("LOD"+lod);go.transform.SetParent(staged.transform,false);
                    Add(go.transform,data.edges,foliage,"疏密花叶簇",marker);Add(go.transform,data.body,body,"折叠花叶与可选细枝",marker);lods.Add(new LOD(lod==0?.5f:lod==1?.18f:.025f,go.GetComponentsInChildren<Renderer>()));if(lod==0)triangles=data.Triangles;
                }
                if(t.generateLOD&&triangles>0){var group=staged.AddComponent<LODGroup>();group.SetLODs(lods.ToArray());group.RecalculateBounds();}
                if(old)Release(old.gameObject);staged.name=Root;staged.hideFlags=HideFlags.None;staged=null;t.triangles=triangles;t.snappedLengths=lengths;t.status="已生成，LOD0 三角形："+triangles.ToString("N0");if(surface!=null&&surface.hits==0)t.status+="；吸附未命中，请先放到目标表面附近或加大范围。";EditorUtility.SetDirty(t);if(t.gameObject.scene.IsValid())EditorSceneManager.MarkSceneDirty(t.gameObject.scene);SceneView.RepaintAll();return true;
            }
            catch(Exception ex){if(staged)Release(staged);t.status="生成失败，保留上次模型："+ex.Message;Debug.LogError("[三角梅工坊] "+ex.Message,t);return false;}
        }
        public static void SavePreset(BGSAuthoring t)
        {
            string path=EditorUtility.SaveFilePanelInProject("保存三角梅预设",t.name+"_参数","asset","包含两种模式、手调点和吸附设置；不保存场景目标引用。");if(string.IsNullOrEmpty(path))return;
            if(AssetDatabase.LoadMainAssetAtPath(path)){Debug.LogWarning("请选择尚未使用的文件名。");return;}
            var asset=ScriptableObject.CreateInstance<BGSPreset>();var temporary=new GameObject("BGS_PresetCopy"){hideFlags=HideFlags.HideAndDontSave};
            try{var copy=temporary.AddComponent<BGSAuthoring>();EditorUtility.CopySerialized(t,copy);copy.snapTarget=null;copy.materialId=null;copy.status=null;asset.parameters=JsonUtility.ToJson(copy);}
            finally{UnityEngine.Object.DestroyImmediate(temporary);}
            asset.atlas=t.atlas;AssetDatabase.CreateAsset(asset,path);AssetDatabase.SaveAssets();EditorGUIUtility.PingObject(asset);
        }
        public static void LoadPreset(BGSAuthoring t,BGSPreset preset)
        {
            if(!preset||string.IsNullOrEmpty(preset.parameters))return;Undo.RecordObject(t,"载入三角梅预设");string id=t.materialId;JsonUtility.FromJsonOverwrite(preset.parameters,t);t.materialId=id;t.atlas=preset.atlas;t.snapTarget=null;t.snapEnabled=false;EditorUtility.SetDirty(t);Build(t);
        }
        public static void Bake(BGSAuthoring t)
        {
            string path=EditorUtility.SaveFilePanelInProject("烘焙游戏用 Prefab",t.gameObject.name,"prefab","只保存静态模型、材质和 LOD；不包含吸附查询或编辑器脚本。");if(string.IsNullOrEmpty(path))return;
            if(AssetDatabase.LoadMainAssetAtPath(path)){Debug.LogWarning("请选择尚未使用的 Prefab 文件名。");return;}if(!Build(t))return;
            GameObject clone=null;string bundlePath=null;bool success=false;
            try
            {
                clone=UnityEngine.Object.Instantiate(t.transform.Find(Root).gameObject);clone.name=t.gameObject.name;clone.transform.SetParent(null,false);clone.transform.localPosition=Vector3.zero;clone.transform.localRotation=Quaternion.identity;clone.transform.localScale=Vector3.one;
                bundlePath=AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.GetDirectoryName(path).Replace('\\','/')+"/"+System.IO.Path.GetFileNameWithoutExtension(path)+"_Data.asset");var bundle=ScriptableObject.CreateInstance<BGSMeshBundle>();AssetDatabase.CreateAsset(bundle,bundlePath);
                var copies=new Dictionary<Mesh,Mesh>();foreach(var filter in clone.GetComponentsInChildren<MeshFilter>())if(filter.sharedMesh){Mesh mesh;if(!copies.TryGetValue(filter.sharedMesh,out mesh)){mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);AssetDatabase.AddObjectToAsset(mesh,bundle);copies.Add(filter.sharedMesh,mesh);}filter.sharedMesh=mesh;}
                var materials=new Dictionary<Material,Material>();foreach(var renderer in clone.GetComponentsInChildren<Renderer>()){var slots=renderer.sharedMaterials;for(int i=0;i<slots.Length;i++)if(slots[i]){Material mat;if(!materials.TryGetValue(slots[i],out mat)){mat=new Material(slots[i]);AssetDatabase.AddObjectToAsset(mat,bundle);materials.Add(slots[i],mat);}slots[i]=mat;}renderer.sharedMaterials=slots;}
                foreach(var marker in clone.GetComponentsInChildren<BGSGenerated>())UnityEngine.Object.DestroyImmediate(marker);
                AssetDatabase.SaveAssets();var prefab=PrefabUtility.SaveAsPrefabAsset(clone,path);if(!prefab)throw new InvalidOperationException("Prefab 保存失败。");success=true;EditorGUIUtility.PingObject(prefab);
            }
            catch(Exception ex){Debug.LogError("[三角梅工坊] 烘焙失败："+ex.Message);}
            finally{if(clone)UnityEngine.Object.DestroyImmediate(clone);if(!success&&bundlePath!=null)AssetDatabase.DeleteAsset(bundlePath);}
        }
    }
}
#endif
