// 술래 가리개 배경: 로고를 엇갈린 줄로 반복해 깔고 천천히 흘러가게 하는 UI 셰이더.
// LogoPatternBackground 컴포넌트가 크기·시간 값을 넣어 준다(직접 이 재질을 만들 필요 없음).
Shader "GameServer/UI/LogoPattern"
{
    Properties
    {
        [PerRendererData] _MainTex ("Logo", 2D) = "white" {}
        _BgColor ("Background Color", Color) = (0.95, 0.95, 0.96, 1)
        _LogoTint ("Logo Tint (A = 진하기)", Color) = (0.98, 0.55, 0.2, 0.35)
        _TintAmount ("Tint Amount (0 = 원래 색)", Range(0, 1)) = 1
        _LineRange ("Line Range (어두운 곳~밝은 곳)", Vector) = (0.3, 0.8, 0, 0)
        _RectSize ("Rect Size", Vector) = (1920, 1080, 0, 0)
        _CellSize ("Cell Size", Vector) = (300, 170, 0, 0)
        _LogoSize ("Logo Size", Vector) = (219, 110, 0, 0)
        _Velocity ("Velocity", Vector) = (40, -25, 0, 0)
        _RowShift ("Row Shift", Range(0, 1)) = 0.5
        _Angle ("Angle (rad)", Float) = 0
        _ScrollTime ("Scroll Time", Float) = 0

        // UI 마스크/스텐실 (UI/Default와 같음)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "False"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _BgColor;
            fixed4 _LogoTint;
            float _TintAmount;
            float4 _LineRange;
            float4 _RectSize;
            float4 _CellSize;
            float4 _LogoSize;
            float4 _Velocity;
            float _RowShift;
            float _Angle;
            float _ScrollTime;
            float4 _ClipRect;

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 화면(UI) 단위 좌표. 가운데 기준으로 돌리고, 시간만큼 흘려보낸다.
                float2 pos = (i.uv - 0.5) * _RectSize.xy;
                float s = sin(_Angle), c = cos(_Angle);
                pos = float2(pos.x * c - pos.y * s, pos.x * s + pos.y * c);
                pos -= _Velocity.xy * _ScrollTime;

                // 칸 나누기: 줄마다 RowShift만큼 옆으로 밀어 엇갈리게
                float2 cellPos = pos / _CellSize.xy;
                cellPos.x += floor(cellPos.y) * _RowShift;
                float2 local = (frac(cellPos) - 0.5) * _CellSize.xy;   // 칸 가운데 기준 위치
                float2 uv = local / _LogoSize.xy + 0.5;

                // 칸 경계에서 밉맵이 튀지 않게 이어진 좌표로 기울기를 구한다.
                float2 g = pos / _LogoSize.xy;
                fixed4 logo = tex2Dgrad(_MainTex, uv, ddx(g), ddy(g));
                float inside = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);

                // 한 가지 색(선화) 모드: 로고의 어두운 선은 Logo Tint 색, 밝은 면은 배경색으로 녹아들게 한다.
                float lum = dot(logo.rgb, float3(0.299, 0.587, 0.114));
                float fade = smoothstep(_LineRange.x, _LineRange.y, lum);
                fixed3 lineCol = lerp(_LogoTint.rgb, _BgColor.rgb, fade);
                fixed3 rgb = lerp(logo.rgb, lineCol, _TintAmount);
                float a = logo.a * _LogoTint.a * inside;

                fixed4 col;
                col.rgb = lerp(_BgColor.rgb, rgb, a / max(_BgColor.a + a * (1 - _BgColor.a), 1e-4));
                col.a = _BgColor.a + a * (1 - _BgColor.a);
                col.a *= i.color.a;   // CanvasGroup 페이드

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
