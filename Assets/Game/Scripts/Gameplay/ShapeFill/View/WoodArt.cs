using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Деревянный арт от владельца: фон-стена, полка, рамка превью и две кнопки.
    /// Спрайты лежат в <c>Assets/Resources/Wood/</c> и грузятся тем же идиомом, что стрелки
    /// зоны воздуха и палец тутора (<c>Resources.Load</c> + предупреждение, без падения):
    /// сцены в проекте собираются кодом, префабов под UI нет, ссылку в инспекторе вешать некуда.
    ///
    /// Здесь же живут числа РАЗМЕТКИ (отступ рамки превью), потому что их читают и игра, и стенд
    /// <c>Game.Editor.WoodStyleCapture</c> — иначе стенд «проверял» бы другие значения, чем игра.
    /// Числа СТИЛЯ (волокно, свет, углы, тень) живут в <see cref="WoodTuning"/> и крутятся в Play.
    /// </summary>
    public static class WoodArt
    {
        private const string BackgroundPath = "Wood/wood_background";
        private const string ShelfPath = "Wood/wood_shelf";
        private const string FramePath = "Wood/wood_frame";
        private const string CanvasPath = "Wood/wood_canvas";
        private const string ButtonDevPath = "Wood/wood_button_dev";
        private const string ButtonRestartPath = "Wood/wood_button_restart";
        private const string LeavesLeftPath = "Wood/wood_leaves_left";
        private const string LeavesRightPath = "Wood/wood_leaves_right";

        /// <summary>Кадры вентилятора. Арт перенесён из соседнего проекта, там он уже в стиле дерева.</summary>
        private static readonly string[] FanPaths = { "Wood/wood_fan_1", "Wood/wood_fan_2", "Wood/wood_fan_3" };

        // Числа тени переехали в WoodTuning: владелец крутит их ползунком в Play, и держать вторую
        // копию здесь значило бы иметь два источника правды. Смотреть WoodTuning.ShadowOffset/ShadowScale.

        /// <summary>Насколько рамка превью шире белого поля внутри (в долях стороны).</summary>
        public const float FramePadFraction = 0.14f;

        private static Sprite _background, _shelf, _frame, _canvas, _buttonDev, _buttonRestart, _leavesLeft, _leavesRight;
        private static Sprite[] _fanFrames = System.Array.Empty<Sprite>();
        private static bool _fanLoaded;
        private static bool _backgroundLoaded, _shelfLoaded, _frameLoaded, _canvasLoaded, _buttonDevLoaded,
            _buttonRestartLoaded, _leavesLeftLoaded, _leavesRightLoaded;

        public static Sprite Background => Get(ref _background, ref _backgroundLoaded, BackgroundPath);
        public static Sprite Shelf => Get(ref _shelf, ref _shelfLoaded, ShelfPath);
        public static Sprite Frame => Get(ref _frame, ref _frameLoaded, FramePath);

        /// <summary>
        /// Фактура холста ПРЕВЬЮ (TASK-267, вердикт владельца: сплошной тон заменён деревянной текстурой,
        /// вариант C2 «limed oak darker» — светлое дерево, не сливается с ореховой стеной). Спрайт лежит ЗА
        /// прозрачным окном рамки. Пусто (спрайт не найден) — <see cref="TargetPreviewView"/> деградирует на
        /// прежний однотонный меш из <c>TargetPreviewConfig.BackdropColorHex</c> (никакой регрессии).
        /// </summary>
        public static Sprite Canvas => Get(ref _canvas, ref _canvasLoaded, CanvasPath);
        public static Sprite ButtonDev => Get(ref _buttonDev, ref _buttonDevLoaded, ButtonDevPath);
        public static Sprite ButtonRestart => Get(ref _buttonRestart, ref _buttonRestartLoaded, ButtonRestartPath);

        /// <summary>Листва в ВЕРХНИЙ ЛЕВЫЙ угол (рисунок уже развёрнут под него).</summary>
        public static Sprite LeavesLeft => Get(ref _leavesLeft, ref _leavesLeftLoaded, LeavesLeftPath);

        /// <summary>Листва в ВЕРХНИЙ ПРАВЫЙ угол.</summary>
        public static Sprite LeavesRight => Get(ref _leavesRight, ref _leavesRightLoaded, LeavesRightPath);

        /// <summary>
        /// Кадры лопастей по порядку. Пустой массив — спрайтов нет, вызывающий рисует прежним способом.
        ///
        /// ЛИЦО АРТА СМОТРИТ ВВЕРХ: картинка 456×252 — это диск, наклонённый от зрителя, а не круг.
        /// Поэтому поворот к направлению потока считается от +Y (см. <c>AirZoneView.FanPointsDeg</c>),
        /// иначе вентилятор, дующий влево, оказался бы повёрнутым на 90° и читался бы плашмя.
        /// </summary>
        public static Sprite[] FanFrames
        {
            get
            {
                if (_fanLoaded)
                    return _fanFrames;
                _fanLoaded = true;
                var list = new System.Collections.Generic.List<Sprite>(FanPaths.Length);
                foreach (var path in FanPaths)
                {
                    var sprite = Resources.Load<Sprite>(path);
                    if (sprite != null)
                        list.Add(sprite);
                }
                if (list.Count == 0)
                    Debug.unityLogger.Log(LogType.Warning,
                        "[wood] кадры вентилятора не найдены — зона рисуется прежней серой планкой");
                _fanFrames = list.ToArray();
                return _fanFrames;
            }
        }

        /// <summary>
        /// Грузит спрайт один раз и запоминает даже отрицательный результат: без флага «уже пробовали»
        /// пропавший спрайт бил бы Resources.Load каждый кадр из LateUpdate фона.
        /// </summary>
        private static Sprite Get(ref Sprite cache, ref bool loaded, string path)
        {
            if (loaded)
                return cache;
            loaded = true;
            cache = Resources.Load<Sprite>(path);
            if (cache == null)
                Debug.unityLogger.Log(LogType.Warning,
                    $"[wood] спрайт '{path}' не найден — элемент рисуется прежним способом");
            return cache;
        }

        /// <summary>
        /// Сбрасывает кеш. Нужен редакторному стенду: он переимпортирует спрайты перед съёмкой,
        /// а переимпорт делает прежние объекты Sprite мёртвыми ссылками.
        /// </summary>
        public static void ResetCache()
        {
            _background = _shelf = _frame = _canvas = _buttonDev = _buttonRestart = _leavesLeft = _leavesRight = null;
            _backgroundLoaded = _shelfLoaded = _frameLoaded = _canvasLoaded = _buttonDevLoaded =
                _buttonRestartLoaded = _leavesLeftLoaded = _leavesRightLoaded = false;
            _fanFrames = System.Array.Empty<Sprite>();
            _fanLoaded = false;
        }
    }
}
