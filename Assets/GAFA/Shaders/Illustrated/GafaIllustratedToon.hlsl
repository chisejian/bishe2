#ifndef GAFA_ILLUSTRATED_TOON_INCLUDED
#define GAFA_ILLUSTRATED_TOON_INCLUDED

// GAFA 插画版卡通光照，移植自 TestGAFA/illustrated/src/materials.js 的 toonLight()。
// 引用本文件之前，材质 CBUFFER 里需声明：
//   _ShadeEdge _ShadeSoftness _BrushNoise _Rim _Specular（对应 uToon.xyzw 与 uSpec）
//   _SkyAmbient _GroundAmbient _AmbientFromScene
//   植被另需 _Translucency，并在引用前 #define GAFA_TOON_FOLIAGE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "GafaIllustratedCommon.hlsl"

float4 GafaIllShadowCoord(float3 positionWS)
{
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
    return ComputeScreenPos(TransformWorldToHClip(positionWS));
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    return TransformWorldToShadowCoord(positionWS);
#else
    return float4(0, 0, 0, 0);
#endif
}

// 卡通光照：两段式明暗 + 笔触噪声扰动的明暗交界 + 中性偏暖的半球环境光 + 边缘光 + 硬高光
// 植被（GAFA_TOON_FOLIAGE）用三档：暗部 / 中间调 / 偏暖黄绿的亮面，另加逆光透光
// albedo：线性反照率；N：着色法线；sitePos：场地坐标（笔触噪声用）
float3 GafaToonLighting(float3 albedo, float3 N, float3 positionWS, float3 sitePos)
{
    float3 V = GetWorldSpaceNormalizeViewDir(positionWS);
    Light light = GetMainLight(GafaIllShadowCoord(positionWS), positionWS, half4(1, 1, 1, 1));
    float3 L = light.direction;
    float3 Lc = light.color;
    float sh = light.shadowAttenuation;

    float ndl = dot(N, L);
    float brush = GafaIllNoise(sitePos.xz * 0.23 + sitePos.y * float2(0.17, -0.13)).g - 0.5;
    float shT = smoothstep(0.35, 0.65, sh + brush * _BrushNoise * 0.5);

    float3 hemi = lerp(_GroundAmbient.rgb, _SkyAmbient.rgb, smoothstep(-0.6, 0.8, N.y));
    float3 amb = lerp(hemi, SampleSH(N), _AmbientFromScene);
    float fres = pow(1.0 - saturate(dot(N, V)), 4.0);

#ifdef GAFA_TOON_FOLIAGE
    float e = ndl + brush * _BrushNoise;
    float mid = smoothstep(_ShadeEdge - _ShadeSoftness, _ShadeEdge + _ShadeSoftness, e) * shT;
    float top = smoothstep(0.5 - _ShadeSoftness, 0.5 + _ShadeSoftness, e) * shT;
    float3 lightCol = amb + Lc * (0.55 * mid + 0.4 * top);
    lightCol *= lerp(float3(1, 1, 1), float3(1.07, 1.05, 0.8), top);
    float lit = mid;
#else
    float lit = smoothstep(_ShadeEdge - _ShadeSoftness, _ShadeEdge + _ShadeSoftness, ndl + brush * _BrushNoise) * shT;
    float body = 0.84 + 0.16 * smoothstep(0.15, 0.95, ndl);
    float3 lightCol = Lc * (lit * body) + amb;
#endif
    lightCol += Lc * fres * _Rim * (0.25 + 0.75 * lit);

#ifdef GAFA_TOON_FOLIAGE
    float bl = pow(max(dot(-V, L), 0.0), 3.0);
    lightCol += Lc * _Translucency * bl * shT * float3(1.0, 1.04, 0.6);
#endif

    float3 H = normalize(L + V);
    float sp = smoothstep(0.965, 0.985, dot(N, H)) * lit * _Specular;
    return albedo * lightCol + Lc * sp;
}

#endif
