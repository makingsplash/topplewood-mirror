#if UNITY_EDITOR || GPIE_DEVELOPMENT
using System.Collections.Generic;
using Gameplay.Core;
using Gameplay.View;
using UnityEngine;

namespace Gameplay.Dev
{
    /// <summary>
    /// Тестовый уровень под настройку материала: деревянный фон, полка и куча деталей — все формы
    /// каталога, все цвета палитры, разные размеры и повороты. Нажал Play — сразу видно всё сразу,
    /// рядом панель <see cref="WoodTunerPanel"/>, и любой ползунок виден на всех деталях одновременно.
    ///
    /// Собирается КОДОМ, а не сценой-ассетом: сцены в этом проекте так и строятся, а ещё это значит,
    /// что песочницу можно поднять в любой сцене и она ничего в ней не портит.
    /// Запуск: <c>Tools → Wood Style → Песочница ▶</c> (см. <c>WoodSandboxMenu</c>) или руками —
    /// повесить этот компонент на пустой объект.
    ///
    /// Ряды подобраны не «чтобы красиво», а чтобы ловить ровно то, на что жаловался владелец:
    ///  * ряд 1 — ОДНА и та же деталь во всех цветах: сравнивать цвета между собой;
    ///  * ряд 2 — одна форма в четырёх размерах: волокно и фаска обязаны выглядеть одинаково;
    ///  * ряды 3-4 — весь каталог форм: треугольники, купола, трапеции;
    ///  * ряд 5 — детали КРУТЯТСЯ: свет мировой, поэтому светлая сторона обязана оставаться сверху,
    ///    а тень — не ездить вокруг детали.
    /// </summary>
    public sealed class WoodSandbox : MonoBehaviour
    {
        /// <summary>Скорость вращения нижнего ряда, град/сек. Ноль — стоп (тумблер в панели).</summary>
        public float SpinSpeed = 18f;

        private readonly List<Spinner> _spinners = new List<Spinner>();
        private readonly List<PaintedPiece> _pieces = new List<PaintedPiece>();

        private UnityEngine.Camera _camera;
        private float _shelfY = -10f;   // ставится BuildContent под последним рядом
        private Bounds _content;
        private bool _hasContent;

        private sealed class Spinner
        {
            public Transform Body;
            public Transform Shadow;
            public Mesh Mesh;
            public float AngleDeg;
        }

        private sealed class PaintedPiece
        {
            public MeshRenderer Renderer;
            public int PaletteIndex;
            public PieceVisualState State;
        }

        private void Start() => Build();

        /// <summary>
        /// Собрать песочницу. Публичный и отдельно от <see cref="Start"/>, потому что редакторный
        /// стенд (<c>Game.Editor.WoodStyleCapture</c>) снимает её кадр БЕЗ входа в Play, а там Start
        /// не зовётся. Панель поднимается только в Play: в редакторе IMGUI игрового объекта не идёт.
        /// </summary>
        public UnityEngine.Camera Build()
        {
            // Спрайты владельца грузятся из Resources один раз и кешируются; в песочнице сцена
            // создаётся заново на каждый вход в Play, а кеш статический — сбрасываем.
            WoodArt.ResetCache();

            _camera = BuildCamera();
            BuildBackdrop(_camera);
            BuildContent();
            BuildShelf();
            MeasureContent();
            FitCamera();

            // Числа могли приехать из сохранённого файла — разложить их по материалам до первого кадра.
            WoodTuning.Current.Apply();
            WoodTuning.Changed += Repaint;

            if (Application.isPlaying)
                WoodTunerPanel.Ensure().Visible = true;

            return _camera;
        }

        /// <summary>
        /// Вписать весь контент в кадр. Считается по РЕАЛЬНЫМ габаритам собранных деталей и текущему
        /// aspect камеры, а не подбирается числом: первый прогон снимался с фиксированной ортокамерой,
        /// и ряд треугольников уехал за края — на другом разрешении Game View уехал бы другой ряд.
        /// Публичный, потому что в редакторе (съёмка стенда) LateUpdate не идёт и звать надо руками.
        /// </summary>
        public void FitCamera()
        {
            if (_camera == null || !_hasContent)
                return;

            const float margin = 0.45f;
            var aspect = _camera.aspect <= 0f ? 1f : _camera.aspect;
            var halfHeight = _content.extents.y + margin;
            var halfWidth = _content.extents.x + margin;

            _camera.orthographicSize = Mathf.Max(halfHeight, halfWidth / aspect);
            _camera.transform.position = new Vector3(_content.center.x, _content.center.y, -10f);
        }

