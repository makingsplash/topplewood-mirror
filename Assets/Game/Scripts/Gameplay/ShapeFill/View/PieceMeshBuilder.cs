using System;
using System.Collections.Generic;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Builds a flat convex fan mesh from a domain <see cref="Shape"/>. Shared by <see cref="PieceView"/>
    /// (the physical pieces) and <see cref="TargetPreviewView"/> (the mini preview) so both render from a
    /// single source of truth — no duplicated triangulation (RFC §4.4). v0 shapes are convex.
    /// Arc primitives are tessellated for the VIEW by <see cref="ShapeVisualOutline"/> (higher segment count
    /// than the ≤8-vertex physics collider, TASK-074) — the physics vertices are never used for rendering.
    ///
    /// <para>
    /// Меш несёт ЧЕТЫРЕ канала, и три из них существуют ради шейдера <c>Game/Wood Block</c>:
    /// uv0 — нормализованные по габариту координаты (запасной путь для чужих мешей),
    /// uv1 — расстояние до кромки В МИРОВЫХ ЕДИНИЦАХ (x) и маркер «меш собран здесь» (y),
    /// uv2 — габарит детали, uv3 — НОРМАЛЬ КРОМКИ этого сектора.
    /// </para>
    /// <para>
    /// Нормаль кромки — то, из-за чего меш собран НЕиндексированным (по три вершины на треугольник,
    /// вершины контура дублируются между соседними секторами). Владелец сказал «материал на всех кусках
    /// смотрится по-разному»: свет и фаска считались по uv, а uv нормализованы по габариту — у широкого
    /// бруска градиент шёл иначе, чем у узкого, у треугольника иначе, чем у обоих. Зная нормаль своей
    /// кромки, шейдер светит по МИРОВОМУ направлению света одинаково на любой детали и при любом
    /// повороте. Общая вершина усреднила бы нормали двух соседних кромок — поэтому вершины и разведены.
    /// Цена — втрое больше вершин на деталь (у квадрата со скруглением это 48 штук), при таком числе
    /// деталей на экране это ничто.
    /// </para>
    /// </summary>
    public static class PieceMeshBuilder
    {
        // View outline, NOT shape.Vertices: dome primitives render at ShapeVisualOutline.VisualArcSegments so the
        // mesh stays smooth while the collider stays at ≤8 vertices (TASK-074). Do not collapse this back to
        // shape.Vertices — that would re-facet the wheels. Straight-edged shapes pass through unchanged.
        // Ключ считается по ДОМЕННОЙ форме, а не по визуальному контуру: у круга и купола контур
        // тесселируется гуще, и ключ поехал бы от числа сегментов (см. WoodPieceKey).
        public static Mesh Build(Shape shape) => Build(ShapeVisualOutline.Build(shape), WoodPieceKey.Of(shape));

        /// <summary>
        /// Радиус скругления углов для детали ОПОРНОГО размера (<see cref="WoodTuning.DetailRefSize"/>);
        /// деталь другого размера получает его в своей пропорции. Углы режутся в геометрии,
        /// а не в шейдере: у деталей произвольный выпуклый контур, и «расстояние до угла» в шейдере
        /// восстановить нечем — фаска знает только расстояние до кромки.
        /// Живёт в <see cref="WoodTuning"/>: владелец крутит радиус ползунком прямо в Play, и
        /// <see cref="RebuildAll"/> перекладывает новую форму во все живые меши.
        /// </summary>
        public static float CornerRadius
        {
            get => WoodTuning.Current.CornerRadius;
            set => WoodTuning.Current.CornerRadius = value;
        }

        /// <summary>Build a flat convex fan mesh from a raw CCW outline (TASK-031: visible static geometry reuses
        /// the same triangulation as pieces). The outline must be convex — v0's fan cannot triangulate a concave
        /// polygon (author concave statics as several convex rectangles).</summary>
        public static Mesh Build(Vec2[] rawVerts) => Build(rawVerts, WoodPieceKey.Of(rawVerts));

        /// <summary><paramref name="key"/> — под каким именем эта форма ищет свою настройку
        /// (<see cref="WoodTuning.PieceOverrides"/>). Хранится вместе с контуром, чтобы
        /// <see cref="RebuildAll"/> перекладывал форму по тем же числам, что и при первой сборке.</summary>
        public static Mesh Build(Vec2[] rawVerts, string key)
        {
            var mesh = new Mesh { name = "PieceMesh" };
            Fill(mesh, rawVerts, key);
            Register(mesh, rawVerts, key);
            return mesh;
        }

        // ------------------------------------------------------- пересборка формы

        private readonly struct LiveMesh
        {
            public readonly WeakReference<Mesh> Mesh;
            public readonly Vec2[] Outline;
            public readonly string Key;

            public LiveMesh(Mesh mesh, Vec2[] outline, string key)
            {
                Mesh = new WeakReference<Mesh>(mesh);
                Outline = outline;
                Key = key;
            }
        }

        // Слабые ссылки: деталь уничтожается вместе со своим мешем, а реестр не должен её удерживать.
        private static readonly List<LiveMesh> Live = new List<LiveMesh>(128);

        private static void Register(Mesh mesh, Vec2[] outline, string key)
            => Live.Add(new LiveMesh(mesh, outline, key));

        /// <summary>
        /// Переложить форму во все живые меши — их читают и деталь, и её тень (тот же объект Mesh),
        /// поэтому тень подхватывает новые углы сама. Зовётся при смене радиуса скругления.
        /// Мёртвые записи выметаются здесь же: отдельного момента «деталь уничтожили» у нас нет.
        /// </summary>
        public static void RebuildAll()
        {
            for (var i = Live.Count - 1; i >= 0; i--)
            {
                if (!Live[i].Mesh.TryGetTarget(out var mesh) || mesh == null)
                {
                    Live.RemoveAt(i);
                    continue;
                }
                Fill(mesh, Live[i].Outline, Live[i].Key);
            }
        }

        // ------------------------------------------------------------- сборка меша

        private static void Fill(Mesh mesh, Vec2[] rawVerts, string key)
        {
            var tuning = WoodTuning.Current;

            // Скругление углов — до расчёта габарита и uv: иначе uv растянулись бы по прежнему,
            // более острому контуру, и волокно поехало бы относительно силуэта.
            //
            // Радиус — доля от размера ЭТОЙ детали, а не мировая константа. На детали кампании 0.3
            // (cap из grand_seesaw) мировые 0.095 срезали по 32% стороны с каждого угла: квадрат
            // превращался в таблетку, и никакой шейдер этого уже не спасал. Габарит меряем по
            // ИСХОДНОМУ контуру — скруглённый заведомо меньше, и радиус поехал бы сам от себя.
            var radius = tuning.CornerRadiusFor(key, ShortSide(rawVerts));
            var verts = RoundCorners(rawVerts, radius, Mathf.Max(1, tuning.CornerSegments));
            var n = verts.Length;
            if (n < 3)
            {
                mesh.Clear();
                return;
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            float cx = 0f, cy = 0f;
            for (var i = 0; i < n; i++)
            {
                var v = verts[i];
                cx += v.X; cy += v.Y;
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            cx /= n; cy /= n;

            // Вырожденный габарит (деталь толщиной в ноль по одной оси) обнулил бы делитель.
            var sizeX = Mathf.Max(maxX - minX, 1e-4f);
            var sizeY = Mathf.Max(maxY - minY, 1e-4f);
            var size = new Vector2(sizeX, sizeY);

            // Веер строится от ЦЕНТРОИДА, а не от нулевой вершины контура: шейдеру нужна вершина
            // ВНУТРИ формы, иначе кромкой оказывается вся деталь целиком и фаске негде лечь.
            // Побочная выгода: у куполов и колёс фаска идёт по силуэту, а не по bbox.
            var center = new Vector2(cx, cy);

            var count = n * 3;
            var vertices = new Vector3[count];
            var uv = new Vector2[count];      // нормализованные по габариту — запасной путь и волокно
            var uv2 = new Vector2[count];     // x: расстояние до кромки В ЕДИНИЦАХ, y: маркер
            var uv3 = new Vector2[count];     // габарит детали
            var uv4 = new Vector2[count];     // нормаль кромки сектора (наружу), в системе детали
            var triangles = new int[count];

            for (var i = 0; i < n; i++)
            {
                var b = new Vector2(verts[i].X, verts[i].Y);
                var c = new Vector2(verts[(i + 1) % n].X, verts[(i + 1) % n].Y);

                // Контур идёт против часовой, поэтому наружная нормаль ребра b→c — это (dy, -dx).
                var edge = c - b;
                var len = edge.magnitude;
                var normal = len > 1e-6f ? new Vector2(edge.y, -edge.x) / len : Vector2.up;

                // Высота центроида над этим ребром — она же расстояние до кромки в его секторе.
                // Модуль: у почти вырожденного сектора знак мог бы уйти в минус и вывернуть фаску.
                var height = Mathf.Abs(Vector2.Dot(center - b, normal));

                var t = i * 3;
                Vertex(t + 0, center, height);
                Vertex(t + 1, b, 0f);
                Vertex(t + 2, c, 0f);

                triangles[t + 0] = t + 0;
                triangles[t + 1] = t + 1;
                triangles[t + 2] = t + 2;

                void Vertex(int index, Vector2 p, float distanceToEdge)
                {
                    vertices[index] = new Vector3(p.x, p.y, 0f);
                    uv[index] = new Vector2((p.x - minX) / sizeX, (p.y - minY) / sizeY);
                    // uv2.y = 1 — маркер «расстояние до кромки посчитано». Меши, собранные не здесь
                    // (кварды зоны воздуха, линии силуэта), придут с uv2 = 0, и шейдер по этому
                    // маркеру откатится на оценку по габариту вместо того, чтобы залить их кромкой.
                    uv2[index] = new Vector2(distanceToEdge, 1f);
                    uv3[index] = size;
                    uv4[index] = normal;
                }
            }

            // Clear перед записью: у пересобираемого меша число вершин меняется вместе с радиусом
            // скругления, и без очистки Unity ругается на несогласованные каналы.
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, uv2);
            mesh.SetUVs(2, uv3);
            mesh.SetUVs(3, uv4);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        /// <summary>Короткая сторона габарита контура — по ней считается, насколько деталь мельче
        /// опорной (<see cref="WoodTuning.DetailFactor"/>). Именно короткая: на бруске 2.8×0.3
        /// грубым выглядит поперечный угол, а не продольный.</summary>
        private static float ShortSide(Vec2[] verts)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in verts)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }
            return Mathf.Max(Mathf.Min(maxX - minX, maxY - minY), 1e-4f);
        }

        /// <summary>
        /// Срезает каждый выпуклый угол контура короткой дугой (квадратичная безье через сам угол —
        /// от настоящей дуги на глаз не отличается, зато нет деления на синус половинного угла).
        /// Радиус зажимается по КОРОТКОЙ соседней стороне, иначе на узком бруске два соседних
        /// скругления съели бы сторону целиком и контур вывернулся бы.
        /// Почти прямые участки (тесселированная дуга купола/колеса от ShapeVisualOutline) пропускаются:
        /// скруглять там нечего, а вершины они бы утроили.
        /// </summary>
        private static Vec2[] RoundCorners(Vec2[] verts, float radius, int segments)
        {
            var n = verts.Length;
            if (n < 3 || radius <= 1e-4f)
                return verts;

            var result = new List<Vec2>(n * (segments + 1));
            for (var i = 0; i < n; i++)
            {
                var cur = new Vector2(verts[i].X, verts[i].Y);
                var prev = new Vector2(verts[(i - 1 + n) % n].X, verts[(i - 1 + n) % n].Y);
                var next = new Vector2(verts[(i + 1) % n].X, verts[(i + 1) % n].Y);

                var toPrev = prev - cur;
                var toNext = next - cur;
                var lenPrev = toPrev.magnitude;
                var lenNext = toNext.magnitude;
                if (lenPrev < 1e-5f || lenNext < 1e-5f)
                {
                    result.Add(verts[i]);
                    continue;
                }

                var dirPrev = toPrev / lenPrev;
                var dirNext = toNext / lenNext;
                if (Vector2.Dot(dirPrev, dirNext) < -0.985f)   // ≈ развёрнутый угол
                {
                    result.Add(verts[i]);
                    continue;
                }

                var r = Mathf.Min(radius, lenPrev * 0.45f, lenNext * 0.45f);
                var a = cur + dirPrev * r;
                var b = cur + dirNext * r;

                for (var s = 0; s <= segments; s++)
                {
                    var t = s / (float)segments;
                    var p = Vector2.Lerp(Vector2.Lerp(a, cur, t), Vector2.Lerp(cur, b, t), t);
                    result.Add(new Vec2(p.x, p.y));
                }
            }

            return result.ToArray();
        }
    }
}
