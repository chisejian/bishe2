#ifndef GAFA_WEATHERED_INPUT_INCLUDED
#define GAFA_WEATHERED_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float4 _PaintColor;
    float  _PaintAmount;
    float  _Moss;
    float  _Streaks;
    float  _Dirt;
    float  _Scale;
    float  _Bump;
    float  _Seed;
    float4 _Rect;        // pavers 的场地范围 (x0, x1, z0, z1)，GAFA 场地坐标
    float  _Decal;
    float  _Roughness;
    float  _Metallic;
    float4 _DecalMap_ST;
CBUFFER_END

TEXTURE2D(_NoiseTex);   SAMPLER(sampler_NoiseTex);
TEXTURE2D(_DecalMap);   SAMPLER(sampler_DecalMap);

// 世界 → GAFA 场地坐标（由 GafaSiteSpace 组件设置；未设置时为全零矩阵，按单位矩阵处理）
float4x4 _GafaWorldToSite;

float3 GafaToSitePos(float3 positionWS)
{
    if (_GafaWorldToSite[3][3] == 0.0) return positionWS;
    return mul(_GafaWorldToSite, float4(positionWS, 1.0)).xyz;
}

float3 GafaToSiteNormal(float3 normalWS)
{
    if (_GafaWorldToSite[3][3] == 0.0) return normalWS;
    return normalize(mul((float3x3)_GafaWorldToSite, normalWS));
}

#endif
