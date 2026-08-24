using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gameplay.Core;
// Именно using, а не Gameplay.Dev.WoodSandbox по месту: файл лежит в namespace Game.Editor, и
// короткое имя компилятор ищет сначала внутри Game — то есть как несуществующий Game.Gameplay.Dev
// (CS0234). Та же грабля описана в шапке WoodStyleCapture, и я на неё наступил повторно.
using Gameplay.Dev;
using Gameplay.View;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Снимает кадр НАСТОЯЩЕГО уровня кампании — тем же кодом, что рисует игру.
    ///
    /// Зачем отдельно от <see cref="WoodStyleCapture"/>: тот стенд собирает синтетическую сцену
    /// (жираф из кубиков 0.94, бруски 2.0×1.0) и потому НЕ МОГ показать главную проблему. Владелец
    /// прислал два кадра — песочница и игра, — и замер показал, что шейдер и палитра там одинаковые,
    /// а расходится размер детали: в песочнице квадраты 1.0–1.7, а в shapefill_levels.json медиана
    /// короткой стороны 0.40 (85 типов, разброс 0.18..1.60). Все толщины стиля заданы в мировых
    /// единицах, поэтому на реальной детали они вчетверо грубее — отсюда «пластилин» вместо дерева.
    /// Стенд, который этого не показывает, проверяет сам себя.
    ///
    /// Кадр строится из настоящих данных уровня, настоящими <see cref="PieceView"/> /
    /// <see cref="StaticGeometryView"/> и кадрируется настоящим <see cref="CameraFramingMath"/> —
    /// то есть отличаться от Play он может только отсутствием физики и UI.
    ///
    /// <para>
    /// Батчем (только при закрытом Unity, без -nographics — иначе рендерить нечем):
    /// Unity.exe -batchmode -projectPath . -executeMethod Game.Editor.WoodLevelCapture.CaptureBeforeAfter -quit
    /// </para>
    /// </summary>
    public static class WoodLevelCapture
    {
        // Не const: съёмка воздуха гоняет ОДИН и тот же уровень на двух пропорциях телефона.
        // Вентилятор стоит снаружи зоны, и срезало его только на узких (19.5:9, 20:9) — на 9:16
        // он влезал, поэтому одной пропорции для приёмки мало.
        private static int Width = 853;
        private static int Height = 1517;
        private const string LevelsPath = "Assets/StreamingAssets/shapefill_levels.json";

        /// <summary>
        /// Дефолтные уровни съёмки, когда <c>WOOD_CAPTURE_ONLY</c> не задан. Сейчас это ВСЕ три уровня с
        /// зонами воздуха — их и проверяем. Комментарий про grand_seesaw / giraffe / well остался от съёмки
        /// масштаба деталей; вернуть эти имена сюда, когда снова понадобится вилка размеров.
        /// </summary>
        private static readonly string[] Wanted = { "mailbox", "air_dunk", "piggy_bank" };

        /// <summary>
        /// Список levelId для съёмки. По умолчанию — <see cref="Wanted"/>; env <c>WOOD_CAPTURE_ONLY</c>
        /// (csv levelId) его ПЕРЕОПРЕДЕЛЯЕТ — оркестратору нужно снимать произвольную волну кампании
        /// (§D15-процесс: скрины после каждой волны на вердикт владельца), не трогая этот файл. Пустое/нет →
        /// дефолт. Паттерн зеркалит <c>SIM_ONLY</c>/<c>SIM_STEPLOG</c> в LevelSimRunner: env выставляет
        /// обёртка/вызов executeMethod, batch-Unity читает. Портируемость: имена уровней НЕ зашиты — они
        /// приходят аргументом из env (дефолт остаётся проектным ярлыком воздуха, а не единственным путём).
        /// </summary>
        private static IReadOnlyList<string> CaptureIds()
        {
            var raw = System.Environment.GetEnvironmentVariable("WOOD_CAPTURE_ONLY");
            if (string.IsNullOrWhiteSpace(raw))
                return Wanted;

            var ids = raw.Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
            if (ids.Length == 0)
                return Wanted;

            Debug.Log($"[wood] WOOD_CAPTURE_ONLY: снимаю {ids.Length} уровень(ей): {string.Join(", ", ids)}");
            return ids;
        }

        [MenuItem("Tools/Wood Style/Снять настоящие уровни")]
        public static void CaptureLevels()
        {
            Run("now");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        /// <summary>
        /// ВОЗДУХ: три зонных уровня на двух пропорциях телефона — 9:16 и 20:9. Смысл в узкой:
        /// именно на ней вентилятор уезжал за край кадра (piggy_bank, 0.45 единицы), а на 9:16
        /// проверка этого не показывала.
        /// </summary>
        public static void CaptureAir()
        {
            foreach (var (w, h, tag) in new[] { (853, 1517, "9x16"), (720, 1600, "20x9") })
            {
                Width = w;
                Height = h;
                Run(tag);
            }

            Width = 853;
            Height = 1517;
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        /// <summary>
        /// Уровни И песочница в одном прогоне — чтобы сравнивать их между собой на ОДНИХ числах.
        /// Пока это делалось двумя запусками, между кадрами успевали разъехаться настройки, и
        /// сравнение «в песочнице красиво, в игре нет» ничего не доказывало.
        /// </summary>
        public static void CaptureAll()
        {
            Run("now");
            WoodStyleCapture.CaptureSandbox();
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        /// <summary>
        /// Те же уровни ДВАЖДЫ: с выключенным масштабированием толщин (как выглядит игра сегодня)
        /// и с включённым. Один прогон вместо двух — редактор в батче поднимается с нуля, и это
        /// самая дорогая часть проверки.
        /// </summary>
        public static void CaptureBeforeAfter()
        {
            var tuning = WoodTuning.Current;
            var saved = tuning.DetailScale;

            tuning.DetailScale = 0f;
            Run("before");

            tuning.DetailScale = 1f;
            Run("after");

            tuning.DetailScale = saved;
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        /// <summary>
        /// Развёртка по ОБЩЕМУ масштабу рисунка: уровни и песочница на одних и тех же числах,
        /// чтобы владелец выбрал крупность глазами, а не по описанию. Поштучная поправка при этом
        /// в нуле — она и была причиной «у каждого куска своя доска».
        /// </summary>
        public static void CaptureScaleSweep()
        {
            var tuning = WoodTuning.Current;
            var savedStyle = tuning.StyleScale;
            var savedDetail = tuning.DetailScale;
            tuning.DetailScale = 0f;

            foreach (var scale in new[] { 1.0f, 0.6f, 0.4f })
            {
                tuning.StyleScale = scale;
                // Имя без точки: «0.4» в имени файла читается как расширение частью инструментов.
                var tag = "s" + Mathf.RoundToInt(scale * 100f);
                Run(tag);
                CaptureSandbox($"Artifacts/wood-sandbox-{tag}.png");
            }

            tuning.StyleScale = savedStyle;
            tuning.DetailScale = savedDetail;
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        /// <summary>
        /// Снять КАЖДУЮ страницу каталога (страница = уровень) в свой PNG. Нужно, чтобы смотреть на
        /// все 85 деталей мог не только владелец в Play, но и я — «кривоту» на мелких формах иначе
        /// пришлось бы искать с его слов.
        /// </summary>
        public static void CaptureCatalog()
        {
            WoodArtImport.Apply();
            WoodArt.ResetCache();

            if (Shader.Find("Game/Wood Block") == null)
            {
                Debug.LogError("[wood] шейдер Game/Wood Block не найден — каталог не снят");
                EditorApplication.Exit(2);
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = new GameObject("WoodCatalog").AddComponent<WoodCatalogSandbox>();
            var cam = catalog.Build();
            cam.aspect = Width / (float)Height;

            var pages = catalog.PageCount;
            if (pages == 0)
            {
                Debug.LogError("[wood] каталог пуст — кампания не прочиталась");
                EditorApplication.Exit(2);
                return;
            }

            for (var i = 0; i < pages; i++)
            {
                catalog.ShowPage(i);
                cam.aspect = Width / (float)Height;
                catalog.FitCamera();
                Render(cam, $"Artifacts/catalog/{i + 1:00}-{catalog.PageName}.png");
            }

            Debug.Log($"[wood] каталог снят: {pages} страниц в Artifacts/catalog");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        /// <summary>
        /// Кадр песочницы в СВОЙ файл. <c>WoodStyleCapture.CaptureSandbox</c> всегда пишет в один и
        /// тот же путь, а развёртке нужен кадр на каждый масштаб.
        /// </summary>
        private static void CaptureSandbox(string path)
        {
            WoodArt.ResetCache();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sandbox = new GameObject("WoodSandbox").AddComponent<WoodSandbox>();
            var cam = sandbox.Build();
            cam.aspect = Width / (float)Height;
            sandbox.FitCamera();

            Render(cam, path);
        }

        // ------------------------------------------------------------------ прогон

        private static void Run(string suffix)
        {
            WoodArtImport.Apply();
            WindFxImport.Apply();
            WoodArt.ResetCache();

            if (Shader.Find("Game/Wood Block") == null)
            {
                Debug.LogError("[wood] шейдер Game/Wood Block не найден — уровни не сняты");
                EditorApplication.Exit(2);
                return;
            }

            var levels = LoadLevels();
            if (levels == null)
                return;

            // Числа стиля — в материал и в меши ДО сборки сцены: PieceMeshBuilder читает радиус
            // скругления в момент постройки, а материал деталей кешируется статически и с прошлым
            // значением _DetailScale уехал бы в оба кадра одинаковым.
            WoodTuning.Current.Apply();

            var tuning = WoodTuning.Current;
            Debug.Log($"[wood] прогон '{suffix}': опорный размер {tuning.DetailRefSize:0.##}, " +
                      $"толщины следуют размеру {tuning.DetailScale:0.##}");

            foreach (var id in CaptureIds())
            {
                var level = levels.Find(l => l.LevelId == id);
                if (level == null)
                {
                    Debug.LogWarning($"[wood] уровень '{id}' не найден в кампании");
                    continue;
                }
                Capture(level, $"Artifacts/wood-level-{id}-{suffix}.png");
            }
        }

        /// <summary>
        /// Уровни читаются DTO напрямую, без <c>JsonLevelSource</c>: тот тянет за собой валидатор и
        /// падает на всей кампании, если ОДИН уровень не прошёл проверку, — а для кадра этого не надо.
        /// </summary>
        private static List<LevelData> LoadLevels()
        {
            if (!File.Exists(LevelsPath))
            {
                Debug.LogError($"[wood] нет файла уровней: {LevelsPath}");
                return null;
            }

            var file = JsonConvert.DeserializeObject<CampaignFileDto>(File.ReadAllText(LevelsPath));
            if (file?.Levels == null || file.Levels.Count == 0)
            {
                Debug.LogError("[wood] кампания не разобралась или пуста");
                return null;
            }

            var result = new List<LevelData>(file.Levels.Count);
            foreach (var dto in file.Levels)
                result.Add(dto.ToDomain());
            return result;
        }

        // ------------------------------------------------------------------- сцена

        private static void Capture(LevelData level, string path)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Цветовая схема ищется по текущему уровню. Без этой строки кадр красился ПРЕЖНЕЙ общей
            // палитрой, и уровень на снимке отличался от того же уровня в каталоге — я на этом
            // чуть не сделал вывод, что схема не доехала до превью.
            WoodTuning.CurrentLevelId = level.LevelId;

            var cam = BuildCamera(level, out var shortest);
            Backdrop(cam);
            BuildStatics(level);
            BuildPieces(level);
            BuildSilhouette(level);
            BuildPreviewCard(level, cam);

            // Листва — тем же презентером, что в игре. Зависимостей у него нет, поэтому в съёмке
            // поднимается напрямую; Start сам вписывает ветки в углы текущей камеры.
            new WoodFoliagePresenter().Start();

            BuildAirZones(level);

            // Партиклы в редакторе сами не идут: без Play время не течёт, и поток вышел бы пустым.
            // Прогоняем симуляцию руками до установившегося состояния — тогда зона на кадре выглядит
            // так же, как в игре через пару секунд после старта.
            // fixedTimeStep: TRUE обязателен. С false Unity прогоняет весь интервал ОДНИМ шагом, и
            // частицы либо улетают за кадр, либо сразу переживают своё время жизни — на кадре поток
            // выглядел неподвижной полоской у входной грани.
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                ps.Simulate(3f, true, true, true);

                // Диагностика: сколько частиц живо и в каком объёме они разлетелись. Без неё
                // «поток стоит» приходилось лечить наугад — три попытки подряд мимо.
                var buf = new ParticleSystem.Particle[ps.main.maxParticles];
                var n = ps.GetParticles(buf);
                if (n == 0)
                {
                    Debug.Log($"[air] {ps.name}: частиц НЕТ");
                    continue;
                }
                // В МИРОВЫЕ координаты. У донорских систем simulationSpace = Local, и GetParticles
                // отдаёт локальные позиции — а локальная Y эффекта это мировой Z (глубина), локальная
                // Z — направление потока. Печатая их как есть, я читал «поток стоит на месте» там, где
                // на деле смотрел не на те оси.
                var toWorld = ps.main.simulationSpace == ParticleSystemSimulationSpace.World
                    ? Matrix4x4.identity
                    : ps.transform.localToWorldMatrix;
                var lo = toWorld.MultiplyPoint3x4(buf[0].position);
                var hi = lo;
                var mean = Vector3.zero;
                for (var i = 0; i < n; i++)
                {
                    var world = toWorld.MultiplyPoint3x4(buf[i].position);
                    lo = Vector3.Min(lo, world);
                    hi = Vector3.Max(hi, world);
                    mean += toWorld.MultiplyVector(buf[i].velocity);
                }
                mean /= n;
                Debug.Log($"[air] {ps.name}: {n} частиц, МИР X {lo.x:0.00}..{hi.x:0.00} " +
                          $"Y {lo.y:0.00}..{hi.y:0.00}, скорость ({mean.x:0.00}, {mean.y:0.00}), " +
                          $"жизнь {ps.main.startLifetime.constantMax:0.00} с, " +
                          $"скорость старта {ps.main.startSpeed.constantMin:0.0}..{ps.main.startSpeed.constantMax:0.0}, " +
                          $"форма {ps.shape.shapeType} r={ps.shape.radius:0.00} угол={ps.shape.angle:0.0}, " +
                          $"габарит {ps.transform.lossyScale.x:0.000}");
            }

            // Печатаем масштаб кадра: по нему сразу видно, сколько ПИКСЕЛЕЙ достаётся самой мелкой
            // детали. Ниже ~30 px дерево не спасёт никакая настройка — там нужен размер в данных.
            var pixelsPerUnit = Height / (2f * cam.orthographicSize);
            Debug.Log($"[wood] {level.LevelId}: ортокамера {cam.orthographicSize:0.00}, " +
                      $"{pixelsPerUnit:0} px на единицу; самая мелкая деталь {shortest:0.00} ед. = " +
                      $"{shortest * pixelsPerUnit:0} px");

            Render(cam, path);
        }

        /// <summary>Камера — тем же расчётом, что <c>CameraFramingPresenter</c>, иначе кадр
        /// отличался бы от игры ровно на масштаб, то есть на то, что мы и проверяем.</summary>
        private static Camera BuildCamera(LevelData level, out float shortestSide)
        {
            shortestSide = ShortestPieceSide(level);

            var cam = new GameObject("LevelCaptureCamera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.35f, 0.16f, 0.06f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            // Батч берёт aspect экрана, а кадр у нас фиксированный — без подстановки уровень
            // вписался бы не в то поле зрения, что снимается.
            cam.aspect = Width / (float)Height;

            var config = new CameraFramingConfig();
            if (CameraFramingMath.TryContentBounds(level, out var min, out var max))
            {
                var size = CameraFramingMath.ComputeOrthoSize(min, max, cam.aspect,
                    config.Margin, config.PreviewBandFraction, config.MinOrthoSize);
                cam.orthographicSize = size;
                cam.transform.position = new Vector3(
                    (min.X + max.X) * 0.5f,
                    (min.Y + max.Y) * 0.5f + size * config.PreviewBandFraction,
                    -10f);
            }
            else
            {
                cam.orthographicSize = 6f;
                cam.transform.position = new Vector3(0f, 0f, -10f);
            }

            return cam;
        }

        private static float ShortestPieceSide(LevelData level)
        {
            var shortest = float.MaxValue;
            if (level.Pieces == null)
                return 0f;

            foreach (var placement in level.Pieces)
            {
                var verts = placement.Type?.Shape?.Vertices;
                if (verts == null || verts.Length == 0)
                    continue;

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                foreach (var v in verts)
                {
                    if (v.X < minX) minX = v.X;
                    if (v.X > maxX) maxX = v.X;
                    if (v.Y < minY) minY = v.Y;
                    if (v.Y > maxY) maxY = v.Y;
                }

                var side = Mathf.Min(maxX - minX, maxY - minY);
                if (side < shortest)
                    shortest = side;
            }

            return shortest == float.MaxValue ? 0f : shortest;
        }

        private static void Backdrop(Camera cam)
        {
            var sprite = WoodArt.Background;
            if (sprite == null)
                return;

            var go = new GameObject("[Wood] Backdrop");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -10000;
            // Bind подгоняет сразу: в редакторе LateUpdate не идёт, а рендер случится через миг.
            go.AddComponent<WoodBackdropFit>().Bind(cam, sr);
        }

        /// <summary>Видимая статика (полка, ложе) — тем же вью, что в игре. StaticVisual — СТРУКТУРА,
        /// поэтому наличие проверяем длиной списка, а не сравнением с null.</summary>
        private static void BuildStatics(LevelData level)
        {
            if (level.StaticGeometry == null)
                return;

            var visuals = level.StaticVisuals;
            for (var i = 0; i < level.StaticGeometry.Length; i++)
            {
                var poly = level.StaticGeometry[i];
                if (poly?.Outline == null)
                    continue;
                if (visuals == null || i >= visuals.Count || !visuals[i].Visible)
                    continue;

                new GameObject($"Static_{i}")
                    .AddComponent<StaticGeometryView>()
                    .Initialize(poly.Outline, visuals[i].Color);
            }
        }

        /// <summary>
        /// Детали — на СТАРТОВЫХ позициях и в состоянии Idle: именно так игрок видит уровень до
        /// первого тапа, и именно этот кадр прислал владелец. Состояние не выставляем явно —
        /// <see cref="PieceView"/> сам стартует с <c>PieceLifecycle.Idle</c>.
        /// </summary>
        private static void BuildPieces(LevelData level)
        {
            if (level.Pieces == null)
                return;

            var root = new GameObject("ShapeFillPieces").transform;
            foreach (var placement in level.Pieces)
            {
                var type = placement.Type;
                if (type?.Shape == null)
                    continue;

                var go = new GameObject($"PieceView_{type.Kind}");
                go.transform.SetParent(root, false);
                var view = go.AddComponent<PieceView>();
                // ВИД обязателен: цветовая схема уровня раздаёт оттенки по видам. Без него деталь на
                // поле красилась прежней палитрой, а карточка-превью — новой схемой, и кадр показывал
                // расхождение, которого в игре нет (там вид передаёт PieceViewFactory).
                view.Initialize(type.Shape, type.Color, type.Kind);
                view.Sync(placement.StartPosition, placement.StartRotation);
            }
        }

        /// <summary>
        /// Контур целевой фигуры. Без него кадр показывал детали, висящие в пустоте, — то есть
        /// уровень без того, что в нём надо собрать. Владелец назвал это «на левел дизайне
        /// накосячил», и был прав: я снимал не уровень, а его половину.
        /// </summary>
        private static void BuildSilhouette(LevelData level)
        {
            // Target.Outline — уже Polygon, а не массив вершин.
            var polygon = level.Target?.Outline;
            if (polygon?.Outline == null || polygon.Outline.Length < 3)
                return;

            new GameObject("Silhouette")
                .AddComponent<SilhouetteView>()
                .Initialize(polygon);
        }

        /// <summary>
        /// Карточка-превью («картина на стене») — тем же вью, что в игре, с тем же конфигом и той же
        /// долей верхней полосы, что заложена в кадрирование камеры. Иначе карточка встала бы не
        /// туда, и кадр опять врал бы про композицию.
        /// </summary>
        private static void BuildPreviewCard(LevelData level, Camera cam)
        {
            if (level.Target?.Slots == null || level.Pieces == null)
                return;

            // Слот несёт только имя вида — форму восстанавливаем по деталям уровня
            // (тот же приём, что в CameraFramingMath.BuildKindMap и TargetPreviewPresenter).
            var kinds = new Dictionary<string, PieceType>();
            foreach (var placement in level.Pieces)
            {
                var type = placement.Type;
                if (type?.Shape != null && !kinds.ContainsKey(type.Kind))
                    kinds[type.Kind] = type;
            }

            new GameObject("TargetPreview")
                .AddComponent<TargetPreviewView>()
                .Initialize(level.Target, kinds, new TargetPreviewConfig(), cam,
                    new CameraFramingConfig().PreviewBandFraction);
        }

        /// <summary>
        /// Зоны воздуха — тем же вью, что в игре: заливка = область силы, вентилятор на входной
        /// грани, поток частиц вдоль оси. В кампании такие зоны ровно у трёх уровней
        /// (<c>mailbox</c> дует влево, <c>air_dunk</c> вверх, <c>piggy_bank</c> вправо), поэтому
        /// съёмка нацелена именно на них.
        /// </summary>
        private static void BuildAirZones(LevelData level)
        {
            if (level.AirZones == null)
                return;

            for (var i = 0; i < level.AirZones.Length; i++)
            {
                var go = new GameObject($"AirZone_{i}");
                go.AddComponent<AirZoneView>().Initialize(level.AirZones[i]);
            }
        }

        private static void Render(Camera cam, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Artifacts");

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            // Rect уточняем полностью: в домене есть свой Gameplay.Core.Rect, иначе CS0104.
            tex.ReadPixels(new UnityEngine.Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[wood] снято: {Path.GetFullPath(path)}");

            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
