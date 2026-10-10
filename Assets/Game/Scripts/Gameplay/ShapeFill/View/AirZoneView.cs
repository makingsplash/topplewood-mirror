using Gameplay.Core;
using UnityEngine;
using Rect = Gameplay.Core.Rect; // disambiguate from UnityEngine.Rect

namespace Gameplay.View
{
    /// <summary>
    /// Unity view for one directed air-flow zone (TASK-097 propeller/fan). Three layers, all built once at level
    /// load (never per-frame), all nudged behind the pieces on +Z so falling pieces read in front:
    /// (1) a TRANSLUCENT fill mesh whose outline is the force <see cref="AirZone.Region"/> byte-for-byte, so §D5
    ///     "the visible area == the force area" holds by construction (the mesh is built from the very coords the
    ///     physics adapter tests membership against);
    /// (2) a procedural EMITTER bar on the up-wind edge — the propeller housing, drawn procedurally (owner PNG
    ///     decode: propeller.png has no alpha, would render as a white patch) → placeholder-grade art, folded into
    ///     TASK-082's pre-soft-launch art pass, NOT a new debt;
    /// (3) tiled ARROW sprites along the flow, loaded like <c>TutorialView</c>'s finger (Resources.Load + graceful
    ///     fallback). If the sprite is missing the fill + emitter still convey the zone and its source side.
    /// The domain stays Unity-free; all Vec2⇄Vector2 lives here.
    /// </summary>
    public sealed class AirZoneView : MonoBehaviour
    {
        // Pieces live at z=0; larger z = further from the +Z-facing camera (see BlockMaterial). Keep the whole zone
        // behind pieces, and layer fill < emitter < arrows within the zone so the cue reads over the fill.
        //
        // Поток стоит ЗА статикой (0.5) и визуальной массой (0.55), а не на одной глубине с массой: на 0.55
        // ленты ветра ложились поверх рампы kangaroo_pouch (TASK-448). Перед тенями деталей на стене (0.9).
        private const float ZFill = 0.88f;
        private const float ZEmitter = 0.6f;
        private const float ZFlow = 0.72f;

        /// <summary>Полуширина полосы глубины, в которой живёт поток: 0.6..0.84, то есть строго между
        /// вентилятором/массой спереди и заливкой/тенями сзади. Камера ортографическая — на кадре
        /// глубины не видно, она решает только, кто кого перекрывает.</summary>
        private const float FlowDepthHalf = 0.12f;

        /// <summary>Лицо арта вентилятора смотрит на +Y (кадр 456×252 — наклонённый диск), поэтому
        /// поворот к потоку отсчитывается отсюда, а не от +X как у стрелки.</summary>
        private const float FanPointsDeg = 90f;

        /// <summary>Вентилятор над заливкой зоны, поток под ним, но оба под деталями.</summary>
        private const int FanSortingOrder = -50;

        private const int FlowSortingOrder = -60;

        private const string DonorWindPath = "WindFX/Wind_FX";

        /// <summary>Потолок добавки к рождаемости частиц. Короткая зона делит время жизни на большое
        /// число, и без потолка компенсация густоты упёрлась бы в лимит частиц системы.</summary>
        private const float MaxDensityBoost = 8f;

        /// <summary>На какую длину зоны собран донорский эффект: его эмиттеры стоят на локальном
        /// z = −12 от корня, то есть весь объём ветра тянется примерно на 12 мировых единиц.</summary>
        private const float DonorWindNativeSpan = 12f;

        /// <summary>Своя ШИРИНА донорского потока — линейный эмиттер внутри префаба длиной 5.
        /// По ней считается поправка размера частиц в <see cref="ShapeFlow"/>.</summary>
        private const float DonorWindNativeCross = 5f;

        /// <summary>Заливка — самый дальний слой зоны, ПОД потоком и вентилятором.</summary>
        private const int FillSortingOrder = -80;

        private SpriteRenderer _fanRenderer;
        private Sprite[] _fanFrames;
        private AirZone _zone;
        private bool _built;

        private const float EmitterThickness = 0.18f;   // world units, along the flow axis (placeholder-grade)
        private const float ArrowWorldSize = 0.6f;       // target on-screen arrow size, world units
        private const float ArrowSpacing = 0.9f;         // gap between tiled arrows along the flow
        private const int ArrowMaxCount = 8;             // cap so a huge zone does not spawn hundreds of sprites

        // arrow-icon-1178.png relocated from the repo root into Resources (TASK-097). Owner byte-decode: 512×512,
        // RGBA, genuinely transparent background ⇒ usable as a Sprite as-is. Assumed to point +X in the source art;
        // if the pass in TASK-082 supplies a differently-oriented glyph, only ARROW_POINTS_DEG changes.
        private const string ArrowResourcePath = "Gameplay/arrow-icon-1178";
        private const float ArrowPointsDeg = 0f; // source art points +X; rotate to the flow from here

        // Тон заливки по умолчанию; АЛЬФУ отсюда не берут — она ползунок WoodTuning.AirFillAlpha.
        // Путь числа: 0.28 (плита поверх стены, глушила и поток, и детали) → 0.12 → ползунок.
        private static readonly Color DefaultFill = new Color(0.30f, 0.70f, 1.00f, 0.12f);
        private static readonly Color EmitterColor = new Color(0.55f, 0.58f, 0.62f);       // neutral housing grey

        private static Sprite _arrowSprite;
        private static bool _arrowLoaded;

