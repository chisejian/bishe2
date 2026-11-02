Shader "StylizedTreeStudio/URP Cards"
{
 Properties
 {
  _MainTex("颜色与透明度 / Color RGBA",2D)="white"{}
  [Normal] _BumpMap("法线 / Normal",2D)="bump"{}
  _BumpScale("法线强度 / Normal Strength",Range(0,2))=1
  _UseNormal("使用法线 / Use Normal",Float)=0
  _UseTexture("使用颜色贴图 / Use Texture",Float)=1
  _Color("颜色 / Tint",Color)=(1,1,1,1)
  _Cutoff("透明裁切 / Alpha Cutoff",Range(0,1))=.35
  [Enum(UnityEngine.Rendering.CullMode)] _Cull("剔除 / Cull",Float)=0
  _WindStrength("风摆幅度 / Wind",Range(0,.6))=.12
  _WindSpeed("风速 / Speed",Float)=1
 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"}
  Cull [_Cull] ZWrite On
  HLSLINCLUDE
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
  #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
  TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
  TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
  CBUFFER_START(UnityPerMaterial)
   float4 _MainTex_ST,_Color;
   float _Cutoff,_BumpScale,_UseNormal,_UseTexture,_WindStrength,_WindSpeed;
  CBUFFER_END
  struct A{float4 p:POSITION;float3 n:NORMAL;float4 tangent:TANGENT;float2 uv:TEXCOORD0;float2 wind:TEXCOORD1;float3 pivot:TEXCOORD2;float4 color:COLOR;};
  struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;float3 n:TEXCOORD2;float4 tangent:TEXCOORD3;float4 color:COLOR;float fog:TEXCOORD4;};
  float3 Wind(A a)
  {float phase=_Time.y*_WindSpeed+a.pivot.x*.37+a.pivot.z*.29;float amount=sin(phase)*_WindStrength*a.wind.x;return a.p.xyz+float3(amount,0,amount*.43);}
  V Vert(A a)
  {
   V o;float3 pos=Wind(a);o.world=TransformObjectToWorld(pos);o.p=TransformWorldToHClip(o.world);
   VertexNormalInputs ni=GetVertexNormalInputs(a.n,a.tangent);o.n=ni.normalWS;o.tangent=float4(ni.tangentWS,a.tangent.w*GetOddNegativeScale());
   o.uv=a.uv*_MainTex_ST.xy+_MainTex_ST.zw;o.color=a.color*_Color;o.fog=ComputeFogFactor(o.p.z);return o;
  }
  half4 Surface(V i)
  {half4 c=lerp(half4(1,1,1,1),SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv),saturate(_UseTexture))*i.color;clip(c.a-_Cutoff);return c;}
  ENDHLSL
  Pass
  {
   Name "Forward" Tags{"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   half4 Frag(V i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
   {
    half4 c=Surface(i);float sign=IS_FRONT_VFACE(face,1,-1);float3 n=normalize(i.n)*sign;
    float3 tangent=normalize(i.tangent.xyz),bitangent=normalize(cross(n,tangent))*i.tangent.w;
    float3 map=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv),_BumpScale);
    n=normalize(lerp(n,tangent*map.x+bitangent*map.y+n*map.z,saturate(_UseNormal)));
    Light light=GetMainLight(TransformWorldToShadowCoord(i.world));float diffuse=floor(saturate(dot(n,light.direction)*.5+.5)*3)/3;
    float3 ambient=max(SampleSH(n)*.45,float3(.18,.18,.18));
    return half4(MixFog(c.rgb*(ambient+light.color*(.3+.7*diffuse)*light.shadowAttenuation),i.fog),1);
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
   half4 ShadowFrag(V i):SV_Target{Surface(i);return 0;}
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask 0
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment DepthFrag
   half4 DepthFrag(V i):SV_Target{Surface(i);return 0;}
   ENDHLSL
  }
 }
 Fallback Off
}
