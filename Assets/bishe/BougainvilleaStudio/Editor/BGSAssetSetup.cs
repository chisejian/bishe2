#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
namespace BougainvilleaStudio
{
    [InitializeOnLoad] internal static class BGSAssetSetup
    {
        static BGSAssetSetup(){EditorApplication.delayCall+=Configure;}
        static void Configure()
        {
            string path=BGSBuilder.Folder+"/Textures/BougainvilleaAtlas.png";var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(!importer||importer.userData=="BGS_DefaultAtlas_1")return;
            importer.textureType=TextureImporterType.Default;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;
            importer.sRGBTexture=true;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;
            importer.npotScale=TextureImporterNPOTScale.ToNearest;importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Compressed;
            importer.userData="BGS_DefaultAtlas_1";importer.SaveAndReimport();
        }
    }
}
#endif
