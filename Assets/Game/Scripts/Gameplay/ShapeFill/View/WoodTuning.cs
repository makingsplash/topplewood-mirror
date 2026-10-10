using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Визуальное состояние детали. Намеренно СВОЙ тип, а не доменный <c>PieceLifecycle</c>: вью-слою
    /// важно ровно одно различие — ждёт деталь запуска или уже летит, — а домен знает про свои фазы.
    /// </summary>
    public enum PieceVisualState
    {
        /// <summary>Ждёт тапа: приглушённая.</summary>
        Idle,

        /// <summary>Полетела или уже легла: полный цвет.</summary>
        Active,

        /// <summary>Взведена в паре (balance-режим): уводится в белый поверх активного цвета.</summary>
        Armed,
    }

    /// <summary>
    /// ВСЕ числа деревянного стиля в одном месте: форма углов, волокно, свет по кромке, цвет, тень.
    /// Раньше они были разбросаны по трём файлам (дефолты шейдера, <see cref="PieceMeshBuilder"/>,
    /// константы <see cref="WoodArt"/>), и покрутить их можно было только пересборкой стенда —
    /// владелец просил крутить лайфтайм, прямо в Play.
    ///
    /// Один экземпляр на игру (<see cref="Current"/>). Любая правка обязана идти через
    /// <see cref="Apply"/>: он раскладывает числа по общему материалу, пересобирает меши (углы режутся
    /// в геометрии, не в шейдере) и подтягивает тени. Слушатели садятся на <see cref="Changed"/> —
    /// песочница так перекрашивает детали под новую палитру.
    ///
    /// Значения по умолчанию — то, что уходит в игру, если файла настроек нет. Сохранение из панели
    /// пишет их в <c>Assets/Resources/Wood/wood_tuning.json</c> (в редакторе) или в PlayerPrefs (на
    /// устройстве), и при следующем запуске они грузятся автоматически.
    /// </summary>
    /// <summary>
    /// Своя настройка дерева для ОДНОЙ формы (ключ — <see cref="WoodPieceKey"/>). Хранится разреженно:
    /// в списке лежат только те формы, которые владелец действительно тронул, остальные берут общие
    /// числа. Поэтому файл настроек не распухает до 72 блоков и правка общих чисел продолжает
    /// доезжать до всех ненастроенных деталей.
    ///
    /// Поля повторяют те общие числа, которые имеет смысл крутить поштучно. Здесь НЕТ волокна
    /// целиком (волнистость, резкость, разброс, рельеф) и нет теней: это свойства ДОСКИ, они обязаны
    /// быть общими, иначе вернётся ровно то, на что владелец жаловался — «у каждого куска своя доска».
    /// Поштучно меняется крупность рисунка, угол волокна, скругление и кромка — то есть то, что зависит
    /// от габарита конкретной формы.
    /// </summary>
    [Serializable]
    public sealed class WoodPieceOverride
    {
        public string Key;

        public float StyleScale;
        public float CornerRadius;
        public float GrainScale;
        public float GrainAngle;
        public float GrainAlignLong;
        public float BevelWidth, BevelLight, BevelDark;
        public float RimWidth, RimLight, RimDark;
        public float LineWidth, LineDark;
        public float Ao, AoWidth;
        public float Saturation, Brightness;
    }

    /// <summary>
    /// Цветовая схема ОДНОГО уровня: какой kind каким цветом красить. Два параллельных списка, а не
    /// словарь, — <c>JsonUtility</c> словари не сериализует.
    ///
    /// Схемы считает <c>Game.Editor.WoodIttenPalette</c> по кругу Иттена и по МАССЕ деталей. Живут
    /// они здесь, во вью, а не в <c>shapefill_levels.json</c>: цвет — часть рескина, и переписывать
    /// им авторские данные уровней нельзя. Выключил <see cref="WoodTuning.IttenPalettes"/> — вернулись
    /// цвета уровня.
    /// </summary>
    [Serializable]
    public sealed class WoodLevelPalette
    {
        public string LevelId;
        public List<string> Kinds = new List<string>();
        public List<string> Colors = new List<string>();

        public bool TryGet(string kind, out string hex)
        {
            hex = null;
            if (kind == null || Kinds == null || Colors == null)
                return false;
            for (var i = 0; i < Kinds.Count && i < Colors.Count; i++)
            {
                if (Kinds[i] != kind)
                    continue;
                hex = Colors[i];
                return true;
            }
            return false;
        }
    }

    [Serializable]
    public sealed class WoodTuning
    {
        // ------------------------------------------------------------------ форма

        // ЧИСЛА НИЖЕ — НЕ ПРИДУМАННЫЕ: это то, что владелец накрутил ползунками в песочнице и сохранил
        // (wood_tuning.json от 2026-08-13). Перенесены в дефолты, чтобы стиль уехал в git вместе с кодом
        // и держался даже там, где файла настроек нет (свежий клон, сборка без Resources).
        // Меняя их руками, помни: файл настроек ГЛАВНЕЕ дефолтов, и в редакторе игра возьмёт его.

        /// <summary>Радиус скругления углов в МИРОВЫХ единицах (кубик уровня ≈ 1). Режется в геометрии.</summary>
        public float CornerRadius = 0.095f;

        /// <summary>Сегментов на угол. Меньше трёх — угол становится фаской-срезом.</summary>
        public int CornerSegments = 4;

        // ------------------------------------------------ масштаб деталей стиля

        // ЗАЧЕМ ЭТО ПОЯВИЛОСЬ. Владелец: «в песочнице очень красиво, а в игре параша». Замер обоих
        // кадров показал, что цвет и шейдер там ОДИН И ТОТ ЖЕ — расходится размер детали:
        //   * песочница строит квадраты 1.0–1.7 мировых единиц;
        //   * в shapefill_levels.json 85 типов деталей, короткая сторона от 0.18 до 1.60,
        //     МЕДИАНА 0.40 — то есть игра втрое мельче стенда.
        // Все толщины ниже (скругление, фаска, кант, контур, шаг волокна) заданы в МИРОВЫХ единицах
        // и потому не ужимаются вместе с деталью. На медианной детали 0.40 это значит:
        // скругление 0.095 = 24% стороны с каждого угла, фаска 0.067 = 17% силуэта, а волокна
        // укладывается ОДИН период вместо трёх. Дерево пропадает, деталь читается пластилиновой
        // таблеткой — ровно то, что владелец и увидел.

        /// <summary>
        /// ОБЩИЙ масштаб рисунка дерева — одно число на всю сцену. Умножает все мировые толщины
        /// (скругление, фаска, кант, контур, тень) и во столько же раз сгущает волокно.
        ///
        /// 1.0 — ровно те числа, что владелец накрутил в песочнице на квадратах 1.0.
        /// 0.4 — те же пропорции, но в масштабе МЕДИАННОЙ детали кампании: на детали 0.40 стиль
        /// выглядит так, как на квадрате 1.0 в песочнице.
        ///
        /// Именно ОБЩИЙ, а не по детали: пока множитель считался индивидуально, у большого куска
        /// выходили три жирные прожилки, у соседнего мелкого — частая рябь, и владелец справедливо
        /// сказал «разные скейл паттерна дерева, у каждого куска своя доска». Настоящая доска
        /// одинакова на всей сцене, из неё просто выпилены куски разного размера.
        /// </summary>
        public float StyleScale = 0.4f;

        /// <summary>
        /// Размер детали (мировые единицы), относительно которого считается индивидуальная поправка
        /// при <see cref="DetailScale"/> &gt; 0. Единица — потому что ползунки крутились на квадратах 1.0.
        /// </summary>
        public float DetailRefSize = 1f;

        /// <summary>
        /// Насколько толщины ДОПОЛНИТЕЛЬНО следуют за размером КАЖДОЙ детали.
        /// 0 (по умолчанию) — не следуют: рисунок дерева одинаков на всей сцене, масштабом рулит
        /// один <see cref="StyleScale"/>. 1 — каждая деталь выглядит как деталь опорного размера,
        /// то есть у мелкой и крупной рисунок разной крупности.
        ///
        /// Держать в нуле, если не просили обратного: единицу владелец забраковал глазами
        /// («разные скейл паттерна дерева»), и это тот же по сути дефект, что и забракованные в
        /// августе нормализованные uv, — только там масштаб плыл по осям, а здесь по деталям.
        /// </summary>
        public float DetailScale;

        /// <summary>
        /// Множитель толщин для детали с короткой стороной <paramref name="shortSide"/>.
        /// При <see cref="DetailScale"/> = 0 он одинаков для всех деталей и равен
        /// <see cref="StyleScale"/>. Зажат: у самой мелкой детали кампании (0.18) без нижней границы
        /// контур ушёл бы в субпиксель и деталь осталась бы вовсе без силуэта.
        /// </summary>
        public float DetailFactor(float shortSide) => DetailFactor(shortSide, null);

        /// <summary>То же, но с учётом своей настройки формы, если она есть.</summary>
        public float DetailFactor(float shortSide, WoodPieceOverride o)
        {
            var styleScale = o?.StyleScale ?? StyleScale;
            if (DetailScale <= 1e-4f)
                return Mathf.Max(styleScale, 1e-3f);
            var perPiece = Mathf.Clamp(shortSide / Mathf.Max(DetailRefSize, 1e-3f), 0.12f, 3f);
            return Mathf.Max(Mathf.Lerp(styleScale, perPiece, Mathf.Clamp01(DetailScale)), 1e-3f);
        }

        // ---------------------------------------------------------------- волокно

        public float GrainScale = 2.96f;
        public float GrainStrength = 0.19f;
        public float GrainWarp = 0.275f;
        public float GrainSharp = 4.13f;
        public float GrainVary = 0.6f;
        public float GrainEmboss = 0.087f;

        /// <summary>Шероховатость МЕЖДУ прожилками. Частота у неё в девять раз выше самой прожилки,
        /// то есть период около полутора пикселей: на мелкой детали не видна, а на крупной статике
        /// (плита 2.60×3.90) читается песком. Раньше была константой 0.35 в шейдере.</summary>
        public float GrainNoise = 0.12f;

        /// <summary>Угол волокна в градусах, В СИСТЕМЕ ДЕТАЛИ: дерево крутится вместе с ней.</summary>
        public float GrainAngle;

        /// <summary>1 — разворачивать волокно вдоль длинной стороны. Владелец включил его обратно уже
        /// осознанно, ползунком: сам по себе разворот не грех, грехом было то, что его нельзя было выключить.</summary>
        public float GrainAlignLong = 1f;

        /// <summary>0 — у каждой детали своя доска, 1 — фигура выпилена из одной.</summary>
        public float GrainSpace;

        // ------------------------------------------------------------ свет и кромка

        /// <summary>Откуда светит, в градусах МИРОВЫХ (0 — справа, 90 — сверху).</summary>
        public float LightAngle = 80.5f;

        public float BevelWidth = 0.067f;
        public float BevelLight = 0.316f;
        public float BevelDark = 0.655f;
        public float RimWidth = 0.036f;
        public float RimLight = 0.20f;
        public float RimDark = 0.313f;

        /// <summary>Тёмный контур по силуэту — идёт по всей кромке, светит на неё или нет.</summary>
        public float LineWidth = 0.0115f;
        public float LineDark = 0.207f;

        /// <summary>Мягкое затемнение к краям: объём «подушкой». Ширина — в мировых единицах.
        /// Владелец увёл в ноль — объём держится на одной фаске.</summary>
        public float Ao;
        public float AoWidth = 0.193f;

        // ------------------------------------------------------------------- цвет

        public float Saturation = 1.126f;
        public float Brightness = 1.044f;

        // ------------------------------------------------------------------ стейты

        // Деталь до запуска ЖДЁТ, после — летит. Владелец: «первый как бы неактивный, а когда полетел —
        // активный». Раньше разница была одна захардкоженная цифра (base * 0.7) и настроить её было нечем.
        // Здесь она разложена на яркость и обесцвечивание — ждущая деталь приглушается, летящая берёт
        // полный цвет и может быть даже ярче базы.

        // ПОЧЕМУ ЖДУЩАЯ ДЕТАЛЬ БОЛЬШЕ НЕ ПРИГЛУШЕНА. Было 0.78/0.85, и замер кадра владельца показал:
        // относительно активной детали это ровно −27% яркости (0.78/1.06) и −21% цветности
        // (0.85/1.08) — цифры сошлись с картинкой до процента. Но на старте уровня ВСЕ детали Idle
        // (PieceView: состояние Idle, пока деталь не тапнули), поэтому приглушался не «вот эта, она
        // ждёт», а весь экран разом. Различать было нечего — сигнала ноль, потеря яркости полная.
        // Владелец: «хочу чтобы всё чётко ярко». Разницу состояний теперь несёт ПОП активной детали,
        // а не траур на ждущей.

        /// <summary>Ждущая деталь: множитель яркости. 1 — полный цвет уровня.</summary>
        public float IdleBrightness = 1f;

        /// <summary>Ждущая деталь: 1 — цвет как есть, 0 — совсем серая. Сильное обесцвечивание
        /// уводит жёлтый в оливковый, поэтому приглушать надо в основном яркостью.</summary>
        public float IdleSaturation = 1f;

        /// <summary>Летящая (активная) деталь: множитель яркости. Больше 1 — «загорается» на запуске.
        /// Держит разницу состояний в одиночку, поэтому заметно больше единицы.</summary>
        public float ActiveBrightness = 1.12f;

        /// <summary>Летящая деталь: подкрутка цветности. 1 — как в уровне.</summary>
        public float ActiveSaturation = 1.1f;

        /// <summary>Взведённая в паре деталь (balance-режим): насколько уводить в белый.</summary>
        public float ArmedWhite = 0.4f;

        // ------------------------------------------------------------------- тень

        /// <summary>
        /// Тень падает ТОЛЬКО НА СТЕНУ, а не на другое дерево. Владелец: «если фигура стоит на
        /// плашке, на неё чёрная тень, как будто прозрачность падает, некрасиво».
        ///
        /// Как это работает: тень рисуется в Transparent с выключенным ZWrite, но с обычным ZTest.
        /// Достаточно увести её ПО ГЛУБИНЕ за статику (та непрозрачная и пишет глубину) — и пол сам
        /// срежет всё, что на него легло бы. Никакой маски и второго прохода не нужно.
        ///
        /// Цена, о которой надо знать: так тень исчезает и между СЛОЖЕННЫМИ деталями — жираф теряет
        /// тёмную полоску там, где тело лежит на ногах. Взамен уходит грязь на каждом стыке.
        /// Выключишь — вернётся прежнее поведение, тень поверх всего дерева.
        /// </summary>
        public bool ShadowOnlyOnWall = true;

        public float ShadowAlpha = 0.342f;
        public float ShadowSoft = 0.055f;
        public float ShadowOffsetX = -0.002f;
        public float ShadowOffsetY = -0.131f;
        public float ShadowScale = 1.049f;

        // --------------------------------------------------------- тень под рамкой

        // Рамка превью («картина с рисунком») висит на стене плоско — владелец попросил тень и под неё.
        // Рисуется мешем с шейдером тени деталей (см. FrameShadow), а не копией спрайта.
        //
        // ЧИСЛА ЗДЕСЬ КРУПНЕЕ, ЧЕМ КАЖЕТСЯ РАЗУМНЫМ, и это проверено кадром: со сдвигом 0.05 доли
        // тень пряталась за рамкой ЦЕЛИКОМ — из-под края торчало 14 пикселей, и размытие съедало
        // почти всю их альфу. Уменьшая сдвиг, уменьшай и мягкость, иначе тень просто исчезнет.

        /// <summary>Смещения и мягкость — в ДОЛЯХ стороны рамки (см. <see cref="FrameShadow"/>),
        /// не в единицах мира: рамка вчетверо крупнее детали и ещё масштабируется под полосу экрана.</summary>
        public float FrameShadowAlpha = 0.5f;
        public float FrameShadowOffsetX = 0.045f;
        public float FrameShadowOffsetY = -0.065f;

        /// <summary>
        /// Мягкость края тени рамки, в долях её стороны. Держать заметно МЕНЬШЕ выноса: размытие
        /// съедает альфу как раз на той полоске, что торчит из-под рамки, и при мягкости больше
        /// выноса тень пропадает целиком — на этом уже наступали.
        /// </summary>
        public float FrameShadowSoft = 0.03f;

        /// <summary>Насколько тень крупнее рамки. Чуть больше единицы — тень «отходит» из-под краёв.</summary>
        public float FrameShadowScale = 1.03f;

        /// <summary>
        /// Применять палитру и к ДЕТАЛЯМ ИГРЫ, а не только к песочнице. Владелец настроил цвет в
        /// песочнице и получил в игре другой — потому что уровни несут свои цвета
        /// (<c>shapefill_levels.json</c>: 16 штук, среди них розовый #E84B8A и фиолетовый #B24BD1).
        /// С этим тумблером цвет уровня подменяется ближайшим по ТОНУ из палитры, и вся игра
        /// выглядит как песочница. Данные уровней при этом не тронуты — подмена живёт во вью,
        /// выключил тумблер и вернулись авторские цвета.
        ///
        /// ЦЕНА, о которой надо знать: 16 цветов ложатся в 6, поэтому два РАЗНЫХ типа деталей в
        /// уровне могут стать одного цвета. Где цвет различал детали, читаемость просядет —
        /// смотреть по логу <c>[wood] палитра</c>, он печатает всю раскладку.
        /// </summary>
        public bool PaletteToGame = true;

        /// <summary>Палитра. Крутится ползунками в панели, hex видно там же. Красит песочницу, а при
        /// <see cref="PaletteToGame"/> — и детали уровней.</summary>
        // Первые шесть сняты с референса владельца и им же настроены — не переписывать.
        // Остальные пять ДОБАВЛЕНЫ под тона, которых в шести не было: уровни используют бирюзу,
        // синий, электрик и фиолетовый ОДНОВРЕМЕННО, а холодный в палитре был ровно один, и при
        // включённом PaletteToGame четыре разных типа деталей сливались в один синий (замер:
        // конфликт в 15 уровнях из 17). С этими пятью — в 5 уровнях, и слипается максимум пара.
        public List<string> Palette = new List<string>
        {
            "#48699A", // синий
            "#5C8E3E", // зелёный
            "#CBA016", // жёлтый
            "#C7681D", // оранжевый
            "#BA3C32", // красный
            "#8A5A3B", // коричневый
            "#2F8C86", // бирюзовый
            "#5A55A3", // индиго
            "#8B5A9E", // слива
            "#B8577A", // розово-терракотовый
            "#DFC694", // кремовый (светлое дерево)
        };

        // ------------------------------------------------- поштучные настройки форм

        /// <summary>Разреженный список: только тронутые формы. Ключ — <see cref="WoodPieceKey"/>.</summary>
        public List<WoodPieceOverride> PieceOverrides = new List<WoodPieceOverride>();

        // ------------------------------------------------------- цвет по Иттену

        /// <summary>
        /// Красить уровни схемами с круга Иттена вместо цветов из <c>shapefill_levels.json</c>.
        /// Схемы считает <c>Tools → Wood Style → Раскрасить уровни по Иттену</c>.
        ///
        /// Чем это отличается от <see cref="PaletteToGame"/>: та подменяла цвет ближайшим по ТОНУ из
        /// одного общего списка, то есть тащила все уровни к одной и той же шестёрке и роль детали в
        /// кадре не учитывала вовсе. Здесь у КАЖДОГО уровня своя гармония, а внутри уровня цвет
        /// зависит от МАССЫ детали: крупные — приглушённые, мелкие — акценты.
        /// </summary>
        public bool IttenPalettes = true;

        /// <summary>
        /// Размер карточки-превью — той рамки сверху, где нарисовано, что надо собрать.
        /// Это ПОТОЛОК: вью всё равно ужимает карточку, если она не влезает в верхнюю полосу,
        /// зарезервированную кадрированием камеры (<c>CameraFramingConfig.PreviewBandFraction</c>).
        /// Ноль — брать значение из <c>TargetPreviewConfig</c>, как было.
        /// </summary>
        public float PreviewScale = 0.75f;

        // ------------------------------------------------------------------ листва

        /// <summary>
        /// Ширина угловой ветки в ДОЛЯХ ширины кадра. Не в мировых единицах: камера меняет масштаб
        /// на каждый уровень, и в мировых ветка то лезла бы на поле, то съёживалась.
        ///
        /// 0.405 — ЗАМЕР по кадру владельца, не на глаз. В его Game view (663×1326, соотношение
        /// ровно 0.500) нижняя кромка зелени левой ветки стоит на 0.191 высоты кадра при угле
        /// «вплотную». Отсюда доля ширины: 0.191 / 0.979 / 0.96 / 0.500 = 0.406, где 0.979 — какую
        /// часть габарита спрайта занимает непрозрачная зелень, 0.96 — пропорция картинки.
        /// Проверено обратным счётом с моего кадра при 0.57: низ вышел 0.301 вместо нужных 0.215,
        /// то есть 0.57 × 0.215/0.301 = 0.407. Две независимые оценки сошлись.
        ///
        /// Промежуточная попытка 0.57 была ошибкой вывода: я сравнил ЕГО прижатую ветку со СВОЕЙ,
        /// у которой ещё стоял вылет за угол 0.16, и всю разницу списал на размер.
        /// </summary>
        public float FoliageWidth = 0.405f;

        /// <summary>Дополнительный сдвиг листвы ВНИЗ, в долях высоты кадра. Ноль — ветка прижата к
        /// верхнему углу. Нужен, если опустить надо, не меняя размера.</summary>
        public float FoliageDropY;

        /// <summary>
        /// Насколько ветка утоплена ЗА край кадра, в долях своего размера. 0 — габарит спрайта
        /// вплотную к углу, то есть рисунок листвы достаёт до самых кромок экрана.
        ///
        /// Ноль — НЕ на глаз: владелец расставил ветки руками в сцене и прислал трансформы. Перевод
        /// его мировых координат в доли кадра дал 0.478 и 0.473 «от угла в долях своего размера»
        /// при 0.5 = вплотную, то есть вылета нет вовсе. Мои прежние 0.16 задвигали ветку за экран
        /// и съедали шестую часть рисунка.
        /// </summary>
        public float FoliageOverhang;

        /// <summary>Глубина листвы. Дальше деталей (0) и полки (0.5), но ближе стены.</summary>
        public float FoliageZ = 1.2f;

        public float FoliageShadowAlpha = 0.28f;

        /// <summary>Сдвиг тени листвы в долях РАЗМЕРА САМОГО СПРАЙТА. Снято с расстановки владельца:
        /// у левой ветки локальный сдвиг (0.7, −1.7) при размере спрайта 24.6×23.7 локальных единиц,
        /// у правой (1.2, −2.0) при 24.3×23.7 — в долях это 0.029/−0.072 и 0.049/−0.084. Беру среднее:
        /// разница между сторонами — ручная погрешность, а не замысел.</summary>
        public float FoliageShadowOffsetX = 0.039f;

        public float FoliageShadowOffsetY = -0.078f;
        public float FoliageShadowScale = 1.02f;

        /// <summary>
        /// Высота кнопок DEV и RESTART в UI-пикселях при референсе 1080×1920. ОДНА на обе: раньше
        /// число 116 было продублировано в двух презентерах и могло разъехаться.
        /// Ширина у каждой своя — она следует пропорции спрайта (DEV 282×147, RESTART 375×147),
        /// потому что текст в них ЗАПЕЧЁН и растягивать его в общую коробку нельзя.
        /// </summary>
        public float UiButtonHeight = 77f;

        // ------------------------------------------------------ вентилятор и поток

        /// <summary>Какую долю ВХОДНОЙ грани зоны закрывает вентилятор. 1 — ровно во всю грань.
        /// Арт своей шириной 4.56 мировых единиц крупнее любой зоны кампании (грани 1.1..2.5),
        /// поэтому он всегда ужимается и никогда не растягивается.</summary>
        public float FanFaceFill = 0.95f;

        /// <summary>Потолок и пол ширины вентилятора в мировых единицах. Потолок обязателен: поперечный
        /// пролёт зоны доходит до 6.2 (piggy_bank), и без него арт растягивался вдвое против своих
        /// 4.56 и уезжал за край экрана.
        ///
        /// 1.7 → 2.1 (владелец: «чуть поскейли его»). Выше 2·<c>CameraFramingMath.AirZoneFanReserve</c>
        /// поднимать нельзя — вью зажмёт по резерву, под который камера держит место в кадре.</summary>
        public float FanMaxWidth = 2.1f;

        public float FanMinWidth = 0.8f;

        /// <summary>Насколько корпус выступает ЗА входную грань, в долях своей глубины. Ноль —
        /// лопасти тонут в заливке зоны и вентилятор читается дующим сам в себя.</summary>
        public float FanOutset = 0.55f;

        /// <summary>Период полного цикла лопастей, секунды. У донора три кадра за 0.05 с — лопасти
        /// сливаются в размытие; здесь чуть медленнее, чтобы вращение читалось.</summary>
        public float FanSpinPeriod = 0.09f;

        /// <summary>Использовать перенесённый эффект ветра вместо самодельного потока.</summary>
        public bool DonorWind = true;

        /// <summary>Поправка к размеру перенесённого эффекта поверх подгонки под длину зоны.</summary>
        public float DonorWindScale = 1f;

        /// <summary>
        /// Прозрачность ЗАЛИВКИ зоны — той самой белёсой плашки поверх стены. Путь числа: константа
        /// 0.28 → 0.12 → ползунок 0.025 → НОЛЬ (владелец: «убери этот плейн, он путает»).
        ///
        /// Ноль означает, что меш не строится совсем. Формально это отступление от §D5 «видимое ==
        /// действующее»: границу области силы теперь показывают только вентилятор и струя, а точный
        /// прямоугольник не показывает ничто. Решение владельца; возврат — этим же ползунком.
        ///
        /// Число обманчиво мелкое: проект в ЛИНЕЙНОМ цветовом пространстве, и светлая заливка поверх
        /// тёмного дерева поднимает картинку сильнее, чем читается по альфе. Замер по кадру: при 0.06
        /// плашка сдвигала фон на 10/20/40 из 255 по каналам — на глаз это примерно 0.18, а не 0.06.
        /// </summary>
        public float AirFillAlpha;

        // ------------------------------------------------------------------ конус потока

        /// <summary>
        /// Поток раскрывается КОНУСОМ: узко у лопастей, широко на выходе. Донорский эффект сеет
        /// частицы с ЛИНЕЙНОГО эмиттера (SingleSidedEdge длиной 5) — это ровная плашка одной ширины
        /// от начала до конца, из-за неё поток и читался вертикальной панелью, а не струёй воздуха.
        ///
        /// Начальный радиус — доля ПОЛУШИРИНЫ вентилятора. Единица (владелец: «низ сделай шире, по
        /// ширине вентилятора») значит, что поток выходит ровно во всю грань лопастей и дальше
        /// раскрывается; меньше единицы — тонкая струйка из середины.
        /// </summary>
        public float WindConeStart = 1f;

        /// <summary>Конечный радиус — доля полуширины ЗОНЫ. Чуть больше единицы: владелец просил,
        /// чтобы наверху поток «всё захватывал», то есть перекрывал зону с запасом. Запас за границу
        /// зоны НЕ рисуется: такие частицы гасит AirFlowClip (TASK-448).</summary>
        public float WindConeEnd = 1.1f;

        /// <summary>Потолок раскрытия, градусы. Нужен коротким и широким зонам: у piggy_bank
        /// (длина 2.4 при поперечнике 6.2) честная геометрия просит 50° — это уже не струя, а взрыв.</summary>
        public float WindConeMaxAngle = 18f;

        /// <summary>Сплющивание конуса ПО ГЛУБИНЕ (локальная ось Y эффекта = мировой Z). Ноль дал бы
        /// плоскую ленту, единица — круглую трубу. Камера ортографическая, глубина не видна, но
        /// разлёт по ней зря растаскивает частицы мимо экрана.</summary>
        public float WindConeFlat = 0.12f;

        /// <summary>Густота потока поверх автоматической компенсации: 1 — как у донора.</summary>
        public float WindDensity = 1f;

        /// <summary>Куда долетает частица, в длинах зоны. Время жизни считается отсюда: у донора оно
        /// 1.5–2 с при скорости 40, и после сжатия под нашу зону поток улетал на 32 единицы вверх
        /// при длине зоны 6.4 — то есть за край экрана. 1.0 — гаснет ровно на выходной грани.</summary>
        public float WindReach = 1.3f;

        /// <summary>Долёт у БОКОВЫХ зон, тоже в длинах зоны. Отдельное число, потому что боковые
        /// зоны короткие вдоль потока (1.1 и 2.4 против 6.4 у вертикальной) и на общем множителе
        /// струя выходила куцей. С TASK-448 частица гаснет на выходной грани зоны, поэтому долёт больше
        /// единицы не удлиняет струю на экране: он задаёт время жизни, а через него длину хвоста.</summary>
        public float WindReachSide = 3.2f;

        /// <summary>
        /// Скорость потока в МИРОВЫХ единицах в секунду, отдельно для вертикальных и боковых зон.
        ///
        /// Именно мировая, а не множитель к донорской: донорская едет на общем масштабе (длина/12),
        /// и одна и та же настройка давала 3.7 ед/с на короткой зоне и 21 на длинной — «медленно,
        /// не читается как поток» и «слишком быстро» одновременно. Длину струи это число не меняет:
        /// время жизни пересчитывается от скорости, долёт задаётся отдельно.
        /// </summary>
        public float WindSpeedVertical = 12.8f;

        /// <summary>Она же для БОКОВЫХ зон. См. <see cref="WindSpeedVertical"/>.</summary>
        public float WindSpeedSide = 9f;

        public bool AirFlowParticles = true;

        /// <summary>Скорость частиц потока, мировые единицы в секунду. Время жизни считается от неё:
        /// частица обязана дожить ровно до выходной грани зоны, иначе поток обрывается в воздухе.</summary>
        public float AirFlowSpeed = 3.2f;

        public float AirFlowRate = 90f;
        public float AirFlowSize = 0.3f;
        public float AirFlowAlpha = 0.75f;

        /// <summary>Показывать ли ещё и тайловые стрелки. Выключено: направление теперь несут
        /// вентилятор и поток, а стрелки поверх них — визуальный шум. Включается, если арта нет.</summary>
        public bool AirArrows;

        /// <summary>Схемы по уровням. Пусто — красим как раньше.</summary>
        public List<WoodLevelPalette> LevelPalettes = new List<WoodLevelPalette>();

        /// <summary>
        /// Цвет ОКРУЖЕНИЯ — пол, стены, колонны, штыри. ОДИН на всю кампанию, тёмное читаемое дерево.
        ///
        /// Пробовал давать окружению тон каждого уровня (тихий член его семьи) — владелец забраковал:
        /// «пол и стены всегда должны быть одного цвета». И правильно: окружение — это сцена, а не
        /// участник композиции; когда оно перекрашивается от уровня к уровню, игрок каждый раз заново
        /// решает, что тут декорация, а что деталь. Тёмное — чтобы фигура читалась поверх него, стена
        /// фона средне-тёплая (около 126,59,21), и светлый пол с ней сливался.
        /// </summary>
        public string EnvironmentColor = "#45301F";

        /// <summary>Цвет окружения. Тумблер схем выключен — авторский цвет из данных уровня.</summary>
        public Color ResolveStaticColor(Color authored)
        {
            if (!IttenPalettes || string.IsNullOrEmpty(EnvironmentColor) ||
                !ColorUtility.TryParseHtmlString(EnvironmentColor, out var parsed))
                return authored;
            parsed.a = 1f;
            return parsed;
        }

        /// <summary>
        /// Какой уровень сейчас на экране. Ставится <see cref="WoodLevelPalettePresenter"/> по
        /// событию загрузки уровня, а в каталоге — при листании страниц. Статика, а не поле вью:
        /// цвет спрашивает <see cref="PieceView"/>, у которого никакой ссылки на уровень нет и не
        /// должно быть — вью детали не обязан знать про кампанию.
        /// </summary>
        public static string CurrentLevelId;

        private WoodLevelPalette FindLevelPalette(string levelId)
        {
            if (string.IsNullOrEmpty(levelId) || LevelPalettes == null)
                return null;
            for (var i = 0; i < LevelPalettes.Count; i++)
                if (LevelPalettes[i] != null && LevelPalettes[i].LevelId == levelId)
                    return LevelPalettes[i];
            return null;
        }

        /// <summary>TASK-332: does the Itten scheme SILENTLY replace an authored static-block colour with
        /// <see cref="EnvironmentColor"/>? Mirrors the guard in <see cref="ResolveStaticColor"/> EXACTLY (scheme on +
        /// env hex parses), so a loader-side audit can report the dead colour without duplicating the condition. When
        /// true, EVERY static block's authored colour is overridden (the override is level-independent).</summary>
        public bool OverridesStaticColor =>
            IttenPalettes && !string.IsNullOrEmpty(EnvironmentColor) &&
            ColorUtility.TryParseHtmlString(EnvironmentColor, out _);

        /// <summary>TASK-332: the palette hex that the Itten scheme substitutes for a piece <paramref name="kind"/> on
        /// <paramref name="levelId"/>, or false when the authored colour survives. Mirrors the palette branch of
        /// <see cref="ResolveLevelColor(Color,string)"/> EXACTLY (scheme on + level has a palette carrying the kind),
        /// so a loader-side audit reports exactly the colours the resolver kills — never more, never fewer.</summary>
        public bool OverridesPieceColor(string levelId, string kind, out string paletteHex)
        {
            paletteHex = null;
            if (!IttenPalettes)
                return false;
            var palette = FindLevelPalette(levelId);
            return palette != null && palette.TryGet(kind, out paletteHex);
        }

        /// <summary>
        /// Цвет детали с учётом схемы Иттена текущего уровня. Не нашлось схемы или вида — падаем
        /// на прежний путь (<see cref="ResolveLevelColor(Color)"/>), поэтому непокрашенный уровень
        /// продолжает работать как работал.
        /// </summary>
        public Color ResolveLevelColor(Color levelColor, string kind)
        {
            if (!IttenPalettes)
                return ResolveLevelColor(levelColor);

            var palette = FindLevelPalette(CurrentLevelId);
            if (palette != null && palette.TryGet(kind, out var hex) &&
                ColorUtility.TryParseHtmlString(hex, out var parsed))
            {
                parsed.a = 1f;
                return parsed;
            }

            return ResolveLevelColor(levelColor);
        }

        /// <summary>
        /// Рисовать СТАТИКУ уровня (пол, стена, колонна, штырь) деревом — тем же шейдером и теми же
        /// числами, что падающие детали. Выключено — прежнее поведение: любой прямоугольный контур
        /// подменяется 9-slice спрайтом полки.
        ///
        /// Почему включено по умолчанию: спрайт полки один на всю статику, а её в кампании 39 штук
        /// и это не только полки — стена 2.60×3.90 в high_striker, колонна 0.35×8.00 в
        /// video_launcher, четыре штырька 0.10×0.35 в locomotive, рычаг 8.00×0.40 в lever_lid.
        /// Растянутый на них 9-slice выглядел одинаково плохо, игнорировал авторский цвет уровня
        /// (#B8926B) и, главное, НЕ СЛУШАЛ НИ ОДНУ настройку дерева: владелец мог крутить только
        /// падающие детали, потому что больше ничего процедурного в кадре не было.
        /// </summary>
        public bool StaticAsWood = true;

        /// <summary>Настройка этой формы, если её крутили. Иначе null — деталь берёт общие числа.</summary>
        public WoodPieceOverride FindOverride(string key)
        {
            if (string.IsNullOrEmpty(key) || PieceOverrides == null)
                return null;
            for (var i = 0; i < PieceOverrides.Count; i++)
                if (PieceOverrides[i] != null && PieceOverrides[i].Key == key)
                    return PieceOverrides[i];
            return null;
        }

        /// <summary>
        /// Завести форме свою настройку, отпочковав её от ТЕКУЩИХ общих чисел. Именно копией, а не
        /// нулями: владелец жмёт «своя настройка» на детали, которая уже как-то выглядит, и ожидает
        /// продолжить с того же места, а не увидеть чёрный квадрат.
        /// </summary>
        public WoodPieceOverride CreateOverride(string key)
        {
            var existing = FindOverride(key);
            if (existing != null)
                return existing;

            var o = new WoodPieceOverride
            {
                Key = key,
                StyleScale = StyleScale,
                CornerRadius = CornerRadius,
                GrainScale = GrainScale,
                GrainAngle = GrainAngle,
                GrainAlignLong = GrainAlignLong,
                BevelWidth = BevelWidth, BevelLight = BevelLight, BevelDark = BevelDark,
                RimWidth = RimWidth, RimLight = RimLight, RimDark = RimDark,
                LineWidth = LineWidth, LineDark = LineDark,
                Ao = Ao, AoWidth = AoWidth,
                Saturation = Saturation, Brightness = Brightness,
            };
            PieceOverrides ??= new List<WoodPieceOverride>();
            PieceOverrides.Add(o);
            return o;
        }

        /// <summary>Вернуть форму к общим числам.</summary>
        public void RemoveOverride(string key)
        {
            if (PieceOverrides == null)
                return;
            PieceOverrides.RemoveAll(o => o != null && o.Key == key);
        }

        /// <summary>Радиус скругления для этой формы, уже с учётом масштаба. Нужен геометрии
        /// (<see cref="PieceMeshBuilder"/>), а не шейдеру: углы режутся в меше.</summary>
        public float CornerRadiusFor(string key, float shortSide)
        {
            var o = FindOverride(key);
            var radius = o?.CornerRadius ?? CornerRadius;
            return radius * DetailFactor(shortSide, o);
        }

        /// <summary>
        /// Разложить настройку ЭТОЙ формы в блок свойств рендерера. Блок предварительно чистится:
        /// иначе у детали, которой сняли свою настройку, в блоке остались бы прошлые числа и она
        /// продолжала бы жить по ним, игнорируя общие.
        /// Ничего не пишем, когда настройки нет, — деталь берёт значения из общего материала.
        /// </summary>
        public void ApplyPieceStyle(MaterialPropertyBlock mpb, string key)
        {
            var o = FindOverride(key);
            if (o == null)
                return;

            mpb.SetFloat(StyleScaleId, o.StyleScale);
            mpb.SetFloat(GrainScaleId, o.GrainScale);
            mpb.SetFloat(GrainAngleId, o.GrainAngle);
            mpb.SetFloat(GrainAlignLongId, o.GrainAlignLong);
            mpb.SetFloat(BevelWidthId, o.BevelWidth);
            mpb.SetFloat(BevelLightId, o.BevelLight);
            mpb.SetFloat(BevelDarkId, o.BevelDark);
            mpb.SetFloat(RimWidthId, o.RimWidth);
            mpb.SetFloat(RimLightId, o.RimLight);
            mpb.SetFloat(RimDarkId, o.RimDark);
            mpb.SetFloat(LineWidthId, o.LineWidth);
            mpb.SetFloat(LineDarkId, o.LineDark);
            mpb.SetFloat(AoId, o.Ao);
            mpb.SetFloat(AoWidthId, o.AoWidth);
            mpb.SetFloat(SaturationId, o.Saturation);
            mpb.SetFloat(BrightnessId, o.Brightness);
        }

        // ------------------------------------------------------------------ доступ

        private static WoodTuning _current;

        public static WoodTuning Current => _current ??= Load();

        /// <summary>Кто-то покрутил числа. Песочница пересобирает по этому событию цвета деталей.</summary>
        public static event Action Changed;

        public Vector2 ShadowOffset => new Vector2(ShadowOffsetX, ShadowOffsetY);

        public Vector2 FrameShadowOffset => new Vector2(FrameShadowOffsetX, FrameShadowOffsetY);

        /// <summary>
        /// Цвет детали уровня, приведённый к палитре — ближайший по ТОНУ (по кругу, поэтому красный
        /// у 0.99 и красный у 0.01 считаются соседями). Тон, а не расстояние в RGB: у палитры своя
        /// яркость и насыщенность, «деревянные», и тянуть их к кислотному оригиналу незачем —
        /// сохранить надо только то, ЧЕМ цвета различались, то есть тон.
        /// Тумблер выключен — цвет возвращается как есть.
        /// </summary>
        public Color ResolveLevelColor(Color levelColor)
        {
            if (!PaletteToGame || Palette == null || Palette.Count == 0)
                return levelColor;

            Color.RGBToHSV(levelColor, out var hue, out var sat, out _);

            // Серое и почти серое по тону не сравнить (тон у него случайный) — оставляем как есть.
            if (sat < 0.08f)
                return levelColor;

            var bestIndex = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < Palette.Count; i++)
            {
                if (!ColorUtility.TryParseHtmlString(Palette[i], out var candidate))
                    continue;
                Color.RGBToHSV(candidate, out var candidateHue, out _, out _);
                var d = Mathf.Abs(candidateHue - hue);
                d = Mathf.Min(d, 1f - d);   // по кругу
                if (d >= bestDistance)
                    continue;
                bestDistance = d;
                bestIndex = i;
            }

            if (bestIndex < 0 || !ColorUtility.TryParseHtmlString(Palette[bestIndex], out var result))
                return levelColor;

            LogRemap(levelColor, Palette[bestIndex]);
            result.a = 1f;
            return result;
        }

        // Раскладку печатаем ОДИН раз на цвет: без неё «почему две детали стали одного цвета»
        // пришлось бы выяснять на глаз, а схлопывание 16 цветов в 6 — заявленная цена тумблера.
        [NonSerialized] private readonly HashSet<string> _loggedRemaps = new HashSet<string>();

        private void LogRemap(Color from, string to)
        {
            var key = "#" + ColorUtility.ToHtmlStringRGB(from);
            if (!_loggedRemaps.Add(key))
                return;
            Debug.Log($"[wood] палитра: цвет уровня {key} → {to}");
        }

        /// <summary>
        /// Цвет детали в её состоянии. Считается в HSV, а не умножением на серое: умножение на 0.7
        /// не только гасит, но и грязнит цвет, и приглушённая деталь уходила в бурый. Здесь яркость и
        /// цветность крутятся раздельно, поэтому ждущая деталь читается тем же цветом, только тише.
        /// </summary>
        public Color Tint(Color baseColor, PieceVisualState state)
        {
            var idle = state == PieceVisualState.Idle;
            var brightness = idle ? IdleBrightness : ActiveBrightness;
            var saturation = idle ? IdleSaturation : ActiveSaturation;

            Color.RGBToHSV(baseColor, out var h, out var s, out var v);
            var result = Color.HSVToRGB(h, Mathf.Clamp01(s * saturation), Mathf.Clamp01(v * brightness));

            if (state == PieceVisualState.Armed)
                result = Color.Lerp(result, Color.white, Mathf.Clamp01(ArmedWhite));

            result.a = 1f;
            return result;
        }

        // ------------------------------------------------------------- применение

        private static readonly int StyleScaleId = Shader.PropertyToID("_StyleScale");
        private static readonly int DetailRefId = Shader.PropertyToID("_DetailRef");
        private static readonly int DetailScaleId = Shader.PropertyToID("_DetailScale");
        private static readonly int GrainScaleId = Shader.PropertyToID("_GrainScale");
        private static readonly int GrainStrengthId = Shader.PropertyToID("_GrainStrength");
        private static readonly int GrainWarpId = Shader.PropertyToID("_GrainWarp");
        private static readonly int GrainSharpId = Shader.PropertyToID("_GrainSharp");
        private static readonly int GrainVaryId = Shader.PropertyToID("_GrainVary");
        private static readonly int GrainEmbossId = Shader.PropertyToID("_GrainEmboss");
        private static readonly int GrainNoiseId = Shader.PropertyToID("_GrainNoise");
        private static readonly int GrainAngleId = Shader.PropertyToID("_GrainAngle");
        private static readonly int GrainAlignLongId = Shader.PropertyToID("_GrainAlignLong");
        private static readonly int GrainSpaceId = Shader.PropertyToID("_GrainSpace");
        private static readonly int LightAngleId = Shader.PropertyToID("_LightAngle");
        private static readonly int BevelWidthId = Shader.PropertyToID("_BevelWidth");
        private static readonly int BevelLightId = Shader.PropertyToID("_BevelLight");
        private static readonly int BevelDarkId = Shader.PropertyToID("_BevelDark");
        private static readonly int RimWidthId = Shader.PropertyToID("_RimWidth");
        private static readonly int RimLightId = Shader.PropertyToID("_RimLight");
        private static readonly int RimDarkId = Shader.PropertyToID("_RimDark");
        private static readonly int LineWidthId = Shader.PropertyToID("_LineWidth");
        private static readonly int LineDarkId = Shader.PropertyToID("_LineDark");
        private static readonly int AoId = Shader.PropertyToID("_Ao");
        private static readonly int AoWidthId = Shader.PropertyToID("_AoWidth");
        private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int ShadowAlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int ShadowSoftId = Shader.PropertyToID("_Soft");

        /// <summary>
        /// Разложить числа по материалам и геометрии. <paramref name="rebuildMeshes"/> выключают,
        /// когда крутят ползунок, не меняющий форму: пересборка мешей на каждый кадр ползунка —
        /// единственное здесь по-настоящему дорогое действие.
        /// </summary>
        public void Apply(bool rebuildMeshes = true)
        {
            ApplyToBlock(BlockMaterial.Shared);
            // TASK-353: у вогнутой массы свой материал (кейворд _CONCAVE_SDF на нём), но числа стиля те же —
            // живой тюнинг обязан доезжать и до неё, иначе масса разъедется с деталями при кручении ползунков.
            ApplyToBlock(BlockMaterial.ConcaveShared);
            ApplyToShadow(PieceShadow.Shared);
            PieceShadow.RefreshLiveShadows();
            FrameShadow.RefreshLive();

            if (rebuildMeshes)
                PieceMeshBuilder.RebuildAll();

            Changed?.Invoke();
        }

        /// <summary>
        /// Разложить числа по КОНКРЕТНОМУ материалу деталей. Отдельный метод, потому что
        /// <see cref="BlockMaterial"/> зовёт его сразу после создания своего материала: пойди он
        /// через <see cref="Apply"/>, получилось бы кольцо «создаю материал → применяю настройки →
        /// беру материал».
        /// </summary>
        public void ApplyToBlock(Material block)
        {
            if (block != null)
            {
                block.SetFloat(StyleScaleId, StyleScale);
                block.SetFloat(DetailRefId, DetailRefSize);
                block.SetFloat(DetailScaleId, DetailScale);
                block.SetFloat(GrainScaleId, GrainScale);
                block.SetFloat(GrainStrengthId, GrainStrength);
                block.SetFloat(GrainWarpId, GrainWarp);
                block.SetFloat(GrainSharpId, GrainSharp);
                block.SetFloat(GrainVaryId, GrainVary);
                block.SetFloat(GrainEmbossId, GrainEmboss);
                block.SetFloat(GrainNoiseId, GrainNoise);
                block.SetFloat(GrainAngleId, GrainAngle);
                block.SetFloat(GrainAlignLongId, GrainAlignLong);
                block.SetFloat(GrainSpaceId, GrainSpace);
                block.SetFloat(LightAngleId, LightAngle);
                block.SetFloat(BevelWidthId, BevelWidth);
                block.SetFloat(BevelLightId, BevelLight);
                block.SetFloat(BevelDarkId, BevelDark);
                block.SetFloat(RimWidthId, RimWidth);
                block.SetFloat(RimLightId, RimLight);
                block.SetFloat(RimDarkId, RimDark);
                block.SetFloat(LineWidthId, LineWidth);
                block.SetFloat(LineDarkId, LineDark);
                block.SetFloat(AoId, Ao);
                block.SetFloat(AoWidthId, AoWidth);
                block.SetFloat(SaturationId, Saturation);
                block.SetFloat(BrightnessId, Brightness);
            }
        }

        /// <summary>Числа тени — в её материал. Отдельно от <see cref="ApplyToBlock"/> по той же
        /// причине: <see cref="PieceShadow"/> зовёт это сразу после создания материала.</summary>
        public void ApplyToShadow(Material shadow)
        {
            if (shadow == null)
                return;
            shadow.SetFloat(ShadowAlphaId, ShadowAlpha);
            shadow.SetFloat(ShadowSoftId, ShadowSoft);
        }

        // ------------------------------------------------------------ файл настроек

        /// <summary>Путь ассета настроек. Именно Resources: сцены здесь собираются кодом, вешать
        /// ссылку в инспекторе не на что — тот же идиом, что у спрайтов в <see cref="WoodArt"/>.</summary>
        public const string ResourcePath = "Wood/wood_tuning";

        public const string AssetPath = "Assets/Resources/Wood/wood_tuning.json";

        private const string PrefsKey = "wood_tuning_json";

        private static WoodTuning Load()
        {
#if UNITY_EDITOR
            // В редакторе источник правды — ФАЙЛ, и читается он с диска напрямую, а не через
            // Resources: после сохранения из панели ассет переимпортируется не мгновенно, и
            // Resources отдал бы прошлую версию. PlayerPrefs здесь не при чём намеренно —
            // иначе «сохранил в файл, а игра всё равно берёт старое» ловилось бы полдня.
            var full = System.IO.Path.GetFullPath(AssetPath);
            if (System.IO.File.Exists(full) && TryParse(System.IO.File.ReadAllText(full), out var fromFile))
                return fromFile;
#else
            // На устройстве писать в ассеты нечем, поэтому панель сохраняет в PlayerPrefs, и эта
            // копия главнее файла: её сделали руками на этом же устройстве, значит она новее.
            var prefs = PlayerPrefs.GetString(PrefsKey, null);
            if (!string.IsNullOrEmpty(prefs) && TryParse(prefs, out var fromPrefs))
                return fromPrefs;

            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset != null && TryParse(asset.text, out var fromAsset))
                return fromAsset;