        public void Initialize(AirZone zone)
        {
            _zone = zone;
            _built = true;
            Build();
        }

        // Размер вентилятора и форма конуса едут от ползунков панели, а строится зона один раз на
        // загрузке уровня. Без пересборки владелец крутил бы слайдер и не видел ничего до перезапуска.
        private void OnEnable() => WoodTuning.Changed += Rebuild;

        private void OnDisable() => WoodTuning.Changed -= Rebuild;

        private void Rebuild()
        {
            if (!_built)
                return; // подписка встаёт раньше Initialize — строить ещё нечего
            for (var i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            _fanRenderer = null;
            _fanFrames = null;
            Build();
        }

        private void Build()
        {
            var zone = _zone;
            var region = zone.Region;
            var min = new Vector2(region.Min.X, region.Min.Y);
            var max = new Vector2(region.Max.X, region.Max.Y);
            var centre = (min + max) * 0.5f;
            var dir = new Vector2(zone.Direction.X, zone.Direction.Y);
            if (dir.sqrMagnitude > 0f)
                dir = dir.normalized; // domain already normalises; guard the direct-constructed path too

            BuildFill(region, zone.Color);
            BuildFan(min, max, centre, dir);

            // Донорский эффект — основной. Свой процедурный поток остаётся запасным: если префаб
            // не найден или его выключили, зона не должна остаться без движения воздуха.
            if (!BuildDonorWind(min, max, centre, dir))
                BuildFlow(min, max, centre, dir, zone.Color);

            // Стрелки — запасной путь: поток частиц показывает направление нагляднее, но если кадров
            // вентилятора нет или партиклы выключены, без стрелок зона осталась бы немой.
            if (WoodTuning.Current.AirArrows || WoodArt.FanFrames.Length == 0)
                BuildArrows(min, max, centre, dir);
        }

        /// <summary>
        /// Вентилятор на ВХОДНОЙ грани зоны, развёрнутый лицом по потоку.
        ///
        /// Лицо арта смотрит ВВЕРХ: кадр 456×252 — это диск, наклонённый от зрителя, а не круг.
        /// Поэтому поворот считается от +Y. Возьми мы +X, как у стрелки, — вентилятор, дующий
        /// влево, встал бы плашмя.
        ///
        /// Размер привязан к ПОПЕРЕЧНОМУ пролёту зоны: вентилятор закрывает грань, из которой дует.
        /// Своя ширина кадра 4.56 мировых единиц, а грани зон кампании 1.1..2.5 — то есть арт всегда
        /// ужимается и никогда не растягивается.
        /// </summary>
        private void BuildFan(Vector2 min, Vector2 max, Vector2 centre, Vector2 dir)
        {
            var frames = WoodArt.FanFrames;
            if (frames.Length == 0 || dir.sqrMagnitude <= 0f)
                return;

            var half = (max - min) * 0.5f;
            var perp = new Vector2(-dir.y, dir.x);
            var extentAlong = Mathf.Abs(dir.x) * half.x + Mathf.Abs(dir.y) * half.y;
            var crossSpan = 2f * (Mathf.Abs(perp.x) * half.x + Mathf.Abs(perp.y) * half.y);

            // Ширина вентилятора ЗАЖАТА потолком. Поперечный пролёт зоны — это сторона, ПЕРПЕНДИКУЛЯРНАЯ
            // потоку: у горизонтального потока это ВЫСОТА зоны, а она доходит до 6.2 (piggy_bank).
            // Без потолка вентилятор растягивался до 5.9 единиц при своих 4.56 — то есть увеличивался
            // и уезжал за край экрана.
            var tuning = WoodTuning.Current;
            var fanWidth = FanWidth(crossSpan, tuning);
            var spriteWidth = frames[0].bounds.size.x;
            var scale = spriteWidth > 1e-4f ? fanWidth / spriteWidth : 1f;

            // Корпус чуть выступает ЗА входную грань наружу: иначе лопасти тонут в заливке зоны,
            // и вентилятор читается дующим сам в себя.
            var halfDepth = frames[0].bounds.size.y * 0.5f * scale;

            // Вылет ЗАЖАТ резервом кадра. За входной гранью вентилятор занимает halfDepth (сам корпус)
            // плюс сдвиг наружу, и вся сумма обязана уложиться в AirZoneFanReserve — ровно под столько
            // камера держит место. Разойдутся — арт снова уедет за край на узком телефоне, с чего всё
            // и началось.
            var outset = Mathf.Min(halfDepth * tuning.FanOutset,
                CameraFramingMath.AirZoneFanReserve - halfDepth);
            var pos = centre - dir * (extentAlong + outset);

            var go = new GameObject("Fan");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(pos.x, pos.y, ZEmitter);
            go.transform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - FanPointsDeg);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            _fanRenderer = go.AddComponent<SpriteRenderer>();
            _fanRenderer.sprite = frames[0];
            _fanRenderer.sortingOrder = FanSortingOrder;
            _fanFrames = frames;
        }

        /// <summary>
        /// Ширина вентилятора в мировых единицах — ОДНО место на вью: её просит и сам вентилятор, и
        /// конус потока (он выходит из лопастей, а не шире их).
        ///
        /// Верхний зажим двойной: потолок владельца (<c>FanMaxWidth</c>) и коробка, которую камера
        /// резервирует в кадре. Второй сильнее — из него нельзя вырасти, даже задрав ползунок.
        /// </summary>
        private static float FanWidth(float crossSpan, WoodTuning tuning)
        {
            var ceiling = Mathf.Min(tuning.FanMaxWidth, 2f * CameraFramingMath.AirZoneFanReserve);
            return Mathf.Clamp(crossSpan * tuning.FanFaceFill, Mathf.Min(tuning.FanMinWidth, ceiling), ceiling);
        }

