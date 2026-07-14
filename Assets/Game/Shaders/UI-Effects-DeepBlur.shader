// URP-compatible replacement for the legacy GrabPass-based DeepBlur shader.
// GrabPass is not supported in URP. This version samples _CameraOpaqueTexture
// (requires "Opaque Texture" enabled in the URP Renderer Asset).
// Canvas must be Screen Space - Camera or World Space.
Shader "UI/Effects/DeepBlur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Size ("Blur Size", Float) = 1

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
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
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
            Name "DeepBlur"

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // UI sprite (white if unused)
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // Scene colour captured by URP before the transparent pass.
            // Requires "Opaque Texture" ON in the active URP Renderer Asset.
            TEXTURE2D(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                float  _Size;
            CBUFFER_END

            // Unity sets this automatically when _CameraOpaqueTexture is declared.
            float4 _CameraOpaqueTexture_TexelSize; // (1/w, 1/h, w, h)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv         = TRANSFORM_TEX(v.uv, _MainTex);
                o.color      = v.color * _Color;
                o.screenPos  = ComputeScreenPos(o.positionCS);
                return o;
            }

            // 9-tap box blur on the opaque texture.
            // stepUV = _CameraOpaqueTexture_TexelSize.xy * _Size
            half3 SampleBlur(float2 uv, float2 step)
            {
                half3 c;
                c  = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv + float2(-step.x, -step.y)).rgb;
                c += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv + float2(      0, -step.y)).rgb;
                c += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv + float2( step.x, -step.y)).rgb;
                c += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv + float2(-step.x,       0)).rgb;
                c += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv).rgb;
                c += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv + float2( step.x,       0)).rgb;
                c += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv + float2(-step.x,  step.y)).rgb;
                c += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv + float2(      0,  step.y)).rgb;
                c += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv + float2( step.x,  step.y)).rgb;
                return c / 9.0;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Screen-space UV for sampling the opaque texture
                float2 screenUV = i.screenPos.xy / i.screenPos.w;

                float2 step = _CameraOpaqueTexture_TexelSize.xy * _Size;
                half3 blurred = SampleBlur(screenUV, step);

                // Tint the blurred scene with the material color
                half4 result;
                result.rgb = blurred * i.color.rgb;
                result.a   = i.color.a;

                #ifdef UNITY_UI_ALPHACLIP
                    clip(result.a - 0.001);
                #endif

                return result;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}
