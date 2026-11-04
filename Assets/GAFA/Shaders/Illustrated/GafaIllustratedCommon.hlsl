#ifndef GAFA_ILLUSTRATED_COMMON_INCLUDED
#define GAFA_ILLUSTRATED_COMMON_INCLUDED

// GAFA 插画版公共部分：噪声采样、场地坐标、哈希与颜色工具。卡通光照见 GafaIllustratedToon.hlsl。

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

// 世界 → GAFA 场地坐标（GafaSiteSpace 组件设置；未设置时为全零矩阵，按单位矩阵处理）
float4x4 _GafaWorldToSite;

float3 GafaIllToSitePos(float3 positionWS)
{
    if (_GafaWorldToSite[3][3] == 0.0) return positionWS;
    return mul(_GafaWorldToSite, float4(positionWS, 1.0)).xyz;
}

float3 GafaIllToSiteNormal(float3 normalWS)
{
    if (_GafaWorldToSite[3][3] == 0.0) return normalWS;
    return normalize(mul((float3x3)_GafaWorldToSite, normalWS));
}

float4 GafaIllNoise(float2 uv)
{
    return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv);
}

float GafaIllHash12(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// GLSL mod（结果与 y 同号）
float GafaIllMod(float x, float y)
{
    return x - y * floor(x / y);
}

float GafaIllBand(float x, float e, float w)
{
    return smoothstep(e - w, e + w, x);
}

// 等值线：f 穿过 t 的地方画一条约 wpx 像素宽的细线
float GafaIllIsoLine(float f, float t, float wpx)
{
    float fw = max(fwidth(f), 1e-5);
    return 1.0 - smoothstep(0.5 * wpx, 1.5 * wpx, abs(f - t) / fw);
}

float3 GafaIllRgbToHsv(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 GafaIllHsvToRgb(float3 c)
{
    float3 p = abs(frac(c.xxx + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return c.z * lerp(float3(1, 1, 1), saturate(p - 1.0), c.y);
}

#endif
