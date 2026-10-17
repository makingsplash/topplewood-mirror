using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gameplay.Core;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-602: линтер подвеса <c>.studio/tools/hang-lint.py</c> меряет деталь-полимино по контуру, который он сам
    /// выводит из таблицы клеток (<c>--shapes</c>), а не по <see cref="Shape.Vertices"/>. Зеркало без замка не
    /// принимается — этот файл и есть замок, с двух сторон, как у графа соседства
    /// (<see cref="WoodKindNeighboursParityTests"/>):
    /// <list type="bullet">
    /// <item>фикстура <c>.studio/tools/tests/hang-lint-polyomino-runtime.json</c> = контуры и клетки рантайма — здесь;
    /// контур, выведенный тулой из клеток, = контуры фикстуры — <c>hang-lint.py --selftest</c>;</item>
    /// <item>таблица <c>.studio/tools/shapes/robo-factory-polyomino.json</c>, которую линтер получает на вход, =
    /// клетки рантайма (<see cref="Shape.ConvexParts"/>) — здесь же.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public sealed class PolyominoContourParityTests
    {
        private const string FixturePath = ".studio/tools/tests/hang-lint-polyomino-runtime.json";
        private const string ShapesTablePath = ".studio/tools/shapes/robo-factory-polyomino.json";
        private const float Tolerance = 1e-5f;

        private sealed class Fixture
        {
            [JsonProperty("cell")] public float Cell { get; set; }
            [JsonProperty("shapes")] public Dictionary<string, Case> Shapes { get; set; } = new();
        }

        private sealed class Case
        {
            [JsonProperty("cells")] public List<int[]> Cells { get; set; } = new();
            [JsonProperty("vertices")] public List<float[]> Vertices { get; set; } = new();
        }

        private static Fixture Load()
        {
            Assert.That(File.Exists(FixturePath), Is.True, $"нет фикстуры {FixturePath}");
            var fixture = JsonConvert.DeserializeObject<Fixture>(File.ReadAllText(FixturePath));
            Assert.That(fixture.Shapes, Is.Not.Empty, "фикстура без форм");
            return fixture;
        }

        private static Shape Runtime(string primitive, float cell)
            => new ShapeDto { Primitive = primitive, Cell = cell }.ToDomain();

        [Test]
        public void Fixture_CoversEveryPolyominoPrimitive()
        {
            var fixture = Load();
            var runtime = Enum.GetNames(typeof(ShapePrimitive))
                .Where(n => n.EndsWith("Tetromino", StringComparison.Ordinal) || n.EndsWith("Pentomino", StringComparison.Ordinal))
                .ToList();
            Assert.That(fixture.Shapes.Count, Is.EqualTo(runtime.Count),
                $"полимино в рантайме: {string.Join(", ", runtime)}; в фикстуре: {string.Join(", ", fixture.Shapes.Keys)}");
        }

        [Test]
        public void Vertices_MatchRuntimeContour()
        {
            var fixture = Load();
            var mismatches = new List<string>();
            foreach (var (primitive, c) in fixture.Shapes)
            {
                var actual = Runtime(primitive, fixture.Cell).Vertices;
                var same = actual.Length == c.Vertices.Count && actual.Select((v, i) =>
                    Math.Abs(v.X - c.Vertices[i][0]) <= Tolerance && Math.Abs(v.Y - c.Vertices[i][1]) <= Tolerance).All(b => b);
                if (!same)
                    mismatches.Add($"{primitive}: рантайм [{string.Join(" ", actual.Select(v => $"({v.X},{v.Y})"))}]");
            }

            Assert.That(mismatches, Is.Empty, "фикстура разошлась с Shape.Vertices:\n" + string.Join("\n", mismatches));
        }

        [Test]
        public void Cells_MatchRuntimePartsAndShapesTable()
        {
            var fixture = Load();
            Assert.That(File.Exists(ShapesTablePath), Is.True, $"нет таблицы {ShapesTablePath}");
            var table = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(ShapesTablePath))
                .Where(kv => !kv.Key.StartsWith("_", StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => JsonConvert.DeserializeObject<List<int[]>>(kv.Value.ToString()));

            var mismatches = new List<string>();
            foreach (var (primitive, c) in fixture.Shapes)
            {
                var cells = c.Cells.Select(Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
                if (!table.TryGetValue(primitive, out var tableCells))
                    mismatches.Add($"{primitive}: нет в таблице {ShapesTablePath}");
                else if (!tableCells.Select(Key).OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(cells))
                    mismatches.Add($"{primitive}: таблица [{string.Join(" ", tableCells.Select(Key))}] ≠ фикстура");

                // Часть рантайма i — клетка, её первый угол = (клетка − центр bbox) · cell
                var cx = (c.Cells.Min(p => p[0]) + c.Cells.Max(p => p[0]) + 1) * 0.5f;
                var cy = (c.Cells.Min(p => p[1]) + c.Cells.Max(p => p[1]) + 1) * 0.5f;
                var parts = Runtime(primitive, fixture.Cell).ConvexParts;
                var actual = parts.Select(part => Key(new[]
                {
                    (int)Math.Round(part[0].X / fixture.Cell + cx), (int)Math.Round(part[0].Y / fixture.Cell + cy),
                })).OrderBy(k => k, StringComparer.Ordinal).ToList();
                if (!actual.SequenceEqual(cells))
                    mismatches.Add($"{primitive}: ConvexParts [{string.Join(" ", actual)}] ≠ фикстура [{string.Join(" ", cells)}]");
            }

            Assert.That(mismatches, Is.Empty, "клетки разошлись с рантаймом:\n" + string.Join("\n", mismatches));
        }

        private static string Key(int[] cell) => $"{cell[0]},{cell[1]}";
    }
}
