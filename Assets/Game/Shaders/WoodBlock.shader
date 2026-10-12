// Дерево для деталей и подставки. Волокно, фаска и контур считаются в шейдере —
// текстур под каждый оттенок кубика в проекте нет.
//
// ГЛАВНОЕ ПРАВИЛО ЭТОГО ФАЙЛА: ничего не считать в нормализованных uv.
// Владелец забраковал прежнюю версию словами «на всех кусках смотрится по-разному», и причина была
// ровно в этом: uv нормализованы по габариту детали, поэтому один и тот же градиент давал на широком
// бруске пологую заливку, на узком — резкую полосу, а на треугольнике третье. Теперь свет, фаска,
// контур и волокно живут в МИРОВЫХ единицах, а направление света берётся из нормали кромки
// (канал TEXCOORD3 от PieceMeshBuilder) — она честно поворачивается вместе с кувыркающейся деталью,
// поэтому свет остаётся мировым, а материал одинаков на детали любого размера и формы.
//
// ВТОРОЕ ПРАВИЛО, дописано после «в песочнице красиво, в игре параша»: мировые единицы обязаны быть
// В МАСШТАБЕ ДЕТАЛИ. Толщины настраивались на квадратах песочницы 1.0, а кампания состоит из деталей
// с медианой короткой стороны 0.40 — те же константы там вчетверо грубее и съедают силуэт. Поэтому
// все ширины и шаг волокна домножаются на k = размер детали / _DetailRef. Это НЕ старые
// нормализованные uv: k один на деталь и одинаков по обеим осям, а не свой по каждой.
//
// Заодно убраны _TopSheen / _BottomShade: это был градиент по высоте детали, тот самый «блестит».
// Объём теперь даёт фаска по кромке (светлая со стороны света, тёмная с теневой) и мягкое
// затемнение к краям (_Ao) — обе величины в единицах мира, то есть одинаковые у всех деталей.
//
// Числа не редактируются здесь: ими рулит WoodTuning (ползунки в Play, панель WoodTunerPanel).
// Значения ниже — только запасные, если материал создан без применения настроек.
//
// СОВМЕСТИМОСТЬ, которую нельзя ломать: BlockMaterial.cs красит каждую деталь через
// MaterialPropertyBlock по _BaseColor (и легаси _Color), а меши деталей — односторонние веера
// с нормалью +Z, поэтому Cull держим выключенным и свойство _Cull обязано существовать.
//
// Unlit намеренно: камера смотрит вдоль +Z, детали кувыркаются под физикой, и «свет»
// надёжнее нарисовать самим, чем гонять его через нормали вееров.
//
// ВАРИАНТ _CONCAVE_SDF (TASK-353, TASK-368): кромка по СИЛУЭТУ, а не по рёбрам меша. Им рисуются вогнутая
// ВИДИМАЯ МАССА уровня (сырой ear-clip, ≈51 треуг.) и составная freeform-ДЕТАЛЬ (веер по каждой выпуклой
// части). Ни у того, ни у другого меша дистанция по вершинам не годится: внутренние рёбра лежат концами на
// силуэте, и линейная интерполяция рисовала вдоль них фаску и контур — шов. Под кейвордом расстояние `edge`
// и нормаль ближнего ребра считаются per-fragment по CCW-полилинии контура (_ConcaveOutline, ≤64 узлов,
// SilhouetteSdf.hlsl) в ОБЪЕКТНОМ пространстве: у массы оно совпадает с мировым, у детали кувыркается вместе
// с ней. Прежний путь массы строил контур ПЛОТНЫМ SDF-мешем (100k+ треуг., фриз 4.7 с). Кейворд включён на
// материале BlockMaterial.ConcaveShared; без кейворда (выпуклые детали, полимино, статика) фрагмент
// байт-в-байт прежний. multi_compile, а не shader_feature: материалы создаются в рантайме, и shader_feature
// вырезал бы вариант из player-сборки.
Shader "Game/Wood Block"
{
    Properties
    {
        [MainColor] _BaseColor ("Цвет детали", Color) = (0.85, 0.42, 0.22, 1)
        _Color ("Цвет (легаси)", Color) = (0.85, 0.42, 0.22, 1)

        [Header(Masshtab risunka)]
        // ОДИН масштаб на всю сцену: доска одна, из неё выпилены куски разного размера. Умножает все
        // мировые толщины и во столько же раз сгущает волокно. 1.0 — числа, настроенные в песочнице
        // на квадратах 1.0; 0.4 — те же пропорции в масштабе медианной детали кампании.
        _StyleScale ("Масштаб рисунка дерева", Range(0.15, 1.5)) = 0.4
        // Индивидуальная поправка по размеру КАЖДОЙ детали. Держать в нуле: на единице у крупного
        // куска выходили три жирные прожилки и широкий белый поясок, у мелкого соседа — частая рябь
        // и тонкий кант, и владелец забраковал это как «у каждого куска своя доска».
        _DetailRef ("Опорный размер детали (ед.)", Range(0.1, 3)) = 1
        _DetailScale ("Толщины следуют размеру детали", Range(0, 1)) = 0

        [Header(Volokno)]
        // Именно ЛИНИЙ НА ЕДИНИЦУ МИРА, а не абстрактная частота: кубик уровня ≈ 1 единица,
        // и через него должно проходить несколько прожилок независимо от размера детали.
        // ВАЖНО: «кубик уровня ≈ 1 единица» — неверная посылка, на ней и погорели. В кампании
        // медиана короткой стороны 0.40, минимум 0.18, и при 3 линиях на единицу через типовую
        // деталь проходила ОДНА прожилка. Плотность теперь считается от опорного размера
        // (_DetailRef), а не от мировой единицы.
        _GrainScale ("Линий на опорную деталь", Range(1, 20)) = 3.2
        _GrainStrength ("Сила волокна", Range(0, 1)) = 0.28
        _GrainWarp ("Волнистость", Range(0, 1)) = 0.35
        _GrainSharp ("Резкость линий", Range(1, 12)) = 3.4
        _GrainVary ("Разброс линий", Range(0, 1)) = 0.6
        _GrainEmboss ("Рельеф прожилок", Range(0, 1)) = 0.05
        // Шероховатость МЕЖДУ прожилками, частота в 9 раз выше их. На крупной статике
        // читается песком, поэтому вынесена в ползунок и приглушена по умолчанию.
        _GrainNoise ("Шероховатость", Range(0, 0.5)) = 0.12
        // Угол задаётся ЧИСЛОМ, а не выводится из габарита. Прежний авто-разворот «вдоль длинной
        // стороны» и делал волокно разным у лежачих и стоячих деталей — теперь это отдельный
        // выключатель _GrainAlignLong, по умолчанию выключенный.
        _GrainAngle ("Угол волокна (град.)", Range(-180, 180)) = 0
        _GrainAlignLong ("Разворот вдоль длинной стороны", Range(0, 1)) = 0
        // 0 — волокно у каждой детали своё (как отдельный кубик из набора),
        // 1 — волокно продолжается сквозь фигуру, будто она выпилена из одной доски.
        _GrainSpace ("Волокно: деталь -> фигура", Range(0, 1)) = 0

        [Header(Svet i kromka)]
        // Мировой угол: 0 — свет справа, 90 — сверху. Все кромки детали освещаются по этому
        // направлению, поэтому у повёрнутой детали светлая сторона остаётся сверху.
        _LightAngle ("Угол света (град.)", Range(0, 360)) = 125
        // Ширины — в МИРОВЫХ ЕДИНИЦАХ. В долях детали поясок света выходил тонким у широкого
        // бруска и широкой полосой у узкого — на кадре это читалось как пластиковый блик.
        _BevelWidth ("Ширина фаски (ед.)", Range(0, 0.5)) = 0.09
        _BevelLight ("Свет фаски", Range(0, 1)) = 0.32
        _BevelDark ("Тень фаски", Range(0, 1)) = 0.28
        _RimWidth ("Ширина канта (ед.)", Range(0, 0.2)) = 0.022
        _RimLight ("Свет канта", Range(0, 1)) = 0.2
        _RimDark ("Тень канта", Range(0, 1)) = 0.22
        // Контур по всему силуэту — не зависит от света, отделяет деталь от соседней того же цвета.
        _LineWidth ("Ширина контура (ед.)", Range(0, 0.1)) = 0.014
        _LineDark ("Сила контура", Range(0, 1)) = 0.26
        // Мягкое затемнение к краям: объём «подушкой», одинаковый на любой детали.
        _Ao ("Затемнение к краям", Range(0, 1)) = 0.1
        _AoWidth ("Глубина затемнения (ед.)", Range(0.01, 1)) = 0.34

        [Header(Cvet)]
        _Saturation ("Насыщенность", Range(0.5, 2)) = 1
        _Brightness ("Яркость", Range(0.5, 1.5)) = 1

        // Меши деталей — односторонние веера, и BlockMaterial.cs гасит отсечение в рантайме.
        // Свойство обязано быть объявлено здесь, иначе Cull [_Cull] в SubShader не к чему привязать.
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Отсечение", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull [_Cull]      // BlockMaterial.cs гасит отсечение — свойство обязано существовать

        Pass
        {
            Name "Unlit"
            // Именно UniversalForward: прохода "UniversalUnlit" в URP не существует,
            // с ним пасс молча выкидывается из рендера и деталь пропадает целиком.
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // TASK-353/368: кромка по силуэту (вогнутая масса, freeform-деталь) — расстояние до кромки и
            // нормаль ближнего ребра считаются per-fragment по полилинии контура (_ConcaveOutline).
            // multi_compile (TASK-368, снимает долг TASK-355): материал с кейвордом создаётся в рантайме,
            // ни один ассет сборки на него не ссылается, и shader_feature вырезал бы вариант из player'а.
            #pragma multi_compile_local_fragment _ _CONCAVE_SDF

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SilhouetteSdf.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;   // x: расстояние до кромки (ед.), y: маркер валидности
                float2 uv3        : TEXCOORD2;   // габарит детали (мировые единицы)
                float2 uv4        : TEXCOORD3;   // нормаль кромки сектора, в системе детали
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 fill       : TEXCOORD1;
                float2 size       : TEXCOORD2;
                float2 edgeN      : TEXCOORD3;
                float3 positionOS : TEXCOORD4;
                float3 positionWS : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Per-instance: цвет приходит из MaterialPropertyBlock на каждую деталь.
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            float _StyleScale, _DetailRef, _DetailScale;
            float _GrainScale, _GrainStrength, _GrainWarp, _GrainSharp, _GrainVary, _GrainEmboss, _GrainNoise;
            float _GrainAngle, _GrainAlignLong, _GrainSpace;
            float _LightAngle;
            float _BevelWidth, _BevelLight, _BevelDark;
            float _RimWidth, _RimLight, _RimDark;
            float _LineWidth, _LineDark;
            float _Ao, _AoWidth;
            float _Saturation, _Brightness;
            float _Cull;

            // Контур силуэта (_ConcaveOutline/_ConcaveOutlineCount) объявлен в SilhouetteSdf.hlsl. Заполняется
            // per-renderer через MaterialPropertyBlock — поэтому вне инстанс-буфера и вне UnityPerMaterial
            // (этот шейдер и без того несёт цвет инстанс-буфером, SRP-батчинг ему не положен).

            // --- шум: хеш + сглаженная интерполяция, дальше три октавы ---
            float hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);                 // сглаживание, иначе волокно рябит
                float a = hash21(i), b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int i = 0; i < 3; i++) { s += vnoise(p) * a; p *= 2.03; a *= 0.5; }
                return s;
            }

            /// across — координата ПОПЕРЁК волокна, along — вдоль. Обе в мировых единицах,
            /// поэтому плотность линий одинакова у большой и маленькой детали.
            float woodGrain(float across, float along)
            {
                // Волна: медленный шум сдвигает линии поперёк. Без него линии — прямая зебра.
                float wave = (fbm(float2(along * 0.75, across * 0.5)) - 0.5) * 2.0;
                float lines = across * _GrainScale + wave * _GrainWarp * _GrainScale;

                // Гребень: |sin| в степени даёт тонкую тёмную прожилку с мягкими краями.
                float ridge = pow(saturate(1.0 - abs(sin(3.14159 * lines))), _GrainSharp);

                // Разброс: часть прожилок глубже, часть почти не видна — ровные линии выдают процедурность.
                float vary = fbm(float2(across * 1.7, along * 0.4));
                ridge *= lerp(1.0, vary * 1.9, _GrainVary);

                // Мелкая шероховатость поверх — дерево не бывает идеально гладким между линиями.
                // Частота у неё в ДЕВЯТЬ раз выше прожилки, то есть период около полутора пикселей:
                // на мелкой детали это незаметно, а на крупной статике (плита 2.60×3.90) читается
                // как песок — владелец назвал это «мелкий патерн шумный». Амплитуда вынесена в
                // ползунок: 0 — гладко между прожилками, 0.35 — как было.
                float fine = (fbm(float2(across * 9.0, along * 3.0)) - 0.5) * _GrainNoise;
                return saturate(ridge + fine);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.uv = IN.uv;
                OUT.fill = IN.uv2;
                OUT.size = IN.uv3;
                OUT.edgeN = IN.uv4;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float4 baseCol = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);
                // Легаси-путь: если _BaseColor остался дефолтным, а _Color задан — берём его.
                float4 legacy = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                baseCol.rgb = lerp(baseCol.rgb, legacy.rgb, step(baseCol.a, 0.001));
                baseCol.a = max(baseCol.a, legacy.a);

                float2 sz = max(IN.size, float2(1e-4, 1e-4));
                // Маркер: 1 — меш собран PieceMeshBuilder'ом и несёт кромку с нормалью;
                // 0 — чужой меш (квард зоны воздуха, линия силуэта), для него всё оценивается по uv.
                float mine = step(0.5, IN.fill.y);

                // Множитель толщин. При _DetailScale = 0 он ОДИНАКОВ у всех деталей и равен
                // _StyleScale — одна доска на всю сцену, куски из неё просто разного размера.
                // Индивидуальная поправка (если её включить) берёт КОРОТКУЮ сторону: именно она
                // решает, во сколько раз фаска шире силуэта — на бруске 2.8×0.3 грубой выглядит
                // поперечная кромка, а не продольная.
                float shortSide = min(sz.x, sz.y);
                float perPiece = clamp(shortSide / max(_DetailRef, 1e-3), 0.12, 3.0);
                float k = max(lerp(_StyleScale, perPiece, saturate(_DetailScale)), 1e-3);

                // ------------------------------------------------------------- волокно
                float2 gp = lerp(IN.positionOS.xy, IN.positionWS.xy, _GrainSpace);

                // Порог 1.1, а не «что больше»: у КВАДРАТНОЙ детали микроскопический перевес по высоте
                // разворачивал бы волокно вертикально. Работает только если разворот включён.
                float longIsY = step(sz.x * 1.1, sz.y) * saturate(_GrainAlignLong);
                float ang = radians(_GrainAngle) + longIsY * 1.5707963;
                float sa, ca;
                sincos(ang, sa, ca);
                // Координаты волокна поджимаются под размер детали ДО woodGrain: так вместе с
                // частотой линий сжимаются и волна, и разброс, и мелкая шероховатость — иначе на
                // мелкой детали частые линии плавали бы в крупной волне и читались рябью.
                float along  = ( gp.x * ca + gp.y * sa) / k;
                float across = (-gp.x * sa + gp.y * ca) / k;

                float grain = woodGrain(across, along);

                // Волокно тянем по тону: тёмные прожилки уходят в глубину, светлые промежутки
                // чуть теплее базы. Плоское умножение на серый делает дерево грязным.
                float3 rgb = baseCol.rgb * _Brightness;
                float3 deep = rgb * float3(0.66, 0.55, 0.47);
                float3 lightWood = rgb * float3(1.07, 1.04, 0.96);
                float3 col = lerp(lightWood, deep, saturate(grain) * _GrainStrength);

                // Рельеф прожилки: разница волокна на шаг поперёк себя. Шаг задан в УЖЕ поджатых
                // координатах, то есть он постоянная доля прожилки на детали любого размера.
                float grainShifted = woodGrain(across + 0.035, along);
                col = saturate(col + (grainShifted - grain) * _GrainEmboss);

                // ------------------------------------------------- кромка: сколько и куда
