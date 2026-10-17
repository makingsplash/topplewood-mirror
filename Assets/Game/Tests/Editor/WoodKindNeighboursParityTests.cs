using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gameplay.Core;
using Gameplay.View;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-599: граф соседства по видам (<see cref="WoodKindNeighbours.Build"/>: SAT по <c>Shape.Vertices</c> в позе
    /// слота, ребро при зазоре ≤ <see cref="WoodKindNeighbours.TouchEpsilon"/>) живёт в C#, а аудит перекраски считает
    /// его Python-тула <c>.studio/tools/palette-audit.py</c> (<c>--neighbours</c>, <c>--suggest</c>, <c>--allow-dup</c>).
    /// Зеркало без замка не принимается — этот файл и есть замок, с двух сторон, как у снапа
    /// (<see cref="WoodPaletteSnapParityTests"/>):
    /// <list type="bullet">
    /// <item><see cref="Cases_MatchRuntimeGraph"/> держит рёбра фикстуры <c>WoodKindNeighboursCases.json</c> на рантайме;
    /// та же фикстура — вход <c>palette-audit.py --selftest</c>. В ней граница эпсилона с обеих сторон (0.05 / 0.07),
    /// поворот слота, который меняет ответ, вогнутый контур (деталь в кармане L — сосед по SAT, хотя физически не
    /// касается) и окружность с дефолтными сегментами DTO.</item>
    /// <item><see cref="Campaign_Graph_Oracle"/> строит граф ВСЕЙ кампании и пишет оракул <c>Logs/kind-neighbours.json</c>;
    /// <c>palette-audit.py --oracle</c> сверяет с ним свой граф целиком.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public sealed class WoodKindNeighboursParityTests
    {
        private const string CasesPath = "Assets/Game/Tests/Editor/WoodKindNeighboursCases.json";
        private const string StreamingAssetsDir = "Assets/StreamingAssets";
        private const string OraclePath = "Logs/kind-neighbours.json";

        private sealed class Fixture
        {
            [JsonProperty("levels")] public List<LevelDataDto> Levels { get; set; } = new();
            [JsonProperty("graphs")] public List<GraphCase> Graphs { get; set; } = new();
        }

        private sealed class GraphCase
        {
            [JsonProperty("levelId")] public string LevelId { get; set; }
            [JsonProperty("edges")] public List<List<string>> Edges { get; set; } = new();
        }

        private sealed class GraphOracle
        {
            [JsonProperty("graphs")] public List<GraphRow> Graphs { get; set; } = new();
        }

        private sealed class GraphRow
        {
            [JsonProperty("number")] public int Number { get; set; }
            [JsonProperty("levelId")] public string LevelId { get; set; }
            [JsonProperty("edges")] public List<List<string>> Edges { get; set; } = new();
        }

        [Test]
        public void Cases_MatchRuntimeGraph()
        {
            Assert.That(File.Exists(CasesPath), Is.True, $"нет фикстуры {CasesPath}");
            var fixture = JsonConvert.DeserializeObject<Fixture>(File.ReadAllText(CasesPath));
            Assert.That(fixture.Graphs, Is.Not.Empty, "фикстура без графов");

            var levels = fixture.Levels.ToDictionary(l => l.LevelId, l => l.ToDomain());
            var mismatches = new List<string>();
            foreach (var c in fixture.Graphs)
            {
                Assert.That(levels.ContainsKey(c.LevelId), Is.True, $"graphs ссылается на неизвестный уровень {c.LevelId}");
                var actual = Edges(levels[c.LevelId]);
                var expected = c.Edges.Select(e => Key(e[0], e[1])).OrderBy(e => e, StringComparer.Ordinal).ToList();
                if (!actual.SequenceEqual(expected))
                    mismatches.Add($"{c.LevelId}: рантайм [{string.Join(", ", actual)}], фикстура [{string.Join(", ", expected)}]");
            }

            Assert.That(mismatches, Is.Empty, "фикстура разошлась с рантаймом:\n" + string.Join("\n", mismatches));
        }

        [Test]
        public void Campaign_Graph_Oracle()
        {
            var source = new JsonLevelSource(new FileStore(), new LevelDataValidator());
            Assert.That(source.Count, Is.GreaterThan(0), "манифест пуст или не разобран");

            var oracle = new GraphOracle();
            var edgeCount = 0;
            for (var i = 0; i < source.Count; i++)
            {
                var level = source.Load(i);
                var edges = Edges(level);
                edgeCount += edges.Count;
                oracle.Graphs.Add(new GraphRow
                {
                    Number = i + 1,
                    LevelId = level.LevelId,
                    Edges = edges.Select(e => e.Split('|').ToList()).ToList(),
                });
            }

            Directory.CreateDirectory("Logs");
            File.WriteAllText(OraclePath, JsonConvert.SerializeObject(oracle, Formatting.Indented));
            TestContext.Out.WriteLine($"оракул графа: уровней {source.Count}, рёбер {edgeCount} → {OraclePath}");

            Assert.That(edgeCount, Is.GreaterThan(0), "ни одного ребра на всей кампании — граф или загрузка сломаны");
        }

        /// <summary>Рёбра графа уровня как отсортированные ключи <c>a|b</c> (a &lt; b ординально).</summary>
        private static List<string> Edges(LevelData level)
        {
            var graph = WoodKindNeighbours.Build(level, WoodKindNeighbours.ShapesByKind(level));
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var pair in graph)
                foreach (var other in pair.Value)
                    keys.Add(Key(pair.Key, other));
            return keys.ToList();
        }

        private static string Key(string a, string b)
            => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;

        private sealed class FileStore : ILevelStore
        {
            public string ReadText(string name)
            {
                var path = Path.Combine(StreamingAssetsDir, name);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
        }
    }
}
