// Тень под деталью. Не тень движка: детали односторонние плоские веера в ортокамере,
// теневых карт тут нет и заводить их незачем — рисуем копию силуэта, сдвинутую и размытую.
//
// Размытие берётся из того же uv2.x, что фаска в Game/Wood Block: расстояние до кромки
// в МИРОВЫХ ЕДИНИЦАХ (0 на кромке). Поэтому тень мягкая по всему силуэту, включая купола, и не
// требует ни второго меша, ни блюра, а ширина размытия одинакова у большой и маленькой детали.
// Меши без этого канала (кварды, линии) пришли бы залитыми — для них считаем запасную оценку по uv
// (она в долях детали, поэтому приведена к единицам множителем 0.5 — грубо, но такие меши тени не носят).
//
// ВАРИАНТ _CONCAVE_SDF (TASK-368): тень составной freeform-детали. Её меш — веер по каждой выпуклой части,
// и общее ребро двух частей лежит концами на силуэте: uv2.x на нём 0, то есть альфа 0 — тень прорезалась бы
// прозрачной щелью вдоль стыка. Под кейвордом расстояние до кромки считается по CCW-контуру силуэта
// (SilhouetteSdf.hlsl) в объектном пространстве. Тень — дочерний объект с тем же мешем и равномерным
// масштабом, поэтому её объектное пространство совпадает с пространством меша детали и контур тот же.
Shader "Game/Wood Piece Shadow"
{
    Properties
    {
        _ShadowColor ("Цвет тени", Color) = (0.16, 0.06, 0.02, 1)
        _Alpha ("Плотность", Range(0, 1)) = 0.32
        _Soft ("Мягкость края (ед.)", Range(0.005, 1)) = 0.16
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Отсечение", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Shadow"
            Tags { "LightMode" = "UniversalForward" }

            // ZWrite Off: тень не должна перекрывать по глубине саму деталь, которая рисуется
            // в Opaque раньше. ZTest оставляем стандартный — деталь корректно съедает свою тень.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // TASK-368: тень freeform-детали по силуэту. multi_compile — по той же причине, что в Wood Block:
            // материал с кейвордом создаётся в рантайме (PieceShadow.SilhouetteShared).
            #pragma multi_compile_local_fragment _ _CONCAVE_SDF

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SilhouetteSdf.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 fill       : TEXCOORD1;
                float2 positionOS : TEXCOORD2;
            };

            float4 _ShadowColor;
            float _Alpha, _Soft, _Cull;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                OUT.uv = IN.uv;
                OUT.fill = IN.uv2;
                OUT.positionOS = IN.positionOS.xy;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
#ifdef _CONCAVE_SDF
                // TASK-567: есть запечённое поле (масса статики) — знаковое расстояние из него; меш тени массы — квад
                // габарита, и форму вырезает знак (снаружи силуэта альфа 0). Без поля — прежний цикл по контуру
                // (тень freeform-детали): её меш целиком внутри силуэта, беззнакового расстояния хватает.
                float edge;
                UNITY_BRANCH
                if (_SilhouetteFieldRect.z > 0.0)
                    edge = SilhouetteFieldSigned(IN.positionOS);
                else
                {
                    float2 nUnused;
                    edge = SilhouetteEdge(IN.positionOS, nUnused);
                }
#else
                float2 q = abs(IN.uv - 0.5) * 2.0;
                float edgeBox = (1.0 - max(q.x, q.y)) * 0.5;
                float edge = lerp(edgeBox, IN.fill.x, step(0.5, IN.fill.y));
#endif

                float a = _Alpha * smoothstep(0.0, max(_Soft, 0.001), edge);
                return half4(_ShadowColor.rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
