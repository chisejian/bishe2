// GAFA 插画版植被材质（URP）：树叶 / 榕树叶 / 草 / 藤蔓 / 灌木 / 蕨 / 落叶
// 移植自 TestGAFA/illustrated/src/materials.js 的 foliage()，配色来自 vegetation.js。核心逻辑见 GafaIllustratedFoliage.hlsl。
Shader "GAFA/Illustrated/Foliage"
{
    Properties
    {
        [Header(Color)]
        _Color ("Palette 1", Color) = (0.533, 0.663, 0.337, 1)
        _Color2 ("Palette 2", Color) = (0.514, 0.639, 0.353, 1)
        _Color3 ("Palette 3", Color) = (0.561, 0.671, 0.361, 1)
        _PaletteGroup ("Palette Group (items, 0 = per object)", Float) = 0
        _ItemVertices ("Vertices Per Item (leaf card = 4)", Float) = 4
        _Jitter ("Per-Item Jitter", Range(0, 0.2)) = 0.03
        _VertexColorStrength ("Vertex Color (glb)", Range(0, 1)) = 0
        _DryColor ("Grass Dry Color", Color) = (0.788, 0.745, 0.494, 1)
        _DryHue ("Grass Dry Hue Threshold", Range(0, 0.5)) = 0.165

        [Header(Surface)]
        _BaseMap ("Texture", 2D) = "white" {}
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 1
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [KeywordEnum(None, Sphere, Up)] _NormalMode ("Normal Mode", Float) = 1
        _NormalBlend ("Normal Blend", Range(0, 1)) = 0.92
        _NormalLift ("Sphere Normal Lift", Range(0, 1)) = 0.35

        [Header(Toon Lighting)]
        _ShadeEdge ("Shade Edge", Range(-1, 1)) = -0.12
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.5)) = 0.04
        _BrushNoise ("Brush Noise", Range(0, 1)) = 0.35
        _Rim ("Rim Light", Range(0, 1)) = 0.06
        _Specular ("Hard Specular", Range(0, 2)) = 0
        _Translucency ("Translucency", Range(0, 1)) = 0.25
        _SkyAmbient ("Sky Ambient", Color) = (0.665, 0.680, 0.715, 1)
        _GroundAmbient ("Ground Ambient", Color) = (0.634, 0.610, 0.566, 1)
        _AmbientFromScene ("Ambient From Scene (SH)", Range(0, 1)) = 1
        [NoScaleOffset] _NoiseTex ("Noise (R lowFbm G highFbm B worley A midFbm)", 2D) = "gray" {}

        [Header(Wind)]
        _Wind ("Wind Sway", Range(0, 0.3)) = 0.05
        _Flutter ("Flutter", Range(0, 0.1)) = 0.015
        _WindSpeed ("Wind Speed", Range(0, 3)) = 1
        [Toggle(_GAFA_GRASS)] _Grass ("Grass (bend from root, root/tip shading, dry clumps)", Float) = 0
        [Toggle(_GAFA_EXACT_COLOR)] _ExactColor ("Exact Instance Colors (exported vegetation)", Float) = 0

        [Header(Ink)]
        _InkId ("Ink Id", Float) = 1
        [Enum(None, 0, Solid, 1, Clump, 2)] _InkMode ("Ink Mode", Float) = 2
        [Toggle] _InkPerItem ("Ink Id Per Item (fern)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Unlit" "Queue" = "AlphaTest" }
        LOD 300
        Cull Off

        HLSLINCLUDE
        #pragma shader_feature_local _ALPHATEST_ON
        #pragma shader_feature_local _GAFA_GRASS
        #pragma shader_feature_local _GAFA_EXACT_COLOR
        #pragma shader_feature_local _NORMALMODE_NONE _NORMALMODE_SPHERE _NORMALMODE_UP
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            AlphaToMask On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #define GAFA_TOON_FOLIAGE
            #include "GafaIllustratedFoliage.hlsl"
            #include "GafaIllustratedToon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 clump      : TEXCOORD2;
                float3 pivot      : TEXCOORD3;
                uint   vertexID   : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 itemColor  : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                float4 clump      : TEXCOORD5;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = GafaIllFoliagePositionWS(input.positionOS.xyz, input.uv, input.pivot);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.itemColor = GafaIllFoliageItemColor(input.vertexID, input.color, input.uv);
                output.clump = input.clump;
            #if !defined(_FOG_FRAGMENT)
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
            #endif
                return output;
            }

            void Frag(Varyings input, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half alpha = GafaIllFoliageAlpha(input.uv);

                float3 N = GafaIllFoliageNormalWS(normalize(input.normalWS), input.positionWS, input.clump);
                float3 albedo = tex.rgb * input.itemColor;
                float3 color = GafaToonLighting(albedo, N, input.positionWS, GafaIllToSitePos(input.positionWS));
                color = MixFog(color, InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor));
                outColor = half4(color, alpha);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "GafaIllustratedFoliage.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float3 pivot      : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = GafaIllFoliagePositionWS(input.positionOS.xyz, input.uv, input.pivot);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                GafaIllFoliageAlpha(input.uv);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "GafaIllustratedFoliage.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float3 pivot      : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformWorldToHClip(GafaIllFoliagePositionWS(input.positionOS.xyz, input.uv, input.pivot));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                GafaIllFoliageAlpha(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "GafaIllustratedFoliage.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 clump      : TEXCOORD2;
                float3 pivot      : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 clump      : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DNVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = GafaIllFoliagePositionWS(input.positionOS.xyz, input.uv, input.pivot);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.clump = input.clump;
                return output;
            }

            void DNFrag(Varyings input, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                GafaIllFoliageAlpha(input.uv);
                float3 normalWS = GafaIllFoliageNormalWS(normalize(input.normalWS), input.positionWS, input.clump);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                outNormalWS = half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0.0);
            #else
                outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
            #endif
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
        // 墨线缓冲：法线 / 叶团编号 / 线型（GafaIllustratedFeature 绘制）
        Pass
        {
            Name "GafaInk"
            Tags { "LightMode" = "GafaInk" }
            ZWrite Off
            ZTest LEqual
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex InkVert
            #pragma fragment InkFrag
            #pragma multi_compile_instancing

            #include "GafaIllustratedFoliage.hlsl"
            #include "GafaIllustratedInk.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 clump      : TEXCOORD2;
                float3 pivot      : TEXCOORD3;
                uint   vertexID   : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 clump      : TEXCOORD3;
                float  inkId      : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings InkVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = GafaIllFoliagePositionWS(input.positionOS.xyz, input.uv, input.pivot);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.clump = input.clump;
                output.inkId = GafaIllFoliageInkId(input.clump, GafaIllFoliageItem(input.vertexID));
                return output;
            }

            float4 InkFrag(Varyings input) : SV_Target
            {
                GafaIllFoliageAlpha(input.uv);
                float3 n = GafaIllFoliageNormalWS(normalize(input.normalWS), input.positionWS, input.clump);
                return float4(GafaOctEncode(n), input.inkId, _InkMode);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
