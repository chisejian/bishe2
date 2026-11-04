#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal static class STSMaterials
{
 public const string Tests="Assets/StylizedTreeStudio/PaintTemplates/";
 public static void EnsureFolders(string path)
 {
  var parts=path.Split('/');string current=parts[0];
  for(int i=1;i<parts.Length;i++){string next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,parts[i]);current=next;}
 }
 static Texture2D Test(string file){return AssetDatabase.LoadAssetAtPath<Texture2D>(Tests+file+".png");}
 public static void Defaults(StylizedTreeAuthoring t)
 {
  if(t.leafCards==null)t.leafCards=new STSCardMap();if(t.twigCards==null)t.twigCards=new STSCardMap();if(t.bloomCards==null)t.bloomCards=new STSCardMap();if(t.fruitCards==null)t.fruitCards=new STSCardMap();
  if(t.showTestTextures)
  {
   if(!t.leafCards.color)t.leafCards.color=Test(t.growthForm==STSGrowthForm.Palm?"TEST_PalmFrond":t.growthForm==STSGrowthForm.ClimbingFern?"TEST_Fern":"TEST_LeafCluster");
   if(!t.twigCards.color)t.twigCards.color=Test("TEST_Twig");if(!t.bloomCards.color)t.bloomCards.color=Test("TEST_FlowerCluster");
  }
 }
 static Material Material(StylizedTreeAuthoring t,string part,Texture2D color,Texture2D normal,float cutoff,bool cards)
 {
  if(string.IsNullOrEmpty(t.materialSetId))t.materialSetId=Guid.NewGuid().ToString("N");
  string folder="Assets/StylizedTreeStudio/Generated/V05/"+t.materialSetId;EnsureFolders(folder);
  string path=folder+"/"+part+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
  bool urp=GraphicsSettings.currentRenderPipeline&&GraphicsSettings.currentRenderPipeline.GetType().Name.IndexOf("Universal",StringComparison.OrdinalIgnoreCase)>=0;
  Shader shader=Shader.Find(urp?(part=="Leaves"?"StylizedTreeStudio/URP Cluster Leaves":"StylizedTreeStudio/URP Cards"):"StylizedTreeStudio/VertexColor");
  if(!shader)throw new InvalidOperationException("插件着色器缺失，请完整复制 Shaders 文件夹。");
  if(part=="Leaves"&&urp&&ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("叶簇 Shader 编译失败，已停止切换材质；请查看 Console 的第一条 Shader 错误。");
  if(!mat){mat=new Material(shader){name="STS_"+part};AssetDatabase.CreateAsset(mat,path);}else if(mat.shader!=shader)mat.shader=shader;
  mat.SetTexture("_MainTex",color);mat.SetFloat("_UseTexture",color?1:0);mat.SetColor("_Color",Color.white);
  mat.SetFloat("_Cutoff",cards?cutoff:.001f);if(mat.HasProperty("_Cull"))mat.SetFloat("_Cull",cards?0:2);
  if(mat.HasProperty("_BumpMap"))mat.SetTexture("_BumpMap",normal);if(mat.HasProperty("_UseNormal"))mat.SetFloat("_UseNormal",normal?1:0);
  if(mat.HasProperty("_WindStrength"))mat.SetFloat("_WindStrength",cards?t.windStrength:0);
  if(part=="Leaves"&&urp)
  {
   mat.SetFloat("_ClusterBlend",t.leafClusterSoftness*(t.growthForm==STSGrowthForm.Tree||t.growthForm==STSGrowthForm.Shrub?1:.45f));
   mat.SetFloat("_Smoothness",t.leafSmoothness);mat.SetFloat("_BumpScale",.3f);mat.SetFloat("_TextureDetail",.58f);
   mat.SetFloat("_OcclusionStrength",.5f);mat.SetFloat("_Translucency",.18f);
   mat.SetVector("_LeafAverage",STSAlphaMask.Read(color).AverageColor(t.leafCards.uv,cutoff));
  }
  EditorUtility.SetDirty(mat);return mat;
 }
 public static void Ensure(StylizedTreeAuthoring t)
 {
  Defaults(t);
  if(!string.IsNullOrEmpty(t.materialSetId))foreach(var other in Resources.FindObjectsOfTypeAll<StylizedTreeAuthoring>())
   if(other&&other!=t&&other.materialSetId==t.materialSetId){t.materialSetId=Guid.NewGuid().ToString("N");break;}
  ConfigureLeafInput(t.leafCards);
  t.barkMaterial=Material(t,"Bark",t.barkColor,t.barkNormal,.001f,false);
  t.leafMaterial=Material(t,"Leaves",t.leafCards.color,t.leafCards.normal,t.leafCards.cutoff,true);
  t.flowerMaterial=Material(t,"Flowers",t.bloomCards.color,t.bloomCards.normal,t.bloomCards.cutoff,true);
  t.fruitMaterial=Material(t,"Fruit",t.fruitCards.color,t.fruitCards.normal,t.fruitCards.cutoff,true);
  t.flowerCenterMaterial=Material(t,"Twigs",t.twigCards.color,t.twigCards.normal,t.twigCards.cutoff,true);
 }
 public static void ConfigureLeafInput(STSCardMap map)
 {
  if(!map.color)return;var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(map.color)) as TextureImporter;if(!importer)return;
  bool changed=importer.textureType!=TextureImporterType.Default||importer.alphaSource!=TextureImporterAlphaSource.FromInput||!importer.alphaIsTransparency||!importer.sRGBTexture||importer.wrapMode!=TextureWrapMode.Clamp||!importer.mipmapEnabled||!importer.mipMapsPreserveCoverage||Mathf.Abs(importer.alphaTestReferenceValue-map.cutoff)>.001f||importer.filterMode!=FilterMode.Trilinear;
  if(changed)
  {
   importer.textureType=TextureImporterType.Default;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;importer.sRGBTexture=true;
   importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=true;importer.alphaTestReferenceValue=map.cutoff;importer.filterMode=FilterMode.Trilinear;importer.SaveAndReimport();
  }
  if(map.normal)
  {
   var normal=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(map.normal)) as TextureImporter;
   if(normal&&(normal.textureType!=TextureImporterType.NormalMap||normal.wrapMode!=TextureWrapMode.Clamp))Normal(map.normal,true);
  }
 }
 public static void ConfigureTextures(StylizedTreeAuthoring t)
 {
  foreach(var map in new[]{t.leafCards,t.twigCards,t.bloomCards,t.fruitCards})
  {
   if(map.color)
   {
    var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(map.color)) as TextureImporter;
    if(importer){importer.textureType=TextureImporterType.Default;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;importer.sRGBTexture=true;importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=true;importer.alphaTestReferenceValue=map.cutoff;importer.SaveAndReimport();}
   }
   Normal(map.normal,true);
  }
  if(t.barkColor){var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t.barkColor)) as TextureImporter;if(importer){importer.sRGBTexture=true;importer.wrapMode=TextureWrapMode.Repeat;importer.SaveAndReimport();}}
  Normal(t.barkNormal,false);
 }
 static void Normal(Texture2D normal,bool clamp)
 {
  if(!normal)return;var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) as TextureImporter;
  if(importer){importer.textureType=TextureImporterType.NormalMap;importer.wrapMode=clamp?TextureWrapMode.Clamp:TextureWrapMode.Repeat;importer.SaveAndReimport();}
 }
 public static bool ReferenceAtlas(StylizedTreeAuthoring t)
 {
  var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/BK/PureNature_Jungle/Models/Trees/Kapokier/KapokierLeaves_a.png");if(!atlas)return false;
  t.leafCards.color=atlas;t.leafCards.uv=new Rect(0,0,.5f,.5f);t.leafCards.tint=new Color(.38f,.55f,.32f,1);
  t.leafCards.normal=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/BK/PureNature_Jungle/Models/Trees/Kapokier/KapokierLeaves_n.png");
  t.twigCards.color=atlas;t.twigCards.uv=new Rect(0,.5f,.25f,.25f);return true;
 }
}
#endif