#endif
            return new WoodTuning();
        }

        private static bool TryParse(string json, out WoodTuning result)
        {
            result = null;
            try
            {
                result = JsonUtility.FromJson<WoodTuning>(json);
            }
            catch (Exception e)
            {
                Debug.unityLogger.Log(LogType.Warning, $"[wood] настройки не читаются ({e.Message}) — беру дефолтные");
                return false;
            }

            if (result == null)
                return false;

            // Палитра могла не попасть в файл (старый формат) — без неё песочница осталась бы без цветов.
            if (result.Palette == null || result.Palette.Count == 0)
                result.Palette = new WoodTuning().Palette;
            return true;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>
        /// Забрать настройки из буфера обмена и применить. Нужно для обмена текстом: владелец
        /// копирует блок кнопкой, отдаёт его мне, я возвращаю правленый — и он вставляет обратно,
        /// не выходя из Play и не трогая файлы.
        /// Ничего не сохраняет: вставил, посмотрел, не понравилось — вышел из Play, файл цел.
        /// </summary>
        public static bool TryPasteFromClipboard(out string error)
        {
            var text = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "буфер пуст";
                return false;
            }

            if (!TryParse(text, out var parsed))
            {
                error = "это не блок настроек";
                return false;
            }

            _current = parsed;
            _current.Apply();
            error = null;
            return true;
        }

        /// <summary>
        /// Сохранить. В редакторе — в ассет (уедет в git вместе с кодом), на устройстве — в PlayerPrefs.
        /// Возвращает, куда именно легло, чтобы панель написала это на экране.
        /// </summary>
        public string Save()
        {
            var json = ToJson();

#if UNITY_EDITOR
            var full = System.IO.Path.GetFullPath(AssetPath);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full) ?? ".");
            System.IO.File.WriteAllText(full, json);
            // Импорт — чтобы файл уехал в сборку как TextAsset (на устройстве он и будет дефолтом).
            UnityEditor.AssetDatabase.ImportAsset(AssetPath);
            return AssetPath;
#else
            PlayerPrefs.SetString(PrefsKey, json);
            PlayerPrefs.Save();
            return "PlayerPrefs";
#endif
        }

        /// <summary>
        /// Вернуть заводские числа. Файл при этом НЕ трогается: сброс — это «покажи, как было
        /// изначально», а не «сотри мою работу». Понравилось — жать «Сохранить», нет — выйти из Play.
        /// </summary>
        public static void ResetToDefaults()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            _current = new WoodTuning();
            _current.Apply();
        }
    }
}
