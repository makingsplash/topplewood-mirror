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

        // TASK-647: числа живого тюнинга (WoodTuning: отступ 0.0375, скругление 0.056, лента 0.022/2 + AA 0.015) —
        // ровно при них скоба storm_traffic_light дала шипы. corner > offset, значит силуэт перед смещением скругляется.
        private const float LiveOffset = 0.0375f;
        private const float LiveCorner = 0.056f;
        private const float LiveHalf = 0.011f + 0.015f;

        // U-скоба (CCW): квадрат 1×1 с вырезом сверху шириной 0.3 (шире 2·(отступ + скругление) = 0.187 — путь заходит).
        private static readonly Vec2[] WideU =
        {
            new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(1f, 1f), new Vec2(0.65f, 1f),
            new Vec2(0.65f, 0.3f), new Vec2(0.35f, 0.3f), new Vec2(0.35f, 1f), new Vec2(0f, 1f),
        };

        // Та же скоба с вырезом 0.1 — уже 0.187: стенки свели бы штрихи вплотную.
        private static readonly Vec2[] NarrowU =
        {
            new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(1f, 1f), new Vec2(0.55f, 1f),
            new Vec2(0.55f, 0.3f), new Vec2(0.45f, 0.3f), new Vec2(0.45f, 1f), new Vec2(0f, 1f),
        };

        // Корпус тостера (toaster, деталь body) как есть в манифесте: ножки, скосы и короткий выпуклый уступ (ребро
        // 0.084) между двумя вогнутыми углами. На нём после первой версии фикса шипы остались: петлю давало уже
        // втягивание замыкания, «съедавшее» уступ.
        private static readonly Vec2[] ToasterBody =
        {
            new Vec2(-1.15f, -0.825f), new Vec2(-0.85f, -0.825f), new Vec2(-0.85f, -0.725f), new Vec2(0.85f, -0.725f),
            new Vec2(0.85f, -0.825f), new Vec2(1.15f, -0.825f), new Vec2(1.15f, -0.725f), new Vec2(1.27f, -0.725f),
            new Vec2(1.32f, -0.675f), new Vec2(1.32f, -0.425f), new Vec2(0.62f, -0.425f), new Vec2(0.62f, -0.341f),
            new Vec2(0.3f, -0.225f), new Vec2(0.3f, 0.825f), new Vec2(-0.3f, 0.825f), new Vec2(-0.3f, -0.225f),
            new Vec2(-0.62f, -0.341f), new Vec2(-0.62f, -0.425f), new Vec2(-1.32f, -0.425f), new Vec2(-1.32f, -0.675f),
            new Vec2(-1.27f, -0.725f), new Vec2(-1.15f, -0.725f),
        };

        [Test]
        public void ConcaveShapes_LiveTuning_NoReversal_NoSelfCrossing_NoPointInsideOffset()
        {
            foreach (var shape in new[] { WideU, NarrowU, ToasterBody })
            {
                var path = IdleDashOutline.BuildPath(shape, LiveOffset, LiveCorner, LiveHalf);
                Assert.That(path.Length, Is.GreaterThan(8));

                // Шип — разворот пути: смещённая скруглённая вогнутая дуга бежала назад, поворот ≈ 180°.
                // Шаг дуги пути — 11.25°, скругление силуэта — 22.5° на сегмент; 30° — с запасом, разворот не пролезет.
                Assert.That(MaxTurnDegrees(path, out var at), Is.LessThan(30f),
                    $"путь разворачивается — лента даст шип: {at}");
                Assert.That(FindSelfCrossing(path), Is.Null, "путь пересекает сам себя");
                // У ВЫПУКЛОГО угла путь по построению ближе отступа (радиус угла пути — скругление, а оно больше
                // отступа): до вершины там R − √2·(R − offset). Вогнутые углы и стенки выреза — строго на отступе и дальше.
                var cornerReach = LiveCorner - Mathf.Sqrt(2f) * (LiveCorner - LiveOffset);
                foreach (var p in path)
                {
                    Assert.That(InsidePolygon(p, shape), Is.False, $"точка {p} внутри детали");
                    var floor = NearConvexVertex(p, shape, LiveCorner) ? cornerReach : LiveOffset;
                    Assert.That(DistanceToPolygon(p, shape), Is.GreaterThanOrEqualTo(floor - 2e-3f),
                        $"точка {p} ближе отступа");
                }
            }
        }

        /// <summary>
        /// Класс, а не три формы: КАЖДАЯ деталь линейки (манифест тем же загрузчиком, что игра; контур — тот же, что
        /// передаёт пунктиру PieceView) при живом тюнинге даёт путь без разворотов и самопересечений и вне детали.
        /// </summary>
        [Test]
        public void EveryManifestPiece_LiveTuning_CleanPath()
        {
            var source = new JsonLevelSource(new FileStore(), new LevelDataValidator());
            Assert.That(source.Count, Is.GreaterThan(0), "манифест пуст или не разобран");

            var failures = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>();
            var checkedPieces = 0;
            for (var i = 0; i < source.Count; i++)
            {
                var level = source.Load(i);
                foreach (var placement in level.Pieces)
                {
                    if (!seen.Add(level.LevelId + "/" + placement.Type.Kind))
                        continue;
                    var shape = placement.Type.Shape;
                    var contour = shape.ConvexParts != null ? shape.Vertices : ShapeVisualOutline.Build(shape);
                    var path = IdleDashOutline.BuildPath(contour, LiveOffset, LiveCorner, LiveHalf);
                    checkedPieces++;

                    var name = $"{level.LevelId}/{placement.Type.Kind}";
                    if (path.Length < 3)
                        failures.Add($"{name}: путь пуст");
                    else if (MaxTurnDegrees(path, out var at) >= 30f)
                        failures.Add($"{name}: разворот у {at}");
                    else if (FindSelfCrossing(path) is { } crossing)
                        failures.Add($"{name}: самопересечение {crossing}");
                    else
                        foreach (var p in path)
                            if (InsidePolygon(p, contour))
                            {
                                failures.Add($"{name}: точка {p} внутри детали");
                                break;
                            }
                }
            }

            Assert.That(checkedPieces, Is.GreaterThan(100), "свип прошёл не по линейке");
            Assert.That(failures, Is.Empty, $"{failures.Count} из {checkedPieces}:\n" + string.Join("\n", failures));
        }

        private sealed class FileStore : ILevelStore
        {
            public string ReadText(string name)
            {
                var path = System.IO.Path.Combine("Assets/StreamingAssets", name);
                return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
            }
        }

        [Test]
        public void WideCutout_PathGoesIn_NarrowCutout_PathBridgesTheMouth()
        {
            var wide = IdleDashOutline.BuildPath(WideU, LiveOffset, LiveCorner, LiveHalf);
            var enters = false;
            foreach (var p in wide)
                if (p.x > 0.35f && p.x < 0.65f && p.y < 0.5f)
                    enters = true;
            Assert.That(enters, Is.True, "в широкий вырез пунктир обязан зайти — его форма часть силуэта");

            var narrow = IdleDashOutline.BuildPath(NarrowU, LiveOffset, LiveCorner, LiveHalf);
            foreach (var p in narrow)
                Assert.That(p.x > 0.45f && p.x < 0.55f && p.y < 1f, Is.False,
                    $"точка {p} в узком вырезе: штрихи двух стенок легли бы друг на друга");
        }

        [Test]
        public void ConcaveShapes_RibbonHasNoSpikes()
        {
            // Шип на кадре — вершина ленты, вынесенная митрой на развороте (1/0.3 полуширины). На чистом пути каждая
            // вершина ленты стоит от своей точки пути не дальше полуширины / cos(половины поворота).
            foreach (var shape in new[] { WideU, ToasterBody })
            {
                var path = IdleDashOutline.BuildPath(shape, LiveOffset, LiveCorner, LiveHalf);
                var mesh = new Mesh();
                try
                {
                    IdleDashOutline.BuildRibbon(mesh, path, LiveHalf);
                    var vertices = mesh.vertices;
                    for (var i = 0; i < vertices.Length; i++)
                    {
                        var p = path[(i / 2) % path.Length];
                        var reach = Vector2.Distance(new Vector2(vertices[i].x, vertices[i].y), p);
                        Assert.That(reach, Is.LessThan(LiveHalf * 1.1f), $"вершина ленты {i} вынесена на {reach}");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        [Test]
        public void OuterLoop_CutsTheSwallowtail_AtTheCrossing()
        {
            // Верх L-образного пути бежит влево за точку (2,2), разворачивается и поднимается вертикалью x=2 —
            // форма смещённой вогнутой дуги радиусом меньше отступа. Внешний контур срезает петлю в точке пересечения.
            var raw = new[]
            {
                new Vec2(0f, 0f), new Vec2(4f, 0f), new Vec2(4f, 2f), new Vec2(1f, 2f),
                new Vec2(2f, 1.5f), new Vec2(2f, 4f), new Vec2(0f, 4f),
            };
            var outer = Geometry.OuterLoop(raw);
            var expected = new[]
            {
                new Vec2(0f, 0f), new Vec2(4f, 0f), new Vec2(4f, 2f), new Vec2(2f, 2f), new Vec2(2f, 4f), new Vec2(0f, 4f),
            };
            Assert.That(outer.Length, Is.EqualTo(expected.Length));
            for (var i = 0; i < expected.Length; i++)
                Assert.That((outer[i] - expected[i]).Length, Is.LessThan(1e-5f), $"вершина {i}: {outer[i].X}, {outer[i].Y}");

            Assert.That(Geometry.OuterLoop(expected), Is.SameAs(expected), "без пересечений контур возвращается как есть");
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

        private static float MaxTurnDegrees(Vector2[] path, out string at)
        {
            var max = 0f;
            at = "";
            var n = path.Length;
            for (var i = 0; i < n; i++)
            {
                var a = path[(i + 1) % n] - path[i];
                var b = path[(i + 2) % n] - path[(i + 1) % n];
                var turn = Vector2.Angle(a, b);
                if (turn <= max)
                    continue;
                max = turn;
                at = $"{path[i].x:F4},{path[i].y:F4} → {path[(i + 1) % n].x:F4},{path[(i + 1) % n].y:F4} → "
                     + $"{path[(i + 2) % n].x:F4},{path[(i + 2) % n].y:F4}";
            }
            return max;
        }

        private static string FindSelfCrossing(Vector2[] path)
        {
            var n = path.Length;
            for (var i = 0; i < n; i++)
            for (var j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1)
                    continue;
                if (SegmentsCross(path[i], path[(i + 1) % n], path[j], path[(j + 1) % n]))
                    return $"отрезки {i} и {j}: {path[i]}→{path[(i + 1) % n]} × {path[j]}→{path[(j + 1) % n]}";
            }
            return null;
        }

        private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float Side(Vector2 p, Vector2 q, Vector2 r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
            return Side(a, b, c) * Side(a, b, d) < 0f && Side(c, d, a) * Side(c, d, b) < 0f;
        }

        /// <summary>Точка ближе <paramref name="radius"/> к выпуклой вершине CCW-контура.</summary>
        private static bool NearConvexVertex(Vector2 p, Vec2[] poly, float radius)
        {
            var n = poly.Length;
            for (var i = 0; i < n; i++)
            {
                Vector2 prev = new Vector2(poly[(i - 1 + n) % n].X, poly[(i - 1 + n) % n].Y);
                Vector2 cur = new Vector2(poly[i].X, poly[i].Y);
                Vector2 next = new Vector2(poly[(i + 1) % n].X, poly[(i + 1) % n].Y);
                var a = cur - prev;
                var b = next - cur;
                if (a.x * b.y - a.y * b.x > 0f && Vector2.Distance(p, cur) < radius)
                    return true;
            }
            return false;
        }

        private static bool InsidePolygon(Vector2 p, Vec2[] poly)
        {
            var inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].Y > p.y) != (poly[j].Y > p.y)
                    && p.x < (poly[j].X - poly[i].X) * (p.y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    inside = !inside;
            return inside;
        }

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