        /// <summary>Габарит всего собранного — по рендерерам, чтобы учесть и полку, и тени.
        /// Фон не в счёт: он сам подгоняется под камеру и раздул бы габарит до бесконечности.</summary>
        private void MeasureContent()
        {
            var first = true;
            foreach (var piece in _pieces)
            {
                if (piece.Renderer == null)
                    continue;
                if (first)
                {
                    _content = piece.Renderer.bounds;
                    first = false;
                    continue;
                }
                _content.Encapsulate(piece.Renderer.bounds);
            }

            var shelf = GameObject.Find("Shelf");
            var shelfRenderer = shelf == null ? null : shelf.GetComponent<Renderer>();
            if (shelfRenderer != null && !first)
                _content.Encapsulate(shelfRenderer.bounds);

            _hasContent = !first;
        }

        private void OnDestroy() => WoodTuning.Changed -= Repaint;

        // Каждый кадр, а не разово: окно Game меняет aspect на лету (и в редакторе это происходит
        // постоянно), а фон подгоняется под камеру — значит и камера обязана держать кадр сама.
        private void LateUpdate() => FitCamera();

        private void Update()
        {
            if (Mathf.Abs(SpinSpeed) < 1e-3f)
                return;

            foreach (var spinner in _spinners)
            {
                if (spinner.Body == null)
                    continue;
                spinner.AngleDeg += SpinSpeed * Time.deltaTime;
                spinner.Body.rotation = Quaternion.Euler(0f, 0f, spinner.AngleDeg);
                if (spinner.Shadow != null)
                    spinner.Shadow.localPosition =
                        PieceShadow.LocalOffset(spinner.AngleDeg * Mathf.Deg2Rad, spinner.Mesh);
            }
        }

        // ------------------------------------------------------------------- сцена

        private UnityEngine.Camera BuildCamera()
        {
            var existing = UnityEngine.Camera.main;
            var camera = existing != null
                ? existing
                : new GameObject("SandboxCamera", typeof(UnityEngine.Camera)).GetComponent<UnityEngine.Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 8.2f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.35f, 0.16f, 0.06f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            return camera;
        }

        private static void BuildBackdrop(UnityEngine.Camera camera)
        {
            var sprite = WoodArt.Background;
            if (sprite == null)
                return;
            var go = new GameObject("[Wood] Backdrop");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -10000;
            go.AddComponent<WoodBackdropFit>().Bind(camera, sr);
        }

        private void BuildShelf()
        {
            // Полка идёт через настоящий StaticGeometryView — тем же путём, что в игре
            // (прямоугольник + спрайт владельца 9-slice'ом).
            var outline = new[]
            {
                new Vec2(-5.2f, _shelfY - 0.4f), new Vec2(5.2f, _shelfY - 0.4f),
                new Vec2(5.2f, _shelfY + 0.4f), new Vec2(-5.2f, _shelfY + 0.4f),
            };
            new GameObject("Shelf").AddComponent<StaticGeometryView>().Initialize(outline, null);
        }

        // ----------------------------------------------------------------- контент

