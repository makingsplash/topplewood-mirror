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
    /// (<see cref="BuildPath"/>): выпуклые углы обходятся дугой, вогнутые — точкой пересечения смещённых рёбер. Штрихи
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
            var path = BuildPath(contour, tuning.IdleDashOffset, tuning.IdleDashCorner);
            BuildRibbon(mesh, path, tuning.IdleDashWidth * 0.5f + AaMargin);
        }

        // ------------------------------------------------------------------ путь

        /// <summary>
        /// Замкнутый путь пунктира (CCW): силуэт, смещённый наружу на <paramref name="offset"/>. Углы пути скруглены
        /// радиусом <paramref name="corner"/>: силуэт сначала скругляется на <c>corner − offset</c>, затем выпуклые
        /// углы смещаются дугой радиуса <paramref name="offset"/> — в сумме радиус угла пути равен <paramref name="corner"/>
        /// (при <c>corner ≤ offset</c> он равен отступу). Вогнутые углы (полимино, freeform) — точка пересечения
        /// смещённых рёбер (митра), с потолком длины, чтобы острый вогнутый зуб не выстрелил иглой.
        /// Пустой массив — контур вырожден.
        /// </summary>
        public static Vector2[] BuildPath(Vec2[] contour, float offset, float corner)
        {
            var ccw = CleanCcw(contour);
            if (ccw.Count < 3)
                return new Vector2[0];

            var preRound = corner - offset;
            if (preRound > 1e-4f)
            {
                var asVec = new Vec2[ccw.Count];
                for (var i = 0; i < ccw.Count; i++)
                    asVec[i] = new Vec2(ccw[i].x, ccw[i].y);
                var rounded = PieceMeshBuilder.RoundCorners(asVec, preRound, CornerSegments);
                ccw = CleanCcw(rounded);
                if (ccw.Count < 3)
                    return new Vector2[0];
            }

            var n = ccw.Count;
            var path = new List<Vector2>(n * 4);
            for (var i = 0; i < n; i++)
            {
                var prev = ccw[(i - 1 + n) % n];
                var cur = ccw[i];
                var next = ccw[(i + 1) % n];
                var n0 = OutwardNormal(prev, cur);
                var n1 = OutwardNormal(cur, next);
                var turn = Cross(cur - prev, next - cur);

                if (turn > 0f)
                {
                    // Выпуклый угол: дуга от нормали входящего ребра к нормали исходящего.
                    var angle = Mathf.Atan2(Cross(n0, n1), Vector2.Dot(n0, n1));
                    var steps = Mathf.Max(1, Mathf.CeilToInt(angle / ArcStep));
                    for (var k = 0; k <= steps; k++)
                        path.Add(cur + Rotate(n0, angle * k / steps) * offset);
                }
                else
                {
                    var denom = 1f + Vector2.Dot(n0, n1);
                    var miter = denom > 1e-3f ? (n0 + n1) / denom * offset : n0 * offset;
                    var limit = offset * 4f;
                    if (miter.sqrMagnitude > limit * limit)
                        miter = miter.normalized * limit;
                    path.Add(cur + miter);
                }
            }

            return CleanCcw(path).ToArray();
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
