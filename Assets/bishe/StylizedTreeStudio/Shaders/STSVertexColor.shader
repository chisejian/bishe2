Shader "StylizedTreeStudio/VertexColor"
{
    Properties
    {
        _Color ("整体颜色 / Tint", Color) = (1,1,1,1)
        _MainTex ("贴图 / Texture", 2D) = "white" {}
        [Toggle] _UseTexture ("使用贴图 / Use Texture", Float) = 0
        _Cutoff ("透明裁切 / Alpha Cutoff", Range(0,1)) = 0.4
        _ShadeColor ("暗面颜色 / Shade", Color) = (0.57,0.65,0.62,1)
        _LightDirection ("造型光方向 / Art Light Direction", Vector) = (0.4,0.8,0.3,0)
        _ShadeSteps ("明暗层数 / Shade Steps", Range(1,6)) = 3
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex;
    float4 _MainTex_ST, _Color, _ShadeColor, _LightDirection;
    float _UseTexture, _Cutoff, _ShadeSteps;
    struct appdata_sts
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float2 uv : TEXCOORD0;
        float4 color : COLOR;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
    struct v2f_sts
    {
        float4 position : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 normal : TEXCOORD1;
        float4 color : COLOR;
        UNITY_VERTEX_OUTPUT_STEREO
    };
    v2f_sts vert_sts(appdata_sts input)
    {
        UNITY_SETUP_INSTANCE_ID(input);
        v2f_sts output;
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.position = UnityObjectToClipPos(input.vertex);
        output.normal = UnityObjectToWorldNormal(input.normal);
        output.uv = TRANSFORM_TEX(input.uv, _MainTex);
        output.color = input.color * _Color;
        return output;
    }
    fixed4 frag_sts(v2f_sts input) : SV_Target
    {
        fixed4 sampleColor = lerp(fixed4(1,1,1,1), tex2D(_MainTex, input.uv), saturate(_UseTexture));
        fixed4 color = input.color * sampleColor;
        clip(color.a - _Cutoff);
        float lightValue = saturate(dot(normalize(input.normal), normalize(_LightDirection.xyz + float3(0.0001,0,0))) * 0.5 + 0.5);
        float steps = max(1, floor(_ShadeSteps));
        lightValue = floor(lightValue * steps) / steps;
        color.rgb *= lerp(_ShadeColor.rgb, float3(1,1,1), lightValue);
        return fixed4(color.rgb, 1);
    }
    ENDCG
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Back ZWrite On
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            CGPROGRAM
            #pragma vertex vert_sts
            #pragma fragment frag_sts
            #pragma multi_compile_instancing
            ENDCG
        }
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Back ZWrite On
        Pass
        {
            Tags { "LightMode"="Always" }
            CGPROGRAM
            #pragma vertex vert_sts
            #pragma fragment frag_sts
            #pragma multi_compile_instancing
            ENDCG
        }
    }
    Fallback Off
}
