#if UNITY_EDITOR || GPIE_DEVELOPMENT
using System.Collections.Generic;
using System.IO;
using Gameplay.Core;
using Gameplay.View;
using Newtonsoft.Json;
using UnityEngine;

namespace Gameplay.Dev
{
    /// <summary>
    /// Каталог ВСЕХ деталей кампании: 17 уровней, 85 типов, 72 разные формы — каждая в своём
    /// настоящем размере, подписанная, кликабельная.
    ///
    /// Зачем отдельно от <see cref="WoodSandbox"/>: тот показывает выдуманный набор форм и размеров
    /// (квадраты 1.0–1.7), которых в игре нет ни на одном уровне, — на нём стиль настраивался вслепую.
    /// Владелец: «посмотри все все все уровни и все детали и создай песочницу с ними, чтобы я мог
    /// каждую настроить». Здесь ровно они, прочитанные из <c>shapefill_levels.json</c>.
    ///
    /// СТРАНИЦА = УРОВЕНЬ. Все 85 деталей разом дали бы кашу из деталей 0.18 и 4.0 в одном кадре;
    /// постранично каждая занимает достаточно экрана, чтобы судить о материале.
    ///
    /// Настройка вешается на ФОРМУ (<see cref="WoodPieceKey"/>), а не на пару «уровень/kind»:
    /// квадрат 0.40×0.40 живёт в четырёх уровнях, и настраивать его четырежды — прямой путь к тому,
    /// чтобы они разъехались. Панель показывает, где ещё встречается выбранная форма.
    /// </summary>
    public sealed class WoodCatalogSandbox : MonoBehaviour
    {
        private const string LevelsFile = "shapefill_levels.json";

        /// <summary>Ширина, в которую укладывается ряд деталей, прежде чем перенестись. Мировые единицы.</summary>
        private const float RowWidth = 9f;

        private const float GapX = 0.55f;
        private const float GapY = 1.25f;

        private sealed class Entry
        {
            public string Kind;
            public string Key;
            public string ColorHex;
            public Renderer Renderer;
            public Vector2 Center;
            public Vector2 Size;

            /// <summary>Статика уровня (пол/стена/колонна). Красится не так, как деталь: без стейта
            /// и своим цветом уровня, поэтому перекраска у неё живёт в её собственном вью.</summary>
            public bool IsStatic;
        }

        /// <summary>
        /// Человеческое имя статики по её габариту — в данных у неё нет ни kind, ни имени, только
        /// контур. Без этого в панели стояло бы «Static_3» и было бы непонятно, что выбрано.
        /// </summary>
        private static string StaticName(Vector2 size)
        {
            var w = size.x;
            var h = size.y;
            if (h >= w * 2.5f)
                return w <= 0.35f ? "штырь" : "колонна";
            if (w >= h * 2.5f)
                return "пол";
            return "стена";
        }

        private sealed class Page
        {
            public string LevelId;
            public LevelData Level;
            public List<(string kind, Shape shape, string color)> Pieces = new List<(string, Shape, string)>();
        }

        /// <summary>
        /// Что показывать на странице. Владелец открыл каталог и сказал «а левел дизайн где, я вижу
        /// только детали» — и был прав: россыпь деталей не даёт судить о композиции, а материал в
        /// собранной фигуре читается иначе, потому что детали стоят вплотную и контур работает.
        /// </summary>
        public bool Assembled = true;

        private readonly List<Page> _pages = new List<Page>();
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Ключ формы → где она встречается («уровень/kind»). Строится один раз по всей кампании.</summary>
        private readonly Dictionary<string, List<string>> _usedBy = new Dictionary<string, List<string>>();

        private UnityEngine.Camera _camera;
        private WoodBackdropFit _backdrop;
        private Bounds _content;
        private bool _hasContent;
        private int _page;

        /// <summary>Ключ выбранной формы, либо null. Читает панель.</summary>
        public string SelectedKey { get; private set; }

        public string SelectedLabel { get; private set; }

        public int PageCount => _pages.Count;

        public int PageIndex => _page;

        public string PageName => _pages.Count == 0 ? "—" : _pages[_page].LevelId;

        /// <summary>Где ещё встречается выбранная форма. Пустой список — только здесь.</summary>
        public IReadOnlyList<string> SelectedUsedBy =>
            SelectedKey != null && _usedBy.TryGetValue(SelectedKey, out var list)
                ? list
                : System.Array.Empty<string>();

        private void Start() => Build();

