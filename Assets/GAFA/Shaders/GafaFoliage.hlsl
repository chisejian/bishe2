#ifndef GAFA_FOLIAGE_INCLUDED
#define GAFA_FOLIAGE_INCLUDED

// 移植自 TestGAFA/src/materials.js 的 foliage()：风动 + 球面法线 + 透光，另加调色参数。
// glb 导出时丢失了每片叶子的实例数据，因此：
//   风动相位改用顶点世界坐标；球面法线改用渲染器包围盒中心（每棵树是独立物体）。

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseColor;
    float  _HueShift;
    float  _Saturation;
    float  _Brightness;
    float  _VertexColorStrength;
    float4 _RootTint;
    float4 _TipTint;
    float  _Cutoff;
    float  _Roughness;
    float  _Translucency;
    float  _Wind;
    float  _Flutter;
    float  _WindSpeed;
    float  _NormalBlend;
CBUFFER_END

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

// ---------------------------------------------------------------- 风
float3 GafaWindOffset(float3 positionWS)
{
    float t = _Time.y * _WindSpeed;
    float3 ip = positionWS;
    float ph = dot(ip, float3(0.37, 0.11, 0.53));
    float gust = 0.55 + 0.45 * sin(t * 0.43 + ip.x * 0.05 + ip.z * 0.035) * sin(t * 0.21 + 1.3);
    float3 o = float3(sin(t * 1.3 + ph), 0.3 * sin(t * 1.9 + ph * 1.7), cos(t * 1.1 + ph * 0.8)) * _Wind * gust;
    o += float3(sin(t * 6.1 + ip.x * 5.0 + ph * 3.1), sin(t * 7.3 + ip.y * 5.0 + ph), cos(t * 5.7 + ip.z * 5.0 + ph * 2.3)) * _Flutter * (0.5 + gust);
    return o;
}

float3 GafaFoliagePositionWS(float3 positionOS, float2 uv)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
#if defined(_GAFA_GRASS)
    positionWS += GafaWindOffset(positionWS) * pow(saturate(uv.y), 1.6);
#else
    positionWS += GafaWindOffset(positionWS);
#endif
    return positionWS;
}

float3 GafaFoliageNormalWS(float3 normalWS, float3 positionWS)
{
#if defined(_NORMALMODE_SPHERE)
    float3 bmin = unity_RendererBounds_Min.xyz, bmax = unity_RendererBounds_Max.xyz;
    if (any(bmax > bmin))
    {
        float3 center = (bmin + bmax) * 0.5;
        center.y = lerp(bmin.y, bmax.y, 0.4);
        // 与原项目一致：从树冠中心向外，再略微偏上
        float3 sphereN = normalize(normalize(positionWS - center) + float3(0, 0.4, 0));
        normalWS = normalize(lerp(normalWS, sphereN, _NormalBlend));
    }
#elif defined(_NORMALMODE_UP)
    normalWS = normalize(lerp(normalWS, float3(0, 1, 0), _NormalBlend));
#endif
    return normalWS;
}

// ---------------------------------------------------------------- 颜色
float3 GafaRgbToHsv(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 GafaHsvToRgb(float3 c)
{
    float3 p = abs(frac(c.xxx + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return c.z * lerp(float3(1, 1, 1), saturate(p - 1.0), c.y);
}

// tex：贴图颜色；vc：顶点颜色（原项目每片叶子 / 每丛草的随机色）
float3 GafaFoliageAlbedo(float3 tex, float3 vc, float2 uv)
{
    float3 c = tex * lerp(float3(1, 1, 1), vc, _VertexColorStrength);
#if defined(_GAFA_GRASS)
    c *= lerp(_RootTint.rgb, _TipTint.rgb, saturate(uv.y));
#endif
    float3 hsv = GafaRgbToHsv(max(c, 0.0));
    hsv.x = frac(hsv.x + _HueShift);
    hsv.y = saturate(hsv.y * _Saturation);
    c = GafaHsvToRgb(hsv);
    return c * _BaseColor.rgb * _Brightness;
}

half GafaFoliageAlpha(float2 uv)
{
#if defined(_ALPHATEST_ON)
    half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
    clip(a - _Cutoff);
    return a;
#else
    return 1.0;
#endif
}

#endif
