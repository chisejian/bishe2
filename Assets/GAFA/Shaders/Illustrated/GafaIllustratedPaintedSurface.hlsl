#ifndef GAFA_ILLUSTRATED_PAINTED_SURFACE_INCLUDED
#define GAFA_ILLUSTRATED_PAINTED_SURFACE_INCLUDED

// 移植自 TestGAFA/illustrated/src/materials.js 的 painted()（P_ALBEDO）：
// 低饱和、低对比的手绘色块；油漆剥落少而大、边缘一圈浅色；苔藓是硬边色块；细节只靠石板缝、裂缝几条细线。
// 噪声贴图 _NoiseTex 与写实版共用（R 低频 fbm，G 高频 fbm，B Worley F1，A 中频 fbm）。

#include "GafaIllustratedCommon.hlsl"

float4 GafaIllTriN(float3 p, float3 n, float s)
{
    float3 w = pow(abs(n), 6.0);
    w /= (w.x + w.y + w.z);
    return GafaIllNoise(p.zy * s) * w.x + GafaIllNoise(p.xz * s + 0.37) * w.y + GafaIllNoise(p.xy * s + 0.71) * w.z;
}

// sitePos / siteN：GAFA 场地坐标下的位置与法线；uv：网格 UV0（glTFast 导入后 v 已翻转）
float3 GafaIllPaintedAlbedo(float3 sitePos, float3 siteN, float2 uv)
{
    float3 wN = normalize(siteN);

#if defined(_KIND_PLAIN)
    // 纯色卡通材质（toon()）：橡胶、玻璃
    return _BaseColor.rgb;
#else
    float3 wp = sitePos * _Scale + _Seed;
    float4 nA = GafaIllTriN(wp, wN, 0.11);
    float4 nB = GafaIllTriN(wp, wN, 0.55);
    float4 nC = GafaIllTriN(wp, wN, 2.7);
    float upF = smoothstep(0.5, 0.95, wN.y);
    float vertF = 1.0 - abs(wN.y);
    float hcoord = lerp(sitePos.x, sitePos.z, abs(wN.x)) * _Scale;
    float strokeH = GafaIllNoise(float2(hcoord * 0.45 + _Seed, sitePos.y * 2.6 * _Scale)).a;
    float strokeV = GafaIllNoise(float2(hcoord * 2.2, sitePos.y * 0.35 + _Seed)).a;
    float stroke = lerp(strokeH, strokeV, 0.35) - 0.5;
    float patchA = GafaIllBand(nA.r, 0.5, 0.05);
    float patchB = GafaIllBand(nB.g, 0.53, 0.04);
    float stA = GafaIllNoise(float2(hcoord * 0.31 + _Seed, sitePos.y * 0.021)).g;
    float stB = GafaIllNoise(float2(hcoord * 1.17, sitePos.y * 0.05 + _Seed)).r;
    float streak = GafaIllBand(stA * 0.75 + stB * 0.45, 0.72, 0.06) * vertF * _Streaks;
    float gl1 = 1.0 - smoothstep(0.0, 0.8, sitePos.y);
    float gl2 = (1.0 - smoothstep(2.4, 2.9, sitePos.y)) * step(2.36, sitePos.y);
    float grime = max(gl1, gl2) * _Dirt;
    float mossGate = max(upF, max(grime, streak * 0.5));
    float mossBias = 0.0;
    float3 baseCol = _BaseColor.rgb;
    float3 wCol = baseCol;

    #if defined(_KIND_RUST)
        float rn = nB.r * 0.6 + nC.g * 0.5;
        wCol = baseCol * (0.88 + 0.2 * GafaIllBand(rn, 0.5, 0.08));
        #ifdef _USE_PAINT
            float wPaintM = GafaIllBand(nA.r * 0.6 + nB.g * 0.5 + _PaintAmount * 0.3 - 0.15, 0.6, 0.012);
            wCol = lerp(wCol, _PaintColor.rgb * (0.94 + 0.1 * nC.r), wPaintM);
        #endif
        mossGate *= 0.3;

    #elif defined(_KIND_BARK)
        float2 tuv = float2(uv.x, 1.0 - uv.y);   // 还原 three.js 的 v（沿树干方向）
        float b1 = GafaIllNoise(float2(tuv.x, tuv.y * 0.03)).r;
        float b2 = GafaIllNoise(float2(tuv.x, tuv.y * 0.08)).a;
        wCol = baseCol * (0.86 + 0.2 * GafaIllBand(b1, 0.5, 0.07)) * (0.93 + 0.12 * GafaIllBand(b2, 0.5, 0.08));
        // 树干下部长满苔藓（参考图里的绿色树根）
        mossGate = max(upF, 1.0 - smoothstep(2.6, 6.0, sitePos.y));
        mossBias = (1.0 - smoothstep(2.5, 5.0, sitePos.y)) * 0.2;
        streak *= 0.2;
        grime *= 0.3;

    #elif defined(_KIND_PAVERS)
        // 大块石板，接缝是一条细线
        float pxW = length(fwidth(sitePos));
        float2 pp = sitePos.xz;
        float TW = _TileSize.x;
        float TH = _TileSize.y;
        float row = floor(pp.y / TH);
        float px = pp.x + GafaIllMod(row, 2.0) * TW * 0.5;
        float2 cell = float2(floor(px / TW), row);
        float2 tf = float2(px - cell.x * TW, pp.y - row * TH);
        float ed = min(min(tf.x, TW - tf.x), min(tf.y, TH - tf.y));
        float hc = GafaIllHash12(cell + 0.5);
        float hc2 = GafaIllHash12(cell * 1.37 + 11.1);
        float edgeD = min(min(pp.x - _Rect.x, _Rect.y - pp.x), min(pp.y - _Rect.z, _Rect.w - pp.y));
        float wild = 1.0 - smoothstep(-1.0, 6.0, edgeD + (nA.r - 0.5) * 9.0);
        float damage = smoothstep(0.55, 0.82, GafaIllNoise(pp * 0.021 + 0.3).r + wild * 0.5);
        float missing = step(hc2, damage * 0.5 + wild * 0.75);
        float3 stone = baseCol * (0.95 + 0.07 * hc) * (0.97 + 0.04 * GafaIllBand(nB.g, 0.5, 0.1));
        float dirt = GafaIllBand(nA.r * 0.75 + nB.g * 0.35 + GafaIllNoise(pp * 0.013 + 0.61).a * 0.35, 0.72, 0.06);
        stone *= 1.0 - 0.08 * dirt;
        float tileFade = 1.0 - smoothstep(0.06, 0.14, pxW);
        float seam = (1.0 - smoothstep(0.4, 1.3, ed / max(fwidth(ed), 1e-5))) * (0.5 + 0.3 * damage) * tileFade;
        float crv = GafaIllNoise(pp * 0.23 + hc * 3.0).a;
        float crack = GafaIllIsoLine(crv, 0.5, 0.9) * step(0.85, hc * 0.8 + damage * 0.45 + hc2 * 0.2) * 0.5 * tileFade;
        wCol = lerp(stone, SRGBToLinear(float3(0x5f, 0x56, 0x49) / 255.0), max(seam, crack));
        wCol = lerp(wCol, _MossColor.rgb * (0.9 + 0.15 * nC.g), missing);
        grime = 0.0;
        streak = 0.0;
        mossGate = 1.0;
        mossBias = missing * 0.35 + damage * 0.2 + wild * 0.5 + dirt * 0.1 - 0.48;

    #elif defined(_KIND_SOIL)
        // 远处的地：低饱和的草地 / 泥土色块
        float m1 = GafaIllBand(nA.r, 0.5, 0.06);
        float m2 = GafaIllBand(nB.g, 0.56, 0.05);
        wCol = lerp(baseCol * float3(0.92, 0.95, 0.9), baseCol, m1);
        wCol = lerp(wCol, baseCol * float3(1.08, 1.06, 0.95), m2 * 0.5);
        float dp = GafaIllBand(nA.a * 0.8 + nB.r * 0.3, 0.8, 0.025);
        wCol = lerp(wCol, SRGBToLinear(float3(0xb4, 0x9a, 0x74) / 255.0), dp * 0.6);
        grime = 0.0;
        streak = 0.0;
        mossGate = 0.0;

    #elif defined(_KIND_WOOD)
        float gr = GafaIllNoise(float2(nC.r * 0.3 + hcoord * 0.2, sitePos.y * 0.15 + nB.g * 0.4)).g;
        wCol = baseCol * (0.88 + 0.2 * GafaIllBand(gr, 0.5, 0.08));

    #else // _KIND_CONCRETE（默认）
        wCol = baseCol * (0.95 + 0.07 * patchA - 0.05 * patchB + stroke * 0.08);
        wCol = lerp(wCol, wCol * float3(1.04, 1.0, 0.93), GafaIllBand(nA.a, 0.56, 0.05) * 0.5);
        #ifdef _USE_PAINT
            float pmN = nB.r * 0.55 + nA.g * 0.5 + nC.r * 0.12;
            float thr = 0.86 - 0.62 * _PaintAmount;
            float wPaintM = smoothstep(thr, thr + 0.012, pmN);
            float rim = smoothstep(thr - 0.04, thr, pmN) * (1.0 - wPaintM);
            float3 pc = _PaintColor.rgb * (0.95 + 0.07 * patchA - 0.05 * patchB + stroke * 0.1);
            wCol = lerp(wCol, pc, wPaintM);
            wCol = lerp(wCol, wCol * 1.1, rim * 0.5);
        #endif
    #endif

    #ifdef _GAFA_DECAL
    {
        float2 duv = TRANSFORM_TEX(uv, _DecalMap);
        float4 dc = SAMPLE_TEXTURE2D(_DecalMap, sampler_DecalMap, duv);
        float2 eu = abs(duv - 0.5) * 2.0;
        float edge = 1.0 - smoothstep(0.86, 0.9, max(eu.x, eu.y) + (nB.r - 0.5) * 0.15);
        float keep = GafaIllBand(nB.r * 0.6 + nC.g * 0.3 + nA.g * 0.4, 0.42, 0.02) * edge * dc.a * _Decal;
        wCol = lerp(wCol, dc.rgb, keep);
    }
    #endif

    wCol = lerp(wCol, wCol * SRGBToLinear(float3(0xcf, 0xd0, 0xc8) / 255.0), streak * 0.7);
    wCol = lerp(wCol, wCol * SRGBToLinear(float3(0xd6, 0xcb, 0xb4) / 255.0), grime * (0.5 + 0.5 * GafaIllBand(nB.r, 0.5, 0.1)));

    float mossN = nA.g * 0.6 + nB.r * 0.5;
    float mossM = GafaIllBand(mossN + upF * 0.25 + grime * 0.35 + streak * 0.15 + mossBias, 0.8, 0.02) * mossGate * _Moss;
    float3 mossCol = _MossColor.rgb * (0.9 + 0.18 * GafaIllBand(nC.r * 0.7 + nB.g * 0.3, 0.5, 0.05));
    mossCol = lerp(mossCol, mossCol * float3(1.1, 1.08, 0.9), upF * 0.4);
    wCol = lerp(wCol, mossCol, mossM);
    return wCol;
#endif
}

#endif
