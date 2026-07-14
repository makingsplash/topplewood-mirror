Shader "SayStudio/RainbowWaveMobileFast"
{
    Properties
    {
        // ── Rainbow ──────────────────────────────────────────────────────────
        [Header(Rainbow)]
        _RainbowSpeed     ("  Speed (0=static)",          Range(-4.0, 4.0)) = 0.15
        _RainbowFrequency ("  Frequency (0.5=1 cycle)",   Range(0.1, 4.0))  = 0.5
        _RainbowOffset    ("  Hue Offset",                Range(0.0, 1.0))  = 0.0
        _RainbowSharpness ("  Sharpness (0=smooth 1=hard)", Range(0.0, 1.0)) = 0.0
        _RainbowBands     ("  Bands count",               Range(2, 12))    = 7
        _RainbowDirection ("  Direction (X=horiz Y=vert)", Vector)          = (1, 0, 0, 0)

        // ── Highlight 1 (большой мягкий) ─────────────────────────────────────
        [Header(Highlight 1)]
        _H1Strength ("  Strength",          Range(0, 1))   = 0.883
        _H1Radius   ("  Radius",            Range(0.1, 4)) = 0.2
        _H1Softness ("  Softness",          Range(1, 16))  = 4.2
        _H1X        ("  Pos X (neg=left)",  Range(-2, 2))  = 0.726
        _H1Y        ("  Pos Y (pos=top)",   Range(-2, 2))  = 1.0
        _H1Color    ("  Color",             Color)         = (1, 1, 1, 1)

        // ── Highlight 2 (маленький острый) ───────────────────────────────────
        [Header(Highlight 2)]
        _H2Strength ("  Strength",          Range(0, 1))    = 0.27
        _H2Radius   ("  Radius",            Range(0.05, 4)) = 0.14
        _H2Softness ("  Softness",          Range(1, 16))   = 2.91
        _H2X        ("  Pos X (neg=left)",  Range(-2, 2))   = -0.81
        _H2Y        ("  Pos Y (pos=top)",   Range(-2, 2))   = -1.06
        _H2Color    ("  Color",             Color)          = (1, 1, 1, 1)

        // ── Highlight 3 (линзовый — от края к центру) ────────────────────────
        [Header(Highlight 3)]
        _H3Strength  ("  Strength",          Range(0, 1))    = 0.083
        _H3Radius    ("  Radius",            Range(0.1, 4))  = 1.75
        _H3Width     ("  Outer Fade",        Range(0.01, 1)) = 0.01
        _H3Softness  ("  Softness",          Range(1, 16))   = 4.39
        _H3X         ("  Pos X (neg=left)",  Range(-2, 2))   = -1.17
        _H3Y         ("  Pos Y (pos=top)",   Range(-2, 2))   = 0.94
        _H3Lightness ("  Lightness (цвет от радуги)", Range(0, 2)) = 0.349

        // ── Rim Light ────────────────────────────────────────────────────────
        [Header(Rim Light)]
        _RimStrength  ("  Strength",   Range(0, 1)) = 0.487
        _RimPower     ("  Sharpness",  Range(1, 8)) = 1.0
        _RimLightness ("  Lightness",  Range(0, 2)) = 0.815

        // ── Color Adjust ─────────────────────────────────────────────────────
        [Header(Color Adjust)]
        _Saturation ("  Saturation (1=original)", Range(0, 2)) = 1.549
        _Brightness ("  Brightness (1=original)", Range(0, 2)) = 1.0

        // ── Gradient ─────────────────────────────────────────────────────────
        [Header(Gradient)]
        [HDR] _GradientTopColor    ("  Top Color",       Color)       = (1, 1, 1, 1)
        _GradientTopStrength    ("  Top Strength",    Range(0, 1)) = 0.0
        [HDR] _GradientBottomColor ("  Bottom Color",    Color)       = (0.3, 0.3, 0.3, 1)
        _GradientBottomStrength ("  Bottom Strength",  Range(0, 1)) = 1.0

        // ── Vignette ─────────────────────────────────────────────────────────
        [Header(Vignette)]
        _VignetteStrength ("  Strength",    Range(0, 0.5)) = 0.265
        [HDR] _VignetteColor ("  Edge Color", Color)       = (2, 2, 2, 1)

        // ── Tile Edge Shadow ─────────────────────────────────────────────────
        [Header(Tile Edge Shadow)]
        _TileEdgeDarken ("  Darken (0=чёрный 1=цвет тайла)", Range(0, 1))     = 0.25
        _TileEdgeWidth  ("  Width",                          Range(0.01, 0.4)) = 0.08
        _TileEdgeOffsetY ("  Offset Y (+ вверх - вниз)",     Range(-0.3, 0.3)) = 0.0

        // ── Lighting ─────────────────────────────────────────────────────────
        [Header(Lighting)]
        _Ambient         ("  Ambient", Range(0, 1)) = 1.0
        _DiffuseStrength ("  Diffuse", Range(0, 1)) = 1.0

        [HideInInspector] _Cull("__cull", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3  normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                half2  cuv        : TEXCOORD1;
                half3  viewDirWS  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                half  _RainbowSpeed;
                half  _RainbowFrequency;
                half  _RainbowOffset;
                half  _RainbowSharpness;
                half  _RainbowBands;
                half4 _RainbowDirection;

                half  _H1Strength; half _H1Radius; half _H1Softness;
                half  _H1X; half _H1Y; half4 _H1Color;

                half  _H2Strength; half _H2Radius; half _H2Softness;
                half  _H2X; half _H2Y; half4 _H2Color;

                half  _H3Strength; half _H3Radius; half _H3Softness; half _H3Width;
                half  _H3X; half _H3Y; half _H3Lightness;

                half  _RimStrength; half _RimPower; half _RimLightness;

                half  _Saturation;
                half  _Brightness;
                half  _GradientTopStrength;
                half  _GradientBottomStrength;
                half4 _GradientTopColor;
                half4 _GradientBottomColor;

                half  _VignetteStrength;
                half4 _VignetteColor;
                half  _TileEdgeDarken;
                half  _TileEdgeWidth;
                half  _TileEdgeOffsetY;
                half  _Ambient;
                half  _DiffuseStrength;
            CBUFFER_END

            // Candy-палитра: 8 насыщенных цветов как на рефе
            // t = 0..1, возвращает плавный градиент между цветами
            half3 CandyPalette(half t)
            {
                half s = frac(t) * 8.0h;

                half3 c0 = half3(1.00h, 0.15h, 0.60h); // hot pink
                half3 c1 = half3(1.00h, 0.08h, 0.05h); // red
                half3 c2 = half3(1.00h, 0.52h, 0.00h); // orange
                half3 c3 = half3(1.00h, 0.95h, 0.00h); // yellow
                half3 c4 = half3(0.20h, 0.92h, 0.05h); // lime green
                half3 c5 = half3(0.00h, 0.80h, 0.80h); // teal/cyan
                half3 c6 = half3(0.10h, 0.45h, 1.00h); // blue
                half3 c7 = half3(0.65h, 0.05h, 0.90h); // purple

                half3 col = c0;
                col = lerp(col, c1, saturate(s - 0.0h));
                col = lerp(col, c2, saturate(s - 1.0h));
                col = lerp(col, c3, saturate(s - 2.0h));
                col = lerp(col, c4, saturate(s - 3.0h));
                col = lerp(col, c5, saturate(s - 4.0h));
                col = lerp(col, c6, saturate(s - 5.0h));
                col = lerp(col, c7, saturate(s - 6.0h));
                col = lerp(col, c0, saturate(s - 7.0h)); // wrap to pink
                return col;
            }

            half Highlight(half2 uv, half2 center, half radius, half softness)
            {
                half2 d = uv - center;
                d.x *= 0.75h;
                return pow(saturate(1.0h - length(d) / max(radius, 0.01h)), softness);
            }

            half RingHighlight(half2 uv, half2 center, half radius, half width, half softness)
            {
                half2 d = uv - center;
                d.x *= 0.75h;
                half dist  = length(d);
                half inner = pow(saturate(dist / max(radius, 0.01h)), softness);
                half outer = saturate(1.0h - (dist - radius) / max(width, 0.01h));
                return inner * outer;
            }

            Varyings vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(input.normalOS);

                output.positionCS = pos.positionCS;
                output.normalWS   = NormalizeNormalPerVertex(nrm.normalWS);
                output.viewDirWS  = half3(GetWorldSpaceNormalizeViewDir(pos.positionWS));
                float3 tileCenter = mul(UNITY_MATRIX_M, float4(0, 0, 0, 1)).xyz;
                output.cuv = half2(pos.positionWS.x - tileCenter.x,
                                   pos.positionWS.y - tileCenter.y) * 2.0h;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half3 normalWS = input.normalWS; // already normalized per vertex

                half2 cuv = input.cuv;

                // ── Rainbow base color ───────────────────────────────────────
                half2 dir     = _RainbowDirection.xy;
                half  len2    = max(dot(dir, dir), 0.0001h);
                half2 waveDir = dir * rsqrt(len2);
                half  phase   = dot(cuv, waveDir) * _RainbowFrequency
                               + _RainbowOffset + _Time.y * _RainbowSpeed;
                // Sharpness: 0=плавный, 1=резкие полосы (_RainbowBands штук)
                half hue      = frac(phase);
                // snap to START каждой полосы — тогда каждый band = чистый цвет
                half sharpHue = floor(hue * _RainbowBands) / _RainbowBands;
                hue           = lerp(hue, sharpHue, _RainbowSharpness);
                half3 rainbowColor = CandyPalette(hue);

                // ── Diffuse ──────────────────────────────────────────────────
                Light mainLight = GetMainLight();
                half  ndotl    = saturate(dot(normalWS, mainLight.direction));
                half3 color    = rainbowColor
                               * saturate(_Ambient + ndotl * _DiffuseStrength)
                               * mainLight.color;

                // ── Saturation + Brightness ──────────────────────────────────
                half lum = dot(color, half3(0.299h, 0.587h, 0.114h));
                color    = lerp(half3(lum,lum,lum), color, _Saturation) * _Brightness;

                // ── Gradient ─────────────────────────────────────────────────
                half gradientT = saturate(cuv.y * 0.5h + 0.5h);
                color *= lerp(half3(1,1,1), _GradientTopColor.rgb,    gradientT          * _GradientTopStrength);
                color *= lerp(half3(1,1,1), _GradientBottomColor.rgb, (1.0h - gradientT) * _GradientBottomStrength);

                // ── Vignette ─────────────────────────────────────────────────
                half vigFactor = saturate(dot(cuv, cuv) * _VignetteStrength);
                color *= lerp(half3(1,1,1), _VignetteColor.rgb, vigFactor);

                // ── Rim light — наследует цвет радуги в точке ────────────────
                half3 viewDir  = normalize(input.viewDirWS);
                half  rim      = pow(1.0h - saturate(dot(normalWS, viewDir)), _RimPower) * _RimStrength;
                half3 rimColor = rainbowColor + half3(_RimLightness, _RimLightness, _RimLightness);
                color         += rimColor * rim;

                // ── Highlight 1 ───────────────────────────────────────────────
                half h1 = Highlight(cuv, half2(_H1X, _H1Y), _H1Radius, _H1Softness) * _H1Strength;
                color   = lerp(color, _H1Color.rgb, h1);

                // ── Highlight 2 ───────────────────────────────────────────────
                half h2 = Highlight(cuv, half2(_H2X, _H2Y), _H2Radius, _H2Softness) * _H2Strength;
                color   = lerp(color, _H2Color.rgb, h2);

                // ── Highlight 3 — цвет от радуги в точке блика ───────────────
                half h3 = RingHighlight(cuv, half2(_H3X, _H3Y), _H3Radius, _H3Width, _H3Softness) * _H3Strength;
                half3 h3Color = rainbowColor + half3(_H3Lightness, _H3Lightness, _H3Lightness);
                color   = lerp(color, h3Color, h3);

                // ── Tile Edge Shadow — последний, затемняет итоговый цвет ────
                half edgeT = smoothstep(0.0h, _TileEdgeWidth, 1.0h - abs(cuv.y + _TileEdgeOffsetY));
                color *= lerp(_TileEdgeDarken, 1.0h, edgeT);

                return half4(saturate(color), 1.0h);
            }
            ENDHLSL
        }
    }
}