#ifdef _CONCAVE_SDF
                // TASK-353/368: аналитический SDF силуэта. Дистанции по вершинам этому мешу верить нельзя —
                // внутренние рёбра (ear-clip массы, стыки частей freeform-детали) лежат концами на силуэте и
                // дали бы вдоль себя фаску и контур. Расстояние и нормаль ближнего ребра считаются здесь, по
                // CCW-полилинии контура, точно в каждом пикселе и без связи с числом треугольников.
                // Контур в ОБЪЕКТНОМ пространстве: у массы оно совпадает с мировым (меш собран в мире,
                // трансформ — сдвиг по Z без поворота), у детали кувыркается вместе с ней. Поэтому нормаль, как
                // и в per-vertex пути, переводится в мир — свет один на всю сцену.
                float2 nSil;
                float edge = SilhouetteEdge(IN.positionOS.xy, nSil);
                float3 nSilW = TransformObjectToWorldDir(float3(nSil, 0.0), false);
                float2 n = nSilW.xy;
                n = (dot(n, n) < 1e-8) ? float2(0.0, 1.0) : normalize(n);
#else
                // Расстояние до кромки: у своих мешей оно посчитано в единицах прямо в вершине
                // (высота центроида над ребром сектора), у чужих оценивается по габариту.
                float2 duv = min(IN.uv, 1.0 - IN.uv) * sz;
                float edgeFallback = min(duv.x, duv.y);
                float edge = lerp(edgeFallback, IN.fill.x, mine);

                // Нормаль кромки. У чужого меша берём ту сторону габарита, что ближе, — грубо, но
                // деталь хотя бы не заливается ровным светом.
                float2 dsign = sign(IN.uv - 0.5);
                float2 nBox = duv.x < duv.y ? float2(dsign.x, 0.0) : float2(0.0, dsign.y);
                float2 nOS = lerp(nBox, IN.edgeN, mine);

                // В МИР: деталь кувыркается, а свет один на всю сцену. Именно этот перевод и делает
                // светлую сторону всегда верхней, как бы деталь ни повернуло физикой.
                float3 nW3 = TransformObjectToWorldDir(float3(nOS, 0.0), false);
                float2 n = nW3.xy;
                n = (dot(n, n) < 1e-8) ? float2(0.0, 1.0) : normalize(n);
