// Аналитическое расстояние до СИЛУЭТА — общий код вариантов _CONCAVE_SDF шейдеров Game/Wood Block и
// Game/Wood Piece Shadow.
//
// Зачем: меш составной детали (freeform, TASK-368) и вогнутой массы (TASK-353) состоит из нескольких
// треугольников/частей, и их ВНУТРЕННИЕ рёбра лежат концами на силуэте. Дистанция до кромки, записанная по
// вершинам, на таком ребре равна нулю на обоих концах и линейно остаётся нулём по всей длине — шейдер рисует
// вдоль него фаску и контур, то есть шов. Поэтому дистанцию считает фрагмент по самой полилинии силуэта.
//
// Контур — CCW-полилиния в ОБЪЕКТНОМ пространстве рендерера (один узел на элемент, .xy; .zw не используются),
// приходит per-renderer из MaterialPropertyBlock (BlockMaterial.SetSilhouette). Массив ВСЕГДА пишется на все 64
// элемента: Unity фиксирует длину массива при первом присваивании и молча обрезает более длинный позже.
// 64 — потолок, единый источник лимита на C#-стороне — BlockMaterial.SilhouetteMax.
//
// Это без знака: фрагменты меша детали всегда внутри силуэта (sdPolygon И. Квилеса без проверки чётности).
#ifndef GAME_SILHOUETTE_SDF_INCLUDED
#define GAME_SILHOUETTE_SDF_INCLUDED

float4 _ConcaveOutline[64];
float _ConcaveOutlineCount;

/// Расстояние от p до ближайшего ребра силуэта (в единицах объектного пространства) и наружная нормаль кромки
/// в этой точке (тоже в объектном пространстве, нормированная; запасная (0,1) на вырожденном ребре).
///
/// Нормаль в КЛИНЕ ВОГНУТОЙ ВЕРШИНЫ (TASK-368). Внутри детали у вогнутой (reflex) вершины есть клин, где ближайшая
/// точка — сама вершина, и оба соседних ребра дают одно и то же расстояние. Нормаль «ближайшего ребра» там выбирается
/// шумом округления (у одного ребра вершина получается как a + e·1, у другого — как a), и фаска рвётся рваным штрихом
/// по биссектрисе: так выглядела выемка обезьянки coconut_palm. Поэтому в клине (t зажат в 0 или 1) нормаль — это
/// направление на ближайшую точку, то есть градиент поля расстояний: он поворачивается от нормали одного ребра к
/// нормали другого плавно, а кромка огибает вершину дугой, как настоящая фаска постоянной ширины. На самих рёбрах
/// (0 < t < 1) нормаль прежняя — нормаль ребра — и кадр там байт-в-байт тот же.
float SilhouetteEdge(float2 p, out float2 n)
{
    int cnt = (int)_ConcaveOutlineCount;
    float bestSq = 1e20;
    float2 nBest = float2(0.0, 1.0);
    float2 qBest = p;
    float atVertex = 0.0;
    [loop] for (int i = 0; i < cnt; i++)
    {
        float2 a = _ConcaveOutline[i].xy;
        float2 b = _ConcaveOutline[(i + 1) % cnt].xy;
        float2 e = b - a;
        float ee = max(dot(e, e), 1e-12);
        float t = saturate(dot(p - a, e) / ee);   // segment-clamp: точно в углах контура
        // Вершину берём ТОЧНО (a или b), а не a + e·t: иначе два ребра одной вершины дают её с разной ошибкой.
        float2 q = (t <= 0.0) ? a : ((t >= 1.0) ? b : a + e * t);
        float2 dq = p - q;
        float dsq = dot(dq, dq);
        if (dsq < bestSq)
        {
            bestSq = dsq;
            nBest = float2(e.y, -e.x);            // наружу для CCW-контура: (dy, −dx)
            qBest = q;
            atVertex = (t <= 0.0 || t >= 1.0) ? 1.0 : 0.0;
        }
    }
    n = (dot(nBest, nBest) < 1e-8) ? float2(0.0, 1.0) : normalize(nBest);
    if (atVertex > 0.5 && bestSq > 1e-12)
        n = (qBest - p) * rsqrt(bestSq);          // градиент: от точки к кромке, то есть наружу
    return sqrt(bestSq);
}

// --- кромка силуэта: ОДНА выборка запечённого поля (TASK-557) ---
// SilhouetteField.cs запекает расстояние СО ЗНАКОМ и наружную нормаль по тому же правилу, что SilhouetteEdge,
// в объектном пространстве рендерера. _SilhouetteFieldRect.z = 0 — поля нет (не запеклось), и вызывающий
// остаётся на цикле по рёбрам. Ветка по uniform'у: у всех пикселей рендерера она одна, расхождения нет.
// TASK-567: объявления переехали сюда из Game/Wood Block — поле читает и тень (Game/Wood Piece Shadow).
// Требует Core.hlsl (макросы TEXTURE2D/SAMPLER) до себя — оба шейдера включают его первым.
TEXTURE2D(_SilhouetteField);
SAMPLER(sampler_SilhouetteField);
float4 _SilhouetteFieldRect;

float4 SilhouetteFieldSample(float2 p)
{
    return SAMPLE_TEXTURE2D_LOD(_SilhouetteField, sampler_SilhouetteField, (p - _SilhouetteFieldRect.xy) * _SilhouetteFieldRect.zw, 0);
}

float SilhouetteFieldEdge(float2 p, out float2 n)
{
    float4 s = SilhouetteFieldSample(p);
    // Нормали соседних текселей по разные стороны биссектрисы смешиваются билинейно; в узкой перемычке
    // встречные нормали гасят друг друга — тогда запасная (0,1), как у SilhouetteEdge на вырожденном ребре.
    float nn = dot(s.yz, s.yz);
    n = (nn < 1e-6) ? float2(0.0, 1.0) : s.yz * rsqrt(nn);
    return max(s.x, 0.0);
}

/// TASK-567: расстояние СО ЗНАКОМ (внутри +, снаружи −) — для тени массы. Её меш — квад габарита, и форму тени
/// вырезает именно знак: снаружи силуэта smoothstep от отрицательного расстояния даёт альфу 0. Выборка за краем
/// текстуры зажимается на крайний тексел, а он снаружи кромки (SilhouetteField.Pad), то есть тоже альфа 0.
float SilhouetteFieldSigned(float2 p)
{
    return SilhouetteFieldSample(p).x;
}

#endif
