#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Geometry is generated as before, then stored outside the scene. Only this
// editor-session's unshared working assets can be updated in place. Saving a
// scene/prefab or reloading scripts freezes them, protecting saved references.
[InitializeOnLoad]
internal static class STSSceneMeshStorage
{
    internal const string Folder="Assets/StylizedTreeStudio/Generated/SceneMeshes";
    sealed class Working { public StylizedTreeAuthoring owner; }
    static readonly Dictionary<STSPlantMeshBundle,Working> WorkingAssets=new Dictionary<STSPlantMeshBundle,Working>();
    static STSSceneMeshStorage()
    {
        EditorSceneManager.sceneSaving+=(scene,path)=>Freeze();
        PrefabStage.prefabSaving+=root=>Freeze();
        PrefabUtility.prefabInstanceApplying+=root=>Freeze();
        Undo.undoRedoPerformed+=Freeze;
    }
    internal static void Freeze(){WorkingAssets.Clear();}

    static bool Reusable(STSPlantMeshBundle bundle,StylizedTreeAuthoring owner,STSGeneratedOutput previous)
    {
        Working work;
        if(!bundle||!WorkingAssets.TryGetValue(bundle,out work)||work.owner!=owner||
            !AssetDatabase.GetAssetPath(bundle).StartsWith(Folder+"/",StringComparison.Ordinal))return false;
        foreach(var other in Resources.FindObjectsOfTypeAll<STSGeneratedOutput>())
            if(other&&other!=previous&&other.meshAsset==bundle){WorkingAssets.Remove(bundle);return false;}
        // Also protect a mesh copied into a plain MeshFilter without its marker.
        var meshes=new HashSet<Mesh>(bundle.meshes);
        foreach(var filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
            if(filter&&meshes.Contains(filter.sharedMesh)&&filter.GetComponentInParent<STSGeneratedOutput>()!=previous)
            {WorkingAssets.Remove(bundle);return false;}
        return true;
    }

    internal static int InlineCount(STSGeneratedOutput output)
    {
        var unique=new HashSet<Mesh>();
        if(output&&output.ownedMeshes!=null)foreach(var mesh in output.ownedMeshes)
            if(mesh&&!EditorUtility.IsPersistent(mesh))unique.Add(mesh);
        return unique.Count;
    }

    // Returns the number of formerly in-scene meshes. No authoring parameters,
    // materials, renderer flags, transforms, or LOD settings are regenerated.
    internal static int Store(StylizedTreeAuthoring owner,STSGeneratedOutput output,STSGeneratedOutput previous,bool undoable)
    {
        if(!owner||!output||output.owner!=owner||EditorUtility.IsPersistent(owner)||EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请选择场景中属于当前植物的可编辑生成节点。");
        var sources=new List<Mesh>();var unique=new HashSet<Mesh>();
        foreach(var mesh in output.ownedMeshes)
            if(mesh&&!EditorUtility.IsPersistent(mesh)&&unique.Add(mesh))sources.Add(mesh);
        if(sources.Count==0)return 0;

        var filters=output.GetComponentsInChildren<MeshFilter>(true);
        var colliders=output.GetComponentsInChildren<MeshCollider>(true);
        var changes=new List<Object>{output};
        foreach(var filter in filters)if(unique.Contains(filter.sharedMesh))changes.Add(filter);
        foreach(var collider in colliders)if(unique.Contains(collider.sharedMesh))changes.Add(collider);

        STSPlantMeshBundle bundle=previous?previous.meshAsset:null;
        bool reuse=Reusable(bundle,owner,previous),created=false;
        string path=null;var remap=new Dictionary<Mesh,Mesh>();
        var backups=new Dictionary<Mesh,Mesh>();var added=new List<Mesh>();
        List<string> oldKeys=null;List<Mesh> oldMeshes=null;
        var originalOwned=new List<Mesh>(output.ownedMeshes);var originalAsset=output.meshAsset;
        bool rebound=false,success=false;
        try
        {
            if(!reuse)
            {
                STSMaterials.EnsureFolders(Folder);
                bundle=ScriptableObject.CreateInstance<STSPlantMeshBundle>();
                bundle.name=owner.gameObject.name+"_网格资源";bundle.plantName=owner.gameObject.name;bundle.pluginVersion="0.5.15";
                path=AssetDatabase.GenerateUniqueAssetPath(Folder+"/Plant_"+Guid.NewGuid().ToString("N")+".asset");
                created=true;AssetDatabase.CreateAsset(bundle,path);
            }
            else path=AssetDatabase.GetAssetPath(bundle);
            oldKeys=new List<string>(bundle.meshKeys);oldMeshes=new List<Mesh>(bundle.meshes);
            var occurrences=new Dictionary<string,int>();
            foreach(var source in sources)
            {
                int occurrence;occurrences.TryGetValue(source.name,out occurrence);occurrences[source.name]=occurrence+1;
                string key=source.name+"#"+occurrence;
                int slot=bundle.meshKeys.IndexOf(key);Mesh saved=slot>=0?bundle.meshes[slot]:null;
                if(saved)
                {
                    backups.Add(saved,Object.Instantiate(saved));
                    EditorUtility.CopySerialized(source,saved);EditorUtility.SetDirty(saved);
                }
                else
                {
                    saved=Object.Instantiate(source);added.Add(saved);AssetDatabase.AddObjectToAsset(saved,bundle);
                    if(slot>=0)bundle.meshes[slot]=saved;
                    else{bundle.meshKeys.Add(key);bundle.meshes.Add(saved);}
                }
                if(!EditorUtility.IsPersistent(saved)||AssetDatabase.GetAssetPath(saved)!=path)
                    throw new InvalidOperationException("网格没有成功保存为独立资源。");
                remap.Add(source,saved);
            }
            EditorUtility.SetDirty(bundle);AssetDatabase.SaveAssetIfDirty(bundle);
            if(undoable)Undo.RecordObjects(changes.ToArray(),"转存植物网格");
            // Mark before the first assignment so any unexpected setter failure
            // restores all original bindings as well as the stored mesh data.
            rebound=true;
            foreach(var filter in filters){Mesh mesh;if(filter.sharedMesh&&remap.TryGetValue(filter.sharedMesh,out mesh)){filter.sharedMesh=mesh;Changed(filter);}}
            foreach(var collider in colliders){Mesh mesh;if(collider.sharedMesh&&remap.TryGetValue(collider.sharedMesh,out mesh)){collider.sharedMesh=mesh;Changed(collider);}}
            for(int i=0;i<output.ownedMeshes.Count;i++){Mesh mesh;if(output.ownedMeshes[i]&&remap.TryGetValue(output.ownedMeshes[i],out mesh))output.ownedMeshes[i]=mesh;}
            output.meshAsset=bundle;Changed(output);
            WorkingAssets[bundle]=new Working{owner=owner};
            if(owner.gameObject.scene.IsValid())EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
            success=true;return sources.Count;
        }
        finally
        {
            if(!success)
            {
                if(rebound)
                {
                    var reverse=new Dictionary<Mesh,Mesh>();foreach(var pair in remap)reverse[pair.Value]=pair.Key;
                    foreach(var filter in filters){Mesh mesh;if(filter.sharedMesh&&reverse.TryGetValue(filter.sharedMesh,out mesh))filter.sharedMesh=mesh;}
                    foreach(var collider in colliders){Mesh mesh;if(collider.sharedMesh&&reverse.TryGetValue(collider.sharedMesh,out mesh))collider.sharedMesh=mesh;}
                    output.ownedMeshes=originalOwned;output.meshAsset=originalAsset;EditorUtility.SetDirty(output);
                }
                if(created)
                {
                    if(!string.IsNullOrEmpty(path)&&AssetDatabase.LoadMainAssetAtPath(path)==bundle)AssetDatabase.DeleteAsset(path);
                    else if(bundle&&!EditorUtility.IsPersistent(bundle))Object.DestroyImmediate(bundle);
                }
                else if(bundle&&oldKeys!=null)
                {
                    foreach(var pair in backups)if(pair.Key){EditorUtility.CopySerialized(pair.Value,pair.Key);EditorUtility.SetDirty(pair.Key);}
                    foreach(var mesh in added)if(mesh&&EditorUtility.IsPersistent(mesh)){AssetDatabase.RemoveObjectFromAsset(mesh);Object.DestroyImmediate(mesh);}
                    bundle.meshKeys=oldKeys;bundle.meshes=oldMeshes;EditorUtility.SetDirty(bundle);
                    try{AssetDatabase.SaveAssetIfDirty(bundle);}catch(Exception ex){Debug.LogError("[植物网格] 恢复资源保存失败："+ex.Message);}
                }
                if(bundle)WorkingAssets.Remove(bundle);
                foreach(var mesh in added)if(mesh&&!EditorUtility.IsPersistent(mesh))Object.DestroyImmediate(mesh);
            }
            foreach(var mesh in backups.Values)if(mesh)Object.DestroyImmediate(mesh);
            // Migration keeps its old meshes alive for Undo. A fresh generated
            // output owns its temporary meshes exclusively and can release them.
            if(success&&!undoable)foreach(var mesh in sources)if(mesh&&!EditorUtility.IsPersistent(mesh))Object.DestroyImmediate(mesh);
        }
    }
    static void Changed(Object value)
    {
        EditorUtility.SetDirty(value);
        if(PrefabUtility.IsPartOfPrefabInstance(value))PrefabUtility.RecordPrefabInstancePropertyModifications(value);
    }
    internal static string TransferCurrent(StylizedTreeAuthoring owner)
    {
        try
        {
            var root=owner?owner.transform.Find(STSPlantBuilder.RootName):null;
            var output=root?root.GetComponent<STSGeneratedOutput>():null;
            if(!output)return "未找到当前植物的生成网格，请选中原植物的可编辑根节点。";
            int count=Store(owner,output,output,true);
            SceneView.RepaintAll();
            return count==0?"当前植物的网格已经是独立资源，无需重复转存。":"已转存 "+count+" 个网格，造型和参数保留。请按 Ctrl+S 保存场景，再查看 .unity 文件大小。";
        }
        catch(Exception ex){Debug.LogError("[植物网格] 转存失败："+ex.Message);return "转存失败，原植物保留。请查看 Console："+ex.Message;}
    }
}

// External scene/asset saves freeze working files before other saved documents
// can acquire references to them. Our targeted SaveAssetIfDirty bypasses this.
internal sealed class STSMeshStorageSaveGuard : AssetModificationProcessor
{
    static string[] OnWillSaveAssets(string[] paths){STSSceneMeshStorage.Freeze();return paths;}
}
#endif
