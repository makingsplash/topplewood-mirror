Shader "SayStudio/UnlitDissolve"
{ 
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        
        _DissolveTexture("Texture", 2D) = "white" {}
        _DissolveThreshold("Dissolve Threshold", Range(0, 1)) = 0
        _DissolveAreaSize("Dissolve Area Size", Range(0, 1)) = 0.1
        _EdgeColor("Edge Color", Color) = (0, 0, 0, 0 )
        _DissolveEdgeMultiplier ("Dissolve Edge Multiplier", Float) = 1
        _DissolveUvMultiplier("Dissolve UV Multiplier", Range(0, 1)) = 0.3
        
    }
    SubShader
    {
		Tags
		{
			"RenderType" = "Transparent" "Queue" = "Transparent"
		}
        
		Blend SrcAlpha OneMinusSrcAlpha
        ZTest [unity_GUIZTestMode]
	    ZWrite Off
	    Fog { Mode Off }
 
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
 
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Input
            {
                float3 worldPos;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv_dissolve : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            
            sampler2D _DissolveTexture;
            float4 _DissolveTexture_ST;
            
            float _DissolveThreshold;
            float4 _EdgeColor;
            float _DissolveAreaSize;
            float _DissolveEdgeMultiplier;
            float _DissolveUvMultiplier;
            
            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv_dissolve = v.vertex * _DissolveUvMultiplier;
                o.color = v.color;
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i.input);

                fixed4 col = tex2D(_MainTex, i.uv) * i.color;
                fixed4 dissolveValue = tex2D(_DissolveTexture, TRANSFORM_TEX(i.uv_dissolve, _DissolveTexture));

                half dissolveClip = dissolveValue.r - _DissolveThreshold;
                half edgeRamp = max(0, _DissolveAreaSize - dissolveClip);

                col = lerp( col, _EdgeColor, min(1, edgeRamp * _DissolveEdgeMultiplier) );

                clip(dissolveClip);
                return col;
            }ENDCG
        }
    }
}
