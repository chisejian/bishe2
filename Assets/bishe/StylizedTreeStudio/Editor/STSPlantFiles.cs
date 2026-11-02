#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static class STSPlantFiles
{
    public static void SavePreset(StylizedTreeAuthoring t)
    {
        string path=EditorUtility.SaveFilePanelInProject("保存自定义植物预设","我的植物预设","asset","保存树形参数与 PNG 贴图引用。");
        if(string.IsNullOrEmpty(path))return;
        var asset=AssetDatabase.LoadAssetAtPath<STSPlantPreset>(path);
        if(!asset&&AssetDatabase.LoadMainAssetAtPath(path)){Debug.LogError("该路径已有其他类型资源，请选择新路径。");return;}
        bool isNew=!asset;if(isNew)asset=ScriptableObject.CreateInstance<STSPlantPreset>();else Undo.RecordObject(asset,"更新植物预设");
        asset.parameters=JsonUtility.ToJson(t);asset.bark=t.barkMaterial;asset.leaves=t.leafMaterial;asset.flowers=t.flowerMaterial;
        asset.centers=t.flowerCenterMaterial;asset.fruit=t.fruitMaterial;asset.flowerTexture=t.flowerTexture;asset.flowerPrefab=t.flowerPrefab;
        asset.barkColor=t.barkColor;asset.barkNormal=t.barkNormal;
        asset.cardTextures=new[]{t.leafCards.color,t.leafCards.normal,t.twigCards.color,t.twigCards.normal,t.bloomCards.color,t.bloomCards.normal,t.fruitCards.color,t.fruitCards.normal};
        asset.pluginVersion="0.5.5";
        if(isNew)AssetDatabase.CreateAsset(asset,path);else EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();EditorGUIUtility.PingObject(asset);
    }
    public static void LoadPreset(StylizedTreeAuthoring t,STSPlantPreset asset)
    {
        if(!asset||string.IsNullOrEmpty(asset.parameters))return;
        Undo.RecordObject(t,"载入植物预设");string materialId=t.materialSetId;
        // Older JSON has neither field: do not inherit another plant's values.
        t.trunkTwist=0;t.leafScale=1;
        JsonUtility.FromJsonOverwrite(asset.parameters,t);t.materialSetId=materialId;
        // Explicit references survive domain reloads; JSON instance IDs are not used for assets.
        t.barkMaterial=asset.bark;t.leafMaterial=asset.leaves;t.flowerMaterial=asset.flowers;t.flowerCenterMaterial=asset.centers;t.fruitMaterial=asset.fruit;
        t.flowerTexture=asset.flowerTexture;t.flowerPrefab=asset.flowerPrefab;t.projectionTarget=null;
        STSMaterials.Defaults(t);
        if(asset.cardTextures!=null&&asset.cardTextures.Length==8)
        {
            t.barkColor=asset.barkColor;t.barkNormal=asset.barkNormal;
            var maps=new[]{t.leafCards,t.twigCards,t.bloomCards,t.fruitCards};
            for(int i=0;i<4;i++){maps[i].color=asset.cardTextures[i*2];maps[i].normal=asset.cardTextures[i*2+1];}
        }
        STSPlantBuilder.Build(t);
    }
    public static void Bake(StylizedTreeAuthoring t,bool editable)
    {
        string path=EditorUtility.SaveFilePanelInProject(editable?"保存可编辑植物 Prefab":"烘焙游戏用植物 Prefab",t.gameObject.name,"prefab","网格保存为独立资源；只导出工坊生成部分。");
        if(string.IsNullOrEmpty(path))return;
        if(AssetDatabase.LoadMainAssetAtPath(path)&&!AssetDatabase.LoadAssetAtPath<GameObject>(path)){Debug.LogError("目标路径不是 Prefab，请选择其他路径。");return;}
        if(!STSPlantBuilder.Build(t))return;
        GameObject clone=null;string bundlePath=null;bool success=false;
        try
        {
            Transform output=t.transform.Find(STSPlantBuilder.RootName);if(!output)throw new InvalidOperationException("没有可导出的生成模型。");
            if(editable)
            {
                clone=new GameObject(t.gameObject.name);var author=clone.AddComponent<StylizedTreeAuthoring>();EditorUtility.CopySerialized(t,author);
                // Scene-only colliders must never leak into a Prefab asset.
                author.projectionTarget=null;author.materialSetId=Guid.NewGuid().ToString("N");
                GameObject generated=UnityEngine.Object.Instantiate(output.gameObject,clone.transform,false);generated.name=STSPlantBuilder.RootName;
                var marker=generated.GetComponent<STSGeneratedOutput>();if(marker)marker.owner=author;
            }
            else {clone=UnityEngine.Object.Instantiate(output.gameObject);clone.name=t.gameObject.name;clone.transform.SetParent(null,false);clone.transform.localPosition=Vector3.zero;clone.transform.localRotation=Quaternion.identity;clone.transform.localScale=Vector3.one;}
            string folder=Path.GetDirectoryName(path).Replace('\\','/'),name=Path.GetFileNameWithoutExtension(path);
            bundlePath=AssetDatabase.GenerateUniqueAssetPath(folder+"/"+name+"_Meshes.asset");
            var bundle=ScriptableObject.CreateInstance<STSPlantMeshBundle>();bundle.plantName=t.gameObject.name;AssetDatabase.CreateAsset(bundle,bundlePath);
            var copies=new Dictionary<Mesh,Mesh>();
            foreach(var mf in clone.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh source=mf.sharedMesh;if(!source||EditorUtility.IsPersistent(source))continue;
                Mesh copy;if(!copies.TryGetValue(source,out copy)){copy=UnityEngine.Object.Instantiate(source);copy.name=source.name;AssetDatabase.AddObjectToAsset(copy,bundle);copies.Add(source,copy);}
                mf.sharedMesh=copy;
            }
            // Freeze material values too: later edits to the authoring plant must not
            // change an already baked game Prefab.
            var materialCopies=new Dictionary<Material,Material>();
            foreach(var renderer in clone.GetComponentsInChildren<Renderer>(true))
            {
                var slots=renderer.sharedMaterials;
                for(int i=0;i<slots.Length;i++)
                {
                    Material source=slots[i];if(!source)continue;Material copy;
                    if(!materialCopies.TryGetValue(source,out copy))
                    {copy=new Material(source){name=source.name};AssetDatabase.AddObjectToAsset(copy,bundle);materialCopies.Add(source,copy);}
                    slots[i]=copy;
                }
                renderer.sharedMaterials=slots;
            }
            var editableAuthor=clone.GetComponent<StylizedTreeAuthoring>();
            if(editableAuthor)
            {
                if(materialCopies.ContainsKey(t.barkMaterial))editableAuthor.barkMaterial=materialCopies[t.barkMaterial];
                if(materialCopies.ContainsKey(t.leafMaterial))editableAuthor.leafMaterial=materialCopies[t.leafMaterial];
                if(materialCopies.ContainsKey(t.flowerMaterial))editableAuthor.flowerMaterial=materialCopies[t.flowerMaterial];
                if(materialCopies.ContainsKey(t.flowerCenterMaterial))editableAuthor.flowerCenterMaterial=materialCopies[t.flowerCenterMaterial];
                if(materialCopies.ContainsKey(t.fruitMaterial))editableAuthor.fruitMaterial=materialCopies[t.fruitMaterial];
            }
            foreach(var marker in clone.GetComponentsInChildren<STSGeneratedOutput>(true))
            {
                if(!editable){UnityEngine.Object.DestroyImmediate(marker);continue;}
                marker.ownedMeshes.Clear();foreach(var copy in copies.Values)marker.ownedMeshes.Add(copy);
            }
            AssetDatabase.SaveAssets();GameObject prefab=PrefabUtility.SaveAsPrefabAsset(clone,path);
            if(!prefab)throw new InvalidOperationException("Prefab 保存失败，请查看 Console。");
            success=true;EditorGUIUtility.PingObject(prefab);Debug.Log("[植物工坊] 已导出 "+path,prefab);
        }
        catch(Exception ex){Debug.LogError("[植物工坊] 导出失败："+ex.Message);}
        finally
        {
            if(clone)UnityEngine.Object.DestroyImmediate(clone);
            if(!success&&!string.IsNullOrEmpty(bundlePath))AssetDatabase.DeleteAsset(bundlePath);
        }
    }
    public static void Variants(StylizedTreeAuthoring source,int count,float spacing)
    {
        count=Mathf.Clamp(count,1,20);int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("批量生成植物变体");
        for(int i=0;i<count;i++)
        {
            var go=new GameObject(source.gameObject.name+"_Variant_"+(i+1).ToString("D2"));
            Undo.RegisterCreatedObjectUndo(go,"生成植物变体");go.transform.SetParent(source.transform.parent,false);
            go.transform.position=source.transform.position+source.transform.right*((i%5+1)*spacing)+source.transform.forward*((i/5)*spacing);
            go.transform.rotation=source.transform.rotation;go.transform.localScale=source.transform.localScale;
            var t=go.AddComponent<StylizedTreeAuthoring>();EditorUtility.CopySerialized(source,t);
            t.materialSetId=Guid.NewGuid().ToString("N");t.seed=STSRandom.Hash(source.seed,i+1);t.leafSeed=STSRandom.Hash(source.leafSeed,i+1);t.flowerSeed=STSRandom.Hash(source.flowerSeed,i+1);t.fruitSeed=STSRandom.Hash(source.fruitSeed,i+1);
            // Locked hand-shaped skeletons remain the same; unlocked ones get a new silhouette.
            if(!t.lockBranches)STSSkeleton.Generate(t);
            STSPlantBuilder.Build(t);
        }
        Undo.CollapseUndoOperations(undo);
    }
}
#endif