#endif

                float2 L;
                sincos(radians(_LightAngle), L.y, L.x);
                float lit = saturate(dot(n, L) * 0.5 + 0.5);   // 1 — кромка смотрит на свет, 0 — от света

                // ------------------------------------------------------------- объём
                // ВСЕ ширины ниже домножены на k — долю детали от опорного размера. Без него на
                // детали 0.4 фаска 0.067 занимала 17% силуэта и объём читался пластиковой подушкой.
                // Затемнение к краям: даёт «подушку» и держит деталь читаемой на фоне соседней.
                float ao = 1.0 - smoothstep(0.0, max(_AoWidth * k, 0.001), edge);
                col *= 1.0 - _Ao * ao;

                // Фаска — широкий мягкий поясок: светлая со стороны света, тёмная с теневой.
                float bevel = 1.0 - smoothstep(0.0, max(_BevelWidth * k, 0.001), edge);
                float3 bevelCol = lerp(col * (1.0 - _BevelDark),
                                       col + (1.0 - col) * _BevelLight, lit);
                col = lerp(col, bevelCol, bevel);

                // Кант — тонкая линия по самому силуэту поверх фаски.
                float rim = 1.0 - smoothstep(0.0, max(_RimWidth * k, 0.001), edge);
                col += (1.0 - col) * _RimLight * rim * lit;
                col *= 1.0 - _RimDark * rim * (1.0 - lit);

                // Контур — по всей кромке, независимо от света: без него две детали одного цвета
                // слипаются в пятно.
                // Имя не "line": в HLSL это ключевое слово примитива геометрического шейдера.
                float outlineMask = 1.0 - smoothstep(0.0, max(_LineWidth * k, 0.0005), edge);
                col *= 1.0 - _LineDark * outlineMask;

                // Насыщенность — дерево должно быть сочным, а не пыльным.
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(lum.xxx, col, _Saturation);

                return half4(saturate(col), baseCol.a);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