        /// <summary>Собрать каталог. Публичный и отдельно от Start — редакторная съёмка строит его
        /// без входа в Play, а там Start не зовётся (тот же идиом, что у <see cref="WoodSandbox"/>).</summary>
        public UnityEngine.Camera Build()
        {
            WoodArt.ResetCache();

            _camera = BuildCamera();
            _backdrop = BuildBackdrop(_camera);
            LoadCampaign();
            ShowPage(0);

            WoodTuning.Current.Apply();
            WoodTuning.Changed += Repaint;

            if (Application.isPlaying)
                WoodTunerPanel.Ensure().Visible = true;

            return _camera;
        }

        private void OnDestroy() => WoodTuning.Changed -= Repaint;

        // -------------------------------------------------------------- кампания

        /// <summary>
        /// Читаем DTO напрямую, минуя <c>JsonLevelSource</c>: тот тянет валидатор и падает на ВСЕЙ
        /// кампании, если хоть один уровень не прошёл проверку. Каталогу это ни к чему — ему нужны
        /// формы, а не гарантия проходимости.
        /// </summary>
        private void LoadCampaign()
        {
            var path = Path.Combine(Application.streamingAssetsPath, LevelsFile);
            if (!File.Exists(path))
            {
                Debug.LogError($"[wood] каталог: не нашёл {path}");
                return;
            }

            CampaignFileDto file;
            try
            {
                file = JsonConvert.DeserializeObject<CampaignFileDto>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[wood] каталог: кампания не разобралась — {e.Message}");
                return;
            }

            if (file?.Levels == null)
                return;

            var shapes = 0;
            foreach (var dto in file.Levels)
            {
                var level = dto.ToDomain();
                var page = new Page { LevelId = level.LevelId, Level = level };

                // Типы берём из ПЛЕЙСМЕНТОВ и дедуплицируем по kind: в LevelData отдельного списка
                // pieceTypes нет, тип живёт на каждой размещённой детали, и одинаковых там по нескольку.
                var seen = new HashSet<string>();
                if (level.Pieces != null)
                {
                    foreach (var placement in level.Pieces)
                    {
                        var type = placement.Type;
                        if (type?.Shape == null || !seen.Add(type.Kind))
                            continue;

                        page.Pieces.Add((type.Kind, type.Shape, type.Color));

                        var key = WoodPieceKey.Of(type.Shape);
                        if (key == null)
                            continue;
                        if (!_usedBy.TryGetValue(key, out var list))
                            _usedBy[key] = list = new List<string>();
                        list.Add($"{level.LevelId}/{type.Kind}");
                        shapes++;
                    }
                }

                if (page.Pieces.Count > 0)
                    _pages.Add(page);
            }

            Debug.Log($"[wood] каталог: {_pages.Count} уровней, {shapes} типов, {_usedBy.Count} разных форм");
        }

        // --------------------------------------------------------------- страницы

        public void NextPage() => ShowPage(_page + 1);

        public void PrevPage() => ShowPage(_page - 1);

        public void ShowPage(int index)
        {
            if (_pages.Count == 0)
                return;

            _page = ((index % _pages.Count) + _pages.Count) % _pages.Count;

            foreach (var go in _spawned)
                if (go != null)
                    DestroyImmediate(go);
            _spawned.Clear();
            _entries.Clear();

            // Схема цвета ищется по текущему уровню — каталог обязан его объявить, иначе
            // страницы красились бы схемой того уровня, который остался от прошлого показа.
            WoodTuning.CurrentLevelId = _pages[_page].LevelId;

            if (Assembled)
                LayoutAssembled(_pages[_page]);
            else
                Layout(_pages[_page]);

            MeasureContent();
            FitCamera();
        }

        /// <summary>Переключить «уровень ⇄ детали» и пересобрать страницу.</summary>
        public void ToggleAssembled()
        {
            Assembled = !Assembled;
            ShowPage(_page);
        }

