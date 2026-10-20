using System.Collections.Generic;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// TASK-614: пунктир вокруг ВИСЯЩЕЙ (ещё не тапнутой) детали — сигнал «эта деталь ждёт тапа». Плейтест владельца:
    /// висящие и упавшие детали были одинаково яркими, и игроки не понимали, что фигуры в воздухе неактивны.
    ///
    /// Лента — ДОЧЕРНИЙ меш детали (как <see cref="PieceShadow"/>), поэтому кувыркается и едет вместе с ней. Видимость
    /// решает <see cref="PieceView"/> по стейту (только Idle); здесь — геометрия и материал.
    ///
    /// Контур — силуэт ЛЮБОЙ формы: выпуклая деталь, полимино, freeform. Путь пунктира строится смещением силуэта наружу
    /// (<see cref="BuildPath"/>, морфологическое замыкание): выпуклые углы обходятся дугой, вогнутые скругляются, узкий
    /// вырез закрывается — путь не пересекает сам себя и не разворачивается назад (TASK-647). Штрихи
    /// режет шейдер <c>Game/Idle Dash Outline</c> по длине пути, записанной в uv, — меш от шага пунктира не зависит.
    ///
    /// Живые ленты помнятся списком: отступ, толщину и скругление владелец крутит ползунком в Play
    /// (<see cref="RefreshLive"/>), а без списка новая форма доехала бы только до деталей, созданных после правки.
    /// </summary>
    public static class IdleDashOutline
    {
        /// <summary>Локальный сдвиг по Z: чуть дальше от камеры, чем детали (камера смотрит вдоль +Z), но ближе тени
        /// (<c>PieceShadow</c>: 0.15 и дальше). Детали непрозрачны и пишут глубину, поэтому пунктир ложится ПОД любую
        /// деталь, включая соседнюю висящую, — иначе у деталей, стоящих вплотную, он лёг бы на краску соседа.</summary>
        public const float ZBehind = 0.05f;

        /// <summary>Шаг дуги на выпуклом углу пути: 180°/16 — на скруглении в 0.12 ед. граней не видно.</summary>
        private const float ArcStep = Mathf.PI / 16f;

        /// <summary>Запас ленты сверх полутолщины — под сглаживание края в шейдере (около двух пикселей кадра).</summary>
        private const float AaMargin = 0.015f;

        /// <summary>Сегментов на скругление угла силуэта перед смещением (как у <c>CornerSegments</c> деталей).</summary>
        private const int CornerSegments = 4;

        private static Material _shared;

        private readonly struct LiveDash
        {
            public readonly Transform Transform;
            public readonly Mesh Mesh;
            public readonly Vec2[] Contour;

            public LiveDash(Transform transform, Mesh mesh, Vec2[] contour)
            {
                Transform = transform;
                Mesh = mesh;
                Contour = contour;
            }
        }

        // Обычные ссылки: уничтоженный трансформ сравнивается с null, по этому признаку запись выметается.
        private static readonly List<LiveDash> Live = new List<LiveDash>(64);

        // Числа, по которым собраны живые ленты: пересобирать меши на каждый ползунок других вкладок незачем.
        private static Vector3 _builtWith = new Vector3(-1f, -1f, -1f);

        public static Material Shared
        {
            get
            {
                if (_shared == null)
                {
                    // Сериализованная ссылка из RuntimeShaders (TASK-554): строкой шейдер в плеер не попал бы.
                    var shader = RuntimeShaders.IdleDashOutline;
                    if (shader == null)
                        return null;
                    _shared = new Material(shader) { name = "IdleDashOutlineMaterial" };
                    _shared.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    WoodTuning.Current.ApplyToIdleDash(_shared);
                }
                return _shared;
            }
        }

        /// <summary>
        /// Вешает ленту на деталь. <paramref name="contour"/> — силуэт детали в её локальных координатах (любой обход).
        /// Возвращает объект ленты (или null, если шейдера нет): его включает и гасит <see cref="PieceView"/>.
        /// </summary>
        public static GameObject Attach(Transform owner, Vec2[] contour)
        {
            var material = Shared;
            if (material == null || owner == null || contour == null || contour.Length < 3)
                return null;

            var tuning = WoodTuning.Current;
            var mesh = new Mesh { name = "IdleDashMesh" };
            Fill(mesh, contour, tuning);

            var go = new GameObject("IdleDash");
            var t = go.transform;
            t.SetParent(owner, false);
            t.localPosition = new Vector3(0f, 0f, ZBehind);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Live.Add(new LiveDash(t, mesh, contour));
            _builtWith = Signature(tuning);
            return go;
        }

        /// <summary>Числа пунктира — в материал; форма ленты — во все живые меши, если её числа сдвинулись.</summary>
        public static void RefreshLive()
        {
            var tuning = WoodTuning.Current;
            if (_shared != null)
                tuning.ApplyToIdleDash(_shared);

            var signature = Signature(tuning);
            var rebuild = signature != _builtWith;
            _builtWith = signature;

            for (var i = Live.Count - 1; i >= 0; i--)
            {
                var dash = Live[i];
                if (dash.Transform == null || dash.Mesh == null)
                {
                    Live.RemoveAt(i);
                    continue;
                }
                if (rebuild)
                    Fill(dash.Mesh, dash.Contour, tuning);
            }
        }

        private static Vector3 Signature(WoodTuning t) => new Vector3(t.IdleDashOffset, t.IdleDashWidth, t.IdleDashCorner);

        private static void Fill(Mesh mesh, Vec2[] contour, WoodTuning tuning)
        {
            var half = tuning.IdleDashWidth * 0.5f + AaMargin;
            var path = BuildPath(contour, tuning.IdleDashOffset, tuning.IdleDashCorner, half);
            BuildRibbon(mesh, path, half);
        }

        // ------------------------------------------------------------------ путь

        /// <summary>
        /// Замкнутый путь пунктира (CCW) — граница МОРФОЛОГИЧЕСКОГО ЗАМЫКАНИЯ силуэта: силуэт раздувается на
        /// <c>offset + close</c>, петли раздутия срезаются (<see cref="Geometry.OuterLoop"/>), затем контур
        /// втягивается обратно на <c>close</c>, где <c>close = max(corner, gap)</c>. Путь не пересекает сам себя и
        /// нигде не ближе <paramref name="offset"/> к силуэту, кроме выпуклых углов при <c>corner &gt; offset</c>:
        /// там радиус угла пути больше отступа, и до вершины детали остаётся <c>corner − √2·(corner − offset)</c> (90°).
        ///
        /// Выпуклые углы: силуэт сначала скругляется на <c>corner − offset</c> (ТОЛЬКО выпуклые углы), раздутие
        /// обходит их дугой — радиус угла пути равен <paramref name="corner"/> (при <c>corner ≤ offset</c> — отступу).
        /// Вогнутые углы раздуваются до точного пересечения смещённых рёбер, а втягивание скругляет их дугой
        /// радиуса <c>close</c>. Вырез уже <c>2·(offset + close)</c> закрывается целиком: путь проходит над устьем,
        /// а не заходит внутрь, — так две стенки узкого выреза не сводят штрихи вплотную. <paramref name="gap"/> —
        /// нижняя граница <c>close</c> (лента передаёт свою полуширину: просвет между стенками не уже ленты).
        ///
        /// TASK-647, почему не «смещение и митра на каждой вершине», как было: вогнутый угол, скруглённый радиусом
        /// <c>corner − offset</c> &lt; offset, при смещении на offset выворачивается петлёй-«ласточкиным хвостом»
        /// (смещённая дуга бежит назад на <c>offset − r</c>), а лента на развороте пути на 180° строит треугольник-шип и
        /// схлопывает соседний квад в нить. Пустой массив — контур вырожден.
        /// </summary>
        public static Vector2[] BuildPath(Vec2[] contour, float offset, float corner, float gap = 0f)
        {
            var ccw = CleanCcw(contour);
            if (ccw.Count < 3)
                return new Vector2[0];

            var preRound = corner - offset;
            if (preRound > 1e-4f)
            {
                var rounded = PieceMeshBuilder.RoundCorners(ToVec(ccw), preRound, CornerSegments, convexOnly: true);
                ccw = CleanCcw(rounded);
                if (ccw.Count < 3)
                    return new Vector2[0];
            }

            var close = Mathf.Max(corner, gap, 0f);
            var dilated = CleanCcw(Geometry.OuterLoop(ToVec(CleanCcw(Dilate(ccw, offset + close)))));
            if (close <= 1e-5f || dilated.Count < 3)
                return dilated.ToArray();

            // Втягивание тоже даёт петли — там, где короткий выпуклый уступ зажат между вогнутыми углами (тостер:
            // ребро 0.084 между двумя вогнутыми) и втягивание его «съедает». Эти петли лежат СНАРУЖИ результата,
            // поэтому контур обходится в обратную сторону — от образа самой левой вершины, заведомо верной точки.
            DropShortEdges(dilated, close * 0.2f);
            var eroded = ToVec(Erode(dilated, close, out var startAt));
            System.Array.Reverse(eroded);
            var outer = Geometry.OuterLoop(eroded, eroded.Length - 1 - startAt);
            return CleanCcw(outer).ToArray();
        }

        /// <summary>Сырое раздутие CCW-контура: выпуклый угол — дуга радиуса <paramref name="distance"/>, вогнутый —
        /// <see cref="AddConcaveJoin"/>. Петли (узкий вырез, вогнутый стык, острый зуб) не срезаются — это работа
        /// <see cref="Geometry.OuterLoop"/>.</summary>
        private static List<Vector2> Dilate(List<Vector2> ccw, float distance)
        {
            var n = ccw.Count;
            var path = new List<Vector2>(n * 4);
            for (var i = 0; i < n; i++)
            {
                var prev = ccw[(i - 1 + n) % n];
                var cur = ccw[i];
                var next = ccw[(i + 1) % n];
                var n0 = OutwardNormal(prev, cur);
                var n1 = OutwardNormal(cur, next);

                if (Cross(cur - prev, next - cur) > 0f)
                {
                    // Выпуклый угол: дуга от нормали входящего ребра к нормали исходящего.
                    var angle = Mathf.Atan2(Cross(n0, n1), Vector2.Dot(n0, n1));
                    var steps = Mathf.Max(1, Mathf.CeilToInt(angle / ArcStep));
                    for (var k = 0; k <= steps; k++)
                        path.Add(cur + Rotate(n0, angle * k / steps) * distance);
                }
                else
                {
                    AddConcaveJoin(path, cur, n0, n1, distance);
                }
            }
            return path;
        }

        /// <summary>
        /// Вогнутый стык сырого офсета (сдвиг <paramref name="n0"/>/<paramref name="n1"/> уже со знаком стороны).
        /// Почти прямой — митра. Любой другой — приём Clipper: концы смещённых рёбер соединяются ЧЕРЕЗ саму вершину
        /// (<c>p+n0·d → p → p+n1·d</c>), и смещённые рёбра обязательно пересекаются — точку пересечения находит
        /// <see cref="Geometry.OuterLoop"/>, а лишний «бантик» срезается. Одной митры мало: если соседнее ребро короче
        /// выноса митры (ножка тостера после скругления — 0.0815 против 0.0935), смещённое ребро выходит ОТРИЦАТЕЛЬНОЙ
        /// длины, путь возвращается назад по той же прямой без единого пересечения, и срезать нечего — лента рисует шип.
        /// </summary>
        private static void AddConcaveJoin(List<Vector2> path, Vector2 cur, Vector2 n0, Vector2 n1, float distance)
        {
            // Порог Clipper2 (|sin| < 0.001): митрой — только почти прямой стык. Шаг дуги (11°) уже крутой: короткое
            // ребро между пересечением и вершиной дуги иначе даёт ту же шпору, только в доли миллиметра.
            if (Mathf.Abs(Cross(n0, n1)) < 1e-3f && Vector2.Dot(n0, n1) > 0f)
            {
                path.Add(cur + Miter(n0, n1, distance));
                return;
            }
            path.Add(cur + n0 * distance);
            path.Add(cur);
            path.Add(cur + n1 * distance);
        }

        /// <summary>Втягивание CCW-контура внутрь на <paramref name="distance"/> — зеркало <see cref="Dilate"/>: выпуклый
        /// угол — митра или <see cref="AddConcaveJoin"/> с обратными нормалями, вогнутый — дуга радиуса
        /// <paramref name="distance"/> вокруг вершины. Петли не срезаются.
        /// <paramref name="leftmostAt"/> — индекс образа самой левой вершины входа: самая левая вершина многоугольника
        /// выпуклая, у границы раздутия это дуга радиусом больше <paramref name="distance"/>, и её образ на результате.</summary>
        private static List<Vector2> Erode(List<Vector2> ccw, float distance, out int leftmostAt)
        {
            var n = ccw.Count;
            var leftmost = 0;
            for (var i = 1; i < n; i++)
                if (ccw[i].x < ccw[leftmost].x || (ccw[i].x == ccw[leftmost].x && ccw[i].y < ccw[leftmost].y))
                    leftmost = i;

            var path = new List<Vector2>(n * 2);
            leftmostAt = 0;
            for (var i = 0; i < n; i++)
            {
                if (i == leftmost)
                    leftmostAt = path.Count;
                var prev = ccw[(i - 1 + n) % n];
                var cur = ccw[i];
                var next = ccw[(i + 1) % n];
                var n0 = OutwardNormal(prev, cur);
                var n1 = OutwardNormal(cur, next);

                if (i == leftmost)
                {
                    // Старт обхода OuterLoop обязан лежать на результате — «бантик» начинается за его пределами.
                    path.Add(cur - Miter(n0, n1, distance));
                }
                else if (Cross(cur - prev, next - cur) >= 0f)
                {
                    // Выпуклый угол: вершина дуги раздутия (поворот не круче шага дуги) — митра; рёбра рядом не короче
                    // её выноса (см. DropShortEdges). Круче — тот же «бантик» через вершину, что у раздутия.
                    if (Vector2.Dot(n0, n1) >= Mathf.Cos(ArcStep) - 1e-4f)
                        path.Add(cur - Miter(n0, n1, distance));
                    else
                        AddConcaveJoin(path, cur, -n0, -n1, distance);
                }
                else
                {
                    // Вогнутый угол (поворот по часовой): дуга от −n0 к −n1, угол отрицательный.
                    var angle = Mathf.Atan2(Cross(n0, n1), Vector2.Dot(n0, n1));
                    var steps = Mathf.Max(1, Mathf.CeilToInt(-angle / ArcStep));
                    for (var k = 0; k <= steps; k++)
                        path.Add(cur - Rotate(n0, angle * k / steps) * distance);
                }
            }
            return path;
        }

        /// <summary>
        /// Схлопывает рёбра короче <paramref name="minLength"/>: из двух концов уходит вершина с МЕНЬШИМ поворотом
        /// (точка среза петли с крутым поворотом остаётся, соседняя вершина дуги — нет). Зачем: срез петли оставляет у
        /// точки пересечения микроребро, а митра втягивания выносит концы ребра вдоль него на
        /// <c>close·tg(поворот/2)</c> (до 0.0055 на шаге дуги) — ребро короче выноса выходит отрицательной длины, и путь
        /// разворачивается шпорой. Хорда дуги раздутия (≈0.018 на живых числах) порога не касается, а если и
        /// коснётся — дуга лишь станет грубее, но останется дугой.
        /// </summary>
        private static void DropShortEdges(List<Vector2> ccw, float minLength)
        {
            var sqr = minLength * minLength;
            for (var guard = ccw.Count; guard > 0 && ccw.Count > 3; guard--)
            {
                var n = ccw.Count;
                var cut = -1;
                for (var i = 0; i < n && cut < 0; i++)
                    if ((ccw[(i + 1) % n] - ccw[i]).sqrMagnitude < sqr)
                        cut = TurnAt(ccw, i) < TurnAt(ccw, (i + 1) % n) ? i : (i + 1) % n;
                if (cut < 0)
                    return;
                ccw.RemoveAt(cut);
            }
        }

        private static float TurnAt(List<Vector2> ccw, int i)
        {
            var n = ccw.Count;
            return Vector2.Angle(ccw[i] - ccw[(i - 1 + n) % n], ccw[(i + 1) % n] - ccw[i]);
        }

        /// <summary>Точка пересечения двух рёбер, смещённых на <paramref name="distance"/> по их нормалям.</summary>
        private static Vector2 Miter(Vector2 n0, Vector2 n1, float distance)
        {
            var denom = 1f + Vector2.Dot(n0, n1);
            var miter = denom > 1e-3f ? (n0 + n1) / denom * distance : n0 * distance;
            var limit = distance * 16f;
            if (miter.sqrMagnitude > limit * limit)
                miter = miter.normalized * limit;
            return miter;
        }

        private static Vec2[] ToVec(List<Vector2> points)
        {
            var result = new Vec2[points.Count];
            for (var i = 0; i < points.Count; i++)
                result[i] = new Vec2(points[i].x, points[i].y);
            return result;
        }

        /// <summary>
        /// Лента вдоль замкнутого пути: по две вершины на точку, полуширина <paramref name="half"/>. uv0 = (пройденная
        /// длина, смещение поперёк), uv1.x = полная длина пути — шейдер режет по ним штрихи. Первая точка повторена в
        /// конце с длиной = L, поэтому стык начала и конца не растягивает uv через весь путь.
        /// </summary>
        public static void BuildRibbon(Mesh mesh, Vector2[] path, float half)
        {
            mesh.Clear();
            var n = path?.Length ?? 0;
            if (n < 3)
                return;

            var total = 0f;
            for (var i = 0; i < n; i++)
                total += Vector2.Distance(path[i], path[(i + 1) % n]);

            var count = (n + 1) * 2;
            var vertices = new Vector3[count];
            var uv = new Vector2[count];
            var uv1 = new Vector2[count];
            var triangles = new int[n * 6];

            var s = 0f;
            for (var i = 0; i <= n; i++)
            {
                var idx = i % n;
                var p = path[idx];
                var nPrev = OutwardNormal(path[(idx - 1 + n) % n], p);
                var nNext = OutwardNormal(p, path[(idx + 1) % n]);
                var dir = nPrev + nNext;
                dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : nNext;
                // Митра: на повороте лента не должна худеть; потолок — на случай почти развёрнутого угла.
                var scale = 1f / Mathf.Max(Vector2.Dot(dir, nNext), 0.3f);
                var extrude = dir * (half * scale);

                if (i > 0)
                    s += Vector2.Distance(path[(i - 1) % n], p);

                var outer = i * 2;
                vertices[outer] = new Vector3(p.x + extrude.x, p.y + extrude.y, 0f);
                vertices[outer + 1] = new Vector3(p.x - extrude.x, p.y - extrude.y, 0f);
                uv[outer] = new Vector2(s, half);
                uv[outer + 1] = new Vector2(s, -half);
                uv1[outer] = new Vector2(total, 0f);
                uv1[outer + 1] = new Vector2(total, 0f);
            }

            for (var i = 0; i < n; i++)
            {
                var a = i * 2;
                var t = i * 6;
                triangles[t + 0] = a;
                triangles[t + 1] = a + 1;
                triangles[t + 2] = a + 2;
                triangles[t + 3] = a + 2;
                triangles[t + 4] = a + 1;
                triangles[t + 5] = a + 3;
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Контур против часовой, без повторов подряд и без замыкающего дубля.</summary>
        private static List<Vector2> CleanCcw(IReadOnlyList<Vec2> contour)
        {
            var list = new List<Vector2>(contour?.Count ?? 0);
            if (contour != null)
                foreach (var v in contour)
                    list.Add(new Vector2(v.X, v.Y));
            return CleanCcw(list);
        }

        private static List<Vector2> CleanCcw(List<Vector2> points)
        {
            var result = new List<Vector2>(points.Count);
            foreach (var p in points)
                if (result.Count == 0 || (p - result[result.Count - 1]).sqrMagnitude > 1e-10f)
                    result.Add(p);
            while (result.Count > 1 && (result[0] - result[result.Count - 1]).sqrMagnitude <= 1e-10f)
                result.RemoveAt(result.Count - 1);

            var area = 0f;
            for (int i = 0, j = result.Count - 1; i < result.Count; j = i++)
                area += result[j].x * result[i].y - result[i].x * result[j].y;
            if (area < 0f)
                result.Reverse();
            return result;
        }

        /// <summary>Наружная нормаль ребра a→b CCW-контура: (dy, −dx).</summary>
        private static Vector2 OutwardNormal(Vector2 a, Vector2 b)
        {
            var e = b - a;
            var len = e.magnitude;
            return len > 1e-6f ? new Vector2(e.y, -e.x) / len : Vector2.up;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static Vector2 Rotate(Vector2 v, float radians)
        {
            var c = Mathf.Cos(radians);
            var s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
