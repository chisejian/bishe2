// GAFA 插画版后期（URP，由 GafaIllustratedFeature 驱动），移植自 TestGAFA/illustrated/src/post.js：
//   0 SAO 环境光遮蔽（半分辨率）   1 深度感知模糊
//   2 合成：AO 着色 + 墨线（轮廓 / 折边 / 叶团边）+ 空气透视
//   3 泛光亮部提取                 4 泛光高斯模糊（UnrealBloomPass）
//   5 调色：泛光叠加 + Khronos Neutral + 降饱和 + 冷暖分离 + 暗角
Shader "Hidden/GAFA/IllustratedPost"
{
    HLSLINCLUDE
    #pragma target 3.5
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "GafaIllustratedCommon.hlsl"
    #include "GafaIllustratedInk.hlsl"

    TEXTURE2D_X_FLOAT(_GafaDepth);
    TEXTURE2D_X(_GafaInk);
    TEXTURE2D_X(_GafaAO);
    TEXTURE2D_X(_GafaBloom0);
    TEXTURE2D_X(_GafaBloom1);
    TEXTURE2D_X(_GafaBloom2);
    TEXTURE2D_X(_GafaBloom3);
    TEXTURE2D_X(_GafaBloom4);

    float4 _GafaProj;       // x = P00, y = P11, z = P02, w = P12（相机投影矩阵，OpenGL 约定）
    float4 _GafaRes;        // xy 全分辨率，zw 半分辨率
    float4x4 _GafaCamToWorld;
    float3 _GafaCamPos;
    float3 _GafaSunDir;     // 世界空间，指向太阳

    float4 _GafaAOParams;   // x 半径  y 强度  z 帧序号  w AO 着色强度
    float3 _GafaAOTint;
    float2 _GafaBlurDir;

    float4 _GafaLine;       // x 线宽(px)  y 轮廓阈值  z 折边阈值(cos)  w 淡出距离
    float3 _GafaInkCol;
    float2 _GafaInkParams;  // x 墨线浓度  y 其它物体（无墨线通道）是否描轮廓
    float3 _GafaHazeCol;
    float3 _GafaHazeSun;
    float4 _GafaHaze;       // x 密度  y 高度衰减  z 最大浓度  w 起始距离

    float4 _GafaBloomParams; // x 阈值  y 平滑宽度  z 强度  w 半径
    float2 _GafaBlurInvSize;
    float  _GafaBlurRadius;

    float4 _GafaGrade;      // x 曝光  y 饱和度  z 暗角

    float GafaRawDepth(float2 uv)
    {
        return SAMPLE_TEXTURE2D_X_LOD(_GafaDepth, sampler_PointClamp, uv, 0).r;
    }

    bool GafaIsSky(float raw)
    {
    #if UNITY_REVERSED_Z
        return raw <= 1e-7;
    #else
        return raw >= 1.0 - 1e-7;
    #endif
    }

    float GafaLinZ(float2 uv)
    {
        float raw = GafaRawDepth(uv);
        if (GafaIsSky(raw)) return 1e5;
        return LinearEyeDepth(raw, _ZBufferParams);
    }

    // 相机空间位置（-z 朝前，与 three.js 相同）
    float3 GafaViewPos(float2 uv, float z)
    {
        float2 ndc = uv * 2.0 - 1.0;
        return float3((ndc.x + _GafaProj.z) / _GafaProj.x * z, (ndc.y + _GafaProj.w) / _GafaProj.y * z, -z);
    }

    float3 GafaViewPosAt(float2 uv)
    {
        return GafaViewPos(uv, GafaLinZ(uv));
    }

    float GafaIgn(float2 p)
    {
        return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        // 0 —— SAO
        Pass
        {
            Name "GafaAO"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float raw = GafaRawDepth(uv);
                if (GafaIsSky(raw)) return float4(1.0, 1e4, 0.0, 1.0);
                float z = LinearEyeDepth(raw, _ZBufferParams);
                float3 P = GafaViewPos(uv, z);
                float2 px = 1.0 / _GafaRes.xy;
                float3 Pr = GafaViewPosAt(uv + float2(px.x, 0.0)), Pl = GafaViewPosAt(uv - float2(px.x, 0.0));
                float3 Pu = GafaViewPosAt(uv + float2(0.0, px.y)), Pd = GafaViewPosAt(uv - float2(0.0, px.y));
                float3 dx = abs(Pr.z - P.z) < abs(P.z - Pl.z) ? Pr - P : P - Pl;
                float3 dy = abs(Pu.z - P.z) < abs(P.z - Pd.z) ? Pu - P : P - Pd;
                float3 N = normalize(cross(dx, dy));
                if (dot(N, P) > 0.0) N = -N;
                float rnd = GafaIgn(input.positionCS.xy + _GafaAOParams.z * 7.13) * 6.2831;
                float radius = _GafaAOParams.x;
                float ssR = min(radius * _GafaProj.y * 0.5 / -P.z, 0.15);
                float aspect = _GafaRes.y / _GafaRes.x;
                float r2 = radius * radius;
                float occ = 0.0;
                const int NS = 12;
                [unroll]
                for (int i = 0; i < NS; i++)
                {
                    float a = (i + 0.5) / NS;
                    float ang = rnd + a * 43.98;
                    float2 off = float2(cos(ang) * aspect, sin(ang)) * ssR * a;
                    float3 Q = GafaViewPosAt(uv + off);
                    float3 v = Q - P;
                    float vv = dot(v, v);
                    float vn = dot(v, N);
                    float f = max(r2 - vv, 0.0);
                    occ += f * f * f * max((vn - 0.012 * -P.z * 0.1) / (0.01 + vv), 0.0);
                }
                float ao = max(0.0, 1.0 - occ * _GafaAOParams.y * 5.0 / (r2 * r2 * r2 * NS));
                return float4(ao, -P.z, 0.0, 1.0);
            }
            ENDHLSL
        }

        // 1 —— 深度感知模糊
        Pass
        {
            Name "GafaAOBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float z0 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).g;
                float sum = 0.0;
                float ws = 0.0;
                [unroll]
                for (int i = -4; i <= 4; i++)
                {
                    float2 s = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv + _GafaBlurDir * i, 0).rg;
                    float w = exp(-(float)(i * i) / 9.0) / (0.002 + abs(s.g - z0) / max(z0, 0.1));
                    sum += s.r * w;
                    ws += w;
                }
                return float4(sum / ws, z0, 0.0, 1.0);
            }
            ENDHLSL
        }

        // 2 —— 合成：AO + 大轮廓线 + 空气透视
        Pass
        {
            Name "GafaComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 GafaInkAt(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_GafaInk, sampler_PointClamp, uv, 0);
            }

            bool GafaIsSolid(float m) { return m > 0.75 && m < 1.25; }

            float GafaClumpEdge(float2 uv, float4 ic, float z)
            {
                float4 inN = GafaInkAt(uv);
                float zN = GafaLinZ(uv);
                return step(0.5, abs(inN.z - ic.z)) * step(0.04, (zN - z) / z);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float3 col = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb;
                float raw = GafaRawDepth(uv);
                bool sky = GafaIsSky(raw);
                float z = sky ? 1e5 : LinearEyeDepth(raw, _ZBufferParams);
                float3 vp = GafaViewPos(uv, z);

                // AO 联合双边上采样
                float2 halfRes = _GafaRes.zw;
                float2 pos = uv * halfRes - 0.5;
                float2 b = floor(pos);
                float2 f = pos - b;
                float ao = 0.0;
                float ws = 0.0;
                [unroll]
                for (int j = 0; j < 2; j++)
                {
                    [unroll]
                    for (int i = 0; i < 2; i++)
                    {
                        int2 cc = clamp(int2(b) + int2(i, j), int2(0, 0), int2(halfRes) - 1);
                        float4 a = LOAD_TEXTURE2D_X(_GafaAO, cc);
                        float bw = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y);
                        float w = bw / (0.001 + abs(a.g - z) / max(z, 0.1)) + 1e-5;
                        ao += a.r * w;
                        ws += w;
                    }
                }
                ao = sky ? 1.0 : ao / ws;
                col *= lerp(float3(1, 1, 1), lerp(_GafaAOTint, float3(1, 1, 1), ao), _GafaAOParams.w);

                if (!sky)
                {
                    float2 px = _GafaLine.x / _GafaRes.xy;
                    float2 oR = float2(px.x, 0.0), oU = float2(0.0, px.y);
                    float4 ic = GafaInkAt(uv);
                    float ln = 0.0;
                    // 线型 1 实体：轮廓（1/z 二阶差分，只画在近处一侧）+ 明显折边；
                    // 线型 3 是没有墨线通道的其它物体（清屏值），只描轮廓
                    bool solid = GafaIsSolid(ic.w);
                    bool other = ic.w > 2.75 && _GafaInkParams.y > 0.5;
                    if (solid || other)
                    {
                        float zl = GafaLinZ(uv - oR), zr = GafaLinZ(uv + oR), zd = GafaLinZ(uv - oU), zu = GafaLinZ(uv + oU);
                        float iz = 1.0 / z;
                        float lap = max(abs(1.0 / zl + 1.0 / zr - 2.0 * iz), abs(1.0 / zu + 1.0 / zd - 2.0 * iz)) / iz;
                        float zmax = max(max(zl, zr), max(zu, zd));
                        ln = smoothstep(_GafaLine.y, _GafaLine.y * 2.0, lap) * step(0.03, (zmax - z) / z);
                        if (solid)
                        {
                            float4 ir = GafaInkAt(uv + oR), iu = GafaInkAt(uv + oU);
                            float3 nc = GafaOctDecode(ic.xy);
                            float nd = 1.0;
                            if (GafaIsSolid(ir.w)) nd = min(nd, dot(nc, GafaOctDecode(ir.xy)));
                            if (GafaIsSolid(iu.w)) nd = min(nd, dot(nc, GafaOctDecode(iu.xy)));
                            ln = max(ln, 1.0 - smoothstep(_GafaLine.z - 0.1, _GafaLine.z, nd));
                        }
                    }
                    else if (ic.w > 1.75)
                    {
                        // 线型 2 叶团：相邻像素属于别的叶团 / 物体 / 天空、且明显更远时描线
                        ln = max(max(GafaClumpEdge(uv + oR, ic, z), GafaClumpEdge(uv - oR, ic, z)),
                                   max(GafaClumpEdge(uv + oU, ic, z), GafaClumpEdge(uv - oU, ic, z)));
                    }
                    ln *= lerp(1.0, 0.3, smoothstep(_GafaLine.w * 0.35, _GafaLine.w, z));
                    // 线色 = 底色压暗 + 一点暖褐墨色，而不是纯黑
                    col = lerp(col, col * 0.3 + _GafaInkCol * 0.5, ln * _GafaInkParams.x);

                    // 空气透视（高度按 GAFA 场地坐标）
                    float3 wp = mul(_GafaCamToWorld, float4(vp, 1.0)).xyz;
                    float3 rd = wp - _GafaCamPos;
                    float dist = length(rd);
                    rd /= dist;
                    float hf = exp(-max(GafaIllToSitePos(wp).y, 0.0) / _GafaHaze.y);
                    float fog = 1.0 - exp(-max(dist - _GafaHaze.w, 0.0) * _GafaHaze.x * (0.4 + 0.6 * hf));
                    fog = min(fog, _GafaHaze.z);
                    float3 hz = lerp(_GafaHazeCol, _GafaHazeSun, pow(max(dot(rd, _GafaSunDir), 0.0), 6.0) * 0.5);
                    col = lerp(col, hz, fog);
                }
                return float4(col, 1.0);
            }
            ENDHLSL
        }

        // 3 —— 泛光亮部提取（LuminosityHighPassShader）
        Pass
        {
            Name "GafaBloomPrefilter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, 0).rgb;
                float v = dot(c, float3(0.299, 0.587, 0.114));
                float a = smoothstep(_GafaBloomParams.x, _GafaBloomParams.x + _GafaBloomParams.y, v);
                return float4(c * a, 1.0);
            }
            ENDHLSL
        }

        // 4 —— 泛光可分离高斯模糊（sigma = 核半径）
        Pass
        {
            Name "GafaBloomBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float GafaGaussianPdf(float x, float sigma)
            {
                return 0.39894 * exp(-0.5 * x * x / (sigma * sigma)) / sigma;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float sigma = _GafaBlurRadius;
                float weightSum = GafaGaussianPdf(0.0, sigma);
                float3 sum = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb * weightSum;
                for (int i = 1; i < 11; i++)
                {
                    if (i >= (int)_GafaBlurRadius) break;
                    float w = GafaGaussianPdf((float)i, sigma);
                    float2 o = _GafaBlurDir * _GafaBlurInvSize * i;
                    sum += (SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + o, 0).rgb
                          + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - o, 0).rgb) * w;
                    weightSum += 2.0 * w;
                }
                return float4(sum / weightSum, 1.0);
            }
            ENDHLSL
        }

        // 5 —— 泛光叠加 + 调色：Khronos Neutral + 整体降饱和 + 轻微冷暖分离 + 暗角
        Pass
        {
            Name "GafaFinal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float3 GafaNeutral(float3 color)
            {
                const float startCompression = 0.8 - 0.04;
                const float desaturation = 0.15;
                float x = min(color.r, min(color.g, color.b));
                float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
                color -= offset;
                float peak = max(color.r, max(color.g, color.b));
                if (peak < startCompression) return color;
                float dd = 1.0 - startCompression;
                float newPeak = 1.0 - dd * dd / (peak + dd - startCompression);
                color *= newPeak / peak;
                float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
                return lerp(color, newPeak.xxx, g);
            }

            float GafaBloomFactor(float f)
            {
                return lerp(f, 1.2 - f, _GafaBloomParams.w);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float3 col = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb;
                float3 bloom = GafaBloomFactor(1.0) * SAMPLE_TEXTURE2D_X_LOD(_GafaBloom0, sampler_LinearClamp, uv, 0).rgb
                             + GafaBloomFactor(0.8) * SAMPLE_TEXTURE2D_X_LOD(_GafaBloom1, sampler_LinearClamp, uv, 0).rgb
                             + GafaBloomFactor(0.6) * SAMPLE_TEXTURE2D_X_LOD(_GafaBloom2, sampler_LinearClamp, uv, 0).rgb
                             + GafaBloomFactor(0.4) * SAMPLE_TEXTURE2D_X_LOD(_GafaBloom3, sampler_LinearClamp, uv, 0).rgb
                             + GafaBloomFactor(0.2) * SAMPLE_TEXTURE2D_X_LOD(_GafaBloom4, sampler_LinearClamp, uv, 0).rgb;
                col += bloom * _GafaBloomParams.z;

                col *= _GafaGrade.x;
                col = GafaNeutral(max(col, 0.0));
                float l = dot(col, float3(0.2126, 0.7152, 0.0722));
                col = lerp(l.xxx, col, _GafaGrade.y);
                col *= lerp(float3(0.98, 0.99, 1.02), float3(1.03, 1.0, 0.95), smoothstep(0.05, 0.7, l));
                float2 dc = uv - 0.5;
                col *= lerp(1.0, 0.9, smoothstep(0.15, 0.7, dot(dc, dc) * 2.0) * _GafaGrade.z);
                // 输出线性值，sRGB 转换交给 URP 的最终输出
                return float4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }
}