        /// <summary>
        /// СОБРАННАЯ ФИГУРА — то, что игрок должен построить: детали стоят в своих слотах
        /// (<c>target.slots</c>), плюс видимая статика уровня. Именно здесь виден «левел дизайн» и
        /// то, как материал ведёт себя, когда детали стоят вплотную: контур и фаска работают на
        /// разделение соседей, а россыпь этого не показывает.
        ///
        /// Формы слотов восстанавливаются по kind из деталей уровня — в слоте лежит только имя вида
        /// (тот же приём, что у <c>CameraFramingMath.BuildKindMap</c> и <c>TargetPreviewPresenter</c>).
        /// </summary>
        private void LayoutAssembled(Page page)
        {
            var level = page.Level;
            if (level?.Target?.Slots == null)
            {
                Layout(page);
                return;
            }

            var kinds = new Dictionary<string, (Shape shape, string color)>();
            foreach (var (kind, shape, color) in page.Pieces)
                kinds[kind] = (shape, color);

            // Видимая статика — часть композиции (полка, ложе), без неё фигура висит в пустоте.
            if (level.StaticGeometry != null)
            {
                var visuals = level.StaticVisuals;
                for (var i = 0; i < level.StaticGeometry.Length; i++)
                {
                    var poly = level.StaticGeometry[i];
                    if (poly?.Outline == null)
                        continue;
                    if (visuals == null || i >= visuals.Count || !visuals[i].Visible)
                        continue;

                    var go = new GameObject($"Static_{i}");
                    var view = go.AddComponent<StaticGeometryView>();
                    view.Initialize(poly.Outline, visuals[i].Color);
                    _spawned.Add(go);

                    // Статику тоже надо уметь выбрать и настроить: пол, стена и колонна — такое же
                    // дерево в кадре, как детали, а крутить их до сих пор было нечем.
                    var renderer = go.GetComponent<Renderer>();
                    if (renderer == null || view.StyleKey == null)
                        continue;
                    var b = renderer.bounds;
                    _entries.Add(new Entry
                    {
                        Kind = StaticName(b.size),
                        Key = view.StyleKey,
                        ColorHex = visuals[i].Color,
                        Renderer = renderer,
                        Center = b.center,
                        Size = b.size,
                        IsStatic = true,
                    });
                }
            }

            foreach (var slot in level.Target.Slots)
            {
                if (!kinds.TryGetValue(slot.Kind, out var entry) || entry.shape == null)
                    continue;
                SpawnPosed(slot.Kind, entry.shape, entry.color,
                    new Vector2(slot.Position.X, slot.Position.Y), slot.Rotation);
            }
        }

        /// <summary>Деталь в ПРОИЗВОЛЬНОЙ позе (собранная фигура). Габарит для клика и подписи
        /// берётся из <c>renderer.bounds</c> — у повёрнутой детали свой контур уже не осевой.</summary>
        private void SpawnPosed(string kind, Shape shape, string colorHex, Vector2 position, float rotationRad)
        {
            var go = new GameObject($"Slot_{kind}");
            go.transform.position = new Vector3(position.x, position.y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, rotationRad * Mathf.Rad2Deg);

            var mesh = PieceMeshBuilder.Build(shape);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BlockMaterial.Shared;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            PieceShadow.Attach(go.transform, mesh);

            var bounds = renderer.bounds;
            var entry = new Entry
            {
                Kind = kind,
                Key = WoodPieceKey.Of(shape),
                Renderer = renderer,
                Center = bounds.center,
                Size = bounds.size,
            };
            Paint(entry, colorHex);

            _entries.Add(entry);
            _spawned.Add(go);
        }

        /// <summary>
        /// Раскладка ряда за рядом, по НАСТОЯЩИМ габаритам. Ряд переносится, когда следующая деталь
        /// не влезает в <see cref="RowWidth"/>: в кампании соседствуют брус 4.0×0.4 и кубик 0.18,
        /// и фиксированная сетка ячеек либо резала бы длинные, либо топила мелкие в пустоте.
        /// </summary>
        private void Layout(Page page)
        {
            var rows = new List<List<(string kind, Shape shape, string color, Vector2 size)>>();
            var row = new List<(string, Shape, string, Vector2)>();
            var rowW = 0f;

            foreach (var (kind, shape, color) in page.Pieces)
            {
                var size = SizeOf(shape);
                if (row.Count > 0 && rowW + GapX + size.x > RowWidth)
                {
                    rows.Add(row);
                    row = new List<(string, Shape, string, Vector2)>();
                    rowW = 0f;
                }
                row.Add((kind, shape, color, size));
                rowW += (row.Count > 1 ? GapX : 0f) + size.x;
            }
            if (row.Count > 0)
                rows.Add(row);

            var y = 0f;
            foreach (var r in rows)
            {
                var tallest = 0f;
                var total = 0f;
                foreach (var item in r)
                {
                    total += item.size.x;
                    if (item.size.y > tallest) tallest = item.size.y;
                }
                total += GapX * (r.Count - 1);

                var x = -total * 0.5f;
                foreach (var (kind, shape, color, size) in r)
                {
                    x += size.x * 0.5f;
                    Spawn(kind, shape, color, new Vector2(x, y - tallest * 0.5f), size);
                    x += size.x * 0.5f + GapX;
                }

                y -= tallest + GapY;
            }
        }