        private void BuildContent()
        {
            var palette = WoodTuning.Current.Palette;

            // Ряд 1: одна деталь во всех цветах — сравнивать цвета между собой. Палитра выросла до
            // одиннадцати, и одной строкой она сжимала бы весь кадр (камера подгоняется под контент),
            // поэтому бьём по шесть в строку.
            // Ряды идут КУРСОРОМ сверху вниз, а не по прошитым координатам: палитра переменной длины,
            // и с фиксированными координатами вторая строка цветов налезала на ряд размеров.
            const int perRow = 6;
            var row = 6.3f;
            for (var start = 0; start < palette.Count; start += perRow)
            {
                var count = Mathf.Min(perRow, palette.Count - start);
                var colorRow = new List<Vec2[]>();
                for (var i = 0; i < count; i++)
                    colorRow.Add(Shape.Box(1f, 1f).Vertices);
                var offset = start;
                PlaceRow(colorRow, row, i => offset + i, 0f, false);
                row -= 1.5f;
            }

            // Ряд 2: одна форма четырьмя размерами — волокно и фаска обязаны совпасть.
            row -= 0.9f;
            PlaceRow(new List<Vec2[]>
            {
                Shape.Box(0.55f, 0.55f).Vertices,
                Shape.Box(1f, 1f).Vertices,
                Shape.Box(1.7f, 1.7f).Vertices,
                Shape.Plank(2.6f, 0.8f).Vertices,
                Shape.Plank(0.8f, 2.0f).Vertices,
            }, row, i => 3, 0f, false);

            // Ряд 2б: НАСТОЯЩИЕ размеры деталей кампании. Ряд выше — размеры, которых в игре нет
            // ни на одном уровне: владелец настраивал материал на квадратах 1.0–1.7, а в
            // shapefill_levels.json 85 типов деталей с короткой стороной 0.18..1.60 и медианой 0.40.
            // Из-за этого песочница показывала стиль, которого игрок не видел, и «в песочнице
            // красиво, в игре параша» было неизбежным. Числа не выдуманы: 0.3 — cap из grand_seesaw,
            // 0.4 — weight оттуда же, 0.5 — pan, 2.8×0.3 — beam, 0.2×1.8 — roof_beam из well.
            row -= 2.2f;
            PlaceRow(new List<Vec2[]>
            {
                Shape.Box(0.2f, 0.2f).Vertices,
                Shape.Box(0.3f, 0.3f).Vertices,
                Shape.Box(0.4f, 0.4f).Vertices,
                Shape.Box(0.5f, 0.5f).Vertices,
                Shape.Plank(2.8f, 0.3f).Vertices,
                Shape.Plank(0.2f, 1.8f).Vertices,
            }, row, i => i, 0f, false);

            // Ряд 3: прямые формы каталога.
            row -= 2.6f;
            PlaceRow(new List<Vec2[]>
            {
                Shape.Triangle(1.5f, 1.4f).Vertices,
                Shape.RightTriangle(1.4f, 1.4f).Vertices,
                Shape.Gable(1.3f, 0.9f, 0.7f).Vertices,
                Shape.Trapezoid(1.7f, 0.9f, 1.2f).Vertices,
                Shape.Parallelogram(1.3f, 1.2f, 0.5f).Vertices,
            }, row, i => i, 0f, false);

            // Ряд 4: круглые — на них видно, как фаска идёт по силуэту, а не по габариту.
            row -= 2.6f;
            PlaceRow(new List<Vec2[]>
            {
                Outline(Shape.HalfDisc(0.85f, 7)),
                Outline(Shape.QuarterDisc(1.3f, 7)),
                Outline(Shape.Circle(0.8f)),
                Outline(Shape.Circle(0.45f)),
                Shape.Box(1.2f, 1.2f).Vertices,
            }, row, i => i + 1, 0f, false);

            // Ряд 5: то же самое, но КРУТИТСЯ — проверка мирового света и тени на повороте.
            row -= 2.9f;
            PlaceRow(new List<Vec2[]>
            {
                Shape.Box(1.4f, 1.4f).Vertices,
                Shape.Plank(2.2f, 0.8f).Vertices,
                Shape.Triangle(1.5f, 1.4f).Vertices,
                Outline(Shape.HalfDisc(0.85f, 7)),
            }, row, i => i + 2, 0f, true);

            // Отдельно: три детали одного цвета вплотную — по ним видно, работает ли контур.
            row -= 2.4f;
            Piece(Shape.Box(1.1f, 1.1f).Vertices, 4, new Vector2(-1.15f, row), 0f, false);
            Piece(Shape.Box(1.1f, 1.1f).Vertices, 4, new Vector2(0f, row), 0f, false);
            Piece(Shape.Box(1.1f, 1.1f).Vertices, 4, new Vector2(1.15f, row), 12f, false);

            row -= 2.3f;
            BuildStateRow(row);

            // Полка встаёт под последним рядом, а не по прошитой координате.
            _shelfY = row - 1.9f;
        }

        /// <summary>
        /// Ряд стейтов: пары ОДНОЙ формы и ОДНОГО цвета — слева ждущая деталь, справа летящая.
        /// Именно парами вплотную: разницу в яркости глаз ловит только на сравнении, порознь
        /// приглушённая деталь просто читается «другим цветом».
        /// Третья пара — взведённая (balance-режим), чтобы и её было с чем сравнить.
        /// </summary>
        private void BuildStateRow(float y)
        {
            // Зазоры считаются от ГАБАРИТОВ, а не от центров: с фиксированным шагом детали внутри пары
            // налезали друг на друга и пара читалась одной деталью, разрезанной пополам.
            const float inPairGap = 0.16f;
            const float betweenPairsGap = 1.1f;

            var pairs = new[]
            {
                (outline: Shape.Box(1.05f, 1.05f).Vertices, color: 0, active: PieceVisualState.Active),
                (outline: Shape.Triangle(1.15f, 1.05f).Vertices, color: 2, active: PieceVisualState.Active),
                (outline: Shape.Plank(1.5f, 0.75f).Vertices, color: 4, active: PieceVisualState.Armed),
            };

            // Сначала общая ширина — ряд обязан стоять по центру, как и все остальные.
            var total = 0f;
            foreach (var pair in pairs)
                total += Width(pair.outline) * 2f + inPairGap;
            total += betweenPairsGap * (pairs.Length - 1);

            var x = -total * 0.5f;
            foreach (var pair in pairs)
            {
                var w = Width(pair.outline);
                x += w * 0.5f;
                Piece(pair.outline, pair.color, new Vector2(x, y), 0f, false, PieceVisualState.Idle);
                x += w + inPairGap;
                Piece(pair.outline, pair.color, new Vector2(x, y), 0f, false, pair.active);
                x += w * 0.5f + betweenPairsGap;
            }
        }

