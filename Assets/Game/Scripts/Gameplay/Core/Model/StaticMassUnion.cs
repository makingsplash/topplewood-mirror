using System;
using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// TASK-567 (долг TASK-350): какие ВИДИМЫЕ залитые блоки статики рисуются ОДНОЙ визуальной массой и где у этой
    /// массы граница. Чистая геометрия, без Unity: её зовёт только слой отрисовки (<c>StaticGeometrySpawner</c>) на
    /// загрузке уровня. Сим, коллайдеры и <see cref="LevelData"/> этот класс не видят — физика и poseHash от слияния
    /// не зависят по построению.
    /// <para>
    /// Правило слияния: блоки одного цвета, которые ПЕРЕКРЫВАЮТСЯ или делят ребро положительной длины. Касание в
    /// точке (угол в угол) массу не образует — союз выродился бы в «бантик» с нулевой перемычкой, и поле расстояний в
    /// этой точке рвалось бы. Щель шире <see cref="TouchEps"/> или другой цвет — авторская граница, шов остаётся.
    /// </para>
    /// <para>
    /// Граница — НЕ кольца объединения, а множество отрезков («buried-edge»): ребро члена, лежащее внутри или на
    /// границе другого члена, погребено и в контур не входит. Потребитель — поле расстояний (<c>SilhouetteField</c>),
    /// ему нужны ближайший отрезок и тест «внутри», а не упорядоченные кольца с иерархией дырок. Дырка союза — просто
    /// ещё несколько отрезков. Почему не Clipper2 и не <c>max(dᵢ)</c> — ADR 0006 п. 3.
    /// </para>
    /// Детерминизм: фиксированный порядок обхода (члены по индексу, рёбра по порядку), без хешей и без сортировки по
    /// float-ключам с равенствами, которые могли бы разрешиться по-разному.
    /// </summary>
    public static class StaticMassUnion
    {
        /// <summary>Допуск касания в мировых единицах. Авторская сетка манифеста 0.01–0.05, ошибка разбора float ~1e-6:
        /// 1e-3 ловит авторский стык и не склеивает щель, оставленную намеренно (самая узкая в кампании — 0.10).</summary>
        public const float TouchEps = 1e-3f;

        /// <summary>Минимальная длина общего участка рёбер, чтобы касание считалось касанием по РЕБРУ, а не в точке.</summary>
        public const float ContactMinLen = 1e-3f;

        /// <summary>Отрезок границы массы и его наружная единичная нормаль.</summary>
        public readonly struct BoundarySegment
        {
            public readonly Vec2 A;
            public readonly Vec2 B;
            public readonly Vec2 Normal;

            public BoundarySegment(Vec2 a, Vec2 b, Vec2 normal)
            {
                A = a;
                B = b;
                Normal = normal;
            }
        }

        /// <summary>Входит ли блок в слияние вообще: видимый, залитый (не лента <c>surface</c>), контур ≥ 3 вершин.
        /// Сюда попадают и выпуклые солиды, и вогнутые <c>collider:false</c>-массы.</summary>
        public static bool IsCandidate(Polygon polygon, StaticVisual visual)
            => visual.Visible && polygon != null && !polygon.IsSurface
               && polygon.Outline != null && polygon.Outline.Length >= 3;

        /// <summary>
        /// Группы видимых залитых статик. Возвращает ТОЛЬКО группы из ≥ 2 членов (одиночка рисуется прежним путём).
        /// Порядок групп — по минимальному индексу члена, члены — по возрастанию индекса.
        /// </summary>
        public static int[][] Group(IReadOnlyList<Polygon> statics, IReadOnlyList<StaticVisual> visuals)
        {
            if (statics == null || visuals == null)
                return Array.Empty<int[]>();

            var count = Math.Min(statics.Count, visuals.Count);
            var parent = new int[count];
            var candidate = new bool[count];
            var colorKey = new string[count];
            for (var i = 0; i < count; i++)
            {
                parent[i] = i;
                candidate[i] = IsCandidate(statics[i], visuals[i]);
                colorKey[i] = ColorKey(visuals[i].Color);
            }

            for (var i = 0; i < count; i++)
            {
                if (!candidate[i])
                    continue;
                for (var j = i + 1; j < count; j++)
                {
                    if (!candidate[j] || colorKey[i] != colorKey[j])
                        continue;
                    if (InContact(statics[i].Outline, statics[j].Outline))
                        Union(parent, i, j);
                }
            }

            // Корень union-find — минимальный индекс группы (Union всегда вешает больший на меньший), поэтому обход по
            // возрастанию i сам даёт и порядок групп, и порядок членов.
            var byRoot = new Dictionary<int, List<int>>();
            var roots = new List<int>();
            for (var i = 0; i < count; i++)
            {
                if (!candidate[i])
                    continue;
                var root = Find(parent, i);
                if (!byRoot.TryGetValue(root, out var members))
                {
                    members = new List<int>();
                    byRoot[root] = members;
                    roots.Add(root);
                }
                members.Add(i);
            }

            var result = new List<int[]>();
            foreach (var root in roots)
                if (byRoot[root].Count >= 2)
                    result.Add(byRoot[root].ToArray());
            return result.ToArray();
        }

        /// <summary>
        /// Граница объединения как множество отрезков (без колец). <paramref name="members"/> — мировые контуры членов
        /// (любая ориентация: каждый приводится к CCW здесь же). Пустой массив — вырожденная группа.
        /// </summary>
        public static BoundarySegment[] Boundary(Vec2[][] members)
        {
            if (members == null || members.Length == 0)
                return Array.Empty<BoundarySegment>();

            var ccw = new Vec2[members.Length][];
            for (var m = 0; m < members.Length; m++)
                ccw[m] = ToCcw(members[m]);

            var result = new List<BoundarySegment>();
            var cuts = new List<float>();
            for (var m = 0; m < ccw.Length; m++)
            {
                var poly = ccw[m];
                if (poly == null)
                    continue;
                for (var e = 0; e < poly.Length; e++)
                {
                    var a = poly[e];
                    var b = poly[(e + 1) % poly.Length];
                    var ab = b - a;
                    var len = ab.Length;
                    if (len <= TouchEps)
                        continue;
                    var normal = new Vec2(ab.Y / len, -ab.X / len); // наружу для CCW: (dy, −dx)

                    // 1. Параметры разбиения ребра — где через него проходят рёбра ДРУГИХ членов.
                    cuts.Clear();
                    cuts.Add(0f);
                    cuts.Add(1f);
                    for (var o = 0; o < ccw.Length; o++)
                    {
                        if (o == m || ccw[o] == null)
                            continue;
                        CollectCuts(a, b, len, ccw[o], cuts);
                    }
                    cuts.Sort();

                    // 2–3. Каждый под-отрезок: наружная точка у его середины внутри другого члена — погребён.
                    var mergeT = TouchEps / len;
                    var t0 = cuts[0];
                    for (var c = 1; c < cuts.Count; c++)
                    {
                        var t1 = cuts[c];
                        if (t1 - t0 <= mergeT)
                            continue; // слить разрез ближе допуска с предыдущим
                        var p0 = a + ab * t0;
                        var p1 = a + ab * t1;
                        var mid = a + ab * ((t0 + t1) * 0.5f);
                        var probe = mid + normal * (2f * TouchEps);
                        if (!InsideOther(ccw, m, probe))
                            result.Add(new BoundarySegment(p0, p1, normal));
                        t0 = t1;
                    }
                }
            }
            return result.ToArray();
        }

        /// <summary>Внутри объединения: внутри любого члена, граница (в пределах <see cref="TouchEps"/>) считается внутри.</summary>
        public static bool InsideAny(Vec2[][] members, float x, float y)
        {
            if (members == null)
                return false;
            var p = new Vec2(x, y);
            foreach (var poly in members)
                if (poly != null && poly.Length >= 3 && InsideOrOn(poly, p))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ контакт пары

        /// <summary>Контакт пары: общий участок коллинеарных рёбер длиннее <see cref="ContactMinLen"/>, собственное
        /// пересечение рёбер или вершина одного строго внутри другого. Касание в одной точке — НЕ контакт.</summary>
        public static bool InContact(Vec2[] a, Vec2[] b)
        {
            if (a == null || b == null || a.Length < 3 || b.Length < 3)
                return false;

            for (var i = 0; i < a.Length; i++)
            {
                var a0 = a[i];
                var a1 = a[(i + 1) % a.Length];
                for (var j = 0; j < b.Length; j++)
                {
                    var b0 = b[j];
                    var b1 = b[(j + 1) % b.Length];
                    if (CollinearOverlap(a0, a1, b0, b1) > ContactMinLen)
                        return true;
                    if (ProperIntersection(a0, a1, b0, b1))
                        return true;
                }
            }

            foreach (var v in a)
                if (StrictlyInside(b, v))
                    return true;
            foreach (var v in b)
                if (StrictlyInside(a, v))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ примитивы

        private static string ColorKey(string color)
            => string.IsNullOrWhiteSpace(color) ? string.Empty : color.Trim().ToUpperInvariant();

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void Union(int[] parent, int i, int j)
        {
            var ri = Find(parent, i);
            var rj = Find(parent, j);
            if (ri == rj)
                return;
            if (ri < rj) parent[rj] = ri;
            else parent[ri] = rj;
        }

        private static Vec2[] ToCcw(Vec2[] poly)
        {
            if (poly == null || poly.Length < 3)
                return null;
            var area = 0f;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                area += poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
            if (area >= 0f)
                return poly;
            var reversed = new Vec2[poly.Length];
            for (var i = 0; i < poly.Length; i++)
                reversed[i] = poly[poly.Length - 1 - i];
            return reversed;
        }

        private static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>Длина общего участка двух отрезков, если они коллинеарны в пределах допуска; иначе 0.</summary>
        private static float CollinearOverlap(Vec2 a0, Vec2 a1, Vec2 b0, Vec2 b1)
        {
            var d = a1 - a0;
            var len = d.Length;
            if (len <= TouchEps)
                return 0f;
            var u = d * (1f / len);
            // Оба конца b — на прямой a в пределах допуска.
            if (Math.Abs(Cross(u, b0 - a0)) > TouchEps || Math.Abs(Cross(u, b1 - a0)) > TouchEps)
                return 0f;
            var s0 = Vec2.Dot(b0 - a0, u);
            var s1 = Vec2.Dot(b1 - a0, u);
            var lo = Math.Max(0f, Math.Min(s0, s1));
            var hi = Math.Min(len, Math.Max(s0, s1));
            return Math.Max(0f, hi - lo);
        }

        /// <summary>Собственное пересечение: каждый отрезок пересекает прямую другого строго (концы не касаются).</summary>
        private static bool ProperIntersection(Vec2 a0, Vec2 a1, Vec2 b0, Vec2 b1)
        {
            var o1 = Orient(a0, a1, b0);
            var o2 = Orient(a0, a1, b1);
            var o3 = Orient(b0, b1, a0);
            var o4 = Orient(b0, b1, a1);
            return o1 * o2 < 0 && o3 * o4 < 0;
        }

        /// <summary>Знак поворота c относительно a→b с допуском в мировых единицах (расстояние c до прямой ab).</summary>
        private static int Orient(Vec2 a, Vec2 b, Vec2 c)
        {
            var ab = b - a;
            var len = ab.Length;
            if (len <= 1e-9f)
                return 0;
            var dist = Cross(ab, c - a) / len;
            if (dist > TouchEps) return 1;
            if (dist < -TouchEps) return -1;
            return 0;
        }

        private static bool StrictlyInside(Vec2[] poly, Vec2 p)
            => Geometry.PointInPolygon(poly, p) && Geometry.DistanceToPolygonBoundary(poly, p) > TouchEps;

        private static bool InsideOrOn(Vec2[] poly, Vec2 p)
            => Geometry.PointInPolygon(poly, p) || Geometry.DistanceToPolygonBoundary(poly, p) <= TouchEps;

        private static bool InsideOther(Vec2[][] polys, int self, Vec2 p)
        {
            for (var o = 0; o < polys.Length; o++)
                if (o != self && polys[o] != null && InsideOrOn(polys[o], p))
                    return true;
            return false;
        }

        /// <summary>Параметры t ∈ (0, 1) на ребре a→b, где его пересекают или касаются рёбра контура
        /// <paramref name="other"/>: собственные пересечения и проекции концов, лежащих на ребре (коллинеарные
        /// перекрытия и Т-стыки).</summary>
        private static void CollectCuts(Vec2 a, Vec2 b, float len, Vec2[] other, List<float> cuts)
        {
            var ab = b - a;
            var u = ab * (1f / len);
            for (var k = 0; k < other.Length; k++)
            {
                var c = other[k];
                var d = other[(k + 1) % other.Length];

                // Конец чужого ребра на нашем ребре.
                if (Math.Abs(Cross(u, c - a)) <= TouchEps)
                {
                    var s = Vec2.Dot(c - a, u) / len;
                    if (s > 0f && s < 1f)
                        cuts.Add(s);
                }

                // Собственное пересечение — точка пересечения прямых.
                if (ProperIntersection(a, b, c, d))
                {
                    var cd = d - c;
                    var denom = Cross(ab, cd);
                    if (Math.Abs(denom) > 1e-12f)
                    {
                        var t = Cross(c - a, cd) / denom;
                        if (t > 0f && t < 1f)
                            cuts.Add(t);
                    }
                }
            }
        }
    }
}