        /// <summary>Кадры крутит код, без Animator: сцены здесь собираются кодом, вешать контроллер
        /// не на что.</summary>
        private void Update()
        {
            if (_fanRenderer == null || _fanFrames == null || _fanFrames.Length < 2)
                return;
            var period = Mathf.Max(WoodTuning.Current.FanSpinPeriod, 0.01f);
            var index = Mathf.FloorToInt(Time.time / period * _fanFrames.Length) % _fanFrames.Length;
            _fanRenderer.sprite = _fanFrames[index];
        }

        /// <summary>
        /// Настоящий эффект ветра, перенесённый из соседнего проекта вместе с текстурами.
        ///
        /// ОСЬ ДУТЬЯ У ПРЕФАБА — ЕГО ЛОКАЛЬНАЯ +Z. Это не догадка: у донора он развёрнут кватернионом
        /// (−0.7071, 0, 0, 0.7071), то есть на −90° по X, что как раз уводит +Z в мировой +Y — а его
        /// вентилятор дует вверх. Тот же разворот даёт <c>LookRotation(поток, мировой +Z)</c>.
        ///
        /// КОРЕНЬ СТОИТ НИЖЕ ПО ПОТОКУ. Эмиттеры внутри префаба висят на локальном z = −12, и у донора
        /// корень поднят на 12 единиц от вентилятора — тогда эмиттеры садятся ровно на лопасти.
        /// Повторяем это, домножив на масштаб.
        ///
        /// МАСШТАБ обязателен: эффект собран под зону длиной около 12 мировых единиц при скорости
        /// частиц 40, а наши зоны 1.1..6.4. Без сжатия частицы улетают за кадр за один кадр.
        /// Режим <c>Hierarchy</c> — чтобы вместе с формой ужимались и размер частиц, и скорость;
        /// в режиме Local сжалась бы только форма, и поток всё равно бы вылетал.
        /// </summary>
        private bool BuildDonorWind(Vector2 min, Vector2 max, Vector2 centre, Vector2 dir)
        {
            var tuning = WoodTuning.Current;
            if (!tuning.DonorWind || dir.sqrMagnitude <= 0f)
                return false;

            var prefab = Resources.Load<GameObject>(DonorWindPath);
            if (prefab == null)
            {
                Debug.unityLogger.Log(LogType.Warning,
                    $"[air] префаб ветра '{DonorWindPath}' не найден — включаю запасной поток");
                return false;
            }

            var half = (max - min) * 0.5f;
            var perp = new Vector2(-dir.y, dir.x);
            var extentAlong = Mathf.Abs(dir.x) * half.x + Mathf.Abs(dir.y) * half.y;
            var crossSpan = 2f * (Mathf.Abs(perp.x) * half.x + Mathf.Abs(perp.y) * half.y);
            var span = 2f * extentAlong;
            var faceCentre = centre - dir * extentAlong;

            var scale = span / DonorWindNativeSpan * tuning.DonorWindScale;

            var go = Object.Instantiate(prefab, transform);
            go.name = "Wind";

            // СНАЧАЛА ТИШИНА, ПОТОМ НАСТРОЙКА, ПОТОМ ПУСК.
            //
            // Instantiate в живую сцену будит системы с playOnAwake сразу, и один кадр эффект живёт
            // в натуральную величину — 12 единиц длиной, частицы по 2 — в начале координат уровня.
            // Отсюда «со старта эффекты снизу на весь экран».
            //
            // Класть префаб в ВЫКЛЮЧЕННЫЙ узел я уже пробовал — так эффект не поднимался вовсе:
            // Awake не выполняется, а playOnAwake после снятия обратно не включишь. Поэтому гасим
            // прямо здесь, до первого шага симуляции частиц в этом кадре, а поток пускаем в конце
            // руками — уже с посаженным масштабом и позицией.
            Silence(go);
            // РАЗВОРОТ ВОКРУГ ОСИ ПОТОКА. LookRotation даёт правильное направление, но у зоны,
            // дующей ВЛЕВО, базис отличается от дующей вправо ровно на 180° вокруг оси камеры:
            // локальный +X эффекта смотрит на экране вниз вместо вверх. Направление при этом верное
            // (замер: средняя скорость −1.95 по X, то есть влево), а вот сам эффект нарисован вверх
            // ногами — и вся его несимметрия (хвосты, клубы, наклон) идёт задом наперёд. Владелец:
            // «когда вентилятор справа и воздух налево, он назад идёт».
            //
            // Докручиваем 180° вокруг САМОЙ оси потока: направление не трогается, экранный верх
            // возвращается на место. Условие — по знаку dir.x: у вертикальной зоны он ноль, её это
            // не касается вовсе.
            var flow = new Vector3(dir.x, dir.y, 0f);
            go.transform.localRotation = Quaternion.LookRotation(flow, Vector3.forward)
                                         * (dir.x < 0f ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity);
            go.transform.localScale = Vector3.one * scale;
            var origin = faceCentre + dir * (DonorWindNativeSpan * scale);
            go.transform.localPosition = new Vector3(origin.x, origin.y, ZFlow);

            // Конус на ВСЕХ зонах, боковых тоже: развилка «горизонталь донорской лентой» убрана
            // по указанию владельца. Основание конуса — по ширине вентилятора (WindConeStart = 1),
            // чтобы поток выходил ровно из лопастей и дальше раскрывался.
            // Долёт у боковых зон СВОЙ. Он считается в длинах зоны, а боковые зоны короткие вдоль
            // потока (1.1 и 2.4 против 6.4 у вертикальной) — на общем числе струя из вентилятора
            // получалась куцей. Форма при этом у всех одна, конус.
            var vertical = Mathf.Abs(dir.y) >= Mathf.Abs(dir.x);
            var reach = vertical ? tuning.WindReach : tuning.WindReachSide;

            // Скорость задаётся в МИРОВЫХ единицах, а не множителем к донорской. Донорская едет
            // на том же общем масштабе (длина/12), поэтому у самой короткой зоны от 40 оставалось
            // 3.7 ед/с — «mailbox медленный, не читается как поток», — а у длинной 21, «слишком
            // быстро». Одно абсолютное число на ось убирает эту зависимость от длины зоны совсем.
            // Долёт не страдает: время жизни считается от скорости, длина струи остаётся та же.
            var targetSpeed = vertical ? tuning.WindSpeedVertical : tuning.WindSpeedSide;
            ShapeFlow(go, scale, span, crossSpan, FanWidth(crossSpan, tuning) * 0.5f,
                reach, targetSpeed, tuning);

            // Порядок отрисовки: над заливкой зоны, но под вентилятором. Без этого заливка перекрыла
            // бы весь эффект — ровно на этом уже погорел мой процедурный поток.
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.sortingOrder = FlowSortingOrder;

            ClipToRegion(go, min, max);
            PlayFlow(go);
            return true;
        }

