#ifndef GAFA_ILLUSTRATED_FOLIAGE_INCLUDED
#define GAFA_ILLUSTRATED_FOLIAGE_INCLUDED

// 移植自 TestGAFA/illustrated/src/materials.js 的 foliage() 与 vegetation.js 的配色：
// 平涂叶片（贴图近白，色相交给调色盘）+ 球面 / 朝上法线 + 三档卡通明暗 + 逆光透光 + 风动。
// glb 导出时丢失了每片叶子的实例数据，因此：
//   每片叶子的颜色抖动按顶点编号分组（一片叶 = 4 个顶点）；
//   每棵树的颜色按渲染器包围盒中心从调色盘里挑（一棵树是一个物体）；
//   草的“枯黄”按写实版顶点色的色相判断，保留原来的分布；
//   从网页版导出并烘焙的植被网格带有原项目的逐实例数据：顶点色 = 实例色（_GAFA_EXACT_COLOR），
//   TEXCOORD2 = (aNormalW, aClump + 1)，TEXCOORD3 = 实例中心（风动相位）；glb 植被没有这些数据时退回包围盒 / 调色盘。

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _Color;
    float4 _Color2;
    float4 _Color3;
    float  _PaletteGroup;
    float  _ItemVertices;
    float  _Jitter;
    float  _VertexColorStrength;
    float4 _DryColor;
    float  _DryHue;
    float  _Cutoff;
    float  _NormalBlend;
    float  _NormalLift;
    float  _ShadeEdge;
    float  _ShadeSoftness;
    float  _BrushNoise;
    float  _Rim;
    float  _Specular;
    float  _Translucency;
    float4 _SkyAmbient;
    float4 _GroundAmbient;
    float  _AmbientFromScene;
    float  _Wind;
    float  _Flutter;
    float  _WindSpeed;
    float  _InkId;
    float  _InkMode;
    float  _InkPerItem;
CBUFFER_END

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

#include "GafaIllustratedCommon.hlsl"

// ---------------------------------------------------------------- 风（原项目按实例中心取相位；没有实例中心时用顶点世界坐标）
float3 GafaIllWindOffset(float3 ip)
{
    float t = _Time.y * _WindSpeed;
    float ph = dot(ip, float3(0.37, 0.11, 0.53));
    float gust = 0.55 + 0.45 * sin(t * 0.43 + ip.x * 0.05 + ip.z * 0.035) * sin(t * 0.21 + 1.3);
    float3 o = float3(sin(t * 1.3 + ph), 0.3 * sin(t * 1.9 + ph * 1.7), cos(t * 1.1 + ph * 0.8)) * _Wind * gust;
    o += float3(sin(t * 6.1 + ip.x * 5.0 + ph * 3.1), sin(t * 7.3 + ip.y * 5.0 + ph), cos(t * 5.7 + ip.z * 5.0 + ph * 2.3)) * _Flutter * (0.5 + gust);
    return o;
}

// 草叶从根到梢的位置：glTFast 导入后 uv.y 是 1 - t（根 1、梢 0）
float GafaIllGrassT(float2 uv)
{
    return saturate(1.0 - uv.y);
}

// pivotOS：实例中心（物体空间，TEXCOORD3）；没有时为 0
float3 GafaIllFoliagePositionWS(float3 positionOS, float2 uv, float3 pivotOS)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 ip = dot(pivotOS, pivotOS) > 1e-8 ? TransformObjectToWorld(pivotOS) : positionWS;
#if defined(_GAFA_GRASS)
    positionWS += GafaIllWindOffset(ip) * pow(GafaIllGrassT(uv), 1.6);
#else
    positionWS += GafaIllWindOffset(ip);
#endif
    return positionWS;
}

float3 GafaIllRendererCenter()
{
    return (unity_RendererBounds_Min.xyz + unity_RendererBounds_Max.xyz) * 0.5;
}

