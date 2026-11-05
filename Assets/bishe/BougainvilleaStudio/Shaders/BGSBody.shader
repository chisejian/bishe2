Shader "BougainvilleaStudio/Body"
{
 Properties {_MainTex("花叶图集 / Atlas",2D)="white"{} _Color("颜色 / Tint",Color)=(1,1,1,1) _Cutoff("透明裁切 / Cutoff",Range(.01,.9))=.3 _Seed("Seed",Float)=42 _PatternSize("花叶图案尺寸 / Pattern Size",Float)=.65 _WindStrength("风摆 / Wind",Range(0,.2))=.015}
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Cull Off ZWrite On
  HLSLINCLUDE
  #define BGS_BODY 1
  #include "BGSCommon.hlsl"
  ENDHLSL
  Pass
  {
   Name "Forward" Tags{"LightMode"="UniversalForwardOnly"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_instancing
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   ENDHLSL
  }
  Pass
  {
   Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ColorMask 0
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex ShadowVert
   #pragma fragment ShadowFrag
   #pragma multi_compile_instancing
   #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
   float3 _LightDirection,_LightPosition;
   V ShadowVert(A a)
   {
    V o=Vert(a);float3 direction=_LightDirection;
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    direction=normalize(_LightPosition-o.world);
    #endif
    o.p=TransformWorldToHClip(ApplyShadowBias(o.world,normalize(o.normal),direction));
    #if UNITY_REVERSED_Z
    o.p.z=min(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
    #else
    o.p.z=max(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
    #endif
    return o;
   }
   half4 ShadowFrag(V i):SV_Target
   {
    #if !BGS_BODY
    Surface(i);
    #endif
    return 0;
   }
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask 0
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment DepthFrag
   #pragma multi_compile_instancing
   half4 DepthFrag(V i):SV_Target
   {
    #if !BGS_BODY
    Surface(i);
    #endif
    return 0;
   }
   ENDHLSL
  }
  Pass
  {
   Name "DepthNormalsOnly" Tags{"LightMode"="DepthNormalsOnly"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment NormalsFrag
   #pragma multi_compile_instancing
   #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
   ENDHLSL
  }
 }
 Fallback Off
}