        private static Vector2 SizeOf(Shape shape)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in shape.Vertices)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            return new Vector2(Mathf.Max(maxX - minX, 0.01f), Mathf.Max(maxY - minY, 0.01f));
        }

        /// <summary>
        /// Деталь ставится ТЕМ ЖЕ путём, что в игре: меш от <see cref="PieceMeshBuilder"/>, общий
        /// материал, цвет через палитру и стейт Idle — именно так игрок видит деталь до тапа.
        /// Иначе каталог показывал бы не то, что настраивается.
        /// </summary>
        private void Spawn(string kind, Shape shape, string colorHex, Vector2 position, Vector2 size)
        {
            var go = new GameObject($"Cat_{kind}");
            go.transform.position = new Vector3(position.x, position.y, 0f);

            var mesh = PieceMeshBuilder.Build(shape);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BlockMaterial.Shared;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            PieceShadow.Attach(go.transform, mesh);

            var entry = new Entry
            {
                Kind = kind,
                Key = WoodPieceKey.Of(shape),
                Renderer = renderer,
                Center = position,
                Size = size,
            };
            Paint(entry, colorHex);

            _entries.Add(entry);
            _spawned.Add(go);
        }

        private void Paint(Entry entry, string colorHex)
        {
            var tuning = WoodTuning.Current;
            var color = new Color(0.29f, 0.56f, 0.89f);
            if (!string.IsNullOrEmpty(colorHex) && ColorUtility.TryParseHtmlString(colorHex, out var parsed))
                color = parsed;

            var mpb = new MaterialPropertyBlock();
            var final = tuning.Tint(tuning.ResolveLevelColor(color, entry.Kind), PieceVisualState.Idle);
            final.a = 1f;
            mpb.SetColor(BlockMaterial.ColorProperty, final);
            mpb.SetColor(BlockMaterial.LegacyColorProperty, final);
            tuning.ApplyPieceStyle(mpb, entry.Key);
            entry.Renderer.SetPropertyBlock(mpb);

            entry.ColorHex = colorHex;
        }

        /// <summary>Числа покрутили — переложить их в детали текущей страницы.</summary>
        private void Repaint()
        {
            foreach (var entry in _entries)
            {
                // Статику перекрашивает её собственный вью (он тоже подписан на Changed): у неё нет
                // стейта «ждёт/полетела», и прогон через Tint приглушил бы пол вместе с деталями.
                if (entry.IsStatic || entry.Renderer == null)
                    continue;
                Paint(entry, entry.ColorHex);
            }
        }

        // ---------------------------------------------------------------- выбор

        private void Update()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || _camera == null)
                return;

            // Тап по панели настройки не должен выбирать деталь под ней.
            var screen = mouse.position.ReadValue();
            if (screen.x > Screen.width * 0.62f)
                return;

            var world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            foreach (var entry in _entries)
            {
                var half = entry.Size * 0.5f;
                if (Mathf.Abs(world.x - entry.Center.x) > half.x || Mathf.Abs(world.y - entry.Center.y) > half.y)
                    continue;
                SelectedKey = entry.Key;
                SelectedLabel = $"{PageName}/{entry.Kind}  —  {entry.Key}";
                return;
            }
        }

        // -------------------------------------------------------------- подписи

        private GUIStyle _caption;
        private Texture2D _line;

        private void OnGUI()
        {
            if (_camera == null || _entries.Count == 0)
                return;

            if (_caption == null)
            {
                _caption = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Clamp(Screen.height / 90, 10, 22),
                    alignment = TextAnchor.UpperCenter,
                };
                _caption.normal.textColor = new Color(1f, 0.95f, 0.85f, 0.95f);
            }
            if (_line == null)
            {
                _line = new Texture2D(1, 1);
                _line.SetPixel(0, 0, Color.white);
                _line.Apply();
            }

            GUI.Label(new UnityEngine.Rect(10f, 8f, Screen.width * 0.6f, 40f),
                $"{PageName}   [{_page + 1}/{_pages.Count}]   {(Assembled ? "УРОВЕНЬ" : "детали")}", _caption);

            foreach (var entry in _entries)
            {
                // В собранной фигуре детали стоят вплотную — подписи под каждой слиплись бы в кашу и
                // закрыли бы саму фигуру. Там подписываем только выбранную.
                if (!Assembled || entry.Key == SelectedKey)
                {
                    var below = _camera.WorldToScreenPoint(
                        new Vector3(entry.Center.x, entry.Center.y - entry.Size.y * 0.5f, 0f));
                    GUI.Label(new UnityEngine.Rect(below.x - 90f, Screen.height - below.y + 4f, 180f, 40f),
                        $"{entry.Kind}\n{entry.Size.x:0.00}×{entry.Size.y:0.00}", _caption);
                }

                if (entry.Key != SelectedKey)
                    continue;

                // Рамка выделения — в экранных координатах: отдельный меш ради неё заводить незачем,
                // а мировая рамка ещё и участвовала бы в подгонке камеры.
                var min = _camera.WorldToScreenPoint(new Vector3(
                    entry.Center.x - entry.Size.x * 0.5f, entry.Center.y - entry.Size.y * 0.5f, 0f));
                var max = _camera.WorldToScreenPoint(new Vector3(
                    entry.Center.x + entry.Size.x * 0.5f, entry.Center.y + entry.Size.y * 0.5f, 0f));
                var rect = new UnityEngine.Rect(min.x - 4f, Screen.height - max.y - 4f,
                    (max.x - min.x) + 8f, (max.y - min.y) + 8f);
                Frame(rect, new Color(1f, 0.85f, 0.2f, 0.9f));
            }
        }

        private void Frame(UnityEngine.Rect r, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            const float t = 2f;
            GUI.DrawTexture(new UnityEngine.Rect(r.x, r.y, r.width, t), _line);
            GUI.DrawTexture(new UnityEngine.Rect(r.x, r.yMax - t, r.width, t), _line);
            GUI.DrawTexture(new UnityEngine.Rect(r.x, r.y, t, r.height), _line);
            GUI.DrawTexture(new UnityEngine.Rect(r.xMax - t, r.y, t, r.height), _line);
            GUI.color = prev;
        }

        // ---------------------------------------------------------------- сцена

        private UnityEngine.Camera BuildCamera()
        {
            var existing = UnityEngine.Camera.main;
            var camera = existing != null
                ? existing
                : new GameObject("CatalogCamera", typeof(UnityEngine.Camera)).GetComponent<UnityEngine.Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.35f, 0.16f, 0.06f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            return camera;
        }

        private static WoodBackdropFit BuildBackdrop(UnityEngine.Camera camera)
        {
            var sprite = WoodArt.Background;
            if (sprite == null)
                return null;
            var go = new GameObject("[Wood] Backdrop");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -10000;
            var fit = go.AddComponent<WoodBackdropFit>();
            fit.Bind(camera, sr);
            return fit;
        }

        /// <summary>Габарит страницы. Считаем по ВСЕМ поставленным объектам, а не только по деталям:
        /// в собранной фигуре есть ещё видимая статика, и без неё камера обрезала бы полку.</summary>
        private void MeasureContent()
        {
            var first = true;
            foreach (var go in _spawned)
            {
                if (go == null)
                    continue;
                var renderer = go.GetComponent<Renderer>();
                if (renderer == null)
                    continue;
                if (first)
                {
                    _content = renderer.bounds;
                    first = false;
                    continue;
                }
                _content.Encapsulate(renderer.bounds);
            }
            _hasContent = !first;
        }

        /// <summary>Вписать страницу в кадр. Запас снизу больше — под подписями деталей.</summary>
        public void FitCamera()
        {
            if (_camera == null || !_hasContent)
                return;

            const float margin = 0.8f;
            var aspect = _camera.aspect <= 0f ? 1f : _camera.aspect;
            var halfHeight = _content.extents.y + margin;
            var halfWidth = _content.extents.x + margin;

            _camera.orthographicSize = Mathf.Max(halfHeight, halfWidth / aspect);
            _camera.transform.position = new Vector3(_content.center.x, _content.center.y, -10f);

            // Фон обязан пересчитаться ПОСЛЕ камеры. Сам он это делает в LateUpdate, но в редакторе
            // и батче LateUpdate не идёт — и стена оставалась растянутой под стартовый
            // orthographicSize, то есть увеличенной вдвое и размытой в кашу. В Play не проявлялось.
            if (_backdrop != null)
                _backdrop.Fit();
        }

        private void LateUpdate() => FitCamera();
    }
}
#endif