// 原项目不翻转背面法线：叶片两面都用叶团的法线
// clump：烘焙的叶团数据，xyz = 叶团中心指向这片叶的方向（物体空间），w = 叶团编号；未烘焙为 0
float3 GafaIllFoliageNormalWS(float3 normalWS, float3 positionWS, float4 clump)
{
#if defined(_NORMALMODE_SPHERE)
    float3 dir = float3(0, 0, 0);
    if (dot(clump.xyz, clump.xyz) > 0.25)
    {
        dir = normalize(TransformObjectToWorldDir(clump.xyz));
    }
    else
    {
        float3 bmin = unity_RendererBounds_Min.xyz, bmax = unity_RendererBounds_Max.xyz;
        if (!any(bmax > bmin)) return normalWS;
        float3 center = (bmin + bmax) * 0.5;
        center.y = lerp(bmin.y, bmax.y, 0.4);
        dir = normalize(positionWS - center);
    }
    float3 sphereN = normalize(dir + float3(0, _NormalLift, 0));
    normalWS = normalize(lerp(normalWS, sphereN, _NormalBlend));
#elif defined(_NORMALMODE_UP)
    normalWS = normalize(lerp(normalWS, float3(0, 1, 0), _NormalBlend));
#endif
    return normalWS;
}

float GafaIllFoliageItem(uint vertexID)
{
    return floor((float)vertexID / max(_ItemVertices, 1.0));
}

// 墨线编号：同一叶团同一编号（原项目 mod(uInkId + vClump * 7, 2000) + 1）
float GafaIllFoliageInkId(float4 clump, float item)
{
    float k;
    if (clump.w > 0.5) k = clump.w;
    else if (_InkPerItem > 0.5) k = item;
    else k = floor(GafaIllHash12(GafaIllRendererCenter().xz * 0.37 + 0.11) * 997.0);
    return GafaIllMod(_InkId + k * 7.0, 2000.0) + 1.0;
}

// 顶点阶段：每片叶 / 每丛草的颜色（item = 顶点编号 / 每件顶点数）
// vc：写实版顶点色（实例色）；uv：网格 UV0
float3 GafaIllFoliageItemColor(uint vertexID, float4 vc, float2 uv)
{
#if defined(_GAFA_EXACT_COLOR)
    // 网页版导出的植被：顶点色就是原项目的实例色（草已乘上根梢渐变）
    return vc.rgb;
#endif
    float item = GafaIllFoliageItem(vertexID);

    // 调色盘：每个物体（每棵树）一种颜色；_PaletteGroup > 0 时每 N 件换一次（灌木丛）
    float3 bc = floor(GafaIllRendererCenter() * 4.0);
    float grp = _PaletteGroup > 0.5 ? floor(item / _PaletteGroup) : 0.0;
    float hp = GafaIllHash12(float2(dot(bc, float3(0.131, 0.071, 0.113)) + grp * 1.618, grp * 0.37 + 4.1));
    float3 c = hp < 1.0 / 3.0 ? _Color.rgb : (hp < 2.0 / 3.0 ? _Color2.rgb : _Color3.rgb);

#if defined(_GAFA_GRASS)
    // 写实版的枯草色相约 0.11–0.15，绿草约 0.19–0.27（线性空间）
    float3 vhsv = GafaIllRgbToHsv(max(vc.rgb, 0.0));
    float dry = step(0.05, vhsv.y) * step(vhsv.x, _DryHue);
    c = lerp(c, _DryColor.rgb, dry);
#endif

    // muted()：色相、饱和度、明度的小幅抖动
    float r1 = GafaIllHash12(float2(item * 0.173 + 1.3, 7.7));
    float r2 = GafaIllHash12(float2(item * 0.219 + 5.1, 3.3));
    float r3 = GafaIllHash12(float2(item * 0.311 + 9.7, 1.9));
    float3 hsv = GafaIllRgbToHsv(c);
    hsv.x = frac(hsv.x + (r1 * 2.0 - 1.0) * _Jitter * 0.25);
    hsv.y = saturate(hsv.y * lerp(0.9, 1.1, r2));
    hsv.z *= lerp(1.0 - _Jitter, 1.0 + _Jitter, r3);
    c = GafaIllHsvToRgb(hsv);

    c *= lerp(float3(1, 1, 1), vc.rgb, _VertexColorStrength);

#if defined(_GAFA_GRASS)
    // 原项目草叶的顶点色：根部深、叶尖浅
    float k = pow(GafaIllGrassT(uv), 0.8);
    c *= float3(0.5 + 0.54 * k, 0.52 + 0.51 * k, 0.45 + 0.45 * k);
#endif
    return c;
}

half GafaIllFoliageAlpha(float2 uv)
{
#if defined(_ALPHATEST_ON)
    half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a;
    clip(a - _Cutoff);
    return a;
#else
    return 1.0;
#endif
}

#endif
