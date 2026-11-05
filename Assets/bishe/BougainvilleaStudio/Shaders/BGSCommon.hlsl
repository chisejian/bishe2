#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
CBUFFER_START(UnityPerMaterial)
float4 _Color;
float _Cutoff,_Seed,_WindStrength,_PatternSize;
CBUFFER_END
struct A {float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;float2 wind:TEXCOORD1;float4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;float3 normal:TEXCOORD2;float4 color:COLOR;float fog:TEXCOORD3;float solid:TEXCOORD4;float3 local:TEXCOORD5;float3 localNormal:TEXCOORD6;UNITY_VERTEX_INPUT_INSTANCE_ID};
V Vert(A a)
{
 UNITY_SETUP_INSTANCE_ID(a);V o;UNITY_TRANSFER_INSTANCE_ID(a,o);
 float3 pos=a.p.xyz;pos.x+=sin(_Time.y+pos.y*.7+pos.x*.2)*_WindStrength*a.wind.x;
 o.world=TransformObjectToWorld(pos);o.p=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(a.n);o.uv=a.uv;o.color=a.color*_Color;o.fog=ComputeFogFactor(o.p.z);o.solid=a.wind.y;o.local=a.p.xyz;o.localNormal=a.n;return o;
}
half4 Surface(V i)
{
 #if BGS_BODY
 if(i.solid>.5)return half4(i.color.rgb,1);
 float3 axes=abs(i.localNormal);
 float2 projected=axes.x>axes.y&&axes.x>axes.z?i.local.yz:axes.y>axes.z?i.local.xz:i.local.xy;
 projected/=max(.05,_PatternSize);
 float2 tile=floor(projected),q=frac(projected);float h=frac(sin(dot(tile,float2(127.1,311.7))+_Seed*.13)*43758.5453);
 float2 dx=ddx(projected)*.494,dy=ddy(projected)*.494;
 if(h>.5){q=1-q;dx=-dx;dy=-dy;}
 float2 uv=float2((h>.5?.5:0)+.003+q.x*.494,.503+q.y*.494);
 half4 c=SAMPLE_TEXTURE2D_GRAD(_MainTex,sampler_MainTex,uv,dx,dy)*i.color;c.a=1;return c;
 #else
 half4 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*i.color;clip(c.a-_Cutoff);return c;
 #endif
}
half4 Frag(V i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
{
 UNITY_SETUP_INSTANCE_ID(i);half4 c=Surface(i);float3 n=normalize(i.normal)*IS_FRONT_VFACE(face,1,-1);
 #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
 float4 shadowCoord=ComputeScreenPos(TransformWorldToHClip(i.world));
 #else
 float4 shadowCoord=TransformWorldToShadowCoord(i.world);
 #endif
 Light light=GetMainLight(shadowCoord);float diffuse=saturate(dot(n,light.direction)*.5+.5);diffuse=floor(diffuse*3)/3;
 float3 ambient=max(SampleSH(n)*.5,float3(.22,.22,.22));return half4(MixFog(c.rgb*(ambient+light.color*(.35+.65*diffuse)*light.shadowAttenuation),i.fog),1);
}
half4 NormalsFrag(V i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
{
 UNITY_SETUP_INSTANCE_ID(i);
 #if !BGS_BODY
 Surface(i);
 #endif
 float3 normalWS=normalize(i.normal)*IS_FRONT_VFACE(face,1,-1);
 #if defined(_GBUFFER_NORMALS_OCT)
 float2 oct=PackNormalOctQuadEncode(normalWS);
 return half4(PackFloat2To888(saturate(oct*.5+.5)),0);
 #else
 return half4(normalWS,0);
 #endif
}
