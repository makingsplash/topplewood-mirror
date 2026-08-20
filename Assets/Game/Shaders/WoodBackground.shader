// Фон: стена из вертикальных досок. Заменяет VerticalGradientBackground.shader.
// Доски разной ширины со своим оттенком и своим волокном, между ними тёмный шов;
// поверх — мягкая виньетка, чтобы центр экрана читался светлее краёв.
//
// Швы намеренно НЕ по сетке кубиков и не по равным долям: равные доски выдают
// процедурность мгновенно. Ширина берётся из хеша индекса доски.
Shader "Game/Wood Background"
{
    Properties
    {
        _BaseColor ("Цвет дерева", Color) = (0.66, 0.35, 0.13, 1)
        _DarkColor ("Цвет в тени", Color) = (0.34, 0.15, 0.05, 1)

        [Header(Doski)]
        _PlankCount ("Досок по ширине", Range(2, 24)) = 7
        _PlankVary ("Разброс ширины", Range(0, 1)) = 0.45
        _PlankTint ("Разброс оттенка", Range(0, 0.6)) = 0.17
        _SeamWidth ("Ширина шва", Range(0, 0.06)) = 0.02
        _SeamDark ("Глубина шва", Range(0, 1)) = 0.72

        [Header(Volokno)]
        _GrainScale ("Частота волокна", Range(4, 200)) = 26
        _GrainStrength ("Сила волокна", Range(0, 1)) = 0.3
        _GrainWarp ("Изгиб волокна", Range(0, 3)) = 0.5
        _KnotAmount ("Сучки", Range(0, 1)) = 0.22

        [Header(Svet)]
        // Держим слабыми: сильный свет сверху выбеливал верх экрана в розовое,
        // и стена переставала читаться как одна плоскость.
        _Vignette ("Виньетка", Range(0, 1.5)) = 0.2
        _TopLight ("Свет сверху", Range(0, 1)) = 0.05
        _Saturation ("Насыщенность", Range(0.5, 2)) = 1.1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" }
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings  { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float4 _BaseColor, _DarkColor;
            float _PlankCount, _PlankVary, _PlankTint, _SeamWidth, _SeamDark;
            float _GrainScale, _GrainStrength, _GrainWarp, _KnotAmount;
            float _Vignette, _TopLight, _Saturation;

            float hash11(float n) { return frac(sin(n * 78.233) * 43758.545); }
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
                for (int i = 0; i < 4; i++) { s += vnoise(p) * a; p *= 2.07; a *= 0.5; }
                return s;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // --- раскладка досок неравной ширины ---
                // Идём слева, накапливая ширину, пока не накроем uv.x. Досок мало,
                // поэтому линейный проход дешевле любой аналитики.
                float x = uv.x * _PlankCount;
                float idx = floor(x);
                float shift = (hash11(idx * 3.71) - 0.5) * _PlankVary;   // сдвиг границы доски
                float local = frac(x) + shift * 0.5;
                float plankId = idx + step(1.0, local) - step(local, 0.0);
                local = frac(local);

                // Шов: не линия, а мягкая щель. Резкая кромка читается натянутой проволокой,
                // поэтому берём широкий плавный спад и гасим его к середине доски.
                float seam = smoothstep(_SeamWidth * 2.5, 0.0, local)
                           + smoothstep(1.0 - _SeamWidth * 2.5, 1.0, local);
                seam = seam * seam;

                // --- цвет доски: свой оттенок + своё смещение волокна ---
                float tint = (hash11(plankId * 9.13) - 0.5) * 2.0 * _PlankTint;
                float3 wood = _BaseColor.rgb * (1.0 + tint);

                // Волокно идёт ВДОЛЬ доски: полосы задаются координатой ПОПЕРЁК (local),
                // а изгиб — медленной модуляцией по высоте. Если строить полосы от uv.y,
                // получается рябь поперёк доски — дерево так не выглядит.
                float2 gp = float2(local, uv.y);
                float warp = fbm(float2(local * 3.0, uv.y * 0.7)) * _GrainWarp;
                float rings = local * _GrainScale + warp * 3.0 + plankId * 13.0;
                float band = frac(rings * 0.5);
                float grain = smoothstep(0.0, 0.4, band) * (1.0 - smoothstep(0.6, 1.0, band));
                grain = saturate(grain * 0.7 + fbm(float2(local * 26.0, uv.y * 4.0)) * 0.45);

                float3 light = wood * float3(1.18, 1.08, 0.86);
                float3 deep = lerp(wood, _DarkColor.rgb, 0.55);
                float3 col = lerp(deep, light, grain);
                col = lerp(wood, col, _GrainStrength);

                // сучки: редкие тёмные пятна с уплотнением волокна вокруг
                float2 kp = float2(local * 1.5 + plankId * 5.0, uv.y * 3.0);
                float knot = fbm(kp * 3.0);
                float knotMask = smoothstep(0.72, 0.86, knot) * _KnotAmount;
                col = lerp(col, _DarkColor.rgb * 0.85, knotMask);

                // шов
                col = lerp(col, _DarkColor.rgb * (1.0 - _SeamDark), saturate(seam));

                // свет сверху и виньетка к краям (uv.y растёт вверх — свет при uv.y → 1)
                col += (1.0 - col) * _TopLight * saturate(uv.y * 1.6 - 0.6);
                float2 c = (uv - 0.5) * float2(1.05, 1.0);
                col *= 1.0 - saturate(dot(c, c)) * _Vignette;

                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(lum.xxx, col, _Saturation);

                return half4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
