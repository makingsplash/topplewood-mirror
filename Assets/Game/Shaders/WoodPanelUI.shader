// Деревянная панель для UI: кнопки (DEV, RESTART), рамка превью, подставка.
// Скруглённый прямоугольник со фаской и волокном — рисуется целиком в шейдере,
// поэтому одна и та же панель тянется на любой размер без девятипатчей и без
// спрайтов на каждый размер кнопки.
//
// Скругление считается в ПИКСЕЛЯХ, а не в долях UV: у кнопки и у подставки разные
// пропорции, и радиус в UV дал бы у широкой подставки эллипс вместо круга.
// Размер в пикселях передаётся через _RectSize (ставит WoodPanelUISize.cs).
//
// Держит контракт UI-шейдера Unity: vertex color (в него бьёт Image.color и
// анимация прозрачности), _ClipRect от Mask, стенсиль-компар от RectMask2D.
Shader "Game/Wood Panel UI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite (не используется)", 2D) = "white" {}
        _BaseColor ("Цвет дерева", Color) = (0.72, 0.42, 0.20, 1)
        _DarkColor ("Цвет в тени", Color) = (0.40, 0.20, 0.08, 1)

        [Header(Forma)]
        _RectSize ("Размер в пикселях (xy)", Vector) = (200, 80, 0, 0)
        _Radius ("Радиус угла (px)", Range(0, 120)) = 22
        _Border ("Тёмный контур (px)", Range(0, 20)) = 3
        _BevelSize ("Фаска (px)", Range(0, 40)) = 10
        _BevelLight ("Свет фаски", Range(0, 1)) = 0.5
        _BevelDark ("Тень фаски", Range(0, 1)) = 0.42

        [Header(Volokno)]
        _GrainScale ("Частота волокна", Range(2, 120)) = 30
        _GrainStrength ("Сила волокна", Range(0, 1)) = 0.3
        _GrainAngle ("Угол волокна (град)", Range(0, 180)) = 0

        [Header(Sostoyanie)]
        // 1 = нажато: панель темнеет и фаска гаснет, будто вдавили
        _Press ("Нажатие", Range(0, 1)) = 0
        _Saturation ("Насыщенность", Range(0.5, 2)) = 1.15

        // служебное для UI-стека Unity
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
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
            Name "Default"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 mask       : TEXCOORD1;
            };

            float4 _BaseColor, _DarkColor;
            float4 _RectSize;
            float _Radius, _Border, _BevelSize, _BevelLight, _BevelDark;
            float _GrainScale, _GrainStrength, _GrainAngle;
            float _Press, _Saturation;
            float4 _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;

            float hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int i = 0; i < 3; i++) { s += vnoise(p) * a; p *= 2.03; a *= 0.5; }
                return s;
            }

            // Знаковое расстояние до скруглённого прямоугольника: <0 внутри, 0 на кромке.
            float sdRoundRect(float2 p, float2 half2, float r)
            {
                float2 q = abs(p) - (half2 - r);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                // маска отсечения (Mask/RectMask2D) — в координатах объекта
                OUT.mask = float4(IN.positionOS.xy * 2.0 - _ClipRect.xy - _ClipRect.zw,
                                  0.25 / (0.25 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY)
                                          + abs(_ClipRect.zw - _ClipRect.xy)));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 size = max(_RectSize.xy, float2(1, 1));
                float2 p = (IN.uv - 0.5) * size;          // позиция в пикселях от центра
                float2 half2 = size * 0.5;
                float r = min(_Radius, min(half2.x, half2.y));

                float d = sdRoundRect(p, half2, r);
                // сглаживание кромки на один пиксель — панель не должна быть ступенчатой
                float aa = 1.0;
                float shape = saturate(-d / aa);
                if (shape <= 0.0) discard;

                // --- волокно вдоль заданного угла ---
                float rad = radians(_GrainAngle);
                float2 dir = float2(cos(rad), sin(rad));
                float2 perp = float2(-dir.y, dir.x);
                float2 gp = float2(dot(IN.uv, perp), dot(IN.uv, dir));
                float warp = fbm(gp * 3.0) * 0.8;
                float band = frac(gp.y * _GrainScale + warp * 4.0);
                float grain = smoothstep(0.0, 0.4, band) * (1.0 - smoothstep(0.6, 1.0, band));
                grain = saturate(grain * 0.7 + fbm(gp * float2(4.0, 24.0)) * 0.45);

                float3 wood = _BaseColor.rgb;
                float3 col = lerp(lerp(wood, _DarkColor.rgb, 0.5),
                                  wood * float3(1.18, 1.09, 0.88), grain);
                col = lerp(wood, col, _GrainStrength);

                // --- фаска: по расстоянию от кромки внутрь ---
                float bevel = 1.0 - saturate(-d / max(_BevelSize, 0.001));
                float lit = saturate((-(IN.uv.y - 0.5) + (IN.uv.x - 0.5) * 0.5) * 2.4);
                float3 bevelCol = lerp(col * (1.0 - _BevelDark),
                                       col + (1.0 - col) * _BevelLight, lit);
                col = lerp(col, bevelCol, bevel * (1.0 - _Press * 0.85));

                // тёмный контур по самой кромке
                float border = 1.0 - saturate((-d - _Border) / max(_Border, 0.001));
                col = lerp(col, _DarkColor.rgb * 0.65, saturate(border));

                // нажатие: общий завал и лёгкая тень сверху, как от вдавленной кнопки
                col *= 1.0 - _Press * 0.18;
                col *= 1.0 - _Press * 0.12 * saturate(1.0 - IN.uv.y * 2.0);

                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(lum.xxx, col, _Saturation);

                half4 outCol = half4(saturate(col), shape);
                outCol *= IN.color;                        // Image.color и анимация альфы

                #ifdef UNITY_UI_CLIP_RECT
                float2 m = saturate((abs(IN.mask.xy) - 1.0) * -IN.mask.zw * 0.5 + 0.5);
                outCol.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(outCol.a - 0.001);
                #endif

                return outCol;
            }
            ENDHLSL
        }
    }

    Fallback "UI/Default"
}
