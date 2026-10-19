using Gameplay.Core;
using Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-614: путь пунктира ждущей детали — силуэт ЛЮБОЙ формы, смещённый наружу. Тесты держат три свойства, на
    /// которых стоит картинка: путь везде на заданном отступе от силуэта (выпуклые углы — дугой, вогнутые — митрой),
    /// радиус угла пути равен заданному скруглению, и лента несёт в uv длину замкнутого пути, по которой шейдер режет
    /// штрихи (целое число на путь).
    /// </summary>
    public sealed class IdleDashOutlineTests
    {
        private static readonly Vec2[] UnitSquare =
        {
            new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(1f, 1f), new Vec2(0f, 1f),
        };

        // L-тетромино (CCW): одна вогнутая вершина в (1, 1).
        private static readonly Vec2[] LShape =
        {
            new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(2f, 1f), new Vec2(1f, 1f), new Vec2(1f, 3f), new Vec2(0f, 3f),
        };

        [Test]
        public void ConvexSquare_PathKeepsOffsetEverywhere_AndRoundsCornersWithOffsetRadius()
        {
            const float offset = 0.1f;
            var path = IdleDashOutline.BuildPath(UnitSquare, offset, corner: 0f);

            Assert.That(path.Length, Is.GreaterThan(8), "у квадрата четыре дуги — точек больше, чем вершин");
            foreach (var p in path)
                Assert.That(DistanceToSquare(p), Is.EqualTo(offset).Within(1e-4f), $"точка {p} не на отступе");

            // Периметр смещённого квадрата со скруглёнными углами: 4 + 2πr (дуга — ломаная, чуть короче).
            var expected = 4f + 2f * Mathf.PI * offset;
            Assert.That(PathLength(path), Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void ClockwiseInput_IsNormalised_PathStillOutside()
        {
            var cw = new[] { UnitSquare[3], UnitSquare[2], UnitSquare[1], UnitSquare[0] };
            var path = IdleDashOutline.BuildPath(cw, 0.1f, 0f);
            foreach (var p in path)
                Assert.That(DistanceToSquare(p), Is.EqualTo(0.1f).Within(1e-4f),
                    "контур по часовой стрелке дал бы путь ВНУТРИ детали");
        }

        [Test]
        public void ConcavePolyomino_InnerCornerIsMiter_NoPointCloserThanOffset()
        {
            const float offset = 0.1f;
            var path = IdleDashOutline.BuildPath(LShape, offset, corner: 0f);

            // Вогнутый угол (1,1) → точка пересечения смещённых рёбер (1.1, 1.1); ни одна точка не ближе отступа.
            var hasMiter = false;
            foreach (var p in path)
            {
                Assert.That(DistanceToPolygon(p, LShape), Is.GreaterThanOrEqualTo(offset - 1e-4f), $"точка {p} внутри отступа");
                if ((p - new Vector2(1.1f, 1.1f)).sqrMagnitude < 1e-6f)
                    hasMiter = true;
            }
            Assert.That(hasMiter, Is.True, "вогнутый угол должен смещаться митрой, а не дугой");
        }

        [Test]
        public void CornerLargerThanOffset_PathCornerRadiusEqualsCorner()
        {
            const float offset = 0.08f;
            const float corner = 0.12f;
            var path = IdleDashOutline.BuildPath(UnitSquare, offset, corner);

            // Скругление угла пути радиусом R срезает с периметра смещённого квадрата 4·(2 − π/2)·R. Сравниваем: путь =
            // (4 + 8·offset) − 4·(2 − π/2)·corner. Допуск 0.02, а не точность: доля угла сверх отступа (R − offset)
            // скругляется той же квадратичной безье, что углы меша детали (PieceMeshBuilder.RoundCorners), а безье на
            // 90° короче дуги на ~6% своей длины — у этого квадрата это −0.015 к периметру.
            var expected = 4f + 8f * offset - 4f * (2f - Mathf.PI / 2f) * corner;
            Assert.That(PathLength(path), Is.EqualTo(expected).Within(0.02f));

            // На прямых участках отступ прежний: скругление трогает только углы (у угла путь ПО ПОСТРОЕНИЮ ближе к
            // вершине детали, чем отступ, — так у любого скругления радиусом больше отступа).
            // Габарит пути — ровно квадрат, раздвинутый на отступ: прямые стороны стоят на отступе, наружу не уходит ничего.
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in path)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            Assert.That(minX, Is.EqualTo(-offset).Within(1e-4f));
            Assert.That(minY, Is.EqualTo(-offset).Within(1e-4f));
            Assert.That(maxX, Is.EqualTo(1f + offset).Within(1e-4f));
            Assert.That(maxY, Is.EqualTo(1f + offset).Within(1e-4f));
        }

        [Test]
        public void Ribbon_CarriesClosedPathLength_AndRepeatsFirstPointAtTheEnd()
        {
            var path = IdleDashOutline.BuildPath(UnitSquare, 0.1f, 0f);
            var mesh = new Mesh();
            try
            {
                IdleDashOutline.BuildRibbon(mesh, path, 0.05f);
                var uv = new System.Collections.Generic.List<Vector2>();
                var uv1 = new System.Collections.Generic.List<Vector2>();
                mesh.GetUVs(0, uv);
                mesh.GetUVs(1, uv1);

                Assert.That(mesh.vertexCount, Is.EqualTo((path.Length + 1) * 2));
                var total = PathLength(path);
                Assert.That(uv1[0].x, Is.EqualTo(total).Within(1e-4f));
                Assert.That(uv[0].x, Is.EqualTo(0f));
                Assert.That(uv[uv.Count - 1].x, Is.EqualTo(total).Within(1e-4f), "стык пути: последняя пара на длине L");
                Assert.That(uv[0].y, Is.EqualTo(0.05f).Within(1e-6f));
                Assert.That(uv[1].y, Is.EqualTo(-0.05f).Within(1e-6f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void DegenerateContour_GivesEmptyPath()
        {
            var path = IdleDashOutline.BuildPath(new[] { new Vec2(0f, 0f), new Vec2(1f, 0f) }, 0.1f, 0.1f);
            Assert.That(path, Is.Empty);
        }

        private static float PathLength(Vector2[] path)
        {
            var total = 0f;
            for (var i = 0; i < path.Length; i++)
                total += Vector2.Distance(path[i], path[(i + 1) % path.Length]);
            return total;
        }

        private static float DistanceToSquare(Vector2 p) => DistanceToPolygon(p, UnitSquare);

        private static float DistanceToPolygon(Vector2 p, Vec2[] poly)
        {
            var best = float.MaxValue;
            for (var i = 0; i < poly.Length; i++)
            {
                var a = new Vector2(poly[i].X, poly[i].Y);
                var b = new Vector2(poly[(i + 1) % poly.Length].X, poly[(i + 1) % poly.Length].Y);
                var ab = b - a;
                var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }
    }
}
