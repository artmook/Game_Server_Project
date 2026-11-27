// 기절 별 전용 셰이더 (URP)
// 별 그림의 알파에는 "별 가장자리까지의 거리"가 들어 있다(0.5 = 가장자리, 안쪽일수록 1, 바깥일수록 0).
// 이 거리로 세 가지를 그린다.
//   1) Core  : 별 몸통 색
//   2) Rim   : 가장자리 안쪽을 밝게 칠한 테두리 빛(림라이트)
//   3) Glow  : 가장자리 바깥으로 퍼져 나가는 빛. 숨 쉬듯 살짝 밝아졌다 어두워진다.
Shader "GameServer/StarGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        [HDR] _CoreColor ("Core Color", Color) = (1, 0.82, 0.15, 1)
        [HDR] _RimColor ("Rim Color", Color) = (1.35, 1.25, 0.75, 1)
        [HDR] _GlowColor ("Glow Color", Color) = (1.4, 0.8, 0.1, 1)
        _RimWidth ("Rim Width", Range(0.02, 0.5)) = 0.07
        _GlowSpread ("Glow Falloff", Range(0.5, 8)) = 2.6
        _GlowStrength ("Glow Strength", Range(0, 4)) = 1.3
        _PulseSpeed ("Pulse Speed", Float) = 5
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Blend One OneMinusSrcAlpha   // 미리 곱한 알파: 빛 부분은 더해지는 느낌으로 보인다
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _RimColor;
                half4 _GlowColor;
                half _RimWidth;
                half _GlowSpread;
                half _GlowStrength;
                half _PulseSpeed;
                half _PulseAmount;
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
                half d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;

                // 1) 별 몸통 (가장자리를 부드럽게)
                half aa = max(fwidth(d), 1e-4) * 0.75;
                half core = smoothstep(0.5 - aa, 0.5 + aa, d);

                // 2) 림라이트: 가장자리에서 가장 밝고 안쪽으로 갈수록 사라진다
                half rim = saturate(1 - (d - 0.5) / _RimWidth);
                rim = rim * rim * core;

                // 3) 바깥으로 퍼지는 빛
                half pulse = 1 + sin(_Time.y * _PulseSpeed) * _PulseAmount;
                half outside = saturate(d / 0.5);
                half glow = pow(outside, _GlowSpread) * (1 - core) * _GlowStrength * pulse;

                half3 body = lerp(_CoreColor.rgb, _RimColor.rgb, rim) * i.color.rgb;
                half fade = i.color.a;
                half3 rgb = (body * core + _GlowColor.rgb * glow) * fade;
                half a = (core + glow * 0.2) * fade;
                return half4(rgb, saturate(a));
            }
            ENDHLSL
        }
    }
}
