// 달리기 먼지 전용 파티클 셰이더 (URP)
// 그림 없이 동그랗고 가장자리가 울퉁불퉁한 뽀얀 먼지 덩어리를 그린다.
// 파티클 색(Start Color, Color over Lifetime)이 그대로 먼지 색과 투명도가 된다.
Shader "GameServer/DustPuff"
{
    Properties
    {
        _Softness ("Softness", Range(0.05, 1)) = 0.55
        _Lumps ("Lumps", Range(0, 0.4)) = 0.14
        _Shade ("Inner Shade", Range(0, 0.5)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Softness;
                half _Lumps;
                half _Shade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float a = atan2(p.y, p.x);
                // 가장자리를 구름처럼 울퉁불퉁하게
                float edge = 1.0 - _Lumps * (0.5 + 0.5 * sin(a * 5.0 + 1.3) * sin(a * 3.0 - 0.7));
                half shape = 1.0 - smoothstep(edge - _Softness, edge, r);
                // 가운데가 살짝 밝고 가장자리가 살짝 어두운 입체감
                half3 rgb = i.color.rgb * (1.0 - _Shade * saturate(r));
                return half4(rgb, shape * i.color.a);
            }
            ENDHLSL
        }
    }
}
