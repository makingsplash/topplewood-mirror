Shader "WoolSort/Background/VerticalGradient3"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.50, 0.75, 1.00, 1.00)
        _CenterColor ("Center Color", Color) = (0.50, 0.75, 1.00, 1.00)
        _BottomColor ("Bottom Color", Color) = (0.50, 0.75, 1.00, 1.00)
        [KeywordEnum(Vertical, Radial)] _GradientMode ("Gradient Mode", Float) = 0
        _RadialCenter ("Radial Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        _RadialRadius ("Radial Radius", Float) = 0.75
        _RadialScaleX ("Radial Scale X", Range(0.1, 4)) = 1
        _RadialScaleY ("Radial Scale Y", Range(0.1, 4)) = 1
        _OverlayTex ("Overlay Texture", 2D) = "white" {}
        _OverlayTilingOffset ("Overlay Tiling (XY) Offset (ZW)", Vector) = (1, 1, 0, 0)
        _OverlayTint ("Overlay Tint", Color) = (1, 1, 1, 1)
        _OverlayStrength ("Overlay Strength", Range(0, 1)) = 0
        _OverlayRadialFade ("Overlay Radial Fade", Range(0, 1)) = 0
        [KeywordEnum(Normal, Multiply, Screen, Overlay, BrightMask)] _OverlayBlend ("Overlay Blend", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry-100"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _GRADIENTMODE_VERTICAL _GRADIENTMODE_RADIAL
            #pragma shader_feature_local_fragment _OVERLAYBLEND_NORMAL _OVERLAYBLEND_MULTIPLY _OVERLAYBLEND_SCREEN _OVERLAYBLEND_OVERLAY _OVERLAYBLEND_BRIGHTMASK

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;  // float: полная точность при интерполяции
            };

            CBUFFER_START(UnityPerMaterial)
                // Цвета: half достаточно — 8 бит на канал с запасом
                half4 _TopColor;
                half4 _CenterColor;
                half4 _BottomColor;
                // Пространственные параметры: float, иначе banding на Android (mediump = FP16)
                float4 _RadialCenter;
                float  _RadialRadius;
                float  _RadialScaleX;
                float  _RadialScaleY;
                float4 _OverlayTilingOffset;
                half4  _OverlayTint;
                half   _OverlayStrength;
                half   _OverlayRadialFade;
            CBUFFER_END

            TEXTURE2D(_OverlayTex);
            SAMPLER(sampler_OverlayTex);

            // Эллиптическое расстояние в float — вся арифметика highp,
            // иначе на мобиле (mediump/FP16) получаем ступенчатый градиент.
            float EllipDist(float2 uv, float2 center, float sx, float sy)
            {
                float2 delta = (uv - center) / max(float2(sx, sy), 0.0001);
                return length(delta);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 color;
                #if defined(_GRADIENTMODE_RADIAL)
                    // t считаем в float — это ключевой lerp-фактор градиента
                    float safeRadius = max(_RadialRadius, 0.0001);
                    float radialT = saturate(EllipDist(input.uv, _RadialCenter.xy, _RadialScaleX, _RadialScaleY) / safeRadius);
                    color = lerp(_CenterColor.rgb, _TopColor.rgb, (half)radialT);
                #else
                    // Вертикальный режим: t тоже в float для плавного перехода
                    float t = saturate(input.uv.y);
                    float blendBottomCenter = saturate(t * 2.0);
                    float blendCenterTop    = saturate((t - 0.5) * 2.0);
                    half3 fromBottom = lerp(_BottomColor.rgb,  _CenterColor.rgb, (half)blendBottomCenter);
                    half3 fromTop    = lerp(_CenterColor.rgb,  _TopColor.rgb,    (half)blendCenterTop);
                    float isTopHalf  = step(0.5, t);
                    color = lerp(fromBottom, fromTop, (half)isTopHalf);
                #endif

                float2 overlayUv = input.uv * _OverlayTilingOffset.xy + _OverlayTilingOffset.zw;
                half4  overlaySample = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, overlayUv);
                half3  overlay = overlaySample.rgb * _OverlayTint.rgb;

                half3 blended;
                half visibilityByBase = 1.0h;
                #if defined(_OVERLAYBLEND_MULTIPLY)
                    blended = color * overlay;
                #elif defined(_OVERLAYBLEND_SCREEN)
                    blended = 1.0h - (1.0h - color) * (1.0h - overlay);
                #elif defined(_OVERLAYBLEND_OVERLAY)
                    half3 low  = 2.0h * color * overlay;
                    half3 high = 1.0h - 2.0h * (1.0h - color) * (1.0h - overlay);
                    blended = lerp(low, high, step(0.5h.xxx, color));
                #elif defined(_OVERLAYBLEND_BRIGHTMASK)
                    blended = overlay;
                    visibilityByBase = dot(color, half3(0.2126h, 0.7152h, 0.0722h));
                #else
                    blended = overlay;
                #endif

                half overlayMask = saturate(_OverlayStrength * overlaySample.a * visibilityByBase);

                // Fade overlay outside the radial radius — тоже float для точности краёв
                float dist      = EllipDist(input.uv, _RadialCenter.xy, _RadialScaleX, _RadialScaleY);
                float softEdge  = max(_RadialRadius * 0.05, 0.005);
                float radialFadeMask = smoothstep(_RadialRadius + softEdge, _RadialRadius - softEdge, dist);
                overlayMask *= lerp(1.0h, (half)radialFadeMask, _OverlayRadialFade);

                color = lerp(color, blended, overlayMask);

                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
