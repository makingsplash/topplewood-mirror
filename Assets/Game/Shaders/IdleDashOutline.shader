// TASK-614: пунктир вокруг ВИСЯЩЕЙ (ещё не тапнутой) детали. Владелец после плейтеста: «не понятно визуально,
// что фигуры в воздухе не активны, пока по ним не тапнешь».
//
// Приём — штрих по ДЛИНЕ ДУГИ в фрагменте (Rougier, «Shader-Based Antialiased, Dashed, Stroked Polylines», JCGT 2013):
// меш — одна лента вдоль контура (IdleDashOutline.BuildMesh), в uv0.x лежит пройденная длина пути, в uv0.y — смещение
// поперёк ленты, оба в МИРОВЫХ единицах; в uv1.x — полная длина замкнутого пути. Штрихи режет фрагмент, а не геометрия:
// меш не пересобирается от шага пунктира, и форма детали может быть любой (выпуклая, полимино, freeform).
//
// Семантика — как у SVG stroke-dasharray + stroke-linecap: round (так нарисован мокап арт-директора): _DashLength —
// длина ЯДРА штриха, круглые концы добавляют по полтолщины с каждой стороны. Конец штриха — SDF капсулы, края
// сглажены на один пиксель (fwidth), поэтому пунктир не рябит на любом масштабе камеры.
//
// Замкнутый путь: период подгоняется так, чтобы на длину пути легло ЦЕЛОЕ число штрихов, — иначе на стыке начала и
// конца пути получился бы обрубок или двойной штрих. Сдвиг периода — доли процента на реальных деталях.
//
// Порядок: Transparent, ZWrite Off, обычный ZTest. Лента стоит по глубине чуть ЗА деталями (IdleDashOutline.ZBehind),
// непрозрачные детали пишут глубину — поэтому пунктир лежит под ЛЮБОЙ деталью, в том числе под соседней висящей
// (требование арт-директора: иначе он ляжет на краску соседа), и над стеной и тенью.
Shader "Game/Idle Dash Outline"
{
    Properties
    {
        _Color ("Цвет", Color) = (0.933, 0.855, 0.753, 1)
        _Width ("Толщина (ед.)", Float) = 0.047
        _DashLength ("Штрих (ед.)", Float) = 0.147
        _DashGap ("Пробел (ед.)", Float) = 0.093
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "IdleDash"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;   // x: длина пути, y: поперёк ленты (ед.)
                float2 uv1        : TEXCOORD1;   // x: полная длина замкнутого пути
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 path       : TEXCOORD0;
                float  pathLength : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Width, _DashLength, _DashGap;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                OUT.path = IN.uv;
                OUT.pathLength = IN.uv1.x;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float halfWidth = _Width * 0.5;

                // Целое число периодов на замкнутый путь; толщина при этом не меняется — только шаг.
                // Период — как у SVG: штрих + пробел; круглые концы съедают пробел по полтолщины с каждой стороны.
                float period = max(_DashLength + _DashGap, 1e-4);
                float count = max(1.0, round(IN.pathLength / period));
                float fit = IN.pathLength / count / period;
                period *= fit;
                float core = _DashLength * fit;

                // Ядро штриха — [0, core] внутри периода; круглый конец перед ним тянется в хвост ПРОШЛОГО периода,
                // поэтому расстояние берём до ближайшего из двух центров.
                float local = IN.path.x - floor(IN.path.x / period) * period;
                float center = core * 0.5;
                float along = min(abs(local - center), abs(local - period - center));
                float2 q = float2(max(along - center, 0.0), IN.path.y);
                float dist = length(q) - halfWidth;

                float aa = max(fwidth(IN.path.y), 1e-5);
                float alpha = saturate(0.5 - dist / aa) * _Color.a;
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