        /// <summary>
        /// Поток рисуется ТОЛЬКО внутри области силы (TASK-448). Ветер — видимая сила, и полосы там,
        /// где не дует, врут игроку о том, куда дует: у kangaroo_pouch они шли через всю фигуру до
        /// края экрана. Долёт (<c>WindReachSide</c> 3.2 длины зоны) и раскрытие конуса
        /// (<c>WindConeEnd</c> 1.1) остаются ползунками, но наружу из зоны больше не выносят.
        /// </summary>
        private static void ClipToRegion(GameObject root, Vector2 min, Vector2 max)
        {
            root.AddComponent<AirFlowClip>().Bind(root.GetComponentsInChildren<ParticleSystem>(true),
                min, max, ZFlow - FlowDepthHalf, ZFlow + FlowDepthHalf);
        }

        /// <summary>
        /// Зона дует ВСЕГДА и существует с нулевого кадра, поэтому игрок не должен видеть, как поток
        /// разгоняется: на старте уровня он обязан быть уже установившимся. Это и делает prewarm —
        /// система на первом кадре прокручивает себе полный цикл.
        ///
        /// Ставится только зацикленным: у одиночного залпа prewarm запрещён самим Unity.
        /// </summary>
        private static void Prewarm(ParticleSystem.MainModule main)
        {
            if (main.loop)
                main.prewarm = true;
        }

        /// <summary>
        /// Полная тишина сразу после Instantiate: снят playOnAwake и стёрты частицы у ВСЕХ систем.
        /// Иначе эффект успевает мигнуть в натуральную величину до того, как ему задали масштаб.
        /// </summary>
        private static void Silence(GameObject root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.playOnAwake = false;
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Clear(false);
            }
        }

