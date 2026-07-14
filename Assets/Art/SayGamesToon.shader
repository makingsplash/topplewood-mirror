Shader "SayGames/Toon"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR]_Color ("Main Color", Color) = (1,1,1,1)
        
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
        
        // Vertical Gradient
        [Header(Gradient Properties)]
        [KeywordEnum(None, Color, Multiply)] _Gradient ("Gradient mode", Float) = 0
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
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Pass
        {
            Tags {"LightMode"="ForwardBase"}
            ZWrite On
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_fwdbase
            #pragma shader_feature_local_fragment GP_USE_SPECULAR
            #pragma shader_feature_local_fragment GP_USE_RIM
            #pragma shader_feature_local_fragment GP_USE_PLANE_COLOR
            #pragma shader_feature_local_fragment _GRADIENT_NONE _GRADIENT_COLOR _GRADIENT_MULTIPLY
            #pragma shader_feature_local_fragment GP_GRADIENT_AFTER_PLANE_COLOR
            #pragma multi_compile_instancing
            #include <AutoLight.cginc>
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            struct appdata
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                float3 normal : NORMAL;
                float4 vertexWorld : COLOR0;
                SHADOW_COORDS(2)
            };
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _HColor;
            fixed4 _SColor;
            fixed _RampThreshold;
            fixed _RampSmoothness;
            fixed4 _SpecularColor;
            fixed _SpecularSize;
            fixed _SpecularSmoothness;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed, _GradientStartHeight)
                UNITY_DEFINE_INSTANCED_PROP(fixed, _GradientEndHeight)
                UNITY_DEFINE_INSTANCED_PROP(fixed, _PlaneColorPower)
                UNITY_DEFINE_INSTANCED_PROP(fixed, _PlaneColorPower2)
            UNITY_INSTANCING_BUFFER_END(Props)
            fixed4 _GradientColor;
            fixed4 _PlaneColor;
            fixed4 _PlaneColor2;
            fixed4 _RimColor;
            fixed _RimMin;
            fixed _RimMax;
            fixed _ReceiveShadows;

            fixed4 ApplyGradient(v2f i, fixed4 col)
            {
                #if defined(_GRADIENT_COLOR)
                
                fixed gradientValue = saturate((i.vertexWorld.y - UNITY_ACCESS_INSTANCED_PROP(Props, _GradientStartHeight))/(UNITY_ACCESS_INSTANCED_PROP(Props, _GradientEndHeight) - UNITY_ACCESS_INSTANCED_PROP(Props, _GradientStartHeight)));
                col.rgb = lerp(_GradientColor.rgb, col.rgb, lerp(1 - _GradientColor.a, 1, gradientValue));
                
                #endif
                
                // Gradient - multiply mode
                #if defined(_GRADIENT_MULTIPLY)
                
                fixed gradientValue = saturate((i.vertexWorld.y - UNITY_ACCESS_INSTANCED_PROP(Props, _GradientStartHeight))/(UNITY_ACCESS_INSTANCED_PROP(Props, _GradientEndHeight) - UNITY_ACCESS_INSTANCED_PROP(Props, _GradientStartHeight)));
                fixed4 gradientMultCol = lerp(_GradientColor, fixed4(1,1,1,1), gradientValue);
                col *= gradientMultCol;
                #endif
                return col;
            
            }
            
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_SETUP_INSTANCE_ID(v)
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);

                o.normal = UnityObjectToWorldNormal(v.normal);
                o.vertexWorld = mul(unity_ObjectToWorld, v.vertex);

                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed4 col = tex2D(_MainTex, i.uv);
                
                const half nl = max(0, dot(i.normal, _WorldSpaceLightPos0.xyz));
                const fixed shadow = SHADOW_ATTENUATION(i) * _ReceiveShadows + (1 - _ReceiveShadows);
                const half ramp = smoothstep(_RampThreshold - _RampSmoothness * 0.5, _RampThreshold + _RampSmoothness * 0.5, nl) * shadow;
                const half4 rampColor = lerp(_SColor, _HColor, ramp);

                col *= rampColor * _LightColor0 * _Color;

                fixed3 view = normalize(_WorldSpaceCameraPos - i.vertexWorld.xyz);

                // Rim
                #if defined(GP_USE_RIM)
                
                const fixed nv = saturate(dot(i.normal, view));
                const fixed rim = smoothstep(_RimMin, _RimMax, 1 - nv);
                col += rim * _RimColor;
                
                #endif
                // Specular
                #if defined(GP_USE_SPECULAR)
                
                half3 h = normalize(_WorldSpaceLightPos0.xyz + view);
                float ndh = max(0, dot (i.normal, h));
                float spec = smoothstep(_SpecularSize + _SpecularSmoothness, _SpecularSize - _SpecularSmoothness, 1 - (ndh / (1 + _SpecularSmoothness)));
                
                half4 specColor = spec * _LightColor0 * _SpecularColor;
                
                col += specColor;
                
                #endif
                #if !defined(GP_GRADIENT_AFTER_PLANE_COLOR)
                col.rgb = ApplyGradient(i, col);
                #endif 
                #if defined(GP_USE_PLANE_COLOR)
                col.rgb = lerp(col.rgb, _PlaneColor2.rgb, UNITY_ACCESS_INSTANCED_PROP(Props, _PlaneColorPower2) * _PlaneColor2.a);
                col.rgb = lerp(col.rgb, _PlaneColor.rgb, UNITY_ACCESS_INSTANCED_PROP(Props, _PlaneColorPower) * _PlaneColor.a);
                #endif
                #if defined(GP_GRADIENT_AFTER_PLANE_COLOR)
                col.rgb = ApplyGradient(i, col);
                #endif 
                
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
        
         Pass
        {
            Tags {"LightMode"="ShadowCaster"}
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct v2f { 
                UNITY_VERTEX_INPUT_INSTANCE_ID
                V2F_SHADOW_CASTER;
            };
            v2f vert(appdata_base v)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                
                UNITY_SETUP_INSTANCE_ID(v);
                
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
}