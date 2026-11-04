// GAFA 插画版手绘材质（URP）：混凝土 / 油漆 / 锈铁 / 树皮 / 石板 / 泥地 / 木头 / 纯色（橡胶、玻璃）
// 移植自 TestGAFA/illustrated/src/materials.js 的 painted() 与 toon()。
// 反照率见 GafaIllustratedPaintedSurface.hlsl，卡通光照见 GafaIllustratedToon.hlsl。
// 原项目后期里的墨线、AO 着色、空气透视不属于材质，未包含在内。
Shader "GAFA/Illustrated/Painted"
{
    Properties
    {
        [KeywordEnum(Concrete, Rust, Bark, Pavers, Soil, Wood, Plain)] _Kind ("Kind", Float) = 0
        _BaseColor ("Base Color", Color) = (0.769, 0.741, 0.678, 1)
        [Toggle(_USE_PAINT)] _UsePaint ("Use Paint", Float) = 0
        _PaintColor ("Paint Color", Color) = (0, 0, 0, 1)
        _PaintAmount ("Paint Amount", Range(0, 1)) = 0.8
        _MossColor ("Moss Color", Color) = (0.561, 0.612, 0.353, 1)
        _Moss ("Moss", Range(0, 2)) = 1
        _Streaks ("Streaks", Range(0, 2)) = 1
        _Dirt ("Dirt", Range(0, 2)) = 1
        _Scale ("Noise Scale", Float) = 1
        _Seed ("Seed", Float) = 0
        _Rect ("Pavers Rect (x0, x1, z0, z1)", Vector) = (-10000, 10000, -10000, 10000)
        _TileSize ("Pavers Tile Size (width, depth)", Vector) = (1.2, 0.6, 0, 0)
        [NoScaleOffset] _NoiseTex ("Noise (R lowFbm G highFbm B worley A midFbm)", 2D) = "gray" {}

        [Toggle(_GAFA_DECAL)] _UseDecal ("Use Decal Map", Float) = 0
        _DecalMap ("Decal Map (mural / painting)", 2D) = "white" {}
        _Decal ("Decal Keep", Range(0, 1)) = 1

        [Header(Toon Lighting)]
        _ShadeEdge ("Shade Edge", Range(-1, 1)) = 0
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.5)) = 0.04
        _BrushNoise ("Brush Noise", Range(0, 1)) = 0.2
        _Rim ("Rim Light", Range(0, 1)) = 0.12
        _Specular ("Hard Specular", Range(0, 2)) = 0
        _SkyAmbient ("Sky Ambient", Color) = (0.665, 0.680, 0.715, 1)
        _GroundAmbient ("Ground Ambient", Color) = (0.634, 0.610, 0.566, 1)
        _AmbientFromScene ("Ambient From Scene (SH)", Range(0, 1)) = 1

        [Header(Surface)]
        [Toggle(_SURFACE_TYPE_TRANSPARENT)] _Transparent ("Transparent", Float) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        [Header(Ink)]
        _InkId ("Ink Id", Float) = 1
        [Enum(None, 0, Solid, 1, Clump, 2)] _InkMode ("Ink Mode", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Unlit" "Queue" = "Geometry" }
        LOD 300

        HLSLINCLUDE
        #pragma shader_feature_local _KIND_CONCRETE _KIND_RUST _KIND_BARK _KIND_PAVERS _KIND_SOIL _KIND_WOOD _KIND_PLAIN
        #pragma shader_feature_local _USE_PAINT
        #pragma shader_feature_local _GAFA_DECAL
        #pragma shader_feature_local _SURFACE_TYPE_TRANSPARENT
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

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

            #include "GafaIllustratedPaintedInput.hlsl"
            #include "GafaIllustratedToon.hlsl"
            #include "GafaIllustratedPaintedSurface.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
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

                VertexPositionInputs vpi = GetVertexPositionInputs(input.positionOS.xyz);
                output.uv = input.uv;
                output.positionWS = vpi.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = vpi.positionCS;
            #if !defined(_FOG_FRAGMENT)
                output.fogFactor = ComputeFogFactor(vpi.positionCS.z);
            #endif
                return output;
            }

            void Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                float3 sitePos = GafaIllToSitePos(input.positionWS);
                float3 albedo = GafaIllPaintedAlbedo(sitePos, GafaIllToSiteNormal(normalWS), input.uv);
                float3 N = normalWS * IS_FRONT_VFACE(face, 1.0, -1.0);
                float3 color = GafaToonLighting(albedo, N, input.positionWS, sitePos);
                color = MixFog(color, InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor));
                outColor = half4(color, _Opacity);
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
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "GafaIllustratedPaintedInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
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
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "GafaIllustratedPaintedInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "GafaIllustratedPaintedInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DNVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            void DNFrag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
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

        // 墨线缓冲：法线 / 编号 / 线型（GafaIllustratedFeature 绘制）
        Pass
        {
            Name "GafaInk"
            Tags { "LightMode" = "GafaInk" }
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex InkVert
            #pragma fragment InkFrag
            #pragma multi_compile_instancing

            #include "GafaIllustratedPaintedInput.hlsl"
            #include "GafaIllustratedInk.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings InkVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs vpi = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vpi.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float4 InkFrag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
            #if defined(_SURFACE_TYPE_TRANSPARENT)
                // 玻璃不描线，并清掉其后的墨线（原项目 INK_SKIP + 混合写入）
                return 0;
            #else
                float3 n = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                return float4(GafaOctEncode(n), _InkId, _InkMode);
            #endif
            }
            ENDHLSL
        }

        // 光照贴图烘焙用
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MetaVert
            #pragma fragment MetaFrag
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "GafaIllustratedPaintedInput.hlsl"
            #include "GafaIllustratedPaintedSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                float2 uv2        : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
            #ifdef EDITOR_VISUALIZATION
                float2 VizUV      : TEXCOORD3;
                float4 LightCoord : TEXCOORD4;
            #endif
            };

            Varyings MetaVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
                output.uv = input.uv0;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            #ifdef EDITOR_VISUALIZATION
                UnityEditorVizData(input.positionOS.xyz, input.uv0, input.uv1, input.uv2, output.VizUV, output.LightCoord);
            #endif
                return output;
            }

            half4 MetaFrag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                MetaInput metaInput = (MetaInput)0;
                metaInput.Albedo = GafaIllPaintedAlbedo(GafaIllToSitePos(input.positionWS), GafaIllToSiteNormal(normalWS), input.uv);
                metaInput.Emission = 0;
            #ifdef EDITOR_VISUALIZATION
                metaInput.VizUV = input.VizUV;
                metaInput.LightCoord = input.LightCoord;
            #endif
                return UnityMetaFragment(metaInput);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