        private static Vec2[] Outline(Shape shape) => ShapeVisualOutline.Build(shape);

        /// <summary>Раскладывает детали в ряд по центру, с равным зазором между габаритами.</summary>
        private void PlaceRow(List<Vec2[]> outlines, float y, System.Func<int, int> colorIndex,
                              float rotationDeg, bool spin)
        {
            // 0.4, а не больше: на портретном кадре (ортокамера 8.2) самый широкий ряд занимает
            // ±4.4 при половине кадра ±4.6 — с прежним зазором крайние детали уезжали за край.
            const float gap = 0.4f;
            var widths = new float[outlines.Count];
            var total = 0f;
            for (var i = 0; i < outlines.Count; i++)
            {
                widths[i] = Width(outlines[i]);
                total += widths[i];
            }
            total += gap * Mathf.Max(0, outlines.Count - 1);

            var x = -total * 0.5f;
            for (var i = 0; i < outlines.Count; i++)
            {
                x += widths[i] * 0.5f;
                Piece(outlines[i], colorIndex(i), new Vector2(x, y), rotationDeg, spin);
                x += widths[i] * 0.5f + gap;
            }
        }

        private static float Width(Vec2[] outline)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in outline)
            {
                if (v.X < min) min = v.X;
                if (v.X > max) max = v.X;
            }
            return Mathf.Max(max - min, 0.1f);
        }

        private Color ColorAt(int index)
        {
            var palette = WoodTuning.Current.Palette;
            if (palette.Count == 0)
                return Color.magenta;
            var hex = palette[((index % palette.Count) + palette.Count) % palette.Count];
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }

        /// <summary>Цвет берётся ИНДЕКСОМ палитры, а не значением: слоты крутят ползунками, и
        /// <see cref="Repaint"/> должен знать, какая деталь из какого слота покрашена. Состояние тоже
        /// помнится: приглушение ждущей детали — такая же настройка, как и всё остальное.</summary>
        private void Piece(Vec2[] outline, int paletteIndex, Vector2 position, float rotationDeg, bool spin,
                           PieceVisualState state = PieceVisualState.Active)
        {
            // Через Tint, а не палитрой напрямую: в игре деталь красится именно так, и песочница
            // обязана показывать тот же цвет, иначе настраивать по ней бессмысленно.
            var color = WoodTuning.Current.Tint(ColorAt(paletteIndex), state);
            var go = new GameObject("Piece");
            go.transform.position = new Vector3(position.x, position.y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, rotationDeg);

            var mesh = PieceMeshBuilder.Build(outline);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BlockMaterial.Shared;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var mpb = new MaterialPropertyBlock();
            color.a = 1f;
            mpb.SetColor(BlockMaterial.ColorProperty, color);
            mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
            renderer.SetPropertyBlock(mpb);

            var shadow = PieceShadow.Attach(go.transform, mesh);

            _pieces.Add(new PaintedPiece { Renderer = renderer, PaletteIndex = paletteIndex, State = state });

            if (spin)
                _spinners.Add(new Spinner
                {
                    Body = go.transform, Shadow = shadow, Mesh = mesh, AngleDeg = rotationDeg
                });
        }

        /// <summary>Палитру покрутили — перекрасить детали. Зовётся из <see cref="WoodTuning.Changed"/>.</summary>
        private void Repaint()
        {
            var mpb = new MaterialPropertyBlock();
            foreach (var piece in _pieces)
            {
                if (piece.Renderer == null)
                    continue;
                var color = WoodTuning.Current.Tint(ColorAt(piece.PaletteIndex), piece.State);
                mpb.SetColor(BlockMaterial.ColorProperty, color);
                mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
                piece.Renderer.SetPropertyBlock(mpb);
            }
        }
    }
}
#endif
