using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gameplay.Core;
using Gameplay.View;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-591: правило «эффективного» цвета детали живёт в C# (<see cref="WoodTuning.ResolveLevelColor(Color,string)"/>:
    /// схема Иттена → снап по тону при <c>PaletteToGame</c> → авторский), а аудит кампании считает его Python-тула
    /// <c>.studio/tools/palette-audit.py</c>. Двух реализаций без замка не бывает — этот файл и есть замок, с двух сторон:
    /// <list type="bullet">
    /// <item><see cref="SnapCases_MatchRuntimeResolver"/> держит фикстуру <c>WoodPaletteSnapCases.json</c> на рантайме.
    /// Та же фикстура — вход <c>palette-audit.py --selftest</c>: разойдись Python с C# хоть на одном случае, упадёт одна
    /// из сторон. Фикстура несёт СВОЮ палитру, поэтому смена <c>wood_tuning.json:Palette</c> владельцем её не ломает.</item>
    /// <item><see cref="Campaign_EffectiveColors_Oracle"/> прогоняет ВСЮ кампанию живым тюнингом и пишет оракул
    /// <c>Logs/effective-piece-colors.json</c> — эффективный hex каждого вида каждого уровня, посчитанный рантаймом.
    /// <c>palette-audit.py --oracle</c> сверяет с ним свою таблицу целиком, включая ветку схем.</item>
    /// </list>
    /// Первым на поломке правила падает <see cref="SnapCases_MatchRuntimeResolver"/>: в фикстуре есть случаи, где
    /// ближайший по ТОНУ тон палитры не ближайший по RGB, почти-серое (<c>sat &lt; 0.08</c>) и тон через ноль круга.
    /// </summary>
    [TestFixture]
    public sealed class WoodPaletteSnapParityTests
    {
        private const string CasesPath = "Assets/Game/Tests/Editor/WoodPaletteSnapCases.json";
        private const string StreamingAssetsDir = "Assets/StreamingAssets";
        private const string OraclePath = "Logs/effective-piece-colors.json";

        [Serializable]
        private sealed class SnapCases
        {
            public List<string> palette = new List<string>();
            public List<SnapCase> cases = new List<SnapCase>();
        }

        [Serializable]
        private sealed class SnapCase
        {
            public string input;
            public string expected;
            public string note;
        }

        [Serializable]
        private sealed class Oracle
        {
            public bool paletteToGame;
            public bool ittenPalettes;
            public List<OracleRow> rows = new List<OracleRow>();
            public List<string> snapInputs = new List<string>();
        }

        [Serializable]
        private sealed class OracleRow
        {
            public int number;
            public string levelId;
            public string kind;
            public string authored;
            public string effective;
            public string source;
        }

        [Test]
        public void SnapCases_MatchRuntimeResolver()
        {
            Assert.That(File.Exists(CasesPath), Is.True, $"нет фикстуры {CasesPath}");
            var fixture = JsonUtility.FromJson<SnapCases>(File.ReadAllText(CasesPath));
            Assert.That(fixture.palette, Is.Not.Empty, "фикстура без палитры");
            Assert.That(fixture.cases, Is.Not.Empty, "фикстура без случаев");

            var tuning = new WoodTuning { PaletteToGame = true, IttenPalettes = false, Palette = fixture.palette };
            var mismatches = new List<string>();
            foreach (var c in fixture.cases)
            {
                Assert.That(ColorUtility.TryParseHtmlString(c.input, out var input), Is.True, $"не цвет: {c.input}");
                var actual = Hex(tuning.ResolveLevelColor(input));
                if (!string.Equals(actual, c.expected, StringComparison.OrdinalIgnoreCase))
                    mismatches.Add($"{c.input} → рантайм {actual}, фикстура {c.expected} ({c.note})");
            }

            Assert.That(mismatches, Is.Empty, "фикстура разошлась с рантаймом:\n" + string.Join("\n", mismatches));
        }

        [Test]
        public void Campaign_EffectiveColors_Oracle()
        {
            var tuning = WoodTuning.Current;
            var source = new JsonLevelSource(new FileStore(), new LevelDataValidator());
            Assert.That(source.Count, Is.GreaterThan(0), "манифест пуст или не разобран");

            var oracle = new Oracle { paletteToGame = tuning.PaletteToGame, ittenPalettes = tuning.IttenPalettes };
            var snapInputs = new SortedSet<string>(StringComparer.Ordinal);
            var unparsed = new List<string>();
            try
            {
                for (var i = 0; i < source.Count; i++)
                {
                    var level = source.Load(i);
                    WoodTuning.SetCurrentLevel(level);
                    var seen = new HashSet<string>();
                    foreach (var placement in level.Pieces)
                    {
                        var type = placement.Type;
                        if (!seen.Add(type.Kind))
                            continue;

                        var row = new OracleRow
                        {
                            number = i + 1, levelId = level.LevelId, kind = type.Kind, authored = type.Color,
                        };
                        if (string.IsNullOrEmpty(type.Color) || !ColorUtility.TryParseHtmlString(type.Color, out var authored))
                        {
                            // Вью нарисует свой дефолт; тула такие виды тоже не считает — помечает.
                            unparsed.Add($"{level.LevelId}.{type.Kind} = '{type.Color}'");
                            row.source = "no-color";
                            oracle.rows.Add(row);
                            continue;
                        }

                        var effective = tuning.ResolveLevelColor(authored, type.Kind);
                        row.effective = Hex(effective);
                        if (tuning.OverridesPieceColor(level.LevelId, type.Kind, out _))
                            row.source = "scheme";
                        else if (tuning.PaletteToGame)
                        {
                            row.source = "snap";
                            snapInputs.Add(Hex(authored));
                        }
                        else
                            row.source = "authored";
                        oracle.rows.Add(row);
                    }
                }
            }
            finally
            {
                WoodTuning.SetCurrentLevel(null);
            }

            oracle.snapInputs = snapInputs.ToList();
            Directory.CreateDirectory("Logs");
            File.WriteAllText(OraclePath, JsonUtility.ToJson(oracle, true));
            TestContext.Out.WriteLine($"оракул: {oracle.rows.Count} видов, уровней {source.Count}, " +
                                      $"входов снапа {snapInputs.Count} → {OraclePath}");

            Assert.That(oracle.rows.Count, Is.GreaterThan(0), "ни одного вида детали на всей кампании — загрузка сломана");
            Assert.That(unparsed, Is.Empty, "виды без разбираемого цвета:\n" + string.Join("\n", unparsed));
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

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
