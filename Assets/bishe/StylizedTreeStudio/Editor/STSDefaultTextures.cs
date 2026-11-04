#if UNITY_EDITOR
using System;
using UnityEditor;

internal static class STSDefaultTextures
{
    public const string Folder="Assets/StylizedTreeStudio/UserDefaults";
    public static string PathFor(STSPlantSpecies species){return Folder+"/Textures_"+species+".asset";}
    public static STSCardMap CopyMap(STSCardMap map)
    {
        return map==null?new STSCardMap():new STSCardMap{color=map.color,normal=map.normal,uv=map.uv,tint=map.tint,cutoff=map.cutoff};
    }
    public static void Save(StylizedTreeAuthoring tree)
    {
        if(!tree)throw new ArgumentNullException("tree");
        string path=PathFor(tree.species);STSMaterials.EnsureFolders(Folder);
        var asset=AssetDatabase.LoadAssetAtPath<STSDefaultTextureSet>(path);bool create=!asset;
        if(create&&AssetDatabase.LoadMainAssetAtPath(path))throw new InvalidOperationException("默认贴图路径已有其他类型的资源："+path);
        if(create)asset=UnityEngine.ScriptableObject.CreateInstance<STSDefaultTextureSet>();
        else Undo.RecordObject(asset,"更新物种默认贴图");
        asset.species=tree.species;asset.barkColor=tree.barkColor;asset.barkNormal=tree.barkNormal;asset.barkTint=tree.barkTint;
        asset.leaves=CopyMap(tree.leafCards);asset.twigs=CopyMap(tree.twigCards);asset.blooms=CopyMap(tree.bloomCards);asset.fruit=CopyMap(tree.fruitCards);
        asset.leafClusterSoftness=tree.leafClusterSoftness;asset.leafSmoothness=tree.leafSmoothness;asset.showTestTextures=tree.showTestTextures;
        if(create)AssetDatabase.CreateAsset(asset,path);else EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();EditorGUIUtility.PingObject(asset);
    }
    public static bool Apply(StylizedTreeAuthoring tree)
    {
        var asset=AssetDatabase.LoadAssetAtPath<STSDefaultTextureSet>(PathFor(tree.species));
        if(!asset||asset.species!=tree.species)return false;
        tree.barkColor=asset.barkColor;tree.barkNormal=asset.barkNormal;tree.barkTint=asset.barkTint;
        tree.leafCards=CopyMap(asset.leaves);tree.twigCards=CopyMap(asset.twigs);tree.bloomCards=CopyMap(asset.blooms);tree.fruitCards=CopyMap(asset.fruit);
        tree.leafClusterSoftness=asset.leafClusterSoftness;tree.leafSmoothness=asset.leafSmoothness;tree.showTestTextures=asset.showTestTextures;
        return true;
    }
}
#endif
