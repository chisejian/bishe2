Shader "StylizedTreeStudio/URP Cluster Leaves"
{
 Properties
 {
  _MainTex("叶簇颜色与透明度 / Leaf RGBA",2D)="white"{}
  [Normal] _BumpMap("细节法线（可选） / Optional Normal",2D)="bump"{}
  _BumpScale("细节法线强度 / Detail Normal",Range(0,1))=.3
  _UseNormal("使用细节法线 / Use Normal",Float)=0
  _UseTexture("使用颜色图 / Use Texture",Float)=1
  _Color("整体颜色 / Tint",Color)=(1,1,1,1)
  _Cutoff("透明裁切 / Alpha Cutoff",Range(0,1))=.35
  [Enum(UnityEngine.Rendering.CullMode)] _Cull("剔除 / Cull",Float)=0
  _ClusterBlend("团簇明暗 / Cluster Shading",Range(0,1))=.9
  _Smoothness("柔和反光 / Smoothness",Range(0,.6))=.24
  _TextureDetail("叶图细节对比 / Texture Contrast",Range(0,1))=.58
  _OcclusionStrength("内部遮蔽 / Occlusion",Range(0,1))=.5
  _Translucency("柔和透光 / Translucency",Range(0,1))=.18
  [HideInInspector] _LeafAverage("平均颜色 / Mean Color",Vector)=(.4,.6,.25,1)
  _WindStrength("风摆幅度 / Wind",Range(0,.6))=.12
  _WindSpeed("风速 / Speed",Float)=1
 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" "UniversalMaterialType"="Lit"}
  Cull [_Cull] ZWrite On
  HLSLINCLUDE
  #pragma target 3.5
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
  #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
  TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
  TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
  CBUFFER_START(UnityPerMaterial)
   float4 _MainTex_ST,_Color,_LeafAverage;
   float _Cutoff,_BumpScale,_UseNormal,_UseTexture,_WindStrength,_WindSpeed;
   float _ClusterBlend,_Smoothness,_TextureDetail,_OcclusionStrength,_Translucency;
  CBUFFER_END
  struct A
  {
   float4 p:POSITION;float3 n:NORMAL;float4 tangent:TANGENT;float2 uv:TEXCOORD0;
   float2 wind:TEXCOORD1;float3 pivot:TEXCOORD2;float3 volume:TEXCOORD3;float2 lighting:TEXCOORD4;float4 color:COLOR;
  };
  struct V
  {
   float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;float3 n:TEXCOORD2;
   float4 tangent:TEXCOORD3;float4 color:COLOR;float fog:TEXCOORD4;float3 volume:TEXCOORD5;float2 lighting:TEXCOORD6;
  };
  V Vert(A a)
  {
   V o;float phase=_Time.y*_WindSpeed+a.pivot.x*.37+a.pivot.z*.29;
   float amount=sin(phase)*_WindStrength*a.wind.x;
   o.world=TransformObjectToWorld(a.p.xyz+float3(amount,0,amount*.43));o.p=TransformWorldToHClip(o.world);
   VertexNormalInputs ni=GetVertexNormalInputs(a.n,a.tangent);o.n=ni.normalWS;o.tangent=float4(ni.tangentWS,a.tangent.w*GetOddNegativeScale());
   o.volume=TransformObjectToWorldNormal(a.volume);o.lighting=a.lighting;
   o.uv=a.uv*_MainTex_ST.xy+_MainTex_ST.zw;o.color=a.color;o.fog=ComputeFogFactor(o.p.z);return o;
  }
  half4 LeafSurface(V i)
  {
   half4 tex=lerp(half4(1,1,1,1),SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv),saturate(_UseTexture));
   // AO never enters alpha clipping. Transparent holes remain identical to the
   // original card shader and to the flower-attachment CPU alpha test.
   clip(tex.a*_Color.a-_Cutoff);
   half3 mean=_LeafAverage.rgb;
   #if !defined(UNITY_COLORSPACE_GAMMA)
   mean=SRGBToLinear(mean);
   #endif
   return half4(lerp(mean,tex.rgb,_TextureDetail)*i.color.rgb*_Color.rgb,tex.a);
  }
  float3 ClusterNormal(V i,float facing)
  {
   float3 plane=normalize(i.n)*facing;
   float3 volume=dot(i.volume,i.volume)>.001?normalize(i.volume):plane;
   return normalize(lerp(plane,volume,saturate(_ClusterBlend*i.lighting.y)));
  }
  ENDHLSL
  Pass
  {
   Name "ForwardLit" Tags{"LightMode"="UniversalForwardOnly"}
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #pragma multi_compile _ _FORWARD_PLUS
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
   #pragma multi_compile_fog
   half4 Frag(V i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
   {
    half4 c=LeafSurface(i);float3 n=ClusterNormal(i,IS_FRONT_VFACE(face,1,-1));
    float3 tangent=i.tangent.xyz-n*dot(i.tangent.xyz,n);
    if(dot(tangent,tangent)<.001)tangent=cross(n,abs(n.y)<.9?float3(0,1,0):float3(1,0,0));
    tangent=normalize(tangent);float3 bitangent=normalize(cross(n,tangent))*i.tangent.w;
    half3 detail=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv),_BumpScale);
    n=normalize(lerp(n,tangent*detail.x+bitangent*detail.y+n*detail.z,saturate(_UseNormal)));
    half ao=lerp(1,saturate(i.lighting.x),_OcclusionStrength);
    // Broad upper/lower color grading follows crown volume, not card rotation.
    half upper=saturate(n.y*.5+.5);c.rgb*=lerp(half3(.76,.84,.73),half3(1.06,1.10,.97),upper);
    InputData inputData=(InputData)0;
    inputData.positionWS=i.world;inputData.normalWS=n;inputData.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.world);
    inputData.shadowCoord=TransformWorldToShadowCoord(i.world);inputData.fogCoord=i.fog;
    inputData.vertexLighting=VertexLighting(i.world,n);inputData.bakedGI=SampleSH(n);
    inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.p);inputData.shadowMask=half4(1,1,1,1);
    SurfaceData surface=(SurfaceData)0;
    surface.albedo=c.rgb;surface.metallic=0;surface.specular=half3(.04,.04,.04);surface.smoothness=_Smoothness;
    surface.normalTS=half3(0,0,1);surface.occlusion=ao;surface.alpha=1;
    Light mainLight=GetMainLight(inputData.shadowCoord);
    half through=pow(saturate(dot(-inputData.viewDirectionWS,mainLight.direction+n*.25)),2);
    surface.emission=c.rgb*mainLight.color*through*_Translucency*lerp(.35,1,mainLight.shadowAttenuation);
    half4 lit=UniversalFragmentPBR(inputData,surface);
    return half4(MixFog(lit.rgb,i.fog),1);
   }
   ENDHLSL
  }
  Pass
  {
   Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ColorMask 0
   HLSLPROGRAM
   #pragma vertex ShadowVert
   #pragma fragment ShadowFrag
   #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
   float3 _LightDirection,_LightPosition;
   V ShadowVert(A a)
   {
    V o=Vert(a);float3 direction=_LightDirection;
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    direction=normalize(_LightPosition-o.world);
    #endif
    o.p=TransformWorldToHClip(ApplyShadowBias(o.world,normalize(o.n),direction));
    #if UNITY_REVERSED_Z
    o.p.z=min(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
    #else
    o.p.z=max(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
    #endif
    return o;
   }
   half4 ShadowFrag(V i):SV_Target{LeafSurface(i);return 0;}
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask 0
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment DepthFrag
   half4 DepthFrag(V i):SV_Target{LeafSurface(i);return 0;}
   ENDHLSL
  }
  Pass
  {
   Name "DepthNormals" Tags{"LightMode"="DepthNormalsOnly"}
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment NormalsFrag
   #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
   half4 NormalsFrag(V i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
   {
    LeafSurface(i);float3 n=ClusterNormal(i,IS_FRONT_VFACE(face,1,-1));
    #if defined(_GBUFFER_NORMALS_OCT)
    float2 oct=PackNormalOctQuadEncode(n);return half4(PackFloat2To888(saturate(oct*.5+.5)),0);
    #else
    return half4(n,0);
    #endif
   }
   ENDHLSL
  }
 }
 Fallback Off
}