        /// <summary>
        /// Пускает ТОЛЬКО поток. У донора шесть систем, но три из них (Rings, RisingClouds и система
        /// на самом корне) — не ветер, а разряд «удара» на разовое срабатывание, у них скорость
        /// 0..0.1. Зона у нас включена всегда и с первого кадра, поэтому этот разряд бил во весь
        /// экран на ровном месте: не запускаем его вовсе.
        ///
        /// Порог по скорости, а не по имени: имена внутри чужого префаба переживут не всякую замену
        /// ассета, а «летит или стоит» — свойство самой системы.
        /// </summary>
        private static void PlayFlow(GameObject root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.main.startSpeed.constantMax >= 1f)
                    ps.Play(false);
        }

        /// <summary>
        /// Раскрывает поток КОНУСОМ: узко у лопастей, широко на выходе.
        ///
        /// Зачем. Донорский эффект сеет частицы с ЛИНЕЙНОГО эмиттера (SingleSidedEdge длиной 5) —
        /// это ровная лента одной ширины от начала до конца, из-за неё поток и читался вертикальной
        /// плашкой, а не струёй. Заменяем форму на конус: радиус у основания — доля полуширины
        /// вентилятора, раскрытие подобрано так, чтобы на выходной грани поток накрыл зону с запасом.
        ///
        /// Угол — из геометрии, а не из головы: частица за время полёта L уходит вбок на L·tg(угол),
        /// отсюда tg = (конечный радиус − начальный) / длина зоны. Потолок нужен коротким и широким
        /// зонам: у piggy_bank (длина 2.4, поперечник 6.2) честные 50° дают не струю, а взрыв.
        ///
        /// ЕДИНИЦЫ. Всё внутри эффекта локальное, а сам он ужат в <paramref name="scale"/> раз, и
        /// scalingMode = Hierarchy домножает и форму, и скорость, и размер частицы. Поэтому мировой
        /// радиус кладём в форму ПОДЕЛЁННЫМ на scale, а скорость читаем и домножаем НА scale.
        ///
        /// ВРЕМЯ ЖИЗНИ. Оно единственное, что иерархия НЕ ужимает: у донора 1.5–2 с при скорости 40,
        /// и после сжатия под нашу зону поток улетал на 32 единицы вверх при длине зоны 6.4 — то есть
        /// далеко за край экрана. Считаем его от длины зоны, как у своего процедурного потока.
        ///
        /// Кто в потоке, а кто нет: у донора шесть систем, но три из них (Rings, RisingClouds и корень
        /// Wind_FX) — вспышка на выходе, у них скорость 0..0.1. Трогаем только те, что реально летят.
        /// </summary>
        private static void ShapeFlow(GameObject root, float scale, float span, float crossSpan,
            float fanRadius, float reachInSpans, float targetSpeed, WoodTuning tuning)
        {
            if (scale <= 1e-4f)
                return;

            var reach = Mathf.Max(reachInSpans, 0.1f);
            // Основание конуса не шире самой зоны. У узкой зоны вентилятор шире её поперечника
            // (golf_cup: высота зоны 1.05), и без зажима частицы рождались уже за границей области
            // силы — срез гасил их на первом же кадре, 103 из 145.
            var r0 = Mathf.Clamp(fanRadius * tuning.WindConeStart, 0.01f, Mathf.Max(crossSpan * 0.5f, 0.01f));
            var r1 = Mathf.Max(crossSpan * 0.5f * tuning.WindConeEnd, r0);
            var angle = Mathf.Clamp(Mathf.Atan2(r1 - r0, span) * Mathf.Rad2Deg, 0f,
                Mathf.Max(tuning.WindConeMaxAngle, 0f));

            var sizeFix = Mathf.Max(fanRadius * 2f / (DonorWindNativeCross * scale), 1f);

            // Общий множитель скорости считается ОДИН на весь эффект и по самой быстрой системе:
            // у трёх потоков донорские 40, 40 и 35, и нормируй я каждую отдельно — все сравнялись бы,
            // а разнобой скоростей и есть половина живости этого ветра.
            var donorTop = 0f;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var top = ps.main.startSpeed.constantMax;
                if (top >= 1f)
                    donorTop = Mathf.Max(donorTop, top);
            }

            var speedFix = donorTop * scale > 0.01f && targetSpeed > 0.01f
                ? targetSpeed / (donorTop * scale)
                : 1f;

            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;

                var speed = main.startSpeed.constantMax; // constantMax берёт и константу, и диапазон
                if (speed < 1f)
                    continue;

                Prewarm(main);

                // ГЛУБИНА ЭМИТТЕРА. В префабе эмиттеры стоят на локальной y = 1, а локальная Y корня после
                // LookRotation(поток, +Z) — это мировая глубина. Значит, поток висел на ZFlow ± масштаб
                // зоны: 0.53 у вертикальной зоны, и знак зависел от того, куда дует (у зоны «влево»
                // корень докручен на 180°). На прежних 0.55 это ставило ленты левых зон почти вровень
                // с деталями (0.02), а правых — за тени на стене. Сажаем каждый эмиттер ровно на ZFlow.
                var at = ps.transform.position;
                ps.transform.position = new Vector3(at.x, at.y, root.transform.position.z);

                // ЛЕНТЫ — НА КАМЕРУ. Видимый ветер это не частицы, а их ХВОСТЫ: у всех трёх систем
                // потока режим отрисовки None, рисуется только трейл. И выровнены эти ленты по осям
                // МИРА — эффект собирали под вентилятор, дующий в одну сторону. У нас зоны дуют в
                // три разные, и боковую ленту разворачивало к камере ребром: отсюда «справа налево
                // плохо себя ведёт, неправильные потоки». Поворотом корня это не лечится — рендерер
                // на трансформ не смотрит вовсе.
                //
                // View разворачивает ленту на камеру при любом направлении потока, и боковая зона
                // становится честным зеркалом вертикальной, а не её обломком.
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                    renderer.alignment = ParticleSystemRenderSpace.View;

                // Размер частицы. Эффект сжимается по ДЛИНЕ зоны, сжатие однородное — на короткой
                // зоне ветер превращался в пыль: замер дал 2 px у mailbox против 13 у air_dunk.
                // Поднимаем до масштаба вентилятора, но ТОЛЬКО вверх: длинная зона (вертикальная,
                // которая владельцу нравится) остаётся ровно такой, какая есть.
                //
                // СКОРОСТЬ этой поправкой НЕ трогается — пробовал, владелец забраковал: «слишком
                // большая, скорость должна быть как была, просто время жизни длиннее». Длину струи
                // задаёт только долёт (WindReachSide), и он же удлиняет время жизни.
                main.startSizeMultiplier *= sizeFix;

                if (!Mathf.Approximately(speedFix, 1f))
                {
                    main.startSpeedMultiplier *= speedFix;
                    speed *= speedFix;
                }

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.radius = r0 / scale;
                shape.angle = angle;
                shape.radiusThickness = 1f;      // сеять по всему кругу, а не только по ободу
                shape.randomDirectionAmount = 0f; // раскрытие даёт угол конуса, случайности не надо

                // ОСЬ КОНУСА. Форма конуса дует вдоль СВОЕГО +Z, а прежняя донорская (SingleSidedEdge)
                // дула вдоль своего +Y — ради этого у трёх эмиттеров и стоит собственный поворот
                // +90° по X. Подменив форму и не тронув ось, я развернул весь поток на 90°: частицы
                // ушли В ЭКРАН, на кадре от них остался пшик у лопастей, а замер честно показал
                // скорость (0.01, 0.24) при старте 10..40 — вся она была в невидимой Z.
                //
                // Поэтому ось не ставится числом, а ПЕРЕСЧИТЫВАЕТСЯ: направление потока (это +Z корня
                // эффекта) и глубина (мировой +Z) переводятся в локальные оси конкретного эмиттера.
                // Тогда своя ориентация эмиттера роли не играет, и правка переживёт замену префаба.
                var axis = ps.transform.InverseTransformDirection(root.transform.forward);
                var depth = ps.transform.InverseTransformDirection(Vector3.forward);
                shape.rotation = Quaternion.LookRotation(axis, depth).eulerAngles;

                // После разворота локальная Y формы смотрит в ГЛУБИНУ. Сплющиваем по ней: камера
                // ортографическая, глубину не видно, а разлёт по ней зря растаскивает частицы.
                //
                // Ползунок ограничен сверху полосой глубины потока: разлёт по глубине ≈ конечный радиус ×
                // сплющивание, и у широкой зоны (piggy_bank, r1 ≈ 3.4) родные 0.12 давали ±0.4 — поток
                // вылезал перед статикой. Срез по глубине (AirFlowClip) гасил бы такие частицы пачками,
                // а на кадре разницы между 0.12 и 0.02 нет: глубину ортокамера не показывает.
                var flat = Mathf.Min(Mathf.Clamp01(tuning.WindConeFlat), FlowDepthHalf * 0.5f / r1);
                shape.scale = new Vector3(1f, flat, 1f);
                FlattenNoise(ps, depth);

                ApplyLifeAndDensity(ps, main, span, reach, speed, scale,
                    Mathf.Cos(angle * Mathf.Deg2Rad), tuning);
            }
        }

        /// <summary>
        /// Шум донорского потока дует по всем трём осям одинаково — значит, и в глубину, где его не видно,
        /// но где он уводит ленты из-за массы вперёд, поверх рампы. Гасим шум ТОЛЬКО по оси глубины:
        /// экранный рисунок струи (две другие оси) остаётся донорским.
        ///
        /// <paramref name="depth"/> — мировой +Z в локальных осях эмиттера. Эмиттеры донора повёрнуты
        /// на прямые углы, поэтому глубина ложится на одну локальную ось; берём ту, где её больше.
        /// </summary>
        private static void FlattenNoise(ParticleSystem ps, Vector3 depth)
        {
            var noise = ps.noise;
            if (!noise.enabled)
                return;

            if (!noise.separateAxes)
            {
                var strength = noise.strength;
                noise.separateAxes = true;
                noise.strengthX = strength;
                noise.strengthY = strength;
                noise.strengthZ = strength;
            }

            var ax = Mathf.Abs(depth.x);
            var ay = Mathf.Abs(depth.y);
            var az = Mathf.Abs(depth.z);
            var none = new ParticleSystem.MinMaxCurve(0f);
            if (ax >= ay && ax >= az)
                noise.strengthX = none;
            else if (ay >= az)
                noise.strengthY = none;
            else
                noise.strengthZ = none;
        }

        /// <summary>
        /// Время жизни и густота — общие для конуса и ровной ленты.
        ///
        /// ЖИЗНЬ. Иерархия ужимает скорость, размер и форму, но НЕ время жизни: у донора оно 1.5–2 с
        /// при скорости 40, и после сжатия под нашу зону поток улетал на 32 единицы вверх при длине
        /// зоны 6.4 — то есть далеко за край экрана. Считаем от длины зоны.
        ///
        /// ГУСТОТА. Частиц на экране столько, сколько их родилось за время жизни: rate × life.
        /// Укоротив жизнь в несколько раз, я во столько же раз разредил поток. Поднимаем рождаемость
        /// обратно, чтобы густота осталась донорской, а не зависела от того, насколько коротка зона.
        /// </summary>
        private static void ApplyLifeAndDensity(ParticleSystem ps, ParticleSystem.MainModule main,
            float span, float reach, float speed, float scale, float alongFraction, WoodTuning tuning)
        {
            var worldSpeed = speed * scale * alongFraction;
            if (worldSpeed <= 0.01f)
                return;

            var wasLife = main.startLifetime.constantMax;
            var life = span * reach / worldSpeed;
            main.startLifetime = life;

            if (wasLife <= 0.01f || life <= 0.001f)
                return;

            var emission = ps.emission;
            emission.rateOverTimeMultiplier *=
                Mathf.Min(wasLife / life, MaxDensityBoost) * Mathf.Max(tuning.WindDensity, 0f);
        }

        /// <summary>
        /// Поток: частицы летят от входной грани к выходной. Скорость задана через
        /// <c>velocityOverLifetime</c> в ЛОКАЛЬНЫХ координатах, а не через startSpeed, — так поток
        /// идёт строго вдоль оси зоны при любом её повороте, и форму эмиттера не надо крутить в 3D.
        /// Текстура частицы рисуется КОДОМ (мягкая точка): готового ассета в проекте нет, а квадрат
        /// без текстуры читался бы рублеными кубиками.
        /// </summary>
        private void BuildFlow(Vector2 min, Vector2 max, Vector2 centre, Vector2 dir, string colorHex)
        {
            var tuning = WoodTuning.Current;
            if (!tuning.AirFlowParticles || dir.sqrMagnitude <= 0f)
                return;

            var half = (max - min) * 0.5f;
            var perp = new Vector2(-dir.y, dir.x);
            var extentAlong = Mathf.Abs(dir.x) * half.x + Mathf.Abs(dir.y) * half.y;
            var crossSpan = 2f * (Mathf.Abs(perp.x) * half.x + Mathf.Abs(perp.y) * half.y);
            var span = 2f * extentAlong;
            var faceCentre = centre - dir * extentAlong;

            var go = new GameObject("Flow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(faceCentre.x, faceCentre.y, ZFlow);

            // Эмиттер развёрнут так, что его СОБСТВЕННАЯ ось выброса (+Z у Box-формы) смотрит вдоль
            // потока, а локальный X ложится поперёк в плоскости экрана (up = мировой +Z даёт именно
            // такой базис). Тогда работает обычный startSpeed.
            //
            // Почему не velocityOverLifetime, как было сначала: замер живых частиц показал скорость
            // ровно (0,0,0) и разлёт вдоль потока 0.04 — то есть толщину коробки эмиттера. Модуль не
            // применялся ни в локальном пространстве, ни в мировом, и три попытки его настроить
            // ушли впустую. startSpeed отрабатывает всегда.
            go.transform.localRotation = Quaternion.LookRotation(new Vector3(dir.x, dir.y, 0f), Vector3.forward);

            Debug.Log($"[air] зона dir={dir} | эмиттер forward={go.transform.forward} " +
                      $"right={go.transform.right} up={go.transform.up} | crossSpan={crossSpan:0.00} span={span:0.00}");

            var ps = go.AddComponent<ParticleSystem>();
            var speed = Mathf.Max(tuning.AirFlowSpeed, 0.1f);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.85f, speed * 1.15f);
            main.startLifetime = span / speed;     // ровно долететь до выходной грани
            main.startSize = tuning.AirFlowSize;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.prewarm = true;                   // на первом кадре зона уже полна, а не пуста

            var color = new Color(0.81f, 0.91f, 0.96f);
            if (!string.IsNullOrEmpty(colorHex) && ColorUtility.TryParseHtmlString(colorHex, out var parsed))
                color = parsed;
            main.startColor = color;

            var emission = ps.emission;
            emission.rateOverTime = tuning.AirFlowRate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            // X — поперёк потока в плоскости экрана, Y — толщина (почти ноль), Z — вдоль потока.
            shape.scale = new Vector3(crossSpan * 0.85f, 0.01f, 0.05f);
            // Веер ОБЯЗАН быть нулевым. При 0.04 частицы расходились вбок так, что у air_dunk
            // (время жизни 2 с) поток растаскивало на 11 мировых единиц при ширине зоны 2.5.
            // Разброс по скорости даёт startSpeed, этого достаточно.
            shape.randomDirectionAmount = 0f;

            // Появляется и тает: резкий обрыв на выходной грани читался бы стеной.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(tuning.AirFlowAlpha, 0.18f),
                    new GradientAlphaKey(tuning.AirFlowAlpha, 0.75f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = FlowMaterial();
            renderer.sortingOrder = FlowSortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ClipToRegion(go, min, max);
        }

        /// <summary>Диапазон скорости по оси: разброс ±15% вдоль потока плюс поперечное дрожание.
        /// Возвращает пару строго по возрастанию, поэтому знак направления роли не играет.</summary>
        private static ParticleSystem.MinMaxCurve Band(float along, float jitter)
        {
            var a = along * 0.85f;
            var b = along * 1.15f;
            var min = Mathf.Min(a, b) - jitter;
            var max = Mathf.Max(a, b) + jitter;
            return new ParticleSystem.MinMaxCurve(min, max);
        }

        /// <summary>
        /// Материал частиц с КОДОВОЙ текстурой — мягкая точка 32×32. Готовой в проекте нет, а без
        /// текстуры билборд рисуется жёстким квадратом. Через Sprites/Default: он есть всегда и
        /// корректно смешивается в URP (тем же путём идёт заливка зоны).
        /// </summary>
        private static Material _flowMaterial;

        private static Material FlowMaterial()
        {
            if (_flowMaterial != null)
                return _flowMaterial;

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var centre = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre)) / centre;
                // Мягкий спад в квадрате: линейный давал видимый круглый кант.
                var a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
            tex.Apply();

            var shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Transparent");
            _flowMaterial = new Material(shader) { name = "AirFlowParticleMaterial", mainTexture = tex };
            _flowMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return _flowMaterial;
        }

        // (1) Translucent fill mesh == the force Region (§D5). Reuses the piece/static triangulation.
        private void BuildFill(Rect region, string colorHex)
        {
            var outline = new[]
            {
                new Vec2(region.Min.X, region.Min.Y),
                new Vec2(region.Max.X, region.Min.Y),
                new Vec2(region.Max.X, region.Max.Y),
                new Vec2(region.Min.X, region.Max.Y),
            };

            var alpha = Mathf.Clamp01(WoodTuning.Current.AirFillAlpha);

            // Ноль — меш вообще НЕ строится, а не рисуется прозрачным. Полностью прозрачный меш всё
            // равно уходил бы в очередь Transparent и сортировался с потоком; лишний рендер ради
            // ничего. Владелец: «убери этот плейн, он путает» — область силы теперь несут вентилятор
            // и сама струя.
            if (alpha <= 0.002f)
                return;

            var color = new Color(DefaultFill.r, DefaultFill.g, DefaultFill.b, alpha);
            if (!string.IsNullOrEmpty(colorHex) && ColorUtility.TryParseHtmlString(colorHex, out var parsed))
                color = new Color(parsed.r, parsed.g, parsed.b, alpha); // цвет от уровня, прозрачность общая

            var go = NewMeshChild("Fill", PieceMeshBuilder.Build(outline), ZFill, TranslucentMaterial(color));
            go.transform.localPosition = new Vector3(0f, 0f, ZFill);

            // sortingOrder ОБЯЗАТЕЛЕН. По умолчанию у меша он 0, а поток и вентилятор стоят в минусе —
            // в прозрачной очереди сортировка идёт СНАЧАЛА по нему, и заливка рисовалась поверх обоих.
            // Наружу пробивались только частицы за пределами зоны, отсюда «поток стоит на месте».
            go.GetComponent<Renderer>().sortingOrder = FillSortingOrder;
        }

        // (2) Procedural emitter bar on the up-wind edge (opposite the flow). Spans the region face perpendicular to
        // the flow; a thin slab along the flow. Support-function extents keep it flush for cardinal flows and a
        // reasonable placeholder for diagonal ones.
        private void BuildEmitter(Vector2 min, Vector2 max, Vector2 centre, Vector2 dir)
        {
            if (dir.sqrMagnitude <= 0f)
                return;
            var half = (max - min) * 0.5f;
            var perp = new Vector2(-dir.y, dir.x);
            var extentAlong = Mathf.Abs(dir.x) * half.x + Mathf.Abs(dir.y) * half.y;   // centre → up-wind face
            var extentPerp = Mathf.Abs(perp.x) * half.x + Mathf.Abs(perp.y) * half.y;  // half span across the face
            var faceCentre = centre - dir * extentAlong;

            var a = faceCentre + perp * extentPerp;
            var b = faceCentre - perp * extentPerp;
            var backA = a - dir * EmitterThickness;
            var backB = b - dir * EmitterThickness;
            var outline = new[]
            {
                new Vec2(backB.x, backB.y),
                new Vec2(b.x, b.y),
                new Vec2(a.x, a.y),
                new Vec2(backA.x, backA.y),
            };

            var mat = new Material(BlockMaterial.Shared) { name = "AirZoneEmitterMaterial" };
            mat.SetColor(BlockMaterial.ColorProperty, EmitterColor);
            mat.SetColor(BlockMaterial.LegacyColorProperty, EmitterColor);
            var go = NewMeshChild("Emitter", PieceMeshBuilder.Build(outline), ZEmitter, mat);
            go.transform.localPosition = new Vector3(0f, 0f, ZEmitter);
        }

        // (3) Tiled direction arrows. Missing sprite ⇒ graceful skip (fill + emitter still read), like TutorialView.
        private void BuildArrows(Vector2 min, Vector2 max, Vector2 centre, Vector2 dir)
        {
            if (dir.sqrMagnitude <= 0f)
                return;
            var sprite = ArrowSprite();
            if (sprite == null)
                return;

            var half = (max - min) * 0.5f;
            var extentAlong = Mathf.Abs(dir.x) * half.x + Mathf.Abs(dir.y) * half.y;
            var span = 2f * extentAlong;
            var count = Mathf.Clamp(Mathf.FloorToInt(span / ArrowSpacing), 1, ArrowMaxCount);

            var spriteSize = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            var scale = spriteSize > 0f ? ArrowWorldSize / spriteSize : 1f;
            var angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - ArrowPointsDeg;
            var start = centre - dir * (extentAlong - ArrowSpacing * 0.5f);

            for (var i = 0; i < count; i++)
            {
                var pos = start + dir * (i * ArrowSpacing);
                var go = new GameObject($"Arrow_{i}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(pos.x, pos.y, ZFlow);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                go.transform.localScale = new Vector3(scale, scale, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
            }
        }

        private GameObject NewMeshChild(string name, Mesh mesh, float z, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.mesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.transform.localPosition = new Vector3(0f, 0f, z);
            return go;
        }

        // Alpha-blended material for the fill. Sprites/Default blends by default (queue Transparent) and is always
        // present; the URP-Unlit / Unlit-Color fallbacks mirror BlockMaterial. Colour is set on the instance (few
        // zones per level) so each zone keeps its own alpha. Double-sided: the fan mesh is one-sided and the camera
        // looks along +Z, so cull-off (guarded — a no-op on shaders without _Cull) keeps the face visible.
        private static Material TranslucentMaterial(Color color)
        {
            var shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "AirZoneFillMaterial" };
            if (mat.HasProperty(BlockMaterial.ColorProperty))
                mat.SetColor(BlockMaterial.ColorProperty, color);
            if (mat.HasProperty(BlockMaterial.LegacyColorProperty))
                mat.SetColor(BlockMaterial.LegacyColorProperty, color);
            var cull = Shader.PropertyToID("_Cull");
            if (mat.HasProperty(cull))
                mat.SetFloat(cull, (float)UnityEngine.Rendering.CullMode.Off);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        private static Sprite ArrowSprite()
        {
            if (_arrowLoaded)
                return _arrowSprite;
            _arrowLoaded = true;
            _arrowSprite = Resources.Load<Sprite>(ArrowResourcePath);
            if (_arrowSprite == null)
                Debug.unityLogger.Log(LogType.Warning,
                    $"[AirZone] arrow sprite '{ArrowResourcePath}' not found — rendering zone without direction arrows");
            return _arrowSprite;
        }
    }
}
