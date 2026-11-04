// GAFA 插画版天空（URP 天空盒），移植自 TestGAFA/illustrated/src/sky.js：
// 饱和蓝渐变 + 硬边卡通积云（Worley 团块，迎光亮边、背光灰蓝）+ 太阳。太阳方向取 URP 主光。
Shader "GAFA/Illustrated/Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.435, 0.639, 0.800, 1)
        _Mid ("Mid", Color) = (0.612, 0.769, 0.863, 1)
        _Horizon ("Horizon", Color) = (0.867, 0.906, 0.878, 1)
        _CloudShade ("Cloud Shade", Color) = (0.769, 0.800, 0.831, 1)
        _CloudSpeed ("Cloud Speed", Float) = 1
        [NoScaleOffset] _NoiseTex ("Noise (R lowFbm G highFbm B worley A midFbm)", 2D) = "gray" {}
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Zenith;
                float4 _Mid;
                float4 _Horizon;
                float4 _CloudShade;
                float  _CloudSpeed;
            CBUFFER_END

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.dir = input.positionOS.xyz;
                return output;
            }

            float4 Noise(float2 p)
            {
                return SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, p, 0);
            }

            // 积云：低频覆盖 + Worley 团块（圆鼓鼓的云朵）+ 少量细节
            float CloudD(float2 p)
            {
                float base = Noise(p * 0.5).r;
                float puff = 1.0 - Noise(p * 1.25 + 0.17).b;
                float puff2 = 1.0 - Noise(p * 2.6 + 0.61).b;
                float det = Noise(p * 4.0 + 0.53).a;
                return base * 0.52 + puff * 0.3 + puff2 * 0.12 + det * 0.08;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 sunDir = normalize(_MainLightPosition.xyz);
                float3 cloudLit = float3(1.1, 1.09, 1.04);
                float3 d = normalize(input.dir);
                float h = d.y;
                float3 col = lerp(_Horizon.rgb, _Mid.rgb, smoothstep(0.0, 0.3, h));
                col = lerp(col, _Zenith.rgb, smoothstep(0.28, 1.0, h));
                float sd = max(dot(d, sunDir), 0.0);
                col += float3(1.0, 0.94, 0.8) * pow(sd, 10.0) * 0.15;
                col = lerp(col, float3(1.7, 1.6, 1.4), smoothstep(0.99935, 0.9996, sd));
                if (h > 0.0)
                {
                    float t = _Time.y * _CloudSpeed;
                    float2 cp = d.xz / (h + 0.2) * 0.13 + float2(t * 0.0016, t * 0.0005);
                    float c = CloudD(cp);
                    // 云多在天空下半部，越往天顶越稀
                    float hm = smoothstep(0.015, 0.1, h) * (1.0 - 0.75 * smoothstep(0.35, 0.85, h));
                    float thr = 0.63 + (1.0 - hm) * 0.25;
                    float cov = smoothstep(thr, thr + 0.012, c);
                    // 朝太阳方向偏移采样：那边更密 → 处在自身阴影里；明暗用硬边
                    float2 toSun = normalize(sunDir.xz + 1e-4) * 0.05;
                    float c2 = CloudD(cp + toSun);
                    float shade = smoothstep(0.012, 0.035, c2 - c);
                    float inner = smoothstep(thr + 0.06, thr + 0.22, c);
                    float3 cc = lerp(cloudLit, _CloudShade.rgb, max(shade * 0.85, inner * 0.2));
                    // 迎光的云边一圈更亮
                    float rimC = (1.0 - smoothstep(thr, thr + 0.03, c)) * (1.0 - shade);
                    cc += float3(0.2, 0.18, 0.12) * rimC;
                    // 靠近地平线的云融进天色
                    cc = lerp(cc, _Horizon.rgb * 1.05, 1.0 - smoothstep(0.0, 0.12, h));
                    col = lerp(col, cc, cov);
                }
                col = lerp(col, _Horizon.rgb * 0.9, smoothstep(0.0, -0.2, h));
                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
