Shader "WoolSort/UI/RadialGradient"
{
    Properties
    {
        _InnerColor ("Inner Color", Color) = (1, 1, 1, 1)
        _OuterColor ("Outer Color", Color) = (0, 0, 0, 1)
        _Center ("Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        _Radius ("Radius", Float) = 0.7
        _OverlayTex ("Overlay Texture", 2D) = "white" {}
        _OverlayTilingOffset ("Overlay Tiling (XY) Offset (ZW)", Vector) = (1, 1, 0, 0)
        _OverlayTint ("Overlay Tint", Color) = (1, 1, 1, 1)
        _OverlayStrength ("Overlay Strength", Range(0, 1)) = 0
        [KeywordEnum(Normal, Multiply, Screen, Overlay, BrightMask)] _OverlayBlend ("Overlay Blend", Float) = 0

        // Required by Unity UI
        [HideInInspector] _MainTex ("Main Texture", 2D) = "white" {}
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
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
            Name "UIRadialGradient"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #pragma shader_feature_local_fragment _OVERLAYBLEND_NORMAL _OVERLAYBLEND_MULTIPLY _OVERLAYBLEND_SCREEN _OVERLAYBLEND_OVERLAY _OVERLAYBLEND_BRIGHTMASK

            CBUFFER_START(UnityPerMaterial)
                half4 _InnerColor;
                half4 _OuterColor;
                half4 _Center;
                half  _Radius;
                half4 _OverlayTilingOffset;
                half4 _OverlayTint;
                half  _OverlayStrength;
            CBUFFER_END

            TEXTURE2D(_OverlayTex);
            SAMPLER(sampler_OverlayTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half4  color       : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half safeRadius = max(_Radius, 0.0001h);
                half t = saturate(distance(input.uv, _Center.xy) / safeRadius);
                half3 color = lerp(_InnerColor.rgb, _OuterColor.rgb, t);
                half  alpha = lerp(_InnerColor.a,   _OuterColor.a,   t);

                half2 overlayUv = input.uv * _OverlayTilingOffset.xy + _OverlayTilingOffset.zw;
                half4 overlaySample = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, overlayUv);
                half3 overlay = overlaySample.rgb * _OverlayTint.rgb;

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

                half overlayMask = saturate(_OverlayStrength * (1.0h - t) * overlaySample.a * visibilityByBase);
                color = lerp(color, blended, overlayMask);

                half4 col = half4(color, alpha);
                col *= input.color;
                return col;
            }
            ENDHLSL
        }
    }
}
