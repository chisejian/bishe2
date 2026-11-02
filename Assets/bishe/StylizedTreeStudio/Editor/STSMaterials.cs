#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal static class STSMaterials
{
    const string Folder="Assets/StylizedTreeStudio/Generated/Materials";
    public static void EnsureFolders(string path)
    {
        string[] parts=path.Split('/');string current=parts[0];
        for(int i=1;i<parts.Length;i++){string next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,parts[i]);current=next;}
    }
    static Material Get(string name,Texture2D texture=null)
    {
        EnsureFolders(Folder);string path=Folder+"/"+name+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat)return mat;
        Shader shader=Shader.Find("StylizedTreeStudio/VertexColor");
        if(!shader)throw new InvalidOperationException("未找到插件自带着色器。请将整个 StylizedTreeStudio 文件夹完整导入，包括 Shaders 文件夹。");
        mat=new Material(shader){name=name,enableInstancing=true};
        mat.SetColor("_Color",Color.white);
        if(texture){mat.SetTexture("_MainTex",texture);mat.SetFloat("_UseTexture",1);}
        AssetDatabase.CreateAsset(mat,path);return mat;
    }
    public static void Ensure(StylizedTreeAuthoring t)
    {
        RenderPipelineAsset pipeline=GraphicsSettings.currentRenderPipeline;
        bool hdrp=pipeline&&pipeline.GetType().Name.IndexOf("HDRenderPipeline",StringComparison.OrdinalIgnoreCase)>=0;
        if(hdrp&&(!t.barkMaterial||!t.leafMaterial||(t.enableFlowers&&(!t.flowerMaterial||!t.flowerCenterMaterial))||(t.enableFruit&&!t.fruitMaterial)))
            throw new InvalidOperationException("默认材质支持 URP 和内置管线。当前为 HDRP，请先在材质栏指定兼容的材质。");
        if(!t.barkMaterial)t.barkMaterial=Get("STS_Bark");
        if(!t.leafMaterial)t.leafMaterial=Get("STS_Leaves");
        if(!t.flowerMaterial)t.flowerMaterial=Get("STS_Flowers");
        if(!t.flowerCenterMaterial)t.flowerCenterMaterial=Get("STS_Centers");
        if(!t.fruitMaterial)t.fruitMaterial=Get("STS_Fruit");
        if(t.flowerMode==STSFlowerMode.TextureCards&&t.flowerTexture&&t.flowerMaterial.shader&&t.flowerMaterial.shader.name=="StylizedTreeStudio/VertexColor")
        {
            string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(t.flowerTexture));
            if(string.IsNullOrEmpty(guid))throw new InvalidOperationException("请先将花朵贴图保存为项目中的 PNG 资源。");
            t.flowerMaterial=Get("STS_Cards_"+guid,t.flowerTexture);
        }
        // Changing back from cards must not leave the previous PNG on procedural petals.
        if(t.flowerMode==STSFlowerMode.ProceduralPetals&&t.flowerMaterial.shader&&t.flowerMaterial.shader.name=="StylizedTreeStudio/VertexColor"&&t.flowerMaterial.GetFloat("_UseTexture")>.5f)
            t.flowerMaterial=Get("STS_Flowers");
    }
    public static void ResetDefaults(StylizedTreeAuthoring t)
    {
        t.barkMaterial=null;t.leafMaterial=null;t.flowerMaterial=null;t.flowerCenterMaterial=null;t.fruitMaterial=null;Ensure(t);
    }
}
#endif
