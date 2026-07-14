#ifndef UI_BLUR_INCLUDED
#define UI_BLUR_INCLUDED

#pragma target 2.0

#include "UnityCG.cginc"
#include "UnityUI.cginc"

#pragma multi_compile_local _ UNITY_UI_CLIP_RECT
#pragma multi_compile_local _ UNITY_UI_ALPHACLIP

#pragma fragmentoption ARB_precision_hint_fastest

struct appdata_t
{
    float4 vertex   : POSITION;
    float4 color    : COLOR;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    float4 vertex         : SV_POSITION;
    fixed4 color          : COLOR;
    float2 texcoord       : TEXCOORD0;
    float4 worldPosition  : TEXCOORD1;
    float4 mask           : TEXCOORD2;
    float4 screenPosition : TEXCOORD3;
    UNITY_VERTEX_OUTPUT_STEREO
};

sampler2D _MainTex;
fixed4 _Color;
fixed4 _TextureSampleAdd;
float4 _ClipRect;
float4 _MainTex_ST;
float _UIMaskSoftnessX;
float _UIMaskSoftnessY;

half _Size;
sampler2D _GrabTexture;
half4 _GrabTexture_TexelSize;

v2f vert(appdata_t v)
{
    v2f OUT;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
    float4 vPosition = UnityObjectToClipPos(v.vertex);
    OUT.worldPosition = v.vertex;
    OUT.vertex = vPosition;

    float2 pixelSize = vPosition.w;
    pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

    float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
    float2 maskUV = (v.vertex.xy - clampedRect.xy) / (clampedRect.zw - clampedRect.xy);
    OUT.texcoord = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
    OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

    OUT.screenPosition = ComputeGrabScreenPos(OUT.vertex);

    OUT.color = v.color * _Color;
    return OUT;
}

fixed4 frag(v2f IN) : SV_Target
{
    half4 color = IN.color * (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd);

    #ifdef UNITY_UI_CLIP_RECT
    half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
    color.a *= m.x * m.y;
    #endif

    #ifdef UNITY_UI_ALPHACLIP
    clip (color.a - 0.001);
    #endif

    color.rgb *= color.a;

    return color;
}

half4 blur(half4 color, float4 uv, half w, half kx, half ky)
{
    half4 result = tex2Dproj(
        _GrabTexture,
        UNITY_PROJ_COORD(
            half4(
                uv.x + _GrabTexture_TexelSize.x * kx * _Size * color.a,
                uv.y + _GrabTexture_TexelSize.y * ky * _Size * color.a,
                uv.z,
                uv.w)));

    result.rgb *= w;
    result.a = w;

    return result;
}

half4 blur_hor(v2f IN) : SV_Target
{
    const half4 c = frag(IN);

    half4 color = 0;
    color += blur(c, IN.screenPosition, 0.05, -4.0, 0.0);
    color += blur(c, IN.screenPosition, 0.09, -3.0, 0.0);
    color += blur(c, IN.screenPosition, 0.12, -2.0, 0.0);
    color += blur(c, IN.screenPosition, 0.15, -1.0, 0.0);
    color += blur(c, IN.screenPosition, 0.18,  0.0, 0.0);
    color += blur(c, IN.screenPosition, 0.15, +1.0, 0.0);
    color += blur(c, IN.screenPosition, 0.12, +2.0, 0.0);
    color += blur(c, IN.screenPosition, 0.09, +3.0, 0.0);
    color += blur(c, IN.screenPosition, 0.05, +4.0, 0.0);
    color.rgb = lerp(color.rgb, c.rgb * color.rgb, c.a);

    return color;
}

half4 blur_ver(v2f IN) : SV_Target
{
    const half4 c = frag(IN);

    half4 color = 0;
    color += blur(c, IN.screenPosition, 0.05, 0.0, -4.0);
    color += blur(c, IN.screenPosition, 0.09, 0.0, -3.0);
    color += blur(c, IN.screenPosition, 0.12, 0.0, -2.0);
    color += blur(c, IN.screenPosition, 0.15, 0.0, -1.0);
    color += blur(c, IN.screenPosition, 0.18, 0.0,  0.0);
    color += blur(c, IN.screenPosition, 0.15, 0.0, +1.0);
    color += blur(c, IN.screenPosition, 0.12, 0.0, +2.0);
    color += blur(c, IN.screenPosition, 0.09, 0.0, +3.0);
    color += blur(c, IN.screenPosition, 0.05, 0.0, +4.0);
    color.rgb = lerp(color.rgb, c.rgb * color.rgb, c.a);

    return color;
}

#endif // UI_BLUR_INCLUDED
