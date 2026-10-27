#ifndef GAFA_WEATHERED_SURFACE_INCLUDED
#define GAFA_WEATHERED_SURFACE_INCLUDED

// 移植自 TestGAFA/src/materials.js 的 weathered()：
// 世界空间三平面噪声 + 油漆剥落 + 苔藓 + 水渍 + 底部污垢 + 程序化凹凸。
// 噪声贴图 _NoiseTex：R 低频 fbm，G 高频 fbm，B Worley F1，A 中频 fbm（GAFA/Generate Noise Texture 生成）

struct GafaSurface
{
    float3 albedo;
    float  height;
    float  moss;
    float  paintM;
    float  roughAdd;
    float  noiseBG;   // nB.g，粗糙度扰动用
};

float4 GafaNoise(float2 uv)
{
    return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv);
}

float4 GafaTriN(float3 p, float3 n, float s)
{
    float3 w = pow(abs(n), 6.0);
    w /= (w.x + w.y + w.z);
    return GafaNoise(p.zy * s) * w.x + GafaNoise(p.xz * s + 0.37) * w.y + GafaNoise(p.xy * s + 0.71) * w.z;
}

float GafaHash12(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// GLSL mod（结果与 y 同号），HLSL 的 fmod 对负数行为不同
float GafaMod(float x, float y)
{
    return x - y * floor(x / y);
}

// sitePos / siteN：GAFA 场地坐标下的位置与法线；uv：网格 UV0（树皮、贴花用）
GafaSurface GafaWeatheredSurface(float3 sitePos, float3 siteN, float2 uv)
{
    GafaSurface o;
    float wHeight = 0.0;
    float wPaintM = 0.0;
    float wRoughAdd = 0.0;

    float3 wN = normalize(siteN);
    float3 wp = sitePos * _Scale + _Seed;
    float4 nA = GafaTriN(wp, wN, 0.11);
    float4 nB = GafaTriN(wp, wN, 0.55);
    float4 nC = GafaTriN(wp, wN, 2.7);
    float upF = smoothstep(0.5, 0.95, wN.y);
    float vertF = 1.0 - abs(wN.y);
    float hcoord = lerp(sitePos.x, sitePos.z, abs(wN.x)) * _Scale;
    float stA = GafaNoise(float2(hcoord * 0.31 + _Seed, sitePos.y * 0.021)).g;
    float stB = GafaNoise(float2(hcoord * 1.17, sitePos.y * 0.05 + _Seed)).r;
    float streak = smoothstep(0.52, 0.86, stA * 0.75 + stB * 0.45) * vertF * _Streaks;
    float gl1 = 1.0 - smoothstep(0.0, 1.1, sitePos.y);
    float gl2 = (1.0 - smoothstep(2.4, 3.3, sitePos.y)) * step(2.36, sitePos.y);
    float grime = max(gl1, gl2) * (0.55 + 0.45 * nB.r) * _Dirt;
    float mossGate = max(upF, max(grime * 0.9, streak * 0.45));
    float mossBias = 0.0;
    float mossH = 0.4;
    float3 baseCol = _BaseColor.rgb;
    float3 paintCol = _PaintColor.rgb;
    float3 wCol = baseCol;

#if defined(_KIND_RUST)
    float rn = nB.r * 0.6 + nC.g * 0.5;
    float3 rA = float3(0.20, 0.085, 0.035), rB = float3(0.46, 0.21, 0.075), rD = float3(0.075, 0.045, 0.03);
    wCol = lerp(rD, lerp(rA, rB, smoothstep(0.35, 0.75, nC.r)), smoothstep(0.25, 0.62, rn));
    wHeight = nC.g * 0.6 + nB.g * 0.3;
    #ifdef _USE_PAINT
        wPaintM = smoothstep(0.6, 0.64, nA.r * 0.6 + nB.g * 0.5 + _PaintAmount * 0.3 - 0.15);
        wCol = lerp(wCol, paintCol * (0.8 + 0.3 * nC.r), wPaintM);
        wHeight += wPaintM * 0.3;
    #endif
    mossGate *= 0.4;

#elif defined(_KIND_BARK)
    float b1 = GafaNoise(float2(uv.x, uv.y * 0.09)).g;
    float b2 = GafaNoise(float2(uv.x * 0.25, uv.y * 0.035)).r;
    float b3 = GafaNoise(float2(uv.x * 2.0, uv.y * 0.6)).b;
    wCol = baseCol * (0.6 + 0.55 * b2) * (0.78 + 0.38 * b1);
    wCol *= 1.0 - 0.25 * (1.0 - smoothstep(0.05, 0.16, b3));
    float lich = smoothstep(0.6, 0.8, nB.a * 0.8 + nC.r * 0.35);
    wCol = lerp(wCol, float3(0.42, 0.45, 0.36), lich * 0.5);
    wHeight = b1 * 0.7 + b2 * 0.3 - (1.0 - smoothstep(0.05, 0.16, b3)) * 0.3;
    streak *= 0.25;
    grime *= 0.3;

#elif defined(_KIND_PAVERS)
    float2 pp = sitePos.xz;
    const float TW = 1.2;
    const float TH = 0.6;
    float row = floor(pp.y / TH);
    float px = pp.x + GafaMod(row, 2.0) * TW * 0.5;
    float2 cell = float2(floor(px / TW), row);
    float2 tf = float2(px - cell.x * TW, pp.y - row * TH);
    float ed = min(min(tf.x, TW - tf.x), min(tf.y, TH - tf.y));
    float hc = GafaHash12(cell + 0.5);
    float hc2 = GafaHash12(cell * 1.37 + 11.1);
    float edgeD = min(min(pp.x - _Rect.x, _Rect.y - pp.x), min(pp.y - _Rect.z, _Rect.w - pp.y));
    float wild = 1.0 - smoothstep(-1.0, 6.0, edgeD + (nA.r - 0.5) * 9.0);
    float damage = smoothstep(0.55, 0.82, GafaNoise(pp * 0.021 + 0.3).r + wild * 0.5);
    float missing = step(hc2, damage * 0.5 + wild * 0.75);
    float3 granite = baseCol * (0.8 + 0.3 * hc) * (0.88 + 0.24 * nC.g);
    granite = lerp(granite, granite * 0.42, smoothstep(0.8, 0.95, GafaNoise(pp * 3.1).g));
    granite = lerp(granite, granite * 1.3, smoothstep(0.86, 0.97, GafaNoise(pp * 2.3 + 0.5).a));
    float dirt = smoothstep(0.38, 0.85, nA.r * 0.75 + nB.g * 0.35 + GafaNoise(pp * 0.013 + 0.61).a * 0.35);
    granite *= 1.0 - 0.38 * dirt;
    granite = lerp(granite, granite * float3(0.92, 0.9, 0.8), smoothstep(0.3, 0.7, GafaNoise(pp * 0.03 + 0.2).g));
    float seamW = 0.007 + 0.022 * damage;
    float seam = 1.0 - smoothstep(seamW, seamW + 0.012, ed + (nC.r - 0.5) * 0.012);
    float crv = abs(GafaNoise(pp * 0.23 + hc * 3.0).a - 0.5);
    float crack = (1.0 - smoothstep(0.003, 0.012, crv)) * step(0.78, hc * 0.8 + damage * 0.45 + hc2 * 0.2);
    float3 soil = lerp(float3(0.05, 0.04, 0.025), float3(0.11, 0.09, 0.05), nB.r);
    float3 seamCol = lerp(soil, float3(0.06, 0.075, 0.03), smoothstep(0.55, 0.8, nB.a) * damage);
    wCol = lerp(granite, seamCol, max(seam, crack * 0.85));
    wCol = lerp(wCol, soil, missing);
    float2 tilt = (float2(hc, hc2) - 0.5) * 0.02 * (0.35 + damage);
    wHeight = (1.0 - seam) * 0.004 + dot(tf - float2(TW, TH) * 0.5, tilt) * (1.0 - missing) - missing * 0.03 - crack * 0.003 + nC.g * 0.0012;
    grime = 0.0;
    streak = 0.0;
    mossGate = 1.0;
    mossBias = missing * 0.35 + seam * 0.2 * damage + damage * 0.2 + wild * 0.5 + dirt * 0.15 - 0.42;
    wRoughAdd = missing * 0.1 - (1.0 - dirt) * 0.05;
    mossH = 0.002;

#elif defined(_KIND_SOIL)
    float3 soil = lerp(float3(0.05, 0.04, 0.026), float3(0.11, 0.088, 0.055), nB.r);
    float cov = smoothstep(0.32, 0.62, nA.r * 0.7 + nB.g * 0.5);
    float3 grass = lerp(float3(0.06, 0.085, 0.02), float3(0.15, 0.17, 0.05), nC.r);
    float3 litter = lerp(float3(0.16, 0.10, 0.045), float3(0.25, 0.18, 0.08), nC.g);
    wCol = lerp(soil, grass, cov);
    wCol = lerp(wCol, litter, smoothstep(0.5, 0.85, nA.a * 0.7 + nB.a * 0.3) * 0.45);
    wHeight = nC.g * 0.02 + nB.r * 0.03;
    grime = 0.0;
    streak = 0.0;
    mossGate = 0.0;
    mossH = 0.0;

#elif defined(_KIND_WOOD)
    float gr = GafaNoise(float2(nC.r * 0.3 + hcoord * 0.2, sitePos.y * 0.15 + nB.g * 0.4)).g;
    wCol = baseCol * (0.62 + 0.55 * gr) * (0.8 + 0.3 * nB.r);
    wCol = lerp(wCol, float3(0.05, 0.05, 0.04), smoothstep(0.62, 0.85, nB.a) * 0.6);
    wHeight = gr * 0.5;

#else // _KIND_CONCRETE（默认）
    wCol = baseCol * (0.8 + 0.32 * nA.r + 0.2 * (nB.g - 0.5));
    float pits = 1.0 - smoothstep(0.03, 0.11, nC.b);
    wCol *= 1.0 - 0.28 * pits;
    wCol = lerp(wCol, wCol * float3(1.05, 0.99, 0.86), smoothstep(0.35, 0.85, nA.a) * 0.8);
    wHeight = nB.g * 0.45 + nC.g * 0.35 - pits * 0.5;
    #ifdef _USE_PAINT
        float pmN = nB.r * 0.55 + nA.g * 0.5 + nC.r * 0.14 + nB.a * 0.1;
        float thr = 0.86 - 0.62 * _PaintAmount;
        wPaintM = smoothstep(thr, thr + 0.025, pmN);
        float rim = smoothstep(thr - 0.05, thr, pmN) * (1.0 - wPaintM);
        float3 pc = paintCol * (0.84 + 0.26 * nA.r + 0.1 * nC.g);
        pc = lerp(pc, (float3)(dot(pc, float3(0.3, 0.5, 0.2)) * 1.15), 0.32 * smoothstep(0.3, 0.8, nB.a));
        wCol = lerp(wCol * (1.0 - 0.3 * rim), pc, wPaintM);
        wHeight += wPaintM * 0.5;
    #endif
#endif

#ifdef _GAFA_DECAL
    {
        float2 duv = TRANSFORM_TEX(uv, _DecalMap);
        float4 dc = SAMPLE_TEXTURE2D(_DecalMap, sampler_DecalMap, duv);
        float2 eu = abs(duv - 0.5) * 2.0;
        float edge = 1.0 - smoothstep(0.82, 1.0, max(eu.x, eu.y) + (nB.r - 0.5) * 0.25);
        float keep = smoothstep(0.25, 0.55, nB.r * 0.6 + nC.g * 0.3 + nA.g * 0.4) * edge * dc.a * _Decal;
        float3 faded = lerp(dc.rgb, (float3)dot(dc.rgb, float3(0.33, 0.33, 0.33)), 0.25) * (0.8 + 0.25 * nA.r);
        wCol = lerp(wCol, faded, keep);
        wPaintM = max(wPaintM, keep);
    }
#endif

    wCol *= 1.0 - 0.42 * streak;
    wCol = lerp(wCol, wCol * float3(0.8, 0.88, 0.72), streak * 0.6);
    wCol *= 1.0 - 0.42 * grime;

    float mossN = nA.g * 0.6 + nB.r * 0.5;
    float mossM = smoothstep(0.6, 0.92, mossN + upF * 0.25 + grime * 0.35 + streak * 0.15 + mossBias) * mossGate * _Moss;
    mossM *= 0.75 + 0.25 * nC.g;
    float3 mossCol = lerp(float3(0.035, 0.045, 0.015), float3(0.085, 0.10, 0.03), nC.r * 0.7 + nB.g * 0.3);
    mossCol = lerp(mossCol, float3(0.12, 0.10, 0.055), smoothstep(0.55, 0.9, nB.a) * 0.55);
    wCol = lerp(wCol, mossCol, mossM);
    wHeight += mossM * mossH * nC.r;

    o.albedo = wCol;
    o.height = wHeight;
    o.moss = mossM;
    o.paintM = wPaintM;
    o.roughAdd = wRoughAdd;
    o.noiseBG = nB.g;
    return o;
}

// 由屏幕空间高度导数扰动法线（Mikkelsen surface gradient，与 three.js 的 perturbNormal 等价）
float3 GafaBumpNormal(float3 positionWS, float3 normalWS, float height, float bump)
{
    float3 dpx = ddx(positionWS);
    float3 dpy = ddy(positionWS);
    float3 r1 = cross(dpy, normalWS);
    float3 r2 = cross(normalWS, dpx);
    float det = dot(dpx, r1);
    float2 dh = float2(ddx(height), ddy(height)) * bump;
    float3 grad = sign(det) * (dh.x * r1 + dh.y * r2);
    return normalize(abs(det) * normalWS - grad);
}

float GafaRoughness(GafaSurface s)
{
    return clamp(_Roughness + (s.noiseBG - 0.5) * 0.16 + s.moss * 0.15 - s.paintM * 0.12 + s.roughAdd, 0.05, 1.0);
}

float GafaMetallic(GafaSurface s)
{
    return _Metallic * (1.0 - s.paintM) * (1.0 - s.moss);
}

#endif
