Shader "SayGames/ToonWithDissolveEdgesURP"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR]_Color ("Main Color", Color) = (1,1,1,1)

        // Dissolve settings
        [Header(Dissolve Settings)]
        _DissolveTexture("Dissolve Texture", 2D) = "white" {}
        _DissolveThreshold("Dissolve Threshold", Range(0, 1)) = 0
        [HDR]_EdgeColor("Edge Color", Color) = (1, 0, 0, 1)
        _EdgeWidth("Edge Width", Range(0, 0.2)) = 0.02

        // Ramp settings
        [Header(Ramp Properties)]
        [HDR]_HColor ("Highlight Color", Color) = (1,1,1,1)
        [HDR]_SColor ("Shadow Color", Color) = (0.3,0.3,0.3,1)
        _RampThreshold ("Ramp Threshold", Range(0,1)) = 0.5
        _RampSmoothness("Ramp Smoothness", Range(0,1)) = 0

        // Specular settings
        [Header(Specular Properties)]
        [Toggle(GP_USE_SPECULAR)] _UseSpecular ("Enable Specular", Float) = 0
        [HDR]_SpecularColor ("Specular Color", Color) = (0.5,0.5,0.5,1)
        _SpecularSize ("Specular Size", Range(0,1)) = 0.25
        _SpecularSmoothness ("Specular Smoothness", Range(0.001,0.5)) = 0.05
        [Toggle(GP_SPEC_CUSTOM_LIGHT)] _SpecularCustomLight ("Specular: Use Custom Light", Float) = 0
        _SpecularLightDir ("Custom Light Direction", Vector) = (0.3, 0.7, 0.3, 0)

        // Vertical Gradient
        [Header(Gradient Properties)]
        [KeywordEnum(None, Color, Multiply)] _Gradient ("Gradient mode", Float) = 0
        [Toggle(GP_GRADIENT_WORLD)] _GradientWorldSpace ("Gradient in World Space", Float) = 0
        _GradientAxisDir ("Gradient Axis (dir)", Vector) = (0,1,0)
        [Toggle(GP_GRADIENT_AFTER_PLANE_COLOR)] _GradientAfterPlaneColor ("Apply Gradient After Plane Color", Float) = 0
        _GradientStartHeight("Start Height", Float) = 0
        _GradientEndHeight("End Height", Float) = 5
        _GradientColor("Gradient Color", Color) = (0, 0, 0, 1)

        // Rim settings
        [Header(Rim Properties)]
        [Toggle(GP_USE_RIM)] _UseRim ("Enable Rim", Float) = 0
        [HDR]_RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimMin ("Rim Min", Range(0, 1)) = 0.2
        _RimMax ("Rim Max", Range(0, 1)) = 0.5

        // Plane color settings
        [Header(Plane Color Properties)]
        [Toggle(GP_USE_PLANE_COLOR)] _UsePlaneColor ("Enable Plane Color", Float) = 0
        _PlaneColor ("Plane Color", Color) = (1,1,1,1)
        _PlaneColorPower ("Plane Color Power", Range(0, 1)) = 0
        _PlaneColor2 ("Plane Color 2", Color) = (1,1,1,1)
        _PlaneColorPower2 ("Plane Color 2 Power", Range(0, 1)) = 0

        // Shadow settings
        [Header(Shadows)]
        [Toggle] _ReceiveShadows("Receive shadows", Float) = 1

        [HideInInspector] _Cull ("__cull", Float) = 2.0
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);         SAMPLER(sampler_MainTex);
        TEXTURE2D(_DissolveTexture); SAMPLER(sampler_DissolveTexture);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _DissolveTexture_ST;
            half4  _Color;
            half4  _HColor;
            half4  _SColor;
            half   _RampThreshold;
            half   _RampSmoothness;
            half4  _SpecularColor;
            half   _SpecularSize;
            half   _SpecularSmoothness;
            half   _SpecularCustomLight;
            float4 _SpecularLightDir;
            float  _GradientStartHeight;
            float  _GradientEndHeight;
            float4 _GradientAxisDir;
            half   _GradientWorldSpace;
            half4  _GradientColor;
            half4  _PlaneColor;
            half   _PlaneColorPower;
            half4  _PlaneColor2;
            half   _PlaneColorPower2;
            half4  _RimColor;
            half   _RimMin;
            half   _RimMax;
            half   _ReceiveShadows;
            half   _DissolveThreshold;
            half4  _EdgeColor;
            half   _EdgeWidth;
            half   _Cull;
        CBUFFER_END
        ENDHLSL

        // ─────────────────────────────────────────────────────────────
        // Forward lit
        // ─────────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT

            #pragma shader_feature_local_fragment GP_USE_SPECULAR
            #pragma shader_feature_local_fragment GP_SPEC_CUSTOM_LIGHT
            #pragma shader_feature_local_fragment GP_USE_RIM
            #pragma shader_feature_local_fragment GP_USE_PLANE_COLOR
            #pragma shader_feature_local_fragment _GRADIENT_NONE _GRADIENT_COLOR _GRADIENT_MULTIPLY
            #pragma shader_feature_local_fragment GP_GRADIENT_AFTER_PLANE_COLOR
            #pragma shader_feature_local_fragment GP_GRADIENT_WORLD

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                float3 positionOS : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half3 ApplyGradient(float3 posOS, half3 col)
            {
                #if defined(_GRADIENT_COLOR) || defined(_GRADIENT_MULTIPLY)
                    float coord = dot(posOS, SafeNormalize(_GradientAxisDir.xyz));
                    half g = saturate((coord - _GradientStartHeight) / (_GradientEndHeight - _GradientStartHeight));
                #endif
                #if defined(_GRADIENT_COLOR)
                    col = lerp(_GradientColor.rgb, col, lerp(1 - _GradientColor.a, 1, g));
                #endif
                #if defined(_GRADIENT_MULTIPLY)
                    half3 gradientMult = lerp(_GradientColor.rgb, half3(1,1,1), g);
                    col *= gradientMult;
                #endif
                return col;
            }

            Varyings vert (Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs posInputs = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = posInputs.positionCS;
                o.positionWS = posInputs.positionWS;
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.uv         = TRANSFORM_TEX(v.uv, _MainTex);
                o.fogCoord   = ComputeFogFactor(o.positionCS.z);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // --- Dissolve ---
                half dissolveValue = SAMPLE_TEXTURE2D(_DissolveTexture, sampler_DissolveTexture, i.uv).r;
                half dissolveDiff = dissolveValue - _DissolveThreshold;
                clip(dissolveDiff);

                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 N = normalize(i.normalWS);
                half3 L = mainLight.direction;
                half  nl = max(0.0h, dot(N, L));

                half shadow = mainLight.shadowAttenuation * _ReceiveShadows + (1.0h - _ReceiveShadows);
                half ramp = smoothstep(_RampThreshold - _RampSmoothness * 0.5,
                                       _RampThreshold + _RampSmoothness * 0.5, nl) * shadow;
                half4 rampColor = lerp(_SColor, _HColor, ramp);

                col *= rampColor * half4(mainLight.color, 1) * _Color;

                half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);

                #if defined(GP_USE_RIM)
                    half nv  = saturate(dot(N, view));
                    half rim = smoothstep(_RimMin, _RimMax, 1 - nv);
                    col.rgb += rim * _RimColor.rgb;
                #endif

                #if defined(GP_USE_SPECULAR)
                    #if defined(GP_SPEC_CUSTOM_LIGHT)
                        // Artificial specular light: direction set directly in the material.
                        half3 specL = SafeNormalize(_SpecularLightDir.xyz);
                        half3 specLightCol = _SpecularColor.rgb;
                    #else
                        half3 specL = L;
                        half3 specLightCol = mainLight.color * _SpecularColor.rgb;
                    #endif
                    half3 h   = normalize(specL + view);
                    half  ndh = max(0.0h, dot(N, h));
                    half  spec = smoothstep(_SpecularSize + _SpecularSmoothness,
                                            _SpecularSize - _SpecularSmoothness,
                                            1 - (ndh / (1 + _SpecularSmoothness)));
                    col.rgb += spec * specLightCol;
                #endif

                #if defined(GP_GRADIENT_WORLD)
                    float3 gradPos = i.positionWS;
                #else
                    float3 gradPos = i.positionOS;
                #endif

                #if !defined(GP_GRADIENT_AFTER_PLANE_COLOR)
                    col.rgb = ApplyGradient(gradPos, col.rgb);
                #endif

                #if defined(GP_USE_PLANE_COLOR)
                    col.rgb = lerp(col.rgb, _PlaneColor2.rgb, _PlaneColorPower2 * _PlaneColor2.a);
                    col.rgb = lerp(col.rgb, _PlaneColor.rgb,  _PlaneColorPower  * _PlaneColor.a);
                #endif

                #if defined(GP_GRADIENT_AFTER_PLANE_COLOR)
                    col.rgb = ApplyGradient(gradPos, col.rgb);
                #endif

                // --- Glowing dissolve edge ---
                half edgeMask = 0;
                if (_EdgeWidth > 0.0)
                    edgeMask = saturate(1.0 - smoothstep(0.0, _EdgeWidth, dissolveDiff));
                col.rgb += edgeMask * _EdgeColor.rgb * _EdgeColor.a;

                col.rgb = MixFog(col.rgb, i.fogCoord);
                return col;
            }
            ENDHLSL
        }

        // ─────────────────────────────────────────────────────────────
        // Shadow caster (dissolves with the surface)
        // ─────────────────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct AttributesS
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsS
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            VaryingsS ShadowVert (AttributesS v)
            {
                VaryingsS o = (VaryingsS)0;
                UNITY_SETUP_INSTANCE_ID(v);

                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(v.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                o.positionCS = positionCS;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 ShadowFrag (VaryingsS i) : SV_Target
            {
                half d = SAMPLE_TEXTURE2D(_DissolveTexture, sampler_DissolveTexture, i.uv).r;
                clip(d - _DissolveThreshold);
                return 0;
            }
            ENDHLSL
        }

        // ─────────────────────────────────────────────────────────────
        // Depth only (clips identically so depth matches the dissolve)
        // ─────────────────────────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct AttributesD
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsD
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            VaryingsD DepthVert (AttributesD v)
            {
                VaryingsD o = (VaryingsD)0;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 DepthFrag (VaryingsD i) : SV_Target
            {
                half d = SAMPLE_TEXTURE2D(_DissolveTexture, sampler_DissolveTexture, i.uv).r;
                clip(d - _DissolveThreshold);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
