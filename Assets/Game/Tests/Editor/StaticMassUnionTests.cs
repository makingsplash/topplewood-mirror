using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Gameplay.Core;
using Gameplay.View;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-567: автослияние видимой статики в визуальную массу (<see cref="StaticMassUnion"/>) и поле по отрезкам
    /// (<see cref="SilhouetteField.EvaluateSegments"/>). Синтетика доказывает правило (ребро — да, угол — нет, щель и
    /// другой цвет — нет, лента — нет), золото держит реальный манифест, инвентаризация печатает таблицу по ВСЕМ уровням
    /// тем же кодом, что сливает в игре, — она и есть ответ на «считать по геометрии, а не на глаз».
    /// Первым падает <see cref="Field_OnFormerSharedEdge_MeasuresToOuterEdge"/>: на любой реализации, которая шов не
    /// убрала, расстояние на бывшем общем ребре равно нулю.
    /// </summary>
    [TestFixture]
    public sealed class StaticMassUnionTests
    {
        private const string StreamingAssetsDir = "Assets/StreamingAssets";
        private const string Brown = "#B8926B";

        // ---------------------------------------------------------------- группировка, синтетика

        [Test]
        public void TwoSquaresSharingAnEdge_AreOneGroup_AndTheSharedEdgeIsBuried()
        {
            var a = Rect(0f, 0f, 1f, 1f);
            var b = Rect(1f, 0f, 2f, 1f);
            Assert.That(Groups(a, b), Is.EqualTo(new[] { new[] { 0, 1 } }));

            var boundary = StaticMassUnion.Boundary(new[] { a, b });
            Assert.That(boundary.Length, Is.EqualTo(6), "по три наружных ребра у каждого, общее выброшено с обеих сторон");
            Assert.That(boundary.Any(s => Mathf.Approximately(s.A.X, 1f) && Mathf.Approximately(s.B.X, 1f)), Is.False,
                "отрезок x = 1 (общее ребро) в границу не входит");
        }

        [Test]
        public void CornerToCornerTouch_IsNotAContact()
        {
            Assert.That(Groups(Rect(0f, 0f, 1f, 1f), Rect(1f, 1f, 2f, 2f)), Is.Empty);
        }

        [Test]
        public void SmallGap_IsAnAuthorSeam()
        {
            Assert.That(Groups(Rect(0f, 0f, 1f, 1f), Rect(1.01f, 0f, 2f, 1f)), Is.Empty);
        }

        [Test]
        public void Overlap_IsOneGroup_AndEdgesAreCutAtTheEntryPoint()
        {
            var a = Rect(0f, 0f, 2f, 1f);
            var b = Rect(1f, 0.5f, 3f, 2f);
            Assert.That(Groups(a, b), Is.EqualTo(new[] { new[] { 0, 1 } }));

            var boundary = StaticMassUnion.Boundary(new[] { a, b });
            // Верх A (y = 1) уходит внутрь B на x ≥ 1: в границе остаётся только участок 0…1.
            var topOfA = boundary.Where(s => Mathf.Approximately(s.A.Y, 1f) && Mathf.Approximately(s.B.Y, 1f)).ToArray();
            Assert.That(topOfA.Length, Is.EqualTo(1));
            Assert.That(Mathf.Max(topOfA[0].A.X, topOfA[0].B.X), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(StaticMassUnion.InsideAny(new[] { a, b }, 1.5f, 0.75f), Is.True);
        }

        [Test]
        public void FrameOfFourPlanks_IsOneGroup_WithAHole()
        {
            var planks = new[]
            {
                Rect(0f, 0f, 3f, 0.5f), Rect(0f, 2.5f, 3f, 3f), Rect(0f, 0.5f, 0.5f, 2.5f), Rect(2.5f, 0.5f, 3f, 2.5f),
            };
            Assert.That(Groups(planks), Is.EqualTo(new[] { new[] { 0, 1, 2, 3 } }));

            var boundary = StaticMassUnion.Boundary(planks);
            Assert.That(boundary.Any(s => Mathf.Approximately(s.A.Y, 0.5f) && Mathf.Approximately(s.B.Y, 0.5f)
                                          && s.Normal.Y > 0.5f), Is.True, "кромка дырки — отрезки с нормалью внутрь рамки");
            Assert.That(StaticMassUnion.InsideAny(planks, 1.5f, 1.5f), Is.False, "центр рамки — дырка, не масса");
        }

        [Test]
        public void DifferentColour_NeverMerges()
        {
            var statics = new[] { Poly(Rect(0f, 0f, 1f, 1f)), Poly(Rect(1f, 0f, 2f, 1f)) };
            var visuals = new[] { new StaticVisual(true, Brown), new StaticVisual(true, "#123456") };
            Assert.That(StaticMassUnion.Group(statics, visuals), Is.Empty);
        }

        [Test]
        public void ColourKey_IgnoresCaseAndWhitespace()
        {
            var statics = new[] { Poly(Rect(0f, 0f, 1f, 1f)), Poly(Rect(1f, 0f, 2f, 1f)) };
            var visuals = new[] { new StaticVisual(true, "#b8926b "), new StaticVisual(true, Brown) };
            Assert.That(StaticMassUnion.Group(statics, visuals).Length, Is.EqualTo(1));
        }

        [Test]
        public void SurfaceAndInvisible_AreExcluded()
        {
            var statics = new[]
            {
                Poly(Rect(0f, 0f, 1f, 1f)),
                new Polygon(new[] { new Vec2(1f, 0f), new Vec2(2f, 0f) }, surface: new StaticSurface(0.2f)),
                Poly(Rect(0f, 1f, 1f, 2f)),
            };
            var surfaceAndHidden = new[]
            {
                new StaticVisual(true, Brown), new StaticVisual(true, Brown), new StaticVisual(false, Brown),
            };
            Assert.That(StaticMassUnion.Group(statics, surfaceAndHidden), Is.Empty);
        }

        [Test]
        public void Grouping_IsDeterministic()
        {
            var blocks = new[] { Rect(0f, 0f, 1f, 1f), Rect(5f, 5f, 6f, 6f), Rect(1f, 0f, 2f, 1f), Rect(5f, 6f, 6f, 7f) };
            var first = Groups(blocks);
            var second = Groups(blocks);
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.EqualTo(new[] { new[] { 0, 2 }, new[] { 1, 3 } }),
                "группы — по минимальному индексу, члены — по возрастанию");

            var b1 = StaticMassUnion.Boundary(blocks.Take(1).Concat(blocks.Skip(2).Take(1)).ToArray());
            var b2 = StaticMassUnion.Boundary(blocks.Take(1).Concat(blocks.Skip(2).Take(1)).ToArray());
            Assert.That(b1.Select(s => (s.A, s.B)), Is.EqualTo(b2.Select(s => (s.A, s.B))));
        }

        // ---------------------------------------------------------------- поле по отрезкам

        [Test]
        public void Field_OnFormerSharedEdge_MeasuresToOuterEdge()
        {
            // Два квадрата 1×1 вплотную: точка (1, 0.5) лежит на бывшем общем ребре. До наружной кромки — 0.5.
            var members = new[] { Rect(0f, 0f, 1f, 1f), Rect(1f, 0f, 2f, 1f) };
            var boundary = StaticMassUnion.Boundary(members);
            SilhouetteField.EvaluateSegments(boundary, members, 1f, 0.5f, out var d, out _, out var ny);
            Assert.That(d, Is.EqualTo(0.5f).Within(1e-4f), "на бывшем стыке поле обязано мерить до наружной кромки, а не 0");
            Assert.That(Mathf.Abs(ny), Is.EqualTo(1f).Within(1e-4f), "нормаль — верх или низ, а не бывшее общее ребро");
        }

        [Test]
        public void Field_IsSignedAndNormalPointsOutward()
        {
            var members = new[] { Rect(0f, 0f, 1f, 1f), Rect(1f, 0f, 2f, 1f) };
            var boundary = StaticMassUnion.Boundary(members);

            SilhouetteField.EvaluateSegments(boundary, members, 1.5f, 0.9f, out var dIn, out var nxIn, out var nyIn);
            Assert.That(dIn, Is.EqualTo(0.1f).Within(1e-4f));
            Assert.That(nyIn, Is.EqualTo(1f).Within(1e-4f));

            SilhouetteField.EvaluateSegments(boundary, members, 1.5f, 1.2f, out var dOut, out _, out var nyOut);
            Assert.That(dOut, Is.EqualTo(-0.2f).Within(1e-4f), "снаружи — отрицательное расстояние (тень массы вырезается знаком)");
            Assert.That(nyOut, Is.EqualTo(1f).Within(1e-4f), "снаружи нормаль та же — иначе билинейная выборка её перевернёт");
        }

        [Test]
        public void Field_InHole_IsOutside()
        {
            var planks = new[]
            {
                Rect(0f, 0f, 3f, 0.5f), Rect(0f, 2.5f, 3f, 3f), Rect(0f, 0.5f, 0.5f, 2.5f), Rect(2.5f, 0.5f, 3f, 2.5f),
            };
            var boundary = StaticMassUnion.Boundary(planks);
            SilhouetteField.EvaluateSegments(boundary, planks, 1.5f, 1.5f, out var d, out _, out _);
            Assert.That(d, Is.EqualTo(-1f).Within(1e-4f), "центр рамки — дырка: снаружи, 1.0 до ближайшей кромки");
        }

        [Test]
        public void SingleBlock_FieldBySegments_MatchesRingField()
        {
            // Группа из одного блока — тот же контур: поле по отрезкам обязано совпасть с полем по кольцу.
            var square = Rect(0f, 0f, 2f, 1f);
            var boundary = StaticMassUnion.Boundary(new[] { square });
            foreach (var (x, y) in new[] { (0.3f, 0.2f), (1.9f, 0.9f), (2.4f, 0.5f), (-0.3f, -0.4f) })
            {
                SilhouetteField.Evaluate(square, x, y, out var ringD, out var ringNx, out var ringNy);
                SilhouetteField.EvaluateSegments(boundary, new[] { square }, x, y, out var segD, out var segNx, out var segNy);
                Assert.That(segD, Is.EqualTo(ringD).Within(1e-5f), $"({x}, {y})");
                Assert.That(segNx, Is.EqualTo(ringNx).Within(1e-5f), $"({x}, {y})");
                Assert.That(segNy, Is.EqualTo(ringNy).Within(1e-5f), $"({x}, {y})");
            }
        }

        [Test]
        public void SegmentField_NarrowBand_MatchesFullEvaluationInBand()
        {
            // «Г» из пола и стенки в большом пустом габарите плюс толстый блок 4×4 (его середина глубже полосы) и
            // наклонный член. В полосе у кромки поле обязано совпасть с полным счётом побайтно; глубже полосы — знак
            // тот же, а модуль ровно BandDistance (всё насыщено внутри, тень 0 снаружи).
            var members = new[]
            {
                Rect(-7f, -0.6f, 7f, 0f), Rect(-7f, 0f, -6.6f, 9f), Rect(1f, 0f, 5f, 4f),
                new[] { new Vec2(-3f, 0f), new Vec2(-1f, 0f), new Vec2(-2.2f, 2.5f) },
            };
            var boundary = StaticMassUnion.Boundary(members);
            var data = SilhouetteField.BakeSegmentsData(boundary, members, StaticGeometryView.MassTexelsPerUnit, out var layout);
            Assert.That(data, Is.Not.Null);

            var exact = 0;
            var far = 0;
            for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var px = layout.OriginX + (x + 0.5f) * layout.Texel;
                var py = layout.OriginY + (y + 0.5f) * layout.Texel;
                SilhouetteField.EvaluateSegments(boundary, members, px, py, out var d, out var nx, out var ny);
                var i = (y * layout.Width + x) * 4;
                var baked = UnityEngine.Mathf.HalfToFloat(data[i]);
                if (System.Math.Abs(d) <= SilhouetteField.BandDistance)
                {
                    exact++;
                    Assert.That(data[i], Is.EqualTo(UnityEngine.Mathf.FloatToHalf(d)), $"тексел ({x},{y}) в полосе");
                    Assert.That(data[i + 1], Is.EqualTo(UnityEngine.Mathf.FloatToHalf(nx)), $"нормаль x ({x},{y})");
                    Assert.That(data[i + 2], Is.EqualTo(UnityEngine.Mathf.FloatToHalf(ny)), $"нормаль y ({x},{y})");
                }
                else
                {
                    far++;
                    var expected = d > 0f ? SilhouetteField.BandDistance : -SilhouetteField.BandDistance;
                    Assert.That(baked, Is.EqualTo(expected).Within(1e-3f), $"тексел ({x},{y}) глубже полосы");
                }
            }
            Assert.That(exact, Is.GreaterThan(0));
            Assert.That(far, Is.GreaterThan(0), "фикстура обязана иметь тексели глубже полосы — иначе проверена одна ветка");
        }

        // ---------------------------------------------------------------- реальный манифест

        [Test]
        public void Manifest_VideoLauncher_LeftNodeIsOneMass()
        {
            var level = LoadAll()["video_launcher"];
            var groups = StaticMassUnion.Group(level.StaticGeometry, level.StaticVisuals);
            Assert.That(groups, Is.EqualTo(new[] { new[] { 0, 1, 2, 3, 4, 6, 7, 8 } }),
                "левый узел, дно ямы (3) и правая тумба (4) — одна масса: низ стен опущен до y −3.3, 3 сидит на ребре; " +
                "планка снята, верхний упор 5 одиночка");

            var outlines = groups[0].Select(i => level.StaticGeometry[i].Outline).ToArray();
            Assert.That(StaticMassUnion.Boundary(outlines).Length, Is.GreaterThanOrEqualTo(8),
                "U-контур — не меньше 8 отрезков (коллинеарные куски одного ребра идут отдельно)");
        }

        [Test]
        public void Manifest_GoldenGroups()
        {
            var levels = LoadAll();
            Assert.That(Group(levels["golf_cup"]), Is.EqualTo(new[] { new[] { 0, 1 } }), "golf_cup: пол + бортик");
            Assert.That(Group(levels["crows_nest"]), Is.Empty, "crows_nest: щели ≥ 0.1, слияний нет");
            var petal = Group(levels["petal_gully"]);
            Assert.That(petal.Length, Is.EqualTo(1), "petal_gully: всё — одна масса");
            Assert.That(petal[0].Length, Is.EqualTo(6));
        }

        /// <summary>Таблица по ВСЕМ уровням — тем же кодом, что сливает в игре. Печатается в TestContext и в
        /// <c>Logs/task567-inventory.md</c>; имплементер переносит её в артефакт. Тест обязан провалиться на пустом
        /// манифесте (иначе «0 строк» прошло бы зелёным).</summary>
        [Test]
        public void Inventory_AllLevels()
        {
            var levels = LoadAll();
            Assert.That(levels.Count, Is.GreaterThan(0), "манифест пуст — инвентаризации нет");

            var sb = new StringBuilder();
            sb.AppendLine("| levelId | групп | члены | отрезков | габарит | текселей поля | МБ | запекание CPU, мс |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            var concaveSingles = new List<string>();
            var crossColour = new List<string>();
            var merged = 0;
            var biggest = new List<(string id, int texels, double ms, long bytes)>();

            foreach (var pair in levels)
            {
                var level = pair.Value;
                var groups = StaticMassUnion.Group(level.StaticGeometry, level.StaticVisuals);

                for (var i = 0; i < (level.StaticGeometry?.Length ?? 0); i++)
                {
                    var poly = level.StaticGeometry[i];
                    if (i < level.StaticVisuals.Count && StaticMassUnion.IsCandidate(poly, level.StaticVisuals[i])
                        && !Geometry.IsConvex(poly.Outline) && !groups.Any(g => g.Contains(i)))
                        concaveSingles.Add($"{pair.Key}[{i}]");
                }

                if (groups.Length == 0)
                    continue;
                merged++;

                for (var g = 0; g < groups.Length; g++)
                {
                    var outlines = groups[g].Select(i => level.StaticGeometry[i].Outline).ToArray();
                    var boundary = StaticMassUnion.Boundary(outlines);
                    var watch = Stopwatch.StartNew();
                    var data = SilhouetteField.BakeSegmentsData(boundary, outlines, StaticGeometryView.MassTexelsPerUnit, out var layout);
                    watch.Stop();
                    Assert.That(data, Is.Not.Null, $"{pair.Key}: поле группы {g} не посчиталось");

                    float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                    foreach (var v in outlines.SelectMany(o => o))
                    {
                        minX = Mathf.Min(minX, v.X); maxX = Mathf.Max(maxX, v.X);
                        minY = Mathf.Min(minY, v.Y); maxY = Mathf.Max(maxY, v.Y);
                    }

                    var texels = layout.Width * layout.Height;
                    biggest.Add((pair.Key, texels, watch.Elapsed.TotalMilliseconds, layout.Bytes));
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "| {0} | {1} | {2} | {3} | {4:0.##}×{5:0.##} | {6}×{7} = {8} | {9:0.00} | {10:0.0} |",
                        g == 0 ? pair.Key : "", g == 0 ? groups.Length.ToString(CultureInfo.InvariantCulture) : "",
                        "{" + string.Join(",", groups[g]) + "}", boundary.Length, maxX - minX, maxY - minY,
                        layout.Width, layout.Height, texels, layout.Bytes / (1024.0 * 1024.0), watch.Elapsed.TotalMilliseconds));
                }

                // Разноцветные массы, перекрывающиеся по площади, стоят на одной глубине и z-файтятся (TASK-352).
                for (var g = 0; g < groups.Length; g++)
                for (var h = g + 1; h < groups.Length; h++)
                {
                    var cg = level.StaticVisuals[groups[g][0]].Color;
                    var ch = level.StaticVisuals[groups[h][0]].Color;
                    if (string.Equals(cg?.Trim(), ch?.Trim(), System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (groups[g].Any(i => groups[h].Any(j =>
                            StaticMassUnion.InContact(level.StaticGeometry[i].Outline, level.StaticGeometry[j].Outline))))
                        crossColour.Add($"{pair.Key}: {{{string.Join(",", groups[g])}}} × {{{string.Join(",", groups[h])}}}");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Уровней всего: {levels.Count}; со слиянием: {merged}.");
            sb.AppendLine($"Одиночные вогнутые массы (получают только тень по полю): {(concaveSingles.Count == 0 ? "нет" : string.Join(", ", concaveSingles))}.");
            sb.AppendLine($"Разноцветные массы с перекрытием (TASK-352): {(crossColour.Count == 0 ? "0 пар" : string.Join("; ", crossColour))}.");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Плотность поля массы: {0} текселей на единицу. Три самых дорогих уровня (сумма по группам; CPU запекания " +
                "в EditMode, без загрузки в GPU):", StaticGeometryView.MassTexelsPerUnit));
            foreach (var lvl in biggest.GroupBy(b => b.id)
                         .Select(g => (id: g.Key, ms: g.Sum(b => b.ms), bytes: g.Sum(b => b.bytes)))
                         .OrderByDescending(l => l.ms).Take(3))
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "- {0}: {1:0.00} МБ, {2:0.0} мс",
                    lvl.id, lvl.bytes / (1024.0 * 1024.0), lvl.ms));
            var maxBytes = biggest.GroupBy(b => b.id).Max(g => g.Sum(b => b.bytes));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Максимум памяти полей на уровень: {0:0.00} МБ.",
                maxBytes / (1024.0 * 1024.0)));

            var table = sb.ToString();
            TestContext.Out.WriteLine(table);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Path.Combine("Logs", "task567-inventory.md"), table);
            Assert.That(merged, Is.GreaterThan(0), "ни одного слияния на всей кампании — правило или загрузка сломаны");
        }

        // ---------------------------------------------------------------- помощники

        private static int[][] Group(LevelData level) => StaticMassUnion.Group(level.StaticGeometry, level.StaticVisuals);

        private static int[][] Groups(params Vec2[][] blocks)
            => StaticMassUnion.Group(blocks.Select(Poly).ToArray(),
                blocks.Select(_ => new StaticVisual(true, Brown)).ToArray());

        private static Polygon Poly(Vec2[] outline) => new Polygon(outline);

        private static Vec2[] Rect(float x0, float y0, float x1, float y1)
            => new[] { new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1) };

        /// <summary>Тем же загрузчиком, что игра: <see cref="JsonLevelSource"/> над файлом StreamingAssets.</summary>
        private static Dictionary<string, LevelData> LoadAll()
        {
            var source = new JsonLevelSource(new FileStore(), new LevelDataValidator());
            Assert.That(source.Count, Is.GreaterThan(0), "манифест пуст или не разобран");
            var result = new Dictionary<string, LevelData>();
            for (var i = 0; i < source.Count; i++)
            {
                var level = source.Load(i);
                result[level.LevelId] = level;
            }
            return result;
        }

        private sealed class FileStore : ILevelStore
        {
            public string ReadText(string name)
            {
                var path = Path.Combine(StreamingAssetsDir, name);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
        }

        private static class Mathf
        {
            public static bool Approximately(float a, float b) => System.Math.Abs(a - b) < 1e-4f;
            public static float Max(float a, float b) => System.Math.Max(a, b);
            public static float Min(float a, float b) => System.Math.Min(a, b);
            public static float Abs(float a) => System.Math.Abs(a);
        }
    }
}
