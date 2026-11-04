// GAFA 插画版树冠材质：外观与 GAFA/Illustrated/Foliage 完全相同（直接复用其各通道），
// 只改墨线通道 —— 整棵树冠写同一个墨线编号，后期的“叶团边”规则因此只在树冠与天空 / 树干 / 地面 / 别的树交界处描线，
// 树冠内部叶片之间不再描边（莫比乌斯式：只描整体外轮廓）。
// 编号取物体原点（同一棵树的 LeavesA / LeavesB 共享）；静态合批后原点为 0，退回渲染器包围盒中心。
Shader "GAFA/Illustrated/Foliage Canopy"
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
        [Enum(None, 0, Canopy Outline, 2)] _InkMode ("Ink Mode", Float) = 2
        [HideInInspector] _InkPerItem ("Ink Id Per Item (unused)", Float) = 0
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

        UsePass "GAFA/Illustrated/Foliage/FORWARDLIT"
        UsePass "GAFA/Illustrated/Foliage/SHADOWCASTER"
        UsePass "GAFA/Illustrated/Foliage/DEPTHONLY"
        UsePass "GAFA/Illustrated/Foliage/DEPTHNORMALS"

        // 墨线缓冲：整棵树冠一个编号
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 clump      : TEXCOORD3;
                nointerpolation float inkId : TEXCOORD4; // 不插值：插值后的 1499.9999 写进半精度缓冲会截成 1499，同一树冠内出现假边
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float GafaCanopyInkId()
            {
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                float3 key = dot(origin, origin) > 1e-6 ? origin : GafaIllRendererCenter();
                float k = floor(GafaIllHash12(floor(key.xz * 4.0) * 0.137 + floor(key.y * 4.0) * 0.071 + 0.11) * 997.0);
                return GafaIllMod(_InkId + k * 7.0, 2000.0) + 1.0;
            }

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
                output.inkId = GafaCanopyInkId();
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
