#ifndef GAFA_ILLUSTRATED_PAINTED_INPUT_INCLUDED
#define GAFA_ILLUSTRATED_PAINTED_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float4 _PaintColor;
    float4 _MossColor;
    float  _PaintAmount;
    float  _Moss;
    float  _Streaks;
    float  _Dirt;
    float  _Scale;
    float  _Seed;
    float4 _Rect;        // pavers 的场地范围 (x0, x1, z0, z1)，GAFA 场地坐标
    float4 _TileSize;    // pavers 石板尺寸 (宽, 深)
    float  _Decal;
    float4 _DecalMap_ST;
    float  _ShadeEdge;
    float  _ShadeSoftness;
    float  _BrushNoise;
    float  _Rim;
    float  _Specular;
    float4 _SkyAmbient;
    float4 _GroundAmbient;
    float  _AmbientFromScene;
    float  _Opacity;
    float  _InkId;
    float  _InkMode;
CBUFFER_END

TEXTURE2D(_DecalMap); SAMPLER(sampler_DecalMap);

#endif
