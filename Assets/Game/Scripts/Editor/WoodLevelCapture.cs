using System.Collections.Generic;
using System.Globalization;
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
    ///
    /// <para>
    /// TASK-319 — «глаза роли из слота»: <see cref="CaptureForRole"/> снимает запрошенные уровни в ДВА PNG
    /// каждый (стартовый кадр + детали в ЦЕЛЕВЫХ позах) в <c>Artifacts/role-preview/</c>. Кодовая роль в
    /// worktree-слоте моста не имеет и авторит уровень вслепую; этот энтрипоинт (обёрнут
    /// <c>.studio/tools/render-level.sh</c>) переносит проверку сходства с карточкой референса ВНУТРЬ её цикла.
    /// </para>
    /// </summary>
    public static class WoodLevelCapture
    {
        // Не const: съёмка воздуха гоняет ОДИН и тот же уровень на двух пропорциях телефона.
        // Вентилятор стоит снаружи зоны, и срезало его только на узких (19.5:9, 20:9) — на 9:16
        // он влезал, поэтому одной пропорции для приёмки мало.
        private static int Width = 853;
        private static int Height = 1517;

        /// <summary>
        /// Путь манифеста для ИНТЕРАКТИВНЫХ энтрипоинтов (menu-item <c>Run</c>/<c>CaptureAir</c>/… — их
        /// запускает человек из редактора, аргумента у клика нет). Это проектный ЯРЛЫК, как <see cref="Wanted"/>.
        /// ПОРТИРУЕМАЯ тула <see cref="CaptureForRole"/> этой константой НЕ пользуется: путь ей приходит из env
        /// <c>WOOD_MANIFEST_PATH</c> (project data), дефолта нет — пусто ⇒ <c>Exit(2)</c> (директива портируемости
        /// CLAUDE.md; тот же контракт, что у levelId через <c>WOOD_CAPTURE_ONLY</c>).
        /// </summary>
        private const string LevelsPath = "Assets/StreamingAssets/shapefill_levels.json";

        private const int DefaultZoom = 1024;
        private static readonly Crop[] NoCrops = System.Array.Empty<Crop>();

        /// <summary>
        /// Зона-кроп в МИРОВЫХ координатах уровня (TASK-362): прямоугольник с центром (<see cref="Cx"/>,
        /// <see cref="Cy"/>) и полными сторонами <see cref="W"/>×<see cref="H"/> в мировых единицах. Автор
        /// знает, где у него стык массы с полом, и задаёт зону там же, не пересчитывая в пиксели кадра.
        /// <see cref="Name"/> уходит в имя файла — кроп нельзя перепутать.
        /// </summary>
        private struct Crop
        {
            public string Name;
            public float Cx, Cy, W, H;
        }

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
        /// ГЛАЗА РОЛИ ИЗ СЛОТА (TASK-319): на каждый запрошенный уровень пишет ДВА PNG в
        /// <c>Artifacts/role-preview/</c> — <c>&lt;id&gt;-start.png</c> (стартовый кадр, переиспользует
        /// <see cref="Capture(LevelData,string)"/>) и <c>&lt;id&gt;-target.png</c> (детали в ЦЕЛЕВЫХ позах —
        /// собранная фигура, та картинка, которую владелец сверяет с карточкой референса). Вызывается
        /// обёрткой <c>.studio/tools/render-level.sh</c> батч-Unity в дереве слота (без <c>-quit</c>: метод
        /// сам зовёт <see cref="EditorApplication.Exit"/>, как все методы этого класса).
        ///
        /// <para>
        /// Портируемость: levelId читается ТОЛЬКО из env <c>WOOD_CAPTURE_ONLY</c> (project data), проектного
        /// дефолта НЕТ — пусто ⇒ <c>Exit(2)</c>. Именно поэтому env читается напрямую, а НЕ через
        /// <see cref="CaptureIds"/>: та на пустом env подставляет <see cref="Wanted"/> (ярлык воздуха этой
        /// игры) — здесь это была бы молчаливая подстановка проектных уровней, запрещённая директивой
        /// переносимости (CLAUDE.md). Тот же контракт у ПУТИ МАНИФЕСТА: <c>WOOD_MANIFEST_PATH</c> (project
        /// data), дефолта нет — пусто ⇒ <c>Exit(2)</c> (долг TASK-320).
        /// </para>
        ///
        /// <para>
        /// TASK-362 — ЗОНЫ-КРОПЫ: env <c>WOOD_CAPTURE_CROPS</c> (<c>name=cx,cy,w,h;…</c>, мировые координаты)
        /// добавляет к каждому кадру увеличенные вырезки зон — правило приёмки «кадр принимается КРОПАМИ, а не
        /// общим планом» (CLAUDE.md). Каждый кроп — своя орто-камера, наведённая на мировой прямоугольник и
        /// снятая в <c>&lt;id&gt;-&lt;pose&gt;-&lt;name&gt;.png</c> (та же сцена, отдельный кадр — без потери
        /// пикселей на пере-масштабе). <c>WOOD_CAPTURE_ZOOM</c> — длинная сторона кропа в px (дефолт
        /// <see cref="DefaultZoom"/>). Нет env ⇒ поведение как раньше (только start/target во весь кадр).
        /// </para>
        ///
        /// <para>
        /// TASK-442 — АСПЕКТ: env <c>WOOD_CAPTURE_SIZE</c> (<c>WxH</c>, обёртка — <c>render-level.sh --size</c>)
        /// задаёт размер полного кадра, а значит и аспект, на котором судится кадрирование. Нет env ⇒ прежние
        /// 853×1517 (9:16); кривое значение ⇒ <c>Exit(2)</c> с причиной, без подстановки.
        /// </para>
        /// </summary>
        public static void CaptureForRole()
        {
            WoodArtImport.Apply();
            WindFxImport.Apply();
            WoodArt.ResetCache();

            if (Shader.Find("Game/Wood Block") == null)
            {
                Debug.LogError("[render] шейдер Game/Wood Block не найден — кадры не сняты");
                EditorApplication.Exit(2);
                return;
            }

            // levelId — ТОЛЬКО из env, без проектного дефолта (в отличие от CaptureIds, что падает на Wanted).
            var raw = System.Environment.GetEnvironmentVariable("WOOD_CAPTURE_ONLY");
            var ids = (raw ?? string.Empty).Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
            if (ids.Length == 0)
            {
                Debug.LogError("[render] WOOD_CAPTURE_ONLY не задан — нечего рендерить " +
                               "(levelId — project data, дефолта у движка рендера нет)");
                EditorApplication.Exit(2);
                return;
            }

            // Путь манифеста — ТОЛЬКО из env, без проектного дефолта (долг TASK-320, зеркалит levelId выше).
            var manifestPath = System.Environment.GetEnvironmentVariable("WOOD_MANIFEST_PATH");
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                Debug.LogError("[render] WOOD_MANIFEST_PATH не задан — путь манифеста это project data, " +
                               "дефолта у тулы нет (директива портируемости CLAUDE.md)");
                EditorApplication.Exit(2);
                return;
            }

            // Размер кадра = аспект, на котором судится кадрирование (TASK-442). Нет env ⇒ прежний 9:16.
            if (!TryParseSize(out var size))
            {
                EditorApplication.Exit(2);
                return;
            }
            if (size.HasValue)
            {
                Width = size.Value.w;
                Height = size.Value.h;
                Debug.Log($"[render] WOOD_CAPTURE_SIZE: кадр {Width}x{Height} (аспект {Width / (float)Height:0.####})");
            }

            var crops = ParseCrops();
            var zoom = ParseZoom();

            // Числа стиля — в материал и меши ДО сборки сцены (как в Run): PieceMeshBuilder читает радиус
            // скругления в момент постройки, а материал деталей кешируется статически.
            WoodTuning.Current.Apply();

            var levels = LoadLevels(manifestPath);
            if (levels == null)
            {
                // LoadLevels уже залогировала причину (нет файла / кампания пуста) — класс «конфиг отвергнут».
                EditorApplication.Exit(2);
                return;
            }

            if (crops.Count > 0)
                Debug.Log($"[render] зоны-кропы ({crops.Count}): " +
                          $"{string.Join(", ", crops.Select(c => c.Name))}; zoom={zoom}px");

            var captured = 0;
            foreach (var id in ids)
            {
                var level = levels.Find(l => l.LevelId == id);
                if (level == null)
                {
                    Debug.LogWarning($"[render] уровень '{id}' не найден в кампании — пропущен");
                    continue;
                }

                Capture(level, $"Artifacts/role-preview/{id}-start.png", targetPose: false, crops, zoom);
                Capture(level, $"Artifacts/role-preview/{id}-target.png", targetPose: true, crops, zoom);
                captured++;
            }

            Debug.Log($"[render] снято {captured} уровень(ей) в Artifacts/role-preview " +
                      $"(по 2 кадра + {crops.Count} кроп(ов) на кадр)");
            if (Application.isBatchMode)
                EditorApplication.Exit(captured == 0 ? 1 : 0);
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

            var levels = LoadLevels(LevelsPath);
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
        private static List<LevelData> LoadLevels(string levelsPath)
        {
            if (!File.Exists(levelsPath))
            {
                Debug.LogError($"[wood] нет файла уровней: {levelsPath}");
                return null;
            }

            var file = JsonConvert.DeserializeObject<CampaignFileDto>(File.ReadAllText(levelsPath));
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
            => Capture(level, path, targetPose: false, NoCrops, DefaultZoom);

        private static void Capture(LevelData level, string path, bool targetPose)
            => Capture(level, path, targetPose, NoCrops, DefaultZoom);

        /// <summary>
        /// Один кадр уровня в PNG. <paramref name="targetPose"/> — единственное отличие двух кадров роли
        /// (TASK-319): <c>false</c> ставит детали на СТАРТОВЫЕ позиции (<see cref="BuildPieces"/>, кадр «уровень
        /// до первого тапа»), <c>true</c> — в ЦЕЛЕВЫЕ позы (<see cref="BuildTargetPosePieces"/>, собранная фигура).
        /// Всё остальное — камера, бэкдроп, статика, силуэт, карточка-превью, листва, воздух, партиклы — одинаково,
        /// чтобы два PNG были прямо сопоставимы глазами.
        /// <paramref name="crops"/> (TASK-362) — после кадра во весь план снимаются увеличенные вырезки зон в
        /// мировых координатах (та же собранная сцена, отдельные орто-камеры) в <c>&lt;stem&gt;-&lt;name&gt;.png</c>.
        /// </summary>
        private static void Capture(LevelData level, string path, bool targetPose,
            IReadOnlyList<Crop> crops, int zoom)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Цветовая схема ищется по текущему уровню. Без этой строки кадр красился ПРЕЖНЕЙ общей
            // палитрой, и уровень на снимке отличался от того же уровня в каталоге — я на этом
            // чуть не сделал вывод, что схема не доехала до превью.
            // TASK-559: уровень целиком — по нему же раздаются доски волокна, как в игре.
            WoodTuning.SetCurrentLevel(level);

            var cam = BuildCamera(level, out var shortest);
            Backdrop(cam);
            BuildStatics(level);
            if (targetPose)
                BuildTargetPosePieces(level);
            else
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
            var systems = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            foreach (var ps in systems)
                ps.Simulate(3f, true, true, true);

            // Срез потока по области зоны (TASK-448). В игре его делает LateUpdate, а здесь время не
            // течёт — без явного вызова кадр приёмки показывал бы полосы за зоной, которых в игре нет.
            // Упреждения ноль: следующего кадра у съёмки не будет.
            foreach (var clip in Object.FindObjectsByType<AirFlowClip>(FindObjectsSortMode.None))
                Debug.Log($"[air] срез по зоне {clip.transform.parent.name}: погашено {clip.Apply(0f)} частиц");

            foreach (var ps in systems)
            {
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

            // Зоны-кропы (TASK-362): та же собранная сцена, отдельная орто-камера на каждый мировой
            // прямоугольник. Снимаем ПОСЛЕ кадра во весь план — сцена уже построена, партиклы прогнаны.
            if (crops != null)
                foreach (var c in crops)
                    RenderCrop(cam, c, zoom, CropPath(path, c.Name));
        }

        /// <summary>
        /// Кадр одной зоны-кропа: орто-камера, наведённая на мировой прямоугольник <paramref name="c"/>, в
        /// ту же собранную сцену. Так вырезка — НАСТОЯЩИЙ рендер зоны в высоком разрешении, а не пересжатие
        /// пикселей общего кадра: щель в 0.1 мира и ступенька тона на стыке видны, чего на общем плане нет.
        /// Настройки клира/клипа клонируются с основной камеры, чтобы кроп отличался от кадра только рамкой.
        /// Длинная сторона зоны рендерится в <paramref name="zoom"/> px, короткая — пропорционально (аспект = w/h).
        /// </summary>
        private static void RenderCrop(Camera main, Crop c, int zoom, string path)
        {
            int outW, outH;
            if (c.W >= c.H)
            {
                outW = zoom;
                outH = Mathf.Max(1, Mathf.RoundToInt(zoom * (c.H / c.W)));
            }
            else
            {
                outH = zoom;
                outW = Mathf.Max(1, Mathf.RoundToInt(zoom * (c.W / c.H)));
            }

            var go = new GameObject($"Crop_{c.Name}");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = main.clearFlags;
            cam.backgroundColor = main.backgroundColor;
            cam.nearClipPlane = main.nearClipPlane;
            cam.farClipPlane = main.farClipPlane;
            cam.orthographicSize = c.H * 0.5f;      // орто-размер = ВЕРТИКАЛЬНАЯ полу-высота зоны
            cam.aspect = c.W / c.H;
            cam.transform.position = new Vector3(c.Cx, c.Cy, main.transform.position.z);

            Debug.Log($"[render] кроп '{c.Name}': мир центр ({c.Cx:0.##},{c.Cy:0.##}) " +
                      $"{c.W:0.##}×{c.H:0.##} ед → {outW}×{outH}px");
            Render(cam, path, outW, outH);
            Object.DestroyImmediate(go);
        }

        /// <summary>Путь кропа: рядом с кадром во весь план, но с именем зоны в стеме —
        /// <c>&lt;dir&gt;/&lt;stem&gt;-&lt;name&gt;.png</c>, чтобы вырезку нельзя было спутать с кадром.</summary>
        private static string CropPath(string basePath, string cropName)
        {
            var dir = Path.GetDirectoryName(basePath);
            var stem = Path.GetFileNameWithoutExtension(basePath);
            var file = $"{stem}-{cropName}.png";
            return string.IsNullOrEmpty(dir) ? file : Path.Combine(dir, file);
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
            // TASK-539: с резервом угла кнопки подсказки, как в игре (safe-area у снимка нулевая).
            // TASK-563: и верхних угловых кнопок (рестарт, шестерёнка).
            if (CameraFramingMath.TryFrame(level, cam.aspect, config.Margin, config.PreviewBandFraction,
                    config.MinOrthoSize, HudCornerZone.HintButton, 0f, 0f, HudCornerZone.TopButtons, default,
                    out var size, out var centre))
            {
                cam.orthographicSize = size;
                cam.transform.position = new Vector3(centre.X, centre.Y, -10f);
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
                    .Initialize(poly, visuals[i].Color); // TASK-325: Polygon overload — a surface captures as a ribbon
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
            for (var index = 0; index < level.Pieces.Count; index++)
            {
                var placement = level.Pieces[index];
                var type = placement.Type;
                if (type?.Shape == null)
                    continue;

                var go = new GameObject($"PieceView_{type.Kind}");
                go.transform.SetParent(root, false);
                var view = go.AddComponent<PieceView>();
                // ВИД обязателен: цветовая схема уровня раздаёт оттенки по видам. Без него деталь на
                // поле красилась прежней палитрой, а карточка-превью — новой схемой, и кадр показывал
                // расхождение, которого в игре нет (там вид передаёт PieceViewFactory).
                // Номер — индекс размещения, как у PieceViewFactory в игре: фаза волокна та же, что на поле (TASK-559).
                view.Initialize(type.Shape, type.Color, type.Kind, index);
                view.Sync(placement.StartPosition, placement.StartRotation);
            }
        }

        /// <summary>
        /// Детали в ЦЕЛЕВЫХ позах (TASK-319): та же <see cref="PieceView"/>, что рисует поле, но поставленная не
        /// на стартовую позицию, а в позу слота целевой фигуры — так получается собранная фигура во весь кадр.
        /// Форму слота (он несёт только имя вида) восстанавливаем по деталям уровня — тот же приём, что в
        /// <see cref="BuildPreviewCard"/> и <c>TargetPreviewView</c>. Зеркалит slot-loop карточки-превью, но в
        /// МИРОВЫХ координатах (не под карточку на стене).
        /// </summary>
        private static void BuildTargetPosePieces(LevelData level)
        {
            if (level.Target?.Slots == null || level.Pieces == null)
                return;

            // Вид → тип (форма/цвет), как в BuildPreviewCard (531–537): слот несёт только имя вида.
            var kinds = new Dictionary<string, PieceType>();
            foreach (var placement in level.Pieces)
            {
                var type = placement.Type;
                if (type?.Shape != null && !kinds.ContainsKey(type.Kind))
                    kinds[type.Kind] = type;
            }

            var root = new GameObject("TargetPosePieces").transform;
            var ordinal = 0;
            foreach (var slot in level.Target.Slots)
            {
                if (!kinds.TryGetValue(slot.Kind, out var type) || type?.Shape == null)
                    continue;

                var go = new GameObject($"PieceView_{slot.Kind}");
                go.transform.SetParent(root, false);
                var view = go.AddComponent<PieceView>();
                // ВИД обязателен: палитра уровня раздаёт оттенки по видам (как в BuildPieces 494–497).
                // Номер — индекс слота (как у превью): какая деталь ляжет в какой слот, заранее неизвестно, а детали
                // одного вида в собранной фигуре обязаны различаться фазой (TASK-559).
                view.Initialize(type.Shape, type.Color, slot.Kind, ordinal++);
                // ЦЕЛЕВАЯ поза вместо старта. slot.Rotation — РАДИАНЫ (как в TargetPreviewView: slot.Rotation·Rad2Deg);
                // PieceView.Sync принимает ту же единицу, что StartRotation в BuildPieces (внутри умножает на Rad2Deg).
                view.Sync(slot.Position, slot.Rotation);
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

        private static void Render(Camera cam, string path) => Render(cam, path, Width, Height);

        private static void Render(Camera cam, string path, int width, int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Artifacts");

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            // Rect уточняем полностью: в домене есть свой Gameplay.Core.Rect, иначе CS0104.
            tex.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[wood] снято: {Path.GetFullPath(path)}");

            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        // ------------------------------------------------------------------- зоны-кропы (TASK-362)

        /// <summary>
        /// Разбор зон-кропов из env <c>WOOD_CAPTURE_CROPS</c>: <c>name=cx,cy,w,h</c> через <c>;</c>, числа
        /// в мировых координатах, инвариантная культура (десятичная точка независимо от локали хоста).
        /// Кривая зона (нет имени / не 4 числа / неположительные стороны) ПРОПУСКАЕТСЯ с варнингом, а не роняет
        /// прогон — один опечатанный кроп не должен стоить всех кадров. Нет env / пусто ⇒ пустой список (кропов нет).
        /// </summary>
        private static List<Crop> ParseCrops()
        {
            var list = new List<Crop>();
            var raw = System.Environment.GetEnvironmentVariable("WOOD_CAPTURE_CROPS");
            if (string.IsNullOrWhiteSpace(raw))
                return list;

            foreach (var part in raw.Split(';'))
            {
                var spec = part.Trim();
                if (spec.Length == 0)
                    continue;

                var eq = spec.IndexOf('=');
                if (eq <= 0)
                {
                    Debug.LogWarning($"[render] кроп без имени пропущен: '{spec}' (ждал name=cx,cy,w,h)");
                    continue;
                }

                var name = Sanitize(spec.Substring(0, eq).Trim());
                var nums = spec.Substring(eq + 1).Split(',');
                if (nums.Length != 4 ||
                    !TryFloat(nums[0], out var cx) || !TryFloat(nums[1], out var cy) ||
                    !TryFloat(nums[2], out var w) || !TryFloat(nums[3], out var h) ||
                    w <= 0f || h <= 0f)
                {
                    Debug.LogWarning($"[render] кроп '{name}' пропущен: ждал 4 числа cx,cy,w,h (w,h>0), " +
                                     $"получил '{spec.Substring(eq + 1)}'");
                    continue;
                }

                list.Add(new Crop { Name = name, Cx = cx, Cy = cy, W = w, H = h });
            }

            return list;
        }

        /// <summary>Длинная сторона кропа в px из env <c>WOOD_CAPTURE_ZOOM</c> (64..4096); иначе <see cref="DefaultZoom"/>.</summary>
        private static int ParseZoom()
        {
            var raw = System.Environment.GetEnvironmentVariable("WOOD_CAPTURE_ZOOM");
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var z) && z >= 64 && z <= 4096)
                return z;
            return DefaultZoom;
        }

        /// <summary>
        /// Размер полного кадра из env <c>WOOD_CAPTURE_SIZE</c> (<c>WxH</c> в px, 64..8192 по стороне — тот же
        /// диапазон, что валидирует <c>render-level.sh --size</c>). Нет env ⇒ <c>true</c> и <c>null</c>: кадр
        /// прежний. Кривое значение ⇒ <c>LogError</c> с причиной и <c>false</c> — вызывающий выходит с кодом 2:
        /// молча снять кадр на другом аспекте значит принять кадрирование, которое никто не заказывал.
        /// </summary>
        private static bool TryParseSize(out (int w, int h)? size)
        {
            size = null;
            var raw = System.Environment.GetEnvironmentVariable("WOOD_CAPTURE_SIZE");
            if (string.IsNullOrWhiteSpace(raw))
                return true;

            var parts = raw.Trim().Split('x');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var w) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var h) ||
                w < 64 || h < 64 || w > 8192 || h > 8192)
            {
                Debug.LogError($"[render] WOOD_CAPTURE_SIZE='{raw}' не разобран: ждал WxH в px, 64..8192 по стороне " +
                               "(например 738x1600) — кадр на непрошеном аспекте не снимаю");
                return false;
            }

            size = (w, h);
            return true;
        }

        private static bool TryFloat(string s, out float v)
            => float.TryParse(s?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        /// <summary>Имя зоны → безопасный кусок имени файла: буквы/цифры/<c>-</c>/<c>_</c>, остальное в <c>_</c>.</summary>
        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "crop";
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            return sb.Length == 0 ? "crop" : sb.ToString();
        }
    }
}
